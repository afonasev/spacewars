using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // The scheduler and ordered host work share one capture traversal and ledger.
    // Each queue carries the owned name even after its row is removed.
    internal sealed class NavigationHostRoot : NavOwnedStorage, INavPhysicalBody
    {
        private static readonly long LayoutBytes=Math.Max(256,NavStorageLayout.ObjectBytesBound(typeof(NavigationHostRoot)));
        internal readonly struct OperationDescriptor
        {
            internal readonly CertifiedRouteKernel.PumpStage Stage;
            internal readonly int Iteration;
            internal readonly bool Entered,ProgressIncrementConsumed;
            internal readonly NavNameReference Selector;
            internal OperationDescriptor(CertifiedRouteKernel.PumpStage stage,int iteration,bool entered,bool progressIncrementConsumed,NavNameReference selector)
            {
                if(!Enum.IsDefined(typeof(CertifiedRouteKernel.PumpStage),stage)||iteration<0||progressIncrementConsumed&&!entered)
                    throw new ArgumentException("Invalid host operation boundary.");
                Stage=stage;Iteration=iteration;Entered=entered;ProgressIncrementConsumed=progressIncrementConsumed;Selector=selector;
            }
        }
        internal NavGeometry.OwnedBuffers AuthorityGeometry {get;private set;}
        internal LayeredNavigationScheduler Scheduler {get;private set;}
        internal NavCowDictionary<NavNameKey,NavOwnedStorage> Requests {get;private set;}
        internal NavCowDictionary<long,NavOwnedStorage> ArrivalIntentSets {get;private set;}
        internal NavCowDictionary<long,NavOwnedStorage> ArrivalHistory {get;private set;}
        internal NavCowDictionary<long,NavOwnedStorage> ArrivalIntents {get;private set;}
        internal NavCowDictionary<long,NavOwnedStorage> ArrivalWork {get;private set;}
        internal NavCowDictionary<long,NavOwnedStorage> Providers {get;private set;}
        internal NavCowDictionary<NavNameKey,NavOwnedStorage> Rows {get;private set;}
        // Published answers stay in this physical DAG while the authority
        // mailbox can still expose their public facade.
        internal NavCowDictionary<NavNameKey,NavOwnedStorage> Delivered {get;private set;}
        internal NavPersistentQueue<NavNameKey> Scan {get;private set;}
        internal NavPersistentQueue<NavNameKey> Projection {get;private set;}
        internal NavPersistentQueue<NavNameKey> Completion {get;private set;}
        internal CertifiedRouteKernel.PumpFrontier Frontier {get;private set;}
        internal long Epoch {get;private set;}
        internal long Progress {get;private set;}
        internal long NextProviderIdentity {get;private set;}=1;
        internal OperationDescriptor Operation {get;private set;}
        private byte lifecycle=8;
        private bool sealedRoot {get=>(lifecycle&1)!=0;set=>lifecycle=(byte)(value?lifecycle|1:lifecycle&~1);}
        private bool released {get=>(lifecycle&2)!=0;set=>lifecycle=(byte)(value?lifecycle|2:lifecycle&~2);}
        private bool faulted {get=>(lifecycle&4)!=0;set=>lifecycle=(byte)(value?lifecycle|4:lifecycle&~4);}
        internal override bool IsImmutableCaptureRoot=>sealedRoot&&!faulted;
        // The quantum and its consumed watermark travel with this exact Host BODY.
        internal bool TickQuantum {get;private set;}
        internal long QuantumGeneration {get;private set;}
        internal long QuantumTick {get;private set;}
        internal long QuantumSerial {get;private set;}
        internal long ConsumedQuantum {get;private set;}
        internal long AdmissionPrefix {get;private set;}
        internal int QuantumBudget {get;private set;}
        internal bool QuantumCompleted {get;private set;}
        private bool fairArrivalPolicy {get=>(lifecycle&8)!=0;set=>lifecycle=(byte)(value?lifecycle|8:lifecycle&~8);}
        internal long LastServedArrival {get;private set;}
        internal long ArrivalRoundEnd {get;private set;}
        internal bool FairArrivals=>TickQuantum&&fairArrivalPolicy;
        internal bool ReplayOnlyProjection=>(lifecycle&16)!=0;
        internal bool AccruedCredits=>(lifecycle&32)!=0;
        internal void MarkAccruedCredits(){if(sealedRoot||TickQuantum)throw new InvalidOperationException("Accrued mode must precede first quantum.");lifecycle|=32;}
        internal bool QuantumRunnable=>TickQuantum&&QuantumSerial>ConsumedQuantum&&!QuantumCompleted;
        internal void IssueQuantum(long generation,long tick,int budget,long prefix)
        {
            if(sealedRoot||generation<1||tick<1||budget<1||budget>int.MaxValue/1024||prefix<0||TickQuantum&&(QuantumSerial!=ConsumedQuantum||Frontier.Stage!=CertifiedRouteKernel.PumpStage.Idle))throw new InvalidOperationException("Invalid quantum issue boundary.");
            TickQuantum=true;QuantumGeneration=generation;QuantumTick=tick;QuantumSerial=checked(QuantumSerial+1);QuantumBudget=budget;AdmissionPrefix=prefix;QuantumCompleted=false;
        }
        internal void CompleteQuantum(){if(sealedRoot||!QuantumRunnable)throw new InvalidOperationException("Invalid quantum completion.");QuantumCompleted=true;}
        internal void ConsumeQuantum(){if(sealedRoot||!QuantumCompleted||QuantumSerial<=ConsumedQuantum)throw new InvalidOperationException("Invalid quantum consumption.");ConsumedQuantum=QuantumSerial;}
        public NavPhysicalTag PhysicalTag=>new NavPhysicalTag(fairArrivalPolicy?NavPhysicalStorageKind.HostRootFairArrivalV1:TickQuantum?NavPhysicalStorageKind.HostRootQuantum:NavPhysicalStorageKind.HostRoot,value:AccruedCredits?NavPhysicalValueKind.AccruedAuthorityCreditsV1:ReplayOnlyProjection?NavPhysicalValueKind.ReplayOnlyProjectionV1:NavPhysicalValueKind.None);
        internal void MarkArrivalPrepared()
        {
            if(sealedRoot||!FairArrivals||Frontier.Stage!=CertifiedRouteKernel.PumpStage.ArrivalAdvance||Frontier.PhaseUsed!=0)throw new InvalidOperationException("Invalid arrival preparation boundary.");
            SetArrivalPhase(CertifiedRouteKernel.PumpStage.ArrivalAdvance,1);
            SetOperation(new OperationDescriptor(CertifiedRouteKernel.PumpStage.ArrivalAdvance,1,false,false,default));
        }
        internal void CommitArrivalService(long attempt,long roundEnd)
        {
            if(sealedRoot||!FairArrivals||attempt<0||roundEnd<attempt||Frontier.Stage!=CertifiedRouteKernel.PumpStage.ArrivalAdvance)throw new InvalidOperationException("Invalid arrival service boundary.");
            LastServedArrival=attempt;ArrivalRoundEnd=roundEnd;SetArrivalPhase(CertifiedRouteKernel.PumpStage.PreFlush,0);
            SetOperation(new OperationDescriptor(CertifiedRouteKernel.PumpStage.PreFlush,0,false,false,default));
        }
        private void SetArrivalPhase(CertifiedRouteKernel.PumpStage stage,int phase)
        {
            var f=Frontier;Frontier=new CertifiedRouteKernel.PumpFrontier(stage,f.OriginalBudget,f.ScansUsed,f.TransfersUsed,f.ReadyUsed,f.ProjectionUsed,f.ProjectionAllowance,f.SolveWorkLimit,f.SolveConsumed,f.AllocationFloor,phase,f.ScanAllowance);
        }
        private NavigationHostRoot(bool ownerReference):base(LayoutBytes)
        {if(ownerReference)Retain();}
        private static NavigationHostRoot Allocate(INavStorageReservation reservation,bool ownerReference=true)
        {
            using(var allocation=new NavStorageAllocationContext(reservation.TryReserve)){
                allocation.Reserve(LayoutBytes);var value=new NavigationHostRoot(ownerReference);allocation.Register(value);return value;
            }
        }

        internal static NavigationHostRoot CreateReplayOnlyOwned(INavStorageReservation reservation)
        {
            var root=CreateOwned(reservation);root.lifecycle|=16;return root;
        }
        internal static NavigationHostRoot CreateOwned(INavStorageReservation reservation)
        {
            var root=Allocate(reservation);
            try{
                root.Scheduler=NavPhysicalMacro.Acquire(root,LayeredNavigationScheduler.CreateOwned(reservation));root.Scheduler.Release();
                root.Requests=NavPhysicalMacro.Acquire(root,NavCowDictionary<NavNameKey,NavOwnedStorage>.CreateOwned(reservation));root.Requests.Release();
                root.ArrivalIntentSets=NavPhysicalMacro.Acquire(root,NavCowDictionary<long,NavOwnedStorage>.CreateOwned(reservation));root.ArrivalIntentSets.Release();
                root.ArrivalHistory=NavPhysicalMacro.Acquire(root,NavCowDictionary<long,NavOwnedStorage>.CreateOwned(reservation));root.ArrivalHistory.Release();
                root.ArrivalIntents=NavPhysicalMacro.Acquire(root,NavCowDictionary<long,NavOwnedStorage>.CreateOwned(reservation));root.ArrivalIntents.Release();
                root.ArrivalWork=NavPhysicalMacro.Acquire(root,NavCowDictionary<long,NavOwnedStorage>.CreateOwned(reservation));root.ArrivalWork.Release();
                root.Providers=NavPhysicalMacro.Acquire(root,NavCowDictionary<long,NavOwnedStorage>.CreateOwned(reservation));root.Providers.Release();
                root.Rows=NavPhysicalMacro.Acquire(root,NavCowDictionary<NavNameKey,NavOwnedStorage>.CreateOwned(reservation));root.Rows.Release();
                root.Delivered=NavPhysicalMacro.Acquire(root,NavCowDictionary<NavNameKey,NavOwnedStorage>.CreateOwned(reservation));root.Delivered.Release();
                root.Scan=NavPhysicalMacro.Acquire(root,NavPersistentQueue<NavNameKey>.CreateOwned(reservation));root.Scan.Release();
                root.Projection=NavPhysicalMacro.Acquire(root,NavPersistentQueue<NavNameKey>.CreateOwned(reservation));root.Projection.Release();
                root.Completion=NavPhysicalMacro.Acquire(root,NavPersistentQueue<NavNameKey>.CreateOwned(reservation));root.Completion.Release();
                return root;
            }catch{root.ReleaseStorage();throw;}
        }
        internal NavigationHostRoot ForkOwned(INavStorageReservation reservation)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.HostFork);
            NavStorageAdmission.RequireOwned(reservation,this);
            if(!sealedRoot)throw new InvalidOperationException("Host root is still mutable.");
            var fork=Allocate(reservation);
            try{
                fork.Scheduler=NavPhysicalMacro.Acquire(fork,Scheduler.ForkOwned(reservation));fork.Scheduler.Release();
                fork.Requests=NavPhysicalMacro.Acquire(fork,Requests.ForkOwned(reservation));fork.Requests.Release();
                fork.ArrivalIntentSets=NavPhysicalMacro.Acquire(fork,ArrivalIntentSets.ForkOwned(reservation));fork.ArrivalIntentSets.Release();
                fork.ArrivalHistory=NavPhysicalMacro.Acquire(fork,ArrivalHistory.ForkOwned(reservation));fork.ArrivalHistory.Release();
                fork.ArrivalIntents=NavPhysicalMacro.Acquire(fork,ArrivalIntents.ForkOwned(reservation));fork.ArrivalIntents.Release();
                fork.ArrivalWork=NavPhysicalMacro.Acquire(fork,ArrivalWork.ForkOwned(reservation));fork.ArrivalWork.Release();
                fork.Providers=NavPhysicalMacro.Acquire(fork,Providers.ForkOwned(reservation));fork.Providers.Release();
                fork.Rows=NavPhysicalMacro.Acquire(fork,Rows.ForkOwned(reservation));fork.Rows.Release();
                fork.Delivered=NavPhysicalMacro.Acquire(fork,Delivered.ForkOwned(reservation));fork.Delivered.Release();
                fork.Scan=NavPhysicalMacro.Acquire(fork,Scan.ForkOwned(reservation));fork.Scan.Release();
                fork.Projection=NavPhysicalMacro.Acquire(fork,Projection.ForkOwned(reservation));fork.Projection.Release();
                fork.Completion=NavPhysicalMacro.Acquire(fork,Completion.ForkOwned(reservation));fork.Completion.Release();
                fork.AuthorityGeometry=NavPhysicalMacro.Acquire(fork,AuthorityGeometry);
                fork.Frontier=Frontier;fork.Epoch=Epoch;fork.Progress=Progress;fork.NextProviderIdentity=NextProviderIdentity;
                fork.TickQuantum=TickQuantum;fork.QuantumGeneration=QuantumGeneration;fork.QuantumTick=QuantumTick;fork.QuantumSerial=QuantumSerial;fork.ConsumedQuantum=ConsumedQuantum;fork.AdmissionPrefix=AdmissionPrefix;fork.QuantumBudget=QuantumBudget;fork.QuantumCompleted=QuantumCompleted;
                if(AccruedCredits)fork.lifecycle|=32;fork.fairArrivalPolicy=fairArrivalPolicy;if(ReplayOnlyProjection)fork.lifecycle|=16;fork.LastServedArrival=LastServedArrival;fork.ArrivalRoundEnd=ArrivalRoundEnd;
                fork.SetOperation(Operation);return fork;
            }catch{fork.ReleaseStorage();throw;}
        }
        internal void SetFrontier(CertifiedRouteKernel.PumpFrontier frontier,long epoch,long progress)
        {if(sealedRoot||epoch<0||progress<0)throw new ArgumentException("Invalid prepared host frontier.");Frontier=frontier;Epoch=epoch;Progress=progress;}
        private long AddProvider(NavigationHostProvider provider,INavStorageReservation reservation)
        {
            if(sealedRoot||provider==null||NextProviderIdentity==long.MaxValue)throw new InvalidOperationException("Invalid prepared host provider registration.");
            NavStorageAdmission.RequireOwned(reservation,provider);
            long identity=NextProviderIdentity;
            Providers.Add(identity,provider);NextProviderIdentity++;
            return identity;
        }
        // The caller owns a fork and discards its scope on any exception. A
        // successful call stages scheduler, request, provider, row and scan
        // together before the short Session publication boundary.
        private PartitionNavigationProvider SharedPartition(ILayeredNavigationProvider provider)
        {
            if(!(provider is PartitionNavigationProvider partition))return null;
            foreach(var entry in Providers)if(((NavigationHostProvider)entry.Value).Provider is PartitionNavigationProvider candidate&&partition.SameArrangement(candidate))return candidate;
            return null;
        }
        internal NavNameKey RegisterOrdinary(NavigationRequest request,NavScheduledRequest prepared,
            string surfaceProviderId,INavStorageReservation reservation,NavGeometry currentAuthority=null)
        {
            if(sealedRoot||request==null||prepared==null||reservation==null||
                string.IsNullOrWhiteSpace(surfaceProviderId)||NextProviderIdentity==long.MaxValue)
                throw new ArgumentException("Invalid prepared host registration.");
            NavStorageAdmission.RequireOwned(reservation,this);
            if(prepared.StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,prepared.StorageRoot);
            if((prepared.Provider as INavStorageFacade)?.StorageRoot is NavOwnedStorage providerOwner)
                NavStorageAdmission.RequireOwned(reservation,providerOwner);
            NavigationHostRequest input=null;NavigationHostProvider provider=null;NavigationHostRow row=null;
            try{
                NavGeometry authority=null;
                if(currentAuthority!=null&&ReferenceEquals(currentAuthority,request.BaseGeometry)){
                    StageAuthorityGeometry(currentAuthority,reservation);
                    authority=AuthorityGeometry.Facade;
                }
                input=NavigationHostRequest.CopyOwned(request,prepared.Provider.Binding,reservation,authority);
                provider=NavigationHostProvider.CopyOwned(prepared.Provider,input.Request.Geometry,
                    surfaceProviderId,1,reservation,SharedPartition(prepared.Provider));
                row=NavigationHostRow.CreateRegistered(input,provider,NextProviderIdentity,prepared,reservation,ReplayOnlyProjection);
                var key=NavNameKey.Subscription(row.Scheduled.Subscription);
                if(Requests.ContainsKey(key)||Rows.ContainsKey(key)||!Scheduler.Submit(row.Scheduled))
                    throw new InvalidOperationException("Physical host registration declined; discard prepared fork.");
                AddProvider(provider,reservation);
                Requests.Add(key,input);Rows.Add(key,row);Scan.AddLast(key);
                return key;
            }catch{faulted=true;throw;}
            finally{row?.ReleaseStorage();provider?.ReleaseStorage();input?.ReleaseStorage();}
        }
        internal NavNameKey RegisterArrival(NavigationRequest request,NavScheduledRequest prepared,NavTypedRoute route,long token,
            string surfaceProviderId,INavStorageReservation reservation)
        {
            if(sealedRoot||token<1||route==null)throw new ArgumentException("Invalid prepared physical arrival registration.");
            NavStorageAdmission.RequireOwned(reservation,route);
            NavigationHostRequest input=null;NavigationHostProvider provider=null;NavigationHostRow row=null,projected=null;NavScheduledCompletion solved=null;
            try{
                input=NavigationHostRequest.CopyOwned(request,prepared.Provider.Binding,reservation);
                provider=NavigationHostProvider.CopyOwned(prepared.Provider,input.Request.Geometry,surfaceProviderId,1,reservation,SharedPartition(prepared.Provider));
                row=NavigationHostRow.CreateRegistered(input,provider,NextProviderIdentity,prepared,reservation,ReplayOnlyProjection);row.SetArrivalToken(token);
                solved=new NavScheduledCompletion(row.Scheduled,NavSolveStatus.Ready,route,0,0,reservation.TryReserve);
                projected=row.WithSolved(solved,reservation);
                var key=NavNameKey.Subscription(projected.Scheduled.Subscription);
                if(Requests.ContainsKey(key)||Rows.ContainsKey(key))throw new ArgumentException("Duplicate physical arrival registration.");
                AddProvider(provider,reservation);Requests.Add(key,input);Rows.Add(key,projected);Scan.AddLast(key);Projection.AddLast(key);return key;
            }catch{faulted=true;throw;}
            finally{projected?.ReleaseStorage();row?.ReleaseStorage();solved?.Release();provider?.ReleaseStorage();input?.ReleaseStorage();}
        }
        private static bool SameAuthorityGeometry(NavGeometry a,NavGeometry b)
        {
            if(a.Revision!=b.Revision||a.HalfExtent!=b.HalfExtent||a.Obstacles.Count!=b.Obstacles.Count)return false;
            for(int n=0;n<a.Obstacles.Count;n++)if(!a.Obstacles[n].GeometryEquals(b.Obstacles[n]))return false;
            return true;
        }
        internal void StageScan(bool current,INavStorageReservation reservation)
        {
            if(sealedRoot||faulted||Scan.Count==0)throw new ArgumentException("Missing physical scan head.");
            NavStorageAdmission.RequireOwned(reservation,this);
            var key=Scan.First;
            if(!Rows.TryGetValue(key,out var entry)||entry is NavigationHostRow completed&&completed.Answer!=null){Scan.RemoveFirst();return;}
            if(current){Scan.RemoveFirst();Scan.AddLast(key);return;}
            StageStale(key,reservation);
        }
        internal void StageStale(NavNameKey key,INavStorageReservation reservation)
        {
            if(sealedRoot||faulted||reservation==null||!Rows.TryGetValue(key,out var entry)||
                !(entry is NavigationHostRow row)||row.Answer!=null||Scan.Count==0||
                Scan.First.Value!=key.Value||
                !ReferenceEquals(Scan.First.Reference.Owner,row.Scheduled.StorageRoot))
                throw new ArgumentException("Missing live physical host row.");
            NavStorageAdmission.RequireOwned(reservation,this);
            NavigationHostAnswer answer=null;NavigationHostRow next=null;
            try{
                if(row.ArrivalToken==0&&row.Solved==null&&!Scheduler.CancelRetained(row.Scheduled.Subscription))
                    throw new InvalidOperationException("Physical scheduler did not own the stale subscription.");
                Scan.RemoveFirst();
                answer=NavigationHostAnswer.CreateTerminal(row.RequestOwner,NavSolveStatus.Stale,reservation);
                next=row.WithAnswer(answer,reservation);
                Rows[key]=next;Completion.AddLast(key);
            }catch{faulted=true;throw;}
            finally{next?.ReleaseStorage();answer?.ReleaseStorage();}
        }
        internal bool StageTakeReady(INavStorageReservation reservation)
        {
            if(sealedRoot||faulted||reservation==null)throw new ArgumentException("Invalid physical Ready transfer.");
            NavStorageAdmission.RequireOwned(reservation,this);
            NavScheduledCompletion completed=null;NavigationHostAnswer answer=null;NavigationHostRow next=null;
            try{
                if(!Scheduler.TryTake(id=>Rows.TryGetValue(id.StableKey,out var value)&&
                    value is NavigationHostRow row&&row.Answer==null&&row.Solved==null,out completed))return false;
                var key=NavNameKey.Subscription(completed.Subscription);
                if(!Rows.TryGetValue(key,out var entry)||!(entry is NavigationHostRow row)||
                    row.Answer!=null||row.Solved!=null)
                    throw new ArgumentException("Physical Ready transfer lost its registered row.");
                if(completed.Status==NavSolveStatus.Ready){
                    next=row.WithSolved(completed,reservation);
                    Projection.AddLast(key);
                }else{
                    answer=NavigationHostAnswer.CreateTerminal(row.RequestOwner,completed.Status,reservation);
                    next=row.WithAnswer(answer,reservation);
                    Completion.AddLast(key);
                }
                Rows[key]=next;return true;
            }catch{faulted=true;throw;}
            finally{next?.ReleaseStorage();answer?.ReleaseStorage();completed?.Release();}
        }
        internal void StageProjectionStep(NavNameKey key,NavigationHostProjection step,
            INavStorageReservation reservation)
        {
            if(sealedRoot||faulted||reservation==null||step==null||
                !Rows.TryGetValue(key,out var entry)||!(entry is NavigationHostRow row)||
                row.Answer!=null||row.ProjectionOwner==null||Projection.Count==0||
                Projection.First.Value!=key.Value||
                !ReferenceEquals(Projection.First.Reference.Owner,row.Scheduled.StorageRoot))
                throw new ArgumentException("Missing physical projection step row.");
            NavStorageAdmission.RequireOwned(reservation,this);
            NavStorageAdmission.RequireOwned(reservation,step);
            NavigationHostRow next=null;
            try{next=row.WithProjection(step,reservation);Rows[key]=next;}
            catch{faulted=true;throw;}
            finally{next?.ReleaseStorage();}
        }
        internal void StageProjectionTransition(NavNameKey key,NavigationHostRow.ProjectionTransition transition,
            NavNameReference portal,NavLocation reached,NavNameReference[] observed,INavStorageReservation reservation)
        {
            if(sealedRoot||faulted||reservation==null||
                !Rows.TryGetValue(key,out var entry)||!(entry is NavigationHostRow row)||
                row.Answer!=null||row.ProjectionOwner==null||Projection.Count==0||
                Projection.First.Value!=key.Value||
                !ReferenceEquals(Projection.First.Reference.Owner,row.Scheduled.StorageRoot))
                throw new ArgumentException("Missing sealed local projection head.");
            NavStorageAdmission.RequireOwned(reservation,this);
            NavigationHostRow next=null;
            try{next=row.AdvanceProjection(transition,portal,reached,observed,reservation);Rows[key]=next;}
            catch{faulted=true;throw;}
            finally{next?.ReleaseStorage();}
        }
        internal void StageReplayFailure(NavNameKey key,INavStorageReservation reservation)
        {
            if(!Rows.TryGetValue(key,out var entry)||!(entry is NavigationHostRow row)||row.ProjectionOwner==null)
                throw new ArgumentException("Missing Replay failure head.");
            if(row.ProjectionOwner.ReplayOnly)
                StageProjectionTransition(key,NavigationHostRow.ProjectionTransition.LegacyReset,default,default,null,reservation);
            else StageProjectionFailure(key,reservation);
        }
        // The final answer is built from the retained prefix; a caller cannot
        // inject a fresh route array or skip an admitted leg/replay step.
        internal void StageReady(NavNameKey key,INavStorageReservation reservation)
        {
            if(sealedRoot||faulted||reservation==null||
                !Rows.TryGetValue(key,out var entry)||!(entry is NavigationHostRow row)||
                row.Answer!=null||row.Solved?.Route==null||row.ProjectionOwner==null||
                Projection.Count==0||
                Projection.First.Value!=key.Value||
                !ReferenceEquals(Projection.First.Reference.Owner,row.Scheduled.StorageRoot))
                throw new ArgumentException("Missing projected physical host row.");
            NavStorageAdmission.RequireOwned(reservation,this);
            NavStorageAdmission.RequireOwned(reservation,row.Solved);
            var state=row.ProjectionOwner;
            if(state.LegCursor!=row.Solved.Route.Legs.Count||
                state.PointCount==0||!state.Expected.Equals(row.Scheduled.Subscription.AssignedEndpoint)||
                !state.Point(state.PointCount-1).Equals(state.Expected.Position)||
                row.Provider is PartitionNavigationProvider&&
                (state.ReplayCursor!=state.PointCount||!state.At.Equals(state.Expected)||
                 !state.PortalsMatch()))
                throw new ArgumentException("Physical projection is not complete.");
            NavigationHostCorridor corridor=null;NavigationHostAnswer answer=null;NavigationHostRow next=null;
            try{
                int originalCount=0;foreach(var leg in row.Solved.Route.Legs)if(leg.Kind==NavTypedLegKind.Surface)originalCount++;
                using(NavStorageScratch.Reserve(reservation,checked(256L+16L*originalCount))){
                    var points=new NavPoint[originalCount];int i=0;
                    foreach(var leg in row.Solved.Route.Legs)if(leg.Kind==NavTypedLegKind.Surface)points[i++]=leg.To.Position;
                    corridor=NavigationHostCorridor.CreateOwned(row.RequestOwner,row.ProviderOwner,
                        row.Scheduled,row.Solved.Route,reservation);
                    answer=NavigationHostAnswer.CreateReady(row.RequestOwner,corridor,points,reservation);
                }
                next=row.WithAnswer(answer,reservation);
                Projection.RemoveFirst();Rows[key]=next;Completion.AddLast(key);
            }catch{faulted=true;throw;}
            finally{next?.ReleaseStorage();answer?.ReleaseStorage();corridor?.ReleaseStorage();}
        }
        internal void StageProjectionFailure(NavNameKey key,INavStorageReservation reservation)
        {
            if(sealedRoot||faulted||reservation==null||
                !Rows.TryGetValue(key,out var entry)||!(entry is NavigationHostRow row)||
                row.Answer!=null||row.Solved?.Route==null||Projection.Count==0||
                Projection.First.Value!=key.Value||
                !ReferenceEquals(Projection.First.Reference.Owner,row.Scheduled.StorageRoot))
                throw new ArgumentException("Missing failed physical projection head.");
            NavStorageAdmission.RequireOwned(reservation,this);
            NavigationHostAnswer answer=null;NavigationHostRow next=null;
            try{
                answer=NavigationHostAnswer.CreateTerminal(row.RequestOwner,
                    NavSolveStatus.BlockedConnector,reservation);
                next=row.WithAnswer(answer,reservation);
                Projection.RemoveFirst();Rows[key]=next;Completion.AddLast(key);
            }catch{faulted=true;throw;}
            finally{next?.ReleaseStorage();answer?.ReleaseStorage();}
        }
        internal bool SkipCompletedProjection(INavStorageReservation reservation)
        {
            if(sealedRoot||faulted||reservation==null||Projection.Count==0)
                throw new ArgumentException("Missing physical projection head.");
            NavStorageAdmission.RequireOwned(reservation,this);
            var key=Projection.First;
            if(Rows.TryGetValue(key,out var entry)&&
                (!(entry is NavigationHostRow row)||row.Answer==null))return false;
            try{Projection.RemoveFirst();return true;}
            catch{faulted=true;throw;}
        }
        // The authority publishes the answer mailbox entry atomically with
        // this sealed fork. Old scan/projection name tombstones remain ordered
        // until their own bounded queue turn consumes them.
        internal void StageDiscardCompletionTombstone(INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);
            if(sealedRoot||Completion.Count==0||Rows.ContainsKey(Completion.First))throw new ArgumentException("Missing completion tombstone.");Completion.RemoveFirst();
        }
        internal void StageFlushCompletion(NavNameKey key,INavStorageReservation reservation)
        {
            if(sealedRoot||faulted||reservation==null||Completion.Count==0||
                Completion.First.Value!=key.Value||
                !Rows.TryGetValue(key,out var entry)||!(entry is NavigationHostRow row)||
                row.Answer==null||
                !ReferenceEquals(Completion.First.Reference.Owner,row.Scheduled.StorageRoot))
                throw new ArgumentException("Missing physical completion head.");
            NavStorageAdmission.RequireOwned(reservation,this);
            try{
                if(Delivered.ContainsKey(key))throw new ArgumentException("Duplicate delivered physical answer.");
                Delivered.Add(Completion.First,row);
                Rows.Remove(key);Requests.Remove(key);Providers.Remove(row.ProviderIdentity);
                Completion.RemoveFirst();
            }catch{faulted=true;throw;}
        }
        internal void StageRetireDelivered(NavNameKey key,INavStorageReservation reservation)
        {
            if(sealedRoot||faulted||reservation==null||
                !Delivered.TryGetValue(key,out var entry)||!(entry is NavigationHostRow row)||
                row.Answer==null)
                throw new ArgumentException("Missing delivered physical answer.");
            var canonical=NavNameKey.Subscription(row.Scheduled.Subscription);
            if(key.Value!=canonical.Value||!row.MatchesDelivered(canonical))
                throw new ArgumentException("Delivered physical name owner changed.");
            NavStorageAdmission.RequireOwned(reservation,this);
            try{Delivered.Remove(canonical);}catch{faulted=true;throw;}
        }
        internal void SetOperation(OperationDescriptor operation)
        {
            if(sealedRoot)throw new InvalidOperationException("Committed host operation cannot change.");
            var next=operation.Selector.Owner;var old=Operation.Selector.Owner;
            if(next!=null){RequireEdge(next);if(!next.IsImmutableCaptureRoot)throw new ArgumentException("Mutable host operation name.");operation.Selector.Validate(operation.Selector.Resolve(),this);next.Retain();}
            Operation=operation;old?.Release();
        }
        internal void SealPrepared()
        {using var measured=PerformanceProfile.Measure(ProfileStage.HostSeal);if(sealedRoot||faulted)throw new InvalidOperationException("Host root cannot be sealed.");ValidateCatalog();Scheduler.SealPrepared();Requests.Seal();ArrivalIntentSets.Seal();ArrivalHistory.Seal();ArrivalIntents.Seal();ArrivalWork.Seal();Providers.Seal();Rows.Seal();Delivered.Seal();Scan.Seal();Projection.Seal();Completion.Seal();sealedRoot=true;}
        internal void StageAuthorityGeometry(NavGeometry current,INavStorageReservation reservation)
        {
            if(sealedRoot||current==null)throw new ArgumentException("Invalid current authority snapshot.");
            if(AuthorityGeometry!=null&&ReferenceEquals(AuthorityGeometry,current.StorageRoot))return;
            if(current.StorageRoot!=null){NavStorageAdmission.RequireOwned(reservation,current.StorageRoot);var old=AuthorityGeometry;AuthorityGeometry=NavPhysicalMacro.Acquire(this,(NavGeometry.OwnedBuffers)current.StorageRoot);old?.Release();return;}
            if(AuthorityGeometry!=null&&SameAuthorityGeometry(AuthorityGeometry.Facade,current))return;
            var copy=NavGeometry.CopyOwned(current,reservation);
            try{var old=AuthorityGeometry;AuthorityGeometry=NavPhysicalMacro.Acquire(this,(NavGeometry.OwnedBuffers)copy.StorageRoot);old?.Release();}
            finally{copy.StorageRoot.Release();}
        }
        internal NavigationRequest[] StageArrivalIntents(long attemptId,NavigationRequest[] inputs,NavGeometry authority,INavStorageReservation reservation)
        {
            if(sealedRoot||inputs==null||inputs.Length==0)throw new ArgumentException("Missing physical arrival intents.");
            StageAuthorityGeometry(authority,reservation);
            using(NavStorageScratch.Reserve(reservation,checked(64L+8L*inputs.Length))){
                var result=new NavOwnedStorage[inputs.Length];
                for(int n=0;n<inputs.Length;n++){
                    var source=inputs[n];var owner=NavigationHostRequest.CopyOwned(source,source.CertifiedProviderBinding,reservation,
                        ReferenceEquals(source.BaseGeometry,authority)?AuthorityGeometry.Facade:null);
                    try{ArrivalIntents.Add(source.Request,owner);result[n]=owner;}finally{owner.ReleaseStorage();}
                }
                var view=NavigationHostArrivalIntents.CreateOwned(result,reservation);
                try{ArrivalIntentSets.Add(attemptId,view);return view.Requests;}finally{view.Release();}
            }
        }
        internal void StageRetainedArrivalAttempt(long id,NavigationHostArrivalIntents intents,NavigationArrivalWork work,NavigationHostArrivalHistory history,INavStorageReservation reservation)
        {
            if(sealedRoot||id<1||intents==null||work==null||history==null||ArrivalWork.ContainsKey(id))throw new ArgumentException("Invalid retained arrival attempt.");
            NavStorageAdmission.RequireOwned(reservation,intents);NavStorageAdmission.RequireOwned(reservation,work);NavStorageAdmission.RequireOwned(reservation,history);
            ArrivalIntentSets.Add(id,intents);ArrivalWork.Add(id,work);ArrivalHistory.Add(id,history);
            foreach(var request in intents.Requests){if(request.StorageOwner==null)throw new ArgumentException("Retained intent has no exact request owner.");ArrivalIntents.Add(request.Request,request.StorageOwner);}
        }
        internal void StageArrivalWork(long id,NavigationArrivalWork work,INavStorageReservation reservation)
        {
            if(sealedRoot||faulted||id<1||work==null||!work.IsImmutableCaptureRoot)throw new ArgumentException("Invalid physical arrival work publication.");
            NavStorageAdmission.RequireOwned(reservation,this);NavStorageAdmission.RequireOwned(reservation,work);
            if(ArrivalWork.ContainsKey(id))ArrivalWork[id]=work;else {ArrivalWork.Add(id,work);var history=NavigationHostArrivalHistory.CreateOwned(reservation);try{ArrivalHistory.Add(id,history);}finally{history.Release();}}
        }
        internal void StageArrivalHistory(long id,NavigationHostArrivalHistory history,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,history);if(sealedRoot||!ArrivalHistory.ContainsKey(id)||!history.IsImmutableCaptureRoot)throw new ArgumentException("Invalid prepared arrival history.");ArrivalHistory[id]=history;
        }
        internal void StageRetireArrivalWork(long id,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);
            if(sealedRoot||!ArrivalWork.ContainsKey(id)||!ArrivalIntentSets.TryGetValue(id,out var value)||!(value is NavigationHostArrivalIntents view))throw new ArgumentException("Missing obsolete physical arrival work.");
            foreach(var request in view.Requests)ArrivalIntents.Remove(request.Request);
            ArrivalWork.Remove(id);ArrivalIntentSets.Remove(id);ArrivalHistory.Remove(id);
        }
        internal void StageRetireArrivalRow(NavNameKey key,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);
            if(sealedRoot||!Rows.TryGetValue(key,out var value)||!(value is NavigationHostRow row)||row.ArrivalToken==0)throw new ArgumentException("Missing retiring arrival row.");
            Rows.Remove(key);Requests.Remove(key);Providers.Remove(row.ProviderIdentity);
        }
        private void ValidateCatalog()
        {
            foreach(var entry in ArrivalIntents)if(!(entry.Value is NavigationHostRequest input)||entry.Key!=input.Request.Request)throw new ArgumentException("Invalid physical arrival intent owner.");
            foreach(var entry in ArrivalWork){
                if(entry.Key<1||!(entry.Value is NavigationArrivalWork work)||!work.IsImmutableCaptureRoot||
                    !ArrivalIntentSets.TryGetValue(entry.Key,out var intents)||!(intents is NavigationHostArrivalIntents view)||
                    !ArrivalHistory.TryGetValue(entry.Key,out var history)||!(history is NavigationHostArrivalHistory grants)||grants.Count!=work.LogicalEpoch+1)
                    throw new ArgumentException("Invalid physical arrival work owner/history.");
                foreach(var request in view.Requests)if(!ArrivalIntents.TryGetValue(request.Request,out var input)||!(input is NavigationHostRequest owned)||!ReferenceEquals(owned.Request,request))throw new ArgumentException("Physical arrival intent view alias changed.");
            }
            if(ArrivalWork.Count!=ArrivalIntentSets.Count||ArrivalWork.Count!=ArrivalHistory.Count)throw new ArgumentException("Unpartitioned physical arrival catalogs.");
            foreach(var entry in Providers)
                if(entry.Key<1||entry.Key>=NextProviderIdentity||!(entry.Value is NavigationHostProvider))
                    throw new ArgumentException("Invalid physical host provider catalog.");
            foreach(var entry in Rows){
                if(!(entry.Value is NavigationHostRow row)||row.ReplayOnly&&!ReplayOnlyProjection||
                    !Requests.TryGetValue(entry.Key,out var request)||
                    !Providers.TryGetValue(row.ProviderIdentity,out var provider)||
                    !row.MatchesCatalog(entry.Key,request,provider))
                    throw new ArgumentException("Invalid physical host row catalog.");
            }
            foreach(var entry in Delivered)
                if(Rows.ContainsKey(entry.Key)||Requests.ContainsKey(entry.Key)||
                    !(entry.Value is NavigationHostRow row)||row.ReplayOnly&&!ReplayOnlyProjection||row.Answer==null||
                    !row.MatchesDelivered(entry.Key))
                    throw new ArgumentException("Invalid delivered physical host answer.");
        }
        public void WritePhysicalBody(NavPhysicalBodyWriter writer)
        {
            if(!sealedRoot||StorageBytes!=NavStorageLayout.Charge(LayoutBytes))throw new ArgumentException("Mutable or undercharged host BODY.");
            ValidateCatalog();
            if(AccruedCredits)writer.Output.Write(ReplayOnlyProjection);
            if(fairArrivalPolicy)writer.Output.Write(TickQuantum);
            if(TickQuantum){writer.Output.Write(QuantumGeneration);writer.Output.Write(QuantumTick);writer.Output.Write(QuantumSerial);writer.Output.Write(ConsumedQuantum);writer.Output.Write(AdmissionPrefix);writer.Output.Write(QuantumBudget);writer.Output.Write(QuantumCompleted);}
            if(fairArrivalPolicy){writer.Output.Write(LastServedArrival);writer.Output.Write(ArrivalRoundEnd);}
            writer.Output.Write((int)Frontier.Stage);writer.Output.Write(Frontier.OriginalBudget);
            writer.Output.Write(Frontier.ScansUsed);writer.Output.Write(Frontier.TransfersUsed);
            writer.Output.Write(Frontier.ReadyUsed);writer.Output.Write(Frontier.ProjectionUsed);
            writer.Output.Write(Frontier.ProjectionAllowance);writer.Output.Write(Frontier.SolveWorkLimit);
            writer.Output.Write(Frontier.SolveConsumed);writer.Output.Write(Frontier.AllocationFloor);
            writer.Output.Write(Frontier.PhaseUsed);writer.Output.Write(Frontier.ScanAllowance);
            writer.Output.Write(Epoch);writer.Output.Write(Progress);writer.Output.Write(NextProviderIdentity);
            writer.Output.Write((int)Operation.Stage);writer.Output.Write(Operation.Iteration);
            writer.Output.Write(Operation.Entered);writer.Output.Write(Operation.ProgressIncrementConsumed);
            Operation.Selector.Validate(Operation.Selector.Resolve(),this);Operation.Selector.Write(writer);
            NavPhysicalMacro.WriteEdge(writer,this,AuthorityGeometry,true);
            NavPhysicalMacro.WriteEdge(writer,this,Scheduler);
            NavPhysicalMacro.WriteEdge(writer,this,Requests);
            NavPhysicalMacro.WriteEdge(writer,this,ArrivalIntentSets);
            NavPhysicalMacro.WriteEdge(writer,this,ArrivalHistory);
            NavPhysicalMacro.WriteEdge(writer,this,ArrivalIntents);
            NavPhysicalMacro.WriteEdge(writer,this,ArrivalWork);
            NavPhysicalMacro.WriteEdge(writer,this,Providers);
            NavPhysicalMacro.WriteEdge(writer,this,Rows);
            NavPhysicalMacro.WriteEdge(writer,this,Delivered);
            NavPhysicalMacro.WriteEdge(writer,this,Scan);
            NavPhysicalMacro.WriteEdge(writer,this,Projection);
            NavPhysicalMacro.WriteEdge(writer,this,Completion);
        }
        internal static NavigationHostRoot ReadPhysicalBody(NavPhysicalBodyReader reader,bool quantum=false,bool fairArrival=false,bool replayOnly=false,bool accrued=false)
        {
            if(accrued)replayOnly=NavPhysicalMacro.ReadBoolean(reader);
            if(fairArrival)quantum=NavPhysicalMacro.ReadBoolean(reader);
            long generation=0,tick=0,serial=0,consumed=0,prefix=0;int grant=0;bool complete=false;
            if(quantum){generation=reader.Input.ReadInt64();tick=reader.Input.ReadInt64();serial=reader.Input.ReadInt64();consumed=reader.Input.ReadInt64();prefix=reader.Input.ReadInt64();grant=reader.Input.ReadInt32();complete=NavPhysicalMacro.ReadBoolean(reader);
                if(generation<1||tick<1||serial<1||consumed<0||consumed>serial||serial-consumed>1||prefix<0||grant<1||grant>int.MaxValue/1024||consumed==serial&&!complete)throw new ArgumentException("Invalid saved tick quantum.");}
            long lastArrival=fairArrival?reader.Input.ReadInt64():0,roundEnd=fairArrival?reader.Input.ReadInt64():0;if(lastArrival<0||roundEnd<lastArrival||!quantum&&(lastArrival!=0||roundEnd!=0))throw new ArgumentException("Invalid saved arrival service cursor.");
            int stage=reader.Input.ReadInt32();if(!Enum.IsDefined(typeof(CertifiedRouteKernel.PumpStage),stage))throw new ArgumentException("Invalid host pump stage.");
            int budget=reader.Input.ReadInt32(),scans=reader.Input.ReadInt32(),transfers=reader.Input.ReadInt32(),ready=reader.Input.ReadInt32(),projection=reader.Input.ReadInt32();
            int allowance=reader.Input.ReadInt32(),solveLimit=reader.Input.ReadInt32(),solveConsumed=reader.Input.ReadInt32();
            long floor=reader.Input.ReadInt64();int phaseUsed=reader.Input.ReadInt32(),scanAllowance=reader.Input.ReadInt32();long epoch=reader.Input.ReadInt64(),progress=reader.Input.ReadInt64(),nextProvider=reader.Input.ReadInt64();
            if(phaseUsed<0||phaseUsed>budget||scanAllowance<0||scanAllowance>budget||budget<0||budget>int.MaxValue/1024||scans<0||scans>budget||transfers<0||transfers>budget||ready<0||ready>budget||projection<0||projection>checked(budget*16)||allowance!=checked(budget*16)||solveLimit<0||solveLimit>checked(budget*1024)||solveConsumed<0||solveConsumed>solveLimit||floor<0||floor>LayeredNavigationScheduler.MaxRetainedBytes||epoch<0||progress<0||nextProvider<1)
                throw new ArgumentException("Invalid host frontier counters.");
            int operationStage=reader.Input.ReadInt32(),iteration=reader.Input.ReadInt32();
            bool entered=NavPhysicalMacro.ReadBoolean(reader),incremented=NavPhysicalMacro.ReadBoolean(reader);
            var selector=NavNameReference.Read(reader);
            if(!Enum.IsDefined(typeof(CertifiedRouteKernel.PumpStage),operationStage))throw new ArgumentException("Invalid host operation stage.");
            var operation=new OperationDescriptor((CertifiedRouteKernel.PumpStage)operationStage,iteration,entered,incremented,selector);
            var authorityGeometry=NavPhysicalMacro.ReadEdge<NavGeometry.OwnedBuffers>(reader,true);
            var scheduler=NavPhysicalMacro.ReadEdge<LayeredNavigationScheduler>(reader);
            var requests=NavPhysicalMacro.ReadEdge<NavCowDictionary<NavNameKey,NavOwnedStorage>>(reader);
            var arrivalintentsets=NavPhysicalMacro.ReadEdge<NavCowDictionary<long,NavOwnedStorage>>(reader);
            var arrivalhistory=NavPhysicalMacro.ReadEdge<NavCowDictionary<long,NavOwnedStorage>>(reader);
            var intents=NavPhysicalMacro.ReadEdge<NavCowDictionary<long,NavOwnedStorage>>(reader);
            var arrivals=NavPhysicalMacro.ReadEdge<NavCowDictionary<long,NavOwnedStorage>>(reader);
            var providers=NavPhysicalMacro.ReadEdge<NavCowDictionary<long,NavOwnedStorage>>(reader);
            var rows=NavPhysicalMacro.ReadEdge<NavCowDictionary<NavNameKey,NavOwnedStorage>>(reader);
            var delivered=NavPhysicalMacro.ReadEdge<NavCowDictionary<NavNameKey,NavOwnedStorage>>(reader);
            var scan=NavPhysicalMacro.ReadEdge<NavPersistentQueue<NavNameKey>>(reader);
            var projected=NavPhysicalMacro.ReadEdge<NavPersistentQueue<NavNameKey>>(reader);
            var completed=NavPhysicalMacro.ReadEdge<NavPersistentQueue<NavNameKey>>(reader);
            // ReadFrom supplies the returned owner reference after the definition
            // is resolved. The scope holds this definition's birth until commit.
            var root=Allocate(reader.Reservation,false);
            try{
                root.AuthorityGeometry=NavPhysicalMacro.Acquire(root,authorityGeometry);root.Scheduler=NavPhysicalMacro.Acquire(root,scheduler);root.Requests=NavPhysicalMacro.Acquire(root,requests);root.ArrivalIntentSets=NavPhysicalMacro.Acquire(root,arrivalintentsets);root.ArrivalHistory=NavPhysicalMacro.Acquire(root,arrivalhistory);root.ArrivalIntents=NavPhysicalMacro.Acquire(root,intents);root.ArrivalWork=NavPhysicalMacro.Acquire(root,arrivals);root.Providers=NavPhysicalMacro.Acquire(root,providers);root.Rows=NavPhysicalMacro.Acquire(root,rows);root.Delivered=NavPhysicalMacro.Acquire(root,delivered);
                root.Scan=NavPhysicalMacro.Acquire(root,scan);root.Projection=NavPhysicalMacro.Acquire(root,projected);root.Completion=NavPhysicalMacro.Acquire(root,completed);
                root.Frontier=new CertifiedRouteKernel.PumpFrontier((CertifiedRouteKernel.PumpStage)stage,budget,scans,transfers,ready,projection,allowance,solveLimit,solveConsumed,floor,phaseUsed,scanAllowance);
                if(quantum&&(complete&&stage!=(int)CertifiedRouteKernel.PumpStage.Idle||stage!=(int)CertifiedRouteKernel.PumpStage.Idle&&budget!=grant))throw new ArgumentException("Quantum/frontier mismatch.");
                root.TickQuantum=quantum;root.QuantumGeneration=generation;root.QuantumTick=tick;root.QuantumSerial=serial;root.ConsumedQuantum=consumed;root.AdmissionPrefix=prefix;root.QuantumBudget=grant;root.QuantumCompleted=complete;
                if(accrued)root.lifecycle|=32;root.fairArrivalPolicy=fairArrival;if(replayOnly)root.lifecycle|=16;root.LastServedArrival=lastArrival;root.ArrivalRoundEnd=roundEnd;
                if(fairArrival&&quantum&&stage==(int)CertifiedRouteKernel.PumpStage.ArrivalAdvance&&phaseUsed>1)throw new ArgumentException("Invalid saved arrival preparation count.");
                root.Epoch=epoch;root.Progress=progress;root.NextProviderIdentity=nextProvider;root.SetOperation(operation);root.sealedRoot=true;
                root.ValidateCatalog();return root;
            }catch{throw;}
        }
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){AuthorityGeometry?.Release();Scheduler?.Release();Requests?.Release();ArrivalIntentSets?.Release();ArrivalHistory?.Release();ArrivalIntents?.Release();ArrivalWork?.Release();Providers?.Release();Rows?.Release();Delivered?.Release();Scan?.Release();Projection?.Release();Completion?.Release();Operation.Selector.Owner?.Release();}
    }
    internal sealed class NavigationHostBodyExtension : INavPhysicalBodyExtension
    {
        internal static readonly NavigationHostBodyExtension Instance=new NavigationHostBodyExtension();
        private NavigationHostBodyExtension(){}
        public NavOwnedStorage Read(NavPhysicalTag tag,NavPhysicalBodyReader reader)
            =>tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostEnvelopePublication,value:NavPhysicalValueKind.AccruedAuthorityCreditsV1))?NavigationAccruedHostEnvelope.Read(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostRootFairArrivalV1,value:NavPhysicalValueKind.AccruedAuthorityCreditsV1))?NavigationHostRoot.ReadPhysicalBody(reader,false,true,false,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostEnvelopeTraffic))?NavigationHostEnvelope.ReadPhysicalBody(reader,traffic:true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.TrafficAuthority))?NavigationTrafficAuthority.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityCatalogLedgerLayout))?NavigationAuthorityOwners.ReadPhysicalBody(reader,ledgerLayout:true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityCatalogAnswers))?NavigationAuthorityOwners.ReadPhysicalBody(reader,answerTracking:true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.LegacyProbeAnswer))?NavigationHostAnswer.ReadPhysicalBody(reader,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityCatalogProbes))?NavigationAuthorityOwners.ReadPhysicalBody(reader,true,true,true,true,false,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityCatalogLeases))?NavigationAuthorityOwners.ReadPhysicalBody(reader,true,true,true,true,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalLeaseCatalog))?NavigationArrivalLeaseCatalog.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalLeaseSnapshot))?NavigationArrivalLeaseSnapshot.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityCatalogArrival))?NavigationAuthorityOwners.ReadPhysicalBody(reader,true,true,true,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalCatalog))?NavigationArrivalCatalog.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalAttemptSnapshot,value:NavPhysicalValueKind.OwnedArrivalAnchorV1))?NavigationArrivalAttemptSnapshot.ReadPhysicalBody(reader,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalAttemptSnapshot))?NavigationArrivalAttemptSnapshot.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalProviderSnapshot))?NavigationArrivalProviderSnapshot.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityCatalogRoutes))?NavigationAuthorityOwners.ReadPhysicalBody(reader,true,true,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.InstalledExecution,value:NavPhysicalValueKind.InstalledGeometryMemberV2))?InstalledGeometryMemberV2.Read(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.InstalledExecution,value:NavPhysicalValueKind.InstalledGeometryCertificateV2))?InstalledGeometryCertificateV2.Read(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.InstalledExecution,value:NavPhysicalValueKind.InstalledExecutionV2))?NavigationInstalledExecutionOwner.ReadPhysicalBody(reader,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.InstalledExecution))?NavigationInstalledExecutionOwner.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityCatalogTerrain))?NavigationAuthorityOwners.ReadPhysicalBody(reader,true,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityCatalog))?NavigationAuthorityOwners.ReadPhysicalBody(reader,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityGroup))?NavigationAuthorityGroup.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityMember,value:NavPhysicalValueKind.GroupMarchProgressV2))?NavigationAuthorityMember.ReadPhysicalBody(reader,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.AuthorityMember))?NavigationAuthorityMember.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostAuthority))?NavigationAuthorityOwners.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostEnvelopePublication))?NavigationHostEnvelope.ReadPhysicalBody(reader,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostEnvelope))?NavigationHostEnvelope.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostRootFairArrivalV1,value:NavPhysicalValueKind.ReplayOnlyProjectionV1))?NavigationHostRoot.ReadPhysicalBody(reader,false,true,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostRow,value:NavPhysicalValueKind.ReplayOnlyProjectionV1))?NavigationHostRow.ReadPhysicalBody(reader,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostProjection,value:NavPhysicalValueKind.ReplayOnlyProjectionV1))?NavigationHostProjection.ReadPhysicalBody(reader,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostRootFairArrivalV1))?NavigationHostRoot.ReadPhysicalBody(reader,false,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostRootQuantum))?NavigationHostRoot.ReadPhysicalBody(reader,true):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostRoot))?NavigationHostRoot.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostRequest))?NavigationHostRequest.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostProvider))?NavigationHostProvider.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostRow))?NavigationHostRow.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostAnswer))?NavigationHostAnswer.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostCorridor))?NavigationHostCorridor.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostProjection))?NavigationHostProjection.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostArrivalIntents))?NavigationHostArrivalIntents.ReadPhysicalBody(reader):
              tag.Equals(new NavPhysicalTag(NavPhysicalStorageKind.HostArrivalHistory))?NavigationHostArrivalHistory.ReadPhysicalBody(reader):null;
    }
}

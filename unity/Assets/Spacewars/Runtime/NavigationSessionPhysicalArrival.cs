using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        // Physical attempts are prepared off transportGate. The gameplay
        // dictionaries borrow the root's immutable work/input facades.
        private bool physicalArrivalEnabled=true;
        internal bool TryObservePhysicalArrivalIngress(out NavigationRequest request,out NavigationAdmissionPin observed,
            out NavArrivalInput input,out NavArrivalProposal proposal,out long token,out long attemptId)
        {
            using(EnterTransport()){
                request=null;observed=default;input=null;proposal=null;token=0;attemptId=0;
                if(commandGroup!=null||admissionBatchDepth!=0||!Requests.TryPeek(out var head)||!QuantumIngressAllowed(head)||!Admission.Contains(head))return false;
                var attempt=arrivalAttempts.Values.SingleOrDefault(a=>a.Intents.Any(i=>i.Request==head.Request));
                if(attempt==null||!attempt.Work.IsRegistered||attempt.Retiring||attempt.Reconstructing)return false;
                proposal=attempt.Work.Proposals.SingleOrDefault(p=>p.Member.RequestSequence==head.Request);if(proposal==null)return false;
                request=head;observed=new NavigationAdmissionPin(admissionOwner,admission);input=attempt.Input;token=checked(arrivalLeaseSequence+1);attemptId=attempt.Id;return true;
            }
        }
        internal void PublishRegisteredPhysicalArrival(NavigationRetentionBudget.Scope scope,NavigationHostRoot expected,
            NavigationHostRoot candidate,NavigationAdmission observed,NavigationRequest ingress,NavNameKey key,long attemptId)
        {
            var owned=PublishOwnedPhysicalIngress(scope,expected,candidate,observed,ingress,key,attemptId);if(owned.HasValue){if(!owned.Value)throw new PhysicalIngressRefused("Owned physical arrival ingress refused.");return;}
            using(EnterTransport())UseLegacyRegistrations();if(scope.Prepared==null)scope.Attach(candidate);
            var row=(NavigationHostRow)candidate.Rows[key];
            ValidateScheduledRoute(ingress,row.Scheduled,observed);
            PublishPhysicalHostRoot(scope,expected,candidate,()=>{
                if(!arrivalAttempts.TryGetValue(attemptId,out var attempt)||attempt.Retiring||attempt.Reconstructing||
                    row.ArrivalToken!=arrivalLeaseSequence+1||Admission.PublicationIdentity!=observed.PublicationIdentity||!Admission.Contains(ingress))
                    throw new InvalidOperationException("Physical arrival ingress changed before publication.");
                if(!Requests.TryTransfer(head=>{
                    if(!ReferenceEquals(head,ingress)||!pending.TryGetValue(head.Entity,out var active)||!ReferenceEquals(active,head))return false;
                    var lease=new ArrivalProviderLease{Session=this,Token=row.ArrivalToken,Acquisition=checked(++arrivalAcquisitionSequence),World=head.Session,Attempt=attemptId,
                        Request=head.Request,Entity=head.Entity,Incarnation=head.MemberIdentity.Incarnation,Order=head.Order,Provider=row.Provider,ProviderId=RetainArrivalProvider(row.Provider),Attached=true,Reason=""};
                    lease.Binding=ProjectionLeaseBinding(lease);arrivalProjectionLeases.Add(lease.Token,lease);arrivalLeaseSequence=lease.Token;
                    registeredRoutes.Add(row.Request);registeredGraphKeys.Add(row.Request,row.Scheduled.GraphKey);pending[head.Entity]=row.Request;
                    PublishAdmission();return true;
                },out var transferred)||!ReferenceEquals(transferred,ingress))throw new InvalidOperationException("Physical arrival mailbox changed before publication.");
            });
        }
        // A retired root may be retained only until a replacement for the same
        // exact group command is born. It never participates in fair selection
        // and never publishes its old request. New work pays all compatibility
        // and monotonicity checks before using this historical oracle.
        private bool HistoricalBirthPending(NavigationArrivalWork work)
        {
            var source=work.HistoricalSource;if(!source.IsHistoricalCandidate)return false;
            var members=source.PhysicalInput.Members;if(members.Count==0)return false;var first=members[0];
            if(!groups.TryGetValue(first.GroupId,out var group)||group.CommandSequence!=first.CommandSequence)return false;
            int count=0;foreach(var row in allocating.Values)if(row.MemberIdentity.GroupId==first.GroupId){
                if(arrivalAttempts.Values.Any(a=>!a.Retiring&&a.Intents.Any(i=>i.Request==row.Request)))return false;
                if(!members.Any(m=>m.EntityId==row.Entity))return false;count++;
            }
            return count==members.Count;
        }
        internal bool RetirePhysicalArrivalOwner(bool forceHistorical=false)
        {
            var source=PinCommittedHostRoot();NavigationHostRoot next=null;NavigationRetentionBudget.Scope scope=null;NavigationHostRequest retiredRequest=null;NavDeclarationStorage retiredRecipe=null;
            if(source==null)return false;
            try{
                ArrivalProviderLease lease;using(EnterTransport())lease=FirstRetiringArrivalLease;
                NavNameKey key=default;long obsolete=0;
                if(lease!=null){
                    foreach(var row in source.Rows)if(((NavigationHostRow)row.Value).ArrivalToken==lease.Token){key=row.Key;break;}
                    if(key.Value==null)throw new InvalidOperationException("Retiring physical arrival lost its row.");
                }else{
                    using(EnterTransport())foreach(var entry in source.ArrivalWork)if(!arrivalAttempts.ContainsKey(entry.Key)&&(forceHistorical||!HistoricalBirthPending((NavigationArrivalWork)entry.Value))){obsolete=entry.Key;break;}
                    if(obsolete==0)return false;
                }
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Arrival,source.Epoch,Admission.PublicationIdentity,out scope))return false;
                var reservation=scope.OwnedReservation();next=source.ForkOwned(reservation);
                if(lease!=null)next.StageRetireArrivalRow(key,reservation);else next.StageRetireArrivalWork(obsolete,reservation);
                var f=source.Frontier;
                next.SetFrontier(new CertifiedRouteKernel.PumpFrontier(f.Stage,f.OriginalBudget,f.ScansUsed,f.TransfersUsed,f.ReadyUsed,f.ProjectionUsed,f.ProjectionAllowance,f.SolveWorkLimit,f.SolveConsumed,f.AllocationFloor,f.PhaseUsed+1,f.ScanAllowance),source.Epoch,checked(source.Progress+1));
                next.SealPrepared();scope.Attach(next);
                PublishPhysicalHostRoot(scope,source,next,()=>{
                    if(lease!=null){
                        if(!lease.Retiring||!arrivalProjectionLeases.TryGetValue(lease.Token,out var current)||!ReferenceEquals(current,lease))throw new InvalidOperationException("Retiring arrival owner changed.");
                        var row=(NavigationHostRow)source.Rows[key];if(liveRegistrations!=null){int index=RegistrationIndex(row.Request);if(index<0)throw new InvalidOperationException("Retirement lost its registration.");liveRegistrations.ExtractNoThrow(index,out retiredRequest,out retiredRecipe);}else{registeredGraphKeys.Remove(row.Request);registeredRoutes.Remove(row.Request);}ReleaseArrivalProjection(lease,"retirement");
                    }else if(arrivalAttempts.ContainsKey(obsolete))throw new InvalidOperationException("Obsolete arrival regained an owner.");
                });return true;
            }finally{retiredRequest?.Release();retiredRecipe?.Release();next?.ReleaseStorage();scope?.Discard();source.Release();}
        }
        internal bool PreparePhysicalArrivalAttempt()
        {
            bool ready=PreparePhysicalArrivalAttemptCore(out bool refused,out bool resolvingRefusal);
            // Core has released all failed preparation owners/scopes before a
            // small ordinary retirement publication. Capacity refusal must not
            // retain an orphan forever and prevent the cold fallback birth.
            // A resolving-scope refusal is transient and already registered its
            // wake; retiring historical work here could race its replacement.
            if(!ready&&refused&&!resolvingRefusal)RetirePhysicalArrivalOwner(forceHistorical:true);
            return ready;
        }
        private bool PreparePhysicalArrivalAttemptCore(out bool refused,out bool resolvingRefusal)
        {
            refused=false;resolvingRefusal=false;
            if(!physicalArrivalEnabled)return false;
            NavigationRetentionBudget.Scope scope=null;NavigationHostRoot source=null,next=null;
            NavArrivalInput input=null;NavigationArrivalWork work=null;NavigationPreparedArrivalObservation observation=null;
            NavigationLiveArrivalAttempt preparedAttempt=null;bool installedAttempt=false;
            NavigationLiveArrivalTables observedTables=null,preparedTables=null;int tableRevision=0;long providerSequence=0,preparedProviderSequence=0;
            try{
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Arrival,Generation,Admission.PublicationIdentity,out scope,out resolvingRefusal)){refused=true;return false;}
                var reservation=scope.OwnedReservation();
                long observationBytes;int rowCapacity,memberCapacity,actorCapacity,occupantCapacity;
                using(EnterTransport()){rowCapacity=allocating.Count;memberCapacity=groups.Count==0?0:groups.Values.Max(g=>g.Members.Length);actorCapacity=Crowd.Units.Count;occupantCapacity=checked(3*actorCapacity+pending.Count+arrivalLedger.Count);observationBytes=checked(8192L+2048L*rowCapacity+2048L*actorCapacity+256L*memberCapacity);}
                using var observationScratch=NavStorageScratch.Reserve(reservation,observationBytes);
                observation=NavigationPreparedArrivalObservation.CreateOwned(rowCapacity,memberCapacity,occupantCapacity,reservation);
                NavigationRequest[] rows;GroupOrderState group;NavigationAdmission observed;
                NavigationProfile observedProfile;NavGeometry observedGeometry;NavCrowd observedCrowd;NavRetainedArray<NavNameReference> observedNames;
                NavGeometry basis;long id,operation,attachment;CertifiedRouteKernel worker;
                using(EnterTransport()){
                    if(hostAllocationThread!=0||allocating.Count>rowCapacity||Crowd.Units.Count>actorCapacity||arrivalAttempts.Values.Any(a=>a.Working||!committedHostRoot.FairArrivals&&!a.Retiring&&!a.Published))return false;
                    if(committedHostRoot.FairArrivals&&(committedHostRoot.Frontier.Stage!=CertifiedRouteKernel.PumpStage.ArrivalAdvance||committedHostRoot.Frontier.PhaseUsed!=0))return false;
                    var first=allocating.Values.OrderBy(r=>r.Request).FirstOrDefault(r=>!arrivalAttempts.Values.Any(a=>!a.Retiring&&a.Intents.Any(i=>i.Request==r.Request)));
                    if(first==null||!QuantumIngressAllowed(first))return false;
                    rows=allocating.Values.Where(r=>r.MemberIdentity.GroupId==first.MemberIdentity.GroupId&&QuantumIngressAllowed(r)).OrderBy(r=>r.Request).ToArray();
                    var liveGroup=groups[first.MemberIdentity.GroupId];if(liveGroup.Members.Length>memberCapacity)return false;
                    group=liveGroup.Copy();basis=navigationGeometry;observed=Admission;id=checked(arrivalSequence+1);operation=authorityOperationIdentity;attachment=certifiedHostAttachmentIdentity;worker=certifiedHost;observedTables=liveArrivalTables;observedTables?.Retain();tableRevision=observedTables?.Revision??0;providerSequence=arrivalProviderSequence;
                    observedProfile=profile;observedGeometry=geometry;observedCrowd=Crowd;observedNames=terrainNameOwners;
                    observation.Observe(rows,group);observation.ObserveTerrain(terrain,observedNames);observation.Pin(observedProfile.StorageRoot);observation.Pin(observedGeometry.StorageRoot);observation.Pin(basis.StorageRoot);observation.Pin(observedNames);observation.Pin(admissionOwner);
                    var groupOwner=(groups as NavigationCommandSlots<long,GroupOrderState>)?.OwnerFor(group.GroupId) as NavigationLiveGroup;observation.Pin(groupOwner?.ImmutableVersion);
                    source=PinCommittedHostRoot();if(source==null)throw new InvalidOperationException("Physical arrival creator needs a committed Pump root.");observation.Pin(source);
                }
                // The paid occupancy/source snapshot remains usable while unrelated
                // actors move. Current A intent, HOLD and geometry still guard it;
                // publication/installation rechecks current endpoint occupancy.
                bool ObservationCurrent()=>ReferenceEquals(profile,observedProfile)&&ReferenceEquals(geometry,observedGeometry)&&ReferenceEquals(navigationGeometry,basis)&&ReferenceEquals(Crowd,observedCrowd)&&ReferenceEquals(terrainNameOwners,observedNames)&&rows.All(CurrentArrivalIntent);
                next=source.ForkOwned(reservation);
                double radius=rows.Max(r=>r.Profile.Radius);
                var terms=observedProfile.ForUnit(radius,observedProfile.TankSpeed,observedProfile.TankTurnSpeed);
                double spacing=Math.Max(terms.ArrivalSlotSpacing,4*radius+2*terms.ArrivalTolerance+2*terms.LocalGridCell);
                int limit=(int)Math.Ceiling(observedGeometry.HalfExtent*2/spacing);
                long bound=checked((2L*limit+1)*(2L*limit+1));
                using(var inputScratch=NavStorageScratch.Reserve(reservation,checked(4096L+128L*bound+1024L*rows.Length+1024L*actorCapacity))){
                    var candidates=new System.Collections.Generic.List<NavLocation>();
                    for(int ring=0;ring<=limit;ring++)for(int z=-ring;z<=ring;z++)for(int x=-ring;x<=ring;x++){
                        if(ring>0&&Math.Abs(x)!=ring&&Math.Abs(z)!=ring)continue;
                        var point=new NavPoint(group.OriginalGoal.X+x*spacing,group.OriginalGoal.Z+z*spacing);
                        if(observedGeometry.IsFree(point,0)&&observation.LocatePoint(point,out var at))candidates.Add(at);
                    }
                    if(candidates.Count==0)throw new InvalidOperationException("Accepted arrival has no geometric candidate.");
                    var canonical=next.StageArrivalIntents(id,rows,basis,reservation);
                    var members=canonical.Select(r=>new NavArrivalMember(r.Entity,r.MemberIdentity.Incarnation,r.Order,r.MemberIdentity.MobilityRevision,r.StartLocation.Value,
                        new NavClearanceProfile("ground",1,"ground",(int)r.MemberIdentity.MobilityRevision,r.Profile.Radius),Generation,group.GroupId,group.OrderRevision,group.CommandSequence,group.IssuedTick,
                        r.MemberIdentity.OrderRevision,r.MemberIdentity.OrderRevision,r.Request,r.HeldIdentity)).ToArray();
                    var bindings=new NavArrivalWorkingBinding[rows.Length];
                    // Temporary concrete providers are kept admitted until their
                    // complete owned copy is present in the same scope.
                    long construction=rows.Sum(r=>ProspectiveArrivalProviderBytes(r.Geometry));
                    using(NavStorageScratch.Reserve(reservation,construction)){
                        for(int n=0;n<rows.Length;n++)bindings[n]=new NavArrivalWorkingBinding(rows[n].Entity,ArrivalProvider(canonical[n].Geometry,canonical[n].HeldIdentity),
                            new NavGraphProfile(NavigationAdmission.ExactProfileBinding(canonical[n].Profile),"distance",1,rows[n].Profile.GridCell));
                        using(EnterTransport()){if(!ObservationCurrent()||checked(3*Crowd.Units.Count+pending.Count+arrivalLedger.Count)>occupantCapacity)return false;observation.ObserveOccupancy(CaptureArrivalOccupancySnapshot(members));}
                        var raw=new NavArrivalInput("geometry:"+basis.Revision,"profile:"+NavigationAdmission.ExactProfileBinding(observedProfile),bindings[0].Provider,bindings[0].Profile,terms,
                            new NavGraphCompileLimits(1048576,300000,1200000),new NavArrivalCandidateSource("authority-spiral",1,group.OriginalLocation??candidates[0],candidates),members,
                            observation.Occupancy,1048576,bindings,true);
                        input=NavigationArrivalPhysicalInput.CopyInitial(raw,reservation);
                    }
                    work=NavigationArrivalWork.CreateOwned(input,reservation);work.UseSessionProviderOwnership(true);
                    using(EnterTransport())foreach(var prior in source.ArrivalWork){
                        var previous=(NavigationArrivalWork)prior.Value;
                        if(arrivalAttempts.TryGetValue(prior.Key,out var active)&&!active.Retiring)continue;
                        if(previous.IsHistoricalCandidate&&previous.PhysicalInput.Members[0].GroupId==group.GroupId&&HistoricalBirthPending(previous)){work.AttachHistorical(previous,reservation);break;}
                    }
                    work.SealPrepared();next.StageArrivalWork(id,work,reservation);
                    var rawAttempt=new ArrivalAttempt(true){Id=id,Group=group.GroupId,Input=input,Work=work,Intents=canonical,Epoch=0,History=(NavigationHostArrivalHistory)next.ArrivalHistory[id]};
                    UpdatePhysicalArrivalSnapshot(rawAttempt);
                    preparedAttempt=NavigationLiveArrivalAttempt.CreateOwned(rawAttempt,work,(NavigationHostArrivalIntents)next.ArrivalIntentSets[id],rawAttempt.History,reservation);
                    if(observedTables!=null){
                        int ac=Math.Max(4,Math.Max(observedTables.Attempts.Capacity,observedTables.Count+1));int pc=Math.Max(4,Math.Max(observedTables.Ids.Capacity,observedTables.Ids.Count+preparedAttempt.Providers.Count));
                        preparedTables=NavigationLiveArrivalTables.AllocatePrepared(ac,pc,reservation,observedTables);
                        using(EnterTransport()){if(!ReferenceEquals(liveArrivalTables,observedTables)||observedTables.Revision!=tableRevision||arrivalProviderSequence!=providerSequence)throw new InvalidOperationException("Arrival table source changed.");preparedTables.CopySourceObserved(observedTables);}
                        preparedProviderSequence=providerSequence;
                        for(int i=0;i<preparedAttempt.Providers.Count;i++){
                            var provider=preparedAttempt.Providers[i];if(preparedTables.References.ContainsKey(provider))throw new InvalidOperationException("Fresh physical input reused a live provider owner.");
                            var record=NavigationLiveArrivalProvider.CreateOwned(checked(++preparedProviderSequence),1,provider,reservation);try{preparedTables.AddProviderPreparing(record);}finally{record.ReleaseStorage();}
                        }
                        var created=preparedAttempt.Record;created.InputProviderId=preparedTables.References[created.Input.Provider].Id;for(int i=0;i<created.WorkingProviderIds.Length;i++)created.WorkingProviderIds[i]=preparedTables.References[created.Input.WorkingBindings[i].Provider].Id;
                        preparedTables.AddAttemptPreparing(preparedAttempt);preparedTables.SealPrepared();
                    }
                    if(next.FairArrivals)next.MarkArrivalPrepared();
                    next.SealPrepared();scope.Attach(next);inputScratch.Dispose();observationScratch.Dispose();
                    PublishPhysicalHostRoot(scope,source,next,()=>{
                        if(!ReferenceEquals(liveArrivalTables,observedTables)||observedTables!=null&&(observedTables.Revision!=tableRevision||arrivalProviderSequence!=providerSequence)||arrivalSequence+1!=id||attachment!=certifiedHostAttachmentIdentity||!ReferenceEquals(worker,certifiedHost)||commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=0||
                            !ObservationCurrent())
                            throw new InvalidOperationException("Physical arrival input lost its authority observation.");
                        for(int n=0;n<rows.Length;n++)allocating[rows[n].Entity]=canonical[n];
                        var attempt=preparedAttempt.Record;
                        if(preparedTables!=null){liveArrivalTables=preparedTables;arrivalAttempts=preparedTables.Ordered;arrivalProviders=preparedTables.References;arrivalProviderIds=preparedTables.Ids;arrivalProviderSequence=preparedProviderSequence;}
                        else{OwnArrivalInput(attempt);arrivalAttempts.Add(id,attempt);}arrivalSequence=id;installedAttempt=true;
                    });
                    SignalHost();return true;
                }
            }catch(InvalidOperationException error){if(installedAttempt)throw;refused=IsPhysicalArrivalAdmissionRefusal(error);return false;}
            finally{if(!installedAttempt)preparedTables?.ReleaseStorage();if(!installedAttempt||preparedTables!=null)preparedAttempt?.ReleaseStorage();if(installedAttempt&&preparedTables!=null)observedTables?.Release();observedTables?.Release();observation?.Release();work?.Release();input?.StorageRoot.Release();next?.ReleaseStorage();scope?.Discard();source?.Release();}
        }
        private static void UpdatePhysicalArrivalSnapshot(ArrivalAttempt attempt)
        {
            attempt.Frontier=LiveArrivalFrontier(attempt.Work);attempt.SnapshotStatus=attempt.Work.Status;
            attempt.SnapshotPhase=attempt.Work.WorkPhase;attempt.SnapshotCandidate=attempt.Work.CandidateCursor;
            attempt.SnapshotOccupancy=attempt.Work.OccupancyCursor;attempt.SnapshotMember=attempt.Work.MemberCursor;
        }
        internal Action<string> ArrivalServiceDiagnosticForQa;
        private void TraceArrivalService(string stage){var observer=ArrivalServiceDiagnosticForQa;if(observer!=null)try{observer(stage);}catch{/* Observation cannot change authority. */}}
        internal bool AdvancePhysicalArrivalAuthority(int budget)
        {
            using var measured=PerformanceProfile.Measure(ProfileStage.PhysicalArrival);
            PreparePhysicalArrivalAttempt();
            bool fair;long serial;
            using(EnterTransport()){fair=committedHostRoot.FairArrivals;serial=committedHostRoot.QuantumSerial;}
            long selected=0,roundEnd=0;
            try{if(TryAdvancePhysicalArrivalAuthority(budget,out selected,out roundEnd))return true;}catch(PhysicalIngressRefused){TraceArrivalService("source-owner-refused");return false;}
            return fair&&TryClosePhysicalArrivalService(selected,roundEnd,serial);
        }
        // Existing reservation APIs report these two precise admission failures
        // as InvalidOperationException; retain all invariant/ownership failures.
        private static bool IsPhysicalArrivalAdmissionRefusal(InvalidOperationException error)=>
            error is NavigationRetentionBudget.CapacityRefused||error.Message=="Physical storage exceeds prepared reservation."||error.Message=="Scratch exceeds prepared reservation.";
        // A refused selected attempt consumes this turn, not another attempt's
        // grant. The small cursor publication is itself admitted and atomic.
        private bool TryClosePhysicalArrivalService(long selected,long roundEnd,long serial)
        {
            NavigationHostRoot source=PinCommittedHostRoot(),next=null;NavigationRetentionBudget.Scope scope=null;
            try{
                if(source==null||!source.FairArrivals||source.QuantumSerial!=serial||source.Frontier.Stage!=CertifiedRouteKernel.PumpStage.ArrivalAdvance)return false;
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Arrival,selected,Admission.PublicationIdentity,out scope))return false;
                next=source.ForkOwned(scope.OwnedReservation());next.CommitArrivalService(selected==0?source.LastServedArrival:selected,selected==0?source.ArrivalRoundEnd:roundEnd);
                next.SealPrepared();scope.Attach(next);PublishPhysicalHostRoot(scope,source,next,()=>{});TraceArrivalService("closed-without-advance");return true;
            }catch(InvalidOperationException e)when(IsPhysicalArrivalAdmissionRefusal(e)){return false;}catch(PhysicalIngressRefused){return false;}finally{next?.ReleaseStorage();scope?.Discard();source?.Release();}
        }
        private bool TryAdvancePhysicalArrivalAuthority(int budget,out long selected,out long roundEnd)
        {
            selected=0;roundEnd=0;
            ArrivalAttempt attempt;NavigationHostRoot source=null;ArrivalAdvance step;
            NavigationArrivalWork observedWork;NavigationHostArrivalHistory observedHistory;long observedEpoch;
            NavigationLiveArrivalAttempt observedAttemptOwner;
            using(EnterTransport()){
                bool Eligible(ArrivalAttempt a)=>(!committedHostRoot.AccruedCredits||a.Intents.All(r=>r.Request<=committedHostRoot.AdmissionPrefix))&&a.Work.IsRegistered&&!a.Working&&!a.Retiring&&!a.Reconstructing&&!a.Published&&
                    (a.SnapshotStatus==NavArrivalStatus.Pending||a.SnapshotStatus==NavArrivalStatus.BudgetExhausted);
                if(committedHostRoot.FairArrivals){
                    if(committedHostRoot.Frontier.Stage!=CertifiedRouteKernel.PumpStage.ArrivalAdvance)return false;
                    roundEnd=committedHostRoot.ArrivalRoundEnd;
                    long end=roundEnd;attempt=arrivalAttempts.Values.FirstOrDefault(a=>a.Id>committedHostRoot.LastServedArrival&&a.Id<=end&&Eligible(a));
                    if(attempt==null){
                        roundEnd=arrivalAttempts.Values.Where(Eligible).Select(a=>a.Id).DefaultIfEmpty(0).Max();
                        attempt=arrivalAttempts.Values.FirstOrDefault(Eligible);
                    }
                }else attempt=arrivalAttempts.Values.FirstOrDefault(Eligible);
                if(attempt==null){TraceArrivalService("no-eligible");return false;}selected=attempt.Id;
                observedWork=attempt.Work;observedHistory=attempt.History;observedEpoch=attempt.Epoch;
                observedAttemptOwner=attempt.StorageOwner;
                int allotment=checked(Math.Max(1,budget)*64);
                step=new ArrivalAdvance{Epoch=attempt.Epoch,Allocation=allotment,Graph=allotment,Field=allotment,Connectors=allotment,Bookkeeping=allotment,Maintenance=256,
                    // Idle-cache retirement can change this ordinary allowance
                    // before End refreshes its compatibility scalar. Read the
                    // current immutable root for v2 and for bytes-only restore.
                    External=64L+ArrivalRetainedBytes-attempt.Work.StructuralRetainedBytes+(TickQuantaEnabled?committedHostRoot.Scheduler.RetainedBytes:hostRetainedBytes)+installedCorridorBytes+installedGeometryBytes+answerCorridorBytes};
                // A live attempt observation is not an immutable worker pin.
                // Keep the exact sources alive even if a reentrant command or
                // authority destruction removes their Session edges.
                observedAttemptOwner?.Retain();observedWork.Retain();observedHistory.Retain();attempt.Working=true;observedAttemptOwner?.ChangedNoThrow();
            }
            NavigationHostRoot next=null;NavigationArrivalWork fork=null;NavigationHostArrivalHistory history=null;NavigationRetentionBudget.Scope scope=null;NavigationArrivalWork retiredWork=null;NavigationHostArrivalHistory retiredHistory=null;bool workPin=false,historyPin=false;
            NavOwnedText nextFrontier=null,nextPhase=null,retiredFrontier=null,retiredPhase=null;
            try{
                TraceArrivalService("advance-start");source=PinCommittedHostRoot();if(source==null)return false;
                if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Arrival,attempt.Id,Admission.PublicationIdentity,out scope)){TraceArrivalService("scope-refused");return false;}
                var reservation=scope.OwnedReservation();next=source.ForkOwned(reservation);using(PerformanceProfile.Measure(ProfileStage.ArrivalFork))fork=observedWork.ForkOwned(reservation);
                if(!fork.TryReserveAuthorityBytes(step.External)){TraceArrivalService("authority-bytes-refused");return false;}
                using(PerformanceProfile.Measure(ProfileStage.ArrivalAdvance))fork.Advance(step.Epoch,step.Allocation,new NavEpochQuota(step.Graph,step.Field,step.Connectors,step.Bookkeeping,step.Maintenance));
                PerformanceProfile.Add(ProfileWork.ArrivalConsumed,fork.LastAdvanceConsumed);
                PerformanceProfile.Add(ProfileWork.ArrivalCandidate,fork.CandidateWorkConsumed-observedWork.CandidateWorkConsumed);
                PerformanceProfile.Add(ProfileWork.ArrivalOccupancy,fork.OccupancyWorkConsumed-observedWork.OccupancyWorkConsumed);
                PerformanceProfile.Add(ProfileWork.ArrivalDirect,fork.DirectWorkConsumed-observedWork.DirectWorkConsumed);
                if(fork.LastAdvanceRetentionBlocked){TraceArrivalService("advance-retention-blocked");return false;}
                BeforeArrivalAdvanceCommit?.Invoke(fork.Status);using(PerformanceProfile.Measure(ProfileStage.ArrivalSeal))fork.SealPrepared();history=observedHistory.AppendOwned(step,reservation);next.StageArrivalHistory(attempt.Id,history,reservation);next.StageArrivalWork(attempt.Id,fork,reservation);
                string frontier,phase;
                using(PerformanceProfile.Measure(ProfileStage.ArrivalFrontier))
                using(NavStorageScratch.Reserve(reservation,checked(4096L+512L*(fork.Proposals.Count+fork.MemberOutcomes.Count)))){TraceArrivalService("frontier-start");frontier=LiveArrivalFrontier(fork);phase=fork.WorkPhase;TraceArrivalService("frontier-end");}
                if(observedAttemptOwner!=null){nextFrontier=NavOwnedText.Copy(frontier,reservation);nextPhase=NavOwnedText.Copy(phase,reservation);frontier=nextFrontier.Value;phase=nextPhase.Value;}
                var status=fork.Status;int candidate=fork.CandidateCursor,occupancy=fork.OccupancyCursor,member=fork.MemberCursor;
                next.SetFrontier(source.Frontier,source.Epoch,checked(source.Progress+1));if(next.FairArrivals)next.CommitArrivalService(attempt.Id,roundEnd);next.SealPrepared();scope.Attach(next);fork.Retain();workPin=true;history.Retain();historyPin=true;
                TraceArrivalService("publish-start");PublishPhysicalHostRoot(scope,source,next,()=>{
                    if(!arrivalAttempts.TryGetValue(attempt.Id,out var current)||!ReferenceEquals(current,attempt)||!ReferenceEquals(attempt.StorageOwner,observedAttemptOwner)||!attempt.Working||attempt.Epoch!=observedEpoch||
                        !ReferenceEquals(attempt.Work,observedWork)||!ReferenceEquals(attempt.History,observedHistory)||!ReferenceEquals(observedWork,source.ArrivalWork[attempt.Id])||!ReferenceEquals(observedHistory,source.ArrivalHistory[attempt.Id]))
                        throw new PhysicalIngressRefused("Physical arrival advance lost its owner.");
                    retiredWork=attempt.OwnedWork;retiredHistory=attempt.OwnedHistory;attempt.OwnedWork=fork;attempt.OwnedHistory=history;workPin=historyPin=false;attempt.Work=fork;attempt.History=history;attempt.Epoch++;
                    if(observedAttemptOwner!=null){observedAttemptOwner.Work=fork;observedAttemptOwner.History=history;retiredFrontier=observedAttemptOwner.Frontier;retiredPhase=observedAttemptOwner.Phase;observedAttemptOwner.Frontier=nextFrontier;observedAttemptOwner.Phase=nextPhase;nextFrontier=nextPhase=null;}
                    attempt.Frontier=frontier;attempt.SnapshotPhase=phase;attempt.SnapshotStatus=status;attempt.SnapshotCandidate=candidate;attempt.SnapshotOccupancy=occupancy;attempt.SnapshotMember=member;
                    observedAttemptOwner?.ChangedNoThrow();
                });
                TraceArrivalService("committed");return true;
            }catch(InvalidOperationException e)when(IsPhysicalArrivalAdmissionRefusal(e)){TraceArrivalService("prepared-admission-refused");return false;}finally{TraceArrivalService("release-start");nextFrontier?.Release();nextPhase?.Release();retiredFrontier?.Release();retiredPhase?.Release();if(workPin)fork?.Release();if(historyPin)history?.Release();retiredWork?.Release();retiredHistory?.Release();history?.Release();next?.ReleaseStorage();fork?.Release();scope?.Discard();source?.Release();try{using(EnterTransport()){attempt.Working=false;observedAttemptOwner?.ChangedNoThrow();PruneArrivalAttempts();}}finally{observedWork.Release();observedHistory.Release();observedAttemptOwner?.Release();TraceArrivalService("release-end");}}
        }
    }
}

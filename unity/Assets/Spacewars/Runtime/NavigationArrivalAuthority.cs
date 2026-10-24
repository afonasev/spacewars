using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Authority attempts retain their immutable input and exact logical allotments.
    // Derivative graphs can be rebuilt under the frozen barrier without issuing a
    // new command or spending the restored logical quota a second time.
    public sealed class NavigationArrivalReservation
    {
        public long CommitOrder, Id, World, Group, Root, Command, IssuedTick, Activation, Entity, Incarnation, Mobility, Request, Order;
        public NavLocation Endpoint; public double Radius; public string Proof, Provider, Profile, Hold;
        public NavArrivalReservationKind State;
        internal NavigationArrivalReservation Copy() => (NavigationArrivalReservation)MemberwiseClone();
    }
    internal sealed class ArrivalProjection
    {
        internal int Leg, Replay;
        internal NavLocation Expected, At;
        internal NavPoint[] Points;
        internal string[] Typed, Observed;
    }
    internal sealed class ArrivalProviderLease : INavStorageFacade
    {
        internal NavigationSession.NavigationLiveArrivalLease StorageOwner;
        NavOwnedStorage INavStorageFacade.StorageRoot=>StorageOwner;
        internal NavigationSession Session;
        internal long Token, Acquisition, Retirement, World, Attempt, Request, Entity, Incarnation, Order, ProviderId;
        internal bool Retiring, Attached;
        internal string Reason, Binding;
        internal ILayeredNavigationProvider Provider;
        internal CertifiedRouteKernel.Registered Projection;
        internal int PointsCapacity,TypedCapacity,ObservedCapacity;
        internal long StagingBytes;
    }
    internal sealed class ArrivalProjectionDelta : IDisposable
    {
        internal NavigationSession Session;internal ArrivalProviderLease Lease;internal ArrivalProjection Prefix;
        internal int PointsBound,TypedBound,ObservedBound;
        public void Dispose(){Session?.ReleaseArrivalProjectionDelta(this);}
    }
    public sealed partial class NavigationSession
    {
        internal struct ArrivalAdvance
        { internal long Epoch, External; internal int Allocation,Graph,Field,Connectors,Bookkeeping,Maintenance; }
        internal sealed class ArrivalAttempt : INavStorageFacade
        {
            internal NavigationLiveArrivalAttempt StorageOwner;
            NavOwnedStorage INavStorageFacade.StorageRoot=>StorageOwner;
            internal ArrivalAttempt(bool physical=false){if(!physical){LegacyJournal=new List<ArrivalAdvance>();Projections=new Dictionary<long,ArrivalProjection>();}}
            internal ArrivalAttempt CopyHeader()=>(ArrivalAttempt)MemberwiseClone();
            internal long Id, Group, Epoch;
            internal NavArrivalInput Input;
            internal NavigationArrivalWork Work,OwnedWork;
            internal NavigationHostArrivalIntents OwnedIntents;internal NavigationHostArrivalHistory OwnedHistory;
            internal NavigationRequest[] Intents;
            internal List<ArrivalAdvance> LegacyJournal;
            internal NavigationHostArrivalHistory History;
            internal IReadOnlyList<ArrivalAdvance> Journal=>History!=null?(IReadOnlyList<ArrivalAdvance>)History:LegacyJournal;
            internal Dictionary<long,ArrivalProjection> Projections;
            internal int ReplayCursor; internal bool Reconstructing, Published;
            internal bool Retiring, LegacyProviderAccounting, RestoredRetiringCompute;
            internal long Retirement;
            internal string RetirementReason;
            internal long InputProviderId; internal long[] WorkingProviderIds;
            internal ILayeredNavigationProvider[] OwnedProviders;
            internal string Frontier, SnapshotPhase; internal int SnapshotCandidate, SnapshotOccupancy, SnapshotMember; internal bool Working; internal NavArrivalStatus SnapshotStatus;
        }
        private sealed class ProviderIdentityComparer : IEqualityComparer<ILayeredNavigationProvider>
        {
            internal static readonly ProviderIdentityComparer Instance=new ProviderIdentityComparer();
            public bool Equals(ILayeredNavigationProvider a,ILayeredNavigationProvider b)=>ReferenceEquals(a,b);
            public int GetHashCode(ILayeredNavigationProvider p)=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p);
        }
        internal sealed class ArrivalProviderRecord : INavStorageFacade
        {internal NavigationLiveArrivalProvider StorageOwner;NavOwnedStorage INavStorageFacade.StorageRoot=>StorageOwner;internal long Id;internal int References;internal ILayeredNavigationProvider Provider;}
        private IDictionary<ILayeredNavigationProvider,ArrivalProviderRecord> arrivalProviders;
        private IDictionary<long,ArrivalProviderRecord> arrivalProviderIds;
        private IDictionary<long,ArrivalProviderLease> arrivalProjectionLeases=new Dictionary<long,ArrivalProviderLease>();
        private SortedDictionary<long,ArrivalProviderLease> arrivalRetirements=new SortedDictionary<long,ArrivalProviderLease>();
        private long arrivalProviderSequence,arrivalLeaseSequence,arrivalAcquisitionSequence,arrivalRetirementSequence,arrivalReleaseSequence;
        internal long ArrivalProviderOwnedBytes=>arrivalProviderIds.Values.Sum(p=>p.Provider.EstimatedRetainedBytes)+
            64L*(ArrivalProviderCapacity(arrivalProviders)+ArrivalProviderCapacity(arrivalProviderIds))+128L*arrivalProviderIds.Count+
            128L*ArrivalLeaseCapacity+512L*arrivalProjectionLeases.Count+
            arrivalProjectionLeases.Values.Sum(l=>1024L+16L*l.PointsCapacity+8L*(l.TypedCapacity+l.ObservedCapacity)+l.StagingBytes);
        private long RetainArrivalProvider(ILayeredNavigationProvider provider,long expectedId=0)
        {
            if(arrivalProviders.TryGetValue(provider,out var existing)){
                if(expectedId!=0&&existing.Id!=expectedId)throw new ArgumentException("Inconsistent physical provider alias.");if(liveArrivalTables!=null)liveArrivalTables.ChangeProviderReferencesNoThrow(existing,checked(existing.References+1));else existing.References++;return existing.Id;
            }
            if(liveArrivalTables!=null)throw new InvalidOperationException("A live provider must be prepared before publication.");
            long id=expectedId==0?checked(++arrivalProviderSequence):expectedId;
            if(id<1||id>arrivalProviderSequence||arrivalProviderIds.ContainsKey(id))throw new ArgumentException("Invalid physical provider identity.");
            var entry=new ArrivalProviderRecord{Id=id,References=1,Provider=provider};arrivalProviders.Add(provider,entry);arrivalProviderIds.Add(id,entry);return id;
        }
        private void ReleaseArrivalProvider(ILayeredNavigationProvider provider)
        {
            if(provider==null||!arrivalProviders.TryGetValue(provider,out var entry)||entry.References<1)throw new InvalidOperationException("Unknown provider owner release.");
            if(liveArrivalTables!=null){
                if(entry.References>1){liveArrivalTables.ChangeProviderReferencesNoThrow(entry,entry.References-1);return;}
                if(!liveArrivalTables.CanExtractProvider(entry,out int reference,out int id))throw new InvalidOperationException("Invalid live arrival provider retirement.");
                var retired=liveArrivalTables.ExtractProviderNoThrow(entry,reference,id);arrivalProviders=liveArrivalTables.References;arrivalProviderIds=liveArrivalTables.Ids;RetireArrivalProviderNoThrow(retired);return;
            }
            if(--entry.References!=0)return;arrivalProviders.Remove(provider);arrivalProviderIds.Remove(entry.Id);entry.Provider=null;
            if(arrivalProviders.Count==0){arrivalProviders=new Dictionary<ILayeredNavigationProvider,ArrivalProviderRecord>(ProviderIdentityComparer.Instance);arrivalProviderIds=new Dictionary<long,ArrivalProviderRecord>();}
        }
        private void OwnArrivalInput(ArrivalAttempt attempt)
        {
            var providers=attempt.StorageOwner!=null?attempt.OwnedProviders:new[]{attempt.Input.Provider}.Concat(attempt.Input.WorkingBindings.Select(b=>b.Provider)).Distinct(ProviderIdentityComparer.Instance).ToArray();
            attempt.OwnedProviders=providers;
            foreach(var provider in providers)RetainArrivalProvider(provider);
            attempt.InputProviderId=arrivalProviders[attempt.Input.Provider].Id;
            if(attempt.StorageOwner==null)attempt.WorkingProviderIds=attempt.Input.WorkingBindings.Select(b=>arrivalProviders[b.Provider].Id).ToArray();
            else for(int i=0;i<attempt.WorkingProviderIds.Length;i++)attempt.WorkingProviderIds[i]=arrivalProviders[attempt.Input.WorkingBindings[i].Provider].Id;
            if(!attempt.Work.IsImmutableCaptureRoot)attempt.Work.UseSessionProviderOwnership(!attempt.LegacyProviderAccounting);
        }
        private long ProspectiveArrivalProviderBytes(NavGeometry value)
            =>NavigationProviderConstructionBound.Estimate(terrain,value);
        private static string ProjectionLeaseBinding(ArrivalProviderLease lease)=>NavContract.Digest(w=>{
            w.Write(lease.Token);w.Write(lease.Acquisition);w.Write(lease.World);w.Write(lease.Attempt);w.Write(lease.Request);w.Write(lease.Entity);w.Write(lease.Incarnation);w.Write(lease.Order);w.Write(lease.ProviderId);
        });
        private ArrivalProviderLease AcquireArrivalProjection(ArrivalAttempt attempt,NavigationRequest request,NavScheduledRequest scheduled,NavTypedRoute route)
        {
            var prior=arrivalProjectionLeases.Values.SingleOrDefault(l=>l.Request==request.Request&&l.World==request.Session);
            if(prior!=null){if(prior.Retiring||prior.Attached||!ReferenceEquals(prior.Provider,scheduled.Provider))throw new InvalidOperationException("Invalid projection owner reacquisition.");prior.Attached=true;prior.Projection.Request=request;prior.Projection.Scheduled=scheduled;prior.Projection.Solved=new NavScheduledCompletion(scheduled,NavSolveStatus.Ready,route,0,0);return prior;}
            var lease=new ArrivalProviderLease{Session=this,Token=checked(++arrivalLeaseSequence),Acquisition=checked(++arrivalAcquisitionSequence),
                World=request.Session,Attempt=attempt.Id,Request=request.Request,Entity=request.Entity,Incarnation=request.MemberIdentity.Incarnation,Order=request.Order,
                Provider=scheduled.Provider,ProviderId=RetainArrivalProvider(scheduled.Provider),Attached=true,Reason=""};
            lease.Binding=ProjectionLeaseBinding(lease);arrivalProjectionLeases.Add(lease.Token,lease);
            // The successful capture already owns zero-progress projection state.
            if(attempt.Projections==null)attempt.Projections=new Dictionary<long,ArrivalProjection>();
            if(!attempt.Projections.ContainsKey(request.Request))attempt.Projections.Add(request.Request,new ArrivalProjection{Leg=0,Replay=0,Expected=route.Subscription.Origin,At=route.Subscription.Origin,
                Points=Array.Empty<NavPoint>(),Typed=Array.Empty<string>(),Observed=Array.Empty<string>()});
            lease.Projection=CertifiedRouteKernel.CreateArrivalProjection(request,scheduled,route,attempt.Projections[request.Request],lease,RoutePort,terrain?.Id??NavLocation.FlatSurface);
            return lease;
        }
        private bool CurrentProjectionLease(ArrivalProviderLease lease)=>lease!=null&&ReferenceEquals(lease.Session,this)&&lease.World==Generation&&lease.Attached&&
            arrivalProjectionLeases.TryGetValue(lease.Token,out var current)&&ReferenceEquals(current,lease)&&lease.Projection!=null;
        private static int ProjectionCapacity(int capacity,int count)=>count<=capacity?capacity:Math.Max(count,capacity==0?4:checked(capacity*2));
        internal bool TryPrepareArrivalProjectionDelta(ArrivalProviderLease lease,bool point,bool typed,int observedExtra,long traceScratch,out ArrivalProjectionDelta delta)
        {
            using(EnterTransport()){
                delta=null;if(!CurrentProjectionLease(lease)||lease.Retiring)return false;
                if(lease.StagingBytes!=0)throw new InvalidOperationException("Concurrent arrival projection delta.");
                var prefix=arrivalAttempts[lease.Attempt].Projections[lease.Request];
                int points=checked(prefix.Points.Length+(point?1:0)),types=checked(prefix.Typed.Length+(typed?1:0)),observed=checked(prefix.Observed.Length+observedExtra);
                int pc=ProjectionCapacity(lease.PointsCapacity,points),tc=ProjectionCapacity(lease.TypedCapacity,types),oc=ProjectionCapacity(lease.ObservedCapacity,observed);
                // Reserve complete new immutable snapshot plus the full replacement
                // backing arrays and bounded trace scratch while old buffers live.
                long staging=checked(128L+16L*points+128L*(types+observed)+
                    (pc==lease.PointsCapacity?0:16L*pc)+(tc==lease.TypedCapacity?0:8L*tc)+(oc==lease.ObservedCapacity?0:8L*oc)+traceScratch);
                if(ArrivalRetainedBytes+hostRetainedBytes+installedCorridorBytes+installedGeometryBytes+answerCorridorBytes+staging>LayeredNavigationScheduler.MaxRetainedBytes)return false;
                lease.StagingBytes=staging;delta=new ArrivalProjectionDelta{Session=this,Lease=lease,Prefix=prefix,PointsBound=points,TypedBound=types,ObservedBound=observed};return true;
            }
        }
        internal void ReleaseArrivalProjectionDelta(ArrivalProjectionDelta delta)
        {using(EnterTransport()){var lease=delta.Lease;delta.Prefix=null;delta.Lease=null;delta.Session=null;if(CurrentProjectionLease(lease))lease.StagingBytes=0;}}
        internal bool CommitArrivalProjectionDelta(ArrivalProjectionDelta delta,ArrivalProjection next)
        {
            using(EnterTransport()){
                var lease=delta.Lease;if(!CurrentProjectionLease(lease)||lease.Retiring)return false;
                var attempt=arrivalAttempts[lease.Attempt];var row=lease.Projection;var prior=delta.Prefix;
                if(!ReferenceEquals(attempt.Projections[lease.Request],prior)||row.LegCursor!=prior.Leg||row.ReplayCursor!=prior.Replay)return false;
                if(next.Points.Length>delta.PointsBound||next.Typed.Length>delta.TypedBound||next.Observed.Length>delta.ObservedBound||
                    next.Points.Length<prior.Points.Length||next.Typed.Length<prior.Typed.Length||next.Observed.Length<prior.Observed.Length)throw new InvalidOperationException("Projection delta exceeded reserved recipe.");
                int pc=ProjectionCapacity(lease.PointsCapacity,next.Points.Length),tc=ProjectionCapacity(lease.TypedCapacity,next.Typed.Length),oc=ProjectionCapacity(lease.ObservedCapacity,next.Observed.Length);
                row.Points.Capacity=pc;row.TypedPortals.Capacity=tc;row.ObservedPortals.Capacity=oc;
                for(int n=prior.Points.Length;n<next.Points.Length;n++)row.Points.Add(next.Points[n]);
                for(int n=prior.Typed.Length;n<next.Typed.Length;n++)row.TypedPortals.Add(next.Typed[n]);
                for(int n=prior.Observed.Length;n<next.Observed.Length;n++)row.ObservedPortals.Add(next.Observed[n]);
                row.LegCursor=next.Leg;row.ReplayCursor=next.Replay;row.Expected=next.Expected;row.At=next.At;
                lease.PointsCapacity=pc;lease.TypedCapacity=tc;lease.ObservedCapacity=oc;attempt.Projections[lease.Request]=next;lease.StagingBytes=0;return true;
            }
        }
        private ArrivalProviderLease FirstRetiringArrivalLease=>liveArrivalLeaseTable!=null?liveArrivalLeaseTable.RetirementHead?.Record:arrivalRetirements.Count==0?null:arrivalRetirements.First().Value;
        internal ArrivalProviderLease NextRetiringArrivalProjection(){using(EnterTransport())return FirstRetiringArrivalLease;}
        internal long LastOwnedLeaseReleaseAllocatedBytes{get;private set;}=-1;
        internal void ReleaseArrivalProjection(ArrivalProviderLease lease,string reason)
        {
            using(EnterTransport()){
                if(lease==null||!ReferenceEquals(lease.Session,this)||lease.World!=Generation||!arrivalProjectionLeases.TryGetValue(lease.Token,out var current)||!ReferenceEquals(current,lease)||!lease.Attached)
                    throw new InvalidOperationException("Unknown or duplicate arrival projection release.");
                if(lease.StagingBytes!=0)throw new InvalidOperationException("Prepared arrival projection must be disposed before owner release.");
                if(liveArrivalLeaseTable!=null){
                    if(arrivalReleaseSequence==long.MaxValue||!liveArrivalLeaseTable.CanExtract(lease,out int slot,out int acquisition)||!arrivalProviders.TryGetValue(lease.Provider,out var record)||!liveArrivalTables.TryProviderSlots(record,out _,out _)||record.References==1&&!liveArrivalTables.CanExtractProvider(record,out _,out _))throw new InvalidOperationException("Invalid owned lease release source.");
                    long before=GC.GetAllocatedBytesForCurrentThread();var ownedProvider=lease.Provider;var owner=liveArrivalLeaseTable.ExtractNoThrow(slot,acquisition);if(liveArrivalLeaseTable.Count==0){owner.RetiredLeaseTable=liveArrivalLeaseTable;liveArrivalLeaseTable=liveArrivalLeaseTable.TakeEmptyResetNoThrow();arrivalProjectionLeases=liveArrivalLeaseTable.Ordered;}
                    arrivalReleaseSequence++;ReleaseArrivalProvider(ownedProvider);RetireArrivalLeaseNoThrow(owner);PruneArrivalAttempts();LastOwnedLeaseReleaseAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;return;
                }
                arrivalProjectionLeases.Remove(lease.Token);if(lease.Retiring)arrivalRetirements.Remove(lease.Retirement);var provider=lease.Provider;lease.Provider=null;lease.Projection=null;lease.Attached=false;arrivalReleaseSequence++;
                ReleaseArrivalProvider(provider);
                if(arrivalProjectionLeases.Count==0)arrivalProjectionLeases=new Dictionary<long,ArrivalProviderLease>();
                PruneArrivalAttempts();
            }
        }
        internal bool RetireRestoredArrivalProjection()
        {
            using(EnterTransport()){
                var lease=FirstRetiringArrivalLease;
                if(lease?.Attached==true)return false;
                if(lease!=null){lease.Attached=true;ReleaseArrivalProjection(lease,"restored-retirement");return true;}
                ArrivalAttempt compute=null;
                if(liveArrivalTables!=null){for(int i=0;i<liveArrivalTables.Count;i++){var candidate=liveArrivalTables.Attempts[liveArrivalTables.SortedId(i)];if(candidate.RestoredRetiringCompute&&!candidate.Reconstructing&&!candidate.Working&&(compute==null||candidate.Retirement<compute.Retirement))compute=candidate;}}
                else compute=arrivalAttempts.Values.Where(a=>a.RestoredRetiringCompute&&!a.Reconstructing&&!a.Working).OrderBy(a=>a.Retirement).FirstOrDefault();
                if(compute==null)return false;compute.RestoredRetiringCompute=false;compute.StorageOwner?.ChangedNoThrow();PruneArrivalAttempts();return true;
            }
        }
        private long arrivalSequence,arrivalCommitSequence;
        private IDictionary<int,NavigationRequest> allocating = new AuthoritySlotMap<int,NavigationRequest>();
        private IDictionary<long,ArrivalAttempt> arrivalAttempts;
        private IDictionary<long,NavigationArrivalReservation> arrivalLedger = new AuthoritySlotMap<long,NavigationArrivalReservation>();
        public IReadOnlyList<NavigationArrivalReservation> ArrivalReservations
        { get { using(EnterTransport()) return arrivalLedger.Values.OrderBy(x=>x.CommitOrder).Select(x=>x.Copy()).ToArray(); } }
        public int AllocationCount {get {using(EnterTransport())return allocating.Count;}}
        internal bool HasReadyArrival {get {using(EnterTransport())return arrivalAttempts.Values.Any(a=>!a.Retiring&&!a.Working&&!a.Reconstructing&&!a.Published&&a.SnapshotStatus!=NavArrivalStatus.Pending&&a.SnapshotStatus!=NavArrivalStatus.BudgetExhausted);}}
        internal bool ArrivalReconstructing => arrivalAttempts.Values.Any(a=>a.Reconstructing);
        internal Action<NavArrivalStatus> BeforeArrivalAdvanceCommit;
        internal event Action<NavigationRequest> EndpointAssigned;
        internal string[] ArrivalPhases {get {using(EnterTransport())return arrivalAttempts.Values.Select(a=>a.SnapshotPhase+":"+a.SnapshotCandidate+":"+a.SnapshotOccupancy+":"+a.SnapshotMember).ToArray();}}
        // Diagnostic-only snapshot of fields published by the authority. Never
        // follows mutable ArrivalWork or worker state from the observing thread.
        internal string ArrivalAuthoritySnapshot
        {
            get{
                string header;long[] attemptIds;NavigationHostRoot root;
                using(EnterTransport()){
                    root=committedHostRoot;
                    var attempts=arrivalAttempts.Values.OrderBy(a=>a.Id).ToArray();attemptIds=attempts.Select(a=>a.Id).ToArray();
                    string hostState=root==null?"host=none":"hostFrontier="+root.Frontier.Stage+" hostOperation="+root.Operation.Stage+
                        " hostQuantum="+root.QuantumRunnable+" quantumEnabled="+root.TickQuantum+" quantumTick="+root.QuantumTick+
                        " quantumSerial="+root.QuantumSerial+" consumedQuantum="+root.ConsumedQuantum;
                    header="allocating="+allocating.Count+" attempts="+attempts.Length+" "+hostState+" "+string.Join(";",attempts.Select(a=>
                        a.Id+":"+a.Group+" epoch="+a.Epoch+" status="+a.SnapshotStatus+" phase="+a.SnapshotPhase+
                        " candidate="+a.SnapshotCandidate+" occupancy="+a.SnapshotOccupancy+" member="+a.SnapshotMember+
                        " working="+a.Working+" reconstructing="+a.Reconstructing+" retiring="+a.Retiring+" published="+a.Published));
                    // The transport gate keeps the root stable until all header
                    // allocations have completed; pin only before leaving it.
                    root?.Retain();
                }
                try{
                    if(attemptIds.Length==0)return header;
                    if(root==null)return header+" routeWork="+string.Join(";",attemptIds.Select(id=>id+":work=unpublished"));
                    var workDiagnostics=new string[attemptIds.Length];
                    for(int i=0;i<attemptIds.Length;i++){
                        long id=attemptIds[i];
                        if(!root.ArrivalWork.TryGetValue(id,out var storage)||!(storage is NavigationArrivalWork work)||!work.IsImmutableCaptureRoot){workDiagnostics[i]=id+":work=unpublished";continue;}
                        var counters=work.RouteCounters;
                        workDiagnostics[i]=id+":pendingRoutes="+work.PendingRouteCount+" readyRoutes="+work.ReadyRouteCount+
                            " LogicalGraphWork="+counters.LogicalGraphWork+" PhysicalGraphWork="+counters.PhysicalGraphWork+
                            " LogicalFieldWork="+counters.LogicalFieldWork+" PhysicalFieldWork="+counters.PhysicalFieldWork+
                            " ConnectorWork="+counters.ConnectorWork+" MaintenanceWork="+counters.MaintenanceWork+
                            " route="+work.RetentionBreakdown;
                    }
                    return header+" routeWork="+string.Join(";",workDiagnostics);
                }finally{root?.Release();}
            }
        }
        private bool AdmitArrivalGroup(int[] ids,NavPoint[] goals)
        {
            if(commandGroup==null || (commandGroup.Kind!=PlayableCommandKind.Move && commandGroup.Kind!=PlayableCommandKind.AttackMove))return false;
            foreach(int index in Enumerable.Range(0,ids.Length).OrderBy(i=>ids[i])) {
                int id=ids[index];var actor=Crowd.Units.Single(u=>u.Id==id);
                Crowd.Locate(goals[index],actor.Radius,out var location);
                orders.TryGetValue(id,out var order);
                var intent=new NavigationRequest(Generation,++requestSequence,id,order+1,
                    profile.ForUnit(actor.Radius,actor.Speed,actor.TurnSpeed),MovementGeometry(id),actor.Position,goals[index],
                    navigationGeometry,HeldIdentity(id),RequestIdentity(actor),actor.Location,location);
                CancelArrival(id,false);pending.Remove(id);reservations.Remove(id);allocating[id]=intent;orders[id]=intent.Order;
                retainedGoals[id]=intent.Goal;movementIdentities[id]=intent.HeldIdentity;ClearFailure(id);
            }
            return true;
        }
        internal Action BeforeArrivalProviderConstruction;
        private ILayeredNavigationProvider ArrivalProvider(NavGeometry value,string hold)
        {
            BeforeArrivalProviderConstruction?.Invoke();
            return terrain==null ? NavigationAdmission.CertifiedFlatProvider(value) : new PartitionNavigationProvider(terrain,value,hold);
        }
        private long ProspectiveInputOwnerMetadata(int added)
        {
            long pc=ArrivalProviderCapacity(arrivalProviders),ic=ArrivalProviderCapacity(arrivalProviderIds),count=arrivalProviderIds.Count+added;
            // Conservative rounded dictionary growth, reserved before publication.
            return checked(64L*((count<=pc?pc:3*count+4)-pc+(count<=ic?ic:3*count+4)-ic)+128L*added);
        }
        private bool CurrentArrivalIntent(NavigationRequest r)
            => allocating.TryGetValue(r.Entity,out var active)&&ReferenceEquals(active,r)&&CurrentMember(r)&&
                (ReferenceEquals(r.BaseGeometry,navigationGeometry)||committedHostRoot?.AuthorityGeometry!=null&&ReferenceEquals(r.BaseGeometry.StorageRoot,committedHostRoot.AuthorityGeometry))&&r.ProfileId==profile.ProfileId&&r.ProfileRevision==profile.Revision&&
                CurrentHeldBinding(r.HeldIdentity,r.Entity);
        private void EnsureArrivalAttempts()
        {
            if(physicalArrivalEnabled)return;
            if(hostAllocationThread!=0||arrivalAttempts.Values.Any(a=>a.Working||!a.Retiring&&!a.Published))return;
            var covered=new HashSet<long>(arrivalAttempts.Values.Where(a=>!a.Retiring&&!a.Published).SelectMany(a=>a.Intents).Select(r=>r.Request));
            foreach(var cohort in allocating.Values.Where(r=>!covered.Contains(r.Request)).OrderBy(r=>r.Request)
                .GroupBy(r=>r.MemberIdentity.GroupId).ToArray()) {
                var group=groups[cohort.Key];var rows=cohort.OrderBy(r=>r.Request).ToArray();
                double radius=rows.Max(r=>r.Profile.Radius);
                var terms=profile.ForUnit(radius,profile.TankSpeed,profile.TankTurnSpeed);
                double spacing=Math.Max(terms.ArrivalSlotSpacing,4*radius+2*terms.ArrivalTolerance+2*terms.LocalGridCell);
                int limit=(int)Math.Ceiling(geometry.HalfExtent*2/spacing);
                long candidateBound=checked((2L*limit+1)*(2L*limit+1));
                long recipeBytes=checked(128L*candidateBound+4096+512L*rows.Length+256L*(3L*Crowd.Units.Count+arrivalLedger.Count));
                long external=ArrivalRetainedBytes+hostRetainedBytes+installedCorridorBytes+installedGeometryBytes+answerCorridorBytes;
                // Temporary pressure keeps the accepted intent and its journal intact.
                // Each provider's construction bound is released into its actual
                // retained size before reserving the next physical instance.
                if(external+recipeBytes+ProspectiveInputOwnerMetadata(rows.Length)+ProspectiveArrivalProviderBytes(rows[0].Geometry)>LayeredNavigationScheduler.MaxRetainedBytes)return;
                var candidates=new List<NavLocation>();
                // Exact existing spiral/extent. This remains finite preparation;
                // per-leg CPU accounting is a separate A5 qualification.
                for(int ring=0;ring<=limit;ring++)for(int z=-ring;z<=ring;z++)for(int x=-ring;x<=ring;x++){
                    if(ring>0&&Math.Abs(x)!=ring&&Math.Abs(z)!=ring)continue;
                    var point=new NavPoint(group.OriginalGoal.X+x*spacing,group.OriginalGoal.Z+z*spacing);
                    if(geometry.IsFree(point,0)&&Crowd.Locate(point,0,out var at))candidates.Add(at);
                }
                if(candidates.Count==0)throw new InvalidOperationException("Accepted arrival has no geometric candidate.");
                var members=rows.Select(r=>new NavArrivalMember(r.Entity,r.MemberIdentity.Incarnation,r.Order,
                    r.MemberIdentity.MobilityRevision,r.StartLocation.Value,
                    new NavClearanceProfile("ground",1,"ground",(int)r.MemberIdentity.MobilityRevision,r.Profile.Radius),
                    Generation,group.GroupId,group.OrderRevision,group.CommandSequence,group.IssuedTick,
                    r.MemberIdentity.OrderRevision,r.MemberIdentity.OrderRevision,r.Request,r.HeldIdentity)).ToArray();
                var bindings=new NavArrivalWorkingBinding[rows.Length];long stagedBytes=0;
                for(int n=0;n<rows.Length;n++){
                    var r=rows[n];long construction=ProspectiveArrivalProviderBytes(r.Geometry);
                    if(external+recipeBytes+ProspectiveInputOwnerMetadata(rows.Length)+stagedBytes+construction>LayeredNavigationScheduler.MaxRetainedBytes)return;
                    var provider=ArrivalProvider(r.Geometry,r.HeldIdentity);
                    if(provider.EstimatedRetainedBytes>construction)throw new InvalidOperationException("Provider recipe exceeded its prospective construction reserve.");
                    stagedBytes=checked(stagedBytes+provider.EstimatedRetainedBytes);
                    bindings[n]=new NavArrivalWorkingBinding(r.Entity,provider,new NavGraphProfile(NavigationAdmission.ExactProfileBinding(r.Profile),"distance",1,r.Profile.GridCell));
                }
                var input=new NavArrivalInput("geometry:"+navigationGeometry.Revision,"profile:"+NavigationAdmission.ExactProfileBinding(profile),
                    bindings[0].Provider,bindings[0].Profile,terms,new NavGraphCompileLimits(1048576,300000,1200000),
                    new NavArrivalCandidateSource("authority-spiral",1,group.OriginalLocation??candidates[0],candidates),members,
                    CaptureArrivalOccupancySnapshot(members),1048576,bindings,true);
                var attempt=new ArrivalAttempt{Id=++arrivalSequence,Group=group.GroupId,Input=input,
                    Work=new NavigationArrivalWork(input),Intents=rows,Epoch=0};
                OwnArrivalInput(attempt);attempt.SnapshotPhase=attempt.Work.WorkPhase;attempt.SnapshotCandidate=attempt.Work.CandidateCursor;attempt.SnapshotOccupancy=attempt.Work.OccupancyCursor;attempt.SnapshotMember=attempt.Work.MemberCursor;attempt.SnapshotStatus=attempt.Work.Status;attempt.Frontier=ArrivalFrontier(attempt.Work);arrivalAttempts.Add(attempt.Id,attempt);SignalHost();break;
            }
        }
        // Only the certified host calls this, off the transport gate for solver work.
        internal bool AdvanceArrivalAuthority(int budget)
        {
            if(physicalArrivalEnabled)return AdvancePhysicalArrivalAuthority(budget);
            ArrivalAttempt attempt;ArrivalAdvance? savedStep;
            using(EnterTransport()) {
                attempt=arrivalAttempts.Values.FirstOrDefault(a=>!a.Working&&(a.Reconstructing||!a.Retiring&&!a.Published&&
                    (a.SnapshotStatus==NavArrivalStatus.Pending||a.SnapshotStatus==NavArrivalStatus.BudgetExhausted)));
                if(attempt==null)return false;
                attempt.Working=true;
                if(attempt.Reconstructing)savedStep=attempt.ReplayCursor<attempt.Journal.Count?(ArrivalAdvance?)attempt.Journal[attempt.ReplayCursor]:null;
                else {
                    int allotment=checked(Math.Max(1,budget)*64);
                    savedStep=new ArrivalAdvance{Epoch=attempt.Epoch,Allocation=allotment,Graph=allotment,Field=allotment,
                        Connectors=allotment,Bookkeeping=allotment,Maintenance=256,
                        External=64L+ArrivalRetainedBytes-attempt.Work.StructuralRetainedBytes+hostRetainedBytes+installedCorridorBytes+installedGeometryBytes+answerCorridorBytes};
                }
            }
            var step=savedStep.GetValueOrDefault();
            try {
                if(savedStep.HasValue){long priorReservation=attempt.Work.AuthorityReservationBytes;
                    if(!attempt.Work.TryReserveAuthorityBytes(step.External))return false;
                    attempt.Work.Advance(step.Epoch,step.Allocation,new NavEpochQuota(step.Graph,step.Field,step.Connectors,step.Bookkeeping,step.Maintenance));
                    if(attempt.Work.LastAdvanceRetentionBlocked){attempt.Work.ReserveAuthorityBytes(priorReservation);return false;}
                }
                var frontier=ArrivalFrontier(attempt.Work);var phase=attempt.Work.WorkPhase;int candidate=attempt.Work.CandidateCursor,occupancy=attempt.Work.OccupancyCursor,member=attempt.Work.MemberCursor;
                BeforeArrivalAdvanceCommit?.Invoke(attempt.Work.Status);
                using(EnterTransport()) {
                    if(attempt.Reconstructing){
                        if(savedStep.HasValue)attempt.ReplayCursor++;
                        if(attempt.ReplayCursor==attempt.Journal.Count){
                            if(frontier!=attempt.Frontier||attempt.Work.Status!=attempt.SnapshotStatus||phase!=attempt.SnapshotPhase||candidate!=attempt.SnapshotCandidate||occupancy!=attempt.SnapshotOccupancy||member!=attempt.SnapshotMember)throw new ArgumentException("Corrupt arrival logical frontier.");
                            ValidateArrivalReconstruction(attempt);attempt.Reconstructing=false;
                            if(attempt.Published&&attempt.Work.Status==NavArrivalStatus.Ready)ReleaseArrivalDerivatives(attempt);
                        }
                    } else {attempt.LegacyJournal.Add(step);attempt.Epoch++;attempt.Frontier=frontier;attempt.SnapshotStatus=attempt.Work.Status;attempt.SnapshotPhase=phase;attempt.SnapshotCandidate=candidate;attempt.SnapshotOccupancy=occupancy;attempt.SnapshotMember=member;}
                }
                return true;
            } finally {using(EnterTransport()){attempt.Working=false;PruneArrivalAttempts();}}
        }
        // Called only by the sole gameplay authority at the existing frozen
        // barrier. Workers never dispatch domain/queue/receipt callbacks.
        private void CommitReadyArrivals()
        {
            foreach(var attempt in arrivalAttempts.Values.Where(a=>!a.Retiring&&!a.Working&&!a.Reconstructing&&!a.Published&&
                a.SnapshotStatus!=NavArrivalStatus.Pending&&a.SnapshotStatus!=NavArrivalStatus.BudgetExhausted).ToArray())PublishArrival(attempt);
            EnsureArrivalAttempts();
        }
        private void PublishArrival(ArrivalAttempt attempt)
        {
            if(physicalArrivalEnabled||attempt.Retiring||attempt.Published||attempt.Reconstructing)return;
            var proposals=attempt.Work.Proposals.OrderBy(p=>p.Member.RequestSequence).ToArray();
            // Serialize commits against the current ledger; don't stale two whole
            // groups on an occupancy hash change caused by the other publication.
            foreach(var proposal in proposals){
                var intent=attempt.Intents.Single(r=>r.Request==proposal.Member.RequestSequence);
                if(!CurrentArrivalIntent(intent))continue;
                var occupancy=CaptureArrivalOccupancySnapshot(attempt.Input.Members);
                if(occupancy.Occupants.Any(o=>o.BlocksPlacement&&ArrivalOverlap(proposal.Endpoint,proposal.Member.Radius,o.Location,o.Radius))){
                    RetryArrival(intent);continue;
                }
                var r=new NavigationRequest(intent.Session,intent.Request,intent.Entity,intent.Order,intent.Profile,intent.Geometry,
                    intent.Start,proposal.Endpoint.Position,intent.BaseGeometry,intent.HeldIdentity,intent.MemberIdentity,intent.StartLocation,proposal.Endpoint);
                if(!Requests.TryEnqueue(r))return;
                allocating.Remove(r.Entity);pending[r.Entity]=r;retainedGoals[r.Entity]=r.Goal;reservations[r.Entity]=r.Goal;
                var member=Member(r.Entity);member.Goal=r.Goal;member.GoalLocation=r.GoalLocation;member.HasGoal=true;
                member.Reservation=r.Request;
                arrivalLedger.Add(r.Request,new NavigationArrivalReservation{Id=r.Request,World=Generation,Group=proposal.Member.GroupId,
                    Root=proposal.Member.RootOrderRevision,Command=proposal.Member.CommandSequence,IssuedTick=proposal.Member.IssuedTick,
                    Activation=proposal.Member.ActivationRevision,Entity=r.Entity,Incarnation=proposal.Member.Incarnation,
                    Mobility=proposal.Member.MobilityRevision,Request=r.Request,Order=r.Order,Endpoint=proposal.Endpoint,
                    Radius=proposal.Member.Radius,Proof=proposal.Proof.StableKey,Provider=proposal.Proof.ProviderBinding,
                    Profile=NavigationAdmission.ExactProfileBinding(r.Profile),Hold=r.HeldIdentity,State=NavArrivalReservationKind.PendingRequest});
                arrivalLedger[r.Request].CommitOrder=++arrivalCommitSequence;
                EndpointAssigned?.Invoke(r);RouteRequested?.Invoke(r);
            }
            foreach(var intent in attempt.Intents){
                if(!allocating.TryGetValue(intent.Entity,out var current)||!ReferenceEquals(current,intent))continue;
                if(!CurrentArrivalIntent(intent)){RetryArrival(intent);continue;}
                if(attempt.Work.Status==NavArrivalStatus.Stale){RetryArrival(intent);continue;}
                allocating.Remove(intent.Entity);
                var status=attempt.Work.MemberOutcomes.TryGetValue(intent.Entity,out var outcome)?outcome:attempt.Work.Status;
                RecordFailure(intent,status==NavArrivalStatus.UnreachableInGraph?NavigationOutcome.Unreachable:NavigationOutcome.Rejected);
                if(status==NavArrivalStatus.UnreachableInGraph)UnreachableResults++;
                retainedGoals.Remove(intent.Entity);MovementCancelled?.Invoke(intent.Entity);
            }
            attempt.Published=true;if(attempt.Work.Status==NavArrivalStatus.Ready)ReleaseArrivalDerivatives(attempt);PublishAdmission();PruneArrivalAttempts();
        }
        private void ReleaseArrivalDerivatives(ArrivalAttempt attempt)
        {if(!attempt.Work.HasPhysicalLifetime){attempt.Work.ReleaseDerivatives();attempt.Frontier=ArrivalFrontier(attempt.Work);}}
        private bool ArrivalOverlap(NavLocation a,double ar,NavLocation b,double br)
        {
            double spacing=Math.Max(profile.ArrivalSlotSpacing,4*Math.Max(ar,br)+2*profile.ArrivalTolerance+2*profile.LocalGridCell);
            double x=a.Position.X-b.Position.X,z=a.Position.Z-b.Position.Z;return x*x+z*z<spacing*spacing-1e-9;
        }
        private void RetryArrival(NavigationRequest old)
        {
            if(!allocating.TryGetValue(old.Entity,out var current)||!ReferenceEquals(old,current))return;
            var member=Member(old.Entity);
            if(member==null||memberGroups[old.Entity]!=old.MemberIdentity.GroupId||member.OrderRevision!=old.MemberIdentity.OrderRevision||
                !Crowd.TryGet(old.Entity,out var alive)||alive.Incarnation!=old.MemberIdentity.Incarnation){allocating.Remove(old.Entity);return;}
            var actor=Crowd.Units.Single(u=>u.Id==old.Entity);
            var next=new NavigationRequest(Generation,++requestSequence,old.Entity,old.Order,
                profile.ForUnit(actor.Radius,actor.Speed,actor.TurnSpeed),MovementGeometry(old.Entity),actor.Position,old.Goal,
                navigationGeometry,HeldIdentity(old.Entity),RequestIdentity(actor),actor.Location,old.GoalLocation);
            allocating[old.Entity]=next;movementIdentities[old.Entity]=next.HeldIdentity;
            EndpointAssigned?.Invoke(next);
        }
        private bool RetryAssignedArrival(NavigationRequest request)
        {
            if(!IsArrivalRequest(request))return false;
            CancelArrival(request.Entity,false);allocating[request.Entity]=request;RetryArrival(request);return true;
        }
        private void CancelArrival(int entity,bool installed)
        {
            allocating.Remove(entity);
            foreach(var key in arrivalLedger.Where(p=>p.Value.Entity==entity&&(installed||p.Value.State==NavArrivalReservationKind.PendingRequest)).Select(p=>p.Key).ToArray())arrivalLedger.Remove(key);
            PruneArrivalAttempts();
        }
        private void InstallArrival(NavigationRequest request)
        {
            foreach(var key in arrivalLedger.Where(p=>p.Value.Entity==request.Entity&&p.Value.State==NavArrivalReservationKind.InstalledExecution).Select(p=>p.Key).ToArray())arrivalLedger.Remove(key);
            if(arrivalLedger.TryGetValue(request.Request,out var row))row.State=NavArrivalReservationKind.InstalledExecution;
            PruneArrivalAttempts();
        }
        private ArrivalAttempt RetainPhysicalAttemptEdges(ArrivalAttempt attempt,NavigationArrivalWork work,NavigationHostArrivalIntents intents,NavigationHostArrivalHistory history)
        {
            // Old Arrival4 accepted legacy projection payloads. Keep that
            // explicit compatibility shape until its separate migration.
            if(attempt.Projections?.Count>0){work.Retain();attempt.OwnedWork=work;intents.Retain();attempt.OwnedIntents=intents;history.Retain();attempt.OwnedHistory=history;return attempt;}
            if(!TryOpenPhysicalHostScope(NavigationPreparationKind.Decode,attempt.Id,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Live arrival header admission refused.");
            using(scope){NavigationLiveArrivalAttempt owner=null;try{owner=NavigationLiveArrivalAttempt.CreateOwned(attempt,work,intents,history,scope.OwnedReservation());scope.Attach(owner);scope.Commit(owner,_=>{});return owner.Record;}catch{owner?.ReleaseStorage();throw;}}
        }
        private static void ReleasePhysicalAttemptEdges(ArrivalAttempt attempt)
        {var owner=attempt.StorageOwner;if(owner!=null){attempt.StorageOwner=null;owner.ReleaseStorage();return;}var work=attempt.OwnedWork;var intents=attempt.OwnedIntents;var history=attempt.OwnedHistory;attempt.OwnedWork=null;attempt.OwnedIntents=null;attempt.OwnedHistory=null;work?.Release();intents?.Release();history?.Release();}
        internal void StageRetainedPhysicalArrivals(NavigationHostRoot root,INavStorageReservation reservation)
        {
            foreach(var attempt in arrivalAttempts.Values)if(attempt.OwnedWork!=null)root.StageRetainedArrivalAttempt(attempt.Id,attempt.OwnedIntents,attempt.OwnedWork,attempt.OwnedHistory,reservation);
        }
        internal long LastOwnedArrivalPruneAllocatedBytes{get;private set;}=-1;
        private bool TryPruneOwnedArrivalAttemptsNoAllocation()
        {
            if(liveArrivalTables==null||arrivalProjectionLeases.Count!=0&&liveArrivalLeaseTable==null||!(allocating is NavigationCommandSlots<int,NavigationRequest> ownedAllocating))return false;
            for(int i=0;i<liveArrivalTables.Count;i++){var value=liveArrivalTables.Attempts[liveArrivalTables.SortedId(i)];if(value.StorageOwner==null||!value.Work.HasPhysicalLifetime||value.OwnedProviders==null)return false;}
            int index=0;
            while(index<liveArrivalTables.Count){
                var attempt=liveArrivalTables.Attempts[liveArrivalTables.SortedId(index)];bool hasLease=false;
                if(liveArrivalLeaseTable!=null){
                    // Acquisition order within each attempt is the historical
                    // retirement order. The paid records contain all links.
                    long next=arrivalRetirementSequence;
                    for(int n=0;n<liveArrivalLeaseTable.Count;n++){var lease=liveArrivalLeaseTable.Tokens[liveArrivalLeaseTable.AcquisitionToken(n)];if(lease.Attempt!=attempt.Id)continue;hasLease=true;
                        if(!lease.Retiring&&!ReadyContainsRequest(arrivalLedger,ownedAllocating,lease.Request)){if(!liveArrivalLeaseTable.CanMarkRetiring(lease)||next==long.MaxValue)throw new InvalidOperationException("Invalid owned lease retirement source.");next++;}}
                    if(!attempt.Retiring&&next==long.MaxValue)throw new InvalidOperationException("Arrival retirement sequence exhausted.");
                    for(int n=0;n<liveArrivalLeaseTable.Count;n++){var lease=liveArrivalLeaseTable.Tokens[liveArrivalLeaseTable.AcquisitionToken(n)];if(lease.Attempt==attempt.Id&&!lease.Retiring&&!ReadyContainsRequest(arrivalLedger,ownedAllocating,lease.Request))liveArrivalLeaseTable.MarkRetiringNoThrow(lease,++arrivalRetirementSequence);}
                }
                bool present=false;
                foreach(var request in attempt.Intents)if(ReadyContainsRequest(arrivalLedger,ownedAllocating,request.Request)){present=true;break;}
                if(present){index++;continue;}
                if(!attempt.Retiring&&arrivalRetirementSequence==long.MaxValue)throw new InvalidOperationException("Arrival retirement sequence exhausted.");
                bool retained=attempt.Working||attempt.RestoredRetiringCompute||hasLease;int slot=-1,sorted=-1;
                if(!retained){
                    if(!liveArrivalTables.CanExtractAttempt(attempt,out slot,out sorted))throw new InvalidOperationException("Missing owned arrival attempt slot.");
                    foreach(var provider in attempt.OwnedProviders)if(!arrivalProviders.TryGetValue(provider,out var record)||!liveArrivalTables.TryProviderSlots(record,out _,out _))throw new InvalidOperationException("Missing owned arrival provider slot.");
                    if(liveArrivalTables.Ids.Count>0&&(liveArrivalTables.ResetReferences==null||liveArrivalTables.ResetIds==null))throw new InvalidOperationException("Missing prepaid arrival reset roots.");
                }
                if(!attempt.Retiring){attempt.Retiring=true;attempt.Retirement=++arrivalRetirementSequence;attempt.RetirementReason=attempt.StorageOwner.RemovedReason.Value;attempt.StorageOwner.ChangedNoThrow();}
                if(retained){index++;continue;}
                foreach(var provider in attempt.OwnedProviders)ReleaseArrivalProvider(provider);
                RetireArrivalAttemptNoThrow(liveArrivalTables.ExtractAttemptNoThrow(slot,sorted));
            }
            return true;
        }
        private void PruneArrivalAttempts()
        {using(EnterTransport()){long before=GC.GetAllocatedBytesForCurrentThread();if(TryPruneOwnedArrivalAttemptsNoAllocation()){LastOwnedArrivalPruneAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-before;return;}LastOwnedArrivalPruneAllocatedBytes=-1;PruneArrivalAttemptsCompatibility();}}
        private void PruneArrivalAttemptsCompatibility()
        {
            foreach(var attempt in arrivalAttempts.Values.ToArray()){
                foreach(var lease in arrivalProjectionLeases.Values.Where(l=>l.Attempt==attempt.Id&&!l.Retiring).OrderBy(l=>l.Acquisition))
                    if(!arrivalLedger.ContainsKey(lease.Request)&&!allocating.Values.Any(i=>i.Request==lease.Request)){
                        lease.Retiring=true;lease.Retirement=checked(++arrivalRetirementSequence);lease.Reason="gameplay-owner-removed";arrivalRetirements.Add(lease.Retirement,lease);
                        if(!physicalArrivalEnabled)foreach(var captured in registeredRoutes.Where(r=>r.Session==lease.World&&r.Request==lease.Request).ToArray()){registeredGraphKeys.Remove(captured);registeredRoutes.Remove(captured);}
                    }
                if(attempt.Intents.Any(r=>arrivalLedger.ContainsKey(r.Request)||allocating.Values.Any(i=>i.Request==r.Request)))continue;
                if(!attempt.Retiring){attempt.Retiring=true;attempt.Retirement=checked(++arrivalRetirementSequence);attempt.RetirementReason=attempt.StorageOwner?.RemovedReason.Value??"gameplay-owner-removed";attempt.StorageOwner?.ChangedNoThrow();}
                if(attempt.Working||attempt.RestoredRetiringCompute||arrivalProjectionLeases.Values.Any(l=>l.Attempt==attempt.Id))continue;
                if(!attempt.Work.HasPhysicalLifetime)attempt.Work.DiscardCancelledDerivatives();
                foreach(var provider in attempt.OwnedProviders??Array.Empty<ILayeredNavigationProvider>())ReleaseArrivalProvider(provider);
                if(liveArrivalTables!=null){
                    if(!liveArrivalTables.CanExtractAttempt(attempt,out int slot,out int sorted))throw new InvalidOperationException("Invalid live arrival attempt retirement.");
                    RetireArrivalAttemptNoThrow(liveArrivalTables.ExtractAttemptNoThrow(slot,sorted));
                }else{attempt.OwnedProviders=null;attempt.Input=null;arrivalAttempts.Remove(attempt.Id);ReleasePhysicalAttemptEdges(attempt);}
            }
        }
        internal bool TryTransferArrival(out NavigationRequest request,out NavScheduledRequest scheduled,out NavTypedRoute route,out ArrivalProjection projection,out ArrivalProviderLease owner)
        {
            NavScheduledRequest found=null;NavTypedRoute typed=null;ArrivalProjection progress=null;ArrivalProviderLease acquired=null;
            bool accepted=TryTransferRoute(r=>{
                var attempt=arrivalAttempts.Values.FirstOrDefault(a=>a.Intents.Any(i=>i.Request==r.Request));
                if(attempt==null||attempt.Retiring||attempt.Reconstructing)return false;
                var proposal=attempt.Work.Proposals.SingleOrDefault(p=>p.Member.RequestSequence==r.Request);
                if(proposal==null)return false;
                long capacity=ArrivalLeaseCapacity;
                long growth=arrivalProjectionLeases.Count+1<=capacity?0:capacity==0?512L:128L*(3*capacity+4);
                if(ArrivalRetainedBytes+hostRetainedBytes+installedCorridorBytes+installedGeometryBytes+answerCorridorBytes+1792+growth+288L*proposal.Route.Legs.Count>LayeredNavigationScheduler.MaxRetainedBytes)return false;
                var binding=attempt.Input.WorkingFor(proposal.Member);
                found=NavScheduledRequest.ForSingleton(binding.Provider,proposal.Member.Clearance,binding.Profile,attempt.Input.Limits,proposal.Route.Subscription,attempt.Input.MaxLabels);
                ValidateScheduledRoute(r,found,Admission);
                r.CertifiedProviderBinding=found.Provider.Binding;registeredGraphKeys.Add(r,found.GraphKey);
                typed=proposal.Route;acquired=AcquireArrivalProjection(attempt,r,found,typed);attempt.Projections.TryGetValue(r.Request,out progress);return true;
            },out request);
            scheduled=found;route=typed;projection=progress;owner=acquired;return accepted;
        }
        internal bool IsArrivalRequest(NavigationRequest request)
            =>arrivalAttempts.Values.Any(a=>a.Intents.Any(i=>i.Request==request.Request));
        internal void SaveArrivalProjection(NavigationRequest request,ArrivalProjection state)
        {using(EnterTransport()){var attempt=arrivalAttempts.Values.FirstOrDefault(a=>a.Intents.Any(i=>i.Request==request.Request));if(attempt!=null){if(attempt.Projections==null)attempt.Projections=new Dictionary<long,ArrivalProjection>();attempt.Projections[request.Request]=state;}}}
        internal string ArrivalRetentionDiagnostic => string.Join(";",arrivalAttempts.Values.Select(a=>a.Id+":"+a.Work.WorkPhase+" status="+a.Work.Status+" journal="+a.Journal.Count+" retained="+a.Work.StructuralRetainedBytes+" candidates="+a.Input.Source.Candidates.Count));
        internal long ArrivalRetainedBytes => ArrivalProviderOwnedBytes+arrivalAttempts.Values.Sum(a=>4096L+128L*a.Input.Source.Candidates.Count+512L*a.Input.Members.Count+
            256L*a.Input.Occupancy.Occupants.Count+64L*a.Journal.Count+a.Work.StructuralRetainedBytes+
            (a.Projections?.Values.Sum(p=>128L+16L*p.Points.Length+128L*(p.Typed.Length+p.Observed.Length))??0L))+512L*arrivalLedger.Count;
        private void ValidateArrivalReconstruction(ArrivalAttempt attempt)
        {
            foreach(var row in arrivalLedger.Values.Where(r=>attempt.Intents.Any(i=>i.Request==r.Request))){
                var proposal=attempt.Work.Proposals.SingleOrDefault(p=>p.Member.RequestSequence==row.Request);
                if(proposal==null||proposal.Proof.StableKey!=row.Proof||proposal.Proof.ProviderBinding!=row.Provider||!proposal.Endpoint.Equals(row.Endpoint)||proposal.Member.Radius!=row.Radius||
                    proposal.Member.GroupId!=row.Group||proposal.Member.RootOrderRevision!=row.Root||proposal.Member.CommandSequence!=row.Command||proposal.Member.IssuedTick!=row.IssuedTick||proposal.Member.ActivationRevision!=row.Activation||proposal.Member.EntityId!=row.Entity||proposal.Member.Incarnation!=row.Incarnation||proposal.Member.MobilityRevision!=row.Mobility||row.Profile!=attempt.Input.WorkingFor(proposal.Member).Profile.ExactProfileBinding||row.Hold!=proposal.Member.HoldBinding||row.Order!=proposal.Member.Order)
                    throw new ArgumentException("Reservation proof reconstruction mismatch.");
            }
            if(attempt.Projections!=null)foreach(var saved in attempt.Projections){
                var route=attempt.Work.Proposals.Single(p=>p.Member.RequestSequence==saved.Key).Route;var p=saved.Value;
                if(p.Leg>route.Legs.Count||!p.Expected.Equals(p.Leg==0?route.Subscription.Origin:route.Legs[p.Leg-1].To)||
                    !p.Points.SequenceEqual(route.Legs.Take(p.Leg).Where(l=>l.Kind==NavTypedLegKind.Surface).Select(l=>l.To.Position))||
                    !p.Typed.SequenceEqual(route.Legs.Take(p.Leg).Where(l=>l.Kind==NavTypedLegKind.Portal).Select(l=>l.PortalId)))throw new ArgumentException("Corrupt projected route prefix.");
            }
        }
        // An owned immutable Work is guarded by exact source/root/history
        // references. Its live anchor is descriptive; capture stores a separate
        // full digest and always verifies it before restoring a live header.
        internal static string LiveArrivalFrontier(NavigationArrivalWork work)
            =>!work.UsesOwnedFrontierAnchor?ArrivalFrontier(work):NavContract.Digest(w=>{
                w.Write("owned-arrival-live-anchor-v1");w.Write(work.InputBinding);w.Write(work.LogicalEpoch);w.Write((int)work.Status);w.Write(work.WorkPhase);
                w.Write(work.AllocationWorkConsumed);w.Write(work.CandidateWorkConsumed);w.Write(work.OccupancyWorkConsumed);w.Write(work.DirectWorkConsumed);
                w.Write(work.AllocationQuotaRemaining);w.Write(work.MemberCursor);w.Write(work.CandidateCursor);w.Write(work.OccupancyCursor);
            });
        internal static string ArrivalFrontier(NavigationArrivalWork work)
            =>NavContract.StreamingDigest(w=>{w.Write(work.InputBinding);w.Write((int)work.Status);w.Write(work.LogicalEpoch);
                w.Write(work.AllocationWorkConsumed);w.Write(work.CandidateWorkConsumed);w.Write(work.OccupancyWorkConsumed);
                w.Write(work.AllocationQuotaRemaining);w.Write(work.MemberCursor);w.Write(work.CandidateCursor);w.Write(work.OccupancyCursor);w.Write(work.WorkPhase);
                var c=work.RouteCounters;w.Write(c.LogicalGraphWork);w.Write(c.LogicalFieldWork);w.Write(c.ConnectorWork);w.Write(c.MaintenanceWork);
                w.Write(work.LogicalContinuationBinding);
                foreach(var p in work.Proposals)w.Write(p.Proof.StableKey);
                foreach(var p in work.MemberOutcomes.OrderBy(p=>p.Key)){w.Write(p.Key);w.Write((int)p.Value);}});
    }
}

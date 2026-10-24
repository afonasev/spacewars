using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // One concrete, private DTO allocation graph. Its accounting outlives the
    // preparation scope and ends only at Dispose / the public caller boundary.
    internal sealed class NavigationCaptureDtoStorage : NavOwnedStorage, IDisposable
    {
        private bool released;
        internal NavigationSessionState Value{get;private set;}
        internal void Bind(NavigationSessionState state){if(Value!=null||state==null||!ReferenceEquals(state.CaptureDto,this))throw new ArgumentException("Invalid capture DTO ownership.");Value=state;}
        private NavigationCaptureDtoStorage(long bytes):base(bytes){Retain();}
        private static class Layout<T>{internal static readonly int Width=NavStorageLayout.ValueBytes(typeof(T));}
        private static class RecordLayout<T>where T:class{internal static readonly long Bytes=NavStorageLayout.ObjectBytesBound(typeof(T));}
        internal static long ArrayBytes<T>(long count)=>checked(32L+((count*Layout<T>.Width+7)/8)*8);
        internal static long RecordBytes<T>() where T:class=>RecordLayout<T>.Bytes;
        internal static NavigationCaptureDtoStorage CreateOwned(long graphBytes,INavStorageReservation reservation)
        {
            long bytes=checked(RecordBytes<NavigationCaptureDtoStorage>()+graphBytes);
            using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(bytes);return allocation.Register(new NavigationCaptureDtoStorage(bytes));
            }
        }
        public void Dispose(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>Dispose();
        protected override void ReleaseEdges(){Value=null;}
    }
    // Bounded capture-only lookup workspace; no dictionary growth or LINQ
    // buffers. The exact backing capacities are admitted before construction.
    internal sealed class NavigationCaptureRequests
    {
        private readonly NavigationRequest[] entries;
        internal int Count{get;private set;}
        internal NavigationRequest this[int index]=>entries[index];
        internal NavigationCaptureRequests(int capacity){entries=new NavigationRequest[capacity];}
        internal int Index(NavigationRequest value){for(int i=0;i<Count;i++)if(ReferenceEquals(entries[i],value))return i;throw new ArgumentException("Unknown captured request.");}
        internal void Include(NavigationRequest value){if(value==null)throw new ArgumentException("Null captured request.");for(int i=0;i<Count;i++)if(ReferenceEquals(entries[i],value))return;if(Count==entries.Length)throw new InvalidOperationException("Capture request bound changed.");entries[Count++]=value;}
    }
    internal sealed class NavigationCaptureGeometries
    {
        private readonly NavGeometry[] entries,aliases;private readonly int[] indices;
        private int aliasCount;
        internal int Count{get;private set;}
        internal NavGeometry this[int index]=>entries[index];
        internal NavigationCaptureGeometries(int capacity){entries=new NavGeometry[capacity];aliases=new NavGeometry[capacity];indices=new int[capacity];}
        internal bool Contains(NavGeometry value){for(int i=0;i<Count;i++)if(ReferenceEquals(entries[i],value))return true;for(int i=0;i<aliasCount;i++)if(ReferenceEquals(aliases[i],value))return true;return false;}
        internal void Alias(NavGeometry value,int index){if(aliasCount==aliases.Length)throw new InvalidOperationException("Capture geometry bound changed.");aliases[aliasCount]=value;indices[aliasCount++]=index;}
        internal int Include(NavGeometry value){if(value==null)throw new ArgumentException("Null captured geometry.");for(int i=0;i<Count;i++)if(ReferenceEquals(entries[i],value))return i;for(int i=0;i<aliasCount;i++)if(ReferenceEquals(aliases[i],value))return indices[i];if(Count==entries.Length)throw new InvalidOperationException("Capture geometry bound changed.");entries[Count]=value;return Count++;}
    }
    public sealed partial class NavigationSession
    {
        private readonly struct CaptureMapStamp<K,V>
        {
            internal readonly NavigationCommandSlots<K,V> Root;internal readonly int Revision,Count,LayoutLength;
            internal CaptureMapStamp(NavigationCommandSlots<K,V> root){Root=root;Revision=root?.Revision??0;Count=root?.Count??0;LayoutLength=root?.LayoutLength??1;root?.Retain();}
            internal bool Current(NavigationCommandSlots<K,V> root)=>ReferenceEquals(Root,root)&&(root?.Revision??0)==Revision&&(root?.Count??0)==Count;
            internal void Release()=>Root?.Release();
        }
        // One capture's concrete read set. It pins mutable source containers for
        // lifetime only; identity/revision and actor facts still guard copying.
        private struct CaptureReadSet
        {
            internal CaptureMapStamp<long,GroupOrderState> Groups;
            internal CaptureMapStamp<int,long> Members,Orders;
            internal CaptureMapStamp<int,NavigationRequest> Pending,Allocating,Probes;
            internal CaptureMapStamp<int,NavPoint> Reservations,Goals;
            internal CaptureMapStamp<int,string> Identities;
            internal CaptureMapStamp<int,NavSolveStatus> Failures;
            internal CaptureMapStamp<long,NavigationArrivalReservation> Ledger;
            internal CaptureMapStamp<NavigationAnswer,NavigationHostAnswer> Tracking;
            internal NavigationTrafficAuthority Traffic;internal NavPagedList<long> Bank;
            internal NavigationHostRoot Host;internal NavigationAuthorityOwners Core;internal NavigationCommandAuthority Backing;internal NavigationAdmissionStorage Admission;
            internal NavigationLiveArrivalTables Arrival;internal NavigationLiveArrivalLeaseTable Leases;internal NavigationLiveRegistrations Registrations;internal NavigationLiveProbeAnswers Queue;
            internal NavigationBarrierReceipt Barrier;internal NavRetainedArray<NavNameReference> Terrain;
            internal NavCrowd Crowd;internal NavMailbox<NavigationRequest> Requests;internal NavMailbox<NavigationAnswer> Answers;
            internal long Operation,Publication,RequestRevision,AnswerRevision,BarrierRevision,RequestSequence,GroupSequence,Attachment,Retirement;internal int Applied,Rejected,Unreachable;
            internal CertifiedRouteKernel Worker;
            internal int Actors,Installed,RequestCount,AnswerCount,BarrierCount,ArrivalRevision,LeaseRevision,RegistrationRevision,RegistrationCount,QueueRevision,QueueCount;
            internal long HostBytes,AnswerBytes,CorridorBytes,GeometryBytes;internal NavigationArrivalCatalog.Values ArrivalCounters;
            internal int RequestBound=>checked(Allocating.Count+Pending.Count+Probes.Count+QueueCount+RequestCount+RegistrationCount+(Host?.Delivered.Count??0)+BarrierCount+AnswerCount);
            internal int GeometryBound=>checked(2+RequestBound*2+Installed*2);
            internal void RequireLedger(INavStorageReservation r)
            {
                void Check(NavOwnedStorage root){if(root!=null)NavStorageAdmission.RequireOwned(r,root);}
                Check(Groups.Root);Check(Members.Root);Check(Orders.Root);Check(Pending.Root);Check(Allocating.Root);Check(Probes.Root);Check(Reservations.Root);Check(Goals.Root);Check(Identities.Root);Check(Failures.Root);Check(Ledger.Root);Check(Tracking.Root);Check(Traffic);Check(Bank);Check(Host);Check(Core);Check(Backing);Check(Admission);Check(Arrival);Check(Leases);Check(Registrations);Check(Queue);Check(Barrier);Check(Terrain);
            }
            internal void Release()
            {
                Groups.Release();Members.Release();Orders.Release();Pending.Release();Allocating.Release();Probes.Release();Reservations.Release();Goals.Release();Identities.Release();Failures.Release();Ledger.Release();Tracking.Release();
                Traffic?.Release();Bank?.Release();Host?.Release();Core?.Release();Backing?.Release();Admission?.Release();Arrival?.Release();Leases?.Release();Registrations?.Release();Queue?.Release();Barrier?.Release();Terrain?.Release();this=default;
            }
        }
        private readonly struct CaptureGroupRefresh
        {
            internal readonly int Group,Member;internal readonly NavigationAuthorityMember.Values Value;
            internal CaptureGroupRefresh(int group,int member,NavigationAuthorityMember.Values value){Group=group;Member=member;Value=value;}
        }
        private sealed class NavigationPreparedCapture:NavOwnedStorage,IDisposable
        {
            private static long Align(long n)=>checked((n+7)/8*8);
            private static long Layout(CaptureReadSet s)=>checked(NavStorageLayout.ObjectBytesBound(typeof(NavigationPreparedCapture))+NavStorageLayout.ObjectBytesBound(typeof(NavCrowdSaveState))+NavStorageLayout.ObjectBytesBound(typeof(NavigationCaptureRequests))+NavStorageLayout.ObjectBytesBound(typeof(NavigationCaptureGeometries))+27*32L+Align(s.Groups.Count*(long)NavStorageLayout.ValueBytes(typeof(RebindGroup)))+Align(s.Members.Count*(long)NavStorageLayout.ValueBytes(typeof(CaptureGroupRefresh)))+s.Ledger.Count*(24L+NavStorageLayout.ObjectBytesBound(typeof(NavigationArrivalReservation)))+8L*s.RegistrationCount+8L*s.Pending.Count+Align(s.Pending.Count)+8L*s.RequestBound+8L*s.RequestBound+20L*s.GeometryBound+8L*s.GeometryBound+Align(8L*s.RequestCount)+16L*(s.AnswerCount+s.BarrierCount+s.QueueCount)+s.Actors*(NavStorageLayout.ObjectBytesBound(typeof(NavUnitSaveState))+56L)+8L*s.Installed+Align(s.Installed)+Align(s.Groups.Count*(long)NavStorageLayout.ValueBytes(typeof(NavigationLiveGroupObservation))));
            internal CaptureReadSet Source;
            internal readonly NavigationCaptureRequests All;internal readonly NavigationCaptureGeometries Geometries;
            internal readonly NavigationRequest[] Queued,Registered,PendingRows;internal readonly bool[] HistoricalPending;internal readonly NavigationAnswer[] Mailbox,BarrierRows,ProbeRows;
            internal readonly NavOwnedStorage[] RequestPins,GeometryPins,AnswerPins,LocationPins;
            internal readonly NavUnitSaveState[] ActorFacts;internal readonly NavLiveUnitStorage[] ActorPins;
            internal readonly NavRetainedArray<NavPoint>[] Globals,Locals;
            internal readonly NavigationInstalledExecutionOwner[] Installed;internal readonly bool[] Detached;
            internal readonly NavigationLiveGroupObservation[] GroupSources;internal readonly RebindGroup[] Groups;internal readonly CaptureGroupRefresh[] RefreshRows;internal int RefreshWrites;internal bool GroupDestinations;
            internal readonly NavigationArrivalReservation[] LedgerRows;internal readonly long[] LedgerKeys;internal readonly NavOwnedStorage[] LedgerPins;internal int WireLeaseCapacity;
            internal readonly NavCrowdSaveState CrowdSummary;internal NavOwnedStorage CrowdGeometry,CrowdProfile;internal NavGeometry CrowdGeometryFacade;internal NavigationProfile CrowdProfileFacade;
            internal NavigationSessionState State;internal NavigationAdmissionStorage NextAdmission;internal NavigationHostRoot NewHost;internal bool HostDestination;
            internal int BaseGeometry;internal long DtoBytes;internal bool Observed,Transferred,AdmissionUnchanged;private bool released;
            private NavigationPreparedCapture(CaptureReadSet source):base(Layout(source))
            {
                Source=source;LedgerRows=new NavigationArrivalReservation[source.Ledger.Count];LedgerKeys=new long[source.Ledger.Count];LedgerPins=new NavOwnedStorage[source.Ledger.Count];for(int i=0;i<LedgerRows.Length;i++)LedgerRows[i]=new NavigationArrivalReservation();CrowdSummary=new NavCrowdSaveState();All=new NavigationCaptureRequests(source.RequestBound);Geometries=new NavigationCaptureGeometries(source.GeometryBound);Queued=new NavigationRequest[source.RequestCount];Registered=new NavigationRequest[source.RegistrationCount];PendingRows=new NavigationRequest[source.Pending.Count];HistoricalPending=new bool[source.Pending.Count];Mailbox=new NavigationAnswer[source.AnswerCount];BarrierRows=new NavigationAnswer[source.BarrierCount];ProbeRows=new NavigationAnswer[source.QueueCount];RequestPins=new NavOwnedStorage[source.RequestBound];GeometryPins=new NavOwnedStorage[source.GeometryBound];AnswerPins=new NavOwnedStorage[source.AnswerCount+source.BarrierCount+source.QueueCount];LocationPins=new NavOwnedStorage[3*source.Actors];ActorFacts=new NavUnitSaveState[source.Actors];ActorPins=new NavLiveUnitStorage[source.Actors];Globals=new NavRetainedArray<NavPoint>[source.Actors];Locals=new NavRetainedArray<NavPoint>[source.Actors];Installed=new NavigationInstalledExecutionOwner[source.Installed];Detached=new bool[source.Installed];GroupSources=new NavigationLiveGroupObservation[source.Groups.Count];Groups=new RebindGroup[source.Groups.Count];RefreshRows=new CaptureGroupRefresh[source.Members.Count];for(int i=0;i<ActorFacts.Length;i++)ActorFacts[i]=new NavUnitSaveState();Retain();
            }
            internal static NavigationPreparedCapture CreateOwned(ref CaptureReadSet source,INavStorageReservation r)
            {
                source.RequireLedger(r);using(var a=new NavStorageAllocation(r.TryReserve)){a.Reserve(Layout(source));var result=a.Register(new NavigationPreparedCapture(source));source=default;return result;}
            }
            public void Dispose(){if(released)return;released=true;Release();}
            internal override void ReleaseOwner()=>Dispose();
            protected override void ReleaseEdges()
            {
                if(!Transferred)State?.Dispose();if(HostDestination)NewHost?.Release();NewHost?.ReleaseStorage();NextAdmission?.Release();foreach(var group in Groups){if(GroupDestinations&&group.Writes>0)group.Next?.Release();group.Next?.Release();group.Retired?.Release();}foreach(var owner in LedgerPins)owner?.Release();CrowdGeometry?.Release();CrowdProfile?.Release();foreach(var owner in RequestPins)owner?.Release();foreach(var owner in GeometryPins)owner?.Release();foreach(var owner in AnswerPins)owner?.Release();foreach(var owner in LocationPins)owner?.Release();foreach(var owner in ActorPins)owner?.Release();foreach(var owner in Globals)owner?.Release();foreach(var owner in Locals)owner?.Release();foreach(var owner in Installed)owner?.Release();foreach(var group in GroupSources)group.ReleasePins();Source.Release();
            }
        }
        internal int OwnedCapturePublications{get;private set;}
        internal long LastOwnedCaptureObservationAllocatedBytes{get;private set;}
        internal long LastOwnedCapturePublicationAllocatedBytes{get;private set;}
        private NavigationSessionState CapturePreparedOwnedState()
        {
            if(System.Threading.Monitor.IsEntered(transportGate))throw new InvalidOperationException("Prepared capture requires an outer transport barrier.");
            CaptureReadSet source=default;NavigationPreparedCapture prepared=null;ArrivalCatalogCapture arrivalCapture=null;ArrivalLeaseCapture leaseCapture=null;
            bool signal=false;
            try{
                using(EnterTransport()){RejectTransferCallbackReentry();if(commandGroup!=null||admissionBatchDepth!=0)throw new InvalidOperationException("Cannot capture an open navigation admission batch.");source=ObserveCaptureReadSet();}
                if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Capture,Generation,source.Publication,out var scope))throw new InvalidOperationException("Authority catalog capture admission refused.");
                using(scope){try{
                    var reservation=scope.OwnedReservation();prepared=NavigationPreparedCapture.CreateOwned(ref source,reservation);
                    using(EnterTransport()){long start=GC.GetAllocatedBytesForCurrentThread();ObserveCaptureSources(prepared);LastOwnedCaptureObservationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-start;}
                    PrepareCaptureHistoricalBindings(prepared,reservation);PrepareCaptureGroupRefresh(prepared,scope);PrepareCaptureDtoGraph(prepared,reservation);
                    prepared.State.AdmissionPublicationIdentity=prepared.NextAdmission?.Value.PublicationIdentity??prepared.Source.Publication;
                    long nextOperation=checked(prepared.Source.Operation+prepared.RefreshWrites+1);
                    arrivalCapture=PrepareArrivalCatalogCapture(reservation);leaseCapture=PrepareArrivalLeaseCapture(reservation);
                    using(EnterTransport()){
                        if(!CaptureSourceFactsCurrent(prepared))throw new InvalidOperationException("Capture source changed before coherent observation.");
                        long start=GC.GetAllocatedBytesForCurrentThread();ObservePreparedCaptureDto(prepared);ObserveCaptureLedger(prepared);arrivalCapture.Observe(this);leaseCapture?.Observe(this);LastOwnedCaptureObservationAllocatedBytes+=GC.GetAllocatedBytesForCurrentThread()-start;
                    }
                    arrivalCapture.Seal(reservation);prepared.State.CaptureArrivalCatalog=NavPhysicalMacro.Acquire(prepared.State.CaptureDto,arrivalCapture.Catalog);
                    if(leaseCapture!=null){leaseCapture.Seal(reservation);prepared.State.CaptureLeaseCatalog=NavPhysicalMacro.Acquire(prepared.State.CaptureDto,leaseCapture.Catalog);}
                    arrivalCapture.Dispose();arrivalCapture=null;leaseCapture?.Dispose();leaseCapture=null;
                    if(prepared.Source.Host==null){
                        prepared.NewHost=CreatePhysicalHostRoot(reservation);prepared.NewHost.StageAuthorityGeometry(prepared.Geometries[prepared.BaseGeometry],reservation);var catalog=prepared.State.CaptureArrivalCatalog;
                        for(int n=0;n<catalog.AttemptCount;n++){long id=catalog.SortedAttempt(n);for(int i=0;i<catalog.AttemptCount;i++){var attempt=catalog.Attempt(i);if(attempt.Data.Id==id){prepared.NewHost.StageRetainedArrivalAttempt(id,attempt.Intents,attempt.Work,attempt.History,reservation);break;}}}
                        prepared.NewHost.SetFrontier(new CertifiedRouteKernel.PumpFrontier(CertifiedRouteKernel.PumpStage.Idle,0,0,0,0,0,0,0,0,0),0,0);prepared.NewHost.SealPrepared();prepared.State.CaptureRoot=NavPhysicalMacro.Acquire(prepared.State.CaptureDto,prepared.NewHost);
                    }
                    prepared.State.CaptureArrivalOutput=WorldWire.PackOwned(w=>WriteFrozenArrivalAuthority(w,prepared,reservation),physicalHostBudget);prepared.State.ArrivalAuthority=prepared.State.CaptureArrivalOutput.Bytes;
                    prepared.State.CaptureOutput=CaptureHostAuthority(prepared.NewHost??prepared.Source.Host,prepared.Source.Core,prepared.State);prepared.State.HostAuthority=prepared.State.CaptureOutput.Bytes;
                    foreach(var group in prepared.Groups)if(group.Writes>0)group.Next.Retain();prepared.GroupDestinations=true;if(prepared.NewHost!=null){prepared.NewHost.Retain();prepared.HostDestination=true;}
                    scope.Attach(prepared);scope.Publish(prepared,_=>{using(EnterTransport()){
                        long start=GC.GetAllocatedBytesForCurrentThread();if(scope.IsCancelled||!CaptureSourceFactsCurrent(prepared))throw new InvalidOperationException("Capture lost its prepared source.");
                        var token=ObserveLiveWriteToken();foreach(var group in prepared.Groups)if(group.Writes>0){if(group.Source.Generation>long.MaxValue-group.Writes)throw new InvalidOperationException("Capture group generation exhausted.");group.Source.Owner.ValidateMarchWrite(group.Source,token);}
                        for(int i=0;i<prepared.Groups.Length;i++)if(prepared.Groups[i].Writes>0){ref var group=ref prepared.Groups[i];group.Retired=group.Source.Version;for(int n=0;n<group.Writes;n++)group.Source.Owner.ApplyMovementMembersNoThrow(group.Next);}prepared.GroupDestinations=false;
                        if(prepared.NextAdmission!=null){signal=PublishAdmissionOwnerNoThrow(prepared.NextAdmission);if(signal)prepared.NextAdmission=null;}
                        if(prepared.NewHost!=null){committedHostRoot=prepared.NewHost;prepared.HostDestination=false;}
                        authorityOperationIdentity=nextOperation;LastOwnedCapturePublicationAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-start;OwnedCapturePublications++;
                    }});scope.ResolveCommitted();
                    if(signal)SignalHost();prepared.Transferred=true;return prepared.State;
                }finally{arrivalCapture?.Dispose();arrivalCapture=null;leaseCapture?.Dispose();leaseCapture=null;prepared?.Dispose();prepared=null;}}
            }finally{arrivalCapture?.Dispose();leaseCapture?.Dispose();prepared?.Dispose();source.Release();}
        }
        private bool CaptureAdmissionMatchesCurrent()
        {
            var previous=admission;
            if(previous==null||previous.PublishedThroughRequestSequence!=requestSequence||
                !ReferenceEquals(previous.Geometry,navigationGeometry)||!ReferenceEquals(previous.Profile,profile)||
                previous.Requests.Count!=pending.Count+probes.Count||previous.Groups.Count!=groups.Count)return false;
            for(int n=0;n<previous.Requests.Count;n++){var request=previous.Requests[n];
                var map=request.Entity<0?probes:pending;
                if(!map.TryGetValue(request.Entity,out var active)||!ReferenceEquals(active,request))return false;
            }
            for(int n=0;n<previous.Groups.Count;n++){var snapshot=previous.Groups[n];
                if(!groups.TryGetValue(snapshot.GroupId,out var group)||
                    snapshot.RootOrderRevision!=group.OrderRevision||snapshot.CommandSequence!=group.CommandSequence||
                    snapshot.IssuedTick!=group.IssuedTick||snapshot.MembershipRevision!=group.MembershipRevision||
                    snapshot.OwnerId!=group.OwnerId||snapshot.TeamId!=group.TeamId||snapshot.TargetId!=group.TargetId||
                    snapshot.Source!=group.Source||snapshot.JobId!=group.JobId||snapshot.ActionId!=group.ActionId||
                    snapshot.Origin!=group.Origin||snapshot.Kind!=group.Kind||
                    !snapshot.OriginalGoal.Equals(group.OriginalGoal)||!Nullable.Equals(snapshot.OriginalTerminal,group.OriginalLocation)||
                    snapshot.Members.Count!=group.Members.Length)return false;
                for(int i=0;i<group.Members.Length;i++){
                    var member=group.Members[i];var saved=snapshot.Members[i];
                    if(saved.Entity!=member.Entity||saved.Slot!=member.Slot||saved.Incarnation!=member.Incarnation||
                        saved.ActivationRevision!=member.OrderRevision||saved.MobilityRevision!=member.MobilityRevision||
                        saved.Phase!=member.Phase||saved.FormationReleased!=member.FormationReleased||
                        saved.HasGoal!=member.HasGoal||!Nullable.Equals(saved.AssignedEndpoint,member.GoalLocation)||
                        !ReferenceEquals(saved.PendingRequest,pending.TryGetValue(member.Entity,out var active)?active:null))return false;
                }
            }
            return true;
        }
        private bool CanPrepareCaptureGraph()
            =>physicalArrivalEnabled&&(liveArrivalLeaseTable!=null||arrivalProjectionLeases.Count==0)&&groupCommandBacking!=null&&authorityOwners!=null&&liveArrivalTables!=null&&registeredRoutes==null&&countedCorridorAnswers.Count==0&&pending is NavigationCommandSlots<int,NavigationRequest>&&allocating is NavigationCommandSlots<int,NavigationRequest>&&groups is NavigationCommandSlots<long,GroupOrderState>&&memberGroups is NavigationCommandSlots<int,long>&&orders is NavigationCommandSlots<int,long>&&reservations is NavigationCommandSlots<int,NavPoint>&&retainedGoals is NavigationCommandSlots<int,NavPoint>&&movementIdentities is NavigationCommandSlots<int,string>&&technicalFailures is NavigationCommandSlots<int,NavSolveStatus>&&arrivalLedger is NavigationCommandSlots<long,NavigationArrivalReservation>&&(ownedProbes!=null||probes.Count==0&&NavigationCommandLayout.Length(probes)==1)&&(liveProbeAnswers!=null||probeAnswers.Count==0);
        private CaptureReadSet ObserveCaptureReadSet()
        {
            CaptureReadSet s=default;try{
                s.Groups=new CaptureMapStamp<long,GroupOrderState>((NavigationCommandSlots<long,GroupOrderState>)groups);s.Members=new CaptureMapStamp<int,long>((NavigationCommandSlots<int,long>)memberGroups);s.Orders=new CaptureMapStamp<int,long>((NavigationCommandSlots<int,long>)orders);
                s.Pending=new CaptureMapStamp<int,NavigationRequest>((NavigationCommandSlots<int,NavigationRequest>)pending);s.Allocating=new CaptureMapStamp<int,NavigationRequest>((NavigationCommandSlots<int,NavigationRequest>)allocating);s.Probes=new CaptureMapStamp<int,NavigationRequest>(ownedProbes);
                s.Reservations=new CaptureMapStamp<int,NavPoint>((NavigationCommandSlots<int,NavPoint>)reservations);s.Goals=new CaptureMapStamp<int,NavPoint>((NavigationCommandSlots<int,NavPoint>)retainedGoals);s.Identities=new CaptureMapStamp<int,string>((NavigationCommandSlots<int,string>)movementIdentities);s.Failures=new CaptureMapStamp<int,NavSolveStatus>((NavigationCommandSlots<int,NavSolveStatus>)technicalFailures);s.Ledger=new CaptureMapStamp<long,NavigationArrivalReservation>((NavigationCommandSlots<long,NavigationArrivalReservation>)arrivalLedger);s.Tracking=new CaptureMapStamp<NavigationAnswer,NavigationHostAnswer>(ownedAnswerTracking);
                s.Bank=creditBank;s.Bank?.Retain();s.Traffic=trafficAuthority;s.Traffic?.Retain();s.Host=committedHostRoot;s.Host?.Retain();s.Core=authorityOwners;s.Core.Retain();s.Backing=groupCommandBacking;s.Backing.Retain();s.Admission=admissionOwner;s.Admission?.Retain();s.Arrival=liveArrivalTables;s.Arrival.Retain();s.Leases=liveArrivalLeaseTable;s.Leases?.Retain();s.Registrations=liveRegistrations;s.Registrations?.Retain();s.Queue=liveProbeAnswers;s.Queue?.Retain();s.Barrier=barrierPayloads;s.Barrier?.Retain();s.Terrain=terrainNameOwners;s.Terrain.Retain();
                s.ArrivalCounters=new NavigationArrivalCatalog.Values(Generation,requestSequence,arrivalSequence,arrivalCommitSequence,arrivalProviderSequence,arrivalLeaseSequence,arrivalAcquisitionSequence,arrivalRetirementSequence,arrivalReleaseSequence);s.Crowd=Crowd;s.Requests=Requests;s.Answers=Answers;s.Operation=authorityOperationIdentity;s.Publication=admissionPublicationIdentity;s.RequestRevision=Requests.Revision;s.AnswerRevision=Answers.Revision;s.BarrierRevision=barrierPayloadRevision;s.RequestSequence=requestSequence;s.GroupSequence=groupSequence;s.Applied=AppliedResults;s.Rejected=RejectedResults;s.Unreachable=UnreachableResults;s.Worker=certifiedHost;s.Attachment=certifiedHostAttachmentIdentity;s.Retirement=arrivalRetirementSequence;
                s.Actors=Crowd.Units.Count;s.Installed=installedExecutions.Count;s.RequestCount=Requests.Count;s.AnswerCount=Answers.Count;s.BarrierCount=barrierAnswers.Count;s.ArrivalRevision=liveArrivalTables.Revision;s.LeaseRevision=liveArrivalLeaseTable?.Revision??0;s.RegistrationRevision=liveRegistrations?.Revision??0;s.RegistrationCount=liveRegistrations?.Count??0;s.QueueRevision=liveProbeAnswers?.Revision??0;s.QueueCount=ProbeAnswerCount;s.HostBytes=hostRetainedBytes;s.AnswerBytes=answerCorridorBytes;s.CorridorBytes=installedCorridorBytes;s.GeometryBytes=installedGeometryBytes;return s;
            }catch{s.Release();throw;}
        }
        private bool CaptureReadSetCurrent(CaptureReadSet s)
        {
            if(s.ArrivalCounters.Attempts!=arrivalSequence||s.ArrivalCounters.Commits!=arrivalCommitSequence||s.ArrivalCounters.Providers!=arrivalProviderSequence||s.ArrivalCounters.Leases!=arrivalLeaseSequence||s.ArrivalCounters.Acquisitions!=arrivalAcquisitionSequence||s.ArrivalCounters.Retirements!=arrivalRetirementSequence||s.ArrivalCounters.Releases!=arrivalReleaseSequence)return false;
            if(!ReferenceEquals(s.Bank,creditBank)||!ReferenceEquals(s.Traffic,trafficAuthority)||!CanPrepareCaptureGraph()||commandGroup!=null||admissionBatchDepth!=0||s.Operation!=authorityOperationIdentity||s.Publication!=admissionPublicationIdentity||!ReferenceEquals(s.Host,committedHostRoot)||!ReferenceEquals(s.Core,authorityOwners)||!ReferenceEquals(s.Backing,groupCommandBacking)||!ReferenceEquals(s.Admission,admissionOwner)||!ReferenceEquals(s.Crowd,Crowd)||s.Actors!=Crowd.Units.Count||s.Installed!=installedExecutions.Count||!ReferenceEquals(s.Requests,Requests)||!ReferenceEquals(s.Answers,Answers)||s.RequestRevision!=Requests.Revision||s.AnswerRevision!=Answers.Revision||s.RequestCount!=Requests.Count||s.AnswerCount!=Answers.Count||s.BarrierRevision!=barrierPayloadRevision||!ReferenceEquals(s.Barrier,barrierPayloads)||s.BarrierCount!=barrierAnswers.Count||s.RequestSequence!=requestSequence||s.GroupSequence!=groupSequence||s.Applied!=AppliedResults||s.Rejected!=RejectedResults||s.Unreachable!=UnreachableResults||!ReferenceEquals(s.Worker,certifiedHost)||s.Attachment!=certifiedHostAttachmentIdentity||s.Retirement!=arrivalRetirementSequence||s.HostBytes!=hostRetainedBytes||s.AnswerBytes!=answerCorridorBytes||s.CorridorBytes!=installedCorridorBytes||s.GeometryBytes!=installedGeometryBytes||!ReferenceEquals(s.Terrain,terrainNameOwners))return false;
            if(!s.Groups.Current(groups as NavigationCommandSlots<long,GroupOrderState>)||!s.Members.Current(memberGroups as NavigationCommandSlots<int,long>)||!s.Orders.Current(orders as NavigationCommandSlots<int,long>)||!s.Pending.Current(pending as NavigationCommandSlots<int,NavigationRequest>)||!s.Allocating.Current(allocating as NavigationCommandSlots<int,NavigationRequest>)||!s.Probes.Current(ownedProbes)||!s.Reservations.Current(reservations as NavigationCommandSlots<int,NavPoint>)||!s.Goals.Current(retainedGoals as NavigationCommandSlots<int,NavPoint>)||!s.Identities.Current(movementIdentities as NavigationCommandSlots<int,string>)||!s.Failures.Current(technicalFailures as NavigationCommandSlots<int,NavSolveStatus>)||!s.Ledger.Current(arrivalLedger as NavigationCommandSlots<long,NavigationArrivalReservation>)||!s.Tracking.Current(ownedAnswerTracking))return false;
            return ReferenceEquals(s.Arrival,liveArrivalTables)&&s.ArrivalRevision==liveArrivalTables.Revision&&ReferenceEquals(s.Leases,liveArrivalLeaseTable)&&s.LeaseRevision==(liveArrivalLeaseTable?.Revision??0)&&ReferenceEquals(s.Registrations,liveRegistrations)&&s.RegistrationRevision==(liveRegistrations?.Revision??0)&&s.RegistrationCount==(liveRegistrations?.Count??0)&&ReferenceEquals(s.Queue,liveProbeAnswers)&&s.QueueRevision==(liveProbeAnswers?.Revision??0)&&s.QueueCount==ProbeAnswerCount;
        }
        private bool CaptureSourceFactsCurrent(NavigationPreparedCapture c)
        {
            if(!CaptureReadSetCurrent(c.Source))return false;if(!c.Observed)return true;if(c.AdmissionUnchanged!=CaptureAdmissionMatchesCurrent())return false;
            for(int i=0;i<c.ActorFacts.Length;i++){var actor=Crowd.Units[i];if(!actor.SameCaptureHeader(c.ActorFacts[i])||!ReferenceEquals(actor.StorageOwner,c.ActorPins[i])||!ReferenceEquals(actor.StorageOwner.Global,c.Globals[i])||!ReferenceEquals(actor.StorageOwner.Local,c.Locals[i]))return false;}
            foreach(var group in c.GroupSources)if(!group.Owner.MatchesObservation(group))return false;
            for(int i=0;i<c.Installed.Length;i++)if(!ReferenceEquals(installedExecutions.OwnerFor(c.Installed[i].Entity),c.Installed[i])||installedExecutions.IsDetached(c.Installed[i].Entity)!=c.Detached[i])return false;
            if(!ReferenceEquals(Crowd.Geometry.StorageRoot,c.CrowdGeometry)||!ReferenceEquals(Crowd.CaptureProfile.StorageRoot,c.CrowdProfile)||!Crowd.SameCaptureSummary(c.CrowdSummary))return false;
            return true;
        }
        private void ObserveCaptureSources(NavigationPreparedCapture c)
        {
            if(!CaptureReadSetCurrent(c.Source)||c.Observed)throw new InvalidOperationException("Capture source changed before shape observation.");
            var s=c.Source;c.AdmissionUnchanged=CaptureAdmissionMatchesCurrent();Crowd.CopyCaptureSummaryTo(c.CrowdSummary);c.CrowdGeometryFacade=Crowd.Geometry;c.CrowdProfileFacade=Crowd.CaptureProfile;c.CrowdGeometry=NavPhysicalMacro.Acquire(c,Crowd.Geometry.StorageRoot);c.CrowdProfile=NavPhysicalMacro.Acquire(c,Crowd.CaptureProfile.StorageRoot);
            for(int i=0;i<s.Actors;i++){
                var actor=Crowd.Units[i];if(actor.StorageOwner==null)throw new InvalidOperationException("Capture actor has no physical owner.");actor.CopyCaptureHeaderTo(c.ActorFacts[i]);c.ActorPins[i]=NavPhysicalMacro.Acquire(c,actor.StorageOwner);c.Globals[i]=NavPhysicalMacro.Acquire(c,actor.StorageOwner.Global);c.Locals[i]=NavPhysicalMacro.Acquire(c,actor.StorageOwner.Local);if((c.Globals[i]?.Count??0)!=actor.route.Length||(c.Locals[i]?.Count??0)!=actor.localRoute.Length||c.Globals[i]!=null&&!ReferenceEquals(c.Globals[i].ReadOnlyBacking,actor.route)||c.Locals[i]!=null&&!ReferenceEquals(c.Locals[i].ReadOnlyBacking,actor.localRoute))throw new InvalidOperationException("Capture actor route has no exact physical owner.");
                c.LocationPins[3*i]=NavPhysicalMacro.Acquire(c,actor.Location.NameReference.Owner);c.LocationPins[3*i+1]=NavPhysicalMacro.Acquire(c,actor.GoalLocation?.NameReference.Owner);c.LocationPins[3*i+2]=NavPhysicalMacro.Acquire(c,actor.FailedGoal?.NameReference.Owner);
            }
            int at=0;for(int slot=0;slot<s.Groups.Root.Capacity;slot++)if(s.Groups.Root.TryReadSlot(slot,out long id,out _)){if(!(s.Groups.Root.OwnerFor(id)is NavigationLiveGroup owner))throw new InvalidOperationException("Capture group has no live physical owner.");c.GroupSources[at++]=owner.ObserveForCopy(ObserveLiveWriteToken());}
            for(int i=1;i<c.GroupSources.Length;i++){var value=c.GroupSources[i];int j=i;while(j>0&&c.GroupSources[j-1].Version.Data.GroupId>value.Version.Data.GroupId){c.GroupSources[j]=c.GroupSources[j-1];j--;}c.GroupSources[j]=value;}
            at=0;for(int slot=0;slot<s.Members.Root.Capacity;slot++)if(s.Members.Root.TryReadSlot(slot,out int entity,out long groupId)){
                int g=-1,m=-1;for(int i=0;i<c.GroupSources.Length;i++)if(c.GroupSources[i].Version.Data.GroupId==groupId){g=i;break;}if(g<0)throw new InvalidOperationException("Capture member group absent.");var version=c.GroupSources[g].Version;
                for(int i=0;i<version.Members.Count;i++)if(((NavigationAuthorityMember)version.Members[i]).Data.Entity==entity){m=i;break;}if(m<0)throw new InvalidOperationException("Capture member slot absent.");var old=((NavigationAuthorityMember)version.Members[m]).Data;var value=old;NavUnitSaveState actor=null;foreach(var fact in c.ActorFacts)if(fact.Id==entity){actor=fact;break;}
                if(actor!=null&&actor.Incarnation==old.Incarnation){bool allocating=s.Allocating.Root.ContainsKey(entity);var phase=allocating?GroupMemberPhase.Allocating:old.FormationReleased?GroupMemberPhase.Combat:actor.Held?GroupMemberPhase.Held:s.Pending.Root.ContainsKey(entity)?GroupMemberPhase.PendingRoute:actor.Moving?GroupMemberPhase.March:actor.Outcome==NavigationOutcome.Arrived?GroupMemberPhase.Arrived:actor.Outcome==NavigationOutcome.Unreachable?GroupMemberPhase.Unreachable:actor.Outcome==NavigationOutcome.Blocked||actor.Outcome==NavigationOutcome.Rejected?GroupMemberPhase.Blocked:GroupMemberPhase.Stopped;
                    value=new NavigationAuthorityMember.Values(old.Entity,old.Slot,old.Incarnation,old.OrderRevision,actor.MobilityRevision,phase,old.Goal,old.Location,old.HasLocation,old.HasGoal,old.FormationReleased,old.Branch,allocating?old.Reservation:s.Reservations.Root.ContainsKey(entity)?entity:0,old.ContactTick,old.ProgressRequest,old.Progress,old.ProgressVersion,old.ProgressOffset);
                }c.RefreshRows[at++]=new CaptureGroupRefresh(g,m,value);
            }if(at!=c.RefreshRows.Length)throw new InvalidOperationException("Capture member refresh count changed.");
            at=0;for(int i=0;i<Crowd.Units.Count;i++){var owner=installedExecutions.OwnerFor(Crowd.Units[i].Id);if(owner==null)continue;c.Installed[at]=NavPhysicalMacro.Acquire(c,owner);c.Detached[at++]=installedExecutions.IsDetached(owner.Entity);}if(at!=c.Installed.Length)throw new InvalidOperationException("Capture installed execution has no physical owner.");
            for(int i=1;i<c.Installed.Length;i++){var value=c.Installed[i];bool detached=c.Detached[i];int j=i;while(j>0&&c.Installed[j-1].Entity>value.Entity){c.Installed[j]=c.Installed[j-1];c.Detached[j]=c.Detached[j-1];j--;}c.Installed[j]=value;c.Detached[j]=detached;}
            for(int i=0;i<c.Queued.Length;i++)c.Queued[i]=Requests.ReadPreparedAt(s.RequestRevision,i);for(int i=0;i<c.Registered.Length;i++)c.Registered[i]=s.Registrations.RequestAt(i);at=0;for(int slot=0;slot<s.Pending.Root.Capacity;slot++)if(s.Pending.Root.TryReadSlot(slot,out _,out var request)){c.PendingRows[at]=request;c.HistoricalPending[at++]=CaptureHistoricalPendingShape(c,request);}for(int i=0;i<c.Mailbox.Length;i++)c.Mailbox[i]=Answers.ReadPreparedAt(s.AnswerRevision,i);for(int i=0;i<c.BarrierRows.Length;i++)c.BarrierRows[i]=barrierAnswers[i];for(int i=0;i<c.ProbeRows.Length;i++)c.ProbeRows[i]=liveProbeAnswers.AnswerAt(i);
            IncludeCaptureRequests(c.All,s.Allocating);IncludeCaptureRequests(c.All,s.Pending);IncludeCaptureRequests(c.All,s.Probes);foreach(var answer in c.ProbeRows)c.All.Include(answer.Request);foreach(var request in c.Queued)c.All.Include(request);foreach(var request in c.Registered)c.All.Include(request);for(int i=0;i<(s.Host?.Delivered.Count??0);i++)c.All.Include(((NavigationHostRow)s.Host.Delivered.EntryAt(i).Value).Request);foreach(var answer in c.BarrierRows)c.All.Include(answer.Request);foreach(var answer in c.Mailbox)c.All.Include(answer.Request);
            for(int i=0;i<c.All.Count;i++){var owner=c.All[i].StorageOwner;if(owner==null)throw new InvalidOperationException("Captured request has no physical owner: entity="+c.All[i].Entity+" request="+c.All[i].Request+" pending="+pending.ContainsKey(c.All[i].Entity)+" allocating="+allocating.ContainsKey(c.All[i].Entity)+" probe="+probes.ContainsKey(c.All[i].Entity)+" queued="+Array.IndexOf(c.Queued,c.All[i])+" registered="+Array.IndexOf(c.Registered,c.All[i]));c.RequestPins[i]=NavPhysicalMacro.Acquire(c,owner);}
            at=0;foreach(var answer in c.ProbeRows)c.AnswerPins[at++]=NavPhysicalMacro.Acquire(c,answer.StorageOwner);foreach(var answer in c.BarrierRows)c.AnswerPins[at++]=NavPhysicalMacro.Acquire(c,answer.StorageOwner);foreach(var answer in c.Mailbox)c.AnswerPins[at++]=NavPhysicalMacro.Acquire(c,answer.StorageOwner);
            c.Geometries.Include(geometry);c.BaseGeometry=c.Geometries.Include(navigationGeometry);if(s.Host?.AuthorityGeometry!=null&&!c.Geometries.Contains(s.Host.AuthorityGeometry.Facade)){if(ReferenceEquals(s.Host.AuthorityGeometry,navigationGeometry.StorageRoot))c.Geometries.Alias(s.Host.AuthorityGeometry.Facade,c.BaseGeometry);else c.Geometries.Include(s.Host.AuthorityGeometry.Facade);}
            for(int i=0;i<c.All.Count;i++){c.Geometries.Include(c.All[i].Geometry);c.Geometries.Include(c.All[i].BaseGeometry);}foreach(var owner in c.Installed){c.Geometries.Include(owner.Request.Request.Geometry);c.Geometries.Include(owner.Request.Request.BaseGeometry);}for(int i=0;i<c.Geometries.Count;i++)c.GeometryPins[i]=NavPhysicalMacro.Acquire(c,c.Geometries[i].StorageRoot);
            c.Observed=true;
        }
        private static void IncludeCaptureRequests(NavigationCaptureRequests all,CaptureMapStamp<int,NavigationRequest> map)
        {if(map.Root==null)return;for(int slot=0;slot<map.Root.Capacity;slot++)if(map.Root.TryReadSlot(slot,out _,out var request))all.Include(request);}
        private bool CaptureHistoricalPendingShape(NavigationPreparedCapture c,NavigationRequest request)
        {
            var identity=request.MemberIdentity;if(identity==null||identity.GroupId<1)return false;NavUnitSaveState actor=null;foreach(var fact in c.ActorFacts)if(fact.Id==request.Entity){actor=fact;break;}
            if(actor==null||actor.Incarnation!=identity.Incarnation||identity.MobilityRevision>=actor.MobilityRevision||actor.Held||actor.Moving||actor.Outcome!=NavigationOutcome.Stopped)return false;
            int actorIndex=-1;for(int i=0;i<c.ActorFacts.Length;i++)if(c.ActorFacts[i].Id==request.Entity){actorIndex=i;break;}if((c.Globals[actorIndex]?.Count??0)!=0||(c.Locals[actorIndex]?.Count??0)!=0)return false;
            var s=c.Source;if(!s.Members.Root.TryGetValue(request.Entity,out long group)||group!=identity.GroupId||!s.Groups.Root.TryGetValue(group,out var record))return false;GroupMemberState member=null;for(int i=0;i<record.Members.Length;i++)if(record.Members[i].Entity==request.Entity){member=record.Members[i];break;}if(member==null||member.Incarnation!=identity.Incarnation||member.OrderRevision!=identity.OrderRevision)return false;
            return s.Goals.Root.TryGetValue(request.Entity,out var goal)&&goal.Equals(request.Goal)&&s.Reservations.Root.TryGetValue(request.Entity,out var reservation)&&reservation.Equals(request.Goal)&&s.Identities.Root.TryGetValue(request.Entity,out var held)&&held==request.HeldIdentity&&!s.Allocating.Root.ContainsKey(request.Entity)&&(installedExecutions.OwnerFor(request.Entity)==null||installedExecutions.IsDetached(request.Entity));
        }
        private static string FrozenCaptureHeldIdentity(NavUnitSaveState[] actors,int entity,bool legacy)
        {
            NavUnitSaveState mover=null;foreach(var actor in actors)if(actor.Id==entity){mover=actor;break;}if(mover==null)return "";int count=0;foreach(var other in actors)if(other.Id!=entity&&other.Held&&other.Team==mover.Team)count++;
            var rows=new string[count];int at=0;var culture=System.Globalization.CultureInfo.InvariantCulture;foreach(var other in actors)if(other.Id!=entity&&other.Held&&other.Team==mover.Team)rows[at++]=other.Id+":"+other.Incarnation+":"+other.MobilityRevision+":"+other.Team+":"+other.Position.X.ToString("R",culture)+":"+other.Position.Z.ToString("R",culture)+":"+other.Radius.ToString("R",culture);
            string raw=string.Join("|",rows);return legacy||raw.Length<=256?raw:NavContract.Digest(w=>{w.Write("held-binding-v1");w.Write(raw);});
        }
        private void PrepareCaptureHistoricalBindings(NavigationPreparedCapture c,INavStorageReservation reservation)
        {
            bool any=false;for(int i=0;i<c.PendingRows.Length;i++)if(c.HistoricalPending[i]&&!CaptureRequestAccounted(c,c.PendingRows[i])){any=true;break;}if(!any)return;
            using(NavStorageScratch.Reserve(reservation,RebindStagingScratchBound(c.Source.Actors,c.PendingRows.Length,c.Geometries[0].Obstacles.Count)))for(int i=0;i<c.PendingRows.Length;i++)if(c.HistoricalPending[i]){
                var request=c.PendingRows[i];c.HistoricalPending[i]=request.HeldIdentity!=FrozenCaptureHeldIdentity(c.ActorFacts,request.Entity,false)&&request.HeldIdentity!=FrozenCaptureHeldIdentity(c.ActorFacts,request.Entity,true);
            }
        }
        private static GroupOrderState CaptureGroupVersion(NavigationAuthorityGroup version)
        {
            var d=version.Data;var group=new GroupOrderState{WorldGeneration=d.WorldGeneration,GroupId=d.GroupId,OrderRevision=d.OrderRevision,CommandSequence=d.CommandSequence,IssuedTick=d.IssuedTick,MembershipRevision=d.MembershipRevision,MarchVersion=d.MarchVersion,MarchProfileRevision=d.MarchProfileRevision,MarchStretch=d.MarchStretch,MarchAnchor=d.MarchAnchor,MarchInitialized=d.MarchInitialized,OwnerId=version.OwnerId,Source=version.Source,TeamId=d.TeamId,TargetId=d.TargetId,Origin=d.Origin,Kind=d.Kind,JobId=d.JobId,ActionId=d.ActionId,OriginalGoal=d.OriginalGoal,OriginalLocation=version.OriginalLocation,Members=new GroupMemberState[version.Members.Count]};
            for(int i=0;i<group.Members.Length;i++){var member=(NavigationAuthorityMember)version.Members[i];var m=member.Data;group.Members[i]=new GroupMemberState{Entity=m.Entity,Slot=m.Slot,Incarnation=m.Incarnation,OrderRevision=m.OrderRevision,MobilityRevision=m.MobilityRevision,Phase=m.Phase,Goal=m.Goal,GoalLocation=member.GoalLocation,HasGoal=m.HasGoal,FormationReleased=m.FormationReleased,Branch=m.Branch,Reservation=m.Reservation,ContactTick=m.ContactTick,ContactPhase=member.ContactPhase,ProgressRequest=m.ProgressRequest,Progress=m.Progress,ProgressVersion=m.ProgressVersion,ProgressOffset=m.ProgressOffset};}return group;
        }
        private void PrepareCaptureGroupRefresh(NavigationPreparedCapture c,NavigationRetentionBudget.Scope scope)
        {
            for(int i=0;i<c.Groups.Length;i++){var source=c.GroupSources[i];source.Version.Retain();c.Groups[i]=new RebindGroup{Source=source,Next=source.Version};}
            c.NextAdmission=c.Source.Admission;c.NextAdmission?.Retain();long publication=c.Source.Publication;
            foreach(var row in c.RefreshRows){ref var group=ref c.Groups[row.Group];var member=(NavigationAuthorityMember)group.Next.Members[row.Member];if(member.Data.BitwiseSame(row.Value))continue;
                NavigationAuthorityMember nextMember=null;NavigationAuthorityGroup next=null;
                try{nextMember=NavigationAuthorityMember.CopyWithValuesOwned(member,row.Value,scope.OwnedReservation());next=NavigationAuthorityGroup.CopyWithMemberAndMarchOwned(group.Next,row.Member,nextMember,group.Next.Data,scope.OwnedReservation());var previous=group.Next;group.Next=next;next=null;previous.Release();group.Writes++;c.RefreshWrites++;}
                finally{next?.ReleaseStorage();nextMember?.ReleaseStorage();}
                var admission=PrepareAdmissionProjection(c.Source.Groups.Root,c.Source.Pending.Root,c.Source.RequestSequence,checked(publication+1),c.Source.Operation,scope,c.NextAdmission,sourceProbes:c.Source.Probes.Root??probes,rebindGroups:c.Groups,rebindGroupCount:c.Groups.Length);c.NextAdmission?.Release();c.NextAdmission=admission;publication=admission.Value.PublicationIdentity;
            }
            if(c.RefreshWrites==0&&!c.AdmissionUnchanged){var admission=PrepareAdmissionProjection(c.Source.Groups.Root,c.Source.Pending.Root,c.Source.RequestSequence,checked(publication+1),c.Source.Operation,scope,c.NextAdmission,sourceProbes:c.Source.Probes.Root??probes,rebindGroups:c.Groups,rebindGroupCount:c.Groups.Length);c.NextAdmission?.Release();c.NextAdmission=admission;}
        }
        private void PrepareCaptureDtoGraph(NavigationPreparedCapture c,INavStorageReservation reservation)
        {
            if(!c.Observed||c.State!=null)throw new InvalidOperationException("Capture shapes are not ready.");
            var source=c.Source;c.DtoBytes=CapturePreparedDtoLayout(c);var dto=NavigationCaptureDtoStorage.CreateOwned(c.DtoBytes,reservation);NavigationSessionState state=null;
            try{
                state=new NavigationSessionState{CaptureDto=dto};c.State=state;dto.Bind(state);
                state.TransportVersion=1;state.CorridorWireVersion=1;state.Generation=Generation;state.RequestSequence=source.RequestSequence;state.GroupSequence=source.GroupSequence;state.AppliedResults=source.Applied;state.RejectedResults=source.Rejected;state.UnreachableResults=source.Unreachable;state.SurfaceSemanticsVersion=terrain?.SurfaceSemanticsVersion??1;state.SurfaceProviderId=terrain?.Id??NavLocation.FlatSurface;state.NavigationGeometryIndex=c.BaseGeometry;state.GroupLayoutVersion=1;
                state.Geometry=SaveGeometry(c.Geometries[0]);state.NavigationGeometry=SaveGeometry(c.Geometries[c.BaseGeometry]);
                var crowdGeometry=c.CrowdGeometryFacade;var crowdProfile=c.CrowdProfileFacade;
                state.Crowd=new NavCrowdSaveState{Obstacles=new NavObstacle[crowdGeometry.Obstacles.Count],ProfileValues=SaveProfile(crowdProfile),Units=new NavUnitSaveState[source.Actors]};
                for(int i=0;i<state.Crowd.Units.Length;i++)state.Crowd.Units[i]=new NavUnitSaveState{Route=new NavPoint[c.Globals[i]?.Count??0],LocalRoute=new NavPoint[c.Locals[i]?.Count??0]};
                state.Groups=new GroupOrderState[source.Groups.Count];for(int i=0;i<state.Groups.Length;i++)state.Groups[i]=CaptureGroupVersion(c.Groups[i].Next);
                state.GroupKeys=new long[source.Groups.Count];state.MemberGroupKeys=new int[source.Members.Count];state.GroupLayouts=new[]{new int[source.Groups.LayoutLength],new int[source.Members.LayoutLength]};
                state.Requests=new NavigationRequestState[c.All.Count];for(int i=0;i<c.All.Count;i++){
                    var r=c.All[i];int g=c.Geometries.Include(r.Geometry),b=c.Geometries.Include(r.BaseGeometry);
                    state.Requests[i]=new NavigationRequestState{Session=r.Session,Request=r.Request,Entity=r.Entity,Order=r.Order,MemberIdentity=r.MemberIdentity,Start=r.Start,Goal=r.Goal,StartLocation=r.StartLocation,GoalLocation=r.GoalLocation,ProfileId=r.ProfileId,ProfileRevision=r.ProfileRevision,ProfileValues=SaveProfile(r.Profile),CertifiedProviderBinding=r.CertifiedProviderBinding,Geometry=SaveGeometry(r.Geometry),BaseGeometry=SaveGeometry(r.BaseGeometry),GeometryIndex=g,BaseGeometryIndex=b,HeldIdentity=r.HeldIdentity,UsesCurrentGeometry=g==c.BaseGeometry,UsesCurrentBaseGeometry=b==c.BaseGeometry};
                }
                state.InstalledExecutions=new NavigationInstalledExecutionState[c.Installed.Length];for(int i=0;i<c.Installed.Length;i++){
                    var owner=c.Installed[i];var r=owner.Request.Request;state.InstalledExecutions[i]=new NavigationInstalledExecutionState{Entity=owner.Entity,Detached=c.Detached[i],Corridor=owner.Corridor.Corridor,InstalledRoute=new NavPoint[owner.Route.Count],ProfileId=r.ProfileId,ProfileRevision=r.ProfileRevision,ProfileValues=SaveProfile(r.Profile),HeldIdentity=r.HeldIdentity,GeometryIndex=c.Geometries.Include(r.Geometry),BaseGeometryIndex=c.Geometries.Include(r.BaseGeometry),ExecutionGeometryIndex=owner.Certificate==null?-1:c.Geometries.Include(owner.CurrentGeometry)};for(int n=0;n<owner.Route.Count;n++)state.InstalledExecutions[i].InstalledRoute[n]=owner.Route[n];
                }
                state.Orders=new NavigationEntityOrderState[source.Orders.Count];for(int i=0;i<state.Orders.Length;i++)state.Orders[i]=new NavigationEntityOrderState();
                state.Reservations=new NavigationEntityPointState[source.Reservations.Count];for(int i=0;i<state.Reservations.Length;i++)state.Reservations[i]=new NavigationEntityPointState();
                state.RetainedGoals=new NavigationEntityPointState[source.Goals.Count];for(int i=0;i<state.RetainedGoals.Length;i++)state.RetainedGoals[i]=new NavigationEntityPointState();
                state.MovementIdentities=new NavigationMovementIdentityState[source.Identities.Count];for(int i=0;i<state.MovementIdentities.Length;i++)state.MovementIdentities[i]=new NavigationMovementIdentityState();
                state.TechnicalFailures=new NavigationTechnicalFailureState[source.Failures.Count];for(int i=0;i<state.TechnicalFailures.Length;i++)state.TechnicalFailures[i]=new NavigationTechnicalFailureState();
                state.ProbeRequestIndices=new int[source.Probes.Count];state.PendingRequestIndices=new int[source.Pending.Count];state.RequestMailboxIndices=new int[c.Queued.Length];
                state.ProbeAnswers=new NavigationAnswerState[c.ProbeRows.Length];for(int i=0;i<state.ProbeAnswers.Length;i++)state.ProbeAnswers[i]=CaptureAnswerGraph(c,c.ProbeRows[i]);
                state.AnswerMailbox=new NavigationAnswerState[c.Mailbox.Length];for(int i=0;i<state.AnswerMailbox.Length;i++)state.AnswerMailbox[i]=CaptureAnswerGraph(c,c.Mailbox[i]);
                state.BarrierAnswers=new NavigationAnswerState[c.BarrierRows.Length];for(int i=0;i<state.BarrierAnswers.Length;i++)state.BarrierAnswers[i]=CaptureAnswerGraph(c,c.BarrierRows[i]);
                state.CollectionLayouts=new[]{new int[source.Pending.LayoutLength],new int[source.Orders.LayoutLength],new int[source.Reservations.LayoutLength],new int[source.Goals.LayoutLength],new int[source.Probes.LayoutLength],new int[source.Identities.LayoutLength]};
                state.GeometryTable=new NavigationGeometryState[c.Geometries.Count];for(int i=0;i<state.GeometryTable.Length;i++)state.GeometryTable[i]=SaveGeometry(c.Geometries[i]);
                state.AllocationRequestIndices=new int[source.Allocating.Count];state.DeliveredRequestIndices=new int[(source.Host?.Delivered.Count??0)];state.RegisteredRequestIndices=new int[source.RegistrationCount];
                state.CaptureRequests=new NavOwnedStorage[c.All.Count];state.CaptureGeometries=new NavOwnedStorage[c.Geometries.Count];state.CaptureGroups=new NavOwnedStorage[source.Groups.Count];state.CaptureIdentities=new NavOwnedStorage[source.Identities.Count];
                if(source.Probes.Root!=null&&source.Queue!=null){state.CaptureProbeAnswers=new NavOwnedStorage[c.ProbeRows.Length];state.CaptureMailboxAnswers=new NavOwnedStorage[c.Mailbox.Length];state.CaptureBarrierAnswers=new NavOwnedStorage[c.BarrierRows.Length];}
                if(source.Tracking.Root!=null){state.CaptureAnswerTrackingOwners=new NavOwnedStorage[source.Tracking.Count];state.CaptureAnswerTrackingLayout=new int[checked(2+source.Tracking.LayoutLength+2*source.Tracking.Count)];}
                state.CaptureExtraLayouts=new[]{new int[source.Allocating.LayoutLength],new int[source.Allocating.Count],new int[source.Failures.LayoutLength],new int[source.Failures.Count],new int[source.Probes.Root!=null&&source.Queue!=null?12:10],new int[source.Ledger.LayoutLength],new int[source.Ledger.Count]};
                state.CaptureCrowdNames=new NavNameReference[source.Actors*3];state.CaptureCrowdPoints=new NavPoint[source.Actors*3];state.CaptureCrowdKeys=new int[source.Actors];state.CaptureCrowdFlags=new int[source.Actors];state.CaptureActorGlobal=new NavOwnedStorage[source.Actors];state.CaptureActorLocal=new NavOwnedStorage[source.Actors];state.CaptureInstalled=new NavOwnedStorage[source.Installed];
            }catch{c.State=null;if(state!=null)state.Dispose();else dto.Dispose();throw;}
        }
        private static NavigationAnswerState CaptureAnswerGraph(NavigationPreparedCapture c,NavigationAnswer answer)
            =>new NavigationAnswerState{RequestIndex=c.All.Index(answer.Request),Route=answer.CopyRoute(),Status=answer.Status,Corridor=answer.Corridor};
        private void ObserveCaptureLedger(NavigationPreparedCapture c)
        {
            if(!CaptureSourceFactsCurrent(c))throw new InvalidOperationException("Capture ledger lost its source.");int at=0;var map=c.Source.Ledger.Root;
            for(int slot=0;slot<map.Capacity;slot++)if(map.TryReadSlot(slot,out long key,out var value)){
                var owner=map.OwnerFor(key) as NavigationLiveArrivalReservation;if(owner==null||!ReferenceEquals(owner.Entry,value))throw new InvalidOperationException("Capture ledger has no exact owned entry.");c.LedgerPins[at]=NavPhysicalMacro.Acquire(c,owner);c.LedgerKeys[at]=key;var target=c.LedgerRows[at++];target.CommitOrder=value.CommitOrder;target.Id=value.Id;target.World=value.World;target.Group=value.Group;target.Root=value.Root;target.Command=value.Command;target.IssuedTick=value.IssuedTick;target.Activation=value.Activation;target.Entity=value.Entity;target.Incarnation=value.Incarnation;target.Mobility=value.Mobility;target.Request=value.Request;target.Order=value.Order;target.Endpoint=value.Endpoint;target.Radius=value.Radius;target.Proof=value.Proof;target.Provider=value.Provider;target.Profile=value.Profile;target.Hold=value.Hold;target.State=value.State;
            }
            if(at!=c.LedgerRows.Length)throw new InvalidOperationException("Capture ledger count changed.");for(int i=1;i<at;i++){long key=c.LedgerKeys[i];var row=c.LedgerRows[i];var pin=c.LedgerPins[i];int j=i;while(j>0&&c.LedgerKeys[j-1]>key){c.LedgerKeys[j]=c.LedgerKeys[j-1];c.LedgerRows[j]=c.LedgerRows[j-1];c.LedgerPins[j]=c.LedgerPins[j-1];j--;}c.LedgerKeys[j]=key;c.LedgerRows[j]=row;c.LedgerPins[j]=pin;}CopyCaptureLayout(c.Source.Ledger,c.State.CaptureExtraLayouts[5]);int live=0;
            for(int slot=0;slot<map.Capacity;slot++)if(map.TryReadSlot(slot,out long key,out _))c.State.CaptureExtraLayouts[6][live++]=System.Array.BinarySearch(c.LedgerKeys,key);
            if(live!=c.LedgerRows.Length||!CaptureSourceFactsCurrent(c))throw new InvalidOperationException("Capture ledger layout source changed.");c.WireLeaseCapacity=ArrivalLeaseCapacity;
        }
        private static void CopyCaptureLayout<K,V>(CaptureMapStamp<K,V> map,int[] target)
        {if(map.Root==null){if(target.Length!=1)throw new InvalidOperationException("Empty capture layout shape changed.");target[0]=0;}else map.Root.CopyLayoutTo(target);}
        private static void CopyCaptureKeys<K,V>(CaptureMapStamp<K,V> map,K[] target)
        {int at=0;if(map.Root!=null)for(int slot=0;slot<map.Root.Capacity;slot++)if(map.Root.TryReadSlot(slot,out var key,out _))target[at++]=key;if(at!=target.Length)throw new InvalidOperationException("Capture key count changed.");}
        private bool CaptureRequestAccounted(NavigationPreparedCapture c,NavigationRequest request)
        {
            foreach(var r in c.Queued)if(ReferenceEquals(r,request))return true;
            foreach(var r in c.Registered)if(ReferenceEquals(r,request))return true;
            foreach(var answer in c.Mailbox)if(ReferenceEquals(answer.Request,request))return true;
            foreach(var answer in c.BarrierRows)if(ReferenceEquals(answer.Request,request))return true;return false;
        }
        private void ObservePreparedCaptureDto(NavigationPreparedCapture c)
        {
            if(!CaptureSourceFactsCurrent(c)||c.State==null)throw new InvalidOperationException("Capture source changed before DTO observation.");
            var state=c.State;var s=c.Source;var dto=state.CaptureDto;
            Crowd.CopyCaptureStateTo(state.Crowd);
            CopyCaptureKeys(s.Groups,state.GroupKeys);CopyCaptureKeys(s.Members,state.MemberGroupKeys);CopyCaptureLayout(s.Groups,state.GroupLayouts[0]);CopyCaptureLayout(s.Members,state.GroupLayouts[1]);
            int at=0;for(int slot=0;slot<s.Orders.Root.Capacity;slot++)if(s.Orders.Root.TryReadSlot(slot,out int id,out long order)){state.Orders[at].Entity=id;state.Orders[at++].Order=order;}
            at=0;for(int slot=0;slot<s.Reservations.Root.Capacity;slot++)if(s.Reservations.Root.TryReadSlot(slot,out int id,out var point)){state.Reservations[at].Entity=id;state.Reservations[at++].Point=point;}
            at=0;for(int slot=0;slot<s.Goals.Root.Capacity;slot++)if(s.Goals.Root.TryReadSlot(slot,out int id,out var point)){state.RetainedGoals[at].Entity=id;state.RetainedGoals[at++].Point=point;}
            at=0;for(int slot=0;slot<s.Identities.Root.Capacity;slot++)if(s.Identities.Root.TryReadSlot(slot,out int id,out var identity)){
                state.MovementIdentities[at].Entity=id;state.MovementIdentities[at].Identity=identity;var owner=s.Identities.Root.OwnerFor(id);if(identity!=null&&(!(owner is NavOwnedText text)||text.Value!=identity))throw new InvalidOperationException("Capture movement identity has no exact owner.");state.CaptureIdentities[at++]=NavPhysicalMacro.Acquire(dto,owner);
            }
            at=0;if(s.Probes.Root!=null)for(int slot=0;slot<s.Probes.Root.Capacity;slot++)if(s.Probes.Root.TryReadSlot(slot,out _,out var request)){if(!CaptureRequestAccounted(c,request))throw new InvalidOperationException("Captured producer is outside frozen transport.");state.ProbeRequestIndices[at++]=c.All.Index(request);}
            at=0;for(int slot=0;slot<s.Pending.Root.Capacity;slot++)if(s.Pending.Root.TryReadSlot(slot,out _,out var request)){
                if(!CaptureRequestAccounted(c,request)&&!c.HistoricalPending[at])throw new InvalidOperationException("Captured pending is outside frozen transport.");state.PendingRequestIndices[at++]=c.All.Index(request);
            }
            for(int i=0;i<c.Queued.Length;i++)state.RequestMailboxIndices[i]=c.All.Index(c.Queued[i]);
            at=0;for(int slot=0;slot<s.Allocating.Root.Capacity;slot++)if(s.Allocating.Root.TryReadSlot(slot,out _,out var request))state.AllocationRequestIndices[at++]=c.All.Index(request);
            for(int i=1;i<state.AllocationRequestIndices.Length;i++){int value=state.AllocationRequestIndices[i],j=i;while(j>0&&c.All[state.AllocationRequestIndices[j-1]].Request>c.All[value].Request){state.AllocationRequestIndices[j]=state.AllocationRequestIndices[j-1];j--;}state.AllocationRequestIndices[j]=value;}
            for(int i=0;i<state.DeliveredRequestIndices.Length;i++)state.DeliveredRequestIndices[i]=c.All.Index(((NavigationHostRow)s.Host.Delivered.EntryAt(i).Value).Request);
            for(int i=0;i<state.RegisteredRequestIndices.Length;i++)state.RegisteredRequestIndices[i]=c.All.Index(c.Registered[i]);
            at=0;for(int slot=0;slot<s.Failures.Root.Capacity;slot++)if(s.Failures.Root.TryReadSlot(slot,out int id,out var status)){if(!Crowd.TryGet(id,out var actor))throw new InvalidOperationException("Capture failure actor absent.");state.TechnicalFailures[at].Entity=id;state.TechnicalFailures[at].Request=actor.FailedRequest;state.TechnicalFailures[at++].Status=status;}
            for(int i=1;i<state.TechnicalFailures.Length;i++){var value=state.TechnicalFailures[i];int j=i;while(j>0&&state.TechnicalFailures[j-1].Entity>value.Entity){state.TechnicalFailures[j]=state.TechnicalFailures[j-1];j--;}state.TechnicalFailures[j]=value;}
            CopyCaptureLayout(s.Pending,state.CollectionLayouts[0]);CopyCaptureLayout(s.Orders,state.CollectionLayouts[1]);CopyCaptureLayout(s.Reservations,state.CollectionLayouts[2]);CopyCaptureLayout(s.Goals,state.CollectionLayouts[3]);CopyCaptureLayout(s.Probes,state.CollectionLayouts[4]);CopyCaptureLayout(s.Identities,state.CollectionLayouts[5]);
            CopyCaptureLayout(s.Allocating,state.CaptureExtraLayouts[0]);CopyCaptureKeys(s.Allocating,state.CaptureExtraLayouts[1]);CopyCaptureLayout(s.Failures,state.CaptureExtraLayouts[2]);CopyCaptureKeys(s.Failures,state.CaptureExtraLayouts[3]);
            var caps=state.CaptureExtraLayouts[4];caps[0]=s.Groups.Root.Capacity;caps[1]=s.Members.Root.Capacity;caps[2]=s.Orders.Root.Capacity;caps[3]=s.Pending.Root.Capacity;caps[4]=s.Allocating.Root.Capacity;caps[5]=s.Reservations.Root.Capacity;caps[6]=s.Goals.Root.Capacity;caps[7]=s.Identities.Root.Capacity;caps[8]=s.Failures.Root.Capacity;caps[9]=s.Ledger.Root.Capacity;if(caps.Length==12){caps[10]=s.Probes.Root.Capacity;caps[11]=s.Queue.Capacity;}
            for(int i=0;i<state.CaptureRequests.Length;i++)state.CaptureRequests[i]=NavPhysicalMacro.Acquire(dto,c.RequestPins[i]);for(int i=0;i<state.CaptureGeometries.Length;i++)state.CaptureGeometries[i]=NavPhysicalMacro.Acquire(dto,c.GeometryPins[i]);
            // Catalog roles deliberately retain occupied slot order, while the
            // public DTO groups above are sorted by group ID.
            at=0;for(int slot=0;slot<s.Groups.Root.Capacity;slot++)if(s.Groups.Root.TryReadSlot(slot,out long id,out var record)){
                var live=s.Groups.Root.OwnerFor(id) as NavigationLiveGroup;var version=live?.ImmutableVersion;if(version==null||!MarchRecordMatches(record,version)||record.OwnerId!=version.OwnerId||record.Source!=version.Source)throw new InvalidOperationException("Capture group record differs from immutable source.");for(int m=0;m<record.Members.Length;m++){var member=(NavigationAuthorityMember)version.Members[m];if(member.ContactPhase!=record.Members[m].ContactPhase||member.GoalLocation?.SurfaceId!=record.Members[m].GoalLocation?.SurfaceId)throw new InvalidOperationException("Capture member location/contact differs from immutable source.");}var captured=version;for(int g=0;g<c.Groups.Length;g++)if(c.Groups[g].Source.Version.Data.GroupId==id){captured=c.Groups[g].Next;break;}state.CaptureGroups[at++]=NavPhysicalMacro.Acquire(dto,captured);
            }
            if(state.CaptureProbeAnswers!=null){for(int i=0;i<c.ProbeRows.Length;i++)state.CaptureProbeAnswers[i]=NavPhysicalMacro.Acquire(dto,c.ProbeRows[i].StorageOwner);for(int i=0;i<c.Mailbox.Length;i++)state.CaptureMailboxAnswers[i]=NavPhysicalMacro.Acquire(dto,c.Mailbox[i].StorageOwner);for(int i=0;i<c.BarrierRows.Length;i++)state.CaptureBarrierAnswers[i]=NavPhysicalMacro.Acquire(dto,c.BarrierRows[i].StorageOwner);}
            if(s.Tracking.Root!=null){
                var data=state.CaptureAnswerTrackingLayout;data[0]=s.Tracking.Root.Capacity;data[1]=s.Tracking.LayoutLength;s.Tracking.Root.CopyLayoutTo(data,2);long bytes=0;at=0;
                for(int slot=0;slot<s.Tracking.Root.Capacity;slot++)if(s.Tracking.Root.TryReadSlot(slot,out var answer,out var owner)){
                    if(owner==null||!ReferenceEquals(answer,owner.Answer)||answer.Corridor==null||!ReferenceEquals(owner.RequestOwner.TransportOwner,transportOwner)||owner.RequestOwner.TransportAnswers<1)throw new InvalidOperationException("Capture tracked answer has invalid transport roles.");state.CaptureAnswerTrackingOwners[at]=NavPhysicalMacro.Acquire(dto,owner);int index=2+data[1]+2*at++;data[index]=owner.RequestOwner.TransportAnswers;data[index+1]=owner.RequestOwner.TransportExternal?1:0;bytes=checked(bytes+answer.Corridor.EstimatedRetainedBytes);
                }if(bytes!=s.AnswerBytes)throw new InvalidOperationException("Capture tracked answer charge differs from source.");
            }
            for(int i=0;i<state.Crowd.Units.Length;i++){
                var actor=state.Crowd.Units[i];state.CaptureCrowdKeys[i]=actor.Id;state.CaptureCrowdFlags[i]=(actor.GoalLocation.HasValue?1:0)|(actor.FailedGoal.HasValue?2:0);var location=OwnedOrFlatName(actor.Location);var goal=OwnedOrFlatName(actor.GoalLocation);var failure=OwnedOrFlatName(actor.FailedGoal);
                location.Owner?.Retain();state.CaptureCrowdNames[3*i]=location;goal.Owner?.Retain();state.CaptureCrowdNames[3*i+1]=goal;failure.Owner?.Retain();state.CaptureCrowdNames[3*i+2]=failure;state.CaptureCrowdPoints[3*i]=actor.Location.Value.Position;state.CaptureCrowdPoints[3*i+1]=actor.GoalLocation?.Position??default;state.CaptureCrowdPoints[3*i+2]=actor.FailedGoal?.Position??default;state.CaptureActorGlobal[i]=NavPhysicalMacro.Acquire(dto,c.Globals[i]);state.CaptureActorLocal[i]=NavPhysicalMacro.Acquire(dto,c.Locals[i]);
            }
            for(int i=0;i<c.Installed.Length;i++)state.CaptureInstalled[i]=NavPhysicalMacro.Acquire(dto,c.Installed[i]);state.CaptureTerrainNames=NavPhysicalMacro.Acquire(dto,s.Terrain);state.CaptureTraffic=NavPhysicalMacro.Acquire(dto,s.Traffic);state.CaptureBank=NavPhysicalMacro.Acquire(dto,s.Bank);state.CaptureRoot=NavPhysicalMacro.Acquire(dto,s.Host);state.CaptureAuthority=NavPhysicalMacro.Acquire(dto,s.Core);state.CaptureBudget=physicalHostBudget;
        }
        private static TKey[] CaptureKeys<TKey,TValue>(IDictionary<TKey,TValue> values)
        {var result=new TKey[values.Count];int i=0;foreach(var pair in values)result[i++]=pair.Key;return result;}
        private GroupOrderState[] CaptureGroupsDirect()
        {
            var result=new GroupOrderState[groups.Count];int i=0;
            foreach(var pair in groups){var value=pair.Value.CopyHeader();value.Members=new GroupMemberState[pair.Value.Members.Length];for(int m=0;m<value.Members.Length;m++)value.Members[m]=pair.Value.Members[m].Copy();result[i++]=value;}
            for(i=1;i<result.Length;i++){var value=result[i];int j=i;while(j>0&&result[j-1].GroupId>value.GroupId){result[j]=result[j-1];j--;}result[j]=value;}return result;
        }
        private NavigationInstalledExecutionState[] CaptureInstalledDirect(NavigationCaptureGeometries geometries)
        {
            var result=new NavigationInstalledExecutionState[installedExecutions.Count];int i=0;
            foreach(var x in installedExecutions.Values)result[i++]=new NavigationInstalledExecutionState{Entity=x.Entity,Detached=installedExecutions.IsDetached(x.Entity),Corridor=x.Corridor,InstalledRoute=(NavPoint[])x.InstalledRoute.Clone(),ProfileId=x.Profile.ProfileId,ProfileRevision=x.Profile.Revision,ProfileValues=SaveProfile(x.Profile),HeldIdentity=x.HeldIdentity,GeometryIndex=geometries.Include(x.MovementGeometry),BaseGeometryIndex=geometries.Include(x.BaseGeometry),ExecutionGeometryIndex=x.StorageOwner?.Certificate==null?-1:geometries.Include(x.StorageOwner.CurrentGeometry)};
            for(i=1;i<result.Length;i++){var value=result[i];int j=i;while(j>0&&result[j-1].Entity>value.Entity){result[j]=result[j-1];j--;}result[j]=value;}return result;
        }
        private NavigationTechnicalFailureState[] CaptureFailuresDirect()
        {
            var result=new NavigationTechnicalFailureState[technicalFailures.Count];int i=0;
            foreach(var pair in technicalFailures){if(!Crowd.TryGet(pair.Key,out var actor))throw new InvalidOperationException("Failure actor absent.");result[i++]=new NavigationTechnicalFailureState{Entity=pair.Key,Request=actor.FailedRequest,Status=pair.Value};}
            for(i=1;i<result.Length;i++){var value=result[i];int j=i;while(j>0&&result[j-1].Entity>value.Entity){result[j]=result[j-1];j--;}result[j]=value;}return result;
        }
        private int[] CaptureAllocationIndices(NavigationCaptureRequests all)
        {
            var result=new int[allocating.Count];int i=0;foreach(var pair in allocating)result[i++]=all.Index(pair.Value);
            for(i=1;i<result.Length;i++){int value=result[i],j=i;while(j>0&&all[result[j-1]].Request>all[value].Request){result[j]=result[j-1];j--;}result[j]=value;}return result;
        }
        private int[] CaptureDeliveredIndices(NavigationCaptureRequests all)
        {if(committedHostRoot==null)return Array.Empty<int>();var result=new int[committedHostRoot.Delivered.Count];int i=0;foreach(var entry in committedHostRoot.Delivered)result[i++]=all.Index(((NavigationHostRow)entry.Value).Request);return result;}
        private int[] CaptureRegisteredIndices(NavigationCaptureRequests all)
        {if(committedHostRoot==null)return Array.Empty<int>();var result=new int[RegisteredRequests.Count];int i=0;foreach(var entry in RegisteredRequests)result[i++]=all.Index(entry);return result;}
        private NavigationRequest[] CaptureQueuedRequests()
        {
            var queued=Requests.CopyItems();if(committedHostRoot!=null)return queued;
            var result=new NavigationRequest[RegisteredRequests.Count+queued.Length];int i=0;foreach(var r in RegisteredRequests)result[i++]=r;for(int j=0;j<queued.Length;j++)result[i++]=queued[j];
            for(i=1;i<result.Length;i++){var value=result[i];int j=i;while(j>0&&result[j-1].Request>value.Request){result[j]=result[j-1];j--;}result[j]=value;}return result;
        }
        private long CapturePreparedDtoLayout(NavigationPreparedCapture c)
        {
            var s=c.Source;var all=c.All;var geometries=c.Geometries;var queued=c.Queued;var answers=c.Mailbox;
            long bytes=NavigationCaptureDtoStorage.RecordBytes<NavigationSessionState>();
            long A<T>(int count)=>NavigationCaptureDtoStorage.ArrayBytes<T>(count);
            long R<T>()where T:class=>NavigationCaptureDtoStorage.RecordBytes<T>();
            long G(NavGeometry g)=>checked(R<NavigationGeometryState>()+A<NavObstacle>(g.Obstacles.Count));
            long L<TKey,TValue>(CaptureMapStamp<TKey,TValue> map)=>A<int>(map.LayoutLength);
            bytes=checked(bytes+A<NavigationInstalledExecutionState>(s.Installed));
            foreach(var x in c.Installed)bytes=checked(bytes+R<NavigationInstalledExecutionState>()+A<NavPoint>(x.Route.Count)+A<double>(x.Request.Request.Profile.WireValueCount));
            bytes=checked(bytes+A<GroupOrderState>(s.Groups.Count)+A<long>(s.Groups.Count)+A<int>(s.Members.Count)+A<int[]>(2)+L(s.Groups)+L(s.Members));
            foreach(var observation in c.GroupSources)bytes=checked(bytes+R<GroupOrderState>()+A<GroupMemberState>(observation.Version.Members.Count)+observation.Version.Members.Count*R<GroupMemberState>());
            bytes=checked(bytes+G(c.Geometries[0])+G(c.Geometries[c.BaseGeometry])+R<NavCrowdSaveState>()+A<NavObstacle>((c.CrowdGeometryFacade).Obstacles.Count)+A<double>(c.CrowdProfileFacade.WireValueCount)+A<NavUnitSaveState>(s.Actors));
            for(int i=0;i<s.Actors;i++)bytes=checked(bytes+R<NavUnitSaveState>()+A<NavPoint>(c.Globals[i]?.Count??0)+A<NavPoint>(c.Locals[i]?.Count??0));
            bytes=checked(bytes+A<NavigationRequestState>(all.Count));
            for(int i=0;i<all.Count;i++)bytes=checked(bytes+R<NavigationRequestState>()+A<double>(all[i].Profile.WireValueCount)+G(all[i].Geometry)+G(all[i].BaseGeometry));
            bytes=checked(bytes+A<NavigationEntityOrderState>(s.Orders.Count)+s.Orders.Count*R<NavigationEntityOrderState>()+A<NavigationEntityPointState>(s.Reservations.Count)+s.Reservations.Count*R<NavigationEntityPointState>()+A<NavigationEntityPointState>(s.Goals.Count)+s.Goals.Count*R<NavigationEntityPointState>()+A<NavigationMovementIdentityState>(s.Identities.Count)+s.Identities.Count*R<NavigationMovementIdentityState>()+A<NavigationTechnicalFailureState>(s.Failures.Count)+s.Failures.Count*R<NavigationTechnicalFailureState>());
            bytes=checked(bytes+A<int>(s.Probes.Count)+A<int>(s.Pending.Count)+A<int>(queued.Length)+A<NavigationAnswerState>(s.QueueCount)+A<NavigationAnswerState>(answers.Length)+A<NavigationAnswerState>(s.BarrierCount));
            foreach(var answer in c.ProbeRows)bytes=checked(bytes+R<NavigationAnswerState>()+A<NavPoint>(answer.RouteLength));
            foreach(var answer in answers)bytes=checked(bytes+R<NavigationAnswerState>()+A<NavPoint>(answer.RouteLength));
            foreach(var answer in c.BarrierRows)bytes=checked(bytes+R<NavigationAnswerState>()+A<NavPoint>(answer.RouteLength));
            bytes=checked(bytes+A<int[]>(6)+L(s.Pending)+L(s.Orders)+L(s.Reservations)+L(s.Goals)+L(s.Probes)+L(s.Identities)+A<NavigationGeometryState>(geometries.Count));
            for(int i=0;i<geometries.Count;i++)bytes=checked(bytes+G(geometries[i]));
            bytes=checked(bytes+A<int>(s.Allocating.Count)+(A<int>(s.Host?.Delivered.Count??0)+A<int>(s.RegistrationCount))+A<NavOwnedStorage>(all.Count)+A<NavOwnedStorage>(geometries.Count)+A<NavOwnedStorage>(s.Groups.Count)+A<NavOwnedStorage>(s.Identities.Count));
            bytes=checked(bytes+A<int[]>(7)+L(s.Ledger)+A<int>(s.Ledger.Count)+L(s.Allocating)+A<int>(s.Allocating.Count)+L(s.Failures)+A<int>(s.Failures.Count)+A<int>(s.Probes.Root!=null&&s.Queue!=null?12:10)+A<NavNameReference>(s.Actors*3)+A<NavPoint>(s.Actors*3)+2*A<int>(s.Actors));
            if(s.Tracking.Root!=null)bytes=checked(bytes+A<NavOwnedStorage>(s.Tracking.Count)+A<int>(2+s.Tracking.LayoutLength+2*s.Tracking.Count));
            return checked(bytes+2*A<NavOwnedStorage>(s.Actors)+A<NavOwnedStorage>(s.Installed)+(s.Probes.Root!=null&&s.Queue!=null?A<NavOwnedStorage>(s.QueueCount)+A<NavOwnedStorage>(answers.Length)+A<NavOwnedStorage>(s.BarrierCount):0));
        }
        private long CaptureDtoLayout(NavigationCaptureRequests all,NavigationCaptureGeometries geometries,NavigationRequest[] queued,NavigationAnswer[] answers)
        {
            long bytes=NavigationCaptureDtoStorage.RecordBytes<NavigationSessionState>();
            long A<T>(int count)=>NavigationCaptureDtoStorage.ArrayBytes<T>(count);
            long R<T>()where T:class=>NavigationCaptureDtoStorage.RecordBytes<T>();
            long G(NavGeometry g)=>checked(R<NavigationGeometryState>()+A<NavObstacle>(g.Obstacles.Count));
            long L<TKey,TValue>(IDictionary<TKey,TValue> map)=>A<int>(NavigationCommandLayout.Length(map));
            bytes=checked(bytes+A<NavigationInstalledExecutionState>(installedExecutions.Count));
            foreach(var x in installedExecutions.Values)bytes=checked(bytes+R<NavigationInstalledExecutionState>()+A<NavPoint>(x.InstalledRoute.Length)+A<double>(x.Profile.WireValueCount));
            bytes=checked(bytes+A<GroupOrderState>(groups.Count)+A<long>(groups.Count)+A<int>(memberGroups.Count)+A<int[]>(2)+L(groups)+L(memberGroups));
            foreach(var pair in groups)bytes=checked(bytes+R<GroupOrderState>()+A<GroupMemberState>(pair.Value.Members.Length)+pair.Value.Members.Length*R<GroupMemberState>());
            bytes=checked(bytes+G(geometry)+G(navigationGeometry)+R<NavCrowdSaveState>()+A<NavObstacle>(Crowd.Geometry.Obstacles.Count)+A<double>(Crowd.CaptureProfile.WireValueCount)+A<NavUnitSaveState>(Crowd.Units.Count));
            foreach(var actor in Crowd.Units)bytes=checked(bytes+R<NavUnitSaveState>()+A<NavPoint>(actor.route.Length)+A<NavPoint>(actor.localRoute.Length));
            bytes=checked(bytes+A<NavigationRequestState>(all.Count));
            for(int i=0;i<all.Count;i++)bytes=checked(bytes+R<NavigationRequestState>()+A<double>(all[i].Profile.WireValueCount)+G(all[i].Geometry)+G(all[i].BaseGeometry));
            bytes=checked(bytes+A<NavigationEntityOrderState>(orders.Count)+orders.Count*R<NavigationEntityOrderState>()+A<NavigationEntityPointState>(reservations.Count)+reservations.Count*R<NavigationEntityPointState>()+A<NavigationEntityPointState>(retainedGoals.Count)+retainedGoals.Count*R<NavigationEntityPointState>()+A<NavigationMovementIdentityState>(movementIdentities.Count)+movementIdentities.Count*R<NavigationMovementIdentityState>()+A<NavigationTechnicalFailureState>(technicalFailures.Count)+technicalFailures.Count*R<NavigationTechnicalFailureState>());
            bytes=checked(bytes+A<int>(probes.Count)+A<int>(pending.Count)+A<int>(queued.Length)+A<NavigationAnswerState>(ProbeAnswerCount)+A<NavigationAnswerState>(answers.Length)+A<NavigationAnswerState>(barrierAnswers.Count));
            foreach(var answer in ProbeAnswerRows)bytes=checked(bytes+R<NavigationAnswerState>()+A<NavPoint>(answer.RouteLength));
            foreach(var answer in answers)bytes=checked(bytes+R<NavigationAnswerState>()+A<NavPoint>(answer.RouteLength));
            foreach(var answer in barrierAnswers)bytes=checked(bytes+R<NavigationAnswerState>()+A<NavPoint>(answer.RouteLength));
            bytes=checked(bytes+A<int[]>(6)+L(pending)+L(orders)+L(reservations)+L(retainedGoals)+L(probes)+L(movementIdentities)+A<NavigationGeometryState>(geometries.Count));
            for(int i=0;i<geometries.Count;i++)bytes=checked(bytes+G(geometries[i]));
            bytes=checked(bytes+A<int>(allocating.Count)+(committedHostRoot==null?0:A<int>(committedHostRoot.Delivered.Count)+A<int>(RegisteredRequests.Count))+A<NavOwnedStorage>(all.Count)+A<NavOwnedStorage>(geometries.Count)+A<NavOwnedStorage>(groups.Count)+A<NavOwnedStorage>(movementIdentities.Count));
            bytes=checked(bytes+A<int[]>(7)+L(arrivalLedger)+A<int>(arrivalLedger.Count)+L(allocating)+A<int>(allocating.Count)+L(technicalFailures)+A<int>(technicalFailures.Count)+A<int>(ownedProbes!=null&&liveProbeAnswers!=null?12:10)+A<NavNameReference>(Crowd.Units.Count*3)+A<NavPoint>(Crowd.Units.Count*3)+2*A<int>(Crowd.Units.Count));
            if(ownedAnswerTracking!=null)bytes=checked(bytes+A<NavOwnedStorage>(ownedAnswerTracking.Count)+A<int>(2+ownedAnswerTracking.LayoutLength+2*ownedAnswerTracking.Count));
            return checked(bytes+2*A<NavOwnedStorage>(Crowd.Units.Count)+A<NavOwnedStorage>(installedExecutions.Count)+(ownedProbes!=null&&liveProbeAnswers!=null?A<NavOwnedStorage>(ProbeAnswerCount)+A<NavOwnedStorage>(answers.Length)+A<NavOwnedStorage>(barrierAnswers.Count):0));
        }
    }
}

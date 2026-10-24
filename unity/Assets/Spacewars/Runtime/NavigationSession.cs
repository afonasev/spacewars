using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Version eight was used independently by two branches. Import callers must
    // name the source grammar; neither the payload nor parse failure chooses it.
    public enum NavigationV8Format { Unspecified, A5Arrival, MainInstalledLayout }
    [Serializable] public sealed class NavigationGeometryState
    {
        public double HalfExtent; public int Revision; public NavObstacle[] Obstacles;
    }
    [Serializable] public sealed class NavigationRequestState
    {
        public long Session, Request, Order; public int Entity;
        public NavigationMemberIdentity MemberIdentity;
        public NavPoint Start, Goal; public NavLocation? StartLocation,GoalLocation; public string ProfileId; public int ProfileRevision;
        public double[] ProfileValues; public NavigationGeometryState Geometry,BaseGeometry; public string HeldIdentity,CertifiedProviderBinding;
        public bool UsesCurrentGeometry,UsesCurrentBaseGeometry;
        public int GeometryIndex,BaseGeometryIndex;
    }
    [Serializable] public sealed class NavigationAnswerState
    {
        public int RequestIndex; public NavPoint[] Route; public NavSolveStatus Status;
        public NavCorridorDescriptor Corridor;
    }
    [Serializable] public sealed class NavigationTechnicalFailureState
    { public int Entity; public long Request; public NavSolveStatus Status; }
    [Serializable] public sealed class NavigationEntityPointState
    {
        public int Entity; public NavPoint Point;
    }
    [Serializable] public sealed class NavigationMovementIdentityState
    { public int Entity; public string Identity; }
    [Serializable] public sealed class NavigationEntityOrderState
    {
        public int Entity; public long Order;
    }
    // Authority-owned checkpoint. Only registered in-flight work can replay;
    // unknown external dequeues remain an invalid capture state.
    [Serializable] public sealed class NavigationSessionState : IDisposable
    {
        internal NavigationCaptureBytes CaptureOutput,CaptureArrivalOutput;
        internal NavigationCaptureDtoStorage CaptureDto;
        internal NavigationHostRoot CaptureRoot;
        internal NavPagedList<long> CaptureBank,PreparedBank;
        internal NavigationAuthorityOwners CaptureAuthority;
        internal NavigationTrafficAuthority CaptureTraffic,PreparedTraffic;
        internal NavigationArrivalCatalog CaptureArrivalCatalog;
        internal NavigationArrivalLeaseCatalog CaptureLeaseCatalog;
        internal NavigationRetentionBudget CaptureBudget;
        internal NavRetainedArray<NavNameReference> CaptureTerrainNames;
        internal NavOwnedStorage[] CaptureRequests,CaptureGeometries,CaptureGroups,CaptureIdentities,CaptureActorGlobal,CaptureActorLocal,CaptureInstalled,CaptureProbeAnswers,CaptureMailboxAnswers,CaptureBarrierAnswers,CaptureAnswerTrackingOwners;
        internal int[][] CaptureExtraLayouts;internal int[] CaptureAnswerTrackingLayout;
        internal NavNameReference[] CaptureCrowdNames;internal int[] CaptureCrowdKeys,CaptureCrowdFlags;internal NavPoint[] CaptureCrowdPoints;
        private static void ReleasePins(NavOwnedStorage[] entries){if(entries!=null)foreach(var entry in entries)entry?.Release();}
        internal void ReleaseCatalogPins(){CaptureBank?.Release();CaptureBank=null;PreparedBank?.Release();PreparedBank=null;CaptureTraffic?.Release();CaptureTraffic=null;PreparedTraffic?.Release();PreparedTraffic=null;CaptureLeaseCatalog?.Release();CaptureLeaseCatalog=null;CaptureArrivalCatalog?.Release();CaptureArrivalCatalog=null;CaptureTerrainNames?.Release();CaptureTerrainNames=null;ReleasePins(CaptureRequests);ReleasePins(CaptureGeometries);ReleasePins(CaptureGroups);ReleasePins(CaptureIdentities);ReleasePins(CaptureActorGlobal);ReleasePins(CaptureActorLocal);ReleasePins(CaptureInstalled);ReleasePins(CaptureAnswerTrackingOwners);CaptureAnswerTrackingOwners=null;CaptureAnswerTrackingLayout=null;ReleasePins(CaptureProbeAnswers);ReleasePins(CaptureMailboxAnswers);ReleasePins(CaptureBarrierAnswers);CaptureProbeAnswers=CaptureMailboxAnswers=CaptureBarrierAnswers=null;CaptureActorGlobal=CaptureActorLocal=CaptureInstalled=null;CaptureRequests=CaptureGeometries=CaptureGroups=CaptureIdentities=null;CaptureExtraLayouts=null;if(CaptureCrowdNames!=null)foreach(var name in CaptureCrowdNames)name.Owner?.Release();CaptureCrowdNames=null;CaptureCrowdKeys=CaptureCrowdFlags=null;CaptureCrowdPoints=null;}
        // Private world capture has already detached the complete Navigation
        // section. Keep DTO/catalog/root pins, but end these serialized copies
        // before the enclosing section allocates its final backing.
        internal void ReleasePrivateSerializedOutputs()
        {
            HostAuthority=null;ArrivalAuthority=null;
            var output=CaptureOutput;CaptureOutput=null;
            var arrival=CaptureArrivalOutput;CaptureArrivalOutput=null;
            try{output?.Dispose();}finally{arrival?.Dispose();}
        }
        private void ReleaseCapture(bool clearPrivateBytes)
        {
            var output=CaptureOutput;CaptureOutput=null;
            if(clearPrivateBytes&&output!=null)HostAuthority=null;
            output?.Dispose();
            var arrival=CaptureArrivalOutput;CaptureArrivalOutput=null;
            if(clearPrivateBytes&&arrival!=null)ArrivalAuthority=null;arrival?.Dispose();
            var host=CaptureRoot;CaptureRoot=null;host?.Release();
            var authority=CaptureAuthority;CaptureAuthority=null;authority?.Release();
            CaptureBudget=null;ReleaseCatalogPins();var dto=CaptureDto;CaptureDto=null;dto?.Dispose();
        }
        internal NavigationSessionState TransferPublic(){ReleaseCapture(false);return this;}
        public void Dispose()=>ReleaseCapture(true);
        // 0 is the old 256-request/implicit-status wire; 1 is the explicit
        // bounded 4096-request/status extension carried by GroupOrderWire v5.
        internal NavigationRetentionBudget RestoreBudget;
        internal NavigationDecodeTransaction.PrivateValue<byte[]> DecodeHostOwner;
        internal bool HostPrepared;internal NavigationHostRoot PreparedHost;internal NavigationAuthorityOwners PreparedAuthority;
        public byte[] ArrivalAuthority,HostAuthority; public int[] AllocationRequestIndices,RegisteredRequestIndices,DeliveredRequestIndices;
        public int TransportVersion;
        // Zero denotes a legacy wire without a durable admission publication.
        public long AdmissionPublicationIdentity;
        public int SurfaceSemanticsVersion; public string SurfaceProviderId;
        public long Generation, RequestSequence, GroupSequence;
        public GroupOrderState[] Groups;
        public int GroupLayoutVersion;public long[] GroupKeys;public int[] MemberGroupKeys;public int[][] GroupLayouts;
        public NavigationGeometryState Geometry, NavigationGeometry;
        public NavigationGeometryState[] GeometryTable; public int NavigationGeometryIndex;
        public NavCrowdSaveState Crowd;
        public NavigationRequestState[] Requests;
        public NavigationEntityOrderState[] Orders;
        public NavigationEntityPointState[] Reservations, RetainedGoals;
        public int[] PendingRequestIndices, RequestMailboxIndices, ProbeRequestIndices;
        public NavigationMovementIdentityState[] MovementIdentities;
        public NavigationAnswerState[] ProbeAnswers;
        public NavigationAnswerState[] AnswerMailbox, BarrierAnswers;
        public NavigationTechnicalFailureState[] TechnicalFailures;
        public int CorridorWireVersion; public NavigationInstalledExecutionState[] InstalledExecutions;
        public int InstalledExecutionLayoutVersion; public int[] InstalledExecutionKeys,InstalledExecutionLayout;
        public int RejectedResults, AppliedResults, UnreachableResults;
        public int[][] CollectionLayouts;
    }
    public sealed class NavigationRequest : INavStorageFacade
    {
        public NavigationRequest(long session,long request,int entity,long order,NavigationProfile profile,NavGeometry geometry,NavPoint start,NavPoint goal,NavGeometry baseGeometry=null,string heldIdentity=null,NavigationMemberIdentity memberIdentity=null,NavLocation? startLocation=null,NavLocation? goalLocation=null)
        { StartLocation=startLocation;GoalLocation=goalLocation;MemberIdentity=memberIdentity;BaseGeometry=baseGeometry??geometry;HeldIdentity=heldIdentity;Session=session;Request=request;Entity=entity;Order=order;Profile=profile;ProfileId=profile.ProfileId;ProfileRevision=profile.Revision;Geometry=geometry;Start=start;Goal=goal; }
        public NavLocation? StartLocation{get;} public NavLocation? GoalLocation{get;}
        internal string CertifiedProviderBinding{get;set;}
        private NavigationHostRequest storageOwner;
        NavOwnedStorage INavStorageFacade.StorageRoot=>storageOwner;
        internal NavigationHostRequest StorageOwner=>storageOwner;
        // The owning aggregate installs this non-owning facade link after its
        // own fields are complete. It never acquires a second reference.
        internal void BindStorageOwner(NavigationHostRequest owner)
        {
            if(owner==null||storageOwner!=null||!ReferenceEquals(owner.Request,this))throw new ArgumentException("Invalid request owner/facade binding.");
            storageOwner=owner;
        }
        public NavigationMemberIdentity MemberIdentity{get;}
        public NavGeometry BaseGeometry{get;} public string HeldIdentity{get;}
        public long Session { get; } public long Request { get; } public int Entity { get; } public long Order { get; }
        public NavigationProfile Profile{get;} public string ProfileId { get; } public int ProfileRevision { get; } public NavGeometry Geometry { get; }
        public NavPoint Start { get; } public NavPoint Goal { get; }
    }
    public sealed class NavigationAnswer : INavStorageFacade
    {
        internal NavigationHostAnswer StorageOwner{get;private set;}
        NavOwnedStorage INavStorageFacade.StorageRoot=>StorageOwner;
        internal void BindStorageOwner(NavigationHostAnswer owner){if(owner==null||StorageOwner!=null||!ReferenceEquals(owner.Answer,this))throw new ArgumentException("Invalid answer owner binding.");StorageOwner=owner;}
        private readonly NavPoint[] route;
        public NavigationAnswer(NavigationRequest request,NavPoint[] route)
            :this(request,route,route!=null&&route.Length>0?NavSolveStatus.Ready:NavSolveStatus.UnreachableInGraph){}
        public NavigationAnswer(NavigationRequest request,NavPoint[] route,NavSolveStatus status)
            :this(request,route,status,null){}
        public NavigationAnswer(NavigationRequest request,NavPoint[] route,NavSolveStatus status,NavCorridorDescriptor corridor)
        {if(request==null||route==null||status==NavSolveStatus.Pending||
            (status==NavSolveStatus.Ready)!=(route.Length>0)||corridor!=null&&status!=NavSolveStatus.Ready)
            throw new ArgumentException("Invalid route proposal.");
            Request=request;Status=status;Corridor=corridor;this.route=(NavPoint[])route.Clone();}
        public NavigationRequest Request{get;}
        public NavSolveStatus Status{get;}
        public NavCorridorDescriptor Corridor{get;}
        public int RouteLength=>route.Length;
        internal NavPoint[] ReadOnlyRouteBacking=>route;
        public NavPoint[] CopyRoute(){return (NavPoint[])route.Clone();}
    }
    // Owned by one domain worker. Mailboxes and registered handoff are the
    // bounded cross-thread surfaces.
    public sealed partial class NavigationSession
    {
        private NavigationInstalledExecutions installedExecutions;
        private HashSet<NavigationAnswer> countedCorridorAnswers=new HashSet<NavigationAnswer>();
        internal string LastCorridorFailure{get;private set;}
        private bool CorridorFailure(string reason){LastCorridorFailure=reason;return false;}
        private long installedCorridorBytes,installedGeometryBytes,answerCorridorBytes,hostRetainedBytes;
        // The authority owns the host identity even while its worker computes
        // outside transportGate. A second kernel must not replay the same
        // registered requests from the checkpoint mailbox.
        private CertifiedRouteKernel certifiedHost;
        private long certifiedHostAttachmentIdentity;
        // A reservation protects the remaining host headroom while the sole solver
        // allocates outside the gate. Corridor growth/installation waits for release;
        // capture, cancellation and ingress never wait for computation.
        private int hostAllocationThread;
        // Wall-clock observation is outside the deterministic authority graph and
        // save wire. Weak keys let stopped generations release their diagnostics.
        private sealed class TransportTiming {internal long Wait,Hold;}
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NavigationSession,TransportTiming> transportTimings=
            new System.Runtime.CompilerServices.ConditionalWeakTable<NavigationSession,TransportTiming>();
        internal double TransportWaitMilliseconds=>transportTimings.TryGetValue(this,out var timing)?System.Threading.Interlocked.Read(ref timing.Wait)*1000d/System.Diagnostics.Stopwatch.Frequency:0;
        internal double TransportHoldMilliseconds=>transportTimings.TryGetValue(this,out var timing)?System.Threading.Interlocked.Read(ref timing.Hold)*1000d/System.Diagnostics.Stopwatch.Frequency:0;
        // Measure outermost gate occupancy only, avoiding reentrant double counting.
        private readonly struct TransportScope : IDisposable
        {
            private readonly NavigationSession session;
            private readonly bool outer;
            private readonly long entered;
            private readonly TransportTiming timing;
            internal TransportScope(NavigationSession session)
            {
                this.session=session;outer=!System.Threading.Monitor.IsEntered(session.transportGate);
                timing=outer?transportTimings.GetValue(session,_=>new TransportTiming()):null;
                long start=outer?System.Diagnostics.Stopwatch.GetTimestamp():0;
                System.Threading.Monitor.Enter(session.transportGate);
                entered=outer?System.Diagnostics.Stopwatch.GetTimestamp():0;
                if(outer)System.Threading.Interlocked.Add(ref timing.Wait,entered-start);
            }
            public void Dispose()
            {
                var retired=outer?session.DetachRetiredAdmissionsNoThrow():null;
                var providers=outer?session.DetachRetiredArrivalProvidersNoThrow():null;
                var attempts=outer?session.DetachRetiredArrivalAttemptsNoThrow():null;
                var leases=outer?session.DetachRetiredArrivalLeasesNoThrow():null;
                var probeQueues=outer?session.retiredProbeQueues:null;if(outer)session.retiredProbeQueues=null;
                var probeReceipts=outer?session.retiredProbeReceipts:null;if(outer)session.retiredProbeReceipts=null;
                var legacyProbeReceipts=outer?session.retiredLegacyProbePayloads:null;if(outer)session.retiredLegacyProbePayloads=null;
                var registrations=outer?session.retiredRegistrations:null;if(outer)session.retiredRegistrations=null;
                if(outer)System.Threading.Interlocked.Add(ref timing.Hold,System.Diagnostics.Stopwatch.GetTimestamp()-entered);
                System.Threading.Monitor.Exit(session.transportGate);
                try{ReleaseRetiredAdmissions(retired);}finally{try{ReleaseRetiredArrivalProviders(providers);}finally{try{ReleaseRetiredArrivalAttempts(attempts);}finally{try{ReleaseRetiredArrivalLeases(leases);}finally{try{ReleaseRetiredProbeQueues(probeQueues);}finally{try{ReleaseRetiredProbeReceipts(probeReceipts);}finally{try{legacyProbeReceipts?.Release();}finally{while(registrations!=null){var next=registrations.RetiredNext;registrations.RetiredNext=null;registrations.Release();registrations=next;}}}}}}}}
            }
        }
        private TransportScope EnterTransport()=>new TransportScope(this);
        internal event Action HostChanged;
        internal void SignalHost()=>HostChanged?.Invoke();
        internal bool HasCertifiedHost{get{using(EnterTransport())return certifiedHost!=null;}}
        internal void AttachCertifiedHost(CertifiedRouteKernel host)
        {
            if(host==null)throw new ArgumentNullException(nameof(host));
            using(EnterTransport()){
                if(certifiedHost!=null&&!ReferenceEquals(certifiedHost,host))
                    throw new InvalidOperationException("A different certified route host owns this session.");
                if(certifiedHost==null)certifiedHostAttachmentIdentity=checked(certifiedHostAttachmentIdentity+1);
                certifiedHost=host;
            }
        }
        internal void DetachCertifiedHost(CertifiedRouteKernel host)
        {
            using(EnterTransport()){
                if(certifiedHost!=null&&!ReferenceEquals(certifiedHost,host))
                    throw new InvalidOperationException("Certified route host release owner mismatch.");
                if(hostAllocationThread!=0)throw new InvalidOperationException("Cannot detach an allocating route host.");
                certifiedHost=null;
            }
        }
        internal long BeginHostAllocation()
        {
            using(EnterTransport()){
                if(hostAllocationThread!=0)throw new InvalidOperationException("Concurrent route kernels.");
                hostAllocationThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
                return installedCorridorBytes+installedGeometryBytes+answerCorridorBytes+ArrivalRetainedBytes;
            }
        }
        internal void EndHostAllocation(long bytes)
        {
            using(EnterTransport()){
                if(hostAllocationThread!=System.Threading.Thread.CurrentThread.ManagedThreadId)
                    throw new InvalidOperationException("Route reservation owner mismatch.");
                // PhysicalRetainedBytes already covers the complete unique-owner
                // Session union. Do not add legacy corridor/arrival estimates again.
                // Their inner scheduler allowance still needs only ordinary work.
                bool physical=committedHostRoot!=null;
                bool over=bytes<0||(physical?physicalHostBudget.RetainedBytes>LayeredNavigationScheduler.MaxRetainedBytes:
                    bytes+installedCorridorBytes+installedGeometryBytes+answerCorridorBytes+ArrivalRetainedBytes>LayeredNavigationScheduler.MaxRetainedBytes);
                hostRetainedBytes=physical?committedHostRoot.Scheduler.RetainedBytes:Math.Max(0,bytes);hostAllocationThread=0;
                if(over)throw new InvalidOperationException("Route retention cap exceeded.");
            }
        }
        internal long CorridorRetainedBytes{get{using(EnterTransport())return installedCorridorBytes+installedGeometryBytes+answerCorridorBytes+ArrivalRetainedBytes;}}
        internal long HostRetainedBytes{get{using(EnterTransport())return hostRetainedBytes;}}
        internal void RunHostStep(Action step)
        {if(step==null)throw new ArgumentNullException(nameof(step));using(EnterTransport())step();}
        internal void SetHostRetainedBytes(long bytes)
        {using(EnterTransport()){if(bytes<0)throw new ArgumentOutOfRangeException(nameof(bytes));hostRetainedBytes=bytes;}}
        private long ProspectiveGeometryBytes(int replacing,NavigationRequest request)
        {
            var seen=new HashSet<NavGeometry>();long bytes=0;
            foreach(var row in installedExecutions.Values)if(row.Entity!=replacing){
                if(seen.Add(row.MovementGeometry))bytes+=GeometryRetention(row.MovementGeometry);
                if(seen.Add(row.BaseGeometry))bytes+=GeometryRetention(row.BaseGeometry);
            }
            if(seen.Add(request.Geometry))bytes+=GeometryRetention(request.Geometry);
            if(seen.Add(request.BaseGeometry))bytes+=GeometryRetention(request.BaseGeometry);
            return bytes;
        }
        private static long GeometryRetention(NavGeometry geometry)
            =>checked(256L+512L*geometry.Obstacles.Count+
                128L*geometry.Obstacles.Sum(o=>(long)(o.Polygon?.Vertices.Count??0)));
        private void RecountInstalledGeometryBytes()
        {
            var seen=new HashSet<NavGeometry>();installedGeometryBytes=0;
            foreach(var row in installedExecutions.Values){
                if(row.MovementGeometry!=null&&seen.Add(row.MovementGeometry))installedGeometryBytes+=GeometryRetention(row.MovementGeometry);
                if(row.BaseGeometry!=null&&seen.Add(row.BaseGeometry))installedGeometryBytes+=GeometryRetention(row.BaseGeometry);
            }
        }
        private void TrackCorridorAnswer(NavigationAnswer answer)
        {if(answer.Corridor!=null&&!IsCorridorAnswerTracked(answer)&&countedCorridorAnswers.Add(answer))answerCorridorBytes+=answer.Corridor.EstimatedRetainedBytes;}
        private void ReleaseCorridorAnswer(NavigationAnswer answer)
        {ForgetCorridorCharge(answer);transportOwner?.Consume(answer.Request);}
        private void ForgetCorridorCharge(NavigationAnswer answer)
        {ForgetCorridorChargeCompatibility(answer);}
        private void SetInstalledExecution(int entity,NavCorridorDescriptor descriptor,NavigationRequest request=null,NavigationInstalledExecutionOwner prepared=null,NavigationPreparedAnswerNames batch=null,int batchIndex=-1)
        {
            if(installedExecutions.TryGetValue(entity,out var old))installedCorridorBytes-=old.Corridor.EstimatedRetainedBytes+16L*old.InstalledRoute.Length;
            if(descriptor==null){installedExecutions.Remove(entity);ResetMarchProgress(entity);RecountInstalledGeometryBytes();return;}
            if(request==null)throw new ArgumentException("Missing historical corridor request.");
            if(!Crowd.TryGet(entity,out var actor))throw new InvalidOperationException("Missing installed corridor actor.");
            if(prepared!=null){if(!ReferenceEquals(prepared.Route,actor.StorageOwner?.Global)||!ReferenceEquals(prepared.Request.Request,request)||!ReferenceEquals(prepared.Corridor.Corridor,descriptor))throw new InvalidOperationException("Prepared installed owner differs from actual route.");
                bool detached=descriptor.Group==0||!memberGroups.TryGetValue(entity,out var preparedGroup)||preparedGroup!=descriptor.Group;
                actor.StorageOwner.SetInstalledNoThrow(prepared,detached,null,out var retired);
                if(batch!=null&&batchIndex>=0)batch.RetiredInstalled[batchIndex]=retired;else retired?.Release();
                installedCorridorBytes+=descriptor.EstimatedRetainedBytes+16L*prepared.Route.Count;ResetMarchProgress(entity);RecountInstalledGeometryBytes();return;}
            var installedRoute=actor.Route.ToArray();
            installedExecutions[entity]=new NavigationInstalledExecutionState{Entity=entity,Corridor=descriptor,InstalledRoute=installedRoute,
                Profile=request.Profile,MovementGeometry=request.Geometry,BaseGeometry=request.BaseGeometry,
                HeldIdentity=request.HeldIdentity,
                Detached=descriptor.Group==0||!memberGroups.TryGetValue(entity,out var group)||group!=descriptor.Group};
            installedCorridorBytes+=descriptor.EstimatedRetainedBytes+16L*installedRoute.Length;
            ResetMarchProgress(entity);
            RecountInstalledGeometryBytes();
        }
        private bool TryActiveInstalledExecution(int entity,out NavigationInstalledExecutionState state)
        {
            if(!installedExecutions.TryGetValue(entity,out state))return false;
            var descriptor=state.Corridor;
            if(state.StorageOwner?.Certificate!=null&&!ReferenceEquals(state.StorageOwner.CurrentGeometry,navigationGeometry))return false;
            if(installedExecutions.IsDetached(entity)||
                !Crowd.TryGet(entity,out var actor)||!actor.Moving||
                actor.InstalledGroupId!=descriptor.Group||actor.InstalledRevision!=descriptor.ActivationRevision||
                actor.InstalledRequest!=descriptor.Request||actor.InstalledOrder!=descriptor.Order||
                actor.Incarnation!=descriptor.Incarnation||actor.MobilityRevision!=descriptor.Mobility||
                !memberGroups.TryGetValue(entity,out var group)||group!=descriptor.Group||
                !groups.TryGetValue(group,out var root)||root.OrderRevision!=descriptor.RootRevision||
                root.Members.All(m=>m.Entity!=entity||m.OrderRevision!=descriptor.ActivationRevision)||
                !state.InstalledRoute.SequenceEqual(actor.Route))return false;
            return true;
        }
        public NavigationInstalledExecutionState InstalledExecutionFor(int entity)
        {
            if(!TryActiveInstalledExecution(entity,out var state))return null;
            return new NavigationInstalledExecutionState{Entity=entity,Detached=false,Corridor=state.Corridor,
                InstalledRoute=(NavPoint[])state.InstalledRoute.Clone()};
        }
        private void DetachInstalledExecution(int entity)
        {installedExecutions.SetDetachedNoThrow(entity,true);}
        internal static void ValidateInstalledExecutionLayout(NavigationSessionState state)
        {
            if(state.InstalledExecutionLayoutVersion==0){
                if(state.InstalledExecutionKeys!=null&&state.InstalledExecutionKeys.Length!=0||state.InstalledExecutionLayout!=null&&state.InstalledExecutionLayout.Length!=0)
                    throw new ArgumentException("Legacy installed execution layout has metadata.");
                return; // v1-v7 recorded no allocation state; reconstruction is dense.
            }
            if(state.InstalledExecutionLayoutVersion!=1||state.CorridorWireVersion!=1||state.InstalledExecutions==null||state.InstalledExecutionKeys==null||state.InstalledExecutionLayout==null||state.InstalledExecutionKeys.Length!=state.InstalledExecutions.Length)
                throw new ArgumentException("Invalid installed execution layout mode/count.");
            if(state.Crowd==null||state.Crowd.Units==null)throw new ArgumentException("Missing installed execution actors.");
            var actors=new HashSet<int>();foreach(var actor in state.Crowd.Units)if(actor==null||!actors.Add(actor.Id))throw new ArgumentException("Invalid installed execution actors.");
            var entities=new HashSet<int>();int previous=-1;
            foreach(var row in state.InstalledExecutions){
                if(row==null||row.Entity<0||row.Entity<=previous||!actors.Contains(row.Entity)||row.Corridor==null||row.Corridor.Entity!=row.Entity||!entities.Add(row.Entity))
                    throw new ArgumentException("Invalid canonical installed execution row/actor binding.");
                previous=row.Entity;
            }
            var validation=new AuthoritySlotMap<int,byte>();
            foreach(int key in state.InstalledExecutionKeys){if(!entities.Contains(key))throw new ArgumentException("Unknown installed execution key.");validation.Add(key,0);}
            validation.RestoreLayout(state.InstalledExecutionLayout);
        }
        private void RestoreInstalledExecutions(NavigationSessionState state,NavGeometry[] geometryTable,NavigationAuthorityOwners body)
        {
            if(state.CorridorWireVersion==0){
                if(state.InstalledExecutions!=null&&state.InstalledExecutions.Length!=0||
                    state.Requests.Any(r=>r.CertifiedProviderBinding!=null))
                    throw new ArgumentException("Legacy corridor wire has installed metadata.");
                return;
            }
            if(state.CorridorWireVersion!=1||state.InstalledExecutions==null)
                throw new ArgumentException("Invalid corridor wire mode.");
            var seen=new HashSet<int>();
            var providers=new Dictionary<Tuple<NavGeometry,string>,ILayeredNavigationProvider>();
            foreach(var row in state.InstalledExecutions){
                var descriptor=row?.Corridor;
                if(descriptor==null||!seen.Add(row.Entity)||!Crowd.TryGet(row.Entity,out var actor)||
                    row.InstalledRoute==null||row.ExecutionGeometryIndex< -1||row.GeometryIndex<0||row.GeometryIndex>=geometryTable.Length||
                    (row.ExecutionGeometryIndex<0?row.BaseGeometryIndex!=state.NavigationGeometryIndex:row.ExecutionGeometryIndex!=state.NavigationGeometryIndex)||
                    !descriptor.ProjectedRoute().Concat(new[]{descriptor.Endpoint.Position}).SequenceEqual(row.InstalledRoute)||
                    descriptor.Entity!=row.Entity||descriptor.World!=Generation||
                    descriptor.Request!=actor.InstalledRequest||descriptor.Order!=actor.InstalledOrder||
                    descriptor.Group!=actor.InstalledGroupId||descriptor.ActivationRevision!=actor.InstalledRevision||
                    descriptor.Radius!=actor.Radius||descriptor.Provenance==NavRouteProvenance.ProducerProbe||
                    row.BaseGeometryIndex<0||row.BaseGeometryIndex>=geometryTable.Length||descriptor.TopologyRevision!=geometryTable[row.BaseGeometryIndex].Revision||
                    descriptor.SurfaceProviderId!=(terrain?.Id??NavLocation.FlatSurface)||
                    descriptor.HoldBinding!=row.HeldIdentity||
                    !descriptor.Endpoint.Equals(actor.GoalLocation??new NavLocation(actor.Goal,NavLocation.FlatSurface)))
                    throw new ArgumentException("Invalid installed corridor checkpoint.");
                var historicalProfile=LoadProfile(new NavigationRequestState{
                    ProfileId=row.ProfileId,ProfileRevision=row.ProfileRevision,ProfileValues=row.ProfileValues});
                var historicalGeometry=geometryTable[row.GeometryIndex];
                var historicalBase=geometryTable[row.BaseGeometryIndex];
                var memberIdentity=descriptor.Provenance==NavRouteProvenance.CommandMember
                    ?new NavigationMemberIdentity(descriptor.Incarnation,descriptor.Mobility,descriptor.Group,
                        descriptor.ActivationRevision):null;
                var historicalRequest=new NavigationRequest(descriptor.World,descriptor.Request,row.Entity,
                    descriptor.Order,historicalProfile,historicalGeometry,descriptor.Origin.Position,
                    descriptor.Endpoint.Position,historicalBase,row.HeldIdentity,memberIdentity,
                    descriptor.Origin,descriptor.Endpoint);
                if(!QualifyCorridor(historicalRequest,descriptor.ProjectedRoute(),descriptor,providers,false))
                    throw new ArgumentException("Invalid historical corridor: "+LastCorridorFailure);
                if(!row.Detached&&descriptor.Group!=0){
                    if(!memberGroups.TryGetValue(row.Entity,out var currentGroup)||currentGroup!=descriptor.Group||
                        !groups.TryGetValue(currentGroup,out var group)||
                        group.OrderRevision!=descriptor.RootRevision||group.CommandSequence!=descriptor.CommandSequence||
                        group.IssuedTick!=descriptor.IssuedTick||
                        group.Members.All(m=>m.Entity!=row.Entity||m.Incarnation!=descriptor.Incarnation||
                            m.OrderRevision!=descriptor.ActivationRevision))
                        throw new ArgumentException("Unbound active installed execution.");
                }else if(!row.Detached)throw new ArgumentException("Legacy installed corridor cannot steer a group.");
                if(actor.Moving&&!row.Detached&&
                    (descriptor.Incarnation!=actor.Incarnation||descriptor.Mobility!=actor.MobilityRevision))
                    throw new ArgumentException("Moving installed identity changed.");
                if(body?.InstalledCatalog!=null){
                    int index=Array.IndexOf(state.InstalledExecutions,row);var owner=body.InstalledCatalog[index] as NavigationInstalledExecutionOwner;
                    if(row.ExecutionGeometryIndex>=0){if(owner?.Certificate==null||!SameGeometry(rowGeometry(owner.CurrentGeometry),geometryTable[row.ExecutionGeometryIndex])||!row.Detached&&(owner.Request.Request.ProfileId!=profile.ProfileId||owner.Request.Request.ProfileRevision!=profile.Revision))throw new ArgumentException("Unbound current installed geometry certificate.");}
                    else if(owner?.Certificate!=null)throw new ArgumentException("Missing installed certificate DTO role.");
                    if(owner==null||owner.Entity!=row.Entity||!row.Detached&&!ReferenceEquals(owner.Route,actor.StorageOwner?.Global)||!SameHostCorridor(owner.Corridor.Corridor,descriptor)||!SameGeometry(rowGeometry(owner.Request.Request.Geometry),historicalGeometry)||!SameGeometry(rowGeometry(owner.Request.Request.BaseGeometry),historicalBase)||owner.Request.Request.ProfileId!=row.ProfileId||owner.Request.Request.ProfileRevision!=row.ProfileRevision||owner.Request.Request.HeldIdentity!=row.HeldIdentity||!SaveProfile(owner.Request.Request.Profile).SequenceEqual(row.ProfileValues)||!owner.Route.SequenceEqual(row.InstalledRoute))throw new ArgumentException("Installed BODY role differs from DTO.");
                    installedExecutions.Set(row.Entity,owner.State,row.Detached);
                    NavigationGeometryState rowGeometry(NavGeometry value)=>SaveGeometry(value);
                }else{
                    if(row.ExecutionGeometryIndex>=0)throw new ArgumentException("Certificate requires exact physical BODY.");
                    var imported=ImportHistoricalExecution(historicalRequest,descriptor,actor,row.InstalledRoute,row.Detached);try{installedExecutions.Set(row.Entity,imported.State,row.Detached);}finally{imported.Release();}
                }
                installedCorridorBytes+=descriptor.EstimatedRetainedBytes+16L*row.InstalledRoute.Length;
            }
            // Explicit main-v8 import has already validated its real slot/free
            // history. Accepted migration retains actor executions and detached
            // flags; the old independent index is not part of the new authority.
            RecountInstalledGeometryBytes();
            if(CorridorRetainedBytes>LayeredNavigationScheduler.MaxRetainedBytes)
                throw new ArgumentException("Restored corridor retention exceeds capacity.");
        }
        // Detached legacy hydration: preserve the validated historical corridor
        // and actor route. Construct admitted ownership, never rerun a solver.
        private NavigationInstalledExecutionOwner ImportHistoricalExecution(NavigationRequest request,NavCorridorDescriptor descriptor,NavUnit actor,NavPoint[] savedRoute,bool detached)
        {
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Decode,Generation,admissionPublicationIdentity,out var scope))throw new InvalidOperationException("Historical execution import admission refused.");
            using(scope){NavigationInstalledExecutionOwner value=null;bool attached=false;
                try{
                    using(NavStorageScratch.Reserve(scope.OwnedReservation(),checked(16384L+2048L*descriptor.Legs.Count))){
                        var r=scope.OwnedReservation();NavigationHostRequest input=null;NavigationHostAnswer answer=null;NavRetainedArray<NavPoint> route=null;
                        try{
                            input=NavigationHostRequest.CopyOwned(request,descriptor.ProviderBinding,r,ReferenceEquals(request.BaseGeometry,navigationGeometry)?navigationGeometry:null);
                            answer=ImportLegacyAnswer(input,new NavigationAnswer(input.Request,descriptor.ProjectedRoute(),NavSolveStatus.Ready,descriptor),r);
                            route=!detached&&actor.StorageOwner.Global!=null&&actor.StorageOwner.Global.SequenceEqual(savedRoute)?NavPhysicalMacro.Acquire(answer,actor.StorageOwner.Global):NavRetainedArray<NavPoint>.Copy(savedRoute,r.TryReserve);
                            value=NavigationInstalledExecutionOwner.CreateOwned(answer,route,detached,r);
                        }finally{route?.Release();answer?.ReleaseStorage();input?.ReleaseStorage();}
                    }
                    scope.Attach(value);attached=true;scope.Commit(value,_=>{});return value;
                }finally{if(!attached)value?.Release();}
            }
        }
        private NavigationHostAnswer ImportLegacyAnswer(NavigationHostRequest input,NavigationAnswer answer,INavStorageReservation r)
        {
            if(answer.Status!=NavSolveStatus.Ready)return NavigationHostAnswer.CreateTerminal(input,answer.Status,r);
            if(answer.Corridor==null)return NavigationHostAnswer.CreateLegacyProbeReady(input,answer.ReadOnlyRouteBacking,r);
            var descriptor=answer.Corridor;NavigationHostProvider provider=null;NavScheduledRequest recipe=null;NavPagedList<NavTypedLeg> legs=null;NavTypedRoute typed=null;NavigationHostCorridor corridor=null;
            using(NavStorageScratch.Reserve(r,checked(16384L+2048L*descriptor.Legs.Count)))try{
                var raw=terrain==null?(ILayeredNavigationProvider)NavigationAdmission.CertifiedFlatProvider(input.Request.Geometry):new PartitionNavigationProvider(terrain,input.Request.Geometry,input.Request.HeldIdentity);
                provider=NavigationHostProvider.CopyOwned(raw,input.Request.Geometry,descriptor.SurfaceProviderId,1,r);
                var d=descriptor;var subscription=new NavRouteSubscription(d.World,d.Group,d.RootRevision,d.CommandSequence,d.IssuedTick,d.ActivationRevision,d.Entity,d.Incarnation,d.ActivationRevision,d.Mobility,d.Request,d.Origin,d.Endpoint,d.HoldBinding,d.Provenance);
                var unowned=NavScheduledRequest.ForSingleton(provider.Provider,new NavClearanceProfile("ground",1,"ground",checked((int)d.Mobility),d.Radius),new NavGraphProfile(d.ProfileBinding,"distance",1,d.GridCell),new NavGraphCompileLimits(1048576,300000,1200000),subscription,1048576);
                recipe=NavScheduledRequest.CopyOwned(unowned,provider.Provider,r);legs=NavPagedList<NavTypedLeg>.Create(r.TryReserve);double cost=0;
                foreach(var old in descriptor.Legs){var leg=NavTypedLeg.CreateOwned(old.From,old.To,old.Cost,old.Kind,old.PortalId,r,provider.Provider);try{legs.Add(leg);cost+=old.Cost;}finally{leg.ReleaseStorage();}}
                legs.Seal();typed=NavTypedRoute.CopySubscriptionAndCreateOwned(recipe.Subscription,legs,cost,r);corridor=NavigationHostCorridor.CreateOwned(input,provider,recipe,typed,r);
                if(!SameHostCorridor(corridor.Corridor,descriptor))throw new ArgumentException("Imported historical corridor changed its transcript.");
                return NavigationHostAnswer.CreateReady(input,corridor,answer.ReadOnlyRouteBacking,r);
            }finally{corridor?.ReleaseStorage();typed?.ReleaseStorage();legs?.ReleaseStorage();recipe?.StorageRoot.Release();provider?.ReleaseStorage();}
        }
        private ILayeredNavigationProvider CorridorProvider(NavigationRequest request,
            Dictionary<Tuple<NavGeometry,string>,ILayeredNavigationProvider> cache)
        {
            var key=Tuple.Create(request.Geometry,request.HeldIdentity);
            if(cache.TryGetValue(key,out var provider))return provider;
            provider=terrain==null
                ?(ILayeredNavigationProvider)NavigationAdmission.CertifiedFlatProvider(request.Geometry)
                :new PartitionNavigationProvider(terrain,request.Geometry,request.HeldIdentity);
            cache.Add(key,provider);return provider;
        }
        private static bool OnCorridorSeam(NavPoint point,NavPoint a,NavPoint b)
        {
            double x=b.X-a.X,z=b.Z-a.Z,length=x*x+z*z;
            double t=length==0?0:Math.Max(0,Math.Min(1,((point.X-a.X)*x+(point.Z-a.Z)*z)/length));
            double dx=point.X-a.X-t*x,dz=point.Z-a.Z-t*z;
            return dx*dx+dz*dz<=1e-14;
        }
        private NavigationCorridorGroupSource ObserveCorridorGroup(NavigationRequest request)
        {
            var id=request.MemberIdentity?.GroupId??0;
            return new NavigationCorridorGroupSource(id!=0&&Member(request.Entity)!=null&&groups.TryGetValue(id,out var group)?group:null);
        }
        private bool QualifyCorridor(NavigationRequest request,NavPoint[] route,NavCorridorDescriptor corridor,
            Dictionary<Tuple<NavGeometry,string>,ILayeredNavigationProvider> cache,bool current)
        {
            var provider=corridor!=null&&(current||request.Session==Generation)?CorridorProvider(request,cache):null;
            bool valid=QualifyCorridorPrepared(request,route,corridor,provider,current,Generation,
                terrain?.Id??NavLocation.FlatSurface,ObserveCorridorGroup(request),out var reason);
            if(corridor!=null||!valid)LastCorridorFailure=reason;
            return valid;
        }
        public const int RouteRequestCapacity=4096;
        private readonly object transportGate = new object();
        private NavigationLiveRegistrations liveRegistrations,retiredRegistrations;
        private IReadOnlyList<NavigationRequest> RegisteredRequests=>liveRegistrations!=null?(IReadOnlyList<NavigationRequest>)liveRegistrations:registeredRoutes;
        private int RegistrationIndex(NavigationRequest request){if(liveRegistrations!=null)return liveRegistrations.IndexOf(request);return registeredRoutes.FindIndex(r=>ReferenceEquals(r,request));}
        private bool RegistrationGraphKey(NavigationRequest request,out string key){if(liveRegistrations!=null)return liveRegistrations.TryGraphKey(request,out key);return registeredGraphKeys.TryGetValue(request,out key);}
        // Explicit public/manual compatibility lane. Qualified physical ingress
        // installs a compact owner again after canonical host roles are checked.
        private void UseLegacyRegistrations()
        {
            if(liveRegistrations==null)return;var list=new List<NavigationRequest>(liveRegistrations.Count);var keys=new Dictionary<NavigationRequest,string>();
            for(int i=0;i<liveRegistrations.Count;i++){var request=liveRegistrations.RequestAt(i);liveRegistrations.TryGraphKey(request,out var key);list.Add(request);keys.Add(request,key);}
            registeredRoutes=list;registeredGraphKeys=keys;var old=liveRegistrations;liveRegistrations=null;old.RetiredNext=retiredRegistrations;retiredRegistrations=old;
        }
        private Action<NavigationRequest> requestTransferHandler,requestEnqueueValidator;
        private List<NavigationRequest> registeredRoutes = new List<NavigationRequest>();
        private Dictionary<NavigationRequest,string> registeredGraphKeys=
            new Dictionary<NavigationRequest,string>();
        private int transferCallbackThread;
        private void RejectTransferCallbackReentry()
        {if(transferCallbackThread==System.Threading.Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Route registration cannot reenter navigation authority.");}
        private int admissionBatchDepth;
        // Runtime operation identity; depth alone cannot detect a nested batch
        // that returned to the same depth while a candidate was prepared.
        private long authorityOperationIdentity;
        private T AdmissionBatch<T>(Func<T> work)
        {
            using(EnterTransport()){RejectTransferCallbackReentry();authorityOperationIdentity=checked(authorityOperationIdentity+1);admissionBatchDepth++;bool committed=false;
                try{var result=work();committed=true;return result;}
                finally{admissionBatchDepth--;if(committed&&admissionBatchDepth==0)PublishAdmission();}}
        }
        private void AdmissionBatch(Action work)
        {
            using(EnterTransport()){RejectTransferCallbackReentry();authorityOperationIdentity=checked(authorityOperationIdentity+1);admissionBatchDepth++;bool committed=false;
                try{work();committed=true;}
                finally{admissionBatchDepth--;if(committed&&admissionBatchDepth==0)PublishAdmission();}}
        }
        // Scheduler admission and mailbox removal are one capture-visible action.
        // A failed admission leaves the original request at the mailbox head.
        internal bool TryTransferRoute(Func<NavigationRequest,bool> register, out NavigationRequest request)
        {
            if(register==null)throw new ArgumentNullException(nameof(register));
            using(EnterTransport())UseLegacyRegistrations();
            using(EnterTransport()){RejectTransferCallbackReentry();if(commandGroup!=null||admissionBatchDepth!=0){request=null;return false;}return Requests.TryTransfer(candidate=>{
                if(!Admission.Contains(candidate))return false;
                registeredRoutes.Add(candidate);
                transferCallbackThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
                try { if(register(candidate)){transportOwner.Transfer(candidate);return true;} }
                catch { registeredRoutes.Remove(candidate); throw; }
                finally { transferCallbackThread=0; }
                registeredRoutes.Remove(candidate);return false;
            },out request);}
        }
        // Internal until A2-d.3 installs the certified flat/authored provider adapter.
        internal bool TryTransferRouteToScheduler(LayeredNavigationScheduler scheduler,
            Func<NavigationRequest,NavScheduledRequest> prepare, out NavigationRequest request)
        {
            if(prepare==null)throw new ArgumentNullException(nameof(prepare));
            using(EnterTransport())UseLegacyRegistrations();
            return TryTransferRouteToScheduler(scheduler,(candidate,admission)=>prepare(candidate),out request);
        }
        internal bool TryTransferRouteToScheduler(LayeredNavigationScheduler scheduler,
            Func<NavigationRequest,NavigationAdmission,NavScheduledRequest> prepare, out NavigationRequest request)
        {
            if(scheduler==null)throw new ArgumentNullException(nameof(scheduler));
            if(prepare==null)throw new ArgumentNullException(nameof(prepare));
            using(EnterTransport())UseLegacyRegistrations();
            NavigationRequest candidate;NavigationAdmission observed;
            using(EnterTransport()){RejectTransferCallbackReentry();
                if(commandGroup!=null||admissionBatchDepth!=0){request=null;return false;}
                if(!Requests.TryPeek(out candidate)||!Admission.Contains(candidate)){request=null;return false;}
                observed=Admission;
            }
            // Provider construction and other preparation happen off the gate.
            // Only the bounded scheduler Submit executes in the transfer commit.
            var scheduled=prepare(candidate,observed);
            if(scheduled==null){request=null;return false;}
            ValidateScheduledRoute(candidate,scheduled,observed);
            using(EnterTransport()){RejectTransferCallbackReentry();
                if(commandGroup!=null||admissionBatchDepth!=0||Admission.Generation!=observed.Generation||Admission.PublicationIdentity!=observed.PublicationIdentity||!Admission.Contains(candidate)){request=null;return false;}
                return Requests.TryTransfer(head=>{
                    if(!ReferenceEquals(head,candidate))return false;
                    registeredRoutes.Add(candidate);
                    try{if(scheduler.Submit(scheduled)){
                        candidate.CertifiedProviderBinding=scheduled.Provider.Binding;
                        registeredGraphKeys.Add(candidate,scheduled.GraphKey);transportOwner.Transfer(candidate);return true;}}
                    catch{registeredRoutes.Remove(candidate);throw;}
                    registeredRoutes.Remove(candidate);return false;
                },out request);
            }
        }
        private void ValidateScheduledRoute(NavigationRequest candidate,NavScheduledRequest scheduled,NavigationAdmission observed)
        {
                var id=scheduled.Subscription;
                var member=candidate.MemberIdentity;
                var group=member==null||member.GroupId==0?null:observed.Groups.FirstOrDefault(g=>g.GroupId==member.GroupId);
                if(id.WorldGeneration!=candidate.Session||id.RequestSequence!=candidate.Request||
                    id.EntityId!=candidate.Entity||!id.Origin.Position.Equals(candidate.Start)||
                    !id.AssignedEndpoint.Position.Equals(candidate.Goal)||id.HoldBinding!=candidate.HeldIdentity||
                    scheduled.Profile.GridCell!=candidate.Profile.GridCell||
                    scheduled.Profile.ExactProfileBinding!=NavigationAdmission.ExactProfileBinding(candidate.Profile)||
                    scheduled.Clearance.Radius!=candidate.Profile.Radius||
                    member!=null&&scheduled.Clearance.MobilityRevision!=member.MobilityRevision||
                    candidate.StartLocation.HasValue&&!id.Origin.Equals(candidate.StartLocation.Value)||
                    candidate.GoalLocation.HasValue&&!id.AssignedEndpoint.Equals(candidate.GoalLocation.Value)||
                    candidate.Entity<0!=(id.Provenance==NavRouteProvenance.ProducerProbe)||
                    member!=null&&(id.GroupId!=member.GroupId||id.Incarnation!=member.Incarnation||
                        id.MobilityRevision!=member.MobilityRevision||id.MemberOrderRevision!=member.OrderRevision||
                        id.ActivationRevision!=member.OrderRevision)||
                    member!=null&&member.GroupId!=0&&(group==null||id.RootOrderRevision!=group.RootOrderRevision||
                        id.CommandSequence!=group.CommandSequence||id.IssuedTick!=group.IssuedTick)||
                    terrain!=null&&!PartitionNavigationProvider.SupportsTerrain(terrain)||
                    PartitionNavigationProvider.SupportsTerrain(terrain)&&
                    scheduled.Provider.Binding!=new PartitionNavigationProvider(terrain,candidate.Geometry,candidate.HeldIdentity).Binding)
                    throw new InvalidOperationException("Scheduler subscription differs from admitted request.");
        }
        // A stale mailbox head is still an original request. Deliver a rejection
        // through the old answer path so it cannot block newer FIFO inputs.
        public bool TryDropStaleRoute(out NavigationRequest request)
        {
            using(EnterTransport()){RejectTransferCallbackReentry();if(commandGroup!=null||admissionBatchDepth!=0){request=null;return false;}return Requests.TryTransfer(candidate=>
                candidate.Request<=Admission.PublishedThroughRequestSequence&&!Admission.Contains(candidate)&&
                Answers.TryEnqueue(new NavigationAnswer(candidate,Array.Empty<NavPoint>(),NavSolveStatus.Stale)),out request);}
        }
        internal bool TryRejectUnserviceableRoute(NavigationRequest expected,out NavigationRequest request)
        {
            using(EnterTransport()){RejectTransferCallbackReentry();if(commandGroup!=null||admissionBatchDepth!=0){request=null;return false;}
                return Requests.TryTransfer(candidate=>ReferenceEquals(candidate,expected)&&Admission.Contains(candidate)&&
                    Answers.TryEnqueue(new NavigationAnswer(candidate,Array.Empty<NavPoint>(),NavSolveStatus.CapacityExceeded)),out request);}
        }
        // Ready results stay owned until the answer mailbox accepts them.
        public bool TryCompleteRegisteredRoute(NavigationAnswer answer)
        {
            if(answer==null)throw new ArgumentNullException(nameof(answer));
            using(EnterTransport())UseLegacyRegistrations();
            using(EnterTransport()){RejectTransferCallbackReentry();
                if(hostAllocationThread!=0)return false;
                int index=registeredRoutes.FindIndex(r=>ReferenceEquals(r,answer.Request));
                if(index<0)return false;
                if(answer.Status==NavSolveStatus.Ready&&answer.Request.CertifiedProviderBinding!=null&&answer.Corridor==null||
                    answer.Corridor!=null&&(!registeredGraphKeys.TryGetValue(answer.Request,out var key)||
                    answer.Corridor.GraphKey!=key||answer.Corridor.ProviderBinding!=answer.Request.CertifiedProviderBinding))
                    throw new ArgumentException("Unregistered corridor binding.");
                if(!Answers.TryEnqueue(answer))return false;
                TrackCorridorAnswer(answer);
                registeredGraphKeys.Remove(answer.Request);
                registeredRoutes.RemoveAt(index);return true;
            }
        }
        public int RegisteredRouteCount { get { using(EnterTransport())return RegisteredRequests.Count; } }
        private IDictionary<int,NavigationRequest> pending=new AuthoritySlotMap<int,NavigationRequest>();
        private IDictionary<int,long> orders=new AuthoritySlotMap<int,long>();
        private IDictionary<int,NavPoint> reservations=new AuthoritySlotMap<int,NavPoint>();
        private IDictionary<int,NavSolveStatus> technicalFailures=new AuthoritySlotMap<int,NavSolveStatus>();
        // Latest intent is deliberately retained while a replacement is planned. A
        // geometry revision reissues it from the current authoritative position.
        private IDictionary<int,NavPoint> retainedGoals=new AuthoritySlotMap<int,NavPoint>();
        // Negative entity IDs are producer topology probes, never crowd actors.
        private IDictionary<int,NavigationRequest> probes=new AuthoritySlotMap<int,NavigationRequest>();
        private Queue<NavigationAnswer> probeAnswers=new Queue<NavigationAnswer>();
        private readonly NavigationBarrierAnswers barrierAnswers;
        public NavigationRequest Probe(int producer,long order,NavigationProfile typed,NavPoint start,NavPoint goal)
        {
            var owned=TryOwnedProbe(producer,order,typed,start,goal,false,out var request);if(owned.HasValue)return owned.Value?request:null;
            using(EnterTransport())return ProbeLocked(producer,order,typed,start,goal);
        }
        private NavigationRequest ProbeLocked(int producer,long order,NavigationProfile typed,NavPoint start,NavPoint goal)
        {
            var request=new NavigationRequest(Generation,++requestSequence,-producer,order,typed,navigationGeometry,start,goal);
            if(Requests.Count+RegisteredRouteCount>=RouteRequestCapacity||!Requests.TryEnqueue(request))return null;
            probes[-producer]=request;PublishAdmission();return request;
        }
        public void CancelProbe(int producer){var owned=TryOwnedProbe(producer,0,default,default,default,true,out _);if(owned.HasValue)return;using(EnterTransport()){probes.Remove(-producer);PublishAdmission();}}
        public bool TryProbeAnswer(out NavigationAnswer answer)
        {if(liveProbeAnswers!=null)return TryTakeOwnedProbeAnswer(out answer);using(EnterTransport()){if(ProbeAnswerCount==0){answer=null;return false;}
            answer=probeAnswers.Dequeue();var payload=ExtractPayloadReceiptNoThrow(ref legacyProbePayloads,answer);if(payload!=null){payload.Next=retiredLegacyProbePayloads;retiredLegacyProbePayloads=payload;}ReleaseCorridorAnswer(answer);return true;}}
        private long requestSequence;
        private readonly IPlayableTerrain terrain;
        private NavGeometry geometry;
        private NavGeometry navigationGeometry;
        private NavigationProfile profile;
        // Constructor opt-in is used only until the first saved HostRoot exists.
        // Restored roots (or historical absence of a root) choose their own policy.
        private bool replayOnlyProjection;
        internal NavigationHostRoot CreatePhysicalHostRoot(INavStorageReservation reservation)
            {var root=replayOnlyProjection?NavigationHostRoot.CreateReplayOnlyOwned(reservation):NavigationHostRoot.CreateOwned(reservation);if(accruedCreditsEnabled)root.MarkAccruedCredits();return root;}
        public NavigationSession(long generation,NavGeometry geometry,NavigationProfile profile,NavGeometry navigationGeometry=null,IPlayableTerrain terrain=null)
            :this(generation,geometry,profile,navigationGeometry,terrain,null,null){}
        internal NavigationSession(long generation,NavGeometry geometry,NavigationProfile profile,NavGeometry navigationGeometry,IPlayableTerrain terrain,NavigationRetentionBudget budget,NavigationAuthorityOwners decoded,object sharedTransportGate=null,NavRetainedArray<NavNameReference> sharedTerrainNames=null,bool replayOnlyProjection=false)
        {
            this.replayOnlyProjection=replayOnlyProjection;
            barrierAnswers=new NavigationBarrierAnswers(this);if(generation<1)throw new ArgumentOutOfRangeException("generation");if(sharedTransportGate!=null)transportGate=sharedTransportGate;this.terrain=terrain;Generation=generation;this.geometry=geometry;this.navigationGeometry=navigationGeometry??geometry;this.profile=profile;
            InitializeAuthorityOwners(budget,decoded);
            try{InitializeLiveArrivalTables();InitializeLiveRegistrations();InitializeAnswerRetention();InitializeTerrainNames(sharedTerrainNames);Crowd=new NavCrowd(this.geometry,this.profile,terrain);BindOwnedCrowdFactories();Crowd.BindSurfaceNamesNoThrow(terrainNameOwners);InitializeInstalledExecutions();PublishAdmission();}catch{Crowd?.ReleaseCrowdStorage();liveRegistrations?.Release();liveRegistrations=null;liveArrivalTables?.Release();liveArrivalTables=null;installedExecutions?.Release();installedExecutions=null;if(transportOwner!=null){Answers.ReleaseRetention();transportOwner.Release();transportOwner=null;}terrainNameOwners?.Release();terrainNameOwners=null;authorityOwners?.Release();authorityOwners=null;throw;}
        }
        private void InitializeLiveRegistrations()
        {
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,0,out var scope))throw new InvalidOperationException("Registration bootstrap admission refused.");
            using(scope){var root=NavigationLiveRegistrations.CreateOwned(scope.OwnedReservation());try{scope.Attach(root);scope.Commit(root,_=>{liveRegistrations=root;registeredRoutes=null;registeredGraphKeys=null;});}catch{root.ReleaseStorage();throw;}}
        }
        private NavigationTransportOwner transportOwner;
        private void InitializeAnswerRetention()
        {
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,0,out var scope))throw new InvalidOperationException("Answer retention admission refused.");
            using(scope){
                NavigationTransportOwner transport=null;NavMailboxRetention storage=null;NavigationAnswerMailboxBacking backing=null;
                try{
                    transport=NavigationTransportOwner.CreateOwned(scope.OwnedReservation());storage=NavMailboxRetention.CreateOwned(Answers.InstanceCapacity,scope.OwnedReservation());backing=NavigationAnswerMailboxBacking.CreateOwned(Answers.InstanceCapacity,scope.OwnedReservation());storage.OwnTransport(transport);storage.OwnBacking(backing);scope.Attach(storage);scope.Commit(storage,_=>{
                        transportOwner=transport;
                        Answers.BindRetention(storage,a=>(NavOwnedStorage)a?.StorageOwner??a?.Request.StorageOwner,backing.Items);
                        Requests.SelectOwner(r=>r?.StorageOwner);requestTransferHandler=transport.Transfer;requestEnqueueValidator=transport.Validate;Requests.BindTransport(transportGate,requestEnqueueValidator,null,requestTransferHandler);
                        Answers.BindTransport(transportGate,transport.ValidateAnswer,transport.AcceptAnswer,null);
                    });
                }catch{if(ReferenceEquals(transportOwner,transport)&&transportOwner!=null){Answers.ReleaseRetention();transportOwner=null;}else storage?.ReleaseStorage();transport?.Release();throw;}finally{backing?.Release();}
            }
        }
        public NavigationRoutePort RoutePort => routePort ?? (routePort = new NavigationRoutePort(this));
        private NavigationRoutePort routePort;
        public long Generation {get;}
        public NavCrowd Crowd {get;private set;}
        public NavMailbox<NavigationRequest> Requests {get;private set;} = new NavMailbox<NavigationRequest>(RouteRequestCapacity);
        public NavMailbox<NavigationAnswer> Answers {get;private set;} = new NavMailbox<NavigationAnswer>();
        public int RejectedResults {get;private set;}
        public int AppliedResults {get;private set;}
        public NavSolveStatus? TechnicalFailureFor(int entity)
            =>technicalFailures.TryGetValue(entity,out var value)?(NavSolveStatus?)value:null;
        public int PendingCount => pending.Count+probes.Count+allocating.Count;
        public bool IsPending(int id) { return pending.ContainsKey(id)||allocating.ContainsKey(id); }
        internal NavigationRequest PendingRequestFor(int id)=>pending.TryGetValue(id,out var request)?request:allocating.TryGetValue(id,out request)?request:null;
        private static NavigationGeometryState SaveGeometry(NavGeometry value)
        {
            var result=new NavigationGeometryState{HalfExtent=value.HalfExtent,Revision=value.Revision,Obstacles=new NavObstacle[value.Obstacles.Count]};
            for(int i=0;i<result.Obstacles.Length;i++)result.Obstacles[i]=value.Obstacles[i];return result;
        }
        private static bool SameGeometry(NavigationGeometryState saved,NavGeometry value)
        {
            if(saved==null||saved.Obstacles==null||saved.HalfExtent!=value.HalfExtent||saved.Revision!=value.Revision||saved.Obstacles.Length!=value.Obstacles.Count)return false;
            for(int i=0;i<saved.Obstacles.Length;i++)if(!saved.Obstacles[i].GeometryEquals(value.Obstacles[i]))return false;
            return true;
        }
        private static NavGeometry LoadGeometry(NavigationGeometryState saved){if(saved==null||saved.Obstacles==null)throw new ArgumentException("Missing saved geometry.");return new NavGeometry(saved.HalfExtent,saved.Obstacles,saved.Revision);}
        private static double[] SaveProfile(NavigationProfile value)
        {
            return value.CaptureWireValues();
        }
        private static NavigationProfile LoadProfile(NavigationRequestState saved)
        {
            return NavigationProfile.FromWireValues(saved.ProfileId,saved.ProfileRevision,saved.ProfileValues);
        }
        private static bool ValidSavedAnswer(NavigationAnswerState answer)
            =>answer.Route!=null&&Enum.IsDefined(typeof(NavSolveStatus),answer.Status)&&
                answer.Status!=NavSolveStatus.Pending&&
                (answer.Status==NavSolveStatus.Ready)==(answer.Route.Length>0);
        // Caller owns and quiesces the synchronous host. Validate replay before releasing
        // any work; then release actual memory and return every registered input to FIFO.
        internal void ReleaseHostForWorldCheckpoint(Action releaseHost)
        {
            if(releaseHost==null)throw new ArgumentNullException(nameof(releaseHost));
            long operation,attachment,publication;CertifiedRouteKernel worker;int registeredCount;long answerRevision;
            using(EnterTransport()){
                RejectTransferCallbackReentry();
                if(!UseLegacyHost)throw new InvalidOperationException("Physical navigation checkpoints preserve their exact continuation. Capture the world without releasing or replaying the host.");
                if(commandGroup!=null||admissionBatchDepth!=0||hostAllocationThread!=0||Requests.Count+RegisteredRequests.Count>RouteRequestCapacity)throw new InvalidOperationException("Checkpoint requires quiescent legacy authority and bounded replay.");
                operation=authorityOperationIdentity;attachment=certifiedHostAttachmentIdentity;worker=certifiedHost;publication=admissionPublicationIdentity;registeredCount=RegisteredRequests.Count;answerRevision=Answers.Revision;
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Capture,Generation,publication,out var scope))throw new InvalidOperationException("Legacy checkpoint replay admission refused.");
            using(scope)using(var replay=NavMailbox<NavigationRequest>.PreparedCheckpointReplay.Create(Requests,scope.OwnedReservation())){
                bool Current()=>UseLegacyHost&&authorityOperationIdentity==operation&&admissionPublicationIdentity==publication&&commandGroup==null&&admissionBatchDepth==0&&hostAllocationThread==0&&RegisteredRequests.Count==registeredCount&&(liveRegistrations==null||liveRegistrations.Count==0)&&Answers.Revision==answerRevision&&replay.SourceCurrent();
                using(EnterTransport()){
                    if(!Current()||certifiedHostAttachmentIdentity!=attachment||!ReferenceEquals(certifiedHost,worker))throw new InvalidOperationException("Legacy checkpoint source changed before replay preparation.");
                    foreach(var r in RegisteredRequests){if(!transportOwner.CanReplayExternalPrepared(r))throw new InvalidOperationException("Legacy checkpoint registration has incompatible transport ownership.");replay.Replay[replay.Count++]=r;}replay.CopyQueued();
                    bool Accounted(NavigationRequest request){for(int i=0;i<replay.Count;i++)if(ReferenceEquals(replay.Replay[i],request))return true;for(int i=0;i<Answers.Count;i++)if(ReferenceEquals(Answers.ReadPreparedAt(answerRevision,i)?.Request,request))return true;foreach(var answer in barrierAnswers)if(ReferenceEquals(answer.Request,request))return true;return false;}
                    foreach(var pair in probes)if(!Accounted(pair.Value))throw new InvalidOperationException("A rally probe is held outside the quiescent mailboxes.");
                    foreach(var pair in pending)if(!Accounted(pair.Value)&&!(RetainedHeldStopIntent(pair.Value)&&!allocating.ContainsKey(pair.Key)&&(installedExecutions.OwnerFor(pair.Key)==null||installedExecutions.IsDetached(pair.Key))))throw new InvalidOperationException("A pending navigation request is held outside the quiescent mailboxes.");
                }
                // Stable insertion order preserves duplicate occurrences as well.
                for(int i=1;i<replay.Count;i++){var r=replay.Replay[i];int j=i;while(j>0&&replay.Replay[j-1].Request>r.Request){replay.Replay[j]=replay.Replay[j-1];j--;}replay.Replay[j]=r;}
                replay.PinInputs();scope.Attach(replay);
                using(EnterTransport())if(!Current()||certifiedHostAttachmentIdentity!=attachment||!ReferenceEquals(certifiedHost,worker)||Requests.Revision==long.MaxValue)throw new InvalidOperationException("Legacy checkpoint changed before release.");
                // Caller owns/quiesces the synchronous host. Arbitrary release
                // callback failure is an execution error, not preparation refusal.
                releaseHost();
                Action<object> publish=_=>{using(EnterTransport()){
                    if(!Current()||hostRetainedBytes!=0||certifiedHost!=null)throw new InvalidOperationException("Legacy checkpoint release did not leave the expected quiescent source.");
                    foreach(var r in RegisteredRequests)if(!transportOwner.CanReplayExternalPrepared(r))throw new InvalidOperationException("Legacy checkpoint transport ownership changed before replay.");
                    replay.ApplyNoThrow();foreach(var r in RegisteredRequests)replay.RetireTransportNoThrow(transportOwner.ReplayExternalPreparedNoThrow(r));if(liveRegistrations==null){registeredRoutes.Clear();registeredGraphKeys.Clear();}
                }};
                scope.Publish(replay,publish);scope.ResolveCommitted();
            }
            PublishAdmission();
        }
        private int[] CaptureLedgerRowOrder(INavStorageReservation reservation)
        {
            using(NavStorageScratch.Reserve(reservation,checked(64L+8L*arrivalLedger.Count))){
                var sorted=CaptureKeys(arrivalLedger);System.Array.Sort(sorted);var result=new int[arrivalLedger.Count];int row=0;foreach(var entry in arrivalLedger)result[row++]=System.Array.BinarySearch(sorted,entry.Key);return result;
            }
        }
        public NavigationSessionState CaptureState()
        {using(var state=CaptureOwnedState())return state.TransferPublic();}
        internal NavigationSessionState CaptureOwnedState()
        {
            bool existingPrepared;using(EnterTransport())existingPrepared=CanPrepareCaptureGraph();
            if(existingPrepared)return CapturePreparedOwnedState();
            RefreshOwnedGroupMembers();
            InitializePhysicalHostForCapture();
            bool preparedCapture;using(EnterTransport())preparedCapture=CanPrepareCaptureGraph();
            if(preparedCapture)return CapturePreparedOwnedState();
            NavigationSessionState state;NavigationHostRoot root;NavigationAuthorityOwners authority;long operation,publication,scratchBytes;
            using(EnterTransport()){
                operation=authorityOperationIdentity;publication=admissionPublicationIdentity;
                long requestBound=allocating.Count+pending.Count+probes.Count+ProbeAnswerCount+Requests.Count+RegisteredRequests.Count+(committedHostRoot?.Delivered.Count??0)+barrierAnswers.Count+Answers.Count;
                long geometryBound=checked(2+requestBound*2+installedExecutions.Count*2);
                scratchBytes=checked(NavigationCaptureDtoStorage.RecordBytes<NavigationCaptureRequests>()+NavigationCaptureDtoStorage.RecordBytes<NavigationCaptureGeometries>()+NavigationCaptureDtoStorage.ArrayBytes<NavigationRequest>(requestBound)+2*NavigationCaptureDtoStorage.ArrayBytes<NavGeometry>(geometryBound)+NavigationCaptureDtoStorage.ArrayBytes<int>(geometryBound)+NavigationCaptureDtoStorage.ArrayBytes<NavigationRequest>(Requests.Count+RegisteredRequests.Count)+NavigationCaptureDtoStorage.ArrayBytes<NavigationAnswer>(Answers.Count)+NavigationCaptureDtoStorage.ArrayBytes<NavigationRequest>(Requests.Count));
            }
            if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Capture,Generation,publication,out var captureScope))throw new InvalidOperationException("Authority catalog capture admission refused.");
            using(captureScope)using(NavStorageScratch.Reserve(captureScope.OwnedReservation(),scratchBytes))using(var arrivalCapture=PrepareArrivalCatalogCapture(captureScope.OwnedReservation()))using(var leaseCapture=PrepareArrivalLeaseCapture(captureScope.OwnedReservation())){
                using(EnterTransport()){
                    RejectTransferCallbackReentry();
                    if(operation!=authorityOperationIdentity||publication!=admissionPublicationIdentity)throw new InvalidOperationException("Capture source changed during admission.");
                    if(commandGroup!=null||admissionBatchDepth!=0)throw new InvalidOperationException("Cannot capture an open navigation admission batch.");
                    arrivalCapture?.Observe(this);leaseCapture?.Observe(this);
                    state=CaptureStateLocked(captureScope.OwnedReservation());terrainNameOwners.Retain();state.CaptureTerrainNames=terrainNameOwners;state.CaptureBank=creditBank;state.CaptureBank?.Retain();root=committedHostRoot;root?.Retain();authority=authorityOwners;authority?.Retain();
                }
                state.CaptureRoot=root;state.CaptureAuthority=authority;state.CaptureBudget=physicalHostBudget;
                try{if(arrivalCapture!=null){arrivalCapture.Seal(captureScope.OwnedReservation());arrivalCapture.Catalog.Retain();state.CaptureArrivalCatalog=arrivalCapture.Catalog;}if(leaseCapture!=null){leaseCapture.Seal(captureScope.OwnedReservation());leaseCapture.Catalog.Retain();state.CaptureLeaseCatalog=leaseCapture.Catalog;}if(root!=null){state.CaptureOutput=CaptureHostAuthority(root,authority,state);state.HostAuthority=state.CaptureOutput.Bytes;}return state;}
                catch{state.Dispose();throw;}
            }
        }
        private NavigationSessionState CaptureStateLocked(INavStorageReservation reservation)
        {
            // Authority is at the frozen routing barrier; registered transport
            // transitions are blocked by transportGate for this checkpoint.
            PublishAdmission();
            // Registered inputs replay through the old request mailbox. The
            // original request sequence is the stable producer FIFO order.
            var queuedRequests=CaptureQueuedRequests();
            if(queuedRequests.Length>RouteRequestCapacity)
                throw new InvalidOperationException("Registered route replay exceeds checkpoint mailbox capacity.");
            var queuedAnswers=Answers.CopyItems();
            int requestCapacity=checked(allocating.Count+pending.Count+probes.Count+ProbeAnswerCount+queuedRequests.Length+RegisteredRequests.Count+(committedHostRoot?.Delivered.Count??0)+barrierAnswers.Count+queuedAnswers.Length);
            int geometryCapacity=checked(2+requestCapacity*2+installedExecutions.Count*2);
            var all=new NavigationCaptureRequests(requestCapacity);var geometries=new NavigationCaptureGeometries(geometryCapacity);
            geometries.Include(geometry);
            bool accounted(NavigationRequest request){foreach(var r in queuedRequests)if(ReferenceEquals(r,request))return true;if(committedHostRoot!=null)foreach(var r in RegisteredRequests)if(ReferenceEquals(r,request))return true;foreach(var answer in queuedAnswers)if(ReferenceEquals(answer?.Request,request))return true;foreach(var answer in barrierAnswers)if(ReferenceEquals(answer.Request,request))return true;return false;}
            foreach(var pair in probes)if(!accounted(pair.Value))throw new InvalidOperationException("A rally probe is held outside the quiescent mailboxes.");
            foreach(var pair in pending)if(!accounted(pair.Value)&&!(RetainedHeldStopIntent(pair.Value)&&!allocating.ContainsKey(pair.Key)&&(installedExecutions.OwnerFor(pair.Key)==null||installedExecutions.IsDetached(pair.Key))))throw new InvalidOperationException("A pending navigation request is held outside the quiescent mailboxes.");
            foreach(var pair in allocating)all.Include(pair.Value);
            foreach(var pair in pending)all.Include(pair.Value);
            foreach(var pair in probes)all.Include(pair.Value);
            foreach(var answer in ProbeAnswerRows)all.Include(answer.Request);
            foreach(var request in queuedRequests)all.Include(request);
            if(committedHostRoot!=null){foreach(var request in RegisteredRequests)all.Include(request);foreach(var e in committedHostRoot.Delivered)all.Include(((NavigationHostRow)e.Value).Request);}
            foreach(var answer in barrierAnswers)all.Include(answer.Request);
            foreach(var answer in queuedAnswers){if(answer==null)throw new InvalidOperationException("Null navigation answer in checkpoint.");all.Include(answer.Request);}
            int geometryIndex(NavGeometry g)=>geometries.Include(g);
            int baseIndex=geometryIndex(navigationGeometry);
            if(committedHostRoot?.AuthorityGeometry!=null&&!geometries.Contains(committedHostRoot.AuthorityGeometry.Facade)){if(ReferenceEquals(committedHostRoot.AuthorityGeometry,navigationGeometry.StorageRoot))geometries.Alias(committedHostRoot.AuthorityGeometry.Facade,baseIndex);else geometryIndex(committedHostRoot.AuthorityGeometry.Facade);}
            for(int i=0;i<all.Count;i++){geometryIndex(all[i].Geometry);geometryIndex(all[i].BaseGeometry);}
            foreach(var entry in installedExecutions.Values){geometryIndex(entry.MovementGeometry);geometryIndex(entry.BaseGeometry);}
            var dto=NavigationCaptureDtoStorage.CreateOwned(CaptureDtoLayout(all,geometries,queuedRequests,queuedAnswers),reservation);
            NavigationSessionState state=null;
            try{
            state=new NavigationSessionState{CaptureDto=dto,TransportVersion=1,CorridorWireVersion=1,InstalledExecutions=CaptureInstalledDirect(geometries),SurfaceSemanticsVersion=terrain?.SurfaceSemanticsVersion??1,SurfaceProviderId=terrain?.Id??NavLocation.FlatSurface,NavigationGeometryIndex=baseIndex,Generation=Generation,RequestSequence=requestSequence,GroupSequence=groupSequence,Groups=CaptureGroupsDirect(),GroupLayoutVersion=1,GroupKeys=CaptureKeys(groups),MemberGroupKeys=CaptureKeys(memberGroups),GroupLayouts=new[]{NavigationCommandLayout.Capture(groups),NavigationCommandLayout.Capture(memberGroups)},Geometry=SaveGeometry(geometry),NavigationGeometry=SaveGeometry(navigationGeometry),
                Crowd=Crowd.CaptureState(),Requests=new NavigationRequestState[all.Count],Orders=new NavigationEntityOrderState[orders.Count],
                Reservations=new NavigationEntityPointState[reservations.Count],RetainedGoals=new NavigationEntityPointState[retainedGoals.Count],
                MovementIdentities=new NavigationMovementIdentityState[movementIdentities.Count],ProbeRequestIndices=new int[probes.Count],ProbeAnswers=new NavigationAnswerState[ProbeAnswerCount],
                PendingRequestIndices=new int[pending.Count],RequestMailboxIndices=new int[queuedRequests.Length],AnswerMailbox=new NavigationAnswerState[queuedAnswers.Length],
                RejectedResults=RejectedResults,AppliedResults=AppliedResults,UnreachableResults=UnreachableResults,BarrierAnswers=new NavigationAnswerState[barrierAnswers.Count],
                TechnicalFailures=CaptureFailuresDirect()};
            for(int i=0;i<all.Count;i++){
                var r=all[i];state.Requests[i]=new NavigationRequestState{Session=r.Session,Request=r.Request,Entity=r.Entity,Order=r.Order,MemberIdentity=r.MemberIdentity,
                    Start=r.Start,Goal=r.Goal,StartLocation=r.StartLocation,GoalLocation=r.GoalLocation,ProfileId=r.ProfileId,ProfileRevision=r.ProfileRevision,ProfileValues=SaveProfile(r.Profile),CertifiedProviderBinding=r.CertifiedProviderBinding,
                    Geometry=SaveGeometry(r.Geometry),BaseGeometry=SaveGeometry(r.BaseGeometry),GeometryIndex=geometryIndex(r.Geometry),BaseGeometryIndex=geometryIndex(r.BaseGeometry),HeldIdentity=r.HeldIdentity,UsesCurrentGeometry=geometryIndex(r.Geometry)==baseIndex,UsesCurrentBaseGeometry=geometryIndex(r.BaseGeometry)==baseIndex};
            }
            int n=0;foreach(var pair in orders)state.Orders[n++]=new NavigationEntityOrderState{Entity=pair.Key,Order=pair.Value};
            n=0;foreach(var pair in reservations)state.Reservations[n++]=new NavigationEntityPointState{Entity=pair.Key,Point=pair.Value};
            n=0;foreach(var pair in retainedGoals)state.RetainedGoals[n++]=new NavigationEntityPointState{Entity=pair.Key,Point=pair.Value};
            n=0;foreach(var pair in pending)state.PendingRequestIndices[n++]=all.Index(pair.Value);
            n=0;foreach(var pair in probes)state.ProbeRequestIndices[n++]=all.Index(pair.Value);
            n=0;foreach(var pair in movementIdentities)state.MovementIdentities[n++]=new NavigationMovementIdentityState{Entity=pair.Key,Identity=pair.Value};
            n=0;foreach(var answer in ProbeAnswerRows)state.ProbeAnswers[n++]=new NavigationAnswerState{RequestIndex=all.Index(answer.Request),Route=answer.CopyRoute(),Status=answer.Status,Corridor=answer.Corridor};
            for(int i=0;i<queuedRequests.Length;i++)state.RequestMailboxIndices[i]=all.Index(queuedRequests[i]);
            for(int i=0;i<queuedAnswers.Length;i++)state.AnswerMailbox[i]=new NavigationAnswerState{RequestIndex=all.Index(queuedAnswers[i].Request),Route=queuedAnswers[i].CopyRoute(),Status=queuedAnswers[i].Status,Corridor=queuedAnswers[i].Corridor};
            for(int i=0;i<barrierAnswers.Count;i++)state.BarrierAnswers[i]=new NavigationAnswerState{RequestIndex=all.Index(barrierAnswers[i].Request),Route=barrierAnswers[i].CopyRoute(),Status=barrierAnswers[i].Status,Corridor=barrierAnswers[i].Corridor};
            state.CollectionLayouts=new[]{NavigationCommandLayout.Capture(pending),NavigationCommandLayout.Capture(orders),NavigationCommandLayout.Capture(reservations),NavigationCommandLayout.Capture(retainedGoals),NavigationCommandLayout.Capture(probes),NavigationCommandLayout.Capture(movementIdentities)};
            state.GeometryTable=new NavigationGeometryState[geometries.Count];for(int i=0;i<geometries.Count;i++)state.GeometryTable[i]=SaveGeometry(geometries[i]);
            state.AllocationRequestIndices=CaptureAllocationIndices(all);
            state.DeliveredRequestIndices=CaptureDeliveredIndices(all);
            state.RegisteredRequestIndices=CaptureRegisteredIndices(all);
            state.AdmissionPublicationIdentity=admissionPublicationIdentity;
            state.CaptureArrivalOutput=WorldWire.PackOwned(WriteArrivalAuthority,physicalHostBudget);
            state.ArrivalAuthority=state.CaptureArrivalOutput.Bytes;
            state.CaptureRequests=new NavOwnedStorage[all.Count];state.CaptureGeometries=new NavOwnedStorage[geometries.Count];state.CaptureGroups=new NavOwnedStorage[groups.Count];state.CaptureIdentities=new NavOwnedStorage[movementIdentities.Count];
            if(ownedProbes!=null&&liveProbeAnswers!=null){state.CaptureProbeAnswers=new NavOwnedStorage[liveProbeAnswers.Count];for(int i=0;i<liveProbeAnswers.Count;i++){var owner=liveProbeAnswers.OwnerAt(i);owner.Retain();state.CaptureProbeAnswers[i]=owner;}state.CaptureMailboxAnswers=new NavOwnedStorage[queuedAnswers.Length];for(int i=0;i<queuedAnswers.Length;i++){var owner=queuedAnswers[i].StorageOwner;owner?.Retain();state.CaptureMailboxAnswers[i]=owner;}state.CaptureBarrierAnswers=new NavOwnedStorage[barrierAnswers.Count];for(int i=0;i<barrierAnswers.Count;i++){var owner=barrierAnswers[i].StorageOwner;owner?.Retain();state.CaptureBarrierAnswers[i]=owner;}}
            CaptureAnswerTracking(state);
            state.CaptureExtraLayouts=new[]{NavigationCommandLayout.Capture(allocating),CaptureKeys(allocating),NavigationCommandLayout.Capture(technicalFailures),CaptureKeys(technicalFailures),CaptureCommandCapacities(),NavigationCommandLayout.Capture(arrivalLedger),CaptureLedgerRowOrder(reservation)};
            try{
                for(int i=0;i<all.Count;i++){var owner=all[i].StorageOwner;if(!(owner is NavigationHostRequest))throw new InvalidOperationException("Captured request has no immutable owner.");owner.Retain();state.CaptureRequests[i]=owner;}
                for(int i=0;i<geometries.Count;i++){var owner=geometries[i].StorageRoot;if(owner==null)throw new InvalidOperationException("Captured geometry has no immutable owner.");owner.Retain();state.CaptureGeometries[i]=owner;}
                if(!(groups is NavigationCommandSlots<long,GroupOrderState> groupSlots)&&groups.Count!=0)throw new InvalidOperationException("Captured groups have no owned live storage.");
                int g=0;foreach(var pair in groups){var owner=((NavigationCommandSlots<long,GroupOrderState>)groups).OwnerFor(pair.Key) as NavigationLiveGroup;if(owner==null)throw new InvalidOperationException("Captured group has no immutable version.");var version=owner.ImmutableVersion;if(!version.Data.BitwiseSame(new NavigationAuthorityGroup.Values(pair.Value)))throw new InvalidOperationException("Live group scalars differ from immutable version.");for(int m=0;m<pair.Value.Members.Length;m++){var member=(NavigationAuthorityMember)version.Members[m];var record=pair.Value.Members[m];if(!member.Data.BitwiseSame(new NavigationAuthorityMember.Values(record))||member.ContactPhase!=record.ContactPhase||member.GoalLocation?.SurfaceId!=record.GoalLocation?.SurfaceId)throw new InvalidOperationException("Live member differs from immutable version.");}version.Retain();state.CaptureGroups[g++]=version;}
                int identity=0;foreach(var pair in movementIdentities){var owner=(movementIdentities as NavigationCommandSlots<int,string>)?.OwnerFor(pair.Key);if(pair.Value!=null&&(!(owner is NavOwnedText text)||text.Value!=pair.Value))throw new InvalidOperationException("Movement identity has no exact owned name.");owner?.Retain();state.CaptureIdentities[identity++]=owner;}
                CaptureCrowdNameCatalog(state);CaptureActorRouteCatalog(state);dto.Bind(state);
                return state;
            }catch{state.Dispose();throw;}
            }catch{if(state!=null)state.Dispose();else dto.Dispose();throw;}
        }
        public void RestoreState(NavigationSessionState state)=>RestoreWorldState(state);
        private NavigationRequest[] RestoreWorldStateCore(NavigationSessionState state)
        {
            if(state==null||state.Generation!=Generation||state.AdmissionPublicationIdentity<0||state.RequestSequence<0||state.Requests==null||state.Orders==null||
                state.Reservations==null||state.RetainedGoals==null||state.PendingRequestIndices==null||state.RequestMailboxIndices==null||state.AnswerMailbox==null||
                (state.TransportVersion!=0&&state.TransportVersion!=1)||state.RequestMailboxIndices.Length>(state.TransportVersion==0?NavMailbox<NavigationRequest>.Capacity:RouteRequestCapacity)||state.AnswerMailbox.Length>NavMailbox<NavigationAnswer>.Capacity||
                state.RejectedResults<0||state.AppliedResults<0||state.UnreachableResults<0||pending.Count!=0||orders.Count!=0||reservations.Count!=0||retainedGoals.Count!=0||
                Requests.Count!=0||Answers.Count!=0||Crowd.Units.Count!=0||!SameGeometry(state.Geometry,geometry)||!SameGeometry(state.NavigationGeometry,navigationGeometry))
                throw new ArgumentException("Navigation session checkpoint or destination mismatch.",nameof(state));
            if(state.GeometryTable==null||state.GeometryTable.Length<1||state.NavigationGeometryIndex<0||state.NavigationGeometryIndex>=state.GeometryTable.Length||!SameGeometry(state.GeometryTable[0],geometry)||!SameGeometry(state.GeometryTable[state.NavigationGeometryIndex],navigationGeometry)||(state.NavigationGeometryIndex==0)!=Object.ReferenceEquals(geometry,navigationGeometry))throw new ArgumentException("Invalid geometry identity table.");
            if(state.RestoreBudget!=null){if(physicalHostBudget!=null&&!ReferenceEquals(physicalHostBudget,state.RestoreBudget))throw new ArgumentException("Destination has another outer ledger.");physicalHostBudget=state.RestoreBudget;}
            ValidateInstalledExecutionLayout(state);
            ValidateSurfaceState(state);
            NavigationHostRoot decodedHost;NavigationAuthorityOwners decodedAuthority;NavigationTrafficAuthority decodedTraffic=null;NavPagedList<long> decodedBank=null;long decodedPublication=0;
            if(state.HostPrepared){decodedBank=state.PreparedBank;state.PreparedBank=null;decodedHost=state.PreparedHost;decodedAuthority=state.PreparedAuthority;decodedTraffic=state.PreparedTraffic;state.PreparedTraffic=null;state.PreparedHost=null;state.PreparedAuthority=null;state.HostPrepared=false;}
            else DecodeDetachedHostAuthority(state.HostAuthority,physicalHostBudget,null,out decodedHost,out decodedAuthority,out decodedPublication,out decodedTraffic,out decodedBank);
            bool restoreComplete=false;
            try{
            replayOnlyProjection=decodedHost?.ReplayOnlyProjection??false;
            accruedCreditsEnabled=decodedHost?.AccruedCredits??false;
            if(accruedCreditsEnabled){if(decodedBank==null||!decodedBank.BelongsTo(physicalHostBudget))throw new ArgumentException("Saved bank belongs to another ledger.");NavigationAccruedCredits.Validate(decodedBank,decodedHost,Generation,state.RequestSequence,long.MaxValue-1);accruedCreditCap=(int)decodedBank[NavigationAccruedCredits.Cap];}else if(decodedBank!=null)throw new ArgumentException("Credit bank without accrued mode.");
            if(decodedPublication>0){if(state.AdmissionPublicationIdentity!=0&&state.AdmissionPublicationIdentity!=decodedPublication)throw new ArgumentException("Outer admission identity differs from BODY.");state.AdmissionPublicationIdentity=decodedPublication;}
            ValidateBodyGroupCatalog(decodedAuthority,state);
            AdoptDecodedAuthority(decodedAuthority);AdoptTerrainNames(decodedAuthority);
            var geometryTable=BodyFirstGeometryCatalog(decodedHost,decodedAuthority,state);
            var restored=new NavigationRequest[state.Requests.Length];long maxRequest=0;var requestIds=new HashSet<(long Session,long Request,int Role)>();
            for(int i=0;i<restored.Length;i++){
                var row=state.Requests[i];if(row==null||row.Session<1||row.Request<1||
                    (row.Entity<0?row.Order<0:row.Order<1)||row.Geometry==null||row.Geometry.Obstacles==null)
                    throw new ArgumentException("Invalid navigation request checkpoint.",nameof(state));
                if(row.GeometryIndex<0||row.GeometryIndex>=geometryTable.Length||row.BaseGeometryIndex<0||row.BaseGeometryIndex>=geometryTable.Length||!requestIds.Add((row.Session,row.Request,RequestCatalogRole(decodedHost,decodedAuthority,state,i))))throw new ArgumentException("Invalid request identity.");
                var bodyOwner=BodyRequestOwnerAt(decodedHost,decodedAuthority,state,i);
                var requestGeometry=geometryTable[row.GeometryIndex];
                if(!SameGeometry(row.Geometry,requestGeometry)||!SameGeometry(row.BaseGeometry,geometryTable[row.BaseGeometryIndex])||row.UsesCurrentGeometry!=(row.GeometryIndex==state.NavigationGeometryIndex)||row.UsesCurrentBaseGeometry!=(row.BaseGeometryIndex==state.NavigationGeometryIndex))throw new ArgumentException("Invalid request geometry identity.");
                if(row.BaseGeometry==null||row.UsesCurrentBaseGeometry&&!SameGeometry(row.BaseGeometry,navigationGeometry)||row.UsesCurrentGeometry&&!SameGeometry(row.Geometry,navigationGeometry))throw new ArgumentException("Request geometry mismatch.",nameof(state));
                if(bodyOwner!=null){ValidateBodyRequestCatalog(row,bodyOwner);restored[i]=bodyOwner.Request;}
                else{var requestProfile=LoadProfile(row);
                    restored[i]=new NavigationRequest(row.Session,row.Request,row.Entity,row.Order,requestProfile,requestGeometry,row.Start,row.Goal,geometryTable[row.BaseGeometryIndex],row.HeldIdentity,row.MemberIdentity,row.StartLocation,row.GoalLocation);
                    restored[i].CertifiedProviderBinding=row.CertifiedProviderBinding;
                }
                if(row.Session==Generation)maxRequest=Math.Max(maxRequest,row.Request);
            }
            BindRestoredHostCatalog(decodedHost,decodedAuthority,state,restored);
            if(maxRequest>state.RequestSequence)throw new ArgumentException("Request sequence mismatch.",nameof(state));
            var nextOrders=new Dictionary<int,long>();foreach(var row in state.Orders){if(row==null||row.Order<0||nextOrders.ContainsKey(row.Entity))throw new ArgumentException("Invalid order checkpoint.",nameof(state));nextOrders.Add(row.Entity,row.Order);}
            var nextPending=new Dictionary<int,NavigationRequest>();foreach(int index in state.PendingRequestIndices){
                if(index<0||index>=restored.Length)throw new ArgumentException("Invalid pending request index.",nameof(state));
                var r=restored[index];if(nextPending.ContainsKey(r.Entity)||r.Session!=Generation||r.ProfileId!=profile.ProfileId||r.ProfileRevision!=profile.Revision||
                    !(Object.ReferenceEquals(r.BaseGeometry,navigationGeometry)||navigationGeometry.StorageRoot!=null&&ReferenceEquals(r.BaseGeometry.StorageRoot,navigationGeometry.StorageRoot))||!nextOrders.TryGetValue(r.Entity,out var order)||order!=r.Order)
                    throw new ArgumentException("Invalid pending request binding.",nameof(state));nextPending.Add(r.Entity,r);
            }
            var nextReservations=new Dictionary<int,NavPoint>();foreach(var row in state.Reservations){if(row==null||nextReservations.ContainsKey(row.Entity))throw new ArgumentException("Invalid reservation checkpoint.",nameof(state));nextReservations.Add(row.Entity,row.Point);}
            var nextGoals=new Dictionary<int,NavPoint>();foreach(var row in state.RetainedGoals){if(row==null||nextGoals.ContainsKey(row.Entity))throw new ArgumentException("Invalid retained goal checkpoint.",nameof(state));nextGoals.Add(row.Entity,row.Point);}
            foreach(int index in state.RequestMailboxIndices)if(index<0||index>=restored.Length)throw new ArgumentException("Invalid request mailbox index.",nameof(state));
            foreach(var answer in state.AnswerMailbox)if(answer==null||answer.RequestIndex<0||answer.RequestIndex>=restored.Length||!ValidSavedAnswer(answer))throw new ArgumentException("Invalid answer mailbox entry.",nameof(state));
            var accountedIndices=new HashSet<int>(state.RequestMailboxIndices);
            foreach(int index in state.RegisteredRequestIndices??Array.Empty<int>())if(index<0||index>=restored.Length||!accountedIndices.Add(index))throw new ArgumentException("Invalid registered host catalog index.");
            foreach(var answer in state.AnswerMailbox)accountedIndices.Add(answer.RequestIndex);
            var savedBarrier=state.BarrierAnswers??Array.Empty<NavigationAnswerState>();
            foreach(var answer in savedBarrier){if(answer==null||answer.RequestIndex<0||answer.RequestIndex>=restored.Length||!ValidSavedAnswer(answer)||!accountedIndices.Add(answer.RequestIndex))throw new ArgumentException("Invalid barrier answer.");}
            // Unaccounted pending is validated after actor/group/goal hydration:
            // only the retained held-stop intent may outlive its consumed answer.
            var identityRows=state.MovementIdentities??Array.Empty<NavigationMovementIdentityState>();
            var identities=new HashSet<int>();foreach(var row in identityRows)if(row==null||row.Identity==null||!identities.Add(row.Entity)||!nextOrders.ContainsKey(row.Entity))throw new ArgumentException("Invalid movement identity.");
            foreach(var pair in nextGoals)if(!identities.Contains(pair.Key))throw new ArgumentException("Missing saved movement identity.");
            var probeIds=new HashSet<int>();foreach(int index in state.ProbeRequestIndices??Array.Empty<int>())if(index<0||index>=restored.Length||restored[index].Entity>=0||restored[index].Session!=Generation||!probeIds.Add(restored[index].Entity)||!accountedIndices.Contains(index)||!Object.ReferenceEquals(restored[index].Geometry,navigationGeometry))throw new ArgumentException("Invalid probe binding.");
            foreach(var answer in state.ProbeAnswers??Array.Empty<NavigationAnswerState>())if(answer==null||answer.RequestIndex<0||answer.RequestIndex>=restored.Length||restored[answer.RequestIndex].Entity>=0||!ValidSavedAnswer(answer))throw new ArgumentException("Invalid probe answer.");
            var savedFailures=state.TechnicalFailures??Array.Empty<NavigationTechnicalFailureState>();
            if(state.TransportVersion==0&&savedFailures.Length!=0)throw new ArgumentException("Legacy transport has technical failures.");
            var failureIds=new HashSet<int>();
            foreach(var failure in savedFailures){
                var actor=state.Crowd.Units.FirstOrDefault(u=>u.Id==failure.Entity);
                if(!failureIds.Add(failure.Entity)||actor==null||actor.FailedRequest!=failure.Request||
                    actor.LastFailure!=NavigationOutcome.Rejected||
                    (failure.Status!=NavSolveStatus.InvalidEndpoint&&failure.Status!=NavSolveStatus.BlockedConnector&&failure.Status!=NavSolveStatus.CapacityExceeded))
                    throw new ArgumentException("Invalid technical route failure.");
            }
            if(decodedAuthority?.ActorGlobalCatalog!=null&&(decodedAuthority.ActorGlobalCatalog.Count!=state.Crowd.Units.Length||decodedAuthority.ActorLocalCatalog.Count!=state.Crowd.Units.Length||decodedAuthority.InstalledCatalog.Count!=state.InstalledExecutions.Length))throw new ArgumentException("Actor/installed BODY role counts differ from DTO.");
            Crowd.RestoreStateCore(state.Crowd,decodedAuthority?.ActorGlobalCatalog,decodedAuthority?.ActorLocalCatalog);
            BindRestoredCrowdNames(decodedAuthority,state);
            foreach(var failure in savedFailures)technicalFailures.Add(failure.Entity,failure.Status);
            foreach(var pair in nextOrders)orders.Add(pair.Key,pair.Value);
            foreach(var pair in nextPending)pending.Add(pair.Key,pair.Value);
            foreach(var row in state.MovementIdentities??Array.Empty<NavigationMovementIdentityState>())movementIdentities.Add(row.Entity,row.Identity);
            foreach(var pair in nextGoals)if(!movementIdentities.ContainsKey(pair.Key))throw new ArgumentException("Missing saved movement identity.");
            if(decodedAuthority?.HasAnswerTracking==true)RestoreOwnedAnswerTracking(decodedAuthority);
            if(decodedAuthority?.HasProbeStorage==true)RestoreOwnedProbeTables(decodedAuthority,decodedHost,state,restored);
            else{
                foreach(int index in state.ProbeRequestIndices??Array.Empty<int>())probes.Add(restored[index].Entity,restored[index]);
                foreach(var answer in state.ProbeAnswers??Array.Empty<NavigationAnswerState>()){
                    var restoredAnswer=ResolveRestoredHostAnswer(decodedHost,restored[answer.RequestIndex],answer);
                    RestoreBarrierPayload(restoredAnswer,false);transportOwner.Answer(restoredAnswer.Request);probeAnswers.Enqueue(restoredAnswer);TrackCorridorAnswer(restoredAnswer);}
            }
            foreach(var pair in nextReservations)reservations.Add(pair.Key,pair.Value);
            foreach(var pair in nextGoals)retainedGoals.Add(pair.Key,pair.Value);
            if(state.CollectionLayouts==null||state.CollectionLayouts.Length!=6)throw new ArgumentException("Missing navigation collection layouts.");
            NavigationCommandLayout.Restore(pending,state.CollectionLayouts[0]);NavigationCommandLayout.Restore(orders,state.CollectionLayouts[1]);NavigationCommandLayout.Restore(reservations,state.CollectionLayouts[2]);NavigationCommandLayout.Restore(retainedGoals,state.CollectionLayouts[3]);NavigationCommandLayout.Restore(probes,state.CollectionLayouts[4]);NavigationCommandLayout.Restore(movementIdentities,state.CollectionLayouts[5]);
            requestSequence=state.RequestSequence;RejectedResults=state.RejectedResults;AppliedResults=state.AppliedResults;UnreachableResults=state.UnreachableResults;
            foreach(int index in state.RequestMailboxIndices)if(!Requests.TryEnqueue(restored[index]))throw new ArgumentException("Request replay exceeds transport capacity.");
            for(int i=0;i<state.AnswerMailbox.Length;i++){var answer=state.AnswerMailbox[i];var restoredAnswer=ResolveRestoredAnswerRole(decodedAuthority?.MailboxAnswerCatalog,i,decodedHost,restored[answer.RequestIndex],answer);
                if(!Answers.TryEnqueue(restoredAnswer))throw new ArgumentException("Answer replay exceeds transport capacity.");TrackCorridorAnswer(restoredAnswer);}
            for(int i=0;i<savedBarrier.Length;i++){var answer=savedBarrier[i];var restoredAnswer=ResolveRestoredAnswerRole(decodedAuthority?.BarrierAnswerCatalog,i,decodedHost,restored[answer.RequestIndex],answer);
                RestoreBarrierPayload(restoredAnswer);transportOwner.Answer(restoredAnswer.Request);TrackCorridorAnswer(restoredAnswer);}
            if(decodedAuthority?.HasAnswerTracking==true)RestoreTrackedTransport(decodedAuthority);
            UseLegacyArrivalTablesForDecode();RestoreGroups(state);
            foreach(int index in state.PendingRequestIndices)if(!accountedIndices.Contains(index)&&(state.TransportVersion!=1||!RetainedHeldStopPendingForRestore(restored[index],state)))throw new ArgumentException("Pending request is outside the checkpoint mailboxes.",nameof(state));
            RestoreArrivalAuthority(state,restored,decodedHost,decodedAuthority?.ArrivalCatalog,decodedAuthority?.LeaseCatalog);
            var corridorProviders=new Dictionary<Tuple<NavGeometry,string>,ILayeredNavigationProvider>();
            foreach(var savedAnswer in (state.ProbeAnswers??Array.Empty<NavigationAnswerState>())
                .Concat(state.AnswerMailbox).Concat(savedBarrier)){
                var original=restored[savedAnswer.RequestIndex];
                if(savedAnswer.Corridor==null){
                    if(savedAnswer.Status==NavSolveStatus.Ready&&original.CertifiedProviderBinding!=null)
                        throw new ArgumentException("Missing certified corridor sidecar.");
                }else if(savedAnswer.Status!=NavSolveStatus.Ready||
                    original.CertifiedProviderBinding!=savedAnswer.Corridor.ProviderBinding||
                    !QualifyCorridor(original,savedAnswer.Route,savedAnswer.Corridor,corridorProviders,
                        nextPending.TryGetValue(original.Entity,out var active)&&
                        Object.ReferenceEquals(active,original)))
                    throw new ArgumentException("Invalid restored corridor answer.");
            }
            RestoreInstalledExecutions(state,geometryTable,decodedAuthority);
            BindInstalledHostOwners(decodedHost);
            ValidateMarchCheckpoint();
            ValidateRestoredTraffic(decodedTraffic);trafficAuthority=decodedTraffic;decodedTraffic=null;creditBank=decodedBank;decodedBank=null;
            // A new snapshot object represents the captured accepted publication.
            // Legacy formats deliberately receive a fresh identity and replay.
            InstallRestoredHost(decodedHost,state,restored);
            OwnRestoredCommandBacking(decodedAuthority,state.AdmissionPublicationIdentity,restored);
            // Catalog directories stage cold hydration; runtime maps now retain
            // each surviving request/group owner. Keep the same lean core shape
            // as the hot Session, rather than retaining decode-only directories.
            if(decodedAuthority?.HasCatalog==true){var decodedDirectories=ReplaceAuthorityCore(geometry,navigationGeometry,profile);decodedDirectories.Release();}
            if(decodedAuthority?.HasCatalog!=true){PublishLegacyAdmissionNoThrow(null);admissionPublicationIdentity=state.AdmissionPublicationIdentity==0?0:state.AdmissionPublicationIdentity-1;}
            PublishAdmission();
            restoreComplete=true;return restored;
            }finally{
                if(!restoreComplete&&ReferenceEquals(committedHostRoot,decodedHost)){committedHostRoot=null;decodedHost?.Release();}
                else if(!ReferenceEquals(committedHostRoot,decodedHost))decodedHost?.Release();
                decodedAuthority?.Release();decodedTraffic?.Release();decodedBank?.Release();
            }
        }
        public bool MoveGroup(int[] ids, NavPoint[] goals)
        {
            var prepared=TryPreparedOrdinaryMoveGroup(ids,goals);
            if(prepared==false&&commandGroup!=null)commandMoveRefused=true;
            return prepared??AdmissionBatch(()=>MoveGroupLocked(ids,goals));
        }
        private bool MoveGroupLocked(int[] ids, NavPoint[] goals)
        {
            if(ids==null||goals==null||ids.Length==0||ids.Length!=goals.Length||Requests.Count+RegisteredRouteCount+allocating.Count+ids.Length>RouteRequestCapacity)return false;
            var unique=new HashSet<int>();
            for(int i=0;i<ids.Length;i++)if(!unique.Add(ids[i])||!Crowd.TryGet(ids[i],out var member)||!geometry.IsFree(goals[i],member.Radius)||!Crowd.Locate(goals[i],member.Radius,out _))return false;
            if(AdmitArrivalGroup(ids,goals))return true;
            if(commandGroup==null)BindLegacyGroup(ids,goals[0]);
            foreach(int i in Enumerable.Range(0,ids.Length).OrderBy(i=>ids[i]))Move(ids[i],goals[i]);
            return true;
        }
        public int UnreachableResults {get;private set;}
        public NavPoint[] AllocateArrivalSlots(NavPoint center,IReadOnlyList<int> group)
        {
            var members=new HashSet<int>(group);var occupied=new List<NavPoint>();
            foreach(var unit in Crowd.Units)if(!members.Contains(unit.Id)){occupied.Add(unit.Position);NavPoint reserved;if(reservations.TryGetValue(unit.Id,out reserved))occupied.Add(reserved);}
            double radius=0;foreach(int id in group)if(Crowd.TryGet(id,out var member))radius=Math.Max(radius,member.Radius);
            return NavArrivalAllocator.Allocate(geometry,profile.ForUnit(radius>0?radius:profile.Radius,profile.TankSpeed,profile.TankTurnSpeed),center,group.Count,occupied);
        }
        private string HeldIdentity(int id)
        {
            var raw=LegacyHeldIdentity(id);
            return raw.Length<=256?raw:NavContract.Digest(w=>{w.Write("held-binding-v1");w.Write(raw);});
        }
        private bool CurrentHeldBinding(string binding,int id)=>binding==HeldIdentity(id)||binding==LegacyHeldIdentity(id);
        private string LegacyHeldIdentity(int id)
        {
            if(!Crowd.TryGet(id,out var mover))return "";
            var rows=new List<string>();var culture=System.Globalization.CultureInfo.InvariantCulture;
            foreach(var other in Crowd.Units)if(other.Id!=id&&other.Held&&other.Team==mover.Team)
                rows.Add(other.Id+":"+other.Incarnation+":"+other.MobilityRevision+":"+other.Team+":"+other.Position.X.ToString("R",culture)+":"+other.Position.Z.ToString("R",culture)+":"+other.Radius.ToString("R",culture));
            return string.Join("|",rows);
        }
        private NavGeometry MovementGeometry(int id)
        {
            Crowd.TryGet(id,out var mover);var solids=new List<NavObstacle>(navigationGeometry.Obstacles);
            foreach(var other in Crowd.Units)if(other.Id!=id&&other.Held&&other.Team==mover.Team)solids.Add(new NavObstacle(other.Position,other.Radius));
            return solids.Count==navigationGeometry.Obstacles.Count?navigationGeometry:new NavGeometry(navigationGeometry.HalfExtent,solids.ToArray(),navigationGeometry.Revision);
        }
        private IDictionary<int,string> movementIdentities=new AuthoritySlotMap<int,string>();
        private void RefreshHeldMovement()
        {
            foreach(var pair in new List<KeyValuePair<int,NavPoint>>(retainedGoals))if(!allocating.ContainsKey(pair.Key)&&Crowd.TryGet(pair.Key,out var mover)&&movementIdentities.TryGetValue(pair.Key,out var identity)&&!CurrentHeldBinding(identity,pair.Key)){
                // Freeze old movement while replanning; a held mover awaiting explicit replacement remains held.
                DetachInstalledExecution(pair.Key);
                if(!mover.Held)Crowd.Stop(pair.Key,false);
                if(!Move(pair.Key,pair.Value))MovementCancelled?.Invoke(pair.Key);
            }
        }
        public bool Move(int id,NavPoint goal)
        {
            if(commandGroup==null&&admissionBatchDepth==0){
                if(!physicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,Generation,admissionPublicationIdentity,out var inputScope))return false;
                using(inputScope)using(NavStorageScratch.Reserve(inputScope.OwnedReservation(),88)){
                    var prepared=TryPreparedOrdinaryMoveGroup(new[]{id},new[]{goal},true);if(prepared.HasValue)return prepared.Value;
                }
            }
            return AdmissionBatch(()=>MoveLocked(id,goal));
        }
        private bool MoveLocked(int id,NavPoint goal)
        {
            NavUnit unit;
            if(!Crowd.TryGet(id,out unit)||!geometry.IsFree(goal,unit.Radius)||!Crowd.Locate(goal,unit.Radius,out var goalLocation)||Requests.Count+RegisteredRouteCount>=RouteRequestCapacity)return false;
            if(commandGroup==null&&Member(id)==null)BindLegacyGroup(new[]{id},goal);
            long order;orders.TryGetValue(id,out order);order++;
            var request=new NavigationRequest(Generation,++requestSequence,id,order,profile.ForUnit(unit.Radius,unit.Speed,unit.TurnSpeed),MovementGeometry(id),unit.Position,goal,navigationGeometry,HeldIdentity(id),RequestIdentity(unit),unit.Location,goalLocation);
            if(!Requests.TryEnqueue(request))return false;
            CancelArrival(id,false);orders[id]=order; pending.Remove(id); retainedGoals[id]=goal;
            var groupMember=Member(id);if(groupMember!=null){groupMember.Goal=goal;groupMember.GoalLocation=goalLocation;groupMember.HasGoal=true;}
            ClearFailure(id);pending[id]=request;movementIdentities[id]=request.HeldIdentity;reservations[id]=goal;PublishAdmission();RouteRequested?.Invoke(request);return true;
        }
        public void Stop(int id,bool hold){if(TryOwnedStopGroup(new[]{id},hold).HasValue)return;using(EnterTransport()){CancelArrival(id,true);ClearFailure(id);MovementCancelled?.Invoke(id);long order;orders.TryGetValue(id,out order);orders[id]=order+1;pending.Remove(id);reservations.Remove(id);retainedGoals.Remove(id);movementIdentities.Remove(id);SetInstalledExecution(id,null);Crowd.Stop(id,hold);PublishAdmission();}}
        public bool Remove(int id)
        {
            using(EnterTransport())if(!Crowd.TryGet(id,out _))return false;
            var owned=TryOwnedStopGroup(new[]{id},false,remove:true);
            if(owned.HasValue){if(!owned.Value)throw new InvalidOperationException("Owned actor removal preparation refused.");return true;}
            using(EnterTransport())return RemoveLocked(id);
        }
        private bool RemoveLocked(int id)
        {
            var spatial=Crowd.PrepareRemovalSpatial(id);
            try{
                CancelArrival(id,true);technicalFailures.Remove(id);
                // An actor that never received a route has no stale result to
                // invalidate. Do not birth a tombstone in a sealed paid table;
                // existing orders still advance before their actor disappears.
                if(orders.TryGetValue(id,out long order))orders[id]=checked(order+1);
                pending.Remove(id);reservations.Remove(id);retainedGoals.Remove(id);movementIdentities.Remove(id);DetachMember(id);SetInstalledExecution(id,null);
                var removed=Crowd.RemovePrepared(id,spatial);if(removed)spatial=null;PublishAdmission();return removed;
            }finally{spatial?.Release();}
        }
        public bool CanRebind => pending.Count+allocating.Count+RegisteredRouteCount<=RouteRequestCapacity;
        public void Rebind(NavigationProfile next)
        {
            var owned=TryOwnedRebind(next);if(owned.HasValue){if(!owned.Value)throw new InvalidOperationException("Owned profile rebind preparation refused.");return;}
            if(physicalArrivalEnabled&&groupCommandBacking!=null)throw new InvalidOperationException("Owned profile rebind source cannot use legacy mutation.");
            AdmissionBatch(()=>RebindLocked(next));
        }
        private void RebindLocked(NavigationProfile next)
        {
            if(!CanRebind)throw new InvalidOperationException("Navigation still has too many in-flight routes to rebind atomically.");
            // Geometry and existing routes are unchanged. Reissue only in-flight solves
            // so a rule change neither restarts moving actors nor floods the mailbox.
            var waiting=new List<NavigationRequest>(pending.Values);
            bool compatible=SameNavigationBinding(profile,next);
            var retiredCore=ReplaceAuthorityCore(geometry,navigationGeometry,next);
            try{Crowd.Rebind(profile);
            foreach(var intent in allocating.Values.ToArray())RetryArrival(intent);
            foreach(var pair in installedExecutions){
                var row=pair.Value;NavUnit actor=null;
                bool preserve=compatible&&!installedExecutions.IsDetached(pair.Key)&&Crowd.TryGet(pair.Key,out actor)&&
                    TryActiveInstalledExecution(pair.Key,out _)&&
                    ReferenceEquals(row.BaseGeometry,navigationGeometry)&&
                    row.Corridor.SurfaceProviderId==(terrain?.Id??NavLocation.FlatSurface);
                if(preserve)preserve=SameNavigationBinding(row.Profile,
                    next.ForUnit(actor.Radius,actor.Speed,actor.TurnSpeed));
                if(!preserve)installedExecutions.SetDetachedNoThrow(pair.Key,true);
            }
            pending.Clear();probes.Clear();
            foreach(var answer in ProbeAnswerRows)ReleaseCorridorAnswer(answer);ClearProbeAnswersNoThrow();
            foreach(var answer in barrierAnswers)ReleaseCorridorAnswer(answer);ClearBarrierAnswers();
            while(Requests.TryDiscard(out _)){}while(Answers.TryDequeue(out var answer))ReleaseCorridorAnswer(answer);
            foreach(var request in waiting)if(!RetryAssignedArrival(request)&&Crowd.TryGet(request.Entity,out _)&&!Move(request.Entity,request.Goal)){RecordFailure(request,NavigationOutcome.Rejected);MovementCancelled?.Invoke(request.Entity);}
            PublishAdmission();
            }finally{retiredCore?.Release();}
        }
        private static bool SameNavigationBinding(NavigationProfile current,NavigationProfile next)
        {
            if(current==null||next==null||current.ProfileId!=next.ProfileId)return false;
            if(current.WireValueCount!=next.WireValueCount)return false;for(int i=0;i<current.WireValueCount;i++)if(current.WireValueAt(i)!=next.WireValueAt(i))return false;
            return true;
        }
        public void ChangeGeometry(NavGeometry next)
        {if(!TryChangeGeometry(next))throw new InvalidOperationException("Owned geometry replacement preparation refused.");}
        internal bool TryChangeGeometry(NavGeometry next,PlayableDomain domain=null)
        {
            if(next==null)throw new ArgumentNullException(nameof(next));
            var owned=TryOwnedOrdinaryRebind(profile,geometryTarget:next,geometryDomain:domain);if(owned.HasValue)return owned.Value;
            if(physicalArrivalEnabled&&groupCommandBacking!=null)return false;
            AdmissionBatch(()=>ChangeGeometryLocked(next));return true;
        }
        private void ChangeGeometryLocked(NavGeometry next)
        {
            if(next==null)throw new ArgumentNullException("next");
            var previousRequests=pending.ToDictionary(p=>p.Key,p=>p.Value);var intents=new List<KeyValuePair<int,NavPoint>>(retainedGoals);
            var retiredCore=ReplaceAuthorityCore(next,next,profile);
            try{next=geometry;
            foreach(var key in arrivalLedger.Where(p=>p.Value.State==NavArrivalReservationKind.InstalledExecution).Select(p=>p.Key).ToArray())arrivalLedger.Remove(key);
            foreach(var intent in allocating.Values.ToArray())RetryArrival(intent);
            installedExecutions.Clear();installedCorridorBytes=installedGeometryBytes=0;pending.Clear();reservations.Clear();probes.Clear();
            foreach(var member in CaptureKeys(memberGroups))ResetMarchProgress(member);
            foreach(var answer in ProbeAnswerRows)ReleaseCorridorAnswer(answer);ClearProbeAnswersNoThrow();
            foreach(var answer in barrierAnswers)ReleaseCorridorAnswer(answer);ClearBarrierAnswers();
            while(Requests.TryDiscard(out _)){} while(Answers.TryDequeue(out var answer))ReleaseCorridorAnswer(answer);
            Crowd.SetGeometry(next);
            foreach(var intent in intents) if(!allocating.ContainsKey(intent.Key)&&!(previousRequests.TryGetValue(intent.Key,out var previous)&&RetryAssignedArrival(previous))&&Crowd.TryGet(intent.Key,out _)&&!Move(intent.Key,intent.Value)){if(previousRequests.TryGetValue(intent.Key,out var failed))RecordFailure(failed,NavigationOutcome.Rejected);MovementCancelled?.Invoke(intent.Key);}
            PublishAdmission();
            }finally{retiredCore?.Release();}
        }
        // Authority only. Reissue held-geometry changes before freezing this tick's jobs.
        internal bool PrepareDeliveryBarrier(PlayableAuthorityTick authority=null){PrepareOwnedReadyArrivals();var owned=TryOwnedHeldMaintenance(authority);if(owned.HasValue)return owned.Value;using(EnterTransport()){RefreshHeldMovement();CommitReadyArrivals();PublishAdmission();authority?.CompleteMaintenanceNoThrow();return true;}}
        internal bool DeliveryBarrierReady
        {
            get
            {
                // An allocation reservation is not a solver lock: retry the frozen barrier.
                PrepareOwnedReadyArrivals();
                using(EnterTransport()){CommitReadyArrivals();}
                // Drain and classify under the same authority gate as prepared
                // ingress. Allocation still fences gameplay, but cannot prevent
                // draining the transport needed to let that allocation finish.
                bool complete=DrainBarrierMailbox(out bool drained),ready=false;
                using(EnterTransport()){
                    if(complete&&allocating.Count==0&&!arrivalAttempts.Values.Any(a=>a.Reconstructing)&&(hostAllocationThread==0||pending.Count==0)){
                        var delivered=new HashSet<NavigationRequest>();foreach(var answer in barrierAnswers)delivered.Add(answer.Request);
                        ready=true;foreach(var request in pending.Values)if(!delivered.Contains(request)){ready=false;break;}
                    }
                }
                if(drained)SignalHost();return ready;
            }
        }
        // Authority-only notification after a route is actually installed, never on admission.
        internal event Action<NavigationRequest> RouteActivated;
        internal event Action<NavigationRequest> RouteRequested;
        internal event Action<NavigationRequest,NavigationOutcome> RouteFailed;
        internal event Action<int> MovementCancelled;
        internal Func<int,bool> PreserveInstalledExecution;
        private void ClearFailure(int id){technicalFailures.Remove(id);if(!Crowd.TryGet(id,out var u))return;u.FailedRequest=u.FailedOrder=u.FailedGroupId=u.FailedRevision=0;u.FailedGoal=null;u.LastFailure=NavigationOutcome.Idle;}
        private void RecordFailure(NavigationRequest r,NavigationOutcome outcome,NavigationPreparedCrowdNameWrite names=null){names?.ApplyNoThrow(false);technicalFailures.Remove(r.Entity);if(!Crowd.TryGet(r.Entity,out var u))return;var m=Member(r.Entity);u.FailedRequest=r.Request;u.FailedOrder=r.Order;u.FailedGroupId=r.MemberIdentity?.GroupId??GroupFor(r.Entity)?.GroupId??0;u.FailedRevision=r.MemberIdentity?.OrderRevision??m?.OrderRevision??0;u.FailedGoal=r.GoalLocation;u.LastFailure=outcome;RouteFailed?.Invoke(r,outcome);}
        public void ApplyResults()=>TryApplyResultsPrepared();
        private bool TryApplyResultsPrepared()
        {
            PrepareOwnedReadyArrivals();int remaining;using(EnterTransport())remaining=checked(barrierAnswers.Count+Answers.Count);
            while(remaining>0){
                if(!TryPrepareAnswerNamesBatch(out var single,true))return false;
                using(single){var owned=TryPublishOwnedResultBatch(single,0,false,true)??TryPublishOwnedProbeAnswer(single)??TryPublishOwnedDiscard(single)??TryPublishOwnedOrdinaryFailure(single)??TryPublishOwnedReady(single);if(owned.HasValue){if(!owned.Value)return false;}else{if(!PrepareCompatibilityInstallDestinations(single))return false;using(EnterTransport()){if(!AnswerNamesSourceCurrent(single))return false;AdmissionBatch(()=>ApplyResultsLocked(single));}}}
                if(--remaining==0)return true;
            }
            // ApplyResults maintains held goals even with no answers. Prepare
            // that replacement work through the same owned result-only path.
            if(!TryPrepareAnswerNamesBatch(out var names,false,true))return false;
            using(names){
                var owned=TryPublishOwnedResultBatch(names,0,false,true);
                if(owned.HasValue)return owned.Value;
                using(EnterTransport()){if(!AnswerNamesSourceCurrent(names))return false;AdmissionBatch(()=>ApplyResultsLocked(names));}
            }
            return true;
        }
        private void ApplyResultsLocked(NavigationPreparedAnswerNames names)
        {
            if(hostAllocationThread!=0)return;
            CommitReadyArrivals();RefreshHeldMovement();
            var corridorProviders=new Dictionary<Tuple<NavGeometry,string>,ILayeredNavigationProvider>();
            var ready=new Queue<NavigationAnswer>();bool drained=false;NavigationBarrierReceipt retiredBarrier=null;
            try{
            if(names?.Single==true){
                ready.Enqueue(names.Answers[0]);
                if(names.BarrierCount>0)retiredBarrier=TakeBarrierAnswerNoThrow(names.BarrierIndex);
                else{if(!Answers.TryDequeue(out var incoming)||!ReferenceEquals(incoming,names.Answers[0]))throw new InvalidOperationException("Single answer changed before compatibility drain.");drained=true;}
            }else{
                barrierAnswers.Sort((a,b)=>a.Request.Request.CompareTo(b.Request.Request));foreach(var answer in barrierAnswers)ready.Enqueue(answer);retiredBarrier=TakeAllBarrierAnswersNoThrow();
                while(Answers.TryDequeue(out var incoming)){ready.Enqueue(incoming);drained=true;}
            }
            if(drained)SignalHost();
            while(ready.Count>0){
                var answer=ready.Dequeue();
                var r=answer.Request;NavigationRequest expected;
                if(r.Entity<0){
                    if(r.Session==Generation&&(Object.ReferenceEquals(r.Geometry,navigationGeometry)||
                        IsCurrentPhysicalAuthorityGeometry(r))&&probes.TryGetValue(r.Entity,out expected)&&Object.ReferenceEquals(expected,r)&&
                        (answer.Status!=NavSolveStatus.Ready||QualifyCorridor(r,answer.CopyRoute(),answer.Corridor,corridorProviders,true))){
                        if(answer.StorageOwner!=null&&(names==null||!names.HasLegacyProbeDestination(answer)))throw new InvalidOperationException("Missing prepaid compatibility producer payload.");
                        AppendLegacyProbeReceiptNoThrow(names?.TakeLegacyProbeDestination(answer));probes.Remove(r.Entity);probeAnswers.Enqueue(answer);
                    }else{ReleaseCorridorAnswer(answer);RejectedResults++;}
                    continue;
                }
                ForgetCorridorCharge(answer);
                try{
                if(r.Session!=Generation||r.ProfileId!=profile.ProfileId||r.ProfileRevision!=profile.Revision||
                    !(Object.ReferenceEquals(r.BaseGeometry,navigationGeometry)||IsCurrentPhysicalAuthorityGeometry(r))||!CurrentHeldBinding(r.HeldIdentity,r.Entity)
                    ||!CurrentMember(r)||!pending.TryGetValue(r.Entity,out expected)||expected.Request!=r.Request||expected.Order!=r.Order||!Object.ReferenceEquals(expected,r)){RejectedResults++;continue;}
                var nameWrite=names?.For(answer);var routeWrite=names?.RouteFor(answer);nameWrite?.Validate(groupCommandBacking.CrowdNames);
                pending.Remove(r.Entity);var route=answer.ReadOnlyRouteBacking;
                long oldBytes=installedExecutions.TryGetValue(r.Entity,out var oldInstalled)
                    ?oldInstalled.Corridor.EstimatedRetainedBytes+16L*oldInstalled.InstalledRoute.Length:0;
                bool overCorridorBudget=answer.Corridor!=null&&
                    hostRetainedBytes+answerCorridorBytes+installedCorridorBytes-oldBytes+
                    ProspectiveGeometryBytes(r.Entity,r)+answer.Corridor.EstimatedRetainedBytes+16L*(route.Length+1)>
                    LayeredNavigationScheduler.MaxRetainedBytes;
                var effectiveStatus=overCorridorBudget?NavSolveStatus.CapacityExceeded:answer.Status;
                if(effectiveStatus!=NavSolveStatus.Ready){
                    var outcome=effectiveStatus==NavSolveStatus.UnreachableInGraph?NavigationOutcome.Unreachable:NavigationOutcome.Rejected;
                    RecordFailure(r,outcome,nameWrite);MovementCancelled?.Invoke(r.Entity);
                    if(effectiveStatus==NavSolveStatus.InvalidEndpoint||effectiveStatus==NavSolveStatus.BlockedConnector||effectiveStatus==NavSolveStatus.CapacityExceeded)
                        technicalFailures[r.Entity]=effectiveStatus;
                    if(outcome==NavigationOutcome.Unreachable)UnreachableResults++;
                    CancelArrival(r.Entity,false);reservations.Remove(r.Entity);retainedGoals.Remove(r.Entity);
                    if(!Crowd.TryGet(r.Entity,out var heldFailure)||!heldFailure.Held&&!heldFailure.Moving&&!(PreserveInstalledExecution?.Invoke(r.Entity)??false))Crowd.Reject(r.Entity,outcome);
                    continue;
                }
                bool valid=Crowd.TryGet(r.Entity,out var actor)&&QualifyPreparedOrLegacy(names,answer,corridorProviders);
                var previous=valid?actor.Position:r.Start;
                foreach(var point in route){if(!r.Geometry.SegmentFree(previous,point,r.Profile.Radius)){valid=false;break;}previous=point;}
                if(valid&&(routeWrite!=null?routeWrite.ApplyNoThrow():Crowd.SetRoute(r.Entity,r.Goal,route,r.GoalLocation))){nameWrite?.ApplyNoThrow(true);AppliedResults++;ClearFailure(r.Entity);actor.InstalledGroupId=r.MemberIdentity?.GroupId??0;actor.InstalledRevision=r.MemberIdentity?.OrderRevision??0;actor.InstalledRequest=r.Request;actor.InstalledOrder=r.Order;
                    var installed=names?.TakeInstalled(answer,out _);int installedIndex=names==null?-1:Array.IndexOf(names.Answers,answer);SetInstalledExecution(r.Entity,answer.Corridor,r,installed,names,installedIndex);InstallArrival(r);
                    RouteActivated?.Invoke(r);}
                else {
                    RejectedResults++;
                    // Replan from current position if the actor moved while this request was in flight.
                    if(Crowd.TryGet(r.Entity,out var current)&&
                       (Math.Abs(current.Position.X-r.Start.X)>1e-4||Math.Abs(current.Position.Z-r.Start.Z)>1e-4)&&Move(r.Entity,r.Goal))continue;
                    RecordFailure(r,NavigationOutcome.Rejected,nameWrite);MovementCancelled?.Invoke(r.Entity);CancelArrival(r.Entity,false);reservations.Remove(r.Entity);retainedGoals.Remove(r.Entity);if(!Crowd.TryGet(r.Entity,out var heldRejected)||!heldRejected.Held&&!heldRejected.Moving&&!(PreserveInstalledExecution?.Invoke(r.Entity)??false))Crowd.Reject(r.Entity,NavigationOutcome.Rejected);
                }
                }finally{transportOwner?.Consume(r);}
            }
            PublishAdmission();
            }finally{retiredBarrier?.Release();}
        }
        private void StepMovementLocked(double dt,NavMotionWorkspace workspace)
        {
            if(HasTraffic)throw new InvalidOperationException("Traffic movement requires a complete prepared frame.");
            ValidateCompatibilityMotion(workspace);
            try{if(SuspendPendingMovement){foreach(int actor in pending.Keys)Crowd.Suspend(actor);foreach(int actor in allocating.Keys)Crowd.Suspend(actor);}PrepareMarch(dt);Crowd.Step(dt,workspace);CaptureMovementDelay(3,workspace.Motion,workspace.Proposals);}finally{Crowd.ClearPreferredMotion();}
            if(MovementDiagnosticObserver!=null){var trace=new System.Text.StringBuilder("compatibility:");for(int i=0;i<workspace.Motion.Length;i++){var body=workspace.Motion[i];var proposal=workspace.Proposals[i];trace.Append(body.Id).Append(":active=").Append(body.Active).Append(":pos=").Append(body.Position.X).Append(',').Append(body.Position.Z).Append(":target=").Append(body.Target.X).Append(',').Append(body.Target.Z).Append(":speed=").Append(body.Speed).Append(":heading=").Append(body.Heading).Append(":proposal=").Append(proposal.Position.X).Append(',').Append(proposal.Position.Z).Append(":velocity=").Append(proposal.Velocity.X).Append(',').Append(proposal.Velocity.Z).Append(":refuse=").Append(proposal.Refuse).Append(":blocked=").Append(proposal.Blocked).Append(":overflow=").Append(proposal.Overflow).Append(';');}LastOwnedMovementComputation=trace.ToString();}
            foreach(var unit in Crowd.Units)if(!unit.Moving&&!IsPending(unit.Id)){
                CancelArrival(unit.Id,true);reservations.Remove(unit.Id);retainedGoals.Remove(unit.Id);
            }
            PublishAdmission();
        }
        // Execution policy belongs to the saved authority envelope. Suspend without
        // changing mobility revision/order, so the in-flight certificate stays current.
        internal bool SuspendPendingMovement {get;set;}
        public void Step(double dt)
        {
            LastOwnedMovementNullReason=0;LastOwnedMovementNullDetail=null;LastOwnedMovementNullActor=0;LastOwnedMovementNullGroup=0;LastOwnedMovementComputation=null;LastTrafficDispatchStage=0;LastNavigationStepPath=0;
            try{
                if(dt<=0||double.IsNaN(dt)||double.IsInfinity(dt)){LastNavigationStepPath=1;throw new ArgumentException("Invalid step duration.");}
                var movement=TryPublishOwnedMovement(dt);if(movement.HasValue){LastNavigationStepPath=movement.Value?2:3;return;}
                var traffic=TryDispatchPreparedTraffic(dt);if(traffic.HasValue){LastNavigationStepPath=traffic.Value?4:5;return;}
                NavMotionWorkspace workspace;
                try{workspace=CreateCompatibilityMotionWorkspace(Crowd);}catch(InvalidOperationException){LastNavigationStepPath=6;return;}
                using(workspace){
                    using(EnterTransport()){if(!workspace.SourceCurrent(Crowd)){LastNavigationStepPath=7;return;}}
                    bool arrivalPrefix=PrepareOwnedReadyArrivals();if(!TryPrepareAnswerNamesBatch(out var names,false,true)){LastNavigationStepPath=8;return;}
                    bool compatibilityBatch=false;
                    using(names){
                        var owned=TryPublishOwnedResultBatch(names,dt,!arrivalPrefix,motionWorkspace:workspace)??TryPublishOwnedProbeAnswer(names,dt,workspace)??TryPublishOwnedDiscard(names,dt,workspace)??TryPublishOwnedOrdinaryFailure(names,dt,workspace)??TryPublishOwnedReady(names,dt,workspace);
                        if(owned.HasValue){LastNavigationStepPath=owned.Value?9:10;if(owned.Value)RefreshOwnedGroupMembers();return;}
                        compatibilityBatch=liveProbeAnswers!=null&&names!=null&&names.Answers.Length>1;
                        if(!compatibilityBatch&&!PrepareCompatibilityInstallDestinations(names)){LastNavigationStepPath=11;return;}
                        if(!compatibilityBatch)using(EnterTransport()){
                            if(!AnswerNamesSourceCurrent(names)||!workspace.SourceCurrent(Crowd)){LastNavigationStepPath=12;return;}
                            AdmissionBatch(()=>{if(names!=null&&names.Answers.Length>0)ApplyResultsLocked(names);StepMovementLocked(dt,workspace);LastNavigationStepPath=13;});
                        }
                    }
                    if(compatibilityBatch){
                        using(EnterTransport()){if(!workspace.SourceCurrent(Crowd)){LastNavigationStepPath=14;return;}}
                        if(!TryApplyResultsPrepared()){LastNavigationStepPath=15;return;}
                        if(!TryPrepareAnswerNames(out names)){LastNavigationStepPath=16;return;}
                        using(names)using(EnterTransport()){
                            if(!AnswerNamesSourceCurrent(names)||!workspace.SourceCurrent(Crowd)){LastNavigationStepPath=17;return;}
                            AdmissionBatch(()=>{if(names!=null&&names.Answers.Length>0)ApplyResultsLocked(names);StepMovementLocked(dt,workspace);LastNavigationStepPath=18;});
                        }
                    }
                    RefreshOwnedGroupMembers();
                    if(LastNavigationStepPath==0)LastNavigationStepPath=19;
                }
            }finally{var observer=MovementDiagnosticObserver;if(observer!=null)try{observer(this);}catch{/* Diagnostic observers cannot affect authority decisions. */}}
        }
    }
}

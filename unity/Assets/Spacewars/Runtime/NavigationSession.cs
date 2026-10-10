using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
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
    [Serializable] public sealed class NavigationSessionState
    {
        // 0 is the old 256-request/implicit-status wire; 1 is the explicit
        // bounded 4096-request/status extension carried by GroupOrderWire v5.
        public int TransportVersion;
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
        public int RejectedResults, AppliedResults, UnreachableResults;
        public int[][] CollectionLayouts;
    }
    public sealed class NavigationRequest
    {
        public NavigationRequest(long session,long request,int entity,long order,NavigationProfile profile,NavGeometry geometry,NavPoint start,NavPoint goal,NavGeometry baseGeometry=null,string heldIdentity=null,NavigationMemberIdentity memberIdentity=null,NavLocation? startLocation=null,NavLocation? goalLocation=null)
        { StartLocation=startLocation;GoalLocation=goalLocation;MemberIdentity=memberIdentity;BaseGeometry=baseGeometry??geometry;HeldIdentity=heldIdentity;Session=session;Request=request;Entity=entity;Order=order;Profile=profile;ProfileId=profile.ProfileId;ProfileRevision=profile.Revision;Geometry=geometry;Start=start;Goal=goal; }
        public NavLocation? StartLocation{get;} public NavLocation? GoalLocation{get;}
        internal string CertifiedProviderBinding{get;set;}
        public NavigationMemberIdentity MemberIdentity{get;}
        public NavGeometry BaseGeometry{get;} public string HeldIdentity{get;}
        public long Session { get; } public long Request { get; } public int Entity { get; } public long Order { get; }
        public NavigationProfile Profile{get;} public string ProfileId { get; } public int ProfileRevision { get; } public NavGeometry Geometry { get; }
        public NavPoint Start { get; } public NavPoint Goal { get; }
    }
    public sealed class NavigationAnswer
    {
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
        public NavPoint[] CopyRoute(){return (NavPoint[])route.Clone();}
    }
    // Owned by one domain worker. Mailboxes and registered handoff are the
    // bounded cross-thread surfaces.
    public sealed partial class NavigationSession
    {
        private readonly Dictionary<int,NavigationInstalledExecutionState> installedExecutions=
            new Dictionary<int,NavigationInstalledExecutionState>();
        private readonly HashSet<NavigationAnswer> countedCorridorAnswers=new HashSet<NavigationAnswer>();
        internal string LastCorridorFailure{get;private set;}
        private bool CorridorFailure(string reason){LastCorridorFailure=reason;return false;}
        private long installedCorridorBytes,installedGeometryBytes,answerCorridorBytes,hostRetainedBytes;
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
                if(outer)System.Threading.Interlocked.Add(ref timing.Hold,System.Diagnostics.Stopwatch.GetTimestamp()-entered);
                System.Threading.Monitor.Exit(session.transportGate);
            }
        }
        private TransportScope EnterTransport()=>new TransportScope(this);
        internal event Action HostChanged;
        internal void SignalHost()=>HostChanged?.Invoke();
        internal long BeginHostAllocation()
        {
            using(EnterTransport()){
                if(hostAllocationThread!=0)throw new InvalidOperationException("Concurrent route kernels.");
                hostAllocationThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
                return installedCorridorBytes+installedGeometryBytes+answerCorridorBytes;
            }
        }
        internal void EndHostAllocation(long bytes)
        {
            using(EnterTransport()){
                if(hostAllocationThread!=System.Threading.Thread.CurrentThread.ManagedThreadId)
                    throw new InvalidOperationException("Route reservation owner mismatch.");
                bool over=bytes<0||bytes+installedCorridorBytes+installedGeometryBytes+answerCorridorBytes>LayeredNavigationScheduler.MaxRetainedBytes;
                hostRetainedBytes=Math.Max(0,bytes);hostAllocationThread=0;
                if(over)throw new InvalidOperationException("Route retention cap exceeded.");
            }
        }
        internal long CorridorRetainedBytes{get{using(EnterTransport())return installedCorridorBytes+installedGeometryBytes+answerCorridorBytes;}}
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
        {if(answer.Corridor!=null&&countedCorridorAnswers.Add(answer))answerCorridorBytes+=answer.Corridor.EstimatedRetainedBytes;}
        private void ReleaseCorridorAnswer(NavigationAnswer answer)
        {if(countedCorridorAnswers.Remove(answer))answerCorridorBytes-=answer.Corridor.EstimatedRetainedBytes;}
        private void SetInstalledExecution(int entity,NavCorridorDescriptor descriptor,NavigationRequest request=null)
        {
            if(installedExecutions.TryGetValue(entity,out var old))installedCorridorBytes-=old.Corridor.EstimatedRetainedBytes+16L*old.InstalledRoute.Length;
            if(descriptor==null){installedExecutions.Remove(entity);ResetMarchProgress(entity);RecountInstalledGeometryBytes();return;}
            if(request==null)throw new ArgumentException("Missing historical corridor request.");
            if(!Crowd.TryGet(entity,out var actor))throw new InvalidOperationException("Missing installed corridor actor.");
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
            if(state.Detached||
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
        {if(installedExecutions.TryGetValue(entity,out var state))state.Detached=true;}
        private void RestoreInstalledExecutions(NavigationSessionState state,NavGeometry[] geometryTable)
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
                    row.InstalledRoute==null||row.GeometryIndex<0||row.GeometryIndex>=geometryTable.Length||
                    row.BaseGeometryIndex!=state.NavigationGeometryIndex||
                    !descriptor.ProjectedRoute().Concat(new[]{descriptor.Endpoint.Position}).SequenceEqual(row.InstalledRoute)||
                    descriptor.Entity!=row.Entity||descriptor.World!=Generation||
                    descriptor.Request!=actor.InstalledRequest||descriptor.Order!=actor.InstalledOrder||
                    descriptor.Group!=actor.InstalledGroupId||descriptor.ActivationRevision!=actor.InstalledRevision||
                    descriptor.Radius!=actor.Radius||descriptor.Provenance==NavRouteProvenance.ProducerProbe||
                    descriptor.TopologyRevision!=geometryTable[row.BaseGeometryIndex].Revision||
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
                installedExecutions.Add(row.Entity,new NavigationInstalledExecutionState{
                    Entity=row.Entity,Detached=row.Detached,Corridor=descriptor,
                    InstalledRoute=(NavPoint[])row.InstalledRoute.Clone(),Profile=historicalProfile,
                    MovementGeometry=historicalGeometry,BaseGeometry=historicalBase,HeldIdentity=row.HeldIdentity});
                installedCorridorBytes+=descriptor.EstimatedRetainedBytes+16L*row.InstalledRoute.Length;
            }
            RecountInstalledGeometryBytes();
            if(CorridorRetainedBytes>LayeredNavigationScheduler.MaxRetainedBytes)
                throw new ArgumentException("Restored corridor retention exceeds capacity.");
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
        private bool QualifyCorridor(NavigationRequest request,NavPoint[] route,NavCorridorDescriptor corridor,
            Dictionary<Tuple<NavGeometry,string>,ILayeredNavigationProvider> cache,bool current)
        {
            if(corridor==null)return request.CertifiedProviderBinding==null||
                CorridorFailure("missing certified sidecar"); // Explicit legacy solver fallback.
            LastCorridorFailure=null;
            if(!current&&request.Session!=Generation){
                var oldIdentity=request.MemberIdentity;
                if(corridor.World!=request.Session||corridor.Request!=request.Request||
                    corridor.Entity!=request.Entity||corridor.Order!=request.Order||
                    corridor.Group!=(oldIdentity?.GroupId??0)||
                    corridor.ProfileBinding!=NavigationAdmission.ExactProfileBinding(request.Profile)||
                    corridor.Radius!=request.Profile.Radius||corridor.GridCell!=request.Profile.GridCell||
                    corridor.HoldBinding!=request.HeldIdentity||
                    corridor.GraphKey!=new NavGraphBinding(corridor.ProviderBinding,
                        new NavClearanceProfile("ground",1,"ground",(int)(oldIdentity?.MobilityRevision??0),corridor.Radius),
                        new NavGraphProfile(corridor.ProfileBinding,"distance",1,corridor.GridCell)).StableKey||
                    !corridor.ProjectedRoute().SequenceEqual(route))return CorridorFailure("stale historical binding");
                return true;
            }
            var identity=request.MemberIdentity;
            var provider=CorridorProvider(request,cache);
            var clearance=new NavClearanceProfile("ground",1,"ground",(int)(identity?.MobilityRevision??0),request.Profile.Radius);
            var origin=request.StartLocation??(provider is PartitionNavigationProvider partitionOrigin&&
                partitionOrigin.TryLocate(request.Start,clearance,out var locatedOrigin)
                ?locatedOrigin:new NavLocation(request.Start,NavLocation.FlatSurface));
            var endpoint=request.GoalLocation??(provider is PartitionNavigationProvider partitionGoal&&
                partitionGoal.TryLocate(request.Goal,clearance,out var locatedGoal)
                ?locatedGoal:new NavLocation(request.Goal,NavLocation.FlatSurface));
            var expectedProvenance=request.Entity<0?NavRouteProvenance.ProducerProbe:
                identity!=null&&identity.GroupId!=0?NavRouteProvenance.CommandMember:NavRouteProvenance.LegacySingleton;
            if(corridor.World!=Generation||corridor.Entity!=request.Entity||corridor.Request!=request.Request||
                corridor.Order!=request.Order||corridor.Group!=(identity?.GroupId??0)||
                corridor.Provenance!=expectedProvenance||
                corridor.ActivationRevision!=(identity?.OrderRevision??0)||
                corridor.Incarnation!=(identity?.Incarnation??0)||corridor.Mobility!=(identity?.MobilityRevision??0)||
                corridor.TopologyRevision!=request.BaseGeometry.Revision)
                return CorridorFailure("identity/topology");
            if(
                corridor.ProfileBinding!=NavigationAdmission.ExactProfileBinding(request.Profile)||
                corridor.Radius!=request.Profile.Radius||corridor.GridCell!=request.Profile.GridCell||
                corridor.HoldBinding!=request.HeldIdentity||
                !corridor.Origin.Equals(origin)||!corridor.Endpoint.Equals(endpoint)||
                corridor.SurfaceProviderId!=(terrain?.Id??NavLocation.FlatSurface))
                return CorridorFailure("profile/endpoints");
            if(corridor.ProviderBinding!=provider.Binding)return CorridorFailure("provider");
            if(
                corridor.GraphKey!=new NavGraphBinding(provider.Binding,clearance,
                    new NavGraphProfile(corridor.ProfileBinding,"distance",1,request.Profile.GridCell)).StableKey)
                return CorridorFailure("graph");
            if(!corridor.ProjectedRoute().SequenceEqual(route))return CorridorFailure("XZ projection");
            if(current&&corridor.Group!=0&&
                (Member(request.Entity)==null||!groups.TryGetValue(corridor.Group,out var group)||
                corridor.RootRevision!=group.OrderRevision||corridor.CommandSequence!=group.CommandSequence||
                corridor.IssuedTick!=group.IssuedTick))return CorridorFailure("current root");
            NavLocation at=corridor.Origin;
            foreach(var leg in corridor.Legs){
                if(!leg.From.Equals(at)||leg.Section!=NavCorridorSection.IndividualConnector||
                    leg.Branch!=0||leg.DownstreamMergeLeg!=0)return CorridorFailure("leg shape");
                if(leg.Kind==NavTypedLegKind.Surface){
                    double cost=LayeredNavigationProvider.Distance(leg.From.Position,leg.To.Position)*
                        provider.SurfaceCost(leg.From.SurfaceId);
                    bool surfaceValid=provider is PartitionNavigationProvider
                        ?provider.IsValid(leg.From,clearance)&&provider.IsValid(leg.To,clearance)&&
                            request.Geometry.SegmentFree(leg.From.Position,leg.To.Position,clearance.Radius)
                        :provider.CanSweep(leg.From,leg.To,clearance);
                    if(!surfaceValid||Math.Abs(leg.Cost-cost)>1e-7)return CorridorFailure("surface clearance/cost");
                }else if(!leg.From.Position.Equals(leg.To.Position)||leg.Cost!=0||
                    !(provider is PartitionNavigationProvider partition)||
                    !partition.Seams.Any(s=>s.Allowed&&s.Id==leg.PortalId&&
                        ((s.FromSurface==leg.From.SurfaceId&&s.ToSurface==leg.To.SurfaceId)||
                         (s.ToSurface==leg.From.SurfaceId&&s.FromSurface==leg.To.SurfaceId))&&
                        OnCorridorSeam(leg.From.Position,s.From,s.To)))return CorridorFailure("portal seam");
                at=leg.To;
            }
            if(!at.Equals(corridor.Endpoint))return CorridorFailure("terminal continuity");
            if(provider is PartitionNavigationProvider actualProvider){
                at=corridor.Origin;var observed=new List<string>();
                foreach(var point in route){
                    if(!actualProvider.TryTrace(at,point,clearance,out var reached,out var trace))
                        return CorridorFailure("XZ replay trace");
                    observed.AddRange(trace.Where(x=>x.Kind==NavTypedLegKind.Portal).Select(x=>x.PortalId));
                    at=reached;
                }
                if(!at.Equals(corridor.Endpoint)||!observed.SequenceEqual(
                    corridor.Legs.Where(x=>x.Kind==NavTypedLegKind.Portal).Select(x=>x.PortalId)))
                    return CorridorFailure("XZ portal sequence");
            }
            return true;
        }
        public const int RouteRequestCapacity=4096;
        private readonly object transportGate = new object();
        private readonly List<NavigationRequest> registeredRoutes = new List<NavigationRequest>();
        private readonly Dictionary<NavigationRequest,string> registeredGraphKeys=
            new Dictionary<NavigationRequest,string>();
        private int transferCallbackThread;
        private void RejectTransferCallbackReentry()
        {if(transferCallbackThread==System.Threading.Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Route registration cannot reenter navigation authority.");}
        private int admissionBatchDepth;
        private T AdmissionBatch<T>(Func<T> work)
        {
            using(EnterTransport()){RejectTransferCallbackReentry();admissionBatchDepth++;bool committed=false;
                try{var result=work();committed=true;return result;}
                finally{admissionBatchDepth--;if(committed&&admissionBatchDepth==0)PublishAdmission();}}
        }
        private void AdmissionBatch(Action work)
        {
            using(EnterTransport()){RejectTransferCallbackReentry();admissionBatchDepth++;bool committed=false;
                try{work();committed=true;}
                finally{admissionBatchDepth--;if(committed&&admissionBatchDepth==0)PublishAdmission();}}
        }
        // Scheduler admission and mailbox removal are one capture-visible action.
        // A failed admission leaves the original request at the mailbox head.
        internal bool TryTransferRoute(Func<NavigationRequest,bool> register, out NavigationRequest request)
        {
            if(register==null)throw new ArgumentNullException(nameof(register));
            using(EnterTransport()){RejectTransferCallbackReentry();if(commandGroup!=null||admissionBatchDepth!=0){request=null;return false;}return Requests.TryTransfer(candidate=>{
                if(!Admission.Contains(candidate))return false;
                registeredRoutes.Add(candidate);
                transferCallbackThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
                try { if(register(candidate))return true; }
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
            return TryTransferRouteToScheduler(scheduler,(candidate,admission)=>prepare(candidate),out request);
        }
        internal bool TryTransferRouteToScheduler(LayeredNavigationScheduler scheduler,
            Func<NavigationRequest,NavigationAdmission,NavScheduledRequest> prepare, out NavigationRequest request)
        {
            if(scheduler==null)throw new ArgumentNullException(nameof(scheduler));
            if(prepare==null)throw new ArgumentNullException(nameof(prepare));
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
                if(commandGroup!=null||admissionBatchDepth!=0||!ReferenceEquals(Admission,observed)||!Admission.Contains(candidate)){request=null;return false;}
                return Requests.TryTransfer(head=>{
                    if(!ReferenceEquals(head,candidate))return false;
                    registeredRoutes.Add(candidate);
                    try{if(scheduler.Submit(scheduled)){
                        candidate.CertifiedProviderBinding=scheduled.Provider.Binding;
                        registeredGraphKeys.Add(candidate,scheduled.GraphKey);return true;}}
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
                    terrain!=null&&!(terrain is ThreeCrossingsMap)&&!(terrain is FoundryMap)||
                    (terrain is ThreeCrossingsMap||terrain is FoundryMap)&&
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
        public int RegisteredRouteCount { get { using(EnterTransport())return registeredRoutes.Count; } }
        private readonly AuthoritySlotMap<int,NavigationRequest> pending=new AuthoritySlotMap<int,NavigationRequest>();
        private readonly AuthoritySlotMap<int,long> orders=new AuthoritySlotMap<int,long>();
        private readonly AuthoritySlotMap<int,NavPoint> reservations=new AuthoritySlotMap<int,NavPoint>();
        private readonly Dictionary<int,NavSolveStatus> technicalFailures=new Dictionary<int,NavSolveStatus>();
        // Latest intent is deliberately retained while a replacement is planned. A
        // geometry revision reissues it from the current authoritative position.
        private readonly AuthoritySlotMap<int,NavPoint> retainedGoals=new AuthoritySlotMap<int,NavPoint>();
        // Negative entity IDs are producer topology probes, never crowd actors.
        private readonly AuthoritySlotMap<int,NavigationRequest> probes=new AuthoritySlotMap<int,NavigationRequest>();
        private readonly Queue<NavigationAnswer> probeAnswers=new Queue<NavigationAnswer>();
        private readonly List<NavigationAnswer> barrierAnswers=new List<NavigationAnswer>();
        public NavigationRequest Probe(int producer,long order,NavigationProfile typed,NavPoint start,NavPoint goal)
        {
            using(EnterTransport())return ProbeLocked(producer,order,typed,start,goal);
        }
        private NavigationRequest ProbeLocked(int producer,long order,NavigationProfile typed,NavPoint start,NavPoint goal)
        {
            var request=new NavigationRequest(Generation,++requestSequence,-producer,order,typed,navigationGeometry,start,goal);
            if(Requests.Count+RegisteredRouteCount>=RouteRequestCapacity||!Requests.TryEnqueue(request))return null;
            probes[-producer]=request;PublishAdmission();return request;
        }
        public void CancelProbe(int producer){using(EnterTransport()){probes.Remove(-producer);PublishAdmission();}}
        public bool TryProbeAnswer(out NavigationAnswer answer)
        {using(EnterTransport()){if(probeAnswers.Count==0){answer=null;return false;}
            answer=probeAnswers.Dequeue();ReleaseCorridorAnswer(answer);return true;}}
        private long requestSequence;
        private readonly IPlayableTerrain terrain;
        private NavGeometry geometry;
        private NavGeometry navigationGeometry;
        private NavigationProfile profile;
        public NavigationSession(long generation,NavGeometry geometry,NavigationProfile profile,NavGeometry navigationGeometry=null,IPlayableTerrain terrain=null){if(generation<1)throw new ArgumentOutOfRangeException("generation");this.terrain=terrain;Generation=generation;this.geometry=geometry;this.navigationGeometry=navigationGeometry??geometry;this.profile=profile;Crowd=new NavCrowd(geometry,profile,terrain);PublishAdmission();}
        public NavigationRoutePort RoutePort => routePort ?? (routePort = new NavigationRoutePort(this));
        private NavigationRoutePort routePort;
        public long Generation {get;}
        public NavCrowd Crowd {get;}
        public NavMailbox<NavigationRequest> Requests {get;} = new NavMailbox<NavigationRequest>(RouteRequestCapacity);
        public NavMailbox<NavigationAnswer> Answers {get;} = new NavMailbox<NavigationAnswer>();
        public int RejectedResults {get;private set;}
        public int AppliedResults {get;private set;}
        public NavSolveStatus? TechnicalFailureFor(int entity)
            =>technicalFailures.TryGetValue(entity,out var value)?(NavSolveStatus?)value:null;
        public int PendingCount => pending.Count+probes.Count;
        public bool IsPending(int id) { return pending.ContainsKey(id); }
        internal NavigationRequest PendingRequestFor(int id)=>pending.TryGetValue(id,out var request)?request:null;
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
            var result=new double[NavigationProfile.Metadata.Count];
            for(int i=0;i<result.Length;i++)result[i]=NavigationProfile.Metadata[i].Read(value);return result;
        }
        private static NavigationProfile LoadProfile(NavigationRequestState saved)
        {
            var v=saved.ProfileValues;
            if(v==null||v.Length!=NavigationProfile.Metadata.Count)throw new ArgumentException("Invalid request profile values.");
            return new NavigationProfile(saved.ProfileId,saved.ProfileRevision,v[7],v[8],v[9],v[10],v[11],v[12],v[13],v[0],v[1],v[2],v[3],v[4],v[5],v[6]);
        }
        private static bool ValidSavedAnswer(NavigationAnswerState answer)
            =>answer.Route!=null&&Enum.IsDefined(typeof(NavSolveStatus),answer.Status)&&
                answer.Status!=NavSolveStatus.Pending&&
                (answer.Status==NavSolveStatus.Ready)==(answer.Route.Length>0);
        public NavigationSessionState CaptureState()
        {
            using(EnterTransport()){RejectTransferCallbackReentry();
                if(commandGroup!=null||admissionBatchDepth!=0)throw new InvalidOperationException("Cannot capture an open navigation admission batch.");
                return CaptureStateLocked();}
        }
        private NavigationSessionState CaptureStateLocked()
        {
            // Authority is at the frozen routing barrier; registered transport
            // transitions are blocked by transportGate for this checkpoint.
            PublishAdmission();
            // Registered inputs replay through the old request mailbox. The
            // original request sequence is the stable producer FIFO order.
            var queuedRequests=registeredRoutes.Concat(Requests.CopyItems()).OrderBy(r=>r.Request).ToArray();
            if(queuedRequests.Length>RouteRequestCapacity)
                throw new InvalidOperationException("Registered route replay exceeds checkpoint mailbox capacity.");
            var queuedAnswers=Answers.CopyItems();
            var accounted=new HashSet<NavigationRequest>(queuedRequests);
            foreach(var answer in queuedAnswers)if(answer!=null)accounted.Add(answer.Request);
            foreach(var answer in barrierAnswers)accounted.Add(answer.Request);
            foreach(var pair in probes)if(!accounted.Contains(pair.Value))throw new InvalidOperationException("A rally probe is held outside the quiescent mailboxes.");
            foreach(var pair in pending)if(!accounted.Contains(pair.Value))
                throw new InvalidOperationException("A pending navigation request is held outside the quiescent mailboxes.");
            var all=new List<NavigationRequest>();var indices=new Dictionary<NavigationRequest,int>();
            Action<NavigationRequest> include=r=>{if(r==null)throw new InvalidOperationException("Null navigation request in checkpoint.");if(!indices.ContainsKey(r)){indices[r]=all.Count;all.Add(r);}};
            foreach(var pair in pending)include(pair.Value);
            foreach(var pair in probes)include(pair.Value);
            foreach(var answer in probeAnswers)include(answer.Request);
            foreach(var request in queuedRequests)include(request);
            foreach(var answer in barrierAnswers)include(answer.Request);
            foreach(var answer in queuedAnswers){if(answer==null)throw new InvalidOperationException("Null navigation answer in checkpoint.");include(answer.Request);}
            var geometries=new List<NavGeometry>{geometry};var geometryIndices=new Dictionary<NavGeometry,int>{{geometry,0}};
            Func<NavGeometry,int> geometryIndex=g=>{if(!geometryIndices.TryGetValue(g,out var index)){index=geometries.Count;geometryIndices.Add(g,index);geometries.Add(g);}return index;};
            int baseIndex=geometryIndex(navigationGeometry);
            var state=new NavigationSessionState{TransportVersion=1,CorridorWireVersion=1,InstalledExecutions=installedExecutions.Values.OrderBy(x=>x.Entity).Select(x=>new NavigationInstalledExecutionState{Entity=x.Entity,Detached=x.Detached,Corridor=x.Corridor,InstalledRoute=(NavPoint[])x.InstalledRoute.Clone(),ProfileId=x.Profile.ProfileId,ProfileRevision=x.Profile.Revision,ProfileValues=SaveProfile(x.Profile),HeldIdentity=x.HeldIdentity,GeometryIndex=geometryIndex(x.MovementGeometry),BaseGeometryIndex=geometryIndex(x.BaseGeometry)}).ToArray(),SurfaceSemanticsVersion=terrain?.SurfaceSemanticsVersion??1,SurfaceProviderId=terrain?.Id??NavLocation.FlatSurface,NavigationGeometryIndex=baseIndex,Generation=Generation,RequestSequence=requestSequence,GroupSequence=groupSequence,Groups=GroupOrders.ToArray(),GroupLayoutVersion=1,GroupKeys=groups.Keys.ToArray(),MemberGroupKeys=memberGroups.Keys.ToArray(),GroupLayouts=new[]{groups.CaptureLayout(),memberGroups.CaptureLayout()},Geometry=SaveGeometry(geometry),NavigationGeometry=SaveGeometry(navigationGeometry),
                Crowd=Crowd.CaptureState(),Requests=new NavigationRequestState[all.Count],Orders=new NavigationEntityOrderState[orders.Count],
                Reservations=new NavigationEntityPointState[reservations.Count],RetainedGoals=new NavigationEntityPointState[retainedGoals.Count],
                MovementIdentities=new NavigationMovementIdentityState[movementIdentities.Count],ProbeRequestIndices=new int[probes.Count],ProbeAnswers=new NavigationAnswerState[probeAnswers.Count],
                PendingRequestIndices=new int[pending.Count],RequestMailboxIndices=new int[queuedRequests.Length],AnswerMailbox=new NavigationAnswerState[queuedAnswers.Length],
                RejectedResults=RejectedResults,AppliedResults=AppliedResults,UnreachableResults=UnreachableResults,BarrierAnswers=new NavigationAnswerState[barrierAnswers.Count],
                TechnicalFailures=technicalFailures.OrderBy(x=>x.Key).Select(x=>new NavigationTechnicalFailureState{
                    Entity=x.Key,Request=Crowd.Units.First(u=>u.Id==x.Key).FailedRequest,Status=x.Value}).ToArray()};
            for(int i=0;i<all.Count;i++){
                var r=all[i];state.Requests[i]=new NavigationRequestState{Session=r.Session,Request=r.Request,Entity=r.Entity,Order=r.Order,MemberIdentity=r.MemberIdentity,
                    Start=r.Start,Goal=r.Goal,StartLocation=r.StartLocation,GoalLocation=r.GoalLocation,ProfileId=r.ProfileId,ProfileRevision=r.ProfileRevision,ProfileValues=SaveProfile(r.Profile),CertifiedProviderBinding=r.CertifiedProviderBinding,
                    Geometry=SaveGeometry(r.Geometry),BaseGeometry=SaveGeometry(r.BaseGeometry),GeometryIndex=geometryIndex(r.Geometry),BaseGeometryIndex=geometryIndex(r.BaseGeometry),HeldIdentity=r.HeldIdentity,UsesCurrentGeometry=Object.ReferenceEquals(r.Geometry,navigationGeometry),UsesCurrentBaseGeometry=Object.ReferenceEquals(r.BaseGeometry,navigationGeometry)};
            }
            int n=0;foreach(var pair in orders)state.Orders[n++]=new NavigationEntityOrderState{Entity=pair.Key,Order=pair.Value};
            n=0;foreach(var pair in reservations)state.Reservations[n++]=new NavigationEntityPointState{Entity=pair.Key,Point=pair.Value};
            n=0;foreach(var pair in retainedGoals)state.RetainedGoals[n++]=new NavigationEntityPointState{Entity=pair.Key,Point=pair.Value};
            n=0;foreach(var pair in pending)state.PendingRequestIndices[n++]=indices[pair.Value];
            n=0;foreach(var pair in probes)state.ProbeRequestIndices[n++]=indices[pair.Value];
            n=0;foreach(var pair in movementIdentities)state.MovementIdentities[n++]=new NavigationMovementIdentityState{Entity=pair.Key,Identity=pair.Value};
            n=0;foreach(var answer in probeAnswers)state.ProbeAnswers[n++]=new NavigationAnswerState{RequestIndex=indices[answer.Request],Route=answer.CopyRoute(),Status=answer.Status,Corridor=answer.Corridor};
            for(int i=0;i<queuedRequests.Length;i++)state.RequestMailboxIndices[i]=indices[queuedRequests[i]];
            for(int i=0;i<queuedAnswers.Length;i++)state.AnswerMailbox[i]=new NavigationAnswerState{RequestIndex=indices[queuedAnswers[i].Request],Route=queuedAnswers[i].CopyRoute(),Status=queuedAnswers[i].Status,Corridor=queuedAnswers[i].Corridor};
            for(int i=0;i<barrierAnswers.Count;i++)state.BarrierAnswers[i]=new NavigationAnswerState{RequestIndex=indices[barrierAnswers[i].Request],Route=barrierAnswers[i].CopyRoute(),Status=barrierAnswers[i].Status,Corridor=barrierAnswers[i].Corridor};
            state.CollectionLayouts=new[]{pending.CaptureLayout(),orders.CaptureLayout(),reservations.CaptureLayout(),retainedGoals.CaptureLayout(),probes.CaptureLayout(),movementIdentities.CaptureLayout()};
            state.GeometryTable=geometries.ConvertAll(SaveGeometry).ToArray();return state;
        }
        public void RestoreState(NavigationSessionState state)=>RestoreWorldState(state);
        internal NavigationRequest[] RestoreWorldState(NavigationSessionState state)
        {
            if(state==null||state.Generation!=Generation||state.RequestSequence<0||state.Requests==null||state.Orders==null||
                state.Reservations==null||state.RetainedGoals==null||state.PendingRequestIndices==null||state.RequestMailboxIndices==null||state.AnswerMailbox==null||
                (state.TransportVersion!=0&&state.TransportVersion!=1)||state.RequestMailboxIndices.Length>(state.TransportVersion==0?NavMailbox<NavigationRequest>.Capacity:RouteRequestCapacity)||state.AnswerMailbox.Length>NavMailbox<NavigationAnswer>.Capacity||
                state.RejectedResults<0||state.AppliedResults<0||state.UnreachableResults<0||pending.Count!=0||orders.Count!=0||reservations.Count!=0||retainedGoals.Count!=0||
                Requests.Count!=0||Answers.Count!=0||Crowd.Units.Count!=0||!SameGeometry(state.Geometry,geometry)||!SameGeometry(state.NavigationGeometry,navigationGeometry))
                throw new ArgumentException("Navigation session checkpoint or destination mismatch.",nameof(state));
            if(state.GeometryTable==null||state.GeometryTable.Length<1||state.NavigationGeometryIndex<0||state.NavigationGeometryIndex>=state.GeometryTable.Length||!SameGeometry(state.GeometryTable[0],geometry)||!SameGeometry(state.GeometryTable[state.NavigationGeometryIndex],navigationGeometry)||(state.NavigationGeometryIndex==0)!=Object.ReferenceEquals(geometry,navigationGeometry))throw new ArgumentException("Invalid geometry identity table.");
            ValidateSurfaceState(state);
            var geometryTable=new NavGeometry[state.GeometryTable.Length];for(int i=0;i<geometryTable.Length;i++)geometryTable[i]=i==0?geometry:i==state.NavigationGeometryIndex?navigationGeometry:LoadGeometry(state.GeometryTable[i]);
            var restored=new NavigationRequest[state.Requests.Length];long maxRequest=0;var requestIds=new HashSet<string>();
            for(int i=0;i<restored.Length;i++){
                var row=state.Requests[i];if(row==null||row.Session<1||row.Request<1||
                    (row.Entity<0?row.Order<0:row.Order<1)||row.Geometry==null||row.Geometry.Obstacles==null)
                    throw new ArgumentException("Invalid navigation request checkpoint.",nameof(state));
                if(row.GeometryIndex<0||row.GeometryIndex>=geometryTable.Length||row.BaseGeometryIndex<0||row.BaseGeometryIndex>=geometryTable.Length||!requestIds.Add(row.Session+":"+row.Request))throw new ArgumentException("Invalid request identity.");
                var requestProfile=LoadProfile(row);var requestGeometry=geometryTable[row.GeometryIndex];
                if(!SameGeometry(row.Geometry,requestGeometry)||!SameGeometry(row.BaseGeometry,geometryTable[row.BaseGeometryIndex])||row.UsesCurrentGeometry!=(row.GeometryIndex==state.NavigationGeometryIndex)||row.UsesCurrentBaseGeometry!=(row.BaseGeometryIndex==state.NavigationGeometryIndex))throw new ArgumentException("Invalid request geometry identity.");
                if(row.BaseGeometry==null||row.UsesCurrentBaseGeometry&&!SameGeometry(row.BaseGeometry,navigationGeometry)||row.UsesCurrentGeometry&&!SameGeometry(row.Geometry,navigationGeometry))throw new ArgumentException("Request geometry mismatch.",nameof(state));
                restored[i]=new NavigationRequest(row.Session,row.Request,row.Entity,row.Order,requestProfile,requestGeometry,row.Start,row.Goal,geometryTable[row.BaseGeometryIndex],row.HeldIdentity,row.MemberIdentity,row.StartLocation,row.GoalLocation);
                restored[i].CertifiedProviderBinding=row.CertifiedProviderBinding;
                if(row.Session==Generation)maxRequest=Math.Max(maxRequest,row.Request);
            }
            if(maxRequest>state.RequestSequence)throw new ArgumentException("Request sequence mismatch.",nameof(state));
            var nextOrders=new Dictionary<int,long>();foreach(var row in state.Orders){if(row==null||row.Order<0||nextOrders.ContainsKey(row.Entity))throw new ArgumentException("Invalid order checkpoint.",nameof(state));nextOrders.Add(row.Entity,row.Order);}
            var nextPending=new Dictionary<int,NavigationRequest>();foreach(int index in state.PendingRequestIndices){
                if(index<0||index>=restored.Length)throw new ArgumentException("Invalid pending request index.",nameof(state));
                var r=restored[index];if(nextPending.ContainsKey(r.Entity)||r.Session!=Generation||r.ProfileId!=profile.ProfileId||r.ProfileRevision!=profile.Revision||
                    !Object.ReferenceEquals(r.BaseGeometry,navigationGeometry)||!nextOrders.TryGetValue(r.Entity,out var order)||order!=r.Order)
                    throw new ArgumentException("Invalid pending request binding.",nameof(state));nextPending.Add(r.Entity,r);
            }
            var nextReservations=new Dictionary<int,NavPoint>();foreach(var row in state.Reservations){if(row==null||nextReservations.ContainsKey(row.Entity))throw new ArgumentException("Invalid reservation checkpoint.",nameof(state));nextReservations.Add(row.Entity,row.Point);}
            var nextGoals=new Dictionary<int,NavPoint>();foreach(var row in state.RetainedGoals){if(row==null||nextGoals.ContainsKey(row.Entity))throw new ArgumentException("Invalid retained goal checkpoint.",nameof(state));nextGoals.Add(row.Entity,row.Point);}
            foreach(int index in state.RequestMailboxIndices)if(index<0||index>=restored.Length)throw new ArgumentException("Invalid request mailbox index.",nameof(state));
            foreach(var answer in state.AnswerMailbox)if(answer==null||answer.RequestIndex<0||answer.RequestIndex>=restored.Length||!ValidSavedAnswer(answer))throw new ArgumentException("Invalid answer mailbox entry.",nameof(state));
            var accountedIndices=new HashSet<int>(state.RequestMailboxIndices);
            foreach(var answer in state.AnswerMailbox)accountedIndices.Add(answer.RequestIndex);
            var savedBarrier=state.BarrierAnswers??Array.Empty<NavigationAnswerState>();
            foreach(var answer in savedBarrier){if(answer==null||answer.RequestIndex<0||answer.RequestIndex>=restored.Length||!ValidSavedAnswer(answer)||!accountedIndices.Add(answer.RequestIndex))throw new ArgumentException("Invalid barrier answer.");}
            foreach(int index in state.PendingRequestIndices)if(!accountedIndices.Contains(index))throw new ArgumentException("Pending request is outside the checkpoint mailboxes.",nameof(state));
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
            Crowd.RestoreState(state.Crowd);
            foreach(var failure in savedFailures)technicalFailures.Add(failure.Entity,failure.Status);
            foreach(var pair in nextOrders)orders.Add(pair.Key,pair.Value);
            foreach(var pair in nextPending)pending.Add(pair.Key,pair.Value);
            foreach(var row in state.MovementIdentities??Array.Empty<NavigationMovementIdentityState>())movementIdentities.Add(row.Entity,row.Identity);
            foreach(var pair in nextGoals)if(!movementIdentities.ContainsKey(pair.Key))throw new ArgumentException("Missing saved movement identity.");
            foreach(int index in state.ProbeRequestIndices??Array.Empty<int>())probes.Add(restored[index].Entity,restored[index]);
            foreach(var answer in state.ProbeAnswers??Array.Empty<NavigationAnswerState>()){
                var restoredAnswer=new NavigationAnswer(restored[answer.RequestIndex],answer.Route,answer.Status,answer.Corridor);
                probeAnswers.Enqueue(restoredAnswer);TrackCorridorAnswer(restoredAnswer);}
            foreach(var pair in nextReservations)reservations.Add(pair.Key,pair.Value);
            foreach(var pair in nextGoals)retainedGoals.Add(pair.Key,pair.Value);
            if(state.CollectionLayouts==null||state.CollectionLayouts.Length!=6)throw new ArgumentException("Missing navigation collection layouts.");
            pending.RestoreLayout(state.CollectionLayouts[0]);orders.RestoreLayout(state.CollectionLayouts[1]);reservations.RestoreLayout(state.CollectionLayouts[2]);retainedGoals.RestoreLayout(state.CollectionLayouts[3]);probes.RestoreLayout(state.CollectionLayouts[4]);movementIdentities.RestoreLayout(state.CollectionLayouts[5]);
            requestSequence=state.RequestSequence;RejectedResults=state.RejectedResults;AppliedResults=state.AppliedResults;UnreachableResults=state.UnreachableResults;
            foreach(int index in state.RequestMailboxIndices)if(!Requests.TryEnqueue(restored[index]))throw new ArgumentException("Request replay exceeds transport capacity.");
            foreach(var answer in state.AnswerMailbox){var restoredAnswer=new NavigationAnswer(restored[answer.RequestIndex],answer.Route,answer.Status,answer.Corridor);
                if(!Answers.TryEnqueue(restoredAnswer))throw new ArgumentException("Answer replay exceeds transport capacity.");TrackCorridorAnswer(restoredAnswer);}
            foreach(var answer in savedBarrier){var restoredAnswer=new NavigationAnswer(restored[answer.RequestIndex],answer.Route,answer.Status,answer.Corridor);
                barrierAnswers.Add(restoredAnswer);TrackCorridorAnswer(restoredAnswer);}
            RestoreGroups(state);
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
            RestoreInstalledExecutions(state,geometryTable);
            ValidateMarchCheckpoint();
            PublishAdmission();
            return restored;
        }
        public bool MoveGroup(int[] ids, NavPoint[] goals)
        {
            return AdmissionBatch(()=>MoveGroupLocked(ids,goals));
        }
        private bool MoveGroupLocked(int[] ids, NavPoint[] goals)
        {
            if(ids==null||goals==null||ids.Length==0||ids.Length!=goals.Length||Requests.Count+RegisteredRouteCount+ids.Length>RouteRequestCapacity)return false;
            var unique=new HashSet<int>();
            for(int i=0;i<ids.Length;i++)if(!unique.Add(ids[i])||!Crowd.TryGet(ids[i],out var member)||!geometry.IsFree(goals[i],member.Radius)||!Crowd.Locate(goals[i],member.Radius,out _))return false;
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
        private readonly AuthoritySlotMap<int,string> movementIdentities=new AuthoritySlotMap<int,string>();
        private void RefreshHeldMovement()
        {
            foreach(var pair in new List<KeyValuePair<int,NavPoint>>(retainedGoals))if(Crowd.TryGet(pair.Key,out var mover)&&movementIdentities.TryGetValue(pair.Key,out var identity)&&identity!=HeldIdentity(pair.Key)){
                // Freeze old movement while replanning; a held mover awaiting explicit replacement remains held.
                DetachInstalledExecution(pair.Key);
                if(!mover.Held)Crowd.Stop(pair.Key,false);
                if(!Move(pair.Key,pair.Value))MovementCancelled?.Invoke(pair.Key);
            }
        }
        public bool Move(int id,NavPoint goal)
        {
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
            orders[id]=order; pending.Remove(id); retainedGoals[id]=goal;
            var groupMember=Member(id);if(groupMember!=null){groupMember.Goal=goal;groupMember.GoalLocation=goalLocation;groupMember.HasGoal=true;}
            ClearFailure(id);pending[id]=request;movementIdentities[id]=request.HeldIdentity;reservations[id]=goal;PublishAdmission();RouteRequested?.Invoke(request);return true;
        }
        public void Stop(int id,bool hold){using(EnterTransport()){ClearFailure(id);MovementCancelled?.Invoke(id);long order;orders.TryGetValue(id,out order);orders[id]=order+1;pending.Remove(id);reservations.Remove(id);retainedGoals.Remove(id);movementIdentities.Remove(id);SetInstalledExecution(id,null);Crowd.Stop(id,hold);PublishAdmission();}}
        public bool Remove(int id)
        {
            using(EnterTransport())return RemoveLocked(id);
        }
        private bool RemoveLocked(int id)
        {
            technicalFailures.Remove(id);
            long order; orders.TryGetValue(id,out order); orders[id]=order+1;
            pending.Remove(id); reservations.Remove(id); retainedGoals.Remove(id);movementIdentities.Remove(id);DetachMember(id);SetInstalledExecution(id,null);var removed=Crowd.Remove(id);PublishAdmission();return removed;
        }
        public bool CanRebind => pending.Count+RegisteredRouteCount<=RouteRequestCapacity;
        public void Rebind(NavigationProfile next)
        {
            AdmissionBatch(()=>RebindLocked(next));
        }
        private void RebindLocked(NavigationProfile next)
        {
            if(!CanRebind)throw new InvalidOperationException("Navigation still has too many in-flight routes to rebind atomically.");
            // Geometry and existing routes are unchanged. Reissue only in-flight solves
            // so a rule change neither restarts moving actors nor floods the mailbox.
            var waiting=new List<NavigationRequest>(pending.Values);
            profile=next;Crowd.Rebind(next);foreach(var row in installedExecutions.Values)row.Detached=true;pending.Clear();probes.Clear();
            foreach(var answer in probeAnswers)ReleaseCorridorAnswer(answer);probeAnswers.Clear();
            foreach(var answer in barrierAnswers)ReleaseCorridorAnswer(answer);barrierAnswers.Clear();
            while(Requests.TryDequeue(out _)){}while(Answers.TryDequeue(out var answer))ReleaseCorridorAnswer(answer);
            foreach(var request in waiting)if(Crowd.TryGet(request.Entity,out _)&&!Move(request.Entity,request.Goal)){RecordFailure(request,NavigationOutcome.Rejected);MovementCancelled?.Invoke(request.Entity);}
            PublishAdmission();
        }
        public void ChangeGeometry(NavGeometry next)
        {
            AdmissionBatch(()=>ChangeGeometryLocked(next));
        }
        private void ChangeGeometryLocked(NavGeometry next)
        {
            if(next==null)throw new ArgumentNullException("next");
            var previousRequests=pending.ToDictionary(p=>p.Key,p=>p.Value);var intents=new List<KeyValuePair<int,NavPoint>>(retainedGoals);
            geometry=next;navigationGeometry=next;installedExecutions.Clear();installedCorridorBytes=0;pending.Clear();reservations.Clear();probes.Clear();
            foreach(var member in memberGroups.Keys.ToArray())ResetMarchProgress(member);
            foreach(var answer in probeAnswers)ReleaseCorridorAnswer(answer);probeAnswers.Clear();
            foreach(var answer in barrierAnswers)ReleaseCorridorAnswer(answer);barrierAnswers.Clear();
            while(Requests.TryDequeue(out _)){} while(Answers.TryDequeue(out var answer))ReleaseCorridorAnswer(answer);
            Crowd.SetGeometry(next);
            foreach(var intent in intents) if(Crowd.TryGet(intent.Key,out _)&&!Move(intent.Key,intent.Value)){if(previousRequests.TryGetValue(intent.Key,out var failed))RecordFailure(failed,NavigationOutcome.Rejected);MovementCancelled?.Invoke(intent.Key);}
            PublishAdmission();
        }
        // Authority only. Reissue held-geometry changes before freezing this tick's jobs.
        internal void PrepareDeliveryBarrier(){using(EnterTransport()){RefreshHeldMovement();PublishAdmission();}}
        internal bool DeliveryBarrierReady
        {
            get
            {
                // An allocation reservation is not a solver lock: retry the frozen barrier.
                using(EnterTransport())if(hostAllocationThread!=0&&pending.Count!=0)return false;
                // Drain the bounded transport while waiting, so superseded jobs or
                // a large owner roster cannot fill the answer mailbox and deadlock.
                bool drained=false;
                while(Answers.TryDequeue(out var answer))
                {
                    drained=true;
                    var r=answer.Request;var active=r.Entity<0?probes:pending;
                    if(active.TryGetValue(r.Entity,out var expected)&&ReferenceEquals(expected,r)&&!barrierAnswers.Exists(a=>ReferenceEquals(a.Request,r)))barrierAnswers.Add(answer);
                    else RejectedResults++;
                }
                if(drained)SignalHost();
                var delivered=new HashSet<NavigationRequest>();
                foreach(var answer in barrierAnswers)delivered.Add(answer.Request);
                foreach(var request in pending.Values)if(!delivered.Contains(request))return false;
                // Producer proofs remain asynchronous: combat/production must not
                // wait for them. Answers still drain and qualify in ApplyResults.
                return true;
            }
        }
        // Authority-only notification after a route is actually installed, never on admission.
        internal event Action<NavigationRequest> RouteActivated;
        internal event Action<NavigationRequest> RouteRequested;
        internal event Action<NavigationRequest,NavigationOutcome> RouteFailed;
        internal event Action<int> MovementCancelled;
        internal Func<int,bool> PreserveInstalledExecution;
        private void ClearFailure(int id){technicalFailures.Remove(id);if(!Crowd.TryGet(id,out var u))return;u.FailedRequest=u.FailedOrder=u.FailedGroupId=u.FailedRevision=0;u.FailedGoal=null;u.LastFailure=NavigationOutcome.Idle;}
        private void RecordFailure(NavigationRequest r,NavigationOutcome outcome){technicalFailures.Remove(r.Entity);if(!Crowd.TryGet(r.Entity,out var u))return;var m=Member(r.Entity);u.FailedRequest=r.Request;u.FailedOrder=r.Order;u.FailedGroupId=r.MemberIdentity?.GroupId??GroupFor(r.Entity)?.GroupId??0;u.FailedRevision=r.MemberIdentity?.OrderRevision??m?.OrderRevision??0;u.FailedGoal=r.GoalLocation;u.LastFailure=outcome;RouteFailed?.Invoke(r,outcome);}
        public void ApplyResults()
        {
            AdmissionBatch(()=>ApplyResultsLocked());
        }
        private void ApplyResultsLocked()
        {
            if(hostAllocationThread!=0)return;
            RefreshHeldMovement();
            var corridorProviders=new Dictionary<Tuple<NavGeometry,string>,ILayeredNavigationProvider>();
            barrierAnswers.Sort((a,b)=>a.Request.Request.CompareTo(b.Request.Request));
            var ready=new Queue<NavigationAnswer>(barrierAnswers);barrierAnswers.Clear();
            bool drained=false;
            while(Answers.TryDequeue(out var incoming)){ready.Enqueue(incoming);drained=true;}
            if(drained)SignalHost();
            while(ready.Count>0){
                var answer=ready.Dequeue();
                var r=answer.Request;NavigationRequest expected;
                if(r.Entity<0){
                    if(r.Session==Generation&&Object.ReferenceEquals(r.Geometry,navigationGeometry)&&probes.TryGetValue(r.Entity,out expected)&&Object.ReferenceEquals(expected,r)&&
                        (answer.Status!=NavSolveStatus.Ready||QualifyCorridor(r,answer.CopyRoute(),answer.Corridor,corridorProviders,true))){
                        probes.Remove(r.Entity);probeAnswers.Enqueue(answer);
                    }else{ReleaseCorridorAnswer(answer);RejectedResults++;}
                    continue;
                }
                ReleaseCorridorAnswer(answer);
                if(r.Session!=Generation||r.ProfileId!=profile.ProfileId||r.ProfileRevision!=profile.Revision||!Object.ReferenceEquals(r.BaseGeometry,navigationGeometry)||r.HeldIdentity!=HeldIdentity(r.Entity)
                    ||!CurrentMember(r)||!pending.TryGetValue(r.Entity,out expected)||expected.Request!=r.Request||expected.Order!=r.Order||!Object.ReferenceEquals(expected,r)){RejectedResults++;continue;}
                pending.Remove(r.Entity);var route=answer.CopyRoute();
                long oldBytes=installedExecutions.TryGetValue(r.Entity,out var oldInstalled)
                    ?oldInstalled.Corridor.EstimatedRetainedBytes+16L*oldInstalled.InstalledRoute.Length:0;
                bool overCorridorBudget=answer.Corridor!=null&&
                    hostRetainedBytes+answerCorridorBytes+installedCorridorBytes-oldBytes+
                    ProspectiveGeometryBytes(r.Entity,r)+answer.Corridor.EstimatedRetainedBytes+16L*(route.Length+1)>
                    LayeredNavigationScheduler.MaxRetainedBytes;
                var effectiveStatus=overCorridorBudget?NavSolveStatus.CapacityExceeded:answer.Status;
                if(effectiveStatus!=NavSolveStatus.Ready){
                    var outcome=effectiveStatus==NavSolveStatus.UnreachableInGraph?NavigationOutcome.Unreachable:NavigationOutcome.Rejected;
                    RecordFailure(r,outcome);MovementCancelled?.Invoke(r.Entity);
                    if(effectiveStatus==NavSolveStatus.InvalidEndpoint||effectiveStatus==NavSolveStatus.BlockedConnector||effectiveStatus==NavSolveStatus.CapacityExceeded)
                        technicalFailures[r.Entity]=effectiveStatus;
                    if(outcome==NavigationOutcome.Unreachable)UnreachableResults++;
                    reservations.Remove(r.Entity);retainedGoals.Remove(r.Entity);
                    if(!Crowd.TryGet(r.Entity,out var heldFailure)||!heldFailure.Held&&!heldFailure.Moving&&!(PreserveInstalledExecution?.Invoke(r.Entity)??false))Crowd.Reject(r.Entity,outcome);
                    continue;
                }
                bool valid=Crowd.TryGet(r.Entity,out var actor)&&QualifyCorridor(r,route,answer.Corridor,corridorProviders,true);
                var previous=valid?actor.Position:r.Start;
                foreach(var point in route){if(!r.Geometry.SegmentFree(previous,point,r.Profile.Radius)){valid=false;break;}previous=point;}
                if(valid&&Crowd.SetRoute(r.Entity,r.Goal,route,r.GoalLocation)){AppliedResults++;ClearFailure(r.Entity);actor.InstalledGroupId=r.MemberIdentity?.GroupId??0;actor.InstalledRevision=r.MemberIdentity?.OrderRevision??0;actor.InstalledRequest=r.Request;actor.InstalledOrder=r.Order;
                    SetInstalledExecution(r.Entity,answer.Corridor,r);
                    RouteActivated?.Invoke(r);}
                else {
                    RejectedResults++;
                    // Replan from current position if the actor moved while this request was in flight.
                    if(Crowd.TryGet(r.Entity,out var current)&&
                       (Math.Abs(current.Position.X-r.Start.X)>1e-4||Math.Abs(current.Position.Z-r.Start.Z)>1e-4)&&Move(r.Entity,r.Goal))continue;
                    RecordFailure(r,NavigationOutcome.Rejected);MovementCancelled?.Invoke(r.Entity);reservations.Remove(r.Entity);retainedGoals.Remove(r.Entity);if(!Crowd.TryGet(r.Entity,out var heldRejected)||!heldRejected.Held&&!heldRejected.Moving&&!(PreserveInstalledExecution?.Invoke(r.Entity)??false))Crowd.Reject(r.Entity,NavigationOutcome.Rejected);
                }
            }
            PublishAdmission();
        }
        public void Step(double dt)
        {
            if(dt<=0||double.IsNaN(dt)||double.IsInfinity(dt))throw new ArgumentException("Invalid step duration.");
            AdmissionBatch(()=>{
                ApplyResultsLocked();
                try{PrepareMarch(dt);Crowd.Step(dt);}finally{Crowd.ClearPreferredMotion();}
                foreach(var unit in Crowd.Units)if(!unit.Moving&&!pending.ContainsKey(unit.Id)){
                    reservations.Remove(unit.Id);retainedGoals.Remove(unit.Id);
                }
                PublishAdmission();
            });
        }
    }
}

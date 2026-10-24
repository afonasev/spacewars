using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // Narrow cross-thread facade. The authority owns the session; the host may
    // only transfer published inputs and publish proposals through its gate.
    public sealed class NavigationRoutePort
    {
        private readonly NavigationSession session;
        internal NavigationRoutePort(NavigationSession session)
        { this.session = session ?? throw new ArgumentNullException(nameof(session)); }

        internal NavigationHostRoot CreatePhysicalHostRoot(INavStorageReservation reservation)=>session.CreatePhysicalHostRoot(reservation);
        internal void ReleaseHostForWorldCheckpoint(Action releaseHost)=>session.ReleaseHostForWorldCheckpoint(releaseHost);
        public NavigationAdmission Admission => session.Admission;
        internal NavigationAdmissionPin PinAdmission()=>session.PinAdmission();
        internal bool ContainsCurrent(NavigationRequest request){using(var pin=PinAdmission())return pin.Value!=null&&pin.Value.Contains(request);}
        public long CorridorRetainedBytes=>session.CorridorRetainedBytes;
        internal string ArrivalRetentionDiagnostic=>session.ArrivalRetentionDiagnostic;
        internal string ArrivalAuthoritySnapshot=>session.ArrivalAuthoritySnapshot;
        internal int QueuedRequestCount=>session.Requests.Count;
        internal event Action Changed {add{session.HostChanged+=value;}remove{session.HostChanged-=value;}}
        public void SignalHost()=>session.SignalHost();
        // Short atomic transport commits only; computation must run outside this gate.
        public void RunHostStep(Action step)=>session.RunHostStep(step);
        public void SetHostRetainedBytes(long bytes)=>session.SetHostRetainedBytes(bytes);
        internal double TransportWaitMilliseconds=>session.TransportWaitMilliseconds;
        internal double TransportHoldMilliseconds=>session.TransportHoldMilliseconds;
        internal long BeginHostAllocation()=>session.BeginHostAllocation();
        internal void EndHostAllocation(long bytes)=>session.EndHostAllocation(bytes);
        internal void AttachHost(CertifiedRouteKernel host)=>session.AttachCertifiedHost(host);
        internal void DetachHost(CertifiedRouteKernel host)=>session.DetachCertifiedHost(host);
        internal bool TryOpenPhysicalHostScope(NavigationPreparationKind kind,long owner,long publication,
            out NavigationRetentionBudget.Scope scope)=>session.TryOpenPhysicalHostScope(kind,owner,publication,out scope);
        internal long? PhysicalRetainedBytes=>session.HasPhysicalHostRoot?session.PhysicalHostBudget.RetainedBytes:(long?)null;
        internal bool HasPhysicalHostRoot=>session.HasPhysicalHostRoot;
        internal bool UseLegacyHost=>session.UseLegacyHost;
        internal bool TickQuantaEnabled=>session.TickQuantaEnabled;
        internal bool QuantumRunnable=>session.QuantumRunnable;
        internal long? PhysicalHeldBytes=>session.HasPhysicalHostRoot?session.PhysicalHostBudget.HeldBytes:(long?)null;
        internal NavigationCreditPromotion TryPromoteAccruedCredit()=>session.TryPromoteAccruedCredit();
        internal void StageRetainedPhysicalArrivals(NavigationHostRoot root,INavStorageReservation reservation)=>session.StageRetainedPhysicalArrivals(root,reservation);
        internal NavigationHostRoot PinCommittedHostRoot()=>session.PinCommittedHostRoot();
        internal void PublishPhysicalHostRoot(NavigationRetentionBudget.Scope scope,
            NavigationHostRoot expected,NavigationHostRoot candidate)
            =>session.PublishPhysicalHostRoot(scope,expected,candidate,()=>{});
        internal bool PublishCompletedPhysicalHost(NavigationRetentionBudget.Scope scope,
            NavigationHostRoot expected,NavigationHostRoot candidate,NavNameKey key)
            =>session.PublishCompletedPhysicalHost(scope,expected,candidate,key);
        internal bool CanRetireDeliveredPhysicalAnswer(NavigationAnswer answer)
            =>session.CanRetireDeliveredPhysicalAnswer(answer);
        internal bool RetirePhysicalArrivalOwner()=>session.RetirePhysicalArrivalOwner();
        internal bool TryObservePhysicalArrivalIngress(out NavigationRequest request,out NavigationAdmissionPin observed,out NavArrivalInput input,out NavArrivalProposal proposal,out long token,out long attempt)
            =>session.TryObservePhysicalArrivalIngress(out request,out observed,out input,out proposal,out token,out attempt);
        internal void PublishRegisteredPhysicalArrival(NavigationRetentionBudget.Scope scope,NavigationHostRoot expected,NavigationHostRoot next,NavigationAdmission observed,NavigationRequest request,NavNameKey key,long attempt)
            =>session.PublishRegisteredPhysicalArrival(scope,expected,next,observed,request,key,attempt);
        internal bool TryObservePhysicalStale(out NavigationRequest request)=>session.TryObservePhysicalStale(out request);
        internal bool PublishOwnedStaleIngress(NavigationRetentionBudget.Scope scope,NavigationHostRoot source,NavigationHostRoot next,NavigationRequest request)=>session.PublishOwnedStaleIngress(scope,source,next,request);
        internal bool TryObservePhysicalIngress(out NavigationRequest request,out NavigationAdmissionPin observed)
            =>session.TryObservePhysicalIngress(out request,out observed);
        internal void PublishRegisteredPhysicalHost(NavigationRetentionBudget.Scope scope,
            NavigationHostRoot expected,NavigationHostRoot candidate,NavigationAdmission observed,
            NavigationRequest ingress,NavNameKey key)
        {
            if(candidate==null||!candidate.Rows.TryGetValue(key,out var entry)||!(entry is NavigationHostRow row))
                throw new ArgumentException("Missing prepared physical host row.");
            ValidateCertifiedScheduled(ingress,observed,row.Scheduled);
            session.PublishRegisteredPhysicalHost(scope,expected,candidate,observed,ingress,key);
        }
        public bool TryTransfer(LayeredNavigationScheduler scheduler,
            Func<NavigationRequest, NavigationAdmission, NavScheduledRequest> prepare, out NavigationRequest request)
            => session.TryTransferRouteToScheduler(scheduler,(candidate,admission)=>{
                var scheduled=prepare(candidate,admission);
                if(scheduled==null)return null;
                ValidateCertifiedScheduled(candidate,admission,scheduled);
                return scheduled;
            },out request);
        private static void ValidateCertifiedScheduled(NavigationRequest candidate,NavigationAdmission admission,NavScheduledRequest scheduled)
        {
                if(scheduled.Profile.CostId!="distance"||scheduled.Profile.CostRevision!=1||
                    scheduled.Clearance.Id!="ground"||scheduled.Clearance.Revision!=1||
                    scheduled.Clearance.MobilityClass!="ground"||
                    scheduled.Limits.MaxSamples!=1048576||scheduled.Limits.MaxNodes!=300000||
                    scheduled.Limits.MaxArcs!=1200000||scheduled.MaxLabels!=1048576)
                    throw new InvalidOperationException("Uncertified route solver profile.");
                if(admission.AuthoredTerrain==null&&admission.SurfaceProviderId==NavLocation.FlatSurface){
                    if(!(scheduled.Provider is LayeredNavigationProvider)||scheduled.Provider.Surfaces.Count!=1||
                        scheduled.Provider.Portals.Count!=0||scheduled.Provider.Surfaces[0].Id!=NavLocation.FlatSurface||
                        scheduled.Provider.Surfaces[0].DistanceCost!=1||
                        scheduled.Provider.Surfaces[0].Area.HalfExtent!=candidate.Geometry.HalfExtent||
                        scheduled.Provider.Surfaces[0].Area.Blockers.Count!=candidate.Geometry.Obstacles.Count)
                        throw new InvalidOperationException("Uncertified flat route provider.");
                    for(int i=0;i<candidate.Geometry.Obstacles.Count;i++)
                        if(!scheduled.Provider.Surfaces[0].Area.Blockers[i].GeometryEquals(candidate.Geometry.Obstacles[i]))
                            throw new InvalidOperationException("Flat provider blockers differ from authority geometry.");
                    double h=candidate.Geometry.HalfExtent;
                    var expected=new[]{new NavPoint(-h,-h),new NavPoint(h,-h),new NavPoint(h,h),new NavPoint(-h,h)};
                    if(!scheduled.Provider.Surfaces[0].Area.Boundary.Vertices.SequenceEqual(expected))
                        throw new InvalidOperationException("Flat provider boundary differs from authority geometry.");
                }
        }
        internal bool QueuedArrivalHead => session.Requests.TryPeek(out var head)&&session.IsArrivalRequest(head);
        internal int AllocationCount => session.AllocationCount;
        internal bool ArrivalReconstructing => session.ArrivalReconstructing;
        internal bool AdvanceArrivals(int budget) => session.AdvanceArrivalAuthority(budget);
        internal bool IsArrival(NavigationRequest request) => session.IsArrivalRequest(request);
        internal bool TryTransferArrival(out NavigationRequest request,out NavScheduledRequest scheduled,out NavTypedRoute route,out ArrivalProjection projection,out ArrivalProviderLease owner)
            =>session.TryTransferArrival(out request,out scheduled,out route,out projection,out owner);
        internal void ReleaseArrivalProjection(ArrivalProviderLease owner,string reason)=>session.ReleaseArrivalProjection(owner,reason);
        internal ArrivalProviderLease NextRetiringArrivalProjection()=>session.NextRetiringArrivalProjection();
        internal bool RetireRestoredArrivalProjection()=>session.RetireRestoredArrivalProjection();
        internal bool TryPrepareArrivalProjectionDelta(ArrivalProviderLease lease,bool point,bool typed,int observedExtra,long traceScratch,out ArrivalProjectionDelta delta)=>session.TryPrepareArrivalProjectionDelta(lease,point,typed,observedExtra,traceScratch,out delta);
        internal bool CommitArrivalProjectionDelta(ArrivalProjectionDelta delta,ArrivalProjection next)=>session.CommitArrivalProjectionDelta(delta,next);
        internal void SaveArrivalProjection(NavigationRequest request,ArrivalProjection state)=>session.SaveArrivalProjection(request,state);
        public bool TryComplete(NavigationAnswer answer) => session.TryCompleteRegisteredRoute(answer);
        public bool TryDropStale(out NavigationRequest request) => session.TryDropStaleRoute(out request);
        public bool TryRejectUnserviceable(NavigationRequest expected,out NavigationRequest request)
            => session.TryRejectUnserviceableRoute(expected,out request);
    }
}

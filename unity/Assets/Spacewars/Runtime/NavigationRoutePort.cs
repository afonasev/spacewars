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

        public NavigationAdmission Admission => session.Admission;
        public long CorridorRetainedBytes=>session.CorridorRetainedBytes;
        // Keeps scheduler growth and authority corridor installation under one
        // retention gate for the complete host work quantum.
        public void RunHostStep(Action step)=>session.RunHostStep(step);
        public void SetHostRetainedBytes(long bytes)=>session.SetHostRetainedBytes(bytes);
        public bool TryTransfer(LayeredNavigationScheduler scheduler,
            Func<NavigationRequest, NavigationAdmission, NavScheduledRequest> prepare, out NavigationRequest request)
            => session.TryTransferRouteToScheduler(scheduler,(candidate,admission)=>{
                var scheduled=prepare(candidate,admission);
                if(scheduled==null)return null;
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
                return scheduled;
            },out request);
        public bool TryComplete(NavigationAnswer answer) => session.TryCompleteRegisteredRoute(answer);
        public bool TryDropStale(out NavigationRequest request) => session.TryDropStaleRoute(out request);
        public bool TryRejectUnserviceable(NavigationRequest expected,out NavigationRequest request)
            => session.TryRejectUnserviceableRoute(expected,out request);
    }
}

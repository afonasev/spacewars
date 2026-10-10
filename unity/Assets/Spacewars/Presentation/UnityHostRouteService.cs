using System;
using System.Threading;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    // Instantiate and service on the Unity main thread. Shared by Player and headless host.
    // Owns solver resources only; answers remain proposals validated by domain authority.
    public sealed class UnityHostRouteService : IDisposable
    {
        private readonly int thread=Thread.CurrentThread.ManagedThreadId;
        private UnityNavigationRouter router;
        private SharedFlowRouter smallRouter;
        private NavGeometry routedGeometry;
        private NavigationProfile routedProfile;
        private double smallRadius;
        private NavigationRequest deferredRequest;
        private NavigationAnswer deferredAnswer;
        private readonly CertifiedRouteHost certified = new CertifiedRouteHost();
        public NavSchedulerCounters CertifiedCounters => certified.Counters;
        public long SolverEpoch => certified.SolverEpoch;
        public long CertifiedRetainedBytes => certified.RetainedBytes;
        public long CertifiedPeakRetainedBytes => certified.PeakRetainedBytes;
        public string LastProjectionFailure => certified.LastProjectionFailure;
        public int NavMeshRequests {get;private set;}
        public int FlowRequests {get;private set;}
        public int NavMeshBuilds {get;private set;}
        public long FlowRebuilds {get;private set;}
        public long FlowCacheHits {get;private set;}
        public long FlowExpandedCells {get;private set;}
        public int PeakFlowCacheFields {get;private set;}
        public long PeakFlowCacheCells {get;private set;}
        private NavPoint[] Flow(SharedFlowRouter solver,NavigationRequest request)
        {
            FlowRequests++;int builds=solver.RebuildCount,hits=solver.FieldCacheHits;long cells=solver.ExpandedCells;
            var result=solver.FindPath(request.Start,request.Goal);
            FlowRebuilds+=solver.RebuildCount-builds;FlowCacheHits+=solver.FieldCacheHits-hits;FlowExpandedCells+=solver.ExpandedCells-cells;PeakFlowCacheFields=Math.Max(PeakFlowCacheFields,solver.CachedFieldCount);PeakFlowCacheCells=Math.Max(PeakFlowCacheCells,solver.CachedCells);return result;
        }
        private void CheckThread(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Unity routes require their owning main thread.");}
        public void Service(PlayableRuntime runtime,int budget)
            {var binding=runtime.NavigationBinding;ServiceHost(binding,runtime.Requests,runtime.Answers,runtime.Generation,budget);}
        public void Service(PlayableAuthorityTick authority,int budget)
            {var binding=authority.NavigationBinding;ServiceHost(binding,authority.Requests,authority.Answers,authority.Generation,budget);}
        public void Service(PlayableRouteBinding binding,int budget)
        {
            CheckThread();if(binding==null||binding.RoutePort==null||budget<1)throw new ArgumentException("Invalid certified route binding.");
            if(binding.Admission?.AuthoredTerrain==null&&binding.Admission?.SurfaceProviderId!=NavLocation.FlatSurface)
                throw new NotSupportedException("No certified provider for this terrain.");
            certified.Service(binding,budget);
        }
        private void ServiceHost(PlayableRouteBinding binding,NavMailbox<NavigationRequest> requests,
            NavMailbox<NavigationAnswer> answers,long generation,int budget)
        {
            CheckThread();if(budget<1)throw new ArgumentOutOfRangeException(nameof(budget));
            if(binding.RoutePort!=null&&(binding.Admission?.AuthoredTerrain!=null||
                binding.Admission?.SurfaceProviderId==NavLocation.FlatSurface))
                Service(binding,budget);
            else Service(requests,answers,generation,binding.Geometry,binding.Profile,budget);
        }
        public void Service(NavMailbox<NavigationRequest> requests,NavMailbox<NavigationAnswer> answers,long generation,NavGeometry current,PlayableProfile profile,int budget)
        {
            CheckThread();if(budget<1)throw new ArgumentOutOfRangeException(nameof(budget));
            if(deferredAnswer!=null){if(!answers.TryEnqueue(deferredAnswer))return;deferredAnswer=null;}
            for(int i=0;i<budget;i++)
            {
                var request=deferredRequest;
                if(request==null&&!requests.TryDequeue(out request))break;
                deferredRequest=null;
                if(request.Session!=generation)continue;
                if(request.ProfileId!=profile.Navigation.ProfileId||request.ProfileRevision!=profile.Navigation.Revision)
                {
                    if(request.ProfileId==profile.Navigation.ProfileId&&request.ProfileRevision<profile.Navigation.Revision)continue;
                    deferredRequest=request;return;
                }
                if(!ReferenceEquals(request.BaseGeometry,current))
                {
                    if(request.Geometry.Revision<current.Revision)continue;
                    deferredRequest=request;return; // Authority publication may follow mailbox enqueue.
                }
                NavPoint[] route;
                if(!ReferenceEquals(request.Geometry,request.BaseGeometry))
                    route=Flow(new SharedFlowRouter(request.Geometry,request.Profile),request);
                else
                {
                    if(!ReferenceEquals(routedGeometry,request.Geometry)||!ReferenceEquals(routedProfile,profile.Navigation))
                    {router?.Dispose();router=null;smallRouter=null;routedGeometry=request.Geometry;routedProfile=profile.Navigation;}
                    if(request.Profile.Radius!=profile.Navigation.Radius)
                    {
                        if(smallRouter==null||smallRadius!=request.Profile.Radius)
                        {smallRouter=new SharedFlowRouter(request.Geometry,request.Profile);smallRadius=request.Profile.Radius;}
                        route=Flow(smallRouter,request);
                    }
                    else
                    {
                        if(router==null){router=new UnityNavigationRouter(request.Geometry,profile.Navigation);NavMeshBuilds++;}
                        NavMeshRequests++;
                        route=router.FindPath(request.Start,request.Goal);
                    }
                }
                var answer=new NavigationAnswer(request,route);
                if(!answers.TryEnqueue(answer)){deferredAnswer=answer;return;}
            }
        }
        public void Dispose(){CheckThread();certified.Clear();router?.Dispose();router=null;smallRouter=null;routedGeometry=null;routedProfile=null;deferredRequest=null;deferredAnswer=null;}
    }
}

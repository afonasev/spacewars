using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // One execution owner per pool. Derived memoization has no world/AI authority.
    internal sealed class ObservedPathWorkPool : IDisposable
    {
        private readonly Dictionary<PlayableOwner,List<ObservedRouteCache>> caches=new Dictionary<PlayableOwner,List<ObservedRouteCache>>();
        // The path lane owns these routers on one thread. An exact immutable
        // input binding can reuse another owner's fields without borrowing any
        // owner policy, fog history or cached route proof.
        private readonly Dictionary<(NavGeometry, NavigationProfile, double, double, double), SharedFlowRouter> shared = new();
        private sealed class ObservedRouteCache
        {
            internal NavGeometry Geometry;internal NavigationProfile Profile;internal double Radius,Speed,Turn;internal SharedFlowRouter Router;
        }
        private readonly bool nativeDistancePages;
#if UNITY_INCLUDE_TESTS
        private static Action testGeometryReplacement;
#endif
        internal ObservedPathWorkPool(bool nativeDistancePages=false){this.nativeDistancePages=nativeDistancePages;}
        public void Dispose()
        {
            foreach(var pool in caches.Values)
                foreach(var cache in pool)cache.Router?.Dispose();
            shared.Clear();
            caches.Clear();
        }
        private static (NavGeometry, NavigationProfile, double, double, double) Key(ObservedRouteCache cache)
            => (cache.Geometry, cache.Profile, cache.Radius, cache.Speed, cache.Turn);
        private void ReleaseRouter(ObservedRouteCache cache)
        {
            if(nativeDistancePages)shared.Remove(Key(cache));
            cache.Router?.Dispose();
        }
        private const int ObservedRouteFieldLimit=64;
        private const int ObservedRouteProfileLimit=8;
        internal SharedFlowRouter Router(PlayableOwner owner,NavGeometry observed,NavigationProfile basis,double radius,double speed,double turn)
        {
            if(nativeDistancePages && shared.TryGetValue((observed,basis,radius,speed,turn),out var warm))
            {
                // The owning slot still controls disposal. Refresh its profile
                // recency if this is that slot's own query.
                if(caches.TryGetValue(owner,out var owned))
                {
                    var current=owned.Find(c=>ReferenceEquals(c.Router,warm));
                    if(current!=null){owned.Remove(current);owned.Add(current);}
                }
                return Trim(warm);
            }
            if(!caches.TryGetValue(owner,out var pool))caches.Add(owner,pool=new List<ObservedRouteCache>());
            bool SameGeometry(NavGeometry a,NavGeometry b)=>ReferenceEquals(a,b)||a.HalfExtent==b.HalfExtent&&a.Obstacles.Count==b.Obstacles.Count&&a.Obstacles.Select((o,i)=>o.GeometryEquals(b.Obstacles[i])).All(x=>x);
            // Mixed armies alternate footprints on every review. Retain independent exact
            // predicates/fields instead of discarding the previous unit's whole router.
            var cache=pool.Find(c=>ReferenceEquals(c.Profile,basis)&&c.Radius==radius&&c.Speed==speed&&c.Turn==turn);
            if(cache==null){
                if(pool.Count==ObservedRouteProfileLimit){ReleaseRouter(pool[0]);pool.RemoveAt(0);}
                cache=new ObservedRouteCache{Geometry=observed,Profile=basis,Radius=radius,Speed=speed,Turn=turn};
            }else{
                // Keep the old owner reachable from the pool until replacement has
                // succeeded, including allocation failure during geometry comparison.
                if(!SameGeometry(cache.Geometry,observed)){
#if UNITY_INCLUDE_TESTS
                    testGeometryReplacement?.Invoke();
#endif
                    var replacement=cache.Router?.ForGeometry(observed);
                    ReleaseRouter(cache);
                    cache.Router=replacement;cache.Geometry=observed;
                }
                pool.Remove(cache);
            }
            pool.Add(cache); // Bounded least-recently-used profile ownership, never persisted.
            // Four-player reviews include more than eight recurring objectives/candidates.
            // Keep a bounded working set (64 fields per typed observer router), not
            // a seven-entry cache that cyclically evicts every public objective.
            // A request can add both floor and attachment fields. Evict old fields while
            // retaining exact grid predicates for this unchanged observer geometry/profile.
            if(cache.Router==null)cache.Router=new SharedFlowRouter(cache.Geometry,basis.ForUnit(radius,speed,turn),null,true,nativeDistancePages);
            if(nativeDistancePages)shared[Key(cache)]=cache.Router;
            return Trim(cache.Router);
        }

        private static SharedFlowRouter Trim(SharedFlowRouter router)
        {
            if(router.CachedFieldCount>=ObservedRouteFieldLimit-1)router.TrimFields(ObservedRouteFieldLimit-2);
            return router;
        }
    }

    // Immutable solver inputs; no Domain, vision, live actor or budget callback.
    internal sealed class ObservedPathWork
    {
        internal readonly PlayableOwner Owner;
        internal readonly NavGeometry Geometry;
        internal readonly NavigationProfile Profile;
        internal readonly double Radius,Speed,Turn;
        internal readonly NavPoint Origin,Goal;
        internal ObservedPathWork(PlayableOwner owner,NavGeometry geometry,NavigationProfile profile,double radius,double speed,double turn,NavPoint origin,NavPoint goal)
        {Owner=owner;Geometry=geometry??throw new ArgumentNullException(nameof(geometry));Profile=profile??throw new ArgumentNullException(nameof(profile));Radius=radius;Speed=speed;Turn=turn;Origin=origin;Goal=goal;}
        internal NavPoint[] Execute(ObservedPathWorkPool pool)=>Execute(pool.Router(Owner,Geometry,Profile,Radius,Speed,Turn));
        internal NavPoint[] Execute(SharedFlowRouter boundRouter)=>boundRouter.FindPath(Origin,Goal);
    }
}

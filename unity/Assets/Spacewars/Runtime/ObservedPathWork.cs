using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // One execution owner per pool. Derived memoization has no world/AI authority.
    internal sealed class ObservedPathWorkPool
    {
        private readonly Dictionary<PlayableOwner,List<ObservedRouteCache>> caches=new Dictionary<PlayableOwner,List<ObservedRouteCache>>();
        private sealed class ObservedRouteCache
        {
            internal NavGeometry Geometry;internal NavigationProfile Profile;internal double Radius,Speed,Turn;internal SharedFlowRouter Router;
        }
        private const int ObservedRouteFieldLimit=64;
        private const int ObservedRouteProfileLimit=8;
        internal SharedFlowRouter Router(PlayableOwner owner,NavGeometry observed,NavigationProfile basis,double radius,double speed,double turn)
        {
            if(!caches.TryGetValue(owner,out var pool))caches.Add(owner,pool=new List<ObservedRouteCache>());
            bool SameGeometry(NavGeometry a,NavGeometry b)=>ReferenceEquals(a,b)||a.HalfExtent==b.HalfExtent&&a.Obstacles.Count==b.Obstacles.Count&&a.Obstacles.Select((o,i)=>o.GeometryEquals(b.Obstacles[i])).All(x=>x);
            // Mixed armies alternate footprints on every review. Retain independent exact
            // predicates/fields instead of discarding the previous unit's whole router.
            var cache=pool.Find(c=>ReferenceEquals(c.Profile,basis)&&c.Radius==radius&&c.Speed==speed&&c.Turn==turn);
            if(cache==null){
                if(pool.Count==ObservedRouteProfileLimit)pool.RemoveAt(0);
                cache=new ObservedRouteCache{Geometry=observed,Profile=basis,Radius=radius,Speed=speed,Turn=turn};
            }else{
                pool.Remove(cache);
                if(!SameGeometry(cache.Geometry,observed)){
                    cache.Router=cache.Router?.ForGeometry(observed);cache.Geometry=observed;
                }
            }
            pool.Add(cache); // Bounded least-recently-used profile ownership, never persisted.
            // Four-player reviews include more than eight recurring objectives/candidates.
            // Keep a bounded working set (64 fields per typed observer router), not
            // a seven-entry cache that cyclically evicts every public objective.
            // A request can add both floor and attachment fields. Evict old fields while
            // retaining exact grid predicates for this unchanged observer geometry/profile.
            if(cache.Router==null)cache.Router=new SharedFlowRouter(cache.Geometry,basis.ForUnit(radius,speed,turn),null,true);
            else if(cache.Router.CachedFieldCount>=ObservedRouteFieldLimit-1)cache.Router.TrimFields(ObservedRouteFieldLimit-2);
            return cache.Router;
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

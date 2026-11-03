using System;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private int wreckRouteRevision=-1;
        private WreckRouteQueries tickWreckQueries;
        private bool WaitingForWreck(int id)=>units.TryGetValue(id,out var u)&&u.WreckWaiting&&u.CurrentOrder!=null&&u.LastOrder!=null&&u.CurrentOrder.CommandSequence==u.LastOrder.Sequence;
        private PlayableTacticalOrderSnapshot WreckWaitingOrder(Unit u)=>u.CurrentOrder;
        // One command/reconciliation observes one obstacle set. Survivors with the same
        // footprint and goal can continue the same exact flow field instead of rebuilding it.
        private sealed class WreckRouteQueries
        {
            private readonly PlayableDomain world;
            private NavGeometry geometry;
            private NavGeometry source;
            internal readonly NavigationProfile Profile;
            private NavGeometry permanent;
            private readonly Dictionary<double,SharedFlowRouter> currentRouters=new Dictionary<double,SharedFlowRouter>();
            private readonly Dictionary<double,SharedFlowRouter> permanentRouters=new Dictionary<double,SharedFlowRouter>();

            internal WreckRouteQueries(PlayableDomain world,bool reusePredicates=false)
            {
                this.world=world;
                source=world.Geometry;
                geometry=reusePredicates?SnapshotPrimitiveGeometry(source):source;
                Profile=world.profile.Navigation;
            }

            internal void ObserveGeometry(NavGeometry next)
            {
                if(ReferenceEquals(next,source))return;
                source=next;
                next=SnapshotPrimitiveGeometry(next);
                // Only untouched grid predicates survive. Fields and cached paths
                // belong to a single revision, even between impacts of the same volley.
                foreach(double radius in currentRouters.Keys.ToArray())
                    currentRouters[radius]=currentRouters[radius].ForGeometry(next);
                geometry=next;
                permanent=null;
                permanentRouters.Clear();
            }

            // Wreck queries outlive a geometry publication within this tick. Keep
            // value footprints, never a router over a retired physical obstacle array.
            private static NavGeometry SnapshotPrimitiveGeometry(NavGeometry map)
                =>new NavGeometry(map.HalfExtent,map.Obstacles.ToArray(),map.Revision);

            private SharedFlowRouter Router(Dictionary<double,SharedFlowRouter> routers,NavGeometry map,NavUnit actor)
            {
                // Grid size is fixed for this pass. Speed and turn rate do not enter FindPath.
                if(!routers.TryGetValue(actor.Radius,out var router))
                {
                    var terms=world.profile.Navigation.ForUnit(actor.Radius,actor.Speed,actor.TurnSpeed);
                    routers.Add(actor.Radius,router=new SharedFlowRouter(map,terms));
                }
                return router;
            }

            internal bool Closed(NavUnit actor,NavPoint goal)
            {
                if(geometry.SegmentFree(actor.Position,goal,actor.Radius))return false;
                if(Router(currentRouters,geometry,actor).HasPath(actor.Position,goal))return false;
                if(permanent==null)
                {
                    var temporary=world.WreckObstacles().ToArray();
                    var solids=geometry.Obstacles.Where(o=>!temporary.Any(w=>w.GeometryEquals(o))).ToArray();
                    permanent=new NavGeometry(geometry.HalfExtent,solids,geometry.Revision);
                }
                var route=Router(permanentRouters,permanent,actor).FindPath(actor.Position,goal);
                if(route.Length==0)return false;
                if(world.profile.AuthoredMap!=null)
                {
                    var location=actor.Location;
                    foreach(var point in route)
                    {
                        if(!world.profile.AuthoredMap.TryTraverse(location,point,actor.Radius,out var next))return false;
                        location=next;
                    }
                }
                return true;
            }
        }

        private WreckRouteQueries QueriesForCurrentWrecks()
        {
            if(!wrecks.Values.Any(w=>w.Blocks(Tick)))return null;
            if(!(profile.AuthoredMap is ProvingGroundMap)||Geometry.Obstacles.Any(o=>o.Polygon!=null))
                return new WreckRouteQueries(this);
            if(tickWreckQueries==null||!ReferenceEquals(tickWreckQueries.Profile,profile.Navigation))
                tickWreckQueries=new WreckRouteQueries(this,true);
            else tickWreckQueries.ObserveGeometry(Geometry);
            return tickWreckQueries;
        }

        private void RetryWreckWaits()=>ReconcileWreckRoutes();
        private void ReconcileWreckRoutes()
        {
            if(wreckRouteRevision==Geometry.Revision)return;wreckRouteRevision=Geometry.Revision;
            var queries=QueriesForCurrentWrecks();
            if(queries==null&&!units.Values.Any(u=>WaitingForWreck(u.Id)))return;
            foreach(var u in units.Values.ToArray()){
                if(u.Air!=null||!TryActor(u.Id,out var actor)||actor.Held||!tacticalQueues.TryGetValue(u.Id,out var q))continue;
                var e=q.Pending??q.Active;if(e==null||e.Stamp.Revision!=u.LastOrder?.Revision||e.Issuance.Command.Kind!=PlayableCommandKind.Move&&e.Issuance.Command.Kind!=PlayableCommandKind.AttackMove)continue;
                bool waiting=WaitingForWreck(u.Id);if(waiting&&navigation.IsPending(u.Id))continue;
                bool closed=queries!=null&&queries.Closed(actor,e.Issuance.Command.Target);
                if(closed&&!waiting){if(!CommitInternalTactical(e.Issuance,new[]{u.Id},InternalTacticalKind.WreckWait,activationRevision:e.Stamp.Revision))wreckRouteRevision=-1;}
                else if(waiting&&!closed){if(!CommitInternalTactical(e.Issuance,new[]{u.Id},InternalTacticalKind.Combat,e.Issuance.Command.Target,e.Stamp.Revision))wreckRouteRevision=-1;}
            }
        }
    }
}

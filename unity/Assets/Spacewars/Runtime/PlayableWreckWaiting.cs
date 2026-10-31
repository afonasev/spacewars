using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private int wreckRouteRevision=-1;
        private bool WaitingForWreck(int id)=>units.TryGetValue(id,out var u)&&u.WreckWaiting&&u.CurrentOrder!=null&&u.LastOrder!=null&&u.CurrentOrder.CommandSequence==u.LastOrder.Sequence;
        private PlayableTacticalOrderSnapshot WreckWaitingOrder(Unit u)=>u.CurrentOrder;
        private bool WreckRouteClosed(NavUnit actor,NavPoint goal)
        {
            if(!wrecks.Values.Any(w=>w.Blocks(Tick)))return false;
            var terms=profile.Navigation.ForUnit(actor.Radius,actor.Speed,actor.TurnSpeed);
            if(new SharedFlowRouter(Geometry,terms).FindPath(actor.Position,goal).Length>0)return false;
            var temporary=WreckObstacles().ToArray();var permanent=Geometry.Obstacles.Where(o=>!temporary.Any(w=>w.GeometryEquals(o))).ToArray();
            var route=new SharedFlowRouter(new NavGeometry(Geometry.HalfExtent,permanent,Geometry.Revision),terms).FindPath(actor.Position,goal);
            if(route.Length==0)return false;
            if(profile.AuthoredMap!=null){var location=actor.Location;foreach(var point in route){if(!profile.AuthoredMap.TryTraverse(location,point,actor.Radius,out var next))return false;location=next;}}
            return true;
        }
        private void RetryWreckWaits()=>ReconcileWreckRoutes();
        private void ReconcileWreckRoutes()
        {
            if(wreckRouteRevision==Geometry.Revision)return;wreckRouteRevision=Geometry.Revision;
            if(!wrecks.Values.Any(w=>w.Blocks(Tick))&&!units.Values.Any(u=>WaitingForWreck(u.Id)))return;
            foreach(var u in units.Values.ToArray()){
                if(u.Air!=null||!TryActor(u.Id,out var actor)||actor.Held||!tacticalQueues.TryGetValue(u.Id,out var q))continue;
                var e=q.Pending??q.Active;if(e==null||e.Stamp.Revision!=u.LastOrder?.Revision||e.Issuance.Command.Kind!=PlayableCommandKind.Move&&e.Issuance.Command.Kind!=PlayableCommandKind.AttackMove)continue;
                bool waiting=WaitingForWreck(u.Id);if(waiting&&navigation.IsPending(u.Id))continue;
                bool closed=WreckRouteClosed(actor,e.Issuance.Command.Target);
                if(closed&&!waiting){if(!CommitInternalTactical(e.Issuance,new[]{u.Id},InternalTacticalKind.WreckWait,activationRevision:e.Stamp.Revision))wreckRouteRevision=-1;}
                else if(waiting&&!closed){if(!CommitInternalTactical(e.Issuance,new[]{u.Id},InternalTacticalKind.Combat,e.Issuance.Command.Target,e.Stamp.Revision))wreckRouteRevision=-1;}
            }
        }
    }
}

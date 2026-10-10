using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Stateless execution within an existing mission. Registry membership is durable;
    // paths and local support positions are current detached owner observations.
    public static class AiTacticalExecutor
    {
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private static double SegmentDistance(NavPoint p,NavPoint a,NavPoint b)
        {
            double dx=b.X-a.X,dz=b.Z-a.Z,length=dx*dx+dz*dz;
            double t=length==0?0:Math.Max(0,Math.Min(1,((p.X-a.X)*dx+(p.Z-a.Z)*dz)/length));
            return Distance(p,new NavPoint(a.X+t*dx,a.Z+t*dz));
        }
        public static bool SafeRoute(PlayableAiObservation o,PlayableRouteProof route,PlayableProfile p)
        {
            if(route==null||route.Owner!=o.Owner||route.Generation!=o.Generation||route.Tick!=o.Tick||route.GeometryRevision<=0||route.Path.Count==0||!route.Path.Last().Equals(route.Goal))return false;
            var actor=o.Entities.FirstOrDefault(u=>u.Id==route.UnitId&&u.Owner==o.Owner&&u.Health>0);
            if(actor==null||!actor.Position.Equals(route.Origin)||route.Radius!=PlayableUnitRules.Radius(p,actor.Kind))return false;
            if(route.Kind!=PlayableRouteTargetKind.FriendlyAnchor||!o.Entities.Any(u=>u.Id==route.TargetId&&u.Owner==o.Owner&&u.Health>0&&(u.Position.Equals(route.Target)||u.Id!=route.UnitId&&PlayableUnitRules.FollowGoal(p,u.Position,u.HullHeading).Equals(route.Target)||u.Id==route.UnitId&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.CurrentOrder.Destination.Equals(route.Target)))&&
                !o.Buildings.Any(b=>b.Id==route.TargetId&&b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.Position.Equals(route.Target)))return false;
            return SafePath(o,route.Origin,route.Path,route.Radius,p);
        }
        internal static bool SafePath(PlayableAiObservation o,NavPoint origin,IEnumerable<NavPoint> path,double radius,PlayableProfile p)
        {
            var enemies=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0).ToArray();var at=origin;
            foreach(var point in path)
            {
                if(o.Vision==null||!o.Vision.IsVisible(point)||enemies.Any(e=>SegmentDistance(e.Position,at,point)<=PlayableUnitRules.Range(p,e.Kind,e.Upgraded)+radius+PlayableUnitRules.Radius(p,e.Kind)))return false;
                at=point;
            }
            return true;
        }
        // A retreat may begin inside an enemy's weapon envelope. It must leave that
        // envelope monotonically, never enter a new one, and finish outside all of them.
        internal static bool WithdrawalPath(PlayableAiObservation o,NavPoint origin,IEnumerable<NavPoint> path,double radius,PlayableProfile p)
        {
            var enemies=o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&o.Vision!=null&&o.Vision.IsVisible(e.Position)&&AiRosterCatalog.Initial.For(e.Kind).targetTags.Contains("ground")).ToArray();
            var at=origin;var points=path.ToArray();if(points.Length==0)return false;
            foreach(var point in points)
            {
                if(o.Vision==null||!o.Vision.IsVisible(point))return false;
                foreach(var e in enemies)
                {
                    double range=PlayableUnitRules.Range(p,e.Kind,e.Upgraded)+radius+PlayableUnitRules.Radius(p,e.Kind);
                    double before=Distance(e.Position,at);
                    if(before<=range)
                    {if(Distance(e.Position,point)<before||SegmentDistance(e.Position,at,point)<before)return false;}
                    else if(SegmentDistance(e.Position,at,point)<=range)return false;
                }
                at=point;
            }
            return enemies.All(e=>Distance(e.Position,at)>PlayableUnitRules.Range(p,e.Kind,e.Upgraded)+radius+PlayableUnitRules.Radius(p,e.Kind));
        }
        public static bool WithdrawalRoute(PlayableAiObservation o,PlayableRouteProof r,PlayableProfile p)
        {
            if(r==null||r.Owner!=o.Owner||r.Generation!=o.Generation||r.Tick!=o.Tick||r.GeometryRevision<=0||r.Kind!=PlayableRouteTargetKind.FriendlyAnchor||r.Path.Count==0||!r.Path.Last().Equals(r.Goal))return false;
            var u=o.Entities.FirstOrDefault(e=>e.Id==r.UnitId&&e.Owner==o.Owner&&e.Health>0);
            if(u==null||!u.Position.Equals(r.Origin)||r.Radius!=PlayableUnitRules.Radius(p,u.Kind))return false;
            bool current=r.TargetId==u.Id&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.CurrentOrder.Destination.Equals(r.Target);
            bool home=o.Buildings.Any(b=>b.Id==r.TargetId&&b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.Position.Equals(r.Target)&&
                (b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Factory));
            return (current||home)&&WithdrawalPath(o,r.Origin,r.Path,r.Radius,p);
        }
        public static PlayableEntitySnapshot[] LocalThreats(PlayableAiObservation o,AiArmyState army,PlayableProfile p)
        {
            var members=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)).ToArray();
            return o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&o.Vision!=null&&o.Vision.IsVisible(e.Position)&&AiRosterCatalog.Initial.For(e.Kind).targetTags.Contains("ground")&&members.Any(u=>
                Distance(u.Position,e.Position)<=Math.Max(PlayableUnitRules.Range(p,e.Kind,e.Upgraded),PlayableUnitRules.Range(p,u.Kind,u.Upgraded))+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,e.Kind)&&
                (u.TargetId==e.Id||e.TargetId==u.Id||o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.VisibleEnemy&&r.TargetId==e.Id))))
                .OrderBy(e=>e.Id).ToArray();
        }
        public static bool Withdraw(PlayableAiObservation o,AiArmyState army,PlayableProfile p,AiProfile ai)
        {
            var own=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)).ToArray();
            return AiDefensePlanner.Force(LocalThreats(o,army,p),p,ai)>AiDefensePlanner.Force(own,p,ai)*ai.Value("armies.retreatThreatRatio");
        }
        internal static bool EffectiveWithdrawal(PlayableAiObservation o,AiArmyState army,PlayableEntitySnapshot u,IEnumerable<PlayableAiTraceRecord> records,PlayableProfile p,bool certified=true)
        {
            var order=u.CurrentOrder;var stamp=u.OrderStamp;
            bool arrived=order==null&&u.NavigationOutcome==NavigationOutcome.Arrived&&stamp?.Kind==PlayableCommandKind.Move;
            if(stamp==null||!arrived&&order==null||u.Owner!=o.Owner||!army.Members.Contains(u.Id)||army.Reinforcements.Contains(u.Id)||
                stamp.Origin!=PlayableOrderOrigin.Ai||stamp.Owner!=o.Owner||stamp.Generation!=o.Generation||stamp.UnitId!=u.Id||stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||stamp.Tick>o.Tick)return false;
            if(order!=null&&(order.Owner!=o.Owner||order.Generation!=o.Generation||order.UnitId!=u.Id||order.CommandSequence!=stamp.Sequence||order.IssuedTick!=stamp.Tick))return false;
            var kind=arrived||order.Kind==PlayableTacticalOrderKind.Move?PlayableCommandKind.Move:PlayableCommandKind.Attack;
            if(!arrived&&order.Kind!=PlayableTacticalOrderKind.Move&&order.Kind!=PlayableTacticalOrderKind.Attack||stamp.Kind!=kind)return false;
            if(!(records??Array.Empty<PlayableAiTraceRecord>()).Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Kind==kind&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.ReceiptIdentity?.Generation==o.Generation&&r.ReceiptIdentity.OwnerId==o.OwnerId))return false;
            if(!certified)return true;
            if(arrived)return o.RouteProofs.Any(r=>r.UnitId==u.Id&&o.Buildings.Any(b=>b.Id==r.TargetId)&&WithdrawalRoute(o,r,p)&&Distance(u.Position,r.Goal)<=p.Navigation.ArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind));
            return kind==PlayableCommandKind.Move?o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.TargetId==u.Id&&r.Target.Equals(order.Destination)&&r.Goal.Equals(order.Destination)&&WithdrawalRoute(o,r,p)):
                LocalThreats(o,army,p).Any(e=>e.Id==order.TargetId);
        }
        public static PlayableAiAction Withdrawal(PlayableAiObservation o,AiArmyState army,PlayableProfile p,AiProfile ai,long actionId,bool stalled=false)
        {
            if(army.OwnerId!=o.OwnerId||army.TacticalOwner!=AiArmyPlanner.Policy||army.Phase==AiArmyPhase.Disbanded)return null;
            if(army.Phase==AiArmyPhase.Recovering&&!Withdraw(o,army,p,ai))return null;
            bool active=army.Phase==AiArmyPhase.Retreating||army.Phase==AiArmyPhase.Recovering;
            if(!active&&!stalled&&!Withdraw(o,army,p,ai))return null;
            var members=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)).OrderBy(u=>u.Id).ToArray();
            foreach(var u in members)
            {
                if(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.TargetId==u.Id&&r.Target.Equals(u.CurrentOrder.Destination)&&WithdrawalRoute(o,r,p)))continue;
                var route=o.RouteProofs.Where(r=>r.UnitId==u.Id&&o.Buildings.Any(b=>b.Id==r.TargetId)&&WithdrawalRoute(o,r,p)).OrderBy(RouteLength).ThenBy(r=>r.TargetId).FirstOrDefault();
                if(route!=null)
                {
                    if(Distance(u.Position,route.Goal)<=p.Navigation.ArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)&&u.CurrentOrder==null&&u.NavigationOutcome==NavigationOutcome.Arrived)continue;
                    return new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,new[]{u.Id},route.Goal,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                }
                var enemy=LocalThreats(o,army,p).OrderBy(e=>Distance(u.Position,e.Position)).ThenBy(e=>e.Id).FirstOrDefault();
                if(enemy!=null&&!(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==enemy.Id))
                    return new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Attack,new[]{u.Id},enemy.Position,targetId:enemy.Id,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            }
            return null;
        }
        private static PlayableEntitySnapshot[] Core(PlayableAiObservation o,AiArmyState army)=>o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0).OrderBy(u=>u.Id).ToArray();
        public static void Observe(PlayableAiObservation o,AiArmyRegistry registry,long armyId,PlayableProfile p,IEnumerable<int> unavailable=null)
        {
            var army=registry.Capture().Armies.FirstOrDefault(a=>a.Id==armyId&&a.TacticalOwner==AiArmyPlanner.Policy&&a.Phase!=AiArmyPhase.Disbanded);
            if(army==null)return;
            var excluded=new HashSet<int>(unavailable??Array.Empty<int>());
            var free=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&registry.ArmyFor(u.Id)==0&&!excluded.Contains(u.Id)&&AiRosterCatalog.Initial.For(u.Kind).lineWeight+AiRosterCatalog.Initial.For(u.Kind).supportWeight>0).Select(u=>u.Id).ToArray();
            if(free.Length>0)registry.TryReinforce(o,army.Id,army.TacticalOwner,free);
            army=registry.Capture().Armies.Single(a=>a.Id==armyId);
            var core=Core(o,army);
            // Arrival uses the existing follow spacing/tolerance, not a new AI balance value.
            var joined=Ready(o,army,p).Where(u=>AiRosterCatalog.Initial.For(u.Kind).supportWeight>0).Select(u=>u.Id);
            registry.JoinReinforcements(armyId,army.TacticalOwner,joined);
        }
        public static PlayableEntitySnapshot[] Ready(PlayableAiObservation o,AiArmyState army,PlayableProfile p)
        {
            var core=Core(o,army);
            return o.Entities.Where(u=>army.Reinforcements.Contains(u.Id)&&core.Any(c=>
                Distance(u.Position,c.Position)<=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,c.Kind)&&
                o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==c.Id&&SafeRoute(o,r,p)&&
                    RouteLength(r)<=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,c.Kind)))).ToArray();
        }
        // Offense admits a ready wave through its effective attack. Recovery cannot
        // attack, so it establishes the same proven assembly through ordinary Follow.
        internal static PlayableAiAction RecoveryAssembly(PlayableAiObservation o,AiArmyState army,PlayableProfile p,long actionId,long repeat,IEnumerable<PlayableAiTraceRecord> records)
        {
            if(army.OwnerId!=o.OwnerId||army.TacticalOwner!=AiArmyPlanner.Policy||army.Phase!=AiArmyPhase.Recovering)return null;
            var core=Core(o,army);
            foreach(var unit in o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&army.Reinforcements.Contains(u.Id)).OrderBy(u=>u.Id))
            {
                var leader=unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow?core.FirstOrDefault(c=>c.Id==unit.CurrentOrder.TargetId):null;
                if(leader!=null&&AiArmyPlanner.AssemblyFollow(o,army,unit,records)&&o.RouteProofs.Any(r=>r.UnitId==unit.Id&&r.TargetId==leader.Id&&r.Target.Equals(PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading))&&r.Goal.Equals(r.Target)&&SafeRoute(o,r,p)))continue;
                if(unit.CurrentOrder!=null&&o.Tick-unit.CurrentOrder.IssuedTick<repeat)continue;
                var route=o.RouteProofs.Where(r=>r.UnitId==unit.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&core.Any(c=>c.Id==r.TargetId&&r.Target.Equals(PlayableUnitRules.FollowGoal(p,c.Position,c.HullHeading)))&&r.Goal.Equals(r.Target)&&SafeRoute(o,r,p))
                    .OrderBy(RouteLength).ThenBy(r=>r.TargetId).FirstOrDefault();
                if(route==null)continue;
                return new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Follow,new[]{unit.Id},targetId:route.TargetId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            }
            return null;
        }
        // A broad cohesion proof can be Ready while the actual native Follow spacing
        // still needs correction. Finish the accepted gather leg before line promotion.
        internal static PlayableAiAction ArrivalAssembly(PlayableAiObservation o,AiArmyState army,PlayableProfile p,long actionId,IEnumerable<PlayableAiTraceRecord> records=null)
        {
            if(army.OwnerId!=o.OwnerId||army.TacticalOwner!=AiArmyPlanner.Policy||
                army.Phase!=AiArmyPhase.Advancing&&army.Phase!=AiArmyPhase.Engaging)return null;
            var core=Core(o,army);
            var applied=(records??Array.Empty<PlayableAiTraceRecord>()).ToArray();
            bool AssemblyMove(PlayableEntitySnapshot u)
            {
                var stamp=u.OrderStamp;
                return stamp!=null&&stamp.UnitId==u.Id&&stamp.Owner==o.Owner&&stamp.Generation==o.Generation&&stamp.Origin==PlayableOrderOrigin.Ai&&
                    stamp.Kind==PlayableCommandKind.Move&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&stamp.ActionId>0&&stamp.JobId>0&&
                    applied.Any(r=>r.Policy==army.TacticalOwner&&r.OwnerId==o.OwnerId&&r.Status==PlayableAiDeliveryStatus.Applied&&r.Kind==PlayableCommandKind.Move&&
                        r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.SourceIdentity==stamp.Source&&AiStateWire.CallbackGenerationMatches(r,o.Generation));
            }
            PlayableRouteProof FollowRoute(PlayableEntitySnapshot u,PlayableEntitySnapshot leader)=>o.RouteProofs.FirstOrDefault(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==leader.Id&&
                r.Target.Equals(PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading))&&r.Goal.Equals(r.Target)&&SafeRoute(o,r,p));
            var candidates=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&!u.Held&&army.Members.Contains(u.Id)&&army.Reinforcements.Contains(u.Id)).ToArray();
            var choices=candidates.Where(u=>
                u.CurrentOrder==null&&u.NavigationOutcome==NavigationOutcome.Arrived&&AssemblyMove(u))
                .SelectMany(u=>core.Where(c=>Distance(u.Position,c.Position)>p.FollowDistance+p.FollowArrivalTolerance&&
                    Distance(u.Position,c.Position)<=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,c.Kind)+p.Navigation.ArrivalTolerance)
                    .Select(c=>new{Unit=u,Leader=c,Route=FollowRoute(u,c)}))
                .Where(x=>x.Route!=null).OrderBy(x=>x.Unit.Id).ThenBy(x=>RouteLength(x.Route)).ThenBy(x=>x.Leader.Id).ToArray();
            var first=choices.FirstOrDefault();if(first==null)return null;
            // Complete this one accepted assembly obligation together. An arrived member
            // provides the genuine correction trigger; compatible siblings need not wait
            // for separate attention windows. Existing valid Follow and human orders stay live.
            var ids=candidates.Where(u=>(u.CurrentOrder==null&&(u.OrderStamp==null||AssemblyMove(u))||
                u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&AssemblyMove(u)&&u.OrderStamp.Sequence==u.CurrentOrder.CommandSequence)&&
                FollowRoute(u,first.Leader)!=null).Select(u=>u.Id).OrderBy(id=>id).ToArray();
            return new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Follow,ids,targetId:first.Leader.Id,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        public static PlayableAiAction Propose(PlayableAiObservation o,AiArmyState army,PlayableProfile p,long actionId)
        {
            var core=Core(o,army);var ready=new HashSet<int>(Ready(o,army,p).Select(u=>u.Id));
            PlayableAiAction Action(PlayableCommandKind kind,int[] ids,NavPoint target=default,int targetId=0)=>new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,kind,ids,target,targetId:targetId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            foreach(var unit in o.Entities.Where(u=>army.Reinforcements.Contains(u.Id)&&!ready.Contains(u.Id)).OrderBy(u=>u.Id))
            {
                // Keep an admitted, currently certified leg until arrival. A moving
                // core must not repeatedly restart it and starve the other wave units.
                if(unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&o.RouteProofs.Any(r=>r.UnitId==unit.Id&&r.TargetId==unit.Id&&r.Target.Equals(unit.CurrentOrder.Destination)&&SafeRoute(o,r,p)))continue;
                var currentLeader=unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow?core.FirstOrDefault(c=>c.Id==unit.CurrentOrder.TargetId):null;
                if(currentLeader!=null&&o.RouteProofs.Any(r=>r.UnitId==unit.Id&&r.TargetId==currentLeader.Id&&r.Target.Equals(PlayableUnitRules.FollowGoal(p,currentLeader.Position,currentLeader.HullHeading))&&r.Goal.Equals(r.Target)&&SafeRoute(o,r,p)))continue;
                var close=core.Where(c=>(unit.NavigationOutcome==NavigationOutcome.Arrived||unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow)&&Distance(unit.Position,c.Position)<=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,unit.Kind)+PlayableUnitRules.Radius(p,c.Kind)+p.Navigation.ArrivalTolerance)
                    .Where(c=>o.RouteProofs.Any(r=>r.UnitId==unit.Id&&r.TargetId==c.Id&&r.Target.Equals(PlayableUnitRules.FollowGoal(p,c.Position,c.HullHeading))&&r.Goal.Equals(r.Target)&&SafeRoute(o,r,p))).OrderBy(c=>Distance(unit.Position,c.Position)).ThenBy(c=>c.Id).FirstOrDefault();
                if(close!=null)
                {
                    // This band only corrects native arrival error. Ready/actual joining
                    // keeps the stricter original cohesion bound above.
                    return Action(PlayableCommandKind.Follow,new[]{unit.Id},targetId:close.Id);
                }
                var routes=o.RouteProofs.Where(r=>r.UnitId==unit.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&core.Any(c=>c.Id==r.TargetId&&c.Position.Equals(r.Target))&&SafeRoute(o,r,p))
                    .OrderBy(r=>RouteLength(r)).ThenBy(r=>r.TargetId).ToArray();
                // If every line anchor is in visible weapon range, gather at a current
                // native-proven ready home/production approach instead of a solo front.
                var route=routes.FirstOrDefault()??o.RouteProofs.Where(r=>r.UnitId==unit.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&
                    o.Buildings.Any(b=>b.Id==r.TargetId&&b.Owner==o.Owner&&b.Phase==ConstructionPhase.Ready)&&SafeRoute(o,r,p)).OrderBy(RouteLength).ThenBy(r=>r.TargetId).FirstOrDefault();
                if(route==null)
                {if(unit.CurrentOrder!=null)return Action(PlayableCommandKind.Stop,new[]{unit.Id});continue;}
                if(unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&unit.CurrentOrder.Destination.Equals(route.Goal))continue;
                if(unit.CurrentOrder==null&&Distance(unit.Position,route.Goal)<=p.Navigation.ArrivalTolerance+PlayableUnitRules.Radius(p,unit.Kind))continue;
                var wave=o.Entities.Where(u=>army.Reinforcements.Contains(u.Id)&&!ready.Contains(u.Id)&&
                    !(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.CurrentOrder.Destination.Equals(route.Goal))&&
                    o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.TargetId==route.TargetId&&r.Goal.Equals(route.Goal)&&SafeRoute(o,r,p))).Select(u=>u.Id).OrderBy(x=>x).ToArray();
                return Action(PlayableCommandKind.Move,wave,route.Goal);
            }
            foreach(var gun in o.Entities.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.For(u.Kind).supportWeight>0).OrderBy(u=>u.Id))
            {
                var fact=o.ArtillerySupport.FirstOrDefault(f=>f.UnitId==gun.Id&&f.Generation==o.Generation&&f.Tick==o.Tick);
                if(fact==null)continue;
                if(!fact.Threatened&&fact.Supported&&fact.UsefulShot||!fact.Position.HasValue)
                {if(gun.CurrentOrder!=null)return Action(PlayableCommandKind.Stop,new[]{gun.Id});continue;}
                var point=fact.Position.Value;
                if(Distance(gun.Position,point)<=p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,gun.Kind))
                {if(gun.CurrentOrder!=null)return Action(PlayableCommandKind.Stop,new[]{gun.Id});continue;}
                if(gun.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&gun.CurrentOrder.Destination.Equals(point))continue;
                return Action(PlayableCommandKind.Move,new[]{gun.Id},point);
            }
            return null;
        }
        private static double RouteLength(PlayableRouteProof route)
        {double length=0;var at=route.Origin;foreach(var point in route.Path){length+=Distance(at,point);at=point;}return length;}
        internal static PlayableRouteRequest[] RouteRequests(IEnumerable<PlayableEntitySnapshot> entities,IEnumerable<PlayableBuildingSnapshot> buildings,AiArmyRegistryState state,PlayableOwner owner,long generation,long tick,PlayableProfile p)
        {
            if(state==null||state.Generation!=generation)return Array.Empty<PlayableRouteRequest>();
            var own=entities.Where(u=>u.Owner==owner&&u.Health>0).ToArray();
            var army=state.Armies.FirstOrDefault(a=>a.TacticalOwner==AiArmyPlanner.Policy&&a.Phase!=AiArmyPhase.Disbanded);
            if(army==null)return Array.Empty<PlayableRouteRequest>();
            var core=own.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0).ToArray();
            var assigned=new HashSet<int>(state.Armies.SelectMany(a=>a.Members));
            return own.Where(u=>(army.Members.Contains(u.Id)||!assigned.Contains(u.Id))&&AiRosterCatalog.Initial.For(u.Kind).lineWeight+AiRosterCatalog.Initial.For(u.Kind).supportWeight>0)
                .OrderBy(u=>u.Id).SelectMany(u=>(army.Reinforcements.Contains(u.Id)||!assigned.Contains(u.Id)?core.OrderBy(c=>c.Id).SelectMany(c=>new[]{new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.FriendlyAnchor,c.Id,generation,tick,u.Position,PlayableUnitRules.Radius(p,u.Kind),c.Position),new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.FriendlyAnchor,c.Id,generation,tick,u.Position,PlayableUnitRules.Radius(p,u.Kind),PlayableUnitRules.FollowGoal(p,c.Position,c.HullHeading))}):Enumerable.Empty<PlayableRouteRequest>())
                    .Concat(buildings.Where(b=>b.Owner==owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Factory)).OrderBy(b=>b.Id)
                        .Select(b=>new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.FriendlyAnchor,b.Id,generation,tick,u.Position,PlayableUnitRules.Radius(p,u.Kind),b.Position)))
                    .Concat(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move?new[]{new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.FriendlyAnchor,u.Id,generation,tick,u.Position,PlayableUnitRules.Radius(p,u.Kind),u.CurrentOrder.Destination)}:Array.Empty<PlayableRouteRequest>())).ToArray();
        }
        // Between strategic decisions, certify only an already effective assembly
        // Follow leg. It disappears on promotion and never solves enemy/all-core routes.
        internal static PlayableRouteRequest[] AssemblyRouteRequests(IEnumerable<PlayableEntitySnapshot> entities,AiArmyRegistryState state,PlayableOwner owner,long generation,long tick,PlayableProfile p)
        {
            if(state==null||state.Generation!=generation)return Array.Empty<PlayableRouteRequest>();
            var own=entities.Where(u=>u.Owner==owner&&u.Health>0).ToArray();var requests=new List<PlayableRouteRequest>();
            foreach(var army in state.Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded&&a.TacticalOwner==AiArmyPlanner.Policy))
            foreach(var unit in own.Where(u=>army.Reinforcements.Contains(u.Id)&&army.Members.Contains(u.Id)&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow))
            {
                var order=unit.CurrentOrder;var stamp=unit.OrderStamp;
                if(stamp==null||stamp.UnitId!=unit.Id||stamp.Owner!=owner||stamp.Generation!=generation||stamp.Origin!=PlayableOrderOrigin.Ai||stamp.Kind!=PlayableCommandKind.Follow||
                    stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||order.Owner!=owner||order.Generation!=generation||order.UnitId!=unit.Id||
                    order.CommandSequence!=stamp.Sequence||order.IssuedTick!=stamp.Tick||stamp.Tick>tick)continue;
                var leader=own.FirstOrDefault(c=>c.Id==order.TargetId&&c.Id!=unit.Id&&army.Members.Contains(c.Id)&&!army.Reinforcements.Contains(c.Id)&&AiRosterCatalog.Initial.For(c.Kind).lineWeight>0);
                if(leader==null)continue;
                requests.Add(new PlayableRouteRequest(unit.Id,PlayableRouteTargetKind.FriendlyAnchor,leader.Id,generation,tick,unit.Position,PlayableUnitRules.Radius(p,unit.Kind),PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading)));
            }
            foreach(var army in state.Armies.Where(a=>a.Phase==AiArmyPhase.Retreating&&a.TacticalOwner==AiArmyPlanner.Policy))
            foreach(var unit in own.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move))
            {
                var order=unit.CurrentOrder;var stamp=unit.OrderStamp;
                if(stamp==null||stamp.UnitId!=unit.Id||stamp.Owner!=owner||stamp.Generation!=generation||stamp.Origin!=PlayableOrderOrigin.Ai||stamp.Kind!=PlayableCommandKind.Move||
                    stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||order.Owner!=owner||order.Generation!=generation||order.UnitId!=unit.Id||order.CommandSequence!=stamp.Sequence||order.IssuedTick!=stamp.Tick||stamp.Tick>tick)continue;
                requests.Add(new PlayableRouteRequest(unit.Id,PlayableRouteTargetKind.FriendlyAnchor,unit.Id,generation,tick,unit.Position,PlayableUnitRules.Radius(p,unit.Kind),order.Destination));
            }
            return requests.OrderBy(r=>r.UnitId).ToArray();
        }
    }
}

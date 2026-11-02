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
#if UNITY_INCLUDE_TESTS
        // Observation-only audit hook; absent from Player/release, no cache/state effect.
        internal static Action<PlayableAiObservation,PlayableRouteProof,PlayableProfile,bool> RouteSafetyAuditForQa;
#endif
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private static double SegmentDistance(NavPoint p,NavPoint a,NavPoint b)
        {
            double dx=b.X-a.X,dz=b.Z-a.Z,length=dx*dx+dz*dz;
            double t=length==0?0:Math.Max(0,Math.Min(1,((p.X-a.X)*dx+(p.Z-a.Z)*dz)/length));
            return Distance(p,new NavPoint(a.X+t*dx,a.Z+t*dz));
        }
        public static bool SafeRoute(PlayableAiObservation o,PlayableRouteProof route,PlayableProfile p,AiKnowledgeState knowledge=null)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            QaAiRouteAudit.Observe(o,route,p,false);
#endif
#if UNITY_INCLUDE_TESTS
            RouteSafetyAuditForQa?.Invoke(o,route,p,false);
#endif
            if(route==null||route.Owner!=o.Owner||route.Generation!=o.Generation||route.Tick!=o.Tick||route.GeometryRevision<=0||route.Path.Count==0||!route.Path.Last().Equals(route.Goal))return false;
            var actor=o.Entities.FirstOrDefault(u=>u.Id==route.UnitId&&u.Owner==o.Owner&&u.Health>0);
            if(actor==null||!actor.Position.Equals(route.Origin)||route.Radius!=PlayableUnitRules.Radius(p,actor.Kind))return false;
            if(route.Kind!=PlayableRouteTargetKind.FriendlyAnchor||!o.Entities.Any(u=>u.Id==route.TargetId&&u.Owner==o.Owner&&u.Health>0&&(u.Position.Equals(route.Target)||u.Id!=route.UnitId&&PlayableUnitRules.FollowGoal(p,u.Position,u.HullHeading).Equals(route.Target)||u.Id==route.UnitId&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.CurrentOrder.Destination.Equals(route.Target)))&&
                !o.Buildings.Any(b=>b.Id==route.TargetId&&b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.Position.Equals(route.Target)))return false;
            return SafePathForUnit(o,route.Origin,route.Path,route.Radius,p,actor.Kind==PlayableEntityKind.Hawk)&&AiStaticDefenseSafety.Withdrawal(o,p,actor,route.Path,knowledge);
        }
        internal static bool SafePath(PlayableAiObservation o,NavPoint origin,IEnumerable<NavPoint> path,double radius,PlayableProfile p)=>SafePathForUnit(o,origin,path,radius,p,false);
        internal static bool SafePathForUnit(PlayableAiObservation o,NavPoint origin,IEnumerable<NavPoint> path,double radius,PlayableProfile p,bool airborne)
        {
            var enemies=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0&&AiRosterCatalog.Initial.Targets(u.Kind,airborne?"air":"ground")).ToArray();var at=origin;
            foreach(var point in path)
            {
                if(o.Vision==null||enemies.Any(e=>SegmentDistance(e.Position,at,point)<=PlayableUnitRules.Range(p,e.Kind,e.Upgraded)+radius+PlayableUnitRules.Radius(p,e.Kind)))return false;
                at=point;
            }
            return true;
        }
        // A retreat may begin inside an enemy's weapon envelope. It must leave that
        // envelope monotonically, never enter a new one, and finish outside all of them.
        internal static bool WithdrawalPath(PlayableAiObservation o,NavPoint origin,IEnumerable<NavPoint> path,double radius,PlayableProfile p)=>WithdrawalPathForUnit(o,origin,path,radius,p,false);
        internal static bool WithdrawalPathForUnit(PlayableAiObservation o,NavPoint origin,IEnumerable<NavPoint> path,double radius,PlayableProfile p,bool airborne)
        {
            var enemies=o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&o.Vision!=null&&o.Vision.IsVisible(e.Position)&&AiRosterCatalog.Initial.Targets(e.Kind,airborne?"air":"ground")).ToArray();
            var at=origin;var points=path.ToArray();if(points.Length==0)return false;
            foreach(var point in points)
            {
                if(o.Vision==null)return false;
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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            QaAiRouteAudit.Observe(o,r,p,true);
#endif
#if UNITY_INCLUDE_TESTS
            RouteSafetyAuditForQa?.Invoke(o,r,p,true);
#endif
            if(r==null||r.Owner!=o.Owner||r.Generation!=o.Generation||r.Tick!=o.Tick||r.GeometryRevision<=0||r.Kind!=PlayableRouteTargetKind.FriendlyAnchor||r.Path.Count==0||!r.Path.Last().Equals(r.Goal))return false;
            var u=o.Entities.FirstOrDefault(e=>e.Id==r.UnitId&&e.Owner==o.Owner&&e.Health>0);
            if(u==null||!u.Position.Equals(r.Origin)||r.Radius!=PlayableUnitRules.Radius(p,u.Kind))return false;
            bool current=r.TargetId==u.Id&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.CurrentOrder.Destination.Equals(r.Target);
            bool home=o.Buildings.Any(b=>b.Id==r.TargetId&&b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.Position.Equals(r.Target)&&
                (b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.RepairTower));
            return (current||home)&&WithdrawalPathForUnit(o,r.Origin,r.Path,r.Radius,p,u.Kind==PlayableEntityKind.Hawk);
        }
        public static PlayableEntitySnapshot[] LocalThreats(PlayableAiObservation o,AiArmyState army,PlayableProfile p)
        {
            var members=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)).ToArray();
            return o.Entities.Where(e=>o.IsHostile(e.Owner)&&e.Health>0&&o.Vision!=null&&o.Vision.IsVisible(e.Position)&&members.Any(u=>AiRosterCatalog.Initial.Targets(e.Kind,u.Kind==PlayableEntityKind.Hawk?"air":"ground")&&
                Distance(u.Position,e.Position)<=Math.Max(PlayableUnitRules.Range(p,e.Kind,e.Upgraded),PlayableUnitRules.Range(p,u.Kind,u.Upgraded))+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,e.Kind)&&
                (u.TargetId==e.Id||e.TargetId==u.Id||o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.VisibleEnemy&&r.TargetId==e.Id))))
                .OrderBy(e=>e.Id).ToArray();
        }
        public static bool Withdraw(PlayableAiObservation o,AiArmyState army,PlayableProfile p,AiProfile ai,double? threatRatio=null)
        {
            var own=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)).ToArray();
            return AiDefensePlanner.Force(LocalThreats(o,army,p),p,ai)>AiDefensePlanner.Force(own,p,ai)*(threatRatio??ai.Value("armies.retreatThreatRatio"));
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
            if(!(records??Array.Empty<PlayableAiTraceRecord>()).Any(r=>r.Policy==army.TacticalOwner&&r.Kind==kind&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.ReceiptIdentity?.Generation==o.Generation&&r.ReceiptIdentity.OwnerId==o.OwnerId))return false;
            if(!certified)return true;
            if(arrived)return o.RouteProofs.Any(r=>r.UnitId==u.Id&&o.Buildings.Any(b=>b.Id==r.TargetId)&&WithdrawalRoute(o,r,p)&&Distance(u.Position,r.Goal)<=p.Navigation.ArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind));
            return kind==PlayableCommandKind.Move?o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.TargetId==u.Id&&r.Target.Equals(order.Destination)&&r.Goal.Equals(order.Destination)&&WithdrawalRoute(o,r,p)):
                LocalThreats(o,army,p).Any(e=>e.Id==order.TargetId);
        }
        private static NavPoint? WithdrawalReservation(PlayableEntitySnapshot other)
        {
            var pending=other.Queue?.Pending;
            return other.Queue?.PendingAiRouteViable==true&&pending?.Kind==PlayableCommandKind.Move&&pending.Anchor.HasValue?pending.Anchor.Value.Position:
                other.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move?other.CurrentOrder.Destination:!other.Moving?(NavPoint?)other.Position:null;
        }
        private static double WithdrawalSpacing(PlayableProfile p,double radius,PlayableEntitySnapshot other)
            =>Math.Max(p.Navigation.ArrivalSlotSpacing,4*Math.Max(radius,PlayableUnitRules.Radius(p,other.Kind))+2*p.Navigation.ArrivalTolerance+2*p.Navigation.LocalGridCell);
        internal static NavPoint WithdrawalDestination(PlayableAiObservation o,PlayableRouteProof route,PlayableProfile p)
        {
            if(route.Path.Count<2||o.Entities.Any(u=>u.Id==route.UnitId&&u.Kind==PlayableEntityKind.Hawk))return route.Goal;
            // Prefixes get an exposed body out of fire without waiting for a
            // second solve of the full bent route. Once outside every observed
            // weapon envelope, commit the final home leg instead of repeatedly
            // installing intermediate arrivals while the recovery clock runs.
            if(WithdrawalPath(o,route.Origin,new[]{route.Origin},route.Radius,p))return route.Goal;
            var from=route.Origin;var first=route.Path[0];double dx=first.X-from.X,dz=first.Z-from.Z,a=dx*dx+dz*dz,t=1;
            if(a<=p.Navigation.ArrivalTolerance*p.Navigation.ArrivalTolerance)return route.Goal;
            var others=o.Entities.Where(u=>u.Id!=route.UnitId&&u.Health>0&&u.Kind!=PlayableEntityKind.Hawk).ToArray();
            var goal=first;
            // Each clip moves below one forbidden interval on the certified segment.
            // At most one interval per observed body is crossed; no search/nav work.
            for(int pass=0;pass<=others.Length;pass++)
            {
                bool clipped=false;
                foreach(var other in others)
                {
                    var reserved=WithdrawalReservation(other);if(!reserved.HasValue)continue;
                    double spacing=WithdrawalSpacing(p,route.Radius,other);if(Distance(goal,reserved.Value)>=spacing)continue;
                    double x=from.X-reserved.Value.X,z=from.Z-reserved.Value.Z,b=2*(x*dx+z*dz),c=x*x+z*z-spacing*spacing;
                    double entry=(-b-Math.Sqrt(Math.Max(0,b*b-4*a*c)))/(2*a);
                    // Numerical separation from a closed circle, not a gameplay margin.
                    t=Math.Min(t,entry-1e-9);if(t<=0)return route.Goal;
                    goal=new NavPoint(from.X+t*dx,from.Z+t*dz);clipped=true;
                }
                if(!clipped)return WithdrawalLeg(o,route,p,goal)?goal:route.Goal;
            }
            return route.Goal;
        }
        // A current certified escape can be executed one clear segment at a time.
        // Completion still uses the original home proof, never this intermediate leg.
        internal static bool WithdrawalLeg(PlayableAiObservation o,PlayableRouteProof route,PlayableProfile p,NavPoint goal)
        {
            if(!WithdrawalRoute(o,route,p))return false;
            if(goal.Equals(route.Goal))return true;
            if(o.Entities.Any(u=>u.Id==route.UnitId&&u.Kind==PlayableEntityKind.Hawk)||route.Path.Count<2||SegmentDistance(goal,route.Origin,route.Path[0])>1e-9||
                Distance(route.Origin,goal)<=p.Navigation.ArrivalTolerance||
                !WithdrawalPath(o,route.Origin,new[]{goal},route.Radius,p))return false;
            foreach(var other in o.Entities.Where(u=>u.Id!=route.UnitId&&u.Health>0&&u.Kind!=PlayableEntityKind.Hawk))
            {
                var reserved=WithdrawalReservation(other);if(!reserved.HasValue)continue;
                double spacing=WithdrawalSpacing(p,route.Radius,other);
                if(Distance(goal,reserved.Value)<spacing)return false;
            }
            return true;
        }
        internal static PlayableAiAction ClearWithdrawalCorridor(PlayableAiObservation o,AiArmyState army,PlayableProfile p,long actionId,IEnumerable<PlayableAiTraceRecord> records,AiKnowledgeState knowledge=null)
        {
            if(army.Phase!=AiArmyPhase.Retreating||LocalThreats(o,army,p).Length!=0)return null;
            var core=Core(o,army);
            if(core.Length<army.MinimumViableForce||core.Any(u=>u.Held||u.Departing||u.OrderStamp?.Origin==PlayableOrderOrigin.Human))return null;
            bool Contains(PlayableRouteProof path,NavPoint point,double radius)
            {
                var from=path.Origin;
                foreach(var to in path.Path){if(SegmentDistance(point,from,to)<radius)return true;from=to;}
                return false;
            }
            bool ClearsBodies(PlayableEntitySnapshot peer,PlayableRouteProof path)
            {
                foreach(var other in core.Where(u=>u.Id!=peer.Id&&u.Kind!=PlayableEntityKind.Hawk))
                {
                    double clearance=PlayableUnitRules.Radius(p,peer.Kind)+PlayableUnitRules.Radius(p,other.Kind)+p.Navigation.ArrivalTolerance;
                    var from=path.Origin;double before=Distance(other.Position,from);
                    foreach(var to in path.Path)
                    {
                        double nearest=SegmentDistance(other.Position,from,to),after=Distance(other.Position,to);
                        if(before<=clearance?(nearest<before||after<before):nearest<clearance)return false;
                        from=to;before=after;
                    }
                    if(before<=clearance)return false;
                }
                return true;
            }
            foreach(var mover in core.Where(u=>u.Moving&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.Queue?.PendingAiRouteViable!=true&&
                u.Kind!=PlayableEntityKind.Hawk&&u.OrderStamp?.Tick>=army.CreatedTick&&EffectiveWithdrawal(o,army,u,records,p,certified:false)))
            {
                var route=o.RouteProofs.FirstOrDefault(r=>r.UnitId==mover.Id&&r.TargetId==mover.Id&&r.Target.Equals(mover.CurrentOrder.Destination)&&r.Goal.Equals(r.Target)&&WithdrawalRoute(o,r,p));
                if(route==null)continue;
                foreach(var peer in core.Where(u=>u.Id!=mover.Id&&u.Kind!=PlayableEntityKind.Hawk&&u.Queue?.PendingAiRouteViable!=true&&
                    u.OrderStamp?.Tick>=army.CreatedTick&&(!u.Moving&&u.CurrentOrder==null&&u.NavigationOutcome==NavigationOutcome.Arrived&&EffectiveWithdrawal(o,army,u,records,p)||
                    u.Moving&&u.Id>mover.Id&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&EffectiveWithdrawal(o,army,u,records,p,certified:false))))
                {
                    // An applied Follow wave is already travelling to this arrived
                    // recovery anchor. Keep it stable until real assembly; neither
                    // this hold nor the receipt gives the wave arrival credit.
                    if(peer.Id==army.LeaderId&&!peer.Moving&&o.Entities.Any(f=>army.Reinforcements.Contains(f.Id)&&
                        !f.Held&&!f.Departing&&f.CurrentOrder?.TargetId==peer.Id&&f.OrderStamp?.Tick>=army.CreatedTick&&
                        AiArmyPlanner.AssemblyFollow(o,army,f,records)))continue;
                    double clearance=PlayableUnitRules.Radius(p,mover.Kind)+PlayableUnitRules.Radius(p,peer.Kind)+p.Navigation.ArrivalTolerance;
                    if(!Contains(route,peer.Position,clearance))continue;
                    if(peer.Moving&&!o.RouteProofs.Any(r=>r.UnitId==peer.Id&&r.TargetId==peer.Id&&r.Target.Equals(peer.CurrentOrder.Destination)&&r.Goal.Equals(r.Target)&&
                        WithdrawalRoute(o,r,p)&&Contains(r,mover.Position,clearance)))continue;
                    // Only an existing full safe home proof can vacate this arrived
                    // body. It remains original core and must really arrive again;
                    // no arrival credit, new clock, or lower-navigation bypass.
                    var home=o.RouteProofs.Where(r=>r.UnitId==peer.Id&&o.Buildings.Any(b=>b.Id==r.TargetId)&&WithdrawalRoute(o,r,p)&&AiStaticDefenseSafety.Withdrawal(o,p,peer,r.Path,knowledge)&&
                        !Contains(route,r.Goal,clearance)&&ClearsBodies(peer,r)&&Distance(peer.Position,r.Goal)>p.Navigation.ArrivalTolerance+PlayableUnitRules.Radius(p,peer.Kind))
                        .OrderBy(RouteLength).ThenBy(r=>r.TargetId).FirstOrDefault();
                    if(home!=null)return new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,new[]{peer.Id},home.Goal,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                }
            }
            return null;
        }
        public static PlayableAiAction Withdrawal(PlayableAiObservation o,AiArmyState army,PlayableProfile p,AiProfile ai,long actionId,bool stalled=false,double? threatRatio=null,IEnumerable<PlayableAiTraceRecord> records=null,AiKnowledgeState knowledge=null,bool prioritizeLeader=false)
        {
            if(army.OwnerId!=o.OwnerId||!AiArmyPlanner.IsArmyPolicy(army.TacticalOwner)||army.Phase==AiArmyPhase.Disbanded)return null;
            if(army.Phase==AiArmyPhase.Recovering&&!Withdraw(o,army,p,ai,threatRatio))return null;
            bool active=army.Phase==AiArmyPhase.Retreating||army.Phase==AiArmyPhase.Recovering;
            if(!active&&!stalled&&!Withdraw(o,army,p,ai,threatRatio))return null;
            var clearing=ClearWithdrawalCorridor(o,army,p,actionId,records,knowledge);
            if(clearing!=null)return clearing;
            // Enemy target IDs are private. Rank exposure using only current visible
            // threat positions/ranges and our own health, so an exposed body does not
            // keep advancing under fire while safer lower IDs take their turns.
            var threats=LocalThreats(o,army,p);
            double Exposure(PlayableEntitySnapshot u)=>threats.Where(e=>AiRosterCatalog.Initial.Targets(e.Kind,u.Kind==PlayableEntityKind.Hawk?"air":"ground"))
                .Select(e=>Distance(u.Position,e.Position)-PlayableUnitRules.Range(p,e.Kind,e.Upgraded)-PlayableUnitRules.Radius(p,u.Kind)-PlayableUnitRules.Radius(p,e.Kind)).DefaultIfEmpty(double.PositiveInfinity).Min();
            // Exposed bodies still leave first. Among safe bodies, establish the
            // bounded repair rendezvous before spending attention on later peers;
            // otherwise each intermediate leader leg waits behind another wave.
            var members=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id))
                .OrderBy(u=>Exposure(u)>0).ThenBy(u=>prioritizeLeader&&u.Id==army.LeaderId?0:1)
                .ThenBy(u=>PlayableUnitRules.HealthRatio(p,u)).ThenBy(Exposure).ThenBy(u=>u.Id).ToArray();
            PlayableAiAction lastResort=null;
            foreach(var u in members)
            {
                // Complete the bounded rendezvous before moving its settled leader
                // on to another home leg. Once the client is physically safe and
                // near, both can continue. This grants no home or membership credit.
                if(prioritizeLeader&&u.Id==army.LeaderId&&!u.Moving&&u.CurrentOrder==null&&u.NavigationOutcome==NavigationOutcome.Arrived&&
                    Exposure(u)>0&&AiStaticDefenseSafety.Withdrawal(o,p,u,new[]{u.Position},knowledge)&&
                    o.Entities.Any(f=>army.Reinforcements.Contains(f.Id)&&!f.Held&&!f.Departing&&f.CurrentOrder?.TargetId==u.Id&&
                        f.OrderStamp?.Tick>=army.CreatedTick&&AiArmyPlanner.AssemblyFollow(o,army,f,records)&&
                        !AiRepairUsePolicy.ReadyForOriginalLeader(o,p,army,f,knowledge)))continue;
                // A current mission's accepted withdrawal may still be installing.
                // Replacing that exact viable pending route restarts its real work.
                if(active&&u.OrderStamp?.Tick>=army.CreatedTick&&AiRepairUsePolicy.OwnedPendingWithdrawal(o,u,army.TacticalOwner,records))continue;
                bool failedMove=!u.Moving&&(u.NavigationOutcome==NavigationOutcome.Blocked||u.NavigationOutcome==NavigationOutcome.Unreachable||u.NavigationOutcome==NavigationOutcome.Stopped)&&
                    u.Queue?.PendingAiRouteViable!=true&&EffectiveWithdrawal(o,army,u,records,p,certified:false);
                if(!failedMove&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.TargetId==u.Id&&r.Target.Equals(u.CurrentOrder.Destination)&&WithdrawalRoute(o,r,p)))continue;
                var choice=o.RouteProofs.Where(r=>r.UnitId==u.Id&&o.Buildings.Any(b=>b.Id==r.TargetId)&&WithdrawalRoute(o,r,p))
                    .Select(r=>new{Route=r,Goal=WithdrawalDestination(o,r,p)})
                    .Where(x=>!failedMove||!x.Goal.Equals(u.CurrentOrder.Destination))
                    .OrderBy(x=>x.Route.Path.Count>1&&x.Goal.Equals(x.Route.Goal)).ThenBy(x=>RouteLength(x.Route)).ThenBy(x=>x.Route.TargetId).FirstOrDefault();
                if(choice!=null)
                {
                    if(Distance(u.Position,choice.Route.Goal)<=p.Navigation.ArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)&&u.CurrentOrder==null&&u.NavigationOutcome==NavigationOutcome.Arrived)continue;
                    return new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,new[]{u.Id},choice.Goal,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                }
                var enemy=LocalThreats(o,army,p).OrderBy(e=>Distance(u.Position,e.Position)).ThenBy(e=>e.Id).FirstOrDefault();
                // An exposed member without an escape must not consume every
                // attention window while another member has a certified way out.
                if(lastResort==null&&enemy!=null&&!(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==enemy.Id))
                    lastResort=new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Attack,new[]{u.Id},enemy.Position,targetId:enemy.Id,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            }
            return lastResort;
        }
        private static PlayableEntitySnapshot[] Core(PlayableAiObservation o,AiArmyState army)=>o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0).OrderBy(u=>u.Id).ToArray();
        internal static bool IsCurrentAppliedDefenseAssemblyOrder(PlayableAiObservation o,PlayableEntitySnapshot unit,IEnumerable<PlayableAiTraceRecord> records)
        {
            var order=unit?.CurrentOrder;var stamp=unit?.OrderStamp;
            var kind=order?.Kind==PlayableTacticalOrderKind.Move?PlayableCommandKind.Move:order?.Kind==PlayableTacticalOrderKind.Follow?PlayableCommandKind.Follow:(PlayableCommandKind?)null;
            return o!=null&&unit!=null&&kind.HasValue&&unit.Owner==o.Owner&&!unit.Held&&order.Owner==o.Owner&&order.Generation==o.Generation&&order.UnitId==unit.Id&&
                stamp!=null&&stamp.UnitId==unit.Id&&stamp.Owner==o.Owner&&stamp.Generation==o.Generation&&stamp.Origin==PlayableOrderOrigin.Ai&&stamp.Kind==kind.Value&&
                stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&stamp.ActionId>0&&stamp.JobId==stamp.ActionId&&stamp.Sequence==order.CommandSequence&&
                stamp.Tick==order.IssuedTick&&stamp.Tick<=o.Tick&&(records??Array.Empty<PlayableAiTraceRecord>()).Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Kind==kind.Value&&
                    r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&
                    r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.ReceiptIdentity?.Generation==o.Generation&&r.ReceiptIdentity.OwnerId==o.OwnerId);
        }
        public static void Observe(PlayableAiObservation o,AiArmyRegistry registry,long armyId,PlayableProfile p,IEnumerable<int> unavailable=null,string tacticalOwner=AiArmyPlanner.Policy,IEnumerable<PlayableAiTraceRecord> records=null,AiProfile ai=null,AiKnowledgeState knowledge=null)
        {
            var army=registry.CaptureArmy(armyId).FirstOrDefault(a=>a.Id==armyId&&a.TacticalOwner==tacticalOwner&&a.Phase!=AiArmyPhase.Disbanded);
            if(army==null)return;
            var excluded=new HashSet<int>(unavailable??Array.Empty<int>());
            bool Eligible(PlayableEntitySnapshot u)=>u.Owner==o.Owner&&AiReinforcementPolicy.Available(u)&&registry.ArmyFor(u.Id)==0&&!excluded.Contains(u.Id)&&
                AiRosterCatalog.Initial.LineWeight(u.Kind)+AiRosterCatalog.Initial.SupportWeight(u.Kind)>0;
            var core=AiReinforcementPolicy.ReceivingCore(o,army,p,ai??AiProfile.Initial,knowledge);
            bool rebuilding=army.Phase==AiArmyPhase.Regrouping||army.Phase==AiArmyPhase.Retreating||army.Phase==AiArmyPhase.Recovering;
            var free=o.Entities.Where(u=>Eligible(u)&&(tacticalOwner==AiDefensePlanner.Policy||rebuilding||AiReinforcementPolicy.PreferredReceiver(o,registry,army,u,p,ai??AiProfile.Initial,knowledge)==army.Id)&&(core.Length>0||rebuilding&&o.RouteProofs.Any(r=>r.UnitId==u.Id&&AiReinforcementPolicy.Home(o,r)&&SafeRoute(o,r,p,knowledge)))&&(tacticalOwner!=AiDefensePlanner.Policy||!u.Held&&u.CurrentOrder==null&&u.Queue==null&&u.NavigationOutcome==NavigationOutcome.Idle&&
                core.Any(c=>o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==c.Id&&SafeRoute(o,r,p))))).Select(u=>u.Id).ToArray();
            if(free.Length>0)registry.TryReinforce(o,army.Id,army.TacticalOwner,free);
            army=registry.CaptureArmy(armyId).Single(a=>a.Id==armyId);
            // Arrival uses the existing follow spacing/tolerance, not a new AI balance value.
            var joined=Ready(o,army,p,knowledge).Where(u=>tacticalOwner==AiDefensePlanner.Policy?
                AiRosterCatalog.Initial.LineWeight(u.Kind)>0&&DefenseAssemblyApplied(o,army,u,p,records):core.Length>0&&AiRosterCatalog.Initial.SupportWeight(u.Kind)>0&&AiArmyPlanner.AssemblyFollow(o,army,u,records)).Select(u=>u.Id);
            registry.JoinReinforcements(armyId,army.TacticalOwner,joined);
        }
        private static bool DefenseAssemblyApplied(PlayableAiObservation o,AiArmyState army,PlayableEntitySnapshot unit,PlayableProfile p,IEnumerable<PlayableAiTraceRecord> records)
        {
            var order=unit.CurrentOrder;var stamp=unit.OrderStamp;
            if(order?.Kind!=PlayableTacticalOrderKind.Follow||stamp==null||unit.Owner!=o.Owner||unit.Health<=0||stamp.UnitId!=unit.Id||
                stamp.Owner!=o.Owner||stamp.Generation!=o.Generation||stamp.Origin!=PlayableOrderOrigin.Ai||stamp.Kind!=PlayableCommandKind.Follow||
                stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||order.Generation!=o.Generation||
                order.Owner!=o.Owner||order.UnitId!=unit.Id||order.CommandSequence!=stamp.Sequence||order.IssuedTick!=stamp.Tick)return false;
            var leader=o.Entities.FirstOrDefault(c=>c.Id==order.TargetId&&c.Id!=unit.Id&&c.Owner==o.Owner&&c.Health>0&&army.Members.Contains(c.Id)&&
                !army.Reinforcements.Contains(c.Id)&&AiRosterCatalog.Initial.LineWeight(c.Kind)>0);
            return leader!=null&&(records??Array.Empty<PlayableAiTraceRecord>()).Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Kind==PlayableCommandKind.Follow&&
                r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.OwnerId==o.OwnerId&&r.SourceIdentity==stamp.Source&&
                r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.ApplicationTick<=o.Tick&&
                r.ReceiptIdentity?.Generation==o.Generation&&r.ReceiptIdentity.OwnerId==o.OwnerId)&&
                o.RouteProofs.Any(r=>r.UnitId==unit.Id&&r.Owner==o.Owner&&r.Generation==o.Generation&&r.Tick==o.Tick&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&
                    r.TargetId==leader.Id&&r.Target.Equals(PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading))&&r.Goal.Equals(r.Target)&&SafeRoute(o,r,p));
        }
        public static PlayableEntitySnapshot[] Ready(PlayableAiObservation o,AiArmyState army,PlayableProfile p,AiKnowledgeState knowledge=null)
        {
            // Membership only: preserve entity order, duplicates and every route predicate.
            var entities=o.Entities;var members=new HashSet<int>(army.Members);var reinforcements=new HashSet<int>(army.Reinforcements);
            var core=entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&members.Contains(u.Id)&&!reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0).OrderBy(u=>u.Id).ToArray();
            return entities.Where(u=>reinforcements.Contains(u.Id)&&core.Any(c=>
                Distance(u.Position,c.Position)<=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,c.Kind)&&
                o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==c.Id&&SafeRoute(o,r,p,knowledge)&&
                    RouteLength(r)<=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,c.Kind)))).ToArray();
        }
        // Offense admits a ready wave through its effective attack. Recovery cannot
        // attack, so it establishes the same proven assembly through ordinary Follow.
        internal static PlayableAiAction RecoveryAssembly(PlayableAiObservation o,AiArmyState army,PlayableProfile p,long actionId,long repeat,IEnumerable<PlayableAiTraceRecord> records)
        {
            if(army.OwnerId!=o.OwnerId||!AiArmyPlanner.IsArmyPolicy(army.TacticalOwner)||army.Phase!=AiArmyPhase.Recovering)return null;
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
        internal static PlayableAiAction ArrivalAssembly(PlayableAiObservation o,AiArmyState army,PlayableProfile p,long actionId,IEnumerable<PlayableAiTraceRecord> records=null,AiKnowledgeState knowledge=null,AiProfile ai=null)
        {
            if(army.OwnerId!=o.OwnerId||!AiArmyPlanner.IsArmyPolicy(army.TacticalOwner)||
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
                r.Target.Equals(PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading))&&r.Goal.Equals(r.Target)&&SafeRoute(o,r,p,knowledge));
            var candidates=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&!u.Held&&army.Members.Contains(u.Id)&&army.Reinforcements.Contains(u.Id)).ToArray();
            long repeat=AiProfile.SecondsToTicks((ai??AiProfile.Initial).Value("decision.repeatOrderSeconds"),30);
            // A crowd leg can keep reporting Moving while its occupied anchor
            // prevents arrival. At the existing repeat-order boundary, reconcile
            // to a certified Follow instead of endlessly retaining/reissuing Move.
            bool NativeMoveDue(PlayableEntitySnapshot u)
            {
                var order=u.CurrentOrder;var active=u.Queue?.Active;
                return order?.Kind==PlayableTacticalOrderKind.Move&&order.CommandSequence==u.OrderStamp.Sequence&&order.IssuedTick==u.OrderStamp.Tick&&
                    order.Owner==o.Owner&&order.Generation==o.Generation&&order.UnitId==u.Id&&o.Tick-order.IssuedTick>=repeat&&
                    active?.Kind==PlayableCommandKind.Move&&active.CommandSequence==u.OrderStamp.Sequence&&active.AiActionId==u.OrderStamp.ActionId&&
                    active.Anchor.HasValue&&active.Anchor.Value.Position.Equals(order.Destination);
            }
            var choices=candidates.Where(u=>AssemblyMove(u)&&
                (u.CurrentOrder==null&&u.NavigationOutcome==NavigationOutcome.Arrived||NativeMoveDue(u)))
                // The core may have moved since the gather was admitted. A current
                // certified Follow finishes that owned leg; Ready still requires
                // physical cohesion and effective Applied Follow custody.
                .SelectMany(u=>core.Where(c=>Distance(u.Position,c.Position)>p.FollowDistance+p.FollowArrivalTolerance)
                    .Select(c=>new{Unit=u,Leader=c,Route=FollowRoute(u,c)}))
                .Where(x=>x.Route!=null).OrderBy(x=>x.Unit.Id).ThenBy(x=>Distance(x.Unit.Position,x.Leader.Position)).ThenBy(x=>RouteLength(x.Route)).ThenBy(x=>x.Leader.Id).ToArray();
            var first=choices.FirstOrDefault();if(first==null)return null;
            // Complete this one accepted assembly obligation together. An arrived member
            // provides the genuine correction trigger; compatible siblings need not wait
            // for separate attention windows. Existing valid Follow and human orders stay live.
            var ids=candidates.Where(u=>(u.CurrentOrder==null&&(u.OrderStamp==null||AssemblyMove(u))||
                u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&AssemblyMove(u)&&u.OrderStamp.Sequence==u.CurrentOrder.CommandSequence)&&
                FollowRoute(u,first.Leader)!=null).Select(u=>u.Id).OrderBy(id=>id).ToArray();
            return new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Follow,ids,targetId:first.Leader.Id,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        public static PlayableAiAction Propose(PlayableAiObservation o,AiArmyState army,PlayableProfile p,long actionId,bool assemblyOnly=false,IEnumerable<PlayableAiTraceRecord> records=null,AiKnowledgeState knowledge=null,AiProfile ai=null)
        {
            var core=AiReinforcementPolicy.ReceivingCore(o,army,p,ai??AiProfile.Initial,knowledge);var ready=new HashSet<int>(core.Length==0?Array.Empty<int>():Ready(o,army,p,knowledge).Select(u=>u.Id));
            PlayableAiAction Action(PlayableCommandKind kind,int[] ids,NavPoint target=default,int targetId=0)=>new PlayableAiAction(actionId,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,kind,ids,target,targetId:targetId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            bool UnsafeCurrent(PlayableEntitySnapshot u)
            {
                var paths=o.RouteProofs.Where(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&
                    (u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&r.TargetId==u.CurrentOrder.TargetId&&core.Any(c=>c.Id==r.TargetId&&r.Target.Equals(PlayableUnitRules.FollowGoal(p,c.Position,c.HullHeading)))||
                     u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&r.TargetId==u.Id&&r.Target.Equals(u.CurrentOrder.Destination))).ToArray();
                return paths.Length>0&&paths.All(r=>!SafeRoute(o,r,p,knowledge));
            }
            long repeatFollow=AiProfile.SecondsToTicks((ai??AiProfile.Initial).Value("decision.repeatOrderSeconds"),30);
            bool TerminalFollow(PlayableEntitySnapshot u)=>u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&
                (u.NavigationOutcome==NavigationOutcome.Stopped||u.NavigationOutcome==NavigationOutcome.Blocked||u.NavigationOutcome==NavigationOutcome.Unreachable||u.NavigationOutcome==NavigationOutcome.Rejected)&&
                o.Tick-u.CurrentOrder.IssuedTick>=repeatFollow&&AiReinforcementPolicy.OwnedJoiningOrder(o,army,u,records);
            bool FreeFollowEndpoint(PlayableEntitySnapshot unit,PlayableEntitySnapshot leader)
            {
                var goal=PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading);
                double radius=PlayableUnitRules.Radius(p,unit.Kind);
                // A route through observed static geometry is not a free native
                // arrival slot. Do not retry terminal clients into a peer's body
                // or the endpoint of its current moving Follow.
                return o.Entities.Where(e=>e.Id!=unit.Id&&e.Health>0&&e.Kind!=PlayableEntityKind.Hawk).All(e=>
                    Distance(goal,e.Position)>=radius+PlayableUnitRules.Radius(p,e.Kind)&&
                    !(e.Moving&&e.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&o.Entities.Any(a=>a.Id==e.CurrentOrder.TargetId&&a.Owner==e.Owner&&
                        Distance(goal,PlayableUnitRules.FollowGoal(p,a.Position,a.HullHeading))<radius+PlayableUnitRules.Radius(p,e.Kind))));
            }
            bool KeepsGatherLeg(PlayableEntitySnapshot u)=>u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&
                (u.Moving||u.NavigationOutcome==NavigationOutcome.Moving||u.Queue?.PendingAiRouteViable==true)&&o.RouteProofs.Any(r=>r.UnitId==u.Id&&
                    (AiReinforcementPolicy.Home(o,r)&&r.Goal.Equals(u.CurrentOrder.Destination)||r.TargetId==u.Id&&r.Target.Equals(u.CurrentOrder.Destination))&&SafeRoute(o,r,p,knowledge));
            // Safety corrections precede first legs and aged terminal retries; both
            // precede refreshes of active legs. Fresh production must not starve
            // an older stranded Follow, or be starved by every moving member.
            foreach(var unit in o.Entities.Where(u=>AiReinforcementPolicy.Available(u)&&army.Reinforcements.Contains(u.Id)&&(!ready.Contains(u.Id)||assemblyOnly||core.Length==0))
                .OrderByDescending(UnsafeCurrent).ThenBy(u=>u.CurrentOrder!=null&&!TerminalFollow(u)).ThenBy(u=>TerminalFollow(u)?u.CurrentOrder.IssuedTick:o.Tick).ThenBy(u=>u.Id))
            {
                if(assemblyOnly&&unit.CurrentOrder!=null&&!IsCurrentAppliedDefenseAssemblyOrder(o,unit,records))continue;
                bool failedFollow=TerminalFollow(unit);
                // A certified but terminal Follow must not hold a client forever at
                // an occupied slot. Reassess another current healthy core member.
                var unitCore=failedFollow?core.Where(c=>c.Id!=unit.CurrentOrder.TargetId).ToArray():core;
                var currentLeader=unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow?unitCore.FirstOrDefault(c=>c.Id==unit.CurrentOrder.TargetId):null;
                if(currentLeader!=null&&o.RouteProofs.Any(r=>r.UnitId==unit.Id&&r.TargetId==currentLeader.Id&&r.Target.Equals(PlayableUnitRules.FollowGoal(p,currentLeader.Position,currentLeader.HullHeading))&&r.Goal.Equals(r.Target)&&SafeRoute(o,r,p,knowledge)))continue;
                // Track a moving core through its current native FollowGoal. A
                // stationary core keeps the ordinary gather Move and its arrival
                // correction; refreshing a fixed point cannot lag that core.
                if(unitCore.Length>0)
                {
                    var followRoute=o.RouteProofs.Where(r=>r.UnitId==unit.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&
                        unitCore.Any(c=>(c.Moving||failedFollow)&&(!failedFollow||FreeFollowEndpoint(unit,c))&&c.Id==r.TargetId&&r.Target.Equals(PlayableUnitRules.FollowGoal(p,c.Position,c.HullHeading)))&&r.Goal.Equals(r.Target)&&SafeRoute(o,r,p,knowledge))
                        .OrderBy(r=>RouteLength(r)).ThenBy(r=>r.TargetId).FirstOrDefault();
                    if(followRoute!=null)
                    {
                        var ids=failedFollow?o.Entities.Where(u=>u.Owner==o.Owner&&AiReinforcementPolicy.Available(u)&&army.Reinforcements.Contains(u.Id)&&(!ready.Contains(u.Id)||assemblyOnly)&&
                            TerminalFollow(u)&&u.CurrentOrder.TargetId==unit.CurrentOrder.TargetId&&o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==followRoute.TargetId&&
                                r.Target.Equals(followRoute.Target)&&r.Goal.Equals(r.Target)&&SafeRoute(o,r,p,knowledge))).Select(u=>u.Id).OrderBy(id=>id).ToArray():new[]{unit.Id};
                        return Action(PlayableCommandKind.Follow,ids,targetId:followRoute.TargetId);
                    }
                }
                // Keep an admitted, currently certified leg until arrival. A moving
                // core must not repeatedly restart it and starve the other wave units.
                if(KeepsGatherLeg(unit))continue;
                var close=unitCore.Where(c=>(unit.NavigationOutcome==NavigationOutcome.Arrived||unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow)&&Distance(unit.Position,c.Position)<=p.FollowDistance+p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,unit.Kind)+PlayableUnitRules.Radius(p,c.Kind)+p.Navigation.ArrivalTolerance)
                    .Where(c=>(!failedFollow||FreeFollowEndpoint(unit,c))&&o.RouteProofs.Any(r=>r.UnitId==unit.Id&&r.TargetId==c.Id&&r.Target.Equals(PlayableUnitRules.FollowGoal(p,c.Position,c.HullHeading))&&r.Goal.Equals(r.Target)&&SafeRoute(o,r,p,knowledge))).OrderBy(c=>Distance(unit.Position,c.Position)).ThenBy(c=>c.Id).FirstOrDefault();
                if(close!=null)
                {
                    // This band only corrects native arrival error. Ready/actual joining
                    // keeps the stricter original cohesion bound above.
                    return Action(PlayableCommandKind.Follow,new[]{unit.Id},targetId:close.Id);
                }
                var routes=o.RouteProofs.Where(r=>r.UnitId==unit.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&unitCore.Any(c=>c.Id==r.TargetId&&c.Position.Equals(r.Target))&&SafeRoute(o,r,p,knowledge))
                    .OrderBy(r=>RouteLength(r)).ThenBy(r=>r.TargetId).ToArray();
                // If every line anchor is in visible weapon range, gather at a current
                // native-proven ready home/production approach instead of a solo front.
                var route=routes.FirstOrDefault()??o.RouteProofs.Where(r=>r.UnitId==unit.Id&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&
                    o.Buildings.Any(b=>b.Id==r.TargetId&&b.Owner==o.Owner&&b.Phase==ConstructionPhase.Ready)&&SafeRoute(o,r,p,knowledge)).OrderBy(RouteLength).ThenBy(r=>r.TargetId).FirstOrDefault();
                if(route==null)
                {
                    if(unit.CurrentOrder!=null&&(!assemblyOnly||IsCurrentAppliedDefenseAssemblyOrder(o,unit,records)))return Action(PlayableCommandKind.Stop,new[]{unit.Id});
                    continue;
                }
                if(unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&unit.CurrentOrder.Destination.Equals(route.Goal))continue;
                if(unit.CurrentOrder==null&&Distance(unit.Position,route.Goal)<=p.Navigation.ArrivalTolerance+PlayableUnitRules.Radius(p,unit.Kind))continue;
                var wave=o.Entities.Where(u=>AiReinforcementPolicy.Available(u)&&army.Reinforcements.Contains(u.Id)&&(!ready.Contains(u.Id)||assemblyOnly)&&!KeepsGatherLeg(u)&&
                    (!assemblyOnly||u.CurrentOrder==null||IsCurrentAppliedDefenseAssemblyOrder(o,u,records))&&
                    !(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&u.CurrentOrder.Destination.Equals(route.Goal))&&
                    // Native approach projection can give each actor a different
                    // free endpoint at the same certified anchor. Admit that live
                    // cohort together; ordinary group movement allocates its slots
                    // and due delivery rechecks every actor's actual path.
                    o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.TargetId==route.TargetId&&(assemblyOnly?r.Goal.Equals(route.Goal):r.Target.Equals(route.Target))&&SafeRoute(o,r,p,knowledge))).Select(u=>u.Id).OrderBy(x=>x).ToArray();
                if(wave.Length>0){
                    // A group's arrival center is the shared anchor, rather than
                    // one actor's already offset approach point. Native admission
                    // allocates separate free slots around that center.
                    var center=!assemblyOnly&&wave.Length>1&&core.Any(c=>c.Id==route.TargetId&&c.Position.Equals(route.Target))?route.Target:route.Goal;
                    return Action(PlayableCommandKind.Move,wave,center);
                }
            }
            if(assemblyOnly)return null;
            foreach(var gun in o.Entities.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.SupportWeight(u.Kind)>0).OrderBy(u=>u.Id))
            {
                var fact=o.ArtillerySupport.FirstOrDefault(f=>f.UnitId==gun.Id&&f.Generation==o.Generation&&f.Tick==o.Tick);
                if(fact==null)continue;
                if(!fact.Threatened&&fact.Supported&&fact.UsefulShot||!fact.Position.HasValue)
                {if(gun.CurrentOrder!=null)return Action(PlayableCommandKind.Stop,new[]{gun.Id});continue;}
                var point=fact.Position.Value;
                if(Distance(gun.Position,point)<=p.FollowArrivalTolerance+PlayableUnitRules.Radius(p,gun.Kind))
                {if(gun.CurrentOrder!=null)return Action(PlayableCommandKind.Stop,new[]{gun.Id});continue;}
                if(HasLivePendingMove(gun,point))continue;
                if(gun.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&gun.CurrentOrder.Destination.Equals(point))continue;
                return Action(PlayableCommandKind.Move,new[]{gun.Id},point);
            }
            return null;
        }
        internal static bool HasLivePendingMove(PlayableEntitySnapshot unit,NavPoint target)
        {
            var pending=unit?.Queue?.Pending;
            return unit?.Queue?.PendingAiRouteViable==true&&pending!=null&&pending.AiActionId>0&&pending.Kind==PlayableCommandKind.Move&&
                pending.Anchor.HasValue&&pending.Anchor.Value.Position.Equals(target);
        }
        private static double RouteLength(PlayableRouteProof route)
        {double length=0;var at=route.Origin;foreach(var point in route.Path){length+=Distance(at,point);at=point;}return length;}
        internal static PlayableRouteRequest[] RouteRequests(IEnumerable<PlayableEntitySnapshot> entities,IEnumerable<PlayableBuildingSnapshot> buildings,AiArmyRegistryState state,PlayableOwner owner,long generation,long tick,PlayableProfile p)
        {
            if(state==null||state.Generation!=generation)return Array.Empty<PlayableRouteRequest>();
            var own=entities.Where(u=>u.Owner==owner&&u.Health>0).ToArray();
            var active=state.Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.TacticalOwner==AiDefensePlanner.Policy||AiArmyPlanner.IsArmyPolicy(a.TacticalOwner))).ToArray();
            return active.SelectMany(army=>ArmyRouteRequests(own,buildings,state,army,owner,generation,tick,p)).ToArray();
        }
        private static PlayableRouteRequest[] ArmyRouteRequests(PlayableEntitySnapshot[] own,IEnumerable<PlayableBuildingSnapshot> buildings,AiArmyRegistryState state,AiArmyState army,PlayableOwner owner,long generation,long tick,PlayableProfile p)
        {
            var core=own.Where(u=>army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0).ToArray();
            var assigned=new HashSet<int>(state.Armies.SelectMany(a=>a.Members));
            return own.Where(u=>(army.Members.Contains(u.Id)||!assigned.Contains(u.Id))&&AiRosterCatalog.Initial.LineWeight(u.Kind)+AiRosterCatalog.Initial.SupportWeight(u.Kind)>0)
                .OrderBy(u=>u.Id).SelectMany(u=>(army.Reinforcements.Contains(u.Id)||!assigned.Contains(u.Id)?core.OrderBy(c=>c.Id).SelectMany(c=>new[]{new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.FriendlyAnchor,c.Id,generation,tick,u.Position,PlayableUnitRules.Radius(p,u.Kind),c.Position),new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.FriendlyAnchor,c.Id,generation,tick,u.Position,PlayableUnitRules.Radius(p,u.Kind),PlayableUnitRules.FollowGoal(p,c.Position,c.HullHeading))}):Enumerable.Empty<PlayableRouteRequest>())
                    .Concat(buildings.Where(b=>b.Owner==owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.RepairTower)).OrderBy(b=>b.Id)
                        .Select(b=>new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.FriendlyAnchor,b.Id,generation,tick,u.Position,PlayableUnitRules.Radius(p,u.Kind),b.Position)))
                    .Concat(u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move?new[]{new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.FriendlyAnchor,u.Id,generation,tick,u.Position,PlayableUnitRules.Radius(p,u.Kind),u.CurrentOrder.Destination)}:Array.Empty<PlayableRouteRequest>())).ToArray();
        }
        // Between strategic decisions, certify only an already effective assembly
        // Follow leg. It disappears on promotion and never solves enemy/all-core routes.
        internal static PlayableRouteRequest[] AssemblyRouteRequests(IEnumerable<PlayableEntitySnapshot> entities,AiArmyRegistryState state,PlayableOwner owner,long generation,long tick,PlayableProfile p)
        {
            if(state==null||state.Generation!=generation)return Array.Empty<PlayableRouteRequest>();
            var own=entities.Where(u=>u.Owner==owner&&u.Health>0).ToArray();var requests=new List<PlayableRouteRequest>();
            foreach(var army in state.Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded&&(AiArmyPlanner.IsArmyPolicy(a.TacticalOwner)||a.TacticalOwner==AiDefensePlanner.Policy)))
            foreach(var unit in own.Where(u=>army.Reinforcements.Contains(u.Id)&&army.Members.Contains(u.Id)&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow))
            {
                var order=unit.CurrentOrder;var stamp=unit.OrderStamp;
                if(stamp==null||stamp.UnitId!=unit.Id||stamp.Owner!=owner||stamp.Generation!=generation||stamp.Origin!=PlayableOrderOrigin.Ai||stamp.Kind!=PlayableCommandKind.Follow||
                    stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||order.Owner!=owner||order.Generation!=generation||order.UnitId!=unit.Id||
                    order.CommandSequence!=stamp.Sequence||order.IssuedTick!=stamp.Tick||stamp.Tick>tick)continue;
                var leader=own.FirstOrDefault(c=>c.Id==order.TargetId&&c.Id!=unit.Id&&army.Members.Contains(c.Id)&&!army.Reinforcements.Contains(c.Id)&&AiRosterCatalog.Initial.LineWeight(c.Kind)>0);
                if(leader==null)continue;
                requests.Add(new PlayableRouteRequest(unit.Id,PlayableRouteTargetKind.FriendlyAnchor,leader.Id,generation,tick,unit.Position,PlayableUnitRules.Radius(p,unit.Kind),PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading)));
            }
            foreach(var army in state.Armies.Where(a=>a.Phase==AiArmyPhase.Retreating&&AiArmyPlanner.IsArmyPolicy(a.TacticalOwner)))
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

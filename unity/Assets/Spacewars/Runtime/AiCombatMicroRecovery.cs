using System;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal sealed partial class AiCombatMicroPolicy
    {
        bool RepairEligible(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,AiArmyState army,bool? combat=null)
        {
            if(V(ai,d,"repairEnabled")==0||!Eligible(u)||u.Queue?.Pending!=null||(u.Queue!=null&&u.Queue.Deferred.Count!=0)||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow)return false;
            double health=PlayableUnitRules.HealthRatio(p,u);
            bool fighting=combat??(Attackers(o,u,p).Length>0||u.TargetId>0||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack||u.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove);
            if(fighting)return health<V(ai,d,"combatRepairHealth");
            // An already accepted short withdrawal may convert directly to
            // repair under its original exact certificate and combat threshold.
            if(certificates.TryGetValue(u.Id,out var certificate)&&certificate.Kind==PlayableCommandKind.Move&&Matches(certificate,o,u))return health<V(ai,d,"combatRepairHealth");
            return health<V(ai,d,"idleRepairHealth")&&(u.CurrentOrder==null||QuietGuard(o,p,ai,d,u,army));
        }
        static bool QuietGuard(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,AiArmyState army)
        {
            var order=u.CurrentOrder;var stamp=u.OrderStamp;
            if(army==null||army.OwnerId!=o.OwnerId||army.TacticalOwner!=AiArmyPlanner.Policy||army.Role!=AiArmyRole.MobileDefense||
                (army.Phase!=AiArmyPhase.Staging&&army.Phase!=AiArmyPhase.Advancing)||!army.Rally.HasValue||order?.Kind!=PlayableTacticalOrderKind.Move||
                stamp==null||stamp.UnitId!=u.Id||stamp.Owner!=o.Owner||stamp.Generation!=o.Generation||stamp.Sequence<=0||stamp.Tick>o.Tick||stamp.Origin!=PlayableOrderOrigin.Ai||stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.Kind!=PlayableCommandKind.Move||stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||
                order.UnitId!=u.Id||order.Owner!=o.Owner||order.Generation!=o.Generation||order.CommandSequence!=stamp.Sequence||order.IssuedTick!=stamp.Tick)return false;
            // Rally is the actual Guard request point committed by its tactical
            // owner, not the HQ center or a guessed member endpoint. Ground's
            // live Native issuance preserves that original request even when its
            // physical assigned destination differs for this particular member.
            var active=u.Queue?.Active;
            if(u.Kind==PlayableEntityKind.Hawk){if(!order.Destination.Equals(army.Rally.Value))return false;}
            else if(active==null||active.IssuanceId<=0||active.GroupId<=0||active.Kind!=PlayableCommandKind.Move||active.CommandSequence!=stamp.Sequence||active.AiActionId!=stamp.ActionId||!active.Anchor.HasValue||!active.Anchor.Value.Position.Equals(army.Rally.Value))return false;
            var anchor=o.Buildings.FirstOrDefault(b=>b.Owner==o.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&
                (b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Mine)&&army.Objective==((int)PlayableRouteTargetKind.FriendlyAnchor)+":"+b.Id);
            if(anchor==null||o.OwnCenterDamage.Any(hit=>hit.CenterId==anchor.Id))return false;double margin=TerritoryRules.Radius(p,anchor.Kind)+V(ai,d,"safetyMargin");
            return !o.Entities.Any(e=>o.IsHostile(e.Owner)&&e.Health>0&&Distance(e.Position,anchor.Position)<=PlayableUnitRules.Range(p,e.Kind,e.Upgraded)+margin)&&
                !AiCombatMicroSafety.Towers(o).Any(tower=>Distance(tower.Position,anchor.Position)<=(tower.Kind==PlayableBuildingKind.GaussTower?p.GaussRange:p.ExplorerRange*p.TurretRangeMultiplier)+margin);
        }
        bool RepairAvailable(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,int towerId)
        {
            var tower=o.Buildings.FirstOrDefault(b=>b.Id==towerId&&Tower(b,o.Owner));if(tower==null)return false;
            double range=p.TurretRange;
            return ((tower.Healing?.TargetId??0)==0||tower.Healing.TargetId==u.Id)&&
                !clients.Any(pair=>pair.Key!=u.Id&&pair.Value.Phase==AiCombatMicroPhase.Repair&&pair.Value.Tower==towerId)&&
                !o.Entities.Any(other=>other.Id!=u.Id&&other.Owner==o.Owner&&other.Health>0&&PlayableUnitRules.HealthRatio(p,other)<1&&Distance(other.Position,tower.Position)<=range);
        }
        internal static double RepairSeconds(PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,double length)=>
            length/Math.Max(double.Epsilon,PlayableUnitRules.Speed(p,u.Kind))+
            (p.RepairTowerHealthPerSecond>0?Math.Max(0,(V(ai,d,"repairReturnHealth")-PlayableUnitRules.HealthRatio(p,u))*PlayableUnitRules.MaximumHealth(p,u))/p.RepairTowerHealthPerSecond:double.PositiveInfinity);
        internal static bool RepairTimeAllowed(PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,double length)=>
            RepairSeconds(p,ai,d,u,length)<=Math.Min(V(ai,d,"repairTravelSeconds"),V(ai,d,"maxPhaseSeconds"));
        internal bool RepairEscapeAllowed(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,NavPoint[] path)
        {
            if(!samples.TryGetValue(u.Id,out var sample)||sample.Loss<=0||sample.DamageTick<0||o.Tick-sample.DamageTick>T(ai,d,"safeSeconds"))return true;
            return RepairEscapeAllowed(p,u,sample.Loss,Attackers(o,u,p),path);
        }
        static bool RepairEscapeAllowed(PlayableProfile p,PlayableEntitySnapshot u,double loss,System.Collections.Generic.IEnumerable<PlayableEntitySnapshot> attackers,System.Collections.Generic.IEnumerable<NavPoint> path)
        {
            double danger=0;
            foreach(var enemy in attackers)
            {
                double range=PlayableUnitRules.Range(p,enemy.Kind,enemy.Upgraded)+PlayableUnitRules.Radius(p,u.Kind)+PlayableUnitRules.Radius(p,enemy.Kind),length=0;var at=u.Position;
                foreach(var end in path)
                {
                    double segment=Distance(at,end);
                    if(Distance(at,enemy.Position)>range)break;
                    if(Distance(end,enemy.Position)>range&&segment>0)
                    {
                        double dx=end.X-at.X,dz=end.Z-at.Z,x=at.X-enemy.Position.X,z=at.Z-enemy.Position.Z;
                        double b=2*(x*dx+z*dz),a=segment*segment,c=x*x+z*z-range*range;
                        double exit=Math.Max(0,Math.Min(1,(-b+Math.Sqrt(Math.Max(0,b*b-4*a*c)))/(2*a)));
                        length+=segment*exit;break;
                    }
                    length+=segment;at=end;
                }
                danger=Math.Max(danger,length);
            }
            // Only time still inside current factual attackers' envelopes counts.
            // Safe travel/healing is not compared with the old incoming-loss rate.
            return Health(u)/loss>danger/Math.Max(double.Epsilon,PlayableUnitRules.Speed(p,u.Kind));
        }
        AiCombatMicroPlan Repair(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiDifficulty d,PlayableEntitySnapshot u,AiArmyState army)
        {
            double range=p.TurretRange,radius=PlayableUnitRules.Radius(p,u.Kind);
            var buildings=u.Kind==PlayableEntityKind.Hawk?Array.Empty<NavObstacle>():o.Buildings.Where(b=>b.Health>0&&b.Phase!=ConstructionPhase.Pending).Select(b=>{
                double half=TerritoryRules.Radius(p,b.Kind);return new NavObstacle(b.Position.X-half,b.Position.Z-half,b.Position.X+half,b.Position.Z+half);
            }).ToArray();
            // Towers are not army route-proof targets. Select an observed physical
            // approach, then use the existing authoritative asynchronous route lane
            // at delivery, including its actual travel length and danger checks.
            var candidate=o.Buildings.Where(b=>RepairAvailable(o,p,ai,d,u,b.Id)).SelectMany(tower=>
                PlayableUnitRules.AttackApproachCandidates(p,u.Position,tower.Position,range).Select(goal=>new{Tower=tower,Goal=goal,Length=Distance(u.Position,goal)}))
                .Where(x=>o.Vision?.IsVisible(x.Goal)==true&&Math.Abs(x.Goal.X)+radius<p.ArenaHalfExtent&&Math.Abs(x.Goal.Z)+radius<p.ArenaHalfExtent&&
                    (u.Kind==PlayableEntityKind.Hawk||!PlayableMap.StaticObstacles(p).Any(obstacle=>obstacle.DistanceSquared(x.Goal)<=radius*radius)&&
                        !buildings.Any(obstacle=>obstacle.DistanceSquared(x.Goal)<=radius*radius))&&
                    !o.Entities.Any(other=>other.Id!=u.Id&&other.Health>0&&(other.Kind==PlayableEntityKind.Hawk)==(u.Kind==PlayableEntityKind.Hawk)&&Distance(other.Position,x.Goal)<radius+PlayableUnitRules.Radius(p,other.Kind))&&
                    RepairTimeAllowed(p,ai,d,u,x.Length)&&RepairEscapeAllowed(o,p,ai,d,u,new[]{x.Goal})&&AiTacticalExecutor.WithdrawalPathForUnit(o,u.Position,new[]{x.Goal},radius,p,u.Kind==PlayableEntityKind.Hawk)&&
                    AiCombatMicroSafety.Path(o,p,u,new[]{x.Goal},true))
                .OrderBy(x=>RepairSeconds(p,ai,d,u,x.Length)).ThenBy(x=>x.Tower.Id).ThenBy(x=>x.Goal.X).ThenBy(x=>x.Goal.Z).FirstOrDefault();
            return candidate==null?null:new AiCombatMicroPlan{Action=Action(o,PlayableCommandKind.Move,new[]{u.Id},candidate.Goal,tower:candidate.Tower.Id),Phase=AiCombatMicroPhase.Repair,Army=army?.Id??0,Survival=Attackers(o,u,p).Length>0};
        }
    }
}

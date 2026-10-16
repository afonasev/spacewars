using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Runtime
{
    // All facts originate in one detached team-visible observation. Control kind,
    // enemy bank, queues and hidden battles are deliberately absent.
    internal sealed class AiOpponentAssessment
    {
        internal int Team, TargetId; internal PlayableRouteTargetKind Kind;
        internal double Threat, EconomicValue, Reachability, Opportunity, NewFrontCost, Score, ArrivalSeconds;
        internal long SourceTick; internal NavPoint Target;
    }
    internal static class AiOpponentSelector
    {
        internal static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        internal static int Team(PlayableAiObservation o,PlayableOwner owner)=>o.Participants.Count==0?(int)owner:o.Participants[(int)owner].Team;
        internal static int TargetTeam(PlayableAiObservation o,int id)=>o.Entities.Where(e=>e.Id==id&&o.IsHostile(e.Owner)).Select(e=>Team(o,e.Owner))
            .Concat(o.Buildings.Where(b=>b.Id==id&&o.IsHostile(b.Owner)).Select(b=>Team(o,b.Owner))).DefaultIfEmpty(-1).First();
        internal static AiOpponentAssessment[] Assess(PlayableAiObservation o,PlayableProfile gameplay,AiProfile profile,int currentTarget=0)
        {
            var own=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0).ToArray();
            var centers=o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)).ToArray();
            int currentTeam=TargetTeam(o,currentTarget);
            return o.RouteProofs.Where(r=>own.Any(u=>u.Id==r.UnitId)&&r.Kind==PlayableRouteTargetKind.VisibleEnemy&&TargetTeam(o,r.TargetId)>=0)
                .GroupBy(r=>r.TargetId).Select(g=>{
                    var route=g.OrderBy(r=>r.UnitId).First();int team=TargetTeam(o,g.Key);
                    // Team estimates never combine forces of independent opponents.
                    var enemies=o.Entities.Where(e=>e.Health>0&&o.IsHostile(e.Owner)&&Team(o,e.Owner)==team).ToArray();
                    double range=gameplay.TankVisionRange;
                    double threat=enemies.Select(e=>centers.Select(c=>1d/(1d+Distance(e.Position,c.Position)/range)).DefaultIfEmpty(0).Max()).DefaultIfEmpty(0).Max();
                    var building=o.Buildings.FirstOrDefault(b=>b.Id==g.Key&&o.IsHostile(b.Owner));
                    double economy=building==null?0:building.Kind==PlayableBuildingKind.Headquarters?1:building.Kind==PlayableBuildingKind.Refinery||building.Kind==PlayableBuildingKind.Mine?1:0;
                    double cost=g.Average(r=>r.Path.Zip(r.Path.Skip(1),(a,b)=>Distance(a,b)).Sum());
                    double reach=1d/(1d+cost/range);
                    var unit=o.Entities.FirstOrDefault(e=>e.Id==g.Key&&o.IsHostile(e.Owner));
                    double opportunity=unit==null?0:Math.Max(0,1d-unit.Health/(double)PlayableUnitRules.Health(gameplay,unit.Kind));
                    // A currently observed attack on another hostile team is a public opportunity.
                    if(enemies.Any(e=>e.TargetId>0&&o.Entities.Any(t=>t.Id==e.TargetId&&o.IsHostile(t.Owner)&&Team(o,t.Owner)!=team)))opportunity=1;
                    double front=currentTeam>=0&&currentTeam!=team?1:0;
                    return new AiOpponentAssessment{Team=team,TargetId=g.Key,Kind=route.Kind,Target=route.Target,SourceTick=o.Tick,
                        Threat=threat,EconomicValue=economy,Reachability=reach,Opportunity=opportunity,NewFrontCost=front,
                        ArrivalSeconds=cost/gameplay.TankSpeed,
                        Score=profile.Value("utility.target.threat")*threat+profile.Value("utility.target.economicValue")*economy+profile.Value("utility.target.reachability")*reach+profile.Value("utility.target.opportunity")*opportunity-profile.Value("utility.target.newFrontCost")*front};
                }).OrderByDescending(a=>a.Score).ThenBy(a=>a.Team).ThenBy(a=>a.TargetId).ToArray();
        }
    }
}

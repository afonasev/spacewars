using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    public enum AiScoutPlan { Standard, BlindRush, ScoutLed }

    // Stateless policy. Durable assignments use the authority's existing army wire;
    // coverage and successful base search come exclusively from O1 knowledge.
    public sealed class AiScoutPlanner
    {
        public const string Policy="scout";
        public int Budget(AiProfile ai,AiDifficulty difficulty,uint personality,AiScoutPlan plan)
        {
            int cap=(int)ai.DifficultyValue(difficulty,"scoutAssignments");
            if(plan==AiScoutPlan.BlindRush)return (int)Math.Floor(cap*ai.Value("scouting.blindRushBudgetScale"));
            if(plan==AiScoutPlan.ScoutLed)return cap;
            return Math.Min(cap,1+(int)Math.Floor(cap*(personality/(double)uint.MaxValue)));
        }
        public bool BaseSearchSuccess(AiKnowledgeState knowledge)=>knowledge.Contacts.Any(c=>c.Building&&
            (c.Kind==(int)PlayableBuildingKind.Headquarters||c.Kind==(int)PlayableBuildingKind.Factory||c.Kind==(int)PlayableBuildingKind.Mine||c.Kind==(int)PlayableBuildingKind.Outpost));
        private static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private static IEnumerable<AiArmyState> Claims(AiArmyRegistry registry)=>registry.Capture().Armies.Where(a=>a.Role==AiArmyRole.Scout&&a.TacticalOwner==Policy&&a.Phase!=AiArmyPhase.Disbanded);
        private static string Objective(int site)=>"survey:"+site.ToString(CultureInfo.InvariantCulture);
        public static PlayableRouteRequest[] RouteRequests(IEnumerable<PlayableEntitySnapshot> units,IEnumerable<PlayablePublicScoutObjective> objectives,PlayableOwner owner,long generation,long tick,PlayableProfile profile)=>
            units.Where(u=>u.Owner==owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer&&!u.Moving).OrderBy(u=>u.Id)
            .SelectMany(u=>objectives.Where(t=>t.Reachable).OrderBy(t=>t.SiteId).Select(t=>new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.PublicObjective,t.SiteId,generation,tick,u.Position,PlayableUnitRules.Radius(profile,u.Kind),t.Approach))).ToArray();
        public void Observe(PlayableAiObservation o,AiKnowledgeState knowledge,AiArmyRegistry registry,AiProfile ai,int budget)
        {
            Binding(o,knowledge,registry);
            foreach(var a in Claims(registry).ToArray())
            {
                var target=o.PublicScoutObjectives.SingleOrDefault(t=>Objective(t.SiteId)==a.Objective);
                var u=o.Entities.SingleOrDefault(e=>e.Id==a.LeaderId&&e.Health>0&&e.Owner==o.Owner);
                if(target==null||u==null||knowledge.Areas.Any(x=>x.AreaId==target.SiteId&&x.ActuallyCoveredTick>=a.CreatedTick)||
                    o.Tick-a.ProgressTick>=AiProfile.SecondsToTicks(ai.Value("armies.stallSeconds"),30))
                {registry.Disband(a.Id);continue;}
                // Actual displacement, never an accepted order, advances the clock.
                registry.UpdateMission(a.Id,Policy,AiArmyPhase.Advancing,o.Tick,a.Objective,u.Position,target.Approach,
                    progressed:a.Anchor.HasValue&&Distance(a.Anchor.Value,u.Position)>0);
            }
            ReconcileBudget(registry,budget);
        }
        internal void ReconcileBudget(AiArmyRegistry registry,int budget)
        {foreach(var a in Claims(registry).OrderBy(a=>a.Id).Skip(budget).ToArray())registry.Disband(a.Id);}
        private static void Binding(PlayableAiObservation o,AiKnowledgeState k,AiArmyRegistry r)
        {if(o.OwnerId!=k.OwnerId||o.Generation!=k.Generation||o.OwnerId!=r.OwnerId||o.Generation!=r.Generation||o.Tick!=k.ObservationTick)throw new ArgumentException("Scout observation binding");}
        public PlayableAiAction Plan(PlayableAiObservation o,AiKnowledgeState knowledge,AiArmyRegistry registry,PlayableProfile gameplay,AiProfile ai,int budget)
        {
            Binding(o,knowledge,registry);var claims=Claims(registry).ToArray();
            var revisit=AiProfile.SecondsToTicks(ai.Value("scouting.revisitSeconds"),30);
            var candidates=from u in o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer&&!u.Moving)
                where registry.ArmyFor(u.Id)==0&&claims.Length<budget
                from target in o.PublicScoutObjectives.Where(t=>t.Reachable)
                where !claims.Any(a=>a.Objective==Objective(target.SiteId))
                let area=knowledge.Areas.SingleOrDefault(a=>a.AreaId==target.SiteId)
                let gain=area==null?1:Math.Min(1,(o.Tick-area.ActuallyCoveredTick)/(double)revisit)
                where gain>=1
                let route=o.RouteProofs.SingleOrDefault(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.PublicObjective&&r.TargetId==target.SiteId)
                where route!=null&&route.Path.Count>0
                let travel=RouteLength(route)/(RouteLength(route)+gameplay.ExplorerSpeed*ai.Value("scouting.revisitSeconds"))
                let danger=knowledge.Contacts.Where(c=>Distance(c.Position,target.Approach)<=gameplay.ExplorerVision).Select(c=>c.Confidence).DefaultIfEmpty(0).Max()
                let score=ai.Value("utility.scout.informationGain")*gain-ai.Value("utility.scout.travelCost")*travel-ai.Value("utility.scout.riskCost")*danger
                orderby score descending,target.SiteId,u.Id
                select new {u,target,route};
            var choice=candidates.FirstOrDefault();if(choice==null)return null;
            return new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,new[]{choice.u.Id},choice.route.Goal,siteId:choice.target.SiteId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        private static double RouteLength(PlayableRouteProof r)
        {double length=0;var p=r.Origin;foreach(var point in r.Path){length+=Distance(p,point);p=point;}return length;}
        public void Commit(PlayableAiObservation o,PlayableAiAction action,AiArmyRegistry registry)
        {
            long id=registry.ArmyFor(action.EntityIds.Single());
            if(id==0&&!registry.TryCreate(o,AiArmyRole.Scout,Policy,action.EntityIds,out id))throw new InvalidOperationException("Selected scout claim admission failed");
            var u=o.Entities.Single(e=>e.Id==action.EntityIds.Single());
            if(!registry.UpdateMission(id,Policy,AiArmyPhase.Advancing,o.Tick,Objective(action.SiteId),u.Position,action.Target))throw new InvalidOperationException("Scout tactical ownership changed");
        }
    }
}

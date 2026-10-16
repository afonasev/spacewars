using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    public enum AiScoutPlan { Standard, BlindRush, ScoutLed }

#if UNITY_INCLUDE_TESTS
    // Immutable observation of values evaluated by the ordinary deferred scout-plan query.
    internal sealed class AiScoutPlanReadbackForTests
    {
        internal AiScoutPlanReadbackForTests(string ownerId,long generation,long tick,string stage,int unitId,int siteId,object value)
        {OwnerId=ownerId;Generation=generation;Tick=tick;Stage=stage;UnitId=unitId;SiteId=siteId;Value=value;}
        internal string OwnerId{get;} internal long Generation{get;} internal long Tick{get;} internal string Stage{get;}
        internal int UnitId{get;} internal int SiteId{get;} internal object Value{get;}
    }
#endif

    // Stateless policy. Durable assignments use the authority's existing army wire;
    // coverage and successful base search come exclusively from O1 knowledge.
    public sealed class AiScoutPlanner
    {
        public const string Policy="scout";
#if UNITY_INCLUDE_TESTS
        internal static Action<AiScoutPlanReadbackForTests> PlanReadbackForTests;
        internal static string PlanReadbackOwnerFilterForTests;
#endif
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
        private static string PreparationObjective(int site)=>"prepare:"+site.ToString(CultureInfo.InvariantCulture);
        internal static bool IsPreparationObjective(string objective,int site)=>site>0&&objective==PreparationObjective(site);
        internal bool IsPreparationActor(AiArmyRegistry registry,int siteId,int actorId)=>siteId>0&&actorId>0&&
            Claims(registry).Any(a=>a.Objective==PreparationObjective(siteId)&&a.LeaderId==actorId&&a.Members.Length==1&&a.Members[0]==actorId);
        internal int PreparationSite(AiArmyRegistry registry,int actorId=0)=>Claims(registry)
            .Where(a=>a.Members.Length==1&&(actorId<=0||a.LeaderId==actorId))
            .Select(a=>{var prefix="prepare:";return a.Objective!=null&&a.Objective.StartsWith(prefix,StringComparison.Ordinal)&&int.TryParse(a.Objective.Substring(prefix.Length),NumberStyles.None,CultureInfo.InvariantCulture,out var site)&&site>0?site:0;})
            .FirstOrDefault(site=>site>0);
        internal void ReleasePreparation(AiArmyRegistry registry,int siteId,int actorId)
        {
            if(siteId<=0||actorId<=0)return;
            foreach(var a in Claims(registry).Where(a=>IsPreparationObjective(a.Objective,siteId)&&a.LeaderId==actorId&&a.Members.Length==1&&a.Members[0]==actorId).ToArray())registry.Disband(a.Id);
        }
        public static PlayableRouteRequest[] RouteRequests(IEnumerable<PlayableEntitySnapshot> units,IEnumerable<PlayablePublicScoutObjective> objectives,PlayableOwner owner,long generation,long tick,PlayableProfile profile)=>
            units.Where(u=>u.Owner==owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer&&!u.Moving).OrderBy(u=>u.Id)
            .SelectMany(u=>objectives.Where(t=>t.Reachable).OrderBy(t=>t.SiteId).Select(t=>new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.PublicObjective,t.SiteId,generation,tick,u.Position,PlayableUnitRules.Radius(profile,u.Kind),t.Approach))).ToArray();
        public void Observe(PlayableAiObservation o,AiKnowledgeState knowledge,AiArmyRegistry registry,AiProfile ai,int budget,int preparationSite=0)
        {
            Binding(o,knowledge,registry);
            foreach(var a in Claims(registry).ToArray())
            {
                var target=o.PublicScoutObjectives.SingleOrDefault(t=>Objective(t.SiteId)==a.Objective||PreparationObjective(t.SiteId)==a.Objective);
                var u=o.Entities.SingleOrDefault(e=>e.Id==a.LeaderId&&e.Health>0&&e.Owner==o.Owner);
                bool covered=target!=null&&knowledge.Areas.Any(x=>x.AreaId==target.SiteId&&x.ActuallyCoveredTick>=a.CreatedTick);
                bool holdingPreparation=target!=null&&IsPreparationObjective(a.Objective,target.SiteId)&&target.Reachable&&target.SiteId==preparationSite&&covered;
                if(target==null||!target.Reachable||u==null||
                    IsPreparationObjective(a.Objective,target.SiteId)&&target.SiteId!=preparationSite||covered&&!holdingPreparation||
                    !holdingPreparation&&o.Tick-a.ProgressTick>=AiProfile.SecondsToTicks(ai.Value("armies.stallSeconds"),30))
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
        public bool UsefulProductionDemand(PlayableAiObservation o,AiKnowledgeState knowledge,AiProfile ai,int budget)
        {
            if(budget<=0)return false;
            long revisit=AiProfile.SecondsToTicks(ai.Value("scouting.revisitSeconds"),30);
            return o.PublicScoutObjectives.Any(t=>t.Reachable&&(knowledge.Areas.All(a=>a.AreaId!=t.SiteId)||
                knowledge.Areas.Any(a=>a.AreaId==t.SiteId&&o.Tick-a.ActuallyCoveredTick>=revisit)));
        }
        public PlayableAiAction Plan(PlayableAiObservation o,AiKnowledgeState knowledge,AiArmyRegistry registry,PlayableProfile gameplay,AiProfile ai,int budget,int preparationSite=0,int reservedActor=0)
        {
            Binding(o,knowledge,registry);var claims=Claims(registry).ToArray();
            var revisit=AiProfile.SecondsToTicks(ai.Value("scouting.revisitSeconds"),30);
#if UNITY_INCLUDE_TESTS
            var readback=PlanReadbackForTests;
            bool capture=readback!=null&&PlanReadbackOwnerFilterForTests==o.OwnerId;
            void Record(string stage,int unitId,int siteId,object value)
            {if(capture)readback(new AiScoutPlanReadbackForTests(o.OwnerId,o.Generation,o.Tick,stage,unitId,siteId,value));}
            bool Guard(string stage,int unitId,int siteId,bool value)
            {Record(stage,unitId,siteId,value);return value;}
            T Value<T>(string stage,int unitId,int siteId,T value)
            {Record(stage,unitId,siteId,value);return value;}
            var candidates=from u in o.Entities.Where(u=>Guard("unit.owner",u.Id,0,u.Owner==o.Owner)&&Guard("unit.alive",u.Id,0,u.Health>0)&&
                    Guard("unit.explorer",u.Id,0,u.Kind==PlayableEntityKind.Explorer)&&Guard("unit.stationary",u.Id,0,!u.Moving)&&Guard("unit.expansion-reserved",u.Id,0,u.Id!=reservedActor))
                where Guard("unit.unclaimed",u.Id,0,registry.ArmyFor(u.Id)==0)&&Guard("budget.available",u.Id,0,claims.Length<budget)
                from target in o.PublicScoutObjectives.Where(t=>Guard("target.reachable",u.Id,t.SiteId,t.Reachable))
                where Guard("target.unclaimed",u.Id,target.SiteId,!claims.Any(a=>Guard("claim.matchesObjective",u.Id,target.SiteId,a.Objective==Objective(target.SiteId)||a.Objective==PreparationObjective(target.SiteId))))
                let area=Value("area",u.Id,target.SiteId,knowledge.Areas.SingleOrDefault(a=>Guard("area.matchesSite",u.Id,target.SiteId,a.AreaId==target.SiteId)))
                let gain=Value("gain",u.Id,target.SiteId,area==null?1:Math.Min(1,(o.Tick-area.ActuallyCoveredTick)/(double)revisit))
                where Guard("gain.accepted",u.Id,target.SiteId,target.SiteId==preparationSite||gain>=1)
                let route=Value("route",u.Id,target.SiteId,o.RouteProofs.SingleOrDefault(r=>Guard("route.matchesUnit",u.Id,target.SiteId,r.UnitId==u.Id)&&
                    Guard("route.publicObjective",u.Id,target.SiteId,r.Kind==PlayableRouteTargetKind.PublicObjective)&&Guard("route.matchesSite",u.Id,target.SiteId,r.TargetId==target.SiteId)))
                where Guard("route.exists",u.Id,target.SiteId,route!=null)&&Guard("route.path.nonempty",u.Id,target.SiteId,route.Path.Count>0)
                let travel=Value("travel",u.Id,target.SiteId,RouteLength(route)/(RouteLength(route)+gameplay.ExplorerSpeed*ai.Value("scouting.revisitSeconds")))
                let danger=Value("danger",u.Id,target.SiteId,knowledge.Contacts.Where(c=>Guard("danger.contact.withinVision",u.Id,target.SiteId,Distance(c.Position,target.Approach)<=gameplay.ExplorerVision))
                    .Select(c=>Value("danger.contact.confidence",u.Id,target.SiteId,c.Confidence)).DefaultIfEmpty(0).Max())
                let score=Value("score",u.Id,target.SiteId,ai.Value("utility.scout.informationGain")*gain-ai.Value("utility.scout.travelCost")*travel-ai.Value("utility.scout.riskCost")*danger)
                orderby (target.SiteId==preparationSite?0:1),score descending,target.SiteId,u.Id
                select new {u,target,route};
#else
            var candidates=from u in o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer&&!u.Moving)
                where u.Id!=reservedActor&&registry.ArmyFor(u.Id)==0&&claims.Length<budget
                from target in o.PublicScoutObjectives.Where(t=>t.Reachable)
                where !claims.Any(a=>a.Objective==Objective(target.SiteId)||a.Objective==PreparationObjective(target.SiteId))
                let area=knowledge.Areas.SingleOrDefault(a=>a.AreaId==target.SiteId)
                let gain=area==null?1:Math.Min(1,(o.Tick-area.ActuallyCoveredTick)/(double)revisit)
                where target.SiteId==preparationSite||gain>=1
                let route=o.RouteProofs.SingleOrDefault(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.PublicObjective&&r.TargetId==target.SiteId)
                where route!=null&&route.Path.Count>0
                let travel=RouteLength(route)/(RouteLength(route)+gameplay.ExplorerSpeed*ai.Value("scouting.revisitSeconds"))
                let danger=knowledge.Contacts.Where(c=>Distance(c.Position,target.Approach)<=gameplay.ExplorerVision).Select(c=>c.Confidence).DefaultIfEmpty(0).Max()
                let score=ai.Value("utility.scout.informationGain")*gain-ai.Value("utility.scout.travelCost")*travel-ai.Value("utility.scout.riskCost")*danger
                orderby (target.SiteId==preparationSite?0:1),score descending,target.SiteId,u.Id
                select new {u,target,route};
#endif
            var choice=candidates.FirstOrDefault();
#if UNITY_INCLUDE_TESTS
            if(capture)Record("final.choice",choice==null?0:choice.u.Id,choice==null?0:choice.target.SiteId,
                choice==null?null:(object)new{UnitId=choice.u.Id,SiteId=choice.target.SiteId,choice.route.Goal,choice.route.Target,choice.route.GeometryRevision});
#endif
            if(choice==null)return null;
            return new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,new[]{choice.u.Id},choice.route.Goal,siteId:choice.target.SiteId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        private static double RouteLength(PlayableRouteProof r)
        {double length=0;var p=r.Origin;foreach(var point in r.Path){length+=Distance(p,point);p=point;}return length;}
        public void Commit(PlayableAiObservation o,PlayableAiAction action,AiArmyRegistry registry,int preparationSite=0)
        {
            long id=registry.ArmyFor(action.EntityIds.Single());
            if(id==0&&!registry.TryCreate(o,AiArmyRole.Scout,Policy,action.EntityIds,out id))throw new InvalidOperationException("Selected scout claim admission failed");
            var u=o.Entities.Single(e=>e.Id==action.EntityIds.Single());
            string objective=action.SiteId==preparationSite?PreparationObjective(action.SiteId):Objective(action.SiteId);
            if(!registry.UpdateMission(id,Policy,AiArmyPhase.Advancing,o.Tick,objective,u.Position,action.Target))throw new InvalidOperationException("Scout tactical ownership changed");
        }
    }
}

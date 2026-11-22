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
        internal double RevisitScale{get;set;}=1;
#if UNITY_INCLUDE_TESTS
        internal static Action<AiScoutPlanReadbackForTests> PlanReadbackForTests;
        internal static string PlanReadbackOwnerFilterForTests;
#endif
        public int Budget(AiProfile ai,AiDifficulty difficulty,uint personality,AiScoutPlan plan)
        {
            int cap=(int)ai.DifficultyValue(difficulty,"scoutAssignments");
            if(plan==AiScoutPlan.BlindRush)return (int)Math.Floor(cap*ai.Value("scouting.blindRushBudgetScale"));
            if(plan==AiScoutPlan.ScoutLed)return cap;
            return cap;
        }
        internal int ProtectedUnit{get;set;}
        internal int OpeningScout{get;set;}
        internal bool SurveyOutposts{get;set;}
        private const string ResumeSurvey="resume-survey";
        private const string CombatLossShare="scouting.maximumCombatHealthLossShare";
        private bool SafeCapturerObjective(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot u,PlayablePublicScoutObjective t,AiProfile ai,AiKnowledgeState knowledge)=>
            (!AiRelayObjectives.HasRelays(o)||!u.Held&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human&&AiRelayObjectives.Viable(o,p,ai,knowledge,new[]{u},t.SiteId,t.Approach))&&(u.Id!=ProtectedUnit||o.Sites.Any(s=>s.Site.Id==t.SiteId&&s.Site.Kind==PlayableBuildingKind.Mine)&&!o.Entities.Any(e=>o.IsHostile(e.Owner)&&e.Health>0&&Distance(e.Position,t.Approach)<=PlayableUnitRules.Vision(p,e.Kind)));
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
        public static PlayableRouteRequest[] RouteRequests(IEnumerable<PlayableEntitySnapshot> units,IEnumerable<PlayablePublicScoutObjective> objectives,PlayableOwner owner,long generation,long tick,PlayableProfile profile,IReadOnlyCollection<int> assigned=null)=>
            units.Where(u=>u.Owner==owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer&&(!u.Moving||assigned?.Contains(u.Id)==true||objectives.Any(t=>t.Role==PlayablePublicScoutObjectiveRole.Relay))).OrderBy(u=>u.Id)
            .SelectMany(u=>objectives.Where(t=>t.Reachable).OrderBy(t=>t.SiteId).Select(t=>new PlayableRouteRequest(u.Id,PlayableRouteTargetKind.PublicObjective,t.SiteId,generation,tick,u.Position,PlayableUnitRules.Radius(profile,u.Kind),t.Approach))).ToArray();
        public void Observe(PlayableAiObservation o,AiKnowledgeState knowledge,AiArmyRegistry registry,AiProfile ai,int budget,int preparationSite=0,PlayableProfile gameplay=null,int pendingScoutId=0)
        {
            Binding(o,knowledge,registry);
            foreach(var saved in Claims(registry).ToArray())
            {
                var a=saved;
                var u=o.Entities.SingleOrDefault(e=>e.Id==a.LeaderId&&e.Health>0&&e.Owner==o.Owner);
                if(CombatInProgress(a)&&a.LeaderId!=pendingScoutId&&u?.CurrentOrder?.Kind!=PlayableTacticalOrderKind.Attack)
                {
                    registry.SetScoutWithdrawalCondition(a.Id,ResumeSurvey);
                    a=registry.CaptureArmy(a.Id).Single();
                }
                if(AiRelayObjectives.IsClaim(a))
                {
                    AiRelayObjectives.ObserveScout(o,registry,ai,gameplay??PlayableProfile.Default,a);
                    continue;
                }
                // A public start can also be an expansion Outpost. Site identity
                // owns coverage/claims; the accepted rally disambiguates approaches.
                var target=o.PublicScoutObjectives.Where(t=>Objective(t.SiteId)==a.Objective||PreparationObjective(t.SiteId)==a.Objective)
                    .Where(t=>!IsPreparationObjective(a.Objective,t.SiteId)||t.Role!=PlayablePublicScoutObjectiveRole.PossibleEnemyStart)
                    .OrderBy(t=>a.Rally.HasValue?Distance(t.Approach,a.Rally.Value):0).ThenBy(t=>t.Role).FirstOrDefault();
                bool contact=CombatInProgress(a);
                bool covered=target!=null&&knowledge.Areas.Any(x=>x.AreaId==target.SiteId&&x.ActuallyCoveredTick>=a.CreatedTick);
                bool holdingPreparation=target!=null&&IsPreparationObjective(a.Objective,target.SiteId)&&target.Reachable&&target.SiteId==preparationSite&&covered;
                if(target==null||!target.Reachable||u==null||u.Held||u.OrderStamp?.Origin==PlayableOrderOrigin.Human||
                    IsPreparationObjective(a.Objective,target.SiteId)&&target.SiteId!=preparationSite||covered&&!holdingPreparation&&!contact||
                    !holdingPreparation&&!contact&&o.Tick-a.ProgressTick>=AiProfile.SecondsToTicks(ai.Value("armies.stallSeconds"),30))
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
            long revisit=AiProfile.SecondsToTicks((ai.Value("scouting.revisitSeconds")*RevisitScale),30);
            return o.PublicScoutObjectives.Any(t=>t.Reachable&&(knowledge.Areas.All(a=>a.AreaId!=t.SiteId)||
                knowledge.Areas.Any(a=>a.AreaId==t.SiteId&&o.Tick-a.ActuallyCoveredTick>=revisit)));
        }
        public PlayableAiAction Plan(PlayableAiObservation o,AiKnowledgeState knowledge,AiArmyRegistry registry,PlayableProfile gameplay,AiProfile ai,int budget,int preparationSite=0,int reservedActor=0,ISet<int> reservedActors=null)
        {
            Binding(o,knowledge,registry);var claims=Claims(registry).ToArray();
            foreach(var claim in claims)
            {
                if(reservedActors?.Contains(claim.LeaderId)==true)continue;
                var action=Continue(o,gameplay,ai,knowledge,claim,reservedActor);
                if(action!=null)return action;
            }
            var relay=AiRelayObjectives.PlanScout(o,knowledge,registry,gameplay,ai,budget,reservedActor,OpeningScout,reservedActors);
            if(relay!=null&&!SurveyOutposts&&(!o.Domination.HasValue||AiRelayObjectives.Urgent(o)))return relay;
            bool OutpostSurvey(PlayablePublicScoutObjective target)=>SurveyOutposts&&target.Role==PlayablePublicScoutObjectiveRole.ExpansionOutpost&&
                !o.Sites.Any(s=>s.Site.Id==target.SiteId);
            var revisit=AiProfile.SecondsToTicks((ai.Value("scouting.revisitSeconds")*RevisitScale),30);
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
                    Guard("unit.explorer",u.Id,0,u.Kind==PlayableEntityKind.Explorer)&&Guard("unit.stationary",u.Id,0,!u.Moving||AiRelayObjectives.HasRelays(o))&&Guard("unit.expansion-reserved",u.Id,0,u.Id!=reservedActor)&&reservedActors?.Contains(u.Id)!=true)
                where (OpeningScout==0||u.Id==OpeningScout)&&!u.Held&&!(u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Human)
                where Guard("unit.unclaimed",u.Id,0,registry.ArmyFor(u.Id)==0)&&Guard("budget.available",u.Id,0,claims.Length<budget)
                from target in o.PublicScoutObjectives.Where(t=>Guard("target.reachable",u.Id,t.SiteId,t.Reachable)&&t.Role!=PlayablePublicScoutObjectiveRole.Relay&&SafeCapturerObjective(o,gameplay,u,t,ai,knowledge))
                where Guard("target.unclaimed",u.Id,target.SiteId,!claims.Any(a=>Guard("claim.matchesObjective",u.Id,target.SiteId,a.Objective==Objective(target.SiteId)||a.Objective==PreparationObjective(target.SiteId))))
                let area=Value("area",u.Id,target.SiteId,knowledge.Areas.SingleOrDefault(a=>Guard("area.matchesSite",u.Id,target.SiteId,a.AreaId==target.SiteId)))
                let gain=Value("gain",u.Id,target.SiteId,area==null?1:Math.Min(1,(o.Tick-area.ActuallyCoveredTick)/(double)revisit))
                where Guard("gain.accepted",u.Id,target.SiteId,target.SiteId==preparationSite||gain>=1)
                let route=Value("route",u.Id,target.SiteId,o.RouteProofs.SingleOrDefault(r=>Guard("route.matchesUnit",u.Id,target.SiteId,r.UnitId==u.Id)&&
                    Guard("route.publicObjective",u.Id,target.SiteId,r.Kind==PlayableRouteTargetKind.PublicObjective)&&Guard("route.matchesSite",u.Id,target.SiteId,r.TargetId==target.SiteId)&&Guard("route.matchesApproach",u.Id,target.SiteId,r.Target.Equals(target.Approach))))
                where Guard("route.exists",u.Id,target.SiteId,route!=null)&&Guard("route.path.nonempty",u.Id,target.SiteId,route.Path.Count>0)
                let travel=Value("travel",u.Id,target.SiteId,RouteLength(route)/(RouteLength(route)+gameplay.ExplorerSpeed*(ai.Value("scouting.revisitSeconds")*RevisitScale)))
                let danger=Value("danger",u.Id,target.SiteId,knowledge.Contacts.Where(c=>Guard("danger.contact.withinVision",u.Id,target.SiteId,Distance(c.Position,target.Approach)<=gameplay.ExplorerVision))
                    .Select(c=>Value("danger.contact.confidence",u.Id,target.SiteId,c.Confidence)).DefaultIfEmpty(0).Max())
                let score=Value("score",u.Id,target.SiteId,ai.Value("utility.scout.informationGain")*gain-ai.Value("utility.scout.travelCost")*travel-ai.Value("utility.scout.riskCost")*danger)
                orderby target.SiteId==preparationSite descending,OutpostSurvey(target) descending,score descending,target.SiteId,u.Id
                select new {u,target,route};
#else
            var candidates=from u in o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer&&(!u.Moving||AiRelayObjectives.HasRelays(o)))
                where (OpeningScout==0||u.Id==OpeningScout)&&!u.Held&&!(u.CurrentOrder!=null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Human)
                where u.Id!=reservedActor&&reservedActors?.Contains(u.Id)!=true&&registry.ArmyFor(u.Id)==0&&claims.Length<budget
                from target in o.PublicScoutObjectives.Where(t=>t.Reachable&&t.Role!=PlayablePublicScoutObjectiveRole.Relay&&SafeCapturerObjective(o,gameplay,u,t,ai,knowledge))
                where !claims.Any(a=>a.Objective==Objective(target.SiteId)||a.Objective==PreparationObjective(target.SiteId))
                let area=knowledge.Areas.SingleOrDefault(a=>a.AreaId==target.SiteId)
                let gain=area==null?1:Math.Min(1,(o.Tick-area.ActuallyCoveredTick)/(double)revisit)
                where target.SiteId==preparationSite||gain>=1
                let route=o.RouteProofs.SingleOrDefault(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.PublicObjective&&r.TargetId==target.SiteId&&r.Target.Equals(target.Approach))
                where route!=null&&route.Path.Count>0
                let travel=RouteLength(route)/(RouteLength(route)+gameplay.ExplorerSpeed*(ai.Value("scouting.revisitSeconds")*RevisitScale))
                let danger=knowledge.Contacts.Where(c=>Distance(c.Position,target.Approach)<=gameplay.ExplorerVision).Select(c=>c.Confidence).DefaultIfEmpty(0).Max()
                let score=ai.Value("utility.scout.informationGain")*gain-ai.Value("utility.scout.travelCost")*travel-ai.Value("utility.scout.riskCost")*danger
                orderby target.SiteId==preparationSite descending,OutpostSurvey(target) descending,score descending,target.SiteId,u.Id
                select new {u,target,route};
#endif
            var choice=candidates.FirstOrDefault();
#if UNITY_INCLUDE_TESTS
            if(capture)Record("final.choice",choice==null?0:choice.u.Id,choice==null?0:choice.target.SiteId,
                choice==null?null:(object)new{UnitId=choice.u.Id,SiteId=choice.target.SiteId,choice.route.Goal,choice.route.Target,choice.route.GeometryRevision});
#endif
            if(choice==null||relay!=null&&!o.Domination.HasValue&&choice.target.SiteId!=preparationSite&&!OutpostSurvey(choice.target))return relay;
            return new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,new[]{choice.u.Id},choice.route.Goal,siteId:choice.target.SiteId,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        internal static bool CombatInProgress(AiArmyState claim)=>
            claim.WithdrawalCondition!=null&&claim.WithdrawalCondition!=ResumeSurvey;

        internal static bool OwnsCombatMission(AiArmyState claim,PlayableAiAction action)=>
            claim.Role==AiArmyRole.Scout&&claim.TacticalOwner==Policy&&action.Kind==PlayableCommandKind.Attack&&
            action.TargetId>0&&action.SiteId>0&&action.EntityIds.Count==1&&claim.LeaderId==action.EntityIds[0]&&
            (claim.Objective==Objective(action.SiteId)||claim.Objective==PreparationObjective(action.SiteId)||
                claim.Objective==AiRelayObjectives.Claim(action.SiteId));

        private static bool MissionMatches(AiArmyState claim,PlayablePublicScoutObjective target)=>
            target.Role==PlayablePublicScoutObjectiveRole.Relay?claim.Objective==AiRelayObjectives.Claim(target.SiteId):
            claim.Objective==Objective(target.SiteId)||claim.Objective==PreparationObjective(target.SiteId);

        private static double Health(PlayableEntitySnapshot unit)=>unit.ExactHealth??unit.Health;
        private static bool VisibleEnemy(PlayableAiObservation o,PlayableEntitySnapshot unit)=>
            o.IsHostile(unit.Owner)&&unit.Health>0&&o.Vision?.IsVisible(unit.Position)==true;

        private static double Floor(AiArmyState claim,PlayableEntitySnapshot unit,AiProfile ai)=>
            double.TryParse(claim.WithdrawalCondition,NumberStyles.Float,CultureInfo.InvariantCulture,out var floor)
                ?floor:Health(unit)*(1-ai.Value(CombatLossShare));

        private static double Range(PlayableProfile p,PlayableEntitySnapshot unit)=>
            PlayableUnitRules.Range(p,unit.Kind,unit.Kind==PlayableEntityKind.Shkval&&unit.Upgraded);

        private static bool Affordable(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot scout,
            PlayableEntitySnapshot target,double floor)
        {
            if(target==null||!VisibleEnemy(o,target)||target.Kind==PlayableEntityKind.Hawk||Distance(scout.Position,target.Position)>p.ExplorerRange)
                return false;
            double Height(NavPoint point)=>(p.AuthoredMap?.SupportAt(point)?.HeightAt(point)??0)+(p.AuthoredMap?.DirectFireHeight??1);
            var from=new BallisticPoint(scout.Position.X,Height(scout.Position),scout.Position.Z);
            var to=new BallisticPoint(target.Position.X,Height(target.Position),target.Position.Z);
            if(!PlayableBallistics.TerrainLineClear(from,to,0,PlayableMap.SolidObstacles(p),p.BallisticWallHeight,
                point=>p.AuthoredMap?.SupportAt(point)?.HeightAt(point)??0)||
                o.Buildings.Any(b=>b.Health>0&&b.Phase!=ConstructionPhase.Pending&&
                    BuildingCombatGeometry.Pose(p,b.Kind,b.RefineryUpgraded,b.Position,
                        p.AuthoredMap?.SupportAt(b.Position)?.HeightAt(b.Position)??0,b.Heading,constructionProgress:b.Progress)
                        .Segment(from,to,0,out _,out _)))return false;

            // A whole firing cycle for the last partial burst and a ready enemy
            // volley account for unknown reload/aim state in a detached observation.
            double ExplorerCycle(PlayableEntitySnapshot unit)
            {
                int burst=unit.Upgraded?p.ExplorerAssaultBurstSize:p.ExplorerBurstSize;
                return (p.ExplorerBurstPauseMs+(burst-1)*p.ExplorerBurstShotIntervalMs)/1000d;
            }
            double Modifier(int level)
            {
                var research=level>=3?PlayableResearchKind.UltimateWeaponSystems:level==2?PlayableResearchKind.AdvancedWeaponSystems:PlayableResearchKind.ImprovedWeaponSystems;
                return level==0?1:1+PlayableResearchRules.Bonus(p,research)/100;
            }
            double scoutCycle=ExplorerCycle(scout);
            int scoutBurst=scout.Upgraded?p.ExplorerAssaultBurstSize:p.ExplorerBurstSize;
            double shot=PlayableStatRounding.Damage(p,p.ExplorerDamage*Modifier(scout.WeaponSystemsLevel));
            if(shot<=0||scoutCycle<=0)return false;
            double bearing=Math.Atan2(target.Position.Z-scout.Position.Z,target.Position.X-scout.Position.X);
            double angle=Math.Abs(Math.IEEERemainder(bearing-scout.TurretHeading,2*Math.PI));
            double seconds=Math.Ceiling(Health(target)/(shot*scoutBurst))*scoutCycle+
                angle/p.ExplorerTurretTurnSpeed+Distance(scout.Position,target.Position)/p.ExplorerProjectileSpeed;
            double incoming=0;
            foreach(var enemy in o.Entities.Where(e=>VisibleEnemy(o,e)&&AiRosterCatalog.Initial.Targets(e.Kind,"ground")))
            {
                if(Distance(enemy.Position,scout.Position)>Range(p,enemy)+PlayableUnitRules.Speed(p,enemy.Kind,enemy.Upgraded)*seconds)continue;
                double cycle=enemy.Kind==PlayableEntityKind.Explorer?ExplorerCycle(enemy):
                    enemy.Kind==PlayableEntityKind.Tank?p.TankWeaponReloadSeconds:
                    enemy.Kind==PlayableEntityKind.Shkval?PlayableUnitRules.ShkvalFireInterval(p,enemy.Upgraded):PlayableUnitRules.HawkSalvoCycle(p,enemy.Upgraded);
                double dps=PlayableUnitRules.DamagePerSecond(p,enemy.Kind,enemy.Upgraded);
                double volley=enemy.Kind==PlayableEntityKind.Hawk?2*p.HawkDamage:dps*cycle;
                incoming+=(1+Math.Floor(seconds/cycle))*Math.Ceiling(volley*Modifier(enemy.WeaponSystemsLevel));
            }
            foreach(var tower in o.Buildings.Where(b=>o.IsHostile(b.Owner)&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&
                TerritoryRules.ArmedDefense(b.Kind)&&o.Vision?.IsVisible(b.Position)==true&&
                AiPositionDevelopmentPlanner.Covers(o,p,b.Position,b.Kind,scout.Position,false,b.Id,scout.Id)))
            {
                if(tower.Kind==PlayableBuildingKind.GaussTower)
                    incoming+=(1+Math.Floor(seconds/p.GaussReloadSeconds))*p.GaussDamage*Modifier(3);
                else
                {
                    int burst=p.TurretBurstSize;
                    double cycle=(p.TurretBurstPauseMs+(burst-1)*p.TurretBurstShotIntervalMs)/1000d;
                    incoming+=(1+Math.Floor(seconds/cycle))*burst*PlayableStatRounding.Damage(p,p.TurretDamage*Modifier(3));
                }
            }
            return incoming<=Health(scout)-floor;
        }

        internal bool CanDeliverAttack(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiArmyRegistry registry,PlayableAiAction action)
        {
            var claim=registry.CaptureArmy(registry.ArmyFor(action.EntityIds.Single())).SingleOrDefault();
            var unit=o.Entities.FirstOrDefault(u=>u.Id==claim?.LeaderId&&u.Owner==o.Owner&&AiReinforcementPolicy.Available(u));
            return unit!=null&&OwnsCombatMission(claim,action)&&
                Affordable(o,p,unit,o.Entities.FirstOrDefault(e=>e.Id==action.TargetId),Floor(claim,unit,ai));
        }

        private static PlayableAiAction Continue(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,
            AiArmyState claim,int reservedActor)
        {
            var unit=o.Entities.FirstOrDefault(u=>u.Id==claim.LeaderId&&u.Owner==o.Owner&&u.Id!=reservedActor&&AiReinforcementPolicy.Available(u));
            if(unit==null)return null;
            var target=o.PublicScoutObjectives.Where(t=>t.Reachable&&MissionMatches(claim,t))
                .OrderBy(t=>claim.Rally.HasValue?Distance(t.Approach,claim.Rally.Value):0).ThenBy(t=>t.Role).FirstOrDefault();
            if(target==null)return null;
            PlayableAiAction Action(PlayableCommandKind kind,NavPoint point=default,int enemy=0,int site=0)=>
                new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,
                    kind,new[]{unit.Id},point,siteId:site,targetId:enemy,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);

            bool fighting=unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack;
            bool disengaging=claim.WithdrawalCondition!=null&&!fighting;
            var enemies=o.Entities.Where(e=>VisibleEnemy(o,e)).ToArray();
            if(fighting)
            {
                bool stalled=o.Tick-claim.ProgressTick>=AiProfile.SecondsToTicks(ai.Value("armies.stallSeconds"),30);
                if(!stalled&&Affordable(o,p,unit,enemies.FirstOrDefault(e=>e.Id==unit.CurrentOrder.TargetId),Floor(claim,unit,ai)))return null;
            }
            else if(!disengaging)
            {
                var enemy=enemies.Where(e=>Affordable(o,p,unit,e,Floor(claim,unit,ai)))
                    .OrderBy(e=>Distance(unit.Position,e.Position)).ThenBy(e=>e.Id).FirstOrDefault();
                if(enemy!=null)return Action(PlayableCommandKind.Attack,enemy.Position,enemy.Id,target.SiteId);
            }

            var routes=o.RouteProofs.Where(r=>r.UnitId==unit.Id&&r.Path.Count>0).ToArray();
            bool Clear(PlayableRouteProof r)=>AiTacticalExecutor.SafePath(o,r.Origin,r.Path,r.Radius,p)&&
                AiStaticDefenseSafety.Withdrawal(o,p,unit,r.Path,knowledge);
            var objectiveRoutes=routes.Where(r=>r.Kind==PlayableRouteTargetKind.PublicObjective&&
                r.TargetId==target.SiteId&&r.Target.Equals(target.Approach)).ToArray();
            var objective=objectiveRoutes.Where(Clear).OrderBy(RouteLength).FirstOrDefault();
            if(objective!=null)
            {
                if(target.Role==PlayablePublicScoutObjectiveRole.Relay&&AiRelayObjectives.Holding(o,p,target.SiteId,new[]{unit})||
                    Distance(unit.Position,objective.Goal)<=p.Navigation.ArrivalTolerance)
                    return fighting?Action(PlayableCommandKind.Stop):null;
                var pending=unit.Queue?.Pending;
                var stamp=unit.OrderStamp;
                bool ownMove=stamp?.Origin==PlayableOrderOrigin.Ai&&stamp.Kind==PlayableCommandKind.Move&&
                    stamp.Generation==o.Generation&&stamp.Source==PlayableAiOpeningComposition.SourceIdentity&&
                    stamp.Owner==o.Owner&&stamp.Tick>=claim.CreatedTick;
                bool failedMove=ownMove&&!unit.Moving&&unit.CurrentOrder==null&&
                    (unit.NavigationOutcome==NavigationOutcome.Rejected||unit.NavigationOutcome==NavigationOutcome.Blocked||
                     unit.NavigationOutcome==NavigationOutcome.Stopped&&pending?.PublicObjectiveId==target.SiteId&&
                     pending.CommandSequence==stamp.Sequence&&unit.Queue.PendingAiRouteViable==false);
                if(!fighting&&!disengaging&&failedMove&&o.Vision?.IsVisible(objective.Goal)!=true)
                {
                    // The full leg was refused without disclosing the blocker.
                    // Advance along the already known safe approach until we can see farther.
                    var step=VisibleScoutStep(o,p,unit,objective,knowledge,pending?.Anchor?.Position);
                    if(step.HasValue)return Action(PlayableCommandKind.Move,step.Value,site:target.SiteId);
                }
                // Normal surveys retain the existing stall window after failed
                // delivery. Only a combat interruption resumes a survey early.
                if(!fighting&&!disengaging&&!AiRelayObjectives.IsClaim(claim))return null;
                if(unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&unit.CurrentOrder.Destination.Equals(objective.Goal))return null;
                return Action(PlayableCommandKind.Move,objective.Goal,site:target.SiteId);
            }

            bool exposed=!AiTacticalExecutor.SafePath(o,unit.Position,new[]{unit.Position},PlayableUnitRules.Radius(p,unit.Kind),p)||
                !AiStaticDefenseSafety.Withdrawal(o,p,unit,new[]{unit.Position},knowledge);
            if(!fighting&&!exposed)
                return unit.CurrentOrder==null||objectiveRoutes.Length==0?null:Action(PlayableCommandKind.Stop);
            var withdrawal=routes.Where(r=>(AiTacticalExecutor.WithdrawalRoute(o,r,p)||
                r.Kind==PlayableRouteTargetKind.PublicObjective&&AiTacticalExecutor.WithdrawalPath(o,r.Origin,r.Path,r.Radius,p))&&
                AiStaticDefenseSafety.Withdrawal(o,p,unit,r.Path,knowledge))
                .OrderBy(RouteLength).ThenBy(r=>r.TargetId).FirstOrDefault();
            if(withdrawal!=null)
            {
                if(unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&unit.CurrentOrder.Destination.Equals(withdrawal.Goal))return null;
                return Action(PlayableCommandKind.Move,withdrawal.Goal);
            }
            return unit.CurrentOrder==null?null:Action(PlayableCommandKind.Stop);
        }

        internal void ObserveReceipt(AiArmyRegistry registry,PlayableAiAction action,PlayableAiDeliveryStatus status)
        {
            if((action.Kind==PlayableCommandKind.Attack)==(status==PlayableAiDeliveryStatus.Applied))return;
            var armyId=registry.ArmyFor(action.EntityIds.Single());
            if(registry.CaptureArmy(armyId).Any(a=>a.Role==AiArmyRole.Scout&&a.TacticalOwner==Policy))
                registry.SetScoutWithdrawalCondition(armyId,
                    status==PlayableAiDeliveryStatus.Applied&&action.SiteId==0?ResumeSurvey:null);
        }

        private static NavPoint? VisibleScoutStep(PlayableAiObservation observation,PlayableProfile profile,
            PlayableEntitySnapshot unit,PlayableRouteProof route,AiKnowledgeState knowledge,NavPoint? failedGoal)
        {
            if(observation.Vision==null)return null;
            var from=route.Origin;
            double stepSize=profile.VisionCellSize/2;
            double remaining=PlayableUnitRules.Vision(profile,unit.Kind);
            NavPoint? last=null;
            var prefix=new List<NavPoint>();
            foreach(var next in route.Path)
            {
                double length=Distance(from,next);
                if(length==0)continue;
                double limit=Math.Min(length,remaining);
                int samples=Math.Max(1,(int)Math.Ceiling(limit/stepSize));
                for(int i=1;i<=samples;i++)
                {
                    double t=limit*i/(samples*length);
                    var point=new NavPoint(from.X+(next.X-from.X)*t,from.Z+(next.Z-from.Z)*t);
                    if(!observation.Vision.IsVisible(point))return last;
                    if(Distance(unit.Position,point)<=profile.Navigation.ArrivalTolerance||
                       failedGoal.HasValue&&Distance(failedGoal.Value,point)<=profile.Navigation.ArrivalTolerance||
                       profile.AuthoredMap!=null&&!profile.AuthoredMap.SupportsFootprint(point,route.Radius))continue;
                    prefix.Add(point);
                    if(AiTacticalExecutor.SafePath(observation,route.Origin,prefix,route.Radius,profile)&&
                       AiStaticDefenseSafety.Withdrawal(observation,profile,unit,prefix,knowledge))
                        last=point;
                    prefix.RemoveAt(prefix.Count-1);
                }
                remaining-=limit;
                if(remaining<=0)break;
                prefix.Add(next);
                from=next;
            }
            return last;
        }

        private static double RouteLength(PlayableRouteProof r)
        {double length=0;var p=r.Origin;foreach(var point in r.Path){length+=Distance(p,point);p=point;}return length;}
        public void Commit(PlayableAiObservation o,PlayableAiAction action,AiArmyRegistry registry,int preparationSite=0,AiProfile ai=null)
        {
            long id=registry.ArmyFor(action.EntityIds.Single());
            if(id==0&&!registry.TryCreate(o,AiArmyRole.Scout,Policy,action.EntityIds,out id))throw new InvalidOperationException("Selected scout claim admission failed");
            var u=o.Entities.Single(e=>e.Id==action.EntityIds.Single());
            if(action.Kind==PlayableCommandKind.Attack)
            {
                var claim=registry.CaptureArmy(id).Single();
                registry.SetScoutWithdrawalCondition(id,Floor(claim,u,ai??AiProfile.Initial).ToString("R",CultureInfo.InvariantCulture));
                return;
            }
            if(action.SiteId==0)return; // A withdrawal retains the survey assignment.
            string objective=AiRelayObjectives.IsRelay(o,action.SiteId)?AiRelayObjectives.Claim(action.SiteId):action.SiteId==preparationSite?PreparationObjective(action.SiteId):Objective(action.SiteId);
            if(!registry.UpdateMission(id,Policy,AiArmyPhase.Advancing,o.Tick,objective,u.Position,action.Target))throw new InvalidOperationException("Scout tactical ownership changed");
        }
    }
}

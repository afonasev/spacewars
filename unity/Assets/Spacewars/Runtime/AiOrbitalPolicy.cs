using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Runtime.CompilerServices;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Only owner/team observations and public map geometry enter this planner.
    // Persistent choices, timers, knowledge and pending actions belong to their existing authorities.
    public static class AiOrbitalPolicy
    {
        public const string Policy="orbital";
        // Profiles are immutable; avoid reflecting hundreds of gameplay fields per candidate.
        private static readonly ConditionalWeakTable<PlayableProfile,NavGeometry> flatGeometry = new ConditionalWeakTable<PlayableProfile,NavGeometry>();
        private static readonly ConditionalWeakTable<PlayableProfile,PlayableProfileData> settings=new ConditionalWeakTable<PlayableProfile,PlayableProfileData>();
        private static PlayableProfileData Data(PlayableProfile p)=>settings.GetValue(p,key=>key.CopyData());
        internal static double ScanRadius(PlayableProfile p)=>Data(p).orbitalScanRadius;
        private sealed class Opportunity
        {
            internal int Ability,Entity;internal NavPoint Point;internal double Utility;
        }
        private static double V(AiProfile ai,string key)=>ai.Value("orbital."+key);
        private static double D(NavPoint a,NavPoint b)=>AiOpponentSelector.Distance(a,b);
        private static bool Visible(PlayableAiObservation o,NavPoint p)=>o.Vision?.IsVisible(p)==true;
        public static bool IsHack(PlayableAiAction a)=>a.Kind==PlayableCommandKind.UseOrbital&&a.TargetId==OrbitalAbilities.ControlHack;
        private static bool Ready(PlayableAiObservation o,int id)=>o.Orbital.CommonRemaining<=0&&o.Orbital.Abilities.Any(a=>a.Id==id&&a.Remaining<=0);
        private static double Health(PlayableEntitySnapshot u)=>u.ExactHealth??u.Health;
        private static double Health(PlayableBuildingSnapshot b)=>b.ExactHealth??b.Health;
        public static bool HackValid(PlayableAiObservation o,PlayableProfile p,int id)
        {
            var data=Data(p);double threshold=data.orbitalHackHealthPercent/100;
            var u=o.Entities.FirstOrDefault(e=>e.Id==id&&o.IsHostile(e.Owner)&&Visible(o,e.Position));
            if(u!=null)return u.StasisRemaining<=0&&Health(u)>0&&Health(u)<PlayableUnitRules.MaximumHealth(p,u)*threshold;
            var b=o.Buildings.FirstOrDefault(e=>e.Id==id&&e.Kind==PlayableBuildingKind.Mine&&o.IsHostile(e.Owner)&&Visible(o,e.Position));
            return b!=null&&Health(b)>0&&b.MaxHealth>0&&Health(b)<b.MaxHealth*threshold;
        }
        private static PlayableAiAction Action(PlayableAiObservation o,string source,PlayableCommandKind kind,int ability=0,int[] ids=null,NavPoint point=default(NavPoint),int objective=0)
            // Stateless local ID; the authority assigns a globally unique action ID before scheduling.
            =>new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,kind,ids,point,parentId:objective,targetId:ability,seed:o.Seed,sourceIdentity:source);
        public static PlayableAiAction Plan(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,AiPersonality personality,IEnumerable<PlayableAiTraceRecord> records,string source,Func<NavPoint,long> lastScan=null,bool allowLinkedReinforcement=true,double reactionSeconds=0)
        {
            if(o.Orbital.Level==0)return null;
            int choiceTier=o.Orbital.Options.Count>0?OrbitalAbilities.Tier(o.Orbital.Options[0]):0;
            var opportunities = Opportunities(o, p, ai, knowledge, lastScan, choiceTier==1, reactionSeconds).ToArray();
            if(o.Orbital.PendingLevel>0)
            {
                int choice=choiceTier==2?ChooseAdvanced(o,p,ai):o.Orbital.Options.OrderByDescending(id=>ChoiceScore(o,p,ai,personality,opportunities,id)).ThenBy(id=>id).FirstOrDefault();
                if(choice!=0)return Action(o,source,PlayableCommandKind.ChooseOrbital,choice);
            }
            if (Ready(o, OrbitalAbilities.NuclearStrike))
            {
                var terms = Data(p);
                var anchors = o.Entities.Where(unit => o.IsHostile(unit.Owner) && unit.Health > 0 && Visible(o,unit.Position))
                    .Select(unit => unit.Position).Concat(o.Buildings.Where(building =>
                        o.IsHostile(building.Owner) && building.Health > 0 && Visible(o,building.Position))
                        .Select(building => building.Position));
                var center = CastCenters(p,anchors,terms.orbitalNuclearRadius)
                    .Select(point => (Point:point, Count:NuclearTargetCount(o,p,terms,point)))
                    .Where(candidate => candidate.Count >= terms.orbitalNuclearMinimumTargets)
                    .OrderByDescending(candidate => candidate.Count)
                    .Select(candidate => (NavPoint?)candidate.Point).FirstOrDefault();
                if (center.HasValue) return Action(o,source,PlayableCommandKind.UseOrbital,
                    OrbitalAbilities.NuclearStrike,point:center.Value);
            }
            if (Ready(o, OrbitalAbilities.SystemFailure))
            {
                var towers = ApproachingTowers(o, p, reactionSeconds).ToArray();
                var center = CastCenters(p, towers.Select(t => t.Position), Data(p).orbitalSystemFailureRadius)
                    .Where(point => Visible(o, point))
                    .OrderByDescending(point => towers.Count(t => D(t.Position, point) <= Data(p).orbitalSystemFailureRadius))
                    .ThenBy(point => towers.Min(t => D(t.Position, point)))
                    .Select(point => (NavPoint?)point).FirstOrDefault();
                if (center.HasValue)
                    return Action(o, source, PlayableCommandKind.UseOrbital, OrbitalAbilities.SystemFailure, point: center.Value);
            }
            if(Ready(o,OrbitalAbilities.RepairNanobots))
            {
                var point=RepairOpportunity(o,p,Data(p));
                if(point.HasValue)return Action(o,source,PlayableCommandKind.UseOrbital,OrbitalAbilities.RepairNanobots,point:point.Value);
            }
            if(Ready(o,OrbitalAbilities.Stasis))
            {
                var point=new AiStasisOrbitalPolicy(o,p,Data(p)).BestPoint();
                if(point.HasValue)return Action(o,source,PlayableCommandKind.UseOrbital,OrbitalAbilities.Stasis,point:point.Value);
            }
            if(Ready(o,OrbitalAbilities.Reinforcement))
            {
                var wave=AiReinforcementOrbitalPolicy.Opportunities(o,p,Data(p),personality,knowledge,allowLinkedReinforcement)
                    .OrderByDescending(candidate=>candidate.Utility).ThenBy(candidate=>candidate.Objective)
                    .ThenBy(candidate=>candidate.Point.X).ThenBy(candidate=>candidate.Point.Z).FirstOrDefault();
                if(wave.Utility>0)return Action(o,source,PlayableCommandKind.UseOrbital,OrbitalAbilities.Reinforcement,
                    wave.Scout==0?null:new[]{wave.Scout},wave.Point,wave.Objective);
            }
            if(Ready(o,OrbitalAbilities.AcceleratedProduction))
            {
                var building=o.Buildings.Select(b=>(Building:b,Score:BoostUtility(o,p,b)))
                    .Where(x=>x.Score>0).OrderByDescending(x=>x.Score).ThenBy(x=>x.Building.Id)
                    .Select(x=>x.Building).FirstOrDefault();
                if(building!=null)return Action(o,source,PlayableCommandKind.UseOrbital,OrbitalAbilities.AcceleratedProduction,
                    new[]{building.Id},building.Position);
            }
            var best=opportunities.Where(c=>Ready(o,c.Ability)&&c.Utility>=V(ai,"minimumUtility"))
                .OrderByDescending(c=>c.Utility).ThenBy(c=>c.Ability).ThenBy(c=>c.Entity).ThenBy(c=>c.Point.X).ThenBy(c=>c.Point.Z).FirstOrDefault();
            if(best!=null)return Action(o,source,PlayableCommandKind.UseOrbital,best.Ability,best.Entity==0?null:new[]{best.Entity},best.Point);
            // A hack resets orders. Give that acquired actor a first useful ordinary order;
            // subsequent grouping, repair and fighting remain with the existing planners.
            var acquired=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.CurrentOrder==null&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&
                records.Any(r=>r.Policy==Policy&&r.Kind==PlayableCommandKind.UseOrbital&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ActionId==u.OrderStamp.ActionId)).OrderBy(u=>u.Id).FirstOrDefault();
            if(acquired==null)return null;
            var anchor=o.Entities.Where(u=>u.Id!=acquired.Id&&u.Owner==o.Owner&&u.Health>0).OrderBy(u=>D(u.Position,acquired.Position)).ThenBy(u=>u.Id).FirstOrDefault();
            if(anchor!=null)return Action(o,source,PlayableCommandKind.Follow,anchor.Id,new[]{acquired.Id},anchor.Position);
            var home=o.Buildings.Where(b=>b.Owner==o.Owner&&b.Health>0&&(b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)).OrderBy(b=>D(b.Position,acquired.Position)).FirstOrDefault();
            return home==null?null:Action(o,source,PlayableCommandKind.Move,ids:new[]{acquired.Id},point:home.Rally);
        }
        internal static int NuclearTargetCount(PlayableAiObservation observation, PlayableProfile profile,
            PlayableProfileData terms, NavPoint point)
        {
            if (!Visible(observation,point) || !OrbitalRepairNanobots.TryLocate(profile,point,null,out var center))
                return 0;
            double radius = terms.orbitalNuclearRadius;
            var visibleUnits = observation.Entities.Where(unit => unit.Health > 0 && Visible(observation,unit.Position));
            if (visibleUnits.Any(unit => !observation.IsHostile(unit.Owner) &&
                (OrbitalRepairNanobots.Contains(profile,center,radius,unit) ||
                    unit.CurrentOrder!=null && D(unit.CurrentOrder.Destination,point)<=radius)))
                return 0;
            int units = visibleUnits.Count(unit => observation.IsHostile(unit.Owner) && unit.StasisRemaining<=0 &&
                OrbitalRepairNanobots.Contains(profile,center,radius,unit));
            var visibleBuildings = observation.Buildings.Where(building => observation.IsHostile(building.Owner) &&
                building.Health > 0 && building.Phase != ConstructionPhase.Pending && Visible(observation,building.Position));
            var bases = visibleBuildings.Where(building => TerritoryRules.Center(building.Kind)).ToArray();
            int buildings = visibleBuildings.Count(building => bases.Any(baseBuilding =>
                building.Owner==baseBuilding.Owner && (building.SiteId>0 && building.SiteId==baseBuilding.SiteId ||
                    building.ParentId==baseBuilding.Id || building.Id==baseBuilding.Id)) &&
                OrbitalRepairNanobots.Contains(profile.AuthoredMap,center,radius,building.Position,null,null));
            return units + buildings;
        }

        private static double BoostUtility(PlayableAiObservation o, PlayableProfile p, PlayableBuildingSnapshot b)
        {
            var life=b.PrivateState?.Lifecycle;
            var data=Data(p);
            bool income=OrbitalProductionBoost.Income(b.Kind);
            if(b.Owner!=o.Owner||b.Phase!=ConstructionPhase.Ready||b.SystemsDisabled||b.BoostRemaining>0||
                life==null||life.Repairing||life.Selling||!Visible(o,b.Position)||
                Health(b)<=TerritoryRules.MaximumHealth(p,b)*OrbitalProductionBoost.HealthFloor(b.Kind,data)||
                (!income&&!OrbitalProductionBoost.Production(b.Kind)))return 0;
            if(o.Entities.Any(e=>o.IsHostile(e.Owner)&&e.Health>0&&
                D(e.Position,b.Position)<=PlayableUnitRules.Range(p,e.Kind)+TerritoryRules.Radius(p,b.Kind)))return 0;
            if(!income)
            {
                if(!b.BoostWorkRemaining.HasValue||b.BoostWorkRemaining.Value<=0)return 0;
                double work=b.BoostWorkRemaining.Value, scale=data.orbitalBoostProductionMultiplier;
                return Math.Min(work*(1-1/scale),data.orbitalBoostSeconds*(scale-1))*
                    PlayableUnitRules.Cost(p,PlayableEntityKind.Tank)/PlayableUnitRules.Duration(p,PlayableEntityKind.Tank);
            }
            double? reserve=b.Kind==PlayableBuildingKind.Mine?o.Sites.FirstOrDefault(s=>s.Site.Id==b.SiteId)?.MineReserve:null;
            if(reserve==0)return 0;
            double extra=OrbitalProductionBoost.ExtraIncome(p,data,b.Kind,b.RefineryUpgraded,reserve);
            var home=o.Buildings.FirstOrDefault(x=>x.Owner==o.Owner&&x.SiteId==o.HomeSiteId&&x.Kind==PlayableBuildingKind.Headquarters);
            return extra+(home==null?0:Math.Min(p.RefineryIncomePerPeriod,D(home.Position,b.Position)));
        }
        private static int ChooseAdvanced(PlayableAiObservation observation,PlayableProfile p,AiProfile ai)
        {
            var options=observation.Orbital.Options;
            if(options.Count==0)return 0;
            if (options.Contains(OrbitalAbilities.SystemFailure) && VisibleTowers(observation).Any(tower =>
                observation.Entities.Any(unit => CombatUnit(observation, unit) &&
                    D(unit.Position, tower.Position) <= AiPositionDevelopmentPlanner.Range(p, tower.Kind) +
                        PlayableUnitRules.Speed(p, unit.Kind, unit.Upgraded) * Data(p).orbitalSystemFailureSeconds / 2)))
                return OrbitalAbilities.SystemFailure;
            uint key=AiRandom.Key(observation.Seed,observation.OwnerId,ai.Hash,"orbital-advanced",observation.Orbital.PendingLevel);
            return options[(int)((ulong)key*(ulong)options.Count>>32)];
        }
        private static IEnumerable<PlayableEntitySnapshot> WoundedAllies(PlayableAiObservation observation, PlayableProfile profile)
            => observation.Entities.Where(unit => !observation.IsHostile(unit.Owner) && Visible(observation,unit.Position) &&
                OrbitalRepairNanobots.Wounded(profile,unit));

        private static IEnumerable<PlayableBuildingSnapshot> WoundedAlliedBuildings(PlayableAiObservation observation, PlayableProfile profile) =>
            observation.Buildings.Where(building => !observation.IsHostile(building.Owner) &&
                Visible(observation,building.Position) && building.Phase == ConstructionPhase.Ready && building.Health > 0 &&
                building.Health < TerritoryRules.MaximumHealth(profile,building) && building.PrivateState?.Lifecycle?.Selling != true);

        private static bool InRepairField(PlayableProfile profile, NavLocation center, double radius, PlayableBuildingSnapshot building) =>
            OrbitalRepairNanobots.Contains(profile.AuthoredMap,center,radius,building.Position,null,null);

        private static bool ValidRepairTarget(PlayableAiObservation observation, PlayableProfile profile,
            PlayableProfileData terms, NavPoint point)
        {
            return Visible(observation,point) && OrbitalRepairNanobots.TryLocate(profile,point,null,out var location) &&
                WoundedAllies(observation,profile).Count(unit =>
                    OrbitalRepairNanobots.Contains(profile,location,terms.orbitalNanobotsRadius,unit)) +
                WoundedAlliedBuildings(observation,profile).Count(building => InRepairField(profile,location,terms.orbitalNanobotsRadius,building))
                >= OrbitalRepairNanobots.MinimumWoundedForBot;
        }

        private static NavPoint? RepairOpportunity(PlayableAiObservation observation, PlayableProfile profile,
            PlayableProfileData terms)
        {
            var wounded = WoundedAllies(observation,profile).ToArray();
            var damagedBuildings = WoundedAlliedBuildings(observation,profile).ToArray();
            if (wounded.Length + damagedBuildings.Length < OrbitalRepairNanobots.MinimumWoundedForBot) return null;

            var underFire = observation.ObservedHostileShots.Select(shot => shot.TargetId).ToHashSet();
            var hostile = observation.Entities.Where(unit => observation.IsHostile(unit.Owner))
                .Select(unit => unit.Id).Concat(observation.Buildings.Where(building => observation.IsHostile(building.Owner))
                    .Select(building => building.Id)).ToHashSet();
            NavPoint? best = null;
            int bestCombat = -1, bestStaying = -1;
            double bestHealth = -1;
            double radius = terms.orbitalNanobotsRadius;
            foreach (var point in CastCenters(profile,wounded.Select(unit => unit.Position).Concat(damagedBuildings.Select(building => building.Position)),radius)
                .OrderBy(candidate => candidate.X).ThenBy(candidate => candidate.Z))
            {
                if (!Visible(observation,point) || !OrbitalRepairNanobots.TryLocate(profile,point,null,out var location))
                    continue;
                int count = 0, combat = 0, staying = 0;
                double recovery = 0;
                foreach (var unit in wounded)
                {
                    if (!OrbitalRepairNanobots.Contains(profile,location,radius,unit)) continue;
                    count++;
                    bool expectedToStay = !unit.Moving || unit.CurrentOrder == null || D(unit.CurrentOrder.Destination,point) <= radius;
                    if (expectedToStay) staying++;
                    if (expectedToStay && (underFire.Contains(unit.Id) || hostile.Contains(unit.TargetId))) combat++;
                    recovery += Math.Min(terms.orbitalNanobotsHealthPerSecond * terms.orbitalNanobotsSeconds,
                        PlayableUnitRules.MaximumHealth(profile,unit) - Health(unit));
                }
                foreach (var building in damagedBuildings)
                {
                    if (!InRepairField(profile, location, radius, building)) continue;
                    count++;
                    staying++;
                    if (underFire.Contains(building.Id)) combat++;
                    recovery += Math.Min(terms.orbitalNanobotsHealthPerSecond * terms.orbitalNanobotsSeconds,
                        TerritoryRules.MaximumHealth(profile,building) - building.Health);
                }
                if (count < OrbitalRepairNanobots.MinimumWoundedForBot || combat < bestCombat ||
                    combat == bestCombat && (staying < bestStaying || staying == bestStaying && recovery <= bestHealth))
                    continue;
                best = point;
                bestCombat = combat;
                bestStaying = staying;
                bestHealth = recovery;
            }
            return best;
        }

        private static double ChoiceScore(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiPersonality personality,Opportunity[] options,int id)
        {
            double score=options.Where(c=>c.Ability==id).Select(c=>c.Utility).DefaultIfEmpty(0).Max();
            if(id==1)score+=V(ai,"scanUtility")*(o.Entities.Any(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Shkval)?1:0);
            if(id==2&&o.Entities.Any(u=>o.IsHostile(u.Owner))&&!o.Entities.Any(u=>o.IsHostile(u.Owner)&&u.Kind!=PlayableEntityKind.Hawk))score=0;
            if(id==OrbitalAbilities.ControlHack)score+=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0).Select(u=>PlayableUnitRules.Cost(p,u.Kind)*V(ai,"hackValueScale")*V(ai,"hackSurvivalFloor")).DefaultIfEmpty(0).Max();
            if(personality==AiPersonality.Defensive&&id==2||personality==AiPersonality.Aggressive&&id==OrbitalAbilities.ControlHack||personality==AiPersonality.Harassing&&id==1)score+=V(ai,"personalityBonus");
            return score+V(ai,"choiceVariation")*(AiRandom.Key(o.Seed,o.OwnerId,ai.Hash,"orbital-choice-"+id,o.Orbital.PendingLevel)/(double)uint.MaxValue);
        }
        public static bool CanDeliver(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,PlayableAiAction action,Func<NavPoint,long> lastScan=null)
        {
            if(action.Kind==PlayableCommandKind.ChooseOrbital)return o.Orbital.Options.Contains(action.TargetId);
            if(!Ready(o,action.TargetId))return false;
            if(action.TargetId==OrbitalAbilities.NuclearStrike)
                return NuclearTargetCount(o,p,Data(p),action.Target)>=Data(p).orbitalNuclearMinimumTargets;
            if(action.TargetId==OrbitalAbilities.RepairNanobots)
                return ValidRepairTarget(o,p,Data(p),action.Target);
            if(action.TargetId==OrbitalAbilities.Stasis)return new AiStasisOrbitalPolicy(o,p,Data(p)).WorthCasting(action.Target);
            if(action.TargetId==OrbitalAbilities.Reinforcement)
                return AiReinforcementOrbitalPolicy.HasResources(o)&&AiReinforcementOrbitalPolicy.SafePlacement(o,p,Data(p),action.Target,knowledge)&&
                    (action.ParentId==0||o.Buildings.Any(b=>b.Id==action.ParentId&&o.IsHostile(b.Owner)&&Visible(o,b.Position))&&
                    o.Entities.Any(u=>action.EntityIds.Contains(u.Id)&&u.Owner==o.Owner&&u.Health>0&&u.Kind==PlayableEntityKind.Explorer));
            if(action.TargetId==OrbitalAbilities.AcceleratedProduction)
                return action.EntityIds.Count==1&&o.Buildings.Any(b=>b.Id==action.EntityIds[0]&&BoostUtility(o,p,b)>0);
            if(IsHack(action))return action.EntityIds.Count==1&&HackValid(o,p,action.EntityIds[0]);
            if (action.TargetId == OrbitalAbilities.SystemFailure)
                return InBounds(p, action.Target) && Visible(o, action.Target) &&
                    ApproachingTowers(o, p, 0).Any(t => D(t.Position, action.Target) <= Data(p).orbitalSystemFailureRadius);
            if (action.TargetId == 1)
                return ScanUtility(o, p, action.Target, ScanInterests(o, ai, knowledge, lastScan)) >= V(ai, "minimumUtility");
            if (action.TargetId == 2)
                return MineAllowed(o, p, ai, action.Target) && MineWorthReplacing(o, p, ai, action.Target, GroundApproaches(o, p, ai, knowledge));
            return false;
        }
        private static IEnumerable<Opportunity> Opportunities(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,Func<NavPoint,long> lastScan, bool choosing, double reactionSeconds)
        {
            bool considerHack = choosing && o.Orbital.Options.Contains(OrbitalAbilities.ControlHack) || Ready(o, OrbitalAbilities.ControlHack);
            foreach(var u in o.Entities.Where(u=>considerHack&&HackValid(o,p,u.Id)))
            {
                double ratio=PlayableUnitRules.HealthRatio(p,u);
                double localEnemies=o.Entities.Count(e=>o.IsHostile(e.Owner)&&e.Id!=u.Id&&D(e.Position,u.Position)<=PlayableUnitRules.Range(p,e.Kind));
                double localFriends=o.Entities.Count(e=>!o.IsHostile(e.Owner)&&D(e.Position,u.Position)<=PlayableUnitRules.Vision(p,e.Kind));
                double survival=(ratio+V(ai,"hackSurvivalFloor"))/(1+Math.Max(0,localEnemies-localFriends));
                bool urgent=o.OwnCenterDamage.Any(h=>h.AttackerId==u.Id);
                yield return new Opportunity{Ability=OrbitalAbilities.ControlHack,Entity=u.Id,Point=u.Position,Utility=PlayableUnitRules.Cost(p,u.Kind)*V(ai,"hackValueScale")*survival+(urgent?V(ai,"hackThreatBonus"):0)};
            }
            foreach(var b in o.Buildings.Where(b=>considerHack&&b.Kind==PlayableBuildingKind.Mine&&HackValid(o,p,b.Id)))
            {
                double hostile=o.Entities.Count(u=>o.IsHostile(u.Owner)&&D(u.Position,b.Position)<=PlayableUnitRules.Vision(p,u.Kind));
                double friendly=o.Entities.Count(u=>!o.IsHostile(u.Owner)&&D(u.Position,b.Position)<=PlayableUnitRules.Vision(p,u.Kind));
                yield return new Opportunity{Ability=OrbitalAbilities.ControlHack,Entity=b.Id,Point=b.Position,Utility=TerritoryRules.Cost(p,b.Kind)*V(ai,"hackValueScale")/(1+Math.Max(0,hostile-friendly))};
            }
            if (choosing && o.Orbital.Options.Contains(1) || Ready(o, 1))
            {
                var interests = ScanInterests(o, ai, knowledge, lastScan);
                foreach (var point in CastCenters(p, interests.Select(i => i.Point), Data(p).orbitalScanRadius))
                {
                    double utility = ScanUtility(o, p, point, interests);
                    if (utility > 0)
                        yield return new Opportunity { Ability = 1, Point = point, Utility = utility };
                }
            }
            if (choosing && o.Orbital.Options.Contains(2) || Ready(o, 2))
            {
                var approaches = GroundApproaches(o, p, ai, knowledge);
                foreach (var point in approaches.SelectMany(a => MinePoints(p, a, reactionSeconds)).Distinct())
                    if (MineAllowed(o, p, ai, point) && MineWorthReplacing(o, p, ai, point, approaches))
                        yield return new Opportunity { Ability = 2, Point = point, Utility = MineUtility(p, point, approaches) };
            }
        }

        private static bool InBounds(PlayableProfile p, NavPoint point)
            => Math.Abs(point.X) <= p.ArenaHalfExtent && Math.Abs(point.Z) <= p.ArenaHalfExtent;

        private static NavPoint Interpolate(NavPoint from, NavPoint to, double t)
            => new NavPoint(from.X + (to.X - from.X) * t, from.Z + (to.Z - from.Z) * t);

        // Evaluate the anchor and nearby centers so one circle can cover adjacent interests
        // or reach past the edge of current allied vision.
        private static IEnumerable<NavPoint> CastCenters(PlayableProfile p, IEnumerable<NavPoint> anchors, double radius)
            => anchors.Distinct().SelectMany(anchor => DiskSamples(anchor, radius / 2))
                .Where(point => InBounds(p, point)).Distinct();

        // Uniform lattice quadrature of the disk, including the exact center and cardinal edges.
        private static IEnumerable<NavPoint> DiskSamples(NavPoint center, double radius)
        {
            for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 2; z++)
                    if (x*x + z*z <= 4)
                        yield return new NavPoint(center.X + x * radius / 2, center.Z + z * radius / 2);
        }

        private static (NavPoint Point, double Utility)[] ScanInterests(PlayableAiObservation o,
            AiProfile ai, AiKnowledgeState knowledge, Func<NavPoint, long> lastScan)
        {
            double utility = V(ai, "scanUtility");
            long revisit = AiProfile.SecondsToTicks(ai.Value("scouting.revisitSeconds"), 30);
            bool RecentlyScanned(NavPoint point, long contactTick = -1)
            {
                long prior = lastScan?.Invoke(point) ?? -1;
                return prior >= 0 && o.Tick - prior < revisit && contactTick <= prior;
            }

            bool enemyBaseKnown = knowledge.Contacts.Any(c => c.Building && c.Kind == (int)PlayableBuildingKind.Headquarters && o.IsHostile(c.Owner));
            var objectives = o.PublicScoutObjectives.Where(t => t.Reachable &&
                !knowledge.Areas.Any(a => a.AreaId == t.SiteId && !a.RevisitDue))
                .Select(t => (Point: o.Intel.Areas.FirstOrDefault(a => a.Envelope.AreaId == t.SiteId)?.Envelope.Placement.FirstOrDefault().Position ?? t.Approach,
                    Utility: utility + (!enemyBaseKnown && t.Role == PlayablePublicScoutObjectiveRole.PossibleEnemyStart ? V(ai, "baseSearchBonus") : 0)))
                .Where(t => !RecentlyScanned(t.Point));
            var contacts = knowledge.Contacts.Where(c => !c.Visible && o.IsHostile(c.Owner) && !Visible(o, c.Position) &&
                !RecentlyScanned(c.Position, c.LastSeenTick)).Select(c => (Point: c.Position, Utility: utility * c.Confidence));
            var attacks = o.Entities.Where(u => u.Owner == o.Owner && u.Health > 0 && AttackDestination(o, u).HasValue)
                .Select(u => AttackDestination(o, u).Value).Distinct().Where(point => !RecentlyScanned(point))
                .Select(point => (Point: point, Utility: utility + V(ai, "engagementBonus")));
            return objectives.Concat(contacts).Concat(attacks).ToArray();
        }

        private static double ScanUtility(PlayableAiObservation o, PlayableProfile p, NavPoint center,
            (NavPoint Point, double Utility)[] interests)
        {
            if (!InBounds(p, center) || interests.Length == 0) return 0;
            double radius = Data(p).orbitalScanRadius;
            var covered = interests.Where(i => D(center, i.Point) <= radius).ToArray();
            if (covered.Length == 0) return 0;

            double score = 0;
            int samples = 0;
            foreach (var point in DiskSamples(center, radius))
            {
                samples++;
                if (!InBounds(p, point) || Visible(o, point)) continue;
                // Nearby interests reinforce useful fog without stacking identical
                // objectives or dozens of unit orders at the same point.
                double best = 0;
                foreach (var interest in covered)
                {
                    double proximity = 1 - D(point, interest.Point) / radius;
                    if (proximity > 0) best = Math.Max(best, interest.Utility * proximity);
                }
                score += best;
            }
            // Also credit an exact obscured interest when it falls between samples.
            double exact = covered.Where(i => !Visible(o, i.Point))
                .Select(i => i.Utility * (1 - D(center, i.Point) / radius)).DefaultIfEmpty(0).Max();
            return (score + exact) / (samples + 1) * 2;
        }

        private static NavPoint? AttackDestination(PlayableAiObservation o, PlayableEntitySnapshot unit)
        {
            var order = unit.CurrentOrder;
            if (order?.Kind == PlayableTacticalOrderKind.AttackMove) return order.Destination;
            if (order?.Kind != PlayableTacticalOrderKind.Attack) return null;
            return o.Buildings.FirstOrDefault(b => b.Id == order.TargetId && o.IsHostile(b.Owner))?.Position ??
                o.Entities.FirstOrDefault(e => e.Id == order.TargetId && o.IsHostile(e.Owner))?.Position;
        }

        private static IEnumerable<PlayableBuildingSnapshot> VisibleTowers(PlayableAiObservation o)
            => o.Buildings.Where(b => o.IsHostile(b.Owner) && b.Health > 0 && b.Phase == ConstructionPhase.Ready &&
                !b.SystemsDisabled && TerritoryRules.Defensive(b.Kind) && Visible(o, b.Position));

        private static bool CombatUnit(PlayableAiObservation o, PlayableEntitySnapshot unit)
            => unit.Owner == o.Owner && unit.Health > 0 && unit.Kind != PlayableEntityKind.Explorer &&
                unit.MaterializationProgress >= 1 && unit.StasisRemaining <= 0;

        private static IEnumerable<PlayableBuildingSnapshot> ApproachingTowers(PlayableAiObservation o, PlayableProfile p, double reactionSeconds)
        {
            var towers = VisibleTowers(o).ToArray();
            if (towers.Length == 0) yield break;

            var attacks = o.Entities.Where(u => CombatUnit(o, u))
                .Select(u => (Unit: u, Destination: AttackDestination(o, u))).Where(a => a.Destination.HasValue).ToArray();
            foreach (var tower in towers)
                if (attacks.Any(a => EntersTowerCoverage(o, p, a.Unit, a.Destination.Value, tower, reactionSeconds)))
                    yield return tower;
        }

        private static bool EntersTowerCoverage(PlayableAiObservation o, PlayableProfile p, PlayableEntitySnapshot unit,
            NavPoint destination, PlayableBuildingSnapshot tower, double reactionSeconds)
        {
            bool air = unit.Kind == PlayableEntityKind.Hawk;
            double range = AiPositionDevelopmentPlanner.Range(p, tower.Kind);
            double initial = D(tower.Position, unit.Position);
            double speed = PlayableUnitRules.Speed(p, unit.Kind, unit.Upgraded);
            // Reserve the second half of the effect for the fight itself.
            double reach = speed * (Data(p).orbitalSystemFailureSeconds / 2 + reactionSeconds);
            double beforeDelivery = speed * reactionSeconds;
            if (initial > range + reach || D(tower.Position, destination) >= initial ||
                tower.Kind == PlayableBuildingKind.GaussTower && air) return false;

            bool Covered(NavPoint point) => AiPositionDevelopmentPlanner.Covers(o, p, tower.Position, tower.Kind, point, air, tower.Id, unit.Id);
            if (Covered(unit.Position)) return false;

            var order = unit.CurrentOrder;
            var route = o.RouteProofs.FirstOrDefault(r => r.UnitId == unit.Id && r.Origin.Equals(unit.Position) &&
                (order.Kind == PlayableTacticalOrderKind.Attack && r.TargetId == order.TargetId ||
                 r.Target.Equals(destination) || r.Goal.Equals(destination)));
            if (route == null && !unit.Moving) return false;
            var before = unit.Position;
            foreach (var end in route?.Path ?? new[] { destination })
            {
                double length = D(before, end);
                if (length == 0) continue;
                var limit = Interpolate(before, end, Math.Min(1, reach / length));
                if (route == null && !air && !GroundSweep(p, before, limit, PlayableUnitRules.Radius(p, unit.Kind)))
                    return false;
                double segmentReach = Math.Min(length, reach);
                double nearest = SegmentFraction(tower.Position, before, limit) * segmentReach;
                if (D(tower.Position, Interpolate(before, end, nearest / length)) <= range)
                {
                    // Sample the entry as well as the closest point, since terrain may
                    // occlude only part of the crossing.
                    double step = PlayableUnitRules.Radius(p, unit.Kind);
                    int samples = Math.Max(1, (int)Math.Ceiling(segmentReach / step));
                    foreach (double distance in Enumerable.Range(1, samples).Select(i => i * segmentReach / samples).Append(nearest).OrderBy(d => d))
                        if (Covered(Interpolate(before, end, distance / length)))
                            return distance > beforeDelivery;
                }
                beforeDelivery -= length;
                reach -= length;
                if (reach <= 0) break;
                before = end;
            }
            return false;
        }

        private sealed class GroundApproach
        {
            public NavPoint From, To;
            public double Utility, UnitRadius, Speed;
        }

        private static GroundApproach[] GroundApproaches(PlayableAiObservation o, PlayableProfile p, AiProfile ai, AiKnowledgeState knowledge)
        {
            double range = V(ai, "mineGuardRadius");
            double approachUtility = V(ai, "mineUtility");
            double movementBonus = V(ai, "mineContactBonus");
            var ground = o.Entities.Where(u => o.IsHostile(u.Owner) && u.Kind != PlayableEntityKind.Hawk &&
                u.Health > 0 && Visible(o, u.Position)).ToArray();
            var assets = o.Buildings.Where(b => !o.IsHostile(b.Owner) && b.Health > 0 && b.Phase != ConstructionPhase.Pending &&
                (b.Kind == PlayableBuildingKind.Headquarters || b.Kind == PlayableBuildingKind.Outpost || b.Kind == PlayableBuildingKind.Mine)).ToArray();
            var approaches = new List<GroundApproach>();
            foreach (var unit in ground)
            {
                double radius = PlayableUnitRules.Radius(p, unit.Kind);
                if (unit.Moving)
                {
                    var ahead = new NavPoint(unit.Position.X + Math.Cos(unit.HullHeading) * range, unit.Position.Z + Math.Sin(unit.HullHeading) * range);
                    AddGroundApproach(p, approaches, unit.Position, ahead, radius, approachUtility + movementBonus,
                        PlayableUnitRules.Speed(p, unit.Kind, unit.Upgraded));
                }
                else
                    Infer(unit.Position, radius, 1);
            }
            foreach (var contact in knowledge.Contacts.Where(c => !c.Visible && !c.Building && o.IsHostile(c.Owner) &&
                c.Kind != (int)PlayableEntityKind.Hawk && c.HealthAtLastSeen > 0 && !Visible(o, c.Position)))
                Infer(contact.Position, PlayableUnitRules.Radius(p, (PlayableEntityKind)contact.Kind), contact.Confidence);
            return approaches.ToArray();

            void Infer(NavPoint from, double radius, double confidence)
            {
                var asset = assets.Where(b => D(b.Position, from) <= range).OrderBy(b => D(b.Position, from)).ThenBy(b => b.Id).FirstOrDefault();
                if (asset == null) return;
                double length = D(asset.Position, from);
                double outside = TerritoryRules.Radius(p, asset.Kind) + radius;
                if (length > outside)
                    AddGroundApproach(p, approaches, from, Interpolate(asset.Position, from, outside / length),
                        radius, approachUtility * confidence, 0);
            }
        }

        private static bool GroundSweep(PlayableProfile p, NavPoint from, NavPoint to, double radius)
            => InBounds(p, to) && (p.AuthoredMap?.SupportsSweep(from, to, radius) ??
                flatGeometry.GetValue(p, profile => new NavGeometry(profile.ArenaHalfExtent, PlayableMap.StaticObstacles(profile), 1))
                    .SegmentFree(from, to, radius));

        private static void AddGroundApproach(PlayableProfile p, List<GroundApproach> approaches,
            NavPoint from, NavPoint to, double radius, double utility, double speed)
        {
            double length = D(from, to);
            double clearance = p.MineEnemyClearanceRadius;
            if (length <= clearance) return;
            // Find the useful prefix when the projection ends at a wall or map edge.
            double low = 0, high = 1;
            if (GroundSweep(p, from, to, radius)) low = 1;
            else if (speed == 0) return;
            else while ((high - low) * length > radius / 2)
            {
                double middle = (low + high) / 2;
                if (GroundSweep(p, from, Interpolate(from, to, middle), radius)) low = middle;
                else high = middle;
            }
            if (low * length > clearance)
                approaches.Add(new GroundApproach { From = from, To = Interpolate(from, to, low), UnitRadius = radius, Utility = utility, Speed = speed });
        }

        private static IEnumerable<NavPoint> MinePoints(PlayableProfile p, GroundApproach approach, double reactionSeconds)
        {
            double length = D(approach.From, approach.To);
            double step = Data(p).orbitalMineModelRadius + approach.UnitRadius;
            for (double distance = p.MineEnemyClearanceRadius + step + approach.Speed * reactionSeconds; distance <= length; distance += step)
                yield return Interpolate(approach.From, approach.To, distance / length);
        }

        private static bool MineAllowed(PlayableAiObservation o,PlayableProfile p,AiProfile ai,NavPoint point)
        {
            var d=Data(p);double blast=d.orbitalMineBlastRadius+V(ai,"mineSafetyMargin");
            if(!Visible(o,point)||Math.Abs(point.X)>p.ArenaHalfExtent||Math.Abs(point.Z)>p.ArenaHalfExtent||p.AuthoredMap!=null&&!p.AuthoredMap.SupportsFootprint(point,d.orbitalMineModelRadius))return false;
            if(o.Entities.Any(u => o.IsHostile(u.Owner) && u.Kind!=PlayableEntityKind.Hawk && u.Health>0 && Visible(o,u.Position) &&
                OrbitalMinePlacement.Blocks(p,point,u.Position,u.Location))) return false;
            if(o.Mines.Any(m=>!o.IsHostile(m.Owner)&&D(m.Point,point)<Math.Max(V(ai,"mineSpacing"),m.BlastRadius+d.orbitalMineBlastRadius)))return false;
            if(o.Entities.Any(u=>!o.IsHostile(u.Owner)&&u.Health>0&&(D(u.Position,point)<=blast+PlayableUnitRules.Radius(p,u.Kind)||u.CurrentOrder!=null&&SegmentDistance(point,u.Position,u.CurrentOrder.Destination)<=blast+PlayableUnitRules.Radius(p,u.Kind))))return false;
            if(o.Buildings.Any(b=>b.Health>0&&D(b.Position,point)<=TerritoryRules.Radius(p,b.Kind)+(!o.IsHostile(b.Owner)?blast:d.orbitalMineModelRadius)))return false;
            return true;
        }
        private static double MineUtility(PlayableProfile p, NavPoint point, GroundApproach[] approaches)
        {
            double utility = 0;
            foreach (var approach in approaches)
            {
                double trigger = Data(p).orbitalMineModelRadius + approach.UnitRadius;
                var intercept = Interpolate(approach.From, approach.To, SegmentFraction(point, approach.From, approach.To));
                if (D(point, intercept) > trigger) continue;
                var map = p.AuthoredMap;
                if (map != null && (!map.TryLocate(point, 0, out var mineLocation) ||
                    !map.TryLocate(intercept, approach.UnitRadius, out var groundLocation) ||
                    !map.CompatibleCombatSurface(mineLocation, groundLocation))) continue;

                // Prefer the first safe intercept along a probable route.
                double remaining = 1 - Math.Max(0, D(approach.From, intercept) - p.MineEnemyClearanceRadius) / D(approach.From, approach.To);
                utility = Math.Max(utility, approach.Utility * remaining);
            }
            return utility;
        }

        private static bool MineWorthReplacing(PlayableAiObservation o, PlayableProfile p, AiProfile ai, NavPoint point, GroundApproach[] approaches)
        {
            double utility = MineUtility(p, point, approaches);
            if (utility < V(ai, "minimumUtility")) return false;
            var owned = o.Mines.Where(m => m.Owner == o.Owner).OrderBy(m => m.Id).ToArray();
            return owned.Length < Data(p).orbitalMineCapacity ||
                utility > MineUtility(p, owned[0].Point, approaches) + V(ai, "replacementGain");
        }

        private static double SegmentFraction(NavPoint p, NavPoint a, NavPoint b)
        {
            double x = b.X - a.X, z = b.Z - a.Z, length = x*x + z*z;
            return length == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X-a.X)*x + (p.Z-a.Z)*z) / length));
        }

        private static double SegmentDistance(NavPoint p, NavPoint a, NavPoint b)
            => D(p, Interpolate(a, b, SegmentFraction(p, a, b)));
    }
    public sealed partial class AiDecisionArbiter
    {
        private const string OrbitalScanPrefix="orbital-scan:";
        internal void RememberOrbitalScan(NavPoint point,long tick)
        {
            string key=OrbitalScanPrefix+point.X.ToString("R",CultureInfo.InvariantCulture)+":"+point.Z.ToString("R",CultureInfo.InvariantCulture);
            history[key]=new History{FirstTick=tick,Conditions="observed"};
        }
        internal long LastOrbitalScan(NavPoint point,double radius)
        {
            long latest=-1;
            foreach(var pair in history.Where(x=>x.Key.StartsWith(OrbitalScanPrefix,StringComparison.Ordinal)))
                if(TryScanPoint(pair.Key,out var p)&&AiOpponentSelector.Distance(point,p)<=radius)latest=Math.Max(latest,pair.Value.FirstTick);
            return latest;
        }
        private static bool TryScanPoint(string key,out NavPoint point)
        {
            point=default;var parts=key.Substring(OrbitalScanPrefix.Length).Split(':');
            if(parts.Length!=2||!double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out double x)||!double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out double z)||double.IsNaN(x)||double.IsNaN(z)||double.IsInfinity(x)||double.IsInfinity(z))return false;
            point=new NavPoint(x,z);return true;
        }
    }

}

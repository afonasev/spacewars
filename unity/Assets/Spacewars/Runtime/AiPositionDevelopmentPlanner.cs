using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    public sealed class AiPositionInvestment
    {
        internal AiPositionInvestment(PlayableAiAction action,double utility,string reason){Action=action;Utility=utility;Reason=reason;}
        public PlayableAiAction Action{get;}
        public double Utility{get;}
        public string Reason{get;}
    }
    // Recomputed from detached owner knowledge. Paid foundations suppress duplicate
    // purchases, but only Ready, compatible, locally useful towers provide coverage.
    public static class AiPositionDevelopmentPlanner
    {
#if UNITY_INCLUDE_TESTS
        [ThreadStatic] internal static Action<string,object> DiagnosticForTests;
#endif
        [System.Diagnostics.Conditional("UNITY_INCLUDE_TESTS")]
        private static void Trace(string stage,object details)
        {
#if UNITY_INCLUDE_TESTS
            DiagnosticForTests?.Invoke(stage,details);
#endif
        }
        internal static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        internal static bool Live(PlayableBuildingSnapshot b)=>b.PrivateState?.Lifecycle?.Selling!=true&&(b.Health>0||b.Phase!=ConstructionPhase.Ready);
        internal static double Range(PlayableProfile p,PlayableBuildingKind kind)=>TerritoryRules.AiDefenseRange(p,kind);
        private static double Height(PlayableProfile p,NavPoint at)=>p.AuthoredMap?.SupportAt(at)?.HeightAt(at)??0;
        // Use the shared physical terrain/mesh queries, never hidden world buildings.
        public static bool Covers(PlayableAiObservation o,PlayableProfile p,NavPoint from,PlayableBuildingKind kind,NavPoint at,bool air=false,int sourceId=0,int targetId=0)
        {
            if(kind==PlayableBuildingKind.GaussTower&&air||Distance(from,at)>Range(p,kind)){Trace("coverage-rejected",new{kind,from,at,air,reason=kind==PlayableBuildingKind.GaussTower&&air?"ground-only weapon":"outside physical range",distance=Distance(from,at),range=Range(p,kind)});return false;}
            // Ordinary repair tests only ownership, health and radius; no weapon LOS.
            if(kind==PlayableBuildingKind.RepairTower)return true;
            double heading=Math.Atan2(at.Z-from.Z,at.X-from.X);
            // The air-body extent is the authored extent used by PlayableHawkCombat.
            var target=new BallisticPoint(at.X,air?o.Entities.Where(u=>u.Id==targetId).Select(u=>u.Altitude).DefaultIfEmpty(Height(p,at)).First()+1.8145018368959427*p.HawkModelScale/2:Height(p,at)+(p.AuthoredMap?.DirectFireHeight??1),at.Z);
            double pivot=Height(p,from)+BuildingCombatGeometry.ApronLift+(kind==PlayableBuildingKind.GaussTower?GaussTowerGeometry.PitchHeight:SentryTurretGeometry.PitchHeight)*p.TurretModelScale;
            double pitch=Math.Atan2(target.Y-pivot,Distance(from,at));
            if(pitch < -p.TurretDepression*Math.PI/180){Trace("coverage-rejected",new{kind,from,at,reason="depression",pitch});return false;}
            var muzzle=kind==PlayableBuildingKind.GaussTower?GaussTowerGeometry.Muzzle(from,Height(p,from)+BuildingCombatGeometry.ApronLift,p.TurretModelScale,heading,pitch):
                SentryTurretGeometry.Muzzle(from,Height(p,from)+BuildingCombatGeometry.ApronLift,p.TurretModelScale,heading,pitch,0);
            // Sentry projectiles are point projectiles in the ordinary combat runtime.
            double radius=kind==PlayableBuildingKind.GaussTower?p.GaussBeamRadius:0;
            if(!PlayableBallistics.TerrainLineClear(muzzle,target,radius,PlayableMap.SolidObstacles(p),p.BallisticWallHeight,q=>Height(p,q))){Trace("coverage-rejected",new{kind,from,at,reason="physical terrain LOS",muzzle,target,radius});return false;}
            bool buildingBlocked=o.Buildings.Where(b=>b.Id!=sourceId&&b.Id!=targetId&&b.Phase!=ConstructionPhase.Pending&&b.Health>0).Any(b=>
                BuildingCombatGeometry.Pose(p,b.Kind,b.RefineryUpgraded,b.Position,Height(p,b.Position),b.Heading,constructionProgress:b.Progress).Segment(muzzle,target,radius,out _,out _));
            Trace("coverage-result",new{kind,from,at,buildingBlocked});return !buildingBlocked;
        }
        private static double MobilePower(PlayableEntitySnapshot[] units,PlayableProfile p,AiProfile ai)=>AiDefensePlanner.Force(units,p,ai)+units.Where(u=>u.Kind==PlayableEntityKind.Hawk).Sum(u=>PlayableUnitRules.DamagePerSecond(p,u.Kind)*ai.Value("utility.forceHorizonSeconds")*Math.Min(1,PlayableUnitRules.HealthRatio(p,u)));
        private static double DefensePower(PlayableProfile p,AiProfile ai,PlayableBuildingKind kind)=>ai.Value("utility.forceHorizonSeconds")*(kind==PlayableBuildingKind.GaussTower?p.TankHealth*p.GaussTankDamageFraction/p.GaussReloadSeconds:p.ExplorerDamage*p.ExplorerAssaultBurstSize*1000d/(p.ExplorerBurstPauseMs+p.ExplorerAssaultBurstSize*p.ExplorerBurstShotIntervalMs));
        // Allocate each ready tower's actual weapon capacity once across compatible
        // local contacts. Residual reassignment avoids counting one tower twice or
        // spending a ground-only Gauss against an air contact on another front.
        internal static bool StaticDefenseCovers(PlayableAiObservation o,PlayableProfile p,PlayableEntitySnapshot[] threats)
        {
            if(threats.Length==0)return true;
            var towers=o.Buildings.Where(b=>b.Owner==o.Owner&&Live(b)&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&TerritoryRules.ArmedDefense(b.Kind)).OrderBy(b=>b.Id).ToArray();
            if(towers.Length==0)return false;
            double Weapon(int level)=>level==0?1:1+PlayableResearchRules.Bonus(p,level>1?PlayableResearchKind.AdvancedWeaponSystems:PlayableResearchKind.ImprovedWeaponSystems)/100;
            int ownLevel=o.OwnerResearch.Any(r=>r.Complete&&r.Kind==PlayableResearchKind.AdvancedWeaponSystems)?2:o.OwnerResearch.Any(r=>r.Complete&&r.Kind==PlayableResearchKind.ImprovedWeaponSystems)?1:0;
            // Health/damage exchange strength includes a building's real durability;
            // a full-health tower is not a one-HP gun merely because both shoot slowly.
            double Damage(PlayableBuildingSnapshot b)=>Math.Sqrt((b.Kind==PlayableBuildingKind.GaussTower?p.TankHealth*p.GaussTankDamageFraction/p.GaussReloadSeconds:p.ExplorerDamage*p.ExplorerAssaultBurstSize*1000d/(p.ExplorerBurstPauseMs+p.ExplorerAssaultBurstSize*p.ExplorerBurstShotIntervalMs))*Weapon(ownLevel)*Math.Max(0,b.ExactHealth??b.Health));
            var free=towers.Select(Damage).ToArray();
            var demand=threats.Select(u=>Math.Sqrt(PlayableUnitRules.DamagePerSecond(p,u.Kind,u.Upgraded)*Weapon(u.WeaponSystemsLevel)*Math.Max(0,u.ExactHealth??u.Health))).ToArray();
            const double epsilon=1e-9; // Floating-point capacity residual, not a balance threshold.
            if(free.Sum()+epsilon<demand.Sum())return false;
            var compatible=new bool[towers.Length,threats.Length];
            for(int t=0;t<towers.Length;t++)for(int e=0;e<threats.Length;e++)compatible[t,e]=Covers(o,p,towers[t].Position,towers[t].Kind,threats[e].Position,threats[e].Kind==PlayableEntityKind.Hawk,towers[t].Id,threats[e].Id);
            var allocation=new double[towers.Length,threats.Length];
            double Supply(int enemy,double needed,bool[] visited)
            {
                double supplied=0;
                for(int t=0;t<towers.Length&&needed-supplied>epsilon;t++)
                {
                    if(visited[t]||!compatible[t,enemy])continue;visited[t]=true;
                    double direct=Math.Min(free[t],needed-supplied);free[t]-=direct;allocation[t,enemy]+=direct;supplied+=direct;
                    for(int other=0;other<threats.Length&&needed-supplied>epsilon;other++)
                    {
                        if(other==enemy||allocation[t,other]<=epsilon)continue;
                        double moved=Supply(other,Math.Min(allocation[t,other],needed-supplied),visited);
                        allocation[t,other]-=moved;allocation[t,enemy]+=moved;supplied+=moved;
                    }
                }
                return supplied;
            }
            foreach(int e in Enumerable.Range(0,threats.Length).OrderBy(e=>Enumerable.Range(0,towers.Length).Count(t=>compatible[t,e])).ThenBy(e=>threats[e].Id))
            {
                double missing=demand[e];
                while(missing>epsilon){double supplied=Supply(e,missing,new bool[towers.Length]);if(supplied<=epsilon)return false;missing-=supplied;}
            }
            return true;
        }
        private static bool KnownEmpty(PlayableAiObservation o,AiKnowledgeState knowledge,int site)
        {
            var area=o.Intel.Areas.FirstOrDefault(a=>a.Envelope.AreaId==site);
            if(area!=null&&o.Buildings.Any(b=>o.IsHostile(b.Owner)&&b.Health>0&&o.Vision?.IsVisible(b.Position)==true&&area.Envelope.Contains(b.Position)))return false;
            return area?.Full==true||knowledge?.Areas.Any(a=>a.AreaId==site&&a.EmptyAtLastCoverage)==true;
        }
        private static PlayableAiAction Build(PlayableAiObservation o,TerritorySiteSnapshot site,TerritorySlot slot,PlayableBuildingKind kind)=>
            new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:site.Site.Id,slotId:slot.Id,parentId:site.CenterId,buildingKind:kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        internal static int ReadySlotCount(PlayableAiObservation o,TerritorySiteSnapshot site)
        {
            return site.Site.Slots.Count(slot=>o.Buildings.Any(b=>b.Owner==o.Owner&&b.SiteId==site.Site.Id&&
                b.SlotId==slot.Id&&b.Phase==ConstructionPhase.Ready&&Live(b)));
        }

        private static bool ReadyScience(PlayableAiObservation o)=>o.Buildings.Any(b=>b.Owner==o.Owner&&
            Live(b)&&b.Phase==ConstructionPhase.Ready&&b.Kind==PlayableBuildingKind.ScientificCenter);

        private static bool IsAir(AiKnownContact c)=>c.Building?c.Kind==(int)PlayableBuildingKind.Hangar:c.Kind==(int)PlayableEntityKind.Hawk;
        private static bool ObservedAir(PlayableAiObservation o)=>o.Entities.Any(u=>o.IsHostile(u.Owner)&&u.Health>0&&u.Kind==PlayableEntityKind.Hawk)||
            o.Buildings.Any(b=>o.IsHostile(b.Owner)&&Live(b)&&b.Kind==PlayableBuildingKind.Hangar);

        private static AiKnownContact[] RecentEnemies(PlayableAiObservation o,AiProfile ai,AiKnowledgeState knowledge)
        {
            long horizon=AiProfile.SecondsToTicks(ai.Value("positionDevelopment.airIntelSeconds"),30);
            return (knowledge?.Contacts??Array.Empty<AiKnownContact>()).Where(c=>o.IsHostile(c.Owner)&&
                c.HealthAtLastSeen>0&&c.Confidence>0&&o.Tick-c.LastSeenTick<=horizon).ToArray();
        }

        internal static bool HasAirEvidence(PlayableAiObservation o,AiProfile ai,AiKnowledgeState knowledge)=>
            ObservedAir(o)||RecentEnemies(o,ai,knowledge).Any(IsAir);

        internal static NavPoint? ObjectiveThreat(NavPoint center, IReadOnlyList<PlayableEntitySnapshot> units,
            IReadOnlyList<PlayableBuildingSnapshot> buildings, IReadOnlyList<PlayablePublicScoutObjective> objectives,
            Func<PlayableOwner,bool> hostile, Func<NavPoint,bool> visible)
        {
            NavPoint? Nearest(IEnumerable<NavPoint> points) => points.Where(p => !p.Equals(center))
                .OrderBy(p => Distance(center,p)).Select(p => (NavPoint?)p).FirstOrDefault();
            return Nearest(units.Where(u => u.Health>0 && hostile(u.Owner) && visible(u.Position)).Select(u => u.Position))
                ?? Nearest(buildings.Where(b => b.Health>0 && hostile(b.Owner) && visible(b.Position)).Select(b => b.Position))
                ?? Nearest(objectives.Where(s => s.Reachable && s.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart)
                    .Select(s => s.Approach));
        }

        internal static bool FacesThreat(NavPoint point, NavPoint center, NavPoint? enemy, double clearance) =>
            enemy.HasValue && (point.X-center.X)*(enemy.Value.X-center.X)+(point.Z-center.Z)*(enemy.Value.Z-center.Z)
                >= clearance * Distance(center,enemy.Value);

        internal static NavPoint? DefensiveFactoryRally(PlayableAiObservation o, PlayableProfile p, AiProfile ai,
            AiKnowledgeState knowledge, PlayableBuildingSnapshot factory, NavGeometry geometry)
        {
            if(factory==null||factory.Owner!=o.Owner||factory.Kind!=PlayableBuildingKind.Factory||
                factory.Phase!=ConstructionPhase.Ready||factory.BoostSelling||o.Vision==null)return null;
            var site=o.Sites.FirstOrDefault(s=>s.Site.Id==factory.SiteId&&s.Owner==o.Owner);
            if(site==null)return null;
            var center=site.Site.Position;
            var bearing=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0&&o.Vision.IsVisible(u.Position))
                .OrderBy(u=>Distance(center,u.Position)).ThenBy(u=>u.Id).Select(u=>(NavPoint?)u.Position).FirstOrDefault()
                ??RecentEnemies(o,ai,knowledge).OrderByDescending(c=>c.LastSeenTick).ThenBy(c=>Distance(center,c.Position))
                    .ThenBy(c=>c.Id).Select(c=>(NavPoint?)c.Position).FirstOrDefault()
                ??o.PublicScoutObjectives.Where(x=>x.Reachable&&x.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart&&
                    x.SiteId!=site.Site.Id&&!KnownEmpty(o,knowledge,x.SiteId))
                    .OrderByDescending(x=>AiPublicEnemyStarts.CanAttack(o,knowledge,x.SiteId)).ThenBy(x=>Distance(center,x.Approach))
                    .ThenBy(x=>x.SiteId).Select(x=>(NavPoint?)x.Approach).FirstOrDefault();
            if(!bearing.HasValue||bearing.Value.Equals(center))return null;

            var exit=FactoryReleaseRules.Exit(p,factory.Position,factory.Heading,PlayableEntityKind.Tank);
            var radius=Math.Max(p.TankCollisionRadius,Math.Max(PlayableUnitRules.Radius(p,PlayableEntityKind.Explorer),
                PlayableUnitRules.Radius(p,PlayableEntityKind.Shkval)));
            var towers=AiActiveStrategyPolicy.KnownTowers(o,knowledge);
            // Anchor the firing arc to the base so a moving contact does not continuously move the flag.
            var axis=new NavPoint(center.X+1,center.Z);
            var candidates=PlayableUnitRules.AttackApproachCandidates(p,axis,center,p.TankRange)
                .Where(at=>(at.X-center.X)*(bearing.Value.X-center.X)+(at.Z-center.Z)*(bearing.Value.Z-center.Z)>0)
                .OrderBy(at=>Distance(at,bearing.Value));
            foreach(var at in candidates)
            {
                if(!o.Vision.IsVisible(at)||!geometry.IsFree(at,radius)||p.AuthoredMap!=null&&!p.AuthoredMap.SupportsFootprint(at,radius)||
                    !AiTacticalExecutor.SafePath(o,exit,new[]{at},radius,p)||
                    towers.Any(t=>Covers(o,p,t.Position,t.Kind,at)))continue;
                bool forwardFactory=(factory.Position.X-center.X)*(at.X-center.X)+(factory.Position.Z-center.Z)*(at.Z-center.Z)>0;
                if(forwardFactory&&Distance(exit,at)<=p.FollowDistance)return null;
                return at;
            }
            return null;
        }

        private static NavPoint? Approach(PlayableAiObservation o,TerritorySiteSnapshot site,NavPoint? direction,double local)
        {
            if(!direction.HasValue)return null;
            var origin=site.Site.Position;
            double bearingDistance=Distance(origin,direction.Value);
            return o.RouteProofs.Where(r=>r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==site.CenterId&&
                    o.Entities.Any(u=>u.Id==r.UnitId&&u.Owner==o.Owner&&u.Kind!=PlayableEntityKind.Hawk))
                .SelectMany(r=>r.Path)
                .Where(at=>Distance(at,origin)<=local&&Distance(at,direction.Value)<bearingDistance)
                .OrderBy(at=>Distance(at,direction.Value)).Select(at=>(NavPoint?)at).FirstOrDefault();
        }

        private static NavPoint AimAt(NavPoint from,NavPoint direction,double reach)
        {
            double length=Distance(from,direction);
            return length<=reach?direction:new NavPoint(from.X+(direction.X-from.X)*reach/length,from.Z+(direction.Z-from.Z)*reach/length);
        }

        internal static bool PredominantlyAir(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge)
        {
            var visible=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0).ToArray();
            var remembered=RecentEnemies(o,ai,knowledge).Where(c=>!c.Building&&visible.All(u=>u.Id!=c.Id)).ToArray();
            double KnownPower(bool air)=>MobilePower(visible.Where(u=>(u.Kind==PlayableEntityKind.Hawk)==air).ToArray(),p,ai)+
                remembered.Where(c=>IsAir(c)==air).Sum(c=>PlayableUnitRules.DamagePerSecond(p,(PlayableEntityKind)c.Kind)*c.Confidence*
                    ai.Value("utility.forceHorizonSeconds"));
            return KnownPower(true)>KnownPower(false);
        }

        internal static int RequiredSentryReserve(PlayableAiObservation o,AiProfile ai,AiKnowledgeState knowledge,AiDifficulty difficulty)
        {
            if(AiProfile.BehaviorDifficulty(difficulty)==AiDifficulty.Veteran)return (int)ai.Value("positionDevelopment.veteranSentryReserve");
            if(HasAirEvidence(o,ai,knowledge))return 1;
            var ground=RecentEnemies(o,ai,knowledge).Where(c=>!c.Building&&!IsAir(c)).ToArray();
            var knownOwners=ground.Select(c=>c.Owner).Distinct().ToArray();
            long revisit=AiProfile.SecondsToTicks(ai.Value("scouting.revisitSeconds"),30);
            bool sustained=ground.Any(c=>o.Tick-c.LastSeenTick>=revisit)&&ground.Any(c=>c.Visible);
            bool allOpponentsKnown=knownOwners.Length>0&&o.Participants.Where(p=>p.Team!=o.Team)
                .All(p=>knownOwners.Contains((PlayableOwner)(p.LogicalPlayer-1)));
            return sustained&&allOpponentsKnown?0:1;
        }

        internal static bool PreferSentry(PlayableAiObservation o,PlayableProfile p,AiProfile ai,AiKnowledgeState knowledge,int siteId)
        {
            var site=o.Sites.FirstOrDefault(s=>s.Site.Id==siteId);
            if(site==null||!ReadyScience(o)||PredominantlyAir(o,p,ai,knowledge))return true;
            var threats=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0&&
                Distance(u.Position,site.Site.Position)<=ai.Value("positionDevelopment.localThreatRange")).ToArray();
            var air=threats.Where(u=>u.Kind==PlayableEntityKind.Hawk).ToArray();
            var ground=threats.Where(u=>u.Kind!=PlayableEntityKind.Hawk).ToArray();
            if(air.Length>0&&MobilePower(air,p,ai)>MobilePower(ground,p,ai))return true;
            return HasAirEvidence(o,ai,knowledge)&&!o.Buildings.Any(b=>b.Owner==o.Owner&&b.SiteId==siteId&&
                b.Kind==PlayableBuildingKind.SentryTurret&&b.Phase==ConstructionPhase.Ready&&Live(b));
        }

        internal static PlayableAiAction Retrofit(PlayableAiObservation o,PlayableProfile p,AiProfile ai,
            AiKnowledgeState knowledge,AiDifficulty difficulty,long freeCredits)
        {
            if(o.Tick<AiProfile.SecondsToTicks(ai.Value("positionDevelopment.retrofitSeconds"),30))return null;
            int reserve=RequiredSentryReserve(o,ai,knowledge,difficulty);
            var recent=RecentEnemies(o,ai,knowledge);
            double local=ai.Value("positionDevelopment.localThreatRange");
            foreach(var site in o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested&&s.Site.Slots.Count>0).OrderBy(s=>s.Site.Id))
            {
                if(ReadySlotCount(o,site)<site.Site.Slots.Count||!site.Site.TryDefenseSlots(p,out var pads))continue;
                var towers=o.Buildings.Where(b=>b.Owner==o.Owner&&b.SiteId==site.Site.Id&&Live(b)&&TerritoryRules.ArmedDefense(b.Kind)).ToArray();
                if(towers.Any(b=>b.Phase!=ConstructionPhase.Ready))continue;
                bool restoreAir=HasAirEvidence(o,ai,knowledge)&&!towers.Any(b=>b.Kind==PlayableBuildingKind.SentryTurret);
                if(restoreAir&&pads.Any(slot=>!o.Buildings.Any(b=>b.SiteId==site.Site.Id&&b.SlotId==slot.Id)))continue;
                bool sentryPreferred=PreferSentry(o,p,ai,knowledge,site.Site.Id);
                var replacement=restoreAir?PlayableBuildingKind.SentryTurret:PlayableBuildingKind.GaussTower;
                if(freeCredits<TerritoryRules.Cost(p,replacement)||!restoreAir&&sentryPreferred)continue;
                var groundContacts=recent.Where(c=>!c.Building&&!IsAir(c)).Select(c=>c.Position)
                    .Concat(o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0&&u.Kind!=PlayableEntityKind.Hawk).Select(u=>u.Position))
                    .Where(at=>Distance(at,site.Site.Position)<=local).Distinct().ToArray();
                if(!restoreAir&&groundContacts.Length==0)continue;
                var sentries=towers.Where(b=>b.Kind==PlayableBuildingKind.SentryTurret).ToArray();
                if(!restoreAir&&sentries.Length<=reserve)continue;
                // Keep the sentries closest to observed air approaches. If none were seen,
                // retain the most useful sentry on the known approach.
                var airFront=recent.Where(IsAir).OrderBy(c=>Distance(c.Position,site.Site.Position))
                    .Select(c=>(NavPoint?)c.Position).FirstOrDefault()??groundContacts.Select(at=>(NavPoint?)at).FirstOrDefault();
                var candidates=restoreAir?towers:sentries.OrderBy(b=>airFront.HasValue?Distance(b.Position,airFront.Value):0).ThenBy(b=>b.Id).Skip(reserve);
                foreach(var tower in candidates.OrderBy(b=>restoreAir&&airFront.HasValue?Distance(b.Position,airFront.Value):0).ThenBy(b=>b.Id))
                {
                    if(tower.PrivateState?.Lifecycle?.Repairing==true||pads.All(pad=>pad.Id!=tower.SlotId))continue;
                    var protectedApproaches=groundContacts.Select(at=>AimAt(site.Site.Position,at,Range(p,PlayableBuildingKind.SentryTurret)))
                        .Where(at=>Covers(o,p,tower.Position,tower.Kind,at,sourceId:tower.Id)).ToArray();
                    if(!restoreAir&&(protectedApproaches.Length==0||!protectedApproaches.Any(at=>
                        Covers(o,p,tower.Position,replacement,at,sourceId:tower.Id))))continue;
                    if(protectedApproaches.Any(at=>!towers.Any(other=>other.Id!=tower.Id&&
                        Covers(o,p,other.Position,other.Kind,at,sourceId:other.Id))))continue;
                    var sell=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.SellBuilding,
                        new[]{tower.Id},siteId:site.Site.Id,slotId:tower.SlotId,parentId:site.CenterId,buildingKind:replacement,
                        seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                    if(AiEconomyAdmission.Reject(o,p,sell)==null)return sell;
                }
            }
            return null;
        }

        public static IReadOnlyList<AiPositionInvestment> Plan(PlayableAiObservation o,PlayableProfile p,AiProfile ai,PlayableAiOpeningCompositionState opening,AiKnowledgeState knowledge,bool essentialFundsProtected,bool airOpening=false,IReadOnlyList<PlayableAiAction> pending=null,AiDifficulty difficulty=AiDifficulty.Fighter)
        {
            if(o==null||p==null||ai==null||opening==null)throw new ArgumentNullException();
            if(p.ProfileId!=o.ProfileId||p.Revision!=o.ProfileRevision||opening.OwnerId!=o.OwnerId||opening.MatchSeed!=o.Seed)throw new ArgumentException("Foreign position context");
            if(knowledge!=null&&(knowledge.OwnerId!=o.OwnerId||knowledge.Generation!=o.Generation||knowledge.ObservationTick>o.Tick))throw new ArgumentException("Foreign position knowledge");
            var result=new List<AiPositionInvestment>();
            if(!essentialFundsProtected){Trace("gate",new{reason="essential funds unprotected"});return result;}
            var enemies=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0).ToArray();
            var remembered=RecentEnemies(o,ai,knowledge);
            bool unknown=enemies.Length==0&&!o.Buildings.Any(b=>o.IsHostile(b.Owner)&&b.Health>0)&&remembered.Length==0;
            bool early=opening.Phase==PlayableAiOpeningPhase.Active;
            bool exception=opening.Opening==PlayableAiOpening.GreedySafe&&(enemies.Length>0||unknown)||
                (airOpening||opening.Opening==PlayableAiOpening.Air)&&enemies.Count(e=>e.Kind!=PlayableEntityKind.Hawk)>=ai.Value("positionDevelopment.airGroundThreat");
            if(early&&!exception)return result;

            double local=ai.Value("positionDevelopment.localThreatRange");
            bool science=ReadyScience(o);
            bool knownAir=ObservedAir(o)||remembered.Any(IsAir);
            bool predominantlyAir=PredominantlyAir(o,p,ai,knowledge);
            int sentryReserve=RequiredSentryReserve(o,ai,knowledge,difficulty);
            var sites=o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&s.Site.Slots.Count>0).ToArray();
            var own=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0).ToArray();
            var towers=o.Buildings.Where(b=>b.Owner==o.Owner&&Live(b)&&TerritoryRules.Defensive(b.Kind)).ToArray();
            var accepted=(pending??Array.Empty<PlayableAiAction>()).Where(a=>a.Kind==PlayableCommandKind.BuildAt&&
                TerritoryRules.Defensive(a.BuildingKind)&&!o.Buildings.Any(b=>b.SiteId==a.SiteId&&b.SlotId==a.SlotId)).ToArray();
            int RepairCount(int siteId)=>towers.Count(b=>b.Kind==PlayableBuildingKind.RepairTower&&(siteId==0||b.SiteId==siteId))+
                accepted.Count(a=>a.BuildingKind==PlayableBuildingKind.RepairTower&&(siteId==0||a.SiteId==siteId));
            var home=sites.FirstOrDefault(s=>s.Site.Id==o.HomeSiteId);
            int repairLimit=sites.Length>=ai.Value("positionDevelopment.secondRepairBases")?2:1;
            bool canRepair=!early&&home!=null&&ReadySlotCount(o,home)>=ai.Value("positionDevelopment.repairHomeBuildings")&&RepairCount(0)<repairLimit;
            var combatPads=new List<AiPositionInvestment>();
            var repairPads=new List<AiPositionInvestment>();
            foreach(var site in sites.Where(s=>!s.Contested).OrderBy(s=>s.Site.Id))
            {
                bool headquarters=site.Site.Kind==PlayableBuildingKind.Headquarters;
                if(early&&site.Site.Id!=o.HomeSiteId)continue;
                var origin=site.Site.Position;
                var threats=enemies.Where(e=>Distance(e.Position,origin)<=local).ToArray();
                var localMemory=remembered.Where(c=>Distance(c.Position,origin)<=local).ToArray();
                var attackers=o.OwnCenterDamage.Where(hit=>hit.CenterId==site.CenterId).Select(hit=>hit.AttackerId).ToHashSet();
                var direction=threats.OrderByDescending(e=>attackers.Contains(e.Id)).ThenBy(e=>Distance(e.Position,origin)).Select(e=>(NavPoint?)e.Position).FirstOrDefault()??
                    localMemory.OrderBy(c=>Distance(c.Position,origin)).ThenBy(c=>c.Id).Select(c=>(NavPoint?)c.Position).FirstOrDefault()??
                    o.PublicScoutObjectives.Where(x=>x.Reachable&&x.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart&&!KnownEmpty(o,knowledge,x.SiteId))
                        .OrderByDescending(x=>AiPublicEnemyStarts.CanAttack(o,knowledge,x.SiteId)).ThenBy(x=>Distance(origin,x.Approach))
                        .ThenBy(x=>x.SiteId).Select(x=>(NavPoint?)x.Approach).FirstOrDefault();
                var approach=Approach(o,site,direction,local);
                var airBearing=remembered.Where(IsAir).OrderBy(c=>Distance(c.Position,origin)).Select(c=>(NavPoint?)c.Position).FirstOrDefault();
                var front=threats.Length>0?direction:approach??airBearing??(early?direction:null);
                if(!front.HasValue||!site.Site.TryDefenseSlots(p,out var supportedPads))continue;

                var armed=towers.Where(b=>b.SiteId==site.Site.Id&&TerritoryRules.ArmedDefense(b.Kind)).ToArray();
                int acceptedCombat=accepted.Count(a=>a.SiteId==site.Site.Id&&TerritoryRules.ArmedDefense(a.BuildingKind));
                int combatCount=armed.Length+acceptedCombat;
                bool readyDevelopment=ReadySlotCount(o,site)==site.Site.Slots.Count;
                double limit=readyDevelopment?supportedPads.Count:ai.Value(headquarters?"positionDevelopment.homeCombatLimit":"positionDevelopment.outpostCombatLimit");
                bool alreadyBuilding=acceptedCombat>0||armed.Any(b=>b.Phase!=ConstructionPhase.Ready);
                var available=supportedPads.Where(slot=>!o.Buildings.Any(b=>b.SiteId==site.Site.Id&&b.SlotId==slot.Id)&&
                    !accepted.Any(a=>a.SiteId==site.Site.Id&&a.SlotId==slot.Id)).ToArray();
                var ground=threats.Where(e=>e.Kind!=PlayableEntityKind.Hawk).ToArray();
                var air=threats.Where(e=>e.Kind==PlayableEntityKind.Hawk).ToArray();
                double groundPower=MobilePower(ground,p,ai),airPower=MobilePower(air,p,ai);
                int sentries=armed.Count(b=>b.Kind==PlayableBuildingKind.SentryTurret);
                bool antiAirMissing=knownAir&&sentries==0;
                var kind=!early&&science&&!predominantlyAir&&groundPower>0&&groundPower>=airPower&&sentries>=sentryReserve?
                    PlayableBuildingKind.GaussTower:PlayableBuildingKind.SentryTurret;
                bool needsCombat=early?towers.All(b=>!TerritoryRules.ArmedDefense(b.Kind))&&accepted.All(a=>!TerritoryRules.ArmedDefense(a.BuildingKind)):
                    threats.Length>0?!StaticDefenseCovers(o,p,threats):
                    !headquarters&&combatCount==0&&approach.HasValue||antiAirMissing&&readyDevelopment;
                double support=MobilePower(own.Where(u=>Distance(u.Position,origin)<=local).ToArray(),p,ai);
                double staticSupport=armed.Where(b=>b.Phase==ConstructionPhase.Ready&&threats.Any(e=>Covers(o,p,b.Position,b.Kind,e.Position,e.Kind==PlayableEntityKind.Hawk,b.Id,e.Id)))
                    .Sum(b=>DefensePower(p,ai,b.Kind)*Math.Min(1,b.Health/Math.Max(1,TerritoryRules.MaximumHealth(p,b))));
                if(groundPower+airPower>(support+staticSupport+DefensePower(p,ai,PlayableBuildingKind.SentryTurret))*ai.Value("positionDevelopment.maximumThreatRatio"))continue;

                TerritorySlot combatPad=null;
                if(needsCombat&&!alreadyBuilding&&combatCount<limit)
                {
                    var targets=(kind==PlayableBuildingKind.GaussTower?ground:threats);
                    combatPad=available.Select(slot=>new
                    {
                        Slot=slot,
                        Coverage=targets.Count(e=>Covers(o,p,slot.Position,kind,e.Position,e.Kind==PlayableEntityKind.Hawk,targetId:e.Id)),
                        Approach=Covers(o,p,slot.Position,kind,approach??AimAt(slot.Position,front.Value,Range(p,kind)))
                    }).Where(x=>x.Coverage>0||x.Approach)
                        .OrderByDescending(x=>x.Coverage).ThenByDescending(x=>x.Approach)
                        .ThenBy(x=>Distance(x.Slot.Position,front.Value)).ThenBy(x=>x.Slot.Id).Select(x=>x.Slot).FirstOrDefault();
                    if(combatPad!=null)
                    {
                        var action=Build(o,site,combatPad,kind);
                        string rejected=AiEconomyAdmission.Reject(o,p,action);
                        if(rejected==null||rejected=="insufficient credits")
                            combatPads.Add(new AiPositionInvestment(action,ai.Value("positionDevelopment.towerValue")*
                                (1+Range(p,kind)/(Range(p,kind)+Distance(combatPad.Position,front.Value))),"compatible defense of local approach"));
                    }
                }

                if(!canRepair||RepairCount(site.Site.Id)>0||combatPad!=null)continue;
                // The second site must serve a withdrawal that the first tower cannot serve.
                var clients=own.Where(u=>Distance(u.Position,origin)<=local&&
                    (threats.Length>0||PlayableUnitRules.HealthRatio(p,u)<ai.Value("positionDevelopment.repairHealth"))&&
                    !towers.Any(b=>b.Kind==PlayableBuildingKind.RepairTower&&(Distance(b.Position,u.Position)<=Range(p,b.Kind)||
                        o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.TargetId==b.Id&&AiTacticalExecutor.WithdrawalRoute(o,r,p))))).ToArray();
                if(accepted.Any(a=>a.BuildingKind==PlayableBuildingKind.RepairTower)||towers.Any(b=>b.Kind==PlayableBuildingKind.RepairTower&&b.Phase!=ConstructionPhase.Ready))continue;
                foreach(var pad in available.Where(s=>Distance(s.Position,front.Value)>=Distance(origin,front.Value))
                    .OrderByDescending(s=>Distance(s.Position,front.Value)).ThenBy(s=>s.Id))
                {
                    if(!o.RouteProofs.Any(r=>r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==site.CenterId&&
                        Distance(r.Goal,pad.Position)<=Range(p,PlayableBuildingKind.RepairTower)&&AiTacticalExecutor.WithdrawalRoute(o,r,p)&&
                        clients.Any(u=>u.Id==r.UnitId&&AiStaticDefenseSafety.Withdrawal(o,p,u,r.Path,knowledge))))continue;
                    var action=Build(o,site,pad,PlayableBuildingKind.RepairTower);
                    string rejected=AiEconomyAdmission.Reject(o,p,action);
                    if(rejected!=null&&rejected!="insufficient credits")continue;
                    repairPads.Add(new AiPositionInvestment(action,ai.Value("positionDevelopment.towerValue")/
                        (1+clients.Min(u=>Distance(u.Position,pad.Position))/local),"sheltered withdrawal from local front"));
                    break;
                }
            }
            result.AddRange(combatPads);
            result.AddRange(repairPads);
            if(!early&&science)
            foreach(var refinery in o.Buildings.Where(b=>b.Owner==o.Owner&&Live(b)&&b.Kind==PlayableBuildingKind.Refinery&&b.Phase==ConstructionPhase.Ready&&b.PrivateState?.Upgrade!=null&&!b.RefineryUpgraded&&!b.PrivateState.Upgrade.Active))
            {
                if(enemies.Any(e=>Distance(e.Position,refinery.Position)<=local))continue;
                double gain=(p.RefineryUpgradedIncome-p.RefineryIncomePerPeriod)/p.IncomePeriodSeconds*Math.Max(0,ai.Value("positionDevelopment.investmentHorizonSeconds")-p.RefineryUpgradeSeconds);
                if(gain<=0)continue;
                var action=new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.UpgradeRefinery,new[]{refinery.Id},seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                if(AiEconomyAdmission.Reject(o,p,action) is string rejection&&rejection!="insufficient credits")continue;
                result.Add(new AiPositionInvestment(action,gain/Math.Max(1,p.RefineryUpgradeCost),"refinery income return over finite investment horizon"));
            }
            return result.OrderByDescending(x=>x.Utility).ThenBy(x=>x.Action.SiteId).ThenBy(x=>x.Action.SlotId).ToArray();
        }
    }
}

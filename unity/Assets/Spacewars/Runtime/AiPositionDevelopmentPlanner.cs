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
        internal static double Range(PlayableProfile p,PlayableBuildingKind kind)=>kind==PlayableBuildingKind.GaussTower?p.GaussRange:p.ExplorerRange*p.TurretRangeMultiplier;
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
        public static IReadOnlyList<AiPositionInvestment> Plan(PlayableAiObservation o,PlayableProfile p,AiProfile ai,PlayableAiOpeningCompositionState opening,AiKnowledgeState knowledge,bool essentialFundsProtected,bool airOpening=false)
        {
            if(o==null||p==null||ai==null||opening==null)throw new ArgumentNullException();
            if(p.ProfileId!=o.ProfileId||p.Revision!=o.ProfileRevision||opening.OwnerId!=o.OwnerId||opening.MatchSeed!=o.Seed)throw new ArgumentException("Foreign position context");
            if(knowledge!=null&&(knowledge.OwnerId!=o.OwnerId||knowledge.Generation!=o.Generation||knowledge.ObservationTick>o.Tick))throw new ArgumentException("Foreign position knowledge");
            var result=new List<AiPositionInvestment>();
            if(!essentialFundsProtected){Trace("gate",new{reason="essential funds unprotected"});return result;}
            var enemies=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0).ToArray();
            var remembered=(knowledge?.Contacts??Array.Empty<AiKnownContact>()).Where(c=>o.IsHostile(c.Owner)&&c.HealthAtLastSeen>0&&c.Confidence>0).ToArray();
            bool unknown=enemies.Length==0&&!o.Buildings.Any(b=>o.IsHostile(b.Owner)&&b.Health>0)&&remembered.Length==0;
            bool early=opening.Phase==PlayableAiOpeningPhase.Active;
            bool exception=opening.Opening==PlayableAiOpening.GreedySafe&&(enemies.Length>0||unknown)||(airOpening||opening.Opening==PlayableAiOpening.Air)&&enemies.Count(e=>e.Kind!=PlayableEntityKind.Hawk)>=ai.Value("positionDevelopment.airGroundThreat");
            Trace("opening",new{early,exception,opening.Phase,opening.Opening});
            if(early&&!exception){Trace("gate",new{reason="ordinary opening active"});return result;}
            double local=ai.Value("positionDevelopment.localThreatRange");
            bool science=o.Buildings.Any(b=>b.Owner==o.Owner&&Live(b)&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.Kind==PlayableBuildingKind.ScientificCenter);
            var sites=o.Sites.Where(s=>s.Owner==o.Owner&&s.Ready&&!s.Contested&&s.Site.Slots.Count>0).ToArray();
            var direction=enemies.OrderBy(e=>sites.Select(s=>Distance(s.Site.Position,e.Position)).DefaultIfEmpty(double.MaxValue).Min()).Select(e=>(NavPoint?)e.Position).FirstOrDefault()??
                remembered.OrderBy(c=>c.Id).Select(c=>(NavPoint?)c.Position).FirstOrDefault()??o.PublicScoutObjectives.Where(x=>x.Reachable&&x.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart&&!KnownEmpty(o,knowledge,x.SiteId)).OrderByDescending(x=>AiPublicEnemyStarts.CanAttack(o,knowledge,x.SiteId)).ThenBy(x=>sites.Select(s=>Distance(s.Site.Position,x.Approach)).DefaultIfEmpty(double.MaxValue).Min()).ThenBy(x=>x.SiteId).Select(x=>(NavPoint?)x.Approach).FirstOrDefault();
            Trace("direction",new{direction,eligibleSites=sites.Select(s=>s.Site.Id).ToArray(),enemies,remembered});
            var own=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0).ToArray();
            double nearestHeld=direction.HasValue?sites.Select(s=>Distance(s.Site.Position,direction.Value)).DefaultIfEmpty(double.MaxValue).Min():double.MaxValue;
            foreach(var site in direction.HasValue?sites.OrderBy(s=>Distance(s.Site.Position,direction.Value)).ThenBy(s=>s.Site.Id):Enumerable.Empty<TerritorySiteSnapshot>())
            {
                var threats=enemies.Where(e=>Distance(e.Position,site.Site.Position)<=local).ToArray();
                double support=MobilePower(own.Where(u=>Distance(u.Position,site.Site.Position)<=local).ToArray(),p,ai);
                double staticSupport=o.Buildings.Where(b=>b.Owner==o.Owner&&Live(b)&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&TerritoryRules.ArmedDefense(b.Kind)&&threats.Any(e=>Covers(o,p,b.Position,b.Kind,e.Position,e.Kind==PlayableEntityKind.Hawk,b.Id,e.Id))).Sum(b=>DefensePower(p,ai,b.Kind)*Math.Min(1,b.Health/Math.Max(1,TerritoryRules.MaximumHealth(p,b))));
                // Evaluate the prospective sentry as well: air/greedy exceptions do
                // not require an existing ground army. Still decline hopeless fronts.
                if(MobilePower(threats,p,ai)>(support+staticSupport+DefensePower(p,ai,PlayableBuildingKind.SentryTurret))*ai.Value("positionDevelopment.maximumThreatRatio")){Trace("site-rejected",new{site.Site.Id,reason="hopeless local threat ratio"});continue;}
                if(early&&site.Site.Id!=o.HomeSiteId)continue;
                var towers=o.Buildings.Where(b=>b.Owner==o.Owner&&Live(b)&&TerritoryRules.Defensive(b.Kind)).ToArray();
                bool armedPending=towers.Any(b=>b.SiteId==site.Site.Id&&TerritoryRules.ArmedDefense(b.Kind)&&b.Phase!=ConstructionPhase.Ready);
                // No speculative second opening tower while another is paid/constructed.
                if(early&&towers.Any(b=>TerritoryRules.ArmedDefense(b.Kind)))continue;
                bool ground=threats.Any(e=>e.Kind!=PlayableEntityKind.Hawk),air=threats.Any(e=>e.Kind==PlayableEntityKind.Hawk);
                var kind=!early&&science&&ground&&!air?PlayableBuildingKind.GaussTower:PlayableBuildingKind.SentryTurret;
                bool covered=threats.Length>0?threats.All(e=>towers.Any(b=>b.Phase==ConstructionPhase.Ready&&TerritoryRules.ArmedDefense(b.Kind)&&Covers(o,p,b.Position,b.Kind,e.Position,e.Kind==PlayableEntityKind.Hawk,b.Id,e.Id))):
                    towers.Any(b=>b.SiteId==site.Site.Id&&b.Phase==ConstructionPhase.Ready&&TerritoryRules.ArmedDefense(b.Kind));
                bool needsCombat=!covered&&!armedPending&&(early||site.Site.Kind==PlayableBuildingKind.Outpost||threats.Length>0);
                bool localRepair=towers.Any(b=>b.Kind==PlayableBuildingKind.RepairTower&&Distance(b.Position,direction.Value)<=nearestHeld+Range(p,b.Kind)&&
                    (b.Phase!=ConstructionPhase.Ready?o.RouteProofs.Any(r=>r.TargetId==b.ParentId&&AiTacticalExecutor.WithdrawalRoute(o,r,p)):
                    own.Any(u=>u.Id==b.Healing?.TargetId&&Distance(u.Position,b.Position)<=Range(p,b.Kind))||o.RouteProofs.Any(r=>r.TargetId==b.Id&&AiTacticalExecutor.WithdrawalRoute(o,r,p))));
                bool needsRepair=!early&&!localRepair&&own.Any(u=>Distance(u.Position,direction.Value)<=local);
                Trace("site-needs",new{site.Site.Id,needsCombat,needsRepair,covered,armedPending,ground,air,kind,support,staticSupport});
                foreach(var desired in new[]{needsCombat?(PlayableBuildingKind?)kind:null,needsRepair?(PlayableBuildingKind?)PlayableBuildingKind.RepairTower:null}.Where(k=>k.HasValue).Select(k=>k.Value))
                {
                    var pads=site.Site.DefenseSlots(p).Where(slot=>!o.Buildings.Any(b=>b.SiteId==site.Site.Id&&b.SlotId==slot.Id)).OrderBy(slot=>Distance(slot.Position,direction.Value)).ThenBy(slot=>slot.Id);
                    foreach(var pad in pads)
                    {
                        double length=Distance(pad.Position,direction.Value),reach=Range(p,desired);
                        // Project the public direction into the actual local weapon arc.
                        var aim=length<=reach?direction.Value:new NavPoint(pad.Position.X+(direction.Value.X-pad.Position.X)*reach/length,pad.Position.Z+(direction.Value.Z-pad.Position.Z)*reach/length);
                        bool coveredAim=Covers(o,p,pad.Position,desired,aim);Trace("pad-projection",new{site=site.Site.Id,pad=pad.Id,pad.Position,desired,aim,reach,length,coveredAim});
                        if(!coveredAim)continue;
                        if(desired==PlayableBuildingKind.RepairTower&&!o.RouteProofs.Any(r=>r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&r.TargetId==site.CenterId&&Distance(r.Goal,pad.Position)<=Range(p,desired)&&AiTacticalExecutor.WithdrawalRoute(o,r,p)&&own.Any(u=>u.Id==r.UnitId&&AiStaticDefenseSafety.Withdrawal(o,p,u,r.Path,knowledge))))continue;
                        var action=Build(o,site,pad,desired);string rejected=AiEconomyAdmission.Reject(o,p,action);Trace("pad-admission",new{site=site.Site.Id,pad=pad.Id,desired,rejected});if(rejected!=null&&rejected!="insufficient credits")continue;
                        result.Add(new AiPositionInvestment(action,ai.Value("positionDevelopment.towerValue")*(1+reach/(reach+Distance(pad.Position,direction.Value))),desired==PlayableBuildingKind.RepairTower?"reachable repair near main battle direction":early?"opening sentry exception":"local compatible defense for held position"));break;
                    }
                }
            }
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

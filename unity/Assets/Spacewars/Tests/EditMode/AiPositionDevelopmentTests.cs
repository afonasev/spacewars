using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiPositionDevelopmentTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        internal static PlayableAiObservation View(bool science=true,bool direction=true,PlayableBuildingSnapshot[] extra=null,PlayableEntitySnapshot[] units=null,PlayableRouteProof[] proofs=null,TerritorySiteSnapshot[] additionalSites=null,int credits=10000,long tick=9000,PlayableProfile activeProfile=null)
        {
            var site=TerritoryRules.Sites(P)[0];
            var own=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,site.Kind,site.Position,P.HeadquartersHealth,1,0,0,default,siteId:site.Id)};
            if(science)own=own.Concat(new[]{new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,site.Slots[0].Position,TerritoryRules.Health(P,PlayableBuildingKind.ScientificCenter),1,0,0,default,siteId:site.Id,slotId:1,parentId:1)}).ToArray();
            var vision=new PlayableVision(0,P.ArenaHalfExtent,P.ArenaHalfExtent,1,1);vision.Refresh(new[]{new VisionSource(default,2*P.ArenaHalfExtent)},Array.Empty<KnownBuilding>());
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,107,7,1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,units??Array.Empty<PlayableEntitySnapshot>(),own.Concat(extra??Array.Empty<PlayableBuildingSnapshot>()).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                vision:vision.Snapshot(),routeProofs:proofs,activeProfile:activeProfile,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)}.Concat(additionalSites??Array.Empty<TerritorySiteSnapshot>()).ToArray(),population:new PlayablePopulationSnapshot(0,0,P.ArmyCapacity),publicScoutObjectives:direction?new[]{new PlayablePublicScoutObjective(2,new NavPoint(21,0),PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)}:Array.Empty<PlayablePublicScoutObjective>(),researchAvailability:Enum.GetValues(typeof(PlayableResearchKind)).Cast<PlayableResearchKind>().Select(k=>new PlayableResearchAvailabilitySnapshot(k,PlayableResearchRules.Cost(P,k),true)).ToArray()));
        }
        internal static PlayableAiOpeningCompositionState Opening(PlayableAiOpening kind,PlayableAiOpeningPhase phase=PlayableAiOpeningPhase.Active)=>PlayableAiOpeningComposition.Initialize(7,"player-1",aiProfile:AiProfile.Initial,forcedOpening:kind).WithPhase(phase);
        static PlayableAiAction Build(PlayableAiObservation o,PlayableBuildingKind kind,int slot)=>new PlayableAiAction(1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.BuildAt,siteId:1,slotId:slot,parentId:1,buildingKind:kind,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        [TestCase(PlayableAiOpening.Safe)][TestCase(PlayableAiOpening.BlindRush)][TestCase(PlayableAiOpening.ExplorerAllIn)][TestCase(PlayableAiOpening.Air)]
        public void OrdinaryOpeningsCannotBuyUnrequestedTowers(PlayableAiOpening opening)
        {Assert.IsEmpty(AiPositionDevelopmentPlanner.Plan(View(),P,AiProfile.Initial,Opening(opening),null,true));}
        [Test] public void GreedyUnknownAllowsOneLegalSentryWithoutInventingEnemy()
        {
            var o=View();Assert.False(o.Entities.Any(e=>o.IsHostile(e.Owner)));
            var choices=AiPositionDevelopmentPlanner.Plan(o,P,AiProfile.Initial,Opening(PlayableAiOpening.GreedySafe),null,true);
            Assert.True(choices.Any(c=>c.Action.BuildingKind==PlayableBuildingKind.SentryTurret));Assert.True(choices.All(c=>c.Action.BuildingKind==PlayableBuildingKind.SentryTurret));
            var a=choices.First().Action;var site=o.Sites.Single().Site;var at=site.DefenseSlots(P).Single(s=>s.Id==a.SlotId).Position;
            var pending=new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.SentryTurret,at,0,0,0,0,default,siteId:1,slotId:a.SlotId,parentId:1,phase:ConstructionPhase.Pending);
            Assert.IsEmpty(AiPositionDevelopmentPlanner.Plan(View(extra:new[]{pending}),P,AiProfile.Initial,Opening(PlayableAiOpening.GreedySafe),null,true));
        }
        [Test] public void DefenseNamespaceAndReadyScienceAreAuthoritativeAdmission()
        {
            var o=View(science:false);int defense=o.Sites.Single().Site.DefenseSlots(P)[0].Id;
            Assert.AreEqual("slot unavailable",AiEconomyAdmission.Reject(o,P,Build(o,PlayableBuildingKind.Refinery,defense)));
            Assert.AreEqual("slot unavailable",AiEconomyAdmission.Reject(o,P,Build(o,PlayableBuildingKind.SentryTurret,2)));
            Assert.AreEqual("ready science prerequisite",AiEconomyAdmission.Reject(o,P,Build(o,PlayableBuildingKind.GaussTower,defense)));
            Assert.IsNull(AiEconomyAdmission.Reject(View(),P,Build(View(),PlayableBuildingKind.GaussTower,defense)));
        }
        [Test] public void EconomicUpgradeDoesNotRequireEnemyDirectionAndProtectsObligations()
        {
            var site=TerritoryRules.Sites(P)[0];var refinery=new PlayableBuildingSnapshot(8,PlayableOwner.Player,PlayableBuildingKind.Refinery,site.Slots[1].Position,P.RefineryHealth,1,0,0,default,siteId:1,slotId:2,parentId:1,upgrade:new PlayableRefineryUpgradeSnapshot(false,false,0,0,P.RefineryUpgradeSeconds,null));
            var o=View(direction:false,extra:new[]{refinery});var opening=Opening(PlayableAiOpening.Safe,PlayableAiOpeningPhase.Complete);
            Assert.True(AiPositionDevelopmentPlanner.Plan(o,P,AiProfile.Initial,opening,null,true).Any(c=>c.Action.Kind==PlayableCommandKind.UpgradeRefinery));
            Assert.IsEmpty(AiPositionDevelopmentPlanner.Plan(o,P,AiProfile.Initial,opening,null,false));
        }
        [Test] public void RepairHasNoWeaponLosAndGaussCannotCoverAir()
        {
            var o=View();var from=new NavPoint(-4.5,12);var to=new NavPoint(4.5,12);
            Assert.True(AiPositionDevelopmentPlanner.Covers(o,P,from,PlayableBuildingKind.RepairTower,to));
            Assert.False(AiPositionDevelopmentPlanner.Covers(o,P,from,PlayableBuildingKind.SentryTurret,to));
            Assert.False(AiPositionDevelopmentPlanner.Covers(o,P,from,PlayableBuildingKind.GaussTower,to,true));
        }
        [Test] public void RepairRouteRejectsEnemyTowerButAllowsBlockedLosAndAirAgainstGauss()
        {
            var unit=new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(20,0),P.TankHealth,false,0,0,0);
            var turret=new PlayableBuildingSnapshot(30,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,new NavPoint(-8,0),TerritoryRules.Health(P,PlayableBuildingKind.SentryTurret),1,0,0,default,includePrivateState:false);
            var o=View(extra:new[]{turret},units:new[]{unit});
            Assert.False(AiStaticDefenseSafety.Withdrawal(o,P,unit,new[]{new NavPoint(0,0)}));
            unit=new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(4.5,11),P.TankHealth,false,0,0,0);
            turret=new PlayableBuildingSnapshot(30,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,new NavPoint(-4.5,12),TerritoryRules.Health(P,PlayableBuildingKind.SentryTurret),1,0,0,default,includePrivateState:false);
            Assert.True(AiStaticDefenseSafety.Withdrawal(View(extra:new[]{turret},units:new[]{unit}),P,unit,new[]{new NavPoint(4.5,13)}));
            var hawk=new PlayableEntitySnapshot(21,PlayableOwner.Player,PlayableEntityKind.Hawk,new NavPoint(20,0),(int)P.HawkHealth,false,0,0,0,altitude:8);
            var gauss=new PlayableBuildingSnapshot(31,PlayableOwner.Enemy,PlayableBuildingKind.GaussTower,new NavPoint(-8,0),TerritoryRules.Health(P,PlayableBuildingKind.GaussTower),1,0,0,default,includePrivateState:false);
            Assert.True(AiStaticDefenseSafety.Withdrawal(View(extra:new[]{gauss},units:new[]{hawk}),P,hawk,new[]{new NavPoint(0,0)}));
        }
        [Test] public void BusyUsefulRepairDoesNotDuplicateButNewFrontCanDevelop()
        {
            var home=TerritoryRules.Sites(P)[0];var pad=home.DefenseSlots(P)[0];
            var healed=new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(pad.Position.X+2,pad.Position.Z),30,false,0,0,0);
            var repair=new PlayableBuildingSnapshot(30,PlayableOwner.Player,PlayableBuildingKind.RepairTower,pad.Position,TerritoryRules.Health(P,PlayableBuildingKind.RepairTower),1,0,0,default,siteId:1,slotId:pad.Id,parentId:1,healing:new PlayableRepairTowerSnapshot(20,0,0));
            var goal=new NavPoint(-18,4);var homeProof=new PlayableRouteProof(20,PlayableOwner.Player,107,9000,1,healed.Position,P.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,1,home.Position,goal,new[]{new NavPoint(-12,7),new NavPoint(-18,7),goal});
            var o=View(extra:new[]{repair},units:new[]{healed},proofs:new[]{homeProof});var opening=Opening(PlayableAiOpening.Safe,PlayableAiOpeningPhase.Complete);
            Assert.False(AiPositionDevelopmentPlanner.Plan(o,P,AiProfile.Initial,opening,null,true).Any(c=>c.Action.BuildingKind==PlayableBuildingKind.RepairTower));
            var front=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(20,-20),P);
            var center=new PlayableBuildingSnapshot(10,PlayableOwner.Player,PlayableBuildingKind.Outpost,front.Position,TerritoryRules.Health(P,PlayableBuildingKind.Outpost),1,0,0,default,siteId:3);
            var wounded=new PlayableEntitySnapshot(21,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(22,-10),40,false,0,0,0);
            var frontGoal=new NavPoint(20,-16);var frontProof=new PlayableRouteProof(21,PlayableOwner.Player,107,9000,1,wounded.Position,P.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,10,front.Position,frontGoal,new[]{frontGoal});
            var expanded=View(extra:new[]{repair,center},units:new[]{healed,wounded},proofs:new[]{homeProof,frontProof},additionalSites:new[]{new TerritorySiteSnapshot(front,PlayableOwner.Player,PlayableOwner.Player,1,false,10,true)});
            Assert.True(AiPositionDevelopmentPlanner.Plan(expanded,P,AiProfile.Initial,opening,null,true).Any(c=>c.Action.BuildingKind==PlayableBuildingKind.RepairTower&&c.Action.SiteId==3));
        }
        [Test] public void StaticCoverageRequiresReadyCompatibleLocalAndSufficientWeapons()
        {
            var enemy=new PlayableEntitySnapshot(100,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-1,0),P.TankHealth,false,0,0,0);
            PlayableBuildingSnapshot Tower(int id,PlayableBuildingKind kind,ConstructionPhase phase=ConstructionPhase.Ready,int health=-1,NavPoint? at=null)=>new PlayableBuildingSnapshot(id,PlayableOwner.Player,kind,at??new NavPoint(-8,0),health<0?TerritoryRules.Health(P,kind):health,1,0,0,default,phase:phase);
            var sentry=Tower(30,PlayableBuildingKind.SentryTurret);var o=View(extra:new[]{sentry},units:new[]{enemy});
            Assert.True(AiPositionDevelopmentPlanner.StaticDefenseCovers(o,P,new[]{enemy}));
            Assert.False(AiPositionDevelopmentPlanner.StaticDefenseCovers(View(extra:new[]{Tower(30,PlayableBuildingKind.SentryTurret,ConstructionPhase.Constructing)},units:new[]{enemy}),P,new[]{enemy}));
            Assert.False(AiPositionDevelopmentPlanner.StaticDefenseCovers(View(extra:new[]{Tower(30,PlayableBuildingKind.SentryTurret,health:1)},units:new[]{enemy}),P,new[]{enemy}));
            var wing=new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Hawk,new NavPoint(-1,0),(int)P.HawkHealth,false,0,0,0,altitude:8);
            Assert.False(AiPositionDevelopmentPlanner.StaticDefenseCovers(View(extra:new[]{Tower(30,PlayableBuildingKind.GaussTower)},units:new[]{wing}),P,new[]{wing}));
            Assert.False(AiPositionDevelopmentPlanner.StaticDefenseCovers(View(extra:new[]{Tower(30,PlayableBuildingKind.SentryTurret,at:new NavPoint(-25,20))},units:new[]{enemy}),P,new[]{enemy}));
            var swarm=Enumerable.Range(0,2).Select(i=>new PlayableEntitySnapshot(100+i,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-1,-1+i*2),P.TankHealth,false,0,0,0)).ToArray();
            Assert.False(AiPositionDevelopmentPlanner.StaticDefenseCovers(View(extra:new[]{sentry},units:swarm),P,swarm),"a single tower's capacity cannot be spent independently against every contact");
            foreach(bool reversed in new[]{false,true})
            {
                var mixed=new[]{Tower(reversed?40:30,PlayableBuildingKind.SentryTurret,at:new NavPoint(-8,-3)),Tower(reversed?30:40,PlayableBuildingKind.GaussTower,at:new NavPoint(-8,3))};
                var contacts=reversed?new[]{wing,enemy}:new[]{enemy,wing};
                Assert.True(AiPositionDevelopmentPlanner.StaticDefenseCovers(View(extra:mixed,units:contacts),P,contacts),"compatible mixed capacity is independent of ID/order");
            }
            var registry=new AiArmyRegistry(o.OwnerId,o.Generation,AiProfile.Initial,AiDifficulty.Veteran);var defense=new AiDefensePlanner(o.OwnerId,o.Generation);
            Assert.IsNull(defense.Propose(o,P,AiProfile.Initial,registry));Assert.True(defense.CoversTechnologyThreats(o,P,AiProfile.Initial,registry,Array.Empty<PlayableAiTraceRecord>()));
        }
        [Test] public void ConcreteIsNotAUnitUpgradeAndTowersDoNotBypassArmyEligibility()
        {
            Assert.False(Enum.GetValues(typeof(PlayableEntityKind)).Cast<PlayableEntityKind>().Any(AiResearchCatalog.BuildingDurability.Affects));
            var turret=new PlayableBuildingSnapshot(9,PlayableOwner.Player,PlayableBuildingKind.SentryTurret,new NavPoint(-12,0),TerritoryRules.Health(P,PlayableBuildingKind.SentryTurret),1,0,0,default);
            var o=View(extra:new[]{turret});var budget=new AiBudgetLedger(o.OwnerId,o.Generation);budget.ObserveBank(o.Credits);
            var plan=new AiTechnologyPlanner().Plan(o,P,AiProfile.Initial,budget.Capture());
            Assert.True(AiTechnologyPlanner.ArmySizeAllows(o,P,PlayableResearchKind.ReinforcedConcrete));
            Assert.False(AiTechnologyPlanner.ArmySizeAllows(o,P,PlayableResearchKind.ImprovedWeaponSystems));
            Assert.Greater(plan.Branches.Single(b=>b.Descriptor.Kind==PlayableResearchKind.ImprovedWeaponSystems).AffectedValue,0);
            Assert.Zero(plan.Branches.Single(b=>b.Descriptor.Kind==PlayableResearchKind.ImprovedWeaponSystems).Utility);
            Assert.Zero(plan.Branches.Single(b=>b.Descriptor.Kind==PlayableResearchKind.ImprovedDefensiveSystems).AffectedValue);
            Assert.Greater(plan.Branches.Single(b=>b.Descriptor.Kind==PlayableResearchKind.ReinforcedConcrete).AffectedValue,0);
        }
    }
}

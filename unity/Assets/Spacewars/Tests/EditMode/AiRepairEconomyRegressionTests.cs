using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiRepairEconomyRegressionTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        static PlayableAiObservation View(bool militaryBasics=false,bool militaryAvailable=false,int? cash=null)
        {
            var home=new NavPoint(-20,-15);var tower=new NavPoint(-15,-8);var goal=new NavPoint(-17,-8);
            var units=Enumerable.Range(10,(int)P.ArmySystemsMinimumUnits).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-12,id-10),P.TankHealth,false,0,0,0)).ToArray();
            var bs=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,home,P.HeadquartersHealth,1,0,0,default),new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(-25,-15),P.FactoryHealth,1,0,0,default),new PlayableBuildingSnapshot(3,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,new NavPoint(-25,-5),250,1,0,0,default),new PlayableBuildingSnapshot(30,PlayableOwner.Player,PlayableBuildingKind.RepairTower,tower,200,1,0,0,default)};
            var proofs=units.SelectMany(u=>new[]{new PlayableRouteProof(u.Id,u.Owner,71,100,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,1,home,home,new[]{home}),new PlayableRouteProof(u.Id,u.Owner,71,100,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,30,tower,goal,new[]{goal})}).ToArray();
            var vision=new PlayableVision(0,200,200,1,1);vision.Refresh(new[]{new VisionSource(default,200)},Array.Empty<KnownBuilding>());
            var research=militaryBasics?new[]{PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems}.Select((k,i)=>new PlayableResearchOrderSnapshot(i+1,k,3,PlayableResearchRules.Cost(P,k),30,30,false,true)).ToArray():Array.Empty<PlayableResearchOrderSnapshot>();
            var availability=AiResearchCatalog.All.Select(d=>new PlayableResearchAvailabilitySnapshot(d.Kind,d.Cost(P),militaryAvailable&&d.Kind==PlayableResearchKind.TankChassis)).Concat(new[]{new PlayableResearchAvailabilitySnapshot(PlayableResearchKind.ReinforcedConcrete,P.ReinforcedConcreteCost,true)}).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,7,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,cash??10000,null,units,bs,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:proofs,researchAvailability:availability,ownerResearch:research,activeProfile:P));
        }
        [Test]
        public void SkatQuotaUsesCombatPopulationAndAdmitsLateSupportHangar()
        {
            var owner = new PlayableAiOwnerLoop(P,PlayableAiOpeningComposition.Initialize(7,"player-1"),71);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var support = typeof(PlayableAiOwnerLoop).GetMethod("PlanSkatSupport",flags);
            var composition = (AiPhaseCompositionPlanner)typeof(PlayableAiOwnerLoop).GetField("composition",flags).GetValue(owner);
            typeof(AiPhaseCompositionPlanner).GetField("phase",flags).SetValue(composition,AiGamePhase.Late);
            var origin = new NavPoint(-20,-15);
            var site = new TerritorySite(1,PlayableBuildingKind.Headquarters,origin,P);
            var buildings = new[] {
                new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,origin,P.HeadquartersHealth,1,0,0,default,siteId:1),
                new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[0].Position,P.FactoryHealth,1,0,0,default,siteId:1,slotId:1,parentId:1),
                new PlayableBuildingSnapshot(3,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[1].Position,P.FactoryHealth,1,0,0,default,siteId:1,slotId:2,parentId:1),
                new PlayableBuildingSnapshot(4,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,site.Slots[2].Position,250,1,0,0,default,siteId:1,slotId:3,parentId:1)
            };
            var hangar = new PlayableBuildingSnapshot(5,PlayableOwner.Player,PlayableBuildingKind.Hangar,site.Slots[3].Position,
                200,1,0,0,default,siteId:1,slotId:4,parentId:1);
            int ratio = PlayableAiOwnerLoop.SkatPopulationRatio(7,"player-1");
            Assert.Contains(ratio,new[]{15,20,25,30,35});
            Assert.AreEqual(5,Enumerable.Range(1,100).Select(seed=>PlayableAiOwnerLoop.SkatPopulationRatio(seed,"player-1")).Distinct().Count());
            int tanks = (int)Math.Ceiling(ratio/(double)PlayableUnitRules.Population(P,PlayableEntityKind.Tank));
            var units = Enumerable.Range(10,tanks).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,
                PlayableEntityKind.Tank,new NavPoint(-12,id-10),P.TankHealth,false,0,0,0)).ToArray();
            var drone = new PlayableEntitySnapshot(90,PlayableOwner.Player,PlayableEntityKind.Skat,origin,P.SkatHealth,false,0,0,0);
            PlayableAiAction Plan(PlayableBuildingSnapshot[] structures,PlayableEntitySnapshot[] force,long money)
            {
                var snapshot = new PlayableSnapshot(P.ProfileId,P.Revision,71,7,100,100,RuntimeStatus.Running,false,
                    PlayableMatchOutcome.Playing,10000,null,force,structures,Array.Empty<PlayableProjectileSnapshot>(),
                    new PlayableRuntimeMetrics(0,0,0,0,0),null,
                    sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},
                    population:new PlayablePopulationSnapshot(tanks*PlayableUnitRules.Population(P,PlayableEntityKind.Tank),0,100),activeProfile:P);
                return (PlayableAiAction)support.Invoke(owner,new object[]{PlayableAiObservation.From(snapshot),money});
            }

            long quote = P.HangarCreditCost + P.SkatCreditCost;
            Assert.IsNull(Plan(buildings,units,quote-1));
            Assert.IsNull(Plan(buildings.Where(b=>b.Id!=3).ToArray(),units,quote));
            var build = Plan(buildings,units,quote);
            Assert.AreEqual(PlayableCommandKind.BuildAt,build?.Kind);
            Assert.AreEqual(PlayableBuildingKind.Hangar,build.BuildingKind);
            Assert.AreEqual(PlayableEntityKind.Skat,build.UnitKind);
            var withHangar = buildings.Append(hangar).ToArray();
            Assert.AreEqual(PlayableCommandKind.QueueSkat,Plan(withHangar,units,P.SkatCreditCost)?.Kind);
            Assert.IsNull(Plan(withHangar,units.Append(drone).ToArray(),P.SkatCreditCost));
        }

        [TestCase(AiArmyPhase.Regrouping)][TestCase(AiArmyPhase.Retreating)]
        public void OrdinaryLeaderMoveToRepairTowerDoesNotCreateRepairClientAndRoundtrips(AiArmyPhase phase)
        {
            var o=View();var registry=new AiArmyRegistry(o.OwnerId,o.Generation,AiProfile.Initial,AiDifficulty.Fighter);var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(P);
            planner.OpeningMembers=new[]{10};var guard=planner.ProposePositioning(o,P,AiProfile.Initial,registry);Assert.NotNull(guard);planner.Commit(o,guard,registry);
            planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,guard.ActionId,o.Tick,1,o.Tick,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"guard",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,1,1),AiArmyPlanner.Policy,guard.Kind));
            planner.SetPhase(registry,phase,o.Tick);var field=typeof(AiArmyPlanner).GetField("state",BindingFlags.NonPublic|BindingFlags.Instance);((AiArmyMissionState)field.GetValue(planner)).Reason=phase==AiArmyPhase.Regrouping?AiReinforcementPolicy.RegroupReason:"withdrawal";
            var move=new PlayableAiAction(guard.ActionId+1,o.OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,new[]{10},new NavPoint(-17,-8),parentId:30,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            Assert.False(AiRepairUsePolicy.IsAction(o,move));Assert.DoesNotThrow(()=>planner.Commit(o,move,registry));Assert.IsEmpty(planner.Capture().RepairClients);Assert.AreEqual(10,registry.CaptureArmy(planner.Capture().ArmyId).Single().LeaderId);
            byte[] Bytes(AiArmyPlanner value){using var s=new MemoryStream();using(var b=new BinaryWriter(s,System.Text.Encoding.UTF8,true))value.WriteState(b);return s.ToArray();}
            var bytes=Bytes(planner);var restored=new AiArmyPlanner(o.OwnerId,o.Generation);restored.Rebind(P);using(var b=new BinaryReader(new MemoryStream(bytes)))restored.ReadState(b,o.Tick,registry);CollectionAssert.AreEqual(bytes,Bytes(restored));
        }
        [Test] public void ConcreteWaitsForWeaponAndDefenseBasics()
        {var o=View();var budget=new AiBudgetLedger(o.OwnerId,o.Generation);budget.ObserveBank(o.Credits);var plan=new AiTechnologyPlanner().Plan(o,P,AiProfile.Initial,budget.Capture());Assert.Greater(plan.Branches.Single(b=>b.Descriptor.Buildings).AffectedValue,0);Assert.Zero(plan.Branches.Single(b=>b.Descriptor.Buildings).Utility);Assert.IsNull(plan.Action);}
        [Test] public void ConcreteCannotSpendMilitarySavingQuoteOnCheaperResearch()
        {var o=View(true,true,(int)P.ReinforcedConcreteCost);var budget=new AiBudgetLedger(o.OwnerId,o.Generation);budget.ObserveBank(o.Credits);var plan=new AiTechnologyPlanner().Plan(o,P,AiProfile.Initial,budget.Capture());Assert.Zero(plan.Branches.Single(b=>b.Descriptor.Buildings).Utility);Assert.AreEqual(PlayableResearchKind.TankChassis,plan.FundingGoal.Research);Assert.True(plan.Action==null||plan.Action.ResearchKind==PlayableResearchKind.TankChassis);}
        [TestCase(false)][TestCase(true)] public void LateConcreteRemainsAvailableWhenNoUsefulProductionCompetes(bool production)
        {var o=View(true);var budget=new AiBudgetLedger(o.OwnerId,o.Generation);budget.ObserveBank(o.Credits);var plan=new AiTechnologyPlanner().PlanWithProduction(o,P,AiProfile.Initial,budget.Capture(),usefulProductionPending:production);Assert.AreEqual(!production,plan.Action?.ResearchKind==PlayableResearchKind.ReinforcedConcrete);}
    }
}

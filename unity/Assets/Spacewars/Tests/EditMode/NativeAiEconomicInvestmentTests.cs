using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiEconomicInvestmentTests
    {
        private static PlayableAiObservation View(int bank=150,double income=6,bool pending=false,bool full=false,long tick=900)
        {
            var p=PlayableProfile.Default;var site=TerritoryRules.Sites(p).First(x=>x.Kind==PlayableBuildingKind.Headquarters);
            var buildings=new[]{new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,p.HeadquartersHealth,1,0,0,default,siteId:site.Id),
                new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,site.Slots[0].Position,p.FactoryHealth,1,0,0,default,siteId:site.Id,slotId:site.Slots[0].Id,parentId:1)}.ToList();
            if(pending)buildings.Add(new PlayableBuildingSnapshot(3,PlayableOwner.Player,PlayableBuildingKind.Refinery,site.Slots[1].Position,100,.5,0,0,default,siteId:site.Id,slotId:site.Slots[1].Id,parentId:1,phase:ConstructionPhase.Constructing));
            if(full)foreach(var slot in site.Slots.Skip(1))buildings.Add(new PlayableBuildingSnapshot(10+slot.Id,PlayableOwner.Player,PlayableBuildingKind.Refinery,slot.Position,p.RefineryHealth,1,0,0,default,siteId:site.Id,slotId:slot.Id,parentId:1));
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,bank,null,Array.Empty<PlayableEntitySnapshot>(),buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},population:new PlayablePopulationSnapshot(0,0,p.ArmyCapacity),incomePerSecond:income));
        }
        private static AiProductionCapacity Capacity(string reason="income within sustained spend capacity")=>new AiProductionCapacity(6,10,.9,900,0,0,600,reason);
        [Test] public void IncomeInvestmentAccumulatesBeforeAnAffordableUnitWithoutRequiringAnArmy()
        {
            var p=PlayableProfile.Default;var plan=AiEconomicInvestmentPlanner.Plan(View(),p,AiProfile.Initial,Capacity(),true,false);
            Assert.NotNull(plan);Assert.AreEqual(PlayableBuildingKind.Refinery,plan.Candidate.Action.BuildingKind);Assert.AreEqual(p.RefineryCreditCost,plan.Credits);
            Assert.AreEqual("insufficient credits",plan.Candidate.Reason);Assert.False(plan.AllowsProduction(p.TankCreditCost,p.TankCreditCost));
            Assert.True(plan.AllowsProduction(p.RefineryCreditCost+p.TankCreditCost,p.TankCreditCost));Assert.True(plan.AllowsProduction(0,p.TankCreditCost,acceptedStartup:true));
        }
        [Test] public void OverflowFundsFactoryButDoesNotBypassExistingUtilizationAdmission()
        {
            var p=PlayableProfile.Default;var plan=AiEconomicInvestmentPlanner.Plan(View(1000,13),p,AiProfile.Initial,Capacity("fill existing idle line"),true,false);
            Assert.AreEqual(PlayableBuildingKind.Factory,plan.Candidate.Action.BuildingKind);Assert.AreEqual(0,plan.Credits,"unqualified lines must receive ordinary production before saving");
            Assert.AreEqual("fill existing idle line",plan.Candidate.Reason);Assert.False(plan.Candidate.Legal);
            var ready=AiEconomicInvestmentPlanner.Plan(View(1000,13),p,AiProfile.Initial,Capacity(null),true,false);Assert.True(ready.Candidate.Legal);Assert.AreEqual(p.FactoryCreditCost+AiProfile.Initial.Value("economy.factoryLaunchCycles")*p.TankCreditCost,ready.Credits);
        }
        [Test] public void QualifiedDemandSurvivesDeliberatePauseWithZeroMoneyAndAnUnchangedExpiry()
        {
            var p=PlayableProfile.Default;var o=View(1000,13);var b=new AiBudgetLedger(o.OwnerId,o.Generation);b.ObserveBank(o.Credits);
            var ready=AiEconomicInvestmentPlanner.Plan(o,p,AiProfile.Initial,Capacity(null),true,false);
            AiEconomicInvestmentPlanner.RememberDemand(o,p,b,ready);var witness=AiEconomicInvestmentPlanner.Demand(b);
            Assert.NotNull(witness);Assert.Zero(b.Reserved);Assert.Zero(witness.Amount);AiEconomicInvestmentPlanner.ValidateDemand(b,p,o.Tick);
            b=AiBudgetLedger.Restore(b.Capture(),o.Tick);var paused=AiEconomicInvestmentPlanner.Plan(o,p,AiProfile.Initial,Capacity("fill existing idle line"),true,false,AiEconomicInvestmentPlanner.Demand(b));
            Assert.True(paused.Candidate.Legal,"a deliberate saving pause does not erase proven demand");Assert.AreEqual(ready.Credits,paused.Credits);
            AiEconomicInvestmentPlanner.RememberDemand(o,p,b,paused);Assert.AreEqual(witness.ExpiresTick,AiEconomicInvestmentPlanner.Demand(b).ExpiresTick);
            b.Expire(witness.ExpiresTick+1,e=>true);Assert.IsNull(AiEconomicInvestmentPlanner.Demand(b));
            var expired=View(1000,13,tick:witness.ExpiresTick+1);Assert.False(AiEconomicInvestmentPlanner.Plan(expired,p,AiProfile.Initial,Capacity("fill existing idle line"),true,false).Candidate.Legal);
        }
        [TestCase(8,PlayableBuildingKind.Refinery)][TestCase(10,PlayableBuildingKind.Refinery)]
        public void CheaperCurrentHeadDoesNotInventFactoryOverflow(double income,PlayableBuildingKind expected)
        {
            var capacity=new AiProductionCapacity(income,100d/15d,.9,900,0,0,600,null);
            var plan=AiEconomicInvestmentPlanner.Plan(View(1000,income),PlayableProfile.Default,AiProfile.Initial,capacity,true,false);
            Assert.AreEqual(expected,plan.Candidate.Action.BuildingKind);
        }
        [TestCase(false,false)][TestCase(true,true)] public void SurvivalAndHomeReadyKeepTheirExistingPriority(bool ready,bool emergency)
        {Assert.IsNull(AiEconomicInvestmentPlanner.Plan(View(),PlayableProfile.Default,AiProfile.Initial,Capacity(),ready,emergency));}
        [Test] public void IncomeEqualToCurrentCapacityFundsTheNextGrowthCycle()
        {
            var p=PlayableProfile.Default;var plan=AiEconomicInvestmentPlanner.Plan(View(income:10),p,AiProfile.Initial,Capacity(),true,false);
            Assert.NotNull(plan);Assert.AreEqual(PlayableBuildingKind.Refinery,plan.Candidate.Action.BuildingKind);
            Assert.AreEqual(p.RefineryCreditCost,plan.Credits);Assert.False(plan.AllowsProduction(p.TankCreditCost,p.TankCreditCost));
        }
        [Test] public void PaidIncomeConstructionFinishesBeforeAnotherInvestment()
        {Assert.IsNull(AiEconomicInvestmentPlanner.Plan(View(pending:true),PlayableProfile.Default,AiProfile.Initial,Capacity(),true,false));}
        [Test] public void OccupiedSlotsDoNotCreateAnUnfundableSavingGoal()
        {Assert.IsNull(AiEconomicInvestmentPlanner.Plan(View(full:true),PlayableProfile.Default,AiProfile.Initial,Capacity(),true,false));}
    }
}

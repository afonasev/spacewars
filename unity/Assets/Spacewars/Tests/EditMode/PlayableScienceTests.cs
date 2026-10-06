using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableScienceTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;private PlayableProfile p;private long sequence;
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        private PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,owner);
        private PlayableBuildingSnapshot B(int id,PlayableOwner owner=PlayableOwner.Player)=>View(owner).Buildings.Single(b=>b.Id==id);
        private double Balance(PlayableOwner owner=PlayableOwner.Player)=>(double)Call("Balance",owner);
        private PlayableCommandStatus Send(PlayableCommandKind kind,int id,string owner="player-1")=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,owner,kind,new[]{id}),null);
        private PlayableCommandStatus Research(PlayableResearchKind kind,int center)=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:kind),null);
        [SetUp] public void Setup(){p=PlayableProfile.Default;domain=Activator.CreateInstance(Domain,Flags,null,new object[]{p,1L},null);sequence=0;}
        private int Build(PlayableBuildingKind kind,int slot, bool ready=true,PlayableOwner owner=PlayableOwner.Player)
        {
            Call("AddCredits",owner,10000d);int site=owner==PlayableOwner.Player?1:2;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",site,slot,kind,site,owner,null));
            if(ready){Call("AdvanceFoundations");Call("AdvanceBuildings",TerritoryRules.Duration(p,kind)+1);}
            return View(owner).Buildings.Single(b=>b.Kind==kind&&b.SlotId==slot).Id;
        }

        [Test] public void ScienceMustBeOwnedReadyAndNotSelling()
        {
            int r=Build(PlayableBuildingKind.Refinery,1);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.UpgradeRefinery,r));
            int enemy=Build(PlayableBuildingKind.ScientificCenter,1,true,PlayableOwner.Enemy);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.UpgradeRefinery,r));
            int s=Build(PlayableBuildingKind.ScientificCenter,2,false);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.UpgradeRefinery,r));
            Call("AdvanceFoundations");Call("AdvanceBuildings",p.ScienceBuildSeconds);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UpgradeRefinery,r));
            Send(PlayableCommandKind.CancelRefineryUpgrade,r);Send(PlayableCommandKind.SellBuilding,s);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.UpgradeRefinery,r));
        }
        [Test] public void ScienceHasNoIncomeProductionAndUsesImportedFootprint()
        {
            double income=View().IncomePerSecond;int s=Build(PlayableBuildingKind.ScientificCenter,1);
            Assert.AreEqual(income,View().IncomePerSecond);Assert.AreEqual(p.ScienceHealth,B(s).Health);
            Assert.AreEqual(p.ScienceFootprintRadius,TerritoryRules.Radius(p,PlayableBuildingKind.ScientificCenter));
            Assert.AreEqual(p.ScienceVisionRange,PlayableVision.BuildingRadius(p,PlayableBuildingKind.ScientificCenter,ConstructionPhase.Ready));
            Assert.AreNotEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueTank,s));
            Assert.AreNotEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UpgradeRefinery,s));
        }
        [Test] public void OwnerResearchQueueChargesSequentiallyAndCenterLossDoesNotRefund()
        {
            int center=Build(PlayableBuildingKind.ScientificCenter,2);double before=Balance();
            Assert.AreEqual(PlayableCommandStatus.Applied,Research(PlayableResearchKind.TankChassis,center));
            Assert.AreEqual(PlayableCommandStatus.Applied,Research(PlayableResearchKind.ExplorerAssaultGuns,center));
            var orders=B(center).PrivateState.Research;Assert.AreEqual(2,orders.Count);Assert.True(orders[0].Active);Assert.False(orders[1].Active);Assert.AreEqual(before-p.TankChassisCost,Balance());
            Call("AdvanceResearch",p.TankChassisSeconds);orders=B(center).PrivateState.Research;Assert.True(orders[0].Complete);Assert.True(orders[1].Active);Assert.AreEqual(before-p.TankChassisCost-p.ExplorerAssaultCost,Balance());
            Call("Damage",center,(int)p.ScienceHealth+1);Assert.AreEqual(before-p.TankChassisCost-p.ExplorerAssaultCost,Balance());
        }
        [Test] public void ResearchSaveStateRoundTripKeepsPaidActiveAndWaitingOrders()
        {
            int center=Build(PlayableBuildingKind.ScientificCenter,2);Research(PlayableResearchKind.TankChassis,center);Research(PlayableResearchKind.ExplorerAssaultGuns,center);
            Call("AdvanceResearch",7d);var saved=(PlayableResearchSaveState)Call("CaptureResearchState");
            Setup();int restoredCenter=Build(PlayableBuildingKind.ScientificCenter,2);Assert.AreEqual(center,restoredCenter);
            Call("RestoreResearchState",saved);var orders=B(restoredCenter).PrivateState.Research;
            Assert.AreEqual(2,orders.Count);Assert.True(orders[0].Active);Assert.AreEqual(p.TankChassisCost,orders[0].PaidCost);Assert.AreEqual(7d,orders[0].Elapsed);Assert.False(orders[1].Active);Assert.Zero(orders[1].PaidCost);
        }
        [Test] public void UpgradePaysOnceCancellationRefundsExactlyOnceAndRejectsOtherOwner()
        {
            int r=Build(PlayableBuildingKind.Refinery,1);Build(PlayableBuildingKind.ScientificCenter,2);double before=Balance();
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.UpgradeRefinery,r,"enemy-1"));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UpgradeRefinery,r));
            Assert.AreEqual(before-p.RefineryUpgradeCost,Balance());
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.UpgradeRefinery,r));
            Call("AdvanceRefineryUpgrades",5d);
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.CancelRefineryUpgrade,r,"enemy-1"));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.CancelRefineryUpgrade,r));Assert.AreEqual(before,Balance());
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.CancelRefineryUpgrade,r));Assert.AreEqual(before,Balance());
            Send(PlayableCommandKind.UpgradeRefinery,r);Assert.Zero(B(r).PrivateState.Upgrade.Elapsed);
        }
        [Test] public void InsufficientCreditsAndConstructionRejectWithoutWork()
        {
            int r=Build(PlayableBuildingKind.Refinery,1,false);int s=Build(PlayableBuildingKind.ScientificCenter,2,false);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.UpgradeRefinery,r));
            Call("AdvanceFoundations");Call("AdvanceBuildings",p.ScienceBuildSeconds);
            Call("AddCredits",PlayableOwner.Player,-Balance());
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.UpgradeRefinery,r));Assert.Zero(Balance());Assert.False(B(r).PrivateState.Upgrade.Active);
        }
        [Test] public void CompletionKeepsDamageAndSurvivesScienceLossWithoutASecondLevel()
        {
            int r=Build(PlayableBuildingKind.Refinery,1),s=Build(PlayableBuildingKind.ScientificCenter,2);double basic=View().IncomePerSecond;
            Send(PlayableCommandKind.UpgradeRefinery,r);Call("AdvanceRefineryUpgrades",p.RefineryUpgradeSeconds-1);
            Assert.AreEqual(basic,View().IncomePerSecond);Call("Damage",s,(int)p.ScienceHealth+1);Call("Damage",r,20);
            Call("AdvanceRefineryUpgrades",1d);Assert.True(B(r).RefineryUpgraded);Assert.AreEqual(p.RefineryHealth-20,B(r).Health);
            Assert.AreEqual(basic+(p.RefineryUpgradedIncome-p.RefineryIncomePerPeriod)/p.IncomePeriodSeconds,View().IncomePerSecond,1e-8);
            double before=Balance();Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.UpgradeRefinery,r));
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.CancelRefineryUpgrade,r));Assert.AreEqual(before,Balance());
        }
        [TestCase(false)] [TestCase(true)] public void SaleIncludesPaidUpgradeAndDisposesIt(bool completed)
        {
            int r=Build(PlayableBuildingKind.Refinery,1);Build(PlayableBuildingKind.ScientificCenter,2);Send(PlayableCommandKind.UpgradeRefinery,r);
            if(completed)Call("AdvanceRefineryUpgrades",p.RefineryUpgradeSeconds);
            Call("Damage",r,p.RefineryHealth/2);Domain.GetField("elapsed",Flags).SetValue(domain,10d);
            double expected=Math.Floor((p.RefineryCreditCost+p.RefineryUpgradeCost)*p.BuildingSaleRefundRatio*.5),before=Balance();
            Assert.AreEqual(expected,B(r).PrivateState.Lifecycle.Refund);Send(PlayableCommandKind.SellBuilding,r);Assert.AreEqual(before+expected,Balance());
            Assert.False(B(r).PrivateState.Upgrade.Active);Assert.AreEqual(completed,B(r).RefineryUpgraded,"Sale retains completed appearance until removal");Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.CancelRefineryUpgrade,r));
        }
        [Test] public void CascadeSaleIncludesUpgradeAndDestructionNeverRefunds()
        {
            int r=Build(PlayableBuildingKind.Refinery,1);Build(PlayableBuildingKind.ScientificCenter,2);Send(PlayableCommandKind.UpgradeRefinery,r);
            double expected=Math.Floor(p.HeadquartersCreditCost*.75)+Math.Floor((p.RefineryCreditCost+p.RefineryUpgradeCost)*.75)+Math.Floor(p.ScienceCreditCost*.75),before=Balance();
            Assert.AreEqual(expected,B(1).PrivateState.Lifecycle.Refund);Send(PlayableCommandKind.SellBuilding,1);Assert.AreEqual(before+expected,Balance());
            Setup();r=Build(PlayableBuildingKind.Refinery,1);Build(PlayableBuildingKind.ScientificCenter,2);Send(PlayableCommandKind.UpgradeRefinery,r);before=Balance();Call("Damage",r,p.RefineryHealth+1);Assert.AreEqual(before,Balance());Assert.False(View().Buildings.Any(b=>b.Id==r));
        }
        [Test] public void OtherRefineriesAndOwnersKeepBasicIncome()
        {
            int a=Build(PlayableBuildingKind.Refinery,1),b=Build(PlayableBuildingKind.Refinery,2);Build(PlayableBuildingKind.ScientificCenter,3);
            double enemy=View(PlayableOwner.Enemy).IncomePerSecond;Send(PlayableCommandKind.UpgradeRefinery,a);Call("AdvanceRefineryUpgrades",p.RefineryUpgradeSeconds);
            Assert.True(B(a).RefineryUpgraded);Assert.False(B(b).RefineryUpgraded);Assert.AreEqual(enemy,View(PlayableOwner.Enemy).IncomePerSecond);
        }
        [Test] public void EnemySeesCompletedVariantButNeverProgressOrInvestment()
        {
            int r=Build(PlayableBuildingKind.Refinery,1,true,PlayableOwner.Enemy);Build(PlayableBuildingKind.ScientificCenter,2,true,PlayableOwner.Enemy);
            var point=B(r,PlayableOwner.Enemy).Position;var observer=new NavPoint(point.X+3,point.Z);Call("SpawnPlayer",observer,observer);
            Send(PlayableCommandKind.UpgradeRefinery,r,"enemy-1");Assert.IsNull(B(r).PrivateState);Assert.False(B(r).RefineryUpgraded);
            Call("AdvanceRefineryUpgrades",p.RefineryUpgradeSeconds);Assert.True(B(r).RefineryUpgraded);Assert.IsNull(B(r).PrivateState);
            Assert.True(View().Vision.KnownBuildings.Single(x=>x.Id==r).RefineryUpgraded);
        }
        [Test] public void MemoryDoesNotLearnUnseenUpgradeUntilRediscovery()
        {
            var vision=new PlayableVision(0,50,50,2,1);var point=new NavPoint(10,0);
            var basic=new KnownBuilding(1,1,PlayableOwner.Enemy,PlayableBuildingKind.Refinery,point,0);
            var upgraded=new KnownBuilding(1,1,PlayableOwner.Enemy,PlayableBuildingKind.Refinery,point,0,true);
            vision.Refresh(new[]{new VisionSource(point,3)},new[]{basic});
            vision.Refresh(Array.Empty<VisionSource>(),new[]{upgraded});Assert.False(vision.Snapshot().KnownBuildings.Single().RefineryUpgraded);
            vision.Refresh(new[]{new VisionSource(point,3)},new[]{upgraded});Assert.True(vision.Snapshot().KnownBuildings.Single().RefineryUpgraded);
            Assert.False(typeof(KnownBuilding).GetProperties().Any(x=>x.Name=="Upgrade"||x.Name=="PaidCost"));
        }
        [Test] public void ScienceProfileFieldsHaveMetadataAndRejectNonfiniteOrOutOfRange()
        {
            var fields=PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("science.")).ToArray();Assert.AreEqual(11,fields.Length);
            foreach(var f in fields){Assert.IsNotEmpty(f.Label);Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);
                var member=typeof(PlayableProfileData).GetField(f.Path.Substring(8));Assert.NotNull(member);
                foreach(double value in new[]{f.Minimum-1,f.Maximum+1,double.NaN,double.PositiveInfinity}){
                    var data=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                    member.SetValue(data,value);Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));}}
        }
    }
}

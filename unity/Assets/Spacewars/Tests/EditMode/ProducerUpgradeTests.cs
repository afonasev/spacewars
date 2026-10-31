using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class ProducerUpgradeTests
    {
        static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        object d;PlayableProfile p;long sequence;
        object Call(string method,params object[] args)=>D.GetMethod(method,F).Invoke(d,args);
        PlayableSnapshot View()=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);
        PlayableBuildingSnapshot Building(int id)=>View().Buildings.Single(b=>b.Id==id);
        PlayableBuildingPrivateState State(int id)=>Building(id).PrivateState;
        PlayableCommandStatus Send(PlayableCommandKind kind,int id,long order=0,PlayableResearchKind research=PlayableResearchKind.TankChassis)=>
            (PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind,new[]{id},productionOrderId:order,researchKind:research),null);
        [SetUp] public void Setup()
        {
            var data=PlayableProfile.Default.CopyData();data.armyCapacity=100;data.tankProductionSeconds=30;data.hawkProductionSeconds=30;
            p=PlayableProfile.Create(data);d=Activator.CreateInstance(D,F,null,new object[]{p,1L,false},null);sequence=0;Call("AddCredits",PlayableOwner.Player,20000d);
        }
        void Close(object domain)
        {
            if(domain==null)return;
            var nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);nav.ReleaseCommittedHostRoot();D.GetMethod("DestroyOwnedNavigation",F).Invoke(domain,null);
        }
        [TearDown] public void Teardown()=>Close(d);
        int Build(PlayableBuildingKind kind,int slot)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,kind,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            var b=View().Buildings.Single(x=>x.SlotId==slot);Assert.AreEqual(ConstructionPhase.Ready,b.Phase);return b.Id;
        }
        void Advance(double seconds){Call("AdvanceProduction",seconds);Call("AdvanceHawks",seconds);}
        void Roundtrip()
        {
            byte[] bytes=(byte[])Call("CaptureWorldBytes",7,"producer-upgrades-v1");
            var restored=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"producer-upgrades-v1"});
            Close(d);d=restored;CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"producer-upgrades-v1"));
        }
        void CompleteUpgrade(int id)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UpgradeProducer,id));Advance(.5);Advance(p.ProducerUpgradeSeconds(Building(id).Kind));Assert.True(State(id).Upgrade.Complete);
        }
        [TestCase(PlayableBuildingKind.Factory)][TestCase(PlayableBuildingKind.Hangar)]
        public void SharedQueueKeepsPrecedingUnitAndSpeedsAlreadyPaidFollowingUnit(PlayableBuildingKind kind)
        {
            int producer=Build(kind,1);Build(PlayableBuildingKind.ScientificCenter,2);
            var command=kind==PlayableBuildingKind.Factory?PlayableCommandKind.QueueTank:PlayableCommandKind.QueueHawk;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(command,producer));Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UpgradeProducer,producer));Assert.AreEqual(PlayableCommandStatus.Applied,Send(command,producer));
            var ids=State(producer).Orders.Select(o=>o.Id).ToArray();double bank=View().ExactCredits;Roundtrip();
            Advance(.1);Assert.AreEqual(30,State(producer).Orders[0].Duration);Assert.False(State(producer).Orders[1].Active);Assert.AreEqual(bank,View().ExactCredits);

            for(int i=0;i<1800&&State(producer).Orders.Any(o=>o.Id==ids[0]);i++)Advance(1d/30);
            Assert.False(State(producer).Orders.Any(o=>o.Id==ids[0]),"preceding unit physically releases");
            Assert.True(View().Entities.Any(u=>u.Owner==PlayableOwner.Player&&u.Kind==(kind==PlayableBuildingKind.Factory?PlayableEntityKind.Tank:PlayableEntityKind.Hawk)));
            for(int i=0;i<1800&&Building(producer).LaunchPhase!=0;i++)Advance(1d/30);
            Assert.Zero(Building(producer).LaunchPhase);
            Assert.False(State(producer).Orders[0].Active,"upgrade waits until previous release closes");Advance(.1);
            Assert.True(State(producer).Orders[0].IsUpgrade&&State(producer).Orders[0].Active);Assert.AreEqual(bank-p.ProducerUpgradeCost(kind),View().ExactCredits);Roundtrip();
            Advance(p.ProducerUpgradeSeconds(kind));Assert.True(State(producer).Upgrade.Complete);Assert.AreEqual(ids[2],State(producer).Orders.Single().Id);
            Assert.False(State(producer).Orders[0].Active);Advance(.1);Assert.AreEqual(20,State(producer).Orders[0].Duration,1e-9);Roundtrip();
            Assert.AreEqual(20,State(producer).Orders[0].Duration);Assert.AreEqual(3,View().Population.Reserved);
        }
        [Test] public void PrerequisiteCapacityDuplicateAndCancelAreAuthoritative()
        {
            int f=Build(PlayableBuildingKind.Factory,1);double bank=View().ExactCredits;
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.UpgradeProducer,f));Assert.AreEqual(bank,View().ExactCredits);
            Build(PlayableBuildingKind.ScientificCenter,2);bank=View().ExactCredits;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UpgradeProducer,f));Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.UpgradeProducer,f));
            long id=State(f).Orders.Single().Id;Assert.AreEqual(bank,View().ExactCredits);Send(PlayableCommandKind.CancelProductionOrder,f,id);Assert.AreEqual(bank,View().ExactCredits);
            for(int i=0;i<6;i++)Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueTank,f));
            Assert.AreEqual(PlayableCommandStatus.Overflow,Send(PlayableCommandKind.UpgradeProducer,f));
        }
        [Test] public void UpgradeWaitsForCenterAndMoneyWithoutOvertakingOrPopulationReservation()
        {
            int f=Build(PlayableBuildingKind.Factory,1),c=Build(PlayableBuildingKind.ScientificCenter,2);
            Send(PlayableCommandKind.UpgradeProducer,f);Send(PlayableCommandKind.QueueTank,f);Call("AddCredits",PlayableOwner.Player,-View().ExactCredits);
            Advance(1);Assert.False(State(f).Orders.Any(o=>o.Active));Assert.Zero(View().Population.Reserved);Assert.That(State(f).Orders[0].BlockedReason,Does.Contain("кредитов"));Roundtrip();
            Call("RemoveBuilding",((IDictionary)D.GetField("buildings",F).GetValue(d))[c],false);Call("AddCredits",PlayableOwner.Player,1000d);Advance(1);
            Assert.False(State(f).Orders.Any(o=>o.Active));Assert.That(State(f).Orders[0].BlockedReason,Does.Contain("центр"));
            // The destroyed center keeps its old pad reserved while the wreck collapses.
            Build(PlayableBuildingKind.ScientificCenter,3);Advance(1);Assert.True(State(f).Orders[0].Active);Assert.Zero(View().Population.Reserved);
            double bank=View().ExactCredits;long id=State(f).Orders[0].Id;Send(PlayableCommandKind.CancelProductionOrder,f,id);Assert.AreEqual(bank+p.ProducerUpgradeCost(PlayableBuildingKind.Factory),View().ExactCredits);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.CancelProductionOrder,f,id));Advance(.1);Assert.AreEqual(30,State(f).Orders[0].Duration);
        }
        [TestCase(PlayableBuildingKind.Factory)][TestCase(PlayableBuildingKind.Hangar)]
        public void SaleIncludesHistoricalUpgradePaymentAndPreservesUnitQueueSalePolicy(PlayableBuildingKind kind)
        {
            int f=Build(kind,1);Build(PlayableBuildingKind.ScientificCenter,2);CompleteUpgrade(f);Roundtrip();
            int expected=(int)Math.Floor((TerritoryRules.Cost(p,kind)+p.ProducerUpgradeCost(kind))*p.BuildingSaleRefundRatio);
            Assert.AreEqual(expected,State(f).Lifecycle.Refund);
            Send(kind==PlayableBuildingKind.Factory?PlayableCommandKind.QueueTank:PlayableCommandKind.QueueHawk,f);
            double bank=View().ExactCredits;Send(PlayableCommandKind.SellBuilding,f);
            Assert.AreEqual(bank+expected,View().ExactCredits);Roundtrip();
        }
        [Test] public void ActiveUpgradeSaleCountsPaymentOnceAndCenterSaleCountsItsOwnUpgrade()
        {
            int f=Build(PlayableBuildingKind.Factory,1),c=Build(PlayableBuildingKind.ScientificCenter,2);Send(PlayableCommandKind.UpgradeProducer,f);Advance(1);
            double bank=View().ExactCredits;int refund=State(f).Lifecycle.Refund;Assert.AreEqual((int)Math.Floor((p.FactoryCreditCost+p.ProducerUpgradeCost(PlayableBuildingKind.Factory))*p.BuildingSaleRefundRatio),refund);
            Send(PlayableCommandKind.SellBuilding,f);Assert.AreEqual(bank+refund,View().ExactCredits);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueResearch,c,research:PlayableResearchKind.ScientificCenterUpgrade));Call("AdvanceResearch",p.ScientificCenterUpgradeSeconds);
            Assert.AreEqual((int)Math.Floor((p.ScienceCreditCost+p.ScientificCenterUpgradeCost)*p.BuildingSaleRefundRatio),State(c).Lifecycle.Refund);
        }
        [Test] public void RepeatUsesUpgradedSpeedAndLabEditsPreservePaidTerms()
        {
            int f=Build(PlayableBuildingKind.Factory,1);Build(PlayableBuildingKind.ScientificCenter,2);CompleteUpgrade(f);
            double paid=State(f).Upgrade.PaidCost;var data=p.CopyData();data.factoryUpgradeCost=700;data.factoryUpgradeSeconds=55;data.factoryCreditCost=900;data.upgradedProducerSpeedMultiplier=2;
            data.revision=p.Revision+1;p=PlayableProfile.Create(data);Call("ApplyBalance",p);Roundtrip();
            Assert.AreEqual(paid,State(f).Upgrade.PaidCost);Assert.AreEqual((int)Math.Floor((300+paid)*p.BuildingSaleRefundRatio),State(f).Lifecycle.Refund);
            Send(PlayableCommandKind.ToggleRepeatProduction,f);Advance(.1);Assert.AreEqual(15,State(f).Orders[0].Duration);Roundtrip();
        }
        [Test] public void ProvingSetupKeepsProducerLevelOnBothSidesWithoutChangingModels()
        {
            var setup=new ProvingGroundSetup(p);
            for(int side=0;side<2;side++)
            {
                var site=setup.Profile.AuthoredMap.Sites(setup.Profile)[side];
                Assert.IsNull(setup.PlaceBuilding(side,PlayableBuildingKind.Factory,site.Slots[0].Position));
                Assert.IsNull(setup.PlaceBuilding(side,PlayableBuildingKind.Hangar,site.Slots[1].Position));
                Assert.IsNull(setup.UpgradeBuilding(site.Id,site.Slots[0].Id));Assert.IsNull(setup.UpgradeBuilding(site.Id,site.Slots[1].Id));
            }
            var config=setup.Configuration();Close(d);p=config.Profile;d=Activator.CreateInstance(D,F,null,new object[]{p,1L,config},null);
            var copied=(ProvingGroundSetup)Call("CaptureProvingSetup");
            var all=(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            var producers=all.Buildings.Where(b=>b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Hangar).ToArray();Assert.AreEqual(4,producers.Length);
            foreach(var b in producers)
            {
                Assert.True(b.PrivateState.Upgrade.Complete);Assert.False(b.ModelUpgraded);Assert.True(copied.BuildingUpgraded(b.SiteId,b.SlotId));
            }
        }
        [Test] public void LabFieldsAreIndependentValidatedAndGamepadCarriesStableCancellationId()
        {
            var data=p.CopyData();var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="factory.upgradeSeconds");NativeBalanceFields.Write(field,data,44);
            Assert.AreEqual(p.FactoryBuildSeconds,data.factoryBuildSeconds);Assert.AreEqual(p.ProducerUpgradeSeconds(PlayableBuildingKind.Hangar),data.hangarUpgradeSeconds);
            var speed=PlayableProfileMetadata.Fields.Single(f=>f.Path=="production.upgradedProducerSpeedMultiplier");Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(speed,data,.5));
            int f=Build(PlayableBuildingKind.Factory,1);Build(PlayableBuildingKind.ScientificCenter,2);Send(PlayableCommandKind.UpgradeProducer,f);
            var action=OfflinePadWorldActions.Building(View(),p,f).Single(a=>a.Sector.Id.EndsWith(":producer-upgrade"));
            Assert.AreEqual(PlayableCommandKind.CancelProductionOrder,action.Command.Kind);Assert.AreEqual(State(f).Orders[0].Id,action.Command.ProductionOrderId);
        }
    }
}

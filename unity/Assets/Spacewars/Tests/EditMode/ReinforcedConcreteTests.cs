using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class ReinforcedConcreteTests
    {
        static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        object domain;PlayableProfile p;int center;long sequence;
        object Call(string name,params object[] args)=>D.GetMethod(name,F).Invoke(domain,args);
        PlayableSnapshot View()=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);
        IDictionary Buildings=>(IDictionary)D.GetField("buildings",F).GetValue(domain);
        object B(int id)=>Buildings[id];
        void Health(int id,double hp)=>B(id).GetType().GetField("Health").SetValue(B(id),hp);
        double Health(int id)=>(double)B(id).GetType().GetField("Health").GetValue(B(id));
        PlayableCommandStatus Send(PlayableCommandKind kind,int id,PlayableResearchKind research=PlayableResearchKind.ReinforcedConcrete)=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind,new[]{id},researchKind:research),null);
        int Build(PlayableBuildingKind kind,int slot,bool ready=true)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,kind,1,PlayableOwner.Player,null));Call("AdvanceFoundations");
            if(ready)Call("AdvanceBuildings",100d);
            return View().Buildings.Single(b=>b.SiteId==1&&b.SlotId==slot).Id;
        }
        void Complete(){Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueResearch,center));Call("AdvanceResearch",30d);}
        [SetUp] public void Setup()
        {
            p=PlayableProfile.Default;domain=Activator.CreateInstance(D,F,null,new object[]{p,1L,false},null);sequence=0;
            Call("AddCredits",PlayableOwner.Player,10000d);Build(PlayableBuildingKind.Factory,5);center=Build(PlayableBuildingKind.ScientificCenter,2);
        }
        [TearDown] public void Cleanup()=>Call("DestroyOwnedNavigation");
        [Test] public void CompletionScalesExistingBuildingsExactlyOnceWithoutChangingEnemiesOrUnits()
        {
            var enemy=Buildings.Values.Cast<object>().First(b=>(PlayableOwner)b.GetType().GetField("Owner").GetValue(b)==PlayableOwner.Enemy);
            double enemyHp=(double)enemy.GetType().GetField("Health").GetValue(enemy);
            var unit=View().Entities.Single();Health(1,123.25);Health(center,100.5);double credits=View().ExactCredits;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueResearch,center));Assert.AreEqual(credits-200,View().ExactCredits);
            Call("AdvanceResearch",29d);Assert.AreEqual(123.25,Health(1));Call("AdvanceResearch",1d);
            Assert.AreEqual(123.25*1.25,Health(1));Assert.AreEqual(125.625,Health(center));Assert.AreEqual(p.HeadquartersHealth*1.25,View().Buildings.Single(b=>b.Id==1).MaxHealth);
            Assert.AreEqual(enemyHp,(double)enemy.GetType().GetField("Health").GetValue(enemy));Assert.AreEqual(unit.ExactHealth,View().Entities.Single().ExactHealth);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.QueueResearch,center));Call("AdvanceResearch",30d);Assert.AreEqual(123.25*1.25,Health(1));
            Call("Damage",center,10000);Assert.AreEqual(p.HeadquartersHealth*1.25,View().Buildings.Single(b=>b.Id==1).MaxHealth);
        }
        [Test] public void ConstructionBeforeAndAfterCompletionUsesResearchedMaximumAndPreservesDamage()
        {
            int factory=Build(PlayableBuildingKind.Factory,1,false);Call("AdvanceBuildings",3d);Health(factory,Health(factory)-7.25);double before=Health(factory);
            Complete();Assert.AreEqual(before*1.25,Health(factory));Call("AdvanceBuildings",100d);Assert.AreEqual((p.FactoryHealth-7.25)*1.25,Health(factory),1e-8);
            int refinery=Build(PlayableBuildingKind.Refinery,3);Assert.AreEqual(p.RefineryHealth*1.25,Health(refinery),1e-8);
        }
        [TestCase(200d)] [TestCase(123.25d)] public void RepairInProgressKeepsPaidFractionAndSaveRestoreIsExact(double health)
        {
            Health(1,health);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.StartBuildingRepair,1));Call("AdvanceBuildingLifecycle",1d);
            var repair=B(1).GetType().GetField("Repair").GetValue(B(1));var rt=repair.GetType();double cost=(double)rt.GetField("TotalCost").GetValue(repair),duration=(double)rt.GetField("Duration").GetValue(repair);
            Complete();Assert.AreEqual(cost,rt.GetField("TotalCost").GetValue(repair));Assert.AreEqual(duration,rt.GetField("Duration").GetValue(repair));
            var bytes=(byte[])Call("CaptureWorldBytes",7,"concrete-test");
            Call("DestroyOwnedNavigation");domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"concrete-test"});
            CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"concrete-test"));Call("AdvanceBuildingLifecycle",100d);
            Assert.AreEqual(p.HeadquartersHealth*1.25,Health(1),1e-9);Assert.AreEqual("Здание не повреждено",View().Buildings.Single(b=>b.Id==1).PrivateState.Lifecycle.RepairBlockedReason);
        }
        [Test] public void RepairStartedAfterResearchAndBalanceBonusChangeKeepsValidFrozenTerms()
        {
            Complete();Health(1,123.25);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.StartBuildingRepair,1));
            var data=p.CopyData();data.revision++;data.reinforcedConcreteBonusPercent=75;data.reinforcedConcreteLevel2BonusPercent=100;var changed=PlayableProfile.Create(data);Call("ApplyBalance",changed);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"concrete-test");Call("DestroyOwnedNavigation");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,changed,7,"concrete-test"});
            CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"concrete-test"));Call("AdvanceBuildingLifecycle",100d);Assert.LessOrEqual(Health(1),700);
        }
        [Test] public void SaleUsesHealthFractionAndProfileApplyDoesNotDropResearchMaximum()
        {
            Complete();int factory=Build(PlayableBuildingKind.Factory,1);Health(factory,p.FactoryHealth*.625);var b=View().Buildings.Single(x=>x.Id==factory);Assert.AreEqual((int)Math.Floor(p.FactoryCreditCost*p.BuildingSaleRefundRatio*.5),b.PrivateState.Lifecycle.Refund);
            var data=p.CopyData();data.revision++;Call("ApplyBalance",PlayableProfile.Create(data));Assert.AreEqual(p.FactoryHealth*.625,Health(factory));Assert.AreEqual(p.FactoryHealth*1.25,View().Buildings.Single(x=>x.Id==factory).MaxHealth);
        }
        [Test] public void QueuedConcreteUsesCommonTermsWithoutInventingAnArmyAiChoice()
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.ImprovedWeaponSystems),null));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueResearch,center));Assert.False(View().OwnerResearch.Single(o=>o.Kind==PlayableResearchKind.ReinforcedConcrete).Active);
            var observation=PlayableAiObservation.From(View());var budget=new AiBudgetLedger(observation.OwnerId,observation.Generation);budget.ObserveBank(observation.Credits);
            var plan=new AiTechnologyPlanner().Plan(observation,p,AiProfile.Initial,budget.Capture());Assert.False(plan.Branches.Any(b=>b.Descriptor.Kind==PlayableResearchKind.ReinforcedConcrete));Assert.True(plan.Branches.All(b=>b.WaitingSeconds>=60));
        }
        [Test] public void ConstructionUsesLiveResearchBonusWithFrozenBaseConstructionTerms()
        {
            int factory=Build(PlayableBuildingKind.Factory,1,false);Call("AdvanceBuildings",3d);
            var data=p.CopyData();data.revision++;data.reinforcedConcreteBonusPercent=75;data.reinforcedConcreteLevel2BonusPercent=100;var changed=PlayableProfile.Create(data);Call("ApplyBalance",changed);
            Complete();Call("AdvanceBuildings",100d);Assert.AreEqual(p.FactoryHealth*1.75,Health(factory),1e-8);Assert.AreEqual(Health(factory),View().Buildings.Single(b=>b.Id==factory).MaxHealth);
        }
        [Test] public void SecondConcreteRequiresFirstAndLocalUpgradedCenterAndReplacesTotal()
        {
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.QueueResearch,center,PlayableResearchKind.AdvancedReinforcedConcrete));
            Health(1,123.25);Complete();Assert.AreEqual(123.25*1.25,Health(1));
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.QueueResearch,center,PlayableResearchKind.AdvancedReinforcedConcrete));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueResearch,center,PlayableResearchKind.ScientificCenterUpgrade));Call("AdvanceResearch",30d);
            int other=Build(PlayableBuildingKind.ScientificCenter,3);Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.QueueResearch,other,PlayableResearchKind.AdvancedReinforcedConcrete));
            double bank=View().ExactCredits;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueResearch,center,PlayableResearchKind.AdvancedReinforcedConcrete));Assert.AreEqual(bank-200,View().ExactCredits);Call("AdvanceResearch",7d);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"concrete-II");Call("DestroyOwnedNavigation");domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"concrete-II"});
            CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"concrete-II"));Call("AdvanceResearch",22d);Assert.AreEqual(123.25*1.25,Health(1));Call("AdvanceResearch",1d);
            Assert.AreEqual(123.25*1.5,Health(1));Assert.AreEqual(p.HeadquartersHealth*1.5,View().Buildings.Single(b=>b.Id==1).MaxHealth);
            int future=Build(PlayableBuildingKind.Refinery,4);Assert.AreEqual(p.RefineryHealth*1.5,Health(future));
            Call("Damage",center,10000);Assert.AreEqual(p.HeadquartersHealth*1.5,View().Buildings.Single(b=>b.Id==1).MaxHealth);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.QueueResearch,other,PlayableResearchKind.AdvancedReinforcedConcrete));
        }
        [Test] public void ProfileControlsValidateAndRoundtrip()
        {
            Assert.AreEqual(200,p.ReinforcedConcreteCost);Assert.AreEqual(30,p.ReinforcedConcreteSeconds);Assert.AreEqual(25,p.ReinforcedConcreteBonusPercent);Assert.AreEqual(50,p.ReinforcedConcreteLevel2BonusPercent);Assert.AreEqual(200,p.ReinforcedConcreteLevel2Cost);Assert.AreEqual(30,p.ReinforcedConcreteLevel2Seconds);
            var fields=PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("research.reinforcedConcrete.")).ToArray();Assert.AreEqual(6,fields.Length);
            foreach(var f in fields){Assert.True(NativeBalanceFields.Editable(f));Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);
                foreach(var v in new[]{f.Minimum-f.Step,f.Maximum+f.Step,double.NaN}){var data=p.CopyData();typeof(PlayableProfileData).GetField(f.FieldName).SetValue(data,v);Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));}}
            var changed=p.CopyData();changed.reinforcedConcreteCost=240;changed.reinforcedConcreteSeconds=40;changed.reinforcedConcreteBonusPercent=75;changed.reinforcedConcreteLevel2BonusPercent=100;var q=PlayableProfile.Create(changed);Assert.AreEqual(240,q.ReinforcedConcreteCost);Assert.AreEqual(40,q.ReinforcedConcreteSeconds);Assert.AreEqual(75,q.ReinforcedConcreteBonusPercent);
        }
    }
}

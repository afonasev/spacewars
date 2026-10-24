using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class RingHangarTests
    {
        static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        object domain;PlayableProfile p;
        object Call(string name,params object[] args)=>D.GetMethod(name,F).Invoke(domain,args);
        PlayableSnapshot View()=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);
        [SetUp] public void Setup(){p=PlayableProfile.Default;domain=Activator.CreateInstance(D,F,null,new object[]{p,1L},null);}
        [TearDown] public void Cleanup(){((NavigationSession)D.GetField("navigation",F).GetValue(domain)).DestroyNavigationAuthority();}
        int Order(){Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Hangar,1,PlayableOwner.Player,null));return View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Hangar).Id;}
        [Test] public void FirstBuildingCostsFactoryPriceAndCancellationReleasesSlot()
        {
            Assert.AreEqual(p.FactoryCreditCost,p.HangarCreditCost);Assert.AreEqual(p.FactoryBuildSeconds,p.HangarBuildSeconds);
            var before=View();Assert.False(before.Buildings.Any(b=>b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.ScientificCenter));
            int id=Order();Assert.AreEqual(before.Credits-p.HangarCreditCost,View().Credits);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("CancelBuilding",id,PlayableOwner.Player,null));
            Assert.False(View().Buildings.Any(b=>b.Kind==PlayableBuildingKind.Hangar));
            Assert.AreEqual((int)(before.Credits-p.HangarCreditCost*(1-p.BuildingCancellationRefundRatio)),View().Credits);Order();
        }
        [Test] public void ConstructionFinishesOnItsOwnDurationAndRejectsProduction()
        {
            int id=Order();Call("AdvanceFoundations");Call("AdvanceBuildings",p.HangarBuildSeconds-1d);
            Assert.AreEqual(ConstructionPhase.Constructing,View().Buildings.Single(b=>b.Id==id).Phase);
            Call("AdvanceBuildings",1d);var b=View().Buildings.Single(b=>b.Id==id);
            Assert.AreEqual(ConstructionPhase.Ready,b.Phase);Assert.AreEqual(p.HangarHealth,b.Health);
            Assert.AreNotEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.QueueTank,new[]{id}),null));
            Assert.AreEqual(0,TerritoryRules.Income(p,PlayableBuildingKind.Hangar));
        }
        [Test] public void LabFieldsAreIndependentRangeValidatedAndRoundtrip()
        {
            var d=p.CopyData();var cost=PlayableProfileMetadata.Fields.Single(f=>f.FieldName=="hangarCreditCost");var time=PlayableProfileMetadata.Fields.Single(f=>f.FieldName=="hangarBuildSeconds");
            Assert.True(NativeBalanceFields.Editable(cost));Assert.True(NativeBalanceFields.Editable(time));
            NativeBalanceFields.Write(cost,d,450);NativeBalanceFields.Write(time,d,25);var q=PlayableProfile.Create(d);
            Assert.AreEqual(450,TerritoryRules.Cost(q,PlayableBuildingKind.Hangar));Assert.AreEqual(25,TerritoryRules.Duration(q,PlayableBuildingKind.Hangar));
            Assert.AreEqual(p.FactoryCreditCost,q.FactoryCreditCost);Assert.AreEqual(p.FactoryBuildSeconds,q.FactoryBuildSeconds);
            var copy=PlayableProfile.Create(NativeBalanceFields.Copy(q.CopyData()));Assert.AreEqual(q.HangarCreditCost,copy.HangarCreditCost);
            Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(time,d,0));
        }
        [Test] public void CurrentWorldRestorePreservesPendingHangarAndPaidTerms()
        {
            int id=Order();var bytes=(byte[])Call("CaptureWorldBytes",7,"ring-hangar-test");
            var restored=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"ring-hangar-test"});
            var saved=domain;
            try{domain=restored;var b=View().Buildings.Single(x=>x.Id==id);Assert.AreEqual(PlayableBuildingKind.Hangar,b.Kind);Assert.AreEqual(ConstructionPhase.Pending,b.Phase);CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"ring-hangar-test"));domain=saved;}
            finally{domain=saved;((NavigationSession)D.GetField("navigation",F).GetValue(restored)).DestroyNavigationAuthority();}
        }
    }
}

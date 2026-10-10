using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableIncomeTests
    {
        private static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;private PlayableProfile profile;
        private object Call(string name,params object[] args)=>D.GetMethod(name,F).Invoke(domain,args);
        private PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        private void Steps(int ticks){for(int i=0;i<ticks;i++)Call("Step",1d/30);}
        private object Building(int id)=>((IDictionary)D.GetField("buildings",F).GetValue(domain))[id];
        [SetUp] public void Setup(){profile=PlayableProfile.Default;domain=Activator.CreateInstance(D,F,null,new object[]{profile,1L},null);}
        [Test] public void EachSecondCreditsIncomeAndOnlyFifthSecondShowsFullPeriod()
        {
            for(int second=1;second<=5;second++)
            {
                Steps(30);var view=View();
                Assert.AreEqual(profile.StartingCredits+second*profile.HeadquartersIncomePerSecond,view.ExactCredits,1e-8);
                Assert.AreEqual(second==5?1:0,view.IncomeEvents.Count);
            }
            var e=View().IncomeEvents.Single();Assert.AreEqual(150,e.Tick);Assert.AreEqual(profile.HeadquartersIncomePerPeriod,e.Amount);
            Assert.AreEqual(PlayableOwner.Player,e.Owner);
            Assert.True(View(PlayableOwner.Enemy).IncomeEvents.All(x=>x.Owner==PlayableOwner.Enemy));
            Steps(29);Assert.AreEqual(1,View().IncomeEvents.Count);Steps(1);Assert.IsEmpty(View().IncomeEvents);
            Steps(120);Assert.AreEqual(300,View().IncomeEvents.Single().Tick);
        }
        [Test] public void ConfiguredPeriodChangesPresentationAndRateWithoutChangingOneSecondSettlement()
        {
            var data=profile.CopyData();data.incomePeriodSeconds=3;profile=PlayableProfile.Create(data);
            domain=Activator.CreateInstance(D,F,null,new object[]{profile,1L},null);
            for(int second=1;second<=3;second++){Steps(30);Assert.AreEqual(profile.StartingCredits+second*profile.HeadquartersIncomePerSecond,View().ExactCredits,1e-8);Assert.AreEqual(second==3?1:0,View().IncomeEvents.Count);}
        }
        [Test] public void UnreadyAndSellingBuildingsDoNotEarnOrCreateMarkers()
        {
            int hq=View().Buildings.Single(b=>b.Owner==PlayableOwner.Player).Id;
            var building=Building(hq);building.GetType().GetField("Ready").SetValue(building,false);
            building.GetType().GetField("Build").SetValue(building,0d);
            Steps(150);Assert.AreEqual(profile.StartingCredits,View().ExactCredits);Assert.IsEmpty(View().IncomeEvents);
            building.GetType().GetField("Ready").SetValue(building,true);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.SellBuilding,new[]{hq}),null));
            double afterSale=View().ExactCredits;Steps(30);Assert.AreEqual(afterSale,View().ExactCredits);Assert.IsEmpty(View().IncomeEvents);
        }
        [Test] public void ColdRestoreRetainsVisualFactsWithoutDuplicatingMoney()
        {
            Steps(150);var before=View();var bytes=(byte[])Call("CaptureWorldBytes",7,"income-test");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,profile,7,"income-test"});
            Assert.AreEqual(before.ExactCredits,View().ExactCredits);Assert.AreEqual(before.IncomeEvents.Single().Tick,View().IncomeEvents.Single().Tick);
            CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"income-test"));
            Steps(30);Assert.AreEqual(before.ExactCredits+profile.HeadquartersIncomePerSecond,View().ExactCredits,1e-8);Assert.IsEmpty(View().IncomeEvents);
        }
        [Test] public void RefineryUsesOrdinaryIncomeWhileUpgradingAndNewIncomeAfterCompletion()
        {
            Call("AddBuilding",PlayableOwner.Player,PlayableBuildingKind.Refinery,new NavPoint(0,-12),true);
            int refinery=View().Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Refinery).Id;
            var building=Building(refinery);var type=D.GetNestedType("RefineryUpgrade",BindingFlags.NonPublic);
            var upgrade=Activator.CreateInstance(type,true);
            type.GetField("TermsRevision").SetValue(upgrade,profile.Revision);type.GetField("Duration").SetValue(upgrade,100d);
            building.GetType().GetField("Upgrade").SetValue(building,upgrade);
            Steps(150);Assert.AreEqual(profile.RefineryIncomePerPeriod,View().IncomeEvents.Single(e=>e.BuildingId==refinery).Amount);
            type.GetField("Complete").SetValue(upgrade,true);double before=View().ExactCredits;Steps(30);
            Assert.AreEqual(before+profile.HeadquartersIncomePerSecond+profile.RefineryUpgradedIncome/profile.IncomePeriodSeconds,View().ExactCredits,1e-8);
            Steps(120);Assert.AreEqual(profile.RefineryUpgradedIncome,View().IncomeEvents.Single(e=>e.BuildingId==refinery).Amount);
        }
        [Test] public void PublishedEventCollectionCannotBeChangedAndDoesNotFollowDomainPruning()
        {
            Steps(150);var view=View();var events=(System.Collections.Generic.IList<PlayableIncomeEvent>)view.IncomeEvents;
            Assert.Throws<NotSupportedException>(()=>events.Clear());Steps(30);
            Assert.AreEqual(1,view.IncomeEvents.Count);Assert.IsEmpty(View().IncomeEvents);
        }
    }
}

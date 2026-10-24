using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class FactoryReleaseLifecycleTests
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        PlayableDomain domain;PlayableProfile profile;int factory;long sequence;
        object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,Hidden).Invoke(domain,args);
        PlayableSnapshot View=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        PlayableBuildingSnapshot Factory=>View.Buildings.Single(b=>b.Id==factory);
        PlayableCommandStatus Send(PlayableCommandKind kind,int id,long order=0)=>domain.Apply(new PlayableCommand(1,++sequence,"player-1",kind,new[]{id},new NavPoint(-10,15),productionOrderId:order),out _);
        [SetUp] public void Setup()
        {
            profile=PlayableProfile.Default;domain=new PlayableDomain(profile,1,false);
            Call("AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);factory=View.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;
        }
        [TearDown] public void Cleanup(){domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
        void Advance(int ticks=1){for(int i=0;i<ticks;i++){Call("AdvanceProduction",1d/30);Call("AdvanceBuildingLifecycle",1d/30);}}
        void Until(int phase){for(int i=0;i<1800&&Factory.LaunchPhase!=phase;i++)Advance();Assert.AreEqual(phase,Factory.LaunchPhase);}
        [TestCase(PlayableEntityKind.Tank,PlayableCommandKind.QueueTank)]
        [TestCase(PlayableEntityKind.Explorer,PlayableCommandKind.QueueExplorer)]
        [TestCase(PlayableEntityKind.Shkval,PlayableCommandKind.QueueShkval)]
        public void FullClearanceCreatesControllableDamageableUnitAtOneBoundary(PlayableEntityKind kind,PlayableCommandKind command)
        {
            var initial=View.Entities.Select(e=>e.Id).ToArray();int crowd=domain.Navigation.Crowd.Units.Count;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(command,factory));Until(FactoryReleaseRules.Opening);
            CollectionAssert.AreEqual(initial,View.Entities.Select(e=>e.Id));Until(FactoryReleaseRules.Driving);
            int prospective=(int)typeof(PlayableDomain).GetField("nextId",Hidden).GetValue(domain);
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.Move,prospective));
            Call("Damage",prospective,10);Assert.AreEqual(crowd,domain.Navigation.Crowd.Units.Count);
            double duration=FactoryReleaseRules.Duration(profile,kind);
            while(Factory.LaunchTime+1d/30<duration){Advance();CollectionAssert.AreEqual(initial,View.Entities.Select(e=>e.Id));}
            Advance();Assert.AreEqual(FactoryReleaseRules.Closing,Factory.LaunchPhase);
            var born=View.Entities.Single(e=>!initial.Contains(e.Id));Assert.AreEqual(prospective,born.Id);Assert.AreEqual(kind,born.Kind);
            Assert.AreEqual(FactoryReleaseRules.Exit(profile,Factory.Position,Factory.Heading,kind),born.Position);
            Assert.GreaterOrEqual(FactoryReleaseRules.ExitDistance(profile,kind)-FactoryReleaseRules.ModelRadius(profile,kind),FactoryReleaseRules.GatePlane(profile));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Move,born.Id));
            Call("Damage",born.Id,10);Assert.AreEqual(born.Health-10,View.Entities.Single(e=>e.Id==born.Id).Health);
            Assert.AreEqual(crowd+1,domain.Navigation.Crowd.Units.Count);Assert.IsEmpty(Factory.PrivateState.Orders);
        }
        [Test] public void OccupiedStraightExitWaitsWithoutTeleportingOrSpendingAgain()
        {
            var exit=FactoryReleaseRules.Exit(profile,Factory.Position,Factory.Heading,PlayableEntityKind.Tank);
            int blocker=(int)Call("SpawnUnit",exit,PlayableOwner.Player,PlayableEntityKind.Tank);int count=View.Entities.Count;
            Send(PlayableCommandKind.QueueTank,factory);int credits=View.Credits;Advance(1500);
            Assert.AreEqual(0,Factory.LaunchPhase);Assert.Zero(Factory.PrivateState.Orders.Single().Remaining);Assert.AreEqual(count,View.Entities.Count);Assert.AreEqual(credits,View.Credits);
            Call("Damage",blocker,100000);Until(FactoryReleaseRules.Closing);Assert.AreEqual(count,View.Entities.Count);Assert.IsEmpty(Factory.PrivateState.Orders);
        }
        [TestCase("cancel")][TestCase("sale")][TestCase("destroy")]
        public void ProducerCancellationDoesNotCreateProtectedVehicle(string action)
        {
            var ids=View.Entities.Select(e=>e.Id).ToArray();Send(PlayableCommandKind.QueueTank,factory);Until(FactoryReleaseRules.Driving);Advance(5);
            if(action=="cancel")Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.CancelProductionOrder,factory,Factory.PrivateState.Orders[0].Id));
            else if(action=="sale")Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.SellBuilding,factory));else Call("Damage",factory,100000);
            Advance(300);CollectionAssert.AreEqual(ids,View.Entities.Select(e=>e.Id));
        }
        [Test] public void DrivingSaveRestoresExactContinuationAndQueuedHeadWaitsForClosure()
        {
            Send(PlayableCommandKind.QueueTank,factory);Send(PlayableCommandKind.QueueExplorer,factory);Until(FactoryReleaseRules.Driving);Advance(12);
            var bytes=domain.CaptureWorldBytes(7,"factory-exit");var restored=PlayableDomain.RestoreWorldBytes(bytes,profile,7,"factory-exit");var original=domain;
            try
            {
                for(int i=0;i<100;i++){domain=original;Advance();domain=restored;Advance();}
                CollectionAssert.AreEqual(original.CaptureWorldBytes(7,"factory-exit"),restored.CaptureWorldBytes(7,"factory-exit"));
            }
            finally{domain=original;restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
            Assert.AreEqual(PlayableEntityKind.Explorer,Factory.PrivateState.Orders.Single().Kind);
        }
        [Test] public void NextOrderRemainsInactiveDuringGateClosure()
        {
            Send(PlayableCommandKind.QueueTank,factory);Send(PlayableCommandKind.QueueExplorer,factory);Until(FactoryReleaseRules.Closing);
            Assert.False(Factory.PrivateState.Orders.Single().Active);Advance();Assert.False(Factory.PrivateState.Orders.Single().Active);
            Until(0);Assert.False(Factory.PrivateState.Orders.Single().Active);Advance();Assert.True(Factory.PrivateState.Orders.Single().Active);
        }
    }
}

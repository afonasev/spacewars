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
            Assert.That(View.Buildings.All(b=>b.Heading==TerritoryRules.BuildingHeading),Is.True,"New buildings face world south, independently of radial pad headings");
        }
        [TearDown] public void Cleanup(){domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
        void Advance(int ticks=1){for(int i=0;i<ticks;i++){Call("AdvanceProduction",1d/30);Call("AdvanceBuildingLifecycle",1d/30);}}
        void Until(int phase){for(int i=0;i<1800&&Factory.LaunchPhase!=phase;i++)Advance();Assert.AreEqual(phase,Factory.LaunchPhase);}
        [TestCase(PlayableEntityKind.Tank,PlayableCommandKind.QueueTank)]
        [TestCase(PlayableEntityKind.Explorer,PlayableCommandKind.QueueExplorer)]
        [TestCase(PlayableEntityKind.Shkval,PlayableCommandKind.QueueShkval)]
        public void FullClearanceCreatesControllableDamageableUnitAtOneBoundary(PlayableEntityKind kind,PlayableCommandKind command)
        {
            if(kind==PlayableEntityKind.Shkval)
            {
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));
                Call("AdvanceFoundations");Call("AdvanceBuildings",100d);int center=View.Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter).Id;
                Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.ScientificCenterUpgrade),out _));
                Call("AdvanceResearch",PlayableResearchRules.Duration(profile,PlayableResearchKind.ScientificCenterUpgrade));
                Assert.True(View.Buildings.Single(b=>b.Id==center).ScientificCenterUpgraded,"Stock Shkval prerequisite must be actually complete before queue admission");
            }
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
        [Test] public void StaleBirthReadsetRetriesSamePaidReleaseWithoutPartialActorOrLostIdentity()
        {
            Send(PlayableCommandKind.QueueTank,factory);Until(FactoryReleaseRules.Driving);
            double duration=FactoryReleaseRules.Duration(profile,PlayableEntityKind.Tank);
            while(Factory.LaunchTime+1d/30<duration)Advance();
            int next=(int)typeof(PlayableDomain).GetField("nextId",Hidden).GetValue(domain);
            int count=View.Entities.Count,credits=View.Credits;var mover=View.Entities.First(u=>u.Owner==PlayableOwner.Player);
            int changes=0;domain.Navigation.BeforeCrowdBirthPublicationForTests=()=>{
                domain.Navigation.BeforeCrowdBirthPublicationForTests=null;changes++;
                Assert.True(domain.Navigation.Move(mover.Id,mover.Position),"Publish a real competing movement readset before birth transfer");
            };
            try{Advance();}finally{domain.Navigation.BeforeCrowdBirthPublicationForTests=null;}
            Assert.AreEqual(1,changes);Assert.AreEqual(count,View.Entities.Count);Assert.AreEqual(next,typeof(PlayableDomain).GetField("nextId",Hidden).GetValue(domain));
            Assert.AreEqual(FactoryReleaseRules.Driving,Factory.LaunchPhase);Assert.AreEqual(duration,Factory.LaunchTime);Assert.AreEqual(credits,View.Credits);
            Assert.AreEqual(1,Factory.PrivateState.Orders.Count);Assert.Zero(Factory.PrivateState.Orders.Single().Remaining);
            Assert.Zero(domain.Navigation.PhysicalHostBudget.HeldBytes);Assert.Zero(domain.Navigation.PhysicalHostBudget.ActiveScopes);
            Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(domain.Navigation),domain.Navigation.PhysicalHostBudget.PhysicalBytes,"Rejected birth leaves no retained actor/spatial/name payload");
            Advance();Assert.AreEqual(FactoryReleaseRules.Closing,Factory.LaunchPhase);Assert.AreEqual(count+1,View.Entities.Count);
            Assert.True(View.Entities.Any(u=>u.Id==next&&u.Kind==PlayableEntityKind.Tank));Assert.AreEqual(next+1,typeof(PlayableDomain).GetField("nextId",Hidden).GetValue(domain));
            Assert.AreEqual(credits,View.Credits);Assert.IsEmpty(Factory.PrivateState.Orders);
        }
        [Test] public void OccupiedStraightExitWaitsWithoutTeleportingOrSpendingAgain()
        {
            var exit=FactoryReleaseRules.Exit(profile,Factory.Position,Factory.Heading,PlayableEntityKind.Tank);
            int blocker=(int)Call("SpawnUnit",exit,PlayableOwner.Player,PlayableEntityKind.Tank);int count=View.Entities.Count;
            Send(PlayableCommandKind.QueueTank,factory);int credits=View.Credits;Advance(1500);
            Assert.AreEqual(0,Factory.LaunchPhase);Assert.Zero(Factory.PrivateState.Orders.Single().Remaining);Assert.AreEqual(count,View.Entities.Count);Assert.AreEqual(credits,View.Credits);
            Call("Damage",blocker,100000);
            // Current wreck rules keep the destroyed hull solid at the exit.
            // Clearing the blocker therefore also requires destroying its wreck;
            // killing only the living unit cannot make this corridor free.
            Call("Damage",blocker,100000);
            Until(FactoryReleaseRules.Closing);Assert.AreEqual(count,View.Entities.Count);Assert.IsEmpty(Factory.PrivateState.Orders);
        }
        [Test] public void SavedActiveRadialReleaseFinishesBeforeSwitchingSouth()
        {
            var all=(System.Collections.IDictionary)typeof(PlayableDomain).GetField("buildings",Hidden).GetValue(domain);
            var building=all[factory];building.GetType().GetField("Heading").SetValue(building,0d);
            Send(PlayableCommandKind.QueueTank,factory);Until(FactoryReleaseRules.Driving);Advance(12);
            var restored=PlayableDomain.RestoreWorldBytes(domain.CaptureWorldBytes(7,"radial-release"),profile,7,"radial-release");var original=domain;
            try
            {
                Assert.AreEqual(0,Factory.Heading);
                for(int tick=0;tick<300;tick++)
                {
                    Advance();domain=restored;Advance();domain=original;
                    CollectionAssert.AreEqual(original.CaptureWorldBytes(7,"radial-release"),restored.CaptureWorldBytes(7,"radial-release"));
                }
                Assert.AreEqual(TerritoryRules.BuildingHeading,Factory.Heading);
            }
            finally{domain=original;restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        [Test] public void SavedIdleRadialFactoryNormalizesToSouth()
        {
            var all=(System.Collections.IDictionary)typeof(PlayableDomain).GetField("buildings",Hidden).GetValue(domain);
            var building=all[factory];building.GetType().GetField("Heading").SetValue(building,0d);
            var restored=PlayableDomain.RestoreWorldBytes(domain.CaptureWorldBytes(7,"idle-radial"),profile,7,"idle-radial");var original=domain;
            try{domain=restored;Assert.AreEqual(TerritoryRules.BuildingHeading,Factory.Heading);}
            finally{domain=original;restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
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

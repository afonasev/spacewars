using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class EconomySoundProjectionTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        private PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        [SetUp] public void Setup(){domain=Activator.CreateInstance(Domain,Flags,null,new object[]{PlayableProfile.Default,1L},null);Call("AddCredits",PlayableOwner.Player,100000d);View();}
        private int Build(PlayableBuildingKind kind,int slot,bool ready=true)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,kind,1,PlayableOwner.Player,null));
            int id=View().Buildings.Single(b=>b.SlotId==slot&&b.SiteId==1).Id;
            if(ready){Call("AdvanceFoundations");Call("AdvanceBuildings",1000d);}return id;
        }
        private int Count(PlayableSoundKind kind)=>View().Sounds.Count(e=>e.Kind==kind);
        [Test] public void FoundationEmitsOnlyWhenActuallyStartedAndCancellationIsNotDestruction()
        {
            int pending=Build(PlayableBuildingKind.Factory,1,false);
            Assert.Zero(Count(PlayableSoundKind.ConstructionStarted));Call("AdvanceFoundations");
            Assert.AreEqual(1,Count(PlayableSoundKind.ConstructionStarted));Call("AdvanceFoundations");Assert.AreEqual(1,Count(PlayableSoundKind.ConstructionStarted));
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("CancelBuilding",pending,PlayableOwner.Player,null));
            Assert.AreEqual(1,Count(PlayableSoundKind.ConstructionCancelled));Assert.Zero(Count(PlayableSoundKind.Destroyed));
        }
        [Test] public void RepairAndDemolitionCompletionAreCausalAndDoNotRepeat()
        {
            int id=Build(PlayableBuildingKind.Factory,1);Call("Damage",id,20);
            ((IDictionary)Domain.GetField("buildings",Flags).GetValue(domain))[id].GetType().GetField("LastDamageTime").SetValue(((IDictionary)Domain.GetField("buildings",Flags).GetValue(domain))[id],null);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("StartRepair",id,PlayableOwner.Player,null));
            Call("AdvanceBuildingLifecycle",1000d);Call("AdvanceBuildingLifecycle",1000d);
            Assert.AreEqual(1,Count(PlayableSoundKind.RepairStarted));Assert.AreEqual(1,Count(PlayableSoundKind.RepairComplete));
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("SellBuilding",id,PlayableOwner.Player,null));
            Call("AdvanceBuildingLifecycle",1000d);Call("AdvanceBuildingLifecycle",1000d);
            Assert.AreEqual(1,Count(PlayableSoundKind.DemolitionStarted));Assert.AreEqual(1,Count(PlayableSoundKind.Demolished));Assert.Zero(Count(PlayableSoundKind.Destroyed));
        }
        [Test] public void ProductionQueueCancelStartAndReadyMatchActualTransactions()
        {
            int id=Build(PlayableBuildingKind.Factory,1);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("QueueUnit",id,PlayableOwner.Player,PlayableEntityKind.Explorer,null));
            long order=View().Buildings.Single(b=>b.Id==id).PrivateState.Orders.Single().Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("CancelProduction",id,order,PlayableOwner.Player,null));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Call("CancelProduction",id,order,PlayableOwner.Player,null));
            Assert.AreEqual(1,Count(PlayableSoundKind.ProductionCancelled));
            Call("QueueUnit",id,PlayableOwner.Player,PlayableEntityKind.Tank,null);Call("AdvanceProduction",1000d);Call("AdvanceProduction",1000d);
            Assert.AreEqual(2,Count(PlayableSoundKind.ProductionQueued));Assert.AreEqual(1,Count(PlayableSoundKind.ProductionStarted));Assert.AreEqual(1,Count(PlayableSoundKind.ProductionComplete));
        }
        [Test] public void ResearchAndRefineryUpgradeHaveCompletionAndCancellationEvents()
        {
            int science=Build(PlayableBuildingKind.ScientificCenter,1),refinery=Build(PlayableBuildingKind.Refinery,2);
            Call("QueueResearch",science,PlayableOwner.Player,PlayableResearchKind.TankChassis,null);Call("AdvanceResearch",1000d);
            Assert.AreEqual(1,Count(PlayableSoundKind.ResearchQueued));Assert.AreEqual(1,Count(PlayableSoundKind.ResearchStarted));Assert.AreEqual(1,Count(PlayableSoundKind.ResearchComplete));
            Call("QueueResearch",science,PlayableOwner.Player,PlayableResearchKind.ExplorerAssaultGuns,null);
            var order=View().OwnerResearch.Single(r=>r.Active);Call("CancelResearch",science,order.Id,PlayableOwner.Player,null);Assert.AreEqual(1,Count(PlayableSoundKind.ResearchCancelled));
            Call("StartRefineryUpgrade",refinery,PlayableOwner.Player,null);Call("CancelRefineryUpgrade",refinery,PlayableOwner.Player,null);
            Call("StartRefineryUpgrade",refinery,PlayableOwner.Player,null);Call("AdvanceRefineryUpgrades",1000d);Call("AdvanceRefineryUpgrades",1000d);
            Assert.AreEqual(2,Count(PlayableSoundKind.UpgradeStarted));Assert.AreEqual(1,Count(PlayableSoundKind.UpgradeCancelled));Assert.AreEqual(1,Count(PlayableSoundKind.UpgradeComplete));
        }
        [Test] public void PrivateEconomicSoundDoesNotLeakToVisibleOpponentOrAi()
        {
            var point=View().Entities.First().Position;
            var nav=(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
            var nearby=new[]{3d,5d,8d}.Select(x=>new NavPoint(point.X,point.Z+x)).First(p=>nav.Crowd.CanPlace(p,PlayableProfile.Default.ExplorerCollisionRadius));
            Call("SpawnUnit",nearby,PlayableOwner.Enemy,PlayableEntityKind.Explorer);Call("RefreshVision");
            Assert.IsTrue(View(PlayableOwner.Enemy).Entities.Any(e=>e.Owner==PlayableOwner.Player),"Opponent actually sees the source; private economics still must be absent.");
            Call("Sound",PlayableSoundKind.ResearchComplete,point,PlayableEntityKind.Tank,(PlayableOwner?)PlayableOwner.Player,true);
            Assert.IsTrue(View().Sounds.Any(e=>e.Kind==PlayableSoundKind.ResearchComplete));
            Assert.IsFalse(View(PlayableOwner.Enemy).Sounds.Any(e=>e.Kind==PlayableSoundKind.ResearchComplete));
            var ai=(PlayableSnapshot)Call("PlayerSnapshotForAi",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player,true);
            Assert.IsEmpty(ai.Sounds);
        }
        [Test] public void MotionStopsOnlyOnActualTransitionAndRestoreSeedsExistingMovement()
        {
            var nav=(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
            var unit=nav.Crowd.Units.First();Call("ObserveSoundMotion");
            var goal=new NavPoint(unit.Position.X+3,unit.Position.Z);
            Assert.IsTrue(nav.Crowd.SetRoute(unit.Id,goal,new[]{unit.Position,goal}));
            Call("ObserveSoundMotion");Assert.AreEqual(1,Count(PlayableSoundKind.MovementStarted));Call("ObserveSoundMotion");Assert.AreEqual(1,Count(PlayableSoundKind.MovementStarted));
            nav.Stop(unit.Id,false);Call("ObserveSoundMotion");Call("ObserveSoundMotion");Assert.AreEqual(1,Count(PlayableSoundKind.MovementStopped));
            const string source="native-domain-world-v1:c77962dd:source:96a32f63:ai-release-c569e3a03045";
            Assert.IsTrue(nav.Crowd.SetRoute(unit.Id,goal,new[]{unit.Position,goal}));
            var bytes=(byte[])Call("CaptureWorldBytes",7,source);
            Call("ObserveSoundMotion");CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,source));
            domain=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,source});
            Call("ObserveSoundMotion");Assert.IsEmpty(View().Sounds);
        }
    }
}

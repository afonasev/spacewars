using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class OrbitalSystemFailureTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly Type type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain");
        object domain;PlayableProfile profile;OfflineMatchConfiguration config;long sequence;
        object Call(string name,params object[] args)=>type.GetMethod(name,F).Invoke(domain,args);
        PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Enemy)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        PlayableCommandStatus Send(PlayableCommandKind kind,int id=0,NavPoint target=default,PlayableOwner owner=PlayableOwner.Player,int ability=3)=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-"+((int)owner+1),kind,id==0?null:new[]{id},target,targetId:ability),null);
        [SetUp] public void Setup()
        {
            var map=new AiTestMap();profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            config=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human),new OfflineParticipant("player-3",3,1,OfflineControl.Human)});
            domain=Activator.CreateInstance(type,F,null,new object[]{profile,1L,config},null);
            var p=profile.CopyData();Call("CreditOrbital",(PlayableOwner?)PlayableOwner.Player,PlayableOwner.Enemy,p.orbitalThreshold1+p.orbitalThreshold2+p.orbitalThreshold3);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ChooseOrbital,ability:1));Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ChooseOrbital,ability:2));Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ChooseOrbital));
        }
        void Reveal(NavPoint point)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("UseOrbital",PlayableOwner.Player,1,point));
            ((OrbitalEscalationState)Call("Orbital",PlayableOwner.Player)).Advance(10,profile.CopyData());
        }
        PlayableCommandStatus VisibleFailure(NavPoint point){Reveal(point);return Send(PlayableCommandKind.UseOrbital,target:point);}
        [TearDown] public void Cleanup(){if(domain!=null)Call("DestroyOwnedNavigation");}
        int Build(PlayableBuildingKind kind,int slot)
        {
            Call("AddCredits",PlayableOwner.Enemy,10000d);var home=View().Buildings.First(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Headquarters);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",home.SiteId,slot,kind,home.Id,PlayableOwner.Enemy,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            return View().Buildings.Single(b=>b.Kind==kind&&b.SlotId==slot&&b.Owner==PlayableOwner.Enemy).Id;
        }
        [Test] public void SelectionUsesCentersNearestLargeTieBreakAndEveryTower()
        {
            var c=new[]{new OrbitalBuildingTarget(7,PlayableBuildingKind.Factory,new NavPoint(2,0)),new OrbitalBuildingTarget(6,PlayableBuildingKind.Headquarters,new NavPoint(-2,0)),new OrbitalBuildingTarget(8,PlayableBuildingKind.Refinery,new NavPoint(3,0)),new OrbitalBuildingTarget(3,PlayableBuildingKind.SentryTurret,new NavPoint(0,0)),new OrbitalBuildingTarget(4,PlayableBuildingKind.RepairTower,new NavPoint(5,0)),new OrbitalBuildingTarget(5,PlayableBuildingKind.GaussTower,new NavPoint(5.01,0))};
            CollectionAssert.AreEqual(new[]{6,7,3,4},OrbitalSystemFailure.Select(c,default,5,2));CollectionAssert.AreEqual(new[]{6,7,3,4},OrbitalSystemFailure.Select(c.Reverse(),default,5,2));
            CollectionAssert.AreEqual(new[]{3},OrbitalSystemFailure.Select(c,default,1,2));Assert.IsEmpty(OrbitalSystemFailure.Select(c,new NavPoint(30,30),1,2));
        }
        [Test] public void InvalidEmptyAndFriendlyAreaSpendsNoCooldown()
        {
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,target:new NavPoint(double.NaN,0)));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,target:new NavPoint(0,0)));
            var home=View(PlayableOwner.Player).Buildings.First(b=>b.Owner==PlayableOwner.Player);Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,target:home.Position));
            Assert.Zero(View(PlayableOwner.Player).Orbital.CommonRemaining);Assert.Zero(View(PlayableOwner.Player).Orbital.Abilities.Single(a=>a.Id==3).Remaining);
        }
        [Test] public void HiddenAndRememberedCentersRejectWithoutSpendingOrSuccessSound()
        {
            var home=View().Buildings.First(b=>b.Owner==PlayableOwner.Enemy);Assert.False(View(PlayableOwner.Player).Vision.IsVisible(home.Position));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,target:home.Position));
            Reveal(home.Position);Assert.True(View(PlayableOwner.Player).Vision.IsVisible(home.Position));
            Call("AdvanceOrbital",profile.CopyData().orbitalScanSeconds);Call("RefreshVision");var seen=View(PlayableOwner.Player);
            Assert.True(seen.Vision.KnownBuildings.Any(b=>b.Id==home.Id));Assert.False(seen.Vision.IsVisible(home.Position));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.UseOrbital,target:home.Position));
            Assert.Zero(View(PlayableOwner.Player).Orbital.CommonRemaining);Assert.Zero(View(PlayableOwner.Player).Orbital.Abilities.Single(a=>a.Id==3).Remaining);
            Assert.Zero(View().Sounds.Count(e=>e.Kind==PlayableSoundKind.OrbitalSystemsDisabled));
        }
        [Test] public void CurrentAlliedScanAuthorizesTheApplicationCenter()
        {
            var home=View().Buildings.First(b=>b.Owner==PlayableOwner.Enemy);var p=profile.CopyData();
            Call("CreditOrbital",(PlayableOwner?)PlayableOwner.Third,PlayableOwner.Enemy,p.orbitalThreshold1);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("ChooseOrbital",PlayableOwner.Third,1));Assert.AreEqual(PlayableCommandStatus.Applied,Call("UseOrbital",PlayableOwner.Third,1,home.Position));
            Assert.True(View(PlayableOwner.Player).Vision.IsVisible(home.Position));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,target:home.Position));Assert.True(View().Buildings.Single(b=>b.Id==home.Id).SystemsDisabled);
        }
        [Test] public void PaidProductionAndReadOnlyQueueResumeWithoutLostProgress()
        {
            int id=Build(PlayableBuildingKind.Factory,1);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueTank,id,owner:PlayableOwner.Enemy));Call("AdvanceProduction",.5d);
            var before=View().Buildings.Single(b=>b.Id==id);double remaining=before.PrivateState.Orders.Single().Remaining;
            Assert.AreEqual(PlayableCommandStatus.Applied,VisibleFailure(before.Position));
            Call("AdvanceProduction",2d);var frozen=View().Buildings.Single(b=>b.Id==id);Assert.True(frozen.SystemsDisabled);Assert.AreEqual(remaining,frozen.PrivateState.Orders.Single().Remaining);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.QueueTank,id,owner:PlayableOwner.Enemy));Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.SellBuilding,id,owner:PlayableOwner.Enemy));Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.StartBuildingRepair,id,owner:PlayableOwner.Enemy));
            Assert.IsEmpty(OfflinePadWorldActions.Building(View(),profile,id));
            // Absolute simulation time controls the effect independently of MAX cooldown speed.
            type.GetField("elapsed",F).SetValue(domain,20d);Call("AdvanceOrbital",0d);Call("AdvanceProduction",.5d);
            var resumed=View().Buildings.Single(b=>b.Id==id);Assert.False(resumed.SystemsDisabled);Assert.AreEqual(remaining-.5,resumed.PrivateState.Orders.Single().Remaining);
        }
        [Test] public void IncomeDropsAndOneSoundIsRecorded()
        {
            int id=Build(PlayableBuildingKind.Refinery,1);var before=View().Buildings.Single(b=>b.Id==id);
            double income=(double)Call("Income",PlayableOwner.Enemy);Assert.AreEqual(PlayableCommandStatus.Applied,VisibleFailure(before.Position));
            Assert.AreEqual(income-TerritoryRules.Income(profile,PlayableBuildingKind.Refinery),(double)Call("Income",PlayableOwner.Enemy));
            Assert.AreEqual(1,View().Sounds.Count(s=>s.Kind==PlayableSoundKind.OrbitalSystemsDisabled));
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.UpgradeRefinery,id,owner:PlayableOwner.Enemy));
        }
        [Test] public void ResearchQueueFreezesWithoutCancellation()
        {
            int factory=Build(PlayableBuildingKind.Factory,1);int center=Build(PlayableBuildingKind.ScientificCenter,2);
            foreach(var kind in new[]{PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.ReinforcedConcrete})Assert.AreEqual(PlayableCommandStatus.Applied,Call("QueueResearch",center,PlayableOwner.Enemy,kind,null));
            Call("AdvanceResearch",.5d);var before=View().OwnerResearch.Where(o=>!o.Complete).ToArray();Assert.AreEqual(3,before.Length);Assert.Greater(before[0].Elapsed,0);
            var point=View().Buildings.Single(b=>b.Id==center).Position;Assert.AreEqual(PlayableCommandStatus.Applied,VisibleFailure(point));Call("AdvanceResearch",4d);
            var after=View().OwnerResearch.Where(o=>!o.Complete).ToArray();CollectionAssert.AreEqual(before.Select(o=>o.Id),after.Select(o=>o.Id));Assert.AreEqual(before[0].Elapsed,after[0].Elapsed);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.CancelResearch,center,owner:PlayableOwner.Enemy));
            type.GetField("elapsed",F).SetValue(domain,20d);Call("AdvanceOrbital",0d);Call("AdvanceResearch",.5d);Assert.AreEqual(before[0].Elapsed+.5,View().OwnerResearch.First(o=>o.Id==before[0].Id).Elapsed);
        }
        [Test] public void ArmedTowerCannotCreateShotsWhileDisabled()
        {
            int tower=Build(PlayableBuildingKind.SentryTurret,TerritoryRules.DefenseSlotOffset+1);var before=View().Buildings.Single(b=>b.Id==tower);
            Assert.AreEqual(PlayableCommandStatus.Applied,VisibleFailure(before.Position));
            for(int i=0;i<30;i++)Call("AdvanceTurrets",1d/30);
            Assert.AreEqual(before.Turret.ShotSequence,View().Buildings.Single(b=>b.Id==tower).Turret.ShotSequence);Assert.True(View().Buildings.Single(b=>b.Id==tower).SystemsDisabled);
        }
        [Test] public void OngoingRepairFreezesAndParentSaleCannotBypassDisable()
        {
            int id=Build(PlayableBuildingKind.Factory,1);var rows=(System.Collections.IDictionary)type.GetField("buildings",F).GetValue(domain);var building=rows[id];building.GetType().GetField("Health").SetValue(building,150d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("StartRepair",id,PlayableOwner.Enemy,null));var before=View().Buildings.Single(b=>b.Id==id);
            Assert.AreEqual(PlayableCommandStatus.Applied,VisibleFailure(before.Position));Call("AdvanceBuildingLifecycle",2d);
            Assert.AreEqual(before.Health,View().Buildings.Single(b=>b.Id==id).Health);
            var home=View().Buildings.First(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Headquarters);Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.SellBuilding,home.Id,owner:PlayableOwner.Enemy));
            type.GetField("elapsed",F).SetValue(domain,20d);Call("AdvanceOrbital",0d);Call("AdvanceBuildingLifecycle",2d);Assert.Greater(View().Buildings.Single(b=>b.Id==id).Health,before.Health);
        }
        [Test] public void CurrentCheckpointRestoresExactDisable()
        {
            var home=View().Buildings.First(b=>b.Owner==PlayableOwner.Enemy);Assert.AreEqual(PlayableCommandStatus.Applied,VisibleFailure(home.Position));
            var bytes=(byte[])Call("CaptureWorldBytes",7,config.SourceIdentity);var restored=OfflineParticipantAuthority.Restore(bytes,config);
            try{CollectionAssert.AreEqual(bytes,restored.CaptureBytes());Assert.AreEqual(20,restored.View("player-2").Buildings.Single(b=>b.Id==home.Id).SystemFailureRemaining);}
            finally{var other=typeof(OfflineParticipantAuthority).GetProperty("Domain",F).GetValue(restored);type.GetMethod("DestroyOwnedNavigation",F).Invoke(other,null);}
        }
    }
}

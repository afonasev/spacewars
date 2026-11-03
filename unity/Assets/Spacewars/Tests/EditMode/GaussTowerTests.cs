using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class GaussTowerTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly List<PlayableDomain> owned=new List<PlayableDomain>();
        object Call(PlayableDomain d,string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
        PlayableDomain New(){var d=new PlayableDomain(PlayableProfile.Default,813,false);owned.Add(d);Call(d,"AddCredits",PlayableOwner.Player,10000d);Build(d,PlayableBuildingKind.Factory,5);return d;}
        PlayableSnapshot View(PlayableDomain d)=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        object Raw(PlayableDomain d,int id)=>((IDictionary)typeof(PlayableDomain).GetField("buildings",F).GetValue(d))[id];
        SentryTurretState Weapon(PlayableDomain d,int id)=>(SentryTurretState)Raw(d,id).GetType().GetField("Weapon").GetValue(Raw(d,id));
        int Build(PlayableDomain d,PlayableBuildingKind kind,int slot){Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",1,slot,kind,1,PlayableOwner.Player,null));Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",100d);return View(d).Buildings.Single(b=>b.SlotId==slot).Id;}
        int Tower(PlayableDomain d){Build(d,PlayableBuildingKind.ScientificCenter,3);return Build(d,PlayableBuildingKind.GaussTower,1001);}
        int Enemy(PlayableDomain d,NavPoint at,PlayableEntityKind kind=PlayableEntityKind.Tank){int id=(int)Call(d,"SpawnUnit",at,PlayableOwner.Enemy,kind);if(kind!=PlayableEntityKind.Hawk)d.Navigation.Crowd.Stop(id,true);return id;}
        double Hp(PlayableDomain d,int id)=>View(d).Entities.Single(e=>e.Id==id).ExactHealth.Value;
        void Tick(PlayableDomain d,double dt){Call(d,"RefreshVision");Call(d,"AdvanceTurrets",dt);}
        [TearDown]public void Cleanup(){foreach(var d in owned){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);}owned.Clear();}
        [Test]public void ScienceMustBeOwnedAndReadyAndExistingTowerSurvivesLoss()
        {
            var d=New();Assert.AreEqual(PlayableCommandStatus.Rejected,Call(d,"BuildAt",1,1001,PlayableBuildingKind.GaussTower,1,PlayableOwner.Player,null));
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",1,3,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));
            Assert.AreEqual(PlayableCommandStatus.Rejected,Call(d,"BuildAt",1,1001,PlayableBuildingKind.GaussTower,1,PlayableOwner.Player,null));
            Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",100d);int tower=Build(d,PlayableBuildingKind.GaussTower,1001);
            int science=View(d).Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter).Id;Call(d,"Damage",science,100000);
            Assert.True(View(d).Buildings.Any(b=>b.Id==tower));Assert.AreEqual(PlayableCommandStatus.Rejected,Call(d,"BuildAt",1,1002,PlayableBuildingKind.GaussTower,1,PlayableOwner.Player,null));
            var at=View(d).Buildings.Single(b=>b.Id==tower).Position;int enemy=Enemy(d,new NavPoint(at.X,at.Z+8));Tick(d,1);Assert.Greater(Weapon(d,tower).ShotSequence,0);Assert.Less(Hp(d,enemy),PlayableProfile.Default.TankHealth);
        }
        [Test]public void EconomicsHealthRangeAndValidatedMetadataMatchAgreement()
        {
            var p=PlayableProfile.Default;Assert.AreEqual(2*TerritoryRules.Cost(p,PlayableBuildingKind.SentryTurret),TerritoryRules.Cost(p,PlayableBuildingKind.GaussTower));
            Assert.AreEqual(1.5*TerritoryRules.Health(p,PlayableBuildingKind.SentryTurret),TerritoryRules.Health(p,PlayableBuildingKind.GaussTower));
            Assert.Greater(p.GaussRange,p.ExplorerRange*p.TurretRangeMultiplier);Assert.Less(p.GaussRange,p.ShkvalBaseRange);Assert.AreEqual(4,p.GaussReloadSeconds);Assert.AreEqual(.7,p.GaussPierceRetention);
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="gauss.pierceRetention");var data=p.CopyData();NativeBalanceFields.Write(field,data,.6);CollectionAssert.AreNotEqual(WorldWire.Binding(p),WorldWire.Binding(PlayableProfile.Create(data)));Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(field,data,1.1));
        }
        [Test]public void OrderedPiercingAttenuatesAndDoesNotHitOffLineOrAir()
        {
            var d=New();int tower=Tower(d);var at=View(d).Buildings.Single(b=>b.Id==tower).Position;
            int a=Enemy(d,new NavPoint(at.X,at.Z+8));int b=Enemy(d,new NavPoint(at.X,at.Z+9.6));int c=Enemy(d,new NavPoint(at.X,at.Z+11.2));int off=Enemy(d,new NavPoint(at.X+3,at.Z+9));int air=Enemy(d,new NavPoint(at.X,at.Z+7),PlayableEntityKind.Hawk);
            double airHp=Hp(d,air);Tick(d,1);double damage=PlayableProfile.Default.TankHealth*.5;
            Assert.AreEqual(PlayableProfile.Default.TankHealth-damage,Hp(d,a),1e-8);Assert.AreEqual(PlayableProfile.Default.TankHealth-damage*.7,Hp(d,b),1e-8);Assert.AreEqual(PlayableProfile.Default.TankHealth-damage*.49,Hp(d,c),1e-8);Assert.AreEqual(PlayableProfile.Default.TankHealth,Hp(d,off));Assert.AreEqual(airHp,Hp(d,air));
        }
        [Test]public void FriendlyGroundBodyDoesNotStopTheBeamOrTakeDamage()
        {
            var d=New();int tower=Tower(d);var at=View(d).Buildings.Single(b=>b.Id==tower).Position;
            int enemy=Enemy(d,new NavPoint(at.X,at.Z+8));
            int ally=(int)Call(d,"SpawnUnit",new NavPoint(at.X,at.Z+5),PlayableOwner.Player,PlayableEntityKind.Tank);d.Navigation.Crowd.Stop(ally,true);
            Tick(d,1);Assert.AreEqual(1,Weapon(d,tower).ShotSequence);Assert.AreEqual(PlayableProfile.Default.TankHealth,Hp(d,ally));Assert.AreEqual(PlayableProfile.Default.TankHealth-PlayableProfile.Default.GaussDamage,Hp(d,enemy),1e-8);
            var shot=(GaussShotState)Raw(d,tower).GetType().GetField("Gauss").GetValue(Raw(d,tower));Assert.Greater(shot.End.Z,at.Z+8);CollectionAssert.AreEqual(new[]{enemy},shot.Targets);
        }
        [Test]public void TwoHitsKillTankAndTargetChangeCannotResetFourSecondCooldown()
        {
            var d=New();int tower=Tower(d);var at=View(d).Buildings.Single(b=>b.Id==tower).Position;int enemy=Enemy(d,new NavPoint(at.X,at.Z+8));Tick(d,1);
            Assert.AreEqual(PlayableProfile.Default.TankHealth*.5,Hp(d,enemy),1e-8);int shots=Weapon(d,tower).ShotSequence;
            int other=Enemy(d,new NavPoint(at.X+3,at.Z+7));Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(813,1,"player-1",PlayableCommandKind.Attack,new[]{tower},targetId:other),out _));
            Tick(d,3.9);Assert.AreEqual(shots,Weapon(d,tower).ShotSequence);
            Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(813,2,"player-1",PlayableCommandKind.Attack,new[]{tower},targetId:enemy),out _));Tick(d,.1);
            Assert.AreEqual(shots+1,Weapon(d,tower).ShotSequence);Assert.False(View(d).Entities.Any(e=>e.Id==enemy));
        }
        [Test]public void RetainsFirstTargetRejectsAirAndMovementAndChoosesNearestAfterLoss()
        {
            var d=New();int tower=Tower(d);var at=View(d).Buildings.Single(b=>b.Id==tower).Position;int first=Enemy(d,new NavPoint(at.X,at.Z+8));Tick(d,1);
            int nearer=Enemy(d,new NavPoint(at.X+3,at.Z+4));int air=Enemy(d,new NavPoint(at.X,at.Z+5),PlayableEntityKind.Hawk);Tick(d,.1);Assert.AreEqual(first,Weapon(d,tower).Target);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,d.Apply(new PlayableCommand(813,1,"player-1",PlayableCommandKind.Attack,new[]{tower},targetId:air),out _));
            Assert.AreEqual(PlayableCommandStatus.Rejected,d.Apply(new PlayableCommand(813,2,"player-1",PlayableCommandKind.Move,new[]{tower},new NavPoint(at.X+4,at.Z)),out _));
            Call(d,"Damage",first,100000);Tick(d,.1);Assert.AreEqual(nearer,Weapon(d,tower).Target);
        }
        [Test]public void SaveDuringImpulseAndReloadContinuesExactly()
        {
            var d=New();int tower=Tower(d);var at=View(d).Buildings.Single(b=>b.Id==tower).Position;Enemy(d,new NavPoint(at.X,at.Z+8));Tick(d,1);
            var bytes=d.CaptureWorldBytes(7,"gauss-current");var restored=PlayableDomain.RestoreWorldBytes(bytes,PlayableProfile.Default,7,"gauss-current");owned.Add(restored);CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"gauss-current"));
            for(int i=0;i<12;i++){d.Step(1d/30);restored.Step(1d/30);CollectionAssert.AreEqual(d.CaptureWorldBytes(7,"gauss-current"),restored.CaptureWorldBytes(7,"gauss-current"));}
        }
        [Test]public void DefensePlotAndGamepadBuildUnlockUseSameRules()
        {
            var d=New();Assert.False(OfflinePadWorldActions.Build(View(d),PlayableProfile.Default,1,1001,false).Single(a=>a.Sector.Id.EndsWith("GaussTower")).Sector.Enabled);
            Tower(d);Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Call(d,"BuildAt",1,1,PlayableBuildingKind.GaussTower,1,PlayableOwner.Player,null));
            var setup=new ProvingGroundSetup(PlayableProfile.Default);var site=setup.Profile.AuthoredMap.Sites(setup.Profile).First(s=>s.Id==1);Assert.IsNull(setup.PlaceBuilding(0,PlayableBuildingKind.GaussTower,site.DefenseSlots(setup.Profile)[0].Position));
        }
    }
}

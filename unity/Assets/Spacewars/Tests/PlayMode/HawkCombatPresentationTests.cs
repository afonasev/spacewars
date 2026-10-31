using System;
using System.Linq;
using System.Collections;
using UnityEngine.TestTools;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
public sealed class HawkCombatPresentationTests
{
    GameObject host;PlayableWorld world;PlayableProfile p;
    [SetUp] public void Setup(){p=PlayableProfile.Default;host=new GameObject("Hawk combat presentation");world=new PlayableWorld(host.transform,p,staged:true);}
    [TearDown] public void Cleanup(){world.Dispose();UnityEngine.Object.DestroyImmediate(host);}
    PlayableSnapshot Frame(long tick,PlayableSoundEvent[] events)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sounds:events,wrecks:events.Where(e=>e.Kind==PlayableSoundKind.Destroyed&&e.Weapon==PlayableEntityKind.Hawk).Select(e=>new PlayableWreckSnapshot(e.UnitId,e.Owner??PlayableOwner.Enemy,e.Weapon,e.Position,e.HullHeading,p.HawkRadius,e.Altitude,0,1,e.Tick,-1,e.Tick,e.Tick+60,e.Tick+60)).Where(w=>tick<w.ExpireTick).ToArray());
    [Test] public void HawkUpgradeAndShotArticulationPreserveSocketsAndFreezeAtSameTime()
    {
        var a=world.Tank(3,true,PlayableEntityKind.Hawk);
        var e=new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Hawk,default,60,false,0,0,0,upgraded:true,altitude:4,pitch:9,roll:-12);
        var facts=new[]{new PlayableSoundEvent(10,3,PlayableSoundKind.Shot,default,PlayableEntityKind.Hawk,unitId:3,shotSide:0)};
        world.UpdateAircraft(a,e,Frame(4,facts));
        var nodes=a.Hull.GetComponentsInChildren<Transform>(true);
        Assert.True(nodes.Single(t=>t.name=="upgradedOnly").gameObject.activeSelf);Assert.False(nodes.Single(t=>t.name=="baseOnly").gameObject.activeSelf);
        var socket=nodes.Single(t=>t.name=="muzzleLeft");var before=socket.localToWorldMatrix;
        var hatch=nodes.Single(t=>t.name=="reloadLeft");var moved=hatch.localPosition;
        world.UpdateAircraft(a,e,Frame(4,facts));Assert.AreEqual(moved,hatch.localPosition);Assert.AreEqual(before,socket.localToWorldMatrix);
        world.UpdateAircraft(a,e,Frame(30,facts));Assert.AreNotEqual(moved,hatch.localPosition);Assert.AreEqual(before,socket.localToWorldMatrix);
        var baseEntity=new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Hawk,default,60,false,0,0,0,altitude:4,pitch:9,roll:-12);
        world.UpdateAircraft(a,baseEntity,Frame(30,facts));Assert.True(nodes.Single(t=>t.name=="baseOnly").gameObject.activeSelf);Assert.AreEqual(before,socket.localToWorldMatrix);
    }
    [TestCase(false)][TestCase(true)] public void ActualExplorerGunPointsUpWhileCabinRemainsFixed(bool upgraded)
    {
        var a=world.Tank(1,true,PlayableEntityKind.Explorer);var e=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Explorer,default,70,false,2,0,0,upgraded:upgraded,weaponPitch:Math.PI/2);world.UpdateResearchModel(a,e);
        var cabin=a.Hull.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="landmark__explorer__forward-armored-cabin");var before=cabin.localToWorldMatrix;world.AimExplorer(a,e);
        Assert.AreEqual(before,cabin.localToWorldMatrix);Assert.Greater(Vector3.Dot(a.Launcher.forward,Vector3.up),.9999);Assert.True(a.Hull.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("muzzle")).All(t=>t.IsChildOf(a.Launcher)));
    }
    [TestCase(0d,0d,0d)] [TestCase(1.2d,8d,-12d)] [TestCase(-2.4d,-7d,14d)]
    public void BothMuzzleOriginsMatchRealPrefabAtEveryBodyPose(double heading,double pitch,double roll)
    {
        var actor=world.Tank(3,true,PlayableEntityKind.Hawk);
        var entity=new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Hawk,new NavPoint(2,-3),60,false,0,heading,heading,altitude:4,pitch:pitch,roll:roll);
        world.UpdateAircraft(actor,entity,Frame(0,Array.Empty<PlayableSoundEvent>()));
        for(int side=0;side<2;side++)
        {
            var socket=actor.Hull.GetComponentsInChildren<Transform>(true).Single(t=>t.name==(side==0?"muzzleLeft":"muzzleRight"));
            var start=HawkWeaponGeometry.Muzzle(entity.Position,entity.Altitude,heading,pitch,roll,p.HawkModelScale,side);
            Assert.Less(Vector3.Distance(socket.position,new Vector3((float)start.X,(float)start.Y,(float)start.Z)),.00001f,"Simulation launch must match the real transformed weapon socket");
        }
    }
    [UnityTest] public IEnumerator AirDeathHasNoGroundWreckColliderOrGroundContactAndPauseDoesNotAgeIt()
    {
        var death=new[]{new PlayableSoundEvent(1,0,PlayableSoundKind.Destroyed,default,PlayableEntityKind.Hawk,unitId:9,altitude:.3,owner:PlayableOwner.Enemy)};world.RenderUnitWrecks(Frame(0,death));Assert.Zero(world.UnitWreckCount);Assert.AreEqual(1,world.AirDestructionCount);
        var debris=host.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Air fragment").ToArray();Assert.AreEqual((int)p.HawkDebrisCount,debris.Length);Assert.False(host.GetComponentsInChildren<Collider>(true).Any(c=>c.enabled));yield return null;Assert.Zero(host.GetComponentsInChildren<Collider>(true).Length);
        world.RenderUnitWrecks(Frame(3,death));var frozen=debris.Select(t=>t.localToWorldMatrix).ToArray();world.RenderUnitWrecks(Frame(3,death));CollectionAssert.AreEqual(frozen,debris.Select(t=>t.localToWorldMatrix).ToArray());
        for(int tick=4;tick<=Math.Ceiling(p.HawkDebrisSeconds*30);tick++){world.RenderUnitWrecks(Frame(tick,death));foreach(var fragment in debris)if(fragment&&fragment.gameObject.activeInHierarchy)Assert.Greater(fragment.position.y,p.HawkMissileRadius);}
        Assert.Zero(world.AirDestructionCount);Assert.Zero(world.UnitWreckCount);
    }
}

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
    PlayableSnapshot Frame(long tick,PlayableSoundEvent[] events)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sounds:events);
    [TestCase(false)][TestCase(true)] public void ActualExplorerGunPointsUpWhileCabinRemainsFixed(bool upgraded)
    {
        var a=world.Tank(1,true,PlayableEntityKind.Explorer);var e=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Explorer,default,70,false,2,0,0,upgraded:upgraded,weaponPitch:Math.PI/2);world.UpdateResearchModel(a,e);
        var cabin=a.Hull.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="landmark__explorer__forward-armored-cabin");var before=cabin.localToWorldMatrix;world.AimExplorer(a,e);
        Assert.AreEqual(before,cabin.localToWorldMatrix);Assert.Greater(Vector3.Dot(a.Launcher.forward,Vector3.up),.9999);Assert.True(a.Hull.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("muzzle")).All(t=>t.IsChildOf(a.Launcher)));
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

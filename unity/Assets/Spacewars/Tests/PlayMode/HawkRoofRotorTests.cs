using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Tests.PlayMode
{
    public sealed class HawkRoofRotorTests
    {
        [Test] public void PetalsNeverExtendOutsideApertureAtAnyOpeningAndRestoreExactly()
        {
            var prefab=Resources.Load<GameObject>("StyleA/hangar");var root=UnityEngine.Object.Instantiate(prefab);
            try{foreach(var t in root.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("roofPetal"))){
                int blade=int.Parse(t.name.Substring(9));var filter=t.GetComponentInChildren<MeshFilter>();var original=filter.sharedMesh.vertices;var shutter=t.gameObject.AddComponent<HangarRetractingPetal>();
                for(int step=0;step<=20;step++){shutter.SetOpen(step/20f,blade);foreach(var v in filter.sharedMesh.vertices)Assert.LessOrEqual(new Vector2(v.x,v.z).magnitude,.96501f);}
                Assert.False(t.GetComponentInChildren<Renderer>().enabled);shutter.SetOpen(0,blade);CollectionAssert.AreEqual(original,filter.sharedMesh.vertices);Assert.True(t.GetComponentInChildren<Renderer>().enabled);
            }}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void HawkRestsOnPlatformDuringOpeningAndRiseAndMatchesFlightOrigin()
        {
            var root=new GameObject("hangar-check");var profile=PlayableProfile.Default;
            var world=new PlayableWorld(root.transform,profile,staged:true);
            try
            {
                var actor=world.Building(1,"Hangar",true);world.FaceBuilding(actor);
                foreach(var phase in new[]{1,2,3})
                {
                    var b=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Hangar,default,100,1,1,1,default,launchPhase:phase,launchTime:phase==2?profile.HawkPlatformSeconds/2:0);
                    world.UpdateHangar(actor,b,0);var hawk=actor.Hull.Find("stagedHawk");Assert.True(hawk.gameObject.activeSelf);
                    var platform=actor.Hull.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="launchPlatform").GetComponentInChildren<Renderer>();
                    float sole=hawk.GetComponentsInChildren<Renderer>().Min(r=>r.bounds.min.y);
                    Assert.AreEqual(platform.bounds.max.y,sole,.001,"Hawk must stand on the moving platform");
                    if(phase==3)Assert.AreEqual(HawkHangarGeometry.LaunchHeight(profile),hawk.position.y,.001);
                }
            }
            finally{world.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void OpenShaftHasNoBodyCapAboveTheBottomPlatform()
        {
            var prefab=Resources.Load<GameObject>("StyleA/hangar");
            foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.transform.parent.name=="modelRoot"))
                foreach(var v in filter.sharedMesh.vertices.Where(v=>v.y>HawkHangarGeometry.RestPlatformTop))
                    Assert.GreaterOrEqual(new Vector2(v.x,v.z).magnitude,.9399f,"Body fills the elevator shaft: "+filter.name);
            Assert.Less(HawkHangarGeometry.RaisedPlatformTop,1.68*1.25/1.754);
        }
        [Test] public void BothStagedRotorsAdvanceBetweenAuthorityTicksAndFreezeWithRenderClock()
        {
            var root=new GameObject("staged");var main=new GameObject("mainRotor").transform;main.SetParent(root.transform);var tail=new GameObject("tailRotor").transform;tail.SetParent(root.transform);
            try{var rotor=root.AddComponent<HawkStagedRotor>();rotor.Render(3,1,0,2160);rotor.Render(3,1,1d/120,2160);Assert.AreEqual(18,main.localEulerAngles.y,.001);Assert.AreEqual(18,tail.localEulerAngles.x,.001);rotor.Render(3,1,1d/120,2160);Assert.AreEqual(18,main.localEulerAngles.y,.001);rotor.Render(2,0,1,2160);Assert.AreEqual(0,main.localEulerAngles.y,.001);}finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}

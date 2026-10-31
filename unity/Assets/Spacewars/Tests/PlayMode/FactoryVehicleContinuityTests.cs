using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Spacewars.Tests.PlayMode
{
    public sealed class FactoryVehicleContinuityTests
    {
        static string PathFrom(Transform root,Transform child)
        {var names=new List<string>();while(child!=root){names.Add(child.name);child=child.parent;}names.Reverse();return string.Join("/",names);}
        static Dictionary<string,Mesh> Meshes(Transform root)=>root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name!="Observed muzzle flash").ToDictionary(f=>PathFrom(root,f.transform),f=>f.sharedMesh);
        static void Capture(Camera camera,RenderTexture target,string name)
        {
            var output=Environment.GetEnvironmentVariable("SPACEWARS_FACTORY_CONTINUITY_OUTPUT");if(string.IsNullOrEmpty(output))return;
            Directory.CreateDirectory(output);camera.Render();var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(System.IO.Path.Combine(output,name+".png"),image.EncodeToPNG());}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(image);}
        }
        [UnityTest,Timeout(65000)] public IEnumerator ActualFactoryRolloutMovesAndHandsOffTheSameVisibleTankRig()
        {
            var p=PlayableProfile.Default;var domain=new PlayableDomain(p,81,false);CertifiedRouteWorker worker=null;
            var host=new GameObject("Actual rollout continuity");var camera=new GameObject("Rollout camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=9.6f;
            var target=new RenderTexture(960,600,24){antiAliasing=4};target.Create();camera.targetTexture=target;
            var sun=new GameObject("Rollout sun").AddComponent<Light>();sun.transform.SetParent(host.transform);IndustrialLightingProfile.Load().ApplyScene(sun);
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(domain,args);
            PlayableSnapshot View()=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            try{
                Call("AddCredits",PlayableOwner.Player,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
                var initial=View();var b=initial.Buildings.Single(x=>x.Kind==PlayableBuildingKind.Factory);var original=new HashSet<int>(initial.Entities.Select(e=>e.Id));
                var authority=new PlayableAuthorityTick(domain,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(81,1,"player-1",PlayableCommandKind.QueueTank,new[]{b.Id})).Status);
                using(var world=new PlayableWorld(host.transform,p)){
                    world.InspectionReveal();var factory=world.Building(b.Id,"Factory",true);factory.Root.position=world.Point(b.Position);factory.Root.rotation=Quaternion.Euler(0,-(float)b.Heading*Mathf.Rad2Deg,0);
                    var exit=FactoryReleaseRules.Exit(p,b.Position,b.Heading,PlayableEntityKind.Tank);var center=world.Point(exit);
                    camera.transform.position=center+new Vector3(0,15,-22);camera.transform.LookAt(center);
                    bool stagedMoved=false,releasedMoved=false;Vector3 previous=default;bool hadStaged=false;Dictionary<string,Mesh> meshes=null;float scale=0;int captures=0;
                    var clock=System.Diagnostics.Stopwatch.StartNew();PlayableWorld.Actor released=null;NavPoint releasePoint=default;
                    while(domain.Tick<1800&&clock.Elapsed.TotalSeconds<55){
                        if(!authority.TryAdvance()){System.Threading.Thread.Sleep(1);continue;}Assert.IsNull(worker.Publication.Failure);var v=View();b=v.Buildings.Single(x=>x.Id==b.Id);
                        factory.Root.position=world.Point(b.Position);factory.Root.rotation=Quaternion.Euler(0,-(float)b.Heading*Mathf.Rad2Deg,0);world.UpdateFactoryModel(factory,b,v);world.FaceBuilding(factory,camera);PlayableWorld.UpdateHealth(factory,camera,1);
                        if(b.LaunchPhase==FactoryReleaseRules.Driving){
                            var at=factory.FactoryStaged.position;if(hadStaged){stagedMoved|=(at-previous).sqrMagnitude>1e-8f;Assert.LessOrEqual(Vector3.Distance(at,previous),(float)(p.FactoryRolloutSpeed/30d)+.01f);}
                            previous=at;hadStaged=true;meshes=Meshes(factory.FactoryStaged);scale=factory.FactoryStaged.localScale.x;Assert.AreEqual(Vector3.one,factory.Root.localScale);
                            double fraction=b.LaunchTime/FactoryReleaseRules.Duration(p,PlayableEntityKind.Tank);
                            if(captures==0||captures==1&&fraction>=.5||captures==2&&fraction>=.95){Capture(camera,target,"actual-rollout-"+captures+"-normal");if(captures==2){camera.orthographicSize=2.135f;PlayableWorld.UpdateHealth(factory,camera,1);Capture(camera,target,"actual-rollout-last-staged-close");camera.orthographicSize=9.6f;PlayableWorld.UpdateHealth(factory,camera,1);}captures++;yield return null;}
                        }
                        var unit=v.Entities.FirstOrDefault(e=>e.Kind==PlayableEntityKind.Tank&&!original.Contains(e.Id));
                        if(unit==null)continue;
                        if(released==null){
                            Assert.True(hadStaged&&stagedMoved);releasePoint=unit.Position;released=world.Tank(unit.Id,true);world.UpdateResearchModel(released,unit);released.Root.position=world.Point(unit.Position);released.Hull.localRotation=Quaternion.Euler(0,90-(float)unit.HullHeading*Mathf.Rad2Deg,0);
                            PlayableWorld.UpdateHealth(released,camera,1);CollectionAssert.AreEquivalent(meshes.Keys,Meshes(released.Hull).Keys);foreach(var mesh in meshes)Assert.AreSame(mesh.Value,Meshes(released.Hull)[mesh.Key]);Assert.AreEqual(Vector3.one,released.Root.localScale);Assert.AreEqual(scale,released.Hull.localScale.x,"Exact authored scale under unit-scale parents; lossyScale decomposes the two different rotation hierarchies with float rounding");
                            Assert.LessOrEqual(Vector3.Distance(previous,released.Root.position),(float)(p.FactoryRolloutSpeed/30d)+.01f,"Last staged pose to first authority actor is one normal rollout step");
                            Assert.False(factory.FactoryStaged.gameObject.activeSelf);Capture(camera,target,"actual-rollout-first-released-normal");camera.orthographicSize=2.135f;PlayableWorld.UpdateHealth(released,camera,1);Capture(camera,target,"actual-rollout-first-released-close");camera.orthographicSize=9.6f;PlayableWorld.UpdateHealth(released,camera,1);yield return null;
                            Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(81,2,"player-1",PlayableCommandKind.Move,new[]{unit.Id},new NavPoint(exit.X+Math.Cos(b.Heading)*6,exit.Z+Math.Sin(b.Heading)*6))).Status);
                        }
                        released.Root.position=world.Point(unit.Position);released.Hull.localRotation=Quaternion.Euler(0,90-(float)unit.HullHeading*Mathf.Rad2Deg,0);released.TankMechanism?.Drive(new PlayableDisplayPose(unit.Position,unit.HullHeading,unit.TurretHeading),v,unit.Id);
                        if(!unit.Position.Equals(releasePoint)){releasedMoved=true;Capture(camera,target,"actual-rollout-released-moving-normal");break;}
                        System.Threading.Thread.Sleep(1);
                    }
                    Assert.True(stagedMoved&&releasedMoved&&captures==3,"Actual production, staged motion, release and commanded motion must occur");TestContext.WriteLine($"FACTORY_HANDOFF seed7 tick={domain.Tick} captures={captures} pumps={worker.Publication.Pumps}");
                }
            }finally{worker?.Dispose();if(worker!=null)Assert.True(System.Threading.SpinWait.SpinUntil(()=>worker.IsStopped,5000));domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(host);}
        }
        [UnityTest] public IEnumerator StagedAndReleasedTanksUseSameMeshesScaleAndCameraLod()
        {
            var host=new GameObject("Factory continuity");var camera=new GameObject("Matched camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.transform.position=new Vector3(0,26,-37);camera.transform.LookAt(Vector3.zero);
            var target=new RenderTexture(960,600,24){antiAliasing=4};target.Create();camera.targetTexture=target;
            var sun=new GameObject("Matched sun").AddComponent<Light>();sun.transform.SetParent(host.transform);IndustrialLightingProfile.Load().ApplyScene(sun);
            try{
                foreach(bool hybrid in new[]{true,false})foreach(bool upgraded in new[]{false,true}){
                    using(var world=new PlayableWorld(host.transform,PlayableProfile.Default)){
                        world.HybridTanks=hybrid;world.InspectionReveal();var factory=world.Building(10,"Factory",true);factory.Root.position=new Vector3(-9,0,0);
                        var profile=PlayableProfile.Default;double time=FactoryReleaseRules.ExitDistance(profile,PlayableEntityKind.Tank)/profile.FactoryRolloutSpeed;
                        var building=new PlayableBuildingSnapshot(10,PlayableOwner.Player,PlayableBuildingKind.Factory,default,100,1,0,0,default,launchPhase:FactoryReleaseRules.Driving,launchTime:time,launchKind:PlayableEntityKind.Tank,launchUpgraded:upgraded);
                        var view=new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,30,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),new[]{building},Array.Empty<PlayableProjectileSnapshot>(),null,null);
                        world.UpdateFactoryModel(factory,building,view);var staged=factory.FactoryStaged;Assert.NotNull(staged);
                        var released=world.Tank(20,true);released.Root.position=staged.position+Vector3.right*8;
                        if(upgraded)world.UpdateResearchModel(released,new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Tank,default,100,false,0,0,0,upgraded:true));
                        released.Hull.localRotation=staged.rotation;
                        foreach(float zoom in new[]{9.6f,2.135f,25.6f}){
                            camera.orthographicSize=zoom;PlayableWorld.UpdateHealth(factory,camera,1);PlayableWorld.UpdateHealth(released,camera,1);yield return null;
                            Capture(camera,target,$"hybrid-{hybrid}-upgrade-{upgraded}-zoom-{zoom}");
                            var expected=Meshes(released.Hull);var actual=Meshes(staged);CollectionAssert.AreEquivalent(expected.Keys,actual.Keys,"Factory must use the actual released vehicle rig, not another tank model");
                            foreach(var entry in expected)Assert.AreSame(entry.Value,actual[entry.Key],"Matching mesh at "+entry.Key+" zoom="+zoom);
                            Assert.That((staged.lossyScale-released.Hull.lossyScale).sqrMagnitude,Is.LessThan(1e-10f),"Identical model/profile scaling across release");
                            Assert.AreEqual(released.DetailLod?released.DetailLod.Level:0,staged.GetComponent<ModelDetailLod>()?staged.GetComponent<ModelDetailLod>().Level:0,"Same per-camera LOD tier");
                        }
                    }
                    yield return null;
                }
            }finally{camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(host);}
        }
    }
}

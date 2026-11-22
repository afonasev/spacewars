using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class NewVehicleWreckTrailTests
    {
        static PlayableSnapshot Frame(PlayableProfile p,long tick,PlayableWreckSnapshot[] wrecks=null,PlayableGroundTrail[] trails=null)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,wrecks:wrecks,groundTrails:trails);
        [UnityTest] public IEnumerator AllNewWreckVariantsHaveReducedTiersAndCharredMaterials()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_NEW_VEHICLE_EVIDENCE")??Path.Combine(Application.temporaryCachePath,"new-vehicle-wrecks");Directory.CreateDirectory(output);
            var host=new GameObject("New vehicle wreck capture");host.layer=27;var p=PlayableProfile.Default;var world=new PlayableWorld(host.transform,p);world.SetPresentationLayer(27);world.Fog.ShowPublic();
            var camera=new GameObject("camera").AddComponent<Camera>();camera.cullingMask=1<<27;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.18f,.20f);camera.transform.position=new Vector3(3,4,-5);camera.transform.LookAt(new Vector3(0,.15f,0));
            var light=new GameObject("sun").AddComponent<Light>();IndustrialLightingProfile.Load("three-crossings").ApplyScene(light);light.cullingMask=1<<27;
            var target=new RenderTexture(640,480,24);target.Create();camera.targetTexture=target;var pixels=new Texture2D(640,480,TextureFormat.RGB24,false);bool enabled=NativeGraphicsSettings.UnitWrecksEnabled;
            try{
                NativeGraphicsSettings.UnitWrecksEnabled=true;
                foreach(var kind in new[]{PlayableEntityKind.Skat,PlayableEntityKind.Cerberus,PlayableEntityKind.Igla})for(int variant=1;variant<=3;variant++){
                    var w=new PlayableWreckSnapshot(41,PlayableOwner.Player,kind,default,Math.PI/2,1,0,.04,variant,0,5,5,905,965);
                    world.RenderUnitWrecks(Frame(p,200,new[]{w}));
                    var model=host.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Unit wreck 41").gameObject;
                    var lod=model.GetComponent<ModelDetailLod>();Assert.NotNull(lod);Assert.Greater(lod.ReferenceBounds.size.sqrMagnitude,0);
                    Assert.IsEmpty(model.GetComponentsInChildren<Collider>(true));Assert.IsEmpty(model.GetComponentsInChildren<Rigidbody>(true));
                    foreach(var r in model.GetComponentsInChildren<MeshRenderer>(true))foreach(var m in r.sharedMaterials){Assert.NotNull(m);Assert.False(m.IsKeywordEnabled("_EMISSION"));}
                    camera.orthographicSize=kind==PlayableEntityKind.Cerberus?2.1f:kind==PlayableEntityKind.Igla?1.3f:.8f;
                    int prior=int.MaxValue;
                    for(int tier=0;tier<3;tier++){
                        lod.SetDetail(tier);yield return null;
                        int triangles=model.GetComponentsInChildren<MeshFilter>().Where(f=>f.gameObject.activeInHierarchy).Sum(f=>f.sharedMesh.triangles.Length/3);
                        Assert.Greater(triangles,0);Assert.Less(triangles,prior,"Every tier must reduce the source");prior=triangles;
                        camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,640,480),0,0);pixels.Apply();RenderTexture.active=previous;
                        File.WriteAllBytes(Path.Combine(output,kind+"-v"+variant+"-lod"+tier+".png"),pixels.EncodeToPNG());
                    }
                    if(kind==PlayableEntityKind.Skat){
                        var nose=Vector3.ProjectOnPlane(model.transform.TransformDirection(Vector3.back),Vector3.up).normalized;
                        Assert.Greater(Vector3.Dot(nose,Vector3.forward),.99f,"The falling and grounded model use the same nose as the live aircraft.");
                        lod.SetDetail(0);
                        var buried=new PlayableWreckSnapshot(41,PlayableOwner.Player,kind,default,Math.PI/2,1,0,p.WreckBurialMax,variant,0,5,5,905,965);
                        world.RenderUnitWrecks(Frame(p,200,new[]{buried}));
                        Assert.Greater(model.GetComponentsInChildren<MeshRenderer>().Max(r=>r.bounds.max.y),lod.ReferenceBounds.extents.y*model.transform.localScale.y-.001f,"The thin wing must retain half its height even at maximum burial");
                    }
                    // Retire the instance so every structural variant is instantiated.
                    world.RenderUnitWrecks(Frame(p,201,Array.Empty<PlayableWreckSnapshot>()));yield return null;
                }
            }finally{NativeGraphicsSettings.UnitWrecksEnabled=enabled;world.Dispose();camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(light.gameObject);}
        }
        [TestCase(PlayableEntityKind.Cerberus)] [TestCase(PlayableEntityKind.Igla)]
        public void GroundTracksUseActualGaugeWidthAndCaterpillarTread(PlayableEntityKind kind)
        {
            var host=new GameObject("Track geometry");var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap());var world=new PlayableWorld(host.transform,p,staged:true);bool enabled=NativeGraphicsSettings.GroundTrailsEnabled;
            try{
                NativeGraphicsSettings.GroundTrailsEnabled=true;world.Fog.ShowPublic();
                world.RenderGroundTrails(Frame(p,5,trails:new[]{new PlayableGroundTrail(new NavPoint(0,-19),new NavPoint(2,-19),0,0,3,kind)}));
                Assert.Greater(world.GroundTrailQuadCount,0);
                var mesh=(Mesh)typeof(PlayableWorld).GetField("trailMesh",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
                float scale=(float)(kind==PlayableEntityKind.Cerberus?PlayableUnitRules.CerberusModelScale(p):PlayableUnitRules.IglaModelScale);
                float half=kind==PlayableEntityKind.Cerberus?1.17f+.64f/2:.64f+.322f/2;
                Assert.AreEqual(2*half*scale,mesh.bounds.size.z,.001);
                var uv=new System.Collections.Generic.List<Vector4>();mesh.GetUVs(1,uv);Assert.IsNotEmpty(uv);Assert.True(uv.All(v=>v.y==0),"Tracked vehicles use the caterpillar tread");
            }finally{NativeGraphicsSettings.GroundTrailsEnabled=enabled;world.Dispose();UnityEngine.Object.DestroyImmediate(host);}
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingsAtmosphereProductionTests
    {
        static ThreeCrossingsMap Map()=>new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
        [Test] public void SelectedMapResourceIsIsolatedAndEveryControlValidates()
        {
            var selected=IndustrialLightingProfile.Load(Map().Id);var current=IndustrialLightingProfile.Load();
            Assert.AreEqual("three-crossings-warm-v1",selected.id);Assert.AreEqual("industrial-warm-cool-v1",current.id);
            Assert.AreEqual(1,selected.revision);Assert.AreEqual(32,selected.sunElevation);Assert.AreEqual(-55,selected.sunAzimuth);Assert.AreEqual(.27f,selected.sunSaturation);Assert.AreEqual(1.45f,selected.sunIntensity);Assert.AreEqual(.28f,selected.ambientSaturation);Assert.AreEqual(.5f,selected.ambientValue);Assert.AreEqual(.7f,selected.shadowStrength);
            Assert.AreEqual(JsonUtility.ToJson(current),JsonUtility.ToJson(IndustrialLightingProfile.Load("foundry-3v3")));Assert.AreEqual(JsonUtility.ToJson(current),JsonUtility.ToJson(IndustrialLightingProfile.Load(null)));
            foreach(var f in selected.Parameters())
            {
                Assert.That(f.Path,Does.StartWith("visual.threeCrossings.lighting."));Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);
                float value=f.Read();foreach(float bad in new[]{float.NaN,float.PositiveInfinity,f.Minimum-f.Step,f.Maximum+f.Step}){f.Write(bad);Assert.Throws<ArgumentException>(()=>selected.Validate());}f.Write(value);
            }
            var surfaces=ThreeCrossingsSurfaceProfile.Load();
            foreach(var f in surfaces.Parameters().Where(f=>f.Group=="Service scenery"))
            {
                Assert.IsNotEmpty(f.Description);Assert.Greater(f.Step,0);float value=f.Read();foreach(float bad in new[]{float.NaN,float.PositiveInfinity,f.Minimum-f.Step,f.Maximum+f.Step}){f.Write(bad);Assert.Throws<ArgumentException>(()=>surfaces.Validate());}f.Write(value);
            }
            Assert.AreEqual(4,surfaces.Parameters().Count(f=>f.Group=="Service scenery"));surfaces.serviceBayCount=.5f;Assert.Throws<ArgumentException>(()=>surfaces.Validate());
        }
        static Texture2D Capture(RenderTexture target)
        {
            var previous=RenderTexture.active;try{RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();return pixels;}finally{RenderTexture.active=previous;}
        }
        [UnityTest] public IEnumerator RealBootstrapRendersSelectedCompositionAndConcealsItWithoutShadowLeaks()
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            string output=Environment.GetEnvironmentVariable("SPACEWARS_ATMOSPHERE_EVIDENCE");if(!string.IsNullOrEmpty(output))Directory.CreateDirectory(output);
            var oldAmbient=RenderSettings.ambientLight;var oldMode=RenderSettings.ambientMode;float oldVolume=AudioListener.volume;AudioListener.volume=0;
            string shadowKey=NativeDisplaySettings.Prefix+"shadows";bool hadPreference=PlayerPrefs.HasKey(shadowKey);int preference=PlayerPrefs.GetInt(shadowKey,1);NativeDisplaySettings.Shadows=true;
            var pipeline=GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;float distance=pipeline.shadowDistance;
            var host=new GameObject("Production Crossings bootstrap art QA");var bootstrap=host.AddComponent<PlayableBootstrap>();bootstrap.enabled=false;
            var map=Map();var gameplay=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var target=new RenderTexture(1600,1000,24);target.Create();Camera camera=null;Texture2D hiddenOn=null,hiddenOff=null;
            try
            {
                typeof(PlayableBootstrap).GetField("profile",flags).SetValue(bootstrap,gameplay);typeof(PlayableBootstrap).GetMethod("CreateWorld",flags).Invoke(bootstrap,null);
                var world=(PlayableWorld)typeof(PlayableBootstrap).GetField("world",flags).GetValue(bootstrap);camera=(Camera)typeof(PlayableBootstrap).GetField("cameraView",flags).GetValue(bootstrap);camera.targetTexture=target;
                var lighting=IndustrialLightingProfile.Load(map.Id);var sun=host.GetComponentsInChildren<Light>().Single(l=>l.type==LightType.Directional);
                Assert.AreEqual(lighting.sunIntensity,sun.intensity);Assert.AreEqual(Color.HSVToRGB(lighting.sunHue,lighting.sunSaturation,1),sun.color);Assert.AreEqual(Quaternion.Euler(lighting.sunElevation,lighting.sunAzimuth,0),sun.transform.rotation);Assert.NotNull(camera.GetComponent<VisibleShadowCoverage>());
                NativeDisplaySettings.Shadows=false;Assert.AreEqual(LightShadows.None,sun.shadows);NativeDisplaySettings.Shadows=true;Assert.AreEqual(LightShadows.Soft,sun.shadows);
                var equipment=host.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="Detail — parked service equipment").ToArray();Assert.AreEqual(1,equipment.Length,"Selected one service composition is part of production world");
                Assert.Zero(equipment.Sum(r=>r.GetComponentsInChildren<Collider>().Length));Assert.Greater(equipment[0].GetComponent<MeshFilter>().sharedMesh.colors.Distinct().Count(),2,"Authored neutral materials retained");
                var diagnostic=new Material(Resources.Load<Shader>("TerritoryFog"));
                try{using(var surfaces=new ThreeCrossingsMaterials(ThreeCrossingsSurfaceProfile.Load(),map,diagnostic,gameplay))
                {
                    foreach(var renderer in equipment)
                    {
                        var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;var bounds=mesh.bounds;
                        Assert.IsTrue(ThreeCrossingsServiceScenery.VisibleFromGameplay(map,gameplay,bounds,renderer.transform.position,renderer.transform.localScale.x,renderer.transform.eulerAngles.y),"Terrain cannot mask the selected equipment silhouette from the normal camera");
                        var reserve=ThreeCrossingsServiceScenery.Envelope(bounds,renderer.transform.position,renderer.transform.localScale.x,.6f,gameplay,renderer.transform.eulerAngles.y);
                        var scree=host.GetComponentsInChildren<MeshFilter>().Single(f=>f.name=="Detail — geological debris");
                        foreach(var vertex in scree.sharedMesh.vertices){var q=scree.transform.TransformPoint(vertex);Assert.IsFalse(reserve.Contains(new NavPoint(q.x,q.z)),"Remaining scree must not intersect the full service reserve");}
                        var physical=ThreeCrossingsDetails.CompoundEnvelope(bounds,renderer.transform.position,renderer.transform.localScale.x,0,null,renderer.transform.eulerAngles.y);
                        var projected=ThreeCrossingsDetails.CompoundEnvelope(bounds,renderer.transform.position,renderer.transform.localScale.x,0,gameplay,renderer.transform.eulerAngles.y);
                        for(double x=Math.Min(physical.MinX,projected.MinX);x<=Math.Max(physical.MaxX,projected.MaxX);x+=.1)for(double z=Math.Min(physical.MinZ,projected.MinZ);z<=Math.Max(physical.MaxZ,projected.MaxZ);z+=.1)
                        {
                            var point=new NavPoint(x,z);Assert.IsTrue(map.SupportsFootprint(point,.3f+surfaces.Art.detailClearance),"Full screen projection remains supported at original clearance");
                            float u=(float)(point.X/(map.HalfExtent*2)+.5),v=(float)(point.Z/(map.HalfExtent*2)+.5);Assert.LessOrEqual(surfaces.RoadMask.GetPixelBilinear(u,v).r,.015f);Assert.LessOrEqual(surfaces.ConstructionMask.GetPixelBilinear(u,v).g,.01f);
                        }
                        // Independent fine sample of the entire local rectangle, not just vertices/anchor.
                        for(float x=bounds.min.x;x<=bounds.max.x+.01f;x+=.1f)for(float z=bounds.min.z;z<=bounds.max.z+.01f;z+=.1f)
                        {
                            var q=renderer.transform.TransformPoint(new Vector3(x,0,z));var point=new NavPoint(q.x,q.z);Assert.IsTrue(map.SupportsFootprint(point,.3f+surfaces.Art.detailClearance));Assert.AreEqual(renderer.transform.position.y,(float)map.SurfaceHeight(point),.05f);
                            float u=(float)(point.X/(map.HalfExtent*2)+.5),v=(float)(point.Z/(map.HalfExtent*2)+.5);Assert.LessOrEqual(surfaces.RoadMask.GetPixelBilinear(u,v).r,.015f);Assert.LessOrEqual(surfaces.ConstructionMask.GetPixelBilinear(u,v).g,.01f);
                        }
                    }
                }
                }finally{UnityEngine.Object.Destroy(diagnostic);}
                world.InspectionReveal();var actors=new List<PlayableWorld.Actor>();
                foreach(var site in TerritoryRules.Sites(gameplay).Where(s=>s.Kind==PlayableBuildingKind.Mine))
                {var actor=world.Building(16000+site.Id,"Mine",false);actor.Root.position=world.Point(site.Position);actor.Health.parent.gameObject.SetActive(false);actor.Selection.SetActive(false);PlayableWorld.PaintOwner(actor,new Color(.55f,.57f,.53f));actors.Add(actor);}
                var hq=world.Building(18000,"Headquarters",true);hq.Root.position=world.Point(TerritoryRules.Sites(gameplay)[0].Position);hq.Health.parent.gameObject.SetActive(false);hq.Selection.SetActive(false);PlayableWorld.PaintOwner(hq,new Color(.3f,.55f,.85f));actors.Add(hq);
                var kitPosition=equipment[0].transform.position;
                var captures=new List<(string name,Vector3 focus,Vector3 offset,float ortho)> {
                    ("gameplay",hq.Root.position,new Vector3((float)gameplay.CameraOffsetX,(float)gameplay.CameraHeight,(float)gameplay.CameraOffsetZ),(float)gameplay.CameraOrthoSize),
                    ("overview",Vector3.zero,new Vector3(0,135,-72),72),
                    ("service-normal",kitPosition,new Vector3((float)gameplay.CameraOffsetX,(float)gameplay.CameraHeight,(float)gameplay.CameraOffsetZ),(float)gameplay.CameraOrthoSize),
                    ("service-close",kitPosition+Vector3.up*.5f,new Vector3(4,6.5f,-6),0),
                    ("bridge",Vector3.zero,new Vector3(3,14,-11),0)};
                foreach(var mine in actors.Take(6))captures.Add(("mine-"+captures.Count,mine.Root.position,new Vector3(0,16,-11),0));
                foreach(var shot in captures)
                {
                    camera.orthographic=shot.ortho>0;camera.orthographicSize=shot.ortho>0?shot.ortho:8;camera.fieldOfView=48;camera.transform.position=shot.focus+shot.offset;camera.transform.LookAt(shot.focus);
                    foreach(var actor in actors)world.FaceBuilding(actor,camera);
                    for(int i=0;i<6;i++)yield return null;var pixels=Capture(target);
                    try{var colors=pixels.GetPixels32();Assert.Greater(colors.Count(c=>Mathf.Max(c.r,Mathf.Max(c.g,c.b))>45),100000);Assert.Zero(colors.Count(c=>c.r>200&&c.b>200&&c.g<40));if(!string.IsNullOrEmpty(output))File.WriteAllBytes(Path.Combine(output,shot.name+".png"),pixels.EncodeToPNG());}finally{UnityEngine.Object.Destroy(pixels);}
                }
                camera.orthographic=false;camera.transform.position=kitPosition+new Vector3(4,7,-6);camera.transform.LookAt(kitPosition+Vector3.up*.5f);
                world.InspectionHide();foreach(var actor in actors)actor.Root.gameObject.SetActive(false);
                for(int i=0;i<6;i++)yield return null;hiddenOn=Capture(target);foreach(var r in equipment)r.enabled=false;
                for(int i=0;i<6;i++)yield return null;hiddenOff=Capture(target);CollectionAssert.AreEqual(hiddenOff.GetPixels32(),hiddenOn.GetPixels32(),"Concealed service scenery and shadows cannot leak silhouettes");
                if(!string.IsNullOrEmpty(output)){File.WriteAllBytes(Path.Combine(output,"fog-service.png"),hiddenOn.EncodeToPNG());File.WriteAllText(Path.Combine(output,"profiles.txt"),map.Id+"@"+map.Revision+"\n"+world.SurfaceRevision+"\n"+lighting.id+"@"+lighting.revision+"\nProduction CreateWorld and camera; diagnostic real HQ/mine actors, no running match/seed; Editor RenderTexture, no Player/device acceptance.\n");}
            }
            finally
            {
                if(camera)camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);if(hiddenOn)UnityEngine.Object.Destroy(hiddenOn);if(hiddenOff)UnityEngine.Object.Destroy(hiddenOff);UnityEngine.Object.DestroyImmediate(host);
                RenderSettings.ambientLight=oldAmbient;RenderSettings.ambientMode=oldMode;pipeline.shadowDistance=distance;AudioListener.volume=oldVolume;
                NativeDisplaySettings.Shadows=preference!=0;if(hadPreference)PlayerPrefs.SetInt(shadowKey,preference);else PlayerPrefs.DeleteKey(shadowKey);PlayerPrefs.Save();
            }
        }
    }
}

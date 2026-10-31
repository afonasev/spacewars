using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingIndustrialFleetRenderingTests
    {
        [UnityTest] public IEnumerator GameplayAngleViewsAndSceneCosts()
        {
            string output=Environment.GetEnvironmentVariable("CROSSINGS_YARD_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
            var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            var profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var root=new GameObject("Fleet rendering");PlayableWorld world=null;var target=new RenderTexture(1600,1000,24);target.Create();var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);var ambient=RenderSettings.ambientLight;
            try
            {
                world=new PlayableWorld(root.transform,profile);world.InspectionReveal();
                var lighting=IndustrialLightingProfile.Load(map.Id);var sun=new GameObject("Fleet sun").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;sun.color=Color.HSVToRGB(lighting.sunHue,lighting.sunSaturation,1);sun.intensity=lighting.sunIntensity;sun.transform.rotation=Quaternion.Euler(lighting.sunElevation,lighting.sunAzimuth,0);lighting.ApplyScene(sun);sun.shadows=LightShadows.Soft;
                var camera=new GameObject("Game angle").AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=700;camera.targetTexture=target;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.1f,.13f);
                var fleet=root.GetComponentsInChildren<ModelDetailLod>().Where(l=>CrossingIndustrialFleet.Kinds.Any(k=>l.name=="Yard — "+k)).OrderBy(l=>l.name).ThenBy(l=>l.transform.position.x).ToArray();Assert.AreEqual(6,fleet.Length);
                Assert.AreEqual(0,fleet.Sum(m=>m.GetComponentsInChildren<Collider>().Length));
                IEnumerator Shot(string name,Vector3 point,float size,bool overhead=false)
                {
                    camera.orthographicSize=size;camera.transform.position=point+new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);camera.transform.LookAt(point);
                    if(overhead){camera.transform.position=new Vector3(0,160,0);camera.transform.LookAt(Vector3.zero,Vector3.forward);}
                    for(int i=0;i<5;i++){world.SelectEnvironmentDetail(camera);yield return null;}var previous=RenderTexture.active;try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();}finally{RenderTexture.active=previous;}
                    Assert.AreEqual(0,pixels.GetPixels32().Count(c=>c.r>200&&c.b>200&&c.g<40),"shader errors");File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());
                }
                foreach(var machine in fleet)
                {
                    string side=machine.transform.position.x<0?"west":"east";string name=machine.name.Substring("Yard — ".Length)+"-"+side;
                    yield return Shot(name+"-normal",machine.transform.position,(float)profile.CameraOrthoSize);
                    if(side=="west")
                    {
                        yield return Shot(name+"-close",machine.transform.position,(float)profile.CameraMinZoom);
                        // Comparable projected-size switching without altering profile thresholds.
                        yield return Shot(name+"-lod1",machine.transform.position,32);Assert.AreEqual(1,machine.Level);
                        yield return Shot(name+"-lod2",machine.transform.position,52);Assert.AreEqual(2,machine.Level);
                        world.InspectionHide();yield return Shot(name+"-fog",machine.transform.position,(float)profile.CameraOrthoSize);world.InspectionReveal();
                    }
                }
                yield return Shot("overview",Vector3.zero,72,true);
                // Matches existing baseline's final camera for meaningful active-scene counts.
                yield return Shot("baseline-curb",new Vector3(-29,0,44),9);
                var renderers=root.GetComponentsInChildren<MeshRenderer>();var meshes=root.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Where(m=>m).Distinct().ToArray();
                var resident=((System.Collections.Generic.List<Mesh>)typeof(PlayableWorld).GetField("terrainMeshes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(world)).Distinct().ToArray();
                File.WriteAllText(Path.Combine(output,"scene-cost.txt"),"renderers="+renderers.Length+" meshes="+meshes.Length+" vertices="+meshes.Sum(m=>m.vertexCount)+" triangles="+meshes.Sum(m=>m.triangles.Length/3)+" materials="+renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count()+" meshBytes="+meshes.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m))+" residentMeshes="+resident.Length+" residentBytes="+resident.Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m)));
            }
            finally{RenderSettings.ambientLight=ambient;world?.Dispose();UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target);}
        }
    }
}

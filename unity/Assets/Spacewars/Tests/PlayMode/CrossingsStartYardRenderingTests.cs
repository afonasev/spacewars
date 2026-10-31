using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingsStartYardRenderingTests
    {
        [Serializable] sealed class Cost
        {
            public string producer;
            public int renderers,meshes,vertices,triangles,submeshes,materials;
            public long meshBytes,residentMeshBytes;
            public int residentMeshes,residentTriangles;
        }
        // Same harness is committed before the product diff and used on both revisions.
        [UnityTest] public IEnumerator SourceViewsAndCosts()
        {
            var output=Environment.GetEnvironmentVariable("CROSSINGS_YARD_EVIDENCE");
            Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
            var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var root=new GameObject("Crossing yards matched rendering");PlayableWorld world=null;
            var target=new RenderTexture(1600,1000,24);target.Create();var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            var oldAmbient=RenderSettings.ambientLight;
            try
            {
                world=new PlayableWorld(root.transform,p);world.InspectionReveal();
                var lighting=IndustrialLightingProfile.Load(map.Id);
                var sun=new GameObject("Matched sun").AddComponent<Light>();sun.transform.SetParent(root.transform);
                sun.type=LightType.Directional;sun.color=Color.HSVToRGB(lighting.sunHue,lighting.sunSaturation,1);sun.intensity=lighting.sunIntensity;
                sun.transform.rotation=Quaternion.Euler(lighting.sunElevation,lighting.sunAzimuth,0);
                lighting.ApplyScene(sun);sun.shadows=LightShadows.Soft;
                var camera=new GameObject("Actual gameplay camera angles").AddComponent<Camera>();camera.transform.SetParent(root.transform);
                camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=700;camera.targetTexture=target;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.1f,.13f);
                var names=new[]{"northwest","northeast","southwest","southeast","mine-northwest","tractor-northwest","overview","fog-northwest","curb","curb-no-shadows","tractor-normal","tractor-far","curb-bias2","curb-bias4","curb-closed-shadows"};
                var points=new[]{new Vector3(-40,0,44),new Vector3(40,0,44),new Vector3(-40,0,-44),new Vector3(40,0,-44),new Vector3(-24,0,51),new Vector3(-54,0,26),Vector3.zero,new Vector3(-40,0,44),new Vector3(-29,0,44),new Vector3(-29,0,44),new Vector3(-54,0,26),new Vector3(-54,0,26),new Vector3(-29,0,44),new Vector3(-29,0,44),new Vector3(-29,0,44)};
                var sizes=new[]{22f,22f,22f,22f,9f,8f,72f,22f,9f,9f,22f,44f,9f,9f,9f};
                for(int i=0;i<names.Length;i++)
                {
                    camera.orthographicSize=sizes[i];camera.transform.position=points[i]+new Vector3((float)p.CameraOffsetX,(float)p.CameraHeight,(float)p.CameraOffsetZ);camera.transform.LookAt(points[i]);
                    if(i==6){camera.transform.position=new Vector3(0,160,0);camera.transform.LookAt(Vector3.zero,Vector3.forward);}
                    if(i==7)world.InspectionHide();
                    if(i==8)world.InspectionReveal();
                    sun.shadows=i==9?LightShadows.None:LightShadows.Soft;
                    if(i>=12){sun.GetUniversalAdditionalLightData().usePipelineSettings=false;sun.shadowBias=i==12?2:4;sun.shadowNormalBias=1;}
                    if(i==14)
                    {
                        sun.GetUniversalAdditionalLightData().usePipelineSettings=true;
                        foreach(var r in root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="Yard retaining curb"))r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.TwoSided;
                    }
                    for(int frame=0;frame<5;frame++)yield return null;
                    var active=RenderTexture.active;
                    try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();}finally{RenderTexture.active=active;}
                    var colors=pixels.GetPixels32();
                    Assert.AreEqual(0,colors.Count(c=>c.r>200&&c.b>200&&c.g<40),"Shader errors: "+names[i]);
                    if(i!=7)Assert.Greater(colors.Count(c=>Mathf.Max(c.r,Mathf.Max(c.g,c.b))>45),100000,"Production rendering: "+names[i]);
                    File.WriteAllBytes(Path.Combine(output,names[i]+".png"),pixels.EncodeToPNG());
                }
                var renderers=root.GetComponentsInChildren<MeshRenderer>();var meshes=root.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Where(m=>m).Distinct().ToArray();
                var cost=new Cost{producer=Environment.GetEnvironmentVariable("CROSSINGS_PRODUCER"),renderers=renderers.Length,meshes=meshes.Length,vertices=meshes.Sum(m=>m.vertexCount),triangles=meshes.Sum(m=>m.triangles.Length/3),submeshes=meshes.Sum(m=>m.subMeshCount),materials=renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count(),meshBytes=meshes.Sum(m=>Profiler.GetRuntimeMemorySizeLong(m))};
                var resident=((System.Collections.Generic.List<Mesh>)typeof(PlayableWorld).GetField("terrainMeshes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(world)).Distinct().ToArray();
                cost.residentMeshes=resident.Length;cost.residentTriangles=resident.Sum(m=>m.triangles.Length/3);cost.residentMeshBytes=resident.Sum(m=>Profiler.GetRuntimeMemorySizeLong(m));
                File.WriteAllText(Path.Combine(output,"cost.json"),JsonUtility.ToJson(cost,true));
                File.WriteAllText(Path.Combine(output,"map.json"),Resources.Load<TextAsset>("ThreeCrossingsProfile").text);
            }
            finally{RenderSettings.ambientLight=oldAmbient;world?.Dispose();if(root)UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target);}
        }
    }
}

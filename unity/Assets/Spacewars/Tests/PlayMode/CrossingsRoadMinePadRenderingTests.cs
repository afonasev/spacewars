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
    public sealed class CrossingsRoadMinePadRenderingTests
    {
        [UnityTest] public IEnumerator ProductionCameraCapturesRoadsAndMineYards()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var root=new GameObject("Road and mine pad render inspection");
            var ambient=RenderSettings.ambientLight;
            var target=new RenderTexture(1600,1000,24);target.Create();
            var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            string output=Environment.GetEnvironmentVariable("SPACEWARS_ROAD_PAD_EVIDENCE");
            if(string.IsNullOrEmpty(output))output=Path.Combine(Application.dataPath,"..","testResults","crossings-road-mine-pad");
            Directory.CreateDirectory(output);
            using(var world=new PlayableWorld(root.transform,profile))
            try
            {
                world.InspectionReveal();RenderSettings.ambientLight=new Color(.65f,.65f,.66f);
                var sun=new GameObject("Diagnostic sun").AddComponent<Light>();sun.transform.SetParent(root.transform);
                sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
                var camera=new GameObject("Production orthographic camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform);camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=700;camera.targetTexture=target;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.10f,.13f);
                var offset=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);
                void View(Vector3 focus,float size){camera.orthographicSize=size;camera.transform.position=focus+offset;camera.transform.LookAt(focus);}
                void Capture(string name)
                {
                    camera.Render();var previous=RenderTexture.active;
                    try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();}
                    finally{RenderTexture.active=previous;}
                    string variant=Environment.GetEnvironmentVariable("SPACEWARS_ROAD_PAD_VARIANT")??"before";
                    File.WriteAllBytes(Path.Combine(output,variant+"-"+name+".png"),pixels.EncodeToPNG());
                }
                var normal=(float)profile.CameraOrthoSize;var close=(float)profile.CameraMinZoom;
                View(new Vector3(-20,0,-18),normal);Capture("road-normal");
                View(new Vector3(-30,0,-18),normal);Capture("road-pan-west");
                View(new Vector3(-20,0,-18),normal);Capture("road-pan-center");
                View(new Vector3(-10,0,-18),normal);Capture("road-pan-east");
                View(new Vector3(40,0,0),normal);Capture("mine-pad-normal");
                View(new Vector3(40,0,0),close);Capture("mine-pad-close");
                var surface=ThreeCrossingsSurfaceProfile.Load();
                Assert.AreEqual($"three-crossings-materials-v1@{surface.revision}",world.SurfaceRevision);
                Assert.AreEqual(.8f,surface.groundOpenDetail);Assert.AreEqual(1f,surface.groundFootDetail);Assert.AreEqual(.3f,surface.groundGrainRetention);
                Assert.AreEqual("natural-frontier-v1@11",world.EnvironmentRevision);
                var map=(ThreeCrossingsMap)profile.AuthoredMap;
                foreach(var platform in map.Supports.Where(s=>s.Id.StartsWith("platform-")&&!s.Id.EndsWith("-ramp")).OrderBy(s=>s.Id))
                {
                    var bounds=platform.Bounds;View(new Vector3((float)((bounds.MinX+bounds.MaxX)*.5),2,(float)((bounds.MinZ+bounds.MaxZ)*.5)),normal);
                    Capture("platform-"+platform.Id.Substring("platform-".Length));
                }
                Assert.AreEqual(4,map.Supports.Count(s=>s.Id.StartsWith("platform-")&&!s.Id.EndsWith("-ramp")),"Capture all four accepted steel platforms.");
                File.WriteAllText(Path.Combine(output,(Environment.GetEnvironmentVariable("SPACEWARS_ROAD_PAD_VARIANT")??"before")+"-inspection.txt"),
                    $"{world.SurfaceRevision}; production orthographic camera; normal={normal}; close={close}; 1600x1000; diagnostic light and revealed fog. Road camera centers=(-30,-18),(-20,-18),(-10,-18); mine yard camera center=(40,0). Baseline source=0226c4f50963a797f556e076a46dfdfcf21ee4c0. Editor render only; not Player/device/human acceptance.\n");
            }
            finally
            {
                RenderSettings.ambientLight=ambient;UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target);
            }
            yield return null;
        }
    }
}

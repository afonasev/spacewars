using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CrossingsRockReadabilityTests
    {
        [UnityTest] public IEnumerator GameplayCameraMeasuresRockOcclusionOfSupportedGround()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;var map=(ThreeCrossingsMap)profile.AuthoredMap;
            var root=new GameObject("Rock readability inspection");var ambient=RenderSettings.ambientLight;
            var target=new RenderTexture(1600,1000,24);target.Create();var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            var output=Environment.GetEnvironmentVariable("SPACEWARS_ROCK_EVIDENCE");
            if(string.IsNullOrEmpty(output))output=Path.GetFullPath(Path.Combine(Application.dataPath,"..","testResults","rock-readability"));
            Directory.CreateDirectory(output);
            using(var world=new PlayableWorld(root.transform,profile))
            try
            {
                world.InspectionReveal();RenderSettings.ambientLight=new Color(.65f,.65f,.66f);
                var sun=new GameObject("Sun").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;sun.intensity=1.4f;sun.transform.rotation=Quaternion.Euler(48,-30,0);
                var camera=new GameObject("Gameplay camera").AddComponent<Camera>();camera.transform.SetParent(root.transform);
                camera.orthographic=true;camera.orthographicSize=(float)profile.CameraOrthoSize;camera.nearClipPlane=.1f;camera.farClipPlane=700;camera.targetTexture=target;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.10f,.13f);
                var block=new MaterialPropertyBlock();var rocks=new List<MeshCollider>();
                foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.GetPropertyBlock(block);if(block.GetFloat("_SurfaceRole")!=4||renderer.name.StartsWith("Detail —"))continue;
                    Assert.IsNull(renderer.GetComponent<Collider>(),"Production rock dressing stays collider-free");
                    // Diagnostic colliders exist only in this isolated fixture, never in the game.
                    var collider=renderer.gameObject.AddComponent<MeshCollider>();collider.sharedMesh=renderer.GetComponent<MeshFilter>().sharedMesh;rocks.Add(collider);
                }
                Physics.SyncTransforms();
                var csv=new List<string>{"x,z,supported,hidden_by_rock"};var samples=new List<Vector3>();var hidden=new List<bool>();
                var offset=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);
                for(int x=-63;x<=63;x++)for(int z=-63;z<=63;z++)
                {
                    var p=new NavPoint(x,z);if(!map.SupportsFootprint(p,.6))continue;
                    var ground=new Vector3(x,(float)map.SurfaceHeight(p)+.04f,z);var ray=new Ray(ground+offset,-offset.normalized);
                    bool obscured=rocks.Any(c=>c.Raycast(ray,out var hit,offset.magnitude-.08f));
                    samples.Add(ground);hidden.Add(obscured);csv.Add(x+","+z+",1,"+(obscured?1:0));
                }
                File.WriteAllLines(Path.Combine(output,"supported-ground.csv"),csv);
                TestContext.WriteLine("SUPPORTED="+samples.Count+" ROCK_OCCLUDED="+hidden.Count(v=>v));
                // Named physical corridor views, plus the worst local screen-overlap region.
                int worst=Enumerable.Range(0,samples.Count).OrderByDescending(i=>Enumerable.Range(0,samples.Count).Count(j=>hidden[j]&&(samples[j]-samples[i]).sqrMagnitude<100)).First();
                var focuses=new[]{new Vector3(-20,0,-18),new Vector3(20,0,18),new Vector3(-24,0,-48),new Vector3(-24,.04f,-52)};
                var names=new[]{"west-central","east-central","south-mine","worst-corridor"};
                for(int view=0;view<focuses.Length;view++)
                {
                    var focus=focuses[view];camera.transform.position=focus+offset;camera.transform.LookAt(focus);
                    for(int f=0;f<3;f++)yield return null;
                    camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();RenderTexture.active=previous;
                    File.WriteAllBytes(Path.Combine(output,names[view]+".png"),pixels.EncodeToPNG());
                    for(int i=0;i<samples.Count;i++)
                    {
                        var screen=camera.WorldToScreenPoint(samples[i]);if(screen.z<=0)continue;
                        int sx=Mathf.RoundToInt(screen.x),sy=Mathf.RoundToInt(screen.y);
                        for(int dx=-2;dx<=2;dx++)for(int dy=-2;dy<=2;dy++)if(sx+dx>=0&&sx+dx<1600&&sy+dy>=0&&sy+dy<1000)pixels.SetPixel(sx+dx,sy+dy,hidden[i]?Color.red:Color.cyan);
                    }
                    pixels.Apply();File.WriteAllBytes(Path.Combine(output,names[view]+"-authority.png"),pixels.EncodeToPNG());
                }
                File.WriteAllText(Path.Combine(output,"camera.txt"),"Production orthographic camera; offset="+offset+" size="+profile.CameraOrthoSize+"; 1600x1000. Cyan: supported radius .6m visible past rock mesh; red: supported but hidden by rock. Grid 1m. Fog revealed. Diagnostic ray colliders removed with fixture. Worst focus="+samples[worst]+"\n");
                Assert.Greater(samples.Count,1000);Assert.Greater(rocks.Count,0);
                Assert.Greater(rocks.Max(c=>c.bounds.max.y),7,"Deep rock masses must retain their tall silhouette");
                Assert.Less(hidden.Count(v=>v),230,"At least 80% of the measured 1152 baseline overlaps must be removed without changing supports");
            }
            finally{RenderSettings.ambientLight=ambient;UnityEngine.Object.Destroy(root);UnityEngine.Object.Destroy(pixels);target.Release();UnityEngine.Object.Destroy(target);}
        }
    }
}

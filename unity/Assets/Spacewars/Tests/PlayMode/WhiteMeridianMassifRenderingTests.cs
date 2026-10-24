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

public sealed class WhiteMeridianMassifRenderingTests
{
    [UnityTest] public IEnumerator MassifsHaveContinuousRoofsInsideExistingSolidsAndRenderWithFog()
    {
        var map=new WhiteMeridianMap(JsonUtility.FromJson<WhiteMeridianProfileData>(Resources.Load<TextAsset>("WhiteMeridianProfile").text));
        var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
        var art=WhiteMeridianArtProfile.Load();art.Validate();
        Assert.Less(art.rockHeightRatio,1,"unit occlusion must be reduced");
        var host=new GameObject("Meridian massif render check");
        var cameraObject=new GameObject("Massif camera");var camera=cameraObject.AddComponent<Camera>();
        var sun=new GameObject("Massif sun").AddComponent<Light>();IndustrialLightingProfile.Load(map.Id).ApplyScene(sun);
        var target=new RenderTexture(800,600,24);target.Create();camera.targetTexture=target;
        try {
            using(var world=new PlayableWorld(host.transform,profile)){
                world.Fog.ShowPublic();
                var meshes=host.GetComponentsInChildren<MeshFilter>().Where(f=>f.sharedMesh.name=="Meridian continuous low gypsum").ToArray();
                Assert.AreEqual(16,meshes.Length,"four overlapping ridge pairs must become four single massifs; all other groups remain separate");
                var solids=map.Ridges.Concat(map.BorderRocks).ToArray();
                foreach(var filter in meshes){
                    var mesh=filter.sharedMesh;var vertices=mesh.vertices;var indices=mesh.triangles;
                    var parent=new Dictionary<Vector3,Vector3>();
                    Vector3 Key(Vector3 v)=>new Vector3(Mathf.Round(v.x*10000),Mathf.Round(v.y*10000),Mathf.Round(v.z*10000));
                    Vector3 Root(Vector3 v){if(!parent.ContainsKey(v))parent[v]=v;while(parent[v]!=v){parent[v]=parent[parent[v]];v=parent[v];}return v;}
                    void Join(Vector3 a,Vector3 b){a=Root(Key(a));b=Root(Key(b));parent[a]=b;}
                    foreach(var v in vertices){
                        Assert.False(float.IsNaN(v.y)||float.IsInfinity(v.y));
                        Assert.True(solids.Any(s=>s.Footprint.Contains(new NavPoint(v.x,v.z))||s.Footprint.DistanceSquared(new NavPoint(v.x,v.z))<1e-8),"vertex leaves physical solid: "+v);
                    }
                    for(int i=0;i<indices.Length;i+=3){
                        var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];
                        Join(a,b);Join(b,c);
                        var normal=Vector3.Cross(b-a,c-a);
                        if(normal.y<=0)continue;
                        var center=(a+b+c)/3;
                        Assert.True(solids.Any(s=>s.Footprint.Contains(new NavPoint(center.x,center.z))||s.Footprint.DistanceSquared(new NavPoint(center.x,center.z))<1e-8),"roof fills an open passage: "+center);
                        Assert.Greater(center.y,0,"roof cannot descend to an internal floor fissure");
                    }
                    Assert.AreEqual(1,parent.Keys.ToArray().Select(Root).Distinct().Count(),"disconnected rock chunks");
                }
                camera.orthographic=true;camera.orthographicSize=24;camera.farClipPlane=300;
                camera.transform.position=new Vector3(-24,37,8);camera.transform.LookAt(new Vector3(-24,0,40));
                var unit=world.Tank(9001,true);unit.Root.position=new Vector3(-24,0,48);
                PlayableWorld.UpdateHealth(unit,camera,1,false);
                yield return null;camera.Render();
                string output=Environment.GetEnvironmentVariable("WHITE_MERIDIAN_EVIDENCE");
                if(!string.IsNullOrEmpty(output)){
                    Directory.CreateDirectory(output);var previous=RenderTexture.active;RenderTexture.active=target;
                    var png=new Texture2D(800,600,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,800,600),0,0);png.Apply();
                    File.WriteAllBytes(Path.Combine(output,"playmode-unit-and-massif.png"),png.EncodeToPNG());
                    UnityEngine.Object.Destroy(png);RenderTexture.active=previous;
                }
                world.Fog.Reset();yield return null;camera.Render();
            }
        } finally {
            camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);
            UnityEngine.Object.Destroy(host);UnityEngine.Object.Destroy(cameraObject);UnityEngine.Object.Destroy(sun.gameObject);
        }
    }
}

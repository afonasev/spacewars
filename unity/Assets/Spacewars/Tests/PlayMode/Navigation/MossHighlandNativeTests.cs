using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace Spacewars.Tests.PlayMode
{
    public sealed class MossHighlandNativeTests
    {
        private static PlayableProfile Profile()=>PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new MossHighlandMap());
        [Test] public void AlbedoFamiliesImportsAndNaturalPaintAreValid(){
            var art=MossHighlandArtProfile.Load();Assert.AreEqual(18,art.Parameters().Count());art.Validate();var invalid=MossHighlandArtProfile.Load();invalid.detailTile=float.NaN;Assert.Throws<ArgumentException>(()=>invalid.Validate());
            Assert.Throws<ArgumentException>(()=>MossHighlandArtProfile.Role("unknown"));
            foreach(string family in new[]{"MossHighlandArt/Moss","Environment/NaturalFrontier/Rock","Environment/NaturalFrontier/Earth"})foreach(string suffix in new[]{"","-b","-c"}){
                var t=Resources.Load<Texture2D>(family+suffix);Assert.IsNotNull(t);Assert.GreaterOrEqual(t.width,1024);Assert.GreaterOrEqual(t.height,1024);Assert.Greater(t.mipmapCount,1);Assert.AreEqual(TextureWrapMode.Repeat,t.wrapMode);Assert.AreEqual(FilterMode.Trilinear,t.filterMode);Assert.AreEqual(8,t.anisoLevel);Assert.AreEqual(family=="MossHighlandArt/Moss"?TextureFormat.RGB24:TextureFormat.BC7,t.format,family+suffix);
#if UNITY_EDITOR
                var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));Assert.True(importer.sRGBTexture);Assert.AreEqual(family=="MossHighlandArt/Moss"?TextureImporterCompression.Uncompressed:TextureImporterCompression.CompressedHQ,importer.textureCompression);
#endif
            }
            var map=new MossHighlandMap();CollectionAssert.AreEquivalent(new[]{"moss-ground","mountain.base-dark","mountain.base-mid","metal-deck"},map.Materials.Select(p=>p.Material).Distinct());
            var masks=PlayableWorld.BuildMossPaintMasks(map,art);try{
                var a=masks[0].GetPixels();var b=masks[1].GetPixels();bool feather=false;var maxima=new float[7];
                for(int i=0;i<a.Length;i++){float[] w={a[i].r,a[i].g,a[i].b,a[i].a,b[i].r,b[i].g,b[i].b};Assert.That(w.Sum(),Is.EqualTo(1).Within(4d/255));for(int j=0;j<7;j++)maxima[j]=Math.Max(maxima[j],w[j]);feather|=w.Count(x=>x>.1f)>1;}
                Assert.True(feather);foreach(int role in new[]{0,1,2,4})Assert.Greater(maxima[role],.5f);
            }finally{foreach(var t in masks)UnityEngine.Object.DestroyImmediate(t);}
        }
        [Test] public void ReliefAndSmallObjectsPreserveAllGameplayGeometry(){
            var p=Profile();var m=(MossHighlandMap)p.AuthoredMap;var host=new GameObject("Highland visual contract");var supports=m.Supports.ToArray();var blockers=m.MovementBlockers.ToArray();var seams=m.SurfaceTransitions.ToArray();
            try{using(var world=new PlayableWorld(host.transform,p)){
                Assert.IsEmpty(host.GetComponentsInChildren<Collider>(true));Assert.IsEmpty(host.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true));
                var pebbles=host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Moss pebble").ToArray();Assert.IsNotEmpty(pebbles);
                foreach(var f in pebbles){var b=f.sharedMesh.bounds;Assert.True(m.VisualRidges.Any(s=>b.min.x>=s.MinX-.0001&&b.max.x<=s.MaxX+.0001&&b.min.z>=s.MinZ-.0001&&b.max.z<=s.MaxZ+.0001&&b.min.y>=s.Bottom-.0001&&b.max.y<=s.Top+.0001));}
                var rocks=host.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="Moss ridge").ToArray();Assert.AreEqual(1,rocks.Length,"One continuous source-height skin");Assert.True(rocks.All(f=>f.sharedMesh.vertexCount>36));
                foreach(var f in rocks.Concat(pebbles))foreach(var v in f.sharedMesh.vertices)Assert.True(m.Solids.Any(s=>s.Contains(new NavPoint(v.x,v.z))&&v.y>=s.Bottom-.0001&&v.y<=s.Top+.0001),"visual outside original solid "+v);
            }CollectionAssert.AreEqual(supports,m.Supports);CollectionAssert.AreEqual(blockers,m.MovementBlockers);CollectionAssert.AreEqual(seams,m.SurfaceTransitions);}finally{UnityEngine.Object.DestroyImmediate(host);}
        }
        [UnityTest] public IEnumerator NativeWorldNormalCameraCliffsRampsAndFogRender(){
            var p=Profile();var map=(MossHighlandMap)p.AuthoredMap;var host=new GameObject("Moss Highland render QA");var cameraObject=new GameObject("Highland QA camera");var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.03f,.045f,.04f);
            cameraObject.AddComponent<VisibleShadowCoverage>().Configure(host.transform,IndustrialLightingProfile.Load(map.Id));
            var sunObject=new GameObject("Highland QA sun");var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;var lighting=IndustrialLightingProfile.Load(map.Id);var priorAmbient=RenderSettings.ambientLight;lighting.ApplyScene(sun);
            var image=new RenderTexture(1280,960,24);image.Create();camera.targetTexture=image;PlayableWorld world=null;var mini=new PlayableMapTerrain(p);
            string folder=Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"artifacts/playtest/moss-highland");Directory.CreateDirectory(folder);
            try{
                foreach(bool before in new[]{true,false}){
                    world?.Dispose();var art=MossHighlandArtProfile.Load();if(before){art.ridgeErosion=0;art.detailDensity=0;art.soilCoverage=0;art.normalSlope=0;art.naturalFeather=.1f;}
                    world=new PlayableWorld(host.transform,p,art);world.Fog.ShowPublic();mini.ShowPublicTerrain();
                    var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant("seat-"+i,i+1,i+1,OfflineControl.Human)).ToArray();var config=map.Configuration(p,7108,roster);var authority=new OfflineParticipantAuthority(config,1);
                    foreach(var owner in roster){var v=authority.View(owner.Id);foreach(var b in v.Buildings.Where(b=>b.Owner==v.Owner)){var actor=world.Building(b.Id,"headquarters",b.Owner==PlayableOwner.Player);actor.Root.position=new Vector3((float)b.Position.X,(float)map.SurfaceHeight(b.Position),(float)b.Position.Z);ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[(int)b.Owner],out var color);PlayableWorld.PaintOwner(actor,color);}foreach(var e in v.Entities.Where(e=>e.Owner==v.Owner)){var actor=world.Tank(e.Id,e.Owner==PlayableOwner.Player,e.Kind);actor.Root.position=new Vector3((float)e.Position.X,(float)map.SurfaceHeight(e.Position),(float)e.Position.Z);ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[(int)e.Owner],out var color);PlayableWorld.PaintOwner(actor,color);}}
                    yield return null;
                    foreach(string pose in new[]{"overview","oblique","normal-camera","ramp-close","cliff-close","moss-close","orbit"}){
                        var focus=pose=="normal-camera"?new Vector3(-38,0,-38):pose=="ramp-close"?new Vector3(-12,0,0):pose=="cliff-close"?new Vector3(-20,0,-20):pose=="moss-close"?new Vector3(-28,0,-10):Vector3.zero;
                        camera.orthographicSize=pose=="overview"||pose=="oblique"||pose=="orbit"?66:pose=="normal-camera"?(float)p.CameraOrthoSize:(float)p.CameraMinZoom;
                        camera.transform.position=focus+(pose=="overview"?new Vector3(0,140,-.01f):pose=="normal-camera"?new Vector3((float)p.CameraOffsetX,(float)p.CameraHeight,(float)p.CameraOffsetZ):pose=="orbit"?new Vector3(45,80,55):new Vector3(-38,82,-58));camera.transform.LookAt(focus);
                        Capture((before?"before-":"")+pose);
                    }
                }
                File.WriteAllBytes(Path.Combine(folder,"minimap.png"),mini.Texture.EncodeToPNG());Assert.Greater(mini.Texture.GetPixels32().Distinct().Count(),3);
                camera.orthographicSize=66;camera.transform.position=new Vector3(-38,82,-58);camera.transform.LookAt(Vector3.zero);camera.Render();float publicMean=Read().grayscale;
                world.Fog.Reset();camera.Render();Assert.Less(Read().grayscale,publicMean*.7f);Capture("fog");
                Color Read(){var png=Pixels();var result=new Color(png.GetPixels().Average(c=>c.r),png.GetPixels().Average(c=>c.g),png.GetPixels().Average(c=>c.b));UnityEngine.Object.DestroyImmediate(png);return result;}
                Texture2D Pixels(){var previous=RenderTexture.active;RenderTexture.active=image;var png=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,image.width,image.height),0,0);png.Apply();RenderTexture.active=previous;return png;}
                void Capture(string name){camera.Render();var png=Pixels();try{var pixels=png.GetPixels();Assert.Greater(pixels.Count(c=>c.maxColorComponent>.05f),pixels.Length/10,name);Assert.Less(pixels.Count(c=>c.r>.8f&&c.b>.8f&&c.g<.1f),pixels.Length/100,"Error shader "+name);File.WriteAllBytes(Path.Combine(folder,name+".png"),png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}}
            }finally{world?.Dispose();mini.Dispose();camera.targetTexture=null;image.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(sunObject);RenderSettings.ambientLight=priorAmbient;}
        }
    }
}

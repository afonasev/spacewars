using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace Spacewars.Tests.PlayMode
{
    public sealed class BasaltCrownNativeTests
    {
        private static PlayableProfile Profile()=>PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new BasaltCrownMap());
        [Test] public void NativeWarmPaintAndImportsRetainAllSourceRoles(){
            var art=BasaltCrownArtProfile.Load();art.Validate();
            Assert.AreEqual(17,art.Parameters().Count());
            Assert.Throws<ArgumentException>(()=>BasaltCrownArtProfile.Role("unknown"));
            var invalid=BasaltCrownArtProfile.Load();invalid.detailTile=float.NaN;Assert.Throws<ArgumentException>(()=>invalid.Validate());
            var m=new BasaltCrownMap();CollectionAssert.AreEquivalent(BasaltCrownArtProfile.Roles,m.Materials.Select(p=>p.Material).Distinct());
            long memory=0;foreach(string family in new[]{"Dark","Stone","Sand"})foreach(string suffix in new[]{"","-b","-c"}){
                var path="BasaltCrownArt/"+family+suffix;var texture=Resources.Load<Texture2D>(path);Assert.IsNotNull(texture,path);
                Assert.AreEqual(1254,texture.width);Assert.AreEqual(1254,texture.height);Assert.Greater(texture.mipmapCount,1);
                Assert.AreEqual(TextureWrapMode.Repeat,texture.wrapMode);Assert.AreEqual(FilterMode.Trilinear,texture.filterMode);Assert.AreEqual(8,texture.anisoLevel);
                // Generated albedo is opaque RGB; no alpha channel or lossy compression.
                Assert.AreEqual(TextureFormat.RGB24,texture.format);memory+=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
#if UNITY_EDITOR
                var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));Assert.True(importer.sRGBTexture);Assert.AreEqual(TextureImporterCompression.Uncompressed,importer.textureCompression);
#endif
            }
            var masks=PlayableWorld.BuildBasaltPaintMasks(m,art);try{
                var a=masks[0].GetPixels();var b=masks[1].GetPixels();var maxima=new float[7];bool feather=false;
                for(int i=0;i<a.Length;i++){
                    float[] weights={a[i].r,a[i].g,a[i].b,a[i].a,b[i].r,b[i].g,b[i].b};Assert.That(weights.Sum(),Is.EqualTo(1).Within(4d/255));
                    for(int j=0;j<7;j++)maxima[j]=Math.Max(maxima[j],weights[j]);if(weights.Count(w=>w>.1f)>1)feather=true;
                }
                Assert.True(feather);Assert.True(maxima.All(v=>v>.5f),string.Join(",",maxima));
                string folder=Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"docs/basalt-crown/warm-surfaces");Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"native-import.json"),"{\"textures\":9,\"width\":1254,\"height\":1254,\"format\":\"RGB24\",\"mipmaps\":true,\"repeat\":true,\"filter\":\"Trilinear\",\"aniso\":8,\"runtime_bytes\":"+memory+"}\n");
            }finally{foreach(var mask in masks)UnityEngine.Object.DestroyImmediate(mask);}
        }
        [UnityTest] public IEnumerator NativeAiDuelRunsWithSelectedLobbyAndNoRuntimeFailure(){
            var p=Profile();var setup=new NativeLobbyConfiguration{BasaltCrown=true,HasExplicitSeed=true,ExplicitSeed=7108};setup.InitializeParticipants();
            using(var runtime=Spacewars.Runtime.PlayableRuntime.CreateLobbyMatch(p,4,setup))using(var routes=new UnityHostRouteService()){
                float deadline=Time.realtimeSinceStartup+15;
                while(runtime.Latest.Tick<120&&runtime.Latest.Failure==null&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.GreaterOrEqual(runtime.Latest.Tick,120,"tick="+runtime.Latest.Tick+" projection="+routes.LastProjectionFailure);
                Assert.IsNull(runtime.Latest.Failure);Assert.Zero(runtime.Latest.Metrics.Errors);Assert.AreEqual(2,runtime.Latest.Participants.Count);
                runtime.RequestStop();float stopDeadline=Time.realtimeSinceStartup+3;while(!runtime.IsStopped&&Time.realtimeSinceStartup<stopDeadline)yield return null;
                Assert.True(runtime.IsStopped);
            }
        }
        [UnityTest] public IEnumerator NativeMixedUnitsTraverseBothRampsWithoutCrossingCliffs(){
            var p=Profile();var m=(BasaltCrownMap)p.AuthoredMap;TerritoryRules.ValidateArena(p);
            var geometry=new NavGeometry(p.ArenaHalfExtent,m.MovementBlockers.ToArray(),1);
            using(var router=new UnityNavigationRouter(geometry,p.Navigation))foreach(int side in new[]{-1,1})foreach(var kind in new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval}){
                double radius=PlayableUnitRules.Radius(p,kind);var origin=new NavPoint(side*30,0);var target=new NavPoint(side*14,0);
                var path=router.FindPath(origin,target);Assert.IsNotEmpty(path);
                var crowd=new NavCrowd(geometry,p.Navigation,m);crowd.Add(1,origin,radius,PlayableUnitRules.Speed(p,kind),PlayableUnitRules.Turn(p,kind));
                Assert.True(crowd.SetRoute(1,target,path));
                for(int tick=0;tick<1200&&crowd.Units[0].Outcome!=NavigationOutcome.Arrived;tick++){
                    var before=crowd.Units[0].Position;crowd.Step(1d/30);var after=crowd.Units[0].Position;
                    Assert.True(m.SupportsSweep(before,after,radius));
                    Assert.LessOrEqual(Math.Abs(m.SurfaceHeight(after)-m.SurfaceHeight(before)),Math.Tan(22*Math.PI/180)*Math.Sqrt(Math.Pow(after.X-before.X,2)+Math.Pow(after.Z-before.Z,2))+1e-7);
                }
                Assert.AreEqual(NavigationOutcome.Arrived,crowd.Units[0].Outcome);yield return null;
            }
        }
        [UnityTest] public IEnumerator NativeBasaltWorldAndMinimapRenderTheSameTerrain(){
            var p=Profile();var m=(BasaltCrownMap)p.AuthoredMap;var host=new GameObject("Basalt Crown rendered QA");
            var cameraObject=new GameObject("Basalt QA camera");var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;cameraObject.AddComponent<VisibleShadowCoverage>().Configure(host.transform,IndustrialLightingProfile.Load());
            var sunObject=new GameObject("Basalt QA sun");var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;sun.transform.rotation=Quaternion.Euler(55,-30,0);sun.intensity=1.4f;
            var priorAmbient=RenderSettings.ambientLight;IndustrialLightingProfile.Load().ApplyScene(sun);
            var image=new RenderTexture(1280,960,24);image.Create();camera.targetTexture=image;
            var world=new PlayableWorld(host.transform,p);var minimap=new PlayableMapTerrain(p);try{
                world.Fog.ShowPublic();world.InspectionSites();minimap.ShowPublicTerrain();
                Assert.Greater(minimap.Texture.GetPixels32().Distinct().Count(),2);
                var setup=new Spacewars.Runtime.OfflineParticipantAuthority(m.Configuration(p,7108,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Ai)}),1);
                var overview=setup.View("player-1");
                foreach(var b in overview.Buildings){var actor=world.Building(b.Id,"headquarters",b.Owner==PlayableOwner.Player);actor.Root.position=new Vector3((float)b.Position.X,(float)m.SurfaceHeight(b.Position),(float)b.Position.Z);}
                foreach(var u in overview.Entities){var actor=world.Tank(u.Id,u.Owner==PlayableOwner.Player,u.Kind);actor.Root.position=new Vector3((float)u.Position.X,(float)m.SurfaceHeight(u.Position),(float)u.Position.Z);}
                yield return null;
                string folder=Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"artifacts/playtest/basalt-crown-warm-surfaces");Directory.CreateDirectory(folder);
                foreach(string pose in new[]{"overview","oblique","ramp-close","east-ramp","ring-close","scree-close","cliff-close"}){
                    camera.orthographicSize=pose=="overview"||pose=="oblique"?52:(float)p.CameraMinZoom;
                    var focus=pose=="ramp-close"?new Vector3(-22,0,0):pose=="east-ramp"?new Vector3(22,0,0):pose=="ring-close"?new Vector3(-36,0,12):pose=="scree-close"?new Vector3(10,0,-24):pose=="cliff-close"?new Vector3(-16,1,-16):Vector3.zero;
                    camera.transform.position=focus+(pose=="overview"?new Vector3(0,120,-.01f):new Vector3(-32,70,-52));camera.transform.LookAt(focus);
                    camera.Render();var png=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);var previous=RenderTexture.active;RenderTexture.active=image;png.ReadPixels(new Rect(0,0,image.width,image.height),0,0);png.Apply();RenderTexture.active=previous;
                    var pixels=png.GetPixels();Assert.Greater(pixels.Count(c=>c.maxColorComponent>.05f),pixels.Length/10);
                    Assert.Less(pixels.Count(c=>c.r>.8f&&c.b>.8f&&c.g<.1f),pixels.Length/100,"Error-shader pixels in "+pose);
                    File.WriteAllBytes(Path.Combine(folder,pose+".png"),png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);
                }
                File.WriteAllBytes(Path.Combine(folder,"minimap.png"),minimap.Texture.EncodeToPNG());
                camera.orthographicSize=52;camera.transform.position=new Vector3(-32,70,-52);camera.transform.LookAt(Vector3.zero);camera.Render();
                var publicRead=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);var priorRead=RenderTexture.active;RenderTexture.active=image;publicRead.ReadPixels(new Rect(0,0,image.width,image.height),0,0);publicRead.Apply();RenderTexture.active=priorRead;
                float publicMean=publicRead.GetPixels().Average(c=>c.grayscale);UnityEngine.Object.DestroyImmediate(publicRead);
                world.Fog.Reset();Assert.True(world.Fog.Texture.GetPixels32().All(c=>c.r>0));
                camera.orthographicSize=52;camera.transform.position=new Vector3(-32,70,-52);camera.transform.LookAt(Vector3.zero);camera.Render();
                var fogPng=new Texture2D(image.width,image.height,TextureFormat.RGB24,false);var previousFog=RenderTexture.active;RenderTexture.active=image;fogPng.ReadPixels(new Rect(0,0,image.width,image.height),0,0);fogPng.Apply();RenderTexture.active=previousFog;
                Assert.Greater(fogPng.GetPixels().Count(c=>c.maxColorComponent>.001f),image.width*image.height/10);
                Assert.Less(fogPng.GetPixels().Average(c=>c.grayscale),publicMean*.7f,"Native fog must visibly darken the terrain shader.");
                File.WriteAllBytes(Path.Combine(folder,"fog.png"),fogPng.EncodeToPNG());UnityEngine.Object.DestroyImmediate(fogPng);
            }finally{world.Dispose();minimap.Dispose();camera.targetTexture=null;image.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(sunObject);RenderSettings.ambientLight=priorAmbient;}
        }
    }
}

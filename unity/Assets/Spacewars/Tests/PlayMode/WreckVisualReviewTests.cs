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
    public sealed class WreckVisualReviewTests
    {
        [UnityTest] public IEnumerator FourHawkWrecksSmokeSinkingAndHitRenderInUnity()
        {
            var host=new GameObject("Wreck visual review");var bootstrap=host.AddComponent<PlayableBootstrap>();bootstrap.enabled=false;
            const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;var p=PlayableProfile.ThreeCrossingsDefault;
            var target=new RenderTexture(1600,900,24);target.Create();PlayableWorld world=null;
            string output=Environment.GetEnvironmentVariable("SPACEWARS_WRECK_EVIDENCE")??Path.GetFullPath("../.local/wrecks/render");Directory.CreateDirectory(output);
            try{
                typeof(PlayableBootstrap).GetField("profile",f).SetValue(bootstrap,p);typeof(PlayableBootstrap).GetMethod("CreateWorld",f).Invoke(bootstrap,null);
                world=(PlayableWorld)typeof(PlayableBootstrap).GetField("world",f).GetValue(bootstrap);world.InspectionReveal();var camera=(Camera)typeof(PlayableBootstrap).GetField("cameraView",f).GetValue(bootstrap);camera.targetTexture=target;camera.orthographicSize=2.2f;camera.transform.position=new Vector3(0,7,-5);camera.transform.LookAt(new Vector3(0,0,1));
                PlayableSnapshot Frame(long tick,PlayableWreckSnapshot[] rows)=>new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,activeProfile:p,wrecks:rows);
                var hawks=Enumerable.Range(0,4).Select(i=>new PlayableWreckSnapshot(501+i,PlayableOwner.Player,PlayableEntityKind.Hawk,new NavPoint(-2.4+i*1.6,1),.2+i*.4,p.HawkRadius,5,.03+i*.04,i+1,0,0,0,900,960)).ToArray();
                void Capture(string name){var old=RenderTexture.active;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,name),pixels.EncodeToPNG());}finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(pixels);}}
                world.RenderUnitWrecks(Frame(30,Array.Empty<PlayableWreckSnapshot>()));for(int i=0;i<4;i++)yield return null;Capture("00-before-no-ground-hawk-wrecks.png");
                world.RenderUnitWrecks(Frame(30,hawks));for(int i=0;i<5;i++)yield return null;Capture("01-four-hawk-wrecks-smoke.png");Assert.AreEqual(4,world.UnitWreckCount);
                foreach(var id in hawks.Select(w=>w.Id)){var model=host.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Unit wreck "+id);Assert.AreEqual(3,model.GetComponentsInChildren<MeshRenderer>().Length);Assert.Zero(model.GetComponentsInChildren<Collider>().Length);}
                world.RenderUnitWrecks(Frame(930,hawks));for(int i=0;i<4;i++)yield return null;Capture("02-half-sunk.png");
                var hit=hawks.Select(w=>w.Destroy(940,PlayableEntityKind.Tank)).ToArray();world.RenderUnitWrecks(Frame(941,hit));for(int i=0;i<4;i++)yield return null;Capture("03-hit-flash-and-smoke.png");Assert.Zero(world.UnitWreckCount);
                world.RenderUnitWrecks(Frame(120,hawks));for(int i=0;i<4;i++)yield return null;Capture("04-hawk-textures.png");
                camera.orthographicSize=3.3f;
                var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval};
                var ground=kinds.SelectMany((kind,row)=>Enumerable.Range(1,3).Select(variant=>new PlayableWreckSnapshot(601+row*3+variant,PlayableOwner.Player,kind,new NavPoint(-2.1+(variant-1)*2.1,-1.2+row*2.1),.4,1,0,.06,variant,0,0,0,900,960))).ToArray();
                world.RenderUnitWrecks(Frame(120,ground));for(int i=0;i<4;i++)yield return null;Capture("05-ground-textures.png");Assert.AreEqual(9,world.UnitWreckCount);
                var costs=new System.Collections.Generic.List<string>{"kind,variant,vertices,triangles,renderers,materials"};
                var textures=new System.Collections.Generic.HashSet<Texture>();
                foreach(bool upgraded in new[]{false,true})foreach(var kind in new[]{"tank","explorer","shkval","hawk"})for(int variant=1;variant<=(kind=="hawk"?4:3);variant++){
                    var prefab=Resources.Load<GameObject>("Wrecks/"+kind+(upgraded?"-upgraded-":"-")+variant);var renderers=prefab.GetComponentsInChildren<MeshRenderer>();var meshes=prefab.GetComponentsInChildren<MeshFilter>().Select(m=>m.sharedMesh).ToArray();
                    costs.Add($"{kind}{(upgraded?"-upgraded":"")},{variant},{meshes.Sum(m=>m.vertexCount)},{meshes.Sum(m=>m.triangles.Length/3)},{renderers.Length},{renderers.SelectMany(r=>r.sharedMaterials).Distinct().Count()}");
                    foreach(var material in renderers.SelectMany(r=>r.sharedMaterials))textures.Add(material.GetTexture("_BaseMap"));
                }
                File.WriteAllLines(Path.Combine(output,"model-costs.csv"),costs);
                File.WriteAllLines(Path.Combine(output,"texture-costs.csv"),new[]{"name,width,height,format,mipmaps,runtimeBytes"}.Concat(textures.Select(t=>{var tex=(Texture2D)t;return $"{tex.name},{tex.width},{tex.height},{tex.format},{tex.mipmapCount},{UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex)}";})));
                PlayableWreckSnapshot Upgrade(PlayableWreckSnapshot w)=>new PlayableWreckSnapshot(w.Id,w.Owner,w.Kind,w.Position,w.Heading,w.Radius,w.DeathHeight,w.Burial,w.Variant,w.DeathTick,w.LandTick,w.RevealTick,w.SinkTick,w.ExpireTick,upgraded:true);
                camera.orthographicSize=2.2f;world.RenderUnitWrecks(Frame(120,Array.Empty<PlayableWreckSnapshot>()));world.RenderUnitWrecks(Frame(120,hawks.Select(Upgrade).ToArray()));
                for(int i=0;i<4;i++)yield return null;Capture("07-upgraded-hawk.png");
                camera.orthographicSize=3.3f;world.RenderUnitWrecks(Frame(120,ground.Select(Upgrade).ToArray()));
                for(int i=0;i<4;i++)yield return null;Capture("08-upgraded-ground.png");
                var neutral=IndustrialLightingProfile.Load(p.AuthoredMap?.Id);neutral.sunSaturation=0;
                foreach(var sun in host.GetComponentsInChildren<Light>())if(sun.type==LightType.Directional)neutral.ApplyScene(sun);
                world.ApplyGraphics(world.MaterialFinish,neutral);camera.orthographicSize=2.2f;world.RenderUnitWrecks(Frame(120,hawks));
                for(int i=0;i<4;i++)yield return null;Capture("06-neutral-light-control.png");
                File.WriteAllText(Path.Combine(output,"scope.txt"),"Unity Editor focused presentation fixture; actual imported assets and snapshot-driven effects. Synthetic presentation rows, not ordinary-match, Player/device or human acceptance. Seed 7; ThreeCrossingsDefault.\n");
            }finally{UnityEngine.Object.DestroyImmediate(host);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
    }
}

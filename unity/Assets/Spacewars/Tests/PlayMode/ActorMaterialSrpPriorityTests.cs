#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed partial class ActorMaterialRenderingTests
    {
        [UnityTest,Explicit("Cycle64 ONLY small A/P x runtime SRP flag admission control; no production/400/performance claim")]
        public IEnumerator OriginalBlocksCompareSrpPriorityAdmission()
        {
            Assert.AreNotEqual(GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);
            string output=Environment.GetEnvironmentVariable("SPACEWARS_MATERIAL_BATCH_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var baseline=PlayableProfile.Create(Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new AiTestMap());
            var store=new NativeBalanceStore(baseline,Path.Combine(output,"isolated-native-balance.json"),false);store.Select(NativeBalanceStore.NewMatchProfileId,NativeBalanceStore.NewMatchProfileVersion);var profile=store.Active;
            Assert.AreEqual(2,profile.Revision);
            var priorDetail=ModelDetailProfile.Current;ModelDetailProfile.UseSession(store.ActiveModelDetail);
            bool priorSrp=GraphicsSettings.useScriptableRenderPipelineBatching;
            var root=new GameObject("Cycle64 SRP priority admission control");
            var target=new RenderTexture(768,512,24,RenderTextureFormat.DefaultHDR){antiAliasing=4};target.Create();
            var camera=View(root.transform,target);camera.orthographic=true;camera.orthographicSize=5;camera.allowHDR=true;camera.backgroundColor=new Color(.045f,.075f,.10f);camera.transform.position=new Vector3(0,7,-10);camera.transform.LookAt(Vector3.zero);
            var lighting=IndustrialLightingProfile.Load(profile.AuthoredMap.Id);var sun=new GameObject("Native profile directional light").AddComponent<Light>();sun.transform.SetParent(root.transform);lighting.ApplyScene(sun);
            var world=new PlayableWorld(root.transform,profile);world.ApplyGraphics(ActorMaterialProfile.Load(),lighting);world.InspectionReveal();
            camera.gameObject.AddComponent<ActorGlowCamera>().Configure(world.MaterialFinish);camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());camera.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Explorer};var actors=kinds.Select((kind,i)=>world.Tank(i+1,true,kind)).ToArray();
            var bindings=new List<MaterialControlBinding>();var states=new List<InstancedLitMaterialState>();var records=new List<object>();var exactChecks=new List<(string label,bool exact)>();
            var sourceHashes=new Dictionary<string,string>();
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Spacewars/Tests/Fixtures/InstancedActorLit/Cycle61Lit.shader");Assert.NotNull(shader);
            void RememberSource(UnityEngine.Object asset)
            {
                string assetPath=AssetDatabase.GetAssetPath(asset);if(string.IsNullOrEmpty(assetPath))return;
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));if(!File.Exists(path))return;
                string hash=MaterialControlHash(File.ReadAllBytes(path));
                if(sourceHashes.TryGetValue(path,out var previous))Assert.AreEqual(previous,hash,"Authored bytes changed");else sourceHashes.Add(path,hash);
            }
            try
            {
                RememberSource(shader);RememberSource(Resources.Load<TextAsset>("PlayableProfile"));
                for(int i=0;i<actors.Length;i++){actors[i].Root.position=new Vector3(i%2==0?-2:2,0,i<2?-1.5f:1.5f);ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[i<2?0:4],out var owner);PlayableWorld.PaintOwner(actors[i],owner);}
                // Let the actual native pipeline initialize its shader tag and original runtime batching flag.
                for(int warm=0;warm<8;warm++)yield return null;
                var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;Assert.NotNull(pipeline);RememberSource(pipeline);
                priorSrp=GraphicsSettings.useScriptableRenderPipelineBatching;Assert.True(priorSrp,"Selected baseline is original SRP TRUE; do not silently force it");Assert.True(pipeline.useSRPBatcher);
                Assert.AreEqual(pipeline.renderPipelineShaderTag,Shader.globalRenderPipeline);Assert.AreEqual("UniversalPipeline",Shader.globalRenderPipeline);
                var post=camera.GetUniversalAdditionalCameraData();
                Func<string> qualityKey=()=>Json(new{quality=QualitySettings.GetQualityLevel(),aa=QualitySettings.antiAliasing,pipeline.msaaSampleCount,pipeline.renderScale,pipeline.supportsHDR,pipeline.supportsDynamicBatching,pipeline.useSRPBatcher,camera.allowHDR,target.antiAliasing,target.sRGB,post.renderPostProcessing,volumeMask=(int)post.volumeLayerMask,antialiasing=post.antialiasing.ToString(),tag=Shader.globalRenderPipeline});
                Func<string> lightKey=()=>Json(new{sun=RenderSettings.sun?RenderSettings.sun.GetInstanceID():0,sun.intensity,color=Components(sun.color),shadows=sun.shadows.ToString(),ambientMode=RenderSettings.ambientMode.ToString(),ambient=Components(RenderSettings.ambientLight),RenderSettings.ambientIntensity,mainColor=Components(Shader.GetGlobalColor("_MainLightColor")),mainPosition=MaterialInputBits(Shader.GetGlobalVector("_MainLightPosition"))});
                string originalQuality=qualityKey(),originalLighting=lightKey();Assert.AreSame(sun,RenderSettings.sun);
                File.WriteAllText(Path.Combine(output,"bound-profile.json"),Json(new{profile.Revision,map=profile.AuthoredMap.Id,profile.ArenaHalfExtent,modelDetail=store.ActiveModelDetail,originalQuality,originalLighting}));
                foreach(bool upgraded in new[]{false,true})
                {
                    if(upgraded)for(int i=0;i<actors.Length;i++)world.UpdateResearchModel(actors[i],new PlayableEntitySnapshot(i+1,PlayableOwner.Player,kinds[i],default,100,false,0,0,0,upgraded:true));
                    foreach(var actor in actors)PlayableWorld.UpdateHealth(actor,camera,1,true);bindings.Clear();states.Clear();
                    var allRenderers=actors.SelectMany(a=>a.Hull.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
                    var allMaterials=allRenderers.Select(r=>r.sharedMaterials).ToArray();var matrices=allRenderers.Select(r=>r.localToWorldMatrix).ToArray();var meshes=allRenderers.Select(r=>r.GetComponent<MeshFilter>().sharedMesh).ToArray();var visibility=allRenderers.Select(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();var shadows=allRenderers.Select(r=>(r.shadowCastingMode,r.receiveShadows)).ToArray();
                    foreach(var renderer in allRenderers)
                    {
                        var materials=renderer.sharedMaterials;foreach(var sourceMaterial in materials)RememberSource(sourceMaterial);RememberSource(renderer.GetComponent<MeshFilter>().sharedMesh);
                        if(materials.Any(m=>m.shader.name!="Universal Render Pipeline/Lit"))continue;
                        Assert.AreEqual(1,materials.Length);Assert.AreEqual(1,renderer.GetComponent<MeshFilter>().sharedMesh.subMeshCount);
                        var global=new MaterialPropertyBlock();renderer.GetPropertyBlock(global);Assert.True(global.isEmpty);
                        var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,0);var known=InventoryBlock(block,materials[0]);Assert.False(known.Keys.Except(MaterialControlColors.Concat(MaterialControlFloats)).Any(),"No newly admitted override");
                        bindings.Add(new MaterialControlBinding{Renderer=renderer,Materials=materials,Blocks=new[]{block}});var m=materials[0];
                        if(states.All(s=>s.Material!=m))states.Add(new InstancedLitMaterialState{Material=m,Shader=m.shader,Keywords=m.shaderKeywords,Instancing=m.enableInstancing,Queue=m.rawRenderQueue});
                    }
                    var frames=new byte[10][];
                    for(int phase=0;phase<10;phase++)
                    {
                        bool prototype=phase>=2&&phase<=5;bool srp=phase<4||phase>=8;
                        foreach(var s in states){s.Restore();if(prototype){s.Material.shader=shader;s.Material.shaderKeywords=s.Keywords;s.Material.enableInstancing=s.Instancing;s.Material.renderQueue=s.Queue;}Assert.AreEqual(s.Instancing,s.Material.enableInstancing);CollectionAssert.AreEquivalent(s.Keywords,s.Material.shaderKeywords);Assert.AreEqual(s.Queue,s.Material.rawRenderQueue);}
                        GraphicsSettings.useScriptableRenderPipelineBatching=srp;
                        for(int warm=0;warm<8;warm++){yield return null;Assert.AreEqual(srp,GraphicsSettings.useScriptableRenderPipelineBatching,"Runtime toggle overwritten; stop rather than modify pipeline asset");}
                        var samples=new List<object>();for(int sample=0;sample<8;sample++){yield return null;Assert.AreEqual(srp,GraphicsSettings.useScriptableRenderPipelineBatching);samples.Add(new{frame=Time.frameCount,srp=GraphicsSettings.useScriptableRenderPipelineBatching,draws=UnityStats.drawCalls,batches=UnityStats.batches,setPass=UnityStats.setPassCalls,triangles=UnityStats.triangles,instancedBatches=UnityStats.instancedBatches,shadowCasters=UnityStats.shadowCasters});}
                        frames[phase]=MaterialControlHdr(target);string label=$"upgrade-{upgraded}-phase-{phase}";File.WriteAllBytes(Path.Combine(output,label+"-rgba-f32.bin"),frames[phase]);
                        var png=ReadSrgbDisplay(target);try{File.WriteAllBytes(Path.Combine(output,label+"-srgb.png"),png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}
                        Assert.AreEqual(originalQuality,qualityKey(),"No quality/asset batching/camera/tag change");Assert.AreEqual(originalLighting,lightKey(),"Native lighting unchanged");
                        for(int i=0;i<allRenderers.Length;i++){var renderer=allRenderers[i];CollectionAssert.AreEqual(allMaterials[i],renderer.sharedMaterials);Assert.AreEqual(matrices[i],renderer.localToWorldMatrix);Assert.AreSame(meshes[i],renderer.GetComponent<MeshFilter>().sharedMesh);Assert.AreEqual(visibility[i],renderer.enabled&&renderer.gameObject.activeInHierarchy);Assert.AreEqual(shadows[i],(renderer.shadowCastingMode,renderer.receiveShadows));}
                        foreach(var b in bindings){var global=new MaterialPropertyBlock();b.Renderer.GetPropertyBlock(global);Assert.True(global.isEmpty);var slot=new MaterialPropertyBlock();b.Renderer.GetPropertyBlock(slot,0);Assert.AreEqual(MaterialControlKey(b.Materials[0],b.Blocks[0]),MaterialControlKey(b.Materials[0],slot),"Original indexed MPB bits and material identity unchanged");}
                        foreach(var s in states){Assert.AreEqual(0,ShaderUtil.GetShaderData(s.Material.shader).ActiveSubshaderIndex,"Never accept fallback shader");Assert.AreEqual(9,s.Material.passCount);}
                        records.Add(new{upgraded,phase,prototype,srp,samples,hdrSha256=MaterialControlHash(frames[phase]),quality=qualityKey(),lighting=lightKey(),materials=states.Select(s=>new{id=s.Material.GetInstanceID(),path=AssetDatabase.GetAssetPath(s.Material),shader=s.Material.shader.name,s.Material.enableInstancing,s.Material.rawRenderQueue}),bindings=bindings.Select(b=>new{renderer=b.Renderer.GetInstanceID(),material=b.Materials[0].GetInstanceID(),mesh=b.Renderer.GetComponent<MeshFilter>().sharedMesh.GetInstanceID(),originalIndexed=InventoryBlock(b.Blocks[0],b.Materials[0])}),limits="Actual global submission counts only; no actor/pass attribution/CPU/GPU/FPS. Only runtime SRPflag and existing A/P shader factor; no MPB/instancingflag/quality/source changes."});
                        File.WriteAllText(Path.Combine(output,"phases.json"),Json(records));
                    }
                    var text=new System.Text.StringBuilder();
                    foreach(var pair in new[]{(0,1),(2,3),(4,5),(6,7),(8,9),(0,8),(0,6),(2,4),(0,2),(6,4)})
                    {string label=$"upgrade-{upgraded}-phase{pair.Item1}-vs-{pair.Item2}";text.AppendLine(OriginalHdrDifference(frames[pair.Item1],frames[pair.Item2],target.width,label));exactChecks.Add((label,frames[pair.Item1].SequenceEqual(frames[pair.Item2])));}
                    File.WriteAllText(Path.Combine(output,$"upgrade-{upgraded}-strict-hdr.txt"),text.ToString());
                    foreach(var s in states)s.Restore();states.Clear();GraphicsSettings.useScriptableRenderPipelineBatching=priorSrp;
                }
                File.WriteAllText(Path.Combine(output,"strict-checks.json"),Json(exactChecks.Select(c=>new{c.label,c.exact})));
                Assert.True(exactChecks.All(c=>c.exact),"Strict HDR difference retained; inspect all20 phase captures and separate SRP vs shader effects");
            }
            finally
            {
                foreach(var s in states)s.Restore();GraphicsSettings.useScriptableRenderPipelineBatching=priorSrp;
                var restored=sourceHashes.ToDictionary(k=>k.Key,k=>new{before=k.Value,after=MaterialControlHash(File.ReadAllBytes(k.Key))});
                File.WriteAllText(Path.Combine(output,"restoration.json"),Json(new{runtimeSrp=GraphicsSettings.useScriptableRenderPipelineBatching,expectedSrp=priorSrp,sourceHashes=restored}));
                camera.targetTexture=null;world.Dispose();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(target);ModelDetailProfile.UseSession(priorDetail);
                Assert.AreEqual(priorSrp,GraphicsSettings.useScriptableRenderPipelineBatching);Assert.True(restored.All(k=>k.Value.before==k.Value.after),"Authored source bytes must restore exactly");
            }
        }
    }
}
#endif

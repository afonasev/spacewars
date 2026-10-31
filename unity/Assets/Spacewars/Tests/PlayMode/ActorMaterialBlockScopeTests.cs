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
        private static Texture2D ReadSrgbDisplay(RenderTexture linearHdr)
        {
            var previous=RenderTexture.active;bool previousSrgb=GL.sRGBWrite;
            var display=RenderTexture.GetTemporary(linearHdr.width,linearHdr.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            try{GL.sRGBWrite=true;Graphics.Blit(linearHdr,display);return Read(display);}
            finally{GL.sRGBWrite=previousSrgb;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(display);}
        }
        private static float[] Components(Color color)=>new[]{color.r,color.g,color.b,color.a};

        [UnityTest,Explicit("Cycle62 ONLY small2+2 single-slot original-MPB scope control; no400/production/public API change")]
        public IEnumerator OriginalBlocksCompareIndexedAndRendererScope()
        {
            Assert.AreNotEqual(GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);string output=Environment.GetEnvironmentVariable("SPACEWARS_MATERIAL_BATCH_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var baseline=PlayableProfile.Create(Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new AiTestMap());
            var store=new NativeBalanceStore(baseline,Path.Combine(output,"isolated-native-balance.json"),false);store.Select(NativeBalanceStore.NewMatchProfileId,NativeBalanceStore.NewMatchProfileVersion);var profile=store.Active;
            var priorDetail=ModelDetailProfile.Current;ModelDetailProfile.UseSession(store.ActiveModelDetail);
            var root=new GameObject("Cycle62 single-slot property scope control");var target=new RenderTexture(768,512,24,RenderTextureFormat.DefaultHDR){antiAliasing=4};target.Create();
            var camera=View(root.transform,target);camera.orthographic=true;camera.orthographicSize=5;camera.allowHDR=true;camera.backgroundColor=new Color(.045f,.075f,.10f);camera.transform.position=new Vector3(0,7,-10);camera.transform.LookAt(Vector3.zero);
            var lighting=IndustrialLightingProfile.Load(profile.AuthoredMap.Id);var sun=new GameObject("Native profile directional light").AddComponent<Light>();sun.transform.SetParent(root.transform);lighting.ApplyScene(sun);
            var world=new PlayableWorld(root.transform,profile);world.ApplyGraphics(ActorMaterialProfile.Load(),lighting);world.InspectionReveal();
            camera.gameObject.AddComponent<ActorGlowCamera>().Configure(world.MaterialFinish);camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());camera.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Explorer};var actors=kinds.Select((kind,i)=>world.Tank(i+1,true,kind)).ToArray();
            var bindings=new List<MaterialControlBinding>();var states=new List<InstancedLitMaterialState>();var records=new List<object>();
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Spacewars/Tests/Fixtures/InstancedActorLit/Cycle61Lit.shader");Assert.NotNull(shader);
            void RestoreScopes(){foreach(var binding in bindings){binding.Renderer.SetPropertyBlock(null);binding.Renderer.SetPropertyBlock(binding.Blocks[0],0);}}
            try
            {
                for(int i=0;i<actors.Length;i++){actors[i].Root.position=new Vector3(i%2==0?-2:2,0,i<2?-1.5f:1.5f);ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[i<2?0:4],out var owner);PlayableWorld.PaintOwner(actors[i],owner);}
                foreach(bool upgraded in new[]{false,true})
                {
                    if(upgraded)for(int i=0;i<actors.Length;i++)world.UpdateResearchModel(actors[i],new PlayableEntitySnapshot(i+1,PlayableOwner.Player,kinds[i],default,100,false,0,0,0,upgraded:true));
                    foreach(var actor in actors)PlayableWorld.UpdateHealth(actor,camera,1,true);bindings.Clear();states.Clear();
                    foreach(var actor in actors)foreach(var renderer in actor.Hull.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var materials=renderer.sharedMaterials;if(materials.Any(m=>m.shader.name!="Universal Render Pipeline/Lit"))continue;
                        Assert.AreEqual(1,materials.Length);Assert.AreEqual(1,renderer.GetComponent<MeshFilter>().sharedMesh.subMeshCount);
                        var global=new MaterialPropertyBlock();renderer.GetPropertyBlock(global);Assert.True(global.isEmpty,"Never overwrite/conflict with a global block");
                        var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,0);var known=InventoryBlock(block,materials[0]);Assert.False(known.Keys.Except(MaterialControlColors.Concat(MaterialControlFloats)).Any(),"Only original four source keys are admitted; no new instance property to bypass noninstanced keys");
                        bindings.Add(new MaterialControlBinding{Renderer=renderer,Materials=materials,Blocks=new[]{block}});var m=materials[0];if(states.All(s=>s.Material!=m))states.Add(new InstancedLitMaterialState{Material=m,Shader=m.shader,Keywords=m.shaderKeywords,Instancing=m.enableInstancing,Queue=m.rawRenderQueue});
                    }
                    var matrices=bindings.Select(b=>b.Renderer.localToWorldMatrix).ToArray();var meshes=bindings.Select(b=>b.Renderer.GetComponent<MeshFilter>().sharedMesh).ToArray();var visibility=bindings.Select(b=>b.Renderer.enabled&&b.Renderer.gameObject.activeInHierarchy).ToArray();var shadows=bindings.Select(b=>(b.Renderer.shadowCastingMode,b.Renderer.receiveShadows)).ToArray();var hdr=new byte[8][];
                    for(int phase=0;phase<8;phase++)
                    {
                        RestoreScopes();foreach(var s in states){s.Restore();if(phase>=2&&phase<=6){s.Material.shader=shader;s.Material.shaderKeywords=s.Keywords;s.Material.enableInstancing=s.Instancing;s.Material.renderQueue=s.Queue;}}
                        bool globalScope=phase==4||phase==5;
                        if(globalScope)foreach(var b in bindings){b.Renderer.SetPropertyBlock(null,0);b.Renderer.SetPropertyBlock(b.Blocks[0]);}
                        for(int warm=0;warm<8;warm++)yield return null;
                        var samples=new List<object>();for(int sample=0;sample<8;sample++){yield return null;samples.Add(new{frame=Time.frameCount,draws=UnityStats.drawCalls,batches=UnityStats.batches,setPass=UnityStats.setPassCalls,triangles=UnityStats.triangles,instancedBatches=UnityStats.instancedBatches,shadowCasters=UnityStats.shadowCasters});}
                        hdr[phase]=MaterialControlHdr(target);string label=$"upgrade-{upgraded}-phase-{phase}";File.WriteAllBytes(Path.Combine(output,label+"-rgba-f32.bin"),hdr[phase]);
                        var png=ReadSrgbDisplay(target);byte[] firstPixel;try{var bytes=png.GetRawTextureData<byte>();firstPixel=new[]{bytes[0],bytes[1],bytes[2]};File.WriteAllBytes(Path.Combine(output,label+"-srgb.png"),png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}
                        var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;var post=camera.GetUniversalAdditionalCameraData();
                        records.Add(new{upgraded,phase,globalScope,samples,hdrSha256=MaterialControlHash(hdr[phase]),firstDisplayPixel=firstPixel,lighting=new{colorSpace=QualitySettings.activeColorSpace.ToString(),targetSrgb=target.sRGB,ambientMode=RenderSettings.ambientMode.ToString(),ambient=Components(RenderSettings.ambientLight),ambientIntensity=RenderSettings.ambientIntensity,renderSettingsSun=RenderSettings.sun?RenderSettings.sun.GetInstanceID():0,mainLightGlobalColor=Components(Shader.GetGlobalColor("_MainLightColor")),mainLightGlobalPosition=MaterialInputBits(Shader.GetGlobalVector("_MainLightPosition")),activeLights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.isActiveAndEnabled).Select(l=>new{l.name,id=l.GetInstanceID(),type=l.type.ToString(),l.intensity,color=Components(l.color),l.cullingMask,shadows=l.shadows.ToString()}),post.renderPostProcessing,volumeMask=(int)post.volumeLayerMask,antialiasing=post.antialiasing.ToString(),pipeline.supportsDynamicBatching,pipeline.useSRPBatcher,pipeline.mainLightRenderingMode},limits="Readout counts only, not CPU/GPU/FPS. DisplayPNG is explicit linearHDR-to-sRGB blit AFTER untouched fullfloat capture. Scope relocation QA-only; indexed GetPropertyBlock intentionally changes inG, public production contract unchanged."});
                        File.WriteAllText(Path.Combine(output,"phases.json"),Json(records));
                        for(int i=0;i<bindings.Count;i++)
                        {
                            var b=bindings[i];CollectionAssert.AreEqual(b.Materials,b.Renderer.sharedMaterials);Assert.AreEqual(matrices[i],b.Renderer.localToWorldMatrix);Assert.AreSame(meshes[i],b.Renderer.GetComponent<MeshFilter>().sharedMesh);Assert.AreEqual(visibility[i],b.Renderer.enabled&&b.Renderer.gameObject.activeInHierarchy);Assert.AreEqual(shadows[i],(b.Renderer.shadowCastingMode,b.Renderer.receiveShadows));
                            var global=new MaterialPropertyBlock();var slot=new MaterialPropertyBlock();b.Renderer.GetPropertyBlock(global);b.Renderer.GetPropertyBlock(slot,0);Assert.AreEqual(MaterialControlKey(b.Materials[0],b.Blocks[0]),MaterialControlKey(b.Materials[0],globalScope?global:slot));Assert.True((globalScope?slot:global).isEmpty);
                        }
                    }
                    var report=new System.Text.StringBuilder();foreach(var pair in new[]{(0,1),(2,3),(4,5),(2,6),(0,7),(0,2),(2,4)})report.AppendLine(OriginalHdrDifference(hdr[pair.Item1],hdr[pair.Item2],target.width,$"phase{pair.Item1}-vs-{pair.Item2}"));File.WriteAllText(Path.Combine(output,$"upgrade-{upgraded}-strict-hdr.txt"),report.ToString());
                    Assert.True(hdr[0].SequenceEqual(hdr[1]),"Original A-A exact stability");Assert.True(hdr[2].SequenceEqual(hdr[3]),"Prototype indexed P-P exact stability");Assert.True(hdr[4].SequenceEqual(hdr[5]),"Prototype global G-G exact stability");Assert.True(hdr[2].SequenceEqual(hdr[6]),"Restored indexed P exact");Assert.True(hdr[0].SequenceEqual(hdr[7]),"Restored original A exact");Assert.True(hdr[0].SequenceEqual(hdr[2]),"Original vs prototype indexed exact; prior61FAIL remains independently");Assert.True(hdr[2].SequenceEqual(hdr[4]),"Effective original MPB values relocated between verified single-slot scopes must preserve exact HDR");
                    RestoreScopes();foreach(var s in states)s.Restore();bindings.Clear();states.Clear();
                }
            }
            finally{RestoreScopes();foreach(var s in states)s.Restore();camera.targetTexture=null;world.Dispose();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(target);ModelDetailProfile.UseSession(priorDetail);}
        }
    }
}
#endif

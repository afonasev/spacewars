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
using UnityEngine.TestTools;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed partial class ActorMaterialRenderingTests
    {
        private sealed class InstancedLitMaterialState
        {
            internal Material Material;
            internal Shader Shader;
            internal string[] Keywords;
            internal bool Instancing;
            internal int Queue;
            internal void Restore()
            {
                Material.shader=Shader;Material.shaderKeywords=Keywords;
                Material.enableInstancing=Instancing;Material.renderQueue=Queue;
            }
        }

        [UnityTest,Explicit("Cycle61 small original-MPB A-A/originalOFF/prototypeOFF/prototypeON/A discriminator; no performance claim")]
        public IEnumerator OriginalBlocksKeepExactInstancedLitPixels()=>OriginalBlocksInstancingControl(false);

        [UnityTest,Explicit("Cycle61 same-flag source/shader factor control; prior strict A-B-C FAIL remains")]
        public IEnumerator OriginalBlocksPreserveShaderAndOriginalInstancingState()=>OriginalBlocksInstancingControl(true);

        private IEnumerator OriginalBlocksInstancingControl(bool factorControl)
        {
            Assert.AreNotEqual(GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);
            Assert.True(SystemInfo.supportsInstancing);
            string output=Environment.GetEnvironmentVariable("SPACEWARS_MATERIAL_BATCH_OUTPUT");
            Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Spacewars/Tests/Fixtures/InstancedActorLit/Cycle61Lit.shader");
            Assert.NotNull(shader);Assert.True(shader.isSupported);
            var root=new GameObject("Cycle61 original MPB small control");
            var target=new RenderTexture(768,512,24,RenderTextureFormat.DefaultHDR){antiAliasing=4};target.Create();
            var camera=View(root.transform,target);camera.orthographic=true;camera.orthographicSize=5;camera.allowHDR=true;
            camera.transform.position=new Vector3(0,7,-10);camera.transform.LookAt(Vector3.zero);
            var world=new PlayableWorld(root.transform,PlayableProfile.Default);world.InspectionReveal();
            var lighting=IndustrialLightingProfile.Load();var sun=new GameObject("Cycle61 original sun").AddComponent<Light>();
            sun.transform.SetParent(root.transform);lighting.ApplyScene(sun);sun.shadows=LightShadows.Soft;
            camera.gameObject.AddComponent<ActorGlowCamera>().Configure(world.MaterialFinish);
            camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            var states=new List<InstancedLitMaterialState>();var bindings=new List<MaterialControlBinding>();
            var report=new System.Text.StringBuilder();using var capture=new OriginalMaterialCapture();
            try
            {
                var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Explorer};
                var actors=kinds.Select((kind,i)=>world.Tank(i+1,true,kind)).ToArray();
                for(int i=0;i<actors.Length;i++)actors[i].Root.position=new Vector3(i%2==0?-2:2,0,i<2?-1.5f:1.5f);
                foreach(bool upgraded in new[]{false,true})
                {
                    if(upgraded)
                    {
                        for(int i=0;i<actors.Length;i++)world.UpdateResearchModel(actors[i],new PlayableEntitySnapshot(i+1,PlayableOwner.Player,kinds[i],default,100,false,0,0,0,upgraded:true));
                        yield return null;
                    }
                    for(int ownerIndex=0;ownerIndex<NativeLobbyConfiguration.ColorHex.Length;ownerIndex++)
                    {
                        ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[ownerIndex],out var owner);
                        foreach(var actor in actors){PlayableWorld.PaintOwner(actor,owner);PlayableWorld.UpdateHealth(actor,camera,1,true);}
                        bindings.Clear();states.Clear();
                        foreach(var actor in actors)foreach(var renderer in actor.Hull.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            var materials=renderer.sharedMaterials;
                            if(materials.Any(m=>m.shader.name!="Universal Render Pipeline/Lit"))continue;
                            var global=new MaterialPropertyBlock();renderer.GetPropertyBlock(global);
                            Assert.True(global.isEmpty,"Small original-producer fixture admits no global override; ordinary ApplyGraphics remains a separate gate");
                            var blocks=new MaterialPropertyBlock[materials.Length];
                            for(int slot=0;slot<materials.Length;slot++)
                            {
                                blocks[slot]=new MaterialPropertyBlock();renderer.GetPropertyBlock(blocks[slot],slot);
                                AssertExactOriginalInput(blocks[slot],capture.Get(renderer,slot));
                                var m=materials[slot];
                                if(states.All(s=>s.Material!=m))
                                {
                                    states.Add(new InstancedLitMaterialState{Material=m,Shader=m.shader,Keywords=m.shaderKeywords,Instancing=m.enableInstancing,Queue=m.rawRenderQueue});
                                    report.AppendLine($"ORIGINAL_MATERIAL {m.name} id={m.GetInstanceID()} instancing={m.enableInstancing} rawQueue={m.rawRenderQueue} effectiveQueue={m.renderQueue}");
                                }
                            }
                            bindings.Add(new MaterialControlBinding{Renderer=renderer,Materials=materials,Blocks=blocks});
                        }
                        Assert.Greater(bindings.Count,0);
                        var matrices=bindings.Select(b=>b.Renderer.localToWorldMatrix).ToArray();
                        var meshes=bindings.Select(b=>b.Renderer.GetComponent<MeshFilter>().sharedMesh).ToArray();
                        var shadows=bindings.Select(b=>(b.Renderer.shadowCastingMode,b.Renderer.receiveShadows)).ToArray();
                        var visible=bindings.Select(b=>b.Renderer.enabled&&b.Renderer.gameObject.activeInHierarchy).ToArray();
                        var frames=new byte[factorControl?13:9][];
                        // Duplicate phases expose temporal instability separately for each path.
                        for(int phase=0;phase<frames.Length;phase++)
                        {
                            foreach(var state in states)
                            {
                                if(phase==0||phase==1||phase==frames.Length-1)state.Restore();
                                else
                                {
                                    // D (2/3) changes only original instancing flags; B (4/5)
                                    // then isolates shader substitution with those flags OFF.
                                    // Factor control adds originalON E(6/7), prototypeON C(8/9),
                                    // and prototype with ORIGINAL flags P(10/11).
                                    bool original=phase<4||(factorControl&&phase>=6&&phase<8);
                                    state.Material.shader=original?state.Shader:shader;state.Material.shaderKeywords=state.Keywords;
                                    state.Material.enableInstancing=factorControl&&phase>=10?state.Instancing:phase>=6;
                                    state.Material.renderQueue=state.Queue;
                                }
                                CollectionAssert.AreEquivalent(state.Keywords,state.Material.shaderKeywords);
                                Assert.AreEqual(state.Queue,state.Material.rawRenderQueue,"Preserve authored automatic queue sentinel as well as effective queue");
                            }
                            for(int warm=0;warm<4;warm++)yield return null;
                            var hdr=frames[phase]=MaterialControlHdr(target);string label=$"owner-{ownerIndex}-upgrade-{upgraded}-phase-{phase}";
                            File.WriteAllBytes(Path.Combine(output,label+"-rgba-f32.bin"),hdr);
                            var png=Read(target);try{File.WriteAllBytes(Path.Combine(output,label+".png"),png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}
                            report.AppendLine($"FRAME {label} sha256={MaterialControlHash(hdr)} materials={states.Count} renderers={bindings.Count}");
                            for(int i=0;i<bindings.Count;i++)
                            {
                                var b=bindings[i];CollectionAssert.AreEqual(b.Materials,b.Renderer.sharedMaterials);
                                Assert.AreEqual(matrices[i],b.Renderer.localToWorldMatrix);Assert.AreSame(meshes[i],b.Renderer.GetComponent<MeshFilter>().sharedMesh);
                                Assert.AreEqual(shadows[i],(b.Renderer.shadowCastingMode,b.Renderer.receiveShadows));
                                Assert.AreEqual(visible[i],b.Renderer.enabled&&b.Renderer.gameObject.activeInHierarchy);
                                for(int slot=0;slot<b.Blocks.Length;slot++)
                                {
                                    var current=new MaterialPropertyBlock();b.Renderer.GetPropertyBlock(current,slot);
                                    Assert.AreEqual(MaterialControlKey(b.Materials[slot],b.Blocks[slot]),MaterialControlKey(b.Materials[slot],current),"Original MPB bits/presence and material identity must never change");
                                }
                            }
                            File.WriteAllText(Path.Combine(output,"control.txt"),report.ToString());
                        }
                        var pairs=factorControl?new[]{(0,1),(2,3),(4,5),(6,7),(8,9),(10,11),(0,12),(0,2),(2,4),(6,8),(0,10),(4,8),(0,6)}:new[]{(0,1),(2,3),(4,5),(6,7),(0,8),(0,2),(2,4),(4,6),(0,4),(0,6)};
                        foreach(var pair in pairs)report.AppendLine(OriginalHdrDifference(frames[pair.Item1],frames[pair.Item2],target.width,$"phase{pair.Item1}-vs-{pair.Item2}"));
                        File.WriteAllText(Path.Combine(output,"control.txt"),report.ToString());
                        Assert.True(frames[0].SequenceEqual(frames[1]),"Original A-A stability");
                        Assert.True(frames[2].SequenceEqual(frames[3]),"Original instancingOFF D-D stability");
                        Assert.True(frames[4].SequenceEqual(frames[5]),"Prototype instancingOFF B-B stability");
                        Assert.True(frames[6].SequenceEqual(frames[7]),factorControl?"Original allON E-E stability":"Prototype instancingON C-C stability");
                        Assert.True(frames[0].SequenceEqual(frames[frames.Length-1]),"Restored original A stability");
                        Assert.True(frames[2].SequenceEqual(frames[4]),"Shader substitution with both original and prototype instancingOFF must preserve exact HDR");
                        if(factorControl)
                        {
                            Assert.True(frames[8].SequenceEqual(frames[9]),"Prototype allON C-C stability");
                            Assert.True(frames[10].SequenceEqual(frames[11]),"Prototype original flags P-P stability");
                            Assert.True(frames[6].SequenceEqual(frames[8]),"Original and prototype both allON must preserve exact HDR");
                            Assert.True(frames[0].SequenceEqual(frames[10]),"Prototype preserving ORIGINAL instancing flags must preserve full A HDR");
                        }
                        else
                        {
                            // Preserve the earlier strict control and its failure contract.
                            Assert.True(frames[0].SequenceEqual(frames[4]),"Prototype OFF must preserve full original A state HDR");
                            Assert.True(frames[4].SequenceEqual(frames[6]),"InstancingON must preserve exact HDR");
                        }
                        foreach(var state in states)state.Restore();states.Clear();
                    }
                }
                TestContext.Out.WriteLine("CYCLE61_SMALL_HDR_PASS owners="+NativeLobbyConfiguration.ColorHex.Length+" upgrades=base,upgraded kinds=Tank,Explorer; actualdraw/shadowpass qualification still separate");
            }
            finally
            {
                foreach(var state in states)state.Restore();capture.Dispose();camera.targetTexture=null;world.Dispose();
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
#endif

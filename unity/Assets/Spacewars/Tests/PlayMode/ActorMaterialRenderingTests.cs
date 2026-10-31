using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Spacewars.Presentation;
using Spacewars.Simulation;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace Spacewars.Tests.PlayMode
{
    public sealed partial class ActorMaterialRenderingTests
    {
        private sealed class MaterialControlBinding
        {
            internal Renderer Renderer;internal Material[] Materials;internal MaterialPropertyBlock[] Blocks;
        }
        private static readonly string[] MaterialControlColors={"_BaseColor","_EmissionColor"};
        private static readonly string[] MaterialControlFloats={"_Metallic","_Smoothness"};
        private static string MaterialControlKey(Material source,MaterialPropertyBlock block)
        {
            using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory)){
                writer.Write(source.GetInstanceID());
                foreach(string name in MaterialControlColors){bool set=block.HasProperty(name);writer.Write(set);if(set){var v=block.GetVector(name);writer.Write(v.x);writer.Write(v.y);writer.Write(v.z);writer.Write(v.w);}}
                foreach(string name in MaterialControlFloats){bool set=block.HasProperty(name);writer.Write(set);if(set)writer.Write(block.GetFloat(name));}
                return Convert.ToBase64String(memory.ToArray());
            }
        }
        private static byte[] MaterialControlHdr(RenderTexture target)
        {
            var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
            try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();return texture.GetRawTextureData<byte>().ToArray();}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture);}
        }
        private static string MaterialControlHash(byte[] bytes)
        {using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
#if UNITY_EDITOR
        private sealed class OriginalMaterialInput
        {
            internal readonly System.Collections.Generic.Dictionary<string,Color> Colors=new();
            internal readonly System.Collections.Generic.Dictionary<string,float> Floats=new();
            internal void Apply(MaterialPropertyBlock block)
            {foreach(var entry in Colors)block.SetColor(entry.Key,entry.Value);foreach(var entry in Floats)block.SetFloat(entry.Key,entry.Value);}
            internal void Apply(Material material,bool shaderRecipe)
            {
                foreach(var entry in Colors){
                    if(shaderRecipe&&IsHdrColor(material,entry.Key)){
                        // Re-run the original typed producer through Unity's MPB conversion.
                        // This is neither an inverse conversion nor existing-block readback.
                        var original=new MaterialPropertyBlock();original.SetColor(entry.Key,entry.Value);
                        material.SetVector(entry.Key,original.GetVector(entry.Key));
                        Assert.AreEqual(original.GetVector(entry.Key),material.GetVector(entry.Key),"HDR shader input must retain exact original linear bits");
                    }else material.SetColor(entry.Key,entry.Value);
                }
                foreach(var entry in Floats)material.SetFloat(entry.Key,entry.Value);
            }
        }
        private static bool IsHdrColor(Material material,string property)
        {
            int index=material.shader.FindPropertyIndex(property);Assert.GreaterOrEqual(index,0);
            Assert.AreEqual(ShaderPropertyType.Color,material.shader.GetPropertyType(index));
            bool hdr=(material.shader.GetPropertyFlags(index)&ShaderPropertyFlags.HDR)!=0;
            Assert.AreEqual(property=="_EmissionColor",hdr,"This probe admits exactly the installed URP Lit BaseColor/EmissionColor property contract");return hdr;
        }
        private sealed class OriginalMaterialCapture:IDisposable
        {
            private readonly System.Collections.Generic.Dictionary<(Renderer,int),OriginalMaterialInput> inputs=new();
            internal OriginalMaterialInput Get(Renderer renderer,int slot)
            {if(!inputs.TryGetValue((renderer,slot),out var input))inputs.Add((renderer,slot),input=new OriginalMaterialInput());return input;}
            internal OriginalMaterialCapture()
            {
                Assert.IsNull(ActorMaterialProfile.ColorInputObserver);Assert.IsNull(ActorMaterialProfile.FloatInputObserver);
                ActorMaterialProfile.ColorInputObserver=(renderer,slot,key,value)=>Get(renderer,slot).Colors[key]=value;
                ActorMaterialProfile.FloatInputObserver=(renderer,slot,key,value)=>Get(renderer,slot).Floats[key]=value;
            }
            public void Dispose(){ActorMaterialProfile.ColorInputObserver=null;ActorMaterialProfile.FloatInputObserver=null;}
        }
        private static void AssertExactOriginalInput(MaterialPropertyBlock original,OriginalMaterialInput input)
        {
            var replay=new MaterialPropertyBlock();input.Apply(replay);
            foreach(string property in MaterialControlColors){Assert.AreEqual(original.HasProperty(property),replay.HasProperty(property),property);if(original.HasProperty(property))Assert.AreEqual(original.GetVector(property),replay.GetVector(property),"Original typed input must reproduce exact stored linear bits: "+property);}
            foreach(string property in MaterialControlFloats){Assert.AreEqual(original.HasProperty(property),replay.HasProperty(property),property);if(original.HasProperty(property))Assert.AreEqual(original.GetFloat(property),replay.GetFloat(property),property);}
        }
        [UnityTest,Explicit("Small graphical original-producer typed-input probe; no batching/performance claim")]
        public IEnumerator OriginalProducerInputsKeepExactUrpLitPixels()=>OriginalProducerInputControl(false);
        [UnityTest,Explicit("Small actual URP Lit HDR-aware producer recipe; no batching/performance claim")]
        public IEnumerator OriginalProducerShaderRecipesKeepExactUrpLitPixels()=>OriginalProducerInputControl(true);
        [UnityTest,Explicit("Discriminate material identity while retaining exact original MPBs")]
        public IEnumerator OriginalProducerShaderRecipesWithBlocksKeepExactUrpLitPixels()=>OriginalProducerInputControl(true,true);
        private IEnumerator OriginalProducerInputControl(bool shaderRecipe,bool retainOriginalBlocks=false)
        {
            Assert.AreNotEqual(GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);
            string output=Environment.GetEnvironmentVariable("SPACEWARS_MATERIAL_BATCH_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var root=new GameObject("Original producer material input probe");
            var target=new RenderTexture(768,512,24,RenderTextureFormat.DefaultHDR){antiAliasing=4};target.Create();
            var camera=View(root.transform,target);camera.orthographic=true;camera.orthographicSize=5;camera.allowHDR=true;
            camera.transform.position=new Vector3(0,7,-10);camera.transform.LookAt(Vector3.zero);
            var world=new PlayableWorld(root.transform,PlayableProfile.Default);world.InspectionReveal();
            var lighting=IndustrialLightingProfile.Load();var sun=new GameObject("Original industrial sun").AddComponent<Light>();sun.transform.SetParent(root.transform);lighting.ApplyScene(sun);sun.shadows=LightShadows.Soft;
            camera.gameObject.AddComponent<ActorGlowCamera>().Configure(world.MaterialFinish);
            camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            var bindings=new System.Collections.Generic.List<MaterialControlBinding>();var owned=new System.Collections.Generic.List<Material>();
            using var capture=new OriginalMaterialCapture();var report=new System.Text.StringBuilder();int checkedSlots=0;
            try{
                var tank=world.Tank(1,true,PlayableEntityKind.Tank);var explorer=world.Tank(2,true,PlayableEntityKind.Explorer);
                tank.Root.position=Vector3.left*2;explorer.Root.position=Vector3.right*2;
                foreach(bool upgraded in new[]{false,true}){
                    if(upgraded){world.UpdateResearchModel(tank,new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,default,100,false,0,0,0,upgraded:true));world.UpdateResearchModel(explorer,new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Explorer,default,100,false,0,0,0,upgraded:true));yield return null;}
                    for(int ownerIndex=0;ownerIndex<NativeLobbyConfiguration.ColorHex.Length;ownerIndex++){
                        ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[ownerIndex],out var owner);
                        foreach(var actor in new[]{tank,explorer}){PlayableWorld.PaintOwner(actor,owner);PlayableWorld.UpdateHealth(actor,camera,1,true);}
                        bindings.Clear();
                        foreach(var actor in new[]{tank,explorer})foreach(var renderer in actor.Hull.GetComponentsInChildren<MeshRenderer>(true)){
                            var materials=renderer.sharedMaterials;
                            if(materials.Any(m=>m.shader.name!="Universal Render Pipeline/Lit")){
                                report.AppendLine("UNCHANGED_NON_URPLIT renderer="+renderer.name+" shaders="+string.Join(",",materials.Select(m=>m.shader.name))+" active="+renderer.gameObject.activeInHierarchy);continue;
                            }
                            var global=new MaterialPropertyBlock();renderer.GetPropertyBlock(global);Assert.True(global.isEmpty);
                            var blocks=new MaterialPropertyBlock[materials.Length];
                            for(int slot=0;slot<materials.Length;slot++){
                                var block=blocks[slot]=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);
                                var shader=materials[slot].shader;
                                for(int property=0;property<shader.GetPropertyCount();property++)if(block.HasProperty(shader.GetPropertyNameId(property)))Assert.True(MaterialControlColors.Contains(shader.GetPropertyName(property))||MaterialControlFloats.Contains(shader.GetPropertyName(property)),"Unadmitted override");
                                AssertExactOriginalInput(block,capture.Get(renderer,slot));checkedSlots++;
                            }
                            bindings.Add(new MaterialControlBinding{Renderer=renderer,Materials=materials,Blocks=blocks});
                        }
                        Assert.Greater(bindings.Count,0);var phaseHdr=new byte[4][];bool typedColorsExact=true;
                        var matrices=bindings.Select(b=>b.Renderer.localToWorldMatrix).ToArray();
                        var visible=bindings.Select(b=>b.Renderer.enabled&&b.Renderer.gameObject.activeInHierarchy).ToArray();
                        for(int mode=0;mode<4;mode++){
                            if(mode==2)foreach(var binding in bindings){
                                var assigned=new Material[binding.Materials.Length];
                                for(int slot=0;slot<assigned.Length;slot++){
                                    var source=binding.Materials[slot];var input=capture.Get(binding.Renderer,slot);
                                    var material=new Material(source);owned.Add(material);input.Apply(material,shaderRecipe);assigned[slot]=material;
                                    CollectionAssert.AreEqual(source.shaderKeywords,material.shaderKeywords);Assert.AreEqual(source.enableInstancing,material.enableInstancing);
                                    foreach(var entry in input.Colors){
                                        report.AppendLine("TYPED_INPUT owner="+ownerIndex+" upgraded="+upgraded+" material="+source.name+" key="+entry.Key+" input="+MaterialInputBits(entry.Value)+" block_linear="+MaterialInputBits(binding.Blocks[slot].GetVector(entry.Key))+" material_srgb_readback="+MaterialInputBits(material.GetVector(entry.Key))+" block_color_readback="+MaterialInputBits(binding.Blocks[slot].GetColor(entry.Key)));
                                        bool same=shaderRecipe&&IsHdrColor(material,entry.Key)?binding.Blocks[slot].GetVector(entry.Key).Equals(material.GetVector(entry.Key)):binding.Blocks[slot].GetColor(entry.Key).Equals(material.GetColor(entry.Key));typedColorsExact&=same;
                                        if(!same)report.AppendLine("EXACT_TYPED_COLOR_MISMATCH material="+source.name+" key="+entry.Key+" original_block_color="+MaterialInputBits(binding.Blocks[slot].GetColor(entry.Key))+" candidate_material_color="+MaterialInputBits(material.GetColor(entry.Key)));
                                    }
                                    foreach(var entry in input.Floats)Assert.AreEqual(entry.Value,material.GetFloat(entry.Key));
                                    if(!retainOriginalBlocks)binding.Renderer.SetPropertyBlock(null,slot);
                                }
                                binding.Renderer.sharedMaterials=assigned;
                            }
                            if(mode==3)RestoreOriginalBindings(bindings);
                            for(int warm=0;warm<4;warm++)yield return null;
                            byte[] hdr=MaterialControlHdr(target);string label="owner-"+ownerIndex+"-upgrade-"+upgraded+"-"+mode;
                            phaseHdr[mode]=hdr;File.WriteAllBytes(Path.Combine(output,label+"-rgba-f32.bin"),hdr);
                            var pixels=Read(target);try{File.WriteAllBytes(Path.Combine(output,label+".png"),pixels.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(pixels);}
                            report.AppendLine("ORIGINAL_INPUT_HDR "+label+" sha256="+MaterialControlHash(hdr)+" checkedSlots="+checkedSlots);
                            File.WriteAllText(Path.Combine(output,"control.txt"),report.ToString());
                            for(int i=0;i<bindings.Count;i++){Assert.AreEqual(matrices[i],bindings[i].Renderer.localToWorldMatrix);Assert.AreEqual(visible[i],bindings[i].Renderer.enabled&&bindings[i].Renderer.gameObject.activeInHierarchy);}
                        }
                        for(int phase=1;phase<4;phase++)report.AppendLine(OriginalHdrDifference(phaseHdr[0],phaseHdr[phase],target.width,"phase0-vs-"+phase));
                        File.WriteAllText(Path.Combine(output,"control.txt"),report.ToString());
                        Assert.True(phaseHdr[0].SequenceEqual(phaseHdr[1]),"Unchanged A-A must be byte-exact before binding attribution");
                        Assert.True(phaseHdr[0].SequenceEqual(phaseHdr[3]),"Restored A must be byte-exact before binding attribution");
                        Assert.True(typedColorsExact,"Same typed Color API must expose exactly equal effective color values; no readback is used to construct candidate");
                        Assert.True(phaseHdr[0].SequenceEqual(phaseHdr[2]),"Identical original typed inputs must preserve byte-exact rendered URP Lit HDR pixels");
                        foreach(var material in owned)UnityEngine.Object.DestroyImmediate(material);owned.Clear();
                    }
                }
                TestContext.Out.WriteLine("ORIGINAL_INPUT_CONTROL exactSlots="+checkedSlots+" owners="+NativeLobbyConfiguration.ColorHex.Length+" kinds=Tank,Explorer upgrades=base,upgraded api="+SystemInfo.graphicsDeviceType);
            }finally{
                RestoreOriginalBindings(bindings);capture.Dispose();camera.targetTexture=null;world.Dispose();foreach(var material in owned)UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(target);
            }
        }
        private static string MaterialInputBits(Vector4 value)=>string.Join(",",new[]{value.x,value.y,value.z,value.w}.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/"+BitConverter.SingleToInt32Bits(v).ToString("x8")));
        private static string OriginalHdrDifference(byte[] before,byte[] after,int width,string label)
        {
            int pixels=0,channels=0,maxIndex=-1;double max=0;var examples=new System.Text.StringBuilder();
            for(int pixel=0;pixel<before.Length/16;pixel++){
                bool changed=false;
                for(int channel=0;channel<4;channel++){
                    int offset=pixel*16+channel*4;if(BitConverter.ToInt32(before,offset)==BitConverter.ToInt32(after,offset))continue;
                    changed=true;channels++;float a=BitConverter.ToSingle(before,offset),b=BitConverter.ToSingle(after,offset);double delta=Math.Abs((double)a-b);
                    if(delta>max){max=delta;maxIndex=pixel*4+channel;}
                    if(channels<=32)examples.Append(" [x="+pixel%width+" y="+pixel/width+" channel="+channel+" a="+a.ToString("R")+" b="+b.ToString("R")+" aBits="+BitConverter.ToInt32(before,offset).ToString("x8")+" bBits="+BitConverter.ToInt32(after,offset).ToString("x8")+"]");
                }
                if(changed)pixels++;
            }
            return "EXACT_HDR_DIFF "+label+" pixels="+pixels+" channels="+channels+" maxAbs="+max.ToString("R")+" maxX="+(maxIndex<0?-1:maxIndex/4%width)+" maxY="+(maxIndex<0?-1:maxIndex/4/width)+" maxChannel="+(maxIndex<0?-1:maxIndex%4)+examples;
        }
        private static void RestoreOriginalBindings(System.Collections.Generic.IEnumerable<MaterialControlBinding> bindings)
        {foreach(var binding in bindings)if(binding.Renderer){binding.Renderer.sharedMaterials=binding.Materials;for(int slot=0;slot<binding.Blocks.Length;slot++)binding.Renderer.SetPropertyBlock(binding.Blocks[slot],slot);}}
#endif
        [UnityTest,Explicit("400-model graphical material-binding control; exclusive graphical quiet Editor only")]
        public IEnumerator FourHundredModelsCompareExactSharedMaterialBatching()
        {
            Assert.AreNotEqual(GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType,"Graphical Metal control must not run with -nographics");
            const int count=400;var output=Environment.GetEnvironmentVariable("SPACEWARS_MATERIAL_BATCH_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var root=new GameObject("Exact material batching control");var world=new PlayableWorld(root.transform,PlayableProfile.Default);world.InspectionReveal();
            var target=new RenderTexture(3456,2160,24,RenderTextureFormat.DefaultHDR){antiAliasing=4};target.Create();
            var camera=View(root.transform,target);camera.orthographic=true;camera.orthographicSize=144;camera.farClipPlane=1728;camera.allowHDR=true;
            camera.transform.position=new Vector3(0,104,-148);camera.transform.LookAt(Vector3.zero);camera.gameObject.AddComponent<ActorGlowCamera>();
            var light=IndustrialLightingProfile.Load();var sun=new GameObject("Unchanged industrial light").AddComponent<Light>();sun.transform.SetParent(root.transform);light.ApplyScene(sun);sun.shadows=LightShadows.Soft;
            camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,light);
            var actors=new PlayableWorld.Actor[count];var bindings=new System.Collections.Generic.List<MaterialControlBinding>();
            var variants=new System.Collections.Generic.Dictionary<string,Material>();
            var draws=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render,"Draw Calls Count",1);
            var batches=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render,"Batches Count",1);
            var passes=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render,"SetPass Calls Count",1);
            var triangles=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render,"Triangles Count",1);
            var main=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Internal,"Main Thread",1);
            var render=Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Internal,"Render Thread",1);
            var timing=new FrameTiming[1];byte[] expected=null;var report=new System.Text.StringBuilder();int slots=0;
            try{
                for(int i=0;i<count;i++){
                    int cohort=i/50,local=i%50;float angle=cohort*Mathf.PI/4;
                    var actor=actors[i]=world.Tank(10000+i,true,local==0?PlayableEntityKind.Explorer:PlayableEntityKind.Tank);
                    actor.Root.position=new Vector3(Mathf.Cos(angle)*88+local%7*2.2f,0,Mathf.Sin(angle)*88+local/7*2.2f);
                    ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[cohort],out var owner);PlayableWorld.PaintOwner(actor,owner);
                    for(int warm=0;warm<3;warm++)PlayableWorld.UpdateHealth(actor,camera,1,true);
                    foreach(var renderer in actor.Hull.GetComponentsInChildren<MeshRenderer>(true)){
                        var materials=renderer.sharedMaterials;if(materials.Any(m=>m.shader.name!="Universal Render Pipeline/Lit"))continue;
                        var global=new MaterialPropertyBlock();renderer.GetPropertyBlock(global);Assert.True(global.isEmpty,"This first control admits no renderer-level overrides; do not silently discard one");
                        var blocks=new MaterialPropertyBlock[materials.Length];
                        for(int slot=0;slot<materials.Length;slot++){
                            var block=blocks[slot]=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,slot);slots++;
                            var shader=materials[slot].shader;
                            for(int property=0;property<shader.GetPropertyCount();property++)if(block.HasProperty(shader.GetPropertyNameId(property)))
                                Assert.True(MaterialControlColors.Contains(shader.GetPropertyName(property))||MaterialControlFloats.Contains(shader.GetPropertyName(property)),"Unadmitted slot override "+shader.GetPropertyName(property));
                        }
                        bindings.Add(new MaterialControlBinding{Renderer=renderer,Materials=materials,Blocks=blocks});
                    }
                }
                Assert.True(draws.Valid&&batches.Valid&&triangles.Valid,"Actual graphical draw/batch/triangle recorders are required; non-threaded Editor render CPU remains NotRun");
                for(int mode=0;mode<3;mode++){
                    bool candidate=mode==1;
                    if(mode==1)foreach(var binding in bindings){
                        var assigned=new Material[binding.Materials.Length];
                        for(int slot=0;slot<assigned.Length;slot++){
                            var source=binding.Materials[slot];var block=binding.Blocks[slot];string key=MaterialControlKey(source,block);
                            if(!variants.TryGetValue(key,out var material)){
                                material=new Material(source){name=source.name+"-exact-shared-control"};
                                foreach(string name in MaterialControlColors)if(block.HasProperty(name)){material.SetVector(name,block.GetVector(name));Assert.AreEqual(block.GetVector(name),material.GetVector(name),"Preserve raw HDR/color values without an extra SetColor conversion");}
                                foreach(string name in MaterialControlFloats)if(block.HasProperty(name)){material.SetFloat(name,block.GetFloat(name));Assert.AreEqual(block.GetFloat(name),material.GetFloat(name));}
                                CollectionAssert.AreEqual(source.shaderKeywords,material.shaderKeywords);Assert.AreEqual(source.enableInstancing,material.enableInstancing);variants.Add(key,material);
                            }
                            assigned[slot]=material;binding.Renderer.SetPropertyBlock(null,slot);
                        }binding.Renderer.sharedMaterials=assigned;
                    }
                    if(mode==2)foreach(var binding in bindings){binding.Renderer.sharedMaterials=binding.Materials;for(int slot=0;slot<binding.Blocks.Length;slot++)binding.Renderer.SetPropertyBlock(binding.Blocks[slot],slot);}
                    for(int warm=0;warm<8;warm++){FrameTimingManager.CaptureFrameTimings();yield return null;}
                    double draw=0,batch=0,pass=0,tri=0,mainNs=0,renderNs=0;int mainSamples=0,renderSamples=0;var gpu=new System.Collections.Generic.Dictionary<ulong,double>();
                    const int frames=24;
                    for(int frame=0;frame<frames;frame++){
                        FrameTimingManager.CaptureFrameTimings();yield return null;
                        Assert.Greater(draws.Count,0);draw+=draws.LastValue;batch+=batches.LastValue;pass+=passes.LastValue;tri+=triangles.LastValue;if(main.Valid&&main.Count>0){mainNs+=main.LastValue;mainSamples++;}if(render.Valid&&render.Count>0){renderNs+=render.LastValue;renderSamples++;}
                        if(FrameTimingManager.GetLatestTimings(1,timing)>0&&timing[0].frameStartTimestamp>0&&timing[0].gpuFrameTime>0)gpu[timing[0].frameStartTimestamp]=timing[0].gpuFrameTime;
                    }
                    string label=mode==0?"baseline-a":candidate?"shared-variants":"baseline-b";
                    byte[] hdr=MaterialControlHdr(target);var pixels=Read(target);try{File.WriteAllBytes(Path.Combine(output,label+".png"),pixels.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(pixels);}
                    var line="MATERIAL_BATCH_CONTROL mode="+label+" actors="+count+" renderers="+bindings.Count+" slots="+slots+" variants="+variants.Count+" frames="+frames+" draws="+(draw/frames)+" batches="+(batch/frames)+" setpass="+(pass/frames)+" triangles="+(tri/frames)+" main_ms="+(mainSamples==0?-1:mainNs/mainSamples/1000000)+" render_ms="+(renderSamples==0?-1:renderNs/renderSamples/1000000)+" gpu_samples="+gpu.Count+" gpu_ms="+(gpu.Count==0?-1:gpu.Values.Average())+" hdr_sha256="+MaterialControlHash(hdr)+" api="+SystemInfo.graphicsDeviceType+" quality="+QualitySettings.GetQualityLevel()+" renderScale="+((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).renderScale;
                    report.AppendLine(line);File.WriteAllText(Path.Combine(output,"control.txt"),report.ToString());TestContext.Out.WriteLine(line);
                    if(expected==null){expected=hdr;File.WriteAllBytes(Path.Combine(output,"baseline-rgba-f32.bin"),hdr);}
                    else Assert.True(expected.SequenceEqual(hdr),"Matched rendered HDR pixels must be byte-exact; no appearance approximation admitted");
                }
            }finally{
                foreach(var binding in bindings)if(binding.Renderer){binding.Renderer.sharedMaterials=binding.Materials;for(int slot=0;slot<binding.Blocks.Length;slot++)binding.Renderer.SetPropertyBlock(binding.Blocks[slot],slot);}
                draws.Dispose();batches.Dispose();passes.Dispose();triangles.Dispose();main.Dispose();render.Dispose();
                camera.targetTexture=null;world.Dispose();foreach(var material in variants.Values)UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(target);
            }
        }
        [Test] public void MaterialColorRoundTripsExposePropertyBlockRepresentation()
        {
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));var block=new MaterialPropertyBlock();
            string Bits(Vector4 value)=>string.Join(",",new[]{value.x,value.y,value.z,value.w}.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/"+BitConverter.SingleToInt32Bits(v).ToString("x8")));
            try{foreach(float value in new[]{.01f,.05f,.19f,.5f,1f,2.8f}){
                var color=new Color(value,value*.7f,value*.4f,1);block.Clear();block.SetColor("_BaseColor",color);
                var raw=block.GetVector("_BaseColor");var read=block.GetColor("_BaseColor");
                material.SetVector("_BaseColor",raw);var vectorRoute=material.GetVector("_BaseColor");
                material.SetColor("_BaseColor",read);var colorRoute=material.GetColor("_BaseColor");var colorVector=material.GetVector("_BaseColor");
                TestContext.Out.WriteLine("MATERIAL_COLOR_PROBE input="+Bits(color)+" blockVector="+Bits(raw)+" blockColor="+Bits(read)+" materialVectorRoute="+Bits(vectorRoute)+" materialColorRoute="+Bits(colorRoute)+" materialColorVector="+Bits(colorVector)+" linearSpace="+QualitySettings.activeColorSpace);
            }}finally{UnityEngine.Object.DestroyImmediate(material);}
        }
        private Color ambient;private AmbientMode mode;private float audio,shadowDistance;
        private Action restoreHealthPreference;
        [SetUp] public void Setup(){restoreHealthPreference=NativePreferenceWriter.Capture();NativeUserSettings.HideDistantHealth=false;ambient=RenderSettings.ambientLight;mode=RenderSettings.ambientMode;audio=AudioListener.volume;AudioListener.volume=0;shadowDistance=((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).shadowDistance;}
        [TearDown] public void Restore(){restoreHealthPreference();PlayerPrefs.Save();RenderSettings.ambientLight=ambient;RenderSettings.ambientMode=mode;AudioListener.volume=audio;((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).shadowDistance=shadowDistance;}
        [TestCase("restrained")][TestCase("luminous")] public void NamedControlsValidateEveryRange(string variant)
        {
            var p=ActorMaterialProfile.Load(variant);p.Validate();Assert.AreEqual(variant,p.variant);
            var parameters=p.Parameters().ToArray();Assert.AreEqual(17,parameters.Length);Assert.AreEqual(parameters.Length,parameters.Select(x=>x.Path).Distinct().Count());
            foreach(var field in parameters){float original=field.Read();field.Write(field.Maximum+field.Step);Assert.Throws<ArgumentException>(()=>p.Validate(),field.Path);field.Write(float.NaN);Assert.Throws<ArgumentException>(()=>p.Validate());field.Write(original);}
            Assert.Greater(ActorMaterialProfile.Load("luminous").teamEmission,ActorMaterialProfile.Load().teamEmission);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void UpgradeAndOwnerPaintKeepFinishAndSharedMaterials(PlayableEntityKind kind)
        {
            var root=new GameObject("Actor finish ownership test");
            try{
                using var world=new PlayableWorld(root.transform,PlayableProfile.Default);var finish=world.MaterialFinish;
                var actor=world.Tank(1,true,kind);var enemy=world.Tank(2,false,kind);
                foreach(bool upgraded in new[]{false,true}){
                    if(upgraded)world.UpdateResearchModel(actor,new PlayableEntitySnapshot(1,PlayableOwner.Player,kind,default,100,false,0,0,0,upgraded:true));
                    var owner=new Color(.75f,.25f,.125f);PlayableWorld.PaintOwner(actor,owner);
                    foreach(var r in actor.Hull.GetComponentsInChildren<Renderer>())for(int slot=0;slot<r.sharedMaterials.Length;slot++){
                        var m=r.sharedMaterials[slot];var block=new MaterialPropertyBlock();r.GetPropertyBlock(block,slot);
                        if(m.name.EndsWith("team-primary")){Assert.AreEqual(StoredColor("_BaseColor",owner),block.GetColor("_BaseColor"));Assert.AreEqual(finish.armorSmoothness,block.GetFloat("_Smoothness"));Assert.AreEqual(finish.armorMetallic,block.GetFloat("_Metallic"));}
                        if(m.name.EndsWith("team-emissive"))Assert.AreEqual(StoredColor("_EmissionColor",owner*finish.TeamEmissionFor(m.name)),block.GetColor("_EmissionColor"));
                        if(m.name.EndsWith("chassis-emissive"))Assert.AreEqual(StoredColor("_EmissionColor",m.GetColor("_EmissionColor")*(float)PlayableProfile.Default.TankChassisGlowIntensity*finish.emissionGain),block.GetColor("_EmissionColor"));
                    }
                }
                var team=enemy.Hull.GetComponentsInChildren<Renderer>().First(r=>r.sharedMaterial.name.EndsWith("team-primary"));
                Assert.AreEqual(1f,team.sharedMaterial.GetFloat("_Smoothness"),"Authored material asset remains unchanged");
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        // Compare the exact engine property-block round trip (SetColor/GetColor includes color-space conversion).
        private static Color StoredColor(string property,Color color){var block=new MaterialPropertyBlock();block.SetColor(property,color);return block.GetColor(property);}
        private static Camera View(Transform root,RenderTexture target)
        {var c=new GameObject("Material preview camera").AddComponent<Camera>();c.transform.SetParent(root);c.targetTexture=target;c.fieldOfView=48;c.nearClipPlane=.1f;c.farClipPlane=700;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.075f,.10f,.13f);return c;}
        private static Texture2D Read(RenderTexture target)
        {var old=RenderTexture.active;try{RenderTexture.active=target;var t=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,target.width,target.height),0,0);t.Apply();return t;}finally{RenderTexture.active=old;}}
        private static int Changed(Color32[] a,Color32[] b,int tolerance=3)
        {int n=0;for(int i=0;i<a.Length;i++)if(Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b)>tolerance)n++;return n;}
        private static string Output(){var p=Environment.GetEnvironmentVariable("SPACEWARS_MATERIAL_EVIDENCE")??Path.GetFullPath("../.local/actor-material/captures");Directory.CreateDirectory(p);return p;}
        private static PlayableProfile Map(bool foundry)=>foundry?FoundryGreybox.LoadProfile():PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
        [UnityTest] public IEnumerator CaptureMatchedMaterialAlternativesWithoutExtraGeometry()
        {
            var output=Output();var light=IndustrialLightingProfile.Load();
            foreach(bool foundry in new[]{false,true}){
                var profile=Map(foundry);var root=new GameObject("Material alternatives");var world=new PlayableWorld(root.transform,profile);world.InspectionReveal();
                var focus=world.Point(TerritoryRules.Sites(profile)[0].Position);var target=new RenderTexture(1600,1000,24);target.Create();var camera=View(root.transform,target);
                var glow=camera.gameObject.AddComponent<ActorGlowCamera>();var sun=new GameObject("Accepted industrial sun").AddComponent<Light>();sun.transform.SetParent(root.transform);light.ApplyScene(sun);sun.shadows=LightShadows.Soft;
                camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,light);
                try{
                    foreach(float zoom in new[]{0f,14f}){
                        camera.orthographic=zoom==0;camera.orthographicSize=(float)profile.CameraOrthoSize;
                        camera.transform.position=focus+(zoom==0?new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ):new Vector3(0,zoom*1.35f,-zoom*.72f));camera.transform.LookAt(focus);
                        Color32[] before=null,restrained=null;int renderers=-1,vertices=-1;
                        foreach(string variant in new[]{"baseline","restrained","restrained-nobloom","luminous"}){
                            world.Clear();world.MaterialFinish=variant=="baseline"?null:ActorMaterialProfile.Load(variant=="luminous"?"luminous":"restrained");
                            var hq=world.Building(1,"Headquarters",true);hq.Root.position=focus;world.FaceBuilding(hq,camera);
                            var ally=world.Tank(2,true);ally.Root.position=world.Point(new NavPoint(TerritoryRules.Sites(profile)[0].Position.X+5,TerritoryRules.Sites(profile)[0].Position.Z-4));
                            var enemy=world.Tank(3,false);enemy.Root.position=world.Point(new NavPoint(TerritoryRules.Sites(profile)[0].Position.X-5,TerritoryRules.Sites(profile)[0].Position.Z-4));
                            for(int i=0;i<2;i++)yield return null;
                            if(variant=="baseline")glow.enabled=false;else {glow.enabled=true;glow.Configure(world.MaterialFinish);if(variant=="restrained-nobloom"){glow.Profile.TryGet<Bloom>(out var disabledBloom);disabledBloom.intensity.Override(0);}}
                            for(int i=0;i<8;i++)yield return null;
                            int count=root.GetComponentsInChildren<Renderer>().Length,verts=root.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount);
                            if(renderers<0){renderers=count;vertices=verts;}else{Assert.AreEqual(renderers,count);Assert.AreEqual(vertices,verts);}
                            var image=Read(target);try{
                                var pixels=image.GetPixels32();Assert.Less(pixels.Count(c=>c.r>200&&c.b>200&&c.g<40),16,"No error pink");
                                if(variant=="baseline")before=pixels;else if(variant=="restrained"){restrained=pixels;Assert.Greater(Changed(before,pixels),100,"Material finish changes actual scene");}
                                else if(variant=="restrained-nobloom")Assert.Greater(Changed(restrained,pixels,0),10,"Existing lamps generate real bloom");
                                else Assert.Greater(Changed(restrained,pixels),30,"Two candidates visibly differ");
                                File.WriteAllBytes(Path.Combine(output,(foundry?"foundry":"crossings")+(zoom==0?"-gameplay":"-close")+"-"+variant+".png"),image.EncodeToPNG());
                            }finally{UnityEngine.Object.Destroy(image);}
                        }
                        File.AppendAllText(Path.Combine(output,"scenes.txt"),(foundry?"foundry":"crossings")+" zoom="+zoom+" renderers="+renderers+" vertices="+vertices+"; staged HQ and two tanks; no simulation seed; accepted lighting="+light.id+"@"+light.revision+"; profiles actor-industrial-finish-v1@1; Unity="+Application.unityVersion+" GPU="+SystemInfo.graphicsDeviceName+"\n");
                    }
                }finally{camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);world.Dispose();UnityEngine.Object.Destroy(root);}
                yield return null;
            }
        }
        [UnityTest] public IEnumerator CaptureExistingRoleGallery()
        {
            var root=new GameObject("Existing material role gallery");var world=new PlayableWorld(root.transform,PlayableProfile.Default);world.InspectionReveal();
            var target=new RenderTexture(1600,1000,24);target.Create();var camera=View(root.transform,target);camera.orthographic=true;camera.orthographicSize=8;
            camera.transform.position=new Vector3(0,14,-12);camera.transform.LookAt(new Vector3(0,1,0));var glow=camera.gameObject.AddComponent<ActorGlowCamera>();
            var sun=new GameObject("Accepted gallery sun").AddComponent<Light>();sun.transform.SetParent(root.transform);var lighting=IndustrialLightingProfile.Load();lighting.ApplyScene(sun);sun.shadows=LightShadows.Soft;camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            int count=-1,vertices=-1;Color32[] baseline=null;
            try{
                foreach(string variant in new[]{"baseline","restrained","luminous"}){
                    world.Clear();world.MaterialFinish=variant=="baseline"?null:ActorMaterialProfile.Load(variant);
                    string[] kinds={"Headquarters","ScientificCenter","Refinery","Mine"};
                    for(int i=0;i<kinds.Length;i++){var actor=world.Building(10+i,kinds[i],true);actor.Root.position=new Vector3((i%2==0?-1:1)*4,0,i<2?0:6);world.FaceBuilding(actor,camera);}
                    var units=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval};
                    for(int i=0;i<3;i++){var actor=world.Tank(20+i,true,units[i]);world.UpdateResearchModel(actor,new PlayableEntitySnapshot(20+i,PlayableOwner.Player,units[i],default,100,false,0,0,0,upgraded:true));actor.Root.position=new Vector3((i-1)*4,0,-5);actor.Hull.localRotation=Quaternion.Euler(0,150,0);}
                    yield return null;yield return null;if(variant=="baseline")glow.enabled=false;else{glow.enabled=true;glow.Configure(world.MaterialFinish);}
                    for(int frame=0;frame<8;frame++)yield return null;
                    int next=root.GetComponentsInChildren<Renderer>().Length,nextVertices=root.GetComponentsInChildren<MeshFilter>().Sum(x=>x.sharedMesh.vertexCount);
                    if(count<0){count=next;vertices=nextVertices;}else{Assert.AreEqual(count,next);Assert.AreEqual(vertices,nextVertices);}
                    var image=Read(target);try{if(variant=="baseline")baseline=image.GetPixels32();else Assert.Greater(Changed(baseline,image.GetPixels32()),100);File.WriteAllBytes(Path.Combine(Output(),"gallery-"+variant+".png"),image.EncodeToPNG());}finally{UnityEngine.Object.Destroy(image);}
                }
                File.AppendAllText(Path.Combine(Output(),"scenes.txt"),"gallery: existing HQ/science/refinery/mine and upgraded tank/explorer/shkval; no simulation seed; orthographic8; renderers="+count+" vertices="+vertices+" unchanged across variants\n");
            }finally{camera.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);world.Dispose();UnityEngine.Object.Destroy(root);}
        }
        [UnityTest] public IEnumerator CameraVolumesAreIsolatedAndHiddenActorsEmitNothing()
        {
            var root=new GameObject("Per-camera emission isolation");var world=new PlayableWorld(root.transform,PlayableProfile.Default);world.InspectionReveal();
            var actor=world.Building(1,"ScientificCenter",false);actor.Root.position=Vector3.zero;
            var targets=new[]{new RenderTexture(640,480,24),new RenderTexture(640,480,24)};foreach(var t in targets)t.Create();
            var cameras=new[]{View(root.transform,targets[0]),View(root.transform,targets[1])};var glows=new ActorGlowCamera[2];var retained=new VolumeProfile[2];
            var sun=new GameObject("Isolation sun").AddComponent<Light>();sun.transform.SetParent(root.transform);IndustrialLightingProfile.Load().ApplyScene(sun);
            try{
                for(int i=0;i<2;i++){cameras[i].transform.position=new Vector3(0,5,-5);cameras[i].transform.LookAt(Vector3.zero);glows[i]=cameras[i].gameObject.AddComponent<ActorGlowCamera>();glows[i].Configure(ActorMaterialProfile.Load(),ActorGlowCamera.FirstLocalVolumeLayer+i);retained[i]=glows[i].Profile;Assert.True(retained[i].TryGet<Tonemapping>(out var tone));Assert.AreEqual(TonemappingMode.None,tone.mode.value);Assert.AreEqual(1<<(30+i),cameras[i].GetUniversalAdditionalCameraData().volumeLayerMask.value);}
                for(int n=0;n<8;n++)yield return null;
                var a=Read(targets[0]);var b=Read(targets[1]);
                try{
                    glows[1].Profile.TryGet<Bloom>(out var bloom);bloom.intensity.Override(1);
                    for(int n=0;n<8;n++)yield return null;
                    var a2=Read(targets[0]);var b2=Read(targets[1]);try{Assert.Zero(Changed(a.GetPixels32(),a2.GetPixels32(),0),"Other camera bloom must not leak");Assert.Greater(Changed(b.GetPixels32(),b2.GetPixels32(),0),20,"Independent bloom must actually render");}finally{UnityEngine.Object.Destroy(a2);UnityEngine.Object.Destroy(b2);}
                }finally{UnityEngine.Object.Destroy(a);UnityEngine.Object.Destroy(b);}
                // Gameplay vision removes concealed enemy roots; verify zero glow survives a hidden renderer.
                actor.Root.gameObject.SetActive(false);for(int n=0;n<8;n++)yield return null;var hidden=Read(targets[1]);
                glows[1].Profile.TryGet<Bloom>(out var hiddenBloom);hiddenBloom.intensity.Override(0);for(int n=0;n<8;n++)yield return null;var noGlow=Read(targets[1]);
                try{Assert.Zero(Changed(hidden.GetPixels32(),noGlow.GetPixels32(),0),"Concealed enemy has no luminous silhouette");File.WriteAllBytes(Path.Combine(Output(),"concealed-enemy.png"),hidden.EncodeToPNG());}finally{UnityEngine.Object.Destroy(hidden);UnityEngine.Object.Destroy(noGlow);}
                glows[0].enabled=false;Assert.False(cameras[0].GetUniversalAdditionalCameraData().renderPostProcessing);Assert.AreEqual(1,cameras[0].GetUniversalAdditionalCameraData().volumeLayerMask.value);glows[0].enabled=true;Assert.True(cameras[0].GetUniversalAdditionalCameraData().renderPostProcessing);
                foreach(var g in glows)UnityEngine.Object.Destroy(g);yield return null;yield return null;
                foreach(var p in retained)Assert.True(p==null,"Owned volume profile disposed");
            }finally{for(int i=0;i<2;i++){cameras[i].targetTexture=null;targets[i].Release();UnityEngine.Object.Destroy(targets[i]);}world.Dispose();UnityEngine.Object.Destroy(root);}
        }
    }
}

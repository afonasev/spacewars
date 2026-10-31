#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed partial class ActorMaterialRenderingTests
    {
        private static string Json(object value)=>Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented);
        private static long Counter(ProfilerRecorder value)=>value.Valid&&value.Count>0?value.LastValue:-1;
        private static string F(double value)=>value.ToString("R",CultureInfo.InvariantCulture);

        private static IEnumerator CaptureInstancingPassesSafe(string output)
        {
            var capture=CaptureInstancingPasses(output);
            try
            {
                while(true)
                {
                    bool moved;object current=null;
                    try{moved=capture.MoveNext();if(moved)current=capture.Current;}
                    catch(Exception error){File.WriteAllText(output+"-status.txt","NotRun: installed internal FrameDebugger readout failed: "+error);break;}
                    if(!moved)break;yield return current;
                }
            }
            finally{(capture as IDisposable)?.Dispose();}
        }

        // Read the installed Editor's frame events without opening/focusing any window.
        // This runs AFTER timed frames and HDR readback, and always disables itself.
        private static IEnumerator CaptureInstancingPasses(string output)
        {
            var type=typeof(Editor).Assembly.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");
            if(type==null){File.WriteAllText(output+"-status.txt","NotRun: installed FrameDebuggerUtility unavailable");yield break;}
            const BindingFlags flags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
            var enable=type.GetMethod("SetEnabled",flags);var eventsMethod=type.GetMethod("GetFrameEvents",flags);
            var nameMethod=type.GetMethod("GetFrameEventInfoName",flags);var dataMethod=type.GetMethod("GetFrameEventData",flags);
            var limit=type.GetProperty("limit",flags);var supported=type.GetProperty("locallySupported",flags);
            if(enable==null||eventsMethod==null||nameMethod==null||dataMethod==null||limit==null||supported?.GetValue(null) is not true)
            {File.WriteAllText(output+"-status.txt","NotRun: installed FrameDebugger API unsupported");yield break;}
            try
            {
                enable.Invoke(null,new object[]{true,0});
                for(int i=0;i<4;i++)yield return null;
                var events=(Array)eventsMethod.Invoke(null,null);var text=new System.Text.StringBuilder("index\ttype\tname\tobject\n");
                var instanced=new List<int>();var meshes=new List<int>();
                for(int i=0;i<events.Length;i++)
                {
                    var entry=events.GetValue(i);var entryType=entry.GetType();string kind=entryType.GetField("m_Type").GetValue(entry).ToString();
                    var obj=entryType.GetField("m_Obj").GetValue(entry) as UnityEngine.Object;
                    text.AppendLine(i+"\t"+kind+"\t"+nameMethod.Invoke(null,new object[]{i})+"\t"+(obj?obj.name:""));
                    if(kind=="InstancedMesh")instanced.Add(i);if(kind=="Mesh")meshes.Add(i);
                }
                File.WriteAllText(output+"-events.tsv",text.ToString());
                var selected=new SortedSet<int>();
                foreach(var list in new[]{instanced,meshes})for(int i=0;i<Math.Min(8,list.Count);i++)selected.Add(list[i*(list.Count-1)/Math.Max(1,Math.Min(8,list.Count)-1)]);
                var samples=new List<object>();var dataType=dataMethod.GetParameters()[1].ParameterType;
                foreach(int index in selected)
                {
                    limit.SetValue(null,index+1);for(int i=0;i<3;i++)yield return null;
                    var data=Activator.CreateInstance(dataType);bool fresh=(bool)dataMethod.Invoke(null,new object[]{index,data});
                    var fields=new Dictionary<string,object>();
                    foreach(string name in new[]{"m_FrameEventIndex","m_InstanceCount","m_DrawCallCount","m_PassName","m_PassLightMode","m_OriginalShaderName","m_RealShaderName","m_VertexCount","m_IndexCount","m_BatchBreakCause","m_RenderTargetName"})
                    {var field=dataType.GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);if(field!=null)fields.Add(name,field.GetValue(data));}
                    samples.Add(new{index,fresh,fields});
                }
                File.WriteAllText(output+"-samples.json",Json(new{eventCount=events.Length,instancedEvents=instanced.Count,meshEvents=meshes.Count,samples,limits="Selected events only; fresh=false is unavailable, never inferred instance/pass data"}));
            }
            finally{enable.Invoke(null,new object[]{false,0});}
            for(int i=0;i<4;i++)yield return null;
        }

        [UnityTest,Explicit("ONE cycle61 frozen400 actual submission/cost diagnostic; strict pixel FAIL stays visible; exclusive quiet")]
        public IEnumerator FourHundredOriginalBlocksReportInstancedLitCost()
        {
            Assert.AreNotEqual(GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);
            string output=Environment.GetEnvironmentVariable("SPACEWARS_MATERIAL_BATCH_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var data=Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text);
            var baseline=PlayableProfile.Create(data,new AiTestMap());
            var store=new NativeBalanceStore(baseline,Path.Combine(output,"isolated-native-balance.json"),false);
            store.Select(NativeBalanceStore.NewMatchProfileId,NativeBalanceStore.NewMatchProfileVersion);
            var profile=store.Active;Assert.AreEqual(2,profile.Revision);Assert.IsInstanceOf<AiTestMap>(profile.AuthoredMap);
            var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            PlayableSnapshot view;
            using(var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,1,setup,startPaused:true,aiProfile:store.ActiveAiProfile))
            {view=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;Assert.AreEqual(400,runtime.DiagnosticGlobalUnits);}
            Assert.AreEqual(0,view.Tick);Assert.AreEqual(400,view.Entities.Count);Assert.AreEqual(8,view.Participants.Count);
            foreach(var group in view.Entities.GroupBy(e=>e.Owner)){Assert.AreEqual(50,group.Count());Assert.AreEqual(49,group.Count(e=>e.Kind==PlayableEntityKind.Tank));Assert.AreEqual(1,group.Count(e=>e.Kind==PlayableEntityKind.Explorer));}
            File.WriteAllText(Path.Combine(output,"bound-profile.json"),Json(profile.CopyData()));
            File.WriteAllText(Path.Combine(output,"bound-ai-profile.json"),Json(store.ActiveAiProfile.CopyData()));
            File.WriteAllText(Path.Combine(output,"genesis-units.json"),Json(view.Entities.Select(e=>new{e.Id,e.Owner,e.Kind,e.Position,e.HullHeading,e.TurretHeading,e.Upgraded})));
            var priorDetail=ModelDetailProfile.Current;ModelDetailProfile.UseSession(store.ActiveModelDetail);
            // Match the recorded58 pacing in process memory, without writing preferences.
            int priorVsync=QualitySettings.vSyncCount,priorFrameRate=Application.targetFrameRate;
            QualitySettings.vSyncCount=1;Application.targetFrameRate=60;
            var root=new GameObject("Cycle61 packaged400 frozen presentation");
            var target=new RenderTexture(3456,2160,24,RenderTextureFormat.DefaultHDR){antiAliasing=4};target.Create();
            var camera=View(root.transform,target);camera.orthographic=true;camera.orthographicSize=(float)profile.ArenaHalfExtent;camera.allowHDR=true;
            camera.transform.position=new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ)*4;
            camera.farClipPlane=(float)profile.ArenaHalfExtent*12;camera.transform.LookAt(Vector3.zero);camera.backgroundColor=new Color(.045f,.075f,.10f);
            var lighting=IndustrialLightingProfile.Load(profile.AuthoredMap.Id);var sun=new GameObject("Original match light").AddComponent<Light>();sun.transform.SetParent(root.transform);lighting.ApplyScene(sun);
            var world=new PlayableWorld(root.transform,profile);
            camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());
            camera.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
            camera.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
            camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            // Match StartSessionCore binds graphics BEFORE its first Render creates actors.
            world.ApplyGraphics(ActorMaterialProfile.Load(),lighting);world.Fog.Update(view.Vision,0);
            var states=new List<InstancedLitMaterialState>();var bindings=new List<MaterialControlBinding>();
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Spacewars/Tests/Fixtures/InstancedActorLit/Cycle61Lit.shader");Assert.NotNull(shader);
            var names=new[]{"Draw Calls Count","Batches Count","SetPass Calls Count","Triangles Count","Main Thread","Render Thread"};
            var counters=names.Select((name,i)=>ProfilerRecorder.StartNew(i<4?ProfilerCategory.Render:ProfilerCategory.Internal,name,1)).ToArray();
            var timing=new FrameTiming[1];var rows=new System.Text.StringBuilder("phase,sample,frame,draws,batches,setPass,triangles,mainNs,renderNs,instancedBatches,shadowCasters,gpuMs,timingStamp\n");
            var hdr=new byte[6][];var summaries=new List<object>();
            try
            {
                foreach(var e in view.Entities)
                {
                    var actor=world.Tank(e.Id,e.Owner==PlayableOwner.Player,e.Kind);world.UpdateResearchModel(actor,e);actor.Root.position=world.Point(e.Position);
                    actor.Hull.localRotation=Quaternion.Euler(0,90-(float)e.HullHeading*Mathf.Rad2Deg,0);actor.Turret.localRotation=Quaternion.Euler(0,(float)(e.HullHeading-e.TurretHeading)*Mathf.Rad2Deg,0);
                    ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[(int)e.Owner],out var owner);PlayableWorld.PaintOwner(actor,owner);PlayableWorld.UpdateHealth(actor,camera,(float)PlayableUnitRules.HealthRatio(profile,e));world.AimExplorer(actor,e);
                }
                foreach(var b in view.Buildings)
                {
                    if(b.Phase==ConstructionPhase.Pending)continue;
                    var actor=world.Building(b.Id,b.Kind.ToString(),b.Owner==PlayableOwner.Player,b.RefineryUpgraded);world.UpdateRefineryModel(actor,b,view.Tick);world.UpdateFactoryModel(actor,b,view);
                    actor.Root.position=world.Point(b.Position);actor.Root.rotation=Quaternion.Euler(0,-(float)b.Heading*Mathf.Rad2Deg,0);world.FaceBuilding(actor,camera);actor.Root.localScale=new Vector3(1,(float)BuildingCombatGeometry.ConstructionHeight(b.Progress),1);
                    ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[(int)b.Owner],out var owner);PlayableWorld.PaintOwner(actor,owner);PlayableWorld.UpdateHealth(actor,camera,(float)(b.Health/TerritoryRules.MaximumHealth(profile,b)));
                }
                world.RenderSites(view,camera);world.RenderMemories(view,camera);world.SelectEnvironmentDetail(camera);
                for(int warm=0;warm<4;warm++)yield return null;
                foreach(var e in view.Entities)foreach(var renderer in world.Actors[e.Id].Hull.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var materials=renderer.sharedMaterials;if(materials.Any(m=>m.shader.name!="Universal Render Pipeline/Lit"))continue;
                    var blocks=new MaterialPropertyBlock[materials.Length];
                    for(int slot=0;slot<materials.Length;slot++)
                    {
                        blocks[slot]=new MaterialPropertyBlock();renderer.GetPropertyBlock(blocks[slot],slot);var m=materials[slot];
                        if(states.All(s=>s.Material!=m))states.Add(new InstancedLitMaterialState{Material=m,Shader=m.shader,Keywords=m.shaderKeywords,Instancing=m.enableInstancing,Queue=m.rawRenderQueue});
                    }
                    bindings.Add(new MaterialControlBinding{Renderer=renderer,Materials=materials,Blocks=blocks});
                }
                var matrices=bindings.Select(b=>b.Renderer.localToWorldMatrix).ToArray();var meshes=bindings.Select(b=>b.Renderer.GetComponent<MeshFilter>().sharedMesh).ToArray();
                var visible=bindings.Select(b=>b.Renderer.enabled&&b.Renderer.gameObject.activeInHierarchy).ToArray();var shadowState=bindings.Select(b=>(b.Renderer.shadowCastingMode,b.Renderer.receiveShadows)).ToArray();
                var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
                Assert.AreEqual(5,QualitySettings.GetQualityLevel());Assert.AreEqual(4,pipeline.msaaSampleCount);Assert.AreEqual(1,pipeline.renderScale);Assert.True(pipeline.supportsHDR);Assert.True(NativeDisplaySettings.Shadows);Assert.True(NativeUserSettings.AlwaysHealth);
                File.WriteAllText(Path.Combine(output,"render-binding.json"),Json(new{unity=Application.unityVersion,api=SystemInfo.graphicsDeviceType.ToString(),gpu=SystemInfo.graphicsDeviceName,width=target.width,height=target.height,quality=QualitySettings.GetQualityLevel(),vsync=QualitySettings.vSyncCount,targetFrameRate=Application.targetFrameRate,pipeline=pipeline.name,renderScale=pipeline.renderScale,msaa=pipeline.msaaSampleCount,hdr=pipeline.supportsHDR,shadowResolution=pipeline.mainLightShadowmapResolution,shadows=NativeDisplaySettings.Shadows,alwaysHealth=NativeUserSettings.AlwaysHealth,cameraSize=camera.orthographicSize,cameraPosition=new[]{camera.transform.position.x,camera.transform.position.y,camera.transform.position.z},cameraRotation=new[]{camera.transform.rotation.x,camera.transform.rotation.y,camera.transform.rotation.z,camera.transform.rotation.w},near=camera.nearClipPlane,far=camera.farClipPlane,aspect=camera.aspect,profile=profile.Revision,map=profile.AuthoredMap.Id,units=view.Entities.Count,buildings=view.Buildings.Count,renderers=root.GetComponentsInChildren<Renderer>(true).Length,changedRenderers=bindings.Count,sourceMaterialFlags=states.Select(s=>new{s.Material.name,s.Instancing,s.Queue}),frameTimingFeature=FrameTimingManager.IsFeatureEnabled(),limits="Frozen actual400 genesis; no moving simulation/HUD/ordinaryFPS acceptance. Metrics exclude setup/HDR readback/FrameDebugger. Original material flags and MPBs preserved."}));
                for(int phase=0;phase<6;phase++)
                {
                    foreach(var s in states)
                    {
                        s.Restore();if(phase==2||phase==3){s.Material.shader=shader;s.Material.shaderKeywords=s.Keywords;s.Material.enableInstancing=s.Instancing;s.Material.renderQueue=s.Queue;}
                    }
                    for(int warm=0;warm<12;warm++)yield return null;
                    int minInstanced=int.MaxValue,maxInstanced=-1,minShadow=int.MaxValue,maxShadow=-1;
                    for(int sample=0;sample<48;sample++)
                    {
                        FrameTimingManager.CaptureFrameTimings();yield return null;
                        uint available=FrameTimingManager.GetLatestTimings(1,timing);double gpu=available>0&&timing[0].gpuFrameTime>0?timing[0].gpuFrameTime:-1;ulong stamp=available>0?timing[0].frameStartTimestamp:0;
                        int instances=UnityStats.instancedBatches,shadows=UnityStats.shadowCasters;minInstanced=Math.Min(minInstanced,instances);maxInstanced=Math.Max(maxInstanced,instances);minShadow=Math.Min(minShadow,shadows);maxShadow=Math.Max(maxShadow,shadows);
                        rows.AppendLine(phase+","+sample+","+Time.frameCount+","+string.Join(",",counters.Select(Counter))+","+instances+","+shadows+","+F(gpu)+","+stamp);
                    }
                    File.WriteAllText(Path.Combine(output,"samples.csv"),rows.ToString());
                    hdr[phase]=MaterialControlHdr(target);File.WriteAllBytes(Path.Combine(output,$"phase-{phase}-rgba-f32.bin"),hdr[phase]);
                    var png=Read(target);try{File.WriteAllBytes(Path.Combine(output,$"phase-{phase}.png"),png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}
                    summaries.Add(new{phase,minInstanced,maxInstanced,minShadow,maxShadow,hdrSha256=MaterialControlHash(hdr[phase])});File.WriteAllText(Path.Combine(output,"phases.json"),Json(summaries));
                    for(int i=0;i<bindings.Count;i++)
                    {
                        var b=bindings[i];CollectionAssert.AreEqual(b.Materials,b.Renderer.sharedMaterials);Assert.AreEqual(matrices[i],b.Renderer.localToWorldMatrix);Assert.AreSame(meshes[i],b.Renderer.GetComponent<MeshFilter>().sharedMesh);Assert.AreEqual(visible[i],b.Renderer.enabled&&b.Renderer.gameObject.activeInHierarchy);Assert.AreEqual(shadowState[i],(b.Renderer.shadowCastingMode,b.Renderer.receiveShadows));
                        for(int slot=0;slot<b.Blocks.Length;slot++){var current=new MaterialPropertyBlock();b.Renderer.GetPropertyBlock(current,slot);Assert.AreEqual(MaterialControlKey(b.Materials[slot],b.Blocks[slot]),MaterialControlKey(b.Materials[slot],current));}
                    }
                    // One pass/event capture per A/P/A state, outside the measured samples.
                    if(phase==1||phase==3||phase==5)yield return CaptureInstancingPassesSafe(Path.Combine(output,$"phase-{phase}-frame-debugger"));
                }
                var comparisons=new System.Text.StringBuilder();foreach(var pair in new[]{(0,1),(2,3),(4,5),(0,4),(0,2)})comparisons.AppendLine(OriginalHdrDifference(hdr[pair.Item1],hdr[pair.Item2],target.width,$"phase{pair.Item1}-vs-{pair.Item2}"));File.WriteAllText(Path.Combine(output,"strict-hdr.txt"),comparisons.ToString());
                Assert.True(hdr[0].SequenceEqual(hdr[1]),"400 original A-A exact stability");Assert.True(hdr[2].SequenceEqual(hdr[3]),"400 prototype P-P exact stability");Assert.True(hdr[4].SequenceEqual(hdr[5]),"400 restored A-A exact stability");Assert.True(hdr[0].SequenceEqual(hdr[4]),"400 restored original A exact HDR");Assert.True(hdr[0].SequenceEqual(hdr[2]),"400 prototype with original flags must preserve exact HDR; diagnostic metrics never waive this FAIL");
            }
            finally
            {
                foreach(var s in states)s.Restore();foreach(var recorder in counters)recorder.Dispose();camera.targetTexture=null;world.Dispose();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(target);ModelDetailProfile.UseSession(priorDetail);QualitySettings.vSyncCount=priorVsync;Application.targetFrameRate=priorFrameRate;
            }
        }
    }
}
#endif

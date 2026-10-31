#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        // Cycle65 only: the exact installed native bindings, never FrameDebuggerWindow.
        private sealed class NativeBatchApi65
        {
            internal const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
            internal const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            internal readonly Type Utility,Data,Runtime;
            internal readonly int ConnectedProfiler;
            internal readonly string[] Causes;
            internal static Type Find(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null);
            internal NativeBatchApi65()
            {
                Utility=Find("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");Data=Find("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData");Runtime=Find("UnityEngine.FrameDebugger");
                if(Utility==null||Data==null||Runtime==null)throw new InvalidOperationException("Installed native binding missing");
                ConnectedProfiler=(int)Find("UnityEditorInternal.ProfilerDriver").GetProperty("connectedProfiler",Static).GetValue(null);
                Causes=(string[])Call("GetBatchBreakCauseStrings");
            }
            internal object Call(string method,params object[] values)=>Utility.GetMethod(method,Static).Invoke(null,values);
            internal object Property(string name)=>Utility.GetProperty(name,Static).GetValue(null);
            internal bool Supported=>(bool)Property("locallySupported");
            internal bool Enabled=>(bool)Runtime.GetProperty("enabled",Static).GetValue(null);
            internal int Hash=>(int)Property("eventsHash");
            internal int Limit{get=>(int)Property("limit");set=>Utility.GetProperty("limit",Static).SetValue(null,value);}
            internal Array Events()=>(Array)Call("GetFrameEvents");
            internal void Enable(bool enabled)=>Call("SetEnabled",enabled,ConnectedProfiler);
            internal object Read(int index,out bool fresh)
            {
                var data=Activator.CreateInstance(Data,true);
                // An unwritten default index0 must never masquerade as fresh native event0.
                Data.GetField("m_FrameEventIndex",Instance).SetValue(data,-1);
                fresh=(bool)Call("GetFrameEventData",index,data);return data;
            }
            internal object Field(object data,string name)=>Data.GetField(name,Instance)?.GetValue(data);
        }
        private sealed class NativeBatchCapture65
        {
            public string label,status="NotRun";public int eventCount,candidates,freshEvents,exactActorEvents,staleEvents;
            public List<object> events=new(),details=new();public string[] causeStrings;
        }
        private static int NativeObjectId65(UnityEngine.Object value)=>value?value.GetInstanceID():0;
        private static string NativePass65(string name,string lightMode)
        {
            string both=(name+" "+lightMode).ToLowerInvariant();
            if(both.Contains("shadowcaster"))return "Shadow";
            if(both.Contains("gbuffer"))return "GBuffer";
            if(both.Contains("forward"))return "Forward";
            if(both.Contains("depth"))return "Depth";
            if(both.Contains("motion"))return "Motion";
            return "Other";
        }
        private static IEnumerator CaptureNativeBatchEvents65(NativeBatchApi65 api,string output,NativeBatchCapture65 capture,Renderer[] actorRenderers)
        {
            Assert.False(api.Enabled,"Never replace an existing native debugger session");
            capture.causeStrings=api.Causes;
            var rendererIds=actorRenderers.ToDictionary(r=>r.GetInstanceID());
            try
            {
                api.Enable(true);
                Array events=null;
                for(int wait=0;wait<8;wait++){EditorApplication.QueuePlayerLoopUpdate();yield return null;events=api.Events();if(wait>=3&&events.Length>0)break;}
                if(events==null||events.Length==0){capture.status="NotRun: no native frame events without UI; no window/focus workaround";yield break;}
                capture.eventCount=events.Length;int frameHash=api.Hash;
                var selected=new List<int>();var entryObjects=new Dictionary<int,UnityEngine.Object>();
                for(int index=0;index<events.Length;index++)
                {
                    var entry=events.GetValue(index);var type=entry.GetType();string kind=type.GetField("m_Type").GetValue(entry).ToString();
                    var obj=type.GetField("m_Obj").GetValue(entry) as UnityEngine.Object;var nativeObj=api.Call("GetFrameEventObject",index) as UnityEngine.Object;
                    entryObjects[index]=nativeObj?nativeObj:obj;
                    string name=(string)api.Call("GetFrameEventInfoName",index);
                    capture.events.Add(new{index,kind,name,entryObjectId=NativeObjectId65(obj),entryObjectName=obj?obj.name:null,entryObjectType=obj?obj.GetType().FullName:null,nativeObjectId=NativeObjectId65(nativeObj),nativeObjectName=nativeObj?nativeObj.name:null,nativeObjectType=nativeObj?nativeObj.GetType().FullName:null});
                    if(kind.Contains("Mesh")||kind.Contains("Batch"))selected.Add(index);
                }
                capture.candidates=selected.Count;
                File.WriteAllText(Path.Combine(output,"native-"+capture.label+".json"),Json(capture));
                if(selected.Count>512){capture.status="NotRun: small capture exceeds512actualmesh/batch-event bound";yield break;}
                foreach(int index in selected)
                {
                    if(api.Hash!=frameHash||api.Events().Length!=events.Length){capture.status="NotRun: native event-list identity drift; do not reuse stale indexes";yield break;}
                    api.Limit=index+1;
                    object data=null;bool fresh=false;
                    for(int attempt=0;attempt<6&&!fresh;attempt++){EditorApplication.QueuePlayerLoopUpdate();yield return null;data=api.Read(index,out fresh);}
                    if(!fresh){capture.staleEvents++;capture.details.Add(new{index,fresh=false,status="NotRun: native selected-event data unavailable"});continue;}
                    if(api.Hash!=frameHash){capture.status="NotRun: native event hash changed during selected-data read";yield break;}
                    capture.freshEvents++;
                    var fields=new Dictionary<string,object>();
                    foreach(string field in new[]{"m_FrameEventIndex","m_InstanceCount","m_DrawCallCount","m_PassName","m_PassLightMode","m_OriginalShaderName","m_RealShaderName","m_ShaderInstanceID","m_SubShaderIndex","m_ShaderPassIndex","shaderKeywords","m_VertexCount","m_IndexCount","m_ComponentInstanceID","m_MeshInstanceID","m_MeshSubset","m_MeshInstanceIDs","m_BatchBreakCause","m_RenderTargetName"})fields[field]=api.Field(data,field);
                    int component=fields["m_ComponentInstanceID"] is int id?id:0;int meshId=fields["m_MeshInstanceID"] is int mid?mid:0;
                    var obj=entryObjects[index];Renderer renderer=null;
                    if(rendererIds.TryGetValue(component,out var componentRenderer))renderer=componentRenderer;
                    else if(obj is Renderer direct&&rendererIds.ContainsKey(direct.GetInstanceID()))renderer=direct;
                    else if(obj is GameObject go){var matches=actorRenderers.Where(r=>r.gameObject==go).ToArray();if(matches.Length==1)renderer=matches[0];}
                    else if(obj is Component c){var matches=actorRenderers.Where(r=>r.gameObject==c.gameObject).ToArray();if(matches.Length==1)renderer=matches[0];}
                    if(renderer)capture.exactActorEvents++;
                    int code=fields["m_BatchBreakCause"] is int cause?cause:-1;
                    capture.details.Add(new{index,fresh=true,fields,passClass=NativePass65(fields["m_PassName"] as string,fields["m_PassLightMode"] as string),causeCode=code,causeString=code>=0&&code<api.Causes.Length?api.Causes[code]:null,
                        actorRendererId=NativeObjectId65(renderer),actorRendererName=renderer?renderer.name:null,
                        materialIdsFromExactRendererBinding=renderer?renderer.sharedMaterials.Select(m=>m.GetInstanceID()).ToArray():null,
                        meshOnlyCandidates=actorRenderers.Where(r=>r.GetComponent<MeshFilter>().sharedMesh.GetInstanceID()==meshId).Select(r=>r.GetInstanceID()).ToArray(),
                        attribution=renderer?"Exact native component/object to actual actor renderer; material IDs from that binding":"Native fields only; mesh matches ambiguous, actor/material attribution NotRun"});
                    File.WriteAllText(Path.Combine(output,"native-"+capture.label+".json"),Json(capture));
                }
                capture.status=capture.freshEvents>0?"Captured actual native events; unavailable attribution/fresh rows remain NotRun":"NotRun: no fresh native event data without UI";
            }
            finally{if(api.Enabled)api.Enable(false);File.WriteAllText(Path.Combine(output,"native-"+capture.label+".json"),Json(capture));}
            for(int wait=0;wait<4;wait++)yield return null;
        }

        [UnityTest,Explicit("Cycle65 ONE small originalA/currentP native batch-break capture, force-gfx-mt/exclusive/no UI")]
        public IEnumerator OriginalBlocksReportNativeBatchBreakEvents()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_MATERIAL_BATCH_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var api=new NativeBatchApi65();File.WriteAllText(Path.Combine(output,"native-preflight.json"),Json(new{unity=Application.unityVersion,device=SystemInfo.graphicsDeviceType.ToString(),supported=api.Supported,enabled=api.Enabled,api.ConnectedProfiler,remoteGuid=api.Call("GetRemotePlayerGUID"),nativeCount=api.Property("count"),causes=api.Causes,utilityAssembly=api.Utility.Assembly.FullName}));
            Assert.True(api.Supported,"Native API unsupported: no new scene admitted");Assert.False(api.Enabled,"Foreign native session: no scene admitted");
            var baseline=PlayableProfile.Create(Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new AiTestMap());
            var store=new NativeBalanceStore(baseline,Path.Combine(output,"isolated-native-balance.json"),false);store.Select(NativeBalanceStore.NewMatchProfileId,NativeBalanceStore.NewMatchProfileVersion);var profile=store.Active;
            var priorDetail=ModelDetailProfile.Current;ModelDetailProfile.UseSession(store.ActiveModelDetail);
            var root=new GameObject("Cycle65 native batching evidence");var target=new RenderTexture(768,512,24,RenderTextureFormat.DefaultHDR){antiAliasing=4};target.Create();
            var camera=View(root.transform,target);camera.orthographic=true;camera.orthographicSize=5;camera.allowHDR=true;camera.backgroundColor=new Color(.045f,.075f,.10f);camera.transform.position=new Vector3(0,7,-10);camera.transform.LookAt(Vector3.zero);
            var lighting=IndustrialLightingProfile.Load(profile.AuthoredMap.Id);var sun=new GameObject("Native profile directional light").AddComponent<Light>();sun.transform.SetParent(root.transform);lighting.ApplyScene(sun);
            var world=new PlayableWorld(root.transform,profile);world.ApplyGraphics(ActorMaterialProfile.Load(),lighting);world.InspectionReveal();
            camera.gameObject.AddComponent<ActorGlowCamera>().Configure(world.MaterialFinish);camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());camera.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Explorer};var actors=kinds.Select((kind,i)=>world.Tank(i+1,true,kind)).ToArray();
            var states=new List<InstancedLitMaterialState>();var bindings=new List<MaterialControlBinding>();var captures=new List<NativeBatchCapture65>();var sourceHashes=new Dictionary<string,string>();var frames=new byte[3][];
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Spacewars/Tests/Fixtures/InstancedActorLit/Cycle61Lit.shader");Assert.NotNull(shader);
            bool originalSrp=GraphicsSettings.useScriptableRenderPipelineBatching;string originalTag=Shader.globalRenderPipeline;
            void Remember(UnityEngine.Object asset){string assetPath=AssetDatabase.GetAssetPath(asset);if(string.IsNullOrEmpty(assetPath))return;string path=Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));if(File.Exists(path))sourceHashes[path]=MaterialControlHash(File.ReadAllBytes(path));}
            try
            {
                for(int i=0;i<actors.Length;i++){actors[i].Root.position=new Vector3(i%2==0?-2:2,0,i<2?-1.5f:1.5f);ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[i<2?0:4],out var owner);PlayableWorld.PaintOwner(actors[i],owner);PlayableWorld.UpdateHealth(actors[i],camera,1,true);}
                for(int warm=0;warm<8;warm++)yield return null;
                var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;originalSrp=GraphicsSettings.useScriptableRenderPipelineBatching;originalTag=Shader.globalRenderPipeline;Assert.True(originalSrp);Assert.AreEqual("UniversalPipeline",originalTag);Assert.AreSame(sun,RenderSettings.sun);Remember(pipeline);Remember(shader);
                var renderers=actors.SelectMany(a=>a.Hull.GetComponentsInChildren<MeshRenderer>(true)).Cast<Renderer>().ToArray();var matrices=renderers.Select(r=>r.localToWorldMatrix).ToArray();var meshes=renderers.Select(r=>r.GetComponent<MeshFilter>().sharedMesh).ToArray();var materials=renderers.Select(r=>r.sharedMaterials).ToArray();var shadows=renderers.Select(r=>(r.shadowCastingMode,r.receiveShadows)).ToArray();var visible=renderers.Select(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
                foreach(var renderer in renderers)
                {
                    foreach(var material in renderer.sharedMaterials)Remember(material);Remember(renderer.GetComponent<MeshFilter>().sharedMesh);
                    if(renderer.sharedMaterials.Any(m=>m.shader.name!="Universal Render Pipeline/Lit"))continue;
                    Assert.AreEqual(1,renderer.sharedMaterials.Length);var m=renderer.sharedMaterials[0];var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,0);var global=new MaterialPropertyBlock();renderer.GetPropertyBlock(global);Assert.True(global.isEmpty);
                    bindings.Add(new MaterialControlBinding{Renderer=renderer,Materials=renderer.sharedMaterials,Blocks=new[]{block}});
                    if(states.All(s=>s.Material!=m))states.Add(new InstancedLitMaterialState{Material=m,Shader=m.shader,Keywords=m.shaderKeywords,Instancing=m.enableInstancing,Queue=m.rawRenderQueue});
                }
                Func<string> invariant=()=>Json(new{profile.Revision,map=profile.AuthoredMap.Id,quality=QualitySettings.GetQualityLevel(),aa=QualitySettings.antiAliasing,pipeline.msaaSampleCount,pipeline.renderScale,pipeline.useSRPBatcher,pipeline.supportsDynamicBatching,camera.allowHDR,target.antiAliasing,target.sRGB,sun=RenderSettings.sun?RenderSettings.sun.GetInstanceID():0,sun.intensity,color=Components(sun.color),ambient=Components(RenderSettings.ambientLight),ambientMode=RenderSettings.ambientMode.ToString(),RenderSettings.ambientIntensity});
                string before=invariant();File.WriteAllText(Path.Combine(output,"bindings.json"),Json(new{before,originalTag,originalSrp,profile.Revision,modelDetail=store.ActiveModelDetail,renderers=renderers.Select(r=>new{id=r.GetInstanceID(),r.name,gameObjectId=r.gameObject.GetInstanceID(),meshId=r.GetComponent<MeshFilter>().sharedMesh.GetInstanceID(),mesh=AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh),materials=r.sharedMaterials.Select(m=>new{id=m.GetInstanceID(),m.name,path=AssetDatabase.GetAssetPath(m),shader=m.shader.name,m.enableInstancing}),block=InventoryBlock(GetBlock65(r),r.sharedMaterials[0])})}));
                for(int phase=0;phase<3;phase++)
                {
                    foreach(var s in states){s.Restore();if(phase==1){s.Material.shader=shader;s.Material.shaderKeywords=s.Keywords;s.Material.enableInstancing=s.Instancing;s.Material.renderQueue=s.Queue;}}
                    for(int warm=0;warm<8;warm++)yield return null;
                    Assert.AreEqual(before,invariant());Assert.AreEqual(originalSrp,GraphicsSettings.useScriptableRenderPipelineBatching);Assert.AreEqual(originalTag,Shader.globalRenderPipeline);
                    foreach(var s in states){Assert.AreEqual(0,ShaderUtil.GetShaderData(s.Material.shader).ActiveSubshaderIndex);Assert.AreEqual(9,s.Material.passCount);Assert.AreEqual(s.Instancing,s.Material.enableInstancing);}
                    frames[phase]=MaterialControlHdr(target);File.WriteAllBytes(Path.Combine(output,$"phase-{phase}-rgba-f32.bin"),frames[phase]);var png=ReadSrgbDisplay(target);try{File.WriteAllBytes(Path.Combine(output,$"phase-{phase}-srgb.png"),png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}
                    if(phase<2){var capture=new NativeBatchCapture65{label=phase==0?"A":"P"};captures.Add(capture);yield return CaptureNativeBatchEvents65(api,output,capture,renderers);}
                    Assert.False(api.Enabled);
                    for(int i=0;i<renderers.Length;i++){CollectionAssert.AreEqual(materials[i],renderers[i].sharedMaterials);Assert.AreEqual(matrices[i],renderers[i].localToWorldMatrix);Assert.AreSame(meshes[i],renderers[i].GetComponent<MeshFilter>().sharedMesh);Assert.AreEqual(shadows[i],(renderers[i].shadowCastingMode,renderers[i].receiveShadows));Assert.AreEqual(visible[i],renderers[i].enabled&&renderers[i].gameObject.activeInHierarchy);}
                    foreach(var b in bindings){var global=new MaterialPropertyBlock();b.Renderer.GetPropertyBlock(global);Assert.True(global.isEmpty);Assert.AreEqual(MaterialControlKey(b.Materials[0],b.Blocks[0]),MaterialControlKey(b.Materials[0],GetBlock65(b.Renderer)),"Original indexed MPB bits unchanged");}
                }
                File.WriteAllText(Path.Combine(output,"strict-hdr.txt"),OriginalHdrDifference(frames[0],frames[1],target.width,"A-vs-P")+"\n"+OriginalHdrDifference(frames[0],frames[2],target.width,"A-vs-restoredA"));
                Assert.True(frames[0].SequenceEqual(frames[2]),"Original source restoration must preserve exact full HDR");
                Assert.True(frames[0].SequenceEqual(frames[1]),"A-P strict HDR remains independent, no tolerance");
                Assert.True(captures.Count==2&&captures.All(c=>c.freshEvents>0&&c.exactActorEvents>0),"Native actor/pass attribution incomplete; preserve NotRun fields");
            }
            finally
            {
                if(api.Enabled)api.Enable(false);foreach(var s in states)s.Restore();
                var restored=sourceHashes.ToDictionary(k=>k.Key,k=>new{before=k.Value,after=MaterialControlHash(File.ReadAllBytes(k.Key))});
                File.WriteAllText(Path.Combine(output,"restoration.json"),Json(new{debuggerEnabled=api.Enabled,originalSrp,currentSrp=GraphicsSettings.useScriptableRenderPipelineBatching,originalTag,currentTag=Shader.globalRenderPipeline,sourceHashes=restored}));
                camera.targetTexture=null;world.Dispose();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(target);ModelDetailProfile.UseSession(priorDetail);
                Assert.False(api.Enabled);Assert.True(restored.All(k=>k.Value.before==k.Value.after));
            }
        }
        private static MaterialPropertyBlock GetBlock65(Renderer renderer){var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,0);return block;}
    }
}
#endif

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
        private static bool UniformHdr67(byte[] bytes)
        {for(int i=16;i<bytes.Length;i++)if(bytes[i]!=bytes[i%16])return false;return true;}
        private static NativeBatchCapture65 ReadPublicRequestEvents67(NativeBatchApi65 api,Array events,Renderer[] renderers,string output,string label)
        {
            var capture=new NativeBatchCapture65 {label=label,eventCount=events.Length,causeStrings=api.Causes};int hash=api.Hash;
            var actorIds=renderers.ToDictionary(r=>r.GetInstanceID());
            for(int index=0;index<events.Length;index++)
            {
                var entry=events.GetValue(index);var type=entry.GetType();string kind=type.GetField("m_Type").GetValue(entry).ToString();
                var obj=api.Call("GetFrameEventObject",index) as UnityEngine.Object;
                capture.events.Add(new{index,kind,name=api.Call("GetFrameEventInfoName",index),objectId=NativeObjectId65(obj),objectName=obj?obj.name:null,objectType=obj?obj.GetType().FullName:null});
                if(!kind.Contains("Mesh")&&!kind.Contains("Batch"))continue;
                capture.candidates++;if(capture.candidates>512){capture.status="NotRun:512actualdraw-event bound";break;}
                if(api.Hash!=hash||api.Events().Length!=events.Length){capture.status="NotRun:event-list identity drift";break;}
                api.Limit=index+1;bool fresh;var data=api.Read(index,out fresh);
                if(!fresh){capture.staleEvents++;capture.details.Add(new{index,fresh=false,status="NotRun:no fresh synchronous selected-event data; no extra producer request"});continue;}
                if(api.Hash!=hash){capture.status="NotRun:event hash changed";break;}
                capture.freshEvents++;var fields=new Dictionary<string,object>();
                foreach(string field in new[]{"m_FrameEventIndex","m_InstanceCount","m_DrawCallCount","m_PassName","m_PassLightMode","m_OriginalShaderName","m_RealShaderName","m_ShaderInstanceID","m_SubShaderIndex","m_ShaderPassIndex","shaderKeywords","m_VertexCount","m_IndexCount","m_ComponentInstanceID","m_MeshInstanceID","m_MeshSubset","m_MeshInstanceIDs","m_BatchBreakCause","m_RenderTargetName"})fields[field]=api.Field(data,field);
                int component=fields["m_ComponentInstanceID"] is int id?id:0;Renderer renderer=null;
                if(actorIds.TryGetValue(component,out var matched))renderer=matched;
                else if(obj is Renderer direct&&actorIds.ContainsKey(direct.GetInstanceID()))renderer=direct;
                else if(obj is GameObject go){var matches=renderers.Where(r=>r.gameObject==go).ToArray();if(matches.Length==1)renderer=matches[0];}
                if(renderer)capture.exactActorEvents++;
                int code=fields["m_BatchBreakCause"] is int cause?cause:-1;
                capture.details.Add(new{index,fresh=true,fields,passClass=NativePass65(fields["m_PassName"] as string,fields["m_PassLightMode"] as string),causeCode=code,causeString=code>=0&&code<api.Causes.Length?api.Causes[code]:null,actorRendererId=NativeObjectId65(renderer),materialIdsFromExactBinding=renderer?renderer.sharedMaterials.Select(m=>m.GetInstanceID()).ToArray():null,attribution=renderer?"Exact native component/object mapping":"NotRun: no exact actor mapping"});
            }
            if(capture.status=="NotRun")capture.status=capture.exactActorEvents>0?"Fresh actor events captured":"NotRun: fresh actor/pass attribution unavailable";
            File.WriteAllText(Path.Combine(output,"native-"+label+".json"),Json(capture));return capture;
        }
        [UnityTest,Explicit("Cycle67 ONE public StandardRequest per A/P/restoredA phase, source-proven producer while native local enabled")]
        public IEnumerator OriginalBlocksReportPublicRequestNativeEvents()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_MATERIAL_BATCH_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var api=new NativeBatchApi65();int sourceLocalGuid=(int)NativeBatchApi65.Find("UnityEditor.Networking.PlayerConnection.GeneralConnectionState").GetField("kEditorGuid",NativeBatchApi65.Static).GetRawConstantValue();Assert.AreEqual(sourceLocalGuid,api.ConnectedProfiler,"Only source-proven current local target; no profiler connection changes");File.WriteAllText(Path.Combine(output,"native-preflight.json"),Json(new{unity=Application.unityVersion,device=SystemInfo.graphicsDeviceType.ToString(),supported=api.Supported,enabled=api.Enabled,api.ConnectedProfiler,remoteGuid=api.Call("GetRemotePlayerGUID"),nativeCount=api.Property("count"),causes=api.Causes,utilityAssembly=api.Utility.Assembly.FullName}));
            Assert.True(api.Supported,"Native API unsupported: no new scene admitted");Assert.False(api.Enabled,"Foreign native session: no scene admitted");
            var baseline=PlayableProfile.Create(Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new AiTestMap());
            var store=new NativeBalanceStore(baseline,Path.Combine(output,"isolated-native-balance.json"),false);store.Select(NativeBalanceStore.NewMatchProfileId,NativeBalanceStore.NewMatchProfileVersion);var profile=store.Active;
            var priorDetail=ModelDetailProfile.Current;ModelDetailProfile.UseSession(store.ActiveModelDetail);
            var root=new GameObject("Cycle67 public render-request native evidence");var target=new RenderTexture(768,512,24,RenderTextureFormat.DefaultHDR){antiAliasing=4};target.Create();
            var camera=View(root.transform,target);camera.orthographic=true;camera.orthographicSize=5;camera.allowHDR=true;camera.backgroundColor=new Color(.045f,.075f,.10f);camera.transform.position=new Vector3(0,7,-10);camera.transform.LookAt(Vector3.zero);
            var lighting=IndustrialLightingProfile.Load(profile.AuthoredMap.Id);var sun=new GameObject("Native profile directional light").AddComponent<Light>();sun.transform.SetParent(root.transform);lighting.ApplyScene(sun);
            var world=new PlayableWorld(root.transform,profile);world.ApplyGraphics(ActorMaterialProfile.Load(),lighting);world.InspectionReveal();
            camera.gameObject.AddComponent<ActorGlowCamera>().Configure(world.MaterialFinish);camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());camera.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Explorer};var actors=kinds.Select((kind,i)=>world.Tank(i+1,true,kind)).ToArray();
            var states=new List<InstancedLitMaterialState>();var bindings=new List<MaterialControlBinding>();var captures=new List<NativeBatchCapture65>();var sourceHashes=new Dictionary<string,string>();var frames=new byte[3][];
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Spacewars/Tests/Fixtures/InstancedActorLit/Cycle61Lit.shader");Assert.NotNull(shader);
            bool originalSrp=GraphicsSettings.useScriptableRenderPipelineBatching;string originalTag=Shader.globalRenderPipeline;bool originalPause=EditorApplication.isPaused;
            void Remember(UnityEngine.Object asset){string assetPath=AssetDatabase.GetAssetPath(asset);if(string.IsNullOrEmpty(assetPath))return;string path=Path.GetFullPath(Path.Combine(Application.dataPath,"..",assetPath));if(File.Exists(path))sourceHashes[path]=MaterialControlHash(File.ReadAllBytes(path));}
            bool inRequest=false;int callbackDepth=0,requestSequence=0,requestBegins=0,requestEnds=0;bool callbacksLocal=true;
            var callbacks=new List<object>();var requests=new List<object>();string stop=null;
            bool LocalEnabled()=>(bool)api.Runtime.GetMethod("IsLocalEnabled",NativeBatchApi65.Static).Invoke(null,null);
            object NativeState(string stage)=>new{stage,frame=Time.frameCount,isPlaying=EditorApplication.isPlaying,isPaused=EditorApplication.isPaused,enabled=api.Enabled,local=LocalEnabled(),remote=(bool)api.Runtime.GetMethod("IsRemoteEnabled",NativeBatchApi65.Static).Invoke(null,null),remoteGuid=api.Call("GetRemotePlayerGUID"),count=api.Property("count"),limit=api.Limit,hash=api.Hash,dataHash=api.Property("eventDataHash"),events=api.Events()?.Length??-1};
            void Begin(ScriptableRenderContext context,Camera rendered){if(rendered!=camera)return;callbackDepth++;if(inRequest){requestBegins++;callbacksLocal&=api.Enabled&&LocalEnabled();callbacks.Add(new{stage="begin",requestSequence,frame=Time.frameCount,camera=rendered.GetInstanceID(),target=NativeObjectId65(rendered.targetTexture),local=LocalEnabled(),insideRequest=inRequest});}}
            void End(ScriptableRenderContext context,Camera rendered){if(rendered!=camera)return;if(inRequest){requestEnds++;callbacksLocal&=api.Enabled&&LocalEnabled();callbacks.Add(new{stage="end",requestSequence,frame=Time.frameCount,camera=rendered.GetInstanceID(),target=NativeObjectId65(rendered.targetTexture),local=LocalEnabled(),insideRequest=inRequest});}callbackDepth--;}
            RenderPipelineManager.beginCameraRendering+=Begin;RenderPipelineManager.endCameraRendering+=End;
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
                    Assert.AreEqual(CameraType.Game,camera.cameraType);Assert.AreEqual(CameraRenderType.Base,camera.GetUniversalAdditionalCameraData().renderType);
                    Assert.True(target.IsCreated());Assert.AreEqual(TextureDimension.Tex2D,target.dimension);
                    var request=new RenderPipeline.StandardRequest {destination=target,mipLevel=0,slice=0};
                    bool supported=RenderPipeline.SupportsRenderRequest(camera,request);Assert.True(supported,"Public request unsupported: producer NotRun");
                    var referenceHdr=MaterialControlHdr(target);File.WriteAllBytes(Path.Combine(output,$"phase-{phase}-warm-rgba-f32.bin"),referenceHdr);
                    Assert.False(api.Enabled,"Sentinel clear must precede native enable");var nativeBeforeClear=NativeState("before-sentinel-clear");
                    var previous=RenderTexture.active;bool priorSrgbWrite=GL.sRGBWrite;
                    try{RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(true,true,new Color(.875f,.125f,.625f,.375f));}
                    finally{RenderTexture.active=previous;GL.sRGBWrite=priorSrgbWrite;}
                    var sentinel=MaterialControlHdr(target);File.WriteAllBytes(Path.Combine(output,$"phase-{phase}-sentinel-rgba-f32.bin"),sentinel);
                    var nativeAfterClear=NativeState("after-sentinel-readback");bool sentinelUniform=UniformHdr67(sentinel);Assert.True(sentinelUniform,"Sentinel write unconfirmed; producer NotRun");
                    Assert.False(api.Enabled);var nativeBeforeEnable=NativeState("before-pause-enable");
                    // Mirrors FrameDebuggerWindow.EnableFrameDebugger local PlayMode prerequisite, without UI.
                    if(EditorApplication.isPlaying&&!EditorApplication.isPaused)EditorApplication.isPaused=true;
                    var afterPause=NativeState("after-owned-test-pause");api.Enable(true);
                    var afterEnable=NativeState("after-enable");
                    File.WriteAllText(Path.Combine(output,$"producer-preflight-phase-{phase}.json"),Json(new{phase,originalPause,nativeBeforeClear,nativeAfterClear,nativeBeforeEnable,afterPause,afterEnable,sentinelUniform,supported}));
                    Assert.True(api.Enabled&&LocalEnabled(),"Native local state unavailable: producer NotRun");
                    var preRequest=MaterialControlHdr(target);bool sentinelStillPresent=preRequest.SequenceEqual(sentinel);
                    var beforeSubmit=NativeState("before-submit");Assert.AreEqual(0,callbackDepth,"Request must originate outside render callbacks");
                    requestSequence++;requestBegins=0;requestEnds=0;callbacksLocal=true;int requestFrame=Time.frameCount;
                    inRequest=true;
                    try{RenderPipeline.SubmitRenderRequest(camera,request);}
                    finally{inRequest=false;}
                    var afterSubmit=NativeState("after-submit-before-readback");
                    frames[phase]=MaterialControlHdr(target);File.WriteAllBytes(Path.Combine(output,$"phase-{phase}-rgba-f32.bin"),frames[phase]);
                    var nativeAfter=NativeState("after-readback");var events=api.Events();
                    bool freshWrite=sentinelStillPresent&&!frames[phase].SequenceEqual(sentinel);
                    bool matchesWarm=frames[phase].SequenceEqual(referenceHdr);
                    bool producerConfirmed=supported&&sentinelUniform&&freshWrite&&requestBegins>0&&requestBegins==requestEnds&&callbacksLocal&&api.Enabled&&LocalEnabled();
                    requests.Add(new{phase,requestSequence,requestFrame,cameraId=camera.GetInstanceID(),targetId=target.GetInstanceID(),supported,requestBegins,requestEnds,callbacksLocal,sentinelUniform,sentinelStillPresent,freshWrite,producerConfirmed,matchesWarm,warmHash=MaterialControlHash(referenceHdr),sentinelHash=MaterialControlHash(sentinel),postHash=MaterialControlHash(frames[phase]),nativeBeforeClear,nativeAfterClear,nativeBeforeEnable,afterPause,afterEnable,beforeSubmit,afterSubmit,nativeAfter,eventCount=events.Length,globalCountersOnly=new{UnityStats.drawCalls,UnityStats.instancedBatches,UnityStats.triangles,UnityStats.shadowCasters}});
                    File.WriteAllText(Path.Combine(output,"requests.json"),Json(new{requests,callbacks}));
                    if(!producerConfirmed)stop="STOP: public camera producer unconfirmed; native attribution NotRun";
                    else if(events.Length==0)stop="STOP: public camera producer confirmed and fresh RT written, native event array empty; no repeat/UI workaround";
                    else captures.Add(ReadPublicRequestEvents67(api,events,renderers,output,phase==0?"A":phase==1?"P":"restoredA"));
                    if(api.Enabled)api.Call("SetEnabled",false,(int)api.Call("GetRemotePlayerGUID"));EditorApplication.isPaused=originalPause;
                    File.WriteAllText(Path.Combine(output,"requests.json"),Json(new{requests,callbacks,stop,afterDisable=NativeState("after-disable")}));
                    var png=ReadSrgbDisplay(target);try{File.WriteAllBytes(Path.Combine(output,$"phase-{phase}-srgb.png"),png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}
                    Assert.False(api.Enabled);
                    for(int i=0;i<renderers.Length;i++){CollectionAssert.AreEqual(materials[i],renderers[i].sharedMaterials);Assert.AreEqual(matrices[i],renderers[i].localToWorldMatrix);Assert.AreSame(meshes[i],renderers[i].GetComponent<MeshFilter>().sharedMesh);Assert.AreEqual(shadows[i],(renderers[i].shadowCastingMode,renderers[i].receiveShadows));Assert.AreEqual(visible[i],renderers[i].enabled&&renderers[i].gameObject.activeInHierarchy);}
                    foreach(var b in bindings){var global=new MaterialPropertyBlock();b.Renderer.GetPropertyBlock(global);Assert.True(global.isEmpty);Assert.AreEqual(MaterialControlKey(b.Materials[0],b.Blocks[0]),MaterialControlKey(b.Materials[0],GetBlock65(b.Renderer)),"Original indexed MPB bits unchanged");}
                    Assert.AreEqual(before,invariant());Assert.AreEqual(originalSrp,GraphicsSettings.useScriptableRenderPipelineBatching);Assert.AreEqual(originalTag,Shader.globalRenderPipeline);
                    if(stop!=null)break;
                }
                if(stop!=null){File.WriteAllText(Path.Combine(output,"stop.json"),Json(new{stop,requestCount=requestSequence,remainingPhases="NotRun at selected STOP boundary",nativeAttribution="NotRun"}));Assert.Fail(stop);}
                File.WriteAllText(Path.Combine(output,"strict-hdr.txt"),OriginalHdrDifference(frames[0],frames[1],target.width,"A-vs-P")+"\n"+OriginalHdrDifference(frames[0],frames[2],target.width,"A-vs-restoredA"));
                Assert.True(frames[0].SequenceEqual(frames[2]),"Original source restoration must preserve exact full HDR");
                Assert.True(frames[0].SequenceEqual(frames[1]),"A-P strict HDR remains independent, no tolerance");
                Assert.True(captures.Count==3&&captures.All(c=>c.freshEvents>0&&c.exactActorEvents>0),"Native actor/pass attribution incomplete; preserve NotRun fields");
            }
            finally
            {
                inRequest=false;RenderPipelineManager.beginCameraRendering-=Begin;RenderPipelineManager.endCameraRendering-=End;
                if(api.Enabled)api.Call("SetEnabled",false,(int)api.Call("GetRemotePlayerGUID"));EditorApplication.isPaused=originalPause;foreach(var s in states)s.Restore();
                var restored=sourceHashes.ToDictionary(k=>k.Key,k=>new{before=k.Value,after=MaterialControlHash(File.ReadAllBytes(k.Key))});
                File.WriteAllText(Path.Combine(output,"restoration.json"),Json(new{debuggerEnabled=api.Enabled,localEnabled=LocalEnabled(),originalPause,currentPause=EditorApplication.isPaused,requestCount=requestSequence,callbackDepth,callbackSubscriptionsRemoved=true,finalNative=NativeState("finally"),originalSrp,currentSrp=GraphicsSettings.useScriptableRenderPipelineBatching,originalTag,currentTag=Shader.globalRenderPipeline,sourceHashes=restored}));
                camera.targetTexture=null;world.Dispose();UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(target);ModelDetailProfile.UseSession(priorDetail);
                Assert.False(api.Enabled);Assert.AreEqual(originalPause,EditorApplication.isPaused);Assert.True(restored.All(k=>k.Value.before==k.Value.after));
            }
        }
    }
}
#endif

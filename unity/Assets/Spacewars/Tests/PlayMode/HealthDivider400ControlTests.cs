#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed partial class ActorMaterialRenderingTests
    {
        [UnityTest, Explicit("Cycle69 ONE frozen actual400 QA-only divider A/P/restoredA; quiet exclusive; no FPS/GPU claim")]
        public IEnumerator FourHundredActualActorsCompareHealthDividerMeshes()
        {
            Assert.AreNotEqual(GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);
            string output=Environment.GetEnvironmentVariable("SPACEWARS_HEALTH_DIVIDER_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
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
            foreach(var group in view.Entities.GroupBy(e=>e.Owner))
            {Assert.AreEqual(50,group.Count());Assert.AreEqual(49,group.Count(e=>e.Kind==PlayableEntityKind.Tank));Assert.AreEqual(1,group.Count(e=>e.Kind==PlayableEntityKind.Explorer));}
            File.WriteAllText(Path.Combine(output,"bound-profile.json"),Json(profile.CopyData()));
            File.WriteAllText(Path.Combine(output,"bound-ai-profile.json"),Json(store.ActiveAiProfile.CopyData()));
            File.WriteAllText(Path.Combine(output,"genesis-units.json"),Json(view.Entities.Select(e=>new{e.Id,e.Owner,e.Kind,e.Position,e.HullHeading,e.TurretHeading,e.Upgraded})));
            var priorDetail=ModelDetailProfile.Current;ModelDetailProfile.UseSession(store.ActiveModelDetail);
            int priorVsync=QualitySettings.vSyncCount,priorFrameRate=Application.targetFrameRate;
            QualitySettings.vSyncCount=1;Application.targetFrameRate=60;
            var root=new GameObject("Cycle69 actual frozen400 divider control");
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
            world.ApplyGraphics(ActorMaterialProfile.Load(),lighting);world.Fog.Update(view.Vision,0);
            var controls=new List<DividerMeshControl68>();var records=new List<object>();
            var names=new[]{"Draw Calls Count","Batches Count","SetPass Calls Count","Triangles Count","Main Thread","Render Thread"};
            var counters=names.Select((name,i)=>ProfilerRecorder.StartNew(i<4?ProfilerCategory.Render:ProfilerCategory.Internal,name,1)).ToArray();
            bool initialized=false,srp=false;string tag=null,renderKey=null;bool allExact=true;
            var rows=new System.Text.StringBuilder("phase,sample,frame,draws,batches,setPass,triangles,mainNs,renderNs,unityDraws,instancedBatches,shadowCasters\n");
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
                for(int warm=0;warm<8;warm++)yield return null;
                var pipeline=(UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
                Assert.AreEqual(5,QualitySettings.GetQualityLevel());Assert.AreEqual(4,pipeline.msaaSampleCount);Assert.AreEqual(1,pipeline.renderScale);Assert.True(pipeline.supportsHDR);
                Assert.True(NativeDisplaySettings.Shadows);Assert.True(NativeUserSettings.AlwaysHealth);Assert.AreSame(sun,RenderSettings.sun);
                srp=GraphicsSettings.useScriptableRenderPipelineBatching;tag=Shader.globalRenderPipeline;Assert.True(srp);Assert.AreEqual("UniversalPipeline",tag);initialized=true;
                Func<string> currentRenderKey=()=>Json(new{quality=QualitySettings.GetQualityLevel(),qualityAA=QualitySettings.antiAliasing,QualitySettings.vSyncCount,Application.targetFrameRate,
                    pipeline=pipeline.GetInstanceID(),pipeline.msaaSampleCount,pipeline.renderScale,pipeline.supportsHDR,pipeline.supportsDynamicBatching,pipeline.useSRPBatcher,
                    camera.allowHDR,targetAA=target.antiAliasing,target.sRGB,camera.cullingMask,camera.orthographicSize,camera.aspect,camera.nearClipPlane,camera.farClipPlane,
                    cameraPosition=camera.transform.position.ToString("R"),cameraRotation=camera.transform.rotation.ToString("R"),shadows=NativeDisplaySettings.Shadows,alwaysHealth=NativeUserSettings.AlwaysHealth});
                renderKey=currentRenderKey();
                var actors=view.Entities.Select(e=>world.Actors[e.Id]).ToArray();Assert.AreEqual(400,actors.Length);Assert.AreEqual(view.Entities.Count+view.Buildings.Count(b=>b.Phase!=ConstructionPhase.Pending),world.Actors.Count);
                foreach(var actor in actors)controls.Add(new DividerMeshControl68(actor));
                Assert.AreEqual(400,controls.Count);Assert.AreEqual(3600,controls.Sum(c=>c.Sources.Length));
                var sourceDividers=controls.SelectMany(c=>c.Sources).ToArray();Assert.True(sourceDividers.All(r=>r.enabled&&r.gameObject.activeInHierarchy));
                var publicLayout=sourceDividers.Select(r=>Matrix4x4.TRS(r.transform.localPosition,r.transform.localRotation,r.transform.localScale)).ToArray();
                var hulls=actors.SelectMany(a=>a.Hull.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
                var hullMatrices=hulls.Select(r=>r.localToWorldMatrix).ToArray();var hullMeshes=hulls.Select(r=>r.GetComponent<MeshFilter>().sharedMesh).ToArray();
                var hullMaterials=hulls.Select(r=>r.sharedMaterials).ToArray();var hullVisibility=hulls.Select(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
                var hullShadows=hulls.Select(r=>(r.shadowCastingMode,r.receiveShadows)).ToArray();
                string HullBlockKey(Renderer renderer,int slot)
                {var block=new MaterialPropertyBlock();if(slot<0)renderer.GetPropertyBlock(block);else renderer.GetPropertyBlock(block,slot);return block.isEmpty?"EMPTY":MaterialControlKey(renderer.sharedMaterial,block);}
                var hullBlocks=hulls.Select(r=>Enumerable.Range(-1,r.sharedMaterials.Length+1).Select(slot=>HullBlockKey(r,slot)).ToArray()).ToArray();
                var planes=GeometryUtility.CalculateFrustumPlanes(camera);
                File.WriteAllText(Path.Combine(output,"render-binding.json"),Json(new{unity=Application.unityVersion,api=SystemInfo.graphicsDeviceType.ToString(),gpu=SystemInfo.graphicsDeviceName,
                    width=target.width,height=target.height,renderKey,profile=profile.Revision,map=profile.AuthoredMap.Id,units=actors.Length,buildings=view.Buildings.Count,
                    hullRenderers=hulls.Length,healthRenderers=actors.Sum(a=>a.Health.parent.GetComponentsInChildren<MeshRenderer>(true).Count(r=>r.name!="QA combined health separators")),
                    dividerRenderers=sourceDividers.Length,dividerFrustum=sourceDividers.Count(r=>GeometryUtility.TestPlanesAABB(planes,r.bounds)),combinedFrustum=controls.Count(c=>GeometryUtility.TestPlanesAABB(planes,c.Renderer.bounds)),
                    sourceVertices=controls.Sum(c=>c.Sources.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.vertexCount)),combinedVertices=controls.Sum(c=>c.Mesh.vertexCount),
                    sourceTriangles=controls.Sum(c=>c.Sources.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3)),combinedTriangles=controls.Sum(c=>c.Mesh.triangles.Length/3),
                    limits="Frozen actual400 genesis from owning61; sourcebindings/quality/camera/postpasses unchanged; no native capture, simulation, FPS or GPU measurement."}));
                byte[] expected=null;
                for(int phase=0;phase<3;phase++)
                {
                    foreach(var control in controls){control.SetCombined(phase==1);control.Refresh();Assert.AreEqual(1,control.Rebuilds);}
                    for(int warm=0;warm<12;warm++)yield return null;
                    var samples=new List<object>();
                    for(int sample=0;sample<48;sample++)
                    {
                        yield return null;
                        var values=counters.Select(Counter).ToArray();
                        rows.AppendLine(phase+","+sample+","+Time.frameCount+","+string.Join(",",values)+","+UnityStats.drawCalls+","+UnityStats.instancedBatches+","+UnityStats.shadowCasters);
                        samples.Add(new{sample,frame=Time.frameCount,draws=values[0],batches=values[1],setPass=values[2],triangles=values[3],mainNs=values[4],renderNs=values[5],unityDraws=UnityStats.drawCalls,instanced=UnityStats.instancedBatches,shadows=UnityStats.shadowCasters});
                    }
                    File.WriteAllText(Path.Combine(output,"samples.csv"),rows.ToString());
                    var hdr=MaterialControlHdr(target);File.WriteAllBytes(Path.Combine(output,$"phase-{phase}-rgba-f32.bin"),hdr);
                    var png=ReadSrgbDisplay(target);try{File.WriteAllBytes(Path.Combine(output,$"phase-{phase}-srgb.png"),png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}
                    for(int wait=0;wait<4;wait++)yield return null;
                    var stable=MaterialControlHdr(target);bool stableExact=hdr.SequenceEqual(stable);if(!stableExact)File.WriteAllBytes(Path.Combine(output,$"phase-{phase}-unstable-rgba-f32.bin"),stable);
                    if(phase==0)expected=hdr;bool exact=expected.SequenceEqual(hdr);allExact&=exact;
                    CollectionAssert.AreEqual(publicLayout,sourceDividers.Select(r=>Matrix4x4.TRS(r.transform.localPosition,r.transform.localRotation,r.transform.localScale)).ToArray());
                    Assert.AreEqual(renderKey,currentRenderKey());Assert.AreEqual(srp,GraphicsSettings.useScriptableRenderPipelineBatching);Assert.AreEqual(tag,Shader.globalRenderPipeline);
                    Assert.AreEqual(400,actors.Length);
                    for(int i=0;i<hulls.Length;i++)
                    {
                        var r=hulls[i];Assert.AreEqual(hullMatrices[i],r.localToWorldMatrix);Assert.AreSame(hullMeshes[i],r.GetComponent<MeshFilter>().sharedMesh);
                        CollectionAssert.AreEqual(hullMaterials[i],r.sharedMaterials);Assert.AreEqual(hullVisibility[i],r.enabled&&r.gameObject.activeInHierarchy);Assert.AreEqual(hullShadows[i],(r.shadowCastingMode,r.receiveShadows));
                        CollectionAssert.AreEqual(hullBlocks[i],Enumerable.Range(-1,r.sharedMaterials.Length+1).Select(slot=>HullBlockKey(r,slot)).ToArray());
                    }
                    records.Add(new{phase,label=new[]{"original-A","combined-P","restored-A"}[phase],stableExact,exact,hdrSha256=MaterialControlHash(hdr),stableSha256=MaterialControlHash(stable),samples,
                        actors=actors.Length,activeDividerRenderers=phase==1?controls.Count:sourceDividers.Length,rebuilds=controls.Select(c=>c.Rebuilds).ToArray(),
                        limits="48 unadjusted main/render thread recorder samples include Editor/pacing/wait context; not CPU utilization, FPS, GPU or moving400 evidence. Setup/helper refresh/HDR/display readbacks outside sample window."});
                    File.WriteAllText(Path.Combine(output,"phases.json"),Json(records));Assert.True(stableExact,"Frozen output must be stable in phase "+phase);
                }
                Assert.True(allExact,"Strict actual400 full-HDR A/P/restoredA equality");
            }
            finally
            {
                foreach(var control in controls)control.Dispose();foreach(var counter in counters)counter.Dispose();
                QualitySettings.vSyncCount=priorVsync;Application.targetFrameRate=priorFrameRate;
                File.WriteAllText(Path.Combine(output,"restoration.json"),Json(new{controls=controls.Count,sourceDividersEnabled=controls.All(c=>c.Sources.All(r=>r.enabled)),
                    sourceMeshesCube=controls.All(c=>c.Sources.All(r=>r.GetComponent<MeshFilter>().sharedMesh.name=="Cube")),ownedCombinedMeshesReleased=controls.All(c=>!c.Mesh),
                    initialized,srpUnchanged=!initialized||srp==GraphicsSettings.useScriptableRenderPipelineBatching,tagUnchanged=!initialized||tag==Shader.globalRenderPipeline,
                    pacingRestored=QualitySettings.vSyncCount==priorVsync&&Application.targetFrameRate==priorFrameRate,phases=records.Count}));
                camera.targetTexture=null;world.Dispose();UnityEngine.Object.DestroyImmediate(root);target.Release();UnityEngine.Object.DestroyImmediate(target);ModelDetailProfile.UseSession(priorDetail);
            }
        }
    }
}
#endif

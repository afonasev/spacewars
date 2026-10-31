#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEditor;

namespace Spacewars.Tests.PlayMode
{
    public sealed partial class ActorMaterialRenderingTests
    {
        // QA-only: original child transforms and meshes stay intact for observers.
        private sealed class DividerMeshControl68 : IDisposable
        {
            internal readonly MeshRenderer[] Sources;
            internal readonly MeshRenderer Renderer;
            internal readonly Mesh Mesh;
            private readonly GameObject host;
            private readonly bool[] enabled;
            private Vector3[] positions,scales;
            private Quaternion[] rotations;
            internal int Rebuilds {get;private set;}
            internal DividerMeshControl68(PlayableWorld.Actor actor)
            {
                Sources=actor.Health.parent.Cast<Transform>().Where(t=>t.name.StartsWith("Health divider ")).Select(t=>t.GetComponent<MeshRenderer>()).ToArray();
                enabled=Sources.Select(r=>r.enabled).ToArray();var source=Sources[0];
                Assert.True(Sources.All(r=>r.sharedMaterial==source.sharedMaterial));
                host=new GameObject("QA combined health separators");host.layer=source.gameObject.layer;host.transform.SetParent(actor.Health.parent,false);
                Mesh=new Mesh{name="QA exact source divider cubes"};host.AddComponent<MeshFilter>().sharedMesh=Mesh;
                Renderer=host.AddComponent<MeshRenderer>();Renderer.sharedMaterials=source.sharedMaterials;
                var block=new MaterialPropertyBlock();source.GetPropertyBlock(block);Renderer.SetPropertyBlock(block);
                var copied=new MaterialPropertyBlock();Renderer.GetPropertyBlock(copied);
                Assert.AreEqual(block.GetVector("_BaseColor"),copied.GetVector("_BaseColor"));
                var slot=new MaterialPropertyBlock();source.GetPropertyBlock(slot,0);
                // An allocated empty indexed block masks the source's global block.
                // Preserve the actual absent indexed binding with null (cycle62).
                Renderer.SetPropertyBlock(slot.isEmpty?null:slot,0);
                Renderer.GetPropertyBlock(copied,0);Assert.AreEqual(slot.isEmpty,copied.isEmpty);
                Renderer.shadowCastingMode=source.shadowCastingMode;Renderer.receiveShadows=source.receiveShadows;
                Renderer.lightProbeUsage=source.lightProbeUsage;Renderer.reflectionProbeUsage=source.reflectionProbeUsage;
                Renderer.probeAnchor=source.probeAnchor;Renderer.motionVectorGenerationMode=source.motionVectorGenerationMode;
                Renderer.allowOcclusionWhenDynamic=source.allowOcclusionWhenDynamic;Renderer.renderingLayerMask=source.renderingLayerMask;
                Renderer.sortingLayerID=source.sortingLayerID;Renderer.sortingOrder=source.sortingOrder;
                Refresh();SetCombined(false);
            }
            internal void Refresh()
            {
                var p=Sources.Select(r=>r.transform.localPosition).ToArray();var s=Sources.Select(r=>r.transform.localScale).ToArray();var q=Sources.Select(r=>r.transform.localRotation).ToArray();
                if(positions!=null&&p.SequenceEqual(positions)&&s.SequenceEqual(scales)&&q.SequenceEqual(rotations))return;
                positions=p;scales=s;rotations=q;
                var instances=Sources.Select((r,i)=>new CombineInstance{mesh=r.GetComponent<MeshFilter>().sharedMesh,subMeshIndex=0,transform=Matrix4x4.TRS(p[i],q[i],s[i])}).ToArray();
                Mesh.Clear();Mesh.CombineMeshes(instances,true,true,false);Rebuilds++;
                Assert.AreEqual(Sources.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.vertexCount),Mesh.vertexCount);
                Assert.AreEqual(Sources.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length),Mesh.triangles.Length);
                int vertex=0;var vertices=Mesh.vertices;var normals=Mesh.normals;var uv=Mesh.uv;
                foreach(var instance in instances)
                {
                    var original=instance.mesh;var v=original.vertices;var n=original.normals;var u=original.uv;
                    for(int i=0;i<v.Length;i++,vertex++)
                    {
                        Assert.AreEqual(instance.transform.MultiplyPoint3x4(v[i]),vertices[vertex],"Every source cube vertex is preserved in health-root coordinates");
                        Assert.That((instance.transform.inverse.transpose.MultiplyVector(n[i]).normalized-normals[vertex]).sqrMagnitude,Is.LessThan(1e-12f));
                        Assert.AreEqual(u[i],uv[vertex]);
                    }
                }
            }
            internal void SetCombined(bool combined)
            {for(int i=0;i<Sources.Length;i++)Sources[i].enabled=combined?false:enabled[i];Renderer.enabled=combined;}
            public void Dispose()
            {SetCombined(false);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(Mesh);}
        }

        [UnityTest, Explicit("Cycle68 QA-only 2+2 divider mesh A/P/restoredA strict HDR and actual submission control")]
        public IEnumerator CompareLosslessHealthDividerMeshes()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_HEALTH_DIVIDER_OUTPUT");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            Assert.AreNotEqual(GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);
            var baseline=PlayableProfile.Create(Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new AiTestMap());
            var store=new NativeBalanceStore(baseline,Path.Combine(output,"isolated-balance.json"),false);store.Select(NativeBalanceStore.NewMatchProfileId,NativeBalanceStore.NewMatchProfileVersion);
            var profile=store.Active;var priorDetail=ModelDetailProfile.Current;ModelDetailProfile.UseSession(store.ActiveModelDetail);
            var root=new GameObject("Cycle68 bounded divider mesh control");
            var target=new RenderTexture(768,512,24,RenderTextureFormat.DefaultHDR){antiAliasing=4};target.Create();
            var camera=View(root.transform,target);camera.allowHDR=true;
            var lighting=IndustrialLightingProfile.Load(profile.AuthoredMap.Id);var sun=new GameObject("Unchanged native profile light").AddComponent<Light>();sun.transform.SetParent(root.transform);lighting.ApplyScene(sun);
            var world=new PlayableWorld(root.transform,profile);world.ApplyGraphics(ActorMaterialProfile.Load(),lighting);world.InspectionReveal();
            camera.gameObject.AddComponent<ActorGlowCamera>().Configure(world.MaterialFinish);
            camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());
            camera.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
            camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            var controls=new System.Collections.Generic.List<DividerMeshControl68>();var records=new System.Collections.Generic.List<object>();
            bool srp=GraphicsSettings.useScriptableRenderPipelineBatching;string tag=Shader.globalRenderPipeline;
            var settings=Json(new{quality=QualitySettings.GetQualityLevel(),QualitySettings.antiAliasing,qualityPipeline=GraphicsSettings.currentRenderPipeline.GetInstanceID(),camera.allowHDR,targetAa=target.antiAliasing,target.sRGB});
            var actors=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Explorer}.Select((kind,i)=>world.Tank(i+1,true,kind)).ToArray();
            try
            {
                for(int i=0;i<actors.Length;i++)
                {
                    actors[i].Root.position=new Vector3(i%2==0?-2:2,0,i<2?-1.5f:1.5f);ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[i<2?0:4],out var owner);PlayableWorld.PaintOwner(actors[i],owner);
                }
                // As in cycle64, snapshot the actual initialized URP state, never
                // compare its pre-first-camera defaults against the running pipeline.
                bool preInitSrp=srp;string preInitTag=tag;
                camera.orthographic=true;camera.orthographicSize=5;camera.transform.position=new Vector3(0,7,-10);camera.transform.LookAt(Vector3.zero);
                for(int warm=0;warm<8;warm++)yield return null;
                srp=GraphicsSettings.useScriptableRenderPipelineBatching;tag=Shader.globalRenderPipeline;
                Assert.True(srp);Assert.AreEqual("UniversalPipeline",tag);Assert.AreSame(sun,RenderSettings.sun);
                settings=Json(new{quality=QualitySettings.GetQualityLevel(),QualitySettings.antiAliasing,qualityPipeline=GraphicsSettings.currentRenderPipeline.GetInstanceID(),camera.allowHDR,targetAa=target.antiAliasing,target.sRGB});
                File.WriteAllText(Path.Combine(output,"initialized-pipeline.json"),Json(new{preInitSrp,preInitTag,srp,tag,settings}));
                foreach(float zoom in new[]{2.135f,9.6f,25.6f,-1f})
                {
                    camera.orthographic=zoom>0;camera.orthographicSize=zoom>0?zoom:5;camera.transform.position=zoom>0?new Vector3(0,7,-10):new Vector3(3,9,-13);camera.transform.LookAt(Vector3.zero);
                    for(int i=0;i<actors.Length;i++)PlayableWorld.UpdateHealth(actors[i],camera,new[]{1f,.67f,.25f,0f}[i],true);
                    if(controls.Count==0)foreach(var actor in actors)controls.Add(new DividerMeshControl68(actor));else foreach(var control in controls)control.Refresh();
                    var transforms=controls.SelectMany(c=>c.Sources).Select(r=>Json(new{position=r.transform.localPosition.ToString("R"),scale=r.transform.localScale.ToString("R"),rotation=r.transform.localRotation.ToString("R")})).ToArray();
                    var rebuilds=controls.Select(c=>c.Rebuilds).ToArray();byte[] expected=null;bool allExact=true;
                    foreach(int phase in new[]{0,1,2})
                    {
                        foreach(var control in controls){control.SetCombined(phase==1);control.Refresh();}
                        CollectionAssert.AreEqual(rebuilds,controls.Select(c=>c.Rebuilds).ToArray(),"No rebuild on unchanged quantized layout or A/P toggle");
                        for(int warm=0;warm<8;warm++)yield return null;
                        string label="zoom-"+zoom.ToString(System.Globalization.CultureInfo.InvariantCulture)+"-"+new[]{"original-A","combined-P","restored-A"}[phase];
                        var hdr=MaterialControlHdr(target);File.WriteAllBytes(Path.Combine(output,label+"-rgba-f32.bin"),hdr);
                        var png=ReadSrgbDisplay(target);try{File.WriteAllBytes(Path.Combine(output,label+"-srgb.png"),png.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(png);}
                        long draws=0,triangles=0,shadows=0,instanced=0;var samples=new System.Collections.Generic.List<object>();
                        for(int frame=0;frame<4;frame++){yield return null;draws+=UnityStats.drawCalls;triangles+=UnityStats.triangles;shadows+=UnityStats.shadowCasters;instanced+=UnityStats.instancedBatches;samples.Add(new{frame=Time.frameCount,draws=UnityStats.drawCalls,triangles=UnityStats.triangles,shadows=UnityStats.shadowCasters,instanced=UnityStats.instancedBatches});}
                        var stable=MaterialControlHdr(target);bool stableExact=hdr.SequenceEqual(stable);if(!stableExact)File.WriteAllBytes(Path.Combine(output,label+"-unstable-rgba-f32.bin"),stable);
                        if(phase==0)expected=hdr;
                        bool exact=expected.SequenceEqual(hdr);allExact&=exact;
                        CollectionAssert.AreEqual(transforms,controls.SelectMany(c=>c.Sources).Select(r=>Json(new{position=r.transform.localPosition.ToString("R"),scale=r.transform.localScale.ToString("R"),rotation=r.transform.localRotation.ToString("R")})).ToArray(),"Public divider layout must remain exact");
                        Assert.AreEqual(srp,GraphicsSettings.useScriptableRenderPipelineBatching);Assert.AreEqual(tag,Shader.globalRenderPipeline);
                        Assert.AreEqual(settings,Json(new{quality=QualitySettings.GetQualityLevel(),QualitySettings.antiAliasing,qualityPipeline=GraphicsSettings.currentRenderPipeline.GetInstanceID(),camera.allowHDR,targetAa=target.antiAliasing,target.sRGB}));
                        records.Add(new{label,phase,zoom,stableExact,exact,hdrSha256=MaterialControlHash(hdr),stableSha256=MaterialControlHash(stable),draws=draws/4.0,triangles=triangles/4.0,shadows=shadows/4.0,instanced=instanced/4.0,samples,rebuilds=controls.Select(c=>c.Rebuilds).ToArray(),activeDividerRenderers=phase==1?controls.Count:controls.Sum(c=>c.Sources.Length),sourceTriangles=controls.Sum(c=>c.Sources.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3)),combinedTriangles=controls.Sum(c=>c.Mesh.triangles.Length/3),limits="Global counts in unchanged small scene, not FPS/GPU/actor pass attribution. All original divider transforms retained; no gameplay/runtime optimization."});
                        File.WriteAllText(Path.Combine(output,"comparison.json"),Json(records));
                        Assert.True(stableExact,"Control output must be stable: "+label);
                        // Finish restoredA even on a failed exact candidate, preserving the causal control.
                        if(phase==2)Assert.True(allExact,"Strict full-HDR A/P/restoredA equality: "+label);
                    }
                }
            }
            finally
            {
                foreach(var control in controls)control.Dispose();
                File.WriteAllText(Path.Combine(output,"restoration.json"),Json(new{sourceDividersEnabled=controls.All(c=>c.Sources.All(r=>r.enabled)),sourceMeshesUnchanged=controls.All(c=>c.Sources.All(r=>r.GetComponent<MeshFilter>().sharedMesh.name=="Cube")),srpUnchanged=srp==GraphicsSettings.useScriptableRenderPipelineBatching,tagUnchanged=tag==Shader.globalRenderPipeline,records=records.Count}));
                camera.targetTexture=null;world.Dispose();UnityEngine.Object.DestroyImmediate(root);target.Release();UnityEngine.Object.DestroyImmediate(target);ModelDetailProfile.UseSession(priorDetail);
            }
        }

        [UnityTest, Explicit("Cycle68 small NewMatch2 health-divider inventory; no runtime changes")]
        public IEnumerator InventoryActualHealthDividers()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_HEALTH_DIVIDER_OUTPUT");
            Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            var baseline=PlayableProfile.Create(Newtonsoft.Json.JsonConvert.DeserializeObject<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),new AiTestMap());
            var store=new NativeBalanceStore(baseline,Path.Combine(output,"isolated-balance.json"),false);
            store.Select(NativeBalanceStore.NewMatchProfileId,NativeBalanceStore.NewMatchProfileVersion);
            var profile=store.Active;var priorDetail=ModelDetailProfile.Current;ModelDetailProfile.UseSession(store.ActiveModelDetail);
            var root=new GameObject("Cycle68 actual health inventory");
            var target=new RenderTexture(768,512,24,RenderTextureFormat.DefaultHDR){antiAliasing=4};target.Create();
            var camera=View(root.transform,target);camera.orthographic=true;camera.orthographicSize=5;camera.allowHDR=true;
            camera.transform.position=new Vector3(0,7,-10);camera.transform.LookAt(Vector3.zero);
            var lighting=IndustrialLightingProfile.Load(profile.AuthoredMap.Id);
            var sun=new GameObject("Unchanged native profile light").AddComponent<Light>();sun.transform.SetParent(root.transform);lighting.ApplyScene(sun);
            var world=new PlayableWorld(root.transform,profile);world.ApplyGraphics(ActorMaterialProfile.Load(),lighting);world.InspectionReveal();
            camera.gameObject.AddComponent<ActorGlowCamera>().Configure(world.MaterialFinish);
            camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());
            camera.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
            camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform,lighting);
            try
            {
                var actors=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Explorer}
                    .Select((kind,i)=>world.Tank(i+1,true,kind)).ToArray();
                for(int i=0;i<actors.Length;i++)
                {
                    actors[i].Root.position=new Vector3(i%2==0?-2:2,0,i<2?-1.5f:1.5f);
                    ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[i<2?0:4],out var owner);
                    PlayableWorld.PaintOwner(actors[i],owner);PlayableWorld.UpdateHealth(actors[i],camera,1,true);
                }
                for(int warm=0;warm<8;warm++)yield return null;
                var planes=GeometryUtility.CalculateFrustumPlanes(camera);
                var all=world.GraphicsRoot.GetComponentsInChildren<MeshRenderer>(true);
                var health=actors.SelectMany(a=>a.Health.parent.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
                var dividers=health.Where(r=>r.name.StartsWith("Health divider ")).ToArray();
                var rows=dividers.Select(r=>
                {
                    var mesh=r.GetComponent<MeshFilter>().sharedMesh;var m=r.sharedMaterial;
                    var block=new MaterialPropertyBlock();r.GetPropertyBlock(block);var slot=new MaterialPropertyBlock();r.GetPropertyBlock(slot,0);
                    return new{actor=r.transform.parent.GetInstanceID(),r.name,mesh=mesh.name,meshId=mesh.GetInstanceID(),mesh.vertexCount,triangles=mesh.triangles.Length/3,
                        vertices=mesh.vertices.Select(v=>new[]{v.x,v.y,v.z}),normals=mesh.normals.Select(v=>new[]{v.x,v.y,v.z}),uv=mesh.uv.Select(v=>new[]{v.x,v.y}),
                        indices=mesh.triangles,attributes=mesh.GetVertexAttributes().Select(v=>v.ToString()),material=m.name,materialId=m.GetInstanceID(),shader=m.shader.name,
                        shaderPath=AssetDatabase.GetAssetPath(m.shader),surfaceRole=m.GetFloat("_SurfaceRole"),yardObject=m.GetFloat("_YardObject"),
                        blockSurfaceRole=block.HasProperty("_SurfaceRole"),blockYardObject=block.HasProperty("_YardObject"),globalBaseColor=block.GetVector("_BaseColor").ToString("R"),slotEmpty=slot.isEmpty,
                        shadow=r.shadowCastingMode.ToString(),r.receiveShadows,r.enabled,r.gameObject.activeInHierarchy,r.gameObject.layer,r.renderingLayerMask,r.motionVectorGenerationMode,
                        r.allowOcclusionWhenDynamic,r.lightProbeUsage,r.reflectionProbeUsage,bounds=r.bounds.ToString("R"),frustum=GeometryUtility.TestPlanesAABB(planes,r.bounds),
                        position=r.transform.localPosition.ToString("R"),scale=r.transform.localScale.ToString("R"),rotation=r.transform.localRotation.ToString("R")};
                }).ToArray();
                File.WriteAllText(Path.Combine(output,"inventory.json"),Json(new{unity=Application.unityVersion,profile=NativeBalanceStore.NewMatchProfileId,version=NativeBalanceStore.NewMatchProfileVersion,
                    profile.HealthBarCells,profile.HealthBarWidth,profile.HealthBarHeight,profile.HealthBarCellGap,quality=QualitySettings.GetQualityLevel(),api=SystemInfo.graphicsDeviceType.ToString(),
                    totalRenderers=all.Length,activeRenderers=all.Count(r=>r.enabled&&r.gameObject.activeInHierarchy),healthRenderers=health.Length,dividerRenderers=dividers.Length,
                    healthFrustum=health.Count(r=>GeometryUtility.TestPlanesAABB(planes,r.bounds)),dividerFrustum=dividers.Count(r=>GeometryUtility.TestPlanesAABB(planes,r.bounds)),
                    globalDraws=UnityStats.drawCalls,globalTriangles=UnityStats.triangles,globalShadowCasters=UnityStats.shadowCasters,rows}));
                Assert.AreEqual(4*(profile.HealthBarCells-1),dividers.Length);
                foreach(var renderer in dividers)
                {
                    Assert.AreEqual(ShadowCastingMode.Off,renderer.shadowCastingMode);Assert.False(renderer.receiveShadows);
                    Assert.AreEqual(0,renderer.sharedMaterial.GetFloat("_YardObject"));Assert.AreEqual(0,renderer.sharedMaterial.GetFloat("_SurfaceRole"));
                }
            }
            finally
            {
                camera.targetTexture=null;world.Dispose();UnityEngine.Object.DestroyImmediate(root);target.Release();UnityEngine.Object.DestroyImmediate(target);
                ModelDetailProfile.UseSession(priorDetail);
            }
        }
    }
}

#endif

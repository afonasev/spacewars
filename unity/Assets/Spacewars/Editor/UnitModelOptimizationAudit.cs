using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.StyleA.Preview;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace Spacewars.Editor
{
    public static class UnitModelOptimizationAudit
    {
        static readonly string[] Keys=UnitModelResources.Keys;
        static string Output=>Environment.GetEnvironmentVariable("SPACEWARS_UNIT_EVIDENCE") ?? throw new InvalidOperationException("Set SPACEWARS_UNIT_EVIDENCE");
        public static void Baseline()=>Capture();
        public static void PreviewBaseline()=>Capture();
        public static void Candidate(){HawkModelAssets.PrepareOptimizedPrefab();RepairImmutableSharing();}
        public static void RepairImmutableSharing()
        {
            Directory.CreateDirectory(Output);var before=Snapshot();File.WriteAllLines(Path.Combine(Output,"variant-bindings-before.txt"),before);
            UnitModelResources.ReuseExactVariantMeshes();var after=Snapshot();File.WriteAllLines(Path.Combine(Output,"variant-bindings-after.txt"),after);
            if(!before.SequenceEqual(after))throw new InvalidOperationException("Immutable repair changed variant data/material/settings/transforms");
            File.WriteAllText(Path.Combine(Output,"variant-equivalence.txt"),"PASS: exact complete per-variant mesh data/bounds/index format, materials, renderer settings, active states and transforms unchanged after independent immutable sharing. Unchanged dependency controls and inherited sole FAIL are retained; authored model refresh requires its selected new representation checks.\n");
            Capture();
        }
        [Serializable] class Variant
        {
            public string key;public bool upgraded;public int allMeshRenderers,activeMeshRenderers,activeVertices,activeTriangles,transientLaunchPulses,skinnedRenderers,transientMuzzleFlashes;public string[] meshSignatures;
        }
        [Serializable] class Variants{public string classification="NATIVE_EDITOR_ASSET_PRESENTATION_NOT_GAMEPLAY_ACCEPTANCE";public Variant[] variants;}
        static void Capture()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output,"prefab-inventory.json"),JsonUtility.ToJson(StyleALodInventory.Inspect(Keys.Select(k=>Resources.Load<GameObject>("StyleA/"+k)).ToArray()),true));
            File.WriteAllText(Path.Combine(Output,"legacy-inventory.json"),JsonUtility.ToJson(StyleALodInventory.Inspect(UnitModelResources.LegacyKeys.Select(k=>Resources.Load<GameObject>("StyleA/"+k)).ToArray()),true));
            File.WriteAllLines(Path.Combine(Output,"full-native-bindings.txt"),Snapshot());
            File.WriteAllText(Path.Combine(Output,"preview-inventory.json"),JsonUtility.ToJson(StyleALodInventory.Inspect(UnitModelResources.PreviewKeys.Select(k=>Resources.Load<GameObject>("StyleA/"+k)).ToArray()),true));
            File.WriteAllText(Path.Combine(Output,"combined-resource-inventory.json"),JsonUtility.ToJson(StyleALodInventory.Inspect(Keys.Concat(UnitModelResources.PreviewKeys).Select(k=>Resources.Load<GameObject>("StyleA/"+k)).ToArray()),true));
            File.WriteAllLines(Path.Combine(Output,"shared-pair-counts.txt"),new[]{"tankHybrid,tankHybridUpgraded","explorer,explorerUpgraded","shkval,shkval-guidance-3"}.Select(pair=>{var keys=pair.Split(',');var a=Resources.Load<GameObject>("StyleA/"+keys[0]).GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh);var b=Resources.Load<GameObject>("StyleA/"+keys[1]).GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh);return pair+"="+a.Intersect(b).Count();}));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.60f);
            var light=new GameObject("Fixed unit daylight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(50,-35,0);
            var fill=new GameObject("Fixed unit fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.45f;fill.transform.rotation=Quaternion.Euler(35,140,0);
            var camera=new GameObject("Fixed unit camera").AddComponent<Camera>();camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=150;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.20f,.23f,.25f);camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);Object.DestroyImmediate(ground.GetComponent<Collider>());ground.transform.localScale=Vector3.one*10;
            var floor=new Material(Shader.Find("Universal Render Pipeline/Lit"));floor.color=new Color(.28f,.30f,.28f);ground.GetComponent<Renderer>().sharedMaterial=floor;
            var p=PlayableProfile.Default;var owner=new Color(.19f,.72f,.77f);var finish=ActorMaterialProfile.Load();var rows=new List<Variant>();
            foreach(string key in Keys)
            foreach(bool upgraded in key=="hawk"?new[]{false,true}:new[]{key.Contains("Upgraded")||key.Contains("guidance")})
            {
                var model=Object.Instantiate(Resources.Load<GameObject>("StyleA/"+key));model.name=key;
                try
                {
                    bool hawk=key=="hawk";float scale=(float)(hawk?p.HawkModelScale:key.StartsWith("shkval")?p.ShkvalModelRadius*p.ShkvalModelScale/2.05:key.StartsWith("explorer")?p.ExplorerModelRadius*p.ExplorerModelScale/1.0773:p.TankModelRadius*p.TankModelScale/1.585);model.transform.localScale=Vector3.one*scale;model.transform.rotation=Quaternion.Euler(0,180,0);
                    foreach(var r in model.GetComponentsInChildren<Renderer>(true))for(int slot=0;slot<r.sharedMaterials.Length;slot++)finish.Apply(r,slot,owner);
                    HawkModelPresentation visual=null;
                    if(hawk){visual=model.AddComponent<HawkModelPresentation>();visual.SetUpgrade(upgraded);visual.Render(.25,2.25,0,0,(float)p.HawkRotorSpeed);}
                    bool spearhead=key.StartsWith("tankHybrid");
                    if(spearhead)model.AddComponent<TankMechanismPresentation>().Initialize();
                    var active=model.GetComponentsInChildren<Renderer>(true).Where(r=>r.gameObject.activeInHierarchy&&r.enabled&&(r is MeshRenderer||r is SkinnedMeshRenderer)).ToArray();
                    var activeMeshes=active.Select(MeshOf).ToArray();
                    rows.Add(new Variant{key=key,upgraded=upgraded,allMeshRenderers=model.GetComponentsInChildren<Renderer>(true).Length,activeMeshRenderers=active.Length,activeVertices=activeMeshes.Sum(m=>m.vertexCount),activeTriangles=activeMeshes.Sum(m=>m.triangles.Length/3),transientLaunchPulses=hawk?2:0,transientMuzzleFlashes=spearhead?1:0,skinnedRenderers=model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length,meshSignatures=model.GetComponentsInChildren<Renderer>(true).Select(r=>r.transform.parent.name+"/"+FullMeshKey(MeshOf(r))).OrderBy(v=>v,StringComparer.Ordinal).ToArray()});
                    string variant=key+(hawk?(upgraded?"-upgraded":"-base"):"");
                    var bounds=active[0].bounds;foreach(var r in active.Skip(1))bounds.Encapsulate(r.bounds);
                    void Frames(string state)
                    {
                        var selected=Environment.GetEnvironmentVariable("SPACEWARS_UNIT_CAPTURE_KEYS");
                        if(!string.IsNullOrEmpty(selected)&&!selected.Split(',').Contains(key))return;
                        foreach(bool close in new[]{true,false})
                        {
                            camera.orthographicSize=close?Mathf.Max(bounds.size.x,bounds.size.z)*.85f:(float)p.CameraOrthoSize;
                            camera.transform.position=bounds.center+new Vector3(8,11,-13);camera.transform.LookAt(bounds.center);
                            Save(camera,Path.Combine(Output,variant+"-"+state+(close?"-close":"-game-scale")+".png"));
                        }
                    }
                    Frames("idle");
                    if(hawk)
                    {
                        visual.Trigger(0,2.25);visual.Trigger(1,2.25);visual.Render(.25,2.28,9,-12,(float)p.HawkRotorSpeed);Frames("shot");
                        visual.Render(.5,2.5,-7,14,(float)p.HawkRotorSpeed);Frames("reload");
                    }
                }
                finally{Object.DestroyImmediate(model);}
            }
            foreach(string key in UnitModelResources.PreviewKeys)
            {
                var selected=Environment.GetEnvironmentVariable("SPACEWARS_UNIT_CAPTURE_KEYS");
                if(!string.IsNullOrEmpty(selected)&&!selected.Split(',').Contains(key))continue;
                var preview=Object.Instantiate(Resources.Load<GameObject>("StyleA/"+key));
                try
                {
                    preview.transform.localScale=Vector3.one*(float)(p.ExplorerModelRadius*p.ExplorerModelScale/1.0773);preview.transform.rotation=Quaternion.Euler(0,180,0);
                    foreach(var r in preview.GetComponentsInChildren<Renderer>(true))for(int slot=0;slot<r.sharedMaterials.Length;slot++)finish.Apply(r,slot,owner);
                    var rs=preview.GetComponentsInChildren<MeshRenderer>();var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);
                    foreach(bool close in new[]{true,false}){camera.orthographicSize=close?Mathf.Max(b.size.x,b.size.z)*.85f:(float)p.CameraOrthoSize;camera.transform.position=b.center+new Vector3(8,11,-13);camera.transform.LookAt(b.center);Save(camera,Path.Combine(Output,"preview-"+key+(close?"-close":"-reference-scale")+".png"));}
                }
                finally{Object.DestroyImmediate(preview);}
            }
            File.WriteAllText(Path.Combine(Output,"runtime-variants.json"),JsonUtility.ToJson(new Variants{variants=rows.ToArray()},true));
            File.WriteAllText(Path.Combine(Output,"settings.txt"),"Unity="+Application.unityVersion+"; fixed daylight50/-35,fill35/140; owner .19/.72/.77; native profile="+p.ProfileId+"; native default game ortho="+p.CameraOrthoSize+"; rotor time.25/.5, effect time2.25/2.28/2.5; no performance measurement\n");
            Object.DestroyImmediate(floor);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Debug.Log("UNIT_MODEL_AUDIT_PASS "+Output);
        }
        static Mesh MeshOf(Renderer r)=>r is SkinnedMeshRenderer skin?skin.sharedMesh:r.GetComponent<MeshFilter>().sharedMesh;
        static string Bits(float v)=>BitConverter.SingleToInt32Bits(v).ToString("x8");
        static string Matrix(Matrix4x4 m)=>string.Join("",Enumerable.Range(0,16).Select(i=>Bits(m[i])));
        static string NodePath(Transform t)=>t?NodePath(t.parent)+"/"+t.name+":"+t.gameObject.activeSelf:"";
        // Full raw streams include skin weights/indices; bindposes and submesh descriptors
        // are additional contracts. Unlike the reuse key this also admits rigged meshes.
        static string FullMeshKey(Mesh m)
        {
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            using(var data=Mesh.AcquireReadOnlyMeshData(m))
            {
                var d=data[0];writer.Write(m.vertexCount);writer.Write(m.vertexBufferCount);
                foreach(var a in m.GetVertexAttributes()){writer.Write((int)a.attribute);writer.Write((int)a.format);writer.Write(a.dimension);writer.Write(a.stream);}
                for(int i=0;i<m.vertexBufferCount;i++)writer.Write(d.GetVertexData<byte>(i).ToArray());
                writer.Write(d.GetIndexData<byte>().ToArray());
            }
            writer.Write(m.indexFormat.ToString());writer.Write(m.isReadable);writer.Write(m.blendShapeCount);
            foreach(var b in m.bindposes)writer.Write(Matrix(b));
            writer.Write(EditorJsonUtility.ToJson(new BoundsRecord{bounds=m.bounds}));
            for(int i=0;i<m.subMeshCount;i++){var sub=m.GetSubMesh(i);writer.Write(sub.indexStart);writer.Write(sub.indexCount);writer.Write(sub.baseVertex);writer.Write(sub.firstVertex);writer.Write(sub.vertexCount);writer.Write((int)sub.topology);writer.Write(JsonUtility.ToJson(new BoundsRecord{bounds=sub.bounds}));}
            writer.Flush();using var sha=System.Security.Cryptography.SHA256.Create();return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
        }
        [Serializable] class BoundsRecord{public Bounds bounds;}
        static string[] Snapshot()=>UnitModelResources.AllKeys.SelectMany(k=>
        {
            var go=Resources.Load<GameObject>("StyleA/"+k);
            var nodes=go.GetComponentsInChildren<Transform>(true).Select(t=>k+"/node/"+NodePath(t)+"/"+Matrix(t.localToWorldMatrix));
            return nodes.Concat(go.GetComponentsInChildren<Renderer>(true).Select(r=>
            {
                var skin=r as SkinnedMeshRenderer;
                string settings=skin?"skin/"+skin.enabled+"/"+skin.shadowCastingMode+"/"+skin.receiveShadows+"/"+skin.lightProbeUsage+"/"+skin.reflectionProbeUsage+"/"+skin.motionVectorGenerationMode+"/"+skin.quality+"/"+skin.updateWhenOffscreen+"/"+JsonUtility.ToJson(new BoundsRecord{bounds=skin.localBounds})+"/"+NodePath(skin.rootBone)+"/"+string.Join(";",skin.bones.Select(NodePath)):StaticBuildingMeshes.Settings((MeshRenderer)r);
                return k+"/renderer/"+NodePath(r.transform)+"/"+FullMeshKey(MeshOf(r))+"/"+settings+"/"+string.Join(";",r.sharedMaterials.Select(m=>AssetDatabase.GetAssetPath(m)+"/"+m.name+"/"+EditorJsonUtility.ToJson(m)));
            }));
        }).OrderBy(v=>v,StringComparer.Ordinal).ToArray();
        static void Save(Camera camera,string path)
        {
            var target=new RenderTexture(1600,1000,24);var texture=new Texture2D(1600,1000,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1600,1000),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);}
        }
    }
}

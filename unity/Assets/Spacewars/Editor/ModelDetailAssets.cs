using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Spacewars.Editor
{
    public static class ModelDetailAssets
    {
        public static readonly string[] Keys={"tankHybrid","tankHybridUpgraded","explorer","explorerUpgraded","shkval","shkval-guidance-3","hawk","headquarters-a","factory-armored","mine-armored","outpost-b","research-scanner","refinery","refineryUpgraded","sentry-turret","repair-tower","gauss-tower"};
        const string Staging="Assets/Spacewars/Content/LodImport/",Output="Assets/Spacewars/Content/Resources/StyleA/Lod/";
        static Mesh MeshOf(Renderer r)=>TankDistantAssets.MeshOf(r);
        static Transform Closest(Transform node,Transform[] roots){for(var p=node;p;p=p.parent)if(roots.Contains(p))return p;throw new InvalidOperationException("Unowned motion node");}
        static bool Same(Mesh a,Mesh b)=>a.indexFormat==b.indexFormat&&a.bounds.Equals(b.bounds)&&a.subMeshCount==b.subMeshCount&&StaticBuildingMeshes.Fingerprint(a)==StaticBuildingMeshes.Fingerprint(b)&&a.bindposes.SequenceEqual(b.bindposes)&&a.boneWeights.SequenceEqual(b.boneWeights)&&Enumerable.Range(0,a.subMeshCount).All(i=>a.GetIndexStart(i)==b.GetIndexStart(i)&&a.GetIndexCount(i)==b.GetIndexCount(i)&&a.GetTopology(i)==b.GetTopology(i));
        static Mesh SaveMesh(Mesh mesh,string path)
        {
            // Missing historical output may share an exact committed peer. Never
            // overwrite that peer when an explicitly reworked mesh diverges.
            string sharedPath=ExactResourceAliases.Resolve(path);
            if(sharedPath!=path){
                var shared=AssetDatabase.LoadAssetAtPath<Mesh>(sharedPath);
                if(shared&&Same(shared,mesh)){Object.DestroyImmediate(mesh);return shared;}
            }
            var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved){if(!Same(saved,mesh)){
                var allowed=(Environment.GetEnvironmentVariable("SPACEWARS_LOD_REBUILD_KEYS")??"").Split(',');
                if(!allowed.Any(k=>k.Length>0&&path.StartsWith(Output+k+"/",StringComparison.Ordinal)))throw new InvalidOperationException("Saved detail mesh differs: "+path);
                // Explicit reviewed rework only. Keep GUIDs and preserve the
                // superseded bytes at the recorded source commit.
                EditorUtility.CopySerialized(mesh,saved);EditorUtility.SetDirty(saved);
            }Object.DestroyImmediate(mesh);return saved;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        static Dictionary<string,Renderer> Rows(GameObject model)=>model.GetComponentsInChildren<Renderer>(true).ToDictionary(r=>r.name);
        static Material Role(string role,GameObject original)
        {
            var matches=original.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().Where(m=>m.name==role||m.name.EndsWith("-"+role,StringComparison.Ordinal)).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("Ambiguous original material role "+original.name+"/"+role+" count="+matches.Length);
            return matches[0];
        }
        static GameObject Imported(string key,string tier,GameObject original,string folder,List<Mesh> transient)
        {
            string maintained=tier=="normal"?(key=="tankHybrid"?"Assets/Spacewars/Content/StyleA/tankHybridDistant.glb":"../design/unit-building-lod/candidates/"+key+"-distant.glb"):"../design/unit-building-lod/far/"+key+"-distant.glb";
            if(key=="gauss-tower")maintained="../design/gauss-tower/gauss-tower-"+tier+".glb";
            if(key=="repair-tower")maintained="../design/repair-tower/repair-tower-"+tier+".glb";
            string source=key+"-"+tier+".glb",path=Staging+source;
            File.Copy(maintained,path,true);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));model.name=key;
            try{
                var exact=Rows(original);
                foreach(var renderer in model.GetComponentsInChildren<Renderer>(true)){
                    // Duplicate role names can denote distinct upgrade finishes.
                    // Preserve exact per-node/per-slot bindings when the original
                    // renderer exists; global role lookup is only for packed parts.
                    if(exact.TryGetValue(renderer.name,out var baseline)){
                        if(renderer.sharedMaterials.Length!=baseline.sharedMaterials.Length)throw new InvalidOperationException("Source material slot count changed: "+renderer.name);
                        renderer.sharedMaterials=baseline.sharedMaterials;
                    }else renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>Role(m.name,original)).ToArray();
                }
                if(key.StartsWith("tankHybrid",StringComparison.Ordinal))foreach(string side in new[]{"L","R"}){
                    var track=model.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="track"+side);TankHybridAssets.PackTrack(track,key+"-"+tier+"-track"+side,false);transient.Add(track.GetComponent<SkinnedMeshRenderer>().sharedMesh);
                }
                if(key=="mine-armored")StaticBuildingMeshes.CombineMinePerimeter(model,folder);
                if(key=="hawk")UnitModelResources.CombineHawkStaticGroups(model,folder);
                if(key=="sentry-turret"||key=="repair-tower"||key=="gauss-tower"){
                    var nodes=model.GetComponentsInChildren<Transform>(true);var roots=new[]{"modelRoot","turretYaw","weaponPitch"}.Select(n=>nodes.Single(t=>t.name==n)).ToArray();
                    foreach(var motion in roots){var filters=model.GetComponentsInChildren<MeshFilter>(true).Where(f=>Closest(f.transform,roots)==motion).ToArray();StaticBuildingMeshes.CombineUnder(motion,filters,folder,key+"-"+motion.name+"-");}
                }
                return model;
            }catch{Object.DestroyImmediate(model);throw;}
        }
        static Mesh[] Bind(string key,string tier,GameObject original,Renderer[] full)
        {
            string folder=Output+key+"/"+tier+"/";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var transient=new List<Mesh>();GameObject imported=null;
            try{
                imported=Imported(key,tier,original,folder,transient);var rows=Rows(imported);
                if(!full.Select(r=>r.name).OrderBy(n=>n).SequenceEqual(rows.Keys.OrderBy(n=>n)))throw new InvalidOperationException("Runtime layout changed: "+key+"/"+tier);
                var meshes=new Mesh[full.Length];
                for(int i=0;i<full.Length;i++){
                    var near=full[i];var reduced=rows[near.name];
                    if(Vector3.Distance(near.transform.position,reduced.transform.position)>.00002f||Quaternion.Angle(near.transform.rotation,reduced.transform.rotation)>.001f||Vector3.Distance(near.transform.lossyScale,reduced.transform.lossyScale)>.00002f)throw new InvalidOperationException("Node frame changed: "+key+"/"+near.name);
                    if(!near.sharedMaterials.SequenceEqual(reduced.sharedMaterials))throw new InvalidOperationException("Material slots changed: "+key+"/"+near.name);
                    // Preserve complete running gear in both reduced tiers. The
                    // packed skin includes every tread and wheel and shares the
                    // original animated bones; seam-blind collapse erased it.
                    if(key.StartsWith("tankHybrid",StringComparison.Ordinal)&&(near is SkinnedMeshRenderer||near.name.StartsWith("modelRoot",StringComparison.Ordinal))){meshes[i]=MeshOf(near);continue;}
                    var mesh=Object.Instantiate(MeshOf(reduced));mesh.name=key+"-"+tier+"-"+i;mesh.bounds=MeshOf(near).bounds;
                    if(near is SkinnedMeshRenderer skin){var other=(SkinnedMeshRenderer)reduced;if(!skin.bones.Select(t=>t.name).SequenceEqual(other.bones.Select(t=>t.name)))throw new InvalidOperationException("Skin bone order changed");var a=skin.sharedMesh.bindposes;var b=mesh.bindposes;if(a.Length!=b.Length)throw new InvalidOperationException("Bindpose count");for(int n=0;n<a.Length;n++)for(int j=0;j<16;j++)if(Mathf.Abs(a[n][j]-b[n][j])>.00002f)throw new InvalidOperationException("Bindpose frame");mesh.bindposes=a;}
                    meshes[i]=SaveMesh(mesh,folder+"mesh-"+i.ToString("D3")+".asset");
                }
                return meshes;
            }finally{if(imported)Object.DestroyImmediate(imported);foreach(var mesh in transient)Object.DestroyImmediate(mesh);}
        }
        public static void Prepare()
        {
            Directory.CreateDirectory(Output);Directory.CreateDirectory(Staging);AssetDatabase.Refresh();
            try{
                foreach(string key in Keys.Concat(new[]{"hangar"}).Where(k=>string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SPACEWARS_LOD_REBUILD_KEYS"))||Environment.GetEnvironmentVariable("SPACEWARS_LOD_REBUILD_KEYS").Split(',').Contains(k))){
                    var original=Object.Instantiate(Resources.Load<GameObject>("StyleA/"+key));original.name=key;
                    try{
                        var full=original.GetComponentsInChildren<Renderer>(true);var near=full.Select(MeshOf).ToArray();
                        var normal=key=="hangar"?BindHangar(original,full,"normal",24):Bind(key,"normal",original,full);
                        var far=key=="hangar"?BindHangar(original,full,"far",8):Bind(key,"far",original,full);
                        long Tri(Mesh[] meshes)=>meshes.Sum(m=>(long)m.triangles.Length/3);
                        if(!(Tri(near)>Tri(normal)&&Tri(normal)>Tri(far)))throw new InvalidOperationException("Detail geometry must decrease: "+key);
                        var bounds=full[0].bounds;foreach(var r in full.Skip(1))bounds.Encapsulate(r.bounds);
                        original.AddComponent<ModelDetailLod>().Configure(full,near,normal,far,bounds);
                        string prefabPath=Output+key+".prefab";
                        PrefabUtility.SaveAsPrefabAsset(original,prefabPath);
                        // Unity emits trailing spaces for empty YAML names. Keep
                        // these generated prefabs within the repository text contract.
                        File.WriteAllLines(prefabPath,File.ReadAllLines(prefabPath).Select(line=>line.TrimEnd()));
                        Debug.Log("MODEL_DETAIL_ASSET "+key+" "+Tri(near)+"/"+Tri(normal)+"/"+Tri(far)+" renderers="+full.Length);
                    }finally{Object.DestroyImmediate(original);}
                }
                AssetDatabase.SaveAssets();CleanupIntermediates();Debug.Log("MODEL_DETAIL_ASSETS_PASS");
            }finally{AssetDatabase.DeleteAsset(Staging.TrimEnd('/'));}
        }
        public static void CleanupIntermediates()
        {
            var keys=Keys.Concat(new[]{"hangar"}).ToArray();
            var dependencies=new HashSet<string>(keys.SelectMany(k=>AssetDatabase.GetDependencies(Output+k+".prefab",true)));
            foreach(string key in keys)foreach(string tier in new[]{"normal","far"}){
                string folder=Output+key+"/"+tier+"/";
                foreach(string path in AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith(folder,StringComparison.Ordinal)&&(p.EndsWith(".asset",StringComparison.Ordinal)||p.EndsWith(".prefab",StringComparison.Ordinal))).ToArray()){
                    if(dependencies.Contains(path)){
                        if(Path.GetFileName(path).StartsWith("mesh-",StringComparison.Ordinal))continue;
                        throw new InvalidOperationException("Intermediate remains referenced: "+path);
                    }
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }
        [Serializable] public sealed class MetricsRow {public string key;public int renderers,materialSlots;public long originalVertices,normalVertices,farVertices,originalSubmeshes,normalSubmeshes,farSubmeshes,originalTriangles,normalTriangles,farTriangles,originalBytes,addedNormalBytes,addedFarBytes;}
        [Serializable] public sealed class MetricsReport {public string classification="UNITY_ASSET_COST_NOT_MATCH_FPS_OR_EXACT_VRAM";public MetricsRow[] assets;}
        public static void RecordMetrics()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_LOD_METRICS")??throw new InvalidOperationException("Set owned metrics output");
            var rows=new List<MetricsRow>();
            foreach(string key in Keys.Concat(new[]{"hangar"})){
                var lod=Resources.Load<GameObject>("StyleA/Lod/"+key).GetComponent<ModelDetailLod>();
                long Tri(Mesh[] a)=>a.Sum(m=>(long)m.triangles.Length/3);long Bytes(Mesh[] a)=>a.Distinct().Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m));
                long Vert(Mesh[] a)=>a.Sum(m=>(long)m.vertexCount);long Sub(Mesh[] a)=>a.Sum(m=>(long)m.subMeshCount);
                rows.Add(new MetricsRow{key=key,originalVertices=Vert(lod.OriginalMeshes),normalVertices=Vert(lod.NormalMeshes),farVertices=Vert(lod.FarMeshes),originalSubmeshes=Sub(lod.OriginalMeshes),normalSubmeshes=Sub(lod.NormalMeshes),farSubmeshes=Sub(lod.FarMeshes),renderers=lod.Renderers.Length,materialSlots=lod.Renderers.Sum(r=>r.sharedMaterials.Length),originalTriangles=Tri(lod.OriginalMeshes),normalTriangles=Tri(lod.NormalMeshes),farTriangles=Tri(lod.FarMeshes),originalBytes=Bytes(lod.OriginalMeshes),addedNormalBytes=Bytes(lod.NormalMeshes.Except(lod.OriginalMeshes).ToArray()),addedFarBytes=Bytes(lod.FarMeshes.Except(lod.OriginalMeshes).ToArray())});
            }
            File.WriteAllText(output,JsonUtility.ToJson(new MetricsReport{assets=rows.ToArray()},true));Debug.Log("MODEL_DETAIL_METRICS_PASS");
        }
        static Mesh[] BindHangar(GameObject original,Renderer[] full,string tier,int segments)
        {
            string folder=Output+"hangar/"+tier+"/";var prefab=RingHangarAssets.BuildDetail(folder,segments);var model=Object.Instantiate(prefab);
            try{
                var rows=model.GetComponentsInChildren<Renderer>(true).ToDictionary(r=>AnimationUtility.CalculateTransformPath(r.transform,model.transform));var result=new Mesh[full.Length];
                if(rows.Count!=full.Length)throw new InvalidOperationException("Hangar layout changed");
                for(int i=0;i<full.Length;i++){
                    var path=AnimationUtility.CalculateTransformPath(full[i].transform,original.transform);var renderer=rows[path];
                    if(!renderer.sharedMaterials.SequenceEqual(full[i].sharedMaterials))throw new InvalidOperationException("Hangar materials changed");
                    var mesh=Object.Instantiate(MeshOf(renderer));mesh.name="hangar-"+tier+"-"+i;mesh.bounds=MeshOf(full[i]).bounds;
                    result[i]=SaveMesh(mesh,folder+"mesh-"+i.ToString("D3")+".asset");
                }
                return result;
            }finally{Object.DestroyImmediate(model);}
        }
    }
}

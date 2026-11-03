using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Spacewars.Editor
{
    public static class MapModelLodAssets
    {
        const string Folder="Assets/Spacewars/Content/Resources/ModelDetail/";
        static string Input=>Environment.GetEnvironmentVariable("SPACEWARS_MAP_LOD_REVIEW")??throw new InvalidOperationException("Set owned mesh input");
        static JObject Read(string hash,string tier,ZipArchive approved)
        {
            string name=hash+"-"+tier+".json",path=Path.Combine(Input,"reduced",name);
            if(File.Exists(path))return JObject.Parse(File.ReadAllText(path));
            var entry=approved?.GetEntry(name)??throw new InvalidOperationException("Missing generated LOD "+name);
            using var reader=new StreamReader(entry.Open());return JObject.Parse(reader.ReadToEnd());
        }
        static Mesh Save(string hash,string tier,JObject data,Bounds bounds)
        {
            var vs=data["vertices"].ToObject<float[][]>();var ns=data["normals"].ToObject<float[][]>();var uv=data["uv"].ToObject<float[][]>();var colors=data["colors"].ToObject<float[][]>();var subs=data["submeshes"].ToObject<int[][]>();
            var mesh=new Mesh{name=hash+"-"+tier,indexFormat=IndexFormat.UInt32};mesh.vertices=vs.Select(v=>new Vector3(v[0],v[1],v[2])).ToArray();mesh.normals=ns.Select(v=>new Vector3(v[0],v[1],v[2])).ToArray();
            if(uv.Length>0)mesh.uv=uv.Select(v=>new Vector2(v[0],v[1])).ToArray();
            if(colors.Length>0)mesh.colors=colors.Select(v=>new Color(v[0],v[1],v[2],v[3])).ToArray();
            mesh.subMeshCount=subs.Length;for(int i=0;i<subs.Length;i++)mesh.SetIndices(subs[i],MeshTopology.Triangles,i,false);
            mesh.bounds=bounds;if(data.Value<bool>("recalculateTangents")&&uv.Length>0)mesh.RecalculateTangents();FinalProceduralMesh.IndexOwned(mesh);
            string path=Folder+"Meshes/"+hash+"-"+tier+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved){if(saved.bounds!=mesh.bounds||StaticBuildingMeshes.Fingerprint(saved)!=StaticBuildingMeshes.Fingerprint(mesh)){EditorUtility.CopySerialized(mesh,saved);EditorUtility.SetDirty(saved);}Object.DestroyImmediate(mesh);return saved;}
            AssetDatabase.CreateAsset(mesh,path);return mesh;
        }
        // Connected solids are merged only when their local AABBs touch. This
        // joins a vehicle's body/wheels while keeping separate map props compact.
        static Bounds[] Compact(float[][] vertices,int[][] triangles)
        {
            var keys=new Dictionary<(long,long,long),int>();var ids=new int[vertices.Length];var points=new List<Vector3>();var parents=new List<int>();
            for(int i=0;i<vertices.Length;i++){
                var v=vertices[i];var key=((long)Math.Round(v[0]*1e6),(long)Math.Round(v[1]*1e6),(long)Math.Round(v[2]*1e6));
                if(!keys.TryGetValue(key,out int id)){id=points.Count;keys.Add(key,id);points.Add(new Vector3(v[0],v[1],v[2]));parents.Add(id);}ids[i]=id;
            }
            int Find(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
            foreach(var indices in triangles)for(int i=0;i<indices.Length;i+=3)for(int j=1;j<3;j++)parents[Find(ids[indices[i+j]])]=Find(ids[indices[i]]);
            var groups=new Dictionary<int,Bounds>();
            for(int i=0;i<points.Count;i++){int key=Find(i);if(groups.TryGetValue(key,out var box)){box.Encapsulate(points[i]);groups[key]=box;}else groups[key]=new Bounds(points[i],Vector3.zero);}
            var result=groups.Values.ToList();bool changed;
            do{changed=false;for(int i=0;i<result.Count;i++)for(int j=i+1;j<result.Count;j++)if(result[i].Intersects(result[j])){var box=result[i];box.Encapsulate(result[j]);result[i]=box;result.RemoveAt(j--);changed=true;}}while(changed);
            return result.ToArray();
        }
        public static void Bake()
        {
            Directory.CreateDirectory(Folder+"Meshes");AssetDatabase.Refresh();
            string approvedPath=Environment.GetEnvironmentVariable("SPACEWARS_APPROVED_LOD_CANDIDATES");
            using var stream=string.IsNullOrEmpty(approvedPath)?null:File.OpenRead(approvedPath);
            using var approved=stream==null?null:new ZipArchive(stream,ZipArchiveMode.Read);
            var inventory=JArray.Parse(File.ReadAllText(Path.Combine(Input,"inventory.json")));var rows=new List<object>();var generated=new Dictionary<string,ModelDetailCatalog.Entry>();
            foreach(var group in inventory.GroupBy(v=>v.Value<string>("group"))){
                var entries=new List<ModelDetailCatalog.Entry>();var exemptions=new List<ModelDetailCatalog.SourceException>();
                var retainedCatalog=AssetDatabase.LoadAssetAtPath<ModelDetailCatalog>(Folder+group.Key+".asset");
                void Exempt(JToken item,string reason)=>exemptions.Add(new ModelDetailCatalog.SourceException{hash=item.Value<string>("hash"),reason=reason,vertices=item.Value<int>("vertices"),indices=item.Value<int>("triangles")*3,submeshes=item.Value<int>("submeshes")});
                foreach(var item in group.Where(v=>v.Value<bool>("modeled")&&!v.Value<bool>("existingLod")).GroupBy(v=>v.Value<string>("hash")).Select(g=>g.First())){
                    string hash=item.Value<string>("hash");long full=item.Value<long>("triangles");
                    if(full==0){rows.Add(new{group=group.Key,hash,triangles=full,status="EMPTY_AUTHORED_PART_NO_RENDERED_GEOMETRY"});continue;}
                    if(full<100){Exempt(item,"already-small-protected-solids");rows.Add(new{group=group.Key,hash,triangles=full,status="SOURCE_ALREADY_SMALL_NO_SAFE_SOLID_REDUCTION"});continue;}
                    if(generated.TryGetValue(hash,out var previous)){entries.Add(previous);continue;}
                    // A scoped layout bake keeps approved tiers for identical expanded
                    // source geometry. Only new hashes need reduction candidates.
                    if(!File.Exists(Path.Combine(Input,"reduced",hash+"-normal.json"))&&retainedCatalog){
                        var keep=retainedCatalog.entries.FirstOrDefault(e=>e.hash==hash&&e.normal&&e.far);
                        if(keep!=null){var retainedEntry=new ModelDetailCatalog.Entry{hash=hash,vertices=item.Value<int>("vertices"),indices=checked((int)full*3),submeshes=item.Value<int>("submeshes"),normal=keep.normal,far=keep.far,compactBounds=keep.compactBounds};entries.Add(retainedEntry);generated.Add(hash,retainedEntry);rows.Add(new{group=group.Key,hash,status="RETAINED_APPROVED_IDENTICAL_SOURCE"});continue;}
                        var exemption=retainedCatalog.exemptions.FirstOrDefault(e=>e.hash==hash);
                        if(exemption!=null){Exempt(item,exemption.reason);rows.Add(new{group=group.Key,hash,status="RETAINED_MEASURED_SOURCE_EXCEPTION"});continue;}
                    }
                    var source=JObject.Parse(File.ReadAllText(Path.Combine(Input,"source",hash+".json")));var normal=Read(hash,"normal",approved);var far=Read(hash,"far",approved);
                    long Tri(JObject data)=>data["submeshes"].Sum(s=>(long)((JArray)s).Count/3);
                    long n=Tri(normal),f=Tri(far);if(!(full>=n&&n>=f&&f>0))throw new InvalidOperationException("LOD order "+hash);
                    if(full==n&&n==f){Exempt(item,"no-safe-further-solid-reduction");rows.Add(new{group=group.Key,hash,triangles=full,status="SOURCE_SMALL_SOLIDS_RETAINED_NO_BENEFIT"});continue;}
                    var vertices=source["vertices"].ToObject<float[][]>();var bounds=new Bounds(new Vector3(vertices[0][0],vertices[0][1],vertices[0][2]),Vector3.zero);
                    foreach(var v in vertices)bounds.Encapsulate(new Vector3(v[0],v[1],v[2]));
                    var entry=new ModelDetailCatalog.Entry{hash=hash,vertices=vertices.Length,indices=source["submeshes"].Sum(s=>((JArray)s).Count),submeshes=((JArray)source["submeshes"]).Count,normal=Save(hash,"normal",normal,bounds),far=Save(hash,"far",far,bounds),compactBounds=group.Key=="Wrecks"?Array.Empty<Bounds>():Compact(vertices,source["submeshes"].ToObject<int[][]>())};
                    entries.Add(entry);generated.Add(hash,entry);rows.Add(new{group=group.Key,hash,originalTriangles=full,normalTriangles=n,farTriangles=f,compactGroups=entry.compactBounds.Length,status="BAKED"});
                }
                string path=Folder+group.Key+".asset";var catalog=AssetDatabase.LoadAssetAtPath<ModelDetailCatalog>(path);
                if(!catalog){catalog=ScriptableObject.CreateInstance<ModelDetailCatalog>();AssetDatabase.CreateAsset(catalog,path);}catalog.entries=entries.ToArray();catalog.exemptions=exemptions.ToArray();catalog.InvalidateLookup();EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();File.WriteAllText(Path.Combine(Input,"bake.json"),JsonConvert.SerializeObject(rows,Formatting.Indented));OptimizeSavedTiers();Debug.Log("MAP_MODEL_LOD_BAKE_PASS unique="+generated.Count);
        }
        public static void PruneNonRenderedPlaceholders()
        {
            int removed=0;
            foreach(string path in Directory.GetFiles(Folder,"*.asset",SearchOption.AllDirectories).Where(p=>!p.Contains("/Meshes/"))){
                var catalog=AssetDatabase.LoadAssetAtPath<ModelDetailCatalog>(path);var rendered=catalog.exemptions.Where(e=>e.indices>0).ToArray();
                if(rendered.Length==catalog.exemptions.Length)continue;removed+=catalog.exemptions.Length-rendered.Length;catalog.exemptions=rendered;EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();Debug.Log("MAP_MODEL_LOD_EMPTY_PLACEHOLDERS_PRUNED "+removed);
        }
        public static void OptimizeSavedTiers()
        {
            int reused=0;var catalogs=Directory.GetFiles(Folder,"*.asset").Select(AssetDatabase.LoadAssetAtPath<ModelDetailCatalog>).ToArray();
            foreach(var catalog in catalogs)foreach(var entry in catalog.entries){
                if(entry.normal!=entry.far&&entry.normal.vertexCount==entry.far.vertexCount&&StaticBuildingMeshes.Fingerprint(entry.normal)==StaticBuildingMeshes.Fingerprint(entry.far)){entry.far=entry.normal;EditorUtility.SetDirty(catalog);reused++;}
            }
            // A first tank death loads only this variant's reduced meshes, not
            // every wreck type. The top-level bank is an authoring/test index.
            Directory.CreateDirectory(Folder+"Wrecks");AssetDatabase.Refresh();
            var bank=AssetDatabase.LoadAssetAtPath<ModelDetailCatalog>(Folder+"Wrecks.asset");
            foreach(var prefab in Resources.LoadAll<GameObject>("Wrecks")){
                var full=prefab.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray();var hashes=new HashSet<string>(full.Select(ModelDetailCatalog.Identity));
                var entries=full.Select(bank.Find).Where(e=>e!=null).Distinct().ToArray();
                string path=Folder+"Wrecks/"+prefab.name+".asset";var catalog=AssetDatabase.LoadAssetAtPath<ModelDetailCatalog>(path);
                if(!catalog){catalog=ScriptableObject.CreateInstance<ModelDetailCatalog>();AssetDatabase.CreateAsset(catalog,path);}catalog.entries=entries;catalog.exemptions=bank.exemptions.Where(e=>hashes.Contains(e.hash)).ToArray();catalog.InvalidateLookup();EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();
            var paths=Directory.GetFiles(Folder,"*.asset",SearchOption.AllDirectories).Where(p=>!p.Contains("/Meshes/")).ToArray();
            var used=new HashSet<string>(paths.SelectMany(p=>AssetDatabase.GetDependencies(p,true)));
            int removed=0;foreach(string path in Directory.GetFiles(Folder+"Meshes","*.asset"))if(!used.Contains(path)){AssetDatabase.DeleteAsset(path);removed++;}
            File.WriteAllText(Path.Combine(Input,"mesh-reuse.json"),JsonConvert.SerializeObject(new{identicalFarMeshesReused=reused,unreferencedGeneratedMeshesRemoved=removed,wreckCatalogs=26},Formatting.Indented));
            Debug.Log("MAP_MODEL_LOD_OPTIMIZE_PASS reused="+reused+" removed="+removed);
        }
    }
}

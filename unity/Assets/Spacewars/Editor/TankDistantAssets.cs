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
    public static class TankDistantAssets
    {
        const string Folder="Assets/Spacewars/Content/Resources/StyleA/";
        public static Mesh MeshOf(Renderer r)=>r is SkinnedMeshRenderer s?s.sharedMesh:r.GetComponent<MeshFilter>().sharedMesh;
        static bool Same(Mesh a,Mesh b)=>a.indexFormat==b.indexFormat&&a.bounds.Equals(b.bounds)&&
            StaticBuildingMeshes.Fingerprint(a)==StaticBuildingMeshes.Fingerprint(b)&&a.bindposes.SequenceEqual(b.bindposes)&&a.boneWeights.SequenceEqual(b.boneWeights)&&
            Enumerable.Range(0,a.subMeshCount).All(i=>a.GetIndexStart(i)==b.GetIndexStart(i)&&a.GetIndexCount(i)==b.GetIndexCount(i)&&a.GetTopology(i)==b.GetTopology(i));
        static long Triangles(IEnumerable<Renderer> rows)=>rows.Sum(r=>(long)MeshOf(r).triangles.Length/3);
        public static void Prepare()
        {
            const string source="Assets/Spacewars/Content/StyleA/tankHybridDistant.glb";
            AssetDatabase.ImportAsset(source,ImportAssetOptions.ForceSynchronousImport);
            var medium=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(source));
            var basis=Object.Instantiate(Resources.Load<GameObject>("StyleA/tankHybrid"));
            var transient=new List<Mesh>();
            try{
                foreach(var r in medium.GetComponentsInChildren<Renderer>(true))
                    r.sharedMaterials=r.sharedMaterials.Select(m=>AssetDatabase.LoadAssetAtPath<Material>(Folder+"tankHybrid-"+m.name+".mat")??throw new InvalidOperationException("Missing material "+m.name)).ToArray();
                foreach(string side in new[]{"L","R"}){
                    var track=medium.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="track"+side);
                    TankHybridAssets.PackTrack(track,"tankHybrid-distant-track"+side,false);transient.Add(track.GetComponent<SkinnedMeshRenderer>().sharedMesh);
                }
                var original=basis.GetComponentsInChildren<Renderer>(true).ToDictionary(r=>r.name);
                var reduced=medium.GetComponentsInChildren<Renderer>(true).ToDictionary(r=>r.name);
                if(Triangles(original.Values)!=51612||Triangles(reduced.Values)!=25802||!original.Keys.OrderBy(x=>x).SequenceEqual(reduced.Keys.OrderBy(x=>x)))throw new InvalidOperationException("Approved distant representation changed");
                var meshes=new Dictionary<string,Mesh>();
                foreach(var row in original){
                    var near=row.Value;var far=reduced[row.Key];
                    if(Vector3.Distance(near.transform.position,far.transform.position)>.00002f||Quaternion.Angle(near.transform.rotation,far.transform.rotation)>.001f||Vector3.Distance(near.transform.lossyScale,far.transform.lossyScale)>.00002f)throw new InvalidOperationException("Distant node frame changed "+row.Key);
                    if(!near.sharedMaterials.SequenceEqual(far.sharedMaterials))throw new InvalidOperationException("Distant materials changed "+row.Key);
                    var mesh=Object.Instantiate(MeshOf(far));mesh.name="tankHybrid-distant-"+row.Key;
                    // Keep the original conservative culling envelope, including animated tracks.
                    mesh.bounds=MeshOf(near).bounds;
                    if(near is SkinnedMeshRenderer skin){
                        var farSkin=(SkinnedMeshRenderer)far;
                        if(!skin.bones.Select(t=>t.name).SequenceEqual(farSkin.bones.Select(t=>t.name)))throw new InvalidOperationException("Distant bone order changed");
                        var a=skin.sharedMesh.bindposes;var b=mesh.bindposes;
                        if(a.Length!=b.Length)throw new InvalidOperationException("Distant bindpose count");
                        for(int n=0;n<a.Length;n++)for(int i=0;i<16;i++)if(Mathf.Abs(a[n][i]-b[n][i])>.00002f)throw new InvalidOperationException("Distant bindpose frame");
                        mesh.bindposes=a;
                    }
                    string path=Folder+mesh.name+".asset";
                    var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if(saved){if(!Same(saved,mesh))throw new InvalidOperationException("Existing distant mesh differs; review source before replacing "+path);Object.DestroyImmediate(mesh);mesh=saved;}
                    else AssetDatabase.CreateAsset(mesh,path);
                    meshes.Add(row.Key,mesh);
                }
                foreach(string key in new[]{"tankHybrid"}){
                    var model=Object.Instantiate(Resources.Load<GameObject>("StyleA/"+key));model.name=key+"Lod";
                    try{
                        var full=model.GetComponentsInChildren<Renderer>(true);var fullMeshes=full.Select(MeshOf).ToArray();var farMeshes=new List<Mesh>();int simplified=0;
                        foreach(var near in full){
                            if(!near.enabled)throw new InvalidOperationException("Unexpected disabled baseline renderer");
                            Mesh mesh=MeshOf(near);
                            // Upgraded-only geometry stays full. Only exactly shared base parts
                            // use the approved simplified shape; no unreviewed upgrade decimation.
                            if(original.TryGetValue(near.name,out var baseline)&&Same(mesh,MeshOf(baseline))){mesh=meshes[near.name];simplified++;}
                            farMeshes.Add(mesh);
                        }
                        if(simplified!=original.Count)throw new InvalidOperationException("Variant does not share every approved base part: "+key+" "+simplified);
                        var bounds=full[0].bounds;foreach(var r in full.Skip(1))bounds.Encapsulate(r.bounds);
                        model.AddComponent<TankDistantLod>().Configure(full,fullMeshes,farMeshes.ToArray(),bounds);
                        long distantTriangles=farMeshes.Sum(m=>(long)m.triangles.Length/3);
                        if(Triangles(full)-distantTriangles!=25810)throw new InvalidOperationException("Variant simplification changed");
                        PrefabUtility.SaveAsPrefabAsset(model,Folder+key+"Lod.prefab");
                        Debug.Log("TANK_DISTANT_ASSET "+key+" near="+Triangles(full)+" distant="+distantTriangles+" renderers="+full.Length+" sharedRig=true");
                    }finally{Object.DestroyImmediate(model);}
                }
                AssetDatabase.SaveAssets();Debug.Log("TANK_DISTANT_ASSETS_PASS");
            }finally{Object.DestroyImmediate(medium);Object.DestroyImmediate(basis);foreach(var mesh in transient)Object.DestroyImmediate(mesh);}
        }
    }
}

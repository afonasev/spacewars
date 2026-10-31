using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Spacewars.Presentation;
using Object=UnityEngine.Object;

namespace Spacewars.Editor
{
    // Reuse reduced meshes of the approved scanner wherever geometry is identical.
    // Upgrade-only mechanisms retain exact meshes: simplifying their small motion
    // groups without authored replacements would risk the approved silhouette.
    public static class CenterUpgradeAssets
    {
        public static void Prepare()
        {
            var baseline=Resources.Load<GameObject>("StyleA/Lod/research-scanner").GetComponent<ModelDetailLod>();
            var model=Object.Instantiate(Resources.Load<GameObject>("StyleA/research-scanner-upgraded"));
            try {
                model.name="research-scanner-upgraded";
                var renderers=model.GetComponentsInChildren<Renderer>(true);
                var full=renderers.Select(r=>r.GetComponent<MeshFilter>().sharedMesh).ToArray();
                var normal=new Mesh[full.Length];var far=new Mesh[full.Length];int reused=0;
                for(int i=0;i<full.Length;i++){
                    string hash=StaticBuildingMeshes.Fingerprint(full[i]);
                    int match=Array.FindIndex(baseline.OriginalMeshes,m=>StaticBuildingMeshes.Fingerprint(m)==hash);
                    normal[i]=match<0?full[i]:baseline.NormalMeshes[match];far[i]=match<0?full[i]:baseline.FarMeshes[match];
                    if(match>=0)reused++;
                }
                long Tri(Mesh[] meshes)=>meshes.Sum(m=>(long)m.triangles.Length/3);
                if(!(Tri(full)>Tri(normal)&&Tri(normal)>Tri(far)))throw new InvalidOperationException("Center detail must decrease");
                var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                model.AddComponent<ModelDetailLod>().Configure(renderers,full,normal,far,bounds);
                const string path="Assets/Spacewars/Content/Resources/StyleA/Lod/research-scanner-upgraded.prefab";
                PrefabUtility.SaveAsPrefabAsset(model,path);File.WriteAllLines(path,File.ReadAllLines(path).Select(l=>l.TrimEnd()));
                AssetDatabase.SaveAssets();
                Debug.Log("CENTER_UPGRADE_LOD triangles="+Tri(full)+"/"+Tri(normal)+"/"+Tri(far)+" renderers="+renderers.Length+" reused="+reused+" materialSlots="+renderers.Sum(r=>r.sharedMaterials.Length));
            }finally{Object.DestroyImmediate(model);}
        }
    }
}

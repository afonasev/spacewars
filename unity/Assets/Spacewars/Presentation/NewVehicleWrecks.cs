using System;
using System.Linq;
using Spacewars.Simulation;
using UnityEngine;
namespace Spacewars.Presentation
{
    // Immutable render-only tiers derived from each live model's authored LOD.
    internal static class NewVehicleWrecks
    {
        internal static GameObject Create(Transform parent,PlayableEntityKind kind,int variant)
        {
            string folder=kind==PlayableEntityKind.Skat?"Skat":"Igla";
            var root=new GameObject(folder+" wreck");root.transform.SetParent(parent,false);
            var tiers=new[]{"close","normal","far"}.Select(t=>{
                string key="StyleA/"+folder+"/wreck-"+variant+"-"+t;
                var asset=Resources.Load<GameObject>(key);
                if(!asset)throw new InvalidOperationException("Missing wreck tier "+key);
                return UnityEngine.Object.Instantiate(asset,root.transform,false);
            }).ToArray();
            foreach(var tier in tiers)foreach(var renderer in tier.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>{
                    string role=m.name.StartsWith("wreck-neutral",StringComparison.Ordinal)?"neutral":m.name.StartsWith("wreck-paint",StringComparison.Ordinal)?"team":"structure";
                    return Resources.Load<Material>("Wrecks/tank-1-wreck-"+role)??throw new InvalidOperationException("Missing charred wreck material "+role);
                }).ToArray();
            var bounds=new Bounds();bool first=true;
            foreach(var filter in tiers[0].GetComponentsInChildren<MeshFilter>(true))
                foreach(var corner in Corners(filter.sharedMesh.bounds)){
                    var p=root.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);
                }
            root.AddComponent<ModelDetailLod>().ConfigureGroups(tiers,bounds);
            return root;
        }
        static System.Collections.Generic.IEnumerable<Vector3> Corners(Bounds b)
        {for(int i=0;i<8;i++)yield return b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));}
    }
}

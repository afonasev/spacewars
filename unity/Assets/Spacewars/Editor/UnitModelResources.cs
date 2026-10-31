using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Spacewars.Editor
{
    public static class UnitModelResources
    {
        public static readonly string[] Keys={"tankHybrid","tankHybridUpgraded","explorer","explorerUpgraded","shkval","shkval-guidance-3","hawk"};
        public static readonly string[] LegacyKeys={"tank","tankUpgraded"};
        public static readonly string[] PreviewKeys={"explorer-twin-cannon"};
        public static IEnumerable<string> AllKeys=>Keys.Concat(PreviewKeys).Concat(LegacyKeys);
        const string Folder="Assets/Spacewars/Content/Resources/StyleA/";
        public static Dictionary<string,string> CombineHawkStaticGroups(GameObject model,string folder)
        {
            var nodes=model.GetComponentsInChildren<Transform>(true);
            var baseRoot=nodes.Single(t=>t.name=="baseOnly");var upgradeRoot=nodes.Single(t=>t.name=="upgradedOnly");
            // Explicit current static assemblies. Rotors/nozzles/reload/sockets and
            // transient launch pulses are never folded into these toggle roots.
            var allowed=new HashSet<string>{"baseOnly","rackLeft","rackRight","rocketLeft1","rocketLeft2","rocketRight1","rocketRight2"};
            bool StaticBase(MeshFilter f){for(var p=f.transform.parent;p&&p!=baseRoot.parent;p=p.parent)if(!allowed.Contains(p.name))return false;return true;}
            var bases=baseRoot.GetComponentsInChildren<MeshFilter>(true).Where(StaticBase).ToArray();
            var staticPods=nodes.Where(t=>t.name=="podLeft"||t.name=="podRight"||t.name=="targetingHead").ToArray();
            var upgrades=upgradeRoot.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.transform.parent==upgradeRoot||staticPods.Any(p=>f.transform.IsChildOf(p))).ToArray();
            var expected=StaticBuildingMeshes.CombineUnder(baseRoot,bases,folder,"hawk-base-static-");
            foreach(var row in StaticBuildingMeshes.CombineUnder(upgradeRoot,upgrades,folder,"hawk-upgraded-static-"))expected.Add(row.Key,row.Value);
            return expected;
        }
        public static string ExactMeshKey(Mesh mesh)
        {
            if(!mesh||!mesh.isReadable||mesh.subMeshCount!=1||mesh.blendShapeCount!=0||mesh.bindposes.Length!=0)return null;
            var b=mesh.bounds;string Bits(float v)=>BitConverter.SingleToInt32Bits(v).ToString("x8");
            return StaticBuildingMeshes.Fingerprint(mesh)+"/"+mesh.indexFormat+"/"+Bits(b.center.x)+Bits(b.center.y)+Bits(b.center.z)+Bits(b.extents.x)+Bits(b.extents.y)+Bits(b.extents.z);
        }
        // Baked resources never point at another variant's mutable GLB mesh.
        // Existing content-addressed assets are validated, never overwritten.
        public static Mesh ImmutableSharedMesh(Mesh source,string folder)
        {
            string key=ExactMeshKey(source);if(key==null)throw new InvalidOperationException("Unsupported shared mesh source");
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","").ToLowerInvariant();
            string name="unit-shared-"+hash;string path=folder+name+".asset";
            var shared=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(shared)
            {
                if(ExactMeshKey(shared)!=key)throw new InvalidOperationException("Immutable shared mesh content mismatch: "+path);
                return shared;
            }
            if(File.Exists(path))throw new InvalidOperationException("Existing immutable shared path is not a valid Mesh: "+path);
            if(!Directory.Exists(folder)){Directory.CreateDirectory(folder);AssetDatabase.Refresh();}
            shared=Object.Instantiate(source);shared.name=name;AssetDatabase.CreateAsset(shared,path);AssetDatabase.SaveAssets();AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            shared=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(!shared||ExactMeshKey(shared)!=key)throw new InvalidOperationException("Saved immutable shared mesh mismatch: "+path);
            return shared;
        }
        public static int ReuseExactVariantMeshes()
        {
            int rebound=0;var models=new Dictionary<string,GameObject>();var before=new Dictionary<string,string[]>();
            try
            {
                foreach(string key in AllKeys)
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+key+".prefab");if(!prefab)throw new InvalidOperationException("Missing active unit prefab "+key);
                    var model=Object.Instantiate(prefab);model.name=key;models.Add(key,model);
                    before.Add(key,model.GetComponentsInChildren<MeshFilter>(true).Select(f=>ExactMeshKey(f.sharedMesh)).ToArray());
                }
                var groups=models.Values.SelectMany(m=>m.GetComponentsInChildren<MeshFilter>(true)).Select(f=>new{Filter=f,Key=ExactMeshKey(f.sharedMesh)}).Where(x=>x.Key!=null).GroupBy(x=>x.Key);
                foreach(var group in groups)
                {
                    var refs=group.ToArray();if(refs.Length<2)continue;
                    var shared=ImmutableSharedMesh(refs[0].Filter.sharedMesh,Folder+"SharedUnitMeshes/");
                    foreach(var item in refs)if(item.Filter.sharedMesh!=shared){item.Filter.sharedMesh=shared;rebound++;}
                }
                foreach(var row in models)
                {
                    string path=Folder+row.Key+".prefab";
                    // Saving all prepared variants also validates the immutable
                    // bindings after a source edit and explicit regeneration.
                    PrefabUtility.SaveAsPrefabAsset(row.Value,path);AssetDatabase.SaveAssets();AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                    var after=AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<MeshFilter>(true).Select(f=>ExactMeshKey(f.sharedMesh)).ToArray();
                    if(!before[row.Key].SequenceEqual(after))throw new InvalidOperationException("Saved unit mesh attributes/bounds/index format changed "+row.Key);
                }
            }
            finally{foreach(var model in models.Values)Object.DestroyImmediate(model);}
            Debug.Log("UNIT_EXACT_MESH_REUSE_PASS immutable rebound="+rebound);return rebound;
        }
    }
}

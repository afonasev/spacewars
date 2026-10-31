using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Object=UnityEngine.Object;
namespace Spacewars.Editor
{
    public static class GaussTowerModelAssets
    {
        const string Folder="Assets/Spacewars/Content/Resources/StyleA/";
        static string Output=>Path.GetFullPath("../.local/gauss-tower/model");
        static Transform Motion(Transform node,Transform[] roots){for(var p=node;p;p=p.parent)if(roots.Contains(p))return p;throw new InvalidOperationException("Unbound gauss motion");}
        [Serializable]sealed class Counts{public int vertices,triangles,renderers,submissions,materials;public long meshBytes;}
        static Counts Measure(GameObject model){var f=model.GetComponentsInChildren<MeshFilter>(true);var r=model.GetComponentsInChildren<MeshRenderer>(true);return new Counts{vertices=f.Sum(x=>x.sharedMesh.vertexCount),triangles=f.Sum(x=>x.sharedMesh.triangles.Length/3),renderers=r.Length,submissions=r.Sum(x=>x.sharedMaterials.Length),materials=r.SelectMany(x=>x.sharedMaterials).Distinct().Count(),meshBytes=f.Select(x=>x.sharedMesh).Distinct().Sum(x=>Profiler.GetRuntimeMemorySizeLong(x))};}
        [MenuItem("Spacewars/Assets/Gauss Tower/Prepare")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Output);AssetDatabase.Refresh();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/StyleA/gauss-tower.glb");if(!source)throw new InvalidOperationException("Gauss GLB missing");
            var model=Object.Instantiate(source);model.name="gauss-tower";
            try
            {
                foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(material=>{
                        string role=material.name,path=Folder+"gauss-tower-"+role+".mat";
                        var saved=AssetDatabase.LoadAssetAtPath<Material>(path);if(saved)return saved;
                        var template=AssetDatabase.LoadAssetAtPath<Material>(Folder+"sentry-turret-"+role+".mat");
                        saved=template?new Material(template):new Material(Shader.Find("Universal Render Pipeline/Lit"));saved.name="gauss-tower-"+role;
                        if(!template){if(role!="gauss-emissive")throw new InvalidOperationException("Unknown gauss role "+role);var green=Color.white;saved.SetColor("_BaseColor",green);saved.EnableKeyword("_EMISSION");saved.SetColor("_EmissionColor",green);}
                        AssetDatabase.CreateAsset(saved,path);return saved;
                    }).ToArray();
                }
                model.AddComponent<SentryTurretRig>().Bind();var before=Measure(model);
                var transforms=model.GetComponentsInChildren<Transform>(true);var roots=new[]{"modelRoot","turretYaw","weaponPitch"}.Select(n=>transforms.Single(t=>t.name==n)).ToArray();
                foreach(var root in roots)StaticBuildingMeshes.CombineUnder(root,model.GetComponentsInChildren<MeshFilter>(true).Where(f=>Motion(f.transform,roots)==root).ToArray(),Folder,"gauss-tower-"+root.name+"-");
                var after=Measure(model);
                if(before.triangles!=after.triangles||before.vertices!=after.vertices||after.renderers>=before.renderers)throw new InvalidOperationException("Gauss packing did not preserve geometry with fewer renderers");
                File.WriteAllText(Path.Combine(Output,"before.json"),JsonUtility.ToJson(before,true));File.WriteAllText(Path.Combine(Output,"after.json"),JsonUtility.ToJson(after,true));
                PrefabUtility.SaveAsPrefabAsset(model,Folder+"gauss-tower.prefab");AssetDatabase.SaveAssets();
                File.WriteAllLines(Folder+"gauss-tower.prefab",File.ReadAllLines(Folder+"gauss-tower.prefab").Select(line=>line.TrimEnd()));
            }
            finally{Object.DestroyImmediate(model);}
            Environment.SetEnvironmentVariable("SPACEWARS_LOD_REBUILD_KEYS","gauss-tower");ModelDetailAssets.Prepare();
            File.WriteAllText(Path.Combine(Output,"PASS.txt"),"Lossless close batching plus authored normal/far detail through shared ModelDetailProfile. No FPS claim.\n");
        }
    }
}

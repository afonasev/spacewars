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
    public static class RepairTowerModelAssets
    {
        const string Folder="Assets/Spacewars/Content/Resources/StyleA/";
        static string Output=>Path.GetFullPath("../.local/repair-tower/model");
        static Transform Motion(Transform node,Transform[] roots){for(var p=node;p;p=p.parent)if(roots.Contains(p))return p;throw new InvalidOperationException("Unbound repair motion");}
        [Serializable]sealed class Counts{public int vertices,triangles,renderers,submissions,materials;public long meshBytes;}
        static Counts Measure(GameObject model){var f=model.GetComponentsInChildren<MeshFilter>(true);var r=model.GetComponentsInChildren<MeshRenderer>(true);return new Counts{vertices=f.Sum(x=>x.sharedMesh.vertexCount),triangles=f.Sum(x=>x.sharedMesh.triangles.Length/3),renderers=r.Length,submissions=r.Sum(x=>x.sharedMaterials.Length),materials=r.SelectMany(x=>x.sharedMaterials).Distinct().Count(),meshBytes=f.Select(x=>x.sharedMesh).Distinct().Sum(x=>Profiler.GetRuntimeMemorySizeLong(x))};}
        static DateTime reviewStarted;
        [InitializeOnLoadMethod]static void ResumeReviewWatch()
        {
            if(!Environment.GetCommandLineArgs().Contains("Spacewars.Editor.RepairTowerModelAssets.RecordInPlayMode"))return;
            reviewStarted=DateTime.UtcNow;EditorApplication.update-=WatchReview;EditorApplication.update+=WatchReview;
        }
        static void WatchReview()
        {
            var output=Environment.GetEnvironmentVariable("SPACEWARS_REPAIR_REVIEW");if(string.IsNullOrEmpty(output))return;
            if(File.Exists(Path.Combine(output,"FAIL.txt")))EditorApplication.Exit(1);
            else if(File.Exists(Path.Combine(output,"PASS.txt")))EditorApplication.Exit(0);
            else if((DateTime.UtcNow-reviewStarted).TotalSeconds>240){Debug.LogError("Repair review did not complete within owned capture deadline");EditorApplication.Exit(1);}
        }
        public static void RecordInPlayMode()
        {
            var output=Environment.GetEnvironmentVariable("SPACEWARS_REPAIR_REVIEW");
            if(string.IsNullOrEmpty(output)||File.Exists(Path.Combine(output,"PASS.txt"))||File.Exists(Path.Combine(output,"FAIL.txt")))throw new InvalidOperationException("Fresh owned repair review output required");
            DockPortraitBaker.BakeRepairTower();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(PlayableProject.ScenePath);
            ResumeReviewWatch();EditorApplication.EnterPlaymode();
        }
        public static void BuildReview()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_REPAIR_BUILD_OUTPUT");
            if(string.IsNullOrEmpty(output))throw new UnityEditor.Build.BuildFailedException("Owned review output required");
            DockPortraitBaker.BakeRepairTower();
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{PlayableProject.ScenePath},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
            if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new UnityEditor.Build.BuildFailedException(report.summary.result.ToString());
            Debug.Log("REPAIR_REVIEW_BUILD_PASS bytes="+report.summary.totalSize);
        }
        [MenuItem("Spacewars/Assets/Repair Tower/Prepare")]
        public static void Prepare()
        {
            Directory.CreateDirectory(Output);AssetDatabase.Refresh();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/StyleA/repair-tower.glb");if(!source)throw new InvalidOperationException("Repair GLB missing");
            var model=Object.Instantiate(source);model.name="repair-tower";
            try
            {
                foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(material=>{
                        string role=material.name,path=Folder+"repair-tower-"+role+".mat";
                        var saved=AssetDatabase.LoadAssetAtPath<Material>(path);if(saved)return saved;
                        var template=AssetDatabase.LoadAssetAtPath<Material>(Folder+"sentry-turret-"+role+".mat");
                        saved=template?new Material(template):new Material(Shader.Find("Universal Render Pipeline/Lit"));saved.name="repair-tower-"+role;
                        if(!template){if(role!="repair-emissive")throw new InvalidOperationException("Unknown repair role "+role);var green=new Color(.02f,1,.08f);saved.SetColor("_BaseColor",green);saved.EnableKeyword("_EMISSION");saved.SetColor("_EmissionColor",green);}
                        AssetDatabase.CreateAsset(saved,path);return saved;
                    }).ToArray();
                }
                model.AddComponent<SentryTurretRig>().Bind();var before=Measure(model);
                var transforms=model.GetComponentsInChildren<Transform>(true);var roots=new[]{"modelRoot","turretYaw","weaponPitch"}.Select(n=>transforms.Single(t=>t.name==n)).ToArray();
                foreach(var root in roots)StaticBuildingMeshes.CombineUnder(root,model.GetComponentsInChildren<MeshFilter>(true).Where(f=>Motion(f.transform,roots)==root).ToArray(),Folder,"repair-tower-"+root.name+"-");
                var after=Measure(model);
                if(before.triangles!=after.triangles||before.vertices!=after.vertices||after.renderers>=before.renderers)throw new InvalidOperationException("Repair packing did not preserve geometry with fewer renderers");
                File.WriteAllText(Path.Combine(Output,"before.json"),JsonUtility.ToJson(before,true));File.WriteAllText(Path.Combine(Output,"after.json"),JsonUtility.ToJson(after,true));
                PrefabUtility.SaveAsPrefabAsset(model,Folder+"repair-tower.prefab");AssetDatabase.SaveAssets();
                File.WriteAllLines(Folder+"repair-tower.prefab",File.ReadAllLines(Folder+"repair-tower.prefab").Select(line=>line.TrimEnd()));
            }
            finally{Object.DestroyImmediate(model);}
            Environment.SetEnvironmentVariable("SPACEWARS_LOD_REBUILD_KEYS","repair-tower");ModelDetailAssets.Prepare();
            File.WriteAllText(Path.Combine(Output,"PASS.txt"),"Lossless close batching plus authored normal/far detail through shared ModelDetailProfile. No FPS claim.\n");
        }
    }
}

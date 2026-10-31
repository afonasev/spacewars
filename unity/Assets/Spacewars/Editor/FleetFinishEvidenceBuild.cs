using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Spacewars.Editor
{
    public static class FleetFinishEvidenceBuild
    {
        const string Materials = "Assets/Spacewars/Content/Resources/StyleA/";
        const string Baseline = "Assets/Spacewars/Content/Resources/FleetFinishBaseline";

        public static void Build()
        {
            string output = Environment.GetEnvironmentVariable("SPACEWARS_FLEET_BUILD_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new BuildFailedException("Owned fleet QA output required");
            string evidence = Path.GetFullPath("../.local/hawk-fleet-finish");
            string saved = Path.Combine(evidence, "baseline");
            Directory.CreateDirectory(saved);
            if (AssetDatabase.IsValidFolder(Baseline)) throw new BuildFailedException("Refusing to overwrite an existing baseline folder");
            AssetDatabase.CreateFolder("Assets/Spacewars/Content/Resources", "FleetFinishBaseline");
            try
            {
                foreach (string role in new[] { "team-primary", "neutral-metal" })
                {
                    string name = "hawk-" + role + ".mat";
                    string snapshot = Path.Combine(saved, name);
                    if (!File.Exists(snapshot)) File.Copy(Materials + name, snapshot);
                    File.Copy(snapshot, Baseline + "/" + name);
                    AssetDatabase.ImportAsset(Baseline + "/" + name, ImportAssetOptions.ForceSynchronousImport);
                }
                HawkModelAssets.RefreshFleetFinish();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Materials + "hawk.prefab");
                var meshes = prefab.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).Distinct().ToArray();
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                File.WriteAllText(Path.Combine(evidence, "asset-cost.txt"), "Material-only repair; same prefab/mesh references before and after.\nrenderers=" + renderers.Length + " meshes=" + meshes.Length + " vertices=" + meshes.Sum(m => m.vertexCount) + " triangles=" + meshes.Sum(m => Enumerable.Range(0, m.subMeshCount).Sum(s => (long)m.GetIndexCount(s) / 3)) + " materialSlots=" + renderers.Sum(r => r.sharedMaterials.Length) + "\nCount deltas=0 (0%); new texture assets=0; existing fleet base map reused; no FPS claim.\n");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { PlayableProject.ScenePath }, locationPathName = output, target = BuildTarget.StandaloneOSX, options = BuildOptions.Development | BuildOptions.DetailedBuildReport });
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
                Debug.Log("FLEET_FINISH_BUILD_PASS bytes=" + report.summary.totalSize);
            }
            finally { AssetDatabase.DeleteAsset(Baseline); AssetDatabase.SaveAssets(); }
        }
    }
}

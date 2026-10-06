using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Spacewars.StyleA.Preview;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Editor
{
    public static class StyleAMaterialImportProject
    {
        public const string Evidence = "../.local/style-a-material-import/";
        public const string RepairEvidence = "../.local/style-a-emission-import/";
        private static string recordRoot = Evidence;
        public static MaterialSourceManifest Sources() => JsonUtility.FromJson<MaterialSourceManifest>(File.ReadAllText("Assets/Spacewars/Content/StyleA/source-materials.json"));
        public static string Hash(string path) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant(); }
        [Serializable] private sealed class ImportRow { public string path, sha256, yaml; public MaterialState state; }
        [Serializable] private sealed class ImportReport { public string stage; public ImportRow[] rows; }
        private static void Record(string stage)
        {
            foreach(var source in Sources().rows) AssetDatabase.LoadAssetAtPath<Material>(source.path);
            AssetDatabase.SaveAssets();
            var report=new ImportReport {stage=stage,rows=Sources().rows.Select(s=>new ImportRow {
                path=s.path,sha256=Hash(s.path),yaml=File.ReadAllText(s.path),
                state=MaterialImportComparison.Read(AssetDatabase.LoadAssetAtPath<Material>(s.path),s.name)}).ToArray()};
            Directory.CreateDirectory(recordRoot+"native/");
            File.WriteAllText(recordRoot+"native/"+stage+".json",JsonUtility.ToJson(report,true));
        }
        private static int repairStage, repairSettle;
        public static void ProbeRepair()
        {
            recordRoot=RepairEvidence;
            repairStage=0;repairSettle=30;
            EditorApplication.update+=ProbeRepairUpdate;
        }
        private static void ProbeRepairUpdate()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(--repairSettle>0)return;
            try
            {
                Record("repair-import-"+repairStage);
                if(repairStage==2)
                {
                    EditorApplication.update-=ProbeRepairUpdate;
                    Debug.Log("MATERIAL_REPAIR_PROBE_COMPLETE");EditorApplication.Exit(0);return;
                }
                foreach(var row in Sources().rows)
                    AssetDatabase.ImportAsset(row.path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                repairStage++;repairSettle=30;
            }
            catch(Exception error)
            {
                EditorApplication.update-=ProbeRepairUpdate;Debug.LogException(error);EditorApplication.Exit(1);
            }
        }
        public static void BuildRepair()
        {
            recordRoot=RepairEvidence;
            Build();
        }
        private static int probeStage, settleUpdates;
        public static void Probe()
        {
            probeStage=0;settleUpdates=30;
            EditorApplication.update+=ProbeUpdate;
        }
        private static void ProbeUpdate()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(--settleUpdates>0)return;
            try
            {
                Record(probeStage==0?"initial-import":"restored-import-"+probeStage);
                if(probeStage==2)
                {
                    EditorApplication.update-=ProbeUpdate;
                    Debug.Log("MATERIAL_IMPORT_PROBE_COMPLETE");EditorApplication.Exit(0);return;
                }
                foreach(var row in Sources().rows)
                {
                    File.WriteAllText(row.path,row.sourceYaml);
                    if(Hash(row.path)!=row.sourceSha256)throw new InvalidOperationException("Restored source hash mismatch");
                    AssetDatabase.ImportAsset(row.path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                }
                probeStage++;settleUpdates=30;
            }
            catch(Exception error)
            {
                EditorApplication.update-=ProbeUpdate;Debug.LogException(error);EditorApplication.Exit(1);
            }
        }
        public static void Build()
        {
            const string scene="Assets/Spacewars/Content/StyleA/Preview/StyleAMaterialImport.generated.unity";
            const string variantsPath="Assets/Spacewars/Content/StyleA/Preview/StyleAMaterialImport.generated.shadervariants";
            var oldName=PlayerSettings.productName;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var camera=new GameObject("Material diagnostic camera").AddComponent<Camera>();
                camera.gameObject.AddComponent<UniversalAdditionalCameraData>();camera.orthographic=true;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.065f,.085f);
                var audit=camera.gameObject.AddComponent<StyleAMaterialImportAudit>();audit.auditCamera=camera;
                Record("prebuild-import");
                audit.sources=Sources().rows;audit.imported=audit.sources.Select(s=>AssetDatabase.LoadAssetAtPath<Material>(s.path)).ToArray();
                if(audit.imported.Any(m=>!m))throw new BuildFailedException("Missing material");
                audit.prefabs=StyleALodAuditProject.LoadPrefabs();
                var variants=new ShaderVariantCollection();
                foreach(var m in audit.imported)
                    foreach(var emission in new[]{false,true})
                    {
                        var keys=m.shaderKeywords.Where(k=>k!="_EMISSION").ToList();if(emission)keys.Add("_EMISSION");
                        variants.Add(new ShaderVariantCollection.ShaderVariant(m.shader,PassType.ScriptableRenderPipeline,keys.ToArray()));
                    }
                AssetDatabase.CreateAsset(variants,variantsPath);audit.diagnosticVariants=variants;
                var light=new GameObject("Fixed diagnostic light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;
                light.transform.rotation=Quaternion.Euler(48,-38,0);
                PlayerSettings.productName="Spacewars Material Import Audit";
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene);
                var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{scene},locationPathName="Builds/macOS/StyleAMaterialImport.app",
                    target=BuildTarget.StandaloneOSX,options=BuildOptions.Development|BuildOptions.DetailedBuildReport});
                if(result.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(result.summary.result.ToString());
                Record("built-import");Debug.Log("MATERIAL_IMPORT_BUILD_PASS bytes="+result.summary.totalSize);
            }
            finally
            {
                PlayerSettings.productName=oldName;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scene);AssetDatabase.DeleteAsset(variantsPath);AssetDatabase.SaveAssets();
            }
        }
    }
}

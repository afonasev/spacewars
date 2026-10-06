using System.IO;
using System.Linq;
using Spacewars.StyleA.Preview;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Editor
{
    public static class StyleALodAuditProject
    {
        public static GameObject[] LoadPrefabs() => StyleALodInventory.Names.Select(n =>
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/Resources/StyleA/" + n + ".prefab")).ToArray();

        public static void Build()
        {
            // Fixed diagnostic camera/lighting, not gameplay tunables. Scene is disposable and untracked.
            const string scenePath = "Assets/Spacewars/Content/StyleA/Preview/StyleALodAudit.generated.unity";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Audit camera").AddComponent<Camera>();
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .065f, .085f);
            camera.orthographic = true; camera.orthographicSize = 13;
            camera.transform.position = new Vector3(0, 25, -30);
            camera.transform.LookAt(Vector3.zero);
            var audit = camera.gameObject.AddComponent<StyleALodAudit>();
            audit.prefabs = LoadPrefabs(); audit.auditCamera = camera;
            if (audit.prefabs.Any(p => !p)) throw new BuildFailedException("Missing approved prefab");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Diagnostic floor"; floor.transform.position = new Vector3(0, -.16f, 0);
            floor.transform.localScale = new Vector3(39, .2f, 23);
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Spacewars/Content/StyleA/Preview/PreviewFloor.mat");
            var light = new GameObject("Audit key light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.6f;
            light.transform.rotation = Quaternion.Euler(48, -38, 0);
            var oldName = PlayerSettings.productName;
            var oldTiming = PlayerSettings.enableFrameTimingStats;
            try
            {
                PlayerSettings.productName = "Spacewars Style A LOD Audit";
                PlayerSettings.enableFrameTimingStats = true;
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scenePath);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath },
                    locationPathName = "Builds/macOS/StyleALodAudit.app", target = BuildTarget.StandaloneOSX,
                    options = BuildOptions.Development | BuildOptions.DetailedBuildReport });
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
                Debug.Log("STYLE_A_LOD_BUILD_PASS bytes=" + report.summary.totalSize);
            }
            finally
            {
                PlayerSettings.productName = oldName; PlayerSettings.enableFrameTimingStats = oldTiming;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath); AssetDatabase.SaveAssets();
            }
        }
    }
}

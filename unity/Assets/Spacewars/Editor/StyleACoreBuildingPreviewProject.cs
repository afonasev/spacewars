using System.IO;
using Spacewars.StyleA.Preview;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Editor
{
    public static class StyleACoreBuildingPreviewProject
    {
        private const string Scene = "Assets/Spacewars/Content/StyleA/Preview/StyleACoreBuildingPreview.unity";

        public static void Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var names = new[] { "headquarters", "factory", "refinery" };
            for (var i = 0; i < names.Length; i++)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/Resources/StyleA/" + names[i] + ".prefab");
                if (!source) throw new BuildFailedException("Missing approved building prefab: " + names[i]);
                var building = (GameObject)PrefabUtility.InstantiatePrefab(source);
                building.transform.position = new Vector3((i - 1) * 6f, 0, 0);
                building.transform.rotation = Quaternion.Euler(0, 18, 0);
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Diagnostic floor";
            floor.transform.position = new Vector3(0, -.16f, 0);
            floor.transform.localScale = new Vector3(19, .2f, 9);
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Spacewars/Content/StyleA/Preview/PreviewFloor.mat");
            var cameraObject = new GameObject("Preview camera");
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .065f, .085f);
            camera.orthographic = true;
            camera.orthographicSize = 7.4f;
            camera.transform.position = new Vector3(0, 9.5f, -17f);
            camera.transform.LookAt(new Vector3(0, 1.0f, 0));
            cameraObject.AddComponent<StyleACoreBuildingPreviewCapture>();
            var keyObject = new GameObject("Key light");
            var light = keyObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            keyObject.transform.rotation = Quaternion.Euler(48, -38, 0);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), Scene);

            var previousName = PlayerSettings.productName;
            var previousIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            var previousBackground = PlayerSettings.runInBackground;
            try
            {
                PlayerSettings.productName = "Spacewars Style A Core Building Preview";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "local.spacewars.styleacorebuildingpreview");
                PlayerSettings.runInBackground = true;
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Builds/macOS");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { Scene },
                    locationPathName = "Builds/macOS/StyleACoreBuildingPreview.app",
                    target = BuildTarget.StandaloneOSX,
                    options = BuildOptions.Development | BuildOptions.DetailedBuildReport
                });
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
                Debug.Log("STYLE_A_CORE_BUILDING_PREVIEW_BUILD_PASS bytes=" + report.summary.totalSize);
            }
            finally
            {
                PlayerSettings.productName = previousName;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, previousIdentifier);
                PlayerSettings.runInBackground = previousBackground;
                AssetDatabase.SaveAssets();
            }
        }
    }
}

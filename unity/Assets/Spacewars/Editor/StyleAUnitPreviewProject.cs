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
    public static class StyleAUnitPreviewProject
    {
        private const string Scene = "Assets/Spacewars/Content/StyleA/Preview/StyleAUnitPreview.unity";

        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var units = new[] { "tank", "explorer", "shkval" };
            for (var i = 0; i < units.Length; i++)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/Resources/StyleA/" + units[i] + ".prefab");
                if (!source) throw new BuildFailedException("Missing approved unit prefab: " + units[i]);
                var unit = (GameObject)PrefabUtility.InstantiatePrefab(source);
                unit.transform.position = new Vector3((i - 1) * 3.6f, 0, 0);
                unit.transform.rotation = Quaternion.Euler(0, 15, 0);
            }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Diagnostic floor";
            floor.transform.position = new Vector3(0, -.16f, 0);
            floor.transform.localScale = new Vector3(12, .2f, 6);
            const string floorPath = "Assets/Spacewars/Content/StyleA/Preview/PreviewFloor.mat";
            var floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(floorPath);
            if (!floorMaterial)
            {
                floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(floorMaterial, floorPath);
            }
            floorMaterial.SetColor("_BaseColor", new Color(.09f, .15f, .18f));
            EditorUtility.SetDirty(floorMaterial);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var cameraObject = new GameObject("Preview camera");
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .065f, .085f);
            camera.orthographic = true;
            camera.orthographicSize = 4.8f;
            camera.transform.position = new Vector3(0, 5.2f, -10f);
            camera.transform.LookAt(new Vector3(0, .65f, 0));
            cameraObject.AddComponent<StyleAUnitPreviewCapture>();
            var keyObject = new GameObject("Key light");
            var light = keyObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            keyObject.transform.rotation = Quaternion.Euler(48, -38, 0);
            EditorSceneManager.SaveScene(scene, Scene);
            var previousName = PlayerSettings.productName;
            var previousIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            var previousBackground = PlayerSettings.runInBackground;
            try
            {
                PlayerSettings.productName = "Spacewars Style A Unit Preview";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "local.spacewars.styleaunitpreview");
                PlayerSettings.runInBackground = true;
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Builds/macOS");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { Scene },
                    locationPathName = "Builds/macOS/StyleAUnitPreview.app",
                    target = BuildTarget.StandaloneOSX,
                    options = BuildOptions.Development | BuildOptions.DetailedBuildReport
                });
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
                Debug.Log("STYLE_A_UNIT_PREVIEW_BUILD_PASS bytes=" + report.summary.totalSize);
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

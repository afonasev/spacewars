using System.IO;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Editor
{
    public static class FoundationProject
    {
        public const string ScenePath = "Assets/Spacewars/Content/Scenes/Foundation.unity";
        [MenuItem("Spacewars/Create Foundation")]
        public static void Create()
        {
            const string content = "Assets/Spacewars/Content/";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(content + "FoundationRenderer.asset");
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, content + "FoundationRenderer.asset");
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(content + "FoundationPipeline.asset");
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, content + "FoundationPipeline.asset");
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            QualitySettings.vSyncCount = 0; // Diagnostic protocol measures unconstrained rendering.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Spacewars Foundation").AddComponent<FoundationBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "Spacewars";
            PlayerSettings.productName = "Spacewars Unity Foundation";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "local.spacewars.foundation");
            PlayerSettings.defaultScreenWidth = 1280; // U1 observation resolution, not product balance.
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // Enable Input System alone before the next Editor invocation.
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            settings.FindProperty("activeInputHandler").intValue = 1;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("FOUNDATION_PROJECT_CREATED");
        }
        [MenuItem("Spacewars/Build Foundation macOS")]
        public static void Build()
        {
            if (!File.Exists(ScenePath)) Create();
            Directory.CreateDirectory("Builds/macOS");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = "Builds/macOS/SpacewarsFoundation.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development | BuildOptions.DetailedBuildReport
            });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
            Debug.Log("FOUNDATION_BUILD_PASS bytes=" + report.summary.totalSize);
        }
    }
}

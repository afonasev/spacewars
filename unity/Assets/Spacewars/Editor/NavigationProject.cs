using System.IO;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Spacewars.Editor
{
    public static class NavigationProject
    {
        public static void Build()
        {
            const string path="Assets/Spacewars/Content/Scenes/Navigation.unity";
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Navigation Experiment").AddComponent<NavigationBenchmark>();
            EditorSceneManager.SaveScene(scene,path);AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Builds/macOS");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{path},locationPathName="Builds/macOS/SpacewarsNavigation.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());
        }
    }
}

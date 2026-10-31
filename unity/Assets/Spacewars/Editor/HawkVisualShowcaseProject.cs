using System;
using System.IO;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Spacewars.Editor
{
    public static class HawkVisualShowcaseProject
    {
        const string Scene="Assets/Spacewars/Content/StyleA/Preview/HawkVisualShowcase.unity";
        public static void Prepare()
        {
            HawkModelAssets.Prepare();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var showcase=new GameObject("Hawk visual comparison").AddComponent<HawkVisualShowcase>();
            showcase.PreviousPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/StyleA/Preview/hawk-previous.prefab");
            if(!showcase.PreviousPrefab)throw new InvalidOperationException("Missing previous model");
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),Scene);AssetDatabase.SaveAssets();
            Debug.Log("HAWK_SHOWCASE_PREPARED");
        }
        public static void Build()
        {
            Prepare();string output=Path.GetFullPath("../.local/refine-hawk-visuals/player/HawkVisuals.app");Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Scene},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development|BuildOptions.DetailedBuildReport});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());Debug.Log("HAWK_VISUAL_BUILD_PASS");
        }
    }
}

using System.IO;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spacewars.Editor
{
    public static class PlayableProject
    {
        public static void ReleaseMac() { Release(BuildTarget.StandaloneOSX,"Builds/release/macOS/Player.app"); }
        public static void ReleaseWindows() { Release(BuildTarget.StandaloneWindows64,"Builds/release/windows/Player.exe"); }
        private static void Release(BuildTarget target,string output)
        {
            var version=System.Environment.GetEnvironmentVariable("SPACEWARS_RELEASE_VERSION");
            if(string.IsNullOrEmpty(version))throw new BuildFailedException("SPACEWARS_RELEASE_VERSION required");
            PlayableAssets.Prepare();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Spacewars").AddComponent<PlayableBootstrap>();
            EditorSceneManager.SaveScene(scene,ScenePath);
            PlayerSettings.productName="Spacewars";PlayerSettings.bundleVersion=version;
            var icon=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Spacewars/Content/Brand/Icon.png");
            if(icon==null)throw new BuildFailedException("Spacewars icon missing");
            var sizes=PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone,IconKind.Any);
            var icons=new Texture2D[sizes.Length];
            for(var i=0;i<icons.Length;i++)icons[i]=icon;
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone,icons,IconKind.Any);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"tech.afonasev.spacewars");
            // The authenticated update API is bound to loopback; remote payloads remain HTTPS.
            PlayerSettings.insecureHttpOption=InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.runInBackground=true;
            if(target==BuildTarget.StandaloneOSX)PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone,2);
            AssetDatabase.SaveAssets();Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=target,options=BuildOptions.DetailedBuildReport});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());
            Debug.Log("NATIVE_RELEASE_BUILD_PASS target="+target+" bytes="+report.summary.totalSize);
        }
        public const string ScenePath="Assets/Spacewars/Content/Scenes/Playable.unity";
        [MenuItem("Spacewars/Build Playable macOS")]
        public static void Build()
        {
            PlayableAssets.Prepare();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Spacewars").AddComponent<PlayableBootstrap>();
            EditorSceneManager.SaveScene(scene,ScenePath);
            PlayerSettings.productName="Spacewars";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"local.spacewars.playable");
            PlayerSettings.runInBackground=true;
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Builds/macOS");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName="Builds/macOS/SpacewarsPlayable.app",target=BuildTarget.StandaloneOSX,options=BuildOptions.Development|BuildOptions.DetailedBuildReport});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());
            Debug.Log("PLAYABLE_BUILD_PASS bytes="+report.summary.totalSize);
        }
    }
}

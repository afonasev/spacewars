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
            var date=System.Environment.GetEnvironmentVariable("SPACEWARS_RELEASE_DATE")??System.DateTime.UtcNow.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
            if(!System.DateTime.TryParseExact(date,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _))throw new BuildFailedException("SPACEWARS_RELEASE_DATE must be yyyy-MM-dd");
            if(System.Environment.GetEnvironmentVariable("SPACEWARS_RELEASE_COMMITTED_ASSETS")=="1")
            {
                if(!File.Exists(ScenePath))throw new BuildFailedException("Committed playable scene missing");
                Debug.Log("RELEASE_ASSETS committed scene and resources; regeneration disabled");
            }
            else
            {
                PlayableAssets.Prepare();
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                new GameObject("Spacewars").AddComponent<PlayableBootstrap>();
                EditorSceneManager.SaveScene(scene,ScenePath);
            }
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
            // Release date is embedded in the Player, so it remains available offline.
            const string metadata="Assets/Spacewars/Resources/NativeReleaseInfo.json";
            var previous=File.Exists(metadata)?File.ReadAllBytes(metadata):null;
            var previousMeta=File.Exists(metadata+".meta")?File.ReadAllBytes(metadata+".meta"):null;
            try
            {
                File.WriteAllText(metadata,"{\"date\":\""+date+"\"}");AssetDatabase.ImportAsset(metadata,ImportAssetOptions.ForceSynchronousImport);
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=target,options=BuildOptions.DetailedBuildReport});
                if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());
                Debug.Log("NATIVE_RELEASE_BUILD_PASS target="+target+" bytes="+report.summary.totalSize);
            }
            finally
            {
                if(previous==null)File.Delete(metadata);else File.WriteAllBytes(metadata,previous);
                if(previousMeta==null)File.Delete(metadata+".meta");else File.WriteAllBytes(metadata+".meta",previousMeta);
                AssetDatabase.Refresh();
            }
        }
        public const string ScenePath="Assets/Spacewars/Content/Scenes/Playable.unity";
        [MenuItem("Spacewars/Build Playable macOS")]
        public static void Build()=>BuildDevelopment(BuildTarget.StandaloneOSX,"Builds/macOS/SpacewarsPlayable.app");
        // Procedural scenery review does not author unrelated imported unit assets.
        // Keep the normal regeneration build unchanged; validate the already committed
        // runtime prefabs before a development-only visual-review build.
        public static void BuildPreparedVisualReview()
        {
            var prefabs=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Spacewars/Content/Resources/StyleA"});
            if(prefabs.Length==0)throw new BuildFailedException("No prepared runtime prefabs");
            foreach(var guid in prefabs)
            {
                var path=AssetDatabase.GUIDToAssetPath(guid);var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var filters=prefab.GetComponentsInChildren<MeshFilter>(true);var renderers=prefab.GetComponentsInChildren<MeshRenderer>(true);
                if(filters.Length==0||renderers.Length==0)throw new BuildFailedException("Empty prepared prefab: "+path);
                foreach(var filter in filters)if(!filter.sharedMesh||filter.sharedMesh.vertexCount==0)throw new BuildFailedException("Missing prepared mesh: "+path+"/"+filter.name);
                foreach(var renderer in renderers)
                {
                    if(renderer.sharedMaterials.Length==0)throw new BuildFailedException("Missing prepared materials: "+path);
                    foreach(var material in renderer.sharedMaterials)if(!material||!material.shader)throw new BuildFailedException("Missing prepared material/shader: "+path);
                }
            }
            Debug.Log("PREPARED_VISUAL_REVIEW_VALIDATED prefabs="+prefabs.Length);
            BuildDevelopment(BuildTarget.StandaloneOSX,"Builds/macOS/SpacewarsPlayable.app",false);
        }
        public static void BuildWindowsDevelopment()=>BuildDevelopment(BuildTarget.StandaloneWindows64,"Builds/windows-qa/SpacewarsPlayable.exe");
        private static void BuildDevelopment(BuildTarget target,string output,bool prepareAssets=true)
        {
            // Performance QA preserves committed authored models and scene.
            if(System.Environment.GetEnvironmentVariable("SPACEWARS_QA_COMMITTED_ASSETS")=="1")
            {
                if(!File.Exists(ScenePath))throw new BuildFailedException("Committed playable scene missing");
                Debug.Log("QA_ASSETS committed scene and resources; regeneration disabled");
            }
            else
            {
                if(prepareAssets)PlayableAssets.Prepare();
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                new GameObject("Spacewars").AddComponent<PlayableBootstrap>();
                EditorSceneManager.SaveScene(scene,ScenePath);
            }
            PlayerSettings.productName="Spacewars";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"local.spacewars.playable");
            PlayerSettings.runInBackground=true;
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var timing=PlayerSettings.enableFrameTimingStats;
            var previousCompilerArguments=PlayerSettings.GetAdditionalCompilerArguments(NamedBuildTarget.Standalone);
            bool optimizedScripts=System.Environment.GetEnvironmentVariable("SPACEWARS_QA_OPTIMIZED_SCRIPTS")=="1";
            var settingsPath="ProjectSettings/ProjectSettings.asset";
            var originalSettings=optimizedScripts?File.ReadAllBytes(settingsPath):null;
            UnityEditor.Build.Reporting.BuildReport report;
            try{
                // Development Players otherwise compile managed code with /optimize-.
                // Keep the diagnostic hooks while measuring optimized C#; this is
                // opt-in QA calibration and does not change normal/release settings.
                if(optimizedScripts){
                    var previous=previousCompilerArguments??System.Array.Empty<string>();
                    var arguments=new string[previous.Length+1];System.Array.Copy(previous,arguments,previous.Length);arguments[previous.Length]="-optimize+";
                    PlayerSettings.SetAdditionalCompilerArguments(NamedBuildTarget.Standalone,arguments);
                    Debug.Log("QA_SCRIPTS optimize+ requested; existing compiler arguments retained");
                }
                if(System.Environment.GetEnvironmentVariable("SPACEWARS_PRECISE_PROFILE")=="1")PlayerSettings.enableFrameTimingStats=true;
                report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=target,options=BuildOptions.Development|BuildOptions.DetailedBuildReport});
            }
            finally{
                PlayerSettings.enableFrameTimingStats=timing;
                if(optimizedScripts){
                    PlayerSettings.SetAdditionalCompilerArguments(NamedBuildTarget.Standalone,previousCompilerArguments??System.Array.Empty<string>());
                    AssetDatabase.SaveAssets();
                    // Unity materializes an empty Standalone key even when the
                    // original argument map was empty. Restore only this exact
                    // no-op serialization; every other settings drift stays visible.
                    string before=System.Text.Encoding.UTF8.GetString(originalSettings);
                    string after=System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(settingsPath));
                    string newline=before.Contains("\r\n")?"\r\n":"\n";
                    const string empty="  additionalCompilerArguments: {}";
                    if(before.Contains(empty)&&after==before.Replace(empty,"  additionalCompilerArguments:"+newline+"    Standalone: []")){
                        File.WriteAllBytes(settingsPath,originalSettings);Debug.Log("QA_COMPILER_ARGUMENTS_EXACT_RESTORE empty Standalone key only");
                    }
                }
            }
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());
            Debug.Log("PLAYABLE_BUILD_PASS bytes="+report.summary.totalSize);
        }
    }
}

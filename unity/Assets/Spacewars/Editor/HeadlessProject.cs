using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Spacewars.Headless;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Spacewars.Editor
{
    public static class HeadlessProject
    {
        // QA worker only: no playable scene, HUD, renderer, installer or release operation.
        public static void Build()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_AI_BUILD_OUTPUT");
            string revision=Environment.GetEnvironmentVariable("SPACEWARS_AI_CODE_REVISION");
            string launcher=Environment.GetEnvironmentVariable("SPACEWARS_AI_LAUNCHER");
            if(string.IsNullOrEmpty(output)||string.IsNullOrEmpty(revision)||string.IsNullOrEmpty(launcher))throw new BuildFailedException("Headless build identity/output required; use tools/ai-sim/build-worker.py.");
            string generated="Assets/Spacewars/Content/HeadlessGenerated";
            Directory.CreateDirectory(generated+"/Resources");
            try
            {
                File.WriteAllText(generated+"/Resources/HeadlessBuildIdentity.txt",revision);
                AssetDatabase.Refresh();
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                new GameObject("HeadlessAuthority").AddComponent<HeadlessBootstrap>();
                string scenePath=generated+"/Worker.unity";EditorSceneManager.SaveScene(scene,scenePath);
                Directory.CreateDirectory(output);
                string root=Path.Combine(output,"SpacewarsAiWorker.app");
                var oldArchitecture=PlayerSettings.GetArchitecture(NamedBuildTarget.Standalone);
                BuildReport report;
                try
                {
                    PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone,1); // native Apple Silicon; identity is platform-fixed v1.
                    report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scenePath},locationPathName=root,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development|BuildOptions.DetailedBuildReport});
                }
                finally{PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone,oldArchitecture);}
                if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());
                string executable=Path.Combine(root,"Contents/MacOS/"+PlayerSettings.productName);
                if(!File.Exists(executable))throw new BuildFailedException("Missing worker executable "+executable);
                var identity=HeadlessFixtures.EngineIdentity(revision);
                identity.ExecutableHash=Wire.FileHash(executable);identity.BundleHash=Wire.BundleHash(root);
                File.WriteAllText(Path.Combine(output,"worker-identity.json"),JsonConvert.SerializeObject(identity,Formatting.Indented));
                foreach(var fixture in new[]{"duel-coverage-v1","obstacle-v1"}.Concat(HeadlessFixtures.EconomyFixtures))
                {
                    var m=HeadlessFixtures.Template(fixture,identity,root,executable,launcher);Wire.Validate(m,false);
                    File.WriteAllText(Path.Combine(output,fixture+".json"),JsonConvert.SerializeObject(m,Formatting.Indented));
                }
                Debug.Log("HEADLESS_WORKER_BUILD_PASS code="+revision+" bundle="+identity.BundleHash+" bytes="+report.summary.totalSize);
            }
            finally{AssetDatabase.DeleteAsset(generated);}
        }
    }
}

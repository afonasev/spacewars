using System;
using UnityEditor;
using UnityEngine;
namespace Spacewars.Editor
{
    public static class SentryTurretCombatProject
    {
        public static void BuildReview()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_SENTRY_BUILD_OUTPUT");
            if(string.IsNullOrEmpty(output))throw new UnityEditor.Build.BuildFailedException("Owned review output required");
            var report=UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions{scenes=new[]{PlayableProject.ScenePath},locationPathName=output,target=UnityEditor.BuildTarget.StandaloneOSX,options=UnityEditor.BuildOptions.Development});
            if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new UnityEditor.Build.BuildFailedException(report.summary.result.ToString());
            Debug.Log("SENTRY_REVIEW_BUILD_PASS bytes="+report.summary.totalSize);
        }
        public static void Prepare()
        {
            const string path="Assets/Spacewars/Resources/SentryTargetLaser.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material)
            {
                material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name="Sentry target laser"};
                material.SetColor("_BaseColor",new Color(1,.003f,.003f));material.enableInstancing=true;
                AssetDatabase.CreateAsset(material,path);
            }
            DockPortraitBaker.BakeTurret();AssetDatabase.SaveAssets();Debug.Log("SENTRY_COMBAT_ASSETS_PREPARED");
        }
    }
}

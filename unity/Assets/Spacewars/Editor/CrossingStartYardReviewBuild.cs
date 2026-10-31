using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
namespace Spacewars.Editor
{
    public static class CrossingStartYardReviewBuild
    {
        public static void Build()
        {
            var output=Environment.GetEnvironmentVariable("CROSSINGS_YARD_PLAYER");
            if(string.IsNullOrEmpty(output)||!Path.IsPathRooted(output))throw new BuildFailedException("Absolute owned CROSSINGS_YARD_PLAYER required");
            // Use the exact committed resources and normal entry scene. Do not
            // regenerate unrelated prefabs or change project/release settings.
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{PlayableProject.ScenePath},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development|BuildOptions.DetailedBuildReport});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());
            UnityEngine.Debug.Log("CROSSING_YARD_REVIEW_BUILD_PASS bytes="+report.summary.totalSize);
        }
    }
}

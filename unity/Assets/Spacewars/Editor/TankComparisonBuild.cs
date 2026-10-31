using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
namespace Spacewars.Editor
{
 public static class TankComparisonBuild
 {
  public static void Preflight()
  {
   var config=Spacewars.Presentation.TankComparisonEvidence.CreateConfiguration(out var points,out var lateral);
   UnityEngine.Debug.Log("TANK_SETUP_PASS seed="+config.Seed+" first="+points[0].X+","+points[0].Z+" lateral="+lateral.X+","+lateral.Z);
  }
  public static void Build()
  {
   string output=Environment.GetEnvironmentVariable("SPACEWARS_TANK_BUILD_OUTPUT");if(string.IsNullOrEmpty(output))throw new BuildFailedException("Owned tank build output required");
   Directory.CreateDirectory(Path.GetDirectoryName(output));
   var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{PlayableProject.ScenePath},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development|BuildOptions.DetailedBuildReport});
   if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());
   UnityEngine.Debug.Log("TANK_COMPARISON_BUILD_PASS bytes="+report.summary.totalSize);
  }
 }
}

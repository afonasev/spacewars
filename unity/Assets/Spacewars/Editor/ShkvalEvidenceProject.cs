using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Spacewars.Presentation;
namespace Spacewars.Editor
{
 public static class ShkvalEvidenceProject
 {
  public static void Build()
  {
   ShkvalMonolithAssets.Prepare();
   if(!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SPACEWARS_SHKVAL_METRICS")))ShkvalAssetMetrics.Measure();
   var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Shkval native art stage").AddComponent<ShkvalNativeEvidence>();
   const string path="Assets/Spacewars/Content/StyleA/Preview/ShkvalNativeEvidence.unity";EditorSceneManager.SaveScene(scene,path);AssetDatabase.SaveAssets();
   string output=Path.GetFullPath("../.local/shkval-r9/Spacewars.app");Directory.CreateDirectory(Path.GetDirectoryName(output));
   var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{path},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
   if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());Debug.Log("SHKVAL_QA_BUILD_PASS");
  }
 }
}

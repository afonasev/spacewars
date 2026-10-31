using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spacewars.Editor
{
    public static class ModelDetailGalleryBuild
    {
        public static void BuildCameraCalibration()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_LOD_CALIBRATION_APP")??throw new InvalidOperationException("Set owned calibration app path");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{PlayableProject.ScenePath},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
            if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());
            Debug.Log("MODEL_DETAIL_CAMERA_BUILD_PASS bytes="+report.summary.totalSize);
        }
        public static void PrepareAndBuild(){ModelDetailAssets.Prepare();Build();}
        public static void Build()
        {
            string output=Environment.GetEnvironmentVariable("SPACEWARS_LOD_GALLERY_APP")??throw new InvalidOperationException("Set owned gallery app path");
            ModelDetailAssets.CleanupIntermediates();
            if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SPACEWARS_LOD_METRICS")))ModelDetailAssets.RecordMetrics();
            const string scene="Assets/Spacewars/Content/ModelDetailGallery.generated.unity";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var gallery=new GameObject("Model detail gallery").AddComponent<ModelDetailGallery>();gallery.keys=(Environment.GetEnvironmentVariable("SPACEWARS_LOD_GALLERY_KEYS")??string.Join(",",ModelDetailAssets.Keys.Concat(new[]{"hangar"}))).Split(',');
            gallery.originals=gallery.keys.Select(k=>Resources.Load<GameObject>("StyleA/"+k)).ToArray();gallery.candidates=gallery.keys.Select(k=>Resources.Load<GameObject>("StyleA/Lod/"+k)).ToArray();
            if(gallery.originals.Any(p=>!p)||gallery.candidates.Any(p=>!p))throw new BuildFailedException("Missing three-level asset");
            try{
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),scene);
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.Development});
                if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException(report.summary.result.ToString());Debug.Log("MODEL_DETAIL_GALLERY_BUILD_PASS bytes="+report.summary.totalSize);
            }finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);AssetDatabase.DeleteAsset(scene);}
        }
    }
}

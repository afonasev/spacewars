using System;
using System.IO;
using System.Text;
using Spacewars.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Spacewars.Editor {
 public static class HawkMotionPreviewProject {
  [MenuItem("Spacewars/Preview/Hawk Motion/Create Scene and Verify")]
  public static void Run(){
   if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
   string output=Environment.GetEnvironmentVariable("HAWK_MOTION_OUTPUT")??Path.GetFullPath("../docs/hawk-motion-preview/evidence");
   bool video=Environment.GetEnvironmentVariable("HAWK_MOTION_OUTPUT")!=null;
   Directory.CreateDirectory(output);if(video)Directory.CreateDirectory(Path.Combine(output,"frames"));
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var preview=new GameObject("Hawk motion laboratory").AddComponent<HawkMotionPreview>();
   EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Spacewars/Content/StyleA/Preview/HawkMotionPreview.unity");AssetDatabase.SaveAssets();
   preview.Initialize();
   var csv=new StringBuilder("frame,time,phase,x,z,speed,yaw,pitch,roll,yawVelocity\n");
   var rt=new RenderTexture(1280,800,24,RenderTextureFormat.ARGB32);var texture=new Texture2D(1280,800,TextureFormat.RGB24,false);
   double maxPitch=0,minPitch=0,maxRoll=0,minRoll=0,turnStartX=0,turnStartZ=0;
   try {
    for(int frame=0;frame<660;frame++){
     preview.Step(1d/30);var m=preview.Motion;double t=preview.Clock;
     maxPitch=Math.Max(maxPitch,m.Pitch);minPitch=Math.Min(minPitch,m.Pitch);maxRoll=Math.Max(maxRoll,m.Roll);minRoll=Math.Min(minRoll,m.Roll);
     if(frame==209&&m.Speed>1e-8)throw new InvalidOperationException("Reverse did not auto-brake");
     if(frame==400){if(Math.Abs(m.Yaw)>1e-8)throw new InvalidOperationException("Strafe changed heading");turnStartX=m.X;turnStartZ=m.Z;}
     if(frame==490&&Math.Abs(m.X-turnStartX)+Math.Abs(m.Z-turnStartZ)>1e-8)throw new InvalidOperationException("Hover turn translated");
     csv.AppendLine(FormattableString.Invariant($"{frame},{t},{preview.Phase},{m.X},{m.Z},{m.Speed},{m.Yaw},{m.Pitch},{m.Roll},{m.YawVelocity}"));
     if(video||frame==12||frame==38||frame==108||frame==238||frame==540){
      var camera=preview.PreviewCamera;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
      texture.ReadPixels(new Rect(0,0,1280,800),0,0);texture.Apply();
      string name=video?Path.Combine("frames",frame.ToString("D4")+".png"):frame.ToString("D4")+".png";
      File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());
     }
    }
    if(maxPitch<8||minPitch>-3||maxRoll<4||minRoll>-4||preview.Motion.Speed>1e-8)throw new InvalidOperationException("Missing attitude or final hover");
    File.WriteAllText(Path.Combine(output,"frames.csv"),csv.ToString());
    File.WriteAllText(Path.Combine(output,"result.txt"),FormattableString.Invariant($"PASS\n660 frames at 30 fps; 22 seconds\nPitch range {minPitch}..{maxPitch}; roll range {minRoll}..{maxRoll}\nReverse auto-stop, strafe heading, stationary yaw and final hover verified.\nIsolated Editor demonstration; not ordinary-match or human acceptance.\n"));
   } finally {RenderTexture.active=null;preview.PreviewCamera.targetTexture=null;UnityEngine.Object.DestroyImmediate(texture);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Debug.Log("HAWK_MOTION_PREVIEW_PASS");
  }
 }
}

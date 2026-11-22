using System;
using System.IO;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;
namespace Spacewars.Editor
{
 public sealed class AntennaCityArtWindow:EditorWindow
 {
  AntennaCityArtProfile art;IndustrialLightingProfile lighting;Vector2 scroll;string error;
  [MenuItem("Spacewars/Balance Lab/Nova City Night Line art")]
  public static void Open()=>GetWindow<AntennaCityArtWindow>("Nova City art");
  void OnEnable(){art=AntennaCityArtProfile.Load();lighting=IndustrialLightingProfile.Load("antenna-city-greybox-v1");}
  void OnGUI(){if(art==null)return;scroll=EditorGUILayout.BeginScrollView(scroll);
   EditorGUILayout.LabelField("Night Line · presentation only",EditorStyles.boldLabel);
   void Controls(System.Collections.Generic.IEnumerable<SurfaceParameter> list){foreach(var p in list){float v=EditorGUILayout.Slider(new GUIContent(p.Label+" ("+p.Unit+")",p.Path+"\n"+p.Description),p.Read(),p.Minimum,p.Maximum);p.Write(Mathf.Clamp(Mathf.Round(v/p.Step)*p.Step,p.Minimum,p.Maximum));}}
   Controls(art.Parameters());EditorGUILayout.Space();EditorGUILayout.LabelField("Blue-hour lighting",EditorStyles.boldLabel);Controls(lighting.Parameters());EditorGUILayout.EndScrollView();
   if(error!=null)EditorGUILayout.HelpBox(error,MessageType.Error);
   if(GUILayout.Button("Validate and save next-match art"))try{art.Validate();lighting.Validate();art.revision++;lighting.revision++;File.WriteAllText("Assets/Spacewars/Content/Resources/AntennaCityArtProfile.json",JsonUtility.ToJson(art,true)+"\n");File.WriteAllText("Assets/Spacewars/Content/Resources/AntennaCityLightingProfile.json",JsonUtility.ToJson(lighting,true)+"\n");AssetDatabase.Refresh();error=null;}catch(Exception e){error=e.Message;}
  }
 }
}

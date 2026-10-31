using System;
using System.IO;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;
namespace Spacewars.Editor
{
    public sealed class NullDockArtWindow:EditorWindow
    {
        private NullDockProfileData map;private NullDockArtProfile art;private Vector2 scroll;private string error;
        [MenuItem("Spacewars/Balance Lab/Null Dock")]
        public static void Open()=>GetWindow<NullDockArtWindow>("Null Dock");
        private void OnEnable(){map=JsonUtility.FromJson<NullDockProfileData>(Resources.Load<TextAsset>("NullDockProfile").text);art=NullDockArtProfile.Load();}
        private void OnGUI(){if(map==null||art==null)return;scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var f in NullDockMap.Fields){double value=EditorGUILayout.Slider(new GUIContent(f.Label+" ("+f.Unit+")",f.Path+"\n"+f.Description),(float)f.Read(map),(float)f.Minimum,(float)f.Maximum);f.Write(map,Math.Round(value/f.Step)*f.Step);}
            foreach(var f in art.Parameters()){float value=EditorGUILayout.Slider(new GUIContent(f.Label+" ("+f.Unit+")",f.Path+"\n"+f.Description),f.Read(),f.Minimum,f.Maximum);f.Write(Mathf.Clamp(Mathf.Round(value/f.Step)*f.Step,f.Minimum,f.Maximum));}
            EditorGUILayout.EndScrollView();if(error!=null)EditorGUILayout.HelpBox(error,MessageType.Error);
            if(GUILayout.Button("Validate and save next-match profiles"))try{art.Validate();PlayableProfile.Create(PlayableProfile.Default.CopyData(),new NullDockMap(map));map.revision++;art.revision++;File.WriteAllText("Assets/Spacewars/Resources/NullDockProfile.json",JsonUtility.ToJson(map,true)+"\n");File.WriteAllText("Assets/Spacewars/Content/Resources/NullDockArtProfile.json",JsonUtility.ToJson(art,true)+"\n");AssetDatabase.Refresh();error=null;}catch(Exception e){error=e.Message;}
        }
    }
}

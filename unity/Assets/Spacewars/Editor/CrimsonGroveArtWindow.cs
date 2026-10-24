using System;
using System.IO;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;
namespace Spacewars.Editor
{
    public sealed class CrimsonGroveArtWindow:EditorWindow
    {
        private const string Root="Assets/Spacewars/Content/Resources/";
        private CrimsonGroveProfileData map;private CrimsonGroveArtProfile art;private Vector2 scroll;private string error;
        [MenuItem("Spacewars/Balance Lab/Crimson Grove")]
        public static void Open()=>GetWindow<CrimsonGroveArtWindow>("Crimson Grove");
        private void OnEnable(){map=JsonUtility.FromJson<CrimsonGroveProfileData>(File.ReadAllText(Root+"CrimsonGroveProfile.json"));art=CrimsonGroveArtProfile.Load();}
        private void OnGUI(){if(map==null||art==null)return;scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var f in CrimsonGroveMap.Fields){double value=EditorGUILayout.Slider(new GUIContent(f.Label+" ("+f.Unit+")",f.Path+"\n"+f.Description),(float)f.Read(map),(float)f.Minimum,(float)f.Maximum);f.Write(map,Math.Round(value/f.Step)*f.Step);}
            foreach(var f in art.Parameters()){float value=EditorGUILayout.Slider(new GUIContent(f.Label+" ("+f.Unit+")",f.Path+"\n"+f.Description),f.Read(),f.Minimum,f.Maximum);f.Write(Mathf.Clamp(Mathf.Round(value/f.Step)*f.Step,f.Minimum,f.Maximum));}
            EditorGUILayout.EndScrollView();if(error!=null)EditorGUILayout.HelpBox(error,MessageType.Error);
            if(GUILayout.Button("Validate and save next-match profiles"))try{art.Validate();PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CrimsonGroveMap(map));map.revision++;art.revision++;File.WriteAllText(Root+"CrimsonGroveProfile.json",JsonUtility.ToJson(map,true)+"\n");File.WriteAllText(Root+"CrimsonGroveArtProfile.json",JsonUtility.ToJson(art,true)+"\n");AssetDatabase.Refresh();error=null;}catch(Exception e){error=e.Message;}
        }
    }
}

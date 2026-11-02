using System;
using System.IO;
using Spacewars.Simulation;
using UnityEditor;
using UnityEngine;
namespace Spacewars.Editor
{
    public sealed class AntennaCityWindow:EditorWindow
    {
        const string Path="Assets/Spacewars/Content/Resources/AntennaCityProfile.json";
        AntennaCityProfileData map;string error;
        [MenuItem("Spacewars/Balance Lab/Antenna City")]
        public static void Open()=>GetWindow<AntennaCityWindow>("Antenna City");
        void OnEnable(){map=JsonUtility.FromJson<AntennaCityProfileData>(Resources.Load<TextAsset>("AntennaCityProfile").text);}
        void OnGUI(){if(map==null)return;
            foreach(var f in AntennaCityMap.Fields){double value=EditorGUILayout.Slider(new GUIContent(f.Label+" ("+f.Unit+")",f.Path+"\n"+f.Description),(float)f.Read(map),(float)f.Minimum,(float)f.Maximum);f.Write(map,Math.Max(f.Minimum,Math.Min(f.Maximum,Math.Round(value/f.Step)*f.Step)));}
            if(error!=null)EditorGUILayout.HelpBox(error,MessageType.Error);
            if(GUILayout.Button("Validate and save next-match profile"))try{PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AntennaCityMap(map));map.revision++;File.WriteAllText(Path,JsonUtility.ToJson(map,true)+"\n");AssetDatabase.Refresh();error=null;}catch(Exception e){error=e.Message;}
        }
    }
}

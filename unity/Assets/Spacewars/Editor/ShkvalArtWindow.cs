using System;
using System.IO;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;
namespace Spacewars.Editor
{
    public sealed class ShkvalArtWindow:EditorWindow
    {
        const string Path="Assets/Spacewars/Content/Resources/ShkvalArtProfile.json";
        ShkvalArtProfile profile;string failure;
        [MenuItem("Spacewars/Balance Lab/Shkval Monolith")] public static void Open()=>GetWindow<ShkvalArtWindow>("Shkval Monolith");
        void OnEnable(){profile=JsonUtility.FromJson<ShkvalArtProfile>(File.ReadAllText(Path));}
        void OnGUI()
        {
            if(profile==null)return;EditorGUILayout.LabelField(profile.id+" @"+profile.revision);
            foreach(var p in profile.Parameters())
            {EditorGUI.BeginChangeCheck();float value=EditorGUILayout.Slider(new GUIContent(p.Label+" ("+p.Unit+")",p.Path+"\n"+p.Description),p.Read(),p.Minimum,p.Maximum);if(EditorGUI.EndChangeCheck())p.Write(Mathf.Clamp(Mathf.Round(value/p.Step)*p.Step,p.Minimum,p.Maximum));}
            if(failure!=null)EditorGUILayout.HelpBox(failure,MessageType.Error);
            if(GUILayout.Button("Validate and save art revision"))try{profile.Validate();profile.revision++;File.WriteAllText(Path,JsonUtility.ToJson(profile,true)+"\n");AssetDatabase.Refresh();failure=null;}catch(Exception e){failure=e.Message;}
        }
    }
}

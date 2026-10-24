using System;
using System.IO;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Editor
{
    public sealed class GroundTrailWindow:EditorWindow
    {
        private const string ProfilePath="Assets/Spacewars/Content/Resources/GroundTrailProfile.json";
        private GroundTrailProfile profile;private Vector2 scroll;private string failure;
        [MenuItem("Spacewars/Balance Lab/Ground trails")]
        public static void Open(){var window=GetWindow<GroundTrailWindow>("Ground trails");window.path=ProfilePath;window.OnEnable();}
        private string path=ProfilePath;
        private void OnEnable(){profile=JsonUtility.FromJson<GroundTrailProfile>(File.ReadAllText(path));}
        private void OnGUI()
        {
            if(profile==null)return;
            EditorGUILayout.LabelField(profile.id+" @"+profile.revision);scroll=EditorGUILayout.BeginScrollView(scroll);
            string group=null;
            foreach(var field in profile.Parameters())
            {
                if(group!=field.Group){group=field.Group;EditorGUILayout.LabelField(group,EditorStyles.boldLabel);}
                EditorGUI.BeginChangeCheck();
                float value=EditorGUILayout.Slider(new GUIContent(field.Label+" ("+field.Unit+")",field.Path+"\n"+field.Description),field.Read(),field.Minimum,field.Maximum);
                if(EditorGUI.EndChangeCheck())field.Write(Mathf.Clamp(Mathf.Round(value/field.Step)*field.Step,field.Minimum,field.Maximum));
            }
            EditorGUILayout.EndScrollView();if(failure!=null)EditorGUILayout.HelpBox(failure,MessageType.Error);
            if(GUILayout.Button("Validate and save art revision"))try
            {profile.Validate();profile.revision++;File.WriteAllText(path,JsonUtility.ToJson(profile,true)+"\n");AssetDatabase.Refresh();failure=null;}
            catch(Exception e){failure=e.Message;}
        }
    }
}

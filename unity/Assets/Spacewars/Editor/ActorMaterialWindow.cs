using System;
using System.IO;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Editor
{
    public sealed class ActorMaterialWindow:EditorWindow
    {
        private const string ProfilePath="Assets/Spacewars/Content/Resources/ActorMaterialProfile.json";
        private ActorMaterialProfile profile;private Vector2 scroll;private string failure;
        [MenuItem("Spacewars/Balance Lab/Actor Materials")]
        public static void Open(){var window=GetWindow<ActorMaterialWindow>("Actor materials");window.path=ProfilePath;window.OnEnable();}
        [MenuItem("Spacewars/Balance Lab/Luminous Actor Materials")]
        public static void OpenLuminous(){var window=GetWindow<ActorMaterialWindow>("Luminous materials");window.path="Assets/Spacewars/Content/Resources/ActorMaterialLuminousProfile.json";window.OnEnable();}
        private string path=ProfilePath;
        private void OnEnable(){profile=JsonUtility.FromJson<ActorMaterialProfile>(File.ReadAllText(path));}
        private void OnGUI()
        {
            if(profile==null)return;
            EditorGUILayout.LabelField(profile.id+" / "+profile.variant+" @"+profile.revision);scroll=EditorGUILayout.BeginScrollView(scroll);
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

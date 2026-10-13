using System;
using System.IO;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Editor
{
    public sealed class IndustrialLightingWindow:EditorWindow
    {
        [SerializeField] private int scope;
        private string ProfilePath=>"Assets/Spacewars/Content/Resources/"+IndustrialLightingProfile.ResourceName(scope==1?"three-crossings-greybox-v1":null)+".json";
        private IndustrialLightingProfile profile;private Vector2 scroll;private string failure;
        [MenuItem("Spacewars/Balance Lab/Industrial Lighting")]
        public static void Open()=>GetWindow<IndustrialLightingWindow>("Industrial lighting");
        private void OnEnable(){profile=JsonUtility.FromJson<IndustrialLightingProfile>(File.ReadAllText(ProfilePath));}
        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();scope=EditorGUILayout.Popup("Profile",scope,new[]{"Global lighting","Three Crossings warm"});
            if(EditorGUI.EndChangeCheck()){OnEnable();failure=null;}
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
            {profile.Validate();profile.revision++;File.WriteAllText(ProfilePath,JsonUtility.ToJson(profile,true)+"\n");AssetDatabase.Refresh();failure=null;}
            catch(Exception e){failure=e.Message;}
        }
    }
}

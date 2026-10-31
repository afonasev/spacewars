using System;
using System.IO;
using System.Linq;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Editor
{
    public sealed class FoundryArtWindow:EditorWindow
    {
        private const string ProfilePath="Assets/Spacewars/Content/Resources/FoundrySurfaceProfile.json";
        private const string IndustrialProfilePath="Assets/Spacewars/Content/Resources/FoundryIndustrialProfile.json";
        private FoundryIndustrialProfile industry;
        private FoundrySurfaceProfile profile;private Vector2 scroll;private string failure;
        [MenuItem("Spacewars/Balance Lab/Foundry Art")]
        public static void Open()=>GetWindow<FoundryArtWindow>("Foundry art");
        private void OnEnable(){profile=JsonUtility.FromJson<FoundrySurfaceProfile>(File.ReadAllText(ProfilePath));industry=JsonUtility.FromJson<FoundryIndustrialProfile>(File.ReadAllText(IndustrialProfilePath));}
        private void OnGUI()
        {
            if(profile==null||industry==null)return;
            EditorGUILayout.LabelField(profile.id+" @"+profile.revision);EditorGUILayout.LabelField(industry.id+" @"+industry.revision);scroll=EditorGUILayout.BeginScrollView(scroll);
            string group=null;
            foreach(var field in profile.Parameters().Concat(industry.Parameters()))
            {
                if(group!=field.Group){group=field.Group;EditorGUILayout.LabelField(group,EditorStyles.boldLabel);}
                EditorGUI.BeginChangeCheck();
                float value=EditorGUILayout.Slider(new GUIContent(field.Label+" ("+field.Unit+")",field.Path+"\n"+field.Description),field.Read(),field.Minimum,field.Maximum);
                if(EditorGUI.EndChangeCheck())field.Write(Mathf.Clamp(Mathf.Round(value/field.Step)*field.Step,field.Minimum,field.Maximum));
            }
            EditorGUILayout.EndScrollView();if(failure!=null)EditorGUILayout.HelpBox(failure,MessageType.Error);
            if(GUILayout.Button("Validate and save art revision"))try
            {profile.Validate();industry.Validate();profile.revision++;industry.revision++;File.WriteAllText(ProfilePath,JsonUtility.ToJson(profile,true)+"\n");File.WriteAllText(IndustrialProfilePath,JsonUtility.ToJson(industry,true)+"\n");AssetDatabase.Refresh();failure=null;}
            catch(Exception e){failure=e.Message;}
        }
    }
}

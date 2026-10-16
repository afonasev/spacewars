using System;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Editor
{
    public sealed class MossHighlandArtWindow:EditorWindow
    {
        private const string ProfilePath="Assets/Spacewars/Content/Resources/MossHighlandArtProfile.json";
        private const string GameplayPath="Assets/Spacewars/Content/Resources/MossHighlandProfile.json";
        private MossHighlandProfileData gameplay;
        private MossHighlandArtProfile profile;private Vector2 scroll;private string failure;
        [MenuItem("Spacewars/Balance Lab/Moss Highland Art")]
        public static void Open()=>GetWindow<MossHighlandArtWindow>("Moss Highland art");
        private void OnEnable(){gameplay=JsonUtility.FromJson<MossHighlandProfileData>(File.ReadAllText(GameplayPath));profile=JsonUtility.FromJson<MossHighlandArtProfile>(File.ReadAllText(ProfilePath));}
        private void OnGUI()
        {
            if(profile==null)return;
            var placement=MossHighlandMap.PlacementField;
            EditorGUILayout.LabelField("Moss Highland placement · next match",EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            double offset=EditorGUILayout.Slider(new GUIContent(placement.Label,placement.Description),(float)placement.Read(gameplay),(float)placement.Minimum,(float)placement.Maximum);
            if(EditorGUI.EndChangeCheck())placement.Write(gameplay,System.Math.Round(offset/placement.Step)*placement.Step);
            if(GUILayout.Button("Validate and save map placement"))try{PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap(gameplay));gameplay.revision++;File.WriteAllText(GameplayPath,JsonUtility.ToJson(gameplay,true)+"\n");AssetDatabase.Refresh();failure=null;}catch(Exception e){failure=e.Message;}
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

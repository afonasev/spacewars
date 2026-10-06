using System;
using System.IO;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Editor
{
    public sealed class ThreeCrossingsMaterialsWindow : EditorWindow
    {
        private const string Path="Assets/Spacewars/Content/Resources/ThreeCrossingsSurfaceProfile.json";
        private ThreeCrossingsSurfaceProfile data;
        private Vector2 scroll;
        private string failure;
        [MenuItem("Spacewars/Balance Lab/Three Crossings Materials")]
        public static void Open()=>GetWindow<ThreeCrossingsMaterialsWindow>("Map materials");
        private void OnEnable(){data=JsonUtility.FromJson<ThreeCrossingsSurfaceProfile>(File.ReadAllText(Path));}
        private void OnGUI()
        {
            if(data==null)return;
            EditorGUILayout.LabelField(data.id+" @"+data.revision);scroll=EditorGUILayout.BeginScrollView(scroll);
            string group=null;
            foreach(var field in data.Parameters())
            {
                if(group!=field.Group){group=field.Group;EditorGUILayout.LabelField(group,EditorStyles.boldLabel);}
                EditorGUI.BeginChangeCheck();
                float value=EditorGUILayout.Slider(new GUIContent(field.Label+" ("+field.Unit+")",field.Path+"\n"+field.Description),field.Read(),field.Minimum,field.Maximum);
                if(EditorGUI.EndChangeCheck())field.Write(Mathf.Clamp(Mathf.Round(value/field.Step)*field.Step,field.Minimum,field.Maximum));
            }
            EditorGUILayout.EndScrollView();if(failure!=null)EditorGUILayout.HelpBox(failure,MessageType.Error);
            if(GUILayout.Button("Validate and save visual revision"))try
            {
                var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(File.ReadAllText("Assets/Spacewars/Content/Resources/ThreeCrossingsProfile.json")));
                ThreeCrossingsMaterials.ValidateRoads(data,map);data.revision++;
                File.WriteAllText(Path,JsonUtility.ToJson(data,true)+"\n");AssetDatabase.Refresh();failure=null;
            }catch(Exception e){failure=e.Message;}
        }
    }
}

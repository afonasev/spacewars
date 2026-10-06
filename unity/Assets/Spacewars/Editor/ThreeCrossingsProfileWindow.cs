using System.IO;
using Spacewars.Simulation;
using UnityEditor;
using UnityEngine;
namespace Spacewars.Editor
{
    public sealed class ThreeCrossingsProfileWindow:EditorWindow
    {
        private ThreeCrossingsProfileData data;private Vector2 scroll;private string failure;
        private const string Path="Assets/Spacewars/Content/Resources/ThreeCrossingsProfile.json";
        [MenuItem("Spacewars/Balance Lab/Three Crossings")]
        public static void Open()=>GetWindow<ThreeCrossingsProfileWindow>("Three Crossings");
        private void OnEnable(){data=JsonUtility.FromJson<ThreeCrossingsProfileData>(File.ReadAllText(Path));}
        private void OnGUI()
        {
            if(data==null)return;EditorGUILayout.LabelField(data.id+" @"+data.revision);scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var field in ThreeCrossingsMap.Fields){double value=EditorGUILayout.Slider(new GUIContent(field.Label+" ("+field.Unit+")",field.Path+"\n"+field.Description),(float)field.Read(data),(float)field.Minimum,(float)field.Maximum);field.Write(data,System.Math.Round(value/field.Step)*field.Step);}
            foreach(var field in ThreeCrossingsMap.ContourFields(data)){double value=EditorGUILayout.Slider(new GUIContent(field.Group+" / "+field.Label+" ("+field.Unit+")",field.Path+"\n"+field.Description),(float)field.Read(),(float)field.Minimum,(float)field.Maximum);field.Write(System.Math.Round(value/field.Step)*field.Step);}
            EditorGUILayout.EndScrollView();if(failure!=null)EditorGUILayout.HelpBox(failure,MessageType.Error);
            if(GUILayout.Button("Validate and save map revision"))try{var map=new ThreeCrossingsMap(data);PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(File.ReadAllText("Assets/Spacewars/Content/Resources/PlayableProfile.json")),map);data.revision++;File.WriteAllText(Path,JsonUtility.ToJson(data,true)+"\n");AssetDatabase.Refresh();failure=null;}catch(System.Exception ex){failure=ex.Message;}
        }
    }
}

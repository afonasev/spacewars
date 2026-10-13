using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spacewars.Editor
{
    public sealed class NereidaProject : EditorWindow
    {
        private NereidaProfileData profile;
        [MenuItem("Spacewars/Maps/Inspect Nereida Prototype")]
        public static void Open()
        {
            // Additive inspection preserves unsaved user scenes. No match or Player launch.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var root=new GameObject("Nereida — geometric prototype; match integration pending");
            root.AddComponent<NereidaPreview>().Build(NereidaPreview.LoadProfile());
            var light=new GameObject("Preview sun").AddComponent<Light>();light.transform.SetParent(root.transform);light.type=LightType.Directional;light.intensity=1.4f;light.transform.rotation=Quaternion.Euler(55,-35,0);
            Selection.activeGameObject=root;SceneView.lastActiveSceneView?.Frame(new Bounds(Vector3.zero,new Vector3(290,30,290)),false);
        }
        [MenuItem("Spacewars/Balance Lab/Nereida Geometry")]
        public static void Settings()=>GetWindow<NereidaProject>("Nereida geometry");
        private void OnGUI()
        {
            if(profile==null)profile=NereidaPreview.LoadProfile();
            EditorGUILayout.HelpBox("Geometric prototype. Saved profiles affect the next inspection. Ordinary match integration is pending.",MessageType.Info);
            foreach(var field in NereidaMap.Fields) {
                double value=EditorGUILayout.Slider(new GUIContent(field.Label+" ("+field.Unit+")",field.Description),(float)field.Read(profile),(float)field.Minimum,(float)field.Maximum);
                field.Write(profile,System.Math.Round(value/field.Step)*field.Step);
            }
            if(GUILayout.Button("Save named profile")) {
                try {new NereidaMap(profile);profile.revision++;System.IO.File.WriteAllText("Assets/Spacewars/Content/Resources/NereidaProfile.json",JsonUtility.ToJson(profile,true)+"\n");AssetDatabase.Refresh();}
                catch(System.ArgumentException e){EditorUtility.DisplayDialog("Invalid geometry",e.Message,"OK");}
            }
            if(GUILayout.Button("Open inspection"))Open();
        }
    }
}

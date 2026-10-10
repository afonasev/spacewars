using System;
using System.IO;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Spacewars.Editor
{
    public static class FoundryProject
    {
        public const string ScenePath="Assets/Spacewars/Content/Scenes/FoundryGreybox.unity";
        [MenuItem("Spacewars/Maps/Open Black Foundry Greybox")]
        public static void Open()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Black Foundry greybox").AddComponent<FoundryGreybox>();EditorSceneManager.SaveScene(scene,ScenePath);
        }
        // Batch Editor camera rendering: this does not build or run a Player.
        public static void Capture()
        {
            var output=Environment.GetEnvironmentVariable("FOUNDRY_EVIDENCE");if(string.IsNullOrEmpty(output))throw new ArgumentException("FOUNDRY_EVIDENCE required");Directory.CreateDirectory(output);
            var profile=FoundryGreybox.LoadProfile();var map=(FoundryMap)profile.AuthoredMap;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Foundry evidence");
            RenderSettings.ambientLight=new Color(.72f,.72f,.72f);RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            var light=new GameObject("Sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(55,-30,0);
            using(var world=new PlayableWorld(root.transform,profile))
            {
                world.InspectionReveal();world.InspectionSites();
                var camera=new GameObject("Evidence camera").AddComponent<Camera>();camera.orthographic=true;camera.nearClipPlane=.1f;camera.farClipPlane=700;camera.backgroundColor=new Color(.08f,.09f,.10f);camera.clearFlags=CameraClearFlags.SolidColor;
                Shot(camera,output,"overview",new Vector3(0,330,0),Quaternion.Euler(90,0,0),138);
                Shot(camera,output,"center-ramps",new Vector3(0,190,-105),Quaternion.Euler(62,0,0),84);
                Shot(camera,output,"allied-rear",new Vector3(0,145,10),Quaternion.Euler(58,0,0),66);
                Shot(camera,output,"outpost-concrete-preview",new Vector3(38,60,38),Quaternion.Euler(65,0,0),21);
                Shot(camera,output,"home-hex-preview",new Vector3(-44,60,76),Quaternion.Euler(65,0,0),24);
                Shot(camera,output,"left-pocket",new Vector3(-38,55,31),Quaternion.Euler(55,0,0),30);
                Shot(camera,output,"right-pocket",new Vector3(38,55,31),Quaternion.Euler(55,0,0),30);
                Shot(camera,output,"height-hierarchy",new Vector3(0,145,-70),Quaternion.Euler(55,0,0),115);
                Shot(camera,output,"height-ramp-close",new Vector3(-38,60,-22),Quaternion.Euler(45,0,0),52);
                Shot(camera,output,"right-lava-river",new Vector3(123,125,-66),Quaternion.Euler(60,0,0),85);
                File.WriteAllText(Path.Combine(output,"render-state.txt"),map.Id+"@"+map.Revision+"; scale="+map.Scale+" rear="+map.RearHeight+" upper="+map.UpperHeight+"; Editor production meshes/material, public diagnostic sites; no seed-dependent layout; no human acceptance.\n");
            }
            Open();AssetDatabase.SaveAssets();Debug.Log("FOUNDRY_EDITOR_CAPTURE_PASS "+output);
        }
        private static void Shot(Camera camera,string output,string name,Vector3 position,Quaternion rotation,float size)
        {
            camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=size;
            var target=new RenderTexture(1600,1600,24);camera.targetTexture=target;camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;var png=new Texture2D(1600,1600,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,1600,1600),0,0);png.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),png.EncodeToPNG());RenderTexture.active=previous;camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(png);UnityEngine.Object.DestroyImmediate(target);
        }
    }
}

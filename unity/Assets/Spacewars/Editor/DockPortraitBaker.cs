using System;
using System.IO;
using Spacewars.Presentation;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Editor
{
    // Reproducible HUD portraits from approved models. Never touches scene lighting or simulation.
    public static class DockPortraitBaker
    {
        [MenuItem("Spacewars/Presentation/Bake dock portraits")]
        public static void Bake() => BakeKeys(new[]{"headquarters","outpost","mine","hangar","factory","refinery","refineryUpgraded","scientificCenter","tank","explorer","shkval","hawk"});

        public static void BakeRefineries() => BakeKeys(new[]{"refinery","refineryUpgraded"});

        public static void BakeHawk() => BakeKeys(new[]{"hawk"});

        static void BakeKeys(string[] keys)
        {
            string folder=Path.Combine(Application.dataPath,"Spacewars/Content/Resources/DockPortraits");Directory.CreateDirectory(folder);
            foreach(string key in keys)
            {
                var prefab=Resources.Load<GameObject>("StyleA/"+key);if(!prefab)throw new InvalidOperationException("Missing portrait model "+key);
                var preview=new PreviewRenderUtility();GameObject model=null;
                try
                {
                    model=UnityEngine.Object.Instantiate(prefab);preview.AddSingleGO(model);
                    model.transform.rotation=Quaternion.Euler(0,155,0);
                    var finish=JsonUtility.FromJson<ActorMaterialProfile>(Resources.Load<TextAsset>("ActorMaterialProfile").text);finish.Validate();var paint=new Color(.16f,.61f,.78f);
                    var bounds=new Bounds();bool first=true;
                    foreach(var renderer in model.GetComponentsInChildren<Renderer>())
                    {
                        if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);
                        for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)finish.Apply(renderer,slot,paint);
                    }
                    var camera=preview.camera;camera.orthographic=true;camera.orthographicSize=bounds.extents.magnitude*.94f;
                    camera.nearClipPlane=.01f;camera.farClipPlane=1000;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                    camera.transform.position=bounds.center+new Vector3(-1, .85f,-1.6f).normalized*bounds.size.magnitude*3;camera.transform.LookAt(bounds.center);
                    preview.lights[0].intensity=1.4f;preview.lights[0].transform.rotation=Quaternion.Euler(40,35,0);
                    preview.lights[1].intensity=.8f;preview.lights[1].transform.rotation=Quaternion.Euler(340,215,0);preview.ambientColor=new Color(.38f,.42f,.48f);
                    preview.BeginPreview(new Rect(0,0,256,256),GUIStyle.none);
                    camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;preview.Render(true);
                    var rendered=(RenderTexture)preview.EndPreview();var previous=RenderTexture.active;RenderTexture.active=rendered;
                    var texture=new Texture2D(rendered.width,rendered.height,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,rendered.width,rendered.height),0,0);texture.Apply();RenderTexture.active=previous;
                    // Preview RT contains linear RGB; PNG/UI textures require sRGB. Preserve coverage alpha.
                    if(QualitySettings.activeColorSpace==ColorSpace.Linear){var pixels=texture.GetPixels();for(int i=0;i<pixels.Length;i++)pixels[i]=pixels[i].gamma;texture.SetPixels(pixels);texture.Apply();}
                    File.WriteAllBytes(Path.Combine(folder,key+".png"),texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
                }
                finally{preview.Cleanup();}
            }
            AssetDatabase.Refresh();
            foreach(string key in keys)
            {
                var path=Path.Combine(folder,key+".png");
                var asset="Assets"+path.Substring(Application.dataPath.Length);var importer=(TextureImporter)AssetImporter.GetAtPath(asset);
                importer.textureType=TextureImporterType.Default;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=false;importer.sRGBTexture=true;importer.maxTextureSize=256;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            }
            Debug.Log("DOCK_PORTRAITS_BAKED "+keys.Length+" actual model portraits");
        }
    }
}

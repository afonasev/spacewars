using System;
using System.IO;
using Spacewars.Presentation;
using Spacewars.Simulation;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Editor
{
    // Reproducible HUD portraits from approved models. Never touches scene lighting or simulation.
    public static class DockPortraitBaker
    {
        [MenuItem("Spacewars/Presentation/Bake dock portraits")]
        public static void Bake() => BakeKeys(new[]{"headquarters","outpost","mine","hangar","factory","refinery","refineryUpgraded","scientificCenter","tank","tankHybrid","tankHybridUpgraded","explorer","explorerUpgraded","shkval","shkval-guidance-3","hawk","hawkUpgraded","repair-tower","gauss-tower","sentry-turret"});

        public static void BakeRefineries() => BakeKeys(new[]{"refinery","refineryUpgraded"});

        public static void BakeHawk() => BakeKeys(new[]{"hawk","hawkUpgraded"});
        public static void BakeRepairTower() => BakeKeys(new[]{"repair-tower"});
        public static void BakeGaussTower() => BakeKeys(new[]{"gauss-tower"});
        public static void BakeTurret() => BakeKeys(new[]{"sentry-turret"});

        [MenuItem("Spacewars/Presentation/Bake defense portraits")]
        public static void BakeDefense() => BakeKeys(new[]{"sentry-turret","gauss-tower","repair-tower"});

        static void BakeKeys(string[] keys)
        {
            string folder=Path.Combine(Application.dataPath,"Spacewars/Content/Resources/DockPortraits");Directory.CreateDirectory(folder);
            foreach(string key in keys)
            {
                var prefab=Resources.Load<GameObject>("StyleA/"+PlayableWorld.ModelAssetKey(key=="scientificCenter"?"research-scanner":key=="hawkUpgraded"?"hawk":key));if(!prefab)throw new InvalidOperationException("Missing portrait model "+key);
                var preview=new PreviewRenderUtility();GameObject model=null;
                try
                {
                    model=UnityEngine.Object.Instantiate(prefab);preview.AddSingleGO(model);
                    if(key=="hawk"||key=="hawkUpgraded"){
                        var nodes=model.GetComponentsInChildren<Transform>(true);
                        nodes.Single(t=>t.name=="baseOnly").gameObject.SetActive(key=="hawk");
                        nodes.Single(t=>t.name=="upgradedOnly").gameObject.SetActive(key=="hawkUpgraded");
                    }
                    bool unit=key.StartsWith("tank")||key.StartsWith("explorer")||key.StartsWith("shkval")||key=="hawk"||key=="hawkUpgraded";
                    bool defense=key=="sentry-turret"||key=="gauss-tower"||key=="repair-tower";
                    float yaw=unit?155:180;
                    if(key=="mine")yaw+=JsonUtility.FromJson<ArmoredMineProfile>(Resources.Load<TextAsset>("ArmoredMineProfile").text).facingYawDegrees;
                    model.transform.rotation=Quaternion.Euler(0,yaw,0);
                    var finish=JsonUtility.FromJson<ActorMaterialProfile>(Resources.Load<TextAsset>("ActorMaterialProfile").text);finish.Validate();var paint=new Color(.16f,.61f,.78f);
                    var bounds=new Bounds();bool first=true;
                    foreach(var renderer in model.GetComponentsInChildren<Renderer>())
                    {
                        if(first){bounds=renderer.bounds;first=false;}else bounds.Encapsulate(renderer.bounds);
                        for(int slot=0;slot<renderer.sharedMaterials.Length;slot++)finish.Apply(renderer,slot,paint);
                    }
                    var camera=preview.camera;camera.orthographic=true;camera.orthographicSize=bounds.extents.magnitude*.94f;
                    camera.nearClipPlane=.01f;camera.farClipPlane=1000;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
                    var profile=PlayableProfile.ThreeCrossingsDefault;
                    var direction=unit?new Vector3(-1,.85f,-1.6f):new Vector3((float)profile.CameraOffsetX,(float)profile.CameraHeight,(float)profile.CameraOffsetZ);
                    if(defense)
                    {
                        var portrait=RadialCardProfile.Load();
                        float azimuth=portrait.defensePortraitYaw*Mathf.Deg2Rad,elevation=portrait.defensePortraitElevation*Mathf.Deg2Rad;
                        direction=new Vector3(Mathf.Sin(azimuth)*Mathf.Cos(elevation),Mathf.Sin(elevation),-Mathf.Cos(azimuth)*Mathf.Cos(elevation));
                    }
                    camera.transform.position=bounds.center+direction.normalized*bounds.size.magnitude*3;camera.transform.LookAt(bounds.center);
                    preview.lights[0].intensity=2.2f;preview.lights[0].transform.rotation=Quaternion.Euler(40,35,0);
                    preview.lights[1].intensity=1.2f;preview.lights[1].transform.rotation=Quaternion.Euler(340,215,0);preview.ambientColor=new Color(.55f,.59f,.65f);
                    preview.BeginPreview(new Rect(0,0,256,256),GUIStyle.none);
                    camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;preview.Render(true);
                    var rendered=(RenderTexture)preview.EndPreview();var previous=RenderTexture.active;RenderTexture.active=rendered;
                    var texture=new Texture2D(rendered.width,rendered.height,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,rendered.width,rendered.height),0,0);texture.Apply();RenderTexture.active=previous;
                    // Preview RT contains linear RGB; PNG/UI textures require sRGB. Preserve coverage alpha.
                    if(QualitySettings.activeColorSpace==ColorSpace.Linear){var pixels=texture.GetPixels();for(int i=0;i<pixels.Length;i++)pixels[i]=pixels[i].gamma;texture.SetPixels(pixels);texture.Apply();}
                    File.WriteAllBytes(Path.Combine(folder,key+".png"),texture.EncodeToPNG());
                    var source=texture.GetPixels32();var contour=new Color32[source.Length];
                    for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
                    {
                        byte alpha=0;
                        for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)if(x+dx>=0&&x+dx<texture.width&&y+dy>=0&&y+dy<texture.height)alpha=(byte)Math.Max(alpha,source[(y+dy)*texture.width+x+dx].a);
                        contour[y*texture.width+x]=new Color32(255,255,255,alpha);
                    }
                    texture.SetPixels32(contour);texture.Apply();File.WriteAllBytes(Path.Combine(folder,key+"-outline.png"),texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
                }
                finally{preview.Cleanup();}
            }
            AssetDatabase.Refresh();
            foreach(string key in keys.SelectMany(k=>new[]{k,k+"-outline"}))
            {
                var path=Path.Combine(folder,key+".png");
                var asset="Assets"+path.Substring(Application.dataPath.Length);var importer=(TextureImporter)AssetImporter.GetAtPath(asset);
                importer.textureType=TextureImporterType.Default;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.mipmapEnabled=false;importer.sRGBTexture=true;importer.maxTextureSize=256;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            }
            Debug.Log("DOCK_PORTRAITS_BAKED "+keys.Length+" actual model portraits");
        }
    }
}

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
        static readonly string[] UnitKeys={"tank","tankHybrid","tankHybridUpgraded","explorer","explorerUpgraded","shkval","shkval-guidance-3","hawk","hawkUpgraded"};
        [MenuItem("Spacewars/Presentation/Bake unit portraits")]
        public static void BakeUnits()=>BakeKeys(UnitKeys);

        // Explicit ungraded input prevents accumulating the grade on previously exported portraits.
        public static void GradeUnitPortraitsFromSource()
        {
            var source=Environment.GetEnvironmentVariable("SPACEWARS_PORTRAIT_SOURCE");
            var destination=Path.Combine(Application.dataPath,"Spacewars/Content/Resources/DockPortraits");
            if(string.IsNullOrEmpty(source)||!Directory.Exists(source)||Path.GetFullPath(source)==Path.GetFullPath(destination))
                throw new ArgumentException("SPACEWARS_PORTRAIT_SOURCE must name a separate directory of ungraded unit PNGs");
            foreach(var key in UnitKeys)if(!File.Exists(Path.Combine(source,key+".png")))throw new FileNotFoundException(key);
            var profile=RadialCardProfile.Load();
            foreach(var key in UnitKeys)
            {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                try
                {
                    if(!texture.LoadImage(File.ReadAllBytes(Path.Combine(source,key+".png"))))throw new InvalidDataException(key);
                    GradeUnitPortrait(texture,profile,key);
                    File.WriteAllBytes(Path.Combine(destination,key+".png"),texture.EncodeToPNG());
                }
                finally{UnityEngine.Object.DestroyImmediate(texture);}
            }
            AssetDatabase.Refresh();Debug.Log("Graded all 9 unit portraits from explicit source; alpha and outlines preserved.");
        }

        static void GradeUnitPortrait(Texture2D texture,RadialCardProfile profile,string key)
        {
            var pixels=texture.GetPixels32();
            float brightness=key.StartsWith("tank",StringComparison.Ordinal)?profile.tankPortraitBrightness:profile.unitPortraitBrightness;
            for(int i=0;i<pixels.Length;i++)
            {
                var p=pixels[i];if(p.a==0)continue;
                float r=Mathf.Clamp01(p.r/255f*brightness),g=Mathf.Clamp01(p.g/255f*brightness),b=Mathf.Clamp01(p.b/255f*brightness);
                // CSS sRGB saturation coefficients reproduce approved comparison A; alpha is coverage.
                float l=.213f*r+.715f*g+.072f*b,s=profile.unitPortraitSaturation;
                pixels[i]=new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(l+(r-l)*s)*255),(byte)Mathf.RoundToInt(Mathf.Clamp01(l+(g-l)*s)*255),(byte)Mathf.RoundToInt(Mathf.Clamp01(l+(b-l)*s)*255),p.a);
            }
            texture.SetPixels32(pixels);texture.Apply();
        }
        [MenuItem("Spacewars/Presentation/Bake dock portraits")]
        public static void Bake() => BakeKeys(new[]{"headquarters","outpost","mine","hangar","hangarUpgraded","factory","factoryUpgraded","refinery","refineryUpgraded","scientificCenter","scientificCenterUpgraded","tank","tankHybrid","tankHybridUpgraded","explorer","explorerUpgraded","shkval","shkval-guidance-3","hawk","hawkUpgraded","repair-tower","gauss-tower","sentry-turret","relay-tower"});

        public static void BakeUpgradedProducers(){BakeKeys(new[]{"factoryUpgraded","hangarUpgraded"});BakeResearchFraming();}

        public static void BakeUpgradedCenter() => BakeKeys(new[]{"scientificCenterUpgraded"});

        public static void BakeRefineries() => BakeKeys(new[]{"refinery","refineryUpgraded"});

        public static void BakeHawk() => BakeKeys(new[]{"hawk","hawkUpgraded"});
        public static void BakeRepairTower() => BakeKeys(new[]{"repair-tower"});
        public static void BakeGaussTower() => BakeKeys(new[]{"gauss-tower"});
        public static void BakeTurret() => BakeKeys(new[]{"sentry-turret"});
        public static void BakeRelayTower() => BakeKeys(new[]{"relay-tower"});

        [MenuItem("Spacewars/Presentation/Bake defense portraits")]
        public static void BakeDefense() => BakeKeys(new[]{"sentry-turret","gauss-tower","repair-tower"});

        // Measure the actual contour, retaining source PNGs and avoiding runtime pixel reads.
        public static void BakeResearchFraming()
        {
            var frames=new System.Collections.Generic.List<PortraitFrame>();
            foreach(string key in new[]{"tankHybridUpgraded","explorerUpgraded","shkval-guidance-3","hawkUpgraded","scientificCenterUpgraded","factory","hangar","factoryUpgraded","hangarUpgraded","refineryUpgraded"})
            {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                try
                {
                    texture.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath,"Spacewars/Content/Resources/DockPortraits",key+"-outline.png")));
                    var pixels=texture.GetPixels32();int x0=texture.width,y0=texture.height,x1=-1,y1=-1;
                    for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)if(pixels[y*texture.width+x].a>0){x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x);y1=Math.Max(y1,y);}
                    if(x1<x0)throw new InvalidOperationException("Empty portrait contour "+key);
                    // One source pixel of sampling clearance preserves the contour antialiasing.
                    x0=Math.Max(0,x0-1);y0=Math.Max(0,y0-1);x1=Math.Min(texture.width-1,x1+1);y1=Math.Min(texture.height-1,y1+1);
                    frames.Add(new PortraitFrame{key=key,uv=new Rect((float)x0/texture.width,(float)y0/texture.height,(float)(x1-x0+1)/texture.width,(float)(y1-y0+1)/texture.height)});
                }
                finally{UnityEngine.Object.DestroyImmediate(texture);}
            }
            File.WriteAllText(Path.Combine(Application.dataPath,"Spacewars/Content/Resources/UpgradePortraitFraming.json"),JsonUtility.ToJson(new PortraitFraming{frames=frames.ToArray()},true));
            AssetDatabase.Refresh();Debug.Log("UPGRADE_PORTRAIT_FRAMING_BAKED "+frames.Count);
        }

        static void BakeKeys(string[] keys)
        {
            string folder=Path.Combine(Application.dataPath,"Spacewars/Content/Resources/DockPortraits");Directory.CreateDirectory(folder);
            foreach(string key in keys)
            {
                var prefab=key=="factoryUpgraded"||key=="hangarUpgraded"?PlayableWorld.ModelPrefab(key):Resources.Load<GameObject>("StyleA/"+PlayableWorld.ModelAssetKey(key=="scientificCenterUpgraded"?"research-scanner-upgraded":key=="scientificCenter"?"research-scanner":key=="hawkUpgraded"?"hawk":key));if(!prefab)throw new InvalidOperationException("Missing portrait model "+key);
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
                    if(unit)GradeUnitPortrait(texture,RadialCardProfile.Load(),key);
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

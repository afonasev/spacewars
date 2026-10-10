using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Spacewars.Presentation
{
    // Map-independent art kit. Coordinates, roads and shore masks belong to each map adapter.
    [Serializable] public sealed class EnvironmentArtProfile
    {
        public string id="natural-frontier-v1";
        public int revision=5;
        [SurfaceSetting("Textures","Earth tile","World-space size of one earth texture tile.","m",1,20,.25f)] public float earthTile=7;
        [SurfaceSetting("Textures","Rock tile","World-space size of one cliff texture tile.","m",1,20,.25f)] public float rockTile=6;
        [SurfaceSetting("Textures","Concrete tile","World-space size of one concrete texture tile.","m",1,20,.25f)] public float concreteTile=5;
        [SurfaceSetting("Textures","Steel tile","World-space size of one brushed steel texture tile.","m",1,20,.25f)] public float steelTile=4;
        [SurfaceSetting("Textures","Detail strength","Blend between base art and photographed-style texture detail.","ratio",0,1,.05f)] public float textureStrength=.85f;
        [SurfaceSetting("Textures","Relief","Small surface normal variation; does not displace geometry.","ratio",0,1,.05f)] public float relief=.3f;
        [SurfaceSetting("Landscape","Moss","Muted moss on upward facing rock surfaces.","ratio",0,1,.05f)] public float moss=.3f;
        [SurfaceSetting("Landscape","Wet bank width","Darker earth along the river edge.","m",.1f,4,.1f)] public float wetBankWidth=1.2f;
        [SurfaceSetting("Landscape","Natural blend width","Soft transition around rock feet and across riverbank edges.","m",.5f,5,.1f)] public float naturalBlendWidth=2.4f;
        [SurfaceSetting("Landscape","Blend variation","Organic variation in natural material transition widths.","ratio",0,.8f,.05f)] public float blendVariation=.45f;
        [SurfaceSetting("Landscape","Blend patch size","World-space scale of natural transition variation.","m",.5f,8,.25f)] public float blendPatchSize=2.5f;
        [SurfaceSetting("Geology","Geology scale","Extent of the continuous basalt to oxidised-rock field.","m",15,100,1)] public float geologyScale=52;
        [SurfaceSetting("Geology","Rust coverage","Oxidised exposure within the same geological field.","ratio",0,1,.05f)] public float rustCoverage=.52f;
        [SurfaceSetting("Geology","Basalt value","Black basalt albedo value; retains readable silhouettes.","ratio",.1f,.6f,.01f)] public float basaltValue=.27f;
        [SurfaceSetting("Geology","Rust value","Orange mineral albedo value.","ratio",.2f,.8f,.01f)] public float rustValue=.57f;
        [SurfaceSetting("Geology","Strata spacing","World height between eroded geological layers.","m",.3f,3,.1f)] public float strataSpacing=.85f;
        [SurfaceSetting("Geology","Strata contrast","Dark mineral seams in exposed rock.","ratio",0,.7f,.05f)] public float strataContrast=.32f;
        [SurfaceSetting("Geology","Erosion depth","Presentation relief stays inside the authoritative solid volume.","m",0,1.5f,.05f)] public float erosionDepth=.8f;
        [SurfaceSetting("Detailing","Scree reach","Distance from cliff feet occupied by debris clusters.","m",1,8,.25f)] public float screeReach=5;
        [SurfaceSetting("Detailing","Scree density","Density of local debris clusters.","ratio",0,1,.05f)] public float screeDensity=.85f;
        [SurfaceSetting("Detailing","Cluster spacing","Minimum separation of distinct cliff-foot debris groups.","m",6,20,.5f)] public float screeClusterSpacing=12;
        [SurfaceSetting("Detailing","Pebble radius","Smallest visible debris radius.","m",.08f,.5f,.02f)] public float pebbleRadius=.18f;
        [SurfaceSetting("Detailing","Boulder radius","Largest decorative rock radius.","m",.5f,1.5f,.05f)] public float boulderRadius=1.05f;
        [SurfaceSetting("Detailing","Clearance","Extra empty margin around travel and construction areas.","m",.25f,2,.05f)] public float detailClearance=.6f;
        [SurfaceSetting("Detailing","Service scale","Scale of background service modules.","ratio",.5f,1.5f,.05f)] public float serviceScale=1;
        [SurfaceSetting("Weathering","Dust reach","Maximum sand reach inward from constructed edges.","m",.5f,6,.25f)] public float dustReach=3.25f;
        [SurfaceSetting("Weathering","Deposit length","Scale of separated sand tongues along construction edges.","m",2,12,.5f)] public float dustPatchLength=6;
        [SurfaceSetting("Weathering","Deposit coverage","Fraction of the deposition noise field admitting sand; lower values expose more edge.","ratio",.2f,.8f,.05f)] public float dustCoverage=.45f;
        [SurfaceSetting("Weathering","Dust strength","Opacity of deposits; panel joints remain partly visible.","ratio",0,.9f,.05f)] public float dustStrength=.72f;
        [SurfaceSetting("Weathering","Wind direction","Direction shared by deposited sand patterns.","deg",0,360,5)] public float dustWind=35;
        [SurfaceSetting("Weathering","Wear strength","Restrained tire and entry wear contrast.","ratio",0,.4f,.02f)] public float wearStrength=.18f;
        [SurfaceSetting("Weathering","Contact shade","Local grounding shade beneath debris and equipment.","ratio",0,.5f,.02f)] public float contactShade=.24f;
        [SurfaceSetting("Water","Hue","Deep turquoise water hue.","turn",0,1,.01f)] public float waterHue=.49f;
        [SurfaceSetting("Water","Saturation","Water color saturation.","ratio",0,1,.01f)] public float waterSaturation=.72f;
        [SurfaceSetting("Water","Brightness","Deep water value.","ratio",.05f,.9f,.01f)] public float waterBrightness=.34f;
        [SurfaceSetting("Water","Shallow brightness","Light turquoise near the shore.","ratio",.1f,1,.01f)] public float shallowBrightness=.63f;
        [SurfaceSetting("Water","Shore width","Distance over which shallow water becomes deep water.","m",.1f,6,.1f)] public float shoreWidth=2.8f;
        [SurfaceSetting("Water","Ripple scale","World size of water ripples.","m",.25f,8,.25f)] public float rippleScale=3.5f;
        [SurfaceSetting("Water","Flow speed","Visual flow speed, independent of simulation.","m/s",0,2,.05f)] public float flowSpeed=.35f;
        [SurfaceSetting("Water","Ripple strength","Water normal distortion.","ratio",0,1,.05f)] public float rippleStrength=.15f;
        [SurfaceSetting("Water","Reflection","Soft sky reflection intensity.","ratio",0,1,.05f)] public float reflection=.25f;
        [SurfaceSetting("Water","Foam","Intermittent pale foam along the land boundary.","ratio",0,1,.05f)] public float foam=.35f;

        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach(var field in GetType().GetFields())
            {
                var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a==null)continue;
                yield return new SurfaceParameter("visual.environment."+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));
            }
        }
        public void Validate()
        {
            if(id!="natural-frontier-v1"||revision<1)throw new ArgumentException("Invalid environment art identity");
            foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid environment setting: "+p.Path);
        }
        public static EnvironmentArtProfile Load()
        {
            var asset=Resources.Load<TextAsset>("Environment/NaturalFrontier/Profile");
            if(!asset)throw new InvalidOperationException("Missing Natural Frontier environment profile");
            var p=JsonUtility.FromJson<EnvironmentArtProfile>(asset.text);p.Validate();return p;
        }
    }

    public static class EnvironmentArt
    {
        // R8 distance encoding covers eight world meters; fixed data contract, not an art setting.
        public const float ShoreDistanceRange=8;
        public static void Apply(Material material,EnvironmentArtProfile profile)
        {
            profile.Validate();
            foreach(var role in new[]{"Earth","Rock","Concrete","Steel"})
            {
                var texture=Resources.Load<Texture2D>("Environment/NaturalFrontier/"+role);
                if(!texture)throw new InvalidOperationException("Missing environment texture: "+role);
                material.SetTexture("_"+role+"Tex",texture);
            }
            material.SetVector("_Weathering",new Vector4(profile.dustReach,profile.dustStrength,profile.dustWind*Mathf.Deg2Rad,profile.wearStrength));
            material.SetFloat("_ContactShade",profile.contactShade);
            material.SetVector("_Geology",new Vector4(profile.geologyScale,profile.rustCoverage,profile.strataSpacing,profile.strataContrast));
            material.SetColor("_BasaltTint",Color.HSVToRGB(.61f,.16f,profile.basaltValue));
            material.SetColor("_RustTint",Color.HSVToRGB(.055f,.72f,profile.rustValue));
            material.SetVector("_NaturalBlend",new Vector4(profile.naturalBlendWidth,profile.blendVariation,profile.blendPatchSize,0));
            material.SetVector("_ArtTiles",new Vector4(profile.earthTile,profile.rockTile,profile.concreteTile,profile.steelTile));
            material.SetVector("_ArtDetail",new Vector4(profile.textureStrength,profile.relief,profile.moss,profile.wetBankWidth));
            material.SetColor("_WaterDeep",Color.HSVToRGB(profile.waterHue,profile.waterSaturation,profile.waterBrightness));
            material.SetColor("_WaterShallow",Color.HSVToRGB(profile.waterHue,.65f*profile.waterSaturation,profile.shallowBrightness));
            material.SetVector("_WaterMotion",new Vector4(profile.rippleScale,profile.flowSpeed,profile.rippleStrength,profile.reflection));
            material.SetVector("_ShoreSettings",new Vector4(profile.shoreWidth,profile.foam,ShoreDistanceRange,0));
        }
    }
}

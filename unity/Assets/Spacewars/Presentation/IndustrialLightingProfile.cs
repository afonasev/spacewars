using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Spacewars.Presentation
{
    // Presentation-only tuning; no simulation/profile state is consumed here.
    [Serializable] public sealed class IndustrialLightingProfile
    {
        public string id="industrial-warm-cool-v1";
        public int revision=3;
        [SurfaceSetting("Sun","Hue","Warm directional light hue.","turn",0,1,.01f)] public float sunHue=.09f;
        [SurfaceSetting("Sun","Saturation","Warmth of light; preserve team paint readability.","ratio",0,.4f,.01f)] public float sunSaturation=.16f;
        [SurfaceSetting("Sun","Intensity","Direct light strength on existing geometry.","ratio",.2f,2,.05f)] public float sunIntensity=1.35f;
        [SurfaceSetting("Sun","Elevation","Sun elevation; lower angles lengthen shadows.","deg",20,75,1)] public float sunElevation=42;
        [SurfaceSetting("Sun","Azimuth","Sun direction across the map.","deg",-180,180,1)] public float sunAzimuth=-30;
        [SurfaceSetting("Fill","Hue","Cool ambient fill hue.","turn",0,1,.01f)] public float ambientHue=.6f;
        [SurfaceSetting("Fill","Saturation","Cool tint in shaded faces.","ratio",0,.4f,.01f)] public float ambientSaturation=.18f;
        [SurfaceSetting("Fill","Value","Ambient brightness; keep shaded units readable.","ratio",.2f,.8f,.01f)] public float ambientValue=.52f;
        [SurfaceSetting("Shadows","Strength","Main light shadow opacity; ambient fill is retained.","ratio",0,1,.05f)] public float shadowStrength=.65f;
        [SurfaceSetting("Shadows","Minimum distance","Minimum camera-relative coverage; visible solid objects automatically expand this distance.","m",50,150,5)] public float shadowDistance=90;
        [SurfaceSetting("Shadows","Coverage reserve","Reserve beyond visible bounds to keep edge objects out of shadow fading.","ratio",0,1,.05f)] public float shadowCoveragePadding=.3f;
        [SurfaceSetting("Materials","Steel highlight","Broad restrained highlight on existing steel surfaces.","ratio",0,1,.01f)] public float steelHighlight=.18f;
        [SurfaceSetting("Materials","Steel exponent","Lower values spread the steel highlight.","power",8,128,1)] public float steelExponent=24;
        [SurfaceSetting("Materials","Concrete highlight","Very restrained sheen on exposed concrete.","ratio",0,.2f,.01f)] public float concreteHighlight=.03f;
        [SurfaceSetting("Materials","Concrete exponent","Broad concrete highlight width.","power",4,64,1)] public float concreteExponent=12;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach(var field in GetType().GetFields())
            {var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a==null)continue;yield return new SurfaceParameter((id=="three-crossings-warm-v1"?"visual.threeCrossings.lighting.":"visual.lighting.")+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));}
        }
        public void Validate()
        {
            if((id!="industrial-warm-cool-v1"&&id!="three-crossings-warm-v1")||revision<1)throw new ArgumentException("Invalid lighting identity");
            foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid lighting: "+p.Path);
        }
        public static string ResourceName(string mapId=null)=>mapId=="three-crossings-greybox-v1"?"ThreeCrossingsLightingProfile":"IndustrialLightingProfile";
        public static IndustrialLightingProfile Load(string mapId=null)
        {var resource=Resources.Load<TextAsset>(ResourceName(mapId));if(!resource)throw new InvalidOperationException("Missing lighting profile for "+mapId);var p=JsonUtility.FromJson<IndustrialLightingProfile>(resource.text);NativeGraphicsSettings.Apply(p.Parameters());p.Validate();return p;}
        public void ApplyScene(Light sun)
        {
            Validate();RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=Color.HSVToRGB(ambientHue,ambientSaturation,ambientValue);
            sun.type=LightType.Directional;sun.color=Color.HSVToRGB(sunHue,sunSaturation,1);sun.intensity=sunIntensity;
            sun.transform.rotation=Quaternion.Euler(sunElevation,sunAzimuth,0);sun.shadowStrength=shadowStrength;
            if(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)pipeline.shadowDistance=shadowDistance;
            NativeDisplaySettings.ConfigureSun(sun);
        }
        public void ApplyMaterial(Material material)
        {Validate();material.SetVector("_IndustrialFinish",new Vector4(steelHighlight,steelExponent,concreteHighlight,concreteExponent));}
    }
}

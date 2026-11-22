using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class AntennaCityArtProfile
    {
        public string id="nova-city-night-line-v1";public int revision=1;
        [SurfaceSetting("City surfaces","Road value","Dry asphalt brightness beneath armies.","ratio",.2f,.6f,.01f)] public float roadValue=.36f;
        [SurfaceSetting("City surfaces","Yard value","Service concrete brightness outside routes.","ratio",.2f,.7f,.01f)] public float yardValue=.43f;
        [SurfaceSetting("City surfaces","Apron value","Quiet construction floor brightness.","ratio",.3f,.8f,.01f)] public float apronValue=.51f;
        [SurfaceSetting("City surfaces","Facade value","Graphite facade brightness.","ratio",.2f,.7f,.01f)] public float facadeValue=.43f;
        [SurfaceSetting("City surfaces","Texture tile","Continuous world-space albedo scale.","m",1,8,.25f)] public float textureTile=4;
        [SurfaceSetting("City surfaces","Texture contrast","Fine wear strength.","ratio",0,.8f,.025f)] public float textureContrast=.25f;
        [SurfaceSetting("City surfaces","Variant region","Irregular sibling texture region size.","m",8,40,1)] public float patchSize=19;
        [SurfaceSetting("City architecture","Facade bay","Window and structural bay spacing.","m",2,6,.25f)] public float bayWidth=3;
        [SurfaceSetting("City architecture","Roof recess","Depth inside the unchanged obstacle envelope.","m",.3f,1.5f,.1f)] public float roofRecess=.8f;
        [SurfaceSetting("City architecture","Parapet width","Roof rim width inside the footprint.","m",.15f,.7f,.05f)] public float parapetWidth=.35f;
        [SurfaceSetting("City architecture","Equipment spacing","Compact rooftop equipment separation.","m",5,15,1)] public float equipmentSpacing=9;
        [SurfaceSetting("City lights","Window emission","Window glow, before team fog masking.","ratio",0,2,.05f)] public float windowEmission=.55f;
        [SurfaceSetting("City lights","Sign emission","Restrained transit signs and edge lighting.","ratio",0,3,.05f)] public float signEmission=.85f;
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var f in GetType().GetFields()){var a=f.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.antennaCity."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=>(float)f.GetValue(this),v=>f.SetValue(this,v));}}
        public void Validate(){if(id!="nova-city-night-line-v1"||revision<1)throw new ArgumentException("Invalid Nova City art identity");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException(p.Path);}
        public static AntennaCityArtProfile Load(){var asset=Resources.Load<TextAsset>("AntennaCityArtProfile");if(!asset)throw new InvalidOperationException("Missing Nova City art profile");var p=JsonUtility.FromJson<AntennaCityArtProfile>(asset.text);p.Validate();return p;}
    }
}

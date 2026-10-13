using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class NereidaArtProfile
    {
        public string id="nereida-polar-shelf-v1"; public int revision=1;
        [SurfaceSetting("Nereida surfaces","Detail tile","World-space scale of sharp sibling albedo detail.","m",2,16,.5f)] public float detailTile=7;
        [SurfaceSetting("Nereida surfaces","Variant region","Extent of irregular single-image regions, independent of fine detail.","m",8,50,1)] public float variantScale=24;
        [SurfaceSetting("Nereida surfaces","Variant transition","Noise-space width of soft sibling transitions.","ratio",.02f,.3f,.01f)] public float variantBlend=.06f;
        [SurfaceSetting("Nereida surfaces","Detail contrast","Calibrated albedo grain contrast around the role tint.","ratio",0,1,.05f)] public float detailContrast=.45f;
        [SurfaceSetting("Nereida surfaces","Normal slope","Maximum finite micro-relief slope; presentation only.","ratio",0,.5f,.025f)] public float normalSlope=.15f;
        [SurfaceSetting("Nereida geology","Snow field scale","Broad drift coverage, independent of texture projection.","m",8,60,1)] public float snowScale=27;
        [SurfaceSetting("Nereida geology","Snow coverage","Irregular surface snow on turquoise ice and exposed terrace rock.","ratio",0,1,.05f)] public float snowCoverage=.35f;
        [SurfaceSetting("Nereida geology","Ice strata spacing","Vertical spacing of sediment bands on cliffs.","m",.3f,3,.1f)] public float strataSpacing=.8f;
        [SurfaceSetting("Nereida geology","Ice strata contrast","Restrained layered ice/rock side contrast.","ratio",0,.6f,.05f)] public float strataContrast=.12f;
        [SurfaceSetting("Nereida geology","Snow value","Snow albedo brightness in sRGB; retains visible texture under preview lighting.","ratio",.3f,.8f,.025f)] public float snowValue=.6f;
        [SurfaceSetting("Nereida geology","Drift feather","Soft authored drift edges in continuous world coordinates.","m",.3f,4,.1f)] public float driftFeather=2;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach(var field in GetType().GetFields()) { var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a==null)continue;
                yield return new SurfaceParameter("visual.nereida."+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v)); }
        }
        public void Validate()
        {
            if(id!="nereida-polar-shelf-v1"||revision<1)throw new ArgumentException("Invalid Nereida art identity");
            foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid Nereida art: "+p.Path);
        }
        public static NereidaArtProfile Load() { var p=JsonUtility.FromJson<NereidaArtProfile>(Resources.Load<TextAsset>("NereidaArtProfile").text);p.Validate();return p; }
        public Material Material(string family,Vector3 means,int role)
        {
            Validate();var m=new Material(Resources.Load<Shader>("NereidaSurface"));
            EnvironmentArt.BindFamily(m,"Detail",family,means);
            m.SetVector("_DetailSettings",new Vector4(detailTile,variantScale,variantBlend,detailContrast));
            m.SetVector("_SnowSettings",new Vector4(snowScale,snowCoverage,strataSpacing,strataContrast));
            m.SetVector("_SnowFinish",new Vector4(Mathf.GammaToLinearSpace(snowValue),driftFeather,0,0));
            m.SetFloat("_NormalSlope",normalSlope);m.SetFloat("_Role",role);return m;
        }
    }
}

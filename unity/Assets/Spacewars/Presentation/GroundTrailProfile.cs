using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class GroundTrailProfile
    {
        public string id="ground-trails-readable-soft-v1";public int revision=2;
        [SurfaceSetting("Appearance","Opacity","Strength of soft-ground impressions.","ratio",0,1,.02f)] public float opacity=.65f;
        [SurfaceSetting("Appearance","Fade","Final fade duration, capped by the lifetime selected in Balance Lab.","s",.1f,10,.1f)] public float fadeSeconds=2.5f;
        [SurfaceSetting("Appearance","Soft edges","Feathered width of each contact strip.","ratio",.01f,.45f,.01f)] public float edgeSoftness=.14f;
        [SurfaceSetting("Appearance","Grass strength","Relative contrast on compressed moss and grass.","ratio",0,1,.02f)] public float grassStrength=1f;
        [SurfaceSetting("Appearance","Grain","Broken organic impression grain.","ratio",0,1,.02f)] public float grain=.24f;
        [SurfaceSetting("Explorer","Half gauge","Half wheel separation in authored model coordinates.","m",.1f,2,.01f)] public float explorerGauge=.73f;
        [SurfaceSetting("Explorer","Tire width","Contact width in authored model coordinates.","m",.05f,1,.01f)] public float explorerWidth=.27f;
        [SurfaceSetting("Explorer","Tread pitch","Tread repeat length in authored model coordinates.","m",.05f,1,.01f)] public float explorerPitch=.22f;
        [SurfaceSetting("Tank","Half gauge","Half caterpillar separation in authored model coordinates.","m",.1f,2,.01f)] public float tankGauge=.98f;
        [SurfaceSetting("Tank","Track width","Contact width in authored model coordinates.","m",.05f,1,.01f)] public float tankWidth=.46f;
        [SurfaceSetting("Tank","Tread pitch","Caterpillar cleat repeat length.","m",.05f,1,.01f)] public float tankPitch=.3f;
        [SurfaceSetting("Shkval","Half gauge","Half heavy wheel separation in authored model coordinates.","m",.1f,2,.01f)] public float shkvalGauge=.72f;
        [SurfaceSetting("Shkval","Tire width","Heavy tire contact width.","m",.05f,1,.01f)] public float shkvalWidth=.28f;
        [SurfaceSetting("Shkval","Tread pitch","Heavy chevron repeat length.","m",.05f,1,.01f)] public float shkvalPitch=.42f;
        [SurfaceSetting("Explorer","Rear contact","Ground-contact edge measured from model origin; used at the trailing end of travel.","m",0,3,.001f)] public float explorerRearContact=1.112f;
        [SurfaceSetting("Explorer","Front contact","Ground-contact edge measured from model origin; used at the trailing end of travel.","m",0,3,.001f)] public float explorerFrontContact=1.098f;
        [SurfaceSetting("Tank","Rear contact","Ground-contact edge measured from model origin; used at the trailing end of travel.","m",0,3,.001f)] public float tankRearContact=1.223f;
        [SurfaceSetting("Tank","Front contact","Ground-contact edge measured from model origin; used at the trailing end of travel.","m",0,3,.001f)] public float tankFrontContact=1.223f;
        [SurfaceSetting("Shkval","Rear contact","Ground-contact edge measured from model origin; used at the trailing end of travel.","m",0,3,.001f)] public float shkvalRearContact=1.329f;
        [SurfaceSetting("Shkval","Front contact","Ground-contact edge measured from model origin; used at the trailing end of travel.","m",0,3,.001f)] public float shkvalFrontContact=1.179f;
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var f in GetType().GetFields()){var a=f.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.groundTrails."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)f.GetValue(this),v=>f.SetValue(this,v));}}
        public void Validate(){if(id!="ground-trails-readable-soft-v1"||revision<1)throw new ArgumentException("Invalid ground trail profile");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException(p.Path);}
        public static GroundTrailProfile Load(){var text=Resources.Load<TextAsset>("GroundTrailProfile");if(!text)throw new InvalidOperationException("Missing ground trail profile");var p=JsonUtility.FromJson<GroundTrailProfile>(text.text);p.Validate();return p;}
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Spacewars.Presentation
{
    [Serializable] public sealed class SplitScreenHudProfile
    {
        public string id="split-screen-hud-v1";
        public int revision=1;
        [SurfaceSetting("Разделители экранов","Толщина","Ширина видимой линии между локальными viewport.","px",1,6,1)] public float thickness=3;
        [SurfaceSetting("Разделители экранов","Зазор у миникарты","Расстояние от конца линии до внешней рамки общей миникарты.","px",2,12,1)] public float minimapGap=4;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach(var field in GetType().GetFields())
            {
                var m=field.GetCustomAttribute<SurfaceSettingAttribute>();if(m==null)continue;
                yield return new SurfaceParameter("visual.splitScreen."+field.Name,m.Group,m.Label,m.Description,m.Unit,m.Minimum,m.Maximum,m.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));
            }
        }
        public void Validate()
        {
            if(id!="split-screen-hud-v1"||revision<1)throw new ArgumentException("Invalid split-screen HUD identity");
            foreach(var p in Parameters()){float v=p.Read();if(float.IsNaN(v)||float.IsInfinity(v)||v<p.Minimum||v>p.Maximum||Mathf.Abs(v/p.Step-Mathf.Round(v/p.Step))>.001f)throw new ArgumentException("Invalid split-screen parameter: "+p.Path);}
        }
        public static SplitScreenHudProfile Load()
        {
            var asset=Resources.Load<TextAsset>("SplitScreenHudProfile");if(!asset)throw new InvalidOperationException("Missing split-screen HUD profile");
            var profile=JsonUtility.FromJson<SplitScreenHudProfile>(asset.text);profile.Validate();return profile;
        }
    }
}

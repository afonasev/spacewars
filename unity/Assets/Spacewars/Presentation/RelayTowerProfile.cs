using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Spacewars.Presentation
{
    [Serializable]
    public sealed class RelayTowerProfile
    {
        public string id="relay-tower-art-v1";
        public int revision=1;
        [SurfaceSetting("Вышка связи", "Скорость вращения", "Вращение верхнего блока только при наличии владельца; время матча, без связи с доходом.", "deg/s", 1, 60, 1)]
        public float rotationDegreesPerSecond=18;
        [SurfaceSetting("Вышка связи", "Ширина индикатора захвата", "Толщина дуги прогресса на круглой опоре вышки.", "m", .04f, .2f, .01f)]
        public float progressRingWidth=.1f;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach(var f in GetType().GetFields())
            {
                var a=f.GetCustomAttribute<SurfaceSettingAttribute>();
                if(a!=null)yield return new SurfaceParameter("visual.relayTower."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)f.GetValue(this),v=>f.SetValue(this,v));
            }
        }
        public void Validate()
        {
            if(id!="relay-tower-art-v1"||revision<1)throw new ArgumentException("Relay tower profile identity");
            foreach(var p in Parameters())if(!float.IsFinite(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException(p.Path);
        }
        public static RelayTowerProfile Load()
        {
            var asset=Resources.Load<TextAsset>("RelayTowerProfile");
            if(!asset)throw new InvalidOperationException("Missing relay tower art profile");
            var profile=JsonUtility.FromJson<RelayTowerProfile>(asset.text);profile.Validate();return profile;
        }
    }
}

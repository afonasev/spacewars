using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Spacewars.Presentation
{
    [Serializable]
    public sealed class ArmoredMineProfile
    {
        public string id = "armored-mine-v1";
        public int revision = 2;
        [SurfaceSetting("Mine", "Cycle duration", "Full weighted extraction stroke; independent of income.", "s", 3, 12, .1f)] public float cycleSeconds=5;
        [SurfaceSetting("Mine", "Rocker angle", "Angular excursion within authored mechanism clearance.", "deg", 2, 12, .5f)] public float rockerDegrees=12;
        [SurfaceSetting("Mine", "Dust rate", "Brief bottom-stroke dust emission.", "1/s", 0, 20, 1)] public float dustRate=12;
        [SurfaceSetting("Mine", "Dust lifetime", "Lifetime of extraction dust.", "s", .2f, 1, .1f)] public float dustLifetime=.55f;
        [SurfaceSetting("Mine", "Dust rise", "Slow dust drift.", "m/s", .02f, .3f, .01f)] public float dustRise=.12f;
        [SurfaceSetting("Mine", "Dust size", "Dust diameter in model units.", "m", .05f, .4f, .01f)] public float dustSize=.18f;
        [SurfaceSetting("Mine", "Dust opacity", "Subtle extraction dust opacity.", "ratio", 0, .3f, .01f)] public float dustOpacity=.13f;
        [SurfaceSetting("Mine", "Deep shaft glow", "Red light from recessed lower shaft surfaces, independent of owner lamps.", "gain", 0, 4, .1f)] public float wellEmission=2.2f;
        [SurfaceSetting("Mine", "Shaft light intensity", "Red light reflected on the inner rim and lower mechanism.", "gain", 0, 3, .1f)] public float wellLightIntensity=1.2f;
        [SurfaceSetting("Mine", "Shaft light reach", "Local red illumination radius in model units.", "m", .5f, 1.5f, .05f)] public float wellLightRange=1.15f;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach (var f in GetType().GetFields())
            {
                var a = f.GetCustomAttribute<SurfaceSettingAttribute>();
                if (a != null) yield return new SurfaceParameter("visual.mine." + f.Name, a.Group, a.Label, a.Description, a.Unit, a.Minimum, a.Maximum, a.Step, () => (float)f.GetValue(this), v => f.SetValue(this, v));
            }
        }
        public void Validate()
        {
            if (id != "armored-mine-v1" || revision < 1) throw new ArgumentException("Invalid mine profile identity");
            foreach (var p in Parameters()) if (float.IsNaN(p.Read()) || float.IsInfinity(p.Read()) || p.Read() < p.Minimum || p.Read() > p.Maximum) throw new ArgumentException(p.Path);
        }
        public static ArmoredMineProfile Load()
        {
            var text = Resources.Load<TextAsset>("ArmoredMineProfile");
            if (!text) throw new InvalidOperationException("Missing armored mine profile");
            var p = JsonUtility.FromJson<ArmoredMineProfile>(text.text); p.Validate(); return p;
        }
    }
}

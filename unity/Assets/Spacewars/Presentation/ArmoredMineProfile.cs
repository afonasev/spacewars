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
        public int revision = 6;
        [SurfaceSetting("Mine", "Facing offset", "Visual yaw relative to world south; negative turns toward screen right at the default camera. Does not change territory or collision.", "deg", -180, 180, 1)] public float facingYawDegrees=-30;
        [SurfaceSetting("Mine", "Cycle duration", "Full weighted extraction stroke; independent of income.", "s", 3, 12, .1f)] public float cycleSeconds=5;
        [SurfaceSetting("Mine", "Rocker angle", "Angular excursion within authored mechanism clearance.", "deg", 2, 16, .5f)] public float rockerDegrees=16;
        [SurfaceSetting("Mine", "Dust rate", "Brief bottom-stroke dust emission.", "1/s", 0, 20, 1)] public float dustRate=12;
        [SurfaceSetting("Mine", "Dust lifetime", "Lifetime of extraction dust.", "s", .2f, 1, .1f)] public float dustLifetime=.55f;
        [SurfaceSetting("Mine", "Dust rise", "Slow dust drift.", "m/s", .02f, .3f, .01f)] public float dustRise=.12f;
        [SurfaceSetting("Mine", "Dust size", "Dust diameter in model units.", "m", .05f, .4f, .01f)] public float dustSize=.18f;
        [SurfaceSetting("Mine", "Dust opacity", "Subtle extraction dust opacity.", "ratio", 0, .3f, .01f)] public float dustOpacity=.13f;
        [SurfaceSetting("Mine", "Deep shaft glow", "Red light from recessed lower shaft surfaces, independent of owner lamps.", "gain", 0, 4, .1f)] public float wellEmission=2.2f;
        [SurfaceSetting("Mine", "Shaft light intensity", "Red light reflected on the inner rim and lower mechanism.", "gain", 0, 3, .1f)] public float wellLightIntensity=1.2f;
        [SurfaceSetting("Mine", "Shaft light reach", "Local red illumination radius in model units.", "m", .5f, 1.5f, .05f)] public float wellLightRange=1.15f;
        [SurfaceSetting("Mine impact", "Spark count", "Brief warm chips at the bottom of each extraction stroke.", "count", 0, 24, 1)] public float sparkCount=8;
        [SurfaceSetting("Mine impact", "Spark lifetime", "Lifetime of the single impact burst; match time only.", "s", .1f, .6f, .01f)] public float sparkLifetime=.42f;
        [SurfaceSetting("Mine impact", "Spark speed", "Initial speed of chips leaving the shaft.", "m/s", .1f, 4, .1f)] public float sparkSpeed=3.4f;
        [SurfaceSetting("Mine impact", "Spark size", "Small bright chip width in model units.", "m", .01f, .12f, .005f)] public float sparkSize=.045f;
        [SurfaceSetting("Mine impact", "Spark spread", "Cone spread around the upward impact direction.", "deg", 5, 85, 1)] public float sparkSpread=40;
        [SurfaceSetting("Mine impact", "Spark origin depth", "Depth below the authored shaft rim; chips must originate inside the bore.", "m", .1f, .35f, .01f)] public float sparkOriginDepth=.22f;
        [SurfaceSetting("Mine impact", "Spark gravity", "Downward acceleration of thrown chips.", "g", 0, 2, .1f)] public float sparkGravity=1;
        [SurfaceSetting("Mine impact", "Impact glow boost", "Brief extra shaft emission and light with a warm colour pulse; zero disables the pulse.", "ratio", 0, 1, .05f)] public float impactGlowBoost=.4f;
        [SurfaceSetting("Mine impact", "Impact glow duration", "Decay after bottom-stroke contact; never uses wall time.", "s", .05f, .5f, .01f)] public float impactGlowDuration=.38f;
        [SurfaceSetting("Mine impact", "Impact glow hue", "Peak heat colour: zero retains steady red, about one sixth reaches yellow.", "HSV", 0, .18f, .005f)] public float impactGlowHue=.16f;
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

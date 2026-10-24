using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Spacewars.Presentation
{
    [Serializable]
    public sealed class FactoryModelProfile
    {
        public string id = "factory-armored-preview-v1";
        public int revision = 1;
        [SurfaceSetting("Factory", "Fan speed", "Working rear ventilation speed.", "deg/s", 30, 720, 10)] public float fanSpeed = 240;
        [SurfaceSetting("Factory", "Flash rate", "Rate of irregular concealed assembly flashes.", "Hz", .2f, 6, .1f)] public float flashRate = 2.4f;
        [SurfaceSetting("Factory", "Flash intensity", "Owner-colored emission through baffled roof vents.", "gain", 0, 12, .1f)] public float flashIntensity = .9f;
        [SurfaceSetting("Factory", "Smoke rate", "Particles per second at each rear exhaust.", "1/s", 0, 15, .5f)] public float smokeRate = 5;
        [SurfaceSetting("Factory", "Smoke lifetime", "Time until residual exhaust dissipates.", "s", .2f, 3, .1f)] public float smokeLifetime = 1.8f;
        [SurfaceSetting("Factory", "Smoke rise", "Upward exhaust drift.", "m/s", .05f, 1, .05f)] public float smokeRise = .35f;
        [SurfaceSetting("Factory", "Smoke size", "Maximum puff diameter in model coordinates.", "m", .1f, .8f, .05f)] public float smokeSize = .42f;
        [SurfaceSetting("Factory", "Smoke opacity", "Opacity of working smoke, separate from damage smoke.", "ratio", .02f, .5f, .01f)] public float smokeOpacity = .2f;
        [SurfaceSetting("Factory", "Door travel", "Sectional door travel beneath fixed lintel.", "m", 1.1f, 2.5f, .01f)] public float doorTravel = 1.85f;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach (var f in GetType().GetFields())
            {
                var a = f.GetCustomAttribute<SurfaceSettingAttribute>();
                if (a != null) yield return new SurfaceParameter("visual.factory." + f.Name, a.Group, a.Label, a.Description, a.Unit, a.Minimum, a.Maximum, a.Step, () => (float)f.GetValue(this), v => f.SetValue(this, v));
            }
        }
        public void Validate()
        {
            if (id != "factory-armored-preview-v1" || revision < 1) throw new ArgumentException("Invalid factory profile identity");
            foreach (var p in Parameters()) if (float.IsNaN(p.Read()) || float.IsInfinity(p.Read()) || p.Read() < p.Minimum || p.Read() > p.Maximum) throw new ArgumentException(p.Path);
        }
        public static FactoryModelProfile Load()
        {
            var text = Resources.Load<TextAsset>("FactoryModelProfile");
            if (!text) throw new InvalidOperationException("Missing factory preview profile");
            var p = JsonUtility.FromJson<FactoryModelProfile>(text.text); p.Validate(); return p;
        }
    }
}

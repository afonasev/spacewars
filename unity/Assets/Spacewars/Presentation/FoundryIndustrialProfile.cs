using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Spacewars.Presentation
{
    [Serializable]
    public sealed class FoundryIndustrialProfile
    {
        public string id = "foundry-industrial-landmarks-v1";
        public int revision = 1;

        [SurfaceSetting("Foundry industry", "Roof rib spacing", "Spacing of exposed steel ribs on the surviving shed roof.", "m", .35f, 1.25f, .05f)] public float ribSpacing = .95f;
        [SurfaceSetting("Foundry industry", "Broken roof gap", "Width of the missing sheet between the two roof halves.", "m", .2f, 2.5f, .1f)] public float roofGap = 1.2f;
        [SurfaceSetting("Foundry industry", "Exhaust stack height", "Height of the taller abandoned chimney above the roof line.", "m", 2f, 5f, .1f)] public float chimneyHeight = 4.2f;
        [SurfaceSetting("Foundry industry", "Crane reach", "Horizontal crane boom length within the blocked factory footprint.", "m", 2f, 7f, .1f)] public float craneReach = 5.2f;

        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach (var field in GetType().GetFields())
            {
                var setting = field.GetCustomAttribute<SurfaceSettingAttribute>();
                if (setting == null) continue;
                yield return new SurfaceParameter("visual.foundryIndustrial." + field.Name, setting.Group, setting.Label, setting.Description, setting.Unit,
                    setting.Minimum, setting.Maximum, setting.Step, () => (float)field.GetValue(this), value => field.SetValue(this, value));
            }
        }

        public void Validate()
        {
            if (id != "foundry-industrial-landmarks-v1" || revision < 1) throw new ArgumentException("Invalid Foundry industrial profile identity.");
            foreach (var setting in Parameters())
            {
                var value = setting.Read();
                if (float.IsNaN(value) || float.IsInfinity(value) || value < setting.Minimum || value > setting.Maximum)
                    throw new ArgumentException("Invalid Foundry industrial setting: " + setting.Path);
            }
        }

        public static FoundryIndustrialProfile Load()
        {
            var text = Resources.Load<TextAsset>("FoundryIndustrialProfile");
            if (!text) throw new InvalidOperationException("Missing Foundry industrial profile.");
            var profile = JsonUtility.FromJson<FoundryIndustrialProfile>(text.text);
            profile.Validate();
            return profile;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class ContactOcclusionProfile
    {
        public string id="contact-occlusion-v1";
        public int revision=2;
        [SurfaceSetting("Contact SSAO","Intensity","Contact darkness; zero disables occlusion.","ratio",0,4,.05f)] public float intensity=3f;
        [SurfaceSetting("Contact SSAO","Radius","World-space reach around existing contacts.","m",.05f,1.5f,.05f)] public float radius=1.3f;
        [SurfaceSetting("Contact SSAO","Falloff","Maximum camera distance of contact shading.","m",50,400,5)] public float falloff=200;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach(var field in GetType().GetFields())
            {var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.contactOcclusion."+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));}
        }
        public void Validate()
        {
            if(id!="contact-occlusion-v1"||revision<1)throw new ArgumentException("Invalid contact occlusion identity");
            foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid contact occlusion: "+p.Path);
        }
        public static ContactOcclusionProfile Load()
        {var asset=Resources.Load<TextAsset>("ContactOcclusionProfile");if(!asset)throw new InvalidOperationException("Missing contact occlusion profile");var p=JsonUtility.FromJson<ContactOcclusionProfile>(asset.text);NativeGraphicsSettings.Apply(p.Parameters());if(!NativeGraphicsSettings.OcclusionEnabled)p.intensity=0;p.Validate();return p;}
    }
}

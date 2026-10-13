using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class UnitWreckProfile
    {
        public string id="native-unit-wreck-v1";
        public int revision=1;
        [SurfaceSetting("Unit wrecks","Visible lifetime","Game time after the destruction flash. Existing wrecks keep their captured duration.","s",.1f,10,.1f)]
        public float lifetimeSeconds=1;
        public IEnumerable<SurfaceParameter> Parameters()
        {
            foreach(var field in GetType().GetFields())
            {var a=field.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.unitWreck."+field.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));}
        }
        public void Validate()
        {
            if(id!="native-unit-wreck-v1"||revision<1)throw new ArgumentException("Invalid unit wreck identity");
            foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException("Invalid unit wreck setting: "+p.Path);
        }
        public static UnitWreckProfile Load()
        {var p=JsonUtility.FromJson<UnitWreckProfile>(Resources.Load<TextAsset>("UnitWreckProfile").text);p.Validate();return p;}
    }
}

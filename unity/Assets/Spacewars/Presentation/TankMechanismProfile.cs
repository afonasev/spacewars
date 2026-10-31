using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class TankMechanismProfile
    {
        public string id="tank-mechanism-v1"; public int revision=1;
        [SurfaceSetting("Tank model","Candidate scale","Uniform size of newly created Spearhead models; collision footprint is unchanged.","ratio",.8f,1.3f,.01f)] public float modelScale=1.12f;
        [SurfaceSetting("Legacy tank detail","Legacy distant entry","Two-level diagnostic asset only. The active game uses the Model detail profile for all three levels.","screen height",.005f,.07f,.005f)] public float distantEnterScreenHeight=.04f;
        [SurfaceSetting("Legacy tank detail","Legacy original return","Two-level diagnostic asset only. Active game thresholds are in the Model detail profile.","screen height",.01f,.1f,.005f)] public float distantExitScreenHeight=.05f;
        [SurfaceSetting("Tank mechanics","Recoil travel","Visual barrel travel after an observed shot.","model m",0,.3f,.01f)] public float recoilTravel=.14f;
        [SurfaceSetting("Tank mechanics","Recoil recovery","Time for barrel return; simulation damage/reload are unchanged.","s",.05f,1,.01f)] public float recoilSeconds=.24f;
        [SurfaceSetting("Tank mechanics","Muzzle flash duration","Observed shot flash lifetime.","s",.02f,.2f,.01f)] public float flashSeconds=.06f;
        [SurfaceSetting("Tank mechanics","Muzzle flash size","Visual flash radius at the authored muzzle.","model m",.02f,.5f,.01f)] public float flashSize=.17f;
        [SurfaceSetting("Tank mechanics","Engine rotor speed","Rear turbine rotation while moving.","deg/m",0,1440,10)] public float rotorDegreesPerMeter=540;
        [SurfaceSetting("Tank shell","Shell length","Visible kinetic penetrator length; collision remains profile-owned.","m",.1f,.8f,.01f)] public float shellLength=.38f;
        [SurfaceSetting("Tank shell","Tracer length","Hot wake behind the moving shell.","m",.1f,1.5f,.01f)] public float tracerLength=.65f;
        [SurfaceSetting("Tank impact","Impact lifetime","Tank spark and smoke visual lifetime; no damage change.","s",.1f,1.5f,.01f)] public float impactSeconds=.30f;
        public IEnumerable<SurfaceParameter> Parameters(){foreach(var f in GetType().GetFields()){var a=f.GetCustomAttribute<SurfaceSettingAttribute>();if(a!=null)yield return new SurfaceParameter("visual.tankMechanism."+f.Name,a.Group,a.Label,a.Description,a.Unit,a.Minimum,a.Maximum,a.Step,()=> (float)f.GetValue(this),v=>f.SetValue(this,v));}}
        public void Validate(){if(id!="tank-mechanism-v1"||revision<1)throw new ArgumentException("Tank mechanism identity");foreach(var p in Parameters())if(float.IsNaN(p.Read())||float.IsInfinity(p.Read())||p.Read()<p.Minimum||p.Read()>p.Maximum)throw new ArgumentException(p.Path);if(distantEnterScreenHeight>=distantExitScreenHeight)throw new ArgumentException("Distant tank detail hysteresis");}
        public static TankMechanismProfile Load(){var p=JsonUtility.FromJson<TankMechanismProfile>(Resources.Load<TextAsset>("TankMechanismProfile").text);NativeGraphicsSettings.Apply(p.Parameters().Where(x=>x.Path=="visual.tankMechanism.modelScale"));p.Validate();return p;}
    }
}

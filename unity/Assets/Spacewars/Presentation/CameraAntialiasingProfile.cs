using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace Spacewars.Presentation
{
    [Serializable] public sealed class CameraAntialiasingProfile
    {
        public string id="camera-antialiasing-v1";
        public int revision=1;
        [SurfaceSetting("Camera AA","Mode","0 Off; 1 MSAA 4x; 2 SMAA High. Candidate selection; no temporal history.","mode",0,2,1)] public float mode=0;
        [SurfaceSetting("Scene resolution","Render Scale","Scene resolution multiplier. Lower values reduce GPU pixel work and image sharpness; UI stays at display resolution.","scale",.5f,1f,.05f)] public float renderScale=1;
        public IEnumerable<SurfaceParameter> Parameters()
        {foreach(var name in new[]{nameof(mode),nameof(renderScale)}){var field=GetType().GetField(name);var m=field.GetCustomAttribute<SurfaceSettingAttribute>();yield return new SurfaceParameter("visual.cameraAntialiasing."+name,m.Group,m.Label,m.Description,m.Unit,m.Minimum,m.Maximum,m.Step,()=> (float)field.GetValue(this),v=>field.SetValue(this,v));}}
        public void Validate()
        {
            if(id!="camera-antialiasing-v1"||revision<1)throw new ArgumentException("Invalid camera AA identity");
            foreach(var parameter in Parameters()){float value=parameter.Read();if(float.IsNaN(value)||float.IsInfinity(value)||value<parameter.Minimum||value>parameter.Maximum||Mathf.Abs(value/parameter.Step-Mathf.Round(value/parameter.Step))>.001f)throw new ArgumentException("Invalid camera AA: "+parameter.Path);}
        }
        // Quality values define candidate identities rather than independently tuneable controls.
        public int Samples=>mode==1?4:1;
        public static CameraAntialiasingProfile Load(){var a=Resources.Load<TextAsset>("CameraAntialiasingProfile");if(!a)throw new InvalidOperationException("Missing camera AA profile");var p=JsonUtility.FromJson<CameraAntialiasingProfile>(a.text);NativeGraphicsSettings.Apply(p.Parameters());p.Validate();return p;}
    }
}

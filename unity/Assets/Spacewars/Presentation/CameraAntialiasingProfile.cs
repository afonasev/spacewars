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
        public IEnumerable<SurfaceParameter> Parameters()
        {var metadata=GetType().GetField(nameof(mode)).GetCustomAttribute<SurfaceSettingAttribute>();yield return new SurfaceParameter("visual.cameraAntialiasing.mode",metadata.Group,metadata.Label,metadata.Description,metadata.Unit,metadata.Minimum,metadata.Maximum,metadata.Step,()=>mode,v=>mode=v);}
        public void Validate()
        {
            if(id!="camera-antialiasing-v1"||revision<1)throw new ArgumentException("Invalid camera AA identity");
            foreach(var parameter in Parameters()){float value=parameter.Read();if(float.IsNaN(value)||float.IsInfinity(value)||value<parameter.Minimum||value>parameter.Maximum||value!=Mathf.Round(value/parameter.Step)*parameter.Step)throw new ArgumentException("Invalid camera AA: "+parameter.Path);}
        }
        // Quality values define candidate identities rather than independently tuneable controls.
        public int Samples=>mode==1?4:1;
        public static CameraAntialiasingProfile Load(){var a=Resources.Load<TextAsset>("CameraAntialiasingProfile");if(!a)throw new InvalidOperationException("Missing camera AA profile");var p=JsonUtility.FromJson<CameraAntialiasingProfile>(a.text);NativeGraphicsSettings.Apply(p.Parameters());p.Validate();return p;}
    }
}

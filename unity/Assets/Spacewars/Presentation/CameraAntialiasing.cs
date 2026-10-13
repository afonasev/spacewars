using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace Spacewars.Presentation
{
    [RequireComponent(typeof(Camera))] public sealed class CameraAntialiasing:MonoBehaviour
    {
        private Camera view;private UniversalAdditionalCameraData data;private bool configured,previousMsaa;
        private AntialiasingMode previousMode;private AntialiasingQuality previousQuality;
        public CameraAntialiasingProfile Profile {get;private set;}
        public void Configure(CameraAntialiasingProfile profile)
        {
            profile.Validate();Restore();view=GetComponent<Camera>();data=view.GetUniversalAdditionalCameraData();
            if(profile.mode==1 && UniversalRenderPipeline.asset.msaaSampleCount<4)throw new InvalidOperationException("MSAA4 requires pipeline capability 4x");
            if(view.targetTexture && view.targetTexture.antiAliasing!=profile.Samples)throw new InvalidOperationException("AA target sample count does not match profile");
            previousMsaa=view.allowMSAA;previousMode=data.antialiasing;previousQuality=data.antialiasingQuality;
            Profile=profile;configured=true;if(isActiveAndEnabled)Apply();
        }
        private void Apply(){if(!configured)return;view.allowMSAA=Profile.mode==1;data.antialiasing=Profile.mode==2?AntialiasingMode.SubpixelMorphologicalAntiAliasing:AntialiasingMode.None;data.antialiasingQuality=AntialiasingQuality.High;}
        private void OnEnable()=>Apply();
        private void Restore(){if(!configured)return;view.allowMSAA=previousMsaa;data.antialiasing=previousMode;data.antialiasingQuality=previousQuality;configured=false;}
        private void OnDisable(){if(!configured)return;view.allowMSAA=previousMsaa;data.antialiasing=previousMode;data.antialiasingQuality=previousQuality;}
        private void OnDestroy(){Restore();CameraAntialiasingDiagnostics.Forget(view);}
    }
}

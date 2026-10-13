using UnityEngine;
namespace Spacewars.Presentation
{
    [RequireComponent(typeof(Camera))] public sealed class NativeGraphicsBinding:MonoBehaviour
    {
        private PlayableWorld world;private string mapId;private Light sun;private Camera view;private bool configured;
        public void Configure(PlayableWorld value,string map,Light light){world=value;mapId=map;sun=light;view=GetComponent<Camera>();configured=true;}
        private void OnEnable()=>NativeGraphicsSettings.Changed+=Refresh;
        private void OnDisable()=>NativeGraphicsSettings.Changed-=Refresh;
        private void Refresh(string path)
        {
            if(!configured)return;
            if(path=="*"||path.StartsWith("visual.cameraAntialiasing."))
            {
                var p=CameraAntialiasingProfile.Load();var target=view.targetTexture;
                // Quality changes require a matching owned render target; dimensions/format remain intact.
                if(target&&target.antiAliasing!=p.Samples){target.Release();target.antiAliasing=p.Samples;target.Create();}
                view.GetComponent<CameraAntialiasing>().Configure(p);
            }
            if(path=="*"||path.StartsWith("visual.contactOcclusion."))view.GetComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());
            if(path=="*"||path.StartsWith("visual.actorFinish."))
            {var p=ActorMaterialProfile.Load();var glow=view.GetComponent<ActorGlowCamera>();glow.Configure(p,glow.VolumeLayer);world.ApplyGraphics(p,IndustrialLightingProfile.Load(mapId));}
            if(path=="*"||path.StartsWith(mapId=="three-crossings-greybox-v1"?"visual.threeCrossings.lighting.":"visual.lighting."))
            {var p=IndustrialLightingProfile.Load(mapId);if(sun)p.ApplyScene(sun);view.GetComponent<VisibleShadowCoverage>().Configure(world.GraphicsRoot,p);world.ApplyGraphics(ActorMaterialProfile.Load(),p);}
        }
    }
}

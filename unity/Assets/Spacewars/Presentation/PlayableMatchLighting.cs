using UnityEngine;
using System.Linq;
namespace Spacewars.Presentation
{
    public sealed partial class PlayableBootstrap
    {
        private IndustrialLightingProfile selectedMatchLighting;
        private IndustrialLightingProfile ActiveLighting=>localCoordinator!=null?localCoordinator.ActiveLighting:selectedMatchLighting??IndustrialLightingProfile.Load(profile.AuthoredMap?.Id);
        private void BindMatchLighting(bool newMatch=false){
            if(localCoordinator==null&&newMatch)
                selectedMatchLighting=WhiteMeridianLightingVariants.Supports(profile.AuthoredMap?.Id)?WhiteMeridianLightingVariants.Load().Select(runtime.MatchSeed).lighting:null;
            if(world==null||cameraView==null)return;
            var light=ActiveLighting;
            var sun=GetComponentsInChildren<Light>().FirstOrDefault(l=>l.type==LightType.Directional);if(localCoordinator==null&&sun)light.ApplyScene(sun);
            cameraView.GetComponent<VisibleShadowCoverage>()?.Configure(world.GraphicsRoot,light);
            cameraView.GetComponent<NativeGraphicsBinding>()?.Configure(world,profile.AuthoredMap?.Id,sun,WhiteMeridianLightingVariants.Supports(profile.AuthoredMap?.Id)?light:null);
            world.ApplyGraphics(ActorMaterialProfile.Load(),light);
        }
    }
}

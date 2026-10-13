using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace Spacewars.Presentation
{
    [RequireComponent(typeof(Camera))] public sealed class ActorGlowCamera:MonoBehaviour
    {
        // Dedicated volume-only layers avoid importing another local camera's overrides.
        public const int NormalVolumeLayer=29,FirstLocalVolumeLayer=30;
        private Camera view;private UniversalAdditionalCameraData data;
        private Volume volume;private VolumeProfile ownedProfile;
        private bool configured,previousPost,previousHdr;private LayerMask previousMask;
        public VolumeProfile Profile=>ownedProfile;
        public int VolumeLayer=>volume?volume.gameObject.layer:-1;
        public void Configure(ActorMaterialProfile profile,int layer=NormalVolumeLayer)
        {
            profile.Validate();Release();view=GetComponent<Camera>();data=view.GetUniversalAdditionalCameraData();
            previousPost=data.renderPostProcessing;previousMask=data.volumeLayerMask;previousHdr=view.allowHDR;
            var host=new GameObject("Actor emission bloom");host.transform.SetParent(transform,false);host.layer=layer;
            volume=host.AddComponent<Volume>();volume.isGlobal=true;
            ownedProfile=ScriptableObject.CreateInstance<VolumeProfile>();ownedProfile.name=profile.id+"/"+profile.variant+"@"+profile.revision;
            var bloom=ownedProfile.Add<Bloom>(true);bloom.intensity.Override(profile.bloomIntensity);bloom.threshold.Override(profile.bloomThreshold);bloom.scatter.Override(profile.bloomScatter);
            // Keep accepted lighting: the project default volume has ACES enabled.
            ownedProfile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
            volume.sharedProfile=ownedProfile;configured=true;if(isActiveAndEnabled)ApplyEnabled();
        }
        private void ApplyEnabled(){if(!configured)return;data.renderPostProcessing=true;data.volumeLayerMask=1<<volume.gameObject.layer;view.allowHDR=true;volume.enabled=true;}
        private void OnEnable()=>ApplyEnabled();
        private void OnDisable(){if(!configured)return;data.renderPostProcessing=previousPost;data.volumeLayerMask=previousMask;view.allowHDR=previousHdr;volume.enabled=false;}
        private void Release()
        {
            OnDisable();configured=false;
            if(volume){volume.sharedProfile=null;Destroy(volume.gameObject);volume=null;}
            if(ownedProfile){foreach(var component in ownedProfile.components)if(component)Destroy(component);ownedProfile.components.Clear();Destroy(ownedProfile);ownedProfile=null;}
        }
        private void OnDestroy()=>Release();
    }
}

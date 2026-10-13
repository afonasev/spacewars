using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace Spacewars.Presentation
{
    // URP 17.3 owns sampling/blur/render graph lifetime. Its settings are internal;
    // use Unity's serialized adapter, covered by an installed-package contract test.
    public sealed class ContactOcclusionFeature:ScreenSpaceAmbientOcclusion
    {
        [Serializable] private sealed class Settings
        {
            // Fixed quality invariants: full-res, depth reconstructed normals, deterministic
            // interleaved noise, 12 samples, bilateral blur, before opaque lighting/bloom.
            public int AOMethod=1,Source=0,NormalSamples=2,Samples=0,BlurQuality=0,SampleCount=-1;
            public bool Downsample=false,AfterOpaque=false;
            public float Intensity,Radius,Falloff;
            // Preserve direct warm light; only ambient illumination is occluded.
            public float DirectLightingStrength=0;
        }
        [Serializable] private sealed class Adapter {public Settings m_Settings;}
        private float intensity=-1,radius=-1,falloff=-1;
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
        {
            var camera=renderingData.cameraData.camera;
            if(!camera.TryGetComponent<ContactOcclusionCamera>(out var control)||!control.isActiveAndEnabled||control.Profile==null)return;
            var p=control.Profile;
            if(intensity!=p.intensity||radius!=p.radius||falloff!=p.falloff)
            {
                p.Validate();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(new Adapter{m_Settings=new Settings{Intensity=p.intensity,Radius=p.radius,Falloff=p.falloff}}),this);
                intensity=p.intensity;radius=p.radius;falloff=p.falloff;
            }
            if(p.intensity<=0)return;
            base.AddRenderPasses(renderer,ref renderingData);
        }
    }
}

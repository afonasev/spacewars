using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace Spacewars.Presentation
{
    // Observes effective URP descriptors without adding a render pass or changing pass order.
    public sealed class CameraAntialiasingDiagnostics:ScriptableRendererFeature
    {
        private static readonly Dictionary<Camera,int> samples=new Dictionary<Camera,int>();
        public static int Samples(Camera camera)=>samples.TryGetValue(camera,out var n)?n:0;
        public static void Forget(Camera camera){if(!ReferenceEquals(camera,null))samples.Remove(camera);}
        public override void Create(){}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {if(data.cameraData.camera.TryGetComponent<CameraAntialiasing>(out var control)&&control.isActiveAndEnabled)samples[data.cameraData.camera]=data.cameraData.cameraTargetDescriptor.msaaSamples;}
        protected override void Dispose(bool disposing)=>samples.Clear();
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace Spacewars.Presentation
{
    // Records effective URP descriptors and schedules the optional per-camera terrain cache.
    public sealed class CameraAntialiasingDiagnostics:ScriptableRendererFeature
    {
        private static readonly Dictionary<Camera,int> samples=new Dictionary<Camera,int>();
        private static readonly Dictionary<Camera,Vector2Int> targetSizes=new Dictionary<Camera,Vector2Int>();
        public static Vector2Int TargetSize(Camera camera)=>targetSizes.TryGetValue(camera,out var size)?size:Vector2Int.zero;
        public static int Samples(Camera camera)=>samples.TryGetValue(camera,out var n)?n:0;
        public static void Forget(Camera camera){if(!ReferenceEquals(camera,null)){samples.Remove(camera);targetSizes.Remove(camera);}}
        private TerrainMaterialCachePass terrainCache, terrainCacheReset;
        public override void Create()
        {
            terrainCache = new TerrainMaterialCachePass();
            terrainCacheReset = new TerrainMaterialCachePass(reset: true);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            var camera = data.cameraData.camera;
            var descriptor = data.cameraData.cameraTargetDescriptor;
            if (camera.TryGetComponent<CameraAntialiasing>(out var control) && control.isActiveAndEnabled)
            {
                samples[camera] = descriptor.msaaSamples;
                targetSizes[camera] = new Vector2Int(descriptor.width, descriptor.height);
            }
            if (camera.TryGetComponent<TerrainMaterialCacheCamera>(out var cache) && cache.Supports(data.cameraData))
            {
                renderer.EnqueuePass(terrainCache);
                renderer.EnqueuePass(terrainCacheReset);
            }
        }
        protected override void Dispose(bool disposing){samples.Clear();targetSizes.Clear();}
    }
}

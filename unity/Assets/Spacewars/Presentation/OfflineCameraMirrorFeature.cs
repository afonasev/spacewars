using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Presentation
{
    // Only explicitly registered offline cameras use the source handedness adapter.
    // Mirror color after world rendering: geometry/shadows keep normal Unity culling.
    public sealed class OfflineCameraMirrorFeature : ScriptableRendererFeature
    {
        private static readonly HashSet<Camera> cameras=new HashSet<Camera>();
        public static void Register(Camera camera)=>cameras.Add(camera);
        public static void Unregister(Camera camera)=>cameras.Remove(camera);
        public static bool IsRegistered(Camera camera)=>camera!=null&&cameras.Contains(camera);
        private Material material;private MirrorPass pass;
        public override void Create(){pass=new MirrorPass{renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing};}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
        {
            if(!IsRegistered(renderingData.cameraData.camera))return;
            if(material==null){var shader=Resources.Load<Shader>("OfflineCameraMirror");if(shader==null){Debug.LogError("Offline source camera mirror shader missing.");return;}material=CoreUtils.CreateEngineMaterial(shader);}
            pass.Setup(material);renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing){CoreUtils.Destroy(material);material=null;}
        private sealed class MirrorPass : ScriptableRenderPass
        {
            private Material material;
            public void Setup(Material value){material=value;requiresIntermediateTexture=true;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            {
                var data=frameData.Get<UniversalResourceData>();if(data.isActiveTargetBackBuffer){Debug.LogError("Offline source mirror requires per-camera intermediate color.");return;}
                var source=data.activeColorTexture;var desc=graph.GetTextureDesc(source);desc.name="OfflineSourceWorldColor";desc.clearBuffer=false;
                var target=graph.CreateTexture(desc);graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source,target,material,0),passName:"Offline source X reflection");data.cameraColor=target;
            }
        }
    }
}

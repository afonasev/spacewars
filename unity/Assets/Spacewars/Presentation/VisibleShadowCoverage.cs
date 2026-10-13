using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Presentation
{
    // Coverage belongs to each camera, rather than the scene's fixed quality distance.
    [DisallowMultipleComponent]
    public sealed class VisibleShadowCoverage : MonoBehaviour
    {
        private Camera view;
        private Transform scene;
        private IndustrialLightingProfile profile;
        private readonly List<Renderer> renderers=new List<Renderer>();
        // A camera frustum always has six clipping planes; this is a geometric invariant.
        private readonly Plane[] planes=new Plane[6];
        private UniversalRenderPipelineAsset activePipeline;
        private float previousDistance;
        public float LastRequiredDistance{get;private set;}
        public void Configure(Transform sceneRoot,IndustrialLightingProfile settings)
        {settings.Validate();scene=sceneRoot;profile=settings;view=GetComponent<Camera>();}
        private void OnEnable()
        {RenderPipelineManager.beginCameraRendering+=Begin;RenderPipelineManager.endCameraRendering+=End;}
        private void OnDisable()
        {RenderPipelineManager.beginCameraRendering-=Begin;RenderPipelineManager.endCameraRendering-=End;Restore();}
        public static float RequiredDistance(Camera camera,IReadOnlyList<Renderer> objects,float minimum,float padding,Plane[] clippingPlanes)
        {
            GeometryUtility.CalculateFrustumPlanes(camera,clippingPlanes);
            float farthest=0;var forward=camera.transform.forward;
            for(int i=0;i<objects.Count;i++)
            {
                var r=objects[i];
                if(!r||!r.enabled||!r.gameObject.activeInHierarchy||(camera.cullingMask&(1<<r.gameObject.layer))==0)continue;
                // Rings, HUD projections and transparent weapon visuals have no solid shadow role.
                if(r.shadowCastingMode==ShadowCastingMode.Off&&!r.receiveShadows)continue;
                var bounds=r.bounds;if(!GeometryUtility.TestPlanesAABB(clippingPlanes,bounds))continue;
                var e=bounds.extents;
                float radius=Mathf.Abs(forward.x)*e.x+Mathf.Abs(forward.y)*e.y+Mathf.Abs(forward.z)*e.z;
                float depth=Vector3.Dot(bounds.center-camera.transform.position,forward)+radius;
                farthest=Mathf.Max(farthest,depth);
            }
            return Mathf.Min(camera.farClipPlane,Mathf.Max(minimum,farthest*(1+padding)));
        }
        private void Begin(ScriptableRenderContext context,Camera camera)
        {
            if(camera!=view||!scene||profile==null)return;
            Restore();
            if(!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline))return;
            scene.GetComponentsInChildren(false,renderers);
            LastRequiredDistance=RequiredDistance(camera,renderers,profile.shadowDistance,profile.shadowCoveragePadding,planes);
            activePipeline=pipeline;previousDistance=pipeline.shadowDistance;pipeline.shadowDistance=LastRequiredDistance;
        }
        private void End(ScriptableRenderContext context,Camera camera){if(camera==view)Restore();}
        private void Restore(){if(activePipeline){activePipeline.shadowDistance=previousDistance;activePipeline=null;}}
    }
}

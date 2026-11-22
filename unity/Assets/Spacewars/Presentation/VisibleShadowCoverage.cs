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
        private bool followsDisplayShadows;
        private readonly List<Renderer> renderers=new List<Renderer>();
        // A camera frustum always has six clipping planes; this is a geometric invariant.
        private readonly Plane[] planes=new Plane[6];
        private UniversalRenderPipelineAsset activePipeline;
        private float previousDistance;
        public int LastRendererCount{get;private set;}
        public double LastCoverageMilliseconds{get;private set;}
        public float LastRequiredDistance{get;private set;}
        public void Configure(Transform sceneRoot,IndustrialLightingProfile settings)
        {settings.Validate();scene=sceneRoot;profile=settings;view=GetComponent<Camera>();}
        internal void FollowDisplayShadowSetting() => followsDisplayShadows = true;
        private void OnEnable()
        {RenderPipelineManager.beginCameraRendering+=Begin;RenderPipelineManager.endCameraRendering+=End;}
        private void OnDisable()
        {RenderPipelineManager.beginCameraRendering-=Begin;RenderPipelineManager.endCameraRendering-=End;Restore();}
        public static float RequiredDistance(Camera camera,IReadOnlyList<Renderer> objects,float minimum,float padding,Plane[] clippingPlanes)
        {
            GeometryUtility.CalculateFrustumPlanes(camera,clippingPlanes);
            var cameraTransform = camera.transform;
            var forward = cameraTransform.forward;
            var absoluteForward = new Vector3(Mathf.Abs(forward.x), Mathf.Abs(forward.y), Mathf.Abs(forward.z));
            var position = cameraTransform.position;
            int mask = camera.cullingMask;
            float reserve = 1 + padding;
            bool canPrune = reserve >= 1 && !float.IsInfinity(reserve) && minimum > 0;
            float farthest = 0;
            for (int i = 0; i < objects.Count; i++)
            {
                var renderer = objects[i];
                if (!renderer || !renderer.enabled) continue;
                var node = renderer.gameObject;
                if (!node.activeInHierarchy || (mask & (1 << node.layer)) == 0) continue;
                // Rings, HUD projections and transparent weapon visuals have no solid shadow role.
                if (renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows) continue;
                var bounds = renderer.bounds;
                var extents = bounds.extents;
                float radius = absoluteForward.x * extents.x + absoluteForward.y * extents.y + absoluteForward.z * extents.z;
                float depth = Vector3.Dot(bounds.center - position, forward) + radius;
                // Bounds inside the minimum or the visible maximum cannot extend the shadow distance.
                if (canPrune && !float.IsNaN(farthest) && (depth <= farthest || depth * reserve <= minimum)) continue;
                if (GeometryUtility.TestPlanesAABB(clippingPlanes, bounds)) farthest = Mathf.Max(farthest, depth);
            }
            return Mathf.Min(camera.farClipPlane, Mathf.Max(minimum, farthest * reserve));
        }
        // The native active-only gather already filters inactive descendants.
        private static float RequiredDistanceGathered(Camera camera, List<Renderer> objects, float minimum,
            float padding, Plane[] clippingPlanes)
        {
            var cameraTransform = camera.transform;
            var forward = cameraTransform.forward;
            var absoluteForward = new Vector3(Mathf.Abs(forward.x), Mathf.Abs(forward.y), Mathf.Abs(forward.z));
            var position = cameraTransform.position;
            int mask = camera.cullingMask;
            float reserve = 1 + padding;
            bool canPrune = reserve >= 1 && !float.IsInfinity(reserve) && minimum > 0;
            bool planesReady = false;
            float farthest = 0;
            for (int i = 0; i < objects.Count; i++)
            {
                var renderer = objects[i];
                if (!renderer || !renderer.enabled) continue;
                var bounds = renderer.bounds;
                var extents = bounds.extents;
                float radius = absoluteForward.x * extents.x + absoluteForward.y * extents.y + absoluteForward.z * extents.z;
                float depth = Vector3.Dot(bounds.center - position, forward) + radius;
                if (canPrune && !float.IsNaN(farthest) && (depth <= farthest || depth * reserve <= minimum)) continue;
                if ((mask & (1 << renderer.gameObject.layer)) == 0) continue;
                if (renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows) continue;
                if (!planesReady)
                {
                    GeometryUtility.CalculateFrustumPlanes(camera, clippingPlanes);
                    planesReady = true;
                }
                if (GeometryUtility.TestPlanesAABB(clippingPlanes, bounds)) farthest = Mathf.Max(farthest, depth);
            }
            return Mathf.Min(camera.farClipPlane, Mathf.Max(minimum, farthest * reserve));
        }
        private void Begin(ScriptableRenderContext context,Camera camera)
        {
            if(camera!=view||!scene||profile==null)return;
            Restore();
            if (followsDisplayShadows && !NativeDisplaySettings.Shadows)
            {
                LastRendererCount = 0;
                LastCoverageMilliseconds = 0;
                LastRequiredDistance = 0;
                return;
            }
            if(!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline))return;
            long stamp=Spacewars.Simulation.PerformanceProfile.Enabled?System.Diagnostics.Stopwatch.GetTimestamp():0;
            scene.GetComponentsInChildren(false,renderers);
            LastRendererCount=renderers.Count;
            // GetComponentsInChildren includes the root even when the root is inactive.
            LastRequiredDistance = scene.gameObject.activeInHierarchy
                ? RequiredDistanceGathered(camera, renderers, profile.shadowDistance, profile.shadowCoveragePadding, planes)
                : RequiredDistance(camera, renderers, profile.shadowDistance, profile.shadowCoveragePadding, planes);
            if(stamp!=0)LastCoverageMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-stamp)*1000d/System.Diagnostics.Stopwatch.Frequency;
            activePipeline=pipeline;previousDistance=pipeline.shadowDistance;pipeline.shadowDistance=LastRequiredDistance;
        }
        private void End(ScriptableRenderContext context,Camera camera){if(camera==view)Restore();}
        private void Restore(){if(activePipeline){activePipeline.shadowDistance=previousDistance;activePipeline=null;}}
    }
}

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Presentation
{
    // Reuse terrain material inputs while the split-screen viewport is stationary.
    // The forward pass continues to calculate live lighting, shadows and fog.
    public sealed class TerrainMaterialCacheCamera : MonoBehaviour
    {
        private readonly RTHandle[] textures = new RTHandle[3];
        private PlayableWorld world;
        private Camera camera;
        private Renderer[] terrain, yard;
        private Material material, yardMaterial;
        private Matrix4x4 view, projection;
        private Rect viewport;
        private int width, height, layerMask, revision, changedFrame;
        private bool valid, yardBound;
        private static int lastFillFrame = -1;

#if UNITY_EDITOR
        public int Fills { get; private set; }
        public int Reuses { get; private set; }
        internal bool CompareFullVariant { get; set; }
        internal bool ForceStock { get; set; }
        internal bool ShowCoverage { get; set; }
        internal bool ShowTerrainRoles { get; set; }
#endif

        internal static bool DeviceSupportsCache =>
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal &&
            SystemInfo.graphicsShaderLevel >= 45 &&
            SystemInfo.IsFormatSupported(TerrainMaterialCachePass.CacheFormat,
                GraphicsFormatUsage.Render | GraphicsFormatUsage.MSAA4x);

        public void Configure(PlayableWorld source)
        {
            if (ReferenceEquals(world, source)) return;
            BindYardMaterials(false);
            ReleaseTextures();
            world = source;
            camera = GetComponent<Camera>();
            material = source.TerrainCacheMaterial;
            yardMaterial = source.PrepareTerrainCacheYardMaterial(out yard);
            terrain = source.GraphicsRoot.GetComponentsInChildren<Renderer>();
            changedFrame = Time.frameCount;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endCameraRendering += AfterCamera;
        }

        private void BeforeCamera(ScriptableRenderContext context, Camera rendering)
        {
#if UNITY_EDITOR
            if (ForceStock) return;
#endif
            if (rendering == camera && valid && camera.allowMSAA &&
                camera.orthographic && MatchesViewport(camera.pixelWidth, camera.pixelHeight))
                BindYardMaterials(true);
        }

        private void AfterCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (rendering == camera) BindYardMaterials(false);
        }

        private void BindYardMaterials(bool bind)
        {
            if (yard == null || yardBound == bind) return;
            yardBound = bind;
            var selected = bind ? yardMaterial : material;
            foreach (var renderer in yard)
                if (renderer && renderer.sharedMaterial != selected)
                    renderer.sharedMaterial = selected;
        }

        private bool MatchesViewport(int targetWidth, int targetHeight) =>
            view.Equals(camera.worldToCameraMatrix) && projection.Equals(camera.projectionMatrix) &&
            viewport.Equals(camera.pixelRect) && width == targetWidth && height == targetHeight &&
            layerMask == camera.cullingMask && revision == world.TerrainCacheRevision;

        internal bool Supports(CameraData data)
        {
            var target = data.cameraTargetDescriptor;
            return world != null && material && isActiveAndEnabled && camera.orthographic &&
                data.renderType == CameraRenderType.Base && data.resolveFinalTarget && !data.xrRendering &&
                data.renderScale == 1f && target.msaaSamples == 4 && target.dimension == TextureDimension.Tex2D &&
                target.width == camera.pixelWidth && target.height == camera.pixelHeight && !target.useDynamicScale &&
                !GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode;
        }

        internal bool Prepare(int targetWidth, int targetHeight, out bool fill)
        {
            fill = false;
            int frame = Time.frameCount;
            if (!MatchesViewport(targetWidth, targetHeight))
            {
                if (width != targetWidth || height != targetHeight) ReleaseTextures();
                view = camera.worldToCameraMatrix;
                projection = camera.projectionMatrix;
                viewport = camera.pixelRect;
                width = targetWidth;
                height = targetHeight;
                layerMask = camera.cullingMask;
                revision = world.TerrainCacheRevision;
                changedFrame = frame;
                valid = false;
            }

            if (valid)
            {
#if UNITY_EDITOR
                Reuses++;
#endif
                return true;
            }

            // Spread cold renders across frames and wait for the camera to settle.
            if (frame - changedFrame < 2 || lastFillFrame == frame) return false;
            lastFillFrame = frame;
            for (int i = 0; i < textures.Length; i++)
                textures[i] ??= RTHandles.Alloc(width, height, colorFormat: TerrainMaterialCachePass.CacheFormat,
                    name: "Terrain cache " + i);
            fill = true;
            valid = true;
#if UNITY_EDITOR
            Fills++;
#endif
            return false;
        }

        internal RTHandle Texture(int index) => textures[index];

        internal void Draw(RasterCommandBuffer cmd, Plane[] planes)
        {
            foreach (var renderer in terrain)
                if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    (layerMask & (1 << renderer.gameObject.layer)) != 0 &&
                    (renderer.sharedMaterial == material || renderer.sharedMaterial == yardMaterial) &&
                    GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                    cmd.DrawRenderer(renderer, material, 0, 0);
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
            BindYardMaterials(false);
            ReleaseTextures();
        }

        private void ReleaseTextures()
        {
            valid = false;
            for (int i = 0; i < textures.Length; i++)
            {
                textures[i]?.Release();
                textures[i] = null;
            }
        }
    }

    internal sealed class TerrainMaterialCachePass : ScriptableRenderPass
    {
        internal const GraphicsFormat CacheFormat = GraphicsFormat.R32G32B32A32_SFloat;
        private static readonly GlobalKeyword ReadCache = GlobalKeyword.Create("SPACEWARS_QA_TERRAIN_CACHE_READ");
        private static readonly GlobalKeyword Key = GlobalKeyword.Create("SPACEWARS_QA_TERRAIN_CACHE_KEY");
        private static readonly GlobalKeyword Coverage = GlobalKeyword.Create("SPACEWARS_QA_TERRAIN_CACHE_COVERAGE");
        private static readonly int Mode = Shader.PropertyToID("_TerrainCacheMode");
        private static readonly int[] TextureIds = {
            Shader.PropertyToID("_TerrainCacheSurface"), Shader.PropertyToID("_TerrainCacheNormal"),
            Shader.PropertyToID("_TerrainCachePosition"),
        };
        private readonly bool reset;

        internal TerrainMaterialCachePass(bool reset = false)
        {
            this.reset = reset;
            renderPassEvent = reset ? RenderPassEvent.AfterRenderingOpaques :
                (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingOpaques - 1);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            if (reset)
            {
                SetMode(graph, 0, null);
                return;
            }

            var data = frameData.Get<UniversalCameraData>();
            var target = data.cameraTargetDescriptor;
            var state = data.camera.GetComponent<TerrainMaterialCacheCamera>();
#if UNITY_EDITOR
            if (state.ForceStock) return;
#endif
            bool reuse = state.Prepare(target.width, target.height, out bool fill);
            if (fill)
            {
                var planes = GeometryUtility.CalculateFrustumPlanes(data.camera);
                for (int i = 0; i < TextureIds.Length; i++)
                    RecordFill(graph, state, target.width, target.height, planes, i);
            }
            if (reuse)
            {
                int mode = 4;
#if UNITY_EDITOR
                if (state.ShowTerrainRoles) mode = 7;
                else if (state.CompareFullVariant) mode = 6;
#endif
                SetMode(graph, mode, state);
            }
        }

        private sealed class FillData
        {
            public TerrainMaterialCacheCamera State;
            public Plane[] Planes;
            public int Mode;
            public Rect Viewport;
        }

        private static void RecordFill(RenderGraph graph, TerrainMaterialCacheCamera state,
            int width, int height, Plane[] planes, int index)
        {
            var desc = new TextureDesc(width, height)
            {
                name = "Terrain material capture",
                colorFormat = CacheFormat,
                msaaSamples = MSAASamples.MSAA4x,
                clearBuffer = true,
                clearColor = Color.clear,
            };
            var color = graph.CreateTexture(desc);
            var depth = graph.CreateTexture(new TextureDesc(width, height)
            {
                name = "Terrain material capture depth",
                depthBufferBits = DepthBits.Depth32,
                msaaSamples = MSAASamples.MSAA4x,
                clearBuffer = true,
            });
            using (var builder = graph.AddRasterRenderPass<FillData>("Capture terrain material", out var pass))
            {
                pass.State = state;
                pass.Planes = planes;
                pass.Mode = index + 1;
                pass.Viewport = new Rect(0, 0, width, height);
                builder.SetRenderAttachment(color, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
                // The capture variant also contains the regular lighting path.
                if (index < 2) builder.UseAllGlobalTextures(true);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (FillData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    cmd.SetViewport(data.Viewport);
                    var keyword = data.Mode == 3 ? Key : ReadCache;
                    cmd.EnableKeyword(keyword);
                    cmd.SetGlobalInt(Mode, data.Mode);
                    data.State.Draw(cmd, data.Planes);
                    cmd.SetGlobalInt(Mode, 0);
                    cmd.DisableKeyword(keyword);
                });
            }
            graph.AddBlitPass(color, graph.ImportTexture(state.Texture(index)), Vector2.one, Vector2.zero,
                filterMode: RenderGraphUtils.BlitFilterMode.ClampNearest, passName: "Resolve terrain cache");
        }

        private sealed class ModeData
        {
            public int Mode;
            public bool Coverage;
        }

        private static void SetMode(RenderGraph graph, int mode, TerrainMaterialCacheCamera state)
        {
            using var builder = graph.AddRasterRenderPass<ModeData>("Bind terrain cache", out var pass);
            pass.Mode = mode;
#if UNITY_EDITOR
            pass.Coverage = state && (state.ShowCoverage || state.ShowTerrainRoles);
#endif
            if (state)
                for (int i = 0; i < TextureIds.Length; i++)
                    builder.SetGlobalTextureAfterPass(graph.ImportTexture(state.Texture(i)), TextureIds[i]);
            builder.AllowGlobalStateModification(true);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (ModeData data, RasterGraphContext context) =>
            {
                context.cmd.SetGlobalInt(Mode, data.Mode);
                if (data.Mode == 0)
                {
                    context.cmd.DisableKeyword(ReadCache);
                    context.cmd.DisableKeyword(Coverage);
                }
                else
                    context.cmd.EnableKeyword(data.Coverage ? Coverage : ReadCache);
            });
        }
    }
}

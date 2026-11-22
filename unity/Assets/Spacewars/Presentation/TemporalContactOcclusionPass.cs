using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Spacewars.Presentation
{
    // Preserve the full-quality forward estimate on pixels unaffected by depth changes.
    internal sealed class TemporalContactOcclusionPass : ScriptableRenderPass, IDisposable
    {
        private const int Tile = 16;
        private const string Temporal = "SPACEWARS_SSAO_TEMPORAL";
        private const string OcclusionKeyword = "_SCREEN_SPACE_OCCLUSION";
        private const string OcclusionTexture = "_ScreenSpaceOcclusionTexture";
        private static readonly int OcclusionTextureId = Shader.PropertyToID(OcclusionTexture);
        private static readonly Action<UniversalResourceData, TextureHandle> registerOcclusion = BindOcclusionResource();

        private static Action<UniversalResourceData, TextureHandle> BindOcclusionResource()
        {
            // URP's opaque pass declares the SSAO read through this per-camera
            // resource, in addition to sampling the shader global.
            var setter = typeof(UniversalResourceData)
                .GetProperty(nameof(UniversalResourceData.ssaoTexture), BindingFlags.Instance | BindingFlags.Public)
                ?.GetSetMethod(true);
            if (setter == null) return null;
            try
            {
                return (Action<UniversalResourceData, TextureHandle>)setter.CreateDelegate(
                    typeof(Action<UniversalResourceData, TextureHandle>));
            }
            catch (ArgumentException) { return null; }
            catch (MemberAccessException) { return null; }
        }

        private sealed class History : IDisposable
        {
            internal ContactOcclusionCamera Owner;
            internal Material Material;
            internal RTHandle Depth, Raw;
            internal RenderTextureDescriptor Descriptor;
            internal Matrix4x4 View, Projection;
            internal Vector4 Parameters;
            internal float Far;
            internal int Halo;
            internal bool Valid;
            internal bool Reused;
            internal int RecordedFrames;
            internal readonly Matrix4x4[] ViewProjection = new Matrix4x4[2];
            internal readonly Vector4[] TopLeft = new Vector4[2], X = new Vector4[2],
                Y = new Vector4[2], Z = new Vector4[2];

            public void Dispose()
            {
                Depth?.Release();
                Raw?.Release();
                CoreUtils.Destroy(Material);
            }
        }

        private sealed class PassData
        {
            internal TextureHandle CameraDepth, CameraColor, CurrentDepth, Raw, Blur, Final, PreviousDepth, PreviousRaw, Dirty, Expanded;
            internal History History;
            internal TemporalContactOcclusionPass Pass;
            internal int Width, Height, TilesX, TilesY;
            internal bool Reuse;
        }

        private readonly Dictionary<Camera, History> histories = new();
        private readonly Shader shader;
        private readonly ComputeShader mask;
        private readonly int mark, expand;
        private Vector4 settings;

        internal static TemporalContactOcclusionPass CreateIfSupported()
        {
            if (registerOcclusion == null || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal ||
                !SystemInfo.supportsComputeShaders ||
                SystemInfo.copyTextureSupport == CopyTextureSupport.None ||
                !SystemInfo.IsFormatSupported(GraphicsFormat.R32_UInt, GraphicsFormatUsage.LoadStore) ||
                !SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, GraphicsFormatUsage.Render) ||
                !SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Render))
                return null;

            var shader = Resources.Load<Shader>("SsaoTemporalDepth/TemporalDepth");
            var mask = Resources.Load<ComputeShader>("SsaoTemporalDepth/DepthDirtyTiles");
            return shader && shader.isSupported && mask ? new TemporalContactOcclusionPass(shader, mask) : null;
        }

        private TemporalContactOcclusionPass(Shader shader, ComputeShader mask)
        {
            this.shader = shader;
            this.mask = mask;
            mark = mask.FindKernel("MarkDirty");
            expand = mask.FindKernel("ExpandDirty");
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses + 1;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        internal static bool Supports(CameraData data)
        {
            var camera = data.camera;
            var desc = data.cameraTargetDescriptor;
            var projection = data.GetProjectionMatrix();
            return camera.cameraType == CameraType.Game && camera.orthographic && !data.xrRendering &&
                data.renderType == CameraRenderType.Base && data.resolveFinalTarget &&
                data.isHdrEnabled && desc.msaaSamples == 4 &&
                data.renderScale == 1f && !desc.useDynamicScale && desc.dimension == TextureDimension.Tex2D &&
                desc.volumeDepth == 1 && projection.GetRow(3).Equals(new Vector4(0f, 0f, 0f, 1f)) &&
                !GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode;
        }

        internal void SetProfile(ContactOcclusionProfile profile) =>
            settings = new Vector4(profile.intensity, profile.radius, 1f, profile.falloff);

        internal (int frames, bool reused, bool retained) Status(Camera camera) =>
            histories.TryGetValue(camera, out var history) ? (history.RecordedFrames, history.Reused, history.Valid) : default;

        internal bool ObserveStationaryView(CameraData data)
        {
            if (!histories.TryGetValue(data.camera, out var history)) return true;
            var view = data.GetViewMatrix();
            var projection = data.GetProjectionMatrix();
            if (history.View.Equals(view) && history.Projection.Equals(projection)) return true;

            // Panning cannot reuse any pixels. Render through the stock pass until the view settles.
            history.View = view;
            history.Projection = projection;
            history.Valid = history.Reused = false;
            return false;
        }

        internal void Forget(Camera camera)
        {
            if (histories.Remove(camera, out var history)) Release(history);
        }

        private void Release(History history)
        {
            history.Owner.ReleaseTemporalHistory -= Forget;
            history.Dispose();
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var cameraData = frame.Get<UniversalCameraData>();
            var camera = cameraData.camera;
            var resources = frame.Get<UniversalResourceData>();
            var depth = resources.cameraDepthTexture;
            if (!depth.IsValid()) throw new InvalidOperationException("Temporal SSAO requires the camera depth prepass.");
            var target = cameraData.cameraTargetDescriptor;
            var depthDescriptor = target;
            depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            depthDescriptor.depthStencilFormat = GraphicsFormat.None;
            depthDescriptor.msaaSamples = 1;
            depthDescriptor.bindMS = false;
            var rawDescriptor = depthDescriptor;
            rawDescriptor.colorFormat = RenderTextureFormat.ARGB32;
            var finalDescriptor = depthDescriptor;
            finalDescriptor.colorFormat = RenderTextureFormat.R8;
            if (!histories.TryGetValue(camera, out var history))
            {
                var material = CoreUtils.CreateEngineMaterial(shader);
                material.EnableKeyword("_ORTHOGRAPHIC");
                material.EnableKeyword("_INTERLEAVED_GRADIENT");
                material.EnableKeyword("_SOURCE_DEPTH_HIGH");
                material.EnableKeyword("_SAMPLE_COUNT_HIGH");
                var owner = camera.GetComponent<ContactOcclusionCamera>();
                history = new History { Owner = owner, Material = material };
                histories.Add(camera, history);
                owner.ReleaseTemporalHistory += Forget;
            }

            bool sameSize = rawDescriptor.Equals(history.Descriptor);
            if (!sameSize)
            {
                history.Depth?.Release();
                history.Raw?.Release();
                history.Depth = history.Raw = null;
                history.Descriptor = rawDescriptor;
            }

            var view = cameraData.GetViewMatrix();
            var projection = cameraData.GetProjectionMatrix();
            bool reuse = history.Valid && sameSize && history.View.Equals(view) &&
                history.Projection.Equals(projection) && history.Parameters.Equals(settings) &&
                history.Far == camera.farClipPlane;
            if (!reuse)
            {
                RefreshParameters(history, view, projection, settings, camera);
                history.Depth ??= RTHandles.Alloc(depthDescriptor, filterMode: FilterMode.Point, name: "SSAO previous depth");
                history.Raw ??= RTHandles.Alloc(rawDescriptor, filterMode: FilterMode.Point, name: "SSAO previous estimate");
                history.Halo = CalculateHalo(history);
            }
            history.Valid = true;
            history.Reused = reuse;
            history.RecordedFrames++;

            using var builder = graph.AddUnsafePass<PassData>("Temporal Contact Occlusion", out var pass);
            pass.CameraDepth = depth;
            pass.CameraColor = resources.cameraColor;
            pass.Raw = UniversalRenderer.CreateRenderGraphTexture(graph, rawDescriptor, "SSAO current estimate", false, FilterMode.Bilinear);
            pass.Blur = UniversalRenderer.CreateRenderGraphTexture(graph, rawDescriptor, "SSAO bilateral blur", false, FilterMode.Bilinear);
            pass.Final = UniversalRenderer.CreateRenderGraphTexture(graph, finalDescriptor, "SSAO final", false, FilterMode.Bilinear);
            pass.Width = target.width;
            pass.Height = target.height;
            pass.TilesX = (pass.Width + Tile - 1) / Tile;
            pass.TilesY = (pass.Height + Tile - 1) / Tile;
            pass.History = history;
            pass.Pass = this;
            pass.Reuse = reuse;
            builder.UseTexture(pass.CameraDepth, AccessFlags.Read);
            // Stock URP resolves this attachment when setting the source size. Declare
            // the same lifetime before the forward pass, even at fixed render scale.
            if (pass.CameraColor.IsValid()) builder.UseTexture(pass.CameraColor, AccessFlags.Read);
            builder.UseTexture(pass.Raw, AccessFlags.ReadWrite);
            builder.UseTexture(pass.Blur, AccessFlags.ReadWrite);
            builder.UseTexture(pass.Final, AccessFlags.Write);
            pass.CurrentDepth = UniversalRenderer.CreateRenderGraphTexture(graph, depthDescriptor, "SSAO current depth", false, FilterMode.Point);
            pass.PreviousDepth = graph.ImportTexture(history.Depth);
            pass.PreviousRaw = graph.ImportTexture(history.Raw);
            builder.UseTexture(pass.CurrentDepth, AccessFlags.ReadWrite);
            builder.UseTexture(pass.PreviousDepth, reuse ? AccessFlags.ReadWrite : AccessFlags.Write);
            builder.UseTexture(pass.PreviousRaw, reuse ? AccessFlags.ReadWrite : AccessFlags.Write);
            if (reuse)
            {
                var tiles = new TextureDesc(pass.TilesX, pass.TilesY)
                {
                    format = GraphicsFormat.R32_UInt, enableRandomWrite = true, name = "SSAO dirty tiles"
                };
                pass.Dirty = graph.CreateTexture(tiles);
                tiles.name = "SSAO expanded dirty tiles";
                pass.Expanded = graph.CreateTexture(tiles);
                builder.UseTexture(pass.Dirty, AccessFlags.ReadWrite);
                builder.UseTexture(pass.Expanded, AccessFlags.ReadWrite);
            }

            builder.AllowGlobalStateModification(true);
            registerOcclusion(resources, pass.Final);
            builder.SetGlobalTextureAfterPass(pass.Final, OcclusionTextureId);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) => data.Pass.Render(data, context));
        }

        private static void RefreshParameters(History history, Matrix4x4 view, Matrix4x4 projection,
            Vector4 parameters, Camera camera)
        {
            history.View = view;
            history.Projection = projection;
            history.Parameters = parameters;
            history.Far = camera.farClipPlane;
            var matrix = projection * view;
            history.ViewProjection[0] = matrix;
            view.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            var inverse = (projection * view).inverse;
            var topLeft = inverse.MultiplyPoint(new Vector3(-1f, 1f, -1f));
            var extentX = inverse.MultiplyPoint(new Vector3(1f, 1f, -1f)) - topLeft;
            var extentY = inverse.MultiplyPoint(new Vector3(-1f, -1f, -1f)) - topLeft;
            var extentZ = inverse.MultiplyPoint(new Vector3(0f, 0f, 1f));
            history.TopLeft[0] = topLeft;
            history.X[0] = extentX;
            history.Y[0] = extentY;
            history.Z[0] = extentZ;

            var material = history.Material;
            material.SetVector("_ProjectionParams2", new Vector4(1f / camera.nearClipPlane, 0f, 0f, 0f));
            material.SetMatrixArray("_CameraViewProjections", history.ViewProjection);
            material.SetVectorArray("_CameraViewTopLeftCorner", history.TopLeft);
            material.SetVectorArray("_CameraViewXExtent", history.X);
            material.SetVectorArray("_CameraViewYExtent", history.Y);
            material.SetVectorArray("_CameraViewZExtent", history.Z);
            material.SetVector("_SSAOParams", parameters);
        }

        private static int CalculateHalo(History history)
        {
            var matrix = history.ViewProjection[0];
            Vector3 topLeft = history.TopLeft[0];
            Vector3 extentX = history.X[0], extentY = history.Y[0], extentZ = history.Z[0];
            float x = Footprint(matrix.GetRow(0), 0, history.Descriptor.width);
            float y = Footprint(matrix.GetRow(1), 1, history.Descriptor.height);
            return Mathf.CeilToInt((Mathf.Max(x, y) + 8f) / Tile);

            float Footprint(Vector3 row, int axis, int pixels)
            {
                // Orthographic reconstruction and reprojection are affine. Check all corners
                // and both depth limits, then add the estimator radius and half-precision margin.
                float centerError = 0f;
                float maxProducts = 0f;
                for (int u = 0; u < 2; u++)
                for (int v = 0; v < 2; v++)
                for (int z = 0; z < 2; z++)
                {
                    var point = topLeft + extentX * u + extentY * (1 - v) + extentZ * z;
                    float expected = 2f * (axis == 0 ? u : v) - 1f;
                    centerError = Mathf.Max(centerError, Mathf.Abs(Vector3.Dot(row, point) - expected));
                    maxProducts = Mathf.Max(maxProducts,
                        Mathf.Abs(row.x * point.x) + Mathf.Abs(row.y * point.y) + Mathf.Abs(row.z * point.z));
                }
                float radius = row.magnitude * history.Parameters.y;
                return (centerError + radius * 1.01f + .004f * (maxProducts + radius + 1f)) * .5f * pixels;
            }
        }

        private void Render(PassData data, UnsafeGraphContext context)
        {
            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            var material = data.History.Material;
            var copyScale = context.GetTextureUVOrigin(in data.CameraDepth) == context.GetTextureUVOrigin(in data.CurrentDepth)
                ? new Vector4(1f, 1f, 0f, 0f) : new Vector4(1f, -1f, 0f, 1f);
            Blitter.BlitCameraTexture(cmd, data.CameraDepth, data.CurrentDepth, copyScale,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, 4);
            CoreUtils.SetKeyword(material, Temporal, data.Reuse);
            if (data.Reuse)
            {
                cmd.SetComputeIntParams(mask, "_Size", data.Width, data.Height);
                cmd.SetComputeTextureParam(mask, mark, "_CurrentDepth", (RTHandle)data.CurrentDepth);
                cmd.SetComputeTextureParam(mask, mark, "_PreviousDepth", (RTHandle)data.PreviousDepth);
                cmd.SetComputeTextureParam(mask, mark, "_DirtyOutput", (RTHandle)data.Dirty);
                cmd.DispatchCompute(mask, mark, data.TilesX, data.TilesY, 1);
                cmd.SetComputeIntParams(mask, "_TileCount", data.TilesX, data.TilesY);
                cmd.SetComputeIntParam(mask, "_HaloTiles", data.History.Halo);
                cmd.SetComputeTextureParam(mask, expand, "_DirtyInput", (RTHandle)data.Dirty);
                cmd.SetComputeTextureParam(mask, expand, "_DirtyOutput", (RTHandle)data.Expanded);
                cmd.DispatchCompute(mask, expand, (data.TilesX + 7) / 8, (data.TilesY + 7) / 8, 1);
                material.SetTexture("_TemporalDirtyTiles", data.Expanded);
                material.SetTexture("_TemporalPreviousAO", data.PreviousRaw);
            }

            cmd.SetGlobalVector("_SourceSize", new Vector4(data.Width, data.Height, 1f / data.Width, 1f / data.Height));
            Blit(data.Raw, data.Raw, 0);
            cmd.CopyTexture((RTHandle)data.CurrentDepth, (RTHandle)data.PreviousDepth);
            cmd.CopyTexture((RTHandle)data.Raw, (RTHandle)data.PreviousRaw);
            Blit(data.Raw, data.Blur, 1);
            Blit(data.Blur, data.Raw, 2);
            RTHandle blurResult = data.Raw;
            Vector2 viewportScale = blurResult.useScaling ? (Vector2)blurResult.rtHandleProperties.rtHandleScale : Vector2.one;
            bool flip = context.GetTextureUVOrigin(in data.Raw) != context.GetTextureUVOrigin(in data.Final);
            var finalScale = flip ? new Vector4(viewportScale.x, -viewportScale.y, 0f, viewportScale.y) :
                new Vector4(viewportScale.x, viewportScale.y, 0f, 0f);
            Blitter.BlitCameraTexture(cmd, data.Raw, data.Final, finalScale,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, 3);
            cmd.EnableShaderKeyword(OcclusionKeyword);
            cmd.SetGlobalVector("_AmbientOcclusionParam", new Vector4(1f, 0f, 0f, 0f));

            void Blit(TextureHandle source, TextureHandle destination, int index) =>
                Blitter.BlitCameraTexture(cmd, source, destination, RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store, material, index);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) => cmd.DisableShaderKeyword(OcclusionKeyword);

        public void Dispose()
        {
            foreach (var history in histories.Values)
                Release(history);
            histories.Clear();
        }
    }
}

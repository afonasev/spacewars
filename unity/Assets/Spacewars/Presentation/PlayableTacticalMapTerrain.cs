using System;
using System.Collections;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Spacewars.Presentation
{
    // Static terrain is prepared once per map and can be shared by local views.
    public sealed class PlayableTacticalMapTerrain : IDisposable
    {
        public Texture2D Texture { get; private set; }
        public IEnumerator Preparation { get; }
        public bool PreparationCacheHit { get; private set; }
        public Color[] CompactTerrain { get; private set; }
        public bool IsOverhead => CompactTerrain != null;

        public PlayableTacticalMapTerrain(PlayableProfile profile, bool staged = false, PlayableWorld overheadSource = null)
        {
            Preparation = Prepare(profile, overheadSource);
            if (!staged) LoadingPreparation.Drain(Preparation);
        }

        public static string Group(string mapKey) => "TacticalMinimap-" + mapKey;
        public static string OverheadGroup(string mapKey) => "OverheadBattle-" + mapKey;
        // Two texels per logical pixel at the configured maximum TAB size.
        public static int Resolution(PlayableProfile profile) => Mathf.CeilToInt((float)profile.MinimapTacticalSize) * 2;

        private IEnumerator Prepare(PlayableProfile profile, PlayableWorld overheadSource)
        {
            int size = Resolution(profile);
            if (overheadSource != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                PrepareOverhead(profile, overheadSource, size);
                yield break;
            }
            string group = Group(PreparedMapCache.MapKey(profile.AuthoredMap));
            string key = PreparedMapCache.Key(profile.AuthoredMap, profile, size);
            byte[] bytes = PreparedMapCache.Read(group, key);
            Texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Prepared tactical map terrain",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            if (bytes != null && bytes.Length == size * size * 4)
            {
                Texture.LoadRawTextureData(bytes);
                PreparationCacheHit = true;
            }
            else
            {
                var terrain = new Color[size * size];
                yield return PlayableMapTerrain.Rasterize(profile, size, terrain);
                var pixels = Texture.GetRawTextureData<Color32>();
                for (int i = 0; i < terrain.Length; i++) pixels[i] = terrain[i];
                bytes = Texture.GetRawTextureData();
                PreparedMapCache.Write(group, key, PreparedMapCache.Envelope(key, writer => writer.Write(bytes)));
            }
            Texture.Apply(false, true);
        }

        private void PrepareOverhead(PlayableProfile profile, PlayableWorld world, int size)
        {
            string group = OverheadGroup(PreparedMapCache.MapKey(profile.AuthoredMap));
            string key = PreparedMapCache.Key(profile.AuthoredMap, profile, size);
            byte[] bytes = PreparedMapCache.Read(group, key);
            Texture = new Texture2D(2, 2, TextureFormat.RGB24, false, true)
            {
                name = "Prepared overhead map terrain",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            PreparationCacheHit = bytes != null && Texture.LoadImage(bytes) && Texture.width == size && Texture.height == size;
            if (!PreparationCacheHit)
            {
                bytes = PlayableMapOverheadCapture.Capture(world, profile, size);
                if (!Texture.LoadImage(bytes) || Texture.width != size || Texture.height != size)
                    throw new InvalidOperationException("Invalid overhead map capture for " + group);
                PreparedMapCache.Write(group, key, PreparedMapCache.Envelope(key, writer => writer.Write(bytes)));
            }
            CompactTerrain = Downsample(Texture.GetPixels32(), size);
            Texture.Apply(false, true);
        }

        private static Color[] Downsample(Color32[] source, int sourceSize)
        {
            const int side = PlayableVision.RasterResolution;
            var pixels = new Color[side * side];
            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int left = x * sourceSize / side, right = (x + 1) * sourceSize / side;
                int bottom = y * sourceSize / side, top = (y + 1) * sourceSize / side;
                int red = 0, green = 0, blue = 0;
                for (int sy = bottom; sy < top; sy++)
                for (int sx = left; sx < right; sx++)
                {
                    var pixel = source[sy * sourceSize + sx];
                    red += pixel.r;
                    green += pixel.g;
                    blue += pixel.b;
                }
                float scale = 1f / (255 * (right - left) * (top - bottom));
                pixels[y * side + x] = new Color(red * scale, green * scale, blue * scale);
            }
            return pixels;
        }

        public void Dispose()
        {
            if (!Texture) return;
            if (Application.isPlaying) Object.Destroy(Texture);
            else Object.DestroyImmediate(Texture);
        }
    }

    // Each viewport composites its own fog. The fog grid and static terrain
    // keep their original resolutions and share only the map projection.
    public sealed class PlayableTacticalMapFog : IDisposable
    {
        static readonly int FogMask = Shader.PropertyToID("_MapFog");
        static readonly int FogTint = Shader.PropertyToID("_FogTint");
        static readonly int BoostTerrain = Shader.PropertyToID("_BoostTerrain");
        readonly Material material;
        readonly Texture2D terrain;
        PlayableFogMask lastFog;
        long lastUpload = -1;
        public RenderTexture Texture { get; }

        public PlayableTacticalMapFog(PlayableProfile profile, Texture2D terrain, bool overhead = false)
        {
            this.terrain = terrain;
            material = new Material(Resources.Load<Shader>("TacticalMapFog")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat(BoostTerrain, overhead ? 0 : 1);
            material.SetVector(FogTint, new Vector4((float)profile.FogTintR, (float)profile.FogTintG, (float)profile.FogTintB, 1));
            Texture = new RenderTexture(terrain.width, terrain.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Tactical map with local fog",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        public void Update(PlayableFogMask fog)
        {
            if (ReferenceEquals(lastFog, fog) && lastUpload == fog.Uploads && Texture.IsCreated()) return;
            material.SetTexture(FogMask, fog.Texture);
            var previousTarget = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(terrain, Texture, material);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgb;
            }
            lastFog = fog;
            lastUpload = fog.Uploads;
        }

        public void Dispose()
        {
            Texture.Release();
            Object.Destroy(Texture);
            Object.Destroy(material);
        }
    }
}

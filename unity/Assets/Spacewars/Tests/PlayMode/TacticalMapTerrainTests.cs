using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public sealed class TacticalMapTerrainTests
{
    static Type Authoring => AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetType("Spacewars.Editor.PreparedMapsAuthoring"))
        .First(type => type != null);

    static PlayableProfile Profile(string key) =>
        (PlayableProfile)Authoring.GetMethod("Profile").Invoke(null, new object[] { key });

    [UnityTest, Timeout(180000)]
    public IEnumerator EveryPackagedMatchMapRendersDetailedTerrainAndAlignedMarkers()
    {
        string output = Environment.GetEnvironmentVariable("SPACEWARS_TAB_EVIDENCE");
        string[] maps = (string[])Authoring.GetField("Maps").GetValue(null);
        Assert.IsTrue((bool)Authoring.GetMethod("Fresh").Invoke(null, null), "Prepared resources must match sources.");
        var host = new GameObject("TAB terrain comparison");
        var panel = ScriptableObject.CreateInstance<PanelSettings>();
        using var targetLifetime = new RenderTarget(1232, 616);
        var target = targetLifetime.Texture;
        try
        {
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("FoundationTheme");
            panel.targetTexture = target;
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = panel;
            var root = document.rootVisualElement;
            root.style.backgroundColor = new Color(.025f, .05f, .065f);
            foreach (string key in maps.Where(key => key != "Nereida"))
            {
                var profile = Profile(key);
                var worldHost = new GameObject("Prepared overhead " + key);
                try
                {
                    using var world = new PlayableWorld(worldHost.transform, profile, staged: true);
                    yield return world.Preparation;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    using var detailed = new PlayableTacticalMapTerrain(profile, staged: true, overheadSource: world);
                    yield return detailed.Preparation;
                    clock.Stop();
                    Assert.IsTrue(detailed.IsOverhead, key);
                    using var compact = new PlayableMapTerrain(profile, staged: true, overheadTerrain: detailed.CompactTerrain);
                    yield return compact.Preparation;
                    Assert.AreEqual(PlayableVision.RasterResolution, compact.Texture.width, key);
                    Assert.IsTrue(compact.PreparationCacheHit, key);
                    Assert.IsTrue(detailed.PreparationCacheHit, key);
                    Assert.AreEqual(PlayableTacticalMapTerrain.Resolution(profile), detailed.Texture.width, key);
                    Assert.GreaterOrEqual(detailed.Texture.width, profile.MinimapTacticalSize * 2, key);
                    Debug.Log($"TAB_PREPARED_HIT {key} {detailed.Texture.width}px {clock.Elapsed.TotalMilliseconds:F1}ms");

                    using var fog = new PlayableFogMask(profile);
                    using var composed = new PlayableTacticalMapFog(profile, detailed.Texture, detailed.IsOverhead);
                    double extent = profile.ArenaHalfExtent;
                    var visible = new VisionSource(new NavPoint(-extent * .28, 0), extent * .32);
                    var explored = new VisionSource(new NavPoint(extent * .28, 0), extent * .24);
                    var vision = new PlayableVision(0, extent, extent, 4, profile.FogEdgeFeather);
                    vision.Refresh(new[] { visible, explored }, Array.Empty<KnownBuilding>());
                    fog.Update(vision.Snapshot(), 10);
                    vision.Refresh(new[] { visible }, Array.Empty<KnownBuilding>());
                    fog.Update(vision.Snapshot(), 10);
                    float visibleAlpha = fog.Texture.GetPixel(92, 128).r;
                    float exploredAlpha = fog.Texture.GetPixel(164, 128).r;
                    float unknownAlpha = fog.Texture.GetPixel(128, 240).r;
                    Assert.Less(visibleAlpha, exploredAlpha, key);
                    Assert.Less(exploredAlpha, unknownAlpha, key);
                    compact.Update(fog);
                    composed.Update(fog);
                    var tint = new Color((float)profile.FogTintR, (float)profile.FogTintG, (float)profile.FogTintB);
                    foreach (var probe in new[] { new Vector2Int(92, 128), new Vector2Int(164, 128), new Vector2Int(128, 240) })
                    {
                        int sampleIndex = probe.y * compact.Texture.width + probe.x;
                        var opacity = fog.Texture.GetPixel(probe.x, probe.y).r;
                        Color expected = Color.Lerp(detailed.CompactTerrain[sampleIndex], tint, opacity * .72f);
                        var pixel = compact.Texture.GetPixel(probe.x, probe.y);
                        Assert.AreEqual(expected.r, pixel.r, .005f, key);
                        Assert.AreEqual(expected.g, pixel.g, .005f, key);
                        Assert.AreEqual(expected.b, pixel.b, .005f, key);
                    }
                    var sites = profile.AuthoredMap?.Sites(profile).Select(site =>
                        new PlayableMapMark(site.Id, site.Position, site.Kind, MapMarkState.Ready, null)).ToArray()
                        ?? Array.Empty<PlayableMapMark>();

                    var before = Map(profile, compact.Texture, sites, 8);
                    var after = Map(profile, composed.Texture, sites, 624);
                    root.Add(before);
                    root.Add(after);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    foreach (var site in sites)
                    {
                        Assert.AreEqual(before.Point(site.Position), after.Point(site.Position), key);
                        var point = after.Ground(after.Point(site.Position));
                        Assert.AreEqual(site.Position.X, point.X, .001, key);
                        Assert.AreEqual(site.Position.Z, point.Z, .001, key);
                    }

                    var shot = Read(target);
                    try
                    {
                        var low = shot.GetPixels32();
                        int difference = 0;
                        int painted = 0;
                        var panelColor = low[0];
                        for (int y = 8; y < 608; y += 3)
                        for (int x = 8; x < 608; x += 3)
                        {
                            var a = low[y * target.width + x];
                            var b = low[y * target.width + x + 616];
                            difference += Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);
                            if (Math.Abs(b.r - panelColor.r) + Math.Abs(b.g - panelColor.g) + Math.Abs(b.b - panelColor.b) > 10) painted++;
                        }
                        const int samples = 40000;
                        Assert.Greater(painted, samples / 8, key + " rendered terrain");
                        // Most texels should still match. This catches projection flips,
                        // a missing fog pass, or a mismatch in linear/gamma conversion.
                        Assert.Less((float)difference / (samples * 3), 7f, key + " palette and projection");
                        if (!string.IsNullOrEmpty(output))
                        {
                            Directory.CreateDirectory(output);
                            File.WriteAllBytes(Path.Combine(output, key + "-comparison.png"), shot.EncodeToPNG());
                        }
                    }
                    finally
                    {
                        Object.Destroy(shot);
                        before.RemoveFromHierarchy();
                        after.RemoveFromHierarchy();
                    }
                }
                finally { Object.Destroy(worldHost); }
                yield return null;
            }
        }
        finally
        {
            panel.targetTexture = null;
            Object.Destroy(host);
            Object.Destroy(panel);
        }
    }

    static PlayableMapSurface Map(PlayableProfile profile, Texture texture, PlayableMapMark[] sites, int x)
    {
        var element = new PlayableMapSurface(profile, texture);
        element.style.position = Position.Absolute;
        element.style.left = x;
        element.style.top = 8;
        element.style.width = 600;
        element.style.height = 600;
        element.SetPublic(sites, profile.ArenaHalfExtent);
        return element;
    }

    static Texture2D Read(RenderTexture target)
    {
        var old = RenderTexture.active;
        var shot = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = target;
            shot.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            shot.Apply();
            return shot;
        }
        finally { RenderTexture.active = old; }
    }

    sealed class RenderTarget : IDisposable
    {
        public RenderTexture Texture { get; }
        public RenderTarget(int width, int height)
        {
            Texture = new RenderTexture(width, height, 24);
            Texture.Create();
        }

        public void Dispose()
        {
            Texture.Release();
            Object.Destroy(Texture);
        }
    }
}

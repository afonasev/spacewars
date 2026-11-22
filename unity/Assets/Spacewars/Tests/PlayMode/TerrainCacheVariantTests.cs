#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Spacewars.Tests.PlayMode
{
    public sealed class TerrainCacheVariantTests
    {
        private const int Width = 1728, Height = 1117;
        private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly PropertyInfo Stock = typeof(TerrainMaterialCacheCamera).GetProperty("ForceStock", Internal);
        private static readonly PropertyInfo Full = typeof(TerrainMaterialCacheCamera).GetProperty("CompareFullVariant", Internal);
        private static readonly PropertyInfo Coverage = typeof(TerrainMaterialCacheCamera).GetProperty("ShowCoverage", Internal);
        private static readonly PropertyInfo Role = typeof(TerrainMaterialCacheCamera).GetProperty("ShowTerrainRoles", Internal);
        private static readonly GraphicsFormat Packed = GraphicsFormat.B10G11R11_UFloatPack32;
        private static readonly GraphicsFormat Float = GraphicsFormat.R32G32B32A32_SFloat;

        [UnityTest]
        public IEnumerator CompareStockAndCachedTerrainInFourIndependentViews()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal)
                Assert.Ignore("The terrain cache uses the Metal shader path.");
            string output = Environment.GetEnvironmentVariable("SPACEWARS_TERRAIN_CACHE_VARIANTS_EVIDENCE");
            bool writeEvidence = !string.IsNullOrEmpty(output);
            if (writeEvidence) Directory.CreateDirectory(output);
            var map = new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(
                Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            var profile = PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(
                Resources.Load<TextAsset>("PlayableProfile").text), map);
            var lighting = IndustrialLightingProfile.Load(map.Id);
            var ao = ContactOcclusionProfile.Load();
            var root = new GameObject("Terrain cache variant witness");
            using var world = new PlayableWorld(root.transform, profile);
            world.InspectionReveal();
            var oldSun = RenderSettings.sun;
            var oldAmbient = RenderSettings.ambientLight;
            var oldAmbientMode = RenderSettings.ambientMode;
            var oldScale = Time.timeScale;
            var oldActive = RenderTexture.active;
            var sun = new GameObject("Terrain cache witness sun").AddComponent<Light>();
            sun.transform.SetParent(root.transform);
            lighting.ApplyScene(sun);
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            var sunRotation = sun.transform.rotation;
            Time.timeScale = 0;

            var views = new[] {
                (Name: "soil", Point: new NavPoint(-42,43)),
                (Name: "rock-edge", Point: new NavPoint(-38,14)),
                (Name: "bridge", Point: new NavPoint(0,map.CrossingZ)),
                (Name: "fog-bank", Point: new NavPoint(-map.RiverHalfWidth-.25,map.CrossingZ-8))
            };
            var cameras = new Camera[4];
            var caches = new TerrainMaterialCacheCamera[4];
            var targets = new List<RenderTexture>();
            var comparisons = new List<string> { "view,format,mode,changed_pixels,first_x,first_y,max_rgba_delta" };
            var coverage = new List<string> { "view,cache_hits,cache_misses,pixels,fills,reuses" };
            var coverageByPixel = new byte[cameras.Length][];
            var driftCoverage = new List<string> { "view,mode,hit,miss,background_or_mixed" };
            var rolesByPixel = new byte[cameras.Length][];
            var driftRoles = new List<string> { "view,mode,role,dressing,yard_object,coverage,changed_pixels" };
            var invalidation = new List<string> { "view,scenario,changed_pixels,fills_before,fills_after,reuses_before,reuses_after" };
            var wall = new List<string> { "block,mode,cycle,elapsed_ms" };
            bool exact = true;
            try
            {
                var packedTarget = Target(Width, Height, Packed, 4, 24);
                var packedRead = Target(Width, Height, Packed, 1, 0);
                var floatTarget = Target(Width, Height, Float, 4, 24);
                var floatRead = Target(Width, Height, Float, 1, 0);
                for (int i = 0; i < cameras.Length; i++)
                {
                    var camera = new GameObject(views[i].Name).AddComponent<Camera>();
                    camera.transform.SetParent(root.transform);
                    camera.enabled = false;
                    camera.orthographic = true;
                    camera.orthographicSize = (float)profile.CameraOrthoSize;
                    camera.nearClipPlane = .1f;
                    camera.farClipPlane = 700f;
                    camera.allowHDR = camera.allowMSAA = true;
                    camera.targetTexture = packedTarget;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.045f, .075f, .10f);
                    camera.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
                    camera.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
                    camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ao);
                    camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(world.GraphicsRoot, lighting);
                    cameras[i] = camera;
                    Position(i, 0f);
                    caches[i] = camera.gameObject.AddComponent<TerrainMaterialCacheCamera>();
                    caches[i].Configure(world);
                }

                if (writeEvidence) File.WriteAllText(Path.Combine(output, "profile.txt"),
                    $"{map.Id} {profile.ProfileId}@{profile.Revision}, {ao.id}@{ao.revision}, {Width}x{Height}, MSAA4x\n");
                SetTick(189);
                for (int i = 0; i < 8; i++)
                {
                    foreach (var camera in cameras) camera.Render();
                    yield return null;
                }

                for (int i = 0; i < cameras.Length; i++)
                {
                    Assert.AreEqual(1, caches[i].Fills);
                    Assert.Positive(caches[i].Reuses);
                    Assert.AreEqual(4, CameraAntialiasingDiagnostics.Samples(cameras[i]));
                    var glow = cameras[i].GetComponent<ActorGlowCamera>();
                    glow.enabled = false;
                    var mask = Read(i, floatTarget, floatRead, "coverage");
                    rolesByPixel[i] = Read(i, floatTarget, floatRead, "roles");
                    glow.enabled = true;
                    var classified = coverageByPixel[i] = new byte[Width * Height];
                    int hits = 0, misses = 0;
                    for (int p = 0; p < mask.Length; p += 16)
                    {
                        float red = BitConverter.ToSingle(mask, p), green = BitConverter.ToSingle(mask, p + 4);
                        if (red == 0 && green == 1) { hits++; classified[p / 16] = 1; }
                        if (red == 1 && green == 0) { misses++; classified[p / 16] = 2; }
                    }
                    Assert.Greater(hits, Width * Height / 4, views[i].Name);
                    coverage.Add($"{views[i].Name},{hits},{misses},{Width * Height},{caches[i].Fills},{caches[i].Reuses}");
                }
                if (writeEvidence) File.WriteAllLines(Path.Combine(output, "coverage.csv"), coverage);

                for (int light = 0; light < 2; light++)
                {
                    if (light == 1)
                    {
                        sun.transform.rotation = Quaternion.Euler(8, 18, 0) * sunRotation;
                        var vision = new PlayableVision(0, profile.ArenaHalfExtent, profile.ArenaHalfExtent,
                            profile.VisionCellSize, profile.FogEdgeFeather);
                        vision.Refresh(new[] { new VisionSource(new NavPoint(-map.RiverHalfWidth - 8, map.CrossingZ - 8), 13) },
                            Array.Empty<KnownBuilding>());
                        world.Fog.Update(vision.Snapshot(), 1);
                        SetTick(418);
                    }
                    for (int i = 0; i < cameras.Length; i++)
                    foreach (var format in new[] {
                        (Name:"packed", Target:packedTarget, Read:packedRead, Bytes:4),
                        (Name:"rgba_float", Target:floatTarget, Read:floatRead, Bytes:16)
                    })
                    {
                        string label = views[i].Name + (light == 1 ? "_relit_fog" : "");
                        var stock = Read(i, format.Target, format.Read, "stock");
                        Compare(label, format.Name, "stock_repeat", stock, Read(i, format.Target, format.Read, "stock"), format.Bytes);
                        var full = Read(i, format.Target, format.Read, "full");
                        Compare(label, format.Name, "full_vs_stock", stock, full, format.Bytes);
                        var cached = Read(i, format.Target, format.Read, "cached");
                        Compare(label, format.Name, "cached_vs_full", full, cached, format.Bytes);
                        Compare(label, format.Name, "cached_vs_stock", stock, cached, format.Bytes);
                        if (format.Bytes == 16)
                        {
                            ClassifyDrift(label, "full_vs_stock", stock, full, coverageByPixel[i], rolesByPixel[i]);
                            ClassifyDrift(label, "cached_vs_stock", stock, cached, coverageByPixel[i], rolesByPixel[i]);
                        }
                    }
                }
                if (writeEvidence) File.WriteAllLines(Path.Combine(output, "comparison.csv"), comparisons);
                if (writeEvidence) File.WriteAllLines(Path.Combine(output, "drift-coverage.csv"), driftCoverage);
                if (writeEvidence) File.WriteAllLines(Path.Combine(output, "drift-roles.csv"), driftRoles);

                for (int i = 0; i < cameras.Length; i++)
                {
                    var cache = caches[i];
                    int fills = cache.Fills, reuses = cache.Reuses;
                    Position(i, .085f);
                    cameras[i].orthographicSize *= i % 2 == 0 ? .9f : 1.1f;
                    byte[] before = Read(i, packedTarget, packedRead, "stock");
                    byte[] after = Read(i, packedTarget, packedRead, "cached");
                    var mismatch = Differences(before, after, 4).Count;
                    invalidation.Add($"{views[i].Name},pan_zoom,{mismatch},{fills},{cache.Fills},{reuses},{cache.Reuses}");
                    Assert.AreEqual(fills, cache.Fills);
                    Assert.AreEqual(reuses, cache.Reuses);
                    Assert.Zero(mismatch);
                }
                if (writeEvidence) File.WriteAllLines(Path.Combine(output, "invalidation.csv"), invalidation);

                if (Environment.GetEnvironmentVariable("SPACEWARS_TERRAIN_CACHE_VARIANTS_TIMING") == "1")
                {
                    var framebuffer = Target(Width * 2, Height * 2, Packed, 4, 24);
                    var resolve = Target(Width * 2, Height * 2, Packed, 1, 0);
                    sun.transform.rotation = sunRotation;
                    world.InspectionReveal();
                    for (int i = 0; i < cameras.Length; i++)
                    {
                        cameras[i].targetTexture = framebuffer;
                        cameras[i].rect = new Rect(.5f * (i % 2), .5f * (i / 2), .5f, .5f);
                        cameras[i].orthographicSize = (float)profile.CameraOrthoSize;
                        Position(i, 0f);
                        SetMode(i, "cached");
                    }

                    for (int i = 0; i < 8; i++)
                    {
                        foreach (var camera in cameras) camera.Render();
                        yield return null;
                    }
                    Assert.True(caches.All(cache => cache.Fills == 2));
                    var modes = new[] { "stock", "full", "cached" };
                    for (int block = 0; block < 6; block++)
                    for (int index = 0; index < modes.Length; index++)
                    {
                        string mode = modes[(block + (block < 3 ? index : 2 - index)) % modes.Length];
                        for (int i = 0; i < cameras.Length; i++) SetMode(i, mode);
                        for (int cycle = 0; cycle < 12; cycle++)
                        {
                            SetTick(200 + cycle);
                            long start = Stopwatch.GetTimestamp();
                            foreach (var camera in cameras) camera.Render();
                            Graphics.Blit(framebuffer, resolve);
                            var fence = AsyncGPUReadback.Request(resolve, 0, 0, 1, 0, 1, 0, 1);
                            fence.WaitForCompletion();
                            double elapsed = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                            Assert.False(fence.hasError);
                            if (cycle >= 2)
                                wall.Add(FormattableString.Invariant($"{block},{mode},{cycle},{elapsed:F5}"));
                        }
                    }
                    if (writeEvidence) File.WriteAllLines(Path.Combine(output, "wall.csv"), wall);
                }
                Assert.True(exact, string.Join(Environment.NewLine, comparisons));

                void Compare(string view, string format, string mode, byte[] a, byte[] b, int stride)
                {
                    var diff = Differences(a, b, stride);
                    exact &= diff.Count == 0;
                    comparisons.Add(FormattableString.Invariant(
                        $"{view},{format},{mode},{diff.Count},{(diff.First < 0 ? -1 : diff.First % Width)},{(diff.First < 0 ? -1 : diff.First / Width)},{diff.Max:R}"));
                }

                void ClassifyDrift(string view, string mode, byte[] stock, byte[] candidate, byte[] classification, byte[] roles)
                {
                    var counts = new int[3];
                    var roleCounts = new Dictionary<(float Role, float Dressing, float Yard, float Coverage), int>();
                    for (int pixel = 0; pixel < classification.Length; pixel++)
                    {
                        int offset = pixel * 16;
                        if (stock.AsSpan(offset, 16).SequenceEqual(candidate.AsSpan(offset, 16))) continue;
                        counts[classification[pixel]]++;
                        var key = (BitConverter.ToSingle(roles, offset), BitConverter.ToSingle(roles, offset + 4),
                            BitConverter.ToSingle(roles, offset + 8), BitConverter.ToSingle(roles, offset + 12));
                        roleCounts.TryGetValue(key, out int count);
                        roleCounts[key] = count + 1;
                    }
                    driftCoverage.Add($"{view},{mode},{counts[1]},{counts[2]},{counts[0]}");
                    foreach (var (role, count) in roleCounts.OrderByDescending(pair => pair.Value))
                        driftRoles.Add(FormattableString.Invariant($"{view},{mode},{role.Role:R},{role.Dressing:R},{role.Yard:R},{role.Coverage:R},{count}"));
                }
            }
            finally
            {
                foreach (var cache in caches)
                    if (cache) cache.enabled = false;
                foreach (var camera in cameras)
                    if (camera) camera.targetTexture = null;
                RenderTexture.active = oldActive;
                foreach (var target in targets)
                {
                    target.Release();
                    Object.Destroy(target);
                }
                RenderSettings.sun = oldSun;
                RenderSettings.ambientMode = oldAmbientMode;
                RenderSettings.ambientLight = oldAmbient;
                Time.timeScale = oldScale;
                Object.Destroy(root);
            }

            void Position(int index, float pan)
            {
                var point = views[index].Point;
                var focus = world.Point(new NavPoint(point.X + pan, point.Z));
                var camera = cameras[index];
                camera.transform.position = focus + new Vector3((float)profile.CameraOffsetX,
                    (float)profile.CameraHeight, (float)profile.CameraOffsetZ);
                camera.transform.LookAt(focus);
            }

            void SetTick(int tick) => world.UpdateMatchAnimation(new PlayableSnapshot(profile.ProfileId, profile.Revision,
                1, 7, tick, tick, RuntimeStatus.Running, false, PlayableMatchOutcome.Playing, 0, null,
                Array.Empty<PlayableEntitySnapshot>(), Array.Empty<PlayableBuildingSnapshot>(),
                Array.Empty<PlayableProjectileSnapshot>(), null, null, activeProfile: profile));

            void SetMode(int index, string mode)
            {
                Stock.SetValue(caches[index], mode == "stock");
                Full.SetValue(caches[index], mode == "full");
                Coverage.SetValue(caches[index], mode == "coverage");
                Role.SetValue(caches[index], mode == "roles");
            }

            byte[] Read(int index, RenderTexture target, RenderTexture resolved, string mode)
            {
                var camera = cameras[index];
                SetMode(index, mode);
                camera.targetTexture = target;
                camera.Render();
                camera.Render();
                Graphics.Blit(target, resolved);
                var request = AsyncGPUReadback.Request(resolved);
                request.WaitForCompletion();
                Assert.False(request.hasError);
                return request.GetData<byte>().ToArray();
            }

            RenderTexture Target(int width, int height, GraphicsFormat format, int samples, int depth)
            {
                var target = new RenderTexture(new RenderTextureDescriptor(width, height, format, depth)
                    { msaaSamples = samples });
                targets.Add(target);
                Assert.True(target.Create());
                Assert.AreEqual(format, target.graphicsFormat);
                Assert.AreEqual(samples, target.antiAliasing);
                return target;
            }
        }

        private static (int Count, int First, float Max) Differences(byte[] a, byte[] b, int stride)
        {
            Assert.AreEqual(a.Length, b.Length);
            int count = 0, first = -1;
            float max = 0;
            for (int offset = 0; offset < a.Length; offset += stride)
            for (int i = 0; i < stride; i++)
            {
                if (a[offset + i] == b[offset + i]) continue;
                if (first < 0) first = offset / stride;
                count++;
                if (stride == 16)
                    for (int channel = 0; channel < stride; channel += 4)
                        max = Mathf.Max(max, Mathf.Abs(BitConverter.ToSingle(a, offset + channel) -
                            BitConverter.ToSingle(b, offset + channel)));
                break;
            }
            return (count, first, max);
        }
    }
}
#endif

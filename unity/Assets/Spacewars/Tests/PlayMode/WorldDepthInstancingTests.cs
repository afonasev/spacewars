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
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Spacewars.Tests.PlayMode
{
    public sealed class WorldDepthInstancingTests
    {
        private const int Width = 1728, Height = 1117;
        private static readonly GraphicsFormat Hdr = GraphicsFormat.B10G11R11_UFloatPack32;
        private static readonly GraphicsFormat FloatHdr = GraphicsFormat.R32G32B32A32_SFloat;
        private static readonly string[] Modes = { "stock", "depth" };

        [UnityTest]
        public IEnumerator WorldDepthInstancingMatchesColorAndReducesSubmissions()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal || !SystemInfo.supportsInstancing)
                Assert.Ignore("World depth instancing is enabled on supported Metal devices.");
            string output = Environment.GetEnvironmentVariable("SPACEWARS_HEALTH_SUBMISSION_EVIDENCE");
            bool writeEvidence = !string.IsNullOrEmpty(output);
            if (writeEvidence) Directory.CreateDirectory(output);
            var map = new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(
                Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
            var profile = PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(
                Resources.Load<TextAsset>("PlayableProfile").text), map);
            var lighting = IndustrialLightingProfile.Load(map.Id);
            var originalSun = RenderSettings.sun;
            var originalAmbient = RenderSettings.ambientLight;
            var originalAmbientMode = RenderSettings.ambientMode;
            var originalActive = RenderTexture.active;
            var originalScale = Time.timeScale;
            var restorePreferences = NativePreferenceWriter.Capture();
            var configure = typeof(PlayableWorld).GetMethod("SetSplitScreenDepthInstancing",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(configure);
            var root = new GameObject("Health submissions across four local worlds");
            var sun = new GameObject("Health submission sun").AddComponent<Light>();
            sun.transform.SetParent(root.transform);
            lighting.ApplyScene(sun);
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            Time.timeScale = 0;
            NativeUserSettings.HideDistantHealth = false;

            var worlds = new PlayableWorld[4];
            var cameras = new Camera[4];
            var actors = new List<(int seat, PlayableWorld.Actor actor, NavPoint position, float ratio)>();
            var renderers = new List<Renderer>();
            var worldMaterials = new HashSet<Material>();
            var target = Target(4, 24, Hdr, Width * 2, Height * 2);
            var resolved = Target(1, 0, Hdr, Width * 2, Height * 2);
            var floatTarget = Target(4, 24, FloatHdr, Width, Height);
            var floatResolved = Target(1, 0, FloatHdr, Width, Height);
            var comparisons = new List<string> { "case,mode,changed_hdr_pixels" };
            var stats = new List<string> { "mode,frame,profiler_draws,profiler_setpass,triangles,unity_draws,instanced_batches" };
            var submissionCounts = new Dictionary<string, List<long>>();
            var instanceCounts = new Dictionary<string, List<int>>();
            var elapsed = new List<string> { "mode,block,cycle,wall_ms" };
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
            using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
            using var triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            try
            {
                for (int seat = 0; seat < cameras.Length; seat++)
                {
                    var world = worlds[seat] = new PlayableWorld(root.transform, profile);
                    world.SetPresentationLayer(8 + seat);
                    configure.Invoke(world, new object[] { true });
                    world.InspectionReveal();
                    var camera = cameras[seat] = new GameObject($"Health submission view {seat}").AddComponent<Camera>();
                    camera.transform.SetParent(root.transform);
                    camera.enabled = false;
                    camera.targetTexture = target;
                    camera.rect = new Rect(.5f * (seat % 2), .5f * (seat / 2), .5f, .5f);
                    camera.orthographic = true;
                    camera.orthographicSize = (float)profile.CameraOrthoSize;
                    camera.nearClipPlane = .1f;
                    camera.farClipPlane = 300f;
                    camera.cullingMask = 1 << (8 + seat);
                    camera.allowHDR = camera.allowMSAA = true;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.045f, .075f, .10f);
                    camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());
                    camera.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
                    camera.gameObject.AddComponent<CameraAntialiasing>().Configure(CameraAntialiasingProfile.Load());
                    camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(world.GraphicsRoot, lighting);

                    double z = seat < 2 ? map.CrossingZ : -map.CrossingZ;
                    double x = seat % 2 == 0 ? -6 : 6;
                    var focus = world.Point(new NavPoint(x, z));
                    camera.transform.position = focus + new Vector3((float)profile.CameraOffsetX,
                        (float)profile.CameraHeight, (float)profile.CameraOffsetZ);
                    camera.transform.LookAt(focus);
                    for (int i = 0; i < 24; i++)
                    {
                        var actor = world.Tank(seat * 24 + i + 1, i % 2 == 0);
                        var position = new NavPoint(x + (i % 6 - 2.5) * 1.7, z + (i / 6 - 1.5) * 1.6);
                        actors.Add((seat, actor, position, .37f + (i % 5) * .15f));
                        ColorUtility.TryParseHtmlString(NativeLobbyConfiguration.ColorHex[i % 4], out var color);
                        PlayableWorld.PaintOwner(actor, color);
                    }
                }

                Layout(0f);
                foreach (var item in actors)
                {
                    var parts = item.actor.Health.parent.GetComponentsInChildren<MeshRenderer>(true)
                        .Where(part => part.enabled).ToArray();
                    Assert.AreEqual(4, parts.Length);
                    foreach (var part in parts)
                    {
                        var block = new MaterialPropertyBlock();
                        part.GetPropertyBlock(block);
                        Assert.False(block.isEmpty);
                        Assert.True(part.sharedMaterial.enableInstancing, "The split-screen world configures depth instancing on its actor parts");
                        renderers.Add(part);
                        worldMaterials.Add(part.sharedMaterial);
                    }
                }
                Assert.AreEqual(4, worldMaterials.Count);
                Assert.AreEqual(4 * 24 * 4, renderers.Count);
                yield return null;
                bool allExact = true;
                foreach (string scene in new[] { "authored", "fog_zoom" })
                {
                    if (scene == "fog_zoom")
                    {
                        for (int seat = 0; seat < cameras.Length; seat++)
                        {
                            cameras[seat].orthographicSize *= .8f;
                            if (seat % 2 != 0) worlds[seat].Fog.Reset();
                        }
                        Layout(.12f);
                    }
                    var baseline = RenderAndRead("stock");
                    int difference = CountChanged(baseline, RenderAndRead("depth"), 4);
                    allExact &= difference == 0;
                    comparisons.Add($"{scene},packed_depth,{difference}");
                    difference = CountChanged(baseline, RenderAndRead("stock"), 4);
                    allExact &= difference == 0;
                    comparisons.Add($"{scene},packed_repeat,{difference}");

                    foreach (var renderer in renderers) renderer.forceRenderingOff = true;
                    int off = CountChanged(baseline, RenderAndRead("stock"), 4);
                    Assert.Greater(off, 100, "Reference actually renders the health bars");
                    comparisons.Add($"{scene},packed_off,{off}");
                    foreach (var renderer in renderers) renderer.forceRenderingOff = false;
                    Assert.True(cameras.All(c => CameraAntialiasingDiagnostics.Samples(c) == 4));

                    for (int seat = 0; seat < cameras.Length; seat++)
                    {
                        var camera = cameras[seat];
                        var rect = camera.rect;
                        camera.targetTexture = floatTarget;
                        camera.rect = new Rect(0f, 0f, 1f, 1f);
                        Layout(scene == "authored" ? 0f : .12f);
                        var floatBaseline = FloatRead("stock", camera);
                        int floatChanged = CountChanged(floatBaseline, FloatRead("depth", camera), 16);
                        comparisons.Add($"{scene}_seat{seat},rgba_float_depth,{floatChanged}");
                        allExact &= floatChanged == 0;
                        Assert.AreEqual(4, CameraAntialiasingDiagnostics.Samples(camera));
                        camera.targetTexture = target;
                        camera.rect = rect;
                        Layout(scene == "authored" ? 0f : .12f);
                    }
                }
                if (writeEvidence) File.WriteAllLines(Path.Combine(output, "comparison.csv"), comparisons);
                if (writeEvidence) File.WriteAllText(Path.Combine(output, "profile.txt"),
                    $"{map.Id} {profile.ProfileId}@{profile.Revision}; 3456x2234, four 1728x1117 HDR/MSAA4x, {actors.Count} bars, {renderers.Count} bindings\n");
                foreach (var mode in Modes)
                {
                    Bind(mode);
                    submissionCounts[mode] = new List<long>();
                    instanceCounts[mode] = new List<int>();
                    foreach (var camera in cameras) camera.enabled = true;
                    for (int warm = 0; warm < 4; warm++) yield return null;
                    for (int sample = 0; sample < 8; sample++)
                    {
                        yield return null;
                        submissionCounts[mode].Add(draws.LastValue);
                        instanceCounts[mode].Add(UnityStats.instancedBatches);
                        stats.Add($"{mode},{Time.frameCount},{draws.LastValue},{setPass.LastValue},{triangles.LastValue}," +
                            $"{UnityStats.drawCalls},{UnityStats.instancedBatches}");
                    }
                    foreach (var camera in cameras) camera.enabled = false;
                }
                if (writeEvidence) File.WriteAllLines(Path.Combine(output, "submissions.csv"), stats);
                Assert.True(allExact, string.Join(Environment.NewLine, comparisons));
                Assert.Less(submissionCounts["depth"].Min(), submissionCounts["stock"].Min(),
                    "Runtime route reduces submitted draws");
                Assert.Greater(instanceCounts["depth"].Min(), instanceCounts["stock"].Min(),
                    "Runtime route issues instanced batches");
                if (Environment.GetEnvironmentVariable("SPACEWARS_HEALTH_SUBMISSION_TIMING") == "1")
                {
                    for (int block = 0; block < 6; block++)
                    foreach (var mode in block % 2 == 0 ? Modes : Modes.Reverse())
                    {
                        Bind(mode);
                        for (int frame = 0; frame < 14; frame++)
                        {
                            Layout(frame * .019f);
                            long start = Stopwatch.GetTimestamp();
                            Render();
                            var complete = AsyncGPUReadback.Request(resolved, 0, 0, 1, 0, 1, 0, 1);
                            complete.WaitForCompletion();
                            double wall = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                            Assert.False(complete.hasError);
                            if (frame >= 2) elapsed.Add(FormattableString.Invariant($"{mode},{block},{frame},{wall:F5}"));
                        }
                    }
                    if (writeEvidence) File.WriteAllLines(Path.Combine(output, "wall.csv"), elapsed);
                }
            }
            finally
            {
                foreach (var camera in cameras)
                {
                    if (!camera) continue;
                    camera.enabled = false;
                    camera.targetTexture = null;
                }
                foreach (var world in worlds) world?.Dispose();
                RenderTexture.active = originalActive;
                foreach (var texture in new[] { target, resolved, floatTarget, floatResolved })
                {
                    texture.Release();
                    Object.Destroy(texture);
                }
                Object.Destroy(root);
                RenderSettings.sun = originalSun;
                RenderSettings.ambientMode = originalAmbientMode;
                RenderSettings.ambientLight = originalAmbient;
                Time.timeScale = originalScale;
                restorePreferences();
            }

            void Layout(float step)
            {
                foreach (var item in actors)
                {
                    double offset = item.ratio > .55f ? step : 0;
                    worlds[item.seat].ApplyUnitPose(item.actor, PlayableEntityKind.Tank,
                        new PlayableDisplayPose(new NavPoint(item.position.X + offset, item.position.Z),
                            item.seat % 2 == 0 ? 0 : Math.PI, 0));
                    PlayableWorld.UpdateHealth(item.actor, cameras[item.seat], item.ratio, true);
                }
            }

            void Bind(string mode)
            {
                foreach (var material in worldMaterials)
                    material.enableInstancing = mode == "depth";
            }

            byte[] FloatRead(string mode, Camera camera)
            {
                Bind(mode);
                camera.Render();
                camera.Render();
                Graphics.Blit(floatTarget, floatResolved);
                return Read(floatResolved);
            }

            byte[] RenderAndRead(string mode)
            {
                Bind(mode);
                Render();
                Render();
                return Read(resolved);
            }

            void Render()
            {
                foreach (var camera in cameras) camera.Render();
                Graphics.Blit(target, resolved);
            }
        }

        private static byte[] Read(RenderTexture texture)
        {
            var request = AsyncGPUReadback.Request(texture);
            request.WaitForCompletion();
            Assert.False(request.hasError);
            return request.GetData<byte>().ToArray();
        }

        private static int CountChanged(byte[] a, byte[] b, int pixelBytes)
        {
            Assert.AreEqual(a.Length, b.Length);
            int count = 0;
            for (int i = 0; i < a.Length; i += pixelBytes)
            for (int offset = 0; offset < pixelBytes; offset++)
            {
                if (a[i + offset] == b[i + offset]) continue;
                count++;
                break;
            }
            return count;
        }

        private static RenderTexture Target(int samples, int depth, GraphicsFormat format, int width, int height)
        {
            var rt = new RenderTexture(new RenderTextureDescriptor(width, height, format, depth) { msaaSamples = samples });
            Assert.True(rt.Create());
            Assert.AreEqual(format, rt.graphicsFormat);
            Assert.AreEqual(samples, rt.antiAliasing);
            return rt;
        }
    }
}
#endif

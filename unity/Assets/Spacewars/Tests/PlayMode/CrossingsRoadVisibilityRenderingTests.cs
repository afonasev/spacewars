using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    // Capture-only diagnostic conditions; never writes art profiles or player preferences.
    public sealed class CrossingsRoadVisibilityRenderingTests
    {
        [UnityTest] public IEnumerator NormalCameraAcrossLightAndFogConditions()
        {
            var profile = PlayableProfile.ThreeCrossingsDefault;
            var lighting = IndustrialLightingProfile.Load(profile.AuthoredMap.Id);
            var ambient = RenderSettings.ambientLight;
            var ambientMode = RenderSettings.ambientMode;
            var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            var shadowDistance = pipeline.shadowDistance;
            var root = new GameObject("Road visibility QA");
            var target = new RenderTexture(1600, 1000, 24); target.Create();
            var pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            var output = Environment.GetEnvironmentVariable("SPACEWARS_ROAD_VISIBILITY_EVIDENCE")
                ?? Path.Combine(Application.dataPath, "..", "testResults", "road-visibility");
            Directory.CreateDirectory(output);
            using (var world = new PlayableWorld(root.transform, profile))
            try
            {
                var sun = new GameObject("QA sun").AddComponent<Light>(); sun.transform.SetParent(root.transform);
                var camera = new GameObject("Normal gameplay camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform); camera.orthographic = true;
                camera.orthographicSize = (float)profile.CameraOrthoSize;
                camera.nearClipPlane = .1f; camera.farClipPlane = 700; camera.targetTexture = target;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.075f, .10f, .13f);
                camera.gameObject.AddComponent<VisibleShadowCoverage>().Configure(root.transform, lighting);
                camera.gameObject.AddComponent<ContactOcclusionCamera>().Configure(ContactOcclusionProfile.Load());
                camera.gameObject.AddComponent<ActorGlowCamera>().Configure(ActorMaterialProfile.Load());
                var ramp = ((ThreeCrossingsMap)profile.AuthoredMap).Supports.Single(s => s.Id == "platform--1--1-east-west-ramp");
                var rampPoint = new NavPoint((ramp.Bounds.MinX + ramp.Bounds.MaxX) * .5, (ramp.Bounds.MinZ + ramp.Bounds.MaxZ) * .5);
                var sites = new[] { new NavPoint(-40, -20), new NavPoint(40, 0), rampPoint };
                var names = new[] { "bend", "mine", "height-transition" };
                var conditions = new[] { "standard", "low-side-sun", "ambient-only", "fog-boundary" };
                var offset = new Vector3((float)profile.CameraOffsetX, (float)profile.CameraHeight, (float)profile.CameraOffsetZ);
                File.WriteAllText(Path.Combine(output, "inspection.txt"),
                    $"{world.SurfaceRevision}; {lighting.id}@{lighting.revision}; normal={profile.CameraOrthoSize}; offset={offset}; 1600x1000; Unity={Application.unityVersion}; GPU={SystemInfo.graphicsDeviceName}.\n" +
                    "Static production world and production fog evaluator, staged vision sources, no match seed or gameplay acceptance. Standard uses saved lighting; low-side-sun rotates only light to (20,125,0); ambient-only disables direct light, not a claim of actual cast shadow. Fog uses standard lighting and production profile opacity/feather. No profile/settings writes.\n");
                for (int site = 0; site < sites.Length; site++)
                {
                    var point = sites[site]; var focus = world.Point(point);
                    camera.transform.position = focus + offset; camera.transform.LookAt(focus);
                    Color32[] baseline = null;
                    foreach (var condition in conditions)
                    {
                        lighting.ApplyScene(sun); sun.shadows = LightShadows.Soft;
                        world.Fog.Reset(); world.Fog.ShowPublic();
                        if (condition == "low-side-sun") sun.transform.rotation = Quaternion.Euler(20, 125, 0);
                        if (condition == "ambient-only") sun.intensity = 0;
                        if (condition == "fog-boundary")
                        {
                            world.Fog.Reset();
                            var vision = new PlayableVision(0, profile.ArenaHalfExtent, profile.ArenaHalfExtent, profile.VisionCellSize, profile.FogEdgeFeather);
                            // A previously visited disc, followed by a smaller current view to its west.
                            vision.Refresh(new[] { new VisionSource(new NavPoint(point.X - 5, point.Z), 13) }, Array.Empty<KnownBuilding>());
                            vision.Refresh(new[] { new VisionSource(new NavPoint(point.X - 9, point.Z), 7) }, Array.Empty<KnownBuilding>());
                            world.Fog.Update(vision.Snapshot(), (float)(Math.Max(profile.FogRevealMs, profile.FogConcealMs) / 1000 + 1));
                            var mask = world.Fog.Texture.GetPixels32();
                            Assert.IsTrue(mask.Any(p => p.r == 0), "Visible fog region exists");
                            Assert.IsTrue(mask.Any(p => p.r == Mathf.RoundToInt((float)profile.FogExploredOpacity * 255)), "Explored region exists");
                            Assert.IsTrue(mask.Any(p => p.r == Mathf.RoundToInt((float)profile.FogUnseenOpacity * 255)), "Unseen region exists");
                        }
                        for (int frame = 0; frame < 8; frame++) yield return null;
                        camera.Render();
                        var previous = RenderTexture.active;
                        try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); pixels.Apply(); }
                        finally { RenderTexture.active = previous; }
                        var current = pixels.GetPixels32();
                        Assert.Less(current.Count(p => p.r > 200 && p.b > 200 && p.g < 40), 16, "No error pink");
                        if (baseline == null) baseline = current;
                        else Assert.Greater(current.Where((p, i) => Math.Abs(p.r - baseline[i].r) + Math.Abs(p.g - baseline[i].g) + Math.Abs(p.b - baseline[i].b) > 12).Count(), 100, "Condition visibly changes the captured scene");
                        File.WriteAllBytes(Path.Combine(output, names[site] + "-" + condition + ".png"), pixels.EncodeToPNG());
                        File.AppendAllText(Path.Combine(output, "inspection.txt"), $"{names[site]} {condition}: focus={focus}; sunIntensity={sun.intensity}; rotation={sun.transform.eulerAngles}\n");
                    }
                }
                Assert.AreEqual(12, Directory.GetFiles(output, "*.png").Length, "Complete three-site/four-condition capture matrix");
            }
            finally
            {
                RenderSettings.ambientLight = ambient; RenderSettings.ambientMode = ambientMode;
                pipeline.shadowDistance = shadowDistance;
                UnityEngine.Object.Destroy(root); UnityEngine.Object.Destroy(pixels);
                target.Release(); UnityEngine.Object.Destroy(target);
            }
        }
    }
}

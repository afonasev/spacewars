using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEditor;

namespace Spacewars.Tests.EditMode
{
    public sealed class FoundryIndustrialLandmarksTests
    {
        [Test]
        public void ProfileHasNamedBoundedVisualControls()
        {
            var profile = FoundryIndustrialProfile.Load();
            var settings = profile.Parameters().ToArray();
            Assert.AreEqual("foundry-industrial-landmarks-v1", profile.id);
            Assert.AreEqual(4, settings.Length);
            Assert.IsTrue(settings.All(p => p.Path.StartsWith("visual.foundryIndustrial.", StringComparison.Ordinal)));
            Assert.IsTrue(settings.All(p => p.Step > 0 && p.Minimum < p.Maximum));
            Assert.Throws<ArgumentException>(() => { profile.ribSpacing = 0; profile.Validate(); });
        }

        [Test]
        public void BuildCombinesFactoryMeshesInsideBlockedBoundsWithoutAddingColliders()
        {
            var profile = FoundryGreybox.LoadProfile();
            var map = (FoundryMap)profile.AuthoredMap;
            var surface = FoundrySurfaceProfile.Load();
            var bounds = map.Solids.Skip(map.Solids.Count - 4).ToArray();
            var before = bounds.Select(b => string.Join(",", b.Footprint.Vertices.Select(p => p.X + "/" + p.Z)) + "/" + b.Top).ToArray();
            var root = new GameObject("Foundry industrial landmark focused test");
            var meshes = new System.Collections.Generic.List<Mesh>();
            var material = new Material(Resources.Load<Shader>("TerritoryFog"));
            try
            {
                FoundryIndustrialLandmarks.Build(root.transform, material, map, surface, meshes);
                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                Assert.AreEqual(20, renderers.Length, "Four spatially bounded factories, five renderers per complete palette/property-block key.");
                Assert.AreEqual(20, meshes.Count);
                Assert.AreEqual(0, root.GetComponentsInChildren<Collider>().Length);
                Assert.IsTrue(renderers.All(r => r.sharedMaterial == material));
                Assert.IsTrue(renderers.All(r => Mathf.Approximately(GetPropertyBlockFloat(r, "_SurfaceRole"), 12)));
                var boundReport = new System.Text.StringBuilder();
                bool allVerticesInside = true;
                foreach (var mesh in meshes)
                {
                    Assert.Greater(mesh.vertexCount, 0);
                    Assert.Greater(mesh.triangles.Length / 3, 0);
                    var vertices = mesh.vertices;
                    float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
                    foreach (var vertex in vertices)
                    {
                        var world = root.transform.TransformPoint(vertex);
                        minX = Mathf.Min(minX, world.x); maxX = Mathf.Max(maxX, world.x);
                        minZ = Mathf.Min(minZ, world.z); maxZ = Mathf.Max(maxZ, world.z);
                        if (!bounds.Any(b => b.Contains(new NavPoint(world.x, world.z)))) allVerticesInside = false;
                    }
                    boundReport.AppendLine($"{mesh.name}: X[{minX:F3},{maxX:F3}] Z[{minZ:F3},{maxZ:F3}]");
                }
                var allBlockBounds = string.Join(";", bounds.Select(b => $"[{b.MinX},{b.MaxX}]x[{b.MinZ},{b.MaxZ}]"));
                Assert.IsTrue(allVerticesInside, $"Factory mesh AABBs (world X/Z):\n{boundReport}Allowed block AABBs: {allBlockBounds}");
                var after = bounds.Select(b => string.Join(",", b.Footprint.Vertices.Select(p => p.X + "/" + p.Z)) + "/" + b.Top).ToArray();
                CollectionAssert.AreEqual(before, after, "Presentation generation must not edit simulation solids.");
            }
            finally
            {
                foreach (var mesh in meshes) if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                if (material) UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void GeneratorRetainsExactFloatBitsWhenCombiningParts()
        {
            // Generator-only control: no PlayableWorld, camera, Render or global lighting changes.
            var root = new GameObject("Foundry exact attribute generator control");
            var meshes = new System.Collections.Generic.List<Mesh>();
            var material = new Material(Resources.Load<Shader>("TerritoryFog"));
            try
            {
                var map = (FoundryMap)FoundryGreybox.LoadProfile().AuthoredMap;
                var surface = FoundrySurfaceProfile.Load();
                FoundryIndustrialLandmarks.BuildPerPartForMeasurement(root.transform, material, map, surface, meshes);
                var parts = SelectLandmarkRenderers(root, "unoptimized factory-");
                foreach (var renderer in parts) renderer.gameObject.SetActive(false);
                FoundryIndustrialLandmarks.Build(root.transform, material, map, surface, meshes);
                var combined = SelectLandmarkRenderers(root, null);
                Assert.AreEqual(20, combined.Length);
                Assert.Greater(parts.Length, 100);
                var output = Environment.GetEnvironmentVariable("SPACEWARS_FOUNDRY_EVIDENCE");
                if (string.IsNullOrEmpty(output)) output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.local/factory-float-bits"));
                Directory.CreateDirectory(output);
                var original = new[] { new Vector3(1f, 0f, 0f) };
                var adjacent = new[] { new Vector3(BitConverter.ToSingle(BitConverter.GetBytes(FloatBits(1f) + 1), 0), 0f, 0f) };
                CollectionAssert.AreNotEqual(VectorBits(original), VectorBits(adjacent), "The bit comparison must detect a one-ULP change.");
                bool vectorComparerAcceptsAdjacent = Is.EqualTo(original).ApplyTo(adjacent).IsSuccess;
                File.WriteAllText(Path.Combine(output, "comparer-control.txt"),
                    "NUnit vector array equality accepts adjacent 1.0f float: " + vectorComparerAcceptsAdjacent +
                    "\nOriginal bits: " + FloatBits(original[0].x).ToString("X8") +
                    "\nAdjacent bits: " + FloatBits(adjacent[0].x).ToString("X8") + "\n");
                // Preserve all components before any assertion can stop at the first mismatch.
                using (var dump = new StreamWriter(Path.Combine(output, "mesh-float-bits.tsv")))
                {
                    dump.WriteLine("mesh\tvertex\tpx\tpy\tpz\tnx\tny\tnz\tu\tv");
                    foreach (var mesh in meshes)
                    {
                        var positions = mesh.vertices; var normals = mesh.normals; var uv = mesh.uv;
                        for (int i = 0; i < positions.Length; i++)
                            dump.WriteLine(mesh.name + "\t" + i + "\t" + string.Join("\t", new[] {
                                FloatBits(positions[i].x), FloatBits(positions[i].y), FloatBits(positions[i].z),
                                FloatBits(normals[i].x), FloatBits(normals[i].y), FloatBits(normals[i].z),
                                FloatBits(uv[i].x), FloatBits(uv[i].y) }.Select(bits => bits.ToString("X8"))));
                    }
                }
                using (var dump = new StreamWriter(Path.Combine(output, "mesh-indices.tsv")))
                {
                    dump.WriteLine("mesh\tindices");
                    foreach (var mesh in meshes) dump.WriteLine(mesh.name + "\t" + string.Join(",", mesh.triangles));
                }
                using (var dump = new StreamWriter(Path.Combine(output, "renderer-semantics.tsv")))
                {
                    dump.WriteLine("renderer\tmaterial\tbaseColorBits\tsurfaceRoleBits\tshadowCasting\treceiveShadows\tlightProbes\treflectionProbes");
                    foreach (var renderer in parts.Concat(combined))
                    {
                        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                        var color = block.GetColor("_BaseColor");
                        dump.WriteLine(renderer.name + "\t" + renderer.sharedMaterial.GetInstanceID() + "\t" +
                            string.Join(",", new[] { FloatBits(color.r), FloatBits(color.g), FloatBits(color.b), FloatBits(color.a) }.Select(bits => bits.ToString("X8"))) + "\t" +
                            FloatBits(block.GetFloat("_SurfaceRole")).ToString("X8") + "\t" + renderer.shadowCastingMode + "\t" + renderer.receiveShadows + "\t" + renderer.lightProbeUsage + "\t" + renderer.reflectionProbeUsage);
                    }
                }
                AssertEquivalentAttributes(parts, combined);
            }
            finally
            {
                foreach (var mesh in meshes) if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                if (material) UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MatchedPresentationHarnessCapturesSourceAndCandidateWithCostManifest()
        {
            var output = Environment.GetEnvironmentVariable("SPACEWARS_FOUNDRY_EVIDENCE");
            if (string.IsNullOrEmpty(output)) output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.local/add-foundry-industrial-landmarks"));
            Directory.CreateDirectory(output);

            var oldQuality = QualitySettings.GetQualityLevel();
            var oldQualityPipeline = QualitySettings.renderPipeline;
            var oldAA = QualitySettings.antiAliasing;
            var oldLinear = GraphicsSettings.lightsUseLinearIntensity;
            var oldTemperature = GraphicsSettings.lightsUseColorTemperature;
            var oldAmbient = RenderSettings.ambientLight;
            var oldFog = RenderSettings.fog;
            var oldTimeScale = Time.timeScale;
            File.WriteAllText(Path.Combine(output, "original-global-state.txt"),
                "quality=" + QualitySettings.names[oldQuality] + " pipeline=" + AssetDatabase.GetAssetPath(oldQualityPipeline) +
                " AA=" + oldAA + " linear=" + oldLinear + " temperature=" + oldTemperature);
            var root = new GameObject("Foundry industrial presentation harness - NOT A MATCH");
            var target = new GameObject("Foundry matched review camera");
            var lightObject = new GameObject("Foundry matched review key light");
            var targetTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            targetTexture.Create();
            PlayableWorld world = null;
            var generatedMeshes = new System.Collections.Generic.List<Mesh>();
            try
            {
                int ultra = Array.IndexOf(QualitySettings.names, "Ultra");
                Assert.GreaterOrEqual(ultra, 0);
                QualitySettings.SetQualityLevel(ultra, true);
                var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Spacewars/Content/FoundationPipeline.asset");
                Assert.NotNull(pipeline);
                QualitySettings.renderPipeline = pipeline;
                RenderSettings.ambientLight = new Color(.65f, .65f, .66f);
                RenderSettings.fog = false; // The isolated harness has no owner visibility mask; material role remains fog-capable.
                Time.timeScale = 0;
                var key = lightObject.AddComponent<Light>();
                key.type = LightType.Directional; key.intensity = 1.4f; key.transform.rotation = Quaternion.Euler(55, -30, 0);
                var playable = FoundryGreybox.LoadProfile();
                var map = (FoundryMap)playable.AuthoredMap;
                world = new PlayableWorld(root.transform, playable);
                world.InspectionSites(); world.InspectionReveal();
                foreach (var lava in root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.gameObject.name == "Lava river / fissure"))
                    lava.enabled = false; // Freeze an unrelated animated surface so complete frames can be compared exactly.
                // Integration reconcile: current PlayableWorld contains the new helper.
                // Disable it and build the frozen 14b76fa0 hall/annex reference in this
                // test-only harness. Runtime source no longer needs legacy objects.
                var live = SelectLandmarkRenderers(root, null);
                Assert.AreEqual(20, live.Length, "Current world must wire all four optimized factories.");
                var material = live[0].sharedMaterial;
                Assert.AreSame(Resources.Load<Shader>("TerritoryFog"), material.shader, "Qualify the actual runtime shader.");
                Assert.Greater(material.GetVector("_ArtDetail").x, 0, "Retain the original texture detail.");
                Assert.Greater(material.GetVector("_ArtDetail").y, 0, "Retain active micro-relief in the exact pixel comparison.");
                foreach (var renderer in live) renderer.gameObject.SetActive(false);
                var baseline = CreateLegacyPlaceholders(world.GraphicsRoot, material, map, generatedMeshes);
                Assert.AreEqual(8, baseline.Length, "Frozen reference contains four hall/annex pairs.");
                var camera = target.AddComponent<Camera>();
                camera.targetTexture = targetTexture; camera.orthographic = true;
                camera.nearClipPlane = .1f; camera.farClipPlane = 800; camera.backgroundColor = new Color(.08f, .09f, .1f);
                ConfigureCloseCamera(camera);
                File.WriteAllText(Path.Combine(output, "pre-pin-state.txt"), MatchedState(camera, key, targetTexture, material, baseline));
                // Match the bundled URP constructor/per-frame normalization before
                // the first Render. Read this actual asset; never edit its settings.
                var serializedPipeline = new SerializedObject(pipeline);
                var msaa = serializedPipeline.FindProperty("m_MSAA"); Assert.NotNull(msaa);
                var matchedAA = msaa.intValue; Assert.Contains(matchedAA, new[] { 1, 2, 4, 8 });
                var matchedLinear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                const bool matchedTemperature = true;
                QualitySettings.antiAliasing = matchedAA;
                GraphicsSettings.lightsUseLinearIntensity = matchedLinear;
                GraphicsSettings.lightsUseColorTemperature = matchedTemperature;
                File.WriteAllText(Path.Combine(output, "after-pin-state.txt"), MatchedState(camera, key, targetTexture, material, baseline));
                string Render(string phase, MeshRenderer[] selected)
                {
                    Assert.AreSame(pipeline, QualitySettings.renderPipeline);
                    Assert.AreEqual(matchedAA, QualitySettings.antiAliasing);
                    Assert.AreEqual(matchedLinear, GraphicsSettings.lightsUseLinearIntensity);
                    Assert.AreEqual(matchedTemperature, GraphicsSettings.lightsUseColorTemperature);
                    string before = MatchedState(camera, key, targetTexture, material, selected);
                    File.WriteAllText(Path.Combine(output, phase + "-before-state.txt"), before);
                    camera.Render();
                    string after = MatchedState(camera, key, targetTexture, material, selected);
                    File.WriteAllText(Path.Combine(output, phase + "-after-state.txt"), after);
                    // The source render is also the original warmup; retain its readback
                    // without assuming pipeline initialization has already completed.
                    if (!phase.StartsWith("source", StringComparison.Ordinal)) Assert.AreEqual(before, after, phase + " effective state changed during Render");
                    return before;
                }
                ConfigureCloseCamera(camera);
                Render("source-close-warmup", baseline);
                SaveFrame(targetTexture, Path.Combine(output, "source-close-presentation-harness.png"));
                var sourceCost = Measure(baseline);
                ConfigureMapCamera(camera); Render("source-map", baseline);
                SaveFrame(targetTexture, Path.Combine(output, "source-map-scale-presentation-harness.png"));

                foreach (var renderer in baseline) renderer.gameObject.SetActive(false);
                FoundryIndustrialLandmarks.BuildPerPartForMeasurement(world.GraphicsRoot, material, map, FoundrySurfaceProfile.Load(), generatedMeshes);
                var unoptimized = SelectLandmarkRenderers(root, "unoptimized factory-");
                Assert.Greater(unoptimized.Length, 100, "Detailed pre-optimization keeps individual fixed parts as separate renderers.");
                ConfigureCloseCamera(camera);
                var detailedCloseState = Render("detailed-close", unoptimized);
                var unoptimizedClosePixels = SaveFrame(targetTexture, Path.Combine(output, "detailed-unoptimized-close-presentation-harness.png"));
                ConfigureMapCamera(camera); var detailedMapState = Render("detailed-map", unoptimized);
                var unoptimizedMapPixels = SaveFrame(targetTexture, Path.Combine(output, "detailed-unoptimized-map-scale-presentation-harness.png"));
                var unoptimizedCost = Measure(unoptimized);
                foreach (var renderer in unoptimized) renderer.gameObject.SetActive(false);

                FoundryIndustrialLandmarks.Build(world.GraphicsRoot, material, map, FoundrySurfaceProfile.Load(), generatedMeshes);
                var optimized = SelectLandmarkRenderers(root, null);
                Assert.AreEqual(20, optimized.Length, "Runtime keeps five material/property-block renderers within each of four factory bounds.");
                AssertEquivalentAttributes(unoptimized, optimized);
                ConfigureCloseCamera(camera);
                var optimizedCloseState = Render("optimized-close", optimized);
                var optimizedClosePixels = SaveFrame(targetTexture, Path.Combine(output, "optimized-candidate-close-presentation-harness.png"));
                var closeDifference = ComparePixels(unoptimizedClosePixels, optimizedClosePixels);
                ConfigureMapCamera(camera); var optimizedMapState = Render("optimized-map", optimized);
                var optimizedMapPixels = SaveFrame(targetTexture, Path.Combine(output, "optimized-candidate-map-scale-presentation-harness.png"));
                var mapDifference = ComparePixels(unoptimizedMapPixels, optimizedMapPixels);
                File.WriteAllText(Path.Combine(output, "image-diff-diagnostics.json"), JsonUtility.ToJson(new ImageDiffDocument {
                    closeView = closeDifference, mapScaleView = mapDifference,
                    comparison = "Full-frame exact Color32 arrays; explicit current FoundationPipeline/Ultra, observed effective AA/light state held identical; original shadow/probe semantics; lava disabled/timeScale zero."
                }, true));
                Assert.AreEqual(detailedCloseState, optimizedCloseState, "Close known render inputs/renderer semantics must match exactly (bounds/counts differ by design).");
                Assert.AreEqual(detailedMapState, optimizedMapState, "Map known render inputs/renderer semantics must match exactly (bounds/counts differ by design).");
                CollectionAssert.AreEqual(unoptimizedClosePixels, optimizedClosePixels, "Close camera: optimized static geometry must render exactly like the same unoptimized parts.");
                CollectionAssert.AreEqual(unoptimizedMapPixels, optimizedMapPixels, "Map-scale camera: optimized static geometry must render exactly like the same unoptimized parts.");
                var optimizedCost = Measure(optimized);
                var report = new MetricsDocument {
                    sourcePlaceholders = sourceCost,
                    detailedBeforeOptimization = unoptimizedCost,
                    detailedAfterOptimization = optimizedCost,
                    sourceToDetailed = Change(sourceCost, unoptimizedCost),
                    detailedToOptimized = Change(unoptimizedCost, optimizedCost),
                    closeViewImageDifference = closeDifference, mapScaleImageDifference = mapDifference,
                    note = "Matched Unity Editor presentation harness on current Foundry map/profile with an explicit frozen 14b76fa0 placeholder reference; identical camera, lighting and material. Animated lava was disabled and Time.timeScale held at zero for exact full-frame comparison. Presentation harness only; not an ordinary match, gameplay witness, or acceptance frame. Renderer submissions are a one-material-slot-per-renderer estimate. Runtime mesh memory is Profiler.GetRuntimeMemorySizeLong."
                };
                File.WriteAllText(Path.Combine(output, "metrics.json"), JsonUtility.ToJson(report, true));
            }
            finally
            {
                foreach (var mesh in generatedMeshes) if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                if (world != null) world.Dispose();
                RenderSettings.ambientLight = oldAmbient; RenderSettings.fog = oldFog; Time.timeScale = oldTimeScale;
                QualitySettings.SetQualityLevel(oldQuality, true); QualitySettings.renderPipeline = oldQualityPipeline;
                QualitySettings.antiAliasing = oldAA; GraphicsSettings.lightsUseLinearIntensity = oldLinear;
                GraphicsSettings.lightsUseColorTemperature = oldTemperature;
                var camera = target ? target.GetComponent<Camera>() : null;
                if (camera) camera.targetTexture = null;
                targetTexture.Release(); UnityEngine.Object.DestroyImmediate(targetTexture);
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(lightObject);
            }
        }

        private static string MatchedState(Camera camera, Light key, RenderTexture target, Material material, MeshRenderer[] renderers)
        {
            var pipeline = QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline;
            var sh = RenderSettings.ambientProbe;
            var coefficients = Enumerable.Range(0, 3).SelectMany(c => Enumerable.Range(0, 9).Select(i => sh[c, i].ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            // Uniform semantic set only: expected submission counts and bounds are measured separately.
            var semantics = renderers.Select(r => string.Join("/", r.shadowCastingMode, r.receiveShadows, r.lightProbeUsage,
                r.reflectionProbeUsage, r.motionVectorGenerationMode, r.allowOcclusionWhenDynamic, r.renderingLayerMask,
                r.probeAnchor ? r.probeAnchor.GetInstanceID() : 0, r.lightProbeProxyVolumeOverride ? r.lightProbeProxyVolumeOverride.GetInstanceID() : 0))
                .Distinct().OrderBy(x => x, StringComparer.Ordinal);
            return "quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()] + " AA=" + QualitySettings.antiAliasing +
                " shadows=" + QualitySettings.shadows + "/" + QualitySettings.shadowResolution + "/" + QualitySettings.shadowProjection + "/" + QualitySettings.shadowDistance + "/" + QualitySettings.shadowCascades +
                " linear=" + GraphicsSettings.lightsUseLinearIntensity + " temperature=" + GraphicsSettings.lightsUseColorTemperature +
                " colorspace=" + QualitySettings.activeColorSpace + " pipeline=" + AssetDatabase.GetAssetPath(pipeline) + "\n" + EditorJsonUtility.ToJson(pipeline) +
                "\nambient=" + RenderSettings.ambientMode + "/" + RenderSettings.ambientLight.ToString("R") + "/" + RenderSettings.ambientSkyColor.ToString("R") + "/" + RenderSettings.ambientEquatorColor.ToString("R") + "/" + RenderSettings.ambientGroundColor.ToString("R") + "/" + RenderSettings.ambientIntensity +
                " SH=" + string.Join(",", coefficients) + " fog=" + RenderSettings.fog + " timeScale=" + Time.timeScale +
                "\nlight=" + EditorJsonUtility.ToJson(key) + " transform=" + key.transform.position.ToString("R") + "/" + key.transform.rotation.ToString("R") +
                "\ncamera=" + camera.transform.position.ToString("R") + "/" + camera.transform.rotation.ToString("R") + "/" + camera.orthographic + "/" + camera.orthographicSize + "/" + camera.nearClipPlane + "/" + camera.farClipPlane + "/" + camera.backgroundColor.ToString("R") + "/" + camera.allowHDR + "/" + camera.allowMSAA + "/" + camera.clearFlags + "/" + camera.cullingMask +
                "\ntarget=" + target.width + "/" + target.height + "/" + target.depth + "/" + target.format + "/" + target.antiAliasing + "/" + target.sRGB +
                "\nmaterial=" + EditorJsonUtility.ToJson(material) + "\nrendererSemantics=" + string.Join(";", semantics);
        }

        private static MeshRenderer[] CreateLegacyPlaceholders(Transform parent,Material material,FoundryMap map,System.Collections.Generic.List<Mesh> ownedMeshes)
        {
            var renderers=new System.Collections.Generic.List<MeshRenderer>();
            // Frozen 14b76fa0 reference dimensions, rather than current art defaults.
            const float crownRelief=1.5f,industrialHeight=4;
            void Prism(string name,NavObstacle footprint,double bottom,double top,Color color)
            {
                var obj=new GameObject(name);obj.transform.SetParent(parent,false);
                var mesh=TerrainMesh.Prism(footprint,bottom,top);ownedMeshes.Add(mesh);obj.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",color);block.SetFloat("_SurfaceRole",12);renderer.SetPropertyBlock(block);renderers.Add(renderer);
            }
            foreach(var rock in map.Solids.Skip(map.Solids.Count-4))
            {
                double x=(rock.MinX+rock.MaxX)/2,z=(rock.MinZ+rock.MaxZ)/2,w=(rock.MaxX-rock.MinX)*.33,d=(rock.MaxZ-rock.MinZ)*.35;
                Prism("Foundry grey industrial hall",new NavObstacle(x-w,z-d,x+w*.55,z+d),rock.Top-crownRelief,rock.Top+industrialHeight,new Color(.43f,.45f,.46f));
                Prism("Foundry grey industrial annex",new NavObstacle(x+w*.55,z-d*.75,x+w,z+d*.4),rock.Top-crownRelief,rock.Top+industrialHeight*.6f,new Color(.37f,.39f,.40f));
            }
            return renderers.ToArray();
        }

        private static void ConfigureCloseCamera(Camera camera)
        {
            camera.orthographicSize = 22;
            camera.transform.position = new Vector3(115, 112, -21);
            camera.transform.LookAt(new Vector3(75, 22, 114));
        }

        private static void ConfigureMapCamera(Camera camera)
        {
            camera.orthographicSize = 145;
            camera.transform.position = new Vector3(0, 320, 0);
            camera.transform.rotation = Quaternion.Euler(90, 0, 0);
        }

        private static MeshRenderer[] SelectLandmarkRenderers(GameObject root, string suffix)
        {
            return root.GetComponentsInChildren<MeshRenderer>(true).Where(r =>
            {
                string name = r.gameObject.name;
                bool landmark = name.StartsWith("Foundry industrial walls", StringComparison.Ordinal) ||
                                name.StartsWith("Foundry oxidized roof", StringComparison.Ordinal) ||
                                name.StartsWith("Foundry pipes and crane", StringComparison.Ordinal) ||
                                name.StartsWith("Foundry weathered concrete", StringComparison.Ordinal) ||
                                name.StartsWith("Foundry dark loading bay", StringComparison.Ordinal);
                return landmark && r.gameObject.activeInHierarchy && (suffix == null || name.Contains(suffix));
            }).ToArray();
        }

        private static Color32[] SaveFrame(RenderTexture target, string path)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                return image.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static PixelDifference ComparePixels(Color32[] before, Color32[] after)
        {
            Assert.AreEqual(before.Length, after.Length);
            int changedPixels = 0, maximum = 0;
            long channelDelta = 0;
            for (int i = 0; i < before.Length; i++)
            {
                int dr = Mathf.Abs(before[i].r - after[i].r), dg = Mathf.Abs(before[i].g - after[i].g), db = Mathf.Abs(before[i].b - after[i].b);
                int pixelMax = Mathf.Max(dr, Mathf.Max(dg, db));
                if (pixelMax != 0) changedPixels++;
                maximum = Mathf.Max(maximum, pixelMax);
                channelDelta += dr + dg + db;
            }
            return new PixelDifference {
                samples = before.Length, changedPixels = changedPixels,
                changedPercent = changedPixels * 100f / before.Length,
                meanAbsoluteChannelDelta = (float)(channelDelta / (before.Length * 3.0)),
                maximumChannelDelta = maximum
            };
        }

        private static void AssertEquivalentAttributes(MeshRenderer[] before, MeshRenderer[] after)
        {
            string[] groups = { "Foundry industrial walls", "Foundry oxidized roof", "Foundry pipes and crane", "Foundry weathered concrete", "Foundry dark loading bay" };
            for (int factory = 1; factory <= 4; factory++) foreach (string group in groups)
            {
                var parts = before.Where(r => r.gameObject.name.StartsWith(group + " unoptimized factory-" + factory + " ", StringComparison.Ordinal))
                    .OrderBy(r => r.gameObject.name, StringComparer.Ordinal).ToArray();
                var combined = after.Single(r => r.gameObject.name == group + " factory " + factory);
                Assert.Greater(parts.Length, 0, group + " factory " + factory + " must contain source parts.");
                Assert.IsTrue(parts.All(r => r.sharedMaterial == combined.sharedMaterial));
                var beforeBlock = new MaterialPropertyBlock(); var afterBlock = new MaterialPropertyBlock();
                parts[0].GetPropertyBlock(beforeBlock); combined.GetPropertyBlock(afterBlock);
                Assert.AreEqual(beforeBlock.GetColor("_BaseColor"), afterBlock.GetColor("_BaseColor"));
                Assert.AreEqual(beforeBlock.GetFloat("_SurfaceRole"), afterBlock.GetFloat("_SurfaceRole"));
                Assert.AreEqual(parts[0].shadowCastingMode, combined.shadowCastingMode);
                Assert.AreEqual(parts[0].receiveShadows, combined.receiveShadows);
                Assert.AreEqual(parts[0].transform.localPosition, combined.transform.localPosition);
                Assert.AreEqual(parts[0].transform.localRotation, combined.transform.localRotation);
                Assert.AreEqual(parts[0].transform.localScale, combined.transform.localScale);
                AssertMeshAttributes(parts.Select(r => r.GetComponent<MeshFilter>().sharedMesh).ToArray(), combined.GetComponent<MeshFilter>().sharedMesh);
            }
        }

        private static void AssertMeshAttributes(Mesh[] parts, Mesh combined)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();
            foreach (var part in parts)
            {
                int offset = vertices.Count;
                vertices.AddRange(part.vertices); normals.AddRange(part.normals); uv.AddRange(part.uv);
                triangles.AddRange(part.triangles.Select(index => index + offset));
            }
            CollectionAssert.AreEqual(vertices, combined.vertices, "Positions must be retained in generator order.");
            CollectionAssert.AreEqual(normals, combined.normals, "Normals must be retained exactly.");
            CollectionAssert.AreEqual(uv, combined.uv, "UVs must be retained exactly.");
            CollectionAssert.AreEqual(VectorBits(vertices), VectorBits(combined.vertices), combined.name + ": position float bits must be retained in generator order.");
            CollectionAssert.AreEqual(VectorBits(normals), VectorBits(combined.normals), combined.name + ": normal float bits must be retained exactly.");
            CollectionAssert.AreEqual(uv.SelectMany(v => new[] { FloatBits(v.x), FloatBits(v.y) }),
                combined.uv.SelectMany(v => new[] { FloatBits(v.x), FloatBits(v.y) }), combined.name + ": UV float bits must be retained exactly.");
            CollectionAssert.AreEqual(triangles, combined.triangles, "Indices must be retained with only their expected base offsets.");
            Assert.AreEqual(combined.vertexCount, combined.normals.Length);
            Assert.AreEqual(combined.vertexCount, combined.uv.Length);
        }

        private static int FloatBits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        private static System.Collections.Generic.IEnumerable<int> VectorBits(System.Collections.Generic.IEnumerable<Vector3> values)
            => values.SelectMany(v => new[] { FloatBits(v.x), FloatBits(v.y), FloatBits(v.z) });

        private static MeshCost Measure(MeshRenderer[] renderers)
        {
            var meshes = renderers.Select(r => r.GetComponent<MeshFilter>().sharedMesh).ToArray();
            return new MeshCost {
                renderers = renderers.Length,
                meshes = meshes.Select(m => m.GetInstanceID()).Distinct().Count(),
                materialSlots = renderers.Sum(r => r.sharedMaterials.Length),
                uniqueMaterials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.GetInstanceID()).Distinct().Count(),
                estimatedSubmissions = renderers.Sum(r => r.sharedMaterials.Length),
                vertices = meshes.Sum(m => m.vertexCount),
                triangles = meshes.Sum(m => (int)m.GetIndexCount(0) / 3),
                runtimeMeshBytes = meshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m))
            };
        }

        private static MetricChange Change(MeshCost from, MeshCost to) => new MetricChange {
            renderers = Percent(from.renderers, to.renderers), meshes = Percent(from.meshes, to.meshes),
            submissions = Percent(from.estimatedSubmissions, to.estimatedSubmissions), vertices = Percent(from.vertices, to.vertices),
            triangles = Percent(from.triangles, to.triangles), runtimeMeshBytes = Percent(from.runtimeMeshBytes, to.runtimeMeshBytes)
        };
        private static float Percent(long from, long to) => from == 0 ? 0 : (float)((to - from) * 100.0 / from);

        [Serializable] private sealed class MetricsDocument
        {
            public MeshCost sourcePlaceholders, detailedBeforeOptimization, detailedAfterOptimization;
            public MetricChange sourceToDetailed, detailedToOptimized;
            public PixelDifference closeViewImageDifference, mapScaleImageDifference;
            public string note;
        }
        [Serializable] private sealed class ImageDiffDocument { public PixelDifference closeView, mapScaleView; public string comparison; }
        [Serializable] private sealed class PixelDifference { public int samples, changedPixels, maximumChannelDelta; public float changedPercent, meanAbsoluteChannelDelta; }
        [Serializable] private sealed class MetricChange
        {
            public float renderers, meshes, submissions, vertices, triangles, runtimeMeshBytes;
        }
        [Serializable] private sealed class MeshCost
        {
            public int renderers, meshes, materialSlots, uniqueMaterials, estimatedSubmissions, vertices, triangles;
            public long runtimeMeshBytes;
        }

        private static float GetPropertyBlockFloat(Renderer renderer, string property)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            return block.GetFloat(property);
        }
    }
}

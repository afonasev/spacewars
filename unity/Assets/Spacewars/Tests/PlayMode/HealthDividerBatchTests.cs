using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class HealthDividerBatchTests
    {
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private System.Action restorePreferences;

        [SetUp] public void CapturePreferences() { restorePreferences = NativePreferenceWriter.Capture(); }
        [TearDown] public void RestorePreferences() { restorePreferences(); PlayerPrefs.Save(); }

        private static int Rebuilds(PlayableWorld.Actor actor)
        {
            var batch = typeof(PlayableWorld.Actor).GetField("HealthDividerBatch", InstancePrivate).GetValue(actor);
            return (int)batch.GetType().GetProperty("RebuildCount", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(batch);
        }

        private static MeshRenderer BatchRenderer(PlayableWorld.Actor actor) =>
            actor.Health.parent.GetComponentsInChildren<MeshRenderer>(true)
                .Single(r => r.gameObject.name == "Batched health dividers");

        private static Transform[] Dividers(PlayableWorld.Actor actor) =>
            (Transform[])typeof(PlayableWorld.Actor).GetField("HealthDividers", InstancePrivate).GetValue(actor);

        private static Transform HealthBackground(PlayableWorld.Actor actor) =>
            (Transform)typeof(PlayableWorld.Actor).GetField("HealthBackground", InstancePrivate).GetValue(actor);

        private static Color[] CaptureFloat(Camera camera, RenderTexture target)
        {
            camera.Render();
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                return image.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
            }
        }

        private static int CountDividerDifferences(Color[] withDividers, Color[] withoutDividers, Camera camera, RenderTexture target, Transform bar, float width, float height)
        {
            var center = camera.WorldToScreenPoint(bar.position);
            var right = camera.WorldToScreenPoint(bar.position + camera.transform.right * width * .5f);
            var up = camera.WorldToScreenPoint(bar.position + camera.transform.up * height * .5f);
            int halfWidth = Mathf.CeilToInt(Mathf.Abs(right.x - center.x)) + 2;
            int halfHeight = Mathf.CeilToInt(Mathf.Abs(up.y - center.y)) + 2;
            int xMin = Mathf.Clamp(Mathf.FloorToInt(center.x) - halfWidth, 0, target.width);
            int xMax = Mathf.Clamp(Mathf.CeilToInt(center.x) + halfWidth, 0, target.width);
            int yMin = Mathf.Clamp(Mathf.FloorToInt(center.y) - halfHeight, 0, target.height);
            int yMax = Mathf.Clamp(Mathf.CeilToInt(center.y) + halfHeight, 0, target.height);
            int changed = 0;
            for (int y = yMin; y < yMax; y++)
            for (int x = xMin; x < xMax; x++)
            {
                int index = y * target.width + x;
                if ((withDividers[index] - withoutDividers[index]).maxColorComponent > 0.01f ||
                    (withoutDividers[index] - withDividers[index]).maxColorComponent > 0.01f) changed++;
            }
            return changed;
        }

        private static void SaveDisplay(Camera camera, RenderTexture target, string path)
        {
            camera.Render();
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
            }
        }

        [UnityTest]
        public IEnumerator PreservesDividerGeometryBindingsAndOnlyRebuildsChangedLayouts()
        {
            var host = new GameObject("health divider batch regression");
            var cameraObject = new GameObject("health divider camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1.1f;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .035f, .05f);
            camera.transform.position = new Vector3(0, 0, -12);
            var keyLightObject = new GameObject("health divider key light");
            var keyLight = keyLightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, .98f, .94f);
            keyLight.intensity = 2f;
            keyLight.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBFloat);
            target.Create();
            camera.targetTexture = target;
            var world = new PlayableWorld(host.transform, PlayableProfile.Default);
            var actor = world.Tank(901, true);
            world.Fog.ShowPublic();
            var settings = NativePreferenceWriter.Capture();
            try
            {
                PlayableWorld.UpdateHealth(actor, camera, .67f, true);
                camera.transform.position = actor.Health.position - camera.transform.forward * 12f;
                PlayableWorld.UpdateHealth(actor, camera, .67f, true);
                for (int i = 0; i < world.GraphicsRoot.childCount; i++)
                {
                    var child = world.GraphicsRoot.GetChild(i);
                    if (child != actor.Health.parent) child.gameObject.SetActive(false);
                }
                var batchRenderer = BatchRenderer(actor);
                var sources = Dividers(actor);
                Assert.AreEqual(PlayableProfile.Default.HealthBarCells - 1, sources.Length);
                Assert.True(sources.All(t => t != null), "Public divider transforms remain available");
                Assert.AreEqual(sources.Length, actor.Health.parent.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.enabled == false && r.gameObject.name.StartsWith("Health divider ")));
                Assert.True(batchRenderer.enabled);

                var source = sources.Select(t => t.GetComponent<MeshFilter>().sharedMesh).ToArray();
                var mesh = batchRenderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreEqual(source.Sum(m => m.vertexCount), mesh.vertexCount);
                Assert.AreEqual(source.Sum(m => m.triangles.Length), mesh.triangles.Length);
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var uv = mesh.uv;
                var expectedBounds = new Bounds(Vector3.zero, Vector3.zero);
                bool hasBounds = false;
                int offset = 0;
                for (int divider = 0; divider < sources.Length; divider++)
                {
                    var original = source[divider];
                    var transform = Matrix4x4.TRS(sources[divider].localPosition, sources[divider].localRotation, sources[divider].localScale);
                    var originalVertices = original.vertices;
                    var originalNormals = original.normals;
                    var originalUv = original.uv;
                    for (int i = 0; i < originalVertices.Length; i++, offset++)
                    {
                        var expectedVertex = transform.MultiplyPoint3x4(originalVertices[i]);
                        Assert.AreEqual(expectedVertex, vertices[offset]);
                        if (!hasBounds) { expectedBounds = new Bounds(expectedVertex, Vector3.zero); hasBounds = true; }
                        else expectedBounds.Encapsulate(expectedVertex);
                        Assert.That((transform.inverse.transpose.MultiplyVector(originalNormals[i]).normalized - normals[offset]).sqrMagnitude, Is.LessThan(1e-12f));
                        Assert.AreEqual(originalUv[i], uv[offset]);
                    }
                }
                Assert.That((mesh.bounds.center - expectedBounds.center).sqrMagnitude, Is.LessThan(1e-12f), "Combined mesh bounds retain the exact source geometry envelope");
                Assert.That((mesh.bounds.size - expectedBounds.size).sqrMagnitude, Is.LessThan(1e-12f));

                var global = new MaterialPropertyBlock();
                var sourceRenderer = sources[0].GetComponent<MeshRenderer>();
                sourceRenderer.GetPropertyBlock(global);
                var copiedGlobal = new MaterialPropertyBlock();
                batchRenderer.GetPropertyBlock(copiedGlobal);
                Assert.AreEqual(global.GetColor("_BaseColor"), copiedGlobal.GetColor("_BaseColor"));
                var indexed = new MaterialPropertyBlock();
                sourceRenderer.GetPropertyBlock(indexed, 0);
                Assert.True(indexed.isEmpty);
                var copiedIndexed = new MaterialPropertyBlock();
                batchRenderer.GetPropertyBlock(copiedIndexed, 0);
                Assert.True(copiedIndexed.isEmpty, "An absent indexed block stays absent");
                Assert.AreEqual(sourceRenderer.sharedMaterial, batchRenderer.sharedMaterial);
                Assert.AreEqual(sourceRenderer.shadowCastingMode, batchRenderer.shadowCastingMode);
                Assert.AreEqual(sourceRenderer.receiveShadows, batchRenderer.receiveShadows);
                Assert.AreEqual(sourceRenderer.lightProbeUsage, batchRenderer.lightProbeUsage);
                Assert.AreEqual(sourceRenderer.reflectionProbeUsage, batchRenderer.reflectionProbeUsage);
                Assert.AreEqual(sourceRenderer.probeAnchor, batchRenderer.probeAnchor);
                Assert.AreEqual(sourceRenderer.motionVectorGenerationMode, batchRenderer.motionVectorGenerationMode);
                Assert.AreEqual(sourceRenderer.allowOcclusionWhenDynamic, batchRenderer.allowOcclusionWhenDynamic);
                Assert.AreEqual(sourceRenderer.renderingLayerMask, batchRenderer.renderingLayerMask);
                Assert.AreEqual(sourceRenderer.sortingLayerID, batchRenderer.sortingLayerID);
                Assert.AreEqual(sourceRenderer.sortingOrder, batchRenderer.sortingOrder);
                foreach (var divider in sources)
                {
                    var sourceBindings = divider.GetComponent<MeshRenderer>();
                    CollectionAssert.AreEqual(sourceRenderer.sharedMaterials, sourceBindings.sharedMaterials);
                    var perRenderer = new MaterialPropertyBlock();
                    var referenceProperties = new MaterialPropertyBlock();
                    sourceRenderer.GetPropertyBlock(perRenderer);
                    sourceBindings.GetPropertyBlock(referenceProperties);
                    Assert.AreEqual(perRenderer.GetColor("_BaseColor"), referenceProperties.GetColor("_BaseColor"));
                    perRenderer.Clear();
                    referenceProperties.Clear();
                    sourceRenderer.GetPropertyBlock(perRenderer, 0);
                    sourceBindings.GetPropertyBlock(referenceProperties, 0);
                    Assert.AreEqual(perRenderer.isEmpty, referenceProperties.isEmpty);
                }

                string evidence = System.Environment.GetEnvironmentVariable("SPACEWARS_HEALTH_DIVIDER_OUTPUT");
                Assert.IsNotEmpty(evidence, "Set a task-owned output folder for exact captures");
                var batchedPixels = CaptureFloat(camera, target);
                SaveDisplay(camera, target, Path.Combine(evidence, "batched.png"));
                batchRenderer.enabled = false;
                foreach (var divider in sources) divider.GetComponent<MeshRenderer>().enabled = true;
                var originalPixels = CaptureFloat(camera, target);
                SaveDisplay(camera, target, Path.Combine(evidence, "original.png"));
                CollectionAssert.AreEqual(originalPixels, batchedPixels, "Original nine cube renderers and the production batch match in full RGBAFloat output");
                foreach (var divider in sources) divider.GetComponent<MeshRenderer>().enabled = false;
                batchRenderer.enabled = false;
                var noDividerPixels = CaptureFloat(camera, target);
                SaveDisplay(camera, target, Path.Combine(evidence, "no-dividers-control.png"));
                int dividerDifferences = CountDividerDifferences(originalPixels, noDividerPixels, camera, target, actor.Health.parent,
                    HealthBackground(actor).localScale.x, HealthBackground(actor).localScale.y);
                Debug.Log("HEALTH_DIVIDER_VISUAL_CONTROL roi_changed_pixels=" + dividerDifferences + " orthographic_size=" + camera.orthographicSize + " light_intensity=" + keyLight.intensity);
                Assert.Greater(dividerDifferences, 0, "Negative control must remove visible divider pixels inside the health-bar ROI");

                batchRenderer.enabled = true;
                CollectionAssert.AreEqual(batchedPixels, CaptureFloat(camera, target), "P/restored-P rendering is byte-exact");

                int stable = Rebuilds(actor);
                for (int frame = 0; frame < 4; frame++) PlayableWorld.UpdateHealth(actor, camera, .67f, true);
                Assert.AreEqual(stable, Rebuilds(actor), "Unchanged frames do not rebuild the persistent mesh");
                actor.Root.position += new Vector3(1.25f, 0, 2.5f);
                camera.transform.rotation = Quaternion.Euler(0, 0, 7);
                PlayableWorld.UpdateHealth(actor, camera, .67f, true);
                Assert.AreEqual(stable, Rebuilds(actor), "Root movement and billboard rotation do not rebuild local mesh geometry");

                camera.orthographicSize = 2.135f;
                PlayableWorld.UpdateHealth(actor, camera, .67f, true);
                Assert.Greater(Rebuilds(actor), stable, "A changed pixel-quantized layout rebuilds");
                int zoomChanged = Rebuilds(actor);
                camera.pixelRect = new Rect(0, 0, 640, 360);
                PlayableWorld.UpdateHealth(actor, camera, .67f, true);
                Assert.Greater(Rebuilds(actor), zoomChanged, "A changed camera viewport pixel size rebuilds the pixel-quantized layout");
                int changed = Rebuilds(actor);
                NativeUserSettings.AlwaysHealth = false;
                PlayableWorld.UpdateHealth(actor, camera, 1f, false);
                Assert.False(actor.Health.parent.gameObject.activeSelf);
                Assert.AreEqual(changed, Rebuilds(actor), "Hidden health keeps its latest mesh without rebuilding");
                PlayableWorld.UpdateHealth(actor, camera, .5f, false);
                Assert.True(actor.Health.parent.gameObject.activeSelf);
                Assert.AreEqual(changed, Rebuilds(actor), "Reveal restores the current unchanged geometry");
                yield return null;
            }
            finally
            {
                settings();
                camera.targetTexture = null;
                world.Dispose();
                Object.Destroy(host);
                Object.Destroy(cameraObject);
                Object.Destroy(keyLightObject);
                target.Release();
                Object.Destroy(target);
            }
        }

        [Test]
        public void RemoveClearAndDisposeReleaseBatchesAndRestoreSourceRenderers()
        {
            var host = new GameObject("health divider lifecycle regression");
            var world = new PlayableWorld(host.transform, PlayableProfile.Default);
            bool disposed = false;
            try
            {
                var removed = world.Tank(911, true);
                var removedSources = Dividers(removed).Select(t => t.GetComponent<MeshRenderer>()).ToArray();
                world.Remove(911);
                Assert.True(removedSources.All(r => r.enabled), "Remove restores source renderer state before releasing its batch");

                var cleared = world.Tank(912, true);
                var clearSources = Dividers(cleared).Select(t => t.GetComponent<MeshRenderer>()).ToArray();
                world.Clear();
                Assert.True(clearSources.All(r => r.enabled), "Clear releases each batch and restores its source renderers");

                var finalActor = world.Tank(913, true);
                var disposeSources = Dividers(finalActor).Select(t => t.GetComponent<MeshRenderer>()).ToArray();
                world.Dispose();
                disposed = true;
                Assert.True(disposeSources.All(r => r.enabled), "Dispose releases batches through Clear");
            }
            finally
            {
                if (!disposed) world.Dispose();
                Object.DestroyImmediate(host);
            }
        }
    }
}

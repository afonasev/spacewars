using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class OrbitalReinforcementRenderingTests
    {
        [UnityTest]
        public IEnumerator MaterializationPulsesAroundEachUnitAndRestoresOriginalMaterials()
        {
            var profile = PlayableProfile.Default;
            var root = new GameObject("Reinforcement rendering witness");
            var target = new RenderTexture(960, 600, 24);
            var pixels = new Texture2D(960, 600, TextureFormat.RGB24, false);
            using var world = new PlayableWorld(root.transform, profile);
            try
            {
                world.InspectionReveal();
                var camera = new GameObject("Reinforcement camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform);
                camera.transform.position = new Vector3(4, 10, -8);
                camera.transform.LookAt(Vector3.zero);
                camera.orthographic = true;
                camera.orthographicSize = 3.6f;
                camera.targetTexture = target;
                var light = new GameObject("Reinforcement witness light").AddComponent<Light>();
                light.transform.SetParent(root.transform);
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.transform.rotation = Quaternion.Euler(58, -35, 0);

                var quote = OrbitalReinforcement.Quote(profile, 1.2, false, false);
                var points = OrbitalReinforcement.Layout(default, quote,
                    PlayableUnitRules.Radius(profile, quote.Kind), _ => true, out bool valid);
                Assert.True(valid);
                var actors = points.Select((point, index) =>
                {
                    var actor = world.Tank(index + 1, true, quote.Kind);
                    actor.Root.position = world.Point(point);
                    actor.Health.parent.gameObject.SetActive(false);
                    return actor;
                }).ToArray();
                var hull = actors[0].Hull.GetComponentsInChildren<MeshRenderer>().First(renderer =>
                    renderer.enabled && renderer.sharedMaterial != null);
                var original = hull.sharedMaterial;
                void Phase(double progress)
                {
                    for (int i = 0; i < actors.Length; i++)
                        world.UpdateReinforcement(actors[i], new PlayableEntitySnapshot(i + 1, PlayableOwner.Player,
                            quote.Kind, points[i], 100, false, 0, 0, 0, materializationProgress:progress));
                }
                byte[] Frame()
                {
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 960, 600), 0, 0);
                        pixels.Apply();
                        return pixels.EncodeToPNG();
                    }
                    finally { RenderTexture.active = previous; }
                }
                Phase(.2);
                yield return null;
                yield return null;
                Assert.AreNotSame(original, hull.sharedMaterial);
                var cylinders = actors.Select(actor => actor.Root.Find("Reinforcement Warp Cylinder").GetComponent<MeshRenderer>()).ToArray();
                Assert.That(cylinders.All(cylinder => cylinder.sharedMaterial.shader.name == "Spacewars/ReinforcementWarp"));
                Assert.That(cylinders.All(cylinder => cylinder.sharedMaterial.shader.isSupported));
                var properties = new MaterialPropertyBlock();
                int progress = Shader.PropertyToID("_Progress");
                foreach (var cylinder in cylinders)
                {
                    cylinder.GetPropertyBlock(properties);
                    Assert.AreEqual(.2f, properties.GetFloat(progress), 1e-5f);
                }
                Assert.Less(hull.sharedMaterial.GetFloat("_ZWrite"), 1);
                var arriving = Frame();
                Phase(.55);
                yield return null;
                yield return null;
                Assert.AreSame(cylinders[0], actors[0].Root.Find("Reinforcement Warp Cylinder").GetComponent<MeshRenderer>());
                cylinders[0].GetPropertyBlock(properties);
                Assert.AreEqual(.55f, properties.GetFloat(progress), 1e-5f);
                var pulsing = Frame();
                Assert.False(arriving.SequenceEqual(pulsing), "materialization advances the rendered effect");
                Phase(1);
                yield return null;
                yield return null;
                Assert.AreSame(original, hull.sharedMaterial);
                Assert.That(actors.All(actor => actor.Root.Find("Reinforcement Warp Cylinder") == null));
                var ready = Frame();
                Assert.False(arriving.SequenceEqual(ready), "materializing and ready frames differ");
                string output = System.Environment.GetEnvironmentVariable("SPACEWARS_REINFORCEMENT_QA");
                if (!string.IsNullOrEmpty(output))
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllBytes(Path.Combine(output, "materializing-editor.png"), arriving);
                    File.WriteAllBytes(Path.Combine(output, "pulsing-editor.png"), pulsing);
                    File.WriteAllBytes(Path.Combine(output, "ready-editor.png"), ready);
                }
            }
            finally
            {
                Object.Destroy(root);
                target.Release();
                Object.Destroy(target);
                Object.Destroy(pixels);
            }
        }
    }
}

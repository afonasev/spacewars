using System.Collections;
using System.IO;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class OrbitalStasisRenderingTests
    {
        [UnityTest]
        public IEnumerator RendersTransparentCylinderAndRemovesItOnThaw()
        {
            var profile = PlayableProfile.Default;
            var root = new GameObject("Stasis rendering witness");
            var target = new RenderTexture(960, 600, 24);
            var pixels = new Texture2D(960, 600, TextureFormat.RGB24, false);
            using var world = new PlayableWorld(root.transform, profile);
            try
            {
                world.InspectionReveal();
                var camera = new GameObject("Stasis camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform);
                camera.transform.position = new Vector3(4, 10, -8);
                camera.transform.LookAt(Vector3.zero);
                camera.orthographic = true;
                camera.orthographicSize = 2.5f;
                camera.targetTexture = target;
                var light = new GameObject("Stasis witness light").AddComponent<Light>();
                light.transform.SetParent(root.transform);
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.transform.rotation = Quaternion.Euler(58, -35, 0);
                var actor = world.Tank(1, true, PlayableEntityKind.Explorer);
                actor.Root.position = world.Point(default);
                actor.Health.parent.gameObject.SetActive(false);

                PlayableEntitySnapshot Snapshot(double remaining) => new PlayableEntitySnapshot(
                    1, PlayableOwner.Player, PlayableEntityKind.Explorer, default, 100, false, 0, 0, 0,
                    stasisRemaining: remaining);

                world.UpdateStasis(actor, Snapshot(7));
                yield return null;
                yield return null;
                var cylinder = actor.Root.Find("Stasis Cylinder");
                Assert.NotNull(cylinder);
                var material = cylinder.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.AreEqual("Spacewars/OrbitalStasis", material.shader.name);
                Assert.True(material.shader.isSupported);
                var output = System.Environment.GetEnvironmentVariable("SPACEWARS_STASIS_QA");
                if (!string.IsNullOrEmpty(output))
                {
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 960, 600), 0, 0);
                        pixels.Apply();
                        File.WriteAllBytes(Path.Combine(output, "stasis-editor.png"), pixels.EncodeToPNG());
                    }
                    finally { RenderTexture.active = previous; }
                }
                world.UpdateStasis(actor, Snapshot(0));
                yield return null;
                Assert.IsNull(actor.Root.Find("Stasis Cylinder"));
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

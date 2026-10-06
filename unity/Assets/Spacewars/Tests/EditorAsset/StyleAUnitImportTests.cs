using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Spacewars.Tests.EditorAsset
{
    public sealed class StyleAUnitImportTests
    {
        private const string Source = "Assets/Spacewars/Content/StyleA/";
        private const string Prefabs = "Assets/Spacewars/Content/Resources/StyleA/";
        private static readonly string[] Units = { "tank", "explorer", "shkval" };

        [TestCase("tank")][TestCase("explorer")][TestCase("shkval")]
        public void ApprovedSourceBytesAndGltfNodesAreRetained(string unit)
        {
            var manifest = File.ReadAllText(Source + "provenance.json");
            foreach (var file in new[] { unit + ".glb", unit + "-detail.png" })
            {
                var match = Regex.Match(manifest, "\\\"" + Regex.Escape(file) + "\\\"\\s*:\\s*\\\"([0-9a-f]{64})\\\"");
                Assert.IsTrue(match.Success, "Missing provenance: " + file);
                var unityBytes = File.ReadAllBytes(Source + file);
                using (var sha = SHA256.Create())
                    Assert.AreEqual(match.Groups[1].Value, BitConverter.ToString(sha.ComputeHash(unityBytes)).Replace("-", "").ToLowerInvariant(), file);
            }

            var glb = File.ReadAllBytes(Source + unit + ".glb");
            Assert.AreEqual(0x46546c67, BitConverter.ToInt32(glb, 0), "glTF binary magic");
            Assert.AreEqual(2, BitConverter.ToInt32(glb, 4), "glTF version");
            Assert.AreEqual(glb.Length, BitConverter.ToInt32(glb, 8), "GLB byte length");
            Assert.AreEqual(0x4e4f534a, BitConverter.ToInt32(glb, 16), "JSON chunk");
            var json = Encoding.UTF8.GetString(glb, 20, BitConverter.ToInt32(glb, 12));
            StringAssert.Contains("\"name\":\"modelRoot\"", json);
            StringAssert.Contains("\"name\":\"turretYaw\"", json);
            StringAssert.Contains("\"name\":\"landmark__" + unit + "__", json);
            Assert.IsFalse(json.Contains("\"extensionsRequired\""), "Required extension needs separate importer proof");
        }

        [TestCase("tank", 1f, -0.07874534f, 1.03f, 0f)]
        [TestCase("explorer", .86f, -.00055913f, 1.0712f, -.28f)]
        [TestCase("shkval", 1f, -.0025f, .87f, -.65f)]
        public void ImportedPrefabPreservesRootPivotAndMeshReferences(string unit, float rootScale, float groundOffset, float turretY, float turretZ)
        {
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(Source + unit + ".glb");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + unit + ".prefab");
            Assert.IsNotNull(glb, unit + " GLB import");
            Assert.IsNotNull(prefab, unit + " resource prefab");
            Assert.AreEqual(unit, prefab.name);
            var root = prefab.transform.Cast<Transform>().Single(t => t.name == "modelRoot");
            Assert.That(root.localScale.x, Is.EqualTo(rootScale).Within(.0001f));
            Assert.That(root.localScale.y, Is.EqualTo(rootScale).Within(.0001f));
            Assert.That(root.localScale.z, Is.EqualTo(rootScale).Within(.0001f));
            Assert.That(root.localPosition.y, Is.EqualTo(groundOffset).Within(.0001f));
            var turret = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "turretYaw");
            Assert.That(turret.localPosition.y, Is.EqualTo(turretY).Within(.0001f));
            Assert.That(turret.localPosition.z, Is.EqualTo(turretZ).Within(.0001f));
            Assert.Greater(turret.childCount, 0, "Independent aiming descendants");
            var meshes = root.GetComponentsInChildren<MeshFilter>(true);
            Assert.Greater(meshes.Length, 3);
            foreach (var filter in meshes)
            {
                Assert.IsNotNull(filter.sharedMesh, filter.name);
                Assert.Greater(filter.sharedMesh.vertexCount, 0, filter.name);
                Assert.AreEqual(Source + unit + ".glb", AssetDatabase.GetAssetPath(filter.sharedMesh), filter.name);
            }
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), "Simulation owns collision; import must add none");
            Assert.IsEmpty(prefab.GetComponentsInChildren<LODGroup>(true), "No authored LOD exists for this unit");
            var importer = AssetImporter.GetAtPath(Source + unit + ".glb");
            Assert.IsNotNull(importer);
            Assert.AreEqual("ScriptedImporter", importer.GetType().BaseType.Name, "glTFast importer expected");
        }

        [TestCase("tank")][TestCase("explorer")][TestCase("shkval")]
        public void UrpRolesAndTextureImportSettingsAreCoherent(string unit)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + unit + ".prefab");
            Assert.IsNotNull(prefab);
            var roles = prefab.GetComponentsInChildren<MeshRenderer>(true)
                .SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            var expected = unit == "explorer"
                ? new[] { "neutral-metal", "structure", "system-emissive", "team-primary", "copper-metal" }
                : new[] { "neutral-metal", "structure", "system-emissive", "team-primary" };
            CollectionAssert.AreEquivalent(expected, roles.Select(m => m.name).ToArray());
            foreach (var role in roles)
            {
                Assert.AreEqual(Prefabs + unit + "-" + role.name + ".mat", AssetDatabase.GetAssetPath(role));
                Assert.AreEqual("Universal Render Pipeline/Lit", role.shader.name);
                Assert.IsTrue(role.shader.isSupported, role.name);
                if (!new[] { "neutral-metal", "structure", "team-primary" }.Contains(role.name)) continue;
                Assert.IsNotNull(role.GetTexture("_BaseMap"), role.name + " base color");
                Assert.IsNotNull(role.GetTexture("_BumpMap"), role.name + " normal");
                Assert.IsNotNull(role.GetTexture("_DetailNormalMap"), role.name + " detail normal");
                Assert.IsNotNull(role.GetTexture("_MetallicGlossMap"), role.name + " packed mask");
                Assert.AreEqual(Source + unit + "-detail.png", AssetDatabase.GetAssetPath(role.GetTexture("_DetailNormalMap")));
                Assert.AreEqual(Prefabs + unit + "-" + role.name + "-mask.png", AssetDatabase.GetAssetPath(role.GetTexture("_MetallicGlossMap")));
            }
            CheckTexture(Source + "style-a-painted-metal-base-color.png", true, false, false);
            CheckTexture(Source + "style-a-painted-metal-structure-base-color.png", true, false, false);
            CheckTexture(Source + "style-a-painted-metal-normal.png", false, true, false);
            CheckTexture(Source + unit + "-detail.png", false, true, true);
            CheckTexture(Source + "style-a-painted-metal-orm.png", false, false, false);
            foreach (var role in new[] { "neutral-metal", "structure", "team-primary" })
                CheckTexture(Prefabs + unit + "-" + role + "-mask.png", false, false, false);
        }

        private static void CheckTexture(string path, bool srgb, bool normal, bool convertedHeight)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(importer, path);
            Assert.AreEqual(srgb, importer.sRGBTexture, path + " sRGB");
            Assert.AreEqual(normal ? TextureImporterType.NormalMap : TextureImporterType.Default, importer.textureType, path + " type");
            if (normal) Assert.AreEqual(convertedHeight, importer.convertToNormalmap, path + " conversion");
        }
    }
}

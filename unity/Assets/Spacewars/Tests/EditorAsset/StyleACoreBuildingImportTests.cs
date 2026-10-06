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
    public sealed class StyleACoreBuildingImportTests
    {
        private const string Source = "Assets/Spacewars/Content/StyleA/";
        private const string Prefabs = "Assets/Spacewars/Content/Resources/StyleA/";

        [TestCase("headquarters", "antennaYaw", 1.62f)]
        [TestCase("factory", null, 0f)]
        [TestCase("refinery", null, 0f)]
        public void ApprovedBytesAndImportedPrefabContract(string building, string articulatedNode, float articulatedY)
        {
            var manifest = File.ReadAllText(Source + "provenance.json");
            foreach (var file in new[] { building + ".glb", building + "-detail.png" })
            {
                var match = Regex.Match(manifest, "\\\"" + Regex.Escape(file) + "\\\"\\s*:\\s*\\\"([0-9a-f]{64})\\\"");
                Assert.IsTrue(match.Success, "Missing approved provenance: " + file);
                var bytes = File.ReadAllBytes(Source + file);
                using (var sha = SHA256.Create())
                    Assert.AreEqual(match.Groups[1].Value, BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(), file);
            }

            var glb = File.ReadAllBytes(Source + building + ".glb");
            Assert.AreEqual(0x46546c67, BitConverter.ToInt32(glb, 0));
            Assert.AreEqual(2, BitConverter.ToInt32(glb, 4), "glTF 2.0 metres");
            Assert.AreEqual(glb.Length, BitConverter.ToInt32(glb, 8));
            Assert.AreEqual(0x4e4f534a, BitConverter.ToInt32(glb, 16));
            var json = Encoding.UTF8.GetString(glb, 20, BitConverter.ToInt32(glb, 12));
            StringAssert.Contains("\"name\":\"modelRoot\"", json);
            StringAssert.Contains("\"name\":\"landmark__" + (building == "headquarters" ? "hq" : building) + "__", json);
            Assert.IsFalse(json.Contains("\"extensionsRequired\""));
            if (articulatedNode != null) StringAssert.Contains("\"name\":\"" + articulatedNode + "\"", json);

            var glbPath = Source + building + ".glb";
            var meta = File.ReadAllText(glbPath + ".meta");
            StringAssert.Contains("ScriptedImporter:", meta);
            StringAssert.Contains("nodeNameMethod: 1", meta);
            StringAssert.Contains("KHR_materials_emissive_strength", meta);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(glbPath));
            var importer = AssetImporter.GetAtPath(glbPath);
            Assert.IsNotNull(importer);
            Assert.AreEqual("ScriptedImporter", importer.GetType().BaseType.Name, "glTFast importer");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + building + ".prefab");
            Assert.IsNotNull(prefab);
            Assert.AreEqual(building, prefab.name);
            var root = prefab.transform.Cast<Transform>().Single(t => t.name == "modelRoot");
            Assert.That(root.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(root.localScale, Is.EqualTo(Vector3.one));
            if (articulatedNode != null)
            {
                var node = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == articulatedNode);
                Assert.That(node.localPosition.y, Is.EqualTo(articulatedY).Within(.0001f));
                Assert.Greater(node.childCount, 0, "Antenna remains an independent assembly");
            }
            var meshes = root.GetComponentsInChildren<MeshFilter>(true);
            Assert.Greater(meshes.Length, 3);
            foreach (var filter in meshes)
            {
                Assert.IsNotNull(filter.sharedMesh, filter.name);
                Assert.Greater(filter.sharedMesh.vertexCount, 0, filter.name);
                Assert.AreEqual(glbPath, AssetDatabase.GetAssetPath(filter.sharedMesh), filter.name);
            }
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true), "Simulation owns collision");
            Assert.IsEmpty(prefab.GetComponentsInChildren<LODGroup>(true), "Authored LOD is absent");
        }

        [TestCase("headquarters", false)]
        [TestCase("factory", false)]
        [TestCase("refinery", true)]
        public void UrpRolesAndTextureImportAreCoherent(string building, bool copper)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + building + ".prefab");
            Assert.IsNotNull(prefab);
            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            Assert.Greater(renderers.Length, 3);
            foreach (var renderer in renderers)
                foreach (var material in renderer.sharedMaterials)
                    Assert.IsNotNull(material, renderer.name + " missing material");
            var roles = renderers.SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            var expected = new[] { "neutral-metal", "structure", "team-emissive", "foundation-concrete", "team-primary" }
                .Concat(copper ? new[] { "copper-metal" } : Array.Empty<string>());
            CollectionAssert.AreEquivalent(expected, roles.Select(m => m.name));
            foreach (var role in roles)
            {
                Assert.AreEqual(Prefabs + building + "-" + role.name + ".mat", AssetDatabase.GetAssetPath(role));
                Assert.AreEqual("Universal Render Pipeline/Lit", role.shader.name);
                Assert.IsTrue(role.shader.isSupported);
                if (!new[] { "neutral-metal", "structure", "team-primary" }.Contains(role.name)) continue;
                Assert.IsNotNull(role.GetTexture("_BaseMap"), role.name + " base map");
                Assert.IsNotNull(role.GetTexture("_BumpMap"), role.name + " normal");
                Assert.IsNotNull(role.GetTexture("_DetailNormalMap"), role.name + " detail");
                Assert.IsNotNull(role.GetTexture("_MetallicGlossMap"), role.name + " mask");
                Assert.AreEqual(Source + building + "-detail.png", AssetDatabase.GetAssetPath(role.GetTexture("_DetailNormalMap")));
                Assert.AreEqual(Prefabs + building + "-" + role.name + "-mask.png", AssetDatabase.GetAssetPath(role.GetTexture("_MetallicGlossMap")));
                CheckTexture(Prefabs + building + "-" + role.name + "-mask.png", false, false, false);
            }
            CheckTexture(Source + "style-a-painted-metal-base-color.png", true, false, false);
            CheckTexture(Source + "style-a-painted-metal-structure-base-color.png", true, false, false);
            CheckTexture(Source + "style-a-painted-metal-normal.png", false, true, false);
            CheckTexture(Source + building + "-detail.png", false, true, true);
            CheckTexture(Source + "style-a-painted-metal-orm.png", false, false, false);
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

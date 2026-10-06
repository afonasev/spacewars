using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Editor;
using Spacewars.StyleA.Preview;
using UnityEngine;

namespace Spacewars.Tests.EditorAsset
{
    public sealed class StyleALodAuditTests
    {
        [Test]
        public void InventoryCountsInstancesSeparatelyFromSharedResources()
        {
            var root = new GameObject("fixture");
            var mesh = new Mesh { name = "shared triangle" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var texture = new Texture2D(8, 4);
            material.SetTexture("_BaseMap", texture);
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    var child = new GameObject("mesh" + i); child.transform.SetParent(root.transform);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    child.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
                var report = StyleALodInventory.Inspect(new[] { root, root });
                var row = report.assets[0];
                Assert.AreEqual(2, row.meshInstances);
                Assert.AreEqual(1, row.uniqueMeshes);
                Assert.AreEqual(2, row.triangleInstances);
                Assert.AreEqual(1, row.uniqueMeshTriangles);
                Assert.AreEqual(6, row.vertexInstances);
                Assert.AreEqual(2, row.materialSlots);
                Assert.AreEqual(1, row.uniqueMaterials);
                Assert.AreEqual(1, report.globalUniqueMeshes);
                Assert.AreEqual(1, row.textures.Count(t => t.width == 8 && t.height == 4));
                Assert.AreEqual(row.textureRuntimeBytes, report.globalTextureRuntimeBytes, "Deduplicate across assets too");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void InventoryRejectsMissingPrefabRatherThanReportingZeroCost()
        {
            Assert.Throws<InvalidOperationException>(() => StyleALodInventory.Inspect(new GameObject[] { null }));
        }

        [Test]
        public void ApprovedNinePrefabInventoryHasGeometryAndExplicitAbsentLod()
        {
            var prefabs = StyleALodAuditProject.LoadPrefabs();
            CollectionAssert.AreEqual(StyleALodInventory.Names, prefabs.Select(p => p.name));
            var report = StyleALodInventory.Inspect(prefabs);
            Assert.AreEqual(9, report.assets.Length);
            foreach (var row in report.assets)
            {
                Assert.Greater(row.triangleInstances, 0, row.name);
                Assert.Greater(row.uniqueMeshRuntimeBytes, 0, row.name);
                Assert.Greater(row.textureRuntimeBytes, 0, row.name);
                Assert.AreEqual(row.meshInstances, row.renderers, row.name);
                Assert.AreEqual(0, row.lodGroups, "Baseline has no authored LOD: " + row.name);
            }
        }
    }
}

using System.Linq;
using NUnit.Framework;
using Spacewars.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Spacewars.Tests.EditorAsset
{
    // Current dependency binding, separate from the retained upstream fixture's
    // historical fixed counts. Does not modify source assets or their assertions.
    public sealed class FoundryCurrentResourceDependencyTests
    {
        [TestCase("shkval")] [TestCase("shkval-guidance-3")]
        public void CurrentShkvalResourceRetainsEveryCompleteSourceMeshBuffer(string key)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/StyleA/" + key + ".glb");
            Assert.NotNull(source);
            var prefab = Resources.Load<GameObject>("StyleA/" + key); Assert.NotNull(prefab);
            var instance = Object.Instantiate(prefab);
            try {
                var original = source.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                var prepared = instance.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh).ToArray();
                Assert.IsNotEmpty(original); Assert.AreEqual(original.Length, prepared.Length);
                Assert.True(original.Concat(prepared).All(m => m && m.isReadable));
                var originalKeys = original.Select(UnitModelResources.ExactMeshKey).ToArray();
                var preparedKeys = prepared.Select(UnitModelResources.ExactMeshKey).ToArray();
                Assert.True(originalKeys.Concat(preparedKeys).All(k => k != null));
                CollectionAssert.AreEquivalent(originalKeys, preparedKeys, "Every complete rigid source buffer, including bounds and attributes, must match.");
                int sourceTriangles = original.Sum(m => m.triangles.Length / 3), resourceTriangles = prepared.Sum(m => m.triangles.Length / 3);
                Assert.AreEqual(sourceTriangles, resourceTriangles);
                Assert.IsEmpty(instance.GetComponentsInChildren<Collider>(true));
                TestContext.WriteLine("CURRENT_SOURCE_RESOURCE key=" + key + " sourceTriangles=" + sourceTriangles + " resourceTriangles=" + resourceTriangles +
                    " meshInstances=" + prepared.Length + " completeBufferMultiset=exact; retained fixed-count fixture FAIL is not changed");
            } finally { Object.DestroyImmediate(instance); }
        }
    }
}

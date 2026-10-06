using System.Collections;
using NUnit.Framework;
using Spacewars.StyleA.Preview;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.MaterialImportPlayMode
{
    public sealed class MaterialImportIsolationTests
    {
        [UnityTest] public IEnumerator TemporaryRoleReplacementAndCleanupPreserveSharedPrefabMaterial()
        {
            var prefab=Resources.Load<GameObject>("StyleA/tank");Assert.IsNotNull(prefab);
            var original=prefab.GetComponentsInChildren<Renderer>(true)[0].sharedMaterial;
            var before=JsonUtility.ToJson(MaterialImportComparison.Read(original,"original"));
            var go=Object.Instantiate(prefab);
            var renderer=go.GetComponentsInChildren<Renderer>(true)[0];
            var clone=MaterialImportComparison.Clone(original,new MaterialSourceRow {name="test-system-emissive",sourceEmission=true},true,false);
            try
            {
                var slots=renderer.sharedMaterials;slots[0]=clone;renderer.sharedMaterials=slots;
                yield return null;
                Assert.AreSame(clone,renderer.sharedMaterials[0]);
                Assert.IsTrue(clone.IsKeywordEnabled("_EMISSION"));
                Assert.AreEqual(before,JsonUtility.ToJson(MaterialImportComparison.Read(original,"original")));
            }
            finally { Object.Destroy(go);Object.Destroy(clone); }
            yield return null;
            Assert.IsTrue(clone==null);Assert.IsNotNull(original);
            Assert.AreEqual(before,JsonUtility.ToJson(MaterialImportComparison.Read(original,"original")));
        }
    }
}

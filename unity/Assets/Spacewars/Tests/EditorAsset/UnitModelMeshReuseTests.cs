using NUnit.Framework;
using Spacewars.Editor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Spacewars.Tests.EditorAsset
{
    public sealed class UnitModelMeshReuseTests
    {
        static Mesh Triangle()=>new Mesh{vertices=new[]{Vector3.zero,Vector3.forward,Vector3.right},normals=new[]{Vector3.up,Vector3.up,Vector3.up},triangles=new[]{0,1,2},uv=new[]{Vector2.zero,Vector2.up,Vector2.right}};
        [Test] public void ExactReuseKeyDistinguishesCullingBoundsAndVertexAttributes()
        {
            var mesh=Triangle();
            try
            {
                string original=UnitModelResources.ExactMeshKey(mesh);var bounds=mesh.bounds;mesh.bounds=new Bounds(bounds.center+Vector3.up*.1f,bounds.size);Assert.AreNotEqual(original,UnitModelResources.ExactMeshKey(mesh));
                mesh.bounds=bounds;Assert.AreEqual(original,UnitModelResources.ExactMeshKey(mesh));mesh.uv2=new[]{Vector2.one,Vector2.up,Vector2.right};Assert.AreNotEqual(original,UnitModelResources.ExactMeshKey(mesh));
            }
            finally{Object.DestroyImmediate(mesh);}
        }
        [Test] public void NonReadableAndRiggedMeshesAreNotShared()
        {
            var mesh=Triangle();
            try{mesh.bindposes=new[]{Matrix4x4.identity};Assert.IsNull(UnitModelResources.ExactMeshKey(mesh));mesh.bindposes=new Matrix4x4[0];mesh.UploadMeshData(true);Assert.IsNull(UnitModelResources.ExactMeshKey(mesh));}
            finally{Object.DestroyImmediate(mesh);}
        }
        [Test] public void ImmutableSharedMeshRetainsOtherVariantAfterSourceMutationAndRegeneration()
        {
            const string folder="Assets/UnitMeshMutationIsolation";Assert.False(UnityEditor.AssetDatabase.IsValidFolder(folder));UnityEditor.AssetDatabase.CreateFolder("Assets","UnitMeshMutationIsolation");
            var source=Triangle();source.name="mutable-source";UnityEditor.AssetDatabase.CreateAsset(source,folder+"/source.asset");var a=new GameObject("base");var b=new GameObject("upgraded");
            try
            {
                string original=UnitModelResources.ExactMeshKey(source);var shared=UnitModelResources.ImmutableSharedMesh(source,folder+"/");Assert.AreNotSame(source,shared);
                a.AddComponent<MeshFilter>().sharedMesh=shared;b.AddComponent<MeshFilter>().sharedMesh=shared;
                source.vertices=new[]{Vector3.zero,Vector3.forward*2,Vector3.right};source.RecalculateBounds();UnityEditor.EditorUtility.SetDirty(source);UnityEditor.AssetDatabase.SaveAssets();UnityEditor.AssetDatabase.ImportAsset(folder+"/source.asset");
                Assert.AreEqual(original,UnitModelResources.ExactMeshKey(b.GetComponent<MeshFilter>().sharedMesh),"Mutable source import cannot change the second variant");
                var replacement=UnitModelResources.ImmutableSharedMesh(source,folder+"/");Assert.AreNotSame(shared,replacement);a.GetComponent<MeshFilter>().sharedMesh=replacement;
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(a,folder+"/base.prefab");UnityEditor.PrefabUtility.SaveAsPrefabAsset(b,folder+"/upgraded.prefab");UnityEditor.AssetDatabase.SaveAssets();UnityEditor.AssetDatabase.ImportAsset(folder+"/upgraded.prefab");
                var saved=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(folder+"/upgraded.prefab");Assert.AreEqual(original,UnitModelResources.ExactMeshKey(saved.GetComponent<MeshFilter>().sharedMesh));
                Assert.AreNotEqual(original,UnitModelResources.ExactMeshKey(a.GetComponent<MeshFilter>().sharedMesh));
            }
            finally{Object.DestroyImmediate(a);Object.DestroyImmediate(b);UnityEditor.AssetDatabase.DeleteAsset(folder);}
        }
        [Test] public void ExistingContentAddressedMeshIsValidatedAndNeverOverwritten()
        {
            const string folder="Assets/UnitMeshImmutableValidation";Assert.False(UnityEditor.AssetDatabase.IsValidFolder(folder));UnityEditor.AssetDatabase.CreateFolder("Assets","UnitMeshImmutableValidation");var source=Triangle();
            try
            {
                var shared=UnitModelResources.ImmutableSharedMesh(source,folder+"/");shared.vertices=new[]{Vector3.zero,Vector3.forward*3,Vector3.right};shared.RecalculateBounds();UnityEditor.EditorUtility.SetDirty(shared);UnityEditor.AssetDatabase.SaveAssets();string corrupted=UnitModelResources.ExactMeshKey(shared);
                Assert.Throws<System.InvalidOperationException>(()=>UnitModelResources.ImmutableSharedMesh(source,folder+"/"));Assert.AreEqual(corrupted,UnitModelResources.ExactMeshKey(shared),"Reject mismatch without rewriting existing content");
            }
            finally{Object.DestroyImmediate(source);UnityEditor.AssetDatabase.DeleteAsset(folder);}
        }
        [Test] public void PreparedVariantReuseIsIdempotent(){Assert.AreEqual(0,UnitModelResources.ReuseExactVariantMeshes());}
    }
}

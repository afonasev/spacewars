using System.Linq;
using NUnit.Framework;
using Spacewars.Editor;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Spacewars.Tests.EditorAsset
{
    public sealed class ActiveUnitResourceTests
    {
        [Test] public void ActiveRegistrySeparatesDefaultSpearheadLegacyAndStandalonePreview()
        {
            CollectionAssert.AreEqual(new[]{"tankHybrid","tankHybridUpgraded","explorer","explorerUpgraded","shkval","shkval-guidance-3","hawk"},UnitModelResources.Keys);
            CollectionAssert.AreEqual(new[]{"tank","tankUpgraded"},UnitModelResources.LegacyKeys);
            CollectionAssert.AreEqual(new[]{"explorer-twin-cannon"},UnitModelResources.PreviewKeys);
            Assert.AreEqual(UnitModelResources.AllKeys.Count(),UnitModelResources.AllKeys.Distinct().Count());
        }
        [TestCase("tankHybrid", "tankHybridUpgraded", 3, 51612, 58100)]
        [TestCase("shkval", "shkval-guidance-3", 18, 48892, 46536)]
        public void CurrentVariantsRetainExactSourceBuffersIndependentRootsAndSkinBindings(string basic,string upgraded,int shared,int basicTriangles,int upgradedTriangles)
        {
            var a=Object.Instantiate(Resources.Load<GameObject>("StyleA/"+basic));
            var b=Object.Instantiate(Resources.Load<GameObject>("StyleA/"+upgraded));
            try
            {
                Mesh[] Static(GameObject go)=>go.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray();
                Mesh[] All(GameObject go)=>Static(go).Concat(go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r=>r.sharedMesh)).ToArray();
                Assert.AreEqual(shared,Static(a).Intersect(Static(b)).Count());
                Assert.AreEqual(basicTriangles,All(a).Sum(m=>m.triangles.Length/3));Assert.AreEqual(upgradedTriangles,All(b).Sum(m=>m.triangles.Length/3));
                foreach(var go in new[]{a,b})
                {
                    string key=go==a?basic:upgraded;
                    var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spacewars/Content/StyleA/"+key+".glb");
                    Assert.NotNull(source);
                    var originals=source.GetComponentsInChildren<MeshFilter>(true).Select(f=>UnitModelResources.ExactMeshKey(f.sharedMesh)).ToArray();
                    foreach(var mesh in Static(go))
                    {
                        string exact=UnitModelResources.ExactMeshKey(mesh);Assert.NotNull(exact);
                        CollectionAssert.Contains(originals,exact,"Native GLB complete rigid buffer retained: "+mesh.name);
                    }
                    Assert.IsEmpty(go.GetComponentsInChildren<Collider>(true));
                    var skins=go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Assert.AreEqual(key.StartsWith("tankHybrid")?2:0,skins.Length);
                    foreach(var skin in skins)
                    {
                        StringAssert.EndsWith(key+"-"+skin.name+".asset",AssetDatabase.GetAssetPath(skin.sharedMesh));
                        Assert.AreEqual(42,skin.bones.Length);Assert.AreEqual(42,skin.sharedMesh.bindposes.Length);
                        Assert.True(skin.bones.All(t=>t.IsChildOf(go.transform)),"Bones must belong to this instance");
                        Assert.False(All(go==a?b:a).Contains(skin.sharedMesh),"Distinct authored track geometry is never aliased across variants");
                    }
                }
                var at=a.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="turretYaw");var bt=b.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="turretYaw");var rest=bt.localToWorldMatrix;
                at.localRotation=Quaternion.Euler(0,37,0);Assert.AreEqual(rest,bt.localToWorldMatrix);
                var ar=a.GetComponentsInChildren<Renderer>(true).First();var br=b.GetComponentsInChildren<Renderer>(true).First();var paint=new MaterialPropertyBlock();paint.SetColor("_BaseColor",Color.red);ar.SetPropertyBlock(paint);Assert.False(br.HasPropertyBlock());
            }
            finally{Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
        }
    }
}

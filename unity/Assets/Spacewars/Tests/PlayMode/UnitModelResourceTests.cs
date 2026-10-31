using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine;
using Object=UnityEngine.Object;

public sealed class UnitModelResourceTests
{
    [TestCase("tank","tankUpgraded",5,15960,17248)] // Legacy comparison only.
    [TestCase("explorer","explorerUpgraded",4,11578,11890)]
    [TestCase("shkval","shkval-guidance-3",18,48892,46536)]
    public void SharedVariantMeshesRetainIndependentArticulationAndOwnerPaint(string basic,string upgraded,int shared,int baseTriangles,int upgradedTriangles)
    {
        var a=Object.Instantiate(Resources.Load<GameObject>("StyleA/"+basic));var b=Object.Instantiate(Resources.Load<GameObject>("StyleA/"+upgraded));
        try
        {
            var am=a.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray();var bm=b.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).ToArray();
            Assert.AreEqual(shared,am.Intersect(bm).Count());Assert.AreEqual(baseTriangles,am.Sum(m=>m.triangles.Length/3));Assert.AreEqual(upgradedTriangles,bm.Sum(m=>m.triangles.Length/3));Assert.True(am.Concat(bm).All(m=>m.isReadable));
            var at=a.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="turretYaw");var bt=b.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="turretYaw");var before=bt.localToWorldMatrix;
            at.localRotation=Quaternion.Euler(0,37,0);Assert.AreEqual(before,bt.localToWorldMatrix,"Sharing mesh buffers must not share pivots");
            var finish=ActorMaterialProfile.Load();var ar=a.GetComponentsInChildren<MeshRenderer>(true);var br=b.GetComponentsInChildren<MeshRenderer>(true);var baseColors=ar.SelectMany(r=>r.sharedMaterials).Select(m=>m.GetColor("_BaseColor")).ToArray();
            foreach(var r in ar)for(int slot=0;slot<r.sharedMaterials.Length;slot++)finish.Apply(r,slot,Color.red);
            foreach(var r in br)Assert.False(r.HasPropertyBlock(),"Other instance must not inherit owner overrides");
            CollectionAssert.AreEqual(baseColors,ar.SelectMany(r=>r.sharedMaterials).Select(m=>m.GetColor("_BaseColor")).ToArray());
            Assert.IsEmpty(a.GetComponentsInChildren<Collider>(true));Assert.IsEmpty(b.GetComponentsInChildren<Collider>(true));
        }
        finally{Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
    }
    [Test] public void HawkStaticMeshesKeepVariantTogglesAndIndependentLaunchPulses()
    {
        var model=Object.Instantiate(Resources.Load<GameObject>("StyleA/hawk"));
        try
        {
            Assert.AreEqual(26,model.GetComponentsInChildren<MeshRenderer>(true).Length);
            var visual=model.AddComponent<HawkModelPresentation>();visual.SetUpgrade(false);
            int Active()=>model.GetComponentsInChildren<MeshRenderer>().Count(r=>r.enabled);
            Assert.AreEqual(13,Active());visual.SetUpgrade(true);Assert.AreEqual(22,Active());
            var nodes=model.GetComponentsInChildren<Transform>(true);foreach(string name in new[]{"rackLeft","rackRight","podLeft","podRight","targetingHead","reloadLeft","reloadRight","nozzleLeft","nozzleRight","mainRotor","tailRotor","muzzleLeft","muzzleRight"})Assert.AreEqual(1,nodes.Count(t=>t.name==name));
            var socket=nodes.Single(t=>t.name=="muzzleLeft");var matrix=socket.localToWorldMatrix;visual.Trigger(0,4);visual.Trigger(1,4);visual.Render(.25,4+visual.Profile.launchSeconds*.5,9,-12,2160);
            Assert.AreEqual(24,Active());Assert.AreEqual(matrix,socket.localToWorldMatrix);Assert.AreEqual(2,nodes.Count(t=>t.name.StartsWith("Hawk launch pulse")));Assert.False(model.GetComponentsInChildren<Collider>(true).Any(c=>c.enabled));
        }
        finally{Object.DestroyImmediate(model);}
    }
}

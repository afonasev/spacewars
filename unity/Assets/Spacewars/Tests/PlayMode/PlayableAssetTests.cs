using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Spacewars.Presentation;
using Spacewars.Simulation;

public sealed class PlayableAssetTests
{
    [TestCase("ScientificCenter",false)][TestCase("Refinery",true)]
    public void BuildingOwnerPaintPreservesSystemEmissionAndSharedAssets(string kind,bool upgraded)
    {
        var parent=new GameObject("building material audit");
        try
        {
            using var world=new PlayableWorld(parent.transform,PlayableProfile.Default);
            var ally=world.Building(1,kind,true,upgraded);var enemy=world.Building(2,kind,false,upgraded);
            var a=ally.Hull.GetComponentsInChildren<Renderer>().First(r=>r.sharedMaterial.name.EndsWith("team-primary"));
            var e=enemy.Hull.GetComponentsInChildren<Renderer>().First(r=>r.sharedMaterial.name.EndsWith("team-primary"));
            var ap=new MaterialPropertyBlock();var ep=new MaterialPropertyBlock();a.GetPropertyBlock(ap,0);e.GetPropertyBlock(ep,0);
            Assert.AreNotEqual(ap.GetColor("_BaseColor"),ep.GetColor("_BaseColor"));Assert.AreSame(a.sharedMaterial,e.sharedMaterial);
            if(kind=="ScientificCenter")
            {
                var system=ally.Hull.GetComponentsInChildren<Renderer>().First(r=>r.sharedMaterial.name.EndsWith("system-emissive"));
                var block=new MaterialPropertyBlock();system.GetPropertyBlock(block,0);Assert.IsTrue(block.isEmpty);
                Assert.IsTrue(system.sharedMaterial.IsKeywordEnabled("_EMISSION"));
                Assert.AreEqual(new Color(0,.57f,1.02f,1),system.sharedMaterial.GetColor("_EmissionColor"));
            }
            else Assert.IsNotNull(ally.Turbine);
        }
        finally{Object.DestroyImmediate(parent);}
    }
    [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
    public void IntegratedResearchVariantsPreserveActorAndAuthoredPivots(PlayableEntityKind kind)
    {
        var parent=new GameObject("integrated upgrade material audit");
        try
        {
            using var world=new PlayableWorld(parent.transform,PlayableProfile.Default);
            var actor=world.Tank(1,true,kind);var root=actor.Root;
            var entity=new PlayableEntitySnapshot(1,PlayableOwner.Player,kind,default,100,false,0,0,0,upgraded:true);
            world.UpdateResearchModel(actor,entity);
            Assert.AreSame(root,actor.Root);Assert.True(actor.UnitUpgraded);Assert.NotNull(actor.Turret);
            if(kind==PlayableEntityKind.Shkval)Assert.NotNull(actor.Launcher);
            foreach(var renderer in actor.Hull.GetComponentsInChildren<Renderer>())
                foreach(var material in renderer.sharedMaterials)
                    if(material.GetColor("_EmissionColor").maxColorComponent>0)Assert.True(material.IsKeywordEnabled("_EMISSION"),material.name);
        }
        finally{Object.DestroyImmediate(parent);}
    }
    [Test] public void PackagedProfileRoundtripsAndMatchesDefaultFields()
    {
        var resource=Resources.Load<TextAsset>("PlayableProfile");Assert.IsNotNull(resource);
        var data=JsonUtility.FromJson<PlayableProfileData>(resource.text);
        var roundtrip=JsonUtility.FromJson<PlayableProfileData>(JsonUtility.ToJson(data));
        var defaults=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,null);
        Assert.AreEqual(PlayableProfile.RequiredProfileId,PlayableProfile.Create(roundtrip).ProfileId);
        foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("artillery.")||f.Path.StartsWith("explorer.")||f.Path.StartsWith("science.")||f.Path.StartsWith("vision.")||f.Path.StartsWith("map.")||f.Path.StartsWith("army.")||f.Path.Contains("buildingSale")||f.Path.Contains("buildingRepair")||f.Path.Contains("lifecycleMarker")||f.Path.Contains("saleMarker")))
        {Assert.AreEqual(field.Read(defaults),field.Read(roundtrip),field.Path);Assert.Greater(field.Step,0);Assert.IsNotEmpty(field.Description);}
    }
    [TestCase("shkval")][TestCase("explorer")][TestCase("tank")][TestCase("headquarters")][TestCase("factory")][TestCase("refinery")][TestCase("outpost")][TestCase("mine")][TestCase("scientificCenter")][TestCase("refineryUpgraded")]
    public void ImportedGameModelHasGeometryAndUsableMaterials(string name)
    {
        var prefab=Resources.Load<GameObject>("StyleA/"+name);Assert.IsNotNull(prefab);
        Assert.IsTrue(prefab.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="modelRoot"));
        var meshes=prefab.GetComponentsInChildren<MeshFilter>(true);Assert.Greater(meshes.Length,3);
        Assert.IsTrue(meshes.All(m=>m.sharedMesh&&m.sharedMesh.vertexCount>0));
        foreach(var renderer in prefab.GetComponentsInChildren<Renderer>(true))foreach(var material in renderer.sharedMaterials)
        {Assert.IsNotNull(material);Assert.IsTrue(material.shader.isSupported);Assert.AreEqual("Universal Render Pipeline/Lit",material.shader.name);}
    }
    [TestCase(PlayableEntityKind.Shkval)][TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)] public void ExistingTankPivotKeepsIndependentWorldAimAndOwnerPaint(PlayableEntityKind kind)
    {
        var parent=new GameObject("asset test");
        try
        {
            using var world=new PlayableWorld(parent.transform,PlayableProfile.Default);var ally=world.Tank(1,true,kind);var enemy=world.Tank(2,false,kind);
            Assert.IsTrue(ally.Turret.IsChildOf(ally.Hull));
            ally.Hull.localRotation=Quaternion.Euler(0,90,0);ally.Turret.localRotation=Quaternion.Euler(0,-90,0);
            Assert.Less(Vector3.Distance(ally.Hull.forward,Vector3.right),.0001f);
            Assert.Less(Vector3.Distance(ally.Turret.forward,Vector3.forward),.0001f);
            var a=ally.Root.GetComponentsInChildren<Renderer>().First(r=>r.sharedMaterial.name.EndsWith("team-primary"));
            var e=enemy.Root.GetComponentsInChildren<Renderer>().First(r=>r.sharedMaterial.name.EndsWith("team-primary"));
            var ap=new MaterialPropertyBlock();var ep=new MaterialPropertyBlock();a.GetPropertyBlock(ap,0);e.GetPropertyBlock(ep,0);
            Assert.AreNotEqual(ap.GetColor("_BaseColor"),ep.GetColor("_BaseColor"));Assert.AreSame(a.sharedMaterial,e.sharedMaterial);
        }
        finally{Object.DestroyImmediate(parent);}
    }
}

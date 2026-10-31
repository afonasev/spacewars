using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
namespace Spacewars.Tests.PlayMode
{
    public sealed class UnitWreckPresentationTests
    {
        GameObject host;PlayableWorld world;
        static PlayableWreckSnapshot Wreck(long destroyed=-1)=>new PlayableWreckSnapshot(41,PlayableOwner.Player,PlayableEntityKind.Tank,default,.75,.72,0,.1,1,0,0,5,905,965,destroyed);
        static PlayableSnapshot Frame(long tick,PlayableWreckSnapshot[] wrecks=null,TeamVisionSnapshot vision=null,long generation=1,bool paused=false,PlayableSoundEvent[] sounds=null)
            =>new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,generation,7,1,tick,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision,sounds:sounds,wrecks:wrecks);
        GameObject Model()=>host.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Unit wreck 41").gameObject;
        [SetUp]public void Setup(){host=new GameObject("Wreck lifetime test");host.layer=11;world=new PlayableWorld(host.transform,PlayableProfile.Default);world.SetPresentationLayer(11);}
        [TearDown]public void Cleanup(){if(host.GetComponent<PlayableBootstrap>())UnityEngine.Object.DestroyImmediate(host);else{world.Dispose();UnityEngine.Object.DestroyImmediate(host);}}
        [Serializable]class SourceManifest{public SourceModel[] models,hawkModels,upgradedModels;}
        [Serializable]class SourceModel{public string kind;public int variant;public SourceLod lod0;}
        [Serializable]class SourceLod{public string file,sha256;public SourceBounds bounds;}
        [Serializable]class SourceBounds{public float width,height,depth;}
        [Test]public void AllNineApprovedMeshesAndTexturesImportWithoutCollisionOrGlow()
        {
            string folder=Path.Combine(Application.dataPath,"Spacewars/Content/Wrecks");var manifest=JsonUtility.FromJson<SourceManifest>(File.ReadAllText(Path.Combine(folder,"source-manifest.json")));Assert.AreEqual(9,manifest.models.Length);
            foreach(var row in manifest.models)
            {
                using(var sha=SHA256.Create())Assert.AreEqual(row.lod0.sha256,BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(folder,row.lod0.file)))).Replace("-","").ToLowerInvariant());
                var prefab=Resources.Load<GameObject>("Wrecks/"+row.kind+"-"+row.variant);Assert.NotNull(prefab);Assert.Zero(prefab.GetComponentsInChildren<Collider>(true).Length);Assert.Zero(prefab.GetComponentsInChildren<Rigidbody>(true).Length);
                var instance=UnityEngine.Object.Instantiate(prefab);try
                {
                    var renderers=instance.GetComponentsInChildren<MeshRenderer>();Assert.AreEqual(3,renderers.Length);var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                    Assert.That(bounds.size.x,Is.EqualTo(row.lod0.bounds.width).Within(.015f));Assert.That(bounds.size.y,Is.EqualTo(row.lod0.bounds.height).Within(.015f));Assert.That(bounds.size.z,Is.EqualTo(row.lod0.bounds.depth).Within(.015f));
                    foreach(var r in renderers)foreach(var m in r.sharedMaterials){Assert.AreEqual("Universal Render Pipeline/Lit",m.shader.name);Assert.NotNull(m.GetTexture("_BaseMap"));Assert.IsFalse(m.IsKeywordEnabled("_EMISSION"));Assert.Zero(m.GetColor("_EmissionColor").maxColorComponent);}
                }finally{UnityEngine.Object.DestroyImmediate(instance);}
            }
        }
        [Test]public void CurrentHawkAndUpgradeSourcesArePinnedAndImportWithoutCollisionOrGlow()
        {
            string folder=Path.Combine(Application.dataPath,"Spacewars/Content/Wrecks");var manifest=JsonUtility.FromJson<SourceManifest>(File.ReadAllText(Path.Combine(folder,"source-manifest.json")));
            Assert.AreEqual(4,manifest.hawkModels.Length);Assert.AreEqual(13,manifest.upgradedModels.Length);
            foreach(var row in manifest.hawkModels.Concat(manifest.upgradedModels))
            {
                using(var sha=SHA256.Create())Assert.AreEqual(row.lod0.sha256,BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(folder,row.lod0.file)))).Replace("-","").ToLowerInvariant());
                var prefab=Resources.Load<GameObject>("Wrecks/"+Path.GetFileNameWithoutExtension(row.lod0.file));Assert.NotNull(prefab);Assert.Zero(prefab.GetComponentsInChildren<Collider>(true).Length);Assert.Zero(prefab.GetComponentsInChildren<Rigidbody>(true).Length);
                var rs=prefab.GetComponentsInChildren<MeshRenderer>();Assert.AreEqual(3,rs.Length);var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
                Assert.That(bounds.size.x,Is.EqualTo(row.lod0.bounds.width).Within(.015f));Assert.That(bounds.size.y,Is.EqualTo(row.lod0.bounds.height).Within(.015f));Assert.That(bounds.size.z,Is.EqualTo(row.lod0.bounds.depth).Within(.015f));
                foreach(var r in rs)foreach(var m in r.sharedMaterials){Assert.AreEqual("Universal Render Pipeline/Lit",m.shader.name);Assert.NotNull(m.GetTexture("_BaseMap"));Assert.IsFalse(m.IsKeywordEnabled("_EMISSION"));Assert.Zero(m.GetColor("_EmissionColor").maxColorComponent);}
            }
        }
        [UnityTest]public IEnumerator CapturedTankUpgradeSelectsItsOwnMeshesAfterFogAndGenerationReset()=>CheckCapturedUpgrade(PlayableEntityKind.Tank);
        [UnityTest]public IEnumerator CapturedExplorerUpgradeSelectsItsOwnMeshesAfterFogAndGenerationReset()=>CheckCapturedUpgrade(PlayableEntityKind.Explorer);
        [UnityTest]public IEnumerator CapturedShkvalUpgradeSelectsItsOwnMeshesAfterFogAndGenerationReset()=>CheckCapturedUpgrade(PlayableEntityKind.Shkval);
        [UnityTest]public IEnumerator CapturedHawkUpgradeSelectsItsOwnMeshesAfterFogAndGenerationReset()=>CheckCapturedUpgrade(PlayableEntityKind.Hawk);
        IEnumerator CheckCapturedUpgrade(PlayableEntityKind kind)
        {
            string key=kind==PlayableEntityKind.Explorer?"explorer":kind.ToString().ToLowerInvariant();
            for(int variant=1;variant<=(kind==PlayableEntityKind.Hawk?4:3);variant++)
            {
                world.Clear();yield return null;var state=new PlayableWreckSnapshot(41,PlayableOwner.Player,kind,default,.75,.72,0,.1,variant,0,0,5,905,965,upgraded:true);var rows=new[]{state};
                world.RenderUnitWrecks(Frame(10,rows,generation:variant));var expected=Resources.Load<GameObject>("Wrecks/"+key+"-upgraded-"+variant).GetComponentsInChildren<MeshFilter>().Select(m=>m.sharedMesh).ToArray();
                CollectionAssert.AreEqual(expected,Model().GetComponentsInChildren<MeshFilter>().Select(m=>m.sharedMesh).ToArray());
                world.RenderUnitWrecks(Frame(11));Assert.Zero(world.UnitWreckCount);yield return null;world.RenderUnitWrecks(Frame(12,rows));
                CollectionAssert.AreEqual(expected,Model().GetComponentsInChildren<MeshFilter>().Select(m=>m.sharedMesh).ToArray());
                world.RenderUnitWrecks(Frame(12,rows,generation:variant+100));yield return null;CollectionAssert.AreEqual(expected,Model().GetComponentsInChildren<MeshFilter>().Select(m=>m.sharedMesh).ToArray());
            }
        }
        [Test] public void WreckLodUsesScreenPercentAndReturnsToExactOriginalWithoutChangingLifecycle()
        {
            var previous=ModelDetailProfile.Current.Copy();var cameraObject=new GameObject("Wreck LOD camera");
            try{
                ModelDetailProfile.UseSession(ModelDetailProfile.Load());world.RenderUnitWrecks(Frame(10,new[]{Wreck()}));
                var model=Model();var lod=model.GetComponent<ModelDetailLod>();Assert.NotNull(lod);
                var original=lod.OriginalMeshes.ToArray();var materials=lod.Renderers.Select(r=>r.sharedMaterials).ToArray();var bounds=lod.Renderers.Select(r=>r.localBounds).ToArray();var pose=model.transform.localToWorldMatrix;
                var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;var center=lod.transform.TransformPoint(lod.ReferenceBounds.center);camera.transform.position=center+new Vector3(0,80,-60);camera.transform.LookAt(center);
                foreach(var row in new[]{(ratio:.2f,level:0),(ratio:.08f,level:1),(ratio:.03f,level:2),(ratio:.08f,level:1),(ratio:.2f,level:0)}){
                    camera.orthographicSize=1;float actualSize=lod.ProjectedScreenRatio(camera)*2;camera.orthographicSize=actualSize/(row.ratio*2);world.SelectEnvironmentDetail(camera);
                    Assert.AreEqual(row.level,lod.Level);Assert.AreEqual(pose,model.transform.localToWorldMatrix);Assert.AreEqual(1,world.UnitWreckCount);
                    for(int i=0;i<lod.Renderers.Length;i++){CollectionAssert.AreEqual(materials[i],lod.Renderers[i].sharedMaterials);Assert.AreEqual(bounds[i],lod.Renderers[i].localBounds);}
                }
                CollectionAssert.AreEqual(original,model.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh));
                world.RenderUnitWrecks(Frame(965));Assert.Zero(world.UnitWreckCount);
            }finally{ModelDetailProfile.UseSession(previous);UnityEngine.Object.DestroyImmediate(cameraObject);}
        }
        [Test]public void AuthorityLifetimeSmokeSinkingAndPauseFollowGameTicks()
        {
            var rows=new[]{Wreck()};world.RenderUnitWrecks(Frame(0,rows));Assert.AreEqual(1,world.UnitWreckCount);Assert.False(Model().activeSelf);
            world.RenderUnitWrecks(Frame(5,rows));Assert.True(Model().activeSelf);Assert.True(host.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Wreck residual smoke").gameObject.activeSelf);
            var pose=Model().transform.localToWorldMatrix;for(int i=0;i<12;i++)world.RenderUnitWrecks(Frame(5,rows,paused:true));Assert.AreEqual(pose,Model().transform.localToWorldMatrix);
            Assert.True(Model().GetComponentsInChildren<Transform>(true).All(t=>t.gameObject.layer==11));Assert.Zero(Model().GetComponentsInChildren<Collider>(true).Length);
            world.RenderUnitWrecks(Frame(60,rows));Assert.False(host.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Wreck residual smoke").gameObject.activeSelf);
            world.RenderUnitWrecks(Frame(935,rows));Assert.Less(Model().transform.position.y,-.1f);world.RenderUnitWrecks(Frame(965));Assert.Zero(world.UnitWreckCount);
        }
        [Test]public void HitRemovesModelWithProjectileFlashAndSmallSmoke()
        {
            world.RenderUnitWrecks(Frame(5,new[]{Wreck()}));world.RenderUnitWrecks(Frame(10,new[]{Wreck(10)}));Assert.False(Model().activeSelf);Assert.Zero(world.UnitWreckCount);
            Assert.True(host.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Wreck residual smoke"&&t.gameObject.activeSelf));
            world.RenderUnitWrecks(Frame(29));Assert.Zero(world.UnitWreckCount);
        }
        [Test]public void SoundJournalCannotCreateWreckAndVisibleAuthorityRestoresAfterFog()
        {
            world.RenderUnitWrecks(Frame(0,sounds:new[]{new PlayableSoundEvent(1,0,PlayableSoundKind.Destroyed,default,PlayableEntityKind.Tank,unitId:41)}));Assert.Zero(world.UnitWreckCount);
            var rows=new[]{Wreck()};world.RenderUnitWrecks(Frame(30,rows));Assert.True(Model().activeSelf);world.RenderUnitWrecks(Frame(30));Assert.Zero(world.UnitWreckCount);
            world.RenderUnitWrecks(Frame(50,rows));Assert.AreEqual(1,world.UnitWreckCount);world.RenderUnitWrecks(Frame(0,generation:2));Assert.Zero(world.UnitWreckCount);
        }
        [UnityTest]public IEnumerator WreckFinishAppliesAtCreationAndRefreshesExistingModelsWithoutChangingSharedAssets()
        {
            world.RenderUnitWrecks(Frame(5,new[]{Wreck()}));var renderer=Model().GetComponentInChildren<MeshRenderer>();var source=renderer.sharedMaterial;var color=source.GetColor("_BaseColor");var metallic=source.GetFloat("_Metallic");
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,0);Assert.AreEqual(world.MaterialFinish.wreckMetallic,block.GetFloat("_Metallic"));Assert.Greater(block.GetColor("_BaseColor").b,block.GetColor("_BaseColor").r);
            var finish=ActorMaterialProfile.Load();finish.wreckMetallic=.2f;finish.wreckSmoothness=.3f;
            var light=IndustrialLightingProfile.Load();light.sunSaturation=0;world.ApplyGraphics(finish,light);renderer.GetPropertyBlock(block,0);
            Assert.AreEqual(.2f,block.GetFloat("_Metallic"));Assert.AreEqual(.3f,block.GetFloat("_Smoothness"));Assert.AreEqual(Color.white,block.GetColor("_BaseColor"),"White light must not acquire a compensating blue tint.");
            world.Clear();yield return null;world.RenderUnitWrecks(Frame(5,new[]{Wreck()},generation:2));renderer=Model().GetComponentInChildren<MeshRenderer>();renderer.GetPropertyBlock(block,0);Assert.AreEqual(Color.white,block.GetColor("_BaseColor"),"Generation reset must retain the active neutral lighting finish.");
            Assert.AreEqual(color,source.GetColor("_BaseColor"));Assert.AreEqual(metallic,source.GetFloat("_Metallic"));
            var controls=NativeGraphicsSettings.Parameters.Where(p=>p.Path.StartsWith("visual.actorFinish.wreck")).ToArray();Assert.AreEqual(3,controls.Length);Assert.True(controls.All(p=>NativeGraphicsSettings.Group(p)=="Остовы · обгоревший металл"));
        }
        [Test]public void ProfileKeepsLifetimeControlsRangeValidated()
        {
            var p=UnitWreckProfile.Load();Assert.AreEqual(30,p.lifetimeSeconds);var field=p.Parameters().Single();Assert.AreEqual("visual.unitWreck.lifetimeSeconds",field.Path);Assert.AreEqual(120,field.Maximum);
            foreach(float bad in new[]{float.NaN,float.PositiveInfinity,field.Minimum-field.Step,field.Maximum+field.Step}){field.Write(bad);Assert.Throws<ArgumentException>(()=>p.Validate());}
        }
    }
}

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
        static PlayableSoundEvent Death(long id=1,long tick=0,PlayableEntityKind kind=PlayableEntityKind.Tank,int unit=41)
            =>new PlayableSoundEvent(id,tick,PlayableSoundKind.Destroyed,new NavPoint(0,0),kind,unitId:unit,hullHeading:.75);
        static PlayableSnapshot Frame(long tick,PlayableSoundEvent[] sounds=null,TeamVisionSnapshot vision=null,long generation=1,bool paused=false)
            =>new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,generation,7,1,tick,RuntimeStatus.Running,paused,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision,sounds:sounds);
        GameObject Model()=>host.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Unit wreck 41").gameObject;
        [SetUp]public void Setup(){host=new GameObject("Wreck lifetime test");host.layer=11;world=new PlayableWorld(host.transform,PlayableProfile.Default);world.SetPresentationLayer(11);}
        [TearDown]public void Cleanup(){if(host.GetComponent<PlayableBootstrap>())UnityEngine.Object.DestroyImmediate(host);else{world.Dispose();UnityEngine.Object.DestroyImmediate(host);}}
        [Serializable]class SourceManifest{public SourceModel[] models;}
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
        [Test]public void FlashThenExactlyOneSecondWreckWithoutDuplicateOrPausedAge()
        {
            var death=new[]{Death()};world.RenderUnitWrecks(Frame(0,death));Assert.AreEqual(1,world.UnitWreckCount);Assert.IsFalse(Model().activeSelf);
            world.RenderUnitWrecks(Frame(5,death));Assert.IsTrue(Model().activeSelf);var transform=Model().transform.localToWorldMatrix;
            for(int i=0;i<12;i++)world.RenderUnitWrecks(Frame(5,death,paused:true));Assert.AreEqual(1,world.UnitWreckCount);Assert.AreEqual(transform,Model().transform.localToWorldMatrix);
            Assert.IsTrue(Model().GetComponentsInChildren<Transform>(true).All(t=>t.gameObject.layer==11));Assert.Zero(Model().GetComponentsInChildren<Collider>(true).Length);
            world.RenderUnitWrecks(Frame(34,death));Assert.AreEqual(1,world.UnitWreckCount);Assert.IsTrue(Model().activeSelf);
            world.RenderUnitWrecks(Frame(35,death));Assert.Zero(world.UnitWreckCount);world.RenderUnitWrecks(Frame(36,death));Assert.Zero(world.UnitWreckCount);
        }
        [Test]public void DurationIsCapturedAndGenerationClearRollbackDoNotRetainOldWrecks()
        {
            world.RenderUnitWrecks(Frame(5,new[]{Death()}));world.WreckProfile.lifetimeSeconds=2;
            world.RenderUnitWrecks(Frame(35,new[]{Death(2,35,unit:42)}));Assert.AreEqual(1,world.UnitWreckCount);
            world.RenderUnitWrecks(Frame(99));Assert.AreEqual(1,world.UnitWreckCount);world.RenderUnitWrecks(Frame(100));Assert.Zero(world.UnitWreckCount);
            world.RenderUnitWrecks(Frame(101,new[]{Death(3,101)}));Assert.AreEqual(1,world.UnitWreckCount);
            world.RenderUnitWrecks(Frame(0,generation:2));Assert.Zero(world.UnitWreckCount);
            world.RenderUnitWrecks(Frame(5,new[]{Death()} ,generation:2));Assert.AreEqual(1,world.UnitWreckCount);world.RenderUnitWrecks(Frame(0,generation:2));Assert.Zero(world.UnitWreckCount);
            world.RenderUnitWrecks(Frame(5,new[]{Death()},generation:2));world.Clear();Assert.Zero(world.UnitWreckCount);
        }
        [Test]public void LabProfileControlsNewWorldAndLiveRebindLifetime()
        {
            var data=PlayableProfile.Default.CopyData();var field=PlayableProfileMetadata.Fields.Single(f=>f.FieldName=="unitWreckLifetimeSeconds");
            Assert.True(NativeBalanceFields.Editable(field));field.Write(data,2.5);var changed=PlayableProfile.Create(data);
            world.Dispose();world=new PlayableWorld(host.transform,changed);Assert.AreEqual(2.5,world.WreckProfile.lifetimeSeconds);
            world.RenderUnitWrecks(Frame(5,new[]{Death()}));world.RenderUnitWrecks(Frame(79));Assert.AreEqual(1,world.UnitWreckCount);world.RenderUnitWrecks(Frame(80));Assert.Zero(world.UnitWreckCount);
            field.Write(data,.1);world.Rebind(PlayableProfile.Create(data));world.RenderUnitWrecks(Frame(5,new[]{Death()}));world.RenderUnitWrecks(Frame(7));Assert.AreEqual(1,world.UnitWreckCount);world.RenderUnitWrecks(Frame(8));Assert.Zero(world.UnitWreckCount);
            foreach(double bad in new[]{0,10.1,double.NaN,double.PositiveInfinity})Assert.Throws<ArgumentOutOfRangeException>(()=>field.Write(data,bad));
        }
        [Test]public void BuildingSaleHiddenDisappearanceAndFutureFactsCannotCreateUnitWrecks()
        {
            var events=new[]{new PlayableSoundEvent(1,0,PlayableSoundKind.Destroyed,default,PlayableEntityKind.Tank,true,41),new PlayableSoundEvent(2,0,PlayableSoundKind.Demolished,default,PlayableEntityKind.Tank,unitId:41),new PlayableSoundEvent(3,0,PlayableSoundKind.Destroyed,default,PlayableEntityKind.Tank),Death(4,5)};
            world.RenderUnitWrecks(Frame(0,events));Assert.Zero(world.UnitWreckCount);world.RenderUnitWrecks(Frame(1));Assert.Zero(world.UnitWreckCount);
        }
        [Test]public void OneSecondProfileMetadataDefinesEveryValidationBoundary()
        {
            var p=UnitWreckProfile.Load();Assert.AreEqual(1,p.lifetimeSeconds);var field=p.Parameters().Single();Assert.AreEqual("visual.unitWreck.lifetimeSeconds",field.Path);Assert.AreEqual("s",field.Unit);Assert.Greater(field.Step,0);Assert.IsNotEmpty(field.Description);
            foreach(float bad in new[]{float.NaN,float.PositiveInfinity,field.Minimum-field.Step,field.Maximum+field.Step}){field.Write(bad);Assert.Throws<ArgumentException>(()=>p.Validate());}
            field.Write(1);p.Validate();
        }
        static TeamVisionSnapshot Vision(bool visible)
        {var v=new PlayableVision(0,64,64,4,1);v.Refresh(visible?new[]{new VisionSource(default,80)}:Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());return v.Snapshot();}
        static Texture2D Read(RenderTexture target)
        {var old=RenderTexture.active;try{RenderTexture.active=target;var t=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);t.ReadPixels(new Rect(0,0,target.width,target.height),0,0);t.Apply();return t;}finally{RenderTexture.active=old;}}
        [UnityTest]public IEnumerator RealThreeCrossingsRenderHidesWreckAndItsShadowExactly()
        {
            world.Dispose();UnityEngine.Object.DestroyImmediate(host);host=new GameObject("Wreck production fog render");var bootstrap=host.AddComponent<PlayableBootstrap>();bootstrap.enabled=false;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;var profile=PlayableProfile.ThreeCrossingsDefault;
            var oldAmbient=RenderSettings.ambientLight;var oldMode=RenderSettings.ambientMode;float oldAudio=AudioListener.volume;AudioListener.volume=0;
            var pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;float oldDistance=pipeline.shadowDistance;
            var target=new RenderTexture(1200,760,24);target.Create();Camera camera=null;Texture2D visible=null,hidden=null,empty=null;
            try
            {
                typeof(PlayableBootstrap).GetField("profile",flags).SetValue(bootstrap,profile);typeof(PlayableBootstrap).GetMethod("CreateWorld",flags).Invoke(bootstrap,null);
                world=(PlayableWorld)typeof(PlayableBootstrap).GetField("world",flags).GetValue(bootstrap);world.InspectionReveal();camera=(Camera)typeof(PlayableBootstrap).GetField("cameraView",flags).GetValue(bootstrap);camera.targetTexture=target;camera.orthographicSize=5;camera.transform.position=new Vector3(0,15,-11);camera.transform.LookAt(Vector3.zero);
                var death=new[]{Death()};world.RenderUnitWrecks(Frame(8,death,Vision(true)));Assert.IsTrue(Model().activeSelf);Assert.AreEqual((float)profile.AuthoredMap.SurfaceHeight(default),Model().transform.position.y);
                for(int i=0;i<5;i++)yield return null;visible=Read(target);
                world.RenderUnitWrecks(Frame(8,death,Vision(false),paused:true));Assert.IsFalse(Model().activeSelf);
                for(int i=0;i<5;i++)yield return null;hidden=Read(target);typeof(PlayableWorld).GetMethod("ClearUnitWrecks",flags).Invoke(world,null);
                for(int i=0;i<5;i++)yield return null;empty=Read(target);
                CollectionAssert.AreEqual(empty.GetPixels32(),hidden.GetPixels32(),"Concealed wreck and its shadow must not leak");Assert.IsFalse(visible.GetPixels32().SequenceEqual(hidden.GetPixels32()));
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/qa/unit-wrecks/frames"));Directory.CreateDirectory(path);File.WriteAllBytes(Path.Combine(path,"visible.png"),visible.EncodeToPNG());File.WriteAllBytes(Path.Combine(path,"concealed.png"),hidden.EncodeToPNG());
            }
            finally
            {if(camera)camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);foreach(var t in new[]{visible,hidden,empty})if(t)UnityEngine.Object.DestroyImmediate(t);RenderSettings.ambientLight=oldAmbient;RenderSettings.ambientMode=oldMode;AudioListener.volume=oldAudio;pipeline.shadowDistance=oldDistance;}
        }
    }
}

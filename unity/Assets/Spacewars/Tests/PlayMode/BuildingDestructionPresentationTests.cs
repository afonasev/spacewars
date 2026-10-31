using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class BuildingDestructionPresentationTests
{
    private GameObject host;private PlayableWorld world;private PlayableProfile profile;
    private PlayableSnapshot View(long tick,PlayableBuildingDestruction[] deaths=null,long generation=1)
        =>new PlayableSnapshot(profile.ProfileId,profile.Revision,generation,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,
            new NavGeometry(profile.ArenaHalfExtent,Array.Empty<NavObstacle>(),1),Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,activeProfile:profile,buildingDestructions:deaths);
    [SetUp] public void Setup()
    {profile=PlayableProfile.Default;host=new GameObject("Building destruction fixture");world=new PlayableWorld(host.transform,profile);world.SetPresentationLayer(8);world.Fog.ShowPublic();}
    [TearDown] public void Cleanup(){world.Dispose();UnityEngine.Object.DestroyImmediate(host);}
    [UnityTest] public IEnumerator EveryBuildingSinksWithoutSelectionColliderOrIndependentClock()
    {
        int id=100;
        foreach(PlayableBuildingKind kind in Enum.GetValues(typeof(PlayableBuildingKind)))
        {
            world.Clear();var d=new PlayableBuildingDestruction(++id,1,1,PlayableOwner.Player,kind,new NavPoint(0,0),Math.PI,1,false,0,72);
            world.RenderBuildingDestructions(View(1,new[]{d}));yield return null;
            Assert.AreEqual(1,world.BuildingDestructionCount,kind.ToString());Assert.IsEmpty(world.Actors,"Visual corpse must not become a live selectable actor");
            var corpse=host.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Building collapse "+id);
            foreach(var collider in corpse.GetComponentsInChildren<Collider>(true))Assert.False(collider.enabled);
            foreach(var part in corpse.GetComponentsInChildren<Transform>(true))Assert.AreEqual(8,part.gameObject.layer);
            var before=corpse.position;world.RenderBuildingDestructions(View(36,new[]{d}));yield return null;
            Assert.Less(corpse.position.y,before.y,"Hull must descend, not shrink away");var paused=corpse.position;
            yield return null;world.RenderBuildingDestructions(View(36,new[]{d}));Assert.AreEqual(paused,corpse.position,"Same simulation tick freezes the collapse");
            world.RenderBuildingDestructions(View(72));yield return null;Assert.AreEqual(0,world.BuildingDestructionCount);
        }
    }
    [UnityTest] public IEnumerator RestartClearsAllCorpseAndParticleObjects()
    {
        var d=new PlayableBuildingDestruction(100,1,1,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(0,0),0,1,false,0,72);
        world.RenderBuildingDestructions(View(20,new[]{d}));Assert.AreEqual(1,world.BuildingDestructionCount);
        world.RenderBuildingDestructions(View(0,generation:2));yield return null;
        Assert.AreEqual(0,world.BuildingDestructionCount);
        Assert.False(host.GetComponentsInChildren<Transform>(true).Any(t=>t.name.StartsWith("Building collapse ")||t.name=="Collapse fire and dust"));
    }
    [UnityTest] public IEnumerator RealDomainFactoryDestructionProducesReviewFrames()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        var domain=Activator.CreateInstance(type,flags,null,new object[]{profile,1L},null);
        object Call(string name,params object[] args)=>type.GetMethod(name,flags).Invoke(domain,args);
        PlayableSnapshot Snapshot()=>(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        Call("AddCredits",PlayableOwner.Player,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
        Call("AdvanceFoundations");Call("AdvanceBuildings",(double)profile.FactoryBuildSeconds);var b=Snapshot().Buildings.Single(x=>x.Kind==PlayableBuildingKind.Factory);
        var model=world.Building(b.Id,b.Kind.ToString(),true);model.Root.position=world.Point(b.Position);world.FaceBuilding(model);model.Selection.SetActive(false);model.Health.parent.gameObject.SetActive(false);
        var camera=new GameObject("Collapse QA camera").AddComponent<Camera>();camera.transform.SetParent(host.transform,false);camera.orthographic=true;camera.orthographicSize=4.6f;camera.cullingMask=1<<8;
        var point=world.Point(b.Position);camera.transform.position=point+new Vector3(7,9,-11);camera.transform.LookAt(point+Vector3.up*.8f);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.07f,.09f);
        var light=new GameObject("Collapse QA light").AddComponent<Light>();light.transform.SetParent(host.transform,false);light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(48,-30,0);
        var previousAmbient=RenderSettings.ambientLight;RenderSettings.ambientLight=new Color(.63f,.69f,.73f);
        var target=new RenderTexture(1280,800,24);target.Create();camera.targetTexture=target;
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/add-building-destruction/frames"));Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(folder,"sequence"));
        try
        {
            yield return null;Capture("00-before.png");
            Call("Damage",b.Id,100000);var death=Snapshot().BuildingDestructions.Single();world.RenderBuildingDestructions(Snapshot());
            var corpse=host.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Building collapse "+b.Id);
            Assert.True(model.Hull.IsChildOf(corpse),"Actual rendered hull must transfer without replacement");
            var effects=host.GetComponentsInChildren<Renderer>().Single(r=>r.name=="Collapse fire and dust");var properties=new MaterialPropertyBlock();effects.GetPropertyBlock(properties);
            Assert.Less(properties.GetFloat("_Radius"),TerritoryRules.Radius(profile,b.Kind)*2,"Service particle bounds must not turn a local fire into a screen-sized effect");
            world.Remove(b.Id);yield return null;Capture("01-flash.png");
            for(int frame=1;frame<=death.EndTick;frame++)
            {
                Call("Step",1d/30d);world.RenderBuildingDestructions(Snapshot());
                yield return null;Capture("sequence/"+frame.ToString("D3")+".png");
                if(frame==18||frame==40||frame==65||frame==72)Capture("collapse-"+frame.ToString("D2")+".png");
            }
            Assert.AreEqual(0,world.BuildingDestructionCount);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            var clip=Resources.Load<AudioClip>("Sfx/Hybrid/building-collapse-1");Assert.NotNull(clip);Assert.AreEqual(2.4f,clip.length,.001f);
        }
        finally{RenderSettings.ambientLight=previousAmbient;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
        void Capture(string file)
        {
            camera.Render();var active=RenderTexture.active;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(folder,file),pixels.EncodeToPNG());}
            finally{RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(pixels);}
        }
    }
}

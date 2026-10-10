using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class PlayableRallyFlagTests
{
    private GameObject host;
    private PlayableWorld world;
    [SetUp] public void Setup(){host=new GameObject("Rally flag fixture");world=new PlayableWorld(host.transform,PlayableProfile.Default);}
    [TearDown] public void Cleanup(){world.Dispose();UnityEngine.Object.DestroyImmediate(host);}
    private static PlayableBuildingSnapshot Building(int id=7,PlayableOwner owner=PlayableOwner.Player,bool hasRally=true,NavPoint? rally=null,bool privateState=true,ConstructionPhase phase=ConstructionPhase.Ready,PlayableBuildingKind kind=PlayableBuildingKind.Factory,int health=250,NavPoint? pending=null,bool selling=false)
        =>new PlayableBuildingSnapshot(id,owner,kind,new NavPoint(-3,0),health,1,0,0,rally??new NavPoint(2,1),includePrivateState:privateState,phase:phase,hasRally:hasRally,pendingRally:pending,lifecycle:selling?new PlayableBuildingLifecycleSnapshot(true,0,false,false,0,0,null,null,0,0,false):null);
    private static PlayableSnapshot View(params PlayableBuildingSnapshot[] buildings)
        =>new PlayableSnapshot("fixture",1,1,7,0,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
    [TestCase(PlayableBuildingKind.Headquarters)]
    [TestCase(PlayableBuildingKind.Outpost)]
    [TestCase(PlayableBuildingKind.Factory)]
    public void SelectedOwnProducerShowsApprovedFlagAtAcceptedPoint(PlayableBuildingKind kind)
    {
        var snapshot=View(Building(kind:kind));world.RenderRallyFlags(snapshot,new HashSet<int>{7});
        Assert.AreEqual(1,world.RallyFlags.Count);var flag=world.RallyFlags[7];
        Assert.AreEqual(new Vector3(2,.03f,1),flag.position);Assert.AreEqual(1.8f/2.1077218f,flag.localScale.x,.00001);
        Assert.IsTrue(flag.GetComponentsInChildren<Transform>().Any(t=>t.name=="landmark__rally__armored-pennant"));
        Assert.IsEmpty(flag.GetComponentsInChildren<Collider>());
        var renderers=flag.GetComponentsInChildren<MeshRenderer>();Assert.AreEqual(5,renderers.Length);
        var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
        Assert.AreEqual(1.8f,bounds.size.y,.0001f);Assert.AreEqual(.03f,bounds.min.y,.0001f);
        foreach(var renderer in renderers)foreach(var material in renderer.sharedMaterials)
        {Assert.IsNotNull(material);Assert.AreEqual("Universal Render Pipeline/Lit",material.shader.name);Assert.IsTrue(material.shader.isSupported);}
        world.RenderRallyFlags(snapshot,new HashSet<int>());Assert.IsEmpty(world.RallyFlags);
        Assert.IsTrue(snapshot.Buildings[0].PrivateState.HasRally,"Deselecting must preserve authority");
        world.RenderRallyFlags(snapshot,new HashSet<int>{7});Assert.AreEqual(1,world.RallyFlags.Count);
    }
    [Test] public void PrivateUnsetEnemyPendingDeadAndSpectatorFlagsStayHidden()
    {
        foreach(var building in new[]{Building(hasRally:false),Building(owner:PlayableOwner.Enemy),Building(privateState:false),Building(phase:ConstructionPhase.Pending),Building(health:0),Building(selling:true),Building(hasRally:false,pending:new NavPoint(9,9))})
        {world.RenderRallyFlags(View(building),new HashSet<int>{7});Assert.IsEmpty(world.RallyFlags);}
        world.RenderRallyFlags(View(Building()),new HashSet<int>{7},spectator:true);Assert.IsEmpty(world.RallyFlags);
    }
    [Test] public void ReplacementPendingRemovalAndClearKeepMarkerLifecycleBounded()
    {
        var selection=new HashSet<int>{7};world.RenderRallyFlags(View(Building()),selection);var flag=world.RallyFlags[7];
        world.RenderRallyFlags(View(Building(rally:new NavPoint(-1,3),pending:new NavPoint(9,9))),selection);
        Assert.AreSame(flag,world.RallyFlags[7]);Assert.AreEqual(new Vector3(-1,.03f,3),flag.position,"Pending point must not replace accepted point");
        world.Remove(7);Assert.IsEmpty(world.RallyFlags);Assert.IsFalse(flag.gameObject.activeSelf);
        world.RenderRallyFlags(View(Building()),selection);world.Clear();Assert.IsEmpty(world.RallyFlags);
        world.RenderRallyFlags(View(Building()),selection);world.Rebind(PlayableProfile.Default);Assert.IsEmpty(world.RallyFlags);
        world.RenderRallyFlags(View(Building()),selection);world.RenderRallyFlags(View(),selection);Assert.IsEmpty(world.RallyFlags);
    }
    [Test] public void ViewerLayerOwnerPaintAndHeightMetadataAreApplied()
    {
        world.SetPresentationLayer(12);world.OwnerPaint=_=>Color.magenta;
        var data=PlayableProfile.Default.CopyData();var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="system.rallyFlagHeight");
        Assert.AreEqual(.5,field.Minimum);Assert.AreEqual(4,field.Maximum);Assert.AreEqual(.1,field.Step);
        field.Write(data,3);world.Rebind(PlayableProfile.Create(data));world.RenderRallyFlags(View(Building()),new HashSet<int>{7});
        var flag=world.RallyFlags[7];Assert.AreEqual(3f/2.1077218f,flag.localScale.y,.00001);
        foreach(var t in flag.GetComponentsInChildren<Transform>())Assert.AreEqual(12,t.gameObject.layer);
        foreach(var r in flag.GetComponentsInChildren<Renderer>())for(int i=0;i<r.sharedMaterials.Length;i++)
        {
            var role=r.sharedMaterials[i].name;if(!role.EndsWith("team-primary")&&!role.EndsWith("team-emissive"))continue;
            var block=new MaterialPropertyBlock();r.GetPropertyBlock(block,i);Assert.AreEqual(Color.magenta,block.GetColor("_BaseColor"));
        }
        Assert.Throws<ArgumentOutOfRangeException>(()=>field.Write(data,.4));
        data.rallyFlagHeight=.4;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
    }
    [Test] public void FlagUsesAuthoredRampHeightAndNormal()
    {
        var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
        var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);world.Rebind(profile);
        NavPoint? ramp=null;
        for(int x=-64;x<=64&&!ramp.HasValue;x++)for(int z=-64;z<=64;z++)
        {
            var point=new NavPoint(x,z);var gradient=map.SurfaceGradient(point);
            if(Math.Abs(gradient.X)+Math.Abs(gradient.Z)>.01){ramp=point;break;}
        }
        Assert.IsTrue(ramp.HasValue,"Authored map must expose a sloped support");
        var position=ramp.Value;world.RenderRallyFlags(View(Building(rally:position)),new HashSet<int>{7});
        var flag=world.RallyFlags[7];var slope=map.SurfaceGradient(position);
        Assert.AreEqual(map.SurfaceHeight(position)+.03,flag.position.y,.0001);
        Assert.Less(Vector3.Distance(new Vector3((float)-slope.X,1,(float)-slope.Z).normalized,flag.up),.0001);
    }
    [UnityTest] public IEnumerator ApprovedModelRendersBesideSelectedFactory()
    {
        var snapshot=View(Building());var selected=new HashSet<int>{7};
        var factory=world.Building(7,"Factory",true);factory.Root.position=new Vector3(-3,0,0);factory.Selection.SetActive(true);
        world.Fog.ShowPublic();world.RenderRallyFlags(snapshot,selected);
        var light= new GameObject("Flag QA light").AddComponent<Light>();light.transform.SetParent(host.transform);light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(48,-30,0);
        var camera=new GameObject("Flag QA camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=4.6f;
        camera.transform.position=new Vector3(7,10,-12);camera.transform.LookAt(new Vector3(-.5f,.7f,0));camera.backgroundColor=new Color(.045f,.075f,.1f);
        PlayableWorld.UpdateHealth(factory,camera,1);
        var target=new RenderTexture(1280,800,24);target.Create();camera.targetTexture=target;
        var oldAmbient=RenderSettings.ambientLight;RenderSettings.ambientLight=new Color(.63f,.69f,.73f);
        try
        {
            yield return null;yield return null;
            Capture(camera,target,"selected-factory.png");
            world.RenderRallyFlags(snapshot,new HashSet<int>());factory.Selection.SetActive(false);yield return null;
            Capture(camera,target,"deselected-factory.png");
            world.RenderRallyFlags(View(Building(rally:new NavPoint(1,-2))),selected);factory.Selection.SetActive(true);yield return null;
            Capture(camera,target,"replaced-rally.png");
        }
        finally{RenderSettings.ambientLight=oldAmbient;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
    }
    private static void Capture(Camera camera,RenderTexture target,string filename)
    {
        string directory=Path.GetFullPath("../.local/rally-flag/evidence");Directory.CreateDirectory(directory);camera.Render();
        var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(directory,filename),texture.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture);}
    }
}

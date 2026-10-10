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

public sealed class PlayableTerritoryPadTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
    private GameObject host;
    private PlayableWorld world;
    private object domain;
    private long tick;
    private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
    private PlayableSnapshot View()
    {
        var v=(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        return new PlayableSnapshot(v.ProfileId,v.ProfileRevision,v.Generation,v.Seed,++tick,tick,v.Status,v.Paused,v.Outcome,v.Credits,v.Geometry,v.Entities.ToArray(),v.Buildings.ToArray(),v.Projectiles.ToArray(),v.Metrics,v.Failure,v.Sites.ToArray());
    }
    [SetUp] public void Setup()
    {
        host=new GameObject("Construction pad recovery fixture");world=new PlayableWorld(host.transform,PlayableProfile.Default);
        domain=Activator.CreateInstance(Domain,Flags,null,new object[]{PlayableProfile.Default,1L},null);tick=0;
    }
    [TearDown] public void Cleanup(){world.Dispose();UnityEngine.Object.DestroyImmediate(host);}
    private Transform Pad(string key)=>host.GetComponentsInChildren<Transform>().SingleOrDefault(t=>t.name=="Site pad "+key);
    private void AssertVisible(Transform pad,int layer)
    {
        Assert.IsNotNull(pad,"Freed site must recreate its pad");
        foreach(var part in pad.GetComponentsInChildren<Transform>())Assert.AreEqual(layer,part.gameObject.layer,part.name);
        Assert.IsNotEmpty(pad.GetComponentsInChildren<MeshRenderer>());
    }
    [UnityTest] public IEnumerator SaleKeepsApronOnEachViewerLayer()=>Recover(true);
    [UnityTest] public IEnumerator DestructionKeepsApronOnEachViewerLayer()=>Recover(false);
    private IEnumerator Recover(bool sale)
    {
        foreach(int layer in new[]{8,9})
        {
            world.Clear();world.RenderSites(View());yield return null;
            world.SetPresentationLayer(layer);AssertVisible(Pad("1:1"),layer);var original=Pad("1:1");
            Call("AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",(double)PlayableProfile.Default.FactoryBuildSeconds+1);
            var building=View().Buildings.Single(b=>b.SiteId==1&&b.SlotId==1);
            world.RenderSites(View());yield return null;Assert.AreSame(original,Pad("1:1"));
            if(sale)
            {
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,layer,"player-1",PlayableCommandKind.SellBuilding,new[]{building.Id}),null));
                world.RenderSites(View());yield return null;Assert.AreSame(original,Pad("1:1"),"Demolition retains the apron");
                Assert.AreEqual(PlayableCommandStatus.OccupiedPad,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null),"Demolition still occupies the slot");
                Call("AdvanceBuildingLifecycle",PlayableProfile.Default.BuildingSaleDemolitionSec);
            }
            else Call("Damage",building.Id,building.Health);
            Assert.IsFalse(View().Buildings.Any(b=>b.Id==building.Id));
            world.RenderSites(View());yield return null;AssertVisible(Pad("1:1"),layer);Assert.AreSame(original,Pad("1:1"));
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null),"Recovered slot must also accept rebuilding");
            Call("CancelBuilding",View().Buildings.Single(b=>b.SiteId==1&&b.SlotId==1).Id,PlayableOwner.Player,null);
        }
    }
    [UnityTest] public IEnumerator DestroyedCenterRecreatesCapturePadAndRendersOnViewerCamera()
    {
        world.Fog.ShowPublic();world.SetPresentationLayer(8);Call("Damage",1,View().Buildings.Single(b=>b.Id==1).Health);
        world.RenderSites(View());yield return null;AssertVisible(Pad("1:0"),8);
        Assert.IsNotNull(Pad("1:1"),"Discovered physical aprons survive center loss");
        Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null),"A surviving apron does not grant building without a ready center");
        var pad=Pad("1:0");var camera=new GameObject("Pad viewer camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);
        camera.cullingMask=1<<8;camera.orthographic=true;camera.orthographicSize=8;
        camera.transform.position=pad.position+new Vector3(9,15,-12);camera.transform.LookAt(pad.position);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var target=new RenderTexture(640,480,24);target.Create();camera.targetTexture=target;
        var texture=new Texture2D(640,480,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try
        {
            camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,640,480),0,0);texture.Apply();
            Assert.IsTrue(texture.GetPixels().Any(c=>c.maxColorComponent>.05f),"Viewer camera must actually render the recovered pad");
            string directory=Path.GetFullPath("../.local/construction-pad-recovery/evidence");Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory,"destroyed-center-pad.png"),texture.EncodeToPNG());
        }
        finally{RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(texture);}
    }
}

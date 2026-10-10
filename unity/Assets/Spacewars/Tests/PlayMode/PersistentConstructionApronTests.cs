using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class PersistentConstructionApronTests
{
    private GameObject host;
    private PlayableWorld world;
    private Camera camera;
    private long tick;
    private PlayableProfile profile;
    [SetUp] public void Setup()
    {
        profile=PlayableProfile.Default;host=new GameObject("Persistent apron preview");world=new PlayableWorld(host.transform,profile);world.Fog.ShowPublic();world.SetPresentationLayer(8);tick=0;
        camera=new GameObject("Apron camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.cullingMask=1<<8;camera.orthographic=true;camera.orthographicSize=10.5f;
        camera.transform.position=new Vector3(15,28,-28);camera.transform.LookAt(new Vector3(0,0,3));camera.backgroundColor=new Color(.06f,.09f,.1f);camera.clearFlags=CameraClearFlags.SolidColor;
        var light=new GameObject("Preview sun").AddComponent<Light>();light.transform.SetParent(host.transform);light.type=LightType.Directional;light.intensity=1.15f;light.transform.rotation=Quaternion.Euler(50,-35,0);
    }
    [TearDown] public void Cleanup(){world.Dispose();UnityEngine.Object.DestroyImmediate(host);}
    private Transform Pad(string key)=>host.GetComponentsInChildren<Transform>().Single(t=>t.name=="Site pad "+key);
    private PlayableSnapshot SceneView(bool occupied,bool upgraded=false)
    {
        var sites=new[]{
            new TerritorySite(100,PlayableBuildingKind.Headquarters,new NavPoint(-7,7),new[]{new TerritorySlot(1,new NavPoint(-7,-1),.6),new TerritorySlot(2,new NavPoint(0,-1),1.8),new TerritorySlot(3,new NavPoint(7,-1),3.1)}),
            new TerritorySite(200,PlayableBuildingKind.Outpost,new NavPoint(0,7),Array.Empty<TerritorySlot>()),
            new TerritorySite(300,PlayableBuildingKind.Mine,new NavPoint(7,7),Array.Empty<TerritorySlot>())};
        var buildings=sites.Select((s,i)=>Building(10+i,s.Kind,s.Position,s.Id,0)).ToList();
        if(occupied)foreach(var slot in sites[0].Slots)buildings.Add(Building(40+slot.Id,slot.Id==1?PlayableBuildingKind.Factory:slot.Id==2?PlayableBuildingKind.Refinery:PlayableBuildingKind.ScientificCenter,slot.Position,100,slot.Id,upgraded&&slot.Id==2));
        return new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,++tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,Array.Empty<PlayableEntitySnapshot>(),buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,sites.Select((s,i)=>new TerritorySiteSnapshot(s,PlayableOwner.Player,PlayableOwner.Player,1,false,10+i,true)).ToArray());
    }
    private static PlayableBuildingSnapshot Building(int id,PlayableBuildingKind kind,NavPoint position,int site,int slot,bool upgraded=false)=>new PlayableBuildingSnapshot(id,PlayableOwner.Player,kind,position,250,1,0,0,default,site,slot,phase:ConstructionPhase.Ready,refineryUpgraded:upgraded);
    private void Actors(PlayableSnapshot view)
    {
        foreach(var b in view.Buildings)
        {
            if(!world.Actors.TryGetValue(b.Id,out var a))a=world.Building(b.Id,b.Kind.ToString(),true);
            a.Root.position=world.Point(b.Position);a.Root.rotation=Quaternion.Euler(0,73,0);world.FaceBuilding(a,camera);a.Health.parent.gameObject.SetActive(false);
        }
    }
    private void Fits(PlayableBuildingSnapshot b)
    {
        var pad=Pad(b.SiteId+":"+b.SlotId);var bounds=pad.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;var actor=world.Actors[b.Id];float margin=EnvironmentArtProfile.Load().apronMargin;
        Assert.Less(Quaternion.Angle(pad.rotation,actor.Hull.rotation),.001f);
        var foundation=actor.Hull.GetComponentsInChildren<Renderer>().Where(r=>r.name.EndsWith(":foundation-concrete",StringComparison.Ordinal)).ToArray();Assert.AreEqual(1,foundation.Length);var half=Vector2.zero;
        foreach(var renderer in foundation)for(int i=0;i<8;i++)
        {
            var bb=renderer.localBounds;var corner=bb.center+Vector3.Scale(bb.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            var p=pad.InverseTransformPoint(renderer.transform.TransformPoint(corner));
            half=Vector2.Max(half,new Vector2(Mathf.Abs(p.x),Mathf.Abs(p.z)));
        }
        Assert.AreEqual(half.x+margin,bounds.extents.x,.001f,b.Kind+" exact lower-platform width");
        Assert.AreEqual(half.y+margin,bounds.extents.z,.001f,b.Kind+" exact lower-platform depth");
        Assert.IsFalse(pad.GetComponentInChildren<MeshFilter>().sharedMesh.colors.Any(c=>c.b>c.r+.2f&&c.g>c.r+.2f),"No cyan/player-style corner markings");
        Assert.IsEmpty(pad.GetComponentsInChildren<Collider>());
        foreach(var t in pad.GetComponentsInChildren<Transform>())Assert.AreEqual(8,t.gameObject.layer);
    }
    [Test] public void EveryKindAndUpgradedRefineryMatchesItsLowerPlatform()
    {
        var view=SceneView(true);world.RenderSites(view,camera);Actors(view);foreach(var b in view.Buildings)Fits(b);
        var refinery=view.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Refinery);var original=Pad("100:2");
        var upgraded=SceneView(true,true);world.UpdateRefineryModel(world.Actors[refinery.Id],upgraded.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Refinery),30);world.RenderSites(upgraded,camera);
        world.FaceBuilding(world.Actors[refinery.Id],camera);Fits(refinery);Assert.AreSame(original,Pad("100:2"));
        Assert.AreEqual(EnvironmentArtProfile.Load().apronHeight,world.Actors[refinery.Id].Root.position.y,.0001f);
    }
    [UnityTest] public IEnumerator PausedCameraTurnKeepsApronsSquareAndRebindRecalculatesModelEnvelope()
    {
        var view=SceneView(true);world.RenderSites(view,camera);Actors(view);var before=Pad("100:1").GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
        camera.transform.rotation=Quaternion.Euler(45,155,0);world.RenderSites(view,camera);Actors(view);foreach(var b in view.Buildings)Fits(b);
        var data=profile.CopyData();data.factoryModelScale=profile.FactoryModelScale+.5;world.Rebind(PlayableProfile.Create(data));world.RenderSites(view,camera);yield return null;
        Assert.Greater(Pad("100:1").GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size.x,before.size.x);
    }
    [Test] public void DiscoveredApronsBindFogWithoutInspectingHiddenBuildings()
    {
        var initial=SceneView(true);world.RenderSites(initial,camera);var original=Pad("100:0");var supportBounds=Pad("100:2").GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
        var discovered=initial.Sites.Select(s=>s.Site).ToArray();
        var hidden=new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,++tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,discoveredSites:discovered);
        world.RenderSites(hidden,camera);Assert.AreSame(original,Pad("100:0"));Assert.AreEqual(supportBounds,Pad("100:2").GetComponentInChildren<MeshFilter>().sharedMesh.bounds,"Hidden plots retain observed support dimensions");Assert.AreEqual(6,host.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Site pad ")));
        foreach(var pad in host.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Site pad ")))
        {
            var material=pad.GetComponentInChildren<MeshRenderer>().sharedMaterial;Assert.AreEqual("Spacewars/ConstructionApron",material.shader.name);Assert.IsTrue(material.shader.isSupported);Assert.AreSame(world.Fog.Texture,material.GetTexture("_FogMask"));
        }
    }
    [Test] public void ApronProfileControlsHaveMetadataAndRejectInvalidRanges()
    {
        var art=EnvironmentArtProfile.Load();var fields=art.Parameters().Where(f=>f.Path.Contains(".apron")).ToArray();Assert.AreEqual(4,fields.Length);
        foreach(var f in fields){float original=f.Read();Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);foreach(float bad in new[]{float.NaN,float.PositiveInfinity,f.Minimum-f.Step,f.Maximum+f.Step}){f.Write(bad);Assert.Throws<ArgumentException>(()=>art.Validate());}f.Write(original);}art.Validate();
    }
    [UnityTest] public IEnumerator CaptureEmptyOccupiedAndFoundryApronsWithRealModelsAndUnits()
    {
        float volume=AudioListener.volume;AudioListener.volume=0;var oldAmbient=RenderSettings.ambientLight;RenderSettings.ambientLight=new Color(.6f,.64f,.67f);
        var target=new RenderTexture(1600,1000,24);target.Create();camera.targetTexture=target;
        try
        {
            var empty=SceneView(false);world.RenderSites(empty,camera);Actors(empty);
            for(int i=0;i<3;i++){var a=world.Tank(900+i,true,i==1?PlayableEntityKind.Explorer:i==2?PlayableEntityKind.Shkval:PlayableEntityKind.Tank);a.Root.position=new Vector3(-8+i*8,0,-7);a.Health.parent.gameObject.SetActive(false);}
            yield return null;yield return null;Capture(target,"empty-ordinary.png");
            var occupied=SceneView(true);world.RenderSites(occupied,camera);Actors(occupied);yield return null;Capture(target,"occupied-all-kinds.png");
            foreach(var b in occupied.Buildings)Fits(b);
            world.Dispose();yield return null;
            profile=FoundryGreybox.LoadProfile();world=new PlayableWorld(host.transform,profile);world.SetPresentationLayer(8);world.Fog.ShowPublic();
            var home=TerritoryRules.Sites(profile).First(s=>s.Kind==PlayableBuildingKind.Headquarters);var hs=new TerritorySiteSnapshot(home,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true);
            var buildings=new[]{Building(1,home.Kind,home.Position,home.Id,0),Building(2,PlayableBuildingKind.Factory,home.Slots[0].Position,home.Id,home.Slots[0].Id),Building(3,PlayableBuildingKind.Refinery,home.Slots[1].Position,home.Id,home.Slots[1].Id),Building(4,PlayableBuildingKind.ScientificCenter,home.Slots[2].Position,home.Id,home.Slots[2].Id)};
            var view=new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,++tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,new[]{hs});
            var center=world.Point(home.Position);camera.orthographicSize=13;camera.transform.position=center+new Vector3(12,24,-24);camera.transform.LookAt(center);world.RenderSites(view,camera);Actors(view);
            for(int i=0;i<3;i++){var a=world.Tank(900+i,true);a.Root.position=world.Point(new NavPoint(home.Position.X-3+i*3,home.Position.Z-9));a.Health.parent.gameObject.SetActive(false);}
            yield return null;yield return null;Capture(target,"foundry-home.png");
            camera.orthographicSize=8;yield return null;Capture(target,"foundry-close.png");
        }
        finally{camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);RenderSettings.ambientLight=oldAmbient;AudioListener.volume=volume;}
    }
    private void Capture(RenderTexture target,string filename)
    {
        string dir=Path.GetFullPath("../.local/persistent-construction-aprons/evidence");Directory.CreateDirectory(dir);camera.Render();var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(dir,filename),texture.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture);}
    }
}

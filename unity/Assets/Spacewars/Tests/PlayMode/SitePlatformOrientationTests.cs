using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

public sealed class SitePlatformOrientationTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly string[] Maps={"NullDock","BasaltCrown","MossHighland","WhiteMeridian","Foundry","AntennaCity","CrimsonGrove","AiTest","ThreeCrossings","DeafMirror","Campaign","ProvingGround","Nereida"};
    static PlayableProfile Profile(string key)
    {
        var author=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Spacewars.Editor.PreparedMapsAuthoring")).First(t=>t!=null);
        return (PlayableProfile)author.GetMethod("Profile").Invoke(null,new object[]{key});
    }
    static Quaternion Facing(PlayableWorld world,NavPoint point,bool mine)
        =>(Quaternion)typeof(PlayableWorld).GetMethod("SiteFacing",Private,null,new[]{typeof(NavPoint),typeof(Camera),typeof(bool)},null).Invoke(world,new object[]{point,null,mine});
    static PlayableSnapshot View(PlayableProfile p,int mode,long tick=1)
    {
        var sites=p.AuthoredMap.Sites(p).Where(s=>s.Kind!=PlayableBuildingKind.RelayTower).ToArray();
        return new PlayableSnapshot(p.ProfileId,p.Revision,1,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,
            Array.Empty<PlayableEntitySnapshot>(),mode==3?sites.Where(s=>s.Kind==PlayableBuildingKind.Mine).Select(s=>Building(s)).ToArray():Array.Empty<PlayableBuildingSnapshot>(),
            Array.Empty<PlayableProjectileSnapshot>(),null,null,
            sites:mode==1||mode==3?sites.Select(s=>new TerritorySiteSnapshot(s,null,null,0,false,mode==3&&s.Kind==PlayableBuildingKind.Mine?100+s.Id:0,p.AuthoredMap is AntennaCityMap)).ToArray():null,
            discoveredSites:mode==2?sites:null,publicSites:mode==0?sites:null);
    }
    static PlayableBuildingSnapshot Building(TerritorySite s)=>new PlayableBuildingSnapshot(100+s.Id,PlayableOwner.Player,s.Kind,s.Position,250,1,0,0,default,s.Id,0,phase:ConstructionPhase.Ready);
    static Vector3 Expected(PlayableProfile p,TerritorySite site)
    {
        var q=site.Position;var map=p.AuthoredMap;
        Vector3 X()=>new Vector3(-(float)Math.Sign(q.X),0,0);
        Vector3 Z()=>new Vector3(0,0,-(float)Math.Sign(q.Z));
        if(map is AntennaCityMap city)return Quaternion.Euler(0,-(float)city.SiteHeading(site.Id)*Mathf.Rad2Deg,0)*Vector3.forward;
        if(map is DeafMirrorMap)return Vector3.back;
        if(map is BasaltCrownMap||map is NullDockMap)return Math.Abs(q.X)>Math.Abs(q.Z)?X():Z();
        if(map is AiTestMap||map is CrimsonGroveMap)return new Vector3(-(float)q.X,0,-(float)q.Z).normalized;
        if(map is WhiteMeridianMap meridian)return Math.Abs(q.X)>meridian.HalfExtent*.5?X():Z();
        if(map is FoundryMap)return Math.Abs(q.X)>Math.Abs(q.Z)?X():Z();
        if(map is ThreeCrossingsMap crossings){var access=ThreeCrossingsMaterials.PlanMineAccess(ThreeCrossingsSurfaceProfile.Load(),crossings,p).Single(a=>a.Site.Id==site.Id);return new Vector3((float)(access.Connection.X-q.X),0,(float)(access.Connection.Z-q.Z)).normalized;}
        if(map is NereidaMap nereida){int quarter=(site.Id-9)%4;var a=nereida.Point(quarter,0,0);var b=nereida.Point(quarter,-1,0);return new Vector3((float)(b.X-a.X),0,(float)(b.Z-a.Z)).normalized;}
        return Vector3.back;
    }

    [UnityTest]
    public IEnumerator CaptureMapPads([ValueSource(nameof(Maps))] string key)
    {
        string output=Environment.GetEnvironmentVariable("SPACEWARS_SITE_ALIGNMENT_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        var p=Profile(key);var host=new GameObject("Site alignment "+key);var target=new RenderTexture(1600,1000,24);target.Create();
        var ambient=RenderSettings.ambientLight;PlayableWorld world=null;
        try
        {
            world=new PlayableWorld(host.transform,p,staged:key=="Nereida");world.Fog.ShowPublic();
            if(key=="Nereida")host.AddComponent<NereidaPreview>().Build(NereidaPreview.LoadProfile());
            world.RenderSites(View(p,1));
            var camera=new GameObject("Alignment camera").AddComponent<Camera>();camera.transform.SetParent(host.transform);camera.orthographic=true;camera.orthographicSize=11;camera.nearClipPlane=.1f;camera.farClipPlane=2000;camera.targetTexture=target;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.05f,.07f,.08f);
            var sun=new GameObject("Alignment sun").AddComponent<Light>();sun.transform.SetParent(host.transform);sun.type=LightType.Directional;sun.intensity=1.3f;sun.transform.rotation=Quaternion.Euler(55,-30,0);RenderSettings.ambientLight=new Color(.55f,.55f,.55f);
            var sites=p.AuthoredMap.Sites(p);var mines=sites.Where(s=>s.Kind==PlayableBuildingKind.Mine).ToArray();
            var focuses=new[]{mines.First(),mines.Last(),sites.FirstOrDefault(s=>s.Kind==PlayableBuildingKind.Headquarters)??mines.First(),sites.FirstOrDefault(s=>s.Kind==PlayableBuildingKind.Outpost)??mines.Last()};
            yield return null;
            for(int i=0;i<4;i++)
            {
                var focus=world.Point(focuses[i].Position);camera.rect=new Rect(i%2*.5f,i/2*.5f,.5f,.5f);camera.transform.position=focus+new Vector3(0,100,-.01f);camera.transform.LookAt(focus,Vector3.forward);camera.Render();
            }
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();Assert.Greater(pixels.GetPixels32().Where((_,i)=>i%107==0).Distinct().Count(),16);File.WriteAllBytes(Path.Combine(output,key+".png"),pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;Object.DestroyImmediate(pixels);}
            File.AppendAllText(Path.Combine(output,"profiles.txt"),key+" "+p.ProfileId+"@"+p.Revision+" map="+p.AuthoredMap.Id+"; diagnostic Editor site view, no match; crops: first mine, last mine, headquarters, outpost\n");
        }
        finally{world?.Dispose();Object.DestroyImmediate(host);target.Release();Object.DestroyImmediate(target);RenderSettings.ambientLight=ambient;}
    }

    [TestCaseSource(nameof(Maps))]
    public void AllMineModelsPadsAndMemoriesShareAuthoredDirection(string key)
    {
        var p=Profile(key);var host=new GameObject("Orientation contracts "+key);
        var world=new PlayableWorld(host.transform,p,staged:true);
        try
        {
            var mines=p.AuthoredMap.Sites(p).Where(s=>s.Kind==PlayableBuildingKind.Mine).ToArray();
            var camera=new GameObject("Orbit").AddComponent<Camera>();camera.transform.SetParent(host.transform);
            foreach(int mode in new[]{0,1,2,3})foreach(float yaw in new[]{0f,73f,155f})
            {
                camera.transform.rotation=Quaternion.Euler(45,yaw,0);var view=View(p,mode,mode+1);world.RenderSites(view,camera);
                foreach(var site in mines)
                {
                    var pad=host.GetComponentsInChildren<Transform>().Single(t=>t.name=="Site pad "+site.Id+":0");
                    Assert.Greater(Vector3.Dot(pad.forward,Expected(p,site)),.9999f,key+" mine "+site.Id+" state "+mode);
                    if(mode==3){var building=view.Buildings.Single(b=>b.SiteId==site.Id);if(!world.Actors.TryGetValue(building.Id,out var actor))actor=world.Building(building.Id,"mine",true);actor.Root.position=world.Point(site.Position);world.FaceBuilding(actor,camera);Assert.Less(Quaternion.Angle(pad.rotation,actor.Hull.rotation),.001f);}
                }
            }
            var vision=new PlayableVision(0,p.ArenaHalfExtent,p.ArenaHalfExtent,p.VisionCellSize,p.FogEdgeFeather);
            var known=mines.Select(s=>new KnownBuilding(100+s.Id,1,PlayableOwner.Enemy,PlayableBuildingKind.Mine,s.Position,0)).ToArray();
            vision.Refresh(mines.Select(s=>new VisionSource(s.Position,Math.Max(p.VisionCellSize*2,p.MineFootprintRadius))).ToArray(),known);
            vision.Refresh(Array.Empty<VisionSource>(),known);
            var hidden=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,10,10,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null,vision:vision.Snapshot());
            world.RenderMemories(hidden,camera);
            foreach(var site in mines){var memory=host.GetComponentsInChildren<Transform>().Single(t=>t.name=="Remembered building "+(100+site.Id));Assert.Less(Quaternion.Angle(memory.GetChild(0).rotation,Facing(world,site.Position,true)),.001f);}
        }finally{world.Dispose();Object.DestroyImmediate(host);}
    }

    [Test] public void CityChildPadsFollowCourtyardsAndContainUnturnedBuildingFoundations()
    {
        var p=Profile("AntennaCity");var map=(AntennaCityMap)p.AuthoredMap;var host=new GameObject("City pad support contracts");
        var world=new PlayableWorld(host.transform,p,staged:true);
        try
        {
            world.RenderSites(View(p,1));
            foreach(var site in map.Sites(p).Where(s=>s.Slots.Count>0))
            foreach(var slot in site.Slots.Concat(site.DefenseSlots(p)))
            {
                var pad=host.GetComponentsInChildren<Transform>().Single(t=>t.name=="Site pad "+site.Id+":"+slot.Id);
                Assert.Less(Quaternion.Angle(pad.rotation,Quaternion.Euler(0,-(float)map.SiteHeading(site.Id)*Mathf.Rad2Deg,0)),.001f);
            }
            var home=map.Sites(p).First(s=>s.Kind==PlayableBuildingKind.Headquarters);var ordinary=home.Slots[0];
            foreach(var kind in new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Hangar,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter})
            foreach(bool upgraded in new[]{false,true})
            {
                var building=new PlayableBuildingSnapshot(500,PlayableOwner.Player,kind,ordinary.Position,250,1,0,0,default,home.Id,ordinary.Id,phase:ConstructionPhase.Ready,refineryUpgraded:upgraded,scientificCenterUpgraded:upgraded,producerUpgraded:upgraded);
                var view=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,2,2,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),new[]{building},Array.Empty<PlayableProjectileSnapshot>(),null,null,
                    sites:new[]{new TerritorySiteSnapshot(home,PlayableOwner.Player,PlayableOwner.Player,1,false,0,true)});
                world.RenderSites(view);
                var pad=host.GetComponentsInChildren<Transform>().Single(t=>t.name=="Site pad "+home.Id+":"+ordinary.Id);
                var mesh=pad.Find("Technical apron").GetComponent<MeshFilter>().sharedMesh;
                var half=(Vector2)typeof(PlayableWorld).GetMethod("ApronHalf",Private).Invoke(world,new object[]{kind,upgraded});
                foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
                {
                    var point=pad.InverseTransformPoint(world.Point(ordinary.Position)+Quaternion.LookRotation(Vector3.back)*new Vector3(x*half.x,0,z*half.y));
                    Assert.LessOrEqual(Mathf.Abs(point.x),mesh.bounds.extents.x+.0001f,kind+" support X");
                    Assert.LessOrEqual(Mathf.Abs(point.z),mesh.bounds.extents.z+.0001f,kind+" support Z");
                }
                var actor=world.Building(500,kind.ToString(),true);actor.Root.position=world.Point(ordinary.Position);world.FaceBuilding(actor);
                Assert.Less(Quaternion.Angle(actor.Hull.rotation,Quaternion.LookRotation(Vector3.back)),.001f,"Do not rotate factory exits or other child buildings");world.Remove(500);
            }
        }
        finally{world.Dispose();Object.DestroyImmediate(host);}
    }

    [Test] public void RebindRebuildsSiteFramesForTheNewProfile()
    {
        var host=new GameObject("Rebound site frames");var first=Profile("AiTest");var world=new PlayableWorld(host.transform,first,staged:true);
        try
        {
            foreach(var key in new[]{"AiTest","AntennaCity","BasaltCrown","ThreeCrossings","AiTest"})
            {
                var next=Profile(key);world.Rebind(next);
                foreach(var mine in next.AuthoredMap.Sites(next).Where(s=>s.Kind==PlayableBuildingKind.Mine))
                    Assert.Greater(Vector3.Dot(Facing(world,mine.Position,true)*Vector3.forward,Expected(next,mine)),.9999f,key);
            }
        }
        finally{world.Dispose();Object.DestroyImmediate(host);}
    }
}

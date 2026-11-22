using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;
public sealed partial class AntennaCityPreparedTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static Type Authoring=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Spacewars.Editor.PreparedMapsAuthoring")).First(t=>t!=null);
    static T Get<T>(PlayableBootstrap app,string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(app);
    static void Call(PlayableBootstrap app,string name)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(app,null);
    static string Fingerprint(PlayableWorld world)
    {
        var material=(Material)typeof(PlayableWorld).GetField("material",Flags).GetValue(world);
        var scene=PreparedSceneCodec.Capture(world.GraphicsRoot,material,world.Fog.Texture);
        var n=scene.nodes[0];n.name="$root";n.position=Vector3.zero;n.rotation=Quaternion.identity;n.scale=Vector3.one;n.layer=0;n.active=true;
        return PreparedMapCache.Hash(PreparedSceneCodec.Encode("city-equivalence",scene));
    }
    static void Shot(PlayableBootstrap app,string path,float size,Vector3 offset,Vector3? target=null,int forcedLod=-1,bool? reveal=true)
    {
        var camera=Get<Camera>(app,"cameraView");var position=camera.transform.position;var rotation=camera.transform.rotation;float zoom=camera.orthographicSize,far=camera.farClipPlane;var previous=camera.targetTexture;var active=RenderTexture.active;
        var materials=app.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.HasProperty("_FogMask")).Distinct().ToArray();var masks=materials.Select(m=>m.GetTexture("_FogMask")).ToArray();if(reveal==true)Get<PlayableWorld>(app,"world").InspectionReveal();else if(reveal==false)Get<PlayableWorld>(app,"world").InspectionHide();
        var details=Get<PlayableWorld>(app,"world").GraphicsRoot.GetComponentsInChildren<ModelDetailLod>();var hooks=Get<PlayableWorld>(app,"world").GraphicsRoot.GetComponentsInChildren<CrossingSceneryDetail>();var enabled=hooks.Select(h=>h.enabled).ToArray();var levels=details.Select(d=>d.Level).ToArray();
        if(forcedLod>=0){foreach(var h in hooks)h.enabled=false;foreach(var d in details)d.SetDetail(forcedLod);}
        var rt=new RenderTexture(1600,1200,24);rt.Create();var png=new Texture2D(1600,1200,TextureFormat.RGB24,false);
        try{camera.farClipPlane=1000;camera.transform.position=offset;camera.transform.LookAt(target??Vector3.zero);camera.orthographicSize=size;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;png.ReadPixels(new Rect(0,0,1600,1200),0,0);png.Apply();File.WriteAllBytes(path,png.EncodeToPNG());}
        finally{for(int i=0;i<hooks.Length;i++)hooks[i].enabled=enabled[i];for(int i=0;i<details.Length;i++)details[i].SetDetail(levels[i]);for(int i=0;i<materials.Length;i++)materials[i].SetTexture("_FogMask",masks[i]);camera.targetTexture=previous;RenderTexture.active=active;camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=zoom;camera.farClipPlane=far;Object.Destroy(png);Object.Destroy(rt);}
    }
    // Compare the same prepared city at fixed resolution. Readback completes the GPU work
    // before the timer stops; these measurements include the synchronization overhead.
    static void MeasureCityRender(PlayableBootstrap app,StreamWriter log,Vector3 focus=default,Action advance=null)
    {
        var camera=Get<Camera>(app,"cameraView");
        var target=camera.targetTexture;var active=RenderTexture.active;
        var position=camera.transform.position;var rotation=camera.transform.rotation;
        float zoom=camera.orthographicSize,far=camera.farClipPlane;
        var rt=new RenderTexture(1280,960,24);rt.Create();
        var pixel=new Texture2D(1,1,TextureFormat.RGB24,false);
        try{
            camera.targetTexture=rt;camera.farClipPlane=1000;
            foreach(var size in new[]{170f,55f}){
                camera.orthographicSize=size;
                camera.transform.position=focus+new Vector3(0,size*1.8f,-size*1.2f);
                camera.transform.LookAt(focus);
                var samples=new double[31];
                for(int i=-8;i<samples.Length;i++){
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    advance?.Invoke();
                    camera.Render();RenderTexture.active=rt;
                    pixel.ReadPixels(new Rect(0,0,1,1),0,0,false);
                    if(i>=0)samples[i]=timer.Elapsed.TotalMilliseconds;
                }
                Array.Sort(samples);
                log.WriteLine($"{(advance==null?"CAMERA_RENDER_MS":"PRESENT_AND_RENDER_MS")} zoom={size} median={samples[15]:F2} p90={samples[27]:F2} gpu={SystemInfo.graphicsDeviceName}");
            }
        }finally{
            camera.targetTexture=target;RenderTexture.active=active;camera.orthographicSize=zoom;
            camera.farClipPlane=far;camera.transform.SetPositionAndRotation(position,rotation);
            Object.Destroy(rt);Object.Destroy(pixel);
        }
    }
    static IEnumerator DriveThroughCentralPassages(PlayableRuntime runtime,AntennaCityMap city,StreamWriter log)
    {
        const string player="player-1";
        var view=runtime.ParticipantView(player);
        int scout=view.Entities.First(e=>e.Owner==view.Owner && e.Kind==PlayableEntityKind.Explorer).Id;
        var solids=new[]{city.MunicipalHallQuarter,city.StationQuarter,city.WorkshopQuarter}.Select(i=>city.Quarters[i].Single()).ToArray();
        foreach(var target in new[]{city.PointAt(210,73),city.Local(210,48,-20,0),city.PointAt(210,36),city.Local(210,48,20,0),city.Local(-30,48,20,0),city.Local(90,48,-20,0)}){
            var start=runtime.ParticipantView(player).Entities.Single(e=>e.Id==scout).Position;
            var seq=runtime.HumanSequenceWatermark+1;
            var order=new PlayableCommand(runtime.Generation,seq,player,PlayableCommandKind.Move,new[]{scout},target).AsHuman();
            Assert.True(runtime.TrySubmit(order).Accepted);
            double deadline=Time.realtimeSinceStartupAsDouble+90;
            bool moved=false;
            while(true){
                var entity=runtime.ParticipantView(player).Entities.Single(e=>e.Id==scout);
                var at=entity.Position;
                Assert.False(solids.Any(solid=>solid.Contains(at)),"Scout entered a central building");
                moved|=Math.Abs(at.X-start.X)+Math.Abs(at.Z-start.Z)>.25;
                if(moved && entity.OrderStamp?.Sequence==seq && entity.NavigationOutcome==NavigationOutcome.Arrived){
                    Assert.Less(Math.Pow(at.X-target.X,2)+Math.Pow(at.Z-target.Z,2),2);
                    log.WriteLine("CITY_ROUTE_ARRIVED "+target.X+","+target.Z+" tick="+runtime.Latest.Tick);
                    break;
                }
                Assert.Less(Time.realtimeSinceStartupAsDouble,deadline,"scout arrival at "+target.X+","+target.Z);
                yield return null;
            }
        }
    }
    [UnityTest,Timeout(240000)] public IEnumerator PackagedCityLoadsFromLobbyAndMatchesGeneratedGeometry()=>InspectPackagedCity(false);
    [UnityTest,Timeout(120000)] public IEnumerator PackagedStreetFinishes()=>InspectPackagedCity(true);
    static IEnumerator InspectPackagedCity(bool streetFinishesOnly)
    {
        string output=Environment.GetEnvironmentVariable("ANTENNA_CITY_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
        using var log=new StreamWriter(Path.Combine(output,"prepared.txt")){AutoFlush=true};
        Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
        Assert.True((bool)Authoring.GetMethod("Fresh").Invoke(null,null),"Packages must be baked by a previous Editor process");
        string oldCache=PreparedMapCache.DirectoryOverride;bool oldBypass=PreparedMapCache.BypassReads;
        PreparedMapCache.DirectoryOverride=Path.Combine(output,"empty-variants-"+Guid.NewGuid().ToString("N"));PreparedMapCache.BypassReads=false;
        bool kbAdded=Keyboard.current==null,mouseAdded=Mouse.current==null;var kb=kbAdded?InputSystem.AddDevice<Keyboard>():Keyboard.current;var mouse=mouseAdded?InputSystem.AddDevice<Mouse>():Mouse.current;
        GameObject host=null,generatedHost=null,cachedHost=null;PlayableWorld generated=null,cached=null;PlayableMapTerrain generatedMini=null,cachedMini=null;PlayableRuntime runtime=null;
        bool ownLogs=TestMatchLogSession.CurrentDirectory==null;
        try{
            if(ownLogs)TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));Assert.IsNotEmpty(TestMatchLogSession.CurrentDirectory);
            log.WriteLine("TEST_LOG_DIRECTORY "+TestMatchLogSession.CurrentDirectory);
            host=new GameObject("City packaged lobby load");var app=host.AddComponent<PlayableBootstrap>();double deadline=Time.realtimeSinceStartupAsDouble+90;
            while(Get<VisualElement>(app,"lobbyScreen")==null&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.NotNull(Get<VisualElement>(app,"lobbyScreen"));Call(app,"ShowLobby");Get<VisualElement>(app,"lobbyScreen").Q<DropdownField>("lobby-map-choice").value=AntennaCityMap.DisplayName;
            var setup=Get<NativeLobbyConfiguration>(app,"lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=1112026;
            double began=Time.realtimeSinceStartupAsDouble;Call(app,"LaunchLobbyMatch");deadline=began+90;
            while((Get<bool>(app,"inLobby")||Get<bool>(app,"preparing"))&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.False(Get<bool>(app,"inLobby"));Assert.False(Get<bool>(app,"preparing"));runtime=Get<PlayableRuntime>(app,"runtime");Assert.NotNull(runtime);
            Assert.True(Get<PlayableWorld>(app,"world").PreparationCacheHit);Assert.True(Get<PlayableMapTerrain>(app,"mapTerrain").PreparationCacheHit);
            log.WriteLine("FRESH_PROCESS_PACKAGED_MENU_READY_SECONDS "+(Time.realtimeSinceStartupAsDouble-began));
            Assert.False(Directory.Exists(PreparedMapCache.DirectoryOverride)&&Directory.GetFiles(PreparedMapCache.DirectoryOverride,"*AntennaCity*").Length>0,"City must load from packaged defaults, not regenerate a local variant");
            Shot(app,Path.Combine(output,"overview.png"),180,new Vector3(0,300,-240));Shot(app,Path.Combine(output,"ground-center.png"),66,new Vector3(0,110,-75));
            var profile=Get<PlayableProfile>(app,"profile");var city=(AntennaCityMap)profile.AuthoredMap;var pads=AntennaCityPads.Create(city,profile);
            var outpost=city.PointAt(-120,82);
            var outpostFocus=new Vector3((float)outpost.X,0,(float)outpost.Z);
            Shot(app,Path.Combine(output,"outpost.png"),25,outpostFocus+new Vector3(0,65,-40),outpostFocus);
            if(streetFinishesOnly){
                if(Environment.GetEnvironmentVariable("NOVA_CITY_RENDER_BENCH")=="1")MeasureCityRender(app,log);
                var sites=pads.Where(p=>p.Kind==PlayableBuildingKind.Outpost).ToArray();
                Assert.AreEqual(6,sites.Length);
                foreach(var p in sites){
                    var focus=new Vector3((float)p.Center.X,0,(float)p.Center.Z);
                    Shot(app,Path.Combine(output,"outpost-"+p.SiteId+".png"),25,focus+new Vector3(0,65,-40),focus);
                }
                var world=Get<PlayableWorld>(app,"world");
                log.WriteLine("CITY_LOD_GROUPS "+world.GraphicsRoot.GetComponentsInChildren<ModelDetailLod>().Length);
                foreach(var pad in pads.Where(p=>p.Kind!=PlayableBuildingKind.RelayTower)){
                    var obj=world.GraphicsRoot.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Site pad "+pad.SiteId+":0");
                    var expected=Quaternion.Euler(0,-(float)pad.Heading*Mathf.Rad2Deg,0);
                    Assert.Less(Quaternion.Angle(expected,obj.rotation),.1f,"site "+pad.SiteId);
                }
                var ring=city.PointAt(-90,124*Math.Cos(Math.PI/6));
                var street=new Vector3((float)ring.X,0,(float)ring.Z);
                Shot(app,Path.Combine(output,"ring-road.png"),18,street+new Vector3(0,65,-40),street);
                log.WriteLine("MATCH_PROFILE "+Get<NativeBalanceStore>(app,"balanceStore").ActiveIdentity);
                if(Environment.GetEnvironmentVariable("NOVA_CITY_WINDOW_SEQUENCE")=="1")
                    yield return WindowStreetSequence(app,runtime.ParticipantView("player-1"),street,output,log);
                yield break;
            }
            var hallFocus=city.PointAt(210,62);
            var hallTarget=new Vector3((float)hallFocus.X,0,(float)hallFocus.Z);
            Shot(app,Path.Combine(output,"municipal-hall.png"),19,hallTarget+new Vector3(0,64,-44),hallTarget);
            foreach(var (angle,file) in new[]{(-30,"station.png"),(90,"workshop.png")}){
                var site=city.PointAt(angle,61);
                var focus=new Vector3((float)site.X,0,(float)site.Z);
                Shot(app,Path.Combine(output,file),23,focus+new Vector3(0,64,-44),focus);
            }
            log.WriteLine("MATCH_PROFILE "+Get<NativeBalanceStore>(app,"balanceStore").ActiveIdentity);
            var cityGroups=Get<PlayableWorld>(app,"world").GraphicsRoot.GetComponentsInChildren<ModelDetailLod>();
            foreach(var family in AntennaCityArchitecture.Themes)
                Assert.True(cityGroups.Any(group=>group.name.StartsWith("City "+family+" ",StringComparison.Ordinal)),family);
            Assert.AreEqual(1,cityGroups.Count(group=>group.name.StartsWith("City City forum ",StringComparison.Ordinal)));
            Assert.AreEqual(27,pads.Length);Assert.AreEqual(6,pads.Count(p=>p.Kind==PlayableBuildingKind.Headquarters));Assert.AreEqual(6,pads.Count(p=>p.Kind==PlayableBuildingKind.Outpost));Assert.AreEqual(12,pads.Count(p=>p.Kind==PlayableBuildingKind.Mine));Assert.AreEqual(3,pads.Count(p=>p.Kind==PlayableBuildingKind.RelayTower));
            foreach(var pad in pads){Assert.AreEqual(pad.SiteId>=25?city.PlazaHeight:0,pad.Height);foreach(var v in pad.Outer.Footprint.Vertices)Assert.True(city.SupportsFootprint(v,0),"flat pad inside clear ground: "+pad.SiteId);}
            var near=city.PointAt(-120,108);var target=new Vector3((float)near.X,0,(float)near.Z);Shot(app,Path.Combine(output,"keypoints-near.png"),50,target+new Vector3(0,110,-75),target);
            // Artwork review only: no additional AI/movement witness or simulation commands.
            for(int tier=0;tier<3;tier++)Shot(app,Path.Combine(output,"architecture-lod-"+tier+".png"),28,target+new Vector3(0,60,-70),target,tier);
            Shot(app,Path.Combine(output,"hidden-city.png"),28,target+new Vector3(0,60,-70),target,0,false);
            for(int i=0;i<6;i++){var at=city.Headquarters((PlayableOwner)i);var focus=new Vector3((float)at.X,0,(float)at.Z);Shot(app,Path.Combine(output,"apron-"+(i+1)+".png"),23,focus+new Vector3(0,65,-40),focus);}
            // Show an occupied HQ from the actual participant view at normal game scale.
            yield return null;
            var view=runtime.ParticipantView("player-1");var home=view.Buildings.FirstOrDefault(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters);
            if(home!=null){var focus=new Vector3((float)home.Position.X,0,(float)home.Position.Z);Shot(app,Path.Combine(output,"occupied-base.png"),25,focus+new Vector3(0,55,-55),focus);}

            Call(app,"ReturnToLobby");deadline=Time.realtimeSinceStartupAsDouble+30;while(!Get<bool>(app,"inLobby")&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(Get<bool>(app,"inLobby"));
            began=Time.realtimeSinceStartupAsDouble;Call(app,"LaunchLobbyMatch");deadline=began+90;
            while((Get<bool>(app,"inLobby")||Get<bool>(app,"preparing"))&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.False(Get<bool>(app,"inLobby"));Assert.False(Get<bool>(app,"preparing"));Assert.True(Get<PlayableWorld>(app,"world").PreparationCacheHit);Assert.True(Get<PlayableMapTerrain>(app,"mapTerrain").PreparationCacheHit);
            log.WriteLine("REPEAT_PACKAGED_MENU_READY_SECONDS "+(Time.realtimeSinceStartupAsDouble-began));
            runtime=Get<PlayableRuntime>(app,"runtime");
            yield return DriveThroughCentralPassages(runtime,city,log);
            Call(app,"ReturnToLobby");deadline=Time.realtimeSinceStartupAsDouble+30;while(!Get<bool>(app,"inLobby")&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(Get<bool>(app,"inLobby"));
            Object.Destroy(host);host=null;yield return null;
            // Compare only this map's static scene/masks. This is not another gameplay replay.
            cachedHost=new GameObject("cached city");began=Time.realtimeSinceStartupAsDouble;cached=new PlayableWorld(cachedHost.transform,profile,staged:true);yield return cached.Preparation;
            double cachedWorld=Time.realtimeSinceStartupAsDouble-began;began=Time.realtimeSinceStartupAsDouble;cachedMini=new PlayableMapTerrain(profile,staged:true);yield return cachedMini.Preparation;
            log.WriteLine("PREPARED_SECONDS world="+cachedWorld+" minimap="+(Time.realtimeSinceStartupAsDouble-began));Assert.True(cached.PreparationCacheHit);Assert.True(cachedMini.PreparationCacheHit);
            PreparedMapCache.BypassReads=true;generatedHost=new GameObject("generated city");began=Time.realtimeSinceStartupAsDouble;generated=new PlayableWorld(generatedHost.transform,profile,staged:true);yield return generated.Preparation;
            double generatedWorld=Time.realtimeSinceStartupAsDouble-began;began=Time.realtimeSinceStartupAsDouble;generatedMini=new PlayableMapTerrain(profile,staged:true);yield return generatedMini.Preparation;
            log.WriteLine("GENERATED_SECONDS world="+generatedWorld+" minimap="+(Time.realtimeSinceStartupAsDouble-began));Assert.False(generated.PreparationCacheHit);Assert.False(generatedMini.PreparationCacheHit);
            Assert.AreEqual(Fingerprint(generated),Fingerprint(cached),"Complete static scene, materials and masks");generatedMini.ShowPublicTerrain();cachedMini.ShowPublicTerrain();CollectionAssert.AreEqual(generatedMini.Texture.GetRawTextureData(),cachedMini.Texture.GetRawTextureData(),"minimap pixels");Assert.AreNotSame(generated.Fog.Texture,cached.Fog.Texture);
            var meshes=generated.GraphicsRoot.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).ToArray();log.WriteLine("STATIC_GEOMETRY meshes="+meshes.Length+" vertices="+meshes.Sum(m=>m.vertexCount)+" triangles="+meshes.Sum(m=>m.triangles.Length/3));
            var map=(AntennaCityMap)profile.AuthoredMap;var lods=generated.GraphicsRoot.GetComponentsInChildren<ModelDetailLod>();
            log.WriteLine("CITY_LOD_GROUPS "+lods.Length);
            log.WriteLine("CITY_TIER_TRIANGLES "+string.Join("/",Enumerable.Range(0,3).Select(level=>lods.Sum(l=>(level==0?l.OriginalMeshes:level==1?l.NormalMeshes:l.FarMeshes).Sum(m=>m.triangles.Length/3)))));
            var renderers=generated.GraphicsRoot.GetComponentsInChildren<MeshRenderer>();var mats=renderers.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
            log.WriteLine("RENDERERS "+renderers.Length+" MATERIALS "+mats.Length+" SUBMESH_SUBMISSIONS "+meshes.Sum(m=>m.subMeshCount));
            log.WriteLine("MESH_BYTES "+meshes.Distinct().Sum(m=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(m)));
            var textures=mats.SelectMany(m=>m.GetTexturePropertyNames().Select(n=>m.GetTexture(n))).Where(t=>t).Distinct().ToArray();
            log.WriteLine("TEXTURES "+textures.Length+" TEXTURE_BYTES "+textures.Sum(t=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t)));
            log.WriteLine("UNBATCHED_SOURCE_MESHES "+(map.Supports.Count+map.Blocks.Count+map.Streets.Count+map.Yards.Count));var block=TerrainMesh.Prism(map.Blocks.First(b=>b.Footprint.Vertices.Count==4),0,map.BlockHeight);
            try{int before=block.vertexCount;FinalProceduralMesh.IndexOwned(block);log.WriteLine("RECTANGULAR_BLOCK vertices_before="+before+" vertices_after="+block.vertexCount+" triangles="+block.triangles.Length/3);Assert.AreEqual(12,block.triangles.Length/3);}finally{Object.Destroy(block);}
            log.WriteLine("PASS packaged lobby load / static scene and minimap exact equality / independent fog");
        }finally{
            runtime?.RequestStop();if(host)Object.Destroy(host);generatedMini?.Dispose();cachedMini?.Dispose();generated?.Dispose();cached?.Dispose();if(generatedHost)Object.Destroy(generatedHost);if(cachedHost)Object.Destroy(cachedHost);
            PreparedMapCache.DirectoryOverride=oldCache;PreparedMapCache.BypassReads=oldBypass;if(kbAdded)InputSystem.RemoveDevice(kb);if(mouseAdded)InputSystem.RemoveDevice(mouse);if(ownLogs)TestMatchLogSession.Shutdown();
        }
    }
}

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
public sealed class AntennaCityPreparedTests
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
    static void Shot(PlayableBootstrap app,string path,float size,Vector3 offset,Vector3? target=null)
    {
        var camera=Get<Camera>(app,"cameraView");var position=camera.transform.position;var rotation=camera.transform.rotation;float zoom=camera.orthographicSize,far=camera.farClipPlane;var previous=camera.targetTexture;var active=RenderTexture.active;
        var materials=app.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.HasProperty("_FogMask")).Distinct().ToArray();var masks=materials.Select(m=>m.GetTexture("_FogMask")).ToArray();Get<PlayableWorld>(app,"world").InspectionReveal();
        var rt=new RenderTexture(1600,1200,24);rt.Create();var png=new Texture2D(1600,1200,TextureFormat.RGB24,false);
        try{camera.farClipPlane=1000;camera.transform.position=offset;camera.transform.LookAt(target??Vector3.zero);camera.orthographicSize=size;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;png.ReadPixels(new Rect(0,0,1600,1200),0,0);png.Apply();File.WriteAllBytes(path,png.EncodeToPNG());}
        finally{for(int i=0;i<materials.Length;i++)materials[i].SetTexture("_FogMask",masks[i]);camera.targetTexture=previous;RenderTexture.active=active;camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=zoom;camera.farClipPlane=far;Object.Destroy(png);Object.Destroy(rt);}
    }
    [UnityTest,Timeout(240000)] public IEnumerator PackagedCityLoadsFromLobbyAndMatchesGeneratedGeometry()
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
            Assert.AreEqual(27,pads.Length);Assert.AreEqual(6,pads.Count(p=>p.Kind==PlayableBuildingKind.Headquarters));Assert.AreEqual(6,pads.Count(p=>p.Kind==PlayableBuildingKind.Outpost));Assert.AreEqual(12,pads.Count(p=>p.Kind==PlayableBuildingKind.Mine));Assert.AreEqual(3,pads.Count(p=>p.Kind==PlayableBuildingKind.RelayTower));
            foreach(var pad in pads){Assert.AreEqual(pad.SiteId>=25?city.PlazaHeight:0,pad.Height);foreach(var v in pad.Outer.Footprint.Vertices)Assert.True(city.SupportsFootprint(v,0),"flat pad inside clear ground: "+pad.SiteId);}
            var near=city.PointAt(-120,108);var target=new Vector3((float)near.X,0,(float)near.Z);Shot(app,Path.Combine(output,"keypoints-near.png"),50,target+new Vector3(0,110,-75),target);
            Call(app,"ReturnToLobby");deadline=Time.realtimeSinceStartupAsDouble+30;while(!Get<bool>(app,"inLobby")&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(Get<bool>(app,"inLobby"));
            began=Time.realtimeSinceStartupAsDouble;Call(app,"LaunchLobbyMatch");deadline=began+90;
            while((Get<bool>(app,"inLobby")||Get<bool>(app,"preparing"))&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.False(Get<bool>(app,"inLobby"));Assert.False(Get<bool>(app,"preparing"));Assert.True(Get<PlayableWorld>(app,"world").PreparationCacheHit);Assert.True(Get<PlayableMapTerrain>(app,"mapTerrain").PreparationCacheHit);
            log.WriteLine("REPEAT_PACKAGED_MENU_READY_SECONDS "+(Time.realtimeSinceStartupAsDouble-began));
            runtime=Get<PlayableRuntime>(app,"runtime");Call(app,"ReturnToLobby");deadline=Time.realtimeSinceStartupAsDouble+30;while(!Get<bool>(app,"inLobby")&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(Get<bool>(app,"inLobby"));
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
            var map=(AntennaCityMap)profile.AuthoredMap;log.WriteLine("UNBATCHED_SOURCE_MESHES "+(map.Supports.Count+map.Blocks.Count+map.Streets.Count+map.Yards.Count));var block=TerrainMesh.Prism(map.Blocks.First(b=>b.Footprint.Vertices.Count==4),0,map.BlockHeight);
            try{int before=block.vertexCount;FinalProceduralMesh.IndexOwned(block);log.WriteLine("RECTANGULAR_BLOCK vertices_before="+before+" vertices_after="+block.vertexCount+" triangles="+block.triangles.Length/3);Assert.AreEqual(12,block.triangles.Length/3);}finally{Object.Destroy(block);}
            log.WriteLine("PASS packaged lobby load / static scene and minimap exact equality / independent fog");
        }finally{
            runtime?.RequestStop();if(host)Object.Destroy(host);generatedMini?.Dispose();cachedMini?.Dispose();generated?.Dispose();cached?.Dispose();if(generatedHost)Object.Destroy(generatedHost);if(cachedHost)Object.Destroy(cachedHost);
            PreparedMapCache.DirectoryOverride=oldCache;PreparedMapCache.BypassReads=oldBypass;if(kbAdded)InputSystem.RemoveDevice(kb);if(mouseAdded)InputSystem.RemoveDevice(mouse);if(ownLogs)TestMatchLogSession.Shutdown();
        }
    }
}

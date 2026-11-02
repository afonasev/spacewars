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
public sealed class AntennaCityOrdinaryTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap app;PlayableRuntime runtime;StreamWriter log;string output;long sequence;
    T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(app);
    void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(app,null);
    PlayableSnapshot View=>runtime.ParticipantView("player-1");
    static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
    void Send(PlayableCommandKind kind,int[] ids,NavPoint target=default,int site=0,int slot=0,PlayableBuildingKind building=PlayableBuildingKind.Headquarters,int parent=0){Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,++sequence,"player-1",kind,ids,target,siteId:site,slotId:slot,buildingKind:building,parentId:parent).AsHuman()).Accepted);log.WriteLine("COMMAND "+sequence+" "+kind+" "+target.X+","+target.Z);}
    IEnumerator Wait(Func<bool> ready,double seconds,string stage,int groundUnit=0){double start=Time.realtimeSinceStartupAsDouble;while(!ready()&&Time.realtimeSinceStartupAsDouble-start<seconds){Assert.False(runtime.IsStopped,runtime.Latest?.Failure);if(groundUnit!=0){var point=View.Entities.Single(e=>e.Id==groundUnit).Position;Assert.AreEqual("city/street",((AntennaCityMap)Get<PlayableProfile>("profile").AuthoredMap).SupportAt(point)?.Id,stage+" used an elevated shortcut");}yield return null;}Assert.True(ready(),stage+" timeout; tick="+runtime.Latest?.Tick);log.WriteLine("PASS "+stage+" seconds="+(Time.realtimeSinceStartupAsDouble-start));log.Flush();}
    IEnumerator Move(int unit,NavPoint target,string stage){var before=View.Entities.Single(e=>e.Id==unit).Position;Send(PlayableCommandKind.Move,new[]{unit},target);yield return Wait(()=>View.Entities.Any(e=>e.Id==unit&&Distance(e.Position,before)>.25),45,stage+" first motion");yield return Wait(()=>View.Entities.Any(e=>e.Id==unit&&Distance(e.Position,target)<1.4),120,stage+" arrival");}
    IEnumerator MoveOnGround(int unit,NavPoint target,string stage){
        var before=View.Entities.Single(e=>e.Id==unit).Position;Send(PlayableCommandKind.Move,new[]{unit},target);
        yield return Wait(()=>View.Entities.Any(e=>e.Id==unit&&Distance(e.Position,before)>.25),45,stage+" first motion",unit);
        yield return Wait(()=>View.Entities.Any(e=>e.Id==unit&&Distance(e.Position,target)<1.4),120,stage+" arrival",unit);
        Assert.Zero(((AntennaCityMap)Get<PlayableProfile>("profile").AuthoredMap).SurfaceHeight(View.Entities.Single(e=>e.Id==unit).Position));
    }
    void Shot(string name,Vector3 target,float size,Vector3 offset){
        var camera=Get<Camera>("cameraView");var position=camera.transform.position;var rotation=camera.transform.rotation;float zoom=camera.orthographicSize,far=camera.farClipPlane;var previous=camera.targetTexture;var active=RenderTexture.active;
        var materials=host.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.HasProperty("_FogMask")).Distinct().ToArray();var masks=materials.Select(m=>m.GetTexture("_FogMask")).ToArray();Get<PlayableWorld>("world").InspectionReveal();
        var rt=new RenderTexture(1600,1200,24);rt.Create();var png=new Texture2D(1600,1200,TextureFormat.RGB24,false);
        try{camera.farClipPlane=1000;camera.transform.position=target+offset;camera.transform.LookAt(target);camera.orthographicSize=size;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;png.ReadPixels(new Rect(0,0,1600,1200),0,0);png.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),png.EncodeToPNG());}
        finally{for(int i=0;i<materials.Length;i++)materials[i].SetTexture("_FogMask",masks[i]);camera.targetTexture=previous;RenderTexture.active=active;camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=zoom;camera.farClipPlane=far;UnityEngine.Object.Destroy(png);UnityEngine.Object.Destroy(rt);}
    }
    [UnityTest,Timeout(600000)] public IEnumerator LobbySixPlayersRampProductionAiAndCachedReload()
    {
        output=Environment.GetEnvironmentVariable("ANTENNA_CITY_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);log=new StreamWriter(Path.Combine(output,"ordinary.txt")){AutoFlush=true};
        bool kbAdded=Keyboard.current==null,mouseAdded=Mouse.current==null;var keyboard=kbAdded?InputSystem.AddDevice<Keyboard>():Keyboard.current;var mouse=mouseAdded?InputSystem.AddDevice<Mouse>():Mouse.current;
        bool ownLogs=TestMatchLogSession.CurrentDirectory==null;if(ownLogs)TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
        var oldCache=PreparedMapCache.DirectoryOverride;PreparedMapCache.DirectoryOverride=Path.Combine(output,"cache");
        try{
            Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
            host=new GameObject("Antenna City ordinary witness");app=host.AddComponent<PlayableBootstrap>();double deadline=Time.realtimeSinceStartupAsDouble+180;
            while(Get<VisualElement>("lobbyScreen")==null&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.NotNull(Get<VisualElement>("lobbyScreen"));Call("ShowLobby");Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value=AntennaCityMap.DisplayName;
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=1112026;Assert.AreEqual(6,setup.Participants.Count);Assert.True(setup.AntennaCity);
            double began=Time.realtimeSinceStartupAsDouble;Call("LaunchLobbyMatch");deadline=began+180;
            while((Get<bool>("inLobby")||Get<bool>("preparing"))&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.False(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));runtime=Get<PlayableRuntime>("runtime");Assert.NotNull(runtime);Assert.AreEqual(6,View.Participants.Count);
            double first=Time.realtimeSinceStartupAsDouble-began;log.WriteLine("READY_FIRST_SECONDS "+first+" worldCache="+Get<PlayableWorld>("world").PreparationCacheHit+" minimapCache="+Get<PlayableMapTerrain>("mapTerrain").PreparationCacheHit);log.WriteLine("TEST_LOG_DIRECTORY "+TestMatchLogSession.CurrentDirectory);
            var p=Get<PlayableProfile>("profile");var map=(AntennaCityMap)p.AuthoredMap;sequence=runtime.HumanSequenceWatermark;
            var hq=View.Buildings.Single(b=>b.Owner==View.Owner&&b.Kind==PlayableBuildingKind.Headquarters);int scout=View.Entities.Single(e=>e.Owner==View.Owner).Id,index=hq.SiteId-1,plaza=index/2,side=index%2==0?-1:1;
            Assert.Zero(map.SurfaceHeight(hq.Position));Shot("overview",Vector3.zero,180,new Vector3(0,300,-240));Shot("topology",Vector3.zero,160,new Vector3(0,360,0));Shot("street-base",new Vector3((float)hq.Position.X,0,(float)hq.Position.Z),31,new Vector3(0,50,-42));
            log.WriteLine("GEOMETRY meshes="+Get<PlayableWorld>("world").GraphicsRoot.GetComponentsInChildren<MeshFilter>().Length+" triangles="+Get<PlayableWorld>("world").GraphicsRoot.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3));
            Send(PlayableCommandKind.BuildAt,Array.Empty<int>(),site:hq.SiteId,slot:1,building:PlayableBuildingKind.Factory,parent:hq.Id);
            // Actual app Update services the threaded host; no sync Pump, accelerated time or entity injection.
            var approach=map.CenterApproach(index);
            // This ordinary threaded command chain must never need a ramp or elevated plaza.
            foreach(var target in approach){yield return MoveOnGround(scout,target,"ground center approach");}
            Shot("ground-center",Vector3.zero,66,new Vector3(0,110,-75));
            foreach(var target in approach.Reverse().Skip(1)){yield return MoveOnGround(scout,target,"ground return");}
            yield return Move(scout,map.RampFoot(plaza,side,3),"street to ramp");yield return Move(scout,map.RampTop(plaza,side,3),"plaza climb");
            Assert.AreEqual(map.PlazaHeight,map.SurfaceHeight(View.Entities.Single(e=>e.Id==scout).Position),.01);
            var at=map.PointAt(-90+120*plaza,52);Shot("plaza-ramps",new Vector3((float)at.X,2,(float)at.Z),38,new Vector3(0,65,-45));
            yield return Move(scout,map.RampFoot(plaza,-side,3),"opposite ramp descent");Assert.Zero(map.SurfaceHeight(View.Entities.Single(e=>e.Id==scout).Position));
            yield return Wait(()=>View.Buildings.Any(b=>b.Owner==View.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready),90,"factory ready");
            var factory=View.Buildings.Single(b=>b.Owner==View.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready);Send(PlayableCommandKind.QueueTank,new[]{factory.Id});
            yield return Wait(()=>View.Entities.Any(e=>e.Owner==View.Owner&&e.Kind==PlayableEntityKind.Tank),90,"ordinary tank production");
            yield return Wait(()=>View.Participants.Where(o=>o.Id!="player-1").All(o=>{var v=runtime.ParticipantView(o.Id);return v.Buildings.Count(b=>b.Owner==v.Owner)>1;}),120,"all five AI economies progress");
            var capture=runtime.RequestCaptureBytes();yield return Wait(()=>capture.IsCompleted,60,"current capture");Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,capture.Status);File.WriteAllBytes(Path.Combine(output,"ordinary.world"),capture.Result);
            Call("ReturnToLobby");deadline=Time.realtimeSinceStartupAsDouble+60;while(!Get<bool>("inLobby")&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(Get<bool>("inLobby"));
            began=Time.realtimeSinceStartupAsDouble;Call("LaunchLobbyMatch");deadline=began+180;while((Get<bool>("inLobby")||Get<bool>("preparing"))&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.False(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));runtime=Get<PlayableRuntime>("runtime");Assert.True(Get<PlayableWorld>("world").PreparationCacheHit);Assert.True(Get<PlayableMapTerrain>("mapTerrain").PreparationCacheHit);
            log.WriteLine("READY_REPEAT_SECONDS "+(Time.realtimeSinceStartupAsDouble-began));log.WriteLine("PASS ordinary six-player city / flat bases / both ramps / production / AI / capture / cache reload");
        }finally{runtime?.RequestStop();log?.Dispose();if(host)UnityEngine.Object.Destroy(host);PreparedMapCache.DirectoryOverride=oldCache;if(kbAdded)InputSystem.RemoveDevice(keyboard);if(mouseAdded)InputSystem.RemoveDevice(mouse);if(ownLogs)TestMatchLogSession.Shutdown();}
    }
}

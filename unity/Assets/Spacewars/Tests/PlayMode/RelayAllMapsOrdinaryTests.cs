using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;
namespace Spacewars.Tests.PlayMode
{
    public sealed class RelayAllMapsOrdinaryTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject host;PlayableBootstrap app;PlayableRuntime runtime;
        static readonly string[] Names={"Нулевой док","Базальтовый венец","Забытое нагорье","Белый меридиан","Огненный разлом","Нова-Сити","Рубиновая котловина","Полигон","Переправа"};
        static readonly int[] Counts={2,1,4,2,2,3,4,4,2};
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,F).GetValue(app);
        void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,F).Invoke(app,null);
        IEnumerator Wait(Func<bool> ready,double seconds,string label){double until=Time.realtimeSinceStartupAsDouble+seconds;while(!ready()&&Time.realtimeSinceStartupAsDouble<until)yield return null;Assert.True(ready(),label);}
        void Shot(string path,Vector3? focus=null,float? zoom=null)
        {
            var world=Get<PlayableWorld>("world");world.InspectionReveal();var camera=Get<Camera>("cameraView");
            float extent=(float)Get<PlayableProfile>("profile").ArenaHalfExtent;
            camera.farClipPlane=2000;camera.orthographicSize=zoom??extent*1.05f;var at=focus??Vector3.zero;camera.transform.position=at+new Vector3(0,extent*2.5f,-extent*.85f);camera.transform.LookAt(at);
            var old=camera.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(1600,1300,24);rt.Create();var png=new Texture2D(1600,1300,TextureFormat.RGB24,false);
            try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;png.ReadPixels(new Rect(0,0,1600,1300),0,0);png.Apply();File.WriteAllBytes(path,png.EncodeToPNG());}
            finally{camera.targetTexture=old;RenderTexture.active=active;rt.Release();Object.Destroy(rt);Object.Destroy(png);}
        }
        static IEnumerable<int> Cases=>(Environment.GetEnvironmentVariable("RELAY_MAPS_CASES")??string.Join(",",Enumerable.Range(0,Names.Length))).Split(',').Select(int.Parse);
        [UnityTest,Timeout(180000)] public IEnumerator BasaltHumanCommandReachesAndCapturesCentralRelay()
        {
            string output=Environment.GetEnvironmentVariable("RELAY_MAPS_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            using var log=new StreamWriter(Path.Combine(output,"basalt-human.txt")){AutoFlush=true};
            var author=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Spacewars.Editor.PreparedMapsAuthoring")).First(t=>t!=null);
            var p=(PlayableProfile)author.GetMethod("Profile").Invoke(null,new object[]{"BasaltCrown"});
            var setup=new NativeLobbyConfiguration{BasaltCrown=true,HasExplicitSeed=true,ExplicitSeed=3112026,Participants=new List<NativeLobbyParticipant>{
                new NativeLobbyParticipant{Name="Command witness",Human=true,Team=1,Color=0},new NativeLobbyParticipant{Name="Idle opponent",Human=true,Team=2,Color=1,DeviceId=1}}};
            using var r=PlayableRuntime.CreateLobbyMatch(p,94,setup,startPaused:true);using var routes=new UnityHostRouteService();
            double deadline=Time.realtimeSinceStartupAsDouble+150;
            try{
                while(!r.IsPrepared&&Time.realtimeSinceStartupAsDouble<deadline){routes.Service(r,p.NavigationRequestsPerFrame);yield return null;}Assert.True(r.IsPrepared);
                var unit=r.Latest.Entities.Single(u=>u.Owner==PlayableOwner.Player);var origin=unit.Position;
                var target=new NavPoint(p.RelayCaptureRadius-p.ExplorerCollisionRadius,0);
                r.RequestPause(false);
                var submitted=r.TrySubmit(new PlayableCommand(r.Generation,r.HumanSequenceWatermark+1,"player-1",PlayableCommandKind.Move,new[]{unit.Id},target).AsHuman());Assert.True(submitted.Accepted,submitted.Status.ToString());
                bool moved=false,captured=false;long last=-1;
                while(r.Latest.Tick<1800&&Time.realtimeSinceStartupAsDouble<deadline){
                    routes.Service(r,p.NavigationRequestsPerFrame);var v=r.Latest;Assert.IsNull(v.Failure);
                    var u=v.Entities.Single(e=>e.Id==unit.Id);moved|=Math.Sqrt(Math.Pow(u.Position.X-origin.X,2)+Math.Pow(u.Position.Z-origin.Z,2))>.75;
                    captured|=v.Sites.Any(x=>x.Site.Kind==PlayableBuildingKind.RelayTower&&x.Owner==PlayableOwner.Player);
                    if(v.Tick/150!=last){last=v.Tick/150;log.WriteLine("tick="+v.Tick+" position="+u.Position.X+","+u.Position.Z+" outcome="+u.NavigationOutcome+" order="+u.CurrentOrder?.Kind+" receipts="+string.Join(";",r.ReceiptsAfter(0).Select(x=>x.Status+":"+x.Message))+" projection="+routes.LastProjectionFailure);}
                    if(captured)break;yield return null;
                }
                var capture=r.RequestCaptureBytes();while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline){routes.Service(r,p.NavigationRequestsPerFrame);yield return null;}
                if(capture.IsCompletedSuccessfully)File.WriteAllBytes(Path.Combine(output,"basalt-human.world"),capture.Result);
                log.WriteLine("RESULT moved="+moved+" captured="+captured+" tick="+r.Latest.Tick);
                Assert.True(moved,"ordinary command first motion");Assert.True(captured,"ordinary player command must finish relay capture");
            }finally{r.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>r.IsStopped,5000));}
        }
        [UnityTest,Timeout(240000)] public IEnumerator OrdinaryLobbyLayoutsLoadMoveAndCaptureWithNativeAi([ValueSource(nameof(Cases))] int i)
        {
            string output=Environment.GetEnvironmentVariable("RELAY_MAPS_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);
            using var log=new StreamWriter(Path.Combine(output,"ordinary-"+i+".txt")){AutoFlush=true};
            Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
            string oldCache=PreparedMapCache.DirectoryOverride;PreparedMapCache.DirectoryOverride=Path.Combine(output,"isolated-cache-"+i);
            bool ownKb=Keyboard.current==null,ownMouse=Mouse.current==null;var kb=ownKb?InputSystem.AddDevice<Keyboard>():Keyboard.current;var mouse=ownMouse?InputSystem.AddDevice<Mouse>():Mouse.current;
            bool requirePrepared=Environment.GetEnvironmentVariable("RELAY_MAPS_PREPARED")=="1";
            try{
                host=new GameObject("All relay maps ordinary lobby");app=host.AddComponent<PlayableBootstrap>();
                yield return Wait(()=>Get<VisualElement>("lobbyScreen")!=null,120,"lobby startup");Call("ShowLobby");
                var choices=Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice");Assert.AreEqual(9,choices.choices.Count);
                {
                    choices.value=choices.choices.Single(s=>s.Contains(Names[i]));var draft=Get<NativeLobbyConfiguration>("lobbySetup");draft.HasExplicitSeed=true;draft.ExplicitSeed=3112026;
                    foreach(var seat in draft.Participants)seat.Human=false;
                    for(int pass=0;pass<(requirePrepared?2:1);pass++){
                        double began=Time.realtimeSinceStartupAsDouble;Call("LaunchLobbyMatch");
                        yield return Wait(()=>!Get<bool>("inLobby")&&!Get<bool>("preparing"),120,Names[i]+" menu ready");
                        runtime=Get<PlayableRuntime>("runtime");Assert.NotNull(runtime);runtime.RequestPause(true);
                        yield return Wait(()=>runtime.Latest.Paused,15,"pause for inspection");Call("UpdateFrame");
                        var world=Get<PlayableWorld>("world");var mini=Get<PlayableMapTerrain>("mapTerrain");var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;
                        Assert.AreEqual(Counts[i],frame.Sites.Count(s=>s.Site.Kind==PlayableBuildingKind.RelayTower));
                        log.WriteLine("READY map="+Names[i]+" pass="+pass+" seconds="+(Time.realtimeSinceStartupAsDouble-began)+" worldHit="+world.PreparationCacheHit+" minimapHit="+mini.PreparationCacheHit+" towers="+Counts[i]);
                        if(requirePrepared){Assert.True(world.PreparationCacheHit,Names[i]+" prepared world");Assert.True(mini.PreparationCacheHit,Names[i]+" prepared minimap");}
                        if(pass==0){
                            Assert.AreEqual(Counts[i],world.GraphicsRoot.GetComponentsInChildren<RelayTowerPresentation>().Length,"actual relay models "+Names[i]);
                            Shot(Path.Combine(output,"map-"+i+".png"));
                            if(i==1){
                                Shot(Path.Combine(output,"basalt-north.png"),new Vector3(16,0,40),18);
                                Shot(Path.Combine(output,"basalt-south.png"),new Vector3(-16,0,-40),18);
                            }
                            if(requirePrepared&&i==1){var details=world.GraphicsRoot.GetComponentsInChildren<ModelDetailLod>();
                                for(int level=0;level<3;level++){foreach(var d in details)d.SetDetail(level);Shot(Path.Combine(output,"basalt-pockets-lod"+level+".png"),new Vector3(16,0,40),18);Assert.True(details.All(d=>d.Level==level));}
                            }
                        }
                        if(pass==0){
                            var starts=frame.Entities.ToDictionary(u=>u.Id,u=>u.Position);long initial=frame.Tick,departure=-1;bool captured=false;double deadline=Time.realtimeSinceStartupAsDouble+150;
                            runtime.RequestPause(false);
                            while(runtime.Latest.Tick-initial<1800&&Time.realtimeSinceStartupAsDouble<deadline){
                                var v=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;Assert.IsNull(runtime.Latest.Failure);
                                if(departure<0&&v.Entities.Any(u=>starts.TryGetValue(u.Id,out var p)&&Math.Sqrt(Math.Pow(p.X-u.Position.X,2)+Math.Pow(p.Z-u.Position.Z,2))>.75))departure=v.Tick-initial;
                                captured|=v.Sites.Any(s=>s.Site.Kind==PlayableBuildingKind.RelayTower&&s.Owner.HasValue);
                                if(departure>=0&&captured)break;yield return null;
                            }
                            log.WriteLine("END_UNITS "+string.Join(";",runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview.Entities.Select(u=>u.Id+":"+u.Position.X+","+u.Position.Z+":"+u.NavigationOutcome)));
                            log.WriteLine("AI map="+Names[i]+" departure="+departure+" captured="+captured+" ticks="+(runtime.Latest.Tick-initial));
                            if(i==1&&!captured){
                                log.WriteLine("RECEIPTS "+string.Join(";",runtime.ReceiptsAfter(0).Select(x=>x.Sequence+":"+x.Status+":"+x.Message)));
                                var saved=runtime.RequestCaptureBytes();while(!saved.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
                                if(saved.IsCompletedSuccessfully)File.WriteAllBytes(Path.Combine(output,"basalt-ai.world"),saved.Result);
                            }
                            Assert.That(departure,Is.GreaterThanOrEqualTo(0).And.LessThan(300),Names[i]+" early ordinary movement");Assert.True(captured,Names[i]+" ordinary relay capture");
                        }
                        Call("ReturnToLobby");yield return Wait(()=>Get<bool>("inLobby")&&Get<PlayableRuntime>("runtime")==null,30,"return to lobby");runtime=null;
                    }
                }
            }finally{runtime?.RequestStop();if(host)Object.Destroy(host);PreparedMapCache.DirectoryOverride=oldCache;if(ownKb)InputSystem.RemoveDevice(kb);if(ownMouse)InputSystem.RemoveDevice(mouse);}
        }
    }
}

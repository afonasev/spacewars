using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Tests.PlayMode
{
    public sealed class LobbyMapLaunchSmokeTests : InputTestFixture
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const int Seed=19092026;
        GameObject host;PlayableBootstrap app;
        T Get<T>(string field)=>app?(T)typeof(PlayableBootstrap).GetField(field,Hidden).GetValue(app):default;
        void Set(string field,object value)=>typeof(PlayableBootstrap).GetField(field,Hidden).SetValue(app,value);
        object Call(string method,params object[] args)=>typeof(PlayableBootstrap).GetMethods(Hidden).Single(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(app,args);
        [Serializable] sealed class Result {public string map,expectedMapId,actualMapId,status="NotRun",reason="",screenshot;public int participants,humans,errors;public bool spectator;public long firstTick,lastTick;public float startupSeconds;public List<string> logs=new List<string>();}
        [Serializable] sealed class Report {public int seed=Seed,liveTicks;public string source;public List<Result> maps=new List<Result>();}
        public override void Setup(){base.Setup();InputSystem.AddDevice<Keyboard>().MakeCurrent();InputSystem.AddDevice<Mouse>().MakeCurrent();}
        public override void TearDown(){if(host)UnityEngine.Object.DestroyImmediate(host);base.TearDown();}
        void CreateHost()
        {
            host=new GameObject("Map launch audit");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;Set("profile",PlayableProfile.ThreeCrossingsDefault);
            Call("CreateWorld");Call("CreateHud");Call("CreateSpectatorHud");Set("input",host.AddComponent<PlayableInput>());Call("BindLocalKeyboardInput");Call("CreateLobby");Call("ShowLobby");
        }
        void Frame(){InputSystem.QueueDeltaStateEvent(Mouse.current.position,new Vector2(Screen.width/2f,Screen.height/2f));InputSystem.Update();Call("OnApplicationFocus",true);Call("UpdateFrame");}
        string UiFailure()=>Get<Label>("lobbyStatus")?.text??"";
        IEnumerator Capture(string path)
        {
            var root=Get<VisualElement>("root");var panel=host.GetComponent<UIDocument>().panelSettings;var camera=Get<Camera>("cameraView");
            var image=new RenderTexture(1280,800,24);var scene=new RenderTexture(1280,800,24);image.Create();scene.Create();var layer=new VisualElement{pickingMode=PickingMode.Ignore};
            layer.style.position=Position.Absolute;layer.style.left=layer.style.top=layer.style.right=layer.style.bottom=0;layer.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(scene));root.Insert(0,layer);
            var prior=camera.targetTexture;float aspect=camera.aspect;camera.targetTexture=scene;camera.aspect=1280f/800;root.style.width=1280;root.style.height=800;panel.targetTexture=image;
            try{
                for(int i=0;i<4;i++){Frame();camera.Render();yield return null;}
                var active=RenderTexture.active;RenderTexture.active=image;var pixels=new Texture2D(1280,800,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1280,800),0,0);pixels.Apply();RenderTexture.active=active;
                File.WriteAllBytes(path,pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
            }finally{panel.targetTexture=null;camera.targetTexture=prior;camera.aspect=aspect;layer.RemoveFromHierarchy();image.Release();scene.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(scene);}
        }
        [UnityTest] public IEnumerator EveryLobbyMapReachesLiveGameplay()=>RunCatalog(60,15,"launch");
        [UnityTest,Timeout(1800000)] public IEnumerator EveryLobbyMapContinuesWithoutErrors()=>RunCatalog(1800,180,"continuation");
        IEnumerator RunCatalog(int liveTicks,float liveBudget,string packet)
        {
            var folder=Environment.GetEnvironmentVariable("SPACEWARS_MAP_LAUNCH_EVIDENCE");if(!string.IsNullOrEmpty(folder)){folder=Path.Combine(folder,packet);Directory.CreateDirectory(folder);}
            var report=new Report{liveTicks=liveTicks,source=Environment.GetEnvironmentVariable("SPACEWARS_MAP_LAUNCH_SOURCE")??"unrecorded"};Result current=null;
            bool previousIgnore=LogAssert.ignoreFailingMessages;
            void Log(string message,string stack,LogType type){if(current==null)return;current.logs.Add(type+": "+message+"\n"+stack);if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)current.errors++;}
            void Save(){if(!string.IsNullOrEmpty(folder))File.WriteAllText(Path.Combine(folder,"results.json"),JsonUtility.ToJson(report,true));}
            Application.logMessageReceived+=Log;LogAssert.ignoreFailingMessages=true;
            try{
                CreateHost();var maps=Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").choices.ToArray();UnityEngine.Object.DestroyImmediate(host);host=null;app=null;yield return null;
                Assert.AreEqual(9,maps.Length,"Every shipped lobby map must be covered.");var ordered=maps.OrderBy(m=>m==AntennaCityMap.DisplayName?0:1).ToArray();
                for(int index=0;index<ordered.Length;index++){
                    current=new Result{map=ordered[index]};report.maps.Add(current);Save();if(!string.IsNullOrEmpty(folder))File.WriteAllText(Path.Combine(folder,"phase.txt"),(index+1)+"/"+ordered.Length+" "+current.map+" preparation");
                    PlayableRuntime launched=null;Exception setup=null;float started=Time.realtimeSinceStartup;
                    try{
                        CreateHost();Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value=current.map;
                        var draft=Get<NativeLobbyConfiguration>("lobbySetup");draft.HasExplicitSeed=true;draft.ExplicitSeed=Seed;Call("RebuildRoster");Call("RefreshLobbyValidation");
                        current.expectedMapId=Get<PlayableProfile>("profile").AuthoredMap?.Id;current.participants=draft.Participants.Count;current.humans=draft.Participants.Count(p=>p.Human);current.spectator=draft.Spectator;
                        if(!Get<Button>("launchButton").enabledSelf)throw new InvalidOperationException("Launch disabled: "+UiFailure());Call("LaunchLobbyMatch");
                    }catch(Exception error){setup=error.InnerException??error;}
                    if(setup!=null){current.status="FAIL";current.reason="Lobby setup/selection: "+setup;}
                    else{
                        float end=Time.realtimeSinceStartup+120;bool ready=false;
                        while(Time.realtimeSinceStartup<end){
                            Frame();var active=Get<PlayableRuntime>("runtime");if(active!=null)launched=active;
                            if(!Get<bool>("preparing")&&Get<bool>("inLobby")){current.status="FAIL";current.reason="Returned to lobby: "+UiFailure()+" runtime="+launched?.Latest?.Failure;break;}
                            if(launched!=null&&launched.IsStopped){current.status="FAIL";current.reason="Runtime stopped during launch: "+launched.Latest?.Failure;break;}
                            if(!Get<bool>("preparing")&&!Get<bool>("inLobby")&&launched?.Latest!=null){ready=true;break;}
                            yield return null;
                        }
                        current.startupSeconds=Time.realtimeSinceStartup-started;
                        if(ready){
                            current.firstTick=launched.Latest.Tick;current.actualMapId=Get<PlayableProfile>("profile").AuthoredMap?.Id;long target=current.firstTick+liveTicks;end=Time.realtimeSinceStartup+liveBudget;
                            while(Time.realtimeSinceStartup<end&&launched.Latest.Tick<target&&!launched.IsStopped&&!Get<bool>("inLobby")&&string.IsNullOrEmpty(launched.Latest.Failure)){Frame();yield return null;}
                            current.lastTick=launched.Latest.Tick;
                            bool live=current.lastTick>=target&&!Get<bool>("inLobby")&&!Get<bool>("preparing")&&!launched.IsStopped&&string.IsNullOrEmpty(launched.Latest.Failure)&&launched.Latest.Entities.Count>0&&Get<Camera>("cameraView").enabled&&Get<PlayableWorld>("world").Actors.Count>0;
                            current.status=live&&current.actualMapId==current.expectedMapId&&current.errors==0?"PASS":"FAIL";
                            current.reason=current.status=="PASS"?"Normal lobby launch, rendered world and "+liveTicks+" live ticks.":"Live/map/error contract failed; failure="+launched.Latest.Failure+" paused="+launched.Latest.Paused+" stopped="+launched.IsStopped;
                        }else if(current.status=="NotRun")current.reason="Startup did not finish within the recorded120s observation budget.";
                    }
                    if(host&&Get<PlayableRuntime>("runtime")!=null)launched=Get<PlayableRuntime>("runtime");
                    if(host&&!string.IsNullOrEmpty(folder)){current.screenshot="map-"+index.ToString("D2")+".png";yield return Capture(Path.Combine(folder,current.screenshot));}
                    if(launched!=null){launched.RequestStop();float end=Time.realtimeSinceStartup+30;while(!launched.IsStopped&&Time.realtimeSinceStartup<end){if(app)Frame();yield return null;}if(!launched.IsStopped){current.status="FAIL";current.reason+="; owned Runtime did not stop within30s";}}
                    if(current.errors>0&&current.status=="PASS"){current.status="FAIL";current.reason="Unity errors during rendering/cleanup.";}
                    Save();TestContext.WriteLine(current.map+" "+current.status+" ticks="+current.firstTick+".."+current.lastTick+" errors="+current.errors+" "+current.reason);
                    if(host)UnityEngine.Object.DestroyImmediate(host);host=null;app=null;current=null;yield return null;yield return null;
                }
                Save();Assert.AreEqual(maps.Length,report.maps.Count);Assert.IsTrue(report.maps.All(m=>m.status=="PASS"),string.Join("\n",report.maps.Where(m=>m.status!="PASS").Select(m=>m.map+": "+m.status+" "+m.reason)));
            }finally{Application.logMessageReceived-=Log;LogAssert.ignoreFailingMessages=previousIgnore;Save();}
        }
    }
}

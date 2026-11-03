using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
namespace Spacewars.Input.Tests
{
    public sealed class WreckContextInputTests:InputTestFixture
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject go;PlayableBootstrap hud;PlayableInput input;Mouse mouse;Keyboard keyboard;
        void Set(string n,object v)=>typeof(PlayableBootstrap).GetField(n,F).SetValue(hud,v);
        T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,F).GetValue(hud);
        object Call(string n,params object[] args)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==args.Length).Invoke(hud,args);
        [SetUp] public override void Setup(){base.Setup();go=new GameObject("Wreck context input");hud=go.AddComponent<PlayableBootstrap>();hud.enabled=false;input=go.AddComponent<PlayableInput>();mouse=InputSystem.AddDevice<Mouse>();keyboard=InputSystem.AddDevice<Keyboard>();input.AssignedMouse=mouse;input.AssignedKeyboard=keyboard;input.SourceSeatSemantics=true;input.Order=(p,a,s)=>Call("Order",p,a,s);}
        [TearDown] public override void TearDown(){Get<PlayableRuntime>("runtime")?.RequestStop();UnityEngine.Object.DestroyImmediate(go);base.TearDown();}
        [UnityTest,Timeout(30000)] public IEnumerator RightClickOnVisibleWreckOrdersOneManualShotThroughActualAdapter()=>RunWreckScenario(false);
        [UnityTest,Timeout(30000)] public IEnumerator InFlightShotsPreserveFreshWreckThroughItsProtectionDeadline()=>RunWreckScenario(true);
        private IEnumerator RunWreckScenario(bool protectionOnly)
        {
            // Isolated fixture health only: obtain a real death quickly without diagnostic damage injection.
            var data=PlayableProfile.Default.CopyData();data.tankHealth=1;var p=PlayableProfile.Create(data);Set("profile",p);Call("CreateHud");
            var starts=new[]{new OfflineStart("one",1,new NavPoint(-24,-24),new NavPoint(-10,-4),pin:1),new OfflineStart("two",2,new NavPoint(24,24),new NavPoint(10,-4),pin:2)};
            var config=new OfflineMatchConfiguration(p,"wreck-input-test","wreck-input-flat","explicit-costs",7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,starts.Select(x=>new TerritorySite(x.SiteId,PlayableBuildingKind.Headquarters,x.Position,Array.Empty<TerritorySlot>())).ToArray(),Array.Empty<NavObstacle>(),new double[,]{{0,1},{1,0}},scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Hawk,new NavPoint(-8,0)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,default)});
            using(var runtime=new PlayableRuntime(config,1,true))using(var routes=new UnityHostRouteService()){
                Set("runtime",runtime);Set("generation",1L);Set("world",new PlayableWorld(go.transform,p));
                var cameraObject=new GameObject("Wreck input camera");var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=16;camera.transform.position=new Vector3(-4,24,-20);camera.transform.LookAt(new Vector3(-4,0,0));Set("cameraView",camera);
                void Pump()=>routes.Service(runtime.Requests,runtime.Answers,1,runtime.NavigationBinding.Geometry,p,64);
                try{
                    runtime.RequestPause(false);double until=Time.realtimeSinceStartupAsDouble+5;while(runtime.Latest.Paused&&Time.realtimeSinceStartupAsDouble<until){Pump();yield return null;}
                    var hawk=runtime.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Hawk);var tank=runtime.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank);
                    Assert.True(runtime.TrySubmit(new PlayableCommand(1,1,"player-1",PlayableCommandKind.Attack,new[]{hawk.Id},targetId:tank.Id)).Accepted);
                    until=Time.realtimeSinceStartupAsDouble+10;while(!runtime.Latest.Wrecks.Any(w=>w.Id==tank.Id)&&Time.realtimeSinceStartupAsDouble<until){Pump();yield return null;}
                    Assert.IsNull(runtime.Latest.Failure);var wreck=runtime.Latest.Wrecks.Single(w=>w.Id==tank.Id);Assert.True(wreck.Blocks(runtime.Latest.Tick));
                    Assert.AreEqual(wreck.RevealTick+30,wreck.InvulnerableUntilTick);
                    until=Time.realtimeSinceStartupAsDouble+3;
                    while(runtime.Latest.Tick<wreck.InvulnerableUntilTick&&Time.realtimeSinceStartupAsDouble<until)
                    {
                        var protectedView=runtime.Latest;
                        if(protectedView.Tick<wreck.InvulnerableUntilTick)Assert.True(protectedView.Wrecks.Any(w=>w.Id==tank.Id&&w.DestroyedTick<0),"In-flight Hawk shots cannot erase the protected wreck");
                        Pump();yield return null;
                    }
                    Assert.GreaterOrEqual(runtime.Latest.Tick,wreck.InvulnerableUntilTick);
                    if(!protectionOnly)
                    {
                    Set("view",runtime.Latest);Set("sequence",1L);var selection=Get<HashSet<int>>("selection");selection.Add(hawk.Id);Vector2 point=camera.WorldToScreenPoint(Get<PlayableWorld>("world").Point(wreck.Position));Assert.True((bool)Call("IsWreckAt",point));
                    InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=2});InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=0});InputSystem.Update();input.Poll();
                    Assert.AreEqual(2,Get<long>("sequence"),"One right click must submit exactly one context command.");
                    until=Time.realtimeSinceStartupAsDouble+2;while(runtime.Latest.Entities.Single(e=>e.Id==hawk.Id).CurrentOrder?.TargetId!=wreck.Id&&Time.realtimeSinceStartupAsDouble<until){Pump();yield return null;}
                    Assert.AreEqual(wreck.Id,runtime.Latest.Entities.Single(e=>e.Id==hawk.Id).CurrentOrder?.TargetId);
                    until=Time.realtimeSinceStartupAsDouble+5;while(runtime.Latest.Wrecks.Any(w=>w.Id==wreck.Id&&w.Blocks(runtime.Latest.Tick))&&Time.realtimeSinceStartupAsDouble<until){Pump();yield return null;}
                    Assert.False(runtime.Latest.Wrecks.Any(w=>w.Id==wreck.Id&&w.Blocks(runtime.Latest.Tick)));Assert.IsNull(runtime.Latest.Failure);
                    }
                }finally{Set("cameraView",null);UnityEngine.Object.DestroyImmediate(cameraObject);runtime.RequestStop();}
                double stop=Time.realtimeSinceStartupAsDouble+3;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<stop)yield return null;Assert.True(runtime.IsStopped);
            }
        }
    }
}

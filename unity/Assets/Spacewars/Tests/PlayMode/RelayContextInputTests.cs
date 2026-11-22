using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace Spacewars.Input.Tests
{
    public sealed class RelayContextInputTests:InputTestFixture
    {
        [UnityTest,Timeout(90000)]
        public IEnumerator RightClickTowerOnNullDockDescendsAndCapturesThroughOrdinaryHost()
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            const int seed=26102026;
            var map=new NullDockMap(JsonUtility.FromJson<NullDockProfileData>(Resources.Load<TextAsset>("NullDockProfile").text));
            var p=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
            var basis=map.Configuration(p,seed,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)});
            var start=map.Point(42,80);
            var relay=basis.Sites.Single(s=>s.Id==9);
            Assert.AreEqual(map.TerraceHeight,map.SurfaceHeight(start),"Tank starts on a raised deck");
            var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,basis.Roster.ToArray(),basis.Starts.ToArray(),
                basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[2,2],scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,start)},terrain:map.Id);
            var go=new GameObject("Relay context input");
            var hud=go.AddComponent<PlayableBootstrap>();hud.enabled=false;
            void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,flags).SetValue(hud,value);
            T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,flags).GetValue(hud);
            object Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethod(name,flags).Invoke(hud,args);
            var input=go.AddComponent<PlayableInput>();
            var mouse=InputSystem.AddDevice<Mouse>();input.AssignedMouse=mouse;input.AssignedKeyboard=InputSystem.AddDevice<Keyboard>();input.SourceSeatSemantics=true;
            input.Order=(point,attack,append)=>Call("Order",point,attack,append);
            try{
            using(var runtime=new PlayableRuntime(config,1,true))
            using(var routes=new UnityHostRouteService())
            using(var world=new PlayableWorld(go.transform,p,staged:true)){
                try{
                    Set("profile",p);Set("runtime",runtime);Set("generation",1L);Set("world",world);
                    Call("CreateHud");
                    var camera=new GameObject("Relay input camera").AddComponent<Camera>();camera.transform.SetParent(go.transform);camera.orthographic=true;camera.orthographicSize=18;
                    var basePoint=world.Point(relay.Position);
                    camera.transform.position=basePoint+new Vector3(0,26,-24);camera.transform.LookAt(basePoint);
                    Set("cameraView",camera);
                    void Service()=>routes.Service(runtime,p.NavigationRequestsPerFrame);
                    PlayableSnapshot View()=>runtime.ParticipantView("player-1");
                    runtime.RequestPause(false);
                    double until=Time.realtimeSinceStartupAsDouble+10;
                    while((View()==null||View().Paused)&&Time.realtimeSinceStartupAsDouble<until){Service();yield return null;}
                    var view=View();Assert.NotNull(view);Assert.False(view.Paused);
                    var tank=view.Entities.Single(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Tank);
                    Assert.AreEqual(start,tank.Position);Assert.IsNull(view.MapRelayControl.Single(x=>x.SiteId==relay.Id).Owner);
                    Set("view",view);Get<HashSet<int>>("selection").Add(tank.Id);world.RenderSites(view,camera);
                    Assert.True(world.TryRelayIncomeAnchor(relay.Id,out var top));
                    // Aim at the model with a ray whose ground hit cannot start capture.
                    var target=Vector3.Lerp(basePoint,top,.75f);
                    float run=(float)(p.RelayCaptureRadius+4)*20/(target.y-basePoint.y);
                    camera.transform.position=target+new Vector3(0,20,-run);camera.transform.LookAt(target);
                    Vector2 point=camera.WorldToScreenPoint(target);
                    double Distance(NavPoint a,NavPoint b)=>Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
                    Assert.Greater(Distance((NavPoint)Call("Ground",point),relay.Position),p.RelayCaptureRadius,
                        "The default ground click must land outside capture range");
                    InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=2});
                    InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=0});
                    InputSystem.Update();input.Poll();
                    bool moved=false,descended=false,arrived=false,captured=false;double finalDistance=double.NaN;until=Time.realtimeSinceStartupAsDouble+65;
                    while(Time.realtimeSinceStartupAsDouble<until){
                        Call("DrainHumanOrders");Service();
                        var current=View();Assert.IsNull(runtime.Latest.Failure);
                        var actor=current.Entities.Single(e=>e.Id==tank.Id);
                        moved|=!actor.Position.Equals(start);
                        descended|=map.SurfaceHeight(actor.Position)<map.TerraceHeight;
                        finalDistance=Distance(actor.Position,relay.Position);
                        arrived=actor.NavigationOutcome==NavigationOutcome.Arrived&&finalDistance<=p.RelayCaptureRadius;
                        captured=current.MapRelayControl.Single(x=>x.SiteId==relay.Id).Owner==current.Owner;
                        if(arrived&&captured)break;
                        yield return null;
                    }
                    TestContext.WriteLine($"Relay context order: profile={p.ProfileId}@{p.Revision} map={map.Id}@{map.Revision} seed={seed} tick={View().Tick} moved={moved} descended={descended} distance={finalDistance:F2}/{p.RelayCaptureRadius:F2} arrived={arrived} captured={captured} notice={Get<string>("notice")}");
                    Assert.AreEqual(1,Get<long>("sequence"),"The click must submit one ordinary Move");
                    Assert.True(moved&&descended&&arrived&&captured,"One click must lead to ramp descent, arrival inside capture range and actual capture");
                }finally{Set("world",null);Set("runtime",null);runtime.RequestStop();}
                double stop=Time.realtimeSinceStartupAsDouble+5;
                while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<stop)yield return null;
                Assert.True(runtime.IsStopped);
            }
            }finally{UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}

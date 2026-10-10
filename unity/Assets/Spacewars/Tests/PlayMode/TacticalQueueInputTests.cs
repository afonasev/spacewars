using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Spacewars.Input.Tests
{
    public sealed class TacticalQueueInputTests : InputTestFixture
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject go;PlayableBootstrap hud;PlayableInput input;Keyboard keyboard;Mouse mouse;
        void Set(string n,object x)=>typeof(PlayableBootstrap).GetField(n,F).SetValue(hud,x);
        T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,F).GetValue(hud);
        object Call(string n,params object[] args)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==args.Length).Invoke(hud,args);
        [SetUp]public override void Setup(){base.Setup();go=new GameObject("queue input adapter fixture");hud=go.AddComponent<PlayableBootstrap>();hud.enabled=false;Set("profile",PlayableProfile.Default);Call("CreateHud");input=go.AddComponent<PlayableInput>();keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();input.AssignedKeyboard=keyboard;input.AssignedMouse=mouse;input.SourceSeatSemantics=true;input.Order=(p,a,s)=>Call("Order",p,a,s);input.MapOrder=(k,p,a,s)=>Call("MapOrder",k,p,a,s);}
        [TearDown]public override void TearDown(){Get<PlayableRuntime>("runtime")?.RequestStop();UnityEngine.Object.DestroyImmediate(go);base.TearDown();}
        [UnityTest] public IEnumerator VisibleAttackAndPartiallyFullSelectionUseActualCapturedAdapterCommands()
        {
            var profile=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("one",1,new NavPoint(-24,-24),new NavPoint(-10,-4),pin:1),new OfflineStart("two",2,new NavPoint(24,24),new NavPoint(2,-4),pin:2)};
            var configuration=new OfflineMatchConfiguration(profile,"queue-input-test","queue-input-flat","authored-test-costs",7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,starts.Select(x=>new TerritorySite(x.SiteId,PlayableBuildingKind.Headquarters,x.Position,Array.Empty<TerritorySlot>())).ToArray(),Array.Empty<NavObstacle>(),new double[,]{{0,1},{1,0}},scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-14,-4))});
            using(var runtime=new PlayableRuntime(configuration,1,true))
            using(var routes=new UnityHostRouteService())
            {
                Set("runtime",runtime);Set("generation",1L);Set("world",new PlayableWorld(go.transform,profile));runtime.RequestPause(false);
                double until=Time.realtimeSinceStartupAsDouble+5;while(runtime.Latest.Paused&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Set("view",runtime.Latest);var selection=Get<HashSet<int>>("selection");var own=runtime.Latest.Entities.Where(e=>e.Owner==runtime.Latest.Owner).Select(e=>e.Id).ToArray();Assert.AreEqual(2,own.Length);var enemy=runtime.Latest.Entities.Single(e=>e.Owner!=runtime.Latest.Owner);foreach(int id in own)selection.Add(id);
                var cameraObject=new GameObject("captured attack fixture camera");var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=20;camera.transform.position=new Vector3(-4,30,-24);camera.transform.LookAt(new Vector3(-4,0,-4));Set("cameraView",camera);
                Vector2 ScreenPoint(NavPoint point,float elevation=0)=>camera.WorldToScreenPoint(new Vector3((float)point.X,elevation,(float)point.Z));
                void Click(NavPoint point,bool append,bool attack=false){if(attack)InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A));InputSystem.QueueStateEvent(keyboard,new KeyboardState(append?new[]{Key.LeftShift}:Array.Empty<Key>()));var at=ScreenPoint(point,attack ? .5f : 0f);InputSystem.QueueStateEvent(mouse,new MouseState{position=at,buttons=(ushort)(attack?1:2)});InputSystem.QueueStateEvent(mouse,new MouseState{position=at,buttons=0});InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();input.Poll();}
                try
                {
                    Click(new NavPoint(-4,-4),false);until=Time.realtimeSinceStartupAsDouble+5;
                    while(!runtime.Latest.Entities.Where(e=>own.Contains(e.Id)).All(e=>e.Queue?.Active!=null)&&Time.realtimeSinceStartupAsDouble<until){routes.Service(runtime.Requests,runtime.Answers,1,runtime.NavigationBinding.Geometry,profile,64);yield return null;}
                    Assert.True(runtime.Latest.Entities.Where(e=>own.Contains(e.Id)).All(e=>e.Queue?.Active!=null));
                    // Direct hostile hit, not the ground fallback of A-mode.
                    Click(enemy.Position,true,true);until=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Entities.Single(e=>e.Id==own[0]).Queue.Deferred.Count<1&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                    foreach(var unit in runtime.Latest.Entities.Where(e=>own.Contains(e.Id))){Assert.AreEqual(PlayableCommandKind.Attack,unit.Queue.Deferred.Single().Kind);Assert.AreEqual(enemy.Id,unit.Queue.Deferred.Single().VisibleTargetId);}
                    // Fill just one recipient; the group append still reaches the other.
                    selection.Clear();selection.Add(own[0]);for(int n=1;n<64;n++)Click(new NavPoint(-4,-4),true);
                    until=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Entities.Single(e=>e.Id==own[0]).Queue.Deferred.Count<64&&Time.realtimeSinceStartupAsDouble<until)yield return null;Assert.AreEqual(64,runtime.Latest.Entities.Single(e=>e.Id==own[0]).Queue.Deferred.Count);
                    selection.Add(own[1]);long sequence=Get<long>("sequence");Click(new NavPoint(-4,-4),true);Assert.AreEqual(sequence+1,Get<long>("sequence"));until=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Entities.Single(e=>e.Id==own[1]).Queue.Deferred.Count<2&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                    Assert.AreEqual(64,runtime.Latest.Entities.Single(e=>e.Id==own[0]).Queue.Deferred.Count);Assert.AreEqual(2,runtime.Latest.Entities.Single(e=>e.Id==own[1]).Queue.Deferred.Count);
                    selection.Clear();selection.Add(own[0]);Set("view",runtime.Latest);Click(enemy.Position,false,true);until=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Entities.Single(e=>e.Id==own[0]).Queue.Deferred.Count>0&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                    Assert.Zero(runtime.Latest.Entities.Single(e=>e.Id==own[0]).Queue.Deferred.Count);Assert.AreEqual(PlayableCommandKind.Attack,runtime.Latest.Entities.Single(e=>e.Id==own[0]).Queue.Active.Kind);Assert.AreEqual(2,runtime.Latest.Entities.Single(e=>e.Id==own[1]).Queue.Deferred.Count);Assert.IsNull(runtime.Latest.Failure);
                }
                finally{Set("cameraView",null);UnityEngine.Object.DestroyImmediate(cameraObject);runtime.RequestStop();}
                until=Time.realtimeSinceStartupAsDouble+3;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<until)yield return null;Assert.True(runtime.IsStopped);
            }
        }
        [UnityTest]public IEnumerator CapturedWorldAndMapOrdersReachRuntimeAuthorityAndHostAcrossPause()
        {
            using(var runtime=new PlayableRuntime(PlayableProfile.Default,1,7,autonomousOwnerAi:false,autonomousEnemyAi:false,startPaused:true))
            using(var routes=new UnityHostRouteService())
            {
                Set("runtime",runtime);Set("generation",1L);Set("world",new PlayableWorld(go.transform,PlayableProfile.Default));
                double until=Time.realtimeSinceStartupAsDouble+5;
                while(runtime.Latest==null&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                Assert.NotNull(runtime.Latest);runtime.RequestPause(false);
                while(runtime.Latest.Paused&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                var view=runtime.Latest;Set("view",view);int id=view.Entities.First(e=>e.Owner==view.Owner).Id;var unit=view.Entities.Single(e=>e.Id==id);Get<HashSet<int>>("selection").Add(id);
                var cameraObject=new GameObject("queue fixture camera");var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=20;camera.transform.position=new Vector3((float)unit.Position.X,30,(float)unit.Position.Z-20);camera.transform.LookAt(new Vector3((float)unit.Position.X,0,(float)unit.Position.Z));Set("cameraView",camera);
                try
                {
                    void Click(Vector2 p,bool attack,bool append){if(attack)InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.A));InputSystem.QueueStateEvent(keyboard,new KeyboardState(append?new[]{Key.LeftShift}:Array.Empty<Key>()));InputSystem.QueueStateEvent(mouse,new MouseState{position=p,buttons=(ushort)(attack?1:2)});InputSystem.QueueStateEvent(mouse,new MouseState{position=p,buttons=0});InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();input.Poll();}
                    var goal=new NavPoint(unit.Position.X+6,unit.Position.Z);var screen=camera.WorldToScreenPoint(new Vector3((float)goal.X,0,(float)goal.Z));Click(screen,false,false);
                    // Route delivery belongs to the ordinary host, not a fake completion receipt.
                    until=Time.realtimeSinceStartupAsDouble+5;
                    while((runtime.Latest.Entities.Single(e=>e.Id==id).Queue?.Active==null)&&Time.realtimeSinceStartupAsDouble<until){routes.Service(runtime.Requests,runtime.Answers,1,runtime.NavigationBinding.Geometry,PlayableProfile.Default,64);yield return null;}
                    Assert.NotNull(runtime.Latest.Entities.Single(e=>e.Id==id).Queue.Active);Assert.Greater(routes.NavMeshRequests+routes.FlowRequests,0);
                    Click(screen,true,true);Click(screen,false,true);
                    until=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Entities.Single(e=>e.Id==id).Queue.Deferred.Count<2&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                    var queue=runtime.Latest.Entities.Single(e=>e.Id==id).Queue;Assert.AreEqual(2,queue.Deferred.Count);Assert.AreEqual(PlayableCommandKind.AttackMove,queue.Deferred[0].Kind);
                    // Captured command intent survives authority pause/resume after UI dispatch.
                    runtime.RequestPause(true);until=Time.realtimeSinceStartupAsDouble+3;while(!runtime.Latest.Paused&&Time.realtimeSinceStartupAsDouble<until)yield return null;Assert.True(runtime.Latest.Paused);Assert.AreEqual(2,runtime.Latest.Entities.Single(e=>e.Id==id).Queue.Deferred.Count);runtime.RequestPause(false);
                    until=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Paused&&Time.realtimeSinceStartupAsDouble<until)yield return null;
                    view=runtime.Latest;Set("view",view);var map=Get<PlayableMapSurface>("compactMap");map.Set(view,Get<HashSet<int>>("selection"),null);yield return null;yield return null;
                    var root=Get<VisualElement>("root");var center=map.worldBound.center;var p=new Vector2(center.x,Screen.height-center.y);input.MapAt=_=>1;
                    Click(p,false,true);until=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Entities.Single(e=>e.Id==id).Queue.Deferred.Count<3&&Time.realtimeSinceStartupAsDouble<until)yield return null;Assert.AreEqual(3,runtime.Latest.Entities.Single(e=>e.Id==id).Queue.Deferred.Count);
                    Set("notice","unchanged append notice");
                    for(int queued=3;queued<64;queued++)Click(p,false,true);
                    until=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Entities.Single(e=>e.Id==id).Queue.Deferred.Count<64&&Time.realtimeSinceStartupAsDouble<until)yield return null;Assert.AreEqual(64,runtime.Latest.Entities.Single(e=>e.Id==id).Queue.Deferred.Count);
                    long previous=Get<long>("sequence");Click(p,false,true);Assert.AreEqual(previous+1,Get<long>("sequence"),"One event produces exactly one command, including silent overflow.");
                    until=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Sequence<=view.Sequence&&Time.realtimeSinceStartupAsDouble<until)yield return null;Assert.AreEqual(64,runtime.Latest.Entities.Single(e=>e.Id==id).Queue.Deferred.Count);Assert.AreEqual("unchanged append notice",Get<string>("notice"));
                    Set("view",runtime.Latest);input.MapAt=_=>0;var currentUnit=runtime.Latest.Entities.Single(e=>e.Id==id);var friendly=camera.WorldToScreenPoint(new Vector3((float)currentUnit.Position.X,.5f,(float)currentUnit.Position.Z));previous=Get<long>("sequence");Click(friendly,false,true);Assert.AreEqual(previous,Get<long>("sequence"),"Shift-follow is ignored without an extra command.");Assert.AreEqual("unchanged append notice",Get<string>("notice"));input.MapAt=_=>1;
                    Click(p,false,false);until=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Entities.Single(e=>e.Id==id).Queue.Deferred.Count!=0&&Time.realtimeSinceStartupAsDouble<until)yield return null;Assert.Zero(runtime.Latest.Entities.Single(e=>e.Id==id).Queue.Deferred.Count);Assert.NotNull(runtime.Latest.Entities.Single(e=>e.Id==id).Queue.Pending);
                    Assert.IsNull(runtime.Latest.Failure);
                }
                finally{Set("cameraView",null);UnityEngine.Object.DestroyImmediate(cameraObject);runtime.RequestStop();}
                until=Time.realtimeSinceStartupAsDouble+3;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<until)yield return null;Assert.True(runtime.IsStopped);
            }
        }
    }
}

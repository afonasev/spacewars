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
 public sealed class SentryTurretInputTests:InputTestFixture
 {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
  GameObject host;PlayableBootstrap hud;PlayableInput input;Mouse mouse;Keyboard keyboard;
  void Set(string n,object value)=>typeof(PlayableBootstrap).GetField(n,F).SetValue(hud,value);
  T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,F).GetValue(hud);
  object Call(string n,params object[] args)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==args.Length).Invoke(hud,args);
  [SetUp]public override void Setup(){base.Setup();host=new GameObject("Sentry mouse adapter");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;input=host.AddComponent<PlayableInput>();mouse=InputSystem.AddDevice<Mouse>();keyboard=InputSystem.AddDevice<Keyboard>();input.AssignedMouse=mouse;input.AssignedKeyboard=keyboard;input.SourceSeatSemantics=true;input.Order=(p,a,s)=>Call("Order",p,a,s);}
  [TearDown]public override void TearDown(){Get<PlayableRuntime>("runtime")?.RequestStop();UnityEngine.Object.DestroyImmediate(host);base.TearDown();}
  [TestCase(PlayableCommandKind.Move)]
  [TestCase(PlayableCommandKind.AttackMove)]
  public void EmptySpatialSelectionDoesNotSubmitACommand(PlayableCommandKind kind)
  {
   var profile=PlayableProfile.Default;
   using var runtime=new PlayableRuntime(profile,1,7,autonomousOwnerAi:false,startPaused:true);
   Set("runtime",runtime);Set("generation",1L);Set("profile",profile);Set("view",runtime.Latest);
   try{
    Assert.IsEmpty(Get<HashSet<int>>("selection"));
    Call("Submit",kind,null,new NavPoint(0,4),0,0L,PlayableEntityKind.Tank,
     PlayableResearchKind.TankChassis,PlayableOrderMode.Replace,0);
    Assert.Zero(Get<long>("sequence"));
    Assert.IsEmpty(runtime.ReceiptsAfter(0));
    var queue=Get<HumanMoveOrderQueue>("humanOrders");
    Assert.Zero(queue.Count);
   }finally{
    runtime.RequestStop();
    Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,10000));
   }
  }
  [UnityTest,Timeout(30000)]public IEnumerator StopAndHoldGoThroughWhileLocalMoveIsStillQueued()
  {
   var setup=new ProvingGroundSetup(PlayableProfile.Default);
   Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Explorer,new NavPoint(-10,0)));
   Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(-14,0)));
   using var runtime=new PlayableRuntime(setup.Configuration(),1);
   using var routes=new UnityHostRouteService();
   double until=Time.realtimeSinceStartupAsDouble+20;
   while(!runtime.IsPrepared&&Time.realtimeSinceStartupAsDouble<until){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);yield return null;}
   Assert.True(runtime.IsPrepared,runtime.Latest.Failure);
   Set("runtime",runtime);Set("generation",1L);Set("profile",setup.Profile);Set("view",runtime.Latest);
   Call("ReconcileHumanOrders");
   var units=runtime.Latest.Entities.Where(e=>e.Owner==runtime.Latest.Owner).ToArray();
   Assert.AreEqual(2,units.Length);
   var queue=Get<HumanMoveOrderQueue>("humanOrders");
   var move=new PlayableCommand(1,0,"player-1",PlayableCommandKind.Move,units.Select(u=>u.Id).ToArray(),new NavPoint(10,12));
   Assert.True(queue.Enqueue(setup.Profile,runtime.Latest,move));
   void Submit(PlayableCommandKind kind,int id)=>Call("Submit",kind,new[]{id},default(NavPoint),0,0L,PlayableEntityKind.Tank,PlayableResearchKind.TankChassis,PlayableOrderMode.Replace,0);
   Submit(PlayableCommandKind.Stop,units[0].Id);
   Assert.AreEqual(1,Get<long>("sequence"),"Stop reaches transport in the issuing frame.");
   Assert.AreEqual(1,queue.Count,"The other recipient retains its queued Move.");
   Submit(PlayableCommandKind.Hold,units[1].Id);
   Assert.AreEqual(2,Get<long>("sequence"),"HOLD reaches transport in the issuing frame.");
   Assert.Zero(queue.Count);
   while(runtime.ReceiptsAfter(0).Count<2&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<until){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);yield return null;}
   CollectionAssert.AreEqual(new[]{PlayableCommandStatus.Applied,PlayableCommandStatus.Applied},runtime.ReceiptsAfter(0).Select(r=>r.Status));
   while(!runtime.Latest.Entities.Single(e=>e.Id==units[1].Id).Held&&Time.realtimeSinceStartupAsDouble<until)yield return null;
   var stopped=runtime.Latest.Entities.Single(e=>e.Id==units[0].Id);
   var held=runtime.Latest.Entities.Single(e=>e.Id==units[1].Id);
   Assert.IsFalse(stopped.Moving);Assert.IsFalse(stopped.Held);Assert.AreEqual(units[0].Position,stopped.Position);
   Assert.IsTrue(held.Held);Assert.AreEqual(units[1].Position,held.Position);
  }
  [UnityTest,Timeout(30000)]public IEnumerator RightClickAssignsVisibleEnemyToSelectedReadyTurretAndGroundDoesNotMoveIt()
  {
   // Focused input/command projection with a ready scenario building, not a repeat of the construction/navigation witness.
   var p=PlayableProfile.Default;Set("profile",p);Call("CreateHud");var sites=TerritoryRules.Sites(p);var homes=sites.Where(s=>s.Kind==PlayableBuildingKind.Headquarters).Take(2).ToArray();var pad=homes[0].DefenseSlots(p)[0];
   var starts=new[]{new OfflineStart("one",homes[0].Id,homes[0].Position,new NavPoint(-11,-8),pin:1),new OfflineStart("two",homes[1].Id,homes[1].Position,new NavPoint(11,-8),pin:2)};
   var at=new NavPoint(pad.Position.X+5,pad.Position.Z+3);
   var config=new OfflineMatchConfiguration(p,"sentry-input-v1","sentry-input-flat","explicit-costs",7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,sites,PlayableMap.StaticObstacles(p),new double[,]{{0,1},{1,0}},scenario:new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(pad.Position.X,pad.Position.Z+5)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,at)},scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.SentryTurret,homes[0].Id,pad.Id,pad.Position,pad.Heading)});
   using(var runtime=new PlayableRuntime(config,1,true))using(var routes=new UnityHostRouteService()){
    Set("runtime",runtime);Set("generation",1L);var world=new PlayableWorld(host.transform,p);Set("world",world);
    var cameraObject=new GameObject("Sentry input camera");cameraObject.transform.SetParent(host.transform);var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=12;camera.transform.position=world.Point(pad.Position)+new Vector3(0,24,-20);camera.transform.LookAt(world.Point(pad.Position));Set("cameraView",camera);
    try{
     double until=Time.realtimeSinceStartupAsDouble+10;while(!runtime.IsPrepared&&Time.realtimeSinceStartupAsDouble<until){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}Assert.True(runtime.IsPrepared);runtime.RequestPause(false);
     until=Time.realtimeSinceStartupAsDouble+3;while(runtime.Latest.Paused&&Time.realtimeSinceStartupAsDouble<until){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
     var view=runtime.ParticipantView("player-1");Assert.IsNull(view.Failure);Set("view",view);var turret=view.Buildings.Single(b=>b.Kind==PlayableBuildingKind.SentryTurret);var enemy=view.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank&&Math.Abs(e.Position.X-at.X)<.1);
     Get<HashSet<int>>("selection").Add(turret.Id);Set("input",input);Call("BindKeyboardCommands");Assert.True(input.CanIssueAttack());
     Vector2 point=camera.WorldToScreenPoint(world.EntityPoint(enemy)+Vector3.up*.5f);
     void Edge(Vector2 pos,ushort buttons){InputSystem.QueueStateEvent(mouse,new MouseState{position=pos,buttons=buttons});InputSystem.Update();input.Poll();}
     Edge(point,2);Edge(point,0);Assert.AreEqual(1,Get<long>("sequence"),"One RMB produces one attack command");
     until=Time.realtimeSinceStartupAsDouble+3;while(!runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied)&&Time.realtimeSinceStartupAsDouble<until){routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
     Assert.True(runtime.ReceiptsAfter(0).Any(r=>r.Sequence==1&&r.Status==PlayableCommandStatus.Applied));Assert.AreEqual(enemy.Id,runtime.ParticipantView("player-1").Buildings.Single(b=>b.Id==turret.Id).Turret.TargetId);
     point=camera.WorldToScreenPoint(world.Point(new NavPoint(pad.Position.X-5,pad.Position.Z-5)));Edge(point,2);Edge(point,0);Assert.AreEqual(1,Get<long>("sequence"),"RMB on terrain must not submit a movement command");
    }finally{runtime.RequestStop();world.Dispose();Set("world",null);}
    double stop=Time.realtimeSinceStartupAsDouble+3;while(!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<stop)yield return null;Assert.True(runtime.IsStopped);
   }
  }
 }
}

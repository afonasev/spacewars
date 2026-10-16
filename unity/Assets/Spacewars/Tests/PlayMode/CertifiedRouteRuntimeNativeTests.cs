using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Spacewars.Input.Tests
{
    public sealed class CertifiedRouteRuntimeNativeTests : InputTestFixture
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private GameObject host;private PlayableBootstrap app;private PlayableRuntime runtime;
        private CertifiedRouteWorker delayed;private ManualResetEventSlim entered,release;
        private NativeRouteFrameTelemetry frameSink;private NavigationSession ownedSession;
        private bool oldCursor;private float oldVolume;
        private T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,F).GetValue(target);
        private void Put(object target,string name,object value)=>target.GetType().GetField(name,F).SetValue(target,value);
        private object Call(object target,string name,params object[] args)=>target.GetType().GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(target,args);
        [SetUp] public override void Setup(){oldCursor=Cursor.visible;oldVolume=AudioListener.volume;AudioListener.volume=0;base.Setup();entered=new ManualResetEventSlim();release=new ManualResetEventSlim();}
        [TearDown] public override void TearDown()
        {
            runtime?.RequestStop();delayed?.Dispose();frameSink?.Dispose();release?.Set();
            if(host)UnityEngine.Object.DestroyImmediate(host);
            if(frameSink!=null)Assert.True(SpinWait.SpinUntil(()=>frameSink.IsStopped,5000));
            if(delayed!=null)Assert.True(SpinWait.SpinUntil(()=>delayed.IsStopped,5000));
            if(runtime!=null)Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
            if(ownedSession!=null){Call(Get<object>(runtime,"domain"),"DestroyOwnedNavigation");Assert.Zero(ownedSession.PhysicalHostBudget.PhysicalBytes);Assert.Zero(ownedSession.PhysicalHostBudget.HeldBytes);Assert.Zero(ownedSession.PhysicalHostBudget.ActiveScopes);ownedSession=null;}
            entered?.Dispose();release?.Dispose();Cursor.visible=oldCursor;AudioListener.volume=oldVolume;base.TearDown();
        }
        private IEnumerator Wait(Func<bool> condition,string phase)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+15;
            while(!condition()&&Time.realtimeSinceStartupAsDouble<deadline){if(app!=null)Call(app,"UpdateFrame");yield return null;}
            Assert.True(condition(),phase+" failure="+runtime?.Latest?.Failure+" entered="+entered?.IsSet+" pending="+runtime?.Latest?.Metrics.NavigationPending+" receipts="+(runtime==null?"":string.Join(";",runtime.ReceiptsAfter(0).Select(r=>r.OwnerId+":"+r.Sequence+":"+r.Status+":"+r.Message))));
        }
        private IEnumerator Exercise(int humans)
        {
            var keyboard=InputSystem.AddDevice<Keyboard>();var mouse=InputSystem.AddDevice<Mouse>();keyboard.MakeCurrent();mouse.MakeCurrent();
            var pads=Enumerable.Range(0,humans).Select(_=>InputSystem.AddDevice<Gamepad>()).ToArray();
            host=new GameObject("Certified routes ordinary native smoke");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;
            Put(app,"profile",PlayableProfile.ThreeCrossingsDefault);Call(app,"CreateWorld");Call(app,"CreateHud");Put(app,"input",host.AddComponent<PlayableInput>());Call(app,"BindLocalKeyboardInput");Call(app,"CreateLobby");Call(app,"ShowLobby");
            Get<UnityEngine.UIElements.VisualElement>(app,"lobbyScreen").Q<UnityEngine.UIElements.DropdownField>("lobby-map-choice").value="Огненный разлом";
            var setup=Get<NativeLobbyConfiguration>(app,"lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=19092026;
            for(int i=0;i<humans;i++){setup.Participants[i].Human=true;setup.Participants[i].DeviceId=i==0?0:pads[i].deviceId;setup.Participants[i].Team=i+1;}
            Call(app,"RebuildRoster");Call(app,"LaunchLobbyMatch");
            yield return Wait(()=>Get<PlayableRuntime>(app,"runtime")!=null&&!Get<bool>(app,"preparing"),"ordinary start");runtime=Get<PlayableRuntime>(app,"runtime");ownedSession=(NavigationSession)Get<object>(runtime,"domain").GetType().GetProperty("Navigation",F).GetValue(Get<object>(runtime,"domain"));
            Call(app,"OnApplicationFocus",true);Call(app,"UpdateFrame");
            var seats=Get<List<PlayableBootstrap>>(app,"localPresentations");Assert.AreEqual(humans,seats.Count);
            var service=Get<UnityHostRouteService>(app,"routeService");var previous=Get<CertifiedRouteWorker>(service,"worker");previous?.Dispose();
            if(previous!=null)yield return Wait(()=>previous.IsStopped,"old solver shutdown");
            int heldEntity=-1;string heldOwner=null;const long heldSequence=1;var kernel=new CertifiedRouteKernel();kernel.BeforePhysicalProjectionStep=()=>{
                if(heldEntity<0||entered.IsSet)return;
                using(var admission=runtime.NavigationBinding.RoutePort.PinAdmission()){
                    var group=admission.Value.Groups.FirstOrDefault(g=>g.WorldGeneration==runtime.Generation&&g.OwnerId==heldOwner&&g.CommandSequence==heldSequence&&g.Origin==PlayableOrderOrigin.Human&&g.Members.Any(m=>m.Entity==heldEntity));
                    if(group==null)return;
                    var root=runtime.NavigationBinding.RoutePort.PinCommittedHostRoot();
                    try{
                        if(root==null)return;bool target=false;
                        // Simulation's physical key type is private to that assembly;
                        // inspect the exact pinned queue/row through the existing test seam.
                        var lookup=root.Rows.GetType().GetMethod("TryGetValue",F|BindingFlags.Public);Assert.NotNull(lookup);
                        foreach(object key in (IEnumerable)root.Projection){var arguments=new[]{key,(object)null};
                            if((bool)lookup.Invoke(root.Rows,arguments)&&arguments[1] is NavigationHostRow row&&row.ArrivalToken!=0&&row.Answer==null&&row.Request.Entity==heldEntity&&row.Request.MemberIdentity.GroupId==group.GroupId){target=true;break;}}
                        if(!target)return;
                        entered.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                    }finally{if(root!=null)typeof(NavOwnedStorage).GetMethod("Release",F).Invoke(root,null);}
                }
            };
            delayed=new CertifiedRouteWorker(runtime.NavigationBinding,4,kernel,()=>runtime.IsStopRequested,runtime.ReportRouteFailure);
            Put(service,"worker",delayed);Put(service,"workerPort",runtime.NavigationBinding.RoutePort);
            var view=Get<PlayableSnapshot>(app,"view");var unit=view.Entities.First(e=>e.Owner==view.Owner);
            heldEntity=unit.Id;heldOwner=view.OwnerId;Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,heldSequence,heldOwner,PlayableCommandKind.Move,new[]{unit.Id},new NavPoint(unit.Position.X+2,unit.Position.Z),origin:PlayableOrderOrigin.Human)).Accepted);
            yield return Wait(()=>entered.IsSet&&runtime.Latest.Metrics.NavigationPending>0,"delayed real provider");
            var applied=runtime.ReceiptsAfter(0).Last(r=>r.OwnerId==heldOwner&&r.Sequence==heldSequence);Assert.AreEqual(PlayableCommandStatus.Applied,applied.Status,applied.Message);
            Assert.AreEqual(PlayableAuthorityTick.AsyncRouteDeliveryPolicy,Get<PlayableAuthorityTick>(runtime,"authorityTick").EffectiveRouteDeliveryPolicy,"The ordinary native runtime must use the accepted per-unit async policy");
            long tick=runtime.Latest.Tick;var camera=Get<Camera>(app,"cameraView");var position=camera.transform.position;
            string framePath=System.IO.Path.GetFullPath("../.local/offload-native-frames-"+humans+".csv");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(framePath));
            frameSink=new NativeRouteFrameTelemetry(framePath);Put(app,"routeFrames",frameSink);
            Set(mouse.position,camera.pixelRect.center);Set(keyboard.rightArrowKey,1f);InputSystem.Update();
            for(int i=0;i<8;i++){Call(app,"Update");yield return null;Assert.GreaterOrEqual(runtime.Latest.Tick,tick);Assert.AreEqual(unit.Position,runtime.Latest.Entities.Single(entity=>entity.Id==unit.Id).Position,"Only the requesting actor waits for its withheld route");Assert.False(delayed.IsStopped);}
            frameSink.Dispose();yield return Wait(()=>frameSink.IsStopped,"all-frame drain");Put(app,"routeFrames",null);
            Assert.Null(frameSink.Failure);Assert.Zero(frameSink.Dropped);
            var lines=System.IO.File.ReadAllLines(framePath);Assert.AreEqual(10,lines.Length);
            var samples=lines.Skip(1).Take(8).Select(line=>line.Split(',')).ToArray();
            Assert.AreEqual(8,samples.Select(row=>row[1]).Distinct().Count());
            long previousTick=tick;foreach(var row in samples){Assert.AreEqual(runtime.Generation.ToString(),row[2]);long observed=long.Parse(row[3]);Assert.GreaterOrEqual(observed,previousTick);Assert.LessOrEqual(observed,runtime.Latest.Tick);previousTick=observed;}
            yield return Wait(()=>runtime.Latest.Tick>tick,"ordinary async ticks continue while the real provider is withheld");Assert.False(delayed.IsStopped);
            Set(keyboard.rightArrowKey,0f);InputSystem.Update();Assert.Greater((camera.transform.position-position).sqrMagnitude,0,"Real keyboard camera input continues on Unity frames during provider delay.");
            Assert.Zero(runtime.BarrierPolls,"Per-unit async delivery never enters the global route barrier");Assert.Zero(runtime.BarrierPayloadReuses);
            var frameView=Get<PlayableSnapshot>(app,"view");foreach(var seat in seats){var owner=Get<PlayableSnapshot>(seat,"view");Assert.AreEqual(frameView.Tick,owner.Tick,"All local views consume one coherent native frame");Assert.GreaterOrEqual(owner.Tick,tick);Assert.AreEqual(runtime.Generation,owner.Generation);Assert.True(Get<PlayableInput>(seat,"input").WorldInputEnabled);}
            Get<PlayableInput>(app,"input").TogglePause();yield return Wait(()=>runtime.Latest.Paused,"pause acknowledgement");long pausedTick=runtime.Latest.Tick;
            for(int i=0;i<8;i++){Call(app,"UpdateFrame");yield return null;Assert.AreEqual(pausedTick,runtime.Latest.Tick,"Acknowledged pause still freezes simulation");}
            Get<PlayableInput>(app,"input").TogglePause();yield return Wait(()=>!runtime.Latest.Paused,"resume acknowledgement");
            release.Set();yield return Wait(()=>runtime.Latest.Tick>pausedTick&&!runtime.Latest.Entities.Single(entity=>entity.Id==unit.Id).Position.Equals(unit.Position),"withheld route commits and its actor moves after solver release");Assert.Null(runtime.Latest.Failure);
            runtime.RequestStop();yield return Wait(()=>runtime.IsStopped&&delayed.IsStopped,"generation stop");
        }
        [UnityTest] public IEnumerator OrdinaryCameraAndPauseRemainAvailableDuringDelayedCertifiedSolver(){yield return Exercise(1);}
        [UnityTest] public IEnumerator TwoLocalOwnerViewsRemainCoherentDuringDelayedCertifiedSolver(){yield return Exercise(2);}
    }
}

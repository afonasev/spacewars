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
        private NativeRouteFrameTelemetry frameSink;
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
            entered?.Dispose();release?.Dispose();Cursor.visible=oldCursor;AudioListener.volume=oldVolume;base.TearDown();
        }
        private IEnumerator Wait(Func<bool> condition,string phase)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+15;
            while(!condition()&&Time.realtimeSinceStartupAsDouble<deadline){if(app!=null)Call(app,"UpdateFrame");yield return null;}
            Assert.True(condition(),phase+" failure="+runtime?.Latest?.Failure);
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
            yield return Wait(()=>Get<PlayableRuntime>(app,"runtime")!=null&&!Get<bool>(app,"preparing"),"ordinary start");runtime=Get<PlayableRuntime>(app,"runtime");
            Call(app,"OnApplicationFocus",true);Call(app,"UpdateFrame");
            var seats=Get<List<PlayableBootstrap>>(app,"localPresentations");Assert.AreEqual(humans,seats.Count);
            var service=Get<UnityHostRouteService>(app,"routeService");var previous=Get<CertifiedRouteWorker>(service,"worker");previous?.Dispose();
            if(previous!=null)yield return Wait(()=>previous.IsStopped,"old solver shutdown");
            var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>{entered.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));};
            delayed=new CertifiedRouteWorker(runtime.NavigationBinding,4,kernel,()=>runtime.IsStopRequested,runtime.ReportRouteFailure);
            Put(service,"worker",delayed);Put(service,"workerPort",runtime.NavigationBinding.RoutePort);
            var view=Get<PlayableSnapshot>(app,"view");var unit=view.Entities.First(e=>e.Owner==view.Owner);
            Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,1,view.OwnerId,PlayableCommandKind.Move,new[]{unit.Id},new NavPoint(unit.Position.X+2,unit.Position.Z))).Accepted);
            yield return Wait(()=>entered.IsSet&&runtime.Latest.Metrics.NavigationPending>0,"delayed real provider");
            long tick=runtime.Latest.Tick;var camera=Get<Camera>(app,"cameraView");var position=camera.transform.position;
            string framePath=System.IO.Path.GetFullPath("../.local/offload-native-frames-"+humans+".csv");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(framePath));
            frameSink=new NativeRouteFrameTelemetry(framePath);Put(app,"routeFrames",frameSink);
            Set(mouse.position,camera.pixelRect.center);Set(keyboard.rightArrowKey,1f);InputSystem.Update();
            for(int i=0;i<8;i++){Call(app,"Update");yield return null;Assert.AreEqual(tick,runtime.Latest.Tick);Assert.False(delayed.IsStopped);}
            frameSink.Dispose();yield return Wait(()=>frameSink.IsStopped,"all-frame drain");Put(app,"routeFrames",null);
            Assert.Null(frameSink.Failure);Assert.Zero(frameSink.Dropped);
            var lines=System.IO.File.ReadAllLines(framePath);Assert.AreEqual(10,lines.Length);
            var samples=lines.Skip(1).Take(8).Select(line=>line.Split(',')).ToArray();
            Assert.AreEqual(8,samples.Select(row=>row[1]).Distinct().Count());
            foreach(var row in samples){Assert.AreEqual(runtime.Generation.ToString(),row[2]);Assert.AreEqual(tick.ToString(),row[3]);}
            Set(keyboard.rightArrowKey,0f);InputSystem.Update();Assert.Greater((camera.transform.position-position).sqrMagnitude,0,"Real keyboard camera input continues on Unity frames during provider delay.");
            Assert.Greater(runtime.BarrierPayloadReuses,0);
            foreach(var seat in seats){var owner=Get<PlayableSnapshot>(seat,"view");Assert.AreEqual(tick,owner.Tick);Assert.AreEqual(runtime.Generation,owner.Generation);Assert.True(Get<PlayableInput>(seat,"input").WorldInputEnabled);}
            Get<PlayableInput>(app,"input").TogglePause();yield return Wait(()=>runtime.Latest.Paused,"pause acknowledgement");Assert.AreEqual(tick,runtime.Latest.Tick);
            Get<PlayableInput>(app,"input").TogglePause();yield return Wait(()=>!runtime.Latest.Paused,"resume acknowledgement");
            release.Set();yield return Wait(()=>runtime.Latest.Tick>tick,"completed route barrier");Assert.Null(runtime.Latest.Failure);
            runtime.RequestStop();yield return Wait(()=>runtime.IsStopped&&delayed.IsStopped,"generation stop");
        }
        [UnityTest] public IEnumerator OrdinaryCameraAndPauseRemainAvailableDuringDelayedCertifiedSolver(){yield return Exercise(1);}
        [UnityTest] public IEnumerator TwoLocalOwnerViewsRemainCoherentDuringDelayedCertifiedSolver(){yield return Exercise(2);}
    }
}

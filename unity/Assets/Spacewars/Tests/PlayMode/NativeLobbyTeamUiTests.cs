using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Spacewars.Runtime;
using Spacewars.Simulation.Ai;
using UnityEngine.TestTools;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class NativeLobbyTeamUiTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;
    private PlayableBootstrap app;
    private T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(app);
    private void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(app,args);
    [SetUp] public void Setup()
    {
        host=new GameObject("Lobby team test");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;
        typeof(PlayableBootstrap).GetField("profile",Flags).SetValue(app,PlayableProfile.ThreeCrossingsDefault);
        Call("CreateWorld");Call("CreateHud");Call("CreateSpectatorHud");
        typeof(PlayableBootstrap).GetField("input",Flags).SetValue(app,host.AddComponent<Spacewars.Input.PlayableInput>());
        Call("CreateLobby");Call("ShowLobby");
    }
    [TearDown] public void Teardown()
    {
        UnityEngine.Object.DestroyImmediate(host);
    }
    [Test] public void JoinAndBotActionsShowMapAwareTeamsWithoutChangingExistingSeats()
    {
        var lobby=Get<VisualElement>("lobbyScreen");
        lobby.Q<DropdownField>("lobby-map-choice").value=MossHighlandMap.DisplayName;
        var setup=Get<NativeLobbyConfiguration>("lobbySetup");
        setup.Participants.RemoveAt(0); // Leave teams 2, 3, 4; the joining human must get 1.
        Call("JoinLobbyWithKeyboardMouse");
        CollectionAssert.AreEqual(new[]{2,3,4,1},setup.Participants.Select(p=>p.Team));
        Assert.AreEqual("1",lobby.Q<DropdownField>("lobby-team-3").value);
        setup.Participants.RemoveAt(1);Call("AddLobbyParticipant",false,null);
        Assert.AreEqual(3,setup.Participants.Last().Team);
        Assert.AreEqual("3",lobby.Q<DropdownField>("lobby-team-3").value);
        lobby.Q<DropdownField>("lobby-map-choice").value="Огненный разлом (6)";
        setup.Participants.Clear();
        for(int i=0;i<6;i++)Call("AddLobbyParticipant",false,null);
        CollectionAssert.AreEqual(new[]{1,2,1,2,1,2},setup.Participants.Select(p=>p.Team));
        for(int i=0;i<6;i++)Assert.AreEqual((i%2+1).ToString(),lobby.Q<DropdownField>("lobby-team-"+i).value);
    }

    private PlayableMapMark[] PreviewMarks()=>((IReadOnlyList<PlayableMapMark>)typeof(PlayableMapSurface)
        .GetField("marks",Flags).GetValue(Get<PlayableMapSurface>("lobbyPreview"))).ToArray();
    private IEnumerator Capture(string name,bool sceneVisible=false)
    {
        string output=Environment.GetEnvironmentVariable("TEAM_START_EVIDENCE");if(string.IsNullOrEmpty(output))yield break;
        var root=Get<VisualElement>("root");var panel=host.GetComponent<UIDocument>().panelSettings;
        var texture=new RenderTexture(1280,800,24);texture.Create();var previous=panel.targetTexture;float scale=panel.scale;
        panel.targetTexture=texture;panel.scale=1;root.style.width=1280;root.style.height=800;
        var camera=Get<Camera>("cameraView");var priorTarget=camera.targetTexture;RenderTexture backdrop=null;VisualElement scene=null;
        try{
            if(sceneVisible){backdrop=new RenderTexture(1280,800,24);backdrop.Create();camera.targetTexture=backdrop;camera.Render();
                scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=scene.style.right=scene.style.top=scene.style.bottom=0;
                scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(backdrop));root.Insert(0,scene);}
            for(int frame=0;frame<8;frame++)yield return null;
            var active=RenderTexture.active;RenderTexture.active=texture;var png=new Texture2D(1280,800,TextureFormat.RGB24,false);
            try{png.ReadPixels(new Rect(0,0,1280,800),0,0);png.Apply();Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,name+".png"),png.EncodeToPNG());}
            finally{RenderTexture.active=active;UnityEngine.Object.Destroy(png);}
        }finally{scene?.RemoveFromHierarchy();camera.targetTexture=priorTarget;panel.targetTexture=previous;panel.scale=scale;texture.Release();UnityEngine.Object.Destroy(texture);if(backdrop){backdrop.Release();UnityEngine.Object.Destroy(backdrop);}}
    }
    [UnityTest] public IEnumerator EveryLobbyPreviewIsAnonymousBeforeAndAfterRosterChanges()
    {
        Call("ShowLobby");var choice=Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice");
        for(int map=0;map<choice.choices.Count;map++){
            choice.value=choice.choices[map];yield return null;
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");var before=PreviewMarks();
            Assert.True(before.All(m=>m.Owner==null&&m.Start==0));
            int capacity=before.Count(m=>m.Kind==PlayableBuildingKind.Headquarters);
            for(int i=0;i<setup.Participants.Count;i++){var p=setup.Participants[i];p.Name="Changed "+i;p.Color=(p.Color+1)%8;p.Team=i%2+1;}
            setup.Participants.Reverse();setup.Participants.RemoveAt(setup.Participants.Count-1);Call("RefreshLobbyPreview");
            var after=PreviewMarks();Assert.AreEqual(capacity,after.Count(m=>m.Kind==PlayableBuildingKind.Headquarters),"All possible starts remain visible, including empty seats.");
            Assert.True(after.All(m=>m.Owner==null&&m.Start==0));CollectionAssert.AreEqual(before.Select(m=>(m.Id,m.Position,m.Kind)),after.Select(m=>(m.Id,m.Position,m.Kind)));
            yield return Capture("neutral-lobby-"+map);
        }
    }
    [UnityTest] public IEnumerator OrdinaryRandomizedMatchKeepsCameraAuthorityAndFirstMotionThenReturnsToAnonymousLobby()
    {
        var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
        PlayableRuntime runtime=null;
        void DeadlineState(string label)
        {
            string output=Environment.GetEnvironmentVariable("TEAM_START_EVIDENCE");if(string.IsNullOrEmpty(output))return;
            // Read immutable publications only after the original deadline. No per-frame
            // logging or worker intervention can perturb the measured command interval.
            var observed=Get<PlayableRuntime>("runtime");var snapshot=observed?.Latest;var frame=observed?.OfflineFrame;
            var publication=Get<UnityHostRouteService>("routeService")?.CertifiedPublication;
            var facts=new List<string>{"stage="+label,"app.paused="+Get<bool>("paused"),"app.menuPaused="+Get<bool>("menuPaused"),
                "runtime.present="+(observed!=null),"runtime.stopped="+observed?.IsStopped,"snapshot.tick="+snapshot?.Tick,
                "snapshot.paused="+snapshot?.Paused,"snapshot.status="+snapshot?.Status,"snapshot.failure="+snapshot?.Failure,
                "frame.tick="+frame?.Tick,"frame.sequence="+frame?.Sequence,"worker.present="+(publication!=null),
                "worker.failure="+publication?.Failure,"worker.projectionFailure="+publication?.ProjectionFailure,
                "worker.pumps="+publication?.Pumps,"worker.epoch="+publication?.Epoch};
            if(frame!=null){
                foreach(var receipt in frame.Receipts){var r=receipt.Receipt;facts.Add("receipt "+r.OwnerId+" seq="+r.Sequence+" tick="+r.AppliedTick+" status="+r.Status+" message="+r.Message);}
                foreach(var view in frame.Views)foreach(var entity in view.Value.Entities.Where(e=>e.Owner==view.Value.Owner))facts.Add("entity "+view.Key+" id="+entity.Id+" x="+entity.Position.X+" z="+entity.Position.Z);
            }
            if(publication!=null)foreach(var property in typeof(NavSchedulerCounters).GetProperties())facts.Add("worker.counter."+property.Name+"="+property.GetValue(publication.Counters));
            Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,"deadline-state.txt"),facts);
        }
        IEnumerator Wait(Func<bool> ready,string label){float end=Time.realtimeSinceStartup+15;while(!ready()&&Time.realtimeSinceStartup<end){if(Get<PlayableRuntime>("runtime")!=null&&!Get<bool>("preparing"))Call("UpdateFrame");yield return null;}bool complete=ready();if(!complete)DeadlineState(label);Assert.True(complete,label);}
        try{
            Call("BindLobbyDevices");Call("ShowLobby");Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value="Полигон (8)";
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");Assert.True(setup.AiTestMap,"Original ordinary witness must use AiTestMap");setup.Participants.RemoveRange(4,4);
            for(int i=0;i<4;i++){setup.Participants[i].Team=i%2+1;setup.Participants[i].Difficulty=AiDifficulty.Recruit;}
            setup.Participants[0].Human=true;setup.Participants[0].DeviceId=0;setup.HasExplicitSeed=true;setup.ExplicitSeed=19;
            Call("RebuildRoster");Call("RefreshLobbyPreview");yield return Capture("ordinary-before-launch");Call("LaunchLobbyMatch");
            yield return Wait(()=>Get<PlayableRuntime>("runtime")!=null&&!Get<bool>("preparing"),"ordinary lobby preparation");
            runtime=Get<PlayableRuntime>("runtime");Assert.IsNull(runtime.Latest.Failure);Assert.AreEqual(4,runtime.Latest.Participants.Count);
            var config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",Flags).GetValue(runtime);
            var initial=runtime.ParticipantView("player-1");var home=initial.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
            Assert.AreEqual(config.Starts[config.Assignments[0]].Position,home.Position);
            var center=(NavPoint)typeof(PlayableBootstrap).GetMethod("Ground",Flags).Invoke(app,new object[]{Get<Camera>("cameraView").pixelRect.center});
            Assert.Less(Math.Abs(center.X-home.Position.X),.1);Assert.Less(Math.Abs(center.Z-home.Position.Z),.1);
            yield return Capture("ordinary-randomized-start",true);
            var unit=runtime.ParticipantView("player-1").Entities.Single(e=>e.Owner==PlayableOwner.Player);var destination=new NavPoint(unit.Position.X+3,unit.Position.Z);
            Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(runtime.Generation,1,"player-1",PlayableCommandKind.Move,new[]{unit.Id},destination)).Status);
            yield return Wait(()=>runtime.ParticipantView("player-1").Entities.Any(e=>e.Id==unit.Id&&!e.Position.Equals(unit.Position)),"threaded first motion from randomized start");
            var aiOrigins=config.Roster.Select((p,i)=>new{p.Id,p.Control,Start=config.Starts[config.Assignments[i]].ExplorerAnchor}).Where(p=>p.Control==OfflineControl.Ai).ToArray();
            yield return Wait(()=>aiOrigins.Any(p=>runtime.ParticipantView(p.Id).Entities.Any(e=>e.Owner==runtime.ParticipantView(p.Id).Owner&&!e.Position.Equals(p.Start))),"native AI first motion from randomized start");
            Assert.IsNull(runtime.Latest.Failure);
            Call("ReturnToLobby");yield return Wait(()=>runtime.IsStopped,"ordinary stop");Call("UpdateFrame");
            Assert.True(Get<bool>("inLobby"));Assert.IsNull(Get<PlayableRuntime>("runtime"));Assert.True(PreviewMarks().All(m=>m.Owner==null&&m.Start==0));yield return Capture("ordinary-return-neutral");
            string output=Environment.GetEnvironmentVariable("TEAM_START_EVIDENCE");if(!string.IsNullOrEmpty(output))File.WriteAllText(Path.Combine(output,"ordinary-mapping.txt"),"seed=19\n"+string.Join("\n",config.Roster.Select((p,i)=>p.Id+" team="+p.Team+" start="+config.Starts[config.Assignments[i]].Id+" site="+config.Starts[config.Assignments[i]].SiteId)));
        }finally{runtime?.RequestStop();UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);}
    }
}

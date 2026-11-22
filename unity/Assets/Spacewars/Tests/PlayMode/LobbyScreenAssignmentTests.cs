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
public sealed class LobbyScreenAssignmentTests:InputTestFixture
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap app;Gamepad pad;
    T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,F).GetValue(app);
    static T Seat<T>(PlayableBootstrap a,string n)=>(T)typeof(PlayableBootstrap).GetField(n,F).GetValue(a);
    void Put(string n,object v)=>typeof(PlayableBootstrap).GetField(n,F).SetValue(app,v);
    object Call(string n,params object[] args)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==n&&m.GetParameters().Length==args.Length).Invoke(app,args);
    public override void Setup(){base.Setup();InputSystem.AddDevice<Keyboard>();InputSystem.AddDevice<Mouse>();pad=InputSystem.AddDevice<Gamepad>();host=new GameObject("Screen assignment");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;Put("profile",PlayableProfile.ThreeCrossingsDefault);Call("CreateWorld");Call("CreateHud");Call("CreateSpectatorHud");Put("input",host.AddComponent<PlayableInput>());Call("CreateLobby");Call("ShowLobby");Get<NativeMenuNavigation>("menuNavigation").Focused=()=>true;}
    public override void TearDown(){UnityEngine.Object.DestroyImmediate(host);base.TearDown();}
    [UnityTest] public IEnumerator IconsReflectAssignmentAndPopupUsesExistingRow()
    {
        yield return null;yield return null;
        var setup=Get<NativeLobbyConfiguration>("lobbySetup");var root=Get<VisualElement>("root");var nav=Get<NativeMenuNavigation>("menuNavigation");
        Assert.AreEqual(0,root.Q<Button>("lobby-screen-0").Q<LobbyScreenIcon>().Slot);Assert.AreEqual(1,root.Q<Button>("lobby-screen-0").Q<LobbyScreenIcon>().Screens);Assert.AreEqual(-1,root.Q<Button>("lobby-screen-1").Q<LobbyScreenIcon>().Slot);
        root.Q<Button>("lobby-screen-1").Focus();nav.Activate();yield return null;
        Assert.NotNull(root.Q("lobby-screen-picker"));Assert.NotNull(root.Q("lobby-screen-option--1"));Assert.AreSame(Get<VisualElement>("lobbyScreenOverlay"),nav.Scope);
        yield return Capture("lobby-picker-off");
        root.Q<Button>("lobby-screen-option-1").Focus();nav.Activate();yield return null;
        Assert.IsNull(root.Q("lobby-screen-picker"));Assert.AreEqual(1,setup.Participants[1].ScreenSlot);Assert.AreEqual(2,root.Q<Button>("lobby-screen-0").Q<LobbyScreenIcon>().Screens);Assert.AreEqual(1,root.Q<Button>("lobby-screen-1").Q<LobbyScreenIcon>().Slot);
        root.Q<Button>("lobby-screen-0").Focus();nav.Activate();Assert.IsNull(root.Q("lobby-screen-option--1"),"human screen is mandatory");Call("CloseLobbyScreenPicker",true);
        yield return Capture("lobby-halves");
        setup.Participants[0].Human=false;Call("RebuildRoster");yield return null;
        Assert.True(setup.Spectator);CollectionAssert.AreEqual(new[]{0,1},setup.Participants.Select(p=>p.ScreenSlot));Assert.True(root.Q<Button>("lobby-screen-0").enabledSelf);Assert.True(root.Q<Button>("lobby-screen-1").enabledSelf);
        yield return Capture("lobby-spectator-exclusive");
    }
    [UnityTest] public IEnumerator MixedViewsUseAssignedCornersAndBotIsReadOnlyThroughOrdinaryLaunch()
    {
        var lobby=Get<VisualElement>("lobbyScreen");lobby.Q<DropdownField>("lobby-map-choice").value="Полигон (8)";
        var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.Participants.RemoveRange(3,setup.Participants.Count-3);setup.Participants[0].Human=true;setup.Participants[0].DeviceId=0;
        NativeScreenLayout.Normalize(setup);Assert.IsNull(NativeScreenLayout.Assign(setup,setup.Participants[1],1));Assert.IsNull(NativeScreenLayout.Assign(setup,setup.Participants[2],3));Assert.IsNull(NativeScreenLayout.Assign(setup,setup.Participants[0],2));Call("RebuildRoster");Call("LaunchLobbyMatch");
        float deadline=Time.realtimeSinceStartup+45;
        while((Get<bool>("preparing")||Get<PlayableRuntime>("runtime")==null)&&Time.realtimeSinceStartup<deadline){Call("UpdateFrame");yield return null;}
        Assert.False(Get<bool>("preparing"));Assert.NotNull(Get<PlayableRuntime>("runtime"));Call("OnApplicationFocus",true);Call("UpdateFrame");
        var seats=Get<List<PlayableBootstrap>>("localPresentations");Assert.AreEqual(3,seats.Count);
        CollectionAssert.AreEqual(new[]{2,1,3},seats.Select(s=>Seat<int>(s,"localViewportIndex")));
        foreach(var seat in seats){var binding=Seat<NativeLobbyParticipant>(seat,"localBinding");if(binding.Human)continue;Assert.False(Seat<PlayableInput>(seat,"input").enabled);Assert.False(Seat<PlayableInput>(seat,"input").WorldInputEnabled);var cam=Seat<Camera>(seat,"cameraView");var previous=cam.transform.position;typeof(PlayableBootstrap).GetMethod("Pan",F).Invoke(seat,new object[]{Vector2.one});Assert.AreEqual(previous,cam.transform.position);}
        for(int i=0;i<3;i++){Call("UpdateFrame");yield return null;}
        var backdrop=Get<VisualElement>("sharedMapBackdrop");Assert.NotNull(backdrop);Assert.AreEqual(1,backdrop.resolvedStyle.backgroundColor.a);Assert.AreEqual(0,backdrop.style.left.value.value);Assert.AreEqual(0,backdrop.style.top.value.value);
        Assert.AreEqual(25,Get<VisualElement>("sharedMapFrame").style.left.value.value);Assert.AreEqual(25,Get<VisualElement>("sharedMapFrame").style.top.value.value);
        var runtime=Get<PlayableRuntime>("runtime");deadline=Time.realtimeSinceStartup+20;
        while(runtime.BotCameraFocus("foundry-2")==null&&Time.realtimeSinceStartup<deadline){Call("UpdateFrame");yield return null;}
        Assert.NotNull(runtime.BotCameraFocus("foundry-2"),"real authority publishes applied AI activity");Assert.Greater(runtime.Latest.Tick,0);Assert.IsNull(runtime.Latest.Failure);
        yield return Capture("mixed-corners");
    }
    [UnityTest] public IEnumerator EightBotsRetainFourObserverViewsAtX4()
    {
        Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value="Полигон (8)";
        var setup=Get<NativeLobbyConfiguration>("lobbySetup");
        for(int i=0;i<8;i++){setup.Participants[i].Human=false;setup.Participants[i].ScreenSlot=i<4?i:-1;}
        Call("RebuildRoster");Call("LaunchLobbyMatch");
        float deadline=Time.realtimeSinceStartup+60;
        while((Get<bool>("preparing")||Get<PlayableRuntime>("runtime")==null)&&Time.realtimeSinceStartup<deadline){Call("UpdateFrame");yield return null;}
        Assert.False(Get<bool>("preparing"));var runtime=Get<PlayableRuntime>("runtime");Assert.NotNull(runtime);
        runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);Put("spectatorRate",4d);Call("OnApplicationFocus",true);
        for(int i=0;i<3;i++){Call("UpdateFrame");yield return null;}
        var seats=Get<List<PlayableBootstrap>>("localPresentations");Assert.AreEqual(4,seats.Count);Assert.AreEqual(120,runtime.TargetTicksPerSecond);
        CollectionAssert.AreEquivalent(Enumerable.Range(0,4),seats.Select(s=>Seat<int>(s,"localViewportIndex")));
        foreach(var seat in seats){
            Assert.True(Seat<bool>(seat,"spectatorMode"));Assert.AreEqual(4,Seat<double>(seat,"spectatorRate"));
            Assert.False(Seat<PlayableInput>(seat,"input").CommandInputEnabled);
            Assert.AreEqual(.5f,Seat<Camera>(seat,"cameraView").rect.width);Assert.AreEqual(.5f,Seat<Camera>(seat,"cameraView").rect.height);
            Assert.AreEqual(Seat<int>(seat,"localRosterIndex"),(int)Seat<PlayableSnapshot>(seat,"view").Owner);
        }
        Assert.AreEqual(1,seats.Select(s=>Seat<PlayableSnapshot>(s,"view").Tick).Distinct().Count(),"all four views consume one captured spectator frame");
        Assert.AreEqual(StyleKeyword.Auto,Get<VisualElement>("top").style.left.keyword,"primary observer resources must use the opposite corner from speed controls");
        Assert.IsNull(runtime.Latest.Failure);
        bool oldFps=NativeDevicePreferences.ShowFps,oldTps=NativeDevicePreferences.ShowTps;
        try{
            NativeDevicePreferences.ShowFps=NativeDevicePreferences.ShowTps=true;
            var panel=Get<VisualElement>("panelRoot");
            var fps=panel.Q<Label>("device-fps");var tps=panel.Q<Label>("device-tps");
            float countersReady=Time.realtimeSinceStartup+2;
            while((string.IsNullOrEmpty(fps.text)||string.IsNullOrEmpty(tps.text))&&Time.realtimeSinceStartup<countersReady){Call("UpdateFrame");yield return null;}
            Call("UpdateFrame");yield return null;
            Assert.AreEqual(DisplayStyle.Flex,fps.resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.Flex,tps.resolvedStyle.display);
            Assert.That((fps.worldBound.xMin+tps.worldBound.xMax)/2,Is.EqualTo(panel.worldBound.center.x).Within(.5f));
            Assert.Greater(panel.IndexOf(fps),panel.IndexOf(Get<VisualElement>("sharedMapFrame")));
            foreach(var seat in seats){
                var header=Seat<VisualElement>(seat,"top");
                Assert.False(fps.worldBound.Overlaps(header.worldBound));
                Assert.False(tps.worldBound.Overlaps(header.worldBound));
            }
            yield return Capture("observer-four-x4-counters");
        }finally{NativeDevicePreferences.ShowFps=oldFps;NativeDevicePreferences.ShowTps=oldTps;}
    }
    [UnityTest] public IEnumerator UnassignedSpectatorRemainsFullscreenAndManualInputReleasesFollow()
    {
        var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.Participants[0].Human=false;foreach(var p in setup.Participants)p.ScreenSlot=-1;Call("RebuildRoster");Put("matchSetup",setup.Copy());Call("StartSession");Put("inLobby",false);Call("ReadPresentationFrame");Call("SetSpectatorPerspective",PlayableOwner.Enemy);Call("UpdateBotCamera");
        Assert.AreEqual(new Rect(0,0,1,1),Get<Camera>("cameraView").rect);Assert.AreEqual(1,Get<List<PlayableBootstrap>>("localPresentations").Count);Assert.True(Get<NativeLobbyConfiguration>("matchSetup").Participants.All(p=>p.ScreenSlot==-1));
        var cam=Get<Camera>("cameraView");var position=cam.transform.position;float zoom=cam.orthographicSize;Call("Pan",Vector2.one);Assert.AreNotEqual(position,cam.transform.position);Assert.True(Get<bool>("spectatorManualCamera"));Call("UpdateBotCamera");Assert.IsNull(Get<string>("botCameraOwner"));
        Call("SetSpectatorPerspective",PlayableOwner.Enemy);Call("UpdateBotCamera");Call("Pan",Vector2.one);Assert.IsNull(Get<string>("botCameraOwner"));yield return null;
    }
    IEnumerator Capture(string label)
    {
        string output=Environment.GetEnvironmentVariable("LOBBY_SCREEN_EVIDENCE");if(string.IsNullOrEmpty(output))yield break;
        var panel=host.GetComponent<UIDocument>().panelSettings;var target=new RenderTexture(1280,800,24);target.Create();panel.targetTexture=target;
        try{for(int i=0;i<4;i++)yield return null;
            if(label=="lobby-picker-off"){var popup=Get<VisualElement>("root").Q("lobby-screen-picker");var button=Get<VisualElement>("root").Q("lobby-screen-1");Assert.That(popup.worldBound.xMax,Is.EqualTo(button.worldBound.xMax).Within(1));Assert.GreaterOrEqual(popup.worldBound.yMin,button.worldBound.yMax-1);}
            var old=RenderTexture.active;RenderTexture.active=target;var png=new Texture2D(1280,800,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,1280,800),0,0);png.Apply();Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,label+".png"),png.EncodeToPNG());UnityEngine.Object.Destroy(png);RenderTexture.active=old;}
        finally{panel.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);}
    }
}

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
        Assert.True(setup.Spectator);Assert.True(setup.Participants.All(p=>p.ScreenSlot==-1));Assert.False(root.Q<Button>("lobby-screen-0").enabledSelf);Assert.False(root.Q<Button>("lobby-screen-1").enabledSelf);
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
    [UnityTest] public IEnumerator SpectatorRemainsFullscreenAndSelectedBotLocksManualCamera()
    {
        var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.Participants[0].Human=false;setup.Participants[1].ScreenSlot=1;Call("RebuildRoster");Put("matchSetup",setup.Copy());Call("StartSession");Put("inLobby",false);Call("ReadPresentationFrame");Call("SetSpectatorPerspective",PlayableOwner.Enemy);Call("UpdateBotCamera");
        Assert.AreEqual(new Rect(0,0,1,1),Get<Camera>("cameraView").rect);Assert.AreEqual(1,Get<List<PlayableBootstrap>>("localPresentations").Count);Assert.True(Get<NativeLobbyConfiguration>("matchSetup").Participants.All(p=>p.ScreenSlot==-1));
        var cam=Get<Camera>("cameraView");var position=cam.transform.position;float zoom=cam.orthographicSize;Call("Pan",Vector2.one);Call("Zoom",1f);Assert.AreEqual(position,cam.transform.position);Assert.AreEqual(zoom,cam.orthographicSize);
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

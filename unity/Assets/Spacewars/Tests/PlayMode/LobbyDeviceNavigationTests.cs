using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class LobbyDeviceNavigationTests : InputTestFixture
{
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;
    private PlayableBootstrap app;
    private Gamepad primary,secondary;
    private VisualElement root,lobby;
    private NativeMenuNavigation nav;
    private NativeLobbyConfiguration SetupRoster=>Get<NativeLobbyConfiguration>("lobbySetup");
    private T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,F).GetValue(app);
    private void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,F).Invoke(app,null);
    private void Put(string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(app,value);
    public override void Setup()
    {
        base.Setup();InputSystem.AddDevice<Keyboard>();InputSystem.AddDevice<Mouse>();primary=InputSystem.AddDevice<Gamepad>();secondary=InputSystem.AddDevice<Gamepad>();primary.MakeCurrent();
        host=new GameObject("Lobby device ownership regression");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;
        Put("profile",PlayableProfile.ThreeCrossingsDefault);Call("CreateWorld");Call("CreateHud");Put("input",host.AddComponent<PlayableInput>());Call("CreateLobby");
        root=Get<VisualElement>("root");root.style.width=1280;root.style.height=800;lobby=Get<VisualElement>("lobbyScreen");nav=Get<NativeMenuNavigation>("menuNavigation");nav.Focused=()=>true;
    }
    public override void TearDown(){UnityEngine.Object.DestroyImmediate(host);base.TearDown();}
    private void State(Gamepad pad,GamepadState state){InputSystem.QueueStateEvent(pad,state);InputSystem.Update();nav.Tick();Call("PollLobbyControllers");}
    private void Tap(Gamepad pad,GamepadButton button){State(pad,new GamepadState());State(pad,new GamepadState().WithButton(button));State(pad,new GamepadState());}
    private void Key(KeyCode code)
    {
        var target=root.focusController.focusedElement as VisualElement??root;
        using(var key=KeyDownEvent.GetPooled('\0',code,EventModifiers.None)){key.target=target;target.SendEvent(key);}
        using(var key=KeyUpEvent.GetPooled('\0',code,EventModifiers.None)){key.target=root;root.SendEvent(key);}
    }
    [UnityTest] public IEnumerator EntryBindsPrimaryAndSecondaryCannotOperateMenus()
    {
        nav.UseGamepad();Call("ShowLobby");yield return null;yield return null;
        Assert.AreEqual(primary.deviceId,SetupRoster.Participants.First(p=>p.Human).DeviceId);Assert.AreSame(primary,nav.CurrentGamepad);
        int count=SetupRoster.Participants.Count;var focus=root.focusController.focusedElement;
        State(primary,new GamepadState());Tap(secondary,GamepadButton.DpadLeft);Tap(secondary,GamepadButton.South);Tap(secondary,GamepadButton.East);Tap(secondary,GamepadButton.West);
        Assert.AreEqual(count,SetupRoster.Participants.Count);Assert.AreSame(focus,root.focusController.focusedElement);Assert.AreEqual(DisplayStyle.Flex,lobby.style.display.value);Assert.False(Get<bool>("preparing"));
        lobby.Q<Button>("lobby-remove-1").Focus();nav.Activate();Assert.AreEqual(count-1,SetupRoster.Participants.Count);
        Tap(secondary,GamepadButton.North);Assert.AreEqual(count,SetupRoster.Participants.Count);Assert.AreEqual(secondary.deviceId,SetupRoster.Participants.Last().DeviceId);
        // Keyboard/mouse remain a menu fallback even though the first player owns a pad.
        var field=lobby.Q<DropdownField>("lobby-team-0");field.Focus();Key(KeyCode.Return);Assert.NotNull(root.Q("menu-dropdown-overlay"));Key(KeyCode.Escape);Assert.IsNull(root.Q("menu-dropdown-overlay"));
        Assert.AreSame(primary,nav.CurrentGamepad,"Secondary MakeCurrent must not steal menu ownership");
        yield return Capture("pad-lobby");
    }
    [UnityTest] public IEnumerator MouseEntryIsExclusiveAndKeyboardCanJoinPadLobby()
    {
        nav.UseKeyboardMouse();Call("ShowLobby");yield return null;
        Assert.AreEqual(0,SetupRoster.Participants.First(p=>p.Human).DeviceId);Assert.IsNull(nav.CurrentGamepad);
        nav.UpdateHints();Assert.True(lobby.Query<NativeControllerGlyph>().ToList().All(g=>g.style.display.value==DisplayStyle.None));
        // A return from the menu preserves explicitly selected devices.
        SetupRoster.Participants.First(p=>p.Human).DeviceId=primary.deviceId;Call("RebuildRoster");Call("ShowMainMenu");Call("ShowLobby");Assert.AreEqual(primary.deviceId,SetupRoster.Participants.First(p=>p.Human).DeviceId);
        lobby.Q<Button>("lobby-remove-1").Focus();nav.Activate();lobby.Q<Button>("lobby-join").Focus();nav.Activate();
        Assert.AreEqual(1,SetupRoster.Participants.Count(p=>p.Human&&p.DeviceId==0));
        yield return Capture("keyboard-joined");
    }
    [UnityTest] public IEnumerator DropdownsPreviewConfirmCancelAndNavigateSpatially()
    {
        nav.UseGamepad();Call("ShowLobby");yield return null;yield return null;State(primary,new GamepadState());
        var team=lobby.Q<DropdownField>("lobby-team-0");int original=team.index;team.Focus();Tap(primary,GamepadButton.South);
        Assert.NotNull(root.Q("menu-dropdown-overlay"));Tap(primary,GamepadButton.DpadDown);Assert.AreEqual(original,team.index);
        yield return Capture("team-dropdown");Tap(primary,GamepadButton.East);Assert.AreEqual(original,team.index);Assert.IsNull(root.Q("menu-dropdown-overlay"));
        Tap(primary,GamepadButton.South);Tap(primary,GamepadButton.DpadDown);Tap(primary,GamepadButton.South);Assert.AreEqual(original+1,team.index);
        team.Focus();Key(KeyCode.Return);Key(KeyCode.DownArrow);Assert.AreEqual(original+1,team.index);Key(KeyCode.Escape);Assert.AreEqual(original+1,team.index);
        team.Focus();nav.Move(Vector2.left);Assert.IsInstanceOf<LobbyColorField>(root.focusController.focusedElement);var color=lobby.Q<LobbyColorField>("lobby-color-0");int initial=color.value;
        Tap(primary,GamepadButton.South);Assert.NotNull(root.Q("lobby-color-palette"));Tap(primary,GamepadButton.DpadDown);Assert.AreEqual(initial,color.value);yield return Capture("colour-dropdown");Tap(primary,GamepadButton.East);Assert.AreEqual(initial,color.value);
        Tap(primary,GamepadButton.South);Tap(primary,GamepadButton.DpadDown);Tap(primary,GamepadButton.South);Assert.AreNotEqual(initial,color.value);
        team.Focus();Key(KeyCode.RightArrow);Assert.AreEqual("lobby-device-0",(root.focusController.focusedElement as VisualElement)?.name);
        Key(KeyCode.DownArrow);Assert.AreEqual("lobby-ai-difficulty",(root.focusController.focusedElement as VisualElement)?.name);
    }
    [UnityTest] public IEnumerator KeyboardNavigationAndGenericPadHintsHaveNoContextualA()
    {
        nav.UseKeyboardMouse();Call("ShowLobby");yield return null;yield return null;
        var field=lobby.Q<TextField>("lobby-human-name");field.Focus();Key(KeyCode.RightArrow);Assert.IsInstanceOf<LobbyColorField>(root.focusController.focusedElement);
        yield return Capture("keyboard-lobby");
        lobby.Q<DropdownField>("lobby-device-0").index=1;nav.UpdateHints();
        Assert.AreEqual("Y",NativeControllerGlyph.TopSymbol(primary));Assert.AreEqual("A",NativeControllerGlyph.Symbol("A",primary));
        lobby.Q<Button>("lobby-add-bot").Focus();nav.UseGamepad();Assert.IsNull(lobby.Q<Button>("lobby-add-bot").Q("menu-pad-glyph"));
        Assert.AreEqual("X",lobby.Q<Button>("lobby-add-bot").Q<Label>("controller-glyph-symbol").text);
        Assert.AreEqual(DisplayStyle.Flex,lobby.Q<Button>("lobby-add-bot").Q<NativeControllerGlyph>().style.display.value);
        yield return null;yield return null;
        foreach(var button in lobby.Query<Button>().ToList())
        {
            var glyph=button.Q<NativeControllerGlyph>();if(glyph==null||glyph.style.display.value==DisplayStyle.None)continue;
            Assert.Greater(glyph.worldBound.center.x,button.worldBound.center.x,button.name+" badge on right");
            Assert.LessOrEqual(Mathf.Abs(glyph.worldBound.center.y-button.worldBound.center.y),1,button.name+" vertically centred");
            var caption=button.Children().OfType<Label>().Single();Assert.LessOrEqual(caption.worldBound.xMax,glyph.worldBound.xMin,button.name+" caption clear of badge");
            Assert.AreEqual(6,glyph.worldBound.xMin-caption.worldBound.xMax,1,button.name+" badge beside caption");
            Assert.AreEqual(button.worldBound.center.x,(caption.worldBound.xMin+glyph.worldBound.xMax)/2,1,button.name+" caption and badge centred together");
        }
        yield return Capture("pad-caption-badges");
    }
    [UnityTest] public IEnumerator InstalledMainMenuCapturesPadEntryBeforeChangingScope()
    {
        Put("nativeMainMenuEnabled",true);Call("ShowMainMenu");yield return null;yield return null;
        var menu=Get<NativeMainMenu>("nativeMainMenu");Assert.NotNull(menu.ScreenRoot);primary.MakeCurrent();nav.UseGamepad();menu.ScreenRoot.Q<Button>("main-play").Focus();nav.Activate();yield return null;
        Assert.True(Get<bool>("lobbyEntryBound"));Assert.AreEqual(primary.deviceId,SetupRoster.Participants.First(p=>p.Human).DeviceId);Assert.AreSame(primary,nav.CurrentGamepad);
    }
    [UnityTest] public IEnumerator MouseCanOpenAndCancelListsForPadPrimaryAndKeyboardCombatIgnoresPads()
    {
        nav.UseGamepad();Call("ShowLobby");yield return null;yield return null;
        var field=lobby.Q<DropdownField>("lobby-team-0");int original=field.index;
        var pointer=new Event{type=EventType.MouseDown,button=0,mousePosition=field.worldBound.center};
        using(var e=PointerDownEvent.GetPooled(pointer)){e.target=field;field.SendEvent(e);}
        Assert.NotNull(root.Q("menu-dropdown-overlay"));Assert.False(nav.UsingGamepad);Key(KeyCode.DownArrow);Key(KeyCode.Escape);Assert.AreEqual(original,field.index);
        // These are the real seat device resolvers, independent of human count.
        var binding=SetupRoster.Participants.First(p=>p.Human);binding.DeviceId=0;Put("localBinding",binding);Put("localHumanCount",1);
        Assert.True((bool)typeof(PlayableBootstrap).GetProperty("SeatUsesKeyboard",F).GetValue(app));Assert.IsNull(typeof(PlayableBootstrap).GetProperty("SeatPad",F).GetValue(app));
        binding.DeviceId=primary.deviceId;Assert.False((bool)typeof(PlayableBootstrap).GetProperty("SeatUsesKeyboard",F).GetValue(app));Assert.AreSame(primary,typeof(PlayableBootstrap).GetProperty("SeatPad",F).GetValue(app));
    }
    private IEnumerator Capture(string name)
    {
        string output=Environment.GetEnvironmentVariable("LOBBY_DEVICE_EVIDENCE");if(string.IsNullOrEmpty(output))yield break;
        var panel=host.GetComponent<UIDocument>().panelSettings;var image=new RenderTexture(1280,800,24);image.Create();panel.targetTexture=image;
        try{for(int i=0;i<5;i++)yield return null;var previous=RenderTexture.active;RenderTexture.active=image;var png=new Texture2D(1280,800,TextureFormat.RGB24,false);try{png.ReadPixels(new Rect(0,0,1280,800),0,0);png.Apply();Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,name+".png"),png.EncodeToPNG());}finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(png);}}
        finally{panel.targetTexture=null;image.Release();UnityEngine.Object.DestroyImmediate(image);}
    }
}

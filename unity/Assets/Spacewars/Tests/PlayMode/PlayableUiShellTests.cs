using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class PlayableUiShellTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;
    private PlayableBootstrap hud;
    private T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
    private void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
    private void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(hud,null);
    [SetUp] public void Setup()
    {
        host=new GameObject("native UI shell test");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        Set("profile",PlayableProfile.Default);Call("CreateHud");
    }
    [TearDown] public void Cleanup()
    {
        Get<PlayableRuntime>("runtime")?.RequestStop();
        UnityEngine.Object.DestroyImmediate(host);
    }
    private static void SendKey(VisualElement target,KeyCode key,EventModifiers modifiers=EventModifiers.None)
    {
        using(var evt=KeyDownEvent.GetPooled('\0',key,modifiers))target.SendEvent(evt);
    }
    private static void SubmitButton(Button button)
    {
        button.Focus();
        using(var evt=NavigationSubmitEvent.GetPooled(EventModifiers.None))button.SendEvent(evt);
    }
    private void ShowTerminal(PlayableMatchOutcome outcome)
    {
        Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,outcome,900,null,
            Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
        Call("UpdateHud");
    }

    [Test] public void ExistingActionsAreGroupedAndFocusable()
    {
        var root=Get<VisualElement>("root");
        foreach(var heading in new[]{"ТАКТИЧЕСКАЯ КАРТА","СТРОИТЕЛЬСТВО","ВЫБОР И ОЧЕРЕДИ","КОМАНДЫ"})
            Assert.True(root.Query<Label>().ToList().Any(label=>label.text==heading),heading);
        Assert.True(root.Query<Button>().ToList().Any(button=>button.text=="Продолжить"&&button.focusable));
        Assert.True(root.Query<Button>().ToList().Any(button=>button.text=="Начать заново"&&button.focusable));
        Assert.True(root.Query<Button>().ToList().Any(button=>button.text=="Выйти"&&button.focusable));
        Assert.AreEqual(6,root.Query<Button>().ToList().Count(button=>button.name.StartsWith("production-slot-")));
        var pause=root.Query<Button>().ToList().First(button=>button.text=="Пауза");
        pause.Focus();
        Assert.AreEqual(new Color(.48f,.88f,.81f),pause.style.borderLeftColor.value);
        root.Focus();
        Assert.AreEqual(new Color(.18f,.39f,.43f),pause.style.borderLeftColor.value);
    }

    [TestCase(PlayableMatchOutcome.Playing,"ПАУЗА",true)]
    [TestCase(PlayableMatchOutcome.PlayerWon,"ПОБЕДА",false)]
    [TestCase(PlayableMatchOutcome.PlayerLost,"ЦЕНТРЫ ПОТЕРЯНЫ",false)]
    public void ModalReflectsExistingSnapshotState(PlayableMatchOutcome outcome,string title,bool canContinue)
    {
        Set("paused",outcome==PlayableMatchOutcome.Playing);
        Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,outcome,900,null,
            Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
        Call("UpdateHud");
        Assert.AreEqual(title,Get<Label>("modalTitle").text);
        Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("modal").style.display.value);
        Assert.AreEqual(canContinue?DisplayStyle.Flex:DisplayStyle.None,Get<Button>("resumeButton").style.display.value);
        Assert.True(Get<Label>("modalCaption").text.Contains("Seed 7 · "+PlayableProfile.RequiredProfileId+"@1"));
        Assert.AreSame(canContinue?Get<Button>("resumeButton"):Get<Button>("modalRestartButton"),Get<VisualElement>("root").focusController.focusedElement);
    }

    [TestCase(PlayableMatchOutcome.PlayerWon)]
    [TestCase(PlayableMatchOutcome.PlayerLost)]
    public void TerminalFocusCyclesOnlyBetweenRestartAndExit(PlayableMatchOutcome outcome)
    {
        var root=Get<VisualElement>("root");
        Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,outcome,900,null,
            Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
        Call("UpdateHud");
        var restart=Get<Button>("modalRestartButton");var exit=Get<Button>("modalExitButton");
        var move=typeof(PlayableBootstrap).GetMethod("MoveHudFocus",Flags);
        Assert.AreSame(restart,root.focusController.focusedElement);
        move.Invoke(hud,new object[]{false});Assert.AreSame(exit,root.focusController.focusedElement);
        move.Invoke(hud,new object[]{false});Assert.AreSame(restart,root.focusController.focusedElement);
        move.Invoke(hud,new object[]{true});Assert.AreSame(exit,root.focusController.focusedElement);
        Call("ToggleHudFocus");Assert.AreSame(restart,root.focusController.focusedElement);
        Assert.AreEqual(new Color(.48f,.88f,.81f),restart.style.borderLeftColor.value);
    }

    [TestCase(PlayableMatchOutcome.PlayerWon)]
    [TestCase(PlayableMatchOutcome.PlayerLost)]
    public void TerminalKeyEventsStayInModalAndSubmitRestart(PlayableMatchOutcome outcome)
    {
        var root=Get<VisualElement>("root");
        var runtime=new PlayableRuntime(PlayableProfile.Default,1,7);
        Set("runtime",runtime);
        ShowTerminal(outcome);
        var restart=Get<Button>("modalRestartButton");
        var exit=Get<Button>("modalExitButton");
        SendKey(restart,KeyCode.Tab);
        Assert.AreSame(exit,root.focusController.focusedElement);
        SendKey(exit,KeyCode.Tab,EventModifiers.Shift);
        Assert.AreSame(restart,root.focusController.focusedElement);
        SendKey(restart,KeyCode.Escape);
        Assert.AreSame(restart,root.focusController.focusedElement);
        Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("modal").style.display.value);
        SubmitButton(restart);
        Assert.True(Get<bool>("restarting"),"The UI Toolkit button must request the normal restart lifecycle.");
        Assert.False(Get<bool>("quitting"));
    }

    [Test]
    public void TerminalExitButtonRequestsNormalPlayerExit()
    {
        Set("runtime",new PlayableRuntime(PlayableProfile.Default,1,7));
        ShowTerminal(PlayableMatchOutcome.PlayerLost);
        SubmitButton(Get<Button>("modalExitButton"));
        Assert.True(Get<bool>("quitting"),"The UI Toolkit button must request the normal Player exit lifecycle.");
        Assert.False(Get<bool>("restarting"));
    }

    [Test]
    public void EscapeFromPauseResumesAndReturnsFieldFocus()
    {
        var root=Get<VisualElement>("root");
        Set("runtime",new PlayableRuntime(PlayableProfile.Default,1,7));
        Set("paused",true);
        ShowTerminal(PlayableMatchOutcome.Playing);
        SendKey(Get<Button>("resumeButton"),KeyCode.Escape);
        Assert.False(Get<bool>("paused"));
        Call("UpdateHud");
        Assert.AreEqual(DisplayStyle.None,Get<VisualElement>("modal").style.display.value);
        Assert.AreSame(root,root.focusController.focusedElement);
    }

    [Test]
    public void ResumeClosesModalAndReturnsFocusToField()
    {
        var root=Get<VisualElement>("root");
        var snapshot=new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,
            Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
        Set("view",snapshot);Set("paused",true);Call("UpdateHud");
        Assert.AreSame(Get<Button>("resumeButton"),root.focusController.focusedElement);
        var move=typeof(PlayableBootstrap).GetMethod("MoveHudFocus",Flags);
        move.Invoke(hud,new object[]{true});Assert.AreSame(Get<Button>("modalExitButton"),root.focusController.focusedElement);
        Set("paused",false);Call("UpdateHud");
        Assert.AreEqual(DisplayStyle.None,Get<VisualElement>("modal").style.display.value);
        Assert.AreSame(root,root.focusController.focusedElement);
    }

    [TestCase(1440f,false)]
    [TestCase(1024f,true)]
    public void ResponsiveLayoutKeepsTheSameActionsAndFocus(float width,bool compact)
    {
        typeof(PlayableBootstrap).GetMethods(Flags).Single(method=>method.Name=="ApplyResponsiveHud"&&method.GetParameters().Length==1)
            .Invoke(hud,new object[]{width});
        var row=Get<VisualElement>("hudRow");
        var army=Get<VisualElement>("armyRegion");
        var commands=Get<VisualElement>("commandRegion");
        Assert.AreEqual(compact?Wrap.Wrap:Wrap.NoWrap,row.style.flexWrap.value);
        Assert.AreEqual(compact?3:2,row.IndexOf(army));
        Assert.AreEqual(compact?2:3,row.IndexOf(commands));
        Assert.AreEqual(6,Get<VisualElement>("root").Query<Button>().ToList().Count(button=>button.name.StartsWith("production-slot-")));
        var pause=Get<VisualElement>("root").Query<Button>().ToList().First(button=>button.text=="Пауза");
        pause.Focus();
        Assert.AreEqual(new Color(.48f,.88f,.81f),pause.style.borderLeftColor.value);
    }

    [Test]
    public void HudFocusCanBeEnteredAndLeftWithoutChangingGameActions()
    {
        var root=Get<VisualElement>("root");
        root.Focus();
        Call("ToggleHudFocus");
        Assert.AreSame(Get<Button>("hudFocusButton"),root.focusController.focusedElement);
        Assert.AreEqual(new Color(.48f,.88f,.81f),Get<Button>("hudFocusButton").style.borderLeftColor.value);
        typeof(PlayableBootstrap).GetMethod("MoveHudFocus",Flags).Invoke(hud,new object[]{false});
        Assert.AreEqual("Заново",((Button)root.focusController.focusedElement).text);
        typeof(PlayableBootstrap).GetMethod("MoveHudFocus",Flags).Invoke(hud,new object[]{true});
        Assert.AreSame(Get<Button>("hudFocusButton"),root.focusController.focusedElement);
        Call("ToggleHudFocus");
        Assert.AreSame(root,root.focusController.focusedElement);
    }

    [UnityTest]
    public IEnumerator NarrowViewportKeepsVisibleActionBoundsInsidePanel()
    {
        var root=Get<VisualElement>("root");
        Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,
            Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
        Call("UpdateHud");
        root.style.width=1024;root.style.height=768;
        yield return null;
        yield return null;
        var panel=root.worldBound;
        Assert.Greater(panel.width,0);
        Assert.AreEqual(DisplayStyle.None,Get<VisualElement>("buildRegion").style.display.value);
        Assert.Greater(Get<VisualElement>("bottom").worldBound.yMin,Get<VisualElement>("top").worldBound.yMax);
        foreach(var button in root.Query<Button>().ToList())
        {
            var bounds=button.worldBound;
            if(bounds.width<=0||bounds.height<=0)continue;
            Assert.GreaterOrEqual(bounds.xMin,panel.xMin-.5f,button.text);
            Assert.LessOrEqual(bounds.xMax,panel.xMax+.5f,button.text);
            Assert.GreaterOrEqual(bounds.yMin,panel.yMin-.5f,button.text);
            Assert.LessOrEqual(bounds.yMax,panel.yMax+.5f,button.text);
        }
        var map=Get<VisualElement>("hudRow").Q<PlayableMapSurface>();
        Assert.Greater(map.worldBound.width,0);
        Assert.LessOrEqual(map.worldBound.xMax,panel.xMax);
    }

    [UnityTest]
    public IEnumerator TerminalResultsFitAndKeepFocusAtCheckedViewports()
    {
        var root=Get<VisualElement>("root");
        var modal=Get<VisualElement>("modal");
        var card=Get<VisualElement>("modalCard");
        var title=Get<Label>("modalTitle");
        var caption=Get<Label>("modalCaption");
        var restart=Get<Button>("modalRestartButton");
        var exit=Get<Button>("modalExitButton");
        var resume=Get<Button>("resumeButton");
        foreach(var size in new[]{new Vector2Int(1024,768),new Vector2Int(1440,900),new Vector2Int(1920,1080)})
        foreach(var outcome in new[]{PlayableMatchOutcome.PlayerWon,PlayableMatchOutcome.PlayerLost})
        {
            root.style.width=size.x;root.style.height=size.y;
            root.Focus();Set("modalWasVisible",false);
            ShowTerminal(outcome);
            yield return null;
            yield return null;
            string context=outcome+" at "+size.x+"x"+size.y;
            Assert.AreEqual(DisplayStyle.Flex,modal.style.display.value,context);
            Assert.AreEqual(DisplayStyle.None,resume.style.display.value,context);
            Assert.AreEqual(outcome==PlayableMatchOutcome.PlayerWon?"ПОБЕДА":"ЦЕНТРЫ ПОТЕРЯНЫ",title.text,context);
            StringAssert.Contains("Seed 7 · "+PlayableProfile.RequiredProfileId+"@1",caption.text,context);
            Assert.AreSame(restart,root.focusController.focusedElement,context);
            AssertInside(root.worldBound,card.worldBound,"card "+context);
            AssertInside(card.worldBound,title.worldBound,"title "+context);
            AssertInside(card.worldBound,caption.worldBound,"caption "+context);
            AssertInside(card.worldBound,restart.worldBound,"restart "+context);
            AssertInside(card.worldBound,exit.worldBound,"exit "+context);
            Assert.LessOrEqual(title.worldBound.yMax,caption.worldBound.yMin+.5f,context);
            Assert.LessOrEqual(caption.worldBound.yMax,restart.worldBound.yMin+.5f,context);
            Assert.LessOrEqual(restart.worldBound.yMax,exit.worldBound.yMin+.5f,context);
            SendKey(restart,KeyCode.Tab);
            Assert.AreSame(exit,root.focusController.focusedElement,context);
            SendKey(exit,KeyCode.Tab,EventModifiers.Shift);
            Assert.AreSame(restart,root.focusController.focusedElement,context);
            SendKey(restart,KeyCode.Escape);
            Assert.AreSame(restart,root.focusController.focusedElement,context);
            Assert.AreEqual(DisplayStyle.Flex,modal.style.display.value,context);
        }
    }

    private static void AssertInside(Rect outer,Rect inner,string context)
    {
        Assert.Greater(inner.width,0,context);Assert.Greater(inner.height,0,context);
        Assert.GreaterOrEqual(inner.xMin,outer.xMin-.5f,context);
        Assert.LessOrEqual(inner.xMax,outer.xMax+.5f,context);
        Assert.GreaterOrEqual(inner.yMin,outer.yMin-.5f,context);
        Assert.LessOrEqual(inner.yMax,outer.yMax+.5f,context);
    }
}

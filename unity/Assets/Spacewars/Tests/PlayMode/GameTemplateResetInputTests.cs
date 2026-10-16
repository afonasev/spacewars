using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
public sealed class GameTemplateResetInputTests:InputTestFixture
{
    private GameObject host;private PanelSettings panel;private NativeMenuNavigation nav;private VisualElement root,page;private Gamepad pad;
    private Action restore;
    public override void Setup()
    {
        base.Setup();restore=NativePreferenceWriter.Capture();pad=InputSystem.AddDevice<Gamepad>();host=new GameObject("settings north shortcut QA");panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;root=doc.rootVisualElement;nav=new NativeMenuNavigation(root,()=>pad);page=NativeSettingsView.Open(root,nav,()=>{});
    }
    public override void TearDown(){nav.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);restore();PlayerPrefs.Save();base.TearDown();}
    private void Poll(bool north){InputSystem.QueueStateEvent(pad,north?new GamepadState().WithButton(GamepadButton.North):new GamepadState());InputSystem.Update();typeof(NativeMenuNavigation).GetMethod("TickGamepad",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(nav,new object[]{pad,Time.unscaledTime});}
    [UnityTest] public IEnumerator KeyboardRepeatCannotConfirmAcrossScreens()
    {
        yield return null;var button=page.Q<Button>("settings-reset-all");button.Focus();
        Action down=()=>{using(var e=KeyDownEvent.GetPooled('\n',KeyCode.Return,EventModifiers.None)){e.target=root.focusController.focusedElement as VisualElement;root.SendEvent(e);}};
        Action up=()=>{using(var e=KeyUpEvent.GetPooled('\n',KeyCode.Return,EventModifiers.None)){e.target=root;root.SendEvent(e);}};
        var storedSize=NativeDisplaySettings.SavedResolution;bool storedFullscreen=NativeDisplaySettings.Fullscreen;
        down();var first=root.Q("settings-confirm");Assert.NotNull(first);root.Q<Button>("settings-confirm-accept").Focus();down();Assert.AreSame(first,root.Q("settings-confirm"),"Repeat must not activate the new screen");
        up();down();Assert.AreNotSame(first,root.Q("settings-confirm"),"A fresh press opens display confirmation");Assert.AreEqual(storedSize,NativeDisplaySettings.SavedResolution);Assert.AreEqual(storedFullscreen,NativeDisplaySettings.Fullscreen,"Display remains unsaved until the separate confirmation");
        up();nav.Back();
    }
    [UnityTest] public IEnumerator NorthIsContextualFreshPressAndNeverConfirmsDialogs()
    {
        yield return null;
        Poll(true);Assert.IsNull(root.Q("settings-confirm"),"Held across entry must not reset");Poll(false);Poll(true);Assert.NotNull(root.Q("settings-confirm"));
        Poll(true);Assert.NotNull(root.Q("settings-confirm"));nav.Back();Poll(true);Assert.IsNull(root.Q("settings-confirm"),"Hold after returning must not repeat");
        Poll(false);var text=new TextField();page.Add(text);text.Focus();Poll(true);Assert.IsNull(root.Q("settings-confirm"),"Text editing owns the top button");text.RemoveFromHierarchy();
        Poll(false);page.Q<DropdownField>("settings-resolution").Focus();var popup=new VisualElement();popup.AddToClassList("unity-base-dropdown");root.panel.visualTree.Add(popup);Poll(true);Assert.IsNull(root.Q("settings-confirm"),"Dropdown owns input");popup.RemoveFromHierarchy();
        Poll(false);nav.UseGamepad();Assert.AreEqual("Верх",page.Q<Button>("settings-reset-all").Q<Label>("menu-pad-glyph").text);Poll(true);Assert.NotNull(root.Q("settings-confirm"));
        Poll(false);Poll(true);Assert.AreEqual("settings-confirm",nav.Scope.name,"Top button must not accept reset confirmation");nav.Back();
        var other=new VisualElement(){name="other-menu"};root.Add(other);nav.SetScope(other,()=>{});Poll(false);Poll(true);Assert.IsNull(root.Q("settings-confirm"),"Shortcut must not escape settings");
    }
}

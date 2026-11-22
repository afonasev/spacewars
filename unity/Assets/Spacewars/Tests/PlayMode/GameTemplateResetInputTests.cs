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
    private void Poll(bool north)=>Poll(north?new GamepadState().WithButton(GamepadButton.North):new GamepadState());
    private void Poll(GamepadState state){InputSystem.QueueStateEvent(pad,state);InputSystem.Update();typeof(NativeMenuNavigation).GetMethod("TickGamepad",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(nav,new object[]{pad,Time.unscaledTime});}
    [UnityTest] public IEnumerator GamepadConfirmCanCloseTheMenuAndReturnFocusToTheField()
    {
        var field=new Button();
        var menu=new VisualElement();
        root.Add(field);
        root.Add(menu);
        var resume=new Button(()=>{nav.SetScope(null);menu.RemoveFromHierarchy();field.Focus();});
        menu.Add(resume);
        yield return null;
        nav.SetScope(menu,preferred:resume);
        Assert.AreSame(resume,root.focusController.focusedElement);
        Poll(new GamepadState());
        Poll(new GamepadState().WithButton(GamepadButton.South));
        Assert.False(nav.Active);
        Assert.AreSame(field,root.focusController.focusedElement);
    }
    [UnityTest] public IEnumerator GamepadEditsSettingsOnlyAfterExplicitActivation()
    {
        yield return null;
        void Press(GamepadButton button)
        {
            Poll(new GamepadState());
            Poll(new GamepadState().WithButton(button));
        }

        var vsync=page.Q<Toggle>("settings-vsync");
        vsync.Focus();
        bool sync=vsync.value;
        Press(GamepadButton.DpadLeft);
        Assert.AreSame(page.Q<Button>("settings-tab-Видео"),root.focusController.focusedElement);
        Assert.AreEqual(sync,vsync.value);
        Press(GamepadButton.DpadRight);
        Assert.AreSame(page.Q<DropdownField>("settings-resolution"),root.focusController.focusedElement);
        vsync.Focus();
        Press(GamepadButton.South);
        Assert.AreEqual(!sync,vsync.value);

        var scale=page.Q<Slider>("settings-graphics-render-scale");
        scale.value=85;
        scale.Focus();
        Press(GamepadButton.DpadRight);
        Assert.AreEqual(85,scale.value);
        Press(GamepadButton.South);
        Assert.True(scale.ClassListContains("settings-editing"));
        Press(GamepadButton.DpadDown);
        Assert.AreSame(scale,root.focusController.focusedElement);
        Press(GamepadButton.DpadRight);
        Assert.AreEqual(90,scale.value);
        Press(GamepadButton.East);
        Assert.AreSame(page,nav.Scope);
        Assert.AreSame(page.Q<Button>("settings-tab-Видео"),root.focusController.focusedElement);
        Assert.False(scale.ClassListContains("settings-editing"));
        Assert.AreEqual(90,scale.value);
        Press(GamepadButton.DpadRight);
        Assert.AreEqual(90,scale.value);

        var fps=page.Q<DropdownField>("settings-fps-limit");
        fps.index=0;
        fps.Focus();
        Press(GamepadButton.South);
        Assert.AreEqual("menu-dropdown-overlay",nav.Scope.name);
        Press(GamepadButton.DpadDown);
        Assert.AreEqual(0,fps.index);
        Press(GamepadButton.East);
        Assert.AreSame(fps,root.focusController.focusedElement);
        Assert.AreEqual(0,fps.index);

        Press(GamepadButton.South);
        Press(GamepadButton.DpadDown);
        Press(GamepadButton.South);
        Assert.AreSame(fps,root.focusController.focusedElement);
        Assert.AreEqual(1,fps.index);
        Assert.AreEqual(-1,NativeDevicePreferences.FrameLimit);
    }

    [UnityTest] public IEnumerator GamepadBReturnsToActiveSectionBeforeClosingSettings()
    {
        yield return null;
        var tab=page.Q<Button>("settings-tab-Интерфейс");
        tab.Focus();
        nav.Activate();
        yield return null;
        var field=page.Q<Toggle>("settings-health");
        field.Focus();
        bool previousValue=field.value;

        var back=new GamepadState().WithButton(GamepadButton.East);
        Poll(new GamepadState());
        Poll(back);
        Assert.AreSame(page,nav.Scope);
        Assert.AreSame(tab,root.focusController.focusedElement);
        Assert.AreEqual(previousValue,field.value);

        Poll(back);
        Assert.AreSame(page,root.Q("settings-screen"),"Holding B must not also leave settings.");
        Poll(new GamepadState());
        Poll(back);
        Assert.IsNull(root.Q("settings-screen"));
    }

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
        Poll(false);nav.UseGamepad();Assert.AreEqual("Y",page.Q<Button>("settings-reset-all").Q<NativeControllerGlyph>("menu-pad-glyph").Q<Label>("controller-glyph-symbol").text);Poll(true);Assert.NotNull(root.Q("settings-confirm"));
        Poll(false);Poll(true);Assert.AreEqual("settings-confirm",nav.Scope.name,"Top button must not accept reset confirmation");nav.Back();
        var other=new VisualElement(){name="other-menu"};root.Add(other);nav.SetScope(other,()=>{});Poll(false);Poll(true);Assert.IsNull(root.Q("settings-confirm"),"Shortcut must not escape settings");
    }
}

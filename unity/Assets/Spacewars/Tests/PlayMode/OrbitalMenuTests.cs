using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using System.Reflection;
using UnityEngine.UIElements;

public sealed class OrbitalMenuTests : InputTestFixture
{
    private GameObject host;
    private PanelSettings panel;
    private VisualElement root;
    private NativeMenuNavigation navigation;
    [TestCase(800,620,1f)]
    [TestCase(1280,800,1f)]
    [TestCase(2560,1600,2f)]
    [TestCase(3372,1972,2.465f)]
    [TestCase(3840,2160,2.7f)]
    public void LargeWindowsKeepMenuControlsReadable(int width,int height,float expected)
    {
        Assert.AreEqual(expected,OrbitalTheme.ReadableScale(width,height),.001f);
        Assert.GreaterOrEqual(44*OrbitalTheme.ReadableScale(width,height),44);
    }
    [SetUp] public override void Setup()
    {
        base.Setup();
        host=new GameObject("Orbital test");panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;root=doc.rootVisualElement;OrbitalTheme.Install(root);navigation=new NativeMenuNavigation(root);
    }
    [TearDown] public override void TearDown(){navigation.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);base.TearDown();}
    [UnityTest] public IEnumerator InstalledPanelResizesWithoutLosingFocusOrClippingControls()
    {
        var small=new RenderTexture(1280,800,0);var large=new RenderTexture(2560,1600,0);
        try
        {
            panel.targetTexture=small;OrbitalTheme.ConfigurePanel(panel,root);
            var page=OrbitalTheme.Screen(root,"resize-review");var card=OrbitalTheme.Card(page);
            var action=OrbitalTheme.Action("Продолжить",()=>{});card.Add(action);navigation.SetScope(page,()=>{},action);
            yield return null;yield return null;
            float before=action.worldBound.height*panel.scale;
            panel.targetTexture=large;
            yield return new WaitForSecondsRealtime(.2f);yield return null;
            Assert.AreEqual(2,panel.scale,.01f,"The installed panel must scale, not just a fallback menu.");
            Assert.AreEqual(before*2,action.worldBound.height*panel.scale,1);
            Assert.AreSame(action,root.focusController.focusedElement);
            Assert.GreaterOrEqual(action.worldBound.xMin,root.worldBound.xMin);
            Assert.LessOrEqual(action.worldBound.xMax,root.worldBound.xMax);
            panel.targetTexture=small;
            yield return new WaitForSecondsRealtime(.2f);yield return null;
            Assert.AreEqual(1,panel.scale,.01f);Assert.AreSame(action,root.focusController.focusedElement);
        }
        finally{panel.targetTexture=null;UnityEngine.Object.DestroyImmediate(small);UnityEngine.Object.DestroyImmediate(large);}
    }
    [UnityTest] public IEnumerator NavigationSkipsDisabledAndSubmitsOnce()
    {
        int calls=0;var a=OrbitalTheme.Action("A",()=>calls++);var disabled=OrbitalTheme.Action("Disabled",()=>Assert.Fail("Disabled activated"));disabled.SetEnabled(false);var b=OrbitalTheme.Action("B",()=>calls++);
        root.Add(a);root.Add(disabled);root.Add(b);yield return null;
        navigation.SetScope(root,null,a);navigation.Move(Vector2.down);Assert.AreSame(b,root.focusController.focusedElement);
        navigation.Activate();Assert.AreEqual(1,calls);
        using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=b;b.SendEvent(evt);}Assert.AreEqual(1,calls,"Default toolkit input must not double-submit our controller intent.");
        navigation.Move(Vector2.down);Assert.AreSame(a,root.focusController.focusedElement);
    }
    [UnityTest] public IEnumerator KeyboardArrowsAndEnterActivateButtonsButDoNotConsumeText()
    {
        int calls=0;var a=OrbitalTheme.Action("A",()=>calls++);var b=OrbitalTheme.Action("B",()=>calls++);root.Add(a);root.Add(b);yield return null;navigation.SetScope(root,null,a);
        using(var key=KeyDownEvent.GetPooled('\0',KeyCode.DownArrow,EventModifiers.None)){key.target=a;a.SendEvent(key);}Assert.AreSame(b,root.focusController.focusedElement);
        using(var key=KeyDownEvent.GetPooled('\0',KeyCode.Return,EventModifiers.None)){key.target=b;b.SendEvent(key);}Assert.AreEqual(1,calls);
    }
    [UnityTest] public IEnumerator GamepadDeadzoneRepeatAndHeldConfirmRearm()
    {
        int calls=0;var a=OrbitalTheme.Action("A",()=>calls++);var b=OrbitalTheme.Action("B",()=>calls++);var c=OrbitalTheme.Action("C",()=>calls++);
        root.Add(a);root.Add(b);root.Add(c);yield return null;navigation.SetScope(root,null,a);
        var pad=InputSystem.AddDevice<Gamepad>();
        void Poll(GamepadState state,float time){InputSystem.QueueStateEvent(pad,state);InputSystem.Update();typeof(NativeMenuNavigation).GetMethod("TickGamepad",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(navigation,new object[]{pad,time});}
        try{
            Poll(new GamepadState(),0);Poll(new GamepadState{leftStick=new Vector2(0,-.15f)},.1f);Assert.AreSame(a,root.focusController.focusedElement);
            Poll(new GamepadState{leftStick=Vector2.down},.2f);Assert.AreSame(b,root.focusController.focusedElement);
            Poll(new GamepadState{leftStick=Vector2.down},.4f);Assert.AreSame(b,root.focusController.focusedElement);
            Poll(new GamepadState{leftStick=Vector2.down},.56f);Assert.AreSame(c,root.focusController.focusedElement);
            Poll(new GamepadState().WithButton(GamepadButton.South),.7f);Assert.AreEqual(1,calls);
            navigation.SetScope(root,null,a);Poll(new GamepadState().WithButton(GamepadButton.South),.8f);Assert.AreEqual(1,calls);
            Poll(new GamepadState(),.9f);Poll(new GamepadState().WithButton(GamepadButton.South),1f);Assert.AreEqual(2,calls);
        }finally{InputSystem.RemoveDevice(pad);}
    }
    [UnityTest] public IEnumerator GamepadStartUsesTheActiveRouteActionBeforeBack()
    {
        int starts=0,backs=0;var action=OrbitalTheme.Action("Launch",()=>{});root.Add(action);yield return null;
        navigation.SetScope(root,()=>backs++,action);navigation.Start=_=>{starts++;return true;};
        var pad=InputSystem.AddDevice<Gamepad>();
        void Poll(GamepadState state,float time){InputSystem.QueueStateEvent(pad,state);InputSystem.Update();typeof(NativeMenuNavigation).GetMethod("TickGamepad",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(navigation,new object[]{pad,time});}
        try{
            Poll(new GamepadState(),0);Poll(new GamepadState().WithButton(GamepadButton.Start),.1f);
            Assert.AreEqual(1,starts);Assert.Zero(backs);
            Poll(new GamepadState(),.2f);navigation.Start=null;Poll(new GamepadState().WithButton(GamepadButton.Start),.3f);Assert.AreEqual(1,backs);
        }finally{InputSystem.RemoveDevice(pad);}
    }
    [UnityTest] public IEnumerator SettingsBackRestoresCallerWithoutResumeAndPersistsPreference()
    {
        bool old=NativeUserSettings.AlwaysHealth;int resumed=0;var menu=new VisualElement();root.Add(menu);var opener=OrbitalTheme.Action("Настройки",()=>{});menu.Add(opener);yield return null;
        try{
            menu.style.display=DisplayStyle.None;
            var settings=NativeSettingsView.Open(root,navigation,()=>{menu.style.display=DisplayStyle.Flex;navigation.SetScope(menu,()=>resumed++,opener);});
            yield return null;
            settings.Q<Toggle>("settings-health").value=!old;
            Assert.AreEqual(!old,NativeUserSettings.AlwaysHealth);
            navigation.Back();Assert.Zero(resumed);Assert.AreSame(opener,root.focusController.focusedElement);Assert.IsNull(root.Q("settings-screen"));
        }finally{NativeUserSettings.AlwaysHealth=old;}
    }
    [UnityTest] public IEnumerator TextEntryCanBeCompletedWithControllerActions()
    {
        var text=new TextField(){name="test-name",value=""};root.Add(text);yield return null;navigation.SetScope(root,null,text);navigation.Activate();yield return null;
        navigation.Activate();Assert.AreEqual("",text.value,"Text is committed only on Done.");
        var keyboard=root.Q(className:"orbital-keyboard");var done=keyboard.Query<Button>().ToList().Single(b=>b.text=="Готово");done.Focus();navigation.Activate();
        Assert.IsNull(root.Q(className:"orbital-keyboard"));Assert.AreSame(text,root.focusController.focusedElement);Assert.AreEqual("А",text.value);
    }
    [UnityTest] public IEnumerator LaboratoryOpensCurrentMatchRevisionIndependentlyOfNextMatchSelection()
    {
        var directory=Path.Combine(Path.GetTempPath(),"spacewars-orbital-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            var baseline=PlayableProfile.Default;var store=new NativeBalanceStore(baseline,Path.Combine(directory,"balance.json"));var data=baseline.CopyData();data.tankWeaponDamage++;
            var active=store.Save("Текущий бой",data);Assert.AreEqual(baseline.Revision,store.Selected.Revision);
            var view=new NativeBalanceView(root,navigation,store,()=>{},_=>null,active);yield return null;
            Assert.AreEqual(active.DisplayName,view.Page.Q<TextField>("lab-name").value);
            Assert.AreEqual(baseline.Revision,store.Selected.Revision,"Opening the active revision must not change the next-match choice.");
        }finally{Directory.Delete(directory,true);}
    }
    [UnityTest] public IEnumerator ControlsHelpKeepsScrollingWithoutScrollbarControls()
    {
        root.style.width=800;root.style.height=620;var page=NativeControlsHelp.Open(root,navigation,()=>{});
        for(int i=0;i<4;i++)yield return null;
        var scroll=page.Q<ScrollView>("controls-scroll");Assert.AreEqual(ScrollerVisibility.Hidden,scroll.horizontalScrollerVisibility);Assert.AreEqual(ScrollerVisibility.Hidden,scroll.verticalScrollerVisibility);
        Assert.Greater(scroll.verticalScroller.highValue,0);
        scroll.Focus();navigation.Move(Vector2.down);Assert.Greater(scroll.scrollOffset.y,0,"D-pad navigation must retain access to the help content");
    }
    [UnityTest] public IEnumerator NarrowLaboratoryKeepsToolbarAndFirstEditorInsideCard()
    {
        var directory=Path.Combine(Path.GetTempPath(),"spacewars-orbital-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            root.style.width=800;root.style.height=620;
            var view=new NativeBalanceView(root,navigation,new NativeBalanceStore(PlayableProfile.Default,Path.Combine(directory,"balance.json")),()=>{},null);
            yield return null;yield return null;
            var card=view.Page.Q(className:"orbital-lab-card");Assert.LessOrEqual(card.worldBound.yMax,root.worldBound.yMax+.5f);
            foreach(var key in new[]{"lab-save","lab-reset","lab-apply","lab-select","lab-back"}){var button=view.Page.Q<Button>(key);Assert.Greater(button.worldBound.width,0,key);Assert.LessOrEqual(button.worldBound.xMax,card.worldBound.xMax+.5f,key);Assert.LessOrEqual(button.worldBound.yMax,card.worldBound.yMax+.5f,key);}
            var editor=view.Page.Query<TextField>().ToList().First(t=>t.name!=null&&t.name.StartsWith("lab-field-"));Assert.Greater(editor.worldBound.width,150);Assert.LessOrEqual(editor.worldBound.xMax,card.worldBound.xMax);
            var scroll=view.Page.Q<ScrollView>("lab-parameters");Assert.AreEqual(ScrollerVisibility.Auto,scroll.verticalScrollerVisibility,"Laboratory retains its scrollbar exception");Assert.AreEqual(Visibility.Visible,scroll.verticalScroller.resolvedStyle.visibility);Assert.LessOrEqual(scroll.contentContainer.worldBound.xMax,scroll.worldBound.xMax+.5f);
            var hints=view.Page.Q(className:"orbital-hints");Assert.LessOrEqual(hints.worldBound.yMax,card.worldBound.yMax+.5f);
        }finally{Directory.Delete(directory,true);}
    }
    [Test] public void SavedNativeRevisionsAreImmutableAndCasProtectsOtherEditors()
    {
        var directory=Path.Combine(Path.GetTempPath(),"spacewars-orbital-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{
            string path=Path.Combine(directory,"balance.json");var baseline=PlayableProfile.Default;var a=new NativeBalanceStore(baseline,path);var b=new NativeBalanceStore(baseline,path);
            var data=baseline.CopyData();data.tankWeaponDamage+=1;var saved=a.Save("Тест",data);data.tankWeaponDamage+=50;
            Assert.AreEqual(baseline.TankWeaponDamage+1,a.Resolve(saved.Revision).TankWeaponDamage);
            Assert.Throws<Spacewars.BalanceLab.StoreConflictException>(()=>b.Save("Другой",baseline.CopyData()));
            a.Select(saved.Revision);var reopened=new NativeBalanceStore(baseline,path);Assert.AreEqual(saved.Revision,reopened.Selected.Revision);
        }finally{Directory.Delete(directory,true);}
    }
}

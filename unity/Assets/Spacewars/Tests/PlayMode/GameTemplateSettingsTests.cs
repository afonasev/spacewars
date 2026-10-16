using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
public sealed class GameTemplateSettingsTests
{
    private GameObject host;
    private PanelSettings panel;
    private NativeMenuNavigation nav;
    private VisualElement root,page;
    private Dictionary<string,int> saved;
    private int oldLimit;private Action restoreAll;
    [SetUp] public void Setup()
    {
        restoreAll=NativePreferenceWriter.Capture();saved=new Dictionary<string,int>();oldLimit=Application.targetFrameRate;
        foreach(var key in new[]{"spacewars.display.width","spacewars.display.height","spacewars.display.fullscreen",NativeDevicePreferences.Prefix+"frameLimit"}){if(PlayerPrefs.HasKey(key))saved[key]=PlayerPrefs.GetInt(key);PlayerPrefs.DeleteKey(key);}
        host=new GameObject("game template settings QA");panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;root=doc.rootVisualElement;OrbitalTheme.ConfigurePanel(panel,root);nav=new NativeMenuNavigation(root);page=NativeSettingsView.Open(root,nav,()=>{});
    }
    [TearDown] public void Cleanup()
    {
        NativePreferenceWriter.Flush=PlayerPrefs.Save;nav.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);
        foreach(var key in new[]{"spacewars.display.width","spacewars.display.height","spacewars.display.fullscreen",NativeDevicePreferences.Prefix+"frameLimit"}){if(saved.TryGetValue(key,out var value))PlayerPrefs.SetInt(key,value);else PlayerPrefs.DeleteKey(key);}restoreAll();PlayerPrefs.Save();Application.targetFrameRate=oldLimit;
    }
    [UnityTest] public IEnumerator PreviewCancelAndConfirmPreserveCorrectPreferencesAndFocus()
    {
        yield return null;
        page.Q<Toggle>("settings-fullscreen").value=false;
        Assert.IsTrue(NativeDisplaySettings.Fullscreen);Assert.NotNull(root.Q("settings-confirm"));Assert.AreSame(root.Q("settings-confirm"),nav.Scope);
        nav.Back();Assert.IsNull(root.Q("settings-confirm"));Assert.IsTrue(NativeDisplaySettings.Fullscreen);Assert.AreSame(page,nav.Scope);
        page.Q<Toggle>("settings-fullscreen").value=false;nav.ActivateElement(root.Q<Button>("settings-confirm-accept"));
        Assert.IsFalse(NativeDisplaySettings.Fullscreen);Assert.AreSame(page,nav.Scope);
        Assert.IsTrue(page.Q<DropdownField>("settings-resolution").choices[0].StartsWith("Auto"));
    }
    [UnityTest] public IEnumerator UnconfirmedModeTimesOutWithoutSaving()
    {
        page.Q<Toggle>("settings-fullscreen").value=false;
        yield return new WaitForSecondsRealtime(15.3f);
        Assert.IsNull(root.Q("settings-confirm"));Assert.IsTrue(NativeDisplaySettings.Fullscreen);Assert.AreSame(page,nav.Scope);
    }
    [Test] public void FailedWriteRestoresLastCorrectChoice()
    {
        NativeDevicePreferences.FrameLimit=60;
        NativePreferenceWriter.Flush=()=>throw new IOException("fixture disk full");
        Assert.Throws<IOException>(()=>NativeDevicePreferences.FrameLimit=120);
        Assert.AreEqual(60,NativeDevicePreferences.FrameLimit);
        StringAssert.Contains("Не удалось сохранить",page.Query<Label>().ToList().Find(l=>l.text.Contains("Не удалось сохранить")).text);
    }
    [UnityTest] public IEnumerator GlobalResetNeedsConfirmationAndLeavesUnrelatedData()
    {
        NativeDevicePreferences.FrameLimit=120;PlayerPrefs.SetString("spacewars.template.fixture.history","keep");
        nav.ActivateElement(page.Q<Button>("settings-reset-all"));Assert.NotNull(root.Q("settings-confirm"));nav.Back();Assert.AreEqual(120,NativeDevicePreferences.FrameLimit);
        nav.ActivateElement(page.Q<Button>("settings-reset-all"));nav.ActivateElement(root.Q<Button>("settings-confirm-accept"));
        Assert.NotNull(root.Q("settings-confirm"),"Screen reset requires its own safe display confirmation");
        Assert.AreEqual(120,NativeDevicePreferences.FrameLimit);nav.ActivateElement(root.Q<Button>("settings-confirm-accept"));
        Assert.AreEqual(0,NativeDevicePreferences.FrameLimit);Assert.AreEqual("keep",PlayerPrefs.GetString("spacewars.template.fixture.history"));PlayerPrefs.DeleteKey("spacewars.template.fixture.history");yield return null;
    }
}

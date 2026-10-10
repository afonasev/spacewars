using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class NativeDisplaySettingsTests
{
    private readonly string[] ints={"spacewars.display.fullscreen","spacewars.display.shadows","spacewars.display.width","spacewars.display.height"};
    private readonly string[] floats={"spacewars.controls.cursorSpeed","spacewars.controls.cameraSpeed",NativeCameraScrollSettings.EdgeKey,NativeCameraScrollSettings.ArrowKey};
    private readonly Dictionary<string,int> oldInts=new Dictionary<string,int>();
    private readonly Dictionary<string,float> oldFloats=new Dictionary<string,float>();
    private readonly Dictionary<Light,LightShadows> oldLights=new Dictionary<Light,LightShadows>();
    private GameObject host;
    private PanelSettings panel;
    private NativeMenuNavigation navigation;
    [SetUp] public void Setup()
    {
        oldInts.Clear();oldFloats.Clear();oldLights.Clear();
        foreach(var key in ints){if(PlayerPrefs.HasKey(key))oldInts[key]=PlayerPrefs.GetInt(key);PlayerPrefs.DeleteKey(key);}
        foreach(var key in floats){if(PlayerPrefs.HasKey(key))oldFloats[key]=PlayerPrefs.GetFloat(key);PlayerPrefs.DeleteKey(key);}
        foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))oldLights[light]=light.shadows;
    }
    [TearDown] public void Cleanup()
    {
        navigation?.Dispose();navigation=null;
        if(host!=null)UnityEngine.Object.DestroyImmediate(host);
        if(panel!=null)UnityEngine.Object.DestroyImmediate(panel);
        foreach(var key in ints){if(oldInts.TryGetValue(key,out int value))PlayerPrefs.SetInt(key,value);else PlayerPrefs.DeleteKey(key);}
        foreach(var key in floats){if(oldFloats.TryGetValue(key,out float value))PlayerPrefs.SetFloat(key,value);else PlayerPrefs.DeleteKey(key);}
        foreach(var pair in oldLights)if(pair.Key!=null)pair.Key.shadows=pair.Value;
        PlayerPrefs.Save();
    }
    [Test] public void FirstLaunchUsesFullscreenAndMonitorModesHaveSafeFallback()
    {
        Assert.IsTrue(NativeDisplaySettings.Fullscreen);Assert.IsTrue(NativeDisplaySettings.Shadows);
        var desktop=new Vector2Int(1920,1080);
        var modes=NativeDisplaySettings.ResolutionOptions(new[]{desktop,new Vector2Int(1280,720),desktop,Vector2Int.zero},desktop);
        CollectionAssert.AreEqual(new[]{new Vector2Int(1280,720),desktop},modes);
        Assert.AreEqual(desktop,NativeDisplaySettings.ResolveResolution(new Vector2Int(3840,2160),modes,desktop));
        Assert.AreEqual(new Vector2Int(1280,720),NativeDisplaySettings.ResolveResolution(new Vector2Int(1280,720),modes,desktop));
        CollectionAssert.AreEqual(new[]{desktop},NativeDisplaySettings.ResolutionOptions(Array.Empty<Vector2Int>(),desktop));
        NativeDisplaySettings.Fullscreen=false;
        Assert.Zero(PlayerPrefs.GetInt("spacewars.display.fullscreen"));Assert.IsFalse(NativeDisplaySettings.Fullscreen);
        NativeDisplaySettings.SetResolution(NativeDisplaySettings.AvailableResolutions[0]);
        Assert.AreEqual(NativeDisplaySettings.AvailableResolutions[0],NativeDisplaySettings.SavedResolution);
    }
    [Test] public void CameraScrollUsesCameraDirectionsAndFrameIndependentSpeed()
    {
        host=new GameObject("Camera direction test");host.transform.rotation=Quaternion.Euler(48,180,0);
        var right=NativeCameraScrollSettings.Displacement(host.transform,Vector2.right,12,1);
        Assert.Less(right.x,0);Assert.AreEqual(0,right.y);Assert.AreEqual(12,right.magnitude,.001f);
        var up=NativeCameraScrollSettings.Displacement(host.transform,Vector2.up,8,1);Assert.Less(up.z,0);Assert.AreEqual(8,up.magnitude,.001f);
        Assert.AreEqual(NativeCameraScrollSettings.Displacement(host.transform,Vector2.right,12,1f/30),NativeCameraScrollSettings.Displacement(host.transform,Vector2.right,12,1f/60)*2);
        PlayerPrefs.SetFloat(NativeCameraScrollSettings.EdgeKey,20);PlayerPrefs.SetFloat(NativeCameraScrollSettings.ArrowKey,5);
        Assert.AreEqual(20,NativeCameraScrollSettings.EdgeSpeed(8));Assert.AreEqual(5,NativeCameraScrollSettings.ArrowSpeed(12));
        PlayerPrefs.SetFloat(NativeCameraScrollSettings.EdgeKey,float.NaN);Assert.AreEqual(8,NativeCameraScrollSettings.EdgeSpeed(8));
    }
    [Test] public void ShadowsApplyImmediatelyAndNewMatchSunKeepsPreference()
    {
        host=new GameObject("Settings shadow sun");var light=host.AddComponent<Light>();light.type=LightType.Directional;
        NativeDisplaySettings.Shadows=false;Assert.AreEqual(LightShadows.None,light.shadows);
        NativeDisplaySettings.ConfigureSun(light);Assert.AreEqual(LightShadows.None,light.shadows);
        NativeDisplaySettings.Shadows=true;Assert.AreEqual(LightShadows.Soft,light.shadows);
    }
    [UnityTest] public IEnumerator ControlsPersistAndSettingsRemainNavigableAtSmallViewport()
    {
        host=new GameObject("Settings layout review");panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var document=host.AddComponent<UIDocument>();document.panelSettings=panel;var root=document.rootVisualElement;
        var target=new RenderTexture(800,620,0);target.Create();panel.targetTexture=target;OrbitalTheme.ConfigurePanel(panel,root);navigation=new NativeMenuNavigation(root);
        try
        {
            var controls=new NativeLocalControlSettings();var page=NativeSettingsView.Open(root,navigation,()=>{},controls);
            yield return null;yield return null;
            Assert.AreEqual(ScrollerVisibility.Hidden,page.Q<ScrollView>("settings-scroll").verticalScrollerVisibility);
            AssertColumns(page);
            var edge=page.Q<Slider>("settings-camera-edge");edge.value=edge.highValue;
            var arrows=page.Q<Slider>("settings-camera-arrows");arrows.value=arrows.lowValue;
            Assert.AreEqual(edge.highValue,NativeCameraScrollSettings.EdgeSpeed(8));Assert.AreEqual(arrows.lowValue,NativeCameraScrollSettings.ArrowSpeed(12));
            var cursor=page.Q<Slider>("settings-pad-cursorSpeed");cursor.value=cursor.highValue;
            var camera=page.Q<Slider>("settings-pad-cameraSpeed");camera.value=camera.lowValue;
            var reloaded=new NativeLocalControlSettings();Assert.AreEqual(controls.CursorSpeed,reloaded.CursorSpeed);Assert.AreEqual(controls.CameraSpeed,reloaded.CameraSpeed);
            page.Q<Toggle>("settings-fullscreen").value=false;page.Q<Toggle>("settings-shadows").value=false;
            navigation.Back();page=NativeSettingsView.Open(root,navigation,()=>{},reloaded);
            yield return null;yield return null;
            Assert.AreEqual(edge.highValue,page.Q<Slider>("settings-camera-edge").value);Assert.AreEqual(arrows.lowValue,page.Q<Slider>("settings-camera-arrows").value);
            Assert.IsFalse(page.Q<Toggle>("settings-fullscreen").value);Assert.IsFalse(page.Q<Toggle>("settings-shadows").value);
            Assert.AreSame(page.Q<DropdownField>("settings-resolution"),root.focusController.focusedElement);
            string directory=Environment.GetEnvironmentVariable("SPACEWARS_SETTINGS_EVIDENCE");
            if(!string.IsNullOrEmpty(directory)){Directory.CreateDirectory(directory);Capture(target,Path.Combine(directory,"settings-800x620.png"));}
            var scroll=page.Q<ScrollView>("settings-scroll");
            for(int i=0;i<12;i++)navigation.Move(Vector2.down);
            yield return null;yield return null;
            var help=page.Q<Button>("settings-controls");Assert.AreSame(help,root.focusController.focusedElement);
            Assert.GreaterOrEqual(help.worldBound.yMin,scroll.contentViewport.worldBound.yMin-1);
            Assert.LessOrEqual(help.worldBound.yMax,scroll.contentViewport.worldBound.yMax+1);
            var back=page.Q<Button>("settings-back");Assert.GreaterOrEqual(back.worldBound.yMin,root.worldBound.yMin);Assert.LessOrEqual(back.worldBound.yMax,root.worldBound.yMax);
            if(!string.IsNullOrEmpty(directory))Capture(target,Path.Combine(directory,"settings-scrolled-800x620.png"));
            panel.targetTexture=null;target.Release();target.width=1280;target.height=800;target.Create();panel.targetTexture=target;
            navigation.SetScope(page,()=>{},page.Q<DropdownField>("settings-resolution"));scroll.scrollOffset=Vector2.zero;
            yield return new WaitForSecondsRealtime(.3f);yield return null;
            AssertColumns(page);
            if(!string.IsNullOrEmpty(directory))Capture(target,Path.Combine(directory,"settings-1280x800.png"));
            scroll.scrollOffset=new Vector2(0,100);yield return null;yield return null;
            if(!string.IsNullOrEmpty(directory))Capture(target,Path.Combine(directory,"settings-camera-scroll-1280x800.png"));
        }
        finally{panel.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);}
    }
    private static void AssertColumns(VisualElement page)
    {
        float left=page.Q<DropdownField>("settings-resolution").Q(className:"unity-base-field__input").worldBound.xMin;
        foreach(string name in new[]{"settings-fullscreen","settings-shadows","settings-health","settings-volume","settings-camera-edge","settings-camera-arrows","settings-pad-cursorSpeed","settings-pad-cameraSpeed"})
            Assert.AreEqual(left,page.Q(name).Q(className:"unity-base-field__input").worldBound.xMin,1,name+" control column");
        foreach(var toggle in page.Query<Toggle>().ToList())
        {
            var input=toggle.Q(className:"unity-base-field__input");
            Assert.Zero(input.resolvedStyle.borderLeftWidth,"Checkbox must not have an outer frame");
            Assert.Zero(input.resolvedStyle.backgroundColor.a,"Checkbox must not have an outer background");
        }
    }
    private static void Capture(RenderTexture target,string path)
    {
        var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture);}
    }
}

using System.Linq;
using System.Collections;
using UnityEngine.TestTools;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class NativeLobbyUiTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;
    private PlayableBootstrap app;
    private T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(app);
    private void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(app,null);
    [SetUp] public void Setup()
    {
        host=new GameObject("Lobby test");app=host.AddComponent<PlayableBootstrap>();app.enabled=false;
        typeof(PlayableBootstrap).GetField("profile",Flags).SetValue(app,PlayableProfile.ThreeCrossingsDefault);
        Call("CreateWorld");Call("CreateHud");
        typeof(PlayableBootstrap).GetField("input",Flags).SetValue(app,host.AddComponent<Spacewars.Input.PlayableInput>());
        Call("CreateLobby");
    }
    [TearDown] public void Teardown(){foreach(var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))Object.DestroyImmediate(camera.gameObject);foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))Object.DestroyImmediate(light.gameObject);Object.DestroyImmediate(host);}
    [Test] public void LobbyIsRealSetupAndHasNoAuthority()
    {
        Call("ShowLobby");
        Assert.IsNull(Get<Spacewars.Runtime.PlayableRuntime>("runtime"));
        Assert.False(Get<Spacewars.Input.PlayableInput>("input").WorldInputEnabled);
        var lobby=Get<VisualElement>("lobbyScreen");
        Assert.AreEqual(2,lobby.Query<TextField>().ToList().Count);
        Assert.AreEqual(4,lobby.Query<DropdownField>().ToList().Count);
        Assert.IsNotNull(lobby.Q<Button>("lobby-launch"));
        Assert.Greater(Get<PlayableMapTerrain>("lobbyTerrain").Texture.GetPixels32().Distinct().Count(),2,"Public terrain must be uploaded before first lobby frame");
        lobby.Q<TextField>("lobby-human-name").value=" ";
        Assert.False(lobby.Q<Button>("lobby-launch").enabledSelf);
        StringAssert.Contains("имена",Get<Label>("lobbyStatus").text);
        Assert.AreEqual(DisplayStyle.None,Get<VisualElement>("top").style.display.value);
    }
    [UnityTest] public IEnumerator NarrowLobbyKeepsLaunchAndFieldsInsideViewport()
    {
        Call("ShowLobby");var root=Get<VisualElement>("root");root.style.width=760;root.style.height=900;
        yield return null;yield return null;
        var lobby=Get<VisualElement>("lobbyScreen");var bounds=root.worldBound;
        var map=Get<VisualElement>("mapPanel").worldBound;var preview=Get<PlayableMapSurface>("lobbyPreview").worldBound;
        Assert.LessOrEqual(preview.yMax,map.yMax+.5f,"Preview overflows map card");
        Assert.LessOrEqual(map.yMax,Get<Label>("lobbyStatus").worldBound.yMin+.5f,"Map overlaps launch status");
        Assert.Less(Mathf.Abs(preview.width-preview.height),1,"Narrow preview must remain square");
        foreach(var control in lobby.Query<VisualElement>().ToList().Where(e=>e is Button||e is TextField||e is DropdownField))
        {
            Assert.Greater(control.worldBound.width,0,control.name);
            Assert.GreaterOrEqual(control.worldBound.xMin,bounds.xMin-.5f,control.name);
            Assert.LessOrEqual(control.worldBound.xMax,bounds.xMax+.5f,control.name);
            Assert.GreaterOrEqual(control.worldBound.yMin,bounds.yMin-.5f,control.name);
            Assert.LessOrEqual(control.worldBound.yMax,bounds.yMax+.5f,control.name);
        }
    }
    [UnityTest] public IEnumerator StandardWindowSettingsDoNotOverlapDeviceAssignment()
    {
        Call("ShowLobby");var root=Get<VisualElement>("root");root.style.width=1280;root.style.height=800;
        yield return null;yield return null;
        var lobby=Get<VisualElement>("lobbyScreen");var bind=lobby.Q<Button>("lobby-bind-devices");
        foreach(var field in lobby.Query<DropdownField>().ToList().Take(2))
            Assert.LessOrEqual(field.worldBound.yMax,bind.worldBound.yMin+.5f,"Settings overlap device assignment");
        Assert.LessOrEqual(Get<VisualElement>("rosterPanel").worldBound.yMax,Get<Label>("lobbyStatus").worldBound.yMin+.5f);
    }
    [UnityTest] public IEnumerator InstalledMenuOpensLobbyAndRetainsUpdateOnReturn()
    {
        typeof(PlayableBootstrap).GetField("nativeMainMenuEnabled",Flags).SetValue(app,true);
        Call("ShowMainMenu");yield return null;
        var menu=Get<NativeMainMenu>("nativeMainMenu");
        try
        {
            Assert.IsNull(Get<Spacewars.Runtime.PlayableRuntime>("runtime"));
            Assert.IsNotNull(menu.GetComponent<UIDocument>().rootVisualElement.Query<Button>().ToList().Single(b=>b.text=="Обновить"));
            yield return (IEnumerator)typeof(NativeMainMenu).GetMethod("EnterGame",Flags).Invoke(menu,null);
            yield return null;
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("lobbyScreen").style.display.value);
            Assert.IsNull(Get<Spacewars.Runtime.PlayableRuntime>("runtime"));
            Call("ShowMainMenu");yield return null;
            menu=Get<NativeMainMenu>("nativeMainMenu");
            Assert.IsNotNull(menu.GetComponent<UIDocument>().rootVisualElement.Query<Button>().ToList().Single(b=>b.text=="Обновить"));
        }
        finally{if(menu!=null)Object.DestroyImmediate(menu.gameObject);}
    }
    [Test] public void BackPreservesDraftAndOnlySwitchesMenu()
    {
        Call("ShowLobby");var lobby=Get<VisualElement>("lobbyScreen");
        lobby.Q<TextField>("lobby-human-name").value="Командир";
        Call("ShowMainMenu");Call("ShowLobby");
        Assert.AreEqual("Командир",lobby.Q<TextField>("lobby-human-name").value);
        Assert.IsNull(Get<Spacewars.Runtime.PlayableRuntime>("runtime"));
        Assert.AreEqual(DisplayStyle.None,Get<VisualElement>("menuScreen").style.display.value);
    }
}

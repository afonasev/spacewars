using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class LoadingScreenTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;
    private PanelSettings panel;
    private T Get<T>(PlayableBootstrap game,string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(game);
    private void Call(PlayableBootstrap game,string name)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(game,null);
    [TearDown] public void Cleanup(){if(host)UnityEngine.Object.DestroyImmediate(host);if(panel)UnityEngine.Object.DestroyImmediate(panel);}

    [UnityTest] public IEnumerator StartupAndMatchCoverInitializationUntilReady()
    {
        host=new GameObject("loading lifecycle");var game=host.AddComponent<PlayableBootstrap>();
        yield return null;
        var startup=Get<GameObject>(game,"startupLoadingHost");Assert.NotNull(startup);
        Assert.AreEqual("Загрузка меню…",startup.GetComponent<UIDocument>().rootVisualElement.Q<Label>("loading-status").text);
        Assert.IsNull(Get<PlayableRuntime>(game,"runtime"));
        float deadline=Time.realtimeSinceStartup+30;
        while(Get<GameObject>(game,"startupLoadingHost")!=null&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.IsNull(Get<GameObject>(game,"startupLoadingHost"),"Startup overlay must be removed.");
        var menu=Get<NativeMainMenu>(game,"nativeMainMenu");Assert.NotNull(menu.ScreenRoot);
        Assert.AreEqual(OrbitalTheme.Copyright,menu.ScreenRoot.Q<Label>("menu-copyright").text);
        Call(game,"ShowLobby");var lobby=Get<VisualElement>(game,"lobbyScreen");Assert.NotNull(lobby.Q<Label>("menu-copyright"));
        Call(game,"LaunchLobbyMatch");var loading=Get<VisualElement>(game,"loadingScreen");
        Assert.IsTrue(Get<bool>(game,"preparing"));Assert.AreEqual(DisplayStyle.Flex,loading.style.display.value);
        Assert.IsNull(Get<PlayableRuntime>(game,"runtime"),"Overlay must precede synchronous session creation.");
        Assert.AreEqual(DisplayStyle.None,lobby.style.display.value);
        while(Get<bool>(game,"preparing")&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.IsFalse(Get<bool>(game,"preparing"));Assert.IsFalse(Get<bool>(game,"inLobby"));
        Assert.NotNull(Get<PlayableRuntime>(game,"runtime").Latest);
        Assert.AreEqual(DisplayStyle.None,loading.style.display.value);
        Call(game,"ReturnToMainMenu");
        deadline=Time.realtimeSinceStartup+10;
        while(Get<PlayableRuntime>(game,"runtime")!=null&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.IsNull(Get<PlayableRuntime>(game,"runtime"));
        yield return null;Assert.NotNull(Get<NativeMainMenu>(game,"nativeMainMenu").ScreenRoot.Q<Label>("menu-copyright"));
    }

    [UnityTest] public IEnumerator RenderedLoadingAndMenuFootersFitSmallAndWideWindows()
    {
        host=new GameObject("loading layout");panel=ScriptableObject.CreateInstance<PanelSettings>();
        panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;
        OrbitalTheme.ConfigurePanel(panel,doc.rootVisualElement);
        foreach(var size in new[]{new Vector2Int(800,620),new Vector2Int(1280,800),new Vector2Int(1920,1080)})
        {
            var target=new RenderTexture(size.x,size.y,24);target.Create();panel.targetTexture=target;
            try
            {
                foreach(string status in new[]{"Загрузка меню…","Подготовка матча…"})
                {
                    var screen=OrbitalTheme.Loading(doc.rootVisualElement,status);
                    for(int i=0;i<8;i++)yield return null;
                    var footer=screen.Q<Label>("menu-copyright");var line=screen.Q(className:"orbital-loading-line");
                    Assert.Greater(footer.worldBound.width,100);Assert.Greater(line.worldBound.width,100);
                    Assert.GreaterOrEqual(footer.worldBound.yMin,line.worldBound.yMax);
                    Assert.LessOrEqual(footer.worldBound.yMax,screen.worldBound.yMax);
                    Assert.GreaterOrEqual(footer.worldBound.xMin,screen.worldBound.xMin);
                    Assert.LessOrEqual(footer.worldBound.xMax,screen.worldBound.xMax);
                    Save(target,(status.StartsWith("Загрузка")?"startup-":"match-")+size.x+"x"+size.y+".png");
                    screen.RemoveFromHierarchy();
                }
                var menu=host.AddComponent<NativeMainMenu>();menu.HostRoot=doc.rootVisualElement;
                for(int i=0;i<8;i++)yield return null;
                var signature=menu.ScreenRoot.Q<Label>("menu-copyright");var info=menu.ScreenRoot.Q(className:"orbital-menu-footer");
                Assert.LessOrEqual(info.worldBound.yMax,signature.worldBound.yMin,"Menu metadata must not cover copyright.");
                Save(target,"main-"+size.x+"x"+size.y+".png");
                var nav=menu.Navigation;
                var settings=NativeSettingsView.Open(doc.rootVisualElement,nav,()=>{});
                for(int i=0;i<8;i++)yield return null;
                var card=settings.Q(className:"orbital-settings-card");
                Assert.LessOrEqual(card.worldBound.yMax,settings.Q<Label>("menu-copyright").worldBound.yMin);
                Save(target,"settings-"+size.x+"x"+size.y+".png");settings.RemoveFromHierarchy();
                UnityEngine.Object.DestroyImmediate(menu);
            }
            finally{panel.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
    }
    private static void Save(RenderTexture target,string filename)
    {
        string output=Environment.GetEnvironmentVariable("SPACEWARS_LOADING_EVIDENCE");if(string.IsNullOrEmpty(output))return;
        Directory.CreateDirectory(output);Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);
        var previous=RenderTexture.active;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(output,filename),texture.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(texture);}
    }
}

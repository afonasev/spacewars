using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Linq;
using Spacewars.Simulation;
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
        Assert.AreEqual(OrbitalTheme.FooterText,menu.ScreenRoot.Q<Label>("menu-copyright").text);
        Call(game,"ShowLobby");var lobby=Get<VisualElement>(game,"lobbyScreen");Assert.NotNull(lobby.Q<Label>("menu-copyright"));
        Call(game,"LaunchLobbyMatch");var loading=Get<VisualElement>(game,"loadingScreen");
        Assert.IsTrue(Get<bool>(game,"preparing"));Assert.AreEqual(DisplayStyle.Flex,loading.style.display.value);
        Assert.IsNull(Get<PlayableRuntime>(game,"runtime"),"Overlay must precede synchronous session creation.");
        Assert.AreEqual(DisplayStyle.None,lobby.style.display.value);
        var emblem=loading.Q<OrbitalLoadingEmblem>("loading-emblem");Assert.NotNull(emblem);
        int initialPaints=emblem.PaintCount;int coveredFrames=0;string lastStage="";float since=Time.realtimeSinceStartup;
        while(Get<bool>(game,"preparing")&&Time.realtimeSinceStartup<deadline){
            string stage=loading.Q<Label>("loading-status").text;
            if(stage!=lastStage){Debug.Log("Loading contract: "+lastStage+" -> "+stage+" at "+(Time.realtimeSinceStartup-since).ToString("F3")+"s, frames="+coveredFrames);lastStage=stage;}
            if(stage!="Подготовка первого кадра…")Assert.IsFalse(Get<Camera>(game,"cameraView").enabled,"Do not render hidden partial terrain.");
            var pending=Get<PlayableRuntime>(game,"runtime");
            if(pending?.Latest!=null)Assert.AreEqual(0,pending.Latest.Tick,"Preparation cannot spend match time.");
            Assert.IsFalse(Get<Spacewars.Input.PlayableInput>(game,"input").WorldInputEnabled);
            coveredFrames++;yield return null;
        }
        Assert.Greater(coveredFrames,2,"Terrain preparation must yield actual frames.");
        Assert.Greater(emblem.PaintCount,initialPaints+1,"Emblem must repaint during actual match preparation.");
        Assert.IsFalse(Get<bool>(game,"preparing"),"stage="+loading.Q<Label>("loading-status").text+" hostReady="+Get<PlayableRuntime>(game,"runtime")?.IsPrepared);Assert.IsFalse(Get<bool>(game,"inLobby"));
        Assert.IsTrue(Get<Camera>(game,"cameraView").enabled);
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
                    var emblem=screen.Q<OrbitalLoadingEmblem>("loading-emblem");
                    int paints=emblem.PaintCount;float phase=emblem.LastPaintedPhase;
                    for(int i=0;i<4;i++)yield return null;
                    Assert.Greater(emblem.PaintCount,paints);Assert.Greater(emblem.LastPaintedPhase,phase);
                    var brand=screen.Q("loading-wordmark");
                    Assert.GreaterOrEqual(brand.worldBound.xMin,screen.worldBound.xMin);
                    Assert.LessOrEqual(brand.worldBound.xMax,screen.worldBound.xMax);
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
    [UnityTest] public IEnumerator MatchOverlayKeepsItsThemeAfterMovingOutsideTheHud()
    {
        host=new GameObject("reparented loading layout");panel=ScriptableObject.CreateInstance<PanelSettings>();
        panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;
        OrbitalTheme.ConfigurePanel(panel,doc.rootVisualElement);
        var hud=new VisualElement();doc.rootVisualElement.Add(hud);
        foreach(var size in new[]{new Vector2Int(800,620),new Vector2Int(1280,800),new Vector2Int(1920,1080)})
        {
            var target=new RenderTexture(size.x,size.y,24);target.Create();panel.targetTexture=target;
            try
            {
                var screen=OrbitalTheme.Loading(hud,"Подготовка поверхности…");
                // LaunchLobbyMatch moves the overlay out of the themed local-seat HUD.
                doc.rootVisualElement.Add(screen);
                for(int i=0;i<8;i++)yield return null;
                Assert.AreEqual(Position.Absolute,screen.resolvedStyle.position,"Overlay must retain its own theme after reparenting.");
                Assert.That(screen.worldBound.width,Is.EqualTo(doc.rootVisualElement.worldBound.width).Within(1));
                Assert.That(screen.worldBound.height,Is.EqualTo(doc.rootVisualElement.worldBound.height).Within(1));
                var brand=screen.Q("loading-wordmark");
                Assert.AreEqual(FlexDirection.Row,brand.resolvedStyle.flexDirection);
                Assert.That(brand.worldBound.center.x,Is.EqualTo(screen.worldBound.center.x).Within(1));
                var status=screen.Q<Label>("loading-status");
                Assert.Greater(status.resolvedStyle.color.r,.7f,"Status must stay readable over the backdrop.");
                Assert.Greater(screen.Q(className:"orbital-loading-line").worldBound.width,100);
                Save(target,"reparented-match-"+size.x+"x"+size.y+".png");
                screen.RemoveFromHierarchy();
            }
            finally{panel.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
    }
    [UnityTest] public IEnumerator DestroyDuringTerrainPreparationCancelsOwnedWork()
    {
        host=new GameObject("cancel staged loading");var game=host.AddComponent<PlayableBootstrap>();
        float deadline=Time.realtimeSinceStartup+30;yield return null;
        while(Get<GameObject>(game,"startupLoadingHost")!=null&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.IsNull(Get<GameObject>(game,"startupLoadingHost"));Call(game,"ShowLobby");Call(game,"LaunchLobbyMatch");
        for(int i=0;i<4;i++)yield return null;
        Assert.IsTrue(Get<bool>(game,"preparing"));
        var runtime=Get<PlayableRuntime>(game,"runtime");
        UnityEngine.Object.DestroyImmediate(host);host=null;
        for(int i=0;i<4;i++)yield return null;
        Assert.IsTrue(runtime==null||runtime.IsStopped,"Destroyed preparation cannot retain an authority.");
    }
    [UnityTest] public IEnumerator StagedTerrainPreservesExactMeshesAndMaskPixels()
    {
        var data=PlayableProfile.ThreeCrossingsDefault.CopyData();
        foreach(IPlayableTerrain map in new IPlayableTerrain[]{ThreeCrossingsMap.Default,new FoundryMap(JsonUtility.FromJson<FoundryProfileData>(Resources.Load<TextAsset>("FoundryProfile").text)),new BasaltCrownMap(),new MossHighlandMap(JsonUtility.FromJson<MossHighlandProfileData>(Resources.Load<TextAsset>("MossHighlandProfile").text))})
        {
            var profile=PlayableProfile.Create(data,map);host=new GameObject("terrain equivalence "+map.Id);
            var syncHost=new GameObject("sync");syncHost.transform.SetParent(host.transform);
            var stagedHost=new GameObject("staged");stagedHost.transform.SetParent(host.transform);
            PlayableWorld sync=null,staged=null;PlayableMapTerrain syncMap=null,stagedMap=null;
            try{
                sync=new PlayableWorld(syncHost.transform,profile);
                staged=new PlayableWorld(stagedHost.transform,profile,staged:true);
                yield return staged.Preparation;
                var left=syncHost.GetComponentsInChildren<MeshFilter>();var right=stagedHost.GetComponentsInChildren<MeshFilter>();
                Assert.AreEqual(left.Length,right.Length,map.Id);
                for(int i=0;i<left.Length;i++){
                    Assert.AreEqual(left[i].name,right[i].name,map.Id);
                    CollectionAssert.AreEqual(left[i].sharedMesh.vertices,right[i].sharedMesh.vertices,map.Id+" vertices");
                    CollectionAssert.AreEqual(left[i].sharedMesh.triangles,right[i].sharedMesh.triangles,map.Id+" indices");
                }
                foreach(string field in new[]{"mossPaintMask","mossAccentMask","mossTransitionMask","basaltPaintMask","basaltAccentMask","foundryRoadMask","foundryLavaHeat","foundryRouteMask"}){
                    var f=typeof(PlayableWorld).GetField(field,Flags);var x=(Texture2D)f.GetValue(sync);var y=(Texture2D)f.GetValue(staged);
                    Assert.AreEqual(x==null,y==null,field);if(x!=null)Assert.IsTrue(x.GetRawTextureData().AsSpan().SequenceEqual(y.GetRawTextureData()),map.Id+field);
                }
                var surfaces=typeof(PlayableWorld).GetField("surfaceMaterials",Flags);var a=(ThreeCrossingsMaterials)surfaces.GetValue(sync);var b=(ThreeCrossingsMaterials)surfaces.GetValue(staged);
                if(a!=null){
                    var x=new[]{a.RoadMask,a.ShoreMask,a.RockMask,a.ConstructionMask,a.WeatheringMask};var y=new[]{b.RoadMask,b.ShoreMask,b.RockMask,b.ConstructionMask,b.WeatheringMask};
                    for(int i=0;i<x.Length;i++)Assert.IsTrue(x[i].GetRawTextureData().AsSpan().SequenceEqual(y[i].GetRawTextureData()),"Crossings mask "+i);
                }
                syncMap=new PlayableMapTerrain(profile);stagedMap=new PlayableMapTerrain(profile,staged:true);yield return stagedMap.Preparation;
                syncMap.ShowPublicTerrain();stagedMap.ShowPublicTerrain();Assert.IsTrue(syncMap.Texture.GetRawTextureData().AsSpan().SequenceEqual(stagedMap.Texture.GetRawTextureData()),map.Id+" minimap");
            }finally{syncMap?.Dispose();stagedMap?.Dispose();sync?.Dispose();staged?.Dispose();UnityEngine.Object.DestroyImmediate(host);host=null;}
            yield return null;
        }
    }
    [UnityTest] public IEnumerator NestedPreparationErrorsAndCancellationDisposeWork()
    {
        var type=typeof(OrbitalTheme).Assembly.GetType("Spacewars.Presentation.LoadingPreparation");
        var run=type.GetMethod("Run",BindingFlags.Public|BindingFlags.Static);int disposed=0,failures=0;
        IEnumerator Broken(){try{yield return null;throw new InvalidOperationException("preparation fixture failure");}finally{disposed++;}}
        IEnumerator Parent(){yield return Broken();}
        var work=(IEnumerator)run.Invoke(null,new object[]{Parent(),(Action<Exception>)(error=>{Assert.AreEqual("preparation fixture failure",error.Message);failures++;})});
        yield return work;Assert.AreEqual(1,failures);Assert.AreEqual(1,disposed);
        work=(IEnumerator)run.Invoke(null,new object[]{Parent(),(Action<Exception>)(_=>Assert.Fail("Cancelled work must not run its next step."))});
        Assert.IsTrue(work.MoveNext());((IDisposable)work).Dispose();Assert.AreEqual(2,disposed);
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

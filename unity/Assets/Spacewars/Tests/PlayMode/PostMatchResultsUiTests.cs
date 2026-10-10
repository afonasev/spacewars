using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class PostMatchResultsUiTests
{
    private GameObject host;private PanelSettings panel;private PostMatchResultsView ui;private NativeMenuNavigation navigation;
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    private static MatchResult Match()
    {
        var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        var profile=PlayableProfile.Default;var domain=Activator.CreateInstance(type,F,null,new object[]{profile,1L,false},null);
        object Call(string name,params object[] args)=>type.GetMethod(name,F).Invoke(domain,args);
        Call("BindMatchSeed",19092026);Call("RecordHumanAction","player-1");
        Call("Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.BuildAt,Array.Empty<int>(),siteId:1,slotId:1,buildingKind:PlayableBuildingKind.Factory,parentId:1,origin:PlayableOrderOrigin.Human),null);
        for(int i=0;i<600;i++)Call("Step",1d/30d);
        var snapshot=(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026);
        int factory=snapshot.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;
        Call("Apply",new PlayableCommand(1,2,"player-1",PlayableCommandKind.ToggleRepeatProduction,new[]{factory},origin:PlayableOrderOrigin.Human),null);
        for(int i=0;i<1800;i++)Call("Step",1d/30d);
        var loopType=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop",true);
        var opening=PlayableAiOpeningComposition.Initialize(19092026,"enemy-1");var loop=Activator.CreateInstance(loopType,F,null,new object[]{profile,opening,1L,null,Spacewars.Simulation.Ai.AiProfile.Initial,Spacewars.Simulation.Ai.AiDifficulty.Fighter},null);
        var enemy=Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,PlayableOwner.Enemy);loopType.GetMethod("Review",F).Invoke(loop,new object[]{enemy,0L});Call("RecordAiState",loopType.GetProperty("Checkpoint",F).GetValue(loop));
        Call("FinishManually");return (MatchResult)type.GetProperty("Result",F).GetValue(domain);
    }
    [SetUp]public void Setup()
    {
        host=new GameObject("post-match native UI fixture");var document=host.AddComponent<UIDocument>();panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;document.panelSettings=panel;
        var root=document.rootVisualElement;root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");OrbitalTheme.Install(root);
        ui=new PostMatchResultsView(Match(),()=>{},()=>{},()=>{});root.Add(ui.Root);navigation=new NativeMenuNavigation(root);navigation.SetScope(ui.Root,null,ui.FirstTab);
    }
    [TearDown]public void Cleanup(){navigation?.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);}
    [Test]public void TabsActionsFiltersAndIndependentSelectorsDoNotMutateSnapshot()
    {
        var result=ui.Result;int facts=result.Facts.Count;
        Assert.That(ui.Root.Q("result-title").ToString(),Is.Not.Null);Assert.AreEqual(2,ui.Root.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("result-player-")));
        var maximumCells=ui.Root.Query<Label>().ToList().Where(label=>label.text.Contains("★")).ToArray();Assert.IsNotEmpty(maximumCells);Assert.IsTrue(maximumCells.All(label=>label.text.StartsWith("★ ")));
        Assert.IsNotNull(ui.Root.Q<Button>("result-overview"));Assert.IsNotNull(ui.Root.Q<Button>("result-menu"));Assert.IsNotNull(ui.Root.Q<Button>("result-repeat"));Assert.IsFalse(ui.Root.Query<Button>().ToList().Any(b=>b.text.ToLowerInvariant().Contains("replay")));
        ui.Root.Q<Button>("result-tab-1").Focus();navigation.Activate();Assert.AreEqual(1,ui.SelectedTab);
        var filter=ui.Root.Q<Toggle>("result-filter-0");Assert.AreEqual(Color.clear,filter.style.backgroundColor.value);Assert.AreEqual(0,filter.style.borderLeftWidth.value);Assert.IsTrue(ui.Root.Query<Toggle>().ToList().All(toggle=>!((toggle.text??"").Contains("━")||(toggle.text??"").Contains("●")||(toggle.text??"").Contains("■"))));filter.Focus();navigation.Activate();Assert.IsFalse(filter.value);ui.InspectTime(10);Assert.IsFalse(ui.Root.Q<Label>("result-graph-values").text.Contains("Игрок 1"));
        filter.value=true;ui.InspectTime(0);Assert.That(ui.Root.Q<Label>("result-graph-values").text,Does.Contain("0 кр/с"));
        ui.ShowTab(2);var left=ui.Root.Q<DropdownField>("result-log-player-0");var right=ui.Root.Q<DropdownField>("result-log-player-1");Assert.AreEqual(0,left.index);Assert.AreEqual(1,right.index);
        Assert.AreEqual(DisplayStyle.None,ui.Root.Q<Toggle>("result-ai-log-0").style.display.value);Assert.AreEqual(DisplayStyle.Flex,ui.Root.Q<Toggle>("result-ai-log-1").style.display.value);ui.Root.Q<Toggle>("result-ai-log-1").value=true;Assert.That(ui.Root.Query<Label>().ToList().Any(l=>l.text.StartsWith("01:20")&&l.text.Contains("Стартовый план:")));Assert.IsFalse(ui.Root.Q<Toggle>("result-ai-log-0").value);
        right.index=0;Assert.AreEqual(0,ui.Root.Q<DropdownField>("result-log-player-0").index);Assert.AreEqual(0,ui.Root.Q<DropdownField>("result-log-player-1").index);Assert.AreEqual(facts,result.Facts.Count);
    }
    [Test]public void ApprovedRollingApmDefaultsToHumanLinesAndSharesPlayerFilter()
    {
        var candidate=new PostMatchResultsView(ui.Result,()=>{},()=>{},()=>{});ui.Root.RemoveFromHierarchy();host.GetComponent<UIDocument>().rootVisualElement.Add(candidate.Root);candidate.ShowTab(1);Assert.IsNotNull(candidate.Root.Q("result-graph-2"));candidate.InspectTime(20);Assert.That(candidate.Root.Q<Label>("result-graph-values").text,Does.Contain("9 APM"));Assert.AreEqual(1,candidate.Root.Q<Label>("result-graph-values").text.Split(new[]{"APM"},StringSplitOptions.None).Length-1);candidate.Root.Q<Toggle>("result-filter-0").value=false;candidate.InspectTime(20);Assert.IsFalse(candidate.Root.Q<Label>("result-graph-values").text.Contains("Игрок 1"));Assert.That(ui.Result.Facts.Count(f=>f.Kind==MatchFactKind.HumanAction),Is.EqualTo(3));
    }
    [UnityTest]public IEnumerator CompactEightSlotTableScrollsWithoutVisibleScrollbars()
    {
        var profile=PlayableProfile.Default;var basis=SourceFlatFixture.Load(Resources.Load<TextAsset>("OfflineFlatFixture").text,profile);var roster=Enumerable.Range(1,8).Select(i=>new OfflineParticipant("slot-"+i,i,i,i<=4?OfflineControl.Human:OfflineControl.Ai)).ToArray();var costs=new double[8,8];for(int a=0;a<8;a++)for(int b=0;b<8;b++)costs[a,b]=basis.RouteCost(a,b);
        var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,roster,basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs);var authority=new OfflineParticipantAuthority(config,1);authority.FinishManually();
        ui.Root.RemoveFromHierarchy();ui=new PostMatchResultsView(authority.Result,()=>{},()=>{},()=>{});host.GetComponent<UIDocument>().rootVisualElement.Add(ui.Root);ui.Root.style.width=640;ui.Root.style.height=480;navigation.SetScope(ui.Root,null,ui.FirstTab);for(int i=0;i<4;i++)yield return null;
        Assert.AreEqual(8,ui.Root.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("result-player-")));var scroll=ui.Root.Q<ScrollView>();Assert.Greater(scroll.horizontalScroller.highValue,0);Assert.Greater(scroll.verticalScroller.highValue,0);Assert.AreEqual(ScrollerVisibility.Hidden,scroll.horizontalScrollerVisibility);Assert.AreEqual(ScrollerVisibility.Hidden,scroll.verticalScrollerVisibility);
        using(var wheel=WheelEvent.GetPooled(new Event{type=EventType.ScrollWheel,delta=new Vector2(10,10),mousePosition=scroll.contentViewport.worldBound.center})){wheel.target=scroll.contentViewport;scroll.contentViewport.SendEvent(wheel);}
        yield return null;Assert.Greater(scroll.scrollOffset.x,0);Assert.Greater(scroll.scrollOffset.y,0);
    }
    [Test]public void ExistingLabRevisionsReceiveTheInitialScoreDivisor()
    {
        string directory=Path.Combine(Path.GetTempPath(),"spacewars-post-match-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try{var store=new NativeBalanceStore(PlayableProfile.Default,Path.Combine(directory,"balance.json"));var row=store.State["revisions"][0];((Newtonsoft.Json.Linq.JObject)row["data"]).Remove("matchScoreEarnedCreditsDivisor");Assert.AreEqual(2,store.Compile(row).MatchScoreEarnedCreditsDivisor);row["data"]["matchScoreEarnedCreditsDivisor"]=0;Assert.Throws<ArgumentException>(()=>store.Compile(row));}
        finally{Directory.Delete(directory,true);}
    }
    [UnityTest]public IEnumerator ConfirmedFinishUsesRuntimePublicationAndOverviewCanReopen()
    {
        var bootstrapHost=new GameObject("authoritative finish UI integration");var hud=bootstrapHost.AddComponent<PlayableBootstrap>();hud.enabled=false;var type=typeof(PlayableBootstrap);
        void Set(string name,object value)=>type.GetField(name,F).SetValue(hud,value);
        T Get<T>(string name)=>(T)type.GetField(name,F).GetValue(hud);
        void Call(string name)=>type.GetMethod(name,F).Invoke(hud,null);
        Set("profile",PlayableProfile.Default);Call("CreateHud");var runtime=PlayableRuntime.CreateHumanMatch(PlayableProfile.Default,1,7);runtime.RequestPause(true);Set("runtime",runtime);Set("paused",true);Set("view",runtime.Latest);Call("UpdateHud");
        var root=Get<VisualElement>("root");var nav=Get<NativeMenuNavigation>("menuNavigation");var finish=root.Q<Button>("pause-finish");finish.Focus();nav.Activate();Assert.AreEqual("Подтвердить завершение",finish.text);Assert.IsNull(runtime.Result);finish.Focus();nav.Activate();
        float deadline=Time.realtimeSinceStartup+5;while(runtime.Result==null&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.IsNotNull(runtime.Result);Assert.IsNull(runtime.Result.WinnerTeam);Assert.IsTrue(runtime.Result.Manual);var frozen=runtime.Result;
        Set("view",runtime.Latest);Call("UpdateHud");Assert.IsNotNull(root.Q("post-match-results"));Get<PostMatchResultsView>("postMatch").ShowTab(1);Assert.IsNotNull(root.Q("result-graph-2"));root.Q<Button>("result-overview").Focus();nav.Activate();Assert.AreEqual(DisplayStyle.None,root.Q("post-match-results").style.display.value);
        Call("UpdateHud");root.Q<Button>("result-reopen").Focus();using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=root.Q<Button>("result-reopen");root.Q<Button>("result-reopen").SendEvent(submit);}Assert.AreEqual(DisplayStyle.Flex,root.Q("post-match-results").style.display.value);Assert.AreSame(frozen,runtime.Result);
        runtime.RequestStop();while(!runtime.IsStopped)yield return null;UnityEngine.Object.DestroyImmediate(bootstrapHost);
    }
    [UnityTest]public IEnumerator TwoLocalPauseConfirmsAndReopensTheSameResult()
    {
        var localHost=new GameObject("two local results lifecycle");var bootstrap=localHost.AddComponent<OfflineTwoLocalBootstrap>();bootstrap.enabled=false;var type=typeof(OfflineTwoLocalBootstrap);
        void Set(string name,object value)=>type.GetField(name,F).SetValue(bootstrap,value);
        T Get<T>(string name)=>(T)type.GetField(name,F).GetValue(bootstrap);
        void Call(string name)=>type.GetMethod(name,F).Invoke(bootstrap,null);
        var config=SourceFlatFixture.Load(Resources.Load<TextAsset>("OfflineFlatFixture").text,PlayableProfile.Default,false);var runtime=new PlayableRuntime(config,1,startPaused:true);
        var session=new OfflineLocalSession(runtime,new[]{OfflineLocalSession.Bind("local-keyboard","owner-11","keyboard+mouse:1:2"),OfflineLocalSession.Bind("local-gamepad","owner-28","gamepad:3")});
        var doc=localHost.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;OrbitalTheme.Install(root);var lobby=new VisualElement();root.Add(lobby);Set("root",root);Set("lobby",lobby);Set("runtime",runtime);Set("session",session);Set("ownerPaint",PostMatchResultsView.Palette);var keyboard=localHost.AddComponent<Spacewars.Input.PlayableInput>();keyboard.enabled=false;Set("keyboard",keyboard);
        var ringType=typeof(OfflineTwoLocalBootstrap).Assembly.GetType("Spacewars.Presentation.OfflinePadRing",true);Set("padRing",Activator.CreateInstance(ringType,true));Call("CreateOfflineResultActions");Call("ConfirmOfflineFinish");Assert.IsNull(runtime.Result);Call("ConfirmOfflineFinish");
        float deadline=Time.realtimeSinceStartup+5;while(runtime.Result==null&&Time.realtimeSinceStartup<deadline)yield return null;Assert.IsNotNull(runtime.Result);Assert.IsNull(runtime.Result.WinnerTeam);Call("UpdateOfflineResult");var frozen=runtime.Result;var results=Get<PostMatchResultsView>("offlineResult");Assert.AreEqual(config.Roster.Count,results.Result.Players.Count);results.ShowTab(1);Assert.IsNotNull(results.Root.Q("result-graph-2"));Call("OverviewOfflineResult");Assert.AreEqual(DisplayStyle.None,results.Root.style.display.value);Call("ShowOfflineResult");Assert.AreEqual(DisplayStyle.Flex,results.Root.style.display.value);Assert.AreSame(frozen,runtime.Result);
        runtime.RequestStop();while(!runtime.IsStopped)yield return null;UnityEngine.Object.DestroyImmediate(localHost);
    }
    [UnityTest]public IEnumerator AdmittedSelectionSurvivesPauseBoundary()
    {
        var runtime=PlayableRuntime.CreateHumanMatch(PlayableProfile.Default,1,7);for(int i=0;i<1000;i++)runtime.RecordHumanAction("player-1");runtime.RequestPause(true);runtime.RequestFinish();float deadline=Time.realtimeSinceStartup+5;
        while(runtime.Result==null&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.IsNotNull(runtime.Result);Assert.AreEqual(1000,runtime.Result.Facts.Count(f=>f.Kind==MatchFactKind.HumanAction));runtime.RequestStop();while(!runtime.IsStopped)yield return null;
    }
    [UnityTest]public IEnumerator AdmittedCommandsCountOnceAcrossPauseAndUnservicedRouteBarrier()
    {
        var runtime=PlayableRuntime.CreateHumanMatch(PlayableProfile.Default,1,7);
        try{
            var unit=runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player);Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{unit.Id},target:new NavPoint(PlayableProfile.Default.ArenaHalfExtent-5,PlayableProfile.Default.ArenaHalfExtent-5))).Status);
            float deadline=Time.realtimeSinceStartup+5;bool applied=false;
            while(!applied&&Time.realtimeSinceStartup<deadline){applied=runtime.DrainReceipts().Any(r=>r.Sequence==1);if(!applied)yield return null;}
            Assert.IsTrue(applied,"First admitted intent reaches the authority before the pause boundary.");
            var tick=typeof(PlayableRuntime).GetField("authorityTick",F).GetValue(runtime);var awaiting=tick.GetType().GetProperty("AwaitingRoutes",F|BindingFlags.Public);deadline=Time.realtimeSinceStartup+5;while(!(bool)awaiting.GetValue(tick)&&Time.realtimeSinceStartup<deadline)yield return null;Assert.IsTrue((bool)awaiting.GetValue(tick),"The unserviced route keeps later command application behind a deterministic barrier.");
            var gate=typeof(PlayableRuntime).GetField("gate",F).GetValue(runtime);
            lock(gate){Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(1,2,"player-1",PlayableCommandKind.Hold,Array.Empty<int>())).Status);runtime.RequestPause(true);runtime.RequestFinish();}
            deadline=Time.realtimeSinceStartup+5;while(runtime.Result==null&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.IsNotNull(runtime.Result);Assert.AreEqual(2,runtime.Result.Facts.Count(f=>f.Kind==MatchFactKind.HumanAction));
            Assert.IsTrue(runtime.Result.Manual);
        }finally{runtime.RequestStop();}
        while(!runtime.IsStopped)yield return null;
    }
    [UnityTest]public IEnumerator CompactEightSlotResultsAndGraphsFit720p()
    {
        var profile=PlayableProfile.Default;var basis=SourceFlatFixture.Load(Resources.Load<TextAsset>("OfflineFlatFixture").text,profile);var roster=Enumerable.Range(1,8).Select(i=>new OfflineParticipant("slot-"+i,i,i,i<=4?OfflineControl.Human:OfflineControl.Ai)).ToArray();var costs=new double[8,8];for(int a=0;a<8;a++)for(int b=0;b<8;b++)costs[a,b]=basis.RouteCost(a,b);
        var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,roster,basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs);var authority=new OfflineParticipantAuthority(config,1);authority.FinishManually();
        ui.Root.RemoveFromHierarchy();ui=new PostMatchResultsView(authority.Result,()=>{},()=>{},()=>{});host.GetComponent<UIDocument>().rootVisualElement.Add(ui.Root);navigation.SetScope(ui.Root,null,ui.FirstTab);
        var target=new RenderTexture(1280,720,24);target.Create();panel.targetTexture=target;
        try{
            for(int tab=0;tab<2;tab++){
                ui.ShowTab(tab);for(int i=0;i<8;i++)yield return null;
                if(tab==0){Assert.AreEqual(8,ui.Root.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("result-player-")));var scroll=ui.Root.Q<ScrollView>();Assert.LessOrEqual(scroll.verticalScroller.highValue,.5f);Assert.LessOrEqual(scroll.horizontalScroller.highValue,.5f);}
                else{Assert.AreEqual(8,ui.Root.Query<Toggle>().ToList().Count);AssertGraphLayoutFits();}
                SaveCapture(target,"eight-slot-"+(tab==0?"results":"graphs")+".png");
            }
        }finally{panel.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);}
    }
    private void AssertGraphLayoutFits()
    {
        Assert.IsNull(ui.Root.Q("result-summary"));Assert.IsNull(ui.Root.Q<ScrollView>(),"The graph screen has no scrolling containers.");
        var content=ui.Root.Q("result-content");var values=ui.Root.Q<Label>("result-graph-values");var actions=ui.Root.Q("result-actions");
        for(int metric=0;metric<3;metric++){
            var block=ui.Root.Q("result-graph-block-"+metric);var chart=ui.Root.Q("result-graph-"+metric);
            Assert.Greater(chart.worldBound.height,40);Assert.GreaterOrEqual(block.worldBound.yMin,content.worldBound.yMin-.5f);Assert.LessOrEqual(block.worldBound.yMax,values.worldBound.yMin+.5f);
        }
        Assert.LessOrEqual(values.worldBound.yMax,actions.worldBound.yMin+.5f);
        var first=ui.Root.Q<Button>("result-overview");var last=ui.Root.Q<Button>("result-repeat");Assert.That(first.worldBound.height,Is.EqualTo(32).Within(.5f));Assert.That(last.worldBound.width,Is.EqualTo(132).Within(.5f));Assert.Greater(first.worldBound.xMin,ui.Root.worldBound.center.x);Assert.That(last.worldBound.xMax,Is.EqualTo(ui.Root.worldBound.xMax-ui.Root.resolvedStyle.paddingRight).Within(.5f));Assert.LessOrEqual(actions.worldBound.yMax,ui.Root.worldBound.yMax-ui.Root.resolvedStyle.paddingBottom+.5f);
    }
    private static void SaveCapture(RenderTexture target,string name)
    {
        var output=Environment.GetEnvironmentVariable("SPACEWARS_POST_MATCH_EVIDENCE");if(string.IsNullOrEmpty(output))return;Directory.CreateDirectory(output);
        Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType,"Screenshot evidence requires the Editor graphics backend; omit -nographics.");
        var previous=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();Assert.Greater(texture.GetPixels32().Where((_,i)=>i%173==0).Select(c=>(int)c.r<<16|(int)c.g<<8|c.b).Distinct().Count(),8,"The captured UI must contain rendered content rather than a blank render texture.");File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());}finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(texture);}
    }
    [UnityTest]public IEnumerator CaptureNativeResultsTabs()
    {
        foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1440,900)}){
            var target=new RenderTexture(size.x,size.y,24);target.Create();panel.targetTexture=target;panel.referenceResolution=size;
            try{
                for(int tab=0;tab<3;tab++){
                    ui.ShowTab(tab);if(tab==1)ui.InspectTime(20);if(tab==2)ui.Root.Q<Toggle>("result-ai-log-1").value=true;for(int i=0;i<8;i++)yield return null;
                    Assert.IsNull(ui.Root.Q("result-summary"));if(tab==1)AssertGraphLayoutFits();
                    SaveCapture(target,new[]{"results","graphs","action-log"}[tab]+"-"+size.x+"x"+size.y+".png");
                }
            }finally{panel.targetTexture=null;target.Release();UnityEngine.Object.Destroy(target);}
        }
    }
}

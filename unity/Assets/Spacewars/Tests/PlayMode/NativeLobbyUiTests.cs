using System;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation.Ai;
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
        Call("CreateWorld");Call("CreateHud");Call("CreateSpectatorHud");
        typeof(PlayableBootstrap).GetField("input",Flags).SetValue(app,host.AddComponent<Spacewars.Input.PlayableInput>());
        Call("CreateLobby");
    }
    [TearDown] public void Teardown(){foreach(var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(camera.gameObject);foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(light.gameObject);UnityEngine.Object.DestroyImmediate(host);}
    [UnityTest] public IEnumerator MapAtmosphereMatchesTerrainAndFitsBelowPreview()
    {
        Call("ShowLobby");var root=Get<VisualElement>("root");var lobby=Get<VisualElement>("lobbyScreen");
        var choice=lobby.Q<DropdownField>("lobby-map-choice");CollectionAssert.AreEqual(new[]{"Переправа","Огненный разлом","ИИ-полигон · 8"},choice.choices);
        var panel=host.GetComponent<UIDocument>().panelSettings;
        foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(1600,1000)})
        {
            var image=new RenderTexture(size.x,size.y,24);image.Create();panel.targetTexture=image;root.style.width=1280;root.style.height=800;
            try
            {
                for(int index=0;index<2;index++)
                {
                    choice.value=choice.choices[index];for(int frame=0;frame<8;frame++)yield return null;
                    Assert.AreEqual(index==1,Get<NativeLobbyConfiguration>("lobbySetup").Foundry);
                    Assert.True(index==1?Get<PlayableProfile>("profile").AuthoredMap is FoundryMap:Get<PlayableProfile>("profile").AuthoredMap is ThreeCrossingsMap);
                    Assert.AreEqual(index==1?"6 игровых мест":"2 игровых места",lobby.Q<Label>("lobby-map-capacity").text);
                    var description=lobby.Q<Label>("lobby-map-description");
                    Assert.AreEqual(index==1?"Над остывшими промышленными площадками дрожит жар лавовой реки. Между шахтами и укреплёнными высотами тишина держится лишь до первых выстрелов.":"Холодная река рассекает каменное плато, оставляя лишь три пути на другой берег. Среди скал и высоких уступов каждый проход становится рубежом, который нельзя отдать.",description.text);
                    Assert.AreEqual(DisplayStyle.Flex,description.resolvedStyle.display);
                    Assert.GreaterOrEqual(description.worldBound.yMin,Get<PlayableMapSurface>("lobbyPreview").worldBound.yMax);
                    Assert.LessOrEqual(description.worldBound.yMax,Get<VisualElement>("mapPanel").worldBound.yMax);
                    Assert.LessOrEqual(description.worldBound.xMax,Get<VisualElement>("mapPanel").worldBound.xMax);
                    Assert.LessOrEqual(Get<VisualElement>("mapPanel").worldBound.xMax,root.worldBound.xMax);
                    string output=Environment.GetEnvironmentVariable("MAP_COPY_EVIDENCE");
                    if(!string.IsNullOrEmpty(output))
                    {
                        var prior=RenderTexture.active;RenderTexture.active=image;var png=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,size.x,size.y),0,0);png.Apply();
                        System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,(index==1?"foundry":"crossings")+"-"+size.x+"x"+size.y+".png"),png.EncodeToPNG());RenderTexture.active=prior;UnityEngine.Object.Destroy(png);
                    }
                }
            }
            finally{panel.targetTexture=null;image.Release();UnityEngine.Object.Destroy(image);}
        }
    }
    [Test] public void LobbyIsRealSetupAndHasNoAuthority()
    {
        Call("ShowLobby");
        Assert.IsNull(Get<Spacewars.Runtime.PlayableRuntime>("runtime"));
        Assert.False(Get<Spacewars.Input.PlayableInput>("input").WorldInputEnabled);
        var lobby=Get<VisualElement>("lobbyScreen");
        Assert.AreEqual(2,lobby.Query<TextField>().ToList().Count);
        Assert.AreEqual(7,lobby.Query<DropdownField>().ToList().Count);
        var launch=lobby.Q<Button>("lobby-launch");Assert.IsNotNull(launch);StringAssert.Contains("Start",launch.Children().OfType<Label>().Single().text);
        Assert.AreEqual(2,launch.style.borderLeftWidth.value);Assert.AreEqual(FontStyle.Bold,launch.style.unityFontStyleAndWeight.value);
        Assert.Zero(lobby.Query<Label>().ToList().Count(label=>label.text=="Настройки"));
        Assert.IsNotNull(lobby.Q<Label>("lobby-title"));
        Assert.Zero(lobby.Query<Label>().ToList().Count(label=>label.text=="SPACEWARS\nЛОКАЛЬНАЯ ИГРА"||label.text=="Выберите участников и карту"));
        var remove=lobby.Q<Button>("lobby-remove-0");Assert.AreEqual(Color.clear,remove.style.backgroundColor.value);Assert.AreEqual(0,remove.style.borderLeftWidth.value);
        var difficulty=lobby.Q<DropdownField>("lobby-ai-difficulty");Assert.IsNotNull(difficulty);
        difficulty.value="Ветеран";
        Assert.AreEqual(Spacewars.Simulation.Ai.AiDifficulty.Veteran,Get<NativeLobbyConfiguration>("lobbySetup").Difficulty);
        Assert.AreEqual("ИИ Ветеран 1",Get<NativeLobbyConfiguration>("lobbySetup").MatchAiName);
        Call("RebuildRoster");Assert.AreEqual("Ветеран",lobby.Q<DropdownField>("lobby-ai-difficulty").value);
        Assert.Greater(Get<PlayableMapTerrain>("lobbyTerrain").Texture.GetPixels32().Distinct().Count(),2,"Public terrain must be uploaded before first lobby frame");
        lobby.Q<TextField>("lobby-human-name").value=" ";
        Assert.False(lobby.Q<Button>("lobby-launch").enabledSelf);
        StringAssert.Contains("имена",Get<Label>("lobbyStatus").text);
        Assert.AreEqual(DisplayStyle.None,Get<VisualElement>("top").style.display.value);
    }
    [Test] public void MouseJoinAddsKeyboardMouseSeatOnlyOnce()
    {
        Call("ShowLobby");
        var setup=Get<NativeLobbyConfiguration>("lobbySetup");
        setup.Participants.RemoveAt(1);setup.Participants[0].Human=false;
        Call("RebuildRoster");

        Call("JoinLobbyWithKeyboardMouse");
        Assert.AreEqual(2,setup.Participants.Count);
        var keyboardMouse=setup.Participants.Last();
        Assert.True(keyboardMouse.Human);Assert.AreEqual(0,keyboardMouse.DeviceId);

        Call("JoinLobbyWithKeyboardMouse");
        Assert.AreEqual(1,setup.Participants.Count(participant=>participant.Human&&participant.DeviceId==0));
    }
    [UnityTest] public IEnumerator ControllerGlyphsCentreLettersAndStartLaunchesOnlyReadyLobby()
    {
        Call("ShowLobby");var root=Get<VisualElement>("root");root.style.width=1280;root.style.height=800;
        yield return null;yield return null;
        var lobby=Get<VisualElement>("lobbyScreen");
        foreach(var glyph in lobby.Query<NativeControllerGlyph>().ToList())
        {
            var label=glyph.Q<Label>("controller-glyph-symbol");Assert.IsNotNull(label);
            Assert.LessOrEqual(Vector2.Distance(label.worldBound.center,glyph.worldBound.center),.5f,glyph.name);
        }
        var navigation=Get<NativeMenuNavigation>("menuNavigation");
        var name=lobby.Q<TextField>("lobby-human-name");name.Focus();Assert.True(navigation.Start(null));Assert.False(Get<bool>("preparing"),"Start must not launch while a participant name is being edited.");
        name.value=" ";lobby.Q<Button>("lobby-launch").Focus();Assert.True(navigation.Start(null));Assert.False(Get<bool>("preparing"),"Start must not launch an invalid roster.");
        name.value="Игрок 1";
        lobby.Q<Button>("lobby-launch").Focus();Assert.True(navigation.Start(null));Assert.True(Get<bool>("preparing"),"Start launches a ready lobby without requiring button focus state.");
    }
    [UnityTest] public IEnumerator LobbyDropdownsFitTheirViewportWithoutScrollbarControls()
    {
        Call("ShowLobby");Get<NativeMenuNavigation>("menuNavigation").SetScope(null); // Exercise the native pointer-style popup, outside controller field cycling.
        var root=Get<VisualElement>("root");var panel=host.GetComponent<UIDocument>().panelSettings;
        foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(800,620),new Vector2Int(760,900)})
        {
            var image=new RenderTexture(size.x,size.y,24);image.Create();panel.targetTexture=image;root.style.width=size.x;root.style.height=size.y;
            try
            {
                for(int i=0;i<5;i++)yield return null;
                var lobby=Get<VisualElement>("lobbyScreen");
                var roster=lobby.Q<ScrollView>();Assert.AreEqual(ScrollerVisibility.Hidden,roster.horizontalScrollerVisibility);Assert.AreEqual(ScrollerVisibility.Hidden,roster.verticalScrollerVisibility);
                string output=Environment.GetEnvironmentVariable("LOBBY_LAYOUT_EVIDENCE");
                if(size.x==1280&&!string.IsNullOrEmpty(output))
                {
                    var prior=RenderTexture.active;RenderTexture.active=image;var png=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
                    png.ReadPixels(new Rect(0,0,size.x,size.y),0,0);png.Apply();System.IO.Directory.CreateDirectory(output);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"start-launch-"+size.x+"x"+size.y+".png"),png.EncodeToPNG());RenderTexture.active=prior;UnityEngine.Object.Destroy(png);
                }
                foreach(var field in lobby.Query<DropdownField>().ToList())
                {
                    field.Focus();using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=field;field.SendEvent(submit);}
                    for(int i=0;i<5;i++)yield return null;
                    var popup=root.panel.visualTree.Q(className:"unity-base-dropdown");Assert.IsNotNull(popup,field.name);
                    var scroll=popup.Q<ScrollView>();Assert.IsNotNull(scroll);
                    Assert.LessOrEqual(scroll.horizontalScroller.worldBound.height,.5f,field.name);
                    Assert.LessOrEqual(scroll.verticalScroller.worldBound.width,.5f,field.name);
                    foreach(var label in popup.Query<Label>(className:"unity-base-dropdown__label").ToList())
                    {
                        Assert.Greater(label.worldBound.height,0,field.name);
                        Assert.LessOrEqual(label.worldBound.xMax,scroll.contentViewport.worldBound.xMax+.5f,field.name+" "+label.text);
                        Assert.GreaterOrEqual(label.worldBound.xMin,scroll.contentViewport.worldBound.xMin-.5f,field.name);
                    }
                    if(field.name=="lobby-map-choice")
                    {
                        if(!string.IsNullOrEmpty(output))
                        {
                            var prior=RenderTexture.active;RenderTexture.active=image;var png=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
                            png.ReadPixels(new Rect(0,0,size.x,size.y),0,0);png.Apply();System.IO.Directory.CreateDirectory(output);
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,"map-dropdown-"+size.x+"x"+size.y+".png"),png.EncodeToPNG());RenderTexture.active=prior;UnityEngine.Object.Destroy(png);
                        }
                    }
                    using(var cancel=NavigationCancelEvent.GetPooled()){cancel.target=scroll.contentContainer;scroll.contentContainer.SendEvent(cancel);}
                    yield return null;Assert.IsNull(root.panel.visualTree.Q(className:"unity-base-dropdown"));
                }
            }
            finally{panel.targetTexture=null;image.Release();UnityEngine.Object.Destroy(image);}
        }
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
    [UnityTest] public IEnumerator ParticipantInputsShareOneBaseline()
    {
        Call("ShowLobby");var root=Get<VisualElement>("root");root.style.width=1280;root.style.height=800;
        yield return null;yield return null;
        var lobby=Get<VisualElement>("lobbyScreen");
        foreach(var row in new[]{lobby.Q<VisualElement>("lobby-participant-0"),lobby.Q<VisualElement>("lobby-participant-1")})
        {
            var fields=row.Query<VisualElement>(className:"unity-base-field").ToList();Assert.GreaterOrEqual(fields.Count,3);
            float y=fields[0].Q(className:"unity-base-field__input").worldBound.yMin;
            foreach(var field in fields)Assert.AreEqual(y,field.Q(className:"unity-base-field__input").worldBound.yMin,.5f,row.name);
        }
    }
    [UnityTest] public IEnumerator StandardAndCompactWindowSettingsDoNotOverlapDeviceAssignment()
    {
        Call("ShowLobby");var root=Get<VisualElement>("root");foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(800,620)})
        {root.style.width=size.x;root.style.height=size.y;
        yield return null;yield return null;
        var lobby=Get<VisualElement>("lobbyScreen");var bind=lobby.Q<Button>("lobby-join");
        foreach(var field in lobby.Query<DropdownField>().ToList().Take(2))
            Assert.LessOrEqual(field.worldBound.yMax,bind.worldBound.yMin+.5f,"Settings overlap device assignment");
        Assert.LessOrEqual(Get<VisualElement>("rosterPanel").worldBound.yMax,Get<Label>("lobbyStatus").worldBound.yMin+.5f);
        Assert.LessOrEqual(lobby.Q<Label>("lobby-mode-reason").worldBound.yMax,Get<VisualElement>("rosterPanel").worldBound.yMax+.5f,"Mode explanation must fit before footer, not overlap launch status.");
        Assert.GreaterOrEqual(lobby.Q<Label>("lobby-mode-reason").worldBound.yMin,bind.worldBound.yMax-.5f,"Mode explanation must not overlap the participant toolbar");
        }
    }
    [UnityTest] public IEnumerator InstalledMenuOpensLobbyAndRetainsUpdateOnReturn()
    {
        typeof(PlayableBootstrap).GetField("nativeMainMenuEnabled",Flags).SetValue(app,true);
        Call("ShowMainMenu");yield return null;
        var menu=Get<NativeMainMenu>("nativeMainMenu");
        try
        {
            Assert.IsNull(Get<Spacewars.Runtime.PlayableRuntime>("runtime"));
            Assert.IsNotNull(menu.ScreenRoot.Query<Button>().ToList().Single(b=>b.text=="Обновить"));
            yield return (IEnumerator)typeof(NativeMainMenu).GetMethod("EnterGame",Flags).Invoke(menu,null);
            yield return null;
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("lobbyScreen").style.display.value);
            Assert.IsNull(Get<Spacewars.Runtime.PlayableRuntime>("runtime"));
            Call("ShowMainMenu");yield return null;
            menu=Get<NativeMainMenu>("nativeMainMenu");
            Assert.IsNotNull(menu.ScreenRoot.Query<Button>().ToList().Single(b=>b.text=="Обновить"));
        }
        finally{if(menu!=null)UnityEngine.Object.DestroyImmediate(menu.gameObject);}
    }
    // Diagnostic terminal trigger is explicit; this tests the real session/result/restart
    // pipeline in PlayMode, not Player combat/visual/device acceptance.
    [UnityTest] public IEnumerator OrdinarySessionBindsDifficultySeedGenesisResultAndRestart()
    {
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(app,value);
        IEnumerator Wait(Func<bool> ready)
        {float deadline=Time.realtimeSinceStartup+8;while(!ready()&&Time.realtimeSinceStartup<deadline)yield return null;Assert.True(ready(),"ordinary runtime deadline");}
        var domainType=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        Set("preparing",true);Set("inLobby",true);
        foreach(var difficulty in new[]{AiDifficulty.Recruit,AiDifficulty.Fighter,AiDifficulty.Veteran})
        {
            var draft=Get<NativeLobbyConfiguration>("lobbySetup");draft.HasExplicitSeed=true;draft.ExplicitSeed=31007+(int)difficulty;
            Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-ai-difficulty").value=NativeLobbyConfiguration.DifficultyNames[(int)difficulty];
            Assert.AreEqual(difficulty,draft.Difficulty);Set("matchSetup",draft.Copy());Call("StartSession");
            var runtime=Get<PlayableRuntime>("runtime");yield return Wait(()=>runtime.Latest.Paused);
            void Genesis(PlayableRuntime r)
            {
                Assert.AreEqual(draft.ExplicitSeed,r.Latest.Seed);Assert.AreEqual(difficulty,r.EnemyAiConfig.Difficulty);Assert.AreEqual(0,r.Latest.Tick);
                var domain=typeof(PlayableRuntime).GetField("domain",Flags).GetValue(r);
                foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})
                {
                    var view=(PlayableSnapshot)domainType.GetMethod("PlayerSnapshot",Flags).Invoke(domain,new object[]{0L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,draft.ExplicitSeed,owner});
                    Assert.AreEqual(1,view.Entities.Count(e=>e.Owner==owner));Assert.AreEqual(PlayableEntityKind.Explorer,view.Entities.Single(e=>e.Owner==owner).Kind);
                    Assert.AreEqual(PlayableBuildingKind.Headquarters,view.Buildings.Single(b=>b.Owner==owner).Kind);Assert.AreEqual(1,view.Buildings.Count(b=>b.Owner==owner));
                    Assert.AreEqual(PlayableProfile.ThreeCrossingsDefault.StartingCredits,view.Credits);
                }
            }
            Genesis(runtime);var generation=runtime.Generation;runtime.RequestPause(false);yield return Wait(()=>runtime.Latest.Tick>=5);Call("UpdateFrame");
            Assert.AreEqual(PlayableMatchOutcome.Playing,runtime.Latest.Outcome);Assert.AreEqual(0,runtime.Latest.Metrics.Errors);
            runtime.RequestPause(true);yield return Wait(()=>runtime.Latest.Paused);
            // Mutate only at the acknowledged pause barrier. Actual CheckOutcome and
            // bootstrap result UI are used; no synthetic result snapshot is supplied.
            var live=typeof(PlayableRuntime).GetField("domain",Flags).GetValue(runtime);
            var enemy=(PlayableSnapshot)domainType.GetMethod("PlayerSnapshot",Flags).Invoke(live,new object[]{0L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,draft.ExplicitSeed,PlayableOwner.Enemy});
            var hq=enemy.Buildings.Single(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Headquarters);
            domainType.GetMethod("Damage",Flags).Invoke(live,new object[]{hq.Id,100000});runtime.RequestPause(false);
            yield return Wait(()=>runtime.Latest.Outcome==PlayableMatchOutcome.TeamWon);Call("UpdateFrame");
            Assert.AreEqual("ПОБЕДА",Get<Label>("modalTitle").text);Assert.AreEqual(DisplayStyle.Flex,Get<PostMatchResultsView>("postMatch").Root.style.display.value);
            Call("Restart");yield return Wait(()=>runtime.IsStopped);Call("UpdateFrame");
            var next=Get<PlayableRuntime>("runtime");Assert.AreNotSame(runtime,next);yield return Wait(()=>next.Latest.Paused);
            Assert.Greater(next.Generation,generation);Genesis(next);Assert.AreEqual(PlayableMatchOutcome.Playing,next.Latest.Outcome);
            next.RequestStop();yield return Wait(()=>next.IsStopped);Set("runtime",null);
        }
    }
    private void SetFirstSeatAi()
    {
        var lobby=Get<VisualElement>("lobbyScreen");
        Get<NativeMenuNavigation>("menuNavigation").ActivateElement(lobby.Q<Button>("lobby-remove-0"));
        typeof(PlayableBootstrap).GetMethod("AddLobbyParticipant",Flags).Invoke(app,new object[]{false,null});
        var draft=Get<NativeLobbyConfiguration>("lobbySetup");draft.Participants[0].Team=1;draft.Participants[0].Difficulty=AiDifficulty.Veteran;draft.Participants[0].Name="ИИ Ветеран 1";draft.Participants[1].Team=2;draft.Participants[1].Name="ИИ Боец 2";
        Call("RebuildRoster");Call("RefreshLobbyValidation");
    }
    [UnityTest] public IEnumerator AllAiLobbyStartsViewerAndResetsOnHumanSession()
    {
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(app,value);
        IEnumerator Wait(Func<bool> ready)
        {float deadline=Time.realtimeSinceStartup+10;while(!ready()&&Time.realtimeSinceStartup<deadline)yield return null;Assert.True(ready(),"viewer runtime deadline");}
        Call("ShowLobby");var lobby=Get<VisualElement>("lobbyScreen");
        SetFirstSeatAi();
        var draft=Get<NativeLobbyConfiguration>("lobbySetup");Assert.True(draft.Spectator);
        Assert.IsNull(lobby.Q<Button>("lobby-bind-devices"));
        Assert.AreEqual("ИИ Ветеран 1",draft.ParticipantName(0));Assert.AreEqual("ИИ Боец 2",draft.ParticipantName(1));
        yield return CaptureViewerFrame("all-ai-lobby",false);
        Set("preparing",true);Set("matchSetup",draft.Copy());Call("StartSession");
        var runtime=Get<PlayableRuntime>("runtime");yield return Wait(()=>runtime.Latest.Paused);
        Assert.True(Get<bool>("spectatorMode"));Assert.False(Get<Spacewars.Input.PlayableInput>("input").CommandInputEnabled);
        var scheduler=typeof(PlayableRuntime).GetField("aiScheduler",Flags).GetValue(runtime);var owners=(System.Collections.IEnumerable)scheduler.GetType().GetField("owners",Flags).GetValue(scheduler);var first=owners.Cast<object>().Single(o=>(string)o.GetType().GetProperty("OwnerId",Flags).GetValue(o)=="player-1");var checkpoint=(PlayableAiOwnerCheckpoint)first.GetType().GetProperty("Checkpoint",Flags).GetValue(first);Assert.AreEqual(AiDifficulty.Veteran,checkpoint.Difficulty);
        Assert.AreEqual(2,runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.Count);
        Call("ReadPresentationFrame");Assert.AreEqual(2,Get<PlayableSnapshot>("view").Buildings.Count(b=>b.Kind==PlayableBuildingKind.Headquarters));
        Set("inLobby",false);Set("preparing",false);lobby.style.display=DisplayStyle.None;typeof(PlayableBootstrap).GetMethod("SetMatchUi",Flags).Invoke(app,new object[]{true});Call("UpdateFrame");
        yield return CaptureViewerFrame("all-ai-authored-match",true);
        Set("spectatorPerspective",(PlayableOwner?)PlayableOwner.Enemy);Set("spectatorCollapsed",true);
        Call("ReadPresentationFrame");Assert.AreEqual(PlayableOwner.Enemy,Get<PlayableSnapshot>("view").Owner);
        runtime.RequestPause(false);yield return Wait(()=>runtime.Latest.Tick>=3);Call("UpdateFrame");
        Assert.AreEqual(0,runtime.Latest.Metrics.Errors);
        runtime.RequestPause(true);yield return Wait(()=>runtime.Latest.Paused);
        var domain=typeof(PlayableRuntime).GetField("domain",Flags).GetValue(runtime);
        var enemyHq=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview.Buildings.Single(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Headquarters);
        domain.GetType().GetMethod("Damage",Flags).Invoke(domain,new object[]{enemyHq.Id,100000});runtime.RequestPause(false);
        yield return Wait(()=>runtime.Latest.Outcome==PlayableMatchOutcome.TeamWon);Call("UpdateFrame");
        Assert.NotNull(runtime.Result);Assert.AreEqual(1,runtime.Result.WinnerTeam);Assert.AreEqual(DisplayStyle.Flex,Get<PostMatchResultsView>("postMatch").Root.style.display.value);
        Set("preparing",true);Call("Restart");yield return Wait(()=>runtime.IsStopped);Call("UpdateFrame");
        runtime=Get<PlayableRuntime>("runtime");yield return Wait(()=>runtime.Latest.Paused);
        Assert.True(Get<bool>("spectatorMode"));Assert.IsNull(Get<PlayableOwner?>("spectatorPerspective"));Assert.False(Get<bool>("spectatorCollapsed"));
        runtime.RequestStop();yield return Wait(()=>runtime.IsStopped);Set("runtime",null);
        Call("ShowLobby");draft.Participants[0].Human=true;draft.Participants[0].DeviceId=0;Call("RebuildRoster");Call("RefreshLobbyValidation");
        Set("matchSetup",draft.Copy());Call("StartSession");runtime=Get<PlayableRuntime>("runtime");yield return Wait(()=>runtime.Latest.Paused);
        Assert.False(Get<bool>("spectatorMode"));Assert.True(Get<Spacewars.Input.PlayableInput>("input").CommandInputEnabled);
        runtime.RequestStop();yield return Wait(()=>runtime.IsStopped);Set("runtime",null);
    }
    [UnityTest] public IEnumerator EightPlayerTestMapSelectsAndRendersOneOpenTexturedGround()
    {
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(app,value);
        Call("ShowLobby");var lobby=Get<VisualElement>("lobbyScreen");
        lobby.Q<DropdownField>("lobby-map-choice").value="ИИ-полигон · 8";
        var setup=Get<NativeLobbyConfiguration>("lobbySetup");var profile=Get<PlayableProfile>("profile");
        Assert.True(setup.AiTestMap);Assert.False(setup.Foundry);Assert.True(setup.Spectator);
        Assert.AreEqual(8,setup.Participants.Count);Assert.AreEqual("8 игровых мест",lobby.Q<Label>("lobby-map-capacity").text);
        Assert.True(lobby.Q<Button>("lobby-launch").enabledSelf);Assert.True(profile.AuthoredMap is AiTestMap);
        Assert.AreEqual(1,Get<PlayableMapTerrain>("lobbyTerrain").Texture.GetPixels32().Distinct().Count());
        setup.HasExplicitSeed=true;setup.ExplicitSeed=7108;
        yield return CaptureViewerFrame("ai-eight-player-lobby",false);
        Set("preparing",true);Set("matchSetup",setup.Copy());Call("RecreateMapPresentation");Call("StartSession");
        var runtime=Get<PlayableRuntime>("runtime");
        try
        {
            float deadline=Time.realtimeSinceStartup+8;
            while(!runtime.Latest.Paused&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.True(runtime.Latest.Paused);Assert.True(Get<bool>("spectatorMode"));
            Assert.AreEqual(8,runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.Count);
            var ground=host.GetComponentsInChildren<Renderer>().Single(r=>r.name=="AI test open ground");
            var block=new MaterialPropertyBlock();ground.GetPropertyBlock(block);Assert.AreEqual(-1,block.GetFloat("_SurfaceRole"));
            Assert.AreSame(Resources.Load<Texture2D>("Environment/NaturalFrontier/Earth"),ground.sharedMaterial.GetTexture("_EarthTex"));
            Assert.Zero(host.GetComponentsInChildren<Transform>().Count(t=>t.name=="Boundary"||t.name=="Paving joint"||t.name=="Fortification"));
            Call("ReadPresentationFrame");Set("inLobby",false);Set("preparing",false);lobby.style.display=DisplayStyle.None;
            typeof(PlayableBootstrap).GetMethod("SetMatchUi",Flags).Invoke(app,new object[]{true});Call("UpdateFrame");
            var camera=Get<Camera>("cameraView");camera.orthographicSize=170;camera.transform.position=new Vector3(0,250,-100);camera.transform.LookAt(Vector3.zero);
            yield return CaptureViewerFrame("ai-eight-player-map",true);
            // Use the ordinary main-thread route host; the authority waits for its answers.
            runtime.RequestPause(false);deadline=Time.realtimeSinceStartup+8;
            while(runtime.Latest.Tick<90&&runtime.Latest.Failure==null&&Time.realtimeSinceStartup<deadline)
            {Call("ServiceRoutes");Call("UpdateFrame");yield return null;}
            Assert.GreaterOrEqual(runtime.Latest.Tick,90,runtime.Latest.Failure);
            runtime.RequestPause(true);deadline=Time.realtimeSinceStartup+3;
            while(!runtime.Latest.Paused&&Time.realtimeSinceStartup<deadline){Call("ServiceRoutes");yield return null;}
            Assert.True(runtime.Latest.Paused);Assert.IsNull(runtime.Latest.Failure);Assert.AreEqual(0,runtime.Latest.Metrics.Errors);
            var scheduler=typeof(PlayableRuntime).GetField("aiScheduler",Flags).GetValue(runtime);
            var owners=((IEnumerable)scheduler.GetType().GetProperty("Owners",Flags).GetValue(scheduler)).Cast<object>().ToArray();
            Assert.AreEqual(8,owners.Length);
            foreach(var owner in owners)
            {
                var checkpoint=(PlayableAiOwnerCheckpoint)owner.GetType().GetProperty("Checkpoint",Flags).GetValue(owner);
                Assert.Greater(checkpoint.DecisionTick,0,checkpoint.OwnerId);
            }
            runtime.RequestStop();deadline=Time.realtimeSinceStartup+8;while(!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.True(runtime.IsStopped);Set("runtime",null);
        }
        finally{runtime.RequestStop();}
    }
    private IEnumerator CaptureViewerFrame(string name,bool sceneVisible)
    {
        var root=Get<VisualElement>("root");var panel=host.GetComponent<UIDocument>().panelSettings;
        var texture=new RenderTexture(1600,900,24);texture.Create();panel.targetTexture=texture;
        float scale=OrbitalTheme.ReadableScale(1600,900);root.style.width=1600/scale;root.style.height=900/scale;
        var camera=Get<Camera>("cameraView");var backdrop=new RenderTexture(1600,900,24);backdrop.Create();camera.targetTexture=backdrop;
        var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=scene.style.right=scene.style.top=scene.style.bottom=0;
        scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(backdrop));root.Insert(0,scene);
        try
        {
            if(sceneVisible)Call("UpdateFrame");
            yield return null;yield return null;yield return null;
            var folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/spectator-native"));System.IO.Directory.CreateDirectory(folder);
            var old=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,1600,900),0,0);pixels.Apply();RenderTexture.active=old;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder,name+".png"),pixels.EncodeToPNG());UnityEngine.Object.DestroyImmediate(pixels);
        }
        finally{scene.RemoveFromHierarchy();panel.targetTexture=null;camera.targetTexture=null;texture.Release();backdrop.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(backdrop);}
    }
    [UnityTest] public IEnumerator AllAiControlsFitCompactLobby()
    {
        Call("ShowLobby");var root=Get<VisualElement>("root");var lobby=Get<VisualElement>("lobbyScreen");SetFirstSeatAi();
        foreach(var size in new[]{new Vector2Int(1280,800),new Vector2Int(800,620),new Vector2Int(760,900)})
        {
            root.style.width=size.x;root.style.height=size.y;yield return null;yield return null;
            foreach(var control in lobby.Query<VisualElement>().ToList().Where(e=>e is Button||e is TextField||e is DropdownField))
            {Assert.Greater(control.worldBound.width,0,control.name);Assert.LessOrEqual(control.worldBound.xMax,root.worldBound.xMax+.5f,control.name);Assert.LessOrEqual(control.worldBound.yMax,root.worldBound.yMax+.5f,control.name);}
            Assert.LessOrEqual(Get<VisualElement>("rosterPanel").worldBound.yMax,Get<Label>("lobbyStatus").worldBound.yMin+.5f);
        }
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
    [UnityTest] public IEnumerator FoundryStartsThroughLobbyAndRestartsAllSixOwners()
    {
        IEnumerator Wait(Func<bool> ready){float deadline=Time.realtimeSinceStartup+12;while(!ready()&&Time.realtimeSinceStartup<deadline)yield return null;Assert.True(ready(),"Foundry ordinary lobby deadline");}
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(app,value);
        string output=Environment.GetEnvironmentVariable("FOUNDRY_EVIDENCE");
        var panel=host.GetComponent<UIDocument>().panelSettings;var image=new RenderTexture(1600,1000,24);image.Create();panel.targetTexture=image;var backdrop=new RenderTexture(1600,1000,24);backdrop.Create();
        var root=Get<VisualElement>("root");panel.scale=OrbitalTheme.ReadableScale(1600,1000);root.style.width=1600/panel.scale;root.style.height=1000/panel.scale;var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=scene.style.right=scene.style.top=scene.style.bottom=0;root.Insert(0,scene);
        IEnumerator Shot(string name){if(!string.IsNullOrEmpty(output)){Get<Camera>("cameraView").targetTexture=backdrop;Get<Camera>("cameraView").Render();scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(backdrop));for(int i=0;i<6;i++)yield return null;System.IO.Directory.CreateDirectory(output);var prior=RenderTexture.active;RenderTexture.active=image;var png=new Texture2D(1600,1000,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,1600,1000),0,0);png.Apply();Assert.Greater(png.GetPixels32().Where((_,i)=>i%173==0).Select(c=>(int)c.r<<16|(int)c.g<<8|c.b).Distinct().Count(),8);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,name+".png"),png.EncodeToPNG());RenderTexture.active=prior;UnityEngine.Object.Destroy(png);}}
        var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
        try
        {
            Call("BindLobbyDevices");Call("ShowLobby");var lobby=Get<VisualElement>("lobbyScreen");lobby.Q<DropdownField>("lobby-map-choice").value="Огненный разлом";
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=19092026;Call("RefreshLobbyValidation");
            Assert.True(Get<Button>("launchButton").enabledSelf);Assert.True(Get<PlayableProfile>("profile").AuthoredMap is FoundryMap);
            yield return Shot("lobby-foundry");
            Get<Button>("launchButton").Focus();Get<NativeMenuNavigation>("menuNavigation").Activate();
            yield return Wait(()=>Get<PlayableRuntime>("runtime")!=null&&!Get<bool>("preparing"));
            var runtime=Get<PlayableRuntime>("runtime");Assert.AreEqual(6,runtime.Latest.Participants.Count);Assert.True(runtime.Latest.ActiveProfile.AuthoredMap is FoundryMap);
            yield return Wait(()=>runtime.Latest.Tick>=10);Call("UpdateFrame");yield return Shot("lobby-active-match");
            var ally=runtime.ParticipantView("foundry-2");Assert.AreEqual(runtime.Latest.Team,ally.Team);
            Assert.False((bool)typeof(PlayableBootstrap).GetMethod("IsOpponent",Flags).Invoke(app,new object[]{(PlayableOwner)1}));Assert.True((bool)typeof(PlayableBootstrap).GetMethod("IsOpponent",Flags).Invoke(app,new object[]{(PlayableOwner)3}));
            // Use normal command ingress through the actual build HUD and production UI.
            var home=runtime.Latest.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
            var slot=runtime.Latest.ActiveProfile.AuthoredMap.Sites(runtime.Latest.ActiveProfile).Single(site=>site.Id==home.SiteId).Slots[0];var cursor=(Vector2)Get<Camera>("cameraView").WorldToScreenPoint(Get<PlayableWorld>("world").Point(slot.Position));
            typeof(PlayableBootstrap).GetMethod("Select",Flags).Invoke(app,new object[]{cursor,cursor,false});Assert.AreEqual(home.SiteId,Get<int>("selectedSite"));Assert.AreEqual(slot.Id,Get<int>("selectedSlot"));
            typeof(PlayableBootstrap).GetMethod("BuildSelected",Flags).Invoke(app,new object[]{PlayableBuildingKind.Factory});
            yield return Wait(()=>runtime.Latest.Buildings.Any(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory));
            Assert.IsNull(runtime.Latest.Failure);
            runtime.RequestFinish();yield return Wait(()=>runtime.Result!=null);Call("UpdateFrame");
            Assert.AreEqual(6,runtime.Result.Players.Count);Assert.AreEqual(6,runtime.Result.Players.Select(p=>setup.ParticipantName(p.Slot)).Distinct().Count());
            yield return Shot("foundry-results");var generation=runtime.Generation;Call("Restart");yield return Wait(()=>runtime.IsStopped);Call("UpdateFrame");
            var next=Get<PlayableRuntime>("runtime");Assert.Greater(next.Generation,generation);Assert.AreEqual(6,next.Latest.Participants.Count);Assert.True(next.Latest.ActiveProfile.AuthoredMap is FoundryMap);
            Call("ReturnToLobby");yield return Wait(()=>next.IsStopped);Call("UpdateFrame");Assert.True(Get<bool>("inLobby"));
            lobby.Q<DropdownField>("lobby-map-choice").value="Переправа";Assert.AreEqual(2,Get<NativeLobbyConfiguration>("lobbySetup").Capacity(Get<PlayableProfile>("profile")));
            Get<Button>("launchButton").Focus();Get<NativeMenuNavigation>("menuNavigation").Activate();yield return Wait(()=>Get<PlayableRuntime>("runtime")!=null&&!Get<bool>("preparing"));
            var two=Get<PlayableRuntime>("runtime");Assert.True(two.Latest.ActiveProfile.AuthoredMap is ThreeCrossingsMap);Assert.AreEqual(2,two.LobbyConfiguration.Capacity(two.Latest.ActiveProfile));Assert.False(two.LobbyConfiguration.Foundry);two.RequestStop();yield return Wait(()=>two.IsStopped);Set("runtime",null);
        }
        finally{Get<PlayableRuntime>("runtime")?.RequestStop();panel.targetTexture=null;Get<Camera>("cameraView").targetTexture=null;scene.RemoveFromHierarchy();image.Release();backdrop.Release();UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(backdrop);UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);}
    }
    [UnityTest] public IEnumerator EditableRosterUsesPressedDeviceAndCapturesApprovedStates()
    {
        var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
        var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
        var pressedPad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
        var otherPad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
        var panel=host.GetComponent<UIDocument>().panelSettings;var image=new RenderTexture(1600,1000,24);image.Create();panel.targetTexture=image;var backdrop=new RenderTexture(1600,1000,24);backdrop.Create();
        var root=Get<VisualElement>("root");root.style.width=1280;root.style.height=800;var scene=new VisualElement{pickingMode=PickingMode.Ignore};scene.style.position=Position.Absolute;scene.style.left=scene.style.right=scene.style.top=scene.style.bottom=0;root.Insert(0,scene);
        IEnumerator Shot(string name)
        {
            string output=Environment.GetEnvironmentVariable("FOUNDRY_EVIDENCE");
            for(int i=0;i<5;i++)yield return null;
            if(string.IsNullOrEmpty(output))yield break;
            var camera=Get<Camera>("cameraView");camera.targetTexture=backdrop;camera.Render();scene.style.backgroundImage=new StyleBackground(Background.FromRenderTexture(backdrop));
            var prior=RenderTexture.active;RenderTexture.active=image;var png=new Texture2D(1600,1000,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,1600,1000),0,0);png.Apply();
            System.IO.Directory.CreateDirectory(output);System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,name+".png"),png.EncodeToPNG());RenderTexture.active=prior;UnityEngine.Object.Destroy(png);
        }
        try
        {
            Call("BindLobbyDevices");Call("ShowLobby");var lobby=Get<VisualElement>("lobbyScreen");lobby.Q<DropdownField>("lobby-map-choice").value="Огненный разлом";
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.Participants.RemoveRange(3,3);
            otherPad.MakeCurrent();typeof(PlayableBootstrap).GetMethod("AddLobbyParticipant",Flags).Invoke(app,new object[]{true,pressedPad});
            Assert.AreEqual(pressedPad.deviceId,setup.Participants.Last().DeviceId);Assert.AreEqual(4,setup.Participants.Count);
            typeof(PlayableBootstrap).GetMethod("AddLobbyParticipant",Flags).Invoke(app,new object[]{true,pressedPad});Assert.AreEqual(4,setup.Participants.Count,"Duplicate physical device must not create a seat");
            setup.Participants[0].Name="Алексей";setup.Participants[1].Name="Орион";setup.Participants[2].Name="Вега";setup.Participants[2].Team=2;setup.Participants[2].Difficulty=AiDifficulty.Veteran;setup.Participants[3].Name="Мария";
            typeof(PlayableBootstrap).GetField("lobbyActionError",Flags).SetValue(app,null);Call("RebuildRoster");Call("RefreshLobbyPreview");Get<Button>("launchButton").Focus();yield return Shot("premium-lobby-four");
            lobby.Q<DropdownField>("lobby-device-3").Focus();yield return Shot("premium-lobby-device-focus");
            foreach(var participant in setup.Participants)participant.Human=false;Call("RebuildRoster");Call("RefreshLobbyValidation");
            Assert.True(setup.Spectator);Assert.True(Get<Button>("launchButton").enabledSelf);StringAssert.Contains("ЗРИТЕЛЯ",lobby.Q<Label>("lobby-mode-reason").text);
            yield return Shot("premium-lobby-spectator");
            Call("LaunchLobbyMatch");float deadline=Time.realtimeSinceStartup+10;while(Get<bool>("preparing")&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.False(Get<bool>("preparing"));Call("UpdateFrame");var input=Get<Spacewars.Input.PlayableInput>("input");Assert.True(input.WorldInputEnabled);Assert.False(input.CommandInputEnabled);
            Assert.AreEqual("ЗРИТЕЛЬ",Get<Label>("objective").text);Assert.AreEqual(DisplayStyle.None,Get<Label>("noticeLabel").style.display.value);Assert.AreEqual(DisplayStyle.None,Get<Label>("creditsLabel").style.display.value);Assert.Greater(Get<PlayableMapTerrain>("mapTerrain").Uploads,0);
            var camera=Get<Camera>("cameraView");var before=camera.transform.position;typeof(PlayableBootstrap).GetMethod("Pan",Flags).Invoke(app,new object[]{Vector2.right});Assert.Greater(camera.transform.position.x,before.x);
            var runtime=Get<PlayableRuntime>("runtime");Assert.True(runtime.Latest.Participants.All(p=>p.Control==OfflineControl.Ai));yield return Shot("premium-spectator-active");runtime.RequestStop();while(!runtime.IsStopped)yield return null;
        }
        finally
        {
            panel.targetTexture=null;Get<Camera>("cameraView").targetTexture=null;scene.RemoveFromHierarchy();image.Release();backdrop.Release();UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(backdrop);
            foreach(var device in new UnityEngine.InputSystem.InputDevice[]{keyboard,mouse,pressedPad,otherPad})UnityEngine.InputSystem.InputSystem.RemoveDevice(device);
        }
    }
}

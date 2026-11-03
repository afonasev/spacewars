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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class PostMatchDetailsUiTests:InputTestFixture
{
    private GameObject host;private PanelSettings panel;private PostMatchResultsView ui;private NativeMenuNavigation navigation;private VisualElement root;private RenderTexture target;
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    private static MatchResult RichMatch(bool producerUpgrades=false)
    {
        var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);var p=PlayableProfile.Default;var d=Activator.CreateInstance(type,F,null,new object[]{p,1L,false},null);
        object Call(string method,params object[] args)=>type.GetMethod(method,F).Invoke(d,args);
        void Fact(PlayableOwner owner,MatchFactKind kind,int entity,string description="")=>Call("Fact",owner,kind,0d,entity,description);
        Call("Step",20d);
        foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy}){
            for(int i=0;i<8;i++)Fact(owner,MatchFactKind.UnitCompleted,(int)(i%2==0?PlayableEntityKind.Tank:PlayableEntityKind.Explorer));
            Fact(owner,MatchFactKind.BuildingCompleted,(int)PlayableBuildingKind.Factory);Fact(owner,MatchFactKind.BuildingCompleted,(int)PlayableBuildingKind.Hangar);Fact(owner,MatchFactKind.BuildingCompleted,(int)PlayableBuildingKind.Refinery);
        }
        Call("Step",20d);
        foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy}){
            foreach(var k in new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ShkvalGuidance})Fact(owner,MatchFactKind.ResearchCompleted,(int)k,PlayableResearchRules.Description(p,k));
            Fact(owner,MatchFactKind.RefineryUpgradeCompleted,(int)PlayableBuildingKind.Refinery,PlayableResearchRules.RefineryDescription(p));
            if(producerUpgrades)foreach(var producer in new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Hangar})Fact(owner,MatchFactKind.RefineryUpgradeCompleted,(int)producer,"Ускорение производства");
        }
        Call("Step",20d);Fact(PlayableOwner.Player,MatchFactKind.PlayerDefeated,0);Fact(PlayableOwner.Player,MatchFactKind.BuildingSold,(int)PlayableBuildingKind.Factory);Fact(PlayableOwner.Enemy,MatchFactKind.BuildingLost,(int)PlayableBuildingKind.Refinery);Call("Step",10d);Fact(PlayableOwner.Enemy,MatchFactKind.PlayerLeft,0);Call("Step",10d);Call("FinishManually");return (MatchResult)type.GetProperty("Result",F).GetValue(d);
    }
    [SetUp]public override void Setup()
    {
        base.Setup();host=new GameObject("post-match details fixture");panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");panel.scaleMode=PanelScaleMode.ConstantPixelSize;target=new RenderTexture(1280,720,24);target.Create();panel.targetTexture=target;
        var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;root=doc.rootVisualElement;root.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");OrbitalTheme.Install(root);ui=new PostMatchResultsView(RichMatch(),()=>{},()=>{},()=>{});root.Add(ui.Root);navigation=new NativeMenuNavigation(root);navigation.SetScope(ui.Root,null,ui.FirstTab);
    }
    [TearDown]public override void TearDown(){navigation.Dispose();panel.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);base.TearDown();}
    private void Poll(Gamepad pad,GamepadState state,float time){InputSystem.QueueStateEvent(pad,state);InputSystem.Update();typeof(NativeMenuNavigation).GetMethod("TickGamepad",F).Invoke(navigation,new object[]{pad,time});}
    private static string TextOf(VisualElement e)=>string.Join(" ",e.Query<Label>().ToList().Select(l=>l.text));
    private void Capture(string name)
    {
        var output=Environment.GetEnvironmentVariable("SPACEWARS_POST_MATCH_EVIDENCE");if(string.IsNullOrEmpty(output))return;Directory.CreateDirectory(output);
        Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);
        var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();Assert.Greater(image.GetPixels32().Where((_,i)=>i%173==0).Select(c=>(int)c.r<<16|(int)c.g<<8|c.b).Distinct().Count(),8);File.WriteAllBytes(Path.Combine(output,name),image.EncodeToPNG());}finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
    }
    [UnityTest]public IEnumerator FourGraphsStacksFilteringCloudsAndNestedDescriptions()
    {
        ui.Root.RemoveFromHierarchy();ui=new PostMatchResultsView(RichMatch(true),()=>{},()=>{},()=>{});root.Add(ui.Root);navigation.SetScope(ui.Root,null,ui.FirstTab);
        ui.ShowTab(1);for(int i=0;i<8;i++)yield return null;
        Assert.IsNull(ui.Root.Q("result-graph-values"));for(int i=0;i<4;i++)Assert.IsNotNull(ui.Root.Q("result-graph-"+i));
        Assert.AreEqual(3,ui.Result.ResearchCount(PlayableOwner.Player,80));Assert.AreEqual(3,ui.Result.Players[0].BuildingsUpgraded);
        var upgrades=ui.Result.Facts.Where(f=>f.Kind==MatchFactKind.RefineryUpgradeCompleted).ToArray();Assert.AreEqual(6,upgrades.Length);
        foreach(int metric in new[]{0,1})
        {
            var found=new System.Collections.Generic.HashSet<long>();
            foreach(var marker in ui.Root.Q("result-graph-"+metric).Query<VisualElement>(className:"result-event-marker").ToList())
            {
                ((Action)marker.userData)();for(int i=0;i<3;i++)yield return null;
                foreach(var fact in upgrades)if(ui.Root.Q("result-popup").Q("result-popup-event-"+fact.Sequence)!=null)found.Add(fact.Sequence);
                if(metric==1&&upgrades.Any(f=>f.EntityKind==(int)PlayableBuildingKind.Factory&&ui.Root.Q("result-popup").Q("result-popup-event-"+f.Sequence)!=null))Capture("producer-upgrades-army.png");
                navigation.Back();
            }
            var expected=upgrades.Where(f=>metric==0?f.EntityKind==(int)PlayableBuildingKind.Refinery:f.EntityKind!=(int)PlayableBuildingKind.Refinery).Select(f=>f.Sequence);
            CollectionAssert.AreEquivalent(expected,found,"Only the appropriate upgrade events appear in each graph's actual event popups.");
        }
        ui.InspectTime(40);yield return null;var tooltip=ui.Root.Q("result-graph-tooltip");Assert.That(TextOf(tooltip),Does.Contain("Игрок 1"));Assert.LessOrEqual(tooltip.worldBound.xMax,ui.Root.worldBound.xMax);Capture("four-graphs-hover.png");
        var research=ui.Root.Q("result-graph-3");var count=research.Q<Label>("result-stack-count");Assert.IsNotNull(count);Assert.GreaterOrEqual(int.Parse(count.text),3);
        var stack=count.parent;((Action)stack.userData)();for(int i=0;i<3;i++)yield return null;var popup=ui.Root.Q("result-popup");Assert.IsNotNull(popup);
        Assert.AreEqual(6,popup.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("result-popup-event-")),"All simultaneous events from both owners remain available.");
        var first=popup.Query<VisualElement>().ToList().First(e=>e.name.StartsWith("result-popup-event-"));((Action)first.userData)();for(int i=0;i<3;i++)yield return null;
        Assert.That(TextOf(ui.Root.Q("result-description")),Does.Contain(PlayableResearchRules.Description(PlayableProfile.Default,PlayableResearchKind.TankChassis)));Capture("research-stack-description.png");
        navigation.Back();ui.Root.Q<Toggle>("result-hide-events").value=true;Assert.IsNull(ui.Root.Q("result-stack-count"));Assert.AreEqual(3,ui.Result.ResearchCount(PlayableOwner.Player,80));ui.Root.Q<Toggle>("result-hide-events").value=false;
        ui.Root.Q<Toggle>("result-filter-0").value=false;ui.InspectTime(40);Assert.IsFalse(TextOf(ui.Root.Q("result-graph-tooltip")).Contains("Игрок 1"));
        ui.ShowTab(0);for(int i=0;i<3;i++)yield return null;var buildings=ui.Root.Q("result-cell-0-2");((Action)buildings.userData)();for(int i=0;i<3;i++)yield return null;
        Assert.IsNotNull(ui.Root.Q("result-total-icon-RefineryUpgradeCompleted-2"));Capture("building-cloud.png");navigation.Back();
        var cell=ui.Root.Q("result-cell-0-6");((Action)cell.userData)();for(int i=0;i<3;i++)yield return null;Assert.IsNull(ui.Root.Q("result-total-icon-RefineryUpgradeCompleted-2"));Assert.AreEqual(3,ui.Root.Q("result-popup-scroll").contentContainer.childCount);
        var icon=ui.Root.Q("result-total-icon-ResearchCompleted-0");((Action)icon.userData)();for(int i=0;i<3;i++)yield return null;Capture("research-cloud-description.png");
        var region=(bool)typeof(PostMatchResultsView).GetMethod("PointerInDetails",F).Invoke(ui,new object[]{ui.Root.Q("result-popup").worldBound.center});Assert.IsTrue(region);
        Assert.IsFalse((bool)typeof(PostMatchResultsView).GetMethod("PointerInDetails",F).Invoke(ui,new object[]{new Vector2(-100,-100)}));
    }
    private static void PointerMove(VisualElement target,Vector2 position)
    {
        using(var evt=PointerMoveEvent.GetPooled(new Event{type=EventType.MouseMove,mousePosition=position})){evt.target=target;target.SendEvent(evt);}
    }
    [UnityTest]public IEnumerator MouseHoverShowsAllValuesAndOnlyExplicitIconsOpenEvents()
    {
        ui.ShowTab(1);for(int i=0;i<8;i++)yield return null;
        var chart=ui.Root.Q("result-graph-0");var point=chart.worldBound.position+new Vector2(chart.worldBound.width*.35f,chart.worldBound.height*.4f);
        PointerMove(chart,point);Assert.IsNull(ui.Root.Q("result-popup"));var tip=ui.Root.Q("result-graph-tooltip");Assert.That(TextOf(tip),Does.Contain("Игрок 1"));Assert.That(TextOf(tip),Does.Contain("ИИ 2"));Assert.AreEqual(2,tip.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("result-hover-value-")));
        var research=ui.Root.Q("result-graph-3");var stack=research.Q<Label>("result-stack-count").parent;using(var enter=PointerEnterEvent.GetPooled(new Event{type=EventType.MouseMove,mousePosition=stack.worldBound.center})){enter.target=stack;stack.SendEvent(enter);}for(int i=0;i<3;i++)yield return null;Assert.IsNotNull(ui.Root.Q("result-popup"));
        PointerMove(chart,point);Assert.IsNull(ui.Root.Q("result-popup"));Assert.AreEqual(DisplayStyle.Flex,tip.style.display.value);Assert.AreEqual(2,tip.Query<OrbitalGlyph>().ToList().Count(g=>g.Kind=="credits"));for(int i=0;i<3;i++)yield return null;Capture("grafana-all-values.png");
        foreach(var toggle in ui.Root.Query<Toggle>().ToList()){
            var input=toggle.Q(className:"unity-base-field__input");Assert.AreEqual(0,input.style.borderLeftWidth.value);Assert.AreEqual(Color.clear,input.style.backgroundColor.value);Assert.LessOrEqual(input.worldBound.xMax,ui.Root.Q("result-plots").worldBound.xMin);Assert.LessOrEqual(toggle.worldBound.xMax,ui.Root.Q("result-plots").worldBound.xMin);
        }
        var army=ui.Root.Q("result-graph-1");Assert.AreEqual(2,army.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("result-exit-band-")));ui.Root.Q<Toggle>("result-hide-events").value=true;Assert.AreEqual(2,army.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("result-exit-band-")));ui.Root.Q<Toggle>("result-filter-0").value=false;Assert.AreEqual(1,army.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("result-exit-band-")));for(int i=0;i<3;i++)yield return null;Capture("exit-bands-and-clean-filters.png");
        ui.ShowTab(0);for(int i=0;i<3;i++)yield return null;((Action)ui.Root.Q("result-cell-0-1").userData)();for(int i=0;i<3;i++)yield return null;Assert.IsFalse(TextOf(ui.Root.Q("result-popup")).Contains("Построено"));Assert.IsFalse(TextOf(ui.Root.Q("result-popup")).Contains("Игрок"));
        var unit=ui.Root.Q("result-total-icon-UnitCompleted-0");((Action)unit.userData)();for(int i=0;i<3;i++)yield return null;string description=TextOf(ui.Root.Q("result-description"));Assert.That(description,Does.Contain("Танк"));Assert.That(description,Does.Contain("Бронированная боевая машина"));Assert.IsFalse(description.Contains("Произведено"));Assert.IsFalse(description.Contains("00:"));Capture("unit-description-no-log-metadata.png");Assert.IsNull(ui.Root.Q("result-pad-hints"));
    }
    [UnityTest]public IEnumerator EightPlayersKeepExitBandsAndHoverValuesReadable()
    {
        var profile=PlayableProfile.Default;var basis=SourceFlatFixture.Load(Resources.Load<TextAsset>("OfflineFlatFixture").text,profile);var roster=Enumerable.Range(1,8).Select(i=>new OfflineParticipant("slot-"+i,i,i,i<=4?OfflineControl.Human:OfflineControl.Ai)).ToArray();var costs=new double[8,8];for(int i=0;i<8;i++)for(int j=0;j<8;j++)costs[i,j]=basis.RouteCost(i,j);
        var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,roster,basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs);var authority=new OfflineParticipantAuthority(config,1);var domain=typeof(OfflineParticipantAuthority).GetField("domain",F).GetValue(authority);var type=domain.GetType();
        for(int slot=0;slot<7;slot++){if(slot!=4)authority.Step(5);type.GetMethod("RecordPlayerExit",F).Invoke(domain,new object[]{(PlayableOwner)slot,slot%3==1});}authority.Step(5);authority.FinishManually();
        ui.Root.RemoveFromHierarchy();ui=new PostMatchResultsView(authority.Result,()=>{},()=>{},()=>{});root.Add(ui.Root);navigation.SetScope(ui.Root,null,ui.FirstTab);ui.ShowTab(1);for(int i=0;i<8;i++)yield return null;
        var army=ui.Root.Q("result-graph-1");Assert.AreEqual(7,army.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("result-exit-band-")));var point=army.worldBound.position+new Vector2(army.worldBound.width*.35f,army.worldBound.height*.6f);PointerMove(army,point);for(int i=0;i<3;i++)yield return null;
        var tooltip=ui.Root.Q("result-graph-tooltip");Assert.AreEqual(8,tooltip.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("result-hover-value-")));foreach(var row in tooltip.Query<VisualElement>().ToList().Where(e=>e.name.StartsWith("result-hover-value-")))Assert.LessOrEqual(row.worldBound.yMax,tooltip.worldBound.yMax+.5f);
        Assert.AreEqual(8,tooltip.Query<OrbitalGlyph>().ToList().Count(g=>g.Kind=="army"));Capture("eight-player-exit-bands-and-values.png");navigation.UseGamepad();for(int i=0;i<3;i++)yield return null;var badge=ui.Root.Q("result-shoulder-right").ElementAt(0);Assert.That(badge.worldBound.center.y,Is.EqualTo(ui.Root.Q("result-tab-2").worldBound.center.y).Within(1.5f));var left=ui.Root.Q("result-shoulder-left").ElementAt(0);Assert.That(left.worldBound.xMin,Is.EqualTo(ui.FirstTab.worldBound.xMin+10).Within(2));Assert.That(left.worldBound.center.y,Is.EqualTo(ui.FirstTab.worldBound.center.y).Within(1.5f));Capture("eight-player-shoulder-badges.png");
    }
    [UnityTest]public IEnumerator RightStickScrollsTheFocusedLogColumn()
    {
        ui.ShowTab(2);var right=ui.Root.Q<DropdownField>("result-log-player-1");var column=right.parent;var rightScroll=column.Q<ScrollView>();var leftScroll=ui.Root.Q<DropdownField>("result-log-player-0").parent.Q<ScrollView>();
        rightScroll.Clear();for(int i=0;i<60;i++)rightScroll.Add(new Label("Длинная запись "+i));for(int i=0;i<8;i++)yield return null;
        var pad=InputSystem.AddDevice<Gamepad>();try{right.Focus();Poll(pad,new GamepadState(),0);Poll(pad,new GamepadState{rightStick=Vector2.down},.1f);Poll(pad,new GamepadState{rightStick=Vector2.down},.2f);Assert.Greater(rightScroll.scrollOffset.y,0);Assert.AreEqual(0,leftScroll.scrollOffset.y);}finally{InputSystem.RemoveDevice(pad);}
    }
    [UnityTest]public IEnumerator AssignedGamepadTabsGraphInspectionAndPopupReturn()
    {
        for(int i=0;i<5;i++)yield return null;var pad=InputSystem.AddDevice<Gamepad>();int overview=0;navigation.SetScope(ui.Root,()=>overview++,ui.FirstTab);
        try{
            Poll(pad,new GamepadState(),0);Poll(pad,new GamepadState().WithButton(GamepadButton.RightShoulder),.1f);Assert.AreEqual(1,ui.SelectedTab);for(int i=0;i<5;i++)yield return null;
            Assert.IsNull(ui.Root.Q("result-pad-hints"));Assert.AreEqual(DisplayStyle.Flex,ui.Root.Q("result-shoulder-right").style.display.value);Capture("gamepad-tabs.png");ui.Root.Q("result-graph-3").Focus();
            Poll(pad,new GamepadState(),.2f);Poll(pad,new GamepadState().WithButton(GamepadButton.South),.3f);Poll(pad,new GamepadState(),.4f);Poll(pad,new GamepadState{leftStick=Vector2.right},.5f);
            Assert.Greater((double)typeof(PostMatchResultsView).GetField("cursorTime",F).GetValue(ui),0);
            Poll(pad,new GamepadState().WithButton(GamepadButton.DpadRight),.8f);Poll(pad,new GamepadState(),.9f);Poll(pad,new GamepadState().WithButton(GamepadButton.South),1f);for(int i=0;i<3;i++)yield return null;Assert.IsNotNull(ui.Root.Q("result-popup"));Assert.IsNotNull(ui.Root.Q("result-description"));
            Poll(pad,new GamepadState(),1.1f);Poll(pad,new GamepadState().WithButton(GamepadButton.East),1.2f);Assert.IsNull(ui.Root.Q("result-popup"));Assert.AreEqual(0,overview);
            Poll(pad,new GamepadState(),1.3f);Poll(pad,new GamepadState().WithButton(GamepadButton.East),1.4f);Assert.AreEqual(0,overview);
            Poll(pad,new GamepadState(),1.5f);Poll(pad,new GamepadState().WithButton(GamepadButton.LeftShoulder),1.6f);Assert.AreEqual(0,ui.SelectedTab);for(int i=0;i<3;i++)yield return null;
            ui.Root.Q("result-cell-0-1").Focus();Poll(pad,new GamepadState(),1.7f);Poll(pad,new GamepadState().WithButton(GamepadButton.South),1.8f);for(int i=0;i<3;i++)yield return null;Assert.IsNotNull(ui.Root.Q("result-popup"));Assert.IsNotNull(ui.Root.Q("result-description"));Assert.Greater(ui.Root.Q("result-description").worldBound.xMin,10,"Nested descriptions follow the laid-out icon rather than the pre-layout origin.");Assert.IsFalse(ui.Root.Q("result-description").worldBound.Overlaps(ui.Root.Q("result-popup").worldBound),"Descriptions leave every cloud icon accessible to the pointer.");Capture("gamepad-cloud.png");
            Poll(pad,new GamepadState(),1.9f);Poll(pad,new GamepadState().WithButton(GamepadButton.East),2f);Assert.AreEqual("result-cell-0-1",((VisualElement)root.focusController.focusedElement).name);Assert.AreEqual(0,overview);
        }finally{InputSystem.RemoveDevice(pad);}
    }
}

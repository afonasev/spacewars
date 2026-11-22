using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class PlayableEscMenuTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap hud;
    T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
    void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
    void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(hud,null);
    [SetUp] public void Setup(){host=new GameObject("minimal Esc menu");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;Set("profile",PlayableProfile.Default);Call("CreateHud");}
    [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(host);}
    [TestCase(-1)] [TestCase(0)] [TestCase(1)]
    public void CommonAndProvingMenusHaveOnlyTheirRequiredActions(int phase)
    {
        Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,
            Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,activeProfile:PlayableProfile.Default));
        Set("paused",true);Set("menuPaused",true);
        if(phase>=0){Set("provingGround",new ProvingGroundSetup(PlayableProfile.Default));Set("provingGroundBattle",phase==1);}
        Call("UpdateHud");
        var card=Get<VisualElement>("modalCard");
        Assert.IsNull(card.Q("match-lab-identity"));Assert.IsNull(card.Q("menu-copyright"));Assert.IsNull(card.Q("pause-controls"));
        Assert.AreEqual(phase<0?"Пауза":"Полигон",Get<Label>("modalTitle").text);
        var expected=phase<0?new[]{"Продолжить","Начать заново","Настройки","Завершить матч"}:phase==0?
            new[]{"Продолжить","Начать бой","Сбросить","Настройки","Выйти в лабораторию"}:new[]{"Продолжить","К расстановке","Настройки","Выйти в лабораторию"};
        CollectionAssert.AreEqual(expected,card.Query<Button>().ToList().Where(b=>b.style.display.value!=DisplayStyle.None).Select(b=>b.text).ToArray());
        if(phase==0){Assert.True(Get<Button>("provingStartButton").ClassListContains("orbital-primary"));Assert.AreEqual(FontStyle.Bold,Get<Button>("provingStartButton").style.unityFontStyleAndWeight.value);Assert.False(Get<Button>("resumeButton").ClassListContains("orbital-primary"));}
        if(phase>=0){
            Assert.AreEqual(DisplayStyle.None,Get<Label>("creditsLabel").style.display.value,"Campaign HUD must not restore proving credits");
            Get<VisualElement>("top").style.display=Get<VisualElement>("bottom").style.display=DisplayStyle.Flex;
            Call("CloseMap");
            Call("CreateProvingGroundUi");
            Assert.True(Get<VisualElement>("hudRow").Contains(Get<VisualElement>("compactMap")),"Minimap must reserve ordinary row space beside the context HUD");
            Assert.AreEqual(DisplayStyle.Flex,Get<VisualElement>("bottom").style.display.value,"Ordinary HUD stays available before and after Start");
        }
        Call("OpenPauseSettings");
        Assert.IsNotNull(Get<VisualElement>("childMenu").Q<Button>("settings-controls"),"Control help stays reachable inside settings.");
    }
    [Test]
    public void ProvingPanelsHaveDistinctTabsUnitIconsAndSharedHoverDescriptions()
    {
        Set("provingGround",new ProvingGroundSetup(PlayableProfile.Default));Call("CreateProvingGroundUi");
        var navigation=typeof(PlayableBootstrap).GetField("menuNavigation",Flags).GetValue(hud);
        for(int side=0;side<2;side++){
            var layer=Get<VisualElement>("provingGroundLayer");
            var units=layer.Q<Button>("proving-tab-"+side+"-units");var research=layer.Q<Button>("proving-tab-"+side+"-research");
            Assert.True(units.ClassListContains("proving-tab-active"));Assert.False(research.ClassListContains("proving-tab-active"));
            Assert.AreNotEqual(units.style.backgroundColor.value,research.style.backgroundColor.value);
            foreach(var kind in new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk}){
                var button=layer.Q<Button>("proving-unit-"+side+"-"+kind);
                Assert.False(button.text.Contains("поставить"));Assert.AreEqual(kind.ToString().ToLowerInvariant(),button.Q<OrbitalGlyph>().Kind);
                Assert.False(button.focusable,"Mouse-only controls must preserve camera key ownership");
                using(var enter=PointerEnterEvent.GetPooled()){enter.target=button;button.SendEvent(enter);}
                var card=layer.Q("proving-unit-detail");Assert.IsNotNull(card);Assert.AreEqual(DisplayStyle.Flex,card.style.display.value);
                Assert.AreEqual(PlayableMenuDescriptions.Unit(PlayableProfile.Default,kind,freeEconomy:true),card.Q<Label>().text);
                using(var leave=PointerLeaveEvent.GetPooled()){leave.target=button;button.SendEvent(leave);}
                Assert.AreEqual(DisplayStyle.None,card.style.display.value);
            }
            navigation.GetType().GetMethod("ActivateElement").Invoke(navigation,new object[]{research});layer=Get<VisualElement>("provingGroundLayer");
            Assert.True(layer.Q<Button>("proving-tab-"+side+"-research").ClassListContains("proving-tab-active"));
            Assert.False(layer.Q<Button>("proving-tab-"+side+"-units").ClassListContains("proving-tab-active"));
            Assert.IsNull(layer.Q("proving-unit-detail"),"Tab switch removes stale hover card");
            foreach(var kind in new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ShkvalGuidance,PlayableResearchKind.HawkSalvoSystems,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.ReinforcedConcrete}){
                var row=layer.Q("proving-research-row-"+side+"-"+kind);Assert.IsNotNull(row,"Every approved chain must remain listed");
                using(var enter=PointerEnterEvent.GetPooled()){enter.target=row;row.SendEvent(enter);}
                Assert.That(layer.Q<Label>("proving-unit-description").text,Does.Contain(PlayableResearchRules.Description(PlayableProfile.Default,kind)));
                using(var leave=PointerLeaveEvent.GetPooled()){leave.target=row;row.SendEvent(leave);}
                Assert.AreEqual(DisplayStyle.None,layer.Q("proving-unit-detail").style.display.value);
            }
        }
    }

    [UnityTest]
    public IEnumerator ExplorerUpgradeSelectionIsImmediateAndDependentForBothSides()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);var runtime=new PlayableRuntime(setup.Configuration(),107);
        IEnumerator Until(Func<bool> condition){float deadline=Time.realtimeSinceStartup+15;while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;Assert.True(condition(),"Proving edit reached the authority");}
        bool Has(int side,PlayableResearchKind kind)=>runtime.ParticipantView(side==0?"player-1":"enemy-1")?.OwnerResearch.Any(o=>o.Kind==kind&&o.Complete)==true;
        try
        {
            yield return Until(()=>runtime.IsPrepared&&runtime.Latest!=null);Set("runtime",runtime);Set("profile",setup.Profile);Set("provingGround",setup);Set("view",runtime.Latest);
            var tabs=Get<string[]>("provingTabs");tabs[0]=tabs[1]="Исследования";Call("CreateProvingGroundUi");var nav=Get<NativeMenuNavigation>("menuNavigation");
            for(int side=0;side<2;side++)
            {
                var row=Get<VisualElement>("provingGroundLayer").Q<Button>("proving-research-"+side+"-ExplorerAssaultGuns");var badge=row.Q<Label>("proving-research-stage-"+side+"-ExplorerAssaultGuns");
                nav.ActivateElement(row);Assert.AreEqual("I",badge.text);
                yield return Until(()=>!runtime.ProvingGroundEditsPending&&Has(side,PlayableResearchKind.ExplorerAssaultGuns));Assert.False(Has(side,PlayableResearchKind.ExplorerRocketLauncher));
                nav.ActivateElement(row);Assert.AreEqual("II",badge.text);Call("UpdateProvingGroundUi");Assert.AreEqual("II",badge.text);
                yield return Until(()=>!runtime.ProvingGroundEditsPending&&Has(side,PlayableResearchKind.ExplorerAssaultGuns)&&Has(side,PlayableResearchKind.ExplorerRocketLauncher));
                Assert.AreEqual(3,row.Q<PortraitUpgradeMark>().ChevronCount);
                nav.ActivateElement(row);Assert.AreEqual("—",badge.text);
                yield return Until(()=>!runtime.ProvingGroundEditsPending&&!Has(side,PlayableResearchKind.ExplorerAssaultGuns)&&!Has(side,PlayableResearchKind.ExplorerRocketLauncher));
                nav.ActivateElement(row);nav.ActivateElement(row);
                yield return Until(()=>!runtime.ProvingGroundEditsPending&&Has(side,PlayableResearchKind.ExplorerRocketLauncher));
                Assert.True(runtime.ProvingGroundResetSetup.Research(side).Contains(PlayableResearchKind.ExplorerAssaultGuns));
            }
        }
        finally{runtime.RequestStop();Set("runtime",null);}
    }

    [Test]
    public void ProvingResearchListsSpecializationsBeforeOrderedGlobalLevels()
    {
        Set("provingGround",new ProvingGroundSetup(PlayableProfile.Default));
        var tabs=Get<string[]>("provingTabs");tabs[0]=tabs[1]="Исследования";Call("CreateProvingGroundUi");
        var expected=new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ShkvalGuidance,PlayableResearchKind.HawkSalvoSystems,
            PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.ReinforcedConcrete};
        for(int side=0;side<2;side++)
        {
            string prefix="proving-research-row-"+side+"-";
            var panel=Get<VisualElement>("provingGroundLayer").Q("proving-side-"+side);
            var rows=panel.Query<VisualElement>().ToList().Where(e=>e.name.StartsWith(prefix)).Select(e=>e.name).ToArray();
            CollectionAssert.AreEqual(expected.Select(k=>prefix+k).ToArray(),rows,"The exact sequence must match on side "+side);
        }
    }

    [Test]
    public void ProvingGroundShowsRocketLauncherMarkForBothSides()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);for(int side=0;side<2;side++)setup.SetResearch(side,PlayableResearchKind.ExplorerRocketLauncher,true);
        Set("provingGround",setup);Call("CreateProvingGroundUi");
        var navigation=Get<NativeMenuNavigation>("menuNavigation");
        for(int side=0;side<2;side++)
        {
            var layer=Get<VisualElement>("provingGroundLayer");
            navigation.ActivateElement(layer.Q<Button>("proving-tab-"+side+"-research"));
            layer=Get<VisualElement>("provingGroundLayer");
            var row=layer.Q("proving-research-row-"+side+"-ExplorerAssaultGuns");
            Assert.NotNull(row);
            Assert.AreEqual(3,row.Q<PortraitUpgradeMark>().ChevronCount);
        }
    }

}

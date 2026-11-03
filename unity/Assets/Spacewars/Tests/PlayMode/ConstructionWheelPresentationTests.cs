using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Input;
using UnityEngine;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine.UIElements;

public sealed class ConstructionWheelPresentationTests
{
    [TestCase(true)] [TestCase(false)]
    public void CapturedMineStartsFocusedAndARespectsAvailability(bool affordable)
    {
        var profile=PlayableProfile.Default;
        var site=TerritoryRules.Sites(profile).First(s=>s.Kind==PlayableBuildingKind.Mine);
        int credits=affordable?TerritoryRules.Cost(profile,PlayableBuildingKind.Mine):0;
        var view=new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,null,1,false,0,false)},activeProfile:profile);
        var actions=OfflinePadWorldActions.Build(view,profile,site.Id,0);
        Assert.AreEqual(1,actions.Length);
        var index=OfflinePadWorldActions.RingSector(actions,0,0,.25);
        Assert.AreEqual(0,index,"Construction is focused without moving the stick");
        var action=actions[index.Value];
        Assert.AreEqual(affordable,action.Sector.Enabled);
        var input=JsonUtility.FromJson<NativeLocalInputProfile>(Resources.Load<TextAsset>("NativeLocalInputProfile").text);
        var gestures=new OfflinePadGestures();gestures.SetMode("buildingWheel");
        Assert.IsEmpty(gestures.StepDetailed(0,0,true,true,input,sector:action.Sector));
        Assert.IsEmpty(gestures.StepDetailed(10,1,true,true,input,sector:action.Sector));
        var intents=gestures.StepDetailed(20,0,true,true,input,sector:action.Sector);
        if(affordable)
        {
            Assert.AreEqual("activate",intents.Single().Kind);
            Assert.AreEqual(action.Sector.Id,intents.Single().Id);
            Assert.AreEqual(PlayableCommandKind.BuildAt,action.Command.Kind);
            Assert.AreEqual(site.Id,action.Command.Site);
        }
        else {Assert.IsEmpty(intents);Assert.IsNull(action.Command);}
    }
    [Test] public void OtherWheelsStillRequireDirection()
    {
        Assert.IsNull(OfflinePadWorldActions.RingSector(Array.Empty<OfflinePadAction>(),0,0,.25));
        Assert.IsNull(OfflinePadWorldActions.RingSector(new[]{new OfflinePadAction("group:1","Group",true)},0,0,.25));
        var actions=Actions(0,"ready");
        Assert.IsNull(OfflinePadWorldActions.RingSector(actions,0,0,.25));
        Assert.AreEqual(0,OfflinePadWorldActions.RingSector(actions,0,-1,.25));
    }
    static OfflinePadAction[] Actions(double deficit,string science,bool free=false,bool defense=true,PlayableBuildingKind? producer=null,string producerState="ready")
    {
        var p=PlayableProfile.Default;var site=TerritoryRules.Sites(p).First(s=>s.Kind==PlayableBuildingKind.Headquarters);
        var center=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Headquarters,site.Position,400,1,0,0,default,siteId:site.Id);
        var buildings=new[]{center};
        if(science!="none")
        {
            var lifecycle=new PlayableBuildingLifecycleSnapshot(science=="selling",0,false,false,0,0,null,null,1,0,false);
            var lab=new PlayableBuildingSnapshot(2,science=="enemy"?PlayableOwner.Enemy:PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,site.Position,100,1,0,0,default,phase:science=="pending"?ConstructionPhase.Pending:ConstructionPhase.Ready,lifecycle:lifecycle);
            buildings=buildings.Concat(new[]{lab}).ToArray();
        }
        if(producer.HasValue)
        {
            var lifecycle=new PlayableBuildingLifecycleSnapshot(producerState=="selling",0,false,false,0,0,null,null,1,0,false);
            buildings=buildings.Concat(new[]{new PlayableBuildingSnapshot(7,producerState=="enemy"?PlayableOwner.Enemy:PlayableOwner.Player,producer.Value,site.Position,producerState=="dead"?0:100,1,0,0,default,siteId:99,slotId:1,phase:producerState=="pending"?ConstructionPhase.Pending:ConstructionPhase.Ready,lifecycle:lifecycle)}).ToArray();
        }
        double credits=TerritoryRules.Cost(p,PlayableBuildingKind.GaussTower)-deficit;
        var view=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,(int)credits,null,Array.Empty<PlayableEntitySnapshot>(),buildings,Array.Empty<PlayableProjectileSnapshot>(),null,null,sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},exactCredits:credits,activeProfile:p);
        return OfflinePadWorldActions.Build(view,p,site.Id,defense?site.DefenseSlots(p).First().Id:site.Slots.First().Id,free);
    }
    [TestCase("none")] [TestCase("pending")] [TestCase("selling")] [TestCase("enemy")]
    public void EveryMissingConstructionConditionIsVisible(string science)
    {
        var action=Actions(150,science).Single(a=>a.Sector.Id.EndsWith(":GaussTower"));
        Assert.AreEqual("Нужен исследовательский центр\nНе хватает 150 кредитов",action.Presentation.Note);
        StringAssert.EndsWith(action.Presentation.Note,action.Detail);Assert.False(action.Sector.Enabled);Assert.IsNull(action.Command);
        var card=new RadialActionCard();card.Set(action,true,null);
        Assert.AreEqual(action.Presentation.Note,card.Q<Label>("radial-note").text);Assert.AreEqual(DisplayStyle.None,card.Q("radial-actions").style.display.value);
        Assert.IsNotEmpty(action.Presentation.Stats,"disabled card keeps real defense facts");
    }
    [TestCase(.25,"1 кредита")] [TestCase(2,"2 кредитов")] [TestCase(11,"11 кредитов")] [TestCase(21,"21 кредита")] [TestCase(24,"24 кредитов")]
    public void ExactDeficitUsesConciseWholeCredits(double deficit,string expected)
    {
        var action=Actions(deficit,"ready").Single(a=>a.Sector.Id.EndsWith(":GaussTower"));
        Assert.AreEqual("Не хватает "+expected,action.Presentation.Note);Assert.False(action.Sector.Enabled);Assert.IsNull(action.Command);
    }
    [Test] public void ConditionsResolveIndependentlyAndFreeEconomyKeepsScience()
    {
        OfflinePadAction Gauss(double deficit,string science,bool free=false)=>Actions(deficit,science,free).Single(a=>a.Sector.Id.EndsWith(":GaussTower"));
        Assert.AreEqual("Нужен исследовательский центр",Gauss(0,"none").Presentation.Note);
        Assert.AreEqual("Не хватает 150 кредитов",Gauss(150,"ready").Presentation.Note);
        var available=Gauss(0,"ready");Assert.True(available.Sector.Enabled);Assert.NotNull(available.Command);Assert.IsNull(available.Presentation.Note);
        var freeLocked=Gauss(400,"none",true);Assert.AreEqual("Нужен исследовательский центр",freeLocked.Presentation.Note);Assert.IsNull(freeLocked.Price);Assert.IsNull(freeLocked.Command);
        var freeReady=Gauss(400,"ready",true);Assert.True(freeReady.Sector.Enabled);Assert.NotNull(freeReady.Command);Assert.IsNull(freeReady.Price);Assert.IsNull(freeReady.Presentation.Note);
        Assert.AreEqual("Не хватает 300 кредитов",Actions(400,"none",defense:false).Single(a=>a.Sector.Id.EndsWith(":Factory")).Presentation.Note);
    }
    [TestCase(PlayableBuildingKind.Factory,"ready",true)] [TestCase(PlayableBuildingKind.Hangar,"ready",true)]
    [TestCase(PlayableBuildingKind.Factory,"pending",false)] [TestCase(PlayableBuildingKind.Hangar,"pending",false)]
    [TestCase(PlayableBuildingKind.Factory,"selling",false)] [TestCase(PlayableBuildingKind.Hangar,"selling",false)]
    [TestCase(PlayableBuildingKind.Factory,"enemy",false)] [TestCase(PlayableBuildingKind.Hangar,"enemy",false)]
    [TestCase(PlayableBuildingKind.Factory,"dead",false)] [TestCase(PlayableBuildingKind.Hangar,"dead",false)]
    public void ScienceUsesEitherOwnersReadyProducerAtAnySite(PlayableBuildingKind producer,string state,bool allowed)
    {
        var action=Actions(0,"none",defense:false,producer:producer,producerState:state).Single(a=>a.Sector.Id.EndsWith(":ScientificCenter"));
        Assert.AreEqual(allowed,action.Sector.Enabled);Assert.AreEqual(allowed,action.Command!=null);
        Assert.AreEqual(allowed?null:"Нужна фабрика или ангар",action.Presentation.Note);
    }
    [Test] public void ScienceShowsEveryBlockAndFreeEconomyStillNeedsProducer()
    {
        var action=Actions(150,"none",defense:false).Single(a=>a.Sector.Id.EndsWith(":ScientificCenter"));
        Assert.AreEqual("Нужна фабрика или ангар\nНе хватает 50 кредитов",action.Presentation.Note);Assert.False(action.Sector.Enabled);Assert.IsNull(action.Command);
        var free=Actions(400,"none",free:true,defense:false).Single(a=>a.Sector.Id.EndsWith(":ScientificCenter"));
        Assert.AreEqual("Нужна фабрика или ангар",free.Presentation.Note);Assert.False(free.Sector.Enabled);Assert.IsNull(free.Command);
        var poor=Actions(150,"none",defense:false,producer:PlayableBuildingKind.Hangar).Single(a=>a.Sector.Id.EndsWith(":ScientificCenter"));
        Assert.AreEqual("Не хватает 50 кредитов",poor.Presentation.Note);Assert.IsNull(poor.Command);
    }
    [Test] public void ConstructionLabelsAndLocksUpdateWithoutChangingOtherMenus()
    {
        var ring=new OrbitalBattleRing();ring.ConfigureInput(false);var locked=Actions(150,"none");ring.Set(locked,"Строительство",locked[2].Sector.Id);
        CollectionAssert.AreEqual(new[]{"Пулемётная","Ремонтная","Гаусс-башня"},ring.Query<Label>("radial-sector-title").ToList().Select(t=>t.text));
        Assert.AreEqual(1,ring.Query<VisualElement>("radial-sector-lock").ToList().Count(v=>v.style.display.value==DisplayStyle.Flex));
        var available=Actions(0,"ready");ring.Set(available,"Строительство",available[2].Sector.Id);
        Assert.True(ring.Query<VisualElement>("radial-sector-lock").ToList().All(v=>v.style.display.value==DisplayStyle.None));
        Assert.True(ring.Query<Label>("radial-sector-title").ToList().All(v=>v.style.display.value==DisplayStyle.Flex));
        var four=Actions(0,"none",defense:false);ring.Set(four,"Строительство",four[0].Sector.Id);
        CollectionAssert.AreEqual(new[]{"Фабрика","Переработка","Научный центр","Ангар"},ring.Query<Label>("radial-sector-title").ToList().Select(t=>t.text));
    }
}

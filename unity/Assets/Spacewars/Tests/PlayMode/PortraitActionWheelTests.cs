using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

public class PortraitActionWheelTests
{
    PlayableSnapshot Snapshot(PlayableBuildingSnapshot building,PlayableResearchOrderSnapshot[] research=null,int credits=1000)=>new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,Array.Empty<PlayableEntitySnapshot>(),new[]{building},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,ownerResearch:research,researchAvailability:Enum.GetValues(typeof(PlayableResearchKind)).Cast<PlayableResearchKind>().Select(k=>new PlayableResearchAvailabilitySnapshot(k,PlayableResearchRules.Cost(PlayableProfile.Default,k),true)).ToArray(),activeProfile:PlayableProfile.Default);
    PlayableBuildingSnapshot Factory()=>new PlayableBuildingSnapshot(7,PlayableOwner.Player,PlayableBuildingKind.Factory,default,100,1,0,0,default,lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false));
    [Test] public void VariantACardKeepsValuesAndClearsActionSpecificHints()
    {
        var actions=OfflinePadWorldActions.Building(Snapshot(Factory()),PlayableProfile.Default,7);
        var card=new RadialActionCard();var tank=actions.Single(a=>a.Sector.Id=="7:tank");
        card.Set(tank,true,null);
        Assert.AreEqual("100",card.Query<Label>("radial-stat-value").ToList()[0].text);
        Assert.AreEqual(tank.Presentation.Stats.Length,card.Query<Label>("radial-stat-label").ToList().Count);
        Assert.NotNull(card.Q("radial-hold-glyph"));Assert.NotNull(card.Q("radial-stick-glyph"));
        card.Set(actions.Single(a=>a.Sector.Id=="7:repair"),false,null);
        Assert.AreEqual(1,card.Query<Label>("radial-stat-value").ToList().Count);
        Assert.AreEqual("100 / 250",card.Q<Label>("radial-stat-value").text);
        Assert.IsNull(card.Q("radial-hold-glyph"));Assert.AreEqual(0,card.Query<NativeControllerGlyph>().ToList().Count);
        card.Set(actions.Single(a=>a.Sector.Id=="7:sale"),true,null);
        Assert.AreEqual("300 кредитов",card.Q<Label>("radial-stat-value").text);Assert.NotNull(card.Q("radial-hold-glyph"));
        card.Set(null,true,null);
        Assert.False(card.HasAction);Assert.AreEqual(0,card.Query<Label>("radial-stat-value").ToList().Count);Assert.IsNull(card.Q("radial-hold-glyph"));
        Assert.AreEqual(0,card.Query<Image>().ToList().Count,"card never duplicates the portrait");
    }
    [Test] public void QueuePortraitContourTracksUpgradeAndClearsWithEmptySlot()
    {
        var card=new Button();
        OrbitalPrecision.QueueCard(card,"tankHybrid",.3,true);
        var portrait=card.Q<Image>("order-portrait");var outline=card.Q<ModelPortrait>("order-artwork").Q<Image>("portrait-outline");
        Assert.AreSame(DockArtwork.TextureFor("tankHybrid-outline"),outline.image);Assert.NotNull(outline.image);
        Assert.AreEqual(portrait.scaleMode,outline.scaleMode);
        OrbitalPrecision.QueueCard(card,"tankHybridUpgraded",-1,true);
        Assert.AreSame(DockArtwork.TextureFor("tankHybridUpgraded"),portrait.image);
        Assert.AreSame(DockArtwork.TextureFor("tankHybridUpgraded-outline"),outline.image);Assert.NotNull(outline.image);
        OrbitalPrecision.QueueCard(card,"tankHybridUpgraded",-1,false);
        Assert.IsNull(portrait.image);Assert.IsNull(outline.image);
        Assert.AreEqual(DisplayStyle.None,outline.style.display.value);
        OrbitalPrecision.QueueCard(card,"factory",-1,true);
        Assert.AreSame(DockArtwork.TextureFor("factory-outline"),outline.image);Assert.NotNull(outline.image);
        Assert.AreEqual(DisplayStyle.Flex,outline.style.display.value);
    }
    [Test] public void CompletedUpgradeChangesProductionArtworkAndStatsButDoesNotInventPurchase()
    {
        var b=Factory();var upgrade=new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,8,0,30,30,false,true);
        var baseline=OfflinePadWorldActions.Building(Snapshot(b),PlayableProfile.Default,7).Single(a=>a.Sector.Id=="7:tank");
        var action=OfflinePadWorldActions.Building(Snapshot(b,new[]{upgrade},0),PlayableProfile.Default,7).Single(a=>a.Sector.Id=="7:tank");
        Assert.AreEqual("tankHybrid",baseline.Presentation.Artwork);Assert.AreEqual("tankHybridUpgraded",action.Presentation.Artwork);Assert.True(action.Presentation.Upgrade);
        Assert.AreNotEqual(baseline.Presentation.Stats.Single(s=>s.StartsWith("Скорость:")),action.Presentation.Stats.Single(s=>s.StartsWith("Скорость:")));
        Assert.IsNull(action.Command);Assert.NotNull(action.HoldCommand);StringAssert.Contains("Недостаточно",action.Presentation.Note);
        Assert.NotNull(DockArtwork.TextureFor(action.Presentation.Artwork));Assert.NotNull(DockArtwork.TextureFor(action.Presentation.Artwork+"-outline"));
    }
    [Test] public void SpecializationsPreviewRealUpgradeAndGeneralResearchKeepsLevelEmblems()
    {
        var b=new PlayableBuildingSnapshot(9,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,100,1,0,0,default,lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false));
        var actions=OfflinePadWorldActions.Building(Snapshot(b),PlayableProfile.Default,9);
        var chassis=actions.Single(a=>a.Sector.Id=="9:tank-chassis");Assert.AreEqual("tankHybridUpgraded",chassis.Presentation.Artwork);Assert.True(chassis.Presentation.Upgrade);Assert.True(chassis.Presentation.Stats.Any(s=>s.Contains("→")));
        Assert.AreEqual("weapon-systems-1",actions.Single(a=>a.Sector.Id.EndsWith(":ImprovedWeaponSystems")).Presentation.Artwork);
        Assert.AreEqual("defensive-systems-1",actions.Single(a=>a.Sector.Id.EndsWith(":ImprovedDefensiveSystems")).Presentation.Artwork);
        Assert.False(actions.Any(a=>a.Sector.Id.EndsWith(":AdvancedWeaponSystems")),"Existing prerequisites remain authoritative.");
        var complete=new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,9,0,30,30,false,true);
        Assert.False(OfflinePadWorldActions.Building(Snapshot(b,new[]{complete}),PlayableProfile.Default,9).Any(a=>a.Sector.Id==chassis.Sector.Id),"Completed research must not become a fake actionable sector.");
    }
    [Test] public void HawkProductionAndSpecializationUseDistinctActualVariantPortraits()
    {
        var hangar=new PlayableBuildingSnapshot(7,PlayableOwner.Player,PlayableBuildingKind.Hangar,default,100,1,0,0,default,lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false));
        var upgrade=new PlayableResearchOrderSnapshot(1,PlayableResearchKind.HawkSalvoSystems,9,0,30,30,false,true);
        var baseline=OfflinePadWorldActions.Building(Snapshot(hangar),PlayableProfile.Default,7).Single(a=>a.Sector.Id=="7:hawk");
        var improved=OfflinePadWorldActions.Building(Snapshot(hangar,new[]{upgrade}),PlayableProfile.Default,7).Single(a=>a.Sector.Id=="7:hawk");
        Assert.AreEqual("hawk",baseline.Presentation.Artwork);Assert.False(baseline.Presentation.Upgrade);
        Assert.AreEqual("hawkUpgraded",improved.Presentation.Artwork);Assert.True(improved.Presentation.Upgrade);
        var science=new PlayableBuildingSnapshot(9,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,100,1,0,0,default,lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false));
        var research=OfflinePadWorldActions.Building(Snapshot(science),PlayableProfile.Default,9).Single(a=>a.Sector.Id=="9:HawkSalvoSystems");
        Assert.AreEqual(improved.Presentation.Artwork,research.Presentation.Artwork);
        foreach(var key in new[]{baseline.Presentation.Artwork,improved.Presentation.Artwork}){
            Assert.NotNull(DockArtwork.TextureFor(key));Assert.NotNull(DockArtwork.TextureFor(key+"-outline"));
        }
        Assert.AreNotSame(DockArtwork.TextureFor("hawk"),DockArtwork.TextureFor("hawkUpgraded"));
    }
    [Test] public void NeutralClearsDetailsAndPreservesOnlySelectionFooter()
    {
        var ring=new OrbitalBattleRing();ring.ConfigureInput(true);var actions=OfflinePadWorldActions.Building(Snapshot(Factory()),PlayableProfile.Default,7);
        ring.Set(actions,"Фабрика","7:tank");Assert.True(ring.Q<RadialActionCard>().HasAction);
        ring.Set(actions,"Фабрика",null);Assert.False(ring.Q<RadialActionCard>().HasAction);
        Assert.NotNull(ring.Q("radial-footer"));Assert.AreEqual(0,ring.Q<RadialActionCard>().Query<Image>().ToList().Count,"card never duplicates model");
    }
    [Test] public void UtilityCardsUsePublishedRepairAndSaleConsequences()
    {
        var actions=OfflinePadWorldActions.Building(Snapshot(Factory()),PlayableProfile.Default,7);
        var repair=actions.Single(a=>a.Sector.Id=="7:repair");Assert.True(repair.Sector.Enabled);StringAssert.Contains("100 /",repair.Presentation.Stats.Single());Assert.False(repair.Presentation.Note.Contains("исправно"));
        var sale=actions.Single(a=>a.Sector.Id=="7:sale");Assert.IsNull(sale.Command);Assert.NotNull(sale.HoldCommand);Assert.AreEqual("Возврат: 300 кредитов",sale.Presentation.Stats.Single());Assert.IsNull(sale.Presentation.Note);
        var rally=actions.Single(a=>a.Sector.Id=="rally");Assert.IsNull(rally.Command);StringAssert.Contains("Новые юниты",rally.Presentation.Description);
    }
}

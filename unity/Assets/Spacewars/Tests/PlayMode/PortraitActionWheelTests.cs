using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Runtime;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;

public class PortraitActionWheelTests
{
    PlayableSnapshot Snapshot(PlayableBuildingSnapshot building,PlayableResearchOrderSnapshot[] research=null,int credits=1000)=>new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,Array.Empty<PlayableEntitySnapshot>(),new[]{building},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,ownerResearch:research,researchAvailability:Enum.GetValues(typeof(PlayableResearchKind)).Cast<PlayableResearchKind>().Select(k=>new PlayableResearchAvailabilitySnapshot(k,PlayableResearchRules.Cost(PlayableProfile.Default,k),true)).ToArray(),activeProfile:PlayableProfile.Default);
    PlayableBuildingSnapshot Factory()=>new PlayableBuildingSnapshot(7,PlayableOwner.Player,PlayableBuildingKind.Factory,default,100,1,0,0,default,lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false));
    [Test] public void PortraitUpgradeChevronsResetWhenArtworkChanges()
    {
        var art=new RadialArtwork();
        foreach(var key in new[]{"factoryUpgraded","hangarUpgraded","refineryUpgraded","scientificCenterUpgraded","tankHybridUpgraded","explorerUpgraded","shkval-guidance-3","hawkUpgraded"})
        {
            art.Set(key,true);Assert.AreEqual(DisplayStyle.Flex,art.Q<PortraitUpgradeMark>().style.display.value);
            Assert.AreEqual(DisplayStyle.None,art.Q<Label>("upgrade-mark").style.display.value);
            art.Set(key,true,available:false);Assert.False(art.Q<PortraitUpgradeMark>().Available);
            art.Set(key,false);Assert.AreEqual(DisplayStyle.None,art.Q<PortraitUpgradeMark>().style.display.value);
        }
        art.Set("tankHybrid");Assert.AreEqual(DisplayStyle.None,art.Q<Label>("upgrade-mark").style.display.value);
        Assert.AreEqual(DisplayStyle.None,art.Q<PortraitUpgradeMark>().style.display.value);
        art.Set("weapon-systems-1");Assert.AreEqual(DisplayStyle.None,art.Q<Label>("upgrade-mark").style.display.value);
    }
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
    [Test] public void UpgradePortraitFramingEnlargesVisibleModelsAndPreservesOutline()
    {
        foreach(var key in new[]{"tankHybridUpgraded","explorerUpgraded","shkval-guidance-3","hawkUpgraded","scientificCenterUpgraded"})
        {
            var frame=PortraitFraming.UvFor(key);Assert.Greater(frame.width,0);Assert.Less(frame.width,1);Assert.Greater(frame.height,0);Assert.Less(frame.height,1);
            var portrait=new ModelPortrait();portrait.Set(key,fitContent:true);
            Assert.AreEqual(frame,portrait.Q<Image>("model-portrait").uv);Assert.AreEqual(frame,portrait.Q<Image>("portrait-outline").uv);
            Assert.AreSame(DockArtwork.TextureFor(key),portrait.Q<Image>("model-portrait").image);
            portrait.Set(key);Assert.AreEqual(new Rect(0,0,1,1),portrait.Q<Image>("model-portrait").uv);
        }
    }
    [Test] public void NeutralClearsDetailsAndPreservesOnlySelectionFooter()
    {
        var ring=new OrbitalBattleRing();ring.ConfigureInput(true);var actions=OfflinePadWorldActions.Building(Snapshot(Factory()),PlayableProfile.Default,7);
        ring.Set(actions,"Фабрика","7:tank");Assert.True(ring.Q<RadialActionCard>().HasAction);
        ring.Set(actions,"Фабрика",null);Assert.False(ring.Q<RadialActionCard>().HasAction);
        Assert.NotNull(ring.Q("radial-footer"));Assert.AreEqual(0,ring.Q<RadialActionCard>().Query<Image>().ToList().Count,"card never duplicates model");
    }
    [UnityTest] public IEnumerator FactoryAndHangarShowProductionPortraitsAtTheSameScale()
    {
        var host=new GameObject("Producer portrait framing");
        var panel=ScriptableObject.CreateInstance<PanelSettings>();
        panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");
        panel.scaleMode=PanelScaleMode.ConstantPixelSize;panel.scale=1;
        var target=new RenderTexture(940,500,24);
        target.Create();
        panel.targetTexture=target;
        try
        {
            var document=host.AddComponent<UIDocument>();document.panelSettings=panel;
            var root=document.rootVisualElement;OrbitalTheme.Install(root);
            root.style.backgroundColor=OrbitalTheme.Background;
            var turns=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Hangar};
            var wheels=new System.Collections.Generic.List<(OrbitalBattleRing Ring,OfflinePadAction[] Actions,string Title,string Glyph)>();
            var research=new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ShkvalGuidance,PlayableResearchKind.HawkSalvoSystems}
                .Select((kind,index)=>new PlayableResearchOrderSnapshot(index+1,kind,9,0,30,30,false,true)).ToArray();
            for(int i=0;i<turns.Length;i++)
            {
                var kind=turns[i];
                var building=new PlayableBuildingSnapshot(7,PlayableOwner.Player,kind,default,100,1,0,0,default,
                    lifecycle:new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false));
                var actions=OfflinePadWorldActions.Building(Snapshot(building,research),PlayableProfile.Default,7);
                var ring=new OrbitalBattleRing();
                ring.style.left=46+i*450;ring.style.top=50;ring.style.display=DisplayStyle.Flex;
                string title=kind==PlayableBuildingKind.Factory?"Фабрика":"Ангар";
                string glyph=OrbitalPrecision.BuildingGlyph(kind);
                ring.ConfigureInput(true);ring.Resize(360);
                ring.Set(actions,title,contextGlyph:glyph);
                root.Add(ring);
                wheels.Add((ring,actions,title,glyph));
                if(kind==PlayableBuildingKind.Factory)
                    Assert.AreEqual("cerberus",actions.Last(a=>a.Sector.Hold=="repeat").Presentation.Artwork);
                Assert.True(actions.Any(a=>a.Sector.Hold=="repeat"&&a.Presentation.Upgrade));
                Assert.True(actions.Any(a=>a.Sector.Hold=="repeat"&&!a.Presentation.Upgrade));
            }
            var unitKeys=wheels.SelectMany(wheel=>wheel.Actions)
                .Where(action=>action.Sector.Hold=="repeat").Select(action=>action.Presentation.Artwork);
            CollectionAssert.IsSubsetOf(new[]{"igla","cerberus","skat"},unitKeys.ToArray());
            void AssertProductionScale(float expected)
            {
                foreach(var (ring,actions,_,_) in wheels)
                foreach(var action in actions.Where(a=>a.Sector.Hold=="repeat"))
                {
                    var petal=ring.Q<Button>("radial-"+action.Sector.Id);
                    var artwork=petal.Q<RadialArtwork>();
                    var image=artwork.Q<Image>("model-portrait");
                    var outline=artwork.Q<Image>("portrait-outline");
                    string key=action.Presentation.Artwork;
                    Assert.IsNotNull(image.image,key);
                    Assert.AreEqual(key,image.image.name);
                    Assert.IsNotNull(outline.image,key);
                    Assert.AreEqual(key+"-outline",outline.image.name);
                    Assert.AreEqual(image.uv,outline.uv);
                    Assert.That(image.uv.width,Is.InRange(.1f,.99f),action.Presentation.Artwork);
                    Assert.That(image.uv.width*image.image.width,Is.EqualTo(image.uv.height*image.image.height));
                    Assert.That(image.worldBound.width,Is.EqualTo(expected).Within(.5f));
                    Assert.That(image.worldBound.height,Is.EqualTo(expected).Within(.5f));
                    Assert.False(artwork.worldBound.Overlaps(petal.Q<Label>("radial-sector-count").worldBound),
                        "The normalized unit portrait must leave the price clear.");
                }
            }
            for(int i=0;i<5;i++)yield return null;
            float standard=RadialCardProfile.Load().compositionSize;
            AssertProductionScale(standard);
            var output=Environment.GetEnvironmentVariable("SPACEWARS_PRODUCER_PORTRAIT_EVIDENCE");
            if(!string.IsNullOrEmpty(output))
            {
                Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null,SystemInfo.graphicsDeviceType);
                Directory.CreateDirectory(output);
                var old=RenderTexture.active;RenderTexture.active=target;
                var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try
                {
                    pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                    File.WriteAllBytes(Path.Combine(output,"factory-hangar.png"),pixels.EncodeToPNG());
                }
                finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(pixels);}
            }
            foreach(var (ring,actions,title,glyph) in wheels)
            {
                ring.Resize(200);
                var selected=actions.First(a=>a.Sector.Hold=="repeat").Sector.Id;
                ring.Set(actions,title,selected,contextGlyph:glyph);
            }
            for(int i=0;i<3;i++)yield return null;
            AssertProductionScale(Mathf.Round(standard*48f/70f));
        }
        finally
        {
            panel.targetTexture=null;target.Release();
            UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);UnityEngine.Object.DestroyImmediate(target);
        }
    }
    [Test] public void ProductionCropAppliesAndClearsForNewUnitsAndRocketExplorer()
    {
        foreach(var key in new[]{"explorerRocket","skat","cerberus","igla"})
        {
            var artwork=new RadialArtwork();
            artwork.Set(key,fitProductionPortrait:true);
            var image=artwork.Q<Image>("model-portrait");
            var outline=artwork.Q<Image>("portrait-outline");
            Assert.IsNotNull(outline.image);
            Assert.AreEqual(image.uv,outline.uv);
            Assert.IsNotNull(image.image);
            Assert.That(image.uv.width,Is.InRange(.1f,.99f));
            Assert.That(image.uv.width,Is.EqualTo(image.uv.height));
            artwork.Set(key);
            Assert.AreEqual(new Rect(0,0,1,1),image.uv);
        }
    }

    [Test] public void UtilityCardsUsePublishedRepairAndSaleConsequences()
    {
        var actions=OfflinePadWorldActions.Building(Snapshot(Factory()),PlayableProfile.Default,7);
        var repair=actions.Single(a=>a.Sector.Id=="7:repair");Assert.True(repair.Sector.Enabled);StringAssert.Contains("100 /",repair.Presentation.Stats.Single());Assert.False(repair.Presentation.Note.Contains("исправно"));
        var sale=actions.Single(a=>a.Sector.Id=="7:sale");Assert.IsNull(sale.Command);Assert.NotNull(sale.HoldCommand);Assert.AreEqual("Возврат: 300 кредитов",sale.Presentation.Stats.Single());Assert.IsNull(sale.Presentation.Note);
        var rally=actions.Single(a=>a.Sector.Id=="rally");Assert.IsNull(rally.Command);StringAssert.Contains("Новые юниты",rally.Presentation.Description);
    }
}

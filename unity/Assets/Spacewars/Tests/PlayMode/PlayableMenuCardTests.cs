using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class PlayableMenuCardTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap hud;PlayableProfile profile;
    void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(hud,value);
    T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,F).GetValue(hud);
    object Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethod(name,F).Invoke(hud,args);
    [SetUp] public void Setup()
    {
        // Use the shipping profile for visual cards; other tests deliberately vary its Lab fields.
        profile=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
        host=new GameObject("menu cards");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;Set("profile",profile);Call("CreateHud");
    }
    [TearDown] public void Cleanup()=>UnityEngine.Object.DestroyImmediate(host);
    PlayableSnapshot View(PlayableBuildingSnapshot[] buildings=null,TerritorySiteSnapshot[] sites=null,int credits=10000,PlayableResearchOrderSnapshot[] research=null)
        =>new PlayableSnapshot(profile.ProfileId,profile.Revision,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,credits,null,Array.Empty<PlayableEntitySnapshot>(),buildings??Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),null,null,sites:sites,ownerResearch:research,activeProfile:profile);
    PlayableBuildingSnapshot Building(PlayableBuildingKind kind,bool selling=false)=>new PlayableBuildingSnapshot(7,PlayableOwner.Player,kind,default,TerritoryRules.Health(profile,kind),1,0,0,default,hasRally:true,lifecycle:new PlayableBuildingLifecycleSnapshot(selling,0,false,false,0,0,null,null,1,100,false));
    [Test] public void OnlyLiveProducersOfferPadRallyAndMouseNeverOffersIt()
    {
        foreach(PlayableBuildingKind kind in Enum.GetValues(typeof(PlayableBuildingKind)))
        foreach(bool controller in new[]{false,true})
        {
            Set("view",View(new[]{Building(kind)}));Set("battleController",controller);Get<HashSet<int>>("selection").Clear();Get<HashSet<int>>("selection").Add(7);
            var actions=(OfflinePadAction[])Call("CurrentBattleActions");bool producer=kind==PlayableBuildingKind.Factory||kind==PlayableBuildingKind.Hangar;
            Assert.AreEqual(controller&&producer,actions.Any(a=>a.Sector.Id=="rally"),kind+" controller="+controller);
            Assert.AreEqual(producer,Call("SelectedRallyProducer")!=null,kind.ToString());
        }
        Set("view",View(new[]{Building(PlayableBuildingKind.Factory,true)}));Assert.IsNull(Call("SelectedRallyProducer"));
        Assert.False(OfflinePadWorldActions.Building(View(new[]{Building(PlayableBuildingKind.Factory,true)}),profile,7).Any(a=>a.Sector.Id=="rally"));
    }
    [Test] public void NonProducingBuildingsNeverRenderPlayerRallyFlags()
    {
        var world=new PlayableWorld(host.transform,profile);
        try
        {
            foreach(var kind in new[]{PlayableBuildingKind.Headquarters,PlayableBuildingKind.Outpost,PlayableBuildingKind.Mine,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter})
            {
                world.RenderRallyFlags(View(new[]{Building(kind)}),new HashSet<int>{7});Assert.IsEmpty(world.RallyFlags,kind.ToString());
                Set("view",View(new[]{Building(kind)}));Get<HashSet<int>>("selection").Add(7);Call("BeginRallyPlacement",true);Assert.False(Get<bool>("battleRally"));
            }
        }
        finally{world.Dispose();}
    }
    [TestCase(PlayableBuildingKind.Mine)]
    [TestCase(PlayableBuildingKind.Outpost)]
    [TestCase(PlayableBuildingKind.Headquarters)]
    public void CapturedSiteHasOnePricedActionWithIncomeAndUnavailableReason(PlayableBuildingKind kind)
    {
        var site=new TerritorySite(4,kind,new NavPoint(10,10),Array.Empty<TerritorySlot>());
        foreach(int credits in new[]{0,10000})
        {
            var actions=OfflinePadWorldActions.Build(View(sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,null,1,false,0,false)},credits:credits),profile,4,0);
            Assert.AreEqual(1,actions.Length);Assert.True(OrbitalBattleRing.IsCapturedSiteBuild(actions));Assert.AreEqual(TerritoryRules.Cost(profile,kind),actions[0].Price);
            StringAssert.Contains("Доход:",actions[0].Detail);StringAssert.Contains("Постройка:",actions[0].Detail);
            Assert.AreEqual(credits>0,actions[0].Command!=null);
            CollectionAssert.AreEqual(credits==0?new[]{"Не хватает "+TerritoryRules.Cost(profile,kind)+" кредитов"}:Array.Empty<string>(),actions[0].BlockedReasons);
            if(credits==0)StringAssert.EndsWith(actions[0].BlockedReasons[0],actions[0].Detail);
        }
        Assert.True(OrbitalBattleRing.DiscContains(new Vector2(66,66),132),"The formerly empty centre is clickable.");
        Assert.False(OrbitalBattleRing.DiscContains(Vector2.zero,132),"Corners pass world input.");
        Assert.False(OrbitalBattleRing.SectorContains(new Vector2(66,66),132,0,1),"Ordinary annular controls retain their geometry.");
    }
    [Test] public void LabChangesDriveIncomePriceAndUnitStatsInsteadOfCopiedNumbers()
    {
        var data=profile.CopyData();data.mineIncomePerPeriod=77;data.incomePeriodSeconds=7;data.mineCreditCost=789;data.tankWeaponDamage=123;data.tankSpeed=9.3;
        var changed=PlayableProfile.Create(data);string mine=PlayableMenuDescriptions.Building(changed,PlayableBuildingKind.Mine);
        StringAssert.Contains("+77 кредитов каждые 7 с",mine);StringAssert.Contains("789 кредитов",mine);
        string tank=PlayableMenuDescriptions.Unit(changed,PlayableEntityKind.Tank);
        StringAssert.Contains("Урон за попадание: 123",tank);StringAssert.Contains(data.tankSpeed.ToString("0.##")+" м/с",tank);
        Assert.AreNotEqual(PlayableMenuDescriptions.Unit(profile,PlayableEntityKind.Tank),tank);
        foreach(PlayableBuildingKind kind in Enum.GetValues(typeof(PlayableBuildingKind)))
        {
            string text=PlayableMenuDescriptions.Building(changed,kind);StringAssert.Contains("Здоровье:",text);StringAssert.Contains("Стоимость:",text);
            Assert.AreEqual(TerritoryRules.Income(changed,kind)>0,text.Contains("Доход:"),kind.ToString());
        }
    }
    [Test] public void UnitCardsDescribeEveryKindBeforeStatsAndReflectCompletedResearch()
    {
        var kinds=new[]{PlayableResearchKind.TankChassis,PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ShkvalGuidance,PlayableResearchKind.HawkSalvoSystems,PlayableResearchKind.AdvancedWeaponSystems,PlayableResearchKind.AdvancedDefensiveSystems};
        var research=kinds.Select((k,i)=>new PlayableResearchOrderSnapshot(i+1,k,10,1,1,0,false,true)).ToArray();
        foreach(PlayableEntityKind kind in Enum.GetValues(typeof(PlayableEntityKind)))
        {
            string card=PlayableMenuDescriptions.Unit(profile,kind,research),baseline=PlayableMenuDescriptions.Unit(profile,kind);
            Assert.Greater(card.IndexOf("Здоровье:",StringComparison.Ordinal),card.IndexOf('\n')+10);
            foreach(string field in new[]{"Урон за попадание:","Дальность огня:","Скорость:","Обзор:","Стоимость:","Производство:","Место в армии:"})StringAssert.Contains(field,card);
            Assert.AreNotEqual(baseline,card,kind.ToString());
            StringAssert.Contains((PlayableUnitRules.Health(profile,kind)*(1+profile.DefensiveSystemsLevel2BonusPercent/100)).ToString("0.##"),card);
        }
        var entity=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,default,73,false,0,0,0,maxHealth:400,exactHealth:72.5,weaponSystemsLevel:2);
        StringAssert.Contains("Здоровье: "+72.5.ToString("0.##")+" / 400",PlayableMenuDescriptions.Unit(profile,entity.Kind,entity:entity));
        var queue=OfflinePadWorldActions.Building(View(new[]{Building(PlayableBuildingKind.Factory)},research:research),profile,7,false);
        foreach(var action in queue.Where(a=>a.Sector.Hold=="repeat"))StringAssert.Contains("Урон за попадание:",action.Detail);
    }
    [UnityTest] public IEnumerator MineAndUnitCardsRenderWithFilledCentreAndBoundedDetails()
    {
        var root=Get<VisualElement>("root");root.Clear();root.style.width=1280;root.style.height=1200;root.style.backgroundColor=OrbitalTheme.Background;
        var panel=host.GetComponent<UIDocument>().panelSettings;panel.scale=1;var texture=new RenderTexture(1280,1200,24);texture.Create();panel.targetTexture=texture;
        try
        {
            var mine=new OrbitalBattleRing();root.Add(mine);mine.Resize(132);mine.style.left=32;mine.style.top=64;mine.style.display=DisplayStyle.Flex;
            var mineAction=new OfflinePadAction("build:4:0:Mine","Построить: Шахта",true,PlayableMenuDescriptions.Building(profile,PlayableBuildingKind.Mine)){Price=profile.MineCreditCost};
            mine.Set(new[]{mineAction},"Строительство",mineAction.Sector.Id);mine.PositionDetail(false,400,1200,64);
            var note=new Label("Unity PlayMode · активный профиль лаборатории · без игрового мира");note.style.color=OrbitalTheme.Ink;note.style.fontSize=17;note.style.position=Position.Absolute;note.style.left=32;note.style.top=20;root.Add(note);
            var rings=new List<OrbitalBattleRing>{mine};
            for(int i=0;i<4;i++)
            {
                var kind=new[]{PlayableEntityKind.Explorer,PlayableEntityKind.Tank,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk}[i];
                var ring=new OrbitalBattleRing();root.Add(ring);ring.Resize(170);ring.style.left=i%2*620+32;ring.style.top=350+i/2*420;ring.style.display=DisplayStyle.Flex;
                var action=new OfflinePadAction("7:"+kind.ToString().ToLowerInvariant(),"Произвести: "+OfflinePadWorldActions.Name(kind),true,PlayableMenuDescriptions.Unit(profile,kind)){Price=PlayableUnitRules.Cost(profile,kind)};
                ring.Set(new[]{action},"Производство",action.Sector.Id);ring.PositionDetail(false,420,1200,350+i/2*420);rings.Add(ring);
            }
            yield return null;yield return null;yield return null;
            foreach(var ring in rings)
            {
                var detail=ring.Q<Label>("radial-detail");Assert.Greater(detail.resolvedStyle.height,50);
                Assert.GreaterOrEqual(detail.worldBound.yMin,0);Assert.LessOrEqual(detail.worldBound.yMax,1200);
            }
            Assert.True(mine.SingleCapturedSiteButton);var button=mine.Q<Button>("radial-"+mineAction.Sector.Id);
            Assert.True(button.ContainsPoint(new Vector2(66,66)));int invoked=0;mine.InvokeAction=(_,hold)=>{Assert.False(hold);invoked++;};button.Focus();
            using(var submit=NavigationSubmitEvent.GetPooled()){submit.target=button;button.SendEvent(submit);}Assert.AreEqual(1,invoked);
            var previous=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(1280,1200,TextureFormat.RGBA32,false);
            try
            {
                pixels.ReadPixels(new Rect(0,0,1280,1200),0,0);pixels.Apply();
                var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/menu-cards"));Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"menu-cards.png"),pixels.EncodeToPNG());
                foreach(var child in button.Children())child.style.visibility=Visibility.Hidden;
                RenderTexture.active=previous;yield return null;yield return null;yield return null;RenderTexture.active=texture;
                pixels.ReadPixels(new Rect(0,0,1280,1200),0,0);pixels.Apply();
                var centre=pixels.GetPixel(98,1199-130);var outside=pixels.GetPixel(34,1199-66);
                Assert.Greater(centre.b,outside.b+.01f,"The build button has a filled centre.");
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
        }
        finally{panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
    }
    [UnityTest] public IEnumerator CapturedSitesRenderMatchingRoundButtons()
    {
        var root=Get<VisualElement>("root");root.Clear();root.style.width=1280;root.style.height=440;root.style.backgroundColor=OrbitalTheme.Background;
        var panel=host.GetComponent<UIDocument>().panelSettings;panel.scale=1;var texture=new RenderTexture(1280,440,24);texture.Create();panel.targetTexture=texture;
        var rings=new List<OrbitalBattleRing>();
        try
        {
            var note=new Label("Захваченные точки · Unity PlayMode · значения активного профиля лаборатории");note.style.color=OrbitalTheme.Ink;note.style.fontSize=17;note.style.position=Position.Absolute;note.style.left=20;note.style.top=20;root.Add(note);
            var kinds=new[]{PlayableBuildingKind.Mine,PlayableBuildingKind.Outpost,PlayableBuildingKind.Headquarters};
            for(int i=0;i<kinds.Length;i++)
            {
                var kind=kinds[i];var site=new TerritorySite(i+1,kind,default,Array.Empty<TerritorySlot>());
                var actions=OfflinePadWorldActions.Build(View(sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,null,1,false,0,false)}),profile,i+1,0);
                Assert.AreEqual(1,actions.Length);Assert.AreEqual(TerritoryRules.Cost(profile,kind),actions[0].Price);
                var ring=new OrbitalBattleRing();root.Add(ring);ring.Resize(132);ring.style.left=20+i*420;ring.style.top=86;ring.style.display=DisplayStyle.Flex;
                ring.Set(actions,"Строительство",actions[0].Sector.Id);ring.PositionDetail(false,280,440,86);rings.Add(ring);
                Assert.True(ring.SingleCapturedSiteButton);Assert.True(ring.Q<Button>().ContainsPoint(new Vector2(66,66)));
                Assert.True(ring.Query<Label>().ToList().Any(l=>l.text=="Построить\n"+OfflinePadWorldActions.Name(kind)),kind.ToString());
            }
            yield return null;yield return null;yield return null;
            foreach(var ring in rings)
            {
                var detail=ring.Q<Label>("radial-detail");Assert.GreaterOrEqual(detail.worldBound.xMin,0);Assert.LessOrEqual(detail.worldBound.xMax,1280);Assert.LessOrEqual(detail.worldBound.yMax,440);
            }
            var previous=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(1280,440,TextureFormat.RGBA32,false);
            try
            {
                pixels.ReadPixels(new Rect(0,0,1280,440),0,0);pixels.Apply();
                var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/captured-site-buttons"));Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"buttons.png"),pixels.EncodeToPNG());
                // Test the painted disc itself: a correctly centred portrait may have a dark centre pixel.
                foreach(var ring in rings)foreach(var child in ring.Q<Button>().Children())child.style.visibility=Visibility.Hidden;
                RenderTexture.active=previous;yield return null;yield return null;yield return null;RenderTexture.active=texture;
                pixels.ReadPixels(new Rect(0,0,1280,440),0,0);pixels.Apply();
                for(int i=0;i<3;i++)Assert.Greater(pixels.GetPixel(86+i*420,439-152).b,pixels.GetPixel(22+i*420,439-88).b+.01f,"Every captured-site button has a filled centre.");
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
        }
        finally{panel.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
    }

}

using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
public sealed class ProvingGroundContractTests
{
    private static void Destroy(OfflineParticipantAuthority authority)
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var domain=typeof(OfflineParticipantAuthority).GetField("domain",flags).GetValue(authority);
        authority.Navigation.ReleaseCommittedHostRoot();
        domain.GetType().GetMethod("DestroyOwnedNavigation",flags).Invoke(domain,null);
    }
    [Test] public void PlacementResearchCostsAndResetUseIndependentSnapshot()
    {
        var data=PlayableProfile.Default.CopyData();data.tankCreditCost=321;
        var setup=new ProvingGroundSetup(PlayableProfile.Create(data));data.tankCreditCost=999;
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(-10,0)));
        Assert.NotNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(10,0)));
        Assert.NotNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(-10,0)));
        var slot=setup.Profile.AuthoredMap.Sites(setup.Profile)[0].Slots[0];
        Assert.NotNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,slot.Position));
        Assert.IsNull(setup.PlaceBuilding(0,PlayableBuildingKind.Factory,slot.Position));
        Assert.NotNull(setup.PlaceBuilding(0,PlayableBuildingKind.Factory,slot.Position));
        var right=setup.Profile.AuthoredMap.Sites(setup.Profile)[1].Slots[0];Assert.IsNull(setup.PlaceBuilding(1,PlayableBuildingKind.ScientificCenter,right.Position));
        setup.SetResearch(0,PlayableResearchKind.AdvancedDefensiveSystems,true);setup.SetResearch(0,PlayableResearchKind.TankChassis,true);
        Assert.AreEqual(321,setup.ArmyCost(0));Assert.AreEqual(0,setup.ArmyCost(1));Assert.AreEqual(0,setup.ResearchCost(1));
        Assert.AreEqual(setup.Profile.DefensiveSystemsLevel1Cost+setup.Profile.DefensiveSystemsLevel2Cost+setup.Profile.TankChassisCost,setup.ResearchCost(0));
        var config=setup.Configuration();var a=new OfflineParticipantAuthority(config,82);
        try{
            var v=a.View("player-1");Assert.AreEqual(10000,v.Credits);Assert.AreEqual(8,v.Buildings.Count);Assert.AreEqual(3,v.OwnerResearch.Count);Assert.True(v.Entities.Single().Upgraded);
            Assert.AreEqual(setup.Profile.TankHealth*(1+setup.Profile.DefensiveSystemsLevel2BonusPercent/100),v.Entities.Single().MaxHealth,1e-6);
            Assert.AreEqual(0,a.View("enemy-1").OwnerResearch.Count);
            setup.SetResearch(0,PlayableResearchKind.ImprovedDefensiveSystems,false);Assert.False(setup.Research(0).Contains(PlayableResearchKind.AdvancedDefensiveSystems));Assert.AreEqual(3,config.ProvingGround.Research(0).Count,"Running recipe is a snapshot");
            var map=(ProvingGroundMap)setup.Profile.AuthoredMap;Assert.Greater(map.AttackDestination(PlayableOwner.Player).X,map.Headquarters(PlayableOwner.Enemy).X);Assert.Less(map.AttackDestination(PlayableOwner.Enemy).X,map.Headquarters(PlayableOwner.Player).X);
        }finally{Destroy(a);}
        var reset=new OfflineParticipantAuthority(config,83);try{Assert.AreEqual(0,reset.Tick);Assert.AreEqual(10000,reset.View("player-1").Credits);Assert.AreEqual(3,reset.View("player-1").OwnerResearch.Count);Assert.AreEqual(new NavPoint(-10,0),reset.View("player-1").Entities.Single().Position);}finally{Destroy(reset);}
    }
    [UnityTest] public IEnumerator LaboratoryLaunchRetainsUnsavedDraftWithoutWritingVersion()
    {
        var folder=Path.Combine(Application.temporaryCachePath,"proving-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var host=new GameObject("Proving lab contract");var panel=ScriptableObject.CreateInstance<PanelSettings>();panel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FoundationTheme");var doc=host.AddComponent<UIDocument>();doc.panelSettings=panel;var root=doc.rootVisualElement;OrbitalTheme.Install(root);var nav=new NativeMenuNavigation(root);
        try{
            var store=new NativeBalanceStore(PlayableProfile.Default,Path.Combine(folder,"balance.json"));PlayableProfile launched=null;
            var lab=new NativeBalanceView(root,nav,store,()=>{},openProvingGround:(p,a)=>launched=p);yield return null;
            var field=lab.Page.Q<TextField>("lab-field-tankHealth");Assert.NotNull(field);field.value="222";
            int versions=store.Versions.Count;string contents=File.Exists(store.StoragePath)?File.ReadAllText(store.StoragePath):null;
            lab.Page.Q<Button>("lab-proving-ground").Focus();nav.Activate();Assert.NotNull(launched);Assert.AreEqual(222,launched.TankHealth);
            lab.Page.style.display=DisplayStyle.None;lab.RestoreFromProvingGround();yield return null;
            Assert.AreEqual("222",field.value);Assert.True(lab.Page.Q<Button>("lab-save").enabledSelf);Assert.AreEqual(versions,store.Versions.Count);Assert.AreEqual(contents,File.Exists(store.StoragePath)?File.ReadAllText(store.StoragePath):null);
            field.value="223";lab.Page.Q<Button>("lab-proving-ground").Focus();nav.Activate();Assert.AreEqual(223,launched.TankHealth);
            field.value="invalid";lab.Page.Q<Button>("lab-proving-ground").Focus();nav.Activate();Assert.AreEqual(223,launched.TankHealth,"Invalid draft must not launch");
        }finally{nav.Dispose();UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(panel);Directory.Delete(folder,true);}
    }
}

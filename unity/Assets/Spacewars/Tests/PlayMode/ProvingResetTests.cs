using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class ProvingResetTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap hud;ProvingGroundSetup original;RenderTexture target;
    void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
    T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
    object Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(Flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(hud,args);
    IEnumerator Until(Func<bool> condition)
    {
        float deadline=Time.realtimeSinceStartup+30;
        while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.True(condition(),"Proving rebuild must finish through the actual UI route");
    }
    [UnitySetUp] public IEnumerator Setup()
    {
        var data=PlayableProfile.Default.CopyData();data.tankHealth=225;
        original=new ProvingGroundSetup(PlayableProfile.Create(data));
        for(int side=0;side<2;side++)
        {
            Assert.IsNull(original.PlaceUnit(side,PlayableEntityKind.Tank,new NavPoint(side==0?-12:12,-9)));
            var site=original.Profile.AuthoredMap.Sites(original.Profile)[side];
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.ScientificCenter,PlayableBuildingKind.Hangar};
            for(int i=0;i<kinds.Length;i++){Assert.IsNull(original.PlaceBuilding(side,kinds[i],site.Slots[i].Position));Assert.IsNull(original.UpgradeBuilding(site.Id,site.Slots[i].Id));}
            original.SetResearch(side,PlayableResearchKind.ExplorerRocketLauncher,true);
            original.SetResearch(side,PlayableResearchKind.UltimateWeaponSystems,true);
            original.SetResearch(side,PlayableResearchKind.UltimateDefensiveSystems,true);
            original.SetResearch(side,PlayableResearchKind.AdvancedReinforcedConcrete,true);
        }
        host=new GameObject("proving reset UI");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        Set("profile",original.Profile);Set("provingGround",original);
        Set("provingGroundAudio",JsonUtility.FromJson<GameplayAudioProfile>(Resources.Load<TextAsset>("GameplayAudioProfile").text));
        Call("CreateWorld");Call("CreateHud");var input=host.AddComponent<PlayableInput>();input.enabled=false;Set("input",input);
        var root=Get<VisualElement>("root");var loading=OrbitalTheme.Loading(root,"Подготовка полигона…");loading.style.display=DisplayStyle.None;Set("loadingScreen",loading);
        var runtime=new PlayableRuntime(original.Configuration(),111,startPaused:true);Set("runtime",runtime);Set("generation",111L);
        yield return Until(()=>runtime.IsPrepared&&runtime.Latest!=null);Set("view",runtime.Latest);Set("inLobby",false);
        var tabs=Get<string[]>("provingTabs");tabs[0]=tabs[1]="Исследования";Call("CreateProvingGroundUi");Call("UpdateHud");
        var panel=host.GetComponent<UIDocument>().panelSettings;target=new RenderTexture(1600,1000,0);target.Create();panel.targetTexture=target;
        root.style.width=1600/panel.scale;root.style.height=1000/panel.scale;root.style.backgroundColor=OrbitalTheme.Background;
        for(int i=0;i<5;i++)yield return null;root.style.width=1600/panel.scale;root.style.height=1000/panel.scale;
        for(int i=0;i<5;i++)yield return null;
    }
    [TearDown] public void Cleanup()
    {
        if(hud!=null)Get<PlayableRuntime>("runtime")?.RequestStop();
        if(target!=null){host.GetComponent<UIDocument>().panelSettings.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
        if(host!=null)UnityEngine.Object.DestroyImmediate(host);NativeGameplayAudioProfile.Reload();
    }
    IEnumerator PressReset(string expectedLabel)
    {
        var old=Get<PlayableRuntime>("runtime");Call("Pause",true);Call("UpdateHud");
        var button=Get<Button>("modalRestartButton");Assert.AreEqual(expectedLabel,button.text);
        Get<NativeMenuNavigation>("menuNavigation").ActivateElement(button);
        yield return Until(()=>!Get<bool>("provingGroundBusy")&&!ReferenceEquals(old,Get<PlayableRuntime>("runtime"))&&Get<PlayableRuntime>("runtime").IsPrepared);
        Assert.True(old.IsStopped);Set("view",Get<PlayableRuntime>("runtime").Latest);Call("UpdateHud");
    }
    void AssertEmpty()
    {
        var setup=Get<ProvingGroundSetup>("provingGround");var runtime=Get<PlayableRuntime>("runtime");
        Assert.Zero(setup.Units.Count);Assert.Zero(setup.Buildings.Count);Assert.AreEqual(225,setup.Profile.TankHealth);
        Assert.IsNull(Get<PlayableEntityKind?>("provingUnit"));Assert.Zero(runtime.Latest.Entities.Count);
        Assert.True(runtime.Latest.Buildings.All(b=>b.SlotId==0),"Only the map's fixed starting objects remain");
        Assert.AreEqual(original.Profile.AuthoredMap.Sites(original.Profile).Length,runtime.Latest.Buildings.Count);
        for(int side=0;side<2;side++)
        {
            Assert.Zero(setup.Research(side).Count);Assert.Zero(runtime.ParticipantView(side==0?"player-1":"enemy-1").OwnerResearch.Count);
            Assert.Zero(runtime.ProvingGroundCosts(side).Army);Assert.Zero(runtime.ProvingGroundCosts(side).Research);
            foreach(var building in original.Buildings)Assert.False(setup.BuildingUpgraded(building.SiteId,building.SlotId));
            var badges=Get<VisualElement>("provingGroundLayer").Query<Label>().ToList().Where(t=>t.name.StartsWith("proving-research-stage-"+side+"-")).ToArray();
            Assert.AreEqual(7,badges.Length);Assert.True(badges.All(t=>t.text=="—"));
        }
    }
    void Capture(string name)
    {
        string directory=Environment.GetEnvironmentVariable("SPACEWARS_PROVING_RESET_EVIDENCE");if(string.IsNullOrEmpty(directory))return;
        Directory.CreateDirectory(directory);var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(directory,name+".png"),image.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
    }
    [UnityTest] public IEnumerator ResetButtonClearsBothSidesBuildingsResearchAndLocalUpgrades()
    {
        Capture("before-reset");yield return PressReset("Сбросить");AssertEmpty();
        for(int i=0;i<5;i++)yield return null;Capture("after-reset");
    }
    [UnityTest] public IEnumerator ReturnToPreparationPreservesRecipeThenResetClearsIt()
    {
        Set("provingGroundBattle",true);yield return PressReset("К расстановке");
        var setup=Get<ProvingGroundSetup>("provingGround");var runtime=Get<PlayableRuntime>("runtime");
        Assert.AreEqual(original.Units.Count,setup.Units.Count);Assert.AreEqual(original.Buildings.Count,setup.Buildings.Count);
        Assert.AreEqual(original.Units.Count,runtime.Latest.Entities.Count);
        for(int side=0;side<2;side++)CollectionAssert.AreEquivalent(original.Research(side),setup.Research(side));
        foreach(var building in original.Buildings)Assert.True(setup.BuildingUpgraded(building.SiteId,building.SlotId));
        yield return PressReset("Сбросить");AssertEmpty();
    }
}

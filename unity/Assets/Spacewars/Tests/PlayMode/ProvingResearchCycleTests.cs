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

public sealed class ProvingResearchCycleTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly PlayableResearchKind[][] Chains={new[]{PlayableResearchKind.TankChassis},new[]{PlayableResearchKind.ExplorerAssaultGuns,PlayableResearchKind.ExplorerRocketLauncher},new[]{PlayableResearchKind.ShkvalGuidance},new[]{PlayableResearchKind.HawkSalvoSystems},new[]{PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.AdvancedWeaponSystems,PlayableResearchKind.UltimateWeaponSystems},new[]{PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.AdvancedDefensiveSystems,PlayableResearchKind.UltimateDefensiveSystems},new[]{PlayableResearchKind.ReinforcedConcrete,PlayableResearchKind.AdvancedReinforcedConcrete}};
    GameObject host;PlayableBootstrap hud;PlayableRuntime runtime;ProvingGroundSetup setup;RenderTexture target;
    T Get<T>(string n)=>(T)typeof(PlayableBootstrap).GetField(n,Flags).GetValue(hud);
    void Set(string n,object v)=>typeof(PlayableBootstrap).GetField(n,Flags).SetValue(hud,v);
    void Call(string n)=>typeof(PlayableBootstrap).GetMethod(n,Flags).Invoke(hud,null);
    Button Control(int side,PlayableResearchKind root)=>Get<VisualElement>("provingGroundLayer").Q<Button>("proving-research-"+side+"-"+root);
    string Badge(int side,PlayableResearchKind root)=>Control(side,root).Q<Label>("proving-research-stage-"+side+"-"+root).text;
    static string Level(int n)=>n==0?"—":n==1?"I":n==2?"II":"III";
    void Click(int side,PlayableResearchKind root)=>Get<NativeMenuNavigation>("menuNavigation").ActivateElement(Control(side,root));
    IEnumerator Until(Func<bool> condition){float end=Time.realtimeSinceStartup+15;while(!condition()&&Time.realtimeSinceStartup<end)yield return null;Assert.True(condition(),"Proving selection must publish");}
    IEnumerator Drain(){yield return Until(()=>!runtime.ProvingGroundEditsPending);Set("view",runtime.Latest);Call("UpdateProvingGroundUi");}
    [UnitySetUp] public IEnumerator Setup()
    {
        setup=new ProvingGroundSetup(PlayableProfile.Default);runtime=new PlayableRuntime(setup.Configuration(),112);
        host=new GameObject("cycling proving UI");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        Set("profile",setup.Profile);Call("CreateHud");yield return Until(()=>runtime.IsPrepared&&runtime.Latest!=null);
        Set("runtime",runtime);Set("provingGround",setup);Set("view",runtime.Latest);var tabs=Get<string[]>("provingTabs");tabs[0]=tabs[1]="Исследования";Call("CreateProvingGroundUi");Call("UpdateProvingGroundUi");
    }
    [TearDown] public void Cleanup()
    {
        runtime?.RequestStop();Set("runtime",null);
        if(target!=null){host.GetComponent<UIDocument>().panelSettings.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
        UnityEngine.Object.DestroyImmediate(host);
    }
    [UnityTest] public IEnumerator EveryChainCyclesAllStagesAndRapidClicksKeepTheirCount()
    {
        CollectionAssert.AreEquivalent(Enum.GetValues(typeof(PlayableResearchKind)).Cast<PlayableResearchKind>().Where(k=>!PlayableResearchRules.Local(k)),Chains.SelectMany(c=>c));
        for(int side=0;side<2;side++)foreach(var chain in Chains)
        {
            for(int stage=1;stage<=chain.Length;stage++)
            {
                Click(side,chain[0]);Assert.AreEqual(Level(stage),Badge(side,chain[0]));Call("UpdateProvingGroundUi");Assert.AreEqual(Level(stage),Badge(side,chain[0]));
                yield return Drain();CollectionAssert.AreEquivalent(chain.Take(stage),runtime.ProvingGroundResearchSelection(side));
                Assert.AreEqual(chain.Take(stage).Sum(k=>PlayableResearchRules.Cost(setup.Profile,k)),runtime.ProvingGroundCosts(side).Research);
                var row=Control(side,chain[0]).parent;using(var enter=PointerEnterEvent.GetPooled()){enter.target=row;row.SendEvent(enter);}
                StringAssert.Contains(PlayableResearchRules.Description(setup.Profile,chain[stage-1]),Get<VisualElement>("provingGroundLayer").Q<Label>("proving-unit-description").text);
                using(var leave=PointerLeaveEvent.GetPooled()){leave.target=row;row.SendEvent(leave);}
            }
            Click(side,chain[0]);Assert.AreEqual("—",Badge(side,chain[0]));yield return Drain();Assert.Zero(runtime.ProvingGroundResearchSelection(side).Count);Assert.Zero(runtime.ProvingGroundCosts(side).Research);
        }
        for(int side=0;side<2;side++)
        {
            for(int click=1;click<=9;click++){Click(side,PlayableResearchKind.ImprovedWeaponSystems);Call("UpdateProvingGroundUi");Assert.AreEqual(Level(click%4),Badge(side,PlayableResearchKind.ImprovedWeaponSystems));}
            yield return Drain();CollectionAssert.AreEquivalent(new[]{PlayableResearchKind.ImprovedWeaponSystems},runtime.ProvingGroundResearchSelection(side));
            Assert.AreEqual("I",Badge(side,PlayableResearchKind.ImprovedWeaponSystems));
        }
        Assert.True(runtime.SelectProvingGroundResearch(0,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.UltimateWeaponSystems));yield return Drain();Assert.AreEqual("III",Badge(0,PlayableResearchKind.ImprovedWeaponSystems));
        Assert.True(runtime.SelectProvingGroundResearch(0,PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.ImprovedWeaponSystems));yield return Drain();
        CollectionAssert.AreEquivalent(new[]{PlayableResearchKind.ImprovedWeaponSystems},runtime.ProvingGroundResearchSelection(0));Assert.AreEqual("I",Badge(0,PlayableResearchKind.ImprovedWeaponSystems));
        runtime.RequestPause(true);yield return Until(()=>runtime.Latest.Paused);Click(0,PlayableResearchKind.ImprovedWeaponSystems);Assert.AreEqual("I",Badge(0,PlayableResearchKind.ImprovedWeaponSystems));
    }
    [UnityTest] public IEnumerator SevenRowsFitWithoutScrollingAtSupportedWindowSizes()
    {
        var chosen=new[]{1,2,0,1,2,3,1};for(int side=0;side<2;side++)for(int i=0;i<Chains.Length;i++)for(int click=0;click<chosen[i];click++)Click(side,Chains[i][0]);yield return Drain();
        var root=Get<VisualElement>("root");var panel=host.GetComponent<UIDocument>().panelSettings;
        foreach(var size in new[]{new Vector2Int(800,620),new Vector2Int(1280,800),new Vector2Int(1600,1000)})
        {
            if(target!=null){panel.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);}
            target=new RenderTexture(size.x,size.y,0);target.Create();panel.targetTexture=target;root.style.backgroundColor=OrbitalTheme.Background;
            for(int pass=0;pass<3;pass++){root.style.width=size.x/panel.scale;root.style.height=size.y/panel.scale;for(int frame=0;frame<3;frame++)yield return null;}
            for(int side=0;side<2;side++)
            {
                var sidePanel=Get<VisualElement>("provingGroundLayer").Q("proving-side-"+side);var viewport=sidePanel.Q<ScrollView>().contentViewport.worldBound;
                Assert.AreEqual(7,sidePanel.Query<Button>().ToList().Count(b=>b.name.StartsWith("proving-research-")));
                foreach(var chain in Chains)
                {
                    var button=Control(side,chain[0]);var label=button.Q<Label>("proving-research-label-"+side+"-"+chain[0]);
                    Assert.AreEqual(WhiteSpace.NoWrap,label.resolvedStyle.whiteSpace);Assert.LessOrEqual(label.worldBound.height,button.worldBound.height+.5f,"Captions must remain on one line");
                    Assert.GreaterOrEqual(button.worldBound.yMin,viewport.yMin-.5f);Assert.LessOrEqual(button.worldBound.yMax,viewport.yMax+.5f,"All seven rows must fit at "+size);
                    Assert.LessOrEqual(button.Q<Label>("proving-research-label-"+side+"-"+chain[0]).worldBound.xMax,button.Q<Label>("proving-research-stage-"+side+"-"+chain[0]).worldBound.xMin+.5f);
                }
                Assert.AreEqual(0,sidePanel.Q<ScrollView>().scrollOffset.y);
            }
            string output=Environment.GetEnvironmentVariable("SPACEWARS_PROVING_CYCLES_EVIDENCE");
            if(!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);var previous=RenderTexture.active;var pixels=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
                try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,"compact-"+size.x+"x"+size.y+".png"),pixels.EncodeToPNG());}
                finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
            }
        }
    }
}

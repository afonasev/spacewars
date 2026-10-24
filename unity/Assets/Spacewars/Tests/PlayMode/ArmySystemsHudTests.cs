using System;
using System.Collections;
using System.Collections.Generic;
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

public sealed class ArmySystemsHudTests
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap hud;PlayableProfile p;
    T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,F).GetValue(hud);
    void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,F).SetValue(hud,value);
    void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethods(F).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(hud,args);
    [SetUp] public void Setup(){host=new GameObject("army research HUD");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;p=PlayableProfile.Default;Set("profile",p);Call("CreateHud");}
    [TearDown] public void Cleanup(){Get<PlayableRuntime>("runtime")?.RequestStop();UnityEngine.Object.DestroyImmediate(host);}
    PlayableBuildingSnapshot Show(PlayableResearchOrderSnapshot[] orders=null,bool enemy=false)
    {
        orders=orders??Array.Empty<PlayableResearchOrderSnapshot>();
        var lifecycle=new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false);
        var building=new PlayableBuildingSnapshot(7,enemy?PlayableOwner.Enemy:PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,250,1,0,0,default,includePrivateState:!enemy,lifecycle:lifecycle,research:orders);
        var view=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,10000,null,Array.Empty<PlayableEntitySnapshot>(),new[]{building},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,activeProfile:p,ownerResearch:enemy?null:orders,researchAvailability:Enum.GetValues(typeof(PlayableResearchKind)).Cast<PlayableResearchKind>().Select(k=>new PlayableResearchAvailabilitySnapshot(k,PlayableResearchRules.Cost(p,k),!orders.Any(r=>r.Kind==k)&&PlayableResearchRules.Prerequisite(k,orders))).ToArray());
        Set("view",view);Get<HashSet<int>>("selection").Clear();Get<HashSet<int>>("selection").Add(7);Call("UpdateHud");return building;
    }
    static PlayableResearchOrderSnapshot Completed(PlayableResearchKind kind)=>new PlayableResearchOrderSnapshot((int)kind+1,kind,7,PlayableResearchRules.Cost(PlayableProfile.Default,kind),PlayableResearchRules.Duration(PlayableProfile.Default,kind),PlayableResearchRules.Duration(PlayableProfile.Default,kind),false,true);
    [Test] public void NewChoicesUseProfileTermsAndSecondLevelPrerequisites()
    {
        var data=p.CopyData();data.weaponSystemsLevel1Cost=327;data.weaponSystemsLevel1Seconds=33;p=PlayableProfile.Create(data);Set("profile",p);Show();
        var actions=Get<OfflinePadAction[]>("battleActions");var weapon=actions.Single(a=>a.Command?.Research==PlayableResearchKind.ImprovedWeaponSystems&&a.Command.Kind==PlayableCommandKind.QueueResearch);
        Assert.AreEqual(327,weapon.Price);Assert.That(weapon.Detail,Does.Contain("33 с"));Assert.That(weapon.Detail,Does.Contain("Улучшенные оружейные системы"));Assert.That(weapon.Detail,Does.Contain("15%"));
        Assert.False(actions.Any(a=>a.Command?.Kind==PlayableCommandKind.QueueResearch&&a.Command.Research==PlayableResearchKind.AdvancedWeaponSystems));
        Show(new[]{Completed(PlayableResearchKind.ImprovedWeaponSystems),Completed(PlayableResearchKind.ImprovedDefensiveSystems)});
        actions=Get<OfflinePadAction[]>("battleActions");foreach(var kind in new[]{PlayableResearchKind.AdvancedWeaponSystems,PlayableResearchKind.AdvancedDefensiveSystems}){
            var a=actions.Single(x=>x.Command?.Kind==PlayableCommandKind.QueueResearch&&x.Command.Research==kind);Assert.AreEqual(500,a.Price);Assert.That(a.Detail,Does.Contain("45 с"));Assert.That(a.Detail,Does.Contain("30%"));}
    }
    [Test] public void FocusSubmitRoutesTheSelectedNewResearchOnceAndEnemyHasNoAction()
    {
        Show();var action=Get<OfflinePadAction[]>("battleActions").Single(a=>a.Command?.Kind==PlayableCommandKind.QueueResearch&&a.Command.Research==PlayableResearchKind.ImprovedDefensiveSystems);
        var ring=Get<OrbitalBattleRing>("battleRing");int fired=0;ring.InvokeAction=(id,hold)=>{Assert.AreEqual(action.Sector.Id,id);Assert.False(hold);fired++;};
        var button=ring.Q<Button>("radial-"+action.Sector.Id);button.Focus();using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}Assert.AreEqual(1,fired);
        Show(enemy:true);Assert.IsEmpty(Get<OfflinePadAction[]>("battleActions"));
    }
    [UnityTest] public IEnumerator AllThirteenLabControlsAreReachableAndPersistRealValues()
    {
        var root=Get<VisualElement>("root");root.Clear();root.style.width=1280;root.style.height=900;
        var panel=host.GetComponent<UIDocument>().panelSettings;var texture=new RenderTexture(1280,900,24);texture.Create();panel.targetTexture=texture;
        var directory=Path.Combine(Path.GetTempPath(),"spacewars-army-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"balance.json");var navigation=new NativeMenuNavigation(root);
        try{
            var store=new NativeBalanceStore(p,path);var lab=new NativeBalanceView(root,navigation,store,()=>{});
            yield return null;yield return null;
            foreach(var entity in new[]{"Оружейные системы","Защитные системы"}){
                var button=lab.Page.Q<Button>("lab-object-"+entity);Assert.NotNull(button,entity);navigation.ActivateElement(button);yield return null;
                string prefix=entity=="Оружейные системы"?"weaponSystems":"defensiveSystems";
                foreach(int level in new[]{1,2})foreach(var term in new[]{"Cost","Seconds","BonusPercent"})Assert.NotNull(lab.Page.Q<TextField>("lab-field-"+prefix+"Level"+level+term));
                if(prefix=="weaponSystems"){lab.Page.Q<TextField>("lab-field-weaponSystemsLevel1Cost").value="325";lab.Page.Q<TextField>("lab-field-weaponSystemsLevel2Seconds").value="46";}
                yield return null;yield return null;
                var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/qa/army-systems/ui-images"));Directory.CreateDirectory(output);
                var old=RenderTexture.active;RenderTexture.active=texture;var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();RenderTexture.active=old;File.WriteAllBytes(Path.Combine(output,"lab-"+prefix+".png"),pixels.EncodeToPNG());UnityEngine.Object.Destroy(pixels);
            }
            navigation.ActivateElement(lab.Page.Q<Button>("lab-section-Правила"));navigation.ActivateElement(lab.Page.Q<Button>("lab-object-ИИ"));yield return null;
            var minimum=lab.Page.Q<TextField>("lab-field-armySystemsMinimumUnits");Assert.NotNull(minimum);minimum.value="11";
            foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("ai.research.armySystems.")))Assert.NotNull(lab.Page.Q<TextField>("lab-field-"+field.FieldName),field.Path);
            lab.Page.Q<TextField>("lab-field-aiArmySystemsFighterCatchupSeconds").value="61";
            lab.Page.Q<TextField>("lab-field-aiArmySystemsVeteranUtilityScale").value="4.5";
            lab.Page.Q<TextField>("lab-field-aiArmySystemsVeteranLevel2DelaySeconds").value="390";
            yield return null;yield return null;
            var aiOutput=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/qa/army-race-lab"));Directory.CreateDirectory(aiOutput);
            var aiOld=RenderTexture.active;RenderTexture.active=texture;var aiPixels=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);aiPixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);aiPixels.Apply();RenderTexture.active=aiOld;File.WriteAllBytes(Path.Combine(aiOutput,"lab-ai.png"),aiPixels.EncodeToPNG());UnityEngine.Object.Destroy(aiPixels);
            navigation.ActivateElement(lab.Page.Q<Button>("lab-save"));yield return null;
            Assert.AreEqual(325,store.Active.WeaponSystemsLevel1Cost);Assert.AreEqual(46,store.Active.WeaponSystemsLevel2Seconds);Assert.AreEqual(11,store.Active.ArmySystemsMinimumUnits);Assert.AreEqual(61,store.Active.ArmySystemsAiCatchupSeconds(Spacewars.Simulation.Ai.AiDifficulty.Fighter));Assert.AreEqual(4.5,store.Active.ArmySystemsAiUtilityScale(Spacewars.Simulation.Ai.AiDifficulty.Veteran));Assert.AreEqual(390,store.Active.ArmySystemsAiDelaySeconds(Spacewars.Simulation.Ai.AiDifficulty.Veteran,2));
            // A new Lab session selects the release Default; persistence is checked against the saved revision.
            int revision=store.SelectedVersion;var reloaded=new NativeBalanceStore(p,path).Resolve(revision);
            Assert.AreEqual(325,reloaded.WeaponSystemsLevel1Cost);Assert.AreEqual(46,reloaded.WeaponSystemsLevel2Seconds);Assert.AreEqual(11,reloaded.ArmySystemsMinimumUnits);Assert.AreEqual(61,reloaded.ArmySystemsAiCatchupSeconds(Spacewars.Simulation.Ai.AiDifficulty.Fighter));Assert.AreEqual(4.5,reloaded.ArmySystemsAiUtilityScale(Spacewars.Simulation.Ai.AiDifficulty.Veteran));Assert.AreEqual(390,reloaded.ArmySystemsAiDelaySeconds(Spacewars.Simulation.Ai.AiDifficulty.Veteran,2));
        }finally{navigation.Dispose();panel.targetTexture=null;texture.Release();UnityEngine.Object.Destroy(texture);Directory.Delete(directory,true);}
    }
    [UnityTest] public IEnumerator NativeResearchRingAndThreeOrderQueueFitCompactAndNormalViews()
    {
        var root=Get<VisualElement>("root");var panel=host.GetComponent<UIDocument>().panelSettings;
        var oldTarget=panel.targetTexture;
        var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/qa/army-systems/ui-images"));Directory.CreateDirectory(output);
        foreach(var size in new[]{new Vector2Int(1600,1000),new Vector2Int(960,620)}){
            var target=new RenderTexture(size.x,size.y,24);target.Create();panel.targetTexture=target;root.style.backgroundColor=OrbitalTheme.Background;
            // Panel scaling uses physical pixels; root bounds use logical points.
            yield return null;yield return null;
            root.style.width=size.x/panel.scale;root.style.height=size.y/panel.scale;
            try{
                foreach(var state in new[]{"first-level","second-level","three-order-queue"}){
                    PlayableResearchOrderSnapshot[] orders=state=="first-level"?Array.Empty<PlayableResearchOrderSnapshot>():state=="second-level"?new[]{Completed(PlayableResearchKind.ImprovedWeaponSystems),Completed(PlayableResearchKind.ImprovedDefensiveSystems)}:new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,7,300,15,30,true,false),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ImprovedWeaponSystems,7,0,0,0,false,false),new PlayableResearchOrderSnapshot(3,PlayableResearchKind.ImprovedDefensiveSystems,7,0,0,0,false,false)};
                    Show(orders);yield return null;yield return null;Call("UpdateHud");yield return null;
                    var actions=Get<OfflinePadAction[]>("battleActions");var action=actions.FirstOrDefault(a=>a.Command?.Kind==PlayableCommandKind.QueueResearch&&PlayableResearchRules.Global(a.Command.Research));
                    if(action!=null){Get<OrbitalBattleRing>("battleRing").Set(actions,"Научный центр",action.Sector.Id,contextGlyph:"science");yield return null;}
                    foreach(var button in Get<OrbitalBattleRing>("battleRing").Query<Button>().ToList()){
                        Assert.GreaterOrEqual(button.worldBound.xMin,0);Assert.LessOrEqual(button.worldBound.xMax,size.x/panel.scale);Assert.GreaterOrEqual(button.worldBound.yMin,0);Assert.LessOrEqual(button.worldBound.yMax,size.y/panel.scale);}
                    Assert.LessOrEqual(Get<VisualElement>("armyRegion").worldBound.yMax,size.y/panel.scale+.5f,"research context must fit the actual scaled viewport");
                    if(state=="three-order-queue"){Assert.That(root.Q<Button>("research-slot-0").text,Does.Contain("50%"));Assert.That(root.Q<Button>("research-slot-2").text,Does.Contain("ждёт"));}
                    var previous=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0);pixels.Apply();RenderTexture.active=previous;
                    File.WriteAllBytes(Path.Combine(output,state+"-"+size.x+".png"),pixels.EncodeToPNG());UnityEngine.Object.Destroy(pixels);
                }
            }finally{panel.targetTexture=oldTarget;target.Release();UnityEngine.Object.Destroy(target);}
        }
    }
}

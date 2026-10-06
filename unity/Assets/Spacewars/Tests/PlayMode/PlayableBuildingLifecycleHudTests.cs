using System;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class PlayableBuildingLifecycleHudTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;private PlayableBootstrap hud;private VisualElement root;
    private void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
    private object Get(string name)=>typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
    private void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(hud,args);
    [SetUp] public void Setup()
    {
        host=new GameObject("lifecycle HUD test");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        root=new VisualElement();Set("root",root);Set("profile",PlayableProfile.Default);Call("CreateBuildingLifecycleHud",root);
        Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
    }
    [TearDown] public void Cleanup()=>UnityEngine.Object.DestroyImmediate(host);
    private void Show(int id=1,string blocked=null,bool repairing=false,bool selling=false,bool waiting=false)
    {
        var state=new PlayableBuildingLifecycleSnapshot(selling,.5,repairing,waiting,1,15,blocked,repairing?"Ремонт уже активен":null,3,400,true);
        Call("UpdateBuildingLifecycleHud",new PlayableBuildingSnapshot(id,PlayableOwner.Player,PlayableBuildingKind.Factory,default,125,1,0,0,default,lifecycle:state));
    }
    [Test] public void ConfirmationResetsOnSelectionAvailabilityFocusAndPause()
    {
        Show();Call("ConfirmBuildingSale");Assert.AreEqual(1,Get("confirmSaleBuilding"));Assert.That(root.Q<Button>("sell-building").text,Does.Contain("✓"));
        Show(2);Assert.Zero((int)Get("confirmSaleBuilding"));Call("ConfirmBuildingSale");Show(2,"В бою");Assert.Zero((int)Get("confirmSaleBuilding"));Assert.False(root.Q<Button>("sell-building").enabledSelf);
        Show();Call("ConfirmBuildingSale");Call("OnApplicationFocus",false);Assert.Zero((int)Get("confirmSaleBuilding"));
        Call("ConfirmBuildingSale");Set("paused",true);Show();Assert.Zero((int)Get("confirmSaleBuilding"));Assert.False(root.Q<Button>("repair-building").enabledSelf);
    }
    [Test] public void WaitingRepairRemainsCancellableAndSaleShowsConsequences()
    {
        Show(repairing:true,waiting:true);Assert.True(root.Q<Button>("repair-building").enabledSelf);Assert.That(root.Q<Button>("repair-building").text,Does.Contain("Отменить"));Assert.That(root.Q<Label>("building-lifecycle-status").text,Does.Contain("ожидает кредитов"));
        Show();Assert.That(root.Q<Label>("building-lifecycle-status").text,Does.Contain("Последний центр"));Assert.That(root.Q<Label>("building-lifecycle-status").text,Does.Contain("3"));
        Show(blocked:"Демонтаж необратим",selling:true);Assert.False(root.Q<Button>("sell-building").enabledSelf);Assert.That(root.Q<Label>("building-lifecycle-status").text,Does.Contain("Демонтаж"));
    }
}

using System;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class PlayableScienceHudTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private GameObject host;private PlayableBootstrap hud;private VisualElement root;
    private void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
    private object Get(string name)=>typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
    private void Call(string name,params object[] args)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(hud,args);
    [SetUp] public void Setup()
    {
        host=new GameObject("science HUD test");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        root=new VisualElement();Set("root",root);Set("profile",PlayableProfile.Default);Call("CreateScienceHud",root);
        Set("view",new PlayableSnapshot(PlayableProfile.RequiredProfileId,1,1,7,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,900,null,Array.Empty<PlayableEntitySnapshot>(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
    }
    [TearDown] public void Cleanup()=>UnityEngine.Object.DestroyImmediate(host);
    private void Show(bool active=false,bool complete=false,string reason=null,bool own=true,bool selling=false)
    {
        var upgrade=new PlayableRefineryUpgradeSnapshot(active,complete,200,5,20,reason);
        var lifecycle=new PlayableBuildingLifecycleSnapshot(selling,0,false,false,0,0,null,null,1,300,false);
        Call("UpdateScienceHud",new PlayableBuildingSnapshot(1,own?PlayableOwner.Player:PlayableOwner.Enemy,PlayableBuildingKind.Refinery,default,200,1,0,0,default,includePrivateState:own,lifecycle:lifecycle,upgrade:upgrade,refineryUpgraded:complete));
    }
    [Test] public void BlockedActionRemainsFocusableAndExplainsRequirementWithoutCommand()
    {
        Show(reason:"Нужен Научный центр");var action=root.Q<Button>("upgrade-refinery");
        Assert.True(action.focusable);Assert.True(action.enabledSelf);Assert.False((bool)Get("scienceActionAllowed"));
        Assert.That(action.tooltip,Does.Contain("Научный центр"));Assert.That(action.tooltip,Does.Contain("200"));Assert.That(action.tooltip,Does.Contain("20"));
        Assert.That(root.Q<Label>("refinery-upgrade-reason").text,Does.Contain("Научный центр"));
        Assert.AreEqual(DisplayStyle.None,root.Q<ProgressBar>("refinery-upgrade-progress").style.display.value);
    }
    [Test] public void ProgressIsCancellableButPauseSaleAndEnemyAreNotInteractive()
    {
        Show(active:true,reason:"Улучшение уже начато");Assert.True((bool)Get("scienceActionAllowed"));
        Assert.AreEqual(25,root.Q<ProgressBar>("refinery-upgrade-progress").value);Assert.That(root.Q<Button>("upgrade-refinery").text,Does.Contain("Отменить"));
        Set("paused",true);Show(active:true);Assert.False((bool)Get("scienceActionAllowed"));
        Set("paused",false);Show(active:true,selling:true);Assert.False((bool)Get("scienceActionAllowed"));
        Show(own:false);Assert.AreEqual(DisplayStyle.None,root.Q<VisualElement>("science-controls").style.display.value);Assert.Zero((int)Get("scienceRefinery"));
        Show(complete:true,reason:"Завод уже улучшен");Assert.False((bool)Get("scienceActionAllowed"));Assert.That(root.Q<Label>("refinery-upgrade-reason").text,Does.Contain("15"));
    }
    [Test] public void CompletedRefinerySwapsOnlyModelAndUsesAuthoredTurbineAxis()
    {
        var parent=new GameObject("refinery model test");
        try
        {
            using var world=new PlayableWorld(parent.transform,PlayableProfile.Default);var a=world.Building(1,"Refinery",true);var rootBefore=a.Root;var facing=a.Hull.rotation;
            var snapshot=new PlayableBuildingSnapshot(1,PlayableOwner.Player,PlayableBuildingKind.Refinery,default,123,1,0,0,default,refineryUpgraded:true);
            world.UpdateRefineryModel(a,snapshot,30);Assert.AreSame(rootBefore,a.Root);Assert.True(a.RefineryUpgraded);Assert.NotNull(a.Turbine);Assert.Less(Quaternion.Angle(facing,a.Hull.rotation),.001f);
            a.Root.rotation=Quaternion.Euler(0,73,0);world.FaceBuilding(a);Assert.Less(Quaternion.Angle(facing,a.Hull.rotation),.001f);
            Assert.Less(Quaternion.Angle(a.Turbine.localRotation,Quaternion.Euler(0,0,-90)),.001f);
            var rotation=a.Turbine.localRotation;world.UpdateRefineryModel(a,snapshot,30);Assert.AreEqual(rotation,a.Turbine.localRotation);
            var camera=new GameObject("seat camera").AddComponent<Camera>();camera.transform.SetParent(parent.transform);camera.transform.rotation=Quaternion.Euler(45,0,0);
            world.FaceBuilding(a,camera);Assert.That(Vector3.Dot(a.Hull.forward,Vector3.back),Is.GreaterThan(.999));
            Assert.NotNull(world.Building(2,"ScientificCenter",true).Hull);
        }
        finally{UnityEngine.Object.DestroyImmediate(parent);}
    }
    [Test] public void ScienceCenterShowsSixResearchSlotsWithActiveAndWaitingOrders()
    {
        var research=new[]{new PlayableResearchOrderSnapshot(1,PlayableResearchKind.TankChassis,7,300,15,30,true,false),new PlayableResearchOrderSnapshot(2,PlayableResearchKind.ExplorerAssaultGuns,7,0,0,30,false,false),new PlayableResearchOrderSnapshot(3,PlayableResearchKind.ShkvalGuidance,7,0,0,30,false,false)};
        var lifecycle=new PlayableBuildingLifecycleSnapshot(false,0,false,false,0,0,null,null,1,300,false);
        Call("UpdateScienceHud",new PlayableBuildingSnapshot(7,PlayableOwner.Player,PlayableBuildingKind.ScientificCenter,default,250,1,0,0,default,lifecycle:lifecycle,research:research));
        Assert.AreEqual(DisplayStyle.Flex,root.Q<VisualElement>("research-slots").style.display.value);
        Assert.That(root.Q<Button>("research-slot-0").text,Does.Contain("50%"));Assert.That(root.Q<Button>("research-slot-1").text,Does.Contain("ждёт"));Assert.AreEqual("—",root.Q<Button>("research-slot-5").text);
        Assert.True(root.Q<Button>("research-slot-0").enabledSelf);Assert.False(root.Q<Button>("research-slot-1").enabledSelf);
    }
}

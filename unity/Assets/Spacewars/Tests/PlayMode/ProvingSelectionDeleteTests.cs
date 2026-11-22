using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Input;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class ProvingSelectionDeleteTests : InputTestFixture
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap hud;PlayableInput input;Keyboard keyboard;PlayableRuntime runtime;
    void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Flags).SetValue(hud,value);
    T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(hud);
    void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(hud,null);
    void Press(Key key){InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));InputSystem.Update();input.Poll();InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();input.Poll();}
    IEnumerator Until(Func<bool> condition)
    {
        float deadline=Time.realtimeSinceStartup+15;
        while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;
        Assert.True(condition(),"Selected-unit edit reached the proving snapshot");
    }
    [SetUp] public override void Setup()
    {
        base.Setup();host=new GameObject("proving selection deletion");hud=host.AddComponent<PlayableBootstrap>();hud.enabled=false;
        Set("profile",PlayableProfile.Default);Call("CreateHud");
        keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();input=host.AddComponent<PlayableInput>();
        input.AssignedKeyboard=keyboard;input.SetFocus(true);Set("input",input);Call("BindKeyboardCommands");
    }
    [TearDown] public override void TearDown()
    {
        runtime?.RequestStop();Set("runtime",null);UnityEngine.Object.DestroyImmediate(host);base.TearDown();
    }
    [UnityTest] public IEnumerator DeleteRemovesSelectedUnitsOnly()=>Exercise(Key.Delete);
    [UnityTest] public IEnumerator BackspaceRemovesSelectedUnitsOnly()=>Exercise(Key.Backspace);
    IEnumerator Exercise(Key key)
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        foreach(var point in new[]{new NavPoint(-12,-9),new NavPoint(-18,-9),new NavPoint(-12,-18),new NavPoint(-18,-18)})Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,point));
        Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Tank,new NavPoint(12,-9)));
        runtime=new PlayableRuntime(setup.Configuration(),108);
        yield return Until(()=>runtime.IsPrepared&&runtime.Latest!=null);
        Set("runtime",runtime);Set("provingGround",setup);Set("profile",setup.Profile);Set("view",runtime.Latest);
        var original=runtime.Latest;var own=original.Entities.Where(e=>e.Owner==PlayableOwner.Player).OrderBy(e=>e.Id).ToArray();
        int enemy=original.Entities.Single(e=>e.Owner==PlayableOwner.Enemy).Id,building=original.Buildings.First().Id;
        var selected=Get<HashSet<int>>("selection");
        Press(key);Assert.AreEqual(5,runtime.Latest.Entities.Count,"Empty selection must not remove the last placed unit");
        selected.Add(building);Press(key);Assert.True(selected.Contains(building));
        selected.Clear();selected.Add(own[0].Id);
        Set("provingGround",null);Press(key);Assert.True(selected.Contains(own[0].Id),"Ordinary matches ignore the proving shortcut");Set("provingGround",setup);
        Set("menuPaused",true);Press(key);Assert.True(selected.Contains(own[0].Id));Set("menuPaused",false);
        var field=new TextField();var root=Get<VisualElement>("root");root.Add(field);field.Focus();yield return null;
        Press(key);Assert.True(selected.Contains(own[0].Id),"Text editing must preserve selection");field.RemoveFromHierarchy();root.Focus();yield return null;
        Press(key);Assert.False(selected.Contains(own[0].Id));
        yield return Until(()=>!runtime.Latest.Entities.Any(e=>e.Id==own[0].Id));
        Assert.AreEqual(4,runtime.Latest.Entities.Count);Assert.True(runtime.Latest.Entities.Any(e=>e.Id==own[3].Id));
        Set("view",runtime.Latest);Set("inspectionEntityId",enemy);Press(key);Assert.Zero(Get<int>("inspectionEntityId"));
        yield return Until(()=>!runtime.Latest.Entities.Any(e=>e.Id==enemy));
        Set("view",runtime.Latest);selected.Add(own[1].Id);selected.Add(own[2].Id);selected.Add(building);selected.Add(int.MaxValue);
        Press(key);
        yield return Until(()=>runtime.Latest.Entities.Count==1);
        Assert.AreEqual(own[3].Id,runtime.Latest.Entities.Single().Id);
        Assert.AreEqual(original.Buildings.Count,runtime.Latest.Buildings.Count);
        Assert.True(selected.Contains(building));Assert.False(selected.Contains(own[1].Id)||selected.Contains(own[2].Id));
        yield return Until(()=>runtime.ProvingGroundResetSetup?.Units.Count==1);
        Assert.AreEqual(setup.Buildings.Count,runtime.ProvingGroundResetSetup.Buildings.Count);
        Set("view",runtime.Latest);selected.Clear();selected.Add(own[3].Id);Set("provingGroundBattle",true);Press(key);
        Assert.True(selected.Contains(own[3].Id),"The shortcut is disabled during battle");
        Assert.True(runtime.StartProvingGroundBattle());Assert.False(runtime.RemoveProvingGroundSelection(new[]{own[3].Id}));
    }
}

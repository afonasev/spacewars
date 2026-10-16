using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
public sealed class NativeAiMultiteamPlayModeTests
{
    private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    [UnityTest] public IEnumerator OrdinaryThreeTeamLobbyMatchFinishAndRestart()
    {
        var host=new GameObject("S5 ordinary three team game");var app=host.AddComponent<PlayableBootstrap>();app.enabled=false;
        T Get<T>(string key)=>(T)typeof(PlayableBootstrap).GetField(key,F).GetValue(app);
        void Set(string key,object value)=>typeof(PlayableBootstrap).GetField(key,F).SetValue(app,value);
        void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,F).Invoke(app,null);
        IEnumerator Wait(Func<bool> predicate){float deadline=Time.realtimeSinceStartup+12;while(!predicate()&&Time.realtimeSinceStartup<deadline)yield return null;Assert.True(predicate(),"ordinary S5 lifecycle deadline");}
        var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
        try
        {
            Set("profile",PlayableProfile.ThreeCrossingsDefault);Call("CreateWorld");Call("CreateHud");Call("CreateSpectatorHud");Set("input",host.AddComponent<Spacewars.Input.PlayableInput>());Call("CreateLobby");Call("BindLobbyDevices");Call("ShowLobby");
            var lobby=Get<VisualElement>("lobbyScreen");lobby.Q<DropdownField>("lobby-map-choice").value="ИИ-полигон · 8";
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=7108;
            setup.Participants.RemoveRange(6,2);for(int i=0;i<6;i++)setup.Participants[i].Team=i/2+1;
            setup.Participants[0].Human=true;setup.Participants[0].DeviceId=0;setup.Participants[0].Name="Игрок S5";
            Call("RebuildRoster");Call("RefreshLobbyValidation");Assert.True(Get<Button>("launchButton").enabledSelf);
            Get<Button>("launchButton").Focus();Get<NativeMenuNavigation>("menuNavigation").Activate();
            yield return Wait(()=>Get<PlayableRuntime>("runtime")!=null&&!Get<bool>("preparing"));
            var runtime=Get<PlayableRuntime>("runtime");yield return Wait(()=>runtime.Latest.Tick>=90);Call("UpdateFrame");
            Assert.AreEqual(6,runtime.Latest.Participants.Count);Assert.AreEqual(3,runtime.Latest.Participants.Select(p=>p.Team).Distinct().Count());Assert.AreEqual(1,runtime.Latest.Participants.Count(p=>p.Control==OfflineControl.Human));Assert.IsNull(runtime.Latest.Failure);
            Assert.False((bool)typeof(PlayableBootstrap).GetMethod("IsOpponent",F).Invoke(app,new object[]{(PlayableOwner)1}));Assert.True((bool)typeof(PlayableBootstrap).GetMethod("IsOpponent",F).Invoke(app,new object[]{(PlayableOwner)2}));
            // Manual finish is labelled separately from authority victory; EditMode covers elimination/victory.
            runtime.RequestFinish();yield return Wait(()=>runtime.Result!=null);Call("UpdateFrame");Assert.True(runtime.Result.Manual);Assert.AreEqual(6,runtime.Result.Players.Count);Assert.AreEqual(3,runtime.Result.Players.Select(p=>p.Team).Distinct().Count());
            long old=runtime.Generation;Call("Restart");yield return Wait(()=>runtime.IsStopped);Call("UpdateFrame");
            var next=Get<PlayableRuntime>("runtime");Assert.Greater(next.Generation,old);Assert.AreEqual(6,next.Latest.Participants.Count);Assert.AreEqual(PlayableMatchOutcome.Playing,next.Latest.Outcome);Assert.True(next.Latest.ActiveProfile.AuthoredMap is AiTestMap);
            Call("ReturnToLobby");yield return Wait(()=>next.IsStopped);Call("UpdateFrame");Assert.True(Get<bool>("inLobby"));
            TestContext.WriteLine("M5 ordinary lobby: human+5AI, 3 teams, seed7108, actual match90ticks/manual result/restart");
        }
        finally
        {
            var runtime=Get<PlayableRuntime>("runtime");if(runtime!=null){runtime.RequestStop();System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000);}
            UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
            foreach(var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(camera.gameObject);
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(light.gameObject);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }
}

using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public sealed class NativeSimulationPresentationFailureTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [UnityTest,Timeout(180000)] public IEnumerator HumanSeedThroughOrdinaryLobbyAndLivePresentation()
    {
        var host=new GameObject("human seed ordinary presentation");var app=host.AddComponent<PlayableBootstrap>();app.enabled=false;
        T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Private).GetValue(app);
        void Set(string name,object value)=>typeof(PlayableBootstrap).GetField(name,Private).SetValue(app,value);
        void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,Private).Invoke(app,null);
        string lab=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/simulation-stop/player-lab"));
        PlayableRuntime runtime=null;long firstTank=-1,lastReport=-1;int maxTanks=0;
        try
        {
            var baseline=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),
                new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));
            // Player mode reads the shipped Default release, using an isolated local store.
            Set("profile",baseline);Set("balanceStore",new NativeBalanceStore(baseline,Path.Combine(lab,"profiles.json"),false));
            Call("CreateWorld");Call("CreateHud");Call("CreateSpectatorHud");Set("input",host.AddComponent<Spacewars.Input.PlayableInput>());
            Call("CreateLobby");Call("BindLobbyDevices");Call("ShowLobby");
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=-515260688;
            for(int i=0;i<setup.Participants.Count;i++)
            {var p=setup.Participants[i];p.Human=false;p.Team=i+1;p.Name="ИИ "+(i+1);p.Difficulty=i==0?AiDifficulty.Fighter:AiDifficulty.Veteran;}
            Assert.AreEqual(2,setup.Participants.Count);Assert.True(setup.Spectator);
            Call("RebuildRoster");Call("RefreshLobbyValidation");Assert.True(Get<Button>("launchButton").enabledSelf);
            float launchAt=Time.realtimeSinceStartup;
            Get<NativeMenuNavigation>("menuNavigation").ActivateElement(Get<Button>("launchButton"));
            // PrepareLobbyMatch rebuilds the map synchronously before creating the
            // worker. Start the authority readiness budget at that lifecycle boundary;
            // the unchanged fixture timeout still bounds the entire setup and match.
            while(Get<PlayableRuntime>("runtime")==null)yield return null;
            TestContext.WriteLine("PRESENTATION_SETUP runtimeCreatedAfter="+(Time.realtimeSinceStartup-launchAt));
            float startupDeadline=Time.realtimeSinceStartup+20;
            while(Get<bool>("preparing")&&Time.realtimeSinceStartup<startupDeadline)
            {Call("UpdateFrame");yield return null;}
            runtime=Get<PlayableRuntime>("runtime");Assert.NotNull(runtime);Assert.False(Get<bool>("preparing"));Assert.False(Get<bool>("inLobby"));
            Assert.True(Get<bool>("spectatorMode"));Assert.AreEqual(1,runtime.Generation);Assert.AreEqual(-515260688,runtime.Latest.Seed);
            StringAssert.Contains("spacewars-default @1",Get<PlayableProfile>("profile").DisplayName);
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
            float deadline=Time.realtimeSinceStartup+160;
            while(runtime.Latest.Tick<6000&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
            {
                // Same main-thread frame path as Player: consume snapshots, service
                // the certified host, render the world/HUD/maps and drain receipts.
                Call("UpdateFrame");
                var view=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;
                int tanks=view.Entities.Count(e=>e.Kind==PlayableEntityKind.Tank);maxTanks=Math.Max(maxTanks,tanks);
                if(tanks>0&&firstTank<0)firstTank=view.Tick;
                long bucket=view.Tick/300;
                if(bucket!=lastReport){lastReport=bucket;TestContext.WriteLine("PRESENTATION_PROGRESS tick="+view.Tick+" tanks="+tanks+" firstTank="+firstTank);}
                yield return null;
            }
            Call("LogRuntimeFailure");
            Assert.IsNull(runtime.Latest.Failure,runtime.Latest.Failure);
            Assert.GreaterOrEqual(runtime.Latest.Tick,6000,"Includes the first-unit expansion tail beyond the human log's last command at3675.");
            Assert.GreaterOrEqual(firstTank,0);Assert.Greater(maxTanks,0);
        }
        finally
        {
            runtime=runtime??Get<PlayableRuntime>("runtime");
            if(runtime!=null)
            {
                var snapshot=runtime.Latest;
                TestContext.WriteLine("PRESENTATION_RESULT tick="+snapshot.Tick+" status="+snapshot.Status+" isPrepared="+runtime.IsPrepared+" firstTank="+firstTank+" maxTanks="+maxTanks+"\n"+snapshot.Failure);
                var scheduler=typeof(PlayableRuntime).GetField("aiScheduler",Private).GetValue(runtime);
                var owners=(System.Collections.IEnumerable)scheduler.GetType().GetField("owners",Private).GetValue(scheduler);
                foreach(var owner in owners)
                {
                    var checkpoint=(PlayableAiOwnerCheckpoint)owner.GetType().GetProperty("Checkpoint",Private).GetValue(owner);
                    foreach(var record in checkpoint.Records.Where(r=>r.Status==PlayableAiDeliveryStatus.Applied))
                        TestContext.WriteLine("PRESENTATION_AI "+Newtonsoft.Json.JsonConvert.SerializeObject(record));
                }
                runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
            }
            UnityEngine.Object.DestroyImmediate(host);
            foreach(var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(camera.gameObject);
            foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(light.gameObject);
            if(Directory.Exists(lab))Directory.Delete(lab,true);
        }
    }
}

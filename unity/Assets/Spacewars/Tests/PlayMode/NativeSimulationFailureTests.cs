using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class NativeSimulationFailureTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static PlayableProfile ShippingProfile()=>PlayableProfile.Create(
        JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),
        new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text)));

    [UnityTest] public IEnumerator FailedRuntimeLogsFullCauseOnceAndNextGenerationLogsAgain()
    {
        var host=new GameObject("simulation failure logging");var app=host.AddComponent<PlayableBootstrap>();app.enabled=false;
        var field=typeof(PlayableBootstrap).GetField("runtime",Private);
        var log=typeof(PlayableBootstrap).GetMethod("LogRuntimeFailure",Private);
        PlayableRuntime runtime=null;
        try
        {
            for(long generation=1;generation<=2;generation++)
            {
                runtime=new PlayableRuntime(PlayableProfile.Default,generation,-515260688,false,startPaused:true);
                field.SetValue(app,runtime);
                log.Invoke(app,null); // Healthy snapshots do not consume the failure report.
                Exception cause;
                try{throw new InvalidOperationException("original route cause");}catch(Exception ex){cause=ex;}
                runtime.ReportRouteFailure(cause);
                float deadline=Time.realtimeSinceStartup+10;
                while(!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.True(runtime.IsStopped,"Failure must finish the runtime lifecycle.");
                Assert.AreEqual(RuntimeStatus.Failed,runtime.Latest.Status);
                StringAssert.Contains("original route cause",runtime.Latest.Failure);
                StringAssert.Contains(nameof(FailedRuntimeLogsFullCauseOnceAndNextGenerationLogsAgain),runtime.Latest.Failure);
                var snapshot=runtime.Latest;
                string expected="[Spacewars simulation failure] generation="+generation+" seed=-515260688 tick="+snapshot.Tick+" map="+snapshot.ActiveProfile?.AuthoredMap?.Id+"\n"+snapshot.Failure;
                LogAssert.Expect(LogType.Error,new Regex("^"+Regex.Escape(expected)+"$"));
                log.Invoke(app,null);log.Invoke(app,null);log.Invoke(app,null);
                LogAssert.NoUnexpectedReceived();
                runtime.Dispose();runtime=null;
            }
        }
        finally
        {
            runtime?.RequestStop();if(runtime!=null)Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    [UnityTest,Timeout(180000)] public IEnumerator HumanSeedCrossingsSpectatorReachesAfterFirstTanks()
    {
        var profile=ShippingProfile();var setup=new NativeLobbyConfiguration
        {
            HasExplicitSeed=true,ExplicitSeed=-515260688,
            Participants=new List<NativeLobbyParticipant>
            {
                new NativeLobbyParticipant{Name="ИИ Боец",Team=1,Color=0,Difficulty=AiDifficulty.Fighter},
                new NativeLobbyParticipant{Name="ИИ Ветеран",Team=2,Color=1,Difficulty=AiDifficulty.Veteran}
            }
        };
        Assert.True(setup.Spectator);Assert.IsNull(setup.Validate(profile,true,true));
        Assert.True(profile.AuthoredMap is ThreeCrossingsMap);
        var runtime=PlayableRuntime.CreateLobbyMatch(profile,1,setup);
        var routes=new UnityHostRouteService();long firstTankTick=-1,lastReport=-1;int maxTanks=0;
        try
        {
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
            float deadline=Time.realtimeSinceStartup+160;
            while(runtime.Latest.Tick<3900&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,profile.NavigationRequestsPerFrame);
                var frame=runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                int tanks=frame.Overview.Entities.Count(e=>e.Kind==PlayableEntityKind.Tank);
                maxTanks=Math.Max(maxTanks,tanks);if(tanks>0&&firstTankTick<0)firstTankTick=frame.Overview.Tick;
                long bucket=runtime.Latest.Tick/300;
                if(bucket!=lastReport){lastReport=bucket;TestContext.WriteLine("HUMAN_SEED_PROGRESS tick="+runtime.Latest.Tick+" tanks="+tanks+" firstTank="+firstTankTick+" routeFailure="+routes.LastProjectionFailure);}
                yield return null;
            }
            var latest=runtime.Latest;
            TestContext.WriteLine("HUMAN_SEED_RESULT generation="+latest.Generation+" seed="+latest.Seed+" tick="+latest.Tick+" status="+latest.Status+" firstTank="+firstTankTick+" maxTanks="+maxTanks+" routeFailure="+routes.LastProjectionFailure+"\n"+latest.Failure);
            Assert.IsNull(latest.Failure,"Real match authority failure: "+latest.Failure);
            Assert.GreaterOrEqual(latest.Tick,3900,"Original human failure happened after command tick3675; earlier stalls are not a pass.");
            Assert.GreaterOrEqual(firstTankTick,0,"The scenario must reach actual first Tank delivery.");
            Assert.Greater(maxTanks,0);
        }
        finally
        {
            runtime.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,3000),"Owned runtime must stop before route host disposal.");
            routes.Dispose();runtime.Dispose();
        }
    }
}

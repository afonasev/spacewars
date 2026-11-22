using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class ExpertAiOrdinaryMatchTests
{
    [UnityTest,Timeout(90000)]
    public IEnumerator ExpertMovesEarnsAndRestoresThroughTheOrdinaryLobby()
        => MovementIncomeAndRestore(AiDifficulty.Expert, AiDifficulty.Veteran, 11);

    [UnityTest,Timeout(90000)]
    public IEnumerator CheaterMovesEarnsAndRestoresWithExpertOnOrdinaryIncome()
        => MovementIncomeAndRestore(AiDifficulty.Cheater, AiDifficulty.Expert, null);

    private static IEnumerator MovementIncomeAndRestore(AiDifficulty bonus, AiDifficulty standard, int? historicalVersion)
    {
        const string expertId="player-1",veteranId="foundry-2";
        var map=new ThreeCrossingsMap(JsonUtility.FromJson<ThreeCrossingsProfileData>(Resources.Load<TextAsset>("ThreeCrossingsProfile").text));
        var baseline=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text),map);
        string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/expert/playmode/"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(output);
        var store=NativeBalanceStore.ForApplication(baseline,Path.Combine(output,"balance.json"));
        Assert.AreEqual(NativeBalanceStore.DefaultId,store.SelectedId);
        if(historicalVersion.HasValue)store.Select(NativeBalanceStore.DefaultId,historicalVersion.Value);
        Assert.AreEqual(historicalVersion??16,store.SelectedVersion);
        var profile=PlayableProfile.Create(store.Active.CopyData(),map,store.Active.DisplayName);
        var ai=store.ActiveAiProfile;
        var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=7,Participants=new List<NativeLobbyParticipant>{
            new NativeLobbyParticipant{Name="ИИ",Team=1,Color=0,Difficulty=bonus},
            new NativeLobbyParticipant{Name="ИИ",Team=2,Color=1,Difficulty=standard}
        }};
        var config=NativeLobbyMatch.Create(profile,setup);
        PlayableRuntime runtime=null,restored=null;
        UnityHostRouteService routes=null,restoredRoutes=null;
        try
        {
            TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
            runtime=PlayableRuntime.CreateLobbyMatch(profile,1,setup,startPaused:true,aiProfile:ai);
            routes=new UnityHostRouteService();
            Assert.That(runtime.LobbyConfiguration.ParticipantName(0),Does.EndWith(" ("+NativeLobbyConfiguration.DifficultyNames[(int)bonus]+")"));
            var initial=runtime.ParticipantView(expertId);
            var veteran=runtime.ParticipantView(veteranId);
            Assert.AreEqual(profile.StartingCredits,initial.ExactCredits);
            Assert.AreEqual(initial.ExactCredits,veteran.ExactCredits);
            Assert.AreEqual(profile.HeadquartersIncomePerSecond,veteran.IncomePerSecond,1e-10);
            Assert.AreEqual(veteran.IncomePerSecond*1.5,initial.IncomePerSecond,1e-10);
            var explorer=initial.Entities.Single(e=>e.Owner==initial.Owner&&e.Kind==PlayableEntityKind.Explorer);
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
            runtime.RequestPause(false);
            double deadline=Time.realtimeSinceStartupAsDouble+45;
            bool moved=false,marker=false;
            while(Time.realtimeSinceStartupAsDouble<deadline&&!runtime.IsStopped)
            {
                routes.Service(runtime,profile.NavigationRequestsPerFrame);
                var view=runtime.ParticipantView(expertId);
                Assert.IsNull(view.Failure);
                moved|=view.Entities.Any(e=>e.Id==explorer.Id&&!e.Position.Equals(explorer.Position)&&e.OrderStamp?.Origin==PlayableOrderOrigin.Ai);
                marker|=view.IncomeEvents.Any(e=>e.Kind==PlayableBuildingKind.Headquarters&&e.Amount==profile.HeadquartersIncomePerPeriod*1.5);
                if(moved&&marker&&view.Tick>=150)break;
                yield return null;
            }
            Assert.True(moved,"Expert scout must execute an AI order and move through the normal host.");
            Assert.True(marker,"The HQ marker must show the income actually awarded to Expert.");
            Assert.IsNull(routes.LastProjectionFailure);
            runtime.RequestPause(true);
            var capture=runtime.RequestCaptureBytes();
            double captureDeadline=Time.realtimeSinceStartupAsDouble+15;
            while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<captureDeadline)
            {routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
            Assert.True(capture.IsCompleted);
            var bytes=capture.GetAwaiter().GetResult();
            var before=runtime.ParticipantView(expertId);
            var beforeVeteran=runtime.ParticipantView(veteranId);
            Assert.GreaterOrEqual(before.SettledIncome,profile.HeadquartersIncomePerPeriod*1.5-1e-8);
            Assert.GreaterOrEqual(beforeVeteran.SettledIncome,profile.HeadquartersIncomePerPeriod-1e-8);
            byte[] recaptured=null;
            restored=PlayableRuntime.RestoreBytesForTests(bytes,config,ai,r=>recaptured=r.CaptureBeforeStartForTests());
            CollectionAssert.AreEqual(bytes,recaptured,"Exact current envelope through the ordinary restore path.");
            Assert.AreEqual(bonus,restored.AiCheckpoint.Difficulty);
            var after=restored.ParticipantView(expertId);
            Assert.AreEqual(before.Tick,after.Tick);
            Assert.AreEqual(before.ExactCredits,after.ExactCredits);
            Assert.AreEqual(before.IncomePerSecond,after.IncomePerSecond);
            Assert.AreEqual(before.SettledIncome,after.SettledIncome);
            restoredRoutes=new UnityHostRouteService();
            restored.RequestPause(false);
            double resumeDeadline=Time.realtimeSinceStartupAsDouble+12;
            while(restored.Latest.Tick<before.Tick+3&&!restored.IsStopped&&Time.realtimeSinceStartupAsDouble<resumeDeadline)
            {restoredRoutes.Service(restored,profile.NavigationRequestsPerFrame);yield return null;}
            var resumed=restored.ParticipantView(expertId);
            Assert.IsNull(resumed.Failure);
            Assert.IsNull(restoredRoutes.LastProjectionFailure);
            Assert.Greater(resumed.SettledIncome,after.SettledIncome);
            TestContext.WriteLine($"AI_INCOME_RESTORE_PASS {store.ActiveIdentity}, seed 7, moved={moved}, {bonus}={before.IncomePerSecond}, {standard}={beforeVeteran.IncomePerSecond}, save={before.Tick}, resumed={resumed.Tick}, log={output}");
        }
        finally
        {
            Stop(restored,restoredRoutes);
            Stop(runtime,routes);
            TestMatchLogSession.Shutdown();
        }
    }

    private static void Stop(PlayableRuntime runtime,UnityHostRouteService routes)
    {
        if(runtime==null){routes?.Dispose();return;}
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",flags).GetValue(runtime);
        var worker=routes==null?null:(CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker",flags).GetValue(routes);
        runtime.RequestStop();
        routes?.Dispose();
        runtime.Dispose();
        Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped)&&authority.Domain.ObservedPathWorkerStopped,10000),
            "Match-owned workers must stop before domain destruction.");
        authority.Domain.Navigation.ReleaseCommittedHostRoot();
        authority.Domain.DestroyOwnedNavigation();
    }
}

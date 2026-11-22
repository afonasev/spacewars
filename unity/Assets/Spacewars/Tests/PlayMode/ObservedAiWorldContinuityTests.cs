using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class ObservedAiWorldContinuityTests
{
    [UnityTest,Timeout(60000)] public IEnumerator DelayedObservedPathKeepsOrdinaryUnityLoopAdvancing()=>RunDelayed(false);
    [UnityTest,Timeout(60000)] public IEnumerator DelayedOwnerReviewKeepsOrdinaryUnityLoopAdvancing()=>RunDelayed(true);
    [UnityTest,Timeout(120000)] public IEnumerator BasaltCrownBotsScoutThroughOrdinaryHost()=>RunJournalMap(false);
    [UnityTest,Timeout(120000)] public IEnumerator EightBotsAdvanceOnDeafMirrorThroughOrdinaryHost()=>RunJournalMap(true);

    private IEnumerator RunJournalMap(bool eight)
    {
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        var baseline=(PlayableProfile)typeof(PlayableBootstrap).GetMethod("LoadNormalProfile",flags).Invoke(null,null);
        string file=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"spacewars-liveness-"+Guid.NewGuid().ToString("N")+".json");
        var balance=NativeBalanceStore.ForApplication(baseline,file);
        Assert.True(balance.HasActive,balance.Problem);
        Assert.That(balance.ActiveIdentity,Does.Contain("spacewars-default").And.Contain("@20"));
        var lobby=new NativeLobbyConfiguration
        {
            BasaltCrown=!eight, DeafMirror=eight, HasExplicitSeed=true, ExplicitSeed=eight?1548132461:1548132460,
            Participants=Enumerable.Range(0,eight?8:2).Select(i=>new NativeLobbyParticipant
            {
                Name="Bot "+i, Color=i, Team=i%2==0?2:1,
                Difficulty=i%2==1?Spacewars.Simulation.Ai.AiDifficulty.Expert:
                    eight?Spacewars.Simulation.Ai.AiDifficulty.Cheater:Spacewars.Simulation.Ai.AiDifficulty.Veteran
            }).ToList()
        };
        var profile=(PlayableProfile)typeof(PlayableBootstrap).GetMethod("ComposeLobbyProfile",flags)
            .Invoke(null,new object[]{balance.Active,lobby});
        Assert.AreEqual(eight?"deaf-mirror-v1":"basalt-crown-native-v1",profile.AuthoredMap.Id);
        using var runtime=PlayableRuntime.CreateLobbyMatch(profile,71,lobby,startPaused:true,aiProfile:balance.ActiveAiProfile);
        using var routes=new UnityHostRouteService();
        var scheduler=(AiAuthorityScheduler)typeof(PlayableRuntime).GetField("aiScheduler",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
        var ages=new System.Collections.Concurrent.ConcurrentQueue<long>();
        var retries=new System.Collections.Concurrent.ConcurrentQueue<(string Owner,long Source,long Tick)>();
        scheduler.ReviewTelemetry+=(owner,source,tick,status,ms)=>
        {
            if(status=="adopted")ages.Enqueue(tick-source);
            if(status=="expired")retries.Enqueue((owner,source,tick));
        };
        var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
        var refusals=new System.Collections.Concurrent.ConcurrentQueue<string>();
        domain.Navigation.GroundRouteFailed+=(owner,status)=>
        {
            if(refusals.Count>=12)return;
            var request=owner.Request;
            refusals.Enqueue($"{request.Entity}:{request.Request}:{status}");
        };
        var origins=new Dictionary<int,NavPoint>();
        var moved=new HashSet<string>();
        var acted=new HashSet<string>();
        try
        {
            float deadline=Time.realtimeSinceStartup+90;
            while(!runtime.IsPrepared&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
            {routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
            Assert.True(runtime.IsPrepared,runtime.Latest?.Failure);
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,eight?4:1);
            runtime.RequestPause(false);
            long horizon=eight?4200:600;
            while(runtime.Latest.Tick<horizon&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline)
            {
                routes.Service(runtime,profile.NavigationRequestsPerFrame);
                foreach(var owner in scheduler.Owners)
                {
                    if(owner.Checkpoint.Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Applied&&
                        (r.Kind==PlayableCommandKind.Move||r.Kind==PlayableCommandKind.AttackMove)))
                        acted.Add(owner.OwnerId);
                    var perspective=runtime.ParticipantView(owner.OwnerId);
                    if(perspective==null)continue;
                    foreach(var actor in perspective.Entities.Where(u=>u.Owner==perspective.Owner&&!PlayableUnitRules.Aircraft(u.Kind)))
                    {
                        if(!origins.TryGetValue(actor.Id,out var origin))origins[actor.Id]=actor.Position;
                        else if(actor.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&
                            Math.Abs(actor.Position.X-origin.X)+Math.Abs(actor.Position.Z-origin.Z)>1)
                            moved.Add(owner.OwnerId);
                    }
                }
                yield return null;
            }
            var ordered=ages.OrderBy(a=>a).ToArray();
            TestContext.WriteLine($"map={profile.AuthoredMap.Id} seed={lobby.ExplicitSeed} tick={runtime.Latest.Tick} "+
                $"applied={string.Join(",",acted.OrderBy(x=>x))} moved={string.Join(",",moved.OrderBy(x=>x))} "+
                $"reviews={ordered.Length} median-age={(ordered.Length==0?-1:ordered[ordered.Length/2])} refreshed={retries.Count} "+
                $"refusals={string.Join(",",refusals)}");
            Assert.IsNull(runtime.Latest.Failure);
            Assert.IsNull(routes.LastProjectionFailure);
            Assert.GreaterOrEqual(runtime.Latest.Tick,horizon);
            CollectionAssert.AreEquivalent(scheduler.Owners.Select(o=>o.OwnerId),acted);
            CollectionAssert.AreEquivalent(scheduler.Owners.Select(o=>o.OwnerId),moved);
        }
        finally
        {
            runtime.RequestStop();
            routes.Dispose();
            Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&scheduler.ReviewWorkerStopped,5000));
            if(System.IO.File.Exists(file))System.IO.File.Delete(file);
        }
    }
    private IEnumerator RunDelayed(bool ownerReview)
    {
        var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
        var lobby=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7,Participants=new List<NativeLobbyParticipant>{new NativeLobbyParticipant{Name="AI",Team=1,Color=0},new NativeLobbyParticipant{Name="Human",Human=true,Team=2,Color=1}}};
        var basis=NativeLobbyMatch.Create(profile,lobby);var costs=new double[basis.Starts.Count,basis.Starts.Count];
        for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
        var roster=basis.Roster.Select((p,i)=>new OfflineParticipant(i==0?"owner-11":"owner-28",p.LogicalPlayer,p.Team,i==0?OfflineControl.Ai:OfflineControl.Human)).ToArray();
        var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,roster,basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,terrain:profile.AuthoredMap.Id);
        var runtime=new PlayableRuntime(config,71,startPaused:true);var routes=new UnityHostRouteService();
        var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();int parked=0;
        var scheduler=(AiAuthorityScheduler)typeof(PlayableRuntime).GetField("aiScheduler",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
        var timings=new System.Collections.Concurrent.ConcurrentQueue<(long Source,long Tick,string Status,double Milliseconds)>();
        scheduler.ReviewTelemetry+=(owner,source,tick,status,ms)=>timings.Enqueue((source,tick,status,ms.ComputeMilliseconds??-1));
        string threadName=null;
        try{
            Action park=()=>{if(Interlocked.CompareExchange(ref parked,1,0)==0){threadName=Thread.CurrentThread.Name;entered.Set();if(!release.Wait(10000))throw new TimeoutException("Observed Unity boundary not released.");}};
            if(ownerReview)domain.BeforeOwnerReviewForTests=park;else domain.BeforeObservedPathForTests=park;
            runtime.RequestPause(false);float deadline=Time.realtimeSinceStartup+15;
            while(!entered.IsSet&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
            Assert.True(entered.IsSet,"Real ordinary AI reaches observed path computation.");Assert.False(runtime.IsStopped,runtime.Latest.Failure);
            var capture=ownerReview?runtime.RequestCaptureBytes():null;
            if(ownerReview)Assert.AreEqual("Spacewars.AI.Decisions",threadName);
            long tick=runtime.Latest.Tick;int frames=0;deadline=Time.realtimeSinceStartup+2;
            while(runtime.Latest.Tick<tick+10&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,profile.NavigationRequestsPerFrame);frames++;yield return null;}
            Assert.GreaterOrEqual(runtime.Latest.Tick,tick+10,"Delayed AI work must not hold ordinary world time.");Assert.Greater(frames,0);Assert.False(runtime.IsStopped,runtime.Latest.Failure);
            if(ownerReview){
                Assert.False(capture.IsCompleted,"A save waiting for this calculation must leave simulation running.");
                Assert.True(timings.Any(row=>row.Status=="pending"&&row.Tick>row.Source));
            }
            release.Set();deadline=Time.realtimeSinceStartup+15;var start=runtime.ParticipantView("owner-11").Entities.Single(e=>e.Owner==PlayableOwner.Player).Position;bool moved=false;
            while(!moved&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline){
                routes.Service(runtime,profile.NavigationRequestsPerFrame);
                moved=runtime.ParticipantView("owner-11").Entities.Any(e=>e.Owner==PlayableOwner.Player&&!e.Position.Equals(start)&&e.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&(e.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move||e.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove));
                yield return null;
            }
            if(ownerReview){
                Assert.True(capture.Status==System.Threading.Tasks.TaskStatus.RanToCompletion);
                var pending=timings.First(row=>row.Status=="pending");
                Assert.True(timings.Any(row=>row.Source==pending.Source && row.Status!="pending" &&
                    row.Tick>=pending.Tick && row.Milliseconds>=0));
            }
            Assert.True(moved,"The released observation must lead to an actual AI movement order serviced by Unity.");Assert.IsNull(routes.LastProjectionFailure);Assert.False(runtime.IsStopped,runtime.Latest.Failure);
        }finally{
            domain.BeforeObservedPathForTests=null;domain.BeforeOwnerReviewForTests=null;release.Set();runtime.RequestStop();routes.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&domain.ObservedPathWorkerStopped&&!domain.Navigation.HasCertifiedHost,5000));runtime.Dispose();domain.DestroyOwnedNavigation();
        }
    }
}

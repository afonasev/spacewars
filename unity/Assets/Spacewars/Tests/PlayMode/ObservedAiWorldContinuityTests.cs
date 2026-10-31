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
        try{
            Action park=()=>{if(Interlocked.CompareExchange(ref parked,1,0)==0){entered.Set();if(!release.Wait(10000))throw new TimeoutException("Observed Unity boundary not released.");}};
            if(ownerReview)domain.BeforeOwnerReviewForTests=park;else domain.BeforeObservedPathForTests=park;
            runtime.RequestPause(false);float deadline=Time.realtimeSinceStartup+15;
            while(!entered.IsSet&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
            Assert.True(entered.IsSet,"Real ordinary AI reaches observed path computation.");Assert.False(runtime.IsStopped,runtime.Latest.Failure);
            long tick=runtime.Latest.Tick;int frames=0;deadline=Time.realtimeSinceStartup+2;
            while(runtime.Latest.Tick<tick+10&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,profile.NavigationRequestsPerFrame);frames++;yield return null;}
            Assert.GreaterOrEqual(runtime.Latest.Tick,tick+10,"Delayed AI work must not hold ordinary world time.");Assert.Greater(frames,0);Assert.False(runtime.IsStopped,runtime.Latest.Failure);
            release.Set();deadline=Time.realtimeSinceStartup+15;var start=runtime.ParticipantView("owner-11").Entities.Single(e=>e.Owner==PlayableOwner.Player).Position;bool moved=false;
            while(!moved&&!runtime.IsStopped&&Time.realtimeSinceStartup<deadline){
                routes.Service(runtime,profile.NavigationRequestsPerFrame);
                moved=runtime.ParticipantView("owner-11").Entities.Any(e=>e.Owner==PlayableOwner.Player&&!e.Position.Equals(start)&&e.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&(e.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move||e.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove));
                yield return null;
            }
            Assert.True(moved,"The released observation must lead to an actual AI movement order serviced by Unity.");Assert.IsNull(routes.LastProjectionFailure);Assert.False(runtime.IsStopped,runtime.Latest.Failure);
        }finally{
            domain.BeforeObservedPathForTests=null;domain.BeforeOwnerReviewForTests=null;release.Set();runtime.RequestStop();routes.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&domain.ObservedPathWorkerStopped,5000));runtime.Dispose();domain.DestroyOwnedNavigation();
        }
    }
}

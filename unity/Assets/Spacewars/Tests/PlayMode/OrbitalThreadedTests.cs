using System;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.PlayMode
{
    public sealed class OrbitalThreadedTests
    {
        [Test,Timeout(45000)] public void HostileCombatUnlocksChoiceCommandsStartCooldownAndPauseFreezesIt()
        {
            var data=PlayableProfile.Default.CopyData();data.orbitalThreshold1=1;var map=new AiTestMap();var profile=PlayableProfile.Create(data,map);
            var basis=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var scenario=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-38,-38)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-30,-35))};
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:scenario,terrain:map.Id);
            var runtime=new PlayableRuntime(config,901,startPaused:true);var host=new UnityHostRouteService();var clock=Stopwatch.StartNew();
            void Wait(Func<bool> predicate){while(!predicate()&&clock.Elapsed.TotalSeconds<35){host.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Thread.Sleep(1);}Assert.True(predicate(),"threaded orbital deadline");}
            try{
                Wait(()=>runtime.IsPrepared&&runtime.Latest.Paused);runtime.RequestPause(false);Wait(()=>runtime.Latest.Orbital.Level>=1);
                Assert.True(runtime.TrySubmit(new PlayableCommand(901,1,"player-1",PlayableCommandKind.ChooseOrbital,null,targetId:1)).Accepted);Wait(()=>runtime.Latest.Orbital.Abilities.Count==1);
                Assert.True(runtime.TrySubmit(new PlayableCommand(901,2,"player-1",PlayableCommandKind.UseOrbital,null,targetId:1)).Accepted);Wait(()=>runtime.Latest.Orbital.Abilities[0].Remaining>0);
                Assert.Greater(runtime.Latest.ScanLines.Count,0,"scan is published through the ordinary host");Assert.Greater(runtime.Latest.Orbital.CommonRemaining,0);Assert.AreEqual(0,runtime.Latest.Orbital.PendingLevel);
                runtime.RequestPause(true);Wait(()=>runtime.Latest.Paused);double remaining=runtime.Latest.Orbital.Abilities[0].Remaining;long tick=runtime.Latest.Tick;var lines=runtime.Latest.ScanLines.ToArray();
                Thread.Sleep(120);host.Service(runtime,profile.NavigationRequestsPerFrame);Assert.AreEqual(tick,runtime.Latest.Tick);Assert.AreEqual(remaining,runtime.Latest.Orbital.Abilities[0].Remaining);CollectionAssert.AreEqual(lines,runtime.Latest.ScanLines,"pause freezes the scan sector too");
                runtime.RequestPause(false);Wait(()=>runtime.Latest.Orbital.Abilities[0].Remaining<remaining);
            }finally{runtime.RequestStop();while(!runtime.IsStopped&&clock.Elapsed.TotalSeconds<40){host.Service(runtime,profile.NavigationRequestsPerFrame);Thread.Sleep(1);}runtime.Dispose();host.Dispose();}
        }
        [Test,Timeout(45000)] public void ActualDamageReachesMaxAndAllFiveChoicesUseDoubleRecovery()
        {
            var data=PlayableProfile.Default.CopyData();data.orbitalThreshold1=data.orbitalThreshold2=data.orbitalThreshold3=data.orbitalThreshold4=data.orbitalThreshold5=1;
            var map=new AiTestMap();var profile=PlayableProfile.Create(data,map);var basis=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-38,-38)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-30,-35))},terrain:map.Id);
            var runtime=new PlayableRuntime(config,903,startPaused:true);var host=new UnityHostRouteService();var clock=Stopwatch.StartNew();
            void Wait(Func<bool> predicate){while(!predicate()&&clock.Elapsed.TotalSeconds<35){host.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Thread.Sleep(1);}Assert.True(predicate(),"threaded MAX deadline");}
            try{
                Wait(()=>runtime.IsPrepared&&runtime.Latest.Paused);runtime.RequestPause(false);Wait(()=>runtime.Latest.Orbital.Level==6);Assert.Zero(runtime.Latest.Orbital.Progress);
                long sequence=0;foreach(int id in new[]{1,2,3,4,6})Assert.True(runtime.TrySubmit(new PlayableCommand(903,++sequence,"player-1",PlayableCommandKind.ChooseOrbital,null,targetId:id)).Accepted);
                Wait(()=>runtime.Latest.Orbital.Abilities.Count==5);Assert.Zero(runtime.Latest.Orbital.PendingLevel);
                Assert.True(runtime.TrySubmit(new PlayableCommand(903,++sequence,"player-1",PlayableCommandKind.UseOrbital,null,targetId:3)).Accepted);Wait(()=>runtime.Latest.Orbital.Abilities.Single(a=>a.Id==3).Recovery>0);
                var before=runtime.Latest;Wait(()=>runtime.Latest.Tick>=before.Tick+3);var after=runtime.Latest;double dt=(after.Tick-before.Tick)/30d;
                Assert.AreEqual(dt*2,before.Orbital.Abilities.Single(a=>a.Id==3).Recovery-after.Orbital.Abilities.Single(a=>a.Id==3).Recovery,1e-7);Assert.AreEqual(dt,before.Orbital.CommonRemaining-after.Orbital.CommonRemaining,1e-7);
                runtime.RequestPause(true);Wait(()=>runtime.Latest.Paused);var paused=runtime.Latest;Thread.Sleep(100);host.Service(runtime,profile.NavigationRequestsPerFrame);Assert.AreEqual(paused.Tick,runtime.Latest.Tick);Assert.AreEqual(paused.Orbital.Abilities[2].Recovery,runtime.Latest.Orbital.Abilities[2].Recovery);
            }finally{runtime.RequestStop();while(!runtime.IsStopped&&clock.Elapsed.TotalSeconds<40){host.Service(runtime,profile.NavigationRequestsPerFrame);Thread.Sleep(1);}runtime.Dispose();host.Dispose();}
        }
    }
}

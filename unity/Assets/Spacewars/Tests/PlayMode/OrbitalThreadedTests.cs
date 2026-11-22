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
            var data=PlayableProfile.Default.CopyData();data.orbitalThreshold1=data.orbitalThreshold2=data.orbitalThreshold3=data.orbitalThreshold4=data.orbitalThreshold5=data.orbitalThresholdMax=1;
            var map=new AiTestMap();var profile=PlayableProfile.Create(data,map);var basis=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-38,-38)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-30,-35))},terrain:map.Id);
            var runtime=new PlayableRuntime(config,903,startPaused:true);var host=new UnityHostRouteService();var clock=Stopwatch.StartNew();
            void Wait(Func<bool> predicate){while(!predicate()&&clock.Elapsed.TotalSeconds<35){host.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Thread.Sleep(1);}Assert.True(predicate(),"threaded MAX deadline");}
            try{
                Wait(()=>runtime.IsPrepared&&runtime.Latest.Paused);runtime.RequestPause(false);Wait(()=>runtime.Latest.Orbital.Level==6);Assert.Zero(runtime.Latest.Orbital.Progress);
                long sequence=0;foreach(int id in new[]{1,2,3,4,6})Assert.True(runtime.TrySubmit(new PlayableCommand(903,++sequence,"player-1",PlayableCommandKind.ChooseOrbital,null,targetId:id)).Accepted);
                Wait(()=>runtime.Latest.Orbital.Abilities.Count==5);Assert.Zero(runtime.Latest.Orbital.PendingLevel);
                // Scan uses the same orbital recovery clock and has no target-visibility precondition.
                long useSequence=++sequence;
                Assert.True(runtime.TrySubmit(new PlayableCommand(903,useSequence,"player-1",PlayableCommandKind.UseOrbital,null,targetId:1)).Accepted);
                Wait(()=>runtime.ReceiptsAfter(useSequence-1).Any(receipt=>receipt.Sequence==useSequence));
                var useReceipt=runtime.ReceiptsAfter(useSequence-1).Single(receipt=>receipt.Sequence==useSequence);
                Assert.AreEqual(PlayableCommandStatus.Applied,useReceipt.Status,useReceipt.Message);
                Wait(()=>runtime.Latest.Orbital.Abilities.Single(a=>a.Id==1).Recovery>0);
                var before=runtime.Latest;Wait(()=>runtime.Latest.Tick>=before.Tick+3);var after=runtime.Latest;double dt=(after.Tick-before.Tick)/30d;
                Assert.AreEqual(dt*2,before.Orbital.Abilities.Single(a=>a.Id==1).Recovery-after.Orbital.Abilities.Single(a=>a.Id==1).Recovery,1e-7);Assert.AreEqual(dt,before.Orbital.CommonRemaining-after.Orbital.CommonRemaining,1e-7);
                runtime.RequestPause(true);Wait(()=>runtime.Latest.Paused);var paused=runtime.Latest;Thread.Sleep(100);host.Service(runtime,profile.NavigationRequestsPerFrame);Assert.AreEqual(paused.Tick,runtime.Latest.Tick);Assert.AreEqual(paused.Orbital.Abilities.Single(a=>a.Id==1).Recovery,runtime.Latest.Orbital.Abilities.Single(a=>a.Id==1).Recovery);
            }finally{runtime.RequestStop();while(!runtime.IsStopped&&clock.Elapsed.TotalSeconds<40){host.Service(runtime,profile.NavigationRequestsPerFrame);Thread.Sleep(1);}runtime.Dispose();host.Dispose();}
        }
        [Test,Timeout(45000)]
        public void ReinforcementAcceptsAnOrderAndWaitsForMaterializationInTheThreadedMatch()
        {
            var data = PlayableProfile.Default.CopyData();
            data.orbitalThreshold1 = data.orbitalThreshold2 = data.orbitalThreshold3 = 1;
            data.startingCredits = 1500;
            var map = new AiTestMap();
            var profile = PlayableProfile.Create(data, map);
            var basis = map.Configuration(profile, 7, new[] {
                new OfflineParticipant("player-1",1,1,OfflineControl.Human),
                new OfflineParticipant("player-2",2,2,OfflineControl.Human)
            });
            var costs = new double[basis.Starts.Count, basis.Starts.Count];
            for(int a=0;a<basis.Starts.Count;a++)
                for(int b=0;b<basis.Starts.Count;b++) costs[a,b]=basis.RouteCost(a,b);
            var config = new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,
                basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,
                scenario:new[] {
                    new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-38,-38)),
                    new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-30,-35))
                },terrain:map.Id);
            var runtime = new PlayableRuntime(config, 904, startPaused:true);
            var host = new UnityHostRouteService();
            var clock = Stopwatch.StartNew();
            long sequence = 0;
            void Wait(Func<bool> condition, string description)
            {
                while(!condition() && clock.Elapsed.TotalSeconds < 36)
                {
                    host.Service(runtime, profile.NavigationRequestsPerFrame);
                    Assert.IsNull(runtime.Latest.Failure);
                    Thread.Sleep(1);
                }
                Assert.True(condition(), description);
            }
            PlayableCommandStatus Send(PlayableCommandKind kind, NavPoint point = default, int[] ids = null,
                int ability = OrbitalAbilities.Reinforcement)
            {
                long sent = ++sequence;
                Assert.True(runtime.TrySubmit(new PlayableCommand(904,sent,"player-1",kind,ids,point,targetId:ability,
                    origin:PlayableOrderOrigin.Human)).Accepted);
                Wait(()=>runtime.ReceiptsAfter(sent-1).Any(receipt=>receipt.Sequence==sent), kind+" receipt");
                return runtime.ReceiptsAfter(sent-1).Single(receipt=>receipt.Sequence==sent).Status;
            }
            try
            {
                Wait(()=>runtime.IsPrepared && runtime.Latest.Paused, "threaded match prepared");
                runtime.RequestPause(false);
                Wait(()=>runtime.Latest.Orbital.Level>=3, "actual damage unlocks advanced ability");
                foreach(int id in new[]{1,2,OrbitalAbilities.Reinforcement})
                    Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ChooseOrbital,ability:id));
                Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.BeginOrbitalReinforcement));
                var home = runtime.Latest.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
                var center = new NavPoint(home.Position.X+5,home.Position.Z+4);
                Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.UseOrbital,center));
                Wait(()=>runtime.Latest.Entities.Count(e=>e.MaterializationProgress<1)==3,"whole wave published");
                var wave = runtime.Latest.Entities.Where(e=>e.MaterializationProgress<1).ToArray();
                var ids = wave.Select(e=>e.Id).ToArray();
                Assert.AreEqual(PlayableCommandStatus.Applied,
                    Send(PlayableCommandKind.Move,new NavPoint(center.X+5,center.Z+4),ids));
                runtime.RequestPause(true);
                Wait(()=>runtime.Latest.Paused,"pause applied");
                var paused = runtime.Latest;
                Thread.Sleep(100);
                host.Service(runtime,profile.NavigationRequestsPerFrame);
                Assert.AreEqual(paused.Tick,runtime.Latest.Tick);
                foreach(var unit in wave)
                {
                    var visible = runtime.Latest.Entities.Single(e=>e.Id==unit.Id);
                    Assert.AreEqual(unit.Position,visible.Position);
                    Assert.Less(visible.MaterializationProgress,1);
                }
                runtime.RequestPause(false);
                Wait(()=>runtime.Latest.Entities.Where(e=>ids.Contains(e.Id)).All(e=>e.MaterializationProgress>=1),
                    "arrival completes");
                Wait(()=>runtime.Latest.Entities.Any(e=>ids.Contains(e.Id)&&!e.Position.Equals(wave.Single(u=>u.Id==e.Id).Position)),
                    "an ordinary route starts moving the wave after materialization");
            }
            finally
            {
                runtime.RequestStop();
                while(!runtime.IsStopped&&clock.Elapsed.TotalSeconds<42){host.Service(runtime,profile.NavigationRequestsPerFrame);Thread.Sleep(1);}
                runtime.Dispose();
                host.Dispose();
            }
        }
    }
}

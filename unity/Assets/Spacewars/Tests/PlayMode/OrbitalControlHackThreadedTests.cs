using System;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class OrbitalControlHackThreadedTests
    {
        [Test,Timeout(45000)] public void RealCombatUnlocksHackTransfersControlAndNewOwnerMovesCapturedUnit()
        {
            var data=PlayableProfile.Default.CopyData();data.orbitalThreshold1=1;
            var map=new AiTestMap();var profile=PlayableProfile.Create(data,map);
            var basis=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var scenario=new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-38,-38)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-30,-35))};
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:scenario,terrain:map.Id);
            var clock=Stopwatch.StartNew();var runtime=new PlayableRuntime(config,908,startPaused:true);CertifiedRouteWorker host=null;
            void Wait(Func<bool> predicate,string phase){while(!predicate()&&clock.Elapsed.TotalSeconds<35){Assert.IsNull(runtime.Latest.Failure,phase);Thread.Sleep(1);}Assert.True(predicate(),phase);}
            try {
                host=new CertifiedRouteWorker(runtime.NavigationBinding,profile.NavigationRequestsPerFrame,()=>runtime.IsStopRequested,runtime.ReportRouteFailure);
                Wait(()=>runtime.IsPrepared&&runtime.Latest.Paused,"prepared paused");runtime.RequestPause(false);
                Wait(()=>runtime.Latest.Orbital.Level>=1,"real hostile damage unlocks I");
                Assert.True(runtime.TrySubmit(new PlayableCommand(908,1,"player-1",PlayableCommandKind.ChooseOrbital,null,targetId:OrbitalAbilities.ControlHack)).Accepted);
                Wait(()=>runtime.Latest.Orbital.Abilities.Any(a=>a.Id==OrbitalAbilities.ControlHack),"hack chosen");
                Wait(()=>runtime.Latest.Entities.Any(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank&&e.ExactHealth<e.MaxHealth*.5),"enemy below half health");
                var target=runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank&&e.ExactHealth<e.MaxHealth*.5);long before=runtime.Latest.Tick;
                Assert.True(runtime.TrySubmit(new PlayableCommand(908,2,"player-1",PlayableCommandKind.UseOrbital,new[]{target.Id},targetId:OrbitalAbilities.ControlHack)).Accepted);
                Wait(()=>runtime.Latest.Entities.Any(e=>e.Id==target.Id&&e.Owner==PlayableOwner.Player),"ownership transferred by worker");
                Assert.Greater(runtime.Latest.Orbital.Abilities[0].Remaining,0);
                Wait(()=>runtime.ParticipantView("player-2").MatchEvents.Any(e=>e.Kind==MatchEventKind.UnitCaptured),"former owner notification published");
                var captured=runtime.Latest.Entities.Single(e=>e.Id==target.Id);Assert.Null(captured.CurrentOrder);Assert.AreEqual(PlayableOwner.Player,captured.OrderStamp.Owner);
                var goal=new NavPoint(captured.Position.X+3,captured.Position.Z+3);
                Assert.True(runtime.TrySubmit(new PlayableCommand(908,3,"player-1",PlayableCommandKind.Move,new[]{target.Id},target:goal).AsHuman()).Accepted);
                Wait(()=>runtime.Latest.Entities.Single(e=>e.Id==target.Id).Position.X>captured.Position.X+.2,"new owner command produces actual motion");
                var moved=runtime.Latest.Entities.Single(e=>e.Id==target.Id);
                Console.WriteLine("THREADED hack seed=7 generation=908 target="+target.Id+" capture_from_tick="+before+" motion_tick="+runtime.Latest.Tick+" deltaX="+(moved.Position.X-captured.Position.X));
                Assert.True(runtime.TrySubmit(new PlayableCommand(908,1,"player-2",PlayableCommandKind.Stop,new[]{target.Id})).Accepted);
                bool rejected=false;Wait(()=>{rejected|=runtime.DrainReceipts().Any(r=>r.OwnerId=="player-2"&&r.Sequence==1&&r.Status==PlayableCommandStatus.InvalidEntity);return rejected;},"old owner loses command authority");
                runtime.RequestPause(true);Wait(()=>runtime.Latest.Paused,"pause");double cooldown=runtime.Latest.Orbital.Abilities[0].Remaining;long tick=runtime.Latest.Tick;Thread.Sleep(120);Assert.AreEqual(tick,runtime.Latest.Tick);Assert.AreEqual(cooldown,runtime.Latest.Orbital.Abilities[0].Remaining);
                runtime.RequestPause(false);Wait(()=>runtime.Latest.Orbital.Abilities[0].Remaining<cooldown,"resume");
            } finally {runtime.RequestStop();while(!runtime.IsStopped&&clock.Elapsed.TotalSeconds<40)Thread.Sleep(1);runtime.Dispose();host?.Dispose();}
        }
    }
}

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class OrbitalMineThreadedTests
    {
        [Test,Timeout(45000)] public void OrdinaryHostMovesEnemyIntoMineAndPauseFreezesBurst()
        {
            var data=PlayableProfile.Default.CopyData();data.orbitalThreshold1=1;data.tankWeaponDamage=1;data.tankWeaponReloadMilliseconds=10000;
            var map=new AiTestMap();var profile=PlayableProfile.Create(data,map);var basis=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-10,0)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-3,0))},terrain:map.Id);
            var runtime=new PlayableRuntime(config,902,startPaused:true);var host=new UnityHostRouteService();var clock=Stopwatch.StartNew();
            void Wait(Func<bool> predicate){while(!predicate()&&clock.Elapsed.TotalSeconds<35){host.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Thread.Sleep(1);}Assert.True(predicate(),"threaded mine deadline tick="+runtime.Latest.Tick+" mines="+string.Join(";",runtime.Latest.Mines.Select(m=>m.Id+"@"+m.Point.X+","+m.Point.Z))+" bursts="+runtime.Latest.MineBlasts.Count+" actors="+string.Join(";",runtime.Latest.Entities.Select(e=>e.Id+":"+e.Owner+"@"+e.Position.X+","+e.Position.Z+" hp="+e.Health+" outcome="+e.NavigationOutcome+" moving="+e.Moving)));}
            try{
                Wait(()=>runtime.IsPrepared&&runtime.Latest.Paused);runtime.RequestPause(false);Wait(()=>runtime.Latest.Orbital.Level==1);
                Assert.True(runtime.TrySubmit(new PlayableCommand(902,1,"player-1",PlayableCommandKind.ChooseOrbital,null,targetId:2)).Accepted);Wait(()=>runtime.Latest.Orbital.Abilities.Count==1);
                Assert.True(runtime.TrySubmit(new PlayableCommand(902,2,"player-1",PlayableCommandKind.UseOrbital,null,new NavPoint(-6,1),targetId:2)).Accepted);Wait(()=>runtime.Latest.Mines.Count==1);
                var speech=new NativeSpeechPlan(new GameplayAudioProfile());
                GameplayAudioScreen[] Screens()=>new[]{new GameplayAudioScreen(runtime.ParticipantView("player-1"),default,new NavPoint(1,0)),new GameplayAudioScreen(runtime.ParticipantView("player-2"),default,new NavPoint(1,0))};
                speech.Observe(Screens(),0,true);
                var enemy=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Tank);int before=enemy.Health;
                Assert.True(runtime.TrySubmit(new PlayableCommand(902,1,"player-2",PlayableCommandKind.Move,new[]{enemy.Id},new NavPoint(-6,1))).Accepted);
                Wait(()=>runtime.Latest.Entities.Any(e=>e.Id==enemy.Id&&Math.Abs(e.Position.X-enemy.Position.X)>.1));
                Wait(()=>runtime.Latest.Mines.Count==0&&runtime.Latest.MineBlasts.Count==1);
                Assert.AreEqual(before-80,runtime.Latest.Entities.Single(e=>e.Id==enemy.Id).Health,"primary is not hit by its own splash");Assert.AreEqual(1.4,runtime.Latest.MineBlasts[0].Radius);
                speech.Observe(Screens(),1,true);Assert.AreEqual("system-mine-hit",speech.Next(1,false)?.Key,"Real host must route only the victim voice on one device");
                Assert.True(runtime.ParticipantView("player-1").MatchEvents.Any(e=>e.Kind==MatchEventKind.MineTriggered));
                Assert.True(runtime.ParticipantView("player-2").MatchEvents.Any(e=>e.Kind==MatchEventKind.MineHit));
                runtime.RequestPause(true);Wait(()=>runtime.Latest.Paused);long tick=runtime.Latest.Tick;var burst=runtime.Latest.MineBlasts[0];Thread.Sleep(100);host.Service(runtime,profile.NavigationRequestsPerFrame);Assert.AreEqual(tick,runtime.Latest.Tick);Assert.AreEqual(burst.Tick,runtime.Latest.MineBlasts[0].Tick);
            }finally{runtime.RequestStop();while(!runtime.IsStopped&&clock.Elapsed.TotalSeconds<40){host.Service(runtime,profile.NavigationRequestsPerFrame);Thread.Sleep(1);}runtime.Dispose();host.Dispose();}
        }
    }
}

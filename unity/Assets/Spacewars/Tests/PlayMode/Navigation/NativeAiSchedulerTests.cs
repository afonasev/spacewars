using System;
using System.Linq;
using System.Collections;
using UnityEngine.TestTools;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class NativeAiSchedulerTests
    {
        private static OfflineMatchConfiguration Config(bool obstacle=true,bool combat=false,bool ai=false)
        {
            var p=PlayableProfile.Default;
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,-24),new NavPoint(-18,-24),pin:1),new OfflineStart("east",2,new NavPoint(24,24),new NavPoint(18,24),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,Array.Empty<TerritorySlot>()),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>()),new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(20,0),Array.Empty<TerritorySlot>())};
            var roster=new[]{new OfflineParticipant("west-owner",1,1,ai?OfflineControl.Ai:OfflineControl.Human),new OfflineParticipant("east-owner",2,2,ai?OfflineControl.Ai:OfflineControl.Human)};
            var units=combat?new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-10,20)),new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(10,20))}:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-20,0)),new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-20,-18))};
            return new OfflineMatchConfiguration(p,"native-f5-harness","f5-flat-wall-v1","UnityHostRouteService",19092026,roster,starts,sites,obstacle?new[]{new NavObstacle(-3,-12,3,12)}:Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:units);
        }
        private static void Advance(PlayableAuthorityTick cycle,UnityHostRouteService service,PlayableProfile profile,int budget=1)
        {
            for(int attempts=0;attempts<512;attempts++)
            {
                if(cycle.TryAdvance())return;
                service.Service(cycle,budget);
            }
            Assert.Fail("Fixed route barrier did not complete");
        }
        [Test] public void SharedTick()
        {
            string Run(int budget)
            {
                var config=Config(ai:true);var cycle=new PlayableAuthorityTick(config,71);
                using(var service=new UnityHostRouteService())for(int tick=0;tick<150;tick++)Advance(cycle,service,config.Profile,budget);
                return PlayableAiCanonical.Hash(PlayableAiCanonical.Encode(cycle.Latest));
            }
            Assert.AreEqual(Run(1),Run(64),"Host batch size cannot change logical authority result");
        }
        [Test] public void RouteBarrier()
        {
            var config=Config();var cycle=new PlayableAuthorityTick(config,71);
            int tank=cycle.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank).Id;
            var goal=new NavPoint(20,-8);var initial=cycle.Latest.Entities.Single(e=>e.Id==tank).Position;
            Assert.AreEqual(PlayableCommandStatus.Applied,cycle.Apply(new PlayableCommand(71,1,"west-owner",PlayableCommandKind.Move,new[]{tank},goal)).Status);
            for(int delay=0;delay<20;delay++)Assert.False(cycle.TryAdvance());
            Assert.Zero(cycle.Tick);Assert.AreEqual(initial,cycle.Latest.Entities.Single(e=>e.Id==tank).Position);
            using(var service=new UnityHostRouteService())
            {
                for(int tick=0;tick<1500;tick++)
                {
                    Advance(cycle,service,config.Profile);
                    var entity=cycle.Latest.Entities.Single(e=>e.Id==tank);
                    Assert.True(cycle.NavigationGeometry.IsFree(entity.Position,config.Profile.TankCollisionRadius));
                    if(entity.NavigationOutcome==NavigationOutcome.Arrived){Assert.LessOrEqual(Math.Sqrt(Math.Pow(goal.X-entity.Position.X,2)+Math.Pow(goal.Z-entity.Position.Z,2)),config.Profile.Navigation.ArrivalTolerance);Assert.Greater(cycle.Tick,30,"Movement cannot teleport");return;}
                }
            }
            Assert.Fail("Tank must navigate around the actual obstacle using stock NavMesh");
        }
        [Test] public void SameTankSolverAndExplicitFailure()
        {
            var config=Config();var geometry=new NavGeometry(50,new[]{new NavObstacle(-3,-12,3,12)},1);var p=config.Profile;
            var request=new NavigationRequest(71,1,1,1,p.Navigation,geometry,new NavPoint(-20,0),new NavPoint(20,0));
            var requests=new NavMailbox<NavigationRequest>();var answers=new NavMailbox<NavigationAnswer>();requests.TryEnqueue(request);
            NavPoint[] actual;
            using(var service=new UnityHostRouteService()){service.Service(requests,answers,71,geometry,p,1);Assert.True(answers.TryDequeue(out var answer));actual=answer.CopyRoute();}
            using(var direct=new UnityNavigationRouter(geometry,p.Navigation))CollectionAssert.AreEqual(direct.FindPath(request.Start,request.Goal),actual);
            Assert.IsNotEmpty(actual);
            var blocked=new NavGeometry(50,new[]{new NavObstacle(-2,-50,2,50)},2);
            requests.TryEnqueue(new NavigationRequest(71,2,1,2,p.Navigation,blocked,request.Start,request.Goal));
            using(var service=new UnityHostRouteService()){service.Service(requests,answers,71,blocked,p,1);Assert.True(answers.TryDequeue(out var answer));Assert.IsEmpty(answer.CopyRoute(),"No substitute solver for a failed tank NavMesh route");}
        }
        [Test] public void CommonServiceCaptureAndCombat()
        {
            var config=Config();var cycle=new PlayableAuthorityTick(config,71);
            int scout=cycle.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Explorer&&e.Position.Equals(new NavPoint(-20,-18))).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,cycle.Apply(new PlayableCommand(71,1,"west-owner",PlayableCommandKind.Move,new[]{scout},new NavPoint(20,0))).Status);
            using(var service=new UnityHostRouteService())
            {
                for(int tick=0;tick<1800;tick++){Advance(cycle,service,config.Profile);if(cycle.Latest.Sites.FirstOrDefault(s=>s.Site.Id==3)?.Progress>=1)break;}
                Assert.AreEqual(1,cycle.Latest.Sites.Single(s=>s.Site.Id==3).Progress,"Capture follows actual routed travel");
            }
            config=Config(obstacle:false,combat:true);cycle=new PlayableAuthorityTick(config,71);
            int tank=cycle.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,cycle.Apply(new PlayableCommand(71,1,"west-owner",PlayableCommandKind.AttackMove,new[]{tank},new NavPoint(10,20))).Status);
            using(var service=new UnityHostRouteService())
            {
                for(int tick=0;tick<900;tick++){Advance(cycle,service,config.Profile);if(cycle.Latest.Entities.Any(e=>e.Kind==PlayableEntityKind.Tank&&e.Health<config.Profile.TankHealth))return;}
            }
            Assert.Fail("Routed attack must cause ordinary domain combat damage");
        }
        [UnityTest] public IEnumerator RealtimeRuntimeUsesSameRouteBarrier()
        {
            var config=Config();var runtime=new PlayableRuntime(config,71,startPaused:true);
            try
            {
                var timeout=System.Diagnostics.Stopwatch.StartNew();
                while(!runtime.Latest.Paused&&timeout.Elapsed.TotalSeconds<4)yield return null;
                Assert.True(runtime.Latest.Paused);
                int tank=runtime.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank).Id;
                runtime.RequestPause(false);
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(71,1,"west-owner",PlayableCommandKind.Move,new[]{tank},new NavPoint(20,-8))).Status);
                while(runtime.Requests.Count==0&&timeout.Elapsed.TotalSeconds<4)yield return null;
                Assert.Greater(runtime.Requests.Count,0);long tick=runtime.Latest.Tick;
                var delay=System.Diagnostics.Stopwatch.StartNew();while(delay.Elapsed.TotalSeconds<.1)yield return null;
                Assert.AreEqual(tick,runtime.Latest.Tick,"Realtime ticks also wait at the same delivery barrier");
                using(var service=new UnityHostRouteService())
                {
                    while(runtime.Latest.Tick<=tick&&timeout.Elapsed.TotalSeconds<4){service.Service(runtime,1);yield return null;}
                    Assert.Greater(runtime.Latest.Tick,tick);Assert.AreEqual(RuntimeStatus.Running,runtime.Latest.Status);
                    Assert.True(runtime.Latest.Entities.Single(e=>e.Id==tank).Moving);
                }
            }
            finally{runtime.RequestStop();}
            var stopped=System.Diagnostics.Stopwatch.StartNew();while(!runtime.IsStopped&&stopped.Elapsed.TotalSeconds<4)yield return null;
            Assert.True(runtime.IsStopped,"Owned runtime must finish before the next fixture");
        }
        // Realtime owner control requires the ordinary Unity host. Without route service,
        // production/scout jobs can pin the fixed barrier and leave accepted human Stop queued.
        [UnityTest] public IEnumerator OrdinaryOwnerAiCannotReplaceHumanHoldButHumanStopCan()
        {
            var runtime=new PlayableRuntime(PlayableProfile.Default,91,19092026);
            try
            {
                using(var service=new UnityHostRouteService())
                {
                    var initial=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer);
                    Assert.True(runtime.TrySubmit(new PlayableCommand(91,1,"player-1",PlayableCommandKind.Hold,new[]{initial.Id})).Accepted);
                    var wait=System.Diagnostics.Stopwatch.StartNew();
                    while(wait.Elapsed.TotalSeconds<20&&!runtime.AiCheckpoint.Records.Any(r=>r.Message=="Human/unknown HOLD has priority."))
                    {service.Service(runtime,64);yield return null;}
                    Assert.True(runtime.AiCheckpoint.Records.Any(r=>r.Status==PlayableAiDeliveryStatus.Rejected&&r.Message=="Human/unknown HOLD has priority."),"Ordinary owner policy must attempt delivery against the retained human HOLD.");
                    var held=runtime.Latest.Entities.Single(e=>e.Id==initial.Id);Assert.True(held.Held);Assert.False(held.Moving);Assert.AreEqual(initial.Position,held.Position);
                    TestContext.WriteLine("Stop at tick="+runtime.Latest.Tick+" pending routes="+runtime.Latest.Metrics.NavigationPending);
                    Assert.True(runtime.TrySubmit(new PlayableCommand(91,2,"player-1",PlayableCommandKind.Stop,new[]{initial.Id})).Accepted);
                    wait.Restart();while(wait.Elapsed.TotalSeconds<2&&runtime.Latest.Entities.Single(e=>e.Id==initial.Id).Held)
                    {service.Service(runtime,64);yield return null;}
                    Assert.False(runtime.Latest.Entities.Single(e=>e.Id==initial.Id).Held);
                    Assert.True(runtime.ReceiptsAfter(0).Any(r=>r.Sequence==2&&r.Status==PlayableCommandStatus.Applied),"Human Stop must apply through ordinary authority.");
                }
            }
            finally{runtime.RequestStop();}
            var stopped=System.Diagnostics.Stopwatch.StartNew();while(!runtime.IsStopped&&stopped.Elapsed.TotalSeconds<4)yield return null;
            Assert.True(runtime.IsStopped,"Owned runtime must finish before the next fixture");runtime.Dispose();
        }
        [Test] public void RestoredWorldAiUsesNativeRouteService()
        {
            var config=Config(ai:true);var a=new PlayableAuthorityTick(config,71);
            int tank=a.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,"west-owner",PlayableCommandKind.Move,new[]{tank},new NavPoint(20,-8))).Status);
            Assert.False(a.TryAdvance()); // A prepared route barrier is part of the save.
            var saved=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(saved,config);
            Assert.True(saved.SequenceEqual(b.CaptureBytes()));Assert.True(b.AwaitingRoutes);
            using(var left=new UnityHostRouteService())using(var right=new UnityHostRouteService())
            {
                for(int i=0;i<180;i++)
                {
                    Advance(a,left,config.Profile);Advance(b,right,config.Profile);
                    Assert.True(a.CaptureBytes().SequenceEqual(b.CaptureBytes()),"restored Unity NavMesh + AI tick "+i);
                }
            }
            Assert.True(b.Latest.Entities.Single(e=>e.Id==tank).Position.X>-20);
            Assert.Greater(b.Tick,100);Assert.AreEqual(PlayableAuthorityTick.RouteDeliveryPolicy,"native-fixed-before-step-v1");
        }

        [Test] public void ServiceRejectsForeignThread()
        {
            using(var service=new UnityHostRouteService())
            {
                Exception error=null;var config=Config();var cycle=new PlayableAuthorityTick(config,71);
                var thread=new Thread(()=>{try{service.Service(cycle,1);}catch(Exception ex){error=ex;}});thread.Start();thread.Join();
                Assert.IsInstanceOf<InvalidOperationException>(error);
            }
        }
    }
}

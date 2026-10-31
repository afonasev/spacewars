using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdMotionThreadedWitnessTests
    {
        [Test,Timeout(65000)] public void OrdinaryCommandMovesGroupThroughRealHostAndCapturesAfterJoinedSelection()
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var basis=((AiTestMap)profile.AuthoredMap).Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var scenario=Enumerable.Range(0,8).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-38+i%4*2.5,-38+i/4*2.5))).ToArray();
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,scenario:scenario,terrain:profile.AuthoredMap.Id);
            var runtime=new PlayableRuntime(config,71,startPaused:true);var host=new UnityHostRouteService();
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            NavMotionExecutor executor=null;var clock=Stopwatch.StartNew();bool applied=false;
            void Service(){host.Service(runtime,profile.NavigationRequestsPerFrame);foreach(var receipt in runtime.DrainReceipts())if(receipt.Sequence==1){Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status);applied=true;}Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(host.CertifiedPublication?.Failure);}
            void Wait(Func<bool> condition,string phase){while(!condition()&&clock.Elapsed.TotalSeconds<50){Service();Thread.Sleep(1);}Assert.True(condition(),phase);}
            try{
                Wait(()=>runtime.IsPrepared&&runtime.Latest.Paused,"prepared paused ACK");
                executor=(NavMotionExecutor)typeof(PlayableRuntime).GetField("motionExecutor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);Assert.NotNull(executor);
                var initial=runtime.ParticipantView("player-1");var tanks=initial.Entities.Where(e=>e.Owner==initial.Owner&&e.Kind==PlayableEntityKind.Tank).ToArray();Assert.AreEqual(8,tanks.Length);
                var goal=new NavPoint(-20,-35);runtime.RequestPause(false);
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,1,"player-1",PlayableCommandKind.Move,tanks.Select(e=>e.Id).ToArray(),goal)).Accepted);
                bool Arrived(){var view=runtime.ParticipantView("player-1");return tanks.All(t=>view.Entities.Any(e=>e.Id==t.Id&&Math.Pow(e.Position.X-goal.X,2)+Math.Pow(e.Position.Z-goal.Z,2)<36));}
                Wait(()=>applied&&executor.CompletedBatches>0&&Arrived(),"ordinary group arrival after real parallel movement");
                Assert.Greater(host.CertifiedPublication.Pumps,0);Assert.Zero(runtime.Latest.Metrics.Errors);
                runtime.RequestPause(true);Wait(()=>runtime.Latest.Paused,"pause boundary");var capture=runtime.RequestCaptureBytes();Wait(()=>capture.IsCompleted,"current-format capture");
                var bytes=capture.GetAwaiter().GetResult();var restored=PlayableAuthorityTick.RestoreBytes(bytes,config);
                try{
                    // Authority restore owns the world/AI/navigation sections;
                    // the outer Runtime separately owns its paused receipt queue.
                    // Recompose that untouched transport section for exact envelope equality.
                    using(var saved=PlayableWorldState.Decode(bytes))using(var roundTrip=PlayableWorldState.Decode(restored.CaptureBytes())){
                        roundTrip.RuntimeTransport=saved.RuntimeTransport;CollectionAssert.AreEqual(bytes,roundTrip.Encode());
                    }
                }finally{restored.Stop();restored.Domain.DestroyOwnedNavigation();}
                Assert.True(executor.IsIdle);TestContext.WriteLine("ORDINARY_MOTION_EXECUTOR seed=7 actors=10 moved=8 tick="+runtime.Latest.Tick+" batches="+executor.CompletedBatches+" pumps="+host.CertifiedPublication.Pumps+" seconds="+clock.Elapsed.TotalSeconds);
            }finally{
                runtime.RequestStop();host.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,10000));Assert.True(executor==null||executor.IsIdle);domain.DestroyOwnedNavigation();Assert.Zero(domain.Navigation.PhysicalHostBudget.PhysicalBytes);
            }
        }
    }
}

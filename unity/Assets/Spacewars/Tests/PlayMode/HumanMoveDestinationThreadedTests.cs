using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class HumanMoveDestinationThreadedTests
    {
        [Test,Timeout(65000)] public void UnsupportedHumanDestinationProjectsAndArrivesThroughOrdinaryHost()
        {
            var p=PlayableProfile.Default;var d=new PlayableDomain(p,81,false);CertifiedRouteWorker worker=null;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var requested=new NavPoint(0,(p.PassageHalfWidth+p.CentralObstacleHalfDepth)/2);
            var start=new NavPoint(-p.CentralObstacleHalfWidth-4,requested.Z);
            try{
                Assert.True(d.Geometry.IsFree(start,p.TankCollisionRadius));Assert.False(d.Geometry.IsFree(requested,p.TankCollisionRadius));
                int mover=(int)typeof(PlayableDomain).GetMethod("SpawnUnit",flags).Invoke(d,new object[]{start,PlayableOwner.Player,PlayableEntityKind.Tank});
                var view=d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
                var projected=HumanMoveDestination.Resolve(p,view,new[]{mover},requested,d.Geometry).Single();
                Assert.AreNotEqual(requested,projected.Target);Assert.True(d.Geometry.IsFree(projected.Target,p.TankCollisionRadius));
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(81,1,"player-1",PlayableCommandKind.Move,projected.Entities,projected.Target)).Status);
                bool motion=false,arrived=false;var clock=Stopwatch.StartNew();
                while(d.Tick<600&&clock.Elapsed.TotalSeconds<55){
                    if(!authority.TryAdvance()){Thread.Sleep(1);continue;}Assert.IsNull(worker.Publication.Failure);
                    var actor=d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7).Entities.Single(e=>e.Id==mover);
                    motion|=!actor.Position.Equals(start);arrived=actor.NavigationOutcome==NavigationOutcome.Arrived;if(arrived)break;Thread.Sleep(1);
                }
                TestContext.WriteLine($"HUMAN_TARGET seed=7 tick={d.Tick} requested={requested.X:R},{requested.Z:R} adjusted={projected.Target.X:R},{projected.Target.Z:R} motion={motion} arrived={arrived} pumps={worker.Publication.Pumps}");
                Assert.True(motion&&arrived,"The actual host must execute the adjusted human movement destination");Assert.Greater(worker.Publication.Pumps,0);
                var proof=d.TacticalQueueProjection(mover,PlayableOwner.Player).Completion;
                Assert.NotNull(proof);Assert.AreEqual(projected.Target,proof.OriginalLocation.Value.Position);
            }finally{worker?.Dispose();if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(d.Navigation.PhysicalHostBudget.HeldBytes);}
        }
    }
}

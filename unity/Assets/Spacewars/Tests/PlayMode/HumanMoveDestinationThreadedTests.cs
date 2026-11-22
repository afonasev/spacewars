using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class HumanMoveDestinationThreadedTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [Timeout(65000)]
        public void SingleGroundMoveKeepsOrApproachesTheClickedPointInAnOrdinaryMatch(bool clickWater)
        {
            var map=new ThreeCrossingsMap(UnityEngine.JsonUtility.FromJson<ThreeCrossingsProfileData>(
                UnityEngine.Resources.Load<UnityEngine.TextAsset>("ThreeCrossingsProfile").text));
            var defaults=PlayableProfile.Create(UnityEngine.JsonUtility.FromJson<PlayableProfileData>(
                UnityEngine.Resources.Load<UnityEngine.TextAsset>("PlayableProfile").text),map);
            var store=new NativeBalanceStore(defaults,authoring:false,startOnRelease:true);
            var profile=PlayableProfile.Create(store.Active.CopyData(),map);
            var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=7};
            Assert.True(profile.UsesRecastGroundRoutes);
            lobby.InitializeParticipants();
            lobby.Participants[1].Human=true;
            var match=NativeLobbyMatch.Create(profile,lobby);
            var origin=new NavPoint(-24,8);
            var target=clickWater?new NavPoint(0,22):new NavPoint(-16.123,8);
            var parked=new NavPoint(target.X,target.Z+2*profile.TankCollisionRadius+.25);
            Assert.True(map.SupportsFootprint(origin,profile.TankCollisionRadius));
            Assert.AreEqual(!clickWater,map.SupportsFootprint(target,profile.TankCollisionRadius));
            if(!clickWater)Assert.True(map.SupportsSweep(origin,target,profile.TankCollisionRadius));
            var scenario=clickWater
                ?new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,origin)}
                :new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,origin),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,parked)};
            var costs=new double[match.Starts.Count,match.Starts.Count];
            for(int i=0;i<match.Starts.Count;i++)
                for(int j=0;j<match.Starts.Count;j++)
                    costs[i,j]=match.RouteCost(i,j);
            var config=new OfflineMatchConfiguration(profile,match.SourceIdentity,match.MapIdentity,match.RouteProvenance,
                match.Seed,match.Roster.ToArray(),match.Starts.ToArray(),match.Sites.ToArray(),match.Obstacles.ToArray(),
                costs,scenario:scenario,terrain:map.Id);

            const int generation=811;
            using var runtime=new PlayableRuntime(config,generation);
            using var routes=new UnityHostRouteService();
            var clock=Stopwatch.StartNew();
            void Service()
            {
                routes.Service(runtime,profile.NavigationRequestsPerFrame);
                Assert.IsNull(runtime.Latest?.Failure);
                Assert.IsNull(routes.CertifiedPublication?.Failure);
                Thread.Sleep(1);
            }
            try
            {
                while((!runtime.IsPrepared||runtime.Latest.Tick==0)&&clock.Elapsed.TotalSeconds<25)Service();
                Assert.True(runtime.IsPrepared&&runtime.Latest.Tick>0);
                var initial=runtime.ParticipantView("player-1");
                int mover=initial.Entities.Single(e=>e.Kind==PlayableEntityKind.Tank&&e.Position.Equals(origin)).Id;
                var queue=new HumanMoveOrderQueue();
                Assert.True(queue.Enqueue(profile,initial,new PlayableCommand(generation,1,"player-1",PlayableCommandKind.Move,new[]{mover},target)));
                PlayableCommand intent=null;
                HumanMoveDestination.Group[] destinations=null;
                Assert.True(SpinWait.SpinUntil(()=>queue.TryTake(out intent,out destinations),10000));
                var destination=destinations.Single();
                if(clickWater)
                {
                    Assert.Less(destination.Target.X,0);
                    Assert.True(map.SupportsFootprint(destination.Target,profile.TankCollisionRadius));
                    Assert.Less(Distance(target,destination.Target),map.RiverHalfWidth+profile.TankCollisionRadius+profile.Navigation.GridCell*3);
                }
                else Assert.AreEqual(target,destination.Target);
                Assert.True(runtime.TrySubmit(new PlayableCommand(generation,intent.Sequence,intent.PlayerId,intent.Kind,
                    destination.Entities,destination.Target)).Accepted);

                PlayableCommandReceipt receipt=null;
                while(receipt==null&&clock.Elapsed.TotalSeconds<32)
                {
                    Service();
                    receipt=runtime.DrainReceipts().SingleOrDefault(r=>r.Sequence==1);
                }
                Assert.NotNull(receipt);
                TestContext.WriteLine($"profile={store.ActiveIdentity} map={match.MapIdentity} seed={match.Seed} result={receipt.Status}");
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                double travel=0;
                var previous=origin;
                while(clock.Elapsed.TotalSeconds<55)
                {
                    Service();
                    var actor=runtime.ParticipantView("player-1").Entities.Single(e=>e.Id==mover);
                    travel+=Distance(previous,actor.Position);
                    previous=actor.Position;
                    if(actor.Completion?.Order.Sequence!=1)continue;
                    TestContext.WriteLine($"travel={travel:R} direct={Distance(origin,target):R} reached={actor.Position.X:R},{actor.Position.Z:R}");
                    Assert.Greater(travel,0);
                    Assert.AreEqual(destination.Target,actor.Completion.OriginalLocation.Position);
                    Assert.AreEqual(destination.Target,actor.Completion.AssignedLocation.Position);
                    Assert.LessOrEqual(Distance(destination.Target,actor.Position),profile.Navigation.ArrivalTolerance+1e-7);
                    if(!clickWater)Assert.LessOrEqual(travel,Distance(origin,target)+profile.Navigation.ArrivalTolerance);
                    return;
                }
                Assert.Fail("The ordinary match did not complete the exact unit move.");
            }
            finally
            {
                runtime.RequestStop();
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
            }
        }

        private static double Distance(NavPoint a,NavPoint b)
        {
            double x=a.X-b.X,z=a.Z-b.Z;
            return Math.Sqrt(x*x+z*z);
        }

        [Test,Timeout(65000)] public void UnsupportedAttackMoveDestinationProjectsAndArrivesThroughOrdinaryHost()
        {
            var p=PlayableProfile.Default;var d=new PlayableDomain(p,81,false);CertifiedRouteWorker worker=null;
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var requested=new NavPoint(0,(p.PassageHalfWidth+p.CentralObstacleHalfDepth)/2);
            var start=new NavPoint(-p.CentralObstacleHalfWidth-4,requested.Z);
            try{
                Assert.True(d.Geometry.IsFree(start,p.TankCollisionRadius));Assert.False(d.Geometry.IsFree(requested,p.TankCollisionRadius));
                int mover=(int)typeof(PlayableDomain).GetMethod("SpawnUnit",flags).Invoke(d,new object[]{start,PlayableOwner.Player,PlayableEntityKind.Tank});
                var view=d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
                var queue=new HumanMoveOrderQueue();Assert.True(queue.Enqueue(p,view,new PlayableCommand(81,0,"player-1",PlayableCommandKind.AttackMove,new[]{mover},requested)));
                HumanMoveDestination.Group[] destinations=null;
                Assert.True(SpinWait.SpinUntil(()=>queue.TryTake(out _,out destinations),10000));var projected=destinations.Single();
                Assert.AreNotEqual(requested,projected.Target);Assert.True(d.Geometry.IsFree(projected.Target,p.TankCollisionRadius));
                var authority=new PlayableAuthorityTick(d,new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()),7);
                worker=new CertifiedRouteWorker(authority.NavigationBinding,p.NavigationRequestsPerFrame);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(81,1,"player-1",PlayableCommandKind.AttackMove,projected.Entities,projected.Target)).Status);
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

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationTrafficScratchAdmissionTests
    {
        private static void Empty(NavigationRetentionBudget budget)
        {
            Assert.Zero(budget.PhysicalBytes);
            Assert.Zero(budget.HeldBytes);
            Assert.Zero(budget.ActiveScopes);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ScratchRefusalLeavesScopeIntactAndReturnsNoLease(bool physicalLimit)
        {
            const long scratch=1024;
            long charge=NavStorageScratch.LeaseBytes+scratch;
            long committed=physicalLimit?LayeredNavigationScheduler.MaxRetainedBytes-NavigationRetentionBudget.RootBytes-NavigationRetentionBudget.ScopeBytes-charge+1:0;
            var budget=new NavigationRetentionBudget(committed);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,0,out var scope));
            using(scope){
                long before=budget.RetainedBytes,held=budget.HeldBytes;
                int refused=0;
                if(!physicalLimit)budget.AllocationAdmission=bytes=>{Assert.AreEqual(charge,bytes);refused++;return false;};
                try{
                    Assert.False(NavStorageScratch.TryReserve(scope.OwnedReservation(),scratch,out var lease));
                    Assert.Null(lease);
                    Assert.AreEqual(before,budget.RetainedBytes);
                    Assert.AreEqual(held,budget.HeldBytes);
                    Assert.AreEqual(1,budget.ActiveScopes);
                    if(!physicalLimit)Assert.AreEqual(1,refused);
                    Assert.LessOrEqual(budget.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                }finally{budget.AllocationAdmission=null;}
                // Existing callers keep their throwing contract at the same cap.
                if(physicalLimit)Assert.Throws<InvalidOperationException>(()=>NavStorageScratch.Reserve(scope.OwnedReservation(),scratch));
            }
            Empty(budget);
        }

        [Test]
        public void GrantedScratchPaysForLeaseAndPayloadUntilDisposed()
        {
            var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,0,out var scope));
            using(scope){
                long before=budget.HeldBytes;
                Assert.True(NavStorageScratch.TryReserve(scope.OwnedReservation(),1024,out var lease));
                Assert.NotNull(lease);
                Assert.AreEqual(before+NavStorageScratch.LeaseBytes+1024,budget.HeldBytes);
                lease.Dispose();
                Assert.AreEqual(before,budget.HeldBytes);
                lease.Dispose();
                Assert.AreEqual(before,budget.HeldBytes);
            }
            Empty(budget);
        }

        [Test]
        public void InvalidScratchBoundsRemainErrorsBeforeAdmission()
        {
            var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,0,out var scope));
            using(scope){
                long before=budget.HeldBytes;int calls=0;
                budget.AllocationAdmission=_=>{calls++;return false;};
                try{
                    Assert.Throws<ArgumentOutOfRangeException>(()=>NavStorageScratch.TryReserve(scope.OwnedReservation(),-1,out _));
                    Assert.Throws<OverflowException>(()=>NavStorageScratch.TryReserve(scope.OwnedReservation(),long.MaxValue,out _));
                    Assert.Zero(calls);
                    Assert.AreEqual(before,budget.HeldBytes);
                }finally{budget.AllocationAdmission=null;}
            }
            Empty(budget);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TrafficProbeScratchRefusalDefersWithoutPublicationAndReleasesPins(bool ready)
        {
            var config=CrowdPassageTests.Configuration();
            var world=new OfflineParticipantAuthority(config,71);
            var nav=world.Navigation;var budget=nav.PhysicalHostBudget;
            var host=new UnityHostRouteService();
            try{
                var movers=nav.Crowd.Units.Where(u=>u.Position.X==-18).Select(u=>u.Id).ToArray();
                Assert.AreEqual(2,movers.Length);
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.Move,movers,new NavPoint(18,0)).AsHuman()).Status);
                for(int i=0;i<CrowdPassageTests.RoutePreparationBound;i++){
                    host.Service(new PlayableRouteBinding(world.Domain.Geometry,config.Profile,nav.Admission,nav.RoutePort),CrowdPassageTests.RouteBudget);
                    if(ready){nav.PrepareDeliveryBarrier();if(nav.Answers.Count>0)break;}
                    else{nav.ApplyResults();if(nav.PendingCount==0)break;}
                }
                if(ready)Assert.Greater(nav.Answers.Count,0,"actual Ready corridor before probe");
                else{
                    Assert.Zero(nav.PendingCount);
                    foreach(int id in movers){
                        Assert.NotNull(nav.InstalledExecutionFor(id));
                        var actor=nav.Crowd.Units.Single(u=>u.Id==id);
                        Assert.True(actor.Moving&&!actor.Held&&!actor.StorageOwner.InstalledDetached,"natural installed probe eligibility");
                    }
                }
                var traffic=typeof(NavigationSession).GetField("trafficAuthority",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert.Null(traffic.GetValue(nav),"probe must inspect corridors rather than bypass through existing Traffic");
                var prefix=world.CaptureBytes();long physical=budget.PhysicalBytes;
                int batches=nav.OwnedResultBatchPublications,movement=nav.OwnedMovementPublications,denied=0;
                long request=0,retained=0;
                budget.AllocationAdmission=bytes=>{
                    var frames=new StackTrace().GetFrames();
                    if(frames.Any(f=>f.GetMethod().DeclaringType==typeof(NavStorageScratch)&&f.GetMethod().Name=="TryReserve")&&frames.Any(f=>f.GetMethod().Name=="RequiresTrafficFrame")){
                        denied++;request=bytes;retained=budget.RetainedBytes;return false;
                    }
                    return true;
                };
                var dispatcher=typeof(NavigationSession).GetMethod("TryDispatchPreparedTraffic",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert.NotNull(dispatcher);
                try{
                    if(ready)Assert.DoesNotThrow(()=>nav.Step(1d/30));
                    else Assert.AreEqual(false,dispatcher.Invoke(nav,new object[]{1d/30}),"actual installed dispatcher defers refused scratch");
                }
                finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied,"reach the actual traffic scratch admission");
                Assert.Greater(request,NavStorageScratch.LeaseBytes);
                Assert.AreEqual(3,nav.LastTrafficDispatchStage,"nullable refusal takes the existing defer path");
                Assert.AreEqual(batches,nav.OwnedResultBatchPublications);
                Assert.AreEqual(movement,nav.OwnedMovementPublications);
                Assert.AreEqual(physical,budget.PhysicalBytes);
                Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                Assert.Null(traffic.GetValue(nav));
                CollectionAssert.AreEqual(prefix,world.CaptureBytes(),"refusal changes no world, route or FIFO bytes");
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(nav),budget.PhysicalBytes);
                if(!ready){
                    Assert.DoesNotThrow(()=>dispatcher.Invoke(nav,new object[]{1d/30}));
                    Assert.AreNotEqual(3,nav.LastTrafficDispatchStage,"admitted installed dispatcher continues normal typed checks");
                }
                nav.Step(1d/30);
                Assert.AreNotEqual(3,nav.LastTrafficDispatchStage,"admitted probe continues normal typed checks");
                Assert.Greater(nav.OwnedMovementPublications,movement);
                Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                TestContext.Out.WriteLine($"TRAFFIC_SCRATCH ready={ready} deniedRequest={request} retainedAtRefusal={retained} cap={LayeredNavigationScheduler.MaxRetainedBytes} grantedPeak={budget.PeakRetainedBytes}");
            }finally{
                budget.AllocationAdmission=null;
                host.Dispose();nav.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();
                Empty(budget);
            }
        }
    }
}

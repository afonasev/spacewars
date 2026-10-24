using System;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static LayeredNavigationScheduler DemandAttemptStep(NavigationRetentionBudget budget,LayeredNavigationScheduler source,long epoch,NavEpochQuota quota,int steps=int.MaxValue)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,epoch+100,out var scope));
            LayeredNavigationScheduler next=null;
            try{using(scope){next=source.ForkOwned(scope.OwnedReservation());next.Advance(epoch,quota,_=>true,steps);next.SealPrepared();scope.Attach(next);scope.Commit(next,_=>{});}source.ReleaseOwner();return next;}
            catch{next?.ReleaseOwner();throw;}
        }
        private static LayeredNavigationScheduler TakeDemandReady(NavigationRetentionBudget budget,LayeredNavigationScheduler source,out double cost)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));
            var next=source.ForkOwned(scope.OwnedReservation());
            using(scope){Assert.True(next.TryTake(_=>true,out var completion));try{Assert.AreEqual(NavSolveStatus.Ready,completion.Status);cost=completion.Route.Cost;}finally{completion.ReleaseOwner();}next.SealPrepared();scope.Attach(next);scope.Commit(next,_=>{});}source.ReleaseOwner();return next;
        }

        [TestCase(false)][TestCase(true)]
        public void DemandAttemptSharedBirthSelectsActualLaneAndColdNextPreservesQuotas(bool v3)
        {
            var budget=new NavigationRetentionBudget();var cold=new NavigationRetentionBudget();
            LayeredNavigationScheduler scheduler=null,restored=null;NavScheduledRequest first=null;
            try{
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var birth));
                using(birth){
                    var r=birth.OwnedReservation();var raw=v3?V3Request():V2Request();
                    raw=new NavScheduledRequest(raw.Provider,raw.Clearance,raw.Profile,raw.Limits,raw.Region,raw.Subscription,raw.MaxLabels,query:true,groupQuery:true,demandQuery:true,demandQueryV2:true,demandQueryV3:v3);
                    var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,r);first=NavScheduledRequest.CopyOwned(raw,provider,r);provider.StorageRoot.ReleaseOwner();
                    scheduler=LayeredNavigationScheduler.CreateOwned(r);scheduler.SessionOwnsProviders=true;scheduler.BindAttemptArtifacts(new string('c',64));Assert.True(scheduler.Submit(first));
                    scheduler.SealPrepared();birth.Attach(scheduler);birth.Commit(scheduler,_=>{});
                }
                long epoch=0;
                while(scheduler.PendingCount>0){Assert.Less(epoch,1000);scheduler=DemandAttemptStep(budget,scheduler,epoch++,new NavEpochQuota(256,256,256,256,256));}
                Assert.Greater(scheduler.Counters.PhysicalGraphWork,0,"first request must take the real detour Demand path");
                scheduler=TakeDemandReady(budget,scheduler,out double firstCost);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var submit));
                using(submit){
                    var next=scheduler.ForkOwned(submit.OwnedReservation());var q=first.Subscription;
                    var secondSub=new NavRouteSubscription(q.WorldGeneration,q.GroupId,q.RootOrderRevision,q.CommandSequence,q.IssuedTick,q.ActivationRevision,2,q.Incarnation,q.MemberOrderRevision,q.MobilityRevision,q.RequestSequence+1,q.Origin,q.AssignedEndpoint,q.HoldBinding,q.Provenance);
                    var raw=new NavScheduledRequest(first.Provider,first.Clearance,first.Profile,first.Limits,first.Region,secondSub,first.MaxLabels,query:true,groupQuery:true,demandQuery:true,demandQueryV2:true,demandQueryV3:v3);
                    var second=NavScheduledRequest.CopyOwned(raw,first.Provider,submit.OwnedReservation());try{Assert.True(next.Submit(second));}finally{second.StorageRoot.ReleaseOwner();}
                    next.SealPrepared();submit.Attach(next);submit.Commit(next,_=>{});scheduler.ReleaseOwner();scheduler=next;
                }
                // Pay the second request's direct probe; it must need a detour.
                scheduler=DemandAttemptStep(budget,scheduler,epoch++,new NavEpochQuota(256,256,256,256,256),1);
                Assert.AreEqual(LayeredNavigationScheduler.JobPhase.Graph,CacheHead(scheduler).Phase);Assert.Null(CacheHead(scheduler).Demand);
                var bytes=CaptureGraph(budget,scheduler);
                long graph=scheduler.Counters.PhysicalGraphWork,connectors=scheduler.Counters.ConnectorWork,hits=scheduler.Counters.CacheHits;
                // Birth has Graph credit, but the shared complete catalog has
                // no legal Graph step and there is no Connector credit yet.
                scheduler=DemandAttemptStep(budget,scheduler,epoch,new NavEpochQuota(256,0,0,0,0));
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));
                using(decode){restored=(LayeredNavigationScheduler)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}
                CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                restored=DemandAttemptStep(cold,restored,epoch,new NavEpochQuota(256,0,0,0,0));
                Assert.AreEqual(hits+1,scheduler.Counters.CacheHits);Assert.AreEqual(graph,scheduler.Counters.PhysicalGraphWork);Assert.AreEqual(connectors,scheduler.Counters.ConnectorWork);
                Assert.False(CacheHead(scheduler).Demand.CanGraph);Assert.True(CacheHead(scheduler).Demand.CanConnector);
                Assert.AreSame(typeof(LayeredNavigationScheduler).GetField("demandCatalog",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scheduler),typeof(NavDemandQueryWork).GetField("catalog",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(CacheHead(scheduler).Demand));
                bytes=CaptureGraph(budget,scheduler);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                // Same epoch cannot refill the exhausted Connector lane.
                scheduler=DemandAttemptStep(budget,scheduler,epoch,new NavEpochQuota(256,256,256,256,256));
                restored=DemandAttemptStep(cold,restored,epoch++,new NavEpochQuota(256,256,256,256,256));
                CollectionAssert.AreEqual(bytes,CaptureGraph(budget,scheduler));CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                // The next epoch's one paid Connector step is exact hot/cold.
                scheduler=DemandAttemptStep(budget,scheduler,epoch,new NavEpochQuota(0,0,1,0,0),1);
                restored=DemandAttemptStep(cold,restored,epoch++,new NavEpochQuota(0,0,1,0,0),1);
                Assert.AreEqual(graph,scheduler.Counters.PhysicalGraphWork);Assert.AreEqual(connectors+1,scheduler.Counters.ConnectorWork);
                CollectionAssert.AreEqual(CaptureGraph(budget,scheduler),CaptureGraph(cold,restored));
                while(scheduler.PendingCount>0){Assert.Less(epoch,1000);scheduler=DemandAttemptStep(budget,scheduler,epoch,new NavEpochQuota(256,256,256,256,256));restored=DemandAttemptStep(cold,restored,epoch++,new NavEpochQuota(256,256,256,256,256));CollectionAssert.AreEqual(CaptureGraph(budget,scheduler),CaptureGraph(cold,restored));}
                scheduler=TakeDemandReady(budget,scheduler,out double secondCost);restored=TakeDemandReady(cold,restored,out double coldCost);
                Assert.AreEqual(firstCost,secondCost);Assert.AreEqual(secondCost,coldCost);Assert.Greater(scheduler.Counters.PhysicalGraphWork,graph,"second request also takes a detour");
            }finally{restored?.ReleaseOwner();scheduler?.ReleaseOwner();first?.StorageRoot.ReleaseOwner();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
        }
    }
}

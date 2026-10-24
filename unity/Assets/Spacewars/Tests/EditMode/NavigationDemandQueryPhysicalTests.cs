using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavScheduledRequest DemandRequest(bool demand=true,int labels=4096)
        {
            var source=(NavScheduledRequest)typeof(NavigationPartitionQueryTests).GetMethod("Request",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{labels});
            return new NavScheduledRequest(source.Provider,source.Clearance,source.Profile,source.Limits,source.Region,source.Subscription,source.MaxLabels,query:true,demandQuery:demand);
        }
        private static NavDemandQueryWork DemandSource(out NavigationRetentionBudget budget,string target)
        {
            budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));
            var r=DemandRequest();Assert.True(r.DemandQuery);var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)r.Provider,scope.OwnedReservation());
            var recipe=NavScheduledRequest.CopyOwned(r,p,scope.OwnedReservation());var work=new NavDemandQueryWork(recipe,scope.TryReserve);recipe.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();
            bool At(){string phase=typeof(NavDemandQueryWork).GetField("phase",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(work).ToString();return target=="Catalog"?work.SampleCount>5:target=="Pending"?(bool)typeof(NavDemandQueryWork).GetField("pending",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(work):phase==target;}
            for(int i=0;!At();i++){Assert.Less(i,20000,target);Assert.AreEqual(NavSolveStatus.Pending,work.Status,target);if(work.CanConnector)work.StepConnector();else{Assert.True(work.CanGraph);work.StepGraph();}}
            work.Seal();scope.Attach(work);scope.Commit(work,_=>{});return work;
        }
        [TestCase("Catalog")][TestCase("Seed")][TestCase("Candidates")][TestCase("Pending")][TestCase("Trace")][TestCase("Reconstruct")]
        public void DemandQueryPhysicalColdBytesAndNextCowStepAreExact(string target)
        {
            var hotRoot=DemandSource(out var hot,target);long charge=hot.PhysicalBytes;Assert.AreEqual(Unique(hotRoot),charge);byte[] bytes=CaptureGraph(hot,hotRoot);
            byte[] Next(NavigationRetentionBudget budget,NavDemandQueryWork source,out long physical){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var next=source.Fork(scope.TryReserve);if(next.CanConnector)next.StepConnector();else next.StepGraph();next.Seal();scope.Attach(next);scope.Commit(next,_=>{});byte[] result=CaptureGraph(budget,next);physical=Unique(next);next.ReleaseOwner();return result;}
            var expected=Next(hot,hotRoot,out long nextBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,hotRoot));hotRoot.ReleaseOwner();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,10000,out var decode));var restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
            Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored,out long actualBytes));Assert.AreEqual(nextBytes,actualBytes);restored.ReleaseOwner();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
        }
        [Test] public void DemandQuerySeparateQuotasPipelineAndNeverRefillSameEpoch()
        {
            var r=DemandRequest();var scheduler=new LayeredNavigationScheduler();Assert.True(scheduler.Submit(r));
            try{
                long oldGraph=0,oldConnector=0;bool originOverlap=false;
                for(int epoch=0;scheduler.ReadyCount==0;epoch++){
                    Assert.Less(epoch,20000);var q=new NavEpochQuota(epoch%3==0?0:7,0,epoch%3==1?0:5,7,7);
                    scheduler.Advance(epoch,q,_=>true);var c=scheduler.Counters;
                    Assert.LessOrEqual(c.PhysicalGraphWork-oldGraph,q.Graph);Assert.LessOrEqual(c.ConnectorWork-oldConnector,q.Connectors);Assert.Zero(c.PhysicalFieldWork);
                    Assert.Zero(scheduler.Advance(epoch,new NavEpochQuota(10000,10000,10000,10000,10000),_=>true));
                    originOverlap|=c.PhysicalGraphWork<81&&c.ConnectorWork>6;oldGraph=c.PhysicalGraphWork;oldConnector=c.ConnectorWork;
                }
                Assert.True(originOverlap);Assert.True(scheduler.TryTake(_=>true,out var done));try{Assert.AreEqual(NavSolveStatus.Ready,done.Status);}finally{done.ReleaseOwner();}
            }finally{scheduler.ClearWorld();scheduler.ReleaseOwner();}
        }
        [Test] public void DemandQueryEligibilityLeavesLegacyKeysAndPolicyUntouched()
        {
            var old=DemandRequest(false);var current=DemandRequest();Assert.False(old.DemandQuery);Assert.True(current.DemandQuery);Assert.AreNotEqual(old.CompileKey,current.CompileKey);Assert.AreNotEqual(old.ArtifactKey,current.ArtifactKey);
            var tooSmall=DemandRequest(true,1);var oldSmall=DemandRequest(false,1);Assert.False(tooSmall.DemandQuery);Assert.AreEqual(oldSmall.CompileKey,tooSmall.CompileKey);Assert.AreEqual(oldSmall.ArtifactKey,tooSmall.ArtifactKey);
        }
        [Test] public void DemandQueryEveryNthPendingCowRefusalLeavesCommittedRootExact()
        {
            var root=DemandSource(out var budget,"Pending");byte[] bytes=CaptureGraph(budget,root);long charge=budget.PhysicalBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var counting));var counter=new Refusal(counting,int.MaxValue);var trial=root.Fork(counter.TryReserve);trial.StepConnector();trial.Seal();int total=counter.Calls;trial.ReleaseOwner();counting.Discard();
            for(int nth=1;nth<=total;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var refusal=new Refusal(scope,nth);NavDemandQueryWork next=null;
                Assert.Throws<InvalidOperationException>(()=>{next=root.Fork(refusal.TryReserve);next.StepConnector();next.Seal();},"reservation "+nth);
                Assert.AreEqual(nth,refusal.Calls);next?.ReleaseOwner();scope.Discard();Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,root));
            }
            TestContext.WriteLine("DEMAND pendingCow everyNth="+total+"/"+total);root.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void DemandQueryForeignOwnerIsRejectedBeforeAnyReservation()
        {
            var root=DemandSource(out var hot,"Catalog");byte[] bytes=CaptureGraph(hot,root);long charge=hot.PhysicalBytes;var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var count=new Refusal(scope,int.MaxValue);
            var recipe=(NavScheduledRequest)typeof(NavDemandQueryWork).GetField("request",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(root);
            Assert.Throws<ArgumentException>(()=>new NavDemandQueryWork(recipe,count.TryReserve));Assert.Zero(count.Calls);Assert.Throws<ArgumentException>(()=>root.Fork(count.TryReserve));Assert.Zero(count.Calls);
            scope.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.AreEqual(charge,hot.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,root));root.ReleaseOwner();Assert.Zero(hot.PhysicalBytes);
        }
        [Test] public void DemandQuerySchedulerColdBodyKeepsPolicyQuotaAndExactNextStep()
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var source=DemandRequest();var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)source.Provider,scope.OwnedReservation());var request=NavScheduledRequest.CopyOwned(source,provider,scope.OwnedReservation());var root=LayeredNavigationScheduler.CreateOwned(scope.OwnedReservation());Assert.True(root.Submit(request));request.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();root.Advance(0,new NavEpochQuota(13,0,7,17,17),_=>true,23);root.SealPrepared();scope.Attach(root);scope.Commit(root,_=>{});
            byte[] bytes=CaptureGraph(hot,root);long charge=hot.PhysicalBytes;
            byte[] Next(NavigationRetentionBudget budget,LayeredNavigationScheduler s){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var op));var n=s.ForkOwned(op.OwnedReservation());n.Advance(0,new NavEpochQuota(10000,10000,10000,10000,10000),_=>true,7);n.SealPrepared();op.Attach(n);op.Commit(n,_=>{});byte[] result=CaptureGraph(budget,n);n.ReleaseOwner();return result;}
            byte[] expected=Next(hot,root);root.ReleaseOwner();Assert.Zero(hot.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,10000,out var decode));var restored=(LayeredNavigationScheduler)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored));restored.ReleaseOwner();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
        }
        [TestCase(false)][TestCase(true)] public void DemandQueryOldArrivalPolicyRemainsEagerAfterLogicalRestore(bool group)
        {
            var r=DemandRequest(false);var graph=LayeredNavigationGraphCompiler.Compile(r.Provider,r.Clearance,r.Profile,r.Limits);
            var members=Enumerable.Range(0,group?2:1).Select(i=>NavigationArrivalTestFactory.Member(100+i,0,graph.Nodes[i].Location,.25,9)).ToArray();
            var input=new NavArrivalInput("topology:"+r.Provider.Binding,"profile:"+r.Profile.ExactProfileBinding,r.Provider,r.Profile,new NavigationProfile(),r.Limits,new NavArrivalCandidateSource("old-policy",1,r.Region.Requested,r.Region.Candidates),members,NavigationArrivalTestFactory.Occupancy(),r.MaxLabels,null,true);
            var current=new NavigationArrivalWork(input);NavigationArrivalWork old=null;
            try{var saved=current.Save();saved.Version=group?4:3;saved.DemandQueryMode=false;saved.QueryMode=true;saved.GroupQueryMode=group;old=NavigationArrivalWork.Restore(input,saved);old.Advance(0,10000,new NavEpochQuota(0,0,0,0,0));
                Assert.AreEqual(group?4:3,old.Save().Version);Assert.False(old.Save().DemandQueryMode);Assert.AreEqual(1,old.PendingRouteCount);Assert.True(old.Save().Requests.All(x=>x.Query&&x.GroupQuery==group&&!x.DemandQuery));
                var tag=((INavPhysicalBody)old).PhysicalTag;Assert.AreEqual(new NavPhysicalTag(group?NavPhysicalStorageKind.GroupQueryArrivalWorkV1:NavPhysicalStorageKind.QueryArrivalWorkV1),tag);
            }finally{current.ReleaseOwner();old?.ReleaseOwner();graph.ReleaseOwner();}
        }
        [Test] public void DemandQueryCancelAndReissueRetiresOwnedCatalogWithoutChangingPinnedRoot()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var source=DemandRequest();var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)source.Provider,scope.OwnedReservation());var request=NavScheduledRequest.CopyOwned(source,provider,scope.OwnedReservation());var root=LayeredNavigationScheduler.CreateOwned(scope.OwnedReservation());Assert.True(root.Submit(request));request.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();root.Advance(0,new NavEpochQuota(13,0,7,17,17),_=>true,23);root.SealPrepared();scope.Attach(root);scope.Commit(root,_=>{});
            byte[] before=CaptureGraph(budget,root);long charge=budget.PhysicalBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var operation));var next=root.ForkOwned(operation.OwnedReservation());Assert.True(next.Cancel(request.Subscription.StableKey));Assert.Zero(next.PendingCount);Assert.Zero(next.ReadyCount);Assert.AreEqual(1,next.Counters.Cancelled);
            var sub=request.Subscription;var replacement=new NavRouteSubscription(sub.WorldGeneration,sub.GroupId,sub.RootOrderRevision,sub.CommandSequence,sub.IssuedTick,sub.ActivationRevision,sub.EntityId,sub.Incarnation,sub.MemberOrderRevision,sub.MobilityRevision,sub.RequestSequence+1,sub.Origin,sub.AssignedEndpoint,sub.HoldBinding);
            var raw=new NavScheduledRequest(request.Provider,request.Clearance,request.Profile,request.Limits,request.Region,replacement,request.MaxLabels,query:true,demandQuery:true);var owned=NavScheduledRequest.CopyOwned(raw,request.Provider,operation.OwnedReservation());Assert.True(next.Submit(owned));owned.StorageRoot.ReleaseOwner();next.Advance(1,new NavEpochQuota(13,0,7,17,17),_=>true,23);next.SealPrepared();operation.Attach(next);operation.Commit(next,_=>{});
            CollectionAssert.AreEqual(before,CaptureGraph(budget,root));Assert.AreEqual(1,next.PendingCount);Assert.Greater(budget.PhysicalBytes,charge);next.ReleaseOwner();Assert.AreEqual(charge,budget.PhysicalBytes);root.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase("Pending")][TestCase("Reconstruct")][TestCase("Done")]
        public void DemandQueryLogicalCheckpointSurvivesCompleteSourceRelease(string target)
        {
            var root=DemandSource(out var hot,target);var state=root.Save();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var next=root.Fork(scope.TryReserve);if(next.Status==NavSolveStatus.Pending){if(next.CanConnector)next.StepConnector();else next.StepGraph();}var expected=next.Save();next.ReleaseOwner();scope.Discard();root.ReleaseOwner();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
            var restored=NavDemandQueryWork.Restore(DemandRequest(),state);try{
                CollectionAssert.AreEqual(state.Scalars,restored.Save().Scalars);if(restored.Status==NavSolveStatus.Pending){if(restored.CanConnector)restored.StepConnector();else restored.StepGraph();}var actual=restored.Save();
                CollectionAssert.AreEqual(expected.Scalars,actual.Scalars);CollectionAssert.AreEqual(expected.Starts,actual.Starts);CollectionAssert.AreEqual(expected.Ends,actual.Ends);CollectionAssert.AreEqual(expected.Distance,actual.Distance);CollectionAssert.AreEqual(expected.Frontier,actual.Frontier);Assert.AreEqual(expected.Chain.Length,actual.Chain.Length);Assert.AreEqual(expected.Legs.Length,actual.Legs.Length);
            }finally{restored.ReleaseOwner();}
        }
    }
}

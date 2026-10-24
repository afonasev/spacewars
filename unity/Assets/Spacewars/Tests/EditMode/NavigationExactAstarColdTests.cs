using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavDemandQueryWork AstarOwnedPoint(NavigationRetentionBudget budget,string target,out NavScheduledRequest raw,bool legacy=false)
        {
            raw=legacy?V5Request():V6Request();
            // The coarse 16-unit fixture has only one node on its target surface.
            // Use an 8-unit catalog to observe a genuinely partial shared bucket.
            if(target=="SharedHull")raw=new NavScheduledRequest(raw.Provider,raw.Clearance,new NavGraphProfile(raw.Profile.ExactProfileBinding,raw.Profile.CostId,raw.Profile.CostRevision,8),raw.Limits,raw.Region,raw.Subscription,raw.MaxLabels,query:true,demandQuery:true,demandQueryV2:true,demandQueryV3:true,demandQueryV6:true);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var reservation=scope.OwnedReservation();
            var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,reservation);var request=NavScheduledRequest.CopyOwned(raw,provider,reservation);var w=new NavDemandQueryWork(request,scope.TryReserve);provider.StorageRoot.ReleaseOwner();request.StorageRoot.ReleaseOwner();
            if(target=="SharedHull"){
                for(int n=0;w.Status==NavSolveStatus.Pending;n++){Assert.Less(n,50000);V3Step(w);}Assert.GreaterOrEqual(w.Save().Locations.Count(at=>at.SurfaceId==request.Subscription.AssignedEndpoint.SurfaceId),2,"partial shared hull requires at least two actual target nodes");var catalog=w.ExportCatalog(scope.TryReserve);var sibling=new NavDemandQueryWork(request,scope.TryReserve,shared:catalog);w.ReleaseOwner();catalog.ReleaseOwner();w=sibling;
            }
            for(int n=0;;n++){
                Assert.Less(n,50000);var state=w.Save();int phase=BitConverter.ToInt32(state.Scalars,0),stage=legacy?0:BitConverter.ToInt32(state.Scalars,617);
                bool at=target=="FreshHull"?stage==2&&state.Indexed>0&&state.Indexed<state.Locations.Length:target=="SharedHull"?stage==2&&BitConverter.ToInt32(state.Scalars,629)>=0&&BitConverter.ToInt32(state.Scalars,625)>=0:target=="Pending"?state.Pending.Trace!=null||state.Pending.Cost>0:target=="Reconstruct"?phase==6&&BitConverter.ToInt32(state.Scalars,52)>0:w.Status==NavSolveStatus.Ready;
                if(at)break;Assert.AreEqual(NavSolveStatus.Pending,w.Status,"fixture missed requested actual state "+target);V3Step(w);
            }
            w.Seal();scope.Attach(w);scope.Commit(w,_=>{});return w;
        }
        private static byte[] AstarNext(NavigationRetentionBudget budget,NavDemandQueryWork source,out NavDemandQueryWork.Checkpoint state)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var next=source.Fork(scope.TryReserve);
            try{if(next.Status==NavSolveStatus.Pending)V3Step(next);next.Seal();scope.Attach(next);scope.Commit(next,_=>{});state=next.Save();return CaptureGraph(budget,next);}finally{next.ReleaseOwner();}
        }
        [TestCase("FreshHull")][TestCase("SharedHull")][TestCase("Pending")][TestCase("Reconstruct")][TestCase("Ready")]
        public void ExactAstarOwnedColdNextCowAndLogicalPrefix(string target)
        {
            var hot=new NavigationRetentionBudget();var cold=new NavigationRetentionBudget();var root=AstarOwnedPoint(hot,target,out var raw);NavDemandQueryWork restored=null,logical=null;
            try{
                var saved=root.Save();Assert.AreEqual(671,saved.Scalars.Length);Assert.AreEqual(NavPhysicalValueKind.ExactAstarPolicyV6,((INavPhysicalBody)root).PhysicalTag.Value);var bytes=CaptureGraph(hot,root);long charge=hot.PhysicalBytes;Assert.AreEqual(charge,Unique(root));
                var expected=AstarNext(hot,root,out var nextSaved);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,root));Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,10000,out var decode));restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,AstarNext(cold,restored,out _));
                root.ReleaseOwner();root=null;PrefixNoLeaks(hot); // DTO must survive source lifetime.
                logical=NavDemandQueryWork.Restore(raw,saved);if(logical.Status==NavSolveStatus.Pending)V3Step(logical);var actual=logical.Save();CollectionAssert.AreEqual(nextSaved.Scalars,actual.Scalars);CollectionAssert.AreEqual(nextSaved.ExactDistance,actual.ExactDistance);CollectionAssert.AreEqual(nextSaved.ExactFrontier.Select(x=>new{x.Node,x.Cost}),actual.ExactFrontier.Select(x=>new{x.Node,x.Cost}));
            }finally{logical?.ReleaseOwner();restored?.ReleaseOwner();root?.ReleaseOwner();}PrefixNoLeaks(hot);PrefixNoLeaks(cold);
        }
        [TestCase("SharedHull")][TestCase("Pending")]
        public void ExactAstarEveryNthMutationRefusalKeepsCommittedBytes(string target)
        {
            var b=new NavigationRetentionBudget();var root=AstarOwnedPoint(b,target,out _);var bytes=CaptureGraph(b,root);long charge=b.PhysicalBytes;
            try{
                Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var counting));var count=new Refusal(counting,int.MaxValue);var trial=root.Fork(count.TryReserve);V3Step(trial);trial.Seal();int total=count.Calls;trial.ReleaseOwner();counting.Discard();Assert.Greater(total,0);
                for(int nth=1;nth<=total;nth++){
                    Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var refusal=new Refusal(scope,nth);NavDemandQueryWork next=null;
                    Assert.Throws<InvalidOperationException>(()=>{next=root.Fork(refusal.TryReserve);V3Step(next);next.Seal();});Assert.AreEqual(nth,refusal.Calls);next?.ReleaseOwner();scope.Discard();Assert.AreEqual(charge,b.PhysicalBytes);Assert.Zero(b.HeldBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(b,root));
                }
            }finally{root.ReleaseOwner();}PrefixNoLeaks(b);
        }
        [Test] public void ExactAstarLegacyV5ColdBytesKeepDijkstraPolicy()
        {
            var hot=new NavigationRetentionBudget();var cold=new NavigationRetentionBudget();var root=AstarOwnedPoint(hot,"Pending",out var raw,true);NavDemandQueryWork restored=null;
            try{Assert.False(raw.DemandQueryV6);Assert.AreEqual(NavDistanceMetricContract.Unknown,raw.MetricContract);Assert.AreEqual(617,root.Save().Scalars.Length);Assert.AreEqual(NavPhysicalValueKind.ExactCostPolicyV5,((INavPhysicalBody)root).PhysicalTag.Value);Assert.False(root.HeuristicActive);var bytes=CaptureGraph(hot,root);var expected=AstarNext(hot,root,out _);Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,10000,out var decode));restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,AstarNext(cold,restored,out _));}
            finally{restored?.ReleaseOwner();root.ReleaseOwner();}PrefixNoLeaks(hot);PrefixNoLeaks(cold);
        }
        [Test] public void ExactAstarArrivalCurrentAndLegacyColdCarryRespectPolicyBoundary()
        {
            foreach(int version in new[]{11,12}){
                var hot=new NavigationRetentionBudget();var cold=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null,restored=null,logical=null,mixed=null;
                try{
                    old=PrefixBirth(hot,PrefixInput("old",100),ready:true,version:version);var raw=PrefixInput("append",200);current=PrefixBirth(hot,raw,old,version:version);Assert.NotNull(PrefixState(current).Oracle);Assert.AreEqual(version,current.Save().Version);
                    var expectedTag=version==12?NavPhysicalValueKind.ExactAstarArrivalPolicyV8:NavPhysicalValueKind.ExactArrivalPolicyV7;Assert.AreEqual(expectedTag,((INavPhysicalBody)current).PhysicalTag.Value);var bytes=CaptureGraph(hot,current);var saved=current.Save();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,10000,out var decode));restored=(NavigationArrivalWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));logical=NavigationArrivalWork.Restore(raw,saved);Assert.AreEqual(current.LogicalContinuationBinding,logical.LogicalContinuationBinding);
                    current=PrefixAdvance(hot,current,0,1,PrefixZero);restored=PrefixAdvance(cold,restored,0,1,PrefixZero);logical.Advance(0,1,PrefixZero);CollectionAssert.AreEqual(CaptureGraph(hot,current),CaptureGraph(cold,restored));Assert.AreEqual(current.LogicalContinuationBinding,logical.LogicalContinuationBinding);
                    mixed=PrefixBirth(hot,PrefixInput("append",300),old,version:version==11?12:11);Assert.Null(PrefixState(mixed).Oracle);
                }finally{mixed?.ReleaseOwner();logical?.ReleaseOwner();restored?.ReleaseOwner();current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(hot);PrefixNoLeaks(cold);
            }
        }
    }
}

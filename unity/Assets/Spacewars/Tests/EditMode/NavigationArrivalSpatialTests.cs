using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static readonly BindingFlags SpatialHidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static NavArrivalOccupant[] SpatialRows(int count=400,double specialRadius=.2,double offset=0,bool blocks=true)
        {
            return Enumerable.Range(0,count).Select(i=>NavigationArrivalTestFactory.Actor(1000+i,
                NavigationArrivalTestFactory.At(i==0?-22+offset:160+16*(i%20),i==0?0:160+16*(i/20)),i==0?specialRadius:.1+(i%7)*.09,i!=0||blocks,"spatial:"+i)).ToArray();
        }
        private static void OriginalScan(NavigationArrivalWork work)
        {
            typeof(NavigationArrivalWork).GetMethod("ReleaseSpatial",SpatialHidden).Invoke(work,null);
            typeof(NavigationArrivalWork).GetField("spatialPolicy",SpatialHidden).SetValue(work,false);
            typeof(NavigationArrivalWork).GetField("paidPagePolicy",SpatialHidden).SetValue(work,false);
        }
        [TestCase(.2,3.6,true)][TestCase(.2,3.599999,true)][TestCase(.2,3.600001,true)]
        [TestCase(2,8.56,true)][TestCase(2,8.559999,true)][TestCase(.2,0,false)]
        [TestCase(1000001,0,true)][TestCase(.2,1000001,true)]
        public void PaidSpatialArrivalMatchesOriginalOrderedEndpointsWithVariableRadiiAndAssignments(double radius,double offset,bool blocks)
        {
            using(var fixture=new OwnedArrivalFixture(true,partition:true,occupants:SpatialRows(400,radius,offset,blocks),memberCount:4,flatCandidateCount:32,variableRadii:true)){
                NavigationArrivalWork indexed=null,original=null;
                try{
                    indexed=fixture.NewWork(w=>Assert.AreEqual(NavArrivalStatus.Ready,FinishOwnedArrival(w)));
                    original=fixture.NewWork(w=>{OriginalScan(w);Assert.AreEqual(NavArrivalStatus.Ready,FinishOwnedArrival(w));});
                    Assert.AreEqual(original.Proposals.Count,indexed.Proposals.Count);
                    CollectionAssert.AreEqual(original.Proposals.Select(p=>p.Member.EntityId),indexed.Proposals.Select(p=>p.Member.EntityId));
                    CollectionAssert.AreEqual(original.Proposals.Select(p=>p.Endpoint),indexed.Proposals.Select(p=>p.Endpoint));
                    CollectionAssert.AreEqual(original.MemberOutcomes,indexed.MemberOutcomes);
                    Assert.AreEqual(indexed.AllocationWorkConsumed,indexed.CandidateWorkConsumed+indexed.OccupancyWorkConsumed);
                    if(radius<1&&Math.Abs(offset)<1000){Assert.Less(indexed.OccupancyWorkConsumed,original.OccupancyWorkConsumed);TestContext.WriteLine("SPATIAL exact endpoints; original occupancy="+original.OccupancyWorkConsumed+" indexed="+indexed.OccupancyWorkConsumed);}
                }finally{indexed?.ReleaseOwner();original?.ReleaseOwner();}
            }
        }
        [TestCase(1)][TestCase(258)][TestCase(690)][TestCase(700)]
        public void PaidSpatialPartialBuildQueryAndPendingCaptureRestoresExactContinuation(int operations)
        {
            using(var fixture=new OwnedArrivalFixture(true,partition:true,occupants:SpatialRows(),memberCount:4,flatCandidateCount:32,variableRadii:true)){
                NavigationArrivalWork pinned=null,hot=null,cold=null,logical=null;var coldBudget=new NavigationRetentionBudget();
                try{
                    pinned=fixture.NewWork(w=>{typeof(NavigationArrivalWork).GetField("paidPagePolicy",SpatialHidden).SetValue(w,false);w.Advance(0,operations,new NavEpochQuota(0,0,0,0,0));});
                    Assert.AreEqual(operations,pinned.AllocationWorkConsumed);Assert.Zero(pinned.AllocationQuotaRemaining);
                    byte[] before=CaptureGraph(fixture.Budget,pinned);var saved=pinned.Save();Assert.AreEqual(22,saved.Version);
                    logical=NavigationArrivalWork.Restore(pinned.PhysicalInput,saved);Assert.AreEqual(pinned.LogicalContinuationBinding,logical.LogicalContinuationBinding);
                    Assert.True(coldBudget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));cold=(NavigationArrivalWork)Decode(before,scope.OwnedReservation());scope.Attach(cold);scope.Commit(cold,_=>{});
                    CollectionAssert.AreEqual(before,CaptureGraph(coldBudget,cold));Assert.AreEqual(pinned.LogicalContinuationBinding,cold.LogicalContinuationBinding);
                    Assert.AreNotSame(pinned,cold);TestContext.WriteLine("SPATIAL checkpoint operations="+operations+" phase="+pinned.WorkPhase+" spatial="+string.Join(",",saved.Spatial.Scalars));
                    hot=Fork(pinned,fixture.Budget,20,w=>w.Advance(1,500,new NavEpochQuota(200,200,200,200,200)));
                    Assert.AreSame(pinned.PhysicalInput,hot.PhysicalInput);
                    var next=Fork(cold,coldBudget,21,w=>w.Advance(1,500,new NavEpochQuota(200,200,200,200,200)));cold.ReleaseOwner();cold=next;
                    logical.Advance(1,500,new NavEpochQuota(200,200,200,200,200));
                    Assert.AreEqual(hot.LogicalContinuationBinding,logical.LogicalContinuationBinding);
                    CollectionAssert.AreEqual(CaptureGraph(fixture.Budget,hot),CaptureGraph(coldBudget,cold));
                    CollectionAssert.AreEqual(before,CaptureGraph(fixture.Budget,pinned),"published/pinned aliases must stay immutable");
                    Assert.AreEqual(hot.AllocationWorkConsumed,cold.AllocationWorkConsumed);Assert.AreEqual(hot.AllocationQuotaRemaining,cold.AllocationQuotaRemaining);
                }finally{logical?.ReleaseOwner();hot?.ReleaseOwner();cold?.ReleaseOwner();pinned?.ReleaseOwner();Assert.Zero(coldBudget.PhysicalBytes);Assert.Zero(coldBudget.HeldBytes);Assert.Zero(coldBudget.ActiveScopes);}
            }
        }
        [Test]
        public void PaidSpatialPendingAssignmentAndEveryDecodeRefusalKeepExactState()
        {
            using(var fixture=new OwnedArrivalFixture(true,partition:true,occupants:SpatialRows(8,.2,200),memberCount:4,flatCandidateCount:32)){
                var pinned=fixture.NewWork(w=>{for(int e=0;e<2000&&w.MemberCursor==0;e++)w.Advance(e,1,new NavEpochQuota(100,100,100,100,100));Assert.AreEqual(1,w.MemberCursor);});
                try{
                    var saved=pinned.Save();Assert.Less(saved.Spatial.Scalars[2],8+saved.Assignments.Length,"new provisional assignment is persisted before its paid insertion");
                    var logical=NavigationArrivalWork.Restore(pinned.PhysicalInput,saved);try{Assert.AreEqual(pinned.LogicalContinuationBinding,logical.LogicalContinuationBinding);}finally{logical.ReleaseOwner();}
                    var bytes=CaptureGraph(fixture.Budget,pinned);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,100,out var trial));var counter=new Refusal(trial,int.MaxValue);var copy=Decode(bytes,counter);trial.Attach(copy);int calls=counter.Calls;copy.ReleaseOwner();trial.Discard();Assert.Zero(budget.PhysicalBytes);
                    for(int nth=1;nth<=calls;nth++){
                        Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,100+nth,out var scope));var refusal=new Refusal(scope,nth);
                        try{Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusal));Assert.AreEqual(nth,refusal.Calls);}finally{scope.Discard();}
                        Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                    }
                    TestContext.WriteLine("SPATIAL decode refusal allocation points="+calls);
                }finally{pinned.ReleaseOwner();}
            }
        }
        [Test]
        public void PaidSpatialForkBuildAllocationRefusalLeavesPinnedInputAndBudgetsExact()
        {
            using(var fixture=new OwnedArrivalFixture(true,partition:true,occupants:SpatialRows(8),memberCount:2)){
                var pinned=fixture.NewWork(w=>w.Advance(0,255,new NavEpochQuota(0,0,0,0,0)));
                try{
                    var bytes=CaptureGraph(fixture.Budget,pinned);long baseline=fixture.Budget.PhysicalBytes;
                    Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Solve,1,100,out var trial));var counter=new Refusal(trial,int.MaxValue);
                    var fork=pinned.ForkOwned(counter);fork.Advance(1,64,new NavEpochQuota(0,0,0,0,0));int calls=counter.Calls;fork.ReleaseOwner();trial.Discard();Assert.AreEqual(baseline,fixture.Budget.PhysicalBytes);
                    for(int nth=1;nth<=calls;nth++){
                        Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Solve,1,100+nth,out var scope));var refusal=new Refusal(scope,nth);NavigationArrivalWork failed=null;
                        try{Assert.Throws<InvalidOperationException>(()=>{failed=pinned.ForkOwned(refusal);failed.Advance(1,64,new NavEpochQuota(0,0,0,0,0));});Assert.AreEqual(nth,refusal.Calls);}
                        finally{failed?.ReleaseOwner();scope.Discard();}
                        Assert.AreEqual(baseline,fixture.Budget.PhysicalBytes);Assert.Zero(fixture.Budget.HeldBytes);Assert.Zero(fixture.Budget.ActiveScopes);CollectionAssert.AreEqual(bytes,CaptureGraph(fixture.Budget,pinned));
                    }
                    TestContext.WriteLine("SPATIAL refusal allocation points="+calls);
                }finally{pinned.ReleaseOwner();}
            }
        }
    }
}

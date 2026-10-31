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
        [TestCase(NavArrivalStatus.Pending)][TestCase(NavArrivalStatus.BudgetExhausted)][TestCase(NavArrivalStatus.Ready)]
        public void PhysicalBirthColdFallbackOnlyDeclinesUnreadyPageZeroOracle(NavArrivalStatus priorStatus)
        {
            const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
            var profile=PlayableProfile.ThreeCrossingsDefault;var domain=new PlayableDomain(profile,71,false);var nav=domain.Navigation;
            var kernel=new CertifiedRouteKernel{PhysicalOperationsPerPump=1};NavigationArrivalWork old=null,current=null,decoded=null;
            var coldBudget=new NavigationRetentionBudget();
            NavigationArrivalWork Latest(){var root=nav.PinCommittedHostRoot();try{var work=(NavigationArrivalWork)root.ArrivalWork.OrderBy(p=>p.Key).Last().Value;work.Retain();return work;}finally{root.Release();}}
            try{
                int first=(int)typeof(PlayableDomain).GetMethod("SpawnUnit",hidden).Invoke(domain,new object[]{new NavPoint(-42,-40),PlayableOwner.Player,PlayableEntityKind.Tank});
                int second=(int)typeof(PlayableDomain).GetMethod("SpawnUnit",hidden).Invoke(domain,new object[]{new NavPoint(-42,-44),PlayableOwner.Player,PlayableEntityKind.Tank});
                using(nav.CaptureOwnedState()){}
                var command=new PlayableCommand(71,1,"player-1",PlayableCommandKind.Move,new[]{first,second},new NavPoint(-36,-46)).AsHuman();
                Assert.AreEqual(PlayableCommandStatus.Applied,typeof(PlayableDomain).GetMethod("Apply",hidden).Invoke(domain,new object[]{command,null}));
                Assert.True(nav.PreparePhysicalArrivalAttempt());old=Latest();
                Assert.NotNull(PrefixState(old));Assert.True(PrefixState(old).Page0Occupancy,"authored owned physical input must reach the paid occupancy-carry policy");
                for(int i=0;i<4096&&old.Status!=priorStatus;i++){
                    kernel.Pump(new PlayableRouteBinding(domain.Geometry,profile,nav.Admission,nav.RoutePort),1);
                    var next=Latest();old.Release();old=next;
                }
                Assert.AreEqual(priorStatus,old.Status,"must enter the requested actual owned predecessor state");
                var before=CaptureGraph(nav.PhysicalHostBudget,old);
                var geometry=domain.Geometry;var changed=new NavGeometry(geometry.HalfExtent,geometry.Obstacles.ToArray(),geometry.Revision+1);
                typeof(PlayableDomain).GetProperty("Geometry",hidden).SetValue(domain,changed);nav.ChangeGeometry(changed);
                bool ready=priorStatus==NavArrivalStatus.Ready;
                Assert.AreEqual(ready,(bool)typeof(NavigationSession).GetMethod("HistoricalBirthPending",hidden).Invoke(nav,new object[]{old}),"birth and orphan retention use the same eligibility");
                // Retire only the orphan root; our pin must stay exact and live.
                Assert.AreEqual(!ready,nav.RetirePhysicalArrivalOwner());
                CollectionAssert.AreEqual(before,CaptureGraph(nav.PhysicalHostBudget,old));
                Assert.True(nav.PreparePhysicalArrivalAttempt());current=Latest();Assert.AreNotSame(old,current);
                Assert.AreEqual(ready,PrefixState(current).Oracle!=null);
                if(ready)Assert.AreSame(old.HistoricalSource,PrefixState(current).Oracle);
                else{Assert.Zero(current.AllocationWorkConsumed);Assert.Zero(current.MemberCursor);Assert.IsEmpty(current.Proposals);}
                var bytes=CaptureGraph(nav.PhysicalHostBudget,current);
                Assert.True(coldBudget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));
                using(decode){decoded=(NavigationArrivalWork)Decode(bytes,decode.OwnedReservation());decode.Attach(decoded);decode.Commit(decoded,_=>{});}
                CollectionAssert.AreEqual(bytes,CaptureGraph(coldBudget,decoded));
                Assert.AreEqual(current.LogicalContinuationBinding,decoded.LogicalContinuationBinding);
                var nextHot=Fork(current,nav.PhysicalHostBudget,20,w=>w.Advance(1,256,PrefixQuota));
                var nextCold=Fork(decoded,coldBudget,20,w=>w.Advance(1,256,PrefixQuota));
                try{CollectionAssert.AreEqual(CaptureGraph(nav.PhysicalHostBudget,nextHot),CaptureGraph(coldBudget,nextCold));}
                finally{nextHot.ReleaseOwner();nextCold.ReleaseOwner();}
                CollectionAssert.AreEqual(before,CaptureGraph(nav.PhysicalHostBudget,old),"existing pinned continuation is never detached or rewritten");
            }finally{
                decoded?.ReleaseOwner();current?.Release();old?.Release();kernel.Clear();
                typeof(PlayableDomain).GetMethod("DestroyOwnedNavigation",hidden).Invoke(domain,null);
                Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);PrefixNoLeaks(coldBudget);
            }
        }
        [Test]
        public void ExistingAttachedPendingPageZeroCurrentFormatStillRestoresExactly()
        {
            var hotBudget=new NavigationRetentionBudget();var coldBudget=new NavigationRetentionBudget();
            NavigationArrivalWork old=null,current=null,cold=null,logical=null,hotNext=null,coldNext=null;
            try{
                old=PaidPageBirth(hotBudget,RemovalInput("old",100));Assert.AreEqual(NavArrivalStatus.Pending,old.Status);
                current=PaidPageBirth(hotBudget,RemovalInput("after",200),old);
                Assert.AreSame(old,PrefixState(current).Oracle,"generic attachment of existing partial state is preserved");
                var bytes=CaptureGraph(hotBudget,current);var saved=current.Save();Assert.AreEqual(23,saved.Version);
                logical=NavigationArrivalWork.Restore(current.PhysicalInput,saved);
                Assert.True(coldBudget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));
                using(decode){cold=(NavigationArrivalWork)Decode(bytes,decode.OwnedReservation());decode.Attach(cold);decode.Commit(cold,_=>{});}
                CollectionAssert.AreEqual(bytes,CaptureGraph(coldBudget,cold));
                Assert.AreEqual(NavArrivalStatus.Pending,PrefixState(cold).Oracle.Status);
                // Drop the standalone alias; the attached active owner must keep its oracle live.
                old.ReleaseOwner();old=null;
                hotNext=Fork(current,hotBudget,20,w=>w.Advance(1,256,PrefixQuota));
                coldNext=Fork(cold,coldBudget,20,w=>w.Advance(1,256,PrefixQuota));logical.Advance(1,256,PrefixQuota);
                CollectionAssert.AreEqual(CaptureGraph(hotBudget,hotNext),CaptureGraph(coldBudget,coldNext));
                Assert.AreEqual(hotNext.LogicalContinuationBinding,logical.LogicalContinuationBinding);
                CollectionAssert.AreEqual(bytes,CaptureGraph(hotBudget,current),"pinned attached pending graph remains immutable");
            }finally{hotNext?.ReleaseOwner();coldNext?.ReleaseOwner();logical?.ReleaseOwner();cold?.ReleaseOwner();current?.ReleaseOwner();old?.ReleaseOwner();}
            PrefixNoLeaks(hotBudget);PrefixNoLeaks(coldBudget);
        }
        [TestCase(false)][TestCase(true)]
        public void PhysicalBirthKeepsNonPageZeroPendingEligibility(bool advance)
        {
            using(var fixture=new OwnedArrivalFixture(true,partition:true)){
                NavigationArrivalWork work=null;
                try{
                    work=fixture.NewWork(w=>{
                        typeof(NavigationArrivalWork).GetMethod("SetContinuationPolicy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,new object[]{true,true,true});
                        if(advance)w.Advance(0,1,PrefixZero);
                    });
                    Assert.AreEqual(advance?NavArrivalStatus.BudgetExhausted:NavArrivalStatus.Pending,work.Status);
                    Assert.True(work.IsPhysicalBirthHistoricalCandidate,"legacy/non-page0 birth eligibility is unchanged");
                }finally{work?.ReleaseOwner();}
            }
        }
        private static OwnedArrivalFixture PaidPageFixture()=>new OwnedArrivalFixture(true,partition:true,memberCount:1,flatCandidateCount:192,
            candidateAt:(i,endpoint)=>new NavLocation(new NavPoint(i<64?10000+i:-22+.001*(i-64),i<64?10000:0),endpoint.SurfaceId));
        private static NavigationArrivalWork PaidPageBirth(NavigationRetentionBudget budget,NavArrivalInput raw,NavigationArrivalWork old=null,bool ready=false)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,1,10,out var scope));
            using(scope){
                var input=NavigationArrivalPhysicalInput.CopyInitial(raw,scope.OwnedReservation());
                NavigationArrivalWork work=null;
                try{work=NavigationArrivalWork.CreateOwned(input,scope.OwnedReservation());if(old!=null)work.AttachHistorical(old,scope.OwnedReservation());if(ready)Assert.AreEqual(NavArrivalStatus.Ready,FinishOwnedArrival(work));work.SealPrepared();scope.Attach(work);scope.Commit(work,_=>{});return work;}
                catch{work?.ReleaseOwner();throw;}finally{input.StorageRoot.ReleaseOwner();}
            }
        }
        [TestCase("after",true)][TestCase("reorder",false)][TestCase("blocked",false)]
        public void PaidPagesHistoricalFullSourceAndPageZeroCertificatesRemainRequired(string change,bool reuse)
        {
            var budget=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null;
            try{
                old=PaidPageBirth(budget,RemovalInput("old",100),ready:true);current=PaidPageBirth(budget,RemovalInput(change,200),old);
                Assert.NotNull(PrefixState(current).Oracle);bool sourceGate=false;
                for(int epoch=0;epoch<1000&&current.Status!=NavArrivalStatus.Ready&&PrefixState(current).Oracle!=null;epoch++){
                    current=PrefixAdvance(budget,current,epoch,1,PrefixQuota);sourceGate|=PrefixState(current).Gate==2;
                }
                Assert.True(sourceGate,"historical source gate must still be paid");
                if(reuse){Assert.AreEqual(NavArrivalStatus.Ready,current.Status);Assert.AreEqual(3,current.Proposals.Count);Assert.AreEqual(23,current.Save().Version);}
                else{Assert.Null(PrefixState(current).Oracle);Assert.Zero(current.MemberCursor);Assert.IsEmpty(current.Proposals);}
            }finally{current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(budget);
        }
        [Test]
        public void PaidPagesEmptyLaterPageKeepsOriginalSourceRegionsAndExactEndpoint()
        {
            using(var fixture=PaidPageFixture()){
                NavigationArrivalWork lazy=null,original=null;
                try{
                    lazy=fixture.NewWork(w=>Assert.AreEqual(NavArrivalStatus.Ready,FinishOwnedArrival(w)));
                    original=fixture.NewWork(w=>{OriginalScan(w);Assert.AreEqual(NavArrivalStatus.Ready,FinishOwnedArrival(w));});
                    Assert.AreEqual(192,lazy.PhysicalInput.Source.Candidates.Count);
                    var saved=lazy.Save();Assert.AreEqual(23,saved.Version);Assert.AreEqual(1,saved.PreparedClearances.Length);
                    var pages=saved.PreparedClearances[0];Assert.NotNull(pages.CandidateIndices[0]);Assert.Zero(pages.CandidateIndices[0].Length);Assert.IsNull(pages.Regions[0]);
                    Assert.AreEqual(64,pages.CandidateIndices[1].Length);Assert.NotNull(pages.Regions[1]);Assert.IsNull(pages.CandidateIndices[2]);Assert.IsNull(pages.Regions[2]);
                    Assert.AreEqual(1,lazy.Proposals.Count);CollectionAssert.AreEqual(original.Proposals.Select(p=>p.Endpoint),lazy.Proposals.Select(p=>p.Endpoint));
                    Assert.Less(lazy.CandidateWorkConsumed,original.CandidateWorkConsumed);
                    var bytes=CaptureGraph(fixture.Budget,lazy);TestContext.WriteLine("PAID_PAGE empty0/nonempty1/unprepared2; physical bytes="+bytes.Length+" candidate="+lazy.CandidateWorkConsumed+" original="+original.CandidateWorkConsumed);
                }finally{lazy?.ReleaseOwner();original?.ReleaseOwner();}
            }
        }
        [TestCase(257)][TestCase(319)][TestCase(321)][TestCase(383)]
        public void PaidPagesPartialFirstAndLaterPageCaptureRestoresExactContinuation(int operations)
        {
            using(var fixture=PaidPageFixture()){
                NavigationArrivalWork pinned=null,hot=null,cold=null,logical=null;var coldBudget=new NavigationRetentionBudget();
                try{
                    pinned=fixture.NewWork(w=>w.Advance(0,operations,new NavEpochQuota(0,0,0,0,0)));
                    Assert.AreEqual(operations,pinned.AllocationWorkConsumed);Assert.Zero(pinned.AllocationQuotaRemaining);
                    byte[] before=CaptureGraph(fixture.Budget,pinned);var saved=pinned.Save();Assert.AreEqual(23,saved.Version);Assert.AreEqual(operations<320?0:1,saved.PreparationPageIndex);Assert.AreEqual(operations-256,saved.PreparationCursor);Assert.AreEqual(0,saved.PreparationMember);
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
        public void PaidPagesCompletionAllocationRefusalLeavesPinnedPartialPageAndBudgetsExact()
        {
            using(var fixture=PaidPageFixture()){
                var pinned=fixture.NewWork(w=>w.Advance(0,383,new NavEpochQuota(0,0,0,0,0)));
                try{
                    var bytes=CaptureGraph(fixture.Budget,pinned);long baseline=fixture.Budget.PhysicalBytes;
                    Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Solve,1,100,out var trial));var counter=new Refusal(trial,int.MaxValue);
                    var fork=pinned.ForkOwned(counter);fork.Advance(1,1,new NavEpochQuota(0,0,0,0,0));int calls=counter.Calls;fork.ReleaseOwner();trial.Discard();Assert.AreEqual(baseline,fixture.Budget.PhysicalBytes);
                    for(int nth=1;nth<=calls;nth++){
                        Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Solve,1,100+nth,out var scope));var refusal=new Refusal(scope,nth);NavigationArrivalWork failed=null;
                        try{Assert.Throws<InvalidOperationException>(()=>{failed=pinned.ForkOwned(refusal);failed.Advance(1,1,new NavEpochQuota(0,0,0,0,0));});Assert.AreEqual(nth,refusal.Calls);}
                        finally{failed?.ReleaseOwner();scope.Discard();}
                        Assert.AreEqual(baseline,fixture.Budget.PhysicalBytes);Assert.Zero(fixture.Budget.HeldBytes);Assert.Zero(fixture.Budget.ActiveScopes);CollectionAssert.AreEqual(bytes,CaptureGraph(fixture.Budget,pinned));
                    }
                    TestContext.WriteLine("SPATIAL refusal allocation points="+calls);
                }finally{pinned.ReleaseOwner();}
            }
        }
    }
}

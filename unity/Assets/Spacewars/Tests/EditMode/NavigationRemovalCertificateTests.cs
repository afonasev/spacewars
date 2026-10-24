using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavArrivalInput RemovalInput(string change,long request,bool detour=false)
        {
            var raw=PrefixInput(change=="old"?"old":change=="blocked"?"blocked":"append",request,detour);
            var map=ThreeCrossingsMap.Default;var extra=change=="radius"?new[]{new NavObstacle(-43.80,-52.02,-43.78,-51.98)}:Array.Empty<NavObstacle>();
            var p=(PartitionNavigationProvider)raw.Provider;
            if(extra.Length>0){p=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.Concat(new[]{new NavObstacle(60,60,61,61)}).Concat(extra).ToArray(),map.Revision+2),null);Assert.True(new NavGeometry(map.HalfExtent,extra,1).IsFree(raw.Source.Candidates[0].Position,0));Assert.False(p.IsValid(raw.Source.Candidates[0],raw.Members[0].Clearance));}
            var points=raw.Source.Candidates.ToList();
            if(change=="old")points.Add(new NavLocation(detour?new NavPoint(-48,-52):new NavPoint(48,48),detour?"deck/-1/-1":"deck/1/1"));
            if(change=="before")points.RemoveAt(0);
            if(change=="reorder"){var first=points[0];points[0]=points[1];points[1]=first;}
            if(change=="insert")points[2]=new NavLocation(new NavPoint(-48,-36),"deck/-1/-1");
            return new NavArrivalInput("removal:"+p.Binding,raw.ProfileIdentity,p,raw.Profile,raw.ArrivalProfile,raw.Limits,new NavArrivalCandidateSource("removal",1,raw.Source.Requested,points),raw.Members.Select((m,n)=>new NavArrivalMember(m.EntityId,m.Incarnation,m.Order,n+1,m.Origin,new NavClearanceProfile(m.Clearance.Id,m.Clearance.Revision,m.Clearance.MobilityClass,n+1,m.Radius),m.WorldGeneration,m.GroupId,m.RootOrderRevision,m.CommandSequence,m.IssuedTick,m.ActivationRevision,m.MemberOrderRevision,m.RequestSequence,m.HoldBinding)),raw.Occupancy,raw.MaxLabels,independentMembers:true);
        }
        [Test] public void RemovalAfterSearchedC3KeepsFullCurrentDomainAndFreshThreeProofs()
        {
            var b=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null;
            try{
                old=PrefixBirth(b,RemovalInput("old",100),ready:true,version:10);var source=old;var raw=RemovalInput("after",200);current=PrefixBirth(b,raw,old,version:10);bool retained=false;
                for(int epoch=0;current.Status!=NavArrivalStatus.Ready;epoch++){Assert.Less(epoch,1000);current=PrefixAdvance(b,current,epoch,1,PrefixZero);retained|=ReferenceEquals(PrefixState(current).Oracle,source);if(PrefixState(current).Oracle==null&&current.Status!=NavArrivalStatus.Ready)Assert.Fail("positive certificate dropped");}
                Assert.True(retained);Assert.AreEqual(10,current.Save().Version);Assert.AreEqual(3,current.PhysicalInput.Source.Candidates.Count);Assert.AreEqual(3,current.Proposals.Count);
                foreach(var result in current.Proposals){Assert.True(raw.Source.Candidates.Contains(result.Endpoint));Assert.True(result.Proof.ValidateAgainst(current.PhysicalInput,result.Member,result.Endpoint));Assert.AreEqual(old.Proposals.Single(p=>p.Member.EntityId==result.Member.EntityId).Route.Cost,result.Route.Cost);Assert.AreNotEqual(old.Proposals.Single(p=>p.Member.EntityId==result.Member.EntityId).Proof.StableKey,result.Proof.StableKey);}
            }finally{current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(b);
        }
        [TestCase("before")][TestCase("radius")][TestCase("reorder")][TestCase("insert")][TestCase("blocked")]
        public void RemovalUnprovedC3OrderOrBlockedEndpointDropsWholeGroup(string change)
        {
            var b=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null;
            try{old=PrefixBirth(b,RemovalInput("old",100),ready:true,version:10);current=PrefixBirth(b,RemovalInput(change,200),old,version:10);
                for(int epoch=0;PrefixState(current).Oracle!=null;epoch++){Assert.Less(epoch,1000);current=PrefixAdvance(b,current,epoch,1,PrefixZero);}
                Assert.AreEqual(NavArrivalStatus.BudgetExhausted,current.Status);Assert.Zero(current.MemberCursor);Assert.IsEmpty(current.Save().Assignments);Assert.IsEmpty(current.Proposals);
            }finally{current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(b);
        }
        [Test] public void RemovalEqualSourcePreservesPendingCarryWithoutFuturePreparation()
        {
            var b=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null;
            try{old=PrefixBirth(b,PrefixInput("old",100,true),version:10);old=PrefixAdvance(b,old,0,10000,PrefixZero);Assert.AreEqual(0,old.MemberCursor);Assert.Greater(old.PendingRouteCount,0);
                current=PrefixBirth(b,PrefixInput("append",200,true),old,version:10);current=PrefixAdvance(b,current,0,10000,PrefixZero);Assert.NotNull(PrefixState(current).Oracle);Assert.AreEqual(4,PrefixState(current).Gate);Assert.Zero(PrefixState(current).PreparationMember);Assert.Zero(PrefixState(current).SourceOld);
            }finally{current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(b);
        }
        [Test] public void RemovalRetainsActualPendingPageZeroSearchAndLegacyProofCharge()
        {
            long previousProofLayout=Math.Max(512,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(NavArrivalReachabilityProof))-8+160);
            Assert.AreEqual(previousProofLayout,(long)typeof(NavArrivalReachabilityProof).GetField("LayoutBase",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null),"detached recipe reference must not change legacy owned proof charge");
            var b=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null;
            try{
                old=PrefixBirth(b,RemovalInput("old",100,true),version:10);long epoch=0;
                while(old.MemberCursor<2||old.PendingRouteCount==0){Assert.Less(epoch,1000);old=PrefixAdvance(b,old,epoch++,256,PrefixQuota);Assert.AreNotEqual(NavArrivalStatus.Ready,old.Status,"fixture must stop at an actual unfinished third query");}
                var raw=RemovalInput("after",200,true);current=PrefixBirth(b,raw,old,version:10);long graph=old.RouteCounters.PhysicalGraphWork;
                current=PrefixAdvance(b,current,0,10000,PrefixZero);Assert.NotNull(PrefixState(current).Oracle);Assert.AreEqual(3,PrefixState(current).PreparationMember);Assert.AreEqual(graph,current.RouteCounters.PhysicalGraphWork);Assert.Greater(PrefixState(current).Oracle.PendingRouteCount,0);
                for(long next=1;current.Status!=NavArrivalStatus.Ready;next++){Assert.Less(next,1000);current=PrefixAdvance(b,current,next,256,PrefixQuota);if(PrefixState(current).Oracle==null&&current.Status!=NavArrivalStatus.Ready)Assert.Fail("certified pending page0 unexpectedly dropped");}
                Assert.AreEqual(3,current.Proposals.Count);foreach(var a in current.Proposals)Assert.True(a.Proof.ValidateAgainst(current.PhysicalInput,a.Member,a.Endpoint));
            }finally{current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(b);
        }
        private static NavigationArrivalWork RemovalPoint(NavigationRetentionBudget b,string target,out NavigationArrivalWork old,out NavArrivalInput raw,out long epoch)
        {
            old=PrefixBirth(b,RemovalInput("old",100),ready:true,version:10);raw=RemovalInput("after",200);var current=PrefixBirth(b,raw,old,version:10);epoch=0;
            bool At(){var s=PrefixState(current);return target=="Subsequence"?s.Gate==2&&s.SourceOld==1:target=="Preparation"?s.Gate==4&&s.RemovalStage==1&&s.PreparationMember==0:target=="Preparation2"?s.Gate==4&&s.RemovalStage==1&&s.PreparationMember==1:target=="Region"?s.RemovalStage==2&&s.RegionCursor==1:s.EndpointCursor==1;}
            while(!At()){Assert.Less(epoch,1000);current=PrefixAdvance(b,current,epoch++,1,PrefixZero);Assert.NotNull(PrefixState(current).Oracle);}
            return current;
        }
        [TestCase("Subsequence")][TestCase("Preparation")][TestCase("Preparation2")][TestCase("Region")][TestCase("Endpoint")]
        public void RemovalPaidCursorColdLogicalNextAndNoRefillAreExact(string target)
        {
            var b=new NavigationRetentionBudget();var cold=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null,restored=null,logical=null;
            try{
                current=RemovalPoint(b,target,out old,out var raw,out long epoch);var bytes=CaptureGraph(b,current);var state=current.Save();Assert.AreEqual(10,state.Version);Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));using(decode){restored=(NavigationArrivalWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                logical=NavigationArrivalWork.Restore(raw,state);Assert.AreEqual(current.LogicalContinuationBinding,logical.LogicalContinuationBinding);
                long samePaid=current.AllocationWorkConsumed;int sourceCursor=PrefixState(current).SourceOld,memberCursor=PrefixState(current).PreparationMember,regionCursor=PrefixState(current).RegionCursor,endpointCursor=PrefixState(current).EndpointCursor;
                current=PrefixAdvance(b,current,epoch-1,256,PrefixZero);restored=PrefixAdvance(cold,restored,epoch-1,256,PrefixZero);logical.Advance(epoch-1,256,PrefixZero);
                Assert.AreEqual(samePaid,current.AllocationWorkConsumed);Assert.AreEqual(sourceCursor,PrefixState(current).SourceOld);Assert.AreEqual(memberCursor,PrefixState(current).PreparationMember);Assert.AreEqual(regionCursor,PrefixState(current).RegionCursor);Assert.AreEqual(endpointCursor,PrefixState(current).EndpointCursor);CollectionAssert.AreEqual(CaptureGraph(b,current),CaptureGraph(cold,restored));
                // Advance resets per-call telemetry even on an exhausted epoch.
                // Once normalized, repeated same-epoch calls are byte-exact.
                bytes=CaptureGraph(b,current);current=PrefixAdvance(b,current,epoch-1,256,PrefixZero);restored=PrefixAdvance(cold,restored,epoch-1,256,PrefixZero);CollectionAssert.AreEqual(bytes,CaptureGraph(b,current));CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                long paid=current.AllocationWorkConsumed;current=PrefixAdvance(b,current,epoch,1,PrefixZero);restored=PrefixAdvance(cold,restored,epoch,1,PrefixZero);logical.Advance(epoch,1,PrefixZero);Assert.AreEqual(paid+1,current.AllocationWorkConsumed);CollectionAssert.AreEqual(CaptureGraph(b,current),CaptureGraph(cold,restored));Assert.AreEqual(current.LogicalContinuationBinding,logical.LogicalContinuationBinding);
            }finally{logical?.ReleaseOwner();restored?.ReleaseOwner();current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(b);PrefixNoLeaks(cold);
        }
        [Test] public void RemovalEveryNthPreparationAdmissionRefusalKeepsPinnedRootExact()
        {
            var b=new NavigationRetentionBudget();var root=RemovalPoint(b,"Preparation",out var old,out _,out long epoch);var bytes=CaptureGraph(b,root);long charge=b.PhysicalBytes;
            try{
                Assert.True(b.TryOpenScope(NavigationPreparationKind.Arrival,1,0,out var counting));var count=new Refusal(counting,int.MaxValue);var trial=root.ForkOwned(count);trial.Advance(epoch,1,PrefixZero);trial.SealPrepared();int total=count.Calls;trial.ReleaseOwner();counting.Discard();Assert.Greater(total,0);
                for(int nth=1;nth<=total;nth++){
                    Assert.True(b.TryOpenScope(NavigationPreparationKind.Arrival,1,0,out var scope));var refusal=new Refusal(scope,nth);NavigationArrivalWork next=null;Assert.Throws<InvalidOperationException>(()=>{next=root.ForkOwned(refusal);next.Advance(epoch,1,PrefixZero);next.SealPrepared();});next?.ReleaseOwner();scope.Discard();Assert.AreEqual(charge,b.PhysicalBytes);Assert.Zero(b.HeldBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(b,root));
                }
                TestContext.WriteLine("REMOVAL_REFUSAL reservations="+total);
            }finally{root.ReleaseOwner();old.ReleaseOwner();}PrefixNoLeaks(b);
        }
    }
}

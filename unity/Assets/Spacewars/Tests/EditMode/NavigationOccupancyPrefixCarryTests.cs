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
        private static NavHistoricalArrivalState PrefixState(NavigationArrivalWork w)=>(NavHistoricalArrivalState)typeof(NavigationArrivalWork).GetField("readyOrHistorical",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(w);
        private static LayeredNavigationScheduler PrefixScheduler(NavigationArrivalWork w)=>(LayeredNavigationScheduler)typeof(NavigationArrivalWork).GetField("scheduler",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(w);
        private static NavArrivalInput PrefixInput(string change,long request,bool detour=false)
        {
            var raw=V3Request();var map=ThreeCrossingsMap.Default;
            var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.Concat(change=="old"?Array.Empty<NavObstacle>():new[]{new NavObstacle(60,60,61,61)}).ToArray(),map.Revision+(change=="old"?0:1)),null);
            var endpoints=Enumerable.Range(0,3).Select(n=>new NavLocation(detour?new NavPoint(48,48-4*n):new NavPoint(-44,-52+8*n),detour?"deck/1/1":"deck/-1/-1")).ToArray();
            var members=Enumerable.Range(0,3).Select(n=>new NavArrivalMember(n+1,1,n,1,new NavLocation(detour?new NavPoint(-48,-48+4*n):new NavPoint(-48,-52+8*n),"deck/-1/-1"),raw.Clearance,1,1,1,1,0,1,1,request+n)).ToArray();
            foreach(var m in members)Assert.True(provider.IsValid(m.Origin,m.Clearance),"fixture origin");foreach(var e in endpoints)Assert.True(provider.IsValid(e,raw.Clearance),"fixture endpoint");
            NavArrivalOccupant Row(long id,NavPoint p,bool block)=>new NavArrivalOccupant(NavArrivalOccupantKind.Actor,id,1,new NavLocation(p,"deck/1/1"),.25,block,"prefix:"+id);
            var rows=new System.Collections.Generic.List<NavArrivalOccupant>();
            if(change!="removed")rows.Add(Row(20,new NavPoint(36,change=="changed"?37:36),true));rows.Add(Row(30,new NavPoint(0,0),false));
            if(change!="old"&&change!="removed"){
                rows.Add(Row(change=="reordered"?10:40,change=="blocked"?endpoints[1].Position:new NavPoint(36,52),true));
                rows.Add(Row(50,endpoints[0].Position,false)); // Added nonblocking row must not reject placement.
            }
            var arrival=change=="spacing"?new NavigationProfile(NavigationProfile.DefaultProfileId,1,.72,4,4.5,.8,.15,3.6,.08,arrivalSlotSpacing:4):new NavigationProfile();
            return new NavArrivalInput("prefix:"+provider.Binding,"prefix-profile",provider,detour?new NavGraphProfile(raw.Profile.ExactProfileBinding,raw.Profile.CostId,raw.Profile.CostRevision,8):raw.Profile,arrival,raw.Limits,new NavArrivalCandidateSource("prefix",1,endpoints[0],endpoints),members,new NavArrivalOccupancySnapshot(1,rows),raw.MaxLabels,independentMembers:true);
        }
        private static NavigationArrivalWork PrefixOwned(NavArrivalInput raw,INavStorageReservation r,int version=8)
        {
            var input=NavigationArrivalPhysicalInput.CopyInitial(raw,r);
            try{var w=NavigationArrivalWork.CreateOwned(input,r);PrefixState(w).TerminalCost=version>=20;PrefixState(w).OwnedArrivalAnchor=version>=19;PrefixState(w).OriginShadow=version>=18;PrefixState(w).TerminalCorner=version>=17;PrefixState(w).TerminalRectangle=version>=16;PrefixState(w).RectangleOrigins=version>=15;PrefixState(w).Certified=version>=14;PrefixState(w).Lazy=version>=13;PrefixState(w).Astar=version>=12;PrefixState(w).Exact=version>=11;PrefixState(w).SeamBound=version>=9&&version<11;PrefixState(w).Removal=version>=10;if(version<8)typeof(NavigationArrivalWork).GetMethod("SetContinuationPolicy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,new object[]{true,version==7,false});return w;}
            finally{input.StorageRoot.ReleaseOwner();}
        }
        private static NavigationArrivalWork PrefixBirth(NavigationRetentionBudget budget,NavArrivalInput raw,NavigationArrivalWork old=null,bool ready=false,int version=8)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,1,0,out var scope));
            using(scope){var w=PrefixOwned(raw,scope.OwnedReservation(),version);if(old!=null)w.AttachHistorical(old,scope.OwnedReservation());if(ready)Assert.AreEqual(NavArrivalStatus.Ready,NavigationArrivalTestFactory.Complete(w));w.SealPrepared();scope.Attach(w);scope.Commit(w,_=>{});return w;}
        }
        private static NavigationArrivalWork PrefixAdvance(NavigationRetentionBudget budget,NavigationArrivalWork source,long epoch,int allocation,NavEpochQuota quota)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,1,epoch+1,out var scope));NavigationArrivalWork next=null;
            try{using(scope){next=source.ForkOwned(scope.OwnedReservation());next.Advance(epoch,allocation,quota);next.SealPrepared();scope.Attach(next);scope.Commit(next,_=>{});}source.ReleaseOwner();return next;}catch{next?.ReleaseOwner();throw;}
        }
        private static void PrefixNoLeaks(NavigationRetentionBudget b)
        {Assert.Zero(b.PhysicalBytes);Assert.Zero(b.HeldBytes);Assert.Zero(b.ActiveScopes);Assert.False(b.CaptureActive);}
        private static readonly NavEpochQuota PrefixZero=new NavEpochQuota(0,0,0,0,0),PrefixQuota=new NavEpochQuota(256,256,256,256,256);

        [Test] public void OccupancyPrefixRetainsPendingOracleCatalogAndPublishesCurrentThree()
        {
            var b=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null;
            try{
                old=PrefixBirth(b,PrefixInput("old",100,true));long oldEpoch=0;object catalog;
                do{Assert.Less(oldEpoch,100);old=PrefixAdvance(b,old,oldEpoch++,256,PrefixQuota);catalog=typeof(LayeredNavigationScheduler).GetField("demandCatalog",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(PrefixScheduler(old));}while(catalog==null&&old.Status==NavArrivalStatus.BudgetExhausted);
                Assert.AreEqual(NavArrivalStatus.BudgetExhausted,old.Status);Assert.Greater(old.PendingRouteCount,0);Assert.NotNull(catalog,"first member completed and exported the catalog");
                var demand=CacheHead(PrefixScheduler(old)).Demand;Assert.NotNull(demand);long graph=old.RouteCounters.PhysicalGraphWork;
                var raw=PrefixInput("append",200,true);
                // Establish the positive fixture's full old outcome and each
                // current typed qualification before exercising carry itself.
                Assert.True(b.TryOpenScope(NavigationPreparationKind.Arrival,1,0,out var probeScope));NavigationArrivalWork probe=null;
                try{
                    using(probeScope){probe=old.ForkOwned(probeScope.OwnedReservation());probe.SealPrepared();probeScope.Attach(probe);probeScope.Commit(probe,_=>{});}
                    long probeEpoch=oldEpoch;while(probe.Status!=NavArrivalStatus.Ready){Assert.Less(probeEpoch,100);probe=PrefixAdvance(b,probe,probeEpoch++,256,PrefixQuota);}
                    Assert.AreEqual(3,probe.Proposals.Count,"positive fixture old group must all succeed");
                    foreach(var proposal in probe.Proposals)foreach(var leg in proposal.Route.Legs){
                        var p=(PartitionNavigationProvider)raw.Provider;
                        Assert.True(p.CertifyHistoricalLeg(leg,proposal.Member.Clearance,true),"positive fixture current leg: "+leg.Kind+" "+leg.From.SurfaceId+"("+leg.From.Position.X+","+leg.From.Position.Z+")->"+leg.To.SurfaceId+"("+leg.To.Position.X+","+leg.To.Position.Z+") cost="+leg.Cost);
                    }
                }finally{probe?.ReleaseOwner();}
                current=PrefixBirth(b,raw,old);long epoch=0;
                while(PrefixState(current).Gate<4){Assert.Less(epoch,100);current=PrefixAdvance(b,current,epoch++,256,PrefixZero);Assert.NotNull(PrefixState(current).Oracle);}
                var oracle=PrefixState(current).Oracle;Assert.AreSame(catalog,typeof(LayeredNavigationScheduler).GetField("demandCatalog",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(PrefixScheduler(oracle)));Assert.AreSame(demand,CacheHead(PrefixScheduler(oracle)).Demand);Assert.AreEqual(graph,oracle.RouteCounters.PhysicalGraphWork);
                while(current.Status!=NavArrivalStatus.Ready){Assert.Less(epoch,100);current=PrefixAdvance(b,current,epoch++,256,PrefixQuota);if(current.Status!=NavArrivalStatus.Ready)Assert.NotNull(PrefixState(current).Oracle,"positive certificate must not fall back");}
                Assert.AreEqual(8,current.Save().Version);Assert.AreEqual(3,current.Proposals.Count);Assert.Null(PrefixState(current).Oracle);
                foreach(var p in current.Proposals){Assert.AreEqual(raw.Occupancy.StableKey,p.Proof.OccupancyBinding);Assert.AreEqual(raw.Provider.Binding,p.Proof.ProviderBinding);Assert.AreEqual(200+p.Member.EntityId-1,p.Proof.Subscription.RequestSequence);Assert.True(p.Proof.ValidateAgainst(raw,raw.Members.Single(m=>m.EntityId==p.Member.EntityId),p.Endpoint));}
                Assert.GreaterOrEqual(current.OccupancyWorkConsumed,12,"each member qualifies every current row, including nonblocking additions");
            }finally{current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(b);
        }

        [Test] public void OccupancyPrefixIncidentSurfaceLegKeepsCurrentSweepAndExactCost()
        {
            var input=PrefixInput("old",100);var p=(PartitionNavigationProvider)input.Provider;var cl=input.Members[0].Clearance;
            var from=new NavLocation(new NavPoint(-40,-48),"deck/-1/-1");var to=new NavLocation(new NavPoint(-34.5,-48),"deck/-1/-1");
            var leg=new NavTypedLeg(from,to,5.5,NavTypedLegKind.Surface,null);var wrong=new NavTypedLeg(from,to,5.4,NavTypedLegKind.Surface,null);
            try{
                Assert.True(p.IsValid(from,cl));Assert.True(p.IsValid(to,cl));Assert.False(p.CertifyHistoricalLeg(leg,cl),"legacy whole-sweep endpoint policy remains unchanged");
                Assert.True(p.CertifyHistoricalLeg(leg,cl,true));Assert.False(p.CertifyHistoricalLeg(wrong,cl,true));
                var map=ThreeCrossingsMap.Default;var blocked=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.Concat(new[]{new NavObstacle(-38,-49,-37,-47)}).ToArray(),map.Revision+1),null);
                Assert.True(blocked.IsValid(from,cl));Assert.True(blocked.IsValid(to,cl));Assert.False(blocked.CertifyHistoricalLeg(leg,cl,true),"current interior blocker cannot be bypassed");
                var blockedSeam=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.Concat(new[]{new NavObstacle(-34.6,-48.1,-34.4,-47.9)}).ToArray(),map.Revision+1),null);
                Assert.False(blockedSeam.IsValid(to,cl));Assert.False(blockedSeam.CertifyHistoricalLeg(leg,cl,true),"current blocked incident seam cannot retain the leg");
            }finally{leg.ReleaseOwner();wrong.ReleaseOwner();}
        }

        [TestCase("changed")][TestCase("removed")][TestCase("reordered")][TestCase("spacing")]
        public void OccupancyPrefixRejectsUnprovedDomainChange(string change)
        {
            var b=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null;
            try{old=PrefixBirth(b,PrefixInput("old",100),ready:true);current=PrefixBirth(b,PrefixInput(change,200),old);long epoch=0;while(PrefixState(current).Oracle!=null){Assert.Less(epoch,1000);current=PrefixAdvance(b,current,epoch++,1,PrefixZero);}Assert.AreEqual(NavArrivalStatus.BudgetExhausted,current.Status);Assert.Zero(current.MemberCursor);Assert.IsEmpty(current.Save().Assignments);Assert.IsEmpty(current.Proposals);}
            finally{current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(b);
        }

        [Test] public void OccupancyPrefixBlockedSecondEndpointDropsWholeProvisionalGroup()
        {
            var b=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null;
            try{old=PrefixBirth(b,PrefixInput("old",100),ready:true);current=PrefixBirth(b,PrefixInput("blocked",200),old);long epoch=0;bool sawFirst=false;
                while(PrefixState(current).Oracle!=null){Assert.Less(epoch,1000);current=PrefixAdvance(b,current,epoch++,1,PrefixZero);if(PrefixState(current).Member==1){sawFirst=true;Assert.AreEqual(1,current.Save().Assignments.Length);}}
                Assert.True(sawFirst,"first current assignment existed before second endpoint was rejected");Assert.Zero(current.MemberCursor);Assert.IsEmpty(current.Save().Assignments);Assert.IsEmpty(current.Proposals);Assert.AreEqual(NavArrivalStatus.BudgetExhausted,current.Status);
            }finally{current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(b);
        }

        [Test] public void OccupancyPrefixPaidCursorColdLogicalNoRefillAndCapRollback()
        {
            var b=new NavigationRetentionBudget();var cold=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null,restored=null,logical=null;
            try{old=PrefixBirth(b,PrefixInput("old",100),ready:true);var raw=PrefixInput("append",200);current=PrefixBirth(b,raw,old);long epoch=0;
                while(PrefixState(current).Gate!=4||PrefixState(current).Cursor!=2){Assert.Less(epoch,1000);current=PrefixAdvance(b,current,epoch++,1,PrefixZero);Assert.NotNull(PrefixState(current).Oracle);}
                var bytes=CaptureGraph(b,current);var state=current.Save();Assert.AreEqual(8,state.Version);Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4),((INavPhysicalBody)current).PhysicalTag);
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));using(decode){restored=(NavigationArrivalWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                logical=NavigationArrivalWork.Restore(raw,state);Assert.AreEqual(current.LogicalContinuationBinding,logical.LogicalContinuationBinding);Assert.AreEqual(2,PrefixState(logical).Cursor);
                long occupancy=current.OccupancyWorkConsumed,candidates=current.CandidateWorkConsumed;
                current=PrefixAdvance(b,current,epoch-1,256,PrefixZero);restored=PrefixAdvance(cold,restored,epoch-1,256,PrefixZero);Assert.AreEqual(2,PrefixState(current).Cursor);Assert.AreEqual(occupancy,current.OccupancyWorkConsumed);CollectionAssert.AreEqual(CaptureGraph(b,current),CaptureGraph(cold,restored));
                current=PrefixAdvance(b,current,epoch,1,PrefixZero);restored=PrefixAdvance(cold,restored,epoch,1,PrefixZero);logical.Advance(epoch,1,PrefixZero);Assert.AreEqual(3,PrefixState(current).Cursor);Assert.AreEqual(3,PrefixState(logical).Cursor);Assert.AreEqual(occupancy+1,current.OccupancyWorkConsumed);Assert.AreEqual(candidates,current.CandidateWorkConsumed);CollectionAssert.AreEqual(CaptureGraph(b,current),CaptureGraph(cold,restored));
                Assert.AreEqual(current.LogicalContinuationBinding,logical.LogicalContinuationBinding);
                var immutable=CaptureGraph(b,current);long physical=b.PhysicalBytes;
                Assert.True(b.TryOpenScope(NavigationPreparationKind.Arrival,1,epoch+1,out var refused));NavigationArrivalWork fork=null;
                try{using(refused){fork=current.ForkOwned(refused.OwnedReservation());long padding=LayeredNavigationScheduler.MaxRetainedBytes-b.RetainedBytes-NavStorageScratch.LeaseBytes;using(NavStorageScratch.Reserve(refused.OwnedReservation(),padding)){Assert.AreEqual(LayeredNavigationScheduler.MaxRetainedBytes,b.RetainedBytes);Assert.Throws<InvalidOperationException>(()=>fork.Advance(epoch+1,256,PrefixZero));}}}finally{fork?.ReleaseOwner();}
                Assert.AreEqual(physical,b.PhysicalBytes);Assert.Zero(b.HeldBytes);CollectionAssert.AreEqual(immutable,CaptureGraph(b,current));
            }finally{logical?.ReleaseOwner();restored?.ReleaseOwner();current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(b);PrefixNoLeaks(cold);
        }

        [TestCase(6)][TestCase(7)] public void OccupancyPrefixLegacyVersionsKeepEqualCountPolicyAndBody(int version)
        {
            var b=new NavigationRetentionBudget();NavigationArrivalWork old=null,current=null;
            try{old=PrefixBirth(b,PrefixInput("old",100),ready:true,version:version);current=PrefixBirth(b,PrefixInput("append",200),old,version:version);current=PrefixAdvance(b,current,0,1,PrefixZero);Assert.Null(PrefixState(current).Oracle);Assert.AreEqual(version,current.Save().Version);
                var bytes=CaptureGraph(b,current);var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));NavigationArrivalWork copy=null;
                try{using(decode){copy=(NavigationArrivalWork)Decode(bytes,decode.OwnedReservation());decode.Attach(copy);decode.Commit(copy,_=>{});}CollectionAssert.AreEqual(bytes,CaptureGraph(cold,copy));Assert.AreEqual(version,copy.Save().Version);}finally{copy?.ReleaseOwner();}PrefixNoLeaks(cold);
            }finally{current?.ReleaseOwner();old?.ReleaseOwner();}PrefixNoLeaks(b);
        }
    }
}

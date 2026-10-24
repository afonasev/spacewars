using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavScheduledRequest CertifiedRequest(bool sufficient=true)
        {
            var map=new CampaignOpeningMap();
            var obstacles=sufficient?new[]{new NavObstacle(-240,-240,-80,240),new NavObstacle(110,-200,130,50)}:new[]{new NavObstacle(-170,-170,-150,-150),new NavObstacle(-90,-170,-70,-150),new NavObstacle(110,-200,130,50)};
            var p=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,obstacles,map.Revision),null);
            var c=new NavClearanceProfile("ground",1,"ground",1,.25);
            Assert.True(p.TryLocate(new NavPoint(80,-160),c,out var from));Assert.True(p.TryLocate(new NavPoint(160,160),c,out var to));
            var region=new NavTerminalRegion("certified-target",1,to,new[]{to,new NavLocation(new NavPoint(160.125,160),to.SurfaceId)});
            return new NavScheduledRequest(p,c,new NavGraphProfile(new string('b',64),"distance",1,80),new NavGraphCompileLimits(49,sufficient?28:47,196),region,
                new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,from,to,null),49,query:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true);
        }
        private static NavScheduledRequest CertifiedTraceRequest()
        {
            var r=LazyRequest();var map=ThreeCrossingsMap.Default;double h=map.HalfExtent;
            var p=new PartitionNavigationProvider(map,new NavGeometry(h,((PartitionNavigationProvider)r.Provider).ExclusionObstacles.Concat(new[]{new NavObstacle(-h,h,-h,h)}).ToArray(),map.Revision),null);
            long width=(long)Math.Floor(h/r.Profile.GridCell)-(long)Math.Ceiling(-h/r.Profile.GridCell)+1;int samples=checked((int)(width*width));
            Assert.True(p.IsValid(r.Subscription.Origin,r.Clearance));Assert.True(p.IsValid(r.Subscription.AssignedEndpoint,r.Clearance));
            return new NavScheduledRequest(p,r.Clearance,r.Profile,new NavGraphCompileLimits(samples,samples-1,4*samples),r.Region,r.Subscription,r.MaxLabels,query:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true);
        }
        private static NavOwnedExclusionState Proof(NavDemandQueryWork w)=>((NavLazyPartitionStore)LazyField(w,"lazyStore")).Exclusion;
        [Test] public void CertifiedCapacityActualCampaignPaidBeforeAnySolver()
        {
            var map=new CampaignOpeningMap();var p=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.Solids.ToArray(),map.Revision),null);
            var c=new NavClearanceProfile("ground",1,"ground",1,.25);Assert.True(p.TryLocate(new NavPoint(-61.5089109845236,108.075765746673),c,out var from));Assert.True(p.TryLocate(new NavPoint(68,64),c,out var to));
            var q=new NavScheduledRequest(p,c,new NavGraphProfile(new string('b',64),"distance",1,.8),new NavGraphCompileLimits(1048576,300000,1200000),new NavTerminalRegion("campaign",1,to,new[]{to}),
                new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,from,to,null),1048576,query:true,demandQuery:true,lazyPartition:true,certifiedLazyPartition:true);
            Assert.True(q.CertifiedLazyPartition);Assert.False(NavDemandQueryWork.LazyEligible(q));
            var w=new NavDemandQueryWork(q);
            try{
                for(int n=0;Proof(w).Progress==NavExclusionProgress.Pending;n++){Assert.Less(n,200);Assert.True(w.CanBookkeeping);Assert.False(w.CanGraph);Assert.False(w.CanConnector);Assert.Zero(w.SampleCount);Assert.Zero(w.FrontierCount);w.StepBookkeeping();}
                Assert.AreEqual(NavExclusionProgress.Certified,Proof(w).Progress);Assert.AreEqual(108781,Proof(w).Cursor.TerminalCertificate.ExcludedCount);Assert.AreEqual(252420,Proof(w).Cursor.TerminalCertificate.RemainingCount);
            }finally{w.ReleaseOwner();}
        }
        [Test] public void CertifiedCapacityFullIdsOriginsAndExactResultMatchFiniteGraph()
        {
            var q=CertifiedRequest();Assert.True(q.CertifiedLazyPartition);var w=new NavDemandQueryWork(q);
            var old=new NavScheduledRequest(q.Provider,q.Clearance,q.Profile,new NavGraphCompileLimits(49,49,196),q.Region,q.Subscription,98,query:true,demandQuery:true,demandQueryV2:true,demandQueryV3:true,demandQueryV6:true);
            var reference=new NavDemandQueryWork(old);
            try{
                for(int n=0;LazyField(w,"phase").ToString()!="Seed";n++){Assert.Less(n,10000);V3Step(w);}
                Assert.True(w.Save().Starts.Any(v=>v.Key>=q.Limits.MaxNodes),"Full S IDs survive U capacity");
                var graph=LayeredNavigationGraphCompiler.Compile(q.Provider,q.Clearance,q.Profile,new NavGraphCompileLimits(49,49,196));
                try{var p=(PartitionNavigationProvider)q.Provider;var expected=graph.Nodes.Where(n=>p.CanSweep(q.Subscription.Origin,n.Location,q.Clearance)).Select(n=>new System.Collections.Generic.KeyValuePair<int,double>(((int)Math.Round(n.Location.Position.X/80)+3)*7+(int)Math.Round(n.Location.Position.Z/80)+3,LayeredNavigationProvider.Distance(q.Subscription.Origin.Position,n.Location.Position)*p.SurfaceCost(n.Location.SurfaceId))).OrderBy(v=>v.Key);CollectionAssert.AreEqual(expected,w.Save().Starts);}finally{graph.ReleaseOwner();}
                LazyComplete(w);LazyComplete(reference);Assert.AreEqual(reference.ExactObjective,w.ExactObjective);
                var logical=NavDemandQueryWork.Restore(q,w.Save());try{Assert.AreEqual(w.ExactObjective,logical.ExactObjective);}finally{logical.ReleaseOwner();}
            }finally{reference.ReleaseOwner();w.ReleaseOwner();}
        }
        [TestCase(false)][TestCase(true)]
        public void CertifiedCapacitySchedulerColdAndInsufficientFallback(bool sufficient)
        {
            var raw=CertifiedRequest(sufficient);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));LayeredNavigationScheduler hot;
            using(birth){var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,birth.OwnedReservation());var q=NavScheduledRequest.CopyOwned(raw,p,birth.OwnedReservation());hot=LayeredNavigationScheduler.CreateOwned(birth.OwnedReservation());hot.BindAttemptArtifacts(new string('c',64));Assert.True(hot.Submit(q));q.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();hot.SealPrepared();birth.Attach(hot);birth.Commit(hot,_=>{});}
            LayeredNavigationScheduler.Job Head(LayeredNavigationScheduler s)=>((NavPersistentQueue<LayeredNavigationScheduler.Job>)LazyField(s,"jobs")).First;
            long epoch=0;
            LayeredNavigationScheduler Step(NavigationRetentionBudget b,LayeredNavigationScheduler source){Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,++epoch,out var scope));LayeredNavigationScheduler n;using(scope){n=source.ForkOwned(scope.OwnedReservation());n.Advance(epoch,new NavEpochQuota(256,256,256,256,256),_=>true,1);n.SealPrepared();scope.Attach(n);scope.Commit(n,_=>{});}return n;}
            while(Head(hot).Demand==null||(sufficient?Proof(Head(hot).Demand).Cursor.Stage!=NavExclusionStage.MinX:Head(hot).Compiler==null)){Assert.Less(epoch,1000);var n=Step(budget,hot);hot.ReleaseStorage();hot=n;}
            if(!sufficient){Assert.True(Head(hot).Demand.CertificateInsufficient);Assert.NotNull(Head(hot).Compiler);}else{Assert.Null(Head(hot).Compiler);Assert.Zero(Head(hot).GraphWork);}
            var bytes=CaptureGraph(budget,hot);var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));LayeredNavigationScheduler restored;using(decode){restored=(LayeredNavigationScheduler)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}
            try{
                CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                while(hot.ReadyCount==0){Assert.Less(epoch,10000);var a=Step(budget,hot);epoch--;var b=Step(cold,restored);hot.ReleaseStorage();restored.ReleaseStorage();hot=a;restored=b;CollectionAssert.AreEqual(CaptureGraph(budget,hot),CaptureGraph(cold,restored));}
                Assert.AreEqual(NavSolveStatus.Ready,hot.Save().Ready.Single().Status);
            }finally{hot.ReleaseStorage();restored.ReleaseStorage();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(cold.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(cold.HeldBytes);
        }
        [TestCase(14)][TestCase(15)][TestCase(16)][TestCase(17)][TestCase(18)][TestCase(19)][TestCase(20)] public void CertifiedCapacityWholeArrivalColdPendingNextAndLogicalVersion(int version)
        {
            var q=CertifiedRequest();var member=new NavArrivalMember(1,1,0,1,q.Subscription.Origin,q.Clearance,1,1,1,1,0,1,1,1);
            var canonical=((PartitionNavigationProvider)q.Provider).DetachedSnapshot();
            var raw=new NavArrivalInput("certified:"+q.Provider.Binding,"certified-profile",canonical,q.Profile,new NavigationProfile(),q.Limits,
                new NavArrivalCandidateSource("certified",1,q.Region.Requested,q.Region.Candidates),new[]{member},new NavArrivalOccupancySnapshot(1,Array.Empty<NavArrivalOccupant>()),q.MaxLabels,independentMembers:true);
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,1,0,out var birth));NavigationArrivalWork hot;
            using(birth){var input=NavigationArrivalPhysicalInput.CopyInitial(raw,birth.OwnedReservation());try{hot=NavigationArrivalWork.CreateOwned(input,birth.OwnedReservation());PrefixState(hot).TerminalCost=version>=20;PrefixState(hot).OwnedArrivalAnchor=version>=19;PrefixState(hot).OriginShadow=version>=18;PrefixState(hot).TerminalCorner=version>=17;PrefixState(hot).TerminalRectangle=version>=16;PrefixState(hot).RectangleOrigins=version>=15;}finally{input.StorageRoot.ReleaseOwner();}hot.SealPrepared();birth.Attach(hot);birth.Commit(hot,_=>{});}
            long epoch=0;
            LayeredNavigationScheduler.Job Head(NavigationArrivalWork w)=>((NavPersistentQueue<LayeredNavigationScheduler.Job>)LazyField(PrefixScheduler(w),"jobs")).First;
            while(Head(hot)?.Demand==null||Proof(Head(hot).Demand).Cursor.Stage!=NavExclusionStage.MinX){Assert.Less(epoch,1000);Assert.That(hot.Status,Is.EqualTo(NavArrivalStatus.Pending).Or.EqualTo(NavArrivalStatus.BudgetExhausted),"Only preparation/pending solve may precede the exact pending certificate checkpoint");hot=PrefixAdvance(budget,hot,epoch++,1,new NavEpochQuota(1,1,1,1,1));}
            Assert.True(Head(hot).Request.CertifiedLazyPartition);Assert.AreEqual(version,hot.Save().Version);
            var state=hot.Save();byte[] bytes=CaptureGraph(budget,hot);
            byte[] Next(NavigationRetentionBudget b,NavigationArrivalWork source){Assert.True(b.TryOpenScope(NavigationPreparationKind.Arrival,1,10000,out var step));NavigationArrivalWork n=null;try{using(step){n=source.ForkOwned(step.OwnedReservation());n.Advance(epoch,1,new NavEpochQuota(1,1,1,1,1));n.SealPrepared();step.Attach(n);step.Commit(n,_=>{});}return CaptureGraph(b,n);}finally{n?.ReleaseOwner();}}
            var expected=Next(budget,hot);hot.ReleaseOwner();PrefixNoLeaks(budget);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));NavigationArrivalWork restored;using(decode){restored=(NavigationArrivalWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}
            try{Assert.AreEqual(version,restored.Save().Version);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored));}finally{restored.ReleaseOwner();}PrefixNoLeaks(cold);
            var logical=NavigationArrivalWork.Restore(raw,state);try{Assert.AreEqual(version,logical.Save().Version);Assert.True(Head(logical).Request.CertifiedLazyPartition);logical.Advance(epoch,1,new NavEpochQuota(1,1,1,1,1));}finally{logical.ReleaseOwner();}
            if(version>=15){state.Version=version==16?15:16;Assert.Throws<ArgumentException>(()=>NavigationArrivalWork.Restore(raw,state),"A logical checkpoint cannot promote/demote pending recipe policy");state.Version=version;}
        }
        [TestCase("binary")][TestCase("skip")][TestCase("trace")]
        public void CertifiedCapacityColdNextCowRefusalAndLogical(string target)
        {
            var raw=target=="trace"?CertifiedTraceRequest():CertifiedRequest();var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));NavDemandQueryWork hot=null;
            using(birth){var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,birth.OwnedReservation());var q=NavScheduledRequest.CopyOwned(raw,p,birth.OwnedReservation());hot=new NavDemandQueryWork(q,birth.TryReserve);q.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();
                for(int n=0;;n++){Assert.Less(n,10000);var proof=Proof(hot);bool reached=target=="binary"?proof.Cursor.Stage==NavExclusionStage.MinX:target=="trace"?(int)LazyField(hot,"lazyTrace")==1:proof.Progress==NavExclusionProgress.Certified&&(int)LazyField(hot,"lazyStage")==4&&hot.CanBookkeeping;if(reached)break;Assert.AreEqual(NavSolveStatus.Pending,hot.Status);V3Step(hot);}hot.Seal();birth.Attach(hot);birth.Commit(hot,_=>{});}
            byte[] bytes=CaptureGraph(budget,hot);var state=hot.Save();
            byte[] Next(NavigationRetentionBudget b,NavDemandQueryWork source){Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));NavDemandQueryWork next=null;try{using(scope){next=source.Fork(scope.TryReserve);V3Step(next);next.Seal();scope.Attach(next);scope.Commit(next,_=>{});}return CaptureGraph(b,next);}finally{next?.ReleaseOwner();}}
            var expected=Next(budget,hot);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var probe));var count=new Refusal(probe,int.MaxValue);NavDemandQueryWork trial=null;try{trial=hot.Fork(count.TryReserve);V3Step(trial);}finally{trial?.ReleaseOwner();probe.Discard();}
            for(int n=1;n<=count.Calls;n++){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,n+10,out var scope));var refusal=new Refusal(scope,n);NavDemandQueryWork next=null;try{Assert.Throws<InvalidOperationException>(()=>{next=hot.Fork(refusal.TryReserve);V3Step(next);});}finally{next?.ReleaseOwner();scope.Discard();}CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));Assert.Zero(budget.HeldBytes);}
            hot.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));NavDemandQueryWork restored;using(decode){restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}
            try{CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored));}finally{restored.ReleaseOwner();}Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
            var logical=NavDemandQueryWork.Restore(raw,state);try{V3Step(logical);Assert.True(logical.Matches(raw));}finally{logical.ReleaseOwner();}
        }
        [TestCase("binary")][TestCase("skip")][TestCase("trace")]
        public void RectangleOriginsQueryColdNextCowRefusalAndLogical(string target)
        {
            var raw=WithRectangleOrigins(target=="trace"?CertifiedTraceRequest():CertifiedRequest());var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));NavDemandQueryWork hot=null;
            using(birth){var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,birth.OwnedReservation());var q=NavScheduledRequest.CopyOwned(raw,p,birth.OwnedReservation());hot=new NavDemandQueryWork(q,birth.TryReserve);q.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();
                for(int n=0;;n++){Assert.Less(n,10000);var proof=Proof(hot);bool reached=target=="binary"?proof.Cursor.Stage==NavExclusionStage.MinX:target=="trace"?(int)LazyField(hot,"lazyTrace")==1:proof.Progress==NavExclusionProgress.Certified&&(int)LazyField(hot,"lazyStage")==4&&hot.CanBookkeeping;if(reached)break;Assert.AreEqual(NavSolveStatus.Pending,hot.Status);V3Step(hot);}hot.Seal();birth.Attach(hot);birth.Commit(hot,_=>{});}
            byte[] bytes=CaptureGraph(budget,hot);var state=hot.Save();
            byte[] Next(NavigationRetentionBudget b,NavDemandQueryWork source){Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));NavDemandQueryWork next=null;try{using(scope){next=source.Fork(scope.TryReserve);V3Step(next);next.Seal();scope.Attach(next);scope.Commit(next,_=>{});}return CaptureGraph(b,next);}finally{next?.ReleaseOwner();}}
            var expected=Next(budget,hot);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var probe));var count=new Refusal(probe,int.MaxValue);NavDemandQueryWork trial=null;try{trial=hot.Fork(count.TryReserve);V3Step(trial);}finally{trial?.ReleaseOwner();probe.Discard();}
            for(int n=1;n<=count.Calls;n++){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,n+10,out var scope));var refusal=new Refusal(scope,n);NavDemandQueryWork next=null;try{Assert.Throws<InvalidOperationException>(()=>{next=hot.Fork(refusal.TryReserve);V3Step(next);});}finally{next?.ReleaseOwner();scope.Discard();}CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));Assert.Zero(budget.HeldBytes);}
            hot.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));NavDemandQueryWork restored;using(decode){restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}
            try{CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored));}finally{restored.ReleaseOwner();}Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
            var logical=NavDemandQueryWork.Restore(raw,state);try{V3Step(logical);Assert.True(logical.Matches(raw));}finally{logical.ReleaseOwner();}
        }
    }
}

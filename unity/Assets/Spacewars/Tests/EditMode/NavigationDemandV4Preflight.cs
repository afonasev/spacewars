using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavScheduledRequest V4Request(bool alternate=false)
        {
            var r=V3Request();var sub=r.Subscription;
            if(alternate){var end=r.Region.Candidates.Last();sub=new NavRouteSubscription(sub.WorldGeneration,sub.GroupId,sub.RootOrderRevision,sub.CommandSequence,sub.IssuedTick,sub.ActivationRevision,sub.EntityId,sub.Incarnation,sub.MemberOrderRevision,sub.MobilityRevision,sub.RequestSequence,sub.Origin,end,sub.HoldBinding,sub.Provenance);}
            return new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,sub,r.MaxLabels,query:true,demandQuery:true,demandQueryV2:true,demandQueryV3:true,demandQueryV4:true);
        }
        [TestCase(false)][TestCase(true)] public void DemandV4SeamBoundIsAdmissibleAndRouteKeepsFullEndpointReference(bool alternate)
        {
            var r=V4Request(alternate);var p=(PartitionNavigationProvider)r.Provider;
            var graph=LayeredNavigationGraphCompiler.Compile(p,r.Clearance,r.Profile,r.Limits);
            var d=Enumerable.Repeat(double.PositiveInfinity,graph.Nodes.Count).ToArray();
            try{
                int portal=0;foreach(var a in graph.Arcs){var from=graph.Nodes[a.From].Location;var to=graph.Nodes[a.To].Location;Assert.True(from.Position.X==to.Position.X||from.Position.Z==to.Position.Z);if(a.Trace!=null)foreach(var leg in a.Trace)if(leg.Kind==NavTypedLegKind.Portal){portal++;Assert.Zero(leg.Cost);Assert.AreEqual(leg.From.Position,leg.To.Position);}}
                Assert.Greater(portal,0);Assert.Greater(graph.Nodes.Select(n=>n.Location.SurfaceId).Distinct().Count(),1);
                for(int i=0;i<d.Length;i++)foreach(var c in r.Region.Candidates)if(p.CanSweep(graph.Nodes[i].Location,c,r.Clearance)&&p.CanSweep(c,r.Subscription.AssignedEndpoint,r.Clearance)){
                    Assert.AreEqual(c.SurfaceId,graph.Nodes[i].Location.SurfaceId);Assert.AreEqual(c.SurfaceId,r.Subscription.AssignedEndpoint.SurfaceId);
                    d[i]=Math.Min(d[i],LayeredNavigationProvider.Distance(graph.Nodes[i].Location.Position,c.Position)+LayeredNavigationProvider.Distance(c.Position,r.Subscription.AssignedEndpoint.Position));}
                for(int round=0;round<d.Length;round++){bool change=false;foreach(var a in graph.Arcs)if(d[a.To]+a.Cost<d[a.From]){d[a.From]=d[a.To]+a.Cost;change=true;}if(!change)break;}
                var seams=p.Seams.Where(s=>s.Allowed&&(s.FromSurface==r.Subscription.AssignedEndpoint.SurfaceId||s.ToSurface==r.Subscription.AssignedEndpoint.SurfaceId)).ToArray();Assert.IsNotEmpty(seams);
                int stronger=0;for(int i=0;i<d.Length;i++){
                    var at=graph.Nodes[i].Location;double old=NavDemandQueryWork.TypedLowerBound(at.Position,r.Subscription.AssignedEndpoint.Position,p.DemandSurfaceBounds(r.Subscription.AssignedEndpoint.SurfaceId));
                    double entry=at.SurfaceId==r.Subscription.AssignedEndpoint.SurfaceId?LayeredNavigationProvider.Distance(at.Position,r.Subscription.AssignedEndpoint.Position):seams.Min(s=>{NavDemandQueryWork.SeamBox(s,out var lo,out var hi);return NavDemandQueryWork.SeamLowerBound(at.Position,r.Subscription.AssignedEndpoint.Position,lo,hi);});
                    double h=Math.Max(old,entry);Assert.LessOrEqual(h,d[i],"raw admissibility node "+i);if(h>old+1e-6&& !double.IsInfinity(d[i]))stronger++;
                }
                Assert.Greater(stronger,0);double exact=double.PositiveInfinity;
                for(int i=0;i<d.Length;i++)if(p.CanSweep(r.Subscription.Origin,graph.Nodes[i].Location,r.Clearance))exact=Math.Min(exact,LayeredNavigationProvider.Distance(r.Subscription.Origin.Position,graph.Nodes[i].Location.Position)+d[i]);
                var w=new NavDemandQueryWork(r);try{
                    for(int step=0;w.Status==NavSolveStatus.Pending;step++){Assert.Less(step,50000);V3Step(w);}
                    Assert.AreEqual(NavSolveStatus.Ready,w.Status);Assert.AreEqual(exact,w.Route.Cost,1e-10);Assert.AreEqual(r.Subscription.AssignedEndpoint,w.Route.Legs.Last().To);
                    var state=w.Save();CollectionAssert.AreEqual(state.Locations.Select((at,i)=>new{at,i}).Where(x=>p.CanSweep(r.Subscription.Origin,x.at,r.Clearance)).Select(x=>x.i),state.Starts.Select(x=>x.Key));
                    Assert.AreEqual(r.Region.Candidates.Count,BitConverter.ToInt32(state.Scalars,16)/2); // terminal prefix, little-endian int below 256
                    TestContext.WriteLine("SEAM_REFERENCE alternate="+alternate+" nodes="+d.Length+" stronger="+stronger+" exact="+exact);
                }finally{w.ReleaseOwner();}
            }finally{graph.ReleaseOwner();}
        }
        [Test] public void DemandV4BoxesIncludeRealIncidentToleranceAndPolicyTagsKeepLegacyBytes()
        {
            foreach(var s in ((PartitionNavigationProvider)V4Request().Provider).Seams){
                NavDemandQueryWork.SeamBox(s,out var lo,out var hi);double t=TerrainLocations.IncidentBoxReach;
                Assert.LessOrEqual(lo.X,Math.Min(s.From.X,s.To.X)-t);Assert.LessOrEqual(lo.Z,Math.Min(s.From.Z,s.To.Z)-t);Assert.GreaterOrEqual(hi.X,Math.Max(s.From.X,s.To.X)+t);Assert.GreaterOrEqual(hi.Z,Math.Max(s.From.Z,s.To.Z)+t);
            }
            foreach(NavPhysicalStorageKind kind in Enum.GetValues(typeof(NavPhysicalStorageKind))){var old=new NavPhysicalTag(kind);Assert.AreEqual((int)kind<<24,old.Encoded);var tag=new NavPhysicalTag(kind,value:NavPhysicalValueKind.SeamBoundPolicyV4);Assert.True(tag.Valid,kind.ToString());Assert.AreEqual(kind,tag.Storage);Assert.AreEqual(tag,NavPhysicalTag.Decode(tag.Encoded));Assert.AreNotEqual(old,tag);}
        }
        private static NavDemandQueryWork V4Owned(NavigationRetentionBudget budget,string target,out NavScheduledRequest raw)
        {
            raw=V4Request();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var r=scope.OwnedReservation();
            var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,r);var recipe=NavScheduledRequest.CopyOwned(raw,p,r);var w=new NavDemandQueryWork(recipe,scope.TryReserve);p.StorageRoot.ReleaseOwner();recipe.StorageRoot.ReleaseOwner();
            for(int n=0;;n++){
                Assert.Less(n,50000);var state=w.Save();
                if(target=="Prepare"?state.SeamCursor==2:target=="Lane"?state.BoundNode>=0&&state.BoundCursor==0:target=="Reduce"?state.BoundNode>=0&&state.BoundCursor>0:target=="Publish"?state.BoundNode>=0&&state.BoundCursor==state.SeamBoxes.Length-2:target=="Search"?state.NodeBounds.Length>3: w.Status==NavSolveStatus.Ready)break;
                V3Step(w);
            }
            w.Seal();scope.Attach(w);scope.Commit(w,_=>{});return w;
        }
        [TestCase("Prepare")][TestCase("Lane")][TestCase("Reduce")][TestCase("Publish")][TestCase("Search")][TestCase("Done")]
        public void DemandV4ColdNextAndLogicalContinuationKeepPaidCursors(string target)
        {
            var hot=new NavigationRetentionBudget();var root=V4Owned(hot,target,out var raw);var logical=root.Save();Assert.AreEqual(((PartitionNavigationProvider)raw.Provider).Seams.Take(logical.SeamCursor).Count(s=>s.Allowed&&(s.FromSurface==raw.Subscription.AssignedEndpoint.SurfaceId||s.ToSurface==raw.Subscription.AssignedEndpoint.SurfaceId))*2,logical.SeamBoxes.Length);var bytes=CaptureGraph(hot,root);long charge=hot.PhysicalBytes;Assert.AreEqual(charge,Unique(root));
            byte[] Next(NavigationRetentionBudget b,NavDemandQueryWork w,out NavDemandQueryWork.Checkpoint saved){Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var n=w.Fork(scope.TryReserve);if(n.Status==NavSolveStatus.Pending)V3Step(n);n.Seal();scope.Attach(n);scope.Commit(n,_=>{});var result=CaptureGraph(b,n);saved=n.Save();n.ReleaseOwner();return result;}
            var expected=Next(hot,root,out var nextLogical);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,root));root.ReleaseOwner();PrefixNoLeaks(hot);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,10000,out var decode));var restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
            Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored,out _));restored.ReleaseOwner();PrefixNoLeaks(cold);
            var unowned=NavDemandQueryWork.Restore(raw,logical);try{
                var saved=unowned.Save();Assert.AreEqual(logical.SeamCursor,saved.SeamCursor);Assert.AreEqual(logical.BoundNode,saved.BoundNode);Assert.AreEqual(logical.BoundCursor,saved.BoundCursor);Assert.AreEqual(logical.BoundMinimum,saved.BoundMinimum);CollectionAssert.AreEqual(logical.NodeBounds,saved.NodeBounds);CollectionAssert.AreEqual(logical.SeamBoxes,saved.SeamBoxes);
                if(unowned.Status==NavSolveStatus.Pending)V3Step(unowned);saved=unowned.Save();CollectionAssert.AreEqual(nextLogical.Scalars,saved.Scalars);CollectionAssert.AreEqual(nextLogical.NodeBounds,saved.NodeBounds);Assert.AreEqual(nextLogical.BoundCursor,saved.BoundCursor);Assert.AreEqual(nextLogical.BoundMinimum,saved.BoundMinimum);
            }finally{unowned.ReleaseOwner();}
        }
        [TestCase("Prepare")][TestCase("Publish")]
        public void DemandV4EveryNthPaidLaneRefusalPreservesCommittedBytes(string target)
        {
            var b=new NavigationRetentionBudget();var root=V4Owned(b,target,out _);var bytes=CaptureGraph(b,root);long charge=b.PhysicalBytes;
            Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var counting));var count=new Refusal(counting,int.MaxValue);var trial=root.Fork(count.TryReserve);V3Step(trial);trial.Seal();int total=count.Calls;trial.ReleaseOwner();counting.Discard();Assert.Greater(total,0);
            for(int nth=1;nth<=total;nth++){
                Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var refusal=new Refusal(scope,nth);NavDemandQueryWork next=null;
                Assert.Throws<InvalidOperationException>(()=>{next=root.Fork(refusal.TryReserve);V3Step(next);next.Seal();});Assert.AreEqual(nth,refusal.Calls);next?.ReleaseOwner();scope.Discard();Assert.AreEqual(charge,b.PhysicalBytes);Assert.Zero(b.HeldBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(b,root));
            }
            TestContext.WriteLine("SEAM_REFUSAL target="+target+" reservations="+total);root.ReleaseOwner();PrefixNoLeaks(b);
        }
        [Test] public void DemandV4SchedulerPaysPreparationAndEvaluationWithoutSameEpochRefill()
        {
            var r=V4Request();var scheduler=new LayeredNavigationScheduler();Assert.True(scheduler.Submit(r));try{
                scheduler.Advance(0,new NavEpochQuota(10000,0,10000,0,0),_=>true);var s=scheduler.Save();var d=s.Jobs.Single().Demand;Assert.NotNull(d);Assert.Zero(d.SeamCursor);Assert.IsEmpty(d.Locations);
                Assert.Zero(scheduler.Advance(0,new NavEpochQuota(10000,0,10000,10000,0),_=>true));Assert.Zero(scheduler.Save().Jobs.Single().Demand.SeamCursor);
                for(int n=0;n<((PartitionNavigationProvider)r.Provider).Seams.Count;n++){scheduler.Advance(n+1,new NavEpochQuota(0,0,0,1,0),_=>true);Assert.AreEqual(n+1,scheduler.Save().Jobs.Single().Demand.SeamCursor);}
                for(long epoch=100;scheduler.ReadyCount==0;epoch++){Assert.Less(epoch,50000);scheduler.Advance(epoch,new NavEpochQuota(13,0,7,3,0),_=>true);Assert.Zero(scheduler.Advance(epoch,new NavEpochQuota(10000,0,10000,10000,0),_=>true));}
                Assert.True(scheduler.TryTake(_=>true,out var done));Assert.AreEqual(NavSolveStatus.Ready,done.Status);done.ReleaseOwner();
            }finally{scheduler.ClearWorld();scheduler.ReleaseOwner();}
        }
        [Test] public void DemandV4SharedCatalogAndSchedulerColdBirthKeepEndpointSpecificPaidLane()
        {
            var hot=new NavigationRetentionBudget();var cold=new NavigationRetentionBudget();LayeredNavigationScheduler scheduler=null,restored=null;NavScheduledRequest first=null;
            try{
                Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var birth));using(birth){
                    var r=birth.OwnedReservation();var raw=V4Request();var provider=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,r);first=NavScheduledRequest.CopyOwned(raw,provider,r);provider.StorageRoot.ReleaseOwner();
                    scheduler=LayeredNavigationScheduler.CreateOwned(r);scheduler.SessionOwnsProviders=true;scheduler.BindAttemptArtifacts(new string('d',64));Assert.True(scheduler.Submit(first));scheduler.SealPrepared();birth.Attach(scheduler);birth.Commit(scheduler,_=>{});
                }
                long epoch=0;while(scheduler.PendingCount>0){Assert.Less(epoch,1000);scheduler=DemandAttemptStep(hot,scheduler,epoch++,new NavEpochQuota(256,0,256,256,0));}
                scheduler=TakeDemandReady(hot,scheduler,out _);
                Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var submit));using(submit){
                    var n=scheduler.ForkOwned(submit.OwnedReservation());var raw=V4Request(true);var owned=NavScheduledRequest.CopyOwned(raw,first.Provider,submit.OwnedReservation());Assert.True(n.Submit(owned));owned.StorageRoot.ReleaseOwner();n.SealPrepared();submit.Attach(n);submit.Commit(n,_=>{});scheduler.ReleaseOwner();scheduler=n;
                }
                // A full sample catalog is reused, but the sibling endpoint's
                // seam preparation is neither inherited nor a free birth step.
                scheduler=DemandAttemptStep(hot,scheduler,epoch++,new NavEpochQuota(0,0,1,0,0),1);
                Assert.AreEqual(LayeredNavigationScheduler.JobPhase.Graph,CacheHead(scheduler).Phase);Assert.Null(CacheHead(scheduler).Demand);
                long graph=scheduler.Counters.PhysicalGraphWork;
                scheduler=DemandAttemptStep(hot,scheduler,epoch,new NavEpochQuota(1,0,0,0,0),1);
                Assert.AreEqual(graph,scheduler.Counters.PhysicalGraphWork);var state=scheduler.Save().Jobs.Single().Demand;Assert.NotNull(state);Assert.Greater(state.Locations.Length,0);Assert.Zero(state.SeamCursor);Assert.IsEmpty(state.NodeBounds);Assert.IsEmpty(state.SeamBoxes);
                byte[] bytes=CaptureGraph(hot,scheduler);Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));using(decode){restored=(LayeredNavigationScheduler)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                scheduler=DemandAttemptStep(hot,scheduler,epoch,new NavEpochQuota(0,0,0,1,0),1);restored=DemandAttemptStep(cold,restored,epoch++,new NavEpochQuota(0,0,0,1,0),1);Assert.Zero(scheduler.Save().Jobs.Single().Demand.SeamCursor);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,scheduler));
                scheduler=DemandAttemptStep(hot,scheduler,epoch,new NavEpochQuota(0,0,0,1,0),1);restored=DemandAttemptStep(cold,restored,epoch,new NavEpochQuota(0,0,0,1,0),1);Assert.AreEqual(1,scheduler.Save().Jobs.Single().Demand.SeamCursor);CollectionAssert.AreEqual(CaptureGraph(hot,scheduler),CaptureGraph(cold,restored));
                var saved=scheduler.Save();var logical=LayeredNavigationScheduler.Restore(saved,new[]{CacheHead(scheduler).Request});try{Assert.AreEqual(scheduler.LogicalContinuationBinding,logical.LogicalContinuationBinding);}finally{logical.ClearWorld();logical.ReleaseOwner();}
            }finally{restored?.ReleaseOwner();scheduler?.ReleaseOwner();first?.StorageRoot.ReleaseOwner();}PrefixNoLeaks(hot);PrefixNoLeaks(cold);
        }
        [Test] public void DemandV4LegacyKeysAndArrivalV5ColdLogicalKeepOccupancyCarry()
        {
            var old=V3Request();var current=V4Request();Assert.False(old.DemandQueryV4);Assert.True(current.DemandQueryV4);Assert.AreNotEqual(old.CompileKey,current.CompileKey);Assert.AreNotEqual(old.ArtifactKey,current.ArtifactKey);
            var b=new NavigationRetentionBudget();var cold=new NavigationRetentionBudget();NavigationArrivalWork prior=null,work=null,restored=null,logical=null;
            try{
                prior=PrefixBirth(b,PrefixInput("old",100),ready:true,version:9);var raw=PrefixInput("append",200);work=PrefixBirth(b,raw,prior,version:9);
                long epoch=0;while(PrefixState(work).Gate!=4||PrefixState(work).Cursor!=2){Assert.Less(epoch,1000);work=PrefixAdvance(b,work,epoch++,1,PrefixZero);Assert.NotNull(PrefixState(work).Oracle);}
                var state=work.Save();Assert.AreEqual(9,state.Version);Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.GroupDemandArrivalWorkV4,value:NavPhysicalValueKind.SeamBoundPolicyV4),((INavPhysicalBody)work).PhysicalTag);
                var bytes=CaptureGraph(b,work);Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,0,out var decode));restored=(NavigationArrivalWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                logical=NavigationArrivalWork.Restore(raw,state);Assert.AreEqual(work.LogicalContinuationBinding,logical.LogicalContinuationBinding);work=PrefixAdvance(b,work,epoch,1,PrefixZero);restored=PrefixAdvance(cold,restored,epoch,1,PrefixZero);logical.Advance(epoch,1,PrefixZero);CollectionAssert.AreEqual(CaptureGraph(b,work),CaptureGraph(cold,restored));Assert.AreEqual(work.LogicalContinuationBinding,logical.LogicalContinuationBinding);
            }finally{logical?.ReleaseOwner();restored?.ReleaseOwner();work?.ReleaseOwner();prior?.ReleaseOwner();}PrefixNoLeaks(b);PrefixNoLeaks(cold);
        }
    }
}

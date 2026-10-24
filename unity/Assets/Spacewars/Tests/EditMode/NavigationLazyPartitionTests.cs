using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavScheduledRequest LazyRequest(int labels=81)
        {var r=V6Request();return new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,r.Subscription,labels,query:true,demandQuery:true,lazyPartition:true);}
        private static NavScheduledRequest LazyDetourRequest()
        {
            var map=ThreeCrossingsMap.Default;var p=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.Concat(new[]{new NavObstacle(43,34.5,45,52)}).ToArray(),map.Revision),null);
            var c=new NavClearanceProfile("ground",1,"ground",1,.25);Assert.True(p.TryLocate(new NavPoint(40,40),c,out var from));Assert.True(p.TryLocate(new NavPoint(48,40),c,out var to));Assert.AreEqual(from.SurfaceId,to.SurfaceId);
            var region=new NavTerminalRegion("target-detour",1,to,new[]{to,new NavLocation(new NavPoint(48.125,40),to.SurfaceId)});
            return new NavScheduledRequest(p,c,new NavGraphProfile(new string('b',64),"distance",1,8),new NavGraphCompileLimits(2000,2000,10000),region,new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,from,to,null),289,query:true,demandQuery:true,lazyPartition:true);
        }
        private static object LazyField(object w,string name)=>w.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(w);
        private static void LazyComplete(NavDemandQueryWork w)
        {for(int i=0;w.Status==NavSolveStatus.Pending;i++){Assert.Less(i,50000);V3Step(w);}Assert.AreEqual(NavSolveStatus.Ready,w.Status);}
        [TestCase(false)][TestCase(true)] public void LazyPartitionExhaustiveOriginsFullDomainMinimumAndEligibility(bool detour)
        {
            var r=detour?LazyDetourRequest():LazyRequest();Assert.True(r.LazyPartition);Assert.True(r.DemandQueryV6);Assert.False(NavDemandQueryWork.Eligible(r));
            var rejected=LazyRequest(80);Assert.False(rejected.LazyPartition);Assert.False(rejected.DemandQuery,"S>MaxLabels keeps conservative fallback");
            var old=V6Request();Assert.False(old.LazyPartition);var p=(PartitionNavigationProvider)r.Provider;
            var graph=LayeredNavigationGraphCompiler.Compile(p,r.Clearance,r.Profile,r.Limits);var w=new NavDemandQueryWork(r);
            try{
                for(int i=0;(string)LazyField(w,"phase").ToString()!="Seed";i++){Assert.Less(i,50000);Assert.AreEqual(NavSolveStatus.Pending,w.Status);V3Step(w);}
                Assert.True(w.HeuristicActive);int min=(int)Math.Ceiling(-p.HalfExtent/r.Profile.GridCell),width=(int)Math.Floor(p.HalfExtent/r.Profile.GridCell)-min+1;
                int Id(NavLocation at)=>((int)Math.Round(at.Position.X/r.Profile.GridCell)-min)*width+(int)Math.Round(at.Position.Z/r.Profile.GridCell)-min;
                var expected=graph.Nodes.Where(n=>p.CanSweep(r.Subscription.Origin,n.Location,r.Clearance)).Select(n=>new KeyValuePair<int,double>(Id(n.Location),LayeredNavigationProvider.Distance(r.Subscription.Origin.Position,n.Location.Position)*p.SurfaceCost(r.Subscription.Origin.SurfaceId))).OrderBy(v=>v.Key).ToArray();
                CollectionAssert.AreEqual(expected,w.Save().Starts,"ALL ingress seeds before Pop, same actual source");
                if(detour)Assert.False(graph.Nodes.Any(n=>p.CanSweep(r.Subscription.Origin,n.Location,r.Clearance)&&r.Region.Candidates.Any(c=>p.CanSweep(n.Location,c,r.Clearance))),"All target-surface ingress nodes have blocked candidate sweeps; outgoing edges are required");
                var box=PreparedBox(w);foreach(var n in graph.Nodes.Where(n=>n.Location.SurfaceId==r.Subscription.AssignedEndpoint.SurfaceId))Assert.True(box.Contains(n.Location.Position));
                var d=new BigInteger?[graph.Nodes.Count];foreach(var n in graph.Nodes)if(p.CanSweep(r.Subscription.Origin,n.Location,r.Clearance))d[n.Id]=ExactReference(LayeredNavigationProvider.Distance(r.Subscription.Origin.Position,n.Location.Position)*p.SurfaceCost(r.Subscription.Origin.SurfaceId));
                for(int round=0;round<d.Length;round++){bool changed=false;foreach(var a in graph.Arcs){if(!d[a.From].HasValue)continue;var v=d[a.From].Value+ExactReference(a.Cost);if(!d[a.To].HasValue||v<d[a.To].Value){d[a.To]=v;changed=true;}}if(!changed)break;}
                BigInteger? best=null;foreach(var n in graph.Nodes)if(d[n.Id].HasValue)foreach(var c in r.Region.Candidates)if(p.CanSweep(n.Location,c,r.Clearance)&&p.CanSweep(c,r.Subscription.AssignedEndpoint,r.Clearance)){
                    var value=d[n.Id].Value+ExactReference(LayeredNavigationProvider.Distance(n.Location.Position,c.Position)*p.SurfaceCost(n.Location.SurfaceId))+ExactReference(LayeredNavigationProvider.Distance(c.Position,r.Subscription.AssignedEndpoint.Position)*p.SurfaceCost(c.SurfaceId));if(!best.HasValue||value<best.Value)best=value;}
                Assert.True(best.HasValue);LazyComplete(w);Assert.AreEqual(best.Value,ExactReference(w.ExactObjective));
                foreach(var leg in w.Route.Legs)Assert.True(p.CertifyHistoricalLeg(leg,r.Clearance,true),"Actual typed incident-surface/portal/cost qualification on identical provider");
                var logical=NavDemandQueryWork.Restore(r,w.Save());try{Assert.AreEqual(w.ExactObjective,logical.ExactObjective);}finally{logical.ReleaseOwner();}
            }finally{w.ReleaseOwner();graph.ReleaseOwner();}
        }
        [Test] public void LazyPartitionPartialStoreReuseKeepsExhaustiveOriginsAndExactObjective()
        {
            var r=LazyRequest();var first=new NavDemandQueryWork(r);NavLazyPartitionStore store=null;NavDemandQueryWork next=null;
            try{LazyComplete(first);store=first.ExportLazyStore();Assert.NotNull(store);int known=store.Cells.Count;
                var q=r.Subscription;var sub=new NavRouteSubscription(q.WorldGeneration,q.GroupId,q.RootOrderRevision,q.CommandSequence,q.IssuedTick,q.ActivationRevision,2,q.Incarnation,q.MemberOrderRevision,2,2,q.Origin,q.AssignedEndpoint,q.HoldBinding,q.Provenance);
                var clearance=new NavClearanceProfile(r.Clearance.Id,r.Clearance.Revision,r.Clearance.MobilityClass,2,r.Clearance.Radius);
                var sibling=new NavScheduledRequest(r.Provider,clearance,r.Profile,r.Limits,r.Region,sub,r.MaxLabels,query:true,demandQuery:true,lazyPartition:true);
                Assert.True(store.Compatible(sibling));next=new NavDemandQueryWork(sibling,lazyShared:store);LazyComplete(next);
                Assert.AreEqual(first.ExactObjective,next.ExactObjective);Assert.AreEqual(known,((NavLazyPartitionStore)LazyField(next,"lazyStore")).Cells.Count);Assert.AreEqual(known,store.Cells.Count);
                Assert.AreEqual(2,next.Route.Subscription.MobilityRevision);CollectionAssert.AreEqual(first.Save().Starts,next.Save().Starts);
            }finally{next?.ReleaseOwner();store?.Release();first.ReleaseOwner();}
        }
        [Test] public void LazyPartitionSchedulerCacheLogicalRestoreAfterSourceRelease()
        {
            var raw=LazyRequest();var q=raw.Subscription;
            var sub=new NavRouteSubscription(q.WorldGeneration,q.GroupId,q.RootOrderRevision,q.CommandSequence,q.IssuedTick,q.ActivationRevision,2,q.Incarnation,q.MemberOrderRevision,q.MobilityRevision,2,q.Origin,q.AssignedEndpoint,q.HoldBinding,q.Provenance);
            var second=new NavScheduledRequest(raw.Provider,raw.Clearance,raw.Profile,raw.Limits,raw.Region,sub,raw.MaxLabels,query:true,demandQuery:true,lazyPartition:true);
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));LayeredNavigationScheduler scheduler=null;
            using(birth){var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,birth.OwnedReservation());var recipe=NavScheduledRequest.CopyOwned(raw,p,birth.OwnedReservation());scheduler=LayeredNavigationScheduler.CreateOwned(birth.OwnedReservation());scheduler.BindAttemptArtifacts(new string('d',64));Assert.True(scheduler.Submit(recipe));recipe.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();scheduler.SealPrepared();birth.Attach(scheduler);birth.Commit(scheduler,_=>{});}
            long epoch=0;double cost=0;
            while(scheduler.ReadyCount==0){Assert.Less(epoch,100);Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,epoch+1,out var scope));var n=scheduler.ForkOwned(scope.OwnedReservation());n.Advance(epoch++,new NavEpochQuota(256,256,256,256,256),_=>true);n.SealPrepared();scope.Attach(n);scope.Commit(n,_=>{});scheduler.ReleaseStorage();scheduler=n;}
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1000,out var issue));
            using(issue){var n=scheduler.ForkOwned(issue.OwnedReservation());Assert.True(n.TryTake(_=>true,out var ready));cost=ready.Route.Cost;ready.ReleaseOwner();var store=(NavLazyPartitionStore)typeof(LayeredNavigationScheduler).GetField("lazyCatalog",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(n);Assert.NotNull(store);
                var recipe=NavScheduledRequest.CopyOwned(second,store.Recipe.Provider,issue.OwnedReservation());Assert.True(n.Submit(recipe));recipe.StorageRoot.ReleaseOwner();n.SealPrepared();issue.Attach(n);issue.Commit(n,_=>{});scheduler.ReleaseStorage();scheduler=n;}
            // Observe the actual Direct -> Graph -> Demand lifecycle with full
            // lane allowances; one charged structural operation per fixture step.
            LayeredNavigationScheduler.Job Head()=>((NavPersistentQueue<LayeredNavigationScheduler.Job>)typeof(LayeredNavigationScheduler).GetField("jobs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scheduler)).First;
            while(Head().Demand==null){Assert.Less(epoch,100);Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,epoch+1001,out var step));using(step){var n=scheduler.ForkOwned(step.OwnedReservation());n.Advance(epoch++,new NavEpochQuota(256,256,256,256,256),_=>true,1);n.SealPrepared();step.Attach(n);step.Commit(n,_=>{});scheduler.ReleaseStorage();scheduler=n;}}
            var state=scheduler.Save();string binding=scheduler.LogicalContinuationBinding;
            // The caller supplies fresh canonical declarations, not authored-only spatial-cache estimates.
            var canonical=((PartitionNavigationProvider)raw.Provider).DetachedSnapshot();var canonicalSecond=NavDemandQueryWork.DetachSharedRecipe(second,canonical);Assert.AreEqual(second.Provider.Binding,canonical.Binding);Assert.AreEqual(state.ProviderBytes,canonical.EstimatedRetainedBytes);Assert.NotNull(state.LazyCatalog);Assert.NotNull(state.Jobs.Single().Demand,"Actual pending Demand observed after paid Direct and birth lifecycle");Assert.NotNull(state.Jobs.Single().Demand.LazyStore);
            scheduler.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            var restored=LayeredNavigationScheduler.Restore(state,new[]{canonicalSecond},new[]{canonical});
            try{Assert.AreEqual(binding,restored.LogicalContinuationBinding);while(restored.ReadyCount==0){Assert.Less(epoch,200);restored.Advance(epoch++,new NavEpochQuota(256,256,256,256,256),_=>true);}Assert.True(restored.TryTake(_=>true,out var ready));try{Assert.AreEqual(cost,ready.Route.Cost);Assert.AreEqual(2,ready.Subscription.EntityId);}finally{ready.ReleaseOwner();}}finally{restored.ReleaseStorage();}
        }
        [Test] public void LazyPartitionPendingColdNextCowAndEveryRefusalPreserveBytes()
        {
            var raw=LazyRequest();var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var birth));NavDemandQueryWork hot=null;
            using(birth){var r=birth.OwnedReservation();var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,r);var recipe=NavScheduledRequest.CopyOwned(raw,p,r);hot=new NavDemandQueryWork(recipe,birth.TryReserve);recipe.StorageRoot.ReleaseOwner();p.StorageRoot.ReleaseOwner();
                int i=0;while(hot.Status==NavSolveStatus.Pending&&(int)LazyField(hot,"lazyTrace")!=1){Assert.Less(++i,50000);V3Step(hot);}Assert.AreEqual(1,(int)LazyField(hot,"lazyTrace"),"Persist Present classification before separately paid trace");hot.Seal();birth.Attach(hot);birth.Commit(hot,_=>{});}
            var bytes=CaptureGraph(budget,hot);var state=hot.Save();
            byte[] Next(NavigationRetentionBudget b,NavDemandQueryWork source){Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var s));NavDemandQueryWork n=null;try{using(s){n=source.Fork(s.TryReserve);V3Step(n);n.Seal();s.Attach(n);s.Commit(n,_=>{});}return CaptureGraph(b,n);}finally{n?.ReleaseOwner();}}
            var expected=Next(budget,hot);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));NavDemandQueryWork restored=null;
            using(decode){restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});}
            try{CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored));}finally{restored.ReleaseOwner();}Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);

            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var probe));var counter=new Refusal(probe,int.MaxValue);NavDemandQueryWork trial=null;
            try{trial=hot.Fork(counter.TryReserve);V3Step(trial);}finally{trial?.ReleaseOwner();probe.Discard();}
            for(int nth=1;nth<=counter.Calls;nth++){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,nth+10,out var scope));var refusal=new Refusal(scope,nth);NavDemandQueryWork n=null;
                try{Assert.Throws<InvalidOperationException>(()=>{n=hot.Fork(refusal.TryReserve);V3Step(n);});}finally{n?.ReleaseOwner();scope.Discard();}CollectionAssert.AreEqual(bytes,CaptureGraph(budget,hot));Assert.Zero(budget.HeldBytes);}
            hot.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.False(budget.CaptureActive);
            // Original owned query/recipe/provider are now completely released.
            var logical=NavDemandQueryWork.Restore(raw,state);try{V3Step(logical);var target=logical.Save();var again=NavDemandQueryWork.Restore(raw,state);try{V3Step(again);CollectionAssert.AreEqual(target.Scalars,again.Save().Scalars);CollectionAssert.AreEqual(target.ExactDistance,again.Save().ExactDistance);CollectionAssert.AreEqual(target.LazyStore.Cells,again.Save().LazyStore.Cells);}finally{again.ReleaseOwner();}}finally{logical.ReleaseOwner();}
        }
    }
}

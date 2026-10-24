using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavScheduledRequest V3Request()
        {var r=V2Request();return new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,r.Subscription,r.MaxLabels,query:true,demandQuery:true,demandQueryV2:true,demandQueryV3:true);}
        private static void V3Step(NavDemandQueryWork w)
        {if(w.CanBookkeeping)w.StepBookkeeping();else if(w.CanConnector)w.StepConnector();else{Assert.True(w.CanGraph);w.StepGraph();}}
        [TestCase(false)][TestCase(true)] public void DemandV3IndexPrefixColdNextAndCompleteStartsAreExact(bool complete)
        {
            Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(typeof(NavDemandQueryCatalog)),256);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var r=scope.OwnedReservation();var raw=V3Request();var p=PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw.Provider,r);var recipe=NavScheduledRequest.CopyOwned(raw,p,r);var w=new NavDemandQueryWork(recipe,scope.TryReserve);p.StorageRoot.ReleaseOwner();recipe.StorageRoot.ReleaseOwner();
            for(int n=0;;n++){Assert.Less(n,20000);var s=w.Save();if(complete?s.Scalars[0]==1:w.CanBookkeeping)break;V3Step(w);}
            var state=w.Save();Assert.AreEqual(complete?state.Locations.Length:state.Locations.Length-1,w.IndexedCount);
            if(complete){var expected=state.Locations.Select((at,i)=>new{at,i}).Where(x=>recipe.Provider.CanSweep(recipe.Subscription.Origin,x.at,recipe.Clearance)).Select(x=>new System.Collections.Generic.KeyValuePair<int,double>(x.i,LayeredNavigationProvider.Distance(recipe.Subscription.Origin.Position,x.at.Position))).ToArray();CollectionAssert.AreEqual(expected,state.Starts);var matching=state.Locations.Count(at=>at.SurfaceId==recipe.Subscription.Origin.SurfaceId);Assert.Less(matching,state.Locations.Length);TestContext.WriteLine("Full catalog="+state.Locations.Length+" matching="+matching+" starts="+state.Starts.Length);}
            if(complete){var catalog=w.ExportCatalog(scope.TryReserve);Assert.NotNull(catalog);var sibling=new NavDemandQueryWork(recipe,scope.TryReserve,shared:catalog);Assert.AreEqual(w.SampleCount,sibling.IndexedCount);w.ReleaseOwner();catalog.ReleaseOwner();w=sibling;state=w.Save();}
            w.Seal();scope.Attach(w);scope.Commit(w,_=>{});var bytes=CaptureGraph(budget,w);
            byte[] Next(NavigationRetentionBudget b,NavDemandQueryWork source){Assert.True(b.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var step));var next=source.Fork(step.TryReserve);V3Step(next);next.Seal();step.Attach(next);step.Commit(next,_=>{});var value=CaptureGraph(b,next);next.ReleaseOwner();return value;}
            var expectedNext=Next(budget,w);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,w));w.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,10000,out var decode));var restored=(NavDemandQueryWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expectedNext,Next(cold,restored));restored.ReleaseOwner();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            var logical=NavDemandQueryWork.Restore(raw,state);try{Assert.AreEqual(state.Indexed,logical.IndexedCount);V3Step(logical);}finally{logical.ReleaseOwner();}
        }
        [Test] public void DemandV3TypedBoundAndRouteRespectFullCardinalReference()
        {
            var r=V3Request();var p=(PartitionNavigationProvider)r.Provider;var graph=LayeredNavigationGraphCompiler.Compile(p,r.Clearance,r.Profile,r.Limits);var d=Enumerable.Repeat(double.PositiveInfinity,graph.Nodes.Count).ToArray();
            try{
                for(int i=0;i<d.Length;i++)foreach(var c in r.Region.Candidates)if(p.CanSweep(graph.Nodes[i].Location,c,r.Clearance)&&p.CanSweep(c,r.Subscription.AssignedEndpoint,r.Clearance))d[i]=Math.Min(d[i],LayeredNavigationProvider.Distance(graph.Nodes[i].Location.Position,c.Position)+LayeredNavigationProvider.Distance(c.Position,r.Subscription.AssignedEndpoint.Position));
                for(int round=0;round<d.Length;round++){bool changed=false;foreach(var a in graph.Arcs)if(d[a.To]+a.Cost<d[a.From]){d[a.From]=d[a.To]+a.Cost;changed=true;}if(!changed)break;}
                double margin=8.0*(d.Length+4)*(p.TracePortalBound+4.0)*2.2204460492503131e-16;var bounds=p.DemandSurfaceBounds(r.Subscription.AssignedEndpoint.SurfaceId);int strictlyStronger=0;
                for(int i=0;i<d.Length;i++){double h=NavDemandQueryWork.TypedLowerBound(graph.Nodes[i].Location.Position,r.Subscription.AssignedEndpoint.Position,bounds);Assert.LessOrEqual(h*(1-margin),d[i],"admissibility node "+i);if(h>LayeredNavigationProvider.Distance(graph.Nodes[i].Location.Position,r.Subscription.AssignedEndpoint.Position))strictlyStronger++;}
                Assert.Greater(strictlyStronger,0);double exact=double.PositiveInfinity;for(int i=0;i<d.Length;i++)if(p.CanSweep(r.Subscription.Origin,graph.Nodes[i].Location,r.Clearance))exact=Math.Min(exact,LayeredNavigationProvider.Distance(r.Subscription.Origin.Position,graph.Nodes[i].Location.Position)+d[i]);
                var w=new NavDemandQueryWork(r);try{for(int step=0;w.Status==NavSolveStatus.Pending;step++){Assert.Less(step,20000);V3Step(w);}Assert.AreEqual(NavSolveStatus.Ready,w.Status);Assert.LessOrEqual(Math.Abs(w.Route.Cost-exact),margin*Math.Max(1,exact));Assert.AreEqual(r.Subscription.AssignedEndpoint,w.Route.Legs.Last().To);}finally{w.ReleaseOwner();}
            }finally{graph.ReleaseOwner();}
        }
        [Test] public void DemandV3IndexUsesBookkeepingWithoutRefillOrFreeRebuild()
        {
            var r=V3Request();var scheduler=new LayeredNavigationScheduler();Assert.True(scheduler.Submit(r));try{
                scheduler.Advance(0,new NavEpochQuota(10000,0,10000,0,0),_=>true);var state=scheduler.Save();var job=state.Jobs.Single();Assert.NotNull(job.Demand);Assert.AreEqual(0,job.Demand.Indexed);Assert.AreEqual(1,job.Demand.Locations.Length);long g=scheduler.Counters.PhysicalGraphWork;
                Assert.Zero(scheduler.Advance(0,new NavEpochQuota(10000,0,10000,10000,0),_=>true));Assert.AreEqual(g,scheduler.Counters.PhysicalGraphWork);Assert.AreEqual(0,scheduler.Save().Jobs.Single().Demand.Indexed);
                var cold=LayeredNavigationScheduler.Restore(state,new[]{r});try{Assert.AreEqual(0,cold.Save().Jobs.Single().Demand.Indexed);cold.Advance(1,new NavEpochQuota(0,0,0,1,0),_=>true);Assert.AreEqual(0,cold.Save().Jobs.Single().Demand.Indexed,"first paid step allocates index surface page");cold.Advance(2,new NavEpochQuota(0,0,0,1,0),_=>true);Assert.AreEqual(1,cold.Save().Jobs.Single().Demand.Indexed);}finally{cold.ClearWorld();cold.ReleaseOwner();}
            }finally{scheduler.ClearWorld();scheduler.ReleaseOwner();}
        }
        [Test] public void DemandV3ArrivalOwnedPolicyColdAndLogical()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,10000,out var scope));var input=CarryInput(false,100);var w=OwnedCarry(input,scope.OwnedReservation(),7);w.Advance(0,1,new NavEpochQuota(0,0,0,0,0));var logical=w.Save();Assert.AreEqual(7,logical.Version);w.SealPrepared();scope.Attach(w);scope.Commit(w,_=>{});var bytes=CaptureGraph(budget,w);w.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,10000,out var decode));var restored=(NavigationArrivalWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));restored.ReleaseOwner();Assert.Zero(cold.PhysicalBytes);var unowned=NavigationArrivalWork.Restore(input,logical);try{Assert.AreEqual(7,unowned.Save().Version);Assert.AreEqual(NavArrivalStatus.Ready,NavigationArrivalTestFactory.Complete(unowned));}finally{unowned.ReleaseOwner();}
        }
    }
}

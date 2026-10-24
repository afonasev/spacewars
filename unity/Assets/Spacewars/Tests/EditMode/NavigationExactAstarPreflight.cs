using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static NavScheduledRequest V6Request(){var r=V3Request();return new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,r.Subscription,r.MaxLabels,query:true,demandQuery:true,demandQueryV2:true,demandQueryV3:true,demandQueryV6:true);}
        private static NavHeuristicBox PreparedBox(NavDemandQueryWork w)=>(NavHeuristicBox)typeof(NavDemandQueryWork).GetField("hBox",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(w);
        [Test] public void ExactAstarSignedFloorDomainAndActualTinyTerminals()
        {
            var values=new[]{-double.MaxValue,-1e16,-1.0,-NavExactHeuristic.Power(-550),-0.0,0.0,NavExactHeuristic.Power(-550),1.0,1e16,double.MaxValue};
            BigInteger Signed(double x)=>x<0?-ExactReference(-x):ExactReference(x);
            foreach(double a in values)foreach(double b in values){var difference=NavExactCost.Difference(a,b);BigInteger exact=BigInteger.Abs(Signed(a)-Signed(b));Assert.AreEqual(exact,ExactReference(difference));var ceil=(exact+((BigInteger.One<<20)-1))>>20;Assert.AreEqual(ceil,ExactReference(difference.CeilDivide2Pow20()));var expected=BigInteger.Max(0,exact-ceil-(BigInteger.One<<576));Assert.AreEqual(expected,ExactReference(NavExactCost.ConservativeHeuristicScale(difference)));}
            var box=new NavHeuristicBox(new NavObstacle(-1,-1,1,1));var origin=new NavPoint(0,0);var known=LayeredNavigationProvider.DistanceMetricContract;Assert.AreEqual(NavDistanceMetricContract.ManagedBinary64SqrtV1,known);
            bool Domain(double g,int p,NavDistanceMetricContract c)=>NavExactHeuristic.Domain(g,10,-10,21,p,origin,origin,box,c);
            Assert.True(Domain(1,1048576,known));Assert.False(Domain(NavExactHeuristic.Power(-201),1,known));Assert.False(Domain(1,1048577,known));Assert.False(Domain(1,1,NavDistanceMetricContract.Unknown));Assert.False(NavExactHeuristic.Domain(1,0,-10,21,1,origin,origin,box,known));
            var outside=new NavPoint(2,0);Assert.False(box.Contains(outside));box.Include(outside);Assert.True(box.Contains(outside));Assert.AreEqual(default(NavExactCost),NavExactHeuristic.Value(outside,outside,box));
            var e=new NavPoint(0,0);var tiny=new NavPoint(NavExactHeuristic.Power(-550),0);var x=new NavPoint(1,0);box=new NavHeuristicBox(new NavObstacle(-2,-2,2,2));
            double small=LayeredNavigationProvider.Distance(tiny,e);Assert.Zero(small);Assert.True(NavExactHeuristic.Value(tiny,e,box)<=NavExactCost.FromDouble(small));
            // Actual Distance implementation, not a copied norm: a tiny trace
            // segment + ordinary segment, zero portal, and both terminal links.
            var traceCost=NavExactCost.FromDouble(LayeredNavigationProvider.Distance(e,tiny)).Add(0).Add(LayeredNavigationProvider.Distance(tiny,x));
            Assert.True(NavExactHeuristic.Value(x,e,box)<=traceCost);Assert.True(NavExactHeuristic.Value(x,e,box)<=NavExactCost.FromDouble(LayeredNavigationProvider.Distance(x,tiny)).Add(LayeredNavigationProvider.Distance(tiny,e)));
            Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(typeof(NavDemandQueryWork)),2048);
        }
        [Test] public void ExactAstarActualCardinalPortalConsistencyAndIndependentMinimum()
        {
            var r=V6Request();Assert.AreEqual(LayeredNavigationProvider.DistanceMetricContract,r.MetricContract);var p=(PartitionNavigationProvider)r.Provider;var graph=LayeredNavigationGraphCompiler.Compile(p,r.Clearance,r.Profile,r.Limits);var w=new NavDemandQueryWork(r);
            try{
                for(int step=0;w.Status==NavSolveStatus.Pending;step++){Assert.Less(step,50000);V3Step(w);}Assert.AreEqual(NavSolveStatus.Ready,w.Status);Assert.True(w.HeuristicActive);var state=w.Save();var box=PreparedBox(w);Assert.AreEqual(671,state.Scalars.Length);
                Assert.True(box.Contains(r.Subscription.AssignedEndpoint.Position));foreach(var c in r.Region.Candidates)Assert.True(box.Contains(c.Position));foreach(var n in state.Locations.Where(n=>n.SurfaceId==r.Subscription.AssignedEndpoint.SurfaceId))Assert.True(box.Contains(n.Position));
                Assert.AreEqual(graph.Nodes.Count,state.Locations.Length);for(int n=0;n<graph.Nodes.Count;n++)Assert.AreEqual(graph.Nodes[n].Location,state.Locations[n]);
                var h=graph.Nodes.Select(n=>NavExactHeuristic.Value(n.Location.Position,r.Subscription.AssignedEndpoint.Position,box)).ToArray();int portal=0;
                foreach(var a in graph.Arcs){var from=graph.Nodes[a.From].Location;var to=graph.Nodes[a.To].Location;Assert.True(from.Position.X==to.Position.X||from.Position.Z==to.Position.Z);Assert.True(h[a.From]<=NavExactCost.FromDouble(a.Cost).Add(h[a.To]),"actual arc "+a.From+"->"+a.To);if(a.Trace!=null)foreach(var leg in a.Trace)if(leg.Kind==NavTypedLegKind.Portal){portal++;Assert.Zero(leg.Cost);Assert.AreEqual(leg.From.Position,leg.To.Position);}}
                Assert.Greater(portal,0);var d=new BigInteger?[graph.Nodes.Count];for(int n=0;n<d.Length;n++)if(p.CanSweep(r.Subscription.Origin,graph.Nodes[n].Location,r.Clearance))d[n]=ExactReference(LayeredNavigationProvider.Distance(r.Subscription.Origin.Position,graph.Nodes[n].Location.Position)*p.SurfaceCost(r.Subscription.Origin.SurfaceId));
                for(int round=0;round<d.Length;round++){bool changed=false;foreach(var a in graph.Arcs){if(!d[a.From].HasValue)continue;var value=d[a.From].Value+ExactReference(a.Cost);if(!d[a.To].HasValue||value<d[a.To].Value){d[a.To]=value;changed=true;}}if(!changed)break;}
                BigInteger? best=null;int terminal=0;for(int n=0;n<d.Length;n++)foreach(var c in r.Region.Candidates)if(p.CanSweep(graph.Nodes[n].Location,c,r.Clearance)&&p.CanSweep(c,r.Subscription.AssignedEndpoint,r.Clearance)){
                    double tail=LayeredNavigationProvider.Distance(graph.Nodes[n].Location.Position,c.Position)*p.SurfaceCost(c.SurfaceId),egress=LayeredNavigationProvider.Distance(c.Position,r.Subscription.AssignedEndpoint.Position)*p.SurfaceCost(c.SurfaceId);Assert.True(h[n]<=NavExactCost.FromDouble(tail).Add(egress));terminal++;if(!d[n].HasValue)continue;var total=d[n].Value+ExactReference(tail)+ExactReference(egress);if(!best.HasValue||total<best.Value)best=total;}
                Assert.Greater(terminal,0);Assert.True(best.HasValue);Assert.AreEqual(best.Value,ExactReference(w.ExactObjective));Assert.AreEqual(r.Subscription.AssignedEndpoint,w.Route.Legs.Last().To);Assert.AreEqual(w.Route.Legs.Aggregate(0.0,(cost,leg)=>cost+leg.Cost),w.Route.Cost);
                var restored=NavDemandQueryWork.Restore(r,state);try{Assert.AreEqual(w.ExactObjective,restored.ExactObjective);Assert.True(restored.HeuristicActive);}finally{restored.ReleaseOwner();}
            }finally{w.ReleaseOwner();graph.ReleaseOwner();}
        }
        [Test] public void ExactAstarPreparationUsesPaidIndexAndExhaustiveSharedBucket()
        {
            var r=V6Request();var scheduler=new LayeredNavigationScheduler();try{
                Assert.True(scheduler.Submit(r));scheduler.Advance(0,new NavEpochQuota(10000,0,10000,0,0),_=>true);var initial=scheduler.Save().Jobs.Single().Demand;Assert.AreEqual(0,BitConverter.ToInt32(initial.Scalars,617));long graph=scheduler.Counters.PhysicalGraphWork;
                Assert.Zero(scheduler.Advance(0,new NavEpochQuota(10000,0,10000,10000,0),_=>true));Assert.AreEqual(graph,scheduler.Counters.PhysicalGraphWork);Assert.AreEqual(0,BitConverter.ToInt32(scheduler.Save().Jobs.Single().Demand.Scalars,617));
                scheduler.Advance(1,new NavEpochQuota(0,0,0,1,0),_=>true);Assert.AreEqual(1,BitConverter.ToInt32(scheduler.Save().Jobs.Single().Demand.Scalars,617));Assert.Zero(scheduler.RemainingQuota.Bookkeeping);
            }finally{scheduler.ReleaseStorage();}
            var w=new NavDemandQueryWork(r);NavDemandQueryCatalog catalog=null;NavDemandQueryWork sibling=null;
            try{
                for(int step=0;w.Status==NavSolveStatus.Pending;step++){Assert.Less(step,50000);V3Step(w);}var saved=w.Save();catalog=w.ExportCatalog(null);Assert.NotNull(catalog);sibling=new NavDemandQueryWork(r,shared:catalog);Assert.False(sibling.HeuristicActive);int paid=0;
                while(!sibling.HeuristicActive){Assert.Less(paid,50000);Assert.True(sibling.CanBookkeeping);sibling.StepBookkeeping();paid++;}
                int count=saved.Locations.Count(n=>n.SurfaceId==r.Subscription.AssignedEndpoint.SurfaceId);Assert.AreEqual(count+r.Region.Candidates.Count+2,paid);Assert.True(PreparedBox(w).Equals(PreparedBox(sibling)));
                var logical=NavDemandQueryWork.Restore(r,sibling.Save());try{Assert.True(logical.HeuristicActive);Assert.True(PreparedBox(logical).Equals(PreparedBox(sibling)));}finally{logical.ReleaseOwner();}
            }finally{sibling?.ReleaseOwner();catalog?.ReleaseOwner();w.ReleaseOwner();}
        }
    }
}

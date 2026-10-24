using System;
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static BigInteger ExactReference(double value)
        {
            ulong raw=unchecked((ulong)BitConverter.DoubleToInt64Bits(value));int e=(int)(raw>>52)&2047;ulong m=raw&0xfffffffffffffUL;if(e!=0)m|=1UL<<52;
            Assert.False(double.IsNaN(value)||double.IsInfinity(value)||value<0);return new BigInteger(m)<<(e==0?0:e-1);
        }
        private static BigInteger ExactReference(NavExactCost value){BigInteger n=0;for(int i=33;i>=0;i--)n=(n<<64)+value.Limb(i);return n;}
        [Test] public void ExactCostFullRangeArithmeticPreservesDyadicMinimum()
        {
            double tiny=BitConverter.Int64BitsToDouble(1),normal=BitConverter.Int64BitsToDouble(0x0010000000000000L);
            var values=new[]{0.0,-0.0,tiny,normal,0.8,1.0,1e16,double.MaxValue};
            foreach(double a in values)foreach(double b in values){var sum=NavExactCost.FromDouble(a).Add(b);Assert.AreEqual(ExactReference(a)+ExactReference(b),ExactReference(sum));using(var stream=new MemoryStream()){using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))sum.Write(w);stream.Position=0;using(var r=new BinaryReader(stream))Assert.AreEqual(sum,NavExactCost.Read(r));}}
            var large=NavExactCost.FromDouble(double.MaxValue);for(int n=0;n<31;n++)large=large.Add(large);Assert.AreEqual(ExactReference(double.MaxValue)<<31,ExactReference(large));
            var a1=NavExactCost.FromDouble(1e16).Add(1).Add(1);var a2=NavExactCost.FromDouble(1).Add(1).Add(1e16);Assert.AreEqual(a1,a2);
            Assert.True(NavExactCost.FromDouble(1).Add(tiny)>NavExactCost.FromDouble(1));Assert.True(NavExactCost.FromDouble(1)<NavExactCost.FromDouble(1).Add(tiny));
            Assert.AreEqual(default(NavExactCost),NavExactCost.FromDouble(-0.0));
            foreach(double invalid in new[]{-tiny,-1,double.NaN,double.PositiveInfinity,double.NegativeInfinity})Assert.Throws<ArgumentException>(()=>NavExactCost.FromDouble(invalid));
            // Atomic portal weight is its actual double fold; flattening must
            // remain a distinct public certificate, not change that weight.
            double portal=(1e16+1.0)+1.0;Assert.AreNotEqual(ExactReference(portal),ExactReference(1e16)+2);Assert.AreEqual(NavExactCost.FromDouble(portal),NavExactCost.FromDouble(0).Add(portal));
            var tie1=new NavExactQueueEntry{Node=1,Cost=a1};var tie2=new NavExactQueueEntry{Node=2,Cost=a2};Assert.Less(tie1.CompareTo(tie2),0);
            Assert.AreEqual(272,NavStorageLayout.ValueBytes(typeof(NavExactCost)));Assert.AreEqual(280,NavStorageLayout.ValueBytes(typeof(NavExactQueueEntry)));Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(typeof(NavDemandQueryWork)),2048);
        }
        private static NavScheduledRequest V5Request(){var r=V3Request();return new NavScheduledRequest(r.Provider,r.Clearance,r.Profile,r.Limits,r.Region,r.Subscription,r.MaxLabels,query:true,demandQuery:true,demandQueryV2:true,demandQueryV3:true,demandQueryV5:true);}
        [Test] public void ExactDemandSmallActualWeightsMatchIndependentIntegerReference()
        {
            var r=V5Request();Assert.True(r.DemandQueryV5);Assert.False(r.DemandQueryV4);var p=(PartitionNavigationProvider)r.Provider;var graph=LayeredNavigationGraphCompiler.Compile(p,r.Clearance,r.Profile,r.Limits);var w=new NavDemandQueryWork(r);
            try{
                for(int step=0;w.Status==NavSolveStatus.Pending;step++){Assert.Less(step,50000);V3Step(w);}Assert.AreEqual(NavSolveStatus.Ready,w.Status);var state=w.Save();
                var d=new BigInteger?[graph.Nodes.Count];for(int n=0;n<d.Length;n++)if(p.CanSweep(r.Subscription.Origin,graph.Nodes[n].Location,r.Clearance))d[n]=ExactReference(LayeredNavigationProvider.Distance(r.Subscription.Origin.Position,graph.Nodes[n].Location.Position)*p.SurfaceCost(r.Subscription.Origin.SurfaceId));
                for(int round=0;round<d.Length;round++){bool changed=false;foreach(var a in graph.Arcs){if(!d[a.From].HasValue)continue;var value=d[a.From].Value+ExactReference(a.Cost);if(!d[a.To].HasValue||value<d[a.To].Value){d[a.To]=value;changed=true;}}if(!changed)break;}
                BigInteger? best=null;for(int n=0;n<d.Length;n++)if(d[n].HasValue)foreach(var candidate in r.Region.Candidates)if(p.CanSweep(graph.Nodes[n].Location,candidate,r.Clearance)&&p.CanSweep(candidate,r.Subscription.AssignedEndpoint,r.Clearance)){
                    var value=d[n].Value+ExactReference(LayeredNavigationProvider.Distance(graph.Nodes[n].Location.Position,candidate.Position)*p.SurfaceCost(candidate.SurfaceId))+ExactReference(LayeredNavigationProvider.Distance(candidate.Position,r.Subscription.AssignedEndpoint.Position)*p.SurfaceCost(candidate.SurfaceId));if(!best.HasValue||value<best.Value)best=value;}
                Assert.True(best.HasValue);Assert.AreEqual(best.Value,ExactReference(w.ExactObjective));Assert.AreEqual(r.Subscription.AssignedEndpoint,w.Route.Legs.Last().To);Assert.AreEqual(w.Route.Legs.Aggregate(0.0,(cost,leg)=>cost+leg.Cost),w.Route.Cost);
                var restored=NavDemandQueryWork.Restore(r,state);try{Assert.AreEqual(w.ExactObjective,restored.ExactObjective);}finally{restored.ReleaseOwner();}
            }finally{w.ReleaseOwner();graph.ReleaseOwner();}
        }
    }
}

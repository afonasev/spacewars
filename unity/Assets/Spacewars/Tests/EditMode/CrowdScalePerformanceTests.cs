using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdScalePerformanceTests
    {
        private static NavMotionBody[] NeighborCohorts(int count=400)
        {
            var bodies=new NavMotionBody[count];
            for(int i=0;i<count;i++){
                int cohort=i/50,local=i%50;double angle=cohort*Math.PI/4;
                bodies[i]=new NavMotionBody{Id=i+1,Position=new NavPoint(Math.Cos(angle)*88+(local%7)*2.2,Math.Sin(angle)*88+(local/7)*2.2),
                    Radius=.72,Speed=4,TurnSpeed=4.5,Potential=true};
            }return bodies;
        }
        [Test] public void ComponentNeighborsEvaluateEachFrozenPairOnlyOnce()
        {
            const int count=400;var bodies=NeighborCohorts(count);
            var neighbors=new int[count*NavCrowdMotion.NeighborCapacity];var counts=new int[count];var labels=new int[count];
            try{
                NavCrowdMotion.CountNeighborPairsForQa=true;NavCrowdMotion.NeighborPairEvaluationsForQa=0;
                NavCrowdMotion.Neighbors(bodies,neighbors,counts,PlayableProfile.Default.Navigation,1d/30,labels);
                long actual=NavCrowdMotion.NeighborPairEvaluationsForQa;
                TestContext.Out.WriteLine("NEIGHBOR_PAIR_CONTROL count="+count+" actual="+actual+" unique="+(count*(count-1)/2));
                Assert.AreEqual(count*(count-1)/2,actual,"Each symmetric frozen-body pair should be evaluated once; no cap or predicate change");
            }finally{NavCrowdMotion.CountNeighborPairsForQa=false;NavCrowdMotion.NeighborPairEvaluationsForQa=0;}
        }
        [TestCase(2,false)] [TestCase(1,false)] [TestCase(8,false)] [TestCase(32,false)]
        [TestCase(2,true)] [TestCase(1,true)] [TestCase(8,true)] [TestCase(32,true)]
        public void ComponentNeighborsKeepOriginalDirectedRowsAndComponentLabels(int cap,bool reverse)
        {
            var p=NavigationProfile.Default;
            var profile=new NavigationProfile(p.ProfileId,p.Revision,p.Radius,p.TankSpeed,p.TankTurnSpeed,p.GridCell,p.ArrivalTolerance,p.LocalDetourDistance,p.ForwardAlignmentAngle,p.CameraOrthoSize,p.CameraHeight,p.CameraOffsetZ,p.RepairRetrySeconds,p.ArrivalSlotSpacing,p.BlockedTimeoutSeconds,p.LocalGridCell,p.CrowdHeadingIntervals,p.CrowdSpeedBands,p.CrowdLookaheadSeconds,cap);
            var bodies=NeighborCohorts();if(reverse)Array.Reverse(bodies);
            // Mix active, receiver, held-like, touching, disconnected and unusual
            // magnitudes. Predicate semantics and row storage must remain exact.
            for(int i=0;i<bodies.Length;i++){
                bodies[i].Active=i%3==0;bodies[i].Potential=i%3==1;
                if(i%17==0)bodies[i].Speed=0;
            }
            bodies[7].Position=bodies[6].Position;bodies[9].Speed=double.PositiveInfinity;
            bodies[10].Position=new NavPoint(double.NaN,0);bodies[11].Radius=double.NaN;
            for(int pass=0;pass<2;pass++){
                int n=bodies.Length;var reference=new int[n*NavCrowdMotion.NeighborCapacity];var actual=new int[reference.Length];
                for(int i=0;i<reference.Length;i++)reference[i]=actual[i]=-12345;
                var expectedCounts=new int[n];var actualCounts=new int[n];var expectedLabels=pass==0?new int[n]:null;var actualLabels=pass==0?new int[n]:null;
                try{
                    NavCrowdMotion.ForceDirectedNeighborsForQa=true;
                    NavCrowdMotion.Neighbors(bodies,reference,expectedCounts,profile,1d/30,expectedLabels);
                    NavCrowdMotion.ForceDirectedNeighborsForQa=false;
                    NavCrowdMotion.Neighbors(bodies,actual,actualCounts,profile,1d/30,actualLabels);
                }finally{NavCrowdMotion.ForceDirectedNeighborsForQa=false;}
                CollectionAssert.AreEqual(reference,actual,"All stored and unused peer slots retain exact original bytes");
                CollectionAssert.AreEqual(expectedCounts,actualCounts,"Cap and overflow remain exact");
                if(pass==0)CollectionAssert.AreEqual(expectedLabels,actualLabels,"Component representatives retain original identity");
            }
        }
        [Test,Explicit("Fixed-input CPU benchmark; run alone through the exclusive Unity runner")]
        public void EightDenseCohortsPreserveExactNeighborAndSpeedOutput()
        {
            const int count=400,repeats=12;const double dt=1d/30;
            var profile=PlayableProfile.Default.Navigation;
            var source=new NavMotionBody[count];
            for(int i=0;i<count;i++){
                int cohort=i/50,local=i%50;double angle=cohort*Math.PI/4;
                source[i]=new NavMotionBody{Id=i+1,Position=new NavPoint(Math.Cos(angle)*88+(local%7)*2.2,Math.Sin(angle)*88+(local/7)*2.2),
                    Radius=.72,Speed=4,TurnSpeed=4.5,Potential=true};
            }
            var neighbors=new int[count*NavCrowdMotion.NeighborCapacity];var counts=new int[count];var labels=new int[count];var speeds=new double[count];
            byte[] expected=null;long ticks=0,allocated=0;
            for(int repeat=0;repeat<repeats+1;repeat++){
                var bodies=(NavMotionBody[])source.Clone();Array.Clear(neighbors,0,neighbors.Length);
                long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,dt,labels);
                NavCrowdMotion.ConstrainOverflowComponents(bodies,neighbors,counts,labels,speeds,profile,dt);
                long elapsed=Stopwatch.GetTimestamp()-start,used=GC.GetAllocatedBytesForCurrentThread()-bytes;
                if(repeat>0){ticks+=elapsed;allocated+=used;}
                using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
                    for(int row=0;row<count;row++){
                        writer.Write(bodies[row].Id);writer.Write(bodies[row].Position.X);writer.Write(bodies[row].Position.Z);
                        writer.Write(bodies[row].Speed);writer.Write(counts[row]);writer.Write(labels[row]);
                        for(int peer=0;peer<NavCrowdMotion.NeighborCapacity;peer++)writer.Write(neighbors[row*NavCrowdMotion.NeighborCapacity+peer]);
                    }
                    var bytesOut=stream.ToArray();
                    if(expected==null)expected=bytesOut;else CollectionAssert.AreEqual(expected,bytesOut,"Repeated fixed inputs must produce exactly the same output bytes");
                }
            }
            using(var sha=SHA256.Create()){var report="CROWD_SCALE_CONTROL count="+count+" repeats="+repeats+
                " total_ms="+(ticks*1000d/Stopwatch.Frequency).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" allocated="+allocated+
                " sha256="+BitConverter.ToString(sha.ComputeHash(expected)).Replace("-","").ToLowerInvariant();
                TestContext.Out.WriteLine(report);
                string output=Environment.GetEnvironmentVariable("SPACEWARS_CROWD_SCALE_RESULT");if(output!=null)File.WriteAllText(output,report+"\n");
            }
        }
    }
}

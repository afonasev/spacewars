using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationRectangularExclusionCertificateTests
    {
        private static NavExclusionInput Input(System.Collections.Generic.IReadOnlyList<NavObstacle> obstacles,
            long min,long max,double grid,long maxSamples,long maxNodes,long maxArcs,long maxLabels,
            string provider="provider-v1",string geometry="geometry-v1") =>
            new NavExclusionInput(obstacles,provider,geometry,grid,min,max,min,max,checked((max-min+1)*(max-min+1)),
                maxSamples,maxNodes,maxArcs,maxLabels);

        private static NavExclusionProgress Run(in NavExclusionInput input,ref NavExclusionCursor cursor,int budget,
            out int paid,out NavExclusionCertificate certificate)
        {
            int total=0;NavExclusionProgress result;
            do { result=NavigationRectangularExclusionCertificate.Step(input,ref cursor,budget,out int used,out certificate);total+=used; }
            while(result==NavExclusionProgress.Pending);
            paid=total;return result;
        }

        [Test]
        public void CampaignBottomRectangleFromAuthoredSolidsCertifiesConservativeCapacity()
        {
            var map=new CampaignOpeningMap();
            int bottom=map.Solids.Select((obstacle,index)=>(obstacle,index)).Single(x=>x.obstacle.Polygon==null&&
                x.obstacle.MinX==-240&&x.obstacle.MaxX==240&&x.obstacle.MinZ==-240&&x.obstacle.MaxZ==-96).index;
            var input=new NavExclusionInput(map.Solids,"campaign-provider:source","campaign-geometry:source",.8,
                -300,300,-300,300,361201,361201,300000,1200000,300000);
            var cursor=NavigationRectangularExclusionCertificate.Start(input);
            var result=Run(input,ref cursor,64,out int paid,out var certificate);
            Assert.AreEqual(NavExclusionProgress.Certified,result);Assert.AreEqual(bottom,certificate.ObstacleIndex);
            Assert.AreEqual(108781,certificate.ExcludedCount);Assert.AreEqual(252420,certificate.RemainingCount);
            Assert.LessOrEqual(certificate.RemainingCount,input.MaxNodes);Assert.LessOrEqual(4*certificate.RemainingCount,input.MaxArcs);
            Assert.LessOrEqual(certificate.RemainingCount,input.MaxLabels);Assert.Greater(paid,0);
            Assert.True(NavigationRectangularExclusionCertificate.Validate(input,cursor,certificate));
        }

        [Test]
        public void BoundariesAdjacentBinary64AndEmptyIntersectionUseActualProducts()
        {
            var exact=Input(new[]{new NavObstacle(-.2,-.2,.2,.2)},-4,4,.1,81,72,288,72);
            var cursor=NavigationRectangularExclusionCertificate.Start(exact);
            Assert.AreEqual(NavExclusionProgress.Certified,Run(exact,ref cursor,32,out _,out var cert));
            Assert.AreEqual(25,cert.ExcludedCount);Assert.AreEqual(-2,cert.MinXIndex);Assert.AreEqual(2,cert.MaxXIndex);

            double aboveNegative=BitIncrement(-.2),belowPositive=BitDecrement(.2);
            var adjacent=Input(new[]{new NavObstacle(aboveNegative,aboveNegative,belowPositive,belowPositive)},-4,4,.1,81,72,288,72);
            cursor=NavigationRectangularExclusionCertificate.Start(adjacent);
            Assert.AreEqual(NavExclusionProgress.Certified,Run(adjacent,ref cursor,32,out _,out cert));
            Assert.AreEqual(9,cert.ExcludedCount);Assert.AreEqual(-1,cert.MinXIndex);Assert.AreEqual(1,cert.MaxXIndex);

            var empty=Input(new[]{new NavObstacle(10,10,11,11)},-2,2,1,25,0,0,0);
            cursor=NavigationRectangularExclusionCertificate.Start(empty);
            Assert.AreEqual(NavExclusionProgress.Insufficient,Run(empty,ref cursor,64,out _,out cert));
            Assert.AreEqual(default(NavExclusionCertificate),cert);Assert.True(NavigationRectangularExclusionCertificate.Validate(empty,cursor,cert));
            var emptyWithinCaps=Input(new[]{new NavObstacle(10,10,11,11)},-2,2,1,25,25,100,25);
            cursor=NavigationRectangularExclusionCertificate.Start(emptyWithinCaps);
            Assert.AreEqual(NavExclusionProgress.Certified,Run(emptyWithinCaps,ref cursor,64,out _,out cert));
            Assert.Zero(cert.ExcludedCount);Assert.AreEqual(25,cert.RemainingCount);
            Assert.True(NavigationRectangularExclusionCertificate.Validate(emptyWithinCaps,cursor,cert));
            var emptyInsideDomain=Input(new[]{new NavObstacle(.1,0,.2,2)},0,4,1,25,25,100,25);
            cursor=NavigationRectangularExclusionCertificate.Start(emptyInsideDomain);
            Assert.AreEqual(NavExclusionProgress.Certified,Run(emptyInsideDomain,ref cursor,64,out _,out cert));
            Assert.Zero(cert.ExcludedCount);Assert.AreEqual(1,cert.MinXIndex);Assert.AreEqual(0,cert.MaxXIndex);
            Assert.True(NavigationRectangularExclusionCertificate.Validate(emptyInsideDomain,cursor,cert));
        }

        [Test]
        public void OverlapsAreNeverSummedAndNonRectanglesAreSkippedInStableOrder()
        {
            var overlapping=Input(new[]{new NavObstacle(0,0,1,4),new NavObstacle(3,0,4,4)},0,4,1,25,10,40,10);
            var cursor=NavigationRectangularExclusionCertificate.Start(overlapping);
            Assert.AreEqual(NavExclusionProgress.Insufficient,Run(overlapping,ref cursor,128,out _,out var cert));
            Assert.AreEqual(default(NavExclusionCertificate),cert);

            var polygon=new NavObstacle(new NavPolygon(new[]{new NavPoint(-5,-5),new NavPoint(5,-5),new NavPoint(5,5),new NavPoint(-5,5)}));
            var circle=new NavObstacle(new NavPoint(0,0),8);
            var mixed=Input(new[]{polygon,circle,new NavObstacle(-1,-1,1,1)},-2,2,1,25,16,64,16);
            cursor=NavigationRectangularExclusionCertificate.Start(mixed);
            Assert.AreEqual(NavExclusionProgress.Certified,Run(mixed,ref cursor,32,out _,out cert));
            Assert.AreEqual(2,cert.ObstacleIndex);Assert.AreEqual(9,cert.ExcludedCount);
        }

        [Test]
        public void ZeroBudgetIsImmutableAndSplitRestoredCursorMatchesUninterruptedRun()
        {
            var input=Input(new[]{new NavObstacle(-1,-1,1,1),new NavObstacle(-3,-3,3,3)},-4,4,1,81,72,288,72);
            var cursor=NavigationRectangularExclusionCertificate.Start(input);var before=cursor;
            Assert.AreEqual(NavExclusionProgress.Pending,NavigationRectangularExclusionCertificate.Step(input,ref cursor,0,out int used,out _));
            Assert.Zero(used);Assert.AreEqual(before,cursor);
            var resumed=cursor;
            var first=NavigationRectangularExclusionCertificate.Step(input,ref resumed,1,out used,out _);
            Assert.AreEqual(NavExclusionProgress.Pending,first);Assert.AreEqual(1,used);
            var restored=resumed; // Plain fields are the complete continuation state.
            var split=Run(input,ref restored,1,out _,out var splitCertificate);
            var whole=Run(input,ref cursor,7,out _,out var wholeCertificate);
            Assert.AreEqual(whole,split);Assert.AreEqual(cursor,restored);
            Assert.AreEqual(wholeCertificate,splitCertificate);
            Assert.True(NavigationRectangularExclusionCertificate.Validate(input,restored,splitCertificate));
            Assert.AreEqual(NavExclusionProgress.Certified,NavigationRectangularExclusionCertificate.Step(input,ref restored,2,out used,out var repeated));
            Assert.Zero(used);Assert.AreEqual(splitCertificate,repeated);Assert.AreEqual(cursor,restored);
        }

        [Test]
        public void EveryCapacityGateKeepsItsOriginalEqualityAndExceedance()
        {
            AssertCap(25,10,40,10,true); // nodes, arcs and labels exactly at limits
            AssertCap(25,9,40,10,false);
            AssertCap(25,10,39,10,false);
            AssertCap(25,10,40,9,false);
            var tooManySamples=Input(new[]{new NavObstacle(0,0,4,4)},0,4,1,24,25,100,25);
            var cursor=NavigationRectangularExclusionCertificate.Start(tooManySamples);
            Assert.AreEqual(NavExclusionProgress.Insufficient,NavigationRectangularExclusionCertificate.Step(tooManySamples,ref cursor,1,out int paid,out var cert));
            Assert.Zero(paid);Assert.AreEqual(default(NavExclusionCertificate),cert);
            Assert.True(NavigationRectangularExclusionCertificate.Validate(tooManySamples,cursor,cert));
        }

        [Test]
        public void RestoreValidationRejectsChangedIdentityBracketsBoundsAndCounts()
        {
            var input=Input(new[]{new NavObstacle(-1,-1,1,1)},-2,2,1,25,16,64,16);
            var cursor=NavigationRectangularExclusionCertificate.Start(input);
            Assert.AreEqual(NavExclusionProgress.Certified,Run(input,ref cursor,32,out _,out var cert));
            Assert.True(NavigationRectangularExclusionCertificate.Validate(input,cursor,cert));
            var changed=Input(input.Obstacles,-2,2,1,25,16,64,16,"provider-other");
            Assert.False(NavigationRectangularExclusionCertificate.Validate(changed,cursor,cert));
            var corrupt=cursor;corrupt.Low=1;Assert.False(NavigationRectangularExclusionCertificate.Validate(input,corrupt,cert));
            var badBounds=cert;badBounds.MaxX=2;Assert.False(NavigationRectangularExclusionCertificate.Validate(input,cursor,badBounds));
            var badCount=cert;badCount.ExcludedCount++;
            Assert.False(NavigationRectangularExclusionCertificate.Validate(input,cursor,badCount));
            var invalidCounts=new NavExclusionInput(input.Obstacles,"provider-v1","geometry-v1",1,-2,2,-2,2,24,25,25,100,25);
            Assert.Throws<ArgumentException>(()=>NavigationRectangularExclusionCertificate.Start(invalidCounts));
        }

        [Test]
        public void PendingRestoreChecksCompletedAxesActiveBracketsAndRectangularRanges()
        {
            var input=new NavExclusionInput(new[]{new NavObstacle(-1,-1,1,1)},"provider-v1","geometry-v1",1,
                -3,3,-2,2,35,35,26,104,26);
            var cursor=NavigationRectangularExclusionCertificate.Start(input);
            while(cursor.Stage!=NavExclusionStage.MaxZ)
                NavigationRectangularExclusionCertificate.Step(input,ref cursor,1,out _,out _);
            Assert.True(NavigationRectangularExclusionCertificate.Validate(input,cursor,default));
            var corrupt=cursor;corrupt.XMin++;
            Assert.False(NavigationRectangularExclusionCertificate.Validate(input,corrupt,default));
            corrupt=cursor;corrupt.Low=input.MaxZIndex+1;
            Assert.False(NavigationRectangularExclusionCertificate.Validate(input,corrupt,default));
            Assert.AreEqual(NavExclusionProgress.Certified,Run(input,ref cursor,32,out _,out var certificate));
            Assert.AreEqual(9,certificate.ExcludedCount);
            Assert.True(NavigationRectangularExclusionCertificate.Validate(input,cursor,certificate));
            var terminalCorrupt=cursor;terminalCorrupt.ZMax--;
            Assert.False(NavigationRectangularExclusionCertificate.Validate(input,terminalCorrupt,certificate));

            var nonFinite=new NavExclusionInput(new[]{new NavObstacle(0,0,1,1)},"provider-v1","geometry-v1",double.MaxValue,
                0,2,0,2,9,9,9,36,9);
            Assert.Throws<ArgumentException>(()=>NavigationRectangularExclusionCertificate.Start(nonFinite));
            var minXUnderflow=new NavExclusionInput(new[]{new NavObstacle(0,0,1,1)},"provider-v1","geometry-v1",1,
                long.MinValue,long.MinValue+2,0,2,9,9,9,36,9);
            Assert.Throws<ArgumentException>(()=>NavigationRectangularExclusionCertificate.Start(minXUnderflow));
            var minZUnderflow=new NavExclusionInput(new[]{new NavObstacle(0,0,1,1)},"provider-v1","geometry-v1",1,
                0,2,long.MinValue,long.MinValue+2,9,9,9,36,9);
            Assert.Throws<ArgumentException>(()=>NavigationRectangularExclusionCertificate.Start(minZUnderflow));
        }

        private static void AssertCap(long samples,long nodes,long arcs,long labels,bool expected)
        {
            var input=Input(new[]{new NavObstacle(0,0,2,4)},0,4,1,samples,nodes,arcs,labels);
            var cursor=NavigationRectangularExclusionCertificate.Start(input);
            var result=Run(input,ref cursor,32,out _,out var certificate);
            Assert.AreEqual(expected?NavExclusionProgress.Certified:NavExclusionProgress.Insufficient,result);
            Assert.AreEqual(expected,certificate.ProviderIdentity!=null);
        }
        private static double BitIncrement(double value)=>BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value)+(value<0?-1:1));
        private static double BitDecrement(double value)=>BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value)+(value>0?-1:1));
    }
}

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using System.Globalization;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableBallisticsTests
    {
        [Serializable]private class Case{public double arc,duration,midY,progress,x,y;public bool wall;public int[] hits;}
        private static BallisticBody Body(int id,double x,double z,PlayableOwner owner=PlayableOwner.Enemy,bool building=false,double radius=1,double height=2)=>new BallisticBody(id,owner,new NavPoint(x,z),default,radius,height,100,building);

        [TestCase(0d)][TestCase(.09)][TestCase(2d)]
        public void TerrainBroadPhasePreservesEverySampleOnAuthoredMossAndBoundaryCases(double radius)
        {
            var map=new MossHighlandMap();var random=new Random(1564176781);
            var solids=map.Solids.Concat(new[]{new NavObstacle(-1,-3,1,3),new NavObstacle(new NavPoint(5,5),2),
                new NavObstacle(new NavPolygon(new[]{new NavPoint(-7,-5),new NavPoint(-3,-4),new NavPoint(-5,0)}),1,4)}).ToArray();
            bool Full(BallisticPoint a,BallisticPoint b){
                double distance=Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Z-a.Z,2)+Math.Pow(b.Y-a.Y,2));int steps=Math.Max(1,(int)Math.Ceiling(distance/.025));
                for(int i=0;i<=steps;i++){double t=(double)i/steps;var point=new BallisticPoint(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t);
                    if(point.Y-radius<map.SurfaceHeight(point.Ground)-1e-5)return false;
                    foreach(var solid in solids)if(point.Y+radius>=solid.Bottom&&point.Y-radius<=(double.IsNaN(solid.Top)?3:solid.Top)&&
                        point.X+radius>=solid.MinX&&point.X-radius<=solid.MaxX&&point.Z+radius>=solid.MinZ&&point.Z-radius<=solid.MaxZ&&solid.DistanceSquared(point.Ground)<=radius*radius)return false;
                }return true;
            }
            for(int i=0;i<120;i++){
                var a=new BallisticPoint(random.NextDouble()*120-60,random.NextDouble()*6,random.NextDouble()*120-60);
                var b=i%10==0?a:new BallisticPoint(random.NextDouble()*120-60,random.NextDouble()*6,random.NextDouble()*120-60);
                Assert.AreEqual(Full(a,b),PlayableBallistics.TerrainLineClear(a,b,radius,solids,3,map.SurfaceHeight),"ray "+i);
            }
            foreach(double edge in new[]{-1-radius,-1-radius-1e-10,1+radius,1+radius+1e-10}){
                var a=new BallisticPoint(edge,2,-3);var b=new BallisticPoint(edge,2,3);
                Assert.AreEqual(Full(a,b),PlayableBallistics.TerrainLineClear(a,b,radius,solids,3,map.SurfaceHeight),"tangent "+edge);
            }
        }

        [Test]public void BuildingsInterceptAndShieldWhileUnitsDoNot()
        {
            var wall=Body(3,5,0,building:true,radius:.5,height:6);var target=Body(4,6,0);var friend=Body(5,4,1,PlayableOwner.Player);
            var bodies=new[]{wall,target,friend};var f=new BallisticFlight(new BallisticPoint(0,1.15,0),new BallisticPoint(10,0,0),4,12);var hit=PlayableBallistics.FirstContact(f,.09,Array.Empty<NavObstacle>(),1.74,bodies,1).Value;
            Assert.AreEqual(3,hit.BuildingId);Assert.Less(hit.Progress,.5);
            var impact=new BallisticPoint(4,0,0);Assert.False(PlayableBallistics.BlastHits(impact,4,target,Array.Empty<NavObstacle>(),1.74,bodies));Assert.True(PlayableBallistics.BlastHits(impact,4,friend,Array.Empty<NavObstacle>(),1.74,bodies));
            var units=new[]{Body(3,5,0),target,friend};Assert.True(PlayableBallistics.BlastHits(impact,4,target,Array.Empty<NavObstacle>(),1.74,units));
        }
        [Test]public void RuntimeRechecksNewBuildingAndFootprintEdge()
        {
            var f=new BallisticFlight(new BallisticPoint(0,1.15,0),new BallisticPoint(10,0,0),4,12);Assert.Null(PlayableBallistics.FirstContact(f,.09,Array.Empty<NavObstacle>(),1.74,Array.Empty<BallisticBody>(),1,0,.3));
            var blocker=Body(9,5,0,building:true,height:8);Assert.NotNull(PlayableBallistics.FirstContact(f,.09,Array.Empty<NavObstacle>(),1.74,new[]{blocker},1,.3,.6));
            var edge=Body(3,2.4,0);Assert.True(PlayableBallistics.BlastHits(new BallisticPoint(0,0,0),1.4,edge,Array.Empty<NavObstacle>(),1.74,new[]{edge}));
        }
        [Test]public void TargetingUsesLexicalTiePositiveScoreAndMovingFriendlyForecast()
        {
            var own=Body(1,0,0,PlayableOwner.Player);var a=Body(10,10,3);var b=Body(2,10,-3);
            int Pick(BallisticBody[] bodies)=>PlayableBallistics.BestTarget(default,PlayableOwner.Player,13,1.15,4,12,.09,1.4,10,2,Array.Empty<NavObstacle>(),1.74,bodies);
            Assert.AreEqual(10,Pick(new[]{own,b,a}));Assert.AreEqual(10,Pick(new[]{own,a,b}));
            var ally=Body(7,10,3,PlayableOwner.Player);Assert.AreEqual(2,Pick(new[]{own,a,b,ally}));Assert.Zero(Pick(new[]{own,a,ally}));
            double duration=new BallisticFlight(new BallisticPoint(0,1.15,0),new BallisticPoint(10,0,0),4,12).Duration;
            var moving=new BallisticBody(7,PlayableOwner.Player,new NavPoint(10,4),new NavPoint(0,-4/duration),1,2,100,false);
            Assert.Zero(Pick(new[]{own,Body(2,10,0),moving}));
        }
    }
}

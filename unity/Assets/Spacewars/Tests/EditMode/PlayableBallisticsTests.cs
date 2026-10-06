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

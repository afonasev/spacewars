using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class RecastReceiverPaceTests
    {
        [Test] public void VoluntaryWaitRemainsAReachableLegalReceiverWhileLegacyZeroCapDoesNotMove()
        {
            var g=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var profile=NavigationProfile.Default;
            var crowd=new NavCrowd(g,profile);crowd.Add(1,new NavPoint(0,0),.72,2,4.5,canYield:true);crowd.Add(2,new NavPoint(1.5,0),.72,2,4.5,canYield:true);
            var bodies=new[]{new NavMotionBody{Id=1,Position=new NavPoint(0,0),Target=new NavPoint(10,0),Radius=.72,Speed=2,TurnSpeed=4.5,Active=true},new NavMotionBody{Id=2,Position=new NavPoint(1.5,0),Target=new NavPoint(1.5,0),Radius=.72,Speed=2,TurnSpeed=4.5,Potential=true}};
            var proposals=new NavMotionProposal[2];var counts=new int[2];var neighbors=new int[2*NavCrowdMotion.NeighborCapacity];
            NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,1d/30);Assert.AreEqual(1,counts[0]);Assert.AreEqual(1,counts[1]);
            typeof(NavCrowd).GetMethod("ActivateYieldChains",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(crowd,new object[]{1d/30,bodies,proposals,new int[2],new int[2],counts});
            Assert.True(bodies[1].Yielding);Assert.Greater(proposals[1].Position.X,1.5);Assert.LessOrEqual(proposals[1].Velocity.X,2);
            bodies[1].TerminalSeed=true;bodies[0].YieldLabel=bodies[1].YieldLabel=0;bodies[1].Yielding=false;proposals=new NavMotionProposal[2];
            typeof(NavCrowd).GetMethod("ActivateYieldChains",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(crowd,new object[]{1d/30,bodies,proposals,new int[2],new int[2],counts});
            Assert.False(bodies[1].Yielding,"Assigned-slot protection is still enforced.");
        }
        [Test] public void OwnSelectionKeepsVoluntaryCapWhileReachSpeedStaysAtActualPace()
        {
            var b=new NavMotionBody{Id=1,Position=new NavPoint(0,0),Target=new NavPoint(10,0),Radius=.72,Speed=2,OwnPaceCap=.5,TurnSpeed=4.5,Active=true};
            var proposal=NavCrowdMotion.Select(new[]{b},0,new int[NavCrowdMotion.NeighborCapacity],0,NavigationProfile.Default,1d/30,new Clear(),null);
            Assert.LessOrEqual(NavCrowdMotion.Length(proposal.Velocity),.5);Assert.AreEqual(2,b.Speed);
            b.Yielding=true;Assert.AreEqual(2,b.OwnSpeed);
        }
        private sealed class Clear:INavMotionClearance{public bool ClearMotion(int actor,NavPoint end)=>true;}
    }
}

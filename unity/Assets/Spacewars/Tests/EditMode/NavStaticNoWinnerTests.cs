using System;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavStaticNoWinnerTests
    {
        private sealed class GeometryClearance : INavMotionClearance
        {
            private readonly NavGeometry geometry;private readonly NavPoint origin;private readonly double radius;
            internal GeometryClearance(NavGeometry geometry,NavPoint origin,double radius){this.geometry=geometry;this.origin=origin;this.radius=radius;}
            public bool ClearMotion(int actor,NavPoint end)=>geometry.SegmentFree(origin,end,radius);
        }
        private sealed class SingleEndpointClearance : INavMotionClearance
        {
            private readonly NavPoint available;
            internal SingleEndpointClearance(NavPoint available){this.available=available;}
            public bool ClearMotion(int actor,NavPoint end)=>(end.X==0&&end.Z==0)||Math.Abs(end.X-available.X)<1e-12&&Math.Abs(end.Z-available.Z)<1e-12;
        }
        private static NavMotionBody Body(int id,NavPoint position,NavPoint target,double heading,bool active=true)
            =>new NavMotionBody{Id=id,Position=position,Target=target,Heading=heading,Radius=.72,Speed=4,TurnSpeed=4.5,Active=active};

        [Test]
        public void StaticNoWinnerRequiresUnchangedHeadingNoOverflowAndNoActiveNeighbor()
        {
            const double dt=1d/30d;var profile=NavigationProfile.Default;var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var body=Body(1,new NavPoint(0,0),new NavPoint(4,0),0);var bodies=new[]{body};var neighbors=new int[NavCrowdMotion.NeighborCapacity];var clearance=new GeometryClearance(geometry,body.Position,body.Radius);
            var proposal=NavCrowdMotion.Select(bodies,0,neighbors,0,profile,dt,new SingleEndpointClearance(new NavPoint(-4*dt,0)));
            Assert.False(proposal.Blocked);Assert.False(proposal.Overflow);Assert.True(proposal.StaticNoWinner);Assert.AreEqual(body.Position,proposal.Position);
            var proposals=new[]{proposal};NavCrowdMotion.ResolveJoint(bodies,proposals,dt);
            Assert.True(proposals[0].StaticNoWinner,"joint resolution must preserve Select's no-winner cause");

            body.Heading=-Math.PI/2;bodies[0]=body;
            proposal=NavCrowdMotion.Select(bodies,0,neighbors,0,profile,dt,clearance);
            Assert.False(proposal.StaticNoWinner,"a bounded turn remains voluntary progress");

            body=Body(1,new NavPoint(0,0),new NavPoint(4,0),0);bodies=new[]{body,Body(2,new NavPoint(1.5,0),new NavPoint(2.5,0),0)};
            neighbors[0]=1;proposal=NavCrowdMotion.Select(bodies,0,neighbors,1,profile,dt,new SingleEndpointClearance(new NavPoint(-4*dt,0)));
            Assert.False(proposal.Blocked);Assert.False(proposal.StaticNoWinner,"an active peer moving away retains the existing no-winner wait lifecycle");Assert.AreEqual(body.Position,proposal.Position);Assert.AreEqual(0,NavCrowdMotion.Length(proposal.Velocity));Assert.AreEqual(body.Heading,proposal.Heading);

            proposal=NavCrowdMotion.Select(bodies,0,neighbors,-1,profile,dt,clearance);
            Assert.True(proposal.Overflow);Assert.False(proposal.StaticNoWinner);
            bodies[0]=Body(1,new NavPoint(0,0),new NavPoint(4,0),0,false);
            proposal=NavCrowdMotion.Select(bodies,0,neighbors,0,profile,dt,clearance);
            Assert.False(proposal.StaticNoWinner);

            var crossingBodies=new[]{Body(10,new NavPoint(-.75,0),new NavPoint(2,0),0),Body(11,new NavPoint(.75,0),new NavPoint(-2,0),Math.PI)};
            var crossing=new[]{new NavMotionProposal{Position=new NavPoint(-.75+4*dt,0),Velocity=new NavPoint(4,0),Heading=0},new NavMotionProposal{Position=new NavPoint(.75-4*dt,0),Velocity=new NavPoint(-4,0),Heading=Math.PI}};
            for(int i=0;i<crossing.Length;i++){Assert.True(crossingBodies[i].Active);Assert.LessOrEqual(NavCrowdMotion.Length(crossing[i].Velocity),crossingBodies[i].Speed);Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(crossing[i].Heading-crossingBodies[i].Heading)),crossingBodies[i].TurnSpeed*dt);}
            NavCrowdMotion.ResolveJoint(crossingBodies,crossing,dt);
            for(int i=0;i<crossing.Length;i++){Assert.AreEqual(crossingBodies[i].Position,crossing[i].Position);Assert.AreEqual(0,NavCrowdMotion.Length(crossing[i].Velocity));Assert.False(crossing[i].StaticNoWinner,"joint refusal must not manufacture a Select no-winner cause");}
        }
    }
}

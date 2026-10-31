using System;using System.Linq;using NUnit.Framework;using Spacewars.Runtime;using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class Oct28StaticArrivalSteeringTests
    {
        sealed class Lane:INavMotionClearance{public bool ClearMotion(int actor,NavPoint end)=>true;}
        static NavMotionBody Body(int id,double x,double z,double heading,double gx,double gz,bool active)=>new NavMotionBody{Id=id,Position=new NavPoint(x,z),Target=new NavPoint(gx,gz),Heading=heading,Radius=id%2==0?.58:.72,Speed=1.51229895253537,TurnSpeed=id%2==0?7.5:4.5,Active=active};
        [Test] public void ArrivedPeerBoundaryAllowsTranslationWithoutCrossingNorth()
        {
            // Saved slot-candidate tick757 positions/headings. Speed is reconstructed
            // from the common translated distance of seven movers at tick758.
            // Goals are the authored spiral slots; the stopped peers are genuinely arrived.
            var bodies=new[]{
                Body(6,1.2051431649387155E-15,16.68148148148147,1.5707963267948966,0.0,16.8,false),
                Body(8,-3.5916329276051568,20.26717272573153,1.633705332830022,-3.6,20.4,false),
                Body(11,-3.5921093191596487,16.682626122499713,1.6379222149969974,-3.6,16.8,false),
                Body(12,1.3798762988009483,20.479757223862283,1.5465661886974438,0.0,24.0,true),
                Body(14,-6.5231204880823679,19.704418482061715,1.7270869681816083,-7.2,24.0,true),
                Body(15,1.259943481615398,17.710070386293477,1.54135881033937,0.0,24.0,true),
                Body(17,-6.7417372426450592,17.633072720531143,1.7349279522821097,-7.2,20.4,true),
                Body(18,-2.417238885846718,17.824193781161021,1.6911997053819234,-3.6,27.6,true),
            };
            var p=PlayableProfile.Default.Navigation;const double dt=1d/30;var neighbors=new int[bodies.Length*32];var counts=new int[bodies.Length];
            NavCrowdMotion.Neighbors(bodies,neighbors,counts,p,dt);int row=Array.FindIndex(bodies,b=>b.Id==18);Assert.AreEqual(7,counts[row]);
            var lane=new Lane();var proposals=new NavMotionProposal[bodies.Length];
            for(int i=0;i<bodies.Length;i++)proposals[i]=NavCrowdMotion.Select(bodies,i,neighbors,counts[i],p,dt,lane,bodies[i].Target);
            Assert.Greater(NavCrowdMotion.Length(proposals[row].Velocity),0,"A stationary peer's safe angular boundary permits forward motion");
            Assert.Less(Math.Cos(proposals[row].Heading),0,"The selected correction must not cross north then turn back");
            NavCrowdMotion.ResolveJoint(bodies,proposals,dt);Assert.Greater(NavCrowdMotion.Length(proposals[row].Velocity),0);
            for(int i=0;i<bodies.Length;i++)for(int j=i+1;j<bodies.Length;j++){
                var separation=new NavPoint(bodies[i].Position.X-bodies[j].Position.X,bodies[i].Position.Z-bodies[j].Position.Z);
                var relative=new NavPoint(proposals[i].Velocity.X-proposals[j].Velocity.X,proposals[i].Velocity.Z-proposals[j].Velocity.Z);
                Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(separation,relative,dt),Math.Pow(bodies[i].Radius+bodies[j].Radius,2)-1e-9);
            }
        }
        [Test] public void MovingPeerConflictRetainsSampledChoice()
        {
            // Earlier ordinary tick89 moving pair. This correction must not revive
            // the rejected global refinement for moving reciprocal encounters.
            var b=new[]{new NavMotionBody{Id=6,Position=new NavPoint(-.297906166255243,-.90379652074288053),Target=new NavPoint(-3.6,20.4),Heading=1.7234456551901618,Radius=.58,Speed=.97413509276352939,TurnSpeed=7.5,Active=true},new NavMotionBody{Id=5,Position=new NavPoint(-1.8580983371045041,-.88562798918560026),Target=new NavPoint(0,24),Heading=1.4968572891369563,Radius=.72,Speed=.97413509276352939,TurnSpeed=4.5,Active=true}};
            var n=new int[64];var counts=new int[2];var p=PlayableProfile.Default.Navigation;NavCrowdMotion.Neighbors(b,n,counts,p,1d/30);
            var choice=NavCrowdMotion.Select(b,0,n,counts[0],p,1d/30,new Lane(),b[0].Target);
            Assert.AreEqual(1.4734456551901618,choice.Heading);Assert.AreEqual(0,NavCrowdMotion.Length(choice.Velocity));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdPassageTests
    {
        // Frozen actual-domain witness premises, not runtime balance defaults.
        internal const int RoutePreparationBound=4096,RouteBudget=8,MotionBound=2700;
        internal static OfflineMatchConfiguration Configuration()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=19092026};setup.InitializeParticipants();foreach(var p in setup.Participants)p.Human=true;
            var c=NativeLobbyMatch.Create(profile,setup);double lane=profile.Navigation.ArrivalSlotSpacing/2;
            var scenario=new[]{new OfflineScenarioUnit(c.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(-18,-lane),0),new OfflineScenarioUnit(c.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(-18,lane),0),new OfflineScenarioUnit(c.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(18,-lane),Math.PI),new OfflineScenarioUnit(c.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(18,lane),Math.PI)};
            var costs=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            return new OfflineMatchConfiguration(profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,c.Spectators.ToArray(),scenario,profile.AuthoredMap.Id,c.ScenarioBuildings.ToArray());
        }
        private static double SweptSeparationSquared(NavPoint relative,NavPoint velocity,double dt)
        {
            double denominator=velocity.X*velocity.X+velocity.Z*velocity.Z;
            double closestTime=denominator>0?Math.Min(dt,Math.Max(0,-(relative.X*velocity.X+relative.Z*velocity.Z)/denominator)):0;
            double x=relative.X+velocity.X*closestTime,z=relative.Z+velocity.Z*closestTime;return x*x+z*z;
        }
        private sealed class StationaryOnlyClearance : INavMotionClearance
        {public bool ClearMotion(int actor,NavPoint end)=>end.Equals(new NavPoint(0,0));}
        private sealed class RowClearance:INavMotionClearance
        {
            private readonly NavMotionBody[] bodies;private readonly NavGeometry geometry;
            internal RowClearance(NavMotionBody[] bodies,NavGeometry geometry){this.bodies=bodies;this.geometry=geometry;}
            public bool ClearMotion(int actor,NavPoint end)=>geometry.SegmentFree(bodies[actor].Position,end,bodies[actor].Radius)&&ThreeCrossingsMap.Default.SupportsSweep(bodies[actor].Position,end,bodies[actor].Radius);
        }
        private sealed class FlatRankingClearance : INavMotionClearance
        {
            private readonly NavMotionBody[] bodies;private readonly bool enclosure;
            private readonly NavGeometry geometry=new NavGeometry(40,Array.Empty<NavObstacle>(),1);
            internal FlatRankingClearance(NavMotionBody[] bodies,bool enclosure=false){this.bodies=bodies;this.enclosure=enclosure;}
            public bool ClearMotion(int actor,NavPoint end)=>geometry.SegmentFree(bodies[actor].Position,end,bodies[actor].Radius)&&(!enclosure||end.Equals(bodies[actor].Position));
        }
        private static NavMotionBody[] Ranking13Bodies(double speed)=>new[]{
            new NavMotionBody{Id=13,Position=new NavPoint(12.304031795721311,-10.12300989018299),Target=new NavPoint(12.50403179572131,-10.12300989018299),Heading=0,Radius=.72,Speed=speed,TurnSpeed=4.5,Active=true,OwnMoving=true,OwnOutcome=NavigationOutcome.Moving,OwnRouteIndex=1},
            new NavMotionBody{Id=11,Position=new NavPoint(8.482355971279183,-11.444809372763128),Radius=.72,Speed=2.412147760614382},
            new NavMotionBody{Id=12,Position=new NavPoint(11.937245840207348,-11.528266312410127),Radius=.72,Speed=2.412147760614382}};

        [TestCase(9.722995004166361e-7)] // Actual post-Q9 cap from the qualified adjacent blocked-onset frame.
        [TestCase(5e-7)][TestCase(1e-8)][TestCase(1e-6)][TestCase(1.01e-6)][TestCase(1d)][TestCase(4d)]
        public void PositiveComposedSpeedRanksFreeFullStepAboveWaitAtTinyAndNormalScales(double speed)
        {
            const double dt=1d/30;var bodies=Ranking13Bodies(speed);var original=bodies[0];var neighbors=new int[3*NavCrowdMotion.NeighborCapacity];neighbors[0]=1;neighbors[1]=2;
            var clearance=new FlatRankingClearance(bodies);var goal=new NavPoint(15.6,-11.4);
            var proposal=NavCrowdMotion.Select(bodies,0,neighbors,2,PlayableProfile.Default.Navigation,dt,clearance,goal);
            Assert.Greater(proposal.Position.X,original.Position.X,"a real free improving step must not lose to wait because squared speed is below a fixed absolute error tolerance");
            Assert.AreEqual(original.Position.Z,proposal.Position.Z);Assert.AreEqual(speed,proposal.Velocity.X);Assert.Zero(proposal.Velocity.Z);Assert.AreEqual(original.Heading,proposal.Heading);
            Assert.False(proposal.Blocked||proposal.StaticNoWinner||proposal.Overflow||proposal.Refuse);Assert.True(clearance.ClearMotion(0,proposal.Position));Assert.AreEqual(original,bodies[0],"ranking cannot boost or alter the composed input cap/body");
            Assert.LessOrEqual(NavCrowdMotion.Length(proposal.Velocity),speed);Assert.True(NavCrowdMotion.ImprovesGoal(original.Position,proposal.Position,goal));
            var proposals=new[]{proposal,new NavMotionProposal{Position=bodies[1].Position},new NavMotionProposal{Position=bodies[2].Position}};NavCrowdMotion.ResolveJoint(bodies,proposals,dt);Assert.AreEqual(proposal,proposals[0],"the actual full-dt peer sweep must retain this free proposal");
            for(int i=1;i<bodies.Length;i++)Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(new NavPoint(original.Position.X-bodies[i].Position.X,original.Position.Z-bodies[i].Position.Z),proposal.Velocity,dt),Math.Pow(original.Radius+bodies[i].Radius,2)-1e-9);
        }
        [TestCase(9.722995004166361e-7)][TestCase(4d)]
        public void ScaleCorrectRankingCannotTurnPhysicalEnclosureIntoVoluntaryWait(double speed)
        {
            var bodies=Ranking13Bodies(speed);var neighbors=new int[3*NavCrowdMotion.NeighborCapacity];neighbors[0]=1;neighbors[1]=2;
            var proposal=NavCrowdMotion.Select(bodies,0,neighbors,2,PlayableProfile.Default.Navigation,1d/30,new FlatRankingClearance(bodies,true));
            Assert.AreEqual(bodies[0].Position,proposal.Position);Assert.AreEqual(default(NavPoint),proposal.Velocity);Assert.True(proposal.Blocked);Assert.False(proposal.StaticNoWinner||proposal.Overflow);
        }
        [TestCase(0d)][TestCase(-1d)]
        public void NonPositiveComposedSpeedPreservesInactiveSelectDisposition(double speed)
        {
            var bodies=Ranking13Bodies(speed);var proposal=NavCrowdMotion.Select(bodies,0,new int[3*NavCrowdMotion.NeighborCapacity],0,PlayableProfile.Default.Navigation,1d/30,new FlatRankingClearance(bodies));
            Assert.AreEqual(bodies[0].Position,proposal.Position);Assert.AreEqual(default(NavPoint),proposal.Velocity);Assert.AreEqual(bodies[0].Heading,proposal.Heading);Assert.False(proposal.Blocked||proposal.StaticNoWinner||proposal.Overflow||proposal.Refuse);
        }
        [Test] public void InactiveHeldNominalSpeedCannotInflateReachAndFalselyOverflowTheActualApproachRow()
        {
            const double dt=1d/30;var profile=NavigationProfile.Default;var bodies=new NavMotionBody[12];
            bodies[0]=new NavMotionBody{Id=6,Position=new NavPoint(-5.298166590145428,-3.421864846273539),Target=new NavPoint(0,-5.6000000000000005),Heading=-.3900480722634496,Radius=.72,Speed=4,TurnSpeed=4.5,Active=true};
            bodies[1]=new NavMotionBody{Id=5,Position=new NavPoint(-7.980187260773117,-3.913545413875582),Target=new NavPoint(.8,-5.6000000000000005),Heading=-.18976389747806016,Radius=.72,Speed=9.856009342001926e-8,TurnSpeed=4.5,Active=true};
            var obstacles=new NavObstacle[8];for(int i=0;i<8;i++){var point=new NavPoint(0,6.2575-i*1.4625);obstacles[i]=new NavObstacle(point,.72);bodies[i+2]=new NavMotionBody{Id=i+9,Position=point,Target=point,Radius=.72,Speed=4,TurnSpeed=4.5,Active=false};}
            bodies[10]=new NavMotionBody{Id=7,Position=new NavPoint(18,-1.8),Radius=.72,Speed=4,TurnSpeed=4.5,Active=false};bodies[11]=new NavMotionBody{Id=8,Position=new NavPoint(18,1.8),Radius=.72,Speed=4,TurnSpeed=4.5,Active=false};
            var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,dt);Assert.AreEqual(4,counts[0],"exact frozen speed reach includes three relevant HOLD circles and follower, cap stays8");
            var geometry=new NavGeometry(ThreeCrossingsMap.Default.HalfExtent,obstacles,1);var clearance=new RowClearance(bodies,geometry);var proposals=new NavMotionProposal[bodies.Length];for(int i=0;i<bodies.Length;i++)proposals[i]=NavCrowdMotion.Select(bodies,i,neighbors,counts[i],profile,dt,clearance);
            Assert.False(proposals[0].Overflow);Assert.Greater(NavCrowdMotion.Length(proposals[0].Velocity),0,"confirmed leader must receive its legal ordinary proposal");NavCrowdMotion.ResolveJoint(bodies,proposals,dt);Assert.Greater(NavCrowdMotion.Length(proposals[0].Velocity),0);
            for(int i=0;i<bodies.Length;i++){if(bodies[i].Active)Assert.True(clearance.ClearMotion(i,proposals[i].Position));for(int j=0;j<i;j++){var p=new NavPoint(bodies[i].Position.X-bodies[j].Position.X,bodies[i].Position.Z-bodies[j].Position.Z);var v=new NavPoint(proposals[i].Velocity.X-proposals[j].Velocity.X,proposals[i].Velocity.Z-proposals[j].Velocity.Z);Assert.GreaterOrEqual(SweptSeparationSquared(p,v,dt),Math.Pow(bodies[i].Radius+bodies[j].Radius,2)-1e-8);}}
        }
        [Test] public void PredictiveWaitCannotVetoEveryPhysicallySafeImprovingTurnAndStep()
        {
            const double dt=1d/30;var profile=NavigationProfile.Default;
            var bodies=new[]{new NavMotionBody{Id=1,Position=new NavPoint(0,0),Target=new NavPoint(10,0),Radius=.72,Speed=3,TurnSpeed=4.5,Active=true},new NavMotionBody{Id=2,Position=new NavPoint(1.5,0),Target=new NavPoint(10,0),Radius=.72,Speed=1e-7,TurnSpeed=4.5,Active=true}};
            var neighbors=new int[2*NavCrowdMotion.NeighborCapacity];var counts=new int[2];var clearance=new RowClearance(bodies,new NavGeometry(ThreeCrossingsMap.Default.HalfExtent,Array.Empty<NavObstacle>(),1));bool translated=false;
            for(int tick=0;tick<8;tick++){
                NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,dt);var before=bodies[0];var proposal=NavCrowdMotion.Select(bodies,0,neighbors,counts[0],profile,dt,clearance);
                if(tick==0)Assert.True(!proposal.Position.Equals(before.Position)||proposal.Heading!=before.Heading,"the physically available improving direction must beat heuristic-only stationary wait");
                var proposals=new[]{proposal,new NavMotionProposal{Position=bodies[1].Position,Heading=bodies[1].Heading}};NavCrowdMotion.ResolveJoint(bodies,proposals,dt);proposal=proposals[0];
                Assert.False(proposal.Blocked);Assert.False(proposal.Overflow);Assert.True(clearance.ClearMotion(0,proposal.Position));Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(proposal.Heading-before.Heading)),before.TurnSpeed*dt+1e-12);Assert.LessOrEqual(NavCrowdMotion.Length(proposal.Velocity),before.Speed+1e-12);
                var relative=new NavPoint(before.Position.X-bodies[1].Position.X,before.Position.Z-bodies[1].Position.Z);Assert.GreaterOrEqual(SweptSeparationSquared(relative,proposal.Velocity,dt),Math.Pow(before.Radius+bodies[1].Radius,2)-1e-9);
                if(!proposal.Position.Equals(before.Position)){translated=true;Assert.Less(Math.Pow(proposal.Position.X-10,2)+proposal.Position.Z*proposal.Position.Z,Math.Pow(before.Position.X-10,2)+before.Position.Z*before.Position.Z);}
                bodies[0].Position=proposal.Position;bodies[0].Heading=proposal.Heading;
            }
            Assert.True(translated,"bounded turn must lead to an actual improving full-radius step");
        }
        [Test] public void NineGenuinelyReachableInactivePeersStillCauseTheOriginalEightNeighborOverflow()
        {
            var bodies=new NavMotionBody[10];bodies[0]=new NavMotionBody{Id=1,Radius=.72,Speed=4,TurnSpeed=4.5,Active=true,Target=new NavPoint(10,0)};
            for(int i=1;i<bodies.Length;i++){double a=2*Math.PI*(i-1)/9;bodies[i]=new NavMotionBody{Id=i+1,Position=new NavPoint(5*Math.Cos(a),5*Math.Sin(a)),Radius=.72,Speed=4,Active=false};}
            var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];NavCrowdMotion.Neighbors(bodies,neighbors,counts,NavigationProfile.Default,1d/30);Assert.AreEqual(-1,counts[0]);var p=NavCrowdMotion.Select(bodies,0,neighbors,counts[0],NavigationProfile.Default,1d/30,new StationaryOnlyClearance());Assert.True(p.Overflow);Assert.AreEqual(bodies[0].Position,p.Position);
        }
        [Test] public void DenseMovingComponentUsesVerifiedQ9BoundaryWithoutScalingDisconnectedMover()
        {
            var profile=NavigationProfile.Default;const double dt=1d/30;var bodies=new NavMotionBody[13];
            for(int i=0;i<12;i++){double a=2*Math.PI*i/12;bodies[i]=new NavMotionBody{Id=i+1,Position=new NavPoint(3.6*Math.Cos(a),3.6*Math.Sin(a)),Radius=.72,Speed=4,TurnSpeed=4.5,Active=true};}
            bodies[12]=new NavMotionBody{Id=13,Position=new NavPoint(100,0),Radius=.72,Speed=4,TurnSpeed=4.5,Active=true};
            var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];var labels=new int[bodies.Length];var original=new double[bodies.Length];NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,dt,labels);Assert.AreEqual(-1,counts[0]);for(int i=1;i<12;i++)Assert.AreEqual(labels[0],labels[i]);Assert.AreNotEqual(labels[0],labels[12]);
            NavCrowdMotion.ConstrainOverflowComponents(bodies,neighbors,counts,labels,original,profile,dt);
            Assert.Greater(bodies[0].Speed,0,"a finite geometric boundary should retain positive speed");Assert.Less(bodies[0].Speed,4);
            for(int i=0;i<12;i++){Assert.AreEqual(4d,original[i]);Assert.AreEqual(bodies[0].Speed,bodies[i].Speed,"one common component ratio is used");Assert.That(counts[i],Is.InRange(0,profile.CrowdNeighborCap));}
            Assert.AreEqual(4d,bodies[12].Speed,1e-12,"disconnected component keeps its composed speed");Assert.AreEqual(0,counts[12]);
        }
        [Test] public void DenseMoverCanUsePositiveVerifiedBoundaryAgainstNineInactiveOuterPeers()
        {
            var profile=NavigationProfile.Default;const double dt=1d/30;var bodies=new NavMotionBody[10];bodies[0]=new NavMotionBody{Id=1,Position=default,Radius=.72,Speed=4,TurnSpeed=4.5,Active=true,Target=new NavPoint(10,0)};
            for(int i=1;i<bodies.Length;i++){double a=2*Math.PI*(i-1)/9;bodies[i]=new NavMotionBody{Id=i+1,Position=new NavPoint(1.5*Math.Cos(a),1.5*Math.Sin(a)),Radius=.72,Speed=4,Active=false};}
            var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];var labels=new int[bodies.Length];var original=new double[bodies.Length];NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,dt,labels);Assert.AreEqual(-1,counts[0]);for(int i=1;i<bodies.Length;i++)Assert.AreEqual(labels[0],labels[i],"untruncated relation must include stationary peers beyond the stored cap");
            NavCrowdMotion.ConstrainOverflowComponents(bodies,neighbors,counts,labels,original,profile,dt);
            Assert.Greater(bodies[0].Speed,0,"stationary peers outside the zero-speed radius still admit a positive ratio");Assert.Less(bodies[0].Speed,4);Assert.That(counts[0],Is.InRange(0,profile.CrowdNeighborCap));
            for(int i=1;i<bodies.Length;i++)Assert.AreEqual(4d,bodies[i].Speed,1e-12,"inactive body state is never scaled");
            var proposal=NavCrowdMotion.Select(bodies,0,neighbors,counts[0],profile,dt,new StationaryOnlyClearance());Assert.False(proposal.Overflow);
        }
        [Test] public void DenseMoverKeepsOriginalOverflowWhenMoreThanCapPeersArePermanentAtZeroSpeed()
        {
            var profile=NavigationProfile.Default;const double dt=1d/30;var bodies=new NavMotionBody[10];bodies[0]=new NavMotionBody{Id=1,Position=default,Radius=.72,Speed=4,TurnSpeed=4.5,Active=true,Target=new NavPoint(10,0)};
            for(int i=1;i<bodies.Length;i++){double a=2*Math.PI*(i-1)/9;bodies[i]=new NavMotionBody{Id=i+1,Position=new NavPoint(1.0*Math.Cos(a),1.0*Math.Sin(a)),Radius=.72,Speed=4,Active=false};}
            var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];var labels=new int[bodies.Length];var original=new double[bodies.Length];NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,dt,labels);Assert.AreEqual(-1,counts[0]);
            NavCrowdMotion.ConstrainOverflowComponents(bodies,neighbors,counts,labels,original,profile,dt);
            Assert.AreEqual(4d,bodies[0].Speed,1e-12,"nine peers remain neighbors even at zero speed");Assert.AreEqual(-1,counts[0]);
            for(int i=1;i<bodies.Length;i++)Assert.AreEqual(4d,bodies[i].Speed,1e-12,"inactive body state is never scaled");
            var proposal=NavCrowdMotion.Select(bodies,0,neighbors,counts[0],profile,dt,new StationaryOnlyClearance());Assert.True(proposal.Overflow);Assert.AreEqual(bodies[0].Position,proposal.Position);
        }
        [Test] public void GeometricEnclosureCannotMasqueradeAsEndlessTurnInPlace()
        {
            var bodies=new[]{new NavMotionBody{Id=1,Position=new NavPoint(0,0),Target=new NavPoint(10,0),Radius=.72,Speed=4,TurnSpeed=4.5,Active=true}};var neighbors=new int[NavCrowdMotion.NeighborCapacity];var profile=NavigationProfile.Default;
            for(int tick=0;tick<180;tick++){var p=NavCrowdMotion.Select(bodies,0,neighbors,0,profile,1d/30,new StationaryOnlyClearance());Assert.True(p.Blocked,"No translated endpoint is possible even if heading keeps changing");Assert.AreEqual(bodies[0].Position,p.Position);bodies[0].Heading=p.Heading;}
        }
        [TestCase(false)][TestCase(true)] public void ExactJointCheckpointTurnsTowardTheAvailableGoalInsteadOfForecastFavoredRetreat(bool reverse)
        {
            const double dt=1d/30;var profile=PlayableProfile.ThreeCrossingsDefault.Navigation;var bodies=new NavMotionBody[10];
            bodies[0]=new NavMotionBody{Id=7,Position=new NavPoint(1.7371111879933163,-6.278008920210177),Target=new NavPoint(-21.6,-3.6),Heading=.2784456094363126,Radius=.72,Speed=4,TurnSpeed=4.5,Active=true};
            bodies[1]=new NavMotionBody{Id=8,Position=new NavPoint(1.712968408520665,-4.815143529760004),Target=new NavPoint(0,-5.6000000000000005),Heading=-2.7119529445854917,Radius=.72,Speed=4,TurnSpeed=4.5,Active=true};
            var obstacles=new NavObstacle[8];for(int i=0;i<8;i++){var point=new NavPoint(0,6.2575-i*1.4625);obstacles[i]=new NavObstacle(point,.72);bodies[i+2]=new NavMotionBody{Id=i+9,Position=point,Target=point,Radius=.72,Speed=4,TurnSpeed=4.5,Active=false};}
            if(reverse)Array.Reverse(bodies);int row=Array.FindIndex(bodies,b=>b.Id==7);var a=bodies[row];var clearance=new RowClearance(bodies,new NavGeometry(ThreeCrossingsMap.Default.HalfExtent,obstacles,1));var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,dt);Assert.AreEqual(4,counts[row]);
            var risk=typeof(NavCrowdMotion).GetMethod("Risk",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);Assert.AreEqual(.1341042552702062,(double)risk.Invoke(null,new object[]{bodies,row,default(NavPoint),neighbors,counts[row],profile.CrowdLookaheadSeconds}),1e-12,"same frozen prediction inputs as current threaded checkpoint");
            double aim=Math.Atan2(a.Target.Z-a.Position.Z,a.Target.X-a.Position.X);var futureVelocity=new NavPoint(Math.Cos(aim)*a.Speed,Math.Sin(aim)*a.Speed);var future=new NavPoint(a.Position.X+futureVelocity.X*dt,a.Position.Z+futureVelocity.Z*dt);Assert.True(clearance.ClearMotion(row,future));
            for(int i=0;i<bodies.Length;i++)if(i!=row){var separation=new NavPoint(a.Position.X-bodies[i].Position.X,a.Position.Z-bodies[i].Position.Z);Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(separation,futureVelocity,dt),Math.Pow(a.Radius+bodies[i].Radius,2)-1e-9,"goal step is full-radius safe when peers wait after the bounded turn");}
            var selected=NavCrowdMotion.Select(bodies,row,neighbors,counts[row],profile,dt,clearance);Assert.Less(Math.Abs(NavCrowdMotion.Angle(aim-selected.Heading)),Math.Abs(NavCrowdMotion.Angle(aim-a.Heading))-1e-12,"available goal turn must beat non-improving forecast-favored retreat");Assert.AreEqual(a.Position,selected.Position,"goal direction requires bounded turn-in-place first");Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(selected.Heading-a.Heading)),a.TurnSpeed*dt+1e-12);Assert.False(selected.Blocked);Assert.False(selected.Overflow);
        }
        [TestCase(false)][TestCase(true)] public void ExactThreadedPairKeepsTheOnlySafeOneSidedStepRegardlessOfRowOrder(bool reverse)
        {
            const double dt=1d/30;var seven=new NavPoint(1.8025588083345399,-6.277346450446917);var eight=new NavPoint(1.7999766506474413,-4.808812069869413);
            var bodies=new[]{new NavMotionBody{Id=7,Position=seven,Radius=.72,Active=true},new NavMotionBody{Id=8,Position=eight,Radius=.72,Active=true}};
            var v7=new NavPoint(3.8455561580950057,1.100771472621624);var v8=new NavPoint(-1.8309305884546425,-.8047938743933982);
            var end7=new NavPoint(1.9307440136043734,-6.240654068026196);var end8=new NavPoint(1.7389456310322866,-4.835638532349193);
            var proposals=new[]{new NavMotionProposal{Position=end7,Velocity=v7,Heading=.27879031069004245},new NavMotionProposal{Position=end8,Velocity=v8,Heading=-2.727459037402399}};
            var relative=new NavPoint(seven.X-eight.X,seven.Z-eight.Z);double radiusSquared=1.44*1.44;
            Assert.Less(NavCrowdMotion.ClosestSquared(relative,new NavPoint(v7.X-v8.X,v7.Z-v8.Z),dt),radiusSquared-1e-9,"captured simultaneous proposals conflict");
            Assert.Less(NavCrowdMotion.ClosestSquared(relative,v7,dt),radiusSquared-1e-9,"7 cannot advance against stationary8");
            Assert.Greater(NavCrowdMotion.ClosestSquared(relative,new NavPoint(-v8.X,-v8.Z),dt),radiusSquared+1e-9,"8 has an actual full-dt-safe step against stationary7");
            if(reverse){Array.Reverse(bodies);Array.Reverse(proposals);}NavCrowdMotion.ResolveJoint(bodies,proposals,dt);
            int row7=Array.FindIndex(bodies,b=>b.Id==7),row8=Array.FindIndex(bodies,b=>b.Id==8);Assert.AreEqual(seven,proposals[row7].Position);Assert.AreEqual(default(NavPoint),proposals[row7].Velocity);Assert.AreEqual(end8,proposals[row8].Position,"retain the only safe proposal instead of refusing both");Assert.AreEqual(v8,proposals[row8].Velocity);
            var jointVelocity=new NavPoint(proposals[row7].Velocity.X-proposals[row8].Velocity.X,proposals[row7].Velocity.Z-proposals[row8].Velocity.Z);Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(relative,jointVelocity,dt),radiusSquared-1e-9);
        }
        [TestCase(false)][TestCase(true)] public void OneSidedStableIdentityRefusalRevalidatesTheNewStationaryHazard(bool reverse)
        {
            const double dt=.4;var bodies=new[]{new NavMotionBody{Id=1,Position=new NavPoint(-1.6,0),Radius=.72,Active=true},new NavMotionBody{Id=2,Position=new NavPoint(2,0),Radius=.72,Active=true},new NavMotionBody{Id=3,Position=new NavPoint(0,0),Radius=.72,Active=true}};
            var proposals=new[]{new NavMotionProposal{Position=new NavPoint(-1.2,0),Velocity=new NavPoint(1,0)},new NavMotionProposal{Position=new NavPoint(1.6,0),Velocity=new NavPoint(-1,0)},new NavMotionProposal{Position=new NavPoint(.4,0),Velocity=new NavPoint(1,0)}};
            if(reverse){Array.Reverse(bodies);Array.Reverse(proposals);}NavCrowdMotion.ResolveJoint(bodies,proposals,dt);
            for(int i=0;i<bodies.Length;i++){
                if(bodies[i].Id==2){Assert.AreEqual(new NavPoint(-1,0),proposals[i].Velocity,"stable id2 wins the safe reciprocal tie");Assert.AreEqual(new NavPoint(1.6,0),proposals[i].Position);}
                else{Assert.AreEqual(default(NavPoint),proposals[i].Velocity);Assert.AreEqual(bodies[i].Position,proposals[i].Position,"id1 must wait after id3 becomes stationary");}
                for(int j=0;j<i;j++){var separation=new NavPoint(bodies[i].Position.X-bodies[j].Position.X,bodies[i].Position.Z-bodies[j].Position.Z);var relative=new NavPoint(proposals[i].Velocity.X-proposals[j].Velocity.X,proposals[i].Velocity.Z-proposals[j].Velocity.Z);Assert.GreaterOrEqual(NavCrowdMotion.ClosestSquared(separation,relative,dt),Math.Pow(bodies[i].Radius+bodies[j].Radius,2)-1e-9);}
            }
        }
        [Test] public void ActivePeerJointRefusalIsSafeWaitWithoutInventingPhysicalBlockage()
        {
            var bodies=new[]{new NavMotionBody{Id=1,Position=new NavPoint(-1,0),Radius=.72,Active=true},new NavMotionBody{Id=2,Position=new NavPoint(1,0),Radius=.72,Active=true}};
            var proposals=new[]{new NavMotionProposal{Position=new NavPoint(0,0),Velocity=new NavPoint(1,0)},new NavMotionProposal{Position=new NavPoint(0,0),Velocity=new NavPoint(-1,0)}};
            NavCrowdMotion.ResolveJoint(bodies,proposals,1);for(int i=0;i<2;i++){Assert.AreEqual(bodies[i].Position,proposals[i].Position);Assert.AreEqual(new NavPoint(0,0),proposals[i].Velocity);Assert.False(proposals[i].Blocked);}
        }
        [Test] public void EnclosingHeldActorsKeepWholePoseAndTheOriginalMoveTimesOut()
        {
            var profile=NavigationProfile.Default;var crowd=new NavCrowd(new NavGeometry(20,Array.Empty<NavObstacle>(),1),profile);var actor=crowd.Add(1,new NavPoint(0,0));Assert.True(crowd.SetRoute(1,new NavPoint(10,0),new[]{new NavPoint(10,0)}));
            var points=new[]{new NavPoint(1.45,0),new NavPoint(-1.45,0),new NavPoint(0,1.45),new NavPoint(0,-1.45)};for(int i=0;i<points.Length;i++){crowd.Add(i+2,points[i]);crowd.Stop(i+2,true);}
            for(int tick=0;tick<180&&actor.Moving;tick++){crowd.Step(1d/30);for(int i=0;i<points.Length;i++){Assert.AreEqual(points[i],crowd.Units[i+1].Position);Assert.AreEqual(0,crowd.Units[i+1].Heading);Assert.True(crowd.Units[i+1].Held);}}
            Assert.AreEqual(NavigationOutcome.Blocked,actor.Outcome);Assert.AreEqual(new NavPoint(0,0),actor.Position);Assert.Greater(crowd.RepairFailed,0,"Retain actual bounded repair attempts before original timeout");
        }
        [Test] public void OrdinaryTwoGroupsCrossCentralBridgeWithJointSweptSafetyAndColdNextStep()
        {
            const double dt=1d/30;var c=Configuration();var world=new OfflineParticipantAuthority(c,71);var nav=world.Navigation;var map=(ThreeCrossingsMap)c.Profile.AuthoredMap;
            var tanks=world.View(c.Roster[0].Id).Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).OrderBy(id=>id).ToArray();
            var left=tanks.Where(id=>nav.Crowd.Units.Single(u=>u.Id==id).Position.X<0).ToArray();var right=tanks.Except(left).ToArray();
            var crossed=new HashSet<int>();var starts=tanks.ToDictionary(id=>id,id=>nav.Crowd.Units.Single(u=>u.Id==id).Position);int preparation=0,ticks=0,compatibilityWorkspaces=0;bool coldChecked=false;
            nav.PhysicalHostBudget.AllocationAdmission=bytes=>{long layout=NavStorageScratch.LeaseBytes+NavMotionWorkspace.HeaderBytes+NavMotionWorkspace.ArrayBytes(nav.Crowd.Units.Count);if(bytes==layout&&new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().Name=="CreateCompatibilityMotionWorkspace"))compatibilityWorkspaces++;return true;};
            var host=new UnityHostRouteService();try{
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,1,c.Roster[0].Id,PlayableCommandKind.Move,left,new NavPoint(18,0)).AsHuman()).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,2,c.Roster[0].Id,PlayableCommandKind.Move,right,new NavPoint(-18,0)).AsHuman()).Status);
                Assert.AreEqual(2,left.Length);Assert.AreEqual(2,right.Length);Assert.AreNotEqual(nav.GroupFor(left[0]).GroupId,nav.GroupFor(right[0]).GroupId);
                var groups=tanks.ToDictionary(id=>id,id=>nav.GroupFor(id).GroupId);var orders=tanks.ToDictionary(id=>id,id=>nav.GroupFor(id).OrderRevision);
                for(;preparation<RoutePreparationBound&&nav.PendingCount>0;preparation++){host.Service(new PlayableRouteBinding(world.Domain.Geometry,c.Profile,nav.Admission,nav.RoutePort),RouteBudget);nav.ApplyResults();}
                Assert.Zero(nav.PendingCount,"frozen route preparation bound, before motion");foreach(int id in tanks)Assert.NotNull(nav.InstalledExecutionFor(id));
                for(;ticks<MotionBound&&tanks.Any(id=>nav.Crowd.Units.Single(u=>u.Id==id).Outcome!=NavigationOutcome.Arrived);ticks++){
                    var before=nav.Crowd.CaptureState().Units;
                    host.Service(new PlayableRouteBinding(world.Domain.Geometry,c.Profile,nav.Admission,nav.RoutePort),RouteBudget);
                    OfflineParticipantAuthority cold=null;
                    UnityHostRouteService coldHost=null;try{
                        if(ticks==90){var bytes=world.CaptureBytes();cold=OfflineParticipantAuthority.Restore(bytes,c);coldHost=new UnityHostRouteService();CollectionAssert.AreEqual(bytes,cold.CaptureBytes());coldHost.Service(new PlayableRouteBinding(cold.Domain.Geometry,c.Profile,cold.Navigation.Admission,cold.Navigation.RoutePort),RouteBudget);}
                        world.Step(dt);if(cold!=null){cold.Step(dt);CollectionAssert.AreEqual(world.CaptureBytes(),cold.CaptureBytes(),"fixed actual motion next step after cold restore");coldChecked=true;}
                    }finally{coldHost?.Dispose();if(cold!=null){cold.Navigation.ReleaseCommittedHostRoot();cold.Domain.DestroyOwnedNavigation();Assert.Zero(cold.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(cold.Navigation.PhysicalHostBudget.HeldBytes);Assert.Zero(cold.Navigation.PhysicalHostBudget.ActiveScopes);}}
                    var after=nav.Crowd.Units;
                    for(int i=0;i<after.Count;i++){
                        var a=after[i];var b=before.Single(u=>u.Id==a.Id);var displacement=new NavPoint(a.Position.X-b.Position.X,a.Position.Z-b.Position.Z);
                        Assert.LessOrEqual(NavCrowdMotion.Length(displacement),b.Speed*dt+1e-8,"no teleport/speed violation");Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(a.Heading-b.Heading)),b.TurnSpeed*dt+1e-8);
                        if(b.Held){Assert.AreEqual(b.Position,a.Position);Assert.AreEqual(b.Heading,a.Heading);}
                        Assert.True(world.Domain.Geometry.SegmentFree(b.Position,a.Position,a.Radius));Assert.True(map.SupportsSweep(b.Position,a.Position,a.Radius));
                        for(int j=0;j<i;j++){var other=after[j];var previous=before.Single(u=>u.Id==other.Id);double r=a.Radius+other.Radius;var relative=new NavPoint(b.Position.X-previous.Position.X,b.Position.Z-previous.Position.Z);var velocity=new NavPoint((displacement.X-other.Position.X+previous.Position.X)/dt,(displacement.Z-other.Position.Z+previous.Position.Z)/dt);Assert.GreaterOrEqual(SweptSeparationSquared(relative,velocity,dt),r*r-1e-8,"whole actual dt peer sweep");}
                        if(tanks.Contains(a.Id)){
                            Assert.AreEqual(groups[a.Id],nav.GroupFor(a.Id).GroupId);Assert.AreEqual(orders[a.Id],nav.GroupFor(a.Id).OrderRevision);
                            if(Math.Abs(a.Position.X)<=map.RiverHalfWidth){Assert.LessOrEqual(Math.Abs(a.Position.Z)+a.Radius,map.CentralBridgeWidth/2+1e-8,"must use central crossing");if(a.Location.SurfaceId=="bridge/central")crossed.Add(a.Id);}
                        }
                    }
                }
                Assert.True(coldChecked);CollectionAssert.AreEquivalent(tanks,crossed);foreach(int id in tanks){var actor=nav.Crowd.Units.Single(u=>u.Id==id);Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome,"actor "+id+" at "+actor.Position);Assert.AreEqual(-Math.Sign(starts[id].X),Math.Sign(actor.Position.X),"opposite bank arrival");Assert.Greater(Math.Abs(actor.Position.X)-actor.Radius,map.RiverHalfWidth,"whole terminal footprint beyond river");}
            }finally{
                TestContext.WriteLine("B0_ACTUAL_PASSAGE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Preparation=preparation,MotionTicks=ticks,ColdNextStep=coldChecked,CompatibilityWorkspaces=compatibilityWorkspaces,Crossed=crossed,nav.Crowd.RepairCount,nav.Crowd.RepairFailed,Actors=tanks.Select(id=>{var a=nav.Crowd.Units.Single(u=>u.Id==id);return new{a.Id,Start=starts[id],a.Position,a.Goal,Progress=Math.Sqrt(Math.Pow(a.Goal.X-starts[id].X,2)+Math.Pow(a.Goal.Z-starts[id].Z,2))-Math.Sqrt(Math.Pow(a.Goal.X-a.Position.X,2)+Math.Pow(a.Goal.Z-a.Position.Z,2)),a.Heading,a.Outcome,a.InstalledGroupId,a.InstalledOrder,a.routeIndex,a.noProgressTicks,a.blockedSeconds};}).ToArray()}));
                nav.PhysicalHostBudget.AllocationAdmission=null;host.Dispose();nav.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
        [Test] public void FrozenWideCentralBridgeSceneHasFullRadiusLaneAndExitClearance()
        {
            var c=Configuration();var world=new OfflineParticipantAuthority(c,71);var nav=world.Navigation;var map=(ThreeCrossingsMap)c.Profile.AuthoredMap;var geometry=world.Domain.Geometry;var radius=c.Profile.TankCollisionRadius;double lane=c.Profile.Navigation.ArrivalSlotSpacing/2;var points=c.ScenarioUnits.Select(u=>u.Position).ToArray();var facts=new List<object>();
            try{
                Assert.AreEqual(4,c.ScenarioUnits.Count);Assert.AreEqual(4,world.View(c.Roster[0].Id).Entities.Count(e=>e.Kind==PlayableEntityKind.Tank));Assert.AreEqual(c.Roster.Count+4,nav.Crowd.Units.Count,"preserve ordinary genesis explorers");
                foreach(var p in points){
                    var blockers=geometry.Obstacles.Where(o=>o.DistanceSquared(p)<radius*radius).Select(o=>new{o.MinX,o.MinZ,o.MaxX,o.MaxZ}).ToArray();bool free=geometry.IsFree(p,radius),footprint=map.SupportsFootprint(p,radius),located=map.TryLocate(p,radius,out var location);
                    facts.Add(new{Point=p,Radius=radius,Free=free,Footprint=footprint,Located=located,Surface=located?location.SurfaceId:null,Blockers=blockers});
                    Assert.True(free&&footprint&&located,"invalid frozen point: "+Newtonsoft.Json.JsonConvert.SerializeObject(facts.Last()));
                }
                for(int i=0;i<points.Length;i++)for(int j=i+1;j<points.Length;j++){double x=points[i].X-points[j].X,z=points[i].Z-points[j].Z;Assert.GreaterOrEqual(x*x+z*z,4*radius*radius,"frozen starts full-radius pair clearance");}
                foreach(var start in points){
                    var end=new NavPoint(-start.X,start.Z);Assert.True(geometry.SegmentFree(start,end,radius),"actual domain geometry lane");Assert.True(map.SupportsSweep(start,end,radius),"whole-footprint authored lane sweep");Assert.True(map.TryLocate(start,radius,out var from));Assert.True(map.TryTraverse(from,end,radius,out var terminal),"bank/bridge/bank seam traversal");Assert.AreEqual(end,terminal.Position);
                    var surfaces=new List<string>();foreach(double x in new[]{-18d,-map.CentralBridgeHalfSpan,-map.RiverHalfWidth,0,map.RiverHalfWidth,map.CentralBridgeHalfSpan,18d}){Assert.True(map.TryLocate(new NavPoint(x,start.Z),radius,out var at));surfaces.Add(at.SurfaceId);}Assert.Contains("bridge/central",surfaces);
                    facts.Add(new{LaneStart=start,LaneEnd=end,Surfaces=surfaces});
                }
                foreach(double x in new[]{-18d,18d}){
                    var center=new NavPoint(x,0);Assert.True(nav.Crowd.CanPlace(center,radius),"opposite original group goal is initially placeable");
                    var low=new NavPoint(x,-lane);var high=new NavPoint(x,lane);Assert.True(geometry.IsFree(low,radius)&&geometry.IsFree(high,radius)&&map.SupportsFootprint(low,radius)&&map.SupportsFootprint(high,radius));Assert.GreaterOrEqual(2*lane,2*radius,"two full terminal slots");
                }
            }finally{
                nav.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();
                TestContext.WriteLine("B0_FINITE_PREFLIGHT "+Newtonsoft.Json.JsonConvert.SerializeObject(new{c.Seed,Radius=radius,LaneOffset=lane,Map=map.Id,Revision=map.Revision,PreparationBound=RoutePreparationBound,RouteBudget,MotionBound,Facts=facts,Cleanup=new{nav.PhysicalHostBudget.PhysicalBytes,nav.PhysicalHostBudget.HeldBytes,nav.PhysicalHostBudget.ActiveScopes}}));
                Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
    }
}

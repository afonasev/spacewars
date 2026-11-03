using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdRefinementReuseTests
    {
        const double Dt=1d/30;
        static byte[] Bytes(NavigationSession s){using(var state=s.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
        static byte[] ProposalBytes(NavMotionProposal p)
        {
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)){
                w.Write(BitConverter.DoubleToInt64Bits(p.Position.X));w.Write(BitConverter.DoubleToInt64Bits(p.Position.Z));
                w.Write(BitConverter.DoubleToInt64Bits(p.Velocity.X));w.Write(BitConverter.DoubleToInt64Bits(p.Velocity.Z));w.Write(BitConverter.DoubleToInt64Bits(p.Heading));
                w.Write(p.Blocked);w.Write(p.Overflow);w.Write(p.Refuse);w.Write(p.StaticNoWinner);w.Write(p.IndependentTurn);return stream.ToArray();
            }
        }
        sealed class SouthClearance : INavMotionClearance
        {
            public bool ClearMotion(int actor,NavPoint end)=>end.Z<=0;
        }
        [Test] public void CertifiedGoalEscapeExecutesAfterItsTurnWhenTheTemporaryTargetOpposesIt()
        {
            var p=PlayableProfile.Default.Navigation;var clearance=new SouthClearance();var goal=new NavPoint(-10,-10);
            var bodies=new[]{new NavMotionBody{Id=1,Position=default,Target=new NavPoint(0,10),Heading=-Math.PI/2+.09,Radius=.72,Speed=4,TurnSpeed=4.5,Active=true,OwnMoving=true},new NavMotionBody{Id=2,Position=new NavPoint(0,1.5),Target=new NavPoint(0,1.5),Radius=.72}};
            var neighbors=Neighbors();var original=(NavMotionBody[])bodies.Clone();bool moved=false;
            // Ordinary opt-out and moving-peer forecasting keep their contract.
            Assert.AreEqual(bodies[0].Position,NavCrowdMotion.Select(bodies,0,neighbors,1,p,Dt,clearance,goal).Position);
            bodies[1].Active=true;
            Assert.AreEqual(bodies[0].Position,NavCrowdMotion.Select(bodies,0,neighbors,1,p,Dt,clearance,goal,true).Position);
            bodies[1]=original[1];
            for(int i=0;i<Math.Ceiling(Math.PI/(bodies[0].TurnSpeed*Dt))+2;i++){
                var before=bodies[0];var next=NavCrowdMotion.Select(bodies,0,neighbors,1,p,Dt,clearance,goal,true);
                Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(next.Heading-before.Heading)),before.TurnSpeed*Dt+1e-12);
                var proposals=new[]{next,new NavMotionProposal{Position=bodies[1].Position}};NavCrowdMotion.ResolveJoint(bodies,proposals,Dt);
                if(!proposals[0].Position.Equals(before.Position)){
                    Assert.AreEqual(next.Position,proposals[0].Position);Assert.False(next.IndependentTurn);Assert.True(clearance.ClearMotion(0,next.Position));
                    Assert.True(NavCrowdMotion.ImprovesGoal(before.Position,next.Position,goal));Assert.LessOrEqual(NavCrowdMotion.Length(next.Velocity),before.Speed);
                    moved=true;break;
                }
                bodies[0].Heading=next.Heading;
            }
            Assert.True(moved,"A certified escape must progress beyond its turn instead of oscillating around alignment");
            Assert.AreEqual(original[1],bodies[1]);Assert.AreEqual(original[0].Position,bodies[0].Position);
        }
        sealed class LocalMotorClearance : INavMotionClearance
        {
            internal readonly NavGeometry Geometry;
            internal LocalMotorClearance(NavMotionBody[] bodies)=>Geometry=new NavGeometry(100,bodies.Skip(1).Select(b=>new NavObstacle(b.Position,b.Radius)).ToArray(),1);
            internal NavPoint From;
            public bool ClearMotion(int actor,NavPoint end)=>Geometry.SegmentFree(From,end,.72);
        }
        [Test] public void CertifiedShortLocalMotorArrivesBeforeForecastingBeyondItsCorner()
        {
            const double dt=1d/30;var p=PlayableProfile.RecastGroupCorridorDefault.Navigation;
            var start=new NavPoint(37.94876984335728,37.43123515886525);var target=new NavPoint(start.X,start.Z+.2);
            var bodies=new[]{
                new NavMotionBody{Id=10,Position=start,Target=target,Heading=1.1831853071795884,Radius=.72,Speed=4,TurnSpeed=4.5,Active=true,OwnMoving=true},
                new NavMotionBody{Id=5,Position=new NavPoint(37.68007491567019,39.131836143724804),Radius=.72},
                new NavMotionBody{Id=6,Position=new NavPoint(36.55227313786638,36.909874737585255),Radius=.72}};
            var original=(NavMotionBody[])bodies.Clone();var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];neighbors[0]=1;neighbors[1]=2;
            var clearance=new LocalMotorClearance(bodies);bool arrived=false;
            for(int tick=0;tick<Math.Ceiling(2*Math.PI/(bodies[0].TurnSpeed*dt))+3;tick++){
                var before=bodies[0];double distance=NavCrowdMotion.Length(new NavPoint(target.X-before.Position.X,target.Z-before.Position.Z));
                if(distance<=p.ArrivalTolerance){arrived=true;break;}
                bodies[0].LocalLookaheadSeconds=Math.Max(dt,distance/before.Speed);clearance.From=before.Position;
                var next=NavCrowdMotion.Select(bodies,0,neighbors,2,p,dt,clearance,new NavPoint(32.3,45.8),false,target);
                var proposals=new[]{next,new NavMotionProposal{Position=bodies[1].Position},new NavMotionProposal{Position=bodies[2].Position}};
                NavCrowdMotion.ResolveJoint(bodies,proposals,dt);next=proposals[0];
                Assert.True(clearance.ClearMotion(0,next.Position),"Full swept body clearance remains authoritative");
                Assert.LessOrEqual(NavCrowdMotion.Length(new NavPoint(next.Position.X-before.Position.X,next.Position.Z-before.Position.Z)),before.Speed*dt+1e-12);
                Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(next.Heading-before.Heading)),before.TurnSpeed*dt+1e-12);
                bodies[0].Position=next.Position;bodies[0].Heading=next.Heading;
            }
            Assert.True(arrived,"The certified local corner must finish instead of oscillating before a static obstacle beyond it");
            Assert.AreEqual(original[1],bodies[1]);Assert.AreEqual(original[2],bodies[2]);
        }
        static NavigationSession Create(int direction)
        {
            var s=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            var a=s.Crowd.Add(1,new NavPoint(0,0),.72,4,4.5);a.Heading=direction>0?0:Math.PI;
            var goal=new NavPoint(10*direction,0);Assert.True(s.Crowd.SetRoute(1,goal,new[]{goal}));
            s.Crowd.Add(2,new NavPoint(2.5*direction,0),.72,4,4.5);
            return s;
        }
        static NavMotionBody[] Bodies(NavigationSession s)=>s.Crowd.Units.Select(u=>new NavMotionBody{
            Id=u.Id,Position=u.Position,Target=u.Moving?u.Goal:u.Position,Heading=u.Heading,Radius=u.Radius,Speed=u.Speed,TurnSpeed=u.TurnSpeed,Active=u.Moving
        }).ToArray();
        static int[] Neighbors(){var n=new int[2*NavCrowdMotion.NeighborCapacity];n[0]=1;return n;}
        static void Switch(bool reuse)
        {
            NavCrowdMotion.DisableSelectClearanceReuseForQa=false;NavCrowdMotion.CountSelectClearanceForQa=true;
            NavCrowdMotion.SelectClearanceCallsForQa=NavCrowdMotion.SelectClearanceHitsForQa=0;
            NavCrowdMotion.DisableRefinementReuseForQa=!reuse;NavCrowdMotion.CountRefinementForQa=true;
            NavCrowdMotion.RefinementCallsForQa=NavCrowdMotion.RefinementIterationsForQa=NavCrowdMotion.RefinementReuseCallsForQa=NavCrowdMotion.RefinementRiskReuseCallsForQa=0;
        }
        [TearDown] public void Reset()
        {
            NavCrowdMotion.DisableRefinementReuseForQa=NavCrowdMotion.CountRefinementForQa=false;
            NavCrowdMotion.DisableSelectClearanceReuseForQa=NavCrowdMotion.CountSelectClearanceForQa=false;
        }
        static void Destroy(NavigationSession s)
        {
            if(s==null)return;s.DestroyNavigationAuthority();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);
        }
        [TestCase(1)][TestCase(-1)] public void ActualRefinementReusesBothInputsAndKeepsEveryProposalBit(int direction)
        {
            NavigationSession s=null;try{
                s=Create(direction);Assert.True(s.Crowd.StableSelectClearance);var before=Bytes(s);long physical=s.PhysicalHostBudget.PhysicalBytes;
                var bodies=Bodies(s);var neighbors=Neighbors();var goal=bodies[0].Target;
                Switch(false);var a=NavCrowdMotion.Select(bodies,0,neighbors,1,PlayableProfile.Default.Navigation,Dt,s.Crowd,goal);
                long calls=NavCrowdMotion.SelectClearanceCallsForQa,iterations=NavCrowdMotion.RefinementIterationsForQa;
                Assert.Greater(iterations,0,"predecessor really enters the refinement loop");Assert.Zero(NavCrowdMotion.RefinementReuseCallsForQa);
                Switch(true);var b=NavCrowdMotion.Select(bodies,0,neighbors,1,PlayableProfile.Default.Navigation,Dt,s.Crowd,goal);
                Assert.Greater(NavCrowdMotion.RefinementReuseCallsForQa,0,"production refinement receives existing clearance span");
                Assert.Greater(NavCrowdMotion.RefinementRiskReuseCallsForQa,0,"production refinement evaluates risk using existing inputs");
                Assert.AreEqual(iterations,NavCrowdMotion.RefinementIterationsForQa,"same angular search and termination");
                Assert.Less(NavCrowdMotion.SelectClearanceCallsForQa,calls,"actual repeated physical queries removed");
                CollectionAssert.AreEqual(ProposalBytes(a),ProposalBytes(b),"every proposal double bit and flag");
                CollectionAssert.AreEqual(before,Bytes(s),"refinement queries have no persisted effects");Assert.AreEqual(physical,s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);
                TestContext.WriteLine("direction="+direction+" refinementIterations="+iterations+" clearanceCallsBefore="+calls+" after="+NavCrowdMotion.SelectClearanceCallsForQa+" refinementRiskReuse="+NavCrowdMotion.RefinementRiskReuseCallsForQa);
            }finally{Destroy(s);}
        }
        sealed class Callback : INavMotionClearance
        {
            readonly INavMotionClearance source;readonly bool stateful;internal readonly List<long> Trace=new List<long>();int calls;
            internal Callback(INavMotionClearance source,bool stateful){this.source=source;this.stateful=stateful;}
            public bool ClearMotion(int actor,NavPoint end)
            {
                bool result=source.ClearMotion(actor,end);calls++;if(stateful&&calls%7==0)result=false;
                Trace.Add(actor);Trace.Add(BitConverter.DoubleToInt64Bits(end.X));Trace.Add(BitConverter.DoubleToInt64Bits(end.Z));Trace.Add(result?1:0);return result;
            }
        }
        [Test] public void ArbitraryAndStatefulCallbacksKeepExactRefinementOrder()
        {
            NavigationSession s=null;try{
                s=Create(1);var bodies=Bodies(s);var neighbors=Neighbors();var before=Bytes(s);
                foreach(bool stateful in new[]{false,true}){
                    var first=new Callback(s.Crowd,stateful);var second=new Callback(s.Crowd,stateful);
                    Switch(false);var a=NavCrowdMotion.Select(bodies,0,neighbors,1,PlayableProfile.Default.Navigation,Dt,first,bodies[0].Target);long iterations=NavCrowdMotion.RefinementIterationsForQa;
                    if(!stateful)Assert.Greater(iterations,0,"opaque callback case really reaches refinement");
                    Switch(true);var b=NavCrowdMotion.Select(bodies,0,neighbors,1,PlayableProfile.Default.Navigation,Dt,second,bodies[0].Target);
                    Assert.AreEqual(iterations,NavCrowdMotion.RefinementIterationsForQa);Assert.Zero(NavCrowdMotion.RefinementReuseCallsForQa);Assert.Zero(NavCrowdMotion.RefinementRiskReuseCallsForQa);
                    CollectionAssert.AreEqual(first.Trace,second.Trace,"exact ordered actor/endpoint-bit/result callback trace; stateful="+stateful);
                    CollectionAssert.AreEqual(ProposalBytes(a),ProposalBytes(b));
                }
                CollectionAssert.AreEqual(before,Bytes(s));
            }finally{Destroy(s);}
        }
    }
}

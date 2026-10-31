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

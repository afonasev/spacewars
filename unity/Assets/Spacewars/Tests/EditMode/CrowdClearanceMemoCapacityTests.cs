using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdClearanceMemoCapacityTests
    {
        [TearDown] public void Reset(){NavCrowdMotion.ForceSmallClearanceMemoForTests=NavCrowdMotion.CountSelectClearanceForQa=false;}
        [Test] public void ActualCrowdSelectKeepsEveryProposalBitAndPaidSessionState()
        {
            var map=ThreeCrossingsMap.Default;var profile=PlayableProfile.Default.Navigation;
            var session=new NavigationSession(71,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),profile,terrain:map);
            byte[] State(){using(var state=session.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
            try{
                for(int x=0;x<8;x++)for(int z=0;z<5;z++){
                    var point=new NavPoint(-55+x*4.5,-20+z*4.5);var goal=new NavPoint(point.X+2,point.Z);
                    if(!map.TryLocate(point,.72,out var from)||!map.TryTraverse(from,goal,.72,out _))continue;
                    int id=session.Crowd.Units.Count+1;session.Crowd.Add(id,point,.72,4,4.5);Assert.True(session.Crowd.SetRoute(id,goal,new[]{goal}));
                }
                Assert.Greater(session.Crowd.Units.Count,16);
                var bodies=session.Crowd.Units.Select(u=>new NavMotionBody{Id=u.Id,Position=u.Position,Target=u.Goal,Heading=u.Heading,Radius=u.Radius,Speed=u.Speed,TurnSpeed=u.TurnSpeed,Active=true}).ToArray();
                var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];
                NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,1d/30);
                var before=State();long bytes=session.PhysicalHostBudget.PhysicalBytes;byte[] expected=null;long smallCalls=-1,largeCalls=-1;
                foreach(bool largeMemo in new[]{false,true,true,false}){
                    NavCrowdMotion.ForceSmallClearanceMemoForTests=!largeMemo;
                    for(int row=0;row<bodies.Length;row++)NavCrowdMotion.Select(bodies,row,neighbors,counts[row],profile,1d/30,session.Crowd,bodies[row].Target);
                    NavCrowdMotion.CountSelectClearanceForQa=true;NavCrowdMotion.SelectClearanceCallsForQa=NavCrowdMotion.SelectClearanceHitsForQa=0;
                    var proposals=new NavMotionProposal[bodies.Length];var watch=Stopwatch.StartNew();
                    for(int repeat=0;repeat<12;repeat++)for(int row=0;row<bodies.Length;row++)proposals[row]=NavCrowdMotion.Select(bodies,row,neighbors,counts[row],profile,1d/30,session.Crowd,bodies[row].Target);
                    watch.Stop();TestContext.WriteLine("SELECT_MEMO_CONTROL large="+largeMemo+" selects="+(12*bodies.Length)+" ms="+watch.Elapsed.TotalMilliseconds+" queries="+NavCrowdMotion.SelectClearanceCallsForQa+" hits="+NavCrowdMotion.SelectClearanceHitsForQa);
                    Assert.Greater(NavCrowdMotion.SelectClearanceCallsForQa,0,"Actual Select must reach terrain clearance, not return only neighbor overflow waits.");
                    if(largeMemo){if(largeCalls>=0)Assert.AreEqual(largeCalls,NavCrowdMotion.SelectClearanceCallsForQa);largeCalls=NavCrowdMotion.SelectClearanceCallsForQa;}
                    else{if(smallCalls>=0)Assert.AreEqual(smallCalls,NavCrowdMotion.SelectClearanceCallsForQa);smallCalls=NavCrowdMotion.SelectClearanceCallsForQa;}
                    using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
                        foreach(var row in Enumerable.Range(0,bodies.Length)){
                            var p=proposals[row];
                            foreach(double v in new[]{p.Position.X,p.Position.Z,p.Velocity.X,p.Velocity.Z,p.Heading})writer.Write(BitConverter.DoubleToInt64Bits(v));
                            writer.Write(p.Blocked);writer.Write(p.Overflow);writer.Write(p.Refuse);writer.Write(p.StaticNoWinner);writer.Write(p.IndependentTurn);
                        }
                        var result=stream.ToArray();if(expected==null)expected=result;else CollectionAssert.AreEqual(expected,result);
                    }
                    CollectionAssert.AreEqual(before,State());Assert.AreEqual(bytes,session.PhysicalHostBudget.PhysicalBytes);
                }
                Assert.Less(largeCalls,smallCalls,"Only exact repeated clearance queries should disappear.");
            }finally{session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
        }
    }
}

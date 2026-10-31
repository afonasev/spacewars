using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdRankFirstTests
    {
        private static byte[] State(NavigationSession n){using(var state=n.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
        private static byte[] Proposal(NavMotionProposal p)
        {
            using(var s=new MemoryStream())using(var w=new BinaryWriter(s)){
                foreach(double value in new[]{p.Position.X,p.Position.Z,p.Velocity.X,p.Velocity.Z,p.Heading})w.Write(BitConverter.DoubleToInt64Bits(value));
                w.Write(p.Blocked);w.Write(p.Overflow);w.Write(p.Refuse);w.Write(p.StaticNoWinner);w.Write(p.IndependentTurn);return s.ToArray();
            }
        }
        [TestCase(false)][TestCase(true)] public void PureProductionRankingKeepsAllProposalBitsAndStateWhileSkippingQueries(bool crossings)
        {
            IPlayableTerrain map=crossings?(IPlayableTerrain)ThreeCrossingsMap.Default:new AiTestMap();var profile=PlayableProfile.Default.Navigation;
            var session=new NavigationSession(71,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),profile,terrain:map);
            bool previous=NavCrowdMotion.DisableRankingBeforeClearanceForTests,previousCount=NavCrowdMotion.CountSelectClearanceForQa;
            try{
                for(int x=0;x<8;x++)for(int z=0;z<5;z++){
                    var point=new NavPoint(-55+x*4.5,-20+z*4.5);var goal=new NavPoint(point.X+2,point.Z+.5);
                    if(!map.TryLocate(point,.72,out var from)||!map.TryTraverse(from,goal,.72,out _))continue;
                    int id=session.Crowd.Units.Count+1;session.Crowd.Add(id,point,.72,4,4.5,canYield:true);Assert.True(session.Crowd.SetRoute(id,goal,new[]{goal}));
                }
                Assert.Greater(session.Crowd.Units.Count,16);var units=session.Crowd.Units.ToArray();var before=State(session);long paid=session.PhysicalHostBudget.PhysicalBytes,totalOld=0,totalNew=0;
                for(int scenario=0;scenario<4;scenario++){
                    var bodies=units.Select(u=>new NavMotionBody{Id=u.Id,Position=u.Position,Target=scenario==2?new NavPoint(u.Position.X-1,u.Position.Z):u.Goal,Heading=scenario==1?Math.PI:scenario==3?-.7:.2,Radius=u.Radius,Speed=u.Speed,TurnSpeed=u.TurnSpeed,Active=true,Potential=true,ReverseDistance=scenario==2?3:0,ReverseSpeedRatio=.5,ReverseConeRadians=.4}).ToArray();
                    var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];var labels=new int[bodies.Length];var speeds=new double[bodies.Length];
                    NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,1d/30,labels);NavCrowdMotion.ConstrainOverflowComponents(bodies,neighbors,counts,labels,speeds,profile,1d/30);
                    for(int row=0;row<bodies.Length;row++){
                        NavCrowdMotion.CountSelectClearanceForQa=true;NavCrowdMotion.DisableRankingBeforeClearanceForTests=true;NavCrowdMotion.SelectClearanceCallsForQa=0;
                        var old=NavCrowdMotion.Select(bodies,row,neighbors,counts[row],profile,1d/30,session.Crowd,bodies[row].Target);totalOld+=NavCrowdMotion.SelectClearanceCallsForQa;
                        NavCrowdMotion.DisableRankingBeforeClearanceForTests=false;NavCrowdMotion.SelectClearanceCallsForQa=0;
                        var next=NavCrowdMotion.Select(bodies,row,neighbors,counts[row],profile,1d/30,session.Crowd,bodies[row].Target);totalNew+=NavCrowdMotion.SelectClearanceCallsForQa;
                        CollectionAssert.AreEqual(Proposal(old),Proposal(next),"scenario="+scenario+" row="+row);
                    }
                }
                Assert.Greater(totalOld,0);Assert.Greater(totalNew,0);Assert.Less(totalNew,totalOld);CollectionAssert.AreEqual(before,State(session));Assert.AreEqual(paid,session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
                TestContext.WriteLine("RANK_FIRST_QUERIES crossings="+crossings+" old="+totalOld+" new="+totalNew);
            }finally{NavCrowdMotion.DisableRankingBeforeClearanceForTests=previous;NavCrowdMotion.CountSelectClearanceForQa=previousCount;session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);}
        }
        [Test] public void EnclosureAndExtremeRiskInputsKeepEveryOldFlag()
        {
            bool previous=NavCrowdMotion.DisableRankingBeforeClearanceForTests;
            try{foreach(double extent in new[]{.72,144d}){
                var crowd=new NavCrowd(new NavGeometry(extent,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);crowd.Add(1,default,.72,4,4.5);
                double negativeZero=BitConverter.Int64BitsToDouble(long.MinValue),nan=BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000042UL));
                foreach(double value in new[]{0d,negativeZero,double.Epsilon,1e-150,1e150,double.MaxValue,double.PositiveInfinity,double.NegativeInfinity,nan}){
                    var bodies=new[]{new NavMotionBody{Id=1,Position=default,Target=new NavPoint(2,.5),Heading=0,Radius=.72,Speed=4,TurnSpeed=4.5,Active=true},new NavMotionBody{Id=2,Position=new NavPoint(value,-value),Target=new NavPoint(2,0),Radius=.72,Speed=4,Active=true}};
                    var neighbors=new int[2*NavCrowdMotion.NeighborCapacity];neighbors[0]=1;
                    NavCrowdMotion.DisableRankingBeforeClearanceForTests=true;var old=NavCrowdMotion.Select(bodies,0,neighbors,1,PlayableProfile.Default.Navigation,1d/30,crowd,bodies[0].Target);
                    NavCrowdMotion.DisableRankingBeforeClearanceForTests=false;var next=NavCrowdMotion.Select(bodies,0,neighbors,1,PlayableProfile.Default.Navigation,1d/30,crowd,bodies[0].Target);
                    CollectionAssert.AreEqual(Proposal(old),Proposal(next),"extent="+extent+" valuebits="+BitConverter.DoubleToInt64Bits(value));
                    if(extent==.72)Assert.True(next.Blocked,"No available future endpoint must retain enclosure");
                }
            }}finally{NavCrowdMotion.DisableRankingBeforeClearanceForTests=previous;}
        }
    }
}

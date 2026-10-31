using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdParallelSelectControlTests
    {
        [Test] public void FrozenOwnedCrowdRowsKeepExactProposalsAndSessionAcrossParallelEvaluation()
        {
            var map=ThreeCrossingsMap.Default;var profile=PlayableProfile.Default.Navigation;
            var session=new NavigationSession(71,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),profile,terrain:map);
            byte[] State(){using(var state=session.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
            try{
                for(int x=0;x<8;x++)for(int z=0;z<5;z++){
                    var point=new NavPoint(-55+x*4.5,-20+z*4.5);var goal=new NavPoint(point.X+2,point.Z);
                    if(!map.TryLocate(point,.72,out var location)||!map.TryTraverse(location,goal,.72,out _))continue;
                    int id=session.Crowd.Units.Count+1;session.Crowd.Add(id,point,.72,4,4.5);Assert.True(session.Crowd.SetRoute(id,goal,new[]{goal}));
                }
                Assert.Greater(session.Crowd.Units.Count,16);Assert.True(session.Crowd.StableSelectClearance);
                var bodies=session.Crowd.Units.Select(u=>new NavMotionBody{Id=u.Id,Position=u.Position,Target=u.Goal,Heading=u.Heading,Radius=u.Radius,Speed=u.Speed,TurnSpeed=u.TurnSpeed,Active=true}).ToArray();
                var neighbors=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];NavCrowdMotion.Neighbors(bodies,neighbors,counts,profile,1d/30);
                Assert.True(counts.Any(n=>n>=0));var proposals=new NavMotionProposal[bodies.Length];var options=new ParallelOptions{MaxDegreeOfParallelism=4};
                Action<int> select=row=>proposals[row]=NavCrowdMotion.Select(bodies,row,neighbors,counts[row],profile,1d/30,session.Crowd,bodies[row].Target);
                var stateBefore=State();long paid=session.PhysicalHostBudget.PhysicalBytes;byte[] expected=null;
                foreach(bool parallel in new[]{false,true,true,false}){
                    if(parallel)Parallel.For(0,bodies.Length,options,select);else for(int row=0;row<bodies.Length;row++)select(row);
                    var watch=Stopwatch.StartNew();
                    for(int repeat=0;repeat<24;repeat++){if(parallel)Parallel.For(0,bodies.Length,options,select);else for(int row=0;row<bodies.Length;row++)select(row);}
                    watch.Stop();using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
                        foreach(var p in proposals){foreach(double value in new[]{p.Position.X,p.Position.Z,p.Velocity.X,p.Velocity.Z,p.Heading})writer.Write(BitConverter.DoubleToInt64Bits(value));writer.Write(p.Blocked);writer.Write(p.Overflow);writer.Write(p.Refuse);writer.Write(p.StaticNoWinner);writer.Write(p.IndependentTurn);}
                        var bytes=stream.ToArray();if(expected==null)expected=bytes;else CollectionAssert.AreEqual(expected,bytes);
                    }
                    CollectionAssert.AreEqual(stateBefore,State());Assert.AreEqual(paid,session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
                    TestContext.WriteLine("PARALLEL_SELECT_CONTROL parallel="+parallel+" selects="+(24*bodies.Length)+" wall_ms="+watch.Elapsed.TotalMilliseconds+" processors="+Environment.ProcessorCount);
                }
            }finally{session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
        }
    }
}

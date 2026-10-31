using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdClearanceReuseTests
    {
        const double Dt=1d/30;
        static byte[] Bytes(NavigationSession s){using(var state=s.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
        static void Destroy(NavigationSession s){if(s==null)return;s.DestroyNavigationAuthority();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
        static NavGeometry Geometry(int scenario)=>new NavGeometry(144,scenario==2?new[]{new NavObstacle(81,-.4,81.2,.4)}:Array.Empty<NavObstacle>(),1);
        static NavigationSession Create(int count,int scenario)
        {
            var map=new AiTestMap();var g=Geometry(scenario);
            var s=new NavigationSession(1,g,PlayableProfile.Default.Navigation,terrain:map);
            for(int i=0;i<count;i++){
                double angle=(i/50)*Math.PI/4;int k=i%50;
                var p=new NavPoint(Math.Cos(angle)*80+(k%7)*2.2,Math.Sin(angle)*80+(k/7)*2.2);
                var u=s.Crowd.Add(i+1,p,.72,4,4.5,canYield:true);
                var goal=new NavPoint(p.X+20,p.Z+7);u.Heading=scenario%2==0?Math.PI:.2;
                if(scenario==1){goal=new NavPoint(p.X-1,p.Z);u.Heading=0;u.ReverseDistance=3;u.ReverseSpeedRatio=.5;u.ReverseConeRadians=.4;}
                Assert.True(s.Crowd.SetRoute(u.Id,goal,scenario==2?new[]{new NavPoint(p.X,p.Z+3),goal}:new[]{goal}));
                if(i%13==0)s.Crowd.Stop(u.Id,true);else if(i%17==0)s.Crowd.Stop(u.Id,false);
            }
            return s;
        }
        static void Switch(bool reuse){NavCrowdMotion.DisableSelectClearanceReuseForQa=!reuse;NavCrowdMotion.CountSelectClearanceForQa=true;NavCrowdMotion.SelectClearanceCallsForQa=NavCrowdMotion.SelectClearanceHitsForQa=0;}
        [TearDown] public void Reset(){NavCrowdMotion.DisableSelectClearanceReuseForQa=false;NavCrowdMotion.CountSelectClearanceForQa=false;}
        [Test] public void CapabilityIsClosedAndTestCallbacksDisableReuse()
        {
            var g=new NavGeometry(144,Array.Empty<NavObstacle>(),1);var p=PlayableProfile.Default.Navigation;
            Assert.True(new NavCrowd(g,p).StableSelectClearance);var a=new NavCrowd(g,p,new AiTestMap());Assert.True(a.StableSelectClearance);
            var crossings=new NavCrowd(g,p,new ThreeCrossingsMap(new ThreeCrossingsProfileData()));Assert.True(crossings.StableSelectClearance);
            Assert.False(new NavCrowd(g,p,new NullDockMap()).StableSelectClearance,"uncertified terrain remains an ordinary callback");
            var before=TerrainLocations.IndexedCutCandidateForTests;try{TerrainLocations.IndexedCutCandidateForTests=_=>{};Assert.False(a.StableSelectClearance);Assert.False(crossings.StableSelectClearance);}finally{TerrainLocations.IndexedCutCandidateForTests=before;}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)] public void ActualProductionCurrentSaveHoldCancelReverseAndNextStepAreExact(int scenario)
        {
            NavigationSession a=null,b=null,cold=null;try{
                a=Create(24,scenario);b=Create(24,scenario);CollectionAssert.AreEqual(Bytes(a),Bytes(b));
                for(int step=0;step<4;step++){
                    if(step==2){a.Crowd.Stop(2,true);b.Crowd.Stop(2,true);a.Crowd.Stop(3,false);b.Crowd.Stop(3,false);}
                    Switch(false);a.Crowd.Step(Dt);long calls=NavCrowdMotion.SelectClearanceCallsForQa;
                    Switch(true);b.Crowd.Step(Dt);CollectionAssert.AreEqual(Bytes(a),Bytes(b),"exact movement/cursors/HOLD/stop/current-format bytes step="+step);
                    Assert.Greater(NavCrowdMotion.SelectClearanceHitsForQa,0,"actual production identical endpoint calls exist");Assert.Less(NavCrowdMotion.SelectClearanceCallsForQa,calls);
                }
                var map=new AiTestMap();cold=new NavigationSession(1,Geometry(scenario),PlayableProfile.Default.Navigation,terrain:map);
                using(var state=b.CaptureOwnedState())cold.RestoreState(state);CollectionAssert.AreEqual(Bytes(b),Bytes(cold));
                Switch(true);b.Crowd.Step(Dt);cold.Crowd.Step(Dt);CollectionAssert.AreEqual(Bytes(b),Bytes(cold),"current-format cold nextstep exact");
            }finally{Destroy(cold);Destroy(b);Destroy(a);}
        }
        static byte[] ProposalBytes(NavMotionProposal p)
        {
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)){
                w.Write(BitConverter.DoubleToInt64Bits(p.Position.X));w.Write(BitConverter.DoubleToInt64Bits(p.Position.Z));w.Write(BitConverter.DoubleToInt64Bits(p.Velocity.X));w.Write(BitConverter.DoubleToInt64Bits(p.Velocity.Z));w.Write(BitConverter.DoubleToInt64Bits(p.Heading));
                w.Write(p.Blocked);w.Write(p.Overflow);w.Write(p.Refuse);w.Write(p.StaticNoWinner);w.Write(p.IndependentTurn);return stream.ToArray();
            }
        }
        [TestCase(0)][TestCase(1)][TestCase(2)] public void PureProductionProposalsAndSweepsAreBitExactBeforeJoint(int scenario)
        {
            NavigationSession s=null;try{
                s=Create(24,scenario);var before=Bytes(s);long physical=s.PhysicalHostBudget.PhysicalBytes;
                var units=s.Crowd.Units.ToArray();var bodies=units.Select(u=>new NavMotionBody{Id=u.Id,Position=u.Position,Target=u.route.Length>u.routeIndex?u.route[u.routeIndex]:u.Goal,Heading=u.Heading,Radius=u.Radius,Speed=u.Speed,TurnSpeed=u.TurnSpeed,ReverseDistance=u.ReverseDistance,ReverseSpeedRatio=u.ReverseSpeedRatio,ReverseConeRadians=u.ReverseConeRadians,Active=u.Moving&&!u.Held,Potential=!u.Held}).ToArray();
                var n=new int[bodies.Length*NavCrowdMotion.NeighborCapacity];var counts=new int[bodies.Length];var p=PlayableProfile.Default.Navigation;
                var labels=new int[bodies.Length];var speeds=new double[bodies.Length];
                NavCrowdMotion.Neighbors(bodies,n,counts,p,Dt,labels);NavCrowdMotion.ConstrainOverflowComponents(bodies,n,counts,labels,speeds,p,Dt);long hits=0;
                for(int row=0;row<bodies.Length;row++){
                    Switch(false);var a=NavCrowdMotion.Select(bodies,row,n,counts[row],p,Dt,s.Crowd,units[row].Goal);
                    Switch(true);var b=NavCrowdMotion.Select(bodies,row,n,counts[row],p,Dt,s.Crowd,units[row].Goal);hits+=NavCrowdMotion.SelectClearanceHitsForQa;
                    CollectionAssert.AreEqual(ProposalBytes(a),ProposalBytes(b),"production pre-Joint proposal bits/flags row="+row);
                    bool x=s.Crowd.Traverse(units[row].Location,a.Position,units[row].Radius,out var ax),y=s.Crowd.Traverse(units[row].Location,b.Position,units[row].Radius,out var bx);Assert.AreEqual(x,y);Assert.AreEqual(ax,bx);
                    Assert.AreEqual(Geometry(scenario).SegmentFree(units[row].Position,a.Position,units[row].Radius),Geometry(scenario).SegmentFree(units[row].Position,b.Position,units[row].Radius));
                }
                Assert.Greater(hits,0);CollectionAssert.AreEqual(before,Bytes(s),"queries/proposals have no persisted effect");Assert.AreEqual(physical,s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);
            }finally{Destroy(s);}
        }
        [Test,Timeout(180000)] public void Fixed400WholeMovementPredecessorCandidatePredecessor()
        {
            const int steps=8;var metrics=new List<string>();byte[] expected=null;string output=Environment.GetEnvironmentVariable("SPACEWARS_CLEARANCE_OUTPUT");if(output!=null)Directory.CreateDirectory(output);
            var cpu=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),typeof(PerformanceProfile).GetMethod("CpuNow",BindingFlags.Static|BindingFlags.NonPublic));
            for(int arm=0;arm<3;arm++){
                NavigationSession s=null;try{
                    s=Create(400,0);Switch(arm==1);s.Crowd.Step(Dt);byte[] start=Bytes(s);NavCrowdMotion.SelectClearanceCallsForQa=NavCrowdMotion.SelectClearanceHitsForQa=0;
                    long allocated=GC.GetAllocatedBytesForCurrentThread(),c=cpu(),t=Stopwatch.GetTimestamp();
                    for(int step=0;step<steps;step++)s.Crowd.Step(Dt);
                    long ticks=Stopwatch.GetTimestamp()-t,usedCpu=cpu()-c,used=GC.GetAllocatedBytesForCurrentThread()-allocated;var end=Bytes(s);
                    if(expected==null)expected=end;else CollectionAssert.AreEqual(expected,end,"whole actual movement current save exact A/B/A");
                    using(var sha=SHA256.Create()){
                        string line=string.Format(CultureInfo.InvariantCulture,"arm={0},reuse={1},actors=400,steps={2},wallMs={3:R},cpuMs={4:R},allocated={5},calls={6},hits={7},startSHA={8},finalSHA={9}",arm,arm==1,steps,ticks*1000d/Stopwatch.Frequency,usedCpu/1000000d,used,NavCrowdMotion.SelectClearanceCallsForQa,NavCrowdMotion.SelectClearanceHitsForQa,BitConverter.ToString(sha.ComputeHash(start)).Replace("-",""),BitConverter.ToString(sha.ComputeHash(end)).Replace("-",""));metrics.Add(line);TestContext.WriteLine(line);
                    }
                    if(output!=null){File.WriteAllBytes(Path.Combine(output,"arm"+arm+".before"),start);File.WriteAllBytes(Path.Combine(output,"arm"+arm+".after"),end);}
                    if(arm==1)Assert.Greater(NavCrowdMotion.SelectClearanceHitsForQa,0);
                }finally{Destroy(s);}
            }
            if(output!=null)File.WriteAllLines(Path.Combine(output,"metrics.txt"),metrics);
        }
    }
}

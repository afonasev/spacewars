using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test] public void DiagnosticReasonsAndCommitRefusalLeaveSavedWorkAndNumericGrantExact()
        {
            using(var fixture=new OwnedArrivalFixture(true,partition:true,occupants:SpatialRows(8),memberCount:4,flatCandidateCount:32)){
                var source=fixture.NewWork();NavigationArrivalWork plain=null,traced=null;
                try{
                    plain=Fork(source,fixture.Budget,20,w=>w.Advance(0,400,new NavEpochQuota(0,0,0,0,0)));
                    NavigationArrivalDiagnostics.Start(32);var trace=NavArrivalDiagnostic.Buffer;
                    trace.Begin(source,1,0,1,400);
                    traced=Fork(source,fixture.Budget,21,w=>w.Advance(0,400,new NavEpochQuota(0,0,0,0,0)));
                    trace.Proposed(traced,1,0,1,400);trace.Finish(traced,1,0,1,400,"committed");
                    Assert.Zero(trace.Dropped);CollectionAssert.AreEqual(CaptureGraph(fixture.Budget,plain),CaptureGraph(fixture.Budget,traced));Assert.AreEqual(plain.AllocationQuotaRemaining,traced.AllocationQuotaRemaining);
                    using(var stream=new MemoryStream()){trace.WriteTo(stream);using(var read=new BinaryReader(stream)){stream.Position=0;Assert.AreEqual(0x3144414e,read.ReadInt32());read.ReadInt32();read.ReadInt32();int count=read.ReadInt32();Assert.Zero(read.ReadInt32());read.ReadInt64();read.ReadBoolean();int reasons=read.ReadInt32();long sum=-1,prepare=0;
                        for(int n=0;n<count;n++){long start=stream.Position;int length=read.ReadInt32();int kind=read.ReadInt32();stream.Position=start+length-reasons*8;if(kind==3){sum=0;for(int k=0;k<reasons;k++){long v=read.ReadInt64();sum+=v;if(k==(int)NavCandidateCharge.PrepareValidity)prepare=v;}}stream.Position=start+length;}
                        Assert.AreEqual(traced.CandidateWorkConsumed-source.CandidateWorkConsumed,sum);Assert.Greater(prepare,0);Assert.AreEqual(stream.Length,stream.Position);
                    }}
                    int stopRequests=0;NavigationArrivalDiagnostics.Arm(()=>stopRequests++);trace.Begin(traced,1,1,2,1);trace.Transition("geometry-rebind",1,2,3,4,4);trace.Finish(traced,1,1,2,1,"source-owner-refused");Assert.Zero(stopRequests);Assert.False(trace.ShouldStop,"service selected before the event does not satisfy next-selected stop");
                    trace.Begin(traced,1,1,3,1);trace.Finish(traced,1,1,3,1,"scope-refused");Assert.True(trace.ShouldStop);Assert.AreEqual(1,stopRequests);Assert.Zero(trace.Dropped);
                }finally{NavigationArrivalDiagnostics.Stop();plain?.ReleaseOwner();traced?.ReleaseOwner();source.ReleaseOwner();}
            }
        }
        [Test] public void DiagnosticOverflowIsExplicitAndDoesNotModifyTheWork()
        {
            using(var fixture=new OwnedArrivalFixture(true,partition:true)){
                var work=fixture.NewWork();try{var bytes=CaptureGraph(fixture.Budget,work);NavigationArrivalDiagnostics.Start(1);var trace=NavArrivalDiagnostic.Buffer;trace.Begin(work,1,0,1,0);trace.Finish(work,1,0,1,0,"scope-refused");Assert.AreEqual(1,trace.Dropped);Assert.True(trace.ShouldStop);CollectionAssert.AreEqual(bytes,CaptureGraph(fixture.Budget,work));}finally{NavigationArrivalDiagnostics.Stop();work.ReleaseOwner();}
            }
        }
    }
}

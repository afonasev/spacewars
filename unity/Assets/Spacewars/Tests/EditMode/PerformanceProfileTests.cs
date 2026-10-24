using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class PerformanceProfileTests
    {
        [Test] public void NestedMeasurementsRemainInclusiveAndCountEveryCall()
        {
            var output=new StringWriter();var previous=Console.Out;Exception failure=null;
            try{Console.SetOut(output);var worker=new Thread(()=>{try{
                using(PerformanceProfile.Measure(ProfileStage.Ai)){
                    for(int i=0;i<3;i++)using(PerformanceProfile.Measure(ProfileStage.Observation)){}
                    using(PerformanceProfile.Measure(ProfileStage.Coverage)){}
                }
                PerformanceProfile.Flush();
            }catch(Exception ex){failure=ex;}});worker.Start();Assert.True(worker.Join(5000));}
            finally{Console.SetOut(previous);}
            Assert.Null(failure);
            if(!PerformanceProfile.Enabled){Assert.AreEqual("",output.ToString());return;}
            var text=output.ToString();StringAssert.Contains("stage=Ai calls=1",text);StringAssert.Contains("stage=Observation calls=3",text);StringAssert.Contains("stage=Coverage calls=1",text);
            Assert.AreEqual(3,text.Split(new[]{Environment.NewLine},StringSplitOptions.RemoveEmptyEntries).Length);
        }
        [Test] public void WorkCountersAggregateOnTheirOwnThreadWithoutTimingCalls()
        {
            var output=new StringWriter();var previous=Console.Out;Exception failure=null;
            try{Console.SetOut(output);var worker=new Thread(()=>{try{
                PerformanceProfile.Add(ProfileWork.OrdinaryConsumed,7);
                PerformanceProfile.Add(ProfileWork.OrdinaryConsumed,13);
                PerformanceProfile.Add(ProfileWork.ArrivalConsumed,3);
                PerformanceProfile.Flush();
            }catch(Exception ex){failure=ex;}});worker.Start();Assert.True(worker.Join(5000));}
            finally{Console.SetOut(previous);}
            Assert.Null(failure);
            if(!PerformanceProfile.Enabled){Assert.AreEqual("",output.ToString());return;}
            var text=output.ToString();StringAssert.Contains("work=OrdinaryConsumed total=20",text);StringAssert.Contains("work=ArrivalConsumed total=3",text);
            StringAssert.DoesNotContain("PRECISE_CPU",text);
            Assert.AreEqual(2,text.Split(new[]{Environment.NewLine},StringSplitOptions.RemoveEmptyEntries).Length);
        }
        [Test] public void SampledMeasurementsReportAttemptsSeparatelyFromMeasuredCalls()
        {
            var output=new StringWriter();var previous=Console.Out;Exception failure=null;
            try{Console.SetOut(output);var worker=new Thread(()=>{try{
                for(int i=0;i<1025;i++)using(PerformanceProfile.Sample(ProfileStage.ExactAdd)){}
                PerformanceProfile.Flush();
            }catch(Exception ex){failure=ex;}});worker.Start();Assert.True(worker.Join(5000));}
            finally{Console.SetOut(previous);}
            Assert.Null(failure);
            if(!PerformanceProfile.Enabled){Assert.AreEqual("",output.ToString());return;}
            var text=output.ToString();StringAssert.Contains("stage=ExactAdd calls=2",text);StringAssert.Contains("attempts=1025",text);
            Assert.AreEqual(1,text.Split(new[]{Environment.NewLine},StringSplitOptions.RemoveEmptyEntries).Length);
        }
        [Test] public void ReportMeasurementOverheadWithoutTimingAcceptanceThreshold()
        {
            var watch=Stopwatch.StartNew();for(int i=0;i<100000;i++)using(PerformanceProfile.Measure(ProfileStage.Observation)){}watch.Stop();
            TestContext.WriteLine("PROFILE_SCOPE_100K enabled="+PerformanceProfile.Enabled+" wall_ms="+watch.Elapsed.TotalMilliseconds);
            watch.Restart();for(int i=0;i<100000;i++)using(PerformanceProfile.Sample(ProfileStage.ExactCompare)){}watch.Stop();
            TestContext.WriteLine("PROFILE_SAMPLE_100K enabled="+PerformanceProfile.Enabled+" wall_ms="+watch.Elapsed.TotalMilliseconds);
        }
    }
}

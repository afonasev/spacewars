using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class EightPlayerRuntimeClockTests
    {
        [Test] public void ReportOrdinaryFourHundredUnitClockProgress()
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var setup=new NativeLobbyConfiguration{AiTestMap=true,HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            var runtime=PlayableRuntime.CreateEightPlayerPerformanceMatch(profile,71,setup,startPaused:true);
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            var scheduler=(AiAuthorityScheduler)typeof(PlayableRuntime).GetField("aiScheduler",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            var routes=new CertifiedRouteWorker(runtime.NavigationBinding,4,new CertifiedRouteKernel(),()=>runtime.IsStopRequested,runtime.ReportRouteFailure);
            try{
                Assert.AreEqual(400,runtime.DiagnosticGlobalUnits);Assert.AreEqual(8,scheduler.Owners.Count);
                var timer=Stopwatch.StartNew();long first=runtime.Latest.Tick,last=first;double previous=0;
                var gaps=new List<double>();var costs=new List<double>();runtime.RequestPause(false);
                while(timer.Elapsed.TotalSeconds<5&&!runtime.IsStopped){
                    var frame=runtime.Latest;
                    if(frame.Tick!=last){double now=timer.Elapsed.TotalMilliseconds;gaps.Add(now-previous);previous=now;last=frame.Tick;costs.Add(frame.Metrics.TickCpuMilliseconds);}
                    Thread.Sleep(1);
                }
                double elapsed=timer.Elapsed.TotalSeconds;long ticks=runtime.Latest.Tick-first;
                Assert.False(runtime.IsStopped,runtime.Latest.Failure);Assert.Greater(ticks,0);
                gaps.Sort();costs.Sort();double P(List<double> values,double q)=>values.Count==0?0:values[(int)Math.Floor((values.Count-1)*q)];
                TestContext.Out.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "ORDINARY_CLOCK owners=8 actors=400 seed=7 generation=71 wall_s={0:F4} ticks={1} sim_wall={2:F4} published_gap_p50_ms={3:F4} p95_ms={4:F4} p99_ms={5:F4} max_ms={6:F4} tick_cpu_p95_ms={7:F4} decision_ticks={8}",
                    elapsed,ticks,ticks/30d/elapsed,P(gaps,.5),P(gaps,.95),P(gaps,.99),P(gaps,1),P(costs,.95),string.Join(",",scheduler.Owners.Select(o=>o.Checkpoint.DecisionTick))));
                Assert.Zero(runtime.Latest.Metrics.Errors);
            }finally{
                runtime.RequestStop();routes.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&routes.IsStopped&&domain.ObservedPathWorkerStopped&&scheduler.ReviewWorkerStopped,15000));runtime.Dispose();domain.DestroyOwnedNavigation();
            }
        }
    }
}

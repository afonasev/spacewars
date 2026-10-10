using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CertifiedRouteOffloadTests
    {
        [Test] public void DelayedPreparationAllowsAtomicCaptureAndCancellation()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
            using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
                var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>{entered.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));};
                var work=Task.Run(()=>kernel.Pump(binding,4));
                try {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    var capture=Task.Run(()=>session.CaptureState());
                    Assert.True(capture.Wait(TimeSpan.FromSeconds(2)),"Capture must not wait for provider preparation.");
                    Assert.AreEqual(1,capture.Result.RequestMailboxIndices.Length);
                    var cancel=Task.Run(()=>session.Stop(1,true));
                    Assert.True(cancel.Wait(TimeSpan.FromSeconds(2)),"Authority must not wait for provider preparation.");
                }finally{release.Set();Assert.True(work.Wait(TimeSpan.FromSeconds(10)));kernel.Clear();}
                Assert.Zero(session.HostRetainedBytes);
                for(int i=0;i<4;i++)session.ApplyResults();
                Assert.Zero(session.AppliedResults);Assert.True(session.Crowd.Units[0].Held);
            }
        }
        [Test] public void FailedPreparationReleasesReservationAndPreservesOriginalRequest()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
            var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>throw new InvalidOperationException("injected provider failure");
            Assert.Throws<InvalidOperationException>(()=>kernel.Pump(binding,4));
            Assert.AreEqual(1,session.Requests.Count);Assert.Zero(session.RegisteredRouteCount);
            kernel.BeforePrepare=null;
            for(int i=0;i<40&&session.PendingCount>0;i++){kernel.Pump(binding,4);session.ApplyResults();}
            Assert.AreEqual(1,session.AppliedResults);Assert.LessOrEqual(kernel.RetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
            kernel.Clear();Assert.Zero(session.HostRetainedBytes);
        }
        [Test] public void WorkerProgressesWithoutRenderPumpAndResumesAfterAnswerBackpressure()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            var filler=new NavigationRequest(1,999,42,1,PlayableProfile.Default.Navigation,geometry,new NavPoint(0,0),new NavPoint(5,0));
            for(int i=0;i<NavMailbox<NavigationAnswer>.Capacity;i++)Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(filler,new[]{filler.Goal})));
            var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
            var worker=new CertifiedRouteWorker(binding,1);
            try {
                Assert.True(SpinWait.SpinUntil(()=>worker.IsWaiting&&worker.Publication.Pumps>0&&worker.Publication.Counters.PendingSubscriptions==0,5000));
                Assert.AreEqual(1,session.RegisteredRouteCount);
                Assert.AreEqual(NavMailbox<NavigationAnswer>.Capacity,session.Answers.Count);
                Assert.True(SpinWait.SpinUntil(()=>{session.ApplyResults();return session.AppliedResults==1;},5000));
                Assert.LessOrEqual(worker.Publication.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.Null(worker.Publication.Failure);
            }finally{worker.Dispose();Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));}
            Assert.Zero(session.HostRetainedBytes);
        }
        [Test] public void WorkerDisposeAndPublicationDoNotWaitForDelayedProvider()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
            using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
                var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>{entered.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));};
                var worker=new CertifiedRouteWorker(binding,1,kernel);
                try {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    var read=Task.Run(()=>worker.Publication);Assert.True(read.Wait(TimeSpan.FromSeconds(2)));
                    Assert.AreEqual(0,read.Result.Pumps);
                    var stop=Task.Run(()=>worker.Dispose());Assert.True(stop.Wait(TimeSpan.FromSeconds(2)));
                    Assert.False(worker.IsStopped,"Solver owner is still in the delayed operation.");
                }finally{worker.Dispose();release.Set();Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));}
                Assert.Zero(session.Answers.Count);Assert.Zero(session.HostRetainedBytes);
                Assert.AreEqual(1,session.CaptureState().RequestMailboxIndices.Length);
            }
        }
        [Test] public void FailedWorkerReportsFailureAndReleasesItsReservation()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
            var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>throw new InvalidOperationException("injected worker failure");
            int failures=0;var worker=new CertifiedRouteWorker(binding,1,kernel,failed:ex=>Interlocked.Increment(ref failures));
            try{Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));Assert.AreEqual(1,failures);StringAssert.Contains("injected worker failure",worker.Publication.Failure);Assert.Zero(session.HostRetainedBytes);Assert.AreEqual(1,session.CaptureState().RequestMailboxIndices.Length);}
            finally{worker.Dispose();}
        }
        [Test] public void DelayedProducerKernelDoesNotReintroduceMemberDeliveryBarrier()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var p=PlayableProfile.Default;
            var session=new NavigationSession(1,geometry,p.Navigation);session.Crowd.Add(1,new NavPoint(-5,0));
            Assert.NotNull(session.Probe(7,1,p.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));
            var binding=new PlayableRouteBinding(geometry,p,session.Admission,session.RoutePort);
            using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
                var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>{entered.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));};
                var worker=new CertifiedRouteWorker(binding,4,kernel);
                try{
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));session.PrepareDeliveryBarrier();
                    Assert.True(session.DeliveryBarrierReady,"Existing producer-only gameplay progression survives reservation.");
                    Assert.True(session.Move(1,new NavPoint(5,0)));session.PrepareDeliveryBarrier();
                    Assert.False(session.DeliveryBarrierReady,"A pending member still freezes the current step.");
                }finally{worker.Dispose();release.Set();Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));}
            }
        }
        [Test] public void BoundedAllFrameTelemetryReportsOverflowAndDrainsOnItsOwner()
        {
            string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"spacewars-route-frames-"+Guid.NewGuid()+".csv");
            using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
                var sink=new NativeRouteFrameTelemetry(path,8,()=>{entered.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));});
                try{
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    for(int i=0;i<100;i++)Assert.AreEqual(i<8,sink.TryWrite(new NativeRouteFrameSample{Frame=i,Tick=7,Generation=1}));
                    Assert.AreEqual(92,sink.Dropped);sink.Dispose();Assert.False(sink.IsStopped);
                    release.Set();Assert.True(SpinWait.SpinUntil(()=>sink.IsStopped,5000));Assert.Null(sink.Failure);
                    var lines=System.IO.File.ReadAllLines(path);Assert.AreEqual(10,lines.Length);StringAssert.Contains("dropped=92",lines[9]);
                    for(int i=0;i<8;i++)Assert.AreEqual(i.ToString(),lines[i+1].Split(',')[1]);
                }finally{sink.Dispose();release.Set();Assert.True(SpinWait.SpinUntil(()=>sink.IsStopped,5000));if(System.IO.File.Exists(path))System.IO.File.Delete(path);}
            }
        }
        [Test] public void FailedLifetimeCleanupDoesNotOpenLaneForFreshComputation()
        {
            var lane=new CertifiedRouteLane();using(var wake=new AutoResetEvent(false)){
                Assert.True(lane.Acquire(wake,()=>false));lane.Release(new InvalidOperationException("injected cleanup failure"));
            }
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var p=PlayableProfile.Default;
            var session=new NavigationSession(2,geometry,p.Navigation);session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            int entered=0,failures=0;var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>Interlocked.Increment(ref entered);
            var worker=new CertifiedRouteWorker(new PlayableRouteBinding(geometry,p,session.Admission,session.RoutePort),4,kernel,failed:ex=>Interlocked.Increment(ref failures),lane:lane);
            try{Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));Assert.Zero(entered);Assert.AreEqual(1,failures);StringAssert.Contains("cleanup failure",worker.Publication.Failure);Assert.Zero(session.HostRetainedBytes);Assert.AreEqual(1,session.Requests.Count);Assert.Zero(session.Answers.Count);}
            finally{worker.Dispose();}
        }
        [TestCase(false)] [TestCase(true)]
        public void SharedHostLaneWaitsForRetiredCleanupAndSkipsCancelledGeneration(bool failOld)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var p=PlayableProfile.Default;
            var lane=new CertifiedRouteLane();
            var old=new NavigationSession(1,geometry,p.Navigation);
            var cancelled=new NavigationSession(2,geometry,p.Navigation);
            var fresh=new NavigationSession(3,geometry,p.Navigation);
            foreach(var session in new[]{old,cancelled,fresh}){session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));}
            using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
                var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>{entered.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));if(failOld)throw new InvalidOperationException("retired failure");};
                int cancelledEntered=0,freshEntered=0,oldFailures=0,newFailures=0;
                var first=new CertifiedRouteWorker(new PlayableRouteBinding(geometry,p,old.Admission,old.RoutePort),4,kernel,failed:ex=>{Interlocked.Increment(ref oldFailures);throw new InvalidOperationException("callback failure");},lane:lane);
                CertifiedRouteWorker skipped=null,next=null;
                try{
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));first.Dispose();
                    var skippedKernel=new CertifiedRouteKernel();skippedKernel.BeforePrepare=()=>Interlocked.Increment(ref cancelledEntered);
                    skipped=new CertifiedRouteWorker(new PlayableRouteBinding(geometry,p,cancelled.Admission,cancelled.RoutePort),4,skippedKernel,lane:lane);
                    Assert.True(SpinWait.SpinUntil(()=>skipped.IsWaiting,5000));skipped.Dispose();
                    Assert.True(SpinWait.SpinUntil(()=>skipped.IsStopped,5000));Assert.Zero(cancelledEntered);
                    var nextKernel=new CertifiedRouteKernel();nextKernel.BeforePrepare=()=>{Assert.Zero(old.HostRetainedBytes);Interlocked.Increment(ref freshEntered);};
                    next=new CertifiedRouteWorker(new PlayableRouteBinding(geometry,p,fresh.Admission,fresh.RoutePort),4,nextKernel,failed:ex=>Interlocked.Increment(ref newFailures),lane:lane);
                    Assert.True(SpinWait.SpinUntil(()=>next.IsWaiting,5000));Assert.Zero(next.Publication.Pumps);Assert.Zero(freshEntered);
                    Assert.False(first.IsStopped);release.Set();
                    Assert.True(SpinWait.SpinUntil(()=>{fresh.ApplyResults();return fresh.AppliedResults==1;},5000));
                    Assert.True(SpinWait.SpinUntil(()=>first.IsStopped,5000));Assert.Greater(freshEntered,0);
                    Assert.AreEqual(failOld?1:0,oldFailures);Assert.Zero(newFailures);
                    Assert.Zero(old.Answers.Count);Assert.Zero(cancelled.Answers.Count);Assert.Zero(old.HostRetainedBytes);
                    Assert.AreEqual(1,fresh.AppliedResults);Assert.Zero(fresh.RejectedResults);
                }finally{first.Dispose();skipped?.Dispose();next?.Dispose();release.Set();Assert.True(SpinWait.SpinUntil(()=>first.IsStopped&&(skipped==null||skipped.IsStopped)&&(next==null||next.IsStopped),5000));}
            }
        }
        [Test] public void StoppedDelayedGenerationCannotWriteIntoFreshWorkerGeneration()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var p=PlayableProfile.Default;
            var old=new NavigationSession(1,geometry,p.Navigation);var fresh=new NavigationSession(2,geometry,p.Navigation);
            old.Crowd.Add(1,new NavPoint(-5,0));fresh.Crowd.Add(1,new NavPoint(-5,0));
            Assert.True(old.Move(1,new NavPoint(5,0)));Assert.True(fresh.Move(1,new NavPoint(7,0)));
            using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
                var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>{entered.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));};
                var first=new CertifiedRouteWorker(new PlayableRouteBinding(geometry,p,old.Admission,old.RoutePort),4,kernel);
                CertifiedRouteWorker next=null;
                try{
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));first.Dispose();
                    next=new CertifiedRouteWorker(new PlayableRouteBinding(geometry,p,fresh.Admission,fresh.RoutePort),4);
                    Assert.True(SpinWait.SpinUntil(()=>{fresh.ApplyResults();return fresh.AppliedResults==1;},5000));
                    release.Set();Assert.True(SpinWait.SpinUntil(()=>first.IsStopped,5000));
                    Assert.Zero(old.Answers.Count);Assert.AreEqual(new NavPoint(7,0),fresh.Crowd.Units[0].Goal);
                    Assert.AreEqual(1,fresh.AppliedResults);Assert.Zero(fresh.RejectedResults);
                }finally{first.Dispose();next?.Dispose();release.Set();Assert.True(SpinWait.SpinUntil(()=>first.IsStopped&&(next==null||next.IsStopped),5000));}
            }
        }
    }
}

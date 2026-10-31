using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class RouteCaptureExclusionTests
    {
        private static bool Until(Func<bool> value)=>SpinWait.SpinUntil(value,5000);
        private static byte[] Bytes(NavigationSession session)
        {using(var capture=session.CaptureOwnedState())using(var bytes=WorldWire.PackOwned(w=>WorldWire.Write(w,capture),capture.CaptureBudget))return bytes.TransferPublic();}
        [TestCase(false)][TestCase(true)] public void NonblockingResultYieldPreservesFifoAndCanBeAcknowledgedOrCancelled(bool cancel)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var port=session.RoutePort;bool operating=false;
            try{
                session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));Assert.True(session.Requests.TryDequeue(out var request));
                var answer=new NavigationAnswer(request,new[]{request.Goal});Assert.True(session.Answers.TryEnqueue(answer));
                Assert.True(port.TryBeginHostOperation());operating=true;
                Assert.Null(port.TryAcquireResultPreparation(()=>false));Assert.True(port.CaptureRequested);Assert.False(port.TryBeginHostOperation());
                CollectionAssert.AreEqual(new[]{answer},session.Answers.CopyItems());
                if(cancel){Assert.Throws<OperationCanceledException>(()=>port.TryAcquireResultPreparation(()=>true));Assert.False(port.CaptureRequested);}
                port.EndHostOperation();operating=false;
                using(port.TryAcquireResultPreparation(()=>false)){Assert.True(port.CaptureRequested);Assert.False(port.TryBeginHostOperation());CollectionAssert.AreEqual(new[]{answer},session.Answers.CopyItems());}
                Assert.False(port.CaptureRequested);Assert.True(port.TryBeginHostOperation());port.EndHostOperation();
            }finally{port.CancelResultPreparation();if(operating)port.EndHostOperation();session.DestroyNavigationAuthority();}
        }
        [Test] public void WorkerCleanupCanPassPendingResultYieldButCannotPassExactCapture()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var port=session.RoutePort;
            try{
                session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));Assert.True(session.Requests.TryDequeue(out var request));Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal})));
                Assert.True(port.TryBeginHostOperation());Assert.Null(port.TryAcquireResultPreparation(()=>false));port.EndHostOperation();
                Assert.False(port.TryBeginHostOperation());Assert.True(port.TryBeginHostOperation(cleanup:true));port.EndHostOperation();
                using(port.AcquireCapture(()=>false))Assert.False(port.TryBeginHostOperation(cleanup:true));
            }finally{port.CancelResultPreparation();session.DestroyNavigationAuthority();}
        }
        [Test] public void ResultPreparationWithoutActualAnswersDoesNotExcludeHost()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            try{Assert.Null(session.RoutePort.AcquireResultPreparation(()=>false));Assert.False(session.RoutePort.CaptureRequested);Assert.True(session.RoutePort.TryBeginHostOperation());session.RoutePort.EndHostOperation();}
            finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
        }
        [TestCase(false)][TestCase(true)] public void ResultPreparationCancellationOrHostFailureReleasesExclusionAndPreservesReadyFifo(bool cancel)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var port=session.RoutePort;var kernel=new CertifiedRouteKernel();int stopping=0;bool hostOperation=false;Task<IDisposable> waiting=null;
            try{
                session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
                for(int i=0;i<20000&&session.Answers.Count==0;i++)kernel.Pump(new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,port),4);
                var answers=session.Answers.CopyItems();Assert.AreEqual(1,answers.Length);var prefix=Bytes(session);
                Assert.True(port.TryBeginHostOperation());hostOperation=true;
                waiting=Task.Run(()=>port.AcquireResultPreparation(()=>Volatile.Read(ref stopping)!=0));Assert.True(Until(()=>port.CaptureRequested));Assert.False(waiting.IsCompleted);
                if(cancel)Volatile.Write(ref stopping,1);else port.ReportHostFailure(new InvalidOperationException("injected result preparation host failure"));
                Assert.True(Until(()=>waiting.IsCompleted));Assert.True(waiting.IsFaulted);Assert.False(port.CaptureRequested);
                if(cancel)Assert.IsInstanceOf<OperationCanceledException>(waiting.Exception.InnerException);else StringAssert.Contains("injected result preparation host failure",waiting.Exception.InnerException.InnerException.Message);
                CollectionAssert.AreEqual(answers,session.Answers.CopyItems());CollectionAssert.AreEqual(prefix,Bytes(session));
                port.EndHostOperation();hostOperation=false;Assert.True(port.TryBeginHostOperation());port.EndHostOperation();
            }finally{Volatile.Write(ref stopping,1);if(hostOperation)port.EndHostOperation();if(waiting!=null){Assert.True(Until(()=>waiting.IsCompleted));if(waiting.Status==TaskStatus.RanToCompletion)waiting.Result?.Dispose();}kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
        }
        [TestCase(false)][TestCase(true)] public void CaptureBeforeFirstWorkerPreventsAttachIncludingLaneWait(bool occupiedLane)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var port=session.RoutePort;
            var lane=new CertifiedRouteLane();using var laneWake=new AutoResetEvent(false);if(occupiedLane)Assert.True(lane.Acquire(laneWake,()=>false));
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            var held=port.AcquireCapture(()=>false);var worker=new CertifiedRouteWorker(new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,port),1,lane:lane);
            try{
                Assert.True(Until(()=>worker.IsWaiting));Assert.Zero(worker.Publication.Pumps);Assert.False(port.TryBeginHostOperation());
                held.Dispose();if(occupiedLane){Assert.Zero(worker.Publication.Pumps);lane.Release(null);}
                Assert.True(Until(()=>{session.ApplyResults();return session.AppliedResults==1;}));Assert.Null(worker.Publication.Failure);
                Assert.True(Until(()=>worker.IsWaiting));using(port.AcquireCapture(()=>false)){long pumps=worker.Publication.Pumps;Assert.False(port.TryBeginHostOperation());Assert.AreEqual(pumps,worker.Publication.Pumps);}
            }finally{held.Dispose();worker.Dispose();if(occupiedLane&&worker.Publication.Pumps==0)lane.Release(null);Assert.True(Until(()=>worker.IsStopped));session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
        }
        [Test] public void CaptureWaitsForPhysicalBoundaryAndPublicationKeepsColdSuffix()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var port=session.RoutePort;
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var kernel=new CertifiedRouteKernel();int once=0;
            kernel.PauseAtPhysicalBoundary=()=>{if(kernel.CapturePumpFrontier().Stage==CertifiedRouteKernel.PumpStage.Transfer&&Interlocked.Exchange(ref once,1)==0){entered.Set();Assert.True(release.Wait(10000));}return false;};
            var worker=new CertifiedRouteWorker(new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,port),4,kernel);Task<IDisposable> capture=null;IDisposable held=null;
            try{
                Assert.True(entered.Wait(5000));capture=Task.Run(()=>port.AcquireCapture(()=>false));Assert.True(Until(()=>port.CaptureRequested));Assert.False(capture.IsCompleted,"Physical operation still owns publication");release.Set();Assert.True(capture.Wait(5000));held=capture.Result;
                Assert.Greater(worker.Publication.Pumps,0,"Acknowledgment follows Pump and immutable publication");Assert.AreEqual(CertifiedRouteKernel.PumpStage.Transfer,kernel.CapturePumpFrontier().Stage);Assert.AreEqual(4,kernel.CapturePumpFrontier().OriginalBudget);Assert.False(port.TryBeginHostOperation());
                var bytes=Bytes(session);var cold=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
                try{cold.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));CollectionAssert.AreEqual(bytes,Bytes(cold));}finally{cold.ReleaseCommittedHostRoot();cold.DestroyNavigationAuthority();}
                held.Dispose();held=null;Assert.True(Until(()=>{session.ApplyResults();return session.AppliedResults==1;}));Assert.Null(worker.Publication.Failure);
            }finally{release.Set();if(held==null&&capture?.Status==TaskStatus.RanToCompletion)capture.Result.Dispose();held?.Dispose();worker.Dispose();Assert.True(Until(()=>worker.IsStopped));session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
        }
        [TestCase(false)][TestCase(true)] public void RuntimeFailureOrStopReleasesCaptureBeforeTaskCompletion(bool stop)
        {
            var runtime=new PlayableRuntime(OfflineParticipantAuthorityTests.Config(2,false),71,startPaused:true);var port=runtime.NavigationBinding.RoutePort;
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            runtime.BeforeCaptureSerialization=()=>{entered.Set();Assert.True(release.Wait(10000));if(!stop)throw new InvalidOperationException("injected serialization failure");};
            try{
                var capture=runtime.RequestCaptureBytes();Assert.True(entered.Wait(5000));Assert.True(port.CaptureRequested);Assert.False(port.TryBeginHostOperation());if(stop)runtime.RequestStop();release.Set();
                Assert.True(Until(()=>capture.IsCompleted));Assert.False(port.CaptureRequested);Assert.True(stop?capture.IsCanceled:capture.IsFaulted);
                Assert.True(port.TryBeginHostOperation());port.EndHostOperation();
            }finally{release.Set();runtime.BeforeCaptureSerialization=null;runtime.Dispose();Assert.True(Until(()=>runtime.IsStopped));}
        }
        [Test] public void CancelledCaptureWaitAndWorkerFailureCannotLeakBarrier()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var port=session.RoutePort;int cancel=0;
            Assert.True(port.TryBeginHostOperation());var pending=Task.Run(()=>port.AcquireCapture(()=>Volatile.Read(ref cancel)!=0));Assert.True(Until(()=>port.CaptureRequested));Volatile.Write(ref cancel,1);Assert.True(Until(()=>pending.IsCompleted));Assert.True(pending.IsFaulted);Assert.IsInstanceOf<OperationCanceledException>(pending.Exception.InnerException);Assert.False(port.CaptureRequested);port.EndHostOperation();
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>{entered.Set();Assert.True(release.Wait(10000));throw new InvalidOperationException("injected worker failure");};
            var worker=new CertifiedRouteWorker(new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,port),1,kernel);
            try{Assert.True(entered.Wait(5000));var capture=Task.Run(()=>port.AcquireCapture(()=>false));Assert.True(Until(()=>port.CaptureRequested));release.Set();Assert.True(Until(()=>capture.IsCompleted));Assert.True(capture.IsFaulted);Assert.False(port.CaptureRequested);Assert.True(Until(()=>worker.IsStopped));StringAssert.Contains("injected worker failure",worker.Publication.Failure);}
            finally{release.Set();worker.Dispose();Assert.True(Until(()=>worker.IsStopped));session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
        }
    }
}

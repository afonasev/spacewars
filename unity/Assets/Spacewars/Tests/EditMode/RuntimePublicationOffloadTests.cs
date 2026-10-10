using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class RuntimePublicationOffloadTests
    {
        private static bool Until(Func<bool> condition)=>SpinWait.SpinUntil(condition,5000);
        private static PlayableRuntime Held(OfflineMatchConfiguration config)
        {
            var runtime=new PlayableRuntime(config,71,startPaused:true);
            Assert.True(Until(()=>runtime.Latest.Status==RuntimeStatus.Paused));
            var owner=config.Roster[0].Id;var view=runtime.ParticipantView(owner);
            int entity=view.Entities.Single(e=>e.Owner==view.Owner).Id;
            runtime.RequestPause(false);Assert.True(Until(()=>!runtime.Latest.Paused));
            Assert.True(runtime.TrySubmit(new PlayableCommand(71,1,owner,PlayableCommandKind.Move,new[]{entity},new NavPoint(-8,8))).Accepted);
            Assert.True(Until(()=>runtime.Latest.Metrics.NavigationPending>0&&runtime.BarrierPayloadReuses>5));return runtime;
        }
        private static void Coherent(PlayableRuntime runtime,OfflineMatchConfiguration config)
        {
            var frame=runtime.OfflineFrame;var spectator=runtime.SpectatorFrame("spectator-99");
            Assert.AreEqual(frame.Sequence,spectator.Overview.Sequence);Assert.AreEqual(frame.Tick,spectator.Overview.Tick);
            foreach(var view in frame.Views.Values){Assert.AreEqual(frame.Sequence,view.Sequence);Assert.AreEqual(frame.Tick,view.Tick);Assert.AreEqual(spectator.Overview.Paused,view.Paused);Assert.AreEqual(spectator.Overview.Status,view.Status);Assert.True(view.Buildings.Where(b=>b.Owner!=view.Owner).All(b=>b.PrivateState==null));}
        }
        [Test] public void IdenticalBarrierPollsReuseOwnerAndSpectatorPayloadsButSpeedAndCapturePublish()
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);var runtime=Held(config);
            try {
                long polls=runtime.BarrierPolls;var latest=runtime.Latest;var owners=runtime.OfflineFrame;var spectator=runtime.SpectatorFrame("spectator-99");
                Assert.True(Until(()=>runtime.BarrierPolls>=polls+10));
                Assert.AreSame(latest,runtime.Latest);Assert.AreSame(owners,runtime.OfflineFrame);Assert.AreSame(spectator,runtime.SpectatorFrame("spectator-99"));Coherent(runtime,config);
                foreach(double speed in new[]{.5,1,2,4}){
                    long prior=runtime.Latest.Sequence;runtime.SetSpectatorSpeed("spectator-99",speed);
                    Assert.True(Until(()=>runtime.Latest.Sequence>prior));Assert.AreEqual(latest.Tick,runtime.Latest.Tick);Coherent(runtime,config);
                }
                long sequence=runtime.Latest.Sequence;var captured=runtime.RequestCaptureBytes();Assert.True(captured.Wait(5000));
                Assert.True(Until(()=>runtime.Latest.Sequence>sequence));Assert.Greater(captured.Result.Length,0);Coherent(runtime,config);
                var restored=PlayableRuntime.RestoreBytes(captured.Result,config);
                try{Assert.True(Until(()=>restored.BarrierPayloadReuses>5));Assert.AreEqual(runtime.Latest.Tick,restored.Latest.Tick);Coherent(restored,config);}
                finally{restored.Dispose();Assert.True(Until(()=>restored.IsStopped));}
            }finally{runtime.Dispose();Assert.True(Until(()=>runtime.IsStopped));}
        }
        [Test] public void SameTickPauseProfileIngressAndTerminalReceiptsStayObservable()
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);var runtime=Held(config);
            try {
                long tick=runtime.Latest.Tick,prior=runtime.Latest.Sequence;
                runtime.RequestPause(true);Assert.True(Until(()=>runtime.Latest.Paused));Assert.AreEqual(tick,runtime.Latest.Tick);Coherent(runtime,config);
                runtime.RequestPause(false);Assert.True(Until(()=>!runtime.Latest.Paused&&runtime.Latest.Sequence>prior));Assert.AreEqual(tick,runtime.Latest.Tick);
                prior=runtime.Latest.Sequence;Assert.Null(runtime.RequestBalance(config.Profile,71,runtime.Latest.ProfileRevision));
                Assert.True(Until(()=>runtime.Latest.Sequence>prior));Assert.AreEqual(tick,runtime.Latest.Tick);Coherent(runtime,config);
                var owner=config.Roster[0].Id;Assert.True(runtime.TrySubmit(new PlayableCommand(71,2,owner,PlayableCommandKind.Stop,Array.Empty<int>())).Accepted);
                runtime.RequestStop();Assert.True(Until(()=>runtime.IsStopped));Assert.AreEqual(RuntimeStatus.Stopped,runtime.Latest.Status);Coherent(runtime,config);
                Assert.True(runtime.OfflineFrame.Receipts.Any(r=>r.Receipt.OwnerId==owner&&r.Receipt.Sequence==1&&r.Receipt.Status==PlayableCommandStatus.Applied));
                Assert.True(runtime.OfflineFrame.Receipts.Any(r=>r.Receipt.OwnerId==owner&&r.Receipt.Sequence==2&&r.Receipt.Status==PlayableCommandStatus.Cancelled));
            }finally{runtime.Dispose();Assert.True(Until(()=>runtime.IsStopped));}
        }
        [Test] public void RouteFailureProducesTerminalFailedFrameInsteadOfAnEndlessBarrier()
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);var runtime=Held(config);
            try{runtime.ReportRouteFailure(new InvalidOperationException("injected route failure"));Assert.True(Until(()=>runtime.IsStopped));Assert.AreEqual(RuntimeStatus.Failed,runtime.Latest.Status);StringAssert.Contains("injected route failure",runtime.Latest.Failure);Coherent(runtime,config);}
            finally{runtime.Dispose();}
        }
        [Test] public void TerminalAuthorityFailureWakesIdleRouteOwnerWithoutRenderDisposal()
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);
            var runtime=new PlayableRuntime(config,71,startPaused:true);
            var worker=new CertifiedRouteWorker(runtime.NavigationBinding,4,()=>runtime.IsStopRequested,runtime.ReportRouteFailure);
            try{
                Assert.True(Until(()=>runtime.Latest.Status==RuntimeStatus.Paused&&worker.IsWaiting));
                runtime.ReportRouteFailure(new InvalidOperationException("authority terminal failure"));
                Assert.True(Until(()=>runtime.IsStopped&&worker.IsStopped));
                Assert.AreEqual(RuntimeStatus.Failed,runtime.Latest.Status);Coherent(runtime,config);
                Assert.Zero(worker.Publication.Pumps);Assert.Zero(worker.Publication.RetainedBytes);
            }finally{runtime.Dispose();worker.Dispose();Assert.True(Until(()=>runtime.IsStopped&&worker.IsStopped));}
        }
        [Test] public void CaptureEncodingDoesNotHoldRenderIngressAndKeepsCapturedWatermarks()
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);var runtime=Held(config);
            using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
                runtime.BeforeCaptureSerialization=()=>{entered.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));};
                try{
                    var capture=runtime.RequestCaptureBytes();Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    var owner=config.Roster[0].Id;
                    var submit=System.Threading.Tasks.Task.Run(()=>runtime.TrySubmit(new PlayableCommand(71,2,owner,PlayableCommandKind.Stop,Array.Empty<int>())));
                    Assert.True(submit.Wait(TimeSpan.FromSeconds(2)),"Render ingress must not wait for encoding.");Assert.True(submit.Result.Accepted);
                    var drain=System.Threading.Tasks.Task.Run(()=>runtime.DrainReceipts());Assert.True(drain.Wait(TimeSpan.FromSeconds(2)),"Render receipt getter must not wait for encoding.");
                    release.Set();Assert.True(capture.Wait(5000));
                    var restored=PlayableRuntime.RestoreBytes(capture.Result,config);
                    try{
                        // Sequence2 was accepted after the capture boundary, so it
                        // must still be fresh in the restored runtime.
                        Assert.True(restored.TrySubmit(new PlayableCommand(71,2,owner,PlayableCommandKind.Stop,Array.Empty<int>())).Accepted);
                    }finally{restored.Dispose();Assert.True(Until(()=>restored.IsStopped));}
                }finally{release.Set();runtime.BeforeCaptureSerialization=null;runtime.Dispose();Assert.True(Until(()=>runtime.IsStopped));}
            }
        }
    }
}

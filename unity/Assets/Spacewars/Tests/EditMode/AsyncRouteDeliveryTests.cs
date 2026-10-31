using System;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class AsyncRouteDeliveryTests
    {
        [Test] public void ObservedLaneBoundsTransportWithoutStarvingLargerObservation()
        {
            var lane=new ObservedPathLane();using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            var query=new ObservedPathWork(PlayableOwner.Player,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation,.5,2,2,new NavPoint(-5,0),new NavPoint(5,0));
            try{
                Assert.False(lane.TryGet(query,1,2,PlayableRouteTargetKind.VisibleEnemy,0,out _,()=>{entered.Set();Assert.True(release.Wait(5000));}));Assert.True(entered.Wait(5000));
                for(int actor=2;actor<=ObservedPathLane.Capacity;actor++)Assert.False(lane.TryGet(query,actor,2,PlayableRouteTargetKind.VisibleEnemy,0,out _));
                Assert.False(lane.TryGet(query,ObservedPathLane.Capacity+1,2,PlayableRouteTargetKind.VisibleEnemy,0,out _));Assert.AreEqual(ObservedPathLane.Capacity,lane.Count);Assert.AreEqual(ObservedPathLane.Capacity+1,lane.RetainedCount);
                release.Set();lane.DrainForCapture(()=>false);
                for(int actor=1;actor<=ObservedPathLane.Capacity+1;actor++){Assert.True(lane.TryGet(query,actor,2,PlayableRouteTargetKind.VisibleEnemy,0,out var path));Assert.IsNotEmpty(path);}
                Assert.Zero(lane.Count);
                lane.BeginProjection(PlayableOwner.Player);Assert.True(lane.TryGet(query,1,2,PlayableRouteTargetKind.VisibleEnemy,0,out _));lane.EndProjection(PlayableOwner.Player);
                Assert.AreEqual(1,lane.RetainedCount,"Only the current observation working set remains retained.");
            }finally{release.Set();lane.Stop();Assert.True(SpinWait.SpinUntil(()=>lane.IsStopped,5000));}
        }
        [Test] public void ObservedLaneReportsWorkerFaultInsteadOfUnreachablePath()
        {
            var lane=new ObservedPathLane();var query=new ObservedPathWork(PlayableOwner.Player,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation,.5,2,2,new NavPoint(-5,0),new NavPoint(5,0));
            try{
                Assert.False(lane.TryGet(query,1,2,PlayableRouteTargetKind.VisibleEnemy,0,out _,()=>throw new InvalidOperationException("Observed test fault")));
                Assert.True(SpinWait.SpinUntil(()=>lane.IsStopped,5000));
                var error=Assert.Throws<InvalidOperationException>(()=>lane.TryGet(query,1,2,PlayableRouteTargetKind.VisibleEnemy,0,out _));StringAssert.Contains("Observed test fault",error.InnerException.Message);
            }finally{lane.Stop();Assert.True(SpinWait.SpinUntil(()=>lane.IsStopped,5000));}
        }
        [Test] public void ObservedLanePreservesSettledAnswersExactlyAndRebindsMovingEndpoints()
        {
            var lane=new ObservedPathLane();ObservedPathLane restored=null;
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var profile=PlayableProfile.Default.Navigation;
            var query=new ObservedPathWork(PlayableOwner.Player,geometry,profile,.5,2,2,new NavPoint(-5,0),new NavPoint(5,0));
            byte[] Capture(ObservedPathLane source){using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);source.WriteState(writer);return stream.ToArray();}
            try{
                Assert.False(lane.TryGet(query,1,2,PlayableRouteTargetKind.VisibleEnemy,0,out _));lane.DrainForCapture(()=>false);
                byte[] bytes=Capture(lane);using(var reader=new BinaryReader(new MemoryStream(bytes)))restored=ObservedPathLane.ReadState(reader);
                CollectionAssert.AreEqual(bytes,Capture(restored),"Current-format query/answer state must roundtrip exactly.");
                var copy=new ObservedPathWork(PlayableOwner.Player,new NavGeometry(20,Array.Empty<NavObstacle>(),1),profile,.5,2,2,query.Origin,query.Goal);
                Assert.True(restored.TryGet(copy,1,2,PlayableRouteTargetKind.VisibleEnemy,0,out var path,()=>Assert.Fail("Restored completed answer must not be recomputed.")));
                var moved=new ObservedPathWork(PlayableOwner.Player,geometry,profile,.5,2,2,new NavPoint(-4,0),new NavPoint(6,0));
                Assert.True(restored.TryGet(moved,1,2,PlayableRouteTargetKind.VisibleEnemy,0,out var rebound));Assert.AreEqual(moved.Goal,rebound.Last());
                var from=moved.Origin;foreach(var point in rebound){Assert.True(geometry.SegmentFree(from,point,.5));from=point;}
                var changed=new ObservedPathWork(PlayableOwner.Player,geometry,profile,.6,2,2,query.Origin,query.Goal);
                Assert.False(restored.TryGet(changed,1,2,PlayableRouteTargetKind.VisibleEnemy,0,out _),"Changed footprint must not consume the old proof.");
            }finally{lane.Stop();restored?.Stop();Assert.True(SpinWait.SpinUntil(()=>lane.IsStopped&&(restored==null||restored.IsStopped),5000));}
        }
        [Test] public void ObservedPathWorkCanRunWithOnlyDetachedSolverInputs()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var origin=new NavPoint(-5,0);var goal=new NavPoint(5,0);
            var work=new ObservedPathWork(PlayableOwner.Player,geometry,PlayableProfile.Default.Navigation,.5,2,2,origin,goal);
            NavPoint[] path=null;Exception error=null;
            var thread=new Thread(()=>{try{path=work.Execute(new ObservedPathWorkPool());}catch(Exception ex){error=ex;}}){IsBackground=true};
            thread.Start();Assert.True(thread.Join(5000));Assert.IsNull(error);Assert.IsNotEmpty(path);
            var from=origin;foreach(var point in path){Assert.True(geometry.SegmentFree(from,point,.5));from=point;}
            Assert.AreEqual(goal,path.Last());
        }
        [Test] public void OrdinaryWorldKeepsTickingWhileAiObservedPathIsDelayed()=>RunObservedDelayMatch(2);
        [Test] public void OrdinaryWorldKeepsTickingWhileAiDecisionReviewIsDelayed()=>RunObservedDelayMatch(2,ownerReview:true);
        [Test] public void FourOrdinaryParticipantsKeepWorldAndAiProgressWhileObservedPathIsDelayed()=>RunObservedDelayMatch(4);
        [Test] public void EightOrdinaryParticipantsKeepWorldAndAiProgressWhileObservedPathIsDelayed()=>RunObservedDelayMatch(8);
        [TestCase(false)][TestCase(true)] public void DelayedOwnerReviewAllowsStopAndPauseWithoutPublishingLateDecision(bool pause)
        {
            var config=ObservedMatchConfiguration(2);var runtime=new PlayableRuntime(config,71,startPaused:true);
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            var scheduler=(AiAuthorityScheduler)typeof(PlayableRuntime).GetField("aiScheduler",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();int parked=0;
            var worker=new CertifiedRouteWorker(runtime.NavigationBinding,4,new CertifiedRouteKernel(),()=>runtime.IsStopRequested,runtime.ReportRouteFailure);
            try{
                domain.BeforeOwnerReviewForTests=()=>{if(Interlocked.CompareExchange(ref parked,1,0)!=0)return;entered.Set();if(!release.Wait(10000))throw new TimeoutException("Review lifecycle boundary not released.");};
                runtime.RequestPause(false);Assert.True(entered.Wait(15000));
                var owner=scheduler.Owners.Single();long decision=owner.Checkpoint.DecisionTick;
                if(pause){runtime.RequestPause(true);Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Paused||runtime.IsStopped,2000));Assert.False(runtime.IsStopped,runtime.Latest.Failure);}
                else {runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,2000),"Stop must finish before the detached review is released.");}
                Assert.False(release.IsSet);Assert.False(scheduler.ReviewWorkerStopped);
                release.Set();Assert.True(SpinWait.SpinUntil(()=>scheduler.ReviewWorkerStopped,5000));
                if(pause){var capture=runtime.RequestCaptureBytes();Assert.True(capture.Wait(10000));var restored=PlayableAuthorityTick.RestoreBytes(capture.Result,config);
                    try{Assert.AreEqual(decision,scheduler.Owners.Single().Checkpoint.DecisionTick);Assert.AreEqual(decision,restored.Scheduler.Owners.Single().Checkpoint.DecisionTick,"Paused review must not adopt a late result.");}
                    finally{restored.Stop();restored.Domain.DestroyOwnedNavigation();}}
                else Assert.AreEqual(decision,scheduler.Owners.Single().Checkpoint.DecisionTick);
            }finally{
                domain.BeforeOwnerReviewForTests=null;release.Set();runtime.RequestStop();worker.Dispose();
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&worker.IsStopped&&domain.ObservedPathWorkerStopped&&scheduler.ReviewWorkerStopped,5000));runtime.Dispose();domain.DestroyOwnedNavigation();
            }
        }
        [Test] public void RuntimeCaptureRestoresAnExplicitNonzeroNextReviewOwner()
        {
            var config=ObservedMatchConfiguration(4);var runtime=new PlayableRuntime(config,71,startPaused:true);PlayableRuntime restored=null;
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            var scheduler=(AiAuthorityScheduler)typeof(PlayableRuntime).GetField("aiScheduler",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            PlayableDomain restoredDomain=null;
            try{
                Assert.AreEqual(3,scheduler.Owners.Count);
                runtime.BeforeCaptureSerialization=()=>scheduler.RestoreReviewCursor(2);
                var capture=runtime.RequestCaptureBytes();Assert.True(capture.Wait(10000));runtime.BeforeCaptureSerialization=null;
                restored=PlayableRuntime.RestoreBytes(capture.Result,config);
                restoredDomain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(restored);
                var copy=(AiAuthorityScheduler)typeof(PlayableRuntime).GetField("aiScheduler",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(restored);
                Assert.AreEqual(2,copy.NextReviewOwnerForCapture,"Resetting the next owner would change current-format continuation.");
                Assert.False(restored.IsStopped,restored.Latest.Failure);
            }finally{
                restored?.RequestStop();runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(restored==null||restored.IsStopped)&&domain.ObservedPathWorkerStopped&&(restoredDomain==null||restoredDomain.ObservedPathWorkerStopped),5000));
                restored?.Dispose();runtime.Dispose();restoredDomain?.DestroyOwnedNavigation();domain.DestroyOwnedNavigation();
            }
        }
        internal static OfflineMatchConfiguration ObservedMatchConfiguration(int participants)
        {
            var basis=OfflineParticipantAuthorityTests.Config(participants,false,seed:7);
            var costs=new double[basis.Starts.Count,basis.Starts.Count];
            for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var roster=basis.Roster.Select((p,i)=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,i<participants-1?OfflineControl.Ai:OfflineControl.Human)).ToArray();
            return new OfflineMatchConfiguration(basis.Profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,roster,basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs);
        }
        private static void RunObservedDelayMatch(int participants,bool ownerReview=false)
        {
            var config=ObservedMatchConfiguration(participants);
            var runtime=new PlayableRuntime(config,71,startPaused:true);
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();int parked=0;
            var worker=new CertifiedRouteWorker(runtime.NavigationBinding,4,new CertifiedRouteKernel(),()=>runtime.IsStopRequested,runtime.ReportRouteFailure);
            PlayableRuntime restored=null;PlayableDomain restoredDomain=null;CertifiedRouteWorker restoredWorker=null;
            try{
                Action park=()=>{
                    if(Interlocked.CompareExchange(ref parked,1,0)!=0)return;
                    entered.Set();if(!release.Wait(10000))throw new TimeoutException("AI computation test boundary not released.");
                };
                if(ownerReview)domain.BeforeOwnerReviewForTests=park;else domain.BeforeObservedPathForTests=park;
                runtime.RequestPause(false);
                Assert.True(entered.Wait(15000),"Ordinary AI must reach its real observed-path solve.");
                var actors=config.Roster.Where(p=>p.Control==OfflineControl.Ai).Select(p=>{
                    var view=runtime.ParticipantView(p.Id);var actor=view.Entities.Single(e=>e.Owner==view.Owner);
                    return new {Owner=p.Id,Actor=actor.Id,Start=actor.Position};
                }).ToArray();
                long tick=runtime.Latest.Tick;
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>=tick+10||runtime.IsStopped,2000),"AI route observation may wait, but the ordinary world must continue ticking.");
                Assert.False(runtime.IsStopped,runtime.Latest.Failure);Assert.GreaterOrEqual(runtime.Latest.Tick,tick+10);
                var scheduler=(AiAuthorityScheduler)typeof(PlayableRuntime).GetField("aiScheduler",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
                var decisions=scheduler.Owners.ToDictionary(o=>o.OwnerId,o=>o.Checkpoint.DecisionTick);
                release.Set();
                bool progressed=SpinWait.SpinUntil(()=>runtime.IsStopped||(participants==2?
                    actors.All(a=>runtime.ParticipantView(a.Owner).Entities.Any(e=>e.Id==a.Actor&&!e.Position.Equals(a.Start)&&e.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&(e.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move||e.CurrentOrder?.Kind==PlayableTacticalOrderKind.AttackMove))):
                    scheduler.Owners.All(o=>o.Checkpoint.DecisionTick>decisions[o.OwnerId])),15000);
                foreach(var a in actors){var current=runtime.ParticipantView(a.Owner).Entities.Single(e=>e.Id==a.Actor);var check=scheduler.Owners.Single(o=>o.OwnerId==a.Owner).Checkpoint;
                    TestContext.Out.WriteLine("AI_OBS_PROGRESS owner="+a.Owner+" tick="+runtime.Latest.Tick+" decision="+check.DecisionTick+" moved="+!current.Position.Equals(a.Start)+" kind="+current.CurrentOrder?.Kind+" stamp="+current.OrderStamp?.Kind+" policy="+check.LastPolicy+" action="+check.LastActionKind+" opening="+check.Opening?.Opening);
                }
                Assert.True(progressed,participants==2?"After the observed solve completes, ordinary AI must produce actual first movement through the real worker.":"After the observed solve completes, every AI must advance its real decision publication; stationary defense is a legal decision.");
                Assert.False(runtime.IsStopped,runtime.Latest.Failure);
                runtime.RequestPause(true);Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Paused||runtime.IsStopped,5000));Assert.False(runtime.IsStopped,runtime.Latest.Failure);
                string liveQueueError=null;
                runtime.BeforeCaptureSerialization=()=>{try{typeof(PlayableDomain).GetMethod("ValidateTacticalQueues",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,new object[]{null});}catch(TargetInvocationException error){liveQueueError=error.InnerException.ToString();}};
                var capture=runtime.RequestCaptureBytes();Assert.True(capture.Wait(10000));runtime.BeforeCaptureSerialization=null;var state=PlayableWorldState.Decode(capture.Result);
                TestContext.Out.WriteLine("AI_OBS_QUEUE_LIVE participants="+participants+" tick="+state.Tick+" validation="+(liveQueueError??"PASS"));
                var evidence=Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),"../.local/nonblocking-match/roster-captures",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(evidence);File.WriteAllBytes(Path.Combine(evidence,"participants-"+participants+".world"),capture.Result);TestContext.Out.WriteLine("AI_OBS_CAPTURE "+evidence);
                Assert.IsNull(liveQueueError,"Live tactical source must be valid before capture.");
                Assert.AreEqual(5,BitConverter.ToInt32(state.RuntimeTransport,0));
                int expectedReviewCursor=scheduler.NextReviewOwnerForCapture;
                restored=PlayableRuntime.RestoreBytes(capture.Result,config);
                restoredDomain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(restored);
                var restoredScheduler=(AiAuthorityScheduler)typeof(PlayableRuntime).GetField("aiScheduler",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(restored);Assert.AreEqual(expectedReviewCursor,restoredScheduler.NextReviewOwnerForCapture,"Current-format capture must preserve fair review continuation.");
                restoredWorker=new CertifiedRouteWorker(restored.NavigationBinding,4,new CertifiedRouteKernel(),()=>restored.IsStopRequested,restored.ReportRouteFailure);
                Assert.True(SpinWait.SpinUntil(()=>restored.Latest.Paused||restored.IsStopped,5000));Assert.False(restored.IsStopped,restored.Latest.Failure);Assert.AreEqual(state.Tick,restored.Latest.Tick);
                var expected=runtime.ParticipantView(config.Roster[0].Id);var actual=restored.ParticipantView(config.Roster[0].Id);
                Assert.AreEqual(expected.Credits,actual.Credits);CollectionAssert.AreEqual(expected.Entities.Select(e=>e.Position),actual.Entities.Select(e=>e.Position));
            }finally{
                restored?.RequestStop();restoredWorker?.Dispose();if(restored!=null){Assert.True(SpinWait.SpinUntil(()=>restored.IsStopped&&(restoredWorker==null||restoredWorker.IsStopped)&&restoredDomain.ObservedPathWorkerStopped,5000));restored.Dispose();restoredDomain.DestroyOwnedNavigation();}
                domain.BeforeObservedPathForTests=null;domain.BeforeOwnerReviewForTests=null;release.Set();runtime.RequestStop();worker.Dispose();
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&worker.IsStopped&&domain.ObservedPathWorkerStopped,5000));runtime.Dispose();domain.DestroyOwnedNavigation();
            }
        }
        private static PlayableAuthorityTick Authority(bool asynchronous=true)=>new PlayableAuthorityTick(OfflineParticipantAuthorityTests.Config(2),71,asynchronousRoutes:asynchronous);
        private static NavGeometry Geometry(NavigationSession n)=>(NavGeometry)typeof(NavigationSession).GetField("geometry",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(n);
        private static NavPoint Goal(NavigationSession n,NavUnit actor,double distance=3){
            foreach(var direction in new[]{new NavPoint(1,0),new NavPoint(-1,0),new NavPoint(0,1),new NavPoint(0,-1)}){
                var goal=new NavPoint(actor.Position.X+direction.X*distance,actor.Position.Z+direction.Z*distance);
                if(Geometry(n).SegmentFree(actor.Position,goal,actor.Radius))return goal;
            }
            Assert.Fail("fixture has no free short route");return default;
        }
        private static NavUnit Actor(PlayableAuthorityTick a)=>a.Domain.Navigation.Crowd.Units.First();
        private static object Call(PlayableAuthorityTick a,string method,params object[] args)=>a.Domain.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(a.Domain,args);
        [Test] public void ReadyAnswerDuringHostOperationDoesNotBlockIndependentWorldProgress()
        {
            var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);
            var other=n.Crowd.Units.First(u=>u.Id!=actor.Id);var otherStart=other.Position;
            Assert.True(n.Crowd.SetRoute(other.Id,Goal(n,other,6),new[]{Goal(n,other,6)}));
            Assert.True(n.Move(actor.Id,Goal(n,actor)));
            Assert.True(n.Requests.TryDequeue(out var request));
            Assert.True(n.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal})));
            // Hold the same operation boundary used by CertifiedRouteWorker. The
            // authority must not wait for its ACK merely because an answer exists.
            Assert.True(n.RoutePort.TryBeginHostOperation());
            using var finished=new ManualResetEventSlim();Exception failure=null;
            var authority=new Thread(()=>{try{for(int i=0;i<30;i++)Assert.True(a.TryAdvance());}catch(Exception e){failure=e;}finally{finished.Set();}}){IsBackground=true};
            authority.Start();
            bool progressed;
            try{progressed=finished.Wait(2000);}
            finally{n.RoutePort.EndHostOperation();Assert.True(authority.Join(10000));}
            try{
                Assert.IsNull(failure);
                Assert.True(progressed,"Ready-result preparation must not wait for the worker operation to finish.");
                Assert.AreEqual(30,a.Tick);Assert.AreNotEqual(otherStart,other.Position,"Independent installed movement must continue.");
                for(int i=0;i<60&&n.IsPending(actor.Id);i++)Assert.True(a.TryAdvance());
                Assert.False(n.IsPending(actor.Id));Assert.AreEqual(1,n.AppliedResults);
            }finally{a.Stop();n.DestroyNavigationAuthority();}
        }
        [Test] public void OrdinaryRuntimeKeepsTickingWhileActualWorkerIsParkedAfterReadyPublication()
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);var runtime=new PlayableRuntime(config,71);
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            var n=domain.Navigation;var kernel=new CertifiedRouteKernel();CertifiedRouteWorker worker=null;
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();int parked=0;
            try{
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,5000));
                var view=runtime.ParticipantView(config.Roster[0].Id);var actor=view.Entities.Single(e=>e.Owner==view.Owner);
                var otherView=runtime.ParticipantView(config.Roster[1].Id);var other=otherView.Entities.Single(e=>e.Owner==otherView.Owner);
                Assert.True(n.Crowd.TryGet(other.Id,out var otherActor));var otherGoal=Goal(n,otherActor,12);
                kernel.PauseAtPhysicalBoundary=()=>{
                    if(n.Answers.CopyItems().Any(a=>a.Request.Entity==actor.Id)&&Interlocked.CompareExchange(ref parked,1,0)==0){entered.Set();if(!release.Wait(10000))throw new TimeoutException("Test worker was not released.");}
                    return n.RoutePort.CaptureRequested;
                };
                worker=new CertifiedRouteWorker(runtime.NavigationBinding,4,kernel,()=>runtime.IsStopRequested,runtime.ReportRouteFailure);
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,1,config.Roster[1].Id,PlayableCommandKind.Move,new[]{other.Id},otherGoal).AsHuman()).Accepted);
                Assert.True(SpinWait.SpinUntil(()=>!runtime.ParticipantView(config.Roster[1].Id).Entities.Single(e=>e.Id==other.Id).Position.Equals(other.Position)||runtime.IsStopped,5000),"Independent actor needs an actual installed route before the worker stall.");
                Assert.False(runtime.IsStopped,runtime.Latest.Failure);
                Assert.True(n.Crowd.TryGet(actor.Id,out var navActor));var goal=Goal(n,navActor);
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.Move,new[]{actor.Id},goal).AsHuman()).Accepted);
                Assert.True(entered.Wait(10000),worker.Publication.Failure??runtime.Latest.Failure??"Real worker did not publish its answer.");
                var otherBefore=runtime.ParticipantView(config.Roster[1].Id).Entities.Single(e=>e.Id==other.Id).Position;
                long tick=runtime.Latest.Tick;
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>=tick+10||runtime.IsStopped,2000),"An unacknowledged real worker must not stop ordinary authority publications.");
                Assert.False(runtime.IsStopped,runtime.Latest.Failure);Assert.GreaterOrEqual(runtime.Latest.Tick,tick+10);
                Assert.AreNotEqual(otherBefore,runtime.ParticipantView(config.Roster[1].Id).Entities.Single(e=>e.Id==other.Id).Position,"Independent certified movement continues before the worker is released.");
                release.Set();
                Assert.True(SpinWait.SpinUntil(()=>!runtime.ParticipantView(config.Roster[0].Id).Entities.Single(e=>e.Id==actor.Id).Position.Equals(actor.Position)||runtime.IsStopped,5000),"The current ready route must eventually produce physical movement.");
                Assert.False(runtime.IsStopped,runtime.Latest.Failure);Assert.Null(worker.Publication.Failure);Assert.Zero(runtime.Latest.Metrics.Errors);
            }finally{
                release.Set();runtime.RequestStop();worker?.Dispose();
                Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,5000));if(worker!=null)Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));
                runtime.Dispose();n.DestroyNavigationAuthority();
            }
        }
        [Test] public void WithheldRouteStopsOnlyRequesterWhileTicksMovementAndProductionContinue(){
            var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);var config=OfflineParticipantAuthorityTests.Config(2);
            var hq=a.Latest.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
            Call(a,"AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(a,"BuildAt",hq.SiteId,1,PlayableBuildingKind.Factory,hq.Id,PlayableOwner.Player,null));
            Call(a,"AdvanceFoundations");Call(a,"AdvanceBuildings",config.Profile.FactoryBuildSeconds+1d);
            var view=a.Domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed);
            var factory=view.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.QueueTank,new[]{factory.Id}).AsHuman()).Status);
            // Begin an old path before issuing another one; it must not keep moving while pending.
            Assert.True(n.Crowd.SetRoute(actor.Id,Goal(n,actor),new[]{Goal(n,actor)}));
            var start=actor.Position;long revision=actor.MobilityRevision;
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,2,config.Roster[0].Id,PlayableCommandKind.Move,new[]{actor.Id},Goal(n,actor)).AsHuman()).Status);
            var other=n.Crowd.Units.First(u=>u.Id!=actor.Id);var otherStart=other.Position;
            Assert.True(n.Crowd.SetRoute(other.Id,Goal(n,other),new[]{Goal(n,other)}));
            for(int i=0;i<30;i++)Assert.True(a.TryAdvance());
            Assert.AreEqual(30,a.Tick);Assert.True(n.IsPending(actor.Id));Assert.AreEqual(start,actor.Position);Assert.AreEqual(revision,actor.MobilityRevision);
            Assert.AreNotEqual(otherStart,other.Position);Assert.False(a.AwaitingRoutes);
            var order=a.Latest.Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Single();
            Assert.True(order.Active);Assert.Less(order.Remaining,order.Duration,"Production must advance while path calculation is withheld.");
            a.Stop();
        }
        [TestCase(false)][TestCase(true)] public void QueuedHumanMoveDoesNotBlockTicksWhileActualHostAcknowledgementIsDelayed(bool pause)=>RunQueuedHumanCommand(pause,PlayableCommandKind.Move);
        [TestCase(PlayableCommandKind.Hold)][TestCase(PlayableCommandKind.Stop)] public void QueuedHumanStationaryCommandDoesNotBlockTicksWhileActualHostAcknowledgementIsDelayed(PlayableCommandKind kind)=>RunQueuedHumanCommand(false,kind);
        private static void RunQueuedHumanCommand(bool pause,PlayableCommandKind kind)
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);var runtime=new PlayableRuntime(config,71);
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            var n=domain.Navigation;var kernel=new CertifiedRouteKernel();CertifiedRouteWorker worker=null;
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();int parked=0;
            var receipts=new System.Collections.Generic.List<PlayableCommandReceipt>();
            try{
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,5000));
                var foreign=config.Roster[1].Id;var fv=runtime.ParticipantView(foreign);var peer=fv.Entities.Single(e=>e.Owner==fv.Owner);
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,1,foreign,PlayableCommandKind.Move,new[]{peer.Id},new NavPoint(peer.Position.X-3,peer.Position.Z+3))).Accepted);
                Assert.True(SpinWait.SpinUntil(()=>{receipts.AddRange(runtime.DrainReceipts());return receipts.Count>0;},5000));Assert.AreEqual(PlayableCommandStatus.Applied,receipts.Single().Status);receipts.Clear();
                kernel.PauseAtPhysicalBoundary=()=>{
                    var root=n.PinCommittedHostRoot();bool park;
                    try{park=root!=null&&root.Frontier.Stage==CertifiedRouteKernel.PumpStage.Projection&&root.Projection.Count>0&&Interlocked.CompareExchange(ref parked,1,0)==0;}finally{root?.Release();}
                    if(park){entered.Set();if(!release.Wait(10000))throw new TimeoutException("Test projection was not released.");}
                    return n.RoutePort.CaptureRequested;
                };
                worker=new CertifiedRouteWorker(runtime.NavigationBinding,4,kernel,()=>runtime.IsStopRequested,runtime.ReportRouteFailure);
                Assert.True(entered.Wait(10000),worker.Publication.Failure??"Real worker did not reach Projection.");
                var view=runtime.ParticipantView(config.Roster[0].Id);var actor=view.Entities.Single(e=>e.Owner==view.Owner);
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,100,view.OwnerId,kind,new[]{actor.Id},new NavPoint(actor.Position.X+2,actor.Position.Z+3)).AsHuman()).Accepted);
                Assert.True(runtime.TrySubmit(new PlayableCommand(71,101,view.OwnerId,kind,new[]{actor.Id},new NavPoint(actor.Position.X+3,actor.Position.Z+3)).AsHuman()).Accepted);
                Assert.True(SpinWait.SpinUntil(()=>n.RoutePort.CaptureRequested||runtime.IsStopped,5000));Assert.False(runtime.IsStopped,runtime.Latest.Failure);
                long tick=runtime.Latest.Tick;
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>=tick+10||runtime.IsStopped,2000),"A queued command must not hold world time while the real host is busy.");
                Assert.False(runtime.IsStopped,runtime.Latest.Failure);Assert.GreaterOrEqual(runtime.Latest.Tick,tick+10);Assert.IsEmpty(runtime.DrainReceipts(),"Admission has not happened yet.");
                var capture=runtime.RequestCaptureBytes();Assert.True(SpinWait.SpinUntil(()=>capture.IsCompleted,2000),"Pending command capture must reject promptly, without waiting for the host.");
                Assert.True(capture.IsFaulted);StringAssert.Contains("empty command/profile/tutorial ingress barrier",capture.Exception.GetBaseException().Message);
                if(pause)runtime.RequestPause(true);else release.Set();
                Assert.True(SpinWait.SpinUntil(()=>{receipts.AddRange(runtime.DrainReceipts());return receipts.Count>=2||runtime.IsStopped;},5000));
                Assert.False(runtime.IsStopped,runtime.Latest.Failure);Assert.AreEqual(2,receipts.Count);
                CollectionAssert.AreEqual(new long[]{100,101},receipts.Select(r=>r.Sequence));
                foreach(var receipt in receipts){Assert.AreEqual(pause?PlayableCommandStatus.Rejected:PlayableCommandStatus.Applied,receipt.Status,receipt.Message);if(pause)Assert.AreEqual("Paused.",receipt.Message);}
                if(pause){Assert.False(n.RoutePort.CaptureRequested,"Pause must release pending command yield even while host remains parked.");Assert.False(release.IsSet);}
            }finally{
                release.Set();runtime.RequestStop();worker?.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped),5000));runtime.Dispose();n.DestroyNavigationAuthority();
            }
        }
        [Test] public void PhysicalHostPendingRoutePreservesIndependentDisplacementAndEconomy(){
            var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);var other=n.Crowd.Units.First(u=>u.Id!=actor.Id);
            var kernel=new CertifiedRouteKernel();
            try{
                kernel.Attach(n.RoutePort);
                // Establish the physical ownership lane before withholding A's work.
                kernel.Pump(a.NavigationBinding,1);
                Assert.True(n.Crowd.SetRoute(other.Id,Goal(n,other),new[]{Goal(n,other)}));
                var otherStart=other.Position;var start=actor.Position;
                Assert.True(n.Move(actor.Id,Goal(n,actor)));long publications=n.OwnedMovementPublications;
                for(int i=0;i<30;i++)Assert.True(a.TryAdvance());
                Assert.AreEqual(30,a.Tick);Assert.True(n.IsPending(actor.Id));Assert.AreEqual(start,actor.Position);
                Assert.AreNotEqual(otherStart,other.Position,"Installed B must physically move while A remains pending in the owned Host lane.");
                Assert.Greater(n.OwnedMovementPublications,publications,"Movement must use the paid immutable carrier.");
            }finally{kernel.Clear();a.Stop();n.DestroyNavigationAuthority();}
        }
        [TestCase(PlayableCommandKind.Move)][TestCase(PlayableCommandKind.AttackMove)]
        public void ActualGroupPendingKeepsInstalledBAndProductionMovingThenCurrentAReachesArrival(PlayableCommandKind kind){
            var config=OfflineParticipantAuthorityTests.Config(2);var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);var other=n.Crowd.Units.First(u=>u.Id!=actor.Id);var kernel=new CertifiedRouteKernel();
            try{
                var hq=a.Latest.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
                Call(a,"AddCredits",PlayableOwner.Player,10000d);
                Assert.AreEqual(PlayableCommandStatus.Applied,Call(a,"BuildAt",hq.SiteId,1,PlayableBuildingKind.Factory,hq.Id,PlayableOwner.Player,null));
                Call(a,"AdvanceFoundations");Call(a,"AdvanceBuildings",config.Profile.FactoryBuildSeconds+1d);
                var view=a.Domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,config.Seed);
                var factory=view.Buildings.First(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Factory);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.QueueTank,new[]{factory.Id}).AsHuman()).Status);
                Assert.True(n.Move(other.Id,Goal(n,other,6)));
                for(int i=0;i<4096&&n.IsPending(other.Id);i++){Assert.True(a.TryAdvance());kernel.Pump(a.NavigationBinding,4);}
                Assert.False(n.IsPending(other.Id));Assert.True(other.Moving);var bStart=other.Position;
                var goal=Goal(n,actor);Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,2,config.Roster[0].Id,kind,new[]{actor.Id},goal).AsHuman()).Status);
                var start=actor.Position;long tick=a.Tick;double remaining=a.Latest.Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Single().Remaining;
                for(int i=0;i<15;i++)Assert.True(a.TryAdvance());
                Assert.AreEqual(tick+15,a.Tick);Assert.True(n.IsPending(actor.Id));Assert.AreEqual(start,actor.Position);Assert.AreNotEqual(bStart,other.Position);
                Assert.Less(a.Latest.Buildings.Single(b=>b.Id==factory.Id).PrivateState.Orders.Single().Remaining,remaining);
                using(var before=new QuantumPin(n.PinCommittedHostRoot())){
                    long serial=before.Value.QuantumSerial;for(int i=0;i<5;i++)Assert.True(a.TryAdvance(true));Assert.AreEqual(tick+15,a.Tick);
                    using(var after=new QuantumPin(n.PinCommittedHostRoot()))Assert.AreEqual(serial,after.Value.QuantumSerial);
                }
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,3,config.Roster[0].Id,kind,new[]{actor.Id},Goal(n,actor,2)).AsHuman()).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,4,config.Roster[0].Id,PlayableCommandKind.Stop,new[]{actor.Id}).AsHuman()).Status);
                Assert.True(a.TryAdvance());Assert.False(n.IsPending(actor.Id));Assert.AreEqual(start,actor.Position);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,5,config.Roster[0].Id,kind,new[]{actor.Id},goal).AsHuman()).Status);
                for(int i=0;i<4096&&(n.IsPending(actor.Id)||actor.Outcome!=NavigationOutcome.Arrived);i++){Assert.True(a.TryAdvance());kernel.Pump(a.NavigationBinding,4);}
                Assert.False(n.IsPending(actor.Id));Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome,"current A must install and physically arrive");Assert.AreNotEqual(start,actor.Position);
            }finally{kernel.Clear();a.Stop();n.DestroyNavigationAuthority();Assert.Zero(n.PhysicalHostBudget.PhysicalBytes);}
        }
        private sealed class QuantumPin:IDisposable{internal readonly NavigationHostRoot Value;internal QuantumPin(NavigationHostRoot value){Value=value;}public void Dispose()=>Value.Release();}
        [Test] public void CombatContinuesWhileAnotherActorsRouteIsWithheld(){
            var a=Authority();var n=a.Domain.Navigation;
            Call(a,"SpawnUnit",new NavPoint(-3,0),PlayableOwner.Player,PlayableEntityKind.Tank);
            Call(a,"SpawnUnit",new NavPoint(3,0),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            double TankHealth()=>a.Domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026).Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).Sum(e=>e.Health);
            int requester=a.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player&&e.Kind==PlayableEntityKind.Explorer).Id;
            Assert.True(n.Crowd.TryGet(requester,out var actor));Assert.True(n.Move(requester,Goal(n,actor)));var start=actor.Position;
            double before=TankHealth();
            for(int i=0;i<60;i++)Assert.True(a.TryAdvance());
            Assert.AreEqual(60,a.Tick);Assert.True(n.IsPending(requester));Assert.AreEqual(start,actor.Position);
            Assert.Less(TankHealth(),before,"Actual firing/projectile damage must continue while another path is withheld.");a.Stop();
        }
        [Test] public void FrozenHeadlessPolicyStillRequiresReadyRoutes(){
            var a=Authority(false);var n=a.Domain.Navigation;var actor=Actor(a);Assert.True(n.Move(actor.Id,Goal(n,actor)));
            Assert.False(a.TryAdvance());Assert.Zero(a.Tick);Assert.True(a.AwaitingRoutes);a.Stop();
        }
        [Test] public void CertifiedReadyPathActivatesOnAnOrdinaryTick(){
            var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);var start=actor.Position;
            Assert.True(n.Move(actor.Id,Goal(n,actor)));for(int i=0;i<5;i++)Assert.True(a.TryAdvance());Assert.AreEqual(start,actor.Position);
            using(var host=new Spacewars.Presentation.UnityHostRouteService()){
                for(int i=0;i<4096&&n.IsPending(actor.Id);i++){host.Service(a,4);Assert.True(a.TryAdvance());}
                Assert.False(n.IsPending(actor.Id));Assert.AreEqual(1,n.AppliedResults);Assert.True(actor.Moving);
                // Route activation can initially turn in place; retain actual movement verification.
                for(int i=0;i<60;i++)Assert.True(a.TryAdvance());Assert.AreNotEqual(start,actor.Position);
            }
            a.Stop();
        }
        [TestCase("stop")][TestCase("new-order")][TestCase("geometry")][TestCase("death")]
        public void LateResultsNeverReactivateObsoleteMovement(string cause){
            var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);var start=actor.Position;
            Assert.True(n.Move(actor.Id,Goal(n,actor)));Assert.True(n.Requests.TryDequeue(out var old));
            for(int i=0;i<5;i++)Assert.True(a.TryAdvance());
            if(cause=="stop")n.Stop(actor.Id,true);
            else if(cause=="new-order")Assert.True(n.Move(actor.Id,Goal(n,actor,2)));
            else if(cause=="geometry")n.ChangeGeometry(new NavGeometry(Geometry(n).HalfExtent,Geometry(n).Obstacles.ToArray(),Geometry(n).Revision+1));
            else Assert.True(n.Remove(actor.Id));
            Assert.True(n.Answers.TryEnqueue(new NavigationAnswer(old,new[]{old.Goal})));
            Assert.True(a.TryAdvance());Assert.Zero(n.AppliedResults);Assert.Greater(n.RejectedResults,0);
            if(cause!="death"){Assert.AreEqual(start,actor.Position);Assert.False(actor.Moving);}
            a.Stop();
        }
        [Test] public void AsyncPolicySurvivesExactPendingRestoreAndPause(){
            var config=OfflineParticipantAuthorityTests.Config(2);var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);Assert.True(n.Move(actor.Id,Goal(n,actor)));
            for(int i=0;i<5;i++)Assert.True(a.TryAdvance());var saved=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(saved,config);
            Assert.AreEqual(PlayableAuthorityTick.AsyncRouteDeliveryPolicy,b.EffectiveRouteDeliveryPolicy);Assert.True(b.Domain.Navigation.SuspendPendingMovement);
            CollectionAssert.AreEqual(saved,b.CaptureBytes());
            for(int i=0;i<15;i++){Assert.True(a.TryAdvance());Assert.True(b.TryAdvance());CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
            long tick=b.Tick;Assert.True(b.TryAdvance(true));Assert.AreEqual(tick,b.Tick);Assert.True(b.Domain.Navigation.IsPending(actor.Id));
            a.Stop();b.Stop();
        }
        [Test] public void UnknownSavedRoutePolicyStillFailsClosed(){
            var config=OfflineParticipantAuthorityTests.Config(2);var a=Authority();var state=PlayableWorldState.Decode(a.CaptureBytes());
            var needle=System.Text.Encoding.UTF8.GetBytes(PlayableAuthorityTick.AsyncRouteDeliveryPolicy);int at=-1;
            for(int i=0;i<=state.AiAuthority.Length-needle.Length;i++)if(state.AiAuthority.Skip(i).Take(needle.Length).SequenceEqual(needle)){at=i;break;}
            Assert.GreaterOrEqual(at,0);state.AiAuthority[at]=(byte)'x';
            Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(state.Encode(),config));a.Stop();
        }
        [Test] public void SolverAllocationReservationNeverFreezesAsyncTicksOrCancellation(){
            var a=Authority();var n=a.Domain.Navigation;var actor=Actor(a);Assert.True(n.Move(actor.Id,Goal(n,actor)));
            Assert.True(a.TryAdvance());long issuedTick=a.Tick;
            using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
                var kernel=new CertifiedRouteKernel();kernel.BeforePrepare=()=>{entered.Set();Assert.True(release.Wait(TimeSpan.FromSeconds(10)));};
                var task=System.Threading.Tasks.Task.Run(()=>kernel.Pump(a.NavigationBinding,4));
                try{
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));var start=actor.Position;
                    var other=n.Crowd.Units.First(u=>u.Id!=actor.Id);var otherStart=other.Position;Assert.True(n.Crowd.SetRoute(other.Id,Goal(n,other),new[]{Goal(n,other)}));
                    for(int i=0;i<30;i++)Assert.True(a.TryAdvance());Assert.AreEqual(issuedTick+30,a.Tick);Assert.AreEqual(start,actor.Position);Assert.AreNotEqual(otherStart,other.Position,"B displacement while physical Host allocation is blocked");
                    n.Stop(actor.Id,true);Assert.True(a.TryAdvance());Assert.False(n.IsPending(actor.Id));
                }finally{release.Set();Assert.True(task.Wait(TimeSpan.FromSeconds(5)));kernel.Clear();a.Stop();}
            }
        }
        [Test] public void RealtimeOfflineCommandsRemainAvailableWithoutRouteService(){
            var config=OfflineParticipantAuthorityTests.Config(2);var runtime=new PlayableRuntime(config,71);
            try{
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,TimeSpan.FromSeconds(5)));var actor=runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player);
                long tick=runtime.Latest.Tick;var goal=new NavPoint(actor.Position.X+2,actor.Position.Z);
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(71,1,config.Roster[0].Id,PlayableCommandKind.Move,new[]{actor.Id},goal).AsHuman()).Status);
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>=tick+15,TimeSpan.FromSeconds(3)),"Realtime authority must not freeze without a solver.");
                Assert.Greater(runtime.Latest.Metrics.NavigationPending,0);
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(71,2,config.Roster[0].Id,PlayableCommandKind.Stop,new[]{actor.Id}).AsHuman()).Status);
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Metrics.NavigationPending==0,TimeSpan.FromSeconds(3)),"A new command must cancel pending work while the solver is absent.");
            }finally{runtime.Dispose();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,TimeSpan.FromSeconds(5)));}
        }
    }
    public sealed class ObservedCaptureReplayTests
    {
        [Test,Explicit("Preserved invalid current-format capture: set SPACEWARS_OBS_CAPTURE to the captured world file.")]
        public void CapturedInvalidFoundationAttackRemainsRejected()
        {
            var path=Environment.GetEnvironmentVariable("SPACEWARS_OBS_CAPTURE");Assert.IsNotEmpty(path);
            var error=Assert.Throws<ArgumentException>(()=>PlayableRuntime.RestoreBytes(File.ReadAllBytes(path),AsyncRouteDeliveryTests.ObservedMatchConfiguration(8)));
            StringAssert.Contains("Invalid active queue execution.",error.Message);
        }
    }

}

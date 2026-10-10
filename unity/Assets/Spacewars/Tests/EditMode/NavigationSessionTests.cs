using System;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationSessionTests
    {
        [Test] public void CrowdRestoreContinuesMovingRouteWithoutReplayingPrefix()
        {
            var geometry=new NavGeometry(20,new NavObstacle[0],1);var profile=new NavigationProfile();
            var continuous=new NavCrowd(geometry,profile);continuous.Add(7,new NavPoint(0,0));
            Assert.IsTrue(continuous.SetRoute(7,new NavPoint(8,0),new[]{new NavPoint(8,0)}));
            for(int i=0;i<21;i++)continuous.Step(1d/30d);
            var saved=continuous.CaptureState();var restored=new NavCrowd(geometry,profile);restored.RestoreState(saved);
            saved.Units[0].Route[0]=new NavPoint(-8,0); // The restored route owns its copy.
            for(int i=0;i<100;i++){
                continuous.Step(1d/30d);restored.Step(1d/30d);
                var a=continuous.Units[0];var b=restored.Units[0];
                Assert.AreEqual(a.Position.X,b.Position.X);Assert.AreEqual(a.Position.Z,b.Position.Z);
                Assert.AreEqual(a.Heading,b.Heading);Assert.AreEqual(a.Outcome,b.Outcome);
                Assert.AreEqual(a.Moving,b.Moving);Assert.AreEqual(a.Route.Count,b.Route.Count);
            }
            Assert.Throws<System.ArgumentException>(()=>new NavCrowd(new NavGeometry(21,new NavObstacle[0],1),profile).RestoreState(continuous.CaptureState()));
        }
        private NavigationSession Session(){var s=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());s.Crowd.Add(0,new NavPoint(0,0));return s;}
        private static NavScheduledRequest Scheduled(NavigationSession source,NavigationRequest request,string defect=null)
        {
            var boundary=new NavPolygon(new[]{new NavPoint(-20,-20),new NavPoint(20,-20),new NavPoint(20,20),new NavPoint(-20,20)});
            var provider=new LayeredNavigationProvider("transport-rendezvous",1,1,1,new string('b',64),
                new[]{new NavLayerSurface("flat","ground",0,1,new NavTraversalArea(boundary,20,Array.Empty<NavObstacle>()))},
                Array.Empty<NavLayerPortal>());
            var identity=request.MemberIdentity;var group=source.Admission.Groups[0];
            var subscription=new NavRouteSubscription(request.Session,group.GroupId,group.RootOrderRevision+(defect=="root"?1:0),
                group.CommandSequence+(defect=="command"?1:0),group.IssuedTick+(defect=="issued"?1:0),identity.OrderRevision+(defect=="activation"?1:0),request.Entity,identity.Incarnation,
                identity.OrderRevision,identity.MobilityRevision+(defect=="mobility"?1:0),request.Request,request.StartLocation.Value,
                request.GoalLocation.Value,request.HeldIdentity);
            return NavScheduledRequest.ForSingleton(provider,new NavClearanceProfile("ground",1,"ground",(int)identity.MobilityRevision,request.Profile.Radius+(defect=="radius"?.1:0)),
                new NavGraphProfile(defect=="profile"?new string('c',64):NavigationAdmission.ExactProfileBinding(request.Profile),"distance",1,request.Profile.GridCell),
                new NavGraphCompileLimits(5000,5000,20000),subscription,5000);
        }
        [Test] public void SessionRestoreRebindsPendingAndBothMailboxesAndMatchesSuffix()
        {
            var geometry=new NavGeometry(20,new NavObstacle[0],2);var profile=new NavigationProfile();
            var continuous=new NavigationSession(4,geometry,profile);
            continuous.Crowd.Add(0,new NavPoint(0,0));continuous.Crowd.Add(1,new NavPoint(0,3));
            Assert.IsTrue(continuous.Move(0,new NavPoint(8,0)));
            NavigationRequest first;Assert.IsTrue(continuous.Requests.TryDequeue(out first));
            continuous.Answers.TryEnqueue(new NavigationAnswer(first,new[]{first.Goal}));
            continuous.Step(1d/30);for(int i=0;i<20;i++)continuous.Step(1d/30);
            Assert.IsTrue(continuous.Move(0,new NavPoint(9,0)));
            NavigationRequest obsolete;Assert.IsTrue(continuous.Requests.TryDequeue(out obsolete));
            continuous.Stop(0,false);continuous.Answers.TryEnqueue(new NavigationAnswer(obsolete,new[]{obsolete.Goal}));
            Assert.IsTrue(continuous.Move(0,new NavPoint(8,0)));
            Assert.IsTrue(continuous.Move(1,new NavPoint(8,3)));
            NavigationRequest queued;Assert.IsTrue(continuous.Requests.TryDequeue(out queued));
            continuous.Answers.TryEnqueue(new NavigationAnswer(queued,new[]{queued.Goal}));
            var saved=continuous.CaptureState();
            var restored=new NavigationSession(4,new NavGeometry(20,new NavObstacle[0],2),new NavigationProfile());
            restored.RestoreState(saved);
            Assert.AreEqual(continuous.PendingCount,restored.PendingCount);
            Assert.AreEqual(continuous.Requests.Count,restored.Requests.Count);
            Assert.AreEqual(continuous.Answers.Count,restored.Answers.Count);
            saved.AnswerMailbox[1].Route[0]=new NavPoint(-9,-9); // Restore owns the valid route copy.
            for(int tick=0;tick<120;tick++){
                NavigationRequest a,b;
                Assert.AreEqual(continuous.Requests.TryDequeue(out a),restored.Requests.TryDequeue(out b));
                if(a!=null){Assert.AreEqual(a.Request,b.Request);Assert.AreEqual(a.Order,b.Order);
                    continuous.Answers.TryEnqueue(new NavigationAnswer(a,new[]{a.Goal}));
                    restored.Answers.TryEnqueue(new NavigationAnswer(b,new[]{b.Goal}));}
                continuous.Step(1d/30);restored.Step(1d/30);
                Assert.AreEqual(continuous.AppliedResults,restored.AppliedResults);
                Assert.AreEqual(continuous.RejectedResults,restored.RejectedResults);
                Assert.AreEqual(continuous.UnreachableResults,restored.UnreachableResults);
                Assert.AreEqual(continuous.PendingCount,restored.PendingCount);
                for(int i=0;i<2;i++){
                    var u=continuous.Crowd.Units[i];var v=restored.Crowd.Units[i];
                    Assert.AreEqual(u.Position.X,v.Position.X);Assert.AreEqual(u.Position.Z,v.Position.Z);
                    Assert.AreEqual(u.Heading,v.Heading);Assert.AreEqual(u.Outcome,v.Outcome);
                    Assert.AreEqual(u.Moving,v.Moving);Assert.AreEqual(u.Route.Count,v.Route.Count);
                }
            }
            Assert.IsTrue(continuous.Move(1,new NavPoint(-8,3)));
            Assert.IsTrue(restored.Move(1,new NavPoint(-8,3)));
            NavigationRequest nextA,nextB;continuous.Requests.TryDequeue(out nextA);restored.Requests.TryDequeue(out nextB);
            Assert.AreEqual(nextA.Request,nextB.Request);
        }
        [Test] public void SessionRestoreRejectsWrongGenerationGeometryAndProfileBeforeInstallingUnits()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));var saved=source.CaptureState();
            var generation=new NavigationSession(2,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            Assert.Throws<System.ArgumentException>(()=>generation.RestoreState(saved));Assert.AreEqual(0,generation.Crowd.Units.Count);
            var geometry=new NavigationSession(1,new NavGeometry(21,new NavObstacle[0],1),new NavigationProfile());
            Assert.Throws<System.ArgumentException>(()=>geometry.RestoreState(saved));Assert.AreEqual(0,geometry.Crowd.Units.Count);
            var profile=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile("other",1,.72,4,4.5,.8,.15,3.6,.08));
            Assert.Throws<System.ArgumentException>(()=>profile.RestoreState(saved));Assert.AreEqual(0,profile.Crowd.Units.Count);
            saved.PendingRequestIndices[0]=99;
            var invalid=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            Assert.Throws<System.ArgumentException>(()=>invalid.RestoreState(saved));Assert.AreEqual(0,invalid.Crowd.Units.Count);
        }
        [Test] public void SessionRestorePreservesStaleOtherGenerationAnswer()
        {
            var old=Session();old.Move(0,new NavPoint(5,0));
            NavigationRequest oldRequest;old.Requests.TryDequeue(out oldRequest);
            var current=new NavigationSession(2,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            current.Crowd.Add(0,new NavPoint(0,0));current.Answers.TryEnqueue(new NavigationAnswer(oldRequest,new[]{oldRequest.Goal}));
            var restored=new NavigationSession(2,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            restored.RestoreState(current.CaptureState());
            current.ApplyResults();restored.ApplyResults();
            Assert.AreEqual(1,current.RejectedResults);Assert.AreEqual(current.RejectedResults,restored.RejectedResults);
            Assert.IsTrue(current.Move(0,new NavPoint(5,0)));Assert.IsTrue(restored.Move(0,new NavPoint(5,0)));
            NavigationRequest a,b;current.Requests.TryDequeue(out a);restored.Requests.TryDequeue(out b);
            Assert.AreEqual(1,a.Request);Assert.AreEqual(a.Request,b.Request);
        }
        [Test] public void SessionCaptureRejectsRequestHeldByExternalWorker()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            NavigationRequest request;source.Requests.TryDequeue(out request);
            Assert.Throws<System.InvalidOperationException>(()=>source.CaptureState());
            source.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal}));
            var saved=source.CaptureState();saved.AnswerMailbox=new NavigationAnswerState[0];
            var destination=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            Assert.Throws<System.ArgumentException>(()=>destination.RestoreState(saved));
            Assert.AreEqual(0,destination.Crowd.Units.Count);
        }
        [Test] public void RegisteredTransportReplaysOriginalFifoAndProbeAcrossCapture()
        {
            var source=Session();
            Assert.IsTrue(source.Move(0,new NavPoint(5,0)));
            Assert.IsTrue(source.TryTransferRoute(_=>true,out var held));
            Assert.AreEqual(1,source.RegisteredRouteCount);
            Assert.IsTrue(source.Move(0,new NavPoint(6,0))); // pending replacement
            var probe=source.Probe(1,1,new NavigationProfile(),new NavPoint(0,0),new NavPoint(3,0));
            Assert.NotNull(probe);
            Assert.IsTrue(source.TryTransferRoute(_=>true,out var replacement));
            Assert.AreEqual(2,source.RegisteredRouteCount);
            var saved=source.CaptureState();
            Assert.AreEqual(3,saved.RequestMailboxIndices.Length);
            Assert.AreEqual(held.Request,saved.Requests[saved.RequestMailboxIndices[0]].Request);
            Assert.AreEqual(replacement.Request,saved.Requests[saved.RequestMailboxIndices[1]].Request);
            Assert.AreEqual(probe.Request,saved.Requests[saved.RequestMailboxIndices[2]].Request);
            var restored=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            restored.RestoreState(saved);
            Assert.AreEqual(0,restored.RegisteredRouteCount);
            Assert.AreEqual(3,restored.Requests.Count);
            Assert.AreEqual(2,restored.PendingCount);
            Assert.AreEqual(PlayableWorldRestoreTests.Facts(source.Admission),
                PlayableWorldRestoreTests.Facts(restored.Admission),"Complete admission projection including producer probe");
            foreach(var expected in new[]{held,replacement,probe}){
                Assert.IsTrue(restored.Requests.TryDequeue(out var replay));
                Assert.AreEqual(expected.Request,replay.Request);
                Assert.IsTrue(restored.Answers.TryEnqueue(new NavigationAnswer(replay,new[]{replay.Goal})));
            }
            restored.ApplyResults();
            Assert.AreEqual(1,restored.RejectedResults); // superseded original
            Assert.AreEqual(1,restored.AppliedResults);
            Assert.IsTrue(restored.TryProbeAnswer(out var probeAnswer));
            Assert.AreEqual(probe.Request,probeAnswer.Request.Request);
            Assert.IsFalse(restored.TryProbeAnswer(out _));
        }
        [Test] public void FrozenAwaitingRoutesBarrierCapturesRegisteredInputForReplay()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            Assert.True(source.TryTransferRoute(_=>true,out var original));
            source.PrepareDeliveryBarrier();
            Assert.False(source.DeliveryBarrierReady);
            var saved=source.CaptureState();
            var restored=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            restored.RestoreState(saved);
            restored.PrepareDeliveryBarrier();
            Assert.False(restored.DeliveryBarrierReady);
            Assert.True(restored.Requests.TryDequeue(out var replay));
            Assert.AreEqual(original.Request,replay.Request);
            Assert.True(restored.Answers.TryEnqueue(new NavigationAnswer(replay,new[]{replay.Goal})));
            Assert.True(restored.DeliveryBarrierReady);
            restored.ApplyResults();Assert.AreEqual(1,restored.AppliedResults);
        }
        [Test] public void RegisteredTransportOwnsRequestUntilAnswerIsPublished()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            Assert.IsFalse(source.TryTransferRoute(_=>false,out _));
            Assert.AreEqual(1,source.Requests.Count);
            Assert.IsTrue(source.TryTransferRoute(_=>true,out var request));
            Assert.AreEqual(0,source.Requests.Count);
            var answer=new NavigationAnswer(request,new[]{request.Goal});
            Assert.IsTrue(source.TryCompleteRegisteredRoute(answer));
            Assert.IsFalse(source.TryCompleteRegisteredRoute(answer));
            Assert.AreEqual(0,source.RegisteredRouteCount);
            var saved=source.CaptureState();
            Assert.AreEqual(0,saved.RequestMailboxIndices.Length);
            Assert.AreEqual(1,saved.AnswerMailbox.Length);
        }
        [Test] public void RegisteredInputsCountAgainstCheckpointCapacityAndBackpressure()
        {
            var source=Session();
            Assert.True(source.Move(0,new NavPoint(5,0)));
            Assert.True(source.TryTransferRoute(_=>true,out var held));
            for(int i=1;i<NavigationSession.RouteRequestCapacity;i++)
                Assert.True(source.Move(0,new NavPoint(5+i*.001,0)));
            Assert.AreEqual(NavigationSession.RouteRequestCapacity-1,source.Requests.Count);
            Assert.False(source.Move(0,new NavPoint(7,0)));
            Assert.IsNull(source.Probe(1,1,new NavigationProfile(),new NavPoint(0,0),new NavPoint(3,0)));
            Assert.AreEqual(NavigationSession.RouteRequestCapacity,source.CaptureState().RequestMailboxIndices.Length);
            Assert.True(source.TryCompleteRegisteredRoute(new NavigationAnswer(held,new[]{held.Goal})));
            Assert.True(source.Move(0,new NavPoint(7,0)));
            Assert.AreEqual(NavigationSession.RouteRequestCapacity,source.CaptureState().RequestMailboxIndices.Length);
        }
        [Test] public void ConcurrentStaleDropNeverRejectsUnpublishedCurrentRequest()
        {
            var source=Session();var errors=new System.Collections.Generic.List<Exception>();
            int done=0;
            var worker=new System.Threading.Thread(()=>{
                try {
                    while(System.Threading.Volatile.Read(ref done)==0){source.TryDropStaleRoute(out _);System.Threading.Thread.Yield();}
                } catch(Exception error){lock(errors)errors.Add(error);}
            });
            worker.Start();
            for(int i=0;i<100;i++)Assert.True(source.Move(0,new NavPoint(5+i*.01,0)));
            System.Threading.Volatile.Write(ref done,1);
            Assert.True(worker.Join(5000));Assert.AreEqual(0,errors.Count);
            var state=source.CaptureState();
            var current=state.Requests[state.PendingRequestIndices[0]];
            Assert.AreEqual(100,current.Request);
            Assert.True(System.Linq.Enumerable.Contains(state.RequestMailboxIndices,state.PendingRequestIndices[0]));
            foreach(var answer in state.AnswerMailbox)Assert.Less(state.Requests[answer.RequestIndex].Request,current.Request);
        }
        [Test] public void GroupBatchAdmissionNeverPublishesPartialRoster()
        {
            var source=new NavigationSession(1,new NavGeometry(30,new NavObstacle[0],1),new NavigationProfile());
            var ids=new int[10];var goals=new NavPoint[10];
            for(int i=0;i<10;i++){ids[i]=i;source.Crowd.Add(i,new NavPoint(-10,-18+i*4));goals[i]=new NavPoint(10,-18+i*4);}
            int done=0,partial=0;Exception error=null;
            var observer=new System.Threading.Thread(()=>{
                try{while(System.Threading.Volatile.Read(ref done)==0){
                    var published=source.Admission;
                    if(published.Groups.Count>0&&(published.Groups[0].Members.Count!=10||published.Requests.Count!=10))
                        System.Threading.Interlocked.Increment(ref partial);
                    source.TryDropStaleRoute(out _);System.Threading.Thread.Yield();
                }}catch(Exception ex){error=ex;}
            });
            observer.Start();Assert.True(source.MoveGroup(ids,goals));
            System.Threading.Volatile.Write(ref done,1);Assert.True(observer.Join(5000));
            Assert.IsNull(error);
            Assert.AreEqual(0,partial);
            Assert.AreEqual(10,source.Admission.Groups[0].Members.Count);
            Assert.AreEqual(10,source.Requests.Count);Assert.AreEqual(0,source.Answers.Count);
        }
        [Test] public void CommandCohortIsInvisibleUntilFinishAndOpenBatchCannotBeCaptured()
        {
            var source=Session();source.Crowd.Add(1,new NavPoint(0,3));
            var command=new PlayableCommand(1,7,"player-1",PlayableCommandKind.Move,new[]{0,1},new NavPoint(5,0),origin:PlayableOrderOrigin.Human)
                .WithResolvedLocation(new NavLocation(new NavPoint(5,0),"flat"));
            source.PrepareCommandGroup(command,4,12,1);
            Assert.True(source.Move(0,new NavPoint(5,0)));
            Assert.AreEqual(0,source.Admission.PublishedThroughRequestSequence);
            Assert.False(source.TryDropStaleRoute(out _));
            Assert.False(source.TryTransferRoute(_=>true,out _));
            Assert.Throws<InvalidOperationException>(()=>source.CaptureState());
            Assert.True(source.Move(1,new NavPoint(5,3)));
            Assert.AreEqual(0,source.Admission.Groups.Count);
            source.FinishCommandGroup(true,new[]{0,1});
            Assert.AreEqual(2,source.Admission.PublishedThroughRequestSequence);
            Assert.AreEqual(2,source.Admission.Groups[0].Members.Count);
            Assert.AreEqual(2,source.Admission.Requests.Count);
            Assert.AreEqual(7,source.Admission.Groups[0].CommandSequence);
            Assert.AreEqual(12,source.Admission.Groups[0].IssuedTick);
            Assert.AreEqual(4,source.Admission.Groups[0].Members[0].ActivationRevision);
            Assert.AreEqual("flat",source.Admission.Groups[0].OriginalTerminal.Value.SurfaceId);
        }
        [Test] public void SchedulerPreparationCanPauseAcrossStopOrCaptureWithoutGateDeadlock()
        {
            void Run(bool stop)
            {
                var source=Session();source.Move(0,new NavPoint(5,0));
                var scheduler=new LayeredNavigationScheduler();
                using(var entered=new System.Threading.ManualResetEventSlim())
                using(var release=new System.Threading.ManualResetEventSlim()){
                    bool transferred=false;Exception error=null;
                    var worker=new System.Threading.Thread(()=>{
                        try{transferred=source.TryTransferRouteToScheduler(scheduler,r=>{
                            entered.Set();if(!release.Wait(5000))throw new Exception("Timed out waiting for preparation release.");
                            return Scheduled(source,r);
                        },out _);}catch(Exception ex){error=ex;}
                    });
                    worker.Start();Assert.True(entered.Wait(5000));
                    if(stop)source.Stop(0,true);else Assert.AreEqual(1,source.CaptureState().RequestMailboxIndices.Length);
                    release.Set();Assert.True(worker.Join(5000));Assert.IsNull(error);
                    if(stop){Assert.False(transferred);Assert.AreEqual(1,source.Requests.Count);Assert.True(source.TryDropStaleRoute(out _));}
                    else {Assert.True(transferred);Assert.AreEqual(0,source.Requests.Count);Assert.AreEqual(1,source.RegisteredRouteCount);}
                }
            }
            Run(true);Run(false);
        }
        [TestCase("root")][TestCase("command")][TestCase("issued")][TestCase("activation")]
        [TestCase("mobility")][TestCase("profile")][TestCase("radius")]
        public void SchedulerAdmissionRejectsMismatchedAuthorityEnvelopeBeforeSubmit(string defect)
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            var scheduler=new LayeredNavigationScheduler();
            Assert.Throws<InvalidOperationException>(()=>source.TryTransferRouteToScheduler(scheduler,
                request=>Scheduled(source,request,defect),out _));
            Assert.AreEqual(1,source.Requests.Count,defect);
            Assert.AreEqual(0,source.RegisteredRouteCount,defect);
            Assert.AreEqual(0,scheduler.PendingCount,defect);
            Assert.AreEqual(0,scheduler.ReadyCount,defect);
            Assert.AreEqual(0,scheduler.Counters.PeakQueue,defect);
        }
        [Test] public void FullAnswerMailboxRetainsRegisteredOwnershipUntilCapacityReturns()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            Assert.True(source.TryTransferRoute(_=>true,out var held));
            var filler=new NavigationRequest(1,999,42,1,new NavigationProfile(),held.Geometry,held.Start,held.Goal);
            for(int i=0;i<NavMailbox<NavigationAnswer>.Capacity;i++)
                Assert.True(source.Answers.TryEnqueue(new NavigationAnswer(filler,new[]{filler.Goal})));
            var answer=new NavigationAnswer(held,new[]{held.Goal});
            Assert.False(source.TryCompleteRegisteredRoute(answer));
            Assert.AreEqual(1,source.RegisteredRouteCount);
            Assert.True(source.Answers.TryDequeue(out _));
            Assert.True(source.TryCompleteRegisteredRoute(answer));
            Assert.AreEqual(0,source.RegisteredRouteCount);
        }
        [Test] public void UnserviceableRouteWaitsForAnswerCapacityAndKeepsTypedFailure()
        {
            var source=Session();Assert.True(source.Move(0,new NavPoint(5,0)));
            Assert.AreEqual(1,source.Requests.Count);
            var head=source.Requests.CopyItems()[0];
            var filler=new NavigationRequest(1,999,42,1,new NavigationProfile(),head.Geometry,head.Start,head.Goal);
            for(int i=0;i<NavMailbox<NavigationAnswer>.Capacity;i++)
                Assert.True(source.Answers.TryEnqueue(new NavigationAnswer(filler,new[]{filler.Goal})));
            Assert.False(source.TryRejectUnserviceableRoute(head,out _));
            Assert.AreEqual(1,source.Requests.Count);
            Assert.True(source.Answers.TryDequeue(out _));
            Assert.True(source.TryRejectUnserviceableRoute(head,out var removed));
            Assert.AreSame(head,removed);
            Assert.AreEqual(0,source.Requests.Count);
            Assert.True(source.Answers.TryDequeue(out var next));
            while(!ReferenceEquals(next.Request,head))Assert.True(source.Answers.TryDequeue(out next));
            Assert.AreEqual(NavSolveStatus.CapacityExceeded,next.Status);
        }
        [Test] public void TransferCallbackExceptionAndReentryLeaveMailboxHeadOwnedByAuthority()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            Assert.Throws<InvalidOperationException>(()=>source.TryTransferRoute(r=>{
                source.Requests.TryDequeue(out _);return true;
            },out _));
            Assert.Throws<InvalidOperationException>(()=>source.TryTransferRoute(r=>{
                source.CaptureState();return true;
            },out _));
            Assert.Throws<InvalidOperationException>(()=>source.TryTransferRoute(r=>throw new InvalidOperationException("admission failed"),out _));
            Assert.AreEqual(1,source.Requests.Count);
            Assert.AreEqual(0,source.RegisteredRouteCount);
            Assert.AreEqual(1,source.CaptureState().RequestMailboxIndices.Length);
        }
        [Test] public void StaleMailboxHeadMovesAtomicallyToRejectedAnswerBeforeNewerRequest()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            source.Move(0,new NavPoint(6,0));
            Assert.True(source.TryDropStaleRoute(out var stale));
            Assert.AreEqual(1,source.Requests.Count);
            Assert.AreEqual(1,source.Answers.Count);
            Assert.False(source.TryDropStaleRoute(out _));
            var saved=source.CaptureState();
            Assert.AreEqual(stale.Request,saved.Requests[saved.AnswerMailbox[0].RequestIndex].Request);
            source.ApplyResults();
            Assert.AreEqual(1,source.RejectedResults);
            Assert.AreEqual(1,source.PendingCount);
        }
        [Test] public void RebindAndGeometryChangeRetainRegisteredStaleResultsForReplayAndRejection()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            Assert.True(source.TryTransferRoute(_=>true,out var old));
            source.Rebind(new NavigationProfile("next",2,.72,4,4.5,.8,.15,3.6,.08));
            Assert.AreEqual(2,source.CaptureState().RequestMailboxIndices.Length);
            Assert.True(source.TryCompleteRegisteredRoute(new NavigationAnswer(old,new[]{old.Goal})));
            source.ApplyResults();Assert.AreEqual(1,source.RejectedResults);
            Assert.True(source.TryTransferRoute(_=>true,out var rebound));
            source.ChangeGeometry(new NavGeometry(20,new NavObstacle[0],2));
            Assert.AreEqual(2,source.CaptureState().RequestMailboxIndices.Length);
            Assert.True(source.TryCompleteRegisteredRoute(new NavigationAnswer(rebound,new[]{rebound.Goal})));
            source.ApplyResults();Assert.AreEqual(2,source.RejectedResults);
        }
        [Test] public void AdmissionPublicationIsDetachedAndRetainsOriginalIssuance()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            var first=source.Admission;
            Assert.AreEqual(1,first.Groups.Count);
            Assert.AreEqual(1,first.Requests.Count);
            Assert.AreEqual(0,first.Groups[0].Members[0].Entity);
            Assert.AreEqual(first.Requests[0],first.Groups[0].Members[0].PendingRequest);
            source.Move(0,new NavPoint(6,0));
            var second=source.Admission;
            Assert.AreNotSame(first,second);
            Assert.AreEqual(5,first.Groups[0].Members[0].AssignedEndpoint.Value.Position.X);
            Assert.AreEqual(6,second.Groups[0].Members[0].AssignedEndpoint.Value.Position.X);
            Assert.IsFalse(second.Contains(first.Requests[0]));
            Assert.AreEqual(first.Groups[0].GroupId,second.Groups[0].GroupId);
            Assert.AreEqual(first.Groups[0].RootOrderRevision,second.Groups[0].RootOrderRevision);
        }
        [Test] public void HoldPublishesFreshMobilityWithoutCancellingSibling()
        {
            var source=Session();source.Crowd.Add(1,new NavPoint(0,3));
            Assert.True(source.MoveGroup(new[]{0,1},new[]{new NavPoint(5,0),new NavPoint(5,3)}));
            var before=source.Admission;
            var sibling=before.Groups[0].Members[1].PendingRequest;
            source.Stop(0,true);
            var after=source.Admission;
            Assert.AreEqual(2,after.Groups[0].Members.Count);
            Assert.Greater(after.Groups[0].Members[0].MobilityRevision,before.Groups[0].Members[0].MobilityRevision);
            Assert.IsNull(after.Groups[0].Members[0].PendingRequest);
            Assert.AreSame(sibling,after.Groups[0].Members[1].PendingRequest);
            Assert.True(after.Contains(sibling));
            Assert.False(after.Contains(before.Groups[0].Members[0].PendingRequest));
        }
        [Test] public void RegisteredSchedulerCompilerFieldConnectorAndReadyCaptureReplayOriginalInput()
        {
            var blocker=new NavObstacle(-.5,-1,.5,1);
            var geometry=new NavGeometry(10,new[]{blocker},1);
            var source=new NavigationSession(1,geometry,new NavigationProfile());
            source.Crowd.Add(0,new NavPoint(-3,0));
            Assert.IsTrue(source.Move(0,new NavPoint(3,0)));
            var boundary=new NavPolygon(new[]{new NavPoint(-10,-10),new NavPoint(10,-10),new NavPoint(10,10),new NavPoint(-10,10)});
            var area=new NavTraversalArea(boundary,10,new[]{blocker});
            var provider=new LayeredNavigationProvider("transport-test",1,1,1,new string('b',64),
                new[]{new NavLayerSurface("flat","ground",0,1,area)},Array.Empty<NavLayerPortal>());
            var scheduler=new LayeredNavigationScheduler();
            var start=new NavLocation(new NavPoint(-3,0),"flat");
            var goal=new NavLocation(new NavPoint(3,0),"flat");
            var group=source.Admission.Groups[0];
            Assert.IsTrue(source.TryTransferRouteToScheduler(scheduler,request=>{
                var identity=request.MemberIdentity;
                var subscription=new NavRouteSubscription(1,group.GroupId,group.RootOrderRevision,
                    group.CommandSequence,group.IssuedTick,identity.OrderRevision,request.Entity,
                    identity.Incarnation,identity.OrderRevision,identity.MobilityRevision,request.Request,
                    start,goal,request.HeldIdentity);
                return new NavScheduledRequest(provider,new NavClearanceProfile("ground",1,"ground",(int)identity.MobilityRevision,.72),
                    new NavGraphProfile(NavigationAdmission.ExactProfileBinding(request.Profile),"distance",1,.8),
                    new NavGraphCompileLimits(5000,5000,20000),new NavTerminalRegion("target",1,goal,new[]{goal}),
                    subscription,5000);
            },out var original));
            void Replay(NavigationSessionState saved)
            {
                var restored=new NavigationSession(1,new NavGeometry(10,new[]{blocker},1),new NavigationProfile());
                restored.RestoreState(saved);
                Assert.AreEqual(1,restored.Requests.Count);
                Assert.AreEqual(0,restored.RegisteredRouteCount);
                Assert.True(restored.Requests.TryDequeue(out var replay));
                Assert.AreEqual(original.Request,replay.Request);
                Assert.False(restored.Requests.TryDequeue(out _));
            }
            Replay(source.CaptureState());
            bool graph=false,field=false,connector=false,ready=false;
            for(int epoch=0;epoch<1000&&!ready;epoch++){
                scheduler.Advance(epoch,new NavEpochQuota(250,250,250),_=>true,100);
                var counters=scheduler.Counters;
                if(!graph&&counters.PhysicalGraphWork>0){Replay(source.CaptureState());graph=true;}
                if(!field&&counters.PhysicalFieldWork>0){Replay(source.CaptureState());field=true;}
                if(!connector&&counters.ConnectorWork>0){Replay(source.CaptureState());connector=true;}
                if(scheduler.ReadyCount>0){Replay(source.CaptureState());ready=true;}
            }
            var resultStatus=scheduler.TryTake(_=>true,out var completion)?completion.Status.ToString():"none";
            Assert.True(graph,"compiler capture; status="+resultStatus);
            Assert.True(field,"field capture; status="+resultStatus);
            Assert.True(connector,"connector capture; status="+resultStatus+" graph="+scheduler.Counters.PhysicalGraphWork+" field="+scheduler.Counters.PhysicalFieldWork);
            Assert.True(ready,"ready capture; status="+resultStatus);
        }
        [Test] public void SessionRestorePreservesReservationsAndGeometryReissueIntent()
        {
            var source=new NavigationSession(3,new NavGeometry(50,new NavObstacle[0],1),new NavigationProfile());
            source.Crowd.Add(0,new NavPoint(-24,-4));source.Crowd.Add(1,new NavPoint(-24,4));
            var goal=source.AllocateArrivalSlots(new NavPoint(24,0),new[]{0})[0];source.Move(0,goal);
            var destination=new NavigationSession(3,new NavGeometry(50,new NavObstacle[0],1),new NavigationProfile());
            destination.RestoreState(source.CaptureState());
            var a=source.AllocateArrivalSlots(new NavPoint(24,0),new[]{1});
            var b=destination.AllocateArrivalSlots(new NavPoint(24,0),new[]{1});
            Assert.AreEqual(a[0].X,b[0].X);Assert.AreEqual(a[0].Z,b[0].Z);
            source.ChangeGeometry(new NavGeometry(50,new NavObstacle[0],2));
            destination.ChangeGeometry(new NavGeometry(50,new NavObstacle[0],2));
            NavigationRequest nextA,nextB;
            Assert.IsTrue(source.Requests.TryDequeue(out nextA));Assert.IsTrue(destination.Requests.TryDequeue(out nextB));
            Assert.AreEqual(nextA.Request,nextB.Request);Assert.AreEqual(nextA.Order,nextB.Order);
            Assert.AreEqual(goal.X,nextB.Goal.X);Assert.AreEqual(goal.Z,nextB.Goal.Z);
            source.Answers.TryEnqueue(new NavigationAnswer(nextA,new NavPoint[0]));
            destination.Answers.TryEnqueue(new NavigationAnswer(nextB,new NavPoint[0]));
            source.Step(1d/30);destination.Step(1d/30);
            Assert.AreEqual(1,destination.UnreachableResults);Assert.AreEqual(source.Crowd.Units[0].Outcome,destination.Crowd.Units[0].Outcome);
        }
        [Test] public void StopRejectsLateResult(){var s=Session();s.Move(0,new NavPoint(5,0));NavigationRequest r;s.Requests.TryDequeue(out r);s.Stop(0,true);s.Answers.TryEnqueue(new NavigationAnswer(r,new[]{new NavPoint(5,0)}));s.Step(1d/30);Assert.AreEqual(1,s.RejectedResults);Assert.AreEqual(0,s.Crowd.Units[0].Position.X);Assert.IsTrue(s.Crowd.Units[0].Held);}
        [Test] public void GeometryRejectsLateResult(){var s=Session();s.Move(0,new NavPoint(5,0));NavigationRequest r;s.Requests.TryDequeue(out r);s.ChangeGeometry(new NavGeometry(20,new[]{new NavObstacle(2,-2,3,2)},2));s.Answers.TryEnqueue(new NavigationAnswer(r,new[]{new NavPoint(5,0)}));s.Step(1d/30);Assert.AreEqual(1,s.RejectedResults);Assert.IsFalse(s.Crowd.Units[0].Moving);}
        [Test] public void NewOrderAndSessionRejectOldResult(){var s=Session();s.Move(0,new NavPoint(5,0));NavigationRequest r;s.Requests.TryDequeue(out r);s.Move(0,new NavPoint(-5,0));s.Answers.TryEnqueue(new NavigationAnswer(r,new[]{new NavPoint(5,0)}));s.ApplyResults();Assert.AreEqual(1,s.RejectedResults);var other=Session();other.Answers.TryEnqueue(new NavigationAnswer(r,new[]{new NavPoint(5,0)}));other.ApplyResults();Assert.AreEqual(1,other.RejectedResults);}
        [Test] public void ValidRouteUsesDomainMotion(){var s=Session();s.Move(0,new NavPoint(5,0));NavigationRequest r;s.Requests.TryDequeue(out r);s.Answers.TryEnqueue(new NavigationAnswer(r,new[]{new NavPoint(5,0)}));s.Step(1d/30);Assert.AreEqual(1,s.AppliedResults);Assert.Greater(s.Crowd.Units[0].Position.X,0);}
        [Test] public void PartialResultCannotBecomeArrival(){var s=Session();s.Move(0,new NavPoint(5,0));NavigationRequest r;s.Requests.TryDequeue(out r);s.Answers.TryEnqueue(new NavigationAnswer(r,new[]{new NavPoint(2,0)}));s.Step(1d/30);Assert.AreEqual(1,s.RejectedResults);Assert.AreEqual(NavigationOutcome.Rejected,s.Crowd.Units[0].Outcome);}
        [Test] public void InFlightGroupsReserveDistinctArrivalSlotsAndStopReleasesThem()
        {
            var profile=new NavigationProfile();var s=new NavigationSession(1,new NavGeometry(50,new NavObstacle[0],1),profile);
            s.Crowd.Add(0,new NavPoint(-24,-4));s.Crowd.Add(1,new NavPoint(-24,4));
            var first=s.AllocateArrivalSlots(new NavPoint(24,0),new[]{0});Assert.IsTrue(s.Move(0,first[0]));
            var second=s.AllocateArrivalSlots(new NavPoint(24,0),new[]{1});
            double dx=first[0].X-second[0].X,dz=first[0].Z-second[0].Z;Assert.GreaterOrEqual(dx*dx+dz*dz,profile.ArrivalSlotSpacing*profile.ArrivalSlotSpacing);
            s.Stop(0,false);var after=s.AllocateArrivalSlots(new NavPoint(24,0),new[]{1});Assert.AreEqual(first[0].X,after[0].X);Assert.AreEqual(first[0].Z,after[0].Z);
        }
    }
}

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

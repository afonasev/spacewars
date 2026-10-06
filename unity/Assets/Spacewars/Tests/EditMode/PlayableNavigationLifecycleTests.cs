using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableNavigationLifecycleTests
    {
        private static NavigationSession Session(long generation,NavGeometry geometry)
        { var session=new NavigationSession(generation,geometry,new NavigationProfile("unity-playable-u3-v1",1,.72,4,4.5,.8,.15,3.6,.08)); session.Crowd.Add(8,new NavPoint(-8,0)); return session; }

        [Test] public void SameRevisionDifferentGeometryContextRejectsAnswer()
        {
            var original=new NavGeometry(20,new NavObstacle[0],4); var session=Session(3,original);
            Assert.IsTrue(session.Move(8,new NavPoint(8,0))); NavigationRequest request; Assert.IsTrue(session.Requests.TryDequeue(out request));
            session.ChangeGeometry(new NavGeometry(20,new NavObstacle[0],4));
            session.Answers.TryEnqueue(new NavigationAnswer(request,new[]{new NavPoint(8,0)})); session.ApplyResults();
            Assert.AreEqual(1,session.RejectedResults);
        }

        [Test] public void MatchingRequestOrderEntityFromPriorGenerationCannotReviveUnit()
        {
            var old=Session(3,new NavGeometry(20,new NavObstacle[0],1)); old.Move(8,new NavPoint(8,0)); NavigationRequest request; old.Requests.TryDequeue(out request);
            var current=Session(4,new NavGeometry(20,new NavObstacle[0],1)); current.Move(8,new NavPoint(8,0)); current.Answers.TryEnqueue(new NavigationAnswer(request,new[]{new NavPoint(8,0)})); current.ApplyResults();
            Assert.AreEqual(1,current.RejectedResults); Assert.IsFalse(current.Crowd.Units[0].Moving);
        }

        [Test] public void DeathRemovesPendingRouteAndLateAnswer()
        {
            var session=Session(3,new NavGeometry(20,new NavObstacle[0],1)); session.Move(8,new NavPoint(8,0)); NavigationRequest request; session.Requests.TryDequeue(out request);
            Assert.IsTrue(session.Remove(8)); session.Answers.TryEnqueue(new NavigationAnswer(request,new[]{new NavPoint(8,0)})); session.ApplyResults();
            Assert.AreEqual(1,session.RejectedResults); Assert.AreEqual(0,session.Crowd.Units.Count);
        }

        [Test] public void StopRejectsDelayedPlannerAnswerWithoutBlockingTicks()
        {
            var session=Session(3,new NavGeometry(20,new NavObstacle[0],1)); session.Move(8,new NavPoint(8,0)); NavigationRequest request; session.Requests.TryDequeue(out request);
            for(int i=0;i<5;i++)session.Step(1d/30d); session.Stop(8,true);
            session.Answers.TryEnqueue(new NavigationAnswer(request,new[]{new NavPoint(8,0)})); session.Step(1d/30d);
            Assert.AreEqual(1,session.RejectedResults); Assert.IsTrue(session.Crowd.Units[0].Held); Assert.AreEqual(-8d,session.Crowd.Units[0].Position.X);
        }
    }
}

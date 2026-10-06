using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationLiveTests
    {
        [Test] public void DelayedPlannerDoesNotBlockTicksAndStopRejectsLateAnswer()
        {
            var runtime=new NavigationRuntime(7,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile(),new[]{new NavPoint(0,0)});
            try{
                Assert.IsTrue(runtime.TrySubmit(new NavigationIntent(0,new NavPoint(10,0))));
                NavigationRequest request=null;
                Assert.IsTrue(SpinWait.SpinUntil(()=>runtime.Requests.TryDequeue(out request),1000));
                long before=runtime.Latest.Tick;Assert.IsTrue(SpinWait.SpinUntil(()=>runtime.Latest.Tick>=before+3,1000));
                Assert.AreEqual(0,runtime.Latest.Positions[0].X);
                runtime.TrySubmit(new NavigationIntent(0,new NavPoint(0,0),true,true));before=runtime.Latest.Tick;
                Assert.IsTrue(SpinWait.SpinUntil(()=>runtime.Latest.Tick>=before+2,1000));
                runtime.Answers.TryEnqueue(new NavigationAnswer(request,new[]{new NavPoint(10,0)}));
                Assert.IsTrue(SpinWait.SpinUntil(()=>runtime.Latest.Rejected==1,1000));
                Assert.AreEqual(0,runtime.Latest.Positions[0].X);Assert.IsNull(runtime.Latest.Failure);
            }finally{runtime.Dispose();Assert.IsTrue(SpinWait.SpinUntil(()=>runtime.IsStopped,1000));}
        }
        [Test] public void FullRequestMailboxPreservesActiveMove()
        {
            var session=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());session.Crowd.Add(0,new NavPoint(0,0));
            session.Move(0,new NavPoint(10,0));NavigationRequest request;session.Requests.TryDequeue(out request);session.Answers.TryEnqueue(new NavigationAnswer(request,new[]{new NavPoint(10,0)}));session.Step(1d/30);
            for(int i=0;i<NavMailbox<NavigationRequest>.Capacity;i++)Assert.IsTrue(session.Requests.TryEnqueue(request));
            Assert.IsFalse(session.Move(0,new NavPoint(-10,0)));Assert.IsTrue(session.Crowd.Units[0].Moving);Assert.AreEqual(10,session.Crowd.Units[0].Goal.X);
        }
    }
}

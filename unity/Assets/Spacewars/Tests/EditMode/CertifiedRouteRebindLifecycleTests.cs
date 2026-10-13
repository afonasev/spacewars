using System;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CertifiedRouteRebindLifecycleTests
    {
        [Test] public void WorkerRebindRejectsRegisteredStaleAnswerThenCompletesNewSubscriptionWithinRetentionCap()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);
            session.Crowd.Add(1,new NavPoint(-5,0));Assert.True(session.Move(1,new NavPoint(5,0)));
            Assert.True(session.TryTransferRoute(_=>true,out var stale));
            session.Rebind(new NavigationProfile(PlayableProfile.Default.Navigation.ProfileId,2,.72,4,4.5,.8,.15,3.6,.08));
            Assert.True(session.IsPending(1));Assert.AreEqual(1,session.RegisteredRouteCount);
            Assert.True(session.TryCompleteRegisteredRoute(new NavigationAnswer(stale,new[]{stale.Goal})));
            var binding=new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
            var worker=new CertifiedRouteWorker(binding,4);
            try{
                Assert.True(SpinWait.SpinUntil(()=>{session.ApplyResults();return session.RejectedResults==1&&session.AppliedResults==1;},10000),
                    "The old registered completion must be rejected and its replacement subscription must complete.");
                Assert.LessOrEqual(worker.Publication.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
                Assert.Null(worker.Publication.Failure);
                Assert.NotNull(session.InstalledExecutionFor(1));
                session.Stop(1,true);Assert.Zero(session.HostRetainedBytes);
            }finally{worker.Dispose();Assert.True(SpinWait.SpinUntil(()=>worker.IsStopped,5000));}
            Assert.Zero(session.HostRetainedBytes);
        }
    }
}

using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CertifiedRallyProbeTests
    {
        [Test] public void ProducerProbeDoesNotJoinMovementDeliveryBarrier()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var p=PlayableProfile.Default;
            var session=new NavigationSession(1,geometry,p.Navigation);session.Crowd.Add(1,new NavPoint(-5,0));
            Assert.IsNotNull(session.Probe(7,1,p.Navigation,new NavPoint(-4,2),new NavPoint(4,2)));
            session.PrepareDeliveryBarrier();Assert.IsTrue(session.DeliveryBarrierReady,"A producer proof must not stop gameplay");
            Assert.IsTrue(session.Move(1,new NavPoint(5,0)));session.PrepareDeliveryBarrier();Assert.IsFalse(session.DeliveryBarrierReady,"Movement delivery remains a complete fixed-step barrier");
            while(session.Requests.TryDequeue(out var request))if(request.Entity==1)session.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal}));
            Assert.IsTrue(session.DeliveryBarrierReady);session.Step(1d/30);Assert.AreEqual(1,session.AppliedResults);Assert.AreEqual(1,session.PendingCount,"Producer proof remains pending, not accepted");
        }
        [TestCase(false,0)][TestCase(false,1)][TestCase(true,0)][TestCase(true,1)]
        public void TypedProducerProbeCompletesWithCertifiedHost(bool authored,long order)
        {
            var map=authored?new FoundryMap(new FoundryProfileData()):null;
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var geometry=authored?new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision):new NavGeometry(32,Array.Empty<NavObstacle>(),1);
            var session=new NavigationSession(1,geometry,profile.Navigation,terrain:map);
            var typed=profile.Navigation.ForUnit(profile.TankCollisionRadius,profile.TankSpeed,profile.TankTurnSpeed);
            Assert.IsNotNull(session.Probe(7,order,typed,new NavPoint(0,0),new NavPoint(1,0)));
            using(var host=new UnityHostRouteService())
            {
                var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                for(int i=0;i<100&&session.PendingCount>0;i++){host.Service(binding,4);session.ApplyResults();}
                Assert.AreEqual(0,session.PendingCount,"rejected="+session.RejectedResults+" corridor="+typeof(NavigationSession).GetProperty("LastCorridorFailure",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(session)+" requests="+session.Requests.Count);
                Assert.IsTrue(session.TryProbeAnswer(out var result));Assert.AreEqual(NavSolveStatus.Ready,result.Status);Assert.AreEqual(order,result.Corridor.Order);
                Assert.AreEqual(NavRouteProvenance.ProducerProbe,result.Corridor.Provenance);Assert.AreEqual(typed.Radius,result.Corridor.Radius);
            }
        }
    }
}

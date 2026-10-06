using System;
using System.Diagnostics;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    // Shipped Resources require engine references; pure geometry fixtures stay engine-free.
    public sealed class EndpointResourceAttachmentTests
    {
        private static NavPoint FloorCenter(NavGeometry geometry, NavigationProfile profile, NavPoint point)
        {
            return new NavPoint(-geometry.HalfExtent + (Math.Floor((point.X + geometry.HalfExtent) / profile.GridCell) + .5) * profile.GridCell,
                -geometry.HalfExtent + (Math.Floor((point.Z + geometry.HalfExtent) / profile.GridCell) + .5) * profile.GridCell);
        }

        private static void Checked(NavGeometry geometry, NavigationProfile profile, NavPoint start, NavPoint goal, NavPoint[] route)
        {
            Assert.That(route.Length, Is.GreaterThan(0));
            var previous = start;
            foreach (var point in route)
            {
                Assert.True(geometry.IsFree(point, profile.Radius));
                Assert.True(geometry.SegmentFree(previous, point, profile.Radius), "Every connector retains the typed footprint");
                previous = point;
            }
            Assert.AreEqual(goal, previous);
        }

        [Test]
        public void OrdinaryExplorer5ExactAcceptedMapPositionHasBlockedFloorButReachableGoal()
        {
            var playable = PlayableProfile.Create(
                UnityEngine.JsonUtility.FromJson<PlayableProfileData>(UnityEngine.Resources.Load<UnityEngine.TextAsset>("PlayableProfile").text),
                new ThreeCrossingsMap(UnityEngine.JsonUtility.FromJson<ThreeCrossingsProfileData>(UnityEngine.Resources.Load<UnityEngine.TextAsset>("ThreeCrossingsProfile").text)));
            Assert.AreEqual("unity-owner-research-queue-u6-v1", playable.ProfileId);
            Assert.AreEqual(3, playable.AuthoredMap.Revision);
            var geometry = new NavGeometry(playable.ArenaHalfExtent, PlayableMap.StaticObstacles(playable), 1);
            var profile = playable.Navigation.ForUnit(playable.ExplorerCollisionRadius, playable.ExplorerSpeed, playable.ExplorerTurnSpeed);
            var start = new NavPoint(22.798198914236519, -27.700871260799971);
            var goal = new NavPoint(43.663551401869157, 33.996219281663514);
            Assert.True(geometry.IsFree(start, profile.Radius));
            Assert.False(geometry.SegmentFree(start, goal, profile.Radius));
            var floor = FloorCenter(geometry, profile, start);
            Assert.False(geometry.SegmentFree(start, floor, profile.Radius), "Pre-F3 floor-only attachment rejects the recorded legal position");
            var router = new SharedFlowRouter(geometry, profile);
            var watch = Stopwatch.StartNew();
            var route = router.FindPath(start, goal);
            watch.Stop();
            Checked(geometry, profile, start, goal, route);
            TestContext.WriteLine("F3 exact Explorer5 field diagnostic ms=" + watch.Elapsed.TotalMilliseconds + " rebuilds=" + router.RebuildCount);
            for (int i = 0; i < 3; i++) CollectionAssert.AreEqual(route, router.FindPath(start, goal));
        }
    }
}

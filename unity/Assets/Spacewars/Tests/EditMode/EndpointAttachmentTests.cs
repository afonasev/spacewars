using System;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class EndpointAttachmentTests
    {
        private static NavigationProfile Small => new NavigationProfile("attachment-fixture", 1, .1, 4, 4.5, .8, .15, 3.6, .08);

        [TestCase(false)]
        [TestCase(true)]
        public void BlockedFloorAttachmentIsSymmetricForStartAndGoal(bool reverse)
        {
            var geometry = new NavGeometry(4, new[] { new NavObstacle(new NavPoint(-2.8, .4), .1), new NavObstacle(-.3, -2, .3, 2) }, 1);
            var start = new NavPoint(-2.81, .01);
            var goal = new NavPoint(3, 0);
            Assert.True(geometry.IsFree(start, Small.Radius));
            Assert.False(geometry.IsFree(FloorCenter(geometry, Small, start), Small.Radius));
            if (reverse) { var swap = start; start = goal; goal = swap; }
            Checked(geometry, Small, start, goal, new SharedFlowRouter(geometry, Small).FindPath(start, goal));
        }

        [Test]
        public void DisconnectedNearestGoalCenterDoesNotHideReachableAlternative()
        {
            // The exact endpoint joins both sides of this slit, but no grid column crosses it.
            var geometry = new NavGeometry(4, new[] { new NavObstacle(-4, -.15, 2.85, .15), new NavObstacle(3.35, -.15, 4, .15) }, 1);
            var start = new NavPoint(-3, -2);
            var goal = new NavPoint(3.1, 0);
            var floor = FloorCenter(geometry, Small, goal);
            Assert.True(geometry.SegmentFree(goal, floor, Small.Radius));
            Assert.AreEqual(0, new SharedFlowRouter(geometry, Small).FindPath(start, floor).Length, "Nearest floor field is disconnected");
            Checked(geometry, Small, start, goal, new SharedFlowRouter(geometry, Small).FindPath(start, goal));
        }

        [Test]
        public void HeldCircleConnectorRetainsFullTypedRadius()
        {
            var held = new NavPoint(-2.8, .4);
            var geometry = new NavGeometry(4, new[] { new NavObstacle(held, .3), new NavObstacle(-.3, -2, .3, 2) }, 1);
            var start = new NavPoint(-2.81, -.11);
            var goal = new NavPoint(3, 0);
            Checked(geometry, Small, start, goal, new SharedFlowRouter(geometry, Small).FindPath(start, goal));
        }

        [Test]
        public void EndpointNeighborhoodDoesNotCrossASealedWall()
        {
            var geometry = new NavGeometry(4, new[] { new NavObstacle(-.15, -4, .15, 4) }, 1);
            Assert.AreEqual(0, new SharedFlowRouter(geometry, Small).FindPath(new NavPoint(-2, 0), new NavPoint(2, 0)).Length);
        }

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

    }
}

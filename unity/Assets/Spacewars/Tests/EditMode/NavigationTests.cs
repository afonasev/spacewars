using System;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationTests
    {
        [Test]
        public void GeometryUsesExactCircleClearance()
        {
            var geometry = new NavGeometry(8d, new[] { new NavObstacle(-1d, -1d, 1d, 1d) }, 1);
            Assert.IsFalse(geometry.IsFree(new NavPoint(1.5d, 1.5d), .72d));
            Assert.IsFalse(geometry.SegmentFree(new NavPoint(-3d, 1.5d), new NavPoint(3d, 1.5d), .72d));
            Assert.IsTrue(geometry.SegmentFree(new NavPoint(-3d, 2d), new NavPoint(3d, 2d), .72d));
        }

        [Test]
        public void FieldRejectsUnreachableAndFindsCheckedTurnRoute()
        {
            var profile = new NavigationProfile();
            var wall = new NavGeometry(8d, new[] { new NavObstacle(-.3d, -8d, .3d, 8d) }, 1);
            Assert.AreEqual(0, new SharedFlowRouter(wall, profile).FindPath(new NavPoint(-4d, 0d), new NavPoint(4d, 0d)).Length);
            var turn = new NavGeometry(8d, new[] { new NavObstacle(-1d, -2d, 1d, 2d) }, 1);
            var router = new SharedFlowRouter(turn, profile); NavPoint start = new NavPoint(-4d, 0d), goal = new NavPoint(4d, 0d); NavPoint[] route = router.FindPath(start, goal);
            Assert.Greater(route.Length, 1); NavPoint previous = start;
            foreach (NavPoint point in route) { Assert.IsTrue(turn.SegmentFree(previous, point, profile.Radius)); previous = point; }
            Assert.AreEqual(1, router.RebuildCount);
        }

        [Test]
        public void CrowdNeverOverlapsAndStopHoldClearTheRoute()
        {
            var profile = new NavigationProfile(); var geometry = new NavGeometry(10d, new NavObstacle[0], 1); var crowd = new NavCrowd(geometry, profile);
            crowd.Add(1, new NavPoint(-3d, 0d)); crowd.Add(2, new NavPoint(3d, 0d));
            Assert.IsTrue(crowd.SetRoute(1, new NavPoint(3d, 0d), new[] { new NavPoint(3d, 0d) }));
            Assert.IsTrue(crowd.SetRoute(2, new NavPoint(-3d, 0d), new[] { new NavPoint(-3d, 0d) }));
            for (int tick = 0; tick < 120; tick++) { crowd.Step(1d / 30d); NavPoint a = crowd.Units[0].Position, b = crowd.Units[1].Position; double dx = a.X - b.X, dz = a.Z - b.Z; Assert.GreaterOrEqual(dx * dx + dz * dz, 4d * profile.Radius * profile.Radius - .00000001d); }
            crowd.Stop(1, true); Assert.IsTrue(crowd.Units[0].Held); Assert.IsFalse(crowd.Units[0].Moving); Assert.AreEqual(0, crowd.Units[0].Route.Count);
        }

        [Test]
        public void CrowdTurnsInPlaceThenCompletesACheckedCornerRoute()
        {
            var profile = new NavigationProfile(); var geometry = new NavGeometry(10d, new[] { new NavObstacle(-1d, -2d, 1d, 2d) }, 1); var crowd = new NavCrowd(geometry, profile);
            crowd.Add(1, new NavPoint(-3d, 0d));
            Assert.IsTrue(crowd.SetRoute(1, new NavPoint(3d, 0d), new[] { new NavPoint(-3d, 3d), new NavPoint(3d, 3d), new NavPoint(3d, 0d) }));
            crowd.Step(1d / 30d); Assert.AreEqual(-3d, crowd.Units[0].Position.X); Assert.AreEqual(0d, crowd.Units[0].Position.Z); Assert.Greater(crowd.Units[0].Heading, 0d);
            for (int tick = 0; tick < 900 && crowd.Units[0].Moving; tick++) crowd.Step(1d / 30d);
            Assert.IsFalse(crowd.Units[0].Moving); Assert.LessOrEqual(Math.Sqrt((crowd.Units[0].Position.X - 3d) * (crowd.Units[0].Position.X - 3d) + crowd.Units[0].Position.Z * crowd.Units[0].Position.Z), profile.ArrivalTolerance);
        }

        [Test]
        public void OpposingUnitsUseComplementaryLocalRepairsAndReachWithoutOverlap()
        {
            var profile = new NavigationProfile(); var geometry = new NavGeometry(12d, new NavObstacle[0], 1); var crowd = new NavCrowd(geometry, profile);
            crowd.Add(1, new NavPoint(-4d, 0d)); crowd.Add(2, new NavPoint(4d, 0d));
            crowd.SetRoute(1, new NavPoint(4d, 0d), new[] { new NavPoint(4d, 0d) }); crowd.SetRoute(2, new NavPoint(-4d, 0d), new[] { new NavPoint(-4d, 0d) });
            for (int tick = 0; tick < 1200 && (crowd.Units[0].Moving || crowd.Units[1].Moving); tick++) { crowd.Step(1d / 30d); double dx = crowd.Units[0].Position.X - crowd.Units[1].Position.X, dz = crowd.Units[0].Position.Z - crowd.Units[1].Position.Z; Assert.GreaterOrEqual(dx * dx + dz * dz, 4d * profile.Radius * profile.Radius - .00000001d); }
            Assert.AreEqual(NavigationOutcome.Arrived,crowd.Units[0].Outcome); Assert.AreEqual(NavigationOutcome.Arrived,crowd.Units[1].Outcome);
        }

        [Test]
        public void SharedWaypointDoesNotPinUnitsBeforeTheirDistinctArrivalSlots()
        {
            var profile = new NavigationProfile(); var geometry = new NavGeometry(20d, new NavObstacle[0], 1); var crowd = new NavCrowd(geometry, profile);
            crowd.Add(1, new NavPoint(-8d, -1d)); crowd.Add(2, new NavPoint(-8d, 1d));
            Assert.IsTrue(crowd.SetRoute(1, new NavPoint(8d, -1d), new[] { new NavPoint(6d, 0d), new NavPoint(8d, -1d) }));
            Assert.IsTrue(crowd.SetRoute(2, new NavPoint(8d, 1d), new[] { new NavPoint(6d, 0d), new NavPoint(8d, 1d) }));
            for (int tick = 0; tick < 1200 && (crowd.Units[0].Moving || crowd.Units[1].Moving); tick++) crowd.Step(1d / 30d);
            Assert.AreEqual(NavigationOutcome.Arrived,crowd.Units[0].Outcome); Assert.AreEqual(NavigationOutcome.Arrived,crowd.Units[1].Outcome);
        }

        [Test]
        public void IdentityRejectsStaleResultAndMailboxIsBounded()
        {
            var current = new NavRouteIdentity(1, 2, "tank-1", 3, "unity-navigation-diagnostic-v1", 1, 4);
            Assert.IsTrue(current.Matches(new NavRouteIdentity(1, 2, "tank-1", 3, "unity-navigation-diagnostic-v1", 1, 4)));
            Assert.IsFalse(current.Matches(new NavRouteIdentity(1, 2, "tank-1", 4, "unity-navigation-diagnostic-v1", 1, 4)));
            var mailbox = new NavMailbox<int>(); for (int i = 0; i < NavMailbox<int>.Capacity; i++) Assert.IsTrue(mailbox.TryEnqueue(i)); Assert.IsFalse(mailbox.TryEnqueue(999));
            int first; Assert.IsTrue(mailbox.TryDequeue(out first)); Assert.AreEqual(0, first); Assert.IsTrue(mailbox.TryEnqueue(999));
        }
    }
}

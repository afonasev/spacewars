using System;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationTests
    {
        private sealed class RejectOneFutureEndpoint : INavMotionClearance
        {
            private readonly INavMotionClearance source;
            private readonly int actor;
            private readonly NavPoint available, rejected;
            internal bool SawAvailable, SawRejected;
            internal RejectOneFutureEndpoint(INavMotionClearance source,int actor,NavPoint available,NavPoint rejected)
            {this.source=source;this.actor=actor;this.available=available;this.rejected=rejected;}
            private static bool Near(NavPoint a,NavPoint b)=>Math.Abs(a.X-b.X)<1e-12&&Math.Abs(a.Z-b.Z)<1e-12;
            public bool ClearMotion(int row,NavPoint end)
            {
                if(row==actor&&Near(end,available))SawAvailable=true;
                if(row==actor&&Near(end,rejected)){SawRejected=true;return false;}
                return source.ClearMotion(row,end);
            }
        }

        private static void AssertSameDouble(double expected,double actual,string label)
            =>Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected),BitConverter.DoubleToInt64Bits(actual),label);

        private static void AssertSameCrowdState(NavCrowdSaveState expected,NavCrowdSaveState actual)
        {
            Assert.AreEqual(expected.NextIncarnation,actual.NextIncarnation);AssertSameDouble(expected.HalfExtent,actual.HalfExtent,"half extent");AssertSameDouble(expected.MaximumRadius,actual.MaximumRadius,"maximum radius");
            Assert.AreEqual(expected.GeometryRevision,actual.GeometryRevision);Assert.AreEqual(expected.ProfileId,actual.ProfileId);Assert.AreEqual(expected.ProfileRevision,actual.ProfileRevision);
            Assert.AreEqual(expected.RepairCount,actual.RepairCount);Assert.AreEqual(expected.RepairFailed,actual.RepairFailed);Assert.AreEqual(expected.MaxNoProgressTicks,actual.MaxNoProgressTicks);
            CollectionAssert.AreEqual(expected.Obstacles,actual.Obstacles);CollectionAssert.AreEqual(expected.ProfileValues,actual.ProfileValues);Assert.AreEqual(expected.Units.Length,actual.Units.Length);
            for(int i=0;i<expected.Units.Length;i++){
                var a=expected.Units[i];var b=actual.Units[i];Assert.AreEqual(a.Id,b.Id);Assert.AreEqual(a.Incarnation,b.Incarnation);Assert.AreEqual(a.MobilityRevision,b.MobilityRevision);Assert.AreEqual(a.Team,b.Team);
                Assert.AreEqual(a.RouteIndex,b.RouteIndex);Assert.AreEqual(a.LocalIndex,b.LocalIndex);Assert.AreEqual(a.NoProgressTicks,b.NoProgressTicks);Assert.AreEqual(a.Position,b.Position);Assert.AreEqual(a.Goal,b.Goal);
                Assert.AreEqual(a.Location,b.Location);Assert.AreEqual(a.GoalLocation,b.GoalLocation);AssertSameDouble(a.Radius,b.Radius,"radius");AssertSameDouble(a.Speed,b.Speed,"speed");AssertSameDouble(a.TurnSpeed,b.TurnSpeed,"turn speed");AssertSameDouble(a.Heading,b.Heading,"heading");AssertSameDouble(a.BlockedSeconds,b.BlockedSeconds,"blocked seconds");
                Assert.AreEqual(a.Moving,b.Moving);Assert.AreEqual(a.Held,b.Held);Assert.AreEqual(a.Outcome,b.Outcome);Assert.AreEqual(a.InstalledGroupId,b.InstalledGroupId);Assert.AreEqual(a.InstalledRevision,b.InstalledRevision);Assert.AreEqual(a.InstalledRequest,b.InstalledRequest);Assert.AreEqual(a.InstalledOrder,b.InstalledOrder);
                Assert.AreEqual(a.FailedGroupId,b.FailedGroupId);Assert.AreEqual(a.FailedRevision,b.FailedRevision);Assert.AreEqual(a.FailedRequest,b.FailedRequest);Assert.AreEqual(a.FailedOrder,b.FailedOrder);Assert.AreEqual(a.FailedGoal,b.FailedGoal);Assert.AreEqual(a.LastFailure,b.LastFailure);
                CollectionAssert.AreEqual(a.Route,b.Route);CollectionAssert.AreEqual(a.LocalRoute,b.LocalRoute);
            }
        }

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
        public void SelectRejectsUnalignedCandidateWithUnavailableEventualSweep()
        {
            const double dt=1d/30d;
            var profile=PlayableProfile.ThreeCrossingsDefault.Navigation;
            var geometry=new NavGeometry(100d,new NavObstacle[0],1);
            var crowd=new NavCrowd(geometry,profile);
            var p12=new NavPoint(41.48419672236648d,48.01315790828938d);
            crowd.Add(12,p12,.72d,4d,4.5d);
            var bodies=new[]{new NavMotionBody{Id=12,Position=p12,Target=new NavPoint(39.2d,48.8d),Heading=-2.9684225822747465d,Radius=.72d,Speed=4d,TurnSpeed=4.5d,Active=true}};
            var neighbors=new int[NavCrowdMotion.NeighborCapacity];var counts=new int[1];
            var checker=new RejectOneFutureEndpoint((INavMotionClearance)crowd,0,
                new NavPoint(41.48013541608066d,48.079700753402115d),new NavPoint(41.4242733443611d,47.98394090616957d));
            var proposal=NavCrowdMotion.Select(bodies,0,neighbors,counts[0],profile,dt,checker);
            Assert.IsTrue(checker.SawAvailable,"the canonical opposite-turn route must have an available left candidate");
            Assert.IsTrue(checker.SawRejected,"the canonical right-turn candidate must be unavailable");
            Assert.Less(Math.Abs(NavCrowdMotion.Angle(proposal.Heading-(-3.1184225822747464d))),1e-12,"the available left-turn candidate must win over the lower-error unavailable right turn");
            Assert.IsFalse(proposal.Blocked,"another legal eventual turn remains available");Assert.IsFalse(proposal.Overflow);
            Assert.IsTrue(geometry.SegmentFree(p12,proposal.Position,.72d),"the actual bounded proposal must retain its real geometry sweep");
        }

        [Test]
        public void CrowdColdRestorePreservesTheExactNextStep()
        {
            const double dt=1d/30d;var profile=new NavigationProfile();var geometry=new NavGeometry(10d,new NavObstacle[0],1);var source=new NavCrowd(geometry,profile);
            source.Add(1,new NavPoint(-3d,0d));source.Add(2,new NavPoint(3d,0d));
            Assert.IsTrue(source.SetRoute(1,new NavPoint(3d,0d),new[]{new NavPoint(3d,0d)}));Assert.IsTrue(source.SetRoute(2,new NavPoint(-3d,0d),new[]{new NavPoint(-3d,0d)}));
            for(int i=0;i<7;i++)source.Step(dt);
            var cold=new NavCrowd(geometry,profile);cold.RestoreState(source.CaptureState());
            source.Step(dt);cold.Step(dt);
            AssertSameCrowdState(source.CaptureState(),cold.CaptureState());
        }

        [Test]
        public void IntermediateCornerInsideArrivalToleranceStillRequiresFullNextSweep()
        {
            var profile=new NavigationProfile();var geometry=new NavGeometry(10,new[]{new NavObstacle(-1,-2,1,2)},1);var crowd=new NavCrowd(geometry,profile);
            var corner=new NavPoint(-1.8,2.75);var start=new NavPoint(-1.8,2.61);var goal=new NavPoint(3,2.75);var actor=crowd.Add(1,start);
            Assert.Less(Math.Sqrt(Math.Pow(corner.X-start.X,2)+Math.Pow(corner.Z-start.Z,2)),profile.ArrivalTolerance);
            Assert.True(geometry.SegmentFree(start,corner,actor.Radius));Assert.True(geometry.SegmentFree(corner,goal,actor.Radius));Assert.False(geometry.SegmentFree(start,goal,actor.Radius));
            Assert.True(crowd.SetRoute(1,goal,new[]{corner,goal}));crowd.Step(1d/30);Assert.Zero(actor.routeIndex,"terminal tolerance cannot consume this obstructed intermediate turn");
            for(int tick=0;tick<900&&actor.Moving;tick++){var before=actor.Position;crowd.Step(1d/30);Assert.True(geometry.SegmentFree(before,actor.Position,actor.Radius));}
            Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome);Assert.LessOrEqual(Math.Sqrt(Math.Pow(goal.X-actor.Position.X,2)+Math.Pow(goal.Z-actor.Position.Z,2)),profile.ArrivalTolerance);
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

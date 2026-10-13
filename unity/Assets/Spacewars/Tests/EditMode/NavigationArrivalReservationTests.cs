using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationArrivalReservationTests
    {
        private static NavArrivalMember Member(NavUnit actor, long group = 1, long order = 0)
            => new NavArrivalMember(actor.Id, actor.Incarnation, order, actor.MobilityRevision, actor.Location,
                new NavClearanceProfile("ground", 1, "ground", 0, actor.Radius), 1, group, 1,
                order + 10, 1, 1, order + 1, Math.Max(1, actor.InstalledRequest == 0 ? order + 100 : actor.InstalledRequest));

        [Test] public void StopAndDeathReleaseOnlyTheirCurrentPendingReservations()
        {
            var session = new NavigationSession(1, new NavGeometry(20, Array.Empty<NavObstacle>(), 1), new NavigationProfile());
            session.Crowd.Add(0, new NavPoint(-8, 0), .5, 4, 4);
            session.Crowd.Add(1, new NavPoint(-8, 4), .9, 4, 4);
            Assert.True(session.Move(0, new NavPoint(8, 0))); Assert.True(session.Move(1, new NavPoint(8, 4)));
            var initial = session.CaptureArrivalOccupancySnapshot();
            var pending = initial.Occupants.Where(x => x.Kind == NavArrivalOccupantKind.PendingReservation).OrderBy(x => x.ActorId).ToArray();
            Assert.AreEqual(2, pending.Length);
            Assert.AreEqual(NavArrivalReservationKind.PendingRequest, pending[0].ReservationKind);
            Assert.Greater(pending[0].RequestSequence, 0);

            session.Stop(0, false);
            var stopped = session.CaptureArrivalOccupancySnapshot();
            CollectionAssert.AreEqual(new long[] { 1 }, stopped.Occupants.Where(x => x.ReservationKind.HasValue).Select(x => x.ActorId));
            Assert.True(stopped.Occupants.Any(x => x.Kind == NavArrivalOccupantKind.Actor && x.ActorId == 0 && x.BlocksPlacement));
            Assert.True(stopped.Occupants.Any(x => x.Kind == NavArrivalOccupantKind.Actor && x.ActorId == 1));

            Assert.True(session.Remove(1));
            var subset = session.CaptureArrivalOccupancySnapshot();
            Assert.False(subset.Occupants.Any(x => x.ActorId == 1));
            Assert.True(subset.Occupants.Any(x => x.Kind == NavArrivalOccupantKind.Actor && x.ActorId == 0));
            Assert.AreEqual(0, subset.Occupants.Count(x => x.ReservationKind.HasValue));
        }

        [Test] public void ActualArrivalDropsReservationButKeepsParkedExactRadiusFootprint()
        {
            var session = new NavigationSession(1, new NavGeometry(20, Array.Empty<NavObstacle>(), 1), new NavigationProfile());
            var actor = session.Crowd.Add(4, new NavPoint(-3, 0), .4, 4.5, 4);
            Assert.True(session.Move(4, new NavPoint(3, 0)));
            Assert.True(session.Requests.TryDequeue(out var request));
            Assert.True(request.GoalLocation.HasValue);
            Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(request, new[] { request.Goal })));
            session.ApplyResults();
            for (int tick = 0; tick < 120 && actor.Moving; tick++) session.Step(1d / 30);
            Assert.AreEqual(NavigationOutcome.Arrived, actor.Outcome);
            Assert.False(actor.Moving);

            var snapshot = session.CaptureArrivalOccupancySnapshot();
            Assert.False(snapshot.Occupants.Any(x => x.ReservationKind.HasValue && x.ActorId == 4));
            var footprint = snapshot.Occupants.Single(x => x.Kind == NavArrivalOccupantKind.Actor && x.ActorId == 4);
            Assert.True(footprint.BlocksPlacement); Assert.AreEqual(.4, footprint.Radius);
            Assert.AreEqual(actor.Location, footprint.Location);
            Assert.LessOrEqual(Math.Abs(actor.Position.X - 3), session.Crowd.Units.Single(x => x.Id == 4).Radius);
        }

        [Test] public void InstalledReservationBindsHistoricalExecutionAndCurrentActorTarget()
        {
            var profile = PlayableProfile.Default;
            var geometry = new NavGeometry(20, Array.Empty<NavObstacle>(), 1);
            var session = new NavigationSession(1, geometry, profile.Navigation);
            var actor = session.Crowd.Add(3, new NavPoint(-5, 0));
            Assert.True(session.Move(3, new NavPoint(5, 0)));
            var binding = new PlayableRouteBinding(geometry, profile, session.Admission, session.RoutePort);
            var kernel = new CertifiedRouteKernel();
            for (int i = 0; i < 40 && session.PendingCount > 0; i++) { kernel.Pump(binding, 4); session.ApplyResults(); }
            var installed = session.InstalledExecutionFor(3);
            Assert.NotNull(installed); Assert.True(actor.Moving); Assert.Greater(actor.InstalledRequest, 0);
            var group = session.GroupFor(3); Assert.NotNull(group);
            var groupMember = group.Members.Single(x => x.Entity == 3);
            var member = new NavArrivalMember(actor.Id, actor.Incarnation, groupMember.Slot, actor.MobilityRevision,
                actor.Location, new NavClearanceProfile("ground", 1, "ground", 0, actor.Radius),
                session.Generation, group.GroupId, group.OrderRevision, group.CommandSequence, group.IssuedTick,
                groupMember.OrderRevision, groupMember.OrderRevision, actor.InstalledRequest);
            var snapshot = session.CaptureArrivalOccupancySnapshot(new[] { member });
            var reservation = snapshot.Occupants.Single(x => x.Kind == NavArrivalOccupantKind.InstalledReservation);
            Assert.AreEqual(NavArrivalReservationKind.InstalledExecution, reservation.ReservationKind);
            Assert.AreEqual(actor.InstalledRequest, reservation.HistoricalExecution);
            Assert.AreEqual(actor.GoalLocation, reservation.ActorTarget);
            Assert.AreEqual(ExpectedInstalledIdentity(session.Generation, actor, installed.Corridor.StableKey), reservation.Identity,
                "The compact identity must hash the complete structured execution and current target, including corridor binding.");

            session.Stop(3, false);
            var stopped = session.CaptureArrivalOccupancySnapshot();
            Assert.False(stopped.Occupants.Any(x => x.Kind == NavArrivalOccupantKind.InstalledReservation));
            Assert.True(stopped.Occupants.Any(x => x.Kind == NavArrivalOccupantKind.Actor && x.ActorId == 3 && x.BlocksPlacement));
            kernel.Clear();
        }

        [Test] public void LegacyAndDetachedMovingExecutionsRetainTargetReservationUntilRelease()
        {
            foreach (bool detachDescriptor in new[] { false, true }) {
                var session = CreateInstalledSession(detachDescriptor ? 20 : 21, out var actor);
                if (detachDescriptor) {
                    var detach = typeof(NavigationSession).GetMethod("DetachInstalledExecution", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(detach); detach.Invoke(session, new object[] { actor.Id });
                } else {
                    var field = typeof(NavigationSession).GetField("installedExecutions", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(field);
                    ((System.Collections.IDictionary)field.GetValue(session)).Remove(actor.Id);
                }
                var retained = session.CaptureArrivalOccupancySnapshot();
                var reservation = retained.Occupants.Single(x => x.Kind == NavArrivalOccupantKind.InstalledReservation);
                Assert.AreEqual(actor.InstalledRequest, reservation.HistoricalExecution);
                Assert.AreEqual(actor.GoalLocation, reservation.ActorTarget);
                Assert.True(reservation.BlocksPlacement);

                session.Stop(actor.Id, false);
                var released = session.CaptureArrivalOccupancySnapshot();
                Assert.False(released.Occupants.Any(x => x.Kind == NavArrivalOccupantKind.InstalledReservation));
                Assert.True(released.Occupants.Any(x => x.Kind == NavArrivalOccupantKind.Actor &&
                    x.ActorId == actor.Id && x.BlocksPlacement));
            }
        }

        [Test] public void InstalledReservationRejectsTypedTargetThatDisagreesWithRetainedPoint()
        {
            var session = CreateInstalledSession(22, out var actor);
            var field = typeof(NavigationSession).GetField("reservations", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var reservations = (System.Collections.IDictionary)field.GetValue(session);
            reservations[actor.Id] = new NavPoint(actor.Goal.X + 1, actor.Goal.Z);
            Assert.Throws<InvalidOperationException>(() => session.CaptureArrivalOccupancySnapshot());
        }

        [Test] public void LongStructuredPendingIdentityHashesAndSurvivesSessionWireRoundTrip()
        {
            var profile = new NavigationProfile(new string('p', 320), 1, .72, 4, 4.5, .8, .15, 3.6, .08);
            var geometry = new NavGeometry(20, Array.Empty<NavObstacle>(), 1);
            var session = new NavigationSession(1, geometry, profile);
            session.Crowd.Add(23, new NavPoint(-5, 0));
            Assert.True(session.Move(23, new NavPoint(5, 0)));
            var before = session.CaptureArrivalOccupancySnapshot();
            var pendingBefore = before.Occupants.Single(x => x.Kind == NavArrivalOccupantKind.PendingReservation);
            Assert.Less(pendingBefore.Identity.Length, 256);

            var wire = WriteAndReadV7(session.CaptureState(), out var version);
            Assert.AreEqual(7, version);
            var restored = new NavigationSession(1, geometry, profile);
            restored.RestoreState(wire);
            var after = restored.CaptureArrivalOccupancySnapshot();
            var pendingAfter = after.Occupants.Single(x => x.Kind == NavArrivalOccupantKind.PendingReservation);
            Assert.AreEqual(before.StableKey, after.StableKey);
            Assert.AreEqual(pendingBefore.Identity, pendingAfter.Identity);
        }

        private static NavigationSession CreateInstalledSession(int id, out NavUnit actor)
        {
            var profile = PlayableProfile.Default;
            var geometry = new NavGeometry(20, Array.Empty<NavObstacle>(), 1);
            var session = new NavigationSession(1, geometry, profile.Navigation);
            actor = session.Crowd.Add(id, new NavPoint(-5, 0));
            Assert.True(session.Move(id, new NavPoint(5, 0)));
            var binding = new PlayableRouteBinding(geometry, profile, session.Admission, session.RoutePort);
            var kernel = new CertifiedRouteKernel();
            for (int i = 0; i < 40 && session.PendingCount > 0; i++) { kernel.Pump(binding, 4); session.ApplyResults(); }
            Assert.True(actor.Moving); Assert.Greater(actor.InstalledRequest, 0);
            Assert.NotNull(session.InstalledExecutionFor(id));
            kernel.Clear();
            return session;
        }

        private static string ExpectedInstalledIdentity(long generation, NavUnit actor, string corridorBinding)
        {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) {
                    writer.Write("arrival-installed-v1"); writer.Write(generation); writer.Write(actor.Id); writer.Write(actor.Incarnation);
                    writer.Write(actor.InstalledGroupId); writer.Write(actor.InstalledRevision); writer.Write(actor.InstalledRequest);
                    writer.Write(actor.InstalledOrder); writer.Write(corridorBinding); writer.Write(actor.Goal.X); writer.Write(actor.Goal.Z);
                    writer.Write(actor.GoalLocation.HasValue);
                    if (actor.GoalLocation.HasValue) {
                        writer.Write(actor.GoalLocation.Value.SurfaceId); writer.Write(actor.GoalLocation.Value.Position.X);
                        writer.Write(actor.GoalLocation.Value.Position.Z);
                    }
                }
                using (var hash = SHA256.Create())
                    return "installed:" + BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }

        [Test] public void V7SessionWireRoundTripPreservesOccupancyAndResumedAllocationOutcome()
        {
            var geometry = new NavGeometry(20, Array.Empty<NavObstacle>(), 1);
            var session = new NavigationSession(1, geometry, new NavigationProfile());
            var actor = session.Crowd.Add(5, new NavPoint(-6, 0), .4, 4, 4);
            Assert.True(session.Move(5, new NavPoint(6, 0)));
            var member = Member(actor);
            var occupancy = session.CaptureArrivalOccupancySnapshot(new[] { member });
            var provider = new LayeredNavigationProvider("arrival-wire", 1, 1, 1,
                NavigationArrivalTestFactory.Sha("arrival-wire-map"),
                new[] { new NavLayerSurface(NavLocation.FlatSurface, "ground", 0, 1, NavigationArrivalTestFactory.Area()) },
                Array.Empty<NavLayerPortal>());
            var endpoint = new NavLocation(new NavPoint(6, 6), NavLocation.FlatSurface);
            var input = NavigationArrivalTestFactory.Input(provider, new[] { endpoint }, new[] { member }, occupancy);
            var work = new NavigationArrivalWork(input);
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(0, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            var checkpoint = work.Save();

            var state = session.CaptureState();
            var wire = WriteAndReadV7(state, out var version);
            Assert.AreEqual(7, version);
            var restoredSession = new NavigationSession(1, geometry, new NavigationProfile());
            restoredSession.RestoreState(wire);
            var restoredMember = Member(restoredSession.Crowd.Units.Single(x => x.Id == 5));
            var restoredOccupancy = restoredSession.CaptureArrivalOccupancySnapshot(new[] { restoredMember });
            Assert.AreEqual(occupancy.StableKey, restoredOccupancy.StableKey);
            var restoredInput = NavigationArrivalTestFactory.Input(provider, new[] { endpoint }, new[] { restoredMember }, restoredOccupancy);
            Assert.AreEqual(input.StableKey, restoredInput.StableKey);

            var resumedBeforeWire = NavigationArrivalWork.Restore(input, checkpoint);
            var resumedAfterWire = NavigationArrivalWork.Restore(restoredInput, checkpoint);
            Assert.AreEqual(NavArrivalStatus.Ready, NavigationArrivalTestFactory.Complete(resumedBeforeWire));
            Assert.AreEqual(NavArrivalStatus.Ready, NavigationArrivalTestFactory.Complete(resumedAfterWire));
            Assert.AreEqual(resumedBeforeWire.Proposals.Single().Proof.StableKey, resumedAfterWire.Proposals.Single().Proof.StableKey);
            Assert.AreEqual(resumedBeforeWire.AllocationWorkConsumed, resumedAfterWire.AllocationWorkConsumed);
            Assert.AreEqual(resumedBeforeWire.RouteCounters.LogicalGraphWork, resumedAfterWire.RouteCounters.LogicalGraphWork);
            Assert.AreEqual(resumedBeforeWire.RouteCounters.LogicalFieldWork, resumedAfterWire.RouteCounters.LogicalFieldWork);
            Assert.AreEqual(resumedBeforeWire.RouteCounters.ConnectorWork, resumedAfterWire.RouteCounters.ConnectorWork);
        }

        private static NavigationSessionState WriteAndReadV7(NavigationSessionState state, out int version)
        {
            var assembly = typeof(NavigationSession).Assembly;
            var wireType = assembly.GetType("Spacewars.Runtime.WorldWire", true);
            var write = wireType.GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(BinaryWriter), typeof(NavigationSessionState) }, null);
            var read = wireType.GetMethod("ReadNavigationSessionState", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(write); Assert.NotNull(read);
            byte[] bytes;
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream)) write.Invoke(null, new object[] { writer, state });
                bytes = stream.ToArray();
            }
            byte[] tag = BitConverter.GetBytes(0x47525031); int index = -1;
            for (int i = 0; i <= bytes.Length - 8; i++) if (bytes.Skip(i).Take(4).SequenceEqual(tag)) index = i;
            Assert.GreaterOrEqual(index, 0, "World snapshot must carry the group navigation extension.");
            version = BitConverter.ToInt32(bytes, index + 4);
            using (var stream = new MemoryStream(bytes)) using (var reader = new BinaryReader(stream))
                return (NavigationSessionState)read.Invoke(null, new object[] { reader });
        }
    }
}

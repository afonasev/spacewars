using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    internal static class NavigationArrivalTestFactory
    {
        internal static string Sha(string value)
        { using (var hash = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        internal static NavLocation At(double x, double z, string surface = "ground") => new NavLocation(new NavPoint(x, z), surface);
        internal static NavTraversalArea Area(double half = 12, IEnumerable<NavObstacle> blockers = null)
            => new NavTraversalArea(new NavPolygon(new[] { new NavPoint(-half, -half), new NavPoint(half, -half), new NavPoint(half, half), new NavPoint(-half, half) }), half, blockers ?? Array.Empty<NavObstacle>());
        internal static LayeredNavigationProvider Layered(string id = "arrival-layered", int revision = 1,
            IEnumerable<NavObstacle> groundBlockers = null, IEnumerable<NavLayerPortal> portals = null)
        {
            var ground = new NavLayerSurface("ground", "ground", 0, 1, Area(12, groundBlockers));
            var layers = new List<NavLayerSurface> { ground };
            if (portals != null) layers.Add(new NavLayerSurface("upper", "ground", 1, 1, Area(12)));
            return new LayeredNavigationProvider(id, revision, 1, revision, Sha(id + revision), layers, portals ?? Array.Empty<NavLayerPortal>());
        }
        internal static NavArrivalMember Member(long id, long order, NavLocation origin, double radius = .25,
            long group = 1, long request = 100)
            => new NavArrivalMember(id, 1, order, 0, origin, new NavClearanceProfile("ground", 1, "ground", 0, radius),
                1, group, 1, order + 10, 1, 1, order + 1, request + id);
        internal static NavArrivalOccupant Actor(long id, NavLocation at, double radius, bool blocks, string identity)
            => new NavArrivalOccupant(NavArrivalOccupantKind.Actor, id, 1, at, radius, blocks, identity);
        internal static NavArrivalOccupant Pending(long id, NavLocation at, double radius, long request, string identity)
            => new NavArrivalOccupant(NavArrivalOccupantKind.PendingReservation, id, 1, at, radius, true, identity, NavArrivalReservationKind.PendingRequest, request);
        internal static NavArrivalOccupant Installed(long id, NavLocation at, double radius, long execution, string identity)
            => new NavArrivalOccupant(NavArrivalOccupantKind.InstalledReservation, id, 1, at, radius, true, identity, NavArrivalReservationKind.InstalledExecution, 0, execution, at);
        internal static NavArrivalOccupancySnapshot Occupancy(params NavArrivalOccupant[] rows) => new NavArrivalOccupancySnapshot(1, rows);
        internal static NavArrivalInput Input(ILayeredNavigationProvider provider, IEnumerable<NavLocation> candidates,
            IEnumerable<NavArrivalMember> members, NavArrivalOccupancySnapshot occupancy,
            string topology = null, string profile = null, NavigationProfile arrival = null,
            NavGraphCompileLimits limits = null, double graphCell = 1, int maxLabels = 4096)
        {
            var list = candidates.ToArray(); var source = new NavArrivalCandidateSource("arrival-terminal", 1, list[0], list);
            return new NavArrivalInput(topology ?? "topology:" + provider.Binding, profile ?? "profile:" + provider.Binding,
                provider, new NavGraphProfile(Sha("arrival-profile"), "distance", 1, graphCell), arrival ?? new NavigationProfile(),
                limits ?? new NavGraphCompileLimits(12000, 8000, 50000), source, members, occupancy, maxLabels);
        }
        internal static NavArrivalStatus Complete(NavigationArrivalWork work, int maxEpochs = 3000)
        {
            long firstEpoch = Math.Max(0, work.LogicalEpoch + 1);
            for (long epoch = firstEpoch; epoch < firstEpoch + maxEpochs; epoch++) {
                var status = work.Advance(epoch, 10000, new NavEpochQuota(20000, 20000, 20000, 20000, 20000));
                if (status != NavArrivalStatus.Pending && status != NavArrivalStatus.BudgetExhausted) return status;
            }
            Assert.Fail("Arrival work did not finish within finite logical epochs."); return NavArrivalStatus.Pending;
        }
    }

    public sealed class NavigationArrivalAllocationTests
    {
        [Test] public void MixedRadiiParkedHoldAndOtherGroupReservationsKeepFootprintsAcrossSurfacesAndInputReorder()
        {
            // A second overlapping floor exercises the physical XZ collision rule.
            var upperProvider = new LayeredNavigationProvider("arrival-two-floors", 1, 1, 1,
                NavigationArrivalTestFactory.Sha("two-floors"), new[] {
                    new NavLayerSurface("ground", "ground", 0, 1, NavigationArrivalTestFactory.Area()),
                    new NavLayerSurface("upper", "ground", 1, 1, NavigationArrivalTestFactory.Area())
                }, Array.Empty<NavLayerPortal>());
            var hold = NavigationArrivalTestFactory.Actor(90, NavigationArrivalTestFactory.At(3, 0), 1.1, true, "parked-hold:90:1");
            var otherGroup = NavigationArrivalTestFactory.Installed(91, NavigationArrivalTestFactory.At(8, 0), .8, 42, "installed:91:42");
            var occupancy = NavigationArrivalTestFactory.Occupancy(hold, otherGroup);
            var candidates = new[] { NavigationArrivalTestFactory.At(-4, 0), NavigationArrivalTestFactory.At(3, 0, "upper"),
                NavigationArrivalTestFactory.At(8, 0), NavigationArrivalTestFactory.At(8, 10, "upper") };
            var members = new[] {
                NavigationArrivalTestFactory.Member(1, 0, NavigationArrivalTestFactory.At(-9, -4), .25),
                NavigationArrivalTestFactory.Member(2, 1, NavigationArrivalTestFactory.At(-9, 4, "upper"), 1.1)
            };
            var forward = NavigationArrivalTestFactory.Input(upperProvider, candidates, members, occupancy);
            var reordered = NavigationArrivalTestFactory.Input(upperProvider, candidates, members.Reverse(), occupancy);
            var a = new NavigationArrivalWork(forward); var b = new NavigationArrivalWork(reordered);
            Assert.AreEqual(NavArrivalStatus.Ready, NavigationArrivalTestFactory.Complete(a));
            Assert.AreEqual(NavArrivalStatus.Ready, NavigationArrivalTestFactory.Complete(b));
            CollectionAssert.AreEqual(a.Proposals.Select(x => x.Member.EntityId + ":" + x.Endpoint.SurfaceId + ":" + x.Endpoint.Position.X + ":" + x.Endpoint.Position.Z),
                b.Proposals.Select(x => x.Member.EntityId + ":" + x.Endpoint.SurfaceId + ":" + x.Endpoint.Position.X + ":" + x.Endpoint.Position.Z));
            Assert.AreEqual(NavigationArrivalTestFactory.At(-4, 0), a.Proposals.Single(x => x.Member.EntityId == 1).Endpoint);
            Assert.AreEqual(NavigationArrivalTestFactory.At(8, 10, "upper"), a.Proposals.Single(x => x.Member.EntityId == 2).Endpoint);
            Assert.True(a.Proposals.All(x => x.Proof.Route.Legs.Count > 0 && x.Proof.Endpoint.Equals(x.Endpoint)));
            Assert.Greater(a.CandidateWorkConsumed, 0); Assert.Greater(a.OccupancyWorkConsumed, 0);
            Assert.AreEqual(1.1, hold.Radius);
            Assert.AreEqual(NavArrivalOccupantKind.InstalledReservation, otherGroup.Kind);
            Assert.AreNotEqual(hold.Location.SurfaceId, a.Proposals[1].Endpoint.SurfaceId);
        }

        [Test] public void RealLayeredProviderRejectsWrongSurfaceAndDirectedPortalThenReturnsExactAlternativeRoute()
        {
            var wall = new[] { new NavObstacle(-.4, -12, .4, 12) };
            var corridor = NavigationArrivalTestFactory.Area(12);
            var lowerEntry = NavigationArrivalTestFactory.At(-2, 0);
            var upperExit = NavigationArrivalTestFactory.At(-2, 0, "upper");
            var portal = new NavLayerPortal("lift-forward", lowerEntry, upperExit, NavPortalDirection.Forward,
                true, 1, corridor, new[] { "ground" });
            var provider = new LayeredNavigationProvider("arrival-directed", 1, 1, 1,
                NavigationArrivalTestFactory.Sha("arrival-directed-map"), new[] {
                    new NavLayerSurface("ground", "ground", 0, 1, NavigationArrivalTestFactory.Area(12, wall)),
                    new NavLayerSurface("upper", "ground", 1, 1, NavigationArrivalTestFactory.Area(12))
                }, new[] { portal });
            var member = NavigationArrivalTestFactory.Member(7, 0, NavigationArrivalTestFactory.At(-5, 0), .2);
            var candidates = new[] { NavigationArrivalTestFactory.At(4, 0, "unknown-surface"),
                NavigationArrivalTestFactory.At(5, 0), NavigationArrivalTestFactory.At(5, 0, "upper") };
            var input = NavigationArrivalTestFactory.Input(provider, candidates, new[] { member }, NavigationArrivalTestFactory.Occupancy());
            var work = new NavigationArrivalWork(input);
            Assert.AreEqual(NavArrivalStatus.Ready, NavigationArrivalTestFactory.Complete(work));
            var proposal = work.Proposals.Single();
            Assert.AreEqual(NavigationArrivalTestFactory.At(5, 0, "upper"), proposal.Endpoint);
            Assert.True(proposal.Route.Legs.Any(x => x.Kind == NavTypedLegKind.Portal && x.PortalId == "lift-forward"));
            Assert.AreEqual(member.Radius, proposal.Proof.Radius);
            Assert.AreEqual(member.Origin, proposal.Proof.Subscription.Origin);

            var reverse = NavigationArrivalTestFactory.Member(8, 0, NavigationArrivalTestFactory.At(5, 0, "upper"), .2);
            var wrongWay = new NavigationArrivalWork(NavigationArrivalTestFactory.Input(provider,
                new[] { NavigationArrivalTestFactory.At(-5, 0) }, new[] { reverse }, NavigationArrivalTestFactory.Occupancy()));
            Assert.AreEqual(NavArrivalStatus.UnreachableInGraph, NavigationArrivalTestFactory.Complete(wrongWay));
        }

        [Test] public void SixtyFiveTerminalCandidatesArePagedWithoutCappingGroupOrRegionCapacity()
        {
            var provider = new LayeredNavigationProvider("arrival-layered-wide", 1, 1, 1,
                NavigationArrivalTestFactory.Sha("arrival-layered-wide"), new[] {
                    new NavLayerSurface("ground", "ground", 0, 1, NavigationArrivalTestFactory.Area(25))
                }, Array.Empty<NavLayerPortal>());
            var candidates = Enumerable.Range(0, 65).Select(i => NavigationArrivalTestFactory.At(-20 + i * .6, 0)).ToArray();
            var input = NavigationArrivalTestFactory.Input(provider, candidates,
                new[] { NavigationArrivalTestFactory.Member(4, 0, NavigationArrivalTestFactory.At(-21, 0), .2) },
                NavigationArrivalTestFactory.Occupancy(), graphCell: 10);
            var work = new NavigationArrivalWork(input);
            Assert.AreEqual(2, work.TerminalPageCount);
            var status = NavigationArrivalTestFactory.Complete(work);
            Assert.AreEqual(NavArrivalStatus.Ready, status, string.Format("status={0}; graphBuilds={1}; fieldBuilds={2}; labels={3}; candidates={4}; pages={5}",
                status, work.RouteCounters.GraphBuilds, work.RouteCounters.FieldBuilds, work.RouteCounters.PeakFieldLabels,
                work.CandidateWorkConsumed, work.TerminalPageCount));
            Assert.AreEqual(1, work.Proposals.Count);
        }

        [Test] public void SixtyFiveMemberGroupCanAllocateAcrossTwoTerminalArtifactPages()
        {
            var provider = new LayeredNavigationProvider("arrival-large-terminal", 1, 1, 1,
                NavigationArrivalTestFactory.Sha("large-terminal"),
                new[] { new NavLayerSurface("ground", "ground", 0, 1, NavigationArrivalTestFactory.Area(200)) },
                Array.Empty<NavLayerPortal>());
            var candidates = Enumerable.Range(0, 65).Select(i => NavigationArrivalTestFactory.At(-160 + 5 * i, 20)).ToArray();
            var members = Enumerable.Range(0, 65).Select(i => NavigationArrivalTestFactory.Member(i, i,
                NavigationArrivalTestFactory.At(0, -80), .1)).ToArray();
            var input = NavigationArrivalTestFactory.Input(provider, candidates, members,
                NavigationArrivalTestFactory.Occupancy(), limits: new NavGraphCompileLimits(1000, 1000, 5000), graphCell: 100);
            var work = new NavigationArrivalWork(input);
            Assert.AreEqual(2, work.TerminalPageCount);
            Assert.AreEqual(NavArrivalStatus.Ready, NavigationArrivalTestFactory.Complete(work));
            Assert.AreEqual(65, work.Proposals.Count);
            Assert.AreEqual(65, work.Proposals.Select(x => x.Member.EntityId).Distinct().Count());
            for (int i = 0; i < work.Proposals.Count; i++)
                for (int j = i + 1; j < work.Proposals.Count; j++)
                    Assert.GreaterOrEqual(Math.Abs(work.Proposals[i].Endpoint.Position.X - work.Proposals[j].Endpoint.Position.X), 3.6);
        }

        [Test] public void ExhaustedSpaceIsNoAvailableSlotAndSchedulerCapacityIsExplicit()
        {
            var provider = NavigationArrivalTestFactory.Layered();
            var candidate = NavigationArrivalTestFactory.At(2, 0);
            var occupied = NavigationArrivalTestFactory.Occupancy(
                NavigationArrivalTestFactory.Actor(50, candidate, .5, true, "parked:50"));
            var full = new NavigationArrivalWork(NavigationArrivalTestFactory.Input(provider, new[] { candidate },
                new[] { NavigationArrivalTestFactory.Member(1, 0, NavigationArrivalTestFactory.At(-2, 0), .2) }, occupied));
            Assert.AreEqual(NavArrivalStatus.NoAvailableSlot, NavigationArrivalTestFactory.Complete(full));
            Assert.AreEqual(0, full.Proposals.Count);

            var members = Enumerable.Range(0, LayeredNavigationScheduler.MaxQueue + 1)
                .Select(i => NavigationArrivalTestFactory.Member(i, i, NavigationArrivalTestFactory.At(-10, 0), .1)).ToArray();
            var tooMany = new NavigationArrivalWork(NavigationArrivalTestFactory.Input(provider, new[] { candidate }, members,
                NavigationArrivalTestFactory.Occupancy()));
            Assert.AreEqual(NavArrivalStatus.CapacityExceeded, tooMany.Status);
        }

        [Test] public void SubmittedRealSolverCapacityFailureIsNotCollapsedIntoNoAvailableSlot()
        {
            var provider = NavigationArrivalTestFactory.Layered("arrival-capacity");
            var input = NavigationArrivalTestFactory.Input(provider,
                new[] { NavigationArrivalTestFactory.At(8, 8) },
                new[] { NavigationArrivalTestFactory.Member(31, 0, NavigationArrivalTestFactory.At(-8, -8), .2) },
                NavigationArrivalTestFactory.Occupancy(),
                limits: new NavGraphCompileLimits(1, 1, 1), graphCell: .25, maxLabels: 1);
            var work = new NavigationArrivalWork(input);
            var status = NavigationArrivalTestFactory.Complete(work);
            Assert.AreEqual(NavArrivalStatus.CapacityExceeded, status);
            Assert.Greater(work.CandidateWorkConsumed, 2, "The submitted solver completion was consumed.");
            Assert.AreEqual(0, work.PendingRouteCount); Assert.AreEqual(0, work.ReadyRouteCount);
            Assert.AreEqual(0, work.Proposals.Count);
        }

        [Test] public void ProviderTopologyProfileMembersAndOccupancyChangesRejectSavedProposalsAsStale()
        {
            var provider = NavigationArrivalTestFactory.Layered(); var candidate = NavigationArrivalTestFactory.At(2, 0);
            var member = NavigationArrivalTestFactory.Member(1, 0, NavigationArrivalTestFactory.At(-2, 0), .2);
            var occupancy = NavigationArrivalTestFactory.Occupancy();
            var original = NavigationArrivalTestFactory.Input(provider, new[] { candidate }, new[] { member }, occupancy);
            NavArrivalInput[] changed = {
                NavigationArrivalTestFactory.Input(NavigationArrivalTestFactory.Layered(revision: 2), new[] { candidate }, new[] { member }, occupancy),
                NavigationArrivalTestFactory.Input(provider, new[] { candidate }, new[] { member }, occupancy, topology: "topology:changed"),
                NavigationArrivalTestFactory.Input(provider, new[] { candidate }, new[] { member }, occupancy, profile: "profile:changed"),
                NavigationArrivalTestFactory.Input(provider, new[] { candidate }, new[] { NavigationArrivalTestFactory.Member(1, 0, NavigationArrivalTestFactory.At(-3, 0), .2) }, occupancy),
                NavigationArrivalTestFactory.Input(provider, new[] { candidate }, new[] { member }, NavigationArrivalTestFactory.Occupancy(
                    NavigationArrivalTestFactory.Actor(70, NavigationArrivalTestFactory.At(10, 10), .3, true, "new-occupant")))
            };
            foreach (var current in changed) {
                var work = new NavigationArrivalWork(original);
                Assert.IsFalse(work.ValidateAgainst(current)); Assert.AreEqual(NavArrivalStatus.Stale, work.Status);
            }
        }

        [Test] public void CandidateOccupancyAndRealSolverCheckpointsRestoreWithExactCountersAndNoSameEpochRefill()
        {
            var provider = NavigationArrivalTestFactory.Layered("arrival-solver", 1,
                new[] { new NavObstacle(-.5, -1, .5, 1) });
            var start = NavigationArrivalTestFactory.At(-5, 0); var finish = NavigationArrivalTestFactory.At(5, 0);
            var member = NavigationArrivalTestFactory.Member(11, 0, start, .2);
            var occupancy = NavigationArrivalTestFactory.Occupancy(
                NavigationArrivalTestFactory.Actor(80, NavigationArrivalTestFactory.At(9, 8), .5, true, "far-a"),
                NavigationArrivalTestFactory.Actor(81, NavigationArrivalTestFactory.At(-9, 8), .5, true, "far-b"));
            var input = NavigationArrivalTestFactory.Input(provider, new[] { finish }, new[] { member }, occupancy);
            var work = new NavigationArrivalWork(input);
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(0, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(1, work.CandidateWorkConsumed); Assert.AreEqual(0, work.OccupancyWorkConsumed);
            work = NavigationArrivalWork.Restore(input, work.Save());
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(0, 100, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(1, work.AllocationWorkConsumed, "Same logical epoch cannot refill candidate quota.");

            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(1, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(2, work.CandidateWorkConsumed); Assert.AreEqual(0, work.OccupancyWorkConsumed);
            work = NavigationArrivalWork.Restore(input, work.Save());
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(2, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(1, work.OccupancyWorkConsumed); Assert.AreEqual(1, work.OccupancyCursor);
            work = NavigationArrivalWork.Restore(input, work.Save());
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(3, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(2, work.OccupancyWorkConsumed);
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(4, 1, new NavEpochQuota(1, 1, 1, 1, 1)));
            Assert.AreEqual("SolvePage", work.WorkPhase);
            var graphBefore = work.RouteCounters.LogicalGraphWork;
            work = NavigationArrivalWork.Restore(input, work.Save());
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(5, 100, new NavEpochQuota(1, 1, 1, 1, 1)));
            Assert.GreaterOrEqual(work.RouteCounters.LogicalGraphWork, graphBefore);
            Assert.Greater(work.PendingRouteCount, 0);
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted,
                work.Advance(6, 0, new NavEpochQuota(20000, 20000, 20000, 20000, 20000)));
            Assert.AreEqual(1, work.ReadyRouteCount, "A finished solver result waits for allocation quota to drain it.");
            work = NavigationArrivalWork.Restore(input, work.Save());
            Assert.AreEqual(1, work.ReadyRouteCount, "Checkpoint preserves the scheduler's queued ready result.");

            var baseline = new NavigationArrivalWork(input);
            Assert.AreEqual(NavArrivalStatus.Ready, NavigationArrivalTestFactory.Complete(baseline));
            Assert.AreEqual(NavArrivalStatus.Ready, work.Advance(7, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(baseline.Proposals.Single().Proof.StableKey, work.Proposals.Single().Proof.StableKey);
            Assert.AreEqual(baseline.AllocationWorkConsumed, work.AllocationWorkConsumed);
            Assert.AreEqual(baseline.RouteCounters.LogicalGraphWork, work.RouteCounters.LogicalGraphWork);
            Assert.AreEqual(baseline.RouteCounters.LogicalFieldWork, work.RouteCounters.LogicalFieldWork);
            Assert.AreEqual(baseline.RouteCounters.ConnectorWork, work.RouteCounters.ConnectorWork);
        }

        [Test] public void OneAllocationUnitCannotValidateAndSubmitCandidateAndSameEpochDoesNotRefill()
        {
            var provider = NavigationArrivalTestFactory.Layered();
            var input = NavigationArrivalTestFactory.Input(provider, new[] { NavigationArrivalTestFactory.At(4, 0) },
                new[] { NavigationArrivalTestFactory.Member(12, 0, NavigationArrivalTestFactory.At(-4, 0), .2) },
                NavigationArrivalTestFactory.Occupancy());
            var work = new NavigationArrivalWork(input);
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(0, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(1, work.LastAdvanceConsumed); Assert.AreEqual(0, work.AllocationQuotaRemaining);
            Assert.AreEqual(1, work.CandidateWorkConsumed); Assert.AreEqual(0, work.PendingRouteCount);
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(0, 100, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(0, work.LastAdvanceConsumed); Assert.AreEqual(1, work.AllocationWorkConsumed);
            Assert.GreaterOrEqual(work.AllocationQuotaRemaining, 0);
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(1, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(1, work.LastAdvanceConsumed); Assert.AreEqual(2, work.AllocationWorkConsumed);
            Assert.AreEqual(0, work.PendingRouteCount, "Candidate submission remains a separate quota unit after validation.");
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(2, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(1, work.PendingRouteCount);
        }

        [Test] public void RealPartitionProviderPreservesPermittedSeamAsTypedRouteLeg()
        {
            var map = ThreeCrossingsMap.Default;
            var provider = new PartitionNavigationProvider(map,
                new NavGeometry(map.HalfExtent, map.MovementBlockers.ToArray(), map.Revision), null);
            var clearance = new NavClearanceProfile("ground", 1, "ground", 0, .58);
            Assert.True(provider.TryLocate(new NavPoint(-18, 0), clearance, out var origin));
            Assert.True(provider.TryLocate(new NavPoint(18, 0), clearance, out var endpoint));
            Assert.True(provider.IsValid(origin, clearance)); Assert.True(provider.IsValid(endpoint, clearance));
            var member = NavigationArrivalTestFactory.Member(6, 0, origin, .58);
            var input = NavigationArrivalTestFactory.Input(provider, new[] { endpoint }, new[] { member },
                NavigationArrivalTestFactory.Occupancy(), limits: new NavGraphCompileLimits(300000, 150000, 600000), graphCell: .8);
            var work = new NavigationArrivalWork(input);
            Assert.AreEqual(NavArrivalStatus.Ready, NavigationArrivalTestFactory.Complete(work));
            var route = work.Proposals.Single().Route;
            Assert.AreEqual(origin, route.Legs.First().From); Assert.AreEqual(endpoint, route.Legs.Last().To);
            Assert.True(route.Legs.Any(x => x.Kind == NavTypedLegKind.Portal));
        }

        [Test] public void MultiMemberPartitionAllocationKeepsSharedTerminalRequests()
        {
            var map = ThreeCrossingsMap.Default;
            var provider = new PartitionNavigationProvider(map,
                new NavGeometry(map.HalfExtent, map.MovementBlockers.ToArray(), map.Revision), null);
            var clearance = new NavClearanceProfile("ground", 1, "ground", 0, .58);
            Assert.True(provider.TryLocate(new NavPoint(-18, 0), clearance, out var firstOrigin));
            Assert.True(provider.TryLocate(new NavPoint(-18, 8), clearance, out var secondOrigin));
            Assert.True(provider.TryLocate(new NavPoint(18, 0), clearance, out var firstEnd));
            Assert.True(provider.TryLocate(new NavPoint(18, 8), clearance, out var secondEnd));
            var members = new[] {
                NavigationArrivalTestFactory.Member(40, 0, firstOrigin, .58),
                NavigationArrivalTestFactory.Member(41, 1, secondOrigin, .58)
            };
            var input = NavigationArrivalTestFactory.Input(provider, new[] { firstEnd, secondEnd }, members,
                NavigationArrivalTestFactory.Occupancy(), limits: new NavGraphCompileLimits(300000, 150000, 600000), graphCell: .8);
            var work = new NavigationArrivalWork(input);
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(0, 4, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(1, work.PendingRouteCount, "The fixed allocation quota is split across clearance preparation and request submission.");
            work = NavigationArrivalWork.Restore(input, work.Save());
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(1, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(1, work.PendingRouteCount);
            work = NavigationArrivalWork.Restore(input, work.Save());
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(2, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(2, work.PendingRouteCount, "Both candidate subscriptions use the same prepared terminal region.");
            work = NavigationArrivalWork.Restore(input, work.Save());
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(3, 0, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual(0, work.RouteCounters.ConnectorWork,
                "A multi-member partition request stays on the shared graph path; it is not the one-member direct seam contract.");
            Assert.AreEqual(2, work.PendingRouteCount);
        }

        [Test] public void SamePageMixedClearancePreparationFiltersInvalidCandidateAndRestoresCursor()
        {
            var blocker = new NavObstacle(new NavPoint(0, 0), .4);
            var provider = NavigationArrivalTestFactory.Layered("arrival-mixed-clearance", groundBlockers: new[] { blocker });
            var narrowInvalidForLarge = NavigationArrivalTestFactory.At(1, 0);
            var validForBoth = NavigationArrivalTestFactory.At(7, 0);
            var laterAlternative = NavigationArrivalTestFactory.At(11, 0);
            var members = new[] {
                NavigationArrivalTestFactory.Member(30, 0, NavigationArrivalTestFactory.At(-8, 0), 1.1),
                NavigationArrivalTestFactory.Member(31, 1, NavigationArrivalTestFactory.At(-8, 4), .2)
            };
            var input = NavigationArrivalTestFactory.Input(provider, new[] { narrowInvalidForLarge, validForBoth, laterAlternative }, members,
                NavigationArrivalTestFactory.Occupancy());
            var work = new NavigationArrivalWork(input);
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(0, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual("PrepareRegions", work.WorkPhase);
            Assert.AreEqual(1, work.AllocationWorkConsumed, "Candidate validity preparation consumes the allocation quota.");
            work = NavigationArrivalWork.Restore(input, work.Save());
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(1, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual("PrepareRegions", work.WorkPhase);
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted, work.Advance(2, 1, new NavEpochQuota(0, 0, 0, 0, 0)));
            Assert.AreEqual("ScanCandidates", work.WorkPhase);
            work = NavigationArrivalWork.Restore(input, work.Save());
            Assert.AreEqual(NavArrivalStatus.Ready, NavigationArrivalTestFactory.Complete(work));
            Assert.AreEqual(2, work.Proposals.Count);
            Assert.AreEqual(validForBoth, work.Proposals.Single(x => x.Member.EntityId == 30).Endpoint,
                "The invalid large-clearance candidate does not poison the valid alternative in its page.");
            Assert.AreEqual(narrowInvalidForLarge, work.Proposals.Single(x => x.Member.EntityId == 31).Endpoint,
                "The exact narrower clearance retains the earlier candidate and its input order.");
        }

        [Test] public void TwoMemberPartitionSeamCompletesWithExactTypedProposalsAndSharedTerminalField()
        {
            var map = ThreeCrossingsMap.Default;
            var outer = map.MovementBlockers.ToArray();
            var corridorWalls = new[] {
                new NavObstacle(-24, 6, 24, map.HalfExtent),
                new NavObstacle(-24, -map.HalfExtent, 24, -6),
                new NavObstacle(24, -map.HalfExtent, map.HalfExtent, map.HalfExtent),
                new NavObstacle(-map.HalfExtent, -map.HalfExtent, -24, map.HalfExtent)
            };
            var provider = new PartitionNavigationProvider(map,
                new NavGeometry(map.HalfExtent, outer.Concat(corridorWalls).ToArray(), map.Revision), null);
            var clearance = new NavClearanceProfile("ground", 1, "ground", 0, .58);
            Assert.True(provider.TryLocate(new NavPoint(-18, 0), clearance, out var firstOrigin));
            Assert.True(provider.TryLocate(new NavPoint(-18, 4), clearance, out var secondOrigin));
            Assert.True(provider.TryLocate(new NavPoint(18, 0), clearance, out var firstEndpoint));
            Assert.True(provider.TryLocate(new NavPoint(18, 4), clearance, out var secondEndpoint));
            Assert.True(provider.IsValid(firstOrigin, clearance)); Assert.True(provider.IsValid(secondOrigin, clearance));
            Assert.True(provider.IsValid(firstEndpoint, clearance)); Assert.True(provider.IsValid(secondEndpoint, clearance));
            var members = new[] {
                NavigationArrivalTestFactory.Member(50, 0, firstOrigin, .58),
                NavigationArrivalTestFactory.Member(51, 1, secondOrigin, .58)
            };
            // The full-height side walls close the four corner bypasses, leaving the
            // central x[-24,24], z[-6,6] strip. At .8 m this contains at most 61 x 17
            // sample positions. Two terminal seeds are bounded to 2074 field labels;
            // the test asserts the observed counter under the existing 4096 limit.
            var input = NavigationArrivalTestFactory.Input(provider, new[] { firstEndpoint, secondEndpoint }, members,
                NavigationArrivalTestFactory.Occupancy(), limits: new NavGraphCompileLimits(300000, 150000, 600000), graphCell: .8);
            double dx = firstEndpoint.Position.X - secondEndpoint.Position.X;
            double dz = firstEndpoint.Position.Z - secondEndpoint.Position.Z;
            double requiredSpacing = Math.Max(input.ArrivalProfile.ArrivalSlotSpacing,
                4 * Math.Max(members[0].Radius, members[1].Radius) +
                2 * input.ArrivalProfile.ArrivalTolerance + 2 * input.ArrivalProfile.LocalGridCell);
            Assert.AreEqual(3.6, requiredSpacing, 1e-9);
            Assert.GreaterOrEqual(Math.Sqrt(dx * dx + dz * dz), requiredSpacing,
                "Proposed endpoints respect the established slot spacing before route solving.");
            var work = new NavigationArrivalWork(input);
            var status = NavigationArrivalTestFactory.Complete(work);
            Assert.AreEqual(NavArrivalStatus.Ready, status, string.Format("status={0}; graphBuilds={1}; fieldBuilds={2}; connectorWork={3}; labels={4}; candidateWork={5}",
                status, work.RouteCounters.GraphBuilds, work.RouteCounters.FieldBuilds, work.RouteCounters.ConnectorWork,
                work.RouteCounters.PeakFieldLabels, work.CandidateWorkConsumed));
            Assert.AreEqual(2, work.Proposals.Count);
            var first = work.Proposals.Single(x => x.Member.EntityId == 50);
            var second = work.Proposals.Single(x => x.Member.EntityId == 51);
            Assert.AreEqual(firstEndpoint, first.Endpoint);
            Assert.AreEqual(secondEndpoint, second.Endpoint);
            Assert.AreEqual(firstOrigin, first.Route.Legs.First().From);
            Assert.AreEqual(firstEndpoint, first.Route.Legs.Last().To);
            Assert.AreEqual(secondOrigin, second.Route.Legs.First().From);
            Assert.AreEqual(secondEndpoint, second.Route.Legs.Last().To);
            Assert.True(first.Route.Legs.Any(x => x.Kind == NavTypedLegKind.Portal));
            Assert.True(second.Route.Legs.Any(x => x.Kind == NavTypedLegKind.Portal));
            foreach (var route in new[] { first.Route, second.Route })
                for (int i = 1; i < route.Legs.Count; i++)
                    Assert.AreEqual(route.Legs[i - 1].To, route.Legs[i].From, "Typed legs retain exact endpoint continuity.");
            Assert.AreEqual(1, work.RouteCounters.FieldBuilds,
                "Equal-clearance members reuse the prepared terminal region and its shared field.");
            Assert.LessOrEqual(work.RouteCounters.PeakFieldLabels, 2074,
                "The actual shared field remains inside the rectangle's two-seed label bound.");
        }
    }
}

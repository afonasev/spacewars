using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PartitionNavigationProviderTests
    {
        private static readonly NavClearanceProfile Explorer = new NavClearanceProfile("explorer", 1, "ground", 1, .58);
        private static readonly NavClearanceProfile Tank = new NavClearanceProfile("tank", 1, "ground", 1, .72);
        private static PartitionNavigationProvider Adapter(IPlayableTerrain map, NavObstacle? extra = null, string hold = "none")
        {
            var blockers = extra == null ? map.MovementBlockers.ToArray() : map.MovementBlockers.Concat(new[] { extra.Value }).ToArray();
            return new PartitionNavigationProvider(map, new NavGeometry(map.HalfExtent, blockers, map.Revision + (extra == null ? 0 : 1)), hold);
        }

        [Test] public void ThreeCrossingsUsesAuthoredFootprintAndAllowedPhysicalSeams()
        {
            var map = ThreeCrossingsMap.Default; var provider = Adapter(map);
            Assert.IsTrue(provider.Seams.Any(s => s.Allowed && s.FromSurface != s.ToSurface));
            Assert.IsTrue(provider.Seams.Any(s => !s.Allowed));
            foreach (var clearance in new[] { Explorer, Tank }) {
                Assert.True(provider.TryLocate(new NavPoint(-18, 0), clearance, out var west));
                Assert.True(provider.TryLocate(new NavPoint(18, 0), clearance, out var east));
                Assert.True(provider.TryTraverse(west, east.Position, clearance, out var reached));
                Assert.AreEqual(east, reached);
                Assert.True(provider.TryTrace(west, east.Position, clearance, out var traced, out var legs));
                Assert.AreEqual(east, traced);
                Assert.AreEqual(west, legs.First().From);
                Assert.AreEqual(east, legs.Last().To);
                Assert.True(legs.Any(l => l.Kind == NavTypedLegKind.Portal && l.Cost == 0 &&
                    provider.Seams.Any(s => s.Id == l.PortalId && s.Allowed)));
                Assert.AreEqual(36, legs.Sum(l => l.Cost), 1e-9);
                Assert.False(provider.CanSweep(west, east, clearance)); // typed cross-surface route needs a seam trace
                Assert.False(provider.TryLocate(new NavPoint(0, 20), clearance, out _));
                Assert.False(provider.IsValid(new NavLocation(west.Position, east.SurfaceId), clearance));
            }
        }

        [Test] public void BlockedCrossingAndNarrowFootprintCannotAcquireAnInventedSeam()
        {
            var map = ThreeCrossingsMap.Default; var provider = Adapter(map);
            foreach (var clearance in new[] { Explorer, Tank }) {
                Assert.True(provider.TryLocate(new NavPoint(-20, 18), clearance, out var bank));
                Assert.False(provider.TryTrace(bank, new NavPoint(20, 18), clearance, out _, out _));
                Assert.False(provider.TryLocate(new NavPoint(0, map.CentralBridgeWidth / 2 - clearance.Radius / 2), clearance, out _));
            }
            var independent = Adapter(ThreeCrossingsMap.Default);
            Assert.AreEqual(provider.Seams.Select(s => s.Id).OrderBy(s => s).ToArray(),
                independent.Seams.Select(s => s.Id).OrderBy(s => s).ToArray());
            Assert.True(provider.TryLocate(new NavPoint(-18, 0), Explorer, out var start));
            Assert.True(provider.TryTrace(start, new NavPoint(18, 0), Explorer, out _, out var trace));
            Assert.True(trace.Any(l => l.Kind == NavTypedLegKind.Portal && l.Cost == 0));
            Assert.True(provider.TryLocate(new NavPoint(18, 0), Explorer, out var end));
            Assert.True(provider.TryTrace(end, start.Position, Explorer, out var reversed, out var reverseTrace));
            Assert.AreEqual(start, reversed);
            Assert.True(reverseTrace.Any(l => l.Kind == NavTypedLegKind.Portal && l.Cost == 0));
            Assert.AreEqual(36, reverseTrace.Sum(l => l.Cost), 1e-9);
        }

        [Test] public void FoundryBindingIncludesScaleBuildingsAndHoldIdentity()
        {
            var map = new FoundryMap(new FoundryProfileData());
            var baseProvider = Adapter(map);
            Assert.IsNotEmpty(baseProvider.Seams.Where(s => s.Allowed));
            Assert.AreNotEqual(baseProvider.Binding, Adapter(map, hold: "hold/1").Binding);
            Assert.AreNotEqual(baseProvider.Binding, Adapter(new FoundryMap(new FoundryProfileData { scale = 1.5 })).Binding);
            var blocker = new NavObstacle(new NavPoint(0, 0), 2);
            var built = Adapter(map, blocker);
            Assert.AreNotEqual(baseProvider.Binding, built.Binding);
            Assert.True(baseProvider.TryLocate(new NavPoint(0, 0), Tank, out var open));
            Assert.False(built.IsValid(open, Tank));
            Assert.True(baseProvider.IsValid(open, Tank));
        }

        [Test] public void PolygonBuildingsAndHoldNearInputCapAreAccountedBeforeAdmission()
        {
            var map = ThreeCrossingsMap.Default;
            var vertices = Enumerable.Range(0, 256).Select(i => {
                double angle = 2 * Math.PI * i / 256;
                return new NavPoint(-30 + 2 * Math.Cos(angle), -20 + 2 * Math.Sin(angle));
            }).ToArray();
            var polygon = new NavObstacle(new NavPolygon(vertices));
            var baseProvider = Adapter(map, hold: "hold/polygon");
            var oneBuilding = Adapter(map, polygon, "hold/polygon");
            Assert.Greater(oneBuilding.EstimatedRetainedBytes - baseProvider.EstimatedRetainedBytes, 128L * 256);
            Assert.AreNotEqual(baseProvider.Binding, oneBuilding.Binding);
            var blockers = map.MovementBlockers.Concat(Enumerable.Repeat(polygon, 2048 - map.MovementBlockers.Count)).ToArray();
            var capped = new PartitionNavigationProvider(map,
                new NavGeometry(map.HalfExtent, blockers, map.Revision + 2), "hold/polygon");
            Assert.Greater(capped.EstimatedRetainedBytes, 60L * 1024 * 1024);
            Assert.True(capped.TryLocate(new NavPoint(-18, 0), Explorer, out var origin));
            Assert.True(capped.TryLocate(new NavPoint(18, 0), Explorer, out var endpoint));
            var region = new NavTerminalRegion("east", 1, endpoint, new[] { endpoint });
            var subscription = new NavRouteSubscription(1, 1, 1, 0, 0, 0, 0, 1, 1, 0, 1,
                origin, endpoint, "hold/polygon", NavRouteProvenance.CommandMember);
            var request = new NavScheduledRequest(capped, Explorer,
                new NavGraphProfile(new string('a', 64), "distance", 1, .8),
                new NavGraphCompileLimits(300000, 150000, 600000), region, subscription, 300000);
            var scheduler = new LayeredNavigationScheduler();
            Assert.True(scheduler.Submit(request));
            var other = new PartitionNavigationProvider(map,
                new NavGeometry(map.HalfExtent, blockers, map.Revision + 2), "hold/polygon-2");
            var otherSubscription = new NavRouteSubscription(1, 2, 1, 0, 0, 0, 0, 1, 1, 0, 2,
                origin, endpoint, "hold/polygon-2", NavRouteProvenance.CommandMember);
            Assert.False(scheduler.Submit(new NavScheduledRequest(other, Explorer,
                new NavGraphProfile(new string('a', 64), "distance", 1, .8),
                new NavGraphCompileLimits(300000, 150000, 600000), region, otherSubscription, 300000)));
            Assert.Throws<ArgumentException>(() => new PartitionNavigationProvider(map,
                new NavGeometry(map.HalfExtent, blockers.Concat(new[] { polygon }).ToArray(), map.Revision + 3), "hold/polygon"));
        }

        [Test] public void UnsupportedTerrainCannotMasqueradeAsAuthoredPartition()
        {
            var stacked = new StackedNavigationFixture();
            Assert.Throws<ArgumentException>(() => Adapter(stacked));
        }

        [Test] public void RequestProvenancePreservesRealZeroAndAbsentValues()
        {
            var p = new NavLocation(new NavPoint(0, 0), NavLocation.FlatSurface);
            var member = new NavRouteSubscription(1, 1, 1, 0, 0, 0, 0, 1, 1, 0, 1, p, p, "",
                NavRouteProvenance.CommandMember);
            var legacy = new NavRouteSubscription(1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, p, p, "",
                NavRouteProvenance.LegacySingleton);
            var probe = new NavRouteSubscription(1, 0, 0, 0, 0, 0, -1, 0, 0, 0, 1, p, p, null,
                NavRouteProvenance.ProducerProbe);
            Assert.AreEqual(0, member.EntityId); Assert.AreEqual(0, member.MobilityRevision);
            Assert.AreEqual(0, member.CommandSequence); Assert.AreEqual("", member.HoldBinding);
            Assert.IsNull(probe.HoldBinding);
            Assert.AreNotEqual(member.StableKey, legacy.StableKey);
            Assert.AreNotEqual(legacy.StableKey, probe.StableKey);
            Assert.Throws<ArgumentException>(() => new NavRouteSubscription(1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, p, p,
                null, NavRouteProvenance.ProducerProbe));
        }
    }
}

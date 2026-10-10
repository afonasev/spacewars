using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PartitionNavigationSchedulerTests
    {
        private static string Sha(string value)
        { using (var hash = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }

        [Test] public void SingletonCrossesPermittedSeamBeforeGraphCompilation()
        {
            var map = ThreeCrossingsMap.Default;
            var provider = new PartitionNavigationProvider(map,
                new NavGeometry(map.HalfExtent, map.MovementBlockers.ToArray(), map.Revision), null);
            foreach (var radius in new[] { .58, .72 }) {
                var clearance = new NavClearanceProfile("ground", 1, "ground", 0, radius);
                Assert.True(provider.TryLocate(new NavPoint(-18, 0), clearance, out var origin));
                Assert.True(provider.TryLocate(new NavPoint(18, 0), clearance, out var endpoint));
                var subscription = new NavRouteSubscription(1, 0, 0, 0, 0, 0, 0, 1, 1, 0, 1,
                    origin, endpoint, null, NavRouteProvenance.LegacySingleton);
                var request = NavScheduledRequest.ForSingleton(provider, clearance,
                    new NavGraphProfile(Sha("direct-seam"), "distance", 1, .8),
                    new NavGraphCompileLimits(300000, 150000, 600000), subscription, 300000);
                var scheduler = new LayeredNavigationScheduler();
                Assert.True(scheduler.Submit(request));
                scheduler.Advance(0, new NavEpochQuota(0, 0, 8), _ => true);
                Assert.True(scheduler.TryTake(_ => true, out var result));
                Assert.AreEqual(NavSolveStatus.Ready, result.Status);
                Assert.True(result.Route.Legs.Any(leg => leg.Kind == NavTypedLegKind.Portal));
                Assert.AreEqual(0, scheduler.Counters.GraphBuilds);
                Assert.AreEqual(0, scheduler.Counters.PhysicalGraphWork);
            }
        }

        [Test] public void FoundryColdWarmAndPendingRestoreKeepTypedOutcomeAndFiniteAccounting()
        {
            var map = new FoundryMap(new FoundryProfileData { scale = 1.5 });
            var provider = new PartitionNavigationProvider(map,
                new NavGeometry(map.HalfExtent, map.MovementBlockers.ToArray(), map.Revision), "");
            var clearance = new NavClearanceProfile("explorer", 1, "ground", 0, .58);
            var profile = new NavGraphProfile(Sha("foundry-route-.8"), "distance", 1, .8);
            var limits = new NavGraphCompileLimits(300000, 150000, 600000);
            Assert.True(provider.TryLocate(new NavPoint(0, 0), clearance, out var origin));
            Assert.True(provider.TryLocate(map.Point(0, 100), clearance, out var endpoint));
            var region = new NavTerminalRegion("foundry-rear", 1, endpoint, new[] { endpoint });
            NavScheduledRequest Request(long group, long request) => new NavScheduledRequest(provider, clearance,
                profile, limits, region, new NavRouteSubscription(1, group, 1, 0, 0, 0, 0, 1, 1, 0,
                    request, origin, endpoint, "", NavRouteProvenance.CommandMember), 300000);
            var quota = new NavEpochQuota(20000, 20000, 20000, 20000, 256);
            NavScheduledCompletion Finish(LayeredNavigationScheduler scheduler, long startEpoch)
            {
                for (long epoch = startEpoch; epoch < startEpoch + 300; epoch++) {
                    scheduler.Advance(epoch, quota, _ => true);
                    Assert.LessOrEqual(scheduler.Counters.RetainedBytes, LayeredNavigationScheduler.MaxRetainedBytes);
                    if (scheduler.TryTake(_ => true, out var result)) return result;
                }
                Assert.Fail("Actual Foundry scheduler did not finish within finite epochs."); return null;
            }

            var cold = new LayeredNavigationScheduler(); Assert.True(cold.Submit(Request(1, 1)));
            var first = Finish(cold, 0);
            Assert.AreEqual(NavSolveStatus.Ready, first.Status);
            Assert.AreEqual(origin, first.Route.Legs.First().From);
            Assert.AreEqual(endpoint, first.Route.Legs.Last().To);
            Assert.True(first.Route.Legs.Any(l => l.Kind == NavTypedLegKind.Portal));
            long physicalGraph = cold.Counters.PhysicalGraphWork;
            long physicalField = cold.Counters.PhysicalFieldWork;

            Assert.True(cold.Submit(Request(2, 2)));
            var second = Finish(cold, 300);
            Assert.AreEqual(NavSolveStatus.Ready, second.Status);
            Assert.AreEqual(first.Route.Cost, second.Route.Cost, 1e-9);
            Assert.AreEqual(physicalGraph, cold.Counters.PhysicalGraphWork);
            Assert.AreEqual(physicalField, cold.Counters.PhysicalFieldWork);
            Assert.Greater(cold.Counters.CacheHits, 0);

            var pending = Request(3, 3); var restored = new LayeredNavigationScheduler();
            Assert.True(restored.Submit(pending));
            restored.Advance(0, quota, _ => true);
            Assert.Greater(restored.PendingCount, 0);
            restored = LayeredNavigationScheduler.Restore(restored.Save(), new[] { pending });
            var third = Finish(restored, 1);
            Assert.AreEqual(NavSolveStatus.Ready, third.Status);
            Assert.AreEqual(first.Route.Cost, third.Route.Cost, 1e-9);
            TestContext.WriteLine("ACTUAL-SCHED graphPhysical=" + cold.Counters.PhysicalGraphWork +
                " fieldPhysical=" + cold.Counters.PhysicalFieldWork + " cacheHits=" + cold.Counters.CacheHits +
                " peakRetained=" + cold.Counters.PeakRetainedBytes);
        }
    }
}

using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PartitionNavigationGraphTests
    {
        private static string Sha(string value)
        { using (var hash = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        private static readonly NavGraphProfile Profile = new NavGraphProfile(Sha("partition-actual-grid-.8"), "distance", 1, .8);
        private static readonly NavGraphCompileLimits Limits = new NavGraphCompileLimits(300000, 150000, 600000);
        private static NavClearanceProfile Clearance(double radius) => new NavClearanceProfile("actual", 1, "ground", 1, radius);
        private static PartitionNavigationProvider Provider(IPlayableTerrain map) => new PartitionNavigationProvider(map,
            new NavGeometry(map.HalfExtent, map.MovementBlockers.ToArray(), map.Revision), "none");

        [Test] public void CompileStartsWithBoundedPagesAndRestoresDuringSamplingAndArcs()
        {
            var provider = Provider(ThreeCrossingsMap.Default);
            var clearance = Clearance(.58);
            var work = new LayeredNavigationCompileWork(provider, clearance, Profile, Limits);
            Assert.AreEqual(0, work.SampleCount); Assert.AreEqual(0, work.ArcCount);
            Assert.Less(work.EstimatedRetainedBytes, 100000);
            Assert.AreEqual(1, work.Advance(1));
            Assert.Less(work.EstimatedRetainedBytes, 100000);
            work = LayeredNavigationCompileWork.Restore(provider, clearance, Profile, Limits, work.Save());
            while (work.Phase == LayeredNavigationCompileWork.Stage.Samples) work.Advance(4096);
            Assert.AreEqual(LayeredNavigationCompileWork.Stage.GridEdges, work.Phase);
            work.Advance(128);
            work = LayeredNavigationCompileWork.Restore(provider, clearance, Profile, Limits, work.Save());
            while (work.Phase != LayeredNavigationCompileWork.Stage.Ready) Assert.Greater(work.Advance(4096), 0);
            Assert.AreEqual(14955, work.Graph.Nodes.Count);
            Assert.AreEqual(57280, work.Graph.Arcs.Count);
        }

        [Test] public void RealCrossingDetoursAndThreeBlockedBridgesProveUnreachable()
        {
            var map = ThreeCrossingsMap.Default; var clearance = Clearance(.58);
            NavIncrementalSharedRoute Solve(PartitionNavigationProvider provider, out NavLocation start, out NavLocation end)
            {
                Assert.True(provider.TryLocate(new NavPoint(-20, 18), clearance, out start));
                Assert.True(provider.TryLocate(new NavPoint(20, 18), clearance, out end));
                Assert.False(provider.TryTrace(start, end.Position, clearance, out _, out _));
                var graph = LayeredNavigationGraphCompiler.Compile(provider, clearance, Profile, Limits);
                var region = new NavTerminalRegion("east-bank", 1, end, new[] { end });
                var field = new NavSharedTerminalField(graph, region, 300000);
                int remaining = 1000000;
                while (field.Status == NavSolveStatus.Pending && remaining > 0)
                    remaining -= field.Advance(Math.Min(10000, remaining)).Consumed;
                Assert.Greater(remaining, 0); Assert.AreEqual(NavSolveStatus.Ready, field.Status);
                var subscription = new NavRouteSubscription(1, 1, 1, 0, 0, 0, 0, 1, 1, 0, 1, start, end, "",
                    NavRouteProvenance.CommandMember);
                var route = new NavIncrementalSharedRoute(field, subscription);
                remaining = 1000000;
                while (route.Status == NavSolveStatus.Pending && remaining > 0)
                    remaining -= route.Advance(Math.Min(10000, remaining)).Consumed;
                Assert.Greater(remaining, 0);
                return route;
            }
            var open = Solve(Provider(map), out var origin, out var endpoint);
            Assert.AreEqual(NavSolveStatus.Ready, open.Status);
            Assert.AreEqual(origin, open.Route.Legs.First().From);
            Assert.AreEqual(endpoint, open.Route.Legs.Last().To);
            Assert.True(open.Route.Legs.Any(l => Math.Abs(l.From.Position.Z - 18) > 1 || Math.Abs(l.To.Position.Z - 18) > 1));
            Assert.True(open.Route.Legs.Any(l => l.Kind == NavTypedLegKind.Portal && l.Cost == 0));

            var barriers = map.MovementBlockers.Concat(new[] {
                new NavObstacle(-2, -12, 2, 12), new NavObstacle(-2, 30, 2, 46),
                new NavObstacle(-2, -46, 2, -30) }).ToArray();
            var blocked = new PartitionNavigationProvider(map,
                new NavGeometry(map.HalfExtent, barriers, map.Revision + 1), "three-bridges-blocked");
            var impossible = Solve(blocked, out _, out _);
            Assert.AreEqual(NavSolveStatus.UnreachableInGraph, impossible.Status);
        }

        [TestCase(false, .58, 14955, 57280, 420)]
        [TestCase(false, .72, 14233, 54408, 396)]
        [TestCase(true, .58, 132657, 527356, 5956)]
        [TestCase(true, .72, 131225, 521632, 5940)]
        public void ExactPartitionGridFitsCompactRepresentation(bool foundry, double radius, int nodes, int arcs, int seams)
        {
            IPlayableTerrain map = foundry ? new FoundryMap(new FoundryProfileData { scale = 1.5 }) : ThreeCrossingsMap.Default;
            var provider = Provider(map);
            var work = new LayeredNavigationCompileWork(provider, Clearance(radius), Profile, Limits);
            while (work.Phase != LayeredNavigationCompileWork.Stage.Ready) Assert.Greater(work.Advance(4096), 0);
            var graph = work.Graph;
            Assert.AreEqual(nodes, graph.Nodes.Count); Assert.AreEqual(arcs, graph.Arcs.Count);
            Assert.AreEqual(seams, graph.Arcs.Count(a => a.Kind == NavGraphArcKind.Portal));
            TestContext.WriteLine("PACKED map=" + (foundry ? "Foundry/1.5" : "ThreeCrossings/default") +
                " radius=" + radius + " nodes=" + graph.Nodes.Count + " arcs=" + graph.Arcs.Count +
                " seams=" + seams + " graphBytes=" + graph.EstimatedRetainedBytes +
                " providerBytes=" + provider.EstimatedRetainedBytes + " compileSteps=" + work.Consumed);
            Assert.Less(graph.EstimatedRetainedBytes + provider.EstimatedRetainedBytes, 64L * 1024 * 1024);
            foreach (var arc in graph.Arcs.Where(a => a.Kind == NavGraphArcKind.Portal)) {
                Assert.IsNotNull(arc.Trace);
                Assert.AreEqual(graph.Nodes[arc.From].Location, arc.Trace.First().From);
                Assert.AreEqual(graph.Nodes[arc.To].Location, arc.Trace.Last().To);
                Assert.AreEqual(arc.Cost, arc.Trace.Sum(l => l.Cost), 1e-9);
                Assert.True(arc.Trace.Where(l => l.Kind == NavTypedLegKind.Portal).All(l =>
                    l.Cost == 0 && provider.Seams.Any(s => s.Id == l.PortalId && s.Allowed)));
            }
            var restored = LayeredNavigationCompileWork.Restore(provider, Clearance(radius), Profile, Limits, work.Save());
            Assert.AreEqual(graph.Nodes.Count, restored.Graph.Nodes.Count);
            Assert.AreEqual(graph.Arcs.Count, restored.Graph.Arcs.Count);
        }

        [TestCase(false, 1.0, 1)]
        [TestCase(true, 1.5, 1)]
        [TestCase(true, 1.5, 2)]
        public void SharedFieldAndConnectorReachExactAuthoredDestination(bool foundry, double scale, int candidates)
        {
            IPlayableTerrain map = foundry ? new FoundryMap(new FoundryProfileData { scale = scale }) : ThreeCrossingsMap.Default;
            var provider = Provider(map); var clearance = Clearance(.58);
            var graph = LayeredNavigationGraphCompiler.Compile(provider, clearance, Profile, Limits);
            var originPoint = foundry ? new NavPoint(0, 0) : new NavPoint(-18, 0);
            var targetPoint = foundry ? new NavPoint(0, 100 * scale) : new NavPoint(18, 0);
            Assert.True(provider.TryLocate(originPoint, clearance, out var origin));
            Assert.True(provider.TryLocate(targetPoint, clearance, out var target));
            var candidatePoints = candidates == 1 ? new[] { target } :
                new[] { target, new NavLocation(new NavPoint(10 * scale, 100 * scale), target.SurfaceId) };
            Assert.True(candidatePoints.All(c => provider.IsValid(c, clearance)));
            var region = new NavTerminalRegion("actual", 1, target, candidatePoints);
            var field = new NavSharedTerminalField(graph, region, 300000);
            int remaining = 3000000;
            while (field.Status == NavSolveStatus.Pending && remaining > 0) remaining -= field.Advance(Math.Min(10000, remaining)).Consumed;
            Assert.Greater(remaining, 0); Assert.AreEqual(NavSolveStatus.Ready, field.Status);
            Assert.Less(graph.EstimatedRetainedBytes + field.EstimatedRetainedBytes + provider.EstimatedRetainedBytes, 64L * 1024 * 1024);
            TestContext.WriteLine("FIELD map=" + (foundry ? "Foundry/1.5" : "ThreeCrossings/default") +
                " candidates=" + candidates + " labels=" + field.LabelCount + " frontier=" + field.FrontierCount +
                " graphBytes=" + graph.EstimatedRetainedBytes + " fieldBytes=" + field.EstimatedRetainedBytes +
                " providerBytes=" + provider.EstimatedRetainedBytes);
            var subscription = new NavRouteSubscription(1, 1, 1, 0, 0, 0, 0, 1, 1, 0, 1, origin, target, "",
                NavRouteProvenance.CommandMember);
            var connector = new NavIncrementalSharedRoute(field, subscription);
            remaining = 3000000;
            while (connector.Status == NavSolveStatus.Pending && remaining > 0) remaining -= connector.Advance(Math.Min(10000, remaining)).Consumed;
            Assert.Greater(remaining, 0); Assert.AreEqual(NavSolveStatus.Ready, connector.Status);
            Assert.AreEqual(origin, connector.Route.Legs.First().From);
            Assert.AreEqual(target, connector.Route.Legs.Last().To);
            Assert.True(connector.Route.Legs.Any(l => l.Kind == NavTypedLegKind.Portal && l.Cost == 0));
            Assert.AreEqual(connector.Route.Cost, connector.Route.Legs.Sum(l => l.Cost), 1e-8);
            for (int i = 1; i < connector.Route.Legs.Count; i++)
                Assert.AreEqual(connector.Route.Legs[i - 1].To, connector.Route.Legs[i].From);
        }

        [TestCase(0, .58)] [TestCase(0, .72)]
        [TestCase(1, .58)] [TestCase(1, .72)]
        [TestCase(2, .58)] [TestCase(2, .72)]
        [TestCase(3, .58)] [TestCase(3, .72)]
        [TestCase(4, .58)] [TestCase(4, .72)]
        [TestCase(5, .58)] [TestCase(5, .72)]
        [TestCase(6, .58)] [TestCase(6, .72)]
        [TestCase(7, .58)] [TestCase(7, .72)]
        [TestCase(8, .58)] [TestCase(8, .72)]
        [TestCase(9, .58)] [TestCase(9, .72)]
        [TestCase(10, .58)] [TestCase(10, .72)]
        public void EverySupportedFoundryScaleCompilesTheSameQualifiedGrid(int i, double radius)
        {
            double[] scales = { 1, 1.05, 1.1, 1.15, 1.2, 1.25, 1.3, 1.35, 1.4, 1.45, 1.5 };
            int[] smallNodes = { 58189, 64489, 71003, 77403, 84715, 91383, 99119, 107369, 114827, 123577, 132657 };
            int[] largeNodes = { 57947, 63859, 69931, 76575, 84715, 90989, 98533, 106203, 114791, 122699, 131225 };
            int[] smallArcs = { 230580, 255676, 281608, 307100, 336244, 362808, 393648, 426548, 456268, 491156, 527356 };
            int[] largeArcs = { 229600, 253140, 277316, 303784, 336244, 361224, 391288, 421872, 456116, 487628, 521632 };
            var map = new FoundryMap(new FoundryProfileData { scale = scales[i] });
            var provider = Provider(map);
            var graph = LayeredNavigationGraphCompiler.Compile(provider, Clearance(radius), Profile, Limits);
            Assert.AreEqual(radius == .58 ? smallNodes[i] : largeNodes[i], graph.Nodes.Count, "nodes scale=" + scales[i]);
            Assert.AreEqual(radius == .58 ? smallArcs[i] : largeArcs[i], graph.Arcs.Count, "arcs scale=" + scales[i]);
            Assert.Less(graph.EstimatedRetainedBytes + provider.EstimatedRetainedBytes, 64L * 1024 * 1024);
            TestContext.WriteLine("SCALE=" + scales[i] + " R=" + radius + " N=" + graph.Nodes.Count +
                " A=" + graph.Arcs.Count + " B=" + graph.EstimatedRetainedBytes);
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    // Reconnaissance of the current .8 m grid. Counts use the real terrain
    // predicates, not an enclosing rectangle or a synthetic polygon provider.
    public sealed class ActualMapCapacityReconTests
    {
        [TestCase(1, .58)]
        [TestCase(1, .72)]
        [TestCase(1.05, .58)]
        [TestCase(1.05, .72)]
        [TestCase(1.1, .58)]
        [TestCase(1.1, .72)]
        [TestCase(1.15, .58)]
        [TestCase(1.15, .72)]
        [TestCase(1.2, .58)]
        [TestCase(1.2, .72)]
        [TestCase(1.25, .58)]
        [TestCase(1.25, .72)]
        [TestCase(1.3, .58)]
        [TestCase(1.3, .72)]
        [TestCase(1.35, .58)]
        [TestCase(1.35, .72)]
        [TestCase(1.4, .58)]
        [TestCase(1.4, .72)]
        [TestCase(1.45, .58)]
        [TestCase(1.45, .72)]
        [TestCase(1.5, .58)]
        [TestCase(1.5, .72)]
        public void Foundry(double scale, double radius)
        {
            Count(new FoundryMap(new FoundryProfileData { scale = scale }), radius, "Foundry/" + scale);
        }

        [TestCase(.58)]
        [TestCase(.72)]
        public void ThreeCrossings(double radius)
        {
            Count(ThreeCrossingsMap.Default, radius, "ThreeCrossings/default");
        }

        private static void Count(IPlayableTerrain map, double radius, string label)
        {
            const double cell = .8;
            int low = (int)Math.Ceiling(-map.HalfExtent / cell);
            int high = (int)Math.Floor(map.HalfExtent / cell);
            long candidates = (long)(high - low + 1) * (high - low + 1);
            var points = new Dictionary<Tuple<int, int>, NavLocation>();
            for (int x = low; x <= high; x++)
                for (int z = low; z <= high; z++)
                {
                    var at = new NavPoint(x * cell, z * cell);
                    if (map.TryLocate(at, radius, out var location)) points.Add(Tuple.Create(x, z), location);
                }
            long neighborPairs = 0, qualifiedPairs = 0, seamPairs = 0;
            foreach (var point in points)
            {
                int x = point.Key.Item1, z = point.Key.Item2;
                foreach (var neighbor in new[] { Tuple.Create(x + 1, z), Tuple.Create(x, z + 1) })
                {
                    if (!points.TryGetValue(neighbor, out var endpoint)) continue;
                    neighborPairs++;
                    if (!map.TryTraverse(point.Value, endpoint.Position, radius, out var reached) || !reached.Equals(endpoint)) continue;
                    qualifiedPairs++;
                    if (point.Value.SurfaceId != endpoint.SurfaceId) seamPairs++;
                }
            }
            TestContext.WriteLine("CAPACITY map=" + label + " radius=" + radius + " grid=" + cell +
                " candidates=" + candidates + " nodes=" + points.Count + " neighborPairs=" + neighborPairs +
                " qualifiedDirectedArcs=" + (2 * qualifiedPairs) + " seamDirectedArcs=" + (2 * seamPairs));
            Assert.Greater(points.Count, 0);
        }
    }
}

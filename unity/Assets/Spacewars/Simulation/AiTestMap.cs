using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Authored diagnostic content: identical open-ground opportunities for eight seats.
    public sealed class AiTestMap : IPlayableTerrain
    {
        public const int Capacity = 8;
        public string Id => "ai-eight-player-test-map-v1";
        public int Revision => 1;
        public double HalfExtent => 144;
        public double DirectFireHeight => 1;
        public int SurfaceSemanticsVersion => 1;
        public IReadOnlyList<MapSupport> Supports { get; }
        public IReadOnlyList<NavObstacle> Solids => Array.Empty<NavObstacle>();
        public IReadOnlyList<NavObstacle> MovementBlockers => Solids;
        public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions => Array.Empty<NavSurfaceTransition>();
        private readonly NavGeometry geometry;
        private readonly TerrainLocations locations;

        public AiTestMap()
        {
            Supports = Array.AsReadOnly(new[] { new MapSupport("open-ground",
                new NavObstacle(-HalfExtent, -HalfExtent, HalfExtent, HalfExtent), false, surfaceId: "open-ground") });
            geometry = new NavGeometry(HalfExtent, Array.Empty<NavObstacle>(), Revision);
            locations = new TerrainLocations(geometry, Supports, SurfaceTransitions);
        }
        private static NavPoint Ring(int seat, double radius, double phase = 0)
        {
            double angle = -Math.PI / 2 + seat * Math.PI / 4 + phase;
            return new NavPoint(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
        }
        public NavPoint Headquarters(PlayableOwner owner)
        {
            int seat = (int)owner;
            if (seat < 0 || seat >= Capacity) throw new ArgumentOutOfRangeException(nameof(owner));
            return Ring(seat, 112);
        }
        public TerritorySite[] Sites(PlayableProfile profile)
        {
            var sites = new List<TerritorySite>();
            for (int i = 0; i < Capacity; i++) sites.Add(new TerritorySite(i + 1, PlayableBuildingKind.Headquarters, Headquarters((PlayableOwner)i), profile));
            for (int i = 0; i < Capacity; i++) sites.Add(new TerritorySite(i + 9, PlayableBuildingKind.Mine, Ring(i, 76), profile));
            for (int i = 0; i < 4; i++) sites.Add(new TerritorySite(i + 17, PlayableBuildingKind.Outpost, Ring(i * 2, 38), profile));
            for (int i = 0; i < 4; i++) sites.Add(new TerritorySite(i + 21, PlayableBuildingKind.Mine, Ring(i * 2, 32, Math.PI / 4), profile));
            return sites.ToArray();
        }
        public OfflineMatchConfiguration Configuration(PlayableProfile profile, int seed, OfflineParticipant[] roster)
        {
            var starts = Enumerable.Range(0, Capacity).Select(i => new OfflineStart("start-" + (i + 1),
                i + 1, Headquarters((PlayableOwner)i), Ring(i, 98), Math.Atan2(-Headquarters((PlayableOwner)i).Z, -Headquarters((PlayableOwner)i).X), i + 1)).ToArray();
            var costs = new double[Capacity, Capacity];
            for (int i = 0; i < Capacity; i++) for (int j = i + 1; j < Capacity; j++)
            {
                double dx = starts[i].Position.X - starts[j].Position.X, dz = starts[i].Position.Z - starts[j].Position.Z;
                costs[i, j] = costs[j, i] = Math.Sqrt(dx * dx + dz * dz);
            }
            return new OfflineMatchConfiguration(profile, "native-ai-test-map-v1", Id, "open-ground-euclidean-v1", seed,
                roster, starts, Sites(profile), Array.Empty<NavObstacle>(), costs, terrain: Id);
        }
        public MapSupport SupportAt(NavPoint point) => Supports[0].Contains(point) ? Supports[0] : null;
        public double SurfaceHeight(NavPoint point) => 0;
        public NavPoint SurfaceGradient(NavPoint point) => default(NavPoint);
        public bool SupportsFootprint(NavPoint point, double radius) => geometry.IsFree(point, radius);
        public bool SupportsSweep(NavPoint from, NavPoint to, double radius) => geometry.SegmentFree(from, to, radius);
        public bool TryLocate(NavPoint point, double radius, out NavLocation location) => locations.TryLocate(point, radius, out location);
        public bool IsValidLocation(NavLocation location, double radius) => locations.Valid(location, radius);
        public bool TryTraverse(NavLocation from, NavPoint to, double radius, out NavLocation location) => locations.Traverse(from, to, radius, out location);
        public bool CompatibleCombatSurface(NavLocation anchor, NavLocation other) => locations.Combat(anchor, other);
    }
}

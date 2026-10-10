using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Bounded authored QA terrain; deliberately absent from the lobby roster.
    // These geometry values are fixture data, not new gameplay balance/defaults.
    // Existing XZ crowd/host cannot execute portal legs yet (A2-d/A5/B).
    public sealed class StackedNavigationFixture : ILayeredPlayableTerrain
    {
        public StackedNavigationFixture(bool open = true, NavPortalDirection direction = NavPortalDirection.Both)
        {
            var lower = new NavLayerSurface("lower", "lower-combat", 0, 1, Area(-8, -8, 8, 8));
            var upper = new NavLayerSurface("upper", "upper-combat", 4, 2, Area(-8, -8, 8, 8, new NavObstacle(4, -1, 5, 1)));
            var portal = new NavLayerPortal("ramp", Entry, Exit, direction, open, 6, Area(-3, -.75, 3, .75), new[] { "ground" });
            // Exact map binding over all authored geometry/metadata; constructor
            // adds provider ID/revisions as a separate semantic binding.
            string mapBinding = NavContract.Digest(w => { lower.Write(w); upper.Write(w); portal.Write(w); });
            Provider = new LayeredNavigationProvider(Id, Revision, SurfaceSemanticsVersion, Revision, mapBinding, new[] { lower, upper }, new[] { portal });
            Supports = Array.AsReadOnly(new[] {
                new MapSupport("floor", new NavObstacle(-8,-8,8,8), false, surfaceId:"lower"),
                new MapSupport("floor", new NavObstacle(-8,-8,8,8), false, 4, surfaceId:"upper") });
            SurfaceTransitions = Array.AsReadOnly(new[] { new NavSurfaceTransition("ramp", "lower", "upper") });
        }
        public static NavLocation Entry => new NavLocation(new NavPoint(-2, 0), "lower");
        public static NavLocation Exit => new NavLocation(new NavPoint(2, 0), "upper");
        public LayeredNavigationProvider Provider { get; } public ILayeredNavigationProvider LayeredNavigation => Provider;
        public string Id => "stacked-navigation-qualification-v1"; public int Revision => 1; public int SurfaceSemanticsVersion => 2;
        public double HalfExtent => 8; public double DirectFireHeight => 1;
        public IReadOnlyList<MapSupport> Supports { get; } public IReadOnlyList<NavSurfaceTransition> SurfaceTransitions { get; }
        public IReadOnlyList<NavObstacle> Solids => Array.Empty<NavObstacle>(); public IReadOnlyList<NavObstacle> MovementBlockers => Solids;
        private static NavClearanceProfile Clearance(double radius) => new NavClearanceProfile("terrain-query", 1, "ground", 1, radius);
        public bool TryLocate(NavPoint point, double radius, out NavLocation location) => Provider.TryLocate(point, Clearance(radius), out location);
        public bool IsValidLocation(NavLocation location, double radius) => Provider.IsValid(location, Clearance(radius));
        public bool TryTraverse(NavLocation from, NavPoint to, double radius, out NavLocation location)
        { location = default; if (from.SurfaceId == null) return false; var target = new NavLocation(to, from.SurfaceId); if (!Provider.CanSweep(from, target, Clearance(radius))) return false; location = target; return true; }
        public bool CompatibleCombatSurface(NavLocation anchor, NavLocation other) => Provider.CompatibleCombatSurface(anchor, other);
        // Refuse untyped ambiguous geometry instead of returning the first floor.
        public MapSupport SupportAt(NavPoint point) { if (!TryLocate(point, 0, out var location)) return null; return Supports.Single(s => s.SurfaceId == location.SurfaceId); }
        public double SurfaceHeight(NavPoint point) => SupportAt(point)?.Height ?? throw new ArgumentException("Explicit surface required.");
        public NavPoint SurfaceGradient(NavPoint point) { SurfaceHeight(point); return default; }
        public bool SupportsFootprint(NavPoint point, double radius) => TryLocate(point, radius, out _);
        public bool SupportsSweep(NavPoint from, NavPoint to, double radius) => TryLocate(from, radius, out var location) && TryTraverse(location, to, radius, out _);
        public TerritorySite[] Sites(PlayableProfile profile) => Array.Empty<TerritorySite>();
        public NavPoint Headquarters(PlayableOwner owner) => throw new InvalidOperationException("Qualification terrain is not a match roster map.");
        public static NavTraversalArea Area(double minX, double minZ, double maxX, double maxZ, params NavObstacle[] blockers)
            => new NavTraversalArea(new NavObstacle(minX, minZ, maxX, maxZ).Footprint, 8, blockers);
    }
}

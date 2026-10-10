using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Adapter over the actual authored precedence partition. Callers supply the
    // current movement geometry, including buildings and HOLD obstacles. An
    // arbitrary legacy terrain is intentionally not accepted as a layered map.
    public sealed class PartitionNavigationProvider : ILayeredNavigationProvider
    {
        private readonly TerrainLocations locations;
        private readonly Dictionary<string, NavLayerSurface> bySurface;

        public PartitionNavigationProvider(IPlayableTerrain terrain, NavGeometry movementGeometry, string holdBinding)
        {
            if (!(terrain is ThreeCrossingsMap) && !(terrain is FoundryMap))
                throw new ArgumentException("No authored partition adapter for this terrain.", nameof(terrain));
            if (movementGeometry == null || movementGeometry.HalfExtent != terrain.HalfExtent)
                throw new ArgumentException("Movement geometry does not match terrain extent.", nameof(movementGeometry));
            if (holdBinding != null && holdBinding.Length > 256) throw new ArgumentException("Invalid HOLD identity.", nameof(holdBinding));
            if (movementGeometry.Obstacles.Count > 2048 || movementGeometry.Obstacles.Any(o =>
                o.Polygon != null && o.Polygon.Vertices.Count > 256))
                throw new ArgumentException("Partition movement geometry capacity exceeded.", nameof(movementGeometry));
            locations = new TerrainLocations(movementGeometry, terrain.Supports, terrain.SurfaceTransitions);
            HalfExtent = terrain.HalfExtent;
            Seams = locations.Seams;
            Surfaces = Array.AsReadOnly(terrain.Supports.Select(s => new NavLayerSurface(
                s.SurfaceId, "ground", s.Height, 1,
                new NavTraversalArea(s.Bounds.Footprint, terrain.HalfExtent, Array.Empty<NavObstacle>()))).ToArray());
            bySurface = Surfaces.ToDictionary(s => s.Id, StringComparer.Ordinal);
            Portals = Array.AsReadOnly(Array.Empty<NavLayerPortal>());
            Binding = NavContract.Digest(w => {
                w.Write("authored-partition-provider-v1"); w.Write(terrain.Id); w.Write(terrain.Revision);
                w.Write(terrain.SurfaceSemanticsVersion); w.Write(terrain.HalfExtent);
                w.Write(holdBinding != null); if (holdBinding != null) w.Write(holdBinding);
                w.Write(terrain.Supports.Count);
                foreach (var s in terrain.Supports) {
                    w.Write(s.SurfaceId); w.Write(s.Id); NavContract.Polygon(w, s.Bounds.Footprint);
                    w.Write(s.Height); w.Write(s.Gradient.X); w.Write(s.Gradient.Z);
                    w.Write(s.Origin.X); w.Write(s.Origin.Z); w.Write(s.IsBridge);
                }
                w.Write(terrain.SurfaceTransitions.Count);
                foreach (var t in terrain.SurfaceTransitions) { w.Write(t.Id); w.Write(t.From); w.Write(t.To); }
                w.Write(movementGeometry.Revision); w.Write(movementGeometry.Obstacles.Count);
                foreach (var b in movementGeometry.Obstacles) {
                    w.Write(b.MinX); w.Write(b.MinZ); w.Write(b.MaxX); w.Write(b.MaxZ);
                    w.Write(b.CircleRadius); w.Write(b.Bottom); w.Write(b.Top);
                    w.Write(b.Polygon != null); if (b.Polygon != null) NavContract.Polygon(w, b.Polygon);
                }
                w.Write(Seams.Count);
                foreach (var s in Seams) {
                    w.Write(s.FromSurface); w.Write(s.ToSurface);
                    NavContract.Point(w, s.From); NavContract.Point(w, s.To); w.Write(s.Allowed);
                }
            });
            // Account once for the geometry retained by TerrainLocations, the
            // support boundaries/arrangement and both seam views. A polygon
            // retains vertices, triangulation and wrappers, not just one slot.
            long supportVertices = terrain.Supports.Sum(s => (long)(s.Bounds.Polygon?.Vertices.Count ?? 4));
            long polygonVertices = movementGeometry.Obstacles.Sum(o => (long)(o.Polygon?.Vertices.Count ?? 0));
            EstimatedRetainedBytes = checked(4096L + 1024L * Surfaces.Count +
                512L * supportVertices + 512L * Seams.Count +
                256L * terrain.SurfaceTransitions.Count +
                512L * movementGeometry.Obstacles.Count + 128L * polygonVertices);
        }

        public string Binding { get; }
        public double HalfExtent { get; }
        public long EstimatedRetainedBytes { get; }
        public IReadOnlyList<NavLayerSurface> Surfaces { get; }
        public IReadOnlyList<NavLayerPortal> Portals { get; }
        public IReadOnlyList<NavPartitionSeam> Seams { get; }
        public double SurfaceCost(string surfaceId) => bySurface.TryGetValue(surfaceId, out var surface)
            ? surface.DistanceCost : throw new ArgumentException("Unknown partition surface.", nameof(surfaceId));
        public bool TryLocate(NavPoint point, NavClearanceProfile clearance, out NavLocation location)
            => locations.TryLocate(point, clearance.Radius, out location);
        public bool IsValid(NavLocation location, NavClearanceProfile clearance)
            => locations.Valid(location, clearance.Radius);
        public bool CanSweep(NavLocation from, NavLocation to, NavClearanceProfile clearance)
            => from.SurfaceId == to.SurfaceId &&
               locations.TryTrace(from, to.Position, clearance.Radius, out var reached, out var trace) &&
               reached.Equals(to) && trace.All(l => l.Kind == NavTypedLegKind.Surface);
        public bool TryTraverse(NavLocation from, NavPoint to, NavClearanceProfile clearance, out NavLocation reached)
            => locations.Traverse(from, to, clearance.Radius, out reached);
        public bool TryTrace(NavLocation from, NavPoint to, NavClearanceProfile clearance,
            out NavLocation reached, out IReadOnlyList<NavTypedLeg> trace)
            => locations.TryTrace(from, to, clearance.Radius, out reached, out trace);
        public NavConnectorResult QualifyPortal(string portalId, NavClearanceProfile clearance)
            => new NavConnectorResult(NavConnectorStatus.UnknownPortal, Binding, portalId, default, default, default, default, 0);
        public NavConnectorResult Connect(string portalId, NavLocation origin, NavLocation endpoint, NavClearanceProfile clearance)
            => new NavConnectorResult(NavConnectorStatus.UnknownPortal, Binding, portalId, origin, default, default, endpoint, 0);
    }
}

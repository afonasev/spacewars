using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spacewars.Simulation
{
    // Opt-in companion contract. Existing IPlayableTerrain callers keep their
    // authored precedence partition; ambiguous XZ never chooses a first floor.
    public interface ILayeredPlayableTerrain : IPlayableTerrain
    {
        ILayeredNavigationProvider LayeredNavigation { get; }
    }

    public interface ILayeredNavigationProvider
    {
        string Binding { get; }
        long EstimatedRetainedBytes { get; }
        double SurfaceCost(string surfaceId);
        IReadOnlyList<NavLayerSurface> Surfaces { get; }
        IReadOnlyList<NavLayerPortal> Portals { get; }
        bool IsValid(NavLocation location, NavClearanceProfile clearance);
        bool CanSweep(NavLocation from, NavLocation to, NavClearanceProfile clearance);
        NavConnectorResult QualifyPortal(string portalId, NavClearanceProfile clearance);
        NavConnectorResult Connect(string portalId, NavLocation origin, NavLocation endpoint, NavClearanceProfile clearance);
    }

    // A closed walkable polygon plus exact obstacles. Sweeps use the whole
    // footprint, including concave boundaries; endpoint containment is insufficient.
    public sealed class NavTraversalArea
    {
        private readonly NavGeometry geometry;
        public NavTraversalArea(NavPolygon boundary, double halfExtent, IEnumerable<NavObstacle> blockers)
        {
            Boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
            // Predicate work has an explicit finite authored-geometry ceiling.
            // The capped enumeration also rejects a nonterminating source.
            if (boundary.Vertices.Count > 256) throw new ArgumentException("Navigation boundary geometry capacity exceeded.");
            var copy = (blockers ?? throw new ArgumentNullException(nameof(blockers))).Take(2049).ToArray();
            if (copy.Length > 2048 || copy.Any(b => b.Polygon != null && b.Polygon.Vertices.Count > 256))
                throw new ArgumentException("Navigation blocker geometry capacity exceeded.");
            if (boundary.Vertices.Any(p => Math.Abs(p.X) > halfExtent || Math.Abs(p.Z) > halfExtent)) throw new ArgumentException("Area outside geometry extent.");
            foreach (var b in copy) {
                if (double.IsNaN(b.MinX) || double.IsInfinity(b.MinX) || double.IsNaN(b.MaxX) || double.IsInfinity(b.MaxX) || double.IsNaN(b.MinZ) || double.IsInfinity(b.MinZ) || double.IsNaN(b.MaxZ) || double.IsInfinity(b.MaxZ) || b.MinX > b.MaxX || b.MinZ > b.MaxZ) throw new ArgumentException("Malformed blocker.");
            }
            geometry = new NavGeometry(halfExtent, copy, 1); Blockers = geometry.Obstacles; HalfExtent = halfExtent;
            long estimate = 2048 + 128L * boundary.Vertices.Count;
            foreach (var blocker in copy) estimate += 512 + 128L * (blocker.Polygon?.Vertices.Count ?? 4);
            EstimatedRetainedBytes = estimate;
        }
        public NavPolygon Boundary { get; } public double HalfExtent { get; } public IReadOnlyList<NavObstacle> Blockers { get; }
        internal long EstimatedRetainedBytes { get; }
        public bool Contains(NavPoint point, double radius) => Boundary.Contains(point) && geometry.IsFree(point, radius) && Boundary.BoundaryDistanceSquared(point) >= radius * radius;
        public bool Sweep(NavPoint from, NavPoint to, double radius)
        {
            if (!Contains(from, radius) || !Contains(to, radius) || !geometry.SegmentFree(from, to, radius)) return false;
            if (from.Equals(to)) return true;
            var v = Boundary.Vertices;
            for (int i = 0; i < v.Count; i++) {
                double distance = SegmentDistance2(from, to, v[i], v[(i + 1) % v.Count]);
                // Zero-radius segments still cannot leave/reenter a concave area.
                if (distance <= 1e-18 || distance < radius * radius) return false;
            }
            return true;
        }
        internal void Write(BinaryWriter w)
        {
            NavContract.Polygon(w, Boundary); w.Write(HalfExtent); w.Write(Blockers.Count);
            foreach (var b in Blockers) {
                w.Write(b.MinX); w.Write(b.MinZ); w.Write(b.MaxX); w.Write(b.MaxZ); w.Write(b.CircleRadius); w.Write(b.Bottom); w.Write(b.Top);
                w.Write(b.Polygon != null); if (b.Polygon != null) NavContract.Polygon(w, b.Polygon);
            }
        }
        private static double Distance2(NavPoint p, NavPoint a, NavPoint b)
        {
            double x = b.X - a.X, z = b.Z - a.Z, length = x * x + z * z;
            double t = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * x + (p.Z - a.Z) * z) / length));
            x = p.X - a.X - t * x; z = p.Z - a.Z - t * z; return x * x + z * z;
        }
        private static double Cross(NavPoint a, NavPoint b, NavPoint p) => (b.X - a.X) * (p.Z - a.Z) - (b.Z - a.Z) * (p.X - a.X);
        private static bool On(NavPoint a, NavPoint b, NavPoint p) => p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Z >= Math.Min(a.Z, b.Z) && p.Z <= Math.Max(a.Z, b.Z);
        private static double SegmentDistance2(NavPoint a, NavPoint b, NavPoint c, NavPoint d)
        {
            double x = Cross(a, b, c), y = Cross(a, b, d), z = Cross(c, d, a), w = Cross(c, d, b);
            if (x * y < 0 && z * w < 0 || x == 0 && On(a, b, c) || y == 0 && On(a, b, d) || z == 0 && On(c, d, a) || w == 0 && On(c, d, b)) return 0;
            return Math.Min(Math.Min(Distance2(a, c, d), Distance2(b, c, d)), Math.Min(Distance2(c, a, b), Distance2(d, a, b)));
        }
    }

    public sealed class NavLayerSurface
    {
        public NavLayerSurface(string id, string combatFamily, double height, double distanceCost, NavTraversalArea area)
        {
            Id = NavContract.Text(id); CombatFamily = NavContract.Text(combatFamily);
            if (double.IsNaN(height) || double.IsInfinity(height)) throw new ArgumentException("Invalid layer height.");
            Height = height; DistanceCost = NavContract.Number(distanceCost, true); Area = area ?? throw new ArgumentNullException(nameof(area));
        }
        public string Id { get; } public string CombatFamily { get; } public double Height { get; } public double DistanceCost { get; } public NavTraversalArea Area { get; }
        internal void Write(BinaryWriter w) { w.Write(Id); w.Write(CombatFamily); w.Write(Height); w.Write(DistanceCost); Area.Write(w); }
    }

    public enum NavPortalDirection { Forward, Both }
    public sealed class NavLayerPortal
    {
        public NavLayerPortal(string id, NavLocation entry, NavLocation exit, NavPortalDirection direction, bool open,
            double traversalCost, NavTraversalArea corridor, IEnumerable<string> mobilityClasses)
        {
            Id = NavContract.Text(id); NavContract.Text(entry.SurfaceId); NavContract.Text(exit.SurfaceId);
            if (entry.SurfaceId == exit.SurfaceId || !Enum.IsDefined(typeof(NavPortalDirection), direction)) throw new ArgumentException("Invalid portal direction/layers.");
            Entry = entry; Exit = exit; Direction = direction; Open = open; TraversalCost = NavContract.Number(traversalCost, true);
            Corridor = corridor ?? throw new ArgumentNullException(nameof(corridor));
            var copy = (mobilityClasses ?? throw new ArgumentNullException(nameof(mobilityClasses))).Select(NavContract.Text).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            if (copy.Length == 0 || copy.Distinct(StringComparer.Ordinal).Count() != copy.Length) throw new ArgumentException("Invalid portal mobility metadata.");
            MobilityClasses = Array.AsReadOnly(copy);
        }
        public string Id { get; } public NavLocation Entry { get; } public NavLocation Exit { get; } public NavPortalDirection Direction { get; } public bool Open { get; }
        public double TraversalCost { get; } public NavTraversalArea Corridor { get; } public IReadOnlyList<string> MobilityClasses { get; }
        internal void Write(BinaryWriter w)
        { w.Write(Id); NavContract.Location(w, Entry); NavContract.Location(w, Exit); w.Write((int)Direction); w.Write(Open); w.Write(TraversalCost); Corridor.Write(w); w.Write(MobilityClasses.Count); foreach (var c in MobilityClasses) w.Write(c); }
    }

    public enum NavConnectorStatus { Ready, Blocked, Closed, WrongDirection, InvalidLocation, UnknownPortal }
    public sealed class NavConnectorResult
    {
        internal NavConnectorResult(NavConnectorStatus status, string binding, string portalId, NavLocation origin, NavLocation entry, NavLocation exit, NavLocation endpoint, double cost)
        { Status = status; ProviderBinding = binding; PortalId = portalId; Origin = origin; Entry = entry; Exit = exit; Endpoint = endpoint; Cost = cost; }
        public NavConnectorStatus Status { get; } public string ProviderBinding { get; } public string PortalId { get; }
        public NavLocation Origin { get; } public NavLocation Entry { get; } public NavLocation Exit { get; } public NavLocation Endpoint { get; } public double Cost { get; }
    }

    public sealed class LayeredNavigationProvider : ILayeredNavigationProvider
    {
        private readonly Dictionary<string, NavLayerSurface> surfaces;
        private readonly Dictionary<string, NavLayerPortal> portals;
        public LayeredNavigationProvider(string id, int revision, int semanticsVersion, int topologyRevision, string exactMapBinding,
            IEnumerable<NavLayerSurface> authoredSurfaces, IEnumerable<NavLayerPortal> authoredPortals)
        {
            Id = NavContract.Text(id); Revision = NavContract.Revision(revision); SemanticsVersion = NavContract.Revision(semanticsVersion); TopologyRevision = NavContract.Revision(topologyRevision); ExactMapBinding = NavContract.Sha256(exactMapBinding);
            var layers = (authoredSurfaces ?? throw new ArgumentNullException(nameof(authoredSurfaces))).Take(129).ToArray();
            var links = (authoredPortals ?? throw new ArgumentNullException(nameof(authoredPortals))).Take(129).ToArray();
            if (layers.Length > 128 || links.Length > 128) throw new ArgumentException("Navigation provider geometry capacity exceeded.");
            if (layers.Length == 0 || layers.Any(s => s == null) || links.Any(p => p == null) || layers.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != layers.Length || links.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != links.Length) throw new ArgumentException("Duplicate/missing provider declarations.");
            layers = layers.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray(); links = links.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
            surfaces = layers.ToDictionary(s => s.Id, StringComparer.Ordinal); portals = links.ToDictionary(p => p.Id, StringComparer.Ordinal);
            foreach (var portal in links) {
                if (!surfaces.ContainsKey(portal.Entry.SurfaceId) || !surfaces.ContainsKey(portal.Exit.SurfaceId) ||
                    !surfaces[portal.Entry.SurfaceId].Area.Contains(portal.Entry.Position, 0) || !surfaces[portal.Exit.SurfaceId].Area.Contains(portal.Exit.Position, 0) ||
                    !portal.Corridor.Sweep(portal.Entry.Position, portal.Exit.Position, 0)) throw new ArgumentException("Malformed portal geometry or surface metadata.");
            }
            Surfaces = Array.AsReadOnly(layers); Portals = Array.AsReadOnly(links);
            long estimate = 4096;
            foreach (var layer in layers) estimate += 1024 + layer.Area.EstimatedRetainedBytes;
            foreach (var link in links) estimate += 2048 + 512L * link.MobilityClasses.Count + link.Corridor.EstimatedRetainedBytes;
            EstimatedRetainedBytes = estimate;
            Binding = NavContract.Digest(w => {
                w.Write("layered-provider-v1"); w.Write(Id); w.Write(Revision); w.Write(SemanticsVersion); w.Write(TopologyRevision); w.Write(ExactMapBinding);
                w.Write(layers.Length); foreach (var s in layers) s.Write(w); w.Write(links.Length); foreach (var p in links) p.Write(w);
            });
        }
        public string Id { get; } public int Revision { get; } public int SemanticsVersion { get; } public int TopologyRevision { get; } public string ExactMapBinding { get; } public string Binding { get; }
        public long EstimatedRetainedBytes { get; }
        public IReadOnlyList<NavLayerSurface> Surfaces { get; } public IReadOnlyList<NavLayerPortal> Portals { get; }
        public double SurfaceCost(string surfaceId) => surfaces[surfaceId].DistanceCost;
        public bool IsValid(NavLocation location, NavClearanceProfile clearance) => clearance != null && location.SurfaceId != null && surfaces.TryGetValue(location.SurfaceId, out var s) && s.Area.Contains(location.Position, clearance.Radius);
        public bool CanSweep(NavLocation from, NavLocation to, NavClearanceProfile clearance) => from.SurfaceId == to.SurfaceId && IsValid(from, clearance) && IsValid(to, clearance) && surfaces[from.SurfaceId].Area.Sweep(from.Position, to.Position, clearance.Radius);
        public bool TryLocate(NavPoint point, NavClearanceProfile clearance, out NavLocation location)
        {
            location = default; if (clearance == null) return false;
            var matches = Surfaces.Where(s => s.Area.Contains(point, clearance.Radius)).ToArray();
            if (matches.Length != 1) return false; location = new NavLocation(point, matches[0].Id); return true;
        }
        public bool CompatibleCombatSurface(NavLocation a, NavLocation b) => a.SurfaceId != null && b.SurfaceId != null && surfaces.TryGetValue(a.SurfaceId, out var aa) && surfaces.TryGetValue(b.SurfaceId, out var bb) && aa.Area.Contains(a.Position, 0) && bb.Area.Contains(b.Position, 0) && aa.CombatFamily == bb.CombatFamily;
        public NavConnectorResult QualifyPortal(string portalId, NavClearanceProfile clearance)
        {
            if (portalId == null || !portals.TryGetValue(portalId, out var p)) return new NavConnectorResult(NavConnectorStatus.UnknownPortal, Binding, portalId, default, default, default, default, 0);
            var status = !p.Open ? NavConnectorStatus.Closed : clearance == null || !p.MobilityClasses.Contains(clearance.MobilityClass) ||
                !IsValid(p.Entry, clearance) || !IsValid(p.Exit, clearance) || !p.Corridor.Sweep(p.Entry.Position, p.Exit.Position, clearance.Radius) ? NavConnectorStatus.Blocked : NavConnectorStatus.Ready;
            return new NavConnectorResult(status, Binding, portalId, p.Entry, p.Entry, p.Exit, p.Exit, status == NavConnectorStatus.Ready ? p.TraversalCost : 0);
        }
        public NavConnectorResult Connect(string portalId, NavLocation origin, NavLocation endpoint, NavClearanceProfile clearance)
        {
            NavLocation entry = default, exit = default;
            NavConnectorResult Result(NavConnectorStatus status, double cost = 0) => new NavConnectorResult(status, Binding, portalId, origin, entry, exit, endpoint, cost);
            if (portalId == null || !portals.TryGetValue(portalId, out var p)) return Result(NavConnectorStatus.UnknownPortal);
            bool forward = origin.SurfaceId == p.Entry.SurfaceId && endpoint.SurfaceId == p.Exit.SurfaceId;
            bool reverse = origin.SurfaceId == p.Exit.SurfaceId && endpoint.SurfaceId == p.Entry.SurfaceId;
            if (!forward && !reverse || !IsValid(origin, clearance) || !IsValid(endpoint, clearance)) return Result(NavConnectorStatus.InvalidLocation);
            entry = forward ? p.Entry : p.Exit; exit = forward ? p.Exit : p.Entry;
            if (reverse && p.Direction == NavPortalDirection.Forward) return Result(NavConnectorStatus.WrongDirection);
            if (!p.Open) return Result(NavConnectorStatus.Closed);
            if (!p.MobilityClasses.Contains(clearance.MobilityClass) || !CanSweep(origin, entry, clearance) || !CanSweep(exit, endpoint, clearance) || !p.Corridor.Sweep(entry.Position, exit.Position, clearance.Radius)) return Result(NavConnectorStatus.Blocked);
            double cost = Distance(origin.Position, entry.Position) * surfaces[entry.SurfaceId].DistanceCost + p.TraversalCost + Distance(exit.Position, endpoint.Position) * surfaces[exit.SurfaceId].DistanceCost;
            if (double.IsInfinity(cost)) return Result(NavConnectorStatus.Blocked);
            return Result(NavConnectorStatus.Ready, cost);
        }
        internal static double Distance(NavPoint a, NavPoint b) { double x = a.X - b.X, z = a.Z - b.Z; return Math.Sqrt(x * x + z * z); }
    }
}

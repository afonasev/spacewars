using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Spacewars.Simulation
{
    // Adapter over the actual authored precedence partition. Callers supply the
    // current movement geometry, including buildings and HOLD obstacles. An
    // arbitrary legacy terrain is intentionally not accepted as a layered map.
    public sealed partial class PartitionNavigationProvider : ILayeredNavigationProvider, INavStorageFacade
    {
        private readonly TerrainLocations locations;
        private readonly string terrainId,holdBinding;
        private readonly int terrainRevision,surfaceSemanticsVersion;
        internal IReadOnlyList<NavObstacle> ExclusionObstacles=>locations.Geometry.Obstacles;
        internal string TerrainId=>terrainId; internal string HoldBinding=>holdBinding;
        internal int TerrainRevision=>terrainRevision; internal int SurfaceSemanticsVersion=>surfaceSemanticsVersion;
        internal NavLocation OwnedCheckpointLocation(NavLocation location)=>locations.OwnedCheckpointLocation(location);
        private IReadOnlyDictionary<string, NavLayerSurface> bySurface;

        // Admission and host construction must agree on the same immutable map set.
        public static bool SupportsTerrain(IPlayableTerrain terrain)
            =>terrain is ThreeCrossingsMap||terrain is FoundryMap||terrain is BasaltCrownMap||terrain is MossHighlandMap||terrain is CampaignOpeningMap||terrain is WhiteMeridianMap||terrain is AiTestMap||terrain is ProvingGroundMap||terrain is CrimsonGroveMap;

        public PartitionNavigationProvider(IPlayableTerrain terrain, NavGeometry movementGeometry, string holdBinding)
        {
            if (!SupportsTerrain(terrain))
                throw new ArgumentException("No authored partition adapter for this terrain.", nameof(terrain));
            if (movementGeometry == null || movementGeometry.HalfExtent != terrain.HalfExtent)
                throw new ArgumentException("Movement geometry does not match terrain extent.", nameof(movementGeometry));
            if (holdBinding != null && holdBinding.Length > 256) throw new ArgumentException("Invalid HOLD identity.", nameof(holdBinding));
            if (movementGeometry.Obstacles.Count > 2048 || movementGeometry.Obstacles.Any(o =>
                o.Polygon != null && o.Polygon.Vertices.Count > 256))
                throw new ArgumentException("Partition movement geometry capacity exceeded.", nameof(movementGeometry));
            locations = terrain is INavArrivalPointTerrain authored
                ?new TerrainLocations(movementGeometry,authored.ArrivalLocations)
                :new TerrainLocations(movementGeometry, terrain.Supports, terrain.SurfaceTransitions);
            HalfExtent = terrain.HalfExtent;
            Seams = locations.Seams;
            Surfaces = Array.AsReadOnly(terrain.Supports.Select(s => new NavLayerSurface(
                s.SurfaceId, "ground", s.Height, 1,
                new NavTraversalArea(s.Bounds.Footprint, terrain.HalfExtent, Array.Empty<NavObstacle>()))).ToArray());
            bySurface = Surfaces.ToDictionary(s => s.Id, StringComparer.Ordinal);
            Portals = Array.AsReadOnly(Array.Empty<NavLayerPortal>());
            terrainId=terrain.Id;terrainRevision=terrain.Revision;surfaceSemanticsVersion=terrain.SurfaceSemanticsVersion;this.holdBinding=holdBinding;
            Binding = NavContract.Digest(WriteBindingTranscript);
            // Account once for the geometry retained by TerrainLocations, the
            // support boundaries/arrangement and both seam views. A polygon
            // retains vertices, triangulation and wrappers, not just one slot.
            long supportVertices = terrain.Supports.Sum(s => (long)(s.Bounds.Polygon?.Vertices.Count ?? 4));
            long polygonVertices = movementGeometry.Obstacles.Sum(o => (long)(o.Polygon?.Vertices.Count ?? 0));
            EstimatedRetainedBytes = checked(RetainedPayloadEstimate(Surfaces.Count,supportVertices,Seams.Count,
                terrain.SurfaceTransitions.Count,movementGeometry.Obstacles.Count,polygonVertices) +
                movementGeometry.SpatialIndexRetainedBytes + locations.FootprintCacheRetainedBytes + locations.RegionIndexRetainedBytes);
        }

        internal static long RetainedPayloadEstimate(long surfaces,long supportVertices,long seams,long transitions,long obstacles,long polygonVertices)
            =>checked(4096L+1024L*surfaces+512L*supportVertices+512L*seams+256L*transitions+512L*obstacles+128L*polygonVertices);

        // A single unchanged transcript serves construction, owned-copy validation
        // and physical restore. It reads the retained payloads, never a terrain catalog.
        internal void WriteBindingTranscript(BinaryWriter w)
        {
            w.Write("authored-partition-provider-v1");w.Write(terrainId);w.Write(terrainRevision);
            w.Write(surfaceSemanticsVersion);w.Write(HalfExtent);w.Write(holdBinding!=null);if(holdBinding!=null)w.Write(holdBinding);
            w.Write(locations.Supports.Count);
            foreach(var s in locations.Supports){
                w.Write(s.SurfaceId);w.Write(s.Id);NavContract.Polygon(w,s.Bounds.Footprint);
                w.Write(s.Height);w.Write(s.Gradient.X);w.Write(s.Gradient.Z);w.Write(s.Origin.X);w.Write(s.Origin.Z);w.Write(s.IsBridge);
            }
            w.Write(locations.Transitions.Count);
            foreach(var t in locations.Transitions){w.Write(t.Id);w.Write(t.From);w.Write(t.To);}
            var movementGeometry=locations.Geometry;w.Write(movementGeometry.Revision);w.Write(movementGeometry.Obstacles.Count);
            foreach(var b in movementGeometry.Obstacles){
                w.Write(b.MinX);w.Write(b.MinZ);w.Write(b.MaxX);w.Write(b.MaxZ);w.Write(b.CircleRadius);w.Write(b.Bottom);w.Write(b.Top);
                w.Write(b.Polygon!=null);if(b.Polygon!=null)NavContract.Polygon(w,b.Polygon);
            }
            w.Write(Seams.Count);foreach(var s in Seams){w.Write(s.FromSurface);w.Write(s.ToSurface);NavContract.Point(w,s.From);NavContract.Point(w,s.To);w.Write(s.Allowed);}
        }

        public string Binding { get; }
        public double HalfExtent { get; }
        public long EstimatedRetainedBytes { get; }
        internal bool SameArrangement(PartitionNavigationProvider other)=>other!=null&&locations.SameArrangement(other.locations);
        internal int TracePortalBound=>locations.TracePortalBound;
        internal long TraceScratchBound=>locations.TraceScratchBound;
        public IReadOnlyList<NavLayerSurface> Surfaces { get; private set; }
        public IReadOnlyList<NavLayerPortal> Portals { get; private set; }
        public IReadOnlyList<NavPartitionSeam> Seams { get; private set; }
        public double SurfaceCost(string surfaceId) => bySurface.TryGetValue(surfaceId, out var surface)
            ? surface.DistanceCost : throw new ArgumentException("Unknown partition surface.", nameof(surfaceId));
        public bool TryLocate(NavPoint point, NavClearanceProfile clearance, out NavLocation location)
            => locations.TryLocate(point, clearance.Radius, out location);
        public bool IsValid(NavLocation location, NavClearanceProfile clearance)
            => locations.Valid(location, clearance.Radius);
        public bool CanSweep(NavLocation from, NavLocation to, NavClearanceProfile clearance)
        {using var measured=PerformanceProfile.Sample(ProfileStage.GeometrySweep);
            return from.SurfaceId == to.SurfaceId &&
               locations.TryGraphTrace(from, to.Position, clearance.Radius, out var reached, out var trace) &&
               reached.Equals(to) && trace.All(l => l.Kind == NavTypedLegKind.Surface);}

        internal bool CanSweep(NavLocation from,NavLocation to,NavClearanceProfile clearance,out int blocker)
        {
            using var measured=PerformanceProfile.Sample(ProfileStage.GeometrySweep);
            blocker=-1;return from.SurfaceId==to.SurfaceId&&locations.TryGraphTrace(from,to.Position,clearance.Radius,out var reached,out var trace,out blocker)&&reached.Equals(to)&&trace.All(l=>l.Kind==NavTypedLegKind.Surface);
        }
        internal bool RejectTerminalRectangle(NavPoint from,NavHeuristicBox candidates,double radius,int blocker,bool corners=false)
            =>blocker>=0&&blocker<ExclusionObstacles.Count&&NavTerminalRectangleProof.Reject(from,candidates,radius,ExclusionObstacles[blocker],corners);
        public bool TryTraverse(NavLocation from, NavPoint to, NavClearanceProfile clearance, out NavLocation reached)
            => locations.Traverse(from, to, clearance.Radius, out reached);
        public bool TryTrace(NavLocation from, NavPoint to, NavClearanceProfile clearance,
            out NavLocation reached, out IReadOnlyList<NavTypedLeg> trace)
            => locations.TryTrace(from, to, clearance.Radius, out reached, out trace);
        internal bool TryTraceOwned(NavLocation from,NavPoint to,NavClearanceProfile clearance,out NavLocation reached,out IReadOnlyList<NavTypedLeg> trace,INavStorageReservation reservation)
        {
            if(StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,StorageRoot);
            return locations.TryTrace(from,to,clearance.Radius,out reached,out trace,reservation,this);
        }
        internal void VerifyPhysicalTrace(NavLocation from,NavLocation to,NavClearanceProfile clearance,IReadOnlyList<NavTypedLeg> trace,INavStorageReservation reservation)
            =>locations.VerifyTrace(from,to,clearance,trace,this,reservation);
        internal bool TryGraphTrace(NavLocation from,NavPoint to,NavClearanceProfile clearance,out NavLocation reached,out IReadOnlyList<NavTypedLeg> trace)
            =>locations.TryGraphTrace(from,to,clearance.Radius,out reached,out trace);
        internal bool TryGraphTraceValidated(NavLocation from,NavPoint to,NavClearanceProfile clearance,out NavLocation reached,out IReadOnlyList<NavTypedLeg> trace)
            =>locations.TryGraphTraceValidated(from,to,clearance.Radius,out reached,out trace);
        internal bool TryGraphTraceValidatedOwned(NavLocation from,NavPoint to,NavClearanceProfile clearance,out NavLocation reached,out IReadOnlyList<NavTypedLeg> trace,INavStorageReservation reservation)
        {using var measured=PerformanceProfile.Sample(ProfileStage.GeometryTrace);if(StorageRoot!=null)NavStorageAdmission.RequireOwned(reservation,StorageRoot);return locations.TryGraphTraceValidated(from,to,clearance.Radius,out reached,out trace,reservation,this);}
        public NavConnectorResult QualifyPortal(string portalId, NavClearanceProfile clearance)
            => new NavConnectorResult(NavConnectorStatus.UnknownPortal, Binding, portalId, default, default, default, default, 0);
        public NavConnectorResult Connect(string portalId, NavLocation origin, NavLocation endpoint, NavClearanceProfile clearance)
            => new NavConnectorResult(NavConnectorStatus.UnknownPortal, Binding, portalId, origin, default, default, endpoint, 0);
    }
}

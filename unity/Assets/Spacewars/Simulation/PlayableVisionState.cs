using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    [Serializable] public sealed class VisionSourceState
    {
        public double X, Z, Radius;
    }
    [Serializable] public sealed class KnownBuildingState
    {
        public int Id, Team;
        public PlayableOwner Owner;
        public PlayableBuildingKind Kind;
        public double X, Z, Heading;
        public bool RefineryUpgraded;
    }
    // A subsystem checkpoint, not a complete world/archive. The enclosing world state
    // must bind profile/map/generation/tick and coordinate the authoritative sources.
    [Serializable] public sealed class PlayableVisionState
    {
        public int Version, Team, RasterResolution;
        public long Revision, CoverageUpdates;
        public double HalfWidth, HalfDepth, CellSize, Feather;
        public VisionSourceState[] Sources;
        public long[] DiscoveredCells;
        public byte[] Coverage;
        public KnownBuildingState[] KnownBuildings;
    }

    public sealed partial class PlayableVision
    {
        // Technical format identity, never a balance setting. Unsupported formats fail closed.
        public const int StateVersion = 1;

        public PlayableVisionState CaptureState() => new PlayableVisionState
        {
            Version = StateVersion, Team = team, RasterResolution = RasterResolution,
            Revision = revision, CoverageUpdates = CoverageUpdates,
            HalfWidth = halfWidth, HalfDepth = halfDepth, CellSize = cellSize, Feather = feather,
            Sources = sources.Select(s => new VisionSourceState { X = s.Position.X, Z = s.Position.Z, Radius = s.Radius }).ToArray(),
            DiscoveredCells = discovered.OrderBy(key => key).ToArray(), Coverage = (byte[])coverage.Clone(),
            KnownBuildings = known.Values.OrderBy(b => b.Id).Select(b => new KnownBuildingState
            {
                Id = b.Id, Team = b.Team, Owner = b.Owner, Kind = b.Kind,
                X = b.Position.X, Z = b.Position.Z, Heading = b.Heading, RefineryUpgraded = b.RefineryUpgraded
            }).ToArray()
        };

        // Owner thread only, at a quiescent boundary. Build and validate all replacement
        // state first so a rejected checkpoint leaves the destination reusable.
        public void RestoreState(PlayableVisionState state)
        {
            if (state == null || state.Version != StateVersion || state.Team != team ||
                state.RasterResolution != RasterResolution || state.HalfWidth != halfWidth || state.HalfDepth != halfDepth ||
                state.CellSize != cellSize || state.Feather != feather || state.Revision < 0 || state.CoverageUpdates < 0 ||
                state.Revision < state.CoverageUpdates || state.Sources == null || state.DiscoveredCells == null ||
                state.Coverage == null || state.Coverage.Length != coverage.Length || state.KnownBuildings == null ||
                revision != 0 || CoverageUpdates != 0 || sources.Length != 0 || discovered.Count != 0 || known.Count != 0)
                throw new ArgumentException("Vision checkpoint or fresh destination mismatch.", nameof(state));

            var nextSources = new List<VisionSource>();
            foreach (var row in state.Sources)
            {
                if (row == null) throw new ArgumentException("Null vision source.", nameof(state));
                nextSources.Add(new VisionSource(new NavPoint(row.X, row.Z), row.Radius));
            }
            var nextDiscovered = new HashSet<long>();
            foreach (long key in state.DiscoveredCells)
            {
                int x = (int)(key >> 32), z = unchecked((int)key);
                if (x < Math.Floor(-halfWidth / cellSize) || x >= Math.Ceiling(halfWidth / cellSize) ||
                    z < Math.Floor(-halfDepth / cellSize) || z >= Math.Ceiling(halfDepth / cellSize) || !nextDiscovered.Add(key))
                    throw new ArgumentException("Invalid discovered cell.", nameof(state));
            }
            var nextKnown = new Dictionary<int, KnownBuilding>();
            foreach (var row in state.KnownBuildings)
            {
                if (row == null || row.Id < 1 || row.Team == team || !Enum.IsDefined(typeof(PlayableOwner), row.Owner) ||
                    !Enum.IsDefined(typeof(PlayableBuildingKind), row.Kind) || !VisionSource.Finite(row.X) ||
                    !VisionSource.Finite(row.Z) || !VisionSource.Finite(row.Heading) || nextKnown.ContainsKey(row.Id))
                    throw new ArgumentException("Invalid known building.", nameof(state));
                nextKnown.Add(row.Id, new KnownBuilding(row.Id, row.Team, row.Owner, row.Kind, new NavPoint(row.X, row.Z), row.Heading, row.RefineryUpgraded));
            }
            var orderedSources = nextSources.OrderBy(s => s.Position.X).ThenBy(s => s.Position.Z).ThenBy(s => s.Radius).ToArray();
            var nextCoverage = (byte[])state.Coverage.Clone();

            sources = orderedSources;
            foreach (long key in nextDiscovered) discovered.Add(key);
            foreach (var pair in nextKnown) known.Add(pair.Key, pair.Value);
            Array.Copy(nextCoverage, coverage, coverage.Length);
            revision = state.Revision; CoverageUpdates = state.CoverageUpdates; cached = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Spacewars.Simulation
{
    // Each call to Step performs at most one sample/predicate, portal qualification,
    // edge qualification, component lookup, or output materialization. Authoring
    // validation and provider digesting precede this derived graph work.
    public sealed class LayeredNavigationCompileWork
    {
        public enum Stage { SurfaceBounds, Samples, Portals, GridEdges, PortalEdges, PortalArcs, Nodes, SortedArcs, Adjacency, WrapAdjacency, Ready }
        private readonly ILayeredNavigationProvider provider;
        private PartitionNavigationCompileWork partition;
        private readonly NavClearanceProfile clearance;
        private readonly NavGraphProfile profile;
        private readonly NavGraphCompileLimits limits;
        private readonly List<NavLocation> samples = new List<NavLocation>();
        private readonly Dictionary<Tuple<string, long, long>, int> grid = new Dictionary<Tuple<string, long, long>, int>();
        private readonly List<Tuple<int, int>> portalEnds = new List<Tuple<int, int>>();
        private readonly List<NavConnectorResult> qualifications = new List<NavConnectorResult>();
        private readonly SortedSet<NavGraphArc> sortedArcs = new SortedSet<NavGraphArc>(new ArcComparer());
        private readonly List<NavGraphArc> arcs = new List<NavGraphArc>();
        private readonly List<NavGraphNode> nodes = new List<NavGraphNode>();
        private readonly Dictionary<int, List<NavGraphArc>> outLists = new Dictionary<int, List<NavGraphArc>>();
        private readonly Dictionary<int, List<NavGraphArc>> inLists = new Dictionary<int, List<NavGraphArc>>();
        private readonly Dictionary<int, IReadOnlyList<NavGraphArc>> outgoing = new Dictionary<int, IReadOnlyList<NavGraphArc>>();
        private readonly Dictionary<int, IReadOnlyList<NavGraphArc>> incoming = new Dictionary<int, IReadOnlyList<NavGraphArc>>();
        private readonly Dictionary<string, double> surfaceCosts = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly List<int> parent = new List<int>();
        private int surface, portal, gridCursor, neighbor, end, portalGridCursor, portalArcCursor, nodeCursor, arcCursor, wrapCursor;
        private int gridCount;
        private long x, z, minX, maxX, minZ, maxZ, sampled;
        public Stage Phase { get; private set; } = Stage.SurfaceBounds;
        public LayeredNavigationGraph Graph { get; private set; }
        public int Consumed { get; private set; }
        public int SampleCount => partition != null ? partition.SampleCount : samples.Count;
        public int ArcCount => partition != null ? partition.ArcCount : sortedArcs.Count + arcs.Count;
        public long EstimatedRetainedBytes => partition != null ? partition.EstimatedRetainedBytes :
            4096L + 64L * limits.MaxNodes + 256L * ArcCount;

        public LayeredNavigationCompileWork(ILayeredNavigationProvider provider, NavClearanceProfile clearance,
            NavGraphProfile profile, NavGraphCompileLimits limits)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.clearance = clearance ?? throw new ArgumentNullException(nameof(clearance));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.limits = limits ?? throw new ArgumentNullException(nameof(limits));
            if (provider is PartitionNavigationProvider actual)
                partition = new PartitionNavigationCompileWork(actual, clearance, profile, limits);
        }

        public int Advance(int quota)
        {
            if (quota < 0) throw new ArgumentOutOfRangeException(nameof(quota));
            if (partition != null) {
                int done = partition.Advance(quota);
                Consumed = partition.Consumed; Graph = partition.Graph;
                Phase = partition.Phase == PartitionNavigationCompileWork.Stage.Ready ? Stage.Ready :
                    partition.Phase == PartitionNavigationCompileWork.Stage.Arcs ? Stage.GridEdges :
                    partition.Phase == PartitionNavigationCompileWork.Stage.Components ? Stage.Nodes : Stage.Samples;
                return done;
            }
            int used = 0;
            while (used < quota && Phase != Stage.Ready) { Step(); used++; Consumed++; }
            return used;
        }
        // Versioned, authority-neutral continuation. The provider/profile are
        // external immutable inputs and must match the exact captured binding.
        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            public string GraphBinding { get; internal set; }
            public Stage Phase { get; internal set; }
            internal NavLocation[] Samples;
            internal int[] Parent;
            internal Tuple<int, int>[] PortalEnds;
            internal NavConnectorResult[] Qualifications;
            internal NavGraphArc[] PendingArcs, MaterializedArcs;
            internal int Surface, Portal, GridCursor, Neighbor, End, PortalGridCursor, PortalArcCursor;
            internal int NodeCursor, ArcCursor, WrapCursor, GridCount, Consumed;
            internal long X, Z, MinX, MaxX, MinZ, MaxZ, Sampled;
            internal PartitionNavigationCompileWork.Checkpoint Partition;
        }
        public Checkpoint Save()
        {
            if (partition != null) return new Checkpoint {
                Version = 1, GraphBinding = new NavGraphBinding(provider.Binding, clearance, profile).StableKey,
                Phase = Phase, Consumed = Consumed, Partition = partition.Save()
            };
            return new Checkpoint {
                Version = 1, GraphBinding = new NavGraphBinding(provider.Binding, clearance, profile).StableKey,
                Phase = Phase, Samples = samples.ToArray(), Parent = parent.ToArray(),
                PortalEnds = portalEnds.ToArray(), Qualifications = qualifications.ToArray(),
                PendingArcs = new List<NavGraphArc>(sortedArcs).ToArray(), MaterializedArcs = arcs.ToArray(),
                Surface = surface, Portal = portal, GridCursor = gridCursor, Neighbor = neighbor,
                End = end, PortalGridCursor = portalGridCursor, PortalArcCursor = portalArcCursor,
                NodeCursor = nodeCursor, ArcCursor = arcCursor, WrapCursor = wrapCursor,
                GridCount = gridCount, Consumed = Consumed, X = x, Z = z, MinX = minX,
                MaxX = maxX, MinZ = minZ, MaxZ = maxZ, Sampled = sampled
            };
        }
        public static LayeredNavigationCompileWork Restore(ILayeredNavigationProvider provider, NavClearanceProfile clearance,
            NavGraphProfile profile, NavGraphCompileLimits limits, Checkpoint state)
        {
            if (state == null || state.Version != 1 || state.GraphBinding != new NavGraphBinding(provider.Binding, clearance, profile).StableKey)
                throw new ArgumentException("Stale graph compilation checkpoint.");
            var work = new LayeredNavigationCompileWork(provider, clearance, profile, limits);
            if (state.Partition != null) {
                if (!(provider is PartitionNavigationProvider actual)) throw new ArgumentException("Partition provider required.");
                work.partition = PartitionNavigationCompileWork.Restore(actual, clearance, profile, limits, state.Partition);
                work.Phase = state.Phase; work.Consumed = state.Consumed; work.Graph = work.partition.Graph;
                return work;
            }
            if (provider is PartitionNavigationProvider) throw new ArgumentException("Partition checkpoint required.");
            if (state.Samples.Length > limits.MaxNodes || state.MaterializedArcs.Length + state.PendingArcs.Length > limits.MaxArcs ||
                state.Parent.Length != state.Samples.Length || state.GridCount > state.Samples.Length || state.PortalEnds.Length > provider.Portals.Count)
                throw new ArgumentException("Invalid graph compilation checkpoint capacity.");
            work.Phase = state.Phase; work.samples.AddRange(state.Samples); work.parent.AddRange(state.Parent);
            work.portalEnds.AddRange(state.PortalEnds); work.qualifications.AddRange(state.Qualifications);
            work.sortedArcs.UnionWith(state.PendingArcs); work.arcs.AddRange(state.MaterializedArcs);
            work.surface = state.Surface; work.portal = state.Portal; work.gridCursor = state.GridCursor;
            work.neighbor = state.Neighbor; work.end = state.End; work.portalGridCursor = state.PortalGridCursor;
            work.portalArcCursor = state.PortalArcCursor; work.nodeCursor = state.NodeCursor;
            work.arcCursor = state.ArcCursor; work.wrapCursor = state.WrapCursor;
            work.gridCount = state.GridCount; work.Consumed = state.Consumed;
            work.x = state.X; work.z = state.Z; work.minX = state.MinX; work.maxX = state.MaxX;
            work.minZ = state.MinZ; work.maxZ = state.MaxZ; work.sampled = state.Sampled;
            foreach (var s in provider.Surfaces) work.surfaceCosts.Add(s.Id, s.DistanceCost);
            int restoredGridCount = state.Phase <= Stage.Portals ? state.Samples.Length : state.GridCount;
            for (int i = 0; i < restoredGridCount; i++) {
                var location = state.Samples[i];
                long gx = (long)Math.Round(location.Position.X / profile.GridCell), gz = (long)Math.Round(location.Position.Z / profile.GridCell);
                work.grid.Add(Tuple.Create(location.SurfaceId, gx, gz), i);
            }
            for (int i = 0; i < state.NodeCursor; i++) work.nodes.Add(new NavGraphNode(i, state.Samples[i], work.Root(i)));
            for (int i = 0; i < state.ArcCursor; i++) {
                var a = state.MaterializedArcs[i];
                if (!work.outLists.ContainsKey(a.From)) work.outLists[a.From] = new List<NavGraphArc>();
                if (!work.inLists.ContainsKey(a.To)) work.inLists[a.To] = new List<NavGraphArc>();
                work.outLists[a.From].Add(a); work.inLists[a.To].Add(a);
            }
            for (int i = 0; i < state.WrapCursor; i++) {
                if (work.outLists.TryGetValue(i, out var outs)) work.outgoing[i] = new ReadOnlyCollection<NavGraphArc>(outs);
                if (work.inLists.TryGetValue(i, out var ins)) work.incoming[i] = new ReadOnlyCollection<NavGraphArc>(ins);
            }
            if (state.Phase == Stage.Ready) work.Graph = new LayeredNavigationGraph(provider,
                new NavGraphBinding(provider.Binding, clearance, profile), new ReadOnlyCollection<NavGraphNode>(work.nodes),
                new ReadOnlyCollection<NavGraphArc>(work.arcs), new ReadOnlyCollection<NavConnectorResult>(work.qualifications),
                work.outgoing, work.incoming, work.surfaceCosts);
            return work;
        }
        private void AddArc(int from, int to, double cost, NavGraphArcKind kind, string portalId = null)
        {
            var arc = new NavGraphArc(from, to, cost, kind, portalId);
            if (sortedArcs.Contains(arc)) return;
            if (sortedArcs.Count >= limits.MaxArcs) throw new ArgumentException("Graph arc capacity exceeded.");
            sortedArcs.Add(arc);
            int a = Root(from), b = Root(to);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }
        private int Root(int n)
        {
            while (parent[n] != n) { parent[n] = parent[parent[n]]; n = parent[n]; }
            return n;
        }
        private int AddSample(NavLocation location)
        {
            if (samples.Count >= limits.MaxNodes) throw new ArgumentException("Graph node capacity exceeded.");
            int id = samples.Count; samples.Add(location); parent.Add(id); return id;
        }
        private void SurfaceArc(int a, int b)
        {
            var from = samples[a]; var to = samples[b];
            if (!provider.CanSweep(from, to, clearance)) return;
            double cost = LayeredNavigationProvider.Distance(from.Position, to.Position) * surfaceCosts[from.SurfaceId];
            AddArc(a, b, cost, NavGraphArcKind.Surface);
            AddArc(b, a, cost, NavGraphArcKind.Surface);
        }
        private void Step()
        {
            if (Phase == Stage.SurfaceBounds) {
                if (surface == provider.Surfaces.Count) { Phase = Stage.Portals; return; }
                var s = provider.Surfaces[surface]; surfaceCosts[s.Id] = s.DistanceCost;
                double a = Math.Ceiling(s.Area.Boundary.MinX / profile.GridCell), b = Math.Floor(s.Area.Boundary.MaxX / profile.GridCell);
                double c = Math.Ceiling(s.Area.Boundary.MinZ / profile.GridCell), d = Math.Floor(s.Area.Boundary.MaxZ / profile.GridCell);
                double count = (b - a + 1) * (d - c + 1);
                if (a < -9007199254740991d || b > 9007199254740991d || c < -9007199254740991d || d > 9007199254740991d ||
                    double.IsNaN(count) || double.IsInfinity(count) || count > limits.MaxSamples - sampled)
                    throw new ArgumentException("Graph sampling capacity exceeded.");
                if (b < a || d < c) { surface++; return; }
                minX = x = (long)a; maxX = (long)b; minZ = z = (long)c; maxZ = (long)d;
                sampled += (long)count; Phase = Stage.Samples; return;
            }
            if (Phase == Stage.Samples) {
                var s = provider.Surfaces[surface];
                var location = new NavLocation(new NavPoint(x * profile.GridCell, z * profile.GridCell), s.Id);
                if (provider.IsValid(location, clearance)) grid.Add(Tuple.Create(s.Id, x, z), AddSample(location));
                if (++z > maxZ) { z = minZ; x++; }
                if (x > maxX) { surface++; Phase = Stage.SurfaceBounds; }
                return;
            }
            if (Phase == Stage.Portals) {
                if (portal == provider.Portals.Count) { gridCount = grid.Count; Phase = Stage.GridEdges; return; }
                var p = provider.Portals[portal++]; var q = provider.QualifyPortal(p.Id, clearance); qualifications.Add(q);
                portalEnds.Add(q.Status == NavConnectorStatus.Ready ?
                    Tuple.Create(AddSample(p.Entry), AddSample(p.Exit)) : null);
                return;
            }
            if (Phase == Stage.GridEdges) {
                if (gridCursor == gridCount) { Phase = Stage.PortalEdges; portal = 0; return; }
                var location = samples[gridCursor];
                long gx = (long)Math.Round(location.Position.X / profile.GridCell), gz = (long)Math.Round(location.Position.Z / profile.GridCell);
                var key = neighbor == 0 ? Tuple.Create(location.SurfaceId, gx + 1, gz) : Tuple.Create(location.SurfaceId, gx, gz + 1);
                if (grid.TryGetValue(key, out int other)) SurfaceArc(gridCursor, other);
                if (++neighbor == 2) { neighbor = 0; gridCursor++; }
                return;
            }
            if (Phase == Stage.PortalEdges) {
                if (portal == provider.Portals.Count) { Phase = Stage.PortalArcs; portal = 0; return; }
                var pair = portalEnds[portal];
                if (pair == null) { portal++; return; }
                int endpoint = end == 0 ? pair.Item1 : pair.Item2;
                var location = samples[endpoint];
                if (portalGridCursor == gridCount) {
                    portalGridCursor = 0;
                    if (++end == 2) { end = 0; portal++; }
                    return;
                }
                var sample = samples[portalGridCursor++];
                if (sample.SurfaceId == location.SurfaceId &&
                    LayeredNavigationProvider.Distance(sample.Position, location.Position) <= Math.Sqrt(2) * profile.GridCell)
                    SurfaceArc(endpoint, portalGridCursor - 1);
                return;
            }
            if (Phase == Stage.PortalArcs) {
                if (portal == provider.Portals.Count) { Phase = Stage.Nodes; return; }
                var pair = portalEnds[portal]; var p = provider.Portals[portal];
                if (pair != null) {
                    if (portalArcCursor == 0) AddArc(pair.Item1, pair.Item2, p.TraversalCost, NavGraphArcKind.Portal, p.Id);
                    else if (p.Direction == NavPortalDirection.Both) AddArc(pair.Item2, pair.Item1, p.TraversalCost, NavGraphArcKind.Portal, p.Id);
                }
                if (++portalArcCursor == 2) { portalArcCursor = 0; portal++; }
                return;
            }
            if (Phase == Stage.Nodes) {
                if (nodeCursor == samples.Count) { Phase = Stage.SortedArcs; return; }
                nodes.Add(new NavGraphNode(nodeCursor, samples[nodeCursor], Root(nodeCursor))); nodeCursor++;
                return;
            }
            if (Phase == Stage.SortedArcs) {
                if (sortedArcs.Count == 0) { Phase = Stage.Adjacency; return; }
                var a = sortedArcs.Min; sortedArcs.Remove(a); arcs.Add(a); return;
            }
            if (Phase == Stage.Adjacency) {
                if (arcCursor == arcs.Count) { Phase = Stage.WrapAdjacency; return; }
                var a = arcs[arcCursor++];
                if (!outLists.ContainsKey(a.From)) outLists[a.From] = new List<NavGraphArc>();
                if (!inLists.ContainsKey(a.To)) inLists[a.To] = new List<NavGraphArc>();
                outLists[a.From].Add(a); inLists[a.To].Add(a); return;
            }
            if (Phase == Stage.WrapAdjacency) {
                if (wrapCursor == nodes.Count) {
                    Graph = new LayeredNavigationGraph(provider, new NavGraphBinding(provider.Binding, clearance, profile),
                        new ReadOnlyCollection<NavGraphNode>(nodes), new ReadOnlyCollection<NavGraphArc>(arcs),
                        new ReadOnlyCollection<NavConnectorResult>(qualifications), outgoing, incoming, surfaceCosts);
                    Phase = Stage.Ready; return;
                }
                if (outLists.TryGetValue(wrapCursor, out var outs)) outgoing[wrapCursor] = new ReadOnlyCollection<NavGraphArc>(outs);
                if (inLists.TryGetValue(wrapCursor, out var ins)) incoming[wrapCursor] = new ReadOnlyCollection<NavGraphArc>(ins);
                wrapCursor++; return;
            }
        }
        private sealed class ArcComparer : IComparer<NavGraphArc>
        {
            public int Compare(NavGraphArc a, NavGraphArc b)
            {
                int c = a.From.CompareTo(b.From); if (c != 0) return c;
                c = a.To.CompareTo(b.To); if (c != 0) return c;
                c = a.Kind.CompareTo(b.Kind); if (c != 0) return c;
                return StringComparer.Ordinal.Compare(a.PortalId, b.PortalId);
            }
        }
    }
}

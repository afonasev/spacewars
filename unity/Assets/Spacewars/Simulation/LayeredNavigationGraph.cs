using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    public sealed class NavGraphCompileLimits
    {
        public NavGraphCompileLimits(int maxSamples, int maxNodes, int maxArcs)
        {
            if (maxSamples > 1048576 || maxNodes > 300000 || maxArcs > 1200000)
                throw new ArgumentException("Graph compilation capacity exceeds the finite navigation profile.");
            MaxSamples = NavContract.Revision(maxSamples); MaxNodes = NavContract.Revision(maxNodes); MaxArcs = NavContract.Revision(maxArcs);
        }
        public int MaxSamples { get; } public int MaxNodes { get; } public int MaxArcs { get; }
    }
    public readonly struct NavGraphNode
    {
        internal NavGraphNode(int id, NavLocation location, int component) { Id = id; Location = location; Component = component; }
        public int Id { get; } public NavLocation Location { get; }
        // Weak connected component of THIS clearance graph. Equality is not
        // proof of directed reachability through a one-way portal.
        public int Component { get; }
    }
    public enum NavGraphArcKind { Surface, Portal }
    public readonly struct NavGraphArc
    {
        internal NavGraphArc(int from, int to, double cost, NavGraphArcKind kind, string portalId,
            IReadOnlyList<NavTypedLeg> trace = null)
        { From = from; To = to; Cost = NavContract.Number(cost); Kind = kind; PortalId = portalId; Trace = trace; }
        public int From { get; } public int To { get; } public double Cost { get; } public NavGraphArcKind Kind { get; } public string PortalId { get; }
        // Non-null only for a physically qualified partition edge. Its surface
        // and zero-cost seam legs are retained without inventing a portal.
        public IReadOnlyList<NavTypedLeg> Trace { get; }
    }
    public sealed class NavGraphAttachment
    {
        internal NavGraphAttachment(NavLocation exactLocation, int nodeId, double cost) { ExactLocation = exactLocation; NodeId = nodeId; Cost = cost; }
        public NavLocation ExactLocation { get; } public int NodeId { get; } public double Cost { get; }
    }

    // Derived immutable data only. Not authority state, a route solver, a cache,
    // or an every-tick Facts field. Compilation work is not budgeted in A2-a.
    public sealed class LayeredNavigationGraph
    {
        private readonly ILayeredNavigationProvider provider;
        private readonly IReadOnlyList<NavGraphArc>[] outgoing, incoming;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<NavGraphArc>> sparseOutgoing, sparseIncoming;
        private readonly Dictionary<string, double> surfaceCosts;
        private readonly IReadOnlyList<NavLocation> partitionLocations;
        private readonly IReadOnlyList<int> partitionComponents, partitionArcSlots;
        private readonly NavPackedSlots partitionNeighbors, partitionIncoming, partitionTraceSlots;
        private readonly IReadOnlyList<IReadOnlyList<NavTypedLeg>> partitionTraces;
        internal LayeredNavigationGraph(ILayeredNavigationProvider provider, NavGraphBinding binding, NavGraphNode[] nodes, NavGraphArc[] arcs, NavConnectorResult[] connectors)
        {
            this.provider = provider; Binding = binding; Nodes = Array.AsReadOnly(nodes); Arcs = Array.AsReadOnly(arcs); PortalQualifications = Array.AsReadOnly(connectors);
            surfaceCosts = provider.Surfaces.ToDictionary(s => s.Id, s => s.DistanceCost, StringComparer.Ordinal);
            var outLists = Enumerable.Range(0, nodes.Length).Select(_ => new List<NavGraphArc>()).ToArray();
            var inLists = Enumerable.Range(0, nodes.Length).Select(_ => new List<NavGraphArc>()).ToArray();
            foreach (var arc in arcs) { outLists[arc.From].Add(arc); inLists[arc.To].Add(arc); }
            outgoing = outLists.Select(a => (IReadOnlyList<NavGraphArc>)Array.AsReadOnly(a.ToArray())).ToArray();
            incoming = inLists.Select(a => (IReadOnlyList<NavGraphArc>)Array.AsReadOnly(a.ToArray())).ToArray();
        }
        // The budgeted compiler builds each adjacency entry and wrapper in its
        // own work step. Publication then only transfers completed immutable views.
        internal LayeredNavigationGraph(ILayeredNavigationProvider provider, NavGraphBinding binding,
            IReadOnlyList<NavGraphNode> nodes, IReadOnlyList<NavGraphArc> arcs, IReadOnlyList<NavConnectorResult> connectors,
            IReadOnlyDictionary<int, IReadOnlyList<NavGraphArc>> preparedOutgoing,
            IReadOnlyDictionary<int, IReadOnlyList<NavGraphArc>> preparedIncoming,
            Dictionary<string, double> preparedSurfaceCosts)
        {
            this.provider = provider; Binding = binding;
            Nodes = nodes; Arcs = arcs; PortalQualifications = connectors;
            sparseOutgoing = preparedOutgoing; sparseIncoming = preparedIncoming;
            surfaceCosts = preparedSurfaceCosts;
        }
        internal LayeredNavigationGraph(PartitionNavigationProvider provider, NavGraphBinding binding,
            IReadOnlyList<NavLocation> locations, IReadOnlyList<int> components, NavPackedSlots neighbors,
            NavPackedSlots incomingSlots, IReadOnlyList<int> arcSlots, NavPackedSlots traceSlots,
            IReadOnlyList<IReadOnlyList<NavTypedLeg>> traces, long traceBytes)
        {
            this.provider = provider; Binding = binding;
            partitionLocations = locations; partitionComponents = components;
            partitionNeighbors = neighbors; partitionIncoming = incomingSlots;
            partitionArcSlots = arcSlots; partitionTraceSlots = traceSlots; partitionTraces = traces;
            Nodes = new PartitionNodes(locations, components);
            Arcs = new PartitionArcs(this);
            PortalQualifications = Array.AsReadOnly(Array.Empty<NavConnectorResult>());
            surfaceCosts = provider.Surfaces.ToDictionary(s => s.Id, s => s.DistanceCost, StringComparer.Ordinal);
            EstimatedRetainedBytes = 4096L + 32L * ((NavPagedList<NavLocation>)locations).Capacity +
                4L * ((NavPagedList<int>)components).Capacity +
                neighbors.EstimatedRetainedBytes + incomingSlots.EstimatedRetainedBytes +
                traceSlots.EstimatedRetainedBytes + 4L * ((NavPagedList<int>)arcSlots).Capacity +
                8L * ((NavPagedList<IReadOnlyList<NavTypedLeg>>)traces).Capacity + traceBytes;
        }
        public long EstimatedRetainedBytes { get; }
        private sealed class PartitionNodes : IReadOnlyList<NavGraphNode>
        {
            private readonly IReadOnlyList<NavLocation> locations; private readonly IReadOnlyList<int> components;
            internal PartitionNodes(IReadOnlyList<NavLocation> locations, IReadOnlyList<int> components)
            { this.locations = locations; this.components = components; }
            public int Count => locations.Count;
            public NavGraphNode this[int index] => new NavGraphNode(index, locations[index], components[index]);
            public IEnumerator<NavGraphNode> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
        private sealed class PartitionArcs : IReadOnlyList<NavGraphArc>
        {
            private readonly LayeredNavigationGraph graph;
            internal PartitionArcs(LayeredNavigationGraph graph) { this.graph = graph; }
            public int Count => graph.partitionArcSlots.Count;
            public NavGraphArc this[int index] => graph.PartitionArc(graph.partitionArcSlots[index]);
            public IEnumerator<NavGraphArc> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
        private sealed class PartitionArcView : IReadOnlyList<NavGraphArc>
        {
            private readonly LayeredNavigationGraph graph; private readonly int node; private readonly bool incoming;
            internal PartitionArcView(LayeredNavigationGraph graph, int node, bool incoming)
            { this.graph = graph; this.node = node; this.incoming = incoming; }
            public int Count { get { int n = 0; for (int d = 0; d < 4; d++) if (Slot(d) >= 0) n++; return n; } }
            private int Slot(int direction) => incoming ? graph.partitionIncoming.Get(node * 4 + direction) - 1 :
                graph.partitionNeighbors.Get(node * 4 + direction) > 0 ? node * 4 + direction : -1;
            public NavGraphArc this[int index]
            {
                get { for (int d = 0; d < 4; d++) { int slot = Slot(d); if (slot < 0) continue;
                    if (index-- == 0) return graph.PartitionArc(slot); } throw new ArgumentOutOfRangeException(nameof(index)); }
            }
            public IEnumerator<NavGraphArc> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
        private NavGraphArc PartitionArc(int slot)
        {
            int from = slot / 4, to = partitionNeighbors.Get(slot) - 1;
            int traceIndex = partitionTraceSlots.Get(slot) - 1;
            var trace = traceIndex >= 0 ? partitionTraces[traceIndex] : null;
            var kind = trace != null && trace.Any(l => l.Kind == NavTypedLegKind.Portal)
                ? NavGraphArcKind.Portal : NavGraphArcKind.Surface;
            string portal = kind == NavGraphArcKind.Portal ? trace.First(l => l.Kind == NavTypedLegKind.Portal).PortalId : null;
            double cost = trace != null ? trace.Sum(l => l.Cost) :
                LayeredNavigationProvider.Distance(partitionLocations[from].Position, partitionLocations[to].Position) *
                SurfaceCost(partitionLocations[from].SurfaceId);
            return new NavGraphArc(from, to, cost, kind, portal, trace);
        }
        public IReadOnlyList<NavLayerSurface> Surfaces => provider.Surfaces; public IReadOnlyList<NavLayerPortal> Portals => provider.Portals;
        public NavGraphBinding Binding { get; } public IReadOnlyList<NavGraphNode> Nodes { get; } public IReadOnlyList<NavGraphArc> Arcs { get; } public IReadOnlyList<NavConnectorResult> PortalQualifications { get; }
        public bool IsValid(NavLocation location) => provider.IsValid(location, Binding.Clearance);
        public bool CanSweep(NavLocation from, NavLocation to) => provider.CanSweep(from, to, Binding.Clearance);
        public double SurfaceCost(string surfaceId) => surfaceCosts[surfaceId];
        public IReadOnlyList<NavGraphArc> Outgoing(int nodeId)
        { Node(nodeId); return partitionNeighbors != null ? new PartitionArcView(this, nodeId, false) : outgoing != null ? outgoing[nodeId] : sparseOutgoing.TryGetValue(nodeId, out var arcs) ? arcs : Array.Empty<NavGraphArc>(); }
        public IReadOnlyList<NavGraphArc> Incoming(int nodeId)
        { Node(nodeId); return partitionNeighbors != null ? new PartitionArcView(this, nodeId, true) : incoming != null ? incoming[nodeId] : sparseIncoming.TryGetValue(nodeId, out var arcs) ? arcs : Array.Empty<NavGraphArc>(); }
        // One node probe is one attachment work unit. Callers own the cursor.
        public bool TryAttach(NavLocation exactLocation, int nodeId, out NavGraphAttachment attachment)
        {
            var node = Node(nodeId); attachment = null;
            if (!CanSweep(exactLocation, node.Location)) return false;
            attachment = new NavGraphAttachment(exactLocation, nodeId,
                NavContract.Number(LayeredNavigationProvider.Distance(exactLocation.Position, node.Location.Position) * SurfaceCost(exactLocation.SurfaceId)));
            return true;
        }
        public IReadOnlyList<NavGraphAttachment> Attach(NavLocation exactLocation)
        {
            if (!provider.IsValid(exactLocation, Binding.Clearance)) throw new ArgumentException("Invalid exact graph endpoint.");
            var surface = provider.Surfaces.Single(s => s.Id == exactLocation.SurfaceId);
            return Array.AsReadOnly(Nodes.Where(n => provider.CanSweep(exactLocation, n.Location, Binding.Clearance))
                .Select(n => new NavGraphAttachment(exactLocation, n.Id, NavContract.Number(LayeredNavigationProvider.Distance(exactLocation.Position, n.Location.Position) * surface.DistanceCost))).ToArray());
        }
        public NavSharedArtifactKey RegionKey(int nodeId, NavTerminalRegion region)
        {
            var node = Node(nodeId);
            if (region == null || !provider.IsValid(region.Requested, Binding.Clearance) || region.Candidates.Any(c => !provider.IsValid(c, Binding.Clearance))) throw new ArgumentException("Invalid terminal region for snapshot.");
            return new NavSharedArtifactKey(Binding, node.Location.SurfaceId, node.Component, NavTerminalKind.Region, region.StableKey);
        }
        public NavSharedArtifactKey PortalKey(int nodeId, string portalId)
        {
            var node = Node(nodeId);
            if (!Arcs.Any(a => a.Kind == NavGraphArcKind.Portal && a.PortalId == portalId)) throw new ArgumentException("Portal unavailable for snapshot.");
            return new NavSharedArtifactKey(Binding, node.Location.SurfaceId, node.Component, NavTerminalKind.Portal, portalId);
        }
        private NavGraphNode Node(int id)
        { if (id < 0 || id >= Nodes.Count) throw new ArgumentException("Unknown snapshot node."); return Nodes[id]; }
    }

    public static class LayeredNavigationGraphCompiler
    {
        private sealed class Sample { internal NavLocation Location; internal string StableId; }
        public static LayeredNavigationGraph Compile(ILayeredNavigationProvider provider, NavClearanceProfile clearance, NavGraphProfile profile, NavGraphCompileLimits limits)
        {
            if (provider == null || clearance == null || profile == null || limits == null) throw new ArgumentNullException("Graph input");
            if (provider is PartitionNavigationProvider) {
                var work = new LayeredNavigationCompileWork(provider, clearance, profile, limits);
                while (work.Phase != LayeredNavigationCompileWork.Stage.Ready) work.Advance(4096);
                return work.Graph;
            }
            var samples = new List<Sample>(); var grid = new Dictionary<Tuple<string, long, long>, Sample>();
            void Add(Sample sample) { if (samples.Count >= limits.MaxNodes) throw new ArgumentException("Graph node capacity exceeded."); samples.Add(sample); }
            long sampleCount = 0;
            foreach (var surface in provider.Surfaces) {
                // Global origin and exact cell size, not bounds-relative snapping.
                double xmin = Math.Ceiling(surface.Area.Boundary.MinX / profile.GridCell), xmax = Math.Floor(surface.Area.Boundary.MaxX / profile.GridCell);
                double zmin = Math.Ceiling(surface.Area.Boundary.MinZ / profile.GridCell), zmax = Math.Floor(surface.Area.Boundary.MaxZ / profile.GridCell);
                double count = (xmax - xmin + 1) * (zmax - zmin + 1);
                if (xmin < -9007199254740991d || xmax > 9007199254740991d || zmin < -9007199254740991d || zmax > 9007199254740991d || double.IsNaN(count) || double.IsInfinity(count) || count > limits.MaxSamples - sampleCount) throw new ArgumentException("Graph sampling capacity exceeded.");
                if (xmax < xmin || zmax < zmin) continue;
                sampleCount += (long)count;
                for (long x = (long)xmin; x <= (long)xmax; x++) for (long z = (long)zmin; z <= (long)zmax; z++) {
                    var location = new NavLocation(new NavPoint(x * profile.GridCell, z * profile.GridCell), surface.Id);
                    if (!provider.IsValid(location, clearance)) continue;
                    var sample = new Sample { Location = location, StableId = "grid/" + surface.Id + "/" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + z.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                    Add(sample); grid.Add(Tuple.Create(surface.Id, x, z), sample);
                }
            }
            var qualifications = new List<NavConnectorResult>(); var portalEnds = new Dictionary<string, Tuple<Sample, Sample>>(StringComparer.Ordinal);
            foreach (var portal in provider.Portals) {
                var result = provider.QualifyPortal(portal.Id, clearance); qualifications.Add(result);
                if (result.Status != NavConnectorStatus.Ready) continue;
                var entry = new Sample { Location = portal.Entry, StableId = "portal/" + portal.Id + "/entry" };
                var exit = new Sample { Location = portal.Exit, StableId = "portal/" + portal.Id + "/exit" };
                Add(entry); Add(exit); portalEnds.Add(portal.Id, Tuple.Create(entry, exit));
            }
            samples = samples.OrderBy(s => s.StableId, StringComparer.Ordinal).ToList();
            var indices = samples.Select((s, i) => new { Sample = s, Id = i }).ToDictionary(s => s.Sample, s => s.Id);
            var arcs = new List<NavGraphArc>();
            void Arc(Sample a, Sample b, double cost, NavGraphArcKind kind, string portalId = null)
            { if (arcs.Count >= limits.MaxArcs) throw new ArgumentException("Graph arc capacity exceeded."); arcs.Add(new NavGraphArc(indices[a], indices[b], cost, kind, portalId)); }
            void SurfaceArcs(Sample a, Sample b)
            {
                if (!provider.CanSweep(a.Location, b.Location, clearance)) return;
                double cost = LayeredNavigationProvider.Distance(a.Location.Position, b.Location.Position) * provider.Surfaces.Single(s => s.Id == a.Location.SurfaceId).DistanceCost;
                Arc(a, b, cost, NavGraphArcKind.Surface); Arc(b, a, cost, NavGraphArcKind.Surface);
            }
            foreach (var pair in grid) {
                var k = pair.Key;
                foreach (var delta in new[] { Tuple.Create(1L, 0L), Tuple.Create(0L, 1L) })
                    if (grid.TryGetValue(Tuple.Create(k.Item1, k.Item2 + delta.Item1, k.Item3 + delta.Item2), out var other)) SurfaceArcs(pair.Value, other);
            }
            foreach (var portal in provider.Portals) {
                if (!portalEnds.TryGetValue(portal.Id, out var ends)) continue;
                foreach (var end in new[] { ends.Item1, ends.Item2 })
                    foreach (var sample in grid.Values.Where(s => s.Location.SurfaceId == end.Location.SurfaceId && LayeredNavigationProvider.Distance(s.Location.Position, end.Location.Position) <= Math.Sqrt(2) * profile.GridCell)) SurfaceArcs(end, sample);
                Arc(ends.Item1, ends.Item2, portal.TraversalCost, NavGraphArcKind.Portal, portal.Id);
                if (portal.Direction == NavPortalDirection.Both) Arc(ends.Item2, ends.Item1, portal.TraversalCost, NavGraphArcKind.Portal, portal.Id);
            }
            // Derive components only from qualified edges in this radius/mobility
            // snapshot. Portal declaration alone cannot union blocked layers.
            var parent = Enumerable.Range(0, samples.Count).ToArray();
            int Root(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            foreach (var arc in arcs) { int a = Root(arc.From), b = Root(arc.To); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }
            var nodes = samples.Select((s, i) => new NavGraphNode(i, s.Location, Root(i))).ToArray();
            arcs = arcs.OrderBy(a => a.From).ThenBy(a => a.To).ThenBy(a => a.Kind).ThenBy(a => a.PortalId, StringComparer.Ordinal).ToList();
            return new LayeredNavigationGraph(provider, new NavGraphBinding(provider.Binding, clearance, profile), nodes, arcs.ToArray(), qualifications.ToArray());
        }
    }
}

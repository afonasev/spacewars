using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    public sealed partial class NavGraphCompileLimits
    {
        public NavGraphCompileLimits(int maxSamples, int maxNodes, int maxArcs)
        {
            if (maxSamples > 1048576 || maxNodes > 300000 || maxArcs > 1200000)
                throw new ArgumentException("Graph compilation capacity exceeds the finite navigation profile.");
            MaxSamples = NavContract.Revision(maxSamples); MaxNodes = NavContract.Revision(maxNodes); MaxArcs = NavContract.Revision(maxArcs);
        }
        public int MaxSamples { get; } public int MaxNodes { get; } public int MaxArcs { get; }
    }
    public readonly struct NavGraphNode : IEquatable<NavGraphNode>
    {
        internal NavGraphNode(int id, NavLocation location, int component) { Id = id; Location = location; Component = component; }
        public int Id { get; } public NavLocation Location { get; }
        // Weak connected component of THIS clearance graph. Equality is not
        // proof of directed reachability through a one-way portal.
        public int Component { get; }
        public bool Equals(NavGraphNode other)=>Id==other.Id&&Component==other.Component&&Location.Equals(other.Location);
        public override bool Equals(object other)=>other is NavGraphNode node&&Equals(node);
        public override int GetHashCode()=>Id^Component^Location.GetHashCode();
    }
    public enum NavGraphArcKind { Surface, Portal }
    public readonly struct NavGraphArc : IEquatable<NavGraphArc>
    {
        internal NavGraphArc(int from, int to, double cost, NavGraphArcKind kind, string portalId,
            IReadOnlyList<NavTypedLeg> trace = null, NavNameReference nameReference = default)
        { From = from; To = to; Cost = NavContract.Number(cost); Kind = kind; PortalId = portalId; Trace = trace; NameReference = nameReference; }
        internal NavNameReference NameReference { get; }
        internal NavGraphArc Detached()=>new NavGraphArc(From,To,Cost,Kind,PortalId,Trace==null?null:Array.AsReadOnly(Trace.Select(leg=>leg.Detached()).ToArray()));
        public bool Equals(NavGraphArc other)=>From==other.From&&To==other.To&&Cost.Equals(other.Cost)&&Kind==other.Kind&&PortalId==other.PortalId&&Equals(Trace,other.Trace);
        public override bool Equals(object other)=>other is NavGraphArc arc&&Equals(arc);
        public override int GetHashCode()=>From^To^Cost.GetHashCode()^(int)Kind^(PortalId?.GetHashCode()??0)^(Trace?.GetHashCode()??0);
        public int From { get; } public int To { get; } public double Cost { get; } public NavGraphArcKind Kind { get; } public string PortalId { get; }
        // Non-null only for a physically qualified partition edge. Its surface
        // and zero-cost seam legs are retained without inventing a portal.
        public IReadOnlyList<NavTypedLeg> Trace { get; }
    }
    internal sealed class NavGraphArcComparer : IComparer<NavGraphArc>
    {
        internal static readonly NavGraphArcComparer Instance=new NavGraphArcComparer();
        public int Compare(NavGraphArc a,NavGraphArc b){int c=a.From.CompareTo(b.From);if(c!=0)return c;c=a.To.CompareTo(b.To);if(c!=0)return c;c=a.Kind.CompareTo(b.Kind);return c!=0?c:StringComparer.Ordinal.Compare(a.PortalId,b.PortalId);}
    }
    public sealed class NavGraphAttachment
    {
        internal NavGraphAttachment(NavLocation exactLocation, int nodeId, double cost) { ExactLocation = exactLocation; NodeId = nodeId; Cost = cost; }
        public NavLocation ExactLocation { get; } public int NodeId { get; } public double Cost { get; }
    }

    // Derived immutable data only. Not authority state, a route solver, a cache,
    // or an every-tick Facts field. Compilation work is not budgeted in A2-a.
    public sealed partial class LayeredNavigationGraph : NavOwnedStorage
    {
        private readonly ILayeredNavigationProvider provider;
        private NavOwnedStorage ownedProvider,ownedBinding,ownedClearance,ownedProfile;
        private void RetainProvider(NavGraphBinding binding){if(provider is INavStorageFacade facade&&facade.StorageRoot!=null){ownedProvider=facade.StorageRoot;if(IsRegistered)RequireEdge(ownedProvider);ownedProvider.Retain();}if(binding.StorageRoot!=null){if(IsRegistered)RequireEdge(binding.StorageRoot);binding.StorageRoot.Retain();ownedBinding=binding.StorageRoot;}else{if(IsRegistered)throw new ArgumentException("Owned graph requires owned binding.");NavMetadataOwnership.RetainDistinct(this,binding.Clearance.MetadataOwner,binding.Profile.MetadataOwner,null,out ownedClearance,out ownedProfile,out _);}}
        internal ILayeredNavigationProvider PhysicalProvider=>provider;
        private readonly IReadOnlyList<NavGraphArc>[] outgoing, incoming;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<NavGraphArc>> sparseOutgoing, sparseIncoming;
        private readonly IReadOnlyDictionary<NavNameKey,double> surfaceCosts;
        private readonly IReadOnlyList<NavLocation> partitionLocations;
        private readonly IReadOnlyList<int> partitionComponents, partitionArcSlots;
        private readonly NavPackedSlots partitionNeighbors, partitionIncoming, partitionTraceSlots;
        private readonly IReadOnlyList<IReadOnlyList<NavTypedLeg>> partitionTraces;
        internal LayeredNavigationGraph(ILayeredNavigationProvider provider, NavGraphBinding binding, NavGraphNode[] nodes, NavGraphArc[] arcs, NavConnectorResult[] connectors):base(LayoutBase)
        {
            using(var allocation=new NavStorageAllocation(null)){allocation.Register(this);Retain();}
            this.provider = provider;RetainProvider(binding); Binding = binding; Nodes = Array.AsReadOnly(nodes); Arcs = Array.AsReadOnly(arcs); PortalQualifications = Array.AsReadOnly(connectors);
            SupportsPackedField=PackedCapability(provider,Nodes);
            surfaceCosts = provider.Surfaces.ToDictionary(s => NavNameKey.Surface(s), s => s.DistanceCost);
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
            IReadOnlyDictionary<NavNameKey,double> preparedSurfaceCosts,Func<long,bool> reserve=null,bool alreadyReserved=false):base(LayoutBase)
        {
            using(var allocation=new NavStorageAllocation(reserve)){
                if(!alreadyReserved)allocation.Reserve(LayoutBase);allocation.Register(this);

            this.provider = provider;RetainProvider(binding); Binding = binding;
            Nodes = nodes; Arcs = arcs; PortalQualifications = connectors;
            sparseOutgoing = preparedOutgoing; sparseIncoming = preparedIncoming;
            surfaceCosts = preparedSurfaceCosts;
            OwnRoot(nodes,0);OwnRoot(arcs,1);OwnRoot(connectors,2);OwnRoot(preparedOutgoing,3);OwnRoot(preparedIncoming,4);OwnRoot(preparedSurfaceCosts,5);
            SupportsPackedField=PackedCapability(provider,Nodes);
                        Retain();
            }
        }
        internal LayeredNavigationGraph(PartitionNavigationProvider provider, NavGraphBinding binding,
            IReadOnlyList<NavLocation> locations, IReadOnlyList<int> components, NavPackedSlots neighbors,
            NavPackedSlots incomingSlots, IReadOnlyList<int> arcSlots, NavPackedSlots traceSlots,
            IReadOnlyList<IReadOnlyList<NavTypedLeg>> traces,long traceBytes,Func<long,bool> reserve=null,bool alreadyReserved=false):base(LayoutBase)
        {
            using(var allocation=new NavStorageAllocation(reserve)){
                if(!alreadyReserved)allocation.Reserve(LayoutBase);allocation.Register(this);

            this.provider = provider;RetainProvider(binding); Binding = binding;
            partitionLocations = locations; partitionComponents = components;
            partitionNeighbors = neighbors; partitionIncoming = incomingSlots;
            partitionArcSlots = arcSlots; partitionTraceSlots = traceSlots; partitionTraces = traces;
            OwnRoot(locations,6);OwnRoot(components,7);OwnRoot(neighbors,8);OwnRoot(incomingSlots,9);OwnRoot(arcSlots,10);OwnRoot(traceSlots,11);OwnRoot(traces,12);
            Nodes = new PartitionNodes(locations, components);
            Arcs = new PartitionArcs(this);
            PortalQualifications = Array.AsReadOnly(Array.Empty<NavConnectorResult>());
            SupportsPackedField=PackedCapability(provider,Nodes);
            surfaceCosts = null;
            EstimatedRetainedBytes = 4096L + 32L * ((NavPagedList<NavLocation>)locations).Capacity +
                4L * ((NavPagedList<int>)components).Capacity +
                neighbors.EstimatedRetainedBytes + incomingSlots.EstimatedRetainedBytes +
                traceSlots.EstimatedRetainedBytes + 4L * ((NavPagedList<int>)arcSlots).Capacity +
                8L * ((NavPagedList<IReadOnlyList<NavTypedLeg>>)traces).Capacity + traceBytes +
                ((NavPagedList<NavLocation>)locations).DirectoryRetainedBytes+((NavPagedList<int>)components).DirectoryRetainedBytes+
                ((NavPagedList<int>)arcSlots).DirectoryRetainedBytes+((NavPagedList<IReadOnlyList<NavTypedLeg>>)traces).DirectoryRetainedBytes;
                        Retain();
            }
        }
        internal static LayeredNavigationGraph FromPrepared(ILayeredNavigationProvider provider,NavGraphBinding binding,IReadOnlyList<NavGraphNode> nodes,IReadOnlyList<NavGraphArc> arcs,IReadOnlyList<NavConnectorResult> connectors,IReadOnlyDictionary<int,IReadOnlyList<NavGraphArc>> outgoing,IReadOnlyDictionary<int,IReadOnlyList<NavGraphArc>> incoming,IReadOnlyDictionary<NavNameKey,double> surfaceCosts,Func<long,bool> reserve=null)
        {if(reserve?.Target is INavStorageAllocator)RequireSparseInputs(reserve.Target,provider,binding,nodes,arcs,connectors,outgoing,incoming,surfaceCosts);using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(LayoutBase);return new LayeredNavigationGraph(provider,binding,nodes,arcs,connectors,outgoing,incoming,surfaceCosts,reserve,true);}}
        internal static LayeredNavigationGraph FromPartition(PartitionNavigationProvider provider,NavGraphBinding binding,IReadOnlyList<NavLocation> locations,IReadOnlyList<int> components,NavPackedSlots neighbors,NavPackedSlots incoming,IReadOnlyList<int> arcs,NavPackedSlots traceSlots,IReadOnlyList<IReadOnlyList<NavTypedLeg>> traces,long traceBytes,Func<long,bool> reserve=null)
        {if(reserve?.Target is INavStorageAllocator)RequirePackedInputs(reserve.Target,provider,binding,locations,components,neighbors,incoming,arcs,traceSlots,traces);using(var allocation=new NavStorageAllocation(reserve)){allocation.Reserve(LayoutBase);return new LayeredNavigationGraph(provider,binding,locations,components,neighbors,incoming,arcs,traceSlots,traces,traceBytes,reserve,true);}}
        private bool released;private int ownedEdges;
        private void OwnRoot(object root,int bit){if(root is NavOwnedStorage storage){if(IsRegistered)RequireEdge(storage);storage.Retain();ownedEdges|=1<<bit;}}
        private static void ReleaseRoot(object root){if(root is NavOwnedStorage storage)storage.Release();}
        internal void ReleaseStorage(){if(released)return;released=true;Release();}
        internal override void ReleaseOwner()=>ReleaseStorage();
        protected override void ReleaseEdges(){ownedBinding?.Release();ownedClearance?.Release();ownedProfile?.Release();ownedProvider?.Release();if((ownedEdges&(1<<0))!=0)ReleaseRoot(Nodes);if((ownedEdges&(1<<1))!=0)ReleaseRoot(Arcs);if((ownedEdges&(1<<2))!=0)ReleaseRoot(PortalQualifications);if((ownedEdges&(1<<3))!=0)ReleaseRoot(sparseOutgoing);if((ownedEdges&(1<<4))!=0)ReleaseRoot(sparseIncoming);if((ownedEdges&(1<<5))!=0)ReleaseRoot(surfaceCosts);if((ownedEdges&(1<<6))!=0)ReleaseRoot(partitionLocations);if((ownedEdges&(1<<7))!=0)ReleaseRoot(partitionComponents);if((ownedEdges&(1<<8))!=0)ReleaseRoot(partitionNeighbors);if((ownedEdges&(1<<9))!=0)ReleaseRoot(partitionIncoming);if((ownedEdges&(1<<10))!=0)ReleaseRoot(partitionArcSlots);if((ownedEdges&(1<<11))!=0)ReleaseRoot(partitionTraceSlots);if((ownedEdges&(1<<12))!=0)ReleaseRoot(partitionTraces);ownedEdges=0;}
        internal NavOwnedStorage PhysicalRoot=>this;
        internal bool SupportsPackedField {get;}
        // Both internal flat constructors receive the compiler's dense IDs: each
        // node is materialized with Id=nodes.Count. No additional publication scan.
        private static bool PackedCapability(ILayeredNavigationProvider provider,IReadOnlyList<NavGraphNode> nodes)=>
            provider is PartitionNavigationProvider||provider is LayeredNavigationProvider flat&&flat.Id=="flat"&&flat.SemanticsVersion==1&&
            flat.Surfaces.Count==1&&flat.Surfaces[0].Id==NavLocation.FlatSurface&&flat.Portals.Count==0;
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
        public double SurfaceCost(string surfaceId) => provider is PartitionNavigationProvider partition?partition.SurfaceCost(surfaceId):surfaceCosts[new NavNameKey(surfaceId)];
        public IReadOnlyList<NavGraphArc> Outgoing(int nodeId)
        { Node(nodeId); return partitionNeighbors != null ? new PartitionArcView(this, nodeId, false) : outgoing != null ? outgoing[nodeId] : sparseOutgoing.TryGetValue(nodeId, out var arcs) ? arcs : Array.Empty<NavGraphArc>(); }
        internal NavGraphArc ResolveCheckpointArc(NavGraphArc saved)
        {
            if(saved.From<0||saved.From>=Nodes.Count)throw new ArgumentException("Invalid successor node.");
            foreach(var arc in Outgoing(saved.From)){
                if(arc.To!=saved.To||arc.Cost!=saved.Cost||arc.Kind!=saved.Kind||arc.PortalId!=saved.PortalId||(arc.Trace==null)!=(saved.Trace==null))continue;
                if(arc.Trace!=null){if(arc.Trace.Count!=saved.Trace.Count)continue;bool same=true;for(int n=0;n<arc.Trace.Count;n++){var a=arc.Trace[n];var b=saved.Trace[n];if(b==null||!a.From.Equals(b.From)||!a.To.Equals(b.To)||a.Cost!=b.Cost||a.Kind!=b.Kind||a.PortalId!=b.PortalId){same=false;break;}}if(!same)continue;}
                return arc;
            }
            throw new ArgumentException("Successor is absent from the exact graph.");
        }
        public IReadOnlyList<NavGraphArc> Incoming(int nodeId)
        { Node(nodeId); return partitionNeighbors != null ? new PartitionArcView(this, nodeId, true) : incoming != null ? incoming[nodeId] : sparseIncoming.TryGetValue(nodeId, out var arcs) ? arcs : Array.Empty<NavGraphArc>(); }
        // One node probe is one attachment work unit. Callers own the cursor.
        internal Action BeforeAttachmentProbe;
        public bool TryAttach(NavLocation exactLocation, int nodeId, out NavGraphAttachment attachment)
        {
            BeforeAttachmentProbe?.Invoke();
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

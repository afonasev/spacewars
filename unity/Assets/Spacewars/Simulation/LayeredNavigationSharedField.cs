using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    // Lazy 64-label pages. Present bits preserve a real zero distance without
    // filling an entire map-wide field before the first budgeted work step.
    internal sealed class NavPackedFieldStorage
    {
        private const int LabelsPerPage = 64, PagesPerDirectory = 256;
        private sealed class Page
        {
            internal readonly double[] Distance = new double[LabelsPerPage];
            internal readonly NavGraphArc[] Next = new NavGraphArc[LabelsPerPage];
            internal readonly byte[] Flags = new byte[LabelsPerPage];
        }
        internal sealed class SavedPage
        {
            internal int Index; internal double[] Distance; internal NavGraphArc[] Next; internal byte[] Flags;
        }
        private readonly Page[][] directories;
        private long pageBytes;
        internal NavPackedFieldStorage(int capacity)
        {
            Capacity = capacity;
            int pages = (capacity + LabelsPerPage - 1) / LabelsPerPage;
            directories = new Page[(pages + PagesPerDirectory - 1) / PagesPerDirectory][];
        }
        internal int Capacity { get; }
        internal int Count { get; private set; }
        internal long EstimatedRetainedBytes => 2048L + 8L * directories.Length + pageBytes;
        private Page Get(int index, bool create)
        {
            int pageIndex = index / LabelsPerPage, directory = pageIndex / PagesPerDirectory;
            int within = pageIndex % PagesPerDirectory;
            var pages = directories[directory];
            if (pages == null) {
                if (!create) return null;
                pages = directories[directory] = new Page[PagesPerDirectory]; pageBytes += 2048;
            }
            var page = pages[within];
            if (page == null && create) { page = pages[within] = new Page(); pageBytes += 4096; }
            return page;
        }
        internal bool TryGet(int index, out double cost)
        {
            var page = Get(index, false); int offset = index % LabelsPerPage;
            cost = page == null ? 0 : page.Distance[offset];
            return page != null && (page.Flags[offset] & 1) != 0;
        }
        internal bool TryNext(int index, out NavGraphArc arc)
        {
            var page = Get(index, false); int offset = index % LabelsPerPage;
            arc = page == null ? default : page.Next[offset];
            return page != null && (page.Flags[offset] & 2) != 0;
        }
        internal void Set(int index, double cost, NavGraphArc? next)
        {
            var page = Get(index, true); int offset = index % LabelsPerPage;
            if ((page.Flags[offset] & 1) == 0) Count++;
            page.Flags[offset] |= 1; page.Distance[offset] = cost;
            if (next.HasValue) { page.Flags[offset] |= 2; page.Next[offset] = next.Value; }
        }
        internal SavedPage[] Save()
        {
            var result = new List<SavedPage>();
            for (int d = 0; d < directories.Length; d++) {
                var pages = directories[d]; if (pages == null) continue;
                for (int p = 0; p < pages.Length; p++) {
                    var page = pages[p]; if (page == null) continue;
                    result.Add(new SavedPage { Index = d * PagesPerDirectory + p,
                        Distance = (double[])page.Distance.Clone(), Next = (NavGraphArc[])page.Next.Clone(),
                        Flags = (byte[])page.Flags.Clone() });
                }
            }
            return result.ToArray();
        }
        internal void Restore(SavedPage[] saved, int expectedCount)
        {
            if (saved == null) throw new ArgumentException("Missing packed field pages.");
            foreach (var row in saved) {
                if (row.Index < 0 || (long)row.Index * LabelsPerPage >= Capacity ||
                    row.Distance.Length != LabelsPerPage || row.Next.Length != LabelsPerPage ||
                    row.Flags.Length != LabelsPerPage) throw new ArgumentException("Invalid packed field page.");
                int first = row.Index * LabelsPerPage;
                var page = Get(first, true);
                Array.Copy(row.Distance, page.Distance, LabelsPerPage);
                Array.Copy(row.Next, page.Next, LabelsPerPage);
                Array.Copy(row.Flags, page.Flags, LabelsPerPage);
                Count += row.Flags.Count(flag => (flag & 1) != 0);
            }
            if (Count != expectedCount) throw new ArgumentException("Packed field count mismatch.");
        }
    }
    // One immutable terminal-region artifact; labels are finite region candidates,
    // never assigned member slots. Independent subscriptions can reuse every label.
    public sealed class NavSharedTerminalField
    {
        private enum Phase { Seed, Search, Done }
        private readonly LayeredNavigationGraph graph; private readonly NavTerminalRegion region;
        private readonly Dictionary<long, double> distance = new Dictionary<long, double>();
        private readonly Dictionary<long, NavGraphArc> next = new Dictionary<long, NavGraphArc>();
        private readonly NavPackedFieldStorage packed;
        private readonly SortedSet<NavQueueEntry> frontier = new SortedSet<NavQueueEntry>();
        private readonly int maxLabels; private Phase phase; private int candidate, node, current = -1, arcCursor, currentCandidate;
        public NavSharedTerminalField(LayeredNavigationGraph graph, NavTerminalRegion region, int maxLabels)
        {
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph)); this.region = region ?? throw new ArgumentNullException(nameof(region));
            if (maxLabels < 1 || maxLabels > 1048576) throw new ArgumentOutOfRangeException(nameof(maxLabels)); this.maxLabels = maxLabels;
            Status = graph.IsValid(region.Requested) ? NavSolveStatus.Pending : NavSolveStatus.InvalidEndpoint;
            phase = Status == NavSolveStatus.Pending ? Phase.Seed : Phase.Done;
            if (graph.EstimatedRetainedBytes > 0) {
                long capacity = (long)graph.Nodes.Count * region.Candidates.Count;
                if (capacity > int.MaxValue) { Status = NavSolveStatus.CapacityExceeded; phase = Phase.Done; }
                else packed = new NavPackedFieldStorage((int)capacity);
            }
            StableKey = NavContract.Digest(w => { w.Write(graph.Binding.StableKey); w.Write(region.StableKey); });
        }
        public string StableKey { get; } public NavSolveStatus Status { get; private set; }
        public LayeredNavigationGraph Graph => graph; public NavTerminalRegion Region => region;
        public int LabelCount => packed != null ? packed.Count : distance.Count;
        public int FrontierCount => frontier.Count;
        public long EstimatedRetainedBytes => packed == null ?
            2048L + 512L * distance.Count + 256L * frontier.Count :
            packed.EstimatedRetainedBytes + 96L * frontier.Count;
        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            public string StableKey { get; internal set; }
            public NavSolveStatus Status { get; internal set; }
            internal int Phase, Candidate, Node, Current, ArcCursor, CurrentCandidate;
            internal KeyValuePair<long, double>[] Distance;
            internal KeyValuePair<long, NavGraphArc>[] Next;
            internal NavQueueEntry[] Frontier;
            internal NavPackedFieldStorage.SavedPage[] PackedPages;
            internal int PackedLabels;
        }
        public Checkpoint Save() => new Checkpoint {
            Version = 1, StableKey = StableKey, Status = Status, Phase = (int)phase,
            Candidate = candidate, Node = node, Current = current, ArcCursor = arcCursor,
            CurrentCandidate = currentCandidate, Distance = distance.ToArray(), Next = next.ToArray(),
            Frontier = frontier.ToArray(), PackedPages = packed?.Save(), PackedLabels = packed?.Count ?? 0
        };
        public static NavSharedTerminalField Restore(LayeredNavigationGraph graph, NavTerminalRegion region, int maxLabels, Checkpoint state)
        {
            var field = new NavSharedTerminalField(graph, region, maxLabels);
            if (state == null || state.Version != 1 || state.StableKey != field.StableKey ||
                state.Distance.Length > maxLabels || state.Frontier.Length > maxLabels ||
                (state.PackedPages == null) != (field.packed == null) ||
                state.Phase < 0 || state.Phase > (int)Phase.Done)
                throw new ArgumentException("Invalid or stale shared field checkpoint.");
            field.phase = (Phase)state.Phase; field.Status = state.Status;
            field.candidate = state.Candidate; field.node = state.Node; field.current = state.Current;
            field.arcCursor = state.ArcCursor; field.currentCandidate = state.CurrentCandidate;
            foreach (var pair in state.Distance) field.distance.Add(pair.Key, pair.Value);
            foreach (var pair in state.Next) field.next.Add(pair.Key, pair.Value);
            foreach (var item in state.Frontier) field.frontier.Add(item);
            if (field.packed != null) field.packed.Restore(state.PackedPages, state.PackedLabels);
            return field;
        }
        private static long Key(int n, int c) => ((long)c << 32) | (uint)n;
        private int Index(int n, int c) => c * graph.Nodes.Count + n;
        public bool TryGet(int n, int c, out double cost)
        { if (packed == null) return distance.TryGetValue(Key(n, c), out cost);
            return packed.TryGet(Index(n, c), out cost); }
        public bool TryNext(int n, int c, out NavGraphArc arc)
        { if (packed == null) return next.TryGetValue(Key(n, c), out arc);
            return packed.TryNext(Index(n, c), out arc); }
        private void Set(int n, int c, double cost, NavGraphArc? successor = null)
        {
            if (packed == null) {
                long key = Key(n, c); distance[key] = cost; if (successor.HasValue) next[key] = successor.Value;
            } else packed.Set(Index(n, c), cost, successor);
        }
        public NavWorkResult Advance(int quota)
        {
            if (quota < 0) throw new ArgumentOutOfRangeException(nameof(quota)); int used = 0;
            while (used < quota && Status == NavSolveStatus.Pending) { Step(); used++; }
            return new NavWorkResult(Status, used);
        }
        private void Step()
        {
            if (phase == Phase.Seed) {
                if (candidate == region.Candidates.Count) { phase = Phase.Search; return; }
                if (node == graph.Nodes.Count) { candidate++; node = 0; return; }
                if (node == 0 && !graph.IsValid(region.Candidates[candidate])) { Finish(NavSolveStatus.InvalidEndpoint); return; }
                int n = node++; NavGraphAttachment attachment;
                if (graph.TryAttach(region.Candidates[candidate], n, out attachment)) {
                    if (!TryGet(n, candidate, out _)) {
                        if (LabelCount == maxLabels) { Finish(NavSolveStatus.CapacityExceeded); return; }
                        Set(n, candidate, attachment.Cost);
                        frontier.Add(new NavQueueEntry { Cost = attachment.Cost, Node = n, Candidate = candidate });
                    }
                }
                return;
            }
            if (current < 0) {
                if (frontier.Count == 0) { Finish(LabelCount == 0 ? NavSolveStatus.BlockedConnector : NavSolveStatus.Ready); return; }
                var entry = frontier.Min; frontier.Remove(entry);
                if (!TryGet(entry.Node, entry.Candidate, out double known) || known != entry.Cost) return;
                current = entry.Node; currentCandidate = entry.Candidate; arcCursor = 0; return;
            }
            var incoming = graph.Incoming(current);
            if (arcCursor == incoming.Count) { current = -1; return; }
            var arc = incoming[arcCursor++];
            TryGet(current, currentCandidate, out double currentCost);
            double cost = currentCost + arc.Cost;
            if (!TryGet(arc.From, currentCandidate, out double old) || cost < old) {
                if (!TryGet(arc.From, currentCandidate, out _) && LabelCount == maxLabels) { Finish(NavSolveStatus.CapacityExceeded); return; }
                if (TryGet(arc.From, currentCandidate, out _)) frontier.Remove(new NavQueueEntry { Cost = old, Node = arc.From, Candidate = currentCandidate });
                Set(arc.From, currentCandidate, cost, arc);
                frontier.Add(new NavQueueEntry { Cost = cost, Node = arc.From, Candidate = currentCandidate });
            }
        }
        private void Finish(NavSolveStatus status) { Status = status; phase = Phase.Done; }
    }

    // Exact ingress and assigned-slot egress are subscriber work, not cache keys.
    public sealed class NavIncrementalSharedRoute
    {
        private enum Phase { Egress, Ingress, Select, Origin, Arcs, Terminal, Endpoint, Done }
        private readonly NavSharedTerminalField field; private readonly NavRouteSubscription subscription;
        private readonly Dictionary<int, double> egress = new Dictionary<int, double>(), ingress = new Dictionary<int, double>();
        private readonly List<NavTypedLeg> legs = new List<NavTypedLeg>();
        private Phase phase; private int cursor, candidate, chosenNode = -1, chosenCandidate = -1, walkNode, traversed, arcLeg;
        private double best = double.PositiveInfinity, routeCost;
        public NavIncrementalSharedRoute(NavSharedTerminalField field, NavRouteSubscription subscription)
        {
            this.field = field ?? throw new ArgumentNullException(nameof(field)); this.subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
            Status = field.Graph.IsValid(subscription.Origin) && field.Graph.IsValid(subscription.AssignedEndpoint) ? NavSolveStatus.Pending : NavSolveStatus.InvalidEndpoint;
            phase = Status == NavSolveStatus.Pending ? Phase.Egress : Phase.Done;
        }
        public NavSolveStatus Status { get; private set; } public NavTypedRoute Route { get; private set; }
        public int LegCount => legs.Count;
        public int StateCount => ingress.Count + egress.Count + legs.Count;
        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            public string FieldKey { get; internal set; }
            public string SubscriptionKey { get; internal set; }
            public NavSolveStatus Status { get; internal set; }
            internal int Phase, Cursor, Candidate, ChosenNode, ChosenCandidate, WalkNode, Traversed, ArcLeg;
            internal double Best, RouteCost;
            internal KeyValuePair<int, double>[] Egress, Ingress;
            internal NavTypedLeg[] Legs;
        }
        public Checkpoint Save() => new Checkpoint {
            Version = 1, FieldKey = field.StableKey, SubscriptionKey = subscription.StableKey,
            Status = Status, Phase = (int)phase, Cursor = cursor, Candidate = candidate,
            ChosenNode = chosenNode, ChosenCandidate = chosenCandidate, WalkNode = walkNode,
            Traversed = traversed, ArcLeg = arcLeg, Best = best, RouteCost = routeCost,
            Egress = egress.ToArray(), Ingress = ingress.ToArray(), Legs = legs.ToArray()
        };
        public static NavIncrementalSharedRoute Restore(NavSharedTerminalField field, NavRouteSubscription subscription, Checkpoint state)
        {
            var work = new NavIncrementalSharedRoute(field, subscription);
            if (state == null || state.Version != 1 || state.FieldKey != field.StableKey ||
                state.SubscriptionKey != subscription.StableKey || state.Phase < 0 || state.Phase > (int)Phase.Done)
                throw new ArgumentException("Invalid or stale shared connector checkpoint.");
            work.Status = state.Status; work.phase = (Phase)state.Phase; work.cursor = state.Cursor;
            work.candidate = state.Candidate; work.chosenNode = state.ChosenNode;
            work.chosenCandidate = state.ChosenCandidate; work.walkNode = state.WalkNode;
            work.traversed = state.Traversed; work.arcLeg = state.ArcLeg; work.best = state.Best; work.routeCost = state.RouteCost;
            foreach (var pair in state.Egress) work.egress.Add(pair.Key, pair.Value);
            foreach (var pair in state.Ingress) work.ingress.Add(pair.Key, pair.Value);
            work.legs.AddRange(state.Legs);
            if (work.Status == NavSolveStatus.Ready) work.Route = new NavTypedRoute(subscription, work.legs, work.routeCost);
            return work;
        }
        public NavWorkResult Advance(int quota)
        {
            if (quota < 0) throw new ArgumentOutOfRangeException(nameof(quota)); int used = 0;
            while (used < quota && Status == NavSolveStatus.Pending) {
                if (field.Status == NavSolveStatus.Pending) break;
                if (field.Status != NavSolveStatus.Ready) { Finish(field.Status); break; }
                Step(); used++;
            }
            return new NavWorkResult(Status, used);
        }
        private void Step()
        {
            var graph = field.Graph;
            if (phase == Phase.Egress) {
                if (cursor == field.Region.Candidates.Count) {
                    if (egress.Count == 0) { Finish(NavSolveStatus.BlockedConnector); return; }
                    phase = Phase.Ingress; cursor = 0; return;
                }
                var end = field.Region.Candidates[cursor];
                if (graph.CanSweep(end, subscription.AssignedEndpoint))
                    egress[cursor] = LayeredNavigationProvider.Distance(end.Position, subscription.AssignedEndpoint.Position) * graph.SurfaceCost(end.SurfaceId);
                cursor++; return;
            }
            if (phase == Phase.Ingress) {
                if (cursor == graph.Nodes.Count) {
                    if (ingress.Count == 0) { Finish(NavSolveStatus.BlockedConnector); return; }
                    phase = Phase.Select; cursor = 0; candidate = 0; return;
                }
                NavGraphAttachment a;
                if (graph.TryAttach(subscription.Origin, cursor, out a)) ingress[cursor] = a.Cost;
                cursor++; return;
            }
            if (phase == Phase.Select) {
                if (cursor == graph.Nodes.Count) {
                    if (chosenNode < 0) { Finish(NavSolveStatus.UnreachableInGraph); return; }
                    walkNode = chosenNode; phase = Phase.Origin; return;
                }
                if (candidate == field.Region.Candidates.Count) { cursor++; candidate = 0; return; }
                int c = candidate++;
                if (ingress.TryGetValue(cursor, out double start) && egress.TryGetValue(c, out double end) && field.TryGet(cursor, c, out double tail)) {
                    double total = start + tail + end;
                    if (total < best || total == best && (cursor < chosenNode || cursor == chosenNode && c < chosenCandidate)) {
                        best = total; chosenNode = cursor; chosenCandidate = c;
                    }
                }
                return;
            }
            if (phase == Phase.Origin) { Add(NavSolverLegs.Surface(graph, subscription.Origin, graph.Nodes[walkNode].Location)); phase = Phase.Arcs; return; }
            if (phase == Phase.Arcs) {
                if (field.TryNext(walkNode, chosenCandidate, out var arc)) {
                    if (arcLeg == 0 && traversed++ >= (long)graph.Nodes.Count * field.Region.Candidates.Count) throw new InvalidOperationException("Shared field cycle.");
                    var trace = NavSolverLegs.ExpandedArc(graph, arc);
                    Add(trace[arcLeg++]);
                    if (arcLeg == trace.Count) { arcLeg = 0; walkNode = arc.To; }
                } else phase = Phase.Terminal;
                return;
            }
            if (phase == Phase.Terminal) { Add(NavSolverLegs.Surface(graph, graph.Nodes[walkNode].Location, field.Region.Candidates[chosenCandidate])); phase = Phase.Endpoint; return; }
            if (phase == Phase.Endpoint) {
                Add(NavSolverLegs.Surface(graph, field.Region.Candidates[chosenCandidate], subscription.AssignedEndpoint));
                Route = new NavTypedRoute(subscription, legs, routeCost); Finish(NavSolveStatus.Ready);
            }
        }
        private void Add(NavTypedLeg leg) { legs.Add(leg); routeCost += leg.Cost; }
        private void Finish(NavSolveStatus status) { Status = status; phase = Phase.Done; }
    }
}

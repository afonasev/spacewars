using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Spacewars.Simulation
{
    public enum NavSolveStatus { Pending, Ready, InvalidEndpoint, BlockedConnector, UnreachableInGraph, CapacityExceeded, Stale }
    public enum NavTypedLegKind { Surface, Portal }
    public sealed class NavTypedLeg
    {
        internal NavTypedLeg(NavLocation from, NavLocation to, double cost, NavTypedLegKind kind, string portalId)
        { From = from; To = to; Cost = cost; Kind = kind; PortalId = portalId; }
        public NavLocation From { get; } public NavLocation To { get; } public double Cost { get; }
        public NavTypedLegKind Kind { get; } public string PortalId { get; }
    }
    public sealed class NavTypedRoute
    {
        internal NavTypedRoute(NavRouteSubscription subscription, List<NavTypedLeg> legs, double cost)
        { Subscription = subscription; Legs = new ReadOnlyCollection<NavTypedLeg>(legs); Cost = cost; }
        public NavRouteSubscription Subscription { get; } public IReadOnlyList<NavTypedLeg> Legs { get; } public double Cost { get; }
    }
    public sealed class NavWorkResult
    {
        internal NavWorkResult(NavSolveStatus status, int consumed) { Status = status; Consumed = consumed; }
        public NavSolveStatus Status { get; } public int Consumed { get; }
    }
    internal struct NavQueueEntry : IComparable<NavQueueEntry>
    {
        internal double Cost; internal int Node, Candidate;
        public int CompareTo(NavQueueEntry other)
        {
            int c = Cost.CompareTo(other.Cost); if (c != 0) return c;
            c = Node.CompareTo(other.Node); return c != 0 ? c : Candidate.CompareTo(other.Candidate);
        }
    }
    internal static class NavSolverLegs
    {
        internal static NavTypedLeg Surface(LayeredNavigationGraph graph, NavLocation from, NavLocation to)
        {
            if (!graph.CanSweep(from, to)) throw new InvalidOperationException("Unqualified surface leg.");
            return new NavTypedLeg(from, to, LayeredNavigationProvider.Distance(from.Position, to.Position) * graph.SurfaceCost(from.SurfaceId), NavTypedLegKind.Surface, null);
        }
        internal static NavTypedLeg Arc(LayeredNavigationGraph graph, NavGraphArc arc)
        {
            if (arc.Trace != null) throw new InvalidOperationException("Partition arc requires its complete typed trace.");
            var from = graph.Nodes[arc.From].Location; var to = graph.Nodes[arc.To].Location;
            if (arc.Kind == NavGraphArcKind.Surface && !graph.CanSweep(from, to)) throw new InvalidOperationException("Unqualified graph leg.");
            // Portal arcs are emitted only after qualification by the immutable graph compiler.
            return new NavTypedLeg(from, to, arc.Cost, arc.Kind == NavGraphArcKind.Portal ? NavTypedLegKind.Portal : NavTypedLegKind.Surface, arc.PortalId);
        }
        internal static IReadOnlyList<NavTypedLeg> ExpandedArc(LayeredNavigationGraph graph, NavGraphArc arc)
            => arc.Trace ?? new[] { Arc(graph, arc) };
    }
    // Pure singleton search. Advance charges each sweep, node attachment, frontier
    // pop, arc relaxation and reconstructed leg. Zero heuristic is admissible A*.
    public sealed class NavIncrementalSingletonRoute
    {
        private enum Phase { Direct, Origins, Endpoints, Search, Trace, Reconstruct, Done }
        private readonly LayeredNavigationGraph graph; private readonly NavRouteSubscription subscription;
        private readonly Dictionary<int, double> starts = new Dictionary<int, double>(), ends = new Dictionary<int, double>(), distance = new Dictionary<int, double>();
        private readonly Dictionary<int, NavGraphArc> previous = new Dictionary<int, NavGraphArc>();
        private readonly SortedSet<NavQueueEntry> frontier = new SortedSet<NavQueueEntry>();
        private readonly List<NavTypedLeg> legs = new List<NavTypedLeg>(); private readonly List<NavGraphArc> chain = new List<NavGraphArc>();
        private Phase phase; private int cursor, arcCursor, current = -1, chosen = -1, reconstructCursor, reconstructLeg, traceNode;
        private double best = double.PositiveInfinity, routeCost; private bool startSeeded;
        private readonly int maxStates;
        public NavIncrementalSingletonRoute(LayeredNavigationGraph graph, NavRouteSubscription subscription, int maxStates = int.MaxValue)
        {
            if (maxStates < 1) throw new ArgumentOutOfRangeException(nameof(maxStates)); this.maxStates = maxStates;
            this.graph = graph ?? throw new ArgumentNullException(nameof(graph)); this.subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
            Status = graph.IsValid(subscription.Origin) && graph.IsValid(subscription.AssignedEndpoint) ? NavSolveStatus.Pending : NavSolveStatus.InvalidEndpoint;
            phase = Status == NavSolveStatus.Pending ? Phase.Direct : Phase.Done;
        }
        public NavSolveStatus Status { get; private set; } public NavTypedRoute Route { get; private set; }
        public int FrontierCount => frontier.Count;
        public int LegCount => legs.Count;
        public int StateCount => starts.Count + ends.Count + distance.Count + previous.Count + chain.Count;
        public sealed class Checkpoint
        {
            public int Version { get; internal set; }
            public string GraphKey { get; internal set; }
            public string SubscriptionKey { get; internal set; }
            public NavSolveStatus Status { get; internal set; }
            internal int Phase, Cursor, ArcCursor, Current, Chosen, ReconstructCursor, ReconstructLeg, TraceNode;
            internal double Best, RouteCost;
            internal bool StartSeeded;
            internal KeyValuePair<int, double>[] Starts, Ends, Distance;
            internal KeyValuePair<int, NavGraphArc>[] Previous;
            internal NavQueueEntry[] Frontier;
            internal NavTypedLeg[] Legs;
            internal NavGraphArc[] Chain;
        }
        public Checkpoint Save() => new Checkpoint {
            Version = 1, GraphKey = graph.Binding.StableKey, SubscriptionKey = subscription.StableKey,
            Status = Status, Phase = (int)phase, Cursor = cursor, ArcCursor = arcCursor,
            Current = current, Chosen = chosen, ReconstructCursor = reconstructCursor, ReconstructLeg = reconstructLeg, TraceNode = traceNode,
            Best = best, RouteCost = routeCost, StartSeeded = startSeeded,
            Starts = starts.ToArray(), Ends = ends.ToArray(), Distance = distance.ToArray(),
            Previous = previous.ToArray(), Frontier = frontier.ToArray(), Legs = legs.ToArray(), Chain = chain.ToArray()
        };
        public static NavIncrementalSingletonRoute Restore(LayeredNavigationGraph graph, NavRouteSubscription subscription,
            Checkpoint state, int maxStates = int.MaxValue)
        {
            var work = new NavIncrementalSingletonRoute(graph, subscription, maxStates);
            if (state == null || state.Version != 1 || state.GraphKey != graph.Binding.StableKey ||
                state.SubscriptionKey != subscription.StableKey || state.Distance.Length > maxStates ||
                state.Frontier.Length > maxStates || state.Phase < 0 || state.Phase > (int)Phase.Done)
                throw new ArgumentException("Invalid or stale singleton checkpoint.");
            work.Status = state.Status; work.phase = (Phase)state.Phase; work.cursor = state.Cursor;
            work.arcCursor = state.ArcCursor; work.current = state.Current; work.chosen = state.Chosen;
            work.reconstructCursor = state.ReconstructCursor; work.reconstructLeg = state.ReconstructLeg; work.traceNode = state.TraceNode;
            work.best = state.Best; work.routeCost = state.RouteCost; work.startSeeded = state.StartSeeded;
            foreach (var pair in state.Starts) work.starts.Add(pair.Key, pair.Value);
            foreach (var pair in state.Ends) work.ends.Add(pair.Key, pair.Value);
            foreach (var pair in state.Distance) work.distance.Add(pair.Key, pair.Value);
            foreach (var pair in state.Previous) work.previous.Add(pair.Key, pair.Value);
            foreach (var entry in state.Frontier) work.frontier.Add(entry);
            work.legs.AddRange(state.Legs); work.chain.AddRange(state.Chain);
            if (work.Status == NavSolveStatus.Ready) work.Route = new NavTypedRoute(subscription, work.legs, work.routeCost);
            return work;
        }
        public NavWorkResult Advance(int quota)
        {
            if (quota < 0) throw new ArgumentOutOfRangeException(nameof(quota)); int used = 0;
            while (used < quota && Status == NavSolveStatus.Pending) { Step(); used++; }
            return new NavWorkResult(Status, used);
        }
        private void Step()
        {
            if (phase == Phase.Direct) {
                if (graph.CanSweep(subscription.Origin, subscription.AssignedEndpoint)) {
                    var direct = NavSolverLegs.Surface(graph, subscription.Origin, subscription.AssignedEndpoint);
                    Route = new NavTypedRoute(subscription, new List<NavTypedLeg> { direct }, direct.Cost); Finish(NavSolveStatus.Ready); return;
                }
                phase = Phase.Origins; return;
            }
            if (phase == Phase.Origins || phase == Phase.Endpoints) {
                if (cursor == graph.Nodes.Count) {
                    if (phase == Phase.Origins) { if (starts.Count == 0) { Finish(NavSolveStatus.BlockedConnector); return; } phase = Phase.Endpoints; cursor = 0; }
                    else { if (ends.Count == 0) { Finish(NavSolveStatus.BlockedConnector); return; } phase = Phase.Search; cursor = -1; }
                    return;
                }
                NavGraphAttachment a;
                var exact = phase == Phase.Origins ? subscription.Origin : subscription.AssignedEndpoint;
                if (graph.TryAttach(exact, cursor, out a)) (phase == Phase.Origins ? starts : ends)[cursor] = a.Cost;
                cursor++; return;
            }
            if (phase == Phase.Search) {
                if (!startSeeded) {
                    // Seed one origin per work unit, in stable node order.
                    cursor++;
                    if (cursor < graph.Nodes.Count) {
                        if (starts.TryGetValue(cursor, out double sourceCost)) {
                            if (distance.Count == maxStates) { Finish(NavSolveStatus.CapacityExceeded); return; }
                            distance[cursor] = sourceCost; frontier.Add(new NavQueueEntry { Cost = sourceCost, Node = cursor });
                        }
                        return;
                    }
                    startSeeded = true; cursor = 0; return;
                }
                if (current < 0) {
                    if (frontier.Count == 0 || frontier.Min.Cost > best) { if (chosen < 0) Finish(NavSolveStatus.UnreachableInGraph); else { traceNode = chosen; phase = Phase.Trace; } return; }
                    var entry = frontier.Min; frontier.Remove(entry);
                    if (!distance.TryGetValue(entry.Node, out double known) || known != entry.Cost) return;
                    current = entry.Node; arcCursor = 0;
                    if (ends.TryGetValue(current, out double egress)) {
                        double candidate = known + egress;
                        if (candidate < best || candidate == best && current < chosen) { best = candidate; chosen = current; }
                    }
                    return;
                }
                var arcs = graph.Outgoing(current);
                if (arcCursor == arcs.Count) { current = -1; return; }
                var arc = arcs[arcCursor++]; double nextCost = distance[current] + arc.Cost;
                if (!distance.TryGetValue(arc.To, out double old) || nextCost < old) {
                    if (!distance.ContainsKey(arc.To) && distance.Count == maxStates) { Finish(NavSolveStatus.CapacityExceeded); return; }
                    if (distance.ContainsKey(arc.To)) frontier.Remove(new NavQueueEntry { Cost = old, Node = arc.To });
                    distance[arc.To] = nextCost; previous[arc.To] = arc; frontier.Add(new NavQueueEntry { Cost = nextCost, Node = arc.To });
                }
                return;
            }
            if (phase == Phase.Trace) {
                if (previous.TryGetValue(traceNode, out var predecessor)) { chain.Add(predecessor); traceNode = predecessor.From; }
                else phase = Phase.Reconstruct;
                return;
            }
            if (phase == Phase.Reconstruct) {
                if (reconstructCursor == 0) {
                    int first = chain.Count == 0 ? chosen : chain[chain.Count - 1].From;
                    Add(NavSolverLegs.Surface(graph, subscription.Origin, graph.Nodes[first].Location));
                } else if (reconstructCursor <= chain.Count) {
                    var trace = NavSolverLegs.ExpandedArc(graph, chain[chain.Count - reconstructCursor]);
                    Add(trace[reconstructLeg++]);
                    if (reconstructLeg < trace.Count) return;
                    reconstructLeg = 0;
                }
                else { Add(NavSolverLegs.Surface(graph, graph.Nodes[chosen].Location, subscription.AssignedEndpoint)); Route = new NavTypedRoute(subscription, legs, routeCost); Finish(NavSolveStatus.Ready); }
                reconstructCursor++;
            }
        }
        private void Add(NavTypedLeg leg) { legs.Add(leg); routeCost += leg.Cost; }
        private void Finish(NavSolveStatus status) { Status = status; phase = Phase.Done; }
    }
}

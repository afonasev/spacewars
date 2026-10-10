using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    internal sealed class NavPagedList<T> : IReadOnlyList<T>
    {
        private const int PageSize = 256;
        private readonly List<T[]> pages = new List<T[]>();
        internal int Capacity => pages.Count * PageSize;
        public int Count { get; private set; }
        public T this[int index]
        {
            get { if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                return pages[index / PageSize][index % PageSize]; }
        }
        internal void Add(T value)
        {
            if (Count == Capacity) pages.Add(new T[PageSize]);
            pages[Count / PageSize][Count % PageSize] = value; Count++;
        }
        internal void Set(int index, T value)
        { if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
          pages[index / PageSize][index % PageSize] = value; }
        internal void AddRange(IEnumerable<T> values) { foreach (var value in values) Add(value); }
        internal T[] ToArray() { var result = new T[Count]; for (int i = 0; i < Count; i++) result[i] = this[i]; return result; }
        public IEnumerator<T> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    // One 16 KiB slot page is the largest fixed array charged to a sampling step.
    // Zero is absent; stored indices are offset by one.
    internal sealed class NavPackedSlots
    {
        internal const int PageSize = 4096;
        private readonly List<int[]> pages = new List<int[]>();
        internal int CapacitySlots => pages.Count * PageSize;
        internal long EstimatedRetainedBytes => 4L * CapacitySlots + 16L * pages.Capacity;
        internal void Ensure(int slots)
        {
            if (slots > CapacitySlots + PageSize) throw new InvalidOperationException("Unbudgeted packed slot expansion.");
            if (slots > CapacitySlots) pages.Add(new int[PageSize]);
        }
        internal int Get(int slot) => pages[slot / PageSize][slot % PageSize];
        internal void Set(int slot, int value) => pages[slot / PageSize][slot % PageSize] = value;
        internal int[][] Save() => pages.Select(p => (int[])p.Clone()).ToArray();
        internal static NavPackedSlots Restore(int[][] saved, int maximumSlots)
        {
            if (saved == null || saved.Length * PageSize > maximumSlots + PageSize ||
                saved.Any(p => p == null || p.Length != PageSize)) throw new ArgumentException("Invalid packed slot pages.");
            var result = new NavPackedSlots();
            foreach (var page in saved) result.pages.Add((int[])page.Clone());
            return result;
        }
    }
    // The authored partition has one canonical location at each .8 m grid
    // sample. Cardinal arcs carry an exact provider trace where they cross
    // seams. No per-support erosion, invented portal or coarser grid is used.
    internal sealed class PartitionNavigationCompileWork
    {
        internal enum Stage { Bounds, Samples, Arcs, Components, Ready }
        private readonly PartitionNavigationProvider provider;
        private readonly NavClearanceProfile clearance;
        private readonly NavGraphProfile profile;
        private readonly NavGraphCompileLimits limits;
        private readonly NavPagedList<NavLocation> locations = new NavPagedList<NavLocation>();
        private readonly NavPagedList<int> components = new NavPagedList<int>();
        private readonly NavPagedList<int> arcSlots = new NavPagedList<int>();
        private readonly NavPagedList<IReadOnlyList<NavTypedLeg>> traces = new NavPagedList<IReadOnlyList<NavTypedLeg>>();
        private NavPackedSlots grid = new NavPackedSlots(), neighbors = new NavPackedSlots(), incoming = new NavPackedSlots();
        private NavPackedSlots traceSlots = new NavPackedSlots();
        private long traceBytes;
        private int min, max, width, x, z, node, direction, componentCursor;
        private long sampled;
        internal Stage Phase { get; private set; } = Stage.Bounds;
        internal int Consumed { get; private set; }
        internal int SampleCount => locations.Count;
        internal int ArcCount => arcSlots.Count;
        internal LayeredNavigationGraph Graph { get; private set; }
        internal long EstimatedRetainedBytes => 4096L + 32L * locations.Capacity +
            4L * components.Capacity + 4L * arcSlots.Capacity + neighbors.EstimatedRetainedBytes +
            incoming.EstimatedRetainedBytes + traceSlots.EstimatedRetainedBytes +
            8L * traces.Capacity + (grid?.EstimatedRetainedBytes ?? 0) + traceBytes;

        internal PartitionNavigationCompileWork(PartitionNavigationProvider provider, NavClearanceProfile clearance,
            NavGraphProfile profile, NavGraphCompileLimits limits)
        {
            this.provider = provider; this.clearance = clearance; this.profile = profile; this.limits = limits;
        }
        private int GridIndex(int gx, int gz) => (gx - min) * width + (gz - min);
        private int Root(int n)
        {
            while (components[n] != n) { components.Set(n, components[components[n]]); n = components[n]; }
            return n;
        }
        internal int Advance(int quota)
        {
            if (quota < 0) throw new ArgumentOutOfRangeException(nameof(quota));
            int used = 0;
            while (used < quota && Phase != Stage.Ready) { Step(); used++; Consumed++; }
            return used;
        }
        private void Step()
        {
            if (Phase == Stage.Bounds) {
                double bound = provider.HalfExtent / profile.GridCell;
                min = (int)Math.Ceiling(-bound); max = (int)Math.Floor(bound);
                sampled = (long)(max - min + 1) * (max - min + 1);
                if (sampled > limits.MaxSamples) throw new ArgumentException("Graph sampling capacity exceeded.");
                width = max - min + 1; x = min; z = min; Phase = Stage.Samples; return;
            }
            if (Phase == Stage.Samples) {
                int gridIndex = GridIndex(x, z);
                grid.Ensure(gridIndex + 1);
                var point = new NavPoint(x * profile.GridCell, z * profile.GridCell);
                if (provider.TryLocate(point, clearance, out var location)) {
                    if (locations.Count >= limits.MaxNodes) throw new ArgumentException("Graph node capacity exceeded.");
                    int id = locations.Count; locations.Add(location);
                    components.Add(id); grid.Set(gridIndex, id + 1);
                    neighbors.Ensure((id + 1) * 4); incoming.Ensure((id + 1) * 4);
                    traceSlots.Ensure((id + 1) * 4);
                }
                if (++z > max) { z = min; x++; }
                if (x > max) Phase = Stage.Arcs;
                return;
            }
            if (Phase == Stage.Arcs) {
                if (node == locations.Count) { Phase = Stage.Components; return; }
                int dir = direction++, from = node;
                if (direction == 4) { direction = 0; node++; }
                int gx = (int)Math.Round(locations[from].Position.X / profile.GridCell);
                int gz = (int)Math.Round(locations[from].Position.Z / profile.GridCell);
                int nx = gx + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                int nz = gz + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                if (nx < min || nx > max || nz < min || nz > max) return;
                int to = grid.Get(GridIndex(nx, nz)) - 1;
                if (to < 0) return;
                var start = locations[from]; var end = locations[to];
                if (!provider.TryTrace(start, end.Position, clearance, out var reached, out var trace) ||
                    !reached.Equals(end)) return;
                if (arcSlots.Count >= limits.MaxArcs) throw new ArgumentException("Graph arc capacity exceeded.");
                int slot = from * 4 + dir;
                neighbors.Set(slot, to + 1);
                incoming.Set(to * 4 + (dir ^ 1), slot + 1);
                arcSlots.Add(slot);
                if (trace.Any(l => l.Kind == NavTypedLegKind.Portal)) {
                    traces.Add(trace); traceSlots.Set(slot, traces.Count); traceBytes += 128L + 96L * trace.Count;
                }
                int a = Root(from), b = Root(to);
                if (a != b) components.Set(Math.Max(a, b), Math.Min(a, b));
                return;
            }
            if (Phase == Stage.Components) {
                if (componentCursor < components.Count) { components.Set(componentCursor, Root(componentCursor)); componentCursor++; return; }
                grid = null;
                Graph = new LayeredNavigationGraph(provider, new NavGraphBinding(provider.Binding, clearance, profile),
                    locations, components, neighbors, incoming, arcSlots, traceSlots, traces, traceBytes);
                Phase = Stage.Ready;
            }
        }

        internal sealed class Checkpoint
        {
            internal string Binding; internal Stage Phase; internal int Consumed, Min, Max, Width, X, Z, Node, Direction, ComponentCursor;
            internal long Sampled; internal NavLocation[] Locations;
            internal int[] Components, ArcSlots;
            internal int[][] Grid, Neighbors, Incoming, TraceSlots;
            internal long TraceBytes;
            internal NavTypedLeg[][] Traces;
        }
        internal Checkpoint Save() => new Checkpoint {
            Binding = new NavGraphBinding(provider.Binding, clearance, profile).StableKey,
            Phase = Phase, Consumed = Consumed, Min = min, Max = max, Width = width, X = x, Z = z,
            Node = node, Direction = direction, ComponentCursor = componentCursor, Sampled = sampled,
            Locations = locations.ToArray(), Components = components.ToArray(), Grid = grid?.Save(),
            Neighbors = neighbors.Save(), Incoming = incoming.Save(), ArcSlots = arcSlots.ToArray(),
            TraceSlots = traceSlots.Save(),
            TraceBytes = traceBytes,
            Traces = traces.Select(t => t.ToArray()).ToArray()
        };
        internal static PartitionNavigationCompileWork Restore(PartitionNavigationProvider provider,
            NavClearanceProfile clearance, NavGraphProfile profile, NavGraphCompileLimits limits, Checkpoint saved)
        {
            if (saved == null || saved.Binding != new NavGraphBinding(provider.Binding, clearance, profile).StableKey ||
                saved.Locations.Length > limits.MaxNodes || saved.ArcSlots.Length > limits.MaxArcs ||
                saved.Locations.Length != saved.Components.Length ||
                saved.Neighbors.Length != saved.Incoming.Length ||
                saved.Phase < Stage.Bounds || saved.Phase > Stage.Ready)
                throw new ArgumentException("Invalid partition compile checkpoint.");
            var work = new PartitionNavigationCompileWork(provider, clearance, profile, limits) {
                Phase = saved.Phase, Consumed = saved.Consumed, min = saved.Min, max = saved.Max, width = saved.Width,
                x = saved.X, z = saved.Z, node = saved.Node, direction = saved.Direction,
                componentCursor = saved.ComponentCursor, sampled = saved.Sampled, traceBytes = saved.TraceBytes
            };
            work.locations.AddRange(saved.Locations);
            work.components.AddRange(saved.Components); work.arcSlots.AddRange(saved.ArcSlots);
            work.neighbors = NavPackedSlots.Restore(saved.Neighbors, limits.MaxNodes * 4);
            work.incoming = NavPackedSlots.Restore(saved.Incoming, limits.MaxNodes * 4);
            work.traceSlots = NavPackedSlots.Restore(saved.TraceSlots, limits.MaxNodes * 4);
            foreach (var t in saved.Traces) work.traces.Add(Array.AsReadOnly(t));
            if (saved.Phase != Stage.Ready) work.grid = NavPackedSlots.Restore(saved.Grid, limits.MaxSamples);
            else {
                work.grid = null;
                work.Graph = new LayeredNavigationGraph(provider, new NavGraphBinding(provider.Binding, clearance, profile),
                    work.locations, work.components, work.neighbors, work.incoming,
                    work.arcSlots, work.traceSlots, work.traces, work.traceBytes);
            }
            return work;
        }
    }
}

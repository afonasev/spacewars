using System;
using System.Collections.Generic;

namespace Spacewars.Simulation
{
    public sealed class SharedFlowRouter
    {
        private struct Cell : IEquatable<Cell> { public readonly int X, Z; public Cell(int x, int z) { X = x; Z = z; } public bool Equals(Cell other) { return X == other.X && Z == other.Z; } public override bool Equals(object other) { return other is Cell && Equals((Cell)other); } public override int GetHashCode() { return X * 486187739 ^ Z; } }
        private sealed class Field { public readonly Dictionary<Cell, int> Distance = new Dictionary<Cell, int>(); }
        private readonly NavGeometry geometry; private readonly NavigationProfile profile; private readonly Dictionary<string, Field> fields = new Dictionary<string, Field>();
        public SharedFlowRouter(NavGeometry geometry, NavigationProfile profile) { if (geometry == null || profile == null) throw new ArgumentNullException(); this.geometry = geometry; this.profile = profile; }
        public int RebuildCount { get; private set; }
        public int FieldCacheHits {get;private set;}
        public NavPoint[] FindPath(NavPoint start, NavPoint goal)
        {
            if (!geometry.IsFree(start, profile.Radius) || !geometry.IsFree(goal, profile.Radius)) return new NavPoint[0];
            if (geometry.SegmentFree(start, goal, profile.Radius)) return new[] { goal };
            Cell from = ToCell(start), target = ToCell(goal);
            // Retain the checked floor fast path and its shared field/cache behavior.
            if (Attached(start, from) && Attached(goal, target))
            {
                var directField = GetField(target);
                if (directField.Distance.ContainsKey(from)) return Route(start, goal, from, directField);
            }
            var starts = Attachments(start); var goals = Attachments(goal);
            if (starts.Count == 0 || goals.Count == 0) return new NavPoint[0];
            Cell bestStart = default(Cell); Field bestField = null;
            double bestCost = double.PositiveInfinity;
            // A single multi-source field considers every attached goal component. This avoids
            // rebuilding the same arena once per alternative while retaining disconnected roots.
            Field field = GetField(goals);
            foreach (Cell begin in starts)
            {
                int distance;
                if (!field.Distance.TryGetValue(begin, out distance)) continue;
                double cost = distance * profile.GridCell + Length(start, ToPoint(begin));
                if (cost >= bestCost) continue; // Stable stencil order breaks exact ties.
                bestCost = cost; bestStart = begin; bestField = field;
            }
            return bestField == null ? new NavPoint[0] : Route(start, goal, bestStart, bestField);
        }
        private bool Attached(NavPoint endpoint, Cell cell) => IsFree(cell) && geometry.SegmentFree(endpoint, ToPoint(cell), profile.Radius);
        private List<Cell> Attachments(NavPoint endpoint)
        {
            Cell floor = ToCell(endpoint); var result = new List<Cell>();
            if (Attached(endpoint, floor)) result.Add(floor);
            // One adjacent-cell ring (3x3) is a technical grid attachment invariant, not a
            // gameplay search radius. Never project endpoints or reduce the typed footprint.
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && z == 0) continue;
                var cell = new Cell(floor.X + x, floor.Z + z);
                if (Attached(endpoint, cell)) result.Add(cell);
            }
            return result;
        }
        private static double Length(NavPoint a, NavPoint b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        private NavPoint[] Route(NavPoint start, NavPoint goal, Cell from, Field field)
        {
            var raw = new List<NavPoint>(); Cell current = from; raw.Add(start); raw.Add(ToPoint(from));
            while (field.Distance[current] != 0) { Cell next = BestNeighbor(field, current); if (next.Equals(current)) return new NavPoint[0]; current = next; raw.Add(ToPoint(current)); }
            raw.Add(goal); var route = Smooth(raw); var previous = start;
            foreach (var point in route)
            {
                if (!geometry.IsFree(point, profile.Radius) || !geometry.SegmentFree(previous, point, profile.Radius)) return new NavPoint[0];
                previous = point;
            }
            return route;
        }
        private Field GetField(Cell target) => GetField(new[] { target });
        private Field GetField(IReadOnlyList<Cell> targets)
        {
            string key = geometry.Revision + ":" + profile.Radius;
            foreach (Cell target in targets) key += ":" + target.X + ":" + target.Z;
            Field field; if (fields.TryGetValue(key, out field)){FieldCacheHits++;return field;}
            field = new Field(); var queue = new Queue<Cell>();
            foreach (Cell target in targets) { field.Distance[target] = 0; queue.Enqueue(target); }
            while (queue.Count > 0) { Cell cell = queue.Dequeue(); int d = field.Distance[cell]; foreach (Cell next in Neighbors(cell)) if (IsFree(next) && geometry.SegmentFree(ToPoint(cell), ToPoint(next), profile.Radius) && !field.Distance.ContainsKey(next)) { field.Distance[next] = d + 1; queue.Enqueue(next); } }
            fields[key] = field; RebuildCount++; return field;
        }
        private Cell BestNeighbor(Field field, Cell current) { int best; if (!field.Distance.TryGetValue(current, out best)) return current; Cell result = current; foreach (Cell next in Neighbors(current)) { int d; if (field.Distance.TryGetValue(next, out d) && d < best && geometry.SegmentFree(ToPoint(current), ToPoint(next), profile.Radius)) { best = d; result = next; } } return result; }
        private IEnumerable<Cell> Neighbors(Cell cell) { yield return new Cell(cell.X, cell.Z + 1); yield return new Cell(cell.X + 1, cell.Z); yield return new Cell(cell.X, cell.Z - 1); yield return new Cell(cell.X - 1, cell.Z); }
        private Cell ToCell(NavPoint point) { return new Cell((int)Math.Floor((point.X + geometry.HalfExtent) / profile.GridCell), (int)Math.Floor((point.Z + geometry.HalfExtent) / profile.GridCell)); }
        private NavPoint ToPoint(Cell cell) { return new NavPoint(-geometry.HalfExtent + (cell.X + .5d) * profile.GridCell, -geometry.HalfExtent + (cell.Z + .5d) * profile.GridCell); }
        private bool IsFree(Cell cell) { NavPoint point = ToPoint(cell); return point.X >= -geometry.HalfExtent && point.X <= geometry.HalfExtent && point.Z >= -geometry.HalfExtent && geometry.IsFree(point, profile.Radius); }
        private NavPoint[] Smooth(List<NavPoint> raw) { var result = new List<NavPoint>(); int anchor = 0; while (anchor < raw.Count - 1) { int chosen = anchor + 1; for (int next = chosen + 1; next < raw.Count; next++) if (geometry.SegmentFree(raw[anchor], raw[next], profile.Radius)) chosen = next; else break; result.Add(raw[chosen]); anchor = chosen; } return result.ToArray(); }
    }
}

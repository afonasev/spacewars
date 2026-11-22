using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    internal static class NavFormationLayout
    {
        internal readonly struct Member
        {
            internal readonly int Id, Rank;
            internal readonly PlayableEntityKind Kind;
            internal readonly NavPoint Position;
            internal readonly double Radius;
            internal Member(int id, PlayableEntityKind kind, NavPoint position, double radius)
            {
                Id = id;
                Kind = kind;
                Position = position;
                Radius = radius;
                Rank = kind == PlayableEntityKind.Explorer || kind == PlayableEntityKind.Hawk ? 0 :
                    kind == PlayableEntityKind.Tank || kind == PlayableEntityKind.Skat ? 1 : 2;
            }
        }

        internal static NavPoint Direction(NavPoint from, NavPoint to) =>
            TryDirection(from, to, 1e-9, out var direction) ? direction : new NavPoint(1, 0);

        internal static bool TryDirection(NavPoint from, NavPoint to, double minimum, out NavPoint direction)
        {
            double x = to.X - from.X, z = to.Z - from.Z;
            double length = Math.Sqrt(x * x + z * z);
            direction = length > minimum ? new NavPoint(x / length, z / length) : default;
            return length > minimum;
        }

        internal static NavPoint DirectionFromHeadings(IEnumerable<double> headings)
        {
            double x = 0, z = 0;
            foreach (double heading in headings)
            {
                x += Math.Cos(heading);
                z += Math.Sin(heading);
            }
            return Direction(default, new NavPoint(x, z));
        }

        internal static double Dot(NavPoint a, NavPoint b) => a.X * b.X + a.Z * b.Z;
        internal static NavPoint Side(NavPoint direction) => new NavPoint(-direction.Z, direction.X);
        internal static NavPoint At(NavPoint center, NavPoint direction, double ahead, double side)
            => new NavPoint(center.X + direction.X * ahead - direction.Z * side,
                center.Z + direction.Z * ahead + direction.X * side);

        internal static NavPoint[] Offsets(Member[] members, double pitch, NavPoint direction)
        {
            var result = new NavPoint[members.Length];
            var side = Side(direction);
            int width = (int)Math.Ceiling(Math.Sqrt(members.Length));
            var rows = new List<int[]>();
            var all = Enumerable.Range(0, members.Length);
            foreach (var rank in all.Where(i => members[i].Kind != PlayableEntityKind.Skat && members[i].Kind != PlayableEntityKind.Cerberus)
                .GroupBy(i => members[i].Rank).OrderBy(g => g.Key))
                rows.AddRange(Rows(rank));
            rows.InsertRange(Math.Min(1, rows.Count), Rows(all.Where(i => members[i].Kind == PlayableEntityKind.Skat)));

            var front = all.Where(i => members[i].Kind == PlayableEntityKind.Cerberus)
                .OrderBy(i => Dot(members[i].Position, side)).ThenBy(i => members[i].Id).ToArray();
            if (front.Length > 0)
                rows.Insert(0, front);

            IEnumerable<int[]> Rows(IEnumerable<int> pool)
            {
                var indices = pool.OrderByDescending(i => Dot(members[i].Position, direction)).ThenBy(i => members[i].Id).ToArray();
                for (int first = 0; first < indices.Length; first += width)
                {
                    yield return indices.Skip(first).Take(width)
                        .OrderBy(i => Dot(members[i].Position, side)).ThenBy(i => members[i].Id).ToArray();
                }
            }
            for (int row = 0; row < rows.Count; row++)
            {
                // Stagger alternating dense rows so the compact footprint still
                // leaves each approaching rank a separate terminal lane.
                double stagger = rows.Count > 2 && rows[row].Length > 1 ? (row % 2 == 0 ? -.25 : .25) * pitch : 0;
                double spacing = pitch;
                if (row == 0 && front.Length > 0)
                {
                    stagger = 0;
                    if (front.Length > 1)
                        spacing = pitch * Math.Max(width - 1, front.Length - 1) / (front.Length - 1);
                }
                for (int column = 0; column < rows[row].Length; column++)
                    result[rows[row][column]] = new NavPoint(((rows.Count - 1) * .5 - row) * pitch,
                        (column - (rows[row].Length - 1) * .5) * spacing + stagger);
            }
            return result;
        }

        // Try whole formations shifted around the click before giving individual
        // rows extra room. Reserve front places first along parallel approaches.
        internal static NavPoint[] Destinations(Member[] members, NavPoint target, NavPoint direction, double pitch,
            double extent, Func<int, NavPoint, bool> clear, Func<int, NavPoint, int, NavPoint, bool> compatible,
            NavPoint[] preferredOffsets = null, (NavPoint Center, double Radius)? objective = null)
        {
            var offsets = preferredOffsets ?? Offsets(members, pitch, direction);
            var order = Enumerable.Range(0, members.Length).OrderByDescending(i => offsets[i].X)
                .ThenBy(i => Math.Abs(offsets[i].Z)).ThenBy(i => members[i].Id).ToArray();
            var points = new NavPoint[members.Length];
            bool Place(int i, NavPoint point, int placed)
            {
                if (!clear(i, point)) return false;
                for (int j = 0; j < placed; j++)
                    if (!compatible(i, point, order[j], points[order[j]])) return false;
                points[i] = point;
                return true;
            }

            NavPoint PreferredPoint(int i, NavPoint center) => At(center, direction, offsets[i].X, offsets[i].Z);

            if (objective is { } zone)
            {
                // Start on the requested (threat-facing) side. Shift whole layouts
                // inward when needed, then reserve the circle before placing overflow.
                double step = pitch * .5;
                int localRings = Math.Max(1, (int)Math.Ceiling(2 * zone.Radius / step));
                bool Inside(NavPoint point) => SquaredDistance(point, zone.Center) <= zone.Radius * zone.Radius;
                var places = RingOffsets(localRings, step)
                    .Select(shift => At(target, direction, shift.X, shift.Z))
                    .Where(Inside).ToArray();
                foreach (var center in places)
                {
                    int placed = 0;
                    while (placed < order.Length)
                    {
                        int i = order[placed];
                        var point = PreferredPoint(i, center);
                        if (!Inside(point) || !Place(i, point, placed)) break;
                        placed++;
                    }
                    if (placed == order.Length) return points;
                }

                var remaining = new List<int>(order.Length);
                int inside = 0;
                var preferredOrder = order.ToArray();
                foreach (int i in preferredOrder)
                {
                    var preferred = PreferredPoint(i, target);
                    bool found = false;
                    foreach (var point in places.OrderBy(point => SquaredDistance(point, preferred)))
                    {
                        if (!Place(i, point, inside)) continue;
                        order[inside++] = i;
                        found = true;
                        break;
                    }
                    if (!found) remaining.Add(i);
                }
                if (inside > 0)
                {
                    remaining.CopyTo(order, inside);
                    var resolved = PlaceRemaining(inside);
                    if (resolved.Length != 0) return resolved;
                    order = preferredOrder;
                }
            }

            // Bound the whole-layout search. On tight terrain switch to reachable
            // individual places so a single obstructed row cannot reject the order.
            int rings = Math.Max(1, (int)Math.Ceiling(2 * extent / pitch));
            foreach (var shift in RingOffsets(Math.Min(2, rings), pitch * .5))
            {
                var center = At(target, direction, shift.X, shift.Z);
                int placed = 0;
                while (placed < order.Length && Place(order[placed], PreferredPoint(order[placed], center), placed)) placed++;
                if (placed == order.Length) return points;
            }
            return PlaceRemaining(0);

            NavPoint[] PlaceRemaining(int start)
            {
                int maxRings = Math.Max(1, (int)Math.Ceiling(2 * extent / pitch));
                for (int placed = start; placed < order.Length; placed++)
                {
                    int i = order[placed];
                    bool found = false;
                    foreach (var shift in RingOffsets(maxRings, pitch))
                    {
                        if (!Place(i, PreferredPoint(i, At(target, direction, shift.X, shift.Z)), placed)) continue;
                        found = true;
                        break;
                    }
                    if (!found) return Array.Empty<NavPoint>();
                }
                return points;
            }
        }

        internal static double SquaredDistance(NavPoint a, NavPoint b)
        {
            double x = a.X - b.X, z = a.Z - b.Z;
            return x * x + z * z;
        }

        private static IEnumerable<NavPoint> RingOffsets(int limit, double step)
        {
            yield return default;
            for (int ring = 1; ring <= limit; ring++)
            {
                // Use side shifts before shifts along the approach at equal distance.
                for (int along = 0; along <= ring; along++)
                {
                    for (int side = 0; side <= ring; side++)
                    {
                        if (along != ring && side != ring) continue;
                        for (int a = along == 0 ? 1 : -1; a <= 1; a += 2)
                            for (int s = side == 0 ? 1 : -1; s <= 1; s += 2)
                                yield return new NavPoint(a * along * step, s * side * step);
                    }
                }
            }
        }

        internal static double Gap(NavigationProfile profile) => 2 * profile.ArrivalTolerance;
        internal static bool Separated(NavPoint a, NavPoint b, double spacing)
            => SquaredDistance(a, b) >= spacing * spacing - 1e-9;
    }
}

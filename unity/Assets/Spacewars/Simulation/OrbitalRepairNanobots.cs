using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    public readonly struct OrbitalRepairRim
    {
        public readonly NavLocation From, To;
        public readonly int Segment;
        public const int SegmentCount = 96;

        public OrbitalRepairRim(NavLocation from, NavLocation to, int segment)
        {
            From = from;
            To = to;
            Segment = segment;
        }
    }

    public static class OrbitalRepairNanobots
    {
        public const double DefaultRadius = 5, DefaultSeconds = 10, DefaultHealthPerSecond = 5;
        public const int MinimumWoundedForBot = 3;

        public static bool TryLocate(PlayableProfile profile, NavPoint point, NavLocation? requested, out NavLocation location)
        {
            location = default;
            if (!(Math.Abs(point.X) <= profile.ArenaHalfExtent && Math.Abs(point.Z) <= profile.ArenaHalfExtent))
                return false;
            var map = profile.AuthoredMap;
            if (requested.HasValue)
            {
                location = requested.Value;
                return location.Position.Equals(point) && (map == null ? location.SurfaceId == NavLocation.FlatSurface :
                    map.IsValidLocation(location, 0));
            }
            if (map != null) return map.TryLocate(point, 0, out location);
            location = new NavLocation(point, NavLocation.FlatSurface);
            return true;
        }

        public static double Height(IPlayableTerrain map, NavLocation location)
        {
            if (map == null) return 0;
            foreach (var support in map.Supports)
                if (support.SurfaceId == location.SurfaceId && support.Contains(location.Position))
                    return support.HeightAt(location.Position);
            return map.SurfaceHeight(location.Position);
        }

        public static bool Contains(IPlayableTerrain map, NavLocation center, double radius, NavPoint point,
            NavLocation? ground, double? airHeight)
        {
            double x = point.X - center.Position.X;
            double z = point.Z - center.Position.Z;
            double distance = x * x + z * z;
            if (distance > radius * radius) return false;
            if (airHeight.HasValue)
            {
                double height = airHeight.Value - Height(map, center);
                return height >= 0 && distance + height * height <= radius * radius;
            }
            if (map == null) return true;
            if (!ground.HasValue)
            {
                if (!map.TryLocate(point, 0, out var resolved)) return false;
                ground = resolved;
            }
            if (center.SurfaceId == ground.Value.SurfaceId) return true;
            var origin = map.Supports.FirstOrDefault(support =>
                support.SurfaceId == center.SurfaceId && support.Contains(center.Position));
            var destination = map.Supports.FirstOrDefault(support =>
                support.SurfaceId == ground.Value.SurfaceId && support.Contains(ground.Value.Position));
            if (origin == null || destination == null) return map.CompatibleCombatSurface(center,ground.Value);
            bool ramp = origin.Gradient.X != 0 || origin.Gradient.Z != 0 ||
                destination.Gradient.X != 0 || destination.Gradient.Z != 0;
            if (ramp) return map.SurfaceTransitions.Any(transition =>
                transition.From == center.SurfaceId && transition.To == ground.Value.SurfaceId ||
                transition.To == center.SurfaceId && transition.From == ground.Value.SurfaceId);
            return Math.Abs(origin.Height - destination.Height) < 1e-6 &&
                map.CompatibleCombatSurface(center,ground.Value);
        }

        public static bool Wounded(PlayableProfile profile, PlayableEntitySnapshot unit) =>
            OrbitalStasis.Eligible(unit) && PlayableUnitRules.HealthRatio(profile, unit) < 1;

        public static bool Contains(PlayableProfile profile, NavLocation center, double radius, PlayableEntitySnapshot unit) =>
            Contains(profile.AuthoredMap, center, radius, unit.Position, unit.Location,
                PlayableUnitRules.Aircraft(unit.Kind) ? unit.Altitude + PlayableUnitRules.AircraftBodyHeight(profile,unit.Kind) / 2 : (double?)null);

        public static OrbitalRepairRim[] Rim(PlayableProfile profile, NavLocation center, double radius)
        {
            const int segments = OrbitalRepairRim.SegmentCount;
            var map = profile.AuthoredMap;
            var rim = new List<OrbitalRepairRim>(segments);
            bool Locate(int step, out NavLocation location)
            {
                double angle = step * Math.PI * 2 / segments;
                var point = new NavPoint(center.Position.X + radius * Math.Cos(angle),
                    center.Position.Z + radius * Math.Sin(angle));
                var preferred = new NavLocation(point, center.SurfaceId);
                return TryLocate(profile, point, map == null || map.IsValidLocation(preferred, 0) ? preferred : (NavLocation?)null,
                    out location) && Contains(map, center, radius + 1e-6, point, location, null);
            }

            bool previousValid = Locate(0, out var previous);
            for (int step = 1; step <= segments; step++)
            {
                bool valid = Locate(step, out var point);
                if (valid && previousValid) rim.Add(new OrbitalRepairRim(previous, point, step - 1));
                previous = point;
                previousValid = valid;
            }
            return rim.ToArray();
        }
    }
}

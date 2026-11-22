using System;
using System.Collections.Generic;
using System.Linq;

namespace Spacewars.Simulation
{
    public static class OrbitalStasis
    {
        public static bool Eligible(PlayableEntitySnapshot unit) =>
            unit.Health > 0 && unit.StasisRemaining <= 0 && unit.MaterializationProgress >= 1 && !unit.Departing;

        public const double GroundAirCeiling = 5;

        public static double HawkCenterHeight(PlayableProfile profile, double altitude) =>
            altitude + PlayableUnitRules.HawkBodyHeight(profile) / 2;

        public static double GroundAltitude(PlayableProfile profile, NavPoint point, NavLocation? location = null)
        {
            var map = profile.AuthoredMap;
            if (map == null) return 0;
            return OrbitalRepairNanobots.TryLocate(profile,point,location,out var ground)
                ? OrbitalRepairNanobots.Height(map,ground) : map.SupportAt(point)?.HeightAt(point) ?? 0;
        }

        public static int[] Select(NavPoint point, PlayableProfileData profile,
            IEnumerable<(int Id, NavPoint Position)> candidates) =>
            Select(point,profile,candidates.Select(unit => (unit.Id,unit.Position,(double?)null)),0);

        public static int[] Select(NavPoint point, PlayableProfileData profile,
            IEnumerable<(int Id, NavPoint Position, double? HullHeight)> candidates, double altitude, bool airOnly = false)
        {
            double radiusSquared = profile.orbitalStasisRadius * profile.orbitalStasisRadius;
            var eligible = new List<(int Id, double Distance)>();
            foreach (var unit in candidates)
            {
                double distance = DistanceSquared(point,unit.Position);
                if (airOnly)
                {
                    if (!unit.HullHeight.HasValue) continue;
                    double difference = unit.HullHeight.Value - altitude;
                    distance += difference * difference;
                }
                else if (unit.HullHeight.HasValue)
                {
                    double height = unit.HullHeight.Value - altitude;
                    if (height < 0 || height > GroundAirCeiling) continue;
                }
                if (distance <= radiusSquared) eligible.Add((unit.Id,distance));
            }
            return eligible.OrderBy(unit => unit.Distance).ThenBy(unit => unit.Id)
                .Take(profile.orbitalStasisTargets).Select(unit => unit.Id).ToArray();
        }

        public static int[] SelectVisible(NavPoint point, PlayableProfile profile, PlayableProfileData terms,
            TeamVisionSnapshot vision, IEnumerable<PlayableEntitySnapshot> units, int hawkId = 0,
            NavLocation? ground = null)
        {
            if (vision == null) return Array.Empty<int>();
            var candidates = units.Where(u => Eligible(u) && vision.IsVisible(u.Position));
            if (hawkId != 0)
            {
                var hawk = candidates.FirstOrDefault(u => u.Id == hawkId && PlayableUnitRules.Aircraft(u.Kind));
                if (hawk == null) return Array.Empty<int>();
                point = hawk.Position;
                return Select(point,terms,Candidates(),hawk.Altitude + PlayableUnitRules.AircraftBodyHeight(profile,hawk.Kind)/2,airOnly:true);
            }
            if (!vision.IsVisible(point)) return Array.Empty<int>();
            return Select(point,terms,Candidates(),GroundAltitude(profile,point,ground));

            IEnumerable<(int Id, NavPoint Position, double? HullHeight)> Candidates() =>
                candidates.Select(u => (u.Id,u.Position,PlayableUnitRules.Aircraft(u.Kind)
                    ? (double?)u.Altitude + PlayableUnitRules.AircraftBodyHeight(profile,u.Kind)/2 : null));
        }

        private static double DistanceSquared(NavPoint a, NavPoint b)
        {
            double x = a.X - b.X, z = a.Z - b.Z;
            return x * x + z * z;
        }
    }
}

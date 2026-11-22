using System;

namespace Spacewars.Simulation
{
    public readonly struct OrbitalReinforcementQuote
    {
        public PlayableEntityKind Kind { get; }
        public int Count { get; }
        public int Credits { get; }
        public int Population { get; }
        public bool Reserved { get; }

        public OrbitalReinforcementQuote AsReserved(int? credits = null, int? population = null) =>
            new OrbitalReinforcementQuote(Kind, Count, credits ?? Credits, population ?? Population, true);

        internal OrbitalReinforcementQuote(PlayableEntityKind kind, int count, int credits, int population, bool reserved)
        {
            Kind = kind;
            Count = count;
            Credits = credits;
            Population = population;
            Reserved = reserved;
        }
    }

    public static class OrbitalReinforcement
    {
        private const int CreditStep = 10;
        private const int OrientationCount = 12;

        public static OrbitalReinforcementQuote Quote(PlayableProfile profile, double multiplier, bool tanks, bool free)
        {
            var kind = tanks ? PlayableEntityKind.Tank : PlayableEntityKind.Explorer;
            int count = tanks ? 2 : 3;
            int credits = free ? 0 : checked(CreditStep * (int)decimal.Ceiling(
                count * (decimal)PlayableUnitRules.Cost(profile, kind) * (decimal)multiplier / CreditStep));
            return new OrbitalReinforcementQuote(kind, count, credits, count * PlayableUnitRules.Population(profile, kind), false);
        }

        public static bool OverlapsBuilding(NavPoint point, NavPoint center, double halfWidth, double clearance)
        {
            double x = Math.Max(0, Math.Abs(point.X - center.X) - halfWidth);
            double z = Math.Max(0, Math.Abs(point.Z - center.Z) - halfWidth);
            return x * x + z * z <= clearance * clearance;
        }

        public static NavPoint[] Layout(NavPoint center, OrbitalReinforcementQuote wave, double radius,
            Func<NavPoint, bool> allowed, out bool valid)
        {
            valid = false;
            if (wave.Count != 2 && wave.Count != 3) return Array.Empty<NavPoint>();

            double spacing = 2.15 * radius;
            double arm = wave.Count == 2 ? spacing / 2 : spacing / Math.Sqrt(3);
            var points = new NavPoint[wave.Count];
            var first = new NavPoint[wave.Count];
            for (int turn = 0; turn < OrientationCount; turn++)
            {
                bool fits = true;
                for (int member = 0; member < wave.Count; member++)
                {
                    double angle = 2 * Math.PI * (turn / (double)OrientationCount + member / (double)wave.Count);
                    var point = new NavPoint(center.X + Math.Cos(angle) * arm, center.Z + Math.Sin(angle) * arm);
                    points[member] = point;
                    fits &= allowed(point);
                }
                if (fits)
                {
                    valid = true;
                    return points;
                }
                if (turn == 0) Array.Copy(points, first, points.Length);
            }
            return first;
        }
    }
}

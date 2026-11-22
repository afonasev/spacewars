using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed class AiStasisOrbitalPolicy
    {
        private readonly PlayableAiObservation observation;
        private readonly PlayableProfile profile;
        private readonly PlayableProfileData terms;
        private readonly PlayableEntitySnapshot[] own, hostile, eligible;
        private readonly Dictionary<int, PlayableEntitySnapshot> byId;
        private readonly ILookup<int, PlayableEntitySnapshot> shooters;

        internal AiStasisOrbitalPolicy(PlayableAiObservation observation, PlayableProfile profile, PlayableProfileData terms)
        {
            this.observation = observation;
            this.profile = profile;
            this.terms = terms;
            eligible = observation.Entities.Where(unit => OrbitalStasis.Eligible(unit) &&
                observation.Vision?.IsVisible(unit.Position) == true).ToArray();
            byId = eligible.ToDictionary(unit => unit.Id);
            own = eligible.Where(unit => unit.Owner == observation.Owner).ToArray();
            hostile = eligible.Where(unit => observation.IsHostile(unit.Owner)).ToArray();
            shooters = observation.ObservedHostileShots
                .Where(shot => byId.ContainsKey(shot.OwnerId))
                .Select(shot => (shot.TargetId, shot.OwnerId)).Distinct()
                .ToLookup(shot => shot.TargetId, shot => byId[shot.OwnerId]);
        }

        internal NavPoint? BestPoint()
        {
            if (hostile.Length == 0 || own.Length == 0) return null;
            var anchors = hostile.Where(enemy => own.Any(friend => CanEngage(friend, enemy) || CanEngage(enemy, friend)) ||
                observation.OwnCenterDamage.Any(hit => hit.AttackerId == enemy.Id))
                .Concat(own.Where(friend => shooters[friend.Id].Any())).ToArray();

            var points = new HashSet<NavPoint>();
            foreach (var anchor in anchors)
            {
                points.Add(anchor.Position);
                foreach (var neighbor in eligible)
                {
                    if (anchor.Id == neighbor.Id || Distance(anchor.Position, neighbor.Position) > 2 * terms.orbitalStasisRadius) continue;
                    points.Add(new NavPoint((anchor.Position.X + neighbor.Position.X) / 2,
                        (anchor.Position.Z + neighbor.Position.Z) / 2));
                }
            }

            NavPoint? best = null;
            var bestScore = (Critical: false, Benefit: 0d);
            foreach (var point in points.OrderBy(point => point.X).ThenBy(point => point.Z))
            {
                var score = Score(point);
                if (!Useful(score) || score.CompareTo(bestScore) <= 0) continue;
                best = point;
                bestScore = score;
            }
            return best;
        }

        internal bool WorthCasting(NavPoint point) => Useful(Score(point));

        private static bool Useful((bool Critical, double Benefit) score) => score.Critical || score.Benefit > 0;

        private (bool Critical, double Benefit) Score(NavPoint point)
        {
            if (!(Math.Abs(point.X) <= profile.ArenaHalfExtent && Math.Abs(point.Z) <= profile.ArenaHalfExtent))
                return default;
            var targets = OrbitalStasis.SelectVisible(point, profile, terms, observation.Vision, eligible);
            if (targets.Length == 0) return default;
            var frozen = new HashSet<int>(targets);
            bool critical = false;
            double benefit = 0;
            foreach (int id in targets)
            {
                var unit = byId[id];
                if (!observation.IsHostile(unit.Owner))
                {
                    // Stopping a friendly combatant costs firepower even when the shot
                    // would protect a second unit in the same circle.
                    if (hostile.Any(enemy => !frozen.Contains(enemy.Id) && CanEngage(unit, enemy)))
                        benefit -= Dps(unit) * terms.orbitalStasisSeconds;
                    if (unit.Owner == observation.Owner) benefit += Rescue(unit, frozen);
                    continue;
                }

                var defense = DelayRaid(unit, frozen);
                critical |= defense.Critical;
                benefit += Math.Max(defense.Benefit, Math.Max(CombatAdvantage(unit, frozen), CoverWithdrawal(unit, frozen)));
            }
            return (critical, benefit);
        }

        private double Rescue(PlayableEntitySnapshot patient, HashSet<int> frozen)
        {
            var incoming = shooters[patient.Id].Where(enemy => !frozen.Contains(enemy.Id)).ToArray();
            if (incoming.Length == 0 || Health(patient) >= PlayableUnitRules.MaximumHealth(profile, patient) ||
                incoming.Sum(Dps) * terms.orbitalStasisSeconds < Health(patient)) return 0;
            if (!incoming.Any(enemy => own.Any(ally => ally.Id != patient.Id &&
                !frozen.Contains(ally.Id) && CanReachFire(ally, enemy.Position)))) return 0;
            return PlayableUnitRules.MaximumHealth(profile, patient);
        }

        private double CombatAdvantage(PlayableEntitySnapshot enemy, HashSet<int> frozen)
        {
            var friends = own.Where(unit => !frozen.Contains(unit.Id) && CanEngage(enemy, unit)).ToArray();
            if (friends.Length == 0) return 0;
            var remaining = hostile.Where(unit => !frozen.Contains(unit.Id) &&
                friends.Any(friend => CanEngage(unit, friend) || CanEngage(friend, unit))).ToArray();
            if (remaining.Length == 0) return 0;

            double enemyPressure = remaining.Sum(Dps);
            double friendlyPressure = friends.Where(friend => remaining.Any(unit => CanEngage(friend, unit))).Sum(Dps);
            if (friendlyPressure <= enemyPressure || friendlyPressure > enemyPressure + hostile
                .Where(unit => frozen.Contains(unit.Id) && friends.Any(friend => CanEngage(unit, friend))).Sum(Dps)) return 0;

            bool killOpportunity = remaining.Any(unit => friends.Where(friend => CanEngage(friend, unit)).Sum(Dps) *
                terms.orbitalStasisSeconds >= Health(unit));
            bool wouldSaveEnemy = friends.Where(friend => friend.TargetId == enemy.Id && CanEngage(friend, enemy)).Sum(Dps) *
                terms.orbitalStasisSeconds >= Health(enemy);
            return killOpportunity && !wouldSaveEnemy ? Dps(enemy) * terms.orbitalStasisSeconds : 0;
        }

        private (bool Critical, double Benefit) DelayRaid(PlayableEntitySnapshot enemy, HashSet<int> frozen)
        {
            bool critical = false;
            double benefit = 0;
            foreach (var hit in observation.OwnCenterDamage.Where(hit => hit.AttackerId == enemy.Id)
                .GroupBy(hit => hit.CenterId).Select(group => group.OrderByDescending(hit => hit.Tick).First()))
            {
                if ((observation.Tick - hit.Tick) / 30d > terms.orbitalStasisSeconds) continue;
                var center = observation.Buildings.FirstOrDefault(building => building.Id == hit.CenterId);
                if (center == null || Distance(center.Position, enemy.Position) > Range(enemy)) continue;
                bool reinforcements = own.Any(unit => !frozen.Contains(unit.Id) &&
                    !CanEngage(unit, enemy) && CanReachFire(unit, enemy.Position));
                var attackers = observation.OwnCenterDamage.Where(fact => fact.CenterId == center.Id &&
                    (observation.Tick - fact.Tick) / 30d <= terms.orbitalStasisSeconds)
                    .Select(fact => fact.AttackerId).Distinct().Where(byId.ContainsKey)
                    .Select(id => byId[id]).Where(unit => Distance(unit.Position, center.Position) <= Range(unit)).ToArray();
                bool savesCenter = attackers.Sum(Dps) * terms.orbitalStasisSeconds >= center.Health &&
                    attackers.Where(unit => !frozen.Contains(unit.Id)).Sum(Dps) * terms.orbitalStasisSeconds < center.Health;
                if (reinforcements || savesCenter) benefit += Dps(enemy) * terms.orbitalStasisSeconds;
                critical |= savesCenter;
            }
            return (critical, benefit);
        }

        private double CoverWithdrawal(PlayableEntitySnapshot enemy, HashSet<int> frozen)
        {
            foreach (var friend in own)
            {
                var order = friend.CurrentOrder;
                if (frozen.Contains(friend.Id) || order?.Kind != PlayableTacticalOrderKind.Move ||
                    !shooters[friend.Id].Any(source => source.Id == enemy.Id) || !CanEngage(enemy, friend)) continue;
                double distance = Distance(friend.Position, order.Destination);
                if (distance <= 0) continue;
                double fraction = Math.Min(1, Speed(friend) * terms.orbitalStasisSeconds / distance);
                var escape = new NavPoint(friend.Position.X + (order.Destination.X - friend.Position.X) * fraction,
                    friend.Position.Z + (order.Destination.Z - friend.Position.Z) * fraction);
                if (Distance(enemy.Position, escape) > Range(enemy))
                    return Dps(enemy) * terms.orbitalStasisSeconds;
            }
            return 0;
        }

        private bool CanEngage(PlayableEntitySnapshot source, PlayableEntitySnapshot target) =>
            (target.Kind != PlayableEntityKind.Hawk || source.Kind == PlayableEntityKind.Hawk ||
                source.Kind == PlayableEntityKind.Explorer) && Distance(source.Position, target.Position) <= Range(source);
        private bool CanReachFire(PlayableEntitySnapshot unit, NavPoint target) =>
            Distance(unit.Position, target) <= Range(unit) + Speed(unit) * terms.orbitalStasisSeconds;
        private double Range(PlayableEntitySnapshot unit) => PlayableUnitRules.Range(profile, unit.Kind, unit.Upgraded);
        private double Speed(PlayableEntitySnapshot unit) => PlayableUnitRules.Speed(profile, unit.Kind, unit.Upgraded);
        private double Dps(PlayableEntitySnapshot unit) => PlayableUnitRules.DamagePerSecond(profile, unit.Kind, unit.Upgraded);
        private static double Health(PlayableEntitySnapshot unit) => unit.ExactHealth ?? unit.Health;
        private static double Distance(NavPoint a, NavPoint b)
        {
            double x = a.X - b.X, z = a.Z - b.Z;
            return Math.Sqrt(x * x + z * z);
        }
    }
}

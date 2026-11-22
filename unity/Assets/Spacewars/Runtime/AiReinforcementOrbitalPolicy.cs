using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    internal static class AiReinforcementOrbitalPolicy
    {
        private const int Bearings = 8;

        internal static bool HasResources(PlayableAiObservation observation)
        {
            var wave = observation.Orbital.ReinforcementQuote;
            var population = observation.Population;
            return wave.Count > 0 && observation.Credits >= wave.Credits &&
                (population == null || population.Living + population.Reserved + wave.Population <= population.Capacity);
        }

        internal static IEnumerable<(NavPoint Point, int Objective, int Scout, double Utility)> Opportunities(
            PlayableAiObservation observation, PlayableProfile profile, PlayableProfileData terms, AiPersonality personality, AiKnowledgeState knowledge, bool allowLinked)
        {
            if (!HasResources(observation)) yield break;
            var friendly = observation.Entities.Where(u => u.Owner == observation.Owner && u.Health > 0 &&
                u.Kind != PlayableEntityKind.Hawk && u.MaterializationProgress >= 1).ToArray();
            var assets = observation.Buildings.Where(b => b.Owner == observation.Owner && b.Health > 0 &&
                (b.Kind == PlayableBuildingKind.Headquarters || b.Kind == PlayableBuildingKind.Outpost ||
                b.Kind == PlayableBuildingKind.Mine)).ToArray();
            foreach (var asset in assets)
            {
                bool urgent = observation.OwnCenterDamage.Any(hit => hit.CenterId == asset.Id);
                int guards = friendly.Count(unit => Distance(unit.Position, asset.Position) <= profile.TankVisionRange);
                if (!urgent && guards >= observation.Orbital.ReinforcementQuote.Count) continue;
                double utility = urgent ? 5 : personality == AiPersonality.Defensive ? 3 : 2;
                foreach (var point in Around(asset.Position, TerritoryRules.Radius(profile, asset.Kind) + 2.5))
                    if (SafePlacement(observation, profile, terms, point, knowledge)) yield return (point, 0, 0, utility);
            }

            if (!allowLinked) yield break;
            foreach (var asset in observation.Buildings.Where(b => observation.IsHostile(b.Owner) &&
                (b.Kind == PlayableBuildingKind.Mine || b.Kind == PlayableBuildingKind.Outpost) && Visible(observation, b.Position)))
            {
                var scout = friendly.Where(u => u.Kind == PlayableEntityKind.Explorer &&
                    Distance(u.Position, asset.Position) <= PlayableUnitRules.Vision(profile, u.Kind))
                    .OrderBy(u => Distance(u.Position, asset.Position)).ThenBy(u => u.Id).FirstOrDefault();
                if (scout == null) continue;
                foreach (var point in Around(scout.Position, 2.5).Prepend(scout.Position))
                    if (Connects(profile, observation.Orbital.ReinforcementQuote.Kind, point, scout.Position) &&
                        SafePlacement(observation, profile, terms, point, knowledge))
                        yield return (point, asset.Id, scout.Id, personality == AiPersonality.Defensive ? 3 : 4);
            }

            foreach (var leader in friendly.Where(unit => unit.CurrentOrder?.Kind == PlayableTacticalOrderKind.AttackMove ||
                unit.CurrentOrder?.Kind == PlayableTacticalOrderKind.Attack).Take(12))
                foreach (var point in Around(leader.Position, 2.5).Concat(
                    Around(leader.Position, PlayableUnitRules.Vision(profile, leader.Kind) / 2)))
                    if (Connects(profile, observation.Orbital.ReinforcementQuote.Kind, point, leader.Position) &&
                        SafePlacement(observation, profile, terms, point, knowledge))
                        yield return (point, 0, leader.Id, personality == AiPersonality.Aggressive ? 4 : 3);
        }

        internal static bool SafePlacement(PlayableAiObservation observation, PlayableProfile profile,
            PlayableProfileData terms, NavPoint center, AiKnowledgeState knowledge)
        {
            if (!Visible(observation, center)) return false;
            var wave = observation.Orbital.ReinforcementQuote;
            var map = profile.AuthoredMap;
            string surface = null;
            if (map != null && map.TryLocate(center, 0, out var location)) surface = location.SurfaceId;
            double radius = PlayableUnitRules.Radius(profile, wave.Kind);
            bool Fits(NavPoint point)
            {
                if (!Visible(observation, point) ||
                    map != null && (!map.TryLocate(point, radius, out var at) || at.SurfaceId != surface)) return false;
                foreach (var unit in observation.Entities)
                {
                    double distance = Distance(point, unit.Position);
                    double footprint = radius + PlayableUnitRules.Radius(profile, unit.Kind);
                    bool hostile = observation.IsHostile(unit.Owner);
                    if (unit.Kind != PlayableEntityKind.Hawk &&
                        distance < footprint + (hostile ? terms.orbitalReinforcementClearance : 0)) return false;
                    if (hostile && distance <= PlayableUnitRules.Range(profile, unit.Kind) + footprint +
                        PlayableUnitRules.Speed(profile, unit.Kind, unit.Upgraded) * terms.orbitalReinforcementSeconds)
                        return false;
                }
                foreach (var building in observation.Buildings)
                {
                    double margin = radius;
                    bool hostile = observation.IsHostile(building.Owner);
                    if (hostile) margin += terms.orbitalReinforcementClearance;
                    if (OrbitalReinforcement.OverlapsBuilding(point, building.Position, TerritoryRules.Radius(profile, building.Kind), margin))
                        return false;
                    if (hostile && TerritoryRules.ArmedDefense(building.Kind) &&
                        Distance(point, building.Position) <= Math.Max(profile.TurretRange, profile.GaussRange) + radius)
                        return false;
                }
                foreach (var contact in knowledge?.Contacts ?? Array.Empty<AiKnownContact>())
                {
                    if (contact.Visible || !contact.Building || contact.Confidence <= 0 ||
                        !observation.IsHostile(contact.Owner)) continue;
                    var kind = (PlayableBuildingKind)contact.Kind;
                    if (TerritoryRules.ArmedDefense(kind) &&
                        AiPositionDevelopmentPlanner.Covers(observation, profile, contact.Position, kind, point, false, contact.Id, 0))
                        return false;
                }
                return true;
            }
            OrbitalReinforcement.Layout(center, wave, radius, Fits, out bool valid);
            return valid;
        }

        private static bool Connects(PlayableProfile profile, PlayableEntityKind kind, NavPoint from, NavPoint to) =>
            profile.AuthoredMap?.SupportsSweep(from, to, PlayableUnitRules.Radius(profile, kind)) ?? true;

        private static bool Visible(PlayableAiObservation observation, NavPoint point) =>
            observation.Vision?.IsVisible(point) == true;

        private static double Distance(NavPoint first, NavPoint second) =>
            AiOpponentSelector.Distance(first, second);

        private static IEnumerable<NavPoint> Around(NavPoint point, double radius)
        {
            for (int bearing = 0; bearing < Bearings; bearing++)
            {
                double angle = bearing * (2 * Math.PI / Bearings);
                yield return new NavPoint(point.X + radius * Math.Cos(angle), point.Z + radius * Math.Sin(angle));
            }
        }
    }
}

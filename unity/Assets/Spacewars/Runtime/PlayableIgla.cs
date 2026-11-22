using System;
using System.Collections.Generic;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private readonly List<Unit> iglaShooters = new List<Unit>();
        private BallisticPoint IglaAimPoint(int target, NavPoint point)
        {
            double height = buildings.TryGetValue(target, out var b)
                ? Math.Min(1, PlayableUnitRules.AirBuildingHeight(profile, b.Kind) * .5)
                : units.TryGetValue(target,out var unit) ? PlayableUnitRules.Radius(profile,unit.Kind)*.90 : DirectFireHeight;
            return new BallisticPoint(point.X, GroundHeight(point) + height, point.Z);
        }

        private bool IglaClear(Unit unit, NavUnit actor, int target, NavPoint point)
        {
            bool hostile = units.TryGetValue(target, out var other)
                ? !PlayableUnitRules.Aircraft(other.Kind) && Hostile(unit.Owner, other.Owner)
                : buildings.TryGetValue(target, out var building) && Hostile(unit.Owner, building.Owner);
            if (!hostile) return false;
            var origin = actor.Position;
            var from = new BallisticPoint(origin.X, GroundHeight(origin) + 1.037 * PlayableUnitRules.IglaModelScale, origin.Z);
            return PlayableBallistics.TerrainLineClear(from, IglaAimPoint(target, point), 0, SolidObstacles(), profile.BallisticWallHeight, AirSurface);
        }

        private void AimIgla(Unit unit, NavUnit actor, double desired, double dt, bool rotateHull)
        {
            double limit = profile.IglaHalfAngle * Math.PI / 180;
            if (rotateHull && Math.Abs(Angle(desired - actor.Heading)) > limit)
                actor.Heading = Turn(actor.Heading, desired, profile.IglaHullTurn * dt);
            double offset = Math.Clamp(Angle(Turn(unit.Turret, desired, 6 * dt) - actor.Heading), -limit, limit);
            unit.Turret = Angle(actor.Heading + offset);
        }

        // Apply after enumeration: lethal damage can remove a unit from the roster.
        private void ApplyIglaShots()
        {
            foreach (var unit in iglaShooters)
            {
                if (!units.ContainsKey(unit.Id) || !TryActor(unit.Id, out var actor) ||
                    !Target(unit.Target, out var point, out _) || !IglaClear(unit, actor, unit.Target, point)) continue;
                var end = IglaAimPoint(unit.Target, point);
                if (!TryDamageWithOwner(unit.Target, WeaponDamage(unit.Owner, profile.IglaDamage), unit.Id, unit.Owner,
                    incoming: new NavPoint(point.X-actor.Position.X, point.Z-actor.Position.Z))) continue;
                unit.IglaBeamEnd = end;
                unit.Reload = profile.IglaReload;
                RememberDefaultShot(unit);
                Sound(PlayableSoundKind.Shot, actor.Position, unit.Kind, unit.Owner, unitId:unit.Id);
            }
            iglaShooters.Clear();
        }

        private BallisticPoint? IglaBeam(Unit unit, PlayableOwner? observer)
        {
            if (unit.Kind != PlayableEntityKind.Igla || unit.Reload <= 0 || unit.Reload < profile.IglaReload - PlayableUnitRules.IglaBeamVisibleSeconds ||
                !unit.IglaBeamEnd.HasValue) return null;
            var end = unit.IglaBeamEnd.Value;
            if (observer.HasValue && !Vision(observer.Value).IsVisible(end.Ground)) return null;
            return end;
        }
    }
}

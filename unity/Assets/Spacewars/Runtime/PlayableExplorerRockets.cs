using System;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private bool ExplorerRocketEquipped(Unit unit) =>
            unit.Kind == PlayableEntityKind.Explorer && unit.AuthoredUpgrade != false &&
            HasResearch(unit.Owner, PlayableResearchKind.ExplorerRocketLauncher) &&
            (unit.BurstRemaining == 0 || unit.HawkBurstReload > 0);

        private double ExplorerRocketRange(bool air) =>
            air ? profile.ExplorerRocketAirRange : profile.ExplorerRocketGroundRange;

        private void FireExplorerRocket(Unit unit, NavPoint from, NavPoint target)
        {
            bool air = AirTarget(unit.Target);
            bool first = unit.BurstRemaining == 0;
            int side = first ? 0 : 1;
            double yaw = unit.Turret;
            var endpoint = new BallisticPoint(target.X, TargetHeight(unit.Target, target), target.Z);
            double pivotY = GroundHeight(from) + ExplorerRocketGeometry.PivotHeight * ExplorerRocketGeometry.Scale(profile);
            double pitch = Math.Atan2(endpoint.Y - pivotY, Distance(from, target));
            double hull = TryActor(unit.Id,out var self) ? self.Heading : yaw;
            double rear = ExplorerRocketGeometry.PivotRear * ExplorerRocketGeometry.Scale(profile);
            var pivot = new NavPoint(from.X - Math.Cos(hull) * rear, from.Z - Math.Sin(hull) * rear);
            var muzzle = ExplorerRocketGeometry.Muzzle(profile, pivot, pivotY, yaw, pitch, side);
            var direction = HawkRocketFlight.Normal(HawkRocketFlight.Sub(endpoint, muzzle));
            double speed = profile.ExplorerRocketSpeed;
            double turn = air ? profile.ExplorerRocketTurnDegrees * Math.PI / 180 : 0;
            double path = ExplorerRocketRange(air) * (1 + profile.ProjectileExtraRangePercent / 100);
            double offset = HawkRocketFlight.Length(HawkRocketFlight.Sub(muzzle,new BallisticPoint(from.X,pivotY,from.Z)));
            double lifetime = (path - offset) / speed;
            if (lifetime <= 0) return;
            if (first)
            {
                unit.BurstTarget = unit.Target;
                unit.BurstRemaining = unit.BurstSize = 2;
                unit.BurstSequence++;
                unit.HawkBurstReload = air ? profile.ExplorerRocketAirReload : profile.ExplorerRocketGroundReload;
            }

            projectiles.Add(new Projectile
            {
                TermsRevision = profile.Revision,
                Id = nextProjectile++,
                Owner = unit.Id,
                Target = unit.Target,
                Faction = unit.Owner,
                Kind = unit.Kind,
                Damage = WeaponDamage(unit.Owner, profile.ExplorerRocketDamage),
                Radius = profile.HawkMissileRadius,
                Speed = speed,
                Position = muzzle.Ground,
                Height = muzzle.Y,
                Spatial = true,
                DirectionX = direction.X,
                DirectionY = direction.Y,
                DirectionZ = direction.Z,
                Hawk = new HawkRocketFlight(unit.Target, muzzle, direction, speed, turn, lifetime)
            });
            RememberDefaultShot(unit);
            Sound(PlayableSoundKind.Shot, from, unit.Kind, unit.Owner, unitId: unit.Id, shotSide: side, rocketLaunch: true);

            unit.BurstRemaining--;
            unit.Reload = unit.HawkBurstReload;
            if (unit.BurstRemaining > 0)
                unit.BurstDelay = air ? profile.ExplorerRocketAirInterval : profile.ExplorerRocketGroundInterval;
            else
            {
                unit.BurstTarget = 0;
                unit.BurstDelay = 0;
                unit.HawkBurstReload = 0;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private sealed class RepairField
        {
            internal PlayableOwner Owner;
            internal NavLocation Center;
            internal double Until, Radius, Rate;
            internal OrbitalRepairRim[] Rim;
        }

        private readonly List<RepairField> repairFields = new List<RepairField>();
        private readonly HashSet<int> repairedUnits = new HashSet<int>();

        private PlayableCommandStatus UseRepairNanobots(PlayableOwner owner, PlayableCommand command)
        {
            if (eliminated.Contains(owner) || !Orbital(owner).CanUse(OrbitalAbilities.RepairNanobots))
                return PlayableCommandStatus.Rejected;

            RefreshVision();
            var vision = Vision(owner);
            if (!OrbitalRepairNanobots.TryLocate(profile, command.Target, command.TargetLocation, out var center) ||
                !vision.IsVisible(center.Position))
                return PlayableCommandStatus.InvalidTarget;

            var data = OrbitalData;
            if (command.Origin == PlayableOrderOrigin.Ai)
            {
                double radius = data.orbitalNanobotsRadius;
                int wounded = units.Values.Count(unit => !Hostile(owner, unit.Owner) && CanReceiveFieldRepair(unit) &&
                    TryActor(unit.Id, out var actor) && vision.IsVisible(actor.Position) && InsideRepairField(unit, actor, center, radius));
                wounded += buildings.Values.Count(building => !Hostile(owner, building.Owner) && CanReceiveBuildingRepair(building) &&
                    vision.IsVisible(building.Position) && OrbitalRepairNanobots.Contains(profile.AuthoredMap,
                        center, radius, building.Position, null, null));
                if (wounded < OrbitalRepairNanobots.MinimumWoundedForBot) return PlayableCommandStatus.InvalidTarget;
            }

            Orbital(owner).Use(OrbitalAbilities.RepairNanobots, data);
            AddRepairField(owner, center, elapsed + data.orbitalNanobotsSeconds,
                data.orbitalNanobotsRadius, data.orbitalNanobotsHealthPerSecond);
            Sound(PlayableSoundKind.OrbitalNanobotsDeployed, center.Position, PlayableEntityKind.Tank, owner);
            return PlayableCommandStatus.Applied;
        }

        private void AddRepairField(PlayableOwner owner, NavLocation center, double until, double radius, double rate)
        {
            repairFields.Add(new RepairField { Owner = owner, Center = center, Until = until, Radius = radius,
                Rate = rate, Rim = OrbitalRepairNanobots.Rim(profile, center, radius) });
        }

        private bool CanReceiveFieldRepair(Unit unit) => unit.Health > 0 && !Frozen(unit) &&
            !Materializing(unit) && unit.Air?.Departing != true &&
            unit.Health < UnitMaximumHealth(unit.Owner, unit.Kind);

        private bool InsideRepairField(Unit unit, NavUnit actor, NavLocation center, double radius) =>
            OrbitalRepairNanobots.Contains(profile.AuthoredMap, center, radius, actor.Position,
                unit.Air == null ? actor.Location : (NavLocation?)null,
                unit.Air == null ? (double?)null : unit.Air.Height + PlayableUnitRules.AircraftBodyHeight(profile,unit.Kind) / 2);

        private void HealUnit(Unit unit, double amount)
        {
            var health = Math.Min(UnitMaximumHealth(unit.Owner, unit.Kind), unit.Health + amount);
            if (health <= unit.Health) return;
            unit.Health = health;
            repairedUnits.Add(unit.Id);
        }

        private double RepairFieldAmount(PlayableOwner owner, NavPoint point, NavLocation? ground, double? altitude, double dt)
        {
            double stepStart = elapsed - dt;
            double fullRate = 0, partialRate = 0, partialSeconds = 0;
            foreach (var field in repairFields)
            {
                if (Hostile(field.Owner, owner) || !OrbitalRepairNanobots.Contains(profile.AuthoredMap,
                    field.Center, field.Radius, point, ground, altitude)) continue;
                double seconds = Math.Min(dt, field.Until - stepStart);
                if (seconds >= dt) fullRate = Math.Max(fullRate, field.Rate);
                else if (seconds > 0 && field.Rate * seconds > partialRate * partialSeconds)
                {
                    partialRate = field.Rate;
                    partialSeconds = seconds;
                }
            }
            return fullRate * dt + Math.Max(0, partialRate - fullRate) * partialSeconds;
        }

        private void AdvanceRepairNanobots(double dt)
        {
            if (repairFields.Count == 0) return;
            foreach (var unit in units.Values)
            {
                if (!CanReceiveFieldRepair(unit) || !TryActor(unit.Id, out var actor)) continue;
                HealUnit(unit, RepairFieldAmount(unit.Owner, actor.Position, unit.Air == null ? actor.Location : (NavLocation?)null,
                    unit.Air == null ? (double?)null : unit.Air.Height + PlayableUnitRules.AircraftBodyHeight(profile,unit.Kind) / 2, dt));
            }
            foreach (var building in buildings.Values)
                if (CanReceiveBuildingRepair(building))
                    HealBuilding(building, RepairFieldAmount(building.Owner, building.Position, null, null, dt));
            repairFields.RemoveAll(field => field.Until <= elapsed + 1e-9);
        }

        private OrbitalRepairRim[] VisibleRepairRims(PlayableVision vision)
        {
            if (repairFields.Count == 0) return Array.Empty<OrbitalRepairRim>();
            return repairFields.SelectMany(field => field.Rim).Where(segment =>
                vision.IsVisible(segment.From.Position) && vision.IsVisible(segment.To.Position) &&
                vision.IsVisible(new NavPoint((segment.From.Position.X + segment.To.Position.X) / 2,
                    (segment.From.Position.Z + segment.To.Position.Z) / 2))).ToArray();
        }

        private int[] VisibleRepairedUnits(PlayableVision vision) => repairedUnits.Count == 0
            ? Array.Empty<int>()
            : repairedUnits.Where(id => buildings.TryGetValue(id, out var building) ? vision.IsVisible(building.Position) :
                units.ContainsKey(id) && TryActor(id, out var actor) && vision.IsVisible(actor.Position)).OrderBy(id => id).ToArray();

        private void WriteRepairNanobots(BinaryWriter writer)
        {
            writer.Write(repairFields.Count);
            foreach (var field in repairFields)
            {
                writer.Write((int)field.Owner);
                WorldWire.Number(writer, field.Center.Position.X);
                WorldWire.Number(writer, field.Center.Position.Z);
                WorldWire.String(writer, field.Center.SurfaceId);
                WorldWire.Number(writer, field.Until);
                WorldWire.Number(writer, field.Radius);
                WorldWire.Number(writer, field.Rate);
            }
            foreach (var pair in orbital.OrderBy(entry => (int)entry.Key))
                WorldWire.Number(writer, pair.Value.ProvingGroundRepairRecovery);
        }

        private void ReadRepairNanobots(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 10000) throw new ArgumentException("Invalid repair field count.");
            for (int i = 0; i < count; i++)
            {
                var owner = WorldWire.EnumValue<PlayableOwner>(reader);
                var point = new NavPoint(WorldWire.Number(reader), WorldWire.Number(reader));
                var center = new NavLocation(point, WorldWire.String(reader));
                double until = WorldWire.Number(reader), radius = WorldWire.Number(reader), rate = WorldWire.Number(reader);
                if (!HasOwner(owner) || until <= elapsed || radius <= 0 || rate <= 0 ||
                    !OrbitalRepairNanobots.TryLocate(profile, point, center, out _))
                    throw new ArgumentException("Invalid repair field.");
                AddRepairField(owner, center, until, radius, rate);
            }
            foreach (var pair in orbital.OrderBy(entry => (int)entry.Key))
                pair.Value.RestoreProvingGroundRepair(WorldWire.Number(reader));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private bool Frozen(Unit unit) => unit.StasisUntil > elapsed + 1e-9;

        private double StasisRemaining(Unit unit) => Frozen(unit) ? unit.StasisUntil - elapsed : 0;

        private PlayableCommandStatus UseStasis(PlayableOwner owner, NavPoint point, int hawkId, NavLocation? ground)
        {
            if (eliminated.Contains(owner) || !Orbital(owner).CanUse(OrbitalAbilities.Stasis))
                return PlayableCommandStatus.Rejected;
            RefreshVision();
            var vision = Vision(owner);
            bool airOnly = hawkId != 0;
            double altitude;
            if (airOnly)
            {
                if (!units.TryGetValue(hawkId,out var hawk) || hawk.Air == null || !StasisEligible(hawk) ||
                    !TryActor(hawkId,out var actor))
                    return PlayableCommandStatus.InvalidTarget;
                point = actor.Position;
                altitude = hawk.Air.Height + PlayableUnitRules.AircraftBodyHeight(profile,hawk.Kind)/2;
            }
            else
            {
                if (!(Math.Abs(point.X) <= profile.ArenaHalfExtent && Math.Abs(point.Z) <= profile.ArenaHalfExtent))
                    return PlayableCommandStatus.InvalidTarget;
                altitude = OrbitalStasis.GroundAltitude(profile,point,ground);
            }
            if (!vision.IsVisible(point)) return PlayableCommandStatus.InvalidTarget;

            IEnumerable<(int Id, NavPoint Position, double? HullHeight)> VisibleCandidates()
            {
                foreach (var unit in units.Values)
                    if (StasisEligible(unit) && TryActor(unit.Id,out var actor) && vision.IsVisible(actor.Position))
                        yield return (unit.Id,actor.Position,unit.Air == null ? (double?)null :
                            unit.Air.Height + PlayableUnitRules.AircraftBodyHeight(profile,unit.Kind)/2);
            }

            var targets = OrbitalStasis.Select(point,OrbitalData,VisibleCandidates(),altitude,airOnly);
            if (targets.Length == 0) return PlayableCommandStatus.InvalidTarget;

            Orbital(owner).Use(OrbitalAbilities.Stasis, OrbitalData);
            double until = elapsed + OrbitalData.orbitalStasisSeconds;
            foreach (int id in targets)
            {
                var unit = units[id];
                unit.StasisUntil = until;
                unit.LastCombatShotTarget = 0;
                unit.Velocity = default;
            }
            TrackUnitImmobility(until);
            Sound(PlayableSoundKind.OrbitalStasisApplied, point, PlayableEntityKind.Tank, owner,
                phaseEndTick: AudioPhaseEnd(OrbitalData.orbitalStasisSeconds));
            return PlayableCommandStatus.Applied;
        }

        private bool StasisEligible(Unit unit) =>
            unit.Health > 0 && !Frozen(unit) && !Materializing(unit) && unit.Air?.Departing != true;

        private void WriteStasis(BinaryWriter writer, Unit[] frozen)
        {
            writer.Write(frozen.Length);
            foreach (var unit in frozen)
            {
                writer.Write(unit.Id);
                WorldWire.Number(writer, unit.StasisUntil);
            }
            foreach (var pair in orbital.OrderBy(entry => (int)entry.Key))
                WorldWire.Number(writer, pair.Value.ProvingGroundStasisRecovery);
        }

        private void ReadStasis(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > units.Count) throw new ArgumentException("Invalid stasis count.");
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                double until = WorldWire.Number(reader);
                if (!units.TryGetValue(id, out var unit) || unit.StasisUntil != 0 ||
                    until <= elapsed + 1e-9 || Materializing(unit) || unit.Air?.Departing == true)
                    throw new ArgumentException("Invalid stasis state.");
                unit.StasisUntil = until;
                TrackUnitImmobility(until);
            }
            foreach (var pair in orbital.OrderBy(entry => (int)entry.Key))
                pair.Value.RestoreProvingGroundStasis(WorldWire.Number(reader));
        }
    }
}

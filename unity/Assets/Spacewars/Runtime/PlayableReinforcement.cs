using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private readonly Dictionary<PlayableOwner, OrbitalReinforcementQuote> reinforcementReservations =
            new Dictionary<PlayableOwner, OrbitalReinforcementQuote>();
        private readonly HashSet<PlayableOwner> reinforcementNextTanks = new HashSet<PlayableOwner>();
        private double immobilityUntil;

        private void TrackUnitImmobility(double until)
        {
            if (until <= immobilityUntil) return;
            if (immobilityUntil == 0) navigation.Crowd.IsUnitImmobile = IsUnitImmobile;
            immobilityUntil = until;
        }

        private bool IsUnitImmobile(int id) => units.TryGetValue(id, out var unit) && (Materializing(unit)||Frozen(unit));

        private void AdvanceUnitImmobility(double dt)
        {
            if (immobilityUntil == 0) return;
            PhaseCompleted(unit => unit.MaterializationUntil, PlayableSoundKind.OrbitalReinforcementReady, dt);
            PhaseCompleted(unit => unit.StasisUntil, PlayableSoundKind.OrbitalStasisReleased, dt);
            if (elapsed + 1e-9 < immobilityUntil) return;
            navigation.Crowd.IsUnitImmobile = null;
            immobilityUntil = 0;
        }

        // Derive completion from authoritative deadlines, including restored effects. No new saved state.
        private void PhaseCompleted(Func<Unit, double> deadline, PlayableSoundKind kind, double dt)
        {
            foreach (var group in units.Values.Where(unit => unit.Health > 0 &&
                deadline(unit) > elapsed - dt + 1e-9 && deadline(unit) <= elapsed + 1e-9)
                .GroupBy(unit => (unit.Owner, Until: deadline(unit))).OrderBy(group => group.Key.Until).ThenBy(group => group.Key.Owner))
            {
                // One cue per surviving wave/owner, not per unit; journal filtering still applies.
                var positions = group.OrderBy(unit => unit.Id).Where(unit => TryActor(unit.Id, out _))
                    .Select(unit => { TryActor(unit.Id, out var actor); return actor.Position; }).ToArray();
                if (positions.Length == 0) continue;
                Sound(kind, new NavPoint(positions.Average(point => point.X), positions.Average(point => point.Z)),
                    PlayableEntityKind.Tank, group.Key.Owner);
            }
        }

        private long AudioPhaseEnd(double seconds) => Tick + (long)Math.Ceiling(seconds * 30 - 1e-9);

        private OrbitalReinforcementQuote ReinforcementQuote(PlayableOwner owner)
        {
            if (reinforcementReservations.TryGetValue(owner, out var reserved)) return reserved;
            return OrbitalReinforcement.Quote(profile, OrbitalData.orbitalReinforcementCostMultiplier,
                reinforcementNextTanks.Contains(owner), FreeProvingGround);
        }

        private int ReinforcementPopulation(PlayableOwner owner) =>
            reinforcementReservations.TryGetValue(owner, out var quote) ? quote.Population : 0;

        private bool HasRoomForReinforcement(PlayableOwner owner, OrbitalReinforcementQuote wave)
        {
            int committed = Population(owner).Living;
            foreach (var building in buildings.Values)
                if (building.Owner == owner)
                    foreach (var order in building.Orders)
                        if (!order.IsUpgrade)
                            committed += PlayableUnitRules.Population(profile, order.Kind);
            return committed + wave.Population <= profile.ArmyCapacity;
        }

        private PlayableCommandStatus BeginReinforcement(PlayableOwner owner, out string message)
        {
            message = null;
            if (eliminated.Contains(owner) || !Orbital(owner).CanUse(OrbitalAbilities.Reinforcement))
                return PlayableCommandStatus.Rejected;
            var quote = ReinforcementQuote(owner);
            if (quote.Reserved) return PlayableCommandStatus.Applied;
            if (!CanAfford(owner, quote.Credits)) return PlayableCommandStatus.InsufficientCredits;
            if (!HasRoomForReinforcement(owner, quote))
            {
                message = "Не хватает мест в армии";
                return PlayableCommandStatus.Rejected;
            }
            AddCredits(owner, -quote.Credits);
            reinforcementReservations.Add(owner, quote.AsReserved());
            return PlayableCommandStatus.Applied;
        }

        private PlayableCommandStatus CancelReinforcement(PlayableOwner owner)
        {
            if (!reinforcementReservations.TryGetValue(owner, out var quote)) return PlayableCommandStatus.Applied;
            reinforcementReservations.Remove(owner);
            AddCredits(owner, quote.Credits);
            return PlayableCommandStatus.Applied;
        }

        private PlayableCommandStatus ConfirmReinforcement(PlayableOwner owner, PlayableCommand command, out string message)
        {
            message = null;
            if (eliminated.Contains(owner) || !Orbital(owner).CanUse(OrbitalAbilities.Reinforcement))
                return PlayableCommandStatus.Rejected;

            var wave = ReinforcementQuote(owner);
            if (!wave.Reserved)
            {
                if (command.Origin != PlayableOrderOrigin.Ai) return PlayableCommandStatus.Rejected;
                if (!CanAfford(owner, wave.Credits)) return PlayableCommandStatus.InsufficientCredits;
                if (!HasRoomForReinforcement(owner, wave))
                {
                    message = "Не хватает мест в армии";
                    return PlayableCommandStatus.Rejected;
                }
            }

            RefreshVision();
            var vision = Vision(owner);
            double radius = PlayableUnitRules.Radius(profile, wave.Kind);
            if (!navigation.Crowd.Locate(command.Target, 0, out var center) || !vision.IsVisible(command.Target))
                return PlayableCommandStatus.InvalidTarget;

            double clearance = OrbitalData.orbitalReinforcementClearance;
            int layer = navigation.Crowd.PhysicalLayer(center);
            bool Fits(NavPoint point)
            {
                if (!vision.IsVisible(point) || !navigation.Crowd.Locate(point, radius, out var location) ||
                    location.SurfaceId != center.SurfaceId || !Geometry.IsFree(point, radius, layer)) return false;
                foreach (var unit in units.Values)
                {
                    if (unit.Air != null || !TryActor(unit.Id, out var actor) ||
                        navigation.Crowd.PhysicalLayer(actor.Location) != layer) continue;
                    if (Distance(point, actor.Position) < radius + actor.Radius +
                        (Hostile(owner, unit.Owner) ? clearance : 0)) return false;
                }
                foreach (var building in buildings.Values)
                {
                    if (!navigation.Crowd.Locate(building.Position, 0, out var buildingLocation))
                    {
                        if (profile.AuthoredMap != null && !profile.AuthoredMap.TryLocate(building.Position, 0, out buildingLocation))
                            continue;
                    }
                    if (navigation.Crowd.PhysicalLayer(buildingLocation) != layer) continue;
                    double margin = radius + (Hostile(owner, building.Owner) ? clearance : 0);
                    if (OrbitalReinforcement.OverlapsBuilding(point, building.Position, BuildingRadius(building.Kind), margin)) return false;
                }
                return true;
            }

            var positions = OrbitalReinforcement.Layout(command.Target, wave, radius, Fits, out bool valid);
            if (!valid) return PlayableCommandStatus.InvalidTarget;

            int first = nextId;
            try
            {
                foreach (var point in positions)
                {
                    int id = SpawnUnitCore(point, owner, wave.Kind, recordArmy: false);
                    units[id].MaterializationUntil = elapsed + OrbitalData.orbitalReinforcementSeconds;
                    units[id].MaterializationDuration = OrbitalData.orbitalReinforcementSeconds;
                }
            }
            catch (Exception error) when (error is NavigationRetentionBudget.CapacityRefused ||
                NavigationCommandRefused.IsExpected(error))
            {
                for (int id = nextId - 1; id >= first; id--)
                {
                    if (!navigation.Crowd.Remove(id))
                        throw new InvalidOperationException("Could not roll back reinforcement actors.");
                    soundMotion.Remove(id);
                    units.Remove(id);
                }
                nextId = first;
                return PlayableCommandStatus.Overflow;
            }

            TrackUnitImmobility(elapsed + OrbitalData.orbitalReinforcementSeconds);
            if (wave.Reserved) reinforcementReservations.Remove(owner);
            else AddCredits(owner, -wave.Credits);
            if (!reinforcementNextTanks.Add(owner)) reinforcementNextTanks.Remove(owner);
            Orbital(owner).Use(OrbitalAbilities.Reinforcement, OrbitalData);
            RecordArmy(owner);
            if(command.Origin==PlayableOrderOrigin.Ai) AssignReinforcement(owner,command,first,positions.Length);
            Sound(PlayableSoundKind.OrbitalReinforcementStarted, command.Target, PlayableEntityKind.Tank, owner,
                phaseEndTick: AudioPhaseEnd(OrbitalData.orbitalReinforcementSeconds));
            return PlayableCommandStatus.Applied;
        }

        private void AssignReinforcement(PlayableOwner owner, PlayableCommand cast, int first, int count)
        {
            var recipients = Enumerable.Range(first, count);
            int target;
            PlayableCommandKind order;
            if (buildings.TryGetValue(cast.ParentId, out var objective) &&
                Hostile(owner, objective.Owner) && VisibleTarget(owner, objective.Id))
            {
                order = PlayableCommandKind.Attack;
                target = objective.Id;
                recipients = recipients.Concat(cast.EntityIds.Where(id => units.TryGetValue(id, out var scout) &&
                    scout.Owner == owner && scout.Kind == PlayableEntityKind.Explorer &&
                    scout.LastOrder?.Origin != PlayableOrderOrigin.Human));
            }
            else if (cast.ParentId == 0 && cast.EntityIds.Count == 1 &&
                units.TryGetValue(cast.EntityIds[0], out var leader) && leader.Owner == owner)
            {
                order = PlayableCommandKind.Follow;
                target = leader.Id;
            }
            else return;

            var command = new PlayableCommand(navigation.Generation, AllocateAiSequence(), cast.PlayerId,
                order, recipients.ToArray(), targetId:target, origin:cast.Origin, source:cast.Source,
                jobId:cast.JobId, actionId:cast.ActionId);
            ApplyTacticalPrepared(command, out _, out _);
        }

        private bool Materializing(Unit unit) => unit.MaterializationUntil > elapsed + 1e-9;

        private double MaterializationRemaining(Unit unit) =>
            Materializing(unit) ? unit.MaterializationUntil - elapsed : 0;

        private bool HasReinforcementState => reinforcementNextTanks.Count != 0 ||
            reinforcementReservations.Count != 0 || units.Values.Any(Materializing);

        private void WriteReinforcement(BinaryWriter writer)
        {
            writer.Write(reinforcementNextTanks.Count);
            foreach (var owner in reinforcementNextTanks.OrderBy(o => o)) writer.Write((int)owner);
            writer.Write(reinforcementReservations.Count);
            foreach (var entry in reinforcementReservations.OrderBy(entry => entry.Key))
            {
                writer.Write((int)entry.Key);
                writer.Write(entry.Value.Credits);
                writer.Write(entry.Value.Population);
            }
            var arriving = units.Values.Where(Materializing).OrderBy(unit => unit.Id).ToArray();
            writer.Write(arriving.Length);
            foreach (var unit in arriving)
            {
                writer.Write(unit.Id);
                WorldWire.Number(writer, unit.MaterializationUntil);
                WorldWire.Number(writer, unit.MaterializationDuration);
            }
        }

        private void ReadReinforcement(BinaryReader reader)
        {
            int nextCount = reader.ReadInt32();
            if (nextCount < 0 || nextCount > Owners.Count()) throw new ArgumentException("Invalid reinforcement cycle.");
            for (int i = 0; i < nextCount; i++)
            {
                var owner = WorldWire.EnumValue<PlayableOwner>(reader);
                if (!HasOwner(owner) || !reinforcementNextTanks.Add(owner))
                    throw new ArgumentException("Invalid reinforcement owner.");
            }
            int reservationCount = reader.ReadInt32();
            if (reservationCount < 0 || reservationCount > Owners.Count())
                throw new ArgumentException("Invalid reinforcement reservations.");
            for (int i = 0; i < reservationCount; i++)
            {
                var owner = WorldWire.EnumValue<PlayableOwner>(reader);
                int cost = reader.ReadInt32();
                int population = reader.ReadInt32();
                if (!HasOwner(owner) || !Orbital(owner).CanUse(OrbitalAbilities.Reinforcement) ||
                    reinforcementReservations.ContainsKey(owner) || cost < 0 || population < 0)
                    throw new ArgumentException("Invalid reinforcement reservation.");
                var wave = ReinforcementQuote(owner);
                reinforcementReservations.Add(owner, wave.AsReserved(cost, population));
            }
            int arriving = reader.ReadInt32();
            if (arriving < 0 || arriving > units.Count) throw new ArgumentException("Invalid materialization count.");
            for (int i = 0; i < arriving; i++)
            {
                int id = reader.ReadInt32();
                double until = WorldWire.Number(reader);
                double duration = WorldWire.Number(reader);
                if (!units.TryGetValue(id, out var unit) || unit.MaterializationDuration != 0 ||
                    unit.Kind != PlayableEntityKind.Explorer && unit.Kind != PlayableEntityKind.Tank ||
                    until <= elapsed || duration <= 0 || until - elapsed > duration + 1e-9)
                    throw new ArgumentException("Invalid materialization state.");
                unit.MaterializationUntil = until;
                unit.MaterializationDuration = duration;
                TrackUnitImmobility(until);
            }
        }
    }
}

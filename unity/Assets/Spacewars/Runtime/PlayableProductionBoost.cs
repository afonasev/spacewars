using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private const string BoostBlockedMessage = "Здание на ускоренной выработке";

        private sealed class ProductionBoost
        {
            public double Started, Until, Floor, WearPerSecond, Multiplier;
        }

        private readonly HashSet<int> boostWear = new HashSet<int>();
        private readonly Dictionary<int, ProductionBoost> boosts = new Dictionary<int, ProductionBoost>();

        private double BoostRemaining(int id) =>
            boosts.TryGetValue(id, out var boost) ? Math.Max(0, boost.Until - elapsed) : 0;

        private static double BoostedSeconds(ProductionBoost boost, double end, double seconds) =>
            Math.Max(0, Math.Min(end, boost.Until) - Math.Max(end - seconds, boost.Started));

        private double ExtraBoostWork(int id, double end, double seconds) =>
            boosts.TryGetValue(id, out var boost) ? BoostedSeconds(boost, end, seconds) * (boost.Multiplier - 1) : 0;

        private double BoostedWork(int id, double dt) => dt + ExtraBoostWork(id, elapsed, dt);

        private double BoostIncomeRate(Building building) =>
            OrbitalProductionBoost.Income(building.Kind) && boosts.TryGetValue(building.Id, out var boost) && boost.Until > elapsed
                ? boost.Multiplier : 1;

        private double? BoostWorkRemaining(Building building)
        {
            if (building.Kind == PlayableBuildingKind.ScientificCenter)
            {
                if (!research.TryGetValue(building.Owner, out var orders)) return null;
                var order = orders.FirstOrDefault(o => o.CenterId == building.Id && o.Active && !o.Complete);
                return order == null ? (double?)null : Math.Max(0, order.Duration - order.Elapsed);
            }
            if (!OrbitalProductionBoost.Production(building.Kind) || building.LaunchPhase != 0 ||
                building.Orders.Count == 0 || !building.Orders[0].Active) return null;
            return building.Orders[0].Remaining;
        }

        private PlayableCommandStatus StartBoost(PlayableOwner owner, int id)
        {
            if (eliminated.Contains(owner) || !Orbital(owner).CanUse(OrbitalAbilities.AcceleratedProduction))
                return PlayableCommandStatus.Rejected;
            if (!buildings.TryGetValue(id, out var building) || TeamOf(owner) != TeamOf(building.Owner) ||
                !building.Ready || building.Sale != null || building.Repair != null || SystemsDisabled(id) ||
                BoostRemaining(id) > 1e-9 ||
                (!OrbitalProductionBoost.Income(building.Kind) && !OrbitalProductionBoost.Production(building.Kind)))
                return PlayableCommandStatus.InvalidTarget;

            var data = OrbitalData;
            double floor = OrbitalProductionBoost.HealthFloor(building.Kind, data);
            double fraction = building.Health / BuildingMaximumHealth(building.Owner, building.Kind);
            RefreshVision();
            if (fraction <= floor + 1e-9 || !Vision(owner).IsVisible(building.Position))
                return PlayableCommandStatus.InvalidTarget;

            Orbital(owner).Use(OrbitalAbilities.AcceleratedProduction, data);
            boosts[id] = new ProductionBoost
            {
                Started = elapsed, Until = elapsed + data.orbitalBoostSeconds, Floor = floor,
                WearPerSecond = (fraction - floor) / data.orbitalBoostSeconds,
                Multiplier = OrbitalProductionBoost.Income(building.Kind) ? data.orbitalBoostIncomeMultiplier : data.orbitalBoostProductionMultiplier
            };
            Sound(PlayableSoundKind.OrbitalProductionBoosted, building.Position, PlayableEntityKind.Tank, owner);
            return PlayableCommandStatus.Applied;
        }

        private void AdvanceBoostWear(double dt)
        {
            foreach (var entry in boosts)
            {
                double seconds = BoostedSeconds(entry.Value, elapsed, dt);
                if (seconds <= 0 || !buildings.TryGetValue(entry.Key, out var building)) continue;
                double max = BuildingMaximumHealth(building.Owner, building.Kind);
                double floor = entry.Value.Floor * max;
                if (building.Health <= floor) continue;
                boostWear.Add(entry.Key);
                double next = building.Health - entry.Value.WearPerSecond * seconds * max;
                building.Health = entry.Value.Until <= elapsed + 1e-9 ? floor : Math.Max(floor, next);
            }
        }

        private void RemoveSettledBoosts()
        {
            if (boosts.Count == 0) return;
            double settled = elapsed - incomeClock;
            foreach (int id in boosts.Where(p => p.Value.Until <= settled + 1e-9).Select(p => p.Key).ToArray())
                boosts.Remove(id);
        }

        private void WriteBoosts(BinaryWriter writer)
        {
            writer.Write(boosts.Count);
            foreach (var pair in boosts.OrderBy(p => p.Key))
            {
                writer.Write(pair.Key);
                WorldWire.Number(writer, pair.Value.Started);
                WorldWire.Number(writer, pair.Value.Until);
                WorldWire.Number(writer, pair.Value.Floor);
                WorldWire.Number(writer, pair.Value.WearPerSecond);
                WorldWire.Number(writer, pair.Value.Multiplier);
            }
            writer.Write(boostWear.Count);
            foreach(int id in boostWear.OrderBy(id => id))writer.Write(id);
            var timers = orbital.Where(p => p.Value.ProvingGroundProductionRecovery > 0).OrderBy(p => p.Key).ToArray();
            writer.Write(timers.Length);
            foreach (var pair in timers)
            {
                writer.Write((int)pair.Key);
                WorldWire.Number(writer, pair.Value.ProvingGroundProductionRecovery);
            }
        }

        private void ReadBoosts(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > buildings.Count) throw new ArgumentException("Invalid accelerated production count.");
            for (int i = 0; i < count; i++)
            {
                int id = reader.ReadInt32();
                var boost = new ProductionBoost
                {
                    Started = WorldWire.Number(reader), Until = WorldWire.Number(reader),
                    Floor = WorldWire.Number(reader), WearPerSecond = WorldWire.Number(reader), Multiplier = WorldWire.Number(reader)
                };
                if (!buildings.TryGetValue(id, out var building) || !building.Ready || SystemsDisabled(id) ||
                    building.Sale != null || building.Repair != null || boosts.ContainsKey(id) ||
                    (!OrbitalProductionBoost.Income(building.Kind) && !OrbitalProductionBoost.Production(building.Kind)) ||
                    boost.Started < 0 || boost.Started > elapsed || boost.Until <= boost.Started ||
                    boost.Floor <= 0 || boost.Floor >= 1 || boost.WearPerSecond <= 0 || boost.Multiplier < 1 || boost.Multiplier > 10)
                    throw new ArgumentException("Invalid accelerated production state.");
                boosts.Add(id, boost);
            }
            int worn = reader.ReadInt32();
            if (worn < 0 || worn > buildings.Count) throw new ArgumentException("Invalid accelerated production wear count.");
            for(int i=0;i<worn;i++)
            {
                int id=reader.ReadInt32();
                if(!buildings.ContainsKey(id)||!boostWear.Add(id))throw new ArgumentException("Invalid accelerated production wear state.");
            }
            int timers = reader.ReadInt32();
            if (timers < 0 || timers > orbital.Count) throw new ArgumentException("Invalid accelerated production timer count.");
            var owners = new HashSet<PlayableOwner>();
            for (int i = 0; i < timers; i++)
            {
                var owner = WorldWire.EnumValue<PlayableOwner>(reader);
                if (!owners.Add(owner) || !orbital.TryGetValue(owner, out var state))
                    throw new ArgumentException("Invalid accelerated production timer owner.");
                state.RestoreProvingGroundProduction(WorldWire.Number(reader));
            }
        }
    }
}

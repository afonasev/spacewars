using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private bool RepairTowerAvailable(Building tower) => tower.Kind == PlayableBuildingKind.RepairTower &&
            tower.Ready && tower.Health > 0 && tower.Sale == null &&
            !SystemsDisabled(tower.Id) && !eliminated.Contains(tower.Owner);

        private bool RepairTarget(PlayableOwner owner, int source, int target, out NavPoint point, out double ratio)
        {
            point = default;
            ratio = 1;
            if (source == target) return false;
            if (units.TryGetValue(target, out var unit))
            {
                if (Hostile(owner, unit.Owner) || !CanReceiveFieldRepair(unit) || !TryActor(target, out var actor)) return false;
                point = actor.Position;
                ratio = unit.Health / UnitMaximumHealth(unit.Owner, unit.Kind);
                return true;
            }
            if (!buildings.TryGetValue(target, out var building) || Hostile(owner, building.Owner) ||
                !CanReceiveBuildingRepair(building)) return false;
            point = building.Position;
            ratio = building.Health / BuildingMaximumHealth(building.Owner, building.Kind);
            return true;
        }

        private bool CanReceiveBuildingRepair(Building building) => building.Ready && building.Health > 0 &&
            building.Sale == null && building.Health < BuildingMaximumHealth(building.Owner, building.Kind);

        private double RepairTargetHeight(int id, NavPoint point) => buildings.TryGetValue(id, out var building)
            ? GroundHeight(point) + BuildingHeightForAir(building.Kind) * .5
            : TargetHeight(id, point);

        private void HealRepairTarget(int id, double amount)
        {
            if (units.TryGetValue(id, out var unit)) HealUnit(unit, amount);
            else if (buildings.TryGetValue(id, out var building)) HealBuilding(building, amount);
        }

        private void HealBuilding(Building building, double amount)
        {
            double maximum = BuildingMaximumHealth(building.Owner, building.Kind);
            double health = Math.Min(maximum, building.Health + amount);
            if (health <= building.Health) return;
            building.Health = health;
            repairedUnits.Add(building.Id);
            if (health >= maximum - 1e-9 && building.Repair != null) CompletePaidRepair(building);
        }

        private bool RepairTowerLegal(Building tower, int target, out NavPoint point, out double ratio) =>
            RepairTarget(tower.Owner, tower.Id, target, out point, out ratio) &&
            Distance(tower.Position, point) <= profile.RepairTowerRange;

        private PlayableRepairTowerSnapshot RepairTowerSnapshot(Building tower, PlayableOwner? observer = null)
        {
            if (SystemsDisabled(tower.Id) || tower.Kind != PlayableBuildingKind.RepairTower) return null;
            var state = tower.Healing;
            int target = state != null && RepairTowerAvailable(tower) &&
                RepairTowerLegal(tower, state.Target, out var point, out _) &&
                (!observer.HasValue || Vision(observer.Value).IsVisible(point)) ? state.Target : 0;
            return new PlayableRepairTowerSnapshot(target, state?.Heading ?? Math.PI / 2, state?.Pitch ?? 0);
        }

        private void AdvanceRepairTowers(double dt)
        {
            foreach (var tower in buildings.Values.Where(b => b.Kind == PlayableBuildingKind.RepairTower).OrderBy(b => b.Id))
            {
                if (!RepairTowerAvailable(tower))
                {
                    if (tower.Healing != null) tower.Healing.Target = 0;
                    continue;
                }

                var state = tower.Healing ?? (tower.Healing = new RepairTowerState());
                if (!RepairTowerLegal(tower, state.Target, out var point, out _))
                {
                    bool preferWorst = !state.Initialized || state.Target != 0;
                    double lowestRatio = double.PositiveInfinity;
                    state.Target = 0;
                    foreach (int id in units.Keys.Concat(buildings.Keys))
                    {
                        if (!RepairTowerLegal(tower, id, out var candidate, out double ratio)) continue;
                        if (!preferWorst) ratio = 0;
                        if (ratio > lowestRatio || (ratio == lowestRatio && id >= state.Target)) continue;
                        lowestRatio = ratio;
                        state.Target = id;
                        point = candidate;
                    }
                }

                state.Initialized = true;
                if (state.Target == 0) continue;
                double distance = Distance(tower.Position, point);
                double heading = distance < 1e-9 ? state.Heading :
                    Math.Atan2(point.Z - tower.Position.Z, point.X - tower.Position.X);
                double emitterHeight = GroundHeight(tower.Position) + BuildingCombatGeometry.ApronLift +
                    RepairTowerGeometry.PitchHeight * profile.TurretModelScale;
                double pitch = Math.Max(-profile.TurretDepression * Math.PI / 180, Math.Min(Math.PI / 2,
                    Math.Atan2(RepairTargetHeight(state.Target, point) - emitterHeight, distance)));
                state.Heading = Turn(state.Heading, heading, profile.TurretYawSpeed * Math.PI / 180 * dt);
                state.Pitch = Turn(state.Pitch, pitch, profile.TurretPitchSpeed * Math.PI / 180 * dt);
                HealRepairTarget(state.Target, profile.RepairTowerHealthPerSecond * dt);
            }
        }
    }
}

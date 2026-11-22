using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableDomain
    {
        private struct DroneRepairState
        {
            public int Target, BeamTarget;
            public bool Detour, Returning;
            public NavPoint Anchor;
        }

        private PlayableCommandStatus OrderDroneRepair(PlayableCommand command, PlayableOwner owner, out string message)
        {
            message = "Выберите повреждённую цель для ремонта";
            if (command.Mode != PlayableOrderMode.Replace) return PlayableCommandStatus.InvalidTarget;
            int[] ids = command.CopyEntityIds();
            var invalid = MoveRecipientFailure(ids, owner);
            if (invalid.HasValue) return invalid.Value;
            if (ids.Any(id => units[id].Kind != PlayableEntityKind.Skat || id == command.TargetId) ||
                !RepairTarget(owner, 0, command.TargetId, out var point, out _) ||
                !Vision(owner).IsVisible(point)) return PlayableCommandStatus.InvalidTarget;

            foreach (int id in ids.OrderBy(id => id))
            {
                var unit = units[id];
                var flight = unit.Air;
                flight.Deferred.Clear();
                flight.Actor.Held = false;
                SetDroneGoal(unit, flight.Actor.Position, false);
                unit.HasAttackMove = false;
                unit.DroneRepair = new DroneRepairState { Target = command.TargetId };
                unit.LastOrder = new PlayableOrderStamp(id, owner, navigation.Generation, nextOrderRevision++,
                    command.Sequence, Tick, command.Origin, command.Kind, command.Source, command.JobId, command.ActionId);
                unit.CurrentOrder = new PlayableTacticalOrderSnapshot(id, owner, navigation.Generation, command.Sequence,
                    Tick, PlayableTacticalOrderKind.Repair, point, command.TargetId);
            }

            message = "Ремонт назначен";
            return PlayableCommandStatus.Applied;
        }

        private bool DroneBeamClear(NavPoint from, double altitude, int target, NavPoint at)
        {
            var start = new BallisticPoint(from.X, altitude, from.Z);
            var end = new BallisticPoint(at.X, RepairTargetHeight(target, at), at.Z);
            // Terrain supports above the target are tested through solid geometry.
            // Using them as the ground height would also hide targets below a deck.
            var terrain = profile.AuthoredMap;
            double SupportBelowBeam(NavPoint point)
            {
                if (terrain == null) return 0;
                double height = double.NegativeInfinity;
                foreach (var surface in terrain.Supports)
                {
                    if (!surface.Contains(point)) continue;
                    double candidate = surface.HeightAt(point);
                    if (candidate <= end.Y) height = Math.Max(height,candidate);
                }
                return height;
            }
            if (buildings.TryGetValue(target, out var structure))
                end = BuildingCombat(structure).Closest(start);
            if (!PlayableBallistics.TerrainLineClear(start, end, 0, SolidObstacles(),
                profile.BallisticWallHeight, SupportBelowBeam)) return false;
            foreach (var building in buildings.Values)
                if (building.Id != target && building.Phase != ConstructionPhase.Pending &&
                    Distance(building.Position, from) <= profile.SkatRepairRange + BuildingRadius(building.Kind) &&
                    BuildingCombat(building).Segment(start, end, 0, out _, out _)) return false;
            return true;
        }

        private bool DroneBeamReaches(Unit unit, int target, NavPoint point)
        {
            var flight = unit.Air;
            return Distance(flight.Actor.Position, point) <= profile.SkatRepairRange &&
                DroneBeamClear(flight.Actor.Position, flight.Height, target, point);
        }

        private bool DroneApproach(Unit unit, int target, NavPoint point, NavPoint? anchor, out NavPoint goal)
        {
            goal = default;
            double best = double.PositiveInfinity;
            var from = unit.Air.Actor.Position;
            var approaches = PlayableUnitRules.AttackApproachCandidates(profile, from, point, profile.SkatRepairRange);
            double leash = profile.CombatResponsePursuitRadius;
            var edge = anchor.HasValue ? EdgeToward(anchor.Value, point, leash) : point;
            foreach (var candidate in approaches.Append(edge))
            {
                if (Distance(candidate, point) > profile.SkatRepairRange) continue;
                if (!AirInside(candidate) || (anchor.HasValue && Distance(anchor.Value, candidate) > leash + 1e-8) ||
                    !DroneBeamClear(candidate, ClearHeight(candidate), target, point)) continue;
                double distance = Distance(unit.Air.Actor.Position, candidate);
                if (distance >= best) continue;
                best = distance;
                goal = candidate;
            }
            return !double.IsPositiveInfinity(best);
        }

        private static NavPoint EdgeToward(NavPoint anchor, NavPoint target, double distance)
        {
            double length = Distance(anchor, target);
            if (length <= distance || length < 1e-9) return target;
            double scale = distance / length;
            return new NavPoint(anchor.X + (target.X-anchor.X)*scale, anchor.Z + (target.Z-anchor.Z)*scale);
        }

        private int FindDroneTarget(Unit unit, bool canIntercept)
        {
            int best = 0;
            double closest = double.PositiveInfinity;
            var position = unit.Air.Actor.Position;
            var vision = Vision(unit.Owner);
            foreach (int id in units.Keys.Concat(buildings.Keys))
            {
                if (best != 0 && units.ContainsKey(best) && buildings.ContainsKey(id)) break;
                if (!RepairTarget(unit.Owner, unit.Id, id, out var point, out _) || !vision.IsVisible(point)) continue;
                double distance = Distance(position, point);
                if (distance > closest || (distance == closest && id >= best)) continue;
                if (!DroneBeamReaches(unit, id, point) &&
                    (!canIntercept || distance > PlayableUnitRules.Vision(profile, unit.Kind) ||
                    !DroneApproach(unit, id, point, position, out _))) continue;
                best = id;
                closest = distance;
            }
            return best;
        }

        private void SetDroneGoal(Unit unit, NavPoint goal, bool moving)
        {
            var flight = unit.Air;
            var actor = flight.Actor;
            if (actor.Goal.Equals(goal) && actor.Moving == moving) return;
            flight.Short = false;
            flight.Escape = false;
            actor.Goal = goal;
            actor.Moving = moving;
            actor.Outcome = moving ? NavigationOutcome.Moving : NavigationOutcome.Stopped;
            NotifyFlightDecision(unit);
        }

        private void FinishDroneRepair(Unit unit)
        {
            ref var repair = ref unit.DroneRepair;
            repair.Target = 0;
            if (repair.Detour)
            {
                repair.Returning = true;
                SetDroneGoal(unit, repair.Anchor, true);
            }
            else if (unit.CurrentOrder?.Kind == PlayableTacticalOrderKind.Repair)
            {
                unit.CurrentOrder = null;
                SetDroneGoal(unit, unit.Air.Actor.Position, false);
            }
        }

        private void AdvanceRepairDrones(double dt)
        {
            foreach (var unit in units.Values)
            {
                if (unit.Kind != PlayableEntityKind.Skat || Frozen(unit) || Materializing(unit) ||
                    unit.Air.Departing || eliminated.Contains(unit.Owner)) continue;
                ref var repair = ref unit.DroneRepair;
                repair.BeamTarget = 0;
                var actor = unit.Air.Actor;
                if (repair.Returning)
                {
                    if (actor.Moving || Distance(default, unit.Air.Velocity) > 1e-5) continue;
                    repair = default;
                    if (unit.CurrentOrder?.Kind == PlayableTacticalOrderKind.AttackMove)
                        SetDroneGoal(unit, unit.CurrentOrder.Destination, true);
                    continue;
                }

                bool single = unit.CurrentOrder?.Kind == PlayableTacticalOrderKind.Repair;
                bool intercept = !actor.Held && (unit.CurrentOrder == null || unit.CurrentOrder.Kind == PlayableTacticalOrderKind.AttackMove);
                if (repair.Target == 0)
                {
                    repair.Target = FindDroneTarget(unit, intercept && unit.CurrentOrder != null);
                    if (repair.Target == 0) continue;
                    if (intercept)
                    {
                        repair.Detour = true;
                        repair.Anchor = actor.Position;
                    }
                }

                if (!RepairTarget(unit.Owner, unit.Id, repair.Target, out var point, out _) ||
                    !Vision(unit.Owner).IsVisible(point))
                {
                    FinishDroneRepair(unit);
                    continue;
                }

                if (DroneBeamReaches(unit, repair.Target, point))
                {
                    if (repair.Detour || single)
                        SetDroneGoal(unit, actor.Position, false);
                    repair.BeamTarget = repair.Target;
                    HealRepairTarget(repair.Target, profile.SkatRepairRate * dt);
                    continue;
                }

                NavPoint? anchor = single ? (NavPoint?)null : repair.Anchor;
                if ((repair.Detour || single) && DroneApproach(unit, repair.Target, point, anchor, out var goal))
                    SetDroneGoal(unit, goal, true);
                else FinishDroneRepair(unit);
            }
        }
    }
}

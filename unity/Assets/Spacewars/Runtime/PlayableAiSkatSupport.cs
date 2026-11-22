using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    internal sealed partial class PlayableAiOwnerLoop
    {
        private static readonly int[] SkatPopulationRatios = { 15, 20, 25, 30, 35 };

        internal static int SkatPopulationRatio(int seed, string owner)
        {
            unchecked
            {
                uint hash = (uint)seed ^ 2166136261;
                foreach (char character in owner) hash = (hash ^ character) * 16777619;
                hash = (hash ^ (hash >> 16)) * 2246822519u;
                return SkatPopulationRatios[(int)(hash % (uint)SkatPopulationRatios.Length)];
            }
        }

        private static bool ReadySkatBuilding(PlayableBuildingSnapshot building) =>
            building.Phase == ConstructionPhase.Ready && building.Health > 0 && !building.SystemsDisabled &&
            building.PrivateState?.Lifecycle?.Selling != true;

        private bool SkatHangarOpportunity(PlayableAiObservation view)
        {
            var buildings = view.Buildings.Where(b => b.Owner == view.Owner).ToArray();
            return buildings.Count(b => b.Kind == PlayableBuildingKind.Factory && ReadySkatBuilding(b)) >= 2 &&
                buildings.Any(b => b.Kind == PlayableBuildingKind.ScientificCenter && ReadySkatBuilding(b)) &&
                !buildings.Any(b => b.Kind == PlayableBuildingKind.Hangar && b.Health > 0 && b.PrivateState?.Lifecycle?.Selling != true);
        }

        private PlayableAiAction PlanSkatSupport(PlayableAiObservation view, long freeCredits)
        {
            if (freeCredits < profile.SkatCreditCost ||
                !view.Buildings.Any(b => b.Owner == view.Owner && b.Kind == PlayableBuildingKind.ScientificCenter && ReadySkatBuilding(b)))
                return null;

            var forces = view.Entities.Where(unit => unit.Owner == view.Owner && unit.Health > 0).ToArray();
            int combat = forces.Where(unit => unit.Kind != PlayableEntityKind.Skat)
                .Sum(unit => PlayableUnitRules.Population(profile, unit.Kind));
            int desired = combat / SkatPopulationRatio(view.Seed, view.OwnerId);
            var buildings = view.Buildings.Where(b => b.Owner == view.Owner).ToArray();
            int ordered = buildings.Where(b => b.PrivateState != null).SelectMany(b => b.PrivateState.Orders)
                .Count(order => !order.IsUpgrade && order.Kind == PlayableEntityKind.Skat);
            ordered += pendingItems.Count(item => item.Action.Kind == PlayableCommandKind.QueueSkat);
            if (forces.Count(unit => unit.Kind == PlayableEntityKind.Skat) + ordered >= desired) return null;

            foreach (var hangar in buildings.Where(b => b.Kind == PlayableBuildingKind.Hangar && ReadySkatBuilding(b))
                .OrderBy(b => b.PrivateState.QueueCount).ThenBy(b => b.Id))
            {
                var action = new PlayableAiAction(1, view.OwnerId, view.ProfileId, view.ProfileRevision, view.Generation,
                    view.SnapshotSequence, PlayableCommandKind.QueueSkat, new[] { hangar.Id },
                    unitKind: PlayableEntityKind.Skat, seed: view.Seed, sourceIdentity: opening.SourceIdentity);
                if (AiEconomyAdmission.Reject(view, profile, action, idleProducer:true, catalog: roster) == null) return action;
            }

            if (composition.Capture().Phase != AiGamePhase.Late || !SkatHangarOpportunity(view) || freeCredits < profile.HangarCreditCost + profile.SkatCreditCost ||
                pendingItems.Any(item => item.Action.Kind == PlayableCommandKind.BuildAt &&
                    item.Action.BuildingKind == PlayableBuildingKind.Hangar)) return null;

            foreach (var site in view.Sites.Where(site => site.Owner == view.Owner && site.Ready && !site.Contested)
                .OrderBy(site => site.Site.Id))
            foreach (var slot in site.Site.Slots.OrderBy(slot => slot.Id))
            {
                var action = new PlayableAiAction(1, view.OwnerId, view.ProfileId, view.ProfileRevision, view.Generation,
                    view.SnapshotSequence, PlayableCommandKind.BuildAt, siteId: site.Site.Id, slotId: slot.Id,
                    buildingKind: PlayableBuildingKind.Hangar, parentId: site.CenterId, unitKind: PlayableEntityKind.Skat,
                    seed: view.Seed, sourceIdentity: opening.SourceIdentity);
                if (!expansion.ClaimsSite(action.SiteId) && !infrastructure.ClaimsSlot(action.SiteId, action.SlotId) &&
                    AiEconomyAdmission.Reject(view, profile, action, catalog: roster) == null) return action;
            }
            return null;
        }

        private void AssignSkatSupport(PlayableAiObservation view)
        {
            var drones = view.Entities.Where(unit => unit.Owner == view.Owner && unit.Kind == PlayableEntityKind.Skat &&
                unit.Health > 0 && !unit.Departing && !unit.Held).ToArray();
            foreach (var drone in drones.Where(unit => armies.ArmyFor(unit.Id) == 0 &&
                !pendingItems.Any(item => item.Action.EntityIds.Contains(unit.Id))).OrderBy(unit => unit.Id))
            {
                var group = armies.Capture().Armies.Where(army => army.Major && army.Phase != AiArmyPhase.Disbanded &&
                    view.Entities.Any(unit => unit.Owner == view.Owner && unit.Kind != PlayableEntityKind.Skat &&
                        unit.Health > 0 && army.Members.Contains(unit.Id)))
                    .OrderBy(army => drones.Count(other => army.Members.Contains(other.Id)))
                    .ThenBy(army => army.Id).FirstOrDefault();
                if (group != null) armies.TryReinforce(view, group.Id, group.TacticalOwner, new[] { drone.Id });
            }
        }
    }
}

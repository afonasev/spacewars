using System;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiStasisOrbitalTests
    {
        private readonly PlayableProfile profile = PlayableProfile.Default;

        private PlayableEntitySnapshot Unit(int id, double x, double z, bool hostile = false, int health = 100,
            int target = 0, PlayableTacticalOrderSnapshot order = null) => new PlayableEntitySnapshot(id,
            hostile ? PlayableOwner.Enemy : PlayableOwner.Player, PlayableEntityKind.Tank, new NavPoint(x, z),
            health, order != null, target, 0, 0, order, maxHealth: profile.TankHealth);

        private AiStasisOrbitalPolicy Policy(PlayableEntitySnapshot[] units, PlayableProjectileSnapshot[] shots = null,
            PlayableBuildingSnapshot[] buildings = null, PlayableCenterDamageSnapshot[] centerDamage = null) =>
            new AiStasisOrbitalPolicy(PlayableAiObservation.From(Snapshot(units, shots, buildings, centerDamage)),
                profile, profile.CopyData());

        private PlayableSnapshot Snapshot(PlayableEntitySnapshot[] units, PlayableProjectileSnapshot[] shots,
            PlayableBuildingSnapshot[] buildings = null, PlayableCenterDamageSnapshot[] centerDamage = null)
        {
            var vision = new PlayableVision(1, 50, 50, 2, 0.2);
            vision.Refresh(new[] { new VisionSource(default, 40) }, Array.Empty<KnownBuilding>());
            return new PlayableSnapshot(profile.ProfileId, profile.Revision, 1, 7, 1, 30,
                RuntimeStatus.Running, false, PlayableMatchOutcome.Playing, 0, null, units,
                buildings ?? Array.Empty<PlayableBuildingSnapshot>(), shots ?? Array.Empty<PlayableProjectileSnapshot>(),
                new PlayableRuntimeMetrics(0, 0, 0, 0, 0), null, vision: vision.Snapshot(), ownCenterDamage: centerDamage);
        }

        [Test]
        public void OrbitalPlannerIssuesStasisAndRevalidatesBeforeDelivery()
        {
            double range = profile.TankRange;
            var units = new[] { Unit(1, 0, 0, health: 1), Unit(2, range * .5, range * .45),
                Unit(3, range * .8, 0, hostile: true) };
            var shot = new PlayableProjectileSnapshot(1, 3, 1, new NavPoint(range * .6, 0));
            var ready = new OrbitalEscalationSnapshot(3, 0, 100, 0, 0,
                new[] { new OrbitalAbilitySnapshot(OrbitalAbilities.Stasis, 0, 60, 1) }, Array.Empty<int>());
            var observation = PlayableAiObservation.From(Snapshot(units, new[] {shot}).WithOrbital(ready));
            var action = AiOrbitalPolicy.Plan(observation, profile, AiProfile.Initial, null, AiPersonality.Aggressive,
                Array.Empty<PlayableAiTraceRecord>(), PlayableAiOpeningComposition.SourceIdentity);
            Assert.NotNull(action);
            Assert.AreEqual(PlayableCommandKind.UseOrbital, action.Kind);
            Assert.AreEqual(OrbitalAbilities.Stasis, action.TargetId);
            Assert.True(AiOrbitalPolicy.CanDeliver(observation, profile, AiProfile.Initial, null, action));
            var safe = PlayableAiObservation.From(Snapshot(units, null).WithOrbital(ready));
            Assert.False(AiOrbitalPolicy.CanDeliver(safe, profile, AiProfile.Initial, null, action));
        }

        [Test]
        public void RescueRequiresObservedIncomingFireAndAFreeAlly()
        {
            double range = profile.TankRange;
            var units = new[] { Unit(1, 0, 0, health: 1), Unit(2, range * .5, range * .45),
                Unit(3, range * .8, 0, hostile: true) };
            var shot = new PlayableProjectileSnapshot(1, 3, 1, new NavPoint(range * .6, 0));
            Assert.False(Policy(units).WorthCasting(default));
            Assert.True(Policy(units, new[] {shot}).WorthCasting(default));
        }

        [Test]
        public void FreezeCanChangeLocalBalanceButWillNotSaveAlreadyFocusedEnemy()
        {
            double range = profile.TankRange;
            var point = new NavPoint(range * .65, -1);
            var units = new[] { Unit(1, 0, 0), Unit(2, 0, .8), Unit(3, point.X, point.Z, hostile: true),
                Unit(4, point.X, point.Z + .8, hostile: true), Unit(5, range * .65, 1.9, hostile: true, health: 1) };
            Assert.True(Policy(units).WorthCasting(point));
            units[0] = Unit(1, 0, 0, target: 5);
            Assert.False(Policy(units).WorthCasting(units[4].Position));
        }

        [Test]
        public void RearDefenseUsesObservedRecentDamageToTheCenter()
        {
            double range = profile.TankRange;
            var enemy = Unit(2, range * .8, 0, hostile: true);
            var units = new[] { Unit(1, -range, 0), enemy };
            var center = new PlayableBuildingSnapshot(10, PlayableOwner.Player,
                PlayableBuildingKind.Headquarters, default, 1, 1, 0, 0, default);
            var damage = new PlayableCenterDamageSnapshot(10, PlayableOwner.Player, 2, 1, 30, 15);
            Assert.True(Policy(units, buildings: new[] {center}, centerDamage: new[] {damage}).WorthCasting(enemy.Position));
        }

        [Test]
        public void WithdrawalCastTargetsThePursuerAndKeepsTheRetreatingUnitMobile()
        {
            double range = profile.TankRange;
            var goal = new NavPoint(2 * range, 0);
            var move = new PlayableTacticalOrderSnapshot(1, PlayableOwner.Player, 1, 1, 1,
                PlayableTacticalOrderKind.Move, goal, 0);
            var units = new[] { Unit(1, range - .1, 0, order: move), Unit(2, 0, 0, hostile: true) };
            var shot = new PlayableProjectileSnapshot(1, 2, 1, new NavPoint(1, 0));
            Assert.True(Policy(units, new[] {shot}).WorthCasting(default));
            Assert.False(Policy(units, new[] {shot}).WorthCasting(units[0].Position));
        }
    }
}

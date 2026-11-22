using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class SelectedAbilityPhaseAudioTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private PlayableDomain domain;
        private OfflineMatchConfiguration configuration;
        private long sequence;
        private object Call(PlayableDomain target, string name, params object[] args) =>
            typeof(PlayableDomain).GetMethod(name, Flags).Invoke(target, args);
        private PlayableSnapshot View(PlayableDomain target) => (PlayableSnapshot)Call(target, "PlayerSnapshot",
            1L, RuntimeStatus.Running, false, new PlayableRuntimeMetrics(0,0,0,0,0), null, 7, PlayableOwner.Player);
        private PlayableCommandStatus Send(PlayableCommandKind kind, int ability, NavPoint point = default) =>
            (PlayableCommandStatus)Call(domain, "Apply", new PlayableCommand(1, ++sequence, "player-1", kind,
                null, point, targetId: ability), null);

        [SetUp] public void Setup()
        {
            var map = new AiTestMap();
            var data = PlayableProfile.Default.CopyData();
            // Deliberately different from the 3s/2.8s audition offsets.
            data.orbitalReinforcementSeconds = 4;
            data.orbitalStasisSeconds = 4;
            var profile = PlayableProfile.Create(data, map);
            configuration = map.Configuration(profile, 7, new[] {
                new OfflineParticipant("player-1",1,1,OfflineControl.Human),
                new OfflineParticipant("player-2",2,2,OfflineControl.Human)
            });
            domain = new PlayableDomain(profile, 1L, configuration);
            Call(domain, "AddCredits", PlayableOwner.Player, 10000d);
            Call(domain, "CreditOrbital", (PlayableOwner?)PlayableOwner.Player, PlayableOwner.Enemy,
                data.orbitalThreshold1 + data.orbitalThreshold2 + data.orbitalThreshold3);
            foreach (int choice in new[] {1, 2})
                Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.ChooseOrbital, choice));
        }

        [TearDown] public void Cleanup() => domain?.DestroyOwnedNavigation();

        private void Cast(int ability)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.ChooseOrbital, ability));
            if (ability == OrbitalAbilities.Reinforcement)
            {
                Assert.AreEqual(PlayableCommandStatus.Rejected, Send(PlayableCommandKind.UseOrbital, ability));
                Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.BeginOrbitalReinforcement, ability));
            }
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,
                Send(PlayableCommandKind.UseOrbital, ability, new NavPoint(double.NaN,0)));
            Assert.IsFalse(View(domain).Sounds.Any(sound => PlayableSoundPolicy.Ability(sound.Kind)));
            if (ability == OrbitalAbilities.Stasis)
            {
                var unit = View(domain).Entities.First(entity => entity.Owner == PlayableOwner.Player);
                Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.UseOrbital, ability, unit.Position));
            }
            else
            {
                var home = View(domain).Buildings.First(building => building.Owner == PlayableOwner.Player &&
                    building.Kind == PlayableBuildingKind.Headquarters);
                bool applied = false;
                for (int x = -8; x <= 8 && !applied; x += 2)
                    for (int z = -8; z <= 8 && !applied; z += 2)
                        applied = Send(PlayableCommandKind.UseOrbital, ability,
                            new NavPoint(home.Position.X+x,home.Position.Z+z)) == PlayableCommandStatus.Applied;
                Assert.True(applied);
            }
            var start = View(domain).Sounds.Single(sound => PlayableSoundPolicy.Ability(sound.Kind));
            Assert.AreEqual(120, start.PhaseEndTick);
        }

        private void AdvanceEffect(PlayableDomain target, double elapsed, double dt)
        {
            typeof(PlayableDomain).GetField("elapsed", Flags).SetValue(target, elapsed);
            Call(target, "AdvanceOrbital", dt);
        }

        [TestCase(OrbitalAbilities.Reinforcement, PlayableSoundKind.OrbitalReinforcementReady)]
        [TestCase(OrbitalAbilities.Stasis, PlayableSoundKind.OrbitalStasisReleased)]
        public void CompletionFollowsActualDeadlineOnceIncludingRestore(int ability, PlayableSoundKind completed)
        {
            Cast(ability);
            var bytes = (byte[])Call(domain, "CaptureWorldBytes", 7, configuration.SourceIdentity);
            var restored = OfflineParticipantAuthority.Restore(bytes, configuration);
            var other = (PlayableDomain)typeof(OfflineParticipantAuthority).GetProperty("Domain", Flags).GetValue(restored);
            try
            {
                CollectionAssert.AreEqual(bytes, restored.CaptureBytes());
                Assert.IsFalse(View(other).Sounds.Any(sound => PlayableSoundPolicy.Ability(sound.Kind)), "restore does not replay activation");
                foreach (var target in new[] {domain, other})
                {
                    AdvanceEffect(target, 3.5, 3.5);
                    Assert.IsFalse(View(target).Sounds.Any(sound => sound.Kind == completed), "no demo-timed completion");
                    AdvanceEffect(target, 4, .5);
                    Assert.AreEqual(1, View(target).Sounds.Count(sound => sound.Kind == completed), "one completion for the group");
                    AdvanceEffect(target, 4.5, .5);
                    Assert.AreEqual(1, View(target).Sounds.Count(sound => sound.Kind == completed), "no repeated completion");
                }
            }
            finally { other.DestroyOwnedNavigation(); }
        }

        [TestCase(OrbitalAbilities.Reinforcement, PlayableSoundKind.OrbitalReinforcementReady)]
        [TestCase(OrbitalAbilities.Stasis, PlayableSoundKind.OrbitalStasisReleased)]
        public void LostGroupDoesNotAnnounceReadiness(int ability, PlayableSoundKind completed)
        {
            Cast(ability);
            var units = (System.Collections.IDictionary)typeof(PlayableDomain).GetField("units", Flags).GetValue(domain);
            // Health is the authoritative condition; no navigation mutation needed for this boundary check.
            foreach (var entity in View(domain).Entities.Where(entity => entity.MaterializationProgress < 1 || entity.StasisRemaining > 0))
                units[entity.Id].GetType().GetField("Health").SetValue(units[entity.Id], 0d);
            AdvanceEffect(domain, 4, 4);
            Assert.IsFalse(View(domain).Sounds.Any(sound => sound.Kind == completed));
        }
    }
}

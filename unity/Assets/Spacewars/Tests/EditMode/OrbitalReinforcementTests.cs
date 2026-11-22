using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class OrbitalReinforcementTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly Type type = typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain");
        private object domain;
        private OfflineMatchConfiguration configuration;
        private long sequence;

        private object Call(string name, params object[] arguments) =>
            type.GetMethod(name, PrivateInstance).Invoke(domain, arguments);

        private PlayableSnapshot View() => (PlayableSnapshot)Call("PlayerSnapshot", 1L, RuntimeStatus.Running, false,
            new PlayableRuntimeMetrics(0, 0, 0, 0, 0), null, 7, PlayableOwner.Player);

        private PlayableCommandStatus Send(PlayableCommandKind kind, NavPoint target = default, int ability = OrbitalAbilities.Reinforcement) =>
            (PlayableCommandStatus)Call("Apply", new PlayableCommand(1, ++sequence, "player-1", kind, null, target,
                targetId: ability, origin: PlayableOrderOrigin.Human), null);

        [SetUp]
        public void Setup()
        {
            var map = new AiTestMap();
            var data = PlayableProfile.Default.CopyData();
            data.explorerCreditCost = 95;
            var profile = PlayableProfile.Create(data, map);
            configuration = map.Configuration(profile, 7, new[] {
                new OfflineParticipant("player-1", 1, 1, OfflineControl.Human),
                new OfflineParticipant("player-2", 2, 2, OfflineControl.Human)
            });
            domain = Activator.CreateInstance(type, PrivateInstance, null, new object[] { profile, 1L, configuration }, null);
            Call("AddCredits", PlayableOwner.Player, 10000d);
            Call("CreditOrbital", (PlayableOwner?)PlayableOwner.Player, PlayableOwner.Enemy,
                data.orbitalThreshold1 + data.orbitalThreshold2 + data.orbitalThreshold3);
            Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.ChooseOrbital, ability: 1));
            Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.ChooseOrbital, ability: 2));
            Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.ChooseOrbital));
        }

        [TearDown]
        public void Cleanup()
        {
            if (domain != null) Call("DestroyOwnedNavigation");
        }

        [Test]
        public void InvalidTargetRetainsPaymentAndCancelReturnsItOnce()
        {
            var initial = View();
            Assert.AreEqual(350, initial.Orbital.ReinforcementQuote.Credits);
            Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.BeginOrbitalReinforcement));
            Assert.AreEqual(initial.Credits - 350, View().Credits);
            Assert.AreEqual(initial.Population.Reserved + initial.Orbital.ReinforcementQuote.Population, View().Population.Reserved);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,
                Send(PlayableCommandKind.UseOrbital, new NavPoint(double.NaN, 0)));
            Assert.AreEqual(initial.Credits - 350, View().Credits);
            Assert.True(View().Orbital.ReinforcementQuote.Reserved);
            Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.CancelOrbitalReinforcement));
            Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.CancelOrbitalReinforcement));
            Assert.IsFalse(View().Sounds.Any(sound => sound.Kind == PlayableSoundKind.OrbitalReinforcementStarted));
            Assert.AreEqual(initial.Credits, View().Credits);
            Assert.AreEqual(initial.Population.Reserved, View().Population.Reserved);
            Assert.Zero(View().Orbital.CommonRemaining);
            Assert.AreEqual(PlayableEntityKind.Explorer, View().Orbital.ReinforcementQuote.Kind);
        }

        [Test]
        public void ReservationAndMaterializationRestoreWithoutCreatingAnotherWave()
        {
            Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.BeginOrbitalReinforcement));
            AssertCheckpoint(true);
            var before = View();
            var home = before.Buildings.First(b => b.Owner == PlayableOwner.Player && b.Kind == PlayableBuildingKind.Headquarters);
            bool applied = false;
            for (int x = -8; x <= 8 && !applied; x += 2)
                for (int z = -8; z <= 8 && !applied; z += 2)
                    applied = Send(PlayableCommandKind.UseOrbital, new NavPoint(home.Position.X + x, home.Position.Z + z)) ==
                        PlayableCommandStatus.Applied;
            Assert.True(applied, "a visible free zone near the friendly HQ accepts the whole wave");
            var after = View();
            Assert.AreEqual(1,after.Sounds.Count(sound => sound.Kind == PlayableSoundKind.OrbitalReinforcementStarted));
            var wave = after.Entities.Where(unit => !before.Entities.Any(old => old.Id == unit.Id)).ToArray();
            Assert.AreEqual(3, wave.Length);
            Assert.That(wave.All(unit => unit.Kind == PlayableEntityKind.Explorer && unit.MaterializationProgress == 0));
            Assert.AreEqual(before.Credits, after.Credits);
            Assert.AreEqual(PlayableEntityKind.Tank, after.Orbital.ReinforcementQuote.Kind);
            Assert.Greater(after.Orbital.CommonRemaining, 0);
            Call("Damage",wave[0].Id,7);
            var hit=View().Entities.Single(unit=>unit.Id==wave[0].Id);
            Assert.AreEqual(wave[0].ExactHealth.Value-7,hit.ExactHealth.Value);
            Assert.Zero(hit.MaterializationProgress);
            AssertCheckpoint(false);
        }

        private void AssertCheckpoint(bool reserved)
        {
            var bytes = (byte[])Call("CaptureWorldBytes", 7, configuration.SourceIdentity);
            var restored = OfflineParticipantAuthority.Restore(bytes, configuration);
            try
            {
                CollectionAssert.AreEqual(bytes, restored.CaptureBytes());
                var result = restored.View("player-1");
                Assert.AreEqual(reserved, result.Orbital.ReinforcementQuote.Reserved);
                Assert.AreEqual(View().Credits, result.Credits);
                Assert.AreEqual(View().Entities.Count(unit => unit.MaterializationProgress < 1),
                    result.Entities.Count(unit => unit.MaterializationProgress < 1));
            }
            finally
            {
                var other = typeof(OfflineParticipantAuthority).GetProperty("Domain", PrivateInstance).GetValue(restored);
                type.GetMethod("DestroyOwnedNavigation", PrivateInstance).Invoke(other, null);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class OrbitalStasisTests
    {
        [Test]
        public void SelectionUsesHorizontalDistanceAndStableFourTargetLimit()
        {
            var point = new NavPoint(0, 0);
            var data = PlayableProfile.Default.CopyData();
            var candidates = new[] {
                (9, new NavPoint(0, 0)), (8, new NavPoint(2, 0)), (5, new NavPoint(1, 0)),
                (3, new NavPoint(-1, 0)), (2, new NavPoint(0, -2)), (1, new NavPoint(2.01, 0))
            };
            CollectionAssert.AreEqual(new[] {9, 3, 5, 2}, OrbitalStasis.Select(point, data, candidates));
            data.orbitalStasisTargets = 5;
            Assert.Throws<ArgumentException>(() => PlayableProfile.Create(data));
        }

        [Test]
        public void GroundAndDirectHawkAimUseTheActualHullHeight()
        {
            var point = new NavPoint(0,0);
            var terms = PlayableProfile.Default.CopyData();
            var units = new (int Id, NavPoint Position, double? HullHeight)[] {
                (1,point,null),
                (2,new NavPoint(1,0),5),
                (3,point,8),
                (4,new NavPoint(1,0),8.5),
                (5,new NavPoint(2,0),9),
                (6,point,10.01)
            };
            CollectionAssert.AreEqual(new[]{1,2},OrbitalStasis.Select(point,terms,units,0));
            CollectionAssert.AreEqual(new[]{3,4},OrbitalStasis.Select(point,terms,units,8,airOnly:true));
        }

        [Test, Timeout(30000)]
        public void AttackOrderKeepsFrozenTargetAndAttackMoveAnswersActiveShooter()
        {
            var profile = PlayableProfile.Default;
            var domain = new PlayableDomain(profile, 1, false);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            object Call(string name, params object[] args) => typeof(PlayableDomain).GetMethod(name, flags).Invoke(domain, args);
            int Spawn(NavPoint point, PlayableOwner owner) => (int)Call("SpawnUnit", point, owner, PlayableEntityKind.Tank);
            var units = (System.Collections.IDictionary)typeof(PlayableDomain).GetField("units", flags).GetValue(domain);
            int UnitField(int id, string name) => (int)units[id].GetType().GetField(name, flags).GetValue(units[id]);
            int Target(int id) => UnitField(id, "Target");
            void Reload(int id, double seconds) => units[id].GetType().GetField("Reload", flags).SetValue(units[id], seconds);
            var authority = new PlayableAuthorityTick(domain, new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()), 7);
            CertifiedRouteWorker worker = null;
            long sequence = 0;
            PlayableCommandStatus Send(string owner, PlayableCommandKind kind, int[] ids, NavPoint point = default, int target = 0)
                => authority.Apply(new PlayableCommand(1, ++sequence, owner, kind, ids, point, targetId: target)).Status;
            void AdvanceUntil(Func<bool> check, int tickBudget)
            {
                var deadline = domain.Tick + tickBudget;
                var clock = Stopwatch.StartNew();
                while (!check() && domain.Tick < deadline && clock.Elapsed.TotalSeconds < 10)
                {
                    if (!authority.TryAdvance()) Thread.Sleep(1);
                    Assert.IsNull(worker.Publication.Failure);
                }
                Assert.True(check(), $"Ordinary route host did not reach the expected state by tick {domain.Tick}.");
            }
            try
            {
                foreach(int id in units.Keys.Cast<int>().ToArray()) { domain.Navigation.Remove(id); units.Remove(id); }
                var start = new NavPoint(-22, 26);
                var point = new NavPoint(-17, 26);
                int shooter = Spawn(start, PlayableOwner.Player);
                int frozen = Spawn(point, PlayableOwner.Enemy);
                int attacker = Spawn(new NavPoint(-15, 23), PlayableOwner.Enemy);
                Reload(attacker, 1000);
                worker = new CertifiedRouteWorker(authority.NavigationBinding, profile.NavigationRequestsPerFrame);
                var data = profile.CopyData();
                Call("CreditOrbital", (PlayableOwner?)PlayableOwner.Player, PlayableOwner.Enemy,
                    data.orbitalThreshold1 + data.orbitalThreshold2 + data.orbitalThreshold3);
                foreach (int ability in new[] { 1, 2, OrbitalAbilities.Stasis })
                    Assert.AreEqual(PlayableCommandStatus.Applied,
                        Send("player-1", PlayableCommandKind.ChooseOrbital, null, target: ability));

                Assert.AreEqual(PlayableCommandStatus.Applied,
                    Send("player-1", PlayableCommandKind.UseOrbital, null, point, OrbitalAbilities.Stasis));
                Assert.AreEqual(PlayableCommandStatus.Applied,
                    Send("player-1", PlayableCommandKind.Attack, new[] { shooter }, target: frozen));
                Assert.AreEqual(PlayableCommandStatus.Applied,
                    Send("enemy-1", PlayableCommandKind.Attack, new[] { attacker }, target: shooter));
                Reload(attacker, 0);
                AdvanceUntil(() => UnitField(attacker, "LastCombatShotTarget") == shooter, 90);
                Assert.AreEqual(frozen, Target(shooter));

                var order = Send("player-1", PlayableCommandKind.AttackMove, new[] { shooter }, new NavPoint(-25, 28));
                Assert.That(order, Is.EqualTo(PlayableCommandStatus.Applied).Or.EqualTo(PlayableCommandStatus.Accepted));
                AdvanceUntil(() => Target(shooter) == attacker, 90);
                Assert.Greater(authority.Latest.Entities.Single(unit => unit.Id == frozen).StasisRemaining, 0);
            }
            finally
            {
                worker?.Dispose();
                if (worker != null) Assert.True(SpinWait.SpinUntil(() => worker.IsStopped, 5000));
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
            }
        }

        [Test]
        public void OrdinaryCastAbsorbsDamageAndRestoresExactRemainingTime()
        {
            var map = new AiTestMap();
            var data = PlayableProfile.Default.CopyData();
            var profile = PlayableProfile.Create(data, map);
            var config = map.Configuration(profile, 7, new[] {
                new OfflineParticipant("player-1", 1, 1, OfflineControl.Human),
                new OfflineParticipant("player-2", 2, 2, OfflineControl.Human)
            });
            var domain = new PlayableDomain(profile, 1L, config);
            var type = typeof(PlayableDomain);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object Call(string name, params object[] args) => type.GetMethod(name, flags).Invoke(domain, args);
            PlayableSnapshot View() => (PlayableSnapshot)Call("PlayerSnapshot", 1L, RuntimeStatus.Running, false,
                new PlayableRuntimeMetrics(0, 0, 0, 0, 0), null, 7, PlayableOwner.Player);
            long sequence = 0;
            PlayableCommandStatus Send(PlayableCommandKind kind, int id, NavPoint point = default) =>
                (PlayableCommandStatus)Call("Apply", new PlayableCommand(1, ++sequence, "player-1", kind, null,
                    target: point, targetId: id), null);
            try
            {
                Call("CreditOrbital", (PlayableOwner?)PlayableOwner.Player, PlayableOwner.Enemy,
                    data.orbitalThreshold1 + data.orbitalThreshold2 + data.orbitalThreshold3);
                foreach (int ability in new[] { 1, 2, OrbitalAbilities.Stasis })
                    Assert.AreEqual(PlayableCommandStatus.Applied, Send(PlayableCommandKind.ChooseOrbital, ability));

                var target = View().Entities.First(unit => unit.Owner == PlayableOwner.Player);
                Assert.AreEqual(PlayableCommandStatus.InvalidTarget,
                    Send(PlayableCommandKind.UseOrbital, OrbitalAbilities.Stasis, new NavPoint(double.NaN, 0)));
                Assert.Zero(View().Orbital.CommonRemaining);
                Assert.AreEqual(PlayableCommandStatus.Applied,
                    Send(PlayableCommandKind.UseOrbital, OrbitalAbilities.Stasis, target.Position));
                var frozen = View().Entities.Single(unit => unit.Id == target.Id);
                Assert.AreEqual(7, frozen.StasisRemaining);
                Assert.AreEqual(10, View().Orbital.CommonRemaining);

                Call("Damage", frozen.Id, int.MaxValue);
                Assert.AreEqual(frozen.ExactHealth, View().Entities.Single(unit => unit.Id == frozen.Id).ExactHealth);
                var bytes = (byte[])Call("CaptureWorldBytes", 7, config.SourceIdentity);
                var restored = OfflineParticipantAuthority.Restore(bytes, config);
                var restoredDomain = (PlayableDomain)typeof(OfflineParticipantAuthority).GetProperty("Domain", flags).GetValue(restored);
                try
                {
                    Assert.AreEqual(frozen.StasisRemaining,
                        restored.View("player-1").Entities.Single(unit => unit.Id == frozen.Id).StasisRemaining);
                    CollectionAssert.AreEqual(bytes, restored.CaptureBytes());
                }
                finally { restoredDomain.DestroyOwnedNavigation(); }
            }
            finally { domain.DestroyOwnedNavigation(); }
        }
    }
}

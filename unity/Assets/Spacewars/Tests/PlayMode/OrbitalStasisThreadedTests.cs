using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed class OrbitalStasisThreadedTests
    {
        [Test, Timeout(60000)]
        public void AcceptedMoveWaitsForThawThenRunsThroughOrdinaryHost()
        {
            var profile = PlayableProfile.Default;
            var domain = new PlayableDomain(profile, 826, false);
            CertifiedRouteWorker worker = null;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var data = profile.CopyData();
                typeof(PlayableDomain).GetMethod("CreditOrbital", flags).Invoke(domain, new object[] {
                    (PlayableOwner?)PlayableOwner.Player, PlayableOwner.Enemy,
                    data.orbitalThreshold1 + data.orbitalThreshold2 + data.orbitalThreshold3
                });
                var start = new NavPoint(-22, 26);
                int id = (int)typeof(PlayableDomain).GetMethod("SpawnUnit", flags).Invoke(domain,
                    new object[] { start, PlayableOwner.Player, PlayableEntityKind.Tank });
                var authority = new PlayableAuthorityTick(domain, new AiAuthorityScheduler(Array.Empty<PlayableAiOwnerLoop>()), 7);
                worker = new CertifiedRouteWorker(authority.NavigationBinding, profile.NavigationRequestsPerFrame);
                long sequence = 0;
                foreach (int ability in new[] { 1, 2, OrbitalAbilities.Stasis })
                    Assert.AreEqual(PlayableCommandStatus.Applied, authority.Apply(new PlayableCommand(826, ++sequence,
                        "player-1", PlayableCommandKind.ChooseOrbital, null, targetId: ability)).Status);
                var cast = authority.Apply(new PlayableCommand(826, ++sequence, "player-1",
                    PlayableCommandKind.UseOrbital, null, start, targetId: OrbitalAbilities.Stasis));
                Assert.AreEqual(PlayableCommandStatus.Applied, cast.Status, cast.Message);
                var goal = new NavPoint(-13, 26);
                var move = authority.Apply(new PlayableCommand(826, ++sequence, "player-1", PlayableCommandKind.Move,
                    new[] { id }, goal));
                Assert.That(move.Status, Is.EqualTo(PlayableCommandStatus.Applied).Or.EqualTo(PlayableCommandStatus.Accepted), move.Message);

                var clock = Stopwatch.StartNew();
                int frozenTicks = 0;
                bool resumed = false;
                while (domain.Tick < 540 && clock.Elapsed.TotalSeconds < 45)
                {
                    if (!authority.TryAdvance()) { Thread.Sleep(1); continue; }
                    Assert.IsNull(worker.Publication.Failure);
                    var unit = authority.Latest.Entities.Single(entity => entity.Id == id);
                    if (unit.StasisRemaining > 0)
                    {
                        frozenTicks++;
                        Assert.AreEqual(start, unit.Position);
                        Assert.False(unit.Moving);
                    }
                    else if (Math.Abs(unit.Position.X - start.X) >= 1) { resumed = true; break; }
                }
                Assert.That(frozenTicks, Is.EqualTo(209));
                Assert.True(resumed, "The move issued during stasis resumes through the normal route worker.");
            }
            finally
            {
                worker?.Dispose();
                if (worker != null) Assert.True(SpinWait.SpinUntil(() => worker.IsStopped, 10000));
                domain.Navigation.ReleaseCommittedHostRoot();
                domain.DestroyOwnedNavigation();
                Assert.Zero(domain.Navigation.PhysicalHostBudget.PhysicalBytes);
                Assert.Zero(domain.Navigation.PhysicalHostBudget.HeldBytes);
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class OrdinaryFirstExplorerMoveTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [Test, Timeout(200000)]
        public void FirstExplorerReachesFarNeutralExpansionThroughOrdinaryThreadedHost()
        {
            var output = Environment.GetEnvironmentVariable("SPACEWARS_EXPLORER_EVIDENCE");
            Assert.IsNotEmpty(output, "Set an owned evidence directory.");
            Directory.CreateDirectory(output);
            using var log = new StreamWriter(Path.Combine(output, "trace.csv")) { AutoFlush = true };
            log.WriteLine("seconds,tick,x,z,navigation,epoch,graph,connectors,pending,ready,command_ms");
            var profile = PlayableProfile.ThreeCrossingsDefault;
            var setup = new NativeLobbyConfiguration { HasExplicitSeed = true, ExplicitSeed = 1024700465 };
            setup.InitializeParticipants();
            Assert.AreEqual(2, setup.Participants.Count);
            Assert.True(setup.Participants[0].Human); Assert.False(setup.Participants[1].Human);
            var config = NativeLobbyMatch.Create(profile, setup);
            var runtime = new PlayableRuntime(config, 71, startPaused: true);
            var host = new UnityHostRouteService();
            var domain = typeof(PlayableRuntime).GetField("domain", Hidden).GetValue(runtime);
            var nav = (NavigationSession)domain.GetType().GetProperty("Navigation", Hidden).GetValue(domain);
            var startup = Stopwatch.StartNew();
            void Service()
            {
                host.Service(runtime, profile.NavigationRequestsPerFrame);
                Assert.IsNull(runtime.Latest.Failure);
                Assert.IsNull(host.CertifiedPublication?.Failure);
            }
            try
            {
                while (!(runtime.IsPrepared && runtime.Latest.Paused) && startup.Elapsed.TotalSeconds < 60)
                { Service(); Thread.Sleep(1); }
                Assert.True(runtime.IsPrepared && runtime.Latest.Paused, "Ordinary startup must finish.");
                Assert.True(nav.AccruedCreditsEnabled, "Fresh ordinary runtime must select accrued v3 before host birth.");
                string owner = config.Roster[0].Id;
                var initial = runtime.ParticipantView(owner);
                var actor = initial.Entities.Single(e => e.Owner == initial.Owner && e.Kind == PlayableEntityKind.Explorer);
                Assert.AreEqual(3, actor.Id); Assert.AreEqual(new NavPoint(-36, -46), actor.Position);
                var goal = new NavPoint(26.42, 48);
                Assert.True(initial.PublicScoutObjectives.Any(o => o.Reachable && o.Role != PlayablePublicScoutObjectiveRole.PossibleEnemyStart && o.Approach.Equals(goal)), "Keep the exact free neutral approach.");
                runtime.RequestPause(false);
                var resume = Stopwatch.StartNew();
                while (runtime.Latest.Paused && resume.Elapsed.TotalSeconds < 5) { Service(); Thread.Sleep(1); }
                Assert.False(runtime.Latest.Paused);
                var clock = Stopwatch.StartNew();
                Assert.True(runtime.TrySubmit(new PlayableCommand(71, 1, owner, PlayableCommandKind.Move, new[] { actor.Id }, goal)).Accepted);
                bool applied = false, moved = false, arrived = false;
                long lastTick = -1, firstMotionTick = -1, arrivalTick = -1;
                double firstMotionSeconds = -1, arrivalSeconds = -1;
                while (clock.Elapsed.TotalSeconds < 120)
                {
                    Service();
                    applied |= runtime.DrainReceipts().Any(r => r.Sequence == 1 && r.Status == PlayableCommandStatus.Applied);
                    var view = runtime.ParticipantView(owner);
                    var current = view.Entities.SingleOrDefault(e => e.Id == actor.Id);
                    if (current != null)
                    {
                        if (!moved && !current.Position.Equals(actor.Position)) { moved = true; firstMotionTick = view.Tick; firstMotionSeconds = clock.Elapsed.TotalSeconds; }
                        if (!arrived && current.NavigationOutcome == NavigationOutcome.Arrived) { arrived = true; arrivalTick = view.Tick; arrivalSeconds = clock.Elapsed.TotalSeconds; }
                        if (view.Tick >= lastTick + 3)
                        {
                            lastTick = view.Tick; var counters = host.CertifiedCounters;
                            log.WriteLine(string.Join(",", clock.Elapsed.TotalSeconds.ToString("R", CultureInfo.InvariantCulture), view.Tick, current.Position.X.ToString("R", CultureInfo.InvariantCulture), current.Position.Z.ToString("R", CultureInfo.InvariantCulture), current.NavigationOutcome, host.SolverEpoch, counters.PhysicalGraphWork, counters.ConnectorWork, counters.PendingSubscriptions, counters.ReadyResults, view.Metrics.CommandLatencyMilliseconds.ToString("R", CultureInfo.InvariantCulture)));
                        }
                    }
                    Thread.Sleep(1);
                }
                var result = $"applied={applied} moved={moved} arrived={arrived} firstMotionTick={firstMotionTick} firstMotionSeconds={firstMotionSeconds:R} arrivalTick={arrivalTick} arrivalSeconds={arrivalSeconds:R} finalTick={runtime.Latest.Tick} startupSeconds={startup.Elapsed.TotalSeconds - clock.Elapsed.TotalSeconds:R}";
                File.WriteAllText(Path.Combine(output, "result.txt"), result); TestContext.WriteLine(result);
                Assert.True(applied, "Ordinary Move receipt must be Applied.");
                Assert.True(moved, "Original first Explorer must physically move within the full120-second scenario.");
                Assert.True(arrived, "Original first Explorer must arrive within the full120-second scenario.");
            }
            finally
            {
                var worker = (CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker", Hidden).GetValue(host);
                runtime.Dispose(); host.Dispose();
                Assert.True(SpinWait.SpinUntil(() => runtime.IsStopped && (worker == null || worker.IsStopped), 5000));
                nav.ReleaseCommittedHostRoot();
                domain.GetType().GetMethod("DestroyOwnedNavigation", Hidden).Invoke(domain, null);
                TestContext.WriteLine($"cleanup physical={nav.PhysicalHostBudget.PhysicalBytes} held={nav.PhysicalHostBudget.HeldBytes} scopes={nav.PhysicalHostBudget.ActiveScopes}");
                Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes); Assert.Zero(nav.PhysicalHostBudget.HeldBytes); Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
    }
}

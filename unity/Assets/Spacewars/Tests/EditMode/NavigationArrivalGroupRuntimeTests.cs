using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationArrivalGroupRuntimeTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test, Timeout(60000)]
        public void RejectedFollowLeavesOriginalMoveAliveThroughOrdinaryThreadedHost()
        {
            var profile = PlayableProfile.ThreeCrossingsDefault;
            var setup = new NativeLobbyConfiguration { HasExplicitSeed = true, ExplicitSeed = 19092026 };
            setup.InitializeParticipants();
            foreach (var participant in setup.Participants) participant.Human = true;
            var seed = NativeLobbyMatch.Create(profile, setup);
            var costs = new double[seed.Starts.Count, seed.Starts.Count];
            for (int i = 0; i < seed.Starts.Count; i++)
                for (int j = 0; j < seed.Starts.Count; j++) costs[i, j] = seed.RouteCost(i, j);
            int logical = seed.Roster[0].LogicalPlayer;
            var config = new OfflineMatchConfiguration(profile, seed.SourceIdentity, seed.MapIdentity,
                seed.RouteProvenance, seed.Seed, seed.Roster.ToArray(), seed.Starts.ToArray(), seed.Sites.ToArray(),
                seed.Obstacles.ToArray(), costs, seed.Spectators.ToArray(), new[] {
                    new OfflineScenarioUnit(logical, PlayableEntityKind.Tank, new NavPoint(-18, -1.8), 0),
                    new OfflineScenarioUnit(logical, PlayableEntityKind.Tank, new NavPoint(-18, 1.8), 0),
                    new OfflineScenarioUnit(logical, PlayableEntityKind.Tank, new NavPoint(-24, 0), 0)
                }, profile.AuthoredMap.Id, seed.ScenarioBuildings.ToArray());
            var runtime = new PlayableRuntime(config, 71, startPaused: true);
            var host = new UnityHostRouteService();
            var domain = typeof(PlayableRuntime).GetField("domain", Hidden).GetValue(runtime);
            var nav = (NavigationSession)domain.GetType().GetProperty("Navigation", Hidden).GetValue(domain);
            var clock = Stopwatch.StartNew();
            bool Until(Func<bool> condition, int milliseconds)
            {
                long start = clock.ElapsedMilliseconds;
                while (clock.ElapsedMilliseconds - start < milliseconds)
                {
                    host.Service(runtime, profile.NavigationRequestsPerFrame);
                    Assert.IsNull(runtime.Latest.Failure);
                    if (condition()) return true;
                    Thread.Sleep(1);
                }
                return false;
            }
            try
            {
                Assert.True(Until(() => runtime.IsPrepared && runtime.Latest.Paused, 5000));
                var initial = runtime.Latest.Entities.Where(e => e.Kind == PlayableEntityKind.Tank && e.Position.X == -18).ToArray();
                Assert.AreEqual(2, initial.Length);
                var ids = initial.Select(e => e.Id).ToArray();
                int leader = runtime.Latest.Entities.Single(e => e.Kind == PlayableEntityKind.Tank && e.Position.Equals(new NavPoint(-24, 0))).Id;
                string owner = config.Roster[0].Id;
                runtime.RequestPause(false);
                Assert.True(Until(() => !runtime.Latest.Paused, 5000));
                Assert.True(runtime.TrySubmit(new PlayableCommand(71, 1, owner, PlayableCommandKind.Move, ids, new NavPoint(-16, 0))).Accepted);
                bool moved = false;
                Assert.True(Until(() => { moved |= runtime.DrainReceipts().Any(r => r.Sequence == 1 && r.Status == PlayableCommandStatus.Applied); return moved && nav.AllocationCount == 2; }, 5000));
                var requests = ids.Select(id => nav.PendingRequestFor(id)).ToArray();
                int denials = 0;
                Volatile.Write(ref nav.PhysicalHostBudget.AllocationAdmission, _ => {
                    if (!new StackTrace().GetFrames().Any(f => f.GetMethod().Name == "TryOwnedStopGroup")) return true;
                    Interlocked.Increment(ref denials); return false;
                });
                bool rejected = false;
                try
                {
                    Assert.True(runtime.TrySubmit(new PlayableCommand(71, 2, owner, PlayableCommandKind.Follow, ids, targetId: leader)).Accepted);
                    Assert.True(Until(() => { rejected |= runtime.DrainReceipts().Any(r => r.Sequence == 2 && r.Status == PlayableCommandStatus.Overflow); return rejected; }, 5000));
                }
                finally { Volatile.Write(ref nav.PhysicalHostBudget.AllocationAdmission, null); }
                Assert.Greater(denials, 0);
                foreach (var request in requests) Assert.AreEqual(request.Request, nav.PendingRequestFor(request.Entity).Request);
                Assert.True(Until(() => runtime.Latest.Entities.Any(e => ids.Contains(e.Id) && !e.Position.Equals(initial.Single(x => x.Id == e.Id).Position)), 30000), "The original Move must make actual first motion.");
                long firstMotion = runtime.Latest.Tick;
                Assert.True(Until(() => runtime.Latest.Entities.Where(e => ids.Contains(e.Id)).All(e => e.NavigationOutcome == NavigationOutcome.Arrived), 10000));
                Assert.IsNull(host.CertifiedPublication.Failure);
                TestContext.WriteLine("ARRIVAL_CONTROL_THREADED firstMotion=" + firstMotion + " arrived=" + runtime.Latest.Tick + " originalRequests=" + string.Join(",", requests.Select(r => r.Request)));
            }
            finally
            {
                Volatile.Write(ref nav.PhysicalHostBudget.AllocationAdmission, null);
                var worker = (CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker", Hidden).GetValue(host);
                runtime.Dispose(); host.Dispose();
                Assert.True(SpinWait.SpinUntil(() => runtime.IsStopped && (worker == null || worker.IsStopped), 5000));
                nav.ReleaseCommittedHostRoot();
                domain.GetType().GetMethod("DestroyOwnedNavigation", Hidden).Invoke(domain, null);
                Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);
                Assert.Zero(nav.PhysicalHostBudget.HeldBytes);
                Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
    }
}

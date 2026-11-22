using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed partial class GroupOrderReplacementThreadedTests
    {
        [Test, Explicit("Matched exclusive route measurement"), Timeout(600000)]
        public void MeasureIndependentRoutes()
        {
            string output = Environment.GetEnvironmentVariable("SPACEWARS_ROUTE_MEASUREMENTS");
            Assert.IsNotEmpty(output);
            var property = typeof(CertifiedRouteKernel).GetProperty("ReuseCompatibleRoutes", BindingFlags.Instance | BindingFlags.NonPublic);
            bool mass = Environment.GetEnvironmentVariable("SPACEWARS_ROUTE_MEASUREMENT_SCALE") == "mass";
            for (int repeat = 0; repeat < 3; repeat++)
                foreach (string scenario in new[] { "common", "scattered", "mixed" })
                    foreach (bool reuse in property == null ? new[] { true } : new[] { repeat % 2 == 0, repeat % 2 != 0 })
                        MeasureRouteScenario(output, scenario, repeat, property, reuse, mass);
        }

        [Test, Explicit("Motion diagnostics for the independent common-goal batch"), Timeout(60000)]
        public void TraceIndependentCommonGoalProgress()
        {
            string output = Environment.GetEnvironmentVariable("SPACEWARS_ROUTE_MEASUREMENTS");
            Assert.IsNotEmpty(output);
            var property = typeof(CertifiedRouteKernel).GetProperty("ReuseCompatibleRoutes", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(property);
            MeasureRouteScenario(output, "common", 0, property, reuse: false, mass: true, traceMotion: true);
        }

        private static void MeasureRouteScenario(string output, string scenario, int repeat, PropertyInfo option,
            bool reuse, bool mass, bool traceMotion = false)
        {
            var starts = new[]
            {
                new NavPoint(-19.03138653662337, -8.705516311616666),
                new NavPoint(-15.011683846386127, -9.338825755162954),
                new NavPoint(-16.15726080075214, -5.991997231249877),
                new NavPoint(-17.684238116797122, -6.899709803573104),
                new NavPoint(-22.986105413387065, -7.676924963525011)
            };
            if (mass) starts = MassRouteStarts();
            var roster = starts.Select((p, i) => new OfflineScenarioUnit(4,
                scenario == "mixed" && i % 2 != 0 ? PlayableEntityKind.Tank : PlayableEntityKind.Explorer, p, 0)).ToArray();
            var config = Configuration(roster, true);
            using var runtime = new PlayableRuntime(config, Generation);
            var kernel = new CertifiedRouteKernel();
            option?.SetValue(kernel, reuse);
            using var worker = new CertifiedRouteWorker(runtime.NavigationBinding, config.Profile.NavigationRequestsPerFrame,
                kernel, () => runtime.IsStopRequested, runtime.ReportRouteFailure);
            try
            {
                Assert.True(SpinWait.SpinUntil(() => runtime.IsPrepared && runtime.Latest.Tick > 0 && worker.Publication.RecastFoundationBuilds > 0, 20000));
                var view = runtime.ParticipantView(Human);
                var ids = starts.Select(position => view.Entities.Single(e => e.Position.Equals(position)).Id).ToArray();
                int count = ids.Length;
                var firstMotion = new long?[count];
                var arrivals = new long?[count];
                var firstRoute = new long?[count];
                var pathLengths = new double[count];
                var stops = new int[count];
                var previous = starts.ToArray();
                var tickMs = new List<double>();
                int requests = 0;
                var domain = (PlayableDomain)typeof(PlayableRuntime).GetField("domain", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime);
                var nav = domain.Navigation;
                var motionTrace = new List<object>();
                var acceptedRoutes = new List<object>();
                if (traceMotion)
                {
                    const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
                    var routeIndex = typeof(NavUnit).GetField("routeIndex", hidden);
                    var localIndex = typeof(NavUnit).GetField("localIndex", hidden);
                    var noProgress = typeof(NavUnit).GetField("noProgressTicks", hidden);
                    var blockedSeconds = typeof(NavUnit).GetField("blockedSeconds", hidden);
                    long firstTick = view.Tick;
                    domain.CombatStepForQa = () =>
                    {
                        if (domain.Tick <= firstTick || domain.Tick > firstTick + 300) return;
                        motionTrace.Add(new { domain.Tick, nav.Crowd.RepairCount, nav.Crowd.RepairFailed,
                            Units = nav.Crowd.Units.Where(unit => Array.IndexOf(ids, unit.Id) >= 0).Select(unit => new
                            {
                                unit.Id, unit.Position, unit.Location, unit.Goal, unit.Heading, unit.Moving, unit.Radius, unit.Speed,
                                RouteIndex = (int)routeIndex.GetValue(unit), RouteLength = unit.Route.Count,
                                LocalIndex = (int)localIndex.GetValue(unit),
                                NoProgress = (int)noProgress.GetValue(unit), BlockedSeconds = (double)blockedSeconds.GetValue(unit)
                            }).ToArray() });
                    };
                }
                nav.RouteRequested += r => { if (Array.IndexOf(ids, r.Entity) >= 0) Interlocked.Increment(ref requests); };
                long begin = view.Tick;
                nav.RouteActivated += r =>
                {
                    int i = Array.IndexOf(ids, r.Entity);
                    if (i < 0) return;
                    firstRoute[i] ??= domain.Tick - begin;
                    var from = r.Start;
                    if (!nav.Crowd.TryGet(r.Entity, out var actor)) return;
                    if (traceMotion) acceptedRoutes.Add(new { domain.Tick, actor.Id, actor.Location, actor.GoalLocation, Points = actor.Route.ToArray() });
                    foreach (var point in actor.Route)
                    {
                        double dx = point.X - from.X, dz = point.Z - from.Z;
                        pathLengths[i] += Math.Sqrt(dx * dx + dz * dz);
                        from = point;
                    }
                };
                var goal = new NavPoint(-6.3472514152526855, 1.4160501956939697);
                var scattered = mass
                    ? starts.Select((start, i) => new NavPoint(start.X + 3.5, start.Z + (i % 2 == 0 ? 2 : -2))).ToArray()
                    : new[] { goal, new NavPoint(-14, -13), new NavPoint(-12,-3), new NavPoint(-20,-3), new NavPoint(-23,-13) };
                var before = worker.Publication;
                long stamp = Stopwatch.GetTimestamp();
                long peakManaged = GC.GetTotalMemory(false);
                if (scenario == "scattered")
                    for (int i = 0; i < count; i++) Assert.True(runtime.TrySubmit(new PlayableCommand(Generation, i+1, Human, PlayableCommandKind.Move, new[] { ids[i] }, scattered[i])).Accepted);
                else
                    Assert.True(runtime.TrySubmit(new PlayableCommand(Generation, 1, Human, PlayableCommandKind.Move, ids, goal)).Accepted);
                long last = begin;
                var clock = Stopwatch.StartNew();
                int horizon = mass ? 300 : 220;
                while (last < begin + horizon && clock.Elapsed.TotalSeconds < (mass ? 18 : 15))
                {
                    Assert.IsNull(worker.Publication.Failure);
                    Assert.IsNull(runtime.Latest.Failure);
                    view = runtime.ParticipantView(Human);
                    if (view.Tick <= last) { Thread.Sleep(1); continue; }
                    last = view.Tick;
                    peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
                    tickMs.Add(view.Metrics.TickCpuMilliseconds);
                    for (int i = 0; i < count; i++)
                    {
                        var unit = view.Entities.Single(e => e.Id == ids[i]);
                        if (!unit.Position.Equals(starts[i])) firstMotion[i] ??= last - begin;
                        if (unit.Completion?.Order.Sequence == (scenario == "scattered" ? i + 1 : 1))
                            arrivals[i] ??= unit.Completion.CompletionTick - begin;
                        if (firstMotion[i].HasValue && !arrivals[i].HasValue && unit.Position.Equals(previous[i])) stops[i]++;
                        previous[i] = unit.Position;
                    }
                    if (arrivals.All(t => t.HasValue)) break;
                }
                var after = worker.Publication;
                var lastView = view;
                long end = Stopwatch.GetTimestamp();
                runtime.RequestStop();
                Assert.True(SpinWait.SpinUntil(() => runtime.IsStopped && worker.IsStopped, 5000));
                var row = new { scenario, repeat, reuse, nav.QualifiedEntryPreparations, baseline = option == null, mass, horizon, seed=Seed, config.Profile.ProfileId, config.Profile.Revision,
                    starts, goals=scenario == "scattered" ? scattered : new[] { goal },
                    kinds=roster.Select(u => u.Kind.ToString()), ids, begin, last, stamp, end, firstMotion, arrivals, firstRoute, pathLengths,
                    final = ids.Select(id => lastView.Entities.Single(u => u.Id == id)).Select(u => new
                    { u.Id, u.Position, u.Location, Outcome=u.NavigationOutcome.ToString(), u.Moving, Pending=u.Queue?.Pending }),
                    stationarySamples=stops, tickMs, requests, peakManaged, after.PeakRetainedBytes,
                    peakNavigationLedgerBytes = nav.PhysicalHostBudget.PeakRetainedBytes,
                    localRepairs = nav.Crowd.RepairCount, localRepairFailures = nav.Crowd.RepairFailed,
                    maxNoProgressTicks = nav.Crowd.MaxNoProgressTicks,
                    longQueries = after.RecastLongQueries - before.RecastLongQueries,
                    workerMs = after.PumpElapsedMilliseconds - before.PumpElapsedMilliseconds };
                File.AppendAllText(output, JsonConvert.SerializeObject(row) + Environment.NewLine);
                if (traceMotion) File.WriteAllText(Path.ChangeExtension(output, ".routes.json"), JsonConvert.SerializeObject(acceptedRoutes));
                if (traceMotion) File.WriteAllLines(Path.ChangeExtension(output, ".motion.jsonl"),
                    motionTrace.Select(record => JsonConvert.SerializeObject(record)));
            }
            finally
            {
                runtime.RequestStop();
                Assert.True(SpinWait.SpinUntil(() => runtime.IsStopped && worker.IsStopped, 5000));
            }
        }
        private static NavPoint[] MassRouteStarts()
        {
            var basis = Configuration(Array.Empty<OfflineScenarioUnit>(), true);
            var map = basis.Profile.AuthoredMap;
            var obstacles = new NavGeometry(basis.Profile.ArenaHalfExtent, basis.Obstacles.ToArray(), 1);
            var starts = new List<NavPoint>();
            const double clearance = 1.5;
            for (double x = -27; x <= -10; x += 3)
                for (double z = -15; z <= -3; z += 3)
                {
                    var point = new NavPoint(x, z);
                    if (!map.TryLocate(point, clearance, out _) || !obstacles.IsFree(point, clearance)) continue;
                    if (basis.Starts.Any(start =>
                        (start.Position.X - x) * (start.Position.X - x) + (start.Position.Z - z) * (start.Position.Z - z) < 25 ||
                        (start.ExplorerAnchor.X - x) * (start.ExplorerAnchor.X - x) +
                        (start.ExplorerAnchor.Z - z) * (start.ExplorerAnchor.Z - z) < 9)) continue;
                    starts.Add(point);
                }
            Assert.GreaterOrEqual(starts.Count, 16, "sixteen non-overlapping authored starts");
            return starts.Take(16).ToArray();
        }
    }
}

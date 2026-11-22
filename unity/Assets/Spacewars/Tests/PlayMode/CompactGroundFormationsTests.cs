using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CompactGroundFormationsTests
    {
        private static double Distance(NavPoint a, NavPoint b)
            => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2));

        [UnityTest, Timeout(135000)]
        public IEnumerator MixedOrderUsesSeparateCompactFormations()
        {
            const int seed = 714;
            const int horizon = 1800;
            const double wallSeconds = 110;
            const long generation = 504;
            var output = Environment.GetEnvironmentVariable("COMPACT_FORMATION_OUTPUT");
            Assert.IsNotEmpty(output);
            Directory.CreateDirectory(output);
            Assert.True(NativeSilentStartup.IsSilent);
            var store = new NativeBalanceStore(PlayableProfile.Default, authoring: false, startOnRelease: true);
            Assert.True(store.HasActive, store.Problem);
            var profile = PlayableProfile.Create(store.Active.CopyData(), new AiTestMap(), store.Active.DisplayName);
            var map = (AiTestMap)profile.AuthoredMap;
            var roster = new[]
            {
                new OfflineParticipant("player-1", 1, 1, OfflineControl.Human),
                new OfflineParticipant("player-2", 2, 2, OfflineControl.Human)
            };
            var basis = map.Configuration(profile, seed, roster);
            // +X travel. Shkval starts in front and Skat starts ahead of the Hawk.
            var start = new[]
            {
                new OfflineScenarioUnit(1, PlayableEntityKind.Explorer, new NavPoint(-41, 10), 0),
                new OfflineScenarioUnit(1, PlayableEntityKind.Tank, new NavPoint(-38, 14), 0),
                new OfflineScenarioUnit(1, PlayableEntityKind.Shkval, new NavPoint(-35, 12), 0),
                new OfflineScenarioUnit(1, PlayableEntityKind.Hawk, new NavPoint(-41, 18), 0),
                new OfflineScenarioUnit(1, PlayableEntityKind.Skat, new NavPoint(-36, 18), 0),
                new OfflineScenarioUnit(1, PlayableEntityKind.Tank, new NavPoint(-2, 10.25)),
                new OfflineScenarioUnit(1, PlayableEntityKind.Tank, new NavPoint(28, 12))
            };
            var target = start.Last().Position;
            var routesCost = new double[basis.Starts.Count, basis.Starts.Count];
            for (int i = 0; i < basis.Starts.Count; i++)
                for (int j = 0; j < basis.Starts.Count; j++)
                    routesCost[i, j] = basis.RouteCost(i, j);
            var config = new OfflineMatchConfiguration(profile, basis.SourceIdentity, basis.MapIdentity, basis.RouteProvenance,
                seed, roster, basis.Starts.ToArray(), basis.Sites.ToArray(), basis.Obstacles.ToArray(), routesCost, scenario: start, terrain: map.Id);
            TestMatchLogSession.StartDefault(Path.Combine(output, "TestLogs"));
            using var match = new PlayableRuntime(config, generation, startPaused: true);
            using var routes = new UnityHostRouteService();
            var samples = new List<PlayableSnapshot>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            PlayableSnapshot View() => match.ParticipantView("player-1");
            void Service()
            {
                routes.Service(match, profile.NavigationRequestsPerFrame);
                Assert.IsNull(match.Latest?.Failure);
                Assert.IsNull(routes.LastProjectionFailure);
                Assert.Less(clock.Elapsed.TotalSeconds, wallSeconds);
            }
            bool Arrived(PlayableEntitySnapshot unit)
                => unit.NavigationOutcome == NavigationOutcome.Arrived;
            int[] ids = null;
            var checks = new Dictionary<string, bool>();
            string captureStatus = "NotRun";
            try
            {
                while (!match.IsPrepared || match.Latest?.Paused != true) { Service(); yield return null; }
                var initial = View();
                ids = start.Take(5).Select(s => initial.Entities.Single(e => e.Kind == s.Kind && e.Position.Equals(s.Position)).Id).ToArray();
                var hold = start.Skip(5).Select(s => initial.Entities.Single(e => e.Kind == s.Kind && e.Position.Equals(s.Position)).Id).ToArray();
                var capture = match.RequestCaptureBytes();
                while (!capture.IsCompleted) { Service(); yield return null; }
                File.WriteAllBytes(Path.Combine(output, "initial-current.bin"), capture.GetAwaiter().GetResult());
                captureStatus = "Captured current runtime format before orders";
                match.RequestPause(false);
                while (match.Latest.Paused) { Service(); yield return null; }
                Assert.True(match.TrySubmit(new PlayableCommand(generation, 1, "player-1", PlayableCommandKind.Hold, hold).AsHuman()).Accepted);
                while (!View().Entities.Where(e => hold.Contains(e.Id)).All(e => e.Held)) { Service(); yield return null; }
                long issued = View().Tick;
                Assert.True(match.TrySubmit(new PlayableCommand(generation, 2, "player-1", PlayableCommandKind.Move, ids, target).AsHuman()).Accepted);
                while (View().Tick - issued <= horizon)
                {
                    Service();
                    var view = View();
                    if (samples.Count == 0 || view.Tick > samples[samples.Count - 1].Tick) samples.Add(view);
                    var selected = ids.Select(id => view.Entities.Single(e => e.Id == id)).ToArray();
                    if (selected.All(Arrived)) break;
                    yield return null;
                }

                PlayableEntitySnapshot[] Selected(PlayableSnapshot frame) => ids.Select(id => frame.Entities.Single(e => e.Id == id)).ToArray();
                var last = Selected(samples.Last());
                var open = samples.Select(Selected).Where(u => u[0].Position.X > -25 && u[0].Position.X < -12).ToArray();
                var recovered = samples.Select(Selected).Where(u => u[2].Position.X > 4 && u[0].Position.X < 19).ToArray();
                double groupSpan = 6 * profile.TankCollisionRadius + 3 * profile.Navigation.ArrivalTolerance;
                bool Ordered(PlayableEntitySnapshot[] u) => u[0].Position.X > u[1].Position.X && u[1].Position.X > u[2].Position.X;
                bool Compact(PlayableEntitySnapshot[] u) => Distance(u[0].Position, u[2].Position) <= groupSpan;
                checks["threaded host"] = routes.CertifiedPublication?.Pumps > 0;
                checks["all aircraft and ground arrive"] = last.All(Arrived);
                checks["role order and compact approach"] = open.Count(u => Ordered(u) && Compact(u)) >= 8;
                var byObstacle = samples.Select(Selected).Where(u => Math.Abs(u[0].Position.X - start[5].Position.X) < 1).ToArray();
                double obstacleClearance = profile.ExplorerCollisionRadius + profile.TankCollisionRadius;
                checks["local diversion around HOLD"] = byObstacle.Any(u =>
                    Math.Abs(u[0].Position.Z - start[5].Position.Z) > obstacleClearance * .8) &&
                    open.Any(u => Math.Abs(u[0].Position.Z - start[5].Position.Z) < obstacleClearance * .5);
                checks["close ranks after HOLD obstacle"] = recovered.Count(u => Ordered(u) && Compact(u)) >= 8;
                checks["compact terminal footprint"] = Ordered(last) && Compact(last);
                checks["air order above ground"] = last[3].Position.X > last[4].Position.X &&
                    last.Skip(3).All(u => u.Altitude > 0) &&
                    last.Take(3).Any(g => last.Skip(3).Any(a => Distance(g.Position, a.Position) < profile.Navigation.ArrivalSlotSpacing));
                checks["air role order on the march"] = samples.Select(Selected).Count(u =>
                    u[3].Position.X > u[4].Position.X && u[3].Position.X > -20 && u[3].Position.X < 15 &&
                    u[3].Moving && u[4].Moving) >= 30;
                // Issue an ordinary air-only order directly above the held tank.
                // A ground reservation or combined occupancy would displace it.
                Assert.True(match.TrySubmit(new PlayableCommand(generation, 3, "player-1", PlayableCommandKind.Move,
                    new[] { ids[3] }, start[5].Position).AsHuman()).Accepted);
                while (View().Tick - issued <= horizon)
                {
                    Service();
                    var hawk = View().Entities.Single(u => u.Id == ids[3]);
                    var parked = View().Entities.Single(u => u.Id == hold[0]);
                    if (Arrived(hawk) && Distance(hawk.Position, parked.Position) <= profile.Navigation.ArrivalTolerance)
                    {
                        checks["ground and air share horizontal space"] = parked.Held && hawk.Altitude > 0;
                        break;
                    }
                    yield return null;
                }
                if (!checks.ContainsKey("ground and air share horizontal space"))
                    checks["ground and air share horizontal space"] = false;
                var pace = samples.Zip(samples.Skip(1), (a, b) => (From: a, To: b))
                    .Where(t => t.To.Tick - t.From.Tick <= 3)
                    .Where(t => { double x = Selected(t.From)[0].Position.X; return x > -25 && x < -12; })
                    .Select(t => Distance(Selected(t.From)[0].Position, Selected(t.To)[0].Position) * 30 / (t.To.Tick - t.From.Tick)).ToArray();
                double slowestSpeed = Math.Min(profile.ShkvalSpeed, profile.Navigation.TankSpeed);
                checks["fast explorer assumes steady group pace on clear ground"] = pace.Length >= 8 &&
                    pace.Count(v => v > .65 * slowestSpeed && v < 1.15 * slowestSpeed) >= pace.Length * .8;
                for (int i = 0; i < 3; i++)
                    for (int j = i + 1; j < 3; j++)
                        checks[$"no ground overlap {i}-{j}"] = samples.Select(Selected).All(u =>
                            Distance(u[i].Position, u[j].Position) + 1e-6 >=
                            PlayableUnitRules.Radius(profile, u[i].Kind) + PlayableUnitRules.Radius(profile, u[j].Kind));
                TestContext.WriteLine(string.Join(", ", checks.Select(c => c.Key + "=" + c.Value)));
                Assert.That(checks.Where(c => !c.Value).Select(c => c.Key), Is.Empty);
            }
            finally
            {
                match.RequestStop();
                TestMatchLogSession.Shutdown();
                var rows = samples.Select(s => new { s.Tick, Units = s.Entities.Where(u => ids != null && ids.Contains(u.Id)).Select(u =>
                    new { u.Id, Kind = u.Kind.ToString(), u.Position, u.Altitude, u.Moving, Outcome = u.NavigationOutcome.ToString(), u.Completion }) });
                File.WriteAllText(Path.Combine(output, "match.json"), JsonConvert.SerializeObject(new
                {
                    producer = Environment.GetEnvironmentVariable("COMPACT_FORMATION_SOURCE"),
                    seed, horizon, generation, wallSeconds, profile.ProfileId, profile.Revision, profile.UsesRecastGroundRoutes,
                    Map = map.Id, MapRevision = map.Revision, captureStatus, ids, target, checks, rows
                }));
            }
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;

namespace Spacewars.Tests.PlayMode
{
    public sealed partial class GroupOrderReplacementThreadedTests
    {
        private const int Seed = -573776657;
        private const int Generation = 2;
        private const string Human = "foundry-4";
        private const string Ai = "foundry-3";

        [TestCase(false), TestCase(true), Timeout(60000)]
        public void OutsideRampApproachCompletesTheInstalledGroupMove(bool currentRelease)
        {
            // Poses from tick 5340. The reconstruction omits the live battle and
            // issues the same Move again, so route and slot assignment run normally.
            var scenario = new[]
            {
                Explorer(2.4813387726389338, -18.77030322368006, 1.9796689931467124),
                Explorer(1.6611473504205805, -23.388567507033866, 1.7162215222060382),
                Explorer(-19.03138653662337, -8.705516311616666, -0.7595808565065671),
                Explorer(-15.011683846386127, -9.338825755162954, -0.6016494734875865),
                Explorer(-16.15726080075214, -5.991997231249877, -0.872956861755863),
                Explorer(-17.684238116797122, -6.899709803573104, 3.133426191254266),
                Explorer(-22.986105413387065, -7.676924963525011, 0.18265034202304242)
            };
            var config = Configuration(scenario, currentRelease);
            using var runtime = new PlayableRuntime(config, Generation, startPaused: true);
            using var host = new UnityHostRouteService();
            var clock = Stopwatch.StartNew();
            void Service()
            {
                host.Service(runtime, config.Profile.NavigationRequestsPerFrame);
                Assert.IsNull(runtime.Latest.Failure);
                Assert.IsNull(host.CertifiedPublication?.Failure);
                Thread.Sleep(1);
            }

            try
            {
                bool Ready() => runtime.IsPrepared && host.CertifiedPublication?.RecastFoundationBuilds > 0;
                while (!Ready() && clock.Elapsed.TotalSeconds < 20) Service();
                Assert.True(Ready(), "Moss Highland startup with the certified worker attached");
                var initial = runtime.ParticipantView(Human);
                var ids = scenario.Select(s => initial.Entities.Single(u => u.Position.Equals(s.Position)).Id).ToArray();
                int scout = ids[5];
                long start = initial.Tick;
                runtime.RequestPause(false);
                // A snapshot can predate the pause request. Wait for an authority
                // tick after resume before submitting the Move.
                bool Resumed()
                {
                    var latest = runtime.Latest;
                    return !latest.Paused && latest.Tick > start;
                }
                while (!Resumed() && clock.Elapsed.TotalSeconds < 25) Service();
                Assert.True(Resumed(), "Moss Highland authority advanced after resume");
                Assert.True(runtime.TrySubmit(new PlayableCommand(Generation, 39, Human, PlayableCommandKind.Move,
                    ids, new NavPoint(-6.3472514152526855, 1.4160501956939697))).Accepted);

                PlayableCommandReceipt receipt = null;
                PlayableEntitySnapshot focus = null;
                while (runtime.Latest.Tick < start + 450 && clock.Elapsed.TotalSeconds < 40)
                {
                    Service();
                    receipt = runtime.DrainReceipts().SingleOrDefault(r => r.Sequence == 39) ?? receipt;
                    if (receipt != null)
                        Assert.AreEqual(PlayableCommandStatus.Applied, receipt.Status, receipt.Message);
                    focus = runtime.ParticipantView(Human).Entities.Single(u => u.Id == scout);
                    if (focus.Completion?.Order.Sequence == 39) break;
                }

                Assert.AreEqual(PlayableCommandStatus.Applied, receipt?.Status, receipt?.Message);
                Assert.AreEqual(39, focus?.Completion?.Order.Sequence,
                    $"Scout at {focus?.Position.X:R},{focus?.Position.Z:R}, {focus?.NavigationOutcome}, tick {runtime.Latest.Tick}");
                Assert.AreEqual("plateau/0", focus.Location?.SurfaceId);
                Assert.Greater(host.CertifiedPublication.Pumps, 0);
                TestContext.WriteLine($"profile={config.Profile.ProfileId}@{config.Profile.Revision} seed={Seed} " +
                    $"scout activation={focus.Completion.ActivationTick} arrival={focus.Completion.CompletionTick}");
            }
            finally
            {
                runtime.RequestStop();
                Assert.True(SpinWait.SpinUntil(() => runtime.IsStopped, 5000));
            }
        }

        [TestCase(false), TestCase(true), Timeout(60000)]
        public void Order39ArrivesWithDifferentMemberMobilityRevisions(bool currentRelease)
        {
            // Moss Highland journal, tick 5280. The first two scouts are #26 and #30.
            var scenario = new[]
            {
                Explorer(4.071707449738833, -22.44073854320474, 0.0772020321493212),
                Explorer(0.4620205258313416, -25.809844228492583, -2.109855020115397),
                Explorer(-21.986966705322267, -6.864570951461792, 1.7742697635036504),
                Explorer(-18.463910297383688, -6.94778229735361, 0.8245137658663515),
                Explorer(-18.451465514666772, -3.3577608008960755, 0.9653767350459417),
                Explorer(-14.884331483828124, -6.922575643952567, 0.5372858027849712),
                Explorer(-25.587599488213968, -10.577902518248175, 1.5652129193288087)
            };
            var config = Configuration(scenario, currentRelease);
            using var runtime = new PlayableRuntime(config, Generation);
            using var host = new UnityHostRouteService();
            var clock = Stopwatch.StartNew();
            void Service()
            {
                host.Service(runtime, config.Profile.NavigationRequestsPerFrame);
                Assert.IsNull(runtime.Latest.Failure);
                Assert.IsNull(host.CertifiedPublication?.Failure);
                Thread.Sleep(1);
            }

            PlayableCommandReceipt Issue(PlayableCommand command)
            {
                Assert.True(runtime.TrySubmit(command).Accepted, $"submit order {command.Sequence}");
                while (clock.Elapsed.TotalSeconds < 40)
                {
                    Service();
                    var receipt = runtime.DrainReceipts().SingleOrDefault(r => r.Sequence == command.Sequence);
                    if (receipt == null) continue;
                    Assert.AreEqual(PlayableCommandStatus.Applied, receipt.Status, receipt.Message);
                    return receipt;
                }
                throw new AssertionException($"Missing receipt for order {command.Sequence}");
            }

            try
            {
                bool Ready() => runtime.IsPrepared && runtime.Latest.Tick > 0 && host.CertifiedPublication?.RecastFoundationBuilds > 0;
                while (!Ready() && clock.Elapsed.TotalSeconds < 20) Service();
                Assert.True(Ready(), "Moss Highland startup with the certified worker attached");
                var initial = runtime.ParticipantView(Human);
                var ids = scenario.Select(s => initial.Entities.Single(u => u.Position.Equals(s.Position)).Id).ToArray();
                // A real Stop advances six mobility counters while #51 keeps its revision.
                Issue(new PlayableCommand(Generation, 38, Human, PlayableCommandKind.Stop, ids.Take(6).ToArray(), default));
                var move = Issue(new PlayableCommand(Generation, 39, Human, PlayableCommandKind.Move,
                    ids, new NavPoint(-6.3472514152526855, 1.4160501956939697)));

                const int ticksBeforeNextOrder = 215;
                var journalIds = new[] { 26, 30 };
                var moved = new bool[journalIds.Length];
                var scouts = new PlayableEntitySnapshot[journalIds.Length];
                bool Arrived(PlayableEntitySnapshot scout) => scout.Completion?.Order.Sequence == 39 &&
                    scout.Completion.CompletionTick <= move.AppliedTick + ticksBeforeNextOrder;
                var view = runtime.ParticipantView(Human);
                while (clock.Elapsed.TotalSeconds < 40)
                {
                    view = runtime.ParticipantView(Human);
                    for (int i = 0; i < scouts.Length; i++)
                    {
                        var scout = scouts[i] = view.Entities.Single(u => u.Id == ids[i]);
                        moved[i] |= scout.CurrentOrder?.CommandSequence == 39 && scout.Moving &&
                            !scout.Position.Equals(scenario[i].Position);
                    }
                    if (scouts.All(Arrived) || view.Tick >= move.AppliedTick + ticksBeforeNextOrder) break;
                    Service();
                }

                for (int i = 0; i < scouts.Length; i++)
                {
                    var scout = scouts[i];
                    Assert.True(moved[i], $"Scout #{journalIds[i]} must move on order 39");
                    Assert.True(Arrived(scout), $"Scout #{journalIds[i]} must arrive on order 39 by tick " +
                        $"{move.AppliedTick + ticksBeforeNextOrder}; observed tick {view.Tick}, " +
                        $"{scout.NavigationOutcome} at {scout.Position.X:R},{scout.Position.Z:R}");
                    Assert.AreEqual(NavigationOutcome.Arrived, scout.NavigationOutcome);
                    Assert.AreEqual("plateau/0", scout.Location?.SurfaceId);
                    TestContext.WriteLine($"profile={config.Profile.ProfileId}@{config.Profile.Revision} seed={Seed} " +
                        $"scout #{journalIds[i]} arrived {scout.Completion.CompletionTick - move.AppliedTick} ticks after order 39");
                }
            }
            finally
            {
                runtime.RequestStop();
                Assert.True(SpinWait.SpinUntil(() => runtime.IsStopped, 5000));
            }
        }

        [TestCase(false), TestCase(true), Timeout(60000)]
        public void TankReplacementQualifiesAndMovesThroughTheRealRouteWorker(bool currentRelease)
        {
            // Poses from tick 9630. Deliver the recorded AI intents directly
            // through trusted ingress to keep unrelated planner decisions out.
            var scenario = new[]
            {
                Tank(-13.62648183353142, -11.139339197256943, -0.7787883592597326),
                Tank(-15.735415216779447, -9.956366648337905, -0.7500328517224221),
                Tank(-16.818301803850865, -8.862752937652141, -0.7500328517224221),
                Tank(-34.01973939574421, 17.035871978696946, -1.051773487842081)
            };
            var config = Configuration(scenario, currentRelease);
            var world = new PlayableAuthorityTick(config, Generation, asynchronousRoutes: true);
            world.EnableAccruedCredits();
            var navigation = world.Domain.Navigation;
            using var worker = new CertifiedRouteWorker(world.NavigationBinding, config.Profile.NavigationRequestsPerFrame);
            var clock = Stopwatch.StartNew();
            try
            {
                while (worker.Publication.RecastFoundationBuilds == 0 && clock.Elapsed.TotalSeconds < 20)
                {
                    Assert.IsNull(worker.Publication.Failure);
                    Thread.Sleep(1);
                }
                Assert.Greater(worker.Publication.RecastFoundationBuilds, 0);

                var tanks = scenario.Select(s => navigation.Crowd.Units.Single(u => u.Position.Equals(s.Position))).ToArray();
                var ids = tanks.Select(u => u.Id).ToArray();
                void Issue(long seq, long action, PlayableCommandKind kind, int[] recipients, NavPoint point, int site = 0, int target = 0)
                {
                    // The direct authority fixture does not go through PlayableRuntime's
                    // queued host boundary. Acquire the same acknowledged exclusion.
                    using var boundary = world.NavigationBinding.RoutePort.AcquireCapture(() => clock.Elapsed.TotalSeconds >= 40);
                    var receipt = world.Apply(new PlayableCommand(Generation, seq, Ai, kind, recipients, point,
                        siteId: site, targetId: target, origin: PlayableOrderOrigin.Ai,
                        source: PlayableAiOpeningComposition.SourceIdentity, jobId: action, actionId: action));
                    Assert.AreEqual(PlayableCommandStatus.Applied, receipt.Status, receipt.Message);
                }

                world.TryAdvance();
                Issue(2305843009213694196, 283, PlayableCommandKind.AttackMove, ids.Take(3).ToArray(), new NavPoint(6.42, -22), 14);
                Issue(2305843009213694204, 291, PlayableCommandKind.Follow, new[] { ids[3] }, default, target: ids[0]);
                void AdvanceUntil(Func<bool> condition, int ticks)
                {
                    long end = world.Tick + ticks;
                    double next = clock.Elapsed.TotalSeconds;
                    while (!condition() && world.Tick < end && clock.Elapsed.TotalSeconds < 40)
                    {
                        Assert.IsNull(worker.Publication.Failure);
                        while (clock.Elapsed.TotalSeconds < next) Thread.Sleep(1);
                        world.TryAdvance();
                        next = clock.Elapsed.TotalSeconds + 1d / 30;
                    }
                }
                AdvanceUntil(() => tanks[0].Moving && tanks[0].InstalledRequest > 0, 90);
                Assert.True(tanks[0].Moving, "old order must be active before Replace");

                const long replacement = 2305843009213694209;
                var before = tanks.Select(u => u.Position).ToArray();
                Issue(replacement, 301, PlayableCommandKind.AttackMove, ids, new NavPoint(6.42, 22), 16);
                bool InstalledAndMoved() => tanks.Select((tank, i) =>
                    world.Domain.TacticalQueueProjection(tank.Id, PlayableOwner.Third)?.Active?.CommandSequence == replacement &&
                    !tank.Position.Equals(before[i])).All(x => x);
                AdvanceUntil(InstalledAndMoved, 100);
                Assert.True(InstalledAndMoved(), $"replacement must activate and move all recipients, tick {world.Tick}, " +
                    string.Join(", ", ids.Select(id => $"{id}={navigation.TechnicalFailureFor(id)}")));
                TestContext.WriteLine($"profile={config.Profile.ProfileId}@{config.Profile.Revision} seed={Seed} " +
                    $"all four moving on replacement by tick {world.Tick}");
            }
            finally
            {
                worker.Dispose();
                Assert.True(SpinWait.SpinUntil(() => worker.IsStopped, 5000));
                world.Stop();
                navigation.ReleaseCommittedHostRoot();
                world.Domain.DestroyOwnedNavigation();
                Assert.Zero(navigation.PhysicalHostBudget.PhysicalBytes);
            }
        }

        [Test, Timeout(60000)]
        public void MovingScoutContinuesWhileItsForwardReplacementIsWithheld()
        {
            var start = Explorer(-17.684331483828124, -6.899709803850865, 3.133426191254266);
            var config = Configuration(new[] { start }, true);
            using var runtime = new PlayableRuntime(config, Generation);
            int withhold = 0;
            var kernel = new CertifiedRouteKernel { PauseAtPhysicalBoundary = () => Volatile.Read(ref withhold) != 0 };
            using var worker = new CertifiedRouteWorker(runtime.NavigationBinding, config.Profile.NavigationRequestsPerFrame,
                kernel, () => runtime.IsStopRequested, runtime.ReportRouteFailure);
            void Until(Func<bool> condition, string label)
            {
                bool reached = SpinWait.SpinUntil(() => runtime.IsStopped || condition(), 12000);
                Assert.IsNull(worker.Publication.Failure, label);
                Assert.IsNull(runtime.Latest.Failure, label);
                Assert.True(reached && !runtime.IsStopped, label);
            }

            try
            {
                Until(() => runtime.IsPrepared && runtime.Latest.Tick > 0 && worker.Publication.RecastFoundationBuilds > 0, "match startup");
                var view = runtime.ParticipantView(Human);
                int id = view.Entities.Single(e => e.Position.Equals(start.Position)).Id;
                PlayableEntitySnapshot Scout() => runtime.ParticipantView(Human).Entities.Single(e => e.Id == id);
                void Move(int sequence, NavPoint point) =>
                    Assert.True(runtime.TrySubmit(new PlayableCommand(Generation, sequence, Human,
                        PlayableCommandKind.Move, new[] { id }, point)).Accepted);

                Move(1, new NavPoint(-6.3472514152526855, 1.4160501956939697));
                Until(() => Scout().Moving && !Scout().Position.Equals(start.Position), "first route physically moving");
                Volatile.Write(ref withhold, 1);
                Move(2, new NavPoint(-5.8, 1.5));
                Until(() => Scout().Queue?.Pending?.CommandSequence == 2, "forward replacement pending");
                var position = Scout().Position;
                long tick = runtime.Latest.Tick;
                Until(() => runtime.Latest.Tick >= tick + 8, "eight authority ticks during delayed planning");
                var waiting = Scout();
                Assert.AreEqual(2, waiting.Queue?.Pending?.CommandSequence);
                Assert.AreNotEqual(position, waiting.Position, "old route must continue after replacement admission");
                Volatile.Write(ref withhold, 0);
                Until(() => Scout().Queue?.Active?.CommandSequence == 2 || Scout().Completion?.Order.Sequence == 2,
                    "replacement installed at actual position");
                Until(() => Scout().Completion?.Order.Sequence == 2, "replacement arrival");
                TestContext.WriteLine($"profile={config.Profile.ProfileId}@{config.Profile.Revision} seed={Seed} delayed={tick} " +
                    $"old=({position.X:R},{position.Z:R})->({waiting.Position.X:R},{waiting.Position.Z:R}) arrived={Scout().Completion.CompletionTick}");
            }
            finally
            {
                Volatile.Write(ref withhold, 0);
                runtime.RequestStop();
                Assert.True(SpinWait.SpinUntil(() => runtime.IsStopped, 5000));
            }
        }

        [Test, Timeout(60000)]
        public void JournalRecordsCommittedAirDestinationsAndCancellation()
        {
            string output = Environment.GetEnvironmentVariable("SPACEWARS_INDEPENDENT_ROUTE_WITNESS") ??
                Path.Combine(Application.dataPath, "../../.local/independent-unit-routes/witness");
            var start = new OfflineScenarioUnit(4, PlayableEntityKind.Hawk, new NavPoint(-22, -8), 0);
            var config = Configuration(new[] { start }, true);
            TestMatchLogSession.StartDefault(output);
            string journal = TestMatchLogSession.CurrentDirectory;
            int id;
            var goal = new NavPoint(-14, -8);
            try
            {
                using var runtime = new PlayableRuntime(config, Generation);
                using var worker = new CertifiedRouteWorker(runtime.NavigationBinding, config.Profile.NavigationRequestsPerFrame,
                    new CertifiedRouteKernel(), () => runtime.IsStopRequested, runtime.ReportRouteFailure);
                try
                {
                    Assert.True(SpinWait.SpinUntil(() => runtime.IsPrepared && runtime.Latest.Tick > 0, 20000));
                    id = runtime.ParticipantView(Human).Entities.Single(e => e.Position.Equals(start.Position)).Id;
                    Assert.True(runtime.TrySubmit(new PlayableCommand(Generation, 1, Human, PlayableCommandKind.Move,
                        new[] { id }, goal)).Accepted);
                    Assert.True(SpinWait.SpinUntil(() => runtime.ParticipantView(Human).Entities.Single(e => e.Id == id).Moving, 5000));
                    Assert.True(runtime.TrySubmit(new PlayableCommand(Generation, 2, Human, PlayableCommandKind.Hold,
                        new[] { id })).Accepted);
                    Assert.True(SpinWait.SpinUntil(() => !runtime.ParticipantView(Human).Entities.Single(e => e.Id == id).Moving, 5000));
                }
                finally
                {
                    runtime.RequestStop();
                    Assert.True(SpinWait.SpinUntil(() => runtime.IsStopped && worker.IsStopped, 5000));
                }
            }
            finally { TestMatchLogSession.Shutdown(); }
            var match = Directory.GetDirectories(journal, "match-*").Single();
            var rows = Directory.GetFiles(match, "events-*.jsonl").OrderBy(file => file, StringComparer.Ordinal)
                .SelectMany(File.ReadLines).Select(JObject.Parse)
                .Where(row => (string)row["Type"] == "accepted-flight" && (int)row["Entity"] == id).ToArray();
            var slot = TestMatchLogSession.ReadAirAssignments(match).Single(r => r.Owner == Human && r.Sequence == 1);
            CollectionAssert.AreEqual(new[] { id }, slot.Aircraft);
            Assert.AreEqual(goal, slot.Destinations.Single());
            Assert.True(rows.Any(r => (string)r["OwnerId"] == Human && (long)r["Stamp"]["Sequence"] == 1 &&
                (bool)r["Moving"] && (double)r["Destination"]["X"] == goal.X && (double)r["Destination"]["Z"] == goal.Z));
            Assert.True(rows.Any(r => (long)r["Stamp"]["Sequence"] == 2 && !(bool)r["Moving"] && (bool)r["Held"]));
            foreach (var tick in rows.GroupBy(r => (long)r["Tick"]))
                Assert.AreEqual(tick.Count(), tick.Select(r => (int)r["Ordinal"]).Distinct().Count());
        }

        [Test, Timeout(60000)]
        public void DelayedReplacementLeavesCombatAndEconomyRunning()
        {
            string output = Environment.GetEnvironmentVariable("SPACEWARS_INDEPENDENT_ROUTE_WITNESS") ??
                Path.Combine(Application.dataPath, "../../.local/independent-unit-routes/witness");
            TestMatchLogSession.StartDefault(output);
            string journal = TestMatchLogSession.CurrentDirectory;
            Assert.IsNotEmpty(journal);
            var config = DelayedReplacementConfiguration();
            (int scout, long arrival, NavPoint finalPosition) live;
            try { live = RunDelayedReplacementMatch(config); }
            finally { TestMatchLogSession.Shutdown(); }
            int scout = live.scout;

            var match = Directory.GetDirectories(journal, "match-*").Single();
            var decisions = Directory.GetFiles(match, "events-*.jsonl").OrderBy(file => file, StringComparer.Ordinal)
                .SelectMany(File.ReadLines).Select(JObject.Parse).ToArray();
            var routes = decisions.Where(row => (string)row["Type"] == "accepted-route").ToArray();
            foreach (var route in routes.Where(row => (int)row["Entity"] == scout))
            {
                var slot = decisions.Last(row => (string)row["Type"] == "ground-slot" &&
                    (long)row["Request"] == (long)route["Request"] && (int)row["Entity"] == scout);
                Assert.That((long)slot["Tick"], Is.LessThanOrEqualTo((long)route["Tick"]));
                if ((long)slot["Tick"] == (long)route["Tick"])
                    Assert.Less((int)slot["Ordinal"], (int)route["Ordinal"]);
                Assert.AreEqual((string)route["Corridor"]["Endpoint"]["SurfaceId"], (string)slot["SurfaceId"]);
                Assert.True(JToken.DeepEquals(route["Corridor"]["Endpoint"]["Position"], slot["Position"]));
            }
            foreach (var tick in decisions.Where(row => row["Ordinal"] != null).GroupBy(row => (long)row["Tick"]))
                CollectionAssert.AreEqual(Enumerable.Range(0, tick.Count()), tick.Select(row => (int)row["Ordinal"]));
            var scoutRoutes = routes.Where(row => (int)row["Entity"] == scout).ToArray();
            CollectionAssert.AreEqual(new long[] { 1, 2 }, scoutRoutes.Select(row => (long)row["Corridor"]["CommandSequence"]));
            Assert.True(scoutRoutes.All(row => (string)row["OwnerId"] == Human && row["Points"].HasValues &&
                row["Corridor"]["Legs"].HasValues));
            Assert.Less((long)scoutRoutes[0]["Tick"], (long)scoutRoutes[1]["Tick"]);
            var recorded = TestMatchLogSession.ReadGroundRoutes(match);
            CollectionAssert.AreEqual(scoutRoutes.Select(row => (string)row["Corridor"]["StableKey"]),
                recorded.Where(route => route.Entity == scout && !route.Reissued && !route.Failure.HasValue)
                    .Select(route => route.Corridor.StableKey));
            Assert.True(recorded.Where(route => route.Status == NavSolveStatus.Ready)
                .All(route => route.Recast != null && route.Recast.SpineLegs.Count > 0));
            var commands = decisions.Where(row => (string)row["Type"] == "command").Select(row => row["Record"])
                .ToDictionary(row => (long)row["Sequence"]);
            var receipts = decisions.Where(row => (string)row["Type"] == "command-receipt")
                .Select(row => row["Record"]).ToArray();
            var recordedSlots = TestMatchLogSession.ReadGroundSlots(match);
            PlayableCommand RecordedCommand(long sequence)
            {
                var command = commands[sequence];
                return new PlayableCommand(Generation, sequence, (string)command["PlayerId"],
                    command["Kind"].ToObject<PlayableCommandKind>(), command["EntityIds"].ToObject<int[]>(),
                    command["Target"].ToObject<NavPoint>());
            }
            var replay = new PlayableAuthorityTick(config, Generation, asynchronousRoutes: true);
            try
            {
                replay.UseRecordedGroundRoutes(recorded, recordedSlots);
                foreach (var receipt in receipts)
                {
                    long tick = (long)receipt["AppliedTick"];
                    while (replay.Tick < tick) Assert.True(replay.TryAdvance());
                    long sequence = (long)receipt["Sequence"];
                    var replayed = replay.Apply(RecordedCommand(sequence));
                    Assert.AreEqual(tick, replayed.AppliedTick);
                    Assert.AreEqual(receipt["Status"].ToObject<PlayableCommandStatus>(), replayed.Status);
                }
                while (replay.Tick < live.arrival) Assert.True(replay.TryAdvance());
                Assert.AreEqual(0, replay.RemainingRecordedGroundRoutes);
                Assert.AreEqual(0, replay.RemainingRecordedGroundSlots);
                Assert.True(replay.Domain.Navigation.Crowd.TryGet(scout, out var actor));
                Assert.AreEqual(NavigationOutcome.Arrived, actor.Outcome);
                Assert.AreEqual(live.finalPosition, actor.Position);
            }
            finally { replay.Stop(); }
            var cancelled = new PlayableAuthorityTick(config, Generation, asynchronousRoutes: true);
            try
            {
                var first = recordedSlots.First();
                cancelled.UseRecordedGroundRoutes(Array.Empty<RecordedGroundRoute>(), new[] { first });
                while (cancelled.Tick < first.Tick) Assert.True(cancelled.TryAdvance());
                Assert.AreEqual(PlayableCommandStatus.Applied, cancelled.Apply(RecordedCommand(1)).Status);
                Assert.True(cancelled.Domain.Navigation.Crowd.TryGet(scout, out var actor));
                var position = actor.Position;
                long delivery = recorded.First(route => route.Entity == scout).Tick;
                while (cancelled.Tick + 1 < delivery) Assert.True(cancelled.TryAdvance());
                Assert.AreEqual(PlayableCommandStatus.Applied, cancelled.Apply(new PlayableCommand(
                    Generation, 2, Human, PlayableCommandKind.Hold, new[] { scout })).Status);
                Assert.True(cancelled.TryAdvance());
                Assert.AreEqual(delivery, cancelled.Tick);
                Assert.True(actor.Held);
                Assert.AreEqual(position, actor.Position);
                Assert.AreEqual(0, cancelled.Domain.Navigation.PendingCount);
                Assert.AreEqual(0, cancelled.RemainingRecordedGroundSlots);
            }
            finally { cancelled.Stop(); }
            var unreachable = new PlayableAuthorityTick(config, Generation, asynchronousRoutes: true);
            try
            {
                var first = recordedSlots.First();
                var delivery = recorded.First(route => route.Entity == scout);
                var failure = new RecordedGroundRoute(delivery.Tick, delivery.Ordinal, first.Owner, first.Entity,
                    first.Request, first.Order, NavSolveStatus.UnreachableInGraph, null, Array.Empty<NavPoint>(), null,
                    reissued: false, failure: NavigationOutcome.Unreachable);
                unreachable.UseRecordedGroundRoutes(new[] { failure }, new[] { first });
                while (unreachable.Tick < first.Tick) Assert.True(unreachable.TryAdvance());
                Assert.AreEqual(PlayableCommandStatus.Applied, unreachable.Apply(RecordedCommand(1)).Status);
                while (unreachable.Tick < delivery.Tick) Assert.True(unreachable.TryAdvance());
                Assert.True(unreachable.Domain.Navigation.Crowd.TryGet(scout, out var actor));
                Assert.AreEqual(NavigationOutcome.Unreachable, actor.LastFailure);
                Assert.False(actor.Moving);
                Assert.AreEqual(0, unreachable.RemainingRecordedGroundRoutes);
            }
            finally { unreachable.Stop(); }
            TestContext.WriteLine($"route-journal={match} ticks={scoutRoutes[0]["Tick"]},{scoutRoutes[1]["Tick"]}");
        }

        private static OfflineMatchConfiguration DelayedReplacementConfiguration()
        {
            var scoutStart = Explorer(-17.684331483828124, -6.899709803850865, 3.133426191254266);
            var defender = new OfflineScenarioUnit(4, PlayableEntityKind.Tank, new NavPoint(4.07, -22.44));
            var attacker = new OfflineScenarioUnit(3, PlayableEntityKind.Tank, new NavPoint(0.46, -25.81));
            return Configuration(new[] { scoutStart, defender, attacker }, currentRelease: true);
        }

        private static (int scout, long arrival, NavPoint finalPosition) RunDelayedReplacementMatch(OfflineMatchConfiguration config)
        {
            var scoutStart = config.ScenarioUnits.First();
            using var runtime = new PlayableRuntime(config, Generation);
            int withhold = 0;
            var kernel = new CertifiedRouteKernel { PauseAtPhysicalBoundary = () => Volatile.Read(ref withhold) != 0 };
            using var worker = new CertifiedRouteWorker(runtime.NavigationBinding, config.Profile.NavigationRequestsPerFrame,
                kernel, () => runtime.IsStopRequested, runtime.ReportRouteFailure);

            void Until(Func<bool> condition, string label)
            {
                bool reached = SpinWait.SpinUntil(() => runtime.IsStopped || condition(), 12000);
                Assert.IsNull(worker.Publication.Failure, label);
                Assert.IsNull(runtime.Latest.Failure, label);
                Assert.True(reached && !runtime.IsStopped, label);
            }

            try
            {
                Until(() => runtime.IsPrepared && runtime.Latest.Tick > 0 &&
                    worker.Publication.RecastFoundationBuilds > 0, "threaded match startup");
                PlayableSnapshot View() => runtime.ParticipantView(Human);
                var initial = View();
                int scout = initial.Entities.Single(u => u.Position.Equals(scoutStart.Position)).Id;
                int tank = initial.Entities.Single(u => u.Kind == PlayableEntityKind.Tank &&
                    u.Owner == initial.Owner).Id;
                PlayableEntitySnapshot Unit(PlayableSnapshot view, int id) => view.Entities.SingleOrDefault(u => u.Id == id);
                void Move(int sequence, NavPoint point) => Assert.True(runtime.TrySubmit(
                    new PlayableCommand(Generation, sequence, Human, PlayableCommandKind.Move, new[] { scout }, point)).Accepted);

                Move(1, new NavPoint(-6.3472514152526855, 1.4160501956939697));
                Until(() => Unit(View(), scout)?.Moving == true, "first route physically moving");
                Volatile.Write(ref withhold, 1);
                Move(2, new NavPoint(-5.8, 1.5));
                Until(() => Unit(View(), scout)?.Queue?.Pending?.CommandSequence == 2, "replacement pending");
                var before = View();
                var startPosition = Unit(before, scout).Position;
                double startHealth = Unit(before, tank).ExactHealth ?? Unit(before, tank).Health;
                Assert.Greater(startHealth, 0, "defender alive at start of delay");
                Until(() => View().Tick >= before.Tick + 60, "sixty authority ticks during the delayed route");
                var after = View();
                var afterScout = Unit(after, scout);
                var afterTank = Unit(after, tank);
                double health = afterTank?.ExactHealth ?? afterTank?.Health ?? 0;
                Assert.AreEqual(2, afterScout?.Queue?.Pending?.CommandSequence);
                Assert.AreNotEqual(startPosition, afterScout.Position);
                Assert.Greater(after.ExactCredits, before.ExactCredits, "credits must accrue during the delay");
                Assert.Less(health, startHealth, "combat must deal damage during the delay");
                Assert.AreEqual(PlayableMatchOutcome.Playing, after.Outcome);
                Volatile.Write(ref withhold, 0);
                Until(() => Unit(View(), scout)?.Completion?.Order.Sequence == 2, "replacement arrival");
                var arrived = Unit(View(), scout);
                TestContext.WriteLine($"profile={after.ProfileId}@{after.ProfileRevision} seed={Seed} " +
                    $"delay={before.Tick}..{after.Tick} credits={before.ExactCredits:R}->{after.ExactCredits:R} " +
                    $"tank-health={startHealth:R}->{health:R} scout-arrival={Unit(View(), scout).Completion.CompletionTick}");
                Assert.IsNull(runtime.JournalFailure);
                return (scout, arrived.Completion.CompletionTick, arrived.Position);
            }
            finally
            {
                Volatile.Write(ref withhold, 0);
                runtime.RequestStop();
                Assert.True(SpinWait.SpinUntil(() => runtime.IsStopped, 5000));
            }
        }

        [Test, Timeout(60000)]
        public void CurrentSaveRestoresActiveNativeSearchWithoutRepeatingItsFirstStep()
        {
            var start = Explorer(-17.684331483828124, -6.899709803850865, 3.133426191254266);
            var config = Configuration(new[] { start }, true);
            byte[] Capture(PlayableRuntime match)
            {
                var task = match.RequestCaptureBytes();
                Assert.True(SpinWait.SpinUntil(() => task.IsCompleted, 10000));
                return task.GetAwaiter().GetResult();
            }
            static RecastGroupPreparation Preparation(CertifiedRouteKernel kernel) =>
                (RecastGroupPreparation)typeof(CertifiedRouteKernel).GetField("recastPreparation",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(kernel);
            byte[] originalBytes;
            int id;
            using (var original = new PlayableRuntime(config, Generation))
            {
                var kernel = new CertifiedRouteKernel();
                int suspended = 0;
                kernel.PauseAtPhysicalBoundary = () =>
                {
                    var prep = Preparation(kernel);
                    if (prep == null) return false;
                    prep.QueryStepWork = 1;
                    if (!prep.HasActiveQuery() || prep.Iterations == 0) return false;
                    Volatile.Write(ref suspended, 1);
                    return true;
                };
                using var worker = new CertifiedRouteWorker(original.NavigationBinding, config.Profile.NavigationRequestsPerFrame,
                    kernel, () => original.IsStopRequested, original.ReportRouteFailure);
                try
                {
                    Assert.True(SpinWait.SpinUntil(() => original.IsPrepared && original.Latest.Tick > 0, 20000));
                    id = original.ParticipantView(Human).Entities.Single(e => e.Position.Equals(start.Position)).Id;
                    Assert.True(original.TrySubmit(new PlayableCommand(Generation, 1, Human, PlayableCommandKind.Move,
                        new[] { id }, new NavPoint(-6.3472514152526855, 1.4160501956939697))).Accepted);
                    Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref suspended) != 0, 10000), "live Detour frontier");
                    original.RequestPause(true);
                    Assert.True(SpinWait.SpinUntil(() => original.Latest.Paused, 5000));
                    originalBytes = Capture(original);
                }
                finally
                {
                    original.RequestStop();
                    Assert.True(SpinWait.SpinUntil(() => original.IsStopped && worker.IsStopped, 5000));
                }
            }
            using var saved = PlayableWorldState.Decode(originalBytes);
            Assert.AreEqual(6, BitConverter.ToInt32(saved.RuntimeTransport, 0));
            using var restored = PlayableRuntime.RestoreBytes(originalBytes, config);
            int hold = 1;
            var restoredKernel = new CertifiedRouteKernel { PauseAtPhysicalBoundary = () => Volatile.Read(ref hold) != 0 };
            using var consumer = new CertifiedRouteWorker(restored.NavigationBinding, config.Profile.NavigationRequestsPerFrame,
                restoredKernel, () => restored.IsStopRequested, restored.ReportRouteFailure);
            try
            {
                Assert.True(SpinWait.SpinUntil(() => restored.IsPrepared && restored.Latest.Paused, 10000));
                using var again = PlayableWorldState.Decode(Capture(restored));
                CollectionAssert.AreEqual(saved.RuntimeTransport, again.RuntimeTransport, "same serialized native nodes and heap order");
                CollectionAssert.AreEqual(saved.Domain, again.Domain);
                CollectionAssert.AreEqual(saved.Navigation, again.Navigation);
                Volatile.Write(ref hold, 0);
                restored.RequestPause(false);
                Assert.True(SpinWait.SpinUntil(() => restored.ParticipantView(Human).Entities
                    .Single(e => e.Id == id).Completion?.Order.Sequence == 1, 12000), "arrival after restoring native frontier");
                Assert.AreEqual(0, Preparation(restoredKernel).LongQueries, "restored search was never issued again");
            }
            finally
            {
                restored.RequestStop();
                Assert.True(SpinWait.SpinUntil(() => restored.IsStopped && consumer.IsStopped, 5000));
            }
        }

        [Test, Timeout(60000)]
        public void CurrentSaveRestoresPendingReplacementAndItsOldMotion()
        {
            var start = Explorer(-17.684331483828124, -6.899709803850865, 3.133426191254266);
            var config = Configuration(new[] { start }, currentRelease: true);
            byte[] CapturePaused(PlayableRuntime runtime)
            {
                Assert.True(runtime.Latest.Paused);
                var capture = runtime.RequestCaptureBytes();
                Assert.True(SpinWait.SpinUntil(() => capture.IsCompleted, 10000), "current save");
                return capture.GetAwaiter().GetResult();
            }
            void Wait(PlayableRuntime runtime, CertifiedRouteWorker worker, Func<bool> condition, string label)
            {
                bool reached = SpinWait.SpinUntil(() => runtime.IsStopped || condition(), 10000);
                Assert.IsNull(runtime.Latest.Failure, label);
                Assert.IsNull(worker.Publication.Failure, label);
                Assert.True(reached && !runtime.IsStopped, label);
            }
            PlayableEntitySnapshot Scout(PlayableRuntime runtime, int id) =>
                runtime.ParticipantView(Human).Entities.Single(u => u.Id == id);

            byte[] bytes;
            int scout;
            PlayableEntitySnapshot prefix;
            using (var original = new PlayableRuntime(config, Generation))
            {
                int withhold = 0;
                var kernel = new CertifiedRouteKernel { PauseAtPhysicalBoundary = () => Volatile.Read(ref withhold) != 0 };
                using var worker = new CertifiedRouteWorker(original.NavigationBinding, config.Profile.NavigationRequestsPerFrame,
                    kernel, () => original.IsStopRequested, original.ReportRouteFailure);
                try
                {
                    Wait(original, worker, () => original.IsPrepared && original.Latest.Tick > 0 &&
                        worker.Publication.RecastFoundationBuilds > 0, "original startup");
                    scout = original.ParticipantView(Human).Entities.Single(u => u.Position.Equals(start.Position)).Id;
                    void Move(int sequence, NavPoint goal) => Assert.True(original.TrySubmit(
                        new PlayableCommand(Generation, sequence, Human, PlayableCommandKind.Move, new[] { scout }, goal)).Accepted);

                    Move(1, new NavPoint(-6.3472514152526855, 1.4160501956939697));
                    Wait(original, worker, () => Scout(original, scout).Moving, "first route installed");
                    Volatile.Write(ref withhold, 1);
                    Move(2, new NavPoint(-5.8, 1.5));
                    Wait(original, worker, () => Scout(original, scout).Queue?.Pending?.CommandSequence == 2,
                        "replacement pending before pause");
                    original.RequestPause(true);
                    Wait(original, worker, () => original.Latest.Paused, "pause acknowledged");
                    prefix = Scout(original, scout);
                    Assert.True(prefix.Moving);
                    Assert.AreEqual(1, prefix.Queue?.Active?.CommandSequence);
                    Assert.AreEqual(2, prefix.Queue?.Pending?.CommandSequence);
                    bytes = CapturePaused(original);
                }
                finally
                {
                    original.RequestStop();
                    Assert.True(SpinWait.SpinUntil(() => original.IsStopped, 5000));
                }
            }

            using var saved = PlayableWorldState.Decode(bytes);
            Assert.AreEqual(PlayableWorldState.CurrentVersion, saved.Version);
            Assert.AreEqual(Seed, saved.Seed);
            Assert.AreEqual(Generation, saved.Generation);
            Assert.AreEqual(config.SourceIdentity, saved.SourceIdentity);
            CollectionAssert.AreEqual(WorldWire.Binding(config.Profile), saved.Binding);
            CollectionAssert.AreEqual(OfflineConfigurationWire.Binding(config, config.Profile), saved.Participants);
            string output = Environment.GetEnvironmentVariable("SPACEWARS_INDEPENDENT_ROUTE_WITNESS");
            if (output != null) File.WriteAllBytes(Path.Combine(output, "pending-replacement.world"), bytes);

            using var restored = PlayableRuntime.RestoreBytes(bytes, config);
            int holdRestored = 1;
            var restoredKernel = new CertifiedRouteKernel { PauseAtPhysicalBoundary = () => Volatile.Read(ref holdRestored) != 0 };
            using var restoredWorker = new CertifiedRouteWorker(restored.NavigationBinding,
                config.Profile.NavigationRequestsPerFrame, restoredKernel,
                () => restored.IsStopRequested, restored.ReportRouteFailure);
            try
            {
                Wait(restored, restoredWorker, () => restored.IsPrepared && restored.Latest.Paused, "cold restore");
                Assert.AreEqual(saved.Tick, restored.Latest.Tick);
                var scoutAfter = Scout(restored, scout);
                Assert.AreEqual(prefix.Position, scoutAfter.Position);
                Assert.AreEqual(prefix.Location, scoutAfter.Location);
                Assert.AreEqual(prefix.HullHeading, scoutAfter.HullHeading);
                Assert.AreEqual(prefix.Moving, scoutAfter.Moving);
                Assert.AreEqual(1, scoutAfter.Queue?.Active?.CommandSequence);
                Assert.AreEqual(2, scoutAfter.Queue?.Pending?.CommandSequence);
                using var recaptured = PlayableWorldState.Decode(CapturePaused(restored));
                CollectionAssert.AreEqual(saved.Domain, recaptured.Domain);
                CollectionAssert.AreEqual(saved.Navigation, recaptured.Navigation);
                CollectionAssert.AreEqual(saved.RuntimeTransport, recaptured.RuntimeTransport);

                Volatile.Write(ref holdRestored, 0);
                restored.RequestPause(false);
                Wait(restored, restoredWorker, () => Scout(restored, scout).Completion?.Order.Sequence == 2,
                    "replacement finishes after restoring the pending request");
                var completion = Scout(restored, scout).Completion;
                TestContext.WriteLine($"profile={config.Profile.ProfileId}@{config.Profile.Revision} seed={Seed} " +
                    $"schema={saved.Version} save-tick={saved.Tick} scout-arrival={completion.CompletionTick}");
            }
            finally
            {
                restored.RequestStop();
                Assert.True(SpinWait.SpinUntil(() => restored.IsStopped, 5000));
            }
        }

        private static OfflineScenarioUnit Explorer(double x, double z, double heading) =>
            new OfflineScenarioUnit(4, PlayableEntityKind.Explorer, new NavPoint(x, z), heading);

        private static OfflineScenarioUnit Tank(double x, double z, double heading) =>
            new OfflineScenarioUnit(3, PlayableEntityKind.Tank, new NavPoint(x, z), heading);

        private static OfflineMatchConfiguration Configuration(OfflineScenarioUnit[] scenario, bool currentRelease)
        {
            var map = new MossHighlandMap(JsonUtility.FromJson<MossHighlandProfileData>(Resources.Load<TextAsset>("MossHighlandProfile").text));
            var defaults = PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text), map);
            var path = Path.Combine(Path.GetTempPath(), $"spacewars-group-orders-{Guid.NewGuid():N}", "balance.json");
            var store = new NativeBalanceStore(defaults, path, authoring: false, startOnRelease: true);
            if (!currentRelease)
                store.Select(NativeBalanceStore.CorridorExperimentId, NativeBalanceStore.CorridorExperimentVersion);
            var profile = PlayableProfile.Create(store.Active.CopyData(), map);
            Assert.True(profile.UsesRecastGroundRoutes);
            var roster = new[]
            {
                new OfflineParticipant("player-1", 1, 2, OfflineControl.Human),
                new OfflineParticipant("foundry-2", 2, 1, OfflineControl.Human),
                new OfflineParticipant(Ai, 3, 2, OfflineControl.Human),
                new OfflineParticipant(Human, 4, 1, OfflineControl.Human)
            };
            var basis = map.Configuration(profile, Seed, roster);
            var costs = new double[4, 4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    costs[i, j] = basis.RouteCost(i, j);
            return new OfflineMatchConfiguration(profile, basis.SourceIdentity, basis.MapIdentity,
                basis.RouteProvenance, Seed, roster, basis.Starts.ToArray(), basis.Sites.ToArray(),
                basis.Obstacles.ToArray(), costs, scenario: scenario, terrain: map.Id);
        }
    }
}

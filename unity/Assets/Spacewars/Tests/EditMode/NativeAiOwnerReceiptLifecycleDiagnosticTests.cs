using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiOwnerReceiptLifecycleDiagnosticTests
    {
        private const int Seed = 19092026;
        private const long Generation = 701;
        private const int HostWatchdogSeconds = 180;
        private const int NoProgressWatchdogSeconds = 15;
        private const int OriginalWallBoundarySeconds = 7;
        private const string Owner = "player-1";

        private sealed class BuildAtCandidateWitness
        {
            internal long DecisionOrdinal;
            internal string ObservationIdentity;
            internal string ReceiptIdentity;
            internal long ActionId;
            internal int SiteId;
            internal int SlotId;
            internal PlayableBuildingKind BuildingKind;
            internal long DueTick;
        }

        private static object CandidateRow(BuildAtCandidateWitness candidate)
        {
            return candidate == null ? null : (object)new
            {
                candidate.DecisionOrdinal, candidate.ObservationIdentity, candidate.ReceiptIdentity,
                candidate.ActionId, candidate.SiteId, candidate.SlotId,
                BuildingKind = candidate.BuildingKind.ToString(), candidate.DueTick
            };
        }

        private static string Root()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "workflow/project.json"))) dir = dir.Parent;
            Assert.NotNull(dir, "project root");
            return dir.FullName;
        }

        private static PlayableAiOwnerCheckpoint EnemyCheckpoint(PlayableRuntime runtime)
        {
            var property = typeof(PlayableRuntime).GetProperty("EnemyAiCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(property, "immutable opponent checkpoint publication");
            return (PlayableAiOwnerCheckpoint)property.GetValue(runtime, null);
        }

        private static object OwnerRow(PlayableAiOwnerCheckpoint checkpoint)
        {
            return new
            {
                checkpoint.OwnerId, checkpoint.SourceIdentity, checkpoint.ProfileId, checkpoint.ProfileRevision,
                checkpoint.Seed, checkpoint.Generation, checkpoint.DecisionTick, checkpoint.LastScoutTick,
                checkpoint.LastScoutOrderTick, checkpoint.PendingActionId, checkpoint.PendingDueTick,
                checkpoint.LastCommandSequence, checkpoint.LastPolicy, checkpoint.LastActionKind,
                checkpoint.Opening, checkpoint.OpeningExecution, checkpoint.GamePhase, checkpoint.Composition,
                checkpoint.EconomyDecision, checkpoint.Expansion, checkpoint.Infrastructure,
                Records = checkpoint.Records.ToArray()
            };
        }

        private static object BuildingRow(PlayableSnapshot snapshot)
        {
            return new
            {
                snapshot.Tick, snapshot.Credits, snapshot.SettledIncome,
                EnemyPrivateStateLeakCount = snapshot.Buildings.Count(b => b.Owner == PlayableOwner.Enemy && b.PrivateState != null),
                Buildings = snapshot.Buildings.Where(b => b.Owner == PlayableOwner.Player).OrderBy(b => b.Id)
                    .Select(b => new { b.Id, Owner = b.Owner.ToString(), Kind = b.Kind.ToString(), b.SiteId, b.SlotId,
                        b.ParentId, b.Health, b.Progress, Phase = b.Phase.ToString(), b.PrivateState }).ToArray()
            };
        }

        private static void ValidateBinding(PlayableAiOwnerCheckpoint checkpoint, string owner, AiProfile profile)
        {
            Assert.NotNull(checkpoint, owner + " checkpoint publication");
            Assert.AreEqual(owner, checkpoint.OwnerId);
            Assert.AreEqual(PlayableAiOpeningComposition.SourceIdentity, checkpoint.SourceIdentity);
            Assert.AreEqual(PlayableProfile.Default.ProfileId, checkpoint.ProfileId);
            Assert.AreEqual(PlayableProfile.Default.Revision, checkpoint.ProfileRevision);
            Assert.AreEqual(Seed, checkpoint.Seed);
            Assert.AreEqual(Generation, checkpoint.Generation);
            Assert.AreEqual(profile.Id, checkpoint.AiProfileId);
            Assert.AreEqual(profile.Revision, checkpoint.AiRevision);
            Assert.AreEqual(profile.Hash, checkpoint.AiHash);
            foreach (var receipt in checkpoint.Records)
            {
                Assert.AreEqual(owner, receipt.OwnerId, "receipt owner");
                Assert.AreEqual(PlayableAiOpeningComposition.SourceIdentity, receipt.SourceIdentity, "receipt source");
                Assert.NotNull(receipt.ReceiptIdentity, "receipt identity");
                Assert.AreEqual(Generation, receipt.ReceiptIdentity.Generation, "receipt generation");
                Assert.AreEqual(owner, receipt.ReceiptIdentity.OwnerId, "receipt identity owner");
                Assert.Greater(receipt.ActionId, 0, "receipt action id");
                StringAssert.Contains(":" + checkpoint.ProfileId + "@" + checkpoint.ProfileRevision + ":", receipt.ObservationIdentity);
                StringAssert.Contains(":" + owner + ":" + Generation + ":", receipt.ObservationIdentity);
                StringAssert.EndsWith(":" + Seed, receipt.ObservationIdentity);
                if (receipt.Status == PlayableAiDeliveryStatus.Applied)
                {
                    Assert.Greater(receipt.CommandSequence, 0, "applied command sequence");
                    Assert.Greater(receipt.ApplicationTick, 0, "applied tick");
                }
            }
            var actionLimit = (int)profile.DifficultyValue(checkpoint.Difficulty, "actionsPerDecision");
            foreach (var decision in checkpoint.Records.Where(r => r.Status == PlayableAiDeliveryStatus.Scheduled).GroupBy(r => r.ReceiptIdentity.DecisionOrdinal))
            {
                Assert.LessOrEqual(decision.Count(), actionLimit, "bounded scheduled actions per decision");
                Assert.AreEqual(decision.Count(), decision.Select(r => r.ReceiptIdentity.ActionOrdinal).Distinct().Count(), "unique action ordinals");
            }
        }

        [Test]
        public void UnchangedOrdinaryRuntimeCapturesOwnerReceiptLifecycleToFactoryOrOpeningBoundary()
        {
            var root = Root();
            var output = Path.Combine(root, "docs/native-ai/q4/owner-receipt-lifecycle-20261013");
            Directory.CreateDirectory(output);
            var tracePath = Path.Combine(output, "ordinary-runtime-trace-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl");

            var wall = Stopwatch.StartNew();
            var totalWatchdog = TimeSpan.FromSeconds(HostWatchdogSeconds);
            var noProgressWatchdog = TimeSpan.FromSeconds(NoProgressWatchdogSeconds);
            var originalBoundary = TimeSpan.FromSeconds(OriginalWallBoundarySeconds);
            var lastAdvance = TimeSpan.Zero;
            long lastTick = -1, lastDecision = -1, openingStartedTick = -1, openingDeadlineTick = -1;
            long firstAnyAppliedTick = -1, sevenSecondBoundaryTick = -1, firstFactoryScheduledDueTick = -1, firstFactoryAppliedTick = -1;
            long firstRefineryAppliedTick = -1, firstDecisionAtDeadline = -1;
            BuildAtCandidateWitness factoryCandidate = null, refineryCandidate = null;
            var buildAtCandidates = new System.Collections.Generic.Dictionary<string, BuildAtCandidateWitness>(StringComparer.Ordinal);
            bool reachedSevenSecondBoundary = false, firstTickSeen = false;
            PlayableCommandReceipt[] externalReceiptsAtEnd = Array.Empty<PlayableCommandReceipt>();
            string stopReason = null, hostFailure = null;

            using (var runtime = new PlayableRuntime(PlayableProfile.Default, Generation, Seed))
            using (var routes = new UnityHostRouteService())
            using (var writer = new StreamWriter(tracePath, false))
            {
                void Write(string type, object data) => writer.WriteLine(JsonConvert.SerializeObject(new { Type = type, WallSeconds = wall.Elapsed.TotalSeconds, Data = data }));

                var profile = runtime.NativeAiProfile;
                var actualDifficulty = runtime.AiCheckpoint.Difficulty;
                var deadlineSeconds = profile.Value("opening.deadlineSeconds");
                var decisionCadenceTicks = AiProfile.SecondsToTicks(profile.DifficultyValue(actualDifficulty, "decisionSeconds"), 30);
                var deadlineTicks = AiProfile.SecondsToTicks(deadlineSeconds, 30);
                Write("run-start", new
                {
                    Generation, Seed, Owner, SourceIdentity = PlayableAiOpeningComposition.SourceIdentity,
                    Profile = PlayableProfile.Default.ProfileId, ProfileRevision = PlayableProfile.Default.Revision,
                    AiProfile = profile.Id, AiProfileRevision = profile.Revision, AiProfileHash = profile.Hash,
                    Difficulty = actualDifficulty.ToString(), DecisionCadenceTicks = decisionCadenceTicks,
                    Speed = 1, HostWatchdogSeconds, NoProgressWatchdogSeconds, OriginalBoundarySeconds = OriginalWallBoundarySeconds,
                    OpeningDeadlineSeconds = deadlineSeconds, OpeningDeadlineTicks = deadlineTicks,
                    RuntimeType = runtime.GetType().FullName, RouteServiceType = routes.GetType().FullName,
                    ExternalInput = "none admitted; terminal external receipt backlog is captured using the existing isolation assertion"
                });

                while (true)
                {
                    if (wall.Elapsed >= totalWatchdog) { hostFailure = "absolute host watchdog expired"; stopReason = "watchdog"; break; }
                    if (firstTickSeen && wall.Elapsed - lastAdvance >= noProgressWatchdog)
                    { hostFailure = "runtime tick stopped advancing after first advancement"; stopReason = "no-progress-watchdog"; break; }

                    routes.Service(runtime, PlayableRuntime.MaxOutstandingCommands);
                    var snapshot = runtime.Latest;
                    if (snapshot != null && snapshot.Tick > lastTick)
                    {
                        lastTick = snapshot.Tick;
                        lastAdvance = wall.Elapsed;
                        firstTickSeen = true;
                    }

                    var checkpoint = runtime.AiCheckpoint;
                    var enemy = EnemyCheckpoint(runtime);
                    ValidateBinding(checkpoint, Owner, runtime.NativeAiProfile);
                    ValidateBinding(enemy, "enemy-1", runtime.NativeAiProfile);
                    if (snapshot != null) Assert.Zero(snapshot.Buildings.Count(b => b.Owner == PlayableOwner.Enemy && b.PrivateState != null), "enemy private state must not enter player publication");

                    if (wall.Elapsed >= originalBoundary && !reachedSevenSecondBoundary)
                    {
                        reachedSevenSecondBoundary = true;
                        sevenSecondBoundaryTick = snapshot?.Tick ?? -1;
                        var boundaryOwnFactory = snapshot?.Buildings.Any(b => b.Owner == PlayableOwner.Player && b.Kind == PlayableBuildingKind.Factory) == true;
                        var boundaryEconomyBuildApplied = checkpoint.Records.Any(r => r.Policy == "economy" && r.Kind == PlayableCommandKind.BuildAt && r.Status == PlayableAiDeliveryStatus.Applied);
                        Write("original-7s-wall-boundary", new
                        {
                            BoundarySeconds = OriginalWallBoundarySeconds,
                            Snapshot = snapshot == null ? null : BuildingRow(snapshot),
                            Player = OwnerRow(checkpoint), Enemy = OwnerRow(enemy),
                            FirstAnyAppliedReceipt = checkpoint.Records.FirstOrDefault(r => r.Status == PlayableAiDeliveryStatus.Applied),
                            EconomyBuildAtApplied = boundaryEconomyBuildApplied, OwnFactoryExists = boundaryOwnFactory,
                            OwnFactoryRows = snapshot?.Buildings.Where(b => b.Owner == PlayableOwner.Player && b.Kind == PlayableBuildingKind.Factory)
                                .Select(b => new { b.Id, b.SiteId, b.SlotId, b.Health, b.Progress, Phase = b.Phase.ToString() }).ToArray(),
                            Credits = snapshot?.Credits, SettledIncome = snapshot?.SettledIncome
                        });
                    }

                    var observedApplied = checkpoint.Records.FirstOrDefault(r => r.Status == PlayableAiDeliveryStatus.Applied);
                    if (observedApplied != null && firstAnyAppliedTick < 0)
                    {
                        firstAnyAppliedTick = observedApplied.ApplicationTick;
                        Write("first-any-applied", new { Snapshot = snapshot == null ? null : BuildingRow(snapshot), Receipt = observedApplied,
                            Player = OwnerRow(checkpoint), Enemy = OwnerRow(enemy) });
                    }

                    if (checkpoint.DecisionTick != lastDecision)
                    {
                        lastDecision = checkpoint.DecisionTick;
                        var execution = checkpoint.OpeningExecution;
                        if (execution != null && execution.StartedTick >= 0 && openingDeadlineTick < 0)
                        {
                            openingStartedTick = execution.StartedTick;
                            openingDeadlineTick = openingStartedTick + deadlineTicks;
                            Write("opening-started", new { OpeningStartedTick = openingStartedTick, OpeningDeadlineSeconds = deadlineSeconds,
                                OpeningDeadlineTicks = deadlineTicks, OpeningDeadlineTick = openingDeadlineTick,
                                DecisionCadenceTicks = decisionCadenceTicks, Opening = checkpoint.Opening?.Opening.ToString() });
                        }

                        Write("ordinary-decision", new
                        {
                            Snapshot = snapshot == null ? null : BuildingRow(snapshot),
                            Player = OwnerRow(checkpoint), Enemy = OwnerRow(enemy),
                            OpeningDeadlineTick = openingDeadlineTick,
                            DecisionCadenceTicks = decisionCadenceTicks
                        });

                        var economyDecision = checkpoint.EconomyDecision;
                        if (economyDecision != null)
                        {
                            var selectedBuilds = economyDecision.Candidates.Where(c => c.Policy == "economy" && c.Kind == PlayableCommandKind.BuildAt && c.Selected).ToArray();
                            Assert.LessOrEqual(selectedBuilds.Length, 1, "at most one selected economy BuildAt candidate per ordinary decision");
                            foreach (var selectedBuild in selectedBuilds)
                            {
                                var scheduled = checkpoint.Records.FirstOrDefault(r => r.Policy == selectedBuild.Policy && r.Kind == selectedBuild.Kind &&
                                    r.Status == PlayableAiDeliveryStatus.Scheduled && r.ReceiptIdentity.DecisionOrdinal == economyDecision.Ordinal &&
                                    r.ObservationIdentity == economyDecision.ObservationIdentity);
                                if (scheduled == null) continue;
                                var identity = scheduled.ReceiptIdentity.Id;
                                if (!buildAtCandidates.ContainsKey(identity))
                                {
                                    var candidate = new BuildAtCandidateWitness
                                    {
                                        DecisionOrdinal = economyDecision.Ordinal, ObservationIdentity = economyDecision.ObservationIdentity,
                                        ReceiptIdentity = identity, ActionId = scheduled.ActionId, SiteId = selectedBuild.SiteId,
                                        SlotId = selectedBuild.SlotId, BuildingKind = selectedBuild.BuildingKind, DueTick = scheduled.DueTick
                                    };
                                    buildAtCandidates.Add(identity, candidate);
                                    if (candidate.BuildingKind == PlayableBuildingKind.Factory && factoryCandidate == null)
                                    {
                                        factoryCandidate = candidate;
                                        firstFactoryScheduledDueTick = scheduled.DueTick;
                                        Write("first-factory-scheduled", new { Candidate = CandidateRow(candidate), EconomyDecision = economyDecision, Receipt = scheduled,
                                            Snapshot = snapshot == null ? null : BuildingRow(snapshot) });
                                    }
                                    if (candidate.BuildingKind == PlayableBuildingKind.Refinery && refineryCandidate == null)
                                    {
                                        refineryCandidate = candidate;
                                        Write("first-refinery-scheduled", new { Candidate = CandidateRow(candidate), EconomyDecision = economyDecision, Receipt = scheduled,
                                            Snapshot = snapshot == null ? null : BuildingRow(snapshot) });
                                    }
                                }
                            }
                        }
                        var ownView = snapshot?.Buildings.Where(b => b.Owner == PlayableOwner.Player).ToArray() ?? Array.Empty<PlayableBuildingSnapshot>();
                        var readyRefineries = ownView.Count(b => b.Kind == PlayableBuildingKind.Refinery && b.Health > 0 && b.Phase == ConstructionPhase.Ready && b.PrivateState?.Lifecycle?.Selling != true);
                        var expectedIncome = checkpoint.Opening?.Opening == PlayableAiOpening.GreedySafe ? (int)profile.Value("opening.greedyRefineries") : 1;
                        if (readyRefineries >= expectedIncome && checkpoint.OpeningExecution?.Completed?.Contains("income") == true)
                            Write("income-prerequisites-ready", new { Expected = expectedIncome, ReadyRefineries = readyRefineries, Buildings = BuildingRow(snapshot), OpeningExecution = checkpoint.OpeningExecution });

                        if (openingDeadlineTick >= 0 && checkpoint.DecisionTick >= openingDeadlineTick && firstDecisionAtDeadline < 0)
                        {
                            firstDecisionAtDeadline = checkpoint.DecisionTick;
                            Write("first-processed-deadline-decision", new { OpeningDeadlineTick = openingDeadlineTick, Player = OwnerRow(checkpoint), Snapshot = snapshot == null ? null : BuildingRow(snapshot) });
                        }
                    }

                    foreach (var applied in checkpoint.Records.Where(r => r.Policy == "economy" && r.Kind == PlayableCommandKind.BuildAt && r.Status == PlayableAiDeliveryStatus.Applied))
                    {
                        if (applied.ReceiptIdentity == null || !buildAtCandidates.TryGetValue(applied.ReceiptIdentity.Id, out var candidate) ||
                            applied.ActionId != candidate.ActionId || applied.ObservationIdentity != candidate.ObservationIdentity) continue;
                        var worldBuilding = snapshot?.Buildings.FirstOrDefault(b => b.Owner == PlayableOwner.Player && b.Kind == candidate.BuildingKind &&
                            b.SiteId == candidate.SiteId && b.SlotId == candidate.SlotId);
                        if (worldBuilding == null || snapshot.Tick < applied.ApplicationTick) continue;
                        if (candidate.BuildingKind == PlayableBuildingKind.Refinery && firstRefineryAppliedTick < 0)
                        {
                            firstRefineryAppliedTick = applied.ApplicationTick;
                            Write("first-refinery-applied", new { Candidate = CandidateRow(candidate), Receipt = applied, Snapshot = BuildingRow(snapshot),
                                Building = new { worldBuilding.Id, worldBuilding.SiteId, worldBuilding.SlotId, worldBuilding.Health,
                                    worldBuilding.Progress, Phase = worldBuilding.Phase.ToString() } });
                        }
                        if (candidate.BuildingKind == PlayableBuildingKind.Factory && firstFactoryAppliedTick < 0)
                        {
                            firstFactoryAppliedTick = applied.ApplicationTick;
                            Write("first-factory-applied", new { Candidate = CandidateRow(candidate), Receipt = applied, Snapshot = BuildingRow(snapshot),
                                Building = new { worldBuilding.Id, worldBuilding.SiteId, worldBuilding.SlotId, worldBuilding.Health,
                                    worldBuilding.Progress, Phase = worldBuilding.Phase.ToString() } });
                        }
                    }

                    var phase = checkpoint.OpeningExecution?.Phase;
                    if (firstFactoryAppliedTick >= 0)
                    { stopReason = "own Factory BuildAt Applied and actual own Factory observed"; break; }
                    if (phase == PlayableAiOpeningPhase.Aborted || phase == PlayableAiOpeningPhase.Complete)
                    { stopReason = "opening terminal state " + phase; break; }
                    if (firstDecisionAtDeadline >= 0)
                    { stopReason = "first ordinary decision at existing opening deadline"; break; }
                    if (snapshot != null && snapshot.Outcome != PlayableMatchOutcome.Playing)
                    { stopReason = "match terminal state " + snapshot.Outcome; break; }
                    if (snapshot?.Status == RuntimeStatus.Failed)
                    { hostFailure = "runtime published Failed status: " + snapshot.Failure; stopReason = "runtime-failure"; break; }

                    Thread.Sleep(20);
                }

                var finalSnapshot = runtime.Latest;
                var finalPlayer = runtime.AiCheckpoint;
                var finalEnemy = EnemyCheckpoint(runtime);
                externalReceiptsAtEnd = runtime.DrainReceipts().ToArray();
                Write("run-end", new
                {
                    StopReason = stopReason, HostFailure = hostFailure,
                    FinalSnapshot = finalSnapshot == null ? null : BuildingRow(finalSnapshot),
                    FinalPlayer = OwnerRow(finalPlayer), FinalEnemy = OwnerRow(finalEnemy),
                    OpeningStartedTick = openingStartedTick, OpeningDeadlineTick = openingDeadlineTick,
                    FirstProcessedDeadlineDecision = firstDecisionAtDeadline,
                    FirstAnyAppliedTick = firstAnyAppliedTick, SevenSecondBoundaryTick = sevenSecondBoundaryTick,
                    FirstRefineryAppliedTick = firstRefineryAppliedTick,
                    FirstFactoryScheduledDueTick = firstFactoryScheduledDueTick, FirstFactoryAppliedTick = firstFactoryAppliedTick,
                    FirstFactoryCandidate = CandidateRow(factoryCandidate), FirstRefineryCandidate = CandidateRow(refineryCandidate),
                    ReadyRefineries = finalSnapshot?.Buildings.Count(b => b.Owner == PlayableOwner.Player && b.Kind == PlayableBuildingKind.Refinery && b.Health > 0 && b.Phase == ConstructionPhase.Ready),
                    ActualFactory = finalSnapshot?.Buildings.Where(b => b.Owner == PlayableOwner.Player && b.Kind == PlayableBuildingKind.Factory).Select(b => new { b.Id, b.SiteId, b.SlotId, b.Health, b.Progress, Phase = b.Phase.ToString() }).ToArray(),
                    FactoryAbsentAtDeadline = firstDecisionAtDeadline >= 0 && !(finalSnapshot?.Buildings.Any(b => b.Owner == PlayableOwner.Player && b.Kind == PlayableBuildingKind.Factory) == true),
                    Outcome = finalSnapshot?.Outcome.ToString(), RuntimeStatus = finalSnapshot?.Status.ToString(),
                    ExternalReceiptsAtEnd = externalReceiptsAtEnd,
                    EnemyPrivateStateLeakCount = finalSnapshot?.Buildings.Count(b => b.Owner == PlayableOwner.Enemy && b.PrivateState != null),
                    WallSeconds = wall.Elapsed.TotalSeconds
                });
                writer.Flush();
            }

            Assert.True(reachedSevenSecondBoundary, "diagnostic failed before recording the unchanged original 7 second wall boundary; trace=" + tracePath);
            Assert.NotNull(stopReason, "diagnostic must record its actual lifecycle boundary");
            Assert.IsNull(hostFailure, hostFailure + "; last tick=" + lastTick + "; trace=" + tracePath);
            Assert.GreaterOrEqual(sevenSecondBoundaryTick, 0, "recorded exact original 7 second wall boundary tick");
            Assert.GreaterOrEqual(firstAnyAppliedTick, 0, "first any-policy Applied receipt captured");
            Assert.IsEmpty(externalReceiptsAtEnd, "AI receipts must not alter external-input backlog accounting");
            Assert.Pass("diagnostic trace and actual stop boundary captured; Factory acceptance is not implied. " + stopReason + "; trace=" + tracePath);
        }
    }
}

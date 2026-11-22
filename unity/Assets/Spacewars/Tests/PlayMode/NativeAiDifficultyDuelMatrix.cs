using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class NativeAiDifficultyDuelMatrix
{
    private const int Speed = 4;
    private const long AdjudicationStart = 6 * 60 * 30;
    private const long Horizon = 20 * 60 * 30;
    private const double StallLimitSeconds = 180;
    private const int ConcurrentMatches = 3;
    private const BindingFlags StaticInternal = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags InstanceInternal = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly (string map, int seed)[] Scenarios =
    {
        ("crossings", 7), ("dock", 19), ("moss", 83), ("crossings", 41), ("dock", 117)
    };

    [UnityTest, Timeout(172800000)]
    public IEnumerator PlayEveryPairWithSwappedStarts()
    {
        string output = Environment.GetEnvironmentVariable("SPACEWARS_AI_DUEL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            Assert.Ignore("Set SPACEWARS_AI_DUEL_OUTPUT to run the opt-in ordinary-match matrix.");
        Directory.CreateDirectory(output);
        Assert.That(Directory.GetFileSystemEntries(output), Is.Empty, "Use a fresh output directory for each matrix run.");
        string pairFilter = Environment.GetEnvironmentVariable("SPACEWARS_AI_DUEL_PAIR");
        var levels = (AiDifficulty[])Enum.GetValues(typeof(AiDifficulty));
        var pending = new Queue<(AiDifficulty lower, AiDifficulty higher, string map, int seed, bool swap)>(
            from scenario in Scenarios
            from higher in levels
            from lower in levels
            where (int)lower < (int)higher &&
                (string.IsNullOrEmpty(pairFilter) || pairFilter == $"{lower}-{higher}")
            from swap in new[] { false, true }
            orderby lower != AiDifficulty.Veteran || higher != AiDifficulty.Expert
            select (lower, higher, scenario.map, scenario.seed, swap));

        Assert.That(pending.Count, Is.EqualTo(string.IsNullOrEmpty(pairFilter) ? 100 : 10));
        var normal = (PlayableProfile)typeof(PlayableBootstrap).GetMethod("LoadNormalProfile", StaticInternal).Invoke(null, null);
        var store = NativeBalanceStore.ForApplication(normal, Path.Combine(output, "balance.json"));
        Assert.AreEqual(NativeBalanceStore.DefaultId, store.SelectedId);
        Assert.IsNull(store.ReleaseProblem);
        var releaseAi = store.ActiveAiProfile;
        var controlledAi = AggressiveExplorersProfile(releaseAi);
        File.WriteAllText(Path.Combine(output, "ai-profile.json"), JsonConvert.SerializeObject(new
        {
            ReleaseId = releaseAi.Id, ReleaseRevision = releaseAi.Revision, ReleaseHash = releaseAi.Hash,
            ControlledId = controlledAi.Id, ControlledRevision = controlledAi.Revision, ControlledHash = controlledAi.Hash
        }, Formatting.Indented));
        var running = new List<Duel>();
        var completed = new List<Duel>();
        string journalSession = null;
        long generation = 0;
        try
        {
            TestMatchLogSession.StartDefault(Path.Combine(output, "TestLogs"));
            journalSession = TestMatchLogSession.CurrentDirectory;
            Assert.IsNotNull(journalSession);
            while (pending.Count > 0 || running.Count > 0)
            {
                while (pending.Count > 0 && running.Count < ConcurrentMatches)
                {
                    var scenario = pending.Dequeue();
                    var duel = new Duel(scenario.lower, scenario.higher, scenario.map, scenario.seed, scenario.swap, ++generation, PlayableAiOpening.Safe);
                    running.Add(duel);
                    duel.Start(store, controlledAi);
                }
                for (int index = running.Count - 1; index >= 0; index--)
                {
                    var duel = running[index];
                    if (!duel.Update()) continue;
                    duel.Dispose();
                    completed.Add(duel);
                    SaveResult(output, duel.Result);
                    Debug.Log($"AI_DUEL_END {duel.Result.Id} tick={duel.Result.Tick} winner={duel.Result.WinnerTeam} status={duel.Result.Status}");
                    running.RemoveAt(index);
                }
                yield return null;
            }
        }
        finally
        {
            foreach (var duel in running) duel.Dispose();
            TestMatchLogSession.Shutdown();
        }

        foreach (var duel in completed)
        {
            duel.VerifyJournal(journalSession);
            SaveResult(output, duel.Result);
        }
        var summary = completed.Select(duel => duel.Result).GroupBy(r => (r.Lower, r.Higher)).Select(pair => new
        {
            pair.Key.Lower, pair.Key.Higher, Matches = pair.Count(),
            StrongerWins = pair.Count(r => r.WinnerTeam == r.HigherTeam),
            WeakerWins = pair.Count(r => r.WinnerTeam.HasValue && r.WinnerTeam != r.HigherTeam),
            NaturalWins = pair.Count(r => r.Status == "finished"),
            AdjudicatedWins = pair.Count(r => r.Status == "adjudicated"),
            Unresolved = pair.Count(r => r.Status == "horizon"),
            Failed = pair.Count(r => r.Failure != null)
        }).ToArray();
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonConvert.SerializeObject(summary, Formatting.Indented));
        var sessionEnd = JObject.Parse(File.ReadAllText(Path.Combine(journalSession, "session-end.json")));
        Assert.IsFalse((bool)sessionEnd["RecorderFailure"], "The match recorder reported an error.");
        Assert.That(summary, Has.Length.EqualTo(string.IsNullOrEmpty(pairFilter) ? 10 : 1));
        Assert.That(summary.Where(pair => pair.Matches != 10 || pair.Failed != 0 || pair.StrongerWins < 7), Is.Empty,
            "Each pair requires at least seven stronger-side wins in its ten scheduled games, counting authoritative results and the six-minute advantage rule.");
    }

    [UnityTest, Timeout(5400000)]
    public IEnumerator VeteranExpertBridgeDuelReachesAResultOrHorizon()
    {
        string output = Environment.GetEnvironmentVariable("SPACEWARS_AI_DUEL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            Assert.Ignore("Set SPACEWARS_AI_DUEL_OUTPUT to run the ordinary bridge duel.");
        Directory.CreateDirectory(output);
        var normal = (PlayableProfile)typeof(PlayableBootstrap).GetMethod("LoadNormalProfile", StaticInternal).Invoke(null, null);
        var store = NativeBalanceStore.ForApplication(normal, Path.Combine(output, "balance.json"));
        Assert.AreEqual(NativeBalanceStore.DefaultId, store.SelectedId);
        Assert.IsNull(store.ReleaseProblem);
        var duel = new Duel(AiDifficulty.Veteran, AiDifficulty.Expert, "crossings", 7, false, 11);
        try
        {
            TestMatchLogSession.StartDefault(Path.Combine(output, "TestLogs"));
            Assert.IsNotNull(TestMatchLogSession.CurrentDirectory);
            duel.Start(store);
            while (!duel.Update()) yield return null;
        }
        finally
        {
            duel.Dispose();
            TestMatchLogSession.Shutdown();
        }
        var result = duel.Result;
        File.WriteAllText(Path.Combine(output, result.Id + ".json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        Assert.IsNull(result.Failure);
        Assert.That(result.Status, Is.EqualTo("finished").Or.EqualTo("adjudicated").Or.EqualTo("horizon"));
    }

    [UnityTest, Timeout(5400000)]
    public IEnumerator VeteranExpertNullDock19ABRepairReplay()
    {
        string output = Environment.GetEnvironmentVariable("SPACEWARS_AI_DUEL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            Assert.Ignore("Set SPACEWARS_AI_DUEL_OUTPUT to run the opt-in Null Dock repair replay.");
        Directory.CreateDirectory(output);
        Assert.That(Directory.GetFileSystemEntries(output), Is.Empty, "Use a fresh output directory.");
        var normal = (PlayableProfile)typeof(PlayableBootstrap).GetMethod("LoadNormalProfile", StaticInternal).Invoke(null, null);
        var store = NativeBalanceStore.ForApplication(normal, Path.Combine(output, "balance.json"));
        Assert.AreEqual(NativeBalanceStore.DefaultId, store.SelectedId);
        Assert.IsNull(store.ReleaseProblem);
        var duel = new Duel(AiDifficulty.Veteran, AiDifficulty.Expert, "dock", 19, false, 3, PlayableAiOpening.Safe);
        string session;
        try
        {
            TestMatchLogSession.StartDefault(Path.Combine(output, "TestLogs"));
            session = TestMatchLogSession.CurrentDirectory;
            Assert.IsNotNull(session);
            duel.Start(store, AggressiveExplorersProfile(store.ActiveAiProfile));
            while (!duel.Update()) yield return null;
        }
        finally
        {
            duel.Dispose();
            TestMatchLogSession.Shutdown();
        }
        duel.VerifyJournal(session);
        SaveResult(output, duel.Result);
        Assert.IsNull(duel.Result.Failure);
        Assert.That(duel.Result.Status, Is.EqualTo("finished").Or.EqualTo("adjudicated").Or.EqualTo("horizon"));
        Assert.AreEqual(1, duel.Result.Home1);
        Assert.AreEqual(2, duel.Result.Home2);
        var sessionEnd = JObject.Parse(File.ReadAllText(Path.Combine(session, "session-end.json")));
        Assert.IsFalse((bool)sessionEnd["RecorderFailure"], "The match recorder reported an error.");
    }

    private static AiProfile AggressiveExplorersProfile(AiProfile release)
    {
        var controls = new Dictionary<string, double>
        {
            ["personality.defensive"] = 0,
            ["personality.aggressive"] = 100,
            ["personality.harassing"] = 0,
            ["personality.maximumChanges"] = 0,
            ["groundStay"] = 1,
            ["ground.balanced"] = 0,
            ["ground.tanks"] = 0,
            ["ground.heavy"] = 0,
            ["ground.explorers"] = 100,
            ["adaptation.probability"] = 0
        };
        var data = release.CopyData();
        data.id += "-duel-aggressive-explorers";
        foreach (var control in controls)
            data.fields.Single(field => field.path == "openingsMidgame." + control.Key).value = control.Value;
        return new AiProfile(data);
    }

    private static void SaveResult(string output, DuelResult result)
    {
        File.WriteAllText(Path.Combine(output, result.Id + ".json"), JsonConvert.SerializeObject(result, Formatting.Indented));
    }

    private static int? AdvantageWinner(double firstIncome, int firstPopulation, double secondIncome, int secondPopulation)
    {
        bool Doubled(double leader, double opponent) => leader > opponent && leader >= 2 * opponent;
        bool Leads(double income, int population, double otherIncome, int otherPopulation) =>
            Doubled(income, otherIncome) && population >= otherPopulation ||
            Doubled(population, otherPopulation) && income >= otherIncome;
        return Leads(firstIncome, firstPopulation, secondIncome, secondPopulation) ? 1 :
            Leads(secondIncome, secondPopulation, firstIncome, firstPopulation) ? 2 : (int?)null;
    }

    [TestCase(20, 15, 10, 15, 1)]
    [TestCase(20, 14, 10, 15, 0)]
    [TestCase(10, 24, 10, 12, 1)]
    [TestCase(9, 24, 10, 12, 0)]
    [TestCase(10, 15, 20, 15, 2)]
    [TestCase(0, 0, 0, 0, 0)]
    public void AdjudicationRequiresARealTwofoldLeadWithoutASecondDeficit(
        double firstIncome, int firstPopulation, double secondIncome, int secondPopulation, int expected)
    {
        Assert.AreEqual(expected, AdvantageWinner(firstIncome, firstPopulation, secondIncome, secondPopulation) ?? 0);
    }

    private sealed class DuelResult
    {
        public string Id, Lower, Higher, Map, Profile, AiHash, Status, Failure;
        public string Opening1, Opening2, Personality1, Personality2, Doctrine1, Doctrine2, JournalDirectory;
        public long DoctrineTick1, DoctrineTick2;
        public int AppliedMicro1, AppliedMicro2;
        public int Seed, HigherTeam, AiRevision, Home1, Home2, Population1, Population2;
        public long Tick;
        public double Credits1, Credits2, Income1, Income2, IncomeRate1, IncomeRate2;
        public int? WinnerTeam;
    }

    private sealed class Duel : IDisposable
    {
        private readonly AiDifficulty lower, higher;
        private readonly string map;
        private readonly bool swap;
        private readonly long generation;
        private readonly PlayableAiOpening? forcedOpening;
        private PlayableRuntime runtime;
        private PlayableProfile profile;
        private UnityHostRouteService routes;
        private double deadline;
        public DuelResult Result { get; }

        public Duel(AiDifficulty lower, AiDifficulty higher, string map, int seed, bool swap, long generation,
            PlayableAiOpening? forcedOpening = null)
        {
            this.lower = lower;
            this.higher = higher;
            this.map = map;
            this.swap = swap;
            this.generation = generation;
            this.forcedOpening = forcedOpening;
            Result = new DuelResult
            {
                Id = $"{lower}-{higher}-{map}-{seed}-{(swap ? "BA" : "AB")}",
                Lower = lower.ToString(), Higher = higher.ToString(),
                Seed = seed, HigherTeam = swap ? 1 : 2
            };
        }

        public void Start(NativeBalanceStore store, AiProfile controlledAi = null)
        {
            try
            {
                var lobby = new NativeLobbyConfiguration
                {
                    NullDock = map == "dock", MossHighland = map == "moss",
                    HasExplicitSeed = true, ExplicitSeed = Result.Seed,
                    Participants = new List<NativeLobbyParticipant>
                    {
                        new NativeLobbyParticipant { Name = "ИИ", Team = 1, Color = 0, Difficulty = swap ? higher : lower },
                        new NativeLobbyParticipant { Name = "ИИ", Team = 2, Color = 1, Difficulty = swap ? lower : higher }
                    }
                };
                var compose = typeof(PlayableBootstrap).GetMethod("ComposeLobbyProfile", StaticInternal, null,
                    new[] { typeof(PlayableProfile), typeof(NativeLobbyConfiguration) }, null);
                profile = (PlayableProfile)compose.Invoke(null, new object[] { store.Active, lobby });
                string problem = lobby.Validate(profile, false, false);
                if (problem != null) throw new InvalidOperationException(problem);
                var ai = controlledAi ?? store.ActiveAiProfile;
                Result.Map = profile.AuthoredMap.Id;
                Result.Profile = store.ActiveIdentity;
                Result.AiRevision = ai.Revision;
                Result.AiHash = ai.Hash;
                runtime = PlayableRuntime.CreateLobbyMatch(profile, generation, lobby, startPaused: true, aiProfile: ai,
                    forcedAiOpening: forcedOpening);
                routes = new UnityHostRouteService();
                Result.Home1 = runtime.ParticipantView("player-1").HomeSiteId;
                Result.Home2 = runtime.ParticipantView("foundry-2").HomeSiteId;
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId, Speed);
                deadline = Time.realtimeSinceStartupAsDouble + StallLimitSeconds;
                runtime.RequestPause(false);
                Debug.Log($"AI_DUEL_START {Result.Id} profile={Result.Profile} map={Result.Map} homes={Result.Home1},{Result.Home2} opening={forcedOpening} ai={ai.Id}@{ai.Revision}");
            }
            catch (Exception error)
            {
                Result.Status = "setup-failure";
                Result.Failure = error.ToString();
            }
        }

        public bool Update()
        {
            if (Result.Status != null) return true;
            routes.Service(runtime, profile.NavigationRequestsPerFrame);
            var snapshot = runtime.Latest;
            if (snapshot.Tick > Result.Tick)
            {
                Result.Tick = snapshot.Tick;
                deadline = Time.realtimeSinceStartupAsDouble + StallLimitSeconds;
            }
            Result.Failure = snapshot.Failure ?? routes.LastProjectionFailure;
            if (Result.Failure != null) Result.Status = "runtime-failure";
            else if (runtime.Result?.WinnerTeam is int winner)
            {
                Result.Status = "finished";
                Result.WinnerTeam = winner;
            }
            else if (snapshot.Tick >= AdjudicationStart)
            {
                var frame = runtime.OfflineFrame;
                var firstView = frame.Views["player-1"];
                var secondView = frame.Views["foundry-2"];
                if (frame.Tick >= AdjudicationStart && frame.Tick <= Horizon)
                {
                    var winnerByAdvantage = AdvantageWinner(firstView.IncomePerSecond, firstView.Population.Living,
                        secondView.IncomePerSecond, secondView.Population.Living);
                    if (winnerByAdvantage.HasValue)
                    {
                        Result.Tick = firstView.Tick;
                        Result.Status = "adjudicated";
                        Result.WinnerTeam = winnerByAdvantage;
                        SaveReadout(firstView, secondView);
                        return true;
                    }
                }
            }
            if (Result.Status == null && snapshot.Tick >= Horizon) Result.Status = "horizon";
            if (Result.Status == null && (runtime.IsStopped || Time.realtimeSinceStartupAsDouble >= deadline))
            {
                Result.Status = "incomplete";
                Result.Failure = runtime.IsStopped ? "Runtime stopped before producing a winner or reaching the horizon."
                    : "Authority tick stopped advancing.";
            }
            if (Result.Status == null) return false;
            var terminalFrame = runtime.OfflineFrame;
            SaveReadout(terminalFrame.Views["player-1"], terminalFrame.Views["foundry-2"]);
            return true;
        }

        private void SaveReadout(PlayableSnapshot first, PlayableSnapshot second)
        {
            Result.Population1 = first.Population.Living;
            Result.Population2 = second.Population.Living;
            Result.Credits1 = first.ExactCredits;
            Result.Credits2 = second.ExactCredits;
            Result.Income1 = first.SettledIncome;
            Result.Income2 = second.SettledIncome;
            Result.IncomeRate1 = first.IncomePerSecond;
            Result.IncomeRate2 = second.IncomePerSecond;
        }

        public void VerifyJournal(string session)
        {
            if (!forcedOpening.HasValue) return;
            string expected = AiOpeningCatalog.For(forcedOpening.Value).Id;
            try
            {
                string directory = Directory.GetDirectories(session, $"match-{generation}-*").Single();
                Result.JournalDirectory = Path.Combine(Path.GetFileName(session), Path.GetFileName(directory));
                var owners = new Dictionary<string, JournalOwner>
                {
                    ["player-1"] = new JournalOwner(),
                    ["foundry-2"] = new JournalOwner()
                };
                foreach (string line in Directory.GetFiles(directory, "events-*.jsonl").OrderBy(path => path).SelectMany(File.ReadLines))
                {
                    if (line.StartsWith("{\"Type\":\"ai-checkpoint\"", StringComparison.Ordinal))
                    {
                        var checkpoint = JObject.Parse(line);
                        var owner = owners[(string)checkpoint["OwnerId"]];
                        owner.Opening = (string)checkpoint["OpeningExecution"]?["Template"];
                        if (owner.Opening != expected)
                            throw new InvalidDataException($"Expected the {expected} opening. Found {owner.Opening}.");
                        // The initial checkpoint precedes the first personality observation.
                        if ((long)checkpoint["DecisionTick"] <= 0) continue;
                        var doctrine = checkpoint["Doctrine"];
                        owner.Personality = (string)doctrine["Personality"];
                        if (owner.Personality != nameof(AiPersonality.Aggressive))
                            throw new InvalidDataException($"Expected Aggressive. Found {owner.Personality}.");
                        string strategy = (string)checkpoint["Strategy"]?["Strategy"];
                        if (strategy != null && strategy != nameof(PlayableAiMidgameStrategy.MassAssault))
                            throw new InvalidDataException($"Expected MassAssault with Aggressive personality. Found {strategy}.");
                        if (!(bool)doctrine["Chosen"]) continue;
                        owner.Doctrine = (string)doctrine["Doctrine"];
                        owner.DoctrineTick = (long)doctrine["ChosenTick"];
                        if (owner.Doctrine != nameof(AiDoctrine.Explorers) || (bool)doctrine["Mixed"])
                            throw new InvalidDataException($"Expected the ground Explorers doctrine. Found {owner.Doctrine}.");
                    }
                    else if (line.StartsWith("{\"Type\":\"ai-receipt\"", StringComparison.Ordinal) &&
                        line.Contains("\"Policy\":\"combat-micro\"") && line.Contains("\"Status\":\"Applied\""))
                    {
                        var receipt = JObject.Parse(line);
                        var record = receipt["Record"];
                        if ((long)record["ApplicationTick"] <= Result.Tick)
                            owners[(string)receipt["OwnerId"]].AppliedMicro.Add((string)record["ReceiptIdentity"]["Id"]);
                    }
                }
                var first = owners["player-1"];
                var second = owners["foundry-2"];
                Result.Opening1 = first.Opening;
                Result.Opening2 = second.Opening;
                Result.Personality1 = first.Personality;
                Result.Personality2 = second.Personality;
                Result.Doctrine1 = first.Doctrine;
                Result.Doctrine2 = second.Doctrine;
                Result.DoctrineTick1 = first.DoctrineTick;
                Result.DoctrineTick2 = second.DoctrineTick;
                Result.AppliedMicro1 = first.AppliedMicro.Count;
                Result.AppliedMicro2 = second.AppliedMicro.Count;
                if (first.Doctrine == null || second.Doctrine == null ||
                    first.DoctrineTick > Result.Tick || second.DoctrineTick > Result.Tick)
                    throw new InvalidDataException("Both owners must reach the Explorers doctrine by the scored end of the match.");
                var summary = JObject.Parse(File.ReadAllText(Path.Combine(directory, "summary.json")));
                if ((string)summary["Status"] != "complete" || (long)summary["Dropped"] != 0)
                    throw new InvalidDataException("The match journal is incomplete.");
            }
            catch (Exception error)
            {
                RecordFailure("journal-failure", error.ToString());
            }
        }

        private sealed class JournalOwner
        {
            public string Opening, Personality, Doctrine;
            public long DoctrineTick;
            public readonly HashSet<string> AppliedMicro = new HashSet<string>();
        }

        private void RecordFailure(string status, string failure)
        {
            if (Result.Failure == null)
            {
                Result.Status = status;
                Result.Failure = failure;
            }
            else
            {
                Result.Failure += Environment.NewLine + failure;
            }
        }

        public void Dispose()
        {
            if (runtime == null) return;
            var authority = (PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick", InstanceInternal).GetValue(runtime);
            var worker = routes == null ? null : (CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker", InstanceInternal).GetValue(routes);
            runtime.RequestStop();
            routes?.Dispose();
            runtime.Dispose();
            if (SpinWait.SpinUntil(() => runtime.IsStopped && (worker == null || worker.IsStopped) && authority.Domain.ObservedPathWorkerStopped, 10000))
            {
                authority.Domain.Navigation.ReleaseCommittedHostRoot();
                authority.Domain.DestroyOwnedNavigation();
            }
            else
            {
                RecordFailure("shutdown-failure", "Match-owned workers did not acknowledge stop.");
            }
            runtime = null;
        }
    }
}

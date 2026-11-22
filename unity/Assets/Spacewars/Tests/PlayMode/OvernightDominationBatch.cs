using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class OvernightDominationBatch
{
    private sealed class Batch
    {
        public string Revision { get; set; }
        public int Speed { get; set; }
        public int HorizonTicks { get; set; }
        public double StallSeconds { get; set; }
        public double MatchWallSeconds { get; set; }
        public string[] OpeningCatalog { get; set; }
        public Job[] Jobs { get; set; }
    }

    private sealed class Job
    {
        public int Id { get; set; }
        public string Map { get; set; }
        public int Seed { get; set; }
        public int[] Teams { get; set; }
        public AiDifficulty[] Difficulties { get; set; }
        public string[] Openings { get; set; }

        public NativeLobbyConfiguration Lobby() => new NativeLobbyConfiguration
        {
            Domination = true, DeafMirror = Map == "deaf-mirror",
            HasExplicitSeed = true,
            ExplicitSeed = Seed,
            NullDock = Map == "dock",
            BasaltCrown = Map == "basalt",
            MossHighland = Map == "moss",
            WhiteMeridian = Map == "meridian",
            Foundry = Map == "foundry",
            AntennaCity = Map == "city",
            CrimsonGrove = Map == "grove",
            AiTestMap = Map == "polygon",
            Participants = Teams.Select((team, slot) => new NativeLobbyParticipant
            {
                Name = "ИИ", Team = team, Color = slot, Difficulty = Difficulties[slot]
            }).ToList()
        };
    }

    private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
    {
        Converters = { new StringEnumConverter() }
    };

    private static void Publish(string path, object value)
    {
        File.WriteAllText(path + ".tmp", JsonConvert.SerializeObject(value, Json));
        File.Move(path + ".tmp", path);
    }

    [UnityTest, Timeout(int.MaxValue)]
    public IEnumerator RecordLobbyMatches()
    {
        string root = Environment.GetEnvironmentVariable("SPACEWARS_OVERNIGHT_BATCH");
        if (string.IsNullOrWhiteSpace(root)) Assert.Ignore("Run make overnight-domination to record this batch.");

        foreach (var type in new[] { LogType.Warning, LogType.Error, LogType.Exception, LogType.Assert })
            Application.SetStackTraceLogType(type, StackTraceLogType.Full);
        // Engine errors are retained in the journals and Editor log. The harness
        // handles a match failure without aborting the remaining cases.
        bool oldIgnore = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            var batch = JsonConvert.DeserializeObject<Batch>(File.ReadAllText(Path.Combine(root, "jobs.json")), Json);
            if (batch.Jobs.Any(job => job.Openings != null))
                CollectionAssert.AreEquivalent(AiOpeningCatalog.All.Select(definition => definition.Id), batch.OpeningCatalog,
                    "Update the overnight matrix when the active AI opening catalog changes.");
            const BindingFlags internalStatic = BindingFlags.Static | BindingFlags.NonPublic;
            var normal = (PlayableProfile)typeof(PlayableBootstrap).GetMethod("LoadNormalProfile", internalStatic).Invoke(null, null);
            var compose = typeof(PlayableBootstrap).GetMethod("ComposeLobbyProfile", internalStatic, null,
                new[] { typeof(PlayableProfile), typeof(NativeLobbyConfiguration) }, null);
            var store = NativeBalanceStore.ForApplication(normal, Path.Combine(root, "balance.json"));
            Assert.AreEqual(NativeBalanceStore.DefaultId, store.SelectedId);
            Assert.IsNull(store.ReleaseProblem);
            Assert.IsTrue(store.HasActive, store.Problem);

            var profiles = new Dictionary<string, PlayableProfile>();
            foreach (var job in batch.Jobs)
            {
                if (File.Exists(Path.Combine(root, "stop"))) break;
                string directory = Path.Combine(root, job.Id.ToString("D4"));
                Directory.CreateDirectory(directory);
                string heartbeat = Path.Combine(directory, "heartbeat");
                File.WriteAllText(heartbeat, "starting");
                using (var match = new Recording(directory, batch, job))
                {
                    match.Start(store, compose, profiles);
                    while (match.Step()) yield return null;
                    match.Stop();
                    double until = Time.realtimeSinceStartupAsDouble + 30;
                    while (!match.Stopped && Time.realtimeSinceStartupAsDouble < until) yield return null;
                    match.Finish();
                    if (!match.Stopped) break;
                }
                // The collector commits and archives one match before the next starts.
                while (!File.Exists(Path.Combine(directory, "collected")) &&
                       !File.Exists(Path.Combine(root, "stop"))) yield return null;
            }
        }
        finally
        {
            LogAssert.ignoreFailingMessages = oldIgnore;
        }
    }

    private sealed class Recording : IDisposable
    {
        private readonly string directory, heartbeat;
        private readonly Batch batch;
        private readonly Job job;
        private readonly StreamWriter perf;
        private PlayableRuntime runtime;
        private PlayableProfile profile;
        private UnityHostRouteService routes;
        private double started, lastSample, lastProgress;
        private long tick, sampleTick;
        private string outcome, failure, status;
        private int? winner;
        private MatchVictoryReason? reason;

        public Recording(string directory, Batch batch, Job job)
        {
            this.directory = directory;
            this.batch = batch;
            this.job = job;
            heartbeat = Path.Combine(directory, "heartbeat");
            perf = new StreamWriter(Path.Combine(directory, "performance.jsonl")) { AutoFlush = true };
        }

        public bool Stopped => runtime == null || runtime.IsStopped;

        public void Start(NativeBalanceStore store, MethodInfo compose, Dictionary<string, PlayableProfile> profiles)
        {
            try
            {
                TestMatchLogSession.StartDefault(Path.Combine(directory, "TestLogs"), compressEvents: true);
                if (TestMatchLogSession.CurrentDirectory == null) throw new InvalidOperationException("Match recorder unavailable.");
                var lobby = job.Lobby();
                if (!profiles.TryGetValue(job.Map, out profile))
                {
                    profile = (PlayableProfile)compose.Invoke(null, new object[] { store.Active, lobby });
                    profiles.Add(job.Map, profile);
                }
                string problem = lobby.ValidateLobby(profile, false, false);
                if (problem != null) throw new InvalidOperationException(problem);
                if (!profile.AuthoredMap.Sites(profile).Any(s => s.Kind == PlayableBuildingKind.RelayTower))
                    throw new InvalidOperationException("This map does not support Domination.");
                var ai = store.ActiveAiProfile;
                var seatOpenings = job.Openings?.Select(id => AiOpeningCatalog.All.Single(d => d.Id == id).Opening).ToArray();
                runtime = PlayableRuntime.CreateLobbyMatch(profile, job.Id, lobby, startPaused: true,
                    aiProfile: ai, forcedAiOpening: null, seatOpenings: seatOpenings);
                routes = new UnityHostRouteService();
                var slots = runtime.OfflineFrame.Views.Values.Select(v => new
                {
                    v.OwnerId, v.Owner, v.Team, v.HomeSiteId, RequestedOpening = job.Openings?[(int)v.Owner]
                });
                Publish(Path.Combine(directory, "identity.json"), new
                {
                    batch.Revision, Unity = Application.unityVersion, Application.platform, Map = profile.AuthoredMap.Id,
                    MapRevision = profile.AuthoredMap.Revision, Balance = store.ActiveIdentity,
                    profile.ProfileId, ProfileRevision = profile.Revision, TickRate = 30,
                    GameplayHash = AiMatchIdentity.GameplayDigest(profile),
                    Ai = new { ai.Id, ai.Revision, ai.Hash }, Owners = slots
                });
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId, batch.Speed);
                started = lastSample = lastProgress = Time.realtimeSinceStartupAsDouble;
                runtime.RequestPause(false);
            }
            catch (Exception error)
            {
                status = "setup-failure";
                failure = error.ToString();
                UnityEngine.Debug.LogException(error);
            }
        }

        public bool Step()
        {
            if (status != null) return false;
            try
            {
                routes.Service(runtime, profile.NavigationRequestsPerFrame);
                var snapshot = runtime.Latest;
                double now = Time.realtimeSinceStartupAsDouble;
                if (tick != snapshot.Tick)
                {
                    tick = snapshot.Tick;
                    lastProgress = now;
                }
                if (now - lastSample >= 1)
                {
                    var sample = runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId)?.Overview ?? snapshot;
                    perf.WriteLine(JsonConvert.SerializeObject(new
                    {
                        Tick = tick, WallSeconds = now - started, WindowSeconds = now - lastSample,
                        Tps = (tick - sampleTick) / (now - lastSample), TargetTps = runtime.TargetTicksPerSecond,
                        NoProgressSeconds = now - lastProgress, Metrics = snapshot.Metrics,
                        Units = runtime.DiagnosticGlobalUnits, Buildings = runtime.DiagnosticGlobalBuildings,
                        runtime.BarrierPolls, Domination = sample.Domination, RelayControl = sample.MapRelayControl
                    }, Json));
                    lastSample = now;
                    sampleTick = tick;
                    File.WriteAllText(heartbeat, tick.ToString());
                }

                failure = snapshot.Failure ?? routes.LastProjectionFailure;
                var result = runtime.Result;
                if (failure != null) status = "runtime-failure";
                else if (result != null)
                {
                    if (!result.Domination) throw new InvalidOperationException("Runtime ended in a different game mode.");
                    status = "finished";
                    tick = result.Tick;
                    winner = result.WinnerTeam;
                    reason = result.VictoryReason;
                }
                else if (snapshot.Outcome == PlayableMatchOutcome.Playing && snapshot.Tick > 0 && !snapshot.Domination.HasValue)
                    throw new InvalidOperationException("Domination state is missing from the running match.");
                else if (now - lastProgress >= batch.StallSeconds || runtime.IsStopped)
                {
                    status = "simulation-stalled";
                    failure = "Authority stopped publishing new ticks before the match ended.";
                }
                else if (tick >= batch.HorizonTicks) status = "horizon";
                else if (now - started >= batch.MatchWallSeconds) status = "wall-limit";
                outcome = snapshot.Outcome.ToString();
            }
            catch (Exception error)
            {
                status = "runtime-failure";
                failure = error.ToString();
                UnityEngine.Debug.LogException(error);
            }
            return status == null;
        }

        public void Stop() => runtime?.RequestStop();

        public void Finish()
        {
            if (!Stopped)
            {
                status = "shutdown-stalled";
                failure = failure == null ? "Authority did not stop within 30 seconds." :
                    failure + "\nAuthority did not stop within 30 seconds.";
            }
            else if (runtime?.Latest?.Failure is string terminalFailure)
            {
                failure = terminalFailure;
                status = "runtime-failure";
            }
            if (runtime?.JournalFailure is string recorderError)
                UnityEngine.Debug.LogError(recorderError);
            TestMatchLogSession.Shutdown();
            Publish(Path.Combine(directory, "result.json"), new
            {
                Status = status, Failure = failure, Outcome = outcome, Tick = tick, WinnerTeam = winner,
                VictoryReason = reason, WallSeconds = started == 0 ? 0 : Time.realtimeSinceStartupAsDouble - started,
                RestartEditor = !Stopped
            });
        }

        public void Dispose()
        {
            perf.Dispose();
            runtime?.Dispose();
            routes?.Dispose();
            TestMatchLogSession.Shutdown();
        }
    }
}

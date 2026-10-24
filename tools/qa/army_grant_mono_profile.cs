using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Reflection;
using Spacewars.Runtime;
using Spacewars.Simulation;

internal static class ArmyGrantMonoProfile {
    static int Main(string[] args) => args.Length == 1 && args[0] == "--clock-guard-self-test"
        ? ArmyInputMonoQualification.RunClockGuardSelfTest()
        : args.Length >= 2 && args[0] == "--report-preflight-only"
            ? ArmyInputMonoQualification.RunReportPreflight(args[1]) : ArmyInputMonoQualification.RunProfile(args);
}

internal static partial class ArmyInputMonoQualification {
    const BindingFlags CensusFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    // Same actual persistent-list projection as archived observer.cs:100,226.
    // Public Proposals deliberately hides partial assignments until Ready.
    static readonly FieldInfo CensusAssignments = typeof(NavigationArrivalWork).GetField("assignments", CensusFlags);
    static NavArrivalProposal[] PartialAssignments(NavigationArrivalWork work) {
        if (CensusAssignments == null) throw new MissingFieldException(typeof(NavigationArrivalWork).FullName, "assignments");
        var stored = CensusAssignments.GetValue(work);
        if (!(stored is IEnumerable)) throw new InvalidOperationException("Missing/non-enumerable actual assignments list");
        var rows = ((IEnumerable)stored).Cast<NavArrivalProposal>().ToArray();
        Require(rows.All(p => p != null), "null actual partial assignment");
        return rows;
    }
    static object CF(object o, string n) => o?.GetType().GetField(n, CensusFlags)?.GetValue(o) ?? o?.GetType().GetProperty(n, CensusFlags)?.GetValue(o);
    static int Count(object o) => o == null ? 0 : o is Array a ? a.Length : (int)CF(o, "Count");
    static bool Finite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
    static object Demand(NavigationArrivalWork w, out NavScheduledRequest request) {
        request = null;
        var scheduler = (LayeredNavigationScheduler)CF(w, "scheduler");
        foreach (var job in (IEnumerable)CF(scheduler, "jobs")) {
            var r = (NavScheduledRequest)CF(job, "Request"); var d = CF(job, "Demand");
            if (r.Subscription.EntityId == 14 && d != null) { request = r; return d; }
        }
        return null;
    }
    static Dictionary<string, object> Fields(object o, string[] names) => names.ToDictionary(n => n, n => CF(o, n));
    static string ExactHex(object cost) => string.Concat(Enumerable.Range(0, 34).Reverse().Select(i => ((ulong)CF(cost, "v" + i)).ToString("x16")));
    // Same complete field set and expressions as army_reconstruction_census.cs.
    // Only Double.IsFinite is expanded for the original Mono framework API.
    static object Fingerprint(object d, NavScheduledRequest q) {
        int chosen = (int)CF(d, "chosen"), candidate = (int)CF(d, "chosenCandidate");
        double best = (double)CF(d, "best"), minimum = (double)CF(d, "boundMinimum");
        var frontier = CF(d, "frontier"); var lazy = CF(d, "lazyStore");
        return new {
            phase = CF(d, "phase").ToString(), status = CF(d, "Status").ToString(),
            samples = CF(d, "SampleCount"), frontier = CF(d, "FrontierCount"), indexed = CF(d, "IndexedCount"),
            canBookkeeping = CF(d, "CanBookkeeping"), best = Finite(best) ? (double?)best : null, bestFinite = Finite(best),
            chosen, chosenCandidate = candidate, current = CF(d, "current"), direction = CF(d, "direction"),
            minCost = Count(frontier) > 0 ? (double?)CF(CF(frontier, "Min"), "Cost") : null,
            hasIncumbent = chosen >= 0 && candidate >= 0, incumbentMode = q.DemandQueryV5 ? "exact" : "double",
            exactState = new {
                q.DemandQueryV5, q.DemandQueryV6, q.LazyPartition, recipeMetric = q.MetricContract.ToString(),
                exactDistanceCount = Count(CF(d, "exactDistance")), exactFrontierCount = Count(CF(d, "exactFrontier")),
                exactBestHex = chosen >= 0 ? ExactHex(CF(d, "exactBest")) : null, exactIncumbent = chosen >= 0 && candidate >= 0,
                heuristicActive = CF(d, "HeuristicActive"), effectiveMetricContract = CF(d, "EffectiveMetricContract").ToString(),
                h = Fields(d, new[] { "hStage", "hCandidateCursor", "hNext", "hLast", "hEligible", "hContract" })
            },
            seam = new {
                seamCursor = CF(d, "seamCursor"), boundNode = CF(d, "boundNode"), boundCursor = CF(d, "boundCursor"), boundBusy = CF(d, "BoundBusy"),
                minimum = Finite(minimum) ? (double?)minimum : null, minimumFinite = Finite(minimum),
                boxCount = CF(d, "seamBoxes") == null ? (int?)null : Count(CF(d, "seamBoxes")),
                nodeCount = CF(d, "nodeBounds") == null ? (int?)null : Count(CF(d, "nodeBounds"))
            },
            lazy = new {
                classified = Count(CF(lazy, "Cells")), present = Count(CF(lazy, "Points")),
                fields = Fields(d, new[] { "lazyStage", "lazyLow", "lazyHigh", "lazyMinX", "lazyMaxX", "lazyMinZ", "lazyMaxZ", "lazyX", "lazyZ", "lazyMask", "lazyTo", "lazyTrace" })
            }
        };
    }
    static object TailState(object d) => d == null ? null : new {
        phase = CF(d, "phase").ToString(), chainCount = Count(CF(d, "chain")), traceNode = CF(d, "traceNode"),
        reconstruct = CF(d, "reconstruct"), reconstructLeg = CF(d, "reconstructLeg"), legsCount = Count(CF(d, "legs")),
        chosen = CF(d, "chosen"), chosenCandidate = CF(d, "chosenCandidate")
    };
    // Framework-only JSON output: public fields/properties match the previous
    // IncludeFields serializer. Enums retain its default integer representation.
    static object Plain(object x) {
        if (x == null || x is string || x is bool) return x;
        var t = x.GetType();
        if (t.IsEnum) return Convert.ToInt64(x, Inv);
        if (t.IsPrimitive || x is decimal) return x;
        if (x is IDictionary dict) {
            var result = new Dictionary<string, object>();
            foreach (DictionaryEntry row in dict) result.Add(Convert.ToString(row.Key, Inv), Plain(row.Value));
            return result;
        }
        if (x is IEnumerable list) return list.Cast<object>().Select(Plain).ToArray();
        var properties = new Dictionary<string, object>();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0)) properties.Add(p.Name, Plain(p.GetValue(x)));
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) properties.Add(f.Name, Plain(f.GetValue(x)));
        return properties;
    }
    static string Json(object value) {
        object x = Plain(value);
        if (x == null) return "null";
        if (x is string s) return "\"" + string.Concat(s.Select(c => c == '"' ? "\\\"" : c == '\\' ? "\\\\" : c < 32 ? "\\u" + ((int)c).ToString("x4") : c.ToString())) + "\"";
        if (x is bool b) return b ? "true" : "false";
        if (x is double d) return Finite(d) ? R(d) : Json(R(d));
        if (x is float f) return float.IsNaN(f) || float.IsInfinity(f) ? Json(f.ToString("R", Inv)) : f.ToString("R", Inv);
        if (x is IDictionary dict) {
            var entries = new List<string>();
            foreach (DictionaryEntry p in dict) entries.Add(Json(p.Key) + ":" + Json(p.Value));
            return "{" + string.Join(",", entries) + "}";
        }
        if (x is IEnumerable list) return "[" + string.Join(",", list.Cast<object>().Select(Json)) + "]";
        return Convert.ToString(x, Inv);
    }
    static void SaveCensus(string folder, string name, object value) => File.WriteAllText(Path.Combine(folder, name), Json(value) + "\n");
    static void Subset(object expected, object actual, string path = "fingerprint") {
        actual = Plain(actual);
        if (expected is Dictionary<string, object> dict) {
            Require(actual is Dictionary<string, object>, path + " expected object");
            var a = (Dictionary<string, object>)actual;
            foreach (var p in dict) {
                Require(a.ContainsKey(p.Key), path + " missing " + p.Key);
                Subset(p.Value, a[p.Key], path + "." + p.Key);
            }
            return;
        }
        // All observed numeric fingerprint leaves are integers; preserve every
        // expected key/type/value. Binary64 values, if observed, compare bits.
        bool equal = expected == null ? actual == null : expected is bool || expected is string ? expected.Equals(actual) : expected is double e ? actual != null && BitConverter.DoubleToInt64Bits(e) == BitConverter.DoubleToInt64Bits(Convert.ToDouble(actual, Inv)) : actual != null && Convert.ToDecimal(expected, Inv) == Convert.ToDecimal(actual, Inv);
        Require(equal, path + " expected " + Json(expected) + " actual " + Json(actual));
    }
    static object TypedLocation(NavLocation l) => new { surface = l.SurfaceId, x = l.Position.X, z = l.Position.Z };
    internal static int RunCensus(string[] args) {
        string folder = args[0], stage = "integral numeric/provenance/input gates";
        var report = new Dictionary<string, object> {
            { "classification", "FIXED_ORIGINAL_INPUT_RECONSTRUCTION_CENSUS_EMBEDDED_MONO" }, { "status", "NOT_QUALIFIED" },
            { "tailMeasurement", "NotRun" }, { "solverBorn", false }, { "solverAdvanced", false }
        };
        var budget = new NavigationRetentionBudget();
        NavigationCaptureBytes bytesOwner = null; NavigationArrivalWork donor = null, w = null;
        NavArrivalInput input = null; NavigationSession session = null;
        int result = 2;
        try {
            // Execute the unchanged qualification Main before any original Work
            // birth. It proves numeric R, unique surfaces and both full hashes.
            Require(Main(args) == 0, "integral original Mono qualification refused; see result.tsv");
            stage = "fresh same-input provider/member/source/profile gates";
            var bytes = File.ReadAllBytes(Path.Combine(folder, "provider-donor.body"));
            Require(Hash(bytes) == "1004067cae0e8eb706da766ecd48a00154c3a4fb7f3eab5281a83437aea3752b", "donor SHA");
            bytesOwner = NavigationCaptureBytes.CreateOwned(bytes.Length, budget, _ => bytes);
            Require(budget.TryOpenScope(NavigationPreparationKind.Decode, 1, 0, out var decode), "donor decode scope");
            using (decode) {
                using (var stream = new MemoryStream(bytesOwner.Bytes, false))
                using (var reader = new BinaryReader(stream)) {
                    donor = (NavigationArrivalWork)NavPhysicalBodyWire.ReadFrom(reader, decode.OwnedReservation());
                    Require(stream.Position == stream.Length, "donor EOF");
                }
                decode.Attach(donor); decode.Commit(donor, _ => {});
            }
            var old = (PartitionNavigationProvider)donor.PhysicalInput.Provider;
            Require(old.Binding == ProviderKey && NavContract.Digest(old.WriteBindingTranscript) == ProviderKey, "provider exact c5");
            var geometry = (NavGeometry)Field(Field(old, "locations"), "Geometry");
            var provider = new PartitionNavigationProvider(PlayableProfile.ThreeCrossingsDefault.AuthoredMap, geometry, old.HoldBinding);
            Require(provider.Binding == ProviderKey, "normal current map/exact geometry c5");
            var source = Source(); Require(source.StableKey == SourceKey && source.Candidates.Count == 796, "ordered source a5");
            var profile = PlayableProfile.ThreeCrossingsDefault.Navigation;
            Require(NavigationAdmission.ExactProfileBinding(profile) == ProfileKey, "profile exact72");
            var members = Members(); foreach (var member in members) Require(member.StableKey == MemberKey(member.EntityId), "member StableKey " + member.EntityId);
            session = new NavigationSession(1, geometry, profile, null, PlayableProfile.ThreeCrossingsDefault.AuthoredMap, budget, null);
            foreach (var a in Actors()) {
                var p = new NavPoint(D(a.XBits), D(a.ZBits)); var clearance = new NavClearanceProfile("ground", 1, "ground", 0, D(a.RadiusBits));
                NavLocation at;
                if (a.Blocks) {
                    var valid = provider.Surfaces.Where(s => provider.IsValid(new NavLocation(p, s.Id), clearance)).Select(s => s.Id).ToArray();
                    Require(valid.Length == 1, "blocking surface not unique " + a.Id);
                    Require(provider.TryLocate(p, clearance, out at) && at.SurfaceId == valid[0], "normal typed location " + a.Id);
                } else at = new NavLocation(p, a.Surface);
                var actor = session.Crowd.Add(a.Id, p, D(a.RadiusBits), profile.TankSpeed, profile.TankTurnSpeed, at); actor.Team = a.Team;
                Require(actor.Incarnation == a.Incarnation && actor.MobilityRevision == a.Mobility && !actor.Moving && !actor.Held, "normal actor operands " + a.Id);
            }
            var occupancy = session.CaptureArrivalOccupancySnapshot(members);
            Require(occupancy.Generation == 1 && occupancy.Occupants.Count == 7 && occupancy.Occupants.All(x => x.Kind == NavArrivalOccupantKind.Actor) && occupancy.StableKey == OccupancyKey, "fresh occupancy940565");
            var graph = new NavGraphProfile(ProfileKey, "distance", 1, profile.GridCell);
            var raw = new NavArrivalInput("geometry:7", "profile:" + ProfileKey, provider, graph, profile.ForUnit(0.72, profile.TankSpeed, profile.TankTurnSpeed), new NavGraphCompileLimits(1048576, 300000, 1200000), source, members, occupancy, 1048576, members.Select(m => new NavArrivalWorkingBinding(m.EntityId, provider, graph)), true);
            Require(raw.StableKey == InputKey, "fresh inputb355");
            SaveCensus(folder, "census-preflight.json", new { raw.StableKey, occupancy = occupancy.StableKey, provider = provider.Binding, source = source.StableKey, profile = ProfileKey, members = members.Select(m => new { m.EntityId, m.StableKey }).ToArray() });
            stage = "owned policy21 birth";
            Require(budget.TryOpenScope(NavigationPreparationKind.Arrival, 1, 0, out var birth), "birth scope");
            using (birth) {
                input = NavigationArrivalPhysicalInput.CopyInitial(raw, birth.OwnedReservation());
                w = NavigationArrivalWork.CreateOwned(input, birth.OwnedReservation());
                w.UseSessionProviderOwnership(true); w.SealPrepared(); birth.Attach(w); birth.Commit(w, _ => {});
            }
            report["solverBorn"] = true; Require(w.Save().Version == 21, "fresh policy21");
            session.DestroyNavigationAuthority(); session = null;
            report["preflight"] = "PASS_EXACT_FULL_ORIGINAL_INPUT_AND_OCCUPANCY";
            SaveCensus(folder, "first-gate.json", report);
            stage = "fixed original trajectory";
            var seen = new HashSet<long>(); bool matched = false;
            long milestoneAllocation = 0, milestoneGraph = 0, milestoneConnector = 0;
            int milestoneService = 0; var samples = new List<object>();
            using (var progress = new StreamWriter(Path.Combine(folder, "services.ndjson"))) {
                for (int service = 0; service < 600 && w.Status != NavArrivalStatus.Ready; service++) {
                    Begin(service, 0); bool admitted = budget.TryOpenScope(NavigationPreparationKind.Solve, 1, service + 1, out var scope); End(service, 0); Require(admitted, "normal service scope");
                    NavigationArrivalWork next = null;
                    using (scope) {
                        Begin(service, 1); var reservation = scope.OwnedReservation(); next = w.ForkOwned(reservation); End(service, 1);
                        try {
                            NavAdvanceProfile.Service(service); Begin(service, 2); next.Advance(service, 256, new NavEpochQuota(256, 256, 256, 256, 256)); End(service, 2);
                            report["solverAdvanced"] = true;
                            Require(!next.LastAdvanceRetentionBlocked && !(bool)CF(CF(next, "scheduler"), "RetentionBlocked"), "retention refusal");
                            Require(next.Status == NavArrivalStatus.Pending || next.Status == NavArrivalStatus.BudgetExhausted || next.Status == NavArrivalStatus.Ready, "terminal failure " + next.Status);
                            Begin(service, 3); next.SealPrepared(); End(service, 3); Begin(service, 4); scope.Attach(next); scope.Commit(next, _ => {}); End(service, 4);
                            Begin(service, 5);
                        } catch { next?.Release(); throw; }
                    }
                    w.Release(); w = next; End(service, 5);
                    var assignments = PartialAssignments(w);
                    foreach (var proposal in assignments) if (proposal.Member.EntityId != 14 && seen.Add(proposal.Member.EntityId)) {
                        var expected = ExpectedProof(proposal.Member.EntityId);
                        SaveCensus(folder, "proof-" + proposal.Member.EntityId + ".json", new { service, proposal.Member.EntityId, proposal.Proof.StableKey, legCount = proposal.Route.Legs.Count, proposal.Route.Cost, counters = w.RouteCounters, expected });
                        Require(proposal.Proof.StableKey == expected.Key && proposal.Route.Legs.Count == expected.Legs && proposal.Route.Cost == expected.Cost, "first two actual proof mismatch entity" + proposal.Member.EntityId);
                    }
                    var demand = Demand(w, out var request); var counters = w.RouteCounters; var tail = TailState(demand);
                    var sample = new { service, counters.PhysicalGraphWork, counters.ConnectorWork, w.AllocationWorkConsumed, w.MemberCursor, assignments = assignments.Length, publicProposals = w.Proposals.Count, outcomes = w.MemberOutcomes.Count, status = w.Status.ToString(), tail };
                    samples.Add(sample); progress.WriteLine(Json(sample)); progress.Flush();
                    if (!matched && counters.PhysicalGraphWork == 39755 && counters.ConnectorWork == 37247) {
                        Require(seen.SetEquals(new long[] { 10, 12 }) && assignments.Length == 2 && w.MemberOutcomes.Count == 0 && w.Save().PageIndex == 0 && demand != null, "matched milestone group prefix");
                        var actual = Fingerprint(demand, request);
                        SaveCensus(folder, "milestone-attempt.json", new { service, actual, expected = ExpectedFingerprint(), tail });
                        Subset(ExpectedFingerprint(), actual);
                        matched = true; milestoneService = service; milestoneAllocation = w.AllocationWorkConsumed;
                        milestoneGraph = counters.PhysicalGraphWork; milestoneConnector = counters.ConnectorWork;
                        report["milestone"] = new { service, observedRawTick = MilestoneTick, allAvailableFingerprintExact = true, actual, tail };
                        SaveCensus(folder, "matched-milestone.json", report["milestone"]);
                    }
                    if (!matched && counters.PhysicalGraphWork >= 39755 && counters.ConnectorWork > 37247) throw new InvalidOperationException("NOT_QUALIFIED: fixed trajectory crossed exact milestone without matching service boundary");
                }
            }
            Require(matched, "milestone not reached within600serviceepochs");
            Require(w.Status == NavArrivalStatus.Ready && w.Proposals.Count == 3 && w.MemberOutcomes.Count == 0, "original oracle not Ready within600serviceepochs");
            var final = w.RouteCounters; var p14 = w.Proposals.Single(p => p.Member.EntityId == 14);
            report["tail"] = new { remainingGraph = final.PhysicalGraphWork - milestoneGraph, remainingConnector = final.ConnectorWork - milestoneConnector, remainingAllocation = w.AllocationWorkConsumed - milestoneAllocation, remainingGrants = samples.Count - 1 - milestoneService, totalServiceEpochs = samples.Count, finalCounters = final, finalAllocation = w.AllocationWorkConsumed, final14 = new { p14.Proof.StableKey, legCount = p14.Route.Legs.Count, p14.Route.Cost } };
            SaveCensus(folder, "final-route14.json", new { p14.Member.EntityId, p14.Proof.StableKey, p14.Route.Cost, legCount = p14.Route.Legs.Count, legs = p14.Route.Legs.Select(l => new { from = TypedLocation(l.From), to = TypedLocation(l.To), kind = l.Kind.ToString(), l.PortalId, l.Cost }).ToArray() });
            report["status"] = "PASS_EXACT_PREFIX_AND_RECONSTRUCTION_TAIL_CENSUS"; report["tailMeasurement"] = "Measured";
            report["limits"] = "Reconstructed fixed immutable input trajectory; not actual saved BODY/exact-next, epoch labels differ historical carry jumps, no CPU/gameTick/ArmyPASS. Component authorityReservationBase0 with default64MiB retention and actual owned resources; no host pressure/cadence inference. Currentgeometry9 certificates/proofs/publication/install not measured.";
            result = 0;
        } catch (Exception e) { report["failedStage"] = stage; report["failure"] = e.ToString(); }
        finally {
            SaveCensus(folder, "census-before-cleanup.json", report);
            try {
                w?.Release(); w = null; input?.StorageRoot.Release(); input = null;
                session?.DestroyNavigationAuthority(); session = null;
                donor?.Release(); donor = null; bytesOwner?.Dispose(); bytesOwner = null;
                report["terminalLedger"] = new { budget.PhysicalBytes, budget.HeldBytes, budget.ActiveScopes };
                Require(budget.PhysicalBytes == 0 && budget.HeldBytes == 0 && budget.ActiveScopes == 0, "terminal ledger0");
            } catch (Exception e) { report["cleanupFailure"] = e.ToString(); report["status"] = "NOT_QUALIFIED"; result = 2; }
            SaveCensus(folder, "census-result.json", report); Console.WriteLine(report["status"]);
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)] struct Timespec { public long Sec; public long Nsec; }
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="clock_gettime")]
    static extern int ClockGetTime(int id, out Timespec value);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="clock_getres")]
    static extern int ClockGetRes(int id, out Timespec value);
    struct Stamp { public long Wall, Process, Thread; }
    static int ProfileThread;
    static Stamp ReadClocks() {
        if (ProfileThread != System.Threading.Thread.CurrentThread.ManagedThreadId)
            throw new InvalidOperationException("managed thread changed; profile NotRun");
        Timespec w, p, t;
        if (ClockGetTime(6, out w) != 0 || ClockGetTime(12, out p) != 0 || ClockGetTime(16, out t) != 0)
            throw new InvalidOperationException("clock_gettime failed; profile NotRun");
        return new Stamp { Wall = w.Sec * 1000000000L + w.Nsec, Process = p.Sec * 1000000000L + p.Nsec, Thread = t.Sec * 1000000000L + t.Nsec };
    }
    static long[,] GrantTimes;
    static Stamp[] GrantStarts;
    static void Begin(int row, int phase) { GrantStarts[row * 6 + phase] = ReadClocks(); }
    static void End(int row, int phase) {
        var delta = ClockDelta(GrantStarts[row * 6 + phase], ReadClocks(), "phase " + phase);
        GrantTimes[row, phase * 3] = delta.Wall;
        GrantTimes[row, phase * 3 + 1] = delta.Process;
        GrantTimes[row, phase * 3 + 2] = delta.Thread;
    }
    static Stamp ClockDelta(Stamp start, Stamp end, string label) {
        long wall = end.Wall - start.Wall, process = end.Process - start.Process, thread = end.Thread - start.Thread;
        if (wall < 0 || process < 0 || thread < 0)
            throw new InvalidOperationException("negative monotonic/process/thread clock delta in " + label);
        return new Stamp { Wall = wall, Process = process, Thread = thread };
    }
    internal static int RunClockGuardSelfTest() {
        var zero = ClockDelta(new Stamp(), new Stamp(), "zero self test");
        Require(zero.Wall == 0 && zero.Process == 0 && zero.Thread == 0, "zero delta guard");
        Action<Stamp, Stamp> rejects = (a, b) => {
            bool rejected = false;
            try { ClockDelta(a, b, "negative self test"); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "negative clock delta was accepted");
        };
        rejects(new Stamp { Wall = 2 }, new Stamp { Wall = 1 });
        rejects(new Stamp { Process = 2 }, new Stamp { Process = 1 });
        rejects(new Stamp { Thread = 2 }, new Stamp { Thread = 1 });
        Console.WriteLine("CLOCK_GUARD_SELF_TEST_PASS; solverBorn=False");
        return 0;
    }
    static object ClockCalibration() {
        Timespec rw = default(Timespec), rp = default(Timespec), rt = default(Timespec);
        Require(ClockGetRes(6, out rw) == 0 && ClockGetRes(12, out rp) == 0 && ClockGetRes(16, out rt) == 0, "clock_getres failed; profile NotRun");
        var samples = new long[101];
        for (int i = 0; i < samples.Length; i++) { var a = ReadClocks(); var b = ReadClocks(); samples[i] = ClockDelta(a, b, "clock preflight").Wall; }
        Array.Sort(samples);
        return new { clockIds = new { monotonic = 6, processCpu = 12, threadCpu = 16 },
            pointerBytes = IntPtr.Size, managedThreadId = ProfileThread,
            resolutionNanoseconds = new { monotonic = rw.Sec * 1000000000L + rw.Nsec, process = rp.Sec * 1000000000L + rp.Nsec, thread = rt.Sec * 1000000000L + rt.Nsec },
            emptyTimestampPairWallNanoseconds = new { minimum = samples[0], median = samples[samples.Length / 2], maximum = samples[samples.Length - 1] },
            subtracted = false, note = "clock reads are sequential; process CPU includes background Mono GC/JIT; process minus thread CPU is not GC time" };
    }
    static object GrantSummary(int count) {
        string[] phases = { "scopeAdmission", "ownedReservationAndForkOwned", "advanceInclusive", "sealPrepared", "attachAndCommit", "scopeDisposalAndPreviousWorkRelease" };
        var output = new Dictionary<string, object>();
        foreach (var phase in Enumerable.Range(0, 6)) {
            long[][] channels = { new long[count], new long[count], new long[count] };
            for (int row = 0; row < count; row++) for (int channel = 0; channel < 3; channel++) channels[channel][row] = GrantTimes[row, phase * 3 + channel];
            var values = new Dictionary<string, object>(); string[] names = { "wallNanoseconds", "processCpuNanoseconds", "threadCpuNanoseconds" };
            for (int channel = 0; channel < 3; channel++) { Array.Sort(channels[channel]); long sum = 0; for (int i = 0; i < count; i++) sum += channels[channel][i]; values[names[channel]] = new { sum, min = count == 0 ? 0 : channels[channel][0], median = count == 0 ? 0 : channels[channel][count / 2], max = count == 0 ? 0 : channels[channel][count - 1] }; }
            output[phases[phase]] = values;
        }
        return output;
    }
    internal static int RunProfile(string[] args) {
        string folder = args[0];
        // A failed monotonic/CPU clock preflight exits before solver birth. No fallback clock is permitted.
        object calibration;
        try { Require(IntPtr.Size == 8, "expected 64-bit embedded Mono; profile NotRun"); ProfileThread = System.Threading.Thread.CurrentThread.ManagedThreadId; calibration = ClockCalibration(); }
        catch (Exception e) { SaveCensus(folder, "profile-not-run.json", new { status = "NOT_RUN", reason = e.ToString(), solverBorn = false }); return 2; }
        GrantTimes = new long[600, 18]; GrantStarts = new Stamp[3600];
        NavAdvanceProfile.Preflight(folder);
        SaveCensus(folder, "grant-profile-report-preflight.json", new { status = "REPORT_PREFLIGHT_ONLY", clock = calibration,
            phaseClockCalls = 0, calibrationClockCalls = 606, measuredGrantRows = new object[0], aggregate = GrantSummary(0), grants = 0,
            observerAndGateInterval = "NotMeasured", hostExternalAuthorityReservation = "NotMeasured" });
        int rc = RunCensus(args);
        var rows = new List<object>();
        string services = Path.Combine(folder, "services.ndjson");
        int count = File.Exists(services) ? File.ReadAllLines(services).Length : 0;
        for (int row = 0; row < count && row < 600; row++) {
            var channels = new Dictionary<string, object>();
            string[] phases = { "scopeAdmission", "ownedReservationAndForkOwned", "advanceInclusive", "sealPrepared", "attachAndCommit", "scopeDisposalAndPreviousWorkRelease" };
            foreach (int phase in Enumerable.Range(0, 6)) channels[phases[phase]] = new { wallNanoseconds = GrantTimes[row, phase * 3], processCpuNanoseconds = GrantTimes[row, phase * 3 + 1], threadCpuNanoseconds = GrantTimes[row, phase * 3 + 2] };
            rows.Add(new { service = row, phases = channels });
        }
        SaveCensus(folder, "subphase-profile.json", NavAdvanceProfile.Report(count));
        SaveCensus(folder, "grant-profile.json", new { status = rc == 0 ? "PASS_ARRIVAL_WORK_GRANT_PROFILE" : "PARTIAL_NOT_QUALIFIED", clock = calibration,
            phaseClockCalls = count * 36, calibrationClockCalls = 606,
            measuredGrantRows = rows, aggregate = GrantSummary(rows.Count), grants = rows.Count,
            observerAndGateInterval = "NotMeasured; not inferred from total process wall",
            hostExternalAuthorityReservation = "NotMeasured", hostPublishCallback = "empty callback; not actual host publication",
            attributionLimits = new[] { "Advance is inclusive; geometry and copy-on-write are not separated", "process CPU includes background Mono GC/JIT; process minus thread CPU is not GC time", "clock reads are sequential; no automatic overhead subtraction", "subphase clock calls are reported separately and remain inside measured buckets" } });
        return rc == 0 ? 0 : 2;
    }
    internal static int RunReportPreflight(string folder) {
        GrantTimes = new long[600, 18]; GrantStarts = new Stamp[3600];
        NavAdvanceProfile.Preflight(folder);
        SaveCensus(folder, "grant-profile-report-preflight.json", new { status = "REPORT_PREFLIGHT_ONLY",
            phaseClockCalls = 0, calibrationClockCalls = 0, measuredGrantRows = new object[0], aggregate = GrantSummary(0), grants = 0 });
        return 0;
    }

    internal static class NavAdvanceProfile {
        [StructLayout(LayoutKind.Sequential)] struct Timespec { public long Sec; public long Nsec; }
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint="clock_gettime")]
        static extern int ClockGetTime(int id, out Timespec value);
        struct Stamp { public long Wall, Process, Thread; }
        static readonly long[,,] Totals = new long[600, 4, 3];
        static readonly int[,] Calls = new int[600, 4];
        static readonly int[] BranchCalls = new int[600];
        static readonly Stamp[] PaidStarts = new Stamp[3];
        static Stamp advanceStart;
        static int service = -1;
        static long clockCalls;
        static Stamp Read() {
            Timespec w, p, t;
            if (ClockGetTime(6, out w) != 0 || ClockGetTime(12, out p) != 0 || ClockGetTime(16, out t) != 0)
                throw new InvalidOperationException("subphase clock_gettime failed");
            clockCalls += 3;
            return new Stamp { Wall = w.Sec * 1000000000L + w.Nsec, Process = p.Sec * 1000000000L + p.Nsec,
                Thread = t.Sec * 1000000000L + t.Nsec };
        }
        internal static void Service(int row) { service = row; }
        internal static void BeginAdvance() { advanceStart = Read(); }
        internal static void EndAdvance() { Add(0, advanceStart, Read()); BranchCalls[service]++; }
        internal static void BeginPaid(int bucket) { PaidStarts[bucket] = Read(); }
        internal static void EndPaid(int bucket) { Add(bucket + 1, PaidStarts[bucket], Read()); }
        static void Add(int bucket, Stamp a, Stamp b) {
            if (service < 0 || service >= 600) throw new InvalidOperationException("subphase service index invalid");
            long wall = b.Wall - a.Wall, process = b.Process - a.Process, thread = b.Thread - a.Thread;
            if (wall < 0 || process < 0 || thread < 0) throw new InvalidOperationException("negative subphase clock delta");
            Totals[service, bucket, 0] += wall; Totals[service, bucket, 1] += process; Totals[service, bucket, 2] += thread;
            Calls[service, bucket]++;
        }
        static object Aggregate(int bucket, int rows) {
            string[] names = { "wallNanoseconds", "processCpuNanoseconds", "threadCpuNanoseconds" };
            var values = new Dictionary<string, object>();
            for (int channel = 0; channel < 3; channel++) {
                var sorted = Enumerable.Range(0, rows).Select(i => Totals[i, bucket, channel]).OrderBy(x => x).ToArray();
                values[names[channel]] = new { sum = sorted.Sum(), min = sorted.Length == 0 ? 0 : sorted[0],
                    median = sorted.Length == 0 ? 0 : sorted[sorted.Length / 2], max = sorted.Length == 0 ? 0 : sorted[sorted.Length - 1] };
            }
            return new { calls = Enumerable.Range(0, rows).Sum(i => Calls[i, bucket]), perGrant = values };
        }
        internal static object Report(int rows) {
            string[] names = { "advanceInclusiveDemandBranch", "StepBookkeeping", "StepGraph", "StepConnector" };
        if (rows < 0 || rows > Totals.GetLength(0) || names.Length != Totals.GetLength(1))
            throw new InvalidOperationException("subphase report dimensions invalid");
        long recordedPairs = 0;
        for (int row = 0; row < rows; row++) {
            if (Calls[row, 0] != BranchCalls[row]) throw new InvalidOperationException("demand branch call count mismatch");
            for (int bucket = 0; bucket < 4; bucket++) {
                if (Calls[row, bucket] < 0) throw new InvalidOperationException("negative subphase call count");
                for (int channel = 0; channel < 3; channel++)
                    if (Totals[row, bucket, channel] < 0) throw new InvalidOperationException("negative subphase total");
                recordedPairs += Calls[row, bucket];
            }
        }
        if (clockCalls != recordedPairs * 6) throw new InvalidOperationException("subphase clock call count mismatch");
            var aggregate = new Dictionary<string, object>();
            var perGrant = new List<object>();
            for (int bucket = 0; bucket < 4; bucket++) aggregate[names[bucket]] = Aggregate(bucket, rows);
            for (int row = 0; row < rows; row++) {
                var values = new Dictionary<string, object>();
                for (int bucket = 0; bucket < 4; bucket++) values[names[bucket]] = new {
                    wallNanoseconds = Totals[row, bucket, 0], processCpuNanoseconds = Totals[row, bucket, 1],
                    threadCpuNanoseconds = Totals[row, bucket, 2], calls = Calls[row, bucket] };
                perGrant.Add(new { service = row, demandBranchCalls = BranchCalls[row], buckets = values,
                    schedulerOverheadRemainderThreadCpuNanoseconds = Totals[row, 0, 2] - Totals[row, 1, 2] - Totals[row, 2, 2] - Totals[row, 3, 2] });
            }
            return new { clockCalls, clockReads = clockCalls / 3, timingPairs = clockCalls / 6,
                semantics = "per-service sums; advance inclusive branch minus non-overlapping paid Step calls is scheduler-overhead remainder; no assumed clock overhead subtraction",
                buckets = aggregate, grants = perGrant };
        }
    internal static void Preflight(string folder) {
        var result = Report(600);
        SaveCensus(folder, "subphase-report-preflight.json", result);
        if (!File.ReadAllText(Path.Combine(folder, "subphase-report-preflight.json")).Contains("advanceInclusiveDemandBranch") ||
            !File.ReadAllText(Path.Combine(folder, "subphase-report-preflight.json")).Contains("StepConnector"))
            throw new InvalidOperationException("subphase report serialization preflight failed");
        for (int row = 0; row < 600; row++) {
            if (BranchCalls[row] != 0) throw new InvalidOperationException("preflight branch counters not zero");
            for (int bucket = 0; bucket < 4; bucket++) {
                if (Calls[row, bucket] != 0) throw new InvalidOperationException("preflight call counters not zero");
                for (int channel = 0; channel < 3; channel++)
                    if (Totals[row, bucket, channel] != 0) throw new InvalidOperationException("preflight timing totals not zero");
            }
        }
        if (clockCalls != 0) throw new InvalidOperationException("preflight clock count not zero");
        Array.Clear(Totals, 0, Totals.Length); Array.Clear(Calls, 0, Calls.Length);
        Array.Clear(BranchCalls, 0, BranchCalls.Length); Array.Clear(PaidStarts, 0, PaidStarts.Length);
        clockCalls = 0; service = -1;
    }
    }

}

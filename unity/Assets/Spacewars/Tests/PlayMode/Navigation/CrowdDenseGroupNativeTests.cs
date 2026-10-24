using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode.Navigation
{
    public sealed class CrowdDenseGroupNativeTests
    {
        private const long Generation = 71;
        private const int Seed = 19092026;
        private static readonly NavPoint Goal = new NavPoint(12, -15);
        private TransitionCapture transitionDiagnostic;

        private static OfflineMatchConfiguration Configuration(PlayableProfile profile, OfflineScenarioUnit[] units)
        {
            // Existing native-flat ordinary witnesses use two human-controlled
            // offline owners, so neither side can add AI production or units.
            var roster = new[]
            {
                new OfflineParticipant("owner-11", 1, 1, OfflineControl.Human),
                new OfflineParticipant("owner-28", 2, 2, OfflineControl.Human)
            };
            var starts = new[]
            {
                new OfflineStart("start-1", 1, profile.Headquarters(PlayableOwner.Player), new NavPoint(profile.Headquarters(PlayableOwner.Player).X + 6, profile.Headquarters(PlayableOwner.Player).Z), pin: 1),
                new OfflineStart("start-2", 2, profile.Headquarters(PlayableOwner.Enemy), new NavPoint(profile.Headquarters(PlayableOwner.Enemy).X - 6, profile.Headquarters(PlayableOwner.Enemy).Z), Math.PI, 2)
            };
            var sites = new[]
                {
                    new TerritorySite(1, PlayableBuildingKind.Headquarters, starts[0].Position, Array.Empty<TerritorySlot>()),
                    new TerritorySite(2, PlayableBuildingKind.Headquarters, starts[1].Position, Array.Empty<TerritorySlot>())
                };
            return new OfflineMatchConfiguration(profile, "native-flat-dense-group-witness-v1", "native-flat-eight-starts-v1",
                "native-flat-two-human-native-starts-v1", Seed, roster, starts, sites,
                Array.Empty<NavObstacle>(), new double[,] { { 0, 100 }, { 100, 0 } },
                scenario: units);
        }

        private sealed class FrameSummary
        {
            internal long Tick;
            internal int TotalCrowdBodies, Active, Moving, Held, MaxNoProgress, NeighborCandidates, Moved, Arrived;
            internal double MaxDisplacement;
            internal bool SpeedSafe = true, TurnSafe = true, SweepSafe = true, StaticSafe = true, BodiesSafe = true;
        }

        private sealed class AuthorityWitness
        {
            internal readonly object Gate = new object();
            internal readonly System.Collections.Generic.List<FrameSummary> Frames = new System.Collections.Generic.List<FrameSummary>();
            internal long FirstMotionTick = -1;
            private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private static object Read(object value, string name) =>
                value.GetType().GetProperty(name, Instance)?.GetValue(value) ?? value.GetType().GetField(name, Instance).GetValue(value);
            private static T Read<T>(object value, string name) => (T)Read(value, name);

            internal void Attach(PlayableRuntime runtime, int[] ids, NavPoint[] origins, TransitionCapture diagnostic = null)
            {
                var domain = typeof(PlayableRuntime).GetField("domain", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime);
                var navigation = Read(domain, "Navigation");
                var crowd = Read(navigation, "Crowd");
                var previous = (NavPoint[])origins.Clone();
                var headings = new double[ids.Length];
                var geometry = runtime.NavigationGeometry;
                diagnostic?.Attach(runtime, domain, crowd, ids);
                Action observe = () =>
                {
                    var summary = new FrameSummary { Tick = Read<long>(domain, "Tick"), NeighborCandidates = Read<int>(crowd, "NeighborCandidates") };
                    var positions = new NavPoint[ids.Length];
                    var currentHeadings = new double[ids.Length];
                    var bodies = ((IEnumerable)Read(crowd, "Units")).Cast<object>().ToDictionary(u => Read<int>(u, "Id"));
                    summary.TotalCrowdBodies = bodies.Count;
                    for (int i = 0; i < ids.Length; i++)
                    {
                        var body = bodies[ids[i]];
                        positions[i] = Read<NavPoint>(body, "Position");
                        currentHeadings[i] = Read<double>(body, "Heading");
                        bool moving = Read<bool>(body, "Moving"), held = Read<bool>(body, "Held");
                        int routeIndex = (int)body.GetType().GetField("routeIndex", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(body);
                        int routeCount = Read<int>(ReadMember(body, "Route"), "Count");
                        if (moving && !held && routeIndex < routeCount) summary.Active++;
                        if (moving) summary.Moving++;
                        if (held) summary.Held++;
                        if (Read<NavigationOutcome>(body, "Outcome") == NavigationOutcome.Arrived) summary.Arrived++;
                        int noProgress = (int)body.GetType().GetField("noProgressTicks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(body);
                        summary.MaxNoProgress = Math.Max(summary.MaxNoProgress, noProgress);
                        double dx = positions[i].X - origins[i].X, dz = positions[i].Z - origins[i].Z;
                        if (dx * dx + dz * dz > 1e-8) summary.Moved++;
                        summary.MaxDisplacement = Math.Max(summary.MaxDisplacement, Math.Sqrt(dx * dx + dz * dz));
                        dx = positions[i].X - previous[i].X; dz = positions[i].Z - previous[i].Z;
                        double speed = Read<double>(body, "Speed"), heading = currentHeadings[i], turnSpeed = Read<double>(body, "TurnSpeed"), radius = Read<double>(body, "Radius");
                        summary.SpeedSafe &= dx * dx + dz * dz <= speed * speed / 900d + 1e-9;
                        double turn = Math.Abs(Math.Atan2(Math.Sin(heading - headings[i]), Math.Cos(heading - headings[i])));
                        summary.TurnSafe &= turn <= turnSpeed / 30d + 1e-9;
                        summary.SweepSafe &= geometry.SegmentFree(previous[i], positions[i], radius);
                        summary.StaticSafe &= geometry.IsFree(positions[i], radius);
                    }
                    for (int i = 0; i < ids.Length; i++) for (int j = 0; j < i; j++)
                    {
                        var a = bodies[ids[i]];
                        var b = bodies[ids[j]];
                        double sx = previous[i].X - previous[j].X, sz = previous[i].Z - previous[j].Z;
                        double vx = positions[i].X - previous[i].X - positions[j].X + previous[j].X;
                        double vz = positions[i].Z - previous[i].Z - positions[j].Z + previous[j].Z;
                        double q = vx * vx + vz * vz;
                        double t = q == 0 ? 0 : Math.Max(0, Math.Min(1, -(sx * vx + sz * vz) / q));
                        double x = sx + vx * t, z = sz + vz * t, radius = Read<double>(a, "Radius") + Read<double>(b, "Radius");
                        summary.BodiesSafe &= x * x + z * z >= radius * radius - 1e-8;
                    }
                    Array.Copy(positions, previous, ids.Length);
                    Array.Copy(currentHeadings, headings, ids.Length);
                    lock (Gate)
                    {
                        Frames.Add(summary);
                        if (summary.Moved > 0 && FirstMotionTick < 0) FirstMotionTick = summary.Tick;
                    }
                    // Queue only after releasing the witness gate. Serialization is
                    // performed by CompleteCapture after this authority cycle publishes.
                    diagnostic?.Observe(bodies, summary.Tick);
                };
                domain.GetType().GetField("CombatStepForQa", Instance).SetValue(domain, observe);
            }
        }

        // Explicit, data-only observer. No retained world/restore/compute, no pause,
        // command, velocity, route or preferred-speed writes. Default ordinary tests
        // never instantiate this observer or register its serialization callback.
        private sealed class TransitionCapture
        {
            private sealed class Checkpoint
            {
                internal long RequestedTick, BoundaryTick = -1, Publication = -1;
                internal string Table;
                internal byte[] Bytes;
            }
            private readonly object gate = new object();
            private readonly string folder, producer;
            private readonly int threshold;
            private readonly bool[] moving = new bool[12];
            private readonly NavigationOutcome[] outcomes = new NavigationOutcome[12];
            private string source, map;
            private Action serializationCallback;
            private PlayableRuntime runtime;
            private UnityHostRouteService host;
            private object domain, crowd;
            private int[] ids;
            private Checkpoint before, after, requested;
            private System.Threading.Tasks.Task<byte[]> pending;
            private System.Threading.Tasks.Task saveTask;
            private long transitionTick = -1, lastTick = -1;
            private int[] transitionIds = Array.Empty<int>();
            private int requests;
            private bool sealedCapture, saving, fullWindow, transitionWasMoving = true;
            private string error;
            private const double Dt = 1d / 30d;
            internal TransitionCapture(string folder, string producer)
            {
                this.folder = folder; this.producer = producer;
                double timeout = PlayableProfile.Default.Navigation.BlockedTimeoutSeconds;
                threshold = checked((int)Math.Ceiling(timeout / Dt));
                // Match the source's product comparison, including rounding at
                // the boundary; do not hard-code the previously observed tick.
                while (threshold > 0 && (threshold - 1) * Dt >= timeout) threshold--;
                while (threshold * Dt < timeout) threshold++;
            }
            internal void Attach(PlayableRuntime runtime, object domain, object crowd, int[] ids)
            {
                this.runtime = runtime; this.domain = domain; this.crowd = crowd; this.ids = (int[])ids.Clone();
                var hook = typeof(PlayableRuntime).GetField("BeforeCaptureSerialization", PrivateInstance);
                if (hook.GetValue(runtime) != null) throw new InvalidOperationException("Capture callback already owned.");
                serializationCallback = AtSerializationBoundary;
                hook.SetValue(runtime, serializationCallback);
            }
            internal void Own(PlayableRuntime runtime, UnityHostRouteService host)
            {
                this.runtime = runtime; this.host = host;
                var config = (OfflineMatchConfiguration)ReadMember(runtime, "offlineConfiguration");
                source = config.SourceIdentity; map = config.MapIdentity;
            }
            private void Harvest()
            {
                if (pending == null || !pending.IsCompleted) return;
                if (pending.IsCanceled || pending.IsFaulted)
                    error = "capture-failed: " + pending.Exception;
                else
                {
                    requested.Bytes = pending.Result; // Transfer reference, never clone.
                    if (transitionTick >= 0 && requested.RequestedTick == transitionTick) after = requested;
                    else before = requested; // Drop the previous rolling buffer before the next request.
                }
                pending = null; requested = null;
            }
            internal void Observe(System.Collections.Generic.Dictionary<int, object> bodies, long tick)
            {
                lock (gate)
                {
                    try
                    {
                        Harvest();
                        if (sealedCapture || error != null || transitionTick >= 0) return;
                        var blocked = new System.Collections.Generic.List<int>();
                        bool nearTimeout = false;
                        for (int i = 0; i < ids.Length; i++)
                        {
                            var body = bodies[ids[i]];
                            bool nowMoving = (bool)ReadMember(body, "Moving");
                            var outcome = (NavigationOutcome)ReadMember(body, "Outcome");
                            bool currentOrder = Convert.ToInt64(ReadMember(body, "InstalledOrder")) == 1;
                            if (currentOrder && outcomes[i] != NavigationOutcome.Blocked && outcome == NavigationOutcome.Blocked)
                            { blocked.Add(ids[i]); transitionWasMoving &= moving[i]; }
                            nearTimeout |= currentOrder && nowMoving && !(bool)ReadMember(body, "Held") &&
                                (int)ReadMember(body, "noProgressTicks") >= Math.Max(0, threshold - 2);
                            moving[i] = nowMoving; outcomes[i] = outcome;
                        }
                        if (blocked.Count != 0)
                        {
                            transitionTick = tick; transitionIds = blocked.ToArray();
                            if (lastTick != tick - 1) error = "observer-gap-before-first-transition";
                        }
                        lastTick = tick;
                        if (error != null || (!nearTimeout && transitionTick < 0)) return;
                        if (pending != null)
                        {
                            // Never wait in authority or enqueue a second pending request.
                            if (transitionTick >= 0) error = "capture-pending-at-first-transition";
                            return;
                        }
                        requested = new Checkpoint { RequestedTick = tick };
                        pending = runtime.RequestCaptureBytes(); requests++;
                    }
                    catch (Exception ex) { error = "observer: " + ex; }
                }
            }
            private void AtSerializationBoundary()
            {
                lock (gate)
                {
                    if (requested == null) return; // An ordinary terminal capture is separate.
                    try
                    {
                        requested.BoundaryTick = Convert.ToInt64(ReadMember(domain, "Tick"));
                        requested.Publication = runtime.Latest.Sequence;
                        requested.Table = AllBodiesTable(crowd);
                        if (requested.BoundaryTick != requested.RequestedTick || runtime.Latest.Tick != requested.BoundaryTick)
                            error = "requested/published/serialization tick mismatch";
                    }
                    catch (Exception ex) { error = "serialization-boundary: " + ex; }
                }
            }
            internal bool SealAndIsSettled()
            {
                lock (gate) { sealedCapture = true; Harvest(); return pending == null; }
            }
            internal System.Threading.Tasks.Task SaveCompleted()
            {
                lock (gate)
                {
                    sealedCapture = true; Harvest();
                    if (saving) return saveTask;
                    saving = true;
                    if (pending != null) error = "capture-not-complete-before-cleanup";
                    // Only completed immutable byte arrays and strings cross to the writer.
                    var first = before; var second = after;
                    string status = error != null ? "NotProven" : transitionTick < 0 ? (fullWindow ? "NO_TRANSITION" : "NotProven") :
                        transitionWasMoving && transitionIds.Length == 1 && transitionIds[0] == 13 && first != null && second != null &&
                        first.RequestedTick == transitionTick - 1 && second.RequestedTick == transitionTick ? "PAIR_CAPTURED" : "NotProven";
                    string facts = "measurement=" + status + "\nerror=" + error + "\nproducer=" + producer +
                        "\nfirstBlockedTick=" + transitionTick + "\nfirstBlockedIds=" + string.Join(",", transitionIds) +
                        "\nfirstBlockedPreviouslyMoving=" + transitionWasMoving + "\nordinaryWindowComplete=" + fullWindow +
                        "\ntimeoutTicks=" + threshold + "\ndt=" + Dt.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                        "\nrequests=" + requests + "\nsource=" + source + "\nmap=" + map +
                        "\nprofile=" + PlayableProfile.Default.ProfileId + "/" + PlayableProfile.Default.Revision +
                        "\nretainedBufferLimit=2\nretainedWorldRoots=0\n" +
                        "caps=NotRetainedAtPostStepBoundary; no reconstructed or substituted values\n" +
                        "qualification=NotRun; privateCycle=NotRun; performanceClaim=none\n";
                    saveTask = System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            System.IO.Directory.CreateDirectory(folder);
                            string firstMetadata = null, secondMetadata = null;
                            void Failed(Exception ex) { facts = facts.Replace("measurement=" + status, "measurement=NotProven") + "saveOrEnvelopeError=" + ex + "\n"; }
                            try { firstMetadata = SaveCheckpoint(first, "before"); } catch (Exception ex) { Failed(ex); }
                            try { secondMetadata = SaveCheckpoint(second, "after"); } catch (Exception ex) { Failed(ex); }
                            try
                            {
                                if (status == "PAIR_CAPTURED")
                                {
                                    string Row(Checkpoint checkpoint, int id) => checkpoint.Table.Split('\n').Single(row => row.StartsWith(id + "\t", StringComparison.Ordinal));
                                    var b = Row(first, 13).Split('\t'); var a = Row(second, 13).Split('\t');
                                    if (b[6] != "True" || b[8] == "Blocked" || a[6] != "False" || a[8] != "Blocked" || b[25] != "1" || a[25] != "1")
                                        throw new InvalidOperationException("Boundary TSV does not contain the observed current-order 13 transition.");
                                    foreach (string key in new[] { "source=", "profileBindingSha256=", "configurationBindingSha256=" })
                                        if (firstMetadata == null || secondMetadata == null || firstMetadata.Split('\n').Single(line => line.StartsWith(key, StringComparison.Ordinal)) != secondMetadata.Split('\n').Single(line => line.StartsWith(key, StringComparison.Ordinal)))
                                            throw new InvalidOperationException("Before/after envelope binding mismatch.");
                                    foreach (int id in new[] { 3, 4 }.Concat(ids))
                                        if (Row(first, id).Split('\t')[20] != Row(second, id).Split('\t')[20])
                                            throw new InvalidOperationException("Actor identity changed across checkpoints.");
                                }
                            }
                            catch (Exception ex) { Failed(ex); }
                            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "transition-measurement.txt"), facts);
                        }
                        finally
                        {
                            // Release the pair before the existing ordinary terminal
                            // capture, which has its own independent lifetime.
                            lock (gate) { before = null; after = null; }
                            first = null; second = null;
                        }
                    });
                    return saveTask;
                }
            }
            internal IEnumerator Flush(bool ordinaryWindowComplete = false)
            {
                lock (gate) fullWindow |= ordinaryWindowComplete;
                while (!SealAndIsSettled()) yield return null;
                var save = SaveCompleted();
                while (!save.IsCompleted) yield return null;
                if (save.IsFaulted) TestContext.WriteLine("B12_TRANSITION_SAVE_FAILED " + save.Exception);
            }
            private string SaveCheckpoint(Checkpoint value, string name)
            {
                if (value?.Bytes == null) return null;
                // Persist original inputs even when envelope/tick verification fails.
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".bin"), value.Bytes);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, name + "-all-bodies.tsv"), value.Table ?? "");
                string metadata = VerifyEnvelope(value.Bytes, value.RequestedTick, value.Publication, source);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, name + "-metadata.txt"),
                    "producer=" + producer + "\nrequestedTick=" + value.RequestedTick + "\nboundaryTick=" + value.BoundaryTick + "\n" + metadata);
                if (value.Table == null || value.Table.TrimEnd().Split('\n').Length != 15 ||
                    value.Table.TrimEnd().Split('\n').Any(row => row.Split('\t').Length != 28))
                    throw new InvalidOperationException("Full 14-body / 28-field table required.");
                return metadata;
            }
            internal void Close()
            {
                if (runtime == null) return;
                // This wrapper alone owns these hooks and runtime. No capture wait
                // or direct live serialization occurs during fixture cleanup.
                runtime.RequestStop();
                if (domain != null) domain.GetType().GetField("CombatStepForQa", PrivateInstance).SetValue(domain, null);
                var hook = typeof(PlayableRuntime).GetField("BeforeCaptureSerialization", PrivateInstance);
                if (serializationCallback != null && ReferenceEquals(hook.GetValue(runtime), serializationCallback)) hook.SetValue(runtime, null);
                host?.Dispose();
                runtime.Dispose();
            }
            private static string AllBodiesTable(object crowd)
            {
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                string Cell(object value) => value is double d ? d.ToString("R", culture) : Convert.ToString(value, culture);
                var table = new System.Text.StringBuilder("Id\tTeam\tCanYield\tX\tZ\tHeading\tMoving\tHeld\tOutcome\tGoalX\tGoalZ\tRadius\tSpeed\tTurnSpeed\tGlobalCount\tGlobalCursor\tLocalCount\tLocalCursor\tNoProgress\tBlockedSeconds\tIncarnation\tMobility\tGroup\tRevision\tRequest\tOrder\tDetached\tInstalledGlobalAlias\n");
                foreach (var body in ((IEnumerable)ReadMember(crowd, "Units")).Cast<object>().OrderBy(u => (int)ReadMember(u, "Id")))
                {
                    var at = (NavPoint)ReadMember(body, "Position"); var goal = (NavPoint)ReadMember(body, "Goal");
                    var storage = ReadMember(body, "StorageOwner"); var installed = ReadMember(storage, "InstalledExecution");
                    var cells = new object[] { ReadMember(body, "Id"), ReadMember(body, "Team"), ReadMember(body, "CanYield"), at.X, at.Z, ReadMember(body, "Heading"), ReadMember(body, "Moving"), ReadMember(body, "Held"), ReadMember(body, "Outcome"), goal.X, goal.Z, ReadMember(body, "Radius"), ReadMember(body, "Speed"), ReadMember(body, "TurnSpeed"), ReadMember(ReadMember(body, "Route"), "Count"), ReadMember(body, "routeIndex"), ((Array)ReadMember(body, "localRoute")).Length, ReadMember(body, "localIndex"), ReadMember(body, "noProgressTicks"), ReadMember(body, "blockedSeconds"), ReadMember(body, "Incarnation"), ReadMember(body, "MobilityRevision"), ReadMember(body, "InstalledGroupId"), ReadMember(body, "InstalledRevision"), ReadMember(body, "InstalledRequest"), ReadMember(body, "InstalledOrder"), ReadMember(storage, "InstalledDetached"), installed != null && ReferenceEquals(ReadMember(installed, "Route"), ReadMember(storage, "Global")) };
                    table.AppendLine(string.Join("\t", cells.Select(Cell)));
                }
                return table.ToString();
            }
            private static string VerifyEnvelope(byte[] bytes, long requestedTick, long publication, string expectedSource)
            {
                // Read and hash slices of the immutable original envelope: no Decode
                // clone, hydrated world, DTO rewrite, or second full-byte backing.
                using var stream = new System.IO.MemoryStream(bytes, false);
                using var reader = new System.IO.BinaryReader(stream);
                using var sha = System.Security.Cryptography.SHA256.Create();
                string Hash(int start, int count) => BitConverter.ToString(sha.ComputeHash(bytes, start, count)).Replace("-", "").ToLowerInvariant();
                bool Digest(int start, int count, int at) => sha.ComputeHash(bytes, start, count).Select((b, i) => b == bytes[at + i]).All(x => x);
                string Text() { int n = reader.ReadInt32(); if (n < 0 || n > bytes.Length - stream.Position) throw new InvalidOperationException("Invalid text length."); string result = System.Text.Encoding.UTF8.GetString(bytes, (int)stream.Position, n); stream.Position += n; return result; }
                (int Start, int Count) Section()
                {
                    int n = reader.ReadInt32(), start = checked((int)stream.Position);
                    if (n < 0 || n > bytes.Length - start - 32 || !Digest(start, n, start + n)) throw new InvalidOperationException("Section digest/length.");
                    stream.Position += n + 32; return (start, n);
                }
                if (bytes.Length < 32 || !Digest(0, bytes.Length - 32, bytes.Length - 32) || reader.ReadInt32() != 0x53575731)
                    throw new InvalidOperationException("Envelope magic/digest.");
                int version = reader.ReadInt32(), seed = reader.ReadInt32(); long generation = reader.ReadInt64(), tick = reader.ReadInt64(); string source = Text();
                var binding = Section(); var config = Section(); Section(); Section(); var ai = Section(); var transport = Section();
                int visions = reader.ReadInt32(); if (visions < 0 || visions > 8) throw new InvalidOperationException("Vision count.");
                for (int i = 0; i < visions; i++) Section();
                if (stream.Position != bytes.Length - 32) throw new InvalidOperationException("Envelope trailing data.");
                stream.Position = ai.Start; int magic = reader.ReadInt32(), aiVersion = reader.ReadInt32(); string policy = Text(); long capturedPublication = reader.ReadInt64();
                if (version != PlayableWorldState.CurrentVersion || seed != Seed || generation != Generation || tick != requestedTick || capturedPublication != publication || magic != 0x41495331 || source != expectedSource)
                    throw new InvalidOperationException("Requested/captured tick, publication or envelope binding mismatch.");
                return "schema=" + version + "\nseed=" + seed + "\ngeneration=" + generation + "\ncapturedTick=" + tick + "\npublication=" + capturedPublication +
                    "\nsource=" + source + "\naiVersion=" + aiVersion + "\nroutePolicy=" + policy + "\ncaptureBytes=" + bytes.Length + "\ncaptureSha256=" + Hash(0, bytes.Length) +
                    "\nprofileBindingSha256=" + Hash(binding.Start, binding.Count) + "\nconfigurationBindingSha256=" + Hash(config.Start, config.Count) +
                    "\nruntimeTransportSha256=" + Hash(transport.Start, transport.Count) + "\nrequestedTickEqualsCapturedTick=True\n";
            }
        }

        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object ReadMember(object value, string name) =>
            value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value) ??
            value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
        private static int CountMember(object value, string name)
        {
            var table = ReadMember(value, name);
            return table == null ? -1 : Convert.ToInt32(ReadMember(table, "Count"));
        }
        private static string CounterFacts(NavSchedulerCounters counters) => counters == null ? "none" :
            string.Join(";", typeof(NavSchedulerCounters).GetProperties().Select(p => p.Name + "=" + p.GetValue(counters)));

        private IEnumerator CaptureFailure(PlayableRuntime runtime, UnityHostRouteService host, OfflineMatchConfiguration config,
            PlayableProfile profile, string phase, long commandTick, long pumpStart, int serviceAttempts, long workerPumps,
            int[] ids, AuthorityWitness witness)
        {
            string facts;
            var latest = runtime.Latest;
            var domain = typeof(PlayableRuntime).GetField("domain", PrivateInstance).GetValue(runtime);
            var navigation = ReadMember(domain, "Navigation");
            var receipts = runtime.ReceiptsAfter(0).Where(r => r.Sequence == 1).Select(r => r.Status + "@" + r.AppliedTick + ":" + r.Message);
            var actors = latest?.Entities.Where(e => ids.Contains(e.Id)).Select(e => e.Id + ":" + e.Position.X + "," + e.Position.Z + ":moving=" + e.Moving + ":held=" + e.Held + ":outcome=" + e.NavigationOutcome + ":order=" + e.OrderStamp?.Sequence);
            facts = "B1_DENSE_PREP_FAIL phase=" + phase + " source=" + config.SourceIdentity + " map=" + config.MapIdentity +
                " seed=" + config.Seed + " profile=" + profile.ProfileId + "/" + profile.Revision +
                " producer=" + Environment.GetEnvironmentVariable("CROWD_DENSE_PRODUCER") + " generation=" + runtime.Generation +
                " currentTick=" + latest?.Tick + " commandTick=" + commandTick + " receipts=" + string.Join(";", receipts) +
                " serviceAttempts=" + serviceAttempts + " workerPumps=" + workerPumps + " pumpStart=" + pumpStart +
                " pending=" + CountMember(navigation, "pending") + " allocating=" + CountMember(navigation, "allocating") +
                " probes=" + CountMember(navigation, "probes") + " queuedRequests=" + ReadMember(ReadMember(navigation, "Requests"), "Count") +
                " answers=" + ReadMember(ReadMember(navigation, "Answers"), "Count") + " navigationPending=" + latest?.Metrics.NavigationPending +
                " projectionFailure=" + host.LastProjectionFailure + " workerFailure=" + host.CertifiedPublication?.Failure +
                " actors=" + string.Join(";", actors ?? Array.Empty<string>());
            if (host.CertifiedPublication != null)
                facts += " workerCounters=" + CounterFacts(host.CertifiedPublication.Counters);
            lock (witness.Gate) facts += " frames=" + witness.Frames.Count + " firstMotion=" + witness.FirstMotionTick +
                " activeMax=" + witness.Frames.Select(f => f.Active).DefaultIfEmpty().Max() +
                " movingMax=" + witness.Frames.Select(f => f.Moving).DefaultIfEmpty().Max() +
                " heldMax=" + witness.Frames.Select(f => f.Held).DefaultIfEmpty().Max() +
                " noProgressMax=" + witness.Frames.Select(f => f.MaxNoProgress).DefaultIfEmpty().Max() +
                " neighborCandidatesMax=" + witness.Frames.Select(f => f.NeighborCandidates).DefaultIfEmpty().Max();
            TestContext.WriteLine(facts);

            string evidence = Environment.GetEnvironmentVariable("CROWD_DENSE_EVIDENCE");
            if (string.IsNullOrEmpty(evidence)) { TestContext.WriteLine("B1_DENSE_CAPTURE_NOTRUN evidence-path-unset"); yield break; }
            runtime.RequestPause(true);
            double pauseBy = Time.realtimeSinceStartupAsDouble + 5;
            while (!runtime.Latest.Paused && Time.realtimeSinceStartupAsDouble < pauseBy) yield return new WaitForSecondsRealtime(.01f);
            var capture = runtime.RequestCaptureBytes();
            double captureBy = Time.realtimeSinceStartupAsDouble + 10;
            while (!capture.IsCompleted && Time.realtimeSinceStartupAsDouble < captureBy) yield return new WaitForSecondsRealtime(.01f);
            if (!capture.IsCompleted || capture.IsFaulted || capture.IsCanceled)
            {
                TestContext.WriteLine("B1_DENSE_CAPTURE_NOTRUN phase=" + phase + " currentTick=" + runtime.Latest?.Tick + " exception=" + capture.Exception);
                yield break;
            }
            var bytes = capture.Result;
            using (var state = PlayableWorldState.Decode(bytes))
            {
                TestContext.WriteLine("B1_DENSE_CAPTURE phase=" + phase + " envelopeSchema=" + state.Version + " seed=" + state.Seed +
                    " generation=" + state.Generation + " tick=" + state.Tick + " source=" + state.SourceIdentity + " map=" + config.MapIdentity +
                    " profile=" + profile.ProfileId + "/" + profile.Revision + " bindingBytes=" + state.Binding.Length +
                    " participantsBytes=" + state.Participants.Length + " producer=" + Environment.GetEnvironmentVariable("CROWD_DENSE_PRODUCER"));
            }
            System.IO.Directory.CreateDirectory(evidence);
            string path = System.IO.Path.Combine(evidence, "crowd-dense-terminal.bin");
            System.IO.File.WriteAllBytes(path, bytes);
            using (var sha = System.Security.Cryptography.SHA256.Create())
                TestContext.WriteLine("B1_DENSE_CAPTURE_FILE path=" + path + " bytes=" + bytes.Length + " sha256=" + BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
            if (Environment.GetEnvironmentVariable("CROWD_DENSE_CAPTURE_TERMINAL") == "1")
            {
                // Read-only evidence from the paused live world and the original
                // encoded authority header. No restore, DTO edits or preview.
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                string Cell(object value) => value is double d ? d.ToString("R", culture) : Convert.ToString(value, culture);
                string Hash(byte[] value) { using (var hash = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(value)).Replace("-", "").ToLowerInvariant(); }
                var table = new System.Text.StringBuilder("Id\tTeam\tCanYield\tX\tZ\tHeading\tMoving\tHeld\tOutcome\tGoalX\tGoalZ\tRadius\tSpeed\tTurnSpeed\tGlobalCount\tGlobalCursor\tLocalCount\tLocalCursor\tNoProgress\tBlockedSeconds\tIncarnation\tMobility\tGroup\tRevision\tRequest\tOrder\tDetached\tInstalledGlobalAlias\n");
                var crowd = ReadMember(navigation, "Crowd");
                foreach (var body in ((IEnumerable)ReadMember(crowd, "Units")).Cast<object>().OrderBy(u => (int)ReadMember(u, "Id")))
                {
                    var at = (NavPoint)ReadMember(body, "Position"); var goal = (NavPoint)ReadMember(body, "Goal");
                    var storage = ReadMember(body, "StorageOwner"); var installed = ReadMember(storage, "InstalledExecution");
                    var cells = new object[] { ReadMember(body, "Id"), ReadMember(body, "Team"), ReadMember(body, "CanYield"), at.X, at.Z, ReadMember(body, "Heading"), ReadMember(body, "Moving"), ReadMember(body, "Held"), ReadMember(body, "Outcome"), goal.X, goal.Z, ReadMember(body, "Radius"), ReadMember(body, "Speed"), ReadMember(body, "TurnSpeed"), ReadMember(ReadMember(body, "Route"), "Count"), ReadMember(body, "routeIndex"), ((Array)ReadMember(body, "localRoute")).Length, ReadMember(body, "localIndex"), ReadMember(body, "noProgressTicks"), ReadMember(body, "blockedSeconds"), ReadMember(body, "Incarnation"), ReadMember(body, "MobilityRevision"), ReadMember(body, "InstalledGroupId"), ReadMember(body, "InstalledRevision"), ReadMember(body, "InstalledRequest"), ReadMember(body, "InstalledOrder"), ReadMember(storage, "InstalledDetached"), installed != null && ReferenceEquals(ReadMember(installed, "Route"), ReadMember(storage, "Global")) };
                    table.AppendLine(string.Join("\t", cells.Select(Cell)));
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(evidence, "terminal-all-bodies.tsv"), table.ToString());
                FrameSummary[] actualFrames; lock (witness.Gate) actualFrames = witness.Frames.ToArray();
                var frameTable = new System.Text.StringBuilder("Tick\tBodies\tActive\tMoving\tHeld\tNoProgress\tNeighbors\tMoved\tArrived\tSpeedSafe\tTurnSafe\tSweepSafe\tStaticSafe\tBodiesSafe\n");
                foreach (var frame in actualFrames) frameTable.AppendLine(string.Join("\t", new object[] { frame.Tick, frame.TotalCrowdBodies, frame.Active, frame.Moving, frame.Held, frame.MaxNoProgress, frame.NeighborCandidates, frame.Moved, frame.Arrived, frame.SpeedSafe, frame.TurnSafe, frame.SweepSafe, frame.StaticSafe, frame.BodiesSafe }.Select(Cell)));
                System.IO.File.WriteAllText(System.IO.Path.Combine(evidence, "authority-frames.tsv"), frameTable.ToString());
                using (var state = PlayableWorldState.Decode(bytes))
                using (var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(state.AiAuthority, false)))
                {
                    int magic = reader.ReadInt32(), version = reader.ReadInt32();
                    var wire = typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.WorldWire", true);
                    var readString = wire.GetMethod("String", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(System.IO.BinaryReader) }, null);
                    string policy = (string)readString.Invoke(null, new object[] { reader }); long publication = reader.ReadInt64();
                    string metadata = facts + "\nphase=" + phase + "\nproducer=" + Environment.GetEnvironmentVariable("CROWD_DENSE_PRODUCER") + "\ncaptureBytes=" + bytes.Length + "\ncaptureSha256=" + Hash(bytes) + "\nactualCaptureTick=" + state.Tick + "\nactualLiveDomainTick=" + ReadMember(domain, "Tick") + "\nactualCapturedPublication=" + publication + "\naiMagic=" + magic + "\naiVersion=" + version + "\nroutePolicy=" + policy + "\nprofileBindingSha256=" + Hash(state.Binding) + "\nconfigurationBindingSha256=" + Hash(state.Participants) + "\nruntimeTransportSha256=" + Hash(state.RuntimeTransport) + "\noriginalCommandTick=" + commandTick + "\nfirstMotionTick=" + witness.FirstMotionTick + "\nlastWitnessTick=" + actualFrames.Last().Tick + "\nlastWitnessArrived=" + actualFrames.Last().Arrived + "\n";
                    System.IO.File.WriteAllText(System.IO.Path.Combine(evidence, "terminal-capture-metadata.txt"), metadata);
                    TestContext.WriteLine("B1_DENSE_DIAGNOSTIC_SAVED tick=" + state.Tick + " publication=" + publication + " allBodies=" + actualFrames.Last().TotalCrowdBodies + " lastWitnessArrived=" + actualFrames.Last().Arrived);
                }
            }
        }

        [UnityTest, Timeout(180000)] public IEnumerator OrdinaryNativeFlatTwelveTankGroupMovesAndArrivesThroughThreadedHost() => RunWitness(false);
        [UnityTest, Timeout(180000)] public IEnumerator OrdinaryNativeFlatCompactFourByThreeGroupMovesAndArrivesThroughThreadedHost() => RunWitness(true);

        [UnityTest, Explicit, Timeout(180000)]
        public IEnumerator DiagnosticNativeFlatCompactFirstBlockedTransitionCapturesAdjacentAuthorityCheckpoints()
        {
            string folder = Environment.GetEnvironmentVariable("CROWD_DENSE_EVIDENCE");
            string producer = Environment.GetEnvironmentVariable("CROWD_DENSE_PRODUCER");
            Assert.False(string.IsNullOrEmpty(folder), "diagnostic capture requires an owned evidence path");
            Assert.True(producer != null && producer.Length == 40 && producer.All(Uri.IsHexDigit), "diagnostic producer must be the frozen full QA source SHA");
            var diagnostic = new TransitionCapture(System.IO.Path.Combine(folder, "first-blocked-transition"), producer);
            var ordinary = RunWitness(true);
            transitionDiagnostic = diagnostic;
            Exception originalFailure = null;
            try
            {
                // Drive the unchanged ordinary iterator, preserving its exception
                // while the completed pair is saved before its runtime is stopped.
                while (true)
                {
                    bool next = false; object current = null;
                    try { next = ordinary.MoveNext(); if (next) current = ordinary.Current; }
                    catch (Exception ex) { originalFailure = ex; }
                    if (originalFailure != null || !next) break;
                    yield return current;
                }
                while (!diagnostic.SealAndIsSettled()) yield return null;
                var save = diagnostic.SaveCompleted();
                while (!save.IsCompleted) yield return null;
                TestContext.WriteLine("B12_TRANSITION_MEASUREMENT " + System.IO.Path.Combine(folder, "first-blocked-transition", "transition-measurement.txt"));
                if (save.IsFaulted) TestContext.WriteLine("B12_TRANSITION_SAVE_FAILED " + save.Exception);
                // The ordinary NUnit FAIL remains the primary result even if
                // diagnostic I/O failed. Measurement is independently classified.
                if (originalFailure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originalFailure).Throw();
                if (save.IsFaulted) throw save.Exception;
            }
            finally
            {
                (ordinary as IDisposable)?.Dispose();
                // Timeout/early-disposal path saves only what has completed; it
                // never waits in or serializes live authority during teardown.
                try { diagnostic.SaveCompleted(); diagnostic.Close(); }
                finally { transitionDiagnostic = null; }
            }
        }

        private IEnumerator RunWitness(bool compact)
        {
            var profile = PlayableProfile.Default;
            bool captureTerminalDiagnostic = compact && Environment.GetEnvironmentVariable("CROWD_DENSE_CAPTURE_TERMINAL") == "1";
            if (captureTerminalDiagnostic)
            {
                Assert.False(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CROWD_DENSE_EVIDENCE")), "diagnostic capture requires an owned evidence path");
                string producer = Environment.GetEnvironmentVariable("CROWD_DENSE_PRODUCER");
                Assert.True(producer != null && producer.Length == 40 && producer.All(Uri.IsHexDigit), "diagnostic producer must be the frozen full QA source SHA");
            }
            Assert.LessOrEqual(profile.NavigationRequestsPerFrame, 8, "frozen profile route quantum must fit caller budget 8");
            var probeConfig = Configuration(profile, Array.Empty<OfflineScenarioUnit>());
            NavPoint[] origins;
            using (var probe = new PlayableRuntime(probeConfig, Generation, startPaused: true))
            {
                double readyBy = Time.realtimeSinceStartupAsDouble + 10;
                while (probe.NavigationGeometry == null && Time.realtimeSinceStartupAsDouble < readyBy) yield return null;
                Assert.NotNull(probe.NavigationGeometry, "native-flat map geometry must initialize before choosing legal origins");
                origins = (from z in GridAxis()
                           from x in GridAxis()
                           let at = new NavPoint(x, z)
                           where probe.NavigationGeometry.IsFree(at, profile.TankCollisionRadius)
                           select at).Take(12).ToArray();
                if (compact)
                {
                    double[] axis = GridAxis();
                    origins = (from zi in Enumerable.Range(0, 3)
                               from xi in Enumerable.Range(0, 4)
                               select new NavPoint(axis[xi], axis[zi])).ToArray();
                    TestContext.WriteLine("B1_DENSE_COMPACT_LEGAL " + string.Join(";", origins.Select(p => p.X + ":" + p.Z + ":" + probe.NavigationGeometry.IsFree(p, profile.TankCollisionRadius))));
                    Assert.AreEqual(12, origins.Length, "exact 4x3 block from the frozen original grid");
                    Assert.True(origins.All(p => probe.NavigationGeometry.IsFree(p, profile.TankCollisionRadius)), "all exact compact origins must be legal; no skipping or replacement");
                }
                else Assert.AreEqual(12, origins.Length, "first 12 legal origins on the frozen 3.2 grid");
                probe.RequestStop();
                while (!probe.IsStopped && Time.realtimeSinceStartupAsDouble < readyBy) yield return null;
                Assert.True(probe.IsStopped, "geometry probe runtime must stop before the witness starts");
            }

            var c = Configuration(profile, origins.Select(at => new OfflineScenarioUnit(1, PlayableEntityKind.Tank, at)).ToArray());
            var runtime = new PlayableRuntime(c, Generation, startPaused: true);
            var host = new UnityHostRouteService();
            transitionDiagnostic?.Own(runtime, host);
            var witness = new AuthorityWitness();
            int[] ids = null;
            NavPoint[] starts = origins;
            long order = 1;
            long commandTick = -1, firstMotion = -1, lastTick = -1;
            int observedTicks = 0, gaps = 0, maxNeighborCandidates = 0;
            bool arrived = false, moved = false;
            int serviceAttempts = 0, freshPublications = 0;
            long workerPumpStart = 0, workerPumpDelta = 0;
            try
            {
                double deadline = Time.realtimeSinceStartupAsDouble + 180;
                while ((!runtime.IsPrepared || runtime.Latest == null || runtime.Latest.Sequence == 0) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.True(runtime.IsPrepared, "ordinary native runtime preparation within 180-second harness guard");
                ids = runtime.Latest.Entities.Where(e => e.Owner == PlayableOwner.Player && e.Kind == PlayableEntityKind.Tank).OrderBy(e => e.Id).Select(e => e.Id).ToArray();
                Assert.AreEqual(12, ids.Length, "actual configured twelve Tank roster");
                CollectionAssert.AreEquivalent(new[] { OfflineControl.Human, OfflineControl.Human }, c.Roster.Select(p => p.Control));
                Assert.AreEqual(12, c.ScenarioUnits.Count);
                Assert.True(c.ScenarioUnits.All(u => u.LogicalPlayer == 1 && u.Kind == PlayableEntityKind.Tank), "no enemy scenario units or additional kinds");
                if (compact) CollectionAssert.AreEqual(origins, c.ScenarioUnits.Select(u => u.Position).ToArray(), "exact compact grid binding");
                Assert.Zero(c.Obstacles.Count, "no extra obstacles");
                CollectionAssert.AreEqual(starts, runtime.Latest.Entities.Where(e => ids.Contains(e.Id)).OrderBy(e => e.Id).Select(e => e.Position).ToArray());
                Assert.True(runtime.Latest.Entities.Where(e => ids.Contains(e.Id)).All(e => !e.Held), "no held group members");
                witness.Attach(runtime, ids, starts, transitionDiagnostic);
                host.Service(runtime, 8); serviceAttempts++;
                workerPumpStart = host.CertifiedPublication?.Pumps ?? 0;
                runtime.RequestPause(false);
                double resumeBy = Time.realtimeSinceStartupAsDouble + 5;
                while (runtime.Latest.Paused && Time.realtimeSinceStartupAsDouble < resumeBy) yield return null;
                Assert.False(runtime.Latest.Paused, "ordinary runtime must be active before human command ingress");
                var submit = runtime.TrySubmit(new PlayableCommand(Generation, order, c.Roster[0].Id, PlayableCommandKind.Move, ids, Goal));
                Assert.True(submit.Accepted, "real human Move must be admitted: " + submit.Status);
                double prepBy = Time.realtimeSinceStartupAsDouble + 60;
                double nextHostServiceAt = Time.realtimeSinceStartupAsDouble + .001;
                long lastPublicationPumps = workerPumpStart;
                bool Ready() => runtime.Latest.Metrics.NavigationPending == 0 &&
                    ids.All(id => runtime.Latest.Entities.Single(e => e.Id == id).OrderStamp?.Sequence == order) &&
                    runtime.ReceiptsAfter(0).Any(r => r.Sequence == order && r.Status == PlayableCommandStatus.Applied);
                while (Time.realtimeSinceStartupAsDouble < prepBy && workerPumpDelta < 4096 && !Ready())
                {
                    if (Time.realtimeSinceStartupAsDouble >= nextHostServiceAt)
                    {
                        host.Service(runtime, 8); serviceAttempts++;
                        nextHostServiceAt = Time.realtimeSinceStartupAsDouble + .001;
                    }
                    var receipts = runtime.ReceiptsAfter(0);
                    var receipt = receipts.SingleOrDefault(r => r.Sequence == order && r.Status != PlayableCommandStatus.Accepted);
                    if (receipt != null && receipt.Status == PlayableCommandStatus.Applied && commandTick < 0) commandTick = receipt.AppliedTick;
                    var publication = host.CertifiedPublication;
                    long currentPumps = publication?.Pumps ?? workerPumpStart;
                    workerPumpDelta = currentPumps - workerPumpStart;
                    if (currentPumps != lastPublicationPumps) { lastPublicationPumps = currentPumps; freshPublications++; }
                    yield return new WaitForSecondsRealtime(.001f);
                }
                workerPumpDelta = (host.CertifiedPublication?.Pumps ?? workerPumpStart) - workerPumpStart;
                var appliedReceipt = runtime.ReceiptsAfter(0).SingleOrDefault(r => r.Sequence == order && r.Status == PlayableCommandStatus.Applied);
                if (appliedReceipt != null && commandTick < 0) commandTick = appliedReceipt.AppliedTick;
                bool prepared = Ready();
                if (!prepared)
                {
                    TestContext.WriteLine("B1_DENSE_PREP_SUMMARY serviceAttempts=" + serviceAttempts + " freshPublications=" + freshPublications +
                        " workerPumps=" + workerPumpDelta + " pumpCap=4096 profileQuantum=" + profile.NavigationRequestsPerFrame +
                        " callerBudget=8 wallElapsed=" + (Time.realtimeSinceStartupAsDouble - (prepBy - 60)) + " pending=" + runtime.Latest.Metrics.NavigationPending);
                    if (transitionDiagnostic != null) yield return transitionDiagnostic.Flush();
                    yield return CaptureFailure(runtime, host, c, profile, "route-preparation", commandTick, workerPumpStart, serviceAttempts, workerPumpDelta, ids, witness);
                }
                Assert.LessOrEqual(workerPumpDelta, 4096, "unchanged ordinary preparation worker-pump cap");
                Assert.True(prepared, "route preparation must complete within unchanged 60-second / 4096 worker-pump limits");
                Assert.GreaterOrEqual(commandTick, 0, "applied receipt tick");
                Assert.True(ids.All(id => runtime.Latest.Entities.Single(e => e.Id == id).OrderStamp?.Sequence == order), "all twelve retain the current Move order");

                while (Time.realtimeSinceStartupAsDouble < deadline)
                {
                    if (Time.realtimeSinceStartupAsDouble >= nextHostServiceAt)
                    {
                        host.Service(runtime, 8); serviceAttempts++;
                        nextHostServiceAt = Time.realtimeSinceStartupAsDouble + .001;
                    }
                    workerPumpDelta = (host.CertifiedPublication?.Pumps ?? workerPumpStart) - workerPumpStart;
                    var snapshot = runtime.Latest;
                    Assert.IsNull(snapshot.Failure, snapshot.Failure);
                    Assert.False(runtime.IsStopped);
                    if (snapshot.Tick == lastTick) { yield return new WaitForSecondsRealtime(.001f); continue; }
                    if (lastTick >= 0 && snapshot.Tick != lastTick + 1) gaps++;
                    observedTicks++;
                    var actors = snapshot.Entities.Where(e => ids.Contains(e.Id)).OrderBy(e => e.Id).ToArray();
                    Assert.AreEqual(12, actors.Length);
                    bool anyDisplaced = false;
                    for (int i = 0; i < actors.Length; i++)
                    {
                        var a = actors[i];
                        double dx = a.Position.X - starts[i].X, dz = a.Position.Z - starts[i].Z;
                        if (dx * dx + dz * dz > 1e-8) anyDisplaced = true;
                        if (a.OrderStamp?.Sequence == order && firstMotion < 0 && anyDisplaced) firstMotion = snapshot.Tick;
                        Assert.AreEqual(order, a.OrderStamp?.Sequence, "current order retained");
                        Assert.AreEqual(PlayableOrderOrigin.Human, a.OrderStamp?.Origin);
                        Assert.True(runtime.NavigationGeometry.IsFree(a.Position, profile.TankCollisionRadius), "static footprint safety");
                        Assert.LessOrEqual(Math.Sqrt(dx * dx + dz * dz), PlayableUnitRules.Speed(profile, a.Kind) * Math.Max(0, snapshot.Tick - commandTick) / 30d + 1e-6, "bounded aggregate progress");
                        for (int j = 0; j < i; j++)
                        {
                            double sx = a.Position.X - actors[j].Position.X, sz = a.Position.Z - actors[j].Position.Z;
                            Assert.GreaterOrEqual(sx * sx + sz * sz, 4 * profile.TankCollisionRadius * profile.TankCollisionRadius - 1e-8, "body separation");
                        }
                    }
                    if (anyDisplaced) moved = true;
                    lock (witness.Gate) if (witness.Frames.Count > 0) maxNeighborCandidates = Math.Max(maxNeighborCandidates, witness.Frames[witness.Frames.Count - 1].NeighborCandidates);
                    arrived = actors.All(a => a.NavigationOutcome == NavigationOutcome.Arrived);
                    lastTick = snapshot.Tick;
                    lock (witness.Gate) firstMotion = witness.FirstMotionTick;
                    FrameSummary latestFrame = null;
                    lock (witness.Gate) if (witness.Frames.Count > 0) latestFrame = witness.Frames[witness.Frames.Count - 1];
                    bool witnessedArrival = latestFrame != null && latestFrame.Arrived == 12;
                    bool tickLimitReached = latestFrame != null && (compact
                        ? latestFrame.Tick - commandTick >= 2700
                        : firstMotion >= 0 && latestFrame.Tick - firstMotion >= 2700);
                    if (arrived || witnessedArrival || tickLimitReached) break;
                    yield return new WaitForSecondsRealtime(.001f);
                }

                FrameSummary[] frames;
                lock (witness.Gate) frames = witness.Frames.ToArray();
                TestContext.WriteLine("B1_DENSE_GROUP source=" + c.SourceIdentity + " map=" + c.MapIdentity + " seed=" + c.Seed +
                    " profile=" + profile.ProfileId + "/" + profile.Revision + " producer=" + Environment.GetEnvironmentVariable("CROWD_DENSE_PRODUCER") +
                    " scenario=" + (compact ? "compact-4x3" : "row-first-12") + " currentNavigationPending=" + runtime.Latest.Metrics.NavigationPending +
                    " generation=" + Generation + " origins=" + string.Join(";", starts.Select(p => p.X + ":" + p.Z)) +
                    " goal=" + Goal.X + ":" + Goal.Z + " order=" + order + " appliedTick=" + commandTick + " firstMotion=" + firstMotion +
                    " currentTick=" + runtime.Latest.Tick + " ticks=" + observedTicks + " gaps=" + gaps + " moved=" + moved + " arrived=" + arrived +
                    " serviceAttempts=" + serviceAttempts + " freshPublications=" + freshPublications + " workerPumpStart=" + workerPumpStart +
                    " workerPumps=" + workerPumpDelta + " workerFailure=" + host.CertifiedPublication?.Failure +
                    " neighborCandidatesMax=" + maxNeighborCandidates + " activeMax=" + frames.Select(f => f.Active).DefaultIfEmpty().Max() +
                    " heldMax=" + frames.Select(f => f.Held).DefaultIfEmpty().Max() + " noProgressMax=" + frames.Select(f => f.MaxNoProgress).DefaultIfEmpty().Max() +
                    " movedMax=" + frames.Select(f => f.Moved).DefaultIfEmpty().Max() + " arrivedMax=" + frames.Select(f => f.Arrived).DefaultIfEmpty().Max() +
                    " totalCrowdBodies=" + frames.Select(f => f.TotalCrowdBodies).DefaultIfEmpty().Max() + " neighborCap=" + profile.Navigation.CrowdNeighborCap +
                    " neighborAtTheoreticalMaxFrames=" + frames.Count(f => f.TotalCrowdBodies > 0 && f.NeighborCandidates == f.TotalCrowdBodies * (profile.Navigation.CrowdNeighborCap + 1)) +
                    " displacementMax=" + frames.Select(f => f.MaxDisplacement).DefaultIfEmpty().Max().ToString("R") +
                    " safety=speed:" + frames.All(f => f.SpeedSafe) + ",turn:" + frames.All(f => f.TurnSafe) + ",sweep:" + frames.All(f => f.SweepSafe) +
                    ",static:" + frames.All(f => f.StaticSafe) + ",bodies:" + frames.All(f => f.BodiesSafe));

                if (transitionDiagnostic != null) yield return transitionDiagnostic.Flush(arrived || frames.Last().Tick - commandTick >= 2700);
                if (!arrived || captureTerminalDiagnostic) yield return CaptureFailure(runtime, host, c, profile, arrived ? "movement-terminal-arrived" : "movement-terminal", commandTick, workerPumpStart, serviceAttempts, workerPumpDelta, ids, witness);
                Assert.True(moved, "all members must show real movement after command admission");
                Assert.True(arrived, compact
                    ? "all twelve current-order Tanks must arrive within 2700 game ticks from command admission; ticks=" + observedTicks
                    : "all twelve current-order Tanks must arrive within 2700 ticks after first movement; ticks=" + observedTicks);
                Assert.GreaterOrEqual(firstMotion, 0, "first movement tick from authority hook");
                Assert.GreaterOrEqual(compact ? frames.Last().Tick - commandTick : frames.Last().Tick - firstMotion,
                    arrived ? 0 : 2700, compact ? "authority-hook game tick window from command admission" : "authority-hook game tick window after first movement");
                Assert.True(frames.All(f => f.SpeedSafe), "per-tick speed contract");
                Assert.True(frames.All(f => f.TurnSafe), "per-tick turn contract");
                Assert.True(frames.All(f => f.SweepSafe), "per-tick swept geometry contract");
                Assert.True(frames.All(f => f.StaticSafe), "static footprint contract");
                Assert.True(frames.All(f => f.BodiesSafe), "continuous body separation contract");
                Assert.AreEqual(12, frames.Select(f => f.Moved).DefaultIfEmpty().Max(), "each member must move");
                Assert.AreEqual(12, frames.Select(f => f.Arrived).DefaultIfEmpty().Max(), "all members must reach Arrived");
            }
            finally { if (transitionDiagnostic == null) { runtime.RequestStop(); host.Dispose(); runtime.Dispose(); } }
        }

        private static double[] GridAxis()
        {
            var values = new System.Collections.Generic.List<double>();
            for (double n = -20; n <= 20; n += 3.2) values.Add(n);
            return values.ToArray();
        }
    }
}

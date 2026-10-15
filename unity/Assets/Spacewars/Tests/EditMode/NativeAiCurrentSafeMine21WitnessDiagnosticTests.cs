using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiCurrentSafeMine21WitnessDiagnosticTests
    {
        private const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Archive = "docs/native-ai/q4/original-natural-20261013-source-compiled-raw.zip";
        private const string ArchiveHash = "bed495a3a55ae9d2b0507cc6f9d91c23f37bfee6e79dd9e099c51263eb81e154";
        private const string Entry = ".local/o6-natural/7109-checkpoint-600.world";
        private const string InputHash = "d6625a566d7f1845f676ca6b718e741be7f3ae495ec46d866c1716398b0a654b";
        private const string Owner = "ai-0";
        private const int StartTick = 600, EndTick = 630, Site = 21, ReferenceSite = 9;

        private static string Root()
        {
            var d = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (d != null && !File.Exists(Path.Combine(d.FullName, "workflow/project.json"))) d = d.Parent;
            Assert.NotNull(d);
            return d.FullName;
        }

        private static object Get(object x, string name)
        {
            var f = x.GetType().GetField(name, F);
            Assert.NotNull(f, name);
            return f.GetValue(x);
        }

        private static object GetMember(object x, string name)
        {
            var field = x.GetType().GetField(name, F);
            if (field != null) return field.GetValue(x);
            var property = x.GetType().GetProperty(name, F);
            Assert.NotNull(property, name);
            return property.GetValue(x, null);
        }

        private static object ActualPlannerSuppression(PlayableAuthorityTick authority)
        {
            var scheduler = Get(authority, "scheduler");
            var owners = (Array)Get(scheduler, "owners");
            var owner = owners.Cast<object>().Single(x => (string)GetMember(x, "OwnerId") == Owner);
            var planner = Get(owner, "expansion");
            var failed = (System.Collections.IDictionary)Get(planner, "failed");
            return failed.Cast<System.Collections.DictionaryEntry>().OrderBy(x => (int)x.Key)
                .Select(x => new { Site = (int)x.Key, ConditionFingerprint = (string)x.Value }).ToArray();
        }

        private static void Advance(PlayableAuthorityTick a, UnityHostRouteService host)
        {
            for (int i = 0; i < 512; i++)
            {
                if (a.TryAdvance()) return;
                host.Service(a, 64);
            }
            Assert.Fail("ordinary typed-route barrier timed out at " + a.Tick);
        }

        private static void AssertBytesEqual(byte[] expected, byte[] actual, string message)
        {
            if (expected.Length != actual.Length) Assert.Fail(message + " length " + expected.Length + " != " + actual.Length);
            for (int i = 0; i < expected.Length; i++) if (expected[i] != actual[i]) Assert.Fail(message + " first difference " + i);
        }

        private static string AuthorityFacts(object domain, FieldInfo projection, FieldInfo requests, FieldInfo ownerFilter, FieldInfo capture)
        {
            var p = projection.GetValue(domain); var r = requests.GetValue(domain); var o = ownerFilter.GetValue(domain); var c = capture.GetValue(domain);
            try
            {
                projection.SetValue(domain, null); requests.SetValue(domain, null); ownerFilter.SetValue(domain, null); capture.SetValue(domain, false);
                return PlayableWorldRestoreTests.Facts(domain, true);
            }
            finally
            {
                projection.SetValue(domain, p); requests.SetValue(domain, r); ownerFilter.SetValue(domain, o); capture.SetValue(domain, c);
            }
        }

        private static byte[] ReadEntry(string path, string entry)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var e = zip.GetEntry(entry); Assert.NotNull(e, entry);
                using (var s = e.Open()) using (var m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
            }
        }

        private static object Projection(PlayableRouteProjectionReadback r) => new
        {
            r.OwnerId, Owner = r.Owner.ToString(), r.UnitId, Kind = r.Kind.ToString(), r.TargetId, r.Generation, r.Tick,
            r.Origin, r.Radius, r.RequestedTarget, Outcome = r.Outcome.ToString(), r.BindingRejection, r.ResolvedTarget,
            r.OriginVisible, r.TargetVisible, r.ObservedGeometryRevision, r.ObservedObstacleCount, r.CandidateCount,
            Candidates = r.Candidates.Select(c => new
            {
                c.Index, c.Candidate, c.Visible, Outcome = c.Outcome.ToString(), c.Path, c.FailedSegmentIndex,
                c.FailedFrom, c.FailedTo, c.AuthoritativeSegmentFree, c.VisibleSegment
            }).ToArray()
        };

        private sealed class Tick
        {
            internal int Number;
            internal byte[] World;
            internal string Facts, Views;
            internal PlayableAiOwnerCheckpoint[] Checkpoints;
            internal AiBudgetState[] Budgets;
        }

        private sealed class Run
        {
            internal Tick[] Ticks;
            internal List<string> Timeline = new List<string>();
            internal List<string> RouteRequests = new List<string>();
            internal List<string> RouteProjections = new List<string>();
            internal List<string> Decisions = new List<string>();
            internal List<string> Finance = new List<string>();
            internal List<string> PlannerGuards = new List<string>();
            internal object ObserverBindingProof;
            internal int ActualDecisionInput630Count;
            internal int ActualRequest21Count;
            internal int Projection21Count;
            internal int ActualRequest21At630Count;
            internal int Projection21At630Count;
            internal object Tick630Input;
            internal int PreallocationInput630Count;
            internal object PreallocationInput630;
            internal object PlannerSuppression630;
        }

        [Test]
        public void OriginalSafe7109_600To630_CapturesActualMine21WitnessInputWithoutAuthorityChange()
        {
            var root = Root();
            var archive = Path.Combine(root, Archive);
            Assert.AreEqual(ArchiveHash, Wire.Hash(File.ReadAllBytes(archive)), "immutable original Natural packet");
            var input = ReadEntry(archive, Entry);
            Assert.AreEqual(InputHash, Wire.Hash(input), "exact checkpoint member");

            var profile = PlayableProfile.Create(PlayableProfile.Default.CopyData(), new AiTestMap());
            var participants = Enumerable.Range(0, 8).Select(i => new OfflineParticipant("ai-" + i, i + 1, i + 1, OfflineControl.Ai)).ToArray();
            var config = ((AiTestMap)profile.AuthoredMap).Configuration(profile, 7109, participants);
            Assert.AreEqual(8, config.Roster.Count); Assert.IsEmpty(config.ScenarioUnits); Assert.IsEmpty(config.ScenarioBuildings);

            Run Execute(bool enabled, Run expected = null)
            {
                var authority = PlayableAuthorityTick.RestoreBytes(input, config);
                var run = new Run { Ticks = new Tick[EndTick - StartTick] };
                object domain = Get(authority, "domain"); var domainType = domain.GetType();
                var routeProjection = domainType.GetField("RouteProjectionReadbackForTests", F);
                var routeRequests = domainType.GetField("RouteRequestsReadbackForTests", F);
                var routeOwner = domainType.GetField("RouteProjectionOwnerFilterForTests", F);
                var routeCapture = domainType.GetField("CaptureCurrentExpansionRoutesForTests", F);
                Assert.NotNull(routeProjection); Assert.NotNull(routeRequests); Assert.NotNull(routeOwner); Assert.NotNull(routeCapture);
                int callbackAuthorityTick = -1;
                void Trace(List<string> rows, object row) => rows.Add(JsonConvert.SerializeObject(row));

                try
                {
                    Assert.AreEqual(StartTick, authority.Tick); AssertBytesEqual(input, authority.CaptureBytes(), "immediate exact restore");
                    var initial = authority.CaptureDiagnosticCheckpoints().Single(x => x.OwnerId == Owner);
                    Assert.AreEqual(7109, initial.Seed); Assert.AreEqual(71, initial.Generation); Assert.AreEqual("Safe", initial.Opening.Opening.ToString());

                    AiExpansionFinancial.ActualLifecycleReadbackOwnerFilterForTests = enabled ? Owner : null;
                    AiExpansionFinancial.ActualLifecycleReadbackForTests = enabled
                        ? (Action<string, long, string, int, AiBudgetEntry, AiBudgetEntry, AiBudgetState>)((who, tick, stage, site, before, after, budget) =>
                        {
                            if (who == Owner) Trace(run.Finance, new { Owner = who, Tick = tick, Stage = "Lifecycle." + stage, Site = site, Before = before, After = after, Budget = budget });
                        }) : null;
                    AiExpansionFinancial.ActualDecisionReadbackOwnerFilterForTests = enabled ? Owner : null;
                    AiExpansionFinancial.ActualDecisionReadbackForTests = enabled ? (Action<object>)(row => Trace(run.Finance, new { Stage = "FinancialDecision", Readback = row })) : null;
                    AiExpansionFinancial.ActualBestSearchOwnerFilterForTests = enabled ? Owner : null;
                    AiExpansionFinancial.ActualBestSearchReadbackForTests = enabled ? (Action<object>)(row => Trace(run.Finance, new { Stage = "ActualBestSearch", Readback = row })) : null;

                    PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests = enabled ? Owner : null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests = enabled ? (Action<CurrentExpansionDecisionReadback>)(r =>
                    {
                        if (r.Observation.Tick != EndTick) return;
                        run.ActualDecisionInput630Count++;
                        var beforeReadBytes = authority.CaptureBytes();
                        var beforeReadFacts = AuthorityFacts(domain, routeProjection, routeRequests, routeOwner, routeCapture);
                        var suppressionState = ActualPlannerSuppression(authority);
                        var afterReadBytes = authority.CaptureBytes();
                        var afterReadFacts = AuthorityFacts(domain, routeProjection, routeRequests, routeOwner, routeCapture);
                        AssertBytesEqual(beforeReadBytes, afterReadBytes, "read-only reflection of real ai-0 suppression state preserves authority bytes");
                        Assert.AreEqual(beforeReadFacts, afterReadFacts, "read-only reflection of real ai-0 suppression state preserves authority facts");
                        run.PlannerSuppression630 = new
                        {
                            Source = "read-only reflection of the existing scheduler owner ai-0 expansion.failed dictionary",
                            CaptureStage = "CurrentExpansionDecisionReadback callback; ordinary selection completed, selected intents not yet committed",
                            FailedSiteFingerprints = suppressionState,
                            BeforeAfterByteAndFullFactEqual = true
                        };
                        run.Tick630Input = new
                        {
                            CaptureStage = "callback emitted after ordinary selection; Observation is the immutable owner-policy input captured before allocation",
                            AuthorityTickAtCallback = callbackAuthorityTick,
                            Observation = r.Observation,
                            ObservationIdentity = r.Observation.Identity,
                            RouteProof21 = r.Observation.RouteProofs.Where(p => p.TargetId == Site && p.Kind == PlayableRouteTargetKind.PublicObjective).ToArray(),
                            Site21 = r.Observation.Sites.FirstOrDefault(s => s.Site.Id == Site),
                            Site9 = r.Observation.Sites.FirstOrDefault(s => s.Site.Id == ReferenceSite),
                            VisibleThreats = new
                            {
                                ActualThreatIds = r.ThreatIds,
                                HostileEntities = r.Observation.Entities.Where(e => r.Observation.IsHostile(e.Owner) && e.Health > 0).ToArray(),
                                HostileBuildings = r.Observation.Buildings.Where(b => r.Observation.IsHostile(b.Owner) && b.Health > 0).ToArray()
                            },
                            PreallocationInput = run.PreallocationInput630,
                            PreselectionClaims = run.PreallocationInput630 == null ? null : ((JObject)run.PreallocationInput630)["Data"]?["ActorClaims"],
                            PostSelectionState = new { r.ArmyActors, r.PendingActors, Expansion = r.ExpansionState },
                            Finance = new
                            {
                                r.FinanceSite, r.FinanceReady, r.ProtectedFunds, r.Threatened, r.UrgentRepair,
                                r.AllowsExpansion, r.MissingScout, r.EssentialScale, r.ScalingReason, r.ScalingStartup,
                                r.BeforeFinanceDetach, r.AfterTechnologyAllocation, r.AfterFinancialAllocation, r.Ledger
                            },
                            ActualDecision = new
                            {
                                r.ExpansionAction, r.ExpansionIntent, r.Proposals, r.Selected, r.Rejections,
                                r.ExpansionState, r.ThreatIds, r.ArmyActors, r.PendingActors
                            },
                            ActualPlannerSuppressionState = run.PlannerSuppression630
                        };
                        Trace(run.Decisions, run.Tick630Input);
                    }) : null;

                    PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests = enabled ? Owner : null;
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests = enabled ? new Action<string>(raw =>
                    {
                        var payload = JObject.Parse(raw);
                        if ((long?)payload["Tick"] == EndTick && (string)payload["Stage"] == "ExpansionFinancialAdmissionInputs")
                        {
                            run.PreallocationInput630Count++;
                            run.PreallocationInput630 = payload;
                        }
                    }) : null;

                    AiExpansionPlannerGuardObserverForTests.OwnerFilter = enabled ? Owner : null;
                    AiExpansionPlannerGuardObserverForTests.Observer = enabled
                        ? (Action<string, long, int, int, string, bool>)((who, tick, site, actor, stage, result) =>
                        { if (who == Owner && site == Site) Trace(run.PlannerGuards, new { Owner = who, Tick = tick, Site = site, Actor = actor, Stage = stage, Result = result, Source = "ordinary actual planner guard" }); }) : null;
                    AiExpansionPlannerGuardObserverForTests.SafetyObserver = enabled
                        ? (Action<string, long, int, int, int, NavPoint, double, double, string>)((who, tick, site, actor, threat, point, distance, range, kind) =>
                        { if (who == Owner && site == Site) Trace(run.PlannerGuards, new { Owner = who, Tick = tick, Site = site, Actor = actor, Threat = threat, Point = point, Distance = distance, Range = range, Kind = kind, Source = "ordinary actual safety check" }); }) : null;

                    routeOwner.SetValue(domain, enabled ? Owner : null);
                    routeCapture.SetValue(domain, enabled);
                    routeProjection.SetValue(domain, enabled ? (object)new Action<PlayableRouteProjectionReadback>(r =>
                    {
                        if (r.OwnerId != Owner || r.Tick < StartTick || r.Tick > EndTick) return;
                        if (r.TargetId == Site)
                        {
                            run.Projection21Count++;
                            if (r.Tick == EndTick) run.Projection21At630Count++;
                        }
                        Trace(run.RouteProjections, new { AuthorityTickAtCallback = callbackAuthorityTick, Readback = Projection(r) });
                    }) : null);
                    routeRequests.SetValue(domain, enabled ? (object)new Action<string, long, PlayableRouteRequest[]>((who, tick, requests) =>
                    {
                        if (who != Owner || tick < StartTick || tick > EndTick) return;
                        var publicRows = requests.Where(r => r.Kind == PlayableRouteTargetKind.PublicObjective).ToArray();
                        run.ActualRequest21Count += publicRows.Count(r => r.TargetId == Site);
                        run.ActualRequest21At630Count += publicRows.Count(r => r.TargetId == Site && r.Tick == EndTick);
                        Trace(run.RouteRequests, new
                        {
                            AuthorityTickAtCallback = callbackAuthorityTick, Owner = who, Tick = tick,
                            Requests = publicRows.Select(r => new { r.UnitId, Kind = r.Kind.ToString(), r.TargetId, r.Generation, r.Tick, r.Origin, r.Radius, r.Target }).ToArray()
                        });
                    }) : null);

                    using (var host = new UnityHostRouteService())
                    {
                        for (int i = 0; i < EndTick - StartTick; i++)
                        {
                            callbackAuthorityTick = (int)authority.Tick;
                            var traceOffset = run.RouteProjections.Count;
                            Advance(authority, host);
                            Assert.AreEqual(StartTick + i + 1, authority.Tick);
                            var world = authority.CaptureBytes();
                            var facts = AuthorityFacts(domain, routeProjection, routeRequests, routeOwner, routeCapture);
                            if (enabled && i == 0)
                            {
                                var twin = PlayableAuthorityTick.RestoreBytes(world, config);
                                try
                                {
                                    var twinDomain = Get(twin, "domain");
                                    var hookedFacts = PlayableWorldRestoreTests.Facts(domain, true);
                                    var twinFacts = PlayableWorldRestoreTests.Facts(twinDomain, true);
                                    var diff = typeof(OfflineParticipantRestoreTests).GetMethod("AuthorityDiff", BindingFlags.Static | BindingFlags.NonPublic);
                                    Assert.NotNull(diff, "existing full-field authority diff helper");
                                    var firstDifference = diff.Invoke(null, new object[] { domain, twinDomain, "domain", 0 });
                                    Assert.AreEqual(facts, twinFacts, "detaching only the four existing route-observer bindings yields exact twin facts");
                                    run.ObserverBindingProof = new
                                    {
                                        Tick = authority.Tick, Compared = "live hooked domain vs exact byte-restored twin", FirstDifferingField = firstDifference,
                                        DetachedFactsEqualToTwin = true,
                                        Bindings = new object[]
                                        {
                                            new { Field = "RouteProjectionReadbackForTests", Before = routeProjection.GetValue(domain)?.GetType().FullName, DuringNeutralization = "null" },
                                            new { Field = "RouteRequestsReadbackForTests", Before = routeRequests.GetValue(domain)?.GetType().FullName, DuringNeutralization = "null" },
                                            new { Field = "RouteProjectionOwnerFilterForTests", Before = routeOwner.GetValue(domain), DuringNeutralization = "null" },
                                            new { Field = "CaptureCurrentExpansionRoutesForTests", Before = routeCapture.GetValue(domain), DuringNeutralization = false }
                                        }
                                    };
                                    Assert.AreNotEqual(hookedFacts, facts, "observer bindings explain the live-vs-twin full-fact delta");
                                }
                                finally { twin.Stop(); }
                            }

                            var checkpoints = authority.CaptureDiagnosticCheckpoints();
                            var budgets = authority.CaptureDiagnosticBudgets();
                            var views = Enumerable.Range(0, 8).Select(n => authority.ParticipantView("ai-" + n)).ToArray();
                            var allViews = JsonConvert.SerializeObject(views);
                            var allFacts = JsonConvert.SerializeObject(new { AuthorityFacts = facts, Checkpoints = checkpoints, Budgets = budgets });
                            var postBytes = authority.CaptureBytes();
                            var postFacts = AuthorityFacts(domain, routeProjection, routeRequests, routeOwner, routeCapture);
                            var postCheckpoints = JsonConvert.SerializeObject(authority.CaptureDiagnosticCheckpoints());
                            var postBudgets = JsonConvert.SerializeObject(authority.CaptureDiagnosticBudgets());
                            var postViews = JsonConvert.SerializeObject(Enumerable.Range(0, 8).Select(n => authority.ParticipantView("ai-" + n)).ToArray());
                            AssertBytesEqual(world, postBytes, "complete diagnostic reads preserve bytes at " + authority.Tick);
                            Assert.AreEqual(facts, postFacts, "independent full-authority facts before/after reads at " + authority.Tick);
                            Assert.AreEqual(JsonConvert.SerializeObject(checkpoints), postCheckpoints, "all-owner checkpoints and receipts before/after reads at " + authority.Tick);
                            Assert.AreEqual(JsonConvert.SerializeObject(budgets), postBudgets, "all-owner ledgers before/after reads at " + authority.Tick);
                            Assert.AreEqual(allViews, postViews, "all eight participant views before/after reads at " + authority.Tick);

                            var current = new Tick { Number = (int)authority.Tick, World = world, Facts = allFacts, Views = allViews, Checkpoints = checkpoints, Budgets = budgets };
                            if (expected != null)
                            {
                                var prior = expected.Ticks[i];
                                AssertBytesEqual(prior.World, world, "hook parity full authority at " + authority.Tick);
                                Assert.AreEqual(prior.Facts, allFacts, "all-owner checkpoints/budgets/receipts at " + authority.Tick);
                                Assert.AreEqual(prior.Views, allViews, "all participant views at " + authority.Tick);
                            }
                            run.Ticks[i] = current;
                            if (enabled)
                            {
                                var targetView = views[0];
                                run.Timeline.Add(JsonConvert.SerializeObject(new
                                {
                                    Tick = authority.Tick, Facts = facts,
                                    Checkpoints = checkpoints.Select(c => new { c.OwnerId, c.Seed, c.Generation, c.DecisionTick, Receipts = c.Records.ToArray() }).ToArray(),
                                    Budgets = budgets, AllViews = views,
                                    ActualRouteProjectionsThisTick = run.RouteProjections.Skip(traceOffset).Select(x => JsonConvert.DeserializeObject<object>(x)).ToArray(),
                                    Mine21View = targetView.Sites.FirstOrDefault(s => s.Site.Id == Site),
                                    ReferenceMine9View = targetView.Sites.FirstOrDefault(s => s.Site.Id == ReferenceSite),
                                    IdleOwnedExplorers = targetView.Entities.Where(e => e.Owner == targetView.Owner && e.Kind == PlayableEntityKind.Explorer).ToArray(),
                                    ViewStage = "ordinary post-tick ParticipantView; not the decision input"
                                }, Formatting.None));
                            }
                        }
                    }
                    AssertBytesEqual(run.Ticks.Last().World, authority.CaptureBytes(), "final read preserves bytes");
                    return run;
                }
                finally
                {
                    AiExpansionFinancial.ActualLifecycleReadbackForTests = null; AiExpansionFinancial.ActualLifecycleReadbackOwnerFilterForTests = null;
                    AiExpansionFinancial.ActualDecisionReadbackForTests = null; AiExpansionFinancial.ActualDecisionReadbackOwnerFilterForTests = null;
                    AiExpansionFinancial.ActualBestSearchReadbackForTests = null; AiExpansionFinancial.ActualBestSearchOwnerFilterForTests = null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests = null; PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests = null;
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests = null; PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests = null;
                    AiExpansionPlannerGuardObserverForTests.Observer = null; AiExpansionPlannerGuardObserverForTests.OwnerFilter = null; AiExpansionPlannerGuardObserverForTests.SafetyObserver = null;
                    routeProjection.SetValue(domain, null); routeRequests.SetValue(domain, null); routeOwner.SetValue(domain, null); routeCapture.SetValue(domain, false);
                    authority.Stop();
                }
            }

            var off = Execute(false);
            var on = Execute(true, off);
            Assert.AreEqual(EndTick - StartTick, off.Ticks.Length);
            Assert.IsEmpty(off.RouteRequests); Assert.IsEmpty(off.RouteProjections);
            Assert.AreEqual(1, on.ActualDecisionInput630Count, "capture the real immutable preallocation owner observation630 exactly once");
            Assert.AreEqual(1, on.PreallocationInput630Count, "capture existing financial admission inputs before technology/financial allocation");
            Assert.Greater(on.ActualRequest21Count, 0, "actual route request for Mine21 was emitted by the ordinary AI path");
            Assert.Greater(on.Projection21Count, 0, "actual route projection must preserve proof21 or its exact projection rejection");
            Assert.Greater(on.ActualRequest21At630Count, 0, "actual route request for Mine21 exists on decision tick630");
            Assert.Greater(on.Projection21At630Count, 0, "actual projection result for Mine21 exists on decision tick630");

            var output = Path.Combine(root, "docs/native-ai/q4/safe-mine21-witness-20261013");
            Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output, "timeline-on.jsonl"), on.Timeline);
            File.WriteAllLines(Path.Combine(output, "route-requests.jsonl"), on.RouteRequests);
            File.WriteAllLines(Path.Combine(output, "route-projections.jsonl"), on.RouteProjections);
            File.WriteAllLines(Path.Combine(output, "owner-decisions.jsonl"), on.Decisions);
            File.WriteAllLines(Path.Combine(output, "financial-and-suppression.jsonl"), on.Finance.Concat(on.PlannerGuards));
            File.WriteAllText(Path.Combine(output, "actual-observation-630.json"), JsonConvert.SerializeObject(on.Tick630Input, Formatting.Indented));
            File.WriteAllText(Path.Combine(output, "observer-binding-proof.json"), JsonConvert.SerializeObject(on.ObserverBindingProof, Formatting.Indented));
            TestContext.WriteLine("Q4_SAFE_MINE21_WITNESS " + JsonConvert.SerializeObject(new
            {
                Seed = 7109, Owner, StartTick, EndTick, Site, ArchiveHash, Entry, InputHash,
                HookOffOnByteAndFullFactEqual = true, ReadNeutrality = true, ActualDecisionInput630Count = on.ActualDecisionInput630Count,
                PreallocationInput630Count = on.PreallocationInput630Count,
                ActualRequest21Count = on.ActualRequest21Count, Projection21Count = on.Projection21Count,
                ActualRequest21At630Count = on.ActualRequest21At630Count, Projection21At630Count = on.Projection21At630Count,
                Evidence = output, Outcome = "captured actual positive proof or exact negative projection/guard result; no predeclared witness outcome"
            }));
        }
    }
}

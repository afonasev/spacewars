using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.PlayMode
{
    public sealed partial class FoundryOrdinaryGraphicsTests
    {
        // Test-only bounded observation on the existing AI audit callback. Each
        // Applied unit has its own first-application origin, owner and radius.
        private sealed class MovementWitness : IDisposable
        {
            private sealed class Row
            {
                internal string Owner; internal PlayableOwner LogicalOwner; internal long Generation, Sequence, Tick;
                internal int Unit, Receipts; internal long LastSequence; internal NavPoint Origin; internal double Radius, Maximum;
                internal long FirstAppliedWallTicks,LastObservedWallTicks,FirstRadiusExceededWallTicks;
            }
            private readonly object gate = new object();
            private readonly List<Row> rows = new List<Row>();
            private readonly List<string> errors = new List<string>();
            private readonly List<Action> detach = new List<Action>();
            private readonly List<(string Owner,long Tick,PlayableCommandKind Kind)> development = new List<(string,long,PlayableCommandKind)>();
            private readonly Dictionary<PlayableOwner,long> readyAt = new Dictionary<PlayableOwner,long>();
            private readonly HashSet<string> issuedMovement = new HashSet<string>();

            private readonly long cutoff;
            private readonly Dictionary<string,long> firstIssuedAt = new Dictionary<string,long>();
            private Row[] cohort;
            private HashSet<string> prefixIssued;
            private HashSet<PlayableOwner> prefixDeveloped;
            private Dictionary<PlayableOwner,long> prefixReady;
            private (string Owner,long Tick,PlayableCommandKind Kind)[] prefixDevelopment;
            internal long PrefixReadbackTick { get; private set; } = -1;
            internal int PrefixClaimedMines { get; private set; }
            internal MovementWitness(long prefixCutoff=long.MaxValue) { cutoff=prefixCutoff; }
            internal void Issued(string owner,long tick=long.MinValue)
            {
                lock(gate) {
                    issuedMovement.Add(owner);
                    if(!firstIssuedAt.TryGetValue(owner,out var first)||tick<first)firstIssuedAt[owner]=tick;
                }
            }
            internal void FreezePrefix(long readbackTick,ISet<PlayableOwner> developed,int claimedMines)
            {
                lock(gate) {
                    Assert.AreNotEqual(long.MaxValue,cutoff); Assert.GreaterOrEqual(readbackTick,cutoff);
                    Assert.IsNull(cohort,"Freeze exactly the first readback at/after cutoff.");
                    PrefixReadbackTick=readbackTick; PrefixClaimedMines=claimedMines;
                    cohort=rows.Where(r=>r.Tick<=cutoff).ToArray();
                    prefixIssued=new HashSet<string>(firstIssuedAt.Where(i=>i.Value<=cutoff).Select(i=>i.Key));
                    prefixDeveloped=new HashSet<PlayableOwner>(developed);
                    prefixReady=new Dictionary<PlayableOwner,long>(readyAt);
                    prefixDevelopment=development.Where(d=>d.Tick<=cutoff).ToArray();
                }
            }

            internal void InfrastructureReady(PlayableOwner owner,long tick) { lock(gate) if(!readyAt.ContainsKey(owner))readyAt.Add(owner,tick); }
            internal void Development(string owner,long tick,PlayableCommandKind kind)
            {
                lock(gate) {
                    if(development.Count>=4096){if(errors.Count==0)errors.Add("Development observation cap reached.");return;}
                    development.Add((owner,tick,kind));
                }
            }

            internal void Applied(string owner, PlayableOwner logicalOwner, long generation, long sequence, long tick,
                IEnumerable<int> recipients, IEnumerable<PlayableEntitySnapshot> entities, PlayableProfile profile)
            {
                lock (gate) {
                    Issued(owner,tick);
                    foreach (int id in recipients.Distinct()) {
                        if (rows.Count >= 4096) { if (errors.Count == 0) errors.Add("Movement recipient observation cap reached."); return; }
                        var unit = entities.SingleOrDefault(u => u.Id == id && u.Owner == logicalOwner);
                        if (unit == null) { errors.Add("Applied recipient missing or foreign: " + owner + "/" + id); continue; }
                        var existing=rows.SingleOrDefault(r=>r.Owner==owner&&r.LogicalOwner==logicalOwner&&r.Generation==generation&&r.Unit==id);
                        if(existing!=null){existing.Receipts++;existing.LastSequence=sequence;continue;}
                        rows.Add(new Row { Owner = owner, LogicalOwner = logicalOwner, Generation = generation, Sequence = sequence,
                            Tick = tick, Unit = id, Receipts=1,LastSequence=sequence, Origin = unit.Position, Radius = PlayableUnitRules.Radius(profile, unit.Kind),
                            FirstAppliedWallTicks=System.Diagnostics.Stopwatch.GetTimestamp() });
                    }
                }
            }

            internal void Observe(PlayableSnapshot snapshot)
            {
                foreach(var building in snapshot.Buildings.Where(b=>b.Health>0&&b.Phase==ConstructionPhase.Ready&&
                    (b.Kind==PlayableBuildingKind.Factory||b.Kind==PlayableBuildingKind.Refinery)))InfrastructureReady(building.Owner,snapshot.Tick);
                lock (gate) foreach (var row in rows) {
                    var unit = snapshot.Entities.SingleOrDefault(u => u.Id == row.Unit && u.Owner == row.LogicalOwner);
                    if (unit != null && snapshot.Generation == row.Generation) {
                        row.Maximum = Math.Max(row.Maximum, Math.Sqrt(Math.Pow(row.Origin.X-unit.Position.X,2)+Math.Pow(row.Origin.Z-unit.Position.Z,2)));
                        row.LastObservedWallTicks=System.Diagnostics.Stopwatch.GetTimestamp();
                        if(row.FirstRadiusExceededWallTicks==0&&row.Maximum>row.Radius)row.FirstRadiusExceededWallTicks=row.LastObservedWallTicks;
                    }
                }
            }

            internal void Attach(PlayableRuntime runtime, PlayableProfile profile)
            {
                Assert.True(runtime.Latest.Paused, "Install observer at an acknowledged pause boundary.");
                var authority = (PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick", Flags).GetValue(runtime);
                var checkpoints = authority.CaptureDiagnosticCheckpoints();
                Assert.True(checkpoints.All(c => c.Records.Count < 128 && !c.Records.Any(r =>
                    r.CommandSequence>0 && (r.Kind == PlayableCommandKind.Move || r.Kind == PlayableCommandKind.AttackMove))),
                    "No issued movement may precede exact recipient observation.");
                var owners = runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.ToDictionary(v => v.OwnerId, v => v.Owner);
                var scheduler = typeof(PlayableRuntime).GetField("aiScheduler", Flags).GetValue(runtime);
                var loops = (IEnumerable)scheduler.GetType().GetProperty("Owners", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(scheduler);
                var domain = typeof(PlayableAuthorityTick).GetField("domain", Flags).GetValue(authority);
                var snapshotMethod = domain.GetType().GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (var loop in loops) {
                    var field = loop.GetType().GetField("audit", Flags);
                    var previous = (Action<PlayableAiEvaluationEvent>)field.GetValue(loop);
                    Action<PlayableAiEvaluationEvent> observer = e => {
                        previous?.Invoke(e);
                        if(e.Sequence>0&&(e.Kind==PlayableCommandKind.Move||e.Kind==PlayableCommandKind.AttackMove))Issued(e.Owner,e.DeliveryTick??e.ReceiptTick??e.ObservationTick);
                        if (e.Status != PlayableAiDeliveryStatus.Applied) return;
                        if(e.Kind==PlayableCommandKind.BuildAt||Spacewars.Simulation.Ai.AiRosterCatalog.IsProduction(e.Kind))
                            Development(e.Owner,e.ApplicationTick??-1,e.Kind);
                        if(e.Kind != PlayableCommandKind.Move && e.Kind != PlayableCommandKind.AttackMove) return;
                        // Callback runs on the authority thread after application:
                        // read exact current positions, without advancing/pumping it.
                        var snapshot = (PlayableSnapshot)snapshotMethod.Invoke(domain, new object[] {
                            0L, RuntimeStatus.Running, false, new PlayableRuntimeMetrics(0,0,0,0,0), null, runtime.MatchSeed });
                        Observe(snapshot);Applied(e.Owner, owners[e.Owner], e.Generation, e.Sequence, e.ApplicationTick ?? snapshot.Tick, e.EntityIds, snapshot.Entities, profile);
                    };
                    field.SetValue(loop, observer);
                    detach.Add(() => field.SetValue(loop, previous));
                }
                Assert.AreEqual(6, detach.Count, "Every AI owner must be observed.");
            }

            internal string Report(IEnumerable<KeyValuePair<string, PlayableOwner>> owners)
            {
                lock (gate) return string.Join("\n", rows.Select(r => "owner=" + r.Owner + " logical=" + r.LogicalOwner + " generation=" + r.Generation +
                    " cohort="+(r.Tick<=cutoff?"prefix":"late-new-recipient")+" unit=" + r.Unit + " sequence=" + r.Sequence + " appliedTick=" + r.Tick + " origin=" + r.Origin.X + "," + r.Origin.Z +
                    " lastSequence="+r.LastSequence+" appliedReceipts="+r.Receipts+" radius=" + r.Radius + " maxDisplacement=" + r.Maximum + " pass=" + (r.Maximum > r.Radius)+
                    " firstAppliedWallTicks="+r.FirstAppliedWallTicks+" lastObservedWallTicks="+r.LastObservedWallTicks+" firstRadiusExceededWallTicks="+r.FirstRadiusExceededWallTicks+
                    " wallFrequency="+System.Diagnostics.Stopwatch.Frequency)) + "\n" +
                    string.Join("\n", owners.Where(o => !rows.Any(r => r.Owner == o.Key)).Select(o => "owner=" + o.Key + " logical=" + o.Value + " AppliedMovementRecipients=0 issuedMovement="+issuedMovement.Contains(o.Key))) +
                    "\n"+string.Join("\n",development.Select(d=>"development owner="+d.Owner+" tick="+d.Tick+" kind="+d.Kind))+
                    "\n"+string.Join("\n",readyAt.Select(r=>"infrastructureReady owner="+r.Key+" firstObservedTick="+r.Value))+"\nprefixCutoff="+cutoff+" prefixFirstReadback="+PrefixReadbackTick+" prefixDevelopedOwners="+(prefixDeveloped==null?"unfrozen":string.Join(",",prefixDeveloped))+" prefixClaimedMines="+PrefixClaimedMines+
                    " prefixIssuedOwners="+(prefixIssued==null?"unfrozen":string.Join(",",prefixIssued))+
                    "\n"+(cohort==null?"":string.Join("\n",owners.Select(o=>"prefixOwner="+o.Key+" appliedRecipients="+cohort.Count(r=>r.Owner==o.Key)+" issued="+prefixIssued.Contains(o.Key)+" developed="+prefixDeveloped.Contains(o.Value)+
                        " continuedDevelopment="+(prefixReady.TryGetValue(o.Value,out var ready)&&prefixDevelopment.Any(d=>d.Owner==o.Key&&d.Tick>ready)))))+"\nerrors=" + string.Join(";", errors);
            }

            internal void Verify(IEnumerable<KeyValuePair<string, PlayableOwner>> owners, ISet<PlayableOwner> developed)
            {
                lock (gate) {
                    Assert.IsEmpty(errors);
                    var selected=rows.ToArray();var issued=issuedMovement;var readyFacts=readyAt;
                    var developmentFacts=development.ToArray();var developedFacts=developed;
                    if(cutoff!=long.MaxValue) {
                        Assert.NotNull(cohort,"Prefix facts must freeze at first readback >=cutoff, before tail.");
                        Assert.AreEqual(cohort.Length,rows.Count(r=>r.Tick<=cutoff),"No prefix recipient may appear after the prefix snapshot.");
                        selected=cohort;issued=prefixIssued;readyFacts=prefixReady;developmentFacts=prefixDevelopment;developedFacts=prefixDeveloped;
                    }
                    Assert.IsNotEmpty(selected, "At least one AI Applied movement recipient is required.");
                    foreach (var row in selected) Assert.Greater(row.Maximum, row.Radius,
                        "Applied movement recipient owner=" + row.Owner + " unit=" + row.Unit + " sequence=" + row.Sequence + " tick=" + row.Tick);
                    foreach (var owner in owners.Where(o => !selected.Any(r => r.Owner == o.Key))) {
                        Assert.False(issued.Contains(owner.Key),"Zero Applied movement cannot be described as absence of issued movement: "+owner.Key);
                        Assert.True(developedFacts.Contains(owner.Value), "Owner without issued movement must continue base/production development: " + owner.Key);
                        Assert.True(readyFacts.TryGetValue(owner.Value,out var ready)&&developmentFacts.Any(d=>d.Owner==owner.Key&&d.Tick>ready),
                            "Owner without movement needs Applied construction/production after observed early infrastructure: "+owner.Key);
                    }
                }
            }

            public void Dispose() { foreach (var restore in detach) restore(); detach.Clear(); }
        }

        private static void FinishMovementWitness(PlayableRuntime runtime,MovementWitness movement,string output)
        {
            if(runtime==null)return;
            runtime.RequestStop();
            var worker=(System.Threading.Thread)typeof(PlayableRuntime).GetField("worker",Flags).GetValue(runtime);
            bool stopped=worker.Join(5000);
            if(movement!=null)System.IO.File.WriteAllText(System.IO.Path.Combine(output,"movement-recipients.txt"),
                movement.Report(runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.ToDictionary(v=>v.OwnerId,v=>v.Owner))+"\nauthorityJoined="+stopped);
            Assert.True(stopped,"Authority stop must complete before restoring observer callbacks or destroying its host.");
            movement?.Dispose();
        }

        [TestCase("late-stuck-ignored")] [TestCase("prefix-stuck-kept")]
        [TestCase("late-development-no-rescue")] [TestCase("prefix-rejected-issued")] [TestCase("late-infrastructure-no-rescue")]
        public void CohortCriterionFreezesCutoffRecipientsAndPrefixFacts(string control)
        {
            using(var witness=new MovementWitness(1800)) {
                var p=PlayableProfile.Default;
                PlayableEntitySnapshot Unit(int id,PlayableOwner owner,double x)=>new PlayableEntitySnapshot(id,owner,PlayableEntityKind.Explorer,new NavPoint(x,0),10,false,0,0,0);
                var initial=new[]{Unit(11,PlayableOwner.Player,0),Unit(12,PlayableOwner.Player,0)};
                PlayableSnapshot View(long generation,long tick,PlayableEntitySnapshot[] units)=>new PlayableSnapshot(p.ProfileId,p.Revision,generation,7,1,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,
                    units,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
                var owners=new Dictionary<string,PlayableOwner>{{"fifth",PlayableOwner.Player},{"player",PlayableOwner.Enemy}};
                var developed=new HashSet<PlayableOwner>{PlayableOwner.Player,PlayableOwner.Enemy};
                witness.Applied("fifth",PlayableOwner.Player,1,7,1800,new[]{11},initial,p);
                if(control!="late-infrastructure-no-rescue")witness.InfrastructureReady(PlayableOwner.Enemy,100);
                if(control!="late-development-no-rescue")witness.Development("player",200,PlayableCommandKind.QueueTank);
                if(control=="prefix-rejected-issued")witness.Issued("player",1800); // A rejected positive sequence also excludes no-issued branch.
                witness.FreezePrefix(1801,developed,1);
                Assert.AreEqual(1801,witness.PrefixReadbackTick);
                witness.InfrastructureReady(PlayableOwner.Enemy,2000);
                witness.Development("player",2000,PlayableCommandKind.QueueTank);
                witness.Issued("player",1801); // Tail issue cannot change prefix classification.
                witness.Applied("fifth",PlayableOwner.Player,1,8,1801,new[]{12},initial,p); // New tail unit stays still.
                witness.Applied("fifth",PlayableOwner.Player,1,9,2000,new[]{11},initial,p); // Repeat keeps first origin/deadline.
                witness.Applied("fifth",PlayableOwner.Player,2,10,2001,new[]{11},initial,p); // Different generation stays separate.
                witness.Observe(View(2,3000,new[]{Unit(11,PlayableOwner.Player,10),Unit(12,PlayableOwner.Player,10)}));
                // Wrong generation cannot move the fixed prefix recipient.
                Assert.Throws<AssertionException>(()=>witness.Verify(owners,developed));
                witness.Observe(View(1,3600,new[]{Unit(11,PlayableOwner.Player,control=="prefix-stuck-kept"?0:5),Unit(12,PlayableOwner.Player,0)}));
                if(control=="late-stuck-ignored")witness.Verify(owners,developed);
                else Assert.Throws<AssertionException>(()=>witness.Verify(owners,developed));
                StringAssert.Contains("cohort=late-new-recipient",witness.Report(owners));
                StringAssert.Contains("appliedTick=1800",witness.Report(owners));
            }
        }

        [TestCase(true,true)] [TestCase(false,true)] [TestCase(false,false)]
        public void MovementCriterionDoesNotLetAnotherRecipientHideAStuckUnit(bool stuck,bool continuingDevelopment)
        {
            using (var witness = new MovementWitness()) {
                var p = PlayableProfile.Default;
                PlayableEntitySnapshot Unit(int id, PlayableOwner owner, double x) => new PlayableEntitySnapshot(id, owner, PlayableEntityKind.Explorer, new NavPoint(x,0), 10, false, 0, 0, 0);
                var initial = new[] { Unit(11,PlayableOwner.Player,0), Unit(12,PlayableOwner.Player,0), Unit(13,PlayableOwner.Enemy,0) };
                witness.Applied("fifth",PlayableOwner.Player,1,7,660,new[] {11,12},initial,p);
                var moved = new[] { Unit(11,PlayableOwner.Player,stuck?0:5), Unit(12,PlayableOwner.Player,5), Unit(13,PlayableOwner.Enemy,0) };
                var view = new PlayableSnapshot(p.ProfileId,p.Revision,1,7,1,900,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,
                    moved,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null);
                witness.Observe(view);
                var owners = new Dictionary<string,PlayableOwner> { {"fifth",PlayableOwner.Player}, {"player",PlayableOwner.Enemy} };
                var developed = new HashSet<PlayableOwner> { PlayableOwner.Player,PlayableOwner.Enemy };
                witness.InfrastructureReady(PlayableOwner.Enemy,100);
                if(continuingDevelopment)witness.Development("player",200,PlayableCommandKind.QueueTank);
                // A repeated horizon-edge command does not reset this unit's
                // already observed displacement or add an unapproved deadline.
                witness.Applied("fifth",PlayableOwner.Player,1,8,1799,new[]{11},moved,p);
                if(stuck||!continuingDevelopment)Assert.Throws<AssertionException>(() => witness.Verify(owners,developed));
                else {
                    witness.Verify(owners,developed);
                    Assert.Throws<AssertionException>(()=>witness.Verify(owners,new HashSet<PlayableOwner>{PlayableOwner.Player}));
                    witness.Issued("player");
                    Assert.Throws<AssertionException>(()=>witness.Verify(owners,developed),"Rejected/accepted issued movement cannot masquerade as no issued movement.");
                }
            }
        }
    }
}

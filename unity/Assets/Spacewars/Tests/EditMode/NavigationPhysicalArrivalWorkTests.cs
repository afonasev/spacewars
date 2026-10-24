using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private sealed class OwnedArrivalFixture : IDisposable
        {
            internal readonly NavigationRetentionBudget Budget = new NavigationRetentionBudget();
            internal NavArrivalInput Input { get; private set; }
            internal readonly NavArrivalMember Member;
            private readonly NavArrivalInput source;

            internal OwnedArrivalFixture(bool independent, bool samePoint = false, bool multipleCandidates = false,
                bool certifiedFlat = false, int memberCount = 1, int flatCandidateCount = 0, bool unreachableFirst = false)
            {
                var blockers = new[] { new NavObstacle(-.35, unreachableFirst ? -12 : -3, .35, unreachableFirst ? 12 : 3) };
                var provider = certifiedFlat ? NavigationAdmission.CertifiedFlatProvider(new NavGeometry(12, blockers, 1)) :
                    NavigationArrivalTestFactory.Layered("physical-arrival-work", 1, blockers);
                string surface = certifiedFlat ? NavLocation.FlatSurface : "ground";
                var origin = NavigationArrivalTestFactory.At(samePoint ? 2 : -5, 0, surface);
                var endpoint = NavigationArrivalTestFactory.At(samePoint ? 2 : 5, 0, surface);
                Member = NavigationArrivalTestFactory.Member(31, 0, origin, .2);
                var candidates = multipleCandidates
                    ? new[] { endpoint, NavigationArrivalTestFactory.At(samePoint ? 3 : 6, 1, surface) }
                    : new[] { endpoint };
                if(flatCandidateCount>0)candidates=Enumerable.Range(0,flatCandidateCount).Select(i=>NavigationArrivalTestFactory.At(5+.2*(i%8),.2*(i/8),surface)).ToArray();
                if(unreachableFirst)candidates[1]=NavigationArrivalTestFactory.At(-5,5,surface);
                var originals=Enumerable.Range(0,memberCount).Select(i=>i==0?Member:NavigationArrivalTestFactory.Member(31+i,0,NavigationArrivalTestFactory.At(-5,-i,surface),.2)).ToArray();
                var candidateSource = new NavArrivalCandidateSource("physical-arrival-work", 1, endpoint, candidates);
                source = new NavArrivalInput("topology:" + provider.Binding, "profile:" + provider.Binding, provider,
                    new NavGraphProfile(NavigationArrivalTestFactory.Sha("physical-arrival-work-profile"), "distance", 1, 1),
                    new NavigationProfile(), new NavGraphCompileLimits(2000, 2000, 10000), candidateSource,
                    originals, NavigationArrivalTestFactory.Occupancy(), 4096, null, independent);
                Assert.True(Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 1, out var birth));
                var reservation = birth.OwnedReservation();
                var ownedProvider = LayeredNavigationProvider.CopyOwned((LayeredNavigationProvider)source.Provider, reservation);
                var metadata = NavGraphMetadata.CopyOptionalUnowned(Member.Clearance, source.Profile, source.Limits, reservation);
                var arrival = NavigationProfile.CopyOwned(source.ArrivalProfile, reservation);
                var ownedSource = NavArrivalCandidateSource.CopyOwned(source.Source, reservation);
                var occupancy = NavArrivalOccupancySnapshot.CopyOwned(source.Occupancy, Array.Empty<NavArrivalOccupant>(), reservation);
                var ownedMembers = originals.Select(m=>NavArrivalMember.CopyOwned(m,metadata.Clearance,reservation)).ToArray();
                var bindings = ownedMembers.Select(m=>NavArrivalWorkingBinding.CreateOwned(m.EntityId,ownedProvider,metadata.Profile,reservation)).ToArray();
                Input = NavArrivalInput.CopyOwned(source, ownedProvider, metadata.Profile, arrival, metadata.Limits,
                    ownedSource, ownedMembers, occupancy, bindings, reservation);
                ownedProvider.StorageRoot.ReleaseOwner(); metadata.Release(); arrival.StorageRoot.ReleaseOwner();
                ownedSource.StorageRoot.ReleaseOwner(); occupancy.StorageRoot.ReleaseOwner();
                foreach(var member in ownedMembers)member.StorageRoot.ReleaseOwner();foreach(var binding in bindings)binding.StorageRoot.ReleaseOwner();
                birth.Attach(Input.StorageRoot); birth.Commit(Input.StorageRoot, _ => { });
                IsIndependent = independent;
            }

            internal bool IsIndependent { get; }
            internal NavArrivalInput UnownedInput => source;

            internal NavigationArrivalWork NewWork(Action<NavigationArrivalWork> prepare = null, NavigationRetentionBudget budget = null)
            {
                budget = budget ?? Budget;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 10, out var scope));
                var work = NavigationArrivalWork.CreateOwned(Input, scope.OwnedReservation());
                try
                {
                    prepare?.Invoke(work);
                    work.SealPrepared();
                    scope.Attach(work); scope.Commit(work, _ => { });
                    return work;
                }
                catch { work.ReleaseOwner(); scope.Discard(); throw; }
            }

            internal void ReleaseInput()
            {
                if (Input == null) return;
                Input.StorageRoot.ReleaseOwner(); Input = null;
            }

            public void Dispose()
            {
                ReleaseInput();
                Assert.Zero(Budget.PhysicalBytes);
                Assert.Zero(Budget.HeldBytes);
            }
        }

        private static readonly NavEpochQuota ArrivalWorkQuota = new NavEpochQuota(20000, 20000, 20000, 20000, 20000);

        private static NavArrivalStatus FinishOwnedArrival(NavigationArrivalWork work)
        {
            long firstEpoch = Math.Max(0, work.LogicalEpoch + 1);
            for (long epoch = firstEpoch; epoch < firstEpoch + 128; epoch++)
            {
                var status = work.Advance(epoch, 10000, ArrivalWorkQuota);
                if (status != NavArrivalStatus.Pending && status != NavArrivalStatus.BudgetExhausted) return status;
            }
            Assert.Fail("Owned arrival work did not finish within the fixture epoch bound.");
            return NavArrivalStatus.Pending;
        }

        private static NavigationArrivalWork Fork(NavigationArrivalWork work, NavigationRetentionBudget budget, int token,
            Action<NavigationArrivalWork> prepare = null)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction, 1, token, out var scope));
            var fork = work.ForkOwned(scope.OwnedReservation());
            try
            {
                prepare?.Invoke(fork);
                fork.SealPrepared();
                scope.Attach(fork); scope.Commit(fork, _ => { });
                return fork;
            }
            catch { fork.ReleaseOwner(); scope.Discard(); throw; }
        }

        private static object ArrivalModeField(object value,string name)
            =>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)?.GetValue(value)??
                value.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)?.GetValue(value);
        private static NavScheduledRequest[] ArrivalModeRequests(NavigationArrivalWork work)
            =>((System.Collections.IEnumerable)ArrivalModeField(work,"pageRequests")).Cast<object>().Select(row=>(NavScheduledRequest)ArrivalModeField(row,"Request")).ToArray();
        private static void PrepareArrivalModeRows(NavigationArrivalWork work)
        {
            for(int i=0;i<16&&CurrentArrivalPhase(work)!="SolvePage";i++)work.Advance(i+1,10000,new NavEpochQuota(0,0,0,0,0));
            Assert.AreEqual("SolvePage",CurrentArrivalPhase(work));
        }
        [TestCase(true,true,1,true)][TestCase(true,true,1,false)]
        [TestCase(false,true,1,true)][TestCase(false,true,1,false)]
        [TestCase(true,false,1,true)][TestCase(true,false,1,false)]
        [TestCase(false,false,1,true)][TestCase(false,false,1,false)]
        [TestCase(true,true,2,true)][TestCase(true,true,2,false)]
        public void CertifiedFlatArrivalSingletonModeIsLimitedInOwnedAndUnownedRequests(bool flat,bool independent,int members,bool owned)
        {
            using(var fixture=new OwnedArrivalFixture(independent,multipleCandidates:true,certifiedFlat:flat,memberCount:members)){
                NavigationArrivalWork work=null;
                try{
                    work=owned?fixture.NewWork(PrepareArrivalModeRows):new NavigationArrivalWork(fixture.UnownedInput);
                    if(!owned)PrepareArrivalModeRows(work);
                    var rows=ArrivalModeRequests(work);Assert.AreEqual(2,rows.Length);
                    for(int n=0;n<rows.Length;n++){
                        Assert.AreEqual(flat&&independent&&members==1,rows[n].Singleton);Assert.False(rows[n].Query);
                        Assert.AreEqual(fixture.Input.Source.Candidates[n],rows[n].Subscription.AssignedEndpoint);
                        Assert.AreEqual(fixture.Member.Origin,rows[n].Subscription.Origin);
                    }
                }finally{work?.ReleaseOwner();}
            }
        }
        [TestCase(false)][TestCase(true)]
        public void CertifiedFlatN1ManyCandidatesUseExactSingletonWithColdCowAndRefusal(bool unreachableFirst)
        {
            using(var fixture=new OwnedArrivalFixture(true,certifiedFlat:true,flatCandidateCount:64,unreachableFirst:unreachableFirst)){
                NavigationArrivalWork work=null,next=null,restored=null,resumed=null;var cold=new NavigationRetentionBudget();
                try{
                    work=fixture.NewWork(w=>w.Advance(1,10000,new NavEpochQuota(20000,0,2,20000,256)));
                    var requests=ArrivalModeRequests(work);Assert.AreEqual(64,requests.Length);Assert.True(requests.All(r=>r.Singleton&&!r.Query));
                    for(int i=0;i<requests.Length;i++)Assert.AreEqual(fixture.Input.Source.Candidates[i],requests[i].Subscription.AssignedEndpoint);
                    var scheduler=(LayeredNavigationScheduler)ArrivalModeField(work,"scheduler");
                    var job=((System.Collections.IEnumerable)ArrivalModeField(scheduler,"jobs")).Cast<object>().First();
                    var singleton=(NavIncrementalSingletonRoute)ArrivalModeField(job,"Singleton");
                    Assert.NotNull(singleton,"reach real singleton continuation, not only a mode flag");Assert.AreEqual(NavSolveStatus.Pending,singleton.Status);
                    Assert.Null(ArrivalModeField(job,"Field"));Assert.Zero(scheduler.Counters.PhysicalFieldWork);
                    byte[] body=CaptureGraph(fixture.Budget,work);long charge=Unique(work);long before=fixture.Budget.PhysicalBytes;
                    Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,19,out var rejected));NavigationArrivalWork denied=null;int refusals=0;
                    try{
                        denied=work.ForkOwned(rejected.OwnedReservation());
                        fixture.Budget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavIncrementalSingletonRoute)&&f.GetMethod().Name=="ForkPrepared")){refusals++;return false;}return true;};
                        Assert.Throws<InvalidOperationException>(()=>denied.Advance(40,10000,ArrivalWorkQuota));Assert.Greater(refusals,0);
                    }finally{fixture.Budget.AllocationAdmission=null;denied?.ReleaseOwner();rejected.Discard();}
                    Assert.AreEqual(before,fixture.Budget.PhysicalBytes);Assert.Zero(fixture.Budget.HeldBytes);CollectionAssert.AreEqual(body,CaptureGraph(fixture.Budget,work));
                    next=Fork(work,fixture.Budget,20,w=>{w.Advance(40,10000,ArrivalWorkQuota);FinishOwnedArrival(w);});
                    Assert.AreEqual(NavArrivalStatus.Ready,next.Status);Assert.AreEqual(1,next.Proposals.Count);
                    var proposal=next.Proposals.Single();Assert.AreEqual(fixture.Input.Source.Candidates[unreachableFirst?1:0],proposal.Endpoint);
                    Assert.True(proposal.Proof.ValidateAgainst(fixture.Input,proposal.Member,proposal.Endpoint));
                    byte[] expected=CaptureGraph(fixture.Budget,next);long expectedCharge=Unique(next);
                    Assert.Zero(((LayeredNavigationScheduler)ArrivalModeField(next,"scheduler")).Counters.PhysicalFieldWork);
                    next.ReleaseOwner();next=null;work.ReleaseOwner();work=null;fixture.ReleaseInput();Assert.Zero(fixture.Budget.PhysicalBytes);
                    Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));restored=(NavigationArrivalWork)Decode(body,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
                    Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(body,CaptureGraph(cold,restored));
                    resumed=Fork(restored,cold,30,w=>{w.Advance(40,10000,ArrivalWorkQuota);FinishOwnedArrival(w);});
                    CollectionAssert.AreEqual(expected,CaptureGraph(cold,resumed));Assert.AreEqual(expectedCharge,Unique(resumed));
                }finally{next?.ReleaseOwner();work?.ReleaseOwner();resumed?.ReleaseOwner();restored?.ReleaseOwner();}
                Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
            }
        }

        [TestCase("prepare")]
        [TestCase("scan")]
        [TestCase("solve")]
        public void BodyOwnedArrivalWorkRoundTripsPartialContinuationAndExactNextAdvance(string phaseName)
        {
            using (var fixture = new OwnedArrivalFixture(false))
            {
                var work = fixture.NewWork(w =>
                {
                    if (phaseName == "prepare")
                        Assert.AreEqual(NavArrivalStatus.BudgetExhausted, w.Advance(1, 1, ArrivalWorkQuota));
                    else if (phaseName == "scan")
                    {
                        for (int i = 0; i < 16 && CurrentArrivalPhase(w) != "ScanCandidates"; i++)
                            w.Advance(i + 1, 1, ArrivalWorkQuota);
                        Assert.AreEqual("ScanCandidates", CurrentArrivalPhase(w));
                    }
                    else
                    {
                        for (int i = 0; i < 16 && CurrentArrivalPhase(w) != "SolvePage"; i++)
                            w.Advance(i + 1, 10000, new NavEpochQuota(0, 0, 0, 0, 0));
                        Assert.AreEqual("SolvePage", CurrentArrivalPhase(w));
                    }
                });
                var inputBacking = fixture.Input.MembersBacking;
                var workMembers = (NavRetainedArray<NavArrivalMember>)typeof(NavigationArrivalWork)
                    .GetField("membersStorage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(work);
                Assert.AreNotSame(inputBacking, workMembers.ReadOnlyBacking);
                byte[] body = CaptureGraph(fixture.Budget, work);
                long charge = Unique(work);
                NavArrivalStatus originalStatus = NavArrivalStatus.Pending;
                var originalNext = Fork(work, fixture.Budget, 20, next => originalStatus = next.Advance(40, 10000, ArrivalWorkQuota));
                byte[] expectedNext = CaptureGraph(fixture.Budget, originalNext);
                long expectedCharge = Unique(originalNext);
                CollectionAssert.AreEqual(body, CaptureGraph(fixture.Budget, work), "Fork advancement must preserve the sealed source BODY.");
                NavArrivalStatus originalFullStatus = NavArrivalStatus.Pending;
                var originalFull = Fork(work, fixture.Budget, 21, next =>
                {
                    next.Advance(40, 10000, ArrivalWorkQuota);
                    originalFullStatus = FinishOwnedArrival(next);
                });
                byte[] expectedFull = CaptureGraph(fixture.Budget, originalFull);
                originalFull.ReleaseOwner();
                originalNext.ReleaseOwner();
                work.ReleaseOwner();
                fixture.ReleaseInput();
                Assert.Zero(fixture.Budget.PhysicalBytes);

                var cold = new NavigationRetentionBudget();
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode, 1, 1, out var decode));
                var restored = (NavigationArrivalWork)Decode(body, decode.OwnedReservation());
                decode.Attach(restored); decode.Commit(restored, _ => { });
                Assert.AreEqual(charge, cold.PhysicalBytes);
                var resumedStatus = NavArrivalStatus.Pending;
                var resumed = Fork(restored, cold, 30, next => resumedStatus = next.Advance(40, 10000, ArrivalWorkQuota));
                Assert.AreEqual(originalStatus, resumedStatus);
                CollectionAssert.AreEqual(expectedNext, CaptureGraph(cold, resumed));
                Assert.AreEqual(expectedCharge, Unique(resumed));
                var resumedFullStatus = NavArrivalStatus.Pending;
                var resumedFull = Fork(restored, cold, 31, next =>
                {
                    next.Advance(40, 10000, ArrivalWorkQuota);
                    resumedFullStatus = FinishOwnedArrival(next);
                });
                Assert.AreEqual(originalFullStatus, resumedFullStatus);
                CollectionAssert.AreEqual(expectedFull, CaptureGraph(cold, resumedFull));
                restored.ReleaseOwner(); resumed.ReleaseOwner(); resumedFull.ReleaseOwner();
                Assert.Zero(cold.PhysicalBytes); Assert.Zero(cold.HeldBytes);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BodyOwnedArrivalWorkReadyDirectAndSolvedRoutesSurviveColdDecode(bool direct)
        {
            using (var fixture = new OwnedArrivalFixture(direct, samePoint: direct))
            {
                var work = fixture.NewWork(w => Assert.AreEqual(NavArrivalStatus.Ready, FinishOwnedArrival(w)));
                Assert.AreEqual(direct ? 1 : 0, work.DirectWorkConsumed);
                Assert.AreEqual(1, work.Proposals.Count);
                var proposal = work.Proposals.Single();
                Assert.True(proposal.Proof.ValidateAgainst(fixture.Input, proposal.Member, proposal.Endpoint));
                if (!direct) Assert.Greater(proposal.Route.Legs.Count, 0);
                byte[] body = CaptureGraph(fixture.Budget, work);
                long charge = Unique(work);
                work.ReleaseOwner();
                fixture.ReleaseInput();
                Assert.Zero(fixture.Budget.PhysicalBytes);

                var cold = new NavigationRetentionBudget();
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode, 1, 1, out var decode));
                var restored = (NavigationArrivalWork)Decode(body, decode.OwnedReservation());
                decode.Attach(restored); decode.Commit(restored, _ => { });
                Assert.AreEqual(NavArrivalStatus.Ready, restored.Status);
                Assert.AreEqual(charge, cold.PhysicalBytes);
                Assert.AreEqual(1, restored.Proposals.Count);
                var restoredInput = (NavArrivalInput)typeof(NavigationArrivalWork).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(restored);
                Assert.True(restored.Proposals[0].Proof.ValidateAgainst(restoredInput, restored.Proposals[0].Member, restored.Proposals[0].Endpoint));
                Assert.AreEqual(direct ? 1 : 0, restored.DirectWorkConsumed);
                restored.ReleaseOwner();
                Assert.Zero(cold.PhysicalBytes); Assert.Zero(cold.HeldBytes);
            }
        }

        private static string CurrentArrivalPhase(NavigationArrivalWork work)
            => typeof(NavigationArrivalWork).GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(work).ToString();

        [TestCase(false)]
        [TestCase(true)]
        public void BodyOwnedArrivalWorkReleaseAndCancellationRespectInputLifetime(bool ready)
        {
            using (var fixture = new OwnedArrivalFixture(false))
            {
                var work = fixture.NewWork(w =>
                {
                    if (ready) Assert.AreEqual(NavArrivalStatus.Ready, FinishOwnedArrival(w));
                    else Assert.AreEqual(NavArrivalStatus.BudgetExhausted, w.Advance(1, 1, ArrivalWorkQuota));
                });
                var fork = Fork(work, fixture.Budget, 50, w =>
                {
                    if (ready) w.ReleaseDerivatives(); else w.DiscardCancelledDerivatives();
                });
                var root = (NavDeclarationStorage)typeof(NavigationArrivalWork).GetField("ownedInput", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fork);
                var requests = (System.Collections.IEnumerable)typeof(NavigationArrivalWork).GetField("pageRequests", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fork);
                var results = (System.Collections.IEnumerable)typeof(NavigationArrivalWork).GetField("pageResults", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fork);
                if (ready)
                {
                    Assert.NotNull(root);
                    Assert.NotNull(typeof(NavigationArrivalWork).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fork));
                    Assert.AreEqual(1, fork.Proposals.Count);
                }
                else
                {
                    Assert.IsNull(typeof(NavigationArrivalWork).GetField("ownedInput", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fork));
                    Assert.IsNull(typeof(NavigationArrivalWork).GetField("input", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fork));
                    Assert.AreEqual(0, requests.Cast<object>().Count());
                    Assert.AreEqual(0, results.Cast<object>().Count());
                }
                fork.ReleaseOwner(); work.ReleaseOwner(); fixture.ReleaseInput();
                Assert.Zero(fixture.Budget.PhysicalBytes); Assert.Zero(fixture.Budget.HeldBytes);
            }
        }

        [Test]
        public void BodyOwnedArrivalWorkEveryNthAdmissionRefusalCleansCreatedAndForkedOwners()
        {
            using (var fixture = new OwnedArrivalFixture(false))
            {
                Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 70, out var countScope));
                var counter = new Refusal(countScope, int.MaxValue);
                var trial = NavigationArrivalWork.CreateOwned(fixture.Input, counter);
                countScope.Attach(trial); int createCalls = counter.Calls; countScope.Discard();
                Assert.Zero(fixture.Budget.HeldBytes);
                for (int nth = 1; nth <= createCalls; nth++)
                {
                    Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 70 + nth, out var scope));
                    var refusal = new Refusal(scope, nth);
                    Assert.Throws<InvalidOperationException>(() => NavigationArrivalWork.CreateOwned(fixture.Input, refusal));
                    Assert.AreEqual(nth, refusal.Calls); scope.Discard();
                    Assert.Zero(fixture.Budget.HeldBytes); Assert.AreEqual(Unique(fixture.Input.StorageRoot), fixture.Budget.PhysicalBytes);
                }

                var seed = fixture.NewWork(w => Assert.AreEqual(NavArrivalStatus.BudgetExhausted, w.Advance(1, 1, ArrivalWorkQuota)));
                Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Reconstruction, 1, 1000, out var forkCountScope));
                var forkCounter = new Refusal(forkCountScope, int.MaxValue);
                var forkTrial = seed.ForkOwned(forkCounter);
                forkTrial.SealPrepared();
                forkCountScope.Attach(forkTrial); int forkCalls = forkCounter.Calls; forkCountScope.Discard();
                for (int nth = 1; nth <= forkCalls; nth++)
                {
                    Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Reconstruction, 1, 1000 + nth, out var scope));
                    var refusal = new Refusal(scope, nth);
                    Assert.Throws<InvalidOperationException>(() => seed.ForkOwned(refusal));
                    Assert.AreEqual(nth, refusal.Calls); scope.Discard();
                    Assert.Zero(fixture.Budget.HeldBytes); Assert.Greater(fixture.Budget.PhysicalBytes, 0);
                }
                forkTrial.ReleaseOwner(); seed.ReleaseOwner();
                Assert.Zero(fixture.Budget.HeldBytes); Assert.AreEqual(Unique(fixture.Input.StorageRoot), fixture.Budget.PhysicalBytes);
            }
        }

        private static readonly object ArrivalMatrixProgressGate = new object();
        private const int ArrivalAdvanceReservationCount = 133111;

        private static void ArrivalMatrixProgress(string message)
        {
            lock (ArrivalMatrixProgressGate) {
                TestContext.Progress.WriteLine(message);
                var path = Environment.GetEnvironmentVariable("SPACEWARS_ARRIVAL_MATRIX_PROGRESS");
                if (!string.IsNullOrEmpty(path)) System.IO.File.AppendAllText(path, message + Environment.NewLine);
            }
        }

        private static int CountArrivalAdvanceReservations(OwnedArrivalFixture fixture, int worker)
        {
            Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 2000, out var countScope));
            var counter = new Refusal(countScope, int.MaxValue);
            NavigationArrivalWork trial = null;
            try
            {
                trial = NavigationArrivalWork.CreateOwned(fixture.Input, counter);
                ArrivalMatrixProgress($"Arrival allNth worker {worker}: counting Advance begins, create calls={counter.Calls}");
                trial.Advance(1, 10000, ArrivalWorkQuota);
                ArrivalMatrixProgress($"Arrival allNth worker {worker}: counting Advance ends, calls={counter.Calls}");
                trial.SealPrepared(); countScope.Attach(trial);
                return counter.Calls;
            }
            finally { trial?.ReleaseOwner(); countScope.Discard(); }
        }

        [Test]
        public void BodyOwnedArrivalWorkEveryNthAdvanceRefusalDiscardsFaultedWorkAndScratch()
        {
            const int workers = 3;
            var counts = new int[workers];
            var covered = new System.Collections.Generic.List<int>[workers];
            var failures = new Exception[workers];
            var threads = new System.Threading.Thread[workers];
            for (int worker = 0; worker < workers; worker++)
            {
                int index = worker;
                threads[index] = new System.Threading.Thread(() =>
                {
                    try
                    {
                        // Construct and dispose every owner on this synchronous thread.
                        // No ledger or live physical DAG crosses a thread boundary.
                        using (var fixture = new OwnedArrivalFixture(false))
                        {
                            int calls = CountArrivalAdvanceReservations(fixture, index);
                            counts[index] = calls;
                            covered[index] = new System.Collections.Generic.List<int>();
                            Assert.AreEqual(ArrivalAdvanceReservationCount, calls, "The complete original source Advance count is fixed.");
                            Assert.Greater(calls, 0); Assert.Zero(fixture.Budget.HeldBytes);
                            long inputPhysicalBytes = Unique(fixture.Input.StorageRoot);
                            Assert.AreEqual(inputPhysicalBytes, fixture.Budget.PhysicalBytes);
                            for (int nth = 1; nth <= calls; nth++)
                            {
                                if ((nth - 1) % workers != index) continue;
                                Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 2000 + nth, out var scope));
                                var refusal = new Refusal(scope, nth);
                                NavigationArrivalWork work = null;
                                try
                                {
                                    work = NavigationArrivalWork.CreateOwned(fixture.Input, refusal);
                                    try { work.Advance(1, 10000, ArrivalWorkQuota); } catch (InvalidOperationException) { }
                                    scope.Attach(work);
                                }
                                catch (InvalidOperationException) { }
                                finally { work?.ReleaseOwner(); scope.Discard(); }
                                Assert.AreEqual(nth, refusal.Calls);
                                Assert.Zero(fixture.Budget.HeldBytes);
                                Assert.AreEqual(inputPhysicalBytes, fixture.Budget.PhysicalBytes);
                                covered[index].Add(nth);
                                if (covered[index].Count % 100 == 0 || nth + workers > calls)
                                    ArrivalMatrixProgress($"Arrival allNth worker {index}: through {nth}/{calls}, covered={covered[index].Count}");
                            }
                            Assert.Zero(fixture.Budget.HeldBytes);
                            Assert.AreEqual(inputPhysicalBytes, fixture.Budget.PhysicalBytes);
                        }
                    }
                    catch (Exception error) { failures[index] = error; }
                });
                threads[index].Start();
            }
            foreach (var thread in threads) thread.Join();
            foreach (var failure in failures)
                if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.True(counts.All(count => count == ArrivalAdvanceReservationCount), "Worker-local full count passes must agree");
            CollectionAssert.AreEqual(Enumerable.Range(1, ArrivalAdvanceReservationCount), covered.SelectMany(items => items).OrderBy(n => n),
                "Every original refusal index must execute exactly once");
        }

        [Test]
        public void BodyOwnedArrivalWorkEveryNthCancellationRefusalReleasesOwnerAndScratch()
        {
            using (var fixture = new OwnedArrivalFixture(false))
            {
                Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 4000, out var countScope));
                var counter = new Refusal(countScope, int.MaxValue);
                var trial = NavigationArrivalWork.CreateOwned(fixture.Input, counter);
                trial.Advance(1, 1, ArrivalWorkQuota);
                trial.DiscardCancelledDerivatives();
                trial.SealPrepared();
                countScope.Attach(trial); int calls = counter.Calls; countScope.Discard();
                trial.ReleaseOwner();
                Assert.Greater(calls, 0); Assert.Zero(fixture.Budget.HeldBytes);
                for (int nth = 1; nth <= calls; nth++)
                {
                    Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 4000 + nth, out var scope));
                    var refusal = new Refusal(scope, nth);
                    NavigationArrivalWork work = null;
                    try
                    {
                        work = NavigationArrivalWork.CreateOwned(fixture.Input, refusal);
                        work.Advance(1, 1, ArrivalWorkQuota);
                        work.DiscardCancelledDerivatives();
                        work.SealPrepared();
                        scope.Attach(work);
                    }
                    catch (InvalidOperationException) { }
                    finally { work?.ReleaseOwner(); scope.Discard(); }
                    Assert.AreEqual(nth, refusal.Calls);
                    Assert.Zero(fixture.Budget.HeldBytes);
                    Assert.AreEqual(Unique(fixture.Input.StorageRoot), fixture.Budget.PhysicalBytes);
                }
            }
        }

        [Test]
        public void BodyOwnedArrivalWorkPageRequestAndResultShareOneImmutableRouteRow()
        {
            using (var fixture = new OwnedArrivalFixture(false, multipleCandidates: true))
            {
                var work = fixture.NewWork(w =>
                {
                    var routeQuota = new NavEpochQuota(10000, 10000, 1, 10000, 10000);
                    for (long epoch = 0; epoch < 4096 && ResultRowCount(w) == 0; epoch++)
                        w.Advance(epoch, 10000, routeQuota);
                    var counters=w.RouteCounters;
                    Assert.Greater(ResultRowCount(w), 0, $"phase={w.WorkPhase}, status={w.Status}, member={w.MemberCursor}, requests={RequestRowCount(w)}, pending={w.PendingRouteCount}, ready={w.ReadyRouteCount}, graph={counters.PhysicalGraphWork}, field={counters.PhysicalFieldWork}, connector={counters.ConnectorWork}, retained={w.StructuralRetainedBytes}, blocked={w.LastAdvanceRetentionBlocked}, layout={w.RetentionBreakdown}");
                    Assert.Less(ResultRowCount(w), RequestRowCount(w));
                });
                var requests = (System.Collections.IEnumerable)typeof(NavigationArrivalWork).GetField("pageRequests", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(work);
                var results = typeof(NavigationArrivalWork).GetField("pageResults", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(work);
                var resultRow = (System.Collections.IEnumerable)results.GetType().GetProperty("Values").GetValue(results);
                Assert.NotNull(resultRow);
                foreach (var row in resultRow.Cast<object>())
                {
                    Assert.True(requests.Cast<object>().Any(candidate => ReferenceEquals(candidate, row)));
                    Assert.True(((NavOwnedStorage)row).IsImmutableCaptureRoot);
                }
                work.ReleaseOwner();
                fixture.ReleaseInput();
                Assert.Zero(fixture.Budget.PhysicalBytes); Assert.Zero(fixture.Budget.HeldBytes);
            }
        }

        private static int ResultRowCount(NavigationArrivalWork work)
            => (int)typeof(NavigationArrivalWork).GetField("pageResults", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(work).GetType().GetProperty("Count").GetValue(typeof(NavigationArrivalWork)
                    .GetField("pageResults", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(work));

        private static int RequestRowCount(NavigationArrivalWork work)
            => ((System.Collections.IEnumerable)typeof(NavigationArrivalWork)
                .GetField("pageRequests", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(work)).Cast<object>().Count();

        private sealed class ArrivalWorkTestCleanup : IDisposable
        {
            internal sealed class Entry
            {
                private Action release;
                internal Entry(Action release) { this.release = release; }
                internal void Run() { var action = release; release = null; action?.Invoke(); }
            }
            private readonly System.Collections.Generic.List<Entry> entries = new System.Collections.Generic.List<Entry>();
            internal Entry Track(Action release) { var entry = new Entry(release); entries.Add(entry); return entry; }
            public void Dispose()
            {
                Exception failure = null;
                for (int n = entries.Count - 1; n >= 0; n--) {
                    try { entries[n].Run(); } catch (Exception error) { if (failure == null) failure = error; }
                }
                entries.Clear();
                if (failure != null) throw failure;
            }
        }

        [Test]
        public void BodyOwnedArrivalWorkSixtyFiveMixedRadiiHoldMembersReorderAndColdContinuation()
        {
            var cleanup = new ArrivalWorkTestCleanup();
            try {

            var provider = new LayeredNavigationProvider("physical-arrival-65", 1, 1, 1,
                NavigationArrivalTestFactory.Sha("physical-arrival-65"),
                new[] { new NavLayerSurface("ground", "ground", 0, 1, NavigationArrivalTestFactory.Area(30)) },
                Array.Empty<NavLayerPortal>());
            var candidates = Enumerable.Range(0, 65).Select(n => NavigationArrivalTestFactory.At(
                -16 + 4 * (n % 9), -12 + 4 * (n / 9))).ToArray();
            var clearances = new[] {
                new NavClearanceProfile("ground", 1, "ground", 1, .2),
                new NavClearanceProfile("ground", 1, "ground", 1, .3),
                new NavClearanceProfile("ground", 1, "ground", 1, .4)
            };
            var members = Enumerable.Range(0, 65).Select(n => new NavArrivalMember(
                n + 1, 1, n, 0, candidates[n], clearances[n % 3], 1, 7, 1,
                n + 10, 1, 1, n + 1, 900 + n + 1, "HOLD")).ToArray();
            var profile = new NavGraphProfile(NavigationArrivalTestFactory.Sha("physical-arrival-65-profile"), "distance", 1, 1);
            var limits = new NavGraphCompileLimits(2000, 2000, 10000);
            var arrival = new NavigationProfile();
            var source = new NavArrivalCandidateSource("physical-arrival-65", 1, candidates[0], candidates);
            var occupancy = NavigationArrivalTestFactory.Occupancy();
            var bindings = members.Select(m => new NavArrivalWorkingBinding(m.EntityId, provider, profile)).ToArray();
            var original = new NavArrivalInput("topology:" + provider.Binding, "profile:" + provider.Binding,
                provider, profile, arrival, limits, source, members, occupancy, 4096, bindings, true);
            var reordered = new NavArrivalInput("topology:" + provider.Binding, "profile:" + provider.Binding,
                provider, profile, arrival, limits, source, members.Reverse().ToArray(), occupancy, 4096,
                members.Reverse().Select(m => new NavArrivalWorkingBinding(m.EntityId, provider, profile)).ToArray(), true);
            Assert.AreEqual(original.StableKey, reordered.StableKey);

            var budget = new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 650, out var birth));
            cleanup.Track(() => birth.Discard());
            var r = birth.OwnedReservation();
            var ownedProvider = LayeredNavigationProvider.CopyOwned((LayeredNavigationProvider)provider, r);
            var ownedProviderOwner = cleanup.Track(ownedProvider.StorageRoot.ReleaseOwner);
            var common = NavGraphMetadata.CopyOptionalUnowned(null, profile, limits, r);
            var commonOwner = cleanup.Track(common.Release);
            var ownedArrival = NavigationProfile.CopyOwned(arrival, r);
            var ownedArrivalOwner = cleanup.Track(ownedArrival.StorageRoot.ReleaseOwner);
            var ownedSource = NavArrivalCandidateSource.CopyOwned(source, r);
            var ownedSourceOwner = cleanup.Track(ownedSource.StorageRoot.ReleaseOwner);
            var ownedOccupancy = NavArrivalOccupancySnapshot.CopyOwned(occupancy, Array.Empty<NavArrivalOccupant>(), r);
            var ownedOccupancyOwner = cleanup.Track(ownedOccupancy.StorageRoot.ReleaseOwner);
            var clearanceOwners = new NavGraphMetadata[clearances.Length];
            var clearanceReleases = new ArrivalWorkTestCleanup.Entry[clearances.Length];
            for (int n = 0; n < clearances.Length; n++) {
                clearanceOwners[n] = NavGraphMetadata.CopyOptionalUnowned(clearances[n], null, null, r);
                clearanceReleases[n] = cleanup.Track(clearanceOwners[n].Release);
            }
            var ownedMembers = new NavArrivalMember[members.Length];
            var memberReleases = new ArrivalWorkTestCleanup.Entry[members.Length];
            for (int n = 0; n < members.Length; n++) {
                ownedMembers[n] = NavArrivalMember.CopyOwned(members[n], clearanceOwners[n % 3].Clearance, r);
                memberReleases[n] = cleanup.Track(ownedMembers[n].StorageRoot.ReleaseOwner);
            }
            var ownedBindings = new NavArrivalWorkingBinding[members.Length];
            var bindingReleases = new ArrivalWorkTestCleanup.Entry[members.Length];
            for (int n = 0; n < members.Length; n++) {
                ownedBindings[n] = NavArrivalWorkingBinding.CreateOwned(ownedMembers[n].EntityId, ownedProvider, common.Profile, r);
                bindingReleases[n] = cleanup.Track(ownedBindings[n].StorageRoot.ReleaseOwner);
            }
            var input = NavArrivalInput.CopyOwned(original, ownedProvider, common.Profile, ownedArrival,
                common.Limits, ownedSource, ownedMembers, ownedOccupancy, ownedBindings, r);
            bool inputAttached = false, inputPublished = false;
            var inputOwner = cleanup.Track(() => { if (!inputAttached || inputPublished) input?.StorageRoot.ReleaseOwner(); });
            ownedProviderOwner.Run(); commonOwner.Run(); ownedArrivalOwner.Run();
            ownedSourceOwner.Run(); ownedOccupancyOwner.Run();
            foreach (var owner in clearanceReleases) owner.Run();
            foreach (var owner in memberReleases) owner.Run();
            foreach (var owner in bindingReleases) owner.Run();
            birth.Attach(input.StorageRoot); inputAttached = true;
            birth.Commit(input.StorageRoot, _ => inputPublished = true);

            NavigationArrivalWork work = null;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 651, out var operation));
            cleanup.Track(() => operation.Discard());
            work = NavigationArrivalWork.CreateOwned(input, operation.OwnedReservation());
            var workOwner = cleanup.Track(work.ReleaseOwner);
            // Deliberately capture a partial preparation BODY: the fixed allocation grant
            // of 64 finishes page 0 and leaves page 1 in its original null default slots.
            Assert.AreEqual(NavArrivalStatus.BudgetExhausted,
                work.Advance(1, 64, new NavEpochQuota(20000, 20000, 20000, 20000, 20000)));
            var partialIndices = (NavPagedList<NavRetainedArray<int>>)typeof(NavigationArrivalWork)
                .GetField("preparationCandidateIndices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(work);
            var partialRegions = (NavPagedList<NavTerminalRegion>)typeof(NavigationArrivalWork)
                .GetField("preparationRegions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(work);
            Assert.AreEqual(2, partialIndices.Count); Assert.AreEqual(2, partialRegions.Count);
            Assert.NotNull(partialIndices[0]); Assert.NotNull(partialRegions[0]);
            Assert.IsNull(partialIndices[1]); Assert.IsNull(partialRegions[1]);
            work.SealPrepared(); operation.Attach(work); operation.Commit(work, _ => { });
            var partialBytes = CaptureGraph(budget, work);
            var partialCharge = Unique(work);
            var memberStorage = (NavRetainedArray<NavArrivalMember>)typeof(NavigationArrivalWork)
                .GetField("membersStorage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(work);
            Assert.AreNotSame(input.MembersBacking, memberStorage.ReadOnlyBacking);

            NavArrivalStatus Next(NavigationArrivalWork value)
                => value.Advance(2, 10000, new NavEpochQuota(20000, 20000, 20000, 20000, 20000));
            NavArrivalStatus expectedNextStatus = NavArrivalStatus.Pending;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction, 1, 652, out var nextScope));
            cleanup.Track(() => nextScope.Discard());
            var next = work.ForkOwned(nextScope.OwnedReservation());
            var nextOwner = cleanup.Track(next.ReleaseOwner);
            expectedNextStatus = Next(next); next.SealPrepared(); nextScope.Attach(next); nextScope.Commit(next, _ => { });
            var expectedNext = CaptureGraph(budget, next);
            var expectedNextCharge = Unique(next);
            CollectionAssert.AreEqual(partialBytes, CaptureGraph(budget, work));

            NavArrivalStatus expectedFullStatus = NavArrivalStatus.Pending;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction, 1, 653, out var fullScope));
            cleanup.Track(() => fullScope.Discard());
            var full = work.ForkOwned(fullScope.OwnedReservation());
            var fullOwner = cleanup.Track(full.ReleaseOwner);
            var firstEpoch = Math.Max(0, full.LogicalEpoch + 1);
            for (long epoch = firstEpoch; epoch < firstEpoch + 128 &&
                 full.Status != NavArrivalStatus.Ready && full.Status != NavArrivalStatus.InvalidInput; epoch++)
                expectedFullStatus = full.Advance(epoch, 10000, new NavEpochQuota(20000, 20000, 20000, 20000, 20000));
            Assert.AreEqual(NavArrivalStatus.Ready, expectedFullStatus);
            Assert.AreEqual(65, full.Proposals.Count);
            Assert.AreEqual(2, full.TerminalPageCount);
            Assert.AreEqual(65, full.Proposals.Select(p => p.Member.EntityId).Distinct().Count());
            Assert.True(full.Proposals.All(p => p.Proof.ValidateAgainst(input, p.Member, p.Endpoint)));
            full.SealPrepared(); fullScope.Attach(full); fullScope.Commit(full, _ => { });
            var expectedFull = CaptureGraph(budget, full);
            var expectedFullCharge = Unique(full);
            CollectionAssert.AreEqual(partialBytes, CaptureGraph(budget, work));

            workOwner.Run(); nextOwner.Run(); fullOwner.Run(); inputOwner.Run();
            cleanup.Dispose(); // clears every closed action before the bytes-only cold boundary
            work = null; next = null; full = null; input = null; memberStorage = null;
            partialIndices = null; partialRegions = null; ownedMembers = null; ownedBindings = null;
            ownedProvider = null; common = null; ownedArrival = null; ownedSource = null; ownedOccupancy = null;
            clearanceOwners = null; original = null; reordered = null; members = null; bindings = null;
            candidates = null; clearances = null; provider = null; profile = null; limits = null;
            arrival = null; source = null; occupancy = null; bindings = null;
            r = null; birth = null; operation = null; nextScope = null; fullScope = null;
            clearanceReleases = null; memberReleases = null; bindingReleases = null;
            Assert.Zero(budget.PhysicalBytes); Assert.Zero(budget.HeldBytes);
            var cold = new NavigationRetentionBudget();
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode, 1, 1, out var decode));
            cleanup.Track(() => decode.Discard());
            var restored = (NavigationArrivalWork)Decode(partialBytes, decode.OwnedReservation());
            var restoredOwner = cleanup.Track(restored.ReleaseOwner);
            decode.Attach(restored); decode.Commit(restored, _ => { });
            Assert.AreEqual(partialCharge, cold.PhysicalBytes);
            Assert.AreEqual(2, restored.TerminalPageCount);
            var coldIndices = (NavPagedList<NavRetainedArray<int>>)typeof(NavigationArrivalWork)
                .GetField("preparationCandidateIndices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(restored);
            var coldRegions = (NavPagedList<NavTerminalRegion>)typeof(NavigationArrivalWork)
                .GetField("preparationRegions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(restored);
            Assert.AreEqual(2, coldIndices.Count); Assert.AreEqual(2, coldRegions.Count);
            Assert.AreEqual(64, coldIndices[0].Count); Assert.AreEqual(64, coldRegions[0].Candidates.Count);
            Assert.IsNull(coldIndices[1]); Assert.IsNull(coldRegions[1]);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Reconstruction, 1, 2, out var coldNextScope));
            cleanup.Track(() => coldNextScope.Discard());
            var coldNext = restored.ForkOwned(coldNextScope.OwnedReservation());
            var coldNextOwner = cleanup.Track(coldNext.ReleaseOwner);
            Assert.AreEqual(expectedNextStatus, coldNext.Advance(2, 10000,
                new NavEpochQuota(20000, 20000, 20000, 20000, 20000)));
            coldNext.SealPrepared(); coldNextScope.Attach(coldNext); coldNextScope.Commit(coldNext, _ => { });
            CollectionAssert.AreEqual(expectedNext, CaptureGraph(cold, coldNext));
            Assert.AreEqual(expectedNextCharge, Unique(coldNext));
            // A separate fork from the untouched decoded root compares the full next
            // continuation; no checkpoint DTO or public replay is involved.
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Reconstruction, 1, 2, out var coldFullScope));
            cleanup.Track(() => coldFullScope.Discard());
            var coldFull = restored.ForkOwned(coldFullScope.OwnedReservation());
            var coldFullOwner = cleanup.Track(coldFull.ReleaseOwner);
            var resumedEpoch = Math.Max(0, coldFull.LogicalEpoch + 1);
            NavArrivalStatus actualFullStatus = NavArrivalStatus.Pending;
            for (long epoch = resumedEpoch; epoch < resumedEpoch + 128 && actualFullStatus != NavArrivalStatus.Ready; epoch++)
                actualFullStatus = coldFull.Advance(epoch, 10000, new NavEpochQuota(20000, 20000, 20000, 20000, 20000));
            Assert.AreEqual(expectedFullStatus, actualFullStatus);
            coldFull.SealPrepared(); coldFullScope.Attach(coldFull); coldFullScope.Commit(coldFull, _ => { });
            Assert.AreEqual(65, coldFull.Proposals.Count);
            CollectionAssert.AreEqual(expectedFull, CaptureGraph(cold, coldFull));
            Assert.AreEqual(expectedFullCharge, Unique(coldFull));
            restoredOwner.Run(); coldNextOwner.Run(); coldFullOwner.Run();
            Assert.Zero(cold.PhysicalBytes); Assert.Zero(cold.HeldBytes); Assert.Zero(cold.ActiveScopes);

            } finally { cleanup.Dispose(); }
        }


        [Test]
        public void BodyOwnedArrivalWorkDecodeRefusesEveryPhysicalAllocationAndRejectsBadStatusScalar()
        {
            using (var cleanup = new ArrivalWorkTestCleanup())
            {
            byte[] bytes;
            using (var fixture = new OwnedArrivalFixture(false))
            {
                var work = fixture.NewWork(value => Assert.AreEqual(NavArrivalStatus.BudgetExhausted,
                    value.Advance(1, 1, ArrivalWorkQuota)));
                var workOwner = cleanup.Track(work.ReleaseOwner);
                bytes = CaptureGraph(fixture.Budget, work);
                workOwner.Run(); fixture.ReleaseInput();
                Assert.Zero(fixture.Budget.PhysicalBytes); Assert.Zero(fixture.Budget.HeldBytes);
            }

            var countBudget = new NavigationRetentionBudget();
            Assert.True(countBudget.TryOpenScope(NavigationPreparationKind.Decode, 1, 7000, out var countScope));
            cleanup.Track(() => countScope.Discard());
            var counter = new Refusal(countScope, int.MaxValue);
            var counted = Decode(bytes, counter);
            var countedOwner = cleanup.Track(counted.ReleaseOwner);
            countScope.Attach(counted);
            int calls = counter.Calls;
            TestContext.WriteLine("NavigationArrivalWork BODY decode reservation calls: " + calls);
            countScope.Discard(); countedOwner.Run(); counted = null;
            Assert.Greater(calls, 0);
            Assert.Zero(countBudget.PhysicalBytes); Assert.Zero(countBudget.HeldBytes); Assert.Zero(countBudget.ActiveScopes);

            byte[] originalBytes = (byte[])bytes.Clone();
            for (int nth = 1; nth <= calls; nth++)
            {
                var cold = new NavigationRetentionBudget();
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode, 1, 7000 + nth, out var scope));
                cleanup.Track(() => scope.Discard());
                var refusal = new Refusal(scope, nth);
                Assert.Throws<InvalidOperationException>(() => Decode(bytes, refusal), "Decode allocation " + nth);
                Assert.AreEqual(nth, refusal.Calls);
                scope.Discard();
                Assert.Zero(cold.PhysicalBytes); Assert.Zero(cold.HeldBytes); Assert.Zero(cold.ActiveScopes);
            }
            CollectionAssert.AreEqual(originalBytes, bytes);

            using (var wire = new System.IO.BinaryReader(new System.IO.MemoryStream(bytes))) {
                Assert.AreEqual(NavPhysicalBodyWire.Magic, wire.ReadInt32());
                Assert.Greater(wire.ReadInt32(), 0);
                Assert.AreEqual(2, wire.ReadByte()); Assert.AreEqual(0, wire.ReadInt32());
                Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.ArrivalWork).Encoded, wire.ReadInt32());
            }
            var malformed = (byte[])bytes.Clone();
            int tail = malformed.Length - sizeof(int); // Work writes its final Status scalar after its fixed edge sequence.
            malformed[tail] = 0xe7; malformed[tail + 1] = 0x03;
            malformed[tail + 2] = 0; malformed[tail + 3] = 0; // enum value 999
            var rejected = new NavigationRetentionBudget();
            Assert.True(rejected.TryOpenScope(NavigationPreparationKind.Decode, 1, 8000, out var badScope));
            cleanup.Track(() => badScope.Discard());
            Assert.Throws<ArgumentException>(() => Decode(malformed, badScope.OwnedReservation()));
            badScope.Discard();
            Assert.Zero(rejected.PhysicalBytes); Assert.Zero(rejected.HeldBytes); Assert.Zero(rejected.ActiveScopes);

            var successful = new NavigationRetentionBudget();
            Assert.True(successful.TryOpenScope(NavigationPreparationKind.Decode, 1, 8001, out var finalScope));
            cleanup.Track(() => finalScope.Discard());
            var final = Decode(bytes, finalScope.OwnedReservation());
            var finalOwner = cleanup.Track(final.ReleaseOwner);
            finalScope.Attach(final); finalScope.Commit(final, _ => { });
            Assert.AreEqual(Unique(final), successful.PhysicalBytes);
            CollectionAssert.AreEqual(bytes, CaptureGraph(successful, final));
            finalOwner.Run();
            Assert.Zero(successful.PhysicalBytes); Assert.Zero(successful.HeldBytes); Assert.Zero(successful.ActiveScopes);
            }
        }

        [Test]
        public void BodyOwnedArrivalWorkFirstAdvanceReservationRefusalFaultsAndCleans()
        {
            using (var cleanup = new ArrivalWorkTestCleanup())
            using (var fixture = new OwnedArrivalFixture(false))
            {
                Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 9100, out var countScope));
                cleanup.Track(() => countScope.Discard());
                var counter = new Refusal(countScope, int.MaxValue);
                var countTrial = NavigationArrivalWork.CreateOwned(fixture.Input, counter);
                var countOwner = cleanup.Track(countTrial.ReleaseOwner);
                int createCalls = counter.Calls;
                countOwner.Run(); countScope.Discard();
                TestContext.WriteLine("NavigationArrivalWork CreateOwned reservations: " + createCalls);
                Assert.Greater(createCalls, 0);
                Assert.Zero(fixture.Budget.HeldBytes);
                Assert.AreEqual(Unique(fixture.Input.StorageRoot), fixture.Budget.PhysicalBytes);

                Assert.True(fixture.Budget.TryOpenScope(NavigationPreparationKind.Admission, 1, 9101, out var scope));
                cleanup.Track(() => scope.Discard());
                var refusal = new Refusal(scope, createCalls + 1);
                var work = NavigationArrivalWork.CreateOwned(fixture.Input, refusal);
                var owner = cleanup.Track(work.ReleaseOwner);
                Assert.AreEqual(createCalls, refusal.Calls);
                Assert.Throws<InvalidOperationException>(() => work.Advance(1, 10000, ArrivalWorkQuota));
                Assert.AreEqual(createCalls + 1, refusal.Calls);
                Assert.Throws<InvalidOperationException>(() => work.Advance(2, 10000, ArrivalWorkQuota));
                Assert.Throws<InvalidOperationException>(() => work.SealPrepared());
                owner.Run(); scope.Discard();
                Assert.Zero(fixture.Budget.HeldBytes);
                Assert.AreEqual(Unique(fixture.Input.StorageRoot), fixture.Budget.PhysicalBytes);
            }
        }
    }
}

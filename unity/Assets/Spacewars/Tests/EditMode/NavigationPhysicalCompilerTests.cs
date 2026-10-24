using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        private static LayeredNavigationCompileWork AdjacencySource(NavigationRetentionBudget budget, int prefix)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction, 1, 1, out var scope));
            var work = LayeredNavigationCompileWork.CopyProviderAndCreateOwned(
                NavigationAdmission.CertifiedFlatProvider(new NavGeometry(3.2, Array.Empty<NavObstacle>(), 1)),
                new NavClearanceProfile("ground", 1, "ground", 1, .25),
                new NavGraphProfile(new string('a', 64), "distance", 1, .8),
                new NavGraphCompileLimits(2000, 2000, 10000), scope.OwnedReservation());
            int steps = 0;
            while (work.Phase != LayeredNavigationCompileWork.Stage.Adjacency) {
                Assert.Less(++steps, 20000); work.Advance(1);
            }
            for (int i = 0; i < prefix; i++) Assert.AreEqual(1, work.Advance(1));
            work.SealPrepared(); scope.Attach(work); scope.Commit(work, _ => {});
            Assert.AreEqual(Unique(work), budget.PhysicalBytes);
            return work;
        }

        private sealed class AdjacencyReservations : INavStorageReservation, INavStorageLedgerIdentity, INavStorageScratchAllocator
        {
            private readonly NavigationRetentionBudget.Scope scope;
            internal readonly List<long> Requests = new List<long>();
            internal AdjacencyReservations(NavigationRetentionBudget.Scope scope) { this.scope = scope; }
            public bool TryReserve(long bytes) { Requests.Add(bytes); return scope.TryReserve(bytes); }
            public void Register(NavOwnedStorage storage) => ((INavStorageAllocator)scope).Register(storage);
            public object Identity => scope.Identity;
            public bool Owns(NavOwnedStorage storage) => scope.Owns(storage);
            public IDisposable ClaimScratch(long bytes) => scope.ClaimScratch(bytes);
        }

        [TestCase(0, 12, 226, 53728)][TestCase(1, 12, 235, 55024)][TestCase(7, 17, 349, 78480)]
        public void AdjacencyPreparationPreservesOrderedBytesWithFewerAllocationsAndLowerOverlap(int prefix, int quota, int legacyCalls, long legacyOverlap)
        {
            var budget = new NavigationRetentionBudget();
            var source = AdjacencySource(budget, prefix);
            byte[] original = CaptureGraph(budget, source);
            long baseline = budget.PhysicalBytes;
            byte[] Run(bool batch, out int calls, out long overlap) {
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection, 1, 2, out var scope));
                var probe = new AdjacencyReservations(scope);
                var work = source.ForkOwned(probe);
                if (batch) Assert.AreEqual(quota, work.Advance(quota));
                else for (int i = 0; i < quota; i++) Assert.AreEqual(1, work.Advance(1));
                Assert.AreEqual(source.Consumed + quota, work.Consumed);
                CollectionAssert.AreEqual(original, CaptureGraph(budget, source), "capture while preparation owns a mutable list");
                work.SealPrepared();
                calls = probe.Requests.Count; overlap = budget.PhysicalBytes - baseline;
                scope.Attach(work); scope.Commit(work, _ => {});
                byte[] result = CaptureGraph(budget, work);
                work.ReleaseStorage(); Assert.AreEqual(baseline, budget.PhysicalBytes); Assert.Zero(budget.HeldBytes);
                CollectionAssert.AreEqual(original, CaptureGraph(budget, source));
                return result;
            }
            byte[] single = Run(false, out int oldCalls, out long oldOverlap);
            byte[] batchBytes = Run(true, out int newCalls, out long newOverlap);
            CollectionAssert.AreEqual(single, batchBytes, "ordered contents, cursors, capacities and final physical representation");
            Assert.AreEqual(oldCalls, newCalls); Assert.AreEqual(oldOverlap, newOverlap);
            // Legacy single-edge immutable results measured on the preceding
            // pilot snapshot; both new quota schedules now reuse prepared pages.
            Assert.Less(newCalls, legacyCalls); Assert.Less(newOverlap, legacyOverlap);
            TestContext.WriteLine($"adjacency prefix={prefix} quota={quota} legacyAllocations={legacyCalls} newUnitAndBatch={newCalls} legacyOverlap={legacyOverlap} newOverlap={newOverlap}");
            source.ReleaseStorage(); Assert.Zero(budget.PhysicalBytes); Assert.Zero(budget.HeldBytes);
        }

        [TestCase(0, 12)][TestCase(1, 12)][TestCase(7, 17)]
        public void AdjacencyPreparationEveryRealReservationRefusalDiscardsWithoutChangingPinnedSource(int prefix, int quota)
        {
            var budget = new NavigationRetentionBudget(); var source = AdjacencySource(budget, prefix);
            byte[] original = CaptureGraph(budget, source); long baseline = budget.PhysicalBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection, 1, 2, out var countScope));
            var count = new Refusal(countScope, int.MaxValue); var trial = source.ForkOwned(count);
            trial.Advance(quota); trial.SealPrepared(); int total = count.Calls; trial.ReleaseStorage(); countScope.Discard();
            Assert.Greater(total, 0); Assert.AreEqual(baseline, budget.PhysicalBytes);
            for (int nth = 1; nth <= total; nth++) {
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection, 1, 3, out var scope));
                var refusal = new Refusal(scope, nth); LayeredNavigationCompileWork work = null;
                Assert.Throws<InvalidOperationException>(() => { work = source.ForkOwned(refusal); work.Advance(quota); work.SealPrepared(); }, "real allocation " + nth);
                Assert.AreEqual(nth, refusal.Calls);
                work?.ReleaseStorage(); scope.Discard();
                Assert.AreEqual(baseline, budget.PhysicalBytes); Assert.Zero(budget.HeldBytes);
                Assert.Zero(budget.ActiveScopes); CollectionAssert.AreEqual(original, CaptureGraph(budget, source));
            }
            TestContext.WriteLine($"adjacency prefix={prefix} quota={quota} everyNth={total}/{total}");
            source.ReleaseStorage(); Assert.Zero(budget.PhysicalBytes); Assert.Zero(budget.HeldBytes);
        }

        [TestCase(0, 12)][TestCase(1, 12)][TestCase(7, 17)]
        public void AdjacencyPreparationBytesOnlyRestoreMatchesNextBatchAndAdmission(int prefix, int quota)
        {
            byte[] Next(NavigationRetentionBudget budget, LayeredNavigationCompileWork source, out long[] requests, out long charge) {
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection, 1, 2, out var scope));
                var probe = new AdjacencyReservations(scope); var work = source.ForkOwned(probe);
                Assert.AreEqual(quota, work.Advance(quota)); work.SealPrepared();
                requests = probe.Requests.ToArray(); scope.Attach(work); scope.Commit(work, _ => {});
                byte[] bytes = CaptureGraph(budget, work); charge = Unique(work); work.ReleaseStorage(); return bytes;
            }
            var hot = new NavigationRetentionBudget(); var original = AdjacencySource(hot, prefix);
            byte[] bytes = CaptureGraph(hot, original);
            byte[] expected = Next(hot, original, out var hotRequests, out long hotCharge);
            original.ReleaseStorage(); original = null; Assert.Zero(hot.PhysicalBytes); Assert.Zero(hot.HeldBytes);
            var cold = new NavigationRetentionBudget();
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode, 1, 1, out var decode));
            var restored = (LayeredNavigationCompileWork)Decode(bytes, decode.OwnedReservation());
            decode.Attach(restored); decode.Commit(restored, _ => {});
            CollectionAssert.AreEqual(bytes, CaptureGraph(cold, restored));
            CollectionAssert.AreEqual(expected, Next(cold, restored, out var coldRequests, out long coldCharge));
            CollectionAssert.AreEqual(hotRequests, coldRequests); Assert.AreEqual(hotCharge, coldCharge);
            restored.ReleaseStorage(); Assert.Zero(cold.PhysicalBytes); Assert.Zero(cold.HeldBytes);
        }

        [Test]
        public void AdjacencyPreparationNearCapRefusalReleasesPreparedStateWithOldSnapshotPinned()
        {
            var budget = new NavigationRetentionBudget(); var source = AdjacencySource(budget, 1);
            byte[] original = CaptureGraph(budget, source); long baseline = budget.PhysicalBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection, 1, 2, out var scope));
            var work = source.ForkOwned(scope.OwnedReservation());
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture, 1, 3, out var overlap));
            // A real live neighbor, not an artificial held-credit counter,
            // consumes the remaining ledger capacity while the old root stays pinned.
            int count = checked((int)((LayeredNavigationScheduler.MaxRetainedBytes - budget.RetainedBytes
                - NavStorageLayout.Charge(96) - 1) / NavRetainedArray<int>.ElementBytes));
            var input = new int[count]; input[count - 1] = 731;
            var neighbor = NavRetainedArray<int>.Copy(input, overlap.TryReserve); input = null;
            Assert.AreEqual(731, neighbor[count - 1]);
            Assert.Less(LayeredNavigationScheduler.MaxRetainedBytes - budget.RetainedBytes, NavRetainedArray<int>.ElementBytes + 1);
            Assert.Throws<InvalidOperationException>(() => work.Advance(12));
            Assert.LessOrEqual(budget.PeakRetainedBytes, LayeredNavigationScheduler.MaxRetainedBytes);
            work.ReleaseStorage(); scope.Discard(); neighbor.Release(); overlap.Discard();
            Assert.AreEqual(baseline, budget.PhysicalBytes); Assert.Zero(budget.HeldBytes); Assert.Zero(budget.ActiveScopes);
            CollectionAssert.AreEqual(original, CaptureGraph(budget, source));
            source.ReleaseStorage(); Assert.Zero(budget.PhysicalBytes);
        }

        [TestCase("flat",.8)][TestCase("layered",8d)][TestCase("partition",16d)]
        public void BodyActualCompilerStagesPreserveColdBytesNextCowReadyForkAndSharedOwners(string kind,double cell)
        {
            ILayeredNavigationProvider Input(){if(kind=="partition"){var terrain=ThreeCrossingsMap.Default;return new PartitionNavigationProvider(terrain,new NavGeometry(terrain.HalfExtent,terrain.MovementBlockers.ToArray(),terrain.Revision),null);}return kind=="layered"?(ILayeredNavigationProvider)new StackedNavigationFixture(true,NavPortalDirection.Forward).Provider:NavigationAdmission.CertifiedFlatProvider(new NavGeometry(3.2,Array.Empty<NavObstacle>(),1));}
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('f',64),"distance",1,cell);var limits=new NavGraphCompileLimits(2000,2000,10000);
            // Discover actual transition boundaries from the original Step; the
            // witness creates no alternative compiler or candidate blueprint.
            var boundaries=new SortedSet<int>{-1,0,1};var discoveryBudget=new NavigationRetentionBudget();Assert.True(discoveryBudget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var discovery));var original=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(Input(),clearance,profile,limits,discovery.OwnedReservation());
            int total=0;var prior=original.Phase;while(original.Phase!=LayeredNavigationCompileWork.Stage.Ready){Assert.Less(++total,20000);original.Advance(1);if(original.Phase!=prior){boundaries.Add(total-1);boundaries.Add(total);if(original.Phase!=LayeredNavigationCompileWork.Stage.Ready)boundaries.Add(total+1);prior=original.Phase;}}
            boundaries.Add(total);original.ReleaseStorage();discovery.Discard();Assert.Zero(discoveryBudget.PhysicalBytes);Assert.Zero(discoveryBudget.HeldBytes);
            foreach(int at in boundaries){
                var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(Input(),clearance,profile,limits,initial.OwnedReservation());if(at!=0)compiler.Advance(Math.Max(at,0));compiler.SealPrepared();
                var bundle=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{compiler,compiler,compiler.Graph},initial.TryReserve);compiler.ReleaseStorage();initial.Attach(bundle);initial.Commit(bundle,_=>{});Assert.AreEqual(Unique(bundle),hot.PhysicalBytes,"source unique "+kind+" "+at);
                byte[] bytes=CaptureGraph(hot,bundle);long charge=hot.PhysicalBytes;
                byte[] Next(NavigationRetentionBudget budget,LayeredNavigationCompileWork work,out long nextCharge,out int used){
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,20,out var prepare));var fork=work.ForkOwned(prepare.OwnedReservation());used=fork.Advance(1);fork.SealPrepared();prepare.Attach(fork);prepare.Commit(fork,_=>{});byte[] next=CaptureGraph(budget,fork,21);nextCharge=Unique(fork);fork.ReleaseStorage();return next;
                }
                byte[] expectedNext=Next(hot,compiler,out long expectedCharge,out int expectedUsed);Assert.AreEqual(charge,hot.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,bundle,22));
                var phase=compiler.Phase;int consumed=compiler.Consumed;bundle.Release();bundle=null;compiler=null;Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
                var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});var result=(LayeredNavigationCompileWork)restored[0];Assert.AreSame(result,restored[1]);Assert.AreSame(result.Graph,restored[2]);Assert.AreEqual(phase,result.Phase);Assert.AreEqual(consumed,result.Consumed);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
                CollectionAssert.AreEqual(expectedNext,Next(cold,result,out long actualCharge,out int actualUsed),"next exact "+kind+" "+at);Assert.AreEqual(expectedCharge,actualCharge);Assert.AreEqual(expectedUsed,actualUsed);Assert.AreEqual(charge,cold.PhysicalBytes);restored.Release();restored=null;result=null;Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
                if(phase==LayeredNavigationCompileWork.Stage.Ready){var forkBudget=new NavigationRetentionBudget();Assert.True(forkBudget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var forkDecode));var readyFork=(LayeredNavigationCompileWork)Decode(expectedNext,forkDecode.OwnedReservation());forkDecode.Attach(readyFork);forkDecode.Commit(readyFork,_=>{});Assert.AreEqual(expectedCharge,forkBudget.PhysicalBytes);Assert.AreEqual(Unique(readyFork),forkBudget.PhysicalBytes);CollectionAssert.AreEqual(expectedNext,CaptureGraph(forkBudget,readyFork));readyFork.ReleaseStorage();Assert.Zero(forkBudget.PhysicalBytes);Assert.Zero(forkBudget.HeldBytes);}
                // Rich partial state and Ready exercise every child, aggregate,
                // and validation admission. Existing everyNth container/graph
                // witnesses cover individual child variants as well.
                if(at==total||at==total-1){
                    var countBudget=new NavigationRetentionBudget();Assert.True(countBudget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var counting));var counter=new Refusal(counting,int.MaxValue);var trial=Decode(bytes,counter);counting.Attach(trial);int allocations=counter.Calls;counting.Discard();Assert.Zero(countBudget.PhysicalBytes);Assert.Zero(countBudget.HeldBytes);
                    for(int nth=1;nth<=allocations;nth++){var rejectedBudget=new NavigationRetentionBudget();Assert.True(rejectedBudget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing),kind+" "+at+" allocation "+nth);Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(rejectedBudget.PhysicalBytes);Assert.Zero(rejectedBudget.HeldBytes);}
                }
            }
        }
        [TestCase(false)][TestCase(true)]
        public void BodyCompilerMalformedPhaseAndCountersFailClosedWithoutSourceMutation(bool partition)
        {
            ILayeredNavigationProvider input;if(partition){var terrain=ThreeCrossingsMap.Default;input=new PartitionNavigationProvider(terrain,new NavGeometry(terrain.HalfExtent,terrain.MovementBlockers.ToArray(),terrain.Revision),null);}else input=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(input,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('c',64),"distance",1,partition?16:1),new NavGraphCompileLimits(2000,2000,10000),initial.OwnedReservation());int steps=0;while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready){Assert.Less(++steps,20000);compiler.Advance(1);}compiler.SealPrepared();initial.Attach(compiler);initial.Commit(compiler,_=>{});byte[] bytes=CaptureGraph(budget,compiler);long charge=budget.PhysicalBytes;
            // Fixed scalar macro header follows the envelope and root tag.
            // Changing Consumed to another nonnegative value must also fail.
            foreach(var change in new[]{Tuple.Create(17,999),Tuple.Create(21,compiler.Consumed+1),Tuple.Create(25,-1),Tuple.Create(61,999),Tuple.Create(65,999)}){
                var invalid=(byte[])bytes.Clone();Buffer.BlockCopy(BitConverter.GetBytes(change.Item2),0,invalid,change.Item1,4);var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));Assert.Throws<ArgumentException>(()=>Decode(invalid,rejected.OwnedReservation()));rejected.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.AreEqual(charge,budget.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(budget,compiler,3));
            }
            compiler.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
        }

        private static NavOwnedStorage[] AdjacencyOwnedRoots(NavigationRetentionBudget.Scope scope,params NavOwnedStorage[] roots)
        {
            var all=new List<NavOwnedStorage>(roots);
            var head=(NavOwnedStorage)typeof(NavigationRetentionBudget.Scope).GetField("births",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scope);
            var seen=new HashSet<NavOwnedStorage>();
            while(head!=null){Assert.True(seen.Add(head),"Birth journal must not alias or cycle.");all.Add(head);head=head.BirthNext;}
            return all.ToArray();
        }
        private sealed class AdjacencyLocalProbe : INavStorageReservation,INavStorageLocalBirthAllocator,INavStorageScratchAllocator,INavStorageLedgerIdentity
        {
            private readonly NavigationRetentionBudget.Scope scope;
            internal readonly List<long> Scratch=new List<long>();
            internal readonly List<Func<long,bool>> Callbacks=new List<Func<long,bool>>();
            internal bool Local=true;internal int LocalTransfers,Fallbacks;
            internal AdjacencyLocalProbe(NavigationRetentionBudget.Scope scope){this.scope=scope;}
            public bool TryReserve(long bytes)=>scope.TryReserve(bytes);
            public void Register(NavOwnedStorage storage){Fallbacks++;((INavStorageAllocator)scope).Register(storage);}
            public bool TryRegisterLocalBirth(NavOwnedStorage storage){
                if(!Local)return false;
                if(storage is NavPagedList<NavGraphArc>){
                    var callback=(Func<long,bool>)typeof(NavPagedList<NavGraphArc>).GetField("reserveCopy",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(storage);
                    if(callback?.Target?.GetType().Name=="AdjacencyBirthRegion")Callbacks.Add(callback);
                }
                LocalTransfers++;return scope.TryRegisterLocalBirth(storage);
            }
            public IDisposable ClaimScratch(long bytes){Scratch.Add(bytes);return scope.ClaimScratch(bytes);}
            public object Identity=>scope.Identity;
            public bool Owns(NavOwnedStorage storage)=>scope.Owns(storage);
        }
        private sealed class AdjacencyLegacyProbe : INavStorageReservation,INavStorageLedgerIdentity
        {
            private readonly NavigationRetentionBudget.Scope scope;internal int Calls;
            internal AdjacencyLegacyProbe(NavigationRetentionBudget.Scope scope){this.scope=scope;}
            public bool TryReserve(long bytes){Calls++;return scope.TryReserve(bytes);}
            public void Register(NavOwnedStorage storage)=>((INavStorageAllocator)scope).Register(storage);
            public object Identity=>scope.Identity;
            public bool Owns(NavOwnedStorage storage)=>scope.Owns(storage);
        }
        [Test,Timeout(180000)]
        public void OwnedAdjacencyLocalBirthsLowerLivePhysicalBeforeResolveWithExactBody()
        {
            var budget=new NavigationRetentionBudget();var source=AdjacencySource(budget,0);var bytes=CaptureGraph(budget,source);long baseline=budget.PhysicalBytes;
            byte[] Run(bool local,out long retained){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));
                // The opaque baseline preserves local AVL births, but its normal
                // list factories keep the original whole-Solve birth journal.
                INavStorageReservation reservation=local?scope.OwnedReservation():new NavStorageReservation(scope.TryReserve,scope);
                var work=source.ForkOwned(reservation);
                for(int i=0;i<64;i++){
                    Assert.AreEqual(1,work.Advance(1));
                    Assert.AreEqual(NavigationStorageOwnershipTests.Unique(AdjacencyOwnedRoots(scope,source,work)),budget.PhysicalBytes,"Every live physical owner is independently reachable, including existing scope births.");
                    Assert.AreEqual(NavigationRetentionBudget.ScopeBytes,budget.HeldBytes,"Region metadata scratch must return before whole Solve resolves.");
                    Assert.AreEqual(1,budget.ActiveScopes);
                }
                retained=budget.PhysicalBytes;work.SealPrepared();var result=CaptureGraph(budget,work);
                CollectionAssert.AreEqual(bytes,CaptureGraph(budget,source));scope.Attach(work);scope.Commit(work,_=>{});work.ReleaseStorage();Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);return result;
            }
            var legacy=Run(false,out long oldLive);var actual=Run(true,out long newLive);
            CollectionAssert.AreEqual(legacy,actual);Assert.Less(newLive,oldLive,"Only list birth lifetime changes; baseline already uses mutation-local AVL births.");
            TestContext.WriteLine("owned adjacency preResolve legacy="+oldLive+" local="+newLive+" delta="+(oldLive-newLive));
            source.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test,Timeout(180000)]
        public void OwnedAdjacencyMetadataIsPrepaidAndOptionalLocalFallbackOwnsEachBirthOnce()
        {
            var budget=new NavigationRetentionBudget();var source=AdjacencySource(budget,0);long baseline=budget.PhysicalBytes;var original=CaptureGraph(budget,source);
            foreach(bool local in new[]{true,false}){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));var probe=new AdjacencyLocalProbe(scope){Local=local};var work=source.ForkOwned(probe);
                work.Advance(12);Assert.AreEqual(12,probe.Scratch.Count);Assert.True(probe.Scratch.All(v=>v==256),"64 region +128 concrete Mono delegate +64 existing scratch lease.");
                Assert.AreEqual(NavigationRetentionBudget.ScopeBytes,budget.HeldBytes);Assert.AreEqual(NavigationStorageOwnershipTests.Unique(AdjacencyOwnedRoots(scope,source,work)),budget.PhysicalBytes);
                if(local){
                    Assert.AreEqual(12,probe.Callbacks.Count);Assert.Greater(probe.LocalTransfers,0);
                    foreach(var callback in probe.Callbacks){
                        var fields=callback.Target.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                        Assert.AreEqual(5,fields.Length);Assert.AreEqual(4,fields.Count(f=>!f.FieldType.IsValueType));Assert.AreEqual(1,fields.Count(f=>f.FieldType==typeof(bool)));
                        int slots=0,flags=0;
                        for(Type type=callback.GetType();type!=typeof(object);type=type.BaseType)foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)){
                            if(field.FieldType==typeof(bool)){flags++;continue;}Assert.True(!field.FieldType.IsValueType||field.FieldType==typeof(IntPtr));slots++;
                            if(field.Name=="data"||field.Name=="delegates")Assert.IsNull(field.GetValue(callback),"No hidden DelegateData/invocation-array metadata.");
                        }
                        Assert.AreEqual(13,slots);Assert.AreEqual(1,flags);Assert.LessOrEqual(16+slots*8+8,128);
                    }
                }else{Assert.Zero(probe.LocalTransfers);Assert.Greater(probe.Fallbacks,0,"false uses original Register, never also links region BirthNext.");}
                work.ReleaseStorage();scope.Discard();Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(original,CaptureGraph(budget,source));
            }
            // Opaque adapters may advertise scratch/local-birth without support.
            // They must retain the existing callback path and never claim scratch.
            byte[] Legacy(bool opaque,out int calls){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var legacyScope));var legacy=new AdjacencyLegacyProbe(legacyScope);
                INavStorageReservation reservation=opaque?new NavStorageReservation(legacy.TryReserve,legacy):legacy;
                var legacyWork=source.ForkOwned(reservation);legacyWork.Advance(12);legacyWork.SealPrepared();calls=legacy.Calls;
                var bytes=CaptureGraph(budget,legacyWork);legacyWork.ReleaseStorage();legacyScope.Discard();Assert.AreEqual(baseline,budget.PhysicalBytes);return bytes;
            }
            var plain=Legacy(false,out int plainCalls);var wrapped=Legacy(true,out int wrappedCalls);CollectionAssert.AreEqual(plain,wrapped);Assert.AreEqual(plainCalls,wrappedCalls,"Opaque unsupported scratch/local markers add no reserve or journal.");
            source.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test,Timeout(180000)]
        public void OwnedAdjacencyEveryPaidReservationRefusalUnwindsConstructorAndIndexBirths()
        {
            var budget=new NavigationRetentionBudget();var source=AdjacencySource(budget,1);long baseline=budget.PhysicalBytes;var original=CaptureGraph(budget,source);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var countScope));int total=0;budget.AllocationAdmission=_=>{total++;return true;};
            LayeredNavigationCompileWork trial=null;
            try{trial=source.ForkOwned(countScope.OwnedReservation());trial.Advance(12);trial.SealPrepared();}finally{budget.AllocationAdmission=null;trial?.ReleaseStorage();countScope.Discard();}
            Assert.Greater(total,12);
            for(int nth=1;nth<=total;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var scope));int calls=0;budget.AllocationAdmission=_=>++calls!=nth;LayeredNavigationCompileWork work=null;
                try{Assert.Throws<InvalidOperationException>(()=>{work=source.ForkOwned(scope.OwnedReservation());work.Advance(12);work.SealPrepared();});Assert.AreEqual(nth,calls);}
                finally{budget.AllocationAdmission=null;work?.ReleaseStorage();scope.Discard();}
                Assert.AreEqual(baseline,budget.PhysicalBytes,"Failure "+nth+" includes unreturned constructor prefixes and index assignment.");Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(original,CaptureGraph(budget,source));
            }
            TestContext.WriteLine("owned adjacency every paid refusal="+total+"/"+total);source.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);
        }
        [Test,Timeout(180000)]
        public void OwnedAdjacencyCancellationReturnsLiveRegionScratchAndPinnedSource()
        {
            var budget=new NavigationRetentionBudget();var source=AdjacencySource(budget,0);var original=CaptureGraph(budget,source);long baseline=budget.PhysicalBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));var work=source.ForkOwned(scope.OwnedReservation());bool entered=false,cancelled=false;
            budget.AllocationAdmission=bytes=>{if(bytes==256&&!entered)entered=true;else if(entered&&!cancelled){cancelled=true;scope.RequestCancel();}return true;};
            try{Assert.Throws<InvalidOperationException>(()=>work.Advance(12));Assert.True(entered);Assert.True(cancelled);Assert.True(scope.IsCancelled);Assert.AreEqual(NavigationRetentionBudget.ScopeBytes,budget.HeldBytes);}
            finally{budget.AllocationAdmission=null;work.ReleaseStorage();scope.Discard();}
            Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(original,CaptureGraph(budget,source));source.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);
        }
        [Test,Timeout(180000)]
        public void OwnedAdjacencyDirectScopeColdNextBodyAndCreditsAreExact()
        {
            byte[] Next(NavigationRetentionBudget budget,LayeredNavigationCompileWork source,out long charge){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));var work=source.ForkOwned(scope.OwnedReservation());work.Advance(12);work.SealPrepared();scope.Attach(work);scope.Commit(work,_=>{});charge=Unique(work);Assert.AreEqual(NavigationStorageOwnershipTests.Unique(source,work),budget.PhysicalBytes);var result=CaptureGraph(budget,work);work.ReleaseStorage();return result;
            }
            var hot=new NavigationRetentionBudget();var source=AdjacencySource(hot,7);var bytes=CaptureGraph(hot,source);var next=Next(hot,source,out long hotCharge);source.ReleaseStorage();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(LayeredNavigationCompileWork)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
            CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(next,Next(cold,restored,out long coldCharge));Assert.AreEqual(hotCharge,coldCharge);restored.ReleaseStorage();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
        }
    }
}

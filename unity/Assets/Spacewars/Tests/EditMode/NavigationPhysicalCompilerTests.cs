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
    }
}

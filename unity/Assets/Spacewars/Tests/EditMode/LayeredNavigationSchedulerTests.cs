using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Spacewars.Runtime;
using NUnit.Framework;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class LayeredNavigationSchedulerTests
    {
        private static readonly NavClearanceProfile Clearance = new NavClearanceProfile("ground", 1, "ground", 1, .25);
        private static readonly NavGraphProfile Profile = new NavGraphProfile(new string('a', 64), "distance", 1, 1);
        private static readonly NavGraphCompileLimits Limits = new NavGraphCompileLimits(100, 100, 1000);
        private static NavLocation At(double x, double z) => new NavLocation(new NavPoint(x, z), "ground");
        private static LayeredNavigationProvider Provider()
        {
            var boundary = new NavPolygon(new[] { new NavPoint(-2, -2), new NavPoint(2, -2), new NavPoint(2, 2), new NavPoint(-2, 2) });
            var area = new NavTraversalArea(boundary, 4, Array.Empty<NavObstacle>());
            return new LayeredNavigationProvider("tiny", 1, 1, 1, new string('b', 64),
                new[] { new NavLayerSurface("ground", "ground", 0, 1, area) }, Array.Empty<NavLayerPortal>());
        }
        private static LayeredNavigationProvider ProviderWithManyBlockers()
        {
            var boundary = new NavPolygon(new[] { new NavPoint(-2, -2), new NavPoint(2, -2), new NavPoint(2, 2), new NavPoint(-2, 2) });
            var blockers = Enumerable.Range(0, 200).Select(i =>
                new NavObstacle(-1 + i * .001, -1, -.99 + i * .001, -.99)).ToArray();
            var area = new NavTraversalArea(boundary, 4, blockers);
            return new LayeredNavigationProvider("many-blockers", 1, 1, 1, new string('c', 64),
                new[] { new NavLayerSurface("ground", "ground", 0, 1, area) }, Array.Empty<NavLayerPortal>());
        }
        private static NavScheduledRequest Request(LayeredNavigationProvider provider, int terminal, int member,
            NavLocation? origin = null, NavLocation? endpoint = null, NavLocation? goalOverride = null)
        {
            var goal = goalOverride ?? At(1, 0);
            var region = new NavTerminalRegion("region-" + terminal, 1, goal, new[] { goal });
            var subscription = new NavRouteSubscription(1, terminal + 1, 1, terminal + 1, 0, 1,
                member + 1, 1, 1, 1, 1, origin ?? At(-1, 0), endpoint ?? goal, "clear");
            return new NavScheduledRequest(provider, Clearance, Profile, Limits, region, subscription, 100);
        }
        private static List<NavScheduledCompletion> Drain(LayeredNavigationScheduler scheduler)
        {
            var output = new List<NavScheduledCompletion>();
            while (scheduler.TryTake(_ => true, out var answer)) output.Add(answer);
            return output;
        }
        private static int FinishEpoch(LayeredNavigationScheduler scheduler, int start, NavEpochQuota quota,
            NavScheduledRequest pending, bool restoreEachEpoch, out NavScheduledCompletion result)
        {
            for (int epoch = start; epoch < start + 2000; epoch++) {
                scheduler.Advance(epoch, quota, _ => true);
                if (restoreEachEpoch) scheduler = LayeredNavigationScheduler.Restore(scheduler.Save(),
                    scheduler.PendingCount == 0 ? Array.Empty<NavScheduledRequest>() : new[] { pending });
                if (scheduler.TryTake(_ => true, out result)) return epoch;
            }
            Assert.Fail("Route did not finish under finite logical quotas."); result = null; return -1;
        }
        [Test] public void ActualSchedulerMetadataIndicesPreserveCancelReinsertSlotOrderAndRecipePins()
        {
            var scheduler=new LayeredNavigationScheduler();var provider=Provider();var requests=Enumerable.Range(0,6).Select(n=>Request(provider,n,n)).ToArray();var epochs=new Dictionary<string,long>(StringComparer.Ordinal);
            foreach(var request in requests){Assert.True(scheduler.Submit(request));epochs.Add(request.Subscription.StableKey,0);}
            foreach(int n in new[]{1,4,2}){Assert.True(scheduler.Cancel(requests[n].Subscription.StableKey));epochs.Remove(requests[n].Subscription.StableKey);}
            foreach(int n in new[]{4,1,2}){Assert.True(scheduler.Submit(requests[n]));epochs.Add(requests[n].Subscription.StableKey,0);}
            CollectionAssert.AreEqual(epochs.ToArray(),scheduler.Save().SubmittedEpoch);
            foreach(string name in new[]{"jobsByKey","subscribers","submittedEpoch","cachedFields"})Assert.IsInstanceOf<NavOwnedStorage>(typeof(LayeredNavigationScheduler).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scheduler));
            var restored=LayeredNavigationScheduler.Restore(scheduler.Save(),requests);CollectionAssert.AreEqual(epochs.ToArray(),restored.Save().SubmittedEpoch);scheduler.ClearWorld();restored.ClearWorld();Assert.Zero(scheduler.PendingCount);Assert.Zero(restored.PendingCount);
        }
        [Test] public void SameEpochRetryCannotRefillAnyQuotaAndPendingIsNotUnreachable()
        {
            var scheduler = new LayeredNavigationScheduler(); var request = Request(Provider(), 0, 0);
            Assert.True(scheduler.Submit(request));
            Assert.AreEqual(1, scheduler.Advance(0, new NavEpochQuota(1, 1, 1), _ => true));
            Assert.AreEqual(0, scheduler.Advance(0, new NavEpochQuota(10000, 10000, 10000), _ => true));
            Assert.AreEqual(1, scheduler.Counters.LogicalGraphWork);
            Assert.AreEqual(1, scheduler.PendingCount); Assert.AreEqual(0, scheduler.ReadyCount);
            for (int epoch = 1; epoch < 100; epoch++) {
                scheduler.Advance(epoch, new NavEpochQuota(100, 100, 100), _ => true);
                if (scheduler.ReadyCount > 0) break;
            }
            var result = Drain(scheduler).Single();
            Assert.AreEqual(NavSolveStatus.Ready, result.Status);
            Assert.AreEqual(request.Subscription.StableKey, result.Route.Subscription.StableKey);
            Assert.Greater(scheduler.Counters.PhysicalGraphWork, 0);
            Assert.Greater(scheduler.Counters.PhysicalFieldWork, 0);
            Assert.Greater(scheduler.Counters.ConnectorWork, 0);
        }
        [Test] public void ChunkedCallsWithinEpochKeepStablePublicationOrderAndWork()
        {
            var provider = Provider(); var requests = new[] { Request(provider, 1, 1), Request(provider, 2, 2), Request(provider, 1, 3) };
            List<string> Trace(bool chunked, out NavSchedulerCounters counters)
            {
                var scheduler = new LayeredNavigationScheduler();
                foreach (var request in requests) scheduler.Submit(request);
                var trace = new List<string>();
                for (int epoch = 0; epoch < 200 && trace.Count < requests.Length; epoch++) {
                    var quota = new NavEpochQuota(31, 37, 43);
                    if (!chunked) scheduler.Advance(epoch, quota, _ => true);
                    else for (int i = 0; i < 10000; i++)
                        if (scheduler.Advance(epoch, quota, _ => true, 1) == 0) break;
                    foreach (var result in Drain(scheduler))
                        trace.Add(epoch + ":" + result.Subscription.StableKey + ":" + result.Status);
                }
                counters = scheduler.Counters; return trace;
            }
            var whole = Trace(false, out var wholeCounters);
            var pieces = Trace(true, out var pieceCounters);
            Assert.AreEqual(3, whole.Count); CollectionAssert.AreEqual(whole, pieces);
            Assert.AreEqual(wholeCounters.LogicalGraphWork, pieceCounters.LogicalGraphWork);
            Assert.AreEqual(wholeCounters.PhysicalFieldWork, pieceCounters.PhysicalFieldWork);
            Assert.AreEqual(wholeCounters.ConnectorWork, pieceCounters.ConnectorWork);
        }
        [Test] public void SchedulerCheckpointAfterEveryWorkUnitKeepsEpochAndResult()
        {
            var provider = Provider(); var request = Request(provider, 0, 0);
            int Trace(bool restoreEachStep, out NavScheduledCompletion answer)
            {
                var scheduler = new LayeredNavigationScheduler(); scheduler.Submit(request);
                for (int epoch = 0; epoch < 2000; epoch++) {
                    var quota = new NavEpochQuota(7, 5, 3);
                    for (int step = 0; step < 1000; step++) {
                        int used = scheduler.Advance(epoch, quota, _ => true, 1);
                        Assert.LessOrEqual(used, 1);
                        if (restoreEachStep) scheduler = LayeredNavigationScheduler.Restore(scheduler.Save(),
                            scheduler.PendingCount == 0 ? Array.Empty<NavScheduledRequest>() : new[] { request });
                        if (scheduler.TryTake(_ => true, out answer)) return epoch;
                        if (used == 0) break;
                    }
                }
                Assert.Fail("Scheduler checkpoint did not progress."); answer = null; return -1;
            }
            int cold = Trace(false, out var expected);
            int restored = Trace(true, out var actual);
            Assert.AreEqual(cold, restored); Assert.AreEqual(expected.Status, actual.Status);
            Assert.AreEqual(expected.Route.Cost, actual.Route.Cost, 1e-12);
        }
        [Test] public void SubsetCancellationAndCheckpointKeepUnaffectedSubscribers()
        {
            var provider = Provider(); var requests = Enumerable.Range(0, 5).Select(i => Request(provider, 0, i)).ToArray();
            var scheduler = new LayeredNavigationScheduler(); foreach (var request in requests) Assert.True(scheduler.Submit(request));
            scheduler.Advance(0, new NavEpochQuota(3, 3, 3), _ => true);
            Assert.True(scheduler.Cancel(requests[1].Subscription.StableKey));
            Assert.True(scheduler.Cancel(requests[3].Subscription.StableKey));
            scheduler = LayeredNavigationScheduler.Restore(scheduler.Save(), new[] { requests[0], requests[2], requests[4] });
            var results = new List<NavScheduledCompletion>();
            for (int epoch = 1; epoch < 100 && results.Count < 3; epoch++) {
                scheduler.Advance(epoch, new NavEpochQuota(1000, 1000, 1000), _ => true);
                results.AddRange(Drain(scheduler));
            }
            Assert.AreEqual(3, results.Count);
            CollectionAssert.AreEqual(new[] { requests[0], requests[2], requests[4] }.Select(r => r.Subscription.StableKey),
                results.Select(r => r.Subscription.StableKey));
            Assert.True(results.All(r => r.Status == NavSolveStatus.Ready));
            Assert.AreEqual(1, scheduler.Counters.FieldBuilds);
        }
        [Test] public void EightGroupsOfFiftyBuildEightRealTailsAndBoundRetention()
        {
            var provider = Provider(); var scheduler = new LayeredNavigationScheduler();
            for (int group = 0; group < 8; group++) for (int member = 0; member < 50; member++)
                Assert.True(scheduler.Submit(Request(provider, group, group * 50 + member,
                    goalOverride: At(-1 + group * .25, 1))));
            var results = new List<NavScheduledCompletion>();
            for (int epoch = 0; epoch < 500 && results.Count < 400; epoch++) {
                scheduler.Advance(epoch, new NavEpochQuota(1000, 1000, 1000), _ => true);
                results.AddRange(Drain(scheduler));
            }
            Assert.AreEqual(400, results.Count);
            Assert.True(results.All(r => r.Status == NavSolveStatus.Ready && r.Route.Legs.Count > 0));
            Assert.AreEqual(8, scheduler.Counters.FieldBuilds);
            Assert.AreEqual(1, scheduler.Counters.GraphBuilds);
            Assert.AreEqual(8, scheduler.Counters.CacheFields);
            Assert.LessOrEqual(scheduler.Counters.PeakQueue, LayeredNavigationScheduler.MaxQueue);
            Assert.LessOrEqual(scheduler.Counters.PeakCacheBytes, LayeredNavigationScheduler.MaxCacheBytes);
            Assert.LessOrEqual(scheduler.Counters.PeakRetainedBytes, LayeredNavigationScheduler.MaxRetainedBytes);
            TestContext.WriteLine($"8x50 graphBuilds={scheduler.Counters.GraphBuilds} fieldBuilds={scheduler.Counters.FieldBuilds} " +
                $"logicalGraph={scheduler.Counters.LogicalGraphWork} physicalGraph={scheduler.Counters.PhysicalGraphWork} " +
                $"logicalField={scheduler.Counters.LogicalFieldWork} physicalField={scheduler.Counters.PhysicalFieldWork} " +
                $"connectors={scheduler.Counters.ConnectorWork} peakQueue={scheduler.Counters.PeakQueue} " +
                $"peakFrontier={scheduler.Counters.PeakFrontier} peakCacheBytes={scheduler.Counters.PeakCacheBytes} " +
                $"peakRetainedBytes={scheduler.Counters.PeakRetainedBytes} peakQueueAge={scheduler.Counters.PeakQueueAge}");
        }
        [Test] public void FourHundredDistinctGoalsEvictWithoutLosingFiniteOutcomes()
        {
            var provider = Provider(); var scheduler = new LayeredNavigationScheduler();
            for (int i = 0; i < 400; i++) Assert.True(scheduler.Submit(Request(provider, i, i,
                goalOverride: At(-1.5 + (i % 20) * .15, -1.5 + (i / 20) * .15))));
            var results = new List<NavScheduledCompletion>();
            for (int epoch = 0; epoch < 2000 && results.Count < 400; epoch++) {
                scheduler.Advance(epoch, new NavEpochQuota(10000, 10000, 10000), _ => true);
                results.AddRange(Drain(scheduler));
            }
            Assert.AreEqual(400, results.Count);
            Assert.True(results.All(r => r.Status == NavSolveStatus.Ready));
            Assert.AreEqual(400, scheduler.Counters.FieldBuilds);
            Assert.AreEqual(256, scheduler.Counters.CacheFields);
            Assert.GreaterOrEqual(scheduler.Counters.Evictions, 144);
            Assert.LessOrEqual(scheduler.Counters.PeakCacheBytes, LayeredNavigationScheduler.MaxCacheBytes);
            Assert.LessOrEqual(scheduler.Counters.PeakQueue, LayeredNavigationScheduler.MaxQueue);
            Assert.LessOrEqual(scheduler.Counters.PeakRetainedBytes, LayeredNavigationScheduler.MaxRetainedBytes);
            TestContext.WriteLine($"400unique graphBuilds={scheduler.Counters.GraphBuilds} fieldBuilds={scheduler.Counters.FieldBuilds} " +
                $"logicalGraph={scheduler.Counters.LogicalGraphWork} physicalGraph={scheduler.Counters.PhysicalGraphWork} " +
                $"logicalField={scheduler.Counters.LogicalFieldWork} physicalField={scheduler.Counters.PhysicalFieldWork} " +
                $"connectors={scheduler.Counters.ConnectorWork} evictions={scheduler.Counters.Evictions} " +
                $"cacheFields={scheduler.Counters.CacheFields} peakQueue={scheduler.Counters.PeakQueue} " +
                $"peakFrontier={scheduler.Counters.PeakFrontier} peakCacheBytes={scheduler.Counters.PeakCacheBytes} " +
                $"peakRetainedBytes={scheduler.Counters.PeakRetainedBytes} peakQueueAge={scheduler.Counters.PeakQueueAge}");
            scheduler.ClearWorld(); Assert.AreEqual(0, scheduler.RetainedBytes);
        }
        private static LayeredNavigationProvider FieldCacheProvider()
        {
            var boundary=new NavPolygon(new[]{new NavPoint(-2,-2),new NavPoint(2,-2),new NavPoint(2,2),new NavPoint(-2,2)});
            var area=new NavTraversalArea(boundary,4,new[]{new NavObstacle(-.15,-.7,.15,.7)});
            return new LayeredNavigationProvider("non-direct-cache",1,1,1,new string('d',64),new[]{new NavLayerSurface("ground","ground",0,1,area)},Array.Empty<NavLayerPortal>());
        }
        [Test] public void FourHundredNonDirectGoalsBuildBoundedCurrentFieldsAndEvict()
        {
            var provider=FieldCacheProvider();var scheduler=new LayeredNavigationScheduler();var results=new List<NavScheduledCompletion>();
            try{
                for(int i=0;i<400;i++){
                    var request=Request(provider,i,i,goalOverride:At(.6+(i%20)*.04,-.38+(i/20)*.04));
                    Assert.True(provider.IsValid(request.Subscription.Origin,Clearance));Assert.True(provider.IsValid(request.Subscription.AssignedEndpoint,Clearance));
                    Assert.False(provider.CanSweep(request.Subscription.Origin,request.Subscription.AssignedEndpoint,Clearance),"every fixed goal must require a real field, not a direct shortcut");Assert.True(scheduler.Submit(request));
                }
                for(int epoch=0;epoch<2000&&results.Count<400;epoch++){scheduler.Advance(epoch,new NavEpochQuota(10000,10000,10000),_=>true);results.AddRange(Drain(scheduler));}
                Assert.AreEqual(400,results.Count);Assert.True(results.All(r=>r.Status==NavSolveStatus.Ready&&r.Route.Legs.Count>0));
                Assert.AreEqual(400,scheduler.Counters.FieldBuilds);Assert.AreEqual(256,scheduler.Counters.CacheFields);Assert.GreaterOrEqual(scheduler.Counters.Evictions,144);
                Assert.LessOrEqual(scheduler.Counters.PeakCacheBytes,LayeredNavigationScheduler.MaxCacheBytes);Assert.LessOrEqual(scheduler.Counters.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);Assert.LessOrEqual(scheduler.Counters.PeakQueue,LayeredNavigationScheduler.MaxQueue);
                TestContext.WriteLine("A23_NON_DIRECT_400 "+Newtonsoft.Json.JsonConvert.SerializeObject(scheduler.Counters));
            }finally{foreach(var result in results)result.ReleaseOwner();scheduler.ClearWorld();Assert.Zero(scheduler.RetainedBytes);}
        }
        [Test] public void NonDirectSubsetCancellationAndCurrentCheckpointKeepOneSharedField()
        {
            var provider=FieldCacheProvider();var requests=Enumerable.Range(0,5).Select(i=>Request(provider,0,i)).ToArray();var scheduler=new LayeredNavigationScheduler();var results=new List<NavScheduledCompletion>();
            try{
                foreach(var request in requests){Assert.False(provider.CanSweep(request.Subscription.Origin,request.Subscription.AssignedEndpoint,Clearance));Assert.True(scheduler.Submit(request));}
                scheduler.Advance(0,new NavEpochQuota(3,3,3),_=>true);Assert.True(scheduler.Cancel(requests[1].Subscription.StableKey));Assert.True(scheduler.Cancel(requests[3].Subscription.StableKey));
                var restored=LayeredNavigationScheduler.Restore(scheduler.Save(),new[]{requests[0],requests[2],requests[4]});scheduler.ClearWorld();scheduler=restored;
                for(int epoch=1;epoch<100&&results.Count<3;epoch++){scheduler.Advance(epoch,new NavEpochQuota(1000,1000,1000),_=>true);results.AddRange(Drain(scheduler));}
                Assert.AreEqual(3,results.Count);Assert.True(results.All(r=>r.Status==NavSolveStatus.Ready));CollectionAssert.AreEqual(new[]{requests[0],requests[2],requests[4]}.Select(r=>r.Subscription.StableKey),results.Select(r=>r.Subscription.StableKey));Assert.AreEqual(1,scheduler.Counters.FieldBuilds);Assert.AreEqual(2,scheduler.Counters.Cancelled);
            }finally{foreach(var result in results)result.ReleaseOwner();scheduler.ClearWorld();Assert.Zero(scheduler.RetainedBytes);}
        }
        [Test] public void ZeroQuotasCannotDrainStaleQueueAndAdmissionCapsRetainedBytes()
        {
            var provider = Provider(); var scheduler = new LayeredNavigationScheduler();
            var goals = Enumerable.Range(0, 64).Select(i => At(-1 + i / 64.0, 0)).ToArray();
            int admitted = 0;
            for (int i = 0; i < 5000; i++) {
                var request = Request(provider, i, i);
                var broad = new NavTerminalRegion("wide-" + i, 1, goals[0], goals);
                request = new NavScheduledRequest(provider, Clearance, Profile, Limits, broad, request.Subscription, 100);
                if (!scheduler.Submit(request)) break;
                admitted++;
                Assert.LessOrEqual(scheduler.RetainedBytes, LayeredNavigationScheduler.MaxRetainedBytes);
            }
            Assert.Greater(admitted, 400); Assert.LessOrEqual(admitted, LayeredNavigationScheduler.MaxQueue);
            var before = scheduler.Counters;
            Assert.AreEqual(0, scheduler.Advance(0, new NavEpochQuota(0, 0, 0), _ => false));
            Assert.AreEqual(before.PendingSubscriptions, scheduler.PendingCount);
            Assert.AreEqual(before.Cancelled, scheduler.Counters.Cancelled);
            Assert.AreEqual(1, scheduler.Advance(1, new NavEpochQuota(0, 0, 0, 1), _ => false));
            Assert.AreEqual(before.PendingSubscriptions - 1, scheduler.PendingCount);
            Assert.AreEqual(0, scheduler.Advance(1, new NavEpochQuota(0, 0, 0, 100), _ => false));
            scheduler.ClearWorld(); Assert.AreEqual(0, scheduler.RetainedBytes);
        }
        [Test] public void EqualBindingDistinctProvidersCountEachRetainedAllocation()
        {
            var first = ProviderWithManyBlockers(); var second = ProviderWithManyBlockers();
            Assert.AreEqual(first.Binding, second.Binding); Assert.False(ReferenceEquals(first, second));
            var shared = new LayeredNavigationScheduler();
            shared.Submit(Request(first, 0, 0)); shared.Submit(Request(first, 1, 1));
            var distinct = new LayeredNavigationScheduler();
            var a = Request(first, 0, 0); var b = Request(second, 1, 1);
            Assert.AreEqual(a.GraphKey, b.GraphKey);
            Assert.True(distinct.Submit(a)); Assert.True(distinct.Submit(b));
            Assert.Greater(distinct.RetainedBytes - shared.RetainedBytes, 100000);
            int admitted = 2;
            for (int i = 2; i < 400; i++) {
                if (!distinct.Submit(Request(ProviderWithManyBlockers(), i, i))) break;
                admitted++;
                Assert.LessOrEqual(distinct.RetainedBytes, LayeredNavigationScheduler.MaxRetainedBytes);
            }
            Assert.Less(admitted, 400); Assert.Greater(admitted, 100);
            Assert.LessOrEqual(distinct.Counters.PeakRetainedBytes, LayeredNavigationScheduler.MaxRetainedBytes);
            TestContext.WriteLine($"equalBinding distinctProviders admitted={admitted} " +
                $"sharedTwoBytes={shared.RetainedBytes} distinctPeakBytes={distinct.Counters.PeakRetainedBytes}");
            distinct.ClearWorld(); Assert.AreEqual(0, distinct.RetainedBytes);
        }
        [Test] public void PublicationChecksOneReadyIdentityPerCall()
        {
            var provider = Provider(); var a = Request(provider, 0, 0); var b = Request(provider, 0, 1);
            var scheduler = new LayeredNavigationScheduler(); scheduler.Submit(a); scheduler.Submit(b);
            for (int epoch = 0; epoch < 100 && scheduler.ReadyCount < 2; epoch++)
                scheduler.Advance(epoch, new NavEpochQuota(1000, 1000, 1000), _ => true);
            Assert.AreEqual(2, scheduler.ReadyCount);
            Assert.False(scheduler.TryTake(s => s.StableKey == b.Subscription.StableKey, out _));
            Assert.AreEqual(1, scheduler.ReadyCount);
            Assert.True(scheduler.TryTake(s => s.StableKey == b.Subscription.StableKey, out var answer));
            Assert.AreEqual(b.Subscription.StableKey, answer.Subscription.StableKey);
            Assert.GreaterOrEqual(answer.QueueAge, 0);
        }
        [Test] public void ColdWarmEvictedAndRestoredApplicationEpochsAgree()
        {
            var provider = Provider(); var quota = new NavEpochQuota(17, 13, 7);
            var coldRequest = Request(provider, 0, 0);
            var cold = new LayeredNavigationScheduler(); cold.Submit(coldRequest);
            int coldEpoch = FinishEpoch(cold, 100, quota, coldRequest, false, out var coldAnswer) - 100;
            Assert.AreEqual(NavSolveStatus.Ready, coldAnswer.Status);

            var warm = new LayeredNavigationScheduler(); var prime = Request(provider, 0, 1);
            warm.Submit(prime); FinishEpoch(warm, 0, quota, prime, false, out _);
            var warmRequest = Request(provider, 0, 2); warm.Submit(warmRequest);
            int warmEpoch = FinishEpoch(warm, 100, quota, warmRequest, false, out var warmAnswer) - 100;
            Assert.LessOrEqual(warmEpoch, coldEpoch);
            Assert.AreEqual(coldAnswer.Status, warmAnswer.Status);
            Assert.Greater(warm.Counters.CacheHits, 0);

            var evicted = new LayeredNavigationScheduler(); evicted.Submit(Request(provider, 0, 3));
            var first = Request(provider, 0, 3); FinishEpoch(evicted, 0, new NavEpochQuota(10000, 10000, 10000), first, false, out _);
            for (int i = 1; i <= 257; i++) {
                var filler = Request(provider, i, i + 10); evicted.Submit(filler);
                FinishEpoch(evicted, i, new NavEpochQuota(10000, 10000, 10000), filler, false, out _);
            }
            Assert.Greater(evicted.Counters.Evictions, 0);
            var again = Request(provider, 0, 500); evicted.Submit(again);
            int evictedEpoch = FinishEpoch(evicted, 1000, quota, again, false, out var evictedAnswer) - 1000;
            Assert.AreEqual(coldEpoch, evictedEpoch);
            Assert.AreEqual(coldAnswer.Status, evictedAnswer.Status);

            var restored = new LayeredNavigationScheduler(); var restoredRequest = Request(provider, 0, 600);
            restored.Submit(restoredRequest);
            int restoredEpoch = FinishEpoch(restored, 100, quota, restoredRequest, true, out var restoredAnswer) - 100;
            Assert.AreEqual(coldEpoch, restoredEpoch);
            Assert.AreEqual(coldAnswer.Status, restoredAnswer.Status);
        }
        [Test] public void FullLruMaintenanceCannotShiftLogicalCompletionWithOneBookkeepingUnit()
        {
            var provider = Provider();
            LayeredNavigationScheduler Prime()
            {
                var scheduler = new LayeredNavigationScheduler();
                for (int i = 0; i < 256; i++) Assert.True(scheduler.Submit(Request(provider, i, i)));
                scheduler.Advance(0, new NavEpochQuota(1000000, 1000000, 1000000), _ => true);
                Assert.AreEqual(256, Drain(scheduler).Count);
                Assert.AreEqual(256, scheduler.Counters.CacheFields);
                return scheduler;
            }
            var quota = new NavEpochQuota(1, 1, 1, bookkeeping: 1, maintenance: 1);
            var warm = Prime(); var warmRequest = Request(provider, 255, 999); warm.Submit(warmRequest);
            long warmGraph = warm.Counters.LogicalGraphWork, warmField = warm.Counters.LogicalFieldWork;
            int warmEpoch = FinishEpoch(warm, 100, quota, warmRequest, false, out var warmAnswer) - 100;
            var cold = Prime(); var coldRequest = Request(provider, 256, 999); cold.Submit(coldRequest);
            long coldGraph = cold.Counters.LogicalGraphWork, coldField = cold.Counters.LogicalFieldWork;
            int coldEpoch = FinishEpoch(cold, 100, quota, coldRequest, false, out var coldAnswer) - 100;
            Assert.AreEqual(warmEpoch, coldEpoch);
            Assert.AreEqual(warmAnswer.Status, coldAnswer.Status);
            Assert.AreEqual(warmAnswer.Route.Cost, coldAnswer.Route.Cost, 1e-12);
            Assert.AreEqual(warm.Counters.LogicalGraphWork - warmGraph, cold.Counters.LogicalGraphWork - coldGraph);
            Assert.AreEqual(warm.Counters.LogicalFieldWork - warmField, cold.Counters.LogicalFieldWork - coldField);
            Assert.Greater(cold.Counters.MaintenanceWork, warm.Counters.MaintenanceWork);
            Assert.AreEqual(256, cold.Counters.CacheFields);
            TestContext.WriteLine($"fullLRU warmEpoch={warmEpoch} coldEpoch={coldEpoch} " +
                $"warmMaintenance={warm.Counters.MaintenanceWork} coldMaintenance={cold.Counters.MaintenanceWork} " +
                $"coldEvictions={cold.Counters.Evictions}");

            var restored = Prime(); var restoredRequest = Request(provider, 256, 1000);
            restored.Submit(restoredRequest);
            restored = LayeredNavigationScheduler.Restore(restored.Save(), new[] { restoredRequest });
            int restoredEpoch = FinishEpoch(restored, 100, quota, restoredRequest, false, out var restoredAnswer) - 100;
            Assert.AreEqual(warmEpoch, restoredEpoch);
            Assert.AreEqual(warmAnswer.Status, restoredAnswer.Status);
        }
        [Test] public void ScheduledRoutesRespectRealTypedFloorsPortalDirectionAndRadius()
        {
            NavScheduledCompletion Solve(StackedNavigationFixture fixture, NavClearanceProfile radius,
                NavLocation origin, NavLocation endpoint)
            {
                var region = new NavTerminalRegion("stacked-goal", 1, endpoint, new[] { endpoint });
                var subscription = new NavRouteSubscription(1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1,
                    origin, endpoint, "clear");
                var request = new NavScheduledRequest(fixture.Provider, radius, Profile,
                    new NavGraphCompileLimits(2000, 2000, 10000), region, subscription, 4000);
                var scheduler = new LayeredNavigationScheduler(); scheduler.Submit(request);
                FinishEpoch(scheduler, 0, new NavEpochQuota(20000, 20000, 20000), request, false, out var result);
                return result;
            }
            var lower = new NavLocation(new NavPoint(0, 0), "lower");
            var upper = new NavLocation(new NavPoint(0, 0), "upper");
            var small = new NavClearanceProfile("small", 1, "ground", 1, .5);
            var large = new NavClearanceProfile("large", 1, "ground", 1, 1);
            var success = Solve(new StackedNavigationFixture(direction: NavPortalDirection.Forward), small, lower, upper);
            Assert.AreEqual(NavSolveStatus.Ready, success.Status);
            Assert.True(success.Route.Legs.Any(leg => leg.Kind == NavTypedLegKind.Portal && leg.PortalId == "ramp"));
            Assert.AreEqual("lower", success.Route.Legs.First().From.SurfaceId);
            Assert.AreEqual("upper", success.Route.Legs.Last().To.SurfaceId);
            Assert.AreEqual(NavSolveStatus.UnreachableInGraph,
                Solve(new StackedNavigationFixture(direction: NavPortalDirection.Forward), small, upper, lower).Status);
            Assert.AreEqual(NavSolveStatus.UnreachableInGraph,
                Solve(new StackedNavigationFixture(open: false), small, lower, upper).Status);
            Assert.AreEqual(NavSolveStatus.UnreachableInGraph,
                Solve(new StackedNavigationFixture(), large, lower, upper).Status);
        }
        [Test] public void TopologyProfileAndHoldChangesRejectStalePublication()
        {
            var original = Provider(); var old = Request(original, 0, 0);
            var changed = new LayeredNavigationProvider(original.Id, original.Revision,
                original.SemanticsVersion, original.TopologyRevision + 1, original.ExactMapBinding,
                original.Surfaces, original.Portals);
            var newProfile = new NavGraphProfile(new string('a', 64), "distance", 2, 1);
            var newSubscription = new NavRouteSubscription(2, 1, 2, 2, 1, 2, 1, 2, 2, 2, 2,
                old.Subscription.Origin, old.Subscription.AssignedEndpoint, "hold-revision-2");
            var replacement = new NavScheduledRequest(changed, Clearance, newProfile, Limits,
                old.Region, newSubscription, 100);
            Assert.AreNotEqual(old.ArtifactKey, replacement.ArtifactKey);
            Assert.AreNotEqual(old.Subscription.StableKey, replacement.Subscription.StableKey);
            var scheduler = new LayeredNavigationScheduler(); scheduler.Submit(old);
            scheduler.Advance(0, new NavEpochQuota(3, 3, 3), _ => true);
            scheduler.Submit(replacement);
            var results = new List<NavScheduledCompletion>();
            for (int epoch = 1; epoch < 100 && results.Count == 0; epoch++) {
                scheduler.Advance(epoch, new NavEpochQuota(1000, 1000, 1000), s =>
                    s.StableKey == replacement.Subscription.StableKey && s.GraphKey == replacement.GraphKey);
                while (scheduler.TryTake(s => s.StableKey == replacement.Subscription.StableKey &&
                    s.GraphKey == replacement.GraphKey, out var answer)) results.Add(answer);
            }
            Assert.AreEqual(1, results.Count); Assert.AreEqual(NavSolveStatus.Ready, results[0].Status);
            Assert.AreEqual(replacement.Subscription.StableKey, results[0].Route.Subscription.StableKey);
            Assert.AreEqual(replacement.GraphKey, results[0].Identity.GraphKey);
            Assert.GreaterOrEqual(scheduler.Counters.Cancelled, 1);
        }
        [Test] public void SingletonUsesSameEpochLedgerAndResumesExactTypedRoute()
        {
            var fixture = new StackedNavigationFixture(direction: NavPortalDirection.Forward);
            var origin = new NavLocation(new NavPoint(0, 0), "lower");
            var endpoint = new NavLocation(new NavPoint(0, 0), "upper");
            var subscription = new NavRouteSubscription(1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1,
                origin, endpoint, "none");
            var request = NavScheduledRequest.ForSingleton(fixture.Provider,
                new NavClearanceProfile("small", 1, "ground", 1, .5), Profile,
                new NavGraphCompileLimits(2000, 2000, 10000), subscription, 2000);
            var scheduler = new LayeredNavigationScheduler(); Assert.True(scheduler.Submit(request));
            FinishEpoch(scheduler, 0, new NavEpochQuota(100, 100, 100), request, true, out var result);
            Assert.AreEqual(NavSolveStatus.Ready, result.Status);
            Assert.AreEqual(subscription.StableKey, result.Route.Subscription.StableKey);
            Assert.AreEqual(1, result.Route.Legs.Count(leg => leg.Kind == NavTypedLegKind.Portal));
            Assert.AreEqual(origin, result.Route.Legs.First().From);
            Assert.AreEqual(endpoint, result.Route.Legs.Last().To);
        }
        [Test] public void ClearSingletonUsesOneQualifiedDirectProbeWithoutCompilingGraph()
        {
            var provider = Provider(); var origin = At(-1.125, .125); var endpoint = At(1.125, .125);
            var subscription = new NavRouteSubscription(1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1,
                origin, endpoint, "none");
            var request = NavScheduledRequest.ForSingleton(provider, Clearance, Profile, Limits, subscription, 100);
            var scheduler = new LayeredNavigationScheduler(); Assert.True(scheduler.Submit(request));
            scheduler = LayeredNavigationScheduler.Restore(scheduler.Save(), new[] { request });
            Assert.AreEqual(1, scheduler.Advance(0, new NavEpochQuota(0, 0, 1), _ => true, 1));
            Assert.AreEqual(0, scheduler.Advance(0, new NavEpochQuota(100, 100, 100), _ => true));
            Assert.True(scheduler.TryTake(_ => true, out var answer));
            Assert.AreEqual(NavSolveStatus.Ready, answer.Status);
            Assert.AreEqual(1, answer.Route.Legs.Count);
            Assert.AreEqual(NavTypedLegKind.Surface, answer.Route.Legs[0].Kind);
            Assert.AreEqual(origin, answer.Route.Legs[0].From); Assert.AreEqual(endpoint, answer.Route.Legs[0].To);
            Assert.AreEqual(0, scheduler.Counters.GraphBuilds);
            Assert.AreEqual(0, scheduler.Counters.FieldBuilds);
            Assert.AreEqual(0, scheduler.Counters.PhysicalGraphWork);
            Assert.AreEqual(1, scheduler.Counters.ConnectorWork);
            var restored = LayeredNavigationScheduler.Restore(scheduler.Save(), Array.Empty<NavScheduledRequest>());
            Assert.AreEqual(0, restored.PendingCount);
        }
        private static LayeredNavigationScheduler PrimitiveRoundtrip(LayeredNavigationScheduler scheduler, NavScheduledRequest[] pending)
        {
            var recipes=new List<NavScheduledRequest>();
            int Id(NavScheduledRequest value){int id=recipes.IndexOf(value);if(id<0){id=recipes.Count;recipes.Add(value);}return id;}
            byte[] bytes;
            using(var stream=new MemoryStream()){
                using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){
                    var codec=new NavigationPrimitiveWire.Writer(writer,Id,_=>throw new InvalidOperationException("Unexpected route in this partial checkpoint."));
                    codec.Scheduler(scheduler.Save());
                }
                bytes=stream.ToArray();
            }
            LayeredNavigationScheduler.Checkpoint checkpoint;
            using(var stream=new MemoryStream(bytes))using(var reader=new BinaryReader(stream)){
                var codec=new NavigationPrimitiveWire.Reader(reader,id=>id>=0&&id<recipes.Count?recipes[id]:throw new ArgumentException("Unknown recipe."),_=>throw new ArgumentException("Unknown route."),bytes.Length);
                checkpoint=codec.Scheduler();Assert.AreEqual(stream.Length,stream.Position);
            }
            return LayeredNavigationScheduler.Restore(checkpoint,pending);
        }
        private static NavOwnedStorage FirstJob(LayeredNavigationScheduler scheduler)
        {
            var jobs=(System.Collections.IEnumerable)typeof(LayeredNavigationScheduler).GetField("jobs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scheduler);
            return jobs.Cast<NavOwnedStorage>().First();
        }
        private static T JobPart<T>(NavOwnedStorage job,string name)=>(T)job.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(job);
        private static byte[] CompileBytes(LayeredNavigationCompileWork work)
        {using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>throw new AssertionException("Unexpected request."),_=>throw new AssertionException("Unexpected route.")).Compile(work.Save());return stream.ToArray();}}
        [Test] public void ActualSchedulerPinsImmutableJobAcrossPreparationAndLastSubscriberCancellation()
        {
            var scheduler=new LayeredNavigationScheduler();var provider=Provider();var first=Request(provider,0,0);var second=Request(provider,0,1);
            Assert.True(scheduler.Submit(first));Assert.True(scheduler.Submit(second));var quota=new NavEpochQuota(100,100,100);
            Assert.AreEqual(3,scheduler.Advance(0,quota,_=>true,3));var pin=FirstJob(scheduler);pin.Retain();var compiler=JobPart<LayeredNavigationCompileWork>(pin,"Compiler");Assert.NotNull(compiler);var before=CompileBytes(compiler);
            Assert.AreEqual(1,scheduler.Advance(0,quota,_=>true,1));Assert.AreNotSame(pin,FirstJob(scheduler));Assert.AreNotSame(compiler,JobPart<LayeredNavigationCompileWork>(FirstJob(scheduler),"Compiler"));CollectionAssert.AreEqual(before,CompileBytes(compiler));
            Assert.True(scheduler.Cancel(first.Subscription.StableKey));Assert.True(scheduler.Cancel(second.Subscription.StableKey));Assert.Zero(scheduler.PendingCount);
            Assert.Greater(compiler.References,0);CollectionAssert.AreEqual(before,CompileBytes(compiler));pin.Release();Assert.Zero(compiler.References);scheduler.ClearWorld();
        }
        [Test] public void ActualWarmCacheOwnsFieldAndGraphAfterJobDropAndReleasesOnNormalEviction()
        {
            var scheduler=new LayeredNavigationScheduler();var provider=Provider();var request=Request(provider,0,0);
            Assert.True(scheduler.Submit(request));FinishEpoch(scheduler,0,new NavEpochQuota(10000,10000,10000),request,false,out var answer);answer.Release();
            var graph=(LayeredNavigationGraph)typeof(LayeredNavigationScheduler).GetProperty("cachedGraph",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scheduler);
            var lru=(System.Collections.IEnumerable)typeof(LayeredNavigationScheduler).GetField("lru",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scheduler);var entry=lru.Cast<NavOwnedStorage>().Single();
            var field=(NavSharedTerminalField)entry.GetType().GetField("Field",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(entry);
            Assert.Zero(scheduler.PendingCount);Assert.Greater(graph.References,0);Assert.AreEqual(1,field.References);entry.Retain();
            var evict=typeof(LayeredNavigationScheduler).GetMethod("EvictOne",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.True((bool)evict.Invoke(scheduler,null));Assert.Zero(scheduler.Counters.CacheFields);Assert.Greater(field.References,0);
            entry.Release();Assert.Zero(field.References);Assert.True((bool)evict.Invoke(scheduler,null));Assert.Zero(graph.References);scheduler.ClearWorld();
        }
        [Test] public void PrimitiveWarmCacheWithoutSubscribersPreservesLruAndNextHitCharges()
        {
            var provider=Provider();var scheduler=new LayeredNavigationScheduler();var quota=new NavEpochQuota(10000,10000,10000);
            for(int n=0;n<3;n++){
                var request=Request(provider,n,n);Assert.True(scheduler.Submit(request));
                FinishEpoch(scheduler,n*10,quota,request,false,out var answer);Assert.AreEqual(NavSolveStatus.Ready,answer.Status);
            }
            Assert.Zero(scheduler.PendingCount);Assert.Zero(scheduler.ReadyCount);Assert.AreEqual(3,scheduler.Counters.CacheFields);
            var restored=PrimitiveRoundtrip(scheduler,Array.Empty<NavScheduledRequest>());
            Assert.AreEqual(scheduler.Counters.CacheFields,restored.Counters.CacheFields);
            Assert.AreEqual(scheduler.Counters.CacheBytes,restored.Counters.CacheBytes);
            Assert.AreEqual(scheduler.RetainedBytes,restored.RetainedBytes);
            var next=Request(provider,1,100);Assert.True(scheduler.Submit(next));Assert.True(restored.Submit(next));
            var limited=new NavEpochQuota(1,1,1,1,1);
            for(int epoch=100;epoch<110;epoch++){
                Assert.AreEqual(scheduler.Advance(epoch,limited,_=>true,1),restored.Advance(epoch,limited,_=>true,1));
                Assert.AreEqual(scheduler.Counters.CacheHits,restored.Counters.CacheHits);
                Assert.AreEqual(scheduler.Counters.LogicalGraphWork,restored.Counters.LogicalGraphWork);
                Assert.AreEqual(scheduler.Counters.LogicalFieldWork,restored.Counters.LogicalFieldWork);
                Assert.AreEqual(scheduler.RetainedBytes,restored.RetainedBytes);
                if(restored.ReadyCount==0)restored=PrimitiveRoundtrip(restored,new[]{next});
            }
        }
        [Test] public void PrimitiveWarmLruWithActiveAliasedJobPreservesNextEvictionAndCharge()
        {
            var provider=Provider();var hot=new LayeredNavigationScheduler();var quota=new NavEpochQuota(10000,10000,10000);
            for(int n=0;n<3;n++){var request=Request(provider,n,n);Assert.True(hot.Submit(request));FinishEpoch(hot,n*10,quota,request,false,out var answer);answer.Release();}
            Assert.Zero(hot.PendingCount);Assert.Zero(hot.ReadyCount);Assert.AreEqual(3,hot.Counters.CacheFields);
            hot=PrimitiveRoundtrip(hot,Array.Empty<NavScheduledRequest>());
            var requests=new[]{Request(provider,1,100),Request(provider,1,101)};foreach(var r in requests)Assert.True(hot.Submit(r));
            for(int work=0;JobPart<NavSharedTerminalField>(FirstJob(hot),"Field")==null;work++){Assert.Less(work,2000);Assert.AreEqual(1,hot.Advance(100,quota,_=>true,1));}
            Assert.AreEqual(2,hot.PendingCount);Assert.Zero(hot.ReadyCount);
            var cold=PrimitiveRoundtrip(hot,requests);
            LayeredNavigationScheduler.CacheField[] Entries(LayeredNavigationScheduler value)=>((System.Collections.IEnumerable)typeof(LayeredNavigationScheduler).GetField("lru",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value)).Cast<LayeredNavigationScheduler.CacheField>().ToArray();
            foreach(var value in new[]{hot,cold})Assert.AreSame(Entries(value).Single(e=>e.Key==requests[0].ArtifactKey).Field,JobPart<NavSharedTerminalField>(FirstJob(value),"Field"),"Active aliased job uses the restored cache field");
            var before=Entries(hot).Select(e=>e.Key).ToArray();CollectionAssert.AreEqual(before,Entries(cold).Select(e=>e.Key));Assert.AreEqual(hot.LogicalContinuationBinding,cold.LogicalContinuationBinding);
            var evict=typeof(LayeredNavigationScheduler).GetMethod("EvictOne",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.True((bool)evict.Invoke(hot,null));Assert.True((bool)evict.Invoke(cold,null));
            CollectionAssert.AreEqual(before.Skip(1),Entries(hot).Select(e=>e.Key));CollectionAssert.AreEqual(Entries(hot).Select(e=>e.Key),Entries(cold).Select(e=>e.Key));Assert.AreEqual(hot.Counters.Evictions,cold.Counters.Evictions);Assert.AreEqual(hot.RetainedBytes,cold.RetainedBytes);Assert.AreEqual(hot.Counters.CacheBytes,cold.Counters.CacheBytes);
            Assert.AreEqual(hot.Advance(101,new NavEpochQuota(1,1,1,1,1),_=>true,1),cold.Advance(101,new NavEpochQuota(1,1,1,1,1),_=>true,1));Assert.AreEqual(hot.LogicalContinuationBinding,cold.LogicalContinuationBinding);Assert.AreEqual(hot.RetainedBytes,cold.RetainedBytes);
            hot.ClearWorld();cold.ClearWorld();
        }
        [TestCase(false)][TestCase(true)] public void ReadyCachePolicySurvivesPrimitiveWireAndLegacyDebtExecutesUnchanged(bool incremental)
        {
            var provider=Provider();var scheduler=new LayeredNavigationScheduler();var initial=Request(provider,0,0);
            Assert.True(scheduler.Submit(initial));FinishEpoch(scheduler,0,new NavEpochQuota(10000,10000,10000),initial,false,out var answer);answer.Release();
            var entry=((System.Collections.IEnumerable)typeof(LayeredNavigationScheduler).GetField("lru",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scheduler)).Cast<LayeredNavigationScheduler.CacheField>().Single();
            var field=entry.Field.ForkPrepared(_=>true);while(field.Status==NavSolveStatus.Pending)Assert.Greater(field.Advance(1000).Consumed,0);
            var state=scheduler.Save();state.CacheFields[0].Field=field.Save();state.CacheFields[0].Incremental=incremental;state.CacheFields[0].Work=41;field.ReleaseOwner();scheduler.ClearWorld();
            scheduler=LayeredNavigationScheduler.Restore(state,Array.Empty<NavScheduledRequest>());
            scheduler=PrimitiveRoundtrip(scheduler,Array.Empty<NavScheduledRequest>());
            var next=Request(provider,0,1);Assert.True(scheduler.Submit(next));int work=0;
            while(JobPart<NavSharedTerminalField>(FirstJob(scheduler),"Field")==null){Assert.Less(work++,2000);Assert.AreEqual(1,scheduler.Advance(100,new NavEpochQuota(10000,10000,10000),_=>true,1));}
            long debt=JobPartLong(FirstJob(scheduler),"FieldDebt");
            Assert.AreEqual(incremental?0:40,debt,"Legacy hit pays its first historical work unit; incremental Ready hit uses only actual work");
            var restored=PrimitiveRoundtrip(scheduler,new[]{next});Assert.AreEqual(debt,JobPartLong(FirstJob(restored),"FieldDebt"));
            long physical=scheduler.Counters.PhysicalFieldWork,logical=scheduler.Counters.LogicalFieldWork;
            Assert.AreEqual(scheduler.Advance(101,new NavEpochQuota(1,1,1,1,1),_=>true,1),restored.Advance(101,new NavEpochQuota(1,1,1,1,1),_=>true,1));
            Assert.AreEqual(scheduler.LogicalContinuationBinding,restored.LogicalContinuationBinding);Assert.AreEqual(physical,scheduler.Counters.PhysicalFieldWork);
            if(!incremental)Assert.AreEqual(logical+1,scheduler.Counters.LogicalFieldWork);
            scheduler.ClearWorld();restored.ClearWorld();
        }
        private static long JobPartLong(NavOwnedStorage job,string name)=>(long)job.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(job);
        [Test] public void PrimitivePartialCompilerPreservesConsumedWorkAndRejectsTruncation()
        {
            var provider=Provider();var scheduler=new LayeredNavigationScheduler();var request=Request(provider,0,0);Assert.True(scheduler.Submit(request));
            var quota=new NavEpochQuota(100,100,100);
            for(int n=0;n<25;n++){
                Assert.AreEqual(1,scheduler.Advance(0,quota,_=>true,1));
                long work=scheduler.Counters.PhysicalGraphWork;var restored=PrimitiveRoundtrip(scheduler,new[]{request});
                Assert.AreEqual(work,restored.Counters.PhysicalGraphWork);Assert.AreEqual(scheduler.RetainedBytes,restored.RetainedBytes);
                scheduler=restored;
            }
            var bytes=new byte[]{1,3,0,0};
            using(var stream=new MemoryStream(bytes))using(var reader=new BinaryReader(stream)){
                var codec=new NavigationPrimitiveWire.Reader(reader,_=>request,_=>null,bytes.Length);
                Assert.Throws<EndOfStreamException>(()=>codec.Scheduler());
            }
        }
    }
}

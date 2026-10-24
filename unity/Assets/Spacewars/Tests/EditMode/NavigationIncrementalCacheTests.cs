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
        private const BindingFlags CacheFlags=BindingFlags.Instance|BindingFlags.NonPublic;
        private static NavPersistentQueue<LayeredNavigationScheduler.CacheField> CacheEntries(LayeredNavigationScheduler root)=>
            (NavPersistentQueue<LayeredNavigationScheduler.CacheField>)typeof(LayeredNavigationScheduler).GetField("lru",CacheFlags).GetValue(root);
        private static LayeredNavigationScheduler.Job CacheHead(LayeredNavigationScheduler root)=>
            ((NavPersistentQueue<LayeredNavigationScheduler.Job>)typeof(LayeredNavigationScheduler).GetField("jobs",CacheFlags).GetValue(root)).First;
        private static LayeredNavigationScheduler CacheAdvance(NavigationRetentionBudget budget,LayeredNavigationScheduler root,long epoch,int steps)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,epoch+50000,out var operation));
            var next=root.ForkOwned(operation.OwnedReservation());
            next.Advance(epoch,new NavEpochQuota(100,100,100),_=>true,steps);next.SealPrepared();
            operation.Attach(next);operation.Commit(next,_=>{});root.ReleaseStorage();return next;
        }
        [Test]
        public void IncrementalCachePartialAliasColdNextAndNewOriginResumeAreExact()
        {
            var source=WarmNamedScheduler(NamedSchedulerSource(out var hot,out var first,out _),hot);
            var entry=CacheEntries(source).Single();Assert.True(entry.Incremental);Assert.AreEqual(NavSolveStatus.Pending,entry.Field.Status);
            var original=CaptureGraph(hot,entry);long fieldWork=source.Counters.PhysicalFieldWork;
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,30000,out var submit));
            var next=source.ForkOwned(submit.OwnedReservation());Assert.True(next.Submit(first));next.SealPrepared();submit.Attach(next);submit.Commit(next,_=>{});source.ReleaseStorage();source=next;
            long epoch=source.CaptureEpoch().Epoch+1;
            while(CacheHead(source).Field==null){Assert.Less(epoch,20000);source=CacheAdvance(hot,source,epoch++,1);}
            Assert.AreSame(CacheEntries(source).Single().Field,CacheHead(source).Field);Assert.Zero(CacheHead(source).FieldDebt);
            var bytes=CaptureGraph(hot,source);long charge=hot.PhysicalBytes;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));
            var restored=(LayeredNavigationScheduler)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
            Assert.AreSame(CacheEntries(restored).Single().Field,CacheHead(restored).Field);Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
            while(source.PendingCount>0){Assert.Less(epoch,20000);source=CacheAdvance(hot,source,epoch,1);restored=CacheAdvance(cold,restored,epoch++,1);CollectionAssert.AreEqual(CaptureGraph(hot,source),CaptureGraph(cold,restored));Assert.AreEqual(hot.PhysicalBytes,cold.PhysicalBytes);}
            Assert.AreEqual(fieldWork,source.Counters.PhysicalFieldWork,"Repeated identical query reuses its certified labels without new physical field work");
            CollectionAssert.AreEqual(original,CaptureGraph(hot,CacheEntries(source).Single()));
            // A different origin uses the same region cache, with private paid continuation as needed.
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,40000,out var originScope));var r=originScope.OwnedReservation();
            var old=first.Subscription;var at=new NavLocation(new NavPoint(-2.7,-2.7),NavLocation.FlatSurface);
            var sub=new NavRouteSubscription(1,1,1,1,0,1,3,1,1,1,1,at,old.AssignedEndpoint,null);
            var request=NavScheduledRequest.CopyOwned(new NavScheduledRequest(first.Provider,first.Clearance,first.Profile,first.Limits,first.Region,sub,first.MaxLabels),first.Provider,r);
            next=source.ForkOwned(r);Assert.True(next.Submit(request));request.StorageRoot.ReleaseOwner();next.SealPrepared();originScope.Attach(next);originScope.Commit(next,_=>{});source.ReleaseStorage();source=next;
            while(source.PendingCount>0){Assert.Less(epoch,20000);source=CacheAdvance(hot,source,epoch++,1);}
            Assert.Greater(source.Counters.PhysicalFieldWork,fieldWork,"New farther origin resumes the retained partial frontier");
            bytes=CaptureGraph(hot,source);Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,40001,out var originDecode));
            var originRestored=(LayeredNavigationScheduler)Decode(bytes,originDecode.OwnedReservation());originDecode.Attach(originRestored);originDecode.Commit(originRestored,_=>{});CollectionAssert.AreEqual(bytes,CaptureGraph(cold,originRestored));
            Assert.AreSame(CacheEntries(source).Single().Recipe,first,"Progress replacement keeps canonical recipe/key ownership");originRestored.ReleaseStorage();
            var answer=source.Save().Ready.Last();Assert.AreEqual(NavSolveStatus.Ready,answer.Status);Assert.AreEqual(at,answer.Route.Legs.First().From);Assert.AreEqual(old.AssignedEndpoint,answer.Route.Legs.Last().To);
            var fresh=new LayeredNavigationScheduler();Assert.True(fresh.Submit(request));
            for(int tick=0;fresh.PendingCount>0;tick++){Assert.Less(tick,2000);fresh.Advance(tick,new NavEpochQuota(1000,1000,1000),_=>true);}
            Assert.True(fresh.TryTake(_=>true,out var coldAnswer));Assert.AreEqual(coldAnswer.Route.Cost,answer.Route.Cost);
            CollectionAssert.AreEqual(coldAnswer.Route.Legs.Select(l=>(l.Kind,l.PortalId,l.From,l.To,l.Cost)),answer.Route.Legs.Select(l=>(l.Kind,l.PortalId,l.From,l.To,l.Cost)));
            coldAnswer.Release();fresh.ClearWorld();fresh.ReleaseStorage();
            source.ReleaseStorage();restored.ReleaseStorage();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(hot.ActiveScopes);Assert.Zero(cold.ActiveScopes);
        }
        [TestCase(false)][TestCase(true)]
        public void IncrementalReadyAndLegacyReadyKeepDistinctBodyTagsAndExactRoundtrip(bool incremental)
        {
            var source=WarmNamedScheduler(NamedSchedulerSource(out var hot,out var first,out _),hot);var cached=CacheEntries(source).Single();
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,30000,out var operation));var r=operation.OwnedReservation();
            var field=cached.Field.ForkPrepared(r.TryReserve);while(field.Status==NavSolveStatus.Pending)Assert.Greater(field.Advance(1000).Consumed,0);
            Assert.AreEqual(NavSolveStatus.Ready,field.Status);var entry=new LayeredNavigationScheduler.CacheField(first,field,41,field.EstimatedRetainedBytes,r.TryReserve,incremental:incremental);field.ReleaseOwner();operation.Attach(entry);operation.Commit(entry,_=>{});
            var bytes=CaptureGraph(hot,entry);Assert.AreEqual(new NavPhysicalTag(NavPhysicalStorageKind.Field,value:incremental?NavPhysicalValueKind.IncrementalCacheField:NavPhysicalValueKind.CacheField).Encoded,BitConverter.ToInt32(bytes,13));
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(LayeredNavigationScheduler.CacheField)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
            Assert.AreEqual(incremental,restored.Incremental);Assert.AreEqual(41,restored.Work);Assert.AreEqual(NavSolveStatus.Ready,restored.Field.Status);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
            entry.Release();source.ReleaseStorage();restored.Release();Assert.Zero(hot.PhysicalBytes);Assert.Zero(cold.PhysicalBytes);Assert.Zero(hot.HeldBytes);Assert.Zero(cold.HeldBytes);
        }
        [Test]
        public void PendingIncrementalEntryCannotDecodeAsHistoricalReadyTag()
        {
            var source=WarmNamedScheduler(NamedSchedulerSource(out var hot,out _,out _),hot);var entry=CacheEntries(source).Single();Assert.AreEqual(NavSolveStatus.Pending,entry.Field.Status);
            var bytes=CaptureGraph(hot,entry);var invalid=(byte[])bytes.Clone();Buffer.BlockCopy(BitConverter.GetBytes(new NavPhysicalTag(NavPhysicalStorageKind.Field,value:NavPhysicalValueKind.CacheField).Encoded),0,invalid,13,4);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));Assert.Throws<ArgumentException>(()=>Decode(invalid,decode.OwnedReservation()));decode.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);
            CollectionAssert.AreEqual(bytes,CaptureGraph(hot,entry));source.ReleaseStorage();Assert.Zero(hot.PhysicalBytes);
        }
        private static void ReplaceIncrementalCandidate(LayeredNavigationScheduler candidate,NavScheduledRequest recipe,INavStorageReservation reservation)
        {
            NavSharedTerminalField field=null;LayeredNavigationScheduler.Job job=null;
            try{
                field=CacheEntries(candidate).Single().Field.ForkPrepared(reservation.TryReserve);field.Advance(1);
                job=new LayeredNavigationScheduler.Job(reservation.TryReserve){Request=recipe,Field=field,FieldWork=1};field.ReleaseOwner();field=null;
                try{typeof(LayeredNavigationScheduler).GetMethod("StoreField",CacheFlags).Invoke(candidate,new object[]{job});}
                catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException){throw e.InnerException;}
            }finally{field?.ReleaseOwner();job?.Release();}
        }
        [Test]
        public void IncrementalCacheReplacementEveryNthRefusalKeepsOldEntryAndJobBytes()
        {
            var source=WarmNamedScheduler(NamedSchedulerSource(out var hot,out var first,out _),hot);var bytes=CaptureGraph(hot,source);long baseline=hot.PhysicalBytes;
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,30000,out var countScope));var count=new Refusal(countScope,int.MaxValue);var trial=source.ForkOwned(count);ReplaceIncrementalCandidate(trial,first,count);trial.SealPrepared();int total=count.Calls;trial.ReleaseStorage();countScope.Discard();Assert.Greater(total,1);Assert.AreEqual(baseline,hot.PhysicalBytes);
            for(int nth=1;nth<=total;nth++){
                Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,30000+nth,out var operation));var refusal=new Refusal(operation,nth);LayeredNavigationScheduler next=null;
                Assert.Throws<InvalidOperationException>(()=>{next=source.ForkOwned(refusal);ReplaceIncrementalCandidate(next,first,refusal);next.SealPrepared();},"cache replacement admission "+nth);
                Assert.AreEqual(nth,refusal.Calls);next?.ReleaseStorage();operation.Discard();Assert.AreEqual(baseline,hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,source));
            }
            TestContext.WriteLine("incremental cache replacement everyNth="+total);source.ReleaseStorage();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);Assert.Zero(hot.ActiveScopes);
        }
    }
}

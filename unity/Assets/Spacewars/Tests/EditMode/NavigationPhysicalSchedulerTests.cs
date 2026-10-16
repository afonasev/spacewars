using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [TestCase("jobs")][TestCase("ready")][TestCase("lru")][TestCase("providerRefs")]
        public void BodySchedulerPayloadQueuesColdBytesAliasesChargesAndEveryNth(string name)
        {
            var scheduler=NamedSchedulerSource(out var hot,out var first,out var second);if(name=="ready"||name=="lru")scheduler=WarmNamedScheduler(scheduler,hot);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var source=(NavOwnedStorage)typeof(LayeredNavigationScheduler).GetField(name,flags).GetValue(scheduler);source.Retain();scheduler.ReleaseStorage();scheduler=null;
            long charge=Unique(source);Assert.AreEqual(charge,hot.PhysicalBytes);byte[] bytes=CaptureGraph(hot,source);source.Release();source=null;first=null;second=null;Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var value=Decode(bytes,scope.OwnedReservation());scope.Attach(value);scope.Commit(value,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(value),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,value));value.ReleaseOwner();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            var counted=new NavigationRetentionBudget();Assert.True(counted.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var count));var all=new Refusal(count,int.MaxValue);var trial=Decode(bytes,all);count.Attach(trial);int allocations=all.Calls;count.Discard();Assert.Zero(counted.PhysicalBytes);Assert.Zero(counted.HeldBytes);
            for(int nth=1;nth<=allocations;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusal=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusal));Assert.AreEqual(nth,refusal.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        [TestCase("Adjacency")][TestCase("initial")][TestCase("Graph")][TestCase("Origin")][TestCase("Field")][TestCase("Connectors")][TestCase("warm")][TestCase("cancelled")][TestCase("ordinary")][TestCase("priorcompletion")][TestCase("singleton")]
        public void BodyActualSchedulerRootColdExactNextOperationAndHistory(string target)
        {
            var source=NamedSchedulerSource(out var hot,out var first,out var second,target!="ordinary"&&target!="priorcompletion"&&target!="singleton",target=="singleton");var flags=BindingFlags.Instance|BindingFlags.NonPublic;int epoch=0;
            LayeredNavigationScheduler.Job Head(LayeredNavigationScheduler root)=>((NavPersistentQueue<LayeredNavigationScheduler.Job>)typeof(LayeredNavigationScheduler).GetField("jobs",flags).GetValue(root)).First;
            if(target=="warm"||target=="ordinary"||target=="priorcompletion"){
                source=WarmNamedScheduler(source,hot);
                if(target=="priorcompletion"){Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,30000,out var submit));var next=source.ForkOwned(submit.OwnedReservation());Assert.True(next.Submit(first));next.SealPrepared();submit.Attach(next);submit.Commit(next,_=>{});source.ReleaseStorage();source=next;Assert.AreEqual(2,source.ReadyCount);Assert.AreEqual(1,source.PendingCount);}
            }
            else if(target!="initial"){
                while(target=="Adjacency"?Head(source).Compiler?.Phase!=LayeredNavigationCompileWork.Stage.Adjacency:target=="singleton"?Head(source).Singleton==null:target=="cancelled"?Head(source).DirectSubscription==null:Head(source).Phase.ToString()!=target){Assert.Less(++epoch,20000);Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,epoch+1000,out var operation));var next=source.ForkOwned(operation.OwnedReservation());next.Advance(epoch,new NavEpochQuota(100,100,100),_=>true,1);next.SealPrepared();operation.Attach(next);operation.Commit(next,_=>{});source.ReleaseStorage();source=next;}
                if(target=="cancelled"){Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,30000,out var cancel));var next=source.ForkOwned(cancel.OwnedReservation());Assert.True(next.Cancel(first.Subscription.StableKey));next.SealPrepared();cancel.Attach(next);cancel.Commit(next,_=>{});source.ReleaseStorage();source=next;Assert.AreSame(first,Head(source).Request);Assert.AreSame(second,Head(source).Subscribers.First);}
            }
            byte[] bytes=CaptureGraph(hot,source);long charge=hot.PhysicalBytes;string binding=source.LogicalContinuationBinding;
            byte[] Next(NavigationRetentionBudget budget,LayeredNavigationScheduler root,out long physical){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,40000,out var operation));var next=root.ForkOwned(operation.OwnedReservation());next.Advance(Math.Max(epoch+1,root.CaptureEpoch().Epoch),new NavEpochQuota(13,17,19,23,29),_=>true,7);next.SealPrepared();operation.Attach(next);operation.Commit(next,_=>{});var output=CaptureGraph(budget,next);physical=Unique(next);next.ReleaseStorage();return output;}
            if (target == "Adjacency") {
                Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve, 1, 40000, out var counting));
                var count = new Refusal(counting, int.MaxValue); var trial = source.ForkOwned(count);
                trial.Advance(Math.Max(epoch + 1, source.CaptureEpoch().Epoch), new NavEpochQuota(13,17,19,23,29), _ => true, 7);
                trial.SealPrepared(); int total = count.Calls; trial.ReleaseStorage(); counting.Discard();
                for (int nth = 1; nth <= total; nth++) {
                    Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve, 1, 40000, out var operation));
                    var refusal = new Refusal(operation, nth); LayeredNavigationScheduler next = null;
                    Assert.Throws<InvalidOperationException>(() => {
                        next = source.ForkOwned(refusal);
                        next.Advance(Math.Max(epoch + 1, source.CaptureEpoch().Epoch), new NavEpochQuota(13,17,19,23,29), _ => true, 7);
                        next.SealPrepared();
                    }, "scheduler adjacency reservation " + nth);
                    Assert.AreEqual(nth, refusal.Calls); next?.ReleaseStorage(); operation.Discard();
                    Assert.AreEqual(charge, hot.PhysicalBytes); Assert.Zero(hot.HeldBytes);
                    CollectionAssert.AreEqual(bytes, CaptureGraph(hot, source));
                }
                TestContext.WriteLine($"actual scheduler adjacency everyNth={total}/{total}");
            }
            var expected=Next(hot,source,out long nextCharge);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,source));source.ReleaseStorage();source=null;first=null;second=null;Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(LayeredNavigationScheduler)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);Assert.AreEqual(binding,restored.LogicalContinuationBinding);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored,out long actualCharge));Assert.AreEqual(nextCharge,actualCharge);restored.ReleaseStorage();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void BodySchedulerRootBundleAliasesEveryNthAndMalformedHeaders(bool warm)
        {
            var source=NamedSchedulerSource(out var hot,out var first,out var second,true);if(warm)source=WarmNamedScheduler(source,hot);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Capture,1,10000,out var capture));var jobs=(NavOwnedStorage)typeof(LayeredNavigationScheduler).GetField("jobs",flags).GetValue(source);var refs=(NavOwnedStorage)typeof(LayeredNavigationScheduler).GetField("providerRefs",flags).GetValue(source);var bundle=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{source,source,jobs,refs,first.StorageRoot},capture.TryReserve);capture.Attach(bundle);capture.Commit(bundle,_=>{});source.ReleaseStorage();source=null;var bytes=CaptureGraph(hot,bundle);long charge=Unique(bundle);Assert.AreEqual(charge,hot.PhysicalBytes);bundle.Release();bundle=null;first=null;second=null;jobs=null;refs=null;Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreSame(restored[0],restored[1]);Assert.AreSame(restored[2],typeof(LayeredNavigationScheduler).GetField("jobs",flags).GetValue(restored[0]));Assert.AreSame(restored[3],typeof(LayeredNavigationScheduler).GetField("providerRefs",flags).GetValue(restored[0]));Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));var rootBytes=CaptureGraph(cold,restored[0]);restored.Release();Assert.Zero(cold.PhysicalBytes);
            var counted=new NavigationRetentionBudget();Assert.True(counted.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var count));var all=new Refusal(count,int.MaxValue);var trial=Decode(bytes,all);count.Attach(trial);int allocations=all.Calls;count.Discard();Assert.Zero(counted.PhysicalBytes);Assert.Zero(counted.HeldBytes);
            for(int nth=1;nth<=allocations;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusal=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusal));Assert.AreEqual(nth,refusal.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
            foreach(int offset in new[]{17,25,185}){var invalid=(byte[])rootBytes.Clone();Buffer.BlockCopy(BitConverter.GetBytes(long.MinValue),0,invalid,offset,8);var bad=new NavigationRetentionBudget();Assert.True(bad.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));Assert.Throws<ArgumentException>(()=>Decode(invalid,rejected.OwnedReservation()));rejected.Discard();Assert.Zero(bad.PhysicalBytes);Assert.Zero(bad.HeldBytes);}
        }
        [Test]
        public void BodyProviderReferencesEqualBindingDistinctRootsColdLogicalCountsAndNextCow()
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var birth));var reservation=birth.OwnedReservation();var input=(LayeredNavigationProvider)NavigationAdmission.CertifiedFlatProvider(new NavGeometry(1,new NavObstacle[0],1));var a=LayeredNavigationProvider.CopyOwned(input,reservation);var b=LayeredNavigationProvider.CopyOwned(input,reservation);Assert.AreEqual(a.Binding,b.Binding);Assert.AreNotSame(a.StorageRoot,b.StorageRoot);
            var refs=NavProviderReferences.CreateOwned(reservation);refs.Add(a,a.EstimatedRetainedBytes);refs.Add(a,a.EstimatedRetainedBytes);refs.Add(b,b.EstimatedRetainedBytes);refs.Seal();a.StorageRoot.ReleaseOwner();b.StorageRoot.ReleaseOwner();birth.Attach(refs);birth.Commit(refs,_=>{});Assert.AreEqual(2,refs.Count);Assert.AreEqual(2,refs.PhysicalEntries.Single(e=>ReferenceEquals(e.Provider,a)).Count);
            var bytes=CaptureGraph(hot,refs);long charge=hot.PhysicalBytes;
            byte[] Next(NavigationRetentionBudget budget,NavProviderReferences root,out long physical){var entries=root.PhysicalEntries.ToArray();Assert.AreEqual(2,entries.Length);Assert.AreEqual(entries[0].Provider.Binding,entries[1].Provider.Binding);Assert.AreNotSame(entries[0].Provider,entries[1].Provider);Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,100,out var operation));var next=root.ForkOwned(operation.OwnedReservation());next.Remove(entries[0].Provider);next.Remove(entries[1].Provider);next.Seal();operation.Attach(next);operation.Commit(next,_=>{});Assert.AreEqual(1,next.Count);Assert.AreEqual(1,next.PhysicalEntries.Single().Count);var output=CaptureGraph(budget,next);physical=Unique(next);next.ReleaseStorage();return output;}
            var expected=Next(hot,refs,out long nextCharge);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,refs));refs.ReleaseStorage();refs=null;a=null;b=null;Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavProviderReferences)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored,out long actualCharge));Assert.AreEqual(nextCharge,actualCharge);restored.ReleaseStorage();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
        }
        [Test]
        public void BodyJobMalformedWorkAssociationRejectsBeforeAggregateBirth()
        {
            var source=NamedSchedulerSource(out var hot,out var first,out var second,true);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var jobs=(NavPersistentQueue<LayeredNavigationScheduler.Job>)typeof(LayeredNavigationScheduler).GetField("jobs",flags).GetValue(source);var job=jobs.First;var bytes=CaptureGraph(hot,job);long charge=hot.PhysicalBytes;int references=first.StorageRoot.References;
            foreach(int offset in new[]{25,58}){var invalid=(byte[])bytes.Clone();if(offset==25)Buffer.BlockCopy(BitConverter.GetBytes((int)LayeredNavigationScheduler.JobPhase.Field),0,invalid,offset,4);else invalid[offset]=1;
                var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));Assert.Throws<ArgumentException>(()=>Decode(invalid,decode.OwnedReservation()));decode.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);Assert.AreEqual(references,first.StorageRoot.References);Assert.AreEqual(charge,hot.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,job));}
            source.ReleaseStorage();source=null;job=null;jobs=null;first=null;second=null;Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
        }
        private static LayeredNavigationScheduler NamedSchedulerSource(out NavigationRetentionBudget budget,out NavScheduledRequest first,out NavScheduledRequest second,bool attempt=false,bool singleton=false)
        {
            budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));var r=scope.OwnedReservation();var provider=LayeredNavigationProvider.CopyOwned((LayeredNavigationProvider)NavigationAdmission.CertifiedFlatProvider(new NavGeometry(3,new[]{new NavObstacle(-.5,-.5,.5,.5)},1)),r);
            var origin=new NavLocation(new NavPoint(-2,0),NavLocation.FlatSurface);var goal=new NavLocation(new NavPoint(2,0),NavLocation.FlatSurface);var region=new NavTerminalRegion("scheduler-names",1,goal,new[]{goal});var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('c',64),"distance",1,1);var limits=new NavGraphCompileLimits(2000,2000,10000);
            NavScheduledRequest Recipe(int entity){var sub=new NavRouteSubscription(1,1,1,1,0,1,entity,1,1,1,1,origin,goal,null);return NavScheduledRequest.CopyOwned(new NavScheduledRequest(provider,clearance,profile,limits,singleton?null:region,sub,4096,singleton),provider,r);}
            first=Recipe(1);second=Recipe(2);var scheduler=LayeredNavigationScheduler.CreateOwned(r);if(attempt)scheduler.BindAttemptArtifacts(new string('d',64));Assert.True(scheduler.Submit(first));Assert.True(scheduler.Submit(second));first.StorageRoot.ReleaseOwner();second.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();scheduler.SealPrepared();scope.Attach(scheduler);scope.Commit(scheduler,_=>{});return scheduler;
        }
        [TestCase("jobsByKey")][TestCase("subscribers")][TestCase("submittedEpoch")][TestCase("cachedFields")]
        public void BodyActualSchedulerLookupOwnsExactRecipeRoleIndependentlyColdBytesAndNextCow(string indexName)
        {
            var scheduler=NamedSchedulerSource(out var hot,out var first,out var second);if(indexName=="cachedFields")scheduler=WarmNamedScheduler(scheduler,hot);var index=typeof(LayeredNavigationScheduler).GetField(indexName,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scheduler);var lookup=(NavCowDictionary<NavNameKey,int>)index.GetType().GetField("lookup",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(index);lookup.Retain();scheduler.ReleaseStorage();scheduler=null;Assert.AreEqual(Unique(lookup),hot.PhysicalBytes);
            foreach(var pair in lookup){Assert.AreEqual(indexName=="jobsByKey"||indexName=="cachedFields"?NavNameRole.RecipeArtifactKey:NavNameRole.SubscriptionStableKey,pair.Key.Reference.Role);Assert.AreSame(pair.Key.Reference.Resolve(),pair.Key.Value);Assert.True(ReferenceEquals(pair.Key.Reference.Owner,first.StorageRoot)||ReferenceEquals(pair.Key.Reference.Owner,second.StorageRoot));}
            var bytes=CaptureGraph(hot,lookup);long charge=hot.PhysicalBytes;string key=lookup.First().Key.Value;
            byte[] Next(NavigationRetentionBudget budget,NavCowDictionary<NavNameKey,int> root,out long physical){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,10,out var projection));var fork=root.ForkOwned(projection.OwnedReservation());Assert.True(fork.Remove(key));fork.Seal();projection.Attach(fork);projection.Commit(fork,_=>{});var result=CaptureGraph(budget,fork);physical=Unique(fork);fork.Release();return result;}
            var next=Next(hot,lookup,out long expectedCharge);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,lookup));lookup.Release();lookup=null;first=null;second=null;Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavCowDictionary<NavNameKey,int>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(next,Next(cold,restored,out long actualCharge));Assert.AreEqual(expectedCharge,actualCharge);restored.ReleaseOwner();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void ActualOwnedSchedulerPreservesScalarOwnerRolesAndCancelledFirstSubscriberHistory(bool direct)
        {
            var root=NamedSchedulerSource(out var budget,out var first,out var second,true);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            NavOwnedStorage Job(LayeredNavigationScheduler value)=>((System.Collections.IEnumerable)typeof(LayeredNavigationScheduler).GetField("jobs",flags).GetValue(value)).Cast<NavOwnedStorage>().Single();
            object Get(object value,string name)=>value.GetType().GetField(name,flags).GetValue(value);
            string keyField=direct?"DirectSubscription":"ActiveConnectorKey",referenceField=direct?"directKeyReference":"activeKeyReference";int steps=0;
            while((string)Get(Job(root),keyField)==null){Assert.Less(++steps,20000);Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,steps+1,out var operation));var fork=root.ForkOwned(operation.OwnedReservation());fork.Advance(steps,new NavEpochQuota(100,100,100),_=>true,1);fork.SealPrepared();operation.Attach(fork);operation.Commit(fork,_=>{});root.ReleaseStorage();root=fork;}
            var pin=Job(root);pin.Retain();var selector=(NavNameReference)Get(pin,referenceField);Assert.AreEqual(NavNameRole.SubscriptionStableKey,selector.Role);Assert.AreSame(first.StorageRoot,selector.Owner);Assert.AreSame(first.Subscription.StableKey,selector.Resolve());Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(pin.GetType()),512);
            var graphKey=(NavNameReference)Get(root,"cachedGraphKeyReference");Assert.AreEqual(NavNameRole.RecipeCompileKey,graphKey.Role);Assert.AreSame(first.StorageRoot,graphKey.Owner);Assert.AreSame(first.CompileKey,graphKey.Resolve());
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,steps+2,out var cancel));var next=root.ForkOwned(cancel.OwnedReservation());Assert.True(next.Cancel(first.Subscription.StableKey));Assert.AreEqual(1,next.PendingCount);var after=Job(next);var subscribers=(System.Collections.IEnumerable)after.GetType().GetProperty("Subscribers",flags).GetValue(after);Assert.AreSame(second,subscribers.Cast<NavScheduledRequest>().Single());Assert.AreSame(first,after.GetType().GetProperty("Request",flags).GetValue(after));
            if(direct){Assert.AreSame(first.Subscription.StableKey,Get(after,keyField));Assert.AreSame(first.StorageRoot,((NavNameReference)Get(after,referenceField)).Owner);}else{Assert.Null(Get(after,keyField));Assert.Null(((NavNameReference)Get(after,referenceField)).Owner);}
            Assert.AreSame(first.Subscription.StableKey,selector.Resolve());next.SealPrepared();cancel.Attach(next);cancel.Commit(next,_=>{});root.ReleaseStorage();Assert.Greater(first.StorageRoot.References,0);
            // The old immutable Job pin keeps its historical selectors alive.
            next.ReleaseStorage();Assert.AreEqual(Unique(pin),budget.PhysicalBytes);pin.Release();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
        }
        private static LayeredNavigationScheduler WarmNamedScheduler(LayeredNavigationScheduler root,NavigationRetentionBudget budget)
        {
            int steps=0;while(root.ReadyCount<2){Assert.Less(++steps,20000);Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,steps+100,out var operation));var fork=root.ForkOwned(operation.OwnedReservation());fork.Advance(steps,new NavEpochQuota(100,100,100),_=>true,128);fork.SealPrepared();operation.Attach(fork);operation.Commit(fork,_=>{});root.ReleaseStorage();root=fork;}
            Assert.Zero(root.PendingCount);Assert.AreEqual(1,root.Counters.CacheFields);return root;
        }
        [Test]
        public void ActualOwnedSchedulerWarmCacheClearEveryNthRefusalPreservesPinnedSourceAndReleasesBirths()
        {
            var root=NamedSchedulerSource(out var budget,out var first,out var second);root=WarmNamedScheduler(root,budget);long baseline=budget.PhysicalBytes;string binding=root.LogicalContinuationBinding;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,30000,out var countScope));var counter=new Refusal(countScope,int.MaxValue);var trial=root.ForkOwned(counter);countScope.Attach(trial);trial.ClearWorld();trial.SealPrepared();int allocations=counter.Calls;countScope.Discard();Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Greater(allocations,1);
            for(int nth=1;nth<=allocations;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,30000+nth,out var operation));var refusing=new Refusal(operation,nth);LayeredNavigationScheduler candidate=null;
                try{candidate=root.ForkOwned(refusing);operation.Attach(candidate);candidate.ClearWorld();candidate.SealPrepared();Assert.Fail("Expected exact refusal "+nth);}
                catch(InvalidOperationException){Assert.AreEqual(nth,refusing.Calls);}
                finally{operation.Discard();}
                Assert.AreEqual(binding,root.LogicalContinuationBinding);Assert.AreEqual(2,root.ReadyCount);Assert.AreEqual(1,root.Counters.CacheFields);Assert.AreEqual(Unique(root),budget.PhysicalBytes);Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
            root.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
        }
        [TestCase("jobsByKey")][TestCase("subscribers")][TestCase("submittedEpoch")][TestCase("cachedFields")][TestCase("subscriber-queue")]
        public void BodyActualSchedulerPersistentContainersColdAliasesExactNextMutationAndEveryNth(string kind)
        {
            var scheduler=NamedSchedulerSource(out var hot,out var first,out var second);if(kind=="cachedFields")scheduler=WarmNamedScheduler(scheduler,hot);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            NavOwnedStorage macro;if(kind=="subscriber-queue"){var job=((System.Collections.IEnumerable)typeof(LayeredNavigationScheduler).GetField("jobs",flags).GetValue(scheduler)).Cast<NavOwnedStorage>().Single();macro=(NavOwnedStorage)job.GetType().GetProperty("Subscribers",flags).GetValue(job);}else macro=(NavOwnedStorage)typeof(LayeredNavigationScheduler).GetField(kind,flags).GetValue(scheduler);
            // Produce actual historical IDs and free slots before capturing the
            // standalone root. The immutable parent remains pinned meanwhile.
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Projection,1,30001,out var churn));NavOwnedStorage source;
            if(macro is NavPersistentIndex<NavNameKey,long> longs){var fork=longs.ForkOwned(churn.OwnedReservation());var key=fork.Keys.First();long value=fork[key];fork.Remove(key);fork.Add(key,value,key.Reference.Owner);fork.Remove(key);fork.Seal();source=fork;}
            else if(macro is NavPersistentIndex<NavNameKey,NavSubscriberLink> links){var fork=links.ForkOwned(churn.OwnedReservation());var key=fork.Keys.First();var value=fork[key];fork.Remove(key);fork.Add(key,value,key.Reference.Owner);fork.Remove(key);fork.Seal();source=fork;}
            else {var fork=((NavPersistentQueue<NavScheduledRequest>)macro).ForkOwned(churn.OwnedReservation());fork.MoveLast(fork.FirstId);fork.RemoveFirst();fork.AddLast(second);fork.Seal();source=fork;}
            var lookup=(NavOwnedStorage)source.GetType().GetField(kind=="subscriber-queue"?"records":"lookup",flags).GetValue(source);var slots=kind=="subscriber-queue"?null:(NavOwnedStorage)source.GetType().GetField("slots",flags).GetValue(source);
            var bundle=NavRetainedArray<NavOwnedStorage>.Copy(new[]{source,source,lookup,slots,first.StorageRoot},churn.TryReserve);Assert.LessOrEqual(NavStorageLayout.ObjectBytesBound(source.GetType()),source.StorageBytes);source.ReleaseOwner();churn.Attach(bundle);churn.Commit(bundle,_=>{});scheduler.ReleaseStorage();scheduler=null;Assert.AreEqual(Unique(bundle),hot.PhysicalBytes);var bytes=CaptureGraph(hot,bundle);long charge=hot.PhysicalBytes;
            byte[] Next(NavigationRetentionBudget budget,NavOwnedStorage value,NavScheduledRequest owner,out long physical){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,40000,out var operation));NavOwnedStorage fork;
                if(value is NavPersistentIndex<NavNameKey,long> a){var next=a.ForkOwned(operation.OwnedReservation());var key=kind=="jobsByKey"||kind=="cachedFields"?NavNameKey.Artifact(owner):NavNameKey.Subscription(owner.Subscription);next.Add(key,17,owner.StorageRoot);next.Seal();fork=next;
                }else if(value is NavPersistentIndex<NavNameKey,NavSubscriberLink> b){var next=b.ForkOwned(operation.OwnedReservation());next.Add(NavNameKey.Subscription(owner.Subscription),new NavSubscriberLink(17,23),owner.StorageRoot);next.Seal();fork=next;}
                else {var next=((NavPersistentQueue<NavScheduledRequest>)value).ForkOwned(operation.OwnedReservation());next.MoveLast(next.FirstId);next.RemoveFirst();next.Seal();fork=next;}
                operation.Attach(fork);operation.Commit(fork,_=>{});var output=CaptureGraph(budget,fork);physical=Unique(fork);fork.ReleaseOwner();return output;
            }
            var expected=Next(hot,bundle[0],(NavScheduledRequest)((NavDeclarationStorage)bundle[4]).Facade,out long nextCharge);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,bundle));bundle.Release();bundle=null;macro=null;source=null;lookup=null;slots=null;first=null;second=null;Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreSame(restored[0],restored[1]);Assert.AreSame(restored[2],restored[0].GetType().GetField(kind=="subscriber-queue"?"records":"lookup",flags).GetValue(restored[0]));Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));CollectionAssert.AreEqual(expected,Next(cold,restored[0],(NavScheduledRequest)((NavDeclarationStorage)restored[4]).Facade,out long actualCharge));Assert.AreEqual(nextCharge,actualCharge);restored.Release();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            var countBudget=new NavigationRetentionBudget();Assert.True(countBudget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var countScope));var counter=new Refusal(countScope,int.MaxValue);var trial=Decode(bytes,counter);countScope.Attach(trial);int allocations=counter.Calls;countScope.Discard();Assert.Zero(countBudget.PhysicalBytes);Assert.Zero(countBudget.HeldBytes);
            for(int nth=1;nth<=allocations;nth++){var bad=new NavigationRetentionBudget();Assert.True(bad.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusal=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusal));Assert.AreEqual(nth,refusal.Calls);rejected.Discard();Assert.Zero(bad.PhysicalBytes);Assert.Zero(bad.HeldBytes);}
        }
        [TestCase(false)][TestCase(true)]
        public void BodyPersistentMalformedNumericCyclesAndHeaderDimensionsFailClosed(bool queue)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));NavOwnedStorage root;
            if(queue){var value=NavPersistentQueue<int>.CreateOwned(scope.OwnedReservation());value.AddLast(3);value.AddLast(7);value.Seal();root=value;}
            else {var value=NavPersistentIndex<NavNameKey,long>.CreateOwned(scope.OwnedReservation());var key=NavOwnedText.Copy("retired-key",scope.OwnedReservation());var name=new NavNameKey(new NavNameReference(key,NavNameRole.Text));value.Add(name,11,key);key.Release();value.Remove(name);value.Seal();root=value;}
            scope.Attach(root);scope.Commit(root,_=>{});var bytes=CaptureGraph(hot,root);long charge=hot.PhysicalBytes;
            int[] offsets=queue?new[]{17,25,33}:new[]{17,21};foreach(int offset in offsets){var invalid=(byte[])bytes.Clone();var replacement=queue?BitConverter.GetBytes(offset==17?0L:99L):BitConverter.GetBytes(offset==17?2:99);Buffer.BlockCopy(replacement,0,invalid,offset,replacement.Length);Reject(invalid);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,root));Assert.AreEqual(charge,hot.PhysicalBytes);}
            if(queue){var records=(NavCowDictionary<long,NavPersistentQueue<int>.Record>)typeof(NavPersistentQueue<int>).GetField("records",flags).GetValue(root);typeof(NavPersistentQueue<int>.Record).GetField("Next",flags).SetValue(records[((NavPersistentQueue<int>)root).FirstId],((NavPersistentQueue<int>)root).FirstId);}
            else {var slots=(NavCowDictionary<int,NavPersistentIndex<NavNameKey,long>.Slot>)typeof(NavPersistentIndex<NavNameKey,long>).GetField("slots",flags).GetValue(root);typeof(NavPersistentIndex<NavNameKey,long>.Slot).GetField("FreeNext",flags).SetValue(slots[0],0);}
            Reject(CaptureGraph(hot,root));root.ReleaseOwner();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
            void Reject(byte[] invalid){var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));Assert.Throws<ArgumentException>(()=>Decode(invalid,decode.OwnedReservation()));decode.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);}
        }
        [TestCase("queue")][TestCase("live-slot")][TestCase("free-slot")][TestCase("distinct-owner")]
        public void BodyPersistentNullPayloadAndEqualTextDifferentOwnerFailClosed(string kind)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var scheduler=NamedSchedulerSource(out var hot,out var first,out var second);
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Projection,1,50000,out var scope));NavOwnedStorage root;
            if(kind=="queue") {var queue=NavPersistentQueue<int>.CreateOwned(scope.OwnedReservation());queue.AddLast(7);queue.Seal();root=queue;}
            else {var index=NavPersistentIndex<NavNameKey,long>.CreateOwned(scope.OwnedReservation());index.Add(NavNameKey.Artifact(first),11,first.StorageRoot);if(kind=="free-slot")index.Remove(NavNameKey.Artifact(first));index.Seal();root=index;}
            scope.Attach(root);scope.Commit(root,_=>{});
            var map=root.GetType().GetField(kind=="queue"?"records":"slots",flags).GetValue(root);var node=map.GetType().GetField("root",flags).GetValue(map);var valueField=node.GetType().GetField("Value",flags);var payload=(NavOwnedStorage)valueField.GetValue(node);
            if(kind=="distinct-owner"){
                Assert.AreEqual(first.ArtifactKey,second.ArtifactKey);Assert.AreNotSame(first.StorageRoot,second.StorageRoot);
                var keyField=payload.GetType().GetField("Key",flags);var old=(NavNameKey)keyField.GetValue(payload);var replacement=NavNameKey.Artifact(second);replacement.Reference.Owner.Retain();keyField.SetValue(payload,replacement);old.Reference.Owner.Release();
            }else {valueField.SetValue(node,null);payload.Release();}
            scheduler.ReleaseStorage();var bytes=CaptureGraph(hot,root);var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));Assert.Throws<ArgumentException>(()=>Decode(bytes,decode.OwnedReservation()));decode.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.Zero(cold.ActiveScopes);root.ReleaseOwner();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void BodyActualPersistentLeafPinOutlivesContainerAndColdRelease(bool queue)
        {
            var scheduler=NamedSchedulerSource(out var hot,out var first,out var second);var flags=BindingFlags.Instance|BindingFlags.NonPublic;NavOwnedStorage leaf;
            if(queue){var job=((System.Collections.IEnumerable)typeof(LayeredNavigationScheduler).GetField("jobs",flags).GetValue(scheduler)).Cast<NavOwnedStorage>().Single();var records=(NavPersistentQueue<NavScheduledRequest>)job.GetType().GetProperty("Subscribers",flags).GetValue(job);var map=(NavCowDictionary<long,NavPersistentQueue<NavScheduledRequest>.Record>)records.GetType().GetField("records",flags).GetValue(records);leaf=map[records.FirstId];}
            else {var index=(NavPersistentIndex<NavNameKey,long>)typeof(LayeredNavigationScheduler).GetField("jobsByKey",flags).GetValue(scheduler);var slots=(NavCowDictionary<int,NavPersistentIndex<NavNameKey,long>.Slot>)index.GetType().GetField("slots",flags).GetValue(index);leaf=slots[0];}
            leaf.Retain();scheduler.ReleaseStorage();Assert.AreEqual(Unique(leaf),hot.PhysicalBytes);var bytes=CaptureGraph(hot,leaf);long charge=hot.PhysicalBytes;leaf.Release();Assert.Zero(hot.PhysicalBytes);Assert.Zero(hot.HeldBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));restored.ReleaseOwner();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
        }
    }
}

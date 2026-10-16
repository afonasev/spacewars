using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationPrimitiveCheckpointTests
    {
        private static T Roundtrip<T>(Action<NavigationPrimitiveWire.Writer> write,Func<NavigationPrimitiveWire.Reader,T> read)
        {
            byte[] bytes;
            using(var stream=new MemoryStream()){
                using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
                    write(new NavigationPrimitiveWire.Writer(writer,_=>throw new ArgumentException("Unexpected external recipe."),_=>throw new ArgumentException("Unexpected external route.")));
                bytes=stream.ToArray();
            }
            using(var stream=new MemoryStream(bytes))using(var reader=new BinaryReader(stream)){
                var codec=new NavigationPrimitiveWire.Reader(reader,_=>throw new ArgumentException("Unexpected external recipe."),_=>throw new ArgumentException("Unexpected external route."),bytes.Length);
                var value=read(codec);Assert.AreEqual(stream.Length,stream.Position);return value;
            }
        }
        private static void Seal(object work)
        {var method=work.GetType().GetMethod("SealPrepared",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);Assert.NotNull(method);method.Invoke(work,null);}
        [Test] public void PartitionPreparedCowLeavesCommittedPagesAndWorkUnchanged()
        {
            var map=ThreeCrossingsMap.Default;
            var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.58);
            var profile=new NavGraphProfile(NavigationArrivalTestFactory.Sha("cow-partition"),"distance",1,.8);
            var limits=new NavGraphCompileLimits(300000,150000,600000);
            var work=new LayeredNavigationCompileWork(provider,clearance,profile,limits);work.Advance(2048);
            byte[] Bytes(LayeredNavigationCompileWork value){using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(value.Save());return stream.ToArray();}}
            Seal(work);var original=Bytes(work);int consumed=work.Consumed;
            var method=typeof(LayeredNavigationCompileWork).GetMethod("ForkPartitionPrepared",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(method);long reserved=0;
            Func<long,bool> reserve=n=>{reserved+=n;return true;};
            var prepared=(LayeredNavigationCompileWork)method.Invoke(work,new object[]{reserve});Assert.AreEqual(consumed,prepared.Consumed);
            Assert.AreEqual(128,prepared.Advance(128));Assert.AreEqual(consumed,work.Consumed);
            CollectionAssert.AreEqual(original,Bytes(work),"Prepared writes must never mutate captured committed pages.");
            Assert.Greater(reserved,0);Assert.Less(reserved,512*1024,"This bounded step must not clone the full graph.");
            var restored=LayeredNavigationCompileWork.Restore(provider,clearance,profile,limits,Roundtrip(c=>c.Compile(prepared.Save()),c=>c.Compile()));
            Assert.AreEqual(prepared.Consumed,restored.Consumed);Assert.AreEqual(prepared.Advance(128),restored.Advance(128));
            CollectionAssert.AreEqual(Bytes(prepared),Bytes(restored));
            Assert.Throws<InvalidOperationException>(()=>work.Advance(1));
            Seal(restored);var reject=Assert.Throws<System.Reflection.TargetInvocationException>(()=>method.Invoke(restored,new object[]{new Func<long,bool>(_=>false)}));
            Assert.IsInstanceOf<InvalidOperationException>(reject.InnerException);
        }
        [Test] public void PackedPreparedCowPreservesCommittedLabelsHeapAndExactColdCapacity()
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,Array.Empty<NavObstacle>(),1));
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);
            var profile=new NavGraphProfile(NavigationArrivalTestFactory.Sha("cow-field"),"distance",1,1);
            var graph=LayeredNavigationGraphCompiler.Compile(provider,clearance,profile,new NavGraphCompileLimits(2000,2000,10000));
            var candidates=Enumerable.Range(0,8).Select(n=>new NavLocation(new NavPoint(-2+n%4,-1+n/4),NavLocation.FlatSurface)).ToArray();
            var region=new NavTerminalRegion("cow-field",1,candidates[0],candidates);var field=new NavSharedTerminalField(graph,region,2048);field.Advance(128);
            byte[] Bytes(NavSharedTerminalField value){using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Field(value.Save());return stream.ToArray();}}
            Seal(field);var before=Bytes(field);long charge=field.EstimatedRetainedBytes;int labels=field.LabelCount,frontier=field.FrontierCount;long reserved=0;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var forkMethod=typeof(NavSharedTerminalField).GetMethod("ForkPackedPrepared",flags);var seal=typeof(NavSharedTerminalField).GetMethod("SealPackedPrepared",flags);
            Func<long,bool> reserve=n=>{reserved+=n;return true;};var fork=(NavSharedTerminalField)forkMethod.Invoke(field,new object[]{reserve});
            Assert.Greater(fork.Advance(128).Consumed,0);CollectionAssert.AreEqual(before,Bytes(field));
            Assert.AreEqual(labels,field.LabelCount);Assert.AreEqual(frontier,field.FrontierCount);Assert.AreEqual(charge,field.EstimatedRetainedBytes);
            Assert.Greater(reserved,0);Assert.Less(reserved,512*1024);seal.Invoke(fork,null);
            var restored=NavSharedTerminalField.Restore(graph,region,2048,Roundtrip(c=>c.Field(fork.Save()),c=>c.Field()));
            Assert.AreEqual(fork.EstimatedRetainedBytes,restored.EstimatedRetainedBytes);CollectionAssert.AreEqual(Bytes(fork),Bytes(restored));
            Assert.Throws<InvalidOperationException>(()=>field.Advance(1));
            var next=(NavSharedTerminalField)forkMethod.Invoke(fork,new object[]{reserve});Assert.AreEqual(restored.Advance(1).Consumed,next.Advance(1).Consumed);
            CollectionAssert.AreEqual(Bytes(restored),Bytes(next));
        }
        [Test] public void FlatCompilerAndConnectorCowResumeAllCommittedPhasesWithoutMutation()
        {
            var provider=NavigationArrivalTestFactory.Layered(groundBlockers:new[]{new NavObstacle(-1,-1,1,1)});
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(NavigationArrivalTestFactory.Sha("flat-cow"),"distance",1,2);
            var limits=new NavGraphCompileLimits(12000,8000,50000);var normal=new LayeredNavigationCompileWork(provider,clearance,profile,limits);var cow=new LayeredNavigationCompileWork(provider,clearance,profile,limits);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var fork=typeof(LayeredNavigationCompileWork).GetMethod("ForkPrepared",flags);Func<long,bool> reserve=_=>true;
            byte[] CompilerBytes(LayeredNavigationCompileWork value){using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(value.Save());return stream.ToArray();}}
            while(normal.Phase!=LayeredNavigationCompileWork.Stage.Ready){
                Seal(cow);var before=CompilerBytes(cow);var prepared=(LayeredNavigationCompileWork)fork.Invoke(cow,new object[]{reserve});
                Assert.AreEqual(normal.Advance(7),prepared.Advance(7));CollectionAssert.AreEqual(before,CompilerBytes(cow));
                cow=prepared;Assert.AreEqual(normal.Consumed,cow.Consumed);CollectionAssert.AreEqual(CompilerBytes(normal),CompilerBytes(cow));
            }
            var goal=NavigationArrivalTestFactory.At(6,0);var region=new NavTerminalRegion("cow-connector",1,goal,new[]{goal});
            var field=new NavSharedTerminalField(cow.Graph,region,4096);while(field.Status==NavSolveStatus.Pending)field.Advance(100);
            var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,NavigationArrivalTestFactory.At(-6,0),goal,null);
            var singleton=new NavIncrementalSingletonRoute(cow.Graph,subscription,4096);var singletonCow=new NavIncrementalSingletonRoute(cow.Graph,subscription,4096);
            var singleFork=typeof(NavIncrementalSingletonRoute).GetMethod("ForkPrepared",flags);
            while(singleton.Status==NavSolveStatus.Pending){
                Seal(singletonCow);int before=singletonCow.StateCount;var prepared=(NavIncrementalSingletonRoute)singleFork.Invoke(singletonCow,new object[]{reserve});
                Assert.AreEqual(singleton.Advance(3).Consumed,prepared.Advance(3).Consumed);Assert.AreEqual(before,singletonCow.StateCount);singletonCow=prepared;
                Assert.AreEqual(singleton.StateCount,singletonCow.StateCount);Assert.AreEqual(singleton.Status,singletonCow.Status);
            }
            Assert.AreEqual(NavSolveStatus.Ready,singleton.Status);CollectionAssert.AreEqual(singleton.Route.Legs.Select(l=>l.To),singletonCow.Route.Legs.Select(l=>l.To));
            var shared=new NavIncrementalSharedRoute(field,subscription);var sharedCow=new NavIncrementalSharedRoute(field,subscription);
            var sharedFork=typeof(NavIncrementalSharedRoute).GetMethod("ForkPrepared",flags);
            while(shared.Status==NavSolveStatus.Pending){
                Seal(sharedCow);int before=sharedCow.StateCount;var prepared=(NavIncrementalSharedRoute)sharedFork.Invoke(sharedCow,new object[]{field,reserve});
                Assert.AreEqual(shared.Advance(3).Consumed,prepared.Advance(3).Consumed);Assert.AreEqual(before,sharedCow.StateCount);sharedCow=prepared;
                Assert.AreEqual(shared.StateCount,sharedCow.StateCount);Assert.AreEqual(shared.Status,sharedCow.Status);
            }
            Assert.AreEqual(NavSolveStatus.Ready,shared.Status);CollectionAssert.AreEqual(shared.Route.Legs.Select(l=>l.To),sharedCow.Route.Legs.Select(l=>l.To));
        }
        [Test] public void AliasCatalogKeepsEqualBindingsPhysicalAndSeparatesIntentFromAssignedRole()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));
            var aliases=NavigationCheckpointAliases.Create(scope);scope.Attach(aliases);
            var geometry=new NavGeometry(4,Array.Empty<NavObstacle>(),1);var first=LayeredNavigationProvider.CopyOwned(NavigationAdmission.CertifiedFlatProvider(geometry),scope.OwnedReservation());var second=LayeredNavigationProvider.CopyOwned(NavigationAdmission.CertifiedFlatProvider(geometry),scope.OwnedReservation());
            Assert.AreEqual(first.Binding,second.Binding);Assert.AreNotSame(first,second);
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(NavigationArrivalTestFactory.Sha("alias-graph"),"distance",1,2);
            var limits=new NavGraphCompileLimits(256,256,1024);
            LayeredNavigationCompileWork Compile(ILayeredNavigationProvider provider){var work=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,clearance,profile,limits,scope.OwnedReservation());while(work.Phase!=LayeredNavigationCompileWork.Stage.Ready)work.Advance(100);work.SealPrepared();return work;}
            var compilerA=Compile(first);var compilerB=Compile(second);var graphA=compilerA.Graph;var graphB=compilerB.Graph;
            Assert.AreEqual(0,aliases.GraphId(graphA));Assert.AreEqual(1,aliases.GraphId(graphB));Assert.AreEqual(0,aliases.GraphId(graphA));Assert.AreEqual(2,aliases.Providers.Count);
            var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);var region=NavTerminalRegion.CopyOwned(new NavTerminalRegion("alias-goal",1,at,new[]{at}),scope.OwnedReservation());
            var fieldA=NavSharedTerminalField.CreateOwned(graphA,region,256,scope.OwnedReservation());var fieldB=NavSharedTerminalField.CreateOwned(graphA,region,256,scope.OwnedReservation());fieldA.SealPrepared();fieldB.SealPrepared();
            Assert.AreEqual(fieldA.StableKey,fieldB.StableKey);Assert.AreEqual(0,aliases.FieldId(fieldA));Assert.AreEqual(1,aliases.FieldId(fieldB));Assert.AreEqual(1,aliases.Regions.Count);region.StorageRoot.ReleaseOwner();fieldA.ReleaseStorage();fieldB.ReleaseStorage();compilerA.ReleaseStorage();compilerB.ReleaseStorage();first.StorageRoot.Release();second.StorageRoot.Release();
            var navigation=new NavigationProfile();var member=new NavigationMemberIdentity(1,0,1,1,1);
            var intentGoal=new NavLocation(new NavPoint(1,1),NavLocation.FlatSurface);var endpoint=new NavLocation(new NavPoint(2,1),NavLocation.FlatSurface);
            var intent=new NavigationRequest(1,1,0,1,navigation,geometry,at.Position,intentGoal.Position,geometry,null,member,at,intentGoal);
            var assigned=new NavigationRequest(1,1,0,1,navigation,geometry,at.Position,endpoint.Position,geometry,null,member,at,endpoint);
            Assert.AreEqual(0,aliases.Requests.Include(intent,NavigationCheckpointRequestRole.ArrivalIntent));
            Assert.AreEqual(1,aliases.Requests.Include(assigned,NavigationCheckpointRequestRole.Canonical|NavigationCheckpointRequestRole.AssignedEndpoint));
            Assert.AreEqual(1,aliases.Requests.Include(assigned,NavigationCheckpointRequestRole.Canonical));
            Assert.AreSame(intent,aliases.Requests.Physical.Resolve(0));Assert.AreSame(assigned,aliases.Requests.Physical.Resolve(1));
            Assert.AreEqual(NavigationCheckpointRequestRole.ArrivalIntent,aliases.Requests.Role(0));
            Assert.AreEqual(NavigationCheckpointRequestRole.Canonical|NavigationCheckpointRequestRole.AssignedEndpoint,aliases.Requests.Role(1));
            Assert.LessOrEqual(aliases.RetainedMetadataBytes,scope.HeldBytes);
            aliases.Freeze();Assert.Throws<InvalidOperationException>(()=>aliases.Providers.Include(NavigationAdmission.CertifiedFlatProvider(geometry)));
            Assert.Throws<ArgumentException>(()=>aliases.Fields.Resolve(2));Assert.Throws<ArgumentException>(()=>aliases.Providers.Resolve(-1));
            aliases=null;scope.Discard();Assert.Null(scope.Prepared);Assert.AreEqual(0,budget.HeldBytes);Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [Test] public void AliasRolesRejectDuplicateCanonicalObjectsAndInvalidPhysicalRebind()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));
            var table=NavigationRequestAliases.Create(scope);scope.Attach(table);var geometry=new NavGeometry(4,Array.Empty<NavObstacle>(),1);var profile=new NavigationProfile();
            var first=new NavigationRequest(1,1,0,1,profile,geometry,new NavPoint(0,0),new NavPoint(1,0));
            var duplicate=new NavigationRequest(1,1,0,1,profile,geometry,new NavPoint(0,0),new NavPoint(1,0));
            Assert.AreEqual(0,table.Include(first,NavigationCheckpointRequestRole.Canonical));
            Assert.Throws<ArgumentException>(()=>table.Include(duplicate,NavigationCheckpointRequestRole.Canonical));
            Assert.Throws<ArgumentException>(()=>table.Freeze());table=null;scope.Discard();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var coldScope));
            var cold=NavigationReferenceTable<NavigationRequest>.Create(coldScope);coldScope.Attach(cold);cold.BindRestored(0,first);
            Assert.Throws<ArgumentException>(()=>cold.BindRestored(1,first));Assert.Throws<ArgumentException>(()=>cold.BindRestored(3,duplicate));
            cold=null;coldScope.Discard();Assert.AreEqual(2,budget.ResolvedScopes);
        }
        [Test] public void PrimitiveSchedulerAliasCodecRestoresDistinctProviderInstancesWithoutBindingFallback()
        {
            var geometry=new NavGeometry(4,Array.Empty<NavObstacle>(),1);var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);
            var profile=new NavGraphProfile(NavigationArrivalTestFactory.Sha("alias-schedule"),"distance",1,2);var limits=new NavGraphCompileLimits(256,256,1024);
            var origin=new NavLocation(new NavPoint(-2,0),NavLocation.FlatSurface);var goal=new NavLocation(new NavPoint(2,0),NavLocation.FlatSurface);
            var region=new NavTerminalRegion("alias-schedule",1,goal,new[]{goal});
            NavScheduledRequest Recipe(ILayeredNavigationProvider provider,NavTerminalRegion target,int entity)=>new NavScheduledRequest(provider,clearance,profile,limits,target,
                new NavRouteSubscription(1,1,1,1,0,1,entity,1,1,0,entity+1,origin,goal,null),256);
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));
            var firstProvider=LayeredNavigationProvider.CopyOwned(NavigationAdmission.CertifiedFlatProvider(geometry),scope.OwnedReservation());var secondProvider=LayeredNavigationProvider.CopyOwned(NavigationAdmission.CertifiedFlatProvider(geometry),scope.OwnedReservation());
            var ownedRegion=NavTerminalRegion.CopyOwned(region,scope.OwnedReservation());
            var first=NavScheduledRequest.CopyOwned(Recipe(firstProvider,region,0),firstProvider,scope.OwnedReservation(),ownedRegion);var second=NavScheduledRequest.CopyOwned(Recipe(secondProvider,region,1),secondProvider,scope.OwnedReservation(),ownedRegion);ownedRegion.StorageRoot.ReleaseOwner();
            var scheduler=new LayeredNavigationScheduler();Assert.True(scheduler.Submit(first));Assert.True(scheduler.Submit(second));scheduler.Advance(0,new NavEpochQuota(32,32,32),_=>true,2);
            var aliases=NavigationCheckpointAliases.Create(scope);scope.Attach(aliases);
            aliases.ScheduledId(first);aliases.ScheduledId(second);first.StorageRoot.ReleaseOwner();second.StorageRoot.ReleaseOwner();firstProvider.StorageRoot.Release();secondProvider.StorageRoot.Release();aliases.Freeze();Assert.AreEqual(2,aliases.Providers.Count);
            byte[] bytes;
            using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,aliases).Scheduler(scheduler.Save());bytes=stream.ToArray();}
            var coldGeometry=new NavGeometry(4,Array.Empty<NavObstacle>(),1);var coldRegion=new NavTerminalRegion("alias-schedule",1,goal,new[]{goal});
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var coldScope));
            var coldFirstProvider=LayeredNavigationProvider.CopyOwned(NavigationAdmission.CertifiedFlatProvider(coldGeometry),coldScope.OwnedReservation());var coldSecondProvider=LayeredNavigationProvider.CopyOwned(NavigationAdmission.CertifiedFlatProvider(coldGeometry),coldScope.OwnedReservation());
            var coldOwnedRegion=NavTerminalRegion.CopyOwned(coldRegion,coldScope.OwnedReservation());
            var coldFirst=NavScheduledRequest.CopyOwned(Recipe(coldFirstProvider,coldRegion,0),coldFirstProvider,coldScope.OwnedReservation(),coldOwnedRegion);var coldSecond=NavScheduledRequest.CopyOwned(Recipe(coldSecondProvider,coldRegion,1),coldSecondProvider,coldScope.OwnedReservation(),coldOwnedRegion);coldOwnedRegion.StorageRoot.ReleaseOwner();
            var coldAliases=NavigationCheckpointAliases.Create(coldScope);coldScope.Attach(coldAliases);
            coldAliases.ScheduledId(coldFirst);coldAliases.ScheduledId(coldSecond);coldFirst.StorageRoot.ReleaseOwner();coldSecond.StorageRoot.ReleaseOwner();coldFirstProvider.StorageRoot.Release();coldSecondProvider.StorageRoot.Release();coldAliases.Freeze();Assert.True(coldScope.TryReserve(bytes.Length));
            LayeredNavigationScheduler restored;
            using(var stream=new MemoryStream(bytes))using(var reader=new BinaryReader(stream)){
                var state=new NavigationPrimitiveWire.Reader(reader,coldAliases,coldScope).Scheduler();Assert.AreEqual(stream.Length,stream.Position);restored=LayeredNavigationScheduler.Restore(state,new[]{coldFirst,coldSecond});
            }
            Assert.AreNotSame(coldAliases.Providers.Resolve(0),coldAliases.Providers.Resolve(1));Assert.AreNotSame(first.Provider,coldAliases.Providers.Resolve(0));
            using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,coldAliases).Scheduler(restored.Save());CollectionAssert.AreEqual(bytes,stream.ToArray());}
            var completion=new NavScheduledCompletion(first,NavSolveStatus.Stale,null,0L,0L);
            Assert.AreEqual(0,aliases.CompletionRecipeId(completion));Assert.Throws<ArgumentException>(()=>coldAliases.CompletionRecipeId(completion));completion.Release();
            // Independent captures own their sealed roots after the operation
            // scopes discard. Pins retain data, never the live allocation callback.
            aliases.Retain();coldAliases.Retain();scope.Discard();coldScope.Discard();Assert.AreEqual(0,budget.HeldBytes);Assert.Greater(budget.PhysicalBytes,0);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            Assert.Null(typeof(NavigationCheckpointAliases).GetField("scope",flags).GetValue(aliases));
            Assert.Null(typeof(NavigationRequestAliases).GetField("scope",flags).GetValue(aliases.Requests));
            Assert.Null(typeof(NavigationReferenceTable<ILayeredNavigationProvider>).GetField("scope",flags).GetValue(aliases.Providers));
            Assert.AreEqual(0,aliases.ScheduledId(first));Assert.AreSame(coldFirst,coldAliases.Scheduled.Resolve(0));
            using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,coldAliases).Scheduler(restored.Save());CollectionAssert.AreEqual(bytes,stream.ToArray());}
            scheduler.ClearWorld();restored.ClearWorld();aliases.Release();coldAliases.Release();Assert.AreEqual(0,budget.PhysicalBytes);aliases=null;coldAliases=null;
        }
        [Test] public void PreparedReservationScopeDiscardsEveryRefusedCompilerPrefixExactlyOnce()
        {
            var provider=NavigationArrivalTestFactory.Layered();var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);
            var profile=new NavGraphProfile(NavigationArrivalTestFactory.Sha("scope-refusal"),"distance",1,4);
            var limits=new NavGraphCompileLimits(1024,256,4096);var source=new LayeredNavigationCompileWork(provider,clearance,profile,limits);source.Advance(12);Seal(source);
            byte[] Bytes(){using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(source.Save());return stream.ToArray();}}
            var prefix=Bytes();long committed=source.EstimatedRetainedBytes;var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var fork=typeof(LayeredNavigationCompileWork).GetMethod("ForkPrepared",flags);int refused=0,reservations=0;
            var successfulBudget=new NavigationRetentionBudget(committed);Assert.True(successfulBudget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var successfulScope));
            var successful=(LayeredNavigationCompileWork)fork.Invoke(source,new object[]{new Func<long,bool>(n=>{reservations++;return successfulScope.TryReserve(n);})});
            successfulScope.Attach(successful);successful.Advance(16);successful=null;successfulScope.Discard();Assert.Greater(reservations,0);
            for(int nth=1;nth<=reservations;nth++){
                var budget=new NavigationRetentionBudget(committed);long baseCharge=budget.RetainedBytes;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));int calls=0;
                LayeredNavigationCompileWork candidate=null;
                try{
                    candidate=(LayeredNavigationCompileWork)fork.Invoke(source,new object[]{new Func<long,bool>(n=>{calls++;return calls!=nth&&scope.TryReserve(n);})});
                    scope.Attach(candidate);candidate.Advance(16);Assert.Fail("Nth callback refusal was not reached.");
                }catch(System.Reflection.TargetInvocationException e){Assert.IsInstanceOf<InvalidOperationException>(e.InnerException);refused++;}
                catch(InvalidOperationException){refused++;}
                finally{
                    // Refused objects may have mutated; they are never resumed or
                    // published. Clear all owner references before resolving.
                    candidate=null;Assert.True(scope.Discard());Assert.False(scope.Discard());
                }
                Assert.Null(scope.Prepared);Assert.True(scope.IsResolved);Assert.AreEqual(0,scope.HeldBytes);
                Assert.AreEqual(1,budget.ResolvedScopes);Assert.AreEqual(0,budget.ActiveScopes);Assert.AreEqual(baseCharge,budget.RetainedBytes);
                CollectionAssert.AreEqual(prefix,Bytes());Assert.AreEqual(committed,source.EstimatedRetainedBytes);
            }
            Assert.AreEqual(reservations,refused);
        }
        [Test] public void DecodeAndReconstructionUseOneBudgetAndRejectBeforeStringAllocation()
        {
            var bytes=System.Text.Encoding.UTF8.GetBytes(new string('x',80));
            using(var stream=new MemoryStream()){
                using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){writer.Write(bytes.Length);writer.Write(bytes);}
                stream.Position=0;
                long limit=LayeredNavigationScheduler.MaxRetainedBytes;
                var budget=new NavigationRetentionBudget(limit-NavigationRetentionBudget.RootBytes-NavigationRetentionBudget.ScopeBytes-stream.Length-32);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));Assert.True(scope.TryReserve(stream.Length));
                using(var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true)){
                    var codec=new NavigationPrimitiveWire.Reader(reader,_=>null,_=>null,scope);
                    var method=typeof(NavigationPrimitiveWire.Reader).GetMethod("ReadString",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                    var error=Assert.Throws<System.Reflection.TargetInvocationException>(()=>method.Invoke(codec,null));Assert.IsInstanceOf<ArgumentException>(error.InnerException);
                    Assert.AreEqual(sizeof(int),stream.Position,"String payload must not be read or allocated after admission refusal.");
                    Assert.False(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var reconstruction));Assert.Null(reconstruction);
                    Assert.AreEqual(1,budget.ActiveScopes);
                }
                scope.Dispose();Assert.AreEqual(0,budget.HeldBytes);Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(1,budget.ResolvedScopes);
            }
            using(var stream=new MemoryStream())using(var reader=new BinaryReader(stream))
                Assert.Throws<ArgumentException>(()=>new NavigationPrimitiveWire.Reader(reader,_=>null,_=>null,-1L));
        }
        [Test] public void PreparedScopePublicationConservesOtherStagedOwners()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var solve));
            Assert.True(solve.TryReserve(4096));object candidate=new object(),published=null;solve.Attach(candidate);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));Assert.True(decode.TryReserve(512));
            long other=decode.HeldBytes;int publications=0;
            solve.Commit(candidate,4096,value=>{published=value;publications++;});
            Assert.AreSame(candidate,published);Assert.Null(solve.Prepared);Assert.AreEqual(1,publications);
            Assert.AreEqual(4096+NavigationRetentionBudget.RootBytes+other,budget.RetainedBytes);
            Assert.Throws<InvalidOperationException>(()=>solve.Commit(candidate,4096,_=>publications++));Assert.AreEqual(1,publications);
            Assert.False(solve.Discard());decode.Discard();Assert.AreEqual(0,budget.HeldBytes);Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(2,budget.ResolvedScopes);
            Assert.AreEqual(4096,budget.CommittedBytes);
        }
        [Test] public void CancellationAndOwnerHandoffClearPreparedRootBeforeResolution()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,2,3,out var scope));
            Assert.True(scope.TryReserve(4096));scope.Attach(new object());
            var cancel=new System.Threading.Thread(scope.RequestCancel);cancel.Start();cancel.Join();
            Assert.False(scope.TryReserve(1));Assert.Throws<OperationCanceledException>(()=>scope.Commit(scope.Prepared,1024,_=>Assert.Fail("Cancelled publication")));
            Assert.NotNull(scope.Prepared);scope.RelinquishOwner();bool acknowledged=false;
            Exception ownerError=null;
            var owner=new System.Threading.Thread(()=>{try{scope.AcquireOwner();Assert.Throws<InvalidOperationException>(()=>scope.AcquireOwner());scope.Discard();acknowledged=scope.Prepared==null&&scope.HeldBytes==0&&scope.IsResolved;}catch(Exception error){ownerError=error;}});
            owner.Start();owner.Join();Assert.Null(ownerError);Assert.True(acknowledged);Assert.False(scope.Discard());Assert.AreEqual(1,budget.ResolvedScopes);
        }
        [Test] public void StructuralChargesCoverCompilerNodesAndFirstLegacyPages()
        {
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);
            var profile=new NavGraphProfile(NavigationArrivalTestFactory.Sha("structural-charge"),"distance",1,1);
            var limits=new NavGraphCompileLimits(1024,256,4096);
            var compiler=new LayeredNavigationCompileWork(NavigationArrivalTestFactory.Layered(),clearance,profile,limits);
            while(compiler.SampleCount<256){Assert.AreEqual(1,compiler.Advance(1));Assert.LessOrEqual(compiler.Consumed,1024);}
            Assert.AreEqual(0,compiler.ArcCount);Assert.AreEqual(LayeredNavigationCompileWork.Stage.Samples,compiler.Phase);
            Assert.Greater(compiler.EstimatedRetainedBytes,128L*256,"Grid nodes alone cost 32768; pages and other roots are additional.");
            var coldCompiler=LayeredNavigationCompileWork.Restore(NavigationArrivalTestFactory.Layered(),clearance,profile,limits,Roundtrip(c=>c.Compile(compiler.Save()),c=>c.Compile()));
            Assert.AreEqual(compiler.EstimatedRetainedBytes,coldCompiler.EstimatedRetainedBytes);
            var nodeProfile=new NavGraphProfile(NavigationArrivalTestFactory.Sha("node-page-charge"),"distance",1,8);
            var nodeCompiler=new LayeredNavigationCompileWork(NavigationArrivalTestFactory.Layered(),clearance,nodeProfile,limits);
            while(nodeCompiler.Phase!=LayeredNavigationCompileWork.Stage.Nodes){Assert.AreEqual(1,nodeCompiler.Advance(1));Assert.Less(nodeCompiler.Consumed,512);}
            long beforeNodePage=nodeCompiler.EstimatedRetainedBytes;
            long predicted=(long)typeof(LayeredNavigationCompileWork).GetProperty("NextGrowthBytes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(nodeCompiler);
            nodeCompiler.Advance(1);
            long nodePageDelta=nodeCompiler.EstimatedRetainedBytes-beforeNodePage;
            Assert.GreaterOrEqual(nodePageDelta,64L*256,"NavGraphNode is a value struct; charge every allocated page slot.");
            Assert.GreaterOrEqual(predicted,nodePageDelta);
            var nodeCold=LayeredNavigationCompileWork.Restore(NavigationArrivalTestFactory.Layered(),clearance,nodeProfile,limits,Roundtrip(c=>c.Compile(nodeCompiler.Save()),c=>c.Compile()));
            Assert.AreEqual(nodeCompiler.EstimatedRetainedBytes,nodeCold.EstimatedRetainedBytes);

            // Two actual qualified portal nodes give exactly two distance labels,
            // one successor and one pending frontier entry after relaxation.
            var lower=NavigationArrivalTestFactory.At(0,0);var upper=NavigationArrivalTestFactory.At(0,0,"upper");
            var portal=new NavLayerPortal("charge-lift",lower,upper,NavPortalDirection.Forward,true,1,NavigationArrivalTestFactory.Area(),new[]{"ground"});
            var provider=NavigationArrivalTestFactory.Layered(portals:new[]{portal});
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var nodeCtor=typeof(NavGraphNode).GetConstructor(flags,null,new[]{typeof(int),typeof(NavLocation),typeof(int)},null);
            var arcCtor=typeof(NavGraphArc).GetConstructors(flags).Single();
            var nodes=new[]{(NavGraphNode)nodeCtor.Invoke(new object[]{0,lower,0}),(NavGraphNode)nodeCtor.Invoke(new object[]{1,upper,0})};
            var arc=(NavGraphArc)arcCtor.Invoke(new object[]{0,1,1d,NavGraphArcKind.Portal,portal.Id,null});
            var graphCtor=typeof(LayeredNavigationGraph).GetConstructor(flags,null,new[]{typeof(ILayeredNavigationProvider),typeof(NavGraphBinding),typeof(NavGraphNode[]),typeof(NavGraphArc[]),typeof(NavConnectorResult[])},null);
            var graph=(LayeredNavigationGraph)graphCtor.Invoke(new object[]{provider,typeof(NavGraphBinding).GetConstructor(flags,null,new[]{typeof(string),typeof(NavClearanceProfile),typeof(NavGraphProfile)},null).Invoke(new object[]{provider.Binding,clearance,profile}),nodes,new[]{arc},new[]{provider.QualifyPortal(portal.Id,clearance)}});
            var region=new NavTerminalRegion("charge-goal",1,upper,new[]{upper});var field=new NavSharedTerminalField(graph,region,16);
            var nextField=typeof(NavSharedTerminalField.Checkpoint).GetField("Next",flags);
            int units=0;while(((Array)nextField.GetValue(field.Save())).Length==0){Assert.AreEqual(1,field.Advance(1).Consumed);Assert.Less(++units,32);}
            Assert.AreEqual(2,field.LabelCount);Assert.AreEqual(1,field.FrontierCount);
            Assert.Greater(field.EstimatedRetainedBytes,4096,"Two allocated long[256] pages alone cost 4096.");
            var coldField=NavSharedTerminalField.Restore(graph,region,16,Roundtrip(c=>c.Field(field.Save()),c=>c.Field()));
            Assert.AreEqual(field.EstimatedRetainedBytes,coldField.EstimatedRetainedBytes);
            while(field.Status==NavSolveStatus.Pending)field.Advance(1);
            var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,lower,upper,null);
            var connector=new NavIncrementalSharedRoute(field,subscription);units=0;
            while(connector.LegCount==0){Assert.AreEqual(1,connector.Advance(1).Consumed);Assert.Less(++units,32);}
            Assert.AreEqual(3,connector.StateCount);
            long Charge(NavIncrementalSharedRoute value)=>(long)typeof(NavIncrementalSharedRoute).GetProperty("EstimatedRetainedBytes",flags).GetValue(value);
            Assert.Greater(Charge(connector),4096,"Ingress/egress int pages and leg reference page alone cost 4096.");
            var coldConnector=NavIncrementalSharedRoute.Restore(field,subscription,Roundtrip(c=>c.Shared(connector.Save()),c=>c.Shared()));
            Assert.AreEqual(Charge(connector),Charge(coldConnector));
        }
        [Test] public void FlatSurfacePrefixColdRestorePreservesNextPreparedReservations()
        {
            // Supplying even an empty portal list creates both authored surfaces.
            var provider=NavigationArrivalTestFactory.Layered(portals:Array.Empty<NavLayerPortal>());
            Assert.AreEqual(2,provider.Surfaces.Count);
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);
            var profile=new NavGraphProfile(NavigationArrivalTestFactory.Sha("surface-prefix"),"distance",1,8);
            var limits=new NavGraphCompileLimits(128,128,1024);
            var work=new LayeredNavigationCompileWork(provider,clearance,profile,limits);
            byte[] Bytes(LayeredNavigationCompileWork value){using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(value.Save());return stream.ToArray();}}
            var fork=typeof(LayeredNavigationCompileWork).GetMethod("ForkPrepared",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            int captures=0;
            do {
                var checkpoint=Roundtrip(c=>c.Compile(work.Save()),c=>c.Compile());
                var cold=LayeredNavigationCompileWork.Restore(provider,clearance,profile,limits,checkpoint);
                CollectionAssert.AreEqual(Bytes(work),Bytes(cold));
                Assert.AreEqual(work.EstimatedRetainedBytes,cold.EstimatedRetainedBytes);
                Seal(work);Seal(cold);
                var hotReservations=new List<long>();var coldReservations=new List<long>();
                var hot=(LayeredNavigationCompileWork)fork.Invoke(work,new object[]{new Func<long,bool>(n=>{hotReservations.Add(n);return true;})});
                var restored=(LayeredNavigationCompileWork)fork.Invoke(cold,new object[]{new Func<long,bool>(n=>{coldReservations.Add(n);return true;})});
                Assert.AreEqual(hot.Advance(1),restored.Advance(1));
                CollectionAssert.AreEqual(hotReservations,coldReservations,"capture="+captures+" phase="+checkpoint.Phase);
                CollectionAssert.AreEqual(Bytes(hot),Bytes(restored));
                Assert.AreEqual(hot.EstimatedRetainedBytes,restored.EstimatedRetainedBytes);
                work=hot;captures++;
            } while(work.Phase<=LayeredNavigationCompileWork.Stage.Samples);
            Assert.Greater(captures,4);
        }
        [TestCase(3)][TestCase(5)][TestCase(9)] public void PackedDirectoryCapacitySurvivesForkColdRestoreAndGrowth(int pages)
        {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var type=typeof(LayeredNavigationCompileWork).Assembly.GetType("Spacewars.Simulation.NavPackedSlots");var source=Activator.CreateInstance(type,true);
            var ensure=type.GetMethod("Ensure",flags);var seal=type.GetMethod("Seal",flags);var save=type.GetMethod("Save",flags);var fork=type.GetMethod("Fork",flags);
            var capacity=type.GetProperty("DirectoryCapacity",flags);var retained=type.GetProperty("EstimatedRetainedBytes",flags);
            for(int n=1;n<=pages;n++)ensure.Invoke(source,new object[]{n*4096});seal.Invoke(source,null);
            int expected=(int)capacity.GetValue(source);Assert.AreEqual(pages==3?4:pages==5?8:16,expected);long before=(long)retained.GetValue(source),reserved=0;
            Func<long,bool> reserve=n=>{reserved+=n;return true;};var prepared=fork.Invoke(source,new object[]{reserve});
            Assert.AreEqual(expected,capacity.GetValue(prepared));Assert.AreEqual(before,retained.GetValue(source));
            var restored=type.GetMethod("Restore",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{save.Invoke(source,null),65536,expected});
            Assert.AreEqual(expected,capacity.GetValue(restored));Assert.AreEqual(before,retained.GetValue(restored));
            for(int n=pages+1;n<=expected;n++)ensure.Invoke(prepared,new object[]{n*4096});
            long beforeGrowth=reserved;ensure.Invoke(prepared,new object[]{(expected+1)*4096});
            Assert.AreEqual(expected*2,capacity.GetValue(prepared));Assert.AreEqual(before,retained.GetValue(source));Assert.AreEqual(expected,capacity.GetValue(source));
            Assert.AreEqual(NavStorageLayout.Charge(96+4*4096),reserved-beforeGrowth,"Private fixed-fanout path is retained; reserve the complete intrusive page wrapper and array before growth.");
            var coldGrowth=type.GetMethod("Restore",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{save.Invoke(source,null),65536,expected});seal.Invoke(coldGrowth,null);
            long coldReserved=0;var coldPrepared=fork.Invoke(coldGrowth,new object[]{new Func<long,bool>(n=>{coldReserved+=n;return true;})});
            for(int n=pages+1;n<=expected+1;n++)ensure.Invoke(coldPrepared,new object[]{n*4096});Assert.AreEqual(reserved,coldReserved);Assert.AreEqual(retained.GetValue(prepared),retained.GetValue(coldPrepared));
            bool refuseGrowth=false;var refusal=fork.Invoke(source,new object[]{new Func<long,bool>(n=>!refuseGrowth||n!=NavStorageLayout.Charge(96+4L*4096))});for(int n=pages+1;n<=expected;n++)ensure.Invoke(refusal,new object[]{n*4096});refuseGrowth=true;
            var error=Assert.Throws<System.Reflection.TargetInvocationException>(()=>ensure.Invoke(refusal,new object[]{(expected+1)*4096}));Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);Assert.AreEqual(before,retained.GetValue(source));
        }
        [Test] public void EveryNthPreparedReservationRefusalKeepsCommittedPayloadAndChargeStable()
        {
            var map=ThreeCrossingsMap.Default;var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.58);var profile=new NavGraphProfile(NavigationArrivalTestFactory.Sha("nth-reservation"),"distance",1,.8);
            var work=new LayeredNavigationCompileWork(provider,clearance,profile,new NavGraphCompileLimits(300000,150000,600000));work.Advance(2048);Seal(work);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var fork=typeof(LayeredNavigationCompileWork).GetMethod("ForkPrepared",flags);
            byte[] Bytes(){using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(work.Save());return stream.ToArray();}}
            var before=Bytes();long charge=work.EstimatedRetainedBytes;int total=0;Func<long,bool> accept=_=>{total++;return true;};
            var first=(LayeredNavigationCompileWork)fork.Invoke(work,new object[]{accept});first.Advance(1);Assert.Greater(total,8);
            for(int failure=1;failure<=total;failure++){
                int calls=0;Func<long,bool> reserve=n=>++calls!=failure;
                try{var rejected=(LayeredNavigationCompileWork)fork.Invoke(work,new object[]{reserve});Assert.Throws<InvalidOperationException>(()=>rejected.Advance(1));}
                catch(System.Reflection.TargetInvocationException e){Assert.IsInstanceOf<InvalidOperationException>(e.InnerException);}
                Assert.AreEqual(failure,calls);CollectionAssert.AreEqual(before,Bytes());Assert.AreEqual(charge,work.EstimatedRetainedBytes);
            }
            var next=(LayeredNavigationCompileWork)fork.Invoke(work,new object[]{new Func<long,bool>(_=>true)});Assert.AreEqual(1,next.Advance(1));CollectionAssert.AreEqual(before,Bytes());
        }
        [Test] public void RawHeapPermutationAndInitialRootCapPredictorKeepExactNextCowReservationSequence()
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,Array.Empty<NavObstacle>(),1));
            var graph=LayeredNavigationGraphCompiler.Compile(provider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('a',64),"distance",1,1),new NavGraphCompileLimits(2000,2000,10000));
            var candidates=Enumerable.Range(0,8).Select(n=>new NavLocation(new NavPoint(-2+n%4,-1+n/4),NavLocation.FlatSurface)).ToArray();var region=new NavTerminalRegion("raw-heap-cow",1,candidates[0],candidates);
            var field=new NavSharedTerminalField(graph,region,2048);var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            long predicted=(long)typeof(NavSharedTerminalField).GetMethod("InitialStorageBytes",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{graph,region,2048});
            Assert.AreEqual(predicted,field.EstimatedRetainedBytes,"Initial admission must include directoryOwners before allocating any storage.");
            var heapMember=typeof(NavSharedTerminalField.Checkpoint).GetField("FrontierHeap",flags);var canonicalMember=typeof(NavSharedTerminalField.Checkpoint).GetField("Frontier",flags);bool unsorted=false;
            for(int n=0;n<2000&&field.Status==NavSolveStatus.Pending;n++){
                field.Advance(1);var saved=field.Save();var raw=(System.Array)heapMember.GetValue(saved);var sorted=(System.Array)canonicalMember.GetValue(saved);
                if(raw.Length>256&&((int[])saved.GetType().GetField("FrontierPositionPages",flags).GetValue(saved)).Length>1&&!raw.Cast<object>().SequenceEqual(sorted.Cast<object>())){unsorted=true;break;}
            }
            Assert.True(unsorted,"Exercise an actual non-sorted physical heap across heap and position pages.");Seal(field);
            var cold=NavSharedTerminalField.Restore(graph,region,2048,Roundtrip(c=>c.Field(field.Save()),c=>c.Field()));Seal(cold);
            CollectionAssert.AreEqual((System.Array)heapMember.GetValue(field.Save()),(System.Array)heapMember.GetValue(cold.Save()));
            var left=new System.Collections.Generic.List<long>();var right=new System.Collections.Generic.List<long>();var fork=typeof(NavSharedTerminalField).GetMethod("ForkPrepared",flags);
            var a=(NavSharedTerminalField)fork.Invoke(field,new object[]{new Func<long,bool>(n=>{left.Add(n);return true;})});
            var b=(NavSharedTerminalField)fork.Invoke(cold,new object[]{new Func<long,bool>(n=>{right.Add(n);return true;})});Assert.AreEqual(a.Advance(1).Consumed,b.Advance(1).Consumed);CollectionAssert.AreEqual(left,right);
            CollectionAssert.AreEqual((System.Array)heapMember.GetValue(a.Save()),(System.Array)heapMember.GetValue(b.Save()));
        }
        [Test] public void PrimitiveAllocatorPreparationResumesRemainingCandidateWork()
        {
            var provider=NavigationArrivalTestFactory.Layered();
            var candidates=Enumerable.Range(0,64).Select(n=>NavigationArrivalTestFactory.At(n%8-4,n/8-4)).ToArray();
            var input=NavigationArrivalTestFactory.Input(provider,candidates,
                new[]{NavigationArrivalTestFactory.Member(1,0,NavigationArrivalTestFactory.At(-8,0))},NavigationArrivalTestFactory.Occupancy());
            var original=new NavigationArrivalWork(input);var restored=new NavigationArrivalWork(input);var quota=new NavEpochQuota(0,0,0,0,0);
            for(int epoch=0;epoch<40;epoch++){
                Assert.AreEqual(original.Advance(epoch,1,quota),restored.Advance(epoch,1,quota));
                var saved=restored.Save();restored=NavigationArrivalWork.Restore(input,Roundtrip(c=>c.Arrival(saved),c=>c.Arrival()));
                Assert.AreEqual(original.WorkPhase,restored.WorkPhase);Assert.AreEqual(original.CandidateCursor,restored.CandidateCursor);
                Assert.AreEqual(original.AllocationWorkConsumed,restored.AllocationWorkConsumed);Assert.AreEqual(original.LogicalEpoch,restored.LogicalEpoch);
            }
        }
        [Test] public void PrimitiveCompilerFieldSharedConnectorAndSingletonPreservePartialConsumption()
        {
            var provider=NavigationArrivalTestFactory.Layered(groundBlockers:new[]{new NavObstacle(-1,-1,1,1)});
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);
            var profile=new NavGraphProfile(NavigationArrivalTestFactory.Sha("primitive-work"),"distance",1,1);
            var limits=new NavGraphCompileLimits(12000,8000,50000);
            var original=new LayeredNavigationCompileWork(provider,clearance,profile,limits);var restored=new LayeredNavigationCompileWork(provider,clearance,profile,limits);
            while(original.Phase!=LayeredNavigationCompileWork.Stage.Ready){
                Assert.AreEqual(original.Advance(17),restored.Advance(17));
                var saved=restored.Save();restored=LayeredNavigationCompileWork.Restore(provider,clearance,profile,limits,Roundtrip(c=>c.Compile(saved),c=>c.Compile()));
                Assert.AreEqual(original.Phase,restored.Phase);Assert.AreEqual(original.Consumed,restored.Consumed);
            }
            var goal=NavigationArrivalTestFactory.At(5,0);var region=new NavTerminalRegion("wire-region",1,goal,new[]{goal});
            var field=new NavSharedTerminalField(original.Graph,region,4096);var coldField=new NavSharedTerminalField(restored.Graph,region,4096);
            while(field.Status==NavSolveStatus.Pending){
                Assert.AreEqual(field.Advance(13).Consumed,coldField.Advance(13).Consumed);
                var saved=coldField.Save();coldField=NavSharedTerminalField.Restore(restored.Graph,region,4096,Roundtrip(c=>c.Field(saved),c=>c.Field()));
                Assert.AreEqual(field.Status,coldField.Status);Assert.AreEqual(field.LabelCount,coldField.LabelCount);Assert.AreEqual(field.FrontierCount,coldField.FrontierCount);
            }
            var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,NavigationArrivalTestFactory.At(-5,0),goal,null);
            var shared=new NavIncrementalSharedRoute(field,subscription);var coldShared=new NavIncrementalSharedRoute(coldField,subscription);
            while(shared.Status==NavSolveStatus.Pending){
                Assert.AreEqual(shared.Advance(7).Consumed,coldShared.Advance(7).Consumed);
                var saved=coldShared.Save();coldShared=NavIncrementalSharedRoute.Restore(coldField,subscription,Roundtrip(c=>c.Shared(saved),c=>c.Shared()));
                Assert.AreEqual(shared.Status,coldShared.Status);
            }
            Assert.AreEqual(NavSolveStatus.Ready,shared.Status);Assert.AreEqual(shared.Route.Cost,coldShared.Route.Cost);
            CollectionAssert.AreEqual(shared.Route.Legs.Select(l=>l.To),coldShared.Route.Legs.Select(l=>l.To));
            var singleton=new NavIncrementalSingletonRoute(original.Graph,subscription,4096);var coldSingleton=new NavIncrementalSingletonRoute(restored.Graph,subscription,4096);
            while(singleton.Status==NavSolveStatus.Pending){
                Assert.AreEqual(singleton.Advance(11).Consumed,coldSingleton.Advance(11).Consumed);
                var saved=coldSingleton.Save();coldSingleton=NavIncrementalSingletonRoute.Restore(restored.Graph,subscription,Roundtrip(c=>c.Singleton(saved),c=>c.Singleton()),4096);
                Assert.AreEqual(singleton.Status,coldSingleton.Status);Assert.AreEqual(singleton.StateCount,coldSingleton.StateCount);
            }
            Assert.AreEqual(NavSolveStatus.Ready,singleton.Status);Assert.AreEqual(singleton.Route.Cost,coldSingleton.Route.Cost);
            CollectionAssert.AreEqual(singleton.Route.Legs.Select(l=>l.To),coldSingleton.Route.Legs.Select(l=>l.To));
        }
    }
}

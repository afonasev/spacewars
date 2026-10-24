using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationStorageOwnershipTests
    {
        // Independent identity walk in tests only. Production has no identity map
        // for every node/page, and doesn't walk untouched neighbors to admit work.
        internal static long Unique(params NavOwnedStorage[] roots)
        {
            var seen=new HashSet<NavOwnedStorage>();long bytes=0;
            void Visit(NavOwnedStorage node){
                if(node==null||!seen.Add(node))return;Assert.Greater(node.References,0);bytes+=node.StorageBytes;
                for(Type type=node.GetType();type!=typeof(NavOwnedStorage);type=type.BaseType)
                    foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly)){
                        var value=field.GetValue(node);VisitPayload(value);
                        if(value is Array children)foreach(var entry in children)VisitPayload(entry);
                        if(value!=null&&value.GetType().IsGenericType&&value.GetType().GetGenericTypeDefinition()==typeof(NavStorageDirectory<>))Visit((NavOwnedStorage)value.GetType().GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value));
                    }
            }
            void VisitPayload(object value){if(value is NavOwnedStorage storage)Visit(storage);else if(value is INavStorageFacade facade)Visit(facade.StorageRoot);else if(value is NavNameReference name)Visit(name.Owner);else if(value is NavNameKey key)Visit(key.Reference.Owner);else if(value is NavMetadataReference metadata)Visit(metadata.Owner);else if(value is NavPhysicalReference reference)Visit(reference.Storage);else if(value is NavObstacle obstacle)Visit(obstacle.Polygon?.StorageRoot);else if(value is NavGraphArc arc)VisitPayload(arc.Trace);else if(value is NavPackedFieldCell cell&&(cell.Flags&2)!=0)VisitPayload(cell.Next);else if(value is NavigationLiveRegistrations.Entry registration){Visit(registration.Request);Visit(registration.Recipe);}}
            foreach(var root in roots)Visit(root);return bytes;
        }
        private static List<NavOwnedStorage> SessionRoots(NavigationSession session,NavOwnedStorage[] roots)
        {
            var all=new List<NavOwnedStorage>(roots);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            foreach(var name in new[]{"authorityOwners","groupCommandBacking","transportOwner","barrierPayloads","legacyProbePayloads","retiredLegacyProbePayloads","terrainNameOwners","committedHostRoot","creditBank","admissionOwner","retiredAdmissions","retiredArrivalProviders","retiredArrivalAttempts","retiredArrivalLeases","liveArrivalLeaseTable","liveRegistrations","retiredRegistrations","ownedAnswerTracking","ownedProbes","liveProbeAnswers","retiredProbeQueues","retiredProbeReceipts","installedExecutions"})all.Add((NavOwnedStorage)typeof(NavigationSession).GetField(name,flags).GetValue(session));
            all.Add((NavOwnedStorage)typeof(NavigationSession).GetField("liveArrivalTables",flags).GetValue(session));
            all.Add((NavOwnedStorage)session.Answers.GetType().GetField("retention",flags).GetValue(session.Answers));
            if(session.Crowd!=null){all.Add(session.Crowd.SpatialStorage);foreach(var unit in session.Crowd.Units)all.Add(unit.StorageOwner);}
            // Destruction closes the live facade; pinned immutable captures are
            // still independently walked through the explicit roots above.
            if(session.Crowd!=null)foreach(var pair in (System.Collections.IEnumerable)typeof(NavigationSession).GetField("arrivalAttempts",flags).GetValue(session)){
                var value=pair.GetType().GetProperty("Value").GetValue(pair);if(value is INavStorageFacade facade)all.Add(facade.StorageRoot);
            }
            var installed=(IDictionary<int,NavigationInstalledExecutionState>)typeof(NavigationSession).GetField("installedExecutions",flags).GetValue(session);if(installed!=null)foreach(var value in installed.Values)all.Add(value.StorageOwner);
            return all;
        }
        internal static long UniqueWithSession(NavigationSession session,params NavOwnedStorage[] roots)=>Unique(SessionRoots(session,roots).ToArray());
        internal static string OwnerShapeWithSession(NavigationSession session,params NavOwnedStorage[] roots)
        {
            var seen=new HashSet<NavOwnedStorage>();var shapes=new SortedDictionary<string,int>();
            void Payload(object value){if(value is NavOwnedStorage owner)Visit(owner);else if(value is INavStorageFacade facade)Visit(facade.StorageRoot);else if(value is NavNameReference name)Visit(name.Owner);else if(value is NavNameKey key)Visit(key.Reference.Owner);else if(value is NavMetadataReference metadata)Visit(metadata.Owner);else if(value is NavPhysicalReference reference)Visit(reference.Storage);else if(value is NavObstacle obstacle)Visit(obstacle.Polygon?.StorageRoot);else if(value is NavGraphArc arc)Payload(arc.Trace);else if(value is NavPackedFieldCell cell&&(cell.Flags&2)!=0)Payload(cell.Next);else if(value is NavigationLiveRegistrations.Entry registration){Visit(registration.Request);Visit(registration.Recipe);}}
            void Visit(NavOwnedStorage node){if(node==null||!seen.Add(node))return;Assert.Greater(node.References,0);string key=node.GetType().FullName+":"+node.StorageBytes;shapes[key]=shapes.TryGetValue(key,out int count)?count+1:1;
                for(Type type=node.GetType();type!=typeof(NavOwnedStorage);type=type.BaseType)foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly)){var value=field.GetValue(node);Payload(value);if(value is Array array)foreach(var item in array)Payload(item);if(value!=null&&value.GetType().IsGenericType&&value.GetType().GetGenericTypeDefinition()==typeof(NavStorageDirectory<>))Visit((NavOwnedStorage)value.GetType().GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value));}}
            foreach(var root in SessionRoots(session,roots))Visit(root);return string.Join("\n",shapes.Select(pair=>pair.Key+"="+pair.Value));
        }
        [TestCase(false)][TestCase(true)]
        public void CompilerMetadataCopyForkAndFinalGraphKeepPrivateOwnersAfterInputAndCompilerRetirement(bool partition)
        {
            SmallRecipe(out var flat);var map=ThreeCrossingsMap.Default;ILayeredNavigationProvider provider=partition?(ILayeredNavigationProvider)new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null):flat;
            var c=new NavClearanceProfile("ground",1,"ground",1,.25);var p=new NavGraphProfile(new string('c',64),"distance",1,partition?16:1);var l=new NavGraphCompileLimits(2000,2000,10000);
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,c,p,l,initial.OwnedReservation());initial.Attach(compiler);compiler.Advance(1);compiler.SealPrepared();initial.Commit(compiler,_=>{});
            var privateClearance=(NavClearanceProfile)typeof(LayeredNavigationCompileWork).GetField("clearance",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(compiler);var metadata=privateClearance.MetadataOwner;
            Assert.AreNotSame(c,privateClearance);Assert.AreNotSame(c.Id,privateClearance.Id);Assert.Greater(metadata.References,0);Assert.AreEqual(Unique(compiler),budget.PhysicalBytes);provider=null;c=null;p=null;l=null;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var solve));var fork=compiler.ForkOwned(solve.OwnedReservation());solve.Attach(fork);Assert.AreSame(privateClearance,typeof(LayeredNavigationCompileWork).GetField("clearance",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fork));int steps=0;while(fork.Phase!=LayeredNavigationCompileWork.Stage.Ready){Assert.Less(++steps,20000);fork.Advance(1);}fork.SealPrepared();solve.Commit(fork,_=>{});
            var graph=fork.Graph;Assert.AreSame(privateClearance,graph.Binding.Clearance);Assert.AreSame(metadata,graph.Binding.Profile.MetadataOwner);graph.Retain();compiler.ReleaseStorage();fork.ReleaseStorage();Assert.Greater(metadata.References,0);Assert.AreEqual(Unique(graph),budget.PhysicalBytes);Assert.AreSame(privateClearance,NavMetadataOwnership.Resolve(metadata,NavMetadataRole.Clearance));graph.Release();Assert.Zero(metadata.References);Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void StrictCompilerMetadataRejectsPublicOrForeignFieldBeforeFirstReservation(bool foreign)
        {
            SmallRecipe(out var input);var c=new NavClearanceProfile("ground",1,"ground",1,.25);var p=new NavGraphProfile(new string('c',64),"distance",1,1);var l=new NavGraphCompileLimits(256,256,1024);
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var provider=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());var own=NavGraphMetadata.CopyOwned(c,p,l,initial.OwnedReservation());var bundle=NavRetainedArray<NavOwnedStorage>.Copy(new[]{provider.StorageRoot,own},initial.TryReserve);provider.StorageRoot.Release();own.Release();initial.Attach(bundle);initial.Commit(bundle,_=>{});
            var other=new NavigationRetentionBudget();Assert.True(other.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var second));var distinct=NavGraphMetadata.CopyOwned(c,p,l,second.OwnedReservation());second.Attach(distinct);second.Commit(distinct,_=>{});
            var state=new LayeredNavigationCompileWork(input,c,p,l);var saved=state.Save();state.ReleaseStorage();var invalid=foreign?distinct.Profile:p;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,2,out var attempt));var counter=new RefusingAllocator(attempt,int.MaxValue);var reservation=new NavStorageReservation(counter.Reserve,counter);long held=attempt.HeldBytes,physical=budget.PhysicalBytes;int refs=own.References,foreignRefs=distinct.References;
            Assert.Throws<ArgumentException>(()=>LayeredNavigationCompileWork.CreateOwned(provider,own.Clearance,invalid,own.Limits,reservation));Assert.Throws<ArgumentException>(()=>LayeredNavigationCompileWork.RestoreOwned(provider,own.Clearance,invalid,own.Limits,saved,reservation));
            if(foreign)Assert.Throws<ArgumentException>(()=>LayeredNavigationCompileWork.CopyProviderAndCreateOwned(input,c,invalid,l,reservation));
            Assert.Zero(counter.Calls);Assert.AreEqual(held,attempt.HeldBytes);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(refs,own.References);Assert.AreEqual(foreignRefs,distinct.References);attempt.Discard();bundle.Release();distinct.Release();Assert.Zero(budget.PhysicalBytes);Assert.Zero(other.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void CopyCompilerForeignOwnedProviderWithPublicMetadataRejectsBeforeFirstAllocation(bool restore)
        {
            SmallRecipe(out var input);var c=new NavClearanceProfile("ground",1,"ground",1,.25);var p=new NavGraphProfile(new string('c',64),"distance",1,1);var l=new NavGraphCompileLimits(256,256,1024);
            var first=new NavigationRetentionBudget();Assert.True(first.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var provider=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());initial.Attach(provider);initial.Commit(provider,_=>{});
            var legacy=new LayeredNavigationCompileWork(input,c,p,l);var saved=legacy.Save();legacy.ReleaseStorage();byte[] Bytes(){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(saved);return stream.ToArray();}}var sourceBytes=Bytes();int refs=provider.StorageRoot.References;long physical=first.PhysicalBytes,retained=first.RetainedBytes;
            var target=new NavigationRetentionBudget();Assert.True(target.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var attempt));var counter=new RefusingAllocator(attempt,int.MaxValue);var reservation=new NavStorageReservation(counter.Reserve,counter);long held=attempt.HeldBytes,targetPhysical=target.PhysicalBytes;
            Assert.Throws<ArgumentException>(()=>{if(restore)LayeredNavigationCompileWork.CopyProviderAndRestoreOwned(provider,c,p,l,saved,reservation);else LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,c,p,l,reservation);});
            Assert.Zero(counter.Calls);Assert.AreEqual(held,attempt.HeldBytes);Assert.AreEqual(targetPhysical,target.PhysicalBytes);Assert.AreEqual(refs,provider.StorageRoot.References);Assert.AreEqual(physical,first.PhysicalBytes);Assert.AreEqual(retained,first.RetainedBytes);CollectionAssert.AreEqual(sourceBytes,Bytes());attempt.Discard();Assert.Zero(target.PhysicalBytes);Assert.Zero(target.HeldBytes);provider.StorageRoot.Release();Assert.Zero(first.PhysicalBytes);
        }
        [Test] public void CopyCompilerMixedOwnersPreservesOwnedFieldsAndCopiesOnlyPublicLimits()
        {
            SmallRecipe(out var input);var c=new NavClearanceProfile("ground",1,"ground",1,.25);var p=new NavGraphProfile(new string('c',64),"distance",1,1);var l=new NavGraphCompileLimits(256,256,1024);
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));var first=NavGraphMetadata.CopyOwned(c,p,l,scope.OwnedReservation());var second=NavGraphMetadata.CopyOwned(c,p,l,scope.OwnedReservation());var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(input,first.Clearance,second.Profile,l,scope.OwnedReservation());
            var pc=(NavClearanceProfile)typeof(LayeredNavigationCompileWork).GetField("clearance",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(compiler);var pp=(NavGraphProfile)typeof(LayeredNavigationCompileWork).GetField("profile",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(compiler);var pl=(NavGraphCompileLimits)typeof(LayeredNavigationCompileWork).GetField("limits",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(compiler);
            Assert.AreSame(first.Clearance,pc);Assert.AreSame(second.Profile,pp);Assert.AreNotSame(l,pl);var partial=(NavGraphMetadata)pl.MetadataOwner;Assert.IsNull(partial.Clearance);Assert.IsNull(partial.Profile);Assert.AreSame(pl,partial.Limits);first.Release();second.Release();compiler.SealPrepared();scope.Attach(compiler);scope.Commit(compiler,_=>{});Assert.AreEqual(Unique(compiler),budget.PhysicalBytes);compiler.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void PhysicalTreeCreditConversionCommitAndPinnedOldRootMatchUniqueOracle()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));
            var source=NavCowDictionary<int,double>.Create(null,false,initial.TryReserve);initial.Attach(source);
            for(int i=0;i<80;i++)source.Add(i,i);source.Seal();
            long retainedBefore=budget.RetainedBytes;initial.Commit(source,_=>{});
            Assert.Less(budget.RetainedBytes,retainedBefore,"Intermediate rotation/path births and unused credits are terminally released.");
            Assert.AreEqual(Unique(source.PhysicalRoot),budget.PhysicalBytes);
            var pin=source.PhysicalRoot;pin.Retain();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));
            var candidate=source.ForkPrepared(scope.TryReserve);scope.Attach(candidate);candidate[37]=999;candidate.Remove(3);candidate.Seal();
            Assert.AreEqual(37,source[37]);Assert.AreEqual(999,candidate[37]);
            scope.Commit(candidate,_=>{});source.ReleaseStorage();
            Assert.AreEqual(Unique(pin,candidate.PhysicalRoot),budget.PhysicalBytes,"Pinned exclusive old nodes survive publication and retirement.");
            pin.Release();Assert.AreEqual(Unique(candidate.PhysicalRoot),budget.PhysicalBytes);
            candidate.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
            Assert.AreEqual(2,budget.ResolvedScopes);
        }
        [Test] public void EveryNthTreeAllocationRefusalReleasesAllIntermediateBirths()
        {
            // Failure injection retains allocator identity through a named wrapper,
            // so actual physical accounting remains active on every allocation.
            var countBudget=new NavigationRetentionBudget();Assert.True(countBudget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var countScope));
            var counter=new RefusingAllocator(countScope,int.MaxValue);var counted=NavCowDictionary<int,double>.Create(null,false,counter.Reserve);
            for(int i=0;i<12;i++)counted.Add(i,i);int maximum=counter.Calls;counted.ReleaseStorage();countScope.Discard();Assert.AreEqual(0,countBudget.PhysicalBytes);
            for(int nth=1;nth<=maximum;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));var allocator=new RefusingAllocator(scope,nth);NavCowDictionary<int,double> value=null;
                try{value=NavCowDictionary<int,double>.Create(null,false,allocator.Reserve);for(int i=0;i<12;i++)value.Add(i,i);Assert.Fail("Expected allocation refusal.");}
                catch(InvalidOperationException){Assert.AreEqual(nth,allocator.Calls);}
                finally{value?.ReleaseStorage();scope.Discard();}
                Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(0,budget.HeldBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
            }
        }
        private sealed class RefusingAllocator : INavStorageAllocator, INavStorageScratchAllocator, INavStorageLedgerIdentity
        {
            private readonly NavigationRetentionBudget.Scope scope;private readonly int nth;
            internal int Calls;
            internal RefusingAllocator(NavigationRetentionBudget.Scope scope,int nth){this.scope=scope;this.nth=nth;}
            internal bool Reserve(long bytes){Calls++;return Calls!=nth&&scope.TryReserve(bytes);}
            public void Register(NavOwnedStorage storage)=>((INavStorageAllocator)scope).Register(storage);
            public IDisposable ClaimScratch(long admittedBytes)=>scope.ClaimScratch(admittedBytes);
            public object Identity=>scope.Identity;
            public bool Owns(NavOwnedStorage storage)=>scope.Owns(storage);
        }
        [Test] public void DirectoryForkIsConstantWorkAndOldPagePinsSurviveCommittedReplacement()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));
            var source=NavPagedList<int>.Create(initial.TryReserve,1);initial.Attach(source);
            for(int i=0;i<1100;i++)source.Add(i);source.Seal();initial.Commit(source,_=>{});
            Assert.AreEqual(Unique(source.PhysicalRoot),budget.PhysicalBytes);var pin=source.PhysicalRoot;pin.Retain();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var operation));long before=budget.PhysicalBytes,held=operation.HeldBytes;
            var fork=source.Fork(operation.TryReserve);operation.Attach(fork);
            Assert.AreEqual(before+384+3*NavOwnedStorage.CaptureHeaderBytes,budget.PhysicalBytes);Assert.AreEqual(384+3*NavOwnedStorage.CaptureHeaderBytes,operation.HeldBytes-held,"Fork charges its bounded root/controller metadata regardless of 1100 pages.");
            fork[1027]=-1;fork.Seal();operation.Commit(fork,_=>{});
            Assert.AreEqual(1027,source[1027]);Assert.AreEqual(-1,fork[1027]);source.ReleaseStorage();
            Assert.AreEqual(Unique(pin,fork.PhysicalRoot),budget.PhysicalBytes);pin.Release();Assert.AreEqual(Unique(fork.PhysicalRoot),budget.PhysicalBytes);
            fork.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void DirectoryWritableLegacyCallbacksKeepSharedBirthJournalAcrossForks(bool callback)
        {
            int calls=0;Func<long,bool> reserve=callback?new Func<long,bool>(bytes=>{Assert.Greater(bytes,0);calls++;return true;}):null;
            var source=new NavStorageDirectory<int>();var original=source.Writable(0,4,4,reserve,true);original.Values[0]=41;
            var sourceOwner=source.PhysicalRoot;var sourceTree=(NavOwnedStorage)typeof(NavStorageDirectory<int>).GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(source);
            var pin=source.Get(0);pin.Retain();var fork=source.Fork(reserve);var forkOwner=fork.PhysicalRoot;var changed=fork.Writable(0,4,4,reserve);var forkTree=(NavOwnedStorage)typeof(NavStorageDirectory<int>).GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fork);changed.Values[0]=73;
            Assert.AreEqual(41,source.Get(0).Values[0]);Assert.AreEqual(73,fork.Get(0).Values[0]);Assert.Greater(pin.References,0);
            fork.Dispose();Assert.Zero(forkOwner.References);Assert.Zero(forkTree.References);Assert.Greater(pin.References,0,"The explicit page pin remains live after the fork is released.");
            source.Dispose();Assert.Zero(sourceOwner.References);Assert.Zero(sourceTree.References);Assert.AreEqual(1,pin.References);Assert.AreEqual(41,pin.Values[0]);pin.Release();Assert.Zero(pin.References);
            if(callback)Assert.Greater(calls,0);
        }
        [Test] public void DirectoryWritableLegacyCallbackRefusalCleansFailedPrefix()
        {
            int calls=0;Func<long,bool> reserve=bytes=>++calls<2;
            var directory=new NavStorageDirectory<int>();
            Assert.Throws<InvalidOperationException>(()=>directory.Writable(0,4,4,reserve,true));
            var root=(NavOwnedStorage)typeof(NavStorageDirectory<int>).GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(directory);
            Assert.NotNull(root);Assert.AreEqual(2,calls,"Refusal remains at the second physical reservation.");Assert.AreEqual(0,directory.Count);
            directory.Dispose();Assert.Zero(directory.References);Assert.Zero(root.References);
        }
        private static List<(OpCode opcode,MethodBase target)> ReadIl(MethodInfo method)
        {
            var bytes=method.GetMethodBody().GetILAsByteArray();var map=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(op=>op.Value);var result=new List<(OpCode,MethodBase)>();int at=0;
            while(at<bytes.Length){short value=bytes[at++]==0xfe?(short)(0xfe00|bytes[at++]):(short)bytes[at-1];var opcode=map[value];MethodBase target=null;int size=0;
                switch(opcode.OperandType){case OperandType.InlineNone:break;case OperandType.ShortInlineBrTarget:case OperandType.ShortInlineI:case OperandType.ShortInlineVar:size=1;break;case OperandType.InlineVar:size=2;break;
                    case OperandType.InlineI:case OperandType.InlineBrTarget:case OperandType.InlineField:case OperandType.InlineSig:case OperandType.InlineString:case OperandType.InlineTok:case OperandType.InlineType:case OperandType.ShortInlineR:size=4;break;
                    case OperandType.InlineI8:case OperandType.InlineR:size=8;break;case OperandType.InlineSwitch:int count=BitConverter.ToInt32(bytes,at);size=4+4*count;break;
                    case OperandType.InlineMethod:int token=BitConverter.ToInt32(bytes,at);try{target=method.Module.ResolveMethod(token,method.DeclaringType.IsGenericType?method.DeclaringType.GetGenericArguments():null,method.IsGenericMethod?method.GetGenericArguments():null);}catch(ArgumentException){try{target=method.Module.ResolveMethod(token);}catch(ArgumentException){}}size=4;break;
                    default:throw new InvalidOperationException("Unsupported IL operand " + opcode.OperandType);}
                result.Add((opcode,target));at+=size;
            }
            return result;
        }
        [Test] public void OwnedDirectoryAllocationContextCompilesWithoutBoxingOrLegacyHelperConstruction()
        {
            Assert.True(typeof(NavStorageAllocationContext).IsValueType);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var writable=typeof(NavStorageDirectory<int>).GetMethod("Writable",flags);var instructions=ReadIl(writable);
            Assert.False(instructions.Any(row=>row.opcode==OpCodes.Box),"The concrete using context must not box for Dispose.");
            Assert.False(instructions.Any(row=>row.opcode==OpCodes.Newobj&&row.target?.DeclaringType==typeof(NavStorageAllocation)),"Writable must not construct the fallback helper.");
            var register=typeof(NavStorageAllocationContext).GetMethod("Register",flags);var registerInstructions=ReadIl(register);
            TestContext.WriteLine("Register IL: " + string.Join(" ",registerInstructions.Select(row=>row.opcode.Name+":"+(row.target?.ToString()??"?"))));
            Assert.False(registerInstructions.Any(row=>row.opcode==OpCodes.Box),"Register IL: " + string.Join(" ",registerInstructions.Select(row=>row.opcode.Name)));
            Assert.True(registerInstructions.Any(row=>row.target?.DeclaringType==typeof(INavStorageAllocator)&&row.target.Name=="Register"),"Owned storage registers directly with its allocator.");
        }
        [Test] public void PackedFieldAndHeapPhysicalPinsAndDiscardMatchUniqueOracle()
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,Array.Empty<NavObstacle>(),1));
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,0,out var graphScope));
            var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('b',64),"distance",1,1),new NavGraphCompileLimits(2000,2000,10000),graphScope.OwnedReservation());graphScope.Attach(compiler);
            while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready)compiler.Advance(100);compiler.SealPrepared();graphScope.Commit(compiler,_=>{});
            var graph=compiler.Graph;graph.Retain();compiler.ReleaseStorage();
            var locations=Enumerable.Range(0,8).Select(n=>new NavLocation(new NavPoint(-2+n%4,-1+n/4),NavLocation.FlatSurface)).ToArray();
            var region=new NavTerminalRegion("storage-field",1,locations[0],locations);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));
            var source=new NavSharedTerminalField(graph,region,2048,false,NavStorageLayout.Before(initial.TryReserve,512),true);initial.Attach(source);source.Advance(100);source.SealPrepared();initial.Commit(source,_=>{});
            Assert.AreEqual(Unique(source.PhysicalRoot),budget.PhysicalBytes);source.Retain();var pin=source.PhysicalRoot;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var operation));var fork=source.ForkPrepared(operation.TryReserve);operation.Attach(fork);fork.Advance(100);fork.SealPrepared();operation.Commit(fork,_=>{});source.ReleaseStorage();
            Assert.AreEqual(Unique(pin,fork.PhysicalRoot),budget.PhysicalBytes);pin.Release();Assert.AreEqual(Unique(fork.PhysicalRoot),budget.PhysicalBytes);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var discard));var rejected=fork.ForkPrepared(discard.TryReserve);discard.Attach(rejected);rejected.Advance(5);discard.RequestCancel();discard.Discard();
            Assert.AreEqual(Unique(fork.PhysicalRoot),budget.PhysicalBytes,"Discard releases the prepared container owner before all intermediate births.");
            fork.ReleaseStorage();graph.Release();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
        }
        [Test] public void WrappedOwnedReservationPublishesWithoutBudgetGateAndResolvesAfterTransfer()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));int calls=0;
            var reservation=scope.OwnedReservation(bytes=>{calls++;return scope.TryReserve(bytes);});
            var root=NavCowDictionary<int,double>.CreateOwned(reservation,insertionOrder:false);scope.Attach(root);for(int n=0;n<12;n++)root.Add(n,n);root.Seal();
            Assert.Greater(calls,12);Assert.Greater(budget.PhysicalBytes,0,"Decorated callback must not bypass physical registration.");
            var gate=typeof(NavigationRetentionBudget).GetField("gate",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(budget);var transport=new object();object published=null;
            scope.Publish(root,value=>{Assert.False(System.Threading.Monitor.IsEntered(gate));lock(transport)published=value;});
            Assert.AreSame(root,published);Assert.False(scope.IsResolved);Assert.AreEqual(1,budget.ActiveScopes);
            scope.ResolveCommitted();Assert.True(scope.IsResolved);Assert.AreEqual(Unique(root.PhysicalRoot),budget.PhysicalBytes);
            root.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        private sealed class BlockingStorage : NavOwnedStorage
        {
            private readonly System.Threading.ManualResetEvent entered,release;
            internal BlockingStorage(System.Threading.ManualResetEvent entered,System.Threading.ManualResetEvent release):base(64){this.entered=entered;this.release=release;}
            protected override void ReleaseEdges(){entered.Set();if(!release.WaitOne(10000))throw new TimeoutException("Test did not release terminal birth.");}
        }
        [Test] public void TerminalAckAndNewScopeAdmissionWaitForLastBirthRelease()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));
            using(var entered=new System.Threading.ManualResetEvent(false))using(var release=new System.Threading.ManualResetEvent(false)){
                using(var allocation=new NavStorageAllocation(scope.TryReserve)){allocation.Reserve(64);allocation.Register(new BlockingStorage(entered,release));}
                scope.Attach(new object());scope.Publish(scope.Prepared,_=>{});scope.RelinquishOwner();Exception error=null;
                var owner=new System.Threading.Thread(()=>{try{scope.AcquireOwner();scope.ResolveCommitted();}catch(Exception failure){error=failure;}});owner.Start();
                try{
                    Assert.True(entered.WaitOne(10000));Assert.True(scope.IsResolving);Assert.False(scope.IsResolved);Assert.AreEqual(1,budget.ActiveScopes);Assert.Greater(budget.PhysicalBytes,0);
                    Assert.False(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out _),"Terminal owner has not yet released the last journal allocation.");
                }finally{release.Set();Assert.True(owner.Join(10000));}
                Assert.Null(error);Assert.True(scope.IsResolved);Assert.False(scope.IsResolving);Assert.AreEqual(0,budget.ActiveScopes);Assert.AreEqual(0,budget.PhysicalBytes);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var next));next.Discard();
            }
        }
        [Test] public void OwnedRouteCopiesPublicArrayAndSharedLegStorageIsChargedOnce()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,1,out var scope));
            var reservation=scope.OwnedReservation(bytes=>scope.TryReserve(bytes));var from=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);var to=new NavLocation(new NavPoint(1,0),NavLocation.FlatSurface);
            var leg=NavTypedLeg.CreateOwned(from,to,1,NavTypedLegKind.Surface,null,reservation);var external=new[]{leg};
            var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,from,to,null);
            var routeA=NavTypedRoute.CopySubscriptionAndCreateOwned(subscription,external,1,reservation);var routeB=new NavTypedRoute(routeA.Subscription,routeA.Legs,1,NavStorageLayout.Before(reservation.TryReserve,128),true);
            external[0]=null;Assert.AreSame(leg,routeA.Legs[0]);Assert.AreSame(routeA.Legs,routeB.Legs);scope.Attach(routeA);scope.Commit(routeA,_=>{});leg.ReleaseStorage();
            Assert.AreEqual(Unique(routeA,routeB),budget.PhysicalBytes);routeA.ReleaseStorage();Assert.AreSame(leg,routeB.Legs[0]);Assert.AreEqual(Unique(routeB),budget.PhysicalBytes);
            routeB.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
        }
        [TestCase("flat")][TestCase("partition")][TestCase("field")][TestCase("singleton")][TestCase("shared")]
        public void TerminalPreparedForkDiscardCannotReleaseOriginalCommittedRoot(string kind)
        {
            ILayeredNavigationProvider provider;if(kind=="partition"){var map=ThreeCrossingsMap.Default;provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);}else provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var graphScope));var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('c',64),"distance",1,kind=="partition"?16:1),new NavGraphCompileLimits(2000,2000,10000),graphScope.OwnedReservation());graphScope.Attach(compiler);int steps=0;while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready){Assert.Less(++steps,20000);compiler.Advance(1);}compiler.SealPrepared();graphScope.Commit(compiler,_=>{});var graph=compiler.Graph;var at=graph.Nodes[0].Location;var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,at,at,null);
            NavSharedTerminalField field=null;NavOwnedStorage source=compiler;
            if(kind=="field"||kind=="shared"){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var fieldScope));field=NavSharedTerminalField.CopyRegionAndCreateOwned(graph,new NavTerminalRegion("terminal-fork",1,at,new[]{at}),4096,fieldScope.OwnedReservation());fieldScope.Attach(field);steps=0;while(field.Status==NavSolveStatus.Pending){Assert.Less(++steps,20000);field.Advance(1);}Assert.AreEqual(NavSolveStatus.Ready,field.Status);field.SealPrepared();fieldScope.Commit(field,_=>{});source=field;
            }
            if(kind=="singleton"||kind=="shared"){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var solve));source=kind=="singleton"?(NavOwnedStorage)NavIncrementalSingletonRoute.CopySubscriptionAndCreateOwned(graph,subscription,4096,solve.OwnedReservation()):NavIncrementalSharedRoute.CopySubscriptionAndCreateOwned(field,subscription,solve.OwnedReservation());solve.Attach(source);steps=0;
                while((source is NavIncrementalSingletonRoute single?single.Status:((NavIncrementalSharedRoute)source).Status)==NavSolveStatus.Pending){Assert.Less(++steps,20000);if(source is NavIncrementalSingletonRoute singleton)singleton.Advance(1);else((NavIncrementalSharedRoute)source).Advance(1);}
                if(source is NavIncrementalSingletonRoute one){Assert.AreEqual(NavSolveStatus.Ready,one.Status);one.SealPrepared();}else{Assert.AreEqual(NavSolveStatus.Ready,((NavIncrementalSharedRoute)source).Status);((NavIncrementalSharedRoute)source).SealPrepared();}solve.Commit(source,_=>{});
            }
            byte[] Bytes(){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true)){var codec=new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1);if(source is LayeredNavigationCompileWork c)codec.Compile(c.Save());else if(source is NavSharedTerminalField f)codec.Field(f.Save());else if(source is NavIncrementalSingletonRoute s)codec.Singleton(s.Save());else codec.Shared(((NavIncrementalSharedRoute)source).Save());}return stream.ToArray();}}
            var before=Bytes();long baseline=budget.RetainedBytes;int references=source.References;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,4,out var scope));var reservation=scope.OwnedReservation();NavOwnedStorage candidate;
            if(source is LayeredNavigationCompileWork compile)candidate=compile.ForkOwned(reservation);else if(source is NavSharedTerminalField readyField)candidate=readyField.ForkOwned(reservation);else if(source is NavIncrementalSingletonRoute singleton)candidate=singleton.ForkOwned(reservation);else candidate=((NavIncrementalSharedRoute)source).ForkOwned(field,reservation);
            Assert.AreNotSame(source,candidate);scope.Attach(candidate);
            var roots=new List<NavOwnedStorage>{compiler,field,source,candidate};for(var birth=(NavOwnedStorage)typeof(NavigationRetentionBudget.Scope).GetField("births",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scope);birth!=null;birth=birth.BirthNext)roots.Add(birth);
            Assert.AreEqual(Unique(roots.ToArray()),budget.PhysicalBytes,"All intermediate metadata births remain charged until terminal resolution.");scope.Discard();CollectionAssert.AreEqual(before,Bytes());Assert.AreEqual(references,source.References);Assert.AreEqual(baseline,budget.RetainedBytes);Assert.AreEqual(Unique(compiler,field,source),budget.PhysicalBytes);
            if(source is NavIncrementalSingletonRoute s){Assert.AreEqual(NavSolveStatus.Ready,s.Status);Assert.Greater(s.Route.References,0);Assert.AreEqual(at,s.Route.Legs[0].From);}
            if(source!=compiler&&source!=field)source.ReleaseOwner();field?.ReleaseStorage();compiler.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [Test] public void AdmittedGeometryCopiesPrivateBuffersAndPreservesPhysicalPolygonAliases()
        {
            var points=new[]{new NavPoint(-1,-1),new NavPoint(1,-1),new NavPoint(1,1),new NavPoint(-1,1)};var first=new NavPolygon(points);var equal=new NavPolygon(points);
            var source=new NavGeometry(8,new[]{new NavObstacle(first),new NavObstacle(first,1,2),new NavObstacle(equal),new NavObstacle(new NavPoint(3,3),.7),new NavObstacle(-4,-4,-3,-3)},2);var originals=source.Obstacles.ToArray();
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));var counter=new RefusingAllocator(scope,int.MaxValue);var copy=NavGeometry.CopyOwned(source,new NavStorageReservation(counter.Reserve,counter));scope.Attach(copy);int calls=counter.Calls;
            Assert.AreNotSame(source.Obstacles,copy.Obstacles);Assert.AreNotSame(first,copy.Obstacles[0].Polygon);Assert.AreSame(copy.Obstacles[0].Polygon,copy.Obstacles[1].Polygon);Assert.AreNotSame(copy.Obstacles[0].Polygon,copy.Obstacles[2].Polygon);Assert.AreNotSame(first.Vertices,copy.Obstacles[0].Polygon.Vertices);Assert.AreNotSame(first.Triangles,copy.Obstacles[0].Polygon.Triangles);
            for(int n=0;n<originals.Length;n++)Assert.True(originals[n].GeometryEquals(copy.Obstacles[n]));CollectionAssert.AreEqual(first.Triangles,copy.Obstacles[0].Polygon.Triangles);
            foreach(var point in new[]{new NavPoint(0,0),new NavPoint(2,0),new NavPoint(7,7),new NavPoint(3,3)})foreach(double radius in new[]{0d,.25,.7}){Assert.AreEqual(source.IsFree(point,radius),copy.IsFree(point,radius));Assert.AreEqual(source.SegmentFree(new NavPoint(-6,0),point,radius),copy.SegmentFree(new NavPoint(-6,0),point,radius));}
            Assert.AreEqual(NavigationRetentionBudget.ScopeBytes,budget.HeldBytes);scope.Commit(copy,_=>{});Assert.AreEqual(Unique(copy.StorageRoot),budget.PhysicalBytes);copy.StorageRoot.ReleaseOwner();Assert.AreEqual(0,budget.PhysicalBytes);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var discard));var discarded=NavGeometry.CopyOwned(source,discard.OwnedReservation());discard.Attach(discarded);discard.Discard();Assert.AreEqual(0,budget.PhysicalBytes);
            for(int nth=1;nth<=calls;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var rejected));counter=new RefusingAllocator(rejected,nth);Assert.Throws<InvalidOperationException>(()=>NavGeometry.CopyOwned(source,new NavStorageReservation(counter.Reserve,counter)));Assert.AreEqual(nth,counter.Calls);rejected.Discard();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);for(int n=0;n<originals.Length;n++)Assert.True(originals[n].GeometryEquals(source.Obstacles[n]));
            }
        }
        [Test] public void AliasCatalogOwnsAllTableRootsThroughFreezeCommitAndEveryNthFailure()
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var graphScope));var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('b',64),"distance",1,1);
            var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,clearance,profile,new NavGraphCompileLimits(2000,2000,10000),graphScope.OwnedReservation());graphScope.Attach(compiler);while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready)compiler.Advance(1);compiler.SealPrepared();graphScope.Commit(compiler,_=>{});long baseline=budget.RetainedBytes;int references=compiler.Graph.References;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,2,out var scope));var counter=new RefusingAllocator(scope,int.MaxValue);var aliases=NavigationCheckpointAliases.CreateOwned(scope,new NavStorageReservation(counter.Reserve,counter));scope.Attach(aliases);Assert.AreEqual(0,aliases.GraphId(compiler.Graph));Assert.AreEqual(0,aliases.GraphId(compiler.Graph));aliases.Freeze();aliases.Freeze();int calls=counter.Calls;
            scope.Commit(aliases,_=>{});Assert.AreEqual(Unique(compiler,aliases),budget.PhysicalBytes);Assert.AreEqual(references+1,compiler.Graph.References);aliases.ReleaseStorage();Assert.AreEqual(references,compiler.Graph.References);Assert.AreEqual(baseline,budget.RetainedBytes);
            for(int nth=1;nth<=calls;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,2,out var rejected));counter=new RefusingAllocator(rejected,nth);NavigationCheckpointAliases candidate=null;
                try{candidate=NavigationCheckpointAliases.CreateOwned(rejected,new NavStorageReservation(counter.Reserve,counter));rejected.Attach(candidate);candidate.GraphId(compiler.Graph);candidate.Freeze();Assert.Fail("Expected alias factory/growth/freeze refusal.");}
                catch(Exception error)when(error is ArgumentException||error is InvalidOperationException){Assert.AreEqual(nth,counter.Calls);}finally{rejected.Discard();}
                Assert.AreEqual(references,compiler.Graph.References);Assert.AreEqual(baseline,budget.RetainedBytes);Assert.AreEqual(Unique(compiler),budget.PhysicalBytes);
            }
            var other=new NavigationRetentionBudget();Assert.True(other.TryOpenScope(NavigationPreparationKind.Decode,1,2,out var foreign));Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,2,out var ours));Assert.Throws<ArgumentException>(()=>NavigationCheckpointAliases.CreateOwned(ours,foreign.OwnedReservation()));ours.Discard();foreign.Discard();
            compiler.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)] public void ColdFieldReconstructionKeepsOneLedgerAndCanonicalGraphTraceAliases(bool partition)
        {
            ILayeredNavigationProvider provider;if(partition){var map=ThreeCrossingsMap.Default;provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);}else provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,Array.Empty<NavObstacle>(),1));
            var budget=new NavigationRetentionBudget();var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('a',64),"distance",1,partition?16:1);var limits=new NavGraphCompileLimits(2000,2000,10000);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var graphScope));var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,clearance,profile,limits,graphScope.OwnedReservation());graphScope.Attach(compiler);int steps=0;while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready){Assert.Less(++steps,20000);compiler.Advance(1);}compiler.SealPrepared();graphScope.Commit(compiler,_=>{});var graph=compiler.Graph;
            var goal=graph.Nodes[graph.Nodes.Count/2].Location;var region=new NavTerminalRegion("cold-owned-field",1,goal,new[]{goal});
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var initial));var source=NavSharedTerminalField.CopyRegionAndCreateOwned(graph,region,4096,initial.OwnedReservation());initial.Attach(source);for(int n=0;n<80&&source.Status==NavSolveStatus.Pending;n++)source.Advance(1);source.SealPrepared();initial.Commit(source,_=>{});
            byte[] Bytes(NavSharedTerminalField field){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Field(field.Save());return stream.ToArray();}}
            var bytes=Bytes(source);source.ReleaseStorage();source=null;long baseline=budget.RetainedBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var coldScope));Assert.True(coldScope.TryReserve(bytes.Length));NavSharedTerminalField.Checkpoint decoded;
            using(var stream=new System.IO.MemoryStream(bytes))using(var reader=new System.IO.BinaryReader(stream)){decoded=new NavigationPrimitiveWire.Reader(reader,_=>null,_=>null,coldScope).Field();Assert.AreEqual(stream.Length,stream.Position);}
            long staging=budget.HeldBytes;var counter=new RefusingAllocator(coldScope,int.MaxValue);var cold=NavSharedTerminalField.CopyRegionAndRestoreOwned(graph,region,4096,decoded,new NavStorageReservation(counter.Reserve,counter));coldScope.Attach(cold);int calls=counter.Calls;Assert.GreaterOrEqual(budget.HeldBytes,staging);CollectionAssert.AreEqual(bytes,Bytes(cold));
            foreach(var page in cold.Save().PackedPages)for(int n=0;n<page.Flags.Length;n++)if((page.Flags[n]&2)!=0){var arc=page.Next[n];Assert.AreSame(graph.ResolveCheckpointArc(arc).Trace,arc.Trace);}
            cold.SealPrepared();coldScope.Commit(cold,_=>{});Assert.AreEqual(Unique(compiler,cold),budget.PhysicalBytes);cold.ReleaseStorage();Assert.AreEqual(baseline,budget.RetainedBytes);
            for(int nth=1;nth<=calls;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,3,out var rejected));counter=new RefusingAllocator(rejected,nth);Assert.Throws<InvalidOperationException>(()=>NavSharedTerminalField.CopyRegionAndRestoreOwned(graph,region,4096,decoded,new NavStorageReservation(counter.Reserve,counter)));Assert.AreEqual(nth,counter.Calls);rejected.Discard();Assert.AreEqual(baseline,budget.RetainedBytes);
            }
            var foreign=new NavigationRetentionBudget();Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Reconstruction,1,3,out var wrong));Assert.Throws<ArgumentException>(()=>NavSharedTerminalField.CopyRegionAndRestoreOwned(graph,region,4096,decoded,wrong.OwnedReservation()));wrong.Discard();Assert.AreEqual(NavigationRetentionBudget.RootBytes,foreign.RetainedBytes);
            compiler.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)]
        public void CompilerDecodeAndOwnedReconstructionShareOneScopeAndEveryNthRefusal(bool partition,bool complete)
        {
            ILayeredNavigationProvider Provider(){if(!partition)return new StackedNavigationFixture().Provider;var map=ThreeCrossingsMap.Default;return new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);}
            var provider=Provider();var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('f',64),"distance",1,partition?16:4);var limits=new NavGraphCompileLimits(2000,2000,10000);
            var sourceBudget=new NavigationRetentionBudget();Assert.True(sourceBudget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));var source=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,clearance,profile,limits,initial.OwnedReservation());initial.Attach(source);int steps=0;
            while(complete?source.Phase!=LayeredNavigationCompileWork.Stage.Ready:source.Phase!=LayeredNavigationCompileWork.Stage.GridEdges){Assert.Less(++steps,20000);source.Advance(1);}
            if(!complete)source.Advance(3);source.SealPrepared();initial.Commit(source,_=>{});
            byte[] Bytes(LayeredNavigationCompileWork work){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(work.Save());return stream.ToArray();}}
            var bytes=Bytes(source);source.ReleaseStorage();Assert.AreEqual(0,sourceBudget.PhysicalBytes);source=null;provider=null;
            var budget=new NavigationRetentionBudget();provider=Provider();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));Assert.True(scope.TryReserve(bytes.Length));
            LayeredNavigationCompileWork.Checkpoint decoded;
            using(var stream=new System.IO.MemoryStream(bytes))using(var reader=new System.IO.BinaryReader(stream)){decoded=new NavigationPrimitiveWire.Reader(reader,_=>null,_=>null,scope).Compile();Assert.AreEqual(stream.Length,stream.Position);}
            long staging=budget.HeldBytes;var counter=new RefusingAllocator(scope,int.MaxValue);var reservation=new NavStorageReservation(counter.Reserve,counter);var restored=LayeredNavigationCompileWork.CopyProviderAndRestoreOwned(provider,clearance,profile,limits,decoded,reservation);scope.Attach(restored);int calls=counter.Calls;
            CollectionAssert.AreEqual(bytes,Bytes(restored));var saved=restored.Save();var canonicalProvider=restored.PhysicalProvider;Assert.True(scope.Owns(((INavStorageFacade)canonicalProvider).StorageRoot));
            if(!partition){foreach(var sample in saved.Samples)Assert.AreSame(canonicalProvider.Surfaces.Single(surface=>surface.Id==sample.SurfaceId).Id,sample.SurfaceId);foreach(var arc in saved.PendingArcs.Concat(saved.MaterializedArcs))if(arc.PortalId!=null)Assert.AreSame(canonicalProvider.Portals.Single(portal=>portal.Id==arc.PortalId).Id,arc.PortalId);}
            else foreach(var trace in saved.Partition.Traces)foreach(var leg in trace)if(leg.PortalId!=null)Assert.AreSame(((PartitionNavigationProvider)canonicalProvider).Seams.Single(seam=>seam.Id==leg.PortalId).Id,leg.PortalId);
            Assert.GreaterOrEqual(budget.HeldBytes,staging,"Decoded staging retains its credit through physical reconstruction.");restored.SealPrepared();scope.Commit(restored,_=>{});Assert.AreEqual(Unique(restored),budget.PhysicalBytes);restored.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
            for(int nth=1;nth<=calls;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var rejected));counter=new RefusingAllocator(rejected,nth);
                Assert.Throws<InvalidOperationException>(()=>LayeredNavigationCompileWork.CopyProviderAndRestoreOwned(provider,clearance,profile,limits,decoded,new NavStorageReservation(counter.Reserve,counter)));
                Assert.AreEqual(nth,counter.Calls);rejected.Discard();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
            }
        }
        [Test] public void PackedCellOwnsTraceAcrossCowReplacementAndOldRootRetirement()
        {
            var map=ThreeCrossingsMap.Default;var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);
            var clearance=new NavClearanceProfile("explorer",1,"ground",1,.58);Assert.True(provider.TryLocate(new NavPoint(-18,0),clearance,out var start));var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));Assert.True(provider.TryTraceOwned(start,new NavPoint(18,0),clearance,out _,out var trace,initial.OwnedReservation()));
            var traceRoot=(NavOwnedStorage)trace;var source=NavPackedFieldStorage.Create(64,initial.TryReserve);initial.Attach(source);source.Set(0,36,new NavGraphArc(0,1,36,NavGraphArcKind.Surface,null,trace));traceRoot.ReleaseOwner();source.SealPrepared();initial.Commit(source,_=>{});
            Assert.AreEqual(1,traceRoot.References);Assert.AreEqual(Unique(source),budget.PhysicalBytes);source.Retain();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));var candidate=source.ForkPrepared(scope.TryReserve);scope.Attach(candidate);candidate.Set(0,35,new NavGraphArc(0,2,35,NavGraphArcKind.Surface,null));candidate.SealPrepared();scope.Commit(candidate,_=>{});
            Assert.AreEqual(1,traceRoot.References);Assert.True(source.TryNext(0,out var prior));Assert.AreSame(trace,prior.Trace);Assert.AreEqual(Unique(source,candidate),budget.PhysicalBytes);
            source.ReleaseStorage();Assert.AreEqual(1,traceRoot.References);source.Release();Assert.AreEqual(0,traceRoot.References);Assert.AreEqual(Unique(candidate),budget.PhysicalBytes);
            candidate.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [Test] public void MaxUnicodeTerminalDigestAdmitsActualBufferBoundBeforeCloneAndEveryNthRefusal()
        {
            var surface=new string('界',256);var id=new string('漢',256);var at=new NavLocation(new NavPoint(0,0),surface);
            var source=new NavTerminalRegion(id,1,at,Enumerable.Range(0,64).Select(n=>new NavLocation(new NavPoint(n,0),surface)));
            byte[] encoded;using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true)){writer.Write(source.Id);writer.Write(source.Revision);NavContract.Location(writer,source.Requested);writer.Write(source.Candidates.Count);foreach(var point in source.Candidates)NavContract.Location(writer,point);}encoded=stream.ToArray();}
            long scratch=NavTerminalRegion.CopyScratchBytes(source);Assert.Greater(encoded.Length,50000);Assert.Greater(scratch,2L*encoded.Length+65536L,"Must cover digest growing backing buffers plus ToArray; retained UTF16 strings are a separate charge.");
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));var counter=new RefusingAllocator(scope,int.MaxValue);var copy=NavTerminalRegion.CopyOwned(source,new NavStorageReservation(counter.Reserve,counter));scope.Attach(copy);int calls=counter.Calls;long rootBytes=copy.StorageRoot.StorageBytes;
            Assert.AreEqual(source.StableKey,copy.StableKey);CollectionAssert.AreEqual(source.Candidates,copy.Candidates);Assert.AreNotSame(source.Candidates,copy.Candidates);Assert.GreaterOrEqual(budget.PeakRetainedBytes,NavigationRetentionBudget.RootBytes+NavigationRetentionBudget.ScopeBytes+NavStorageScratch.LeaseBytes+scratch+rootBytes);scope.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
            for(int nth=1;nth<=calls;nth++){
                var failed=new NavigationRetentionBudget();Assert.True(failed.TryOpenScope(NavigationPreparationKind.Admission,1,nth,out var operation));var refusing=new RefusingAllocator(operation,nth);
                Assert.Throws<InvalidOperationException>(()=>NavTerminalRegion.CopyOwned(source,new NavStorageReservation(refusing.Reserve,refusing)));Assert.AreEqual(nth,refusing.Calls);operation.Discard();Assert.Zero(failed.PhysicalBytes);Assert.Zero(failed.HeldBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,failed.RetainedBytes);
            }
            long baseBytes=LayeredNavigationScheduler.MaxRetainedBytes-NavigationRetentionBudget.RootBytes-NavigationRetentionBudget.ScopeBytes-NavStorageScratch.LeaseBytes-scratch-rootBytes+1;
            var nearCap=new NavigationRetentionBudget(baseBytes);Assert.True(nearCap.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var refused));
            Assert.Throws<InvalidOperationException>(()=>NavTerminalRegion.CopyOwned(source,refused.OwnedReservation()));Assert.Zero(nearCap.PhysicalBytes);Assert.LessOrEqual(nearCap.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);refused.Discard();Assert.Zero(nearCap.HeldBytes);Assert.AreEqual(baseBytes+NavigationRetentionBudget.RootBytes,nearCap.RetainedBytes);Assert.AreEqual(id,source.Id);Assert.AreEqual(surface,source.Requested.SurfaceId);
        }
        [Test] public void ActualGraphOnlyWarmCacheRetainsOwnedRecipeMetadataThroughEvictionAndCapturePin()
        {
            var source=SmallRecipe(out var input);source=new NavScheduledRequest(input,source.Clearance,source.Profile,new NavGraphCompileLimits(256,256,2048),source.Region,source.Subscription,512);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));var provider=LayeredNavigationProvider.CopyOwned(input,scope.OwnedReservation());var recipe=NavScheduledRequest.CopyOwned(source,provider,scope.OwnedReservation());scope.Attach(recipe);scope.Commit(recipe,_=>{});
            var scheduler=new LayeredNavigationScheduler();Assert.True(scheduler.Submit(recipe));var root=recipe.StorageRoot;root.ReleaseOwner();provider.StorageRoot.ReleaseOwner();
            var quota=new NavEpochQuota(10000,10000,10000);for(int epoch=0;epoch<2000&&scheduler.ReadyCount==0;epoch++)scheduler.Advance(epoch,quota,_=>true);
            Assert.True(scheduler.TryTake(_=>true,out var completion));Assert.AreEqual(NavSolveStatus.Ready,completion.Status);completion.Release();Assert.Zero(scheduler.PendingCount);Assert.AreEqual(1,scheduler.Counters.CacheFields);
            var evict=typeof(LayeredNavigationScheduler).GetMethod("EvictOne",BindingFlags.Instance|BindingFlags.NonPublic);Assert.True((bool)evict.Invoke(scheduler,null));Assert.Zero(scheduler.Counters.CacheFields);Assert.AreEqual(4,root.References,"Cached graph, completed compiler, cached recipe and independently owned compile-key selector retain the recipe aggregate after subscriber and field drops.");Assert.AreEqual(Unique(root),budget.PhysicalBytes);
            root.Retain();Assert.True((bool)evict.Invoke(scheduler,null));Assert.AreEqual(1,root.References);Assert.AreEqual(Unique(root),budget.PhysicalBytes);scheduler.ClearWorld();root.Release();Assert.Zero(budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
        }
        [Test] public void OwnedProviderIndexKeepsEqualBindingIdentityAndPinnedSourceAcrossLastReferenceDrops()
        {
            SmallRecipe(out var input);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var initial));
            var first=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());var second=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());Assert.AreEqual(first.Binding,second.Binding);Assert.AreNotSame(first,second);
            var index=NavProviderReferences.CreateOwned(initial.OwnedReservation());initial.Attach(index);index.Add(first,10);index.Add(second,20);index.Add(first,10);first.StorageRoot.ReleaseOwner();second.StorageRoot.ReleaseOwner();index.Seal();initial.Commit(index,_=>{});Assert.AreEqual(2,index.Count);Assert.AreEqual(30,index.DiagnosticBytes);Assert.AreEqual(Unique(index),budget.PhysicalBytes);index.Retain();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));var next=index.ForkOwned(scope.OwnedReservation());scope.Attach(next);next.Remove(first);Assert.True(next.Contains(first));next.Remove(first);Assert.False(next.Contains(first));Assert.True(next.Contains(second));next.Remove(second);next.Seal();scope.Commit(next,_=>{});
            Assert.AreEqual(2,index.Count);Assert.True(index.Contains(first));Assert.True(index.Contains(second));Assert.Zero(next.Count);Assert.Zero(next.DiagnosticBytes);Assert.AreEqual(Unique(index,next),budget.PhysicalBytes);
            index.ReleaseStorage();Assert.Greater(first.StorageRoot.References,0);index.Release();Assert.Zero(first.StorageRoot.References);Assert.Zero(second.StorageRoot.References);Assert.AreEqual(Unique(next),budget.PhysicalBytes);next.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void OwnedProviderIndexForeignPayloadRejectsBeforeEntryOrBucketAdmission()
        {
            SmallRecipe(out var input);var a=new NavigationRetentionBudget();Assert.True(a.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var source));var provider=LayeredNavigationProvider.CopyOwned(input,source.OwnedReservation());source.Attach(provider);source.Commit(provider,_=>{});
            var b=new NavigationRetentionBudget();Assert.True(b.TryOpenScope(NavigationPreparationKind.Admission,2,1,out var scope));var index=NavProviderReferences.CreateOwned(scope.OwnedReservation());scope.Attach(index);long physical=b.PhysicalBytes,held=scope.HeldBytes;int references=provider.StorageRoot.References;
            Assert.Throws<ArgumentException>(()=>index.Add(provider,10));Assert.AreEqual(physical,b.PhysicalBytes);Assert.AreEqual(held,scope.HeldBytes);Assert.AreEqual(references,provider.StorageRoot.References);Assert.Zero(index.Count);scope.Discard();Assert.Zero(b.PhysicalBytes);provider.StorageRoot.ReleaseOwner();Assert.Zero(a.PhysicalBytes);
        }
        [Test] public void EveryNthProviderIndexMutationRefusalRequiresDiscardAndPreservesOldIdentityCounts()
        {
            SmallRecipe(out var input);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var initial));var provider=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());var index=NavProviderReferences.CreateOwned(initial.OwnedReservation());initial.Attach(index);index.Add(provider,10);provider.StorageRoot.ReleaseOwner();index.Seal();initial.Commit(index,_=>{});long baseline=budget.PhysicalBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var countScope));var counter=new RefusingAllocator(countScope,int.MaxValue);var count=index.ForkOwned(new NavStorageReservation(counter.Reserve,counter));countScope.Attach(count);count.Add(provider,10);count.Remove(provider);count.Remove(provider);int maximum=counter.Calls;countScope.Discard();Assert.AreEqual(baseline,budget.PhysicalBytes);
            for(int nth=1;nth<=maximum;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,nth+2,out var scope));var refusing=new RefusingAllocator(scope,nth);NavProviderReferences candidate=null;
                try{candidate=index.ForkOwned(new NavStorageReservation(refusing.Reserve,refusing));scope.Attach(candidate);candidate.Add(provider,10);candidate.Remove(provider);candidate.Remove(provider);Assert.Fail("Expected refusal.");}
                catch(InvalidOperationException){Assert.AreEqual(nth,refusing.Calls);if(candidate!=null)Assert.Throws<InvalidOperationException>(()=>candidate.Seal());}
                finally{scope.Discard();}
                Assert.AreEqual(1,index.Count);Assert.AreEqual(10,index.DiagnosticBytes);Assert.True(index.Contains(provider));Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
            }
            index.ReleaseStorage();Assert.Zero(provider.StorageRoot.References);Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void PersistentIndexMatchesRealDictionarySlotReuseAndPinnedPhysicalForest()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var initial));
            var index=NavPersistentIndex<int,int>.CreateOwned(initial.OwnedReservation());initial.Attach(index);var expected=new Dictionary<int,int>();
            for(int n=0;n<40;n++){index.Add(n,n*3);expected.Add(n,n*3);}foreach(int n in new[]{3,19,7,22,0}){Assert.True(index.Remove(n));expected.Remove(n);}
            for(int n=40;n<47;n++){index.Add(n,n*3);expected.Add(n,n*3);}index.Add(3,90);expected.Add(3,90);
            CollectionAssert.AreEqual(expected.ToArray(),index.ToArray(),"Execute the actual runtime Dictionary as the ordering oracle.");
            index.Seal();initial.Commit(index,_=>{});Assert.AreEqual(Unique(index),budget.PhysicalBytes);var before=index.ToArray();index.Retain();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));var fork=index.ForkOwned(scope.OwnedReservation());scope.Attach(fork);
            foreach(int n in new[]{40,1,46}){fork.Remove(n);expected.Remove(n);}foreach(int n in new[]{90,91,1}){fork.Add(n,n*3);expected.Add(n,n*3);}
            CollectionAssert.AreEqual(expected.ToArray(),fork.ToArray());CollectionAssert.AreEqual(before,index.ToArray());fork.Seal();scope.Commit(fork,_=>{});index.ReleaseStorage();Assert.AreEqual(Unique(index,fork),budget.PhysicalBytes);
            index.Release();Assert.AreEqual(Unique(fork),budget.PhysicalBytes);fork.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
        }
        [Test] public void PersistentIndexEveryNthRefusalLeavesExactSlotsAndFreeListInPinnedSource()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var initial));var index=NavPersistentIndex<int,int>.CreateOwned(initial.OwnedReservation());initial.Attach(index);
            for(int n=0;n<8;n++)index.Add(n,n);index.Remove(2);index.Remove(5);index.Seal();initial.Commit(index,_=>{});long baseline=budget.PhysicalBytes;var before=index.ToArray();
            void Mutate(NavPersistentIndex<int,int> candidate){candidate.Add(12,12);candidate.Remove(0);candidate.Add(5,50);candidate.Clear();candidate.Add(2,22);candidate.Seal();}
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var counted));var count=new RefusingAllocator(counted,int.MaxValue);var trial=index.ForkOwned(new NavStorageReservation(count.Reserve,count));counted.Attach(trial);Mutate(trial);int maximum=count.Calls;counted.Discard();Assert.AreEqual(baseline,budget.PhysicalBytes);
            for(int nth=1;nth<=maximum;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,nth+2,out var scope));var refusing=new RefusingAllocator(scope,nth);NavPersistentIndex<int,int> candidate=null;
                try{candidate=index.ForkOwned(new NavStorageReservation(refusing.Reserve,refusing));scope.Attach(candidate);Mutate(candidate);Assert.Fail("Expected refusal.");}
                catch(InvalidOperationException){Assert.AreEqual(nth,refusing.Calls);if(candidate!=null)Assert.Throws<InvalidOperationException>(()=>candidate.Seal());}
                finally{scope.Discard();}
                CollectionAssert.AreEqual(before,index.ToArray());Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
            }
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,maximum+3,out var next));var restored=index.ForkOwned(next.OwnedReservation());next.Attach(restored);restored.Add(12,12);CollectionAssert.AreEqual(new[]{0,1,3,4,12,6,7},restored.Keys.ToArray(),"Two removals reuse slot5 before slot2.");next.Discard();index.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void PersistentStringIndexOwnsRecipeAndRejectsForeignBeforeAdmission()
        {
            var input=SmallRecipe(out var source);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var initial));var provider=LayeredNavigationProvider.CopyOwned(source,initial.OwnedReservation());var recipe=NavScheduledRequest.CopyOwned(input,provider,initial.OwnedReservation());var index=NavPersistentIndex<string,long>.CreateOwned(initial.OwnedReservation(),StringComparer.Ordinal);initial.Attach(index);index.Add(recipe.Subscription.StableKey,8,recipe.StorageRoot);recipe.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();index.Seal();initial.Commit(index,_=>{});Assert.AreEqual(Unique(index),budget.PhysicalBytes);
            var foreign=new NavigationRetentionBudget();Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Admission,2,1,out var other));long held=other.HeldBytes;Assert.Throws<ArgumentException>(()=>index.ForkOwned(other.OwnedReservation()));Assert.AreEqual(held,other.HeldBytes);Assert.Zero(foreign.PhysicalBytes);other.Discard();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var prepare));var fork=index.ForkOwned(prepare.OwnedReservation());prepare.Attach(fork);long physical=budget.PhysicalBytes,credits=prepare.HeldBytes;
            Assert.Throws<ArgumentException>(()=>fork.Add("unowned",9));Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(credits,prepare.HeldBytes);
            fork.Remove(recipe.Subscription.StableKey);fork.Seal();prepare.Commit(fork,_=>{});Assert.Greater(recipe.StorageRoot.References,0,"Old index still owns metadata.");index.ReleaseStorage();Assert.Zero(recipe.StorageRoot.References);Assert.AreEqual(Unique(fork),budget.PhysicalBytes);fork.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);
        }
        private static LayeredNavigationScheduler OwnedScheduler(out NavigationRetentionBudget budget,out NavScheduledRequest publicRecipe,bool attempt=false)
        {
            var input=SmallRecipe(out var provider);publicRecipe=new NavScheduledRequest(provider,input.Clearance,input.Profile,new NavGraphCompileLimits(256,256,2048),input.Region,input.Subscription,512);
            budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));var owned=LayeredNavigationProvider.CopyOwned(provider,scope.OwnedReservation());var recipe=NavScheduledRequest.CopyOwned(publicRecipe,owned,scope.OwnedReservation());var root=LayeredNavigationScheduler.CreateOwned(scope.OwnedReservation());scope.Attach(root);if(attempt)root.BindAttemptArtifacts(new string('c',64));Assert.True(root.Submit(recipe));recipe.StorageRoot.ReleaseOwner();owned.StorageRoot.ReleaseOwner();root.Advance(0,new NavEpochQuota(2,2,2),_=>true,2);root.SealPrepared();scope.Commit(root,_=>{});return root;
        }
        [Test] public void ActualOwnedSchedulerRootForkKeepsQuotaAndExactContinuationThroughGraphFieldAndResult()
        {
            var root=OwnedScheduler(out var budget,out var input);var reference=new LayeredNavigationScheduler();Assert.True(reference.Submit(input));reference.Advance(0,new NavEpochQuota(2,2,2),_=>true,2);Assert.AreEqual(reference.LogicalContinuationBinding,root.LogicalContinuationBinding);Assert.AreEqual(Unique(root),budget.PhysicalBytes);
            Assert.Throws<InvalidOperationException>(()=>root.Advance(0,new NavEpochQuota(100,100,100),_=>true));Assert.Throws<InvalidOperationException>(()=>root.Cancel(input.Subscription.StableKey));
            var pin=root;pin.Retain();var pinnedBinding=pin.LogicalContinuationBinding;int operations=0;
            for(int epoch=1;epoch<500&&root.ReadyCount==0;epoch++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,epoch+1,out var scope));var next=root.ForkOwned(scope.OwnedReservation());scope.Attach(next);var quota=new NavEpochQuota(64,64,64);
                int expected=reference.Advance(epoch,quota,_=>true,64);Assert.AreEqual(expected,next.Advance(epoch,quota,_=>true,64));Assert.AreEqual(reference.LogicalContinuationBinding,next.LogicalContinuationBinding);next.SealPrepared();scope.Commit(next,_=>{});root.ReleaseStorage();root=next;operations++;
                Assert.AreEqual(pinnedBinding,pin.LogicalContinuationBinding);Assert.AreEqual(Unique(pin,root),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
            }
            Assert.Greater(operations,2);Assert.AreEqual(1,root.ReadyCount);Assert.AreEqual(1,root.Counters.GraphBuilds);Assert.AreEqual(1,root.Counters.FieldBuilds);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,501,out var transfer));var final=root.ForkOwned(transfer.OwnedReservation());transfer.Attach(final);Assert.True(final.TryTake(_=>true,out var actual));Assert.True(reference.TryTake(_=>true,out var expectedAnswer));Assert.AreEqual(expectedAnswer.Status,actual.Status);Assert.AreEqual(NavSolveStatus.Ready,actual.Status);Assert.AreEqual(expectedAnswer.Route.Cost,actual.Route.Cost);CollectionAssert.AreEqual(expectedAnswer.Route.Legs.Select(l=>l.To),actual.Route.Legs.Select(l=>l.To));actual.Release();expectedAnswer.Release();final.ClearWorld();final.SealPrepared();transfer.Commit(final,_=>{});root.ReleaseStorage();pin.Release();reference.ClearWorld();Assert.AreEqual(Unique(final),budget.PhysicalBytes);final.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void ActualOwnedSchedulerEveryNthPreparedRootAndJobFailureReleasesAllBirths()
        {
            var root=OwnedScheduler(out var budget,out var input);long baseline=budget.PhysicalBytes;var binding=root.LogicalContinuationBinding;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var counted));var count=new RefusingAllocator(counted,int.MaxValue);var trial=root.ForkOwned(new NavStorageReservation(count.Reserve,count));counted.Attach(trial);trial.Advance(1,new NavEpochQuota(16,16,16),_=>true,16);trial.SealPrepared();int maximum=count.Calls;counted.Discard();Assert.AreEqual(baseline,budget.PhysicalBytes);
            for(int nth=1;nth<=maximum;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,nth+2,out var scope));var refusing=new RefusingAllocator(scope,nth);LayeredNavigationScheduler candidate=null;
                try{candidate=root.ForkOwned(new NavStorageReservation(refusing.Reserve,refusing));scope.Attach(candidate);candidate.Advance(1,new NavEpochQuota(16,16,16),_=>true,16);candidate.SealPrepared();Assert.Fail("Expected refusal.");}
                catch(InvalidOperationException){Assert.AreEqual(nth,refusing.Calls);if(candidate!=null)Assert.Throws<InvalidOperationException>(()=>candidate.SealPrepared());}
                finally{scope.Discard();}
                Assert.AreEqual(binding,root.LogicalContinuationBinding);Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
            var foreign=new NavigationRetentionBudget();Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Solve,2,1,out var other));long held=other.HeldBytes;Assert.Throws<ArgumentException>(()=>root.ForkOwned(other.OwnedReservation()));Assert.AreEqual(held,other.HeldBytes);Assert.Zero(foreign.PhysicalBytes);other.Discard();root.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void ActualOwnedAttemptPinsCopiedBindingAndOriginCertificateThroughWarmCacheAndClear()
        {
            var root=OwnedScheduler(out var budget,out var input,true);var reference=new LayeredNavigationScheduler();reference.BindAttemptArtifacts(new string('c',64));Assert.True(reference.Submit(input));reference.Advance(0,new NavEpochQuota(2,2,2),_=>true,2);
            root.Retain();var pin=root;string binding=pin.LogicalContinuationBinding;int epoch=1;
            for(;epoch<500&&root.ReadyCount==0;epoch++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,epoch+1,out var scope));var next=root.ForkOwned(scope.OwnedReservation());scope.Attach(next);var quota=new NavEpochQuota(64,64,64);Assert.AreEqual(reference.Advance(epoch,quota,_=>true,64),next.Advance(epoch,quota,_=>true,64));Assert.AreEqual(reference.LogicalContinuationBinding,next.LogicalContinuationBinding);next.SealPrepared();scope.Commit(next,_=>{});root.ReleaseStorage();root=next;Assert.AreEqual(Unique(root,pin),budget.PhysicalBytes);Assert.AreEqual(binding,pin.LogicalContinuationBinding);
            }
            Assert.AreEqual(1,root.ReadyCount);Assert.True(root.HasAttemptOwner);Assert.AreEqual(new string('c',64),root.AttemptBinding);Assert.AreEqual(1,root.Counters.GraphBuilds);Assert.AreEqual(1,root.Counters.FieldBuilds);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,epoch+1,out var last));var final=root.ForkOwned(last.OwnedReservation());last.Attach(final);Assert.True(final.TryTake(_=>true,out var answer));Assert.True(reference.TryTake(_=>true,out var expected));Assert.AreEqual(NavSolveStatus.Ready,answer.Status);Assert.AreEqual(expected.Route.Cost,answer.Route.Cost);answer.Release();expected.Release();Assert.True(final.EndAttemptArtifacts(epoch,new NavEpochQuota(64,64,64)));Assert.True(reference.EndAttemptArtifacts(epoch,new NavEpochQuota(64,64,64)));Assert.AreEqual(reference.LogicalContinuationBinding,final.LogicalContinuationBinding);Assert.False(final.HasAttemptOwner);final.ClearWorld();final.SealPrepared();last.Commit(final,_=>{});root.ReleaseStorage();pin.Release();reference.ClearWorld();Assert.AreEqual(Unique(final),budget.PhysicalBytes);final.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
        }
        [Test] public void OwnedAttemptBindingEveryNthStringAndScratchRefusalPreservesPinnedSource()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var initial));var root=LayeredNavigationScheduler.CreateOwned(initial.OwnedReservation());initial.Attach(root);root.SealPrepared();initial.Commit(root,_=>{});long baseline=budget.PhysicalBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,2,out var counted));var count=new RefusingAllocator(counted,int.MaxValue);var trial=root.ForkOwned(new NavStorageReservation(count.Reserve,count));counted.Attach(trial);trial.BindAttemptArtifacts(new string('d',64));trial.SealPrepared();int maximum=count.Calls;counted.Discard();Assert.AreEqual(baseline,budget.PhysicalBytes);
            for(int nth=1;nth<=maximum;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,nth+2,out var scope));var refusing=new RefusingAllocator(scope,nth);LayeredNavigationScheduler candidate=null;
                try{candidate=root.ForkOwned(new NavStorageReservation(refusing.Reserve,refusing));scope.Attach(candidate);candidate.BindAttemptArtifacts(new string('d',64));candidate.SealPrepared();Assert.Fail("Expected refusal.");}
                catch(InvalidOperationException){Assert.AreEqual(nth,refusing.Calls);if(candidate!=null)Assert.Throws<InvalidOperationException>(()=>candidate.SealPrepared());}
                finally{scope.Discard();}
                Assert.Null(root.AttemptBinding);Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
            root.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void ActualOwnedOriginDigestPinSurvivesCompletionAndReleasesOnLastCaptureRetirement()
        {
            var root=OwnedScheduler(out var budget,out var input,true);int epoch=1;
            for(;epoch<500&&root.Counters.GraphBuilds==0;epoch++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,epoch+1,out var scope));var next=root.ForkOwned(scope.OwnedReservation());scope.Attach(next);next.Advance(epoch,new NavEpochQuota(64,0,0),_=>true,64);next.SealPrepared();scope.Commit(next,_=>{});root.ReleaseStorage();root=next;
            }
            Assert.AreEqual(1,root.Counters.GraphBuilds);Assert.Zero(root.ReadyCount);
            var jobs=typeof(LayeredNavigationScheduler).GetField("jobs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(root);var job=jobs.GetType().GetProperty("First",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(jobs);
            var origin=(NavOwnedText)job.GetType().GetField("originMetadata",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(job);Assert.NotNull(origin);Assert.AreEqual(job.GetType().GetField("OriginBinding",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(job),origin.Value);Assert.Greater(origin.References,0);
            root.Retain();var pin=root;string binding=pin.LogicalContinuationBinding;
            for(;epoch<1000&&root.ReadyCount==0;epoch++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,epoch+1,out var scope));var next=root.ForkOwned(scope.OwnedReservation());scope.Attach(next);next.Advance(epoch,new NavEpochQuota(64,64,64),_=>true,64);next.SealPrepared();scope.Commit(next,_=>{});root.ReleaseStorage();root=next;Assert.AreEqual(binding,pin.LogicalContinuationBinding);Assert.AreEqual(Unique(pin,root),budget.PhysicalBytes);
            }
            Assert.AreEqual(1,root.ReadyCount);Assert.Greater(origin.References,0,"Retired job's derived origin metadata remains alive through capture pin.");pin.Release();Assert.Zero(origin.References);Assert.AreEqual(Unique(root),budget.PhysicalBytes);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,epoch+1,out var last));var final=root.ForkOwned(last.OwnedReservation());last.Attach(final);final.ClearWorld();final.SealPrepared();last.Commit(final,_=>{});root.ReleaseStorage();final.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
        }
        [Test] public void IntrusiveCaptureIdsResetPerSnapshotAndChargeNoPerNodeIdentityMap()
        {
            var root=OwnedScheduler(out var budget,out var input);var rootTag=new NavPhysicalTag(NavPhysicalStorageKind.Scheduler);var queueTag=new NavPhysicalTag(NavPhysicalStorageKind.Queue,value:NavPhysicalValueKind.Job);var queue=(NavOwnedStorage)typeof(LayeredNavigationScheduler).GetField("jobs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(root);long baseline=budget.PhysicalBytes;
            for(int capture=0;capture<3;capture++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,capture+2,out var scope));var index=NavPhysicalCaptureIndex.CreateOwned(root,scope.OwnedReservation());scope.Attach(index);Assert.True(budget.CaptureActive);long physical=budget.PhysicalBytes;
                Assert.AreEqual(baseline+128+NavOwnedStorage.CaptureHeaderBytes,physical);Assert.True(index.Begin(root,rootTag,out int rootId));Assert.Zero(rootId);Assert.True(index.Begin(queue,queueTag,out int queueId));Assert.AreEqual(1,queueId);index.Complete(queue,queueTag);Assert.False(index.Begin(queue,queueTag,out int alias));Assert.AreEqual(queueId,alias);index.Complete(root,rootTag);Assert.AreEqual(2,index.Finish());Assert.AreEqual(physical,budget.PhysicalBytes,"Touching headers creates no per-node index allocation.");Assert.AreEqual(Unique(root,index),budget.PhysicalBytes);scope.Discard();Assert.False(budget.CaptureActive);Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,capture+10,out var history));var unrelated=NavTypedLeg.CreateOwned(new NavLocation(new NavPoint(0,0),"floor"),new NavLocation(new NavPoint(1,0),"floor"),1,NavTypedLegKind.Surface,null,history.OwnedReservation());history.Attach(unrelated);history.Discard();Assert.AreEqual(baseline,budget.PhysicalBytes);
            }
            root.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void CaptureRejectsForeignMutableAndBusyRootsBeforeAllocationThenRejectsCycleAndKind()
        {
            var root=OwnedScheduler(out var budget,out var input);Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var preparation));var mutable=root.ForkOwned(preparation.OwnedReservation());preparation.Attach(mutable);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,3,out var rejected));long physical=budget.PhysicalBytes,held=rejected.HeldBytes;Assert.Throws<ArgumentException>(()=>NavPhysicalCaptureIndex.CreateOwned(mutable,rejected.OwnedReservation()));Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,rejected.HeldBytes);Assert.False(budget.CaptureActive);rejected.Discard();preparation.Discard();
            var foreign=new NavigationRetentionBudget();Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Capture,2,1,out var other));long foreignHeld=other.HeldBytes;Assert.Throws<ArgumentException>(()=>NavPhysicalCaptureIndex.CreateOwned(root,other.OwnedReservation()));Assert.Zero(foreign.PhysicalBytes);Assert.AreEqual(foreignHeld,other.HeldBytes);Assert.False(foreign.CaptureActive);other.Discard();
            var tag=new NavPhysicalTag(NavPhysicalStorageKind.Scheduler);
            for(int n=0;n<2;n++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,n+4,out var scope));var index=NavPhysicalCaptureIndex.CreateOwned(root,scope.OwnedReservation());scope.Attach(index);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,n+6,out var busy));physical=budget.PhysicalBytes;held=busy.HeldBytes;Assert.Throws<InvalidOperationException>(()=>NavPhysicalCaptureIndex.CreateOwned(root,busy.OwnedReservation()));Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,busy.HeldBytes);busy.Discard();Assert.True(budget.CaptureActive);
                Assert.True(index.Begin(root,tag,out _));if(n==0)Assert.Throws<ArgumentException>(()=>index.Begin(root,tag,out _));else{index.Complete(root,tag);Assert.Throws<ArgumentException>(()=>index.Begin(root,new NavPhysicalTag(NavPhysicalStorageKind.Metadata),out _));}scope.Discard();Assert.False(budget.CaptureActive);
            }
            root.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
        }
        [Test] public void EveryNthCaptureIndexHeaderRefusalReleasesLaneAndPreservesSource()
        {
            var root=OwnedScheduler(out var budget,out var input);long baseline=budget.PhysicalBytes;string binding=root.LogicalContinuationBinding;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,2,out var counted));var count=new RefusingAllocator(counted,int.MaxValue);var trial=NavPhysicalCaptureIndex.CreateOwned(root,new NavStorageReservation(count.Reserve,count));counted.Attach(trial);int maximum=count.Calls;counted.Discard();Assert.False(budget.CaptureActive);
            for(int nth=1;nth<=maximum;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,nth+2,out var scope));var refusing=new RefusingAllocator(scope,nth);Assert.Throws<InvalidOperationException>(()=>NavPhysicalCaptureIndex.CreateOwned(root,new NavStorageReservation(refusing.Reserve,refusing)));Assert.AreEqual(nth,refusing.Calls);scope.Discard();Assert.False(budget.CaptureActive);Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.AreEqual(binding,root.LogicalContinuationBinding);Assert.Zero(budget.HeldBytes);
            }
            root.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void PagedPhysicalResolutionRetainsDefinitionsRejectsHolesDuplicateKindsAndForwardCycles()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var resolution=NavPhysicalResolution.CreateOwned(2,scope.OwnedReservation());scope.Attach(resolution);var tag=new NavPhysicalTag(NavPhysicalStorageKind.Leg);var at=new NavLocation(new NavPoint(0,0),"floor");var a=NavTypedLeg.CreateOwned(at,at,0,NavTypedLegKind.Surface,null,scope.OwnedReservation());var b=NavTypedLeg.CreateOwned(at,at,0,NavTypedLegKind.Surface,null,scope.OwnedReservation());
            Assert.Throws<ArgumentException>(()=>resolution.Resolve(0,tag));resolution.Define(1,tag,a);a.ReleaseOwner();Assert.AreSame(a,resolution.Resolve(1,tag));Assert.Throws<ArgumentException>(()=>resolution.Finish());long physical=budget.PhysicalBytes,held=scope.HeldBytes;int refs=a.References;Assert.Throws<ArgumentException>(()=>resolution.Define(1,tag,b));Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,scope.HeldBytes);Assert.AreEqual(refs,a.References);Assert.Throws<ArgumentException>(()=>resolution.Resolve(1,new NavPhysicalTag(NavPhysicalStorageKind.Metadata)));
            resolution.Define(0,tag,b);b.ReleaseOwner();resolution.Finish();Assert.AreSame(b,resolution.Resolve(0,tag));Assert.AreNotSame(resolution.Resolve(0,tag),resolution.Resolve(1,tag),"Equal content is not physical alias equality.");scope.Commit(resolution,_=>{});Assert.AreEqual(Unique(resolution),budget.PhysicalBytes);resolution.ReleaseStorage();Assert.Zero(a.References);Assert.Zero(b.References);Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
        }
        [Test] public void EveryNthPagedDefinitionResolutionRefusalReleasesPayloadAndPageBirths()
        {
            void Build(NavigationRetentionBudget.Scope scope,INavStorageReservation reservation){var resolution=NavPhysicalResolution.CreateOwned(4,reservation);scope.Attach(resolution);var tag=new NavPhysicalTag(NavPhysicalStorageKind.Leg);var at=new NavLocation(new NavPoint(0,0),"floor");for(int n=3;n>=0;n--){var leg=NavTypedLeg.CreateOwned(at,at,0,NavTypedLegKind.Surface,null,reservation);try{resolution.Define(n,tag,leg);}finally{leg.ReleaseOwner();}}resolution.Finish();}
            var countedBudget=new NavigationRetentionBudget();Assert.True(countedBudget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var counted));var count=new RefusingAllocator(counted,int.MaxValue);Build(counted,new NavStorageReservation(count.Reserve,count));int maximum=count.Calls;counted.Discard();Assert.Zero(countedBudget.PhysicalBytes);
            for(int nth=1;nth<=maximum;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var refusing=new RefusingAllocator(scope,nth);Assert.Throws<InvalidOperationException>(()=>Build(scope,new NavStorageReservation(refusing.Reserve,refusing)));Assert.AreEqual(nth,refusing.Calls);scope.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
        }
        [Test] public void PhysicalResolutionRejectsSameObjectTwoIdsAndSelfBeforeAllocationOrRetain()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var resolution=NavPhysicalResolution.CreateOwned(2,scope.OwnedReservation());scope.Attach(resolution);var at=new NavLocation(new NavPoint(0,0),"floor");var leg=NavTypedLeg.CreateOwned(at,at,0,NavTypedLegKind.Surface,null,scope.OwnedReservation());var tag=new NavPhysicalTag(NavPhysicalStorageKind.Leg);resolution.Define(0,tag,leg);leg.ReleaseOwner();
            long physical=budget.PhysicalBytes,held=scope.HeldBytes;int refs=leg.References,tableRefs=resolution.References;
            Assert.Throws<ArgumentException>(()=>resolution.Define(1,tag,leg));Assert.Throws<ArgumentException>(()=>resolution.Define(1,new NavPhysicalTag(NavPhysicalStorageKind.Metadata),resolution));Assert.Throws<ArgumentException>(()=>resolution.Define(1,tag,resolution));
            Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,scope.HeldBytes);Assert.AreEqual(refs,leg.References);Assert.AreEqual(tableRefs,resolution.References);Assert.True(budget.CaptureActive);Assert.Throws<ArgumentException>(()=>resolution.Finish());scope.Discard();Assert.Zero(leg.References);Assert.Zero(resolution.References);Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
        }
        private static NavScheduledRequest SmallRecipe(out LayeredNavigationProvider provider)
        {
            var polygon=new NavPolygon(new[]{new NavPoint(-4,-4),new NavPoint(4,-4),new NavPoint(4,4),new NavPoint(-4,4)});
            var surface=new NavLayerSurface("floor","ground",0,1,new NavTraversalArea(polygon,4,Array.Empty<NavObstacle>()));
            provider=new LayeredNavigationProvider("small",1,1,1,new string('b',64),new[]{surface},Array.Empty<NavLayerPortal>());
            var c=new NavClearanceProfile("ground",1,"ground",1,.25);var p=new NavGraphProfile(new string('a',64),"distance",1,.8);
            var origin=new NavLocation(new NavPoint(-1,0),"floor");var end=new NavLocation(new NavPoint(1,0),"floor");
            var r=new NavTerminalRegion("region",1,end,new[]{end,new NavLocation(new NavPoint(1.8,0),"floor")});
            var sub=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,origin,end,"hold");
            return new NavScheduledRequest(provider,c,p,new NavGraphCompileLimits(100,100,1000),r,sub,100);
        }
        [Test] public void OwnedRecipeMetadataCopiesPublicInputsAndKeepsProviderUntilLastQueueOrCompletionEdge()
        {
            var source=SmallRecipe(out var provider);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));
            var ownedProvider=LayeredNavigationProvider.CopyOwned(provider,scope.OwnedReservation());var copy=NavScheduledRequest.CopyOwned(source,ownedProvider,scope.OwnedReservation());
            var queue=NavPersistentQueue<NavScheduledRequest>.CreateOwned(scope.OwnedReservation());scope.Attach(queue);queue.AddLast(copy);copy.StorageRoot.ReleaseOwner();((INavStorageFacade)ownedProvider).StorageRoot.ReleaseOwner();queue.Seal();scope.Commit(queue,_=>{});
            Assert.AreEqual(Unique(queue),budget.PhysicalBytes);Assert.AreNotSame(source,copy);Assert.AreNotSame(source.Clearance,copy.Clearance);Assert.AreNotSame(source.Profile,copy.Profile);Assert.AreNotSame(source.Region.Candidates,copy.Region.Candidates);Assert.AreNotSame(source.Subscription,copy.Subscription);
            Assert.AreSame(copy.StorageRoot,((INavStorageFacade)copy.Clearance).StorageRoot);Assert.AreSame(copy.Clearance,NavMetadataOwnership.Resolve(copy.StorageRoot,NavMetadataRole.Clearance));Assert.AreSame(copy.Profile,NavMetadataOwnership.Resolve(copy.StorageRoot,NavMetadataRole.GraphProfile));Assert.AreSame(copy.Limits,NavMetadataOwnership.Resolve(copy.StorageRoot,NavMetadataRole.CompileLimits));Assert.AreSame(copy.Subscription,NavMetadataOwnership.Resolve(copy.StorageRoot,NavMetadataRole.Subscription));
            Assert.AreEqual(source.ArtifactKey,copy.ArtifactKey);Assert.AreEqual(source.Subscription.StableKey,copy.Subscription.StableKey);Assert.AreSame(copy.Provider.Surfaces[0].Id,copy.Subscription.Origin.SurfaceId);
            var completion=new NavScheduledCompletion(copy,NavSolveStatus.UnreachableInGraph,null,0,1);var recipeRoot=copy.StorageRoot;queue.ReleaseStorage();Assert.AreEqual(1,recipeRoot.References);Assert.AreEqual(Unique(recipeRoot),budget.PhysicalBytes);
            completion.Release();Assert.Zero(recipeRoot.References);Assert.Zero(budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
        }
        [Test] public void OwnedRecipeForeignProviderRejectsBeforeScratchOrPrivateMetadataAllocation()
        {
            var source=SmallRecipe(out var provider);var first=new NavigationRetentionBudget();Assert.True(first.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var a));var owned=LayeredNavigationProvider.CopyOwned(provider,a.OwnedReservation());a.Attach(owned);a.Commit(owned,_=>{});
            var second=new NavigationRetentionBudget();Assert.True(second.TryOpenScope(NavigationPreparationKind.Admission,2,1,out var b));long held=b.HeldBytes,physical=second.PhysicalBytes;int refs=((INavStorageFacade)owned).StorageRoot.References;
            Assert.Throws<ArgumentException>(()=>NavScheduledRequest.CopyOwned(source,owned,b.OwnedReservation()));Assert.AreEqual(held,b.HeldBytes);Assert.AreEqual(physical,second.PhysicalBytes);Assert.AreEqual(refs,((INavStorageFacade)owned).StorageRoot.References);b.Discard();((INavStorageFacade)owned).StorageRoot.ReleaseOwner();Assert.Zero(first.PhysicalBytes);
        }
        [Test] public void EveryNthOwnedRecipeRefusalReturnsScratchAndProviderEdgesExactlyOnce()
        {
            var source=SmallRecipe(out var provider);var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var first));var owned=LayeredNavigationProvider.CopyOwned(provider,first.OwnedReservation());first.Attach(owned);first.Commit(owned,_=>{});long physical=budget.PhysicalBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,2,out var countScope));var counter=new RefusingAllocator(countScope,int.MaxValue);var counted=NavScheduledRequest.CopyOwned(source,owned,new NavStorageReservation(counter.Reserve,counter));countScope.Attach(counted);int maximum=counter.Calls;countScope.Discard();Assert.AreEqual(physical,budget.PhysicalBytes);
            for(int nth=1;nth<=maximum;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,nth+2,out var scope));var refusing=new RefusingAllocator(scope,nth);NavScheduledRequest copy=null;
                try{copy=NavScheduledRequest.CopyOwned(source,owned,new NavStorageReservation(refusing.Reserve,refusing));scope.Attach(copy);Assert.Fail("Expected refusal.");}
                catch(InvalidOperationException){Assert.AreEqual(nth,refusing.Calls);}
                finally{scope.Discard();}
                Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.AreEqual(0,budget.ActiveScopes);
            }
            ((INavStorageFacade)owned).StorageRoot.ReleaseOwner();Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void PersistentQueueNumericLinksKeepPinnedOrderAndUniquePayloadOwnership()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));
            var queue=NavPersistentQueue<NavTypedLeg>.CreateOwned(initial.OwnedReservation());initial.Attach(queue);var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);
            var leg=NavTypedLeg.CreateOwned(at,at,0,NavTypedLegKind.Surface,null,initial.OwnedReservation());
            long first=queue.AddLast(leg),second=queue.AddLast(leg),third=queue.AddLast(leg);leg.ReleaseStorage();queue.Seal();initial.Commit(queue,_=>{});
            Assert.AreEqual(Unique(queue),budget.PhysicalBytes);queue.Retain();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));var next=queue.ForkOwned(scope.OwnedReservation());scope.Attach(next);
            next.MoveLast(first);next.Remove(second);long fourth=next.AddLast(leg);Assert.Greater(fourth,third);next.Replace(third,leg);next.Seal();scope.Commit(next,_=>{});
            Assert.AreEqual(first,queue.FirstId);Assert.AreEqual(third,queue.LastId);Assert.AreEqual(3,queue.Count);
            Assert.AreEqual(third,next.FirstId);Assert.AreEqual(fourth,next.LastId);Assert.AreEqual(3,next.Count);Assert.AreEqual(Unique(queue,next),budget.PhysicalBytes);
            queue.ReleaseStorage();queue.Release();Assert.AreEqual(Unique(next),budget.PhysicalBytes);next.ReleaseStorage();Assert.AreEqual(0,leg.References);Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [Test] public void PersistentQueueForkDoesNotCopyNeighbourFrontierAndForeignForkRefusesBeforeAllocation()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));
            var queue=NavPersistentQueue<int>.CreateOwned(initial.OwnedReservation());initial.Attach(queue);for(int n=0;n<1024;n++)queue.AddLast(n);queue.Seal();initial.Commit(queue,_=>{});
            var other=new NavigationRetentionBudget();Assert.True(other.TryOpenScope(NavigationPreparationKind.Solve,2,1,out var foreign));long foreignHeld=foreign.HeldBytes;
            Assert.Throws<ArgumentException>(()=>queue.ForkOwned(foreign.OwnedReservation()));Assert.AreEqual(foreignHeld,foreign.HeldBytes);foreign.Discard();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));long before=budget.PhysicalBytes;
            var next=queue.ForkOwned(scope.OwnedReservation());scope.Attach(next);Assert.AreEqual(256+2*NavOwnedStorage.CaptureHeaderBytes,budget.PhysicalBytes-before,"Only queue and map controller roots, including capture headers, are copied.");
            next.MoveLast(next.FirstId);next.RemoveFirst();next.Seal();scope.Commit(next,_=>{});
            CollectionAssert.AreEqual(Enumerable.Range(0,1024),queue);CollectionAssert.AreEqual(Enumerable.Range(2,1022).Concat(new[]{0}),next);
            Assert.AreEqual(Unique(queue,next),budget.PhysicalBytes);queue.ReleaseStorage();next.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [Test] public void PersistentQueueEveryNthPreparedLinkRefusalPreservesPinnedSourceAndReleasesBirths()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));
            var queue=NavPersistentQueue<int>.CreateOwned(initial.OwnedReservation());initial.Attach(queue);for(int n=0;n<16;n++)queue.AddLast(n);queue.Seal();initial.Commit(queue,_=>{});long baseline=budget.PhysicalBytes;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var countScope));var count=new RefusingAllocator(countScope,int.MaxValue);
            var counted=queue.ForkOwned(new NavStorageReservation(count.Reserve,count));countScope.Attach(counted);counted.MoveLast(counted.FirstId);counted.RemoveFirst();counted.AddLast(99);int maximum=count.Calls;countScope.Discard();Assert.AreEqual(baseline,budget.PhysicalBytes);
            for(int nth=1;nth<=maximum;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,nth+2,out var scope));var refusing=new RefusingAllocator(scope,nth);NavPersistentQueue<int> candidate=null;
                try{candidate=queue.ForkOwned(new NavStorageReservation(refusing.Reserve,refusing));scope.Attach(candidate);candidate.MoveLast(candidate.FirstId);candidate.RemoveFirst();candidate.AddLast(99);Assert.Fail("Expected allocation refusal.");}
                catch(InvalidOperationException){Assert.AreEqual(nth,refusing.Calls);if(candidate!=null)Assert.Throws<InvalidOperationException>(()=>candidate.Seal());}
                finally{scope.Discard();}
                CollectionAssert.AreEqual(Enumerable.Range(0,16),queue);Assert.AreEqual(baseline,budget.PhysicalBytes);Assert.AreEqual(0,budget.HeldBytes);
            }
            queue.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        private sealed class TraceFacade : IReadOnlyList<NavTypedLeg>, INavStorageFacade
        {
            private readonly NavRetainedArray<NavTypedLeg> trace;
            internal TraceFacade(NavRetainedArray<NavTypedLeg> trace){this.trace=trace;}
            public NavOwnedStorage StorageRoot=>trace;
            public int Count=>trace.Count;
            public NavTypedLeg this[int index]=>trace[index];
            public IEnumerator<NavTypedLeg> GetEnumerator()=>trace.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        [Test] public void ArcFacadeTraceOwnsSameLedgerEdgeAcrossCowAndRejectsForeignBeforeAdmission()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));
            var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);
            var leg=NavTypedLeg.CreateOwned(at,at,0,NavTypedLegKind.Surface,null,initial.OwnedReservation());
            var trace=NavRetainedArray<NavTypedLeg>.Copy(new[]{leg},initial.TryReserve);leg.ReleaseStorage();
            var facade=new TraceFacade(trace);var source=NavPackedFieldStorage.Create(64,initial.TryReserve);initial.Attach(source);
            source.Set(0,0,new NavGraphArc(0,1,0,NavGraphArcKind.Surface,null,facade));trace.ReleaseOwner();source.SealPrepared();initial.Commit(source,_=>{});
            Assert.AreEqual(1,trace.References);Assert.AreEqual(Unique(source),budget.PhysicalBytes);source.Retain();
            var foreignBudget=new NavigationRetentionBudget();Assert.True(foreignBudget.TryOpenScope(NavigationPreparationKind.Solve,2,1,out var foreign));
            var target=NavPackedFieldStorage.Create(64,foreign.TryReserve);foreign.Attach(target);long held=foreign.HeldBytes,physical=foreignBudget.PhysicalBytes;int references=trace.References;
            Assert.Throws<ArgumentException>(()=>target.Set(0,0,new NavGraphArc(0,1,0,NavGraphArcKind.Surface,null,facade)));
            Assert.AreEqual(held,foreign.HeldBytes);Assert.AreEqual(physical,foreignBudget.PhysicalBytes);Assert.AreEqual(references,trace.References);foreign.Discard();Assert.AreEqual(0,foreignBudget.PhysicalBytes);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));var next=source.ForkPrepared(scope.TryReserve);scope.Attach(next);
            next.Set(0,1,new NavGraphArc(0,2,1,NavGraphArcKind.Surface,null));next.SealPrepared();scope.Commit(next,_=>{});
            source.ReleaseStorage();Assert.AreEqual(1,trace.References);source.Release();Assert.AreEqual(0,trace.References);
            next.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
        }
        [Test] public void FailedTreePayloadRetainReleasesOnlyAcquiredNodeEdges()
        {
            var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));var leg=NavTypedLeg.CreateOwned(at,at,0,NavTypedLegKind.Surface,null,initial.OwnedReservation());
            var source=NavCowDictionary<int,NavTypedLeg>.CreateOwned(initial.OwnedReservation(),insertionOrder:false);initial.Attach(source);for(int n=0;n<7;n++)source.Add(n,leg);source.Seal();initial.Commit(source,_=>{});
            long baseline=budget.RetainedBytes;int references=leg.References;var values=source.ToArray();
            var dead=new NavTypedLeg(at,at,0,NavTypedLegKind.Surface,null);dead.ReleaseStorage();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));var candidate=source.ForkOwned(scope.OwnedReservation());scope.Attach(candidate);
            Assert.Throws<InvalidOperationException>(()=>candidate[3]=dead);scope.Discard();Assert.AreEqual(references,leg.References);CollectionAssert.AreEqual(values,source.ToArray());Assert.AreEqual(baseline,budget.RetainedBytes);Assert.AreEqual(Unique(source,leg),budget.PhysicalBytes);
            source.ReleaseStorage();leg.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        private sealed class ScratchProbeStorage : NavOwnedStorage
        {internal ScratchProbeStorage():base(64){}protected override void ReleaseEdges(){}}
        [Test] public void LiveScratchCreditsCannotBecomePhysicalRootsOrResolveBeforeOwnerRelease()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));var attached=new object();scope.Attach(attached);
            var scratch=NavStorageScratch.Reserve(scope.OwnedReservation(),4096);long retained=budget.RetainedBytes;
            Assert.AreEqual(NavigationRetentionBudget.RootBytes+NavigationRetentionBudget.ScopeBytes+4096+NavStorageScratch.LeaseBytes,retained);
            var unadmitted=new ScratchProbeStorage();Assert.Throws<InvalidOperationException>(()=>((INavStorageAllocator)scope).Register(unadmitted));unadmitted.Release();
            Assert.Throws<InvalidOperationException>(()=>scope.Publish(attached,_=>Assert.Fail("Live scratch published.")));
            Assert.Throws<InvalidOperationException>(()=>scope.Commit(attached,0,_=>Assert.Fail("Live scratch scalar committed.")));
            Assert.Throws<InvalidOperationException>(()=>scope.RelinquishOwner());Assert.Throws<InvalidOperationException>(()=>scope.Discard());Assert.False(scope.IsResolved);
            scope.RequestCancel();scratch.Dispose();scratch.Dispose();Assert.AreEqual(NavigationRetentionBudget.ScopeBytes,budget.HeldBytes);Assert.AreEqual(retained,budget.PeakRetainedBytes);
            Assert.True(scope.Discard());Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
        }
        [Test] public void RealPartitionTraceOwnsEveryLegAndReturnsScratchBeforeCommitOrEveryNthRefusal()
        {
            var map=ThreeCrossingsMap.Default;var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);
            var clearance=new NavClearanceProfile("explorer",1,"ground",1,.58);Assert.True(provider.TryLocate(new NavPoint(-18,0),clearance,out var start));
            Assert.True(provider.TryTrace(start,new NavPoint(18,0),clearance,out var legacyEnd,out var legacy));
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));var allocator=new RefusingAllocator(initial,int.MaxValue);
            Assert.True(provider.TryTraceOwned(start,new NavPoint(18,0),clearance,out var endpoint,out var trace,new NavStorageReservation(allocator.Reserve,allocator)));int total=allocator.Calls;
            Assert.AreEqual(legacyEnd,endpoint);Assert.AreEqual(legacy.Count,trace.Count);
            for(int n=0;n<trace.Count;n++){Assert.AreEqual(legacy[n].From,trace[n].From);Assert.AreEqual(legacy[n].To,trace[n].To);Assert.AreEqual(legacy[n].Cost,trace[n].Cost);Assert.AreEqual(legacy[n].Kind,trace[n].Kind);Assert.AreEqual(legacy[n].PortalId,trace[n].PortalId);}
            Assert.True(trace.Any(l=>l.Kind==NavTypedLegKind.Portal));Assert.AreEqual(NavigationRetentionBudget.ScopeBytes,budget.HeldBytes);
            var root=(NavOwnedStorage)trace;initial.Attach(root);initial.Commit(root,_=>{});Assert.AreEqual(Unique(root),budget.PhysicalBytes);root.ReleaseOwner();Assert.AreEqual(0,budget.PhysicalBytes);
            for(int nth=1;nth<=total;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));allocator=new RefusingAllocator(scope,nth);
                Assert.Throws<InvalidOperationException>(()=>provider.TryTraceOwned(start,new NavPoint(18,0),clearance,out _,out _,new NavStorageReservation(allocator.Reserve,allocator)));
                Assert.AreEqual(nth,allocator.Calls);scope.Discard();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
            }
        }
        [Test] public void ZeroPartitionTraceOwnsEveryLegAndReturnsScratchBeforeCommitOrEveryNthRefusal()
        {
            var map=ThreeCrossingsMap.Default;var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);
            var clearance=new NavClearanceProfile("explorer",1,"ground",1,.58);Assert.True(provider.TryLocate(new NavPoint(-18,0),clearance,out var start));
            Assert.True(provider.TryTrace(start,start.Position,clearance,out var legacyEnd,out var legacy));
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));var allocator=new RefusingAllocator(initial,int.MaxValue);
            Assert.True(provider.TryTraceOwned(start,start.Position,clearance,out var endpoint,out var trace,new NavStorageReservation(allocator.Reserve,allocator)));int total=allocator.Calls;
            Assert.AreEqual(legacyEnd,endpoint);Assert.AreEqual(legacy.Count,trace.Count);
            for(int n=0;n<trace.Count;n++){Assert.AreEqual(legacy[n].From,trace[n].From);Assert.AreEqual(legacy[n].To,trace[n].To);Assert.AreEqual(legacy[n].Cost,trace[n].Cost);Assert.AreEqual(legacy[n].Kind,trace[n].Kind);Assert.AreEqual(legacy[n].PortalId,trace[n].PortalId);}
            Assert.AreEqual(1,trace.Count);Assert.AreEqual(NavTypedLegKind.Surface,trace[0].Kind);Assert.AreEqual(0,trace[0].Cost);Assert.IsNull(trace[0].PortalId);Assert.AreEqual(NavigationRetentionBudget.ScopeBytes,budget.HeldBytes);
            var root=(NavOwnedStorage)trace;initial.Attach(root);initial.Commit(root,_=>{});Assert.AreEqual(Unique(root),budget.PhysicalBytes);root.ReleaseOwner();Assert.AreEqual(0,budget.PhysicalBytes);
            for(int nth=1;nth<=total;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));allocator=new RefusingAllocator(scope,nth);
                Assert.Throws<InvalidOperationException>(()=>provider.TryTraceOwned(start,start.Position,clearance,out _,out _,new NavStorageReservation(allocator.Reserve,allocator)));
                Assert.AreEqual(nth,allocator.Calls);scope.Discard();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
            }
        }
        [TestCase(false)][TestCase(true)] public void PortalQualificationAndInlineEndpointEveryNthRefusalKeepCommittedPrefix(bool open)
        {
            var provider=new StackedNavigationFixture(open).Provider;var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('e',64),"distance",1,1);var limits=new NavGraphCompileLimits(2000,2000,10000);
            var source=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,clearance,profile,limits,initial.OwnedReservation());initial.Attach(source);
            int steps=0;while(source.Phase!=LayeredNavigationCompileWork.Stage.Portals){Assert.Less(++steps,20000);source.Advance(1);}
            source.SealPrepared();initial.Commit(source,_=>{});long baseline=budget.RetainedBytes;
            byte[] Bytes(){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(source.Save());return stream.ToArray();}}
            var bytes=Bytes();Assert.AreEqual(Unique(source),budget.PhysicalBytes);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var probeScope));var allocator=new RefusingAllocator(probeScope,int.MaxValue);var reservation=new NavStorageReservation(allocator.Reserve,allocator);
            var probe=source.ForkOwned(reservation);probeScope.Attach(probe);Assert.AreEqual(1,probe.Advance(1));int total=allocator.Calls;probe.SealPrepared();probeScope.Commit(probe,_=>{});
            var checkpoint=probe.Save();Assert.AreEqual(open?NavConnectorStatus.Ready:NavConnectorStatus.Closed,checkpoint.Qualifications[0].Status);Assert.AreEqual(open,checkpoint.PortalEnds[0]!=null);
            Assert.GreaterOrEqual(checkpoint.Qualifications[0].StorageBytes,NavStorageLayout.ValueBytes(typeof(NavLocation))*4+96);
            Assert.AreEqual(Unique(source,probe),budget.PhysicalBytes);probe.ReleaseStorage();Assert.AreEqual(baseline,budget.RetainedBytes);
            for(int nth=1;nth<=total;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));allocator=new RefusingAllocator(scope,nth);LayeredNavigationCompileWork candidate=null;
                try{candidate=source.ForkOwned(new NavStorageReservation(allocator.Reserve,allocator));scope.Attach(candidate);candidate.Advance(1);Assert.Fail("Expected portal prefix allocation refusal.");}
                catch(InvalidOperationException){Assert.AreEqual(nth,allocator.Calls);}finally{candidate?.ReleaseStorage();scope.Discard();}
                CollectionAssert.AreEqual(bytes,Bytes());Assert.AreEqual(baseline,budget.RetainedBytes);Assert.AreEqual(Unique(source),budget.PhysicalBytes);
            }
            source.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)] public void FinalGraphEveryNthAdmissionRefusalRollsBackAllOwnedCompilerEdges(bool partition)
        {
            ILayeredNavigationProvider provider;
            if(partition){var map=ThreeCrossingsMap.Default;provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);}
            else provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));
            // Coarse grid is only a tiny real-provider rollback fixture, never a
            // production profile change or authored physical/performance gate.
            var source=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('c',64),"distance",1,partition?16:1),new NavGraphCompileLimits(2000,2000,10000),initial.OwnedReservation());initial.Attach(source);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;int steps=0;
            while(true){
                bool final;
                if(partition){var work=typeof(LayeredNavigationCompileWork).GetField("partition",flags).GetValue(source);var type=work.GetType();final=type.GetProperty("Phase",flags).GetValue(work).ToString()=="Components"&&(int)type.GetField("componentCursor",flags).GetValue(work)==((NavPagedList<int>)type.GetField("components",flags).GetValue(work)).Count;}
                else final=source.Phase==LayeredNavigationCompileWork.Stage.WrapAdjacency&&(int)typeof(LayeredNavigationCompileWork).GetField("wrapCursor",flags).GetValue(source)==((NavPagedList<NavGraphNode>)typeof(LayeredNavigationCompileWork).GetField("nodes",flags).GetValue(source)).Count;
                if(final)break;Assert.Less(++steps,20000);Assert.AreEqual(1,source.Advance(1));
            }
            source.SealPrepared();initial.Commit(source,_=>{});long baseline=budget.RetainedBytes;
            byte[] Bytes(){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(source.Save());return stream.ToArray();}}
            var bytes=Bytes();Assert.AreEqual(Unique(source.PhysicalRoot),budget.PhysicalBytes);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var probeScope));var probeAllocator=new RefusingAllocator(probeScope,int.MaxValue);
            var probe=source.ForkOwned(new NavStorageReservation(probeAllocator.Reserve,probeAllocator));probeScope.Attach(probe);Assert.AreEqual(1,probe.Advance(1));Assert.AreEqual(LayeredNavigationCompileWork.Stage.Ready,probe.Phase);int total=probeAllocator.Calls;probeScope.Discard();Assert.AreEqual(baseline,budget.RetainedBytes);
            for(int nth=1;nth<=total;nth++){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));var allocator=new RefusingAllocator(scope,nth);LayeredNavigationCompileWork candidate=null;
                try{candidate=source.ForkOwned(new NavStorageReservation(allocator.Reserve,allocator));scope.Attach(candidate);candidate.Advance(1);Assert.Fail("Expected exact graph/fork allocation refusal.");}
                catch(InvalidOperationException){Assert.AreEqual(nth,allocator.Calls);}
                finally{candidate?.ReleaseStorage();scope.Discard();}
                CollectionAssert.AreEqual(bytes,Bytes());Assert.AreEqual(baseline,budget.RetainedBytes);Assert.AreEqual(Unique(source.PhysicalRoot),budget.PhysicalBytes);
            }
            Assert.Greater(total,1);source.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
        }
        [TestCase(false)][TestCase(true)] public void OwnedSolverPreparedPinsKeepSearchAndTypedLegChargesExact(bool shared)
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,new[]{new NavObstacle(-.5,-.5,.5,.5)},1));var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var graphScope));var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('d',64),"distance",1,1),new NavGraphCompileLimits(2000,2000,10000),graphScope.OwnedReservation());graphScope.Attach(compiler);
            while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready)compiler.Advance(100);compiler.SealPrepared();graphScope.Commit(compiler,_=>{});var graph=compiler.Graph;graph.Retain();compiler.ReleaseStorage();
            var origin=new NavLocation(new NavPoint(-2,0),NavLocation.FlatSurface);var goal=new NavLocation(new NavPoint(2,0),NavLocation.FlatSurface);var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,origin,goal,null);NavSharedTerminalField field=null;
            if(shared){Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var fieldScope));field=NavSharedTerminalField.CopyRegionAndCreateOwned(graph,new NavTerminalRegion("owned-solver",1,goal,new[]{goal}),4096,fieldScope.OwnedReservation());fieldScope.Attach(field);while(field.Status==NavSolveStatus.Pending)field.Advance(100);Assert.AreEqual(NavSolveStatus.Ready,field.Status);field.SealPrepared();fieldScope.Commit(field,_=>{});}
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var initial));NavOwnedStorage current=shared?(NavOwnedStorage)NavIncrementalSharedRoute.CopySubscriptionAndCreateOwned(field,subscription,initial.OwnedReservation()):NavIncrementalSingletonRoute.CopySubscriptionAndCreateOwned(graph,subscription,4096,initial.OwnedReservation());initial.Attach(current);
            void Seal(NavOwnedStorage value){if(shared)((NavIncrementalSharedRoute)value).SealPrepared();else ((NavIncrementalSingletonRoute)value).SealPrepared();}
            NavSolveStatus Status(NavOwnedStorage value)=>shared?((NavIncrementalSharedRoute)value).Status:((NavIncrementalSingletonRoute)value).Status;
            byte[] Bytes(NavOwnedStorage value){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true)){var codec=new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1);if(shared)codec.Shared(((NavIncrementalSharedRoute)value).Save());else codec.Singleton(((NavIncrementalSingletonRoute)value).Save());}return stream.ToArray();}}
            Seal(current);initial.Commit(current,_=>{});int steps=0;
            while(Status(current)==NavSolveStatus.Pending){
                Assert.Less(++steps,20000);var prior=current;var before=Bytes(prior);prior.Retain();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,4+steps,out var operation));
                current=shared?(NavOwnedStorage)((NavIncrementalSharedRoute)prior).ForkOwned(field,operation.OwnedReservation()):((NavIncrementalSingletonRoute)prior).ForkOwned(operation.OwnedReservation());operation.Attach(current);
                if(shared)((NavIncrementalSharedRoute)current).Advance(7);else ((NavIncrementalSingletonRoute)current).Advance(7);Seal(current);operation.Publish(current,_=>{});operation.ResolveCommitted();prior.ReleaseOwner();
                CollectionAssert.AreEqual(before,Bytes(prior));Assert.AreEqual(Unique(current,prior,field,graph),budget.PhysicalBytes);prior.Release();Assert.AreEqual(Unique(current,field,graph),budget.PhysicalBytes);
            }
            Assert.AreEqual(NavSolveStatus.Ready,Status(current));var route=shared?((NavIncrementalSharedRoute)current).Route:((NavIncrementalSingletonRoute)current).Route;Assert.Greater(route.Legs.Count,1);Assert.Greater(steps,1);
            current.ReleaseOwner();field?.ReleaseStorage();graph.Release();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
        }
        [TestCase(false,0)][TestCase(false,1)][TestCase(false,2)][TestCase(true,0)][TestCase(true,1)][TestCase(true,2)]
        public void StrictOwnedCompilerRejectsMissingUnownedAndForeignProviderBeforeAllocation(bool partition,int kind)
        {
            ILayeredNavigationProvider raw;if(partition){var map=ThreeCrossingsMap.Default;raw=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);}else raw=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));
            var sourceBudget=new NavigationRetentionBudget();ILayeredNavigationProvider source=raw;
            if(kind==1){Assert.True(sourceBudget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var sourceScope));source=partition?(ILayeredNavigationProvider)PartitionNavigationProvider.CopyOwned((PartitionNavigationProvider)raw,sourceScope.OwnedReservation()):LayeredNavigationProvider.CopyOwned((LayeredNavigationProvider)raw,sourceScope.OwnedReservation());var root=((INavStorageFacade)source).StorageRoot;sourceScope.Attach(root);sourceScope.Commit(root,_=>{});}
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('b',64),"distance",1,partition?16:1);var limits=new NavGraphCompileLimits(2000,2000,10000);var legacy=new LayeredNavigationCompileWork(source,clearance,profile,limits);legacy.Advance(1);var state=legacy.Save();
            byte[] Bytes(){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(legacy.Save());return stream.ToArray();}}
            var bytes=Bytes();var sourceRoot=(source as INavStorageFacade)?.StorageRoot;int references=sourceRoot?.References??0;long baseline=sourceBudget.RetainedBytes;
            var target=new NavigationRetentionBudget();Assert.True(target.TryOpenScope(NavigationPreparationKind.Reconstruction,1,2,out var scope));var counter=new RefusingAllocator(scope,int.MaxValue);var reservation=new NavStorageReservation(counter.Reserve,counter);var input=kind==2?null:source;long targetBaseline=target.RetainedBytes;
            Assert.Throws<ArgumentException>(()=>LayeredNavigationCompileWork.CreateOwned(input,clearance,profile,limits,reservation));Assert.Throws<ArgumentException>(()=>LayeredNavigationCompileWork.RestoreOwned(input,clearance,profile,limits,state,reservation));Assert.AreEqual(0,counter.Calls);Assert.AreEqual(references,sourceRoot?.References??0);Assert.AreEqual(baseline,sourceBudget.RetainedBytes);Assert.AreEqual(targetBaseline,target.RetainedBytes);CollectionAssert.AreEqual(bytes,Bytes());
            scope.Discard();legacy.ReleaseStorage();sourceRoot?.Release();Assert.AreEqual(0,target.PhysicalBytes);Assert.AreEqual(0,sourceBudget.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void GenericOwnedPayloadAdoptionRejectsForeignAndUnownedLegWithoutRetain(bool unowned)
        {
            var sourceBudget=new NavigationRetentionBudget();var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);NavTypedLeg leg;
            if(unowned)leg=new NavTypedLeg(at,at,0,NavTypedLegKind.Surface,null);
            else{Assert.True(sourceBudget.TryOpenScope(NavigationPreparationKind.Projection,1,1,out var source));leg=NavTypedLeg.CreateOwned(at,at,0,NavTypedLegKind.Surface,null,source.OwnedReservation());source.Attach(leg);source.Commit(leg,_=>{});}
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,2,out var scope));var counter=new RefusingAllocator(scope,int.MaxValue);var reservation=new NavStorageReservation(counter.Reserve,counter);var map=NavCowDictionary<int,NavTypedLeg>.CreateOwned(reservation);scope.Attach(map);var list=NavPagedList<NavTypedLeg>.Create(reservation.TryReserve);counter.Calls=0;int references=leg.References;long baseline=sourceBudget.RetainedBytes;
            Assert.Throws<ArgumentException>(()=>map.Add(1,leg));Assert.Throws<ArgumentException>(()=>list.Add(leg));Assert.AreEqual(0,counter.Calls);Assert.AreEqual(0,map.Count);Assert.AreEqual(0,list.Count);Assert.Throws<ArgumentException>(()=>NavRetainedArray<NavTypedLeg>.Copy(new[]{leg},reservation.TryReserve));Assert.AreEqual(references,leg.References);Assert.AreEqual(baseline,sourceBudget.RetainedBytes);
            list.ReleaseStorage();scope.Discard();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(0,budget.HeldBytes);leg.ReleaseStorage();Assert.AreEqual(0,sourceBudget.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void OwnedPartitionProviderPreservesPredicatesCompleteCompilerAndSharedOwnerLifetime(bool foundry)
        {
            IPlayableTerrain map=foundry?(IPlayableTerrain)new FoundryMap(new FoundryProfileData{scale=1.5}):ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);var source=new PartitionNavigationProvider(map,geometry,null);var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));var owned=PartitionNavigationProvider.CopyOwned(source,scope.OwnedReservation());scope.Attach(owned.StorageRoot);scope.Commit(owned.StorageRoot,_=>{});
            Assert.AreEqual(source.Binding,owned.Binding);Assert.AreEqual(Unique(owned.StorageRoot),budget.PhysicalBytes);Assert.AreNotSame(source.Seams,owned.Seams);Assert.AreEqual(source.Seams.Count,owned.Seams.Count);
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('c',64),"distance",1,16);var limits=new NavGraphCompileLimits(2000,2000,10000);
            for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++){
                var point=new NavPoint(map.HalfExtent*x/4,map.HalfExtent*z/4);bool expected=source.TryLocate(point,clearance,out var a);Assert.AreEqual(expected,owned.TryLocate(point,clearance,out var b));Assert.AreEqual(a,b);
                if(expected){Assert.AreEqual(source.IsValid(a,clearance),owned.IsValid(b,clearance));var goal=new NavPoint(point.X+.1,point.Z+.1);Assert.AreEqual(source.TryTraverse(a,goal,clearance,out var c),owned.TryTraverse(b,goal,clearance,out var d));Assert.AreEqual(c,d);}
            }
            byte[] Bytes(LayeredNavigationCompileWork value){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(value.Save());return stream.ToArray();}}
            var legacy=new LayeredNavigationCompileWork(source,clearance,profile,limits);while(legacy.Phase!=LayeredNavigationCompileWork.Stage.Ready)legacy.Advance(100);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,2,out var compile));var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(owned,clearance,profile,limits,compile.OwnedReservation());compile.Attach(compiler);while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready)compiler.Advance(100);CollectionAssert.AreEqual(Bytes(legacy),Bytes(compiler));legacy.ReleaseStorage();compiler.SealPrepared();compile.Commit(compiler,_=>{});
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,3,out var secondScope));var second=PartitionNavigationProvider.CopyOwned(owned,secondScope.OwnedReservation());secondScope.Attach(second.StorageRoot);secondScope.Commit(second.StorageRoot,_=>{});Assert.AreNotSame(owned.StorageRoot,second.StorageRoot);Assert.AreSame(((NavDeclarationStorage)owned.StorageRoot).First,((NavDeclarationStorage)second.StorageRoot).First);Assert.AreSame(owned.Surfaces[0],second.Surfaces[0]);Assert.AreEqual(Unique(owned.StorageRoot,second.StorageRoot,compiler),budget.PhysicalBytes);
            owned.StorageRoot.Release();compiler.ReleaseStorage();Assert.AreEqual(Unique(second.StorageRoot),budget.PhysicalBytes);second.StorageRoot.Release();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(0,budget.HeldBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void PartitionOwnedIdentityUsesOriginalTranscriptPrivateHoldAndRejectsForeignBeforeAdmission(bool foundry)
        {
            IPlayableTerrain terrain=foundry?(IPlayableTerrain)new FoundryMap(new FoundryProfileData{scale=1.5}):ThreeCrossingsMap.Default;
            var geometry=new NavGeometry(terrain.HalfExtent,terrain.MovementBlockers.ToArray(),terrain.Revision);
            foreach(var hold in new[]{null,""," ","held-building-v1"}){
                var source=new PartitionNavigationProvider(terrain,geometry,hold);
                string expected=NavContract.Digest(w=>{
                    w.Write("authored-partition-provider-v1");w.Write(terrain.Id);w.Write(terrain.Revision);w.Write(terrain.SurfaceSemanticsVersion);w.Write(terrain.HalfExtent);
                    w.Write(hold!=null);if(hold!=null)w.Write(hold);w.Write(terrain.Supports.Count);
                    foreach(var support in terrain.Supports){w.Write(support.SurfaceId);w.Write(support.Id);NavContract.Polygon(w,support.Bounds.Footprint);w.Write(support.Height);w.Write(support.Gradient.X);w.Write(support.Gradient.Z);w.Write(support.Origin.X);w.Write(support.Origin.Z);w.Write(support.IsBridge);}
                    w.Write(terrain.SurfaceTransitions.Count);foreach(var transition in terrain.SurfaceTransitions){w.Write(transition.Id);w.Write(transition.From);w.Write(transition.To);}
                    w.Write(geometry.Revision);w.Write(geometry.Obstacles.Count);foreach(var obstacle in geometry.Obstacles){w.Write(obstacle.MinX);w.Write(obstacle.MinZ);w.Write(obstacle.MaxX);w.Write(obstacle.MaxZ);w.Write(obstacle.CircleRadius);w.Write(obstacle.Bottom);w.Write(obstacle.Top);w.Write(obstacle.Polygon!=null);if(obstacle.Polygon!=null)NavContract.Polygon(w,obstacle.Polygon);}
                    w.Write(source.Seams.Count);foreach(var seam in source.Seams){w.Write(seam.FromSurface);w.Write(seam.ToSurface);NavContract.Point(w,seam.From);NavContract.Point(w,seam.To);w.Write(seam.Allowed);}
                });
                Assert.AreEqual(expected,source.Binding);
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var owned=PartitionNavigationProvider.CopyOwned(source,initial.OwnedReservation());initial.Attach(owned.StorageRoot);initial.Commit(owned.StorageRoot,_=>{});
                Assert.AreEqual(expected,NavContract.Digest(owned.WriteBindingTranscript));Assert.AreEqual(terrain.Id,owned.TerrainId);Assert.AreNotSame(terrain.Id,owned.TerrainId);Assert.AreEqual(terrain.Revision,owned.TerrainRevision);Assert.AreEqual(terrain.SurfaceSemanticsVersion,owned.SurfaceSemanticsVersion);Assert.AreEqual(hold,owned.HoldBinding);if(!string.IsNullOrEmpty(hold))Assert.AreNotSame(hold,owned.HoldBinding);
                Assert.GreaterOrEqual(owned.StorageRoot.StorageBytes,NavStorageLayout.ObjectBytesBound(typeof(NavDeclarationStorage))+NavStorageLayout.ObjectBytesBound(typeof(PartitionNavigationProvider))+NavProviderCopy.TextBytes(owned.Binding)+NavProviderCopy.TextBytes(owned.TerrainId)+NavProviderCopy.TextBytes(owned.HoldBinding));
                long physical=budget.PhysicalBytes;int references=owned.StorageRoot.References;
                var foreign=new NavigationRetentionBudget();Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var rejected));var counter=new RefusingAllocator(rejected,int.MaxValue);long held=rejected.HeldBytes;Assert.Throws<ArgumentException>(()=>PartitionNavigationProvider.CopyOwned(owned,new NavStorageReservation(counter.Reserve,counter)));Assert.Zero(counter.Calls);Assert.AreEqual(held,rejected.HeldBytes);Assert.AreEqual(references,owned.StorageRoot.References);Assert.AreEqual(physical,budget.PhysicalBytes);rejected.Discard();
                // A public facade containing an owned movement geometry is also
                // foreign input; a missing aggregate root is not permission to copy it.
                var locations=(TerrainLocations)((NavDeclarationStorage)((NavDeclarationStorage)owned.StorageRoot).First).Facade;
                var publicWithOwnedGeometry=new PartitionNavigationProvider(terrain,locations.Geometry,hold);
                Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Reconstruction,2,2,out rejected));counter=new RefusingAllocator(rejected,int.MaxValue);Assert.Throws<ArgumentException>(()=>PartitionNavigationProvider.CopyOwned(publicWithOwnedGeometry,new NavStorageReservation(counter.Reserve,counter)));Assert.Zero(counter.Calls);rejected.Discard();
                var bindingField=typeof(PartitionNavigationProvider).GetField("<Binding>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);bindingField.SetValue(owned,new string('f',64));
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,2,out var invalid));Assert.Throws<ArgumentException>(()=>PartitionNavigationProvider.CopyOwned(owned,invalid.OwnedReservation()));invalid.Discard();bindingField.SetValue(owned,expected);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(references,owned.StorageRoot.References);owned.StorageRoot.Release();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(foreign.PhysicalBytes);Assert.Zero(foreign.HeldBytes);
            }
        }
        [Test] public void EveryNthPartitionProviderCopyRefusalResolvesSupportSeamMetadataAndSharedInputPins()
        {
            void RejectCopy(PartitionNavigationProvider input,RefusingAllocator allocator)
            {
                Type expected=null;
                bool Reserve(long bytes){
                    bool accepted=allocator.Reserve(bytes);
                    if(!accepted)expected=new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(PartitionNavigationProvider)&&f.GetMethod().Name=="ReserveCopyScratch")?
                        typeof(PartitionNavigationProvider.CopyScratchRefused):typeof(InvalidOperationException);
                    return accepted;
                }
                var error=Assert.Catch<InvalidOperationException>(()=>PartitionNavigationProvider.CopyOwned(input,new NavStorageReservation(Reserve,allocator)));
                Assert.NotNull(expected,"the selected allocation must refuse");Assert.AreEqual(expected,error.GetType(),"exact refusal type at the selected reservation site");
            }
            var map=ThreeCrossingsMap.Default;var source=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var probe=new RefusingAllocator(initial,int.MaxValue);var owned=PartitionNavigationProvider.CopyOwned(source,new NavStorageReservation(probe.Reserve,probe));initial.Attach(owned.StorageRoot);int total=probe.Calls;initial.Commit(owned.StorageRoot,_=>{});Assert.Greater(total,10);long baseline=budget.RetainedBytes;
            for(int nth=1;nth<=total;nth++){
                var empty=new NavigationRetentionBudget();Assert.True(empty.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));var allocator=new RefusingAllocator(scope,nth);RejectCopy(source,allocator);Assert.AreEqual(nth,allocator.Calls);scope.Discard();Assert.AreEqual(0,empty.PhysicalBytes);Assert.AreEqual(0,empty.HeldBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,empty.RetainedBytes);Assert.AreEqual(baseline,budget.RetainedBytes);
            }
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,2,out var countScope));var count=new RefusingAllocator(countScope,int.MaxValue);var copy=PartitionNavigationProvider.CopyOwned(owned,new NavStorageReservation(count.Reserve,count));countScope.Attach(copy.StorageRoot);int sharedTotal=count.Calls;countScope.Discard();Assert.AreEqual(baseline,budget.RetainedBytes);
            for(int nth=1;nth<=sharedTotal;nth++){
                int references=owned.StorageRoot.References;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,3,out var scope));var allocator=new RefusingAllocator(scope,nth);RejectCopy(owned,allocator);Assert.AreEqual(nth,allocator.Calls);scope.Discard();Assert.AreEqual(references,owned.StorageRoot.References);Assert.AreEqual(baseline,budget.RetainedBytes);Assert.AreEqual(Unique(owned.StorageRoot),budget.PhysicalBytes);
            }
            owned.StorageRoot.Release();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [Test] public void OwnedLayeredProviderCopiesPhysicalAliasesAndRetiresOnlyAfterGraphOwner()
        {
            var polygon=new NavPolygon(new[]{new NavPoint(-2,-2),new NavPoint(2,-2),new NavPoint(2,2),new NavPoint(-2,2)});
            var blocker=new NavPolygon(new[]{new NavPoint(-.25,-.25),new NavPoint(.25,-.25),new NavPoint(.25,.25),new NavPoint(-.25,.25)});
            var area=new NavTraversalArea(polygon,2,new[]{new NavObstacle(blocker,-1,1),new NavObstacle(blocker,-1,1)});
            var a=new NavLayerSurface("a","ground",0,1,area);var b=new NavLayerSurface("b","ground",1,1,area);
            var portal=new NavLayerPortal("ab",new NavLocation(new NavPoint(1,1),"a"),new NavLocation(new NavPoint(1,1),"b"),NavPortalDirection.Both,true,1,area,new[]{"ground"});
            var c=new NavLayerSurface("c","ground",2,1,new NavTraversalArea(blocker,2,Array.Empty<NavObstacle>()));
            var source=new LayeredNavigationProvider("owned",1,1,1,new string('a',64),new[]{a,b,c},new[]{portal});var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));var owned=LayeredNavigationProvider.CopyOwned(source,scope.OwnedReservation());scope.Attach(owned.StorageRoot);scope.Commit(owned.StorageRoot,_=>{});
            Assert.AreEqual(source.Binding,owned.Binding);Assert.AreNotSame(source,owned);Assert.AreNotSame(area,owned.Surfaces[0].Area);Assert.AreSame(owned.Surfaces[0].Area,owned.Surfaces[1].Area);Assert.AreSame(owned.Surfaces[0].Area,owned.Portals[0].Corridor);
            Assert.AreNotSame(blocker,owned.Surfaces[0].Area.Blockers[0].Polygon);Assert.AreSame(owned.Surfaces[0].Area.Blockers[0].Polygon,owned.Surfaces[0].Area.Blockers[1].Polygon);Assert.AreSame(owned.Surfaces[0].Area.Blockers[0].Polygon,owned.Surfaces[2].Area.Boundary);Assert.AreEqual(Unique(owned.StorageRoot),budget.PhysicalBytes);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,2,out var compileScope));var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(owned,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('c',64),"distance",1,1),new NavGraphCompileLimits(256,256,1024),compileScope.OwnedReservation());compileScope.Attach(compiler);
            while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready)compiler.Advance(100);
            var legacy=new LayeredNavigationCompileWork(source,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('c',64),"distance",1,1),new NavGraphCompileLimits(256,256,1024));while(legacy.Phase!=LayeredNavigationCompileWork.Stage.Ready)legacy.Advance(100);
            byte[] Bytes(LayeredNavigationCompileWork value){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(value.Save());return stream.ToArray();}}
            CollectionAssert.AreEqual(Bytes(legacy),Bytes(compiler));legacy.ReleaseStorage();
            compiler.SealPrepared();compileScope.Commit(compiler,_=>{});var graph=compiler.Graph;graph.Retain();owned.StorageRoot.Release();compiler.ReleaseStorage();
            Assert.Greater(owned.StorageRoot.References,0);Assert.AreEqual(Unique(graph),budget.PhysicalBytes);graph.Release();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [Test] public void EveryNthLayeredProviderCopyRefusalReturnsAllAliasRootsAndScratch()
        {
            var source=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));var probeBudget=new NavigationRetentionBudget();Assert.True(probeBudget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var probeScope));var counter=new RefusingAllocator(probeScope,int.MaxValue);var copy=LayeredNavigationProvider.CopyOwned(source,new NavStorageReservation(counter.Reserve,counter));probeScope.Attach(copy.StorageRoot);int total=counter.Calls;probeScope.Discard();Assert.AreEqual(0,probeBudget.PhysicalBytes);Assert.Greater(total,5);
            for(int nth=1;nth<=total;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));var allocator=new RefusingAllocator(scope,nth);
                Assert.Throws<InvalidOperationException>(()=>LayeredNavigationProvider.CopyOwned(source,new NavStorageReservation(allocator.Reserve,allocator)));Assert.AreEqual(nth,allocator.Calls);scope.Discard();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(0,budget.HeldBytes);Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);
            }
        }
        [Test] public void EqualBindingOwnedProvidersHaveDistinctLedgersAndRejectForeignCompilerAdmission()
        {
            var source=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));var budget=new NavigationRetentionBudget();var other=new NavigationRetentionBudget();
            LayeredNavigationProvider Copy(NavigationRetentionBudget owner){Assert.True(owner.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));var value=LayeredNavigationProvider.CopyOwned(source,scope.OwnedReservation());scope.Attach(value.StorageRoot);scope.Commit(value.StorageRoot,_=>{});return value;}
            var a=Copy(budget);var b=Copy(other);Assert.AreEqual(a.Binding,b.Binding);Assert.AreNotSame(a.StorageRoot,b.StorageRoot);Assert.AreNotSame(a.Surfaces[0].Area.Boundary,b.Surfaces[0].Area.Boundary);
            Assert.True(other.TryOpenScope(NavigationPreparationKind.Reconstruction,1,2,out var attempt));var counter=new RefusingAllocator(attempt,int.MaxValue);int references=a.StorageRoot.References;long bytes=budget.RetainedBytes;
            Assert.Throws<ArgumentException>(()=>LayeredNavigationCompileWork.CreateOwned(a,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('c',64),"distance",1,1),new NavGraphCompileLimits(256,256,1024),new NavStorageReservation(counter.Reserve,counter)));Assert.AreEqual(0,counter.Calls);Assert.AreEqual(references,a.StorageRoot.References);Assert.AreEqual(bytes,budget.RetainedBytes);
            attempt.Discard();a.StorageRoot.Release();b.StorageRoot.Release();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(0,other.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void OwnedCompilerAndDictionaryForkRejectForeignLedgerBeforeAdmission(bool complete)
        {
            var budget=new NavigationRetentionBudget();var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));
            var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('b',64),"distance",1,1),new NavGraphCompileLimits(256,256,1024),initial.OwnedReservation());initial.Attach(compiler);
            if(complete)while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready)compiler.Advance(100);else compiler.Advance(1);
            compiler.SealPrepared();initial.Commit(compiler,_=>{});
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var indexScope));var dictionary=NavCowDictionary<int,double>.CreateOwned(indexScope.OwnedReservation());indexScope.Attach(dictionary);dictionary.Add(7,3);dictionary.Seal();indexScope.Commit(dictionary,_=>{});
            byte[] Bytes(){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1).Compile(compiler.Save());return stream.ToArray();}}
            var bytes=Bytes();long baseline=budget.RetainedBytes;int compilerRefs=compiler.References,indexRefs=dictionary.References;
            var other=new NavigationRetentionBudget();Assert.True(other.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var scope));var counter=new RefusingAllocator(scope,int.MaxValue);var reservation=new NavStorageReservation(counter.Reserve,counter);
            Assert.Throws<ArgumentException>(()=>compiler.ForkOwned(reservation));Assert.Throws<ArgumentException>(()=>dictionary.ForkOwned(reservation));Assert.AreEqual(0,counter.Calls);Assert.AreEqual(compilerRefs,compiler.References);Assert.AreEqual(indexRefs,dictionary.References);Assert.AreEqual(baseline,budget.RetainedBytes);CollectionAssert.AreEqual(bytes,Bytes());Assert.AreEqual(3,dictionary[7]);
            scope.Discard();dictionary.ReleaseStorage();compiler.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(0,other.PhysicalBytes);
        }
        private static byte[] LedgerGuardBytes(NavSharedTerminalField field,NavIncrementalSharedRoute route,NavIncrementalSingletonRoute singleton=null)
        {
            using(var stream=new System.IO.MemoryStream()){
                using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true)){
                    var codec=new NavigationPrimitiveWire.Writer(writer,_=>-1,_=>-1);codec.Field(field.Save());codec.Shared(route.Save());if(singleton!=null)codec.Singleton(singleton.Save());
                }
                return stream.ToArray();
            }
        }
        private static LayeredNavigationGraph LedgerGuardGraph(NavigationRetentionBudget budget)
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));
            var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('b',64),"distance",1,1),new NavGraphCompileLimits(256,256,1024),scope.OwnedReservation());scope.Attach(compiler);
            while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready)compiler.Advance(100);
            compiler.SealPrepared();scope.Commit(compiler,_=>{});var graph=compiler.Graph;graph.Retain();compiler.ReleaseStorage();return graph;
        }
        [TestCase(false)][TestCase(true)]
        public void OwnedFieldAndSingletonRejectForeignOrUnownedGraphBeforeAdmission(bool unowned)
        {
            var sourceBudget=new NavigationRetentionBudget();var graph=LedgerGuardGraph(sourceBudget);
            var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);var region=new NavTerminalRegion("ledger-guard",1,at,new[]{at});
            var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,at,at,null);
            var legacyProvider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));
            var legacy=unowned?LayeredNavigationGraphCompiler.Compile(legacyProvider,new NavClearanceProfile("ground",1,"ground",1,.25),new NavGraphProfile(new string('b',64),"distance",1,1),new NavGraphCompileLimits(256,256,1024)):null;
            var input=legacy??graph;var targetBudget=new NavigationRetentionBudget();Assert.True(targetBudget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));
            var counter=new RefusingAllocator(scope,int.MaxValue);var reservation=new NavStorageReservation(counter.Reserve,counter);int refs=input.References;long sourceBytes=sourceBudget.RetainedBytes,targetBytes=targetBudget.RetainedBytes;
            Assert.Throws<ArgumentException>(()=>NavSharedTerminalField.CopyRegionAndCreateOwned(input,region,256,reservation));
            Assert.Throws<ArgumentException>(()=>NavIncrementalSingletonRoute.CreateOwned(input,subscription,256,reservation));
            using(var field=new TestFieldLease(new NavSharedTerminalField(input,region,256)))
                Assert.Throws<ArgumentException>(()=>NavSharedTerminalField.CopyRegionAndRestoreOwned(input,region,256,field.Value.Save(),reservation));
            Assert.AreEqual(0,counter.Calls);Assert.AreEqual(refs,input.References);Assert.AreEqual(sourceBytes,sourceBudget.RetainedBytes);Assert.AreEqual(targetBytes,targetBudget.RetainedBytes);
            scope.Discard();legacy?.ReleaseStorage();graph.Release();Assert.AreEqual(0,sourceBudget.PhysicalBytes);Assert.AreEqual(0,targetBudget.PhysicalBytes);
        }
        private sealed class TestFieldLease : IDisposable
        {
            internal readonly NavSharedTerminalField Value;
            internal TestFieldLease(NavSharedTerminalField value){Value=value;}
            public void Dispose()=>Value.ReleaseStorage();
        }
        [TestCase(false)][TestCase(true)]
        public void OwnedFieldAndConnectorForkRejectForeignOrUnownedRootsBeforeAdmission(bool unowned)
        {
            var budget=new NavigationRetentionBudget();var graph=LedgerGuardGraph(budget);var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);var region=new NavTerminalRegion("ledger-fork",1,at,new[]{at});
            var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,at,at,null);NavSharedTerminalField field;NavIncrementalSharedRoute route;NavIncrementalSingletonRoute singleton;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var initial));
            if(unowned){initial.Discard();field=new NavSharedTerminalField(graph,region,256);route=new NavIncrementalSharedRoute(field,subscription);singleton=new NavIncrementalSingletonRoute(graph,subscription,256);}
            else{field=NavSharedTerminalField.CopyRegionAndCreateOwned(graph,region,256,initial.OwnedReservation());initial.Attach(field);field.SealPrepared();initial.Commit(field,_=>{});Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var routeScope));route=NavIncrementalSharedRoute.CopySubscriptionAndCreateOwned(field,subscription,routeScope.OwnedReservation());routeScope.Attach(route);route.SealPrepared();routeScope.Commit(route,_=>{});Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,4,out var singleScope));singleton=NavIncrementalSingletonRoute.CopySubscriptionAndCreateOwned(graph,subscription,256,singleScope.OwnedReservation());singleScope.Attach(singleton);singleton.SealPrepared();singleScope.Commit(singleton,_=>{});}
            field.SealPrepared();route.SealPrepared();singleton.SealPrepared();var bytes=LedgerGuardBytes(field,route,singleton);var target=unowned?budget:new NavigationRetentionBudget();Assert.True(target.TryOpenScope(NavigationPreparationKind.Solve,1,5,out var scope));var counter=new RefusingAllocator(scope,int.MaxValue);var reservation=new NavStorageReservation(counter.Reserve,counter);
            int graphRefs=graph.References,fieldRefs=field.References,routeRefs=route.References;long baseline=budget.RetainedBytes,targetBaseline=target.RetainedBytes;
            Assert.Throws<ArgumentException>(()=>field.ForkOwned(reservation));Assert.Throws<ArgumentException>(()=>NavIncrementalSharedRoute.CreateOwned(field,subscription,reservation));Assert.Throws<ArgumentException>(()=>route.ForkOwned(field,reservation));Assert.Throws<ArgumentException>(()=>singleton.ForkOwned(reservation));
            Assert.AreEqual(0,counter.Calls);Assert.AreEqual(graphRefs,graph.References);Assert.AreEqual(fieldRefs,field.References);Assert.AreEqual(routeRefs,route.References);Assert.AreEqual(baseline,budget.RetainedBytes);Assert.AreEqual(targetBaseline,target.RetainedBytes);CollectionAssert.AreEqual(bytes,LedgerGuardBytes(field,route,singleton));
            scope.Discard();route.ReleaseStorage();singleton.ReleaseStorage();field.ReleaseStorage();graph.Release();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(0,target.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void OwnedConnectorRejectsEqualBindingDistinctGraphBeforeAdmission(bool foreignTarget)
        {
            var budget=new NavigationRetentionBudget();var other=foreignTarget?new NavigationRetentionBudget():budget;var graph=LedgerGuardGraph(budget);var distinct=LedgerGuardGraph(other);Assert.AreEqual(graph.Binding.StableKey,distinct.Binding.StableKey);Assert.AreNotSame(graph.PhysicalProvider,distinct.PhysicalProvider);
            var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);var region=new NavTerminalRegion("same-key",1,at,new[]{at});var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,at,at,null);
            NavSharedTerminalField Field(NavigationRetentionBudget owner,LayeredNavigationGraph input){Assert.True(owner.TryOpenScope(NavigationPreparationKind.Solve,1,2,out var scope));var value=NavSharedTerminalField.CopyRegionAndCreateOwned(input,region,256,scope.OwnedReservation());scope.Attach(value);value.SealPrepared();scope.Commit(value,_=>{});return value;}
            var field=Field(budget,graph);var target=Field(other,distinct);Assert.AreEqual(field.StableKey,target.StableKey);Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var initial));var route=NavIncrementalSharedRoute.CopySubscriptionAndCreateOwned(field,subscription,initial.OwnedReservation());initial.Attach(route);route.SealPrepared();initial.Commit(route,_=>{});
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,4,out var attempt));var counter=new RefusingAllocator(attempt,int.MaxValue);var bytes=LedgerGuardBytes(field,route);int targetRefs=target.References;long baseline=budget.RetainedBytes,otherBaseline=other.RetainedBytes;
            Assert.Throws<ArgumentException>(()=>route.ForkOwned(target,new NavStorageReservation(counter.Reserve,counter)));Assert.AreEqual(0,counter.Calls);Assert.AreEqual(targetRefs,target.References);Assert.AreEqual(baseline,budget.RetainedBytes);Assert.AreEqual(otherBaseline,other.RetainedBytes);CollectionAssert.AreEqual(bytes,LedgerGuardBytes(field,route));
            attempt.Discard();route.ReleaseStorage();field.ReleaseStorage();target.ReleaseStorage();graph.Release();distinct.Release();Assert.AreEqual(0,budget.PhysicalBytes);Assert.AreEqual(0,other.PhysicalBytes);
        }
        private sealed class ThrowingLegList : IReadOnlyList<NavTypedLeg>
        {
            private readonly NavTypedLeg first;internal int CountReads;
            internal ThrowingLegList(NavTypedLeg first){this.first=first;}
            public int Count{get{CountReads++;return CountReads==1?2:3;}}
            public NavTypedLeg this[int index]=>index==0?first:throw new InvalidOperationException("Failed public input getter.");
            public IEnumerator<NavTypedLeg> GetEnumerator(){yield return first;throw new InvalidOperationException("Unexpected enumeration.");}
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        [TestCase(false)][TestCase(true)] public void RetainedArrayFailedPayloadPrefixIsTerminallyReleasedWithOneCountRead(bool invalidPayload)
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,1,out var initial));var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);
            var first=NavTypedLeg.CreateOwned(at,at,0,NavTypedLegKind.Surface,null,initial.OwnedReservation());initial.Attach(first);initial.Commit(first,_=>{});long baseline=budget.RetainedBytes;int references=first.References;
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,2,out var scope));IReadOnlyList<NavTypedLeg> input;ThrowingLegList throwing=null;
            if(invalidPayload){var invalid=new NavTypedLeg(at,at,0,NavTypedLegKind.Surface,null);invalid.ReleaseStorage();input=new[]{first,invalid};}
            else input=throwing=new ThrowingLegList(first);
            Assert.Throws<InvalidOperationException>(()=>NavRetainedArray<NavTypedLeg>.Copy(input,scope.OwnedReservation().TryReserve));scope.Discard();
            if(throwing!=null)Assert.AreEqual(1,throwing.CountReads);Assert.AreSame(first,input[0]);Assert.AreEqual(references,first.References);Assert.AreEqual(baseline,budget.RetainedBytes);Assert.AreEqual(Unique(first),budget.PhysicalBytes);
            first.ReleaseStorage();Assert.AreEqual(0,budget.PhysicalBytes);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)]
        public void EveryNthInitialContainerAllocationResolvesUnreturnedPrefix(int kind)
        {
            var provider=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('e',64),"distance",1,1);var limits=new NavGraphCompileLimits(256,256,1024);
            var graph=LayeredNavigationGraphCompiler.Compile(provider,clearance,profile,limits);var at=new NavLocation(new NavPoint(0,0),NavLocation.FlatSurface);var region=new NavTerminalRegion("initial-refusal",1,at,new[]{at});int graphReferences=graph.References;
            NavOwnedStorage Create(Func<long,bool> reserve){switch(kind){case 0:return NavCowDictionary<int,double>.Create(reserve:reserve);case 1:return NavPagedList<int>.Create(reserve);case 2:return NavPackedSlots.Create(reserve);case 3:return NavPackedFieldStorage.Create(1024,reserve);case 4:return NavFieldFrontier.Create(64,2,128,true,reserve);case 5:return NavFieldFrontier.Create(64,2,128,false,reserve);default:return new NavSharedTerminalField(graph,region,128,false,NavStorageLayout.Before(reserve,512),true);}}
            var counterBudget=new NavigationRetentionBudget();Assert.True(counterBudget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var counterScope));var counter=new RefusingAllocator(counterScope,int.MaxValue);var sample=Create(counter.Reserve);counterScope.Attach(sample);int maximum=counter.Calls;counterScope.Discard();Assert.AreEqual(0,counterBudget.PhysicalBytes);Assert.AreEqual(graphReferences,graph.References);
            for(int nth=1;nth<=maximum;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));var allocator=new RefusingAllocator(scope,nth);
                Assert.Throws<InvalidOperationException>(()=>Create(allocator.Reserve));Assert.AreEqual(nth,allocator.Calls);scope.Discard();
                Assert.AreEqual(0,budget.PhysicalBytes,"No inaccessible caller reference may survive an unreturned constructor.");Assert.AreEqual(NavigationRetentionBudget.RootBytes,budget.RetainedBytes);Assert.AreEqual(graphReferences,graph.References);
            }
            Assert.Greater(maximum,1);graph.ReleaseStorage();
        }
        [Test] public void ConcreteWideNodeLayoutExceedsOldConventionWithoutScalarCommit()
        {
            Assert.Greater(NavCowDictionary<NavGraphArc,NavGraphArc>.NodeBytes,128);
            Assert.GreaterOrEqual(NavCowDictionary<NavGraphArc,NavGraphArc>.NodeBytes,80+2*NavStorageLayout.ValueBytes(typeof(NavGraphArc)));
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var scope));
            var list=NavPagedList<int>.Create(scope.TryReserve);scope.Attach(list);list.Add(1);list.Seal();
            Assert.Throws<InvalidOperationException>(()=>scope.Commit(list,1,_=>Assert.Fail("Scalar publication")));
            list.ReleaseStorage();scope.Discard();Assert.AreEqual(0,budget.PhysicalBytes);
        }
    }
}

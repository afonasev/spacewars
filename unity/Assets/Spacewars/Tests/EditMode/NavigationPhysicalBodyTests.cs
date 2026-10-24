using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [Test] public void AvlMutationBirthsReleaseBeforeScopeResolutionAndRefusalPreservesOldRoot()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));
            var source=NavCowDictionary<int,int>.CreateOwned(initial.OwnedReservation(),insertionOrder:false);
            for(int i=0;i<64;i++)source.Add(i,i);source.Seal();initial.Attach(source);initial.Commit(source,_=>{});
            long committed=budget.PhysicalBytes;var bytes=Capture(budget,source);
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,2,out var mutation));
            var fork=source.ForkOwned(mutation.OwnedReservation());mutation.Attach(fork);
            fork[31]=100;long live=budget.PhysicalBytes;
            for(int i=0;i<4096;i++){fork[31]=101+i;Assert.AreEqual(live,budget.PhysicalBytes,"Temporary path births must die within each mutation.");Assert.AreEqual(31,source[31]);}
            for(int i=64;i<128;i++)fork.Add(i,i);
            Assert.LessOrEqual(budget.PhysicalBytes,Unique(source)+Unique(fork));Assert.AreEqual(64,source.Count);Assert.AreEqual(128,fork.Count);
            TestContext.WriteLine("AVL physical live="+budget.PhysicalBytes+" peak="+budget.PeakRetainedBytes);
            mutation.Discard();Assert.AreEqual(committed,budget.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(budget,source));
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,3,out var rejected));
            var candidate=source.ForkOwned(rejected.OwnedReservation());rejected.Attach(candidate);int calls=0;
            budget.AllocationAdmission=_=>++calls!=2;
            try{Assert.Throws<InvalidOperationException>(()=>candidate[0]=999);Assert.AreEqual(2,calls);Assert.AreEqual(0,candidate[0]);Assert.AreEqual(31,source[31]);}
            finally{budget.AllocationAdmission=null;rejected.Discard();}
            Assert.AreEqual(committed,budget.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(budget,source));source.ReleaseStorage();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void HostRootCarriesSchedulerAndQueuesInOnePhysicalDag()
        {
            var hot=new NavigationRetentionBudget();
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var prepare));
            var root=NavigationHostRoot.CreateOwned(prepare.OwnedReservation());
            var sourceRequest=new NavigationRequest(7,11,4,3,new NavigationProfile(),new NavGeometry(12,Array.Empty<NavObstacle>(),1),
                new NavPoint(-2,0),new NavPoint(4,0),heldIdentity:"hold-a",memberIdentity:new NavigationMemberIdentity(2,3,4,5,6),
                startLocation:new NavLocation(new NavPoint(-2,0),NavLocation.FlatSurface),goalLocation:new NavLocation(new NavPoint(4,0),NavLocation.FlatSurface));
            var sourceProvider=NavigationAdmission.CertifiedFlatProvider(sourceRequest.Geometry);
            var origin=sourceRequest.StartLocation.Value;var endpoint=sourceRequest.GoalLocation.Value;
            var subscription=new NavRouteSubscription(7,4,1,1,0,5,4,2,5,3,11,origin,endpoint,"hold-a");
            var recipe=new NavScheduledRequest(sourceProvider,new NavClearanceProfile("ground",1,"ground",3,sourceRequest.Profile.Radius),
                new NavGraphProfile(NavigationAdmission.ExactProfileBinding(sourceRequest.Profile),"distance",1,sourceRequest.Profile.GridCell),
                new NavGraphCompileLimits(1048576,300000,1200000),new NavTerminalRegion("goal",1,endpoint,new[]{endpoint}),
                subscription,1048576);
            var rowKey=root.RegisterOrdinary(sourceRequest,recipe,NavLocation.FlatSurface,prepare.OwnedReservation());
            var ownedRow=(NavigationHostRow)root.Rows[rowKey];long providerIdentity=ownedRow.ProviderIdentity;
            root.Projection.AddLast(rowKey);
            var foreign=new NavigationRetentionBudget();Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var foreignScope));
            var foreignRoot=NavigationHostRoot.CreateOwned(foreignScope.OwnedReservation());
            var foreignKey=foreignRoot.RegisterOrdinary(sourceRequest,recipe,NavLocation.FlatSurface,foreignScope.OwnedReservation());
            var foreignRow=(NavigationHostRow)foreignRoot.Rows[foreignKey];
            var foreignCounter=new Refusal(foreignScope,int.MaxValue);long hotCharge=hot.PhysicalBytes,foreignCharge=foreign.PhysicalBytes;
            Assert.Throws<ArgumentException>(()=>NavigationHostProvider.CopyOwned(ownedRow.Provider,
                ((NavigationHostRequest)root.Requests[rowKey]).Request.Geometry,NavLocation.FlatSurface,1,foreignCounter));
            Assert.Throws<ArgumentException>(()=>NavigationHostRow.CreateRegistered(
                (NavigationHostRequest)foreignRoot.Requests[foreignKey],
                (NavigationHostProvider)foreignRoot.Providers[foreignRow.ProviderIdentity],foreignRow.ProviderIdentity,
                ownedRow.Scheduled,foreignCounter));
            Assert.Zero(foreignCounter.Calls);Assert.AreEqual(hotCharge,hot.PhysicalBytes);Assert.AreEqual(foreignCharge,foreign.PhysicalBytes);
            foreignRoot.ReleaseStorage();foreignScope.Discard();Assert.Zero(foreign.PhysicalBytes);Assert.Zero(foreign.HeldBytes);
            var stale=NavOwnedText.Copy("stale-completion",prepare.OwnedReservation());
            root.Completion.AddLast(new NavNameKey(new NavNameReference(stale,NavNameRole.Text)));
            stale.ReleaseOwner();
            root.SetOperation(new NavigationHostRoot.OperationDescriptor(CertifiedRouteKernel.PumpStage.Projection,3,true,true,rowKey.Reference));
            root.SetFrontier(new CertifiedRouteKernel.PumpFrontier(CertifiedRouteKernel.PumpStage.Projection,4,2,2,1,3,64,4096,17,1024),5,29);
            root.SealPrepared();prepare.Attach(root);prepare.Commit(root,_=>{});
            long charge=hot.PhysicalBytes;Assert.AreEqual(Unique(root),charge);
            byte[] bytes=Capture(hot,root);
            var withoutExtension=new NavigationRetentionBudget();Assert.True(withoutExtension.TryOpenScope(NavigationPreparationKind.Decode,3,1,out var rejected));
            using(var oldStream=new MemoryStream(bytes,false))using(var oldReader=new BinaryReader(oldStream))
                Assert.Throws<ArgumentException>(()=>NavPhysicalBodyWire.ReadFrom(oldReader,rejected.OwnedReservation()));
            rejected.Discard();Assert.Zero(withoutExtension.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,2,1,out var decode));
            NavigationHostRoot restored;
            using(var stream=new MemoryStream(bytes,false))using(var reader=new BinaryReader(stream)){
                restored=(NavigationHostRoot)NavPhysicalBodyWire.ReadFrom(reader,decode.OwnedReservation(),NavigationHostBodyExtension.Instance);
                Assert.AreEqual(stream.Length,stream.Position);
            }
            decode.Attach(restored);decode.Commit(restored,_=>{});
            Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);
            Assert.AreEqual(29,restored.Progress);Assert.AreEqual(5,restored.Epoch);
            Assert.AreEqual(CertifiedRouteKernel.PumpStage.Projection,restored.Frontier.Stage);
            Assert.AreEqual(17,restored.Frontier.SolveConsumed);
            Assert.AreSame(restored.Scan.First.Reference.Owner,restored.Projection.First.Reference.Owner);
            Assert.AreSame(((NavigationHostRow)restored.Rows.First().Value).Scheduled.StorageRoot,restored.Scan.First.Reference.Owner);
            Assert.AreEqual(1,restored.Rows.Count);
            var restoredRequest=(NavigationHostRequest)restored.Requests[restored.Scan.First];
            Assert.AreEqual(11,restoredRequest.Request.Request);
            Assert.AreEqual("hold-a",restoredRequest.Request.HeldIdentity);
            Assert.AreSame(restoredRequest.Request.Geometry,restoredRequest.Request.BaseGeometry);
            Assert.AreSame(restoredRequest.Request.StartLocation.Value.NameReference.Owner,
                restoredRequest.Request.GoalLocation.Value.NameReference.Owner);
            Assert.AreSame(restoredRequest.Request.StartLocation.Value.SurfaceId,
                restoredRequest.Request.GoalLocation.Value.SurfaceId);
            Assert.AreEqual(4,restoredRequest.Request.Entity);
            Assert.AreEqual(6,restoredRequest.Request.MemberIdentity.MembershipRevision);
            var restoredProvider=(NavigationHostProvider)restored.Providers[providerIdentity];
            Assert.AreSame(restoredRequest.Request.Geometry,restoredProvider.Geometry);
            Assert.AreEqual(NavLocation.FlatSurface,restoredProvider.SurfaceProviderId);
            Assert.AreEqual(1,restoredProvider.LogicalReferences);
            Assert.AreEqual(providerIdentity+1,restored.NextProviderIdentity);
            var restoredRow=(NavigationHostRow)restored.Rows[rowKey.Value];
            Assert.AreSame(restoredRequest.Request,restoredRow.Request);
            Assert.AreSame(restoredProvider.Provider,restoredRow.Provider);
            Assert.AreSame(restoredRow.Provider,restoredRow.Scheduled.Provider);
            Assert.AreEqual(providerIdentity,restoredRow.ProviderIdentity);
            Assert.AreEqual(1,restored.Scheduler.PendingCount);
            Assert.True(restored.Scheduler.HasProviderReference(restoredProvider.Provider));
            Assert.AreEqual("stale-completion",restored.Completion.First.Value);
            Assert.False(restored.Rows.ContainsKey(restored.Completion.First));
            Assert.True(restored.Operation.ProgressIncrementConsumed);
            Assert.AreEqual(3,restored.Operation.Iteration);
            Assert.AreSame(restored.Scan.First.Reference.Owner,restored.Operation.Selector.Owner);
            CollectionAssert.AreEqual(bytes,Capture(cold,restored));
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Projection,1,2,out var staleScope));
            var staleFork=root.ForkOwned(staleScope.OwnedReservation());
            staleFork.StageStale(rowKey,staleScope.OwnedReservation());staleFork.SealPrepared();
            Assert.AreEqual(1,root.Scheduler.PendingCount);
            Assert.IsNull(((NavigationHostRow)root.Rows[rowKey]).Answer);
            staleScope.Attach(staleFork);staleScope.Commit(staleFork,_=>{});
            byte[] staleBytes=Capture(hot,staleFork);
            var staleBudget=new NavigationRetentionBudget();Assert.True(staleBudget.TryOpenScope(NavigationPreparationKind.Decode,4,1,out var staleDecode));
            NavigationHostRoot staleRestored;
            using(var stream=new MemoryStream(staleBytes,false))using(var reader=new BinaryReader(stream))
                staleRestored=(NavigationHostRoot)NavPhysicalBodyWire.ReadFrom(reader,staleDecode.OwnedReservation(),NavigationHostBodyExtension.Instance);
            staleDecode.Attach(staleRestored);staleDecode.Commit(staleRestored,_=>{});
            var terminal=(NavigationHostRow)staleRestored.Rows[rowKey.Value];
            Assert.AreEqual(NavSolveStatus.Stale,terminal.Answer.Status);
            Assert.AreSame(((NavigationHostRequest)staleRestored.Requests[rowKey.Value]).Request,terminal.Answer.Request);
            Assert.Zero(staleRestored.Scheduler.PendingCount);
            Assert.AreEqual(rowKey.Value,staleRestored.Completion[staleRestored.Completion.LastId].Value);
            CollectionAssert.AreEqual(staleBytes,Capture(staleBudget,staleRestored));
            staleRestored.ReleaseStorage();staleFork.ReleaseStorage();Assert.Zero(staleBudget.PhysicalBytes);
            root.ReleaseStorage();restored.ReleaseStorage();
            Assert.Zero(hot.PhysicalBytes);Assert.Zero(cold.PhysicalBytes);
        }
        [Test] public void PhysicalHostReadyAnswerKeepsTheSchedulerRouteAndCorridorInOneBody()
        {
            var hot=new NavigationRetentionBudget();
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));
            var root=NavigationHostRoot.CreateOwned(scope.OwnedReservation());
            var request=new NavigationRequest(7,11,4,3,new NavigationProfile(),
                new NavGeometry(12,Array.Empty<NavObstacle>(),1),new NavPoint(-2,0),new NavPoint(4,0),
                heldIdentity:"hold-a",memberIdentity:new NavigationMemberIdentity(2,3,4,5,6),
                startLocation:new NavLocation(new NavPoint(-2,0),NavLocation.FlatSurface),
                goalLocation:new NavLocation(new NavPoint(4,0),NavLocation.FlatSurface));
            var provider=NavigationAdmission.CertifiedFlatProvider(request.Geometry);
            var origin=request.StartLocation.Value;var endpoint=request.GoalLocation.Value;
            var subscription=new NavRouteSubscription(7,4,1,1,0,5,4,2,5,3,11,origin,endpoint,"hold-a");
            var recipe=new NavScheduledRequest(provider,
                new NavClearanceProfile("ground",1,"ground",3,request.Profile.Radius),
                new NavGraphProfile(NavigationAdmission.ExactProfileBinding(request.Profile),"distance",1,request.Profile.GridCell),
                new NavGraphCompileLimits(1048576,300000,1200000),
                new NavTerminalRegion("goal",1,endpoint,new[]{endpoint}),subscription,1048576);
            var key=root.RegisterOrdinary(request,recipe,NavLocation.FlatSurface,scope.OwnedReservation());
            var quota=new NavEpochQuota(16384,16384,16384,16384*3,256);
            for(long epoch=0;epoch<128&&root.Scheduler.ReadyCount==0;epoch++)
                root.Scheduler.Advance(epoch,quota,_=>true,16384);
            Assert.Greater(root.Scheduler.ReadyCount,0,"the actual physical scheduler must produce Ready");
            Assert.True(root.StageTakeReady(scope.OwnedReservation()));
            var projecting=(NavigationHostRow)root.Rows[key];
            Assert.AreEqual(NavSolveStatus.Ready,projecting.Solved.Status);
            var points=projecting.Solved.Route.Legs.Where(leg=>leg.Kind==NavTypedLegKind.Surface)
                .Select(leg=>leg.To.Position).ToArray();
            foreach(var leg in projecting.Solved.Route.Legs){
                var active=(NavigationHostRow)root.Rows[key];
                var step=active.ProjectionOwner.ForkOwned(scope.OwnedReservation());
                try{step.AdvanceLeg(leg,default);step.SealPrepared();
                    root.StageProjectionStep(key,step,scope.OwnedReservation());}
                finally{step.ReleaseStorage();}
            }
            root.StageReady(key,scope.OwnedReservation());
            root.SealPrepared();scope.Attach(root);scope.Commit(root,_=>{});
            Assert.AreEqual(Unique(root),hot.PhysicalBytes);
            var foreign=new NavigationRetentionBudget();
            Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Projection,3,1,out var rejected));
            var refusal=new Refusal(rejected,int.MaxValue);
            var hotBefore=hot.PhysicalBytes;
            var owned=(NavigationHostRow)root.Rows[key];
            Assert.Throws<ArgumentException>(()=>NavigationHostCorridor.CreateOwned(
                owned.RequestOwner,owned.ProviderOwner,owned.Scheduled,owned.Solved.Route,refusal));
            Assert.Zero(refusal.Calls);
            Assert.AreEqual(hotBefore,hot.PhysicalBytes);
            rejected.Discard();Assert.Zero(foreign.PhysicalBytes);
            byte[] bytes=CaptureGraph(hot,root);
            var cold=new NavigationRetentionBudget();
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,2,1,out var decode));
            NavigationHostRoot restored;
            using(var stream=new MemoryStream(bytes,false))using(var reader=new BinaryReader(stream))
                restored=(NavigationHostRoot)NavPhysicalBodyWire.ReadFrom(reader,
                    decode.OwnedReservation(),NavigationHostBodyExtension.Instance);
            decode.Attach(restored);decode.Commit(restored,_=>{});
            Assert.AreEqual(Unique(restored),cold.PhysicalBytes);
            var row=(NavigationHostRow)restored.Rows[key.Value];
            Assert.AreEqual(NavSolveStatus.Ready,row.Answer.Status);
            Assert.AreSame(((NavigationHostRequest)restored.Requests[key.Value]).Request,row.Answer.Request);
            CollectionAssert.AreEqual(points,row.Answer.CopyRoute());
            Assert.AreSame(row.Answer.Corridor,row.AnswerOwner.CorridorOwner.Corridor);
            Assert.AreSame(row.Scheduled.Subscription,
                row.AnswerOwner.CorridorOwner.TypedRoute.Subscription);
            Assert.AreSame(row.Solved.Route,row.AnswerOwner.CorridorOwner.TypedRoute);
            Assert.AreSame(row.Provider,row.AnswerOwner.CorridorOwner.ProviderOwner.Provider);
            Assert.AreEqual(0,restored.Projection.Count);
            Assert.AreEqual(key.Value,restored.Completion.First.Value);
            Assert.Zero(restored.Scheduler.ReadyCount);
            CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Projection,2,2,out var flushScope));
            var flushed=restored.ForkOwned(flushScope.OwnedReservation());
            flushed.StageFlushCompletion(key,flushScope.OwnedReservation());
            flushed.SealPrepared();flushScope.Attach(flushed);flushScope.Commit(flushed,_=>{});
            restored.ReleaseStorage();
            Assert.Zero(flushed.Rows.Count);Assert.Zero(flushed.Requests.Count);
            Assert.Zero(flushed.Providers.Count);Assert.Zero(flushed.Completion.Count);
            Assert.AreEqual(1,flushed.Delivered.Count);
            Assert.AreSame(row.Answer,((NavigationHostRow)flushed.Delivered[key.Value]).Answer);
            Assert.AreEqual(Unique(flushed),cold.PhysicalBytes);
            byte[] deliveredBytes=CaptureGraph(cold,flushed);
            var deliveredBudget=new NavigationRetentionBudget();
            Assert.True(deliveredBudget.TryOpenScope(NavigationPreparationKind.Decode,5,1,out var deliveredScope));
            NavigationHostRoot deliveredCold;
            using(var stream=new MemoryStream(deliveredBytes,false))using(var reader=new BinaryReader(stream))
                deliveredCold=(NavigationHostRoot)NavPhysicalBodyWire.ReadFrom(reader,
                    deliveredScope.OwnedReservation(),NavigationHostBodyExtension.Instance);
            deliveredScope.Attach(deliveredCold);deliveredScope.Commit(deliveredCold,_=>{});
            Assert.Zero(deliveredCold.Rows.Count);Assert.AreEqual(1,deliveredCold.Delivered.Count);
            Assert.AreEqual(NavSolveStatus.Ready,
                ((NavigationHostRow)deliveredCold.Delivered[key.Value]).Answer.Status);
            CollectionAssert.AreEqual(deliveredBytes,CaptureGraph(deliveredBudget,deliveredCold));
            deliveredCold.ReleaseStorage();Assert.Zero(deliveredBudget.PhysicalBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Projection,3,3,out var retireScope));
            var retired=flushed.ForkOwned(retireScope.OwnedReservation());
            retired.StageRetireDelivered(key,retireScope.OwnedReservation());
            retired.SealPrepared();retireScope.Attach(retired);retireScope.Commit(retired,_=>{});
            flushed.ReleaseStorage();
            Assert.Zero(retired.Delivered.Count);
            Assert.AreEqual(Unique(retired),cold.PhysicalBytes);
            retired.ReleaseStorage();root.ReleaseStorage();
            Assert.Zero(cold.PhysicalBytes);Assert.Zero(hot.PhysicalBytes);
        }
        [Test] public void PhysicalProjectionPrefixRestoresBeforeFinalization()
        {
            var hot=new NavigationRetentionBudget();
            Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));
            var root=NavigationHostRoot.CreateOwned(scope.OwnedReservation());
            var request=new NavigationRequest(7,11,4,3,new NavigationProfile(),
                new NavGeometry(12,Array.Empty<NavObstacle>(),1),new NavPoint(-2,0),new NavPoint(4,0),
                startLocation:new NavLocation(new NavPoint(-2,0),NavLocation.FlatSurface),
                goalLocation:new NavLocation(new NavPoint(4,0),NavLocation.FlatSurface));
            var provider=NavigationAdmission.CertifiedFlatProvider(request.Geometry);
            var origin=request.StartLocation.Value;var endpoint=request.GoalLocation.Value;
            var subscription=new NavRouteSubscription(7,4,1,1,0,5,4,2,5,3,11,origin,endpoint,null);
            var recipe=new NavScheduledRequest(provider,
                new NavClearanceProfile("ground",1,"ground",3,request.Profile.Radius),
                new NavGraphProfile(NavigationAdmission.ExactProfileBinding(request.Profile),"distance",1,request.Profile.GridCell),
                new NavGraphCompileLimits(1048576,300000,1200000),
                new NavTerminalRegion("goal",1,endpoint,new[]{endpoint}),subscription,1048576);
            var key=root.RegisterOrdinary(request,recipe,NavLocation.FlatSurface,scope.OwnedReservation());
            var quota=new NavEpochQuota(16384,16384,16384,16384*3,256);
            for(long epoch=0;epoch<128&&root.Scheduler.ReadyCount==0;epoch++)
                root.Scheduler.Advance(epoch,quota,_=>true,16384);
            Assert.Greater(root.Scheduler.ReadyCount,0);
            Assert.True(root.StageTakeReady(scope.OwnedReservation()));
            var row=(NavigationHostRow)root.Rows[key];
            Assert.Greater(row.Solved.Route.Legs.Count,0);
            var leg=row.Solved.Route.Legs[0];
            Assert.AreEqual(NavTypedLegKind.Surface,leg.Kind);
            var step=row.ProjectionOwner.ForkOwned(scope.OwnedReservation());
            try{step.AdvanceLeg(leg,default);step.SealPrepared();
                root.StageProjectionStep(key,step,scope.OwnedReservation());}
            finally{step.ReleaseStorage();}
            root.SealPrepared();scope.Attach(root);scope.Commit(root,_=>{});
            var bytes=CaptureGraph(hot,root);
            var cold=new NavigationRetentionBudget();
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,2,1,out var decode));
            NavigationHostRoot restored;
            using(var stream=new MemoryStream(bytes,false))using(var reader=new BinaryReader(stream))
                restored=(NavigationHostRoot)NavPhysicalBodyWire.ReadFrom(reader,
                    decode.OwnedReservation(),NavigationHostBodyExtension.Instance);
            decode.Attach(restored);decode.Commit(restored,_=>{});
            var prefix=((NavigationHostRow)restored.Rows[key.Value]).ProjectionOwner;
            Assert.AreEqual(1,prefix.LegCursor);
            Assert.AreEqual(1,prefix.PointCount);
            Assert.AreEqual(0,prefix.ReplayCursor);
            Assert.AreEqual(leg.To.Position,prefix.Point(0));
            CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Projection,3,1,out var continueScope));
            var continued=restored.ForkOwned(continueScope.OwnedReservation());
            var coldKey=continued.Projection.First;
            var continuation=(NavigationHostRow)continued.Rows[key.Value];
            for(int i=1;i<continuation.Solved.Route.Legs.Count;i++){
                var next=continuation.ProjectionOwner.ForkOwned(continueScope.OwnedReservation());
                try{next.AdvanceLeg(continuation.Solved.Route.Legs[i],default);next.SealPrepared();
                    continued.StageProjectionStep(coldKey,next,continueScope.OwnedReservation());}
                finally{next.ReleaseStorage();}
                continuation=(NavigationHostRow)continued.Rows[key.Value];
            }
            continued.StageReady(coldKey,continueScope.OwnedReservation());
            continued.SealPrepared();continueScope.Attach(continued);continueScope.Commit(continued,_=>{});
            Assert.AreEqual(NavSolveStatus.Ready,((NavigationHostRow)continued.Rows[key.Value]).Answer.Status);
            Assert.AreEqual(1,continued.Completion.Count);
            restored.ReleaseStorage();continued.ReleaseStorage();root.ReleaseStorage();
            Assert.Zero(hot.PhysicalBytes);Assert.Zero(cold.PhysicalBytes);
        }
        private static long Unique(NavOwnedStorage root)
        {
            var seen=new HashSet<NavOwnedStorage>();long bytes=0;
            void Visit(NavOwnedStorage value){
                if(value==null||!seen.Add(value))return;bytes+=value.StorageBytes;Assert.Greater(value.References,0);
                for(Type type=value.GetType();type!=typeof(NavOwnedStorage);type=type.BaseType)
                    foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly)){
                        var child=field.GetValue(value);Payload(child);
                        if(child is Array array)foreach(var item in array)Payload(item);
                    }
            }
            void Payload(object value){if(value is NavOwnedStorage storage)Visit(storage);else if(value is INavStorageFacade facade)Visit(facade.StorageRoot);else if(value is NavPhysicalReference reference)Visit(reference.Storage);else if(value is NavMetadataReference metadata)Visit(metadata.Owner);else if(value is NavNameReference name)Visit(name.Owner);else if(value is NavLocation location)Visit(location.NameReference.Owner);else if(value is NavGridCoordinate grid)Visit(grid.NameReference.Owner);else if(value is NavGraphNode node)Visit(node.Location.NameReference.Owner);else if(value is NavObstacle obstacle)Visit(obstacle.Polygon?.StorageRoot);else if(value is NavNameKey key)Visit(key.Reference.Owner);else if(value is NavGraphArc arc){Payload(arc.Trace);Visit(arc.NameReference.Owner);}else if(value is NavPackedFieldCell cell&&(cell.Flags&2)!=0)Payload(cell.Next);}
            Visit(root);return bytes;
        }
        private static NavRetainedArray<NavOwnedStorage> Source(NavigationRetentionBudget budget)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var scope));
            var a=NavCowDictionary<int,double>.CreateOwned(scope.OwnedReservation());
            foreach(int n in new[]{9,3,12,1,6,11,14,4,7})a.Add(n,n+.5);a.Seal();
            var b=a.ForkOwned(scope.OwnedReservation());b[6]=200;b.Remove(11);b.Add(18,18.5);b.Seal();
            var sameValues=NavRetainedArray<int>.Copy(new[]{7,8,9},scope.TryReserve);
            var distinct=NavRetainedArray<int>.Copy(new[]{7,8,9},scope.TryReserve);
            var bundle=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{a,b,a,sameValues,distinct,sameValues},scope.TryReserve);
            a.ReleaseStorage();b.ReleaseStorage();sameValues.Release();distinct.Release();
            scope.Attach(bundle);scope.Commit(bundle,_=>{});Assert.AreEqual(Unique(bundle),budget.PhysicalBytes);return bundle;
        }
        private static byte[] Capture(NavigationRetentionBudget budget,NavOwnedStorage root,int token=2)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,token,out var scope));
            try{
                using(var transport=NavStorageScratch.Reserve(scope.OwnedReservation(),2*131072))
                using(var stream=new MemoryStream(131072)){
                    using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))NavPhysicalBodyWire.WriteTo(writer,root,scope.OwnedReservation());
                    Assert.Less(stream.Length,131072);return stream.ToArray();
                }
            }finally{scope.Discard();}
        }
        // Actual graph BODY exceeds the small container fixture transport.
        // Use a separately admitted fixed 1MiB transport; product wire32MiB
        // and Session64MiB limits remain unchanged, as does Capture above.
        private static byte[] CaptureGraph(NavigationRetentionBudget budget,NavOwnedStorage root,int token=2)
        {
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,token,out var scope));
            try{using(var transport=NavStorageScratch.Reserve(scope.OwnedReservation(),2*1048576))using(var stream=new MemoryStream(1048576)){
                using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))NavPhysicalBodyWire.WriteTo(writer,root,scope.OwnedReservation());Assert.Less(stream.Length,1048576);return stream.ToArray();
            }}finally{scope.Discard();}
        }
        private static NavOwnedStorage Decode(byte[] bytes,INavStorageReservation reservation)
        {
            using(var stream=new MemoryStream(bytes,false))using(var reader=new BinaryReader(stream)){
                var root=NavPhysicalBodyWire.ReadFrom(reader,reservation);
                if(stream.Position!=stream.Length){root.ReleaseOwner();throw new ArgumentException("Trailing BODY bytes.");}return root;
            }
        }
        [TestCase("flat")][TestCase("layered")][TestCase("partition")]
        public void BodyActualOwnedGraphRestoresExactContainersBindingComponentsAndPackedTraces(string kind)
        {
            ILayeredNavigationProvider provider;
            if(kind=="partition"){var terrain=ThreeCrossingsMap.Default;provider=new PartitionNavigationProvider(terrain,new NavGeometry(terrain.HalfExtent,terrain.MovementBlockers.ToArray(),terrain.Revision),null);}
            else provider=kind=="layered"?(ILayeredNavigationProvider)new StackedNavigationFixture(true,NavPortalDirection.Forward).Provider:NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('a',64),"distance",1,kind=="partition"?16:kind=="layered"?8:1);var limits=new NavGraphCompileLimits(2000,2000,10000);
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,clearance,profile,limits,initial.OwnedReservation());int steps=0;while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready){Assert.Less(++steps,20000);compiler.Advance(1);}compiler.SealPrepared();var graph=compiler.Graph;Assert.True(graph.IsImmutableCaptureRoot);Assert.Greater(graph.Nodes.Count,0);
            NavOwnedStorage Field(LayeredNavigationGraph value,string name)=>(NavOwnedStorage)typeof(LayeredNavigationGraph).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
            string[] names=kind=="partition"?new[]{"ownedProvider","ownedBinding","partitionLocations","partitionComponents","partitionNeighbors","partitionIncoming","partitionArcSlots","partitionTraceSlots","partitionTraces"}:new[]{"ownedProvider","ownedBinding","sparseOutgoing","sparseIncoming","surfaceCosts"};
            var children=new List<NavOwnedStorage>{graph};foreach(var name in names)children.Add(Field(graph,name));if(kind!="partition"){children.Add((NavOwnedStorage)graph.Nodes);children.Add((NavOwnedStorage)graph.Arcs);children.Add((NavOwnedStorage)graph.PortalQualifications);}var bundle=NavRetainedArray<NavOwnedStorage>.Copy(children,initial.TryReserve);initial.Attach(bundle);compiler.ReleaseStorage();initial.Commit(bundle,_=>{});Assert.AreEqual(Unique(bundle),hot.PhysicalBytes);
            byte[] Observe(LayeredNavigationGraph value){using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){
                writer.Write(value.Binding.StableKey);writer.Write(value.SupportsPackedField);writer.Write(value.EstimatedRetainedBytes);writer.Write(value.Nodes.Count);foreach(var node in value.Nodes){writer.Write(node.Id);writer.Write(node.Component);NavContract.Location(writer,node.Location);}writer.Write(value.Arcs.Count);
                void Arc(NavGraphArc arc){writer.Write(arc.From);writer.Write(arc.To);writer.Write(arc.Cost);writer.Write((int)arc.Kind);writer.Write(arc.PortalId!=null);if(arc.PortalId!=null)writer.Write(arc.PortalId);writer.Write(arc.Trace?.Count??-1);if(arc.Trace!=null)foreach(var leg in arc.Trace){NavContract.Location(writer,leg.From);NavContract.Location(writer,leg.To);writer.Write(leg.Cost);writer.Write((int)leg.Kind);writer.Write(leg.PortalId!=null);if(leg.PortalId!=null)writer.Write(leg.PortalId);}}
                foreach(var arc in value.Arcs)Arc(arc);for(int n=0;n<value.Nodes.Count;n++){foreach(var adjacent in new[]{value.Outgoing(n),value.Incoming(n)}){writer.Write(adjacent.Count);foreach(var arc in adjacent)Arc(arc);}}
            }return stream.ToArray();}}
            byte[] expected=Observe(graph),bytes=CaptureGraph(hot,bundle);long charge=hot.PhysicalBytes;
            var foreign=new NavigationRetentionBudget();Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var foreignScope));var foreignCounter=new Refusal(foreignScope,int.MaxValue);int graphRefs=graph.References;
            if(kind=="partition")Assert.Throws<ArgumentException>(()=>LayeredNavigationGraph.FromPartition((PartitionNavigationProvider)((NavDeclarationStorage)Field(graph,"ownedProvider")).Facade,graph.Binding,(NavPagedList<NavLocation>)Field(graph,"partitionLocations"),(NavPagedList<int>)Field(graph,"partitionComponents"),(NavPackedSlots)Field(graph,"partitionNeighbors"),(NavPackedSlots)Field(graph,"partitionIncoming"),(NavPagedList<int>)Field(graph,"partitionArcSlots"),(NavPackedSlots)Field(graph,"partitionTraceSlots"),(NavPagedList<IReadOnlyList<NavTypedLeg>>)Field(graph,"partitionTraces"),0,foreignCounter.TryReserve));
            else Assert.Throws<ArgumentException>(()=>LayeredNavigationGraph.FromPrepared((ILayeredNavigationProvider)((NavDeclarationStorage)Field(graph,"ownedProvider")).Facade,graph.Binding,graph.Nodes,graph.Arcs,graph.PortalQualifications,(NavCowDictionary<int,IReadOnlyList<NavGraphArc>>)Field(graph,"sparseOutgoing"),(NavCowDictionary<int,IReadOnlyList<NavGraphArc>>)Field(graph,"sparseIncoming"),(NavCowDictionary<NavNameKey,double>)Field(graph,"surfaceCosts"),foreignCounter.TryReserve));
            Assert.Zero(foreignCounter.Calls);Assert.AreEqual(graphRefs,graph.References);foreignScope.Discard();Assert.Zero(foreign.PhysicalBytes);Assert.Zero(foreign.HeldBytes);Assert.AreEqual(charge,hot.PhysicalBytes);
            var malformed=new List<byte[]>();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Projection,1,10,out var corrupt));
            string corruptName=kind=="partition"?"partitionNeighbors":"sparseOutgoing";var original=Field(graph,corruptName);NavOwnedStorage replacement;
            if(kind=="partition"){var slots=(NavPackedSlots)original;Assert.Less(graph.Nodes.Count*4,slots.CapacitySlots);var fork=slots.Fork(corrupt.TryReserve);fork.Set(graph.Nodes.Count*4,1);fork.Seal();replacement=fork;}
            else {var empty=NavCowDictionary<int,IReadOnlyList<NavGraphArc>>.CreateOwned(corrupt.OwnedReservation());empty.Seal();replacement=empty;Assert.Greater(graph.Arcs.Count,0);}
            corrupt.Attach(replacement);corrupt.Commit(replacement,_=>{});var corruptField=typeof(LayeredNavigationGraph).GetField(corruptName,BindingFlags.Instance|BindingFlags.NonPublic);replacement.Retain();corruptField.SetValue(graph,replacement);original.Release();malformed.Add(CaptureGraph(hot,graph,11));original.Retain();corruptField.SetValue(graph,original);replacement.Release();replacement.ReleaseOwner();Assert.AreEqual(charge,hot.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,bundle,12));
            graph.BeforeAttachmentProbe=()=>{};Assert.Throws<ArgumentException>(()=>CaptureGraph(hot,graph,3));graph.BeforeAttachmentProbe=null;Assert.AreEqual(charge,hot.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(hot,bundle,4));bundle.Release();bundle=null;children=null;graph=null;compiler=null;provider=null;Assert.Zero(hot.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});var result=(LayeredNavigationGraph)restored[0];Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(expected,Observe(result));CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));for(int n=0;n<names.Length;n++)Assert.AreSame(restored[n+1],Field(result,names[n]));Assert.AreSame(((NavDeclarationStorage)result.Binding.StorageRoot).First,Field(result,"ownedProvider"));if(kind!="partition"){int start=names.Length+1;Assert.AreSame(restored[start],result.Nodes);Assert.AreSame(restored[start+1],result.Arcs);Assert.AreSame(restored[start+2],result.PortalQualifications);}else{Assert.True(result.Arcs.Any(arc=>arc.Trace!=null));foreach(var arc in result.Arcs)if(arc.Trace!=null)foreach(var leg in arc.Trace)Assert.True(leg.HasCanonicalProvider(Field(result,"ownedProvider")));}
            restored.Release();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            foreach(var invalid in malformed){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));Assert.Throws<ArgumentException>(()=>Decode(invalid,rejected.OwnedReservation()));rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var failed));var rejecting=new Refusal(failed,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,rejecting));Assert.AreEqual(nth,rejecting.Calls);failed.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
        }
        [TestCase(false)][TestCase(true)]
        public void BodyActualAdjacencyStagingAndPublishedMapsPreserveConcreteKindsSharedListsAndNextCow(bool layered)
        {
            ILayeredNavigationProvider provider=layered?(ILayeredNavigationProvider)new StackedNavigationFixture(true,NavPortalDirection.Forward).Provider:NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));
            var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);var profile=new NavGraphProfile(new string('e',64),"distance",1,layered?8:1);var limits=new NavGraphCompileLimits(2000,2000,10000);
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var compiler=LayeredNavigationCompileWork.CopyProviderAndCreateOwned(provider,clearance,profile,limits,initial.OwnedReservation());int steps=0;while(compiler.Phase!=LayeredNavigationCompileWork.Stage.Ready){Assert.Less(++steps,20000);compiler.Advance(1);}compiler.SealPrepared();
            NavOwnedStorage Field(string name)=>(NavOwnedStorage)typeof(LayeredNavigationCompileWork).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(compiler);
            var staging=(NavCowDictionary<int,NavPagedList<NavGraphArc>>)Field("outLists");var published=(NavCowDictionary<int,IReadOnlyList<NavGraphArc>>)Field("outgoing");Assert.Greater(staging.Count,0);Assert.AreEqual(staging.Count,published.Count);foreach(var pair in staging)Assert.AreSame(pair.Value,published[pair.Key]);Assert.AreNotEqual(staging.PhysicalTag,published.PhysicalTag);if(layered)Assert.True(compiler.Graph.Arcs.Any(arc=>arc.Kind==NavGraphArcKind.Portal));
            var first=staging.First();var concrete=NavRetainedArray<NavPagedList<NavGraphArc>>.Copy(new[]{first.Value,first.Value},initial.TryReserve);var readOnly=NavRetainedArray<IReadOnlyList<NavGraphArc>>.Copy(new IReadOnlyList<NavGraphArc>[]{first.Value,first.Value},initial.TryReserve);
            var bundle=NavRetainedArray<NavOwnedStorage>.Copy(new[]{Field("outLists"),Field("inLists"),Field("outgoing"),Field("incoming"),concrete,readOnly,first.Value},initial.TryReserve);concrete.Release();readOnly.Release();initial.Attach(bundle);compiler.ReleaseStorage();initial.Commit(bundle,_=>{});Assert.AreEqual(Unique(bundle),hot.PhysicalBytes);
            byte[] Cow(NavCowDictionary<int,NavPagedList<NavGraphArc>> mapArg,NavigationRetentionBudget budget,int token,out long delta){
                long before=budget.PhysicalBytes;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,token,out var cow));var fork=mapArg.ForkOwned(cow.OwnedReservation());var pair=mapArg.First();var list=pair.Value.Fork(cow.TryReserve);list.Add(pair.Value[0]);list.Seal();fork[pair.Key]=list;list.ReleaseStorage();fork.Seal();cow.Attach(fork);cow.Commit(fork,_=>{});delta=budget.PhysicalBytes-before;byte[] result=Capture(budget,fork,token+1);fork.ReleaseStorage();Assert.AreEqual(before,budget.PhysicalBytes);return result;
            }
            byte[] hotCow=Cow(staging,hot,20,out long hotDelta);byte[] bytes=Capture(hot,bundle);long charge=hot.PhysicalBytes;bundle.Release();bundle=null;compiler=null;provider=null;staging=null;published=null;concrete=null;readOnly=null;first=default;Assert.Zero(hot.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));var coldStaging=(NavCowDictionary<int,NavPagedList<NavGraphArc>>)restored[0];var coldPublished=(NavCowDictionary<int,IReadOnlyList<NavGraphArc>>)restored[2];foreach(var pair in coldStaging)Assert.AreSame(pair.Value,coldPublished[pair.Key]);Assert.AreSame(((NavRetainedArray<NavPagedList<NavGraphArc>>)restored[4])[0],((NavRetainedArray<IReadOnlyList<NavGraphArc>>)restored[5])[0]);Assert.AreSame(restored[6],((NavRetainedArray<IReadOnlyList<NavGraphArc>>)restored[5])[1]);
            byte[] coldCow=Cow(coldStaging,cold,20,out long coldDelta);Assert.AreEqual(hotDelta,coldDelta);CollectionAssert.AreEqual(hotCow,coldCow);restored.Release();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var failed));var rejecting=new Refusal(failed,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,rejecting));Assert.AreEqual(nth,rejecting.Calls);failed.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
        }
        [Test] public void ArcListPayloadsRejectPublicMutableForeignAndNullBeforeMapOrArrayAdmission()
        {
            var source=new NavigationRetentionBudget();Assert.True(source.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var foreignList=NavPagedList<NavGraphArc>.Create(initial.TryReserve,4);foreignList.Add(new NavGraphArc(0,1,1,NavGraphArcKind.Surface,null));foreignList.Seal();initial.Attach(foreignList);initial.Commit(foreignList,_=>{});int references=foreignList.References;long original=source.PhysicalBytes;
            var target=new NavigationRetentionBudget();Assert.True(target.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var attempt));var counter=new Refusal(attempt,int.MaxValue);var published=NavCowDictionary<int,IReadOnlyList<NavGraphArc>>.CreateOwned(counter);var staging=NavCowDictionary<int,NavPagedList<NavGraphArc>>.CreateOwned(counter);var mutable=NavPagedList<NavGraphArc>.Create(counter.TryReserve,4);var unowned=NavPagedList<NavGraphArc>.Create(null,4);unowned.Seal();counter.Calls=0;long physical=target.PhysicalBytes,held=attempt.HeldBytes;
            foreach(var value in new IReadOnlyList<NavGraphArc>[]{Array.AsReadOnly(Array.Empty<NavGraphArc>()),mutable,foreignList,unowned,null}){Assert.Throws<ArgumentException>(()=>published.Add(1,value));Assert.Throws<ArgumentException>(()=>NavRetainedArray<IReadOnlyList<NavGraphArc>>.Copy(new[]{value},counter.TryReserve));}
            foreach(var value in new[]{mutable,foreignList,unowned,null}){Assert.Throws<ArgumentException>(()=>staging.Add(1,value));Assert.Throws<ArgumentException>(()=>NavRetainedArray<NavPagedList<NavGraphArc>>.Copy(new[]{value},counter.TryReserve));}
            Assert.Zero(counter.Calls);Assert.Zero(published.Count);Assert.Zero(staging.Count);Assert.AreEqual(physical,target.PhysicalBytes);Assert.AreEqual(held,attempt.HeldBytes);Assert.AreEqual(references,foreignList.References);Assert.AreEqual(original,source.PhysicalBytes);unowned.ReleaseStorage();mutable.ReleaseStorage();published.ReleaseStorage();staging.ReleaseStorage();attempt.Discard();foreignList.ReleaseStorage();Assert.Zero(source.PhysicalBytes);Assert.Zero(target.PhysicalBytes);Assert.Zero(target.HeldBytes);
        }
        [Test] public void PartitionVerifierRejectsMissingLeaveReturnCrossingOnAdmittedAuthoredBoundary()
        {
            // This is an admitted source geometry, not the current gameplay
            // blocker set: zero-radius movement along a ramp's lower boundary
            // exercises a leave-and-return crossing without a surrogate map.
            var terrain=ThreeCrossingsMap.Default;var source=new PartitionNavigationProvider(terrain,new NavGeometry(terrain.HalfExtent,Array.Empty<NavObstacle>(),terrain.Revision),null);var clearance=new NavClearanceProfile("boundary",1,"ground",1,0);
            var ramp=terrain.Supports.Single(support=>support.SurfaceId=="ramp/-1/1/x");var start=new NavPoint(ramp.Origin.X,ramp.Bounds.MinZ-1);var finish=new NavPoint(ramp.Origin.X,ramp.Bounds.MaxZ+1);
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var provider=PartitionNavigationProvider.CopyOwned(source,initial.OwnedReservation());Assert.True(provider.TryLocate(start,clearance,out var from));Assert.True(provider.TryLocate(finish,clearance,out var to));Assert.AreEqual(from.SurfaceId,to.SurfaceId);
            Assert.True(provider.TryTraceOwned(from,to.Position,clearance,out var reached,out var trace,initial.OwnedReservation()));Assert.AreEqual(to,reached);Assert.AreEqual(2,trace.Count(leg=>leg.Kind==NavTypedLegKind.Portal));long physical=budget.PhysicalBytes,held=initial.HeldBytes;
            provider.VerifyPhysicalTrace(from,to,clearance,trace,initial.OwnedReservation());Assert.Throws<ArgumentException>(()=>provider.VerifyPhysicalTrace(from,to,clearance,null,initial.OwnedReservation()));Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,initial.HeldBytes);
            var wrongSelector=provider.Surfaces.Single(surface=>surface.Id==from.SurfaceId).OwnedLocation(from.Position);var counter=new Refusal(initial,int.MaxValue);Assert.Throws<ArgumentException>(()=>provider.VerifyPhysicalTrace(wrongSelector,to,clearance,trace,counter));Assert.Zero(counter.Calls);Assert.AreEqual(physical,budget.PhysicalBytes);((NavOwnedStorage)trace).ReleaseOwner();provider.StorageRoot.Release();initial.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);
        }
        [Test] public void BodyActualCanonicalPartitionLegsPreserveSurfaceRolesAndVerifyCrossingTranscriptWithoutEmitting()
        {
            var terrain=ThreeCrossingsMap.Default;var input=new PartitionNavigationProvider(terrain,new NavGeometry(terrain.HalfExtent,terrain.MovementBlockers.ToArray(),terrain.Revision),null);var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var provider=PartitionNavigationProvider.CopyOwned(input,initial.OwnedReservation());
            var routes=new[]{Tuple.Create(new NavPoint(-18,0),new NavPoint(18,0),true),Tuple.Create(new NavPoint(-20,0),new NavPoint(-18,0),false),Tuple.Create(new NavPoint(-20,0),new NavPoint(-20,0),false)};
            var roots=new List<NavOwnedStorage>{provider.StorageRoot};var savedTraces=new List<IReadOnlyList<NavTypedLeg>>();var endpoints=new List<NavLocation>();
            foreach(var route in routes){Assert.True(provider.TryLocate(route.Item1,clearance,out var from),"actual route origin "+route.Item1);Assert.True(provider.TryLocate(route.Item2,clearance,out var to),"actual route endpoint "+route.Item2);Assert.AreEqual(NavNameRole.SupportSurface,from.NameReference.Role);Assert.True(provider.TryTraceOwned(from,to.Position,clearance,out var reached,out var trace,initial.OwnedReservation()),"actual route traversal "+route.Item1+" -> "+route.Item2);Assert.AreEqual(to,reached);Assert.AreEqual(route.Item3,trace.Any(leg=>leg.Kind==NavTypedLegKind.Portal));if(route.Item1.Equals(route.Item2)){Assert.AreEqual(1,trace.Count);Assert.AreEqual(0,trace[0].Cost);Assert.AreEqual(NavTypedLegKind.Surface,trace[0].Kind);Assert.IsNull(trace[0].PortalId);}foreach(var leg in trace){Assert.True(leg.HasCanonicalProvider(provider.StorageRoot));Assert.AreEqual(NavNameRole.SurfaceId,leg.From.NameReference.Role);Assert.AreEqual(NavNameRole.SurfaceId,leg.To.NameReference.Role);}
                long physical=hot.PhysicalBytes,held=initial.HeldBytes;provider.VerifyPhysicalTrace(from,to,clearance,trace,initial.OwnedReservation());Assert.AreEqual(physical,hot.PhysicalBytes);Assert.AreEqual(held,initial.HeldBytes);
                if(route.Item3)Assert.Throws<ArgumentException>(()=>provider.VerifyPhysicalTrace(from,to,clearance,null,initial.OwnedReservation()));else provider.VerifyPhysicalTrace(from,to,clearance,null,initial.OwnedReservation());
                roots.Add((NavOwnedStorage)trace);savedTraces.Add(trace);endpoints.Add(from);endpoints.Add(to);
            }
            var locations=NavRetainedArray<NavLocation>.Copy(endpoints,initial.TryReserve);roots.Add(locations);roots.Add(roots[1]);var first=savedTraces[0][0];var privateLeg=NavTypedLeg.CreateOwned(first.From.Detached(),first.To.Detached(),first.Cost,first.Kind,first.PortalId,initial.OwnedReservation());roots.Add(privateLeg);
            var bundle=NavRetainedArray<NavOwnedStorage>.Copy(roots,initial.TryReserve);provider.StorageRoot.Release();foreach(var trace in savedTraces)((NavOwnedStorage)trace).ReleaseOwner();locations.Release();privateLeg.ReleaseOwner();initial.Attach(bundle);initial.Commit(bundle,_=>{});Assert.AreEqual(Unique(bundle),hot.PhysicalBytes);byte[] bytes=Capture(hot,bundle);long charge=hot.PhysicalBytes;
            roots=null;savedTraces=null;endpoints=null;first=null;privateLeg=null;locations=null;provider=null;input=null;terrain=null;bundle.Release();bundle=null;Assert.Zero(hot.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));var owner=(PartitionNavigationProvider)((NavDeclarationStorage)restored[0]).Facade;var nodes=(NavRetainedArray<NavLocation>)restored[routes.Length+1];Assert.AreSame(restored[1],restored[routes.Length+2]);Assert.AreEqual(NavNameRole.Text,((NavTypedLeg)restored[routes.Length+3]).From.NameReference.Role);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Projection,1,3,out var verify));var counter=new Refusal(verify,int.MaxValue);long before=cold.PhysicalBytes,credit=verify.HeldBytes;
            for(int n=0;n<routes.Length;n++){var trace=(NavPagedList<NavTypedLeg>)restored[n+1];owner.VerifyPhysicalTrace(nodes[2*n],nodes[2*n+1],clearance,trace,counter);foreach(var leg in trace){Assert.True(leg.HasCanonicalProvider(owner.StorageRoot));Assert.AreEqual(NavNameRole.SurfaceId,leg.From.NameReference.Role);Assert.AreEqual(NavNameRole.SurfaceId,leg.To.NameReference.Role);}}
            Assert.AreEqual(routes.Length,counter.Calls,"Verifier must admit one scratch lease per route and no retained leg/container allocations.");Assert.AreEqual(before,cold.PhysicalBytes);Assert.AreEqual(credit,verify.HeldBytes);
            var real=(NavPagedList<NavTypedLeg>)restored[1];var wrong=NavRetainedArray<NavTypedLeg>.Copy(real.Reverse().ToArray(),verify.TryReserve);Assert.Throws<ArgumentException>(()=>owner.VerifyPhysicalTrace(nodes[0],nodes[1],clearance,wrong,verify.OwnedReservation()));wrong.Release();verify.Discard();Assert.AreEqual(before,cold.PhysicalBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Projection,1,4,out var refuse));var rejecting=new Refusal(refuse,1);Assert.Throws<InvalidOperationException>(()=>owner.VerifyPhysicalTrace(nodes[0],nodes[1],clearance,real,rejecting));Assert.AreEqual(1,rejecting.Calls);refuse.Discard();Assert.AreEqual(before,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored,5));
            var foreign=new NavigationRetentionBudget();Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var foreignScope));var other=PartitionNavigationProvider.CopyOwned(new PartitionNavigationProvider(ThreeCrossingsMap.Default,new NavGeometry(ThreeCrossingsMap.Default.HalfExtent,ThreeCrossingsMap.Default.MovementBlockers.ToArray(),ThreeCrossingsMap.Default.Revision),null),foreignScope.OwnedReservation());Assert.True(other.TryLocate(routes[0].Item1,clearance,out var foreignFrom));Assert.True(other.TryLocate(routes[0].Item2,clearance,out var foreignTo));var foreignCounter=new Refusal(foreignScope,int.MaxValue);Assert.Throws<ArgumentException>(()=>other.VerifyPhysicalTrace(foreignFrom,foreignTo,clearance,real,foreignCounter));Assert.Zero(foreignCounter.Calls);other.StorageRoot.Release();foreignScope.Discard();Assert.Zero(foreign.PhysicalBytes);Assert.Zero(foreign.HeldBytes);
            restored.Release();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,6,out var probe));var probeCounter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,probeCounter);probe.Attach(trial);int total=probeCounter.Calls;probe.Discard();for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
        }
        [TestCase(false)][TestCase(true)]
        public void BodyActualPartitionProviderPreservesEightEdgesCoverageAliasesColdChargeAndEveryNthRefusal(bool foundry)
        {
            foreach(var hold in new[]{null,""," "}){
                IPlayableTerrain terrain=foundry?(IPlayableTerrain)new FoundryMap(new FoundryProfileData{scale=1.5}):ThreeCrossingsMap.Default;
                var input=new PartitionNavigationProvider(terrain,new NavGeometry(terrain.HalfExtent,terrain.MovementBlockers.ToArray(),terrain.Revision),hold);string binding=input.Binding;
                var clearance=new NavClearanceProfile("ground",1,"ground",1,.25);
                var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var provider=PartitionNavigationProvider.CopyOwned(input,initial.OwnedReservation());var shared=PartitionNavigationProvider.CopyOwned(provider,initial.OwnedReservation());
                var root=(NavDeclarationStorage)provider.StorageRoot;var locationRoot=(NavDeclarationStorage)root.First;var locations=(TerrainLocations)locationRoot.Facade;
                var bundle=NavRetainedArray<NavOwnedStorage>.Copy(new[]{provider.StorageRoot,shared.StorageRoot,locationRoot,locationRoot.Second,locationRoot.Third,locationRoot.Fourth,locationRoot.Fifth,locationRoot.Sixth,locationRoot.Seventh,locationRoot.Eighth},initial.TryReserve);provider.StorageRoot.Release();shared.StorageRoot.Release();initial.Attach(bundle);initial.Commit(bundle,_=>{});Assert.AreEqual(Unique(bundle),hot.PhysicalBytes);
                var probes=new List<Tuple<NavPoint,bool,NavLocation,bool,NavLocation,bool,NavLocation,Tuple<IReadOnlyList<NavTypedLeg>>>>();
                for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++){
                    var point=new NavPoint(terrain.HalfExtent*x/4,terrain.HalfExtent*z/4);bool located=provider.TryLocate(point,clearance,out var from);var to=new NavPoint(point.X+.1,point.Z+.1);bool traverse=located&&provider.TryTraverse(from,to,clearance,out _);NavLocation reached=default,traceReached=default;IReadOnlyList<NavTypedLeg> trace=null;if(located)provider.TryTraverse(from,to,clearance,out reached);bool traced=located&&provider.TryTrace(from,to,clearance,out traceReached,out trace);probes.Add(Tuple.Create(point,located,from.Detached(),traverse,reached.Detached(),traced,traceReached.Detached(),trace==null?null:(IReadOnlyList<NavTypedLeg>)trace.Select(leg=>leg.Detached()).ToArray()));
                }
                byte[] Cow(NavCowDictionary<NavNameKey,int> mapArg,NavigationRetentionBudget budget,int token,out long delta){
                    long before=budget.PhysicalBytes;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,token,out var cow));var fork=mapArg.ForkOwned(cow.OwnedReservation());var first=mapArg.First();fork[first.Key]=first.Value+7;fork.Seal();cow.Attach(fork);cow.Commit(fork,_=>{});delta=budget.PhysicalBytes-before;byte[] result=Capture(budget,fork,token+1);fork.ReleaseStorage();Assert.AreEqual(before,budget.PhysicalBytes);return result;
                }
                byte[] hotCow=Cow((NavCowDictionary<NavNameKey,int>)locationRoot.Fourth,hot,20,out long hotDelta);
                byte[] bytes=Capture(hot,bundle);long charge=hot.PhysicalBytes;
                // Delete exterior coverage with the same retained Binding and
                // internally consistent replacement array. Digest checks alone
                // cannot reject this; the interval coverage audit must do so.
                var oldExterior=(NavRetainedArray<TerrainLocations.Seam>)locationRoot.Sixth;Assert.Greater(oldExterior.Count,0);var exteriorField=typeof(TerrainLocations).GetField("exterior",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert.True(hot.TryOpenScope(NavigationPreparationKind.Projection,1,3,out var replacementScope));var shortened=NavRetainedArray<TerrainLocations.Seam>.Copy(oldExterior.Take(oldExterior.Count-1).ToArray(),replacementScope.TryReserve);replacementScope.Attach(shortened);replacementScope.Commit(shortened,_=>{});
                oldExterior.Retain();shortened.Retain();locationRoot.Sixth=shortened;oldExterior.Release();exteriorField.SetValue(locations,shortened);byte[] missingExterior=Capture(hot,bundle,4);
                oldExterior.Retain();locationRoot.Sixth=oldExterior;shortened.Release();exteriorField.SetValue(locations,oldExterior);oldExterior.Release();shortened.Release();Assert.AreEqual(charge,hot.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(hot,bundle,5));
                var idField=typeof(PartitionNavigationProvider).GetField("terrainId",BindingFlags.Instance|BindingFlags.NonPublic);string originalId=provider.TerrainId;idField.SetValue(provider,new string('x',originalId.Length));byte[] wrongIdentity=Capture(hot,bundle,6);idField.SetValue(provider,originalId);
                bundle.Release();bundle=null;provider=null;shared=null;root=null;locationRoot=null;locations=null;input=null;terrain=null;oldExterior=null;shortened=null;originalId=null;Assert.Zero(hot.PhysicalBytes);
                var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));
                var providerRoot=(NavDeclarationStorage)restored[0];var restoredProvider=(PartitionNavigationProvider)providerRoot.Facade;var sharedProvider=(PartitionNavigationProvider)((NavDeclarationStorage)restored[1]).Facade;var retainedLocations=(NavDeclarationStorage)restored[2];var restoredLocations=(TerrainLocations)retainedLocations.Facade;
                Assert.AreSame(providerRoot.First,((NavDeclarationStorage)restored[1]).First);Assert.AreSame(restoredProvider.Surfaces[0],sharedProvider.Surfaces[0]);Assert.AreSame(retainedLocations,providerRoot.First);Assert.AreSame(retainedLocations.Second,restored[3]);Assert.AreSame(retainedLocations.Third,restored[4]);Assert.AreSame(retainedLocations.Fourth,restored[5]);Assert.AreSame(retainedLocations.Fifth,restored[6]);Assert.AreSame(retainedLocations.Sixth,restored[7]);Assert.AreSame(retainedLocations.Seventh,restored[8]);Assert.AreSame(retainedLocations.Eighth,restored[9]);Assert.AreSame(restoredLocations.Seams,restoredProvider.Seams);Assert.AreEqual(binding,restoredProvider.Binding);Assert.AreEqual(hold,restoredProvider.HoldBinding);
                foreach(var probe in probes){bool located=restoredProvider.TryLocate(probe.Item1,clearance,out var from);Assert.AreEqual(probe.Item2,located);Assert.AreEqual(probe.Item3,from);if(!located)continue;var to=new NavPoint(probe.Item1.X+.1,probe.Item1.Z+.1);Assert.AreEqual(probe.Item4,restoredProvider.TryTraverse(from,to,clearance,out var reached));Assert.AreEqual(probe.Item5,reached);Assert.AreEqual(probe.Item6,restoredProvider.TryTrace(from,to,clearance,out var tracedReached,out var trace));Assert.AreEqual(probe.Item7,tracedReached);var expectedTrace=probe.Rest.Item1;if(expectedTrace==null)Assert.IsNull(trace);else{Assert.AreEqual(expectedTrace.Count,trace.Count);for(int n=0;n<trace.Count;n++){Assert.AreEqual(expectedTrace[n].From,trace[n].From);Assert.AreEqual(expectedTrace[n].To,trace[n].To);Assert.AreEqual(expectedTrace[n].Cost,trace[n].Cost);Assert.AreEqual(expectedTrace[n].Kind,trace[n].Kind);Assert.AreEqual(expectedTrace[n].PortalId,trace[n].PortalId);}}}
                byte[] coldCow=Cow((NavCowDictionary<NavNameKey,int>)retainedLocations.Fourth,cold,20,out long coldDelta);Assert.AreEqual(hotDelta,coldDelta);CollectionAssert.AreEqual(hotCow,coldCow);
                restored.Release();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
                foreach(var malformed in new[]{missingExterior,wrongIdentity}){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var failed));Assert.Throws<ArgumentException>(()=>Decode(malformed,failed.OwnedReservation()));failed.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var probeScope));var counter=new Refusal(probeScope,int.MaxValue);var trial=Decode(bytes,counter);probeScope.Attach(trial);int total=counter.Calls;probeScope.Discard();
                for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
            }
        }
        [TestCase(false)][TestCase(true)]
        public void BodyPartitionSurfaceIndexRetainsExactSupportNamesAfterProviderRetirement(bool foundry)
        {
            IPlayableTerrain terrain=foundry?(IPlayableTerrain)new FoundryMap(new FoundryProfileData{scale=1.5}):ThreeCrossingsMap.Default;
            var source=new PartitionNavigationProvider(terrain,new NavGeometry(terrain.HalfExtent,terrain.MovementBlockers.ToArray(),terrain.Revision),null);
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var provider=PartitionNavigationProvider.CopyOwned(source,initial.OwnedReservation());initial.Attach(provider.StorageRoot);initial.Commit(provider.StorageRoot,_=>{});
            var locations=(TerrainLocations)((NavDeclarationStorage)((NavDeclarationStorage)provider.StorageRoot).First).Facade;
            var index=(NavCowDictionary<NavNameKey,int>)locations.SurfaceIndex;index.Retain();
            foreach(var pair in index){Assert.AreEqual(NavNameRole.SupportSurface,pair.Key.Reference.Role);Assert.AreSame(locations.Supports[pair.Value].StorageRoot,pair.Key.Reference.Owner);Assert.AreSame(locations.Supports[pair.Value].SurfaceId,pair.Key.Value);Assert.AreEqual(pair.Value,index[new NavNameKey(new string(pair.Key.Value.ToCharArray()))]);}
            provider.StorageRoot.Release();provider=null;source=null;locations=null;terrain=null;Assert.AreEqual(Unique(index),hot.PhysicalBytes);
            byte[] bytes=Capture(hot,index);long charge=hot.PhysicalBytes;index.ReleaseStorage();index=null;Assert.Zero(hot.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavCowDictionary<NavNameKey,int>)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));
            foreach(var pair in restored){var support=(MapSupport)((NavDeclarationStorage)pair.Key.Reference.Owner).Facade;Assert.AreEqual(NavNameRole.SupportSurface,pair.Key.Reference.Role);Assert.AreSame(support.SurfaceId,pair.Key.Value);Assert.AreEqual(pair.Value,restored[new NavNameKey(new string(pair.Key.Value.ToCharArray()))]);}
            restored.ReleaseStorage();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();
            for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
        }
        [Test] public void BodyMetadataOwnerRolesPreserveExactFieldsDistinctEqualAggregatesAndPrivateStrings()
        {
            var c=new NavClearanceProfile(new string("radius".ToCharArray()),1,new string("ground".ToCharArray()),1,.58);
            var p=new NavGraphProfile(new string('a',64),new string("distance".ToCharArray()),1,.8);var l=new NavGraphCompileLimits(100,100,1000);
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));
            var a=NavGraphMetadata.CopyOwned(c,p,l,initial.OwnedReservation());var b=NavGraphMetadata.CopyOwned(c,p,l,initial.OwnedReservation());
            Assert.AreNotSame(c,a.Clearance);Assert.AreNotSame(c.Id,a.Clearance.Id);Assert.AreNotSame(p.ExactProfileBinding,a.Profile.ExactProfileBinding);Assert.AreNotSame(l,a.Limits);
            var roles=NavRetainedArray<NavMetadataReference>.Copy(new[]{NavMetadataReference.From(a.Clearance,initial.OwnedReservation()),NavMetadataReference.From(a.Profile,initial.OwnedReservation()),NavMetadataReference.From(a.Limits,initial.OwnedReservation()),NavMetadataReference.From(a.Clearance,initial.OwnedReservation()),NavMetadataReference.From(b.Clearance,initial.OwnedReservation()),default(NavMetadataReference)},initial.TryReserve);
            a.Release();b.Release();initial.Attach(roles);initial.Commit(roles,_=>{});Assert.AreEqual(Unique(roles),hot.PhysicalBytes);
            byte[] bytes=Capture(hot,roles);long charge=hot.PhysicalBytes;roles.Release();Assert.Zero(hot.PhysicalBytes);roles=null;a=null;b=null;c=null;p=null;l=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavRetainedArray<NavMetadataReference>)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});
            var owner=(NavGraphMetadata)restored[0].Owner;Assert.AreSame(owner.Clearance,restored[0].Value);Assert.AreSame(owner.Profile,restored[1].Value);Assert.AreSame(owner.Limits,restored[2].Value);Assert.AreSame(restored[0].Value,restored[3].Value);Assert.AreNotSame(restored[0].Owner,restored[4].Owner);Assert.AreEqual(((NavClearanceProfile)restored[0].Value).Id,((NavClearanceProfile)restored[4].Value).Id);Assert.AreNotSame(((NavClearanceProfile)restored[0].Value).Id,((NavClearanceProfile)restored[4].Value).Id);Assert.IsNull(restored[5].Value);
            Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));restored.Release();Assert.Zero(cold.PhysicalBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();Assert.Zero(cold.PhysicalBytes);
            for(int nth=1;nth<=total;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
            }
        }
        [TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)][TestCase(7)]
        public void BodyPartialMetadataAggregatesAdmitOnlyPresentFieldsAndColdRestoreExactMask(int mask)
        {
            var c=new NavClearanceProfile("ground",1,"ground",1,.25);var p=new NavGraphProfile(new string('c',64),"distance",1,1);var l=new NavGraphCompileLimits(256,256,1024);var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var owned=NavGraphMetadata.CopyOwned(c,p,l,initial.OwnedReservation());var partial=NavGraphMetadata.CopyUnowned((mask&1)!=0?c:owned.Clearance,(mask&2)!=0?p:owned.Profile,(mask&4)!=0?l:owned.Limits,initial.OwnedReservation());owned.Release();initial.Attach(partial);initial.Commit(partial,_=>{});
            long charge=hot.PhysicalBytes;byte[] bytes=Capture(hot,partial);partial.Release();Assert.Zero(hot.PhysicalBytes);partial=null;owned=null;c=null;p=null;l=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavGraphMetadata)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});Assert.AreEqual((mask&1)!=0,restored.Clearance!=null);Assert.AreEqual((mask&2)!=0,restored.Profile!=null);Assert.AreEqual((mask&4)!=0,restored.Limits!=null);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));restored.Release();Assert.Zero(cold.PhysicalBytes);
        }
        [Test] public void MetadataForeignOwnersAndContradictoryRolesRejectBeforeAllocationOrRetain()
        {
            var c=new NavClearanceProfile("radius",1,"ground",1,.58);var p=new NavGraphProfile(new string('a',64),"distance",1,.8);var l=new NavGraphCompileLimits(100,100,1000);
            var first=new NavigationRetentionBudget();Assert.True(first.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var owner=NavGraphMetadata.CopyOwned(c,p,l,initial.OwnedReservation());initial.Attach(owner);initial.Commit(owner,_=>{});
            var other=new NavigationRetentionBudget();Assert.True(other.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var foreign));long held=foreign.HeldBytes;int refs=owner.References;
            Assert.Throws<ArgumentException>(()=>NavMetadataReference.From(owner.Clearance,foreign.OwnedReservation()));Assert.Throws<ArgumentException>(()=>NavGraphMetadata.CopyOwned(c,owner.Profile,l,foreign.OwnedReservation()));Assert.AreEqual(held,foreign.HeldBytes);Assert.Zero(other.PhysicalBytes);Assert.AreEqual(refs,owner.References);foreign.Discard();
            Assert.Throws<ArgumentException>(()=>new NavMetadataReference(owner,NavMetadataRole.Subscription));Assert.Throws<ArgumentException>(()=>new NavMetadataReference(owner,(NavMetadataRole)99));Assert.Throws<ArgumentException>(()=>new NavMetadataReference(null,NavMetadataRole.Clearance));
            owner.Release();Assert.Zero(first.PhysicalBytes);Assert.Throws<ArgumentException>(()=>NavMetadataOwnership.Resolve(owner,NavMetadataRole.Clearance));
        }
        [TestCase(0)][TestCase(3)][TestCase(99)]
        public void BodyMetadataMalformedRoleFailsClosedAndReleasesDecodedAggregate(int role)
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var metadata=NavGraphMetadata.CopyOwned(new NavClearanceProfile("radius",1,"ground",1,.58),new NavGraphProfile(new string('a',64),"distance",1,.8),new NavGraphCompileLimits(100,100,1000),initial.OwnedReservation());
            var root=NavRetainedArray<NavMetadataReference>.Copy(new[]{NavMetadataReference.From(metadata.Clearance,initial.OwnedReservation())},initial.TryReserve);metadata.Release();initial.Attach(root);initial.Commit(root,_=>{});byte[] bytes=Capture(hot,root);root.Release();Assert.Zero(hot.PhysicalBytes);Array.Copy(BitConverter.GetBytes(role),0,bytes,bytes.Length-4,4);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));Assert.Throws<ArgumentException>(()=>Decode(bytes,scope.OwnedReservation()));scope.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.False(cold.CaptureActive);
        }
        [Test] public void BodyGraphBindingOwnsCanonicalProviderMetadataAndRejectsDigestMismatchEveryNthRefusal()
        {
            var input=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));var c=new NavClearanceProfile("ground",1,"ground",1,.25);var p=new NavGraphProfile(new string('c',64),"distance",1,1);var l=new NavGraphCompileLimits(256,256,1024);
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var providerA=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());var providerB=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());var metadataA=NavGraphMetadata.CopyOwned(c,p,l,initial.OwnedReservation());var metadataB=NavGraphMetadata.CopyOwned(c,p,l,initial.OwnedReservation());var a=NavGraphBinding.CreatePhysical(providerA,metadataA.Clearance,metadataA.Profile,initial.OwnedReservation());var b=NavGraphBinding.CreatePhysical(providerB,metadataB.Clearance,metadataB.Profile,initial.OwnedReservation());Assert.AreEqual(a.StableKey,b.StableKey);Assert.AreNotSame(a,b);
            var bundle=NavRetainedArray<NavOwnedStorage>.Copy(new[]{a.StorageRoot,b.StorageRoot,a.StorageRoot,metadataA,providerA.StorageRoot},initial.TryReserve);providerA.StorageRoot.Release();providerB.StorageRoot.Release();metadataA.Release();metadataB.Release();initial.Attach(bundle);initial.Commit(bundle,_=>{});Assert.AreEqual(Unique(bundle),hot.PhysicalBytes);
            long charge=hot.PhysicalBytes;byte[] bytes=Capture(hot,bundle);bundle.Release();Assert.Zero(hot.PhysicalBytes);bundle=null;a=null;b=null;providerA=null;providerB=null;metadataA=null;metadataB=null;c=null;p=null;l=null;input=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});var first=(NavGraphBinding)((NavDeclarationStorage)restored[0]).Facade;var second=(NavGraphBinding)((NavDeclarationStorage)restored[1]).Facade;var provider=(LayeredNavigationProvider)((NavDeclarationStorage)restored[4]).Facade;var metadata=(NavGraphMetadata)restored[3];
            Assert.AreSame(restored[0],restored[2]);Assert.AreNotSame(first,second);Assert.AreEqual(first.StableKey,second.StableKey);Assert.AreNotSame(first.StableKey,second.StableKey);Assert.AreSame(provider.Binding,first.ProviderBinding);Assert.AreSame(metadata.Clearance,first.Clearance);Assert.AreSame(metadata.Profile,first.Profile);Assert.AreNotSame(first.Clearance,second.Clearance);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));restored.Release();Assert.Zero(cold.PhysicalBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();Assert.Zero(cold.PhysicalBytes);
            for(int nth=1;nth<=total;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
            }
            var bad=(byte[])bytes.Clone();int keyPosition=8+9+4+9+4;char digit=(char)BitConverter.ToUInt16(bad,keyPosition);Assert.True("0123456789abcdef".Contains(digit));Array.Copy(BitConverter.GetBytes((ushort)(digit=='0'?'1':'0')),0,bad,keyPosition,2);
            var invalid=new NavigationRetentionBudget();Assert.True(invalid.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var attempt));Assert.Throws<ArgumentException>(()=>Decode(bad,attempt.OwnedReservation()));attempt.Discard();Assert.Zero(invalid.PhysicalBytes);Assert.Zero(invalid.HeldBytes);Assert.False(invalid.CaptureActive);
        }
        [TestCase(false)][TestCase(true)]
        public void BodyConnectorCanonicalRolesPreservePortalEndpointNamesDistinctFromEqualSurfaceIds(bool open)
        {
            var polygon=new NavPolygon(new[]{new NavPoint(-2,-2),new NavPoint(2,-2),new NavPoint(2,2),new NavPoint(-2,2)});var area=new NavTraversalArea(polygon,2,Array.Empty<NavObstacle>());var surfaces=new[]{new NavLayerSurface("a","ground",0,1,area),new NavLayerSurface("b","ground",1,1,area)};var portal=new NavLayerPortal("ab",new NavLocation(new NavPoint(0,0),"a"),new NavLocation(new NavPoint(0,0),"b"),NavPortalDirection.Both,open,1,area,new[]{"ground"});var input=new LayeredNavigationProvider("body",1,1,1,new string('a',64),surfaces,new[]{portal});var clearance=new NavClearanceProfile("ground",1,"ground",1,.1);
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var provider=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());var q=provider.QualifyPortalOwned(provider.Portals[0].Id,clearance,initial.TryReserve);Assert.AreEqual(open?NavConnectorStatus.Ready:NavConnectorStatus.Closed,q.Status);Assert.AreSame(provider.Portals[0].Entry.SurfaceId,q.Entry.SurfaceId);Assert.AreNotSame(provider.Surfaces[0].Id,q.Entry.SurfaceId);var list=NavRetainedArray<NavConnectorResult>.Copy(new[]{q,q},initial.TryReserve);var root=NavRetainedArray<NavOwnedStorage>.Copy(new[]{(NavOwnedStorage)list,provider.StorageRoot,q},initial.TryReserve);list.Release();q.ReleaseOwner();provider.StorageRoot.Release();initial.Attach(root);initial.Commit(root,_=>{});long charge=hot.PhysicalBytes;Assert.AreEqual(Unique(root),charge);byte[] bytes=Capture(hot,root);root.Release();Assert.Zero(hot.PhysicalBytes);root=null;list=null;q=null;provider=null;input=null;surfaces=null;portal=null;area=null;polygon=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});var qs=(NavRetainedArray<NavConnectorResult>)restored[0];var pr=(LayeredNavigationProvider)((NavDeclarationStorage)restored[1]).Facade;var cq=qs[0];Assert.AreSame(qs[0],qs[1]);Assert.AreSame(restored[2],cq);Assert.AreSame(pr.Binding,cq.ProviderBinding);Assert.AreSame(pr.Portals[0].Id,cq.PortalId);Assert.AreSame(pr.Portals[0].Entry.SurfaceId,cq.Entry.SurfaceId);Assert.AreSame(pr.Portals[0].StorageRoot,cq.Entry.NameReference.Owner);Assert.AreEqual(NavNameRole.PortalEntrySurface,cq.Entry.NameReference.Role);Assert.AreEqual(NavNameRole.PortalExitSurface,cq.Exit.NameReference.Role);Assert.AreSame(pr.Portals[0].StorageRoot,cq.Exit.NameReference.Owner);Assert.AreSame(cq.Origin.SurfaceId,cq.Entry.SurfaceId);Assert.AreSame(pr.Portals[0].Exit.SurfaceId,cq.Exit.SurfaceId);Assert.AreSame(cq.Endpoint.SurfaceId,cq.Exit.SurfaceId);Assert.AreNotSame(pr.Surfaces[0].Id,cq.Entry.SurfaceId);Assert.AreEqual(pr.Surfaces[0].Id,cq.Entry.SurfaceId);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));restored.Release();Assert.Zero(cold.PhysicalBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();
            for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);}
        }
        [Test] public void BodyConnectorPrivateNamesKeepAliasesNullableUnknownAndRejectMalformedRoleCycle()
        {
            string binding=new string('a',64),name=new string("private".ToCharArray());var at=new NavLocation(new NavPoint(0,0),name);var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var q=NavConnectorResult.CreatePhysical(NavConnectorStatus.UnknownPortal,binding,name,at,at,default,default,0,initial.OwnedReservation(),null);initial.Attach(q);initial.Commit(q,_=>{});Assert.AreNotSame(binding,q.ProviderBinding);Assert.AreNotSame(name,q.PortalId);Assert.AreSame(q.PortalId,q.Origin.SurfaceId);Assert.AreSame(q.Origin.SurfaceId,q.Entry.SurfaceId);Assert.IsNull(q.Exit.SurfaceId);long charge=hot.PhysicalBytes;byte[] bytes=Capture(hot,q);q.ReleaseOwner();Assert.Zero(hot.PhysicalBytes);q=null;binding=null;name=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var cq=(NavConnectorResult)Decode(bytes,scope.OwnedReservation());scope.Attach(cq);scope.Commit(cq,_=>{});Assert.AreEqual(NavConnectorStatus.UnknownPortal,cq.Status);Assert.AreSame(cq.PortalId,cq.Origin.SurfaceId);Assert.AreSame(cq.Entry.SurfaceId,cq.Origin.SurfaceId);Assert.AreSame(cq,cq.Origin.NameReference.Owner);Assert.AreSame(cq,cq.Entry.NameReference.Owner);Assert.AreEqual(NavNameRole.ConnectorPrivateSlot,cq.Origin.NameReference.Role);Assert.IsNull(cq.Exit.SurfaceId);Assert.IsNull(cq.Endpoint.SurfaceId);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(cq),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,cq));cq.ReleaseOwner();Assert.Zero(cold.PhysicalBytes);
            foreach(bool cycle in new[]{false,true}){var bad=(byte[])bytes.Clone();bad[17]=cycle?(byte)NavConnectorNameRole.PrivateAlias:(byte)255;if(cycle)Array.Copy(BitConverter.GetBytes(0),0,bad,18,4);Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var reject));Assert.Throws<ArgumentException>(()=>Decode(bad,reject.OwnedReservation()));reject.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);Assert.False(cold.CaptureActive);}
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,4,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();
            for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);}
        }
        [Test] public void BodyConnectorLocationPageRetainsPrivateOwnerWithoutSelfCycleOrPublicDtoHints()
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));string name=new string("unknown".ToCharArray());var at=new NavLocation(new NavPoint(1,2),name);var q=NavConnectorResult.CreatePhysical(NavConnectorStatus.UnknownPortal,new string('a',64),name,at,at,default,default,0,initial.OwnedReservation(),null);var dto=q.Detached();Assert.IsNull(dto.Origin.NameReference.Owner);Assert.IsNull(dto.Entry.NameReference.Owner);Assert.False(dto.IsRegistered);var page=NavPagedList<NavLocation>.Create(initial.TryReserve,4);page.Add(q.Origin);page.Add(q.Entry);page.Seal();q.ReleaseOwner();initial.Attach(page);initial.Commit(page,_=>{});Assert.AreSame(page[0].NameReference.Owner,page[1].NameReference.Owner);long charge=hot.PhysicalBytes;Assert.AreEqual(Unique(page),charge);byte[] bytes=Capture(hot,page);page.ReleaseStorage();Assert.Zero(hot.PhysicalBytes);page=null;q=null;dto=null;at=default;name=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavPagedList<NavLocation>)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});var owner=(NavConnectorResult)restored[0].NameReference.Owner;Assert.AreSame(owner,restored[1].NameReference.Owner);Assert.AreSame(owner,owner.Origin.NameReference.Owner);Assert.AreEqual(NavNameRole.ConnectorPrivateSlot,restored[0].NameReference.Role);Assert.AreSame(owner.PortalId,restored[0].SurfaceId);Assert.AreSame(restored[0].SurfaceId,restored[1].SurfaceId);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));restored.ReleaseStorage();Assert.Zero(cold.PhysicalBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
        }
        [Test] public void BodyArcEqualTextWithoutExactTraceNameIdentityFailsClosed()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var at=new NavLocation(new NavPoint(0,0),"a");var leg=NavTypedLeg.CreateOwned(at,new NavLocation(new NavPoint(0,0),"b"),1,NavTypedLegKind.Portal,"ab",initial.OwnedReservation());var trace=NavRetainedArray<NavTypedLeg>.Copy(new[]{leg},initial.TryReserve);var badName=new string(leg.PortalId.ToCharArray());Assert.AreEqual(leg.PortalId,badName);Assert.AreNotSame(leg.PortalId,badName);
            var list=NavPagedList<NavGraphArc>.Create(initial.TryReserve);long charge=budget.PhysicalBytes,held=initial.HeldBytes;int refs=trace.References;
            Assert.Throws<ArgumentException>(()=>list.Add(new NavGraphArc(0,1,1,NavGraphArcKind.Portal,badName,trace)));Assert.AreEqual(charge,budget.PhysicalBytes);Assert.AreEqual(held,initial.HeldBytes);Assert.AreEqual(refs,trace.References);Assert.AreEqual(0,list.Count);list.ReleaseStorage();leg.ReleaseOwner();trace.Release();initial.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
        }
        [Test] public void ConnectorForeignProviderAndCopyOwnerRejectBeforeScratchAllocationOrRetain()
        {
            var input=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));var source=new NavigationRetentionBudget();Assert.True(source.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var provider=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());var q=provider.QualifyPortalOwned(null,null,initial.TryReserve);var root=NavRetainedArray<NavOwnedStorage>.Copy(new[]{provider.StorageRoot,q},initial.TryReserve);provider.StorageRoot.Release();q.ReleaseOwner();initial.Attach(root);initial.Commit(root,_=>{});long physical=source.PhysicalBytes;int pr=provider.StorageRoot.References,qr=q.References;
            var target=new NavigationRetentionBudget();Assert.True(target.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var attempt));var refusing=new Refusal(attempt,int.MaxValue);long held=attempt.HeldBytes;
            Assert.Throws<ArgumentException>(()=>NavConnectorResult.CreatePhysical(NavConnectorStatus.UnknownPortal,provider.Binding,null,default,default,default,default,0,refusing,provider));Assert.Throws<ArgumentException>(()=>NavConnectorResult.CopyOwned(q,refusing.TryReserve,provider));Assert.Zero(refusing.Calls);Assert.AreEqual(held,attempt.HeldBytes);Assert.Zero(target.PhysicalBytes);Assert.AreEqual(physical,source.PhysicalBytes);Assert.AreEqual(pr,provider.StorageRoot.References);Assert.AreEqual(qr,q.References);attempt.Discard();root.Release();Assert.Zero(source.PhysicalBytes);Assert.Zero(target.HeldBytes);
        }
        [Test] public void BodyDirectNameOwnersPreservePartitionLeavesSharedPolygonAndEqualDifferentRoles()
        {
            var polygon=new NavPolygon(new[]{new NavPoint(-2,-2),new NavPoint(2,-2),new NavPoint(2,2),new NavPoint(-2,2)});var support=new MapSupport("support",new NavObstacle(polygon),true,6,new NavPoint(.5,-.25),new NavPoint(1,1),"floor");var surface=new NavLayerSurface("floor","ground",6,1,new NavTraversalArea(polygon,2,Array.Empty<NavObstacle>()));var transition=new NavSurfaceTransition("cross","floor","other");var seam=new NavPartitionSeam("floor","other",new NavPoint(0,0),new NavPoint(1,1),false);
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));NavRetainedArray<NavNameReference> root;
            using(var scratch=NavStorageScratch.Reserve(initial.OwnedReservation(),8192)){
                var copy=new NavPartitionCopy(initial.OwnedReservation());var a=copy.Support(support);var b=copy.Declarations.Surface(surface);var t=copy.Transition(transition);var z=copy.Seam(seam);var privateText=NavOwnedText.Copy("private",initial.OwnedReservation());
                root=NavRetainedArray<NavNameReference>.Copy(new[]{new NavNameReference(a.StorageRoot,NavNameRole.SupportSurface),new NavNameReference(b.StorageRoot,NavNameRole.SurfaceId),new NavNameReference(a.StorageRoot,NavNameRole.SupportId),new NavNameReference(t.StorageRoot,NavNameRole.TransitionId),new NavNameReference(t.StorageRoot,NavNameRole.TransitionFrom),new NavNameReference(t.StorageRoot,NavNameRole.TransitionTo),new NavNameReference(z.StorageRoot,NavNameRole.SeamId),new NavNameReference(z.StorageRoot,NavNameRole.SeamFrom),new NavNameReference(z.StorageRoot,NavNameRole.SeamTo),new NavNameReference(privateText,NavNameRole.Text),default(NavNameReference)},initial.TryReserve);privateText.Release();
            }
            initial.Attach(root);initial.Commit(root,_=>{});long charge=hot.PhysicalBytes;Assert.AreEqual(Unique(root),charge);Assert.AreEqual(root[0].Resolve(),root[1].Resolve());Assert.AreNotSame(root[0].Resolve(),root[1].Resolve());byte[] bytes=Capture(hot,root);byte[] badSeam=Capture(hot,root[6].Owner,3);int idPosition=8+9+12;char digit=(char)BitConverter.ToUInt16(badSeam,idPosition);Array.Copy(BitConverter.GetBytes((ushort)(digit=='0'?'1':'0')),0,badSeam,idPosition,2);root.Release();Assert.Zero(hot.PhysicalBytes);root=null;polygon=null;support=null;surface=null;transition=null;seam=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavRetainedArray<NavNameReference>)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});
            var aSupport=(MapSupport)((NavDeclarationStorage)restored[0].Owner).Facade;var bSurface=(NavLayerSurface)((NavDeclarationStorage)restored[1].Owner).Facade;var restoredTransition=(NavSurfaceTransition)((NavDeclarationStorage)restored[3].Owner).Facade;var restoredSeam=(NavPartitionSeam)((NavDeclarationStorage)restored[6].Owner).Facade;
            Assert.AreSame(aSupport.SurfaceId,restored[0].Resolve());Assert.AreSame(aSupport.Id,restored[2].Resolve());Assert.AreSame(aSupport.Bounds.Polygon,bSurface.Area.Boundary);Assert.AreNotSame(aSupport.SurfaceId,bSurface.Id);Assert.AreEqual(aSupport.SurfaceId,bSurface.Id);Assert.AreEqual(6,aSupport.Height);Assert.AreEqual(new NavPoint(.5,-.25),aSupport.Gradient);Assert.AreEqual(new NavPoint(1,1),aSupport.Origin);Assert.True(aSupport.IsBridge);Assert.True(aSupport.Contains(new NavPoint(0,0)));Assert.AreSame(restoredTransition.From,restored[4].Resolve());Assert.AreSame(restoredTransition.To,restored[5].Resolve());Assert.AreSame(restoredSeam.FromSurface,restored[7].Resolve());Assert.AreSame(restoredSeam.ToSurface,restored[8].Resolve());Assert.AreNotSame(restoredTransition.From,restoredSeam.FromSurface);Assert.False(restoredSeam.Allowed);Assert.IsNull(restored[10].Resolve());Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));restored.Release();Assert.Zero(cold.PhysicalBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var invalid));Assert.Throws<ArgumentException>(()=>Decode(badSeam,invalid.OwnedReservation()));invalid.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,4,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();
            for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);}
        }
        [Test] public void DirectNameSelectorsRejectEqualTextWrongRoleAndForeignOwnerBeforeReservation()
        {
            var first=new NavigationRetentionBudget();Assert.True(first.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var text=NavOwnedText.Copy("name",initial.OwnedReservation());initial.Attach(text);initial.Commit(text,_=>{});var reference=new NavNameReference(text,NavNameRole.Text);Assert.AreSame(text.Value,reference.Resolve());
            var second=new NavigationRetentionBudget();Assert.True(second.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var target));var counter=new Refusal(target,int.MaxValue);long held=target.HeldBytes;int refs=text.References;Assert.Throws<ArgumentException>(()=>reference.Require(counter,text.Value));Assert.Zero(counter.Calls);Assert.AreEqual(held,target.HeldBytes);Assert.Zero(second.PhysicalBytes);Assert.AreEqual(refs,text.References);target.Discard();
            Assert.Throws<ArgumentException>(()=>new NavNameReference(text,NavNameRole.SurfaceId));Assert.Throws<ArgumentException>(()=>new NavNameReference(text,NavNameRole.Text,0));Assert.Throws<ArgumentException>(()=>new NavNameReference(text,(NavNameRole)99));Assert.True(first.TryOpenScope(NavigationPreparationKind.Projection,1,2,out var attempt));var ownCounter=new Refusal(attempt,int.MaxValue);Assert.Throws<ArgumentException>(()=>reference.Require(ownCounter,new string(text.Value.ToCharArray())));Assert.Zero(ownCounter.Calls);attempt.Discard();text.Release();Assert.Zero(first.PhysicalBytes);Assert.Throws<ArgumentException>(()=>reference.Resolve());
        }
        [Test] public void BodyLocationGridAndNodePagesOutliveProviderControllersAndPreserveNextCow()
        {
            var input=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var provider=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());var first=provider.Surfaces[0].OwnedLocation(new NavPoint(-1,0));var second=provider.Surfaces[0].OwnedLocation(new NavPoint(1,0));
            var locations=NavPagedList<NavLocation>.Create(initial.TryReserve,4);locations.Add(first);locations.Add(second);locations.Seal();var directory=(NavStorageDirectory<NavLocation>)typeof(NavPagedList<NavLocation>).GetField("pages",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(locations);var page=directory.Get(0);
            var grid=NavCowDictionary<NavGridCoordinate,int>.CreateOwned(initial.OwnedReservation());for(int n=0;n<13;n++)grid.Add(new NavGridCoordinate(first,n,n-6),n);grid.Seal();var node=(NavOwnedStorage)typeof(NavCowDictionary<NavGridCoordinate,int>).GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(grid);
            var nodes=NavPagedList<NavGraphNode>.Create(initial.TryReserve,4);nodes.Add(new NavGraphNode(0,first,0));nodes.Add(new NavGraphNode(1,second,0));nodes.Seal();var ends=NavRetainedArray<NavPortalEndpoints>.Copy(new[]{new NavPortalEndpoints(0,1),default},initial.TryReserve);var root=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{locations,grid,nodes,page,node,ends},initial.TryReserve);locations.ReleaseStorage();grid.ReleaseStorage();nodes.ReleaseStorage();ends.Release();provider.StorageRoot.Release();initial.Attach(root);initial.Commit(root,_=>{});Assert.AreEqual(Unique(root),hot.PhysicalBytes);byte[] bytes=Capture(hot,root);long charge=hot.PhysicalBytes;root.Release();Assert.Zero(hot.PhysicalBytes);root=null;provider=null;input=null;locations=null;grid=null;nodes=null;page=null;node=null;first=default;second=default;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});var list=(NavPagedList<NavLocation>)restored[0];var map=(NavCowDictionary<NavGridCoordinate,int>)restored[1];var nodeList=(NavPagedList<NavGraphNode>)restored[2];var restoredDirectory=(NavStorageDirectory<NavLocation>)typeof(NavPagedList<NavLocation>).GetField("pages",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(list);Assert.AreSame(restored[3],restoredDirectory.Get(0));Assert.AreSame(restored[4],typeof(NavCowDictionary<NavGridCoordinate,int>).GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(map));Assert.AreSame(list[0].NameReference.Owner,nodeList[0].Location.NameReference.Owner);Assert.AreSame(list[0].SurfaceId,nodeList[0].Location.SurfaceId);Assert.AreEqual(13,map.Count);Assert.AreEqual(7,map[new NavGridCoordinate(list[0].SurfaceId,7,1)]);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));
            long baseline=cold.PhysicalBytes;Assert.True(cold.TryOpenScope(NavigationPreparationKind.Projection,1,3,out var mutate));var fork=list.ForkOwned(mutate.OwnedReservation());mutate.Attach(fork);fork[0]=new NavLocation(new NavPoint(-.5,0),list[0].NameReference);fork.Seal();Assert.AreEqual(new NavPoint(-1,0),list[0].Position);Assert.AreEqual(new NavPoint(-.5,0),fork[0].Position);mutate.Discard();Assert.AreEqual(baseline,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored,4));
            restored[3].Retain();restored[4].Retain();var pinnedPage=restored[3];var pinnedNode=restored[4];var sharedOwner=list[0].NameReference.Owner;restored.Release();Assert.AreEqual(Unique(pinnedPage)+Unique(pinnedNode)-Unique(sharedOwner),cold.PhysicalBytes,"Two independently retained storage roots share only their declaration DAG.");pinnedPage.Release();pinnedNode.Release();Assert.Zero(cold.PhysicalBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,5,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();
            for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);}
        }
        [Test] public void NamedLocationPayloadRejectsUnownedForeignAndDefaultBeforeCowAllocationAndPreservesSemanticEquality()
        {
            var input=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));var first=new NavigationRetentionBudget();Assert.True(first.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var provider=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());initial.Attach(provider);initial.Commit(provider,_=>{});var location=provider.Surfaces[0].OwnedLocation(new NavPoint(0,0));var plain=new NavLocation(location.Position,location.SurfaceId);Assert.AreEqual(plain,location);Assert.AreEqual(plain.GetHashCode(),location.GetHashCode());Assert.AreEqual(new NavGridCoordinate(plain.SurfaceId,1,2),new NavGridCoordinate(location,1,2));Assert.AreEqual(new NavGraphNode(0,plain,0),new NavGraphNode(0,location,0));
            var target=new NavigationRetentionBudget();Assert.True(target.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var attempt));var counter=new Refusal(attempt,int.MaxValue);var list=NavPagedList<NavLocation>.Create(counter.TryReserve,4);attempt.Attach(list);counter.Calls=0;long held=attempt.HeldBytes,physical=target.PhysicalBytes;int refs=location.NameReference.Owner.References;Assert.Throws<ArgumentException>(()=>list.Add(location));Assert.Throws<ArgumentException>(()=>list.Add(plain));Assert.Throws<ArgumentException>(()=>list.Add(default));Assert.Zero(counter.Calls);Assert.AreEqual(held,attempt.HeldBytes);Assert.AreEqual(physical,target.PhysicalBytes);Assert.AreEqual(refs,location.NameReference.Owner.References);attempt.Discard();provider.StorageRoot.Release();Assert.Zero(target.PhysicalBytes);Assert.Zero(first.PhysicalBytes);
            Assert.AreEqual(40,NavStorageLayout.ValueBytes(typeof(NavLocation)));Assert.AreEqual(56,NavStorageLayout.ValueBytes(typeof(NavGraphNode)));Assert.GreaterOrEqual(NavStorageLayout.Charge(NavTypedLeg.StorageCharge),NavStorageLayout.ObjectBytesBound(typeof(NavTypedLeg)));Assert.GreaterOrEqual(NavStorageLayout.Charge(NavConnectorResult.StorageCharge),NavStorageLayout.ObjectBytesBound(typeof(NavConnectorResult)));Assert.AreEqual(Math.Max(64,NavStorageLayout.ValueBytes(typeof(NavPackedFieldCell))),NavPackedFieldStorage.CellBytes);Assert.Greater(NavPackedFieldStorage.CellBytes,64,"Arc name selector is included in every packed cell charge.");
        }
        [Test] public void BodyDirectArcNameKeyAndPackedCellPreserveAliasesWidthColdChargeAndNextCow()
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var text=NavOwnedText.Copy("portal-name",initial.OwnedReservation());var name=new NavNameReference(text,NavNameRole.Text);var arc=new NavGraphArc(0,1,2,NavGraphArcKind.Portal,text.Value,null,name);
            var arcs=NavPagedList<NavGraphArc>.Create(initial.TryReserve,4);arcs.Add(arc);arcs.Seal();var keys=NavCowDictionary<NavNameKey,double>.CreateOwned(initial.OwnedReservation());keys.Add(new NavNameKey(name),2);keys.Seal();var ordered=NavCowOrderedSet<NavGraphArc>.Create(NavGraphArcComparer.Instance,initial.TryReserve);ordered.Add(arc);ordered.Seal();var field=NavPackedFieldStorage.Create(65,initial.TryReserve);field.Set(64,2,arc);field.SealPrepared();Assert.IsNull(field.Save()[0].Next[0].NameReference.Owner);Assert.IsNull(arc.Detached().NameReference.Owner);var root=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{arcs,keys,ordered,field},initial.TryReserve);arcs.ReleaseStorage();keys.ReleaseStorage();ordered.ReleaseStorage();field.ReleaseStorage();text.ReleaseOwner();initial.Attach(root);initial.Commit(root,_=>{});Assert.AreEqual(Unique(root),hot.PhysicalBytes);byte[] bytes=Capture(hot,root);long charge=hot.PhysicalBytes;Assert.True(hot.TryOpenScope(NavigationPreparationKind.Projection,1,3,out var hotMutation));var hotFork=field.ForkPrepared(hotMutation.TryReserve);hotMutation.Attach(hotFork);long expectedGrowth=hotFork.GrowthForIndex(64),hotBefore=hot.PhysicalBytes;hotFork.Set(64,3,new NavGraphArc(0,2,3,NavGraphArcKind.Surface,null));long exactGrowth=hot.PhysicalBytes-hotBefore;Assert.GreaterOrEqual(expectedGrowth,exactGrowth,"Directory predictor conservatively includes one extra bounded block.");hotMutation.Discard();Assert.AreEqual(charge,hot.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(hot,root,4));root.Release();Assert.Zero(hot.PhysicalBytes);root=null;text=null;arcs=null;keys=null;ordered=null;field=null;arc=default;name=default;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var restore));var result=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,restore.OwnedReservation());restore.Attach(result);restore.Commit(result,_=>{});var restoredArcs=(NavPagedList<NavGraphArc>)result[0];var restoredKeys=(NavCowDictionary<NavNameKey,double>)result[1];var restoredOrder=(NavCowOrderedSet<NavGraphArc>)result[2];var restoredField=(NavPackedFieldStorage)result[3];Assert.True(restoredField.TryNext(64,out var next));Assert.AreSame(restoredArcs[0].NameReference.Owner,next.NameReference.Owner);Assert.AreSame(restoredKeys.Keys.Single().Reference.Owner,next.NameReference.Owner);Assert.AreSame(restoredOrder.Single().NameReference.Owner,next.NameReference.Owner);Assert.AreEqual(2,restoredKeys[new NavNameKey("portal-name")]);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(result),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,result));Assert.AreEqual(56,NavStorageLayout.ValueBytes(typeof(NavGraphArc)));Assert.AreEqual(72,NavPackedFieldStorage.CellBytes);
            long baseline=cold.PhysicalBytes;Assert.True(cold.TryOpenScope(NavigationPreparationKind.Projection,1,3,out var mutation));var fork=restoredField.ForkPrepared(mutation.TryReserve);mutation.Attach(fork);long growth=fork.GrowthForIndex(64),before=cold.PhysicalBytes;fork.Set(64,3,new NavGraphArc(0,2,3,NavGraphArcKind.Surface,null));Assert.AreEqual(expectedGrowth,growth);Assert.AreEqual(exactGrowth,cold.PhysicalBytes-before);Assert.GreaterOrEqual(growth,cold.PhysicalBytes-before);fork.SealPrepared();Assert.True(restoredField.TryNext(64,out var old));Assert.AreEqual(NavGraphArcKind.Portal,old.Kind);mutation.Discard();Assert.AreEqual(baseline,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,result,4));result.Release();Assert.Zero(cold.PhysicalBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,5,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
        }
        [Test] public void DirectArcAndNameKeyRejectForeignUnownedAndDefaultBeforeAllocation()
        {
            var source=new NavigationRetentionBudget();Assert.True(source.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var text=NavOwnedText.Copy("portal",initial.OwnedReservation());initial.Attach(text);initial.Commit(text,_=>{});var name=new NavNameReference(text,NavNameRole.Text);var arc=new NavGraphArc(0,1,1,NavGraphArcKind.Portal,text.Value,null,name);Assert.AreEqual(arc,new NavGraphArc(0,1,1,NavGraphArcKind.Portal,text.Value));Assert.AreEqual(arc.GetHashCode(),new NavGraphArc(0,1,1,NavGraphArcKind.Portal,text.Value).GetHashCode());
            var target=new NavigationRetentionBudget();Assert.True(target.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var attempt));var counter=new Refusal(attempt,int.MaxValue);var list=NavPagedList<NavGraphArc>.Create(counter.TryReserve);var map=NavCowDictionary<NavNameKey,double>.CreateOwned(counter);var field=NavPackedFieldStorage.Create(64,counter.TryReserve);counter.Calls=0;long held=attempt.HeldBytes,physical=target.PhysicalBytes;int refs=text.References;
            Assert.Throws<ArgumentException>(()=>list.Add(arc));Assert.Throws<ArgumentException>(()=>list.Add(new NavGraphArc(0,1,1,NavGraphArcKind.Portal,text.Value)));Assert.Throws<ArgumentException>(()=>map.Add(new NavNameKey(name),1));Assert.Throws<ArgumentException>(()=>map.Add(new NavNameKey(text.Value),1));Assert.Throws<ArgumentException>(()=>map.Add(default,1));Assert.Throws<ArgumentException>(()=>field.Set(0,1,arc));Assert.Zero(counter.Calls);Assert.AreEqual(held,attempt.HeldBytes);Assert.AreEqual(physical,target.PhysicalBytes);Assert.AreEqual(refs,text.References);list.ReleaseStorage();map.ReleaseStorage();field.ReleaseStorage();attempt.Discard();text.ReleaseOwner();Assert.Zero(source.PhysicalBytes);Assert.Zero(target.PhysicalBytes);
        }
        [Test] public void BodyTerminalRegionOwnsDistinctNamesSelectorsAndPageAfterCallerRetirement()
        {
            var requested=new NavLocation(new NavPoint(0,0),"flat");var input=new NavTerminalRegion("goal",3,requested,new[]{new NavLocation(new NavPoint(1,0),"flat"),new NavLocation(new NavPoint(2,0),"flat"),new NavLocation(new NavPoint(3,0),"flat")});var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var region=NavTerminalRegion.CopyOwned(input,initial.OwnedReservation());Assert.AreNotSame(region.Requested.SurfaceId,region.Candidates[0].SurfaceId);Assert.AreNotSame(region.Candidates[0].SurfaceId,region.Candidates[1].SurfaceId);var list=NavPagedList<NavLocation>.Create(initial.TryReserve,4);list.Add(region.Requested);list.AddRange(region.Candidates);list.Seal();region.StorageRoot.Release();initial.Attach(list);initial.Commit(list,_=>{});byte[] bytes=Capture(hot,list),regionBytes=Capture(hot,region.StorageRoot,3);long charge=hot.PhysicalBytes;Assert.AreEqual(Unique(list),charge);string key=region.StableKey;list.ReleaseStorage();Assert.Zero(hot.PhysicalBytes);list=null;region=null;input=null;requested=default;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavPagedList<NavLocation>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});var owner=restored[0].NameReference.Owner;var restoredRegion=(NavTerminalRegion)((NavDeclarationStorage)owner).Facade;Assert.AreEqual(key,restoredRegion.StableKey);Assert.AreEqual(NavNameRole.RegionRequestedSurface,restored[0].NameReference.Role);for(int n=1;n<4;n++){Assert.AreSame(owner,restored[n].NameReference.Owner);Assert.AreEqual(NavNameRole.RegionCandidateSurface,restored[n].NameReference.Role);Assert.AreEqual(n-1,restored[n].NameReference.Index);Assert.AreSame(restoredRegion.Candidates[n-1].SurfaceId,restored[n].SurfaceId);Assert.AreNotSame(restored[0].SurfaceId,restored[n].SurfaceId);}Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Projection,1,3,out var mutate));var fork=restored.ForkOwned(mutate.OwnedReservation());mutate.Attach(fork);fork[1]=new NavLocation(new NavPoint(1.5,0),restored[1].NameReference);fork.Seal();Assert.AreEqual(new NavPoint(1,0),restored[1].Position);mutate.Discard();Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored,4));restored.ReleaseStorage();Assert.Zero(cold.PhysicalBytes);
            var bad=(byte[])regionBytes.Clone();int digestOffset=17+16+3*4+2*4;bad[digestOffset]=(byte)(bad[digestOffset]=='0'?'1':'0');Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,5,out var invalid));Assert.Throws<ArgumentException>(()=>Decode(bad,invalid.OwnedReservation()));invalid.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,6,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
        }
        [Test] public void TerminalRegionForeignRootAndLocationSelectorsRejectBeforeCopyAdmission()
        {
            var location=new NavLocation(new NavPoint(0,0),"flat");var input=new NavTerminalRegion("goal",1,location,new[]{location});var source=new NavigationRetentionBudget();Assert.True(source.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var region=NavTerminalRegion.CopyOwned(input,initial.OwnedReservation());initial.Attach(region);initial.Commit(region,_=>{});var mixed=new NavTerminalRegion("mixed",1,region.Requested,new[]{region.Requested});var target=new NavigationRetentionBudget();Assert.True(target.TryOpenScope(NavigationPreparationKind.Reconstruction,2,1,out var attempt));var refusal=new Refusal(attempt,int.MaxValue);long held=attempt.HeldBytes,charge=source.PhysicalBytes;int refs=region.StorageRoot.References;Assert.Throws<ArgumentException>(()=>NavTerminalRegion.CopyOwned(region,refusal));Assert.Throws<ArgumentException>(()=>NavTerminalRegion.CopyOwned(mixed,refusal));Assert.Zero(refusal.Calls);Assert.AreEqual(refs,region.StorageRoot.References);Assert.AreEqual(charge,source.PhysicalBytes);Assert.AreEqual(held,attempt.HeldBytes);attempt.Discard();region.StorageRoot.Release();Assert.Zero(source.PhysicalBytes);Assert.Zero(target.PhysicalBytes);
        }
        [Test] public void BodyRecipeMetadataRolesPreservePhysicalAliasesPrivateHoldAndRecomputedDigests()
        {
            foreach(string hold in new string[]{null,""," hold "})foreach(bool singleton in new[]{false,true}){
                var input=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(2,Array.Empty<NavObstacle>(),1));var origin=new NavLocation(new NavPoint(-1,0),"flat");var goal=new NavLocation(new NavPoint(1,0),"flat");var c=new NavClearanceProfile("ground",1,"ground",1,.1);var p=new NavGraphProfile(new string('b',64),"distance",1,1);var limits=new NavGraphCompileLimits(64,64,256);var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,0,1,1,1,origin,goal,hold);var region=singleton?null:new NavTerminalRegion("goal",1,goal,new[]{goal});var source=new NavScheduledRequest(input,c,p,limits,region,subscription,64,singleton);
                var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));var provider=LayeredNavigationProvider.CopyOwned(input,initial.OwnedReservation());var recipe=NavScheduledRequest.CopyOwned(source,provider,initial.OwnedReservation());var refs=NavRetainedArray<NavMetadataReference>.Copy(new[]{new NavMetadataReference(recipe.StorageRoot,NavMetadataRole.Clearance),new NavMetadataReference(recipe.StorageRoot,NavMetadataRole.GraphProfile),new NavMetadataReference(recipe.StorageRoot,NavMetadataRole.Subscription),new NavMetadataReference(recipe.StorageRoot,NavMetadataRole.CompileLimits)},initial.TryReserve);var binding=NavGraphBinding.CreatePhysical(provider,recipe.Clearance,recipe.Profile,initial.OwnedReservation());Assert.AreEqual(recipe.GraphKey,binding.StableKey);Assert.AreNotSame(recipe.GraphKey,binding.StableKey);var root=NavRetainedArray<NavOwnedStorage>.Copy(new[]{recipe.StorageRoot,(NavOwnedStorage)refs,binding.StorageRoot,recipe.StorageRoot},initial.TryReserve);recipe.StorageRoot.Release();refs.Release();provider.StorageRoot.Release();initial.Attach(root);initial.Commit(root,_=>{});Assert.AreEqual(Unique(root),hot.PhysicalBytes);byte[] bytes=Capture(hot,root),recipeBytes=Capture(hot,recipe.StorageRoot,3);long charge=hot.PhysicalBytes;string graph=recipe.GraphKey,compile=recipe.CompileKey,artifact=recipe.ArtifactKey,subscriptionKey=recipe.Subscription.StableKey;root.Release();Assert.Zero(hot.PhysicalBytes);root=null;recipe=null;provider=null;refs=null;binding=null;source=null;input=null;subscription=null;region=null;c=null;p=null;limits=null;origin=default;goal=default;
                var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});var value=(NavScheduledRequest)((NavDeclarationStorage)restored[0]).Facade;var roles=(NavRetainedArray<NavMetadataReference>)restored[1];var restoredBinding=(NavGraphBinding)((NavDeclarationStorage)restored[2]).Facade;Assert.AreSame(restored[0],restored[3]);Assert.AreSame(value.Clearance,roles[0].Value);Assert.AreSame(value.Profile,roles[1].Value);Assert.AreSame(value.Subscription,roles[2].Value);Assert.AreSame(value.Limits,roles[3].Value);Assert.AreSame(value.Clearance,restoredBinding.Clearance);Assert.AreSame(value.Profile,restoredBinding.Profile);Assert.AreEqual(graph,value.GraphKey);Assert.AreEqual(compile,value.CompileKey);Assert.AreEqual(artifact,value.ArtifactKey);Assert.AreEqual(subscriptionKey,value.Subscription.StableKey);Assert.AreEqual(hold,value.Subscription.HoldBinding);Assert.AreEqual(singleton,value.Singleton);Assert.AreNotSame(value.GraphKey,restoredBinding.StableKey);Assert.AreSame(value.Provider.Surfaces[0].StorageRoot,value.Subscription.Origin.NameReference.Owner);Assert.AreSame(value.Provider.Surfaces[0].Id,value.Subscription.Origin.SurfaceId);Assert.AreSame(value.Provider.Surfaces[0].Id,value.Subscription.AssignedEndpoint.SurfaceId);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));restored.Release();Assert.Zero(cold.PhysicalBytes);
                var bad=(byte[])recipeBytes.Clone();bad[bad.Length-2]=(byte)(bad[bad.Length-2]=='0'?'1':'0');Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var malformed));Assert.Throws<ArgumentException>(()=>Decode(bad,malformed.OwnedReservation()));malformed.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
                Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,4,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();for(int nth=1;nth<=total;nth++){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
            }
        }
        [Test] public void BodyColdRestorePreservesTopologyOrderInternalSharingDistinctArraysAndNextCow()
        {
            var hot=new NavigationRetentionBudget();var source=Source(hot);byte[] bytes=Capture(hot,source);long charge=hot.PhysicalBytes;
            source.Release();Assert.Zero(hot.PhysicalBytes);source=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));
            var root=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,scope.OwnedReservation());scope.Attach(root);scope.Commit(root,_=>{});
            Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(root),cold.PhysicalBytes);Assert.AreSame(root[0],root[2]);Assert.AreSame(root[3],root[5]);Assert.AreNotSame(root[3],root[4]);
            var a=(NavCowDictionary<int,double>)root[0];var b=(NavCowDictionary<int,double>)root[1];
            CollectionAssert.AreEqual(new[]{9,3,12,1,6,11,14,4,7},a.Keys);CollectionAssert.AreEqual(new[]{9,3,12,1,6,14,4,7,18},b.Keys);
            CollectionAssert.AreEqual(bytes,Capture(cold,root));
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var mutate));var fork=b.ForkOwned(mutate.OwnedReservation());mutate.Attach(fork);fork[1]=999;fork.Seal();
            Assert.AreEqual(1.5,a[1]);Assert.AreEqual(1.5,b[1]);Assert.AreEqual(999,fork[1]);mutate.Discard();CollectionAssert.AreEqual(bytes,Capture(cold,root,4));root.Release();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void BodyRawHeapAndLegacyQueueResumeSamePopAndNextCowAdmission(bool compact)
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));
            var frontier=NavFieldFrontier.Create(300,3,700,compact,initial.TryReserve);
            for(int n=0;n<60;n++)frontier.Add(new NavQueueEntry{Node=n*7%300,Candidate=n%3,Cost=n*11%60});
            for(int n=0;n<6;n++)frontier.Remove(new NavQueueEntry{Node=n*7%300,Candidate=n%3,Cost=n*11%60});frontier.SealPrepared();initial.Attach(frontier);initial.Commit(frontier,_=>{});
            var bytes=Capture(hot,frontier);long charge=hot.PhysicalBytes;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavFieldFrontier)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
            Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));CollectionAssert.AreEqual(frontier.ToArray(),restored.ToArray());
            byte[] Next(NavigationRetentionBudget budget,NavFieldFrontier committed,out long[] grants){
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var scope));var observed=new List<long>();
                var reservation=scope.OwnedReservation(amount=>{observed.Add(amount);return scope.TryReserve(amount);});
                var fork=committed.ForkPrepared(reservation.TryReserve);scope.Attach(fork);var min=fork.Min;Assert.True(fork.Remove(min));fork.Add(new NavQueueEntry{Node=299,Candidate=2,Cost=.1});fork.SealPrepared();scope.Commit(fork,_=>{});
                grants=observed.ToArray();var result=Capture(budget,fork,4);fork.ReleaseStorage();return result;
            }
            var hotNext=Next(hot,frontier,out var hotGrants);frontier.ReleaseStorage();Assert.Zero(hot.PhysicalBytes);
            var coldNext=Next(cold,restored,out var coldGrants);CollectionAssert.AreEqual(hotGrants,coldGrants);CollectionAssert.AreEqual(hotNext,coldNext);CollectionAssert.AreEqual(bytes,Capture(cold,restored,5));restored.ReleaseStorage();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
        }
        [Test] public void BodyTypedLegTraceAndMetadataColdRestoreRetainAliasesAndPrivateStrings()
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));
            var at=new NavLocation(new NavPoint(1,2),"ground");var leg=NavTypedLeg.CreateOwned(at,at,0,NavTypedLegKind.Surface,null,initial.OwnedReservation());
            Assert.GreaterOrEqual(leg.StorageBytes,168,"Concrete leg layout includes both locations and all owning metadata/provider references.");
            var trace=NavRetainedArray<NavTypedLeg>.Copy(new[]{leg,leg},initial.TryReserve);
            var traces=NavRetainedArray<IReadOnlyList<NavTypedLeg>>.Copy(new IReadOnlyList<NavTypedLeg>[]{trace,trace},initial.TryReserve);
            var metadata=NavOwnedText.Copy("private metadata",initial.OwnedReservation());var root=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{traces,metadata,leg},initial.TryReserve);
            leg.ReleaseStorage();trace.Release();traces.Release();metadata.Release();initial.Attach(root);initial.Commit(root,_=>{});
            long charge=hot.PhysicalBytes;byte[] bytes=Capture(hot,root);root.Release();Assert.Zero(hot.PhysicalBytes);root=null;trace=null;traces=null;metadata=null;leg=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var decoded=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,scope.OwnedReservation());scope.Attach(decoded);scope.Commit(decoded,_=>{});
            var restored=(NavRetainedArray<IReadOnlyList<NavTypedLeg>>)decoded[0];Assert.AreSame(restored[0],restored[1]);Assert.AreSame(restored[0][0],restored[0][1]);Assert.AreSame(decoded[2],restored[0][0]);Assert.AreEqual("private metadata",((NavOwnedText)decoded[1]).Value);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(decoded),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,decoded));decoded.Release();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var counted));var counter=new Refusal(counted,int.MaxValue);var trial=Decode(bytes,counter);counted.Attach(trial);int total=counter.Calls;counted.Discard();Assert.Zero(cold.PhysicalBytes);
            for(int nth=1;nth<=total;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);
                Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
            }
        }
        [Test] public void BodyRealPartitionTraceSparseFieldAndSlotsPreserveColdSharingAndNextCow()
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Solve,1,1,out var initial));
            var map=ThreeCrossingsMap.Default;var provider=new PartitionNavigationProvider(map,new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),null);
            var clearance=new NavClearanceProfile("explorer",1,"ground",1,.58);Assert.True(provider.TryLocate(new NavPoint(-18,0),clearance,out var start));
            Assert.True(provider.TryTraceOwned(start,new NavPoint(18,0),clearance,out _,out var trace,initial.OwnedReservation()));Assert.True(trace.Any(l=>l.Kind==NavTypedLegKind.Portal));
            var arc=new NavGraphArc(0,1,trace.Sum(l=>l.Cost),NavGraphArcKind.Portal,trace.First(l=>l.Kind==NavTypedLegKind.Portal).PortalId,trace);
            var field=NavPackedFieldStorage.Create(1024,initial.TryReserve);field.Set(3,100,arc);field.Set(900,20,new NavGraphArc(1,2,1,NavGraphArcKind.Surface,null));field.SealPrepared();
            var fork=field.ForkPrepared(initial.TryReserve);fork.Set(3,200,null);fork.SealPrepared();var slots=NavPackedSlots.Create(initial.TryReserve);slots.Ensure(4096);slots.Ensure(8192);slots.Set(4097,123);slots.Seal();
            var root=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{field,fork,(NavOwnedStorage)trace,slots},initial.TryReserve);field.ReleaseStorage();fork.ReleaseStorage();((NavOwnedStorage)trace).ReleaseOwner();slots.ReleaseStorage();initial.Attach(root);initial.Commit(root,_=>{});Assert.AreEqual(Unique(root),hot.PhysicalBytes);
            byte[] bytes=Capture(hot,root);long charge=hot.PhysicalBytes;root.Release();Assert.Zero(hot.PhysicalBytes);root=null;trace=null;field=null;fork=null;slots=null;provider=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});
            Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);var a=(NavPackedFieldStorage)restored[0];var b=(NavPackedFieldStorage)restored[1];Assert.True(a.TryNext(3,out var first));Assert.True(b.TryNext(3,out var second));Assert.AreSame(first.Trace,second.Trace);Assert.AreSame(restored[2],first.Trace);Assert.AreEqual(first.PortalId,first.Trace.First(l=>l.Kind==NavTypedLegKind.Portal).PortalId);Assert.AreEqual(123,((NavPackedSlots)restored[3]).Get(4097));CollectionAssert.AreEqual(bytes,Capture(cold,restored));
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Solve,1,3,out var mutate));var candidate=b.ForkPrepared(mutate.TryReserve);mutate.Attach(candidate);candidate.Set(4,5,null);candidate.SealPrepared();Assert.False(b.TryGet(4,out _));Assert.True(candidate.TryGet(4,out var cost));Assert.AreEqual(5,cost);mutate.Discard();CollectionAssert.AreEqual(bytes,Capture(cold,restored,4));restored.Release();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,5,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();Assert.Zero(cold.PhysicalBytes);
            for(int nth=1;nth<=total;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var refusing=new Refusal(scope,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);scope.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
            }
        }
        [Test] public void BodyGeometryColdRestorePreservesPolygonBuffersAliasesAndPredicates()
        {
            var polygon=new NavPolygon(new[]{new NavPoint(-2,-2),new NavPoint(2,-2),new NavPoint(2,2),new NavPoint(0,1),new NavPoint(-2,2)});
            var source=new NavGeometry(4,new[]{new NavObstacle(polygon,-1,1),new NavObstacle(polygon,2,3),new NavObstacle(new NavPoint(.1,.2),.3),new NavObstacle(-3,-3,-2.5,-2.5)},7);
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));
            var a=NavGeometry.CopyOwned(source,initial.OwnedReservation());var b=NavGeometry.CopyOwned(source,initial.OwnedReservation(),_=>a.Obstacles[0].Polygon);var distinct=NavPolygon.CopyOwned(polygon,initial.OwnedReservation());
            var root=NavRetainedArray<NavOwnedStorage>.Copy(new[]{a.StorageRoot,b.StorageRoot,distinct.StorageRoot,a.StorageRoot},initial.TryReserve);a.StorageRoot.ReleaseOwner();b.StorageRoot.ReleaseOwner();distinct.StorageRoot.ReleaseOwner();initial.Attach(root);initial.Commit(root,_=>{});Assert.AreEqual(Unique(root),hot.PhysicalBytes);
            byte[] bytes=Capture(hot,root);long charge=hot.PhysicalBytes;var expectedTriangles=a.Obstacles[0].Polygon.Triangles.ToArray();var expectedVertices=a.Obstacles[0].Polygon.Vertices.ToArray();bool[] predicate=new[]{new NavPoint(0,0),new NavPoint(3,3),new NavPoint(0,3)}.Select(p=>a.IsFree(p,.1)).ToArray();root.Release();Assert.Zero(hot.PhysicalBytes);a=null;b=null;distinct=null;root=null;polygon=null;source=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});
            var first=((NavGeometry.OwnedBuffers)restored[0]).Facade;var second=((NavGeometry.OwnedBuffers)restored[1]).Facade;var other=((NavPolygon.OwnedBuffers)restored[2]).Facade;
            Assert.AreSame(restored[0],restored[3]);Assert.AreSame(first.Obstacles[0].Polygon,first.Obstacles[1].Polygon);Assert.AreSame(first.Obstacles[0].Polygon,second.Obstacles[0].Polygon);Assert.AreNotSame(first.Obstacles[0].Polygon,other);Assert.AreEqual(7,first.Revision);Assert.AreEqual(4,first.HalfExtent);
            CollectionAssert.AreEqual(expectedTriangles,first.Obstacles[0].Polygon.Triangles);CollectionAssert.AreEqual(expectedVertices,first.Obstacles[0].Polygon.Vertices);CollectionAssert.AreEqual(predicate,new[]{new NavPoint(0,0),new NavPoint(3,3),new NavPoint(0,3)}.Select(p=>first.IsFree(p,.1)).ToArray());Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));restored.Release();Assert.Zero(cold.PhysicalBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();Assert.Zero(cold.PhysicalBytes);
            for(int nth=1;nth<=total;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
            }
        }
        [TestCase("bounds")][TestCase("indices")][TestCase("count")]
        public void BodyPolygonRejectsContradictoryBoundsRawTriangulationAndCount(string kind)
        {
            byte[] bytes;
            using(var stream=new MemoryStream()){
                using(var w=new BinaryWriter(stream,Encoding.UTF8,true)){
                    void Def(int id,NavPhysicalTag tag){w.Write((byte)2);w.Write(id);w.Write(tag.Encoded);}
                    w.Write(NavPhysicalBodyWire.Magic);w.Write(3);Def(0,new NavPhysicalTag(NavPhysicalStorageKind.Polygon));w.Write(0d);w.Write(kind=="bounds"?2d:1d);w.Write(0d);w.Write(1d);
                    Def(1,new NavPhysicalTag(NavPhysicalStorageKind.Array,value:NavPhysicalValueKind.Point));w.Write(4);foreach(var p in new[]{new NavPoint(0,0),new NavPoint(1,0),new NavPoint(1,1),new NavPoint(0,1)}){w.Write(p.X);w.Write(p.Z);}
                    Def(2,new NavPhysicalTag(NavPhysicalStorageKind.Array,value:NavPhysicalValueKind.Integer));w.Write(kind=="count"?3:6);foreach(int n in kind=="count"?new[]{3,0,1}:kind=="indices"?new[]{2,0,1,1,2,3}:new[]{3,0,1,1,2,3})w.Write(n);
                }
                bytes=stream.ToArray();
            }
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));Assert.Throws<ArgumentException>(()=>Decode(bytes,scope.OwnedReservation()));scope.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
        }
        [Test] public void BodySurfaceDeclarationsPreserveEqualDistinctFacadesSharedAreaAndNames()
        {
            var boundary=new NavPolygon(new[]{new NavPoint(-2,-2),new NavPoint(2,-2),new NavPoint(2,2),new NavPoint(-2,2)});var area=new NavTraversalArea(boundary,2,Array.Empty<NavObstacle>());var source=new NavLayerSurface("same","ground",-1,2,area);
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));NavRetainedArray<NavOwnedStorage> root;
            using(var scratch=NavStorageScratch.Reserve(initial.OwnedReservation(),16384)){
                var copy=new NavProviderCopy(initial.OwnedReservation());var a=NavLayerSurface.CopyOwned(source,copy);var b=NavLayerSurface.CopyOwned(source,copy);Assert.AreNotSame(source.Id,a.Id);Assert.AreNotSame(a,b);Assert.AreSame(a.Area,b.Area);
                var surfaces=NavRetainedArray<NavLayerSurface>.Copy(new[]{a,b,a},initial.TryReserve);var index=NavCowDictionary<string,NavLayerSurface>.CreateOwned(initial.OwnedReservation(),StringComparer.Ordinal,false);index.Add(a.Id,a);index.Seal();root=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{surfaces,a.Area.StorageRoot,index},initial.TryReserve);index.ReleaseStorage();surfaces.Release();a.StorageRoot.Release();b.StorageRoot.Release();
            }
            initial.Attach(root);initial.Commit(root,_=>{});long charge=hot.PhysicalBytes;Assert.AreEqual(Unique(root),charge);byte[] bytes=Capture(hot,root);root.Release();Assert.Zero(hot.PhysicalBytes);root=null;source=null;area=null;boundary=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});
            var values=(NavRetainedArray<NavLayerSurface>)restored[0];Assert.AreSame(values[0],values[2]);Assert.AreNotSame(values[0],values[1]);Assert.AreEqual(values[0].Id,values[1].Id);Assert.AreSame(values[0].Area,values[1].Area);Assert.AreSame(restored[1],values[0].Area.StorageRoot);var restoredIndex=(NavCowDictionary<string,NavLayerSurface>)restored[2];Assert.AreSame(values[0],restoredIndex[values[0].Id]);Assert.AreSame(values[0].Id,restoredIndex.Keys.Single());Assert.AreEqual(-1,values[0].Height);Assert.AreEqual(2,values[0].DistanceCost);Assert.True(values[0].Area.Contains(new NavPoint(0,0),.1));Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));restored.Release();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();Assert.Zero(cold.PhysicalBytes);
            for(int nth=1;nth<=total;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
            }
        }
        [Test] public void BodyPortalDeclarationsPreservePrivateMobilityArraysCanonicalKeysAndAreaAliases()
        {
            var boundary=new NavPolygon(new[]{new NavPoint(-2,-2),new NavPoint(2,-2),new NavPoint(2,2),new NavPoint(-2,2)});var area=new NavTraversalArea(boundary,2,Array.Empty<NavObstacle>());
            var source=new NavLayerPortal("p",new NavLocation(new NavPoint(0,0),"a"),new NavLocation(new NavPoint(0,0),"b"),NavPortalDirection.Both,false,2,area,new[]{"heavy","ground"});
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));NavRetainedArray<NavOwnedStorage> root;
            using(var scratch=NavStorageScratch.Reserve(initial.OwnedReservation(),16384)){
                var copy=new NavProviderCopy(initial.OwnedReservation());var a=NavLayerPortal.CopyOwned(source,copy);var b=NavLayerPortal.CopyOwned(source,copy);var portals=NavRetainedArray<NavLayerPortal>.Copy(new[]{a,b,a},initial.TryReserve);
                var index=NavCowDictionary<string,NavLayerPortal>.CreateOwned(initial.OwnedReservation(),StringComparer.Ordinal,false);index.Add(a.Id,a);index.Seal();root=NavRetainedArray<NavOwnedStorage>.Copy(new NavOwnedStorage[]{portals,index,a.Corridor.StorageRoot},initial.TryReserve);portals.Release();index.ReleaseStorage();a.StorageRoot.Release();b.StorageRoot.Release();
            }
            initial.Attach(root);initial.Commit(root,_=>{});long charge=hot.PhysicalBytes;Assert.AreEqual(Unique(root),charge);byte[] bytes=Capture(hot,root);root.Release();Assert.Zero(hot.PhysicalBytes);root=null;source=null;area=null;boundary=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});
            var values=(NavRetainedArray<NavLayerPortal>)restored[0];var restoredIndex=(NavCowDictionary<string,NavLayerPortal>)restored[1];Assert.AreSame(values[0],values[2]);Assert.AreNotSame(values[0],values[1]);Assert.AreSame(values[0].Corridor,values[1].Corridor);Assert.AreSame(restored[2],values[0].Corridor.StorageRoot);Assert.AreNotSame(values[0].MobilityClasses,values[1].MobilityClasses);Assert.AreSame(values[0],restoredIndex[values[0].Id]);Assert.AreSame(values[0].Id,restoredIndex.Keys.Single());CollectionAssert.AreEqual(new[]{"ground","heavy"},values[0].MobilityClasses);Assert.False(values[0].Open);Assert.AreEqual(NavPortalDirection.Both,values[0].Direction);Assert.AreEqual("a",values[0].Entry.SurfaceId);Assert.AreEqual("b",values[0].Exit.SurfaceId);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));restored.Release();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();Assert.Zero(cold.PhysicalBytes);
            for(int nth=1;nth<=total;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
            }
        }
        [Test] public void BodyLayeredProviderAndCanonicalLegRestoreEqualBindingsDistinctRootsAndSharedDeclarations()
        {
            var polygon=new NavPolygon(new[]{new NavPoint(-2,-2),new NavPoint(2,-2),new NavPoint(2,2),new NavPoint(-2,2)});var area=new NavTraversalArea(polygon,2,Array.Empty<NavObstacle>());
            var aSurface=new NavLayerSurface("a","ground",0,1,area);var bSurface=new NavLayerSurface("b","ground",1,1,area);var portal=new NavLayerPortal("ab",new NavLocation(new NavPoint(0,0),"a"),new NavLocation(new NavPoint(0,0),"b"),NavPortalDirection.Both,true,1,area,new[]{"ground"});
            var source=new LayeredNavigationProvider("body",1,1,1,new string('a',64),new[]{aSurface,bSurface},new[]{portal});var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Reconstruction,1,1,out var initial));
            var a=LayeredNavigationProvider.CopyOwned(source,initial.OwnedReservation());var b=LayeredNavigationProvider.CopyOwned(source,initial.OwnedReservation());var c=LayeredNavigationProvider.CopyOwned(a,initial.OwnedReservation());
            var leg=NavTypedLeg.CreateOwned(a.Portals[0].Entry,a.Portals[0].Exit,1,NavTypedLegKind.Portal,a.Portals[0].Id,initial.OwnedReservation(),a);
            var root=NavRetainedArray<NavOwnedStorage>.Copy(new[]{a.StorageRoot,b.StorageRoot,c.StorageRoot,(NavOwnedStorage)leg,a.StorageRoot},initial.TryReserve);a.StorageRoot.Release();b.StorageRoot.Release();c.StorageRoot.Release();leg.ReleaseStorage();initial.Attach(root);initial.Commit(root,_=>{});long charge=hot.PhysicalBytes;Assert.AreEqual(Unique(root),charge);
            byte[] bytes=Capture(hot,root);byte[] badBinding=Capture(hot,a.StorageRoot,3);int offset=8+9+12+2*a.Id.Length+128;badBinding[offset]=(byte)(badBinding[offset]=='0'?'1':'0');string expectedBinding=a.Binding;root.Release();Assert.Zero(hot.PhysicalBytes);root=null;source=null;a=null;b=null;c=null;leg=null;portal=null;aSurface=null;bSurface=null;area=null;polygon=null;
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,scope.OwnedReservation());scope.Attach(restored);scope.Commit(restored,_=>{});
            var first=(LayeredNavigationProvider)((NavDeclarationStorage)restored[0]).Facade;var second=(LayeredNavigationProvider)((NavDeclarationStorage)restored[1]).Facade;var shared=(LayeredNavigationProvider)((NavDeclarationStorage)restored[2]).Facade;var restoredLeg=(NavTypedLeg)restored[3];
            Assert.AreEqual(expectedBinding,first.Binding);Assert.AreEqual(first.Binding,second.Binding);Assert.AreNotSame(first,second);Assert.AreNotSame(first.Surfaces[0],second.Surfaces[0]);Assert.AreSame(first.Surfaces[0],shared.Surfaces[0]);Assert.AreSame(first.Portals[0],shared.Portals[0]);Assert.AreSame(restored[0],restored[4]);Assert.AreSame(first.Surfaces[0].Id,restoredLeg.From.SurfaceId);Assert.AreSame(first.Surfaces[1].Id,restoredLeg.To.SurfaceId);Assert.AreSame(first.Portals[0].Id,restoredLeg.PortalId);Assert.AreEqual(charge,cold.PhysicalBytes);Assert.AreEqual(Unique(restored),cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,Capture(cold,restored));
            Assert.True(first.IsValid(restoredLeg.From,new NavClearanceProfile("ground",1,"ground",1,.1)));restored.Release();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,3,out var malformed));Assert.Throws<ArgumentException>(()=>Decode(badBinding,malformed.OwnedReservation()));malformed.Discard();Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,4,out var probe));var counter=new Refusal(probe,int.MaxValue);var trial=Decode(bytes,counter);probe.Attach(trial);int total=counter.Calls;probe.Discard();Assert.Zero(cold.PhysicalBytes);
            for(int nth=1;nth<=total;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejected));var refusing=new Refusal(rejected,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing));Assert.AreEqual(nth,refusing.Calls);rejected.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
            }
        }
        private sealed class Refusal : INavStorageReservation,INavStorageLedgerIdentity,INavStorageScratchAllocator
        {
            private readonly NavigationRetentionBudget.Scope scope;private readonly int nth;internal int Calls;
            internal Refusal(NavigationRetentionBudget.Scope scope,int nth){this.scope=scope;this.nth=nth;}
            public bool TryReserve(long bytes){Calls++;return Calls!=nth&&scope.TryReserve(bytes);}
            public void Register(NavOwnedStorage storage)=>((INavStorageAllocator)scope).Register(storage);
            public object Identity=>scope.Identity;public bool Owns(NavOwnedStorage storage)=>scope.Owns(storage);
            public IDisposable ClaimScratch(long bytes)=>scope.ClaimScratch(bytes);
        }
        [Test] public void EveryNthBodyDecodeRefusalDiscardsDefinitionsPagesAndScratchExactlyOnce()
        {
            var hot=new NavigationRetentionBudget();var root=Source(hot);byte[] bytes=Capture(hot,root);root.Release();Assert.Zero(hot.PhysicalBytes);
            var countBudget=new NavigationRetentionBudget();Assert.True(countBudget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var counterScope));var counter=new Refusal(counterScope,int.MaxValue);
            var trial=Decode(bytes,counter);counterScope.Attach(trial);int maximum=counter.Calls;counterScope.Discard();Assert.Zero(countBudget.PhysicalBytes);Assert.Greater(maximum,20);
            for(int nth=1;nth<=maximum;nth++){
                var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var refusing=new Refusal(scope,nth);
                Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusing),"Refusal "+nth);Assert.AreEqual(nth,refusing.Calls);scope.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
            }
        }
        [TestCase("alias")][TestCase("empty")][TestCase("depth")][TestCase("order")]
        public void BodyRejectsDirectoryPhysicalAliasesEmptyBlocksDepthAndMissingInsertionKey(string kind)
        {
            var list=new NavPhysicalTag(NavPhysicalStorageKind.List,value:NavPhysicalValueKind.Integer);
            var directory=new NavPhysicalTag(NavPhysicalStorageKind.Directory,value:NavPhysicalValueKind.Integer);
            var block=new NavPhysicalTag(NavPhysicalStorageKind.DirectoryBlock,value:NavPhysicalValueKind.Integer);
            var page=new NavPhysicalTag(NavPhysicalStorageKind.Page,value:NavPhysicalValueKind.Integer);
            byte[] bytes;
            using(var stream=new MemoryStream()){
                using(var w=new BinaryWriter(stream,Encoding.UTF8,true)){
                    void Def(int id,NavPhysicalTag tag){w.Write((byte)2);w.Write(id);w.Write(tag.Encoded);}
                    w.Write(NavPhysicalBodyWire.Magic);
                    if(kind=="order"){
                        w.Write(6);Def(0,new NavPhysicalTag(NavPhysicalStorageKind.Map,NavPhysicalValueKind.Integer,NavPhysicalValueKind.Double));w.Write(1);
                        Def(1,new NavPhysicalTag(NavPhysicalStorageKind.AvlNode,NavPhysicalValueKind.Integer,NavPhysicalValueKind.Double));w.Write(3);w.Write(1d);w.Write((byte)0);w.Write((byte)0);
                        Def(2,list);w.Write(1);w.Write(1);Def(3,directory);w.Write(1);w.Write(4);w.Write(0);w.Write(1);w.Write(1);Def(4,block);
                        Def(5,page);w.Write(1);w.Write(4);w.Write(2);for(int n=1;n<32;n++)w.Write((byte)0);
                    }else{
                        w.Write(kind=="empty"?3:kind=="depth"?5:4);Def(0,list);w.Write(1);w.Write(kind=="alias"?2:1);Def(1,directory);w.Write(kind=="alias"?2:1);w.Write(4);w.Write(kind=="depth"?1:0);w.Write(kind=="empty"?0:kind=="alias"?2:1);w.Write(kind=="depth"?2:1);Def(2,block);
                        if(kind=="depth")Def(3,block);
                        if(kind!="empty"){
                            Def(kind=="depth"?4:3,page);w.Write(1);w.Write(4);w.Write(5);
                            if(kind=="alias"){w.Write((byte)1);w.Write(3);w.Write(page.Encoded);}
                        }
                        for(int n=kind=="empty"?0:kind=="alias"?2:1;n<32;n++)w.Write((byte)0);
                        if(kind=="depth")for(int n=1;n<32;n++)w.Write((byte)0);
                    }
                }
                bytes=stream.ToArray();
            }
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));Assert.Throws<ArgumentException>(()=>Decode(bytes,scope.OwnedReservation()));scope.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);
        }
        [Test] public void BodyRejectsForwardCycleDuplicateIdWrongKindAndRootCountBeforePublication()
        {
            byte[] Packet(Action<BinaryWriter> body){using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){writer.Write(NavPhysicalBodyWire.Magic);writer.Write(2);body(writer);}return stream.ToArray();}}
            var tag=new NavPhysicalTag(NavPhysicalStorageKind.Array,value:NavPhysicalValueKind.Storage);
            void Definition(BinaryWriter w,int id){w.Write((byte)2);w.Write(id);w.Write(tag.Encoded);w.Write(1);}
            var packets=new[]{
                Packet(w=>{Definition(w,0);w.Write((byte)1);w.Write(0);w.Write(tag.Encoded);}),
                Packet(w=>{Definition(w,0);Definition(w,0);}),
                Packet(w=>{Definition(w,0);w.Write((byte)1);w.Write(1);w.Write(new NavPhysicalTag(NavPhysicalStorageKind.List,value:NavPhysicalValueKind.Integer).Encoded);}),
                Packet(w=>{w.Write((byte)2);w.Write(0);w.Write(new NavPhysicalTag(NavPhysicalStorageKind.Map,NavPhysicalValueKind.Integer,NavPhysicalValueKind.Double).Encoded);w.Write(1);w.Write((byte)0);w.Write((byte)0);})};
            foreach(var bytes in packets){var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));Assert.Throws<ArgumentException>(()=>Decode(bytes,scope.OwnedReservation()));scope.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.False(budget.CaptureActive);}
        }
    }
}

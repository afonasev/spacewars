using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationSessionTests
    {
        [Test] public void PaidSpatialRestoreKeepsAcceptedActorOrderAndFrameLookup()
        {
            var session=new NavigationSession(1,new NavGeometry(12,Array.Empty<NavObstacle>(),1),new NavigationProfile());var budget=session.PhysicalHostBudget;
            try{
                var input=new NavCrowd(new NavGeometry(12,Array.Empty<NavObstacle>(),1),new NavigationProfile());input.Add(1,new NavPoint(-4,0));input.Add(2,new NavPoint(0,0));input.Add(3,new NavPoint(4,4));input.SetRoute(1,new NavPoint(4,0),new[]{new NavPoint(4,0)});var state=input.CaptureState();input.ReleaseCrowdStorage();Array.Reverse(state.Units);
                session.Crowd.RestoreState(state);CollectionAssert.AreEqual(new[]{3,2,1},session.Crowd.Units.Select(u=>u.Id));
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));using(scope){var frame=NavPreparedCrowdStep.Create(session.Crowd,3,scope.OwnedReservation());try{frame.Observe();Assert.AreEqual(0,frame.Find(3));Assert.AreEqual(1,frame.Find(2));Assert.AreEqual(2,frame.Find(1));Assert.AreEqual(-1,frame.Find(9));frame.Compute(1d/30);Assert.True(frame.SourceCurrent());scope.Attach(frame);scope.Commit(frame,_=>frame.ApplyNoThrow());}finally{frame.Release();}}
                CollectionAssert.AreEqual(new[]{3,2,1},session.Crowd.Units.Select(u=>u.Id));Assert.True(session.Crowd.TryGet(1,out var actor));Assert.Greater(actor.Position.X,-4);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
            }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void PreparedCrowdFrameBytesDoNotScaleWithBorrowedGlobalRouteLength()
        {
            var session=Session();var budget=session.PhysicalHostBudget;
            try{
                long FrameBytes(int points){
                    var route=Enumerable.Repeat(new NavPoint(5,0),points).ToArray();Assert.True(session.Crowd.SetRoute(0,new NavPoint(5,0),route));var actor=session.Crowd.Units[0];var owner=actor.StorageOwner.Global;long before=budget.PhysicalBytes;long delta;
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));using(scope){var frame=NavPreparedCrowdStep.Create(session.Crowd,1,scope.OwnedReservation());try{frame.Observe();delta=budget.PhysicalBytes-before;Assert.AreSame(owner.ReadOnlyBacking,frame.Before[0].route);Assert.AreSame(owner.ReadOnlyBacking,frame.After[0].route);Assert.AreSame(owner,frame.Globals[0]);}finally{frame.Release();}}
                    Assert.AreEqual(before,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);return delta;
                }
                long shortFrame=FrameBytes(2),longFrame=FrameBytes(4096);Assert.Greater(shortFrame,0);Assert.AreEqual(shortFrame,longFrame);TestContext.WriteLine("prepared crowd bytes short/4096="+shortFrame+"/"+longFrame);
            }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void PreparedCrowdPinsExactFailureNameAndRoutesAcrossSourceRetirement()
        {
            var session=Session();var budget=session.PhysicalHostBudget;NavOwnedText name=null;bool destroyed=false;
            try{
                Assert.True(session.Crowd.SetRoute(0,new NavPoint(5,0),new[]{new NavPoint(5,0)}));
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var names));using(names){name=NavOwnedText.Copy("flat",names.OwnedReservation());names.Attach(name);names.Commit(name,_=>{});}
                var actor=session.Crowd.Units[0];actor.FailedGoal=new NavLocation(new NavPoint(4,0),new NavNameReference(name,NavNameRole.Text));var global=actor.StorageOwner.Global;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));using(scope){var frame=NavPreparedCrowdStep.Create(session.Crowd,1,scope.OwnedReservation());try{
                    frame.Observe();name.Release();name=null;session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();destroyed=true;
                    Assert.Greater(frame.FailurePins[0].References,0);Assert.AreEqual("flat",frame.Before[0].FailedGoal.Value.NameReference.Resolve());Assert.AreSame(global,frame.Globals[0]);Assert.Greater(global.References,0);Assert.AreEqual(NavigationStorageOwnershipTests.Unique(frame),budget.PhysicalBytes);frame.Compute(1d/30);Assert.Greater(frame.After[0].Position.X,frame.Before[0].Position.X);Assert.False(frame.SourceCurrent());
                }finally{frame.Release();}}
            }finally{name?.Release();if(!destroyed){session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void PaidSpatialRefusalPrecedesRemovalAndPlacementRechecksAfterCallbacks()
        {
            var session=Session();var budget=session.PhysicalHostBudget;
            try{
                session.Crowd.Add(1,new NavPoint(-4,4));Assert.True(session.Move(0,new NavPoint(5,0)));var pending=session.PendingRequestFor(0);var actor=session.Crowd.Units.Single(u=>u.Id==0);var spatial=session.Crowd.SpatialStorage;long physical=budget.PhysicalBytes;int denied=0;
                try{budget.AllocationAdmission=_=>{denied++;return false;};Assert.Throws<InvalidOperationException>(()=>session.Remove(0));}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.AreSame(pending,session.PendingRequestFor(0));Assert.AreSame(actor,session.Crowd.Units.Single(u=>u.Id==0));Assert.AreSame(spatial,session.Crowd.SpatialStorage);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(1,session.Requests.Count);
                bool moved=false;var requested=new NavPoint(-6,-6);
                try{budget.AllocationAdmission=_=>{if(!moved){moved=true;actor.Position=requested;actor.Location=new NavLocation(requested,NavLocation.FlatSurface);}return true;};Assert.Throws<InvalidOperationException>(()=>session.Crowd.Add(2,requested));}finally{budget.AllocationAdmission=null;}
                Assert.True(moved);Assert.False(session.Crowd.TryGet(2,out _));Assert.AreEqual(requested,actor.Position);Assert.AreEqual(2,session.Crowd.Units.Count);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                Assert.True(session.Remove(1));Assert.True(session.Remove(0));Assert.Null(session.Crowd.SpatialStorage);
                using(var state=session.CaptureState()){
                    var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),new NavigationProfile());
                    try{restored.RestoreState(state);Assert.Null(restored.Crowd.SpatialStorage);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(restored),restored.PhysicalHostBudget.PhysicalBytes);}
                    finally{restored.ReleaseCommittedHostRoot();restored.DestroyNavigationAuthority();Assert.Zero(restored.PhysicalHostBudget.PhysicalBytes);}
                }
            }finally{budget.AllocationAdmission=null;session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void PreparedCrowdAndRepairRejectForeignNamesBeforeFirstReservation()
        {
            var session=Session();var foreign=new NavigationRetentionBudget();
            try{
                Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));using(scope){int calls=0;foreign.AllocationAdmission=_=>{calls++;return true;};Assert.Throws<ArgumentException>(()=>NavPreparedCrowdStep.Create(session.Crowd,1,scope.OwnedReservation()));Assert.Zero(calls);
                    var names=(NavOwnedStorage)typeof(NavigationSession).GetField("terrainNameOwners",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(session);
                    Assert.Throws<ArgumentException>(()=>NavLocalRepairWorkspace.Create(18,scope.TryReserve,names));Assert.Zero(calls);}
            }finally{foreign.AllocationAdmission=null;session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(foreign.PhysicalBytes);Assert.Zero(foreign.HeldBytes);Assert.Zero(foreign.ActiveScopes);Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);
        }
        [Test] public void PreparedCrowdPassKeepsActorsUntilPublicationAndTransfersLocalOwner()
        {
            var session=new NavigationSession(1,new NavGeometry(12,Array.Empty<NavObstacle>(),1),new NavigationProfile());var budget=session.PhysicalHostBudget;
            try{
                var actor=session.Crowd.Add(1,new NavPoint(-4,0));session.Crowd.Add(2,new NavPoint(0,0));
                Assert.True(session.Crowd.SetRoute(1,new NavPoint(4,0),new[]{new NavPoint(4,0)}));var global=actor.StorageOwner.Global;
                int publications=0;
                for(int tick=0;tick<300&&session.Crowd.RepairCount==0;tick++){
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,0,out var scope));
                    using(scope){var frame=NavPreparedCrowdStep.Create(session.Crowd,2,scope.OwnedReservation());
                        try{
                            frame.Observe();var position=actor.Position;int cursor=actor.routeIndex;double blocked=actor.blockedSeconds;var oldSpatial=session.Crowd.SpatialStorage;
                            frame.Compute(1d/30);Assert.True(frame.SourceCurrent());Assert.AreEqual(position,actor.Position);Assert.AreEqual(cursor,actor.routeIndex);Assert.AreEqual(blocked,actor.blockedSeconds);Assert.AreSame(oldSpatial,session.Crowd.SpatialStorage);
                            scope.Attach(frame);long allocated=-1;scope.Publish(frame,_=>{Assert.True(frame.SourceCurrent());long before=GC.GetAllocatedBytesForCurrentThread();frame.ApplyNoThrow();allocated=GC.GetAllocatedBytesForCurrentThread()-before;});scope.ResolveCommitted();Assert.Zero(allocated);publications++;
                        }finally{frame.Release();}
                    }
                    Assert.AreSame(global,actor.StorageOwner.Global);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                }
                Assert.Greater(publications,0);Assert.Greater(session.Crowd.RepairCount,0);Assert.Greater(actor.StorageOwner.Local.Count,0);Assert.True(actor.Moving);
            }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void LocalRepairUsesPaidWorkspaceAndKeepsGlobalOwner()
        {
            var session=new NavigationSession(1,new NavGeometry(12,Array.Empty<NavObstacle>(),1),new NavigationProfile());var budget=session.PhysicalHostBudget;
            try{
                var actor=session.Crowd.Add(1,new NavPoint(-4,0));session.Crowd.Add(2,new NavPoint(0,0));
                Assert.True(session.Crowd.SetRoute(1,new NavPoint(4,0),new[]{new NavPoint(4,0)}));var global=actor.StorageOwner.Global;
                for(int tick=0;tick<300&&session.Crowd.RepairCount==0;tick++){
                    session.Crowd.Step(1d/30);Assert.AreSame(global,actor.StorageOwner.Global);
                    Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                }
                Assert.Greater(session.Crowd.RepairCount,0);Assert.Greater(actor.StorageOwner.Local.Count,0);Assert.True(actor.Moving);
                Assert.AreSame(global.ReadOnlyBacking,actor.route);Assert.AreSame(actor.StorageOwner.Local.ReadOnlyBacking,actor.localRoute);
            }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void MalformedCapturedActorNameDoesNotReleaseBorrowedLocationOwner()
        {
            var source=Session();var budget=source.PhysicalHostBudget;NavOwnedText name=null;
            try{
                using(var warm=source.CaptureOwnedState()){}
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,0,out var scope));using(scope){name=NavOwnedText.Copy("flat",scope.OwnedReservation());scope.Attach(name);scope.Commit(name,_=>{});}
                Assert.True(source.Crowd.TryGet(0,out var actor));actor.Location=new NavLocation(actor.Position,new NavNameReference(name,NavNameRole.Text));actor.GoalLocation=new NavLocation(actor.Position,"unowned-invalid-surface");
                int refs=name.References;long physical=budget.PhysicalBytes;
                Assert.Throws<InvalidOperationException>(()=>source.CaptureOwnedState());Assert.AreEqual(refs,name.References);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                actor.GoalLocation=null;using(var saved=source.CaptureOwnedState()){CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(saved);}
                Assert.AreEqual(refs,name.References);
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();name?.Release();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void CaptureDtoAdmissionRefusalPreservesSourceAndReleasesPrivateGraph()
        {
            var source=Session();var budget=source.PhysicalHostBudget;
            try{
                source.Crowd.Add(1,new NavPoint(-3,3));Assert.True(source.Move(0,new NavPoint(5,0)));Assert.True(source.Move(1,new NavPoint(5,3)));
                long dtoBytes;byte[] expected;using(var measured=source.CaptureOwnedState()){CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(measured);dtoBytes=measured.CaptureDto.StorageBytes;expected=(byte[])measured.HostAuthority.Clone();}
                var crowd=source.Crowd;var admission=source.Admission;var first=source.PendingRequestFor(0);var second=source.PendingRequestFor(1);var root=source.PinCommittedHostRoot();
                try{long before=budget.PhysicalBytes;int denied=0;budget.AllocationAdmission=bytes=>{if(bytes==dtoBytes){denied++;return false;}return true;};
                    Assert.Throws<InvalidOperationException>(()=>source.CaptureOwnedState());Assert.AreEqual(1,denied);
                    Assert.AreSame(crowd,source.Crowd);Assert.AreSame(admission,source.Admission);Assert.AreSame(first,source.PendingRequestFor(0));Assert.AreSame(second,source.PendingRequestFor(1));Assert.AreEqual(2,source.Requests.Count);
                    var current=source.PinCommittedHostRoot();try{Assert.AreSame(root,current);}finally{current.Release();}
                    Assert.AreEqual(before,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                    budget.AllocationAdmission=null;using(var saved=source.CaptureOwnedState()){CertifiedPhysicalPumpTests.AssertCaptureDtoLayout(saved);CollectionAssert.AreEqual(expected,saved.HostAuthority);}
                }finally{root.Release();}
            }finally{budget.AllocationAdmission=null;source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();}
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void SelectedColdRestoreRefusalsLeaveDestinationUntouched(int point)
        {
            var source=Session();NavigationSessionState saved=null;
            NavigationSession Empty()=>new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),new NavigationProfile());
            void Destroy(NavigationSession value){value.PhysicalHostBudget.AllocationAdmission=null;value.ReleaseCommittedHostRoot();value.DestroyNavigationAuthority();Assert.Zero(value.PhysicalHostBudget.PhysicalBytes);Assert.Zero(value.PhysicalHostBudget.HeldBytes);Assert.Zero(value.PhysicalHostBudget.ActiveScopes);}
            try{
                Assert.True(source.Move(0,new NavPoint(5,0)));saved=source.CaptureState();int sites=0;var measured=Empty();
                try{measured.PhysicalHostBudget.AllocationAdmission=_=>{sites++;return true;};measured.RestoreState(saved);}finally{Destroy(measured);}
                Assert.Greater(sites,3);var destination=Empty();
                try{var crowd=destination.Crowd;var admission=destination.Admission;var geometry=destination.AuthorityGeometry;long bytes=destination.PhysicalHostBudget.PhysicalBytes;int calls=0;int selected=point==0?1:point==1?sites/2:sites;
                    destination.PhysicalHostBudget.AllocationAdmission=_=>++calls!=selected;
                    Assert.Throws<InvalidOperationException>(()=>destination.RestoreState(saved));Assert.GreaterOrEqual(calls,selected);
                    Assert.AreSame(crowd,destination.Crowd);Assert.AreSame(admission,destination.Admission);Assert.AreSame(geometry,destination.AuthorityGeometry);Assert.Zero(destination.Crowd.Units.Count);Assert.Zero(destination.Requests.Count);Assert.Zero(destination.Answers.Count);Assert.Zero(destination.PendingCount);
                    Assert.AreEqual(bytes,destination.PhysicalHostBudget.PhysicalBytes);Assert.Zero(destination.PhysicalHostBudget.HeldBytes);Assert.Zero(destination.PhysicalHostBudget.ActiveScopes);
                    destination.PhysicalHostBudget.AllocationAdmission=null;destination.RestoreState(saved);Assert.AreEqual(source.PendingCount,destination.PendingCount);
                }finally{Destroy(destination);}
            }finally{saved?.Dispose();Destroy(source);}
        }
        [Test] public void LateMalformedColdActorDoesNotInstallPartialCrowd()
        {
            var source=Session();var destination=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),new NavigationProfile());
            try{
                source.Crowd.Add(1,new NavPoint(-3,3));Assert.True(source.Move(0,new NavPoint(5,0)));Assert.True(source.Move(1,new NavPoint(5,3)));
                using(var saved=source.CaptureState()){
                    saved.Crowd.Units[1].Location=new NavLocation(saved.Crowd.Units[1].Position,"invalid-surface");
                    var crowd=destination.Crowd;var admission=destination.Admission;long bytes=destination.PhysicalHostBudget.PhysicalBytes;
                    Assert.Throws<ArgumentException>(()=>destination.RestoreState(saved));Assert.AreSame(crowd,destination.Crowd);Assert.AreSame(admission,destination.Admission);Assert.Zero(destination.Crowd.Units.Count);Assert.AreEqual(bytes,destination.PhysicalHostBudget.PhysicalBytes);Assert.Zero(destination.PhysicalHostBudget.HeldBytes);Assert.Zero(destination.PhysicalHostBudget.ActiveScopes);
                }
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();destination.ReleaseCommittedHostRoot();destination.DestroyNavigationAuthority();}
            Assert.Zero(source.PhysicalHostBudget.PhysicalBytes);Assert.Zero(destination.PhysicalHostBudget.PhysicalBytes);
        }
        [Test] public void RefusedAnswerNamesKeepMailboxActorAndPhysicalCharge()
        {
            var session=Session();
            try{
                Assert.True(session.Move(0,new NavPoint(5,0)));Assert.True(session.Requests.TryDequeue(out var request));
                Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal})));
                var admission=session.Admission;var queued=session.Answers.CopyItems();long charge=session.PhysicalHostBudget.PhysicalBytes;int calls=0;
                session.PhysicalHostBudget.AllocationAdmission=_=>{calls++;return false;};session.ApplyResults();
                Assert.Greater(calls,0);Assert.AreSame(admission,session.Admission);CollectionAssert.AreEqual(queued,session.Answers.CopyItems());
                Assert.AreEqual(charge,session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.AppliedResults);Assert.False(session.Crowd.Units[0].Moving);
                session.PhysicalHostBudget.AllocationAdmission=null;session.ApplyResults();Assert.AreEqual(1,session.AppliedResults);
                Assert.AreSame(request.GoalLocation.Value.NameReference.Owner,session.Crowd.Units[0].GoalLocation.Value.NameReference.Owner);
                Assert.True(session.Move(0,new NavPoint(-5,0)),"A subsequent command may safely pin the actor's historical goal name.");
            }finally{session.PhysicalHostBudget.AllocationAdmission=null;session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }
        [Test] public void FailedAnswerNameSurvivesConsumptionAndClearsOnReissue()
        {
            var session=Session();
            try{
                Assert.True(session.Move(0,new NavPoint(5,0)));Assert.True(session.Requests.TryDequeue(out var request));
                var name=request.GoalLocation.Value.NameReference.Owner;
                Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>(),NavSolveStatus.UnreachableInGraph)));
                session.ApplyResults();Assert.AreEqual(1,session.UnreachableResults);Assert.AreSame(name,session.Crowd.Units[0].FailedGoal.Value.NameReference.Owner);Assert.Greater(name.References,0);
                Assert.True(session.Move(0,new NavPoint(-5,0)));Assert.False(session.Crowd.Units[0].FailedGoal.HasValue);
            }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }
        [Test] public void LateStoppedTransferKeepsExactOwnerUntilAnswerConsumptionAndDestroy()
        {
            var session=Session();
            try{
                Assert.True(session.Move(0,new NavPoint(5,0)));
                Assert.True(session.Requests.TryDequeue(out var request));var owner=request.StorageOwner;
                session.Stop(0,false);Assert.Greater(owner.References,0);
                Assert.True(session.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal})));
                Assert.AreSame(owner,request.StorageOwner);
                session.ApplyResults();Assert.AreEqual(1,session.RejectedResults);
                Assert.True(session.Move(0,new NavPoint(-5,0)));
                Assert.Zero(owner.References,"Completed transfer must retire its original owner, not retain history.");
            }finally{session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }
        [Test] public void ForeignAnswerRefusesBeforeLazyMailboxAllocationOrRetain()
        {
            var source=Session();var destination=Session();
            try{
                Assert.True(source.Move(0,new NavPoint(5,0)));Assert.True(source.Requests.TryDequeue(out var request));
                var field=typeof(NavMailbox<NavigationAnswer>).GetField("items",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                var backing=field.GetValue(destination.Answers);int references=request.StorageOwner.References;long sourceBytes=source.PhysicalHostBudget.PhysicalBytes,destinationBytes=destination.PhysicalHostBudget.PhysicalBytes;
                Assert.Throws<ArgumentException>(()=>destination.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal})));
                Assert.AreSame(backing,field.GetValue(destination.Answers));Assert.Zero(destination.Answers.Count);
                Assert.AreEqual(references,request.StorageOwner.References);Assert.AreEqual(sourceBytes,source.PhysicalHostBudget.PhysicalBytes);Assert.AreEqual(destinationBytes,destination.PhysicalHostBudget.PhysicalBytes);
            }finally{source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();destination.ReleaseCommittedHostRoot();destination.DestroyNavigationAuthority();}
            Assert.Zero(source.PhysicalHostBudget.PhysicalBytes);Assert.Zero(destination.PhysicalHostBudget.PhysicalBytes);
        }
        private static void EnqueueDestinationOwnedStale(NavigationSession destination,NavigationRequest original)
        {
            Assert.True(destination.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,destination.Generation,0,out var scope));
            using(scope){
                // Explicit test preparation preserves stale scalar values while
                // using this destination's ledger, geometry and profile owners.
                var geometry=destination.Admission.Geometry;
                var owner=NavigationHostRequest.CreateOwned(original.Session,original.Request,original.Entity,original.Order,
                    destination.Admission.Profile,geometry,original.Start,original.Goal,geometry,original.HeldIdentity,
                    original.MemberIdentity,original.StartLocation.HasValue?new NavLocation(original.StartLocation.Value.Position,original.StartLocation.Value.SurfaceId):(NavLocation?)null,
                    original.GoalLocation.HasValue?new NavLocation(original.GoalLocation.Value.Position,original.GoalLocation.Value.SurfaceId):(NavLocation?)null,
                    null,scope.OwnedReservation());
                try{scope.Attach(owner);scope.Commit(owner,_=>Assert.True(destination.Answers.TryEnqueue(new NavigationAnswer(owner.Request,new[]{owner.Request.Goal}))));}
                finally{owner.ReleaseStorage();}
            }
        }
        [Test] public void SessionPublishesPinnedPhysicalHostRootAndDiscardsStaleFork()
        {
            var session=Session();
            Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,1,1,out var first));
            var initial=NavigationHostRoot.CreateOwned(first.OwnedReservation());initial.SealPrepared();first.Attach(initial);
            int published=0;session.PublishPhysicalHostRoot(first,null,initial,()=>published++);
            Assert.AreEqual(1,published);Assert.True(first.IsResolved);
            var pinned=session.PinCommittedHostRoot();Assert.AreSame(initial,pinned);
            long baseline=session.PhysicalHostBudget.PhysicalBytes;
            Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Projection,1,2,out var stale));
            var refused=pinned.ForkOwned(stale.OwnedReservation());refused.SealPrepared();stale.Attach(refused);
            Assert.Catch<InvalidOperationException>(()=>session.PublishPhysicalHostRoot(stale,null,refused,()=>published++));
            Assert.AreEqual(1,published);var still=session.PinCommittedHostRoot();Assert.AreSame(pinned,still);still.Release();
            pinned.Release();stale.Discard();refused.ReleaseStorage();
            Assert.AreEqual(baseline,session.PhysicalHostBudget.PhysicalBytes);
            Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Projection,1,3,out var next));
            var fork=initial.ForkOwned(next.OwnedReservation());fork.SealPrepared();next.Attach(fork);
            session.PublishPhysicalHostRoot(next,initial,fork,()=>published++);
            Assert.AreEqual(2,published);var installed=session.PinCommittedHostRoot();Assert.AreSame(fork,installed);installed.Release();
            fork.ReleaseStorage();initial.ReleaseStorage();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);
            Assert.Zero(session.PhysicalHostBudget.HeldBytes);
            Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }
        [Test] public void SessionPublishesPreparedPhysicalRegistrationWithMailboxHandoff()
        {
            var session=Session();Assert.True(session.Move(0,new NavPoint(5,0)));
            var ingress=session.Requests.CopyItems()[0];var observed=session.Admission;
            var prepared=Scheduled(session,ingress);
            Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,1,1,out var scope));
            var root=NavigationHostRoot.CreateOwned(scope.OwnedReservation());
            var key=root.RegisterOrdinary(ingress,prepared,observed.SurfaceProviderId,scope.OwnedReservation());
            root.SealPrepared();scope.Attach(root);
            session.PublishRegisteredPhysicalHost(scope,null,root,observed,ingress,key);
            Assert.True(scope.IsResolved);Assert.Zero(session.Requests.Count);
            Assert.AreEqual(1,session.RegisteredRouteCount);
            Assert.AreEqual(1,root.Scheduler.PendingCount);
            var canonical=((NavigationHostRow)root.Rows[key]).Request;
            Assert.AreNotSame(ingress,canonical);
            Assert.AreEqual(prepared.Provider.Binding,canonical.CertifiedProviderBinding);
            Assert.True(session.Admission.Contains(canonical));
            Assert.False(session.Admission.Contains(ingress));
            Assert.True(session.TryCompleteRegisteredRoute(new NavigationAnswer(canonical,
                Array.Empty<NavPoint>(),NavSolveStatus.Stale)));
            var pinned=session.PinCommittedHostRoot();Assert.AreSame(root,pinned);pinned.Release();
            root.ReleaseStorage();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);
            Assert.Zero(session.PhysicalHostBudget.HeldBytes);
        }
        [Test] public void StalePhysicalIngressDoesNotPublishPreparedRoot()
        {
            var session=Session();Assert.True(session.Move(0,new NavPoint(5,0)));
            var ingress=session.Requests.CopyItems()[0];var observed=session.Admission;
            var prepared=Scheduled(session,ingress);
            Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,1,1,out var scope));
            var root=NavigationHostRoot.CreateOwned(scope.OwnedReservation());
            var key=root.RegisterOrdinary(ingress,prepared,observed.SurfaceProviderId,scope.OwnedReservation());
            root.SealPrepared();scope.Attach(root);
            session.Stop(0,true);
            Assert.Throws<InvalidOperationException>(()=>session.PublishRegisteredPhysicalHost(scope,null,root,observed,ingress,key));
            Assert.IsNull(session.PinCommittedHostRoot());
            Assert.AreEqual(1,session.Requests.Count);Assert.Zero(session.RegisteredRouteCount);
            root.ReleaseStorage();scope.Discard();session.DestroyNavigationAuthority();
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);
            Assert.Zero(session.PhysicalHostBudget.HeldBytes);
        }
        [Test] public void CertifiedKernelRegistersOrdinaryThroughSessionOwnedPhysicalRoot()
        {
            var session=Session();Assert.True(session.Move(0,new NavPoint(5,0)));
            var ingress=session.Requests.CopyItems()[0];
            var kernel=new CertifiedRouteKernel();
            try{
                var binding=new PlayableRouteBinding(session.Admission.Geometry,PlayableProfile.Default,
                    session.Admission,session.RoutePort);
                Assert.True(kernel.TryRegisterPhysicalOrdinary(binding));
                Assert.Zero(session.Requests.Count);
                Assert.AreEqual(1,session.RegisteredRouteCount);
                var before=session.PinCommittedHostRoot();
                long priorProgress=before.Progress;
                try{
                    Assert.Greater(kernel.AdvancePhysicalOrdinary(binding,64),0);
                    Assert.Zero(before.Scheduler.Counters.LogicalGraphWork);
                }finally{before.Release();}
                var root=session.PinCommittedHostRoot();
                try{
                    Assert.AreEqual(1,root.Rows.Count);
                    Assert.Greater(root.Progress,priorProgress);
                    var row=(NavigationHostRow)root.Rows[root.Scan.First];
                    Assert.True(session.Admission.Contains(row.Request));
                    Assert.False(session.Admission.Contains(ingress));
                    Assert.AreSame(row.Request,((NavigationHostRequest)root.Requests[root.Scan.First]).Request);
                    Assert.AreEqual(root.Scan.First.Value,row.Scheduled.Subscription.StableKey);
                }finally{root.Release();}
            }finally{kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);
            Assert.Zero(session.PhysicalHostBudget.HeldBytes);
            Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }
        [Test] public void CertifiedKernelTransfersActualReadyIntoSessionOwnedProjectionQueue()
        {
            var session=Session();Assert.True(session.Move(0,new NavPoint(5,0)));
            var kernel=new CertifiedRouteKernel();
            try{
                var binding=new PlayableRouteBinding(session.Admission.Geometry,PlayableProfile.Default,
                    session.Admission,session.RoutePort);
                Assert.True(kernel.TryRegisterPhysicalOrdinary(binding));
                bool ready=false;
                for(int n=0;n<32&&!ready;n++){
                    kernel.AdvancePhysicalOrdinary(binding,16384);
                    var current=session.PinCommittedHostRoot();
                    try{ready=current.Scheduler.ReadyCount>0;}finally{current.Release();}
                }
                Assert.True(ready,"the actual scheduler must produce a physical Ready completion");
                Assert.AreEqual(1,kernel.TransferPhysicalReady(binding,1));
                var root=session.PinCommittedHostRoot();
                try{
                    Assert.AreEqual(1,root.Projection.Count);
                    Assert.Zero(root.Scheduler.ReadyCount);
                    var row=(NavigationHostRow)root.Rows[root.Projection.First];
                    Assert.AreEqual(NavSolveStatus.Ready,row.Solved.Status);
                    Assert.AreSame(row.Scheduled.Subscription,row.Solved.Subscription);
                    Assert.True(session.Admission.Contains(row.Request));
                }finally{root.Release();}
                bool projectedReady=false;
                for(int n=0;n<128&&!projectedReady;n++){
                    Assert.AreEqual(1,kernel.ProjectPhysicalOrdinary(binding,64));
                    var state=session.PinCommittedHostRoot();
                    try{projectedReady=state.Completion.Count>0;}finally{state.Release();}
                }
                Assert.True(projectedReady,"bounded physical leg steps must finish the Ready projection");
                var projected=session.PinCommittedHostRoot();
                try{
                    Assert.Zero(projected.Projection.Count);
                    Assert.AreEqual(1,projected.Completion.Count);
                    var row=(NavigationHostRow)projected.Rows[projected.Completion.First];
                    Assert.AreEqual(NavSolveStatus.Ready,row.Answer.Status);
                    Assert.AreSame(row.Request,row.Answer.Request);
                    Assert.AreSame(row.Solved.Route,row.AnswerOwner.CorridorOwner.TypedRoute);
                }finally{projected.Release();}
                Assert.True(kernel.FlushPhysicalOrdinary(binding));
                var delivered=session.PinCommittedHostRoot();
                try{
                    Assert.Zero(delivered.Rows.Count);
                    Assert.Zero(delivered.Completion.Count);
                    Assert.AreEqual(1,delivered.Delivered.Count);
                    var physical=((NavigationHostRow)delivered.Delivered[delivered.Delivered.First().Key]).Answer;
                    Assert.AreSame(physical,session.Answers.CopyItems()[0]);
                }finally{delivered.Release();}
                Assert.False(kernel.RetirePhysicalDelivered(binding),
                    "the physical owner remains while its answer is in the authority mailbox");
                session.ApplyResults();
                Assert.AreEqual(1,session.AppliedResults);
                Assert.False(kernel.RetirePhysicalDelivered(binding),
                    "installed execution still uses the physical corridor and request facets");
                session.Stop(0,true);
                Assert.True(kernel.RetirePhysicalDelivered(binding));
                var retired=session.PinCommittedHostRoot();
                try{Assert.Zero(retired.Delivered.Count);}finally{retired.Release();}
            }finally{kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();}
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);
            Assert.Zero(session.PhysicalHostBudget.HeldBytes);
        }
        [Test] public void CrowdRestoreContinuesMovingRouteWithoutReplayingPrefix()
        {
            var geometry=new NavGeometry(20,new NavObstacle[0],1);var profile=new NavigationProfile();
            var continuous=new NavCrowd(geometry,profile);continuous.Add(7,new NavPoint(0,0));
            Assert.IsTrue(continuous.SetRoute(7,new NavPoint(8,0),new[]{new NavPoint(8,0)}));
            for(int i=0;i<21;i++)continuous.Step(1d/30d);
            var saved=continuous.CaptureState();var restored=new NavCrowd(geometry,profile);restored.RestoreState(saved);
            saved.Units[0].Route[0]=new NavPoint(-8,0); // The restored route owns its copy.
            for(int i=0;i<100;i++){
                continuous.Step(1d/30d);restored.Step(1d/30d);
                var a=continuous.Units[0];var b=restored.Units[0];
                Assert.AreEqual(a.Position.X,b.Position.X);Assert.AreEqual(a.Position.Z,b.Position.Z);
                Assert.AreEqual(a.Heading,b.Heading);Assert.AreEqual(a.Outcome,b.Outcome);
                Assert.AreEqual(a.Moving,b.Moving);Assert.AreEqual(a.Route.Count,b.Route.Count);
            }
            Assert.Throws<System.ArgumentException>(()=>new NavCrowd(new NavGeometry(21,new NavObstacle[0],1),profile).RestoreState(continuous.CaptureState()));
        }
        [Test] public void AdmissionPublicationIdentitySurvivesStateRestoreAndNoOpCapture()
        {
            var source=Session();Assert.True(source.Move(0,new NavPoint(8,0)));var accepted=source.Admission;
            Assert.Greater(accepted.PublicationIdentity,0);var saved=source.CaptureState();Assert.AreSame(accepted,source.Admission);
            Assert.AreEqual(accepted.PublicationIdentity,saved.AdmissionPublicationIdentity);
            var restored=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),new NavigationProfile());restored.RestoreState(saved);
            Assert.AreNotSame(accepted,restored.Admission);Assert.AreEqual(accepted.PublicationIdentity,restored.Admission.PublicationIdentity);
            var before=restored.Admission;restored.CaptureState();restored.PrepareDeliveryBarrier();Assert.AreSame(before,restored.Admission);
            Assert.True(source.Move(0,new NavPoint(9,0)));Assert.True(restored.Move(0,new NavPoint(9,0)));
            Assert.AreEqual(accepted.PublicationIdentity+1,source.Admission.PublicationIdentity);Assert.AreEqual(source.Admission.PublicationIdentity,restored.Admission.PublicationIdentity);
        }
        [Test] public void LegacyAdmissionIdentityReceivesFreshPublicationAndNegativeRejectsBeforeInstall()
        {
            var source=Session();source.Move(0,new NavPoint(8,0));var state=source.CaptureState();state.AdmissionPublicationIdentity=-1;
            var rejected=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),new NavigationProfile());
            Assert.Throws<ArgumentException>(()=>rejected.RestoreState(state));Assert.Zero(rejected.Crowd.Units.Count);
            state.AdmissionPublicationIdentity=0;var legacy=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),new NavigationProfile());legacy.RestoreState(state);
            Assert.AreEqual(1,legacy.Admission.PublicationIdentity);
        }
        private static NavigationSessionState LegacyWire(NavigationSessionState state)
        {
            Assert.Null(state.HostAuthority,"This fixture exercises explicit legacy replay, never physical BODY continuation");
            return WorldWire.Unpack(WorldWire.Pack(writer=>WorldWire.Write(writer,state)),reader=>WorldWire.ReadNavigationSessionState(reader,NavigationV8Format.A5Arrival));
        }
        private static void DestroyFixtureSession(NavigationSession session)
        {if(session==null)return;session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
        private NavigationSession LegacySession(){var session=CertifiedHostRouteTests.LegacyCheckpointSession(new NavGeometry(20,Array.Empty<NavObstacle>(),1),new NavigationProfile());session.Crowd.Add(0,new NavPoint(0,0));return session;}
        private NavigationSession Session(){var s=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());s.Crowd.Add(0,new NavPoint(0,0));return s;}
        private static NavScheduledRequest Scheduled(NavigationSession source,NavigationRequest request,string defect=null)
        {
            var boundary=new NavPolygon(new[]{new NavPoint(-20,-20),new NavPoint(20,-20),new NavPoint(20,20),new NavPoint(-20,20)});
            var provider=new LayeredNavigationProvider("transport-rendezvous",1,1,1,new string('b',64),
                new[]{new NavLayerSurface("flat","ground",0,1,new NavTraversalArea(boundary,20,Array.Empty<NavObstacle>()))},
                Array.Empty<NavLayerPortal>());
            var identity=request.MemberIdentity;var group=source.Admission.Groups[0];
            var subscription=new NavRouteSubscription(request.Session,group.GroupId,group.RootOrderRevision+(defect=="root"?1:0),
                group.CommandSequence+(defect=="command"?1:0),group.IssuedTick+(defect=="issued"?1:0),identity.OrderRevision+(defect=="activation"?1:0),request.Entity,identity.Incarnation,
                identity.OrderRevision,identity.MobilityRevision+(defect=="mobility"?1:0),request.Request,request.StartLocation.Value,
                request.GoalLocation.Value,request.HeldIdentity);
            return NavScheduledRequest.ForSingleton(provider,new NavClearanceProfile("ground",1,"ground",(int)identity.MobilityRevision,request.Profile.Radius+(defect=="radius"?.1:0)),
                new NavGraphProfile(defect=="profile"?new string('c',64):NavigationAdmission.ExactProfileBinding(request.Profile),"distance",1,request.Profile.GridCell),
                new NavGraphCompileLimits(5000,5000,20000),subscription,5000);
        }
        [Test] public void SessionRestoreRebindsPendingAndBothMailboxesAndMatchesSuffix()
        {
            var geometry=new NavGeometry(20,new NavObstacle[0],2);var profile=new NavigationProfile();
            var continuous=new NavigationSession(4,geometry,profile);
            continuous.Crowd.Add(0,new NavPoint(0,0));continuous.Crowd.Add(1,new NavPoint(0,3));
            Assert.IsTrue(continuous.Move(0,new NavPoint(8,0)));
            NavigationRequest first;Assert.IsTrue(continuous.Requests.TryDequeue(out first));
            continuous.Answers.TryEnqueue(new NavigationAnswer(first,new[]{first.Goal}));
            continuous.Step(1d/30);for(int i=0;i<20;i++)continuous.Step(1d/30);
            Assert.IsTrue(continuous.Move(0,new NavPoint(9,0)));
            NavigationRequest obsolete;Assert.IsTrue(continuous.Requests.TryDequeue(out obsolete));
            continuous.Stop(0,false);continuous.Answers.TryEnqueue(new NavigationAnswer(obsolete,new[]{obsolete.Goal}));
            Assert.IsTrue(continuous.Move(0,new NavPoint(8,0)));
            Assert.IsTrue(continuous.Move(1,new NavPoint(8,3)));
            NavigationRequest queued;Assert.IsTrue(continuous.Requests.TryDequeue(out queued));
            continuous.Answers.TryEnqueue(new NavigationAnswer(queued,new[]{queued.Goal}));
            var saved=continuous.CaptureState();
            var restored=new NavigationSession(4,new NavGeometry(20,new NavObstacle[0],2),new NavigationProfile());
            restored.RestoreState(saved);
            Assert.AreEqual(continuous.PendingCount,restored.PendingCount);
            Assert.AreEqual(continuous.Requests.Count,restored.Requests.Count);
            Assert.AreEqual(continuous.Answers.Count,restored.Answers.Count);
            saved.AnswerMailbox[1].Route[0]=new NavPoint(-9,-9); // Restore owns the valid route copy.
            for(int tick=0;tick<120;tick++){
                NavigationRequest a,b;
                Assert.AreEqual(continuous.Requests.TryDequeue(out a),restored.Requests.TryDequeue(out b));
                if(a!=null){Assert.AreEqual(a.Request,b.Request);Assert.AreEqual(a.Order,b.Order);
                    continuous.Answers.TryEnqueue(new NavigationAnswer(a,new[]{a.Goal}));
                    restored.Answers.TryEnqueue(new NavigationAnswer(b,new[]{b.Goal}));}
                continuous.Step(1d/30);restored.Step(1d/30);
                Assert.AreEqual(continuous.AppliedResults,restored.AppliedResults);
                Assert.AreEqual(continuous.RejectedResults,restored.RejectedResults);
                Assert.AreEqual(continuous.UnreachableResults,restored.UnreachableResults);
                Assert.AreEqual(continuous.PendingCount,restored.PendingCount);
                for(int i=0;i<2;i++){
                    var u=continuous.Crowd.Units[i];var v=restored.Crowd.Units[i];
                    Assert.AreEqual(u.Position.X,v.Position.X);Assert.AreEqual(u.Position.Z,v.Position.Z);
                    Assert.AreEqual(u.Heading,v.Heading);Assert.AreEqual(u.Outcome,v.Outcome);
                    Assert.AreEqual(u.Moving,v.Moving);Assert.AreEqual(u.Route.Count,v.Route.Count);
                }
            }
            Assert.IsTrue(continuous.Move(1,new NavPoint(-8,3)));
            Assert.IsTrue(restored.Move(1,new NavPoint(-8,3)));
            NavigationRequest nextA,nextB;continuous.Requests.TryDequeue(out nextA);restored.Requests.TryDequeue(out nextB);
            Assert.AreEqual(nextA.Request,nextB.Request);
        }
        [Test] public void SessionRestoreRejectsWrongGenerationGeometryAndProfileBeforeInstallingUnits()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));var saved=source.CaptureState();
            var generation=new NavigationSession(2,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            Assert.Throws<System.ArgumentException>(()=>generation.RestoreState(saved));Assert.AreEqual(0,generation.Crowd.Units.Count);
            var geometry=new NavigationSession(1,new NavGeometry(21,new NavObstacle[0],1),new NavigationProfile());
            Assert.Throws<System.ArgumentException>(()=>geometry.RestoreState(saved));Assert.AreEqual(0,geometry.Crowd.Units.Count);
            var profile=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile("other",1,.72,4,4.5,.8,.15,3.6,.08));
            Assert.Throws<System.ArgumentException>(()=>profile.RestoreState(saved));Assert.AreEqual(0,profile.Crowd.Units.Count);
            saved.PendingRequestIndices[0]=99;
            var invalid=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            Assert.Throws<System.ArgumentException>(()=>invalid.RestoreState(saved));Assert.AreEqual(0,invalid.Crowd.Units.Count);
        }
        [Test] public void SessionRestorePreservesStaleOtherGenerationAnswer()
        {
            var old=Session();old.Move(0,new NavPoint(5,0));
            NavigationRequest oldRequest;old.Requests.TryDequeue(out oldRequest);
            var current=new NavigationSession(2,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            current.Crowd.Add(0,new NavPoint(0,0));EnqueueDestinationOwnedStale(current,oldRequest);
            var restored=new NavigationSession(2,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            restored.RestoreState(current.CaptureState());
            current.ApplyResults();restored.ApplyResults();
            Assert.AreEqual(1,current.RejectedResults);Assert.AreEqual(current.RejectedResults,restored.RejectedResults);
            Assert.IsTrue(current.Move(0,new NavPoint(5,0)));Assert.IsTrue(restored.Move(0,new NavPoint(5,0)));
            NavigationRequest a,b;current.Requests.TryDequeue(out a);restored.Requests.TryDequeue(out b);
            Assert.AreEqual(1,a.Request);Assert.AreEqual(a.Request,b.Request);
        }
        [Test] public void SessionCaptureRejectsRequestHeldByExternalWorker()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            NavigationRequest request;source.Requests.TryDequeue(out request);
            Assert.Throws<System.InvalidOperationException>(()=>source.CaptureState());
            source.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal}));
            var saved=source.CaptureState();saved.AnswerMailbox=new NavigationAnswerState[0];
            var destination=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            Assert.Throws<System.ArgumentException>(()=>destination.RestoreState(saved));
            Assert.AreEqual(0,destination.Crowd.Units.Count);
        }
        [Test] public void RegisteredTransportReplaysOriginalFifoAndProbeAcrossCapture()
        {
            var source=LegacySession();NavigationSession restored=null;
            try{
            Assert.IsTrue(source.Move(0,new NavPoint(5,0)));
            Assert.IsTrue(source.TryTransferRoute(_=>true,out var held));
            Assert.AreEqual(1,source.RegisteredRouteCount);
            Assert.IsTrue(source.Move(0,new NavPoint(6,0))); // pending replacement
            var probe=source.Probe(1,1,new NavigationProfile(),new NavPoint(0,0),new NavPoint(3,0));
            Assert.NotNull(probe);
            Assert.IsTrue(source.TryTransferRoute(_=>true,out var replacement));
            Assert.AreEqual(2,source.RegisteredRouteCount);
            var saved=source.CaptureState();
            Assert.AreEqual(3,saved.RequestMailboxIndices.Length);
            Assert.AreEqual(held.Request,saved.Requests[saved.RequestMailboxIndices[0]].Request);
            Assert.AreEqual(replacement.Request,saved.Requests[saved.RequestMailboxIndices[1]].Request);
            Assert.AreEqual(probe.Request,saved.Requests[saved.RequestMailboxIndices[2]].Request);
            restored=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            using(var wire=LegacyWire(saved))restored.RestoreState(wire);
            Assert.AreEqual(0,restored.RegisteredRouteCount);
            Assert.AreEqual(3,restored.Requests.Count);
            Assert.AreEqual(2,restored.PendingCount);
            Assert.AreEqual(PlayableWorldRestoreTests.Facts(source.Admission),
                PlayableWorldRestoreTests.Facts(restored.Admission),"Complete admission projection including producer probe");
            foreach(var expected in new[]{held,replacement,probe}){
                Assert.IsTrue(restored.Requests.TryDequeue(out var replay));
                Assert.AreEqual(expected.Request,replay.Request);
                Assert.IsTrue(restored.Answers.TryEnqueue(new NavigationAnswer(replay,new[]{replay.Goal})));
            }
            restored.ApplyResults();
            Assert.AreEqual(1,restored.RejectedResults); // superseded original
            Assert.AreEqual(1,restored.AppliedResults);
            Assert.IsTrue(restored.TryProbeAnswer(out var probeAnswer));
            Assert.AreEqual(probe.Request,probeAnswer.Request.Request);
            Assert.IsFalse(restored.TryProbeAnswer(out _));
            }finally{DestroyFixtureSession(restored);DestroyFixtureSession(source);}
        }
        [Test] public void FrozenAwaitingRoutesBarrierCapturesRegisteredInputForReplay()
        {
            var source=LegacySession();NavigationSession restored=null;
            try{source.Move(0,new NavPoint(5,0));
            Assert.True(source.TryTransferRoute(_=>true,out var original));
            source.PrepareDeliveryBarrier();
            Assert.False(source.DeliveryBarrierReady);
            var saved=source.CaptureState();
            restored=new NavigationSession(1,new NavGeometry(20,new NavObstacle[0],1),new NavigationProfile());
            using(var wire=LegacyWire(saved))restored.RestoreState(wire);
            restored.PrepareDeliveryBarrier();
            Assert.False(restored.DeliveryBarrierReady);
            Assert.True(restored.Requests.TryDequeue(out var replay));
            Assert.AreEqual(original.Request,replay.Request);
            Assert.True(restored.Answers.TryEnqueue(new NavigationAnswer(replay,new[]{replay.Goal})));
            Assert.True(restored.DeliveryBarrierReady);
            restored.ApplyResults();Assert.AreEqual(1,restored.AppliedResults);
            }finally{DestroyFixtureSession(restored);DestroyFixtureSession(source);}
        }
        [Test] public void RegisteredTransportOwnsRequestUntilAnswerIsPublished()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            Assert.IsFalse(source.TryTransferRoute(_=>false,out _));
            Assert.AreEqual(1,source.Requests.Count);
            Assert.IsTrue(source.TryTransferRoute(_=>true,out var request));
            Assert.AreEqual(0,source.Requests.Count);
            var answer=new NavigationAnswer(request,new[]{request.Goal});
            Assert.IsTrue(source.TryCompleteRegisteredRoute(answer));
            Assert.IsFalse(source.TryCompleteRegisteredRoute(answer));
            Assert.AreEqual(0,source.RegisteredRouteCount);
            var saved=source.CaptureState();
            Assert.AreEqual(0,saved.RequestMailboxIndices.Length);
            Assert.AreEqual(1,saved.AnswerMailbox.Length);
        }
        [Test] public void RegisteredInputsCountAgainstCheckpointCapacityAndBackpressure()
        {
            var source=LegacySession();
            try{
            Assert.True(source.Move(0,new NavPoint(5,0)));
            Assert.True(source.TryTransferRoute(_=>true,out var held));
            for(int i=1;i<NavigationSession.RouteRequestCapacity;i++)
                Assert.True(source.Move(0,new NavPoint(5+i*.001,0)));
            Assert.AreEqual(NavigationSession.RouteRequestCapacity-1,source.Requests.Count);
            Assert.False(source.Move(0,new NavPoint(7,0)));
            Assert.IsNull(source.Probe(1,1,new NavigationProfile(),new NavPoint(0,0),new NavPoint(3,0)));
            Assert.AreEqual(NavigationSession.RouteRequestCapacity,source.CaptureState().RequestMailboxIndices.Length);
            Assert.True(source.TryCompleteRegisteredRoute(new NavigationAnswer(held,new[]{held.Goal})));
            Assert.True(source.Move(0,new NavPoint(7,0)));
            Assert.AreEqual(NavigationSession.RouteRequestCapacity,source.CaptureState().RequestMailboxIndices.Length);
            }finally{DestroyFixtureSession(source);}
        }
        [Test] public void ConcurrentStaleDropNeverRejectsUnpublishedCurrentRequest()
        {
            var source=Session();var errors=new System.Collections.Generic.List<Exception>();
            int done=0;
            var worker=new System.Threading.Thread(()=>{
                try {
                    while(System.Threading.Volatile.Read(ref done)==0){source.TryDropStaleRoute(out _);System.Threading.Thread.Yield();}
                } catch(Exception error){lock(errors)errors.Add(error);}
            });
            worker.Start();
            for(int i=0;i<100;i++)Assert.True(source.Move(0,new NavPoint(5+i*.01,0)));
            System.Threading.Volatile.Write(ref done,1);
            Assert.True(worker.Join(5000));Assert.AreEqual(0,errors.Count);
            var state=source.CaptureState();
            var current=state.Requests[state.PendingRequestIndices[0]];
            Assert.AreEqual(100,current.Request);
            Assert.True(System.Linq.Enumerable.Contains(state.RequestMailboxIndices,state.PendingRequestIndices[0]));
            foreach(var answer in state.AnswerMailbox)Assert.Less(state.Requests[answer.RequestIndex].Request,current.Request);
        }
        [Test] public void GroupBatchAdmissionNeverPublishesPartialRoster()
        {
            var source=new NavigationSession(1,new NavGeometry(30,new NavObstacle[0],1),new NavigationProfile());
            var ids=new int[10];var goals=new NavPoint[10];
            for(int i=0;i<10;i++){ids[i]=i;source.Crowd.Add(i,new NavPoint(-10,-18+i*4));goals[i]=new NavPoint(10,-18+i*4);}
            int done=0,partial=0;Exception error=null;
            var observer=new System.Threading.Thread(()=>{
                try{while(System.Threading.Volatile.Read(ref done)==0){
                    var published=source.Admission;
                    if(published.Groups.Count>0&&(published.Groups[0].Members.Count!=10||published.Requests.Count!=10))
                        System.Threading.Interlocked.Increment(ref partial);
                    source.TryDropStaleRoute(out _);System.Threading.Thread.Yield();
                }}catch(Exception ex){error=ex;}
            });
            observer.Start();Assert.True(source.MoveGroup(ids,goals));
            System.Threading.Volatile.Write(ref done,1);Assert.True(observer.Join(5000));
            Assert.IsNull(error);
            Assert.AreEqual(0,partial);
            Assert.AreEqual(10,source.Admission.Groups[0].Members.Count);
            Assert.AreEqual(10,source.Requests.Count);Assert.AreEqual(0,source.Answers.Count);
        }
        [Test] public void CommandCohortIsInvisibleUntilFinishAndOpenBatchCannotBeCaptured()
        {
            var source=Session();source.Crowd.Add(1,new NavPoint(0,3));
            var command=new PlayableCommand(1,7,"player-1",PlayableCommandKind.Move,new[]{0,1},new NavPoint(5,0),origin:PlayableOrderOrigin.Human)
                .WithResolvedLocation(new NavLocation(new NavPoint(5,0),"flat"));
            source.PrepareCommandGroup(command,4,12,1);
            Assert.True(source.Move(0,new NavPoint(5,0)));
            Assert.AreEqual(0,source.Admission.PublishedThroughRequestSequence);
            Assert.False(source.TryDropStaleRoute(out _));
            Assert.False(source.TryTransferRoute(_=>true,out _));
            Assert.Throws<InvalidOperationException>(()=>source.CaptureState());
            Assert.True(source.Move(1,new NavPoint(5,3)));
            Assert.AreEqual(0,source.Admission.Groups.Count);
            source.FinishCommandGroup(true,new[]{0,1});
            Assert.AreEqual(2,source.Admission.PublishedThroughRequestSequence);
            Assert.AreEqual(2,source.Admission.Groups[0].Members.Count);
            Assert.AreEqual(2,source.Admission.Requests.Count);
            Assert.AreEqual(7,source.Admission.Groups[0].CommandSequence);
            Assert.AreEqual(12,source.Admission.Groups[0].IssuedTick);
            Assert.AreEqual(4,source.Admission.Groups[0].Members[0].ActivationRevision);
            Assert.AreEqual("flat",source.Admission.Groups[0].OriginalTerminal.Value.SurfaceId);
        }
        [Test] public void SchedulerPreparationCanPauseAcrossStopOrCaptureWithoutGateDeadlock()
        {
            void Run(bool stop)
            {
                var source=Session();source.Move(0,new NavPoint(5,0));
                var scheduler=new LayeredNavigationScheduler();
                using(var entered=new System.Threading.ManualResetEventSlim())
                using(var release=new System.Threading.ManualResetEventSlim()){
                    bool transferred=false;Exception error=null;
                    var worker=new System.Threading.Thread(()=>{
                        try{transferred=source.TryTransferRouteToScheduler(scheduler,r=>{
                            entered.Set();if(!release.Wait(5000))throw new Exception("Timed out waiting for preparation release.");
                            return Scheduled(source,r);
                        },out _);}catch(Exception ex){error=ex;}
                    });
                    worker.Start();Assert.True(entered.Wait(5000));
                    if(stop)source.Stop(0,true);else Assert.AreEqual(1,source.CaptureState().RequestMailboxIndices.Length);
                    release.Set();Assert.True(worker.Join(5000));Assert.IsNull(error);
                    if(stop){Assert.False(transferred);Assert.AreEqual(1,source.Requests.Count);Assert.True(source.TryDropStaleRoute(out _));}
                    else {Assert.True(transferred);Assert.AreEqual(0,source.Requests.Count);Assert.AreEqual(1,source.RegisteredRouteCount);}
                }
            }
            Run(true);Run(false);
        }
        [TestCase("root")][TestCase("command")][TestCase("issued")][TestCase("activation")]
        [TestCase("mobility")][TestCase("profile")][TestCase("radius")]
        public void SchedulerAdmissionRejectsMismatchedAuthorityEnvelopeBeforeSubmit(string defect)
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            var scheduler=new LayeredNavigationScheduler();
            Assert.Throws<InvalidOperationException>(()=>source.TryTransferRouteToScheduler(scheduler,
                request=>Scheduled(source,request,defect),out _));
            Assert.AreEqual(1,source.Requests.Count,defect);
            Assert.AreEqual(0,source.RegisteredRouteCount,defect);
            Assert.AreEqual(0,scheduler.PendingCount,defect);
            Assert.AreEqual(0,scheduler.ReadyCount,defect);
            Assert.AreEqual(0,scheduler.Counters.PeakQueue,defect);
        }
        [Test] public void FullAnswerMailboxRetainsRegisteredOwnershipUntilCapacityReturns()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            Assert.True(source.TryTransferRoute(_=>true,out var held));
            var filler=new NavigationRequest(1,999,42,1,new NavigationProfile(),held.Geometry,held.Start,held.Goal);
            for(int i=0;i<NavMailbox<NavigationAnswer>.Capacity;i++)
                Assert.True(source.Answers.TryEnqueue(new NavigationAnswer(filler,new[]{filler.Goal})));
            var answer=new NavigationAnswer(held,new[]{held.Goal});
            Assert.False(source.TryCompleteRegisteredRoute(answer));
            Assert.AreEqual(1,source.RegisteredRouteCount);
            Assert.True(source.Answers.TryDequeue(out _));
            Assert.True(source.TryCompleteRegisteredRoute(answer));
            Assert.AreEqual(0,source.RegisteredRouteCount);
        }
        [Test] public void UnserviceableRouteWaitsForAnswerCapacityAndKeepsTypedFailure()
        {
            var source=Session();Assert.True(source.Move(0,new NavPoint(5,0)));
            Assert.AreEqual(1,source.Requests.Count);
            var head=source.Requests.CopyItems()[0];
            var filler=new NavigationRequest(1,999,42,1,new NavigationProfile(),head.Geometry,head.Start,head.Goal);
            for(int i=0;i<NavMailbox<NavigationAnswer>.Capacity;i++)
                Assert.True(source.Answers.TryEnqueue(new NavigationAnswer(filler,new[]{filler.Goal})));
            Assert.False(source.TryRejectUnserviceableRoute(head,out _));
            Assert.AreEqual(1,source.Requests.Count);
            Assert.True(source.Answers.TryDequeue(out _));
            Assert.True(source.TryRejectUnserviceableRoute(head,out var removed));
            Assert.AreSame(head,removed);
            Assert.AreEqual(0,source.Requests.Count);
            Assert.True(source.Answers.TryDequeue(out var next));
            while(!ReferenceEquals(next.Request,head))Assert.True(source.Answers.TryDequeue(out next));
            Assert.AreEqual(NavSolveStatus.CapacityExceeded,next.Status);
        }
        [Test] public void TransferCallbackExceptionAndReentryLeaveMailboxHeadOwnedByAuthority()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            Assert.Throws<InvalidOperationException>(()=>source.TryTransferRoute(r=>{
                source.Requests.TryDequeue(out _);return true;
            },out _));
            Assert.Throws<InvalidOperationException>(()=>source.TryTransferRoute(r=>{
                source.CaptureState();return true;
            },out _));
            Assert.Throws<InvalidOperationException>(()=>source.TryTransferRoute(r=>throw new InvalidOperationException("admission failed"),out _));
            Assert.AreEqual(1,source.Requests.Count);
            Assert.AreEqual(0,source.RegisteredRouteCount);
            Assert.AreEqual(1,source.CaptureState().RequestMailboxIndices.Length);
        }
        [Test] public void StaleMailboxHeadMovesAtomicallyToRejectedAnswerBeforeNewerRequest()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            source.Move(0,new NavPoint(6,0));
            Assert.True(source.TryDropStaleRoute(out var stale));
            Assert.AreEqual(1,source.Requests.Count);
            Assert.AreEqual(1,source.Answers.Count);
            Assert.False(source.TryDropStaleRoute(out _));
            var saved=source.CaptureState();
            Assert.AreEqual(stale.Request,saved.Requests[saved.AnswerMailbox[0].RequestIndex].Request);
            source.ApplyResults();
            Assert.AreEqual(1,source.RejectedResults);
            Assert.AreEqual(1,source.PendingCount);
        }
        [Test] public void RebindAndGeometryChangeRetainRegisteredStaleResultsForReplayAndRejection()
        {
            var source=LegacySession();
            try{source.Move(0,new NavPoint(5,0));
            Assert.True(source.TryTransferRoute(_=>true,out var old));
            source.Rebind(new NavigationProfile("next",2,.72,4,4.5,.8,.15,3.6,.08));
            Assert.AreEqual(2,source.CaptureState().RequestMailboxIndices.Length);
            Assert.True(source.TryCompleteRegisteredRoute(new NavigationAnswer(old,new[]{old.Goal})));
            source.ApplyResults();Assert.AreEqual(1,source.RejectedResults);
            Assert.True(source.TryTransferRoute(_=>true,out var rebound));
            source.ChangeGeometry(new NavGeometry(20,new NavObstacle[0],2));
            Assert.AreEqual(2,source.CaptureState().RequestMailboxIndices.Length);
            Assert.True(source.TryCompleteRegisteredRoute(new NavigationAnswer(rebound,new[]{rebound.Goal})));
            source.ApplyResults();Assert.AreEqual(2,source.RejectedResults);
            }finally{DestroyFixtureSession(source);}
        }
        [Test] public void AdmissionPublicationIsDetachedAndRetainsOriginalIssuance()
        {
            var source=Session();source.Move(0,new NavPoint(5,0));
            var first=source.Admission;
            Assert.AreEqual(1,first.Groups.Count);
            Assert.AreEqual(1,first.Requests.Count);
            Assert.AreEqual(0,first.Groups[0].Members[0].Entity);
            Assert.AreEqual(first.Requests[0],first.Groups[0].Members[0].PendingRequest);
            source.Move(0,new NavPoint(6,0));
            var second=source.Admission;
            Assert.AreNotSame(first,second);
            Assert.AreEqual(5,first.Groups[0].Members[0].AssignedEndpoint.Value.Position.X);
            Assert.AreEqual(6,second.Groups[0].Members[0].AssignedEndpoint.Value.Position.X);
            Assert.IsFalse(second.Contains(first.Requests[0]));
            Assert.AreEqual(first.Groups[0].GroupId,second.Groups[0].GroupId);
            Assert.AreEqual(first.Groups[0].RootOrderRevision,second.Groups[0].RootOrderRevision);
        }
        [Test] public void HoldPublishesFreshMobilityWithoutCancellingSibling()
        {
            var source=Session();source.Crowd.Add(1,new NavPoint(0,3));
            Assert.True(source.MoveGroup(new[]{0,1},new[]{new NavPoint(5,0),new NavPoint(5,3)}));
            var before=source.Admission;
            var sibling=before.Groups[0].Members[1].PendingRequest;
            source.Stop(0,true);
            var after=source.Admission;
            Assert.AreEqual(2,after.Groups[0].Members.Count);
            Assert.Greater(after.Groups[0].Members[0].MobilityRevision,before.Groups[0].Members[0].MobilityRevision);
            Assert.IsNull(after.Groups[0].Members[0].PendingRequest);
            Assert.AreSame(sibling,after.Groups[0].Members[1].PendingRequest);
            Assert.True(after.Contains(sibling));
            Assert.False(after.Contains(before.Groups[0].Members[0].PendingRequest));
        }
        [Test] public void RegisteredSchedulerCompilerFieldConnectorAndReadyCaptureReplayOriginalInput()
        {
            var blocker=new NavObstacle(-.5,-1,.5,1);
            var geometry=new NavGeometry(10,new[]{blocker},1);
            var source=CertifiedHostRouteTests.LegacyCheckpointSession(geometry,new NavigationProfile());
            try{
            source.Crowd.Add(0,new NavPoint(-3,0));
            Assert.IsTrue(source.Move(0,new NavPoint(3,0)));
            var boundary=new NavPolygon(new[]{new NavPoint(-10,-10),new NavPoint(10,-10),new NavPoint(10,10),new NavPoint(-10,10)});
            var area=new NavTraversalArea(boundary,10,new[]{blocker});
            var provider=new LayeredNavigationProvider("transport-test",1,1,1,new string('b',64),
                new[]{new NavLayerSurface("flat","ground",0,1,area)},Array.Empty<NavLayerPortal>());
            var scheduler=new LayeredNavigationScheduler();
            var start=new NavLocation(new NavPoint(-3,0),"flat");
            var goal=new NavLocation(new NavPoint(3,0),"flat");
            var group=source.Admission.Groups[0];
            Assert.IsTrue(source.TryTransferRouteToScheduler(scheduler,request=>{
                var identity=request.MemberIdentity;
                var subscription=new NavRouteSubscription(1,group.GroupId,group.RootOrderRevision,
                    group.CommandSequence,group.IssuedTick,identity.OrderRevision,request.Entity,
                    identity.Incarnation,identity.OrderRevision,identity.MobilityRevision,request.Request,
                    start,goal,request.HeldIdentity);
                return new NavScheduledRequest(provider,new NavClearanceProfile("ground",1,"ground",(int)identity.MobilityRevision,.72),
                    new NavGraphProfile(NavigationAdmission.ExactProfileBinding(request.Profile),"distance",1,.8),
                    new NavGraphCompileLimits(5000,5000,20000),new NavTerminalRegion("target",1,goal,new[]{goal}),
                    subscription,5000);
            },out var original));
            void Replay(NavigationSessionState saved)
            {
                var restored=new NavigationSession(1,new NavGeometry(10,new[]{blocker},1),new NavigationProfile());
                try{using(var wire=LegacyWire(saved))restored.RestoreState(wire);
                Assert.AreEqual(1,restored.Requests.Count);
                Assert.AreEqual(0,restored.RegisteredRouteCount);
                Assert.True(restored.Requests.TryDequeue(out var replay));
                Assert.AreEqual(original.Request,replay.Request);
                Assert.False(restored.Requests.TryDequeue(out _));
                }finally{DestroyFixtureSession(restored);}
            }
            Replay(source.CaptureState());
            bool graph=false,field=false,connector=false,ready=false;
            for(int epoch=0;epoch<1000&&!ready;epoch++){
                scheduler.Advance(epoch,new NavEpochQuota(250,250,250),_=>true,100);
                var counters=scheduler.Counters;
                if(!graph&&counters.PhysicalGraphWork>0){Replay(source.CaptureState());graph=true;}
                if(!field&&counters.PhysicalFieldWork>0){Replay(source.CaptureState());field=true;}
                if(!connector&&counters.ConnectorWork>0){Replay(source.CaptureState());connector=true;}
                if(scheduler.ReadyCount>0){Replay(source.CaptureState());ready=true;}
            }
            var resultStatus=scheduler.TryTake(_=>true,out var completion)?completion.Status.ToString():"none";
            Assert.True(graph,"compiler capture; status="+resultStatus);
            Assert.True(field,"field capture; status="+resultStatus);
            Assert.True(connector,"connector capture; status="+resultStatus+" graph="+scheduler.Counters.PhysicalGraphWork+" field="+scheduler.Counters.PhysicalFieldWork);
            Assert.True(ready,"ready capture; status="+resultStatus);
            }finally{DestroyFixtureSession(source);}
        }
        [Test] public void SessionRestorePreservesReservationsAndGeometryReissueIntent()
        {
            var source=new NavigationSession(3,new NavGeometry(50,new NavObstacle[0],1),new NavigationProfile());
            source.Crowd.Add(0,new NavPoint(-24,-4));source.Crowd.Add(1,new NavPoint(-24,4));
            var goal=source.AllocateArrivalSlots(new NavPoint(24,0),new[]{0})[0];source.Move(0,goal);
            var destination=new NavigationSession(3,new NavGeometry(50,new NavObstacle[0],1),new NavigationProfile());
            destination.RestoreState(source.CaptureState());
            var a=source.AllocateArrivalSlots(new NavPoint(24,0),new[]{1});
            var b=destination.AllocateArrivalSlots(new NavPoint(24,0),new[]{1});
            Assert.AreEqual(a[0].X,b[0].X);Assert.AreEqual(a[0].Z,b[0].Z);
            source.ChangeGeometry(new NavGeometry(50,new NavObstacle[0],2));
            destination.ChangeGeometry(new NavGeometry(50,new NavObstacle[0],2));
            NavigationRequest nextA,nextB;
            Assert.IsTrue(source.Requests.TryDequeue(out nextA));Assert.IsTrue(destination.Requests.TryDequeue(out nextB));
            Assert.AreEqual(nextA.Request,nextB.Request);Assert.AreEqual(nextA.Order,nextB.Order);
            Assert.AreEqual(goal.X,nextB.Goal.X);Assert.AreEqual(goal.Z,nextB.Goal.Z);
            source.Answers.TryEnqueue(new NavigationAnswer(nextA,new NavPoint[0]));
            destination.Answers.TryEnqueue(new NavigationAnswer(nextB,new NavPoint[0]));
            source.Step(1d/30);destination.Step(1d/30);
            Assert.AreEqual(1,destination.UnreachableResults);Assert.AreEqual(source.Crowd.Units[0].Outcome,destination.Crowd.Units[0].Outcome);
        }
        [Test] public void StopRejectsLateResult(){var s=Session();s.Move(0,new NavPoint(5,0));NavigationRequest r;s.Requests.TryDequeue(out r);s.Stop(0,true);s.Answers.TryEnqueue(new NavigationAnswer(r,new[]{new NavPoint(5,0)}));s.Step(1d/30);Assert.AreEqual(1,s.RejectedResults);Assert.AreEqual(0,s.Crowd.Units[0].Position.X);Assert.IsTrue(s.Crowd.Units[0].Held);}
        [Test] public void GeometryRejectsLateResult(){var s=Session();s.Move(0,new NavPoint(5,0));NavigationRequest r;s.Requests.TryDequeue(out r);s.ChangeGeometry(new NavGeometry(20,new[]{new NavObstacle(2,-2,3,2)},2));s.Answers.TryEnqueue(new NavigationAnswer(r,new[]{new NavPoint(5,0)}));s.Step(1d/30);Assert.AreEqual(1,s.RejectedResults);Assert.IsFalse(s.Crowd.Units[0].Moving);}
        [Test] public void NewOrderAndSessionRejectOldResult(){var s=Session();s.Move(0,new NavPoint(5,0));NavigationRequest r;s.Requests.TryDequeue(out r);s.Move(0,new NavPoint(-5,0));s.Answers.TryEnqueue(new NavigationAnswer(r,new[]{new NavPoint(5,0)}));s.ApplyResults();Assert.AreEqual(1,s.RejectedResults);var other=Session();EnqueueDestinationOwnedStale(other,r);other.ApplyResults();Assert.AreEqual(1,other.RejectedResults);}
        [Test] public void ValidRouteUsesDomainMotion(){var s=Session();s.Move(0,new NavPoint(5,0));NavigationRequest r;s.Requests.TryDequeue(out r);s.Answers.TryEnqueue(new NavigationAnswer(r,new[]{new NavPoint(5,0)}));s.Step(1d/30);Assert.AreEqual(1,s.AppliedResults);Assert.Greater(s.Crowd.Units[0].Position.X,0);}
        [Test] public void PartialResultCannotBecomeArrival(){var s=Session();s.Move(0,new NavPoint(5,0));NavigationRequest r;s.Requests.TryDequeue(out r);s.Answers.TryEnqueue(new NavigationAnswer(r,new[]{new NavPoint(2,0)}));s.Step(1d/30);Assert.AreEqual(1,s.RejectedResults);Assert.AreEqual(NavigationOutcome.Rejected,s.Crowd.Units[0].Outcome);}
        [Test] public void InFlightGroupsReserveDistinctArrivalSlotsAndStopReleasesThem()
        {
            var profile=new NavigationProfile();var s=new NavigationSession(1,new NavGeometry(50,new NavObstacle[0],1),profile);
            s.Crowd.Add(0,new NavPoint(-24,-4));s.Crowd.Add(1,new NavPoint(-24,4));
            var first=s.AllocateArrivalSlots(new NavPoint(24,0),new[]{0});Assert.IsTrue(s.Move(0,first[0]));
            var second=s.AllocateArrivalSlots(new NavPoint(24,0),new[]{1});
            double dx=first[0].X-second[0].X,dz=first[0].Z-second[0].Z;Assert.GreaterOrEqual(dx*dx+dz*dz,profile.ArrivalSlotSpacing*profile.ArrivalSlotSpacing);
            s.Stop(0,false);var after=s.AllocateArrivalSlots(new NavPoint(24,0),new[]{1});Assert.AreEqual(first[0].X,after[0].X);Assert.AreEqual(first[0].Z,after[0].Z);
        }
    }
}

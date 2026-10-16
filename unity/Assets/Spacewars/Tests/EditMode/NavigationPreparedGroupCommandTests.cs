using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationPreparedGroupCommandTests
    {
        private sealed class EqualIdentityKey
        {
            public override bool Equals(object other)=>other is EqualIdentityKey;
            public override int GetHashCode()=>1;
        }
        [Test] public void ArrivalSlotsPreserveReferenceIdentityEmptyCapacityAndFreeOrder()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Arrival,1,1,out var scope));
            NavigationCommandSlots<EqualIdentityKey,long> empty=null,source=null,copy=null,wrong=null,restored=null,concrete=null;
            try{
                var r=scope.OwnedReservation();empty=NavigationCommandSlots<EqualIdentityKey,long>.CreateEmptyOwned(r,true);
                var first=new EqualIdentityKey();var second=new EqualIdentityKey();var third=new EqualIdentityKey();
                Assert.That(empty.Capacity,Is.Zero);Assert.That(empty.Count,Is.Zero);Assert.False(empty.ContainsKey(first));Assert.False(empty.TryGetValue(first,out _));Assert.False(empty.Remove(first));
                Assert.Throws<InvalidOperationException>(()=>empty.AddPreparing(first,1));Assert.That(empty.CaptureLayout(),Is.EqualTo(new[]{0}));
                source=NavigationCommandSlots<EqualIdentityKey,long>.CreateOwned(3,r,true);source.CopyLayoutFrom(empty,null);source.AddPreparing(first,11);source.AddPreparing(second,22);source.AddPreparing(third,33);
                Assert.That(source.Count,Is.EqualTo(3));Assert.That(source[first],Is.EqualTo(11));Assert.That(source[second],Is.EqualTo(22));
                Assert.True(source.Remove(first));Assert.True(source.Remove(third));Assert.That(source.CaptureLayout(),Is.EqualTo(new[]{3,2,0}));
                copy=NavigationCommandSlots<EqualIdentityKey,long>.CreateOwned(3,r,true);copy.CopyLayoutFrom(source,null);Assert.That(copy.CaptureLayout(),Is.EqualTo(source.CaptureLayout()));Assert.That(copy[second],Is.EqualTo(22));
                copy.AddPreparing(first,44);Assert.True(copy.TryReadSlot(2,out var reused,out var value));Assert.That(reused,Is.SameAs(first));Assert.That(value,Is.EqualTo(44));Assert.That(source.Count,Is.EqualTo(1));
                restored=NavigationCommandSlots<EqualIdentityKey,long>.CreateOwned(3,r,true);restored.InitializeLayoutPreparing(source.CaptureLayout());Assert.True(restored.IsPreparedHole(0));Assert.True(restored.IsPreparedHole(2));restored.AddAtPreparedSlot(1,second,22);restored.Seal();Assert.That(restored.CaptureLayout(),Is.EqualTo(source.CaptureLayout()));restored.Add(first,55);Assert.True(restored.TryReadSlot(2,out var key,out long restoredValue));Assert.That(key,Is.SameAs(first));Assert.That(restoredValue,Is.EqualTo(55));
                concrete=NavigationCommandSlots<EqualIdentityKey,long>.CreateOwned(5,r,true);concrete.CopyPreparedFrom(source);concrete.Seal();Assert.That(concrete.CaptureLayout(),Is.EqualTo(source.CaptureLayout()));Assert.That(concrete[second],Is.EqualTo(22));
                wrong=NavigationCommandSlots<EqualIdentityKey,long>.CreateOwned(3,r);Assert.Throws<ArgumentException>(()=>wrong.CopyLayoutFrom(source,null));Assert.That(wrong.Count,Is.Zero);
                long bytes=budget.PhysicalBytes;budget.AllocationAdmission=_=>false;Assert.Throws<InvalidOperationException>(()=>NavigationCommandSlots<EqualIdentityKey,long>.CreateEmptyOwned(r,true));Assert.That(budget.PhysicalBytes,Is.EqualTo(bytes));
            }finally{budget.AllocationAdmission=null;concrete?.ReleaseStorage();restored?.ReleaseStorage();wrong?.ReleaseStorage();copy?.ReleaseStorage();source?.ReleaseStorage();empty?.ReleaseStorage();scope.Discard();Assert.That(budget.PhysicalBytes,Is.Zero);Assert.That(budget.HeldBytes,Is.Zero);Assert.That(budget.ActiveScopes,Is.Zero);}
        }
        private static NavigationSession Create(){var s=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);s.Crowd.Add(1,new NavPoint(-5,-2));s.Crowd.Add(2,new NavPoint(-5,2));return s;}
        private static bool Issue(NavigationSession s)=>s.MoveGroup(new[]{1,2},new[]{new NavPoint(5,-2),new NavPoint(5,2)});
        private static void Destroy(NavigationSession s){s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.That(s.PhysicalHostBudget.PhysicalBytes,Is.Zero);Assert.That(s.PhysicalHostBudget.HeldBytes,Is.Zero);Assert.That(s.PhysicalHostBudget.ActiveScopes,Is.Zero);}
        [TestCase(false)][TestCase(true)] public void FreshStopOrHoldBirthRefusesPrivatelyThenSupportsNextCommand(bool hold)
        {
            var s=Create();try{
                for(int id=3;id<=5;id++)s.Crowd.Add(id,new NavPoint(-8,(id-4)*3));var ids=new[]{1,2,3,4,5};
                byte[] Bytes(){using(var state=s.CaptureOwnedState())return WorldWire.Pack(writer=>WorldWire.Write(writer,state));}
                var before=Bytes();var budget=s.PhysicalHostBudget;long charge=budget.PhysicalBytes;var gate=typeof(NavigationSession).GetField("transportGate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s);int denied=0,underGate=0;
                var stop=new PlayableCommand(1,7,"player-1",hold?PlayableCommandKind.Hold:PlayableCommandKind.Stop,ids,origin:PlayableOrderOrigin.Human);s.PrepareCommandGroup(stop,4,12,1);
                try{budget.AllocationAdmission=_=>{if(System.Threading.Monitor.IsEntered(gate))underGate++;if(new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage))){denied++;return false;}return true;};Assert.False(s.TryStopGroup(ids,hold));s.FinishCommandGroup(false,ids);}finally{budget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.Zero(underGate);Assert.AreEqual(charge,budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);Assert.IsEmpty(s.GroupOrders);Assert.Zero(s.Requests.Count);CollectionAssert.AreEqual(before,Bytes());
                s.PrepareCommandGroup(stop,4,12,1);Assert.True(s.TryStopGroup(ids,hold));s.FinishCommandGroup(true,ids);Assert.AreEqual(1,s.GroupOrders.Count);foreach(int id in ids){Assert.True(s.Crowd.TryGet(id,out var actor));Assert.AreEqual(hold,actor.Held);}
                var move=new PlayableCommand(1,8,"player-1",PlayableCommandKind.Move,ids,new NavPoint(5,0),origin:PlayableOrderOrigin.Human).WithResolvedLocation(new NavLocation(new NavPoint(5,0),"flat"));s.PrepareCommandGroup(move,5,13,1);Assert.True(s.MoveGroup(ids,new[]{new NavPoint(5,-6),new NavPoint(5,-3),new NavPoint(5,0),new NavPoint(5,3),new NavPoint(5,6)}));s.FinishCommandGroup(true,ids);Assert.AreEqual(5,s.AllocationCount);Assert.Zero(s.Requests.Count);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),budget.PhysicalBytes);
            }finally{s.PhysicalHostBudget.AllocationAdmission=null;Destroy(s);}
        }
        [Test] public void DeniedOrdinaryGroupBirthKeepsEveryIngressSurfaceUnchanged()
        {
            var s=Create();try{var budget=s.PhysicalHostBudget;long bytes=budget.PhysicalBytes;var admission=s.Admission;int calls=0;budget.AllocationAdmission=_=>{calls++;return false;};
                Assert.False(Issue(s));Assert.That(calls,Is.EqualTo(1));Assert.That(s.GroupOrders,Is.Empty);Assert.That(s.Requests.Count,Is.Zero);Assert.That(s.PendingCount,Is.Zero);Assert.That(budget.PhysicalBytes,Is.EqualTo(bytes));Assert.That(s.Admission,Is.SameAs(admission));
            }finally{s.PhysicalHostBudget.AllocationAdmission=null;Destroy(s);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void SelectedPreparationRefusalPreservesOldGroupAndMailbox(int point)
        {
            int sites=0;var counter=Create();try{counter.PhysicalHostBudget.AllocationAdmission=_=>{sites++;return true;};Assert.True(Issue(counter));}finally{counter.PhysicalHostBudget.AllocationAdmission=null;Destroy(counter);}
            var s=Create();try{
                Assert.True(Issue(s));var budget=s.PhysicalHostBudget;long bytes=budget.PhysicalBytes;var admission=s.Admission;var queued=s.Requests.CopyItems();var group=s.GroupFor(1);int calls=0;
                int target=point==0?2:point==1?sites/2:sites;
                budget.AllocationAdmission=_=>++calls!=target;
                Assert.False(Issue(s));Assert.That(calls,Is.GreaterThanOrEqualTo(target));Assert.That(s.Admission,Is.SameAs(admission));Assert.That(s.Requests.CopyItems(),Is.EqualTo(queued));Assert.That(s.PendingCount,Is.EqualTo(2));Assert.That(s.GroupFor(1).GroupId,Is.EqualTo(group.GroupId));Assert.That(s.GroupFor(1).Members.Select(m=>m.Goal),Is.EqualTo(group.Members.Select(m=>m.Goal)));Assert.That(budget.PhysicalBytes,Is.EqualTo(bytes));
            }finally{s.PhysicalHostBudget.AllocationAdmission=null;Destroy(s);}
        }
        [Test] public void MetadataOnlyGroupKeepsOtherOwnedGroupAdmissible()
        {
            var s=Create();try{
                Assert.True(s.MoveGroup(new[]{1},new[]{new NavPoint(5,-2)}));
                var command=new PlayableCommand(1,8,"player-1",PlayableCommandKind.Move,new[]{2},new NavPoint(5,2),origin:PlayableOrderOrigin.Human);
                s.PrepareCommandGroup(command,5,13,1);s.FinishCommandGroup(true,new[]{2});
                Assert.AreEqual(2,s.GroupOrders.Count);
                s.SetFormationContact(1,true,14);
                Assert.True(s.GroupFor(1).Members.Single().FormationReleased);
                Assert.AreEqual(14,s.GroupFor(1).Members.Single().ContactTick);
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),s.PhysicalHostBudget.PhysicalBytes);
            }finally{Destroy(s);}
        }
        [Test] public void MetadataOnlySubsetRebindIsOwnedAndColdRestorable()
        {
            var s=Create();try{
                Assert.True(Issue(s));s.Stop(1,false);s.Stop(2,false);var old=s.GroupFor(1);
                var command=new PlayableCommand(1,old.CommandSequence,"player-1",PlayableCommandKind.Move,new[]{1,2},new NavPoint(6,0),origin:PlayableOrderOrigin.Human)
                    .WithResolvedLocation(new NavLocation(new NavPoint(6,0),"flat"));
                // Rebind the same issuance, including an intermediate subset version.
                s.PrepareCommandGroup(command,5,13,1,old.GroupId,old.OrderRevision,old.IssuedTick);s.FinishCommandGroup(true,new[]{1,2});
                Assert.AreEqual(1,s.GroupOrders.Count);Assert.AreEqual(2,s.GroupFor(1).Members.Length);
                s.SetFormationContact(1,true,14);s.SetFormationContact(2,true,14);
                using(var saved=s.CaptureState()){
                    var cold=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
                    try{cold.RestoreState(saved);Assert.AreEqual(2,cold.GroupFor(1).Members.Length);Assert.True(cold.GroupFor(2).Members.All(m=>m.FormationReleased));}finally{Destroy(cold);}
                }
            }finally{Destroy(s);}
        }
        [Test] public void AttackContextStopGrowsOwnedOrderMapAndPreservesIssuance()
        {
            var s=Create();try{
                Assert.True(Issue(s));for(int id=1;id<=5;id++){if(id>2)s.Crowd.Add(id,new NavPoint(3*(id-3)-6,8));if(id<5)s.Stop(id,false);}
                var command=new PlayableCommand(1,88,"player-1",PlayableCommandKind.Attack,new[]{5},targetId:1,origin:PlayableOrderOrigin.Human);
                s.PrepareCommandGroup(command,89,13,1);s.Stop(5,false);s.FinishCommandGroup(true,new[]{5});
                Assert.AreEqual(PlayableCommandKind.Attack,s.GroupFor(5).Kind);Assert.AreEqual(88,s.GroupFor(5).CommandSequence);
                s.SetFormationContact(1,true,14);s.SetFormationContact(5,true,14);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),s.PhysicalHostBudget.PhysicalBytes);
            }finally{Destroy(s);}
        }
        [Test] public void MetadataSemanticContactChangeStillRejectsBeforeHostRefresh()
        {
            var s=Create();try{
                Assert.True(Issue(s));var old=s.GroupFor(2);bool changed=false;NavigationAdmission contactAdmission=null;
                var command=new PlayableCommand(1,8,"player-1",PlayableCommandKind.Attack,new[]{2},targetId:99);
                s.PrepareCommandGroup(command,5,13,1);
                s.PhysicalHostBudget.AllocationAdmission=_=>{
                    if(!changed&&new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().Name=="CaptureCrowdNameReferences")){
                        changed=true;s.SetFormationContact(1,false,9);contactAdmission=s.Admission;
                    }return true;
                };
                try{var error=Assert.Throws<InvalidOperationException>(()=>s.FinishCommandGroup(true,new[]{2}));Assert.AreEqual("Group command lost its observed authority.",error.Message);}
                finally{s.PhysicalHostBudget.AllocationAdmission=null;}
                Assert.True(changed);Assert.AreSame(contactAdmission,s.Admission);Assert.AreEqual(old.GroupId,s.GroupFor(2).GroupId);Assert.AreEqual(9,s.GroupFor(1).Members.Single(m=>m.Entity==1).ContactTick);
                Assert.True((s.LastGroupGuardObservationForQa.Mismatch&NavigationSession.GroupGuardMismatch.Operation)!=0);
                Assert.Zero(s.LastGroupGuardCandidateForQa.References);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);
            }finally{s.PhysicalHostBudget.AllocationAdmission=null;s.LastGroupGuardCandidateForQa=null;Destroy(s);}
        }
        [Test] public void MetadataOnlyRefusalPreservesAdmissionAndAllowsRetry()
        {
            var s=Create();try{
                Assert.True(Issue(s));var before=s.Admission;var bytes=s.PhysicalHostBudget.PhysicalBytes;var old=s.GroupFor(1);
                var command=new PlayableCommand(1,8,"player-1",PlayableCommandKind.Move,new[]{2},new NavPoint(6,0),origin:PlayableOrderOrigin.Human);
                s.PrepareCommandGroup(command,5,13,1);s.PhysicalHostBudget.AllocationAdmission=_=>!new System.Diagnostics.StackTrace().GetFrames().Any(frame=>frame.GetMethod().DeclaringType==typeof(NavigationAdmissionStorage));
                try{Assert.Throws<InvalidOperationException>(()=>s.FinishCommandGroup(true,new[]{2}));}finally{s.PhysicalHostBudget.AllocationAdmission=null;}
                Assert.AreSame(before,s.Admission);Assert.AreEqual(bytes,s.PhysicalHostBudget.PhysicalBytes);Assert.AreEqual(old.GroupId,s.GroupFor(2).GroupId);
                s.PrepareCommandGroup(command,5,13,1);s.FinishCommandGroup(true,new[]{2});s.SetFormationContact(1,true,14);Assert.AreEqual(2,s.GroupOrders.Count);
            }finally{s.PhysicalHostBudget.AllocationAdmission=null;Destroy(s);}
        }
        [Test] public void OrdinaryGroupContactSubsetStopAndColdRestoreRemainConnected()
        {
            var s=Create();try{
                long baseline=s.PhysicalHostBudget.PhysicalBytes;Assert.True(Issue(s));Assert.That(s.PhysicalHostBudget.PhysicalBytes,Is.GreaterThan(baseline));Assert.That(s.GroupFor(1).GroupId,Is.EqualTo(s.GroupFor(2).GroupId));
                s.SetFormationContact(1,true,4);s.Stop(2,false);
                using(var saved=s.CaptureState()){
                    var cold=new NavigationSession(1,new NavGeometry(20,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
                    try{cold.RestoreState(saved);Assert.That(cold.GroupFor(1).Members[0].ContactTick,Is.EqualTo(4));Assert.That(cold.GroupFor(1).Members[1].Phase,Is.EqualTo(GroupMemberPhase.Stopped));}finally{Destroy(cold);}
                }
                Assert.True(s.MoveGroup(new[]{1},new[]{new NavPoint(6,-2)}));Assert.That(s.GroupFor(1).Members.Length,Is.EqualTo(1));Assert.That(s.GroupFor(2).Members.Length,Is.EqualTo(1));
            }finally{Destroy(s);}
        }
        [Test] public void ReentrantContactDuringPreparationCannotPublishOverNewerAuthority()
        {
            var s=Create();try{
                Assert.True(Issue(s));long group=s.GroupFor(1).GroupId;long bytes=s.PhysicalHostBudget.PhysicalBytes;bool changed=false;long contactBytes=bytes;
                s.PhysicalHostBudget.AllocationAdmission=_=>{if(!changed){changed=true;s.SetFormationContact(1,false,9);contactBytes=s.PhysicalHostBudget.PhysicalBytes;}return true;};
                Assert.False(Issue(s));Assert.That(s.GroupFor(1).GroupId,Is.EqualTo(group));Assert.That(s.GroupFor(1).Members[0].ContactTick,Is.EqualTo(9));Assert.That(s.PhysicalHostBudget.PhysicalBytes,Is.EqualTo(contactBytes));Assert.That(s.Requests.Count,Is.EqualTo(2));
            }finally{s.PhysicalHostBudget.AllocationAdmission=null;Destroy(s);}
        }
        private static void Prepare(NavigationSession s){var command=new PlayableCommand(1,7,"player-1",PlayableCommandKind.Move,new[]{1,2},new NavPoint(5,0),origin:PlayableOrderOrigin.Human).WithResolvedLocation(new NavLocation(new NavPoint(5,0),"flat"));s.PrepareCommandGroup(command,4,12,1);}
        [Test] public void CommandArrivalBirthRemainsPrivateUntilAtomicFinish()
        {
            var s=Create();try{Prepare(s);var admission=s.Admission;Assert.True(Issue(s));Assert.That(s.Admission,Is.SameAs(admission));Assert.That(s.GroupOrders,Is.Empty);Assert.That(s.PendingCount,Is.Zero);Assert.That(s.Requests.Count,Is.Zero);
                s.FinishCommandGroup(true,new[]{1,2});Assert.That(s.AllocationCount,Is.EqualTo(2));Assert.That(s.GroupOrders.Count,Is.EqualTo(1));Assert.That(s.GroupFor(1).OrderRevision,Is.EqualTo(4));Assert.That(s.GroupFor(1).CommandSequence,Is.EqualTo(7));Assert.That(s.PhysicalHostBudget.ActiveScopes,Is.Zero);
            }finally{Destroy(s);}
        }
        [Test] public void RefusedCommandFinishPreservesPreviousAdmissionAndBytes()
        {
            var s=Create();try{var admission=s.Admission;long bytes=s.PhysicalHostBudget.PhysicalBytes;Prepare(s);s.PhysicalHostBudget.AllocationAdmission=_=>false;Assert.False(Issue(s));s.FinishCommandGroup(false,new[]{1,2});Assert.That(s.Admission,Is.SameAs(admission));Assert.That(s.GroupOrders,Is.Empty);Assert.That(s.PendingCount,Is.Zero);Assert.That(s.PhysicalHostBudget.PhysicalBytes,Is.EqualTo(bytes));
            }finally{s.PhysicalHostBudget.AllocationAdmission=null;Destroy(s);}
        }
        [Test] public void DestroyDiscardsUnpublishedCommandOwnersAndScope()
        {var s=Create();Prepare(s);Assert.True(Issue(s));Assert.That(s.PhysicalHostBudget.ActiveScopes,Is.EqualTo(1));Destroy(s);}
        [Test] public void ObserverFailureCannotReleaseAlreadyPublishedOwners()
        {
            var s=Create();try{s.RouteRequested+=_=>throw new InvalidOperationException("observer test");Assert.Throws<InvalidOperationException>(()=>Issue(s));Assert.That(s.GroupOrders.Count,Is.EqualTo(1));Assert.That(s.PendingCount,Is.EqualTo(2));Assert.That(s.PhysicalHostBudget.PhysicalBytes,Is.GreaterThan(1088));}finally{Destroy(s);}
        }
    }
}

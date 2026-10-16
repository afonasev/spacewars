using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationOwnershipPrerequisiteTests
    {
        [Test] public void CachedPortalKeepsLegSelectorWhileRequestReplayKeepsItsOwnSelector()
        {
            var terrain=new MossHighlandMap();var geometry=new NavGeometry(terrain.HalfExtent,terrain.MovementBlockers.ToArray(),1);
            var source=new PartitionNavigationProvider(terrain,geometry,null);var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Projection,1,1,out var scope));
            PartitionNavigationProvider a=null,b=null;NavTypedLeg leg=null,surface=null;NavigationHostProjection projection=null;
            try{
                var r=scope.OwnedReservation();a=PartitionNavigationProvider.CopyOwned(source,r);b=PartitionNavigationProvider.CopyOwned(source,r);
                var seam=a.Seams.First(s=>s.Allowed);var point=new NavPoint((seam.From.X+seam.To.X)/2,(seam.From.Z+seam.To.Z)/2);
                var from=a.Surfaces.Single(s=>s.Id==seam.FromSurface).OwnedLocation(point);var to=a.Surfaces.Single(s=>s.Id==seam.ToSurface).OwnedLocation(point);
                leg=NavTypedLeg.CreateOwned(from,to,0,NavTypedLegKind.Portal,seam.Id,r,a);
                var requestSeam=b.Seams.First(s=>s.Id==leg.PortalId);var replay=new NavNameReference(requestSeam.StorageRoot,NavNameRole.SeamId);
                Assert.AreEqual(a.Binding,b.Binding);Assert.AreNotSame(replay.Resolve(),leg.PortalId);
                projection=NavigationHostProjection.CreateOwned(from,r);surface=NavTypedLeg.CreateOwned(from,from,0,NavTypedLegKind.Surface,null,r,a);projection.AdvanceLeg(surface,default);
                Assert.Throws<ArgumentException>(()=>projection.AdvanceLeg(leg,replay),"equal text must not bypass the leg identity guard");
                leg.RequireCanonicalProviderBinding(b,r);var canonical=leg.CanonicalPortalName(b);Assert.AreSame(leg.PortalId,canonical.Resolve());Assert.AreNotSame(replay.Owner,canonical.Owner);
                projection.AdvanceLeg(leg,canonical);projection.AdvanceReplay(to,new[]{replay});Assert.True(projection.PortalsMatch());
                var foreign=NavigationAdmission.CertifiedFlatProvider(new NavGeometry(4,Array.Empty<NavObstacle>(),1));
                Assert.Throws<ArgumentException>(()=>leg.CanonicalPortalName(foreign));
                projection.SealPrepared();Assert.AreEqual(1,projection.TypedCount);Assert.AreEqual(1,projection.ObservedCount);
            }finally{projection?.ReleaseStorage();leg?.ReleaseStorage();surface?.ReleaseStorage();b?.StorageRoot.ReleaseOwner();a?.StorageRoot.ReleaseOwner();scope.Discard();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);}
        }
        [Test] public void RemoveNeverOrderedActorsDoesNotBirthOrderSlotsAndExistingOrderStillInvalidates()
        {
            var s=new NavigationSession(1,new NavGeometry(30,Array.Empty<NavObstacle>(),1),PlayableProfile.Default.Navigation);
            try{
                for(int id=1;id<=10;id++)s.Crowd.Add(id,new NavPoint(-15+id*3,0));
                Assert.True(s.TryStopGroup(new[]{1},false));
                IDictionary<int,long> Orders()=>(IDictionary<int,long>)typeof(NavigationSession).GetField("orders",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(s);
                var orders=Orders();
                Assert.IsInstanceOf<NavigationCommandSlots<int,long>>(orders);int capacity=((NavigationCommandSlots<int,long>)orders).Capacity;long order=orders[1];
                Assert.AreEqual(1,orders.Count);
                for(int id=2;id<=10;id++){Assert.True(s.Remove(id));Assert.False(s.Crowd.TryGet(id,out _));orders=Orders();Assert.AreEqual(1,orders.Count);Assert.AreEqual(capacity,((NavigationCommandSlots<int,long>)orders).Capacity);}
                Assert.True(s.Remove(1));orders=Orders();Assert.AreEqual(order+1,orders[1]);Assert.AreEqual(1,orders.Count);Assert.IsEmpty(s.Crowd.Units);
            }finally{s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
        }
        [Test] public void OrderedSubsetDeathKeepsSurvivorMovementCaptureAndFreshRestoreConsistent()
        {
            var geometry=new NavGeometry(30,Array.Empty<NavObstacle>(),1);var p=PlayableProfile.Default;
            var s=new NavigationSession(1,geometry,p.Navigation);NavigationSession cold=null;
            try{
                s.ConfigureMarch(.5,p.Revision);s.Crowd.Add(1,new NavPoint(-10,-2));s.Crowd.Add(2,new NavPoint(-10,2));
                Assert.True(s.MoveGroup(new[]{1,2},new[]{new NavPoint(10,-2),new NavPoint(10,2)}));
                using(var host=new UnityHostRouteService()){
                    for(int i=0;i<80&&s.PendingCount>0;i++){host.Service(new PlayableRouteBinding(geometry,p,s.Admission,s.RoutePort),8);s.ApplyResults();}
                    Assert.AreEqual(2,s.AppliedResults);Assert.NotNull(s.InstalledExecutionFor(2));
                    using(var pinned=s.CaptureOwnedState()){
                        var old=WorldWire.Pack(w=>WorldWire.Write(w,pinned));
                        long charge=s.PhysicalHostBudget.PhysicalBytes;
                        s.PhysicalHostBudget.AllocationAdmission=_=>false;try{Assert.Throws<InvalidOperationException>(()=>s.Remove(1));}finally{s.PhysicalHostBudget.AllocationAdmission=null;}
                        Assert.AreEqual(charge,s.PhysicalHostBudget.PhysicalBytes);Assert.True(s.Crowd.TryGet(1,out _));Assert.AreEqual(2,s.GroupOrders.Single().Members.Length);
                        using(var unchanged=s.CaptureOwnedState())CollectionAssert.AreEqual(old,WorldWire.Pack(w=>WorldWire.Write(w,unchanged)),"refused removal keeps exact state");
                        Assert.True(s.Remove(1));s.SetFormationContact(2,true,1);s.SetFormationContact(2,false,2);s.Step(1d/30);
                        Assert.False(s.Crowd.TryGet(1,out _));Assert.True(s.Crowd.TryGet(2,out var survivor));Assert.True(survivor.Moving);
                        Assert.AreEqual(1,s.GroupOrders.Single().Members.Length);Assert.AreEqual(2,s.GroupOrders.Single().Members[0].Entity);
                        using(var current=s.CaptureOwnedState()){
                            var bytes=WorldWire.Pack(w=>WorldWire.Write(w,current));var decoded=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState);
                            cold=new NavigationSession(1,geometry,p.Navigation);cold.ConfigureMarch(.5,p.Revision);cold.RestoreState(decoded);
                            using(var restored=cold.CaptureOwnedState())CollectionAssert.AreEqual(bytes,WorldWire.Pack(w=>WorldWire.Write(w,restored)));
                        }
                        s.Step(1d/30);cold.Step(1d/30);
                        Assert.AreEqual(s.Crowd.Units.Single().Position,cold.Crowd.Units.Single().Position);
                        using(var left=s.CaptureOwnedState())using(var right=cold.CaptureOwnedState())CollectionAssert.AreEqual(WorldWire.Pack(w=>WorldWire.Write(w,left)),WorldWire.Pack(w=>WorldWire.Write(w,right)),"ordered removal exact movement suffix");
                        CollectionAssert.AreEqual(old,WorldWire.Pack(w=>WorldWire.Write(w,pinned)),"old immutable capture stays unchanged");
                    }
                }
            }finally{foreach(var session in new[]{cold,s})if(session!=null){session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}}
        }
        [TestCase(false)][TestCase(true)]
        public void OrdinaryHostScanOwnsStaleCancellationBeforeAndAfterSchedulerReady(bool complete)
        {
            var geometry=new NavGeometry(4,Array.Empty<NavObstacle>(),1);var profile=PlayableProfile.Default;
            var session=new NavigationSession(1,geometry,profile.Navigation);var kernel=new CertifiedRouteKernel();
            try{
                var start=new NavLocation(new NavPoint(-2,0),NavLocation.FlatSurface);var goal=new NavLocation(new NavPoint(2,0),NavLocation.FlatSurface);
                var request=new NavigationRequest(1,1,1,1,profile.Navigation,geometry,start.Position,goal.Position,heldIdentity:"stale-case",memberIdentity:new NavigationMemberIdentity(1,1,1,1,1),startLocation:start,goalLocation:goal);
                var provider=NavigationAdmission.CertifiedFlatProvider(geometry);
                var subscription=new NavRouteSubscription(1,1,1,1,0,1,1,1,1,1,1,start,goal,"stale-case");
                var recipe=new NavScheduledRequest(provider,new NavClearanceProfile("ground",1,"ground",1,profile.Navigation.Radius),new NavGraphProfile(NavigationAdmission.ExactProfileBinding(profile.Navigation),"distance",1,profile.Navigation.GridCell),new NavGraphCompileLimits(2000,2000,10000),new NavTerminalRegion("goal",1,goal,new[]{goal}),subscription,2000);
                Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Admission,1,0,out var birth));
                using(birth){var root=NavigationHostRoot.CreateOwned(birth.OwnedReservation());try{root.RegisterOrdinary(request,recipe,NavLocation.FlatSurface,birth.OwnedReservation());root.SealPrepared();birth.Attach(root);session.PublishPhysicalHostRoot(birth,null,root,()=>{});}finally{root.ReleaseStorage();}}
                var binding=new PlayableRouteBinding(geometry,profile,session.Admission,session.RoutePort);
                // This request is intentionally no longer in authority admission.
                // Advance the actual host solver before the next host scan.
                for(int i=0;i<(complete?512:1);i++){
                    kernel.AdvancePhysicalOrdinary(binding,complete?128:1);
                    var check=session.PinCommittedHostRoot();try{if(check.Scheduler.ReadyCount>0)break;}finally{check.Release();}
                }
                var source=session.PinCommittedHostRoot();try{
                    Assert.AreEqual(1,source.Scheduler.PendingCount+source.Scheduler.ReadyCount,"solver must not silently cancel the host row");
                    Assert.AreEqual(complete?1:0,source.Scheduler.ReadyCount);
                    Assert.True(session.TryOpenPhysicalHostScope(NavigationPreparationKind.Solve,source.Epoch,session.Admission.PublicationIdentity,out var cancel));
                    using(cancel){var next=source.ForkOwned(cancel.OwnedReservation());try{
                        Assert.Throws<ArgumentException>(()=>next.Scheduler.CancelRetained(subscription),"same-key public subscription cannot replace the exact retained owner");
                        Assert.AreEqual(1,next.Scheduler.PendingCount+next.Scheduler.ReadyCount);
                        next.StageScan(false,cancel.OwnedReservation());Assert.Zero(next.Scheduler.PendingCount);Assert.Zero(next.Scheduler.ReadyCount);Assert.AreEqual(1,next.Completion.Count);
                        var row=(NavigationHostRow)next.Rows[next.Completion.First];Assert.AreEqual(NavSolveStatus.Stale,row.Answer.Status);
                        Assert.AreEqual(1,source.Scheduler.PendingCount+source.Scheduler.ReadyCount,"old physical root remains pinned and unchanged");
                    }finally{next.ReleaseStorage();}}
                }finally{source.Release();}
            }finally{kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);}
        }
    }
}

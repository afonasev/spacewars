using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationStopMailboxRaceTests
    {
        static byte[] Bytes(NavigationSession session)
        {using(var state=session.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
        static void Destroy(NavigationSession session,CertifiedRouteKernel kernel)
        {
            session.BeforeStopMailboxSnapshotForQa=null;kernel.Clear();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();
            Assert.Zero(session.PhysicalHostBudget.PhysicalBytes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);Assert.Zero(session.PhysicalHostBudget.ActiveScopes);
        }
        [Test] public void ChangedRevisionRefusesEvenWhenCountMatchesAndFreshCopyPreservesOrder()
        {
            var mailbox=new NavMailbox<int>(3);Assert.True(mailbox.TryEnqueue(1));Assert.True(mailbox.TryEnqueue(2));
            mailbox.ObserveState(out long old,out int count);Assert.True(mailbox.TryDequeue(out int first));Assert.AreEqual(1,first);Assert.True(mailbox.TryEnqueue(3));Assert.AreEqual(count,mailbox.Count);
            Assert.False(mailbox.TryCopyItemsAtRevision(old,out var stale));Assert.IsNull(stale);
            Assert.Throws<InvalidOperationException>(()=>mailbox.CopyItemsAtRevision(old),"Strict callers retain their existing invariant contract");
            mailbox.ObserveState(out long current,out _);Assert.True(mailbox.TryCopyItemsAtRevision(current,out var copy));CollectionAssert.AreEqual(new[]{2,3},copy);
            Assert.AreEqual(current,mailbox.Revision);copy[0]=-1;Assert.True(mailbox.TryDequeue(out int second));Assert.AreEqual(2,second);
        }
        [Test] public void ClosedAndReentrantMailboxFaultsAreNeverConvertedToRefusal()
        {
            var mailbox=new NavMailbox<int>(2);Assert.True(mailbox.TryEnqueue(1));long revision=mailbox.Revision;
            Assert.Throws<InvalidOperationException>(()=>mailbox.TryTransfer(item=>mailbox.TryCopyItemsAtRevision(revision,out _),out _));
            Assert.AreEqual(revision,mailbox.Revision);CollectionAssert.AreEqual(new[]{1},mailbox.CopyItems());
            mailbox.CloseNoThrow();Assert.Throws<ObjectDisposedException>(()=>mailbox.TryCopyItemsAtRevision(revision,out _));
        }
        [TestCase(false)][TestCase(true)]
        public void StopAndRemovalPreserveUnrelatedInvariantIdentity(bool remove)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var kernel=new CertifiedRouteKernel();
            var expected=new InvalidOperationException("unexpected Stop invariant fixture");
            try{
                session.Crowd.Add(1,new NavPoint(-5,-3));Assert.True(session.Move(1,new NavPoint(5,-3)));var before=Bytes(session);
                session.BeforeStopMailboxSnapshotForQa=()=>throw expected;
                var actual=Assert.Throws<InvalidOperationException>(()=>{if(remove)session.Remove(1);else session.Stop(1,false);});Assert.AreSame(expected,actual);
                session.BeforeStopMailboxSnapshotForQa=null;CollectionAssert.AreEqual(before,Bytes(session));Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);
            }finally{Destroy(session,kernel);}
        }
        [TestCase(false)][TestCase(true)]
        public void ActualWorkerTransferRefusesStaleStopThenFreshStopAndColdSuffixSucceed(bool hold)
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var session=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var kernel=new CertifiedRouteKernel();
            Thread worker=null;Exception workerError=null;byte[] afterTransfer=null;int calls=0;
            PlayableRouteBinding Binding()=>new PlayableRouteBinding(geometry,PlayableProfile.Default,session.Admission,session.RoutePort);
            try
            {
                session.Crowd.Add(1,new NavPoint(-5,-3));session.Crowd.Add(2,new NavPoint(-5,3));
                Assert.True(session.MoveGroup(new[]{1,2},new[]{new NavPoint(5,-3),new NavPoint(5,3)}));Assert.AreEqual(2,session.Requests.Count);
                session.BeforeStopMailboxSnapshotForQa=()=>{
                    session.BeforeStopMailboxSnapshotForQa=null;calls++;long observed=session.Requests.Revision;
                    worker=new Thread(()=>{try{kernel.Pump(Binding(),4);}catch(Exception error){workerError=error;}}){IsBackground=true};
                    worker.Start();Assert.True(worker.Join(10000),"Actual host transfer must complete outside the Stop transport gate");Assert.IsNull(workerError);
                    Assert.Greater(session.Requests.Revision,observed);Assert.Less(session.Requests.Count,2);afterTransfer=Bytes(session);
                };
                // The public path used by CombatStop must survive the ordinary refusal.
                Assert.DoesNotThrow(()=>session.Stop(1,hold));Assert.AreEqual(1,calls);Assert.NotNull(afterTransfer);
                CollectionAssert.AreEqual(afterTransfer,Bytes(session),"Stale Stop cannot overwrite actual transferred host/neighbor state");
                Assert.True(session.Crowd.TryGet(1,out var actor));Assert.False(actor.Held);Assert.True(session.IsPending(1));
                Assert.Zero(session.PhysicalHostBudget.ActiveScopes);Assert.Zero(session.PhysicalHostBudget.HeldBytes);
                Assert.True(session.TryStopGroup(new[]{1},hold));Assert.False(session.IsPending(1));Assert.AreEqual(hold,actor.Held);Assert.False(actor.Moving);
                Assert.True(session.Crowd.TryGet(2,out _));Assert.AreEqual(2,session.GroupOrders.SelectMany(g=>g.Members).Select(m=>m.Entity).Distinct().Count());
                var bytes=Bytes(session);var cold=new NavigationSession(1,geometry,PlayableProfile.Default.Navigation);var coldKernel=new CertifiedRouteKernel();
                try{
                    cold.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));CollectionAssert.AreEqual(bytes,Bytes(cold));
                    for(int tick=0;tick<30;tick++){kernel.Pump(Binding(),4);session.Step(1d/30);coldKernel.Pump(new PlayableRouteBinding(geometry,PlayableProfile.Default,cold.Admission,cold.RoutePort),4);cold.Step(1d/30);}
                    CollectionAssert.AreEqual(Bytes(session),Bytes(cold),"Exact current-format continuation after refused then accepted Stop");
                    Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),session.PhysicalHostBudget.PhysicalBytes);
                }finally{Destroy(cold,coldKernel);}
            }
            finally{if(worker?.IsAlive==true)worker.Join(10000);Destroy(session,kernel);}
        }
    }
}

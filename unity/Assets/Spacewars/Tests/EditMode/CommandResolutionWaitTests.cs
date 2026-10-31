using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CommandResolutionWaitTests
    {
        private sealed class Birth : NavOwnedStorage
        {
            private readonly Action release;
            internal Birth(Action release):base(128){this.release=release;}
            protected override void ReleaseEdges()=>release();
        }
        private static NavigationRetentionBudget.Scope OpenBirth(NavigationRetentionBudget b,Action release)
        {
            Assert.True(b.TryOpenScope(NavigationPreparationKind.Capture,71,0,out var scope));
            using(var a=new NavStorageAllocation(scope.TryReserve)){a.Reserve(128);a.Register(new Birth(release));}
            return scope;
        }
        [Test] public void CommandAndWorkerWakeRemainSeparateAndAdmissionStaysClosedUntilLastBirth()
        {
            var b=new NavigationRetentionBudget();using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            var scope=OpenBirth(b,()=>{entered.Set();Assert.True(release.Wait(10000));});scope.RelinquishOwner();Exception error=null;int command=0,worker=0;
            var resolver=new Thread(()=>{try{scope.AcquireOwner();scope.Discard();}catch(Exception e){error=e;}}){IsBackground=true};resolver.Start();
            try{
                Assert.True(entered.Wait(10000));
                Action commandWake=()=>{Assert.False(Monitor.IsEntered(typeof(NavigationRetentionBudget).GetField("gate",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(b)));Interlocked.Increment(ref command);};
                Assert.False(b.TryOpenCommandScope(71,0,out var denied,commandWake,out bool transient));Assert.True(transient);Assert.IsNull(denied);
                Assert.False(b.TryOpenScope(NavigationPreparationKind.Solve,71,0,out _,()=>Interlocked.Increment(ref worker),out bool workerTransient));Assert.True(workerTransient);
                Assert.Zero(command);Assert.Zero(worker);Assert.True(scope.IsResolving);Assert.Greater(b.PhysicalBytes,0);
            }finally{release.Set();Assert.True(resolver.Join(10000));}
            Assert.IsNull(error);Assert.AreEqual(1,command);Assert.AreEqual(1,worker);
            Assert.True(b.TryOpenCommandScope(71,0,out var next,()=>Assert.Fail("No stale wake"),out bool laterTransient));Assert.False(laterTransient);next.Discard();
            Assert.Zero(b.ActiveScopes);Assert.Zero(b.HeldBytes);Assert.Zero(b.PhysicalBytes);
        }
        [Test] public void BothWakeFaultsPreserveFirstFailureAfterCommittedResolution()
        {
            var b=new NavigationRetentionBudget();using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            var scope=OpenBirth(b,()=>{entered.Set();Assert.True(release.Wait(10000));});scope.RelinquishOwner();
            var primary=new InvalidOperationException("worker wake fault");var secondary=new InvalidOperationException("command wake fault");Exception actual=null;int commands=0;
            var resolver=new Thread(()=>{try{scope.AcquireOwner();scope.Discard();}catch(Exception e){actual=e;}}){IsBackground=true};resolver.Start();
            try{
                Assert.True(entered.Wait(10000));Assert.False(b.TryOpenCommandScope(71,0,out _,()=>{commands++;throw secondary;},out bool transient));Assert.True(transient);
                Assert.False(b.TryOpenScope(NavigationPreparationKind.Solve,71,0,out _,()=>throw primary));
            }finally{release.Set();Assert.True(resolver.Join(10000));}
            Assert.AreSame(primary,actual);Assert.AreEqual(1,commands);CollectionAssert.Contains((System.Collections.IList)actual.Data["TacticalSecondaryFailures"],secondary);
            Assert.True(scope.IsResolved);Assert.True(b.TryOpenScope(NavigationPreparationKind.Admission,71,0,out var next));next.Discard();Assert.Zero(b.PhysicalBytes);Assert.Zero(b.HeldBytes);Assert.Zero(b.ActiveScopes);
        }
        [Test] public void SelfOwnerAndPhysicalReleaseFaultNeverBecomeCommandWaits()
        {
            var b=new NavigationRetentionBudget();int calls=0;
            var scope=OpenBirth(b,()=>{Assert.False(b.TryOpenCommandScope(71,0,out _,()=>calls++,out bool transient));Assert.False(transient,"Never wait for this thread's own release");});
            scope.Discard();Assert.Zero(calls);Assert.Zero(b.PhysicalBytes);
            var fault=new InvalidOperationException("physical release fault");scope=OpenBirth(b,()=>throw fault);
            Assert.AreSame(fault,Assert.Throws<InvalidOperationException>(()=>scope.Discard()));
            Assert.False(b.TryOpenCommandScope(71,0,out _,()=>calls++,out bool faultTransient));Assert.False(faultTransient,"A faulted ledger remains closed, not transient");
            Assert.Zero(calls);Assert.True(scope.IsResolving);Assert.Greater(b.HeldBytes,0);
        }
        [Test] public void RegisteredCommandGetsTerminalNotificationOnForeignReleaseFaultWithoutWorkerAvailability()
        {
            var b=new NavigationRetentionBudget();using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            var fault=new InvalidOperationException("foreign physical release fault");var notifyFault=new InvalidOperationException("terminal notification fault");
            var scope=OpenBirth(b,()=>{entered.Set();Assert.True(release.Wait(10000));throw fault;});scope.RelinquishOwner();Exception actual=null;int commands=0,workers=0;
            var resolver=new Thread(()=>{try{scope.AcquireOwner();scope.Discard();}catch(Exception e){actual=e;}}){IsBackground=true};resolver.Start();
            try{
                Assert.True(entered.Wait(10000));Assert.False(b.TryOpenCommandScope(71,0,out _,()=>{commands++;throw notifyFault;},out bool transient));Assert.True(transient);
                Assert.False(b.TryOpenScope(NavigationPreparationKind.Solve,71,0,out _,()=>workers++));
            }finally{release.Set();Assert.True(resolver.Join(10000));}
            Assert.AreSame(fault,actual);CollectionAssert.Contains((System.Collections.IList)actual.Data["TacticalSecondaryFailures"],notifyFault);Assert.AreEqual(1,commands);Assert.Zero(workers);
            Assert.False(b.TryOpenCommandScope(71,0,out _,()=>commands++,out bool afterFault));Assert.False(afterFault);Assert.True(scope.IsResolving);Assert.Greater(b.HeldBytes,0);
        }
        [Test] public void AlreadyWaitingOrdinaryRuntimeRefusesForeignResolverFaultWithoutStopOrRouteFailure()
        {
            var runtime=new PlayableRuntime(OfflineParticipantAuthorityTests.Config(2,false),71);var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            var b=domain.Navigation.PhysicalHostBudget;using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var fault=new InvalidOperationException("foreign capture resolver failed");Exception actual=null;Thread resolver=null;var receipts=new List<PlayableCommandReceipt>();
            try{
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,10000));
                resolver=new Thread(()=>{
                    try{
                        NavigationRetentionBudget.Scope scope=null;Assert.True(SpinWait.SpinUntil(()=>b.TryOpenScope(NavigationPreparationKind.Capture,71,0,out scope),10000));
                        using(var a=new NavStorageAllocation(scope.TryReserve)){a.Reserve(128);a.Register(new Birth(()=>{entered.Set();Assert.True(release.Wait(10000));throw fault;}));}
                        scope.Discard();
                    }catch(Exception e){actual=e;entered.Set();}
                }){IsBackground=true};resolver.Start();Assert.True(entered.Wait(10000));Assert.IsNull(actual);
                string owner=runtime.Latest.Participants[0].Id;var view=runtime.ParticipantView(owner);var actor=view.Entities.First(e=>e.Owner==view.Owner);
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(71,100,owner,PlayableCommandKind.Move,new[]{actor.Id},actor.Position)).Status);
                Assert.True(SpinWait.SpinUntil(()=>runtime.WaitingForCommandResolution||runtime.IsStopped,10000));Assert.True(runtime.WaitingForCommandResolution);
                Assert.IsNull(typeof(PlayableRuntime).GetField("routeFailure",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime));
                release.Set();Assert.True(resolver.Join(10000));Assert.AreSame(fault,actual);
                Assert.True(SpinWait.SpinUntil(()=>{receipts.AddRange(runtime.DrainReceipts());return receipts.Count>0;},10000),"Foreign release fault must wake the already waiting command without Stop or routeFailure");
                Assert.AreEqual(1,receipts.Count);Assert.AreEqual(100,receipts[0].Sequence);Assert.AreEqual(PlayableCommandStatus.Overflow,receipts[0].Status);Assert.False(runtime.WaitingForCommandResolution);
                Assert.False(b.TryOpenScope(NavigationPreparationKind.Admission,71,0,out _),"Poisoned ledger remains closed");
            }finally{
                release.Set();if(resolver!=null)Assert.True(resolver.Join(10000));runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,10000));runtime.Dispose();domain.DestroyOwnedNavigation();
            }
        }
        [Test] public void InjectedPermanentAdmissionRefusalDoesNotRegisterCommandWake()
        {
            var b=new NavigationRetentionBudget();int calls=0;b.AllocationAdmission=_=>false;
            Assert.False(b.TryOpenCommandScope(71,0,out _,()=>calls++,out bool transient));Assert.False(transient);Assert.Zero(calls);
            b.AllocationAdmission=null;Assert.True(b.TryOpenScope(NavigationPreparationKind.Admission,71,0,out var next));next.Discard();Assert.Zero(calls);Assert.Zero(b.ActiveScopes);
        }
        [Test] public void OrdinaryRuntimeSettlesMovementScopeWithExistingDomainPayloadAndPinnedSource()
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);var runtime=new PlayableRuntime(config,71);var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            var nav=domain.Navigation;var b=nav.PhysicalHostBudget;using var begin=new ManualResetEventSlim();using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();Thread resolver=null;Exception error=null;var receipts=new List<PlayableCommandReceipt>();int hits=0;
            try{
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,10000));
                resolver=new Thread(()=>{
                    NavigationRetentionBudget.Scope scope=null;
                    try{
                        Assert.True(begin.Wait(10000));Assert.True(b.TryOpenScope(NavigationPreparationKind.Capture,71,0,out scope));
                        using(var a=new NavStorageAllocation(scope.TryReserve)){a.Reserve(128);a.Register(new Birth(()=>{entered.Set();Assert.True(release.Wait(10000));}));}
                        scope.Discard();
                    }catch(Exception e){error=e;entered.Set();}finally{scope?.Dispose();}
                }){IsBackground=true};resolver.Start();
                nav.AfterMovementSourcePinsForTests=()=>{nav.AfterMovementSourcePinsForTests=null;hits++;begin.Set();Assert.True(entered.Wait(10000));};
                string owner=config.Roster[0].Id;var view=runtime.ParticipantView(owner);var actor=view.Entities.First(e=>e.Owner==view.Owner);
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(new PlayableCommand(71,100,owner,PlayableCommandKind.Move,new[]{actor.Id},actor.Position)).Status);
                Assert.True(SpinWait.SpinUntil(()=>runtime.WaitingForCommandResolution||runtime.IsStopped,10000));Assert.True(runtime.WaitingForCommandResolution,runtime.Latest.Failure);Assert.IsNull(error);
                long tick=domain.Tick;Assert.GreaterOrEqual(b.ActiveScopes,2,"Existing paid Domain payload plus resolving foreign owner must both remain live");Assert.IsEmpty(runtime.DrainReceipts());
                release.Set();Assert.True(resolver.Join(10000));Assert.IsNull(error);
                Assert.True(SpinWait.SpinUntil(()=>{receipts.AddRange(runtime.DrainReceipts());return receipts.Count>0;},10000));
                Assert.AreEqual(1,hits);Assert.AreEqual(1,receipts.Count);Assert.AreEqual(100,receipts[0].Sequence);Assert.AreEqual(PlayableCommandStatus.Applied,receipts[0].Status,receipts[0].Message);Assert.AreEqual(tick,receipts[0].AppliedTick);Assert.Zero(runtime.Latest.Metrics.Errors);
            }finally{
                nav.AfterMovementSourcePinsForTests=null;begin.Set();release.Set();if(resolver!=null)Assert.True(resolver.Join(10000));runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,10000));runtime.Dispose();domain.DestroyOwnedNavigation();
            }
            Assert.Zero(b.ActiveScopes);Assert.Zero(b.HeldBytes);Assert.Zero(b.PhysicalBytes);
        }
        [TestCase(false)][TestCase(true)]
        public void OrdinaryRuntimeSettlesOriginalCommandAtSameTickAndKeepsFifoOrCancelsOnStop(bool stop)
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);var runtime=new PlayableRuntime(config,71);var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            var b=domain.Navigation.PhysicalHostBudget;using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();Exception resolverError=null;Thread resolver=null;var receipts=new List<PlayableCommandReceipt>();
            try{
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,10000));
                resolver=new Thread(()=>{
                    NavigationRetentionBudget.Scope scope=null;
                    try{
                        Assert.True(SpinWait.SpinUntil(()=>b.TryOpenScope(NavigationPreparationKind.Capture,71,0,out scope),10000));
                        using(var a=new NavStorageAllocation(scope.TryReserve)){a.Reserve(128);a.Register(new Birth(()=>{entered.Set();Assert.True(release.Wait(10000));}));}
                        scope.Discard();
                    }catch(Exception e){resolverError=e;entered.Set();}finally{scope?.Dispose();}
                }){IsBackground=true};resolver.Start();Assert.True(entered.Wait(10000));Assert.IsNull(resolverError);
                string owner=config.Roster[0].Id;var view=runtime.ParticipantView(owner);var actor=view.Entities.First(e=>e.Owner==view.Owner);
                var first=new PlayableCommand(71,100,owner,PlayableCommandKind.Move,new[]{actor.Id},actor.Position);
                var second=new PlayableCommand(71,101,owner,PlayableCommandKind.Move,new[]{actor.Id},actor.Position);
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(first).Status);
                Assert.True(SpinWait.SpinUntil(()=>runtime.WaitingForCommandResolution||runtime.IsStopped,10000));Assert.True(runtime.WaitingForCommandResolution,runtime.Latest.Failure);
                long tick=domain.Tick;var host=domain.Navigation.PinCommittedHostRoot();long quantum=host?.QuantumSerial??0;host?.Release();
                Assert.AreEqual(PlayableCommandStatus.Accepted,runtime.TrySubmit(second).Status);Assert.IsEmpty(runtime.DrainReceipts());
                Assert.AreEqual(tick,domain.Tick,"No AI/tick advancement while original command settles");host=domain.Navigation.PinCommittedHostRoot();try{Assert.AreEqual(quantum,host?.QuantumSerial??0,"No new route grant");}finally{host?.Release();}
                if(stop){runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,10000),"Stop must not depend on foreign birth release");}
                release.Set();Assert.True(resolver.Join(10000));Assert.IsNull(resolverError);
                Assert.True(SpinWait.SpinUntil(()=>{receipts.AddRange(runtime.DrainReceipts());return receipts.Count>=2;},10000));
                CollectionAssert.AreEqual(new long[]{100,101},receipts.Select(x=>x.Sequence));
                Assert.True(receipts.All(x=>x.Status==(stop?PlayableCommandStatus.Cancelled:PlayableCommandStatus.Applied)),string.Join(";",receipts.Select(x=>x.Status+":"+x.Message)));
                Assert.AreEqual(tick,receipts[0].AppliedTick,"Same original authority turn, no command replay");
                Assert.Zero(runtime.Latest.Metrics.Errors);Assert.IsNull(domain.Navigation.AwaitCommandResolution);
            }finally{
                release.Set();if(resolver!=null)Assert.True(resolver.Join(10000));runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,10000));runtime.Dispose();domain.DestroyOwnedNavigation();
            }
            Assert.Zero(b.HeldBytes);Assert.Zero(b.ActiveScopes);Assert.Zero(b.PhysicalBytes);
        }
    }
}

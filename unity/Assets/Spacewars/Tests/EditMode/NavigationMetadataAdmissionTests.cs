using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationMetadataAdmissionTests
    {
        [TestCase("scope")][TestCase("scratch")]
        public void AllocationWaitKeepsPaidCreditWithoutHoldingSharedLedger(string kind)
        {
            var budget=new NavigationRetentionBudget();
            var gate=typeof(NavigationRetentionBudget).GetField("gate",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(budget);
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
            Exception failure=null;long during=0;int active=0;int authorityThread=Thread.CurrentThread.ManagedThreadId;
            budget.BeforeMetadataAllocationForTests=at=>{if(at!=kind||Thread.CurrentThread.ManagedThreadId==authorityThread)return;Assert.False(Monitor.IsEntered(gate));entered.Set();Assert.True(release.Wait(10000));};
            var worker=new Thread(()=>{try{
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var scope));
                using(scope)if(kind=="scratch"){
                    Assert.True(scope.TryReserve(NavStorageScratch.LeaseBytes));using(scope.ClaimScratch(NavStorageScratch.LeaseBytes)){}
                }
            }catch(Exception error){failure=error;}}){IsBackground=true};worker.Start();
            try{
                Assert.True(entered.Wait(10000));
                bool acquired=Monitor.TryEnter(gate,1000);
                try{Assert.True(acquired,"An allocation stall must not block authority ledger access");during=budget.HeldBytes;active=budget.ActiveScopes;
                    Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,2,0,out var other));other.Discard();
                }finally{if(acquired)Monitor.Exit(gate);}
            }finally{release.Set();Assert.True(worker.Join(10000));}
            Assert.IsNull(failure);Assert.AreEqual(1,active);
            Assert.AreEqual(NavigationRetentionBudget.ScopeBytes+(kind=="scratch"?NavStorageScratch.LeaseBytes:0),during);
            Assert.Zero(budget.ActiveScopes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.PhysicalBytes);
        }
        [Test] public void FailedScopeAllocationReturnsCreditAndDoesNotReuseToken()
        {
            var budget=new NavigationRetentionBudget();budget.BeforeMetadataAllocationForTests=_=>throw new OutOfMemoryException("injected metadata refusal");
            Assert.Throws<OutOfMemoryException>(()=>budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out _));
            Assert.Zero(budget.ActiveScopes);Assert.Zero(budget.HeldBytes);Assert.AreEqual(1,budget.Sequence);
            budget.BeforeMetadataAllocationForTests=null;Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var scope));
            Assert.AreEqual(2,scope.Token);scope.Discard();Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test] public void FailedScratchAllocationLeavesOriginalReservationUsable()
        {
            var budget=new NavigationRetentionBudget();Assert.True(budget.TryOpenScope(NavigationPreparationKind.Solve,1,0,out var scope));
            using(scope){Assert.True(scope.TryReserve(NavStorageScratch.LeaseBytes));long held=budget.HeldBytes;
                budget.BeforeMetadataAllocationForTests=_=>throw new OutOfMemoryException("injected scratch refusal");
                Assert.Throws<OutOfMemoryException>(()=>scope.ClaimScratch(NavStorageScratch.LeaseBytes));Assert.AreEqual(held,budget.HeldBytes);
                budget.BeforeMetadataAllocationForTests=null;using(scope.ClaimScratch(NavStorageScratch.LeaseBytes)){}
                Assert.AreEqual(NavigationRetentionBudget.ScopeBytes,budget.HeldBytes);
            }
            Assert.Zero(budget.ActiveScopes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.PhysicalBytes);
        }
    }
}

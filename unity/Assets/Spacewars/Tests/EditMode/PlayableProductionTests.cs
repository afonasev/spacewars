using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableProductionTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;
        private PlayableProfile profile;
        private long sequence;
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        private PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        private PlayableBuildingPrivateState State(int id)=>View().Buildings.Single(b=>b.Id==id).PrivateState;
        private void Create(int capacity=100)
        {
            var data=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            // Capacity fixture leaves the original free budget after the new mandatory scout.
            data.armyCapacity=capacity+data.explorerPopulationCost;profile=PlayableProfile.Create(data);
            domain=Activator.CreateInstance(Domain,Flags,null,new object[]{profile,1L},null);sequence=0;
        }
        private int Factory(int slot=1,PlayableOwner owner=PlayableOwner.Player)
        {
            int site=owner==PlayableOwner.Player?1:2;
            Call("AddCredits",owner,10000d);
            var result=Call("BuildAt",site,slot,PlayableBuildingKind.Factory,site,owner,null);
            Assert.AreEqual(PlayableCommandStatus.Applied,result);
            Call("AdvanceFoundations");Call("AdvanceBuildings",(double)profile.FactoryBuildSeconds+1);
            return View(owner).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory&&b.SlotId==slot).Id;
        }
        private PlayableCommandStatus Send(PlayableCommandKind kind,int id,long order=0,string player="player-1")
            =>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,player,kind,new[]{id},productionOrderId:order),null);
        private void Pay(int amount=10000)=>Call("AddCredits",PlayableOwner.Player,(double)amount);
        private void Advance(double seconds=0.1)=>Call("AdvanceProduction",seconds);
        // Economic fixtures settle the new physical release explicitly; the separate
        // FactoryReleaseLifecycleTests assert its timing, protection and blocked corridor.
        private void Release(int id,bool park=false)
        {
            var nav=(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
            var ids=nav.Crowd.Units.Select(u=>u.Id).ToArray();
            for(int i=0;i<180&&View().Buildings.Single(b=>b.Id==id).LaunchPhase!=FactoryReleaseRules.Closing;i++)Advance(1d/30);
            Assert.AreEqual(FactoryReleaseRules.Closing,View().Buildings.Single(b=>b.Id==id).LaunchPhase);
            if(park)foreach(var born in nav.Crowd.Units.Where(u=>!ids.Contains(u.Id)))
            {
                var factory=View().Buildings.Single(b=>b.Id==id).Position;
                var position=(from x in Enumerable.Range(-7,15) from z in Enumerable.Range(-7,15) let point=new NavPoint(x*4,z*4) where Math.Pow(point.X-factory.X,2)+Math.Pow(point.Z-factory.Z,2)>36&&nav.Crowd.CanPlace(point,born.Radius) select point).First();
                Assert.True(nav.Crowd.CanPlace(position,born.Radius));born.Position=position;born.Location=new NavLocation(position,NavLocation.FlatSurface);
                typeof(NavCrowd).GetMethod("BuildIndex",Flags).Invoke(nav.Crowd,null);
            }
            for(int i=0;i<30&&View().Buildings.Single(b=>b.Id==id).LaunchPhase!=0;i++)Advance(1d/30);
            Assert.AreEqual(0,View().Buildings.Single(b=>b.Id==id).LaunchPhase);
        }


        [Test] public void RealBirthCapacityRefusalKeepsPaidHeadAndRetriesExactlyOnce()
        {
            Create();int factory=Factory();Pay();Send(PlayableCommandKind.QueueTank,factory);Advance();var order=State(factory).Orders.Single();
            var session=(NavigationSession)Domain.GetField("navigation",Flags).GetValue(domain);var budget=session.PhysicalHostBudget;
            int count=session.Crowd.Units.Count,credits=View().Credits;var beforeIds=session.Crowd.Units.Select(u=>u.Id).ToArray();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,1,1,out var pressure));
            try{
                budget.AllocationAdmission=bytes=>{
                    if(!new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavCrowdSpatialStorage)&&f.GetMethod().Name=="Create"))return true;
                    budget.AllocationAdmission=null;Assert.True(pressure.TryReserve(LayeredNavigationScheduler.MaxRetainedBytes-budget.RetainedBytes-bytes+1));return true;
                };
                Advance(profile.TankProductionSeconds);Advance(FactoryReleaseRules.Duration(profile,PlayableEntityKind.Tank));
                Assert.AreEqual(order.Id,State(factory).Orders.Single().Id);Assert.Zero(State(factory).Orders.Single().Remaining);
                Assert.AreEqual(credits,View().Credits);CollectionAssert.AreEqual(beforeIds,session.Crowd.Units.Select(u=>u.Id).ToArray());Assert.AreEqual(count,session.Crowd.Units.Count);
                pressure.Discard();Advance();Assert.IsEmpty(State(factory).Orders);Assert.AreEqual(count+1,session.Crowd.Units.Count);
                var born=session.Crowd.Units.Select(u=>u.Id).Except(beforeIds).Single();Advance();Assert.AreEqual(1,session.Crowd.Units.Count(u=>u.Id==born));Assert.AreEqual(count+1,session.Crowd.Units.Count);Assert.AreEqual(credits,View().Credits);
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(session),budget.PhysicalBytes);
            }finally{budget.AllocationAdmission=null;pressure.Discard();session.ReleaseCommittedHostRoot();session.DestroyNavigationAuthority();Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);}
        }
        [Test] public void SixPaidSlotsRemainWaitingAtCapacityAndCancelRefundsOnce()
        {
            Create(1);int f=Factory();Pay();int before=View().Credits;
            for(int i=0;i<6;i++)Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueTank,f));
            Assert.AreEqual(PlayableCommandStatus.Overflow,Send(PlayableCommandKind.QueueTank,f));
            Advance();var frozen=State(f);Assert.AreEqual(6,frozen.Orders.Count);
            Assert.True(frozen.Orders.All(o=>!o.Active&&o.Remaining==o.Duration));
            Assert.Zero(View().Population.Reserved);Assert.AreEqual(before-6*profile.TankCreditCost,View().Credits);
            long cancelled=frozen.Orders[2].Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.CancelProductionOrder,f,cancelled));
            Assert.AreEqual(before-5*profile.TankCreditCost,View().Credits);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.CancelProductionOrder,f,cancelled));
            Assert.AreEqual(before-5*profile.TankCreditCost,View().Credits);
            Assert.AreEqual(6,frozen.Orders.Count,"Published order collections remain immutable after cancellation.");
            Send(PlayableCommandKind.QueueTank,f);Assert.Greater(State(f).Orders.Last().Id,frozen.Orders.Last().Id);
        }

        [Test] public void GlobalEnqueueOrderWinsCapacityAndCancellationReleasesReserve()
        {
            Create(3);int a=Factory(),b=Factory(2);Pay();
            Send(PlayableCommandKind.QueueTank,b);Send(PlayableCommandKind.QueueTank,a);Advance();
            Assert.True(State(b).Orders[0].Active);Assert.False(State(a).Orders[0].Active);
            Assert.AreEqual(3,View().Population.Reserved);
            Send(PlayableCommandKind.CancelProductionOrder,b,State(b).Orders[0].Id);Assert.Zero(View().Population.Reserved);
            Advance();Assert.True(State(a).Orders[0].Active);Assert.AreEqual(3,View().Population.Reserved);
        }

        [Test] public void RepeatPreservesPaidQueueProgressAndDisablingKeepsPaidOrders()
        {
            Create(3);int f=Factory();Pay();int before=View().Credits;
            Send(PlayableCommandKind.QueueTank,f);Send(PlayableCommandKind.QueueTank,f);Advance();
            var paid=State(f).Orders.ToArray();int reserved=View().Population.Reserved;
            Send(PlayableCommandKind.ToggleRepeatProduction,f);Assert.True(State(f).Repeat);
            CollectionAssert.AreEqual(paid.Select(o=>o.Id),State(f).Orders.Select(o=>o.Id));
            Assert.AreEqual(paid[0].Remaining,State(f).Orders[0].Remaining);
            Assert.AreEqual(before-2*profile.TankCreditCost,View().Credits);Assert.AreEqual(reserved,View().Population.Reserved);
            Send(PlayableCommandKind.ToggleRepeatProduction,f);Assert.False(State(f).Repeat);
            CollectionAssert.AreEqual(paid.Select(o=>o.Id),State(f).Orders.Select(o=>o.Id));
            Advance(profile.TankProductionSeconds);Release(f);Assert.AreEqual(paid[1].Id,State(f).Orders.Single().Id);
            Assert.AreEqual(3+profile.ExplorerPopulationCost,View().Population.Living);Assert.Zero(View().Population.Reserved);
            Send(PlayableCommandKind.CancelProductionOrder,f,paid[1].Id);
            Send(PlayableCommandKind.ToggleRepeatProduction,f);Advance();
            Assert.True(State(f).Repeat);Assert.IsEmpty(State(f).Orders);
            Assert.AreEqual(before-profile.TankCreditCost,View().Credits,"Repeat intent does not spend while capacity is full.");
        }

        [Test] public void PaidManualHeadHasPriorityOverRepeatAndManualClickPreservesRepeat()
        {
            Create(3);int a=Factory(),b=Factory(2);Pay();
            Send(PlayableCommandKind.ToggleRepeatProduction,a);Send(PlayableCommandKind.QueueTank,b);Advance();
            Assert.IsEmpty(State(a).Orders);Assert.True(State(a).Repeat);Assert.True(State(b).Orders.Single().Active);
            Send(PlayableCommandKind.QueueTank,a);Assert.True(State(a).Repeat);Assert.False(State(a).Orders.Single().Active);
        }

        [Test] public void MixedManualOrdersFollowActiveRepeatThenSeriesResumes()
        {
            Create();int f=Factory();Pay();Send(PlayableCommandKind.ToggleRepeatProduction,f);Advance();
            var active=State(f).Orders.Single();int before=View().Credits;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueExplorer,f));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueTank,f));
            var paid=State(f).Orders.ToArray();Assert.True(State(f).Repeat);
            CollectionAssert.AreEqual(new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Tank},paid.Select(o=>o.Kind));
            Assert.AreEqual(active.Id,paid[0].Id);Assert.AreEqual(active.Remaining,paid[0].Remaining);
            Advance(active.Remaining);Release(f,park:true);CollectionAssert.AreEqual(paid.Skip(1).Select(o=>o.Id),State(f).Orders.Select(o=>o.Id));
            Assert.AreEqual(before-profile.ExplorerCreditCost-profile.TankCreditCost,View().Credits);
            Advance(profile.ExplorerProductionDurationSec);Release(f,park:true);Assert.AreEqual(paid[2].Id,State(f).Orders.Single().Id);
            Advance(profile.TankProductionSeconds);Release(f,park:true);Assert.IsEmpty(State(f).Orders);Assert.True(State(f).Repeat);
            Advance();var resumed=State(f).Orders.Single();Assert.AreEqual(PlayableEntityKind.Tank,resumed.Kind);Assert.True(resumed.Active);Assert.Greater(resumed.Id,paid[2].Id);
            Assert.AreEqual(before-profile.ExplorerCreditCost-2*profile.TankCreditCost,View().Credits);
        }

        [TestCase(false)][TestCase(true)] public void CancellationPreservesRepeatAndRefundsOnlySelectedOrder(bool active)
        {
            Create();int f=Factory();Pay();Send(PlayableCommandKind.ToggleRepeatProduction,f);Advance();
            Send(PlayableCommandKind.QueueExplorer,f);var paid=State(f).Orders.ToArray();int before=View().Credits;
            var cancelled=paid[active?0:1];var kept=paid[active?1:0];
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.CancelProductionOrder,f,cancelled.Id));
            Assert.True(State(f).Repeat);Assert.AreEqual(kept.Id,State(f).Orders.Single().Id);Assert.AreEqual(kept.Remaining,State(f).Orders.Single().Remaining);
            Assert.AreEqual(before+(int)Math.Floor(cancelled.PaidCost*profile.UnitCancellationRefundRatio),View().Credits);
            Assert.AreEqual(active?0:profile.TankPopulationCost,View().Population.Reserved);
            int after=View().Credits;Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(PlayableCommandKind.CancelProductionOrder,f,cancelled.Id));
            Assert.True(State(f).Repeat);Assert.AreEqual(after,View().Credits);Assert.AreEqual(kept.Id,State(f).Orders.Single().Id);
        }

        [Test] public void ChangingRepeatKindPreservesPaidHeadAndUsesNewKindAfterManualQueue()
        {
            Create();int f=Factory();Pay();Send(PlayableCommandKind.ToggleRepeatProduction,f);Advance();Send(PlayableCommandKind.QueueTank,f);
            var paid=State(f).Orders.ToArray();int before=View().Credits;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("ToggleRepeat",f,PlayableOwner.Player,null,PlayableEntityKind.Explorer));
            Assert.True(State(f).Repeat);Assert.AreEqual(PlayableEntityKind.Explorer,State(f).RepeatKind);
            CollectionAssert.AreEqual(paid.Select(o=>o.Id),State(f).Orders.Select(o=>o.Id));Assert.AreEqual(paid[0].Remaining,State(f).Orders[0].Remaining);Assert.AreEqual(before,View().Credits);
            Advance(profile.TankProductionSeconds);Release(f,park:true);Advance(profile.TankProductionSeconds);Release(f,park:true);Assert.IsEmpty(State(f).Orders);
            Advance();Assert.AreEqual(PlayableEntityKind.Explorer,State(f).Orders.Single().Kind);
        }

        [Test] public void FullQueueRejectsManualOrderWithoutChangingRepeatOrPaidTerms()
        {
            Create(1);int f=Factory();Pay();Send(PlayableCommandKind.ToggleRepeatProduction,f);
            for(int i=0;i<6;i++)Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueTank,f));
            var paid=State(f).Orders.ToArray();int before=View().Credits;
            Assert.AreEqual(PlayableCommandStatus.Overflow,Send(PlayableCommandKind.QueueExplorer,f));Assert.True(State(f).Repeat);
            CollectionAssert.AreEqual(paid.Select(o=>o.Id),State(f).Orders.Select(o=>o.Id));Assert.AreEqual(before,View().Credits);
            Advance();Assert.True(State(f).Orders.All(o=>!o.Active));Assert.Zero(View().Population.Reserved);
        }

        [Test] public void BlockedReadyOrderKeepsReserveAndProducerRemovalDoesNotRefund()
        {
            Create(100);int f=Factory();Pay();
            Send(PlayableCommandKind.QueueTank,f);Send(PlayableCommandKind.QueueTank,f);
            Advance(profile.TankProductionSeconds);Release(f);
            // Block every currently safe typed candidate, preserving the all-exits wait invariant.
            var all=(IDictionary)Domain.GetField("buildings",Flags).GetValue(domain);
            var nav=(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
            var candidates=(NavPoint[])Call("FactoryExitCandidates",all[f],PlayableEntityKind.Tank);
            int blocker=10000;
            foreach(var point in candidates)if(nav.Crowd.CanPlace(point,profile.TankCollisionRadius))
                nav.Crowd.Add(blocker++,point,profile.TankCollisionRadius,profile.TankSpeed,profile.TankTurnSpeed);
            Advance(profile.TankProductionSeconds);
            var head=State(f).Orders.Single();Assert.True(head.Active);Assert.Zero(head.Remaining);
            Assert.AreEqual(3+profile.ExplorerPopulationCost,View().Population.Living);Assert.AreEqual(3,View().Population.Reserved);
            Advance(100);Assert.AreEqual(head.Id,State(f).Orders.Single().Id);
            int before=View().Credits;
            Call("RemoveBuilding",all[f],false);
            Assert.AreEqual(before,View().Credits);Assert.Zero(View().Population.Reserved);
        }

        [Test] public void RepeatWithoutMoneyDoesNotCreateDebtOrPaidOrder()
        {
            Create();int f=Factory();Call("AddCredits",PlayableOwner.Player,-(double)View().Credits);
            Send(PlayableCommandKind.ToggleRepeatProduction,f);Advance();
            Assert.True(State(f).Repeat);Assert.IsEmpty(State(f).Orders);Assert.Zero(View().Credits);
            Pay(profile.TankCreditCost);Advance();Assert.AreEqual(1,State(f).Orders.Count);Assert.Zero(View().Credits);
            Assert.AreEqual(PlayableCommandStatus.InsufficientCredits,Send(PlayableCommandKind.QueueTank,f));Assert.True(State(f).Repeat);
            Assert.AreEqual(1,State(f).Orders.Count,"A failed manual purchase preserves repeat and the paid head.");
        }

        [TestCase(0)][TestCase(1001)] public void ArmyCapacityUsesMetadataRange(int invalid)
        {
            var data=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            data.armyCapacity=invalid;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="army.capacity");
            Assert.AreEqual(1,field.Minimum);Assert.AreEqual(1000,field.Maximum);Assert.AreEqual(1,field.Step);
        }

        [Test] public void ForeignOrdersAreRejectedAndEnemyProductionIsPrivate()
        {
            Create();
            // This privacy/capacity fixture explicitly retains two diagnostic defenders;
            // ordinary symmetric genesis no longer supplies the historical bonus tanks.
            domain=NativeCombatFixture.WithTwoEnemyDefenders(profile,patrol:false);
            int f=Factory(owner:PlayableOwner.Enemy);
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.QueueTank,f));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueTank,f,player:"enemy-1"));
            var enemy=View(PlayableOwner.Enemy).Buildings.Single(b=>b.Id==f).PrivateState;
            Assert.AreEqual(1,enemy.Orders.Count);
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.CancelProductionOrder,f,enemy.Orders[0].Id));
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.ToggleRepeatProduction,f));
            Assert.False(View().Buildings.Any(b=>b.Id==f));
            Assert.AreEqual(profile.ExplorerPopulationCost,View().Population.Living);Assert.AreEqual(6+profile.ExplorerPopulationCost,View(PlayableOwner.Enemy).Population.Living);
        }
    }
}

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

        [Test] public void RepeatRefundsPaidQueueWaitsForCapacityAndDisablingKeepsPaidHead()
        {
            Create(3);int f=Factory();Pay();int before=View().Credits;
            Send(PlayableCommandKind.QueueTank,f);Send(PlayableCommandKind.QueueTank,f);Advance();
            Send(PlayableCommandKind.ToggleRepeatProduction,f);Assert.True(State(f).Repeat);
            Assert.IsEmpty(State(f).Orders);Assert.AreEqual(before,View().Credits);Assert.Zero(View().Population.Reserved);
            Advance();var head=State(f).Orders.Single();Assert.True(head.Active);
            Assert.AreEqual(before-profile.TankCreditCost,View().Credits);
            Send(PlayableCommandKind.ToggleRepeatProduction,f);Assert.False(State(f).Repeat);
            Assert.AreEqual(head.Id,State(f).Orders.Single().Id);
            Advance(profile.TankProductionSeconds);Assert.IsEmpty(State(f).Orders);
            Assert.AreEqual(3+profile.ExplorerPopulationCost,View().Population.Living);Assert.Zero(View().Population.Reserved);
            Send(PlayableCommandKind.ToggleRepeatProduction,f);Advance();
            Assert.True(State(f).Repeat);Assert.IsEmpty(State(f).Orders);
            Assert.AreEqual(before-profile.TankCreditCost,View().Credits,"Repeat intent does not spend while capacity is full.");
        }

        [Test] public void PaidManualHeadHasPriorityOverRepeatAndManualClickDisablesRepeat()
        {
            Create(3);int a=Factory(),b=Factory(2);Pay();
            Send(PlayableCommandKind.ToggleRepeatProduction,a);Send(PlayableCommandKind.QueueTank,b);Advance();
            Assert.IsEmpty(State(a).Orders);Assert.True(State(a).Repeat);Assert.True(State(b).Orders.Single().Active);
            Send(PlayableCommandKind.QueueTank,a);Assert.False(State(a).Repeat);Assert.False(State(a).Orders.Single().Active);
        }

        [Test] public void BlockedReadyOrderKeepsReserveAndProducerRemovalDoesNotRefund()
        {
            Create(100);int f=Factory();Pay();
            Send(PlayableCommandKind.QueueTank,f);Send(PlayableCommandKind.QueueTank,f);
            Advance(profile.TankProductionSeconds);
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
            Send(PlayableCommandKind.QueueTank,f);Assert.False(State(f).Repeat);
            Assert.AreEqual(1,State(f).Orders.Count,"A failed manual purchase still turns repeat off, preserving the paid head.");
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
            Create();int f=Factory(owner:PlayableOwner.Enemy);
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

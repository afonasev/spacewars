using System;
using System.Linq;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableBuildingLifecycleTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;
        private PlayableProfile profile;
        private long sequence;
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        private PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        private PlayableBuildingSnapshot Building(int id)=>View().Buildings.Single(b=>b.Id==id);
        private PlayableBuildingLifecycleSnapshot State(int id)=>Building(id).PrivateState.Lifecycle;
        [SetUp] public void Create()
        {
            profile=PlayableProfile.Default;
            domain=Activator.CreateInstance(Domain,Flags,null,new object[]{profile,1L},null);sequence=0;
        }
        private int Factory(int slot=1,bool ready=true)
        {
            Call("AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            if(ready){Call("AdvanceFoundations");Call("AdvanceBuildings",(double)profile.FactoryBuildSeconds+1);}
            return View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory&&b.SlotId==slot).Id;
        }
        private PlayableCommandStatus Send(PlayableCommandKind kind,int id,string owner="player-1")
            =>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,owner,kind,new[]{id}),null);
        private void Clock(double seconds)=>Domain.GetField("elapsed",Flags).SetValue(domain,seconds);
        private double Balance()=>(double)Call("Balance",PlayableOwner.Player);
        [Test] public void VisibleBuildingCanTakeAutonomousFireDespiteItsNavigationFootprint()
        {
            // Source idle defense prioritizes visible units before buildings. Isolate the
            // building target so this fixture still proves its own footprint permits fire.
            foreach(var unit in View().Entities.Where(e=>e.Owner==PlayableOwner.Player).ToArray())Call("Damage",unit.Id,10000);
            Call("SpawnEnemy",new NavPoint(profile.PlayerHeadquartersX+profile.DefenderOffsetX,profile.HeadquartersZ+profile.DefenderOffsetZ));
            for(int i=0;i<30;i++)Call("Step",1d/30d);
            Assert.Less(Building(1).Health,profile.HeadquartersHealth,"The target's own navigation footprint must not block target acquisition.");
            Assert.IsNotNull(State(1).RepairBlockedReason,"Actual projectile damage starts the repair cooldown.");
        }
        [Test] public void NormalTickConstructionDoesNotLoseRefundToFloatingPointHealth()
        {
            int f=Factory(1,false);Call("AdvanceFoundations");
            for(int i=0;i<profile.FactoryBuildSeconds*30;i++)Call("AdvanceBuildings",1d/30d);
            Assert.AreEqual(ConstructionPhase.Ready,Building(f).Phase);
            Assert.AreEqual(225,State(f).Refund);Assert.AreEqual("Здание не повреждено",State(f).RepairBlockedReason);
            double before=Balance();Send(PlayableCommandKind.SellBuilding,f);Assert.AreEqual(before+225,Balance());
        }
        [Test] public void CompletionPreservesRealConstructionDamageAndRepairEligibility()
        {
            int f=Factory(1,false);Call("AdvanceFoundations");
            for(int i=0;i<profile.FactoryBuildSeconds*15;i++)Call("AdvanceBuildings",1d/30d);
            Call("Damage",f,1);
            for(int i=0;i<profile.FactoryBuildSeconds*15;i++)Call("AdvanceBuildings",1d/30d);
            Clock(profile.BuildingRepairCombatLockoutSec);
            Assert.AreEqual(ConstructionPhase.Ready,Building(f).Phase);Assert.Less(Building(f).Health,profile.FactoryHealth);
            Assert.Less(State(f).Refund,225);Assert.IsNull(State(f).RepairBlockedReason);
        }
        [Test] public void StartingHeadquartersUsesCanonicalBasePriceForSale()
        {
            double before=Balance();Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.SellBuilding,1));
            Assert.AreEqual(before+Math.Floor(profile.HeadquartersCreditCost*profile.BuildingSaleRefundRatio),Balance());
        }
        [Test] public void EnemyAndMemoryNeverExposeRepairOrDemolition()
        {
            Call("SpawnPlayer",new NavPoint(10,-4),new NavPoint(10,-4));
            Call("Damage",2,20);Clock(5d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.StartBuildingRepair,2,"enemy-1"));
            Assert.IsNull(View().Buildings.Single(b=>b.Id==2).PrivateState);
            Assert.True(View(PlayableOwner.Enemy).Buildings.Single(b=>b.Id==2).PrivateState.Lifecycle.Repairing);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.SellBuilding,2,"enemy-1"));
            Assert.IsNull(View().Buildings.Single(b=>b.Id==2).PrivateState);
            var memory=View().Vision.KnownBuildings.Single(b=>b.Id==2);
            Assert.False(memory.GetType().GetProperties().Any(p=>p.Name.Contains("Repair")||p.Name.Contains("Sale")));
        }
        [Test] public void LifecycleProfileRejectsEveryOutOfRangeAndNonfiniteValue()
        {
            foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.Contains("buildingSale")||f.Path.Contains("buildingRepair")||f.Path.Contains("lifecycleMarker")||f.Path.Contains("saleMarker")))
            {
                var member=typeof(PlayableProfileData).GetField(field.Path.Substring(field.Path.IndexOf('.')+1));Assert.NotNull(member,field.Path);
                foreach(double value in new[]{field.Minimum-1,field.Maximum+1,double.NaN,double.PositiveInfinity})
                {
                    var data=(PlayableProfileData)typeof(PlayableProfile).GetMethod("DefaultData",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                    member.SetValue(data,value);Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data),field.Path);
                }
            }
        }
        [Test] public void SaleRefundsOnceStopsOrdersAndRetainsCollisionUntilRemoval()
        {
            int f=Factory();Send(PlayableCommandKind.QueueTank,f);Call("AdvanceProduction",.1d);
            Assert.AreEqual(profile.TankPopulationCost,View().Population.Reserved);
            double before=Balance();var position=Building(f).Position;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.SellBuilding,f));
            Assert.AreEqual(before+Math.Floor(profile.FactoryCreditCost*profile.BuildingSaleRefundRatio),Balance());
            Assert.Zero(View().Population.Reserved);Assert.Zero(Building(f).QueueCount);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.SellBuilding,f));
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.QueueTank,f));
            Assert.False(((NavGeometry)Domain.GetProperty("Geometry",Flags).GetValue(domain)).IsFree(position,0));
            Call("AdvanceBuildingLifecycle",profile.BuildingSaleDemolitionSec-.01);
            Assert.True(State(f).Selling);Call("AdvanceBuildingLifecycle",.01d);
            Assert.False(View().Buildings.Any(b=>b.Id==f));
            Assert.True(((NavGeometry)Domain.GetProperty("Geometry",Flags).GetValue(domain)).IsFree(position,0));
        }
        [Test] public void CascadePrevalidatesChildCooldownAndLossWaitsForRemoval()
        {
            int f=Factory();Call("Damage",f,10);double before=Balance();
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.SellBuilding,1));Assert.AreEqual(before,Balance());Assert.False(State(1).Selling);
            Clock(profile.BuildingSaleCombatLockoutSec);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.SellBuilding,1));
            Assert.True(State(f).Selling);Call("CheckOutcome");Assert.AreEqual(PlayableMatchOutcome.Playing,View().Outcome);
            Call("AdvanceBuildingLifecycle",profile.BuildingSaleDemolitionSec);Call("CheckOutcome");Assert.AreEqual(PlayableMatchOutcome.PlayerLost,View().Outcome);
        }
        [Test] public void OutpostSaleLeavesOtherBaseAliveAndPreventsNewChildWork()
        {
            int homeFactory=Factory();
            var sites=(IDictionary)Domain.GetField("sites",Flags).GetValue(domain);var site=sites[3];
            site.GetType().GetField("Claimant").SetValue(site,PlayableOwner.Player);site.GetType().GetField("Progress").SetValue(site,1d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",3,0,PlayableBuildingKind.Outpost,0,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",profile.OutpostBuildSeconds+1);
            int center=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Outpost).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",3,1,PlayableBuildingKind.Factory,center,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",(double)profile.FactoryBuildSeconds+1);
            int child=View().Buildings.Single(b=>b.ParentId==center).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.SellBuilding,center));
            Assert.False(State(homeFactory).Selling);Assert.True(State(child).Selling);Assert.False(State(1).Selling);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Call("BuildAt",3,2,PlayableBuildingKind.Refinery,center,PlayableOwner.Player,null));
            Call("AdvanceBuildingLifecycle",profile.BuildingSaleDemolitionSec);Call("CheckOutcome");
            Assert.AreEqual(PlayableMatchOutcome.Playing,View().Outcome);Assert.True(View().Buildings.Any(b=>b.Id==homeFactory));
        }
        [Test] public void CascadePendingChildNeverMaterializesOrAcceptsCancellation()
        {
            int f=Factory(1,false);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.SellBuilding,1));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            Assert.AreEqual(ConstructionPhase.Pending,Building(f).Phase);
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.CancelBuilding,f));
        }
        [Test] public void RepairHalfHealthHasExactCostAndPreservesProduction()
        {
            int f=Factory();Call("Damage",f,profile.FactoryHealth/2);Clock(profile.BuildingRepairCombatLockoutSec);
            Send(PlayableCommandKind.QueueTank,f);Call("AdvanceProduction",.1d);long order=Building(f).PrivateState.Orders[0].Id;
            double before=Balance();Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.StartBuildingRepair,f));
            Assert.AreEqual(profile.BuildingRepairDurationSec*.5,State(f).RepairDuration);
            for(int i=0;i<15;i++)Call("AdvanceBuildingLifecycle",1d);
            Assert.AreEqual(profile.FactoryHealth,Building(f).Health);Assert.False(State(f).Repairing);
            Assert.AreEqual(before-profile.FactoryCreditCost*profile.BuildingRepairCostRatio*.5,Balance(),1e-8);
            Assert.AreEqual(order,Building(f).PrivateState.Orders[0].Id);
        }
        [Test] public void RepairWaitsThenPaysFractionalTerminalAndDamageCancels()
        {
            int f=Factory();Call("Damage",f,1);Clock(5d);Call("AddCredits",PlayableOwner.Player,-Balance());
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.StartBuildingRepair,f));Call("AdvanceBuildingLifecycle",1d);
            Assert.True(State(f).WaitingForCredits);Assert.AreEqual(profile.FactoryHealth-1,Building(f).Health);
            Call("AddCredits",PlayableOwner.Player,10d);Call("AdvanceBuildingLifecycle",1d);
            Assert.AreEqual(profile.FactoryHealth,Building(f).Health);Assert.AreEqual(10-profile.FactoryCreditCost*profile.BuildingRepairCostRatio/profile.FactoryHealth,Balance(),1e-9);
            Call("Damage",f,25);Clock(10d);Send(PlayableCommandKind.StartBuildingRepair,f);Call("Damage",f,0);Assert.True(State(f).Repairing);
            Call("Damage",f,1);Assert.False(State(f).Repairing);Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.StartBuildingRepair,f));
        }
        [Test] public void OwnerValidationAndRepairCancellationDoNotRefund()
        {
            int f=Factory();Call("Damage",f,50);Clock(5d);
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.SellBuilding,f,"enemy-1"));
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.StartBuildingRepair,f,"enemy-1"));
            Send(PlayableCommandKind.StartBuildingRepair,f);Call("AdvanceBuildingLifecycle",1d);double paid=Balance();int health=Building(f).Health;
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.CancelBuildingRepair,f));Call("AdvanceBuildingLifecycle",5d);
            Assert.AreEqual(paid,Balance());Assert.AreEqual(health,Building(f).Health);
        }
    }
}

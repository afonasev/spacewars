using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class RepairTowerTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly List<PlayableDomain> owned=new List<PlayableDomain>();
        object Call(PlayableDomain d,string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
        PlayableDomain New(){var d=new PlayableDomain(PlayableProfile.Default,813,false);owned.Add(d);Call(d,"AddCredits",PlayableOwner.Player,10000d);return d;}
        PlayableSnapshot View(PlayableDomain d)=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        PlayableBuildingSnapshot Tower(PlayableDomain d,int id)=>View(d).Buildings.Single(b=>b.Id==id);
        int Build(PlayableDomain d,int slot=1001){Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",1,slot,PlayableBuildingKind.RepairTower,1,PlayableOwner.Player,null));Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",100d);return View(d).Buildings.Single(b=>b.SlotId==slot).Id;}
        int Unit(PlayableDomain d,NavPoint at,int damage,PlayableEntityKind kind=PlayableEntityKind.Tank,PlayableOwner owner=PlayableOwner.Player){int id=(int)Call(d,"SpawnUnit",at,owner,kind);if(kind!=PlayableEntityKind.Hawk)d.Navigation.Crowd.Stop(id,true);if(damage>0)Call(d,"Damage",id,damage);return id;}
        void Heal(PlayableDomain d,double seconds)=>Call(d,"AdvanceRepairTowers",seconds);
        double Hp(PlayableDomain d,int id)=>View(d).Entities.Single(u=>u.Id==id).ExactHealth.Value;
        [TearDown]public void Cleanup(){foreach(var d in owned){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);}owned.Clear();}
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Hawk)]
        public void FixedRateContinuesUnderFireAndNeverOverheals(PlayableEntityKind kind)
        {
            var d=New();int tower=Build(d);var at=Tower(d,tower).Position;int target=Unit(d,new NavPoint(at.X+3,at.Z),20,kind);
            double initial=Hp(d,target);Heal(d,.25);Assert.AreEqual(initial+1,Hp(d,target),1e-9);
            Call(d,"Damage",tower,10);Call(d,"Damage",target,5);Heal(d,1);Assert.AreEqual(initial,Hp(d,target),1e-9,"+1-5+4 regardless of incoming fire");
            Heal(d,100);Assert.AreEqual(View(d).Entities.Single(u=>u.Id==target).MaxHealth,Hp(d,target));Assert.Zero(Tower(d,tower).Healing.TargetId);
        }
        [Test]public void ConcreteScalesCurrentAndFutureTowerHealthWhileWeaponResearchNeverChangesHealing()
        {
            var p=PlayableProfile.Default;var d=New();int tower=Build(d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",1,5,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",100d);
                Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",1,3,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));
            Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",100d);
            int science=View(d).Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            void Research(PlayableResearchKind kind){Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"QueueResearch",science,PlayableOwner.Player,kind,null));Call(d,"AdvanceResearch",1000d);}
            Call(d,"Damage",tower,40);var before=Tower(d,tower);double ratio=1+p.ReinforcedConcreteBonusPercent/100;
            Research(PlayableResearchKind.ReinforcedConcrete);var reinforced=Tower(d,tower);
            Assert.AreEqual(before.MaxHealth*ratio,reinforced.MaxHealth,1e-9);
            Assert.AreEqual(Math.Ceiling(before.Health*ratio),reinforced.Health,"Concrete preserves the current health fraction.");
            int future=Build(d,1002);Assert.AreEqual(reinforced.MaxHealth,Tower(d,future).MaxHealth);Assert.AreEqual(reinforced.MaxHealth,Tower(d,future).Health);
            var at=reinforced.Position;int target=Unit(d,new NavPoint(at.X+3,at.Z),80);double initial=Hp(d,target);
            Heal(d,1);Assert.AreEqual(initial+4,Hp(d,target),1e-9);
            Research(PlayableResearchKind.ScientificCenterUpgrade);
            foreach(var kind in new[]{PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.AdvancedWeaponSystems})
            {
                Research(kind);double hp=Hp(d,target);Heal(d,1);Assert.AreEqual(hp+4,Hp(d,target),1e-9,"Weapon research cannot change the repair rate, including overlapping tower coverage.");
                Assert.IsNull(Tower(d,tower).Turret,"Repair tower never acquires a weapon.");Assert.AreEqual(reinforced.MaxHealth,Tower(d,tower).MaxHealth);
            }
        }
        [Test]public void InitialPriorityUsesPercentageAndExistingLockSurvivesWorseEntrant()
        {
            var d=New();int tower=Build(d);var at=Tower(d,tower).Position;
            // Health ratios are authoritative even when absolute missing HP ranks differently.
            int tank=Unit(d,new NavPoint(at.X+3,at.Z),60);int explorer=Unit(d,new NavPoint(at.X+3,at.Z+2),PlayableProfile.Default.ExplorerHealth-10,PlayableEntityKind.Explorer);
            Heal(d,.01);Assert.AreEqual(explorer,Tower(d,tower).Healing.TargetId);
            int worse=Unit(d,new NavPoint(at.X+3,at.Z-2),99);Heal(d,.01);Assert.AreEqual(explorer,Tower(d,tower).Healing.TargetId);
            Call(d,"Damage",explorer,10000);Heal(d,.01);Assert.AreEqual(worse,Tower(d,tower).Healing.TargetId);
        }
        [Test]public void FirstEntrantLocksThenExitSelectsLowestPercentage()
        {
            var d=New();int tower=Build(d);var at=Tower(d,tower).Position;Heal(d,.01);
            int first=Unit(d,new NavPoint(at.X+3,at.Z),10);Heal(d,.01);
            int worse=Unit(d,new NavPoint(at.X+3,at.Z+2),80);Heal(d,.01);Assert.AreEqual(first,Tower(d,tower).Healing.TargetId);
            d.Navigation.Crowd.Units.Single(u=>u.Id==first).Position=new NavPoint(at.X+40,at.Z);
            Heal(d,.01);Assert.AreEqual(worse,Tower(d,tower).Healing.TargetId);
        }
        [Test]public void ExistingHigherIdLockCannotBeStolenAndHealingCannotStack()
        {
            var d=New();int low=Build(d),high=Build(d,1002);var a=Tower(d,low).Position;var b=Tower(d,high).Position;
            int target=Unit(d,new NavPoint((a.X+b.X)/2,(a.Z+b.Z)/2),50);
            var rows=(System.Collections.IDictionary)typeof(PlayableDomain).GetField("buildings",F).GetValue(d);
            rows[high].GetType().GetField("Healing").SetValue(rows[high],new RepairTowerState{Target=target,Initialized=true});
            double before=Hp(d,target);Heal(d,1);Assert.AreEqual(before+4,Hp(d,target),1e-9);Assert.AreEqual(target,Tower(d,high).Healing.TargetId);Assert.Zero(Tower(d,low).Healing.TargetId);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"SellBuilding",high,PlayableOwner.Player,null));Heal(d,.25);Assert.AreEqual(target,Tower(d,low).Healing.TargetId);Assert.Zero(Tower(d,high).Healing.TargetId);
        }
        [Test]public void OwnUnitsOnlyAndDefensePlotEconomicContract()
        {
            var d=New();Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Call(d,"BuildAt",1,1,PlayableBuildingKind.RepairTower,1,PlayableOwner.Player,null));int tower=Build(d);var at=Tower(d,tower).Position;
            Unit(d,new NavPoint(at.X+3,at.Z),50,owner:PlayableOwner.Enemy);Call(d,"Damage",1,50);Heal(d,1);Assert.Zero(Tower(d,tower).Healing.TargetId);
            var p=PlayableProfile.Default;Assert.AreEqual(TerritoryRules.Cost(p,PlayableBuildingKind.SentryTurret),TerritoryRules.Cost(p,PlayableBuildingKind.RepairTower));Assert.AreEqual(TerritoryRules.Duration(p,PlayableBuildingKind.SentryTurret),TerritoryRules.Duration(p,PlayableBuildingKind.RepairTower));
            Call(d,"Damage",1,10000);Assert.False(View(d).Buildings.Any(b=>b.Id==tower));
        }
        [Test]public void CurrentSaveRetainsFractionalHealingTargetAndExactContinuation()
        {
            var d=New();int tower=Build(d);var at=Tower(d,tower).Position;Unit(d,new NavPoint(at.X+3,at.Z),50);d.Step(1d/30);
            var bytes=d.CaptureWorldBytes(7,"repair-current");var restored=PlayableDomain.RestoreWorldBytes(bytes,PlayableProfile.Default,7,"repair-current");owned.Add(restored);
            CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"repair-current"));
            for(int i=0;i<12;i++){d.Step(1d/30);restored.Step(1d/30);CollectionAssert.AreEqual(d.CaptureWorldBytes(7,"repair-current"),restored.CaptureWorldBytes(7,"repair-current"));}
        }
        [Test]public void RateHasValidatedLabMetadataAndCurrentWorldBinding()
        {
            var p=PlayableProfile.Default;Assert.AreEqual(4,p.RepairTowerHealthPerSecond);
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="repairTower.healthPerSecond");var data=p.CopyData();NativeBalanceFields.Write(field,data,5);
            CollectionAssert.AreNotEqual(WorldWire.Binding(p),WorldWire.Binding(PlayableProfile.Create(data)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(field,data,-1));data.repairTowerHealthPerSecond=-1;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
        [Test]public void ProvingPreparationAllowsRepairTowerAndKeepsPadBinding()
        {
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new ProvingGroundMap());var setup=new ProvingGroundSetup(p);var site=p.AuthoredMap.Sites(p).First(s=>s.Id==1);var pad=site.DefenseSlots(p)[0];
            Assert.IsNull(setup.PlaceBuilding(0,PlayableBuildingKind.RepairTower,pad.Position));Assert.AreEqual(pad.Id,setup.Buildings.Single().SlotId);Assert.DoesNotThrow(()=>setup.Configuration());
        }
    }
}

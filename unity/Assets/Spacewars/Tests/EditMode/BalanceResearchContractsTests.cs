using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class BalanceResearchContractsTests
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly List<PlayableDomain> owned=new List<PlayableDomain>();
        PlayableDomain d;PlayableProfile p;long sequence;
        object Call(string method,params object[] args)=>typeof(PlayableDomain).GetMethod(method,Hidden).Invoke(d,args);
        object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(value);
        void Set(object value,string name,object data)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(value,data);
        IDictionary Units=>(IDictionary)typeof(PlayableDomain).GetField("units",Hidden).GetValue(d);
        IList Shots=>(IList)typeof(PlayableDomain).GetField("projectiles",Hidden).GetValue(d);
        PlayableSnapshot View=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        PlayableResearchOrderSnapshot Order(PlayableResearchKind kind)=>View.OwnerResearch.Single(o=>o.Kind==kind);
        double Balance=>(double)Call("Balance",PlayableOwner.Player);
        [SetUp] public void Setup(){p=PlayableProfile.Default;d=new PlayableDomain(p,813,false);owned.Add(d);Call("AddCredits",PlayableOwner.Player,10000d);}
        [TearDown] public void Cleanup(){foreach(var domain in owned){domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();Assert.Zero(domain.Navigation.PhysicalHostBudget.PhysicalBytes);}owned.Clear();}
        int Build(PlayableBuildingKind kind,int slot)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,kind,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            return View.Buildings.Single(b=>b.SlotId==slot).Id;
        }
        PlayableCommandStatus Queue(int center,PlayableResearchKind kind)=>d.Apply(new PlayableCommand(813,++sequence,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:kind),out _);
        void Centers(out int a,out int b){Build(PlayableBuildingKind.Factory,5);a=Build(PlayableBuildingKind.ScientificCenter,1);b=Build(PlayableBuildingKind.ScientificCenter,2);}
        [Test] public void TwoCentersAdvanceIndependentlyWithGlobalUniquenessAndLocalCancellation()
        {
            Centers(out int a,out int b);double before=Balance;
            Assert.AreEqual(PlayableCommandStatus.Applied,Queue(a,PlayableResearchKind.TankChassis));
            Assert.AreEqual(PlayableCommandStatus.Applied,Queue(a,PlayableResearchKind.ExplorerAssaultGuns));
            Assert.AreEqual(PlayableCommandStatus.Applied,Queue(b,PlayableResearchKind.ShkvalGuidance));
            Assert.AreEqual(before-600,Balance);Assert.AreEqual(2,View.OwnerResearch.Count(o=>o.Active));
            Assert.AreEqual(PlayableCommandStatus.Rejected,Queue(b,PlayableResearchKind.TankChassis));
            Call("AdvanceResearch",10d);Assert.AreEqual(10,Order(PlayableResearchKind.TankChassis).Elapsed);Assert.AreEqual(10,Order(PlayableResearchKind.ShkvalGuidance).Elapsed);Assert.Zero(Order(PlayableResearchKind.ExplorerAssaultGuns).Elapsed);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,d.Apply(new PlayableCommand(813,++sequence,"player-1",PlayableCommandKind.CancelResearch,new[]{b},productionOrderId:Order(PlayableResearchKind.TankChassis).Id),out _));
            Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(813,++sequence,"player-1",PlayableCommandKind.CancelResearch,new[]{a},productionOrderId:Order(PlayableResearchKind.TankChassis).Id),out _));
            Assert.AreEqual(before-600,Balance,"Refunded chassis immediately funds only its own successor");
            Assert.True(Order(PlayableResearchKind.ExplorerAssaultGuns).Active);Assert.AreEqual(10,Order(PlayableResearchKind.ShkvalGuidance).Elapsed);
            Call("AdvanceResearch",20d);Assert.True(Order(PlayableResearchKind.ShkvalGuidance).Complete);Assert.AreEqual(20,Order(PlayableResearchKind.ExplorerAssaultGuns).Elapsed);
        }
        [Test] public void CenterDestructionCancelsPaidJobAndPreservesUnpaidOrdersWithoutStallingOtherCenter()
        {
            Centers(out int a,out int b);Queue(a,PlayableResearchKind.TankChassis);Queue(a,PlayableResearchKind.ExplorerAssaultGuns);Queue(b,PlayableResearchKind.ShkvalGuidance);Call("AdvanceResearch",10d);double before=Balance;
            Call("Damage",a,100000);Call("AdvanceResearch",0d);
            Assert.AreEqual(before,Balance);Assert.AreEqual(2,View.OwnerResearch.Count);Assert.AreEqual(b,Order(PlayableResearchKind.ShkvalGuidance).CenterId);
            Assert.AreEqual(b,Order(PlayableResearchKind.ExplorerAssaultGuns).CenterId);Assert.False(Order(PlayableResearchKind.ExplorerAssaultGuns).Active);Assert.Zero(Order(PlayableResearchKind.ExplorerAssaultGuns).PaidCost);
            Assert.AreEqual(10,Order(PlayableResearchKind.ShkvalGuidance).Elapsed);Assert.AreEqual(PlayableCommandStatus.Applied,Queue(b,PlayableResearchKind.TankChassis));
            Call("AdvanceResearch",20d);Assert.True(Order(PlayableResearchKind.ShkvalGuidance).Complete);Assert.True(Order(PlayableResearchKind.ExplorerAssaultGuns).Active);Assert.False(Order(PlayableResearchKind.TankChassis).Active);
        }
        [Test] public void ScarceCreditsCannotStartTwoWaitingJobsOrStallTheOtherCenter()
        {
            Centers(out int a,out int b);Queue(a,PlayableResearchKind.TankChassis);Queue(a,PlayableResearchKind.ExplorerAssaultGuns);Queue(b,PlayableResearchKind.ShkvalGuidance);Queue(b,PlayableResearchKind.ReinforcedConcrete);
            Call("AddCredits",PlayableOwner.Player,200-Balance);Call("AdvanceResearch",30d);
            Assert.False(Order(PlayableResearchKind.ExplorerAssaultGuns).Active);Assert.True(Order(PlayableResearchKind.ReinforcedConcrete).Active);Assert.Zero(Balance);
            Call("AddCredits",PlayableOwner.Player,300d);Call("AdvanceResearch",0d);Assert.True(Order(PlayableResearchKind.ExplorerAssaultGuns).Active);Assert.Zero(Balance);
        }
        [Test] public void ParallelResearchSurvivesExactWorldRestoreAndCompletesOnce()
        {
            Centers(out int a,out int b);Queue(a,PlayableResearchKind.ImprovedWeaponSystems);Queue(b,PlayableResearchKind.ImprovedDefensiveSystems);Call("AdvanceResearch",7d);
            var original=d;var bytes=d.CaptureWorldBytes(7,"parallel-research");var restored=PlayableDomain.RestoreWorldBytes(bytes,p,7,"parallel-research");owned.Add(restored);
            CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"parallel-research"));
            Call("AdvanceResearch",23d);d=restored;Call("AdvanceResearch",23d);
            CollectionAssert.AreEqual(original.CaptureWorldBytes(7,"parallel-research"),restored.CaptureWorldBytes(7,"parallel-research"));
            Assert.AreEqual(2,View.OwnerResearch.Count(o=>o.Complete));Assert.Zero(View.OwnerResearch.Count(o=>o.Active));double before=Balance;Call("AdvanceResearch",30d);Assert.AreEqual(before,Balance);
        }
        [Test] public void TowerDefaultsAndDescriptionsStayIndependentOfMobileUnitTuning()
        {
            var data=p.CopyData();data.tankHealth=200;data.explorerRange=15;data.explorerDamage=10;data.explorerAssaultBurstSize=10;data.explorerAssaultSpreadDeg=15;data.explorerBurstPauseMs=2000;data.explorerBurstShotIntervalMs=200;data.explorerProjectileSpeed=60;data.shkvalBaseRange=20;
            var changed=PlayableProfile.Create(data);
            foreach(var kind in new[]{PlayableBuildingKind.SentryTurret,PlayableBuildingKind.RepairTower,PlayableBuildingKind.GaussTower})
            {
                Assert.AreEqual(TerritoryRules.Health(p,kind),TerritoryRules.Health(changed,kind));
                CollectionAssert.AreEqual(PlayableMenuDescriptions.DefenseStats(p,kind,null),PlayableMenuDescriptions.DefenseStats(changed,kind,null));
            }
            Assert.AreEqual(300,changed.TurretHealth);Assert.AreEqual(450,TerritoryRules.Health(changed,PlayableBuildingKind.GaussTower));Assert.AreEqual(50,changed.GaussDamage);Assert.AreEqual(11.5,changed.GaussRange);Assert.AreEqual(10,changed.TurretRange);
            Assert.AreEqual(6,changed.TurretBurstSize);Assert.AreEqual(5,changed.TurretSpreadDeg);Assert.AreEqual(32,changed.TurretProjectileSpeed);
            foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Group.Contains("independent-defense-v1")))
            {
                Assert.IsNotEmpty(field.Description);Assert.IsNotEmpty(field.Unit);Assert.Greater(field.Step,0);Assert.True(NativeBalanceFields.Editable(field));
                var invalid=p.CopyData();Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(field,invalid,field.Maximum+field.Step));
                NativeBalanceFields.Write(field,invalid,field.Minimum);Assert.DoesNotThrow(()=>PlayableProfile.Create(invalid));
            }
        }
        [Test] public void ImportedTowerTermsSurviveCopySaveAndMobileTuning()
        {
            string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"spacewars-tower-profile-"+Guid.NewGuid().ToString("N")+".json");
            try
            {
                var store=new NativeBalanceStore(p,path);Assert.True(store.HasActive,store.Problem);var before=store.Selected;
                store.Copy("Independent defense test");var data=store.Selected.CopyData();data.tankHealth+=100;data.explorerDamage+=2;data.explorerRange+=1;
                store.Save(data,store.SelectedAudio);var restored=new NativeBalanceStore(p,path).Selected;
                Assert.AreEqual(before.TurretHealth,restored.TurretHealth);Assert.AreEqual(before.TurretRange,restored.TurretRange);Assert.AreEqual(before.TurretDamage,restored.TurretDamage);Assert.AreEqual(before.GaussDamage,restored.GaussDamage);Assert.AreEqual(before.GaussRange,restored.GaussRange);
            }
            finally{foreach(string file in System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(path),System.IO.Path.GetFileName(path)+"*"))System.IO.File.Delete(file);}
        }
        [TestCase(PlayableEntityKind.Tank,false)] [TestCase(PlayableEntityKind.Tank,true)]
        [TestCase(PlayableEntityKind.Explorer,false)] [TestCase(PlayableEntityKind.Explorer,true)]
        public void GroundMissMayHitSecondaryTargetOnlyWithinFullBudget(PlayableEntityKind kind,bool beyond)
        {
            int a=(int)Call("SpawnUnit",new NavPoint(-24,-20),PlayableOwner.Player,kind);
            int initial=(int)Call("SpawnUnit",new NavPoint(-20,-20),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            NavUnit Actor(int id){object[] args={id,null};Assert.True((bool)Call("TryActor",args));return (NavUnit)args[1];}
            var unit=Units[a];Set(unit,"Target",initial);Set(unit,"ExplicitTarget",true);Set(unit,"Turret",0d);Actor(a).Heading=0;
            Call("RefreshVision");Call("AdvanceCombat",.01d);
            var shot=Shots.Cast<object>().Single(x=>(int)Field(x,"Owner")==a);
            var start=(NavPoint)Field(shot,"Position");double dx=(double)Field(shot,"DirectionX"),dz=(double)Field(shot,"DirectionZ"),remaining=(double)Field(shot,"Remaining");
            double offset=kind==PlayableEntityKind.Explorer?p.ExplorerMuzzleOffset:0;
            Assert.AreEqual(12,offset+remaining,1e-9);
            Actor(initial).Position=new NavPoint(-20,-25);
            double travel=remaining+(beyond?p.TankCollisionRadius+.3:-.15);
            int secondary=(int)Call("SpawnUnit",new NavPoint(start.X+dx*travel,start.Z+dz*travel),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            Assert.Greater(secondary,0);Call("AdvanceProjectiles",2d);
            double damage=kind==PlayableEntityKind.Explorer?p.ExplorerDamage:p.TankWeaponDamage;
            Assert.AreEqual(p.TankHealth-(beyond?0:damage),View.Entities.Single(x=>x.Id==secondary).ExactHealth.Value,1e-9);
            Assert.False(Shots.Cast<object>().Any(x=>(int)Field(x,"Owner")==a));
        }
        [TestCase(false,8d/1.37d)] [TestCase(true,12d/1.55d)]
        public void ExplorerEstimateUsesTheIntervalsBetweenShots(bool upgraded,double expected)
        {Assert.AreEqual(expected,PlayableUnitRules.DamagePerSecond(p,PlayableEntityKind.Explorer,upgraded),1e-9);}
    }
}

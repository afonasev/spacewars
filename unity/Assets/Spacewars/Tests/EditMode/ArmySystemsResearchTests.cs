using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class ArmySystemsResearchTests
    {
        static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        object domain;PlayableProfile p;long seq;int center;
        object Call(string name,params object[] args)=>D.GetMethod(name,F).Invoke(domain,args);
        PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        IDictionary Units=>(IDictionary)D.GetField("units",F).GetValue(domain);
        [SetUp] public void Setup()
        {
            p=PlayableProfile.Default;domain=Activator.CreateInstance(D,F,null,new object[]{p,1L,false},null);seq=0;
            Call("AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            center=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            Assert.AreEqual(ConstructionPhase.Ready,View().Buildings.Single(b=>b.Id==center).Phase,"fixture center must actually be ready");
        }
        PlayableCommandStatus Research(PlayableResearchKind kind)=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++seq,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:kind),null);
        void Complete(PlayableResearchKind kind){Assert.AreEqual(PlayableCommandStatus.Applied,Research(kind));Call("AdvanceResearch",PlayableResearchRules.Duration(p,kind));Assert.True(View().OwnerResearch.Single(r=>r.Kind==kind).Complete);}
        [Test] public void VisibleUnitsRevealOnlyCompletedSystemsAndHiddenUnitsRevealNothing()
        {
            int near=(int)Call("SpawnUnit",new NavPoint(-22,-9),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            Call("SpawnUnit",new NavPoint(22,9),PlayableOwner.Player,PlayableEntityKind.Tank);
            Assert.AreEqual(PlayableCommandStatus.Applied,Research(PlayableResearchKind.ImprovedWeaponSystems));
            var before=View(PlayableOwner.Enemy).Entities.First(u=>u.Owner==PlayableOwner.Player);
            Assert.Zero(before.WeaponSystemsLevel,"active research is private");
            Call("AdvanceResearch",30d);Complete(PlayableResearchKind.ImprovedDefensiveSystems);Complete(PlayableResearchKind.AdvancedDefensiveSystems);
            var seen=PlayableAiObservation.From(View(PlayableOwner.Enemy));var targets=seen.Entities.Where(u=>u.Owner==PlayableOwner.Player).ToArray();Assert.IsNotEmpty(targets);
            Assert.True(targets.All(u=>u.WeaponSystemsLevel==1&&u.DefensiveSystemsLevel==2));
            Assert.True(seen.Buildings.Where(b=>b.Owner!=seen.Owner).All(b=>b.PrivateState==null),"research queues remain private");
            Assert.True(seen.Entities.All(u=>seen.Vision.IsVisible(u.Position)||u.Owner==seen.Owner));
        }
        [TestCase(PlayableResearchKind.ImprovedWeaponSystems,PlayableResearchKind.AdvancedWeaponSystems)]
        [TestCase(PlayableResearchKind.ImprovedDefensiveSystems,PlayableResearchKind.AdvancedDefensiveSystems)]
        public void OrdinaryResearchChargesSequentialLevelsOnce(PlayableResearchKind first,PlayableResearchKind second)
        {
            Assert.False(View().ResearchAvailability.Single(r=>r.Kind==second).Available);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Research(second));double balance=View().ExactCredits;
            Assert.AreEqual(PlayableCommandStatus.Applied,Research(first));Assert.AreEqual(balance-300,View().ExactCredits);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Research(first));Call("AdvanceResearch",29d);Assert.False(View().OwnerResearch.Single().Complete);Call("AdvanceResearch",1d);
            Assert.True(View().ResearchAvailability.Single(r=>r.Kind==second).Available);
            Assert.AreEqual(PlayableCommandStatus.Applied,Research(second));Assert.AreEqual(balance-800,View().ExactCredits);Call("AdvanceResearch",44d);Assert.False(View().OwnerResearch.Last().Complete);Call("AdvanceResearch",1d);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Research(second));Assert.AreEqual(2,View().OwnerResearch.Count);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void DefensePreservesExactHealthFractionAndNewUnits(PlayableEntityKind kind)
        {
            int id=(int)Call("SpawnUnit",new NavPoint(-22,-9),PlayableOwner.Player,kind);int hp=PlayableUnitRules.Health(p,kind);
            Call("Damage",id,hp/2);double remaining=hp-hp/2;
            Complete(PlayableResearchKind.ImprovedDefensiveSystems);var unit=View().Entities.Single(u=>u.Id==id);
            Assert.AreEqual(hp*1.15,unit.MaxHealth,1e-9);Assert.AreEqual(remaining*1.15,unit.ExactHealth.Value,1e-9);
            Complete(PlayableResearchKind.AdvancedDefensiveSystems);unit=View().Entities.Single(u=>u.Id==id);
            Assert.AreEqual(hp*1.30,unit.MaxHealth,1e-9);Assert.AreEqual(remaining*1.30,unit.ExactHealth.Value,1e-9);
            int fresh=(int)Call("SpawnUnit",new NavPoint(-20,-10),PlayableOwner.Player,kind);
            Assert.AreEqual(hp*1.30,View().Entities.Single(u=>u.Id==fresh).ExactHealth.Value,1e-9);
            Assert.AreEqual(p.ExplorerHealth,View(PlayableOwner.Enemy).Entities.Single(u=>u.Owner==PlayableOwner.Enemy).ExactHealth.Value);
            Call("Damage",center,(int)p.ScienceHealth+1);Assert.AreEqual(hp*1.30,View().Entities.Single(u=>u.Id==id).MaxHealth,1e-9);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void PhysicalShotsCarryFractionalDamageAndFreezeLaunchedTerms(PlayableEntityKind kind)
        {
            Complete(PlayableResearchKind.ImprovedWeaponSystems);
            int shooter=(int)Call("SpawnUnit",new NavPoint(-20,-16),PlayableOwner.Player,kind);
            int victim=(int)Call("SpawnUnit",new NavPoint(-16,-16),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            var u=Units[shooter];void Set(string name,object value)=>u.GetType().GetField(name).SetValue(u,value);
            Set("Target",victim);Set("ExplicitTarget",true);Set("StoppedSeconds",100d);Set("Turret",0d);
            Call("AdvanceCombat",.01d);
            var shots=((IEnumerable)D.GetField("projectiles",F).GetValue(domain)).Cast<object>().Where(x=>(int)x.GetType().GetField("Owner").GetValue(x)==shooter).ToArray();
            Assert.IsNotEmpty(shots,"ordinary firing path must emit a physical projectile");
            var shot=shots[0];double baseDamage=kind==PlayableEntityKind.Tank?p.TankWeaponDamage:kind==PlayableEntityKind.Explorer?p.ExplorerDamage:p.ShkvalDamage;
            double Damage()=> (double)shot.GetType().GetField("Damage").GetValue(shot);
            Assert.AreEqual(baseDamage*1.15,Damage(),1e-9);
            Complete(PlayableResearchKind.AdvancedWeaponSystems);Assert.AreEqual(baseDamage*1.15,Damage(),1e-9,"in-flight terms stay frozen");
            Assert.AreEqual(1.30,(double)Call("WeaponMultiplier",PlayableOwner.Player),1e-12);
            // Apply this exact projectile payload through the ordinary damage authority.
            var before=View(PlayableOwner.Enemy).Entities.Single(x=>x.Id==victim).ExactHealth.Value;
            Call("DamageFromProjectile",victim,Damage(),shooter);
            Assert.AreEqual(before-baseDamage*1.15,View(PlayableOwner.Enemy).Entities.Single(x=>x.Id==victim).ExactHealth.Value,1e-9);
        }
        [Test] public void ReportCompletedSystemsProjectionCostWithoutThreshold()
        {
            Complete(PlayableResearchKind.ImprovedWeaponSystems);Complete(PlayableResearchKind.AdvancedWeaponSystems);
            Complete(PlayableResearchKind.ImprovedDefensiveSystems);Complete(PlayableResearchKind.AdvancedDefensiveSystems);
            var read=(Func<PlayableOwner,bool,int>)Delegate.CreateDelegate(typeof(Func<PlayableOwner,bool,int>),domain,D.GetMethod("ArmySystemsLevel",F));
            int sum=0;var watch=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<100000;i++){sum+=read(PlayableOwner.Player,true);sum+=read(PlayableOwner.Player,false);}
            watch.Stop();Assert.AreEqual(400000,sum);Assert.Zero(read(PlayableOwner.Enemy,true));
            TestContext.WriteLine("SYSTEMS_PROJECTION_200K wall_ms="+watch.Elapsed.TotalMilliseconds);
        }
        [Test] public void ExactWorldRestoreAndLabApplyKeepEffectiveMaximum()
        {
            Complete(PlayableResearchKind.ImprovedDefensiveSystems);Complete(PlayableResearchKind.ImprovedWeaponSystems);
            int id=View().Entities.Single().Id;Call("Damage",id,11);
            Assert.AreEqual(PlayableCommandStatus.Applied,Research(PlayableResearchKind.AdvancedWeaponSystems));Call("AdvanceResearch",7d);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"army-systems-test");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"army-systems-test"});
            CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"army-systems-test"));
            double health=View().Entities.Single(u=>u.Id==id).ExactHealth.Value;
            Assert.AreEqual(3,View().OwnerResearch.Count);Assert.AreEqual(7,View().OwnerResearch.Single(r=>r.Active).Elapsed);
            var data=p.CopyData();data.revision++;var revised=PlayableProfile.Create(data);Call("ApplyBalance",revised);
            Assert.AreEqual(health,View().Entities.Single(u=>u.Id==id).ExactHealth.Value);Assert.AreEqual(p.ExplorerHealth*1.15,View().Entities.Single().MaxHealth,1e-9);
        }
        [Test] public void ActualDomainAiPlanOrdersNewTechnologyAndLossDoesNotCancelPaidWork()
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            Complete(PlayableResearchKind.TankChassis);Complete(PlayableResearchKind.ExplorerAssaultGuns);Complete(PlayableResearchKind.ShkvalGuidance);
            Assert.AreEqual(ConstructionPhase.Ready,View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Phase);
            for(int i=0;i<9;i++)Call("SpawnUnit",new NavPoint(-28+(i%3)*3,-14-(i/3)*3),PlayableOwner.Player,PlayableEntityKind.Tank);
            var o=PlayableAiObservation.From(View());Assert.AreEqual(10,o.Entities.Count(u=>u.Owner==o.Owner));
            var budget=new AiBudgetLedger(o.OwnerId,o.Generation);budget.ObserveBank(o.Credits);
            var plan=new AiTechnologyPlanner().Plan(o,p,AiProfile.Initial,budget.Capture());Assert.NotNull(plan.Action);Assert.True(PlayableResearchRules.Global(plan.Action.ResearchKind));
            Assert.AreEqual(PlayableCommandStatus.Applied,Research(plan.Action.ResearchKind));
            var victim=View().Entities.First(u=>u.Kind==PlayableEntityKind.Tank);Call("Damage",victim.Id,10000);
            Assert.False(AiTechnologyPlanner.ArmySizeAllows(PlayableAiObservation.From(View()),p,plan.Action.ResearchKind));
            Call("AdvanceResearch",PlayableResearchRules.Duration(p,plan.Action.ResearchKind));
            Assert.True(View().OwnerResearch.Single(r=>r.Kind==plan.Action.ResearchKind).Complete);
        }
        [Test] public void OrdinaryFactoryProducesUnitWithCompletedDefensiveTechnology()
        {
            Complete(PlayableResearchKind.ImprovedDefensiveSystems);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            int factory=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++seq,"player-1",PlayableCommandKind.QueueTank,new[]{factory}),null));
            Call("AdvanceProduction",1d);Call("AdvanceProduction",p.TankProductionSeconds);
            // Finish the existing physical rollout; completion of production opens the gate first.
            Call("AdvanceProduction",FactoryReleaseRules.Duration(p,PlayableEntityKind.Tank));
            var tank=View().Entities.Single(u=>u.Kind==PlayableEntityKind.Tank);Assert.AreEqual(p.TankHealth*1.15,tank.ExactHealth.Value,1e-9);Assert.AreEqual(tank.ExactHealth.Value,tank.MaxHealth);
        }
        [Test] public void AllBalanceParametersHaveWritersBoundsAndCrossLevelValidation()
        {
            var fields=PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("research.weaponSystems.")||f.Path.StartsWith("research.defensiveSystems.")||f.Path=="ai.research.armySystemsMinimumUnits").ToArray();Assert.AreEqual(13,fields.Length);
            foreach(var f in fields){Assert.True(NativeBalanceFields.Editable(f));Assert.IsNotEmpty(f.Label);Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);
                foreach(var value in new[]{f.Minimum-f.Step,f.Maximum+f.Step,double.NaN,double.PositiveInfinity}){var data=p.CopyData();typeof(PlayableProfileData).GetField(f.FieldName).SetValue(data,value);Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data),f.Path);}}
            var bad=p.CopyData();bad.weaponSystemsLevel2BonusPercent=14;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(bad));
        }
    }
}

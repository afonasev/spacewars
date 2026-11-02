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
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,5,PlayableBuildingKind.Hangar,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            center=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            Assert.AreEqual(ConstructionPhase.Ready,View().Buildings.Single(b=>b.Id==center).Phase,"fixture center must actually be ready");
        }
        PlayableCommandStatus Research(PlayableResearchKind kind)=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++seq,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:kind),null);
        void UpgradeCenter(){if(!View().Buildings.Single(b=>b.Id==center).ScientificCenterUpgraded)Complete(PlayableResearchKind.ScientificCenterUpgrade);}
        void Complete(PlayableResearchKind kind){if(PlayableResearchRules.NeedsUpgradedCenter(kind))UpgradeCenter();Assert.AreEqual(PlayableCommandStatus.Applied,Research(kind));Call("AdvanceResearch",PlayableResearchRules.Duration(p,kind));Assert.True(View().OwnerResearch.Single(r=>r.Kind==kind&&(!PlayableResearchRules.Local(kind)||r.CenterId==center)).Complete);}
        [TestCase(false)] [TestCase(true)]
        public void ChassisHealthCombinesWithDefensePreservesDamageAndSurvivesRestore(bool defenseFirst)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            int tank=(int)Call("SpawnUnit",new NavPoint(-22,-9),PlayableOwner.Player,PlayableEntityKind.Tank);
            Call("Damage",tank,40);
            if(defenseFirst)Complete(PlayableResearchKind.ImprovedDefensiveSystems);
            Complete(PlayableResearchKind.TankChassis);
            if(!defenseFirst)Complete(PlayableResearchKind.ImprovedDefensiveSystems);
            double multiplier=1.4; // 100 × 1.2 × 1.15 = 138, rounded once to 140.
            var unit=View().Entities.Single(u=>u.Id==tank);
            Assert.AreEqual(p.TankHealth*multiplier,unit.MaxHealth,1e-9);
            Assert.AreEqual((p.TankHealth-40)*multiplier,unit.ExactHealth.Value,1e-9);
            int fresh=(int)Call("SpawnUnit",new NavPoint(-20,-10),PlayableOwner.Player,PlayableEntityKind.Tank);
            Assert.AreEqual(p.TankHealth*multiplier,View().Entities.Single(u=>u.Id==fresh).ExactHealth.Value,1e-9);
            byte[] bytes=(byte[])Call("CaptureWorldBytes",7,"chassis-health");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"chassis-health"});
            Assert.AreEqual(unit.ExactHealth,View().Entities.Single(u=>u.Id==tank).ExactHealth);
            Assert.AreEqual(unit.MaxHealth,View().Entities.Single(u=>u.Id==tank).MaxHealth);
        }
        [Test] public void RuntimeCombatSurfaceTracksTheCompletedCenterModelUpgrade()
        {
            var buildings=(IDictionary)D.GetField("buildings",F).GetValue(domain);
            BallisticPoint Roof()
            {
                var snapshot=View().Buildings.Single(b=>b.Id==center);
                var pose=(BuildingCombatPose)Call("BuildingCombat",buildings[center]);
                var from=new BallisticPoint(snapshot.Position.X,30,snapshot.Position.Z);
                var to=new BallisticPoint(snapshot.Position.X,-2,snapshot.Position.Z);
                Assert.True(pose.Segment(from,to,0,out _,out var hit));return hit;
            }
            var original=Roof();UpgradeCenter();var upgraded=Roof();
            Assert.AreNotEqual(original.Y,upgraded.Y,"Completed upgrade must change the authoritative surface with the visible scanner");
            var data=(byte[])Call("CaptureWorldBytes",7,"combat-center-upgrade");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{data,p,7,"combat-center-upgrade"});
            Assert.AreEqual(upgraded.Y,Roof().Y,"Restored upgraded center must keep its current combat surface");
        }
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
            Assert.AreEqual(PlayableCommandStatus.Rejected,Research(second));UpgradeCenter();double balance=View().ExactCredits;
            Assert.AreEqual(PlayableCommandStatus.Applied,Research(first));Assert.AreEqual(balance-200,View().ExactCredits);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Research(first));Call("AdvanceResearch",29d);Assert.False(View().OwnerResearch.Single(r=>r.Kind==first).Complete);Call("AdvanceResearch",1d);
            Assert.True(View().ResearchAvailability.Single(r=>r.Kind==second).Available);
            Assert.AreEqual(PlayableCommandStatus.Applied,Research(second));Assert.AreEqual(balance-500,View().ExactCredits);Call("AdvanceResearch",44d);Assert.False(View().OwnerResearch.Last().Complete);Call("AdvanceResearch",1d);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Research(second));Assert.AreEqual(3,View().OwnerResearch.Count);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void DefensePreservesExactHealthFractionAndNewUnits(PlayableEntityKind kind)
        {
            int id=(int)Call("SpawnUnit",new NavPoint(-22,-9),PlayableOwner.Player,kind);int hp=PlayableUnitRules.Health(p,kind);
            Call("Damage",id,hp/2);double remaining=hp-hp/2;
            Complete(PlayableResearchKind.ImprovedDefensiveSystems);var unit=View().Entities.Single(u=>u.Id==id);
            Assert.AreEqual(PlayableStatRounding.MaximumHealth(p,hp*1.15),unit.MaxHealth,1e-9);Assert.AreEqual(remaining*PlayableStatRounding.MaximumHealth(p,hp*1.15)/hp,unit.ExactHealth.Value,1e-9);
            Complete(PlayableResearchKind.AdvancedDefensiveSystems);unit=View().Entities.Single(u=>u.Id==id);
            Assert.AreEqual(PlayableStatRounding.MaximumHealth(p,hp*1.30),unit.MaxHealth,1e-9);Assert.AreEqual(remaining*PlayableStatRounding.MaximumHealth(p,hp*1.30)/hp,unit.ExactHealth.Value,1e-9);
            int fresh=(int)Call("SpawnUnit",new NavPoint(-20,-10),PlayableOwner.Player,kind);
            Assert.AreEqual(PlayableStatRounding.MaximumHealth(p,hp*1.30),View().Entities.Single(u=>u.Id==fresh).ExactHealth.Value,1e-9);
            Assert.AreEqual(p.ExplorerHealth,View(PlayableOwner.Enemy).Entities.Single(u=>u.Owner==PlayableOwner.Enemy).ExactHealth.Value);
            Call("Damage",center,(int)p.ScienceHealth+1);Assert.AreEqual(PlayableStatRounding.MaximumHealth(p,hp*1.30),View().Entities.Single(u=>u.Id==id).MaxHealth,1e-9);
        }
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Shkval)]
        public void PhysicalShotsCarryFractionalDamageAndFreezeLaunchedTerms(PlayableEntityKind kind)
        {
            Complete(PlayableResearchKind.ImprovedWeaponSystems);
            int shooter=(int)Call("SpawnUnit",new NavPoint(-20,-16),PlayableOwner.Player,kind);
            int victim=(int)Call("SpawnUnit",new NavPoint(-16,-16),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            var u=Units[shooter];void Set(string name,object value)=>u.GetType().GetField(name).SetValue(u,value);
            Set("Target",victim);Set("ExplicitTarget",true);Set("StoppedSeconds",100d);Set("Turret",0d);
            // This fixture measures payload terms, not weapon aiming. Align the
            // articulated launch pose before its unchanged single firing tick.
            if(kind==PlayableEntityKind.Shkval)
            {
                object[] actorArgs={shooter,null};Assert.True((bool)Call("TryActor",actorArgs));var actor=(NavUnit)actorArgs[1];
                var flight=(BallisticFlight)Call("BaselineFlight",actor.Position,new NavPoint(-16,-16));
                ShkvalLaunchGeometry.Aim(new BallisticPoint(actor.Position.X,0,actor.Position.Z),actor.Heading,false,0,p.ShkvalModelRadius*p.ShkvalModelScale/2.05,p.ShkvalProjectileLength,default(NavPoint),flight.End,p.ShkvalArcHeight,out double heading,out double pitch);
                Set("Turret",heading);Set("ShkvalPitch",pitch);
            }
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
            UpgradeCenter();Assert.AreEqual(PlayableCommandStatus.Applied,Research(PlayableResearchKind.AdvancedWeaponSystems));Call("AdvanceResearch",7d);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"army-systems-test");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"army-systems-test"});
            CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"army-systems-test"));
            double health=View().Entities.Single(u=>u.Id==id).ExactHealth.Value;
            Assert.AreEqual(4,View().OwnerResearch.Count);Assert.AreEqual(7,View().OwnerResearch.Single(r=>r.Active).Elapsed);
            var data=p.CopyData();data.revision++;var revised=PlayableProfile.Create(data);Call("ApplyBalance",revised);
            Assert.AreEqual(health,View().Entities.Single(u=>u.Id==id).ExactHealth.Value);Assert.AreEqual(80,View().Entities.Single().MaxHealth,1e-9);
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
        [Test] public void CenterUpgradeIsLocalPaidAtStartCancelableAndExactRestorable()
        {
            double bank=View().ExactCredits;
            Assert.AreEqual(PlayableCommandStatus.Applied,Research(PlayableResearchKind.ScientificCenterUpgrade));
            Assert.AreEqual(bank-300,View().ExactCredits);Call("AdvanceResearch",13d);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"center-upgrade-test");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"center-upgrade-test"});
            CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"center-upgrade-test"));
            var job=View().OwnerResearch.Single();Assert.AreEqual(13,job.Elapsed);Assert.False(View().Buildings.Single(b=>b.Id==center).ScientificCenterUpgraded);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++seq,"player-1",PlayableCommandKind.CancelResearch,new[]{center},productionOrderId:job.Id),null));
            Assert.AreEqual(bank,View().ExactCredits);UpgradeCenter();
            Assert.True(View().Buildings.Single(b=>b.Id==center).ScientificCenterUpgraded);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Research(PlayableResearchKind.ScientificCenterUpgrade));
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,3,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            int first=center;center=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter&&b.Id!=first).Id;
            Assert.False(View().Buildings.Single(b=>b.Id==center).ScientificCenterUpgraded);
            Complete(PlayableResearchKind.ImprovedWeaponSystems);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Research(PlayableResearchKind.AdvancedWeaponSystems));
            UpgradeCenter();Assert.True(View().Buildings.Where(b=>b.Kind==PlayableBuildingKind.ScientificCenter).All(b=>b.ScientificCenterUpgraded));
            var both=(byte[])Call("CaptureWorldBytes",7,"two-upgraded-centers");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{both,p,7,"two-upgraded-centers"});
            CollectionAssert.AreEqual(both,(byte[])Call("CaptureWorldBytes",7,"two-upgraded-centers"));
            Call("Damage",first,10000);Assert.AreEqual(1,View().OwnerResearch.Count(o=>PlayableResearchRules.Local(o.Kind)));
        }
        [Test] public void PublicCenterModelAndFogMemoryNeverExposePrivateQueue()
        {
            var pos=View().Buildings.Single(b=>b.Id==center).Position;
            int scout=(int)Call("SpawnUnit",new NavPoint(pos.X+4,pos.Z),PlayableOwner.Enemy,PlayableEntityKind.Explorer);
            Assert.AreEqual(PlayableCommandStatus.Applied,Research(PlayableResearchKind.ScientificCenterUpgrade));Call("AdvanceResearch",15d);
            var during=View(PlayableOwner.Enemy).Buildings.Single(b=>b.Id==center);Assert.False(during.ScientificCenterUpgraded);Assert.Null(during.PrivateState);
            Call("AdvanceResearch",15d);
            var enemy=View(PlayableOwner.Enemy);var seen=enemy.Buildings.Single(b=>b.Id==center);Assert.True(seen.ScientificCenterUpgraded);Assert.Null(seen.PrivateState);
            Assert.IsEmpty(enemy.OwnerResearch);
            Call("Damage",scout,10000);enemy=View(PlayableOwner.Enemy);
            var memory=enemy.Vision.KnownBuildings.Single(b=>b.Id==center);Assert.True(memory.ScientificCenterUpgraded);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"center-fog-test");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"center-fog-test"});
            CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"center-fog-test"));
            Assert.True(View(PlayableOwner.Enemy).Vision.KnownBuildings.Single(b=>b.Id==center).ScientificCenterUpgraded);
        }
        [TestCase(PlayableBuildingKind.ScientificCenter,false)] [TestCase(PlayableBuildingKind.ScientificCenter,true)]
        [TestCase(PlayableBuildingKind.Refinery,false)] [TestCase(PlayableBuildingKind.Refinery,true)]
        public void HiddenBuildingUpgradeAndRemovalKeepLastSeenImageUntilScoutedAgain(PlayableBuildingKind kind,bool sale)
        {
            int id=center;
            if(kind==PlayableBuildingKind.Refinery){Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,3,kind,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);id=View().Buildings.Single(b=>b.Kind==kind).Id;}
            var pos=View().Buildings.Single(b=>b.Id==id).Position;
            // Flying observers avoid placing a later scout inside the previous ground scout wreck.
            int Scout()=>(int)Call("SpawnUnit",new NavPoint(pos.X+4,pos.Z),PlayableOwner.Enemy,PlayableEntityKind.Hawk);
            int scout=Scout();var seen=View(PlayableOwner.Enemy);Assert.False(seen.Vision.KnownBuildings.Single(b=>b.Id==id).ModelUpgraded);
            Call("Damage",scout,10000);Assert.False(View(PlayableOwner.Enemy).Vision.IsVisible(pos));
            if(kind==PlayableBuildingKind.ScientificCenter)UpgradeCenter();
            else {UpgradeCenter();Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++seq,"player-1",PlayableCommandKind.UpgradeRefinery,new[]{id}),null));Call("AdvanceRefineryUpgrades",p.RefineryUpgradeSeconds);}
            Assert.True(View().Buildings.Single(b=>b.Id==id).ModelUpgraded);
            var hidden=View(PlayableOwner.Enemy);Assert.False(hidden.Buildings.Any(b=>b.Id==id));Assert.False(hidden.Vision.KnownBuildings.Single(b=>b.Id==id).ModelUpgraded,"hidden upgrade must not reveal itself");
            var bytes=(byte[])Call("CaptureWorldBytes",7,"hidden-upgrade-last-seen");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"hidden-upgrade-last-seen"});
            CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"hidden-upgrade-last-seen"));
            Assert.False(View(PlayableOwner.Enemy).Vision.KnownBuildings.Single(b=>b.Id==id).ModelUpgraded,"restore must retain the old seen model");
            scout=Scout();seen=View(PlayableOwner.Enemy);Assert.True(seen.Buildings.Single(b=>b.Id==id).ModelUpgraded);Assert.True(seen.Vision.KnownBuildings.Single(b=>b.Id==id).ModelUpgraded);Assert.Null(seen.Buildings.Single(b=>b.Id==id).PrivateState);
            Call("Damage",scout,10000);Assert.False(View(PlayableOwner.Enemy).Vision.IsVisible(pos));
            if(sale){Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++seq,"player-1",PlayableCommandKind.SellBuilding,new[]{id}),null));Call("AdvanceBuildingLifecycle",p.BuildingSaleDemolitionSec);}
            else Call("Damage",id,10000);
            hidden=View(PlayableOwner.Enemy);Assert.True(hidden.Vision.KnownBuildings.Single(b=>b.Id==id).ModelUpgraded,"hidden sale/destruction keeps the last seen upgraded picture");Assert.False(hidden.BuildingDestructions.Any(b=>b.Id==id));
            Scout();Assert.False(View(PlayableOwner.Enemy).Vision.KnownBuildings.Any(b=>b.Id==id),"visible empty location confirms removal");
        }
        [Test] public void ThirdLevelsReplaceTotalsPreserveFractionAndFutureUnits()
        {
            int id=View().Entities.Single().Id;Call("Damage",id,17);double hp=View().Entities.Single().ExactHealth.Value;
            Assert.AreEqual(PlayableCommandStatus.Rejected,Research(PlayableResearchKind.UltimateWeaponSystems));
            Complete(PlayableResearchKind.ImprovedWeaponSystems);Complete(PlayableResearchKind.AdvancedWeaponSystems);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Research(PlayableResearchKind.UltimateDefensiveSystems));
            Complete(PlayableResearchKind.UltimateWeaponSystems);
            Complete(PlayableResearchKind.ImprovedDefensiveSystems);Complete(PlayableResearchKind.AdvancedDefensiveSystems);Complete(PlayableResearchKind.UltimateDefensiveSystems);
            Assert.AreEqual(1.5,(double)Call("WeaponMultiplier",PlayableOwner.Player));
            var u=View().Entities.Single();Assert.AreEqual(p.ExplorerHealth*1.5,u.MaxHealth,1e-9);Assert.AreEqual(hp*1.5,u.ExactHealth.Value,1e-9);
            Assert.AreEqual(3,u.WeaponSystemsLevel);Assert.AreEqual(3,u.DefensiveSystemsLevel);
            int fresh=(int)Call("SpawnUnit",new NavPoint(-20,-10),PlayableOwner.Player,PlayableEntityKind.Tank);
            Assert.AreEqual(p.TankHealth*1.5,View().Entities.Single(x=>x.Id==fresh).ExactHealth.Value,1e-9);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"third-systems-test");
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"third-systems-test"});
            CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"third-systems-test"));
            Call("Damage",center,10000);Assert.AreEqual(1.5,(double)Call("WeaponMultiplier",PlayableOwner.Player));Assert.AreEqual(hp*1.5,View().Entities.Single(x=>x.Id==id).ExactHealth.Value,1e-9);
        }
        [Test] public void AllBalanceParametersHaveWritersBoundsAndCrossLevelValidation()
        {
            CollectionAssert.AreEqual(new double[]{200,300,400,200,300,400},new[]{p.WeaponSystemsLevel1Cost,p.WeaponSystemsLevel2Cost,p.WeaponSystemsLevel3Cost,p.DefensiveSystemsLevel1Cost,p.DefensiveSystemsLevel2Cost,p.DefensiveSystemsLevel3Cost});
            var fields=PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("research.weaponSystems.")||f.Path.StartsWith("research.defensiveSystems.")||f.Path.StartsWith("research.centerUpgrade.")||f.Path=="ai.research.armySystemsMinimumUnits").ToArray();Assert.AreEqual(21,fields.Length);
            foreach(var f in fields){Assert.True(NativeBalanceFields.Editable(f));Assert.IsNotEmpty(f.Label);Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Greater(f.Step,0);
                foreach(var value in new[]{f.Minimum-f.Step,f.Maximum+f.Step,double.NaN,double.PositiveInfinity}){var data=p.CopyData();typeof(PlayableProfileData).GetField(f.FieldName).SetValue(data,value);Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data),f.Path);}}
            var bad=p.CopyData();bad.weaponSystemsLevel2BonusPercent=14;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(bad));
        }
    }
}

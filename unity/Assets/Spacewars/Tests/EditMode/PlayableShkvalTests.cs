using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableShkvalTests
    {
        private static readonly Type Domain=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private object domain;private PlayableProfile p;private long sequence;
        private object Call(string name,params object[] args)=>Domain.GetMethod(name,Flags).Invoke(domain,args);
        private NavigationSession Nav=>(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(domain);
        private IDictionary Units=>(IDictionary)Domain.GetField("units",Flags).GetValue(domain);
        private PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player)=>(PlayableSnapshot)Call("PlayerSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        [SetUp]public void Setup(){p=PlayableProfile.Default;domain=Activator.CreateInstance(Domain,Flags,null,new object[]{p,1L},null);sequence=0;Clear();}
        private void Clear(){foreach(int id in Units.Keys.Cast<int>().ToArray()){Nav.Remove(id);Units.Remove(id);}}
        private int Spawn(PlayableOwner owner,PlayableEntityKind kind,double x,double z)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,kind);
        private void Combat(int count){for(int i=0;i<count;i++){Call("RefreshVision");Call("AdvanceCombat",1d/30);Call("AdvanceProjectiles",1d/30);}}
        private PlayableCommandStatus Send(PlayableCommandKind kind,int id,PlayableEntityKind unitKind=PlayableEntityKind.Tank,long order=0,int target=0)=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind,new[]{id},unitKind:unitKind,productionOrderId:order,targetId:target),null);
        private void Set(int id,string field,object value)=>Units[id].GetType().GetField(field).SetValue(Units[id],value);
        private T Get<T>(int id,string field)=>(T)Units[id].GetType().GetField(field).GetValue(Units[id]);
        // Rapid-fire/cell fixtures start already aimed; preparation has its own contract below.
        private void Align(int gun,int target){
            var e=View().Entities.Single(x=>x.Id==gun);var t=View().Entities.Single(x=>x.Id==target);
            ShkvalLaunchGeometry.Aim(new BallisticPoint(e.Position.X,0,e.Position.Z),e.HullHeading,e.Upgraded,e.WeaponSequence,p.ShkvalModelRadius*p.ShkvalModelScale/2.05,p.ShkvalProjectileLength,default,new BallisticPoint(t.Position.X,0,t.Position.Z),p.ShkvalArcHeight,out double yaw,out double pitch);
            Set(gun,"Turret",yaw);Set(gun,"ShkvalPitch",pitch);
        }
        private int Factory(){Call("AddCredits",PlayableOwner.Player,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);return View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.Factory).Id;}
        [TestCase(PlayableEntityKind.Shkval)]
        [TestCase(PlayableEntityKind.Tank)]
        [TestCase(PlayableEntityKind.Explorer)]
        public void IdleWeaponTracksHullAndReturnsAfterTargetLoss(PlayableEntityKind kind)
        {
            int gun=Spawn(PlayableOwner.Player,kind,-12,0);
            Nav.Crowd.TryGet(gun,out var actor);actor.Heading=1.2;
            double speed=kind==PlayableEntityKind.Shkval?p.ShkvalTurretTurnSpeed:kind==PlayableEntityKind.Explorer?p.ExplorerTurretTurnSpeed:p.TankTurretTurnSpeed;
            Combat(1);Assert.AreEqual(Math.Min(1.2,speed/30),Get<double>(gun,"Turret"),1e-10);
            Combat(120);Assert.AreEqual(actor.Heading,Get<double>(gun,"Turret"),1e-10);
            // A new chassis heading must carry the resting weapon along with it.
            actor.Heading=-.7;Combat(120);Assert.AreEqual(actor.Heading,Get<double>(gun,"Turret"),1e-10);
            int target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);
            Set(target,"Reload",999d);Set(gun,"Target",target);Set(gun,"ExplicitTarget",true);Set(gun,"Reload",999d);
            Combat(90);Assert.Greater(Math.Abs(Get<double>(gun,"Turret")-actor.Heading),.2,"Combat must still aim away from the hull");
            Nav.Remove(target);Units.Remove(target);
            double before=Get<double>(gun,"Turret");Combat(1);
            Assert.LessOrEqual(Math.Abs(Get<double>(gun,"Turret")-before),speed/30+1e-10,"No return snap");
            Combat(120);var rest=View().Entities.Single(e=>e.Id==gun);
            Assert.AreEqual(actor.Heading,rest.TurretHeading,1e-10);
            Assert.AreEqual(kind==PlayableEntityKind.Shkval?ShkvalLaunchGeometry.NeutralPitch:0,rest.WeaponPitch,1e-10);
            Assert.IsEmpty(View().Projectiles);
        }
        [Test]public void IdleAcquisitionRaisesBeforeFiringWithoutMovingRoot()
        {
            int gun=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);
            Set(target,"Reload",999d);Set(gun,"StoppedSeconds",10d);Set(gun,"Target",target);Set(gun,"ExplicitTarget",true);Call("RefreshVision");
            var before=View().Entities.Single(e=>e.Id==gun);Combat(1);var raised=View().Entities.Single(e=>e.Id==gun);
            Assert.IsEmpty(View().Projectiles);Assert.Greater(raised.WeaponPitch,before.WeaponPitch);Assert.LessOrEqual(raised.WeaponPitch-before.WeaponPitch,p.ShkvalTurretTurnSpeed/30+1e-12);Assert.AreEqual(before.Position,raised.Position);
            Combat(4);var shot=View().Projectiles.Single();Assert.NotNull(shot.Launch);Assert.Greater(shot.Launch.Pitch,ShkvalLaunchGeometry.NeutralPitch);Assert.AreEqual(before.Position,shot.Launch.Ground.Ground);
        }
        [Test]public void FourMuzzlesCyclePerUnitIndependentlyOfOtherWeapons()
        {
            int a=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),b=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,3),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);
            Set(target,"Reload",999d);Set(a,"StoppedSeconds",10d);Set(b,"StoppedSeconds",10d);
            Set(a,"Target",target);Set(a,"ExplicitTarget",true);Set(b,"Reload",999d);Call("RefreshVision");
            var seen=new System.Collections.Generic.List<int>();
            for(int i=0;i<5;i++){
                Set(a,"Reload",0d);Align(a,target);Call("AdvanceIndexedCombat",1d/30);
                var shot=View().Projectiles.Where(x=>x.OwnerId==a).OrderByDescending(x=>x.Id).First();Assert.NotNull(shot.Launch);seen.Add(shot.Launch.Barrel);
                Assert.AreEqual((i+1)%4,View().Entities.Single(x=>x.Id==a).WeaponSequence);
            }
            CollectionAssert.AreEqual(new[]{0,1,2,3,0},seen);Assert.Zero(View().Entities.Single(x=>x.Id==b).WeaponSequence);
        }
        [Test]public void FourLoadedCellsConsumeRefillOldestAndRecoverWithoutAmmoGate()
        {
            int gun=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);
            Set(target,"Reload",999d);Set(gun,"StoppedSeconds",10d);Set(gun,"Target",target);Set(gun,"ExplicitTarget",true);Call("RefreshVision");
            Assert.Null(View().Entities.Single(x=>x.Id==gun).ShkvalCells,"Unfired unit has four ready cells");
            double interval=p.ShkvalFireIntervalMs/1000;
            Align(gun,target);Call("AdvanceIndexedCombat",0d);
            var first=View().Entities.Single(x=>x.Id==gun).ShkvalCells;
            Assert.AreEqual(2*interval,first.Remaining(0));for(int c=1;c<4;c++)Assert.Zero(first.Remaining(c));
            Call("AdvanceIndexedCombat",interval);
            var second=View().Entities.Single(x=>x.Id==gun).ShkvalCells;
            Assert.AreEqual(interval,second.Remaining(0),1e-9,"Oldest begins feeding when two remain");Assert.AreEqual(2*interval,second.Remaining(1));Assert.Zero(second.Remaining(2));Assert.Zero(second.Remaining(3));
            Assert.AreEqual(2*interval,first.Remaining(0),"Earlier snapshot remains immutable");
            for(int shot=2;shot<12;shot++){
                Call("AdvanceIndexedCombat",interval);
                var e=View().Entities.Single(x=>x.Id==gun);
                Assert.AreEqual((shot+1)%4,e.WeaponSequence,"Visual supply never prevents a shot");
                Assert.GreaterOrEqual(Enumerable.Range(0,4).Count(c=>e.ShkvalCells.Remaining(c)==0),2);
            }
            Set(gun,"Reload",999d);Call("AdvanceIndexedCombat",interval);
            Assert.AreEqual(3,Enumerable.Range(0,4).Count(c=>View().Entities.Single(x=>x.Id==gun).ShkvalCells.Remaining(c)==0));
            Call("AdvanceIndexedCombat",interval);
            for(int c=0;c<4;c++)Assert.Zero(View().Entities.Single(x=>x.Id==gun).ShkvalCells.Remaining(c));
        }
        [Test]public void MixedProductionKeepsShkvalPayloadAndTypedRepeat()
        {
            int f=Factory(),before=View().Credits;Send(PlayableCommandKind.QueueShkval,f);Send(PlayableCommandKind.QueueExplorer,f);Send(PlayableCommandKind.QueueTank,f);Call("AdvanceProduction",1d);
            var q=View().Buildings.Single(b=>b.Id==f).PrivateState.Orders;Assert.AreEqual(3,q.Count);Assert.AreEqual(PlayableEntityKind.Shkval,q[0].Kind);Assert.AreEqual(p.ShkvalCreditCost,q[0].PaidCost);Assert.AreEqual(p.ShkvalProductionDurationSec,q[0].Duration);Assert.AreEqual(p.ShkvalPopulationCost,View().Population.Reserved);
            Send(PlayableCommandKind.CancelProductionOrder,f,order:q[1].Id);Assert.AreEqual(before-p.ShkvalCreditCost-p.TankCreditCost,View().Credits);
            Call("AdvanceProduction",100d);Call("AdvanceProduction",p.FactoryGateSeconds);Call("AdvanceProduction",FactoryReleaseRules.Duration(p,PlayableEntityKind.Shkval));var unit=View().Entities.Single(e=>e.Kind==PlayableEntityKind.Shkval);Assert.AreEqual(p.ShkvalHealth,unit.Health);Assert.AreEqual(p.ShkvalPopulationCost,View().Population.Living);Assert.True(Nav.Crowd.TryGet(unit.Id,out var body));Assert.AreEqual(p.ShkvalCollisionRadius,body.Radius);
            Send(PlayableCommandKind.ToggleRepeatProduction,f,PlayableEntityKind.Shkval);Assert.AreEqual(PlayableEntityKind.Shkval,View().Buildings.Single(b=>b.Id==f).PrivateState.RepeatKind);
        }
        [Test]public void BaselineRangeAndPreparationAreIndependentFromTank()
        {
            int id=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0);Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-1,0);Call("RefreshVision");
            Combat(53);Assert.IsEmpty(View().Projectiles);Combat(2);Assert.True(View().Projectiles.Any(x=>x.Kind==PlayableEntityKind.Shkval));Assert.AreEqual(13,PlayableUnitRules.Range(p,PlayableEntityKind.Shkval));Assert.AreEqual(16,p.ShkvalRange);
            Set(id,"Velocity",new NavPoint(.01,0));Combat(1);Assert.Zero(Get<double>(id,"StoppedSeconds"));
        }
        [Test]public void MissileSurvivesSourceAndFrozenEndpointDoesNotHome()
        {
            int id=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0);int target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);Set(target,"Reload",999d);Combat(55);
            var shot=View().Projectiles.Single(x=>x.Kind==PlayableEntityKind.Shkval);Assert.NotNull(shot.Marker);double endpoint=shot.Marker.Position.X;
            Call("Damage",id,999);Nav.Remove(target);Units.Remove(target);Combat(2);var later=View().Projectiles.Single(x=>x.Kind==PlayableEntityKind.Shkval);Assert.AreEqual(endpoint,later.Marker.Position.X);Assert.Greater(later.Age,shot.Age);Assert.AreNotEqual(later.Position.X,shot.Position.X);
            Combat(60);Assert.IsEmpty(View().Projectiles.Where(x=>x.Kind==PlayableEntityKind.Shkval));
        }
        [Test]public void ExplicitShotOverridesFriendlyPenaltyAndAoEDamagesBoth()
        {
            int id=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-5,0),friend=Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,-5,1.5);
            Set(friend,"Reload",999d);Set(target,"Reload",999d);Combat(60);Assert.IsEmpty(View().Projectiles);Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Attack,id,target:target));Combat(45);
            Assert.AreEqual(p.TankHealth-p.ShkvalDamage,Get<double>(friend,"Health"));Assert.AreEqual(p.TankHealth-p.ShkvalDirectHitDamage,Get<double>(target,"Health"));
        }
        [Test]public void HiddenEntitiesCannotChangeAutomaticChoice()
        {
            int id=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);Call("RefreshVision");
            int before=(int)Call("ArtilleryTarget",Units[id],new NavPoint(-12,0));Assert.AreEqual(target,before);
            Spawn(PlayableOwner.Enemy,PlayableEntityKind.Shkval,20,20);Call("AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(20,20),true);Call("RefreshVision");
            Assert.AreEqual(before,Call("ArtilleryTarget",Units[id],new NavPoint(-12,0)));
        }
        [Test]public void MissileAndMarkerVisibilityAreIndependentAndImmutable()
        {
            Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0);int target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);Set(target,"Reload",999d);Combat(55);
            var list=(IList)Domain.GetField("projectiles",Flags).GetValue(domain);var shot=list.Cast<object>().Single(x=>x.GetType().GetField("Rocket").GetValue(x)!=null);var rocket=shot.GetType().GetField("Rocket").GetValue(shot);
            var saved=View().Projectiles.Single();rocket.GetType().GetField("Predicted").SetValue(rocket,new BallisticContact(1,new BallisticPoint(25,0,25)));
            var enemy=View(PlayableOwner.Enemy).Projectiles.Single();Assert.True(enemy.Visible);Assert.Null(enemy.Marker);Assert.NotNull(saved.Marker);Assert.Less(saved.Marker.Position.X,0);
            rocket.GetType().GetField("Predicted").SetValue(rocket,new BallisticContact(1,new BallisticPoint(-4,0,0)));shot.GetType().GetField("Position").SetValue(shot,new NavPoint(25,25));
            enemy=View(PlayableOwner.Enemy).Projectiles.Single();Assert.False(enemy.Visible);Assert.NotNull(enemy.Marker);Assert.AreEqual(0,enemy.Position.X);Assert.AreEqual(0,enemy.TargetId);Assert.AreEqual(0,enemy.OwnerId);
        }
        [Test]public void BlockedShkvalExitRetainsReservationAndCancellationRefunds()
        {
            int f=Factory();var buildings=(IDictionary)Domain.GetField("buildings",Flags).GetValue(domain);
            // Preserve the all-exits blocked contract after introducing alternatives.
            var exits=(NavPoint[])Call("FactoryExitCandidates",buildings[f],PlayableEntityKind.Shkval);
            foreach(var exit in exits)if(Nav.Crowd.CanPlace(exit,p.ShkvalCollisionRadius))Spawn(PlayableOwner.Player,PlayableEntityKind.Explorer,exit.X,exit.Z);
            Assert.False(exits.Any(exit=>Nav.Crowd.CanPlace(exit,p.ShkvalCollisionRadius)));
            int credits=View().Credits;Send(PlayableCommandKind.QueueShkval,f);Call("AdvanceProduction",100d);var q=View().Buildings.Single(b=>b.Id==f).PrivateState.Orders.Single();Assert.True(q.Active);Assert.Zero(q.Remaining);Assert.AreEqual(p.ShkvalPopulationCost,View().Population.Reserved);Assert.False(View().Entities.Any(e=>e.Kind==PlayableEntityKind.Shkval));
            Send(PlayableCommandKind.CancelProductionOrder,f,order:q.Id);Assert.AreEqual(credits,View().Credits);Assert.Zero(View().Population.Reserved);
        }
        private object FieldObject(object value,string field)=>value.GetType().GetField(field).GetValue(value);
        private void FieldSet(object value,string field,object data)=>value.GetType().GetField(field).SetValue(value,data);
        private object InjectRocket(int source,int target,BallisticFlight flight,double splash=10,double direct=20)
        {
            var shot=Activator.CreateInstance(Domain.GetNestedType("Projectile",BindingFlags.NonPublic),true);
            var rocket=Activator.CreateInstance(Domain.GetNestedType("Rocket",BindingFlags.NonPublic),true);
            foreach(var pair in new[]{("Flight",(object)flight),("Radius",(object)p.ShkvalProjectileRadius),("BlastRadius",(object)p.ShkvalBlastRadius),("BuildingHeight",(object)p.ShkvalBuildingCollisionHeight),("DirectDamage",(object)direct)})FieldSet(rocket,pair.Item1,pair.Item2);
            foreach(var pair in new[]{("Rocket",rocket),("Id",(object)1),("Owner",(object)source),("Target",(object)target),("Faction",(object)PlayableOwner.Player),("Kind",(object)PlayableEntityKind.Shkval),("Damage",(object)splash)})FieldSet(shot,pair.Item1,pair.Item2);
            return shot;
        }
        [TestCase(false)][TestCase(true)]public void PhysicalBodyGetsDirectTotalAndNeighborGetsSplashIncludingLethalGeometry(bool lethal)
        {
            int source=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),contact=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0),neighbor=Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,-4,1.5);
            if(lethal)Set(contact,"Health",15d);
            // The assigned target is the neighbor: contact identity must come from physical collision.
            var shot=InjectRocket(source,neighbor,new BallisticFlight(new BallisticPoint(-8,.5,0),new BallisticPoint(0,.5,0),0,10));
            Assert.True((bool)Call("AdvanceRocket",shot,2d));
            if(lethal){Assert.False(Units.Contains(contact));var wrecks=(IDictionary)Domain.GetField("wrecks",Flags).GetValue(domain);Assert.True(wrecks.Contains(contact));}
            else Assert.AreEqual(p.TankHealth-20,Get<double>(contact,"Health"));
            Assert.AreEqual(p.TankHealth-10,Get<double>(neighbor,"Health"));
        }
        [Test]public void GroundImpactUsesOnlySplashAndUnitsDoNotShieldBlast()
        {
            int source=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),near=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,1.2),far=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,-1.2);
            var shot=InjectRocket(source,near,new BallisticFlight(new BallisticPoint(-8,4,0),new BallisticPoint(-4,0,0),0,10));
            Assert.True((bool)Call("AdvanceRocket",shot,2d));Assert.AreEqual(p.TankHealth-10,Get<double>(near,"Health"));Assert.AreEqual(p.TankHealth-10,Get<double>(far,"Health"));
        }
        [Test]public void CenterPrerequisitePreservesPaidQueueAndBlocksRepeatPayment()
        {
            int f=Factory();var buildings=(IDictionary)Domain.GetField("buildings",Flags).GetValue(domain);
            int science=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            FieldSet(buildings[science],"Ready",false);FieldSet(buildings[science],"Phase",ConstructionPhase.Constructing);Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.QueueShkval,f));
            FieldSet(buildings[science],"Ready",true);FieldSet(buildings[science],"Phase",ConstructionPhase.Ready);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.QueueShkval,f));
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ToggleRepeatProduction,f,PlayableEntityKind.Shkval));
            buildings.Remove(science);int credits=View().Credits;
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.QueueShkval,f));Assert.AreEqual(credits,View().Credits);
            Call("AdvanceProduction",1d);Assert.True(View().Buildings.Single(b=>b.Id==f).PrivateState.Orders.Single().Active);
            var order=View().Buildings.Single(b=>b.Id==f).PrivateState.Orders.Single();Send(PlayableCommandKind.CancelProductionOrder,f,order:order.Id);
            credits=View().Credits;Call("AdvanceProduction",100d);Assert.IsEmpty(View().Buildings.Single(b=>b.Id==f).PrivateState.Orders);Assert.AreEqual(credits,View().Credits);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.ToggleRepeatProduction,f,PlayableEntityKind.Shkval));
            Call("AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.ScientificCenter,new NavPoint(20,15),true);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Send(PlayableCommandKind.QueueShkval,f),"Enemy center is insufficient");
        }
        [TestCase(false)][TestCase(true)]public void BuildingDirectHitUsesIndividualLethalityAndCoverSnapshot(bool lethal)
        {
            int source=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),behind=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-1,0);
            Call("AddCredits",PlayableOwner.Enemy,10000d);int center=View(PlayableOwner.Enemy).Buildings.Single(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Headquarters).Id;
            int site=(int)Call("HomeSite",PlayableOwner.Enemy);int slot=TerritoryRules.Sites(p).Single(s=>s.Id==site).Slots[0].Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",site,slot,PlayableBuildingKind.Factory,center,PlayableOwner.Enemy,null));
            // Use the existing authored building identity, move its combat pose into this isolated firing lane.
            var buildings=(IDictionary)Domain.GetField("buildings",Flags).GetValue(domain);var building=buildings.Values.Cast<object>().Single(b=>(PlayableOwner)FieldObject(b,"Owner")==PlayableOwner.Enemy&&(PlayableBuildingKind)FieldObject(b,"Kind")==PlayableBuildingKind.Factory);
            int id=(int)FieldObject(building,"Id");FieldSet(building,"Position",new NavPoint(-4,0));FieldSet(building,"Ready",true);FieldSet(building,"Phase",ConstructionPhase.Ready);FieldSet(building,"Health",lethal?15d:100d);
            var shot=InjectRocket(source,behind,new BallisticFlight(new BallisticPoint(-10,1,0),new BallisticPoint(2,1,0),0,10));
            Assert.True((bool)Call("AdvanceRocket",shot,2d));
            if(lethal)Assert.False(buildings.Contains(id));else Assert.AreEqual(80d,FieldObject(building,"Health"));
            Assert.AreEqual(p.TankHealth,Get<double>(behind,"Health"),"Cover stays effective for this explosion even when destroyed");
        }
        [TestCase(PlayableResearchKind.ImprovedWeaponSystems)]
        [TestCase(PlayableResearchKind.AdvancedWeaponSystems)]
        [TestCase(PlayableResearchKind.UltimateWeaponSystems)]
        public void WeaponResearchRoundsBothLaunchDamagesIndependently(PlayableResearchKind kind)
        {
            var order=Activator.CreateInstance(Domain.GetNestedType("ResearchOrder",BindingFlags.NonPublic),true);FieldSet(order,"Kind",kind);FieldSet(order,"Complete",true);((IList)Call("Research",PlayableOwner.Player)).Add(order);
            int gun=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);Set(target,"Reload",999d);Combat(55);
            var shot=((IList)Domain.GetField("projectiles",Flags).GetValue(domain)).Cast<object>().Single(x=>FieldObject(x,"Rocket")!=null);
            double multiplier=1+PlayableResearchRules.Bonus(p,kind)/100;
            Assert.AreEqual(PlayableStatRounding.Damage(p,10*multiplier),FieldObject(shot,"Damage"));Assert.AreEqual(PlayableStatRounding.Damage(p,20*multiplier),FieldObject(FieldObject(shot,"Rocket"),"DirectDamage"));
        }
        [Test]public void DirectDamageProfileMetadataAndFrozenLaunchRoundtrip()
        {
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="artillery.directHitDamage");var data=p.CopyData();NativeBalanceFields.Write(field,data,35);data.revision++;
            var changed=PlayableProfile.Create(data);Assert.AreEqual(35,changed.ShkvalDirectHitDamage);Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(field,data,1001));
            Call("ApplyBalance",changed);
            int gun=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);Set(target,"Reload",999d);Combat(55);
            var shots=(IList)Domain.GetField("projectiles",Flags).GetValue(domain);var shot=shots.Cast<object>().Single(x=>FieldObject(x,"Rocket")!=null);
            Assert.AreEqual(35d,FieldObject(FieldObject(shot,"Rocket"),"DirectDamage"));
            data=changed.CopyData();data.revision++;data.shkvalDirectHitDamage=55;changed=PlayableProfile.Create(data);
            // Live balance changes affect the next shot, never either damage on this one.
            Call("ApplyBalance",changed);
            byte[] bytes=(byte[])Call("CaptureWorldBytes",7,"shkval");
            var restored=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,changed,7,"shkval"});
            var restoredShots=(IList)Domain.GetField("projectiles",Flags).GetValue(restored);var restoredShot=restoredShots.Cast<object>().Single(x=>FieldObject(x,"Rocket")!=null);
            Assert.AreEqual(35d,FieldObject(FieldObject(restoredShot,"Rocket"),"DirectDamage"));
            var terms=(System.Collections.Generic.IDictionary<int,PlayableProfile>)Domain.GetField("termProfiles",Flags).GetValue(restored);Assert.AreEqual(35,terms[changed.Revision-1].ShkvalDirectHitDamage);
            CollectionAssert.AreEqual(bytes,(byte[])Domain.GetMethod("CaptureWorldBytes",Flags).Invoke(restored,new object[]{7,"shkval"}));
            Call("AdvanceProjectiles",2d);Domain.GetMethod("AdvanceProjectiles",Flags).Invoke(restored,new object[]{2d});
            CollectionAssert.AreEqual((byte[])Call("CaptureWorldBytes",7,"shkval"),(byte[])Domain.GetMethod("CaptureWorldBytes",Flags).Invoke(restored,new object[]{7,"shkval"}));
        }
        private void CompleteGuidance(int center)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("QueueResearch",center,PlayableOwner.Player,PlayableResearchKind.ShkvalGuidance,null));Call("AdvanceResearch",100d);
            Assert.True(View().OwnerResearch.Single(o=>o.Kind==PlayableResearchKind.ShkvalGuidance).Complete);
        }
        [TestCase(false)][TestCase(true)]public void BlastRadiusFreezesAcrossResearchProfileChangeAndSave(bool initiallyGuided)
        {
            Factory();int center=View().Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter).Id;if(initiallyGuided)CompleteGuidance(center);
            int gun=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);Set(target,"Reload",999d);Combat(55);
            var shots=(IList)Domain.GetField("projectiles",Flags).GetValue(domain);var shot=shots.Cast<object>().Single(x=>FieldObject(x,"Rocket")!=null);var rocket=FieldObject(shot,"Rocket");
            double expected=initiallyGuided?1.4:1.12;Assert.AreEqual(expected,(double)FieldObject(rocket,"BlastRadius"),1e-12);Assert.AreEqual(initiallyGuided,((ShkvalLaunchSnapshot)FieldObject(rocket,"Launch")).Upgraded);
            var marker=View().Projectiles.Single().Marker;Assert.NotNull(marker);var flight=(BallisticFlight)FieldObject(rocket,"Flight");double progress=Math.Min(1,(double)FieldObject(rocket,"Elapsed")/flight.Duration/Math.Max(1e-5,((BallisticContact)FieldObject(rocket,"Predicted")).Progress));
            Assert.AreEqual(p.ShkvalMarkerStartRadius+(expected-p.ShkvalMarkerStartRadius)*progress,marker.Radius,1e-12);
            if(!initiallyGuided)CompleteGuidance(center);
            var data=p.CopyData();data.revision++;data.shkvalBaseBlastRadiusMultiplier=.6;data.shkvalBlastRadius=1.8;var changed=PlayableProfile.Create(data);Call("ApplyBalance",changed);
            byte[] bytes=(byte[])Call("CaptureWorldBytes",7,"blast");var restored=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,changed,7,"blast"});
            try{
                CollectionAssert.AreEqual(bytes,(byte[])Domain.GetMethod("CaptureWorldBytes",Flags).Invoke(restored,new object[]{7,"blast"}));
                var terms=(System.Collections.Generic.IDictionary<int,PlayableProfile>)Domain.GetField("termProfiles",Flags).GetValue(restored);Assert.AreEqual(.8,terms[p.Revision].ShkvalBaseBlastRadiusMultiplier);Assert.AreEqual(.6,terms[changed.Revision].ShkvalBaseBlastRadiusMultiplier);
                Call("AdvanceProjectiles",2d);Domain.GetMethod("AdvanceProjectiles",Flags).Invoke(restored,new object[]{2d});Assert.AreEqual(expected,View().Impacts.Single().Radius,1e-12);
                CollectionAssert.AreEqual((byte[])Call("CaptureWorldBytes",7,"blast"),(byte[])Domain.GetMethod("CaptureWorldBytes",Flags).Invoke(restored,new object[]{7,"blast"}));
            }finally{var nav=(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(restored);nav.ReleaseCommittedHostRoot();Domain.GetMethod("DestroyOwnedNavigation",Flags).Invoke(restored,null);}
            Set(gun,"Reload",0d);Set(gun,"StoppedSeconds",10d);Align(gun,target);Call("AdvanceIndexedCombat",0d);
            var next=shots.Cast<object>().Single(x=>FieldObject(x,"Rocket")!=null);Assert.AreEqual(1.8,(double)FieldObject(FieldObject(next,"Rocket"),"BlastRadius"),1e-12,"Next upgraded shot uses the new full radius");
        }
        [TestCase(false)][TestCase(true)]public void EdgeVictimRequiresExpandedRadius(bool guided)
        {
            int gun=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0),victim=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,1.95);
            var shot=InjectRocket(gun,victim,new BallisticFlight(new BallisticPoint(-8,4,0),new BallisticPoint(-4,0,0),0,10));FieldSet(FieldObject(shot,"Rocket"),"BlastRadius",PlayableUnitRules.ShkvalBlastRadius(p,guided));
            Assert.True((bool)Call("AdvanceRocket",shot,2d));Assert.AreEqual(p.TankHealth-(guided?10:0),Get<double>(victim,"Health"));
        }
        [Test]public void PreviouslyLaunchedFullRadiusRemainsValidAndMetadataBoundsAreShared()
        {
            var descriptor=PlayableProfileMetadata.Fields.Single(f=>f.Path=="artillery.baseBlastRadiusMultiplier");var data=p.CopyData();Assert.AreEqual(.8,descriptor.Read(data));Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(descriptor,data,.05));Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(descriptor,data,1.01));
            Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,0);int target=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-4,0);Set(target,"Reload",999d);Combat(55);
            var shot=((IList)Domain.GetField("projectiles",Flags).GetValue(domain)).Cast<object>().Single(x=>FieldObject(x,"Rocket")!=null);var rocket=FieldObject(shot,"Rocket");FieldSet(rocket,"UsesBlastUpgrade",false);FieldSet(rocket,"BlastRadius",p.ShkvalBlastRadius);
            byte[] bytes=(byte[])Call("CaptureWorldBytes",7,"prior-blast");var restored=Domain.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,p,7,"prior-blast"});
            try{CollectionAssert.AreEqual(bytes,(byte[])Domain.GetMethod("CaptureWorldBytes",Flags).Invoke(restored,new object[]{7,"prior-blast"}));Call("AdvanceProjectiles",2d);Assert.AreEqual(1.4,View().Impacts.Single().Radius);}
            finally{var nav=(NavigationSession)Domain.GetProperty("Navigation",Flags).GetValue(restored);nav.ReleaseCommittedHostRoot();Domain.GetMethod("DestroyOwnedNavigation",Flags).Invoke(restored,null);}
        }
        [Test]public void ShkvalRouteUsesItsOwnRadiusAndSpeed()
        {
            int id=Spawn(PlayableOwner.Player,PlayableEntityKind.Shkval,-12,8);Assert.True(Nav.Move(id,new NavPoint(-8,8)));Assert.True(Nav.Requests.TryDequeue(out var r));Assert.AreEqual(p.ShkvalCollisionRadius,r.Profile.Radius);Assert.AreEqual(p.ShkvalSpeed,r.Profile.TankSpeed);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class SentryTurretTests
    {
        static PlayableProfile MapProfile(string map)=>map=="foundry"?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(new FoundryProfileData())):map=="basalt"?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new BasaltCrownMap()):map=="moss"?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap()):map=="crossings"?PlayableProfile.Create(PlayableProfile.Default.CopyData(),ThreeCrossingsMap.Default):map=="meridian"?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new WhiteMeridianMap()):map=="grove"?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CrimsonGroveMap()):map=="nereida"?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new NereidaMap(new NereidaProfileData())):map=="proving"?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new ProvingGroundMap()):PlayableProfile.Default;
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        readonly List<PlayableDomain> owned=new List<PlayableDomain>();
        object Call(PlayableDomain d,string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
        PlayableDomain New(PlayableProfile p=null){var d=new PlayableDomain(p??PlayableProfile.Default,813,false);owned.Add(d);Call(d,"AddCredits",PlayableOwner.Player,10000d);return d;}
        PlayableSnapshot View(PlayableDomain d)=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        int Build(PlayableDomain d,PlayableBuildingKind kind,int slot)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"BuildAt",1,slot,kind,1,PlayableOwner.Player,null));
            Call(d,"AdvanceFoundations");Call(d,"AdvanceBuildings",100d);
            var b=View(d).Buildings.Single(x=>x.SiteId==1&&x.SlotId==slot);Assert.AreEqual(ConstructionPhase.Ready,b.Phase);return b.Id;
        }
        int Enemy(PlayableDomain d,NavPoint point,PlayableEntityKind kind=PlayableEntityKind.Tank)
        {int id=(int)Call(d,"SpawnUnit",point,PlayableOwner.Enemy,kind);if(kind!=PlayableEntityKind.Hawk)d.Navigation.Crowd.Stop(id,true);return id;}
        void Step(PlayableDomain d,int ticks=1){for(int i=0;i<ticks;i++)d.Step(1d/30);}
        [TearDown] public void Cleanup(){foreach(var d in owned){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);}owned.Clear();}

        [TestCase("default")][TestCase("foundry")][TestCase("basalt")][TestCase("moss")][TestCase("crossings")][TestCase("meridian")][TestCase("grove")][TestCase("nereida")][TestCase("proving")] public void SmallDefenseSlotsPreserveOrdinarySlotsAndFullBaseClearance(string map)
        {
            var p=MapProfile(map);TerritoryRules.ValidateArena(p);
            foreach(var site in TerritoryRules.Sites(p).Where(s=>TerritoryRules.Center(s.Kind)))
            {
                var ordinary=site.Slots.Select(s=>(s.Id,s.Position)).ToArray();var defense=site.DefenseSlots(p);
                Assert.AreEqual(site.Slots.Count,defense.Count);Assert.True(defense.All(s=>s.Id>1000));
                Assert.That(site.Slots.Select(s=>(s.Id,s.Position)).ToArray(),Is.EqualTo(ordinary));
                Assert.True(((IList<TerritorySlot>)defense).IsReadOnly);
                double ring=site.Slots.Average(s=>Math.Sqrt(Math.Pow(s.Position.X-site.Position.X,2)+Math.Pow(s.Position.Z-site.Position.Z,2)))+p.TurretPadOffset;
                foreach(var pad in defense)
                    Assert.AreEqual(ring,Math.Sqrt(Math.Pow(pad.Position.X-site.Position.X,2)+Math.Pow(pad.Position.Z-site.Position.Z,2)),1e-9,map+"/"+site.Id+"/"+pad.Id+" common radius");
                foreach(var pad in defense)foreach(var slot in site.Slots)
                {
                    // Option A changes the placement metric, not the required physical gap.
                    double dx=Math.Max(0,Math.Abs(pad.Position.X-slot.Position.X)-1.45-p.TurretFootprintRadius);
                    double dz=Math.Max(0,Math.Abs(pad.Position.Z-slot.Position.Z)-1.45-p.TurretFootprintRadius);
                    Assert.GreaterOrEqual(Math.Sqrt(dx*dx+dz*dz),p.TurretPassageWidth-1e-9,map+"/"+site.Id+"/"+pad.Id+" envelope clearance");
                }
                var obstacles=PlayableMap.StaticObstacles(p).Concat(site.Slots.Select(s=>new NavObstacle(s.Position.X-1.45,s.Position.Z-1.45,s.Position.X+1.45,s.Position.Z+1.45)))
                    .Concat(defense.Select(s=>new NavObstacle(s.Position.X-p.TurretFootprintRadius,s.Position.Z-p.TurretFootprintRadius,s.Position.X+p.TurretFootprintRadius,s.Position.Z+p.TurretFootprintRadius))).ToArray();
                var geometry=new NavGeometry(p.ArenaHalfExtent,obstacles,1);
                foreach(var slot in site.Slots)Assert.True(geometry.IsFree(new NavPoint(slot.Position.X,slot.Position.Z-p.FactoryExitDistance),p.TankCollisionRadius),"South exit must remain free");
            }
        }
        [Test] public void StandardLeftPlotMovesInwardToTheUnchangedCommonRing()
        {
            var p=PlayableProfile.Default;var home=TerritoryRules.Sites(p).Single(s=>s.Id==1);
            var left=home.DefenseSlots(p).Single(s=>s.Id==1003);
            Assert.AreEqual(home.Position.X-p.SlotRingRadius-p.TurretPadOffset,left.Position.X,1e-9);
            Assert.AreEqual(home.Position.Z,left.Position.Z,1e-9);
        }
        [TestCase("turret.passageWidth")][TestCase("turret.footprintRadius")]
        public void IncreasedProfileConstraintsExpandTheWholeRingWithoutLosingPassage(string path)
        {
            var original=PlayableProfile.Default;var data=original.CopyData();
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path==path);
            NativeBalanceFields.Write(field,data,field.Read(data)+field.Step);
            var p=PlayableProfile.Create(data);
            foreach(var home in TerritoryRules.Sites(p).Where(s=>s.Kind==PlayableBuildingKind.Headquarters))
            {
                var pads=home.DefenseSlots(p);double ring=Distance(pads[0].Position,home.Position);
                Assert.Greater(ring,p.SlotRingRadius+p.TurretPadOffset,"The valid neighboring profile must expand the common ring");
                foreach(var pad in pads)
                {
                    Assert.AreEqual(ring,Distance(pad.Position,home.Position),1e-9,"No individual radial displacement");
                    foreach(var ordinary in home.Slots)
                    {
                        double dx=Math.Max(0,Math.Abs(pad.Position.X-ordinary.Position.X)-1.45-p.TurretFootprintRadius);
                        double dz=Math.Max(0,Math.Abs(pad.Position.Z-ordinary.Position.Z)-1.45-p.TurretFootprintRadius);
                        Assert.GreaterOrEqual(Math.Sqrt(dx*dx+dz*dz),p.TurretPassageWidth-1e-9);
                    }
                }
            }
        }
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
        [TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Shkval)]
        public void EveryFactoryExitConnectsToExteriorWhenEveryPlotIsBuilt(PlayableEntityKind kind)
        {
            var p=PlayableProfile.Default;
            foreach(var site in TerritoryRules.Sites(p).Where(s=>TerritoryRules.Center(s.Kind)))
            {
                double r=PlayableUnitRules.Radius(p,kind),h=TerritoryRules.Radius(p,site.Kind);
                var obstacles=PlayableMap.StaticObstacles(p).Concat(new[]{new NavObstacle(site.Position.X-h,site.Position.Z-h,site.Position.X+h,site.Position.Z+h)})
                    .Concat(site.Slots.Select(s=>new NavObstacle(s.Position.X-1.45,s.Position.Z-1.45,s.Position.X+1.45,s.Position.Z+1.45)))
                    .Concat(site.DefenseSlots(p).Select(s=>new NavObstacle(s.Position.X-p.TurretFootprintRadius,s.Position.Z-p.TurretFootprintRadius,s.Position.X+p.TurretFootprintRadius,s.Position.Z+p.TurretFootprintRadius))).ToArray();
                var geometry=new NavGeometry(p.ArenaHalfExtent,obstacles,1);
                using(var router=new UnityNavigationRouter(geometry,p.Navigation.ForUnit(r,PlayableUnitRules.Speed(p,kind),p.Navigation.TankTurnSpeed)))
                foreach(var slot in site.Slots)
                {
                    var start=new NavPoint(slot.Position.X,slot.Position.Z-p.FactoryExitDistance);
                    var goal=new NavPoint(site.Position.X,site.Position.Z-11);
                    var path=router.FindPath(start,goal);Assert.IsNotEmpty(path,site.Id+"/"+slot.Id+" "+kind);
                    var previous=start;foreach(var point in path){Assert.True(geometry.SegmentFree(previous,point,r));previous=point;}Assert.AreEqual(goal,previous);
                }
            }
        }
        [TestCase("foundry")][TestCase("basalt")][TestCase("moss")][TestCase("crossings")][TestCase("meridian")][TestCase("grove")][TestCase("nereida")][TestCase("proving")]
        public void AuthoredFactoryExitsKeepSupportedPathsAcrossFullyBuiltBase(string map)
        {
            var p=MapProfile(map);double r=p.TankCollisionRadius;Assert.AreEqual(r,p.ShkvalCollisionRadius);
            foreach(var site in TerritoryRules.Sites(p).Where(s=>TerritoryRules.Center(s.Kind)))
            {
                double h=TerritoryRules.Radius(p,site.Kind);
                var obstacles=PlayableMap.StaticObstacles(p).Concat(new[]{new NavObstacle(site.Position.X-h,site.Position.Z-h,site.Position.X+h,site.Position.Z+h)})
                    .Concat(site.Slots.Select(s=>new NavObstacle(s.Position.X-1.45,s.Position.Z-1.45,s.Position.X+1.45,s.Position.Z+1.45)))
                    .Concat(site.DefenseSlots(p).Select(s=>new NavObstacle(s.Position.X-p.TurretFootprintRadius,s.Position.Z-p.TurretFootprintRadius,s.Position.X+p.TurretFootprintRadius,s.Position.Z+p.TurretFootprintRadius))).ToArray();
                var geometry=new NavGeometry(p.ArenaHalfExtent,obstacles,1);
                using(var router=new UnityNavigationRouter(geometry,p.Navigation.ForUnit(r,p.TankSpeed,p.Navigation.TankTurnSpeed)))
                foreach(var slot in site.Slots)
                {
                    var start=new NavPoint(slot.Position.X,slot.Position.Z-p.FactoryExitDistance);var goal=new NavPoint(site.Position.X,site.Position.Z-4);
                    Assert.True(p.AuthoredMap.TryLocate(start,r,out var location),map+"/"+site.Id+"/"+slot.Id+" exit support");
                    var path=router.FindPath(start,goal);Assert.IsNotEmpty(path,map+"/"+site.Id+"/"+slot.Id+" continuous path");
                    var previous=start;foreach(var point in path){Assert.True(geometry.SegmentFree(previous,point,r));Assert.True(p.AuthoredMap.TryTraverse(location,point,r,out var next),map+"/"+site.Id+"/"+slot.Id+" supported traversal");location=next;previous=point;}Assert.AreEqual(goal,previous);
                }
            }
        }
        [Test] public void OnlyTurretBuildsOnDefensePlotAndItCannotConsumeOrdinaryPlot()
        {
            var d=New();
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Call(d,"BuildAt",1,1001,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Call(d,"BuildAt",1,1,PlayableBuildingKind.SentryTurret,1,PlayableOwner.Player,null));
            Build(d,PlayableBuildingKind.SentryTurret,1001);
            Assert.AreEqual(PlayableCommandStatus.OccupiedPad,Call(d,"BuildAt",1,1001,PlayableBuildingKind.SentryTurret,1,PlayableOwner.Player,null));
        }
        [Test] public void IdleTurretReturnsYawAndPitchAtProfileSpeed()
        {
            var d=New();int id=Build(d,PlayableBuildingKind.SentryTurret,1001);
            var buildings=(System.Collections.IDictionary)typeof(PlayableDomain).GetField("buildings",F).GetValue(d);
            var building=buildings[id];var field=building.GetType().GetField("Weapon");
            var state=new SentryTurretState{Heading=-.7,Pitch=.8,Target=int.MaxValue,Remaining=2};field.SetValue(building,state);
            Call(d,"AdvanceTurrets",1d/30);
            Assert.AreEqual(-.7+PlayableProfile.Default.TurretYawSpeed*Math.PI/180/30,state.Heading,1e-10);
            Assert.AreEqual(.8-PlayableProfile.Default.TurretPitchSpeed*Math.PI/180/30,state.Pitch,1e-10);
            Assert.True(state.Turning);Assert.Zero(state.Target);Assert.Zero(state.Remaining);Assert.Zero(state.ShotSequence);
            for(int i=0;i<120;i++)Call(d,"AdvanceTurrets",1d/30);
            Assert.AreEqual(Math.PI/2,state.Heading,1e-10);Assert.Zero(state.Pitch);Assert.False(state.Turning);
        }
        [Test] public void FirstTargetStaysUntilDeathThenNearestWinsAndManualAttackOverrides()
        {
            var d=New();int turret=Build(d,PlayableBuildingKind.SentryTurret,1001);var at=View(d).Buildings.Single(b=>b.Id==turret).Position;
            int first=Enemy(d,new NavPoint(at.X,at.Z+4));Step(d);
            Assert.AreEqual(first,View(d).Buildings.Single(b=>b.Id==turret).Turret.TargetId);
            int near=Enemy(d,new NavPoint(at.X,at.Z+2));int other=Enemy(d,new NavPoint(at.X+3,at.Z+2));Step(d);
            Assert.AreEqual(first,View(d).Buildings.Single(b=>b.Id==turret).Turret.TargetId,"Closer entrant must not steal lock");
            Call(d,"DamageWithOwner",first,10000d,0,(PlayableOwner?)PlayableOwner.Player);Step(d);
            Assert.AreEqual(near,View(d).Buildings.Single(b=>b.Id==turret).Turret.TargetId,"Nearest remaining enemy wins after death");
            Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(813,1,"player-1",PlayableCommandKind.Attack,new[]{turret},targetId:other),out _));Step(d);
            Assert.AreEqual(other,View(d).Buildings.Single(b=>b.Id==turret).Turret.TargetId);
            Assert.AreEqual(PlayableCommandStatus.Rejected,d.Apply(new PlayableCommand(813,2,"player-1",PlayableCommandKind.Move,new[]{turret},new NavPoint(at.X+5,at.Z)),out _));
        }
        [Test] public void TurretTargetsAirAndCanReachVerticalElevationWithoutMovingBase()
        {
            var p=PlayableProfile.Default;var d=New(p);
            foreach(var unit in View(d).Entities.Where(e=>e.Owner==PlayableOwner.Player).ToArray())Call(d,"Damage",unit.Id,10000);
            Assert.False(View(d).Entities.Any(e=>e.Owner==PlayableOwner.Player),"Turret must be the only friendly weapon in this causal fixture");
            int turret=Build(d,PlayableBuildingKind.SentryTurret,1001);var at=View(d).Buildings.Single(b=>b.Id==turret).Position;
            int air=Enemy(d,at,PlayableEntityKind.Hawk);bool physicalContact=false;
            for(int tick=0;tick<60;tick++){Step(d);physicalContact|=View(d).Sounds.Any(e=>e.Kind==PlayableSoundKind.Impact&&e.Weapon==PlayableEntityKind.Explorer&&e.Contact?.TargetId==air);}
            Assert.True(physicalContact,"Turret must cause an actual machine-gun contact on the aircraft without mobile weapon support");
            var b=View(d).Buildings.Single(x=>x.Id==turret);Assert.AreEqual(at,b.Position);Assert.AreEqual(air,b.Turret.TargetId);
            Assert.Greater(b.Turret.Pitch,Math.PI*.45);Assert.Greater(b.Turret.ShotSequence,0);
            Assert.Less(View(d).Entities.Single(e=>e.Id==air).Health,p.HawkHealth,"Physical anti-air shots must reduce the selected aircraft health");
        }
        void Research(PlayableDomain d,int center,PlayableResearchKind kind)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"QueueResearch",center,PlayableOwner.Player,kind,null));
            Call(d,"AdvanceResearch",1000d);
        }
        [Test] public void WeaponSystemsBuffDamageArmyArmorDoesNotBuffTurretAndConcreteDoes()
        {
            var p=PlayableProfile.Default;var d=New(p);Build(d,PlayableBuildingKind.Factory,5);int turret=Build(d,PlayableBuildingKind.SentryTurret,1001);int science=Build(d,PlayableBuildingKind.ScientificCenter,3);
            double original=View(d).Buildings.Single(b=>b.Id==turret).MaxHealth;
            Research(d,science,PlayableResearchKind.ImprovedDefensiveSystems);Assert.AreEqual(original,View(d).Buildings.Single(b=>b.Id==turret).MaxHealth);
            Research(d,science,PlayableResearchKind.ReinforcedConcrete);var upgraded=View(d).Buildings.Single(b=>b.Id==turret);
            Assert.AreEqual(original*(1+p.ReinforcedConcreteBonusPercent/100),upgraded.MaxHealth,1e-9);
            Research(d,science,PlayableResearchKind.ImprovedWeaponSystems);
            var at=upgraded.Position;Enemy(d,new NavPoint(at.X,at.Z+5));Step(d);
            var records=(IEnumerable)typeof(PlayableDomain).GetField("projectiles",F).GetValue(d);
            var shot=records.Cast<object>().First(x=>(int)x.GetType().GetField("Owner").GetValue(x)==turret);
            double damage=(double)shot.GetType().GetField("Damage").GetValue(shot);
            Assert.Greater(damage,p.ExplorerDamage);Assert.AreEqual(1,View(d).Buildings.Single(b=>b.Id==turret).Turret.WeaponLevel);
        }
        [Test] public void RepairSaleAndParentDestructionUseBuildingRules()
        {
            var d=New();int turret=Build(d,PlayableBuildingKind.SentryTurret,1001);Call(d,"Damage",turret,60);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Call(d,"StartRepair",turret,PlayableOwner.Player,null));
            Step(d,180);Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"StartRepair",turret,PlayableOwner.Player,null));
            Call(d,"AdvanceBuildingLifecycle",30d);Assert.AreEqual(300,View(d).Buildings.Single(b=>b.Id==turret).Health);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"SellBuilding",turret,PlayableOwner.Player,null));
            Assert.True(View(d).Buildings.Single(b=>b.Id==turret).PrivateState.Lifecycle.Selling);Call(d,"AdvanceBuildingLifecycle",3d);Assert.False(View(d).Buildings.Any(b=>b.Id==turret));
            int second=Build(d,PlayableBuildingKind.SentryTurret,1002);Call(d,"Damage",1,10000);Assert.False(View(d).Buildings.Any(b=>b.Id==second));
        }
        [Test] public void ProvingGroundTurretUsesDefensivePadAndCaptureRecipeKeepsItsBinding()
        {
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new ProvingGroundMap());var setup=new ProvingGroundSetup(p);
            var site=p.AuthoredMap.Sites(p).First(s=>s.Id==1);var pad=site.DefenseSlots(p)[0];
            Assert.IsNull(setup.PlaceBuilding(0,PlayableBuildingKind.SentryTurret,pad.Position));
            Assert.AreEqual(pad.Id,setup.Buildings.Single().SlotId);
            var d=new PlayableDomain(p,813,setup.Configuration());owned.Add(d);
            var captured=(ProvingGroundSetup)Call(d,"CaptureProvingSetup");Assert.AreEqual(pad.Id,captured.Buildings.Single().SlotId);Assert.DoesNotThrow(()=>captured.Configuration());
        }
        [Test] public void EveryTurretProfileFieldParticipatesInCurrentWorldBinding()
        {
            var p=PlayableProfile.Default;var binding=WorldWire.Binding(p);
            foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path.StartsWith("turret.")))
            {
                var data=p.CopyData();double current=field.Read(data),changed=current+field.Step<=field.Maximum?current+field.Step:current-field.Step;
                NativeBalanceFields.Write(field,data,changed);Assert.That(WorldWire.Binding(PlayableProfile.Create(data)),Is.Not.EqualTo(binding),field.Path);
            }
            foreach(var field in PlayableProfileMetadata.Fields.Where(f=>f.Path=="turret.padOffset"||f.Path=="turret.passageWidth"))Assert.False(NativeBalanceFields.Editable(field),"Topology changes require a new match like ordinary slot positions");
        }
        [Test] public void CurrentSaveContinuesTargetAimAndBurstExactly()
        {
            var p=PlayableProfile.Default;var d=New(p);int turret=Build(d,PlayableBuildingKind.SentryTurret,1001);var at=View(d).Buildings.Single(b=>b.Id==turret).Position;
            Enemy(d,new NavPoint(at.X,at.Z+5));Step(d,5);
            var bytes=d.CaptureWorldBytes(7,"sentry-current");var restored=PlayableDomain.RestoreWorldBytes(bytes,p,7,"sentry-current");owned.Add(restored);
            Assert.That(restored.CaptureWorldBytes(7,"sentry-current"),Is.EqualTo(bytes));
            for(int i=0;i<20;i++)
            {
                Step(d);Step(restored);
                Assert.That(restored.CaptureWorldBytes(7,"sentry-current"),Is.EqualTo(d.CaptureWorldBytes(7,"sentry-current")),"Exact future state at tick "+i);
            }
        }
    }
}

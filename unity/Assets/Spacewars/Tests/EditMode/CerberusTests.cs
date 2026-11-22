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
    public sealed class CerberusTests
    {
        const BindingFlags Private = BindingFlags.Instance|BindingFlags.NonPublic;
        const PlayableEntityKind Cerberus = PlayableEntityKind.Cerberus;
        PlayableDomain domain;
        object Call(string method,params object[] args)=>typeof(PlayableDomain).GetMethod(method,Private).Invoke(domain,args);
        object Row(string table,int id)=>((IDictionary)typeof(PlayableDomain).GetField(table,Private).GetValue(domain))[id];
        double Health(int id)=>(double)Row("units",id).GetType().GetField("Health").GetValue(Row("units",id));
        int Spawn(PlayableOwner owner,PlayableEntityKind kind,double x,double z)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,kind);
        void Frame(int id,double dt)
        {
            var body=domain.Navigation.Crowd.Units.Single(n=>n.Id==id);
            Call("RebuildTargetSpatialIndex");Call("RefreshVision");
            Call("AdvanceCerberus",Row("units",id),body,dt);
            Call("ApplyFlameHits");
        }
        [SetUp] public void Setup()=>domain=new PlayableDomain(PlayableProfile.Default,71,false);
        [TearDown] public void Release(){domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}

        [Test] public void DualStreamDamagesFriendlyAndEnemyAndResumesAfterCooling()
        {
            int actor=Spawn(PlayableOwner.Player,Cerberus,-10,-20);
            int enemy=Spawn(PlayableOwner.Enemy,Cerberus,-7.43,-20);
            int ally=Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,-5.4,-20);
            int air=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Hawk,-6,-20);
            double friendlyHealth=Health(ally),airHealth=Health(air);
            for(int i=0;i<30;i++)Frame(actor,1d/30);
            double firing=Health(enemy);
            Assert.Less(firing,370);
            Assert.Less(Health(ally),friendlyHealth);
            Assert.AreEqual(airHealth,Health(air));Assert.AreEqual(400,Health(actor));
            for(int i=0;i<35;i++)Frame(actor,1d/30);
            double cooling=Health(enemy);
            var bytes=domain.CaptureWorldBytes(19,"cerberus");
            var saved=PlayableDomain.RestoreWorldBytes(bytes,PlayableProfile.Default,19,"cerberus");
            Release();domain=saved;
            CollectionAssert.AreEqual(bytes,domain.CaptureWorldBytes(19,"cerberus"));
            for(int i=0;i<21;i++)Frame(actor,1d/30);
            Assert.AreEqual(cooling,Health(enemy),1e-7);
            for(int i=0;i<14;i++)Frame(actor,1d/30);
            Assert.Less(Health(enemy),cooling);
        }

        [TestCase("lower", 1)]
        [TestCase("upper", 5)]
        public void FlameTravelsOnTheShootersDeck(string surface, double height)
        {
            var map=new StackedNavigationFixture();
            var start=new NavLocation(new NavPoint(-4,2),surface);
            var end=new NavLocation(new NavPoint(-1,2),surface);
            Assert.True(map.CompatibleCombatSurface(start,end));
            Assert.False(map.CompatibleCombatSurface(start,new NavLocation(end.Position,surface=="upper"?"lower":"upper")));
            Assert.True(PlayableDomain.FlameTerrainClear(map,start,new BallisticPoint(-4,height,2),
                new BallisticPoint(-1,height,2),10));
            Assert.False(PlayableDomain.FlameTerrainClear(map,start,new BallisticPoint(-4,height,2),
                new BallisticPoint(-1,height-2,2),10));
        }

        [TestCase(-1, 1)]
        [TestCase(1, -1)]
        public void FlameClearanceAllowsTheRampAscentAndDescent(double from, double to)
        {
            var map = new StackedRampQualificationMap();
            var origin = new NavLocation(new NavPoint(from, 0), "ramp-support");
            Assert.True(PlayableDomain.FlameTerrainClear(map, origin,
                new BallisticPoint(from, from + 3, 0), new BallisticPoint(to, to + 3, 0), 10));
        }

        [TestCase(1, 1)]
        [TestCase(4, 3)]
        public void FlameClearanceBlocksSideEntryOntoOrBeneathRamp(double targetHeight, double originHeight)
        {
            var map = new StackedRampQualificationMap();
            var surface = originHeight < 4 ? "lower" : "upper";
            var origin = new NavLocation(new NavPoint(0, 2), surface);
            Assert.False(PlayableDomain.FlameTerrainClear(map, origin,
                new BallisticPoint(0, originHeight, 2), new BallisticPoint(0, targetHeight, 0), 10));
        }

        [Test] public void TwoSidesKeepSeparateTargetsAndDirectOrderPinsBoth()
        {
            int actor=Spawn(PlayableOwner.Player,Cerberus,-10,-20);
            int left=Spawn(PlayableOwner.Enemy,Cerberus,-7.6,-18.5);
            int right=Spawn(PlayableOwner.Enemy,Cerberus,-7.6,-21.5);
            for(int i=0;i<20;i++)Frame(actor,1d/30);
            Assert.Less(Health(left),400);Assert.Less(Health(right),400);
            double previous=Health(right);
            Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(71,1,"player-1",PlayableCommandKind.Attack,new[]{actor},targetId:left),out _));
            for(int i=0;i<12;i++)Frame(actor,1d/30);
            Assert.AreEqual(previous,Health(right),1e-7);
        }

        [Test] public void CommonTargetConsumesRemainingChargeThenRestartsTogether()
        {
            int actor=Spawn(PlayableOwner.Player,Cerberus,-10,-20);
            Spawn(PlayableOwner.Enemy,Cerberus,-9,-17.6);
            // A normal Move may fire its outboard weapon without steering the hull to the flank.
            var movingOrder=new PlayableTacticalOrderSnapshot(actor,PlayableOwner.Player,71,1,0,
                PlayableTacticalOrderKind.Move,new NavPoint(-5,-20),0);
            Row("units",actor).GetType().GetField("CurrentOrder").SetValue(Row("units",actor),movingOrder);
            CerberusWeaponPose Pose()=>domain.PlayerSnapshot(1,RuntimeStatus.Running,false,
                new PlayableRuntimeMetrics(0,0,0,0,0),null,19).Entities.Single(e=>e.Id==actor).Flames.Value;
            for(int i=0;i<29;i++)Frame(actor,1d/30);
            var split=Pose();Assert.Greater(split.LeftHeat,split.RightHeat+.1);
            int front=Spawn(PlayableOwner.Enemy,Cerberus,-7.2,-20);
            Call("RefreshVision");
            Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(71,2,"player-1",PlayableCommandKind.Attack,new[]{actor},targetId:front),out _));
            double before=Health(front);
            int elapsed=0;
            while(elapsed++<60 && Math.Max(Pose().LeftHeat,Pose().RightHeat)<.9999)Frame(actor,1d/30);
            Assert.Less(elapsed,60);Assert.Less(Health(front),before);
            double overheated=Health(front);
            for(int i=0;i<30;i++)Frame(actor,1d/30);
            Assert.AreEqual(overheated,Health(front),1e-7);
            Assert.AreEqual(0,Pose().LeftHeat,1e-7);Assert.AreEqual(0,Pose().RightHeat,1e-7);
            Frame(actor,1d/30);
            Assert.AreEqual(Pose().LeftHeat,Pose().RightHeat,1e-7);Assert.Greater(Pose().LeftLength,0);Assert.Greater(Pose().RightLength,0);
        }

        [TestCase(0)]
        [TestCase(18)]
        [TestCase(63)]
        [TestCase(100)]
        public void RestoredWeaponContinuesFromExactPhase(int frames)
        {
            int actor=Spawn(PlayableOwner.Player,Cerberus,-10,-20);
            int target=Spawn(PlayableOwner.Enemy,Cerberus,-7.2,-20);
            for(int i=0;i<frames;i++)Frame(actor,1d/30);
            var bytes=domain.CaptureWorldBytes(19,"phase");
            for(int i=0;i<12;i++)Frame(actor,1d/30);
            double expected=Health(target);
            var continuation=domain.CaptureWorldBytes(19,"phase");
            var restored=PlayableDomain.RestoreWorldBytes(bytes,PlayableProfile.Default,19,"phase");
            Release();domain=restored;
            Assert.True(domain.Navigation.Crowd.LeadsFormation?.Invoke(actor)==true,"Restored Cerberus must lead its group.");
            Assert.False(domain.Navigation.Crowd.LeadsFormation(3),"Restored starting Explorer must not lead the group.");
            for(int i=0;i<12;i++)Frame(actor,1d/30);
            Assert.AreEqual(expected,Health(target));
            CollectionAssert.AreEqual(continuation,domain.CaptureWorldBytes(19,"phase"));
        }

        [Test] public void HeldHullRotatesToSurvivingTargetAndIgnoresDistantArmor()
        {
            int actor=Spawn(PlayableOwner.Player,Cerberus,-10,-20);
            int target=Spawn(PlayableOwner.Enemy,Cerberus,-9,-17.6);
            int distant=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Shkval,-6,-10);
            double safe=Health(distant);
            Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(71,1,"player-1",
                PlayableCommandKind.Hold,new[]{actor}),out _));
            var hull=domain.Navigation.Crowd.Units.Single(n=>n.Id==actor);
            var position=hull.Position;
            for(int i=0;i<60;i++)Frame(actor,1d/30);
            Assert.True(hull.Held);Assert.AreEqual(position,hull.Position);Assert.Greater(hull.Heading,.5);
            Assert.Less(Health(target),400);Assert.AreEqual(safe,Health(distant));
        }

        [Test] public void RetreatingTanksLeaveTheConeAndStopTakingDamage()
        {
            int actor=Spawn(PlayableOwner.Player,Cerberus,-10,-20);
            int left=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-7.6,-18.5);
            int right=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-7.6,-21.5);
            for(int i=0;i<12;i++)Frame(actor,1d/30);
            double leftHealth=Health(left),rightHealth=Health(right);
            Assert.Less(leftHealth,PlayableProfile.Default.TankHealth);
            Assert.Less(rightHealth,PlayableProfile.Default.TankHealth);
            foreach(var id in new[]{left,right})
            {
                var hull=domain.Navigation.Crowd.Units.Single(n=>n.Id==id);
                var away=new NavPoint(-6,id==left?-12:-28);
                Assert.True(domain.Navigation.Crowd.Locate(away,hull.Radius,out var at));
                hull.Position=away;hull.Location=at;
            }
            for(int i=0;i<18;i++)Frame(actor,1d/30);
            Assert.AreEqual(leftHealth,Health(left));Assert.AreEqual(rightHealth,Health(right));
        }

        [Test] public void EnemyBuildingInterceptsStreamAndProtectsGroundUnitBehindIt()
        {
            int actor=Spawn(PlayableOwner.Player,Cerberus,-10,-20);
            int target=Spawn(PlayableOwner.Enemy,Cerberus,-6,-20);
            Call("AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.Refinery,new NavPoint(-7.7,-20),true);
            var fort=domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),
                null,19,PlayableOwner.Enemy).Buildings.Single(b=>b.Kind==PlayableBuildingKind.Refinery);
            var state=Row("buildings",fort.Id);
            double BuildingHealth()=>(double)state.GetType().GetField("Health").GetValue(state);
            double before=BuildingHealth();
            for(int i=0;i<25;i++)Frame(actor,1d/30);
            Assert.Less(BuildingHealth(),before);Assert.AreEqual(400,Health(target));
        }

        [Test] public void LosingUpgradedCenterRetainsPaidOrderAndRejectsNewOrders()
        {
            var player=PlayableOwner.Player;
            long seq=0;
            PlayableSnapshot View()=>domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19,player);
            int Build(PlayableBuildingKind kind,int slot)
            {
                Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,kind,1,player,null));
                Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
                return View().Buildings.Single(b=>b.Kind==kind).Id;
            }
            PlayableCommandStatus Queue(int id)=>domain.Apply(new PlayableCommand(71,++seq,"player-1",PlayableCommandKind.QueueCerberus,new[]{id}),out _);
            Call("AddCredits",player,10000d);
            int factory=Build(PlayableBuildingKind.Factory,1),center=Build(PlayableBuildingKind.ScientificCenter,2);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Queue(factory));
            Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(71,++seq,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.ScientificCenterUpgrade),out _));
            Call("AdvanceResearch",PlayableProfile.Default.ScientificCenterUpgradeSeconds);
            Assert.AreEqual(PlayableCommandStatus.Applied,Queue(factory));
            var paid=View().Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Single();
            Assert.AreEqual(400,paid.PaidCost);
            Call("RemoveBuilding",Row("buildings",center),false);
            Assert.AreEqual(PlayableCommandStatus.Rejected,Queue(factory));
            Call("AdvanceProduction",.1d);
            var retained=View().Buildings.Single(b=>b.Id==factory).PrivateState.Orders.Single();
            Assert.AreEqual(paid.Id,retained.Id);Assert.True(retained.Active);Assert.AreEqual(45,retained.Duration);
            var producer = View().Buildings.Single(b => b.Id == factory);
            var exit = FactoryReleaseRules.Exit(PlayableProfile.Default, producer.Position, producer.Heading, Cerberus);
            // Reproduce the stopped tank on the previous apron grid inside the new release corridor.
            Spawn(player, PlayableEntityKind.Tank, exit.X + 2.12, exit.Z);
            Call("AdvanceProduction",retained.Duration);
            for(int i=0;i<180 && !View().Entities.Any(u=>u.Kind==Cerberus);i++)
                domain.Step(1d/30);
            var released=View().Entities.Single(u=>u.Kind==Cerberus);
            Assert.AreEqual(player,released.Owner);Assert.AreEqual(400,released.Health);
        }

        [Test] public void OneMountKeepsFiringAlongTheEdgeOfARealWall()
        {
            var data=PlayableProfile.Default.CopyData();
            data.centralObstacleHalfWidth=.5;
            // Keep both mounts in acquisition range while both hulls clear the wall.
            data.cerberusEngagementRange=3.5;
            var profile=PlayableProfile.Create(data);
            Release();domain=new PlayableDomain(profile,71,false);
            double edge=profile.PassageHalfWidth;
            int actor=Spawn(PlayableOwner.Player,Cerberus,-1.8,edge);
            int target=Spawn(PlayableOwner.Enemy,Cerberus,1.8,edge);
            Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(71,1,"player-1",
                PlayableCommandKind.Hold,new[]{actor}),out _));
            for(int i=0;i<24;i++)Frame(actor,1d/30);
            var player=domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19);
            var hull=player.Entities.Single(u=>u.Id==actor);
            Assert.AreEqual(new NavPoint(-1.8,edge),hull.Position);
            var mounts=hull.Flames.Value;
            Assert.Zero(mounts.LeftLength);Assert.Zero(mounts.LeftHeat);
            Assert.Greater(mounts.RightLength,0);Assert.Greater(mounts.RightHeat,0);
            Assert.Less(Health(target),400);
        }

        [Test] public void CommonResearchBoostsFlamesAndArmorAndVeprChassisDoesNotApply()
        {
            Call("AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Factory,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            var view=domain.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19);
            int center=view.Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            long sequence=0;
            void Complete(PlayableResearchKind kind)
            {
                Assert.AreEqual(PlayableCommandStatus.Applied,domain.Apply(new PlayableCommand(71,++sequence,
                    "player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:kind),out _));
                Call("AdvanceResearch",PlayableResearchRules.Duration(PlayableProfile.Default,kind));
            }
            int actor=Spawn(PlayableOwner.Player,Cerberus,-20,-16);
            int target=Spawn(PlayableOwner.Enemy,Cerberus,-17.2,-16);
            for(int i=0;i<10;i++)Frame(actor,1d/30);
            double before=Health(target);
            Frame(actor,1d/30);
            double damage=before-Health(target);
            Assert.Greater(damage,0);
            Complete(PlayableResearchKind.ScientificCenterUpgrade);
            Complete(PlayableResearchKind.TankChassis);
            Assert.AreEqual(400,Health(actor));
            Complete(PlayableResearchKind.ImprovedDefensiveSystems);
            Assert.Greater(Health(actor),400);
            Complete(PlayableResearchKind.ImprovedWeaponSystems);
            before=Health(target);Frame(actor,1d/30);
            Assert.Greater(before-Health(target),damage);
        }

        [Test] public void EnemyCerberusRequiresVisionToAppearInPlayerSnapshot()
        {
            Spawn(PlayableOwner.Player,Cerberus,-10,-20);
            Spawn(PlayableOwner.Enemy,Cerberus,20,25);
            Call("RefreshVision");
            var metrics=new PlayableRuntimeMetrics(0,0,0,0,0);
            var observer=domain.Snapshot(1,RuntimeStatus.Running,false,metrics,null,19);
            var player=domain.PlayerSnapshot(1,RuntimeStatus.Running,false,metrics,null,19);
            Assert.AreEqual(2,observer.Entities.Count(u=>u.Kind==Cerberus));
            Assert.AreEqual(1,player.Entities.Count(u=>u.Kind==Cerberus));
            Assert.AreEqual(PlayableOwner.Player,player.Entities.Single(u=>u.Kind==Cerberus).Owner);
        }

        [Test] public void RearUnitInsideStreamOnlyTakesDamageAfterFrontTargetTriggersFire()
        {
            int actor=Spawn(PlayableOwner.Player,Cerberus,-10,-20);
            int rear=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-5.7,-20);
            double before=Health(rear);
            for(int i=0;i<12;i++)Frame(actor,1d/30);
            Assert.AreEqual(before,Health(rear));
            int front=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-7.3,-20);
            double frontHealth=Health(front);
            for(int i=0;i<12;i++)Frame(actor,1d/30);
            Assert.Less(Health(front),frontHealth);
            Assert.Less(Health(rear),before);
        }

        [TestCase(4.68,0,false)]
        [TestCase(4.35,0,true)]
        [TestCase(2.5,.98,false)]
        [TestCase(2.5,.45,true)]
        public void FlameDamageRequiresInteriorOverlap(double ahead,double sideways,bool hit)
        {
            int actor=Spawn(PlayableOwner.Player,Cerberus,-10,-20);
            int victim=Spawn(PlayableOwner.Enemy,PlayableEntityKind.Tank,-10+ahead,-20+sideways);
            object[] args={Row("units",actor),domain.Navigation.Crowd.Units.Single(u=>u.Id==actor).Location,
                victim,new NavPoint(-10,-20),default(NavPoint),(double?)0,(double?)4,null};
            Assert.AreEqual(hit,typeof(PlayableDomain).GetMethod("FlameContact",Private).Invoke(domain,args));
        }

        [Test] public void CustomEngagementDistanceSurvivesWorldRestore()
        {
            var field=PlayableProfileMetadata.CerberusEngagementField;
            Assert.True(NativeBalanceFields.LabEditable(field));
            var data=PlayableProfile.Default.CopyData();
            field.Write(data,2.4);
            var profile=PlayableProfile.Create(data);
            Release();domain=new PlayableDomain(profile,71,false);
            var bytes=domain.CaptureWorldBytes(19,"custom-engagement");
            var restored=PlayableDomain.RestoreWorldBytes(bytes,profile,19,"custom-engagement");
            Release();domain=restored;
            CollectionAssert.AreEqual(bytes,domain.CaptureWorldBytes(19,"custom-engagement"));
        }

        [TestCase(false, 3, 0, 0, "upgraded scientific center prerequisite")]
        [TestCase(true, 0, 0, 0, "AI Cerberus limit")]
        [TestCase(true, 1, 0, 0, null)]
        [TestCase(true, 2, 1, 1, "AI Cerberus limit")]
        [TestCase(true, 3, 2, 1, "AI Cerberus limit")]
        [TestCase(true, 4, 2, 1, null)]
        [TestCase(true, 4, 2, 2, "AI Cerberus limit")]
        [TestCase(true, 4, 4, 0, "AI Cerberus limit")]
        public void AiProductionCountsLivingAndPaidCerberus(bool upgradedCenter, int preference, int alive, int queued, string reason)
        {
            var profile = PlayableProfile.Default;
            var owner = PlayableOwner.Player;
            int support = 15 * Math.Min(alive + queued + 1, 4);
            int tankCost = PlayableUnitRules.Population(profile, PlayableEntityKind.Tank);
            int tanks = (support + tankCost - 1) / tankCost;
            var entities = Enumerable.Range(10, alive).Select(id =>
                new PlayableEntitySnapshot(id, owner, Cerberus, default, 400, false, 0, 0, 0))
                .Concat(Enumerable.Range(100, tanks).Select(id =>
                    new PlayableEntitySnapshot(id, owner, PlayableEntityKind.Tank, default, 100, false, 0, 0, 0)))
                .ToArray();
            var orders = Enumerable.Range(1, queued).Select(id =>
                new PlayableProductionOrderSnapshot(id, Cerberus, 400, 10, 45, 45, false)).ToArray();
            var factory = new PlayableBuildingSnapshot(2, owner, PlayableBuildingKind.Factory, default,
                100, 1, queued, 0, default, orders: orders);
            var science = new PlayableBuildingSnapshot(3, owner, PlayableBuildingKind.ScientificCenter, default,
                100, 1, 0, 0, default, scientificCenterUpgraded: upgradedCenter);
            const string ownerId = "player-1";
            int seed = Enumerable.Range(1, 100).First(candidate =>
                AiEconomyAdmission.CerberusPreference(candidate, ownerId) == preference);
            var snapshot = new PlayableSnapshot(profile.ProfileId, profile.Revision, 71, seed, 1, 0,
                RuntimeStatus.Running, false, PlayableMatchOutcome.Playing, 10000, domain.Geometry,
                entities, new[] { factory, science }, Array.Empty<PlayableProjectileSnapshot>(),
                new PlayableRuntimeMetrics(0, 0, 0, 0, 0), null,
                population: new PlayablePopulationSnapshot(alive * 10 + tanks * tankCost, 0, 100));
            var observation = PlayableAiObservation.From(snapshot);
            var action = new PlayableAiAction(1, observation.OwnerId, profile.ProfileId, profile.Revision,
                observation.Generation, observation.SnapshotSequence, PlayableCommandKind.QueueCerberus,
                new[] { factory.Id }, unitKind: Cerberus);
            Assert.AreEqual(reason, AiEconomyAdmission.Reject(observation, profile, action));
        }

        [Test] public void CerberusChoosesReachableStrikeGroupWithFewerCerberus()
        {
            var profile = PlayableProfile.Default;
            var ai = AiProfile.Initial;
            var owner = PlayableOwner.Player;
            var fresh = new PlayableEntitySnapshot(5, owner, Cerberus, new NavPoint(-20, -20), 400, false, 0, 0, 0);
            var leaderOne = new PlayableEntitySnapshot(1, owner, PlayableEntityKind.Tank, new NavPoint(-10, -20), 100, false, 0, 0, 0);
            var leaderTwo = new PlayableEntitySnapshot(2, owner, PlayableEntityKind.Tank, new NavPoint(-20, -10), 100, false, 0, 0, 0);
            var firstCerberus = new PlayableEntitySnapshot(3, owner, Cerberus, new NavPoint(-12, -20), 400, false, 0, 0, 0);
            var vision = new TeamVisionSnapshot(1, 0, 0, 128, 128, 4,
                Array.Empty<VisionSource>(), Array.Empty<long>(),
                new byte[PlayableVision.RasterResolution * PlayableVision.RasterResolution], Array.Empty<KnownBuilding>());
            PlayableRouteProof RouteTo(PlayableEntitySnapshot leader) =>
                new PlayableRouteProof(fresh.Id, owner, 71, 0, 1, fresh.Position,
                    PlayableUnitRules.Radius(profile, Cerberus), PlayableRouteTargetKind.FriendlyAnchor,
                    leader.Id, leader.Position, leader.Position, new[] { leader.Position });
            PlayableAiObservation View(params PlayableRouteProof[] routes) =>
                PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId, profile.Revision, 71, 19, 1, 0,
                    RuntimeStatus.Running, false, PlayableMatchOutcome.Playing, 0, domain.Geometry,
                    new[] { fresh, leaderOne, firstCerberus, leaderTwo }, Array.Empty<PlayableBuildingSnapshot>(),
                    Array.Empty<PlayableProjectileSnapshot>(), new PlayableRuntimeMetrics(0, 0, 0, 0, 0), null,
                    vision: vision, routeProofs: routes), AiDifficulty.Expert);

            var bothRoutes = View(RouteTo(leaderOne), RouteTo(leaderTwo));
            var registry = new AiArmyRegistry(bothRoutes.OwnerId, 71, ai, AiDifficulty.Expert);
            Assert.True(registry.TryCreate(bothRoutes, AiArmyRole.Attack, AiArmyPlanner.Policy,
                new[] { leaderOne.Id, firstCerberus.Id }, out long groupOne, requiredMinimum: 1));
            Assert.True(registry.TryCreate(bothRoutes, AiArmyRole.Attack, AiArmyPlanner.FlankPolicy,
                new[] { leaderTwo.Id }, out long groupTwo, requiredMinimum: 1));
            registry.UpdateMission(groupOne, AiArmyPlanner.Policy, AiArmyPhase.Advancing, 0, "attack at west");
            registry.UpdateMission(groupTwo, AiArmyPlanner.FlankPolicy, AiArmyPhase.Advancing, 0, "attack at east");
            var primary = registry.CaptureArmy(groupOne).Single();
            Assert.AreEqual(groupTwo,
                AiReinforcementPolicy.PreferredReceiver(bothRoutes, registry, primary, fresh, profile, ai, null));
            Assert.AreEqual(groupOne,
                AiReinforcementPolicy.PreferredReceiver(View(RouteTo(leaderOne)), registry, primary, fresh, profile, ai, null));
        }

        [TestCase(0, 14, false)]
        [TestCase(0, 15, true)]
        [TestCase(1, 29, false)]
        [TestCase(1, 30, true)]
        [TestCase(3, 59, false)]
        [TestCase(3, 60, true)]
        public void AiCerberusOrderRequiresLivingSupportForEveryCommittedTank(int committed, int support, bool allowed)
        {
            const string owner = "player-1";
            int seed = Enumerable.Range(1, 100).First(candidate => AiEconomyAdmission.CerberusPreference(candidate, owner) == 4);
            Assert.AreEqual(allowed, AiEconomyAdmission.CerberusOrderRejection(seed, owner, committed, support) == null);
        }

        [Test] public void CerberusPreferenceIsBoundToTheMatchAndOwner()
        {
            var seeds = Enumerable.Range(-20, 41).ToArray();
            var firstOwner = seeds.Select(seed => AiEconomyAdmission.CerberusPreference(seed, "player-1")).ToArray();
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4 }, firstOwner.Distinct());
            CollectionAssert.AreEqual(firstOwner,
                seeds.Select(seed => AiEconomyAdmission.CerberusPreference(seed, "player-1")).ToArray());
            Assert.False(firstOwner.SequenceEqual(
                seeds.Select(seed => AiEconomyAdmission.CerberusPreference(seed, "enemy-1"))));
        }

        [Test] public void AiRosterTreatsCerberusAsAFrontLineGroundVehicle()
        {
            var profile=PlayableProfile.Default;
            Assert.AreEqual(400,PlayableUnitRules.Health(profile,Cerberus));
            Assert.AreEqual(400,PlayableUnitRules.Cost(profile,Cerberus));
            Assert.AreEqual(10,PlayableUnitRules.Population(profile,Cerberus));
            Assert.AreEqual(45,PlayableUnitRules.Duration(profile,Cerberus));
            Assert.AreEqual(2.4,PlayableUnitRules.Speed(profile,Cerberus,true));
            Assert.AreEqual(Math.PI/4,PlayableUnitRules.Turn(profile,Cerberus),1e-8);
            Assert.AreEqual(Cerberus,PlayableUnitRules.ProductionKind(PlayableCommandKind.QueueCerberus));
            var catalog=AiRosterCatalog.Initial;
            Assert.AreEqual(Cerberus,catalog.Production(PlayableCommandKind.QueueCerberus).kind);
            Assert.AreEqual(1,catalog.LineWeight(Cerberus));
            Assert.Zero(catalog.SupportWeight(Cerberus)+catalog.ReconWeight(Cerberus));
            foreach(var field in PlayableProfileMetadata.CerberusFields)Assert.True(NativeBalanceFields.LabEditable(field),field.Path);
        }
    }
}

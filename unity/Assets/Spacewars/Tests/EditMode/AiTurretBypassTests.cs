using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiTurretBypassTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile AI=AiProfile.Initial;
        static readonly NavPoint Goal=new NavPoint(30,33.5);
        static PlayableBuildingSnapshot Target()=>new PlayableBuildingSnapshot(101,PlayableOwner.Enemy,PlayableBuildingKind.Headquarters,new NavPoint(30,25),1000,1,0,0,default);
        static PlayableBuildingSnapshot Tower(NavPoint? point=null,ConstructionPhase phase=ConstructionPhase.Ready)=>new PlayableBuildingSnapshot(102,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,point??new NavPoint(21,25),300,1,0,0,default,phase:phase);
        static PlayableEntitySnapshot Unit(int id,int hp=100,NavPoint? point=null)=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,point??new NavPoint(15,33.5),hp,false,0,0,0);
        static PlayableAiObservation View(PlayableEntitySnapshot[] units,PlayableBuildingSnapshot[] towers=null,PlayableEntitySnapshot[] enemies=null,PlayableRouteProof[] routes=null,long tick=100)
        {
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(default,100)},Array.Empty<KnownBuilding>());
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,107,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,
                units.Concat(enemies??Array.Empty<PlayableEntitySnapshot>()).ToArray(),new[]{Target()}.Concat(towers??new[]{Tower()}).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:routes,activeProfile:P),AiDifficulty.Expert);
        }
        static Dictionary<int,NavPoint[]> Paths(PlayableEntitySnapshot[] units)=>units.ToDictionary(u=>u.Id,_=>new[]{Goal});
        static bool Assess(PlayableEntitySnapshot[] units,out double damage,PlayableBuildingSnapshot[] towers=null,PlayableEntitySnapshot[] enemies=null,AiTurretBypassPolicy.Passage departure=null)=>AiTurretBypassPolicy.Assess(View(units,towers,enemies),P,AI,units,101,Paths(units),null,null,out damage,out _,departure);
        [Test] public void RemainingLegRequiresTheActiveGroupIdentityRatherThanOnlyAnAcceptedRevision()
        {
            var stamp=new PlayableOrderStamp(1,PlayableOwner.Player,107,3,55,100,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,5,5);
            PlayableEntitySnapshot U(long group,PlayableCommandKind kind,bool viable=true)=>new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,default,100,true,0,0,0,orderStamp:stamp,
                queue:new PlayableUnitQueueSnapshot(new PlayableQueuedOrderSnapshot(5,group,55,3,kind,new NavLocation(Goal,NavLocation.FlatSurface),101),null,Array.Empty<PlayableQueuedOrderSnapshot>(),null,activeAiOrderViable:viable));
            Assert.False(AiTurretBypassPolicy.RemainingGoal(U(6,PlayableCommandKind.Move),4,3,Goal,Goal,out _),"A pending group's accepted revision does not prove its leg is installed");
            Assert.False(AiTurretBypassPolicy.RemainingGoal(U(4,PlayableCommandKind.Attack),4,3,Goal,Goal,out _),"The previous attack cannot supply a stationary passage endpoint");
            Assert.False(AiTurretBypassPolicy.RemainingGoal(U(4,PlayableCommandKind.Move,false),4,3,Goal,Goal,out _),"Accepted Move waits for an active physical order before projecting its remaining destination");
            var actual=new NavPoint(31,34);Assert.True(AiTurretBypassPolicy.RemainingGoal(U(4,PlayableCommandKind.Move),4,3,actual,Goal,out var goal));Assert.AreEqual(actual,goal);
        }
        [Test] public void AffordableCrossingEndsOutsideTowerFire()
        {
            var units=Enumerable.Range(1,8).Select(id=>Unit(id)).ToArray();
            Assert.True(Assess(units,out var damage));Assert.Greater(damage,0);Assert.LessOrEqual(damage,units.Sum(u=>u.Health)*.2);
            Assert.False(Assess(units,out _,new[]{Tower(new NavPoint(30,25))}),"Covered endpoint is never a bypass");
        }
        [Test] public void StaggeredMembersExtendTheSharedTurretFireEnvelope()
        {
            var aligned=Enumerable.Range(1,8).Select(id=>Unit(id)).ToArray();Assess(aligned,out var together);
            var staggered=aligned.Select(u=>u.Id==8?Unit(u.Id,100,new NavPoint(0,33.5)):u).ToArray();Assess(staggered,out var separated);
            Assert.Greater(separated,together,"A tower can shoot successive members at different times; maximum per-member exposure undercounts group damage");
        }
        [Test] public void VisibleRocketUpgradeAndPerProjectileRoundingReachTheThreatForecast()
        {
            var units=Enumerable.Range(1,8).Select(id=>Unit(id)).ToArray();
            var rocket=new PlayableEntitySnapshot(201,PlayableOwner.Enemy,PlayableEntityKind.Explorer,new NavPoint(30,34),80,false,0,0,0,rocketLauncher:true);
            Assert.True(View(units,enemies:new[]{rocket}).Entities.Single(u=>u.Id==201).RocketLauncher);
            Assess(units,out var damage);
            double upgraded=PlayableStatRounding.Damage(P,P.TurretDamage*(1+P.WeaponSystemsLevel3BonusPercent/100d))*P.TurretBurstSize;
            Assert.GreaterOrEqual(damage,upgraded,"A ready upgraded burst rounds every projectile before summing");
        }
        [Test] public void DamageBudgetCountsTheWholeGroupButEveryMemberMustSurvive()
        {
            Assert.False(Assess(new[]{Unit(1),Unit(2)},out var damage));Assert.Greater(damage,40);
            var units=Enumerable.Range(1,8).Select(id=>Unit(id,id==1?5:100)).ToArray();
            Assert.False(Assess(units,out _),"A low-HP member cannot be sacrificed under a group average");
        }
        [Test] public void OriginalHealthBudgetCannotRenewAfterTakingDamageOrLosingAMember()
        {
            var healthy=Enumerable.Range(1,8).Select(id=>Unit(id,100,Goal)).ToArray();
            var departure=new AiTurretBypassPolicy.Passage{Health=healthy.ToDictionary(u=>u.Id,_=>100d)};
            var atLimit=healthy.Select(u=>Unit(u.Id,80,Goal)).ToArray();
            Assert.True(Assess(atLimit,out _,departure:departure));
            Assert.False(Assess(healthy.Select(u=>Unit(u.Id,79,Goal)).ToArray(),out _,departure:departure));
            Assert.False(Assess(healthy.Take(6).ToArray(),out _,departure:departure),"Dead members spend their entire starting HP");
        }
        [Test] public void HealingCannotRefundTheObservedPassageDamageBudget()
        {
            var healthy=Enumerable.Range(1,8).Select(id=>Unit(id,100,Goal)).ToArray();var view=View(healthy);
            var policy=new AiTurretBypassPolicy();policy.Begin(1,new PlayableAiAction(1,view.OwnerId,P.ProfileId,P.Revision,view.Generation,100,PlayableCommandKind.Move,healthy.Select(u=>u.Id).ToArray(),Goal,targetId:101),view,true);
            policy.ObserveHealth(1,healthy.Select(u=>Unit(u.Id,80,Goal)),101);policy.ObserveHealth(1,healthy,102);Assert.AreEqual(160,policy.For(1).Damage);
            using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))policy.Write(writer);stream.Position=0;
            var restored=new AiTurretBypassPolicy();using(var reader=new BinaryReader(stream,System.Text.Encoding.UTF8,true))restored.Read(reader,102);Assert.AreEqual(160,restored.For(1).Damage);
            var wounded=healthy.Select(u=>Unit(u.Id,99,Goal)).ToArray();restored.ObserveHealth(1,wounded,103);
            Assert.False(AiTurretBypassPolicy.Assess(View(wounded,tick:103),P,AI,wounded,101,Paths(wounded),null,null,out _,out _,restored.For(1)));
        }
        [Test] public void DestinationDefendersRequireSignificantCompatibleSuperiority()
        {
            var units=Enumerable.Range(1,8).Select(id=>Unit(id,100,Goal)).ToArray();
            var defenders=Enumerable.Range(201,5).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(30,34),100,false,0,0,0)).ToArray();
            Assert.False(Assess(units,out _,enemies:defenders),"Eight tanks do not have double the strength of five");
            var hawk=new PlayableEntitySnapshot(250,PlayableOwner.Enemy,PlayableEntityKind.Hawk,new NavPoint(30,34),100,false,0,0,0);
            Assert.False(Assess(units,out _,enemies:new[]{hawk}),"Tanks cannot answer an air defender");
        }
        [Test] public void ConstructingTowerDoesNotCreateFireAndMissingRoutesCannotAuthorizePassage()
        {
            var units=Enumerable.Range(1,8).Select(id=>Unit(id)).ToArray();
            Assert.True(Assess(units,out var damage,new[]{Tower(phase:ConstructionPhase.Constructing)}));Assert.Zero(damage);
            var paths=Paths(units);paths.Remove(1);
            Assert.False(AiTurretBypassPolicy.Assess(View(units),P,AI,units,101,paths,null,null,out _,out _));
        }
        [Test] public void VeteranQuotaIsSharedOncePerBattleAndSurvivesExactRestore()
        {
            var ledger=new AiTurretBypassPolicy();
            for(long battle=1;battle<=9;battle++)
            {
                for(int observation=0;observation<20;observation++)ledger.Battle(battle);
                Assert.AreEqual(battle%3==0,ledger.Allowed(AiDifficulty.Veteran,battle,AI));
                Assert.True(ledger.Allowed(AiDifficulty.Expert,battle,AI));Assert.True(ledger.Allowed(AiDifficulty.Cheater,battle,AI));Assert.False(ledger.Allowed(AiDifficulty.Fighter,battle,AI));
            }
            var units=Enumerable.Range(1,8).Select(id=>Unit(id)).ToArray();var o=View(units);
            var action=new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Move,units.Select(u=>u.Id).ToArray(),Goal,targetId:101);
            ledger.Begin(3,action,o,true);
            byte[] Write(AiTurretBypassPolicy x){using var stream=new MemoryStream();using var w=new BinaryWriter(stream);x.Write(w);return stream.ToArray();}
            var bytes=Write(ledger);var restored=new AiTurretBypassPolicy();using(var r=new BinaryReader(new MemoryStream(bytes)))restored.Read(r,100);
            CollectionAssert.AreEqual(bytes,Write(restored));Assert.True(restored.For(3).Dangerous);Assert.AreEqual(800,restored.For(3).Health.Values.Sum());
            for(long b=1;b<=9;b++)Assert.AreEqual(ledger.Allowed(AiDifficulty.Veteran,b,AI),restored.Allowed(AiDifficulty.Veteran,b,AI));
        }
        [Test] public void NewControlsAreEditableBoundedAndPreserveLegacyDefaultIdentity()
        {
            var fields=NativeBalanceFields.AiLabFields.Where(f=>f.Path.StartsWith("turretBypass.")).ToArray();Assert.AreEqual(3,fields.Length);
            foreach(var f in fields)
            {
                Assert.IsNotEmpty(f.Label);Assert.IsNotEmpty(f.Description);Assert.IsNotEmpty(f.Unit);Assert.Throws<ArgumentOutOfRangeException>(()=>f.Validate(f.Maximum+f.Step));
                var d=AI.CopyData();NativeBalanceFields.Write(f,d,f.Minimum);Assert.AreEqual(f.Minimum,new AiProfile(d).Value(f.Path));
            }
            var legacy=AI.CopyData();legacy.fields=legacy.fields.Where(f=>!f.path.StartsWith("turretBypass.")).ToArray();Assert.AreEqual(AI.Hash,new AiProfile(legacy).Hash);
        }
        [Test] public void AirGroupsIgnoreGroundOnlyGaussAndPreserveCompatibleFireRules()
        {
            var units=Enumerable.Range(1,8).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Hawk,new NavPoint(30,33.5),100,false,0,0,0,altitude:10)).ToArray();
            var gauss=new PlayableBuildingSnapshot(102,PlayableOwner.Enemy,PlayableBuildingKind.GaussTower,new NavPoint(26,30),300,1,0,0,default);
            Assert.True(Assess(units,out var damage,new[]{gauss}));Assert.Zero(damage);
            Assert.Greater(AiTurretBypassPolicy.Positions(P,Target(),units).Length,0);
        }
        [Test] public void PassageMissionAndSharedLedgerRoundTripThroughTheArmyWire()
        {
            var units=Enumerable.Range(1,8).Select(id=>Unit(id)).ToArray();var o=View(units);var registry=new AiArmyRegistry(o.OwnerId,o.Generation,AI,AiDifficulty.Veteran);
            var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(P);
            var attack=new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Attack,units.Select(u=>u.Id).ToArray(),Target().Position,targetId:101);
            planner.Commit(o,attack,registry);
            // Terminal receipt is part of the real planner contract, not a direct phase edit.
            planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,1,100,1,100,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(o.Generation,o.OwnerId,1,1)));
            var proofs=units.Select(u=>new PlayableRouteProof(u.Id,u.Owner,o.Generation,o.Tick,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),PlayableRouteTargetKind.EnemyApproach,101,Goal,Goal,new[]{Goal})).ToArray();
            var moveView=View(units,routes:proofs);var move=planner.ProposePassage(moveView,P,AI,registry);Assert.IsNotNull(move);
            planner.Commit(moveView,move,registry);Assert.True(planner.Passing);
            var oldAttacks=units.Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,false,101,0,0,
                currentOrder:new PlayableTacticalOrderSnapshot(u.Id,u.Owner,o.Generation,1,100,PlayableTacticalOrderKind.Attack,Target().Position,101),
                orderStamp:new PlayableOrderStamp(u.Id,u.Owner,o.Generation,1,1,100,PlayableOrderOrigin.Ai,PlayableCommandKind.Attack,PlayableAiOpeningComposition.SourceIdentity,1,1))).ToArray();
            planner.Observe(View(oldAttacks),registry,AI,replanQuietGuards:false);Assert.True(planner.Passing,"The previous owned attack is retained while the passage waits for its receipt");
            Assert.AreEqual(800,planner.Bypass.For(planner.Capture().ArmyId).Health.Values.Sum());
            byte[] Write(AiArmyPlanner x){using var stream=new MemoryStream();using var w=new BinaryWriter(stream);x.WriteState(w);return stream.ToArray();}
            var expiredRegistry=AiArmyRegistry.Restore(registry.Capture(),AI,AiDifficulty.Veteran,100);var waiting=Write(planner);var expired=new AiArmyPlanner(o.OwnerId,o.Generation);using(var reader=new BinaryReader(new MemoryStream(waiting)))expired.ReadState(reader,100,expiredRegistry);
            expired.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,2,100,0,0,PlayableAiDeliveryStatus.Stale,PlayableCommandStatus.StaleGeneration,"Intent expired before delivery.",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(o.Generation,o.OwnerId,2,1),kind:PlayableCommandKind.Move));
            var terminal=Write(expired);var terminalClone=new AiArmyPlanner(o.OwnerId,o.Generation);using(var reader=new BinaryReader(new MemoryStream(terminal)))terminalClone.ReadState(reader,100,expiredRegistry);
            expired.Observe(moveView,expiredRegistry,AI,replanQuietGuards:false);
            var reconsidered=Write(expired);var reconsideredClone=new AiArmyPlanner(o.OwnerId,o.Generation);using(var reader=new BinaryReader(new MemoryStream(reconsidered)))reconsideredClone.ReadState(reader,100,expiredRegistry);
            expired.Observe(View(units,routes:proofs,tick:101),expiredRegistry,AI,replanQuietGuards:false);
            Assert.True(expired.Active,"The next observation must not disband a group whose intent expired before execution");
            Assert.False(expired.Passing);Assert.IsNull(expired.Bypass.For(expired.Capture().ArmyId));Assert.NotNull(expired.ProposePassage(moveView,P,AI,expiredRegistry),"An unexecuted intent can be reassessed within the same battle");
            var bytes=Write(planner);var restored=new AiArmyPlanner(o.OwnerId,o.Generation);using(var reader=new BinaryReader(new MemoryStream(bytes)))restored.ReadState(reader,100,registry);
            CollectionAssert.AreEqual(bytes,Write(restored));Assert.True(restored.Passing);Assert.IsNull(restored.ProposePassage(o,P,AI,registry));
            restored.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,2,100,2,100,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(o.Generation,o.OwnerId,2,1),kind:PlayableCommandKind.Move));
            var admitted=units.Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,100,false,101,0,0,
                currentOrder:new PlayableTacticalOrderSnapshot(u.Id,u.Owner,o.Generation,1,100,PlayableTacticalOrderKind.Attack,Target().Position,101),
                orderStamp:new PlayableOrderStamp(u.Id,u.Owner,o.Generation,2,2,100,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,2,2))).ToArray();
            restored.Observe(View(admitted),registry,AI,replanQuietGuards:false);Assert.True(restored.Passing,"The old Attack retained during Move admission is not an attack after arrival");
            var arrivals=units.Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Id==8?new NavPoint(37,33.5):Goal,u.Health,false,0,0,0,navigationOutcome:NavigationOutcome.Arrived,
                orderStamp:new PlayableOrderStamp(u.Id,u.Owner,o.Generation,1,2,100,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,2,2))).ToArray();
            var firingProofs=arrivals.Select(u=>new PlayableRouteProof(u.Id,u.Owner,o.Generation,100,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),PlayableRouteTargetKind.VisibleEnemy,101,Target().Position,u.Position,new[]{u.Position})).ToArray();
            var arrived=View(arrivals,routes:firingProofs);restored.Observe(arrived,registry,AI,replanQuietGuards:false);Assert.False(restored.Passing);
            var opening=PlayableAiOpeningComposition.Initialize(7,o.OwnerId,aiProfile:AI).WithPhase(PlayableAiOpeningPhase.Complete);
            var fire=restored.Propose(arrived,opening,P,AI,registry);Assert.NotNull(fire);Assert.AreEqual(PlayableCommandKind.Attack,fire.Kind);Assert.False(fire.EntityIds.Contains(8),"A safe back-row member cannot charge out of its uncovered position just to obtain a shot");
        }
        [Test] public void PendingReinforcementsCannotVetoTheViableCorePassage()
        {
            var core=Enumerable.Range(1,8).Select(id=>Unit(id)).ToArray();var all=core.Append(Unit(9)).ToArray();
            var proofs=core.Select(u=>new PlayableRouteProof(u.Id,u.Owner,107,100,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),PlayableRouteTargetKind.EnemyApproach,101,Goal,Goal,new[]{Goal})).ToArray();
            var o=View(all,routes:proofs);var registry=new AiArmyRegistry(o.OwnerId,o.Generation,AI,AiDifficulty.Expert);
            var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(P);
            var attack=new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,o.Generation,o.SnapshotSequence,PlayableCommandKind.Attack,core.Select(u=>u.Id).ToArray(),Target().Position,targetId:101);
            planner.Commit(o,attack,registry);planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,1,100,1,100,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(o.Generation,o.OwnerId,1,1)));
            Assert.True(registry.TryReinforce(o,planner.Capture().ArmyId,AiArmyPlanner.Policy,new[]{9}));
            var passage=planner.ProposePassage(o,P,AI,registry);Assert.NotNull(passage);CollectionAssert.AreEqual(core.Select(u=>u.Id),passage.EntityIds);
            planner.Commit(o,passage,registry);Assert.False(planner.AllowAutomaticReinforcement,"Newcomers must not enter the dangerous leg alone");
            planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,2,100,2,100,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(o.Generation,o.OwnerId,2,1),kind:PlayableCommandKind.Move));
            var moving=core.Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,new NavPoint(21,31),u.Health,true,0,0,0,
                currentOrder:new PlayableTacticalOrderSnapshot(u.Id,u.Owner,o.Generation,2,100,PlayableTacticalOrderKind.Move,Goal,0),navigationOutcome:NavigationOutcome.Moving,
                orderStamp:new PlayableOrderStamp(u.Id,u.Owner,o.Generation,1,2,100,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,2,2))).Append(Unit(9)).ToArray();
            byte[] Write(AiArmyPlanner x){using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);x.WriteState(writer);return stream.ToArray();}
            var riskRegistry=AiArmyRegistry.Restore(registry.Capture(),AI,AiDifficulty.Expert,100);var risk=new AiArmyPlanner(o.OwnerId,o.Generation);using(var reader=new BinaryReader(new MemoryStream(Write(planner))))risk.ReadState(reader,100,riskRegistry);risk.Rebind(P);
            var wounded=moving.Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Id==9?100:79,u.Moving,0,0,0,currentOrder:u.CurrentOrder,orderStamp:u.OrderStamp)).ToArray();
            risk.Observe(View(wounded,tick:101),riskRegistry,AI,replanQuietGuards:false);Assert.False(risk.Passing);
            var safetyClone=new AiArmyPlanner(o.OwnerId,o.Generation);using(var reader=new BinaryReader(new MemoryStream(Write(risk))))safetyClone.ReadState(reader,101,riskRegistry);
            planner.Observe(View(moving,tick:101),registry,AI,replanQuietGuards:false);Assert.True(planner.Passing);Assert.AreEqual(AiArmyPhase.Advancing,planner.Capture().Phase,"A deliberately crossing core is not the safe receiving position promised to a reinforcement");
        }
        [Test] public void UncoveredMembersFireWhileTheRemainingMemberKeepsItsPassageAndHealthBudget()
        {
            var units=Enumerable.Range(1,8).Select(id=>Unit(id)).ToArray();var routes=units.Select(u=>new PlayableRouteProof(u.Id,u.Owner,107,100,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),PlayableRouteTargetKind.EnemyApproach,101,Goal,Goal,new[]{Goal})).ToArray();
            var o=View(units,routes:routes);var registry=new AiArmyRegistry(o.OwnerId,o.Generation,AI,AiDifficulty.Expert);var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(P);
            planner.Commit(o,new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,o.Generation,100,PlayableCommandKind.Attack,units.Select(u=>u.Id).ToArray(),Target().Position,targetId:101),registry);
            planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,1,100,1,100,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(o.Generation,o.OwnerId,1,1)));
            planner.Commit(o,planner.ProposePassage(o,P,AI,registry),registry);
            planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,2,100,2,100,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(o.Generation,o.OwnerId,2,1),kind:PlayableCommandKind.Move));
            var positioned=units.Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Id==8?new NavPoint(21,31):Goal,100,true,0,0,0,
                currentOrder:new PlayableTacticalOrderSnapshot(u.Id,u.Owner,107,2,100,PlayableTacticalOrderKind.Move,Goal,101),
                orderStamp:new PlayableOrderStamp(u.Id,u.Owner,107,1,2,100,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,2,2))).ToArray();
            var view=View(positioned,tick:101);planner.Observe(view,registry,AI,replanQuietGuards:false);
            var opening=PlayableAiOpeningComposition.Initialize(7,o.OwnerId,aiProfile:AI).WithPhase(PlayableAiOpeningPhase.Complete);
            var fire=planner.Propose(view,opening,P,AI,registry);Assert.NotNull(fire);Assert.AreEqual(PlayableCommandKind.Attack,fire.Kind);CollectionAssert.AreEqual(Enumerable.Range(1,7),fire.EntityIds);Assert.True(planner.PassageFireSafe(view,P,fire));
            Assert.False(planner.PassageFireSafe(view,P,new PlayableAiAction(3,o.OwnerId,P.ProfileId,P.Revision,107,101,PlayableCommandKind.Attack,new[]{8},Target().Position,targetId:101)));
            planner.Commit(view,fire,registry);planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,3,101,3,101,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(o.Generation,o.OwnerId,3,1),kind:PlayableCommandKind.Attack));
            var firing=positioned.Select(u=>u.Id==8?u:new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,100,false,101,0,0,
                currentOrder:new PlayableTacticalOrderSnapshot(u.Id,u.Owner,107,3,101,PlayableTacticalOrderKind.Attack,Target().Position,101),
                orderStamp:new PlayableOrderStamp(u.Id,u.Owner,107,2,3,101,PlayableOrderOrigin.Ai,PlayableCommandKind.Attack,PlayableAiOpeningComposition.SourceIdentity,3,3))).ToArray();
            planner.Observe(View(firing,tick:102),registry,AI,replanQuietGuards:false);Assert.True(planner.Passing);Assert.AreEqual(2,planner.Bypass.For(planner.Capture().ArmyId).Sequence);Assert.AreEqual(800,planner.Bypass.For(planner.Capture().ArmyId).Health.Values.Sum());
            using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))planner.WriteState(writer);stream.Position=0;var restored=new AiArmyPlanner(o.OwnerId,o.Generation);using(var reader=new BinaryReader(stream))restored.ReadState(reader,102,registry);Assert.True(restored.Passing);
        }
        [Test] public void LoggedRelease19FormationFindsAnUncoveredGroupFiringPosition()
        {
            var store=Spacewars.Presentation.NativeBalanceStore.ForApplication(P,Path.GetFullPath("../.local/ai-turret-bypass/formation-profile.json"));
            store.Select(Spacewars.Presentation.NativeBalanceStore.DefaultId,19);var p=store.Active;var ai=store.ActiveAiProfile;
            var core=Enumerable.Range(0,8).Select(i=>new PlayableEntitySnapshot(9+i,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(9+(i%2)*3,8+(i/2)*2),100,false,0,0,0)).ToArray();
            var all=core.Append(new PlayableEntitySnapshot(7,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(-19,-12),80,false,0,0,0)).ToArray();
            var target=new PlayableBuildingSnapshot(2,PlayableOwner.Enemy,PlayableBuildingKind.Headquarters,new NavPoint(19,0),300,1,0,0,default);
            var tower=new PlayableBuildingSnapshot(4,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,new NavPoint(12.123355547812947,-4.996174644486021),300,1,0,0,default);
            var buildings=new[]{target,tower};var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(default,100)},Array.Empty<KnownBuilding>());
            PlayableAiObservation View(PlayableRouteProof[] routes)=>PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,407,7,53,53,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,all,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:routes,activeProfile:p),AiDifficulty.Expert);
            var geometry=new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p).Concat(buildings.Select(b=>{double radius=TerritoryRules.Radius(p,b.Kind);return new NavObstacle(b.Position.X-radius,b.Position.Z-radius,b.Position.X+radius,b.Position.Z+radius);})).ToArray(),1);
            var proofs=new List<PlayableRouteProof>();
            foreach(var anchor in AiTurretBypassPolicy.Positions(p,target,all))
            {
                var slots=NavArrivalAllocator.Allocate(geometry,p.Navigation.ForUnit(p.TankCollisionRadius,p.TankSpeed,p.TankTurnSpeed),anchor,core.Length,new[]{all.Last().Position});
                if(slots.Length!=core.Length)continue;
                for(int i=0;i<core.Length;i++)
                {
                    var unit=core[i];var path=new SharedFlowRouter(geometry,p.Navigation.ForUnit(p.TankCollisionRadius,p.TankSpeed,p.TankTurnSpeed)).FindPath(unit.Position,slots[i]);
                    if(path.Length>0)proofs.Add(new PlayableRouteProof(unit.Id,unit.Owner,407,53,1,unit.Position,p.TankCollisionRadius,PlayableRouteTargetKind.EnemyApproach,2,anchor,slots[i],path));
                }
            }
            var o=View(proofs.ToArray());var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Role=AiArmyRole.Attack,Members=core.Select(u=>u.Id).ToArray(),MinimumViableForce=8};
            var policy=new AiTurretBypassPolicy();policy.Battle(1);var move=policy.Propose(o,p,ai,army,1,null,null);Assert.NotNull(move);
            var selected=o.RouteProofs.Where(r=>r.Target.Equals(move.Target)).ToArray();Assert.AreEqual(8,selected.Length);
            Assert.True(selected.All(r=>!AiPositionDevelopmentPlanner.Covers(o,p,tower.Position,tower.Kind,r.Goal,false,tower.Id,r.UnitId)),"Every formation slot stays outside tower fire");
            Assert.GreaterOrEqual(selected.Count(r=>AiTurretBypassPolicy.UsefulShot(o,p,core.Single(u=>u.Id==r.UnitId),target,r.Goal)),ai.Value("armies.minimumUnits"),"A viable firing core can attack while its safe back row holds position");
        }
        [Test] public void CommonPositionsAreIndependentOfActorOriginAndRequireAUsefulShot()
        {
            var a=new[]{Unit(1),Unit(2)};var b=new[]{Unit(1,100,new NavPoint(-30,-20)),Unit(2,100,new NavPoint(20,20))};
            CollectionAssert.AreEqual(AiTurretBypassPolicy.Positions(P,Target(),a),AiTurretBypassPolicy.Positions(P,Target(),b));
            Assert.False(AiTurretBypassPolicy.UsefulShot(View(a),P,a[0],Target(),new NavPoint(-30,-30)));
        }
    }
}

using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroRecoveryTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        const long Gen=901;
        static PlayableBuildingSnapshot Tower(PlayableOwner owner=PlayableOwner.Player,ConstructionPhase phase=ConstructionPhase.Ready,int healing=0)=>
            new PlayableBuildingSnapshot(200,owner,PlayableBuildingKind.RepairTower,new NavPoint(-14,10),100,1,0,0,default,phase:phase,healing:new PlayableRepairTowerSnapshot(healing,0,0));
        static PlayableAiObservation View(PlayableBuildingSnapshot tower,int health=70,PlayableOrderOrigin origin=PlayableOrderOrigin.Ai,PlayableTacticalOrderKind? order=null,bool occupied=false,double occupiedDistance=3)
        {
            var units=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-28,10),health,false,0,0,0,
                order.HasValue?new PlayableTacticalOrderSnapshot(1,PlayableOwner.Player,Gen,1,0,order.Value,new NavPoint(-25,10),0):null,
                orderStamp:origin==PlayableOrderOrigin.Human?new PlayableOrderStamp(1,PlayableOwner.Player,Gen,1,1,0,origin,PlayableCommandKind.Stop,"human",0,0):null,maxHealth:100)};
            if(occupied)units=units.Concat(new[]{new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-14-occupiedDistance,10),50,false,0,0,0,held:true,maxHealth:100)}).ToArray();
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),50)},Array.Empty<KnownBuilding>());
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,0,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,tower==null?Array.Empty<PlayableBuildingSnapshot>():new[]{tower},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot()));
        }
        static AiCombatMicroPlan Plan(PlayableAiObservation o,AiProfile ai=null,PlayableProfile profile=null)
        {var p=profile??P;var m=new AiCombatMicroPolicy();var r=new AiArmyRegistry("player-1",Gen,ai??Ai,AiDifficulty.Veteran);m.Observe(o,p,ai??Ai,AiDifficulty.Veteran,r);return m.Plan(o,p,ai??Ai,AiDifficulty.Veteran,r);}
        [Test]
        public void IdleWoundedUsesObservedReadyTowerWithoutPrecomputedArmyProof()
        {
            var o=View(Tower());Assert.IsEmpty(o.RouteProofs);var plan=Plan(o);Assert.NotNull(plan);Assert.AreEqual(AiCombatMicroPhase.Repair,plan.Phase);Assert.AreEqual(200,plan.Action.ParentId);Assert.Zero(plan.Army);
            Assert.LessOrEqual(Math.Sqrt(Math.Pow(plan.Action.Target.X+14,2)+Math.Pow(plan.Action.Target.Z-10,2)),P.ExplorerRange*P.TurretRangeMultiplier);
            Assert.True(AiCombatMicroPolicy.RepairTimeAllowed(P,Ai,AiDifficulty.Veteran,o.Entities.Single(),5));
            Assert.False(AiCombatMicroPolicy.RepairTimeAllowed(P,Ai,AiDifficulty.Veteran,o.Entities.Single(),1000),"Actual path, rather than straight candidate distance, is checked at delivery");
        }
        [TestCase(6,8,false)]
        [TestCase(14,12,true)]
        public void RepairApproachAndObservedLoadUseCurrentRadius(double radius,double otherDistance,bool busy)
        {
            var data=P.CopyData();data.turretRange=radius;var profile=PlayableProfile.Create(data);
            Assert.AreEqual(10,profile.ExplorerRange*profile.TurretRangeMultiplier);
            var free=Plan(View(Tower()),profile:profile);Assert.NotNull(free);
            Assert.LessOrEqual(Math.Sqrt(Math.Pow(free.Action.Target.X+14,2)+Math.Pow(free.Action.Target.Z-10,2)),radius);
            var occupied=Plan(View(Tower(),occupied:true,occupiedDistance:otherDistance),profile:profile);
            Assert.AreEqual(busy,occupied==null,"Only wounded units inside the actual ordinary healing radius occupy the tower.");
        }
        [Test]
        public void ForeignUnreadyBusyOrAlreadyLoadedTowerCannotBeReserved()
        {
            foreach(var tower in new[]{Tower(PlayableOwner.Enemy),Tower(phase:ConstructionPhase.Constructing),Tower(healing:9),null})Assert.IsNull(Plan(View(tower)));
            Assert.IsNull(Plan(View(Tower(),occupied:true)));
        }
        [Test]
        public void QuietFriendlyGuardCanRepairBeforeArrivalButUrgentMissionRechecksAtDelivery()
        {
            var raw=AppliedView(70,true);var requested=new NavPoint(-26,10);
            PlayableEntitySnapshot Patient(NavPoint original)=>new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,raw.Entities[0].Position,70,false,0,0,0,raw.Entities[0].CurrentOrder,orderStamp:raw.Entities[0].OrderStamp,maxHealth:100,
                queue:new PlayableUnitQueueSnapshot(new PlayableQueuedOrderSnapshot(1,11,12,1,PlayableCommandKind.Move,new NavLocation(original,NavLocation.FlatSurface),0,aiActionId:88),null,Array.Empty<PlayableQueuedOrderSnapshot>(),null));
            var headquarters=new PlayableBuildingSnapshot(201,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(-20,18),100,1,0,0,default);
            var o=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,0,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,new[]{Patient(requested)}.Concat(raw.Entities.Skip(1)).ToArray(),raw.Buildings.Concat(new[]{headquarters}).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:raw.Vision));
            var registry=new AiArmyRegistry(o.OwnerId,Gen,Ai,AiDifficulty.Veteran);Assert.True(registry.TryCreate(o,AiArmyRole.MobileDefense,AiArmyPlanner.Policy,new[]{1,2,3},out var army));
            string objective=((int)PlayableRouteTargetKind.FriendlyAnchor)+":"+headquarters.Id;
            Assert.True(registry.UpdateMission(army,AiArmyPlanner.Policy,AiArmyPhase.Advancing,0,objective,rally:requested));
            var micro=new AiCombatMicroPolicy();micro.Observe(o,P,Ai,AiDifficulty.Veteran,registry);var plan=micro.Plan(o,P,Ai,AiDifficulty.Veteran,registry);
            Assert.NotNull(plan);Assert.AreEqual(AiCombatMicroPhase.Repair,plan.Phase);Assert.AreEqual(army,plan.Army);micro.Commit(plan,Gen,0);
            var intent=new AiIntent("quiet-guard-repair",AiCombatMicroPolicy.Policy,plan.Action,100,0,0,Array.Empty<string>());
            Assert.True(micro.Authorizes(intent,o,P,Ai,AiDifficulty.Veteran,registry));
            Assert.AreNotEqual(requested,o.Entities.Single(u=>u.Id==1).CurrentOrder.Destination,"Native member destinations need not equal the shared requested Guard point.");
            var unrelated=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,0,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,new[]{Patient(new NavPoint(-24,10))}.Concat(raw.Entities.Skip(1)).ToArray(),o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:raw.Vision));
            Assert.False(micro.Authorizes(intent,unrelated,P,Ai,AiDifficulty.Veteran,registry),"Same membership/role/objective and common AI source cannot authorize an unrelated live Move.");
            var hostile=new PlayableBuildingSnapshot(300,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,new NavPoint(-20,30),100,1,0,0,default);
            var threatened=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,0,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,o.Entities.ToArray(),o.Buildings.Concat(new[]{hostile}).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:raw.Vision));
            Assert.False(micro.Authorizes(intent,threatened,P,Ai,AiDifficulty.Veteran,registry),"Observed fire against the Guard anchor is an urgent current task.");
            Assert.True(registry.UpdateMission(army,AiArmyPlanner.Policy,AiArmyPhase.Engaging,0,objective));
            Assert.False(micro.Authorizes(intent,o,P,Ai,AiDifficulty.Veteran,registry),"A quiet Guard proposal cannot steal an urgent current mission after reaction delay.");
        }
        [Test]
        public void RepairDoesNotStealHumanOrMovingIdleUnitAndUsesActualControlKnobs()
        {
            Assert.IsNull(Plan(View(Tower(),origin:PlayableOrderOrigin.Human)));Assert.IsNull(Plan(View(Tower(),order:PlayableTacticalOrderKind.Move)));
            foreach(var setting in new[]{new{Key="repairEnabled",Value=0d},new{Key="intensity",Value=0d},new{Key="idleRepairHealth",Value=.5d},new{Key="repairTravelSeconds",Value=1d},new{Key="maxPhaseSeconds",Value=1d}})
            {var data=Ai.CopyData();NativeBalanceFields.Write(AiProfileMetadata.Field("difficulty.veteran.micro."+setting.Key),data,setting.Value);Assert.IsNull(Plan(View(Tower()),new AiProfile(data)),setting.Key);}
        }
        static AiCombatMicroPolicy AppliedClient(PlayableAiObservation o,AiArmyRegistry registry,AiCombatMicroPhase phase,long army)
        {
            var micro=new AiCombatMicroPolicy();var a=new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,Gen,0,PlayableCommandKind.Move,new[]{1},new NavPoint(-27,10),parentId:phase==AiCombatMicroPhase.Repair?200:0,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            micro.Commit(new AiCombatMicroPlan{Action=a,Phase=phase,Army=army},Gen,0);
            var actual=new PlayableAiAction(88,a.PlayerId,a.ProfileId,a.ProfileRevision,a.Generation,0,a.Kind,a.CopyEntityIds(),a.Target,parentId:a.ParentId,seed:a.Seed,sourceIdentity:a.SourceIdentity);var identity=new AiReceiptIdentity(Gen,o.OwnerId,1,1);
            micro.Applied(actual,12,0,identity);micro.Receipt(new PlayableAiTraceRecord(o.Identity,1,0,12,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"control",o.OwnerId,a.SourceIdentity,identity,AiCombatMicroPolicy.Policy,a.Kind));return micro;
        }
        static PlayableAiObservation AppliedView(int health,bool includeCore)
        {
            var raw=View(Tower(),health);var stamp=new PlayableOrderStamp(1,PlayableOwner.Player,Gen,1,12,0,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,88,88);
            var order=new PlayableTacticalOrderSnapshot(1,PlayableOwner.Player,Gen,12,0,PlayableTacticalOrderKind.Move,new NavPoint(-27,10),0);
            var units=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-28,10),health,false,0,0,0,order,orderStamp:stamp,maxHealth:100)};
            if(includeCore)units=units.Concat(new[]{new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-25,14),100,false,0,0,0,maxHealth:100),new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-25,6),100,false,0,0,0,maxHealth:100)}).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,0,0,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,raw.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:raw.Vision));
        }
        [TestCase("current",true)][TestCase("installed",false)][TestCase("human",false)]
        [TestCase("sequence",false)][TestCase("action",false)][TestCase("other-tower",false)]
        [TestCase("withdraw",false)][TestCase("other-army",false)][TestCase("healed",false)]
        [TestCase("expired",false)][TestCase("new-damage",false)]
        public void PendingLocalRepairCanYieldOnlyToCurrentSameTowerArmyRecovery(string changed,bool expected)
        {
            var raw=AppliedView(70,true);var registry=new AiArmyRegistry(raw.OwnerId,Gen,Ai,AiDifficulty.Veteran);
            Assert.True(registry.TryCreate(raw,AiArmyRole.MobileDefense,AiArmyPlanner.Policy,new[]{1,2,3},out var army,requiredMinimum:1));
            var micro=new AiCombatMicroPolicy();var localGoal=new NavPoint(-22,10);
            micro.Observe(raw,P,Ai,AiDifficulty.Veteran,registry);
            var local=new PlayableAiAction(1,raw.OwnerId,P.ProfileId,P.Revision,Gen,0,PlayableCommandKind.Move,new[]{1},localGoal,parentId:200,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            micro.Commit(new AiCombatMicroPlan{Action=local,Phase=changed=="withdraw"?AiCombatMicroPhase.Withdraw:AiCombatMicroPhase.Repair,Army=changed=="other-army"?army+1:army},Gen,0);
            var applied=new PlayableAiAction(88,raw.OwnerId,P.ProfileId,P.Revision,Gen,0,local.Kind,new[]{1},localGoal,parentId:200,seed:7,sourceIdentity:local.SourceIdentity);
            var identity=new AiReceiptIdentity(Gen,raw.OwnerId,1,1);micro.Applied(applied,12,0,identity);
            micro.Receipt(new PlayableAiTraceRecord(raw.Identity,1,0,12,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"applied local repair",raw.OwnerId,local.SourceIdentity,identity,AiCombatMicroPolicy.Policy,local.Kind));
            var pending=changed=="installed"?null:new PlayableQueuedOrderSnapshot(1,11,changed=="sequence"?13:12,1,PlayableCommandKind.Move,new NavLocation(localGoal,NavLocation.FlatSurface),0,aiActionId:changed=="action"?89:88);
            var stamp=changed=="human"?new PlayableOrderStamp(1,PlayableOwner.Player,Gen,1,13,0,PlayableOrderOrigin.Human,PlayableCommandKind.Stop,"human",0,0):null;
            var patient=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,raw.Entities[0].Position,changed=="healed"?100:changed=="new-damage"?50:70,false,0,0,0,orderStamp:stamp,maxHealth:100,
                queue:new PlayableUnitQueueSnapshot(null,pending,Array.Empty<PlayableQueuedOrderSnapshot>(),null,pendingAiRouteViable:true));
            long tick=changed=="expired"?AiProfile.SecondsToTicks(Ai.DifficultyValue(AiDifficulty.Veteran,"micro.maxPhaseSeconds"),30)+1:1;
            var view=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,0,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,new[]{patient}.Concat(raw.Entities.Skip(1)).ToArray(),raw.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:raw.Vision));
            var recovery=new PlayableAiAction(99,raw.OwnerId,P.ProfileId,P.Revision,Gen,0,PlayableCommandKind.Move,new[]{1},new NavPoint(-20,10),parentId:changed=="other-tower"?201:200,seed:7,sourceIdentity:local.SourceIdentity);
            var before=WorldWire.Pack(micro.Write);
            Assert.AreEqual(expected,micro.CanYieldPendingRepair(view,P,Ai,AiDifficulty.Veteran,registry,AiArmyPlanner.Policy,recovery));
            CollectionAssert.AreEqual(before,WorldWire.Pack(micro.Write),"A handoff eligibility query never retires or rewrites the accepted local job.");
        }
        [Test]
        public void StronglyDamagedAppliedWithdrawalConvertsToRepairBeforeReturn()
        {
            var o=AppliedView(28,true);var registry=new AiArmyRegistry(o.OwnerId,Gen,Ai,AiDifficulty.Veteran);Assert.True(registry.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var army));
            var micro=AppliedClient(o,registry,AiCombatMicroPhase.Withdraw,army);micro.Observe(o,P,Ai,AiDifficulty.Veteran,registry);var repair=micro.Plan(o,P,Ai,AiDifficulty.Veteran,registry);
            Assert.NotNull(repair);Assert.AreEqual(AiCombatMicroPhase.Repair,repair.Phase);Assert.AreEqual(200,repair.Action.ParentId);Assert.AreEqual(army,repair.Army);Assert.AreEqual(PlayableCommandKind.Move,repair.Action.Kind);
            micro.Commit(repair,Gen,0);Assert.True(micro.Reserves(1));
        }
        [Test]
        public void HealedFreeRepairClientReleasesImmediatelyForNormalAssignment()
        {
            var o=AppliedView(90,false);var registry=new AiArmyRegistry(o.OwnerId,Gen,Ai,AiDifficulty.Veteran);var micro=AppliedClient(o,registry,AiCombatMicroPhase.Repair,0);
            micro.Observe(o,P,Ai,AiDifficulty.Veteran,registry);Assert.True(micro.Reserves(1));Assert.IsNull(micro.Plan(o,P,Ai,AiDifficulty.Veteran,registry));Assert.False(micro.Reserves(1),"Free healed patient must release at the actual health threshold, not wait30seconds");
        }
        [Test]
        public void HealedSoloGuardReleasesAtSafeTowerWithoutInventingAJoinCore()
        {
            var o=AppliedView(90,false);var registry=new AiArmyRegistry(o.OwnerId,Gen,Ai,AiDifficulty.Veteran);Assert.True(registry.TryCreate(o,AiArmyRole.MobileDefense,AiArmyPlanner.Policy,new[]{1},out var army,requiredMinimum:1));
            var micro=AppliedClient(o,registry,AiCombatMicroPhase.Repair,army);micro.Observe(o,P,Ai,AiDifficulty.Veteran,registry);
            Assert.True(micro.Reserves(1));Assert.IsNull(micro.Plan(o,P,Ai,AiDifficulty.Veteran,registry));Assert.False(micro.Reserves(1));Assert.AreEqual(army,registry.ArmyFor(1),"Normal registry assignment survives; no fake core or ownership transfer");
        }
        [Test]
        public void ReactionDeliveryRechecksTowerLoadAndCurrentReadyState()
        {
            var o=View(Tower());var micro=new AiCombatMicroPolicy();var registry=new AiArmyRegistry("player-1",Gen,Ai,AiDifficulty.Veteran);micro.Observe(o,P,Ai,AiDifficulty.Veteran,registry);var plan=micro.Plan(o,P,Ai,AiDifficulty.Veteran,registry);Assert.NotNull(plan);micro.Commit(plan,Gen,0);
            var intent=new AiIntent("repair-control",AiCombatMicroPolicy.Policy,plan.Action,100,0,0,Array.Empty<string>());
            Assert.True(micro.Authorizes(intent,o,P,Ai,AiDifficulty.Veteran,registry));Assert.False(micro.Authorizes(intent,View(Tower(healing:9)),P,Ai,AiDifficulty.Veteran,registry));
            Assert.False(micro.Authorizes(intent,View(Tower(phase:ConstructionPhase.Constructing)),P,Ai,AiDifficulty.Veteran,registry));Assert.False(micro.Authorizes(intent,View(Tower(),occupied:true),P,Ai,AiDifficulty.Veteran,registry));
        }
    }
}

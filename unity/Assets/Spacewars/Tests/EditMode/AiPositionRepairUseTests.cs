using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiPositionRepairUseTests
    {
        [TestCase("ground-center",true)][TestCase("ground-behind",true)]
        [TestCase("air-center",false)][TestCase("air-behind",true)]
        [TestCase("foreign-leader",false)][TestCase("stale-route",false)][TestCase("long-route",false)]
        public void RepairedGroundFollowRecognizesCurrentRadialLeaderProof(string mode,bool expected)
        {
            var p=PlayableProfile.Default;const long tick=9000;
            var at=new NavPoint(-17.24383835218257,-1.4141440852885856);
            var leaderAt=new NavPoint(-15.799850387394224,-3.0750966359005467);
            var leader=new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Explorer,leaderAt,70,false,0,-.11645872549896459,0);
            bool air=mode.StartsWith("air",StringComparison.Ordinal),behind=mode.EndsWith("behind",StringComparison.Ordinal);
            var unit=new PlayableEntitySnapshot(20,PlayableOwner.Player,air?PlayableEntityKind.Hawk:PlayableEntityKind.Tank,at,100,false,0,0,0,
                new PlayableTacticalOrderSnapshot(20,PlayableOwner.Player,107,7,8990,PlayableTacticalOrderKind.Follow,default,mode=="foreign-leader"?11:10));
            var target=behind?PlayableUnitRules.FollowGoal(p,leaderAt,leader.HullHeading):leaderAt;
            var goal=behind?target:new NavPoint(-19.396609419645984,-3.22782012432127);
            var path=mode=="long-route"?new[]{new NavPoint(-16.4,2),goal}:new[]{goal};
            var proof=new PlayableRouteProof(20,PlayableOwner.Player,107,mode=="stale-route"?tick-1:tick,1,at,PlayableUnitRules.Radius(p,unit.Kind),PlayableRouteTargetKind.FriendlyAnchor,10,target,goal,path);
            var vision=new PlayableVision(0,64,64,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(-16,0),64)},Array.Empty<KnownBuilding>());
            var o=PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,107,7,0,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,new[]{leader,unit},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:new[]{proof},activeProfile:p));
            var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Role=AiArmyRole.MobileDefense,Phase=AiArmyPhase.Advancing,Members=new[]{10,20},Reinforcements=new[]{20},LeaderId=10,MinimumViableForce=1};
            Assert.AreEqual(expected,AiRepairUsePolicy.ReadyForOriginalLeader(o,p,army,unit));
        }
        [TestCase("valid",true)]
        [TestCase("saved-action",false)]
        [TestCase("receipt-action",false)]
        [TestCase("policy",false)]
        [TestCase("owner",false)]
        [TestCase("receipt-owner",false)]
        [TestCase("generation",false)]
        [TestCase("sequence",false)]
        [TestCase("source",false)]
        [TestCase("unapplied",false)]
        [TestCase("human",false)]
        [TestCase("held",false)]
        [TestCase("recovered",false)]
        [TestCase("expired",false)]
        [TestCase("tower-lost",false)]
        public void RecoveringBindsLocalCallbackToDistinctGlobalAuthority(string changed,bool expected)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;
            const int unitId=20,towerId=30,sequence=7;const long action=42,globalAction=314,issued=8990;
            var towerAt=new NavPoint(-20,0);var goal=new NavPoint(-18,0);
            var order=new PlayableTacticalOrderSnapshot(unitId,PlayableOwner.Player,107,sequence,issued,PlayableTacticalOrderKind.Move,goal,0);
            var stamp=new PlayableOrderStamp(unitId,PlayableOwner.Player,107,1,sequence,issued,
                changed=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Move,
                PlayableAiOpeningComposition.SourceIdentity,globalAction,globalAction);
            var unit=new PlayableEntitySnapshot(unitId,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-17,0),
                changed=="recovered"?p.TankHealth:30,false,0,0,0,order,held:changed=="held",orderStamp:stamp);
            var tower=new PlayableBuildingSnapshot(towerId,PlayableOwner.Player,PlayableBuildingKind.RepairTower,towerAt,
                changed=="tower-lost"?0:TerritoryRules.Health(p,PlayableBuildingKind.RepairTower),1,0,0,default);
            long tick=changed=="expired"?issued+AiProfile.SecondsToTicks(ai.Value("positionDevelopment.repairWaitSeconds"),30):9000;
            var o=AiPositionDevelopmentTests.View(extra:new[]{tower},units:new[]{unit},tick:tick);
            var memory=new Dictionary<string,long>{{"repair-use:"+unitId+":"+towerId+":"+(changed=="saved-action"?action+1:action),issued}};
            // OwnerLoop.Finish stores the global record but translates the
            // feedback ActionId back to the policy-local action before callback.
            var callback=new PlayableAiTraceRecord(o.Identity,changed=="receipt-action"?action+1:action,issued,sequence,issued,
                PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"local callback",o.OwnerId,
                PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,sequence),AiArmyPlanner.Policy,PlayableCommandKind.Move);
            AiRepairUsePolicy.Receipt(memory,callback);
            var record=new PlayableAiTraceRecord(o.Identity,globalAction,issued,
                changed=="sequence"?sequence+1:sequence,issued,
                changed=="unapplied"?PlayableAiDeliveryStatus.Accepted:PlayableAiDeliveryStatus.Applied,
                PlayableCommandStatus.Applied,"repair receipt binding",changed=="owner"?"foreign-owner":o.OwnerId,
                changed=="source"?"foreign-source":PlayableAiOpeningComposition.SourceIdentity,
                new AiReceiptIdentity(changed=="generation"?108:107,changed=="receipt-owner"?"foreign-owner":o.OwnerId,1,sequence),
                changed=="policy"?"foreign-policy":AiArmyPlanner.Policy,PlayableCommandKind.Move);
            var clients=AiRepairUsePolicy.Recovering(o,p,ai,memory,AiArmyPlanner.Policy,new[]{record});
            Assert.AreEqual(expected,clients.Contains(unitId),changed);
            Assert.AreEqual(1,memory.Count,"Observation cannot mutate saved return ownership");
            AiRepairUsePolicy.StartHealedReturns(o,p,ai,memory,AiArmyPlanner.Policy,new[]{record});
            Assert.AreEqual(changed=="recovered",memory.ContainsKey("repair-return:"+unitId),"only exact delivered and actually healed client starts a return budget");
            if(changed=="recovered")
            {
                long first=memory["repair-return:"+unitId];
                AiRepairUsePolicy.StartHealedReturns(o,p,ai,memory,AiArmyPlanner.Policy,new[]{record});
                Assert.AreEqual(first,memory["repair-return:"+unitId],"repeated decisions cannot extend the return deadline");
                Assert.True(AiRepairUsePolicy.HasLiveRecoveryBudget(o,ai,memory));
                var expired=AiPositionDevelopmentTests.View(extra:new[]{tower},units:new[]{unit},tick:first+AiProfile.SecondsToTicks(ai.Value("positionDevelopment.repairWaitSeconds"),30));
                Assert.False(AiRepairUsePolicy.HasLiveRecoveryBudget(expired,ai,memory));
                AiRepairUsePolicy.Returned(memory,unitId);Assert.IsEmpty(memory);
            }

        }

        [TestCase("valid",true)]
        [TestCase("staging",true)]
        [TestCase("retreating",false)]
        [TestCase("emergency-staging",false)]
        [TestCase("local-id",false)]
        [TestCase("memo",false)]
        [TestCase("owner",false)]
        [TestCase("generation",false)]
        [TestCase("source",false)]
        [TestCase("unit",false)]
        [TestCase("tower",false)]
        [TestCase("tower-lost",false)]
        [TestCase("busy",false)]
        [TestCase("human",false)]
        [TestCase("held",false)]
        [TestCase("expired",false)]
        [TestCase("transferred",false)]
        [TestCase("registry-generation",false)]
        [TestCase("not-returning",false)]
        [TestCase("leader",false)]
        [TestCase("weak-core",false)]
        [TestCase("core-human",false)]
        [TestCase("recovered",false)]
        public void RepairDeliveryRequiresSavedClientAndCurrentViableArmy(string changed,bool expected)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;
            const long issued=8990,local=42,global=314;
            long tick=changed=="expired"?issued+AiProfile.SecondsToTicks(ai.Value("positionDevelopment.repairWaitSeconds"),30):9000;
            double range=PlayableUnitRules.Range(p,PlayableEntityKind.Tank);
            var goal=new NavPoint(-range-2*PlayableUnitRules.Radius(p,PlayableEntityKind.Tank)-2,0);
            var towerAt=new NavPoint(goal.X-2,goal.Z);
            var human=new PlayableOrderStamp(20,PlayableOwner.Player,107,1,9,issued,PlayableOrderOrigin.Human,PlayableCommandKind.Move,"human",1,1);
            var coreHuman=new PlayableOrderStamp(11,PlayableOwner.Player,107,1,10,issued,PlayableOrderOrigin.Human,PlayableCommandKind.Move,"human",2,2);
            var core=Enumerable.Range(10,3).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-25,id-10),
                changed=="weak-core"?1:p.TankHealth,false,0,0,0,orderStamp:changed=="core-human"&&id==11?coreHuman:null)).ToArray();
            var client=new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-range/2,0),
                changed=="recovered"?p.TankHealth:30,false,0,0,0,held:changed=="held",orderStamp:changed=="human"?human:null);
            var enemy=new PlayableEntitySnapshot(100,PlayableOwner.Enemy,PlayableEntityKind.Tank,default,p.TankHealth,false,0,0,0);
            var tower=new PlayableBuildingSnapshot(30,PlayableOwner.Player,PlayableBuildingKind.RepairTower,towerAt,
                changed=="tower-lost"?0:TerritoryRules.Health(p,PlayableBuildingKind.RepairTower),1,0,0,default,
                healing:changed=="busy"?new PlayableRepairTowerSnapshot(99,0,0):null);
            var units=core.Concat(new[]{client,enemy}).ToArray();
            var o=AiPositionDevelopmentTests.View(extra:new[]{tower},units:units,tick:tick);
            var registry=new AiArmyRegistry(o.OwnerId,o.Generation,ai,AiDifficulty.Veteran);
            Assert.True(registry.TryCreate(AiPositionDevelopmentTests.View(extra:new[]{tower},units:units,tick:issued),changed=="emergency-staging"?AiArmyRole.MobileDefense:AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{10,11,12,20},out long army));
            var phase=changed=="staging"||changed=="emergency-staging"?AiArmyPhase.Staging:changed=="retreating"?AiArmyPhase.Retreating:AiArmyPhase.Engaging;
            Assert.True(registry.UpdateMission(army,AiArmyPlanner.Policy,phase,issued,"fixture-pressure"));
            Assert.True(registry.BeginRepairReturn(army,AiArmyPlanner.Policy,20));
            if(changed=="not-returning")registry.JoinReinforcements(army,AiArmyPlanner.Policy,new[]{20});
            if(changed=="transferred"||changed=="leader"||changed=="registry-generation")
            {
                var state=registry.Capture();var row=state.Armies.Single();
                if(changed=="transferred"){row.Members=row.Members.Where(id=>id!=20).ToArray();row.Reinforcements=Array.Empty<int>();}
                else if(changed=="leader"){row.LeaderId=20;row.Reinforcements=Array.Empty<int>();}
                else state.Generation=108;
                registry=AiArmyRegistry.Restore(state,ai,AiDifficulty.Veteran,tick);
            }
            var planner=new AiArmyPlanner(o.OwnerId,o.Generation);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(string name,object value)=>typeof(AiArmyPlanner).GetField(name,flags).SetValue(planner,value);
            Set("state",new AiArmyMissionState{ArmyId=army,Phase=phase,StartedTick=issued,PhaseTick=issued,ProgressTick=issued});
            Set("nextId",43L);Set("pendingId",local);Set("pendingGeneration",107L);
            Set("completed",new Dictionary<string,long>{{"repair-use:20:30:"+(changed=="memo"?43:42),issued}});
            var action=new PlayableAiAction(global,changed=="owner"?"foreign-owner":o.OwnerId,p.ProfileId,p.Revision,changed=="generation"?108:107,o.SnapshotSequence,
                PlayableCommandKind.Move,new[]{changed=="unit"?99:20},goal,parentId:changed=="tower"?31:30,seed:o.Seed,
                sourceIdentity:changed=="source"?"foreign-source":PlayableAiOpeningComposition.SourceIdentity);
            bool valid=planner.CanDeliverRepairReturn(o,p,ai,registry,action,changed=="local-id"?43:local);
            Assert.AreEqual(expected,valid,changed);
            if(expected)
            {
                Assert.AreNotEqual(local,action.ActionId,"The owner item must bridge distinct action namespaces");
                Assert.False(AiTacticalExecutor.SafePathForUnit(o,client.Position,new[]{goal},PlayableUnitRules.Radius(p,client.Kind),p,false));
                Assert.True(AiTacticalExecutor.WithdrawalPathForUnit(o,client.Position,new[]{goal},PlayableUnitRules.Radius(p,client.Kind),p,false),"Safe monotonic withdrawal must leave the observed enemy envelope");
            }
        }
        [TestCase("offensive-reinforcement",true)]
        [TestCase("offensive-staging",true)]
        [TestCase("offensive-insufficient",false)]
        [TestCase("staging",true)]
        [TestCase("advancing",true)]
        [TestCase("emergency",false)]
        [TestCase("retreating",false)]
        [TestCase("weak-core",false)]
        [TestCase("human",false)]
        [TestCase("busy",false)]
        [TestCase("unsafe",false)]
        public void QuietGuardRepairKeepsStoredMinimumAndRealClient(string changed,bool expected)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;const long tick=9000;
            var at=new NavPoint(-16,-8);var towerAt=new NavPoint(-24,-8);var goal=new NavPoint(-22,-8);
            var leader=new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-20,-12),changed=="weak-core"?1:p.TankHealth,false,0,0,0);
            var human=new PlayableOrderStamp(20,PlayableOwner.Player,107,1,7,tick,PlayableOrderOrigin.Human,PlayableCommandKind.Move,"human",1,1);
            var client=new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Tank,at,30,false,0,0,0,orderStamp:changed=="human"?human:null);
            var tower=new PlayableBuildingSnapshot(30,PlayableOwner.Player,PlayableBuildingKind.RepairTower,towerAt,TerritoryRules.Health(p,PlayableBuildingKind.RepairTower),1,0,0,default,
                healing:changed=="busy"?new PlayableRepairTowerSnapshot(99,0,0):null);
            var proof=new PlayableRouteProof(20,PlayableOwner.Player,107,tick,1,at,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,30,towerAt,goal,new[]{goal});
            var enemies=changed=="unsafe"?new[]{new PlayableEntitySnapshot(100,PlayableOwner.Enemy,PlayableEntityKind.Tank,goal,p.TankHealth,false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>();
            bool offensive=changed.StartsWith("offensive",StringComparison.Ordinal);
            var extraCore=offensive?Enumerable.Range(11,changed=="offensive-insufficient"?2:3).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-20,-12-id),p.TankHealth,false,0,0,0)).ToArray():Array.Empty<PlayableEntitySnapshot>();
            var o=AiPositionDevelopmentTests.View(extra:new[]{tower},units:new[]{leader,client}.Concat(extraCore).Concat(enemies).ToArray(),proofs:new[]{proof},tick:tick);
            var registry=new AiArmyRegistry(o.OwnerId,o.Generation,ai,AiDifficulty.Veteran);
            Assert.True(registry.TryCreate(o,offensive?AiArmyRole.Attack:AiArmyRole.MobileDefense,AiArmyPlanner.Policy,new[]{10,20}.Concat(extraCore.Select(u=>u.Id)).Where(id=>changed!="offensive-reinforcement"||id!=20),out long id,requiredMinimum:offensive?4:(int?)null));
            if(changed=="offensive-reinforcement")Assert.True(registry.TryReinforce(o,id,AiArmyPlanner.Policy,new[]{20}));
            var phase=changed=="retreating"?AiArmyPhase.Retreating:changed=="advancing"?AiArmyPhase.Advancing:AiArmyPhase.Staging;
            Assert.True(registry.UpdateMission(id,AiArmyPlanner.Policy,phase,tick,changed=="emergency"?"emergency-defense":((int)PlayableRouteTargetKind.FriendlyAnchor)+":1"));
            var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(p);
            typeof(AiArmyPlanner).GetField("state",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(planner,new AiArmyMissionState{ArmyId=id,Phase=phase,StartedTick=tick,PhaseTick=tick,ProgressTick=tick,TargetKind=offensive?PlayableRouteTargetKind.PublicObjective:PlayableRouteTargetKind.FriendlyAnchor,TargetId=offensive?2:1});
            var action=planner.ProposeWithRecords(o,AiPositionDevelopmentTests.Opening(PlayableAiOpening.Safe,PlayableAiOpeningPhase.Complete),p,ai,registry,Array.Empty<PlayableAiTraceRecord>());
            Assert.AreEqual(expected,action!=null&&action.ParentId==30,changed);
            if(!expected)return;
            Assert.AreEqual(offensive?4:1,registry.CaptureArmy(id).Single().MinimumViableForce,"preserve the actual saved minimum");
            planner.Commit(o,action,registry);
            var record=new PlayableAiTraceRecord(o.Identity,action.ActionId,tick,7,tick,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"repair",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,7),AiArmyPlanner.Policy,PlayableCommandKind.Move);
            planner.ObserveReceipt(record);
            Assert.True(registry.CaptureArmy(id).Single().Reinforcements.Contains(20));
            Assert.True(planner.Capture().RepairClients.ContainsKey("repair-use:20:30:"+action.ActionId+":7"));
            var global=new PlayableAiAction(314,o.OwnerId,p.ProfileId,p.Revision,107,o.SnapshotSequence,PlayableCommandKind.Move,new[]{20},goal,parentId:30,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            // A separate pending delivery is tested by the existing saved-client
            // matrix; this test proves the actual planner reaches and commits repair.
            Assert.False(planner.OwnsPendingRepairReturn(global,action.ActionId),"terminal callback clears pending, retaining the client");
            if(offensive)return;
            var mission=planner.Capture();mission.Target=leader.Position;
            typeof(AiArmyPlanner).GetField("state",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(planner,mission);
            var free=new PlayableEntitySnapshot(40,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-18,-12),p.TankHealth,false,0,0,0);
            var outpost=new PlayableBuildingSnapshot(31,PlayableOwner.Player,PlayableBuildingKind.Outpost,new NavPoint(20,20),TerritoryRules.Health(p,PlayableBuildingKind.Outpost),1,0,0,default);
            var resting=AiPositionDevelopmentTests.View(extra:new[]{tower,outpost},units:new[]{leader,client,free},tick:tick+1);
            planner.Observe(resting,registry,ai,new[]{record},replanQuietGuards:false);
            Assert.True(planner.Active,"completed guard positioning cannot release its unfinished repair client to assign another asset");
            Assert.True(planner.Capture().RepairClients.Keys.Any(k=>k.StartsWith("repair-use:20:30:")));

        }

        [TestCase("valid",true)]
        [TestCase("no-memo",false)]
        [TestCase("foreign-order",false)]
        [TestCase("stale-route",false)]
        [TestCase("weak-core",false)]
        [TestCase("core-human",false)]
        [TestCase("not-healed",false)]
        [TestCase("expired",true)]
        public void QuietGuardReturnRequiresSavedEffectiveRepairAndCurrentSafeCore(string changed,bool expected)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;const long issued=8990;
            long tick=changed=="expired"?issued+AiProfile.SecondsToTicks(ai.Value("positionDevelopment.repairWaitSeconds"),30):9000;
            var towerAt=new NavPoint(-24,-8);var at=new NavPoint(-22,-8);
            var leader=new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-17,-12),changed=="weak-core"?1:p.TankHealth,false,0,0,0,
                orderStamp:changed=="core-human"?new PlayableOrderStamp(10,PlayableOwner.Player,107,1,8,issued,PlayableOrderOrigin.Human,PlayableCommandKind.Move,"human",1,1):null);
            var order=new PlayableTacticalOrderSnapshot(20,PlayableOwner.Player,107,7,issued,PlayableTacticalOrderKind.Move,at,0);
            var stamp=new PlayableOrderStamp(20,PlayableOwner.Player,107,1,7,issued,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,changed=="foreign-order"?315:314,changed=="foreign-order"?315:314);
            var unit=new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Tank,at,changed=="not-healed"||changed=="expired"?30:p.TankHealth,false,0,0,0,order,orderStamp:stamp);
            var tower=new PlayableBuildingSnapshot(30,PlayableOwner.Player,PlayableBuildingKind.RepairTower,towerAt,TerritoryRules.Health(p,PlayableBuildingKind.RepairTower),1,0,0,default);
            var goal=PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading);
            var proof=new PlayableRouteProof(20,PlayableOwner.Player,107,changed=="stale-route"?tick-1:tick,1,at,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,10,goal,goal,new[]{goal});
            var o=AiPositionDevelopmentTests.View(extra:new[]{tower},units:new[]{leader,unit},proofs:new[]{proof},tick:tick,activeProfile:p);
            var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Role=AiArmyRole.MobileDefense,Phase=AiArmyPhase.Staging,Objective=((int)PlayableRouteTargetKind.FriendlyAnchor)+":1",Members=new[]{10,20},Reinforcements=new[]{20},LeaderId=10,MinimumViableForce=1};
            var memory=new Dictionary<string,long>();if(changed!="no-memo")memory.Add("repair-use:20:30:42:7",issued);
            var receipt=new PlayableAiTraceRecord(o.Identity,314,issued,7,issued,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"repair",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,7),AiArmyPlanner.Policy,PlayableCommandKind.Move);
            var action=AiRepairUsePolicy.ReturnAssembly(o,p,ai,army,43,memory,AiArmyPlanner.Policy,new[]{receipt});
            Assert.AreEqual(expected,action!=null,changed+" quiet="+AiRepairUsePolicy.QuietGuard(army)+" clients="+string.Join(",",AiRepairUsePolicy.Clients(memory))+" delivered="+string.Join(",",AiRepairUsePolicy.Recovering(o,p,ai,memory,AiArmyPlanner.Policy,new[]{receipt},includeRecovered:true,includeExpired:true))+" safe="+AiTacticalExecutor.SafeRoute(o,proof,p)+" coreRatio="+PlayableUnitRules.HealthRatio(p,leader)+" phase="+army.Phase+" proofs="+o.RouteProofs.Count);
            if(expected){Assert.AreEqual(PlayableCommandKind.Follow,action.Kind);Assert.AreEqual(10,action.TargetId);CollectionAssert.AreEqual(new[]{20},action.EntityIds);}
        }
        [TestCase("advancing",true)][TestCase("retreating",true)][TestCase("recovering",true)]
        [TestCase("core-count",false)][TestCase("weak-group",false)][TestCase("unhealed",false)]
        [TestCase("expired",false)][TestCase("human-core",false)][TestCase("stale-route",false)][TestCase("no-memo",false)][TestCase("near-other-core",true)][TestCase("leader-no-route",false)]
        public void OrganizedReturnCountsHealedClientOnlyTowardStoredGroupViability(string changed,bool expected)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;const long issued=8990,tick=9000;
            var core=Enumerable.Range(10,changed=="core-count"?1:2).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-17,id-10),changed=="weak-group"?20:94,false,0,0,0,
                orderStamp:changed=="human-core"&&id==10?new PlayableOrderStamp(id,PlayableOwner.Player,107,1,8,issued,PlayableOrderOrigin.Human,PlayableCommandKind.Move,"human",1,1):null)).ToArray();
            var at=new NavPoint(-22,-8);var tower=new PlayableBuildingSnapshot(30,PlayableOwner.Player,PlayableBuildingKind.RepairTower,new NavPoint(-24,-8),TerritoryRules.Health(p,PlayableBuildingKind.RepairTower),1,0,0,default);
            var order=new PlayableTacticalOrderSnapshot(20,PlayableOwner.Player,107,7,issued,PlayableTacticalOrderKind.Move,at,0);
            var stamp=new PlayableOrderStamp(20,PlayableOwner.Player,107,1,7,issued,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,314,314);
            var client=new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Tank,at,changed=="unhealed"?30:90,false,0,0,0,order,orderStamp:stamp);
            var goal=PlayableUnitRules.FollowGoal(p,core[0].Position,core[0].HullHeading);
            var proof=new PlayableRouteProof(20,PlayableOwner.Player,107,changed=="stale-route"?tick-1:tick,1,at,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,10,goal,goal,new[]{goal});
            var proofs=new System.Collections.Generic.List<PlayableRouteProof>();if(changed!="leader-no-route")proofs.Add(proof);
            if(changed=="near-other-core"||changed=="leader-no-route"){var other=core[1];var otherGoal=PlayableUnitRules.FollowGoal(p,other.Position,other.HullHeading);proofs.Add(new PlayableRouteProof(20,PlayableOwner.Player,107,tick,1,at,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,11,otherGoal,otherGoal,new[]{otherGoal}));}
            var o=AiPositionDevelopmentTests.View(extra:new[]{tower},units:core.Concat(new[]{client}).ToArray(),proofs:proofs.ToArray(),tick:tick,activeProfile:p);
            var phase=changed=="retreating"?AiArmyPhase.Retreating:changed=="recovering"?AiArmyPhase.Recovering:AiArmyPhase.Advancing;
            var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Role=AiArmyRole.Attack,Phase=phase,Objective="1:2",Members=core.Select(u=>u.Id).Concat(new[]{20}).ToArray(),Reinforcements=new[]{20},LeaderId=10,MinimumViableForce=2};
            var memory=new Dictionary<string,long>();if(changed!="no-memo")memory.Add("repair-use:20:30:42:7",changed=="expired"?tick-AiProfile.SecondsToTicks(ai.Value("positionDevelopment.repairWaitSeconds"),30):issued);
            var receipt=new PlayableAiTraceRecord(o.Identity,314,issued,7,issued,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"repair",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,7),AiArmyPlanner.Policy,PlayableCommandKind.Move);
            var action=AiRepairUsePolicy.ReturnAssembly(o,p,ai,army,43,memory,AiArmyPlanner.Policy,new[]{receipt});Assert.AreEqual(expected,action!=null,changed);
            Assert.AreEqual(phase,army.Phase,"Planning a safe return cannot resume offense or promote membership");CollectionAssert.AreEqual(new[]{20},army.Reinforcements);Assert.AreEqual(2,army.MinimumViableForce);
            if(expected){Assert.AreEqual(PlayableCommandKind.Follow,action.Kind);Assert.AreEqual(10,action.TargetId);}
        }
        [TestCase("regrouping",false)][TestCase("retreating",false)][TestCase("moving",false)]
        [TestCase("arrived",true)][TestCase("unapplied",false)][TestCase("stale-home",false)][TestCase("advancing",true)]
        [TestCase("intermediate",true)][TestCase("in-flight",false)][TestCase("returned",false)]
        public void ReturnCallerCoordinatesSettledLeaderLegAndPhysicalFollowBeforeHomeContinuation(string mode,bool follow)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;const long started=8980,issued=8990,tick=9000;
            var home=AiPositionDevelopmentTests.View().Buildings.Single(b=>b.Id==1);var goal=new NavPoint(-17,0);
            bool atHome=mode=="arrived"||mode=="unapplied"||mode=="stale-home",intermediate=mode=="intermediate"||mode=="in-flight"||mode=="returned";
            var leader=new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Tank,atHome?goal:intermediate?new NavPoint(-3,7):new NavPoint(15,8),94,mode=="moving",0,0,0,
                mode=="moving"?new PlayableTacticalOrderSnapshot(10,PlayableOwner.Player,107,8,issued,PlayableTacticalOrderKind.Move,goal,0):null,
                navigationOutcome:atHome||intermediate?NavigationOutcome.Arrived:mode=="moving"?NavigationOutcome.Moving:NavigationOutcome.Stopped,
                orderStamp:new PlayableOrderStamp(10,PlayableOwner.Player,107,1,8,issued,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,315,315));
            var other=new PlayableEntitySnapshot(11,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-17,4),70,false,0,0,0);
            bool appliedFollow=mode=="in-flight"||mode=="returned";
            var at=mode=="returned"?PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading):new NavPoint(-22,-8);var tower=new PlayableBuildingSnapshot(30,PlayableOwner.Player,PlayableBuildingKind.RepairTower,new NavPoint(-24,-8),TerritoryRules.Health(p,PlayableBuildingKind.RepairTower),1,0,0,default);
            var client=new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Tank,at,90,false,0,0,0,
                new PlayableTacticalOrderSnapshot(20,PlayableOwner.Player,107,appliedFollow?9:7,issued,appliedFollow?PlayableTacticalOrderKind.Follow:PlayableTacticalOrderKind.Move,at,appliedFollow?10:0),
                orderStamp:new PlayableOrderStamp(20,PlayableOwner.Player,107,1,appliedFollow?9:7,issued,PlayableOrderOrigin.Ai,appliedFollow?PlayableCommandKind.Follow:PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,appliedFollow?316:314,appliedFollow?316:314));
            var units=new[]{leader,other,client};var target=PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading);
            var proofs=new[]{new PlayableRouteProof(20,PlayableOwner.Player,107,tick,1,at,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,10,target,target,new[]{target}),
                new PlayableRouteProof(10,PlayableOwner.Player,107,mode=="stale-home"?tick-1:tick,1,leader.Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,home.Id,home.Position,goal,new[]{goal}),
                new PlayableRouteProof(11,PlayableOwner.Player,107,tick,1,other.Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,home.Id,home.Position,new NavPoint(-19,4),new[]{new NavPoint(-19,4)})};
            var o=AiPositionDevelopmentTests.View(extra:new[]{tower},units:units,proofs:proofs,tick:tick,activeProfile:p);
            var registry=new AiArmyRegistry(o.OwnerId,o.Generation,ai,AiDifficulty.Veteran);
            var before=AiPositionDevelopmentTests.View(units:units.Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,false,0,0,0)).ToArray(),tick:started,activeProfile:p);
            Assert.True(registry.TryCreate(before,AiArmyRole.Attack,AiArmyPlanner.Policy,units.Select(u=>u.Id),out long id,requiredMinimum:2));
            Assert.True(registry.BeginRepairReturn(id,AiArmyPlanner.Policy,20));
            var phase=mode=="regrouping"?AiArmyPhase.Regrouping:mode=="advancing"?AiArmyPhase.Advancing:AiArmyPhase.Retreating;
            registry.UpdateMission(id,AiArmyPlanner.Policy,phase,tick,((int)PlayableRouteTargetKind.PublicObjective)+":2",anchor:goal);
            var memory=new Dictionary<string,long>{{"repair-use:20:30:42:7",issued},{"repair-return:20",issued}};
            var records=new[]{new PlayableAiTraceRecord(o.Identity,314,issued,7,issued,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"repair",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,7),AiArmyPlanner.Policy,PlayableCommandKind.Move),
                new PlayableAiTraceRecord(o.Identity,315,issued,8,issued,mode=="unapplied"?PlayableAiDeliveryStatus.Accepted:PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"withdrawal",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,8),AiArmyPlanner.Policy,PlayableCommandKind.Move),
                new PlayableAiTraceRecord(o.Identity,316,issued,9,issued,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"return",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,9),AiArmyPlanner.Policy,PlayableCommandKind.Follow)};
            if(!appliedFollow)Assert.NotNull(AiRepairUsePolicy.ReturnAssembly(o,p,ai,registry.CaptureArmy(id).Single(),43,memory,AiArmyPlanner.Policy,records),"Pure return eligibility remains available during retreat");
            var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(p);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(AiArmyPlanner).GetField("state",flags).SetValue(planner,new AiArmyMissionState{ArmyId=id,Phase=phase,Reason=mode=="advancing"?"pressure":"withdrawal",StartedTick=started,PhaseTick=tick,ProgressTick=tick,TargetKind=PlayableRouteTargetKind.PublicObjective,TargetId=2,Target=new NavPoint(21,0)});
            typeof(AiArmyPlanner).GetField("completed",flags).SetValue(planner,memory);
            var action=planner.ProposeWithRecords(o,AiPositionDevelopmentTests.Opening(PlayableAiOpening.Safe),p,ai,registry,records);
            Assert.AreEqual(follow,action?.Kind==PlayableCommandKind.Follow,mode);
            if(follow){Assert.AreEqual(10,action.TargetId);CollectionAssert.AreEqual(new[]{20},action.EntityIds);}
            else if(mode=="regrouping"||mode=="retreating"){Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);CollectionAssert.AreEqual(new[]{10},action.EntityIds);}
            if(appliedFollow){Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);CollectionAssert.AreEqual(new[]{mode=="in-flight"?11:10},action.EntityIds);}
            if(intermediate)Assert.False(AiTacticalExecutor.EffectiveWithdrawal(o,registry.CaptureArmy(id).Single(),leader,records,p),"A settled rendezvous leg is not physical home arrival or recovery credit");
            Assert.AreEqual(phase,planner.Capture().Phase);Assert.AreEqual(tick,planner.Capture().ProgressTick);
            CollectionAssert.AreEqual(new[]{20},registry.CaptureArmy(id).Single().Reinforcements);Assert.AreEqual(issued,memory["repair-return:20"]);
        }
        [TestCase("valid",true)][TestCase("sequence",false)][TestCase("action",false)][TestCase("revision",false)]
        [TestCase("unapplied",false)][TestCase("human",false)][TestCase("foreign-policy",false)][TestCase("not-viable",false)]
        public void PendingWithdrawalRetainsExactQueueStampAndAppliedReceipt(string changed,bool expected)
        {
            var p=PlayableProfile.Default;const long tick=8990;var goal=new NavPoint(-20,0);
            var old=new PlayableTacticalOrderSnapshot(10,PlayableOwner.Player,107,6,tick-50,PlayableTacticalOrderKind.AttackMove,new NavPoint(18,24),0);
            var stamp=new PlayableOrderStamp(10,PlayableOwner.Player,107,24,7,tick,changed=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,314,314);
            var pending=new PlayableQueuedOrderSnapshot(1,1,changed=="sequence"?8:7,changed=="revision"?25:24,PlayableCommandKind.Move,new NavLocation(goal,NavLocation.FlatSurface),0,aiActionId:changed=="action"?315:314);
            var queue=new PlayableUnitQueueSnapshot(null,pending,Array.Empty<PlayableQueuedOrderSnapshot>(),null,pendingAiRouteViable:changed!="not-viable");
            var unit=new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-18,0),100,false,0,0,0,old,orderStamp:stamp,queue:queue);
            var o=AiPositionDevelopmentTests.View(units:new[]{unit});var record=new PlayableAiTraceRecord(o.Identity,314,tick,7,tick,changed=="unapplied"?PlayableAiDeliveryStatus.Accepted:PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"pending withdrawal",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,7),changed=="foreign-policy"?"army-flank":AiArmyPlanner.Policy,PlayableCommandKind.Move);
            Assert.AreEqual(expected,AiRepairUsePolicy.OwnedPendingWithdrawal(o,unit,AiArmyPlanner.Policy,new[]{record}));
        }
        [TestCase(true,11)][TestCase(false,10)]
        public void WithdrawalDoesNotReplaceItsCurrentViablePendingCoreMove(bool applied,int expected)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;const long issued=8990;
            var home=AiPositionDevelopmentTests.View().Buildings.First(b=>b.Kind==PlayableBuildingKind.Headquarters);var goal=new NavPoint(home.Position.X+TerritoryRules.Radius(p,home.Kind)+p.TankCollisionRadius+1,home.Position.Z);
            var old=new PlayableTacticalOrderSnapshot(10,PlayableOwner.Player,107,6,issued-50,PlayableTacticalOrderKind.AttackMove,new NavPoint(18,24),0);
            var stamp=new PlayableOrderStamp(10,PlayableOwner.Player,107,24,7,issued,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,314,314);
            var pending=new PlayableQueuedOrderSnapshot(1,1,7,24,PlayableCommandKind.Move,new NavLocation(goal,NavLocation.FlatSurface),0,aiActionId:314);
            var queue=new PlayableUnitQueueSnapshot(null,pending,Array.Empty<PlayableQueuedOrderSnapshot>(),null,pendingAiRouteViable:true);
            var first=new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-4,0),100,false,0,0,0,old,orderStamp:stamp,queue:queue);
            var second=new PlayableEntitySnapshot(11,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-3,0),100,false,0,0,0);
            var routes=new[]{first,second}.Select(u=>new PlayableRouteProof(u.Id,PlayableOwner.Player,107,9000,1,u.Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,home.Id,home.Position,goal,new[]{goal})).ToArray();
            var o=AiPositionDevelopmentTests.View(units:new[]{first,second},proofs:routes,tick:9000,activeProfile:p);
            var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Role=AiArmyRole.Attack,Phase=AiArmyPhase.Retreating,CreatedTick=8980,Members=new[]{10,11},Reinforcements=Array.Empty<int>(),LeaderId=10,MinimumViableForce=2,Anchor=goal};
            var receipt=new PlayableAiTraceRecord(o.Identity,314,issued,7,issued,applied?PlayableAiDeliveryStatus.Applied:PlayableAiDeliveryStatus.Accepted,PlayableCommandStatus.Applied,"withdrawal",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,7),AiArmyPlanner.Policy,PlayableCommandKind.Move);
            var action=AiTacticalExecutor.Withdrawal(o,army,p,ai,43,stalled:true,records:new[]{receipt});Assert.NotNull(action);CollectionAssert.AreEqual(new[]{expected},action.EntityIds,"Only an exact applied pending route keeps its place; remaining original core still receives withdrawal");
            Assert.AreSame(pending,first.Queue.Pending);Assert.AreEqual(AiArmyPhase.Retreating,army.Phase);
        }
        [TestCase("stored-minimum",true)]
        [TestCase("live",true)]
        [TestCase("expired",false)]
        [TestCase("dead-core",false)]
        [TestCase("held-core",false)]
        public void UnderstrengthOriginalCoreRegroupsOnlyWithinActualClientBudget(string changed,bool expected)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;const long started=8990;
            long tick=changed=="expired"?started+AiProfile.SecondsToTicks(ai.Value("positionDevelopment.repairWaitSeconds"),30):9000;
            int minimum=changed=="stored-minimum"?4:3;
            var initial=Enumerable.Range(10,minimum).Concat(new[]{20}).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-20,id-10),p.TankHealth,false,0,0,0)).ToArray();
            var before=AiPositionDevelopmentTests.View(units:initial,tick:started);
            var registry=new AiArmyRegistry(before.OwnerId,before.Generation,ai,AiDifficulty.Veteran);
            Assert.True(registry.TryCreate(before,AiArmyRole.Attack,AiArmyPlanner.Policy,initial.Select(u=>u.Id),out long id,requiredMinimum:minimum));
            var target=new NavPoint(21,0);
            Assert.True(registry.UpdateMission(id,AiArmyPlanner.Policy,AiArmyPhase.Advancing,started,((int)PlayableRouteTargetKind.PublicObjective)+":2",rally:target));
            Assert.True(registry.BeginRepairReturn(id,AiArmyPlanner.Policy,20));
            var units=initial.Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Id==20?30:changed=="dead-core"?0:changed=="stored-minimum"?90:u.Id==10?50:p.TankHealth,false,0,0,0,held:changed=="held-core"&&u.Id==10)).ToArray();
            var o=AiPositionDevelopmentTests.View(units:units,tick:tick);
            var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(p);
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(AiArmyPlanner).GetField("state",flags).SetValue(planner,new AiArmyMissionState{ArmyId=id,Phase=AiArmyPhase.Advancing,StartedTick=started,PhaseTick=started,ProgressTick=started,TargetKind=PlayableRouteTargetKind.PublicObjective,TargetId=2,Target=target});
            typeof(AiArmyPlanner).GetField("nextId",flags).SetValue(planner,43L);
            typeof(AiArmyPlanner).GetField("completed",flags).SetValue(planner,new Dictionary<string,long>{{"repair-use:20:30:42:7",started}});
            planner.Observe(o,registry,ai,replanQuietGuards:false);
            Assert.AreEqual(expected,planner.Active,changed);
            if(expected)
            {
                Assert.AreEqual(id,planner.Capture().ArmyId);Assert.AreEqual(AiArmyPhase.Regrouping,planner.Capture().Phase);
                Assert.AreEqual("withdrawal",planner.Capture().Reason);Assert.AreEqual(minimum,registry.CaptureArmy(id).Single().MinimumViableForce);
                Assert.True(planner.Capture().RepairClients.ContainsKey("repair-use:20:30:42:7"));
                using var stream=new System.IO.MemoryStream();using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))planner.WriteState(writer);
                stream.Position=0;var cold=new AiArmyPlanner(o.OwnerId,o.Generation);using(var reader=new System.IO.BinaryReader(stream,System.Text.Encoding.UTF8,true))cold.ReadState(reader,tick,registry);
                Assert.AreEqual(AiArmyPhase.Regrouping,cold.Capture().Phase);Assert.AreEqual(id,cold.Capture().ArmyId);
            }
        }
        [TestCase("returned",true)][TestCase("expired-unreturned",false)][TestCase("missing-core",false)]
        [TestCase("held-core",false)][TestCase("human-core",false)][TestCase("wrong-leader",false)]
        [TestCase("unsafe",false)][TestCase("stale-proof",false)][TestCase("unapplied",false)]
        [TestCase("human",true)][TestCase("held",false)][TestCase("wounded",false)]
        [TestCase("old-follow",false)][TestCase("pending-repair",false)][TestCase("not-withdrawing",true)]
        public void PhysicallyReturnedClientSustainsRetreatWithoutRenewingExpiredBudget(string changed,bool expected)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;const long started=8990;const int minimum=4;
            long tick=started+AiProfile.SecondsToTicks(ai.Value("positionDevelopment.repairWaitSeconds"),30)+1;
            var initial=Enumerable.Range(10,minimum).Concat(new[]{20}).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-20,id-10),p.TankHealth,false,0,0,0)).ToArray();
            var before=AiPositionDevelopmentTests.View(units:initial,tick:started,activeProfile:p);
            var registry=new AiArmyRegistry(before.OwnerId,before.Generation,ai,AiDifficulty.Veteran);
            Assert.True(registry.TryCreate(before,AiArmyRole.Attack,AiArmyPlanner.Policy,initial.Select(u=>u.Id),out long armyId,requiredMinimum:minimum));
            Assert.True(registry.BeginRepairReturn(armyId,AiArmyPlanner.Policy,20));
            var phase=changed=="not-withdrawing"?AiArmyPhase.Advancing:AiArmyPhase.Retreating;var home=new NavPoint(-25,0);
            Assert.True(registry.UpdateMission(armyId,AiArmyPlanner.Policy,phase,tick-1,((int)PlayableRouteTargetKind.FriendlyAnchor)+":1",anchor:home,rally:home));
            long issued=changed=="old-follow"?started-1:started+1;
            var order=new PlayableTacticalOrderSnapshot(20,PlayableOwner.Player,107,7,issued,PlayableTacticalOrderKind.Follow,default,changed=="wrong-leader"?11:10);
            var stamp=new PlayableOrderStamp(20,PlayableOwner.Player,107,1,7,issued,changed=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Follow,PlayableAiOpeningComposition.SourceIdentity,314,314);
            var client=new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Tank,changed=="expired-unreturned"?new NavPoint(-5,0):new NavPoint(-19,0),changed=="wounded"?30:p.TankHealth,false,0,0,0,order,held:changed=="held",orderStamp:stamp);
            var units=initial.Where(u=>u.Id!=20).Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,changed=="missing-core"&&u.Id==13?0:(int)(p.TankHealth*.9),false,0,0,0,
                held:changed=="held-core"&&u.Id==10,orderStamp:changed=="human-core"&&u.Id==10?new PlayableOrderStamp(u.Id,u.Owner,107,1,1,started,PlayableOrderOrigin.Human,PlayableCommandKind.Move,"human",1,1):null)).Concat(new[]{client}).ToArray();
            var leader=units.Single(u=>u.Id==order.TargetId);var goal=PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading);
            var proof=new PlayableRouteProof(20,PlayableOwner.Player,107,changed=="stale-proof"?tick-1:tick,1,client.Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,leader.Id,goal,goal,new[]{goal});
            var enemy=new PlayableBuildingSnapshot(99,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,new NavPoint(-23,4),TerritoryRules.Health(p,PlayableBuildingKind.SentryTurret),1,0,0,default);
            var o=AiPositionDevelopmentTests.View(units:units,proofs:new[]{proof},extra:changed=="unsafe"?new[]{enemy}:null,tick:tick,activeProfile:p);
            var record=new PlayableAiTraceRecord(o.Identity,314,issued,7,issued,changed=="unapplied"?PlayableAiDeliveryStatus.Accepted:PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"returned",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,7),AiArmyPlanner.Policy,PlayableCommandKind.Follow);
            var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(p);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(AiArmyPlanner).GetField("state",flags).SetValue(planner,new AiArmyMissionState{ArmyId=armyId,Phase=phase,Reason="withdrawal",StartedTick=started,PhaseTick=tick-1,ProgressTick=tick-1,TargetKind=PlayableRouteTargetKind.FriendlyAnchor,TargetId=1,Target=home});
            string key="repair-use:20:30:42"+(changed=="pending-repair"?"":":8");var memory=new Dictionary<string,long>{{key,started},{"repair-return:20",started}};
            typeof(AiArmyPlanner).GetField("completed",flags).SetValue(planner,memory);
            Assert.False(AiRepairUsePolicy.HasLiveRecoveryBudget(o,ai,memory),"The original budget has actually expired");
            planner.Observe(o,registry,ai,new[]{record},replanQuietGuards:false);
            Assert.AreEqual(expected,planner.Active,changed);
            if(changed=="human")
            {
                Assert.False(memory.ContainsKey(key),"Human takeover retires the AI client instead of crediting its return");
                Assert.Contains(20,registry.CaptureArmy(armyId).Single().Reinforcements,"No human order can prove AI joining");
                return;
            }
            if(changed=="not-withdrawing")
            {
                Assert.False(memory.ContainsKey(key),"Existing safe offensive return finishes normally; no retreat credit is needed");
                Assert.False(registry.CaptureArmy(armyId).Single().Reinforcements.Contains(20));
                return;
            }
            if(expected)
            {
                var state=planner.Capture();var army=registry.CaptureArmy(armyId).Single();
                Assert.AreEqual(AiArmyPhase.Retreating,state.Phase,"No home arrival or offensive resumption is credited");
                Assert.AreEqual(tick-1,state.PhaseTick);Assert.AreEqual(tick-1,state.ProgressTick);
                Assert.AreEqual(started,state.RepairClients[key]);Assert.AreEqual(started,memory["repair-return:20"]);
                Assert.AreEqual(minimum,army.MinimumViableForce);Assert.AreEqual(10,army.LeaderId);
                CollectionAssert.AreEquivalent(new[]{10,11,12,13},army.Members.Except(army.Reinforcements));Assert.Contains(20,army.Reinforcements,"No early joining");
                Assert.False(AiRepairUsePolicy.HasLiveRecoveryBudget(o,ai,memory),"Physical availability never resets either clock");
                var withoutProof=AiPositionDevelopmentTests.View(units:units,tick:tick+1,activeProfile:p);
                planner.Observe(withoutProof,registry,ai,new[]{record},replanQuietGuards:false);
                Assert.False(planner.Active,"The physical safety proof must remain current, never cached");
            }
        }
        [TestCase("valid",true)][TestCase("old-own",false)][TestCase("old-defense",true)][TestCase("new-defense",false)]
        [TestCase("human",false)][TestCase("foreign-policy",false)][TestCase("sequence",false)][TestCase("unapplied",false)][TestCase("held",false)]
        public void RetainedRepairCoreOrderRequiresExactAuthority(string changed,bool expected)
        {
            const long tick=8990;var goal=new NavPoint(-20,0);
            var order=new PlayableTacticalOrderSnapshot(10,PlayableOwner.Player,107,7,tick,PlayableTacticalOrderKind.Move,goal,0);
            var stamp=new PlayableOrderStamp(10,PlayableOwner.Player,107,1,changed=="sequence"?8:7,tick,changed=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,314,314);
            var unit=new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-18,0),100,false,0,0,0,order,held:changed=="held",orderStamp:stamp);
            var o=AiPositionDevelopmentTests.View(units:new[]{unit});
            var record=new PlayableAiTraceRecord(o.Identity,314,tick,7,tick,changed=="unapplied"?PlayableAiDeliveryStatus.Accepted:PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"core",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,7),changed=="foreign-policy"?"foreign":changed.EndsWith("defense")?AiDefensePlanner.Policy:AiArmyPlanner.Policy,PlayableCommandKind.Move);
            Assert.AreEqual(expected,AiRepairUsePolicy.OwnedCoreOrder(o,unit,AiArmyPlanner.Policy,changed=="old-own"||changed=="old-defense"?tick+1:tick-1,new[]{record},allowPriorDefense:true));
        }
        [TestCase("pending",false)][TestCase("old-follow",false)][TestCase("returned-follow",true)][TestCase("retreating",false)][TestCase("wrong-core",false)]
        public void RepairClientCannotJoinThroughAnOlderAssemblyFollow(string changed,bool expected)
        {
            var p=PlayableProfile.Default;var ai=AiProfile.Initial;long issued=changed=="returned-follow"||changed=="retreating"||changed=="wrong-core"?9000:8980;
            var core=new PlayableEntitySnapshot(10,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-20,0),p.TankHealth,false,0,0,0);
            var order=new PlayableTacticalOrderSnapshot(20,PlayableOwner.Player,107,7,issued,PlayableTacticalOrderKind.Follow,default,changed=="wrong-core"?11:10);
            var stamp=new PlayableOrderStamp(20,PlayableOwner.Player,107,1,7,issued,PlayableOrderOrigin.Ai,PlayableCommandKind.Follow,PlayableAiOpeningComposition.SourceIdentity,314,314);
            var client=new PlayableEntitySnapshot(20,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-19,0),p.TankHealth,false,0,0,0,order,orderStamp:stamp);
            var target=changed=="wrong-core"?new NavPoint(-20,2):core.Position;var followGoal=PlayableUnitRules.FollowGoal(p,target,0);
            var proof=new PlayableRouteProof(20,PlayableOwner.Player,107,9001,1,client.Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,changed=="wrong-core"?11:10,followGoal,followGoal,new[]{followGoal});
            var secondCore=new PlayableEntitySnapshot(11,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-20,2),p.TankHealth,false,0,0,0);
            var o=AiPositionDevelopmentTests.View(units:new[]{core,secondCore,client},proofs:new[]{proof},tick:9001,activeProfile:p);
            var registry=new AiArmyRegistry(o.OwnerId,o.Generation,ai,AiDifficulty.Veteran);
            var before=AiPositionDevelopmentTests.View(units:new[]{core,secondCore,client},tick:8980,activeProfile:p);
            Assert.True(registry.TryCreate(before,AiArmyRole.MobileDefense,AiArmyPlanner.Policy,new[]{10,11,20},out long id),"create actual army before its mission");
            Assert.True(registry.BeginRepairReturn(id,AiArmyPlanner.Policy,20),"detach nonleader repair client");
            var phase=changed=="retreating"?AiArmyPhase.Retreating:AiArmyPhase.Staging;
            Assert.True(registry.UpdateMission(id,AiArmyPlanner.Policy,phase,8990,((int)PlayableRouteTargetKind.FriendlyAnchor)+":1",rally:core.Position));
            var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(p);const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(AiArmyPlanner).GetField("state",f).SetValue(planner,new AiArmyMissionState{ArmyId=id,Phase=phase,Reason=changed=="retreating"?"withdrawal":"",StartedTick=8990,ProgressTick=8990,PhaseTick=8990,TargetKind=PlayableRouteTargetKind.FriendlyAnchor,TargetId=1,Target=core.Position});
            string key="repair-use:20:30:42"+(changed=="pending"?"":":8");typeof(AiArmyPlanner).GetField("completed",f).SetValue(planner,new Dictionary<string,long>{{key,8990}});
            var record=new PlayableAiTraceRecord(o.Identity,314,issued,7,issued,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"follow",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(107,o.OwnerId,1,7),AiArmyPlanner.Policy,PlayableCommandKind.Follow);
            Assert.True(AiArmyPlanner.AssemblyFollow(o,registry.CaptureArmy(id).Single(),client,new[]{record}),"old and new Follow both need genuine authority; only time/client binding differs");
            planner.Observe(o,registry,ai,new[]{record},replanQuietGuards:false);
            Assert.AreEqual(expected,!registry.CaptureArmy(id).Single().Reinforcements.Contains(20));
            Assert.AreEqual(!expected,planner.Capture().RepairClients.ContainsKey(key));
        }
    }
}

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiArmyTests
    {
        private static PlayableAiObservation SafeWave(string change="healthy",long tick=100,bool arrived=false)
        {
            var p=PlayableProfile.Default;var original=A4Observation(tick,wave:true);var home=new NavPoint(-25,12);
            var entities=original.Entities.Where(u=>u.Owner==PlayableOwner.Player&&(change!="lost"||u.Id==4)).Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,
                u.Id==4?(arrived?home:new NavPoint(-16,0)):new NavPoint(change=="dispersed"?u.Id*40:0,u.Id*2),change=="wounded"&&u.Id!=4?1:u.Health,u.Id!=4,0,0,0)).ToList();
            if(change=="single")entities=entities.Where(u=>u.Id==1||u.Id==4).ToList();
            if(change=="overwhelmed"||change=="intercepted")entities.AddRange(Enumerable.Range(100,change=="overwhelmed"?10:1).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(change=="overwhelmed"?0:-4,change=="overwhelmed"?3:0),p.TankHealth,false,0,0,0)));
            var wave=entities.Single(u=>u.Id==4);
            var core=entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Id!=4).ToArray();
            var routes=core.SelectMany(c=>new[]{c.Position,PlayableUnitRules.FollowGoal(p,c.Position,c.HullHeading)}.Select(target=>new PlayableRouteProof(4,PlayableOwner.Player,71,tick,1,wave.Position,PlayableUnitRules.Radius(p,wave.Kind),PlayableRouteTargetKind.FriendlyAnchor,c.Id,target,target,new[]{target}))).ToList();
            foreach(var u in entities.Where(u=>u.Owner==PlayableOwner.Player))routes.Add(new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,PlayableUnitRules.Radius(p,u.Kind),PlayableRouteTargetKind.FriendlyAnchor,50,home,home,new[]{home}));
            var vision=new PlayableVision(0,200,200,1,1);vision.Refresh(new[]{new VisionSource(default,200)},Array.Empty<KnownBuilding>());
            var buildings=new[]{new PlayableBuildingSnapshot(50,PlayableOwner.Player,PlayableBuildingKind.Headquarters,home,p.HeadquartersHealth,1,0,0,default)};
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,original.Seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities.ToArray(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:routes.ToArray(),activeProfile:p));
        }
        [TestCase("healthy",true)][TestCase("single",false)][TestCase("dispersed",false)][TestCase("wounded",false)][TestCase("overwhelmed",false)]
        public void SafeReinforcement_ReceiverRequiresLiveCompactHealthyCompatibleCore(string change,bool viable)
        {
            var o=SafeWave(change);var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Members=new[]{1,2,3,4},Reinforcements=new[]{4},MinimumViableForce=3,Phase=AiArmyPhase.Engaging};
            Assert.AreEqual(viable,AiReinforcementPolicy.ReceivingCore(o,army,PlayableProfile.Default,AiProfile.Initial).Length>0);
            army.Phase=AiArmyPhase.Retreating;Assert.IsEmpty(AiReinforcementPolicy.ReceivingCore(o,army,PlayableProfile.Default,AiProfile.Initial));
        }
        [Test] public void SafeReinforcement_HealthyCoreGetsOneUnitFollowImmediatelyAndTargetTracksIt()
        {
            var o=SafeWave();var registry=Registry();Assert.True(registry.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out long id));registry.UpdateMission(id,AiArmyPlanner.Policy,AiArmyPhase.Advancing,o.Tick,"front");
            AiTacticalExecutor.Observe(o,registry,id,PlayableProfile.Default);Assert.AreEqual(id,registry.ArmyFor(4));
            var army=registry.CaptureArmy(id).Single();var action=AiTacticalExecutor.Propose(o,army,PlayableProfile.Default,1);
            TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{army,core=AiReinforcementPolicy.ReceivingCore(o,army,PlayableProfile.Default,AiProfile.Initial).Select(u=>u.Id),PlayableProfile.Default.FollowDistance,routes=o.RouteProofs.Select(r=>new{r.UnitId,r.TargetId,r.Target,r.Goal,safe=AiTacticalExecutor.SafeRoute(o,r,PlayableProfile.Default)})}));
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Follow,action.Kind);CollectionAssert.AreEqual(new[]{4},action.EntityIds);Assert.Contains(action.TargetId,new[]{1,2,3});
            Assert.AreEqual(0,action.SiteId);Assert.False(action.TargetId==101);
            Assert.IsEmpty(AiTacticalExecutor.Ready(o,army,PlayableProfile.Default));
        }
        [Test] public void SafeReinforcement_InterceptedJoinUsesSafeHomeInsteadOfEnemyOrUnsafeLeader()
        {
            var o=SafeWave("intercepted");var registry=Registry();Assert.True(registry.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out long id));Assert.True(registry.TryReinforce(o,id,AiArmyPlanner.Policy,new[]{4}));
            var action=AiTacticalExecutor.Propose(o,registry.CaptureArmy(id).Single(),PlayableProfile.Default,1);
            TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{army=registry.CaptureArmy(id).Single(),core=AiReinforcementPolicy.ReceivingCore(o,registry.CaptureArmy(id).Single(),PlayableProfile.Default,AiProfile.Initial).Select(u=>u.Id),routes=o.RouteProofs.Select(r=>new{r.UnitId,r.TargetId,r.Target,r.Goal,safe=AiTacticalExecutor.SafeRoute(o,r,PlayableProfile.Default)})}));
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);Assert.AreEqual(new NavPoint(-25,12),action.Target);CollectionAssert.AreEqual(new[]{4},action.EntityIds);
        }
        [Test] public void SafeReinforcement_CoreLossRetainsClientForSafeRegroupAndExactPlannerRestore()
        {
            var registry=Registry();var planner=A4Mission(registry);long id=planner.Capture().ArmyId;var wave=SafeWave();AiTacticalExecutor.Observe(wave,registry,id,PlayableProfile.Default);
            var lost=SafeWave("lost");registry.Observe(lost);planner.Observe(lost,registry,AiProfile.Initial,replanQuietGuards:false);
            Assert.True(planner.Active);Assert.AreEqual(AiArmyPhase.Regrouping,planner.Capture().Phase);Assert.AreEqual(AiReinforcementPolicy.RegroupReason,planner.Capture().Reason);Assert.AreEqual(id,registry.ArmyFor(4));
            var action=planner.ProposeWithRecords(lost,A4Opening(),PlayableProfile.Default,AiProfile.Initial,registry,Array.Empty<PlayableAiTraceRecord>());
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);Assert.AreEqual(new NavPoint(-25,12),action.Target);
            Assert.AreEqual(1,planner.ReinforcementPriority(lost,registry,action),"Stopped client home admission precedes ordinary front refresh, below survival");
            byte[] Capture(AiArmyPlanner value){using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))typeof(AiArmyPlanner).GetMethod("WriteState",F).Invoke(value,new object[]{writer});return stream.ToArray();}}
            var bytes=Capture(planner);var restored=new AiArmyPlanner(lost.OwnerId,lost.Generation);
            using(var reader=new BinaryReader(new MemoryStream(bytes)))typeof(AiArmyPlanner).GetMethod("ReadState",F).Invoke(restored,new object[]{reader,lost.Tick,registry});
            CollectionAssert.AreEqual(bytes,Capture(restored));Assert.AreEqual(action.Target,restored.ProposeWithRecords(lost,A4Opening(),PlayableProfile.Default,AiProfile.Initial,registry,Array.Empty<PlayableAiTraceRecord>()).Target);
        }
        [Test] public void SafeReinforcement_RegroupWaitingClientPrecedesOlderHomeRefresh()
        {
            var p=PlayableProfile.Default;var before=SafeWave("lost");var old=before.Entities.Single();var home=before.Buildings.Single();
            var order=new PlayableTacticalOrderSnapshot(4,old.Owner,71,7,90,PlayableTacticalOrderKind.Move,new NavPoint(-24,12),0);
            var stamp=new PlayableOrderStamp(4,old.Owner,71,1,7,90,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,44,44);
            var moving=new PlayableEntitySnapshot(4,old.Owner,old.Kind,old.Position,old.Health,true,0,0,0,order,orderStamp:stamp);
            var waiting=new PlayableEntitySnapshot(5,old.Owner,old.Kind,new NavPoint(-16,4),old.Health,false,0,0,0);
            var routes=before.RouteProofs.Concat(new[]{new PlayableRouteProof(5,waiting.Owner,71,before.Tick,1,waiting.Position,PlayableUnitRules.Radius(p,waiting.Kind),PlayableRouteTargetKind.FriendlyAnchor,home.Id,home.Position,home.Position,new[]{home.Position})}).ToArray();
            var o=PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,before.Seed,before.Tick,before.Tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,new[]{moving,waiting},before.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:before.Vision,routeProofs:routes,activeProfile:p));
            var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Members=new[]{4,5},Reinforcements=new[]{4,5},MinimumViableForce=3,Phase=AiArmyPhase.Regrouping,CreatedTick=10};
            var action=AiReinforcementPolicy.Gather(o,army,p,99,Array.Empty<PlayableAiTraceRecord>(),regroupTick:80);
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);CollectionAssert.AreEqual(new[]{5},action.EntityIds);
        }
        [Test] public void SafeReinforcement_JoinedClientEscapePrecedesTrappedCoreLastResortAttack()
        {
            var p=PlayableProfile.Default;var o=SafeWave("intercepted");
            var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Members=new[]{1,4},Reinforcements=Array.Empty<int>(),MinimumViableForce=3,Phase=AiArmyPhase.Retreating};
            Assert.False(o.RouteProofs.Any(r=>r.UnitId==1&&AiTacticalExecutor.WithdrawalRoute(o,r,p)),"Exposed first core has no certified escape");
            Assert.True(o.RouteProofs.Any(r=>r.UnitId==4&&AiTacticalExecutor.WithdrawalRoute(o,r,p)),"Joined client has a certified escape");
            var action=AiTacticalExecutor.Withdrawal(o,army,p,AiProfile.Initial,99);
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);CollectionAssert.AreEqual(new[]{4},action.EntityIds);
        }
        [TestCase(false,false,false)][TestCase(true,false,false)][TestCase(true,true,false)][TestCase(true,false,true)] public void SafeReinforcement_OnlyTerminalAppliedFollowReassessesAnotherCertifiedCore(bool terminal,bool occupied,bool fresh)
        {
            var p=PlayableProfile.Default;var before=SafeWave(tick:180);var unit=before.Entities.Single(u=>u.Id==4);
            var order=new PlayableTacticalOrderSnapshot(4,unit.Owner,71,7,90,PlayableTacticalOrderKind.Follow,default,1);
            var stamp=new PlayableOrderStamp(4,unit.Owner,71,1,7,90,PlayableOrderOrigin.Ai,PlayableCommandKind.Follow,PlayableAiOpeningComposition.SourceIdentity,44,44);
            var follower=new PlayableEntitySnapshot(4,unit.Owner,unit.Kind,unit.Position,unit.Health,!terminal,0,0,0,order,navigationOutcome:terminal?NavigationOutcome.Stopped:NavigationOutcome.Moving,orderStamp:stamp);
            var o=PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,before.Seed,before.Tick,before.Tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,before.Entities.Where(u=>u.Id!=4).Concat(new[]{follower}).Concat(occupied?new[]{new PlayableEntitySnapshot(5,unit.Owner,unit.Kind,PlayableUnitRules.FollowGoal(p,before.Entities.Single(u=>u.Id==2).Position,0),100,false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>()).Concat(fresh?new[]{new PlayableEntitySnapshot(6,unit.Owner,unit.Kind,new NavPoint(-16,8),100,false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>()).ToArray(),before.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:before.Vision,routeProofs:before.RouteProofs.Concat(fresh?before.RouteProofs.Where(r=>r.UnitId==4).Select(r=>new PlayableRouteProof(6,r.Owner,r.Generation,r.Tick,r.GeometryRevision,new NavPoint(-16,8),r.Radius,r.Kind,r.TargetId,r.Target,r.Goal,r.Path.ToArray())):Array.Empty<PlayableRouteProof>()).ToArray(),activeProfile:p));
            var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Members=fresh?new[]{1,2,3,4,6}:new[]{1,2,3,4},Reinforcements=fresh?new[]{4,6}:new[]{4},MinimumViableForce=3,Phase=AiArmyPhase.Advancing,CreatedTick=10};
            var receipt=new PlayableAiTraceRecord(SafeWave(tick:90).Identity,44,90,7,90,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"owned joining Follow",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,1,1),AiArmyPlanner.Policy,PlayableCommandKind.Follow);
            var action=AiTacticalExecutor.Propose(o,army,p,99,records:new[]{receipt});
            if(!terminal)Assert.Null(action,"A valid moving Follow keeps its certified leader");
            else{Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Follow,action.Kind);Assert.AreNotEqual(1,action.TargetId);if(occupied)Assert.AreEqual(3,action.TargetId,"A nearer occupied Follow endpoint cannot trap the client");CollectionAssert.AreEqual(new[]{4},action.EntityIds);}
        }
        [Test] public void SafeReinforcement_ProfileFieldsHaveBoundsAndRoundTrip()
        {
            var fields=NativeBalanceFields.AiOpeningFields.Where(f=>f.Path.StartsWith("safeReinforcements.")).ToArray();Assert.AreEqual(3,fields.Length);
            foreach(var field in fields){Assert.IsNotEmpty(field.Label);Assert.IsNotEmpty(field.Description);Assert.IsNotEmpty(field.Unit);Assert.Greater(field.Step,0);Assert.Throws<ArgumentOutOfRangeException>(()=>field.Validate(field.Maximum+field.Step));var data=AiProfile.Initial.CopyData();NativeBalanceFields.Write(field,data,field.Minimum);Assert.AreEqual(field.Minimum,new AiProfile(data).Value(field.Path));}
        }
        [Test] public void SafeReinforcement_IdleOutputPrecedesRefreshOfAnOlderMovingMember()
        {
            var p=PlayableProfile.Default;var before=SafeWave();var old=before.Entities.Single(u=>u.Id==4);
            var order=new PlayableTacticalOrderSnapshot(4,before.Owner,71,5,90,PlayableTacticalOrderKind.Move,new NavPoint(0,2),0);
            var fresh=new PlayableEntitySnapshot(5,before.Owner,PlayableEntityKind.Tank,new NavPoint(-16,8),100,false,0,0,0);
            var units=before.Entities.Where(u=>u.Id!=4).Concat(new[]{new PlayableEntitySnapshot(4,old.Owner,old.Kind,old.Position,old.Health,true,0,0,0,order),fresh}).ToArray();
            var leader=units.Single(u=>u.Id==1);var target=PlayableUnitRules.FollowGoal(p,leader.Position,leader.HullHeading);
            var routes=before.RouteProofs.Concat(new[]{new PlayableRouteProof(5,fresh.Owner,71,before.Tick,1,fresh.Position,PlayableUnitRules.Radius(p,fresh.Kind),PlayableRouteTargetKind.FriendlyAnchor,leader.Id,target,target,new[]{target})}).ToArray();
            var o=PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,before.Seed,before.Tick,before.Tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,before.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:before.Vision,routeProofs:routes,activeProfile:p));
            var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Members=new[]{1,2,3,4,5},Reinforcements=new[]{4,5},MinimumViableForce=3,Phase=AiArmyPhase.Advancing};
            var action=AiTacticalExecutor.Propose(o,army,p,1);Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Follow,action.Kind);CollectionAssert.AreEqual(new[]{5},action.EntityIds);
        }
        [TestCase(false)][TestCase(true)] public void SafeReinforcement_LostCoreStopsAppliedJoiningBeforeSerialEscapeAndPreservesHuman(bool human)
        {
            var p=PlayableProfile.Default;var before=SafeWave("lost");var unit=before.Entities.Single();
            var order=new PlayableTacticalOrderSnapshot(4,unit.Owner,71,7,90,PlayableTacticalOrderKind.Follow,default,1);
            var stamp=new PlayableOrderStamp(4,unit.Owner,71,1,7,90,human?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Follow,human?null:PlayableAiOpeningComposition.SourceIdentity,human?0:44,human?0:44);
            var follower=new PlayableEntitySnapshot(4,unit.Owner,unit.Kind,unit.Position,unit.Health,true,0,0,0,order,orderStamp:stamp);
            var o=PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,before.Seed,before.Tick,before.Tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,new[]{follower},before.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:before.Vision,routeProofs:before.RouteProofs.ToArray(),activeProfile:p));
            var army=new AiArmyState{Id=1,OwnerId=o.OwnerId,TacticalOwner=AiArmyPlanner.Policy,Members=new[]{4},Reinforcements=new[]{4},MinimumViableForce=3,Phase=AiArmyPhase.Regrouping,CreatedTick=10};
            var receipt=new PlayableAiTraceRecord(SafeWave(tick:90).Identity,44,90,7,90,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"owned original joining order",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,1,1),AiArmyPlanner.Policy,PlayableCommandKind.Follow);
            var action=AiReinforcementPolicy.Gather(o,army,p,99,new[]{receipt},regroupTick:100);
            if(human)Assert.Null(action);else{Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Stop,action.Kind);CollectionAssert.AreEqual(new[]{4},action.EntityIds);}
        }
        [TestCase(false)][TestCase(true)] public void SafeReinforcement_SafeRebuildReleasesOnlyAPhysicallyAssembledSufficientGroup(bool sufficient)
        {
            var p=PlayableProfile.Default;var registry=Registry();var planner=A4Mission(registry);long id=planner.Capture().ArmyId;var before=SafeWave();
            var ids=sufficient?new[]{4,5,6}:new[]{4,5};var home=before.Buildings.Single();
            PlayableAiObservation View(bool arrived)
            {
                var spots=new[]{new NavPoint(-18,12),new NavPoint(-20,18),new NavPoint(-28,19)};
                var units=ids.Select((unit,index)=>new PlayableEntitySnapshot(unit,before.Owner,PlayableEntityKind.Tank,arrived?spots[index]:new NavPoint(-16,index*2),100,false,0,0,0,
                    navigationOutcome:arrived?NavigationOutcome.Arrived:NavigationOutcome.Idle,orderStamp:arrived?new PlayableOrderStamp(unit,before.Owner,71,1,unit*10,110,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,PlayableAiOpeningComposition.SourceIdentity,100+unit,100+unit):null)).ToArray();
                var routes=units.Select(u=>new PlayableRouteProof(u.Id,u.Owner,71,arrived?120:100,1,u.Position,PlayableUnitRules.Radius(p,u.Kind),PlayableRouteTargetKind.FriendlyAnchor,home.Id,home.Position,arrived?u.Position:spots[Array.IndexOf(ids,u.Id)],new[]{arrived?u.Position:spots[Array.IndexOf(ids,u.Id)]})).ToArray();
                return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,before.Seed,arrived?120:100,arrived?120:100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,new[]{home},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:before.Vision,routeProofs:routes,activeProfile:p));
            }
            var waiting=View(false);Assert.True(registry.TryReinforce(waiting,id,AiArmyPlanner.Policy,ids));registry.Observe(waiting);planner.Observe(waiting,registry,AiProfile.Initial,replanQuietGuards:false);Assert.AreEqual(AiArmyPhase.Regrouping,planner.Capture().Phase);
            var arrived=View(true);var receipts=ids.Select(unit=>new PlayableAiTraceRecord(SafeWave(tick:110).Identity,100+unit,110,unit*10,110,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"actual owned home arrival",arrived.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,arrived.OwnerId,1,unit),AiArmyPlanner.Policy,PlayableCommandKind.Move)).ToArray();
            planner.Observe(arrived,registry,AiProfile.Initial,receipts,replanQuietGuards:false);Assert.AreEqual(!sufficient,planner.Active);
            if(sufficient){Assert.True(ids.All(unit=>registry.ArmyFor(unit)==0));Assert.True(registry.TryCreate(arrived,AiArmyRole.Attack,AiArmyPlanner.Policy,ids,out long replacement));Assert.Greater(replacement,id);}
            else{Assert.AreEqual(AiArmyPhase.Regrouping,planner.Capture().Phase);Assert.Null(planner.ProposeWithRecords(arrived,A4Opening(),p,AiProfile.Initial,registry,receipts));}
        }
    }
}

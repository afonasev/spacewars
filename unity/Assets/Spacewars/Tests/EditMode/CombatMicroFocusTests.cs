using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroFocusTests
    {
        const long Gen=901;
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        static PlayableAiObservation View(PlayableEntitySnapshot[] own,PlayableEntitySnapshot[] enemy,long tick=0,PlayableBuildingSnapshot[] buildings=null)
        {
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),50)},Array.Empty<KnownBuilding>());
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,own.Concat(enemy).ToArray(),buildings??Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot()));
        }
        static PlayableEntitySnapshot[] Own(int count=10)=>Enumerable.Range(1,count).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-(id%2)*2,(id/2)*1.5-3),100,false,0,0,0,maxHealth:100)).ToArray();
        static PlayableEntitySnapshot[] Enemy(int health=100)=>Enumerable.Range(101,10).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(7+(id%2),((id-101)/2)*1.5-3),id==101?health:100,false,0,0,Math.PI,maxHealth:100)).ToArray();
        static AiArmyRegistry Army(PlayableAiObservation o,out long id)
        {var r=new AiArmyRegistry("player-1",Gen,Ai,AiDifficulty.Veteran);Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,o.Entities.Where(u=>u.Owner==o.Owner).Select(u=>u.Id),out id));return r;}
        [Test]
        public void TenOnTenUsesBoundedSubsetsAndExistingAssignmentsPreventOverkill()
        {
            var own=Own();var enemies=Enemy();var o=View(own,enemies);var armies=Army(o,out _);var micro=new AiCombatMicroPolicy();micro.Observe(o,P,Ai,AiDifficulty.Veteran,armies);
            var first=micro.Plan(o,P,Ai,AiDifficulty.Veteran,armies);Assert.NotNull(first);Assert.AreEqual(PlayableCommandKind.Attack,first.Action.Kind);Assert.AreEqual(4,first.Action.EntityIds.Count);
            micro.Commit(first,Gen,0);var actual=new PlayableAiAction(77,"player-1",P.ProfileId,P.Revision,Gen,0,first.Action.Kind,first.Action.CopyEntityIds(),first.Action.Target,targetId:first.Action.TargetId,seed:7,sourceIdentity:first.Action.SourceIdentity);
            micro.Applied(actual,12,10,new AiReceiptIdentity(Gen,"player-1",1,1));micro.Receipt(new PlayableAiTraceRecord(o.Identity,first.Action.ActionId,10,12,10,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"control","player-1",actual.SourceIdentity,new AiReceiptIdentity(Gen,"player-1",1,1),AiCombatMicroPolicy.Policy,actual.Kind));
            var assigned=own.Select(u=>!actual.EntityIds.Contains(u.Id)?u:new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,100,false,actual.TargetId,0,0,new PlayableTacticalOrderSnapshot(u.Id,u.Owner,Gen,12,10,PlayableTacticalOrderKind.Attack,actual.Target,actual.TargetId),orderStamp:new PlayableOrderStamp(u.Id,u.Owner,Gen,1,12,10,PlayableOrderOrigin.Ai,PlayableCommandKind.Attack,actual.SourceIdentity,77,77),maxHealth:100)).ToArray();
            var next=View(assigned,enemies,30);micro.Observe(next,P,Ai,AiDifficulty.Veteran,armies);var second=micro.Plan(next,P,Ai,AiDifficulty.Veteran,armies);
            Assert.NotNull(second);Assert.AreNotEqual(first.Action.TargetId,second.Action.TargetId);Assert.IsEmpty(first.Action.EntityIds.Intersect(second.Action.EntityIds));Assert.LessOrEqual(second.Action.EntityIds.Count,4);
            var far=enemies.Where(e=>e.Id==actual.TargetId).Select(e=>new PlayableEntitySnapshot(e.Id,e.Owner,e.Kind,new NavPoint(25,0),100,true,0,0,0,maxHealth:100)).ToArray();
            var alternatives=View(assigned,far.Concat(enemies.Where(e=>e.Id!=actual.TargetId)).ToArray(),60);
            micro.Observe(alternatives,P,Ai,AiDifficulty.Veteran,armies);var retarget=micro.Plan(alternatives,P,Ai,AiDifficulty.Veteran,armies);
            Assert.NotNull(retarget);Assert.AreEqual(PlayableCommandKind.Attack,retarget.Action.Kind);Assert.AreNotEqual(actual.TargetId,retarget.Action.TargetId);Assert.LessOrEqual(retarget.Action.EntityIds.Count,4);
            var retreat=View(assigned,far,60);micro.Observe(retreat,P,Ai,AiDifficulty.Veteran,armies);var stop=micro.Plan(retreat,P,Ai,AiDifficulty.Veteran,armies);
            Assert.NotNull(stop);Assert.AreEqual(PlayableCommandKind.Stop,stop.Action.Kind);CollectionAssert.AreEquivalent(actual.CopyEntityIds(),stop.Action.CopyEntityIds(),"One common pursuit is stopped by one group action");
        }
        [Test]
        public void FocusDeliveryRechecksNearbyAirSupportAndOtherAssignmentsDuringReaction()
        {
            var own=Own(6).Select(u=>u.Id==1?new PlayableEntitySnapshot(u.Id,u.Owner,PlayableEntityKind.Hawk,u.Position,100,false,0,0,0,maxHealth:100,altitude:6):u).ToArray();
            var enemies=Enemy(8);var o=View(own,enemies);var registry=Army(o,out _);var micro=new AiCombatMicroPolicy();micro.Observe(o,P,Ai,AiDifficulty.Veteran,registry);var plan=micro.Plan(o,P,Ai,AiDifficulty.Veteran,registry);
            Assert.NotNull(plan);Assert.AreEqual(AiCombatMicroPhase.Focus,plan.Phase);CollectionAssert.AreEqual(new[]{1},plan.Action.CopyEntityIds());micro.Commit(plan,Gen,0);
            var intent=new AiIntent("focus-reaction-control",AiCombatMicroPolicy.Policy,plan.Action,100,0,0,Array.Empty<string>());Assert.True(micro.Authorizes(intent,o,P,Ai,AiDifficulty.Veteran,registry));
            var separated=own.Select(u=>u.Id==1?u:new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,new NavPoint(40,40),100,false,0,0,0,maxHealth:100)).ToArray();
            Assert.False(micro.Authorizes(intent,View(separated,enemies,15),P,Ai,AiDifficulty.Veteran,registry),"A still-visible reachable target does not replace current nearby friendly support.");
            var assigned=own.Select(u=>u.Id!=2?u:new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,100,false,101,0,0,new PlayableTacticalOrderSnapshot(u.Id,u.Owner,Gen,12,10,PlayableTacticalOrderKind.Attack,enemies[0].Position,101),maxHealth:100)).ToArray();
            Assert.False(micro.Authorizes(intent,View(assigned,enemies,15),P,Ai,AiDifficulty.Veteran,registry),"New ordinary fire assignments consume the same target's current overkill allowance.");
        }
        [Test]
        public void OneLowHealthTargetDoesNotReceiveTheWholeFocusGroup()
        {
            var o=View(Own(),Enemy(8));var armies=Army(o,out _);var micro=new AiCombatMicroPolicy();micro.Observe(o,P,Ai,AiDifficulty.Veteran,armies);
            var plan=micro.Plan(o,P,Ai,AiDifficulty.Veteran,armies);Assert.NotNull(plan);Assert.AreEqual(101,plan.Action.TargetId);Assert.AreEqual(1,plan.Action.EntityIds.Count);
        }
        [Test]
        public void ACommonRetreatWithDifferentLocalAlternativesProducesOneGroupStopRatherThanTenStops()
        {
            var own=Enumerable.Range(1,10).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10,id<=5?-8:8),100,false,101,0,0,new PlayableTacticalOrderSnapshot(id,PlayableOwner.Player,Gen,1,0,PlayableTacticalOrderKind.Attack,new NavPoint(8,0),101),orderStamp:new PlayableOrderStamp(id,PlayableOwner.Player,Gen,1,1,0,PlayableOrderOrigin.Ai,PlayableCommandKind.Attack,PlayableAiOpeningComposition.SourceIdentity,1,1),maxHealth:100)).ToArray();
            var enemies=new[]{new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(25,0),100,true,0,0,0),new PlayableEntitySnapshot(102,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-7,-8),100,false,0,0,0),new PlayableEntitySnapshot(103,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-7,8),100,false,0,0,0)};
            var o=View(own,enemies);
            Assert.True(AiCombatMicroSafety.FireClear(o,P,own[0],enemies[1]),"Local alternative must have a clear real shot, outside the authored wall");
            Assert.True(AiCombatMicroSafety.FireClear(o,P,own[9],enemies[2]));
            Assert.Greater(Math.Abs(own[0].Position.Z-enemies[2].Position.Z),P.TankRange,"The two fronts must not share a reachable alternative");
            var armies=Army(o,out _);var micro=new AiCombatMicroPolicy();micro.Observe(o,P,Ai,AiDifficulty.Veteran,armies);var stop=micro.Plan(o,P,Ai,AiDifficulty.Veteran,armies);
            Assert.NotNull(stop);Assert.AreEqual(PlayableCommandKind.Stop,stop.Action.Kind);Assert.AreEqual(10,stop.Action.EntityIds.Count);
            var data=Ai.CopyData();NativeBalanceFields.Write(AiProfileMetadata.Field("difficulty.veteran.micro.chaseEnabled"),data,0);NativeBalanceFields.Write(AiProfileMetadata.Field("difficulty.veteran.micro.focusEnabled"),data,0);
            Assert.IsNull(new AiCombatMicroPolicy().Plan(o,P,new AiProfile(data),AiDifficulty.Veteran,armies));
            data=Ai.CopyData();NativeBalanceFields.Write(AiProfileMetadata.Field("difficulty.veteran.micro.intensity"),data,0);Assert.IsNull(new AiCombatMicroPolicy().Plan(o,P,new AiProfile(data),AiDifficulty.Veteran,armies));
        }
        [Test]
        public void RepeatedChaseRetargetCountsAlreadyAssignedShooters()
        {
            var own=Own().Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,new NavPoint(-10,0),100,false,101,0,0,
                new PlayableTacticalOrderSnapshot(u.Id,u.Owner,Gen,1,0,PlayableTacticalOrderKind.Attack,new NavPoint(25,0),101),maxHealth:100)).ToArray();
            var enemies=new[]{new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(25,0),100,true,0,0,0),
                new PlayableEntitySnapshot(102,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-7,0),100,false,0,0,0),
                new PlayableEntitySnapshot(103,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-7,1),100,false,0,0,0)};
            var o=View(own,enemies);var armies=Army(o,out _);var micro=new AiCombatMicroPolicy();var first=micro.Plan(o,P,Ai,AiDifficulty.Veteran,armies);
            Assert.NotNull(first);Assert.AreEqual(102,first.Action.TargetId);Assert.AreEqual(4,first.Action.EntityIds.Count);
            micro.Commit(first,Gen,0);var a=new PlayableAiAction(77,"player-1",P.ProfileId,P.Revision,Gen,0,first.Action.Kind,first.Action.CopyEntityIds(),first.Action.Target,targetId:first.Action.TargetId,seed:7,sourceIdentity:first.Action.SourceIdentity);
            var receipt=new AiReceiptIdentity(Gen,"player-1",1,1);micro.Applied(a,12,10,receipt);
            micro.Receipt(new PlayableAiTraceRecord(o.Identity,first.Action.ActionId,10,12,10,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"control","player-1",a.SourceIdentity,receipt,AiCombatMicroPolicy.Policy,a.Kind));
            own=own.Select(u=>!a.EntityIds.Contains(u.Id)?u:new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,100,false,a.TargetId,0,0,
                new PlayableTacticalOrderSnapshot(u.Id,u.Owner,Gen,12,10,PlayableTacticalOrderKind.Attack,a.Target,a.TargetId),orderStamp:new PlayableOrderStamp(u.Id,u.Owner,Gen,1,12,10,PlayableOrderOrigin.Ai,PlayableCommandKind.Attack,a.SourceIdentity,77,77),maxHealth:100)).ToArray();
            o=View(own,enemies,30);micro.Observe(o,P,Ai,AiDifficulty.Veteran,armies);var next=micro.Plan(o,P,Ai,AiDifficulty.Veteran,armies);
            Assert.NotNull(next);Assert.AreEqual(PlayableCommandKind.Attack,next.Action.Kind);Assert.AreEqual(103,next.Action.TargetId);Assert.AreEqual(4,next.Action.EntityIds.Count);Assert.IsEmpty(a.EntityIds.Intersect(next.Action.EntityIds));
            o=View(own,enemies.Where(e=>e.Id!=103).ToArray(),30);micro.Observe(o,P,Ai,AiDifficulty.Veteran,armies);var stop=micro.Plan(o,P,Ai,AiDifficulty.Veteran,armies);
            Assert.NotNull(stop);Assert.AreEqual(PlayableCommandKind.Stop,stop.Action.Kind);Assert.AreEqual(6,stop.Action.EntityIds.Count,"One action cancels the remaining common chase when the sole alternative is saturated");
        }
        [Test]
        public void StrategicAttackMoveDoesNotBecomeAChaseStopWhenItsGoalIsFarAway()
        {
            var own=Own().Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,100,true,0,0,0,new PlayableTacticalOrderSnapshot(u.Id,u.Owner,Gen,1,0,PlayableTacticalOrderKind.AttackMove,new NavPoint(25,0),0),orderStamp:new PlayableOrderStamp(u.Id,u.Owner,Gen,1,1,0,PlayableOrderOrigin.Ai,PlayableCommandKind.AttackMove,PlayableAiOpeningComposition.SourceIdentity,1,1),maxHealth:100)).ToArray();
            var o=View(own,new[]{new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(25,0),100,true,0,0,0)});var armies=Army(o,out _);
            Assert.IsNull(new AiCombatMicroPolicy().Plan(o,P,Ai,AiDifficulty.Veteran,armies));
        }
        [Test]
        public void AirFocusUsesMobilityPreferenceButRejectsKnownAntiairExposure()
        {
            var own=Own(6).Select(u=>u.Id<=3?new PlayableEntitySnapshot(u.Id,u.Owner,PlayableEntityKind.Hawk,u.Position,100,false,0,0,0,maxHealth:100,altitude:6):u).ToArray();
            var enemies=Enemy();var o=View(own,enemies);var armies=Army(o,out _);var micro=new AiCombatMicroPolicy();micro.Observe(o,P,Ai,AiDifficulty.Veteran,armies);var plan=micro.Plan(o,P,Ai,AiDifficulty.Veteran,armies);
            Assert.NotNull(plan);Assert.True(plan.Action.EntityIds.Any(id=>id<=3));
            var tower=new PlayableBuildingSnapshot(200,PlayableOwner.Enemy,PlayableBuildingKind.SentryTurret,new NavPoint(0,6),100,1,0,0,default,phase:ConstructionPhase.Ready,includePrivateState:false);
            o=View(own,enemies,buildings:new[]{tower});var noAir=new AiCombatMicroPolicy();noAir.Observe(o,P,Ai,AiDifficulty.Veteran,armies);var underAa=noAir.Plan(o,P,Ai,AiDifficulty.Veteran,armies);
            Assert.True(underAa==null||underAa.Action.EntityIds.All(id=>id>3));
        }
    }
}

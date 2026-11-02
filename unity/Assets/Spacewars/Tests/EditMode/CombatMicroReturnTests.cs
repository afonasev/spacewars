using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroReturnTests
    {
        const long Gen=901;
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        static readonly NavPoint Goal=new NavPoint(-12,0);
        static PlayableAiObservation View(long tick=40,int leaderHealth=100,PlayableOrderOrigin origin=PlayableOrderOrigin.Ai,long sequence=12,bool held=false,PlayableUnitQueueSnapshot queue=null,NavPoint? followerAt=null,PlayableEntityKind kind=PlayableEntityKind.Tank)
        {
            var stamp=new PlayableOrderStamp(1,PlayableOwner.Player,Gen,1,sequence,40,origin,PlayableCommandKind.Follow,PlayableAiOpeningComposition.SourceIdentity,88,88);
            var order=new PlayableTacticalOrderSnapshot(1,PlayableOwner.Player,Gen,sequence,40,PlayableTacticalOrderKind.Follow,Goal,2);
            var units=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,kind,followerAt??new NavPoint(-20,0),90,true,0,0,0,order,held:held,orderStamp:stamp,queue:queue,maxHealth:100),
                new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10,0),leaderHealth,false,0,0,0,maxHealth:100),
                new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10,2),100,false,0,0,0,maxHealth:100)};
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),50)},Array.Empty<KnownBuilding>());
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot()));
        }
        static PlayableAiAction Follow(long id=1,int target=2)=>new PlayableAiAction(id,"player-1",P.ProfileId,P.Revision,Gen,30,PlayableCommandKind.Follow,new[]{1},Goal,targetId:target,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        static AiIntent Intent(PlayableAiAction a)=>new AiIntent("return-control",AiCombatMicroPolicy.Policy,a,120,0,0,Array.Empty<string>());
        static AiCombatMicroPolicy Applied(out AiArmyRegistry registry,out long army,PlayableEntityKind kind=PlayableEntityKind.Tank)
        {
            registry=new AiArmyRegistry("player-1",Gen,Ai,AiDifficulty.Veteran);Assert.True(registry.TryCreate(View(kind:kind),AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out army));
            var micro=new AiCombatMicroPolicy();micro.Commit(new AiCombatMicroPlan{Action=Follow(),Phase=AiCombatMicroPhase.Return,Army=army},Gen,30);
            micro.Applied(Follow(88),12,40,new AiReceiptIdentity(Gen,"player-1",1,1));
            micro.Receipt(new PlayableAiTraceRecord(View().Identity,1,40,12,40,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"control","player-1",PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(Gen,"player-1",1,1),AiCombatMicroPolicy.Policy,PlayableCommandKind.Follow));return micro;
        }
        static AiCombatMicroPolicy Clone(AiCombatMicroPolicy micro)
        {var bytes=WorldWire.Pack(micro.Write);using(var r=new BinaryReader(new MemoryStream(bytes))){var clone=AiCombatMicroPolicy.Read(r,50,Gen);CollectionAssert.AreEqual(bytes,WorldWire.Pack(clone.Write));return clone;}}
        [Test]
        public void ReactionDeliveryRechecksActualTargetCoreAndPhase()
        {
            var registry=new AiArmyRegistry("player-1",Gen,Ai,AiDifficulty.Veteran);Assert.True(registry.TryCreate(View(),AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var army));
            var micro=new AiCombatMicroPolicy();micro.Commit(new AiCombatMicroPlan{Action=Follow(),Phase=AiCombatMicroPhase.Return,Army=army},Gen,30);
            Assert.True(micro.Authorizes(Intent(Follow(88)),View(),P,Ai,AiDifficulty.Veteran,registry));
            Assert.False(micro.Authorizes(Intent(Follow(88)),View(leaderHealth:0),P,Ai,AiDifficulty.Veteran,registry));
            Assert.True(registry.UpdateMission(army,AiArmyPlanner.Policy,AiArmyPhase.Regrouping,40,"lost core"));
            Assert.False(micro.Authorizes(Intent(Follow(88)),View(),P,Ai,AiDifficulty.Veteran,registry));
        }
        [Test]
        public void LostMembershipRetainsOnlyExactCancelProofAndNormalStopSurvivesSave()
        {
            var micro=Applied(out var registry,out var army);registry.Disband(army);var o=View(50);micro.Observe(o,P,Ai,AiDifficulty.Veteran,registry);
            Assert.False(micro.CurrentOverride(o,registry.Capture().Armies.Single(),o.Entities.First(u=>u.Id==1)));
            micro=Clone(micro);var stop=micro.Plan(o,P,Ai,AiDifficulty.Veteran,registry);Assert.NotNull(stop);Assert.AreEqual(PlayableCommandKind.Stop,stop.Action.Kind);CollectionAssert.AreEqual(new[]{1},stop.Action.CopyEntityIds());
            micro.Commit(stop,Gen,50);micro=Clone(micro);Assert.True(micro.Authorizes(Intent(stop.Action),o,P,Ai,AiDifficulty.Veteran,registry));
            foreach(var bad in new[]{View(50,origin:PlayableOrderOrigin.Human),View(50,sequence:13),View(50,held:true)})Assert.False(micro.Authorizes(Intent(stop.Action),bad,P,Ai,AiDifficulty.Veteran,registry));
            var queued=new PlayableQueuedOrderSnapshot(9,9,13,1,PlayableCommandKind.Move,null,0,aiActionId:89);
            foreach(var q in new[]{new PlayableUnitQueueSnapshot(null,queued,Array.Empty<PlayableQueuedOrderSnapshot>(),null),new PlayableUnitQueueSnapshot(null,null,new[]{queued},null)})
                Assert.False(micro.Authorizes(Intent(stop.Action),View(50,queue:q),P,Ai,AiDifficulty.Veteran,registry));
            var wrongArmy=Applied(out var wrongRegistry,out var previousArmy);wrongRegistry.Disband(previousArmy);var wrongPlan=wrongArmy.Plan(o,P,Ai,AiDifficulty.Veteran,wrongRegistry);wrongPlan.Army++;wrongArmy.Commit(wrongPlan,Gen,50);wrongArmy=Clone(wrongArmy);
            Assert.False(wrongArmy.Authorizes(Intent(wrongPlan.Action),o,P,Ai,AiDifficulty.Veteran,wrongRegistry),"Cancellation cannot impersonate a different saved army binding");
            var arbitrary=new AiCombatMicroPolicy();arbitrary.Commit(new AiCombatMicroPlan{Action=new PlayableAiAction(1,"player-1",P.ProfileId,P.Revision,Gen,50,PlayableCommandKind.Stop,new[]{1},Goal,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity),Phase=AiCombatMicroPhase.Regroup,Army=army},Gen,50);
            Assert.False(arbitrary.Authorizes(Intent(stop.Action),o,P,Ai,AiDifficulty.Veteran,registry));
        }
        [Test]
        public void DeadCoreCancelsCurrentFollowButHealthyCoreKeepsFollowing()
        {
            var micro=Applied(out var registry,out _);var healthy=View(50);micro.Observe(healthy,P,Ai,AiDifficulty.Veteran,registry);Assert.IsNull(micro.Plan(healthy,P,Ai,AiDifficulty.Veteran,registry));
            var dead=View(50,leaderHealth:0);micro.Observe(dead,P,Ai,AiDifficulty.Veteran,registry);var stop=micro.Plan(dead,P,Ai,AiDifficulty.Veteran,registry);Assert.NotNull(stop);Assert.AreEqual(PlayableCommandKind.Stop,stop.Action.Kind);
        }
        [Test]
        public void NativeRadialGroundJoinReleasesEvenOutsideTheIdealRoutingPoint()
        {
            var micro=Applied(out var registry,out var army);var o=View(50,followerAt:new NavPoint(-11.55,-1.62));var actor=o.Entities.Single(u=>u.Id==1);var leader=o.Entities.Single(u=>u.Id==2);
            Assert.Less(Math.Sqrt(1.55*1.55+1.62*1.62),P.FollowDistance+P.FollowArrivalTolerance);
            Assert.Greater(Math.Sqrt(.45*.45+1.62*1.62),P.FollowArrivalTolerance+P.Navigation.ArrivalTolerance+PlayableUnitRules.Radius(P,actor.Kind),"Old ideal routing point predicate would hold this actually joined follower");
            micro.Observe(o,P,Ai,AiDifficulty.Veteran,registry);Assert.False(micro.Reserves(1));
            Assert.True(micro.CurrentOverride(o,registry.Capture().Armies.Single(a=>a.Id==army),actor),"Releasing temporary control retains only the exact current native Follow certificate");
            var restored=Clone(micro);Assert.False(restored.Reserves(1));
        }
        [Test]
        public void GroundOutsideNativeRadialThresholdStaysReservedEvenNearIdealPoint()
        {
            var micro=Applied(out var registry,out _);var o=View(50,followerAt:new NavPoint(-12.26,0));micro.Observe(o,P,Ai,AiDifficulty.Veteran,registry);Assert.True(micro.Reserves(1));
        }
        [TestCase(true)][TestCase(false)]
        public void AirJoinUsesActualPointToleranceInsteadOfGroundRadius(bool inside)
        {
            var micro=Applied(out var registry,out _,PlayableEntityKind.Hawk);var o=View(50,followerAt:new NavPoint(-12,P.FollowArrivalTolerance+(inside?-.01:.01)),kind:PlayableEntityKind.Hawk);
            micro.Observe(o,P,Ai,AiDifficulty.Veteran,registry);Assert.AreEqual(!inside,micro.Reserves(1));
        }
        [Test]
        public void SavedPendingBindsOwnerAndEveryActionField()
        {
            var micro=new AiCombatMicroPolicy();micro.Commit(new AiCombatMicroPlan{Action=Follow(),Phase=AiCombatMicroPhase.Return,Army=1},Gen,30);micro=Clone(micro);
            Assert.DoesNotThrow(()=>micro.ValidateOwner("player-1"));Assert.Throws<ArgumentException>(()=>micro.ValidateOwner("enemy-1"));
            Assert.DoesNotThrow(()=>micro.ValidatePending(Follow(88),1));Assert.Throws<ArgumentException>(()=>micro.ValidatePending(Follow(88,3),1));Assert.Throws<ArgumentException>(()=>micro.ValidatePending(Follow(88),2));
        }
    }
}

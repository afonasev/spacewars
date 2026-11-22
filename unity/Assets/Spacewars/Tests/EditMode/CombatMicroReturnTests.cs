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
        static PlayableAiObservation View(long tick=40,int leaderHealth=100,PlayableOrderOrigin origin=PlayableOrderOrigin.Ai,long sequence=12,bool held=false,PlayableUnitQueueSnapshot queue=null,NavPoint? followerAt=null,PlayableEntityKind kind=PlayableEntityKind.Tank,bool combat=false)
        {
            var stamp=new PlayableOrderStamp(1,PlayableOwner.Player,Gen,1,sequence,40,origin,PlayableCommandKind.Follow,PlayableAiOpeningComposition.SourceIdentity,88,88);
            var order=new PlayableTacticalOrderSnapshot(1,PlayableOwner.Player,Gen,sequence,40,PlayableTacticalOrderKind.Follow,Goal,2);
            var units=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,kind,followerAt??new NavPoint(-20,0),90,true,0,0,0,order,held:held,orderStamp:stamp,queue:queue,maxHealth:100),
                new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10,0),leaderHealth,false,0,0,0,maxHealth:100),
                new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10,2),100,false,0,0,0,maxHealth:100)};
            if(combat)units=units.Append(new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-16,-1),100,false,0,0,0)).ToArray();
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
        public void RejectedFollowRetriesTheAcceptedWithdrawalAfterRestore()
        {
            var registry=new AiArmyRegistry("player-1",Gen,Ai,AiDifficulty.Veteran);
            Assert.True(registry.TryCreate(View(),AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var army));
            var move=new PlayableAiAction(1,"player-1",P.ProfileId,P.Revision,Gen,0,PlayableCommandKind.Move,new[]{1},Goal,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var micro=new AiCombatMicroPolicy();
            micro.Commit(new AiCombatMicroPlan{Action=move,Phase=AiCombatMicroPhase.Withdraw,Army=army},Gen,0);
            var receipt=new AiReceiptIdentity(Gen,"player-1",1,1);
            micro.Applied(move,12,1,receipt);
            micro.Receipt(new PlayableAiTraceRecord(View().Identity,1,1,12,1,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"withdrawal","player-1",move.SourceIdentity,receipt,AiCombatMicroPolicy.Policy,move.Kind));

            PlayableAiObservation Withdrawn(long tick)
            {
                var original=View(tick);
                var stamp=new PlayableOrderStamp(1,PlayableOwner.Player,Gen,1,12,1,PlayableOrderOrigin.Ai,PlayableCommandKind.Move,move.SourceIdentity,1,1);
                var order=new PlayableTacticalOrderSnapshot(1,PlayableOwner.Player,Gen,12,1,PlayableTacticalOrderKind.Move,Goal,0);
                var unit=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,Goal,60,false,0,0,0,order,orderStamp:stamp,maxHealth:100);
                return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,
                    original.Entities.Where(u=>u.Id!=1).Prepend(unit).ToArray(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),
                    new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:original.Vision));
            }
            micro.Observe(Withdrawn(2),P,Ai,AiDifficulty.Veteran,registry);
            var current=Withdrawn(50);
            micro.Observe(current,P,Ai,AiDifficulty.Veteran,registry);
            var follow=micro.Plan(current,P,Ai,AiDifficulty.Veteran,registry);
            Assert.NotNull(follow);
            Assert.AreEqual(AiCombatMicroPhase.Return,follow.Phase);
            micro.Commit(follow,Gen,50);
            micro=Clone(micro);
            micro.Receipt(new PlayableAiTraceRecord(current.Identity,follow.Action.ActionId,50,13,50,PlayableAiDeliveryStatus.Rejected,PlayableCommandStatus.Rejected,"route changed","player-1",move.SourceIdentity,
                new AiReceiptIdentity(Gen,"player-1",2,2),AiCombatMicroPolicy.Policy,follow.Action.Kind));
            Assert.True(micro.Reserves(1),"The accepted short withdrawal still owns the actor.");
            var retry=micro.Plan(current,P,Ai,AiDifficulty.Veteran,registry);
            Assert.NotNull(retry);
            Assert.AreEqual(AiCombatMicroPhase.Return,retry.Phase);
            Assert.Greater(retry.Action.ActionId,follow.Action.ActionId);
        }
        [Test]
        public void JoinedTankSelectsAnotherFiringPointWhenTheShortestPointHasAWreck()
        {
            var profile=PlayableProfile.Create(P.CopyData(),new ProvingGroundMap(),P.DisplayName);
            var micro=Applied(out var registry,out var army);
            const long tick=50;
            var patient=new NavPoint(-12,0);
            var current=View(tick,followerAt:patient);
            var enemy=new NavPoint(patient.X+profile.TankRange+.8,0);
            var wreckAt=new NavPoint(patient.X+.8+2*profile.Navigation.ArrivalTolerance,0);
            const double wreckRadius=.25;
            var wreck=new PlayableWreckSnapshot(99,PlayableOwner.Enemy,PlayableEntityKind.Tank,wreckAt,0,wreckRadius,
                0,0,1,40,40,40,100,110);
            var units=new[]{
                current.Entities.Single(u=>u.Id==1),
                new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10.85,1.85),100,false,101,0,0,maxHealth:100),
                new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-11,3.85),100,false,0,0,0,maxHealth:100),
                new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,enemy,100,false,0,0,0,maxHealth:100)};
            var observed=PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,Gen,7,tick,tick,
                RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,units,Array.Empty<PlayableBuildingSnapshot>(),
                Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:current.Vision,wrecks:new[]{wreck}));
            Assert.True(registry.UpdateMission(army,AiArmyPlanner.Policy,AiArmyPhase.Engaging,tick,"contact"));
            micro.Observe(observed,profile,Ai,AiDifficulty.Veteran,registry);

            var resume=micro.Plan(observed,profile,Ai,AiDifficulty.Veteran,registry);
            Assert.NotNull(resume);
            Assert.AreEqual(AiCombatMicroPhase.Resume,resume.Phase);
            var geometry=new NavGeometry(profile.ArenaHalfExtent,new[]{new NavObstacle(wreckAt,wreckRadius)},1);
            Assert.True(geometry.SegmentFree(patient,resume.Action.Target,profile.TankCollisionRadius));
            Assert.LessOrEqual(AiCombatMicroSafety.Distance(resume.Action.Target,enemy),profile.TankRange);
            Assert.True(micro.IsWithdrawalRecovery(observed,resume.Action));
            Assert.False(micro.IsWithdrawalRecovery(View(tick,origin:PlayableOrderOrigin.Human,followerAt:patient),resume.Action));
        }
        [Test]
        public void EarlyReturnUsesTheObservedSwitchButWaitsForThePreviousShotToClear()
        {
            var profile=PlayableProfile.Create(P.CopyData(),new ProvingGroundMap(),P.DisplayName);
            var micro=Applied(out var registry,out var army);
            var patient=new NavPoint(-12,0);
            var enemy=new NavPoint(patient.X+profile.TankRange+.1,0);
            var original=View(50,followerAt:patient);
            var units=new[]{
                original.Entities.Single(u=>u.Id==1),
                new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10.85,1.85),100,false,101,0,0,maxHealth:100),
                new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-11,3.85),100,false,0,0,0,maxHealth:100),
                new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,enemy,100,false,0,0,0,maxHealth:100)};
            PlayableAiObservation At(long tick,params PlayableProjectileSnapshot[] shots)=>
                PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,Gen,7,tick,tick,
                    RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,units,Array.Empty<PlayableBuildingSnapshot>(),
                    shots,new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:original.Vision));
            var incoming=new PlayableProjectileSnapshot(1,101,1,enemy,faction:PlayableOwner.Enemy,directLaunchTick:45);
            var switched=new PlayableProjectileSnapshot(2,101,2,enemy,faction:PlayableOwner.Enemy,directLaunchTick:50);
            Assert.True(registry.UpdateMission(army,AiArmyPlanner.Policy,AiArmyPhase.Engaging,45,"contact"));
            micro.Observe(At(45,incoming),profile,Ai,AiDifficulty.Veteran,registry);

            Assert.IsNull(micro.PlanSwitchedReturn(At(49,incoming),profile,Ai,AiDifficulty.Veteran,registry));
            Assert.IsNull(micro.PlanSwitchedReturn(At(50,incoming,switched),profile,Ai,AiDifficulty.Veteran,registry));
            var observation=At(51,switched);
            var plan=micro.PlanSwitchedReturn(observation,profile,Ai,AiDifficulty.Veteran,registry);
            Assert.NotNull(plan);
            Assert.AreEqual(AiCombatMicroPhase.Resume,plan.Phase);
            Assert.True(micro.IsQuickRecovery(observation,profile,Ai,AiDifficulty.Veteran,plan.Action));
            Assert.True(micro.Authorizes(Intent(plan.Action),observation,profile,Ai,AiDifficulty.Veteran,registry));
        }
        [Test]
        public void SwitchedAttackerSupportsShortReturnWithoutAnAnchorTarget()
        {
            var profile=PlayableProfile.Create(P.CopyData(),new ProvingGroundMap(),P.DisplayName);
            var original=View();
            var registry=new AiArmyRegistry("player-1",Gen,Ai,AiDifficulty.Veteran);
            Assert.True(registry.TryCreate(original,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var army));
            Assert.True(registry.UpdateMission(army,AiArmyPlanner.Policy,AiArmyPhase.Engaging,40,"contact"));
            var patient=new NavPoint(-12,0);
            var enemy=new NavPoint(patient.X+profile.TankRange+.45,0);
            var move=new PlayableAiAction(1,"player-1",profile.ProfileId,profile.Revision,Gen,39,PlayableCommandKind.Move,
                new[]{1},patient,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var micro=new AiCombatMicroPolicy();
            micro.Commit(new AiCombatMicroPlan{Action=move,Phase=AiCombatMicroPhase.Withdraw,Army=army},Gen,39);
            var receipt=new AiReceiptIdentity(Gen,"player-1",1,1);
            micro.Applied(move,12,40,receipt);
            micro.Receipt(new PlayableAiTraceRecord(original.Identity,1,40,12,40,PlayableAiDeliveryStatus.Applied,
                PlayableCommandStatus.Applied,"withdrawal","player-1",move.SourceIdentity,receipt,AiCombatMicroPolicy.Policy,move.Kind));
            var stamp=new PlayableOrderStamp(1,PlayableOwner.Player,Gen,1,12,40,PlayableOrderOrigin.Ai,move.Kind,move.SourceIdentity,1,1);
            var units=new[]{
                new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,patient,60,false,0,0,0,
                    navigationOutcome:NavigationOutcome.Arrived,orderStamp:stamp,maxHealth:100),
                new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-11,2),100,false,0,0,0,maxHealth:100),
                new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-11,4),100,false,0,0,0,maxHealth:100),
                new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,enemy,100,false,0,0,0)};
            PlayableAiObservation At(long tick,bool distant=false,params PlayableProjectileSnapshot[] shots)
            {
                var actors=distant?units.Take(3).Append(new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,
                    new NavPoint(enemy.X+3,enemy.Z),100,false,0,0,0)).ToArray():units;
                return PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,Gen,7,tick,tick,
                    RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,actors,Array.Empty<PlayableBuildingSnapshot>(),
                    shots,new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:original.Vision));
            }
            Assert.True(micro.ShortReverseOrder(1,patient,stamp,45,Ai,AiDifficulty.Veteran));
            Assert.False(micro.ShortReverseOrder(1,new NavPoint(-13,0),stamp,45,Ai,AiDifficulty.Veteran));
            Assert.False(micro.ShortReverseOrder(1,patient,stamp,1000,Ai,AiDifficulty.Veteran));
            var incoming=new PlayableProjectileSnapshot(1,101,1,enemy,faction:PlayableOwner.Enemy,directLaunchTick:45);
            var switched=new PlayableProjectileSnapshot(2,101,2,enemy,faction:PlayableOwner.Enemy,directLaunchTick:50);
            micro.Observe(At(45,false,incoming),profile,Ai,AiDifficulty.Veteran,registry);
            Assert.IsNull(micro.PlanSwitchedReturn(At(49),profile,Ai,AiDifficulty.Veteran,registry));
            var observation=At(51,false,switched);
            var resume=micro.PlanSwitchedReturn(observation,profile,Ai,AiDifficulty.Veteran,registry);
            Assert.NotNull(resume);
            Assert.AreEqual(AiCombatMicroPhase.Resume,resume.Phase);
            Assert.True(AiCombatMicroPolicy.IsShortReturn(profile,Ai,AiDifficulty.Veteran,units[0],resume.Action.Target));
            Assert.True(micro.Authorizes(Intent(resume.Action),observation,profile,Ai,AiDifficulty.Veteran,registry));
            Assert.False(micro.Authorizes(Intent(resume.Action),At(52,true,switched),profile,Ai,AiDifficulty.Veteran,registry));
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
        public void JoinedFollowerCanConcentrateFireAndDeliveryRechecksTheJoin()
        {
            var micro=Applied(out var registry,out _);
            var joined=new NavPoint(-12,0);
            micro.Observe(View(50,followerAt:joined),P,Ai,AiDifficulty.Veteran,registry);
            var ready=View(200,followerAt:joined,combat:true);
            micro.Observe(ready,P,Ai,AiDifficulty.Veteran,registry);
            var focus=micro.Plan(ready,P,Ai,AiDifficulty.Veteran,registry);
            Assert.NotNull(focus);
            Assert.AreEqual(101,focus.Action.TargetId);
            Assert.Contains(1,focus.Action.CopyEntityIds());
            Assert.True(micro.Authorizes(Intent(focus.Action),ready,P,Ai,AiDifficulty.Veteran,registry));
            Assert.False(micro.Authorizes(Intent(focus.Action),View(201,followerAt:new NavPoint(-13,0),combat:true),P,Ai,AiDifficulty.Veteran,registry));
            Assert.False(micro.Authorizes(Intent(focus.Action),View(201,followerAt:joined,origin:PlayableOrderOrigin.Human,combat:true),P,Ai,AiDifficulty.Veteran,registry));
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
        public void RepairOriginRoundTripPreservesGroundSurfaceAndAcceptsLegacyAircraftSentinel()
        {
            var position=new NavLocation(new NavPoint(-27,13),"repair-approach");
            byte[] Save(NavLocation? origin)
            {
                var micro=new AiCombatMicroPolicy();
                var move=new PlayableAiAction(1,"player-1",P.ProfileId,P.Revision,Gen,30,
                    PlayableCommandKind.Move,new[]{1},Goal,parentId:7,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
                micro.Commit(new AiCombatMicroPlan{Action=move,Phase=AiCombatMicroPhase.Repair,Army=0},Gen,30);
                var receipt=new AiReceiptIdentity(Gen,"player-1",1,1);
                micro.Applied(move,12,40,receipt,origin);
                micro.Receipt(new PlayableAiTraceRecord(View().Identity,1,40,12,40,PlayableAiDeliveryStatus.Applied,
                    PlayableCommandStatus.Applied,"repair","player-1",move.SourceIdentity,receipt,AiCombatMicroPolicy.Policy,move.Kind));
                Clone(micro);
                return WorldWire.Pack(micro.Write);
            }
            var ground=Save(position);
            var absent=Save(null);
            CollectionAssert.AreEqual(absent,Save(default(NavLocation)));
            var encoded=WorldWire.Pack(w=>GroupOrderWire.WriteLocation(w,position));
            int offset=Enumerable.Range(0,ground.Length-encoded.Length+1)
                .Single(i=>ground.Skip(i).Take(encoded.Length).SequenceEqual(encoded));
            var sentinel=WorldWire.Pack(w=>GroupOrderWire.WriteLocation(w,default(NavLocation)));
            var legacy=ground.Take(offset).Concat(sentinel).Concat(ground.Skip(offset+encoded.Length)).ToArray();
            using var reader=new BinaryReader(new MemoryStream(legacy));
            var restored=AiCombatMicroPolicy.Read(reader,50,Gen);
            Assert.AreEqual(legacy.Length,reader.BaseStream.Position);
            Assert.True(restored.Reserves(1));
            CollectionAssert.AreEqual(absent,WorldWire.Pack(restored.Write));
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

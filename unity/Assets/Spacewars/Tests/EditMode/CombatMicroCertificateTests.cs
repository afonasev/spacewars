using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroCertificateTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        const long Gen=901;
        static PlayableAiObservation View(long tick,int health,PlayableOrderStamp stamp=null,PlayableTacticalOrderSnapshot order=null,bool shots=true)
        {
            var units=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(0,0),health,false,0,0,0,order,orderStamp:stamp,maxHealth:100),
                new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-4,3),100,false,0,0,0,maxHealth:100),new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-4,-3),100,false,0,0,0,maxHealth:100)}
                .Concat(Enumerable.Range(101,3).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(9,(id-102)*2),100,false,0,0,Math.PI,maxHealth:100))).ToArray();
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),50)},Array.Empty<KnownBuilding>());
            var bullets=shots?Enumerable.Range(101,3).Select(id=>new PlayableProjectileSnapshot(id,id,1,new NavPoint(4,(id-102)),faction:PlayableOwner.Enemy)).ToArray():Array.Empty<PlayableProjectileSnapshot>();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,Array.Empty<PlayableBuildingSnapshot>(),bullets,new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot()));
        }
        static byte[] Encode(AiCombatMicroPolicy p){using(var s=new MemoryStream()){using(var w=new BinaryWriter(s,System.Text.Encoding.UTF8,true))p.Write(w);return s.ToArray();}}
        static AiCombatMicroPolicy Clone(AiCombatMicroPolicy p,long tick){using(var r=new BinaryReader(new MemoryStream(Encode(p))))return AiCombatMicroPolicy.Read(r,tick,Gen);}
        static PlayableOrderStamp Stamp(PlayableOrderOrigin origin=PlayableOrderOrigin.Ai,long action=88,long sequence=12,string source=PlayableAiOpeningComposition.SourceIdentity,PlayableOwner owner=PlayableOwner.Player)=>new PlayableOrderStamp(1,owner,Gen,1,sequence,40,origin,PlayableCommandKind.Move,source,action,action);
        [Test]
        public void AppliedLocalFocusDoesNotBlockUrgentWithdrawalAfterRealObservedDamage()
        {
            var initial=View(0,100,shots:false);var registry=new AiArmyRegistry("player-1",Gen,Ai,AiDifficulty.Veteran);Assert.True(registry.TryCreate(initial,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var army));
            var micro=new AiCombatMicroPolicy();var goal=new NavPoint(9,-2);var local=new PlayableAiAction(1,"player-1",P.ProfileId,P.Revision,Gen,0,PlayableCommandKind.Attack,new[]{1},goal,targetId:101,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            micro.Commit(new AiCombatMicroPlan{Action=local,Phase=AiCombatMicroPhase.Focus,Army=army},Gen,0);
            var actual=new PlayableAiAction(88,local.PlayerId,local.ProfileId,local.ProfileRevision,Gen,0,local.Kind,local.CopyEntityIds(),goal,targetId:101,seed:7,sourceIdentity:local.SourceIdentity);var receipt=new AiReceiptIdentity(Gen,"player-1",1,1);
            micro.Applied(actual,12,0,receipt);micro.Receipt(new PlayableAiTraceRecord(initial.Identity,1,0,12,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"control",local.PlayerId,local.SourceIdentity,receipt,AiCombatMicroPolicy.Policy,local.Kind));
            var stamp=new PlayableOrderStamp(1,PlayableOwner.Player,Gen,1,12,0,PlayableOrderOrigin.Ai,PlayableCommandKind.Attack,local.SourceIdentity,88,88);
            var order=new PlayableTacticalOrderSnapshot(1,PlayableOwner.Player,Gen,12,0,PlayableTacticalOrderKind.Attack,goal,101);
            micro.Observe(View(0,100,stamp,order,false),P,Ai,AiDifficulty.Veteran,registry);var damaged=View(30,60,stamp,order);micro.Observe(damaged,P,Ai,AiDifficulty.Veteran,registry);
            var withdrawal=micro.Plan(damaged,P,Ai,AiDifficulty.Veteran,registry);Assert.NotNull(withdrawal);Assert.AreEqual(AiCombatMicroPhase.Withdraw,withdrawal.Phase);Assert.AreEqual(PlayableCommandKind.Move,withdrawal.Action.Kind);CollectionAssert.AreEqual(new[]{1},withdrawal.Action.CopyEntityIds());
            micro.Commit(withdrawal,Gen,30);Assert.True(micro.Reserves(1));
        }
        [Test]
        public void ExactAppliedCertificateSurvivesActiveReservationAndRejectsUnknownHumanReplacedOrRevokedOrders()
        {
            var initial=View(0,100,shots:false);var registry=new AiArmyRegistry("player-1",Gen,Ai,AiDifficulty.Veteran);
            Assert.True(registry.TryCreate(initial,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var army));
            var micro=new AiCombatMicroPolicy();micro.Observe(initial,P,Ai,AiDifficulty.Veteran,registry);
            var observed=View(30,60);micro.Observe(observed,P,Ai,AiDifficulty.Veteran,registry);var plan=micro.Plan(observed,P,Ai,AiDifficulty.Veteran,registry);
            Assert.NotNull(plan);Assert.AreEqual(AiCombatMicroPhase.Withdraw,plan.Phase);
            micro.Commit(plan,Gen,30);
            var actual=new PlayableAiAction(88,"player-1",P.ProfileId,P.Revision,Gen,30,plan.Action.Kind,plan.Action.CopyEntityIds(),plan.Action.Target,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            micro.Applied(actual,12,40,new AiReceiptIdentity(Gen,"player-1",1,1));
            micro.Receipt(new PlayableAiTraceRecord(observed.Identity,plan.Action.ActionId,40,12,40,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"control",actual.PlayerId,actual.SourceIdentity,new AiReceiptIdentity(Gen,actual.PlayerId,1,1),AiCombatMicroPolicy.Policy,actual.Kind));
            var order=new PlayableTacticalOrderSnapshot(1,PlayableOwner.Player,Gen,12,40,PlayableTacticalOrderKind.Move,actual.Target,0);
            var current=View(40,60,Stamp(),order);var group=registry.Capture().Armies.Single(a=>a.Id==army);var actor=current.Entities.Single(u=>u.Id==1);
            Assert.True(micro.CurrentOverride(current,group,actor));
            var clone=Clone(micro,40);CollectionAssert.AreEqual(Encode(micro),Encode(clone));Assert.True(clone.CurrentOverride(current,group,actor));
            foreach(var bad in new[]{Stamp(PlayableOrderOrigin.Human),Stamp(action:89),Stamp(sequence:13),Stamp(source:"unknown"),Stamp(owner:PlayableOwner.Enemy)})
            {var wrong=View(40,60,bad,order);Assert.False(clone.CurrentOverride(wrong,group,wrong.Entities.Single(u=>u.Id==1)));}
            var foreign=group.Copy();foreign.OwnerId="enemy-1";Assert.False(clone.CurrentOverride(current,foreign,actor));
            foreign=group.Copy();foreign.Id++;Assert.False(clone.CurrentOverride(current,foreign,actor));
            foreign=group.Copy();foreign.Members=new[]{2,3};Assert.False(clone.CurrentOverride(current,foreign,actor));
            var human=View(41,60,Stamp(PlayableOrderOrigin.Human),order);clone.Observe(human,P,Ai,AiDifficulty.Veteran,registry);Assert.False(clone.CurrentOverride(current,group,actor),"Observed human replacement revokes the saved certificate");
            clone=Clone(micro,40);clone.Preempt(new[]{1});Assert.False(clone.CurrentOverride(current,group,actor));Assert.False(clone.Reserves(1));
            clone=Clone(micro,40);registry.Disband(army);clone.Observe(current,P,Ai,AiDifficulty.Veteran,registry);Assert.False(clone.CurrentOverride(current,group,actor),"A previous group cannot authorize a future override");
        }
        [TestCase(PlayableEntityKind.Tank,PlayableBuildingKind.SentryTurret)]
        [TestCase(PlayableEntityKind.Hawk,PlayableBuildingKind.SentryTurret)]
        [TestCase(PlayableEntityKind.Tank,PlayableBuildingKind.GaussTower)]
        public void StaticFireRejectsCrossingAndReturnButAllowsAnActualMonotoneEscape(PlayableEntityKind kind,PlayableBuildingKind towerKind)
        {
            var o=View(60,60);var tower=new PlayableBuildingSnapshot(200,PlayableOwner.Enemy,towerKind,new NavPoint(0,0),100,1,0,0,default,phase:ConstructionPhase.Ready,includePrivateState:false);
            PlayableAiObservation At(NavPoint from)
            {
                var u=new PlayableEntitySnapshot(1,PlayableOwner.Player,kind,from,60,false,0,0,0,maxHealth:100,altitude:kind==PlayableEntityKind.Hawk?6:0);
                return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,60,60,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,new[]{u},new[]{tower},Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision));
            }
            var crossing=At(new NavPoint(-35,0));var actor=crossing.Entities.Single();
            Assert.False(AiCombatMicroSafety.Path(crossing,P,actor,new[]{new NavPoint(35,0)},false));
            Assert.False(AiCombatMicroSafety.Path(crossing,P,actor,new[]{new NavPoint(35,0)},true));
            var escaping=At(new NavPoint(6,0));Assert.True(AiCombatMicroSafety.Path(escaping,P,escaping.Entities.Single(),new[]{new NavPoint(35,0)},true));
            Assert.False(AiCombatMicroSafety.Path(escaping,P,escaping.Entities.Single(),new[]{new NavPoint(5,0)},true));
        }
        [Test]
        public void GroundOnlyGaussDoesNotCreateHiddenAntiairAuthority()
        {
            var o=View(60,60);var tower=new PlayableBuildingSnapshot(200,PlayableOwner.Enemy,PlayableBuildingKind.GaussTower,new NavPoint(0,0),100,1,0,0,default,phase:ConstructionPhase.Ready,includePrivateState:false);
            var u=new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Hawk,new NavPoint(5,0),100,false,0,0,0,altitude:6);
            Assert.False(AiCombatMicroSafety.Covers(o,P,tower,u,u.Position));
        }
    }
}

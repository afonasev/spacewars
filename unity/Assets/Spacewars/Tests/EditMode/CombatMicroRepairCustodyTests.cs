using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroRepairCustodyTests
    {
        const long Gen=901;
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        static readonly NavPoint Goal=new NavPoint(-27,10);
        static object Field(object value,string name)=>value.GetType().GetField(name,F).GetValue(value);
        static IDictionary Map(AiCombatMicroPolicy micro,string name)=>(IDictionary)Field(micro,name);
        static PlayableAiObservation View(int health=28,long tick=1,string invalid=null,bool completed=false)
        {
            var stamp=new PlayableOrderStamp(1,PlayableOwner.Player,Gen,1,invalid=="new-ai"?13:12,0,
                invalid=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Move,
                PlayableAiOpeningComposition.SourceIdentity,invalid=="new-ai"?89:88,invalid=="new-ai"?89:88);
            var order=completed?null:new PlayableTacticalOrderSnapshot(1,PlayableOwner.Player,Gen,12,0,PlayableTacticalOrderKind.Move,Goal,0);
            var units=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-28,10),health,false,0,0,0,order,
                held:invalid=="held",departing:invalid=="departing",orderStamp:stamp,maxHealth:100,
                navigationOutcome:invalid=="unreachable"?NavigationOutcome.Unreachable:invalid=="rejected"?NavigationOutcome.Rejected:completed?NavigationOutcome.Arrived:NavigationOutcome.Moving),
                new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-25,14),100,false,0,0,0,maxHealth:100),
                new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-25,6),100,false,0,0,0,maxHealth:100)};
            var towers=invalid=="tower-gone"?Array.Empty<PlayableBuildingSnapshot>():new[]{new PlayableBuildingSnapshot(200,PlayableOwner.Player,PlayableBuildingKind.RepairTower,new NavPoint(-14,10),100,1,0,0,default,
                phase:invalid=="tower-unready"?ConstructionPhase.Constructing:ConstructionPhase.Ready)};
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),50)},Array.Empty<KnownBuilding>());
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,towers,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot()));
        }
        static AiCombatMicroPolicy Accepted(PlayableAiObservation o,long army,bool applied=true,AiCombatMicroPhase phase=AiCombatMicroPhase.Repair)
        {
            var m=new AiCombatMicroPolicy();var local=new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,Gen,0,PlayableCommandKind.Move,new[]{1},Goal,parentId:phase==AiCombatMicroPhase.Repair?200:0,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            m.Commit(new AiCombatMicroPlan{Action=local,Phase=phase,Army=army},Gen,0);
            if(applied){var actual=new PlayableAiAction(88,o.OwnerId,P.ProfileId,P.Revision,Gen,0,local.Kind,new[]{1},Goal,parentId:local.ParentId,seed:7,sourceIdentity:local.SourceIdentity);var receipt=new AiReceiptIdentity(Gen,o.OwnerId,1,1);
                m.Applied(actual,12,0,receipt);m.Receipt(new PlayableAiTraceRecord(o.Identity,1,0,12,0,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"accepted repair",o.OwnerId,local.SourceIdentity,receipt,AiCombatMicroPolicy.Policy,local.Kind));}
            return m;
        }
        static AiArmyRegistry Registry(PlayableAiObservation o,out long army)
        {var r=new AiArmyRegistry(o.OwnerId,Gen,Ai,AiDifficulty.Veteran);Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out army));return r;}
        static void Observe(AiCombatMicroPolicy m,PlayableAiObservation o,AiArmyRegistry r)=>m.Observe(o,P,Ai,AiDifficulty.Veteran,r);
        static byte[] Bytes(AiCombatMicroPolicy m)=>WorldWire.Pack(m.Write);
        static AiCombatMicroPolicy Cold(AiCombatMicroPolicy m,long tick)
        {using(var reader=new BinaryReader(new MemoryStream(Bytes(m))))return AiCombatMicroPolicy.Read(reader,tick,Gen);}

        [TestCase(false)][TestCase(true)]
        public void ExactAcceptedRepairRetainsCustodyAfterOriginRetirementWithoutRebinding(bool completed)
        {
            var o=View(completed:completed);var r=Registry(o,out var old);var m=Accepted(o,old);Observe(m,o,r);byte[] before=Bytes(m);r.Disband(old);
            Observe(m,o,r);Assert.True(m.Reserves(1));CollectionAssert.AreEqual(before,Bytes(m));Assert.Zero(r.ArmyFor(1));
            Assert.AreEqual(old,Field(Map(m,"clients")[1],"Army"));Assert.AreEqual(old,Field(Map(m,"certificates")[1],"Army"));
            Assert.False(m.CurrentOverride(o,r.Capture().Armies.Single(a=>a.Id==old),o.Entities.Single(u=>u.Id==1)));
            var cold=Cold(m,o.Tick);CollectionAssert.AreEqual(Bytes(m),Bytes(cold));Observe(cold,View(tick:2,completed:completed),r);Assert.True(cold.Reserves(1));
        }
        [TestCase("human")][TestCase("new-ai")][TestCase("held")][TestCase("departing")]
        [TestCase("unreachable")][TestCase("rejected")][TestCase("tower-gone")][TestCase("tower-unready")][TestCase("timeout")]
        [TestCase("pair-sequence")][TestCase("pair-action")][TestCase("pair-goal")][TestCase("certificate-missing")]
        public void RetiredOriginCannotPreserveInvalidRepair(string invalid)
        {
            var o=View();var r=Registry(o,out var old);var m=Accepted(o,old);r.Disband(old);
            if(invalid.StartsWith("pair-")){var c=Map(m,"clients")[1];string field=invalid=="pair-sequence"?"Sequence":invalid=="pair-action"?"ActionId":"Goal";
                c.GetType().GetField(field,F).SetValue(c,field=="Goal"?(object)new NavPoint(-26,10):(long)999);}
            if(invalid=="certificate-missing")Map(m,"certificates").Remove(1);
            Observe(m,View(tick:invalid=="timeout"?10000:2,invalid:invalid),r);Assert.False(m.Reserves(1));Assert.IsFalse(Map(m,"clients").Contains(1));
        }
        [TestCase(false)][TestCase(true)]
        public void UnappliedRepairAndAppliedWithdrawalStillLoseRetiredArmy(bool appliedWithdrawal)
        {
            var o=View();var r=Registry(o,out var old);var m=Accepted(o,old,applied:appliedWithdrawal,phase:appliedWithdrawal?AiCombatMicroPhase.Withdraw:AiCombatMicroPhase.Repair);
            r.Disband(old);Observe(m,o,r);Assert.False(m.Reserves(1));
        }
        [TestCase(false)][TestCase(true)]
        public void HealedRetainedRepairWithoutStrictCurrentCoreReleasesForOrdinaryAssignment(bool reinforcement)
        {
            var o=View();var r=Registry(o,out var old);var m=Accepted(o,old);r.Disband(old);Observe(m,o,r);Assert.True(m.Reserves(1));
            if(reinforcement){Assert.True(r.TryCreate(o,AiArmyRole.MobileDefense,AiArmyPlanner.Policy,new[]{2,3},out var current,requiredMinimum:2));Assert.True(r.TryReinforce(o,current,AiArmyPlanner.Policy,new[]{1}));}
            var healed=View(90,tick:2);Observe(m,healed,r);Assert.IsNull(m.Plan(healed,P,Ai,AiDifficulty.Veteran,r));Assert.False(m.Reserves(1));
            if(reinforcement)CollectionAssert.Contains(r.Capture().Armies.Single(a=>a.Phase!=AiArmyPhase.Disbanded).Reinforcements,1);
        }
        [Test]
        public void FreshFollowUsesIndependentCurrentMembershipAndRechecksItAtDelivery()
        {
            var o=View();var r=Registry(o,out var old);var m=Accepted(o,old);r.Disband(old);Observe(m,o,r);
            // Independent normal registry admission; no certificate or client rebind.
            Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var current));Assert.AreNotEqual(old,current);
            var healed=View(90,tick:2);Observe(m,healed,r);var cold=Cold(m,healed.Tick);var plan=m.Plan(healed,P,Ai,AiDifficulty.Veteran,r);var twin=cold.Plan(healed,P,Ai,AiDifficulty.Veteran,r);
            Assert.NotNull(plan);Assert.AreEqual(AiCombatMicroPhase.Return,plan.Phase);Assert.AreEqual(PlayableCommandKind.Follow,plan.Action.Kind);Assert.AreEqual(current,plan.Army);Assert.AreEqual(2,plan.Action.TargetId);
            Assert.AreEqual(old,Field(Map(m,"certificates")[1],"Army"));CollectionAssert.AreEqual(Bytes(m),Bytes(cold));Assert.AreEqual(plan.Action.TargetId,twin.Action.TargetId);
            m.Commit(plan,Gen,2);var intent=new AiIntent("current-return",AiCombatMicroPolicy.Policy,plan.Action,100,0,0,Array.Empty<string>());
            Assert.True(m.Authorizes(intent,healed,P,Ai,AiDifficulty.Veteran,r));r.Disband(current);Assert.False(m.Authorizes(intent,healed,P,Ai,AiDifficulty.Veteran,r));
        }
    }
}

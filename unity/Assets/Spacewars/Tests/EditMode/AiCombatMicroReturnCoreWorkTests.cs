using System;
using System.IO;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiCombatMicroReturnCoreWorkTests
    {
        const long Generation=905;static readonly PlayableProfile P=PlayableProfile.Default;static readonly AiProfile Ai=AiProfile.Initial;
        static PlayableAiObservation View(int leaderHealth=100,double peerDistance=2,bool held=false)
        {var units=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-20,0),90,false,0,0,0,maxHealth:100),new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10,0),leaderHealth,false,0,0,0,held:held,maxHealth:100),new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10,peerDistance),100,false,0,0,0,maxHealth:100)};return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Generation,7,40,40,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,units,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));}
        static byte[] Bytes(Action<BinaryWriter> write){using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))write(w);return m.ToArray();}}
        [TestCase(100,2,false,false,true)][TestCase(0,2,false,false,false)][TestCase(100,100,false,false,false)][TestCase(100,2,true,false,false)][TestCase(100,2,false,true,false)]
        public void PaidCoreUsesTheSameStrictAuthorizesPredicateAndColdSnapshot(int health,double distance,bool held,bool regroup,bool expected)
        {
            var registry=new AiArmyRegistry("player-1",Generation,Ai,AiDifficulty.Veteran);Assert.True(registry.TryCreate(View(),AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var army));if(regroup)Assert.True(registry.UpdateMission(army,AiArmyPlanner.Policy,AiArmyPhase.Regrouping,40,"test"));var o=View(health,distance,held);
            var action=new PlayableAiAction(1,"player-1",P.ProfileId,P.Revision,Generation,40,PlayableCommandKind.Follow,new[]{1},new NavPoint(-12,0),targetId:2,seed:7,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);var policy=new AiCombatMicroPolicy();policy.Commit(new AiCombatMicroPlan{Action=action,Phase=AiCombatMicroPhase.Return,Army=army},Generation,40);var intent=new AiIntent("core",AiCombatMicroPolicy.Policy,action,120,0,0,Array.Empty<string>());Assert.AreEqual(expected,policy.Authorizes(intent,o,P,Ai,AiDifficulty.Veteran,registry));
            var storage=new AiCombatMicroExecutionStorage();long baseline=storage.Retained;long grant=10000;Assert.True(AiCombatMicroPolicy.ReturnCoreWork.TryCreate(o,P,Ai,AiDifficulty.Veteran,registry,1,army,2,ref grant,storage,out var work));Assert.Less(grant,10000);
            using(work){var before=Bytes(work.Write);Assert.Zero(work.Advance(0,()=>throw new Exception("zero grant")));Assert.Zero(work.Advance(1,()=>false));CollectionAssert.AreEqual(before,Bytes(work.Write));while(!work.Complete){Assert.AreEqual(1,work.Advance(1));var bytes=Bytes(work.Write);long coldGrant=10000;AiCombatMicroPolicy.ReturnCoreWork cold;using(var r=new BinaryReader(new MemoryStream(bytes)))cold=AiCombatMicroPolicy.ReturnCoreWork.Read(r,o,P,Ai,AiDifficulty.Veteran,registry,1,army,2,ref coldGrant,storage);using(cold){CollectionAssert.AreEqual(bytes,Bytes(cold.Write));Assert.AreEqual(work.Advance(1),cold.Advance(1));CollectionAssert.AreEqual(Bytes(work.Write),Bytes(cold.Write));}}Assert.AreEqual(expected,work.Allowed);}
            Assert.AreEqual(baseline,storage.Retained);
        }
        [Test]public void UnpaidFactoryAndMemoryRefusalDoNotCopyOrSpendCredits()
        {
            var o=View();var registry=new AiArmyRegistry("player-1",Generation,Ai,AiDifficulty.Veteran);Assert.True(registry.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var army));var storage=new AiCombatMicroExecutionStorage();long grant=0;Assert.False(AiCombatMicroPolicy.ReturnCoreWork.TryCreate(o,P,Ai,AiDifficulty.Veteran,registry,1,army,2,ref grant,storage,out _));Assert.Zero(grant);Assert.AreEqual(AiCombatMicroExecutionStorage.ControlBytes,storage.Retained);grant=10000;Assert.False(AiCombatMicroPolicy.ReturnCoreWork.TryCreate(o,P,Ai,AiDifficulty.Veteran,registry,1,army,2,ref grant,new AiCombatMicroExecutionStorage(128),out _));Assert.AreEqual(10000,grant);
        }
    }
}

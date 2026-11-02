using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroRepairEscapeTests
    {
        const long Gen=901;
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        static PlayableAiObservation View(long tick,int health,bool shots)
        {
            var units=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-10,0),health,false,101,0,0,new PlayableTacticalOrderSnapshot(1,PlayableOwner.Player,Gen,1,0,PlayableTacticalOrderKind.Attack,new NavPoint(-1,0),101),maxHealth:100),
                new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-11,5),100,false,0,0,0,maxHealth:100),new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-11,-5),100,false,0,0,0,maxHealth:100)}
                .Concat(Enumerable.Range(101,3).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-1,(id-102)*2),100,false,0,0,Math.PI,maxHealth:100))).ToArray();
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),50)},Array.Empty<KnownBuilding>());
            var bullets=shots?Enumerable.Range(101,3).Select(id=>new PlayableProjectileSnapshot(id,id,1,new NavPoint(-5,id-102),faction:PlayableOwner.Enemy)).ToArray():Array.Empty<PlayableProjectileSnapshot>();
            var tower=new PlayableBuildingSnapshot(200,PlayableOwner.Player,PlayableBuildingKind.RepairTower,new NavPoint(-27,0),100,1,0,0,default,phase:ConstructionPhase.Ready);
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,Gen,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,new[]{tower},bullets,new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot()));
        }
        [Test]
        public void KnownLethalIncomingLossCannotOrderFutileRepairAndLeavesOrdinaryFire()
        {
            var before=View(0,100,false);var registry=new AiArmyRegistry(before.OwnerId,Gen,Ai,AiDifficulty.Veteran);Assert.True(registry.TryCreate(before,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out _));
            var micro=new AiCombatMicroPolicy();micro.Observe(before,P,Ai,AiDifficulty.Veteran,registry);var now=View(30,20,true);micro.Observe(now,P,Ai,AiDifficulty.Veteran,registry);
            Assert.AreEqual(3,micro.ObservedAttackCount(now,now.Entities.Single(u=>u.Id==1),P));
            Assert.False(micro.RepairEscapeAllowed(now,P,Ai,AiDifficulty.Veteran,now.Entities.Single(u=>u.Id==1),new[]{new NavPoint(-18.5,0)}));
            var plan=micro.Plan(now,P,Ai,AiDifficulty.Veteran,registry);Assert.True(plan==null||plan.Action.Kind!=PlayableCommandKind.Move,"Neither repair nor a longer withdrawal can outrun factual lethal loss");
            Assert.AreEqual(PlayableTacticalOrderKind.Attack,now.Entities.Single(u=>u.Id==1).CurrentOrder.Kind);
        }
        [Test]
        public void SafeTravelAndHealingDoNotExtrapolateTheOldDamageRate()
        {
            var before=View(0,30,false);var registry=new AiArmyRegistry(before.OwnerId,Gen,Ai,AiDifficulty.Veteran);Assert.True(registry.TryCreate(before,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out _));
            var micro=new AiCombatMicroPolicy();micro.Observe(before,P,Ai,AiDifficulty.Veteran,registry);var now=View(30,20,true);micro.Observe(now,P,Ai,AiDifficulty.Veteran,registry);
            var unit=now.Entities.Single(u=>u.Id==1);Assert.True(micro.RepairEscapeAllowed(now,P,Ai,AiDifficulty.Veteran,unit,new[]{new NavPoint(-18.5,0)}));
            Assert.Greater(AiCombatMicroPolicy.RepairSeconds(P,Ai,AiDifficulty.Veteran,unit,8.5),2,"The old loss rate gives two seconds of life, but only the escape part is dangerous");
            var plan=micro.Plan(now,P,Ai,AiDifficulty.Veteran,registry);Assert.NotNull(plan);Assert.AreEqual(AiCombatMicroPhase.Repair,plan.Phase);Assert.AreEqual(200,plan.Action.ParentId);
        }
    }
}

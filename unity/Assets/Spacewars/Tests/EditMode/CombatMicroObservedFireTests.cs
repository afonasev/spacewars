using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class CombatMicroObservedFireTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        [TestCase(false)]
        [TestCase(true)]
        public void WoundedWithdrawalUsesStrictThresholdAndKeepsFiringWhenRearIsBlocked(bool blocked)
        {
            const long generation=901;
            var profile=PlayableProfile.Default;
            var data=AiProfile.Initial.CopyData();
            data.fields.Single(f=>f.path=="difficulty.veteran.micro.withdrawHealth").value=.6;
            var ai=new AiProfile(data);
            var difficulty=AiDifficulty.Veteran;
            var vision=new PlayableVision(0,50,50,1,1);
            vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),50)},Array.Empty<KnownBuilding>());
            PlayableAiObservation View(long tick,int health,bool fire)
            {
                PlayableEntitySnapshot Tank(int id,PlayableOwner owner,double x,double z,int hp=100)=>
                    new PlayableEntitySnapshot(id,owner,PlayableEntityKind.Tank,new NavPoint(x,z),hp,false,0,0,0,maxHealth:100);
                var tanks=new[]{Tank(1,PlayableOwner.Player,-10,0,health),
                    Tank(2,PlayableOwner.Player,blocked?-11.45:-9,blocked?0:-3),Tank(3,PlayableOwner.Player,-9,3),
                    Tank(4,PlayableOwner.Enemy,-2,-2),Tank(5,PlayableOwner.Enemy,-2,2)};
                var shots=fire?new[]{4,5}.Select(id=>new PlayableProjectileSnapshot(id+100,id,1,
                    tanks[id-1].Position,faction:PlayableOwner.Enemy,directLaunchTick:tick-1)).ToArray():Array.Empty<PlayableProjectileSnapshot>();
                return PlayableAiObservation.From(new PlayableSnapshot(profile.ProfileId,profile.Revision,generation,7,tick,tick,
                    RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,tanks,Array.Empty<PlayableBuildingSnapshot>(),
                    shots,new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot()));
            }

            var first=View(10,100,false);
            var registry=new AiArmyRegistry(first.OwnerId,generation,ai,difficulty);
            Assert.True(registry.TryCreate(first,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out _));
            var micro=new AiCombatMicroPolicy();
            micro.Observe(first,profile,ai,difficulty,registry);
            var hit=View(20,60,true);
            micro.Observe(hit,profile,ai,difficulty,registry);
            Assert.AreNotEqual(AiCombatMicroPhase.Withdraw,micro.Plan(hit,profile,ai,difficulty,registry)?.Phase);
            var saved=WorldWire.Pack(micro.Write);
            var restored=WorldWire.Unpack(saved,reader=>AiCombatMicroPolicy.Read(reader,20,generation));
            var second=View(30,59,true);
            foreach(var state in new[]{micro,restored})
            {
                state.Observe(second,profile,ai,difficulty,registry);
                var plan=state.Plan(second,profile,ai,difficulty,registry);
                if(blocked){Assert.AreNotEqual(AiCombatMicroPhase.Withdraw,plan?.Phase);continue;}
                Assert.NotNull(plan);
                Assert.AreEqual(AiCombatMicroPhase.Withdraw,plan.Phase);
                CollectionAssert.AreEqual(new[]{1},plan.Action.CopyEntityIds());
                Assert.Less(plan.Action.Target.X,-10);
                Assert.True(AiCombatMicroPolicy.WithdrawalSpaceFree(second,profile,second.Entities[0],new[]{plan.Action.Target}));
            }
            CollectionAssert.AreEqual(WorldWire.Pack(micro.Write),WorldWire.Pack(restored.Write));
        }

        [Test]
        public void EnemyIntentStaysRedactedAndOnlyVisibleKnownActualShotsBecomeFocusFacts()
        {
            const long gen=901;var p=PlayableProfile.Default;var ai=AiProfile.Initial;var d=new PlayableDomain(p,gen,false);
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
            try
            {
                int victim=(int)Call("SpawnUnit",new NavPoint(-16,15),PlayableOwner.Player,PlayableEntityKind.Tank);
                var foes=new[]{new NavPoint(-7,12),new NavPoint(-6.5,15),new NavPoint(-7,18)}.Select(at=>(int)Call("SpawnUnit",at,PlayableOwner.Enemy,PlayableEntityKind.Tank)).ToArray();
                long sequence=1;
                foreach(int id in foes){string message;Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(gen,sequence++,"enemy-1",PlayableCommandKind.Attack,new[]{id},new NavPoint(-16,15),targetId:victim),out message),message);}
                PlayableSnapshot View()=>d.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player);
                PlayableSnapshot shotView=null;
                // This tiny source-control loop establishes the actual projection contract;
                // it is not an ordinary threaded gameplay witness.
                for(int i=0;i<60;i++)
                {
                    d.Step(1d/30);var view=View();
                    Assert.That(view.Entities.Where(u=>foes.Contains(u.Id)).Select(u=>u.TargetId),Is.All.Zero,"Enemy selected intent remains private");
                    if(view.Projectiles.Count(s=>s.Visible&&foes.Contains(s.OwnerId)&&s.TargetId==victim)>=3){shotView=view;break;}
                }
                Assert.NotNull(shotView,"Actual visible enemy shots are available without exposing enemy orders");
                var registry=new AiArmyRegistry("player-1",gen,ai,AiDifficulty.Veteran);var micro=new AiCombatMicroPolicy();
                var actual=PlayableAiObservation.From(shotView);micro.Observe(actual,p,ai,AiDifficulty.Veteran,registry);
                var own=actual.Entities.Single(u=>u.Id==victim);
                Assert.AreEqual(3,micro.ObservedAttackCount(actual,own,p));
                var first=actual.ObservedHostileShots.First();
                PlayableAiObservation At(long tick,PlayableProjectileSnapshot[] shots)=>PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,gen,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,shotView.Entities.ToArray(),shotView.Buildings.ToArray(),shots,new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:shotView.Vision));
                var invalid=new[]{new PlayableProjectileSnapshot(1001,0,victim,first.Position),new PlayableProjectileSnapshot(1002,foes[0],victim,first.Position,visible:false),new PlayableProjectileSnapshot(1003,foes[0],foes[1],first.Position),new PlayableProjectileSnapshot(1004,int.MaxValue,victim,first.Position)};
                var excluded=At(actual.Tick,invalid);Assert.IsEmpty(excluded.ObservedHostileShots);
                var noFacts=new AiCombatMicroPolicy();noFacts.Observe(excluded,p,ai,AiDifficulty.Veteran,registry);Assert.Zero(noFacts.ObservedAttackCount(excluded,own,p));
                byte[] Encode(AiCombatMicroPolicy value){using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))value.Write(writer);return stream.ToArray();}}
                var bytes=Encode(micro);AiCombatMicroPolicy restored;
                using(var reader=new BinaryReader(new MemoryStream(bytes)))restored=AiCombatMicroPolicy.Read(reader,actual.Tick,gen);
                CollectionAssert.AreEqual(bytes,Encode(restored));Assert.AreEqual(3,restored.ObservedAttackCount(actual,own,p));
                long expires=actual.Tick+AiProfile.SecondsToTicks(ai.DifficultyValue(AiDifficulty.Veteran,"micro.observedFireSeconds"),30)+1;
                var expired=At(expires,Array.Empty<PlayableProjectileSnapshot>());restored.Observe(expired,p,ai,AiDifficulty.Veteran,registry);Assert.Zero(restored.ObservedAttackCount(expired,own,p));
            }
            finally{d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}
        }
    }
}

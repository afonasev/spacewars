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

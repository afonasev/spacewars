using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Newtonsoft.Json;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class TacticalAttackRetirementTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static object Call(object d,string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
        static object Field(object o,string name)=>o.GetType().GetField(name,F).GetValue(o);

        [TestCase(PlayableEntityKind.Tank,false)]
        [TestCase(PlayableEntityKind.Shkval,false)]
        [TestCase(PlayableEntityKind.Explorer,true)]
        public void AirTargetCombatAndQueueLifecycleRestoreExactlyWithoutAdvancingTime(PlayableEntityKind kind,bool retainsAttack)
        {
            var d=new PlayableDomain(PlayableProfile.Default,1,false);PlayableDomain restored=null;
            try
            {
                int id=(int)Call(d,"SpawnUnit",new NavPoint(-14,-26),PlayableOwner.Player,kind);
                int enemy=(int)Call(d,"SpawnUnit",new NavPoint(-8,-26),PlayableOwner.Enemy,PlayableEntityKind.Hawk);
                var command=new PlayableCommand(1,1,"player-1",PlayableCommandKind.Attack,new[]{id},default,targetId:enemy);
                Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"Apply",command,null));
                var before=d.TacticalQueueProjection(id,PlayableOwner.Player);Assert.NotNull(before.Active);
                Assert.AreEqual(PlayableCommandKind.Attack,before.Active.Kind);
                long tick=d.Tick;
                Call(d,"AdvanceCombat",0d);Call(d,"RefreshTacticalQueues");
                var after=d.TacticalQueueProjection(id,PlayableOwner.Player);
                var units=(System.Collections.IDictionary)Field(d,"units");var unit=units[id];
                TestContext.WriteLine(JsonConvert.SerializeObject(new{kind,id,enemy,tick,AfterTick=d.Tick,Target=Field(unit,"Target"),CurrentOrder=Field(unit,"CurrentOrder"),before,after}));
                Assert.AreEqual(tick,d.Tick,"Component calls must not advance world time.");
                var bytes=(byte[])Call(d,"CaptureWorldBytes",7,"queue-test");
                restored=PlayableDomain.RestoreWorldBytes(bytes,PlayableProfile.Default,7,"queue-test");
                CollectionAssert.AreEqual(bytes,(byte[])Call(restored,"CaptureWorldBytes",7,"queue-test"));
                Assert.AreEqual(retainsAttack,after.Active!=null,"Combat and queue agree about whether this actor can retain Attack.");
                Assert.AreEqual(retainsAttack,Field(unit,"CurrentOrder")!=null);
                if(!retainsAttack){Assert.NotNull(after.Completion);Assert.AreEqual(enemy,after.Completion.TargetId);}
            }
            finally{restored?.DestroyOwnedNavigation();d.DestroyOwnedNavigation();}
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RetiringUnsupportedAttackPreservesAcceptedMoveAndRestoresExactly(bool pendingReplacement)
        {
            var d=new PlayableDomain(PlayableProfile.Default,1,false);PlayableDomain restored=null;
            try
            {
                int id=(int)Call(d,"SpawnUnit",new NavPoint(-14,-26),PlayableOwner.Player,PlayableEntityKind.Tank);
                int enemy=(int)Call(d,"SpawnUnit",new NavPoint(-8,-26),PlayableOwner.Enemy,PlayableEntityKind.Hawk);
                Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"Apply",new PlayableCommand(1,1,"player-1",PlayableCommandKind.Attack,new[]{id},default,targetId:enemy),null));
                var mode=pendingReplacement?PlayableOrderMode.Replace:PlayableOrderMode.Append;
                Assert.AreEqual(PlayableCommandStatus.Applied,Call(d,"Apply",new PlayableCommand(1,2,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-14,-20),mode:mode),null));
                var before=d.TacticalQueueProjection(id,PlayableOwner.Player);
                Assert.NotNull(before.Active);
                if(pendingReplacement){Assert.NotNull(before.Pending);Assert.Zero(before.Deferred.Count);}
                else{Assert.Null(before.Pending);Assert.AreEqual(1,before.Deferred.Count);}
                long tick=d.Tick;Call(d,"AdvanceCombat",0d);Call(d,"RefreshTacticalQueues");
                var after=d.TacticalQueueProjection(id,PlayableOwner.Player);
                Assert.AreEqual(tick,d.Tick);Assert.Null(after.Active);Assert.NotNull(after.Pending);
                Assert.AreEqual(2,after.Pending.CommandSequence);Assert.Zero(after.Deferred.Count);
                var bytes=(byte[])Call(d,"CaptureWorldBytes",7,"queue-test");
                restored=PlayableDomain.RestoreWorldBytes(bytes,PlayableProfile.Default,7,"queue-test");
                CollectionAssert.AreEqual(bytes,(byte[])Call(restored,"CaptureWorldBytes",7,"queue-test"));
                Assert.AreEqual(2,restored.TacticalQueueProjection(id,PlayableOwner.Player).Pending.CommandSequence);
            }
            finally{restored?.DestroyOwnedNavigation();d.DestroyOwnedNavigation();}
        }
    }
}

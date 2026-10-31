using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class DefaultCombatTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        PlayableDomain d;PlayableProfile p;long sequence;
        IDictionary Units=>(IDictionary)typeof(PlayableDomain).GetField("units",F).GetValue(d);
        object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
        object Unit(int id)=>Units[id];
        T Get<T>(int id,string field)=>(T)Unit(id).GetType().GetField(field,F).GetValue(Unit(id));
        void Set(int id,string field,object value)=>Unit(id).GetType().GetField(field,F).SetValue(Unit(id),value);
        object State(int id)=>Get<object>(id,"DefaultCombat");
        string Phase(int id){var s=State(id);return s==null?"None":s.GetType().GetField("Phase",F).GetValue(s).ToString();}
        double Deadline(int id)=>(double)State(id).GetType().GetField("Deadline",F).GetValue(State(id));
        NavUnit Actor(int id){object[] args={id,null};Assert.True((bool)Call("TryActor",args));return (NavUnit)args[1];}
        void Relocate(int id,NavPoint point)
        {
            var n=Actor(id);Assert.True(d.Navigation.Crowd.Locate(point,n.Radius,out var location));
            n.Position=point;n.Location=location;n.Goal=point;n.GoalLocation=location;
        }
        PlayableSnapshot View=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        int Spawn(PlayableOwner owner,PlayableEntityKind kind,NavPoint point)=>(int)Call("SpawnUnit",point,owner,kind);
        int Enemy(NavPoint point,PlayableEntityKind kind=PlayableEntityKind.Tank){int id=Spawn(PlayableOwner.Enemy,kind,point);Set(id,"Reload",1000d);return id;}
        void Tick(int count=1){for(int i=0;i<count;i++)d.Step(1d/30);}
        void Until(Func<bool> condition,int ticks=300){for(int i=0;i<ticks&&!condition();i++)Tick();Assert.True(condition(),"Fixed component horizon exhausted");}
        void Send(int id,PlayableCommandKind kind,NavPoint goal=default,int target=0)=>Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(1,++sequence,"player-1",kind,new[]{id},goal,targetId:target),out _));
        void Fresh(PlayableProfile profile)
        {
            if(d!=null){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}
            p=profile;d=new PlayableDomain(p,1,false);
            foreach(int id in Units.Keys.Cast<int>().ToArray()){d.Navigation.Remove(id);Units.Remove(id);}
        }
        [SetUp] public void Setup(){sequence=0;Fresh(PlayableProfile.Default);}
        [TearDown] public void Cleanup(){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();d=null;}

        [TestCase(PlayableEntityKind.Tank)] [TestCase(PlayableEntityKind.Explorer)] [TestCase(PlayableEntityKind.Shkval)] [TestCase(PlayableEntityKind.Hawk)]
        public void SharedVisionRevealsOtherwiseHiddenInRangeEnemyAndAllowsAutomaticFire(PlayableEntityKind kind)
        {
            var data=p.CopyData();data.tankVisionRange=3;data.explorerVision=3;data.shkvalVision=3;Fresh(PlayableProfile.Create(data));
            var start=new NavPoint(-20,26);double distance=Math.Min(8,PlayableUnitRules.Range(p,kind)-.5);var goal=new NavPoint(start.X+distance,26);
            int shooter=Spawn(PlayableOwner.Player,kind,start),enemy=Enemy(goal);
            Tick(30);Assert.Zero(Get<int>(shooter,"Target"),"Range alone cannot reveal a hidden enemy");Assert.False(View.Projectiles.Any(x=>x.OwnerId==shooter));
            int scout=Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(goal.X,23.5));Set(scout,"Reload",1000d);
            Until(()=>View.Projectiles.Any(x=>x.OwnerId==shooter));Assert.AreEqual(enemy,Get<int>(shooter,"Target"));
            Assert.Greater(distance,PlayableUnitRules.Vision(p,kind));Assert.AreEqual(start,Actor(shooter).Position,"Idle defense must not approach an in-range enemy");
        }
        [Test] public void BlockedNearestEnemyDoesNotMaskFartherClearTarget()
        {
            int shooter=Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-5,15));
            int blocked=Enemy(new NavPoint(4,15)),clear=Enemy(new NavPoint(-12,21));
            Assert.False((bool)Call("GroundFireClear",Actor(shooter).Position,Actor(blocked).Position,p.TankProjectileCollisionRadius));
            Until(()=>View.Projectiles.Any(x=>x.OwnerId==shooter));Assert.AreEqual(clear,Get<int>(shooter,"Target"));
        }
        [TestCase(PlayableEntityKind.Tank)] [TestCase(PlayableEntityKind.Explorer)] [TestCase(PlayableEntityKind.Shkval)] [TestCase(PlayableEntityKind.Hawk)]
        public void HoldFiresButNeverPursuesOrRepositions(PlayableEntityKind kind)
        {
            var start=new NavPoint(-20,26);int shooter=Spawn(PlayableOwner.Player,kind,start),enemy=Enemy(new NavPoint(-13,26));
            Send(shooter,PlayableCommandKind.Hold);Until(()=>View.Projectiles.Any(x=>x.OwnerId==shooter));
            Relocate(enemy,new NavPoint(-5,26));Tick(90);
            Assert.AreEqual(start,Actor(shooter).Position);Assert.True(Actor(shooter).Held);Assert.AreEqual("Idle",Phase(shooter));
        }
        void PursuingHawk(out int shooter,out int enemy)
        {
            shooter=Spawn(PlayableOwner.Player,PlayableEntityKind.Hawk,new NavPoint(-20,26));enemy=Enemy(new NavPoint(-13,26));
            int id=shooter;Until(()=>View.Projectiles.Any(x=>x.OwnerId==id));
            int scout=Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-4,22));Set(scout,"Reload",1000d);
            Relocate(enemy,new NavPoint(-5,26));Tick();Assert.AreEqual("Pursuit",Phase(shooter));
        }
        [Test] public void LosingSharedVisibilityDuringPursuitStartsReturnImmediately()
        {
            PursuingHawk(out int shooter,out int enemy);double deadline=Deadline(shooter);
            Relocate(enemy,new NavPoint(20,26));Tick();Assert.AreEqual("Return",Phase(shooter));Assert.Zero(Get<int>(shooter,"Target"));Assert.AreEqual(deadline,Deadline(shooter));
        }
        [Test] public void DestroyedPursuitTargetStartsReturnAndHoldCancelsIt()
        {
            PursuingHawk(out int shooter,out int enemy);Call("Damage",enemy,10000);Tick();Assert.AreEqual("Return",Phase(shooter));
            Send(shooter,PlayableCommandKind.Hold);var held=Actor(shooter).Position;Tick(90);Assert.AreEqual("Idle",Phase(shooter));Assert.AreEqual(held,Actor(shooter).Position);
        }
        [Test] public void NewMoveCancelsPursuitAndDoesNotReturnToOldAnchor()
        {
            PursuingHawk(out int shooter,out _);Send(shooter,PlayableCommandKind.Move,new NavPoint(-26,26));
            Assert.IsNull(State(shooter));Until(()=>!Actor(shooter).Moving);Tick(90);Assert.That(Actor(shooter).Position.X,Is.LessThan(-25));Assert.AreNotEqual("Return",Phase(shooter));
        }
        [TestCase(false)] [TestCase(true)]
        public void CurrentSaveExactlyContinuesPursuitAndReturnWithoutRenewingDeadline(bool returning)
        {
            PursuingHawk(out int shooter,out int enemy);
            if(returning){Relocate(enemy,new NavPoint(20,26));Tick();Assert.AreEqual("Return",Phase(shooter));}
            double deadline=Deadline(shooter);var bytes=d.CaptureWorldBytes(7,"default-combat");
            var restored=PlayableDomain.RestoreWorldBytes(bytes,p,7,"default-combat");
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"default-combat"));
                for(int i=0;i<120;i++){d.Step(1d/30);restored.Step(1d/30);}
                CollectionAssert.AreEqual(d.CaptureWorldBytes(7,"default-combat"),restored.CaptureWorldBytes(7,"default-combat"));
                Assert.AreNotEqual("Pursuit",Phase(shooter));Assert.Greater(deadline,0);
            }
            finally{restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        [Test] public void PursuitParameterHasMetadataRangeStepAndSaveBinding()
        {
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="combat.default.pursuitSeconds");Assert.AreEqual(2,p.DefaultCombatPursuitSeconds);Assert.True(NativeBalanceFields.Editable(field));Assert.That(field.Group,Does.Contain("unit-default-combat-v1"));
            var data=p.CopyData();field.Write(data,3);var next=PlayableProfile.Create(data);Assert.AreEqual(3,next.DefaultCombatPursuitSeconds);
            var bytes=d.CaptureWorldBytes(7,"default-profile");Assert.Throws<ArgumentException>(()=>PlayableDomain.RestoreWorldBytes(bytes,next,7,"default-profile"));
            data.defaultCombatPursuitSeconds=-1;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
            data.defaultCombatPursuitSeconds=2.05;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
        [Test] public void RejectedPreparedMovePreservesDefaultStateAndAcceptedHoldClearsPursuit()
        {
            int shooter=Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-20,26)),enemy=Enemy(new NavPoint(-13,26));
            Until(()=>View.Projectiles.Any(x=>x.OwnerId==shooter));int scout=Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-4,22));Set(scout,"Reload",1000d);
            Relocate(enemy,new NavPoint(-5,26));Tick();Assert.AreEqual("Pursuit",Phase(shooter));var state=State(shooter);double deadline=Deadline(shooter);
            d.AfterTacticalPreparationForQa=_=>Set(shooter,"Health",Get<double>(shooter,"Health")-1);
            Assert.AreEqual(PlayableCommandStatus.Rejected,d.Apply(new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.Move,new[]{shooter},new NavPoint(-25,26)),out _));
            d.AfterTacticalPreparationForQa=null;Assert.AreSame(state,State(shooter));Assert.AreEqual("Pursuit",Phase(shooter));Assert.AreEqual(deadline,Deadline(shooter));
            Send(shooter,PlayableCommandKind.Hold);Assert.IsNull(State(shooter));Tick();Assert.True(Actor(shooter).Held);Assert.AreEqual("Idle",Phase(shooter));
        }
        [Test] public void GroundPursuitCurrentSavePreservesPendingNavigationAndExactContinuation()
        {
            int shooter=Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-20,26)),enemy=Enemy(new NavPoint(-13,26));
            Until(()=>View.Projectiles.Any(x=>x.OwnerId==shooter));int scout=Spawn(PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-4,22));Set(scout,"Reload",1000d);
            Relocate(enemy,new NavPoint(-5,26));Tick();Assert.AreEqual("Pursuit",Phase(shooter));double deadline=Deadline(shooter);
            var bytes=d.CaptureWorldBytes(7,"ground-default");var restored=PlayableDomain.RestoreWorldBytes(bytes,p,7,"ground-default");
            var originalKernel=new CertifiedRouteKernel();var restoredKernel=new CertifiedRouteKernel();
            try
            {
                CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"ground-default"));
                for(int i=0;i<180;i++)
                {
                    originalKernel.Pump(new PlayableRouteBinding(d.Geometry,p,d.Navigation.Admission,d.Navigation.RoutePort),4);d.Step(1d/30);
                    restoredKernel.Pump(new PlayableRouteBinding(restored.Geometry,p,restored.Navigation.Admission,restored.Navigation.RoutePort),4);restored.Step(1d/30);
                }
                CollectionAssert.AreEqual(d.CaptureWorldBytes(7,"ground-default"),restored.CaptureWorldBytes(7,"ground-default"));
                Assert.AreNotEqual("Pursuit",Phase(shooter));Assert.Greater(deadline,0);
            }
            finally{originalKernel.Clear();restoredKernel.Clear();restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
    }
}

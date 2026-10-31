using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class ProducerEgressTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        PlayableDomain d;PlayableProfile p;OfflineMatchConfiguration config;int producer;long sequence;
        object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
        PlayableSnapshot View=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        PlayableBuildingSnapshot Producer=>View.Buildings.Single(b=>b.Id==producer);
        IDictionary Units=>(IDictionary)typeof(PlayableDomain).GetField("units",F).GetValue(d);
        NavUnit Actor(int id)
        {
            if(d.Navigation.Crowd.TryGet(id,out var actor))return actor;
            var u=Units[id];var air=u.GetType().GetField("Air").GetValue(u);return (NavUnit)air.GetType().GetField("Actor").GetValue(air);
        }
        void Setup(PlayableBuildingKind building)
        {
            config=OfflineParticipantAuthorityTests.Config(3,true,seed:7);p=config.Profile;d=new PlayableDomain(p,1,config);
            var hq=View.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters);
            Call("AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",hq.SiteId,1,building,hq.Id,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);producer=View.Buildings.Single(b=>b.Kind==building).Id;
        }
        [TearDown] public void Cleanup(){if(d!=null){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();d=null;}}
        void Advance(int ticks=1)
        {
            for(int i=0;i<ticks;i++){d.Navigation.Crowd.Step(1d/30);Call("AdvanceProduction",1d/30);Call("AdvanceHawks",1d/30);Call("AdvanceBuildingLifecycle",1d/30);}
        }
        int Spawn(NavPoint position,PlayableOwner owner,PlayableEntityKind kind)
        {
            int id=(int)Call("SpawnUnit",position,owner,kind);Actor(id).Held=true;Actor(id).Outcome=NavigationOutcome.Held;return id;
        }
        void Order(PlayableEntityKind kind)
        {
            var command=kind==PlayableEntityKind.Hawk?PlayableCommandKind.QueueHawk:kind==PlayableEntityKind.Explorer?PlayableCommandKind.QueueExplorer:kind==PlayableEntityKind.Shkval?PlayableCommandKind.QueueShkval:PlayableCommandKind.QueueTank;
            Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(1,++sequence,config.Roster[0].Id,command,new[]{producer}),out _));
        }
        void Safe()
        {
            var ground=d.Navigation.Crowd.Units.ToArray();
            for(int i=0;i<ground.Length;i++)
            {
                Assert.True(d.Geometry.IsFree(ground[i].Position,ground[i].Radius));
                for(int j=i+1;j<ground.Length;j++)Assert.GreaterOrEqual(Distance(ground[i].Position,ground[j].Position),ground[i].Radius+ground[j].Radius-1e-8);
            }
            var air=View.Entities.Where(e=>e.Kind==PlayableEntityKind.Hawk).ToArray();
            for(int i=0;i<air.Length;i++)for(int j=i+1;j<air.Length;j++)Assert.GreaterOrEqual(Distance(air[i].Position,air[j].Position),2*p.HawkRadius+p.HawkSeparation-1e-8);
        }
        [TestCase(PlayableEntityKind.Tank,PlayableOwner.Player)]
        [TestCase(PlayableEntityKind.Tank,PlayableOwner.Third)]
        [TestCase(PlayableEntityKind.Tank,PlayableOwner.Enemy)]
        [TestCase(PlayableEntityKind.Explorer,PlayableOwner.Enemy)]
        [TestCase(PlayableEntityKind.Shkval,PlayableOwner.Enemy)]
        [TestCase(PlayableEntityKind.Hawk,PlayableOwner.Player)]
        [TestCase(PlayableEntityKind.Hawk,PlayableOwner.Third)]
        [TestCase(PlayableEntityKind.Hawk,PlayableOwner.Enemy)]
        public void AllOwnersAndTypedReleasePhysicallyClearHoldWithoutDamage(PlayableEntityKind kind,PlayableOwner blockerOwner)
        {
            Setup(kind==PlayableEntityKind.Hawk?PlayableBuildingKind.Hangar:PlayableBuildingKind.Factory);
            var origin=kind==PlayableEntityKind.Hawk?Producer.Position:FactoryReleaseRules.Exit(p,Producer.Position,Producer.Heading,kind);
            int blocker=Spawn(origin,blockerOwner,kind);double health=View.Entities.Single(e=>e.Id==blocker).Health;
            Assert.AreEqual(Actor(blocker).Team,blockerOwner==PlayableOwner.Enemy?7:3,"Real allied/enemy team binding");
            Order(kind);int initial=View.Entities.Count;bool moved=false;
            for(int i=0;i<1800&&View.Entities.Count==initial;i++){Advance();Safe();moved|=!Actor(blocker).Position.Equals(origin);}
            Assert.AreEqual(initial+1,View.Entities.Count);Assert.True(moved);Assert.True(Actor(blocker).Held);Assert.False(Actor(blocker).Moving);
            Assert.AreEqual(health,View.Entities.Single(e=>e.Id==blocker).Health);Assert.IsEmpty(Producer.PrivateState.Orders);
        }
        [TestCase(false)][TestCase(true)] public void DenseMixedOwnerChainClearsWithoutOverlapAndHoldStaysAtNewSpot(bool air)
        {
            Setup(air?PlayableBuildingKind.Hangar:PlayableBuildingKind.Factory);
            var kind=air?PlayableEntityKind.Hawk:PlayableEntityKind.Tank;
            var origin=air?Producer.Position:FactoryReleaseRules.Exit(p,Producer.Position,Producer.Heading,kind);
            double spacing=air?2*p.HawkRadius+p.HawkSeparation:2*p.TankCollisionRadius;
            var ids=Enumerable.Range(0,4).Select(i=>Spawn(new NavPoint(origin.X,origin.Z-i*spacing),(PlayableOwner)(i%3),kind)).ToArray();
            var positions=ids.Select(id=>Actor(id).Position).ToArray();Order(kind);int initial=View.Entities.Count;
            for(int i=0;i<1800&&View.Entities.Count==initial;i++){Advance();Safe();}
            Assert.AreEqual(initial+1,View.Entities.Count);Assert.True(ids.Any(id=>!Actor(id).Position.Equals(positions[Array.IndexOf(ids,id)])));
            Assert.True(ids.All(id=>Actor(id).Held));Assert.True(ids.All(id=>View.Entities.Any(e=>e.Id==id)));
            var cleared=ids.Select(id=>Actor(id).Position).ToArray();Advance(120);Safe();CollectionAssert.AreEqual(cleared,ids.Select(id=>Actor(id).Position).ToArray());
        }
        [Test] public void GroundPressureRoundTripsExactWorldDuringPhysicalYield()
        {
            Setup(PlayableBuildingKind.Factory);var exit=FactoryReleaseRules.Exit(p,Producer.Position,Producer.Heading,PlayableEntityKind.Tank);
            int blocker=Spawn(exit,PlayableOwner.Enemy,PlayableEntityKind.Tank);Order(PlayableEntityKind.Tank);
            for(int i=0;i<1500&&Actor(blocker).Position.Equals(exit);i++)Advance();Assert.AreNotEqual(exit,Actor(blocker).Position);
            var original=d;var restored=PlayableDomain.RestoreConfiguredWorldBytes(d.CaptureWorldBytes(7,"producer-egress"),p,7,"producer-egress",config);
            try{for(int i=0;i<180;i++){d=original;Advance();d=restored;Advance();CollectionAssert.AreEqual(original.CaptureWorldBytes(7,"producer-egress"),restored.CaptureWorldBytes(7,"producer-egress"));}}
            finally{d=original;restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
        }
        [Test] public void FactoryGapIsGovernedAndBoundToCurrentWorldIdentity()
        {
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="production.factoryAssemblyGap");
            var data=PlayableProfile.Default.CopyData();Assert.True(NativeBalanceFields.Editable(field));
            Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(field,data,field.Minimum-field.Step));
            NativeBalanceFields.Write(field,data,data.factoryAssemblyGap+field.Step);
            var changed=PlayableProfile.Create(data);
            Assert.AreEqual(data.factoryAssemblyGap,changed.FactoryAssemblyGap);
            CollectionAssert.AreNotEqual(WorldWire.Binding(PlayableProfile.Default),WorldWire.Binding(changed));
        }
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
    }
}

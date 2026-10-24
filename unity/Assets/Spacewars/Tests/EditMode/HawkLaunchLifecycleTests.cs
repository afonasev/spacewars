using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class HawkLaunchLifecycleTests
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;PlayableDomain d;PlayableProfile p;int hangar;long sequence;
        object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,Hidden).Invoke(d,args);
        IDictionary Rows(string name)=>(IDictionary)typeof(PlayableDomain).GetField(name,Hidden).GetValue(d);
        object Building=>Rows("buildings")[hangar];
        object Air(int id){var u=Rows("units")[id];return u.GetType().GetField("Air").GetValue(u);}
        NavUnit Actor(int id)=>(NavUnit)Air(id).GetType().GetField("Actor").GetValue(Air(id));
        PlayableSnapshot View=>d.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        PlayableBuildingSnapshot Hangar=>View.Buildings.Single(b=>b.Id==hangar);
        [SetUp] public void Setup(){p=PlayableProfile.Default;d=new PlayableDomain(p,1,false);Call("AddCredits",PlayableOwner.Player,10000d);Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,1,PlayableBuildingKind.Hangar,1,PlayableOwner.Player,null));Call("AdvanceFoundations");Call("AdvanceBuildings",100d);hangar=View.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Hangar).Id;}
        [TearDown] public void Cleanup(){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}
        void Advance(int ticks=1){for(int i=0;i<ticks;i++){Call("AdvanceProduction",1d/30);Call("AdvanceHawks",1d/30);Call("AdvanceBuildingLifecycle",1d/30);}}
        void Order()=>Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.QueueHawk,new[]{hangar}),out _));
        int Spawn(NavPoint point,PlayableOwner owner)=>(int)Call("SpawnUnit",point,owner,PlayableEntityKind.Hawk);
        void UntilPhase(int phase){for(int i=0;i<1800&&Hangar.LaunchPhase!=phase;i++)Advance();Assert.AreEqual(phase,Hangar.LaunchPhase);}
        [TestCase(false,3)][TestCase(true,3)][TestCase(false,4)][TestCase(true,4)] public void ProducerRemovalCancelsOnlyAircraftBeforeIndependentLiftoff(bool sale,int phase)
        {
            Order();UntilPhase(phase);int air=View.Entities.Where(e=>e.Kind==PlayableEntityKind.Hawk).Select(e=>e.Id).SingleOrDefault();
            if(sale)Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.SellBuilding,new[]{hangar}),out _));else Call("Damage",hangar,100000);
            Advance(300);Assert.False(View.Buildings.Any(b=>b.Id==hangar));if(phase==3)Assert.False(View.Entities.Any(e=>e.Kind==PlayableEntityKind.Hawk));else{Assert.True(View.Entities.Any(e=>e.Id==air));Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.Move,new[]{air},new NavPoint(-10,15)),out _));var before=Actor(air).Position;Advance(60);Assert.AreNotEqual(before,Actor(air).Position);}
        }
        [Test] public void AllOwnerHoldChainYieldsSafelyAndRoundTripsWhileYielding()
        {
            var center=Hangar.Position;double spacing=2*p.HawkRadius+p.HawkSeparation;int[] ids=Enumerable.Range(0,3).Select(i=>Spawn(new NavPoint(center.X+i*spacing,center.Z),i==1?PlayableOwner.Enemy:PlayableOwner.Player)).ToArray();foreach(int id in ids){Actor(id).Held=true;Actor(id).Outcome=NavigationOutcome.Held;}
            var shortAir=Air(ids[0]);shortAir.GetType().GetField("Short").SetValue(shortAir,true);shortAir.GetType().GetField("ShortRemaining").SetValue(shortAir,0d);Order();bool captured=false,born=false;
            for(int tick=0;tick<1800;tick++)
            {
                Advance();var air=View.Entities.Where(e=>e.Kind==PlayableEntityKind.Hawk).ToArray();for(int i=0;i<air.Length;i++)for(int j=i+1;j<air.Length;j++)Assert.GreaterOrEqual(Distance(air[i].Position,air[j].Position),spacing-1e-8);
                if(!captured&&ids.Any(id=>(bool)Air(id).GetType().GetField("Yielding").GetValue(Air(id))))
                {
                    captured=true;var bytes=d.CaptureWorldBytes(7,"hawk-exit");var restored=PlayableDomain.RestoreWorldBytes(bytes,p,7,"hawk-exit");var original=d;
                    try{for(int i=0;i<30;i++){d=original;Advance();d=restored;Advance();}CollectionAssert.AreEqual(original.CaptureWorldBytes(7,"hawk-exit"),restored.CaptureWorldBytes(7,"hawk-exit"));}
                    finally{d=original;restored.Navigation.ReleaseCommittedHostRoot();restored.DestroyOwnedNavigation();}
                }
                born=air.Length==4;if(born&&Hangar.LaunchPhase==0)break;
            }
            Assert.True(captured&&born);foreach(int id in ids){Assert.True(Actor(id).Held);Assert.False(Actor(id).Moving);Assert.Greater(Distance(Actor(id).Position,center),spacing-1e-7);}var positions=ids.Select(id=>Actor(id).Position).ToArray();Advance(120);CollectionAssert.AreEqual(positions,ids.Select(id=>Actor(id).Position).ToArray(),"Hold remains at the cleared spot");
        }
        [Test] public void NoRoomReturnsWithoutPartialYieldOrSpawnWithinFiniteSearch()
        {
            d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();var data=p.CopyData();data.hawkRadius=5;p=PlayableProfile.Create(data);d=new PlayableDomain(p,1,false);
            Call("AddBuilding",PlayableOwner.Player,PlayableBuildingKind.Hangar,default(NavPoint),true);hangar=View.Buildings.Single(b=>b.Kind==PlayableBuildingKind.Hangar).Id;double spacing=2*p.HawkRadius+p.HawkSeparation;for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)Spawn(new NavPoint(x*spacing,z*spacing),PlayableOwner.Player);
            var before=View.Entities.Where(e=>e.Kind==PlayableEntityKind.Hawk).Select(e=>e.Position).ToArray();var timer=Stopwatch.StartNew();Assert.False((bool)Call("ReserveAirExit",Building));Assert.Less(timer.Elapsed.TotalSeconds,1);Assert.False(Rows("units").Values.Cast<object>().Select(u=>u.GetType().GetField("Air").GetValue(u)).Where(a=>a!=null).Any(a=>(bool)a.GetType().GetField("Yielding").GetValue(a)));CollectionAssert.AreEqual(before,View.Entities.Where(e=>e.Kind==PlayableEntityKind.Hawk).Select(e=>e.Position).ToArray());Call("AddCredits",PlayableOwner.Player,10000d);Order();Call("AdvanceProduction",p.HawkProductionSeconds+1);Assert.AreEqual(0,Hangar.LaunchPhase);Assert.AreEqual(1,Hangar.PrivateState.Orders.Count);Assert.AreEqual(0,Hangar.PrivateState.Orders[0].Remaining);Assert.AreEqual(before.Length,View.Entities.Count(e=>e.Kind==PlayableEntityKind.Hawk));
        }
        [Test] public void NextQueueHeadWaitsForRoofClosure()
        {
            Order();Order();bool first=false,closed=false,second=false;for(int tick=0;tick<2400;tick++){Advance();int count=View.Entities.Count(e=>e.Kind==PlayableEntityKind.Hawk);if(count==1)first=true;if(first&&Hangar.LaunchPhase==0)closed=true;if(count==2){second=true;Assert.True(closed);break;}}Assert.True(first&&closed&&second);
        }
        static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
    }
}

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class PadRallyTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        PlayableDomain d;PlayableProfile p;long sequence;
        object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
        PlayableSnapshot View()=>d.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        IDictionary Buildings=>(IDictionary)typeof(PlayableDomain).GetField("buildings",F).GetValue(d);
        [SetUp] public void Create(){p=PlayableProfile.Default;d=new PlayableDomain(p,91,false);Call("AddCredits",PlayableOwner.Player,100000d);}
        [TearDown] public void Cleanup(){d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}
        int Build(int slot,PlayableBuildingKind kind)
        {
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,slot,kind,1,PlayableOwner.Player,null));
            Call("AdvanceFoundations");Call("AdvanceBuildings",100d);
            return View().Buildings.Single(b=>b.SiteId==1&&b.SlotId==slot).Id;
        }
        void SetRally(int id,NavPoint point)
        {
            var status=d.Apply(new PlayableCommand(91,++sequence,"player-1",PlayableCommandKind.SetRally,new[]{id},point),out _);
            Assert.That(status,Is.EqualTo(PlayableCommandStatus.Accepted).Or.EqualTo(PlayableCommandStatus.Applied));
            for(int i=0;i<100&&View().Buildings.Single(b=>b.Id==id).PrivateState.PendingRally.HasValue;i++)
            {
                Call("AdvanceRally");while(d.Navigation.Requests.TryDequeue(out var request))
                    d.Navigation.Answers.TryEnqueue(new NavigationAnswer(request,new SharedFlowRouter(request.Geometry,request.Profile).FindPath(request.Start,request.Goal)));
                d.Navigation.ApplyResults();Call("AdvanceRally");
            }
            var b=View().Buildings.Single(x=>x.Id==id);Assert.True(b.PrivateState.HasRally);Assert.AreEqual(point,b.Rally);
        }
        [TestCase(false,PlayableBuildingKind.Factory)] [TestCase(true,PlayableBuildingKind.Factory)]
        [TestCase(false,PlayableBuildingKind.Hangar)] [TestCase(true,PlayableBuildingKind.Hangar)]
        public void AcceptedPadRallySurvivesSaleOrDestructionEmptySaveAndDifferentProducer(bool destroyed,PlayableBuildingKind replacement)
        {
            int first=Build(2,replacement==PlayableBuildingKind.Factory?PlayableBuildingKind.Hangar:PlayableBuildingKind.Factory);
            var point=new NavPoint(-8,10);SetRally(first,point);
            if(destroyed)Call("Damage",first,100000);
            else{Assert.AreEqual(PlayableCommandStatus.Applied,Call("SellBuilding",first,PlayableOwner.Player,null));Call("AdvanceBuildingLifecycle",100d);}
            for(int tick=0;tick<300;tick++)d.Step(1d/30);
            Assert.False(View().Buildings.Any(b=>b.Id==first));
            var bytes=d.CaptureWorldBytes(7,"pad-rally");
            var restored=PlayableDomain.RestoreWorldBytes(bytes,p,7,"pad-rally");
            CollectionAssert.AreEqual(bytes,restored.CaptureWorldBytes(7,"pad-rally"));
            d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();d=restored;
            int next=Build(2,replacement);var b=View().Buildings.Single(x=>x.Id==next);
            Assert.True(b.PrivateState.HasRally);Assert.AreEqual(point,b.Rally);
            int other=Build(1,PlayableBuildingKind.Hangar);Assert.False(View().Buildings.Single(x=>x.Id==other).PrivateState.HasRally,"Other pad must remain independent");
            // Invoke the same production completion boundary; ordinary release/combat is covered by ProducerEgressThreadedTests.
            Call("SpawnProduced",new NavPoint(-10,10),(NavPoint?)b.Rally,PlayableOwner.Player,replacement==PlayableBuildingKind.Hangar?PlayableEntityKind.Hawk:PlayableEntityKind.Tank);
            var actor=View().Entities.OrderByDescending(x=>x.Id).First();Assert.AreEqual(PlayableTacticalOrderKind.AttackMove,actor.CurrentOrder.Kind);Assert.AreEqual(point,actor.CurrentOrder.Destination);
        }
        [Test] public void PendingRequestCannotReplaceAcceptedPadPointAfterSale()
        {
            int id=Build(2,PlayableBuildingKind.Factory);var accepted=new NavPoint(-8,10);SetRally(id,accepted);
            Assert.AreEqual(PlayableCommandStatus.Accepted,d.Apply(new PlayableCommand(91,++sequence,"player-1",PlayableCommandKind.SetRally,new[]{id},new NavPoint(10,10)),out _));
            // Cancel while the real request is outstanding. Do not forge a second answer from retired storage.
            Call("AdvanceRally");Assert.Greater(d.Navigation.Requests.Count,0);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("SellBuilding",id,PlayableOwner.Player,null));Call("AdvanceBuildingLifecycle",100d);Call("AdvanceRally");
            int next=Build(2,PlayableBuildingKind.Hangar);Assert.AreEqual(accepted,View().Buildings.Single(b=>b.Id==next).Rally);
        }
    }
}

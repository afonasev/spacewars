using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class ScienceConstructionTests
    {
        PlayableDomain domain;long sequence;
        object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,args);
        PlayableSnapshot View()=>domain.Snapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
        [SetUp] public void Setup(){domain=new PlayableDomain(PlayableProfile.Default,813,false);sequence=0;Call("AddCredits",PlayableOwner.Player,10000d);Call("AddCredits",PlayableOwner.Enemy,10000d);}
        [TearDown] public void Cleanup(){domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
        PlayableCommandStatus Build(PlayableBuildingKind kind,int slot,PlayableOwner owner=PlayableOwner.Player)=>(PlayableCommandStatus)Call("BuildAt",owner==PlayableOwner.Player?1:2,slot,kind,owner==PlayableOwner.Player?1:2,owner,null);
        void Ready(){Call("AdvanceFoundations");Call("AdvanceBuildings",100d);}
        void RejectScience()
        {
            double money=(double)Call("Balance",PlayableOwner.Player);int count=View().Buildings.Count;
            Assert.AreEqual(PlayableCommandStatus.Rejected,Build(PlayableBuildingKind.ScientificCenter,3));
            Assert.AreEqual(money,Call("Balance",PlayableOwner.Player));Assert.AreEqual(count,View().Buildings.Count);
        }
        [TestCase(PlayableBuildingKind.Factory)] [TestCase(PlayableBuildingKind.Hangar)]
        public void OnlyOwnReadyLiveNonSellingProducerUnlocksNewCenters(PlayableBuildingKind kind)
        {
            RejectScience();Assert.AreEqual(PlayableCommandStatus.Applied,Build(kind,1,PlayableOwner.Enemy));Ready();RejectScience();
            Assert.AreEqual(PlayableCommandStatus.Applied,Build(kind,1));RejectScience();Ready();
            int producer=View().Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==kind).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Build(PlayableBuildingKind.ScientificCenter,2));Ready();
            int center=View().Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.ScientificCenter).Id;
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(813,++sequence,"player-1",PlayableCommandKind.SellBuilding,new[]{producer}),null));RejectScience();
            Call("Damage",producer,100000);RejectScience();
            Assert.AreEqual(ConstructionPhase.Ready,View().Buildings.Single(b=>b.Id==center).Phase);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(813,++sequence,"player-1",PlayableCommandKind.QueueResearch,new[]{center},researchKind:PlayableResearchKind.ImprovedWeaponSystems),null));
            Call("AdvanceResearch",100d);Assert.True(View().OwnerResearch.Single(r=>r.Kind==PlayableResearchKind.ImprovedWeaponSystems).Complete);
        }
    }
}

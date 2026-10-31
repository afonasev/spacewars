using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class ContactSpatialPhaseTests
    {
        const BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance;
        PlayableDomain domain;
        object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(domain,args);
        IDictionary Rows(string name)=>(IDictionary)typeof(PlayableDomain).GetField(name,F).GetValue(domain);
        int Spawn(NavPoint p,PlayableOwner owner,PlayableEntityKind kind)=>(int)Call("SpawnUnit",p,owner,kind);
        [SetUp] public void Start(){ContactPhaseQa.Reset();domain=new PlayableDomain(PlayableProfile.Default,1,false);}
        [TearDown] public void End(){ContactPhaseQa.Reset();var b=domain.Navigation.PhysicalHostBudget;domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();Assert.Zero(b.PhysicalBytes);Assert.Zero(b.HeldBytes);Assert.Zero(b.ActiveScopes);}
        void Compare(PlayableOwner owner,NavPoint p,double range,double radius)
        {
            var near=new List<int>();var terrain=new List<int>();
            ContactPhaseQa.RangeCandidate=near.Add;ContactPhaseQa.TerrainCandidate=terrain.Add;
            ContactPhaseQa.FullScanDetection=true;bool expected=(bool)Call("FormationContact",owner,p,range,radius);var a=near.ToArray();var b=terrain.ToArray();near.Clear();terrain.Clear();
            ContactPhaseQa.FullScanDetection=false;bool actual=(bool)Call("FormationContact",owner,p,range,radius);
            Assert.AreEqual(expected,actual);CollectionAssert.AreEqual(a,near,"Exact range-pass candidate order");CollectionAssert.AreEqual(b,terrain,"Exact terrain-call and short-circuit order");
            ContactPhaseQa.RangeCandidate=ContactPhaseQa.TerrainCandidate=null;
        }
        [Test] public void ExactPredicateOrderBoundariesAirMissingBodiesAndPendingBuildings()
        {
            Spawn(new NavPoint(-20,-16),PlayableOwner.Player,PlayableEntityKind.Explorer);
            int ground=Spawn(new NavPoint(-16,-16),PlayableOwner.Enemy,PlayableEntityKind.Tank);
            int air=Spawn(new NavPoint(-14,-16),PlayableOwner.Enemy,PlayableEntityKind.Hawk);
            Assert.NotNull(Rows("units")[air].GetType().GetField("Air").GetValue(Rows("units")[air]));
            Call("AddBuilding",PlayableOwner.Enemy,PlayableBuildingKind.Factory,new NavPoint(-12,-16),true);
            var building=Rows("buildings").Values.Cast<object>().Last();Call("RefreshVision");
            foreach(var phase in new[]{ConstructionPhase.Pending,ConstructionPhase.Ready}){
                building.GetType().GetField("Phase").SetValue(building,phase);
                foreach(var point in new[]{new NavPoint(-20,-16),new NavPoint(-16,-16),new NavPoint(0,0),new NavPoint(1e200,0),new NavPoint(double.NaN,0)})
                foreach(double range in new[]{-1d,0d,3.999999999999,4d,4.000000000001,16d,1e12,double.NaN})
                foreach(var owner in new[]{PlayableOwner.Player,PlayableOwner.Enemy})Compare(owner,point,range,PlayableProfile.Default.TankProjectileCollisionRadius);
            }
            domain.Navigation.Crowd.Remove(ground);Compare(PlayableOwner.Player,new NavPoint(-20,-16),16,0);
            Call("Damage",air,10000);Call("RefreshVision");Compare(PlayableOwner.Player,new NavPoint(-20,-16),16,0);
        }
        [Test] public void ConservativeIndexFallbackKeepsOriginalOrdinalAndFreshClear()
        {
            var index=new PlayableTargetSpatialIndex();var list=new List<PlayableTargetSpatialIndex.Entry>();
            index.Add(70,new NavPoint(16,0),false);index.Add(3,new NavPoint(-16,0),false);index.Add(1,new NavPoint(0,0),true);
            foreach(double range in new[]{0d,16d,16.00000000001,1e9,double.PositiveInfinity}){
                index.Query(default(NavPoint),range,list);list.Sort();var ids=list.Select(e=>e.Id).ToArray();
                if(range>=16)CollectionAssert.AreEqual(new[]{70,3,1},ids);else CollectionAssert.Contains(ids,1);
                CollectionAssert.AreEqual(list.Select(e=>e.Ordinal).OrderBy(x=>x),list.Select(e=>e.Ordinal));
            }
            index.Add(99,new NavPoint(1e200,0),true);index.Query(default(NavPoint),0,list);list.Sort();CollectionAssert.AreEqual(new[]{70,3,1,99},list.Select(e=>e.Id));
            index.Clear();index.Add(8,new NavPoint(0,0),false);index.Query(default(NavPoint),0,list);Assert.AreEqual(8,list.Single().Id);
            index.Query(default(NavPoint),double.NaN,list);Assert.IsEmpty(list);index.Query(default(NavPoint),-1,list);Assert.IsEmpty(list);
        }
    }
}

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationAtomicCrowdMembershipTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly NavGeometry Geometry=new NavGeometry(30,Array.Empty<NavObstacle>(),1);
        private static NavigationSession Create(bool ordered=true){var s=new NavigationSession(1,Geometry,NavigationProfile.Default);s.Crowd.Add(10,new NavPoint(-10,0));if(ordered)Assert.True(s.TryStopGroup(new[]{10},false));return s;}
        private static NavigationCommandAuthority Backing(NavigationSession s)=>(NavigationCommandAuthority)typeof(NavigationSession).GetField("groupCommandBacking",F).GetValue(s);
        private static byte[] Bytes(NavigationSession s){using(var state=s.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
        private static void Destroy(NavigationSession s){s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
        [TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)]
        public void EveryBirthAllocationOrdinalRefusesWithoutLiveOrBudgetChanges(int deniedOrdinal)
        {
            var s=Create();try{
                var prefix=Bytes(s);var names=Backing(s).CrowdNames;var spatial=s.Crowd.SpatialStorage;long bytes=s.PhysicalHostBudget.PhysicalBytes;int calls=0,underGate=0,neighbors=s.Crowd.NeighborCandidates;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(s);
                try{s.PhysicalHostBudget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return ++calls!=deniedOrdinal;};Assert.Throws<InvalidOperationException>(()=>s.Crowd.Add(5,new NavPoint(0,0)));}finally{s.PhysicalHostBudget.AllocationAdmission=null;}
                Assert.AreEqual(deniedOrdinal,calls);Assert.Zero(underGate);Assert.AreSame(names,Backing(s).CrowdNames);Assert.AreSame(spatial,s.Crowd.SpatialStorage);Assert.AreEqual(neighbors,s.Crowd.NeighborCandidates);Assert.AreEqual(bytes,s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);CollectionAssert.AreEqual(prefix,Bytes(s));Assert.False(s.Crowd.TryGet(5,out _));
            }finally{Destroy(s);}
        }
        [TestCase(false)][TestCase(true)]
        public void BirthKeepsNullUntilCommandAndLateBirthHasExactColdOwnerShape(bool ordered)
        {
            var s=Create(ordered);NavigationSession cold=null;try{
                Assert.AreEqual(ordered,Backing(s)!=null);int underGate=0;var gate=typeof(NavigationSession).GetField("transportGate",F).GetValue(s);
                try{s.PhysicalHostBudget.AllocationAdmission=_=>{if(Monitor.IsEntered(gate))underGate++;return true;};s.Crowd.Add(5,new NavPoint(0,0));}finally{s.PhysicalHostBudget.AllocationAdmission=null;}
                Assert.Zero(underGate);Assert.AreEqual(ordered,Backing(s)!=null);if(!ordered)Assert.True(s.TryStopGroup(new[]{10},false));
                CollectionAssert.AreEqual(new[]{5,10},Backing(s).CrowdNames.Keys);Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),s.PhysicalHostBudget.PhysicalBytes);
                var bytes=Bytes(s);cold=new NavigationSession(1,Geometry,NavigationProfile.Default);cold.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));CollectionAssert.AreEqual(bytes,Bytes(cold));CollectionAssert.AreEqual(Backing(s).CrowdNames.Keys,Backing(cold).CrowdNames.Keys);Assert.AreEqual(s.PhysicalHostBudget.PhysicalBytes,cold.PhysicalHostBudget.PhysicalBytes);Assert.AreEqual(NavigationStorageOwnershipTests.OwnerShapeWithSession(s),NavigationStorageOwnershipTests.OwnerShapeWithSession(cold));Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(cold),cold.PhysicalHostBudget.PhysicalBytes);
                Assert.True(s.Crowd.Remove(5));CollectionAssert.AreEqual(new[]{10},Backing(s).CrowdNames.Keys);
            }finally{if(cold!=null)Destroy(cold);Destroy(s);}
        }
        [TestCase(1)][TestCase(3)][TestCase(4)][TestCase(6)]
        public void ReentrantBirthSurvivesAndOnlyStaleOuterBirthRefuses(int ordinal)
        {
            var s=Create();try{int calls=0;bool changed=false;
                using(var pinned=s.CaptureOwnedState()){
                    var prefix=WorldWire.Pack(w=>WorldWire.Write(w,pinned));
                    try{s.PhysicalHostBudget.AllocationAdmission=_=>{if(++calls==ordinal){s.PhysicalHostBudget.AllocationAdmission=null;s.Crowd.Add(20,new NavPoint(10,0));changed=true;}return true;};Assert.Throws<InvalidOperationException>(()=>s.Crowd.Add(5,new NavPoint(0,0)));}finally{s.PhysicalHostBudget.AllocationAdmission=null;}
                    Assert.True(changed);Assert.True(s.Crowd.TryGet(20,out _));Assert.False(s.Crowd.TryGet(5,out _));CollectionAssert.AreEqual(new[]{10,20},Backing(s).CrowdNames.Keys);CollectionAssert.AreEqual(prefix,WorldWire.Pack(w=>WorldWire.Write(w,pinned)));
                }
                Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);
            }finally{Destroy(s);}
        }
        [TestCase(false)][TestCase(true)]
        public void BirthDuringCaptureOrCommandInvalidatesStalePublicationAndKeepsPinnedCapture(bool capture)
        {
            var s=Create();try{using(var pinned=s.CaptureOwnedState()){
                var prefix=WorldWire.Pack(w=>WorldWire.Write(w,pinned));bool born=false;
                try{s.PhysicalHostBudget.AllocationAdmission=_=>{s.PhysicalHostBudget.AllocationAdmission=null;s.Crowd.Add(20,new NavPoint(10,0));born=true;return true;};if(capture)Assert.Throws<InvalidOperationException>(()=>{using(var unused=s.CaptureOwnedState()){};});else Assert.False(s.MoveGroup(new[]{10},new[]{new NavPoint(-5,0)}));}finally{s.PhysicalHostBudget.AllocationAdmission=null;}
                Assert.True(born);Assert.True(s.Crowd.TryGet(20,out _));CollectionAssert.AreEqual(new[]{10,20},Backing(s).CrowdNames.Keys);CollectionAssert.AreEqual(prefix,WorldWire.Pack(w=>WorldWire.Write(w,pinned)));
            }Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),s.PhysicalHostBudget.PhysicalBytes);}finally{Destroy(s);}
        }
        [Test] public void BirthIndexPreservesAcceptedDictionaryOrderForUnsortedIdsAndReusedSlots()
        {
            var s=new NavigationSession(1,Geometry,NavigationProfile.Default);var legacy=new NavCrowd(Geometry,NavigationProfile.Default);
            void Add(int id,NavPoint point){s.Crowd.Add(id,point,.1,4,4.5);legacy.Add(id,point,.1,4,4.5);}
            void SameIndex(){foreach(var field in typeof(NavCrowdSpatialStorage).GetFields(F)){var left=field.GetValue(s.Crowd.SpatialStorage);var right=field.GetValue(legacy.SpatialStorage);if(left is Array a&&right is Array b)CollectionAssert.AreEqual(b,a,field.Name);}CollectionAssert.AreEqual(legacy.Units.Select(u=>u.Id),s.Crowd.Units.Select(u=>u.Id));}
            try{Add(30,new NavPoint(-3.5,0));Add(10,new NavPoint(-3.2,0));Add(20,new NavPoint(-2.9,0));SameIndex();Assert.True(s.Crowd.Remove(10));Assert.True(legacy.Remove(10));Add(5,new NavPoint(-3.2,0));SameIndex();}finally{legacy.ReleaseCrowdStorage();Destroy(s);}
        }
        [Test] public void BirthCatalogIncludesActorZeroAndSortsHydratedActorOrder()
        {
            var s=new NavigationSession(1,Geometry,NavigationProfile.Default);var input=new NavCrowd(Geometry,NavigationProfile.Default);
            try{input.Add(0,new NavPoint(-10,0));input.Add(10,new NavPoint(0,0));var state=input.CaptureState();Array.Reverse(state.Units);s.Crowd.RestoreState(state);Assert.True(s.TryStopGroup(new[]{10},false));s.Crowd.Add(5,new NavPoint(10,0));CollectionAssert.AreEqual(new[]{0,5,10},Backing(s).CrowdNames.Keys);CollectionAssert.AreEqual(new[]{0,5,10},s.Crowd.Units.Select(u=>u.Id));Assert.AreEqual(NavigationStorageOwnershipTests.UniqueWithSession(s),s.PhysicalHostBudget.PhysicalBytes);}finally{input.ReleaseCrowdStorage();Destroy(s);}
        }
        [Test] public void DomainRefusedNameAllocationKeepsIdAndAllSpawnBookkeepingUnchanged()
        {
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);var d=Activator.CreateInstance(type,F,null,new object[]{PlayableProfile.Default,1L,false},null);var s=(NavigationSession)type.GetProperty("Navigation",F).GetValue(d);
            object Spawn()=>type.GetMethod("SpawnUnit",F).Invoke(d,new object[]{new NavPoint(-18,-14),PlayableOwner.Player,PlayableEntityKind.Tank});
            try{
                Assert.True(s.TryStopGroup(new[]{s.Crowd.Units[0].Id},false));int id=(int)type.GetField("nextId",F).GetValue(d);var units=(IDictionary)type.GetField("units",F).GetValue(d);var sound=(IDictionary)type.GetField("soundMotion",F).GetValue(d);int count=units.Count,sounds=sound.Count;var prefix=Bytes(s);int denied=0;
                try{s.PhysicalHostBudget.AllocationAdmission=_=>{if(new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().DeclaringType==typeof(NavigationCrowdNameBacking))){denied++;return false;}return true;};var error=Assert.Throws<TargetInvocationException>(()=>Spawn());Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);}finally{s.PhysicalHostBudget.AllocationAdmission=null;}
                Assert.AreEqual(1,denied);Assert.AreEqual(id,type.GetField("nextId",F).GetValue(d));Assert.AreEqual(count,units.Count);Assert.AreEqual(sounds,sound.Count);Assert.False(s.Crowd.TryGet(id,out _));CollectionAssert.AreEqual(prefix,Bytes(s));Assert.AreEqual(id,Spawn());Assert.AreEqual(id+1,type.GetField("nextId",F).GetValue(d));
            }finally{s.ReleaseCommittedHostRoot();type.GetMethod("DestroyOwnedNavigation",F).Invoke(d,null);Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
        }
    }
}

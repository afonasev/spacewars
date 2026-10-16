using System;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed partial class NavigationPhysicalBodyTests
    {
        [TestCase(0,0)][TestCase(0,1)][TestCase(0,2)][TestCase(0,3)]
        [TestCase(1,0)][TestCase(1,1)][TestCase(1,2)][TestCase(1,3)]
        [TestCase(2,0)][TestCase(2,1)][TestCase(2,2)][TestCase(2,3)]
        public void BodyArrivalOccupantKindsLocationsAndRawDuplicateOwnerEdges(int kind,int mask)
        {
            var hot=new NavigationRetentionBudget();Assert.True(hot.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var birth));var r=birth.OwnedReservation();var provider=LayeredNavigationProvider.CopyOwned((LayeredNavigationProvider)NavigationAdmission.CertifiedFlatProvider(new NavGeometry(3,new NavObstacle[0],1)),r);
            var location=(mask&1)!=0?provider.Surfaces[0].OwnedLocation(new NavPoint(-1,0)):new NavLocation(new NavPoint(-1,0),NavLocation.FlatSurface);NavLocation? target=kind==2?((mask&2)!=0?provider.Surfaces[0].OwnedLocation(new NavPoint(1,0)):new NavLocation(new NavPoint(1,0),NavLocation.FlatSurface)):(NavLocation?)null;
            var original=new NavArrivalOccupant((NavArrivalOccupantKind)kind,4,1,location,.4,true,"actor-4",kind==0?(NavArrivalReservationKind?)null:(NavArrivalReservationKind)(kind-1),kind==1?7:0,kind==2?9:0,target);var value=NavArrivalOccupant.CopyOwned(original,r);string stable=original.StableKey;Assert.AreEqual(stable,value.StableKey);
            var root=NavRetainedArray<NavOwnedStorage>.Copy(new[]{value.StorageRoot,provider.StorageRoot},birth.TryReserve);value.StorageRoot.ReleaseOwner();provider.StorageRoot.ReleaseOwner();birth.Attach(root);birth.Commit(root,_=>{});var bytes=CaptureGraph(hot,root);long charge=Unique(root);Assert.AreEqual(charge,hot.PhysicalBytes);root.Release();root=null;value=null;provider=null;original=null;Assert.Zero(hot.PhysicalBytes);
            var cold=new NavigationRetentionBudget();Assert.True(cold.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var decode));var restored=(NavRetainedArray<NavOwnedStorage>)Decode(bytes,decode.OwnedReservation());decode.Attach(restored);decode.Commit(restored,_=>{});var row=(NavArrivalOccupant)((NavDeclarationStorage)restored[0]).Facade;var p=(LayeredNavigationProvider)((NavDeclarationStorage)restored[1]).Facade;
            Assert.AreEqual(stable,row.StableKey);Assert.AreEqual((NavArrivalOccupantKind)kind,row.Kind);Assert.AreEqual(4,row.ActorId);Assert.AreEqual(1,row.Incarnation);Assert.AreEqual(.4,row.Radius);Assert.True(row.BlocksPlacement);Assert.AreEqual("actor-4",row.Identity);Assert.AreSame((mask&1)!=0?p.Surfaces[0].StorageRoot:restored[0],row.Location.NameReference.Owner);Assert.AreEqual((mask&1)!=0?NavNameRole.SurfaceId:NavNameRole.ArrivalOccupantSurface,row.Location.NameReference.Role);Assert.AreEqual(kind==2,row.ActorTarget.HasValue);if(kind==2){Assert.AreSame((mask&2)!=0?p.Surfaces[0].StorageRoot:restored[0],row.ActorTarget.Value.NameReference.Owner);Assert.AreEqual((mask&2)!=0?NavNameRole.SurfaceId:NavNameRole.ArrivalOccupantTargetSurface,row.ActorTarget.Value.NameReference.Role);Assert.AreEqual(9,row.HistoricalExecution);}Assert.AreEqual(charge,cold.PhysicalBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));
            NavArrivalOccupant Born(INavStorageReservation reservation)=>NavArrivalOccupant.CopyOwned(row,reservation);
            Assert.True(cold.TryOpenScope(NavigationPreparationKind.Admission,1,20,out var sourceScope));var counter=new Refusal(sourceScope,int.MaxValue);var copied=Born(counter);Assert.AreEqual(row.StableKey,copied.StableKey);Assert.AreSame(row.Location.NameReference.Owner,copied.Location.NameReference.Owner);sourceScope.Attach(copied.StorageRoot);int calls=counter.Calls;sourceScope.Discard();Assert.AreEqual(charge,cold.PhysicalBytes);
            for(int nth=1;nth<=calls;nth++){Assert.True(cold.TryOpenScope(NavigationPreparationKind.Admission,1,20+nth,out var rejection));var refusal=new Refusal(rejection,nth);Assert.Throws<InvalidOperationException>(()=>Born(refusal));Assert.AreEqual(nth,refusal.Calls);rejection.Discard();Assert.AreEqual(charge,cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);CollectionAssert.AreEqual(bytes,CaptureGraph(cold,restored));}
            var foreign=new NavigationRetentionBudget();Assert.True(foreign.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var rejected));Assert.Throws<ArgumentException>(()=>Born(rejected.OwnedReservation()));Assert.Zero(foreign.PhysicalBytes);Assert.AreEqual(NavigationRetentionBudget.ScopeBytes,foreign.HeldBytes);rejected.Discard();restored.Release();restored=null;row=null;p=null;Assert.Zero(cold.PhysicalBytes);Assert.Zero(cold.HeldBytes);
            var counted=new NavigationRetentionBudget();Assert.True(counted.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var scope));var all=new Refusal(scope,int.MaxValue);var trial=Decode(bytes,all);scope.Attach(trial);int count=all.Calls;scope.Discard();Assert.Zero(counted.PhysicalBytes);
            for(int nth=1;nth<=count;nth++){var bad=new NavigationRetentionBudget();Assert.True(bad.TryOpenScope(NavigationPreparationKind.Decode,1,1,out var rejection));var refusal=new Refusal(rejection,nth);Assert.Throws<InvalidOperationException>(()=>Decode(bytes,refusal));Assert.AreEqual(nth,refusal.Calls);rejection.Discard();Assert.Zero(bad.PhysicalBytes);Assert.Zero(bad.HeldBytes);Assert.Zero(bad.ActiveScopes);}
        }
    }
}

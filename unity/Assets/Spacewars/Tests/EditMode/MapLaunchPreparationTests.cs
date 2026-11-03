using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class MapLaunchPreparationTests
    {
        [TestCase(.00001)][TestCase(.000005)]
        public void ThinCliffFootprintIsValidInBothPolygonConstructionPaths(double halfWidth)
        {
            var bounds=new NavObstacle(-halfWidth,-1,halfWidth,1);
            var publicPolygon=bounds.Footprint;
            var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));
            using(scope){
                var owned=NavPolygon.CreateOwned(publicPolygon.Vertices,scope.OwnedReservation());
                try{
                    CollectionAssert.AreEqual(publicPolygon.Vertices,owned.Vertices);
                    CollectionAssert.AreEqual(publicPolygon.Triangles,owned.Triangles);
                    Assert.True(owned.Contains(new NavPoint(0,0)));
                    Assert.False(owned.Contains(new NavPoint(halfWidth*2,0)));
                }finally{owned.StorageRoot.ReleaseOwner();scope.Discard();}
            }
            Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            Assert.Throws<ArgumentException>(()=>new NavPolygon(new[]{new NavPoint(0,0),new NavPoint(0,0),new NavPoint(1,1)}));
        }
        [Test] public void FarOutsideNullAreaWallsDoNotExpandBakeOrRemoveInteriorBlocker()
        {
            ulong maps=RecastNative.sw_recast_allocation_count();
            var geometry=new NavGeometry(16,new[]{new NavObstacle(1000,1000,1100,1100),new NavObstacle(-1,-3,1,3)},1);
            var input=RecastAuthorityGeometry.Prepare(null,geometry,new NavigationProfile());
            using(var map=new RecastNavigationMap(input))
            using(var query=map.Query(new NavLocation(new NavPoint(-6,0),NavLocation.FlatSurface),new NavLocation(new NavPoint(6,0),NavLocation.FlatSurface))){
                for(int i=0;i<1000&&query.Status==RecastQueryStatus.Pending;i++)query.Step(256);
                Assert.AreEqual(RecastQueryStatus.Full,query.Status);
                var points=query.CopyTypedRoute();Assert.Greater(points.Length,2,"The interior null-area prism must still cut the direct route");
                Assert.True(points.Any(p=>Math.Abs(p.Location.Position.Z)>3));
            }
            Assert.AreEqual(maps,RecastNative.sw_recast_allocation_count());
        }
        [Test] public void OwnedClippingRetainsExactGeometryAndReturnsCreditAfterRefusal()
        {
            var terrain=new CrimsonGroveMap();var empty=new NavGeometry(terrain.HalfExtent,null,terrain.Revision);var profile=new NavigationProfile();
            var expected=RecastAuthorityGeometry.Prepare(terrain,empty,profile);
            for(int refused=0;refused<=8;refused++){
                var budget=new NavigationRetentionBudget();int calls=0;int rejectAt=refused;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));
                budget.AllocationAdmission=bytes=>++calls!=rejectAt;
                bool completed=false;
                using(scope){
                    try{
                        var r=scope.OwnedReservation();
                        using(NavStorageScratch.Reserve(r,RecastAuthorityGeometry.ScratchBound(terrain,empty,true))){
                            var actual=RecastAuthorityGeometry.PrepareOwned(terrain,empty,profile,null,r);
                            Assert.AreEqual(expected.Binding,actual.Binding);completed=true;
                        }
                    }catch(InvalidOperationException){Assert.Greater(refused,0);Assert.AreEqual(refused,calls);}
                    finally{scope.Discard();}
                }
                Assert.AreEqual(refused==0,completed);
                Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
                Assert.LessOrEqual(budget.PeakRetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes);
            }
        }
    }
}

using System;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationTransientDecodeTests
    {
        private static readonly NavPoint[] Concave={new NavPoint(-3,-2),new NavPoint(3,-2),new NavPoint(3,2),new NavPoint(0,0),new NavPoint(-3,2)};
        private static byte[] PolygonWire(bool clockwise)
        {
            var points=(NavPoint[])Concave.Clone();if(clockwise)Array.Reverse(points);
            return WorldWire.Pack(w=>WorldWire.Write(w,new NavObstacle(new NavPolygon(points),-2,4)));
        }
        [TestCase(false)][TestCase(true)]
        public void RealObstacleReaderAdmitsPolygonConstructionAndPreservesTriangulation(bool clockwise)
        {
            var bytes=PolygonWire(clockwise);var original=(byte[])bytes.Clone();var budget=new NavigationRetentionBudget();
            var expected=WorldWire.Unpack(bytes,WorldWire.ReadObstacle);
            using(var transaction=new NavigationDecodeTransaction(budget)){
                var decoded=WorldWire.UnpackPrivate(bytes,0,bytes.Length,WorldWire.ReadObstacle,transaction);
                Assert.True(decoded.Polygon.StorageRoot.BelongsTo(budget));
                Assert.Greater(budget.PhysicalBytes,0);
                CollectionAssert.AreEqual(expected.Polygon.Vertices,decoded.Polygon.Vertices);
                CollectionAssert.AreEqual(expected.Polygon.Triangles,decoded.Polygon.Triangles);
                Assert.True(expected.GeometryEquals(decoded));
            }
            CollectionAssert.AreEqual(original,bytes);Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
        }
        [Test]
        public void EachRealPolygonDecodeRefusalReleasesStagingWithoutChangingInput()
        {
            var bytes=PolygonWire(false);var original=(byte[])bytes.Clone();int sites=0;
            var countBudget=new NavigationRetentionBudget();countBudget.AllocationAdmission=_=>{sites++;return true;};
            using(var transaction=new NavigationDecodeTransaction(countBudget))WorldWire.UnpackPrivate(bytes,0,bytes.Length,WorldWire.ReadObstacle,transaction);
            Assert.Greater(sites,3);Assert.Zero(countBudget.PhysicalBytes);
            for(int refused=1;refused<=sites;refused++){
                var budget=new NavigationRetentionBudget();int seen=0;int target=refused;budget.AllocationAdmission=_=>++seen!=target;
                Assert.Throws<InvalidOperationException>(()=>{
                    using(var transaction=new NavigationDecodeTransaction(budget))WorldWire.UnpackPrivate(bytes,0,bytes.Length,WorldWire.ReadObstacle,transaction);
                },"allocation site "+refused);
                CollectionAssert.AreEqual(original,bytes);Assert.Zero(budget.PhysicalBytes,"physical site "+refused);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);
            }
        }
    }
}

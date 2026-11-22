using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class NavigationBroadPhaseTests
    {
        private static readonly Func<NavPoint,NavObstacle,double> PointDistance=(Func<NavPoint,NavObstacle,double>)typeof(NavGeometry).GetMethod("PointRectDistanceSquared",BindingFlags.Static|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,NavObstacle,double>));
        private static readonly Func<NavPoint,NavPoint,NavObstacle,double> SegmentDistance=(Func<NavPoint,NavPoint,NavObstacle,double>)typeof(NavGeometry).GetMethod("SegmentRectDistanceSquared",BindingFlags.Static|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,NavPoint,NavObstacle,double>));
        private static bool Free(NavGeometry g,NavPoint p,double r){
            if(r<0||double.IsNaN(r)||double.IsInfinity(r)||double.IsNaN(p.X)||double.IsInfinity(p.X)||double.IsNaN(p.Z)||double.IsInfinity(p.Z)||Math.Abs(p.X)+r>g.HalfExtent||Math.Abs(p.Z)+r>g.HalfExtent)return false;
            foreach(var o in g.Obstacles)if(p.X+r>=o.MinX&&p.X-r<=o.MaxX&&p.Z+r>=o.MinZ&&p.Z-r<=o.MaxZ&&PointDistance(p,o)<=r*r)return false;return true;
        }
        private static bool Sweep(NavGeometry g,NavPoint a,NavPoint b,double r){
            if(!Free(g,a,r)||!Free(g,b,r))return false;
            foreach(var o in g.Obstacles)if(Math.Max(a.X,b.X)+r>=o.MinX&&Math.Min(a.X,b.X)-r<=o.MaxX&&Math.Max(a.Z,b.Z)+r>=o.MinZ&&Math.Min(a.Z,b.Z)-r<=o.MaxZ&&SegmentDistance(a,b,o)<=r*r)return false;return true;
        }
        [Test] public void UnindexedRebindDraftKeepsThePaidSweepIndexAndBlockers()
        {
            var map=new CrimsonGroveMap();
            var obstacles=map.MovementBlockers.ToArray();
            var ordinary=new NavGeometry(map.HalfExtent,obstacles,7);
            var draft=NavGeometry.CreateRebindSource(map.HalfExtent,obstacles,7);
            Assert.Greater(ordinary.SpatialIndexRetainedBytes,0);
            Assert.Zero(draft.SpatialIndexRetainedBytes);
            var budget=new NavigationRetentionBudget();
            Assert.True(budget.TryOpenScope(NavigationPreparationKind.Admission,1,1,out var scope));
            using(scope){
                NavGeometry before=null,after=null;
                try{
                    var r=scope.OwnedReservation();
                    before=NavGeometry.CopyOwned(ordinary,r);
                    after=NavGeometry.CopyOwned(draft,r);
                    Assert.AreEqual(before.StorageRoot.StorageBytes,after.StorageRoot.StorageBytes);
                    Assert.AreEqual(before.SpatialIndexRetainedBytes,after.SpatialIndexRetainedBytes);
                    Assert.AreEqual(before.Revision,after.Revision);
                    Assert.True(before.Obstacles.Zip(after.Obstacles,(a,b)=>a.GeometryEquals(b)).All(equal=>equal));
                    Assert.AreEqual(before.Obstacles.Count,after.Obstacles.Count);
                    var random=new Random(7121);
                    NavPoint Point()=>new NavPoint((2*random.NextDouble()-1)*map.HalfExtent,(2*random.NextDouble()-1)*map.HalfExtent);
                    for(int i=0;i<600;i++){
                        var a=Point();
                        var b=i%2==0?new NavPoint(a.X+.4,a.Z-.7):Point();
                        double radius=i%3==0?0:.72;
                        Assert.AreEqual(before.IsFree(a,radius),after.IsFree(a,radius));
                        Assert.AreEqual(before.SegmentFree(a,b,radius,out int expected),after.SegmentFree(a,b,radius,out int actual));
                        Assert.AreEqual(expected,actual);
                    }
                }finally{after?.StorageRoot.Release();before?.StorageRoot.Release();}
            }
            Assert.Zero(budget.PhysicalBytes);
            Assert.Zero(budget.HeldBytes);
        }
        [Test] public void PolygonContainmentKeepsOriginalBoundaryToleranceAndRayParity(){
            bool Original(NavPolygon polygon,NavPoint p){
                if(p.X<polygon.MinX||p.X>polygon.MaxX||p.Z<polygon.MinZ||p.Z>polygon.MaxZ)return false;
                bool inside=false;var vertices=polygon.Vertices;
                for(int i=0,j=vertices.Count-1;i<vertices.Count;j=i++){
                    var a=vertices[j];var b=vertices[i];double x=b.X-a.X,z=b.Z-a.Z,t=Math.Max(0,Math.Min(1,((p.X-a.X)*x+(p.Z-a.Z)*z)/(x*x+z*z)));
                    var q=new NavPoint(a.X+t*x,a.Z+t*z);double dx=p.X-q.X,dz=p.Z-q.Z;
                    if(dx*dx+dz*dz<=1e-9*1e-9)return true;
                    if((a.Z>p.Z)!=(b.Z>p.Z)&&p.X<(b.X-a.X)*(p.Z-a.Z)/(b.Z-a.Z)+a.X)inside=!inside;
                }
                return inside;
            }
            var map=new ThreeCrossingsMap(new ThreeCrossingsProfileData());
            var polygons=map.Supports.Select(s=>s.Bounds.Footprint).Concat(map.MovementBlockers.Select(o=>o.Footprint)).ToArray();var random=new Random(7108);
            foreach(var polygon in polygons){
                for(int i=0;i<500;i++){var p=new NavPoint(random.NextDouble()*140-70,random.NextDouble()*140-70);Assert.AreEqual(Original(polygon,p),polygon.Contains(p));}
                for(int i=0;i<polygon.Vertices.Count;i++){
                    var a=polygon.Vertices[i];var b=polygon.Vertices[(i+1)%polygon.Vertices.Count];
                    foreach(double t in new[]{0d,.5,1d})foreach(double offset in new[]{0d,1e-12,-1e-12,1e-9,-1e-9,2e-9,-2e-9}){
                        var p=new NavPoint(a.X+(b.X-a.X)*t+offset,a.Z+(b.Z-a.Z)*t-offset);Assert.AreEqual(Original(polygon,p),polygon.Contains(p));
                    }
                }
            }
        }
        [Test]
        public void RepairEndpointCacheStillTestsEachChord()
        {
            var points=new[]{
                new NavPoint(0,0),new NavPoint(8,0),new NavPoint(0,6),new NavPoint(8,6),
                new NavPoint(4,0),new NavPoint(24.7,0),new NavPoint(8,3)
            };
            var geometry=new NavGeometry(25,new[]{
                new NavObstacle(3,-1,5,1),
                new NavObstacle(new NavPoint(4,6),.8),
                new NavObstacle(new NavPolygon(new[]{new NavPoint(2,2),new NavPoint(5,2),new NavPoint(2,4)}))
            },1);
            foreach(double radius in new[]{.35,.72})
            {
                var endpoints=new byte[points.Length];
                int hint=-1;
                for(int pass=0;pass<2;pass++)
                for(int i=0;i<points.Length;i++)
                {
                    byte origin=0;
                    for(int j=0;j<points.Length;j++)
                    {
                        bool expected=geometry.SegmentFree(points[i],points[j],radius);
                        bool cached=geometry.SegmentFreeWithBlockerHint(points[i],points[j],radius,
                            ref hint,ref origin,ref endpoints[j]);
                        Assert.AreEqual(expected,cached,$"chord {i} -> {j}, radius {radius}");
                    }
                }
            }
        }

        [Test] public void RepairBlockerHintRetestsEachChordAcrossChangedClearance()
        {
            var map=new AntennaCityMap();
            var obstacles=map.MovementBlockers.Concat(new[]{
                new NavObstacle(new NavPoint(-120,0),.58),
                new NavObstacle(-116,-1,-115,1),
                new NavObstacle(new NavPolygon(new[]{new NavPoint(-112,-2),new NavPoint(-110,0),new NavPoint(-112,2)}))
            }).ToArray();
            var geometry=new NavGeometry(map.HalfExtent,obstacles,1);
            var random=new Random(1951);
            int hint=-1,learned=0,clearAfterHint=0;
            void Check(NavPoint from,NavPoint to,double radius)
            {
                int previous=hint;
                bool expected=geometry.SegmentFree(from,to,radius,out int blocker);
                Assert.AreEqual(expected,geometry.SegmentFreeWithBlockerHint(from,to,radius,ref hint));
                if(hint!=previous&&hint>=0){Assert.AreEqual(blocker,hint);learned++;}
                if(expected&&previous>=0)clearAfterHint++;
            }
            for(int i=0;i<8000;i++){
                var a=new NavPoint(-128+25*random.NextDouble(),random.NextDouble()*30-15);
                var b=new NavPoint(-124+100*random.NextDouble(),random.NextDouble()*100-50);
                Check(a,b,i%4==0?0:.58);
                Check(a,new NavPoint(a.X+.4,a.Z+.1),i%4==0?0:.58);
            }
            Assert.Greater(learned,0);
            Assert.Greater(clearAfterHint,0);
        }

        [Test] public void IndexedCandidatesKeepExactFootprintsSweepsAndBucketBoundaryTangency(){
            var random=new Random(7108);var solids=Enumerable.Range(0,40).Select(i=>{
                double x=random.NextDouble()*100-50,z=random.NextDouble()*100-50;
                return i%3==0?new NavObstacle(new NavPoint(x,z),1.5):i%3==1?new NavObstacle(x-2,z-3,x+2,z+3):new NavObstacle(new NavPolygon(new[]{new NavPoint(x-2,z-2),new NavPoint(x+2,z-1),new NavPoint(x,z+3)}));
            }).Concat(new[]{new NavObstacle(-8,-64,0,64),new NavObstacle(-128,-100,-64,100)}).ToArray();
            var g=new NavGeometry(64,solids,1);Assert.Greater(g.SpatialIndexRetainedBytes,0);
            for(int i=0;i<8000;i++){
                var a=new NavPoint(random.NextDouble()*140-70,random.NextDouble()*140-70);
                var b=i%2==0?new NavPoint(a.X+.8,a.Z-.8):new NavPoint(random.NextDouble()*140-70,random.NextDouble()*140-70);
                double r=new[]{0d,.3,1.4,5d}[i%4];Assert.AreEqual(Free(g,a,r),g.IsFree(a,r),"footprint "+i);Assert.AreEqual(Sweep(g,a,b,r),g.SegmentFree(a,b,r),"sweep "+i);
            }
            for(int x=-64;x<=64;x+=8)for(int z=-64;z<=64;z+=8)foreach(double r in new[]{0d,.3,8d}){
                var a=new NavPoint(x,z);var b=new NavPoint(x+.8,z+.8);Assert.AreEqual(Free(g,a,r),g.IsFree(a,r));Assert.AreEqual(Sweep(g,a,b,r),g.SegmentFree(a,b,r));
            }
            Assert.False(g.IsFree(new NavPoint(double.NaN,0),.3));Assert.False(g.SegmentFree(default,new NavPoint(double.PositiveInfinity,0),.3));Assert.False(g.IsFree(default,double.NaN));
        }
    }
}

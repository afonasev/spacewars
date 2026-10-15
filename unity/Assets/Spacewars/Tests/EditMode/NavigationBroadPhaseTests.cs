using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
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

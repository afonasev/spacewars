using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class TerrainFootprintCacheTests
    {
        private static System.Collections.Generic.List<double> OriginalCuts(NavPoint a,NavPoint b,Tuple<NavPoint,NavPoint>[] edges)
        {
            double Cross(double x,double z,double xx,double zz)=>x*zz-z*xx;
            var cuts=new System.Collections.Generic.List<double>{0,1};double dx=b.X-a.X,dz=b.Z-a.Z,length=dx*dx+dz*dz;if(length==0)return cuts;
            foreach(var edge in edges){var c=edge.Item1;var d=edge.Item2;double ex=d.X-c.X,ez=d.Z-c.Z,den=Cross(dx,dz,ex,ez);
                if(Math.Abs(den)>1e-12){double t=Cross(c.X-a.X,c.Z-a.Z,ex,ez)/den,u=Cross(c.X-a.X,c.Z-a.Z,dx,dz)/den;if(t>=0&&t<=1&&u>=0&&u<=1)cuts.Add(t);}
                else if(Math.Abs(Cross(c.X-a.X,c.Z-a.Z,dx,dz))<=1e-10){foreach(var point in new[]{c,d}){double t=((point.X-a.X)*dx+(point.Z-a.Z)*dz)/length;if(t>0&&t<1)cuts.Add(t);}}
            }
            cuts.Sort();return System.Linq.Enumerable.ToList(System.Linq.Enumerable.Distinct(cuts));
        }
        [Test] public void BroadPhaseKeepsEveryOriginalCutIncludingVeryShortCollinearSegments()
        {
            var type=typeof(PartitionNavigationProvider).Assembly.GetType("Spacewars.Simulation.TerrainLocations",true);
            var cuts=(Func<NavPoint,NavPoint,Tuple<NavPoint,NavPoint>[],System.Collections.Generic.List<double>>)type.GetMethod("Cuts",BindingFlags.Static|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,NavPoint,Tuple<NavPoint,NavPoint>[],System.Collections.Generic.List<double>>));
            var random=new Random(7108);var edges=new Tuple<NavPoint,NavPoint>[100];
            for(int i=0;i<edges.Length;i++)edges[i]=Tuple.Create(new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64),new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64));
            for(int i=0;i<500;i++){
                var a=new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64);var b=i%2==0?new NavPoint(a.X+.8,a.Z-.8):new NavPoint(random.NextDouble()*128-64,random.NextDouble()*128-64);
                CollectionAssert.AreEqual(OriginalCuts(a,b,edges),cuts(a,b,edges));
            }
            foreach(double length in new[]{0d,1e-16,1e-12,1e-8,.8,100d})foreach(double offset in new[]{0d,1e-12,1e-8,.01,10d}){
                var a=default(NavPoint);var b=new NavPoint(length,0);var e=new[]{Tuple.Create(new NavPoint(-100,offset),new NavPoint(length*.5,offset+1e-12)),Tuple.Create(new NavPoint(length*.25,-1),new NavPoint(length*.25,1))};
                CollectionAssert.AreEqual(OriginalCuts(a,b,e),cuts(a,b,e),"length="+length+" offset="+offset);
            }
        }
        [Test] public void CompactGraphTracingKeepsExactPortalLegsAndAllClearanceResults()
        {
            var map=new ThreeCrossingsMap(new ThreeCrossingsProfileData());var g=new NavGeometry(map.HalfExtent,System.Linq.Enumerable.ToArray(map.MovementBlockers),1);
            var provider=new PartitionNavigationProvider(map,g,null);var method=typeof(PartitionNavigationProvider).GetMethod("TryGraphTrace",BindingFlags.Instance|BindingFlags.NonPublic);var validated=typeof(PartitionNavigationProvider).GetMethod("TryGraphTraceValidated",BindingFlags.Instance|BindingFlags.NonPublic);int portals=0;
            foreach(double radius in new[]{.58,.72}){
                var clearance=new NavClearanceProfile("graph-check",1,"ground",0,radius);
                void Check(NavPoint a,NavPoint b){
                    if(!provider.TryLocate(a,clearance,out var from))return;
                    bool expected=provider.TryTrace(from,b,clearance,out var end,out var full);var args=new object[]{from,b,clearance,null,null};bool actual=(bool)method.Invoke(provider,args);
                    Assert.AreEqual(expected,actual);Assert.AreEqual(end,(NavLocation)args[3]);
                    if(provider.TryLocate(b,clearance,out _)){
                        var certified=new object[]{from,b,clearance,null,null};bool certifiedResult=(bool)validated.Invoke(provider,certified);
                        Assert.AreEqual(actual,certifiedResult);Assert.AreEqual((NavLocation)args[3],(NavLocation)certified[3]);
                        Assert.AreEqual(PlayableWorldRestoreTests.Facts(args[4]),PlayableWorldRestoreTests.Facts(certified[4]));
                    }
                    if(!expected)return;var compact=(System.Collections.Generic.IReadOnlyList<NavTypedLeg>)args[4];
                    if(System.Linq.Enumerable.Any(full,l=>l.Kind==NavTypedLegKind.Portal)){
                        portals++;Assert.AreEqual(PlayableWorldRestoreTests.Facts(full),PlayableWorldRestoreTests.Facts(compact),"Detailed crossing trace must remain exact.");
                    }else Assert.IsEmpty(compact,"Same-surface move data is reconstructed by the graph, not retained by its compiler.");
                }
                for(int x=-60;x<=60;x+=4)for(int z=-60;z<=60;z+=4){var a=new NavPoint(x,z);Check(a,new NavPoint(x+.8,z));Check(a,new NavPoint(x,z+.8));}
                foreach(var seam in provider.Seams){
                    if(!seam.Allowed)continue;double dx=seam.To.X-seam.From.X,dz=seam.To.Z-seam.From.Z,length=Math.Sqrt(dx*dx+dz*dz);if(length==0)continue;
                    var mid=new NavPoint((seam.From.X+seam.To.X)/2,(seam.From.Z+seam.To.Z)/2);var a=new NavPoint(mid.X-dz/length*.4,mid.Z+dx/length*.4);var b=new NavPoint(mid.X+dz/length*.4,mid.Z-dx/length*.4);Check(a,b);Check(b,a);
                }
            }
            Assert.Greater(portals,0);TestContext.WriteLine("EXACT_COMPACT_PORTAL_TRACES="+portals);
        }
        [Test] public void IndexedCutsKeepOriginalSourceOrderAndExactDoubleBits()
        {
            var map=new ThreeCrossingsMap(new ThreeCrossingsProfileData());var g=new NavGeometry(map.HalfExtent,System.Linq.Enumerable.ToArray(map.MovementBlockers),1);var provider=new PartitionNavigationProvider(map,g,null);
            var locations=typeof(PartitionNavigationProvider).GetField("locations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(provider);var type=locations.GetType();
            var indexed=(Func<NavPoint,NavPoint,System.Collections.Generic.List<double>>)type.GetMethod("IndexedCuts",BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,NavPoint,System.Collections.Generic.List<double>>),locations);
            var edges=(Tuple<NavPoint,NavPoint>[])type.GetField("edges",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(locations);var random=new Random(7108);
            void Check(NavPoint a,NavPoint b){CollectionAssert.AreEqual(System.Linq.Enumerable.Select(OriginalCuts(a,b,edges),BitConverter.DoubleToInt64Bits),System.Linq.Enumerable.Select(indexed(a,b),BitConverter.DoubleToInt64Bits));}
            for(int i=0;i<500;i++){var a=new NavPoint(random.NextDouble()*140-70,random.NextDouble()*140-70);var b=i%2==0?new NavPoint(a.X+.8,a.Z-.8):new NavPoint(random.NextDouble()*140-70,random.NextDouble()*140-70);Check(a,b);}
            foreach(var edge in edges){Check(edge.Item1,edge.Item2);Check(edge.Item2,edge.Item1);}
            foreach(double length in new[]{0d,1e-16,1e-12,1e-8,.8,100d}){Check(default,new NavPoint(length,0));Check(new NavPoint(BitConverter.Int64BitsToDouble(long.MinValue),0),new NavPoint(length,0));}
        }
        [Test] public void BoundedMemoizationMatchesExactUncachedFootprintsAcrossEvictionAndRadii(){
            var map=new ThreeCrossingsMap(new ThreeCrossingsProfileData());
            var g=new NavGeometry(map.HalfExtent,System.Linq.Enumerable.ToArray(map.MovementBlockers),1);
            var provider=new PartitionNavigationProvider(map,g,null);
            var locations=typeof(PartitionNavigationProvider).GetField("locations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(provider);
            var type=locations.GetType();var cached=(Func<NavPoint,double,bool>)type.GetMethod("Footprint",BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,double,bool>),locations);
            var exact=(Func<NavPoint,double,bool>)type.GetMethod("UncachedFootprint",BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,double,bool>),locations);
            var random=new Random(7108);var points=new NavPoint[5000];for(int i=0;i<points.Length;i++)points[i]=new NavPoint(random.NextDouble()*140-70,random.NextDouble()*140-70);
            foreach(double radius in new[]{0d,.58,.72,1.4})for(int pass=0;pass<2;pass++)foreach(var point in points)Assert.AreEqual(exact(point,radius),cached(point,radius));
            var entries=(IDictionary)type.GetField("footprints",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(locations);
            Assert.AreEqual(4096,entries.Count);Assert.GreaterOrEqual(provider.EstimatedRetainedBytes,128L*entries.Count,"Provider retention must charge the entire bounded footprint cache.");
        }
    }
}

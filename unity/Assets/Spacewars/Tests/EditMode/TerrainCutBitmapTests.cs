using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class TerrainCutBitmapTests
    {
        private static IPlayableTerrain Map(int kind)=>kind==0?(IPlayableTerrain)ThreeCrossingsMap.Default:kind==1?new AiTestMap():new FoundryMap(new FoundryProfileData());
        [TearDown] public void Reset()=>TerrainLocations.DisableCutBitmapForTests=false;
        [TestCase(0)][TestCase(1)][TestCase(2)] public void IndexedCutsKeepEveryDoubleBitAndAuthoredEdgeOrder(int kind)
        {
            var map=Map(kind);var locations=((INavArrivalPointTerrain)map).ArrivalLocations;
            var method=typeof(TerrainLocations).GetMethod("IndexedCuts",BindingFlags.Instance|BindingFlags.NonPublic);
            var edges=(Tuple<NavPoint,NavPoint>[])typeof(TerrainLocations).GetField("edges",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(locations);
            var random=new Random(7111);var pairs=new List<Tuple<NavPoint,NavPoint>>();
            foreach(var edge in edges){pairs.Add(edge);pairs.Add(Tuple.Create(edge.Item2,edge.Item1));pairs.Add(Tuple.Create(edge.Item1,edge.Item1));}
            pairs.Add(Tuple.Create(new NavPoint(-0d,0d),new NavPoint(0d,-0d)));
            for(int i=0;i<600;i++){NavPoint Point()=>new NavPoint((random.NextDouble()*2-1)*map.HalfExtent*1.2,(random.NextDouble()*2-1)*map.HalfExtent*1.2);pairs.Add(Tuple.Create(Point(),Point()));}
            int nontrivial=0;
            foreach(var pair in pairs){
                TerrainLocations.DisableCutBitmapForTests=true;var before=((List<double>)method.Invoke(locations,new object[]{pair.Item1,pair.Item2})).Select(BitConverter.DoubleToInt64Bits).ToArray();
                TerrainLocations.DisableCutBitmapForTests=false;var after=((List<double>)method.Invoke(locations,new object[]{pair.Item1,pair.Item2})).Select(BitConverter.DoubleToInt64Bits).ToArray();
                CollectionAssert.AreEqual(before,after);if(before.Length>2)nontrivial++;
            }
            Assert.Greater(nontrivial,20);TestContext.WriteLine("CUT_BITMAP_BITS map="+map.Id+" queries="+pairs.Count+" nontrivial="+nontrivial);
        }
        private static byte[] Trace(TerrainLocations locations,NavLocation from,NavPoint to,double radius)
        {
            bool ready=locations.TryTrace(from,to,radius,out var endpoint,out var legs);
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
                void Location(NavLocation value){writer.Write(value.SurfaceId??"");writer.Write(BitConverter.DoubleToInt64Bits(value.Position.X));writer.Write(BitConverter.DoubleToInt64Bits(value.Position.Z));}
                writer.Write(ready);Location(endpoint);writer.Write(legs?.Count??-1);
                if(legs!=null)foreach(var leg in legs){Location(leg.From);Location(leg.To);writer.Write(BitConverter.DoubleToInt64Bits(leg.Cost));writer.Write((int)leg.Kind);writer.Write(leg.PortalId??"");}
                return stream.ToArray();
            }
        }
        [TestCase(0)][TestCase(1)][TestCase(2)] public void TraversalAndTypedTraceRemainExactAcrossCandidateDedup(int kind)
        {
            var map=Map(kind);var locations=((INavArrivalPointTerrain)map).ArrivalLocations;var random=new Random(7112);int compared=0,positive=0;
            for(int i=0;i<2500;i++){
                double radius=new[]{0,.58,.72,1.4}[i%4];var point=new NavPoint((random.NextDouble()*2-1)*map.HalfExtent,(random.NextDouble()*2-1)*map.HalfExtent);
                if(!map.TryLocate(point,radius,out var from))continue;
                var to=i%2==0?new NavPoint(point.X+.2,point.Z-.1):new NavPoint((random.NextDouble()*2-1)*map.HalfExtent,(random.NextDouble()*2-1)*map.HalfExtent);
                TerrainLocations.DisableCutBitmapForTests=true;var before=Trace(locations,from,to,radius);
                TerrainLocations.DisableCutBitmapForTests=false;var after=Trace(locations,from,to,radius);CollectionAssert.AreEqual(before,after);if(before[0]!=0)positive++;compared++;
            }
            Assert.Greater(compared,400);Assert.Greater(positive,200);TestContext.WriteLine("CUT_BITMAP_TRACE map="+map.Id+" queries="+compared+" positive="+positive);
        }
        [Test] public void FixedWarmTraversalControlKeepsEveryAcceptedQuery()
        {
            var map=ThreeCrossingsMap.Default;var random=new Random(7113);var pairs=new List<Tuple<NavLocation,NavPoint>>();
            for(int i=0;i<4000&&pairs.Count<128;i++){
                var point=new NavPoint((random.NextDouble()*2-1)*map.HalfExtent,(random.NextDouble()*2-1)*map.HalfExtent);
                var to=i%2==0?new NavPoint(point.X+.2,point.Z-.1):new NavPoint((random.NextDouble()*2-1)*map.HalfExtent,(random.NextDouble()*2-1)*map.HalfExtent);
                if(map.TryLocate(point,.72,out var from)&&map.TryTraverse(from,to,.72,out _))pairs.Add(Tuple.Create(from,to));
            }
            Assert.AreEqual(128,pairs.Count);
            foreach(bool bitmap in new[]{false,true,true,false}){
                TerrainLocations.DisableCutBitmapForTests=!bitmap;foreach(var pair in pairs)Assert.True(map.TryTraverse(pair.Item1,pair.Item2,.72,out _));
                var watch=Stopwatch.StartNew();int ready=0;
                for(int repeat=0;repeat<20;repeat++)foreach(var pair in pairs)if(map.TryTraverse(pair.Item1,pair.Item2,.72,out _))ready++;
                watch.Stop();Assert.AreEqual(2560,ready);TestContext.WriteLine("CUT_BITMAP_CONTROL bitmap="+bitmap+" queries="+ready+" wall_ms="+watch.Elapsed.TotalMilliseconds);
            }
        }
    }
}

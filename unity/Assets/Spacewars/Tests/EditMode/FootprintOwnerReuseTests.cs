using System;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class FootprintOwnerReuseTests
    {
        [TearDown] public void Reset()=>TerrainLocations.DisableFootprintOwnerReuseForTests=false;
        [Test] public void IntegerOwnerFitsExistingPaidEntryBound()
        {
            var key=typeof(TerrainLocations).GetNestedType("FootprintKey",BindingFlags.NonPublic);
            var node=typeof(NavStorageLayout).GetMethod("NodeBytes",BindingFlags.Static|BindingFlags.NonPublic);
            long before=(long)node.MakeGenericMethod(key,typeof(bool)).Invoke(null,null);
            long after=(long)node.MakeGenericMethod(key,typeof(int)).Invoke(null,null);
            Assert.AreEqual(128,before);Assert.AreEqual(before,after);
        }
        [TestCase(false)][TestCase(true)] public void TraversalRetainsExactOutcomeAndEndpointBits(bool foundry)
        {
            IPlayableTerrain map=foundry?(IPlayableTerrain)new FoundryMap(new FoundryProfileData()):ThreeCrossingsMap.Default;
            var random=new Random(7108);int compared=0;
            for(int i=0;i<6000;i++){
                double radius=new[]{0,.58,.72,1.4}[i%4];
                var p=new NavPoint((random.NextDouble()*2-1)*map.HalfExtent,(random.NextDouble()*2-1)*map.HalfExtent);
                if(!map.TryLocate(p,radius,out var from))continue;
                var to=i%2==0?new NavPoint(p.X+.2,p.Z-.1):new NavPoint((random.NextDouble()*2-1)*map.HalfExtent,(random.NextDouble()*2-1)*map.HalfExtent);
                TerrainLocations.DisableFootprintOwnerReuseForTests=true;bool before=map.TryTraverse(from,to,radius,out var a);
                TerrainLocations.DisableFootprintOwnerReuseForTests=false;bool after=map.TryTraverse(from,to,radius,out var b);
                Assert.AreEqual(before,after);Assert.AreEqual(a.SurfaceId,b.SurfaceId);
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.Position.X),BitConverter.DoubleToInt64Bits(b.Position.X));
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.Position.Z),BitConverter.DoubleToInt64Bits(b.Position.Z));compared++;
            }
            Assert.Greater(compared,1000);TestContext.WriteLine("FOOTPRINT_OWNER_EQUIVALENCE foundry="+foundry+" queries="+compared);
        }
        [Test] public void FixedWarmTraversalRemovesRepeatedOwnerSearches()
        {
            var map=ThreeCrossingsMap.Default;var random=new Random(7108);var pairs=new List<Tuple<NavLocation,NavPoint>>();
            for(int i=0;i<3000&&pairs.Count<128;i++){
                var p=new NavPoint((random.NextDouble()*2-1)*map.HalfExtent,(random.NextDouble()*2-1)*map.HalfExtent);
                var to=new NavPoint(p.X+.2,p.Z-.1);
                if(map.TryLocate(p,.72,out var from)&&map.TryTraverse(from,to,.72,out _))pairs.Add(Tuple.Create(from,to));
            }
            Assert.AreEqual(128,pairs.Count);long original=-1,optimized=-1;int expected=-1;
            foreach(bool reuse in new[]{false,true,true,false}){
                TerrainLocations.DisableFootprintOwnerReuseForTests=!reuse;
                foreach(var pair in pairs)map.TryTraverse(pair.Item1,pair.Item2,.72,out _);
                TerrainLocations.OwnerIndexCallsForTests=0;var clock=Stopwatch.StartNew();int ready=0;
                for(int repeat=0;repeat<20;repeat++)foreach(var pair in pairs)if(map.TryTraverse(pair.Item1,pair.Item2,.72,out _))ready++;
                clock.Stop();long searches=TerrainLocations.OwnerIndexCallsForTests;
                if(expected<0)expected=ready;Assert.AreEqual(expected,ready);
                if(reuse)optimized=searches;else original=searches;
                TestContext.WriteLine("FOOTPRINT_OWNER_CONTROL reuse="+reuse+" queries=2560 searches="+searches+" ms="+clock.Elapsed.TotalMilliseconds);
            }
            Assert.Less(optimized,original);
        }
    }
}

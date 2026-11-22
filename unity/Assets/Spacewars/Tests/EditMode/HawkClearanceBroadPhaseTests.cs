using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
 public sealed class HawkClearanceBroadPhaseTests
 {
  [TestCase(false)][TestCase(true)]
  public void HeightsMatchOriginalBitsAcrossMapAndSolidEdges(bool grove)
  {
   var p=grove?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CrimsonGroveMap()):PlayableProfile.ThreeCrossingsDefault;
   var domain=new PlayableDomain(p,1,false);
   var height=(Func<NavPoint,double>)typeof(PlayableDomain).GetMethod("ClearHeight",BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,double>),domain);
   try{
    var random=new Random(7);var points=new List<NavPoint>();
    for(int i=0;i<2000;i++)points.Add(new NavPoint((random.NextDouble()*2-1)*p.ArenaHalfExtent,(random.NextDouble()*2-1)*p.ArenaHalfExtent));
    foreach(var solid in PlayableMap.SolidObstacles(p))foreach(double offset in new[]{-p.HawkRadius,0d,p.HawkRadius}){
     points.Add(new NavPoint(solid.MinX+offset,(solid.MinZ+solid.MaxZ)/2));points.Add(new NavPoint(solid.MaxX+offset,(solid.MinZ+solid.MaxZ)/2));
     points.Add(new NavPoint((solid.MinX+solid.MaxX)/2,solid.MinZ+offset));points.Add(new NavPoint((solid.MinX+solid.MaxX)/2,solid.MaxZ+offset));
    }
    PlayableDomain.DisableAirClearanceBoundsForTests=true;PlayableDomain.AirClearanceExactQueriesForTests=0;
    var expected=points.ConvertAll(point=>BitConverter.DoubleToInt64Bits(height(point)));long original=PlayableDomain.AirClearanceExactQueriesForTests;
    PlayableDomain.DisableAirClearanceBoundsForTests=false;PlayableDomain.AirClearanceExactQueriesForTests=0;
    for(int i=0;i<points.Count;i++)Assert.AreEqual(expected[i],BitConverter.DoubleToInt64Bits(height(points[i])),"point "+i);
    long current=PlayableDomain.AirClearanceExactQueriesForTests;Assert.Less(current,original);
    TestContext.WriteLine("AIR_CLEARANCE grove="+grove+" points="+points.Count+" original_exact="+original+" current_exact="+current);
   }finally{PlayableDomain.DisableAirClearanceBoundsForTests=false;domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
  }

  [TestCase(false)][TestCase(true)]
  public void SweepsMatchSingletonGeometryAtSolidAndArenaEdges(bool grove)
  {
   var p=grove?PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CrimsonGroveMap()):PlayableProfile.ThreeCrossingsDefault;
   var domain=new PlayableDomain(p,1,false);
   var sweep=(Func<NavPoint,NavPoint,double,bool>)typeof(PlayableDomain).GetMethod("AirSweepClear",BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(Func<NavPoint,NavPoint,double,bool>),domain);
   try{
    var solids=PlayableMap.SolidObstacles(p);
    long original=0;
    double Surface(NavPoint point)=>p.AuthoredMap.SupportAt(point)?.HeightAt(point)??0;
    bool Original(NavPoint from,NavPoint to,double height)
    {
     foreach(var solid in solids){
      double top=double.IsNaN(solid.Top)?Math.Max(Surface(from),Surface(to))+p.BallisticWallHeight:solid.Top;
      if(height-p.HawkRadius>top)continue;
      original++;
      if(!new NavGeometry(p.ArenaHalfExtent,new[]{solid},1).SegmentFree(from,to,p.HawkRadius))return false;
     }
     return true;
    }
    var cases=new List<(NavPoint From,NavPoint To,double Height)>();
    var random=new Random(7);
    for(int i=0;i<1000;i++){
     var from=new NavPoint((random.NextDouble()*2-1)*p.ArenaHalfExtent,(random.NextDouble()*2-1)*p.ArenaHalfExtent);
     var to=new NavPoint(from.X+4*(random.NextDouble()-.5),from.Z+4*(random.NextDouble()-.5));
     cases.Add((from,to,i%3==0?0:i%3==1?p.BallisticWallHeight:1000));
    }
    foreach(var solid in solids){
     double z=(solid.MinZ+solid.MaxZ)/2;
     foreach(double offset in new[]{-p.HawkRadius,0d,p.HawkRadius})
      cases.Add((new NavPoint(solid.MinX+offset,z-1),new NavPoint(solid.MinX+offset,z+1),0));
    }
    foreach(double h in new[]{0d,1000d})
     cases.Add((new NavPoint(p.ArenaHalfExtent,0),new NavPoint(p.ArenaHalfExtent-p.HawkRadius,0),h));
    var expected=new bool[cases.Count];var actual=new bool[cases.Count];
    for(int i=0;i<cases.Count;i++){var c=cases[i];expected[i]=Original(c.From,c.To,c.Height);}
    PlayableDomain.AirSweepExactQueriesForTests=0;
    for(int i=0;i<cases.Count;i++){var c=cases[i];actual[i]=sweep(c.From,c.To,c.Height);}
    long current=PlayableDomain.AirSweepExactQueriesForTests;
    CollectionAssert.AreEqual(expected,actual);
    CollectionAssert.Contains(actual,true);
    CollectionAssert.Contains(actual,false);
    Assert.Less(current,original);
    TestContext.WriteLine("AIR_SWEEPS grove="+grove+" samples="+cases.Count+" exact_queries="+original+" -> "+current);
   }finally{domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
  }
 }
}

using System;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
 public sealed class BuildingCombatBroadPhaseTests
 {
  [TearDown] public void Reset()=>BuildingCombatPose.DisableQueryBroadPhaseForTests=false;
  [TestCase(PlayableBuildingKind.Factory)][TestCase(PlayableBuildingKind.Hangar)]
  [TestCase(PlayableBuildingKind.ScientificCenter)][TestCase(PlayableBuildingKind.Headquarters)]
  [TestCase(PlayableBuildingKind.Refinery)][TestCase(PlayableBuildingKind.Mine)][TestCase(PlayableBuildingKind.Outpost)]
  [TestCase(PlayableBuildingKind.SentryTurret)][TestCase(PlayableBuildingKind.RepairTower)][TestCase(PlayableBuildingKind.GaussTower)]
  public void ExactQueriesMatchUnprunedAcrossAnimationConstructionAndDistantRays(PlayableBuildingKind kind)
  {
   var random=new Random(7);
   foreach(double progress in new[]{0d,.37,1d})foreach(double opening in new[]{0d,.6,1d}){
    var pose=BuildingCombatGeometry.Pose(PlayableProfile.Default,kind,true,new NavPoint(17,-23),3,heading:.71,opening:opening,platform:opening,constructionProgress:progress);
    for(int i=0;i<24;i++){
     double extent=i%3==0?100:8;
     var a=new BallisticPoint(17+(random.NextDouble()-.5)*extent,random.NextDouble()*10,-23+(random.NextDouble()-.5)*extent);
     var b=new BallisticPoint(17+(random.NextDouble()-.5)*extent,random.NextDouble()*10,-23+(random.NextDouble()-.5)*extent);double radius=i%2==0?0:.06;
     BuildingCombatPose.DisableQueryBroadPhaseForTests=true;bool hit=pose.Segment(a,b,radius,out var fraction,out var contact);bool overlap=pose.Overlaps(a,radius);
     BuildingCombatPose.DisableQueryBroadPhaseForTests=false;Assert.AreEqual(hit,pose.Segment(a,b,radius,out var actual,out var point));Assert.AreEqual(BitConverter.DoubleToInt64Bits(fraction),BitConverter.DoubleToInt64Bits(actual));Assert.AreEqual(contact,point);Assert.AreEqual(overlap,pose.Overlaps(a,radius));
    }
   }
  }
  [TestCase(PlayableBuildingKind.SentryTurret)]
  [TestCase(PlayableBuildingKind.RepairTower)]
  [TestCase(PlayableBuildingKind.GaussTower)]
  public void ConstructionSweepMatchesExhaustiveWorldTriangles(PlayableBuildingKind kind)
  {
   var profile=PlayableProfile.Default;
   BuildingCombatGeometry.Pose(profile,kind,false,default,0);
   var random=new Random(137);
   foreach(double progress in new[]{0d,.27,.82})
   foreach(double pitch in new[]{-.33,0,.49})
   {
    Assert.True(BuildingCombatGeometry.TryPreparedTurretPose(profile,kind,new NavPoint(38.64,4.25),
        1.2,1.07,pitch,progress,kind==PlayableBuildingKind.GaussTower?.13:0,out var pose));
    for(int i=0;i<14;i++)
    {
     var from=new BallisticPoint(38.64-4+8*random.NextDouble(),1.2+3*random.NextDouble(),4.25-3+6*random.NextDouble());
     var to=i%5==0?from:new BallisticPoint(38.64-1+2*random.NextDouble(),1.2+2*random.NextDouble(),4.25-1+2*random.NextDouble());
     double radius=i%2==0?.06:.3;
     double expected=double.PositiveInfinity;
     for(int root=0;root<pose.RootCount;root++)
     foreach(var part in pose.MeshAt(root).Parts)
     foreach(var local in part.Triangles)
     {
      var triangle=pose.WorldTriangle(local,root);
      expected=Math.Min(expected,triangle.Sweep(new V(from.X,from.Y,from.Z),new V(to.X,to.Y,to.Z),radius));
     }
     BuildingCombatPose.DisableQueryBroadPhaseForTests=true;
     bool hit=pose.Segment(from,to,radius,out var fraction,out _);
     Assert.AreEqual(!double.IsPositiveInfinity(expected),hit);
     Assert.AreEqual(BitConverter.DoubleToInt64Bits(expected),BitConverter.DoubleToInt64Bits(fraction));
    }
   }
  }
  [Test] public void DistantConstructionQueriesNeverEnterTriangleWork()
  {
   var pose=BuildingCombatGeometry.Pose(PlayableProfile.Default,PlayableBuildingKind.SentryTurret,false,default,0,constructionProgress:.5);
   BuildingCombatPose.NarrowQueryCallsForTests=0;
   Assert.False(pose.Segment(new BallisticPoint(100,1,100),new BallisticPoint(101,1,100),.06,out var f,out var at));
   Assert.AreEqual(double.PositiveInfinity,f);Assert.AreEqual(new BallisticPoint(101,1,100),at);
   Assert.False(pose.Overlaps(new BallisticPoint(100,1,100),.06));Assert.AreEqual(0,BuildingCombatPose.NarrowQueryCallsForTests);
  }
 }
}

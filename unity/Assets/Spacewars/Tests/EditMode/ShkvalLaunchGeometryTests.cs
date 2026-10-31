using System;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
 public sealed class ShkvalLaunchGeometryTests
 {
  [TestCase(false)][TestCase(true)] public void AllBoresAlignOnSlopesAndRangesWithOneParabola(bool upgraded)
  {
   var p=PlayableProfile.Default;
   foreach(var slope in new[]{default(NavPoint),new NavPoint(.12,-.18)})foreach(double heading in new[]{0d,.7,-2.3})foreach(double range in new[]{4d,8,12,16})for(int cell=0;cell<4;cell++){
    var ground=new BallisticPoint(3,2,5);var end=new BallisticPoint(3+range*Math.Cos(heading),1,5+range*Math.Sin(heading));
    ShkvalLaunchGeometry.Aim(ground,heading+.6,upgraded,cell,p.ShkvalModelRadius*p.ShkvalModelScale/2.05,p.ShkvalProjectileLength,slope,end,p.ShkvalArcHeight,out double yaw,out double pitch);
    var frame=ShkvalLaunchGeometry.At(ground,heading+.6,yaw,pitch,upgraded,cell,p.ShkvalModelRadius*p.ShkvalModelScale/2.05,p.ShkvalProjectileLength,slope);
    var flight=new BallisticFlight(frame.Center,end,p.ShkvalArcHeight,p.ShkvalProjectileSpeed);var tangent=flight.Tangent(0);double norm=Math.Sqrt(tangent.X*tangent.X+tangent.Y*tangent.Y+tangent.Z*tangent.Z);
    Assert.AreEqual(frame.Direction.X,tangent.X/norm,1e-9);Assert.AreEqual(frame.Direction.Y,tangent.Y/norm,1e-9);Assert.AreEqual(frame.Direction.Z,tangent.Z/norm,1e-9);
    for(double u=.02;u<.9;u+=.1){var a=flight.Point(u-.01);var b=flight.Point(u);var c=flight.Point(u+.01);Assert.AreEqual(-8*p.ShkvalArcHeight,(c.Y-2*b.Y+a.Y)/.0001,1e-8);}
   }
  }
 }
}

using System;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode {
 public sealed class HawkMotionTests {
  static HawkMotion New()=>new HawkMotion(HawkMotionPreviewProfile.Load().Settings());
  [Test]public void ForwardAcceleratesAndBrakesWithOppositeAttitude(){var m=New();m.Advance(.2,1,0,0);Assert.That(m.Speed,Is.GreaterThan(0).And.LessThan(1));Assert.That(m.Pitch,Is.GreaterThan(0));m.Advance(2,1,0,0);m.Advance(.4,0,0,0);Assert.That(m.Pitch,Is.LessThan(0));m.Advance(3,0,0,0);Assert.That(m.Speed,Is.Zero.Within(1e-10));Assert.That(m.Pitch,Is.Zero.Within(1e-10));}
  [Test]public void StrafeKeepsHeadingAndBanks(){var m=New();m.Advance(.2,0,1,0);Assert.That(m.X,Is.GreaterThan(0));Assert.That(m.Z,Is.Zero);Assert.That(m.Yaw,Is.Zero);Assert.That(m.Roll,Is.LessThan(0));m.Advance(3,0,0,0);Assert.That(m.Roll,Is.Zero.Within(1e-10));}
  [Test]public void HeldReverseAutomaticallyStopsUntilReleased(){var m=New();m.Advance(6,-1,0,0);Assert.That(m.Z,Is.LessThan(0).And.GreaterThan(-2));Assert.That(m.Speed,Is.Zero.Within(1e-10));Assert.That(m.ReverseExhausted,Is.True);double z=m.Z;m.Advance(5,-1,0,0);Assert.That(m.Z,Is.EqualTo(z));m.Advance(.1,0,0,0);m.Advance(.5,-1,0,0);Assert.That(m.Z,Is.LessThan(z));}
  [Test]public void YawRampsAndStopsWithoutTranslating(){var m=New();m.Advance(.1,0,0,1);Assert.That(m.YawVelocity,Is.GreaterThan(0).And.LessThan(55));double yaw=m.Yaw;m.Advance(.1,0,0,0);Assert.That(m.Yaw,Is.GreaterThan(yaw));m.Advance(2,0,0,0);Assert.That(m.YawVelocity,Is.Zero);Assert.That(m.Speed,Is.Zero);}
  [Test]public void ForwardTurnBanksAndChangesCourse(){var m=New();m.Advance(2,1,0,1);Assert.That(m.X,Is.GreaterThan(0));Assert.That(m.Z,Is.GreaterThan(0));Assert.That(m.Yaw,Is.GreaterThan(40));Assert.That(m.Roll,Is.LessThan(0));}
  [Test]public void FramePartitionPreservesMotion(){var a=New();var b=New();a.Advance(2,1,.4,.3);for(int i=0;i<120;i++)b.Advance(1d/60,1,.4,.3);Assert.That(a.X,Is.EqualTo(b.X).Within(1e-9));Assert.That(a.Z,Is.EqualTo(b.Z).Within(1e-9));Assert.That(a.Yaw,Is.EqualTo(b.Yaw).Within(1e-9));}
  [Test]public void ProfileRangesAndNonfiniteInputsRejected(){var p=HawkMotionPreviewProfile.Load();foreach(var f in p.Parameters()){float old=f.Read();f.Write(float.NaN);Assert.Throws<ArgumentException>(p.Validate);f.Write(old);}Assert.Throws<ArgumentException>(()=>New().Advance(1,double.NaN,0,0));Assert.Throws<ArgumentOutOfRangeException>(()=>New().Advance(0,0,0,0));}
 }
}

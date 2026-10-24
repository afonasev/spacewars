using System;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
 public sealed class GroundTrailJournalTests
 {
  [Test] public void UnobservedPassageIsRevealedWithOriginalAgeAndExpiresAtTenSeconds()
  {
   var journal=new GroundTrailJournal();var vision=new PlayableVision(0,50,50,1,1);
   vision.Refresh(new[]{new VisionSource(new NavPoint(30,30),2)},Array.Empty<KnownBuilding>());
   journal.Observe(42,PlayableEntityKind.Explorer,new NavPoint(0,0),new NavPoint(.1,0),0,1);
   Assert.AreEqual(1,journal.Count);Assert.IsEmpty(journal.Visible(1,vision.Snapshot()));
   vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),10)},Array.Empty<KnownBuilding>());
   var revealed=journal.Visible(211,vision.Snapshot());Assert.AreEqual(1,revealed.Length);Assert.AreEqual(1,revealed[0].Tick);
   Assert.AreEqual(1,journal.Visible(300,vision.Snapshot()).Length);Assert.IsEmpty(journal.Visible(301,vision.Snapshot()));
  }
  [TestCase(1)] [TestCase(20)] public void ConfiguredLifetimeExpiresAtExactTick(int seconds)
  {
   var data=PlayableProfile.Default.CopyData();data.groundTrailLifetimeSeconds=seconds;var p=PlayableProfile.Create(data);
   var j=new GroundTrailJournal();j.Observe(1,PlayableEntityKind.Tank,new NavPoint(0,0),new NavPoint(1,0),0,1);
   Assert.AreEqual(1,j.Visible(p.GroundTrailLifetimeTicks,lifetimeTicks:p.GroundTrailLifetimeTicks).Length);
   Assert.IsEmpty(j.Visible(p.GroundTrailLifetimeTicks+1,lifetimeTicks:p.GroundTrailLifetimeTicks));
  }
  [Test] public void PauseAndStationaryCannotRenewMarks()
  {
   var j=new GroundTrailJournal();j.Observe(1,PlayableEntityKind.Tank,new NavPoint(0,0),new NavPoint(1,0),0,1);
   for(int i=0;i<40;i++)Assert.AreEqual(1,j.Visible(100).Length);
   for(int t=2;t<=300;t++)j.Observe(1,PlayableEntityKind.Tank,new NavPoint(1,0),new NavPoint(1,0),0,t);
   Assert.AreEqual(1,j.Count);Assert.IsEmpty(j.Visible(301));
  }
  [Test] public void FirstDisplacementAndRestartAreImmediateButSteadyMotionIsBounded()
  {
   var j=new GroundTrailJournal();var k=PlayableEntityKind.Shkval;
   j.Observe(1,k,new NavPoint(0,0),new NavPoint(.01,0),0,1);Assert.AreEqual(1,j.Count);Assert.AreEqual(1,j.Visible(1)[0].Tick);
   j.Observe(1,k,new NavPoint(.01,0),new NavPoint(.02,0),0,2);j.Observe(1,k,new NavPoint(.02,0),new NavPoint(.03,0),0,3);Assert.AreEqual(1,j.Count);
   j.Observe(1,k,new NavPoint(.03,0),new NavPoint(.04,0),0,4);Assert.AreEqual(2,j.Count);
   j.Observe(1,k,new NavPoint(.04,0),new NavPoint(.04,0),0,5);
   j.Observe(1,k,new NavPoint(.04,0),new NavPoint(.05,0),0,6);Assert.AreEqual(3,j.Count);Assert.AreEqual(6,j.Visible(6)[2].Tick);
  }
  [Test] public void RemovedUnitCannotConnectItsOldPositionToNewSpawn()
  {
   var j=new GroundTrailJournal();j.Observe(1,PlayableEntityKind.Shkval,new NavPoint(0,0),new NavPoint(1,0),0,3);
   j.RemoveMissing(new System.Collections.Generic.HashSet<int>());
   j.Observe(1,PlayableEntityKind.Shkval,new NavPoint(20,0),new NavPoint(21,0),0,4);
   var result=j.Visible(4);Assert.AreEqual(2,result.Length);Assert.AreEqual(20,result[1].From.X);Assert.AreEqual(21,result[1].To.X);
  }
 }
}

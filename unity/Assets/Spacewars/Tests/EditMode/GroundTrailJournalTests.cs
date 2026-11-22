using System;
using NUnit.Framework;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
 public sealed class GroundTrailJournalTests
 {
  [Test] public void SharedEndpointQueriesMatchExactVisibilityThroughGapsReuseFogChangesAndExpiry()
  {
   var journal=new GroundTrailJournal();var vision=new PlayableVision(0,100,100,1,1);
   var positions=new NavPoint[12];for(int i=0;i<positions.Length;i++)positions[i]=new NavPoint(i-6,-4);
   for(int tick=1;tick<=210;tick++)for(int id=0;id<positions.Length;id++){
    var from=positions[id];var to=new NavPoint(from.X+.01*(id+1),from.Z+.005*(id%3));
    if(tick%37==0)to=from; // stopped segment and immediate restart
    if(tick==100)to=new NavPoint(from.X+15,from.Z); // discarded large displacement leaves a gap
    journal.Observe(id,PlayableEntityKind.Tank,from,to,.1*id,tick);positions[id]=to;
   }
   journal.RemoveMissing(new System.Collections.Generic.HashSet<int>());
   journal.Observe(1,PlayableEntityKind.Explorer,new NavPoint(-5,-4),new NavPoint(-4.9,-4),0,211);
   try{
    PlayableGroundTrail[] retained=null;string oldValues=null;long referenceQueries=0,reusedQueries=0;
    foreach(int tick in new[]{211,212,350,500,511})foreach(double x in new[]{0d,20d,40d}){
     vision.Refresh(new[]{new VisionSource(new NavPoint(x,0),18),new VisionSource(new NavPoint(-5,-4),.1)},Array.Empty<KnownBuilding>());
     var snapshot=vision.Snapshot();
     GroundTrailJournal.DisableEndpointReuseForTests=true;GroundTrailJournal.VisibilityQueriesForTests=0;var expected=journal.Visible(tick,snapshot);referenceQueries+=GroundTrailJournal.VisibilityQueriesForTests;
     GroundTrailJournal.DisableEndpointReuseForTests=false;GroundTrailJournal.VisibilityQueriesForTests=0;var actual=journal.Visible(tick,snapshot);reusedQueries+=GroundTrailJournal.VisibilityQueriesForTests;
     CollectionAssert.AreEqual(expected,actual,"same sample identities and order at tick "+tick+", sight "+x);
     Assert.AreEqual(journal.Count,journal.Visible(tick).Length,"overview keeps every unexpired sample");
     if(retained!=null)Assert.AreEqual(oldValues,Newtonsoft.Json.JsonConvert.SerializeObject(retained));
     retained=actual;oldValues=Newtonsoft.Json.JsonConvert.SerializeObject(actual);
    }
    Assert.Less(reusedQueries,referenceQueries);Assert.Zero(journal.Count);
    TestContext.WriteLine("TRAIL_QUERIES reference="+referenceQueries+" reused="+reusedQueries);
   }finally{GroundTrailJournal.DisableEndpointReuseForTests=false;GroundTrailJournal.VisibilityQueriesForTests=0;}
  }
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

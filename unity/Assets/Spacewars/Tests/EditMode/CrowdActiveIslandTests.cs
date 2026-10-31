using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode {
public sealed class CrowdActiveIslandTests {
 const double Dt=1d/30;
 static NavigationSession Create(int moving,bool tiny){
  var s=new NavigationSession(73,new NavGeometry(256,Array.Empty<NavObstacle>(),1),NavigationProfile.Default);
  try{
   for(int i=0;i<400;i++){
    int group=i/50,j=i%50;double angle=group*Math.PI/4;bool active=moving==2?j==0:moving==50?j<25:moving==100;
    double radial=moving==2&&j==0?130:88;
    var point=new NavPoint(Math.Cos(angle)*radial+(j%7)*2.2,Math.Sin(angle)*radial+(j/7)*2.2);
    s.Crowd.Add(i+1,point,.72,4,4.5,canYield:true).Team=group+1;
    if(active){var goal=moving==2?new NavPoint(point.X+Math.Cos(angle)*12,point.Z+Math.Sin(angle)*12):new NavPoint(point.X+12,point.Z);Assert.True(s.Crowd.SetRoute(i+1,goal,new[]{goal}));}
    if(tiny&&j==1)s.Crowd.PreferMaxSpeed(i+1,1e-12);
   }
   return s;
  }catch{s.DestroyNavigationAuthority();throw;}
 }
 static byte[] Bytes(NavigationSession s){using(var state=s.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
 static void Destroy(NavigationSession s){s.ReleaseCommittedHostRoot();s.DestroyNavigationAuthority();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
 [TestCase(0,false)][TestCase(2,false)][TestCase(50,false)][TestCase(100,false)][TestCase(2,true)]
 public void Fixed400AuthorityCaptureAndBudgetsMatchPredecessor(int moving,bool tiny){
  var expected=new List<byte[]>();var totals=new double[3];byte[] final=null;
  try{
   for(int phase=0;phase<3;phase++){
    NavCrowdMotion.ForceAllOverflowComponentsEvidence=phase!=1;
    var s=Create(moving,tiny);
    try{
     for(int step=0;step<20;step++){
      var watch=Stopwatch.StartNew();s.Crowd.Step(Dt);watch.Stop();totals[phase]+=watch.Elapsed.TotalMilliseconds;
      var bytes=Bytes(s);if(phase==0)expected.Add(bytes);else CollectionAssert.AreEqual(expected[step],bytes,"exact full authority step "+step+" phase "+phase);
      Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);final=bytes;
     }
    }finally{Destroy(s);}
   }
   using(var sha=SHA256.Create())TestContext.WriteLine($"ACTIVE_ISLAND_ABA moving={moving} tiny={tiny} steps=20 original_ms={totals[0]:R} candidate_ms={totals[1]:R} original2_ms={totals[2]:R} sha256={BitConverter.ToString(sha.ComputeHash(final))}");
  }finally{NavCrowdMotion.ForceAllOverflowComponentsEvidence=false;}
 }
}}

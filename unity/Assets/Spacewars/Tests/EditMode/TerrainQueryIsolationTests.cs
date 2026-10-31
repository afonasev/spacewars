using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class TerrainQueryIsolationTests
    {
        private static ThreeCrossingsMap Map()=>new ThreeCrossingsMap(new ThreeCrossingsProfileData());
        private static byte[] Bytes(NavigationSession session){using(var state=session.CaptureOwnedState())using(var bytes=WorldWire.PackOwned(w=>WorldWire.Write(w,state),state.CaptureBudget))return bytes.TransferPublic();}
        [Test,Timeout(60000)] public void ColdRestoreAndActualCrowdQueryKeepIndependentCutsAndExactContinuation()
        {
            var map=Map();var p=PlayableProfile.Default;var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);var source=new NavigationSession(1,geometry,p.Navigation,terrain:map);NavigationSession cold=null;
            using var entered=new ManualResetEventSlim();using var resume=new ManualResetEventSlim();int restoreThread=0,paused=0;Exception error=null;Thread thread=null;var previous=TerrainLocations.IndexedCutCandidateForTests;Assert.IsNull(previous);
            try{
                var locations=((INavArrivalPointTerrain)map).ArrivalLocations;var index=typeof(TerrainLocations).GetField("edgeIndex",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(locations);var edgeCount=((Array)typeof(TerrainLocations).GetField("edges",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(locations)).Length;var entries=index.GetType().GetProperty("Entries",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(index);
                TestContext.WriteLine("Q4_TRACE_BOUND edges="+edgeCount+" indexedEntries="+entries+" scratch="+locations.TraceScratchBound+" cache="+locations.FootprintCacheRetainedBytes);
                var actor=source.Crowd.Add(51,new NavPoint(35.2,43.2),.58,7,7.5);var route=new[]{new NavPoint(34.5,43.2),new NavPoint(29.5,43.2),new NavPoint(11.2,43.2),new NavPoint(11.2,42.4),new NavPoint(8,42.4),new NavPoint(-28.8,42.4),new NavPoint(-28.8,43.2),new NavPoint(-35.2,43.2),new NavPoint(-43.08,46)};Assert.True(source.Crowd.SetRoute(actor.Id,route.Last(),route));var bytes=Bytes(source);cold=new NavigationSession(1,geometry,p.Navigation,terrain:map);
                var crowd=new NavCrowd(geometry,p.Navigation,map);crowd.Add(1,new NavPoint(-35.2,-43.2),.58,7,7.5);Assert.True(crowd.SetRoute(1,new NavPoint(-29.5,-43.2),new[]{new NavPoint(-29.5,-43.2)}));var end=new NavPoint(-32,-43.2);bool expected=((INavMotionClearance)crowd).ClearMotion(0,end);Assert.True(expected);
                TerrainLocations.IndexedCutCandidateForTests=owner=>{if(Thread.CurrentThread.ManagedThreadId!=restoreThread||Interlocked.CompareExchange(ref paused,1,0)!=0)return;entered.Set();if(!resume.Wait(10000))throw new TimeoutException("bounded test-only first-query barrier");};
                thread=new Thread(()=>{restoreThread=Thread.CurrentThread.ManagedThreadId;try{cold.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));}catch(Exception e){error=e;}});thread.Start();Assert.True(entered.Wait(15000),"actual current cold surface validation is paused inside its first candidate enumeration");
                Assert.AreEqual(expected,((INavMotionClearance)crowd).ClearMotion(0,end),"second actual query retains its own result");resume.Set();Assert.True(thread.Join(15000));Assert.IsNull(error,error?.ToString());TerrainLocations.IndexedCutCandidateForTests=previous;
                CollectionAssert.AreEqual(bytes,Bytes(cold));for(int i=0;i<3;i++){source.Step(1d/30);cold.Step(1d/30);CollectionAssert.AreEqual(Bytes(source),Bytes(cold),"exact continuation after simultaneous terrain queries "+i);}
            }finally{resume.Set();if(thread?.IsAlive==true)Assert.True(thread.Join(15000));TerrainLocations.IndexedCutCandidateForTests=previous;source.ReleaseCommittedHostRoot();source.DestroyNavigationAuthority();if(cold!=null){cold.ReleaseCommittedHostRoot();cold.DestroyNavigationAuthority();}}
        }
        [Test,Timeout(60000)] public void ConcurrentFootprintEvictionMatchesSerialValuesAndKeepsTheSameBound()
        {
            var expectedMap=Map();var sharedMap=Map();const int count=5000;var points=Enumerable.Range(0,count).Select(i=>new NavPoint(-63+(i%100)*1.26,-63+(i/100)*1.26)).ToArray();var radii=Enumerable.Range(0,count).Select(i=>i%3==0?.58:i%3==1?.72:1.24).ToArray();var expected=new bool[count];var locations=new NavLocation[count];for(int i=0;i<count;i++)expected[i]=expectedMap.TryLocate(points[i],radii[i],out locations[i]);
            Assert.True(expected.Any(x=>x));Assert.True(expected.Any(x=>!x));using var start=new Barrier(2);var errors=new Exception[2];var workers=new Thread[2];
            for(int lane=0;lane<2;lane++){int own=lane;workers[lane]=new Thread(()=>{try{Assert.True(start.SignalAndWait(10000));for(int n=0;n<count;n++){int i=own==0?n:count-1-n;bool value=sharedMap.TryLocate(points[i],radii[i],out var location);if(value!=expected[i]||value&&!location.Equals(locations[i]))throw new InvalidOperationException("cross-query footprint value changed at "+i);}}catch(Exception e){errors[own]=e;}});workers[lane].Start();}
            foreach(var worker in workers)Assert.True(worker.Join(20000));Assert.IsNull(errors[0],errors[0]?.ToString());Assert.IsNull(errors[1],errors[1]?.ToString());
            var terrain=typeof(ThreeCrossingsMap).GetField("locations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(sharedMap);var type=terrain.GetType();var cache=(IDictionary)type.GetField("footprints",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(terrain);var queue=(ICollection)type.GetField("footprintOrder",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(terrain);
            Assert.AreEqual(4096,cache.Count,"the existing bounded cache cap stays unchanged");Assert.AreEqual(cache.Count,queue.Count);Assert.AreEqual(queue.Count,((IEnumerable)queue).Cast<object>().Distinct().Count(),"eviction ownership has no duplicate/missing keys");
        }
    }
}

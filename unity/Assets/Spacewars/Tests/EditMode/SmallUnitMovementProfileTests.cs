using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class SmallUnitMovementProfileTests
    {
        [TearDown] public void Reset(){NavCrowdMotion.DisableSelectClearanceReuseForQa=false;NavCrowdMotion.CountSelectClearanceForQa=false;}
        static byte[] Bytes(NavigationSession s){using(var state=s.CaptureOwnedState())return WorldWire.Pack(w=>WorldWire.Write(w,state));}
        static void Destroy(NavigationSession s){if(s==null)return;s.DestroyNavigationAuthority();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
        static NavigationSession Routed(int count,PlayableProfile p,CertifiedRouteKernel kernel)
        {
            var s=new NavigationSession(1,new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1),p.Navigation,terrain:p.AuthoredMap);
            try{
                s.CancelProbe(7);
                for(int i=0;i<count;i++){s.Crowd.Add(i+1,new NavPoint(-18-(i/3)*2.5,((i%3)-1)*2.5),.72,4,4.5);Assert.True(s.Move(i+1,new NavPoint(18,((i%3)-1)*2.5)));}
                for(int i=0;i<20000&&s.PendingCount>0;i++){kernel.Pump(new PlayableRouteBinding(s.Admission.Geometry,p,s.Admission,s.RoutePort),64);s.ApplyResults();}
                Assert.Zero(s.PendingCount);return s;
            }catch{kernel.Clear();Destroy(s);throw;}
        }
        [TestCase(3)][TestCase(5)]
        public void ThreeCrossingsReusePreservesExactCurrentStateAndColdNextStep(int count)
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var ka=new CertifiedRouteKernel();var kb=new CertifiedRouteKernel();NavigationSession a=null,b=null,cold=null;
            try{
                a=Routed(count,p,ka);b=Routed(count,p,kb);CollectionAssert.AreEqual(Bytes(a),Bytes(b));
                NavCrowdMotion.CountSelectClearanceForQa=true;long totalHits=0,totalSaved=0;
                for(int step=0;step<120;step++){
                    if(step==32){a.Stop(count,true);b.Stop(count,true);}if(step==48){a.Stop(count-1,false);b.Stop(count-1,false);}
                    NavCrowdMotion.DisableSelectClearanceReuseForQa=true;NavCrowdMotion.SelectClearanceCallsForQa=0;a.Step(1d/30);long original=NavCrowdMotion.SelectClearanceCallsForQa;
                    NavCrowdMotion.DisableSelectClearanceReuseForQa=false;NavCrowdMotion.SelectClearanceCallsForQa=NavCrowdMotion.SelectClearanceHitsForQa=0;b.Step(1d/30);
                    totalHits+=NavCrowdMotion.SelectClearanceHitsForQa;totalSaved+=original-NavCrowdMotion.SelectClearanceCallsForQa;
                    if(step%20==0)CollectionAssert.AreEqual(Bytes(a),Bytes(b),"authored movement/current routes/HOLD/stop step="+step);
                }
                CollectionAssert.AreEqual(Bytes(a),Bytes(b));Assert.Greater(totalHits,0);Assert.Greater(totalSaved,0);
                cold=new NavigationSession(1,new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1),p.Navigation,terrain:p.AuthoredMap);
                using(var state=b.CaptureOwnedState())cold.RestoreState(state);CollectionAssert.AreEqual(Bytes(b),Bytes(cold));
                b.Step(1d/30);cold.Step(1d/30);CollectionAssert.AreEqual(Bytes(b),Bytes(cold),"cold next step stays exact");
                TestContext.WriteLine("THREE_CROSSINGS_CLEARANCE count="+count+" hits="+totalHits+" saved_calls="+totalSaved);
            }finally{ka.Clear();kb.Clear();Destroy(cold);Destroy(b);Destroy(a);}
        }
        [TestCase(3)][TestCase(5)][TestCase(30)][TestCase(50)]
        public void ProfileThreeCrossingsIdleAndMoving(int count)
        {
            var p=PlayableProfile.ThreeCrossingsDefault;
            var g=new NavGeometry(p.ArenaHalfExtent,PlayableMap.StaticObstacles(p),1);
            var s=new NavigationSession(1,g,p.Navigation,terrain:p.AuthoredMap);
            var kernel=new CertifiedRouteKernel();
            try{
                s.CancelProbe(7);
                var origins=Enumerable.Range(0,count).Select(i=>new NavPoint(-18-(i/3)*2.5,((i%3)-1)*2.5)).ToArray();
                if(count>5){
                    var free=new System.Collections.Generic.List<NavPoint>();
                    for(double x=-48;x<=-20;x+=2.5)for(double z=-5;z<=5;z+=2.5){var point=new NavPoint(x,z);if(g.IsFree(point,.72)&&p.AuthoredMap.TryLocate(point,.72,out _))free.Add(point);}
                    // Preserve the measured30 origins; add separately validated rows
                    // for50 instead of assuming every authored bank cell is free.
                    for(double band=7.5;free.Count<count&&band<p.ArenaHalfExtent-2;band+=2.5)for(double x=-48;x<=-20&&free.Count<count;x+=2.5)foreach(double z in new[]{-band,band}){var point=new NavPoint(x,z);if(g.IsFree(point,.72)&&p.AuthoredMap.TryLocate(point,.72,out _))free.Add(point);}
                    Assert.GreaterOrEqual(free.Count,count,"scaled fixture requires distinct valid authored starting footprints");origins=free.Take(count).ToArray();
                }
                for(int i=0;i<count;i++)s.Crowd.Add(i+1,origins[i],.72,4,4.5);
                void Measure(string phase){
                    for(int i=0;i<8;i++)s.Step(1d/30);
                    var samples=new double[120];long alloc=GC.GetAllocatedBytesForCurrentThread();
                    for(int i=0;i<samples.Length;i++){long start=Stopwatch.GetTimestamp();s.Step(1d/30);samples[i]=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;}
                    alloc=GC.GetAllocatedBytesForCurrentThread()-alloc;
                    string output=Environment.GetEnvironmentVariable("SPACEWARS_SMALL_MOVEMENT_OUTPUT");
                    if(output!=null){Directory.CreateDirectory(output);File.WriteAllLines(Path.Combine(output,phase+"-"+count+".csv"),new[]{"step,navigation_ms"}.Concat(samples.Select((ms,i)=>i+","+ms.ToString("R",CultureInfo.InvariantCulture))));File.WriteAllBytes(Path.Combine(output,phase+"-"+count+"-state.bin"),Bytes(s));}
                    var sorted=samples.OrderBy(x=>x).ToArray();TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,"SMALL_MOVEMENT count={0} phase={1} mean_ms={2:F6} p95_ms={3:F6} p99_ms={4:F6} allocated={5} repairs={6} stable_clearance={7}",count,phase,samples.Average(),sorted[113],sorted[118],alloc,s.Crowd.RepairCount,s.Crowd.StableSelectClearance));
                }
                Measure("idle");
                for(int i=0;i<count;i++)Assert.True(s.Move(i+1,new NavPoint(18,origins[i].Z)));
                for(int i=0;i<20000&&s.PendingCount>0;i++){kernel.Pump(new PlayableRouteBinding(s.Admission.Geometry,p,s.Admission,s.RoutePort),64);s.ApplyResults();}
                Assert.Zero(s.PendingCount);Assert.True(s.Crowd.Units.All(u=>u.Moving));
                var before=s.Crowd.Units.Select(u=>u.Position).ToArray();Measure("moving");
                Assert.True(s.Crowd.Units.Where((u,i)=>!u.Position.Equals(before[i])).Any());
                PerformanceProfile.Flush();
            }finally{kernel.Clear();s.DestroyNavigationAuthority();Assert.Zero(s.PhysicalHostBudget.PhysicalBytes);Assert.Zero(s.PhysicalHostBudget.HeldBytes);Assert.Zero(s.PhysicalHostBudget.ActiveScopes);}
        }
    }
}

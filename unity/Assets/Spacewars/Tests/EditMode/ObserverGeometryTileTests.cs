using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class ObserverGeometryTileTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly Type CellType=typeof(SharedFlowRouter).GetNestedType("Cell",BindingFlags.NonPublic);
        private static object Cell(int x,int z)=>Activator.CreateInstance(CellType,new object[]{x,z});
        private static bool Free(SharedFlowRouter r,int x,int z)=>(bool)typeof(SharedFlowRouter).GetMethod("IsFree",Private).Invoke(r,new[]{Cell(x,z)});
        private static bool Edge(SharedFlowRouter r,int x,int z,int tx,int tz)=>(bool)typeof(SharedFlowRouter).GetMethod("GridSegmentFree",Private).Invoke(r,new[]{Cell(x,z),Cell(tx,tz)});
        private static NavigationProfile Profile(double radius=.3)=>new NavigationProfile("tile-check",1,radius,4,4.5,1,.15,3.6,.08);
        private static SharedFlowRouter Tiled(NavGeometry g,NavigationProfile p)=>new SharedFlowRouter(g,p,null,true);
        private static SharedFlowRouter OwnerRouter(object d,NavGeometry g,NavigationProfile p,PlayableOwner owner)=>(SharedFlowRouter)d.GetType().GetMethod("ObservedRouter",Private).Invoke(d,new object[]{owner,g,p,.3,4d,4.5d});

        [Test]public void CellsDirectedEdgesHaloAndWarmAnswersMatchOriginal()
        {
            foreach(var shape in new[]{new NavObstacle(-1,-3,1,3),new NavObstacle(new NavPoint(0,0),2),new NavObstacle(new NavPolygon(new[]{new NavPoint(-2,-3),new NavPoint(2,-1),new NavPoint(1,3)}))})
            foreach(double radius in new[]{.3,.72}){
                var g=new NavGeometry(181,new[]{shape},1);var p=Profile(radius);var original=new SharedFlowRouter(g,p);var actual=Tiled(g,p);
                foreach(int x in new[]{int.MinValue,-2,-1,0,31,32,179,180,181,182,361,362,363,int.MaxValue})
                foreach(int z in new[]{-2,-1,0,31,32,180,181,182,361,362,363}){
                    Assert.AreEqual(Free(original,x,z),Free(actual,x,z));
                    foreach(var delta in new[]{new[]{0,1},new[]{1,0},new[]{0,-1},new[]{-1,0}})if(x!=int.MaxValue&&x!=int.MinValue)Assert.AreEqual(Edge(original,x,z,x+delta[0],z+delta[1]),Edge(actual,x,z,x+delta[0],z+delta[1]));
                }
                Free(actual,170,170);Edge(actual,170,170,171,170);long cells=actual.PreparedCellComputations,edges=actual.PreparedEdgeComputations;
                for(int n=0;n<8;n++){Assert.AreEqual(Free(original,170,170),Free(actual,170,170));Assert.AreEqual(Edge(original,170,170,171,170),Edge(actual,170,170,171,170));}
                Assert.AreEqual(cells,actual.PreparedCellComputations);Assert.AreEqual(edges,actual.PreparedEdgeComputations);
                Edge(actual,171,170,170,170);Assert.AreEqual(edges+1,actual.PreparedEdgeComputations,"Opposite directions retain independent answers");
                Assert.Zero(actual.PreparedGeometrySide);Assert.LessOrEqual(actual.PreparedGeometryBytes,SharedFlowRouter.ObserverPreparationPayloadLimit);
            }
        }

        [Test]public void ExactPathsAndBfsWorkMatchIndependentReferenceAcrossFields()
        {
            foreach(double radius in new[]{.3,.72}){
                var g=new NavGeometry(181,new[]{new NavObstacle(-.8,-7,.8,7)},1);var p=Profile(radius);
                var reference=new SharedFlowRouterReference(g,p);var original=new SharedFlowRouter(g,p,null);var actual=Tiled(g,p);
                for(int i=0;i<12;i++){
                    var from=new NavPoint(-5-i*.13,-2+i*.11);var to=new NavPoint(5+i*.17,2-i*.19);
                    var expected=reference.FindPath(from,to);CollectionAssert.AreEqual(expected,actual.FindPath(from,to));CollectionAssert.AreEqual(expected,original.FindPath(from,to));
                    Assert.AreEqual(original.ExpandedCells,actual.ExpandedCells);Assert.AreEqual(original.CachedCells,actual.CachedCells);Assert.AreEqual(original.RebuildCount,actual.RebuildCount);
                    if(i%3==2){original.TrimFields(1);actual.TrimFields(1);}
                }
                Assert.Zero(original.PreparedGeometryBytes,"Original over-cap constructor contract is preserved");Assert.Greater(actual.PreparedGeometryBytes,0);
            }
        }

        [Test]public void TileEvictionNeverAliasesAnswersAndPayloadStaysBounded()
        {
            var g=new NavGeometry(2100,new[]{new NavObstacle(-2099,-2099,-2097,-2097)},1);var p=Profile();var original=new SharedFlowRouter(g,p);var actual=Tiled(g,p);
            Assert.False(Free(actual,1,1));
            for(int i=0;i<160;i++){
                int x=(i%16)*32,z=(i/16)*32;Assert.AreEqual(Free(original,x,z),Free(actual,x,z));
                Assert.AreEqual(Edge(original,x,z,x+1,z),Edge(actual,x,z,x+1,z));Assert.LessOrEqual(actual.PreparedGeometryBytes,SharedFlowRouter.ObserverPreparationPayloadLimit);
            }
            Assert.AreEqual(SharedFlowRouter.ObserverPreparationPayloadLimit,actual.PreparedGeometryBytes);long before=actual.PreparedCellComputations;
            Assert.False(Free(actual,1,1));Assert.Greater(actual.PreparedCellComputations,before,"Evicted blocked answer must be recomputed under its full tile key");
            Assert.AreEqual(Free(original,512,0),Free(actual,512,0));
        }

        [Test]public void GeometryProfileFallbackAndDetachedPathContractsRemainExact()
        {
            var p=Profile();var g=new NavGeometry(181,new[]{new NavObstacle(-.8,-7,.8,7)},1);var a=Tiled(g,p);var from=new NavPoint(-5,0);var goal=new NavPoint(5,0);
            var expected=new SharedFlowRouterReference(g,p).FindPath(from,goal);var first=a.FindPath(from,goal);CollectionAssert.AreEqual(expected,first);first[0]=new NavPoint(123,456);CollectionAssert.AreEqual(expected,a.FindPath(from,goal));
            for(int i=0;i<100;i++)a.FindPath(new NavPoint(-5,i*.01),new NavPoint(5,-i*.01));Assert.LessOrEqual(a.CachedPathCount,64);Assert.LessOrEqual(a.CachedPathPoints,8192);
            var changed=new NavGeometry(181,new[]{new NavObstacle(-.8,-2,.8,2)},2);var next=a.ForGeometry(changed);Assert.Zero(next.PreparedGeometryBytes);CollectionAssert.AreEqual(new SharedFlowRouterReference(changed,p).FindPath(from,goal),next.FindPath(from,goal));
            var fallback=Tiled(new NavGeometry(1e308,Array.Empty<NavObstacle>(),1),p);Free(fallback,0,0);Edge(fallback,0,0,1,0);Assert.Zero(fallback.PreparedGeometryBytes);
            var allocationFallback=Tiled(g,p);var prep=typeof(SharedFlowRouter).GetField("preparation",Private).GetValue(allocationFallback);prep.GetType().GetField("allocationUnavailable",Private).SetValue(prep,true);Assert.AreEqual(Free(new SharedFlowRouter(g,p),180,180),Free(allocationFallback,180,180));Assert.Zero(allocationFallback.PreparedGeometryBytes);
            var empty=Tiled(new NavGeometry(181,Array.Empty<NavObstacle>(),1),p);foreach(double z in new[]{0d,BitConverter.Int64BitsToDouble(long.MinValue),0d})Assert.AreEqual(BitConverter.DoubleToInt64Bits(z),BitConverter.DoubleToInt64Bits(empty.FindPath(new NavPoint(3,2),new NavPoint(0,z))[0].Z));
        }

        [Test]public void OwnerDomainAndTypedProfileCachesStayIndependent()
        {
            var domainType=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
            object New()=>Activator.CreateInstance(domainType,Private,null,new object[]{PlayableProfile.Default,1L,false},null);
            var d=New();var g=new NavGeometry(181,new[]{new NavObstacle(-1,-3,1,3)},1);var p=Profile();SharedFlowRouter previous=null;long total=0;
            foreach(var owner in Enumerable.Range(0,8).Select(i=>(PlayableOwner)i)){
                var router=OwnerRouter(d,g,p,owner);Assert.AreNotSame(previous,router);Assert.Zero(router.PreparedGeometryBytes);Free(router,180,180);Assert.Greater(router.PreparedGeometryBytes,0);total+=router.PreparedGeometryBytes;Assert.AreSame(router,OwnerRouter(d,g,p,owner));previous=router;
            }
            Assert.LessOrEqual(total,8L*SharedFlowRouter.ObserverPreparationPayloadLimit);
            Assert.Zero(OwnerRouter(New(),g,p,PlayableOwner.Player).PreparedGeometryBytes);
            Assert.Zero(OwnerRouter(d,g,Profile(.72),PlayableOwner.Player).PreparedGeometryBytes);
            Assert.Zero(OwnerRouter(d,new NavGeometry(181,new[]{new NavObstacle(-2,-3,2,3)},2),p,PlayableOwner.Player).PreparedGeometryBytes);
        }

        [Test]public void CurrentWorldRoundTripIgnoresDerivedTilesAndRestoresCold()
        {
            var map=new CrimsonGroveMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var c=map.Configuration(p,23102026,Enumerable.Range(0,8).Select(i=>new OfflineParticipant("seat-"+i,i+1,i+1,OfflineControl.Ai)).ToArray());
            var a=new PlayableAuthorityTick(c,1);PlayableAuthorityTick restored=null;
            try{
                var before=a.CaptureBytes();var domain=typeof(PlayableAuthorityTick).GetField("domain",Private).GetValue(a);var router=OwnerRouter(domain,a.NavigationGeometry,p.Navigation,PlayableOwner.Player);Free(router,180,180);Assert.Greater(router.PreparedGeometryBytes,0);
                CollectionAssert.AreEqual(before,a.CaptureBytes(),"Memoized predicates carry no persisted authority state");
                restored=PlayableAuthorityTick.RestoreBytes(before,c);CollectionAssert.AreEqual(before,restored.CaptureBytes());
                var cold=OwnerRouter(typeof(PlayableAuthorityTick).GetField("domain",Private).GetValue(restored),restored.NavigationGeometry,p.Navigation,PlayableOwner.Player);Assert.Zero(cold.PreparedGeometryBytes);Free(cold,180,180);CollectionAssert.AreEqual(before,restored.CaptureBytes());
            }finally{restored?.Stop();a.Stop();}
        }

        [Test]public void ActualCrimsonGroveProofsMatchOriginalSolverAndRemainFreshObjects()
        {
            var map=new CrimsonGroveMap();var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var c=map.Configuration(p,23102026,Enumerable.Range(0,8).Select(i=>new OfflineParticipant("seat-"+i,i+1,i+1,OfflineControl.Ai)).ToArray());
            var a=new PlayableAuthorityTick(c,1);
            try{
                var domain=typeof(PlayableAuthorityTick).GetField("domain",Private).GetValue(a);var view=a.ParticipantView("seat-0");var o=PlayableAiObservation.From(view);
                var requests=AiScoutPlanner.RouteRequests(o.Entities,o.PublicScoutObjectives,o.Owner,o.Generation,o.Tick,p);Assert.Greater(requests.Length,0);
                var method=domain.GetType().GetMethod("PlayerSnapshotWithRoutes",BindingFlags.Instance|BindingFlags.NonPublic);
                PlayableSnapshot Project(long sequence)=>(PlayableSnapshot)method.Invoke(domain,new object[]{sequence,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,23102026,o.Owner,requests});
                var warm=Project(1);var again=Project(2);Assert.Greater(warm.RouteProofs.Count,0);
                Assert.AreEqual(Newtonsoft.Json.JsonConvert.SerializeObject(warm.RouteProofs),Newtonsoft.Json.JsonConvert.SerializeObject(again.RouteProofs));
                for(int i=0;i<warm.RouteProofs.Count;i++)Assert.AreNotSame(warm.RouteProofs[i],again.RouteProofs[i],"Reuse predicates, never a prior proof object");
                // Replace only derived routers with the pre-change constructor on the exact same world/requests.
                var table=domain.GetType().GetField("observedRouteCaches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var lookup=new object[]{domain,null};Assert.True((bool)table.GetType().GetMethod("TryGetValue").Invoke(table,lookup));
                foreach(System.Collections.IList pool in ((System.Collections.IDictionary)lookup[1]).Values)foreach(var entry in pool){
                    object Get(string name)=>entry.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(entry);
                    var basis=(NavigationProfile)Get("Profile");var original=new SharedFlowRouter((NavGeometry)Get("Geometry"),basis.ForUnit((double)Get("Radius"),(double)Get("Speed"),(double)Get("Turn")),null);
                    entry.GetType().GetField("Router",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(entry,original);
                }
                var control=Project(3);Assert.AreEqual(Newtonsoft.Json.JsonConvert.SerializeObject(control.RouteProofs),Newtonsoft.Json.JsonConvert.SerializeObject(warm.RouteProofs),"Every current proof binding and path must match the original evaluator on identical inputs");
            }finally{a.Stop();}
        }

        [Test]public void LastLookupKeepsWarmDirectedAnswersInTheSameTile()
        {
            var g=new NavGeometry(181,new[]{new NavObstacle(-1,-3,1,3)},1);var p=Profile();var actual=Tiled(g,p);var control=new SharedFlowRouter(g,p);
            Free(actual,170,170);Edge(actual,170,170,171,170);long cells=actual.PreparedCellComputations,edges=actual.PreparedEdgeComputations;
            var prep=typeof(SharedFlowRouter).GetField("preparation",Private).GetValue(actual);var last=prep.GetType().GetField("lastTile",Private);var storage=last.GetValue(prep);Assert.NotNull(storage);
            for(int i=0;i<32;i++){Assert.AreEqual(Free(control,170,170),Free(actual,170,170));Assert.AreEqual(Edge(control,170,170,171,170),Edge(actual,170,170,171,170));Assert.AreSame(storage,last.GetValue(prep));}
            Assert.AreEqual(cells,actual.PreparedCellComputations);Assert.AreEqual(edges,actual.PreparedEdgeComputations);
            Assert.AreEqual(Edge(control,171,170,170,170),Edge(actual,171,170,170,170));Assert.AreEqual(edges+1,actual.PreparedEdgeComputations,"Reverse direction remains independent");
        }

        [Test]public void LastLookupDoesNotPromoteOldestFifoTileOrAliasRecycledStorage()
        {
            var g=new NavGeometry(2100,new[]{new NavObstacle(-2099,-2099,-2097,-2097)},1);var actual=Tiled(g,Profile());var control=new SharedFlowRouter(g,Profile());Assert.False(Free(actual,1,1));
            for(int x=1;x<128;x++)Assert.AreEqual(Free(control,x*32,1),Free(actual,x*32,1));
            var prep=typeof(SharedFlowRouter).GetField("preparation",Private).GetValue(actual);
            int OldestX(){var queue=(System.Collections.IEnumerable)prep.GetType().GetField("tileOrder",Private).GetValue(prep);var e=queue.GetEnumerator();Assert.True(e.MoveNext());return (int)CellType.GetField("X").GetValue(e.Current);}
            Assert.Zero(OldestX());Assert.False(Free(actual,1,1));Assert.Zero(OldestX(),"Warm lookup cannot promote FIFO position");
            long before=actual.PreparedCellComputations;Assert.AreEqual(Free(control,4096,1),Free(actual,4096,1));Assert.AreEqual(1,OldestX(),"Oldest tile is evicted even when it was the most recent lookup");
            Assert.False(Free(actual,1,1),"Recycled free tile cannot alias the old blocked tile");Assert.AreEqual(before+2,actual.PreparedCellComputations);
            Assert.AreEqual(SharedFlowRouter.ObserverPreparationPayloadLimit,actual.PreparedGeometryBytes);
            for(int i=0;i<32;i++){int x=(i%2==0?4096:1);Assert.AreEqual(Free(control,x,1),Free(actual,x,1));Assert.AreEqual(Edge(control,x,1,x+1,1),Edge(actual,x,1,x+1,1));}
        }

        [Test]public void LastLookupPreservesBoundsAndAllocationUnavailableFallback()
        {
            var g=new NavGeometry(181,new[]{new NavObstacle(-1,-3,1,3)},1);var p=Profile();var actual=Tiled(g,p);var control=new SharedFlowRouter(g,p);Free(actual,170,170);
            var prep=typeof(SharedFlowRouter).GetField("preparation",Private).GetValue(actual);var last=prep.GetType().GetField("lastTile",Private);var storage=last.GetValue(prep);prep.GetType().GetField("allocationUnavailable",Private).SetValue(prep,true);
            int bytes=actual.PreparedGeometryBytes;long cells=actual.PreparedCellComputations;
            foreach(int x in new[]{170,10,-2,int.MinValue,int.MaxValue,364})Assert.AreEqual(Free(control,x,170),Free(actual,x,170));
            Assert.AreEqual(bytes,actual.PreparedGeometryBytes);Assert.AreEqual(cells,actual.PreparedCellComputations,"Warm hits and uncached fallbacks cannot allocate or install new answers");Assert.AreSame(storage,last.GetValue(prep));
            var changed=actual.ForGeometry(new NavGeometry(181,Array.Empty<NavObstacle>(),2));Assert.Zero(changed.PreparedGeometryBytes);var next=typeof(SharedFlowRouter).GetField("preparation",Private).GetValue(changed);Assert.IsNull(last.GetValue(next),"New geometry starts without a retained lookup");
        }

        public static int RunSelected()
        {
            int passed=0,failed=0;var instance=new ObserverGeometryTileTests();
            foreach(var method in typeof(ObserverGeometryTileTests).GetMethods().Where(m=>m.IsDefined(typeof(TestAttribute),false)).OrderBy(m=>m.Name)){
                var clock=System.Diagnostics.Stopwatch.StartNew();try{method.Invoke(instance,null);passed++;Console.WriteLine("CHECK_PASS "+method.Name+" ms="+clock.Elapsed.TotalMilliseconds);}
                catch(TargetInvocationException e){failed++;Console.WriteLine("CHECK_FAIL "+method.Name+" "+e.InnerException);}
            }
            Console.WriteLine("CHECK_RESULT passed="+passed+" failed="+failed);return failed==0?0:1;
        }
    }
}

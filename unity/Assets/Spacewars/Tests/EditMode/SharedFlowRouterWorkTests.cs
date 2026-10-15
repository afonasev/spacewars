using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class SharedFlowRouterWorkTests
    {
        [Test]public void PreparedPredicatesKeepExactPathsForEveryProducedFootprintAndCancelCleanly()
        {
            var p=PlayableProfile.Default;var g=new NavGeometry(12,new[]{new NavObstacle(-.8,-6,.8,6)},1);
            foreach(PlayableEntityKind kind in Enum.GetValues(typeof(PlayableEntityKind))){
                var profile=p.Navigation.ForUnit(PlayableUnitRules.Radius(p,kind),PlayableUnitRules.Speed(p,kind),PlayableUnitRules.Turn(p,kind));
                var router=new SharedFlowRouter(g,profile);router.PrepareGridPredicates();long checks=router.GridGeometryChecks;
                var a=new NavPoint(-6,-3);var b=new NavPoint(6,3);
                CollectionAssert.AreEqual(new SharedFlowRouterReference(g,profile).FindPath(a,b),router.FindPath(a,b));
                Assert.AreEqual(checks,router.GridGeometryChecks,"Live search must reuse all exact grid predicates.");
                var cancelled=new SharedFlowRouter(g,profile);cancelled.PrepareGridPredicates(()=>true);Assert.Zero(cancelled.GridGeometryChecks);
                CollectionAssert.AreEqual(new SharedFlowRouterReference(g,profile).FindPath(a,b),cancelled.FindPath(a,b));
            }
        }
        [TestCase(false)][TestCase(true)] public void ClosedReachabilityAvoidsRepeatedUnreachableFloodsWithoutChangingPaths(bool sparse)
        {
            var p=NavigationProfile.Default;var geometry=new NavGeometry(12,new[]{new NavObstacle(-.8,-12,.8,12)},1);
            var router=new SharedFlowRouter(geometry,p);var reference=new SharedFlowRouterReference(geometry,p);
            if(sparse)typeof(SharedFlowRouter).GetField("denseWidth",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(router,0);
            for(int i=0;i<24;i++){
                router.TrimFields(6);var start=new NavPoint(-6,-4+i*.1);var goal=new NavPoint(5+i%4,-8+i*.6);
                CollectionAssert.AreEqual(reference.FindPath(start,goal),router.FindPath(start,goal));
                Assert.IsEmpty(router.FindPath(start,goal));
            }
            int width=(int)Math.Ceiling(24/p.GridCell);
            Assert.LessOrEqual(router.ExpandedCells,(long)width*width,"One complete closure replaces repeated candidate floods.");
            Assert.Greater(router.ClosedReachabilityHits,0);
            var openedGeometry=new NavGeometry(12,new[]{new NavObstacle(-.8,-5,.8,5)},2);var opened=router.ForGeometry(openedGeometry);
            var a=new NavPoint(-6,-3);var b=new NavPoint(6,3);var route=opened.FindPath(a,b);
            CollectionAssert.AreEqual(new SharedFlowRouterReference(openedGeometry,p).FindPath(a,b),route);
            Assert.IsNotEmpty(route,"A changed geometry must discard the old negative witness.");
        }
        [Test] public void DetachedStartupExecutionKeepsLiveWorldAiClockAndMailboxesUnchanged()
        {
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=-1875503562};setup.InitializeParticipants();
            var config=NativeLobbyMatch.Create(PlayableProfile.ThreeCrossingsDefault,setup);
            var live=new PlayableAuthorityTick(config,71,asynchronousRoutes:true);var pristine=new PlayableAuthorityTick(config,71,asynchronousRoutes:true);
            var before=live.CaptureBytes();
            Assert.Zero(live.PrepareStartupExecution(()=>true));
            Assert.GreaterOrEqual(live.PrepareStartupExecution(),45);
            CollectionAssert.AreEqual(before,live.CaptureBytes());
            Assert.Zero(live.Tick);Assert.Zero(live.Requests.Count);Assert.Zero(live.Answers.Count);
            for(int tick=0;tick<90;tick++){
                Assert.True(live.TryAdvance());Assert.True(pristine.TryAdvance());
                CollectionAssert.AreEqual(pristine.CaptureBytes(),live.CaptureBytes(),"first live policy and delivery at "+tick);
            }
        }
        [Test] public void InitialNavigationPreparationKeepsWorldBytesClocksReceiptsAndAiUnchanged()
        {
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new ThreeCrossingsMap(new ThreeCrossingsProfileData()));
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=-1875503562};setup.InitializeParticipants();
            var config=NativeLobbyMatch.Create(p,setup);var cycle=new PlayableAuthorityTick(config,71);var pristine=new PlayableAuthorityTick(config,71);
            var before=cycle.CaptureBytes();cycle.PrepareNavigationViews();
            var domain=typeof(PlayableAuthorityTick).GetField("domain",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var difference=typeof(OfflineParticipantRestoreTests).GetMethod("AuthorityDiff",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{domain.GetValue(pristine),domain.GetValue(cycle),"domain",0});
            TestContext.WriteLine("PREPARE_STATE_DIFF="+difference);
            CollectionAssert.AreEqual(before,cycle.CaptureBytes());Assert.Zero(cycle.Tick);Assert.Zero(cycle.Requests.Count);Assert.Zero(cycle.Answers.Count);
        }
        [Test] public void LazyFieldsAndEvictionKeepExactCompleteFieldRoutes()
        {
            var p=NavigationProfile.Default;
            var shapes=new[]{new NavObstacle(-.8,-6,.8,6),new NavObstacle(new NavPoint(0,0),2),
                new NavObstacle(new NavPolygon(new[]{new NavPoint(-2,-4),new NavPoint(2,-3),new NavPoint(1,5),new NavPoint(-1,4)})),new NavObstacle(-.8,-10,.8,10)};
            foreach(var shape in shapes)
            {
                var g=new NavGeometry(10,new[]{shape},1);var reference=new SharedFlowRouterReference(g,p);var actual=new SharedFlowRouter(g,p);
                for(int i=0;i<48;i++)
                {
                    var start=new NavPoint(i%2==0?-7:7,-7+(i%15));var goal=new NavPoint(i%3==0?-6:6,6-(i%13)*.5);
                    CollectionAssert.AreEqual(reference.FindPath(start,goal),actual.FindPath(start,goal),"exact path shape="+Array.IndexOf(shapes,shape)+" query="+i);
                    if(i%7==6)actual.TrimFields(2);
                }
            }
        }
        [Test] public void NearbyBlockedRouteDoesNotFloodUnneededArenaAndContinuesForFarOrigin()
        {
            var g=new NavGeometry(64,new[]{new NavObstacle(-1,-3,1,3)},1);var p=NavigationProfile.Default;
            var actual=new SharedFlowRouter(g,p);var reference=new SharedFlowRouterReference(g,p);
            var goal=new NavPoint(3,0);var start=new NavPoint(-3,0);
            CollectionAssert.AreEqual(reference.FindPath(start,goal),actual.FindPath(start,goal));
            Assert.Less(actual.ExpandedCells,reference.ExpandedCells/10,"Nearby detour must stop after shortest distances are known.");
            long expanded=actual.ExpandedCells;
            start=new NavPoint(-58,55);CollectionAssert.AreEqual(reference.FindPath(start,goal),actual.FindPath(start,goal));
            Assert.Greater(actual.ExpandedCells,expanded,"A farther origin resumes the original BFS frontier.");
            actual.TrimFields(0);long checks=actual.GridGeometryChecks;
            CollectionAssert.AreEqual(reference.FindPath(new NavPoint(-3,0),goal),actual.FindPath(new NavPoint(-3,0),goal));
            Assert.AreEqual(checks,actual.GridGeometryChecks,"Evicting distance fields must retain immutable grid predicates.");
        }
        [Test] public void GeometryChangesClearFieldsAndKeepExactNewPathsWithUnaffectedPredicates()
        {
            var p=NavigationProfile.Default;var wall=new NavObstacle(-1,-12,1,12);var baseGeometry=new NavGeometry(20,new[]{wall},1);
            var warm=new SharedFlowRouter(baseGeometry,p);var origin=new NavPoint(-15,-10);var goal=new NavPoint(15,10);warm.FindPath(origin,goal);
            var shapes=new[]{new NavObstacle(new NavPoint(18,-18),1),new NavObstacle(-4,0,-2,5),new NavObstacle(new NavPolygon(new[]{new NavPoint(-5,-6),new NavPoint(-2,-5),new NavPoint(-3,-2)})),new NavObstacle(-1e200,-1e200,1e200,1e200)};
            foreach(var extra in shapes){
                var geometry=new NavGeometry(20,new[]{wall,extra},2);var reused=warm.ForGeometry(geometry);Assert.Zero(reused.CachedFieldCount,"Distance fields must never survive a geometry change.");
                var reference=new SharedFlowRouterReference(geometry,p);
                for(int i=0;i<8;i++)CollectionAssert.AreEqual(reference.FindPath(new NavPoint(origin.X+i*.1,origin.Z),goal),reused.FindPath(new NavPoint(origin.X+i*.1,origin.Z),goal));
                var removed=reused.ForGeometry(baseGeometry);CollectionAssert.AreEqual(new SharedFlowRouterReference(baseGeometry,p).FindPath(origin,goal),removed.FindPath(origin,goal));
            }
            var change=new NavGeometry(20,new[]{wall,shapes[0]},2);var inherited=warm.ForGeometry(change);var cold=new SharedFlowRouter(change,p);CollectionAssert.AreEqual(cold.FindPath(origin,goal),inherited.FindPath(origin,goal));
            Assert.Less(inherited.GridGeometryChecks,cold.GridGeometryChecks/10,"Remote geometry change must not rescan the whole unchanged arena.");
            var changedExtent=new NavGeometry(21,new[]{wall},3);CollectionAssert.AreEqual(new SharedFlowRouterReference(changedExtent,p).FindPath(origin,goal),warm.ForGeometry(changedExtent).FindPath(origin,goal));
        }
        [Test] public void RepeatedExactPathsAreDetachedBitExactAndBounded()
        {
            var p=NavigationProfile.Default;var g=new NavGeometry(8,new[]{new NavObstacle(-.8,-5,.8,5)},1);var router=new SharedFlowRouter(g,p);var start=new NavPoint(-5,0);var goal=new NavPoint(5,0);
            var expected=new SharedFlowRouterReference(g,p).FindPath(start,goal);var first=router.FindPath(start,goal);CollectionAssert.AreEqual(expected,first);first[0]=new NavPoint(123,456);
            CollectionAssert.AreEqual(expected,router.FindPath(start,goal));Assert.Greater(router.PathCacheHits,0);
            for(int i=0;i<100;i++)router.FindPath(new NavPoint(-5,i*.01),new NavPoint(5,-i*.01));
            Assert.LessOrEqual(router.CachedPathCount,64);Assert.LessOrEqual(router.CachedPathPoints,8192);
            var empty=new SharedFlowRouter(new NavGeometry(8,Array.Empty<NavObstacle>(),1),p);
            foreach(double z in new[]{0d,BitConverter.Int64BitsToDouble(long.MinValue),0d})Assert.AreEqual(BitConverter.DoubleToInt64Bits(z),BitConverter.DoubleToInt64Bits(empty.FindPath(new NavPoint(1,1),new NavPoint(0,z))[0].Z));
        }
        [Test] public void CrossingsPublicObjectivesKeepExactPathsWithWarmGeometryAfterFieldEviction()
        {
            var map=new ThreeCrossingsMap(new ThreeCrossingsProfileData());
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var g=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),1);
            var nav=p.Navigation.ForUnit(PlayableUnitRules.Radius(p,PlayableEntityKind.Explorer),4,4.5);
            var actual=new SharedFlowRouter(g,nav);var reference=new SharedFlowRouterReference(g,nav);
            var goals=map.Sites(p).Select(s=>s.Position).Concat(new[]{new NavPoint(-46,-46),new NavPoint(46,46)}).ToArray();
            double oldMs=0,newMs=0;long firstChecks=0,oldExpanded=0;
            for(int pass=0;pass<2;pass++)
            {
                for(int i=0;i<goals.Length;i++)
                {
                    if(actual.CachedFieldCount>=7)actual.TrimFields(6);
                    if(reference.CachedFieldCount>=7)reference=new SharedFlowRouterReference(g,nav);
                    var start=new NavPoint(46,-44+pass*.1);long before=reference.ExpandedCells;var watch=Stopwatch.StartNew();var expected=reference.FindPath(start,goals[i]);oldMs+=watch.Elapsed.TotalMilliseconds;oldExpanded+=reference.ExpandedCells-before;
                    watch.Restart();var path=actual.FindPath(start,goals[i]);newMs+=watch.Elapsed.TotalMilliseconds;
                    CollectionAssert.AreEqual(expected,path,"public objective "+i+" pass "+pass);
                    var from=start;foreach(var point in path){Assert.True(g.SegmentFree(from,point,nav.Radius));from=point;}
                    Assert.LessOrEqual(actual.CachedFieldCount,8);
                }
                if(pass==0)firstChecks=actual.GridGeometryChecks;
            }
            TestContext.WriteLine("CROSSINGS_FLOW old_ms="+oldMs+" new_ms="+newMs+" geometry_checks_first="+firstChecks+" total="+actual.GridGeometryChecks+" expanded_old="+oldExpanded+" new="+actual.ExpandedCells);
            Assert.Less(actual.ExpandedCells,oldExpanded);
        }
    }
}

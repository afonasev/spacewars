using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class ObservedRouteRouterTests
    {
        [Test]public void ReportPreparedFrontierCostWithoutThreshold()
        {
            var geometry=new NavGeometry(100,new[]{new NavObstacle(-.8,-60,.8,60)},1);
            var profile=NavigationProfile.Default.ForUnit(.72,4,4.5);
            var router=new SharedFlowRouter(geometry,profile,null);router.PrepareGridPredicates();
            router.FindPath(new NavPoint(-70,-21),new NavPoint(70,21));long expanded=router.ExpandedCells;int ready=0;
            var watch=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<100;i++){router.TrimFields(0);if(router.FindPath(new NavPoint(-70-i*.002,-20+i*.4),new NavPoint(70,20-i*.4)).Length>0)ready++;}
            watch.Stop();Assert.AreEqual(100,ready);Assert.Greater(router.ExpandedCells-expanded,1000000);
            TestContext.WriteLine("PREPARED_FRONTIER_100 ms="+watch.Elapsed.TotalMilliseconds+" expanded="+(router.ExpandedCells-expanded)+" ready="+ready+" payload="+router.PreparedGeometryBytes);
        }
        [Test]public void PreparedFrontierDistanceAllocationFailureKeepsSparsePathsAndWork()
        {
            var geometry=Geometry(new NavObstacle(-.8,-5,.8,5));var profile=NavigationProfile.Default;
            var dense=new SharedFlowRouter(geometry,profile,null);var fallback=new SharedFlowRouter(geometry,profile,null);
            dense.PrepareGridPredicates();fallback.PrepareGridPredicates();int refused=0;
            try{
                for(int i=0;i<12;i++){
                    var from=new NavPoint(-5-i*.01,-3);var to=new NavPoint(5,-4+i*.5);
                    var expected=dense.FindPath(from,to);SetRouterTestSeam("testDistanceAllocator",(Func<int,int[]>)(n=>{refused++;throw new OutOfMemoryException("Prepared frontier test allocation refusal");}));
                    var actual=fallback.FindPath(from,to);SetRouterTestSeam("testDistanceAllocator",null);
                    CollectionAssert.AreEqual(expected,actual);Assert.AreEqual(dense.ExpandedCells,fallback.ExpandedCells);Assert.AreEqual(dense.CachedCells,fallback.CachedCells);
                    Assert.AreEqual(dense.PreparedGeometryBytes,fallback.PreparedGeometryBytes);Assert.AreEqual(dense.PreparedCellComputations,fallback.PreparedCellComputations);Assert.AreEqual(dense.PreparedEdgeComputations,fallback.PreparedEdgeComputations);
                }
                Assert.Greater(refused,0);
            }finally{SetRouterTestSeam("testDistanceAllocator",null);}
        }
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        object New()=>Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,1L,false},null);
        SharedFlowRouter Router(object d,NavGeometry g,NavigationProfile p,double radius=.3,PlayableOwner owner=PlayableOwner.Player)=>(SharedFlowRouter)D.GetMethod("ObservedRouter",F).Invoke(d,new object[]{owner,g,p,radius,4d,4.5d});
        // This ceiling was explicitly changed by 4a1e0271ec for the current
        // observer working set. Keep it independent of the production constant.
        private const int ObserverFieldLimit=64;
        private static System.Collections.IDictionary ObserverCaches(object domain)
        {
            var pool=D.GetField("observedPathPool",F).GetValue(domain);
            return (System.Collections.IDictionary)typeof(ObservedPathWorkPool).GetField("caches",F).GetValue(pool);
        }
        static NavGeometry Geometry(params NavObstacle[] solids)=>new NavGeometry(8,solids,1);
        static readonly Type CellType=typeof(SharedFlowRouter).GetNestedType("Cell",BindingFlags.NonPublic);
        static object Cell(int x,int z)=>Activator.CreateInstance(CellType,new object[]{x,z});
        static bool FreeCell(SharedFlowRouter router,int x,int z)=>(bool)typeof(SharedFlowRouter).GetMethod("IsFree",F).Invoke(router,new[]{Cell(x,z)});
        static bool FreeEdge(SharedFlowRouter router,int x,int z,int toX,int toZ)=>(bool)typeof(SharedFlowRouter).GetMethod("GridSegmentFree",F).Invoke(router,new[]{Cell(x,z),Cell(toX,toZ)});
        static object Preparation(SharedFlowRouter router)=>typeof(SharedFlowRouter).GetField("preparation",F).GetValue(router);
        SharedFlowRouter TypedRouter(object d,NavGeometry g,NavigationProfile p,double radius=.3,double speed=4,double turn=4.5,PlayableOwner owner=PlayableOwner.Player)=>(SharedFlowRouter)D.GetMethod("ObservedRouter",F).Invoke(d,new object[]{owner,g,p,radius,speed,turn});
        [TestCase("empty")][TestCase("rectangle")][TestCase("circle")][TestCase("polygon")]
        public void ObserverPreparation_ExactCellsDirectedEdgesAndWarmReuseMatchActualUnprepared(string shape)
        {
            var solids=shape=="empty"?Array.Empty<NavObstacle>():shape=="rectangle"?new[]{new NavObstacle(-1,-2,1,2)}:shape=="circle"?new[]{new NavObstacle(new NavPoint(0,0),1)}:new[]{new NavObstacle(new NavPolygon(new[]{new NavPoint(-1,-2),new NavPoint(1,-1),new NavPoint(.7,2),new NavPoint(-1,1)}))};
            var geometry=Geometry(solids);
            foreach(double radius in new[]{.3,.72})foreach(double grid in new[]{.7,1.2})
            {
                var profile=new NavigationProfile("prepared-query",1,radius,4,4.5,grid,.15,3.6,.08);
                var original=new SharedFlowRouter(geometry,profile);var prepared=new SharedFlowRouter(geometry,profile,null);
                Assert.Zero(prepared.PreparedGeometryBytes);Assert.IsNull(Preparation(original));
                int center=(int)Math.Floor(geometry.HalfExtent/grid),side=(int)Math.Ceiling(2*geometry.HalfExtent/grid)+2;
                var coordinates=new[]{-2,-1,0,1,center-2,center-1,center,center+1,center+2,side-2,side-1,side}.Distinct().ToArray();
                foreach(int x in coordinates)foreach(int z in coordinates)
                {
                    Assert.AreEqual(FreeCell(original,x,z),FreeCell(prepared,x,z),"actual original cell predicate "+x+","+z);
                    foreach(var delta in new[]{new[]{0,1},new[]{1,0},new[]{0,-1},new[]{-1,0}})
                        Assert.AreEqual(FreeEdge(original,x,z,x+delta[0],z+delta[1]),FreeEdge(prepared,x,z,x+delta[0],z+delta[1]),"actual original directed edge");
                }
                long cells=prepared.PreparedCellComputations,edges=prepared.PreparedEdgeComputations;
                for(int n=0;n<4;n++){Assert.AreEqual(FreeCell(original,center,center),FreeCell(prepared,center,center));Assert.AreEqual(FreeEdge(original,center,center,center+1,center),FreeEdge(prepared,center,center,center+1,center));}
                Assert.AreEqual(cells,prepared.PreparedCellComputations);Assert.AreEqual(edges,prepared.PreparedEdgeComputations,"warm query uses its exact immutable answer");
                // Opposite directions have independent answers; no symmetry shortcut.
                var directed=new SharedFlowRouter(geometry,profile,null);
                Assert.AreEqual(FreeEdge(original,center,center,center+1,center),FreeEdge(directed,center,center,center+1,center));Assert.AreEqual(1,directed.PreparedEdgeComputations);
                Assert.AreEqual(FreeEdge(original,center+1,center,center,center),FreeEdge(directed,center+1,center,center,center));Assert.AreEqual(2,directed.PreparedEdgeComputations);
                Assert.AreEqual(FreeEdge(original,center,center,center+3,center+2),FreeEdge(directed,center,center,center+3,center+2));Assert.AreEqual(2,directed.PreparedEdgeComputations,"nonadjacent segment remains original predicate");
                Assert.AreEqual(side,prepared.PreparedGeometrySide);Assert.AreEqual(side*side,prepared.PreparedGeometryBytes);Assert.LessOrEqual(prepared.PreparedGeometryBytes,SharedFlowRouter.ObserverPreparationPayloadLimit);
            }
        }
        [Test]public void ObserverPreparation_All243FiveTritStatesPreserveIndependentAnswers()
        {
            var type=typeof(SharedFlowRouter).GetNestedType("GeometryPreparation",BindingFlags.NonPublic);
            var read=type.GetMethod("Trit",BindingFlags.Static|BindingFlags.NonPublic);var write=type.GetMethod("WithTrit",BindingFlags.Static|BindingFlags.NonPublic);
            int[] powers={1,3,9,27,81};
            for(int state=0;state<243;state++)foreach(int power in powers)for(int answer=0;answer<3;answer++)
            {
                byte next=(byte)write.Invoke(null,new object[]{(byte)state,power,answer});Assert.Less(next,243);
                foreach(int other in powers)Assert.AreEqual(other==power?answer:state/other%3,read.Invoke(null,new object[]{next,other}),"state="+state+" selected="+power+" checked="+other);
            }
        }
        [Test]public void ObserverPreparation_PathsLazyFieldCountersAndEvictionsMatchActualUnprepared()
        {
            var d=New();var basis=NavigationProfile.Default;var geometry=Geometry(new NavObstacle(-.8,-5,.8,5));var unit=basis.ForUnit(.3,4,4.5);
            var original=new SharedFlowRouter(geometry,unit);SharedFlowRouter previous=null;object firstPreparation=null;int resets=0;
            const int limit=ObserverFieldLimit;
            for(int i=0;i<96;i++)
            {
                bool reset=original.CachedFieldCount>=limit-1;if(reset)original.TrimFields(limit-2);
                var prepared=Router(d,geometry,basis);if(firstPreparation==null)firstPreparation=Preparation(prepared);
                if(reset){Assert.AreSame(previous,prepared);Assert.AreSame(firstPreparation,Preparation(prepared));resets++;}
                var start=new NavPoint(-5-(i%3)*.2,-3);var goal=new NavPoint(2+(i%7)*.8,-6+(i/7)*.8);
                CollectionAssert.AreEqual(original.FindPath(start,goal),prepared.FindPath(start,goal),"complete actual original path "+i);
                Assert.AreEqual(original.RebuildCount,prepared.RebuildCount);Assert.AreEqual(original.FieldCacheHits,prepared.FieldCacheHits);Assert.AreEqual(original.CachedFieldCount,prepared.CachedFieldCount);
                Assert.AreEqual(original.ExpandedCells,prepared.ExpandedCells);Assert.AreEqual(original.CachedCells,prepared.CachedCells);Assert.LessOrEqual(prepared.CachedFieldCount,limit);Assert.LessOrEqual(prepared.PreparedGeometryBytes,SharedFlowRouter.ObserverPreparationPayloadLimit);previous=prepared;
            }
            Assert.Greater(resets,0,"the bounded lazy-field eviction boundary is exercised");
            Assert.False(typeof(SharedFlowRouter).GetFields(F).Any(f=>typeof(SharedFlowRouter).IsAssignableFrom(f.FieldType)),"new router retains no predecessor or old fields");
            Assert.False(firstPreparation.GetType().GetFields(F).Any(f=>typeof(SharedFlowRouter).IsAssignableFrom(f.FieldType)));
        }
        [Test]public void ObserverPreparation_DirectPathsCapsAndNonrepresentableBoundsKeepOriginalFallback()
        {
            var profile=new NavigationProfile("prepared-cap",1,.3,4,4.5,1,.15,3.6,.08);var empty=Geometry();var direct=new SharedFlowRouter(empty,profile,null);
            CollectionAssert.AreEqual(new SharedFlowRouter(empty,profile).FindPath(new NavPoint(-3,0),new NavPoint(3,0)),direct.FindPath(new NavPoint(-3,0),new NavPoint(3,0)));Assert.Zero(direct.PreparedGeometryBytes,"no table for direct path");Assert.Zero(direct.PreparedGeometrySide);
            var capGeometry=new NavGeometry(180,Array.Empty<NavObstacle>(),1);var capped=new SharedFlowRouter(capGeometry,profile,null);var capOriginal=new SharedFlowRouter(capGeometry,profile);
            Assert.AreEqual(FreeCell(capOriginal,1,1),FreeCell(capped,1,1));Assert.AreEqual(362,capped.PreparedGeometrySide);Assert.AreEqual(131044,capped.PreparedGeometryBytes,"largest square within approved128KiB payload cap");Assert.LessOrEqual(capped.PreparedGeometryBytes,128*1024);
            foreach(double half in new[]{180.5d,1e308})
            {
                var huge=new NavGeometry(half,Array.Empty<NavObstacle>(),1);var prepared=new SharedFlowRouter(huge,profile,null);var original=new SharedFlowRouter(huge,profile);
                foreach(int x in new[]{int.MinValue,-2,-1,0,1,int.MaxValue}){Assert.AreEqual(FreeCell(original,x,0),FreeCell(prepared,x,0));Assert.AreEqual(FreeEdge(original,x,0,x,1),FreeEdge(prepared,x,0,x,1));}
                Assert.Zero(prepared.PreparedGeometryBytes);Assert.Zero(prepared.PreparedGeometrySide);Assert.Zero(prepared.PreparedCellComputations);Assert.Zero(prepared.PreparedEdgeComputations);
            }
        }
        [Test]public void ObserverPreparation_OriginalOwnerDomainShapeProfileAndUnitInvalidationStartsCold()
        {
            var d=New();var other=New();var profile=NavigationProfile.Default;var geometry=Geometry(new NavObstacle(new NavPoint(0,0),1));
            var first=TypedRouter(d,geometry,profile);FreeCell(first,1,1);var prepared=Preparation(first);Assert.Greater(first.PreparedGeometryBytes,0);
            Assert.AreSame(first,TypedRouter(d,Geometry(new NavObstacle(new NavPoint(0,0),1)),profile));
            var changed=new[]{TypedRouter(d,Geometry(new NavObstacle(-1,-1,1,1)),profile),TypedRouter(d,new NavGeometry(9,geometry.Obstacles.ToArray(),1),profile),TypedRouter(d,Geometry(),profile),TypedRouter(other,geometry,profile),TypedRouter(d,geometry,profile,owner:PlayableOwner.Enemy),TypedRouter(d,geometry,new NavigationProfile()),TypedRouter(d,geometry,profile,radius:.72),TypedRouter(d,geometry,profile,speed:4.2),TypedRouter(d,geometry,profile,turn:4.7)};
            foreach(var router in changed){Assert.AreNotSame(prepared,Preparation(router));Assert.Zero(router.PreparedGeometryBytes);Assert.Zero(router.PreparedCellComputations);Assert.Zero(router.PreparedEdgeComputations);}
            ObserverCaches(d).Clear();Assert.AreNotSame(prepared,Preparation(TypedRouter(d,geometry,profile)),"cold domain state has no persisted preparation");
        }
        [Test]public void ObserverPreparation_EightOwnerPayloadAndFieldBudgetsRemainBounded()
        {
            var d=New();var profile=PlayableProfile.Default;var geometry=new NavGeometry(profile.ArenaHalfExtent,new[]{new NavObstacle(-.8,-24,.8,24)},1);var rows=new System.Collections.Generic.List<object>();long total=0;
            foreach(var owner in Enumerable.Range(0,8).Select(x=>(PlayableOwner)x))
            {
                var router=TypedRouter(d,geometry,profile.Navigation,owner:owner);var path=router.FindPath(new NavPoint(-20,0),new NavPoint(20,0));Assert.Greater(path.Length,0);
                total+=router.PreparedGeometryBytes;Assert.LessOrEqual(router.PreparedGeometryBytes,SharedFlowRouter.ObserverPreparationPayloadLimit);Assert.LessOrEqual(router.CachedFieldCount,ObserverFieldLimit);
                rows.Add(new{Owner=owner,geometry.HalfExtent,profile.Navigation.GridCell,router.PreparedGeometrySide,router.PreparedGeometryBytes,router.CachedFieldCount,router.PreparedCellComputations,router.PreparedEdgeComputations});
            }
            Assert.LessOrEqual(total,8L*SharedFlowRouter.ObserverPreparationPayloadLimit);
            var root=new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());while(root!=null&&!System.IO.File.Exists(System.IO.Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;Assert.NotNull(root);
            string folder=System.IO.Path.Combine(root.FullName,".local/o6-route-preparation");System.IO.Directory.CreateDirectory(folder);System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"synthetic-memory.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{PayloadCapPerOwner=SharedFlowRouter.ObserverPreparationPayloadLimit,TotalPayloadBytes=total,Owners=rows,Note="Synthetic default32/.8 eight-slot actual path/field/array contracts; headers additional, not real AiTestMap natural peak or performance acceptance"}));
        }
        [Test]public void ObserverPreparation_ActualAiEightMapBytePayloadEligibility()
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,7109,roster);var authority=new PlayableAuthorityTick(config,1);var d=typeof(PlayableAuthorityTick).GetField("domain",F).GetValue(authority);var geometry=(NavGeometry)D.GetProperty("Geometry",F|BindingFlags.Public).GetValue(d);var rows=new System.Collections.Generic.List<object>();long total=0;
            try{
            Assert.AreEqual(144,geometry.HalfExtent);Assert.AreEqual(.8,profile.Navigation.GridCell);
            foreach(var participant in config.Roster)
            {
                var owner=authority.ParticipantView(participant.Id).Owner;var router=TypedRouter(d,geometry,profile.Navigation,owner:owner);
                Assert.Zero(router.PreparedGeometryBytes);FreeCell(router,180,180);FreeEdge(router,180,180,181,180);Assert.AreEqual(362,router.PreparedGeometrySide);Assert.AreEqual(131044,router.PreparedGeometryBytes);
                total+=router.PreparedGeometryBytes;Assert.LessOrEqual(router.PreparedGeometryBytes,SharedFlowRouter.ObserverPreparationPayloadLimit);Assert.LessOrEqual(router.CachedFieldCount,ObserverFieldLimit);
                rows.Add(new{Owner=owner,geometry.HalfExtent,profile.Navigation.GridCell,router.PreparedGeometrySide,router.PreparedGeometryBytes,router.CachedFieldCount,router.PreparedCellComputations,router.PreparedEdgeComputations});
            }
            Assert.AreEqual(1048352,total);Assert.AreEqual(8,rows.Count);Assert.LessOrEqual(total,8L*SharedFlowRouter.ObserverPreparationPayloadLimit);
            var root=new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());while(root!=null&&!System.IO.File.Exists(System.IO.Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;Assert.NotNull(root);
            string folder=System.IO.Path.Combine(root.FullName,".local/o6-route-preparation");System.IO.Directory.CreateDirectory(folder);System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"actual-ai-memory.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{PayloadCapPerOwner=SharedFlowRouter.ObserverPreparationPayloadLimit,TotalPayloadBytes=total,Owners=rows,Note="Actual AiTestMap7109 eight-owner fresh-authority array payload after explicit predicate queries; CLR headers additional; authored allocation preflight, not natural peak or performance acceptance"}));
            }finally{authority.Stop();}
        }
        [Test]public void VisibleScopeRejectsOnlyDisconnectedSampledPathsAndPreservesSmallGaps()
        {
            Assert.False(PlayableVision.MayShareSampledVisiblePath(new[]{new VisionSource(new NavPoint(0,0),2),new VisionSource(new NavPoint(10,0),2)},new NavPoint(0,0),new NavPoint(10,0),.5));
            Assert.True(PlayableVision.MayShareSampledVisiblePath(new[]{new VisionSource(new NavPoint(0,0),2),new VisionSource(new NavPoint(4.4,0),2)},new NavPoint(0,0),new NavPoint(4.4,0),.5),"Small unseen gaps may be skipped by the existing sampled predicate.");
            var d=New();var p=PlayableProfile.Default;var visible=D.GetMethod("VisibleSegment",F);var rng=new Random(7105);
            int accepted=0;
            for(int iteration=0;iteration<600;iteration++){
                var sources=Enumerable.Range(0,6).Select(i=>new VisionSource(new NavPoint(rng.Next(-12,13),rng.Next(-12,13)),rng.Next(2,8))).ToArray();
                var vision=new PlayableVision(0,50,50,p.VisionCellSize,1);vision.Refresh(sources,Array.Empty<KnownBuilding>());
                var a=sources[iteration%6].Position;var b=sources[(iteration+1)%6].Position;
                if(!(bool)visible.Invoke(d,new object[]{vision,a,b}))continue;accepted++;
                Assert.True(PlayableVision.MayShareSampledVisiblePath(vision.Snapshot().Sources,a,b,p.VisionCellSize/2),"Existing visible-segment acceptance cannot be culled.");
            }
            Assert.Greater(accepted,20);
        }
        private sealed class CyclicFacts { public int Value; public CyclicFacts Next; }
        [Test]public void FullAuthorityOracleRejectsUnknownCyclesAndKeepsNameEncodingExact()
        {
            var a=new CyclicFacts{Value=1,Next=new CyclicFacts{Value=1}};
            var b=new CyclicFacts{Value=1,Next=new CyclicFacts{Value=1}};
            CollectionAssert.AreEqual(PlayableWorldRestoreTests.FactBytes(a,true),PlayableWorldRestoreTests.FactBytes(b,true));
            CollectionAssert.AreEqual(PlayableWorldRestoreTests.FactBytes(a,true,false),PlayableWorldRestoreTests.FactBytes(a,true,true));
            b.Value=2;CollectionAssert.AreNotEqual(PlayableWorldRestoreTests.FactBytes(a,true),PlayableWorldRestoreTests.FactBytes(b,true));
            b.Value=1;b.Next.Next=new CyclicFacts{Value=1};
            CollectionAssert.AreNotEqual(PlayableWorldRestoreTests.FactBytes(a,true),PlayableWorldRestoreTests.FactBytes(b,true));
            a.Next=a;b.Next.Next=b;
            foreach(var value in new[]{a,b})foreach(bool cached in new[]{false,true})
                StringAssert.Contains("reference cycle",Assert.Throws<InvalidOperationException>(()=>PlayableWorldRestoreTests.FactBytes(value,true,cached)).Message);
        }
        [Test]public void MixedFootprintsRetainIndependentWarmRoutersAndInvalidateTheirOwnGeometry()
        {
            var d=New();var p=NavigationProfile.Default;
            var g=Geometry(new NavObstacle(-.8,-5,.8,5));var start=new NavPoint(-5,-3);var goal=new NavPoint(5,3);
            var small=Router(d,g,p,.3);var large=Router(d,g,p,.72);
            var expectedSmall=new SharedFlowRouter(g,p.ForUnit(.3,4,4.5)).FindPath(start,goal);
            var expectedLarge=new SharedFlowRouter(g,p.ForUnit(.72,4,4.5)).FindPath(start,goal);
            for(int review=0;review<12;review++){
                Assert.AreSame(small,Router(d,g,p,.3));CollectionAssert.AreEqual(expectedSmall,small.FindPath(start,goal));
                Assert.AreSame(large,Router(d,g,p,.72));CollectionAssert.AreEqual(expectedLarge,large.FindPath(start,goal));
            }
            Assert.AreEqual(11,small.PathCacheHits);Assert.AreEqual(11,large.PathCacheHits);
            var changed=Geometry(new NavObstacle(-.8,-5,.8,5),new NavObstacle(-8,5,8,8));
            foreach(var radius in new[]{.3,.72,.3,.72}){
                var r=Router(d,changed,p,radius);Assert.AreNotSame(radius==.3?small:large,r);
                CollectionAssert.AreEqual(new SharedFlowRouter(changed,p.ForUnit(radius,4,4.5)).FindPath(start,goal),r.FindPath(start,goal));
            }
        }
        [Test]public void MixedProfilePoolIsBoundedAndKeepsRecentlyUsedProfile()
        {
            var d=New();var p=NavigationProfile.Default;var g=Geometry();var recent=Router(d,g,p,.3);
            for(int i=0;i<20;i++){
                Router(d,g,p,.4+i*.01);Assert.AreSame(recent,Router(d,g,p,.3));
                var caches=ObserverCaches(d);Assert.True(caches.Contains(PlayableOwner.Player));
                var pool=(System.Collections.IList)caches[PlayableOwner.Player];
                Assert.LessOrEqual(pool.Count,8);
            }
        }
        [Test]public void WarmFieldsKeepExactFreshPathsForMovingOriginsAndGoalsAndBoundedResets()
        {
            var d=New();var p=NavigationProfile.Default;var g=Geometry(new NavObstacle(-.8,-5,.8,5));var first=Router(d,g,p);
            for(int i=0;i<4;i++){var start=new NavPoint(-5-i*.2,-3);var goal=new NavPoint(5,3+i*.1);var router=Router(d,g,p);CollectionAssert.AreEqual(new SharedFlowRouter(g,p.ForUnit(.3,4,4.5)).FindPath(start,goal),router.FindPath(start,goal));Assert.AreSame(first,router);}
            Assert.Greater(first.FieldCacheHits,0);Assert.Less(first.RebuildCount,4);
            for(int i=0;i<24;i++){var goal=new NavPoint(4, -6+i*.5);var r=Router(d,g,p);CollectionAssert.AreEqual(new SharedFlowRouter(g,p.ForUnit(.3,4,4.5)).FindPath(new NavPoint(-5,-2),goal),r.FindPath(new NavPoint(-5,-2),goal));Assert.LessOrEqual(r.CachedFieldCount,ObserverFieldLimit);}
        }
        [Test]public void ExactObservedShapeExtentOwnerDomainAndProfileInvalidateIndependently()
        {
            var a=New();var b=New();var p=NavigationProfile.Default;var circle=Geometry(new NavObstacle(new NavPoint(0,0),1));var rectangle=Geometry(new NavObstacle(-1,-1,1,1));var first=Router(a,circle,p);
            Assert.AreSame(first,Router(a,Geometry(new NavObstacle(new NavPoint(0,0),1)),p));
            foreach(var g in new[]{rectangle,Geometry(),new NavGeometry(9,circle.Obstacles.ToArray(),1),Geometry(new NavObstacle(new NavPolygon(new[]{new NavPoint(-1,-1),new NavPoint(1,-1),new NavPoint(1,1)})))}){var r=Router(a,g,p);Assert.AreNotSame(first,r);CollectionAssert.AreEqual(new SharedFlowRouter(g,p.ForUnit(.3,4,4.5)).FindPath(new NavPoint(-5,0),new NavPoint(5,0)),r.FindPath(new NavPoint(-5,0),new NavPoint(5,0)));}
            Assert.AreNotSame(Router(a,circle,p),Router(b,circle,p));Assert.AreNotSame(Router(a,circle,p),Router(a,circle,p,owner:PlayableOwner.Enemy));
            var previous=Router(a,circle,p);var changed=new NavigationProfile("changed-grid",2,.3,4,4.5,1.2,.15,3.6,.08);var changedRouter=Router(a,circle,changed);Assert.AreNotSame(previous,changedRouter);CollectionAssert.AreEqual(new SharedFlowRouter(circle,changed.ForUnit(.3,4,4.5)).FindPath(new NavPoint(-5,0),new NavPoint(5,0)),changedRouter.FindPath(new NavPoint(-5,0),new NavPoint(5,0)));
            foreach(double radius in new[]{.3,.72,.3}){var g=Geometry(new NavObstacle(-.8,-8,.8,-.5),new NavObstacle(-.8,.5,.8,8));var r=Router(a,g,p,radius);CollectionAssert.AreEqual(new SharedFlowRouter(g,p.ForUnit(radius,4,4.5)).FindPath(new NavPoint(-5,0),new NavPoint(5,0)),r.FindPath(new NavPoint(-5,0),new NavPoint(5,0)));}
        }
        [TestCase(true)][TestCase(false)]
        public void FriendlyHomeCandidatesRespectOtherViablePendingMoveAnchors(bool viable)
        {
            var domain=(PlayableDomain)New();try{
                var p=PlayableProfile.Default;var nav=domain.Navigation;
                int actor=(int)D.GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(-16,0),PlayableOwner.Player,PlayableEntityKind.Tank});
                int other=(int)D.GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(-20,8),PlayableOwner.Player,PlayableEntityKind.Tank});
                D.GetMethod("AddBuilding",F).Invoke(domain,new object[]{PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(16,0),true});D.GetMethod("RebuildGeometry",F).Invoke(domain,null);
                var view=(PlayableSnapshot)D.GetMethod("PlayerSnapshot",F).Invoke(domain,new object[]{0L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player});
                var home=view.Buildings.Single(b=>b.Position.Equals(new NavPoint(16,0)));var unit=view.Entities.Single(u=>u.Id==actor);
                var request=new PlayableRouteRequest(actor,PlayableRouteTargetKind.FriendlyAnchor,home.Id,nav.Generation,domain.Tick,unit.Position,PlayableUnitRules.Radius(p,unit.Kind),home.Position);
                var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(default,100)},Array.Empty<KnownBuilding>());
                PlayableRouteProof[] Project(PlayableEntitySnapshot[] entities)=>(PlayableRouteProof[])D.GetMethod("ProjectRoutesCore",F).Invoke(domain,new object[]{PlayableOwner.Player,new[]{request},vision,entities,view.Buildings,null,false,false});
                var baseline=Project(view.Entities.ToArray()).Single();var second=view.Entities.Single(u=>u.Id==other);
                var pending=new PlayableQueuedOrderSnapshot(1,1,7,24,PlayableCommandKind.Move,new NavLocation(baseline.Goal,NavLocation.FlatSurface),0,aiActionId:314);
                var queued=new PlayableEntitySnapshot(other,second.Owner,second.Kind,second.Position,second.Health,false,0,second.HullHeading,second.TurretHeading,queue:new PlayableUnitQueueSnapshot(null,pending,Array.Empty<PlayableQueuedOrderSnapshot>(),null,viable));
                var actual=Project(view.Entities.Select(u=>u.Id==other?queued:u).ToArray()).Single();
                double spacing=Math.Max(p.Navigation.ArrivalSlotSpacing,4*request.Radius+2*p.Navigation.ArrivalTolerance+2*p.Navigation.LocalGridCell);
                if(viable)Assert.GreaterOrEqual(AiPositionDevelopmentPlanner.Distance(actual.Goal,baseline.Goal),spacing,"A different pending owned Move reserves the same existing landing clearance before its effective order installs");
                else Assert.AreEqual(baseline.Goal,actual.Goal,"A refused/stale pending route cannot reserve a destination");
                Assert.True(domain.Geometry.IsFree(actual.Goal,request.Radius));var from=unit.Position;foreach(var point in actual.Path){Assert.True(domain.Geometry.SegmentFree(from,point,request.Radius));from=point;}
            }finally{domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
        }
        [Test]
        public void FriendlyApproachKeepsProofsWhenRemoteTrafficGrows()
        {
            var domain=(PlayableDomain)New();
            try{
                var p=PlayableProfile.Default;
                var nav=domain.Navigation;
                int actor=(int)D.GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(-16,0),PlayableOwner.Player,PlayableEntityKind.Tank});
                D.GetMethod("AddBuilding",F).Invoke(domain,new object[]{PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(16,0),true});
                D.GetMethod("RebuildGeometry",F).Invoke(domain,null);
                var view=domain.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
                var home=view.Buildings.Single(b=>b.Position.Equals(new NavPoint(16,0)));
                var unit=view.Entities.Single(u=>u.Id==actor);
                var request=new PlayableRouteRequest(actor,PlayableRouteTargetKind.FriendlyAnchor,home.Id,nav.Generation,domain.Tick,
                    unit.Position,PlayableUnitRules.Radius(p,unit.Kind),home.Position);
                var vision=new PlayableVision(0,100,100,1,1);
                vision.Refresh(new[]{new VisionSource(default,200)},Array.Empty<KnownBuilding>());
                PlayableRouteProof[] Project(PlayableEntitySnapshot[] entities)=>(PlayableRouteProof[])D.GetMethod("ProjectRoutesCore",F)
                    .Invoke(domain,new object[]{PlayableOwner.Player,new[]{request},vision,entities,view.Buildings,null,false,false});
                var members=view.Entities.ToArray();
                var expected=Project(members).Single();
                int first=domain.FriendlyApproachChecksForTests;
                var remote=Enumerable.Range(1000,96).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Enemy,
                    PlayableEntityKind.Explorer,new NavPoint(-67+(id%12)*2,40+(id/12%8)*2),40,false,0,0,0));
                var actual=Project(members.Concat(remote).ToArray()).Single();
                int second=domain.FriendlyApproachChecksForTests-first;
                Assert.AreEqual(expected.Goal,actual.Goal);
                CollectionAssert.AreEqual(expected.Path,actual.Path);
                TestContext.WriteLine($"FRIENDLY_APPROACH_CHECKS small={first} with_remote={second}");
                Assert.AreEqual(first,second,"Distant visible entities must not be tested against every candidate.");
            }finally{domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
        }
        [Test]public void ActualFriendlyProjectRoutesBoundsEveryUnreachableCandidateAndKeepsColdProof()
        {
            var d=New();var p=(PlayableProfile)D.GetField("profile",F).GetValue(d);var geometryProperty=D.GetProperty("Geometry",F);var nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(d);
            int id=(int)D.GetMethod("SpawnUnit",F).Invoke(d,new object[]{new NavPoint(-16,0),PlayableOwner.Player,PlayableEntityKind.Tank});
            D.GetMethod("AddBuilding",F).Invoke(d,new object[]{PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(16,0),true});D.GetMethod("RebuildGeometry",F).Invoke(d,null);
            var original=(NavGeometry)geometryProperty.GetValue(d);var blocked=new NavGeometry(original.HalfExtent,original.Obstacles.Concat(new[]{new NavObstacle(-.8,-original.HalfExtent,.8,original.HalfExtent)}).ToArray(),original.Revision+1);geometryProperty.SetValue(d,blocked);
            var view=(PlayableSnapshot)D.GetMethod("PlayerSnapshot",F).Invoke(d,new object[]{0L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,PlayableOwner.Player});
            var home=view.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Position.Equals(new NavPoint(16,0)));var actor=view.Entities.Single(u=>u.Id==id);var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),100)},Array.Empty<KnownBuilding>());
            var request=new PlayableRouteRequest(id,PlayableRouteTargetKind.FriendlyAnchor,home.Id,nav.Generation,0,actor.Position,PlayableUnitRules.Radius(p,actor.Kind),home.Position);
            nav.Crowd.TryGet(id,out var native);var prewarm=(SharedFlowRouter)D.GetMethod("ObservedRouter",F).Invoke(d,new object[]{PlayableOwner.Player,blocked,p.Navigation,native.Radius,native.Speed,native.TurnSpeed});
            const int limit=ObserverFieldLimit;
            // Populate valid native field roots without repeated unreachable probes, which
            // now deliberately share a closed-reachability witness. Prewarm just
            // below the current trim boundary before multi-candidate projection.
            var cellType=typeof(SharedFlowRouter).GetNestedType("Cell",BindingFlags.NonPublic);
            var field=typeof(SharedFlowRouter).GetMethod("GetField",F,null,new[]{cellType},null);
            double grid=p.Navigation.GridCell;
            int startX=(int)Math.Floor((22+blocked.HalfExtent)/grid),startZ=(int)Math.Floor((-24+blocked.HalfExtent)/grid);
            for(int k=0;prewarm.CachedFieldCount<limit-2&&k<64;k++){
                int x=startX+k%8,z=startZ+k/8;
                var point=new NavPoint((x+.5)*grid-blocked.HalfExtent,(z+.5)*grid-blocked.HalfExtent);
                Assert.True(blocked.IsFree(point,native.Radius));
                field.Invoke(prewarm,new[]{Cell(x,z)});
            }
            Assert.AreEqual(limit-2,prewarm.CachedFieldCount,"Compatible router remains just below reset threshold before the actual projection");
            var method=D.GetMethod("ProjectRoutes",F);PlayableRouteProof[] Project()=>(PlayableRouteProof[])method.Invoke(d,new object[]{PlayableOwner.Player,new[]{request},vision,view.Entities,view.Buildings,null,false});
            double radius=TerritoryRules.Radius(p,home.Kind)+request.Radius+p.FollowDistance;Assert.Greater(PlayableUnitRules.AttackApproachCandidates(p,actor.Position,home.Position,radius/p.AttackApproachRangeRatio).Count(goal=>blocked.IsFree(goal,request.Radius)),1,"Actual friendly-anchor request has multiple native approach candidates");Assert.IsEmpty(Project(),"A full visible wall cannot produce a proof");
            var entries=(System.Collections.IList)ObserverCaches(d)[PlayableOwner.Player];
            var entry=entries.Cast<object>().Single();
            Assert.AreSame(prewarm,entry.GetType().GetField("Router",F|BindingFlags.Public).GetValue(entry),
                "Measure the same router the actual projection used, without invoking a trim");
            TestContext.WriteLine("ACTUAL_PROJECT_ROUTES_FIELDS="+prewarm.CachedFieldCount+" PREWARM_INITIAL="+(limit-2));Assert.LessOrEqual(prewarm.CachedFieldCount,limit,"Bound is applied inside actual multi-candidate projection");
            geometryProperty.SetValue(d,original);var warm=Project();Assert.AreEqual(1,warm.Length);ObserverCaches(d).Clear();var cold=Project();Assert.AreEqual(1,cold.Length);CollectionAssert.AreEqual(warm[0].Path,cold[0].Path);Assert.AreEqual(warm[0].Goal,cold[0].Goal);
            var from=actor.Position;foreach(var point in warm[0].Path){Assert.True(original.SegmentFree(from,point,request.Radius));from=point;}
        }
        [Test]public void PublicApproachesOnlyRebuildWhenObservedInputsChange()
        {
            var map=new MossHighlandMap();
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),map);
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)};
            var config=map.Configuration(profile,7,roster);
            var world=new PlayableAuthorityTick(config,1);
            try
            {
                var domain=world.Domain;
                var view=world.ParticipantView("player-1");
                var observer=view.Owner;
                var actor=view.Entities.Single(u=>u.Owner==observer&&u.Kind==PlayableEntityKind.Explorer);
                var objectives=view.PublicScoutObjectives.Where(o=>o.Role==PlayablePublicScoutObjectiveRole.PossibleEnemyStart).ToArray();
                Assert.Greater(objectives.Length,0);
                var requests=objectives.Select(o=>new PlayableRouteRequest(actor.Id,PlayableRouteTargetKind.PublicObjective,o.SiteId,
                    1,0,actor.Position,profile.ExplorerCollisionRadius,o.Approach)).ToArray();
                var metrics=new PlayableRuntimeMetrics(0,0,0,0,0);
                PlayableRouteProof[] Project()=>domain.PlayerSnapshotWithRoutes(0,RuntimeStatus.Running,false,metrics,null,7,observer,requests).RouteProofs.ToArray();
                var geometry=domain.Geometry;
                var first=Project();
                Assert.Greater(first.Length,0);
                int built=domain.PublicApproachBuildsForTests;
                Assert.AreEqual(objectives.Length,built);
                var same=Project();
                Assert.AreEqual(PlayableWorldRestoreTests.Facts(first),PlayableWorldRestoreTests.Facts(same));
                Assert.AreEqual(built,domain.PublicApproachBuildsForTests,"Adjacent projections with the same visible inputs reuse the candidate rings.");

                var vision=(PlayableVision)D.GetMethod("Vision",F).Invoke(domain,new object[]{observer});
                var point=actor.Position;
                var hidden=new NavPoint(-point.X,-point.Z);
                var remote=new NavObstacle(hidden,.1);
                Assert.False(vision.IsVisible(new NavPoint(remote.MinX,remote.MinZ)));
                var property=D.GetProperty("Geometry",F);
                property.SetValue(domain,new NavGeometry(geometry.HalfExtent,geometry.Obstacles.Append(remote).ToArray(),geometry.Revision+1));
                Assert.AreEqual(PlayableWorldRestoreTests.Facts(first),PlayableWorldRestoreTests.Facts(Project()));
                Assert.AreEqual(built,domain.PublicApproachBuildsForTests,"Unseen solids cannot change this public candidate set.");

                var visible=new NavObstacle(point,.05);
                Assert.True(vision.IsVisible(new NavPoint(visible.MinX,visible.MinZ)));
                property.SetValue(domain,new NavGeometry(geometry.HalfExtent,geometry.Obstacles.Append(visible).ToArray(),geometry.Revision+2));
                Project();
                Assert.AreEqual(built*2,domain.PublicApproachBuildsForTests,"A different observed obstacle set invalidates the candidate rings.");
                property.SetValue(domain,geometry);
                TestContext.WriteLine($"PUBLIC_APPROACH_CACHE first={built} after_rebind={domain.PublicApproachBuildsForTests} proofs={first.Length}");
            }
            finally{world.Stop();world.Domain.Navigation.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();}
        }
        [Test]public void AuthoredPolygonPublicScoutProofsRemainExactAfterIndependentWorldDecode()
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(new FoundryProfileData()));
            var config=((FoundryMap)profile.AuthoredMap).Configuration(profile,19092026,humanId:"player-1");
            var original=new PlayableAuthorityTick(config,1);PlayableAuthorityTick restored=null;
            try{
                var wire=original.CaptureBytes();restored=PlayableAuthorityTick.RestoreBytes((byte[])wire.Clone(),config);
                CollectionAssert.AreEqual(wire,restored.CaptureBytes(),"Full world/AI remains exact before route projections");
                var originalDomain=original.Domain;var restoredDomain=restored.Domain;
                var originalGeometry=(NavGeometry)D.GetProperty("Geometry",F).GetValue(originalDomain);var restoredGeometry=(NavGeometry)D.GetProperty("Geometry",F).GetValue(restoredDomain);
                var polygons=config.Obstacles.Where(o=>o.Polygon!=null).ToArray();Assert.AreEqual(18,polygons.Length,"Current Foundry authored polygon fixture");
                foreach(var polygon in polygons)
                {
                    var actual=originalGeometry.Obstacles.Single(o=>o.GeometryEquals(polygon));
                    var decoded=restoredGeometry.Obstacles.Single(o=>o.GeometryEquals(polygon));
                    Assert.AreNotSame(actual.Polygon,decoded.Polygon,"Real wire decode independently materializes the owned polygon");
                    Assert.True(decoded.GeometryEquals(polygon));
                }
                var view=original.ParticipantView("foundry-2");var observation=PlayableAiObservation.From(view);
                var requests=AiScoutPlanner.RouteRequests(observation.Entities,observation.PublicScoutObjectives,observation.Owner,observation.Generation,observation.Tick,profile);
                Assert.AreEqual(21,requests.Length);Assert.True(requests.All(q=>q.UnitId==8),"Actual Foundry explorer8 candidate set");
                var vision=(PlayableVision)D.GetMethod("Vision",F).Invoke(originalDomain,new object[]{observation.Owner});
                Assert.True(polygons.Any(o=>!vision.IsVisible(new NavPoint(o.MinX,o.MinZ))&&!vision.IsVisible(new NavPoint(o.MaxX,o.MaxZ))),"Fixture includes public authored geography outside current vision");
                var left=originalDomain.PlayerSnapshotWithRoutes(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,observation.Owner,requests);
                var right=restoredDomain.PlayerSnapshotWithRoutes(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026,observation.Owner,requests);
                Assert.AreEqual(21,left.RouteProofs.Count);Assert.AreEqual(PlayableWorldRestoreTests.Facts(left.RouteProofs),PlayableWorldRestoreTests.Facts(right.RouteProofs),"Every actual public scout proof origin/goal/path must survive polygon decode");
            }finally{original.Stop();restored?.Stop();}
        }
        [Test]public void HiddenChangedPolygonWithAuthoredBoundsRemainsVisionGated()
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(new FoundryProfileData()));var config=((FoundryMap)profile.AuthoredMap).Configuration(profile,19092026,humanId:"player-1");var world=new PlayableAuthorityTick(config,1);
            try{
                var view=world.ParticipantView("foundry-2");var observation=PlayableAiObservation.From(view);var domain=world.Domain;var vision=(PlayableVision)D.GetMethod("Vision",F).Invoke(domain,new object[]{observation.Owner});
                var nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);var requests=AiScoutPlanner.RouteRequests(observation.Entities,observation.PublicScoutObjectives,observation.Owner,observation.Generation,observation.Tick,profile);
                NavObstacle changed=default(NavObstacle);PlayableRouteRequest selected=null;
                foreach(var authored in config.Obstacles.Where(o=>o.Polygon!=null)){
                    if(vision.IsVisible(new NavPoint(authored.MinX,authored.MinZ))||vision.IsVisible(new NavPoint(authored.MaxX,authored.MinZ))||vision.IsVisible(new NavPoint(authored.MinX,authored.MaxZ))||vision.IsVisible(new NavPoint(authored.MaxX,authored.MaxZ)))continue;
                    var vertices=authored.Polygon.Vertices.ToList();var a=vertices[0];var b=vertices[1];var center=new NavPoint(vertices.Average(v=>v.X),vertices.Average(v=>v.Z));vertices.Insert(1,new NavPoint((a.X+b.X)*.45+center.X*.1,(a.Z+b.Z)*.45+center.Z*.1));
                    var candidate=new NavObstacle(new NavPolygon(vertices.ToArray()),authored.Bottom,authored.Top);var geometry=new NavGeometry(profile.ArenaHalfExtent,new[]{candidate},1);
                    selected=requests.FirstOrDefault(q=>geometry.IsFree(q.Origin,q.Radius)&&geometry.IsFree(q.Target,q.Radius)&&!geometry.SegmentFree(q.Origin,q.Target,q.Radius));
                    if(selected==null)continue;changed=candidate;Assert.AreEqual(authored.MinX,changed.MinX);Assert.AreEqual(authored.MinZ,changed.MinZ);Assert.AreEqual(authored.MaxX,changed.MaxX);Assert.AreEqual(authored.MaxZ,changed.MaxZ);Assert.False(changed.GeometryEquals(authored));break;
                }
                Assert.NotNull(selected,"Real scout candidate crosses a hidden changed polygon sharing authored bounds");Assert.False(config.Obstacles.Any(o=>o.GeometryEquals(changed)),"Unknown geometry is not public authored geography");
                var testGeometry=new NavGeometry(profile.ArenaHalfExtent,new[]{changed},1);D.GetProperty("Geometry",F).SetValue(domain,testGeometry);
                var project=D.GetMethod("ProjectRoutes",F);PlayableRouteProof[] Project(PlayableVision fog)=>(PlayableRouteProof[])project.Invoke(domain,new object[]{observation.Owner,new[]{selected},fog,view.Entities,view.Buildings,null,false});
                var hidden=Project(vision);Assert.AreEqual(1,hidden.Length);CollectionAssert.AreEqual(new[]{selected.Target},hidden[0].Path,"Unseen dynamic polygon cannot reveal hidden geometry through public-objective proof");
                var allVisible=new PlayableVision(0,profile.ArenaHalfExtent,profile.ArenaHalfExtent,profile.VisionCellSize,profile.FogEdgeFeather);allVisible.Refresh(new[]{new VisionSource(default(NavPoint),profile.ArenaHalfExtent*3)},Array.Empty<KnownBuilding>());
                var visible=Project(allVisible);nav.Crowd.TryGet(selected.UnitId,out var actor);var expected=new SharedFlowRouter(testGeometry,profile.Navigation.ForUnit(actor.Radius,actor.Speed,actor.TurnSpeed)).FindPath(selected.Origin,selected.Target);
                Assert.True(expected.Length==0||expected.Length>1,"Visible changed polygon actually blocks the direct leg");if(expected.Length==0)Assert.IsEmpty(visible);else{Assert.AreEqual(1,visible.Length);CollectionAssert.AreEqual(expected,visible[0].Path,"Visible dynamic polygon is handled by actual solver");}
            }finally{world.Stop();}
        }
        // Frozen from the actual original iterator branch, source154dd86/sourcecompiled proof0f9c79f5.
        [TestCase("empty",484,"a4e766e8d120f3f4abd845fb95ef50b4cdd86cf173b437bf4b0cf5cacba1c693")]
        [TestCase("rectangle",463,"602a576777787c9cfc0b42f175c3430c5020584870c0b0d8fcaaeebe681f3806")]
        [TestCase("circle",475,"c7281947ea21aa91e7ba30c40f07df7e5ca7a1ac72b6194edf919d48712acf06")]
        [TestCase("polygon",467,"658fd4d1ffcef69ae0ac6218246c5e1ab0e7b2c5ffa248aa28f6cbc686d72a01")]
        public void ExplicitStencilFieldKeepsFrozenOriginalInsertionAndEveryDistance(string shape,int count,string expectedHash)
        {
            var solids=shape=="empty"?Array.Empty<NavObstacle>():shape=="rectangle"?new[]{new NavObstacle(-1,-2,1,2)}:shape=="circle"?new[]{new NavObstacle(new NavPoint(0,0),1)}:new[]{new NavObstacle(new NavPolygon(new[]{new NavPoint(-1,-2),new NavPoint(1,-1),new NavPoint(.7,2),new NavPoint(-1,1)}))};var geometry=Geometry(solids);var profile=new NavigationProfile("frozen-iterator",1,.3,4,4.5,.7,.15,3.6,.08);var router=new SharedFlowRouter(geometry,profile);
            var seeds=Array.CreateInstance(CellType,1);seeds.SetValue(Cell(2,2),0);var method=typeof(SharedFlowRouter).GetMethod("GetField",F,null,new[]{typeof(System.Collections.Generic.IReadOnlyList<>).MakeGenericType(CellType)},null);SetRouterTestSeam("testSparseFields",true);object field;try{field=method.Invoke(router,new object[]{seeds});CompleteField(router,field);}finally{SetRouterTestSeam("testSparseFields",false);}var storage=field.GetType().GetField("Distance",BindingFlags.Instance|BindingFlags.Public).GetValue(field);var dictionary=(System.Collections.IDictionary)storage.GetType().GetField("sparse",F).GetValue(storage);Assert.NotNull(dictionary,"Explicit sparse branch retains actualDictionary fallback");
            Assert.AreEqual(count,dictionary.Count);Assert.AreEqual(count,router.ExpandedCells);Assert.AreEqual(count,router.CachedCells);Assert.AreEqual(1,router.RebuildCount);Assert.Zero(router.FieldCacheHits);
            using(var memory=new System.IO.MemoryStream()){
                using(var writer=new System.IO.BinaryWriter(memory,System.Text.Encoding.UTF8,true)){writer.Write(dictionary.Count);foreach(var key in dictionary.Keys){writer.Write((int)CellType.GetField("X").GetValue(key));writer.Write((int)CellType.GetField("Z").GetValue(key));writer.Write((int)dictionary[key]);}}
                using(var sha=System.Security.Cryptography.SHA256.Create())Assert.AreEqual(expectedHash,BitConverter.ToString(sha.ComputeHash(memory.ToArray())).Replace("-","").ToLowerInvariant(),"Every distance and queue-derived insertion order matches actualoldcompiledsource");
            }
        }
        private static void CompleteField(SharedFlowRouter router,object field)
        {
            // Lazy main fields expose the same complete FIFO after ordinary frontier expansion.
            // An unreachable cell requires exhaustion; frozen counts/hashes are unchanged.
            var outside=Array.CreateInstance(CellType,1);outside.SetValue(Cell(int.MaxValue,int.MaxValue),0);
            typeof(SharedFlowRouter).GetMethod("ExpandUntil",F).Invoke(router,new object[]{field,outside});
        }
        private static void SetRouterTestSeam(string name,object value)
        {
            var field=typeof(SharedFlowRouter).GetField(name,BindingFlags.Static|BindingFlags.NonPublic);
            Assert.NotNull(field,"Actual simulation Editor assembly must compile UNITY_INCLUDE_TESTS private seam "+name);
            field.SetValue(null,value);
        }
        [TestCase(false)][TestCase(true)]public void PackedObserverPagePreservesAdjacentValuesExactOverflowAndCounts(bool native)
        {
            using var router=new SharedFlowRouter(new NavGeometry(181,Array.Empty<NavObstacle>(),1),new NavigationProfile("packed-boundary-check",1,.3,4,4.5,1,.15,3.6,.08),null,true,native);
            var seeds=Array.CreateInstance(CellType,0);
            var getField=typeof(SharedFlowRouter).GetMethod("GetField",F,null,new[]{typeof(System.Collections.Generic.IReadOnlyList<>).MakeGenericType(CellType)},null);
            var field=getField.Invoke(router,new object[]{seeds});var storage=field.GetType().GetField("Distance",BindingFlags.Instance|BindingFlags.Public).GetValue(field);
            var item=storage.GetType().GetProperty("Item",F|BindingFlags.Public);var count=storage.GetType().GetProperty("Count",F|BindingFlags.Public);var read=storage.GetType().GetMethod("TryGetValue",F|BindingFlags.Public);
            var values=new System.Collections.Generic.Dictionary<object,int>();
            foreach(var cell in new[]{Cell(-1,-1),Cell(0,-1),Cell(14,0),Cell(15,0),Cell(16,0),Cell(30,0),Cell(31,0),Cell(32,0),Cell(362,362),Cell(-2,0),Cell(int.MaxValue,int.MinValue)}){
                foreach(int value in new[]{0,65533,65534,65535,70000,int.MaxValue-1,1,65534,0}){
                    item.SetValue(storage,value,new[]{cell});values[cell]=value;Assert.AreEqual(values.Count,count.GetValue(storage));
                    foreach(var pair in values){var args=new object[]{pair.Key,0};Assert.True((bool)read.Invoke(storage,args));Assert.AreEqual(pair.Value,args[1],"Packed neighbors and overflow remain exact after replacement");}
                    var missing=new object[]{Cell(19,19),0};Assert.False((bool)read.Invoke(storage,missing));Assert.Zero((int)missing[1]);
                }
            }
            var sparse=(System.Collections.IDictionary)storage.GetType().GetField("sparse",F).GetValue(storage);Assert.AreEqual(2,sparse.Count,"Returning from overflow releases only that key; outside values remain");
        }
        [TestCase(-1,false)][TestCase(0,false)][TestCase(3,false)][TestCase(-1,true)][TestCase(0,true)][TestCase(3,true)]
        public void PagedObserverDistancesKeepEverySparseValueFifoAndAllocationFallback(int failAfter,bool native)
        {
            var geometry=new NavGeometry(181,new[]{new NavObstacle(-.8,-7,.8,7)},1);
            var profile=new NavigationProfile("paged-distance-check",1,.3,4,4.5,1,.15,3.6,.08);
            // Exercise the same route algorithm on both sides. The reference
            // still forces sparse distance storage below.
            using var reference=new SharedFlowRouter(geometry,profile,null,true,native);
            using var actual=new SharedFlowRouter(geometry,profile,null,true,native);
            var getField=typeof(SharedFlowRouter).GetMethod("GetField",F,null,new[]{typeof(System.Collections.Generic.IReadOnlyList<>).MakeGenericType(CellType)},null);
            var seeds=Array.CreateInstance(CellType,5);seeds.SetValue(Cell(-1,180),0);seeds.SetValue(Cell(175,180),1);seeds.SetValue(Cell(175,180),2);seeds.SetValue(Cell(-2,180),3);seeds.SetValue(Cell(int.MaxValue,int.MinValue),4);
            var qa=new System.Collections.Generic.List<string>();var qb=new System.Collections.Generic.List<string>();int allocations=0;long allocatedBytes=0;
            object Storage(object f)=>f.GetType().GetField("Distance",BindingFlags.Instance|BindingFlags.Public).GetValue(f);
            long[] Counters(SharedFlowRouter r)=>new long[]{r.RebuildCount,r.FieldCacheHits,r.CachedFieldCount,r.ExpandedCells,r.CachedCells,r.PreparedGeometryBytes,r.PreparedCellComputations,r.PreparedEdgeComputations};
            try{
                SetRouterTestSeam("testSparseFields",true);SetRouterTestSeam("testFieldDequeued",(Action<int,int>)((x,z)=>qa.Add(x+","+z)));
                var a=getField.Invoke(reference,new object[]{seeds});CompleteField(reference,a);
                SetRouterTestSeam("testSparseFields",false);SetRouterTestSeam("testFieldDequeued",(Action<int,int>)((x,z)=>qb.Add(x+","+z)));
                if(native)SetRouterTestSeam("testNativePageAdmission",(Func<int,bool>)(n=>{if(allocations++==failAfter)return false;allocatedBytes+=n;return true;}));
                else SetRouterTestSeam("testDistanceAllocator",(Func<int,int[]>)(n=>{if(allocations++==failAfter)throw new OutOfMemoryException("Injected page refusal");var result=new int[n];allocatedBytes+=result.Length*sizeof(int);return result;}));
                var b=getField.Invoke(actual,new object[]{seeds});CompleteField(actual,b);
                CollectionAssert.AreEqual(qa,qb,"Complete FIFO includes duplicate, halo and outside roots");CollectionAssert.AreEqual(Counters(reference),Counters(actual),"Discovery, geometry reads and field accounting are unchanged");
                var sa=Storage(a);var sb=Storage(b);var dictionary=(System.Collections.IDictionary)sa.GetType().GetField("sparse",F).GetValue(sa);
                var read=sb.GetType().GetMethod("TryGetValue",F|BindingFlags.Public);
                foreach(var key in dictionary.Keys){var args=new object[]{key,0};Assert.True((bool)read.Invoke(sb,args));Assert.AreEqual(dictionary[key],args[1]);}
                foreach(var key in new[]{Cell(-2,-2),Cell(int.MinValue,int.MaxValue),Cell(364,364)}){var args=new object[]{key,0};Assert.False((bool)read.Invoke(sb,args));Assert.Zero((int)args[1]);}
                Assert.AreEqual(dictionary.Count,sb.GetType().GetProperty("Count",F|BindingFlags.Public).GetValue(sb));
                var fallback=(System.Collections.IDictionary)sb.GetType().GetField("sparse",F).GetValue(sb);
                if(failAfter<0){Assert.AreEqual(2,fallback.Count,"Only the two outside roots use dictionary entries");Assert.Less(allocatedBytes,actual.CachedCells*3,"Actual packed page payload is close to two bytes per visited cell, including page edges");Assert.Less(allocations,actual.CachedCells/800,"Page grouping reduces object/reference churn as well as payload");}
                else{Assert.AreEqual(failAfter+1,allocations,"After refusal, new pages fall back without repeated allocation attempts");Assert.Greater(fallback.Count,0);}
                if(native)Assert.AreEqual(allocatedBytes,actual.NativeDistanceBytes,"Actual native payload plus link headers, including retained witness");
                SetRouterTestSeam("testDistanceAllocator",null);SetRouterTestSeam("testNativePageAdmission",null);SetRouterTestSeam("testFieldDequeued",null);
                for(int i=0;i<8;i++){
                    var from=new NavPoint(-5-i*.13,-2+i*.11);var to=new NavPoint(5+i*.17,2-i*.19);
                    SetRouterTestSeam("testSparseFields",true);var path=reference.FindPath(from,to);SetRouterTestSeam("testSparseFields",false);CollectionAssert.AreEqual(path,actual.FindPath(from,to));CollectionAssert.AreEqual(Counters(reference),Counters(actual));
                    reference.TrimFields(1);actual.TrimFields(1);Assert.AreEqual(reference.CachedCells,actual.CachedCells);
                }
                TestContext.WriteLine($"PAGED_DISTANCES native={native} refusal={failAfter} cells={dictionary.Count} allocatedArrayBytes={allocatedBytes} allocations={allocations}");
            }finally{SetRouterTestSeam("testSparseFields",false);SetRouterTestSeam("testDistanceAllocator",null);SetRouterTestSeam("testNativePageAdmission",null);SetRouterTestSeam("testFieldDequeued",null);}
        }

        [TestCase("rectangle")][TestCase("circle")][TestCase("polygon")][TestCase("blocked")]
        public void NativeObserverRoutesKeepMaterializedPointsAndExactFieldWork(string shape)
        {
            var obstacle=shape=="circle"?new NavObstacle(new NavPoint(0,0),2):shape=="polygon"?new NavObstacle(new NavPolygon(new[]{new NavPoint(-1,-5),new NavPoint(1,-3),new NavPoint(.7,5),new NavPoint(-1,4)})):new NavObstacle(-.8,shape=="blocked"?-8:-5,.8,shape=="blocked"?8:5);
            var geometry=Geometry(obstacle);int routed=0,empty=0;
            foreach(double radius in new[]{.3,.72}){
                var profile=NavigationProfile.Default.ForUnit(radius,4,4.5);
                using var original=new SharedFlowRouter(geometry,profile,null,true);
                using var streamed=new SharedFlowRouter(geometry,profile,null,true,true);
                for(int i=0;i<80;i++){
                    var start=new NavPoint(-6+(i%5)*.17,-6+(i%13)*.8);var goal=new NavPoint(4+(i%7)*.13,-5+(i%11)*.9);
                    var expected=original.FindPath(start,goal);var actual=streamed.FindPath(start,goal);
                    CollectionAssert.AreEqual(expected,actual,"same exact points including smoothing ties and failure");if(actual.Length==0)empty++;else routed++;
                    CollectionAssert.AreEqual(new long[]{original.RebuildCount,original.FieldCacheHits,original.CachedCells,original.ExpandedCells,original.PreparedCellComputations,original.PreparedEdgeComputations},new long[]{streamed.RebuildCount,streamed.FieldCacheHits,streamed.CachedCells,streamed.ExpandedCells,streamed.PreparedCellComputations,streamed.PreparedEdgeComputations});
                    if(i%7==6){original.TrimFields(2);streamed.TrimFields(2);}
                }
                // A malformed/unreachable descending chain still returns no path.
                var f1=NewDistanceField(original,5,5);var f2=NewDistanceField(streamed,5,5);
                foreach(var f in new[]{f1,f2}){var storage=DistanceStorage(f);storage.GetType().GetProperty("Item",F|BindingFlags.Public).SetValue(storage,1,new[]{Cell(0,0)});}
                var route=typeof(SharedFlowRouter).GetMethod("Route",F);
                CollectionAssert.IsEmpty((NavPoint[])route.Invoke(original,new[]{(object)new NavPoint(-7,-7),new NavPoint(7,7),Cell(0,0),f1}));
                CollectionAssert.IsEmpty((NavPoint[])route.Invoke(streamed,new[]{(object)new NavPoint(-7,-7),new NavPoint(7,7),Cell(0,0),f2}));
            }
            if(shape=="blocked")Assert.Greater(empty,0);else Assert.Greater(routed,0);
        }
        private static object NewDistanceField(SharedFlowRouter router,int x,int z)
        {
            var seeds=Array.CreateInstance(CellType,1);seeds.SetValue(Cell(x,z),0);
            return typeof(SharedFlowRouter).GetMethod("GetField",F,null,new[]{typeof(System.Collections.Generic.IReadOnlyList<>).MakeGenericType(CellType)},null).Invoke(router,new object[]{seeds});
        }
        private static object DistanceStorage(object field)=>field.GetType().GetField("Distance",BindingFlags.Instance|BindingFlags.Public).GetValue(field);
        private static long NativeBytes(object field)=>(long)DistanceStorage(field).GetType().GetProperty("NativeBytes",F).GetValue(DistanceStorage(field));
        [Test]public void NativePagesRetainEvictedClosureUntilReplacementAndReleaseEveryOwner()
        {
            var geometry=new NavGeometry(181,Array.Empty<NavObstacle>(),1);var profile=new NavigationProfile("native-lifetime",1,.3,4,4.5,1,.15,3.6,.08);
            using var router=new SharedFlowRouter(geometry,profile,null,true,true);
            // Exhaust a real field, then keep its exact closed witness beyond FIFO eviction.
            var first=NewDistanceField(router,180,180);CompleteField(router,first);long bytes=NativeBytes(first);Assert.Greater(bytes,0);
            router.TrimFields(0);Assert.Zero(router.CachedCells);Assert.AreEqual(bytes,router.NativeDistanceBytes);
            var storage=DistanceStorage(first);var read=storage.GetType().GetMethod("TryGetValue",F|BindingFlags.Public);var args=new object[]{Cell(180,180),0};Assert.True((bool)read.Invoke(storage,args));Assert.Zero((int)args[1]);
            var second=NewDistanceField(router,179,180);CompleteField(router,second);Assert.Zero(NativeBytes(first),"Replaced retired closure releases immediately, without GC");
            var third=NewDistanceField(router,178,180);CompleteField(router,third);Assert.Greater(NativeBytes(second),0,"Replaced closure still belongs to a cached field");
            router.TrimFields(1);Assert.Zero(NativeBytes(second));Assert.Greater(NativeBytes(third),0);router.TrimFields(0);Assert.Greater(router.NativeDistanceBytes,0);
            router.Dispose();Assert.Zero(NativeBytes(third));Assert.Zero(router.NativeDistanceBytes);router.Dispose();
            Assert.Throws<ObjectDisposedException>(()=>router.FindPath(default,new NavPoint(1,1)));
        }
        [Test] public void NativePathLaneSharesOnlyExactObservedGeometryAndMobility()
        {
            using var pool = new ObservedPathWorkPool(true);
            var profile = NavigationProfile.Default;
            var geometry = new NavGeometry(181, new[] { new NavObstacle(-1, -7, 1, 7) }, 1);
            var first = pool.Router(PlayableOwner.Player, geometry, profile, .3, 4, 4.5);
            Assert.AreSame(first, pool.Router(PlayableOwner.Enemy, geometry, profile, .3, 4, 4.5));
            Assert.AreNotSame(first, pool.Router(PlayableOwner.Enemy, geometry, profile, .4, 4, 4.5));
            var changed = new NavGeometry(181, new[] { new NavObstacle(-1, -7, 1, 8) }, 1);
            var second = pool.Router(PlayableOwner.Player, changed, profile, .3, 4, 4.5);
            Assert.AreNotSame(first, second);
            var coldResult = pool.Router(PlayableOwner.Enemy, geometry, profile, .3, 4, 4.5);
            Assert.AreNotSame(first, coldResult, "An evicted owner must not leave a disposed router in the shared index.");
            Assert.AreNotSame(second, coldResult, "A geometry revision collision must not share routes.");
        }

        [Test]public void NativeObserverPoolReleasesGeometryProfileAndFinalOwners()
        {
            using var pool=new ObservedPathWorkPool(true);var geometry=new NavGeometry(181,Array.Empty<NavObstacle>(),1);var profile=NavigationProfile.Default;
            var a=pool.Router(PlayableOwner.Player,geometry,profile,.3,4,4.5);NewDistanceField(a,0,0);Assert.Greater(a.NativeDistanceBytes,0);
            var next=new NavGeometry(181,new[]{new NavObstacle(-1,-1,1,1)},2);
            var b=pool.Router(PlayableOwner.Player,next,profile,.3,4,4.5);Assert.Zero(a.NativeDistanceBytes);NewDistanceField(b,0,0);
            for(int i=0;i<8;i++){var r=pool.Router(PlayableOwner.Player,next,profile,.4+i*.01,4,4.5);NewDistanceField(r,0,0);}
            Assert.Zero(b.NativeDistanceBytes,"Original profile was evicted at the unchanged eight-profile cap");
            var last=pool.Router(PlayableOwner.Player,next,profile,.47,4,4.5);NewDistanceField(last,0,0);Assert.Greater(last.NativeDistanceBytes,0);pool.Dispose();Assert.Zero(last.NativeDistanceBytes);
        }
        [TestCase(0)][TestCase(1)]public void NativeUnpublishedFieldReleasesOnOwnershipPublicationFailure(int stage)
        {
            using var router=new SharedFlowRouter(new NavGeometry(181,Array.Empty<NavObstacle>(),1),NavigationProfile.Default,null,true,true);object retained=null;
            try{
                SetRouterTestSeam("testFieldPublication",(Action<object,int>)((field,at)=>{if(at==stage){retained=field;Assert.Greater(NativeBytes(field),0);throw new OutOfMemoryException("Field ownership refusal");}}));
                var error=Assert.Throws<TargetInvocationException>(()=>NewDistanceField(router,0,0));Assert.IsInstanceOf<OutOfMemoryException>(error.InnerException);
                Assert.NotNull(retained);Assert.Zero(NativeBytes(retained),"Retained failed field cannot depend on finalization");Assert.Zero(router.NativeDistanceBytes);Assert.Zero(router.CachedFieldCount);Assert.Zero(router.CachedCells);Assert.Zero(router.RebuildCount);
            }finally{SetRouterTestSeam("testFieldPublication",null);}
            NewDistanceField(router,1,1);Assert.AreEqual(1,router.CachedFieldCount);router.TrimFields(0);Assert.Zero(router.NativeDistanceBytes);
        }
        [Test]public void NativeGeometryReplacementFailureKeepsOldPoolOwnerUntilDispose()
        {
            using var pool=new ObservedPathWorkPool(true);var geometry=new NavGeometry(181,Array.Empty<NavObstacle>(),1);var profile=NavigationProfile.Default;
            var router=pool.Router(PlayableOwner.Player,geometry,profile,.3,4,4.5);NewDistanceField(router,0,0);long before=router.NativeDistanceBytes;Assert.Greater(before,0);
            var seam=typeof(ObservedPathWorkPool).GetField("testGeometryReplacement",BindingFlags.NonPublic|BindingFlags.Static);
            try{
                seam.SetValue(null,(Action)(()=>throw new OutOfMemoryException("Replacement creation failed")));
                Assert.Throws<OutOfMemoryException>(()=>pool.Router(PlayableOwner.Player,new NavGeometry(181,new[]{new NavObstacle(-1,-1,1,1)},2),profile,.3,4,4.5));
                Assert.AreEqual(before,router.NativeDistanceBytes);pool.Dispose();Assert.Zero(router.NativeDistanceBytes);
            }finally{seam.SetValue(null,null);}
        }
        [Test]public void NativePagesEveryAllocationRefusalKeepsEarlierPagesAndExactFallback()
        {
            var geometry=new NavGeometry(181,Array.Empty<NavObstacle>(),1);var profile=NavigationProfile.Default;
            for(int refusal=0;refusal<6;refusal++){
                using var router=new SharedFlowRouter(geometry,profile,null,true,true);int calls=0;long bytes=0;
                try{
                    SetRouterTestSeam("testNativePageAdmission",(Func<int,bool>)(n=>{if(calls++==refusal)return false;bytes+=n;return true;}));
                    var field=NewDistanceField(router,0,0);var storage=DistanceStorage(field);var item=storage.GetType().GetProperty("Item",F|BindingFlags.Public);
                    for(int i=0;i<6;i++)item.SetValue(storage,i*17000,new[]{Cell(i*32,0)});
                    for(int i=0;i<6;i++)Assert.AreEqual(i*17000,item.GetValue(storage,new[]{Cell(i*32,0)}));
                    Assert.AreEqual(refusal+1,calls);Assert.AreEqual(bytes,router.NativeDistanceBytes);
                    router.TrimFields(0);Assert.Zero(router.NativeDistanceBytes);Assert.Zero(NativeBytes(field));
                }finally{SetRouterTestSeam("testNativePageAdmission",null);}
            }
        }
        [Test]public void NativePagesReplaceManagedPayloadWithAccountedNativeStorage()
        {
            var geometry=new NavGeometry(181,new[]{new NavObstacle(-.8,-7,.8,7)},1);var profile=new NavigationProfile("native-allocation-cost",1,.3,4,4.5,1,.15,3.6,.08);
            long calibration=GC.GetAllocatedBytesForCurrentThread();var positive=new byte[4096];long observed=GC.GetAllocatedBytesForCurrentThread()-calibration;GC.KeepAlive(positive);
            long[] payload=new long[2];long[] ticks=new long[2];long[] expanded=new long[2];long[] cells=new long[2];long nativeBytes=0;
            try{
                for(int mode=0;mode<2;mode++){
                    var actualArrays=new System.Collections.Generic.List<int[]>();
                    SetRouterTestSeam("testDistanceAllocator",(Func<int,int[]>)(n=>{var array=new int[n];actualArrays.Add(array);return array;}));
                    using var router=new SharedFlowRouter(geometry,profile,null,true,mode==1);var field=NewDistanceField(router,175,180);
                    long clock=System.Diagnostics.Stopwatch.GetTimestamp();CompleteField(router,field);
                    ticks[mode]=System.Diagnostics.Stopwatch.GetTimestamp()-clock;payload[mode]=actualArrays.Sum(array=>array.LongLength*sizeof(int));expanded[mode]=router.ExpandedCells;cells[mode]=router.CachedCells;
                    if(mode==1)nativeBytes=router.NativeDistanceBytes;
                }
                Assert.AreEqual(expanded[0],expanded[1]);Assert.AreEqual(cells[0],cells[1]);Assert.Greater(payload[0],200000);Assert.Zero(payload[1],"Native path constructs no managed distance arrays");
                Assert.AreEqual(payload[0]+payload[0]/2048*IntPtr.Size,nativeBytes,"Native bytes include all actual payload and intrusive links");
                TestContext.WriteLine($"NATIVE_DISTANCE_STORAGE managedPagePayload={payload[0]} nativeManagedPagePayload={payload[1]} nativeBytes={nativeBytes} ticksManaged={ticks[0]} ticksNative={ticks[1]} cells={cells[0]} managedCounter4096Calibration={observed}; total managed allocation is not inferred from an unavailable runtime counter");
            }finally{SetRouterTestSeam("testDistanceAllocator",null);}
        }
        [TestCase("empty")][TestCase("rectangle")][TestCase("circle")][TestCase("polygon")]
        public void DensePreparedFieldKeepsActualDictionaryEveryValueQueueSeedsPathsAndCounters(string shape)
        {
            var solids=shape=="empty"?Array.Empty<NavObstacle>():shape=="rectangle"?new[]{new NavObstacle(-1,-2,1,2)}:shape=="circle"?new[]{new NavObstacle(new NavPoint(0,0),1)}:new[]{new NavObstacle(new NavPolygon(new[]{new NavPoint(-1,-2),new NavPoint(1,-1),new NavPoint(.7,2),new NavPoint(-1,1)}))};var geometry=Geometry(solids);
            var getField=typeof(SharedFlowRouter).GetMethod("GetField",F,null,new[]{typeof(System.Collections.Generic.IReadOnlyList<>).MakeGenericType(CellType)},null);
            object Storage(object f)=>f.GetType().GetField("Distance",BindingFlags.Instance|BindingFlags.Public).GetValue(f);
            long[] Counters(SharedFlowRouter r)=>new long[]{r.RebuildCount,r.FieldCacheHits,r.CachedFieldCount,r.ExpandedCells,r.CachedCells,r.PreparedGeometryBytes,r.PreparedCellComputations,r.PreparedEdgeComputations};
            try{
                foreach(double radius in new[]{.3,.72})foreach(double grid in new[]{.7,1.2}){
                    var profile=new NavigationProfile("dense-field-check",1,radius,4,4.5,grid,.15,3.6,.08);var reference=new SharedFlowRouter(geometry,profile,null);var variant=new SharedFlowRouter(geometry,profile,null);FreeCell(reference,2,2);FreeCell(variant,2,2);
                    foreach(var seeds in new[]{Array.Empty<int>(),new[]{2,2},new[]{2,2,2,2},new[]{-2,-2,-2,-2},new[]{-2,-2,2,2}}){
                        var cells=Array.CreateInstance(CellType,seeds.Length/2);for(int i=0;i<seeds.Length;i+=2)cells.SetValue(Cell(seeds[i],seeds[i+1]),i/2);var queueA=new System.Collections.Generic.List<string>();var queueB=new System.Collections.Generic.List<string>();
                        SetRouterTestSeam("testSparseFields",true);SetRouterTestSeam("testFieldDequeued",(Action<int,int>)((x,z)=>queueA.Add(x+","+z)));var a=getField.Invoke(reference,new object[]{cells});CompleteField(reference,a);SetRouterTestSeam("testSparseFields",false);SetRouterTestSeam("testFieldDequeued",(Action<int,int>)((x,z)=>queueB.Add(x+","+z)));var b=getField.Invoke(variant,new object[]{cells});CompleteField(variant,b);SetRouterTestSeam("testFieldDequeued",null);
                        CollectionAssert.AreEqual(queueA,queueB,"Actual completeFIFO dequeue order incl duplicate/outside seeds");CollectionAssert.AreEqual(Counters(reference),Counters(variant));
                        var sa=Storage(a);var sb=Storage(b);Assert.NotNull(sa.GetType().GetField("sparse",F).GetValue(sa));if(seeds.Any(n=>n< -1))Assert.IsNull(sb.GetType().GetField("dense",F).GetValue(sb),"Anyoutside seed retainsdefaultDictionary");else Assert.NotNull(sb.GetType().GetField("dense",F).GetValue(sb));
                        var read=sa.GetType().GetMethod("TryGetValue",F|BindingFlags.Public);int side=(int)Math.Ceiling(2*geometry.HalfExtent/grid)+2;
                        for(int x=-2;x<=side;x++)for(int z=-2;z<=side;z++){var left=new object[]{Cell(x,z),0};var right=new object[]{Cell(x,z),0};Assert.AreEqual(read.Invoke(sa,left),read.Invoke(sb,right));Assert.AreEqual(left[1],right[1],"Everydistance and defaultmissing value");}
                    }
                    for(int i=0;i<24;i++){
                        var start=new NavPoint(-5-(i%3)*.2,-3);var goal=new NavPoint(5,-6+i*.5);SetRouterTestSeam("testSparseFields",true);var path=reference.FindPath(start,goal);SetRouterTestSeam("testSparseFields",false);CollectionAssert.AreEqual(path,variant.FindPath(start,goal));CollectionAssert.AreEqual(Counters(reference),Counters(variant));
                    }
                }
                SetRouterTestSeam("testSparseFields",false);ObserverPreparation_PathsLazyFieldCountersAndEvictionsMatchActualUnprepared();ObserverPreparation_OriginalOwnerDomainShapeProfileAndUnitInvalidationStartsCold();ObserverPreparation_DirectPathsCapsAndNonrepresentableBoundsKeepOriginalFallback();
                foreach(int mode in new[]{0,1,2}){
                    var g=mode==1?new NavGeometry(180.5,Array.Empty<NavObstacle>(),1):Geometry();var profile=NavigationProfile.Default;var r=mode==0?new SharedFlowRouter(g,profile):new SharedFlowRouter(g,profile,null);FreeCell(r,1,1);int allocations=0;
                    if(mode==0)SetRouterTestSeam("testSparseFields",true);
                    if(mode==2)SetRouterTestSeam("testDistanceAllocator",(Func<int,int[]>)(n=>{allocations++;throw new OutOfMemoryException("Ownedinjectedactualallocatorfailure");}));var seeds=Array.CreateInstance(CellType,1);seeds.SetValue(Cell(2,2),0);var field=getField.Invoke(r,new object[]{seeds});Assert.IsNull(Storage(field).GetType().GetField("dense",F).GetValue(Storage(field)),"ExplicitSparse/oversized/actualOOMcatch retainsDictionary");if(mode==2)Assert.AreEqual(1,allocations);SetRouterTestSeam("testDistanceAllocator",null);SetRouterTestSeam("testSparseFields",false);
                }
            }finally{SetRouterTestSeam("testSparseFields",false);SetRouterTestSeam("testDistanceAllocator",null);SetRouterTestSeam("testNativePageAdmission",null);SetRouterTestSeam("testFieldDequeued",null);}
        }
        // Test-only liveness observations. No CaptureBytes, barrier getter, mailbox drain or native object serialization.
        private static long[] HarnessProgress(PlayableAuthorityTick authority,UnityHostRouteService routes)
        {
            var domain=typeof(PlayableAuthorityTick).GetField("domain",F).GetValue(authority);
            var nav=(NavigationSession)D.GetProperty("Navigation",F|BindingFlags.Public).GetValue(domain);
            var kernel=(CertifiedRouteKernel)typeof(UnityHostRouteService).GetField("certified",F).GetValue(routes);
            int Count(object value,string name)=>((System.Collections.ICollection)value.GetType().GetField(name,F).GetValue(value)).Count;
            var counters=routes.CertifiedCounters;
            return new long[]{authority.Tick,authority.AwaitingRoutes?1:0,nav.PendingCount,authority.Requests.Count,authority.Answers.Count,
                nav.RegisteredRouteCount,Count(nav,"barrierAnswers"),kernel.ProgressSequence,routes.SolverEpoch,
                counters.LogicalGraphWork,counters.LogicalFieldWork,counters.ConnectorWork,counters.MaintenanceWork,counters.Cancelled,
                counters.ReadyResults,counters.RetainedBytes,Count(kernel,"registered"),Count(kernel,"scanQueue"),Count(kernel,"projectionQueue"),Count(kernel,"completionQueue")};
        }
        private static int AdvanceHarnessTick(PlayableAuthorityTick authority,UnityHostRouteService routes,Action beforeService,System.Diagnostics.Stopwatch wholeTest)
        {
            int pumps=0;
            while(true)
            {
                Assert.Less(wholeTest.ElapsedMilliseconds,600000,"unchanged whole-test600s deadline at tick "+authority.Tick);
                var before=HarnessProgress(authority,routes);beforeService?.Invoke();routes.Service(authority,64);pumps++;
                if(authority.TryAdvance())return pumps;
                var after=HarnessProgress(authority,routes);
                Assert.False(before.SequenceEqual(after),"route barrier has no logical progress after service/delivery at tick "+authority.Tick+
                    "; primitive tick/awaiting/pending/mailbox/registration/barrier/work/epoch/counter/queue states before="+string.Join(",",before)+" after="+string.Join(",",after));
            }
        }
        [Test]public void FoundryHarness_LostActualPendingRequestFailsWithoutAdvancingWorld()
        {
            var config=OfflineParticipantAuthorityTests.Config(2,false);var authority=new PlayableAuthorityTick(config,1);
            try{using(var routes=new UnityHostRouteService()){
                // V1 is the supported all-routes barrier. V2 advances once its
                // bounded quantum completes even when an actor still awaits a route.
                typeof(PlayableAuthorityTick).GetField("tickQuanta",F).SetValue(authority,false);
                typeof(PlayableAuthorityTick).GetMethod("SetRouteDeliveryPolicy",F).Invoke(authority,new object[]{false});
                Assert.AreEqual(PlayableAuthorityTick.LegacyRouteDeliveryPolicy,authority.EffectiveRouteDeliveryPolicy);
                var view=authority.ParticipantView(config.Roster[0].Id);var actor=view.Entities.Single(e=>e.Owner==view.Owner);
                Assert.AreEqual(PlayableCommandStatus.Applied,authority.Apply(new PlayableCommand(1,1,config.Roster[0].Id,PlayableCommandKind.Move,new[]{actor.Id},new NavPoint(-5,5))).Status);
                Assert.False(authority.TryAdvance());Assert.Greater(authority.Requests.Count,0,"actual authority request is queued before deliberate transport loss");
                while(authority.Requests.TryDequeue(out _)){} // Authored invalid host ownership, not a solver shortcut in the positive scenario.
                long tick=authority.Tick;var failure=Assert.Throws<AssertionException>(()=>AdvanceHarnessTick(authority,routes,null,System.Diagnostics.Stopwatch.StartNew()));
                StringAssert.Contains("no logical progress",failure.Message);Assert.AreEqual(tick,authority.Tick,"stalled transport must not manufacture a world step");
            }}finally{authority.Stop();}
        }
        private static void SaveCheckpointDivergence(int tick,byte[] warm,byte[] cold,PlayableAuthorityTick a,PlayableAuthorityTick b,UnityHostRouteService hostA,UnityHostRouteService hostB)
        {
            string root=Environment.GetEnvironmentVariable("SPACEWARS_O6_DIAGNOSTICS");if(string.IsNullOrEmpty(root))return;
            var dir=System.IO.Path.Combine(root,"checkpoint-"+tick);System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir,"warm.world"),warm);System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir,"cold.world"),cold);
            var left=PlayableWorldState.Decode(warm);var right=PlayableWorldState.Decode(cold);var lines=new System.Collections.Generic.List<string>{"tick="+tick,"mode=original reconciled checkpoint; no extra CaptureBytes calls","seed="+left.Seed,"source="+left.SourceIdentity};
            int Difference(byte[] x,byte[] y){for(int i=0;i<Math.Min(x.Length,y.Length);i++)if(x[i]!=y[i])return i;return x.Length==y.Length?-1:Math.Min(x.Length,y.Length);}
            void Section(string name,byte[] x,byte[] y){lines.Add(name+" warmBytes="+x.Length+" coldBytes="+y.Length+" firstDifference="+Difference(x,y));System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir,"warm-"+name+".bin"),x);System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir,"cold-"+name+".bin"),y);}
            Section("domain",left.Domain,right.Domain);Section("navigation",left.Navigation,right.Navigation);Section("ai",left.AiAuthority,right.AiAuthority);Section("binding",left.Binding,right.Binding);Section("participants",left.Participants,right.Participants);Section("transport",left.RuntimeTransport,right.RuntimeTransport);
            for(int i=0;i<left.Vision.Length;i++)Section("vision-"+i,left.Vision[i],right.Vision[i]);
            lines.Add("warmAuthorityFacts="+PlayableWorldRestoreTests.Facts(a.Domain,true));lines.Add("coldAuthorityFacts="+PlayableWorldRestoreTests.Facts(b.Domain,true));
            lines.Add("firstAuthorityField="+typeof(OfflineParticipantRestoreTests).GetMethod("AuthorityDiff",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{a.Domain,b.Domain,"domain",0}));
            for(int i=0;i<a.Scheduler.Owners.Count;i++){var ownerA=a.Scheduler.Owners[i];var ownerB=b.Scheduler.Owners.Single(x=>x.OwnerId==ownerA.OwnerId);lines.Add("owner-"+i+"="+ownerA.OwnerId);Section("owner-"+i,WorldWire.Pack(w=>ownerA.WriteState(w)),WorldWire.Pack(w=>ownerB.WriteState(w)));}
            lines.Add("warmEpoch="+hostA.SolverEpoch+" coldEpoch="+hostB.SolverEpoch);lines.Add("warmLogicalGraph="+hostA.CertifiedCounters.LogicalGraphWork+" coldLogicalGraph="+hostB.CertifiedCounters.LogicalGraphWork);lines.Add("warmLogicalField="+hostA.CertifiedCounters.LogicalFieldWork+" coldLogicalField="+hostB.CertifiedCounters.LogicalFieldWork);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir,"difference.txt"),lines);TestContext.WriteLine("FOUNDRY_DIVERGENCE saved="+dir+" tick="+tick);
        }
        private static void CompareAuthorityFacts(PlayableDomain warm,PlayableDomain cold,string stage,bool restored=false)
        {
            var expected=PlayableWorldRestoreTests.FactBytes(warm,true,restoreProjection:restored);
            var actual=PlayableWorldRestoreTests.FactBytes(cold,true,restoreProjection:restored);
            if(!expected.SequenceEqual(actual))
            {
                var positions=new System.Collections.Generic.Dictionary<long,string>();
                PlayableWorldRestoreTests.FactBytes(warm,true,positions:positions,restoreProjection:restored);
                var fields=positions.OrderBy(row=>row.Key).ToArray();
                var reported=new System.Collections.Generic.HashSet<string>();
                int field=0;
                int comparedLength=Math.Min(expected.Length,actual.Length);
                TestContext.WriteLine("AUTHORITY_ORACLE_DIFF "+stage+" warmLength="+expected.Length+" coldLength="+actual.Length);
                for(int offset=0;offset<=comparedLength&&reported.Count<20;offset++)
                {
                    if(offset<comparedLength&&expected[offset]==actual[offset])continue;
                    if(offset==comparedLength&&expected.Length==actual.Length)break;
                    while(field+1<fields.Length&&fields[field+1].Key<=offset)field++;
                    if(reported.Add(fields[field].Value))
                        TestContext.WriteLine("AUTHORITY_ORACLE_FIELD byte="+offset+" warmField="+fields[field].Value);
                    if(expected.Length!=actual.Length)break;
                }
            }
            ExactByteAssert.AreEqual(expected,actual,stage);
            if(restored)
            {
                // Each process charges its own live roots and its own cache
                // allocations. Compare the saved graph above and check each
                // ledger against its independently reached Session owners.
                foreach(var domain in new[]{warm,cold})
                {
                    var nav=domain.Navigation;
                    var sources=(NavOwnedStorage)typeof(NavigationSession).GetField("motionSources",F).GetValue(nav);
                    Assert.GreaterOrEqual(nav.PhysicalHostBudget.PhysicalBytes,
                        NavigationStorageOwnershipTests.UniqueWithSession(nav,sources),stage);
                    Assert.LessOrEqual(nav.PhysicalHostBudget.RetainedBytes,LayeredNavigationScheduler.MaxRetainedBytes,stage);
                }
            }
        }
        [Test,Timeout(600000)]public void WarmAndColdFoundryAuthorityAndRestoreRemainExactlyEqual()
            =>CompareFoundry(false);
        [Test,Timeout(600000)]public void WarmAndColdRealtimeFoundryAuthorityAndRestoreRemainExactlyEqual()
            =>CompareFoundry(true);
        private void CompareFoundry(bool asynchronous)
        {
            var wholeTest=System.Diagnostics.Stopwatch.StartNew();
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(new FoundryProfileData()));var config=((FoundryMap)p.AuthoredMap).Configuration(p,19092026,humanId:"player-1");var a=new PlayableAuthorityTick(config,1,asynchronousRoutes:asynchronous);var b=new PlayableAuthorityTick(config,1,asynchronousRoutes:asynchronous);
            if(asynchronous)a.PrepareStartupExecution();
            object Domain(PlayableAuthorityTick t)=>t.Domain;
            void CompareViews(bool restored)
            {
                foreach(var participant in config.Roster)
                {
                    var left=a.ParticipantView(participant.Id);
                    var right=b.ParticipantView(participant.Id);
                    ExactByteAssert.AreEqual(PlayableWorldRestoreTests.FactBytes(left,restoreProjection:restored),
                        PlayableWorldRestoreTests.FactBytes(right,restoreProjection:restored),
                        "all persisted observations and proofs for "+participant.Id+" at tick "+a.Tick);
                }
            }
            // CertifiedRouteKernel.Clear drops in-flight world work when its port changes. Each independent world owns its host.
            UnityHostRouteService routesA=null,routesB=null;
            try{routesA=new UnityHostRouteService();routesB=new UnityHostRouteService();
                long warmPumps=0,coldPumps=0;int warmMax=0,coldMax=0;
                void Step(PlayableAuthorityTick t,UnityHostRouteService routes,bool cold){int pumps=AdvanceHarnessTick(t,routes,cold?()=>ObserverCaches(Domain(t)).Clear():(Action)null,wholeTest);if(cold){coldPumps+=pumps;coldMax=Math.Max(coldMax,pumps);}else{warmPumps+=pumps;warmMax=Math.Max(warmMax,pumps);}}
                for(int tick=1;tick<=1200;tick++){Step(a,routesA,false);Step(b,routesB,true);if(tick%300!=0)continue;TestContext.WriteLine("FOUNDRY_HARNESS tick="+tick+" warmPumps="+warmPumps+" coldPumps="+coldPumps+" warmMax="+warmMax+" coldMax="+coldMax+" elapsedMs="+wholeTest.ElapsedMilliseconds);var warmBytes=a.CaptureBytes();var coldBytes=b.CaptureBytes();if(!warmBytes.SequenceEqual(coldBytes))SaveCheckpointDivergence(tick,warmBytes,coldBytes,a,b,routesA,routesB);CollectionAssert.AreEqual(warmBytes,coldBytes,"full world/AI bytes at "+tick);CompareAuthorityFacts(a.Domain,b.Domain,"full authority at tick "+tick,restored:tick>600);CompareViews(restored:tick>600);if(tick==600){
                    NavigationSession Nav(PlayableAuthorityTick t)=>(NavigationSession)D.GetProperty("Navigation",F).GetValue(Domain(t));
                    void Boundary(string label,PlayableAuthorityTick t,UnityHostRouteService host){var nav=Nav(t);var port=t.NavigationBinding.RoutePort;Assert.AreSame(nav,typeof(NavigationRoutePort).GetField("session",F).GetValue(port));TestContext.WriteLine("FOUNDRY_CHECKPOINT "+label+" tick="+t.Tick+" hostBytes="+nav.HostRetainedBytes+" kernelBytes="+host.CertifiedRetainedBytes+" registered="+nav.RegisteredRouteCount+" pending="+nav.PendingCount+" requests="+nav.Requests.Count+" answers="+nav.Answers.Count+" ownPort=true epoch="+host.SolverEpoch);}
                    var beforeRelease=b.CaptureBytes();Boundary("warm-before",a,routesA);Boundary("cold-before",b,routesB);
                    // Physical checkpoints carry their host continuation. Releasing
                    // the host was a replay step in the old wire, now explicitly refused.
                    StringAssert.Contains("preserve their exact continuation",Assert.Throws<InvalidOperationException>(()=>routesA.ReleaseForWorldCheckpoint(a.NavigationBinding)).Message);
                    StringAssert.Contains("preserve their exact continuation",Assert.Throws<InvalidOperationException>(()=>routesB.ReleaseForWorldCheckpoint(b.NavigationBinding)).Message);
                    Boundary("warm-preserved",a,routesA);Boundary("cold-preserved",b,routesB);
                    CollectionAssert.AreEqual(beforeRelease,b.CaptureBytes(),"Rejected release preserves the complete physical checkpoint");
                    CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes(),"full world/AI bytes at preserved boundary");
                    CompareAuthorityFacts(a.Domain,b.Domain,"all authority fields at preserved boundary");
                    var saved=b.CaptureBytes();b.Stop();b=PlayableAuthorityTick.RestoreBytes(saved,config);
                    // Materialize only the own-session facade; no Service/solver/quota work.
                    var restoredBinding=b.NavigationBinding;
                    Boundary("cold-restored",b,routesB);CollectionAssert.AreEqual(saved,b.CaptureBytes(),"Immediate restored full world/AI bytes");
                    // The two transient presentation feeds deliberately restart on
                    // restore. Assert the restart and compare every durable fact.
                    foreach(var participant in config.Roster)
                    {
                        var view=b.ParticipantView(participant.Id);
                        Assert.IsEmpty(view.GroundTrails);Assert.IsEmpty(view.MatchEvents);
                    }
                    Assert.IsEmpty(b.Domain.BotCameraActivities,"Restored bot camera hints start fresh");
                    CompareViews(restored:true);
                    CompareAuthorityFacts(a.Domain,b.Domain,"immediate cold restore",restored:true);
                }}
            }finally{a.Stop();b.Stop();routesA?.Dispose();routesB?.Dispose();}
        }
    }
}

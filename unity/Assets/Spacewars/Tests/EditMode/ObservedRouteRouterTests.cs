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
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        object New()=>Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,1L,false},null);
        SharedFlowRouter Router(object d,NavGeometry g,NavigationProfile p,double radius=.3,PlayableOwner owner=PlayableOwner.Player)=>(SharedFlowRouter)D.GetMethod("ObservedRouter",F).Invoke(d,new object[]{owner,g,p,radius,4d,4.5d});
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
            int limit=(int)D.GetField("ObservedRouteFieldLimit",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            for(int i=0;i<48;i++)
            {
                bool reset=original.CachedFieldCount>=limit-1;if(reset)original.TrimFields(limit-2);
                var prepared=Router(d,geometry,basis);if(firstPreparation==null)firstPreparation=Preparation(prepared);
                if(reset){Assert.AreSame(previous,prepared);Assert.AreSame(firstPreparation,Preparation(prepared));resets++;}
                var start=new NavPoint(-5-(i%3)*.2,-3);var goal=new NavPoint(5,-6+(i%24)*.5);
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
            var table=D.GetField("observedRouteCaches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);table.GetType().GetMethod("Remove").Invoke(table,new[]{d});Assert.AreNotSame(prepared,Preparation(TypedRouter(d,geometry,profile)),"cold domain state has no persisted preparation");
        }
        [Test]public void ObserverPreparation_EightOwnerPayloadAndFieldBudgetsRemainBounded()
        {
            var d=New();var profile=PlayableProfile.Default;var geometry=new NavGeometry(profile.ArenaHalfExtent,new[]{new NavObstacle(-.8,-24,.8,24)},1);var rows=new System.Collections.Generic.List<object>();long total=0;
            foreach(var owner in Enumerable.Range(0,8).Select(x=>(PlayableOwner)x))
            {
                var router=TypedRouter(d,geometry,profile.Navigation,owner:owner);var path=router.FindPath(new NavPoint(-20,0),new NavPoint(20,0));Assert.Greater(path.Length,0);
                total+=router.PreparedGeometryBytes;Assert.LessOrEqual(router.PreparedGeometryBytes,SharedFlowRouter.ObserverPreparationPayloadLimit);Assert.LessOrEqual(router.CachedFieldCount,8);
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
                total+=router.PreparedGeometryBytes;Assert.LessOrEqual(router.PreparedGeometryBytes,SharedFlowRouter.ObserverPreparationPayloadLimit);Assert.LessOrEqual(router.CachedFieldCount,8);
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
        [Test]public void FullAuthorityOracleKeepsCyclicValuesAndNameEncodingExact()
        {
            var a=new CyclicFacts{Value=1};a.Next=a;var b=new CyclicFacts{Value=1};b.Next=b;
            CollectionAssert.AreEqual(PlayableWorldRestoreTests.FactBytes(a,true),PlayableWorldRestoreTests.FactBytes(b,true));
            CollectionAssert.AreEqual(PlayableWorldRestoreTests.FactBytes(a,true,false),PlayableWorldRestoreTests.FactBytes(a,true,true));
            b.Value=2;CollectionAssert.AreNotEqual(PlayableWorldRestoreTests.FactBytes(a,true),PlayableWorldRestoreTests.FactBytes(b,true));
            b.Value=1;b.Next=new CyclicFacts{Value=1,Next=b};
            CollectionAssert.AreNotEqual(PlayableWorldRestoreTests.FactBytes(a,true),PlayableWorldRestoreTests.FactBytes(b,true));
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
                var table=D.GetField("observedRouteCaches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var args=new object[]{d,null};
                Assert.True((bool)table.GetType().GetMethod("TryGetValue").Invoke(table,args));
                var pool=(System.Collections.IList)((System.Collections.IDictionary)args[1])[PlayableOwner.Player];
                Assert.LessOrEqual(pool.Count,8);
            }
        }
        [Test]public void WarmFieldsKeepExactFreshPathsForMovingOriginsAndGoalsAndBoundedResets()
        {
            var d=New();var p=NavigationProfile.Default;var g=Geometry(new NavObstacle(-.8,-5,.8,5));var first=Router(d,g,p);
            for(int i=0;i<4;i++){var start=new NavPoint(-5-i*.2,-3);var goal=new NavPoint(5,3+i*.1);var router=Router(d,g,p);CollectionAssert.AreEqual(new SharedFlowRouter(g,p.ForUnit(.3,4,4.5)).FindPath(start,goal),router.FindPath(start,goal));Assert.AreSame(first,router);}
            Assert.Greater(first.FieldCacheHits,0);Assert.Less(first.RebuildCount,4);
            for(int i=0;i<24;i++){var goal=new NavPoint(4, -6+i*.5);var r=Router(d,g,p);CollectionAssert.AreEqual(new SharedFlowRouter(g,p.ForUnit(.3,4,4.5)).FindPath(new NavPoint(-5,-2),goal),r.FindPath(new NavPoint(-5,-2),goal));Assert.LessOrEqual(r.CachedFieldCount,8);}
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
            int limit=(int)D.GetField("ObservedRouteFieldLimit",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            // Populate valid native field roots without repeated unreachable probes, which
            // now deliberately share a closed-reachability witness. Keep the same6-field
            // precondition so the actual multi-candidate projection still tests its bound.
            var cellType=typeof(SharedFlowRouter).GetNestedType("Cell",BindingFlags.NonPublic);
            var field=typeof(SharedFlowRouter).GetMethod("GetField",F,null,new[]{cellType},null);
            for(int k=0;prewarm.CachedFieldCount<limit-2&&k<16;k++){
                var point=new NavPoint(24,-24+k*2);Assert.True(blocked.IsFree(point,native.Radius));
                field.Invoke(prewarm,new[]{Activator.CreateInstance(cellType,new object[]{(int)Math.Floor((point.X+blocked.HalfExtent)/p.Navigation.GridCell),(int)Math.Floor((point.Z+blocked.HalfExtent)/p.Navigation.GridCell)})});
            }
            Assert.AreEqual(limit-2,prewarm.CachedFieldCount,"Compatible router remains just below reset threshold before the actual projection");
            var method=D.GetMethod("ProjectRoutes",F);PlayableRouteProof[] Project()=>(PlayableRouteProof[])method.Invoke(d,new object[]{PlayableOwner.Player,new[]{request},vision,view.Entities,view.Buildings,null});
            double radius=TerritoryRules.Radius(p,home.Kind)+request.Radius+p.FollowDistance;Assert.Greater(PlayableUnitRules.AttackApproachCandidates(p,actor.Position,home.Position,radius/p.AttackApproachRangeRatio).Count(goal=>blocked.IsFree(goal,request.Radius)),1,"Actual friendly-anchor request has multiple native approach candidates");Assert.IsEmpty(Project(),"A full visible wall cannot produce a proof");
            var table=D.GetField("observedRouteCaches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var router=(SharedFlowRouter)D.GetMethod("ObservedRouter",F).Invoke(d,new object[]{PlayableOwner.Player,blocked,p.Navigation,native.Radius,native.Speed,native.TurnSpeed});TestContext.WriteLine("ACTUAL_PROJECT_ROUTES_FIELDS="+router.CachedFieldCount+" PREWARM_INITIAL="+(limit-2));Assert.LessOrEqual(router.CachedFieldCount,8,"Bound is applied inside actual multi-candidate projection");
            geometryProperty.SetValue(d,original);var warm=Project();Assert.AreEqual(1,warm.Length);table.GetType().GetMethod("Remove").Invoke(table,new[]{d});var cold=Project();Assert.AreEqual(1,cold.Length);CollectionAssert.AreEqual(warm[0].Path,cold[0].Path);Assert.AreEqual(warm[0].Goal,cold[0].Goal);
            var from=actor.Position;foreach(var point in warm[0].Path){Assert.True(original.SegmentFree(from,point,request.Radius));from=point;}
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
                var polygons=config.Obstacles.Where(o=>o.Polygon!=null).ToArray();Assert.AreEqual(22,polygons.Length,"Actual Foundry authored polygon fixture");
                foreach(var polygon in polygons){Assert.True(originalGeometry.Obstacles.Contains(polygon));var decoded=restoredGeometry.Obstacles.Single(o=>o.GeometryEquals(polygon));Assert.AreNotSame(polygon.Polygon,decoded.Polygon,"Real wire decode independently materializes polygon values");Assert.True(decoded.GeometryEquals(polygon));}
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
                var project=D.GetMethod("ProjectRoutes",F);PlayableRouteProof[] Project(PlayableVision fog)=>(PlayableRouteProof[])project.Invoke(domain,new object[]{observation.Owner,new[]{selected},fog,view.Entities,view.Buildings,null});
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
            }finally{SetRouterTestSeam("testSparseFields",false);SetRouterTestSeam("testDistanceAllocator",null);SetRouterTestSeam("testFieldDequeued",null);}
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
                Count(nav,"registeredRoutes"),Count(nav,"barrierAnswers"),kernel.ProgressSequence,routes.SolverEpoch,
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
        [Test,Timeout(600000)]public void WarmAndColdFoundryAuthorityAndRestoreRemainExactlyEqual()
            =>CompareFoundry(false);
        [Test,Timeout(600000)]public void WarmAndColdRealtimeFoundryAuthorityAndRestoreRemainExactlyEqual()
            =>CompareFoundry(true);
        private void CompareFoundry(bool asynchronous)
        {
            var wholeTest=System.Diagnostics.Stopwatch.StartNew();
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(new FoundryProfileData()));var config=((FoundryMap)p.AuthoredMap).Configuration(p,19092026,humanId:"player-1");var a=new PlayableAuthorityTick(config,1,asynchronousRoutes:asynchronous);var b=new PlayableAuthorityTick(config,1,asynchronousRoutes:asynchronous);
            if(asynchronous)a.PrepareStartupExecution();
            var table=D.GetField("observedRouteCaches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var remove=table.GetType().GetMethod("Remove");object Domain(PlayableAuthorityTick t)=>typeof(PlayableAuthorityTick).GetField("domain",F).GetValue(t);
            // CertifiedRouteKernel.Clear drops in-flight world work when its port changes. Each independent world owns its host.
            UnityHostRouteService routesA=null,routesB=null;
            try{routesA=new UnityHostRouteService();routesB=new UnityHostRouteService();
                long warmPumps=0,coldPumps=0;int warmMax=0,coldMax=0;
                void Step(PlayableAuthorityTick t,UnityHostRouteService routes,bool cold){int pumps=AdvanceHarnessTick(t,routes,cold?()=>remove.Invoke(table,new[]{Domain(t)}):(Action)null,wholeTest);if(cold){coldPumps+=pumps;coldMax=Math.Max(coldMax,pumps);}else{warmPumps+=pumps;warmMax=Math.Max(warmMax,pumps);}}
                for(int tick=1;tick<=1200;tick++){Step(a,routesA,false);Step(b,routesB,true);if(tick%300!=0)continue;TestContext.WriteLine("FOUNDRY_HARNESS tick="+tick+" warmPumps="+warmPumps+" coldPumps="+coldPumps+" warmMax="+warmMax+" coldMax="+coldMax+" elapsedMs="+wholeTest.ElapsedMilliseconds);var warmBytes=a.CaptureBytes();var coldBytes=b.CaptureBytes();if(!warmBytes.SequenceEqual(coldBytes))SaveCheckpointDivergence(tick,warmBytes,coldBytes,a,b,routesA,routesB);CollectionAssert.AreEqual(warmBytes,coldBytes,"full world/AI bytes at "+tick);Assert.AreEqual(PlayableWorldRestoreTests.Facts(Domain(a),true),PlayableWorldRestoreTests.Facts(Domain(b),true),"unchanged full authority oracle "+tick+" "+typeof(OfflineParticipantRestoreTests).GetMethod("AuthorityDiff",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Domain(a),Domain(b),"domain",0}));foreach(var participant in config.Roster)Assert.AreEqual(PlayableWorldRestoreTests.Facts(a.ParticipantView(participant.Id)),PlayableWorldRestoreTests.Facts(b.ParticipantView(participant.Id)),"all observations/proofs "+tick);if(tick==600){
                    NavigationSession Nav(PlayableAuthorityTick t)=>(NavigationSession)D.GetProperty("Navigation",F).GetValue(Domain(t));
                    void Boundary(string label,PlayableAuthorityTick t,UnityHostRouteService host){var nav=Nav(t);var port=t.NavigationBinding.RoutePort;Assert.AreSame(nav,typeof(NavigationRoutePort).GetField("session",F).GetValue(port));TestContext.WriteLine("FOUNDRY_CHECKPOINT "+label+" tick="+t.Tick+" hostBytes="+nav.HostRetainedBytes+" kernelBytes="+host.CertifiedRetainedBytes+" registered="+nav.RegisteredRouteCount+" pending="+nav.PendingCount+" requests="+nav.Requests.Count+" answers="+nav.Answers.Count+" ownPort=true epoch="+host.SolverEpoch);}
                    var beforeRelease=b.CaptureBytes();Boundary("warm-before",a,routesA);Boundary("cold-before",b,routesB);
                    routesA.ReleaseForWorldCheckpoint(a.NavigationBinding);routesB.ReleaseForWorldCheckpoint(b.NavigationBinding);
                    Boundary("warm-released",a,routesA);Boundary("cold-released",b,routesB);
                    CollectionAssert.AreEqual(beforeRelease,b.CaptureBytes(),"Release/replay preserves complete checkpoint wire");
                    CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes(),"full world/AI bytes at released boundary");
                    Assert.AreEqual(PlayableWorldRestoreTests.Facts(Domain(a),true),PlayableWorldRestoreTests.Facts(Domain(b),true),"all authority fields at released boundary");
                    var saved=b.CaptureBytes();b.Stop();b=PlayableAuthorityTick.RestoreBytes(saved,config);
                    // Materialize only the own-session facade; no Service/solver/quota work.
                    var restoredBinding=b.NavigationBinding;
                    Boundary("cold-restored",b,routesB);CollectionAssert.AreEqual(saved,b.CaptureBytes(),"Immediate restored full world/AI bytes");
                    foreach(var participant in config.Roster)Assert.AreEqual(PlayableWorldRestoreTests.Facts(a.ParticipantView(participant.Id)),PlayableWorldRestoreTests.Facts(b.ParticipantView(participant.Id)),"immediate released restore observations "+participant.Id);
Assert.AreEqual(PlayableWorldRestoreTests.Facts(Domain(a),true),PlayableWorldRestoreTests.Facts(Domain(b),true),"immediate cold restore "+typeof(OfflineParticipantRestoreTests).GetMethod("AuthorityDiff",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Domain(a),Domain(b),"domain",0}));}}
            }finally{a.Stop();b.Stop();routesA?.Dispose();routesB?.Dispose();}
        }
    }
}

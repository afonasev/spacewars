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
        [Test,Timeout(600000)]public void WarmAndColdFoundryAuthorityAndRestoreRemainExactlyEqual()
            =>CompareFoundry(false);
        [Test,Timeout(600000)]public void WarmAndColdRealtimeFoundryAuthorityAndRestoreRemainExactlyEqual()
            =>CompareFoundry(true);
        private void CompareFoundry(bool asynchronous)
        {
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new FoundryMap(new FoundryProfileData()));var config=((FoundryMap)p.AuthoredMap).Configuration(p,19092026,humanId:"player-1");var a=new PlayableAuthorityTick(config,1,asynchronousRoutes:asynchronous);var b=new PlayableAuthorityTick(config,1,asynchronousRoutes:asynchronous);
            if(asynchronous)a.PrepareStartupExecution();
            var table=D.GetField("observedRouteCaches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var remove=table.GetType().GetMethod("Remove");object Domain(PlayableAuthorityTick t)=>typeof(PlayableAuthorityTick).GetField("domain",F).GetValue(t);
            var routes=new UnityHostRouteService();var coldRoutes=asynchronous?new UnityHostRouteService():routes;
            try{
                void Step(PlayableAuthorityTick t,bool cold){for(int tries=0;tries<100;tries++){if(cold)remove.Invoke(table,new[]{Domain(t)});(cold?coldRoutes:routes).Service(t,64);if(t.TryAdvance())return;}Assert.Fail("fixed route barrier stalled tick="+t.Tick+" cold="+cold+" pending="+t.Latest.Metrics.NavigationPending);}
                for(int tick=1;tick<=1200;tick++){Step(a,false);Step(b,true);if(tick%300!=0)continue;CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes(),"full world/AI bytes at "+tick);Assert.AreEqual(PlayableWorldRestoreTests.Facts(Domain(a),true),PlayableWorldRestoreTests.Facts(Domain(b),true),"unchanged full authority oracle "+tick+" "+typeof(OfflineParticipantRestoreTests).GetMethod("AuthorityDiff",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Domain(a),Domain(b),"domain",0}));foreach(var participant in config.Roster)Assert.AreEqual(PlayableWorldRestoreTests.Facts(a.ParticipantView(participant.Id)),PlayableWorldRestoreTests.Facts(b.ParticipantView(participant.Id)),"all observations/proofs "+tick);if(tick==600){if(asynchronous){
                    // Solver progress is host-owned, not checkpoint authority. Restart both
                    // hosts at this boundary; both resume the same serialized pending work.
                    a=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),config);routes.Dispose();coldRoutes.Dispose();routes=new UnityHostRouteService();coldRoutes=new UnityHostRouteService();
                }b=PlayableAuthorityTick.RestoreBytes(b.CaptureBytes(),config);Assert.AreEqual(PlayableWorldRestoreTests.Facts(Domain(a),true),PlayableWorldRestoreTests.Facts(Domain(b),true),"immediate cold restore "+typeof(OfflineParticipantRestoreTests).GetMethod("AuthorityDiff",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Domain(a),Domain(b),"domain",0}));}}
            }finally{if(!ReferenceEquals(routes,coldRoutes))coldRoutes.Dispose();routes.Dispose();}
        }
    }
}

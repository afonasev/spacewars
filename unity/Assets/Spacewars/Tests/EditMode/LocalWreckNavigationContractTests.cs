using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class LocalWreckNavigationContractTests
    {
        const BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance;
        [TestCase(false)][TestCase(true)] public void ReachabilityResumesWithinAggregateCreditsAndMatchesOriginalGraph(bool closed)
        {
            var geometry=new NavGeometry(12,new[]{new NavObstacle(-1,-12,1,closed?12:7)},1);
            var p=PlayableProfile.Default.Navigation.ForUnit(.72,4,3);var router=new SharedFlowRouter(geometry,p);
            var from=new NavPoint(-7,0);var to=new NavPoint(7,0);bool expected=new SharedFlowRouter(geometry,p).HasPath(from,to),actual=false,done=false;
            int slices=0;while(!done&&slices++<20000){int credits=1;long before=router.ExpandedCells;done=router.TryHasPath(from,to,ref credits,out actual);Assert.That(credits,Is.InRange(0,1));Assert.LessOrEqual(router.ExpandedCells-before,1);}
            Assert.True(done);Assert.AreEqual(expected,actual);Assert.Greater(slices,1);
        }
        [Test] public void ZeroCreditsNeverReportsUnreachableOrAdvances()
        {var r=new SharedFlowRouter(new NavGeometry(10,Array.Empty<NavObstacle>(),1),NavigationProfile.Default);int credits=0;Assert.False(r.TryHasPath(default,new NavPoint(4,0),ref credits,out _));Assert.Zero(r.ExpandedCells);}
        [Test] public void WreckDeltaRejectsStaticChangesButAllowsCircleReplacementAndRemoval()
        {
            var box=new NavObstacle(-2,-2,-1,-1);var circle=new NavObstacle(new NavPoint(4,4),1);
            var before=new NavGeometry(16,new[]{box,circle},1);
            Assert.True(InstalledGeometryCertificateV2.WreckDelta(before,new NavGeometry(16,new[]{box},2)));
            Assert.True(InstalledGeometryCertificateV2.WreckDelta(before,new NavGeometry(16,new[]{box,new NavObstacle(new NavPoint(6,6),1)},2)));
            Assert.False(InstalledGeometryCertificateV2.WreckDelta(before,new NavGeometry(16,new[]{circle},2)));
        }
        [Test] public void WreckDeltaPreservesSetSemanticsForReorderDuplicatesAndStaticReplacement()
        {
            var a=new NavObstacle(-8,-8,-6,-6);var b=new NavObstacle(6,6,8,8);
            var c=new NavObstacle(new NavPoint(0,0),1);var moved=new NavObstacle(new NavPoint(2,2),1);
            var before=new NavGeometry(16,new[]{a,b,c},1);
            Assert.True(InstalledGeometryCertificateV2.WreckDelta(before,new NavGeometry(16,new[]{b,a,moved},2)),"Static order is not physical change");
            Assert.True(InstalledGeometryCertificateV2.WreckDelta(before,new NavGeometry(16,new[]{a,b,a,c,c},2)),"Exact duplicate shapes keep existing set semantics");
            Assert.True(InstalledGeometryCertificateV2.WreckDelta(before,new NavGeometry(16,new[]{a,b},2)),"Long unchanged prefix with a circular removal");
            Assert.False(InstalledGeometryCertificateV2.WreckDelta(before,new NavGeometry(16,new[]{a,new NavObstacle(7,6,9,8),c},2)),"A changed suffix box is still a static change");
            Assert.False(InstalledGeometryCertificateV2.WreckDelta(before,new NavGeometry(16,new[]{a,c},2)),"A removed suffix box is still a static change");
        }
        [Test] public void RemoteCarryKeepsRouteIndicesAndCurrentSaveColdContinuation()
        {
            var p=PlayableProfile.Default;var d=new PlayableDomain(p,81,false);var kernel=new CertifiedRouteKernel();PlayableDomain cold=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
            try{
                int dead=(int)Call("SpawnUnit",default(NavPoint),PlayableOwner.Player,PlayableEntityKind.Tank);
                int mover=(int)Call("SpawnUnit",new NavPoint(-18,-14),PlayableOwner.Player,PlayableEntityKind.Tank);
                Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(81,1,"player-1",PlayableCommandKind.Move,new[]{mover},new NavPoint(-8,-14)),out _));
                for(int i=0;i<20000&&d.Navigation.PendingCount>0;i++){kernel.Pump(new PlayableRouteBinding(d.Geometry,p,d.Navigation.Admission,d.Navigation.RoutePort),4);d.Navigation.ApplyResults();}
                Assert.Zero(d.Navigation.PendingCount);for(int i=0;i<15;i++)d.Step(1d/30);
                d.Navigation.Crowd.TryGet(mover,out var actor);Assert.True(actor.Moving);var route=actor.Route;int index=actor.routeIndex;long request=actor.InstalledRequest;var position=actor.Position;
                Call("Damage",dead,int.MaxValue);Assert.True(actor.Moving);Assert.AreEqual(index,actor.routeIndex);Assert.AreEqual(request,actor.InstalledRequest);Assert.AreSame(route,actor.Route);Assert.AreEqual(position,actor.Position);
                Assert.NotNull(d.Navigation.InstalledExecutionFor(mover));
                kernel.Clear();var bytes=d.CaptureWorldBytes(7,"local-wreck-contract");cold=PlayableDomain.RestoreWorldBytes(bytes,p,7,"local-wreck-contract");
                CollectionAssert.AreEqual(bytes,cold.CaptureWorldBytes(7,"local-wreck-contract"));
                d.Step(1d/30);cold.Step(1d/30);CollectionAssert.AreEqual(d.CaptureWorldBytes(7,"local-wreck-contract"),cold.CaptureWorldBytes(7,"local-wreck-contract"));
            }finally{kernel.Clear();d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();cold?.Navigation.ReleaseCommittedHostRoot();cold?.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);}
        }
        [Test] public void TypedWreckBlocksItsOwnLayerWhileUpperUnitContinuesThroughSameProjection()
        {
            var map=new StackedRampQualificationMap();var p=NavigationProfile.Default.ForUnit(.42,4,3);
            var g=new NavGeometry(64,new[]{new NavObstacle(new NavPoint(0,4),.72,0)},2);
            Assert.False(g.SegmentFree(new NavPoint(-6,4),new NavPoint(6,4),.42,0));
            Assert.True(g.SegmentFree(new NavPoint(-6,4),new NavPoint(6,4),.42,1));
            var crowd=new NavCrowd(g,p,map);
            var actor=crowd.Add(1,new NavPoint(-6,4),.42,4,3,new NavLocation(new NavPoint(-6,4),"upper"));
            Assert.True(crowd.SetRoute(1,new NavPoint(6,4),new[]{new NavPoint(6,4)},new NavLocation(new NavPoint(6,4),"upper")));
            for(int i=0;i<160;i++)crowd.Step(1d/30);
            Assert.Greater(actor.Position.X,4);Assert.AreEqual("upper",actor.Location.SurfaceId);
            var provider=map.ForGeometry(g,null);
            Assert.True(provider.IsValid(new NavLocation(new NavPoint(0,4),"upper"),new NavClearanceProfile("ground",1,"ground",0,.42)));
            Assert.False(provider.IsValid(new NavLocation(new NavPoint(0,4),"lower"),new NavClearanceProfile("ground",1,"ground",0,.42)));
        }
        [Test] public void DeferredGeometrySnapshotOwnsPolygonValuesAndReusesUnchangedStaticShape()
        {
            var polygon=new NavPolygon(new[]{new NavPoint(-2,-2),new NavPoint(2,-2),new NavPoint(2,2),new NavPoint(-2,2)});
            var source=new NavGeometry(16,new[]{new NavObstacle(polygon)},1);
            var nested=typeof(PlayableDomain).GetNestedType("WreckRouteQueries",BindingFlags.NonPublic);
            var snapshot=nested.GetMethod("SnapshotPrimitiveGeometry",BindingFlags.NonPublic|BindingFlags.Static);
            var first=(NavGeometry)snapshot.Invoke(null,new object[]{source,null});
            Assert.AreNotSame(polygon,first.Obstacles[0].Polygon);Assert.IsNull(first.Obstacles[0].Polygon.StorageRoot);
            var second=(NavGeometry)snapshot.Invoke(null,new object[]{new NavGeometry(16,source.Obstacles.ToArray(),2),first});
            Assert.AreSame(first.Obstacles[0].Polygon,second.Obstacles[0].Polygon);Assert.True(first.Obstacles[0].GeometryEquals(source.Obstacles[0]));
        }
        [Test] public void LongUnresolvedQueryYieldsToTheNextCurrentOrderAndSurvivesSave()
        {
            var data=PlayableProfile.Default.CopyData();data.wreckReconcileCellsPerTick=1;data.wreckResponseTicks=2;var p=PlayableProfile.Create(data);
            var home=new[]{new NavPoint(-20,-20),new NavPoint(20,-20)};
            var sites=home.Select((x,i)=>new TerritorySite(i+1,PlayableBuildingKind.Headquarters,x,p)).ToArray();
            var starts=home.Select((x,i)=>new OfflineStart("fair-start-"+i,i+1,x,new NavPoint(x.X,-10),pin:i+1)).ToArray();
            var config=new OfflineMatchConfiguration(p,"wreck-fair-fixture","wreck-fair","flat-explicit-walls",7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)},starts,sites,new[]{new NavObstacle(-2,-p.ArenaHalfExtent,2,p.ArenaHalfExtent)},new double[,]{{0,40},{40,0}});
            var d=new PlayableDomain(p,81,config);var kernel=new CertifiedRouteKernel();PlayableDomain cold=null;
            object Call(string name,params object[] args)=>typeof(PlayableDomain).GetMethod(name,F).Invoke(d,args);
            try{
                int dead=(int)Call("SpawnUnit",new NavPoint(-14,-8),PlayableOwner.Player,PlayableEntityKind.Tank);Call("Damage",dead,int.MaxValue);
                int a=(int)Call("SpawnUnit",new NavPoint(-8,0),PlayableOwner.Player,PlayableEntityKind.Tank),b=(int)Call("SpawnUnit",new NavPoint(-12,8),PlayableOwner.Player,PlayableEntityKind.Tank);
                Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(81,1,"player-1",PlayableCommandKind.Move,new[]{a},new NavPoint(8,0)),out _));
                Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(81,2,"player-1",PlayableCommandKind.Move,new[]{b},new NavPoint(12,8)),out _));
                for(int i=0;i<20000&&d.Navigation.PendingCount>0;i++){kernel.Pump(new PlayableRouteBinding(d.Geometry,p,d.Navigation.Admission,d.Navigation.RoutePort),4);d.Navigation.ApplyResults();}
                Assert.Zero(d.Navigation.PendingCount);bool secondVisited=false;
                for(int i=0;i<8;i++){d.Step(1d/30);secondVisited|=(int)typeof(PlayableDomain).GetField("wreckScanCursor",F).GetValue(d)>=3;}
                Assert.True(secondVisited,"A bounded long query must yield the scan cursor to the second live order.");
                kernel.Clear();var bytes=d.CaptureWorldBytes(7,"wreck-fair");cold=PlayableDomain.RestoreConfiguredWorldBytes(bytes,p,7,"wreck-fair",config);CollectionAssert.AreEqual(bytes,cold.CaptureWorldBytes(7,"wreck-fair"));
                foreach(int id in new[]{a,b}){d.Navigation.Crowd.TryGet(id,out var actor);Assert.AreNotEqual(NavigationOutcome.Arrived,actor.Outcome);Assert.NotNull(d.TacticalQueueProjection(id,PlayableOwner.Player));}
            }finally{kernel.Clear();d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();cold?.Navigation.ReleaseCommittedHostRoot();cold?.DestroyOwnedNavigation();Assert.Zero(d.Navigation.PhysicalHostBudget.PhysicalBytes);}
        }
        [Test] public void NamedQuotasHaveMetadataAndRejectInvalidValues()
        {
            foreach(var field in PlayableProfileMetadata.WreckFields.Where(x=>x.Path.StartsWith("wrecks.navigation."))){Assert.IsNotEmpty(field.Description);var data=PlayableProfile.Default.CopyData();Assert.Throws<ArgumentOutOfRangeException>(()=>field.Write(data,field.Maximum+1));typeof(PlayableProfileData).GetField(field.FieldName).SetValue(data,(int)field.Maximum+1);Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));}
            Assert.AreEqual(4,PlayableProfileMetadata.WreckFields.Count(x=>x.Path.StartsWith("wrecks.navigation.")));
        }
    }
}

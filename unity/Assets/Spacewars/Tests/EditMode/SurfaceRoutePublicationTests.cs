using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class SurfaceRoutePublicationTests
    {
        private static readonly NavPoint Start=new NavPoint(32.0183066482289,42.60111620978801);
        private static readonly NavPoint Drift=new NavPoint(31.785246258424468,42.589833517350016);
        private static readonly NavPoint[] Route={new NavPoint(8,42.400000000000006),new NavPoint(-28.8,42.400000000000006),new NavPoint(-28.8,43.2),new NavPoint(-35.2,43.2),new NavPoint(-43.08,46)};
        private static ThreeCrossingsMap Map()=>new ThreeCrossingsMap(new ThreeCrossingsProfileData());
        private static byte[] Bytes(NavigationSession s){using(var state=s.CaptureOwnedState())using(var bytes=WorldWire.PackOwned(w=>WorldWire.Write(w,state),state.CaptureBudget))return bytes.TransferPublic();}
        private static void ValidRemaining(ThreeCrossingsMap map,NavUnit actor)
        {var location=actor.Location;foreach(var point in actor.localRoute.Skip(actor.localIndex).Concat(actor.route.Skip(actor.routeIndex)))Assert.True(map.TryTraverse(location,point,actor.Radius,out location),"every published remaining surface leg stays valid");}
        [Test] public void ActualOwnedTurnKeepsRemainingSurfaceRouteAndExactColdContinuation()
        {
            var map=Map();var p=PlayableProfile.Default;var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);var nav=new NavigationSession(1,geometry,p.Navigation,terrain:map);NavigationSession cold=null;
            try{
                var actor=nav.Crowd.Add(51,Start,.58,7,7.5);Assert.True(nav.Crowd.SetRoute(51,Route.Last(),Route));
                actor.Heading=Math.Atan2(Route[0].Z-Start.Z,Route[0].X-Start.X)+actor.TurnSpeed/30+p.Navigation.ForwardAlignmentAngle/2;
                Assert.True(map.TryTraverse(actor.Location,Drift,.58,out var drift));Assert.False(map.TryTraverse(drift,Route[0],.58,out _),"the original legal physical step disconnects the active route");ValidRemaining(map,actor);
                double heading=actor.Heading;nav.Step(1d/30);ValidRemaining(map,actor);Assert.AreNotEqual(heading,actor.Heading,"a rejected drift still permits the bounded turn toward progress");
                var bytes=Bytes(nav);cold=new NavigationSession(1,geometry,p.Navigation,terrain:map);cold.RestoreState(WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState));CollectionAssert.AreEqual(bytes,Bytes(cold));
                for(int i=0;i<3;i++){nav.Step(1d/30);cold.Step(1d/30);ValidRemaining(map,actor);CollectionAssert.AreEqual(Bytes(nav),Bytes(cold),"exact current continuation "+i);}
                for(int i=0;i<30;i++){nav.Step(1d/30);ValidRemaining(map,actor);}
                Assert.Greater(Math.Sqrt(Math.Pow(actor.Position.X-Start.X,2)+Math.Pow(actor.Position.Z-Start.Z,2)),actor.Radius,"valid alternative turn/translation cannot become a permanent stall");
            }finally{nav.ReleaseCommittedHostRoot();nav.DestroyNavigationAuthority();if(cold!=null){cold.ReleaseCommittedHostRoot();cold.DestroyNavigationAuthority();}}
        }
        [Test] public void ActiveLocalLegIsQualifiedBeforeTheGlobalLeg()
        {
            var map=Map();var crowd=new NavCrowd(new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision),PlayableProfile.Default.Navigation,map);var actor=crowd.Add(51,Start,.58,7,7.5);
            var global=new[]{new NavPoint(31.2,43.2),new NavPoint(11.2,43.2),new NavPoint(11.2,42.4)}.Concat(Route).ToArray();Assert.True(crowd.SetRoute(51,Route.Last(),global));actor.localRoute=new[]{Route[0],new NavPoint(11.2,42.4),new NavPoint(11.2,43.2)};actor.localIndex=0;ValidRemaining(map,actor);
            Assert.True(map.TryTraverse(actor.Location,Drift,.58,out var drift));Assert.True(map.TryTraverse(drift,actor.route[actor.routeIndex],.58,out _),"global head alone would accept this candidate");Assert.False(((INavMotionClearance)crowd).ClearMotion(0,Drift),"the active local head is the publication dependency");
        }
        [TestCase(false,true)][TestCase(true,false)] public void FlatOrInactiveActorsKeepOrdinaryPhysicalClearance(bool terrainEnabled,bool moving)
        {
            var map=Map();var geometry=new NavGeometry(map.HalfExtent,map.MovementBlockers.ToArray(),map.Revision);var crowd=new NavCrowd(geometry,PlayableProfile.Default.Navigation,terrainEnabled?map:null);var actor=crowd.Add(51,Start,.58,7,7.5);Assert.True(crowd.SetRoute(51,Route.Last(),Route));actor.Moving=moving;
            Assert.True(((INavMotionClearance)crowd).ClearMotion(0,Drift));
        }
        [TestCase(true,true,false,false)][TestCase(false,true,false,true)]
        [TestCase(true,false,false,true)][TestCase(true,true,true,true)]
        public void FlatNativeTypedAvoidancePreservesPublishedCorner(bool typed,bool moving,bool held,bool allowed)
        {
            var p=PlayableProfile.Default;
            var geometry=new NavGeometry(32,new[]{new NavObstacle(-21.7,13.3,-18.3,16.7)},1);
            var start=new NavPoint(-17.700832757298905,12.6);
            var drift=new NavPoint(-17.700832757298905,13.384031993465866);
            var goal=new NavPoint(-27.102009311457817,12.303274062411466);
            var nav=new NavigationSession(107,geometry,p.Navigation){UsesRecastGroundRoutes=true};
            try{
                var actor=nav.Crowd.Add(22,start,.58,7,7.5);
                Assert.True(nav.MoveGroup(new[]{22},new[]{goal}));
                using(var host=new UnityHostRouteService()){
                    var binding=new PlayableRouteBinding(geometry,p,nav.Admission,nav.RoutePort);
                    for(int i=0;i<200&&nav.AppliedResults<1;i++){host.Service(binding,8);nav.ApplyResults();}
                }
                Assert.AreEqual(1,nav.AppliedResults);
                var route=((NavigationInstalledExecutionOwner)actor.StorageOwner.InstalledExecution).Corridor.TypedRoute;Assert.NotNull(route);
                Assert.True(geometry.SegmentFree(start,goal,actor.Radius));
                Assert.True(geometry.SegmentFree(start,drift,actor.Radius));
                Assert.False(geometry.SegmentFree(drift,goal,actor.Radius),"The legal avoidance step disconnects the next published steering corner.");
                // Freeze the legal forward target that ordinary selection can choose.
                actor.routeIndex=actor.route.Length-1;actor.Moving=moving;actor.Held=held;
                Assert.True(nav.PhysicalHostBudget.TryOpenScope(NavigationPreparationKind.Admission,107,0,out var scope));
                using(scope){
                    var frame=NavPreparedCrowdStep.Create(nav.Crowd,1,scope.OwnedReservation());
                    try{
                        frame.Observe();if(typed)frame.BindRecast(0,route,NavRecastIntervals.Initial(route));
                        Assert.AreEqual(allowed,((INavMotionClearance)frame.Working).ClearMotion(0,drift));
                        Assert.AreEqual(start,actor.Position,"Clearance is a query, not motion or a replacement route.");
                    }finally{frame.Release();}scope.Discard();
                }
                if(typed&&moving&&!held){
                    actor.Heading=Math.PI/2;
                    void Valid(){var at=actor.Position;foreach(var point in actor.localRoute.Skip(actor.localIndex).Concat(actor.route.Skip(actor.routeIndex))){Assert.True(geometry.SegmentFree(at,point,actor.Radius),"Actual warm avoidance keeps every published remaining segment valid.");at=point;}}
                    for(int tick=0;tick<4;tick++){nav.Step(1d/30);Valid();}
                    var bytes=Bytes(nav);NavigationSession cold=null;
                    try{
                        cold=new NavigationSession(107,geometry,p.Navigation){UsesRecastGroundRoutes=true};
                        using(var state=WorldWire.Unpack(bytes,WorldWire.ReadNavigationSessionState))cold.RestoreState(state);
                        CollectionAssert.AreEqual(bytes,Bytes(cold),"Strict current physical restore remains unchanged.");
                        for(int tick=0;tick<3;tick++){nav.Step(1d/30);cold.Step(1d/30);Valid();CollectionAssert.AreEqual(Bytes(nav),Bytes(cold),"Exact warm/cold continuation "+tick);}
                    }finally{if(cold!=null){cold.ReleaseCommittedHostRoot();cold.DestroyNavigationAuthority();}}
                }
            }finally{nav.ReleaseCommittedHostRoot();nav.DestroyNavigationAuthority();}
        }
    }
}

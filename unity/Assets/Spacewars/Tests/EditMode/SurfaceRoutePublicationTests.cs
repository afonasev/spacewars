using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
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
    }
}

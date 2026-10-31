using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class TankManeuverTests
    {
        sealed class Clearance:INavMotionClearance
        {
            readonly NavGeometry geometry;readonly NavPoint from;readonly double radius;
            public Clearance(NavGeometry g,NavMotionBody a){geometry=g;from=a.Position;radius=a.Radius;}
            public bool ClearMotion(int row,NavPoint end)=>geometry.SegmentFree(from,end,radius);
        }
        static NavMotionBody Body(NavPoint target,double reverse=2)=>new NavMotionBody{Id=1,Position=new NavPoint(0,0),Target=target,Radius=.72,Speed=4,TurnSpeed=4.5,Active=true,ReverseDistance=reverse,ReverseSpeedRatio=.45,ReverseConeRadians=.4};
        static NavMotionProposal Select(NavMotionBody a,NavGeometry g,NavPoint? final)=>NavCrowdMotion.Select(new[]{a},0,new int[32],0,NavigationProfile.Default,1d/30,new Clearance(g,a),final);
        [Test] public void NearbyFinalRearGoalReversesWithBoundedSpeedAndNoHullFlip()
        {var a=Body(new NavPoint(-1.5,0));var p=Select(a,new NavGeometry(20,Array.Empty<NavObstacle>(),1),a.Target);Assert.Less(p.Velocity.X,0);Assert.AreEqual(0,p.Heading);Assert.LessOrEqual(NavCrowdMotion.Length(p.Velocity),a.Speed*.45);Assert.Less(p.Position.X,0);}
        [TestCase(false)][TestCase(true)] public void DistantGoalOrIntermediateCornerKeepsForwardSteering(bool corner)
        {var a=Body(new NavPoint(corner?-1.5:-3,0));var p=Select(a,new NavGeometry(20,Array.Empty<NavObstacle>(),1),corner?new NavPoint(-4,2):a.Target);Assert.GreaterOrEqual(p.Velocity.X,0);Assert.LessOrEqual(Math.Abs(p.Heading),a.TurnSpeed/30);}
        [Test] public void DisabledCapabilityKeepsBaselineAndWallCannotBeReversedThrough()
        {var g=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var a=Body(new NavPoint(-1.5,0),0);Assert.GreaterOrEqual(Select(a,g,a.Target).Velocity.X,0);a=Body(new NavPoint(-1.5,0));g=new NavGeometry(20,new[]{new NavObstacle(-1,-2,-.75,2)},1);var p=Select(a,g,a.Target);Assert.True(g.SegmentFree(a.Position,p.Position,a.Radius));Assert.GreaterOrEqual(p.Position.X,-.03);}
        [Test] public void JointSweepStillRefusesAReverseCollision()
        {var a=Body(new NavPoint(-1.5,0));var b=Body(new NavPoint(-1.46,0));b.Id=2;b.Position=new NavPoint(-1.46,0);b.Active=false;var p=Select(a,new NavGeometry(20,Array.Empty<NavObstacle>(),1),a.Target);var proposals=new[]{p,new NavMotionProposal{Position=b.Position}};NavCrowdMotion.ResolveJoint(new[]{a,b},proposals,1d/30);Assert.AreEqual(a.Position,proposals[0].Position);}
        [Test] public void CurrentWorldEnvelopeRetainsTypedReverseBindingAndExactIdleContinuation()
        {
            var basis=CrowdPassageTests.Configuration();var data=basis.Profile.CopyData();data.tankReverseDistance=2;var profile=PlayableProfile.Create(data,basis.Profile.AuthoredMap);var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int i=0;i<basis.Starts.Count;i++)for(int j=0;j<basis.Starts.Count;j++)costs[i,j]=basis.RouteCost(i,j);
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,basis.Spectators.ToArray(),basis.ScenarioUnits.ToArray(),profile.AuthoredMap.Id,basis.ScenarioBuildings.ToArray());
            var original=new OfflineParticipantAuthority(config,71);var bytes=original.CaptureBytes();Assert.AreEqual(PlayableWorldState.CurrentVersion,PlayableWorldState.Decode(bytes).Version);var restored=OfflineParticipantAuthority.Restore(bytes,config);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
            Assert.True(restored.Navigation.Crowd.Units.Where(u=>u.CanYield).All(u=>u.ReverseDistance==2&&u.ReverseSpeedRatio==.45&&u.ReverseConeRadians==.4));
            for(int i=0;i<5;i++){original.Step(1d/30);restored.Step(1d/30);CollectionAssert.AreEqual(original.CaptureBytes(),restored.CaptureBytes());}
        }
        [Test] public void CrowdCaptureContinuationAndPrimitiveWireRetainReverseCapability()
        {
            var geometry=new NavGeometry(20,Array.Empty<NavObstacle>(),1);var profile=NavigationProfile.Default;var crowd=new NavCrowd(geometry,profile);var a=crowd.Add(1,new NavPoint(0,0));a.ReverseDistance=2;a.ReverseSpeedRatio=.45;a.ReverseConeRadians=.4;
            Assert.True(crowd.SetRoute(1,new NavPoint(-1.5,0),new[]{new NavPoint(-1.5,0)}));
            var state=crowd.CaptureState();var wire=typeof(PlayableWorldState).Assembly.GetType("Spacewars.Runtime.WorldWire",true);var write=wire.GetMethod("Write",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(BinaryWriter),typeof(NavUnitSaveState)},null);var read=wire.GetMethod("ReadNavUnitSaveState",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            using(var m=new MemoryStream()){using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true))write.Invoke(null,new object[]{w,state.Units[0]});m.Position=0;using(var r=new BinaryReader(m,System.Text.Encoding.UTF8,true)){var decoded=(NavUnitSaveState)read.Invoke(null,new object[]{r});Assert.AreEqual(a.ReverseDistance,decoded.ReverseDistance);Assert.AreEqual(a.ReverseSpeedRatio,decoded.ReverseSpeedRatio);Assert.AreEqual(a.ReverseConeRadians,decoded.ReverseConeRadians);}Assert.AreEqual(m.Length,m.Position);}
            var restored=new NavCrowd(geometry,profile);restored.RestoreState(state);Assert.True(restored.TryGet(1,out var b));Assert.True(b.SameCaptureHeader(state.Units[0]));
            for(int i=0;i<80;i++){crowd.Step(1d/30);restored.Step(1d/30);Assert.True(b.SameCaptureHeader(crowd.CaptureState().Units[0]));Assert.AreEqual(a.Position,b.Position);Assert.AreEqual(a.Heading,b.Heading);}
            Assert.AreEqual(NavigationOutcome.Arrived,a.Outcome);
        }
    }
}

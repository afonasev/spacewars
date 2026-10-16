using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class CrowdPassageTests
    {
        // Frozen actual-domain witness premises, not runtime balance defaults.
        internal const int RoutePreparationBound=4096,RouteBudget=8,MotionBound=2700;
        internal static OfflineMatchConfiguration Configuration()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=19092026};setup.InitializeParticipants();foreach(var p in setup.Participants)p.Human=true;
            var c=NativeLobbyMatch.Create(profile,setup);double lane=profile.Navigation.ArrivalSlotSpacing/2;
            var scenario=new[]{new OfflineScenarioUnit(c.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(-18,-lane),0),new OfflineScenarioUnit(c.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(-18,lane),0),new OfflineScenarioUnit(c.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(18,-lane),Math.PI),new OfflineScenarioUnit(c.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(18,lane),Math.PI)};
            var costs=new double[c.Starts.Count,c.Starts.Count];for(int i=0;i<c.Starts.Count;i++)for(int j=0;j<c.Starts.Count;j++)costs[i,j]=c.RouteCost(i,j);
            return new OfflineMatchConfiguration(profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),costs,c.Spectators.ToArray(),scenario,profile.AuthoredMap.Id,c.ScenarioBuildings.ToArray());
        }
        private static double SweptSeparationSquared(NavPoint relative,NavPoint velocity,double dt)
        {
            double denominator=velocity.X*velocity.X+velocity.Z*velocity.Z;
            double closestTime=denominator>0?Math.Min(dt,Math.Max(0,-(relative.X*velocity.X+relative.Z*velocity.Z)/denominator)):0;
            double x=relative.X+velocity.X*closestTime,z=relative.Z+velocity.Z*closestTime;return x*x+z*z;
        }
        private sealed class StationaryOnlyClearance : INavMotionClearance
        {public bool ClearMotion(int actor,NavPoint end)=>end.Equals(new NavPoint(0,0));}
        [Test] public void GeometricEnclosureCannotMasqueradeAsEndlessTurnInPlace()
        {
            var bodies=new[]{new NavMotionBody{Id=1,Position=new NavPoint(0,0),Target=new NavPoint(10,0),Radius=.72,Speed=4,TurnSpeed=4.5,Active=true}};var neighbors=new int[NavCrowdMotion.NeighborCapacity];var profile=NavigationProfile.Default;
            for(int tick=0;tick<180;tick++){var p=NavCrowdMotion.Select(bodies,0,neighbors,0,profile,1d/30,new StationaryOnlyClearance());Assert.True(p.Blocked,"No translated endpoint is possible even if heading keeps changing");Assert.AreEqual(bodies[0].Position,p.Position);bodies[0].Heading=p.Heading;}
        }
        [Test] public void ActivePeerJointRefusalIsSafeWaitWithoutInventingPhysicalBlockage()
        {
            var bodies=new[]{new NavMotionBody{Id=1,Position=new NavPoint(-1,0),Radius=.72,Active=true},new NavMotionBody{Id=2,Position=new NavPoint(1,0),Radius=.72,Active=true}};
            var proposals=new[]{new NavMotionProposal{Position=new NavPoint(0,0),Velocity=new NavPoint(1,0)},new NavMotionProposal{Position=new NavPoint(0,0),Velocity=new NavPoint(-1,0)}};
            NavCrowdMotion.ResolveJoint(bodies,proposals,1);for(int i=0;i<2;i++){Assert.AreEqual(bodies[i].Position,proposals[i].Position);Assert.AreEqual(new NavPoint(0,0),proposals[i].Velocity);Assert.False(proposals[i].Blocked);}
        }
        [Test] public void EnclosingHeldActorsKeepWholePoseAndTheOriginalMoveTimesOut()
        {
            var profile=NavigationProfile.Default;var crowd=new NavCrowd(new NavGeometry(20,Array.Empty<NavObstacle>(),1),profile);var actor=crowd.Add(1,new NavPoint(0,0));Assert.True(crowd.SetRoute(1,new NavPoint(10,0),new[]{new NavPoint(10,0)}));
            var points=new[]{new NavPoint(1.45,0),new NavPoint(-1.45,0),new NavPoint(0,1.45),new NavPoint(0,-1.45)};for(int i=0;i<points.Length;i++){crowd.Add(i+2,points[i]);crowd.Stop(i+2,true);}
            for(int tick=0;tick<180&&actor.Moving;tick++){crowd.Step(1d/30);for(int i=0;i<points.Length;i++){Assert.AreEqual(points[i],crowd.Units[i+1].Position);Assert.AreEqual(0,crowd.Units[i+1].Heading);Assert.True(crowd.Units[i+1].Held);}}
            Assert.AreEqual(NavigationOutcome.Blocked,actor.Outcome);Assert.AreEqual(new NavPoint(0,0),actor.Position);Assert.Greater(crowd.RepairFailed,0,"Retain actual bounded repair attempts before original timeout");
        }
        [Test] public void OrdinaryTwoGroupsCrossCentralBridgeWithJointSweptSafetyAndColdNextStep()
        {
            const double dt=1d/30;var c=Configuration();var world=new OfflineParticipantAuthority(c,71);var nav=world.Navigation;var map=(ThreeCrossingsMap)c.Profile.AuthoredMap;
            var tanks=world.View(c.Roster[0].Id).Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).OrderBy(id=>id).ToArray();
            var left=tanks.Where(id=>nav.Crowd.Units.Single(u=>u.Id==id).Position.X<0).ToArray();var right=tanks.Except(left).ToArray();
            var crossed=new HashSet<int>();var starts=tanks.ToDictionary(id=>id,id=>nav.Crowd.Units.Single(u=>u.Id==id).Position);int preparation=0,ticks=0,compatibilityWorkspaces=0;bool coldChecked=false;
            nav.PhysicalHostBudget.AllocationAdmission=bytes=>{long layout=NavStorageScratch.LeaseBytes+NavMotionWorkspace.HeaderBytes+NavMotionWorkspace.ArrayBytes(nav.Crowd.Units.Count);if(bytes==layout&&new System.Diagnostics.StackTrace().GetFrames().Any(f=>f.GetMethod().Name=="CreateCompatibilityMotionWorkspace"))compatibilityWorkspaces++;return true;};
            var host=new UnityHostRouteService();try{
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,1,c.Roster[0].Id,PlayableCommandKind.Move,left,new NavPoint(18,0)).AsHuman()).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,2,c.Roster[0].Id,PlayableCommandKind.Move,right,new NavPoint(-18,0)).AsHuman()).Status);
                Assert.AreEqual(2,left.Length);Assert.AreEqual(2,right.Length);Assert.AreNotEqual(nav.GroupFor(left[0]).GroupId,nav.GroupFor(right[0]).GroupId);
                var groups=tanks.ToDictionary(id=>id,id=>nav.GroupFor(id).GroupId);var orders=tanks.ToDictionary(id=>id,id=>nav.GroupFor(id).OrderRevision);
                for(;preparation<RoutePreparationBound&&nav.PendingCount>0;preparation++){host.Service(new PlayableRouteBinding(world.Domain.Geometry,c.Profile,nav.Admission,nav.RoutePort),RouteBudget);nav.ApplyResults();}
                Assert.Zero(nav.PendingCount,"frozen route preparation bound, before motion");foreach(int id in tanks)Assert.NotNull(nav.InstalledExecutionFor(id));
                for(;ticks<MotionBound&&tanks.Any(id=>nav.Crowd.Units.Single(u=>u.Id==id).Outcome!=NavigationOutcome.Arrived);ticks++){
                    var before=nav.Crowd.CaptureState().Units;
                    host.Service(new PlayableRouteBinding(world.Domain.Geometry,c.Profile,nav.Admission,nav.RoutePort),RouteBudget);
                    OfflineParticipantAuthority cold=null;
                    UnityHostRouteService coldHost=null;try{
                        if(ticks==90){var bytes=world.CaptureBytes();cold=OfflineParticipantAuthority.Restore(bytes,c);coldHost=new UnityHostRouteService();CollectionAssert.AreEqual(bytes,cold.CaptureBytes());coldHost.Service(new PlayableRouteBinding(cold.Domain.Geometry,c.Profile,cold.Navigation.Admission,cold.Navigation.RoutePort),RouteBudget);}
                        world.Step(dt);if(cold!=null){cold.Step(dt);CollectionAssert.AreEqual(world.CaptureBytes(),cold.CaptureBytes(),"fixed actual motion next step after cold restore");coldChecked=true;}
                    }finally{coldHost?.Dispose();if(cold!=null){cold.Navigation.ReleaseCommittedHostRoot();cold.Domain.DestroyOwnedNavigation();Assert.Zero(cold.Navigation.PhysicalHostBudget.PhysicalBytes);Assert.Zero(cold.Navigation.PhysicalHostBudget.HeldBytes);Assert.Zero(cold.Navigation.PhysicalHostBudget.ActiveScopes);}}
                    var after=nav.Crowd.Units;
                    for(int i=0;i<after.Count;i++){
                        var a=after[i];var b=before.Single(u=>u.Id==a.Id);var displacement=new NavPoint(a.Position.X-b.Position.X,a.Position.Z-b.Position.Z);
                        Assert.LessOrEqual(NavCrowdMotion.Length(displacement),b.Speed*dt+1e-8,"no teleport/speed violation");Assert.LessOrEqual(Math.Abs(NavCrowdMotion.Angle(a.Heading-b.Heading)),b.TurnSpeed*dt+1e-8);
                        if(b.Held){Assert.AreEqual(b.Position,a.Position);Assert.AreEqual(b.Heading,a.Heading);}
                        Assert.True(world.Domain.Geometry.SegmentFree(b.Position,a.Position,a.Radius));Assert.True(map.SupportsSweep(b.Position,a.Position,a.Radius));
                        for(int j=0;j<i;j++){var other=after[j];var previous=before.Single(u=>u.Id==other.Id);double r=a.Radius+other.Radius;var relative=new NavPoint(b.Position.X-previous.Position.X,b.Position.Z-previous.Position.Z);var velocity=new NavPoint((displacement.X-other.Position.X+previous.Position.X)/dt,(displacement.Z-other.Position.Z+previous.Position.Z)/dt);Assert.GreaterOrEqual(SweptSeparationSquared(relative,velocity,dt),r*r-1e-8,"whole actual dt peer sweep");}
                        if(tanks.Contains(a.Id)){
                            Assert.AreEqual(groups[a.Id],nav.GroupFor(a.Id).GroupId);Assert.AreEqual(orders[a.Id],nav.GroupFor(a.Id).OrderRevision);
                            if(Math.Abs(a.Position.X)<=map.RiverHalfWidth){Assert.LessOrEqual(Math.Abs(a.Position.Z)+a.Radius,map.CentralBridgeWidth/2+1e-8,"must use central crossing");if(a.Location.SurfaceId=="bridge/central")crossed.Add(a.Id);}
                        }
                    }
                }
                Assert.True(coldChecked);CollectionAssert.AreEquivalent(tanks,crossed);foreach(int id in tanks){var actor=nav.Crowd.Units.Single(u=>u.Id==id);Assert.AreEqual(NavigationOutcome.Arrived,actor.Outcome,"actor "+id+" at "+actor.Position);Assert.AreEqual(-Math.Sign(starts[id].X),Math.Sign(actor.Position.X),"opposite bank arrival");Assert.Greater(Math.Abs(actor.Position.X)-actor.Radius,map.RiverHalfWidth,"whole terminal footprint beyond river");}
            }finally{
                TestContext.WriteLine("B0_ACTUAL_PASSAGE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Preparation=preparation,MotionTicks=ticks,ColdNextStep=coldChecked,CompatibilityWorkspaces=compatibilityWorkspaces,Crossed=crossed,nav.Crowd.RepairCount,nav.Crowd.RepairFailed,Actors=tanks.Select(id=>{var a=nav.Crowd.Units.Single(u=>u.Id==id);return new{a.Id,Start=starts[id],a.Position,a.Goal,Progress=Math.Sqrt(Math.Pow(a.Goal.X-starts[id].X,2)+Math.Pow(a.Goal.Z-starts[id].Z,2))-Math.Sqrt(Math.Pow(a.Goal.X-a.Position.X,2)+Math.Pow(a.Goal.Z-a.Position.Z,2)),a.Heading,a.Outcome,a.InstalledGroupId,a.InstalledOrder,a.routeIndex,a.noProgressTicks,a.blockedSeconds};}).ToArray()}));
                nav.PhysicalHostBudget.AllocationAdmission=null;host.Dispose();nav.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
        [Test] public void FrozenWideCentralBridgeSceneHasFullRadiusLaneAndExitClearance()
        {
            var c=Configuration();var world=new OfflineParticipantAuthority(c,71);var nav=world.Navigation;var map=(ThreeCrossingsMap)c.Profile.AuthoredMap;var geometry=world.Domain.Geometry;var radius=c.Profile.TankCollisionRadius;double lane=c.Profile.Navigation.ArrivalSlotSpacing/2;var points=c.ScenarioUnits.Select(u=>u.Position).ToArray();var facts=new List<object>();
            try{
                Assert.AreEqual(4,c.ScenarioUnits.Count);Assert.AreEqual(4,world.View(c.Roster[0].Id).Entities.Count(e=>e.Kind==PlayableEntityKind.Tank));Assert.AreEqual(c.Roster.Count+4,nav.Crowd.Units.Count,"preserve ordinary genesis explorers");
                foreach(var p in points){
                    var blockers=geometry.Obstacles.Where(o=>o.DistanceSquared(p)<radius*radius).Select(o=>new{o.MinX,o.MinZ,o.MaxX,o.MaxZ}).ToArray();bool free=geometry.IsFree(p,radius),footprint=map.SupportsFootprint(p,radius),located=map.TryLocate(p,radius,out var location);
                    facts.Add(new{Point=p,Radius=radius,Free=free,Footprint=footprint,Located=located,Surface=located?location.SurfaceId:null,Blockers=blockers});
                    Assert.True(free&&footprint&&located,"invalid frozen point: "+Newtonsoft.Json.JsonConvert.SerializeObject(facts.Last()));
                }
                for(int i=0;i<points.Length;i++)for(int j=i+1;j<points.Length;j++){double x=points[i].X-points[j].X,z=points[i].Z-points[j].Z;Assert.GreaterOrEqual(x*x+z*z,4*radius*radius,"frozen starts full-radius pair clearance");}
                foreach(var start in points){
                    var end=new NavPoint(-start.X,start.Z);Assert.True(geometry.SegmentFree(start,end,radius),"actual domain geometry lane");Assert.True(map.SupportsSweep(start,end,radius),"whole-footprint authored lane sweep");Assert.True(map.TryLocate(start,radius,out var from));Assert.True(map.TryTraverse(from,end,radius,out var terminal),"bank/bridge/bank seam traversal");Assert.AreEqual(end,terminal.Position);
                    var surfaces=new List<string>();foreach(double x in new[]{-18d,-map.CentralBridgeHalfSpan,-map.RiverHalfWidth,0,map.RiverHalfWidth,map.CentralBridgeHalfSpan,18d}){Assert.True(map.TryLocate(new NavPoint(x,start.Z),radius,out var at));surfaces.Add(at.SurfaceId);}Assert.Contains("bridge/central",surfaces);
                    facts.Add(new{LaneStart=start,LaneEnd=end,Surfaces=surfaces});
                }
                foreach(double x in new[]{-18d,18d}){
                    var center=new NavPoint(x,0);Assert.True(nav.Crowd.CanPlace(center,radius),"opposite original group goal is initially placeable");
                    var low=new NavPoint(x,-lane);var high=new NavPoint(x,lane);Assert.True(geometry.IsFree(low,radius)&&geometry.IsFree(high,radius)&&map.SupportsFootprint(low,radius)&&map.SupportsFootprint(high,radius));Assert.GreaterOrEqual(2*lane,2*radius,"two full terminal slots");
                }
            }finally{
                nav.ReleaseCommittedHostRoot();world.Domain.DestroyOwnedNavigation();
                TestContext.WriteLine("B0_FINITE_PREFLIGHT "+Newtonsoft.Json.JsonConvert.SerializeObject(new{c.Seed,Radius=radius,LaneOffset=lane,Map=map.Id,Revision=map.Revision,PreparationBound=RoutePreparationBound,RouteBudget,MotionBound,Facts=facts,Cleanup=new{nav.PhysicalHostBudget.PhysicalBytes,nav.PhysicalHostBudget.HeldBytes,nav.PhysicalHostBudget.ActiveScopes}}));
                Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
    }
}

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class CrowdPassageNativeTests
    {
        [UnityTest] public IEnumerator OrdinaryTwoGroupsCompleteTheActualCentralBridgePassage()
        {
            var profile=PlayableProfile.ThreeCrossingsDefault;var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=19092026};setup.InitializeParticipants();foreach(var p in setup.Participants)p.Human=true;
            var seed=NativeLobbyMatch.Create(profile,setup);double lane=profile.Navigation.ArrivalSlotSpacing/2;
            var scenario=new[]{new OfflineScenarioUnit(seed.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(-18,-lane),0),new OfflineScenarioUnit(seed.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(-18,lane),0),new OfflineScenarioUnit(seed.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(18,-lane),Math.PI),new OfflineScenarioUnit(seed.Roster[0].LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(18,lane),Math.PI)};
            var costs=new double[seed.Starts.Count,seed.Starts.Count];for(int i=0;i<seed.Starts.Count;i++)for(int j=0;j<seed.Starts.Count;j++)costs[i,j]=seed.RouteCost(i,j);
            var c=new OfflineMatchConfiguration(profile,seed.SourceIdentity,seed.MapIdentity,seed.RouteProvenance,seed.Seed,seed.Roster.ToArray(),seed.Starts.ToArray(),seed.Sites.ToArray(),seed.Obstacles.ToArray(),costs,seed.Spectators.ToArray(),scenario,profile.AuthoredMap.Id,seed.ScenarioBuildings.ToArray());
            var world=new OfflineParticipantAuthority(c,71);var nav=world.Navigation;var host=new UnityHostRouteService();int preparation=0,ticks=0;var crossed=new System.Collections.Generic.HashSet<int>();
            var ids=world.View(c.Roster[0].Id).Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).ToArray();var left=ids.Where(id=>nav.Crowd.Units.Single(u=>u.Id==id).Position.X<0).ToArray();var right=ids.Except(left).ToArray();var map=(ThreeCrossingsMap)profile.AuthoredMap;
            try{
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,1,c.Roster[0].Id,PlayableCommandKind.Move,left,new NavPoint(18,0)).AsHuman()).Status);
                Assert.AreEqual(PlayableCommandStatus.Applied,world.Apply(new PlayableCommand(71,2,c.Roster[0].Id,PlayableCommandKind.Move,right,new NavPoint(-18,0)).AsHuman()).Status);
                for(;preparation<4096&&nav.PendingCount>0;preparation++){host.Service(new PlayableRouteBinding(nav.Admission.Geometry,profile,nav.Admission,nav.RoutePort),8);nav.ApplyResults();if(preparation%30==0)yield return null;}
                Assert.Zero(nav.PendingCount);Assert.AreNotEqual(nav.GroupFor(left[0]).GroupId,nav.GroupFor(right[0]).GroupId);
                for(;ticks<2700&&ids.Any(id=>nav.Crowd.Units.Single(u=>u.Id==id).Outcome!=NavigationOutcome.Arrived);ticks++){
                    var before=nav.Crowd.CaptureState().Units;host.Service(new PlayableRouteBinding(nav.Admission.Geometry,profile,nav.Admission,nav.RoutePort),8);world.Step(1d/30);
                    for(int i=0;i<nav.Crowd.Units.Count;i++){
                        var a=nav.Crowd.Units[i];var b=before.Single(u=>u.Id==a.Id);double dx=a.Position.X-b.Position.X,dz=a.Position.Z-b.Position.Z;
                        Assert.LessOrEqual(dx*dx+dz*dz,b.Speed*b.Speed/900+1e-9);Assert.True(map.SupportsSweep(b.Position,a.Position,a.Radius));Assert.True(nav.Admission.Geometry.SegmentFree(b.Position,a.Position,a.Radius));
                        for(int j=0;j<i;j++){var other=nav.Crowd.Units[j];var old=before.Single(u=>u.Id==other.Id);double x=b.Position.X-old.Position.X,z=b.Position.Z-old.Position.Z,vx=dx-other.Position.X+old.Position.X,vz=dz-other.Position.Z+old.Position.Z;double q=vx*vx+vz*vz,t=q>0?Math.Max(0,Math.Min(1,-(x*vx+z*vz)/q)):0,r=a.Radius+other.Radius;Assert.GreaterOrEqual((x+vx*t)*(x+vx*t)+(z+vz*t)*(z+vz*t),r*r-1e-8);}
                        if(ids.Contains(a.Id)&&Math.Abs(a.Position.X)<=map.RiverHalfWidth){Assert.LessOrEqual(Math.Abs(a.Position.Z)+a.Radius,map.CentralBridgeWidth/2+1e-8);if(a.Location.SurfaceId=="bridge/central")crossed.Add(a.Id);}
                    }
                    if(ticks%30==0)yield return null;
                }
                CollectionAssert.AreEquivalent(ids,crossed);foreach(int id in ids)Assert.AreEqual(NavigationOutcome.Arrived,nav.Crowd.Units.Single(u=>u.Id==id).Outcome);TestContext.WriteLine($"B0_EDITOR_PLAYMODE preparation={preparation},ticks={ticks},arrived={ids.Length},centralCrossed={crossed.Count},repairs={nav.Crowd.RepairCount}");
            }finally{
                host.Dispose();nav.ReleaseCommittedHostRoot();var domain=typeof(OfflineParticipantAuthority).GetProperty("Domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);domain.GetType().GetMethod("DestroyOwnedNavigation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,null);Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
            }
        }
    }
}

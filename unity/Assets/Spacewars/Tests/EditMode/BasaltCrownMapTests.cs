using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class BasaltCrownMapTests
    {
        private static PlayableProfile Profile()=>PlayableProfile.Create(PlayableProfile.Default.CopyData(),new BasaltCrownMap());
        [Test] public void SourceSitesFullEnvelopesAndFactoryExitsRemainValid(){
            var p=Profile();var m=(BasaltCrownMap)p.AuthoredMap;
            Assert.AreEqual("7d0acb8d078451eaf0ed166a6f8b6d748925abf8",m.SourceRevision);
            Assert.AreEqual(2,m.Sites(p).Count(s=>s.Kind==PlayableBuildingKind.Headquarters));
            Assert.AreEqual(3,m.Sites(p).Count(s=>s.Kind==PlayableBuildingKind.Mine));
            Assert.AreEqual(3,m.Sites(p).Count(s=>s.Kind==PlayableBuildingKind.Outpost));
            Assert.AreEqual(new NavPoint(-40,-32),m.Headquarters(PlayableOwner.Player));
            Assert.AreEqual(new NavPoint(40,32),m.Headquarters(PlayableOwner.Enemy));
            Assert.DoesNotThrow(()=>TerritoryRules.ValidateArena(p));
            Assert.False(m.SupportsFootprint(new NavPoint(0,50),p.TankCollisionRadius));
        }
        [Test] public void OnlyTwoContinuousRampsEnterCitadelAndBothOuterBypassesStayOpen(){
            var p=Profile();var m=(BasaltCrownMap)p.AuthoredMap;double r=p.TankCollisionRadius;
            foreach(int side in new[]{-1,1}){
                Assert.True(m.SupportsSweep(new NavPoint(side*26,0),new NavPoint(side*18,0),r));
                Assert.False(m.SupportsSweep(new NavPoint(side*26,12),new NavPoint(side*18,12),r));
                for(double x=18;x<26;x+=.05)Assert.LessOrEqual(Math.Abs(m.SurfaceHeight(new NavPoint(side*(x+.05),0))-m.SurfaceHeight(new NavPoint(side*x,0))),Math.Tan(22*Math.PI/180)*.05+1e-8);
                Assert.True(m.SupportsSweep(new NavPoint(-32,side*26),new NavPoint(32,side*26),r));
            }
            Assert.AreEqual(1,m.SurfaceHeight(new NavPoint(0,0)));
            Assert.AreEqual(0,m.SurfaceHeight(new NavPoint(30,0)));
        }
        [Test] public void PartitionRoutesReachCenterAndBothEconomyFlanks(){
            var p=Profile();var m=(BasaltCrownMap)p.AuthoredMap;
            var built=m.MovementBlockers.ToList();
            foreach(var site in m.Sites(p)){
                double radius=TerritoryRules.Radius(p,site.Kind);built.Add(new NavObstacle(site.Position.X-radius,site.Position.Z-radius,site.Position.X+radius,site.Position.Z+radius));
                foreach(var slot in site.Slots){double r=Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius));built.Add(new NavObstacle(slot.Position.X-r,slot.Position.Z-r,slot.Position.X+r,slot.Position.Z+r));}
            }
            var provider=new PartitionNavigationProvider(m,new NavGeometry(m.HalfExtent,built.ToArray(),1),"fully-built");
            var clear=new NavClearanceProfile("native-map",1,"ground",1,p.TankCollisionRadius);
            var graph=LayeredNavigationGraphCompiler.Compile(provider,clear,new NavGraphProfile(new string('a',64),"distance",1,p.Navigation.GridCell),new NavGraphCompileLimits(300000,150000,600000));
            var factoryExits=m.Sites(p).SelectMany(site=>site.Slots).Select(slot=>new NavPoint(slot.Position.X+Math.Cos(slot.Heading)*p.FactoryExitDistance,slot.Position.Z+Math.Sin(slot.Heading)*p.FactoryExitDistance));
            foreach(var goal in new[]{new NavPoint(0,0),new NavPoint(-38,8),new NavPoint(38,-8),new NavPoint(40-10/Math.Sqrt(2),32-10/Math.Sqrt(2))}.Concat(factoryExits)){
                Assert.True(provider.TryLocate(new NavPoint(-40+10/Math.Sqrt(2),-32+10/Math.Sqrt(2)),clear,out var start));Assert.True(provider.TryLocate(goal,clear,out var end));
                var field=new NavSharedTerminalField(graph,new NavTerminalRegion("goal",1,end,new[]{end}),300000);
                int budget=1000000;while(field.Status==NavSolveStatus.Pending&&budget>0)budget-=field.Advance(Math.Min(10000,budget)).Consumed;
                Assert.Greater(budget,0);Assert.AreEqual(NavSolveStatus.Ready,field.Status);
                var sub=new NavRouteSubscription(1,1,1,0,0,0,0,1,1,0,1,start,end,"",NavRouteProvenance.CommandMember);
                var route=new NavIncrementalSharedRoute(field,sub);budget=1000000;while(route.Status==NavSolveStatus.Pending&&budget>0)budget-=route.Advance(Math.Min(10000,budget)).Consumed;
                Assert.Greater(budget,0);Assert.AreEqual(NavSolveStatus.Ready,route.Status,"goal "+goal);
                Assert.True(route.Route.Legs.All(l=>provider.TryTrace(l.From,l.To.Position,clear,out _,out _)));
            }
        }
        [Test] public void DuelAuthorityAndSaveRestoreBindNativeMap(){
            var p=Profile();var m=(BasaltCrownMap)p.AuthoredMap;
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Human),new OfflineParticipant("enemy-1",2,2,OfflineControl.Ai)};
            var c=m.Configuration(p,7108,roster);var a=new OfflineParticipantAuthority(c,1);
            var v=a.View("player-1");Assert.AreEqual(2,v.Participants.Count);Assert.AreEqual(1,v.Entities.Count(e=>e.Owner==PlayableOwner.Player));
            Assert.AreEqual(new NavPoint(-40,-32),v.Buildings.Single(b=>b.Owner==PlayableOwner.Player).Position);
            var bytes=a.CaptureBytes();CollectionAssert.AreEqual(bytes,OfflineParticipantAuthority.Restore(bytes,c).CaptureBytes());
        }
    }
}

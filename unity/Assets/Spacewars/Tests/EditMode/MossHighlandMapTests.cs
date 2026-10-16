using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class MossHighlandMapTests
    {
        private static PlayableProfile Profile()=>PlayableProfile.Create(PlayableProfile.Default.CopyData(),new MossHighlandMap());
        [Test] public void OriginalSitesAndCompleteBuildingEnvelopesRemainSupported(){
            var p=Profile();var m=(MossHighlandMap)p.AuthoredMap;
            Assert.AreEqual("7d0acb8d078451eaf0ed166a6f8b6d748925abf8",m.SourceRevision);
            Assert.AreEqual(64,m.HalfExtent);Assert.AreEqual(4,m.Sites(p).Count(s=>s.Kind==PlayableBuildingKind.Headquarters));Assert.AreEqual(4,m.Sites(p).Count(s=>s.Kind==PlayableBuildingKind.Mine));Assert.AreEqual(5,m.Sites(p).Count(s=>s.Kind==PlayableBuildingKind.Outpost));
            Assert.AreEqual(new NavPoint(-38,-38),m.Headquarters(PlayableOwner.Player));Assert.DoesNotThrow(()=>TerritoryRules.ValidateArena(p));
            foreach(var site in m.Sites(p)){Assert.True(m.SupportsFootprint(site.Position,TerritoryRules.Radius(p,site.Kind)),"site "+site.Id);foreach(var slot in site.Slots)Assert.True(m.SupportsFootprint(slot.Position,Math.Max(p.FactoryFootprintRadius,Math.Max(p.RefineryFootprintRadius,p.ScienceFootprintRadius))),"slot "+site.Id+"/"+slot.Id);}
        }
        [Test] public void SideOutpostsMoveTogetherAndLeaveFullBuiltBypasses(){
            var p=Profile();var map=(MossHighlandMap)p.AuthoredMap;var sites=map.Sites(p);
            var field=MossHighlandMap.PlacementField;
            foreach(double value in new[]{field.Minimum,field.Maximum}){var data=new MossHighlandProfileData();field.Write(data,value);Assert.DoesNotThrow(()=>PlayableProfile.Create(p.CopyData(),new MossHighlandMap(data)));}
            foreach(double bad in new[]{double.NaN,double.PositiveInfinity,-1,7})Assert.Throws<ArgumentOutOfRangeException>(()=>field.Write(new MossHighlandProfileData(),bad));
            var zero=new MossHighlandProfileData();field.Write(zero,0);var oldMap=new MossHighlandMap(zero);var old=oldMap.Sites(p);
            foreach(var site in sites){var before=old.Single(x=>x.Id==site.Id);bool side=site.Id>=9&&site.Id<=12;
                var delta=new NavPoint(site.Position.X-before.Position.X,site.Position.Z-before.Position.Z);
                Assert.That(Math.Abs(delta.X)+Math.Abs(delta.Z),Is.EqualTo(side?6:0).Within(1e-9));
                foreach(var slot in site.Slots){var a=before.Slots.Single(x=>x.Id==slot.Id);Assert.That(slot.Position.X-a.Position.X,Is.EqualTo(delta.X).Within(1e-9));Assert.That(slot.Position.Z-a.Position.Z,Is.EqualTo(delta.Z).Within(1e-9));}
            }
            var obstacles=map.MovementBlockers.ToList();
            foreach(var site in sites){double r=TerritoryRules.Radius(p,site.Kind);obstacles.Add(new NavObstacle(site.Position.X-r,site.Position.Z-r,site.Position.X+r,site.Position.Z+r));
                foreach(var slot in site.Slots){r=Math.Max(p.FactoryFootprintRadius,Math.Max(p.RefineryFootprintRadius,p.ScienceFootprintRadius));obstacles.Add(new NavObstacle(slot.Position.X-r,slot.Position.Z-r,slot.Position.X+r,slot.Position.Z+r));}}
            var geometry=new NavGeometry(map.HalfExtent,obstacles.ToArray(),map.Revision);
            foreach(int sign in new[]{-1,1}){
                var a=new NavPoint(sign*36,-12);var b=new NavPoint(sign*36,12);
                Assert.True(geometry.SegmentFree(a,b,1.5));Assert.True(map.SupportsSweep(a,b,1.5));
                a=new NavPoint(-12,sign*36);b=new NavPoint(12,sign*36);
                Assert.True(geometry.SegmentFree(a,b,1.5));Assert.True(map.SupportsSweep(a,b,1.5));
            }
        }
        [Test] public void NewMountainsAndRiversBlockTheirExactFootprintsInsideTheSquare(){
            var p=Profile();var m=(MossHighlandMap)p.AuthoredMap;
            Assert.AreEqual(4,m.BorderMountains.Count);Assert.AreEqual(2,m.BorderRivers.Count);
            foreach(var b in m.BorderMountains.Concat(m.BorderRivers)){
                Assert.GreaterOrEqual(b.MinX,-64);Assert.GreaterOrEqual(b.MinZ,-64);Assert.LessOrEqual(b.MaxX,64);Assert.LessOrEqual(b.MaxZ,64);
                Assert.True(m.MovementBlockers.Any(o=>o.GeometryEquals(b)));
                var v=b.Footprint.Vertices;var t=b.Footprint.Triangles;
                for(int i=0;i<t.Count;i+=3){var q=new NavPoint((v[t[i]].X+v[t[i+1]].X+v[t[i+2]].X)/3,(v[t[i]].Z+v[t[i+1]].Z+v[t[i+2]].Z)/3);Assert.False(m.SupportsFootprint(q,p.TankCollisionRadius));}
            }
            foreach(int sign in new[]{-1,1}){
                Assert.False(m.SupportsSweep(new NavPoint(sign*50,sign*50),new NavPoint(sign*60,sign*60),p.TankCollisionRadius));
                Assert.False(m.SupportsSweep(new NavPoint(sign*50,-sign*30),new NavPoint(sign*62,-sign*30),p.TankCollisionRadius));
            }
            foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1}){
                Assert.True(m.SupportsSweep(new NavPoint(sx*50,sz*16),new NavPoint(sx*50,sz*50),1.5));
                Assert.True(m.SupportsSweep(new NavPoint(sx*16,sz*50),new NavPoint(sx*50,sz*50),1.5));
            }
            Assert.That(m.OriginalRidges.Count,Is.GreaterThan(100));
            Assert.AreEqual(m.OriginalRidges.Count+4,m.Solids.Count);
            foreach(var ridge in m.OriginalRidges)Assert.True(m.Solids.Any(solid=>solid.GeometryEquals(ridge)));
        }
        [Test] public void OutpostSlotsHaveOneRearAndTwoSymmetricFrontBuildings(){
            var p=Profile();var map=(MossHighlandMap)p.AuthoredMap;Assert.AreEqual(4,map.Revision);
            foreach(var site in map.Sites(p).Where(s=>s.Kind==PlayableBuildingKind.Outpost)){
                Assert.AreEqual(3,site.Slots.Count);
                foreach(var slot in site.Slots){double dx=slot.Position.X-site.Position.X,dz=slot.Position.Z-site.Position.Z;Assert.That(Math.Sqrt(dx*dx+dz*dz),Is.EqualTo(p.SlotRingRadius).Within(1e-9));}
                var rear=site.Slots.Single(s=>s.Position.Z>site.Position.Z);Assert.That(rear.Position.X,Is.EqualTo(site.Position.X).Within(1e-9));
                var front=site.Slots.Where(s=>s.Position.Z<site.Position.Z).OrderBy(s=>s.Position.X).ToArray();Assert.AreEqual(2,front.Length);
                Assert.That(front[0].Position.Z,Is.EqualTo(front[1].Position.Z).Within(1e-9));Assert.That(front[0].Position.X+front[1].Position.X,Is.EqualTo(2*site.Position.X).Within(1e-9));
            }
        }
        [TestCase(PlayableEntityKind.Explorer)][TestCase(PlayableEntityKind.Tank)][TestCase(PlayableEntityKind.Shkval)]
        public void FourContinuousRampsPermitUnitsAndOtherCliffEdgesRemainClosed(PlayableEntityKind kind){
            var p=Profile();var m=(MossHighlandMap)p.AuthoredMap;double r=PlayableUnitRules.Radius(p,kind);
            foreach(int side in new[]{-1,1}){
                Assert.True(m.SupportsSweep(new NavPoint(side*16,0),new NavPoint(side*8,0),r));Assert.True(m.SupportsSweep(new NavPoint(0,side*16),new NavPoint(0,side*8),r));
                Assert.False(m.SupportsSweep(new NavPoint(side*16,8),new NavPoint(side*8,8),r));Assert.False(m.SupportsSweep(new NavPoint(8,side*16),new NavPoint(8,side*8),r));
            }
        }
        [Test] public void TeamAssignmentUsesAdjacentStartsAndSaveRestoreBindsTheMap(){
            var p=Profile();var m=(MossHighlandMap)p.AuthoredMap;
            var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant("seat-"+i,i+1,i%2+1,OfflineControl.Human)).ToArray();
            foreach(int seed in new[]{1,7108,19092026}){
                var c=m.Configuration(p,seed,roster);Assert.AreEqual(4,c.Assignments.Distinct().Count());
                foreach(var group in Enumerable.Range(0,4).GroupBy(i=>roster[i].Team)){int[] positions=group.Select(i=>c.Assignments[i]).ToArray();Assert.That(Math.Abs(positions[0]-positions[1]),Is.EqualTo(1).Or.EqualTo(3));}
                var a=new OfflineParticipantAuthority(c,1);foreach(var owner in c.Roster)Assert.AreEqual(1,a.View(owner.Id).Entities.Count(e=>e.Owner==a.View(owner.Id).Owner));
                var bytes=a.CaptureBytes();CollectionAssert.AreEqual(bytes,OfflineParticipantAuthority.Restore(bytes,c).CaptureBytes());
            }
            var duel=m.Configuration(p,7108,roster.Take(2).ToArray());Assert.AreEqual(2,Math.Abs(duel.Assignments[0]-duel.Assignments[1]));
        }
        [Test] public void NativeLobbyHasFourSeatsAndDoesNotReplaceAFullRoster(){
            var p=Profile();var setup=new NativeLobbyConfiguration{MossHighland=true};setup.InitializeParticipants();Assert.AreEqual(4,setup.Participants.Count);Assert.AreEqual(4,setup.Capacity(p));Assert.IsNull(setup.Validate(p,true,true));
            var before=setup.Participants.Select(x=>x.Name).ToArray();Assert.IsNotNull(setup.AddParticipant(false,0,4));CollectionAssert.AreEqual(before,setup.Participants.Select(x=>x.Name));
            using(var runtime=PlayableRuntime.CreateLobbyMatch(p,1,setup,true))Assert.AreEqual(4,runtime.Latest.Participants.Count);
        }
        [Test] public void FullyBuiltPartitionRoutesReachEveryFactoryExit(){
            var p=Profile();var m=(MossHighlandMap)p.AuthoredMap;var built=m.MovementBlockers.ToList();
            foreach(var site in m.Sites(p)){
                double r=TerritoryRules.Radius(p,site.Kind);built.Add(new NavObstacle(site.Position.X-r,site.Position.Z-r,site.Position.X+r,site.Position.Z+r));
                foreach(var slot in site.Slots){r=Math.Max(p.ScienceFootprintRadius,Math.Max(p.FactoryFootprintRadius,p.RefineryFootprintRadius));built.Add(new NavObstacle(slot.Position.X-r,slot.Position.Z-r,slot.Position.X+r,slot.Position.Z+r));}
            }
            var provider=new PartitionNavigationProvider(m,new NavGeometry(m.HalfExtent,built.ToArray(),1),"fully-built");var clear=new NavClearanceProfile("native-highland",1,"ground",1,p.TankCollisionRadius);
            var graph=LayeredNavigationGraphCompiler.Compile(provider,clear,new NavGraphProfile(new string('a',64),"distance",1,p.Navigation.GridCell),new NavGraphCompileLimits(300000,150000,600000));
            var exits=m.Sites(p).SelectMany(s=>s.Slots).Select(s=>new NavPoint(s.Position.X+Math.Cos(s.Heading)*p.FactoryExitDistance,s.Position.Z+Math.Sin(s.Heading)*p.FactoryExitDistance));
            Assert.True(provider.TryLocate(new NavPoint(-38,-48),clear,out var start));
            // Include every outside corner in the same connected full-build component.
            var corners=new[]{new NavPoint(-50,-50),new NavPoint(-50,50),new NavPoint(50,-50),new NavPoint(50,50)};
            foreach(var goal in exits.Concat(corners)){
                Assert.True(provider.TryLocate(goal,clear,out var end),goal.ToString());var field=new NavSharedTerminalField(graph,new NavTerminalRegion("exit",1,end,new[]{end}),300000);int budget=1000000;
                while(field.Status==NavSolveStatus.Pending&&budget>0)budget-=field.Advance(Math.Min(10000,budget)).Consumed;Assert.Greater(budget,0);Assert.AreEqual(NavSolveStatus.Ready,field.Status);
                var subscription=new NavRouteSubscription(1,1,1,0,0,0,0,1,1,0,1,start,end,"",NavRouteProvenance.CommandMember);var route=new NavIncrementalSharedRoute(field,subscription);budget=1000000;
                while(route.Status==NavSolveStatus.Pending&&budget>0)budget-=route.Advance(Math.Min(10000,budget)).Consumed;Assert.Greater(budget,0);Assert.AreEqual(NavSolveStatus.Ready,route.Status);Assert.True(route.Route.Legs.All(l=>provider.TryTrace(l.From,l.To.Position,clear,out _,out _)));
            }
        }
        [Test] public void FourNativeAiOwnersProgressOnOriginalTerrainWithoutRouteBarrier(){
            var p=Profile();var m=(MossHighlandMap)p.AuthoredMap;var roster=Enumerable.Range(0,4).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=m.Configuration(p,7108,roster);var cycle=new PlayableAuthorityTick(config,71);
            var origins=roster.Select(o=>cycle.ParticipantView(o.Id).Entities.Single(e=>e.Owner==cycle.ParticipantView(o.Id).Owner).Position).ToArray();var moved=new bool[4];
            using(var host=new UnityHostRouteService())for(int tick=0;tick<1800;tick++){
                bool advanced=false;for(int attempt=0;attempt<512&&!advanced;attempt++){advanced=cycle.TryAdvance();if(!advanced)host.Service(cycle,64);}Assert.True(advanced,"route barrier at "+cycle.Tick);
                for(int i=0;i<4;i++){var v=cycle.ParticipantView(roster[i].Id);Assert.GreaterOrEqual(v.ExactCredits,0);foreach(var e in v.Entities.Where(e=>e.Owner==v.Owner)){Assert.True(m.SupportsFootprint(e.Position,PlayableUnitRules.Radius(p,e.Kind)));if(!e.Position.Equals(origins[i]))moved[i]=true;}}
            }
            Assert.True(moved.All(x=>x),"Each native AI must move on this map");
            foreach(var owner in roster){var v=cycle.ParticipantView(owner.Id);Assert.Greater(v.Buildings.Count(b=>b.Owner==v.Owner),1,"AI economy progress "+owner.Id);}
            var bytes=cycle.CaptureBytes();CollectionAssert.AreEqual(bytes,PlayableAuthorityTick.RestoreBytes(bytes,config).CaptureBytes());
        }
    }
}

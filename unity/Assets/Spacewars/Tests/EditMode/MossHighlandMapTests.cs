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
            foreach(var goal in exits){
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class LobbyStartPlacementTests
    {
        private static readonly MethodInfo Create=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.NativeLobbyMatch")
            .GetMethod("Create",BindingFlags.Static|BindingFlags.NonPublic);
        private static IPlayableTerrain Map(int index)
        {
            switch(index){case 0:return ThreeCrossingsMap.Default;case 1:return new FoundryMap(new FoundryProfileData());case 2:return new AiTestMap();
                case 3:return new BasaltCrownMap();case 4:return new MossHighlandMap();default:return new WhiteMeridianMap();}
        }
        private static NativeLobbyConfiguration Setup(int map,int seed)
        {
            var s=new NativeLobbyConfiguration{Foundry=map==1,AiTestMap=map==2,BasaltCrown=map==3,MossHighland=map==4,WhiteMeridian=map==5,HasExplicitSeed=true,ExplicitSeed=seed};
            s.InitializeParticipants();
            for(int i=0;i<s.Participants.Count;i++)s.Participants[i].Team=map==1||map==4||map==5?i%2+1:i+1;
            return s;
        }
        private static OfflineMatchConfiguration Config(PlayableProfile p,NativeLobbyConfiguration s)=>(OfflineMatchConfiguration)Create.Invoke(null,new object[]{p,s});
        private static void Grouped(IReadOnlyList<OfflineStart> starts,IReadOnlyList<OfflineParticipant> roster,int[] assignment)
        {
            Assert.AreEqual(roster.Count,assignment.Distinct().Count());
            double cx=starts.Average(s=>s.Position.X),cz=starts.Average(s=>s.Position.Z);
            var ring=Enumerable.Range(0,roster.Count).OrderBy(i=>Math.Atan2(starts[assignment[i]].Position.Z-cz,starts[assignment[i]].Position.X-cx)).Select(i=>roster[i].Team).ToArray();
            int transitions=Enumerable.Range(0,ring.Length).Count(i=>ring[i]!=ring[(i+1)%ring.Length]);
            Assert.LessOrEqual(transitions,ring.Distinct().Count(),"Each team must occupy one contiguous arc, including across the wraparound.");
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)]
        public void EveryLobbyMapBindsSeededGroupsWithoutChangingRosterOrGeometry(int map)
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),Map(map));var setup=Setup(map,7);
            var before=setup.Participants.Select(p=>(p.Name,p.Team,p.Color,p.Human)).ToArray();
            var config=Config(profile,setup);var placements=new HashSet<string>();
            for(int seed=0;seed<32;seed++){
                var assignment=OfflineStartAssignment.ResolveLobby(config.Starts,config.Roster,seed);
                Grouped(config.Starts,config.Roster,assignment);placements.Add(string.Join(",",assignment));
                CollectionAssert.AreEqual(assignment,OfflineStartAssignment.ResolveLobby(config.Starts,config.Roster,seed));
                if(seed==7)CollectionAssert.AreEqual(assignment,config.Assignments);
                if(map==1||map==5){
                    var sideByTeam=config.Roster.Select((p,i)=>new{p.Team,Position=config.Starts[assignment[i]].Position}).GroupBy(p=>p.Team);
                    foreach(var team in sideByTeam)Assert.AreEqual(1,team.Select(p=>Math.Sign(map==1?p.Position.Z:p.Position.X)).Distinct().Count(),"Authored team halves must stay intact.");
                }
            }
            Assert.Greater(placements.Count,1,"A roster must not fix the side or the individual starts.");
            CollectionAssert.AreEqual(before,setup.Participants.Select(p=>(p.Name,p.Team,p.Color,p.Human)));
            CollectionAssert.AreEqual(profile.AuthoredMap.Sites(profile).Select(s=>s.Position),config.Sites.Select(s=>s.Position));
        }
        [TestCase(3,1)][TestCase(2,1,1)][TestCase(4,4)][TestCase(7,1)][TestCase(3,3,2)][TestCase(2,2,2,2)]
        public void UnequalAndMultipleTeamsRemainContiguousAndKeepEveryParticipant(params int[] sizes)
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),sizes.Sum()<=4?(IPlayableTerrain)new MossHighlandMap():new AiTestMap());
            var starts=profile.AuthoredMap.Sites(profile).Where(s=>s.Kind==PlayableBuildingKind.Headquarters).Select((s,i)=>new OfflineStart("s"+i,s.Id,s.Position,s.Position)).ToArray();
            var roster=sizes.SelectMany((n,t)=>Enumerable.Repeat(t+1,n)).Select((t,i)=>new OfflineParticipant("p"+i,i+1,t,OfflineControl.Human)).ToArray();
            for(int seed=0;seed<4;seed++)Grouped(starts,roster,OfflineStartAssignment.ResolveLobby(starts,roster,seed));
            // A renamed team and reordered roster cannot change the geographic result for the same identities/seed.
            var first=OfflineStartAssignment.ResolveLobby(starts,roster,13);
            var reversed=roster.Reverse().Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,9-p.Team,p.Control)).ToArray();
            var second=OfflineStartAssignment.ResolveLobby(starts,reversed,13);
            CollectionAssert.AreEqual(first,second.Reverse());
        }
        [Test] public void RandomizedOwnershipAndCurrentRestorePreserveAssignedHomes()
        {
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var setup=Setup(2,19);
            setup.Participants.RemoveRange(4,4);for(int i=0;i<4;i++)setup.Participants[i].Team=i%2+1;
            var c=Config(profile,setup);var a=new OfflineParticipantAuthority(c,71);var bytes=a.CaptureBytes();var b=OfflineParticipantAuthority.Restore(bytes,c);
            for(int i=0;i<c.Roster.Count;i++){
                var expected=c.Starts[c.Assignments[i]];var view=a.View(c.Roster[i].Id);var restored=b.View(c.Roster[i].Id);
                Assert.AreEqual((PlayableOwner)i,view.Owner);Assert.AreEqual(c.Roster[i].Team,view.Team);
                var home=view.Buildings.Single(x=>x.Owner==view.Owner&&x.Kind==PlayableBuildingKind.Headquarters);
                Assert.AreEqual(expected.SiteId,home.SiteId);Assert.AreEqual(expected.Position,home.Position);
                Assert.AreEqual(expected.ExplorerAnchor,view.Entities.Single(x=>x.Owner==view.Owner).Position);
                Assert.AreEqual(home.Position,restored.Buildings.Single(x=>x.Owner==view.Owner&&x.Kind==PlayableBuildingKind.Headquarters).Position);
                CollectionAssert.AreEqual(view.PublicScoutObjectives.Select(x=>x.SiteId),restored.PublicScoutObjectives.Select(x=>x.SiteId));
            }
            CollectionAssert.AreEqual(bytes,b.CaptureBytes());
            var authored=new AiTestMap().Configuration(profile,19,c.Roster.ToArray());
            CollectionAssert.AreEqual(new[]{0,1,2,3},authored.Assignments,"Direct scenario configurations retain their authored pins.");
        }
    }
}

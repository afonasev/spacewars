using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class Oct29AiMapCoverageTests
    {
        [TestCase(3)][TestCase(5)][TestCase(7)] public void RemainingAuthoredMapsAdmitObservedAirCoordinates(int number)
        {
            var source=JObject.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/Oct29AiMatches.json")));
            var entry=source["Scenarios"].Single(s=>(int)s["Number"]==number);
            IPlayableTerrain map=number==3?(IPlayableTerrain)new BasaltCrownMap():number==5?new WhiteMeridianMap():new CrimsonGroveMap();
            var profile=PlayableProfile.Create(source["Profile"].ToObject<PlayableProfileData>(),map);
            var lobby=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=(int)entry["Seed"],BasaltCrown=number==3,WhiteMeridian=number==5,CrimsonGrove=number==7,
                Participants=entry["Participants"].Select(s=>new NativeLobbyParticipant{Name=(string)s["Id"],Team=(int)s["Team"],Color=(int)s["Color"],Difficulty=(AiDifficulty)Enum.Parse(typeof(AiDifficulty),(string)s["Difficulty"])}).ToList()};
            Assert.IsNull(lobby.Validate(profile,true,true));
            var d=new PlayableDomain(profile,number,NativeLobbyMatch.Create(profile,lobby));
            try
            {
                var view=d.PlayerSnapshot(1,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,(int)entry["Seed"],PlayableOwner.Player);
                var home=view.Buildings.Single(b=>b.Owner==PlayableOwner.Player&&b.Kind==PlayableBuildingKind.Headquarters).Position;
                int id=(int)typeof(PlayableDomain).GetMethod("SpawnUnit",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(d,new object[]{home,PlayableOwner.Player,PlayableEntityKind.Hawk});
                var target=new NavPoint(home.X*.9,home.Z*.9);
                var action=new PlayableAiAction(1,"player-1",profile.ProfileId,profile.Revision,number,1,PlayableCommandKind.Move,new[]{id},target,seed:(int)entry["Seed"]);
                Assert.IsNull(d.ArmyMoveRejection(action,false));
                Assert.AreEqual(PlayableCommandStatus.Applied,d.Apply(new PlayableCommand(number,1,"player-1",PlayableCommandKind.Move,new[]{id},target),out var message),message);
            }
            finally{d.Navigation.ReleaseCommittedHostRoot();d.DestroyOwnedNavigation();}
        }
    }
}

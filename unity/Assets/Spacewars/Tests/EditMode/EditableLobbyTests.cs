using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class EditableLobbyTests
    {
        [TestCase(false)] [TestCase(true)]
        public void FfaAdditionsUseUnusedTeamsForHumansAndBots(bool human)
        {
            var setup=new NativeLobbyConfiguration{AiTestMap=true};setup.InitializeParticipants();
            setup.Participants.Clear();
            for(int i=0;i<8;i++)
            {
                Assert.IsNull(setup.AddParticipant(human&&i<4,100+i,8));
                Assert.AreEqual(i+1,setup.Participants.Last().Team);
            }
            setup.Participants.RemoveAt(2);
            Assert.IsNull(setup.AddParticipant(false,0,8));Assert.AreEqual(3,setup.Participants.Last().Team);
            Assert.AreEqual(8,setup.Participants.Select(p=>p.Team).Distinct().Count());
        }
        [TestCase(false)] [TestCase(true)]
        public void AdditionPreservesManualTeamsAndUsesFirstUnusedTeam(bool foundry)
        {
            var setup=new NativeLobbyConfiguration{Foundry=foundry};setup.InitializeParticipants();
            setup.Participants.RemoveRange(1,setup.Participants.Count-1);setup.Participants[0].Team=2;
            Assert.IsNull(setup.AddParticipant(true,19,foundry?6:2));
            CollectionAssert.AreEqual(new[]{2,1},setup.Participants.Select(p=>p.Team));
        }
        [Test] public void FoundryAlternatesAvailableTeamsAndFillsTheirCapacity()
        {
            var setup=new NativeLobbyConfiguration{Foundry=true};setup.InitializeParticipants();setup.Participants.Clear();
            for(int i=0;i<6;i++)Assert.IsNull(setup.AddParticipant(false,0,6));
            CollectionAssert.AreEqual(new[]{1,2,1,2,1,2},setup.Participants.Select(p=>p.Team));
            // Both removals are from team 1; team 2 is full. Repeating team 1 is necessary.
            setup.Participants.RemoveAt(2);setup.Participants.RemoveAt(0);
            Assert.IsNull(setup.AddParticipant(false,0,6));Assert.AreEqual(1,setup.Participants.Last().Team);
            Assert.IsNull(setup.AddParticipant(true,47,6));Assert.AreEqual(1,setup.Participants.Last().Team);
            Assert.AreEqual(3,setup.Participants.Count(p=>p.Team==1));Assert.AreEqual(3,setup.Participants.Count(p=>p.Team==2));
        }
        [Test] public void FfaAdditionAfterManualEditsFillsTheTeamGap()
        {
            var setup=new NativeLobbyConfiguration{MossHighland=true};setup.InitializeParticipants();
            setup.Participants.RemoveAt(1);setup.Participants[0].Team=8;
            Assert.IsNull(setup.AddParticipant(false,0,4));
            CollectionAssert.AreEqual(new[]{8,3,4,1},setup.Participants.Select(p=>p.Team));
            using(var runtime=PlayableRuntime.CreateLobbyMatch(PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap()),18,setup,true))
                CollectionAssert.AreEquivalent(new[]{8,3,4,1},runtime.Latest.Participants.Select(p=>p.Team));
        }
        [Test] public void DuplicateControllerAndFullMapPreserveRoster()
        {
            var setup=new NativeLobbyConfiguration{Foundry=true};setup.InitializeParticipants();setup.Participants.RemoveAt(5);
            Assert.IsNull(setup.AddParticipant(true,37,6));
            var before=setup.Participants.Select(p=>p.Name).ToArray();
            Assert.IsNotNull(setup.AddParticipant(true,37,6));Assert.IsNotNull(setup.AddParticipant(false,0,6));
            CollectionAssert.AreEqual(before,setup.Participants.Select(p=>p.Name));
            setup.Participants.RemoveAt(4);Assert.IsNull(setup.AddParticipant(false,0,6));
            Assert.AreEqual(1,setup.Participants.Count(p=>p.Human&&p.DeviceId==37));
        }
        [Test] public void CopyIsIndependentAndCapacityDoesNotReplaceParticipants()
        {
            var setup=new NativeLobbyConfiguration{Foundry=true};setup.InitializeParticipants();var copy=setup.Copy();
            copy.Participants[0].Name="Changed";Assert.AreEqual("Игрок 1",setup.Participants[0].Name);
            setup.Foundry=false;Assert.IsNotNull(setup.Validate(PlayableProfile.ThreeCrossingsDefault,true,true));Assert.AreEqual(6,setup.Participants.Count);
        }
        [Test] public void AuthoredOrdinaryRosterBindsTeamRoleAndSpectatorIngress()
        {
            // This fixture checks roster/authority ingress, not Three Crossings route prewarming.
            // Use the real open-ground authored map; keep the original 3s tick deadline.
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var setup=new NativeLobbyConfiguration();setup.InitializeParticipants();setup.Participants[0].Human=false;
            setup.Participants[0].Difficulty=AiDifficulty.Recruit;setup.Participants[1].Difficulty=AiDifficulty.Veteran;
            Assert.True(setup.Spectator);Assert.IsNull(setup.Validate(profile,false,false));
            using(var routes=new UnityHostRouteService())
            using(var runtime=PlayableRuntime.CreateLobbyMatch(profile,18,setup,true))
            {
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var scheduler=typeof(PlayableRuntime).GetField("aiScheduler",flags).GetValue(runtime);var owners=(System.Collections.IEnumerable)scheduler.GetType().GetProperty("Owners",flags).GetValue(scheduler);
                var difficulties=owners.Cast<object>().Select(owner=>((AiOwnerConfig)owner.GetType().GetField("nativeConfig",flags).GetValue(owner)).Difficulty).ToArray();CollectionAssert.AreEqual(new[]{AiDifficulty.Veteran,AiDifficulty.Recruit},difficulties);
                Assert.AreEqual(2,runtime.Latest.Participants.Count);Assert.True(runtime.Latest.Participants.All(p=>p.Control==OfflineControl.Ai));
                var own=runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player);
                Assert.AreEqual(PlayableCommandStatus.InvalidOwner,runtime.TrySubmit(new PlayableCommand(18,1,"player-1",PlayableCommandKind.Stop,new[]{own.Id})).Status);
                runtime.RequestPause(false);Assert.True(SpinWait.SpinUntil(()=>{routes.Service(runtime,64);return runtime.Latest.Tick>0;},3000));Assert.IsNull(runtime.Latest.Failure);
                runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
            }
        }
    }
}

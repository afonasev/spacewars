using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class EditableLobbyTests
    {
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
            var setup=new NativeLobbyConfiguration();setup.InitializeParticipants();setup.Participants[0].Human=false;
            setup.Participants[0].Difficulty=AiDifficulty.Recruit;setup.Participants[1].Difficulty=AiDifficulty.Veteran;
            Assert.True(setup.Spectator);Assert.IsNull(setup.Validate(PlayableProfile.ThreeCrossingsDefault,false,false));
            using(var runtime=PlayableRuntime.CreateLobbyMatch(PlayableProfile.ThreeCrossingsDefault,18,setup,true))
            {
                var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var scheduler=typeof(PlayableRuntime).GetField("aiScheduler",flags).GetValue(runtime);var owners=(System.Collections.IEnumerable)scheduler.GetType().GetProperty("Owners",flags).GetValue(scheduler);
                var difficulties=owners.Cast<object>().Select(owner=>((AiOwnerConfig)owner.GetType().GetField("nativeConfig",flags).GetValue(owner)).Difficulty).ToArray();CollectionAssert.AreEqual(new[]{AiDifficulty.Veteran,AiDifficulty.Recruit},difficulties);
                Assert.AreEqual(2,runtime.Latest.Participants.Count);Assert.True(runtime.Latest.Participants.All(p=>p.Control==OfflineControl.Ai));
                var own=runtime.Latest.Entities.First(e=>e.Owner==PlayableOwner.Player);
                Assert.AreEqual(PlayableCommandStatus.InvalidOwner,runtime.TrySubmit(new PlayableCommand(18,1,"player-1",PlayableCommandKind.Stop,new[]{own.Id})).Status);
                runtime.RequestPause(false);Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,3000));Assert.IsNull(runtime.Latest.Failure);
                runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));
            }
        }
    }
}

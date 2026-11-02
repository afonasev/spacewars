using System.Linq;
using NUnit.Framework;
using Spacewars.Simulation;
using Spacewars.Runtime;
namespace Spacewars.Tests.EditMode
{
    public sealed class NativeScreenLayoutTests
    {
        NativeLobbyConfiguration Roster(int humans,int bots)=>new NativeLobbyConfiguration{Participants=Enumerable.Range(0,humans+bots).Select(i=>new NativeLobbyParticipant{Human=i<humans,Name="Seat"+i,Team=i+1,Color=i,DeviceId=i}).ToList()};
        [TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)] public void HumansMandatoryBotsDefaultOff(int humans)
        {var s=Roster(humans,2);NativeScreenLayout.Normalize(s);CollectionAssert.AreEqual(Enumerable.Range(0,humans),s.Participants.Where(p=>p.Human).Select(p=>p.ScreenSlot));Assert.True(s.Participants.Where(p=>!p.Human).All(p=>p.ScreenSlot==-1));}
        [Test] public void SwapKeepsOwnerDeviceTeamAndCopy()
        {var s=Roster(2,1);NativeScreenLayout.Normalize(s);var a=s.Participants[0];Assert.IsNull(NativeScreenLayout.Assign(s,a,1));Assert.AreEqual(0,s.Participants[1].ScreenSlot);Assert.AreEqual(0,a.DeviceId);Assert.AreEqual(1,a.Team);Assert.AreEqual(1,s.Copy().Participants[0].ScreenSlot);Assert.NotNull(NativeScreenLayout.Assign(s,a,-1));}
        [Test] public void ThirdCameraCanUseAnyCornerAndKeepFreeCorner()
        {var s=Roster(2,1);NativeScreenLayout.Normalize(s);Assert.IsNull(NativeScreenLayout.Assign(s,s.Participants[2],3));Assert.AreEqual(3,NativeScreenLayout.Count(s));CollectionAssert.AreEquivalent(new[]{0,1,3},s.Participants.Select(p=>p.ScreenSlot));Assert.IsNull(NativeScreenLayout.Assign(s,s.Participants[0],2));CollectionAssert.AreEquivalent(new[]{1,2,3},s.Participants.Select(p=>p.ScreenSlot));}
        [Test] public void RemovingCameraCompactsToHalvesWithoutDroppingHuman()
        {var s=Roster(2,1);NativeScreenLayout.Normalize(s);NativeScreenLayout.Assign(s,s.Participants[2],3);NativeScreenLayout.Assign(s,s.Participants[0],2);Assert.IsNull(NativeScreenLayout.Assign(s,s.Participants[2],-1));CollectionAssert.AreEquivalent(new[]{0,1},s.Participants.Where(p=>p.Human).Select(p=>p.ScreenSlot));}
        [Test] public void FifthCameraIsRejectedAndHumansHavePriority()
        {var s=Roster(3,2);NativeScreenLayout.Normalize(s);Assert.IsNull(NativeScreenLayout.Assign(s,s.Participants[3],3));Assert.NotNull(NativeScreenLayout.Assign(s,s.Participants[4],0));s.Participants[4].Human=true;NativeScreenLayout.Normalize(s);Assert.AreEqual(-1,s.Participants[3].ScreenSlot);Assert.AreEqual(4,NativeScreenLayout.Count(s));}
        [Test] public void SpectatorAlwaysClearsAndRejectsAssignments()
        {var s=Roster(0,4);s.Participants[0].ScreenSlot=2;NativeScreenLayout.Normalize(s);Assert.True(s.Participants.All(p=>p.ScreenSlot==-1));Assert.AreEqual(0,NativeScreenLayout.Count(s));Assert.NotNull(NativeScreenLayout.Assign(s,s.Participants[0],0));}
        [Test] public void ActivityProjectsIssuersNotDistantMovementDestination()
        {
            var p=PlayableProfile.Default;var d=new PlayableDomain(p,1,false);var v=d.PlayerSnapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            var unit=v.Entities.First(e=>e.Owner==v.Owner);var command=new PlayableCommand(1,1,"player-1",PlayableCommandKind.Move,new[]{unit.Id},new NavPoint(40,40),origin:PlayableOrderOrigin.Ai,source:"test",jobId:1,actionId:1);
            d.RecordBotCameraActivity(command);Assert.AreEqual(unit.Position,d.BotCameraActivities.Single().Position);
            d.RecordBotCameraActivity(new PlayableCommand(1,2,"player-1",PlayableCommandKind.Move,new[]{unit.Id}));Assert.AreEqual(1,d.BotCameraActivities.Single().Sequence,"human input cannot redirect AI activity");
            d.DestroyOwnedNavigation();
        }
    }
}

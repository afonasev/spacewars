using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class CampaignOpeningStateTests
    {
        static OfflineMatchConfiguration Config()=>CampaignReconOpening.Configuration(PlayableProfile.Create(PlayableProfile.Default.CopyData(),new CampaignOpeningMap()));
        static void Damage(OfflineParticipantAuthority a,int id,int amount)=>OfflineParticipantAuthorityTests.Call(a,"Damage",id,amount);
        static void Place(OfflineParticipantAuthority a,int id,NavPoint point)
        {
            Assert.True(a.Navigation.Crowd.TryGet(id,out var unit));var args=new object[]{point,unit.Radius,default(NavLocation)};Assert.True((bool)typeof(NavCrowd).GetMethod("Locate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(a.Navigation.Crowd,args));var location=(NavLocation)args[2];
            typeof(NavUnit).GetProperty("Position").SetValue(unit,point);typeof(NavUnit).GetProperty("Location").SetValue(unit,location);
        }
        [Test] public void BaseLessGenesisAndArmyDefeatRoundTrip()
        {
            var config=Config();var a=new OfflineParticipantAuthority(config,51);a.Step(1d/30);
            var v=a.View("player-1");Assert.AreEqual(1,v.Entities.Count(e=>e.Owner==PlayableOwner.Player));Assert.False(v.Buildings.Any(b=>b.Owner==PlayableOwner.Player));Assert.AreEqual(PlayableMatchOutcome.Playing,v.Outcome);
            Damage(a,v.Entities.Single(e=>e.Owner==PlayableOwner.Player).Id,100000);a.Step(1d/30);Assert.AreEqual(PlayableMatchOutcome.PlayerLost,a.View("player-1").Outcome);
            var bytes=a.CaptureBytes();var restored=OfflineParticipantAuthority.Restore(bytes,config);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());Assert.AreEqual(PlayableMatchOutcome.PlayerLost,restored.View("player-1").Outcome);
        }
        [Test] public void TransferKeepsIdentityLossesAndOneTimeReceiptsAcrossRestore()
        {
            var config=Config();var a=new OfflineParticipantAuthority(config,52);var original=a.View("player-1").Entities.OrderBy(e=>e.Id).ToArray();var player=original.Single(e=>e.Owner==PlayableOwner.Player);var reserve=original.First(e=>e.Owner==PlayableOwner.Third);
            Damage(a,player.Id,7);Damage(a,reserve.Id,9);Place(a,player.Id,CampaignReconOpening.Rendezvous);a.Step(1d/30);
            var v=a.View("player-1");Assert.True(v.Mission.Reinforced);Assert.AreEqual(player.Health-7,v.Entities.Single(e=>e.Id==player.Id).Health);Assert.AreEqual(reserve.Health-9,v.Entities.Single(e=>e.Id==reserve.Id).Health);Assert.AreEqual(4,v.Entities.Count(e=>e.Owner==PlayableOwner.Player));CollectionAssert.AreEqual(original.Select(e=>e.Id),v.Entities.OrderBy(e=>e.Id).Select(e=>e.Id));
            var bytes=a.CaptureBytes();var restored=OfflineParticipantAuthority.Restore(bytes,config);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
            for(int i=0;i<3;i++)restored.Step(1d/30);Assert.AreEqual(4,restored.View("player-1").Entities.Count(e=>e.Owner==PlayableOwner.Player));Assert.False(restored.View("player-1").Mission.MineDiscovered);Assert.That(restored.View("player-1").Mission.Objectives.Select(g=>g.Id),Does.Not.Contain("first-mine"));
        }
        [Test] public void VisibilityAndDestructionRevealAndCompleteOnlyTheOpeningObjective()
        {
            var config=Config();var a=new OfflineParticipantAuthority(config,53);int player=a.View("player-1").Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;
            Place(a,player,CampaignReconOpening.Rendezvous);a.Step(1d/30);Assert.False(a.View("player-1").Mission.MineDiscovered);
            Place(a,player,CampaignReconOpening.Explore);a.Step(1d/30);Assert.True(a.View("player-1").Mission.MineDiscovered);
            int mine=a.View("player-1").Buildings.Single(b=>b.Kind==PlayableBuildingKind.Mine&&b.SiteId==1).Id;Damage(a,mine,100000);a.Step(1d/30);
            var v=a.View("player-1");Assert.True(v.Mission.MineDestroyed);Assert.True(v.Mission.Objectives.Single(g=>g.Id=="first-mine").Complete);Assert.AreEqual(PlayableMatchOutcome.Playing,v.Outcome);Assert.IsNull(a.Result);
            var restored=OfflineParticipantAuthority.Restore(a.CaptureBytes(),config);Assert.True(restored.View("player-1").Mission.MineDestroyed);Assert.AreEqual(PlayableMatchOutcome.Playing,restored.View("player-1").Outcome);
        }
    }
}

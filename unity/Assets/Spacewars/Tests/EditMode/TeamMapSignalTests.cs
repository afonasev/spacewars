using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class TeamMapSignalTests
    {
        OfflineParticipantAuthority a;OfflineMatchConfiguration config;long seq;
        [SetUp] public void Setup(){config=OfflineParticipantAuthorityTests.Config();a=new OfflineParticipantAuthority(config,71);}
        PlayableCommandReceipt Send(int author,int point,PlayableCommandKind kind=PlayableCommandKind.SignalAttack)=>a.Apply(new PlayableCommand(71,++seq,config.Roster[author].Id,kind,Array.Empty<int>(),new NavPoint(point,point)));
        PlayableSnapshot View(int index)=>a.View(config.Roster[index].Id);
        [Test] public void TypedSignalsInFogReachOnlyLivingTeamWithoutChangingOrdersOrVision()
        {
            var before=View(0);Assert.AreEqual(PlayableCommandStatus.Applied,Send(0,0).Status);
            Assert.AreEqual(1,View(0).TeamSignals.Count);Assert.AreEqual(1,View(2).TeamSignals.Count);Assert.Zero(View(1).TeamSignals.Count);Assert.Zero(View(3).TeamSignals.Count);
            var after=View(0);CollectionAssert.AreEqual(before.Entities.Select(e=>e.CurrentOrder),after.Entities.Select(e=>e.CurrentOrder));
            Assert.AreEqual(before.Credits,after.Credits);Assert.AreEqual(before.Vision.IsVisible(new NavPoint(0,0)),after.Vision.IsVisible(new NavPoint(0,0)));
            var spectator=a.SpectatorView("spectator-99");Assert.Zero(spectator.Overview.TeamSignals.Count);foreach(var perspective in spectator.Players)CollectionAssert.AreEqual(View((int)perspective.Owner).TeamSignals.Select(s=>s.Id),perspective.TeamSignals.Select(s=>s.Id),"Selected spectator gets exactly that owner’s team signals");
        }
        [Test] public void FourthEvictsOnlyAuthorsOldestAndRepeatRetainsIdentity()
        {
            Send(2,8,PlayableCommandKind.SignalDefend);Send(0,0);long oldest=View(0).TeamSignals.Single(s=>s.Author==(PlayableOwner)0).Id;
            Send(0,2);Send(0,4);Send(0,6);
            var signals=View(0).TeamSignals;Assert.AreEqual(4,signals.Count);Assert.False(signals.Any(s=>s.Id==oldest));Assert.AreEqual(1,signals.Count(s=>s.Author==(PlayableOwner)2));
            var id=signals.Single(s=>s.Point.X==6).Id;Send(0,6);Assert.AreEqual(4,View(0).TeamSignals.Count);Assert.AreEqual(id,View(0).TeamSignals.Single(s=>s.Point.X==6).Id);
        }
        [Test] public void ExpiryIsExactly150SimulationTicksAndRefreshRestartsLifetime()
        {
            Send(0,0);OfflineParticipantAuthorityTests.Step(a,149);Assert.AreEqual(1,View(0).TeamSignals.Count);
            Send(0,0);Assert.AreEqual(299,View(0).TeamSignals.Single().ExpiresTick);
            OfflineParticipantAuthorityTests.Step(a,149);Assert.AreEqual(1,View(0).TeamSignals.Count);a.Step(1d/30);Assert.Zero(View(0).TeamSignals.Count);
        }
        [Test] public void CurrentWorldRoundTripPreservesSignalsIdsExpiryAndNextEviction()
        {
            Send(0,0);Send(0,2,PlayableCommandKind.SignalRecon);Send(2,4,PlayableCommandKind.SignalDefend);
            var bytes=a.CaptureBytes();var restored=OfflineParticipantAuthority.Restore(bytes,config);
            CollectionAssert.AreEqual(bytes,restored.CaptureBytes());foreach(int owner in Enumerable.Range(0,4))CollectionAssert.AreEqual(View(owner).TeamSignals.Select(s=>s.Id),restored.View(config.Roster[owner].Id).TeamSignals.Select(s=>s.Id));
        }
        [Test] public void InvalidPositionSpectatorAndEliminatedAuthorAreRejected()
        {
            Assert.AreEqual(PlayableCommandStatus.InvalidOwner,a.Apply(new PlayableCommand(71,1,"spectator-99",PlayableCommandKind.SignalAttack,null)).Status);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,a.Apply(new PlayableCommand(71,2,config.Roster[0].Id,PlayableCommandKind.SignalAttack,null,new NavPoint(double.NaN,0))).Status);
            Assert.AreEqual(PlayableCommandStatus.InvalidTarget,Send(0,10000).Status);
            var domain=OfflineParticipantAuthorityTests.Domain(a);var eliminated=(System.Collections.Generic.HashSet<PlayableOwner>)domain.GetType().GetField("eliminated",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(domain);eliminated.Add((PlayableOwner)0);
            Assert.AreEqual(PlayableCommandStatus.InvalidOwner,Send(0,0).Status);Assert.Zero(View(0).TeamSignals.Count);
        }
        [Test] public void PulseIsContinuousNonzeroBeforeFadeAndProfileIsGoverned()
        {
            var p=PlayableProfile.Default;var s=new PlayableTeamSignal(1,PlayableOwner.Player,TeamSignalKind.Attack,default,0,150);
            Assert.AreEqual(1,s.Alpha(0,p));Assert.Greater(s.Alpha(18,p),0);Assert.Less(s.Alpha(149,p),.1);Assert.Zero(s.Alpha(150,p));
            for(double tick=1;tick<150;tick+=.5)Assert.Less(Math.Abs(s.Alpha(tick,p)-s.Alpha(tick-.5,p)),.1);
            var field=PlayableProfileMetadata.Fields.Single(f=>f.Path=="teamSignals.maximumPerAuthor");var data=p.CopyData();Assert.Throws<ArgumentOutOfRangeException>(()=>NativeBalanceFields.Write(field,data,0));
        }
    }
}

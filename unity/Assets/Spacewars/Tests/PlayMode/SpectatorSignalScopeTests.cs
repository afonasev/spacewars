using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
namespace Spacewars.Input.Tests
{
    public sealed partial class OrdinaryLocalPadTests
    {
        [UnityTest] public IEnumerator SpectatorSignalsFollowSelectedOwnerAcrossOpposingTeamsAndOverview()
        {
            yield return StartOrdinary(0,configureRoster:d=>{foreach(var participant in d.Participants)participant.Human=false;});
            Call(app,"CreateSpectatorHud");Call(app,"UpdateFrame");
            var setup=Get<NativeLobbyConfiguration>(app,"matchSetup");int teamA=setup.Participants[0].Team;
            int other=setup.Participants.Select((p,i)=>new{p.Team,i}).First(x=>x.Team!=teamA).i;var ownerB=(PlayableOwner)other;
            var view=Get<PlayableSnapshot>(app,"view");
            Assert.True((bool)Call(app,"PresentAiCommunication",new AiCommunication(65001,view.Generation,view.Tick,"player-1",teamA,"F1 команда A",new NavPoint(10,10),TeamSignalKind.Attack,true,"ally-advance")));
            Assert.True((bool)Call(app,"PresentAiCommunication",new AiCommunication(65002,view.Generation,view.Tick,"foundry-"+(other+1),setup.Participants[other].Team,"F1 команда B",new NavPoint(-10,-10),TeamSignalKind.Defend,true,"ally-need-defense")));
            Call(app,"UpdateFrame");
            Label[] Lines()=>Get<VisualElement>(app,"chatFeed").Query<Label>("chat-message").ToList().ToArray();
            PlayableTeamSignal[] Marks()=>(Get<PlayableMapSurface>(app,"compactMap").GetType().GetField("teamSignals",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Get<PlayableMapSurface>(app,"compactMap")) as IReadOnlyList<PlayableTeamSignal>).ToArray();
            Assert.False(Lines().Any(l=>l.text.Contains("F1 команда")));Assert.IsEmpty(Marks());Assert.Null(Get<NativeMatchNotifications>(app,"matchNotifications").Latest);
            Call(app,"SetSpectatorPerspective",PlayableOwner.Player);Call(app,"UpdateFrame");
            Assert.True(Lines().Any(l=>l.text.Contains("F1 команда A")));Assert.False(Lines().Any(l=>l.text.Contains("F1 команда B")));
            Assert.True(Marks().Any(m=>m.Author==PlayableOwner.Player));Assert.True(Marks().All(m=>setup.Participants[(int)m.Author].Team==teamA));
            view=Get<PlayableSnapshot>(app,"view");Put(app,"view",view.WithMatchEvents(new[]{new PlayableMatchEvent(95001,view.Tick,view.Owner,MatchEventKind.MineCaptured,default)}));Call(app,"UpdateMatchNotifications");
            Assert.AreEqual("Шахта захвачена",Get<NativeMatchNotifications>(app,"matchNotifications").Latest.Text);
            yield return Capture("spectator-signals-selected-a",captureChecks:()=>{
                Assert.True(Lines().Any(l=>l.text.Contains("F1 команда A")));Assert.False(Lines().Any(l=>l.text.Contains("F1 команда B")));
                Assert.True(Marks().All(m=>setup.Participants[(int)m.Author].Team==teamA));
                foreach(var line in Lines())Inside(Get<VisualElement>(app,"root").worldBound,line.worldBound,"selected signal text");
            });
            Call(app,"SetSpectatorPerspective",ownerB);Assert.Zero(Get<NativeMatchNotifications>(app,"matchNotifications").Count);Call(app,"UpdateFrame");
            Assert.False(Lines().Any(l=>l.text.Contains("F1 команда A")));Assert.True(Lines().Any(l=>l.text.Contains("F1 команда B")));
            Assert.True(Marks().All(m=>setup.Participants[(int)m.Author].Team==setup.Participants[other].Team));
            view=Get<PlayableSnapshot>(app,"view");Put(app,"view",view.WithMatchEvents(new[]{new PlayableMatchEvent(95002,view.Tick,PlayableOwner.Player,MatchEventKind.UnitCaptured,default)}));Call(app,"UpdateMatchNotifications");Assert.Null(Get<NativeMatchNotifications>(app,"matchNotifications").Latest,"Other owner's event cannot enter the selected banner");
            Put(app,"view",view.WithMatchEvents(new[]{new PlayableMatchEvent(95003,view.Tick,ownerB,MatchEventKind.UnitCaptured,default)}));Call(app,"UpdateMatchNotifications");Assert.AreEqual("Юнит захвачен",Get<NativeMatchNotifications>(app,"matchNotifications").Latest.Text);
            Call(app,"SetSpectatorPerspective",ownerB);Call(app,"UpdateFrame");
            Assert.Null(Get<PlayableOwner?>(app,"spectatorPerspective"));Assert.Zero(Get<NativeMatchNotifications>(app,"matchNotifications").Count);Assert.IsEmpty(Marks());Assert.False(Lines().Any(l=>l.text.Contains("F1 команда")));
            Assert.AreEqual(DisplayStyle.None,Get<Label>(app,"matchNotification").resolvedStyle.display);
            yield return Capture("spectator-signals-overview-clear",captureChecks:()=>{
                Assert.IsEmpty(Marks());Assert.False(Lines().Any(l=>l.text.Contains("F1 команда")));
                Assert.AreEqual(DisplayStyle.None,Get<Label>(app,"matchNotification").resolvedStyle.display);
            });
        }
    }
}

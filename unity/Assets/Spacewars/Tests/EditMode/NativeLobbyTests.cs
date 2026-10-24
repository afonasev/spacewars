using System;
using System.Threading;
using System.Linq;
using Spacewars.Simulation.Ai;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeLobbyTests
    {
        [Test] public void ActualAuthoredCapacityAndDefaultAreValid()
        {
            var setup=new NativeLobbyConfiguration();
            Assert.AreEqual(2,setup.Capacity(PlayableProfile.ThreeCrossingsDefault));
            Assert.IsNull(setup.Validate(PlayableProfile.ThreeCrossingsDefault,true,true));
            Assert.AreEqual("ИИ",setup.MatchAiName);
        }
        [Test] public void MatchBotNamesAreUniqueStableAndDoNotMutateLobby()
        {
            var lobby=new NativeLobbyConfiguration{AiTestMap=true};lobby.InitializeParticipants();
            CollectionAssert.AreEqual(new[]{"Кадет","Боец","Ветеран"},NativeLobbyConfiguration.DifficultyNames);
            for(int i=0;i<lobby.Participants.Count;i++)lobby.Participants[i].Difficulty=(AiDifficulty)(i%3);
            for(int launch=0;launch<20;launch++)
            {
                var match=lobby.Copy();match.PrepareMatchNames();
                var names=Enumerable.Range(0,8).Select(match.ParticipantName).ToArray();
                Assert.AreEqual(8,names.Select(n=>n.Split('(')[0].Trim()).Distinct().Count());
                for(int i=0;i<8;i++)
                {
                    StringAssert.EndsWith(" ("+NativeLobbyConfiguration.DifficultyNames[i%3]+")",names[i]);
                    Assert.AreEqual(names[i],match.Copy().ParticipantName(i));
                    Assert.AreEqual("ИИ",lobby.ParticipantName(i));
                    Assert.AreEqual("ИИ",lobby.Participants[i].Name);
                }
            }
        }
        [Test] public void RuntimeBindsGeneratedIdentityAndPreservesHumanName()
        {
            var lobby=new NativeLobbyConfiguration{HumanName="Командир"};lobby.InitializeParticipants();
            lobby.Participants[1].Difficulty=AiDifficulty.Recruit;lobby.Participants[1].Name=null;
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            using(var runtime=PlayableRuntime.CreateLobbyMatch(profile,1,lobby,true))
            {
                var match=runtime.LobbyConfiguration;
                Assert.AreEqual("Командир",match.ParticipantName(0));
                StringAssert.EndsWith(" (Кадет)",match.ParticipantName(1));
                Assert.AreEqual(match.ParticipantName(1),match.MatchAiName);
                Assert.AreEqual(match.MatchAiName,runtime.LobbyConfiguration.MatchAiName);
                Assert.AreEqual("ИИ",lobby.ParticipantName(1));
            }
        }
        [Test] public void InvalidRosterCannotLaunch()
        {
            var p=PlayableProfile.ThreeCrossingsDefault;var s=new NativeLobbyConfiguration();
            s.AiPresent=false;Assert.IsNotNull(s.Validate(p,true,true));s.AiPresent=true;
            s.HumanName="  ";Assert.IsNotNull(s.Validate(p,true,true));s.HumanName="Командир";
            s.AiTeam=s.HumanTeam;Assert.IsNotNull(s.Validate(p,true,true));s.AiTeam=8;
            s.AiColor=s.HumanColor;Assert.IsNotNull(s.Validate(p,true,true));s.AiColor=7;
            Assert.IsNotNull(s.Validate(p,false,true));Assert.IsNotNull(s.Validate(p,true,false));
            Assert.IsNull(s.Validate(p,true,true));
        }
        [Test] public void LaunchBindsCopyAndPreparationHasNoTicks()
        {
            var s=new NativeLobbyConfiguration{HumanName="Командир",HumanTeam=3,AiTeam=7,HumanColor=4,AiColor=3};
            var runtime=PlayableRuntime.CreateLobbyMatch(PlayableProfile.ThreeCrossingsDefault,6,s,startPaused:true);
            try {
                s.HumanName="Changed";s.HumanColor=1;
                Thread.Sleep(120);
                Assert.AreEqual(0,runtime.Latest.Tick);
                Assert.AreEqual("Командир",runtime.LobbyConfiguration.MatchHumanName);
                Assert.AreEqual(4,runtime.LobbyConfiguration.HumanColor);
                runtime.RequestPause(false);
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>0,3000));
                Assert.IsNull(runtime.Latest.Failure);
            } finally {runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,3000));}
        }
        [Test] public void InvalidSetupCannotCreateAuthority()
        {
            var s=new NativeLobbyConfiguration{AiTeam=1};
            Assert.Throws<ArgumentException>(()=>PlayableRuntime.CreateLobbyMatch(PlayableProfile.ThreeCrossingsDefault,1,s));
        }
    }
}

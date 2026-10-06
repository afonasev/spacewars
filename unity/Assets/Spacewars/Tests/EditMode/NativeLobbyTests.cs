using System;
using System.Threading;
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
            Assert.AreEqual("ИИ Боец 1",setup.MatchAiName);
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

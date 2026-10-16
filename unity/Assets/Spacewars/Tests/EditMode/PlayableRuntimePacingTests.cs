using System;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableRuntimePacingTests
    {
        private static readonly Type Type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableRuntimePacing",true);
        private object Create(bool spectator)=>Activator.CreateInstance(Type,new object[]{1d/30d,spectator});
        private double Wait(object clock,double now,double speed=1,bool paused=false)=>(double)Type.GetMethod("WaitSeconds").Invoke(clock,new object[]{now,speed,paused});
        private void Complete(object clock,double now)=>Type.GetMethod("Complete").Invoke(clock,new object[]{now});
        [Test] public void SpectatorRecoversTicksAfterLongWorkWithoutDroppingSimulationTime()
        {
            var clock=Create(true);Assert.AreEqual(0,Wait(clock,0));Complete(clock,.399);
            Assert.AreEqual(1d/30-.399,Wait(clock,.399),1e-12);
            int ticks=1;while(Wait(clock,.399)<=0&&ticks<100){Complete(clock,.399);ticks++;}
            Assert.AreEqual(12,ticks);Assert.AreEqual(.4-.399,Wait(clock,.399),1e-12);
        }
        [Test] public void ParticipantPacingRetainsItsExistingNoCatchUpPolicy()
        {
            var clock=Create(false);Wait(clock,0);Complete(clock,.399);Assert.AreEqual(0,Wait(clock,.399),1e-12);
            Complete(clock,.4);Assert.AreEqual(.399+1d/30-.4,Wait(clock,.4),1e-12);
        }
        [TestCase(.5)][TestCase(1d)][TestCase(2d)][TestCase(4d)]
        public void RateChangeStartsNewCadenceWithoutOldSpeedDebt(double speed)
        {
            var clock=Create(true);Wait(clock,0,4);Complete(clock,1);
            // Force a different rate first so every case exercises a real transition.
            Wait(clock,1,speed==4?1:4);Assert.AreEqual(0,Wait(clock,1,speed));
            Complete(clock,1);Assert.AreEqual(1d/30/speed,Wait(clock,1,speed),1e-12);
        }
        [Test] public void PausingAndResumingDiscardDebtAndNeverCatchUpPausedTime()
        {
            var clock=Create(true);Wait(clock,0,4);Complete(clock,2);
            Assert.AreEqual(0,Wait(clock,2,4,true));Complete(clock,3);Assert.AreEqual(0,Wait(clock,3,4,true));
            Assert.AreEqual(0,Wait(clock,8,4,false));Complete(clock,8);
            Assert.AreEqual(1d/120,Wait(clock,8,4,false),1e-12);
        }
    }
}

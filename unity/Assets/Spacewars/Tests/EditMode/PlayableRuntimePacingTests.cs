using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableRuntimePacingTests
    {
        private static readonly Type Type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableRuntimePacing",true);
        private object Create(bool catchUp)=>Activator.CreateInstance(Type,new object[]{1d/30d,catchUp});
        private double Wait(object clock,double now,double speed=1,bool paused=false)=>(double)Type.GetMethod("WaitSeconds").Invoke(clock,new object[]{now,speed,paused});
        private void Complete(object clock,double now)=>Type.GetMethod("Complete").Invoke(clock,new object[]{now});
        [Test] public void OrdinaryRealtimeRuntimeRecoversOneDelayedPublication()
        {
            var runtime=new PlayableRuntime(OfflineParticipantAuthorityTests.Config(2,false),71);
            var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();int parked=0;
            try{
                domain.SnapshotCostObserverForTests=(label,begin)=>{
                    if(label=="WholeProjection"&&begin&&Interlocked.CompareExchange(ref parked,1,0)==0){entered.Set();if(!release.Wait(10000))throw new TimeoutException("Delayed publication not released.");}
                };
                Assert.True(entered.Wait(5000));long tick=domain.Tick;
                Thread.Sleep(1000);release.Set();
                Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>=tick+25||runtime.IsStopped,600),"Ordinary realtime clock must recover a transient one-second overrun rather than discard it.");
                Assert.False(runtime.IsStopped,runtime.Latest.Failure);Assert.GreaterOrEqual(runtime.Latest.Tick,tick+25);
            }finally{
                domain.SnapshotCostObserverForTests=null;release.Set();runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,5000));runtime.Dispose();domain.DestroyOwnedNavigation();
            }
        }
        [Test] public void SpectatorRecoversTicksAfterLongWorkWithoutDroppingSimulationTime()
        {
            var clock=Create(true);Assert.AreEqual(0,Wait(clock,0));Complete(clock,.399);
            Assert.AreEqual(1d/30-.399,Wait(clock,.399),1e-12);
            int ticks=1;while(Wait(clock,.399)<=0&&ticks<100){Complete(clock,.399);ticks++;}
            Assert.AreEqual(12,ticks);Assert.AreEqual(.4-.399,Wait(clock,.399),1e-12);
        }
        [Test] public void LegacyFixedPacingRetainsItsExistingNoCatchUpPolicy()
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

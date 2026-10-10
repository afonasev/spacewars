using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayableSpectatorTests
    {
        [TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)][TestCase(7)][TestCase(8)]
        public void RegisteredViewerGetsCoherentFullRosterWithoutChangingOwnerView(int count)
        {
            var a=new OfflineParticipantAuthority(OfflineParticipantAuthorityTests.Config(count),41);
            var id=a.Configuration.Roster[0].Id;var before=a.View(id);
            Assert.Throws<ArgumentException>(()=>a.SpectatorView(id));Assert.Throws<ArgumentException>(()=>a.View("spectator-99"));
            var frame=a.SpectatorView("spectator-99");Assert.AreEqual(count,frame.Players.Count);Assert.AreEqual(count,frame.Overview.Entities.Count);
            foreach(var player in frame.Players){Assert.AreEqual(frame.Overview.Tick,player.Tick);Assert.AreSame(player,frame.Perspective(player.Owner));Assert.True(frame.Overview.Vision.IsVisible(player.Entities.First(e=>e.Owner==player.Owner).Position));}
            Assert.AreSame(frame.Overview,frame.Perspective(null));
            var after=a.View(id);Assert.AreEqual(before.Credits,after.Credits);CollectionAssert.AreEqual(before.Entities.Select(e=>e.Id),after.Entities.Select(e=>e.Id));Assert.True(after.Buildings.Where(b=>b.Owner!=after.Owner).All(b=>b.PrivateState==null));
            Assert.AreEqual(PlayableCommandStatus.InvalidOwner,a.Apply(OfflineParticipantAuthorityTests.Send(a,1,"spectator-99",PlayableCommandKind.Move)).Status);
        }
        [Test] public void ViewerTracksConstructionCancellationResearchCompletionAndCenterLoss()
        {
            var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var domain=Activator.CreateInstance(type,flags,null,new object[]{PlayableProfile.Default,51L},null);
            object Call(string method,params object[] args)=>type.GetMethod(method,flags).Invoke(domain,args);
            PlayableSpectatorFrame Frame()=>(PlayableSpectatorFrame)Call("SpectatorSnapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7);
            Call("AddCredits",PlayableOwner.Player,10000d);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("BuildAt",1,2,PlayableBuildingKind.ScientificCenter,1,PlayableOwner.Player,null));
            var pending=Frame().Players[0].Buildings.Single(b=>b.Kind==PlayableBuildingKind.ScientificCenter);Assert.AreNotEqual(ConstructionPhase.Ready,pending.Phase);
            Call("AdvanceFoundations");Call("AdvanceBuildings",PlayableProfile.Default.ScienceBuildSeconds+1);
            Assert.AreEqual(ConstructionPhase.Ready,Frame().Players[0].Buildings.Single(b=>b.Id==pending.Id).Phase);
            PlayableCommandStatus Send(long sequence,PlayableCommandKind kind,PlayableResearchKind research=PlayableResearchKind.TankChassis,long order=0)=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(51,sequence,"player-1",kind,new[]{pending.Id},researchKind:research,productionOrderId:order),null);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(1,PlayableCommandKind.QueueResearch));
            var active=Frame().Players[0].OwnerResearch.Single();Assert.True(active.Active);Assert.False(active.Complete);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(2,PlayableCommandKind.CancelResearch,order:active.Id));Assert.IsEmpty(Frame().Players[0].OwnerResearch);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(3,PlayableCommandKind.QueueResearch));Call("AdvanceResearch",PlayableProfile.Default.TankChassisSeconds);
            Assert.True(Frame().Players[0].OwnerResearch.Single().Complete);Call("Damage",pending.Id,100000);
            Assert.True(Frame().Players[0].OwnerResearch.Single().Complete);Assert.False(Frame().Overview.Buildings.Any(b=>b.Id==pending.Id));
            Assert.IsEmpty(Frame().Players[1].OwnerResearch);Assert.True(Frame().Players[1].Buildings.Where(b=>b.Owner!=PlayableOwner.Enemy).All(b=>b.PrivateState==null));
        }
        [Test] public void RuntimeCapabilityAndSpeedDoNotGrantCommands()
        {
            using(var r=new PlayableRuntime(PlayableProfile.Default,9,7,autonomousOwnerAi:false,spectator:true,startPaused:true))
            {
                Assert.NotNull(r.SpectatorFrame(PlayableRuntime.LocalSpectatorId));Assert.Throws<ArgumentException>(()=>r.SpectatorFrame("player-1"));
                foreach(var rate in new[]{.5,1,2,4})r.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,rate);
                Assert.Throws<ArgumentOutOfRangeException>(()=>r.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,3));
                Assert.AreEqual(PlayableCommandStatus.InvalidOwner,r.TrySubmit(new PlayableCommand(9,1,"player-1",PlayableCommandKind.Stop,Array.Empty<int>(),default)).Status);
                Assert.True(r.Latest.Buildings.Where(b=>b.Owner!=r.Latest.Owner).All(b=>b.PrivateState==null));
                r.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>r.IsStopped,5000));
            }
            using(var r=new PlayableRuntime(PlayableProfile.Default,10,7,autonomousOwnerAi:false,startPaused:true)){Assert.Throws<ArgumentException>(()=>r.SpectatorFrame(PlayableRuntime.LocalSpectatorId));r.RequestStop();Assert.True(System.Threading.SpinWait.SpinUntil(()=>r.IsStopped,5000));}
        }
    }
}

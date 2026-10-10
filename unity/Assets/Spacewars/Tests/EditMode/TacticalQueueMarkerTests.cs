using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
namespace Spacewars.Tests.EditMode
{
    public sealed class TacticalQueueMarkerTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        object domain;NavigationSession nav;long seq;
        object Call(string n,params object[] a)=>D.GetMethod(n,F).Invoke(domain,a);
        [SetUp]public void Setup(){domain=Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,1L,false},null);nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);}
        int Spawn(double x,double z,PlayableOwner owner=PlayableOwner.Player)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,PlayableEntityKind.Tank);
        void Send(int[] ids,NavPoint point,PlayableCommandKind kind=PlayableCommandKind.Move,bool append=false,int target=0)=>Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",new PlayableCommand(1,++seq,"player-1",kind,ids,point,targetId:target,mode:append?PlayableOrderMode.Append:PlayableOrderMode.Replace),null));
        void Deliver(){while(nav.Requests.TryDequeue(out var r))nav.Answers.TryEnqueue(new NavigationAnswer(r,new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal)));nav.ApplyResults();Call("Step",1d/30);}
        PlayableSnapshot View(PlayableOwner owner=PlayableOwner.Player,bool paused=false)=>(PlayableSnapshot)Call("PlayerSnapshot",seq,RuntimeStatus.Running,paused,new PlayableRuntimeMetrics(0,0,0,0,0),null,7,owner);
        PlayableCommandReceipt Apply(PlayableCommand command){var args=new object[]{command,null};var status=(PlayableCommandStatus)D.GetMethod("Apply",F).Invoke(domain,args);return new PlayableCommandReceipt(command.Sequence,(long)D.GetProperty("Tick",F).GetValue(domain),status,(string)args[1],0,command.PlayerId);}
        [Test]public void ExplicitSelectionExpiresEvenWhileSelectedAndPollingOrTargetMotionCannotRenew()
        {
            int a=Spawn(-14,-14),enemy=Spawn(-10,-10,PlayableOwner.Enemy);Send(new[]{a},default,PlayableCommandKind.Attack,target:enemy);Send(new[]{a},new NavPoint(-12,-14),append:true);
            var model=new PlayableOrderMarkers();model.Selected(View(),new[]{a},10);Assert.AreEqual(1,model.Visible(View(),new[]{a},10.5).Count);
            var target=nav.Crowd.Units.Single(n=>n.Id==enemy);var point=new NavPoint(-9,-10);typeof(NavUnit).GetProperty("Position").SetValue(target,point);typeof(NavUnit).GetProperty("Location").SetValue(target,new NavLocation(point,NavLocation.FlatSurface));
            Assert.AreEqual(point,model.Visible(View(),new[]{a},10.9).Single().Location.Value.Position);Assert.Zero(model.Visible(View(),new[]{a},11.01).Count,"still selected; no polling renewal");
            model.Selected(View(),new[]{a},12);Assert.AreEqual(1,model.Visible(View(),new[]{a},12.1).Count);Assert.Zero(model.Visible(View(),new[]{a},13.01).Count);Assert.NotNull(QueueOf(a).Active);
        }
        PlayableUnitQueueSnapshot QueueOf(int id)=>(PlayableUnitQueueSnapshot)Call("TacticalQueueProjection",id,PlayableOwner.Player);
        [Test]public void ActualAcceptedPendingFlashesAfterDeselectionButAdmissionAndIgnoredAppendDoNot()
        {
            int a=Spawn(-14,-14);Send(new[]{a},new NavPoint(-12,-14));Deliver();var model=new PlayableOrderMarkers();model.Selected(View(),new[]{a},0);
            var command=new PlayableCommand(1,++seq,"player-1",PlayableCommandKind.Move,new[]{a},new NavPoint(-8,-14));model.Submitted(View(),command);Assert.AreEqual(1,model.Visible(View(),new[]{a},.1).Count,"transport admission alone cannot make recent");var receipt=Apply(command);model.Observe(View(),new[]{receipt},.2);
            var shown=model.Visible(View(),new[]{a},.3);Assert.AreEqual(1,shown.Count);Assert.AreEqual(QueueMarkerPhase.Pending,shown[0].Phase,"issuance window shows only the newly accepted pending, never claims it is active");
            Assert.AreEqual(1,model.Visible(View(),Array.Empty<int>(),.4).Count);Assert.Zero(model.Visible(View(),new[]{a},1.21).Count);model.Selected(View(),new[]{a},1.3);Assert.AreEqual(QueueMarkerPhase.Active,model.Visible(View(),new[]{a},1.4).Single().Phase,"selection shows actual old active while replacement remains pending");
            for(int k=0;k<64;k++)Send(new[]{a},new NavPoint(-10,-14),append:true);
            var overflow=new PlayableCommand(1,++seq,"player-1",PlayableCommandKind.Move,new[]{a},new NavPoint(-10,-14),mode:PlayableOrderMode.Append);model.Submitted(View(),overflow);receipt=Apply(overflow);model.Observe(View(),new[]{receipt},2);Assert.Zero(model.Visible(View(),Array.Empty<int>(),2.1).Count);Assert.AreEqual(1,model.Visible(View(),new[]{a},2.1).Count,"ignored overflow cannot cancel the prior selection exposure");Assert.Zero(model.Visible(View(),new[]{a},2.31).Count,"ignored overflow cannot renew it");Assert.AreEqual(64,QueueOf(a).Deferred.Count);
            var follow=new PlayableCommand(1,++seq,"player-1",PlayableCommandKind.Follow,new[]{a},targetId:a,mode:PlayableOrderMode.Append);model.Submitted(View(),follow);receipt=Apply(follow);model.Observe(View(),new[]{receipt},3);Assert.Zero(model.Visible(View(),new[]{a},3.1).Count);
        }
        [Test]public void SelectionWindowIsFogOwnerAndRestoreSafeWithoutPauseRenewal()
        {
            int a=Spawn(-14,-14),enemy=Spawn(-10,-10,PlayableOwner.Enemy);Send(new[]{a},default,PlayableCommandKind.Attack,target:enemy);var model=new PlayableOrderMarkers();model.Selected(View(),new[]{a},0);Assert.AreEqual(1,model.Visible(View(paused:true),new[]{a},.2).Count);
            var position=new NavPoint(26,26);var target=nav.Crowd.Units.Single(n=>n.Id==enemy);typeof(NavUnit).GetProperty("Position").SetValue(target,position);typeof(NavUnit).GetProperty("Location").SetValue(target,new NavLocation(position,NavLocation.FlatSurface));Assert.Zero(model.Visible(View(paused:true),new[]{a},.3).Count);Assert.Zero(model.Visible(View(PlayableOwner.Enemy),new[]{a},.4).Count);
            model.Selected(View(),new[]{a},.5);Assert.Zero(model.Visible(View(paused:true),new[]{a},1.51).Count);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-marker-test");domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"queue-marker-test"});nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);Assert.Zero(model.Visible(View(),new[]{a},2).Count);
        }
        [Test]public void TargetDeathAndRecipientSubsetClearRemoveExposureWithoutAnewEvent()
        {
            int a=Spawn(-14,-14),b=Spawn(-18,-14),enemy=Spawn(-10,-10,PlayableOwner.Enemy);Send(new[]{a,b},default,PlayableCommandKind.Attack,target:enemy);var model=new PlayableOrderMarkers();model.Selected(View(),new[]{a,b},0);Assert.AreEqual(1,model.Visible(View(),new[]{a,b},.1).Count);
            Send(new[]{a},default,PlayableCommandKind.Hold);Assert.AreEqual(1,model.Visible(View(),new[]{a,b},.2).Count);Assert.Zero(model.Visible(View(),new[]{a},.2).Count);
            var units=(System.Collections.IDictionary)D.GetField("units",F).GetValue(domain);units[enemy].GetType().GetField("Health").SetValue(units[enemy],0);Assert.Zero(model.Visible(View(),new[]{a,b},.3).Count);Assert.Zero(model.Visible(View(),new[]{a,b},1.1).Count);
        }
        [Test]public void SharedSequenceDeduplicatesExactOwnSelectionAndTracksPendingReplacement()
        {
            int a=Spawn(-14,-14),b=Spawn(-18,-14),enemy=Spawn(-10,-10,PlayableOwner.Enemy);
            Send(new[]{a,b},new NavPoint(-12,-14));Deliver();Send(new[]{a,b},new NavPoint(-10,-14),PlayableCommandKind.AttackMove,true);Send(new[]{a,b},default,PlayableCommandKind.Attack,true,enemy);
            var markers=PlayableQueueMarkers.ForSelection(View(),new[]{a,b,enemy});Assert.AreEqual(3,markers.Count);CollectionAssert.AreEqual(new[]{1,2,3},markers.Select(m=>m.Number));Assert.AreEqual(QueueMarkerPhase.Active,markers[0].Phase);Assert.False(markers[0].Attack);Assert.True(markers[1].Attack);Assert.NotNull(markers[2].Location);
            Assert.Zero(PlayableQueueMarkers.ForSelection(View(),Array.Empty<int>()).Count);Assert.Zero(PlayableQueueMarkers.ForSelection(View(PlayableOwner.Enemy),new[]{a,b}).Count);
            Send(new[]{a},new NavPoint(-8,-14));var subset=PlayableQueueMarkers.ForSelection(View(),new[]{a});Assert.AreEqual(2,subset.Count);Assert.AreEqual(QueueMarkerPhase.Active,subset[0].Phase);Assert.AreEqual(QueueMarkerPhase.Pending,subset[1].Phase);
            var remaining=PlayableQueueMarkers.ForSelection(View(),new[]{b});Assert.AreEqual(3,remaining.Count);Assert.AreEqual(markers[2].IssuanceId,remaining[2].IssuanceId);
            Send(new[]{a},default,PlayableCommandKind.Hold);Assert.Zero(PlayableQueueMarkers.ForSelection(View(),new[]{a}).Count);Assert.AreEqual(3,PlayableQueueMarkers.ForSelection(View(),new[]{b}).Count);
        }
        [Test]public void HiddenLivingTargetHasNoPositionAndRestorePauseAndReselectionUseCurrentObservation()
        {
            int a=Spawn(-14,-14),enemy=Spawn(-10,-10,PlayableOwner.Enemy);Send(new[]{a},new NavPoint(-12,-14));Deliver();Send(new[]{a},default,PlayableCommandKind.Attack,true,enemy);
            var visible=PlayableQueueMarkers.ForSelection(View(),new[]{a});Assert.NotNull(visible.Last().Location);
            var hiddenPosition=new NavPoint(26,26);var targetActor=nav.Crowd.Units.Single(n=>n.Id==enemy);typeof(NavUnit).GetProperty("Position").SetValue(targetActor,hiddenPosition);typeof(NavUnit).GetProperty("Location").SetValue(targetActor,new NavLocation(hiddenPosition,NavLocation.FlatSurface));
            var hidden=PlayableQueueMarkers.ForSelection(View(),new[]{a});Assert.AreEqual(2,hidden.Count);Assert.Null(hidden.Last().Location);Assert.AreEqual(visible.Last().IssuanceId,hidden.Last().IssuanceId);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-marker-test");domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"queue-marker-test"});nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);
            var restored=PlayableQueueMarkers.ForSelection(View(paused:true),new[]{a});Assert.AreEqual(hidden.Last().IssuanceId,restored.Last().IssuanceId);Assert.Null(restored.Last().Location);Assert.Zero(PlayableQueueMarkers.ForSelection(View(),new[]{enemy}).Count);
            Send(new[]{a},default,PlayableCommandKind.Stop);Assert.Zero(PlayableQueueMarkers.ForSelection(View(),new[]{a}).Count);
        }
    }
}

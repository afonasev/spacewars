using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class GroupOrderLifecycleTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        private object domain;private NavigationSession nav;private long sequence;
        private object Call(string name,params object[] args)=>D.GetMethod(name,F).Invoke(domain,args);
        [SetUp] public void Setup(){domain=Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,1L,false},null);nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);sequence=0;}
        private int Spawn(double x,double z,PlayableOwner owner=PlayableOwner.Player,PlayableEntityKind kind=PlayableEntityKind.Tank)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,kind);
        private PlayableCommandStatus Send(PlayableCommandKind kind,int[] ids,NavPoint goal=default(NavPoint),int target=0,PlayableOrderOrigin origin=PlayableOrderOrigin.Human)
        {return (PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind,ids,goal,targetId:target,origin:origin,source:origin==PlayableOrderOrigin.Ai?"a1-ai":null,jobId:origin==PlayableOrderOrigin.Ai?11:0,actionId:origin==PlayableOrderOrigin.Ai?12:0),null);}
        private NavigationRequest[] Drain(){var result=new List<NavigationRequest>();while(nav.Requests.TryDequeue(out var request))result.Add(request);return result.ToArray();}
        private void Deliver(){foreach(var r in Drain())nav.Answers.TryEnqueue(new NavigationAnswer(r,new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal)));nav.ApplyResults();}
        private void Contact(int id,bool value)=>typeof(NavigationSession).GetMethod("SetFormationContact",F).Invoke(nav,new object[]{id,value,0L});
        private byte[] Save()=>(byte[])Call("CaptureWorldBytes",19092026,"a1-world");
        private static object Restore(byte[] bytes)=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,19092026,"a1-world"});
        private static NavigationSession Nav(object d)=>(NavigationSession)D.GetProperty("Navigation",F).GetValue(d);

        [Test] public void S09PendingSubsetRejectsOnlyReplacedAnswerAndPreservesRemainingReservation()
        {
            int a=Spawn(-12,-12),b=Spawn(-14,-12);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Move,new[]{b,a},new NavPoint(-10,-12)));
            var original=nav.GroupFor(a);var old=Drain();Assert.AreEqual(2,old.Length);
            Assert.AreEqual(original.GroupId,nav.GroupFor(b).GroupId);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Move,new[]{a},new NavPoint(-12,-10)));
            var replacement=nav.GroupFor(a);Assert.AreNotEqual(original.GroupId,replacement.GroupId);
            CollectionAssert.AreEqual(new[]{b},nav.GroupFor(b).Members.Select(m=>m.Entity));Assert.AreEqual(2,nav.GroupFor(b).MembershipRevision);
            var newRequests=Drain();foreach(var request in old)nav.Answers.TryEnqueue(new NavigationAnswer(request,new[]{request.Goal}));
            nav.ApplyResults();Assert.AreEqual(1,nav.RejectedResults);Assert.True(nav.Crowd.Units.Single(u=>u.Id==b).Moving);Assert.True(nav.IsPending(a));
            // Save requires all executing requests back in the quiescent mailboxes.
            foreach(var request in newRequests)nav.Requests.TryEnqueue(request);
            var state=nav.CaptureState();CollectionAssert.AreEquivalent(new[]{a,b},state.Reservations.Select(r=>r.Entity));
            Assert.AreEqual(original.OrderRevision,nav.GroupFor(b).OrderRevision);
        }
        [Test] public void StableOrderingAndAtomicRejectionKeepAcceptedIntent()
        {
            int a=Spawn(-12,-12),b=Spawn(-14,-12);Send(PlayableCommandKind.Move,new[]{b,a},new NavPoint(-10,-12));var first=nav.GroupFor(a);var requests=Drain();
            CollectionAssert.AreEqual(new[]{a,b},first.Members.Select(m=>m.Entity));CollectionAssert.AreEqual(new[]{a,b},requests.Select(r=>r.Entity));
            foreach(var r in requests)nav.Requests.TryEnqueue(r);
            Assert.AreEqual(PlayableCommandStatus.InvalidEntity,Send(PlayableCommandKind.Move,new[]{a,a},new NavPoint(-10,-12)));Assert.AreEqual(first.GroupId,nav.GroupFor(a).GroupId);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Move,new[]{a,b},new NavPoint(-10,-12)));var second=nav.GroupFor(a);var next=Drain().Where(r=>r.MemberIdentity.GroupId==second.GroupId).ToArray();
            CollectionAssert.AreEqual(requests.Select(r=>r.Goal),next.Select(r=>r.Goal));Assert.AreEqual(2,second.Members.Length);
            // Defensive observations cannot change authority membership or original intent.
            second.Members[0].Entity=999;Assert.AreEqual(a,nav.GroupFor(a).Members[0].Entity);
        }
        [TestCase(PlayableCommandKind.Stop)][TestCase(PlayableCommandKind.Hold)]
        public void S10ExplicitStopAndHoldDetachOnlySubsetAndCannotResumeOldOrder(PlayableCommandKind kind)
        {
            int a=Spawn(-12,-12),b=Spawn(-14,-12);Send(PlayableCommandKind.Move,new[]{a,b},new NavPoint(-10,-12));var original=nav.GroupFor(a);var old=Drain();
            Send(kind,new[]{a});foreach(var r in old)nav.Answers.TryEnqueue(new NavigationAnswer(r,new[]{r.Goal}));nav.Step(1d/30);
            Assert.AreNotEqual(original.GroupId,nav.GroupFor(a).GroupId);Assert.AreEqual(original.GroupId,nav.GroupFor(b).GroupId);
            Assert.False(nav.Crowd.Units.Single(u=>u.Id==a).Moving);Assert.AreEqual(kind==PlayableCommandKind.Hold,nav.Crowd.Units.Single(u=>u.Id==a).Held);
            Assert.False(nav.TryForwardResume(1,a,original.GroupId,original.OrderRevision,original.Members[0].Incarnation,out _,out _));
            Assert.False(nav.CaptureState().Reservations.Any(r=>r.Entity==a));
            // HOLD changes physical blocker identity; its old answer must stay stale,
            // but the unaffected accepted order survives the required geometry replan.
            Deliver();Assert.True(nav.Crowd.Units.Single(u=>u.Id==b).Moving);Assert.AreEqual(original.GroupId,nav.GroupFor(b).GroupId);
        }
        [Test] public void S10DeathAndReusedEntityIncarnationCannotApplyOldJob()
        {
            int a=Spawn(-12,-12),b=Spawn(-14,-12);Send(PlayableCommandKind.Move,new[]{a,b},new NavPoint(-10,-12));var old=Drain();var group=nav.GroupFor(a);
            Call("Damage",a,10000);Assert.Null(nav.GroupFor(a));Assert.AreEqual(b,nav.GroupFor(b).Members.Single().Entity);
            nav.Crowd.Add(a,new NavPoint(-12,-12));Assert.AreNotEqual(group.Members[0].Incarnation,nav.Crowd.Units.Single(u=>u.Id==a).Incarnation);
            foreach(var r in old)nav.Answers.TryEnqueue(new NavigationAnswer(r,new[]{r.Goal}));nav.ApplyResults();Assert.AreEqual(1,nav.RejectedResults);Assert.False(nav.Crowd.Units.Single(u=>u.Id==a).Moving);
        }
        [Test] public void S10FollowAcceptedSubsetAndInternalStopsRetainIdentity()
        {
            int a=Spawn(-12,-12),leader=Spawn(-12,-10);Send(PlayableCommandKind.Move,new[]{a,leader},new NavPoint(-10,-12));var old=nav.GroupFor(leader);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Follow,new[]{leader,a,999999},target:leader));
            var follow=nav.GroupFor(a);Assert.AreEqual(PlayableCommandKind.Follow,follow.Kind);Assert.AreEqual(a,follow.Members.Single().Entity);Assert.AreEqual(old.GroupId,nav.GroupFor(leader).GroupId);
            Call("Step",1d/30);Assert.AreEqual(follow.GroupId,nav.GroupFor(a).GroupId);
            Call("Damage",leader,10000);Call("Step",1d/30);Assert.AreEqual(follow.GroupId,nav.GroupFor(a).GroupId);Assert.False(nav.IsPending(a));
        }
        [Test] public void S10ExplicitAttackKeepsAiProvenanceThroughInternalApproach()
        {
            int a=Spawn(-12,-12),enemy=Spawn(-4,-12,PlayableOwner.Enemy);Call("RefreshVision");
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(PlayableCommandKind.Attack,new[]{a},target:enemy,origin:PlayableOrderOrigin.Ai));var group=nav.GroupFor(a);
            Assert.AreEqual(11,group.JobId);Assert.AreEqual(12,group.ActionId);Assert.AreEqual("a1-ai",group.Source);Assert.AreEqual(PlayableOrderOrigin.Ai,group.Origin);
            nav.Move(a,new NavPoint(-10,-12));Deliver();nav.Stop(a,false);nav.Move(a,new NavPoint(-10,-10));Assert.AreEqual(group.GroupId,nav.GroupFor(a).GroupId);
        }
        [Test] public void FormationContactUsesOwnerVisibilityAndDoesNotTurnMoveIntoAttackMove()
        {
            int a=Spawn(-12,-12),hidden=Spawn(12,12,PlayableOwner.Enemy);Send(PlayableCommandKind.Move,new[]{a},new NavPoint(-10,-12));Deliver();Call("Step",1d/30);Assert.False(nav.GroupFor(a).Members.Single().FormationReleased);
            int visible=Spawn(-8,-12,PlayableOwner.Enemy);Call("Step",1d/30);var group=nav.GroupFor(a);Assert.True(group.Members.Single().FormationReleased);Assert.AreEqual("PostMovementVisibility",group.Members[0].ContactPhase);
            var view=(PlayableSnapshot)Call("Snapshot",1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,19092026);Assert.AreEqual(0,view.Entities.Single(e=>e.Id==a).TargetId);Assert.True(nav.Crowd.Units.Single(u=>u.Id==a).Moving);
            Assert.False(nav.TryForwardResume(1,a,group.GroupId,group.OrderRevision,group.Members[0].Incarnation,out _,out _));
            Call("Damage",visible,10000);Call("Step",1d/30);Assert.True(nav.TryForwardResume(1,a,group.GroupId,group.OrderRevision,group.Members[0].Incarnation,out var from,out var goal));Assert.AreEqual(new NavPoint(-10,-12),goal);Assert.AreEqual(nav.Crowd.Units.Single(u=>u.Id==a).Position,from);
            Send(PlayableCommandKind.Stop,new[]{a});Assert.False(nav.TryForwardResume(1,a,group.GroupId,group.OrderRevision,group.Members[0].Incarnation,out _,out _));Assert.False(nav.TryForwardResume(2,a,group.GroupId,group.OrderRevision,group.Members[0].Incarnation,out _,out _));
        }
        [Test] public void PendingMembershipAndReleasedHookRoundTripInsideV9WithoutLosingStatistics()
        {
            int a=Spawn(-12,-12),b=Spawn(-14,-12);Send(PlayableCommandKind.AttackMove,new[]{a,b},new NavPoint(-10,-12));Contact(a,true);var original=nav.GroupFor(a);
            var bytes=Save();var envelope=PlayableWorldState.Decode(bytes);Assert.AreEqual(9,envelope.Version);var restored=Restore(bytes);var restoredNav=Nav(restored);var group=restoredNav.GroupFor(a);
            Assert.AreEqual(original.GroupId,group.GroupId);Assert.AreEqual(original.CommandSequence,group.CommandSequence);Assert.True(group.Members[0].FormationReleased);Assert.AreEqual(2,restoredNav.PendingCount);
            var recaptured=(byte[])D.GetMethod("CaptureWorldBytes",F).Invoke(restored,new object[]{19092026,"a1-world"});CollectionAssert.AreEqual(bytes,recaptured);
            var next=Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,2L,false},null);Assert.IsEmpty(Nav(next).GroupOrders);Assert.False(Nav(next).TryForwardResume(1,a,group.GroupId,group.OrderRevision,group.Members[0].Incarnation,out _,out _));
        }
        [Test] public void LegacyV9AbsentExtensionUpgradesSingletonsAndMalformedPresentExtensionFails()
        {
            int a=Spawn(-12,-12),b=Spawn(-14,-12);Send(PlayableCommandKind.Move,new[]{a,b},new NavPoint(-10,-12));var envelope=PlayableWorldState.Decode(QueueLegacyLayout.Convert(Save(),PlayableProfile.Default,true,nav.Crowd.Units.Count));
            var restored=Nav(Restore(envelope.Encode()));Assert.AreNotEqual(restored.GroupFor(a).GroupId,restored.GroupFor(b).GroupId);Assert.AreEqual(2,restored.PendingCount);
            envelope.Navigation=envelope.Navigation.Concat(new byte[]{1,2,3,4,1,0,0,0}).ToArray();Assert.Throws<TargetInvocationException>(()=>Restore(envelope.Encode()));
        }
        [Test] public void InvalidGroupIncarnationOrCommandBindingIsRejectedOnRestore()
        {
            int a=Spawn(-12,-12);Send(PlayableCommandKind.Move,new[]{a},new NavPoint(-10,-12));var state=nav.CaptureState();state.Groups[0].Members[0].Incarnation++;
            var target=new NavigationSession(1,nav.CaptureState().Crowd==null?null:(NavGeometry)D.GetProperty("Geometry",F).GetValue(domain),PlayableProfile.Default.Navigation);
            Assert.Throws<ArgumentException>(()=>target.RestoreState(state));
        }
    }
}

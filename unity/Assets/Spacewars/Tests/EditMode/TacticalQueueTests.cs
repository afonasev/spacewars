using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class TacticalQueueTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        object domain;NavigationSession nav;long sequence;
        object Call(string name,params object[] args)=>D.GetMethod(name,F).Invoke(domain,args);
        object InternalExecution(int id,string slot){var queues=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);return queues[id].GetType().GetField(slot,F).GetValue(queues[id]);}
        [SetUp]public void Setup(){domain=Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,1L,false},null);nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);sequence=0;}
        int Spawn(double x,double z,PlayableOwner owner=PlayableOwner.Player)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,PlayableEntityKind.Tank);
        PlayableCommandStatus Send(int[] ids,NavPoint goal,PlayableCommandKind kind=PlayableCommandKind.Move,bool append=false,int target=0)=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind,ids,goal,targetId:target,origin:PlayableOrderOrigin.Human,mode:append?PlayableOrderMode.Append:PlayableOrderMode.Replace),null);
        PlayableUnitQueueSnapshot Queue(int id,PlayableOwner owner=PlayableOwner.Player)=>(PlayableUnitQueueSnapshot)Call("TacticalQueueProjection",id,owner);
        void Deliver(){while(nav.Requests.TryDequeue(out var r))nav.Answers.TryEnqueue(new NavigationAnswer(r,new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal)));nav.ApplyResults();}
        void Step(int n=1){for(int k=0;k<n;k++)Call("Step",1d/30);}
        void Advance(int n=200){for(int k=0;k<n;k++){Deliver();Step();}}
        void Restore(){var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"queue-test"});nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);}
        [Test]public void DeferredTailDoesNotRouteAndLimitIsPerMember()
        {
            int a=Spawn(-14,-14),b=Spawn(-18,-14);Send(new[]{a,b},new NavPoint(-12,-14));Deliver();
            var count=nav.CaptureState().RequestSequence;
            for(int k=0;k<64;k++)Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{a},new NavPoint(-10,-14),append:true));
            Assert.AreEqual(64,Queue(a).Deferred.Count);Assert.AreEqual(count,nav.CaptureState().RequestSequence);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{a,b},new NavPoint(-10,-12),append:true));Assert.AreEqual(64,Queue(a).Deferred.Count);Assert.AreEqual(1,Queue(b).Deferred.Count);Assert.Null(Queue(a,PlayableOwner.Enemy));
            Restore();Assert.AreEqual(64,Queue(a).Deferred.Count);Assert.AreEqual(1,Queue(b).Deferred.Count);
        }
        [Test]public void PresentGroupAllocationMetadataRejectsPartialUnknownAndMalformedTails()
        {
            int a=Spawn(-14,-14),b=Spawn(-18,-14);Send(new[]{a},new NavPoint(-12,-14));Send(new[]{b},new NavPoint(-16,-14));
            foreach(Action<NavigationSessionState> corrupt in new Action<NavigationSessionState>[]{s=>s.GroupLayoutVersion=2,s=>s.GroupLayouts=null,s=>s.GroupKeys[1]=s.GroupKeys[0],s=>s.MemberGroupKeys[0]=99999,s=>s.GroupLayouts[0]=new[]{-1}}){var state=nav.CaptureState();corrupt(state);var restored=new NavigationSession(1,((NavGeometry)D.GetProperty("Geometry",F).GetValue(domain)),PlayableProfile.Default.Navigation);Assert.Throws<ArgumentException>(()=>restored.RestoreState(state));}
        }
        [Test]public void PriorGroupWireThreeRetainsTerminalAttemptProof()
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-12,-14));Assert.True(nav.Requests.TryDequeue(out var request));nav.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>()));nav.ApplyResults();var state=nav.CaptureState();Assert.Greater(state.Crowd.Units.Single(u=>u.Id==id).FailedRequest,0);
            Assert.AreEqual(0,state.Requests.Length);Assert.AreEqual(0,state.InstalledExecutions.Length);
            var world=PlayableWorldState.Decode((byte[])Call("CaptureWorldBytes",7,"queue-test"));int tag=QueueLegacyLayout.Tag(world.Navigation,0x47525031);BitConverter.GetBytes(3).CopyTo(world.Navigation,tag+4);int tail=12+8*state.GroupKeys.Length+4*state.MemberGroupKeys.Length+state.GroupLayouts.Sum(layout=>4+4*layout.Length)+
                12+4*(state.ProbeAnswers.Length+state.AnswerMailbox.Length+state.BarrierAnswers.Length)+4+16*state.TechnicalFailures.Length+
                20+4+state.Groups.Sum(g=>37+12*g.Members.Length); // v6 corridor sidecars and v7 march rows
            world.Navigation=world.Navigation.Take(world.Navigation.Length-tail).ToArray();domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{world.Encode(),PlayableProfile.Default,7,"queue-test"});nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);Assert.AreEqual(request.Request,nav.Crowd.Units.Single(u=>u.Id==id).FailedRequest);Assert.NotNull(Queue(id).Pending);Restore();Assert.NotNull(Queue(id).Pending);
        }
        [Test]public void SpatialAndQueueSlotReuseSurvivesNonSortedAndEmptyMapRestore()
        {
            Call("BindMatchSeed",7);int a=Spawn(-14,-14),b=Spawn(-18,-14),c=Spawn(-22,-14);
            Send(new[]{b},new NavPoint(-18,-14));Deliver();Step();Send(new[]{a},new NavPoint(-14,-14));Deliver();Step();Send(new[]{c},new NavPoint(-20,-14));Deliver();Step();
            Send(new[]{b},default,PlayableCommandKind.Stop);Send(new[]{a},default,PlayableCommandKind.Stop);
            void CompareAndReuse(int id){var original=domain;Restore();Assert.AreEqual(PlayableWorldRestoreTests.Facts(original,true),PlayableWorldRestoreTests.Facts(domain,true),"slot restore "+typeof(OfflineParticipantRestoreTests).GetMethod("AuthorityDiff",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{original,domain,"domain",0}));var command=new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-12,-14),origin:PlayableOrderOrigin.Human);Assert.AreEqual(Call("Apply",command,null),D.GetMethod("Apply",F).Invoke(original,new object[]{command,null}));Assert.AreEqual(PlayableWorldRestoreTests.Facts(original,true),PlayableWorldRestoreTests.Facts(domain,true),"identical live order and LIFO reuse after restore");}
            CompareAndReuse(b);Send(new[]{a,b,c},default,PlayableCommandKind.Stop);CompareAndReuse(a);
        }
        [TestCase(3)][TestCase(4)]public void PriorQueueWireWithoutLayoutTailRemainsExplicitlyReadable(int version)
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-12,-14));var world=PlayableWorldState.Decode((byte[])Call("CaptureWorldBytes",7,"queue-test"));int size=0;
            foreach(string name in new[]{"spatialExecutions","spatialCompletions","tacticalQueues"}){var map=(System.Collections.IDictionary)D.GetField(name,F).GetValue(domain);var layout=(int[])map.GetType().GetMethod("CaptureLayout").Invoke(map,null);size+=8+4*(map.Count+layout.Length);}
            int tag=QueueLegacyLayout.Tag(world.Domain,0x54514631);BitConverter.GetBytes(4).CopyTo(world.Domain,tag+4);world.Domain=world.Domain.Take(world.Domain.Length-size).ToArray();if(version==3)world.Domain=QueueLegacyLayout.QueueFourToThree(world.Domain);domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{world.Encode(),PlayableProfile.Default,7,"queue-test"});nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);Assert.NotNull(Queue(id).Pending);Restore();Assert.NotNull(Queue(id).Pending);
        }
        [TestCase(false)][TestCase(true)]public void QueueSlotTailRejectsDuplicateAndUnknownLiveKeys(bool unknown)
        {
            int a=Spawn(-14,-14),b=Spawn(-18,-14);Send(new[]{a},new NavPoint(-12,-14));Send(new[]{b},new NavPoint(-16,-14));var world=PlayableWorldState.Decode((byte[])Call("CaptureWorldBytes",7,"queue-test"));var map=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);var layout=(int[])map.GetType().GetMethod("CaptureLayout").Invoke(map,null);int start=world.Domain.Length-8-4*(map.Count+layout.Length);BitConverter.GetBytes(unknown?99999:BitConverter.ToInt32(world.Domain,start+4)).CopyTo(world.Domain,start+8);
            var error=Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{world.Encode(),PlayableProfile.Default,7,"queue-test"}));Assert.IsInstanceOf<ArgumentException>(error.InnerException);
        }
        [Test]public void InvalidPresentQueueSlotLayoutIsRejectedWithoutWeakeningWorldBinding()
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-12,-14));var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");var world=PlayableWorldState.Decode(bytes);Array.Copy(BitConverter.GetBytes(-1),0,world.Domain,world.Domain.Length-4,4);
            var error=Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{world.Encode(),PlayableProfile.Default,7,"queue-test"}));Assert.IsInstanceOf<ArgumentException>(error.InnerException);
        }
        [Test]public void QueuedCommandRetainsAllConstructorFieldsAcrossRestore()
        {
            int id=Spawn(-14,-14);var command=new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-12,-14),pad:1,siteId:1,slotId:2,buildingKind:PlayableBuildingKind.Factory,parentId:1,productionOrderId:9,unitKind:PlayableEntityKind.Shkval,researchKind:PlayableResearchKind.ShkvalGuidance,origin:PlayableOrderOrigin.Human);
            Assert.AreEqual(PlayableCommandStatus.Applied,Call("Apply",command,null));Restore();var execution=InternalExecution(id,"Pending");var issuance=execution.GetType().GetField("Issuance",F).GetValue(execution);var saved=(PlayableCommand)issuance.GetType().GetField("Command",F).GetValue(issuance);
            Assert.AreEqual(command.Pad,saved.Pad);Assert.AreEqual(command.SiteId,saved.SiteId);Assert.AreEqual(command.SlotId,saved.SlotId);Assert.AreEqual(command.BuildingKind,saved.BuildingKind);Assert.AreEqual(command.ParentId,saved.ParentId);Assert.AreEqual(command.ProductionOrderId,saved.ProductionOrderId);Assert.AreEqual(command.UnitKind,saved.UnitKind);Assert.AreEqual(command.ResearchKind,saved.ResearchKind);
        }
        [Test]public void PendingReplacementPreservesOldBehaviorAndSurvivesOldArrival()
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-12,-14));Deliver();Step(2);var old=Queue(id).Active;
            Send(new[]{id},new NavPoint(-14,-10));Assert.AreEqual(old.CommandSequence,Queue(id).Active.CommandSequence);Assert.NotNull(Queue(id).Pending);Restore();Assert.AreEqual(old.CommandSequence,Queue(id).Active.CommandSequence);
            Step(100);Assert.True(nav.IsPending(id));Assert.Null(Queue(id).Active);Assert.Null(Queue(id).Completion);Restore();Assert.NotNull(Queue(id).Pending);
            Advance();Assert.AreEqual(2,Queue(id).Completion.Order.Sequence);Assert.Null(Queue(id).Pending);
        }
        [Test]public void IndependentRecipientsShareIssuanceAndRootWithDifferentActivationRevision()
        {
            int a=Spawn(-14,-14),b=Spawn(-22,-14);Send(new[]{a},new NavPoint(-12,-14));Send(new[]{b},new NavPoint(-18,-14));Deliver();
            Send(new[]{a,b},new NavPoint(-10,-12),append:true);long issuance=Queue(a).Deferred[0].IssuanceId;Assert.AreEqual(issuance,Queue(b).Deferred[0].IssuanceId);
            long ga=0,gb=0,ra=0,rb=0;
            for(int k=0;k<350;k++){Deliver();Step();var qa=Queue(a);var qb=Queue(b);if(qa.Active?.IssuanceId==issuance){ga=qa.Active.GroupId;ra=qa.Active.ActivationRevision;}if(qb.Active?.IssuanceId==issuance){gb=qb.Active.GroupId;rb=qb.Active.ActivationRevision;}}
            Assert.Greater(ga,0);Assert.AreEqual(ga,gb);Assert.AreNotEqual(ra,rb);
        }
        [Test]public void MixedChainAndDeadTargetSkipActivateFollowingMoveInSameTick()
        {
            int id=Spawn(-14,-14),enemy=Spawn(-10,-14,PlayableOwner.Enemy);
            Send(new[]{id},new NavPoint(-12,-14));Send(new[]{id},new NavPoint(-12,-12),PlayableCommandKind.AttackMove,true);
            Send(new[]{id},default,PlayableCommandKind.Attack,true,enemy);Send(new[]{id},new NavPoint(-14,-12),append:true);
            Call("Damage",enemy,10000);Advance(350);Assert.AreEqual(4,Queue(id).Completion.Order.Sequence);Assert.Null(Queue(id).Active);Assert.Zero(Queue(id).Deferred.Count);
        }
        [TestCase(PlayableCommandKind.Stop)][TestCase(PlayableCommandKind.Hold)]public void StopAndHoldCancelRecipientOnly(PlayableCommandKind kind)
        {
            int a=Spawn(-14,-14),b=Spawn(-18,-14);Send(new[]{a,b},new NavPoint(-12,-14));Send(new[]{a,b},new NavPoint(-10,-14),append:true);
            Send(new[]{a},default,kind);Assert.Null(Queue(a));Assert.AreEqual(1,Queue(b).Deferred.Count);Assert.False(nav.IsPending(a));
        }
        [Test]public void AppendFollowIsSilentNoOp()
        {
            int a=Spawn(-14,-14),leader=Spawn(-18,-14);Send(new[]{a},new NavPoint(-12,-14));Send(new[]{a},new NavPoint(-10,-14),append:true);var before=Queue(a);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{a},default,PlayableCommandKind.Follow,true,leader));Assert.AreEqual(before.Pending.IssuanceId,Queue(a).Pending.IssuanceId);Assert.AreEqual(1,Queue(a).Deferred.Count);
        }
        [Test]public void QueueMetadataRejectsFractionalAndInvalidValues()
        {
            var data=PlayableProfile.Default.CopyData();Assert.AreEqual(64,PlayableProfile.Default.UnitOrderQueueLimit);Assert.AreEqual("orders",PlayableProfileMetadata.Fields.Single(f=>f.Path=="system.unitOrderQueueLimit").Unit);
            data.unitOrderQueueLimit=64.5;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));data.unitOrderQueueLimit=0;Assert.Throws<ArgumentException>(()=>PlayableProfile.Create(data));
        }
        void Relocate(int id,NavPoint p){Assert.True(nav.Crowd.TryGet(id,out var actor));typeof(NavUnit).GetProperty("Position").SetValue(actor,p);typeof(NavUnit).GetProperty("Location").SetValue(actor,new NavLocation(p,NavLocation.FlatSurface));Call("RefreshVision");}
        [Test]public void AliveAnchorClearanceHoldsThenLeavingAnchorActivatesNext()
        {
            int id=Spawn(-12,-26),enemy=Spawn(2,-26,PlayableOwner.Enemy);Send(new[]{id},new NavPoint(-10,-26),PlayableCommandKind.AttackMove);Send(new[]{id},new NavPoint(-14,-26),append:true);
            Advance(180);Assert.AreEqual(1,Queue(id).Active.CommandSequence);Assert.Null(Queue(id).Completion);Assert.AreEqual(1,Queue(id).Deferred.Count);Assert.AreEqual(NavigationOutcome.Arrived,nav.Crowd.Units.Single(u=>u.Id==id).Outcome);
            Restore();Assert.AreEqual(1,Queue(id).Active.CommandSequence);Relocate(enemy,new NavPoint(4,-26));Step();Assert.AreEqual(2,Queue(id).Pending.CommandSequence);Assert.Zero(Queue(id).Deferred.Count);Advance();Assert.AreEqual(2,Queue(id).Completion.Order.Sequence);
        }
        [Test]public void HiddenAliveDirectAttackRetainsIntentWithoutHiddenRoutesAndDeathAdvancesSameTick()
        {
            int id=Spawn(-14,-14),enemy=Spawn(-9,-14,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Send(new[]{id},new NavPoint(-12,-12),append:true);Step();
            Relocate(enemy,new NavPoint(20,20));long requests=nav.CaptureState().RequestSequence;Step(5);Assert.AreEqual(requests,nav.CaptureState().RequestSequence);Assert.AreEqual(PlayableCommandKind.Attack,Queue(id).Active.Kind);Assert.Zero(Queue(id).Active.VisibleTargetId);Assert.Null(Queue(id).Completion);Assert.Null(Queue(id,PlayableOwner.Enemy));
            Restore();Assert.Zero(Queue(id).Active.VisibleTargetId);Call("Damage",enemy,10000);Step();Assert.AreEqual(2,Queue(id).Pending.CommandSequence);Assert.Zero(Queue(id).Deferred.Count);
        }
        [Test]public void OldCombatResumesWithPendingReplacementAndLatePreRestoreResultCannotApply()
        {
            int id=Spawn(-14,-14),enemy=Spawn(-8,-14,PlayableOwner.Enemy);Send(new[]{id},new NavPoint(-4,-14),PlayableCommandKind.AttackMove);Deliver();Step();Assert.False(nav.Crowd.Units.Single(u=>u.Id==id).Moving);
            Send(new[]{id},new NavPoint(-14,-10));Assert.True(nav.Requests.TryDequeue(out var old));Assert.True(nav.Requests.TryEnqueue(old));
            Relocate(enemy,new NavPoint(20,20));Step();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.True(nav.IsPending(id));Restore();
            nav.Answers.TryEnqueue(new NavigationAnswer(old,new[]{old.Goal}));nav.ApplyResults();Assert.True(nav.IsPending(id));Assert.AreEqual(1,Queue(id).Active.CommandSequence);Deliver();Assert.AreEqual(2,Queue(id).Active.CommandSequence);Assert.Null(Queue(id).Pending);
        }
        [Test]public void FailedReplacementPreservesSuspendedActiveAndDeferredIntentThroughRestore()
        {
            int id=Spawn(-14,-14),enemy=Spawn(-8,-14,PlayableOwner.Enemy);Send(new[]{id},new NavPoint(-4,-14),PlayableCommandKind.AttackMove);Deliver();Step();Send(new[]{id},new NavPoint(-14,-10));
            while(nav.Requests.TryDequeue(out var r))nav.Answers.TryEnqueue(new NavigationAnswer(r,Array.Empty<NavPoint>()));nav.ApplyResults();Assert.False(nav.IsPending(id));Assert.NotNull(Queue(id).Pending);Assert.Greater(nav.Crowd.Units.Single(u=>u.Id==id).Route.Count,0);
            Send(new[]{id},new NavPoint(-12,-10),append:true);Restore();Assert.NotNull(Queue(id).Pending);Assert.AreEqual(1,Queue(id).Deferred.Count);Relocate(enemy,new NavPoint(20,20));Step();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.NotNull(Queue(id).Pending);
        }
        [Test]public void SubsetFollowAndUnitDeathClearOnlyAddressedQueueAndRestartStartsEmpty()
        {
            int a=Spawn(-14,-14),b=Spawn(-18,-14),leader=Spawn(-20,-12);Send(new[]{a,b},new NavPoint(-12,-14));Send(new[]{a,b},new NavPoint(-10,-14),append:true);
            Send(new[]{a},default,PlayableCommandKind.Follow,target:leader);Assert.Null(Queue(a));Assert.AreEqual(1,Queue(b).Deferred.Count);Call("Damage",b,10000);Assert.Null(Queue(b));Restore();Assert.Null(Queue(b));Setup();Assert.Null(Queue(a));
        }
        [TestCase(0)][TestCase(1)][TestCase(2)]public void UnknownTruncatedOrMissingQueueExtensionRejects(int mode)
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-12,-14));Send(new[]{id},new NavPoint(-10,-14),append:true);var w=PlayableWorldState.Decode((byte[])Call("CaptureWorldBytes",7,"queue-test"));int tag=FindTag(w.Domain,0x54514631);
            if(mode==0)w.Domain[tag]^=1;if(mode==1)w.Domain=w.Domain.Take(w.Domain.Length-1).ToArray();if(mode==2)w.Domain=w.Domain.Take(tag).ToArray();
            Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{w.Encode(),PlayableProfile.Default,7,"queue-test"}));
        }
        static int FindTag(byte[] bytes,int tag){for(int k=0;k<bytes.Length-3;k++)if(BitConverter.ToInt32(bytes,k)==tag)return k;Assert.Fail("Missing tag");return -1;}
        [TestCase("prea1")][TestCase("group")][TestCase("surface")]public void RealSchema9ProducerWorldsPreserveBindingStatsAndNoInventedQueue(string format)
        {
            var root=new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());while(root!=null&&!System.IO.File.Exists(System.IO.Path.Combine(root.FullName,"unity/Tests/Fixtures/a1q-core-legacy/manifest.json")))root=root.Parent;Assert.NotNull(root);
            var bytes=System.IO.File.ReadAllBytes(System.IO.Path.Combine(root.FullName,"unity/Tests/Fixtures/a1q-core-legacy",format+"-installed.world"));var before=PlayableWorldState.Decode(bytes);
            domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,19092026,"a1q-surface-legacy"});nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);
            Assert.AreEqual(9,BitConverter.ToInt32(before.Binding,0));Assert.True(nav.Crowd.Units.Any(u=>u.Moving));Assert.True(nav.Crowd.Units.All(u=>Queue(u.Id)==null));
            var after=PlayableWorldState.Decode((byte[])Call("CaptureWorldBytes",19092026,"a1q-surface-legacy"));Assert.AreEqual(10,BitConverter.ToInt32(after.Binding,0));
            var wire=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.WorldWire",true);CollectionAssert.AreEqual(before.Binding,(byte[])wire.GetMethod("Binding",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{PlayableProfile.Default,true}));
        }
        [TestCase("Arrived")][TestCase("Failed")]public void ImpossibleActivePhaseCannotMintCompletionAfterRestore(string flag)
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-10,-14));Deliver();Step();
            var queues=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);var q=queues[id];var active=q.GetType().GetField("Active",F).GetValue(q);active.GetType().GetField(flag,F).SetValue(active,true);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"queue-test"}));
        }
        [Test]public void VisibleOutOfRangeAttackChasesAndTargetDeathActivatesTailAfterRestore()
        {
            int id=Spawn(-14,-26),enemy=Spawn(-2,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Send(new[]{id},new NavPoint(-12,-26),append:true);Step();Assert.True(nav.IsPending(id));Restore();Deliver();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Call("Damage",enemy,10000);Step();Assert.AreEqual(2,Queue(id).Pending.CommandSequence);
        }
        [Test]public void FailedReplacementKeepsAttackChaseOnOriginalIdentityAcrossRestore()
        {
            int id=Spawn(-14,-26),enemy=Spawn(-6,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Send(new[]{id},new NavPoint(-14,-10));
            while(nav.Requests.TryDequeue(out var rejected))nav.Answers.TryEnqueue(new NavigationAnswer(rejected,Array.Empty<NavPoint>()));nav.ApplyResults();
            var failed=Queue(id);Assert.NotNull(failed.Pending);Assert.AreEqual(2,failed.Pending.CommandSequence);Assert.AreEqual(1,failed.Active.CommandSequence);Assert.Null(failed.Completion);Restore();failed=Queue(id);Assert.AreEqual(2,failed.Pending.CommandSequence);Assert.AreEqual(1,failed.Active.CommandSequence);Send(new[]{id},new NavPoint(-12,-10),append:true);
            Relocate(enemy,new NavPoint(-3,-26));Step();Assert.True(nav.Requests.TryDequeue(out var chase));Assert.AreEqual(failed.Active.GroupId,chase.MemberIdentity.GroupId);Assert.AreEqual(failed.Active.ActivationRevision,chase.MemberIdentity.OrderRevision);Assert.True(nav.Requests.TryEnqueue(chase));
            var qs=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);var pendingExecution=qs[id].GetType().GetField("Pending",F).GetValue(qs[id]);Assert.True((bool)D.GetMethod("TerminalProofMatches",F).Invoke(domain,new object[]{pendingExecution,nav.Crowd.Units.Single(u=>u.Id==id)}));
            Restore();Assert.AreEqual(2,Queue(id).Pending.CommandSequence);Assert.AreEqual(1,Queue(id).Active.CommandSequence);Assert.AreEqual(1,Queue(id).Deferred.Count);Assert.Null(Queue(id).Completion);Assert.True(nav.IsPending(id));Assert.True(nav.Requests.TryDequeue(out var restoredChase));Assert.AreEqual(chase.Request,restoredChase.Request);
            nav.Answers.TryEnqueue(new NavigationAnswer(restoredChase,new SharedFlowRouter(restoredChase.Geometry,restoredChase.Profile).FindPath(restoredChase.Start,restoredChase.Goal)));nav.ApplyResults();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Restore();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.True((bool)Call("TargetAlive",enemy));
            Call("Damage",enemy,10000);Step();Assert.Null(Queue(id).Active);Assert.AreEqual(2,Queue(id).Pending.CommandSequence);Assert.AreEqual(1,Queue(id).Deferred.Count);Assert.Null(Queue(id).Completion);Restore();Assert.Null(Queue(id).Active);Assert.AreEqual(2,Queue(id).Pending.CommandSequence);Assert.AreEqual(1,Queue(id).Deferred.Count);Assert.Null(Queue(id).Completion);Send(new[]{id},default,PlayableCommandKind.Stop);Assert.Null(Queue(id));
        }
        [TestCase(false)][TestCase(true)]public void FailedReplacementRejectsForgedTerminalOrChaseRequest(bool corruptChase)
        {
            int id=Spawn(-14,-26),enemy=Spawn(-6,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Send(new[]{id},new NavPoint(-14,-10));while(nav.Requests.TryDequeue(out var rejected))nav.Answers.TryEnqueue(new NavigationAnswer(rejected,Array.Empty<NavPoint>()));nav.ApplyResults();
            Relocate(enemy,new NavPoint(-3,-26));Step();Assert.True(nav.Requests.TryDequeue(out var chase));Assert.True(nav.Requests.TryEnqueue(chase));
            var queues=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);var q=queues[id];var execution=q.GetType().GetField(corruptChase?"Active":"Pending",F).GetValue(q);
            if(corruptChase)execution.GetType().GetField("AttemptRequest",F).SetValue(execution,chase.Request+1);else{var proof=execution.GetType().GetField("TerminalFailure",F).GetValue(execution);proof.GetType().GetField("Request",F).SetValue(proof,chase.Request+1);}
            var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"queue-test"}));
        }
        [Test]public void FailedAttackChaseRestoresThenRetriesSuccessfullyWithoutStaleProof()
        {
            int id=Spawn(-14,-26),enemy=Spawn(-6,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Send(new[]{id},new NavPoint(-14,-10));while(nav.Requests.TryDequeue(out var rejected))nav.Answers.TryEnqueue(new NavigationAnswer(rejected,Array.Empty<NavPoint>()));nav.ApplyResults();
            Relocate(enemy,new NavPoint(-3,-26));Step();Assert.True(nav.Requests.TryDequeue(out var failedChase));nav.Answers.TryEnqueue(new NavigationAnswer(failedChase,Array.Empty<NavPoint>()));nav.ApplyResults();
            Assert.NotNull(InternalExecution(id,"Active").GetType().GetField("TerminalFailure",F).GetValue(InternalExecution(id,"Active")));Restore();
            var units=(System.Collections.IDictionary)D.GetField("units",F).GetValue(domain);units[id].GetType().GetField("Repath",F|BindingFlags.Public).SetValue(units[id],0d);Step();Assert.True(nav.Requests.TryDequeue(out var retry));Assert.AreEqual(Queue(id).Active.GroupId,retry.MemberIdentity.GroupId);Assert.Null(InternalExecution(id,"Active").GetType().GetField("TerminalFailure",F).GetValue(InternalExecution(id,"Active")));
            Assert.True(nav.Requests.TryEnqueue(retry));Restore();Assert.True(nav.Requests.TryDequeue(out var restoredRetry));nav.Answers.TryEnqueue(new NavigationAnswer(restoredRetry,new SharedFlowRouter(restoredRetry.Geometry,restoredRetry.Profile).FindPath(restoredRetry.Start,restoredRetry.Goal)));nav.ApplyResults();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.Null(InternalExecution(id,"Active").GetType().GetField("TerminalFailure",F).GetValue(InternalExecution(id,"Active")));Restore();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.AreEqual(2,Queue(id).Pending.CommandSequence);
        }
        [Test]public void FailedAttackProofSurvivesAcceptedReplacementBeforeNextChaseAttempt()
        {
            int id=Spawn(-14,-26),enemy=Spawn(-3,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Step();
            Assert.True(nav.Requests.TryDequeue(out var failedChase));Assert.AreEqual(Queue(id).Active.GroupId,failedChase.MemberIdentity.GroupId);nav.Answers.TryEnqueue(new NavigationAnswer(failedChase,Array.Empty<NavPoint>()));nav.ApplyResults();
            var active=InternalExecution(id,"Active");Assert.NotNull(active.GetType().GetField("TerminalFailure",F).GetValue(active));Assert.AreEqual(NavigationOutcome.Unreachable,nav.Crowd.Units.Single(u=>u.Id==id).LastFailure);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{id},new NavPoint(-14,-10)));Assert.NotNull(Queue(id).Pending);Assert.AreEqual(PlayableCommandKind.Attack,Queue(id).Active.Kind);Assert.AreNotEqual(failedChase.Request,nav.CaptureState().Requests[nav.CaptureState().PendingRequestIndices.Single()].Request);
            Assert.AreEqual(0,nav.Crowd.Units.Single(u=>u.Id==id).FailedRequest);Restore();
            Assert.AreEqual(PlayableCommandKind.Attack,Queue(id).Active.Kind);Assert.AreEqual(PlayableCommandKind.Move,Queue(id).Pending.Kind);Assert.Null(Queue(id).Completion);Assert.True(nav.IsPending(id));Assert.True((bool)Call("TargetAlive",enemy));
        }
        [Test]public void FailedAttackProofCannotMaskNewSameRootChaseAttemptDuringRestore()
        {
            int id=Spawn(-14,-26),enemy=Spawn(-3,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Step();Assert.True(nav.Requests.TryDequeue(out var first));nav.Answers.TryEnqueue(new NavigationAnswer(first,Array.Empty<NavPoint>()));nav.ApplyResults();
            var active=InternalExecution(id,"Active");var type=active.GetType();long oldRequest=(long)type.GetField("AttemptRequest",F).GetValue(active),oldOrder=(long)type.GetField("AttemptOrder",F).GetValue(active);var oldLocation=type.GetField("AttemptLocation",F).GetValue(active);var oldFailure=type.GetField("TerminalFailure",F).GetValue(active);Assert.NotNull(oldFailure);
            var units=(System.Collections.IDictionary)D.GetField("units",F).GetValue(domain);units[id].GetType().GetField("Repath",F|BindingFlags.Public).SetValue(units[id],0d);Step();Assert.True(nav.Requests.TryDequeue(out var retry));Assert.AreNotEqual(oldRequest,retry.Request);Assert.AreEqual(first.MemberIdentity.GroupId,retry.MemberIdentity.GroupId);Assert.AreEqual(first.MemberIdentity.OrderRevision,retry.MemberIdentity.OrderRevision);Assert.True(nav.Requests.TryEnqueue(retry));
            type.GetField("AttemptRequest",F).SetValue(active,oldRequest);type.GetField("AttemptOrder",F).SetValue(active,oldOrder);type.GetField("AttemptLocation",F).SetValue(active,oldLocation);type.GetField("TerminalFailure",F).SetValue(active,oldFailure);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"queue-test"}));
        }
        [Test]public void FailedAttackProofCannotMaskNewerSameRootInstalledRouteDuringRestore()
        {
            int id=Spawn(-14,-26),enemy=Spawn(-3,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Step();Assert.True(nav.Requests.TryDequeue(out var failed));nav.Answers.TryEnqueue(new NavigationAnswer(failed,Array.Empty<NavPoint>()));nav.ApplyResults();
            var active=InternalExecution(id,"Active");var type=active.GetType();long oldRequest=(long)type.GetField("AttemptRequest",F).GetValue(active),oldOrder=(long)type.GetField("AttemptOrder",F).GetValue(active);var oldLocation=type.GetField("AttemptLocation",F).GetValue(active);var oldFailure=type.GetField("TerminalFailure",F).GetValue(active);Assert.NotNull(oldFailure);
            var units=(System.Collections.IDictionary)D.GetField("units",F).GetValue(domain);units[id].GetType().GetField("Repath",F|BindingFlags.Public).SetValue(units[id],0d);Step();Assert.True(nav.Requests.TryDequeue(out var retry));Assert.AreEqual(failed.MemberIdentity.GroupId,retry.MemberIdentity.GroupId);Assert.AreEqual(failed.MemberIdentity.OrderRevision,retry.MemberIdentity.OrderRevision);
            nav.Answers.TryEnqueue(new NavigationAnswer(retry,new SharedFlowRouter(retry.Geometry,retry.Profile).FindPath(retry.Start,retry.Goal)));nav.ApplyResults();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.Greater(nav.Crowd.Units.Single(u=>u.Id==id).InstalledRequest,oldRequest);
            type.GetField("AttemptRequest",F).SetValue(active,oldRequest);type.GetField("AttemptOrder",F).SetValue(active,oldOrder);type.GetField("AttemptLocation",F).SetValue(active,oldLocation);type.GetField("TerminalFailure",F).SetValue(active,oldFailure);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"queue-test"}));
        }
        [Test]public void AttackGeometryReplanAtSameAttemptedGoalRemainsChaseAcrossRestore()
        {
            int id=Spawn(-14,-26),enemy=Spawn(-6,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Send(new[]{id},new NavPoint(-14,-10));while(nav.Requests.TryDequeue(out var rejected))nav.Answers.TryEnqueue(new NavigationAnswer(rejected,Array.Empty<NavPoint>()));nav.ApplyResults();Relocate(enemy,new NavPoint(-3,-26));Step();Assert.True(nav.Requests.TryDequeue(out var first));
            Assert.True(nav.Move(id,first.Goal));Assert.False((bool)InternalExecution(id,"Active").GetType().GetField("Interrupted",F).GetValue(InternalExecution(id,"Active")));
            Assert.True(nav.Requests.TryDequeue(out var replan));Assert.True(nav.Requests.TryEnqueue(replan));Restore();Assert.AreEqual(1,Queue(id).Active.CommandSequence);Assert.AreEqual(2,Queue(id).Pending.CommandSequence);Assert.Null(Queue(id).Completion);Assert.True(nav.IsPending(id));
        }
        [Test]public void FailedReplacementAndLaterFailedChaseKeepPriorInstalledAttackRoute()
        {
            int id=Spawn(-14,-26),enemy=Spawn(-3,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Step();Assert.True(nav.Requests.TryDequeue(out var originalChase));nav.Answers.TryEnqueue(new NavigationAnswer(originalChase,new SharedFlowRouter(originalChase.Geometry,originalChase.Profile).FindPath(originalChase.Start,originalChase.Goal)));nav.ApplyResults();
            var actor=nav.Crowd.Units.Single(u=>u.Id==id);Assert.True(actor.Moving);long installed=actor.InstalledRequest;Send(new[]{id},new NavPoint(-14,-10));Assert.True(nav.Requests.TryDequeue(out var replacement));nav.Answers.TryEnqueue(new NavigationAnswer(replacement,Array.Empty<NavPoint>()));nav.ApplyResults();Assert.True(actor.Moving);Assert.AreEqual(installed,actor.InstalledRequest);Assert.AreEqual(PlayableCommandKind.Attack,Queue(id).Active.Kind);Assert.NotNull(Queue(id).Pending);
            Assert.True((bool)Call("CombatMove",id,new NavPoint(-14,-24)));Assert.True(nav.Requests.TryDequeue(out var retry));nav.Answers.TryEnqueue(new NavigationAnswer(retry,Array.Empty<NavPoint>()));nav.ApplyResults();Assert.True(actor.Moving);Assert.AreEqual(installed,actor.InstalledRequest);Assert.AreEqual(PlayableCommandKind.Attack,Queue(id).Active.Kind);Assert.NotNull(InternalExecution(id,"Active").GetType().GetField("TerminalFailure",F).GetValue(InternalExecution(id,"Active")));
            Restore();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.AreEqual(installed,nav.Crowd.Units.Single(u=>u.Id==id).InstalledRequest);Assert.AreEqual(PlayableCommandKind.Attack,Queue(id).Active.Kind);Assert.NotNull(Queue(id).Pending);
        }
        [Test]public void NonAnchorAttackMoveMemberResumesInstalledRouteAfterContactLossAndRestore()
        {
            int a=Spawn(-14,-14),b=Spawn(-18,-14);var anchor=new NavPoint(-8,-14);Send(new[]{a,b},anchor,PlayableCommandKind.AttackMove);Deliver();
            object Execution(int entity){var queues=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);return queues[entity].GetType().GetField("Active",F).GetValue(queues[entity]);}
            var assignedA=(NavLocation?)Execution(a).GetType().GetField("Assigned",F).GetValue(Execution(a));int id=assignedA.HasValue&&!assignedA.Value.Position.Equals(anchor)?a:b;
            var assigned=(NavLocation?)Execution(id).GetType().GetField("Assigned",F).GetValue(Execution(id));Assert.True(assigned.HasValue);Assert.AreNotEqual(anchor,assigned.Value.Position);
            typeof(NavigationSession).GetMethod("SetFormationContact",F).Invoke(nav,new object[]{id,true,0L});Call("CombatStop",id);long requests=nav.CaptureState().RequestSequence;Assert.True((bool)Execution(id).GetType().GetField("Suspended",F).GetValue(Execution(id)));
            Assert.True((bool)Call("CombatMove",id,assigned.Value.Position));Assert.AreEqual(requests,nav.CaptureState().RequestSequence);Assert.False(nav.IsPending(id));Assert.False((bool)Execution(id).GetType().GetField("Interrupted",F).GetValue(Execution(id)));
            Restore();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.False((bool)Execution(id).GetType().GetField("Interrupted",F).GetValue(Execution(id)));Assert.Null(Queue(id).Completion);
        }
        [Test]public void SuccessfulInternalOverrideBeforeTacticalInstallationRestoresWithoutCompletion()
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-10,-14));Send(new[]{id},new NavPoint(-14,-18),append:true);
            Assert.True(nav.Move(id,new NavPoint(-12,-10)));Deliver();Restore();Assert.NotNull(Queue(id).Pending);Assert.Null(Queue(id).Completion);Assert.AreEqual(1,Queue(id).Deferred.Count);
            Advance();Assert.Null(Queue(id).Completion);Assert.AreEqual(1,Queue(id).Deferred.Count);
        }
        [TestCase(false)][TestCase(true)]public void SettledAttackMoveEvacuationClearsArrivalAndRestores(bool deliver)
        {
            int id=Spawn(-12,-26);Spawn(2,-26,PlayableOwner.Enemy);Send(new[]{id},new NavPoint(-10,-26),PlayableCommandKind.AttackMove);Send(new[]{id},new NavPoint(-14,-26),append:true);Advance(180);
            Assert.AreEqual(NavigationOutcome.Arrived,nav.Crowd.Units.Single(u=>u.Id==id).Outcome);Assert.True(nav.Move(id,new NavPoint(-14,-24)));if(deliver)Deliver();
            Restore();Assert.NotNull(Queue(id).Active);Assert.Null(Queue(id).Completion);Assert.AreEqual(1,Queue(id).Deferred.Count);Advance();Assert.Null(Queue(id).Completion);Assert.AreEqual(1,Queue(id).Deferred.Count);
        }
        [Test]public void SettledAttackMoveArrivalSurvivesUnrelatedGeometryRebuild()
        {
            int id=Spawn(-12,-26),enemy=Spawn(2,-26,PlayableOwner.Enemy);Send(new[]{id},new NavPoint(-10,-26),PlayableCommandKind.AttackMove);Send(new[]{id},new NavPoint(-14,-26),append:true);
            Advance(180);Assert.AreEqual(NavigationOutcome.Arrived,nav.Crowd.Units.Single(u=>u.Id==id).Outcome);
            Call("RebuildGeometry");Restore();Assert.NotNull(Queue(id).Active);Assert.AreEqual(1,Queue(id).Deferred.Count);Assert.Null(Queue(id).Completion);
            Relocate(enemy,new NavPoint(4,-26));Step();Assert.AreEqual(2,Queue(id).Pending.CommandSequence);
        }
        [TestCase(false)][TestCase(true)]public void FailedInternalOverrideDoesNotInventCompletionAndRestores(bool pending)
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-10,-14));if(!pending)Deliver();Assert.True(nav.Move(id,new NavPoint(-12,-10)));while(nav.Requests.TryDequeue(out var r))nav.Answers.TryEnqueue(new NavigationAnswer(r,Array.Empty<NavPoint>()));nav.ApplyResults();Restore();Assert.Null(Queue(id).Completion);Send(new[]{id},default,PlayableCommandKind.Stop);Assert.Null(Queue(id));
        }
        [Test]public void LiveLimitCannotShrinkBelowAcceptedDeferredCount()
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-10,-14));Send(new[]{id},new NavPoint(-12,-10),append:true);Send(new[]{id},new NavPoint(-14,-10),append:true);var data=PlayableProfile.Default.CopyData();data.revision=2;data.unitOrderQueueLimit=1;Assert.NotNull(Call("ValidateBalance",PlayableProfile.Create(data)));Assert.AreEqual(2,Queue(id).Deferred.Count);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)]public void PendingAttemptAndAssignedLocationCorruptionRejects(int mode)
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-12,-14));Deliver();Send(new[]{id},new NavPoint(-14,-10));
            var queues=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);var q=queues[id];var e=q.GetType().GetField("Pending",F).GetValue(q);
            if(mode==0)e.GetType().GetField("AttemptRequest",F).SetValue(e,0L);if(mode==1)e.GetType().GetField("Assigned",F).SetValue(e,(NavLocation?)new NavLocation(new NavPoint(-10,-10),NavLocation.FlatSurface));if(mode==2)e.GetType().GetField("Failed",F).SetValue(e,true);
            var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"queue-test"}));
        }
        [TestCase(false)][TestCase(true)]public void DeferredRootAndFifoPermutationCorruptionRejects(bool root)
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-12,-14));Send(new[]{id},new NavPoint(-10,-14),append:true);Send(new[]{id},new NavPoint(-10,-12),append:true);
            var queues=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);var q=queues[id];var tail=(System.Collections.IList)q.GetType().GetField("Deferred",F).GetValue(q);
            if(root)tail[0].GetType().GetField("GroupId",F).SetValue(tail[0],long.MaxValue);else{var first=tail[0];tail[0]=tail[1];tail[1]=first;}
            var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"queue-test"}));
        }
        [Test]public void RealLegacyTwoRevisionTransactionRegistrySurvivesSchemaMigration()
        {
            var root=new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());while(root!=null&&!System.IO.File.Exists(System.IO.Path.Combine(root.FullName,"unity/Tests/Fixtures/a1q-core-legacy/manifest.json")))root=root.Parent;Assert.NotNull(root);
            var bytes=System.IO.File.ReadAllBytes(System.IO.Path.Combine(root.FullName,"unity/Tests/Fixtures/a1q-core-legacy/terms-installed.world"));var before=PlayableWorldState.Decode(bytes);Assert.AreEqual(2,BitConverter.ToInt32(before.Domain,0));
            var data=PlayableProfile.Default.CopyData();data.revision=2;data.tankWeaponDamage=9;var profile=PlayableProfile.Create(data);
            var restored=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,profile,19092026,"a1q-surface-legacy"});var after=PlayableWorldState.Decode((byte[])D.GetMethod("CaptureWorldBytes",F).Invoke(restored,new object[]{19092026,"a1q-surface-legacy"}));Assert.AreEqual(2,BitConverter.ToInt32(after.Domain,0));
            var again=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{after.Encode(),profile,19092026,"a1q-surface-legacy"});CollectionAssert.AreEqual(after.Domain,PlayableWorldState.Decode((byte[])D.GetMethod("CaptureWorldBytes",F).Invoke(again,new object[]{19092026,"a1q-surface-legacy"})).Domain);
            data.unitOrderQueueLimit=63;Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Create(data),19092026,"a1q-surface-legacy"}));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
namespace Spacewars.Tests.EditMode
{
    public sealed class TacticalQueueTests
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly Type D=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
        object domain;NavigationSession nav;long sequence;
        UnityHostRouteService actualHost;bool actualRoutes;int actualRestores;
        object Call(string name,params object[] args)=>D.GetMethod(name,F).Invoke(domain,args);
        object InternalExecution(int id,string slot){var queues=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);return queues[id].GetType().GetField(slot,F).GetValue(queues[id]);}
        [SetUp]public void Setup(){domain=Activator.CreateInstance(D,F,null,new object[]{PlayableProfile.Default,1L,false},null);nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);sequence=0;actualRoutes=false;actualHost=null;actualRestores=0;failureFixtureWorlds=null;failureFixtureHosts=null;}
        // Only the three migrated queue scenarios opt into the real owned route
        // host. Other legacy/manual-answer fixtures keep their original lane.
        void UseActualRoutes(){actualRoutes=true;actualHost=new UnityHostRouteService();}
        void ReleaseActualWorld()
        {
            try{actualHost?.Dispose();}finally{actualHost=null;Call("DestroyOwnedNavigation");}
            Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
        }
        [TearDown]public void CleanupActualRoutes(){if(actualRoutes){ReleaseActualWorld();TestContext.WriteLine("A6 actual-host cleanup; restores="+actualRestores);}}
        List<object> failureFixtureWorlds;Dictionary<object,CertifiedRouteKernel> failureFixtureHosts;
        void UseArrivalFailureFixture(){failureFixtureWorlds=new List<object>();failureFixtureHosts=new Dictionary<object,CertifiedRouteKernel>();RememberFailureWorld();}
        void RememberFailureWorld(){if(failureFixtureWorlds!=null&&!failureFixtureWorlds.Contains(domain))failureFixtureWorlds.Add(domain);}
        void MaterializeReplacementRequest(int id)
        {
            RememberFailureWorld();Assert.NotNull(failureFixtureHosts,"explicit fixture opt-in");
            if(!failureFixtureHosts.TryGetValue(domain,out var kernel)){kernel=new CertifiedRouteKernel();failureFixtureHosts.Add(domain,kernel);}
            kernel.PauseAtPhysicalBoundary=()=>nav.Requests.Count>0;
            var binding=new PlayableRouteBinding((NavGeometry)D.GetProperty("Geometry",F).GetValue(domain),PlayableProfile.Default,nav.Admission,nav.RoutePort);
            for(int pump=0;pump<20000&&nav.Requests.Count==0;pump++){kernel.Pump(binding,4);nav.ApplyResults();binding=new PlayableRouteBinding((NavGeometry)D.GetProperty("Geometry",F).GetValue(domain),PlayableProfile.Default,nav.Admission,nav.RoutePort);}
            Assert.Greater(nav.Requests.Count,0,"real arrival publication must expose the replacement before manual failure injection");Assert.Zero(nav.AllocationCount);Assert.True(nav.IsPending(id));Assert.NotNull(nav.PendingRequestFor(id));
        }
        [TearDown]public void CleanupArrivalFailureFixture()
        {
            if(failureFixtureWorlds==null)return;RememberFailureWorld();Exception cleanup=null;TestContext.WriteLine("Primary before arrival cleanup: "+TestContext.CurrentContext.Result.Outcome+" "+TestContext.CurrentContext.Result.Message);
            foreach(var world in failureFixtureWorlds){try{
                if(failureFixtureHosts.TryGetValue(world,out var kernel)){kernel.PauseAtPhysicalBoundary=null;kernel.Clear();}
                var n=(NavigationSession)D.GetProperty("Navigation",F).GetValue(world);n.ReleaseCommittedHostRoot();D.GetMethod("DestroyOwnedNavigation",F).Invoke(world,null);
                TestContext.WriteLine("Arrival fixture census: Physical="+n.PhysicalHostBudget.PhysicalBytes+" Held="+n.PhysicalHostBudget.HeldBytes+" Scopes="+n.PhysicalHostBudget.ActiveScopes);Assert.Zero(n.PhysicalHostBudget.PhysicalBytes);Assert.Zero(n.PhysicalHostBudget.HeldBytes);Assert.Zero(n.PhysicalHostBudget.ActiveScopes);
            }catch(Exception error){if(cleanup==null)cleanup=error;else PlayableDomain.PreserveTacticalSecondary(cleanup,error);TestContext.Error.WriteLine("Arrival fixture cleanup: "+error);}}
            failureFixtureWorlds=null;failureFixtureHosts=null;if(cleanup!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanup).Throw();
        }
        int Spawn(double x,double z,PlayableOwner owner=PlayableOwner.Player,PlayableEntityKind kind=PlayableEntityKind.Tank)=>(int)Call("SpawnUnit",new NavPoint(x,z),owner,kind);
        PlayableCommandStatus Send(int[] ids,NavPoint goal,PlayableCommandKind kind=PlayableCommandKind.Move,bool append=false,int target=0)=>(PlayableCommandStatus)Call("Apply",new PlayableCommand(1,++sequence,"player-1",kind,ids,goal,targetId:target,origin:PlayableOrderOrigin.Human,mode:append?PlayableOrderMode.Append:PlayableOrderMode.Replace),null);
        PlayableCommandStatus SendAiAttackMove(int id,NavPoint goal,int site=77)
        {long action=(long)Call("AllocateAiSequence");return (PlayableCommandStatus)Call("Apply",new PlayableCommand(1,action,"player-1",PlayableCommandKind.AttackMove,new[]{id},goal,siteId:site,origin:PlayableOrderOrigin.Ai,source:PlayableAiOpeningComposition.SourceIdentity,jobId:action,actionId:action),null);}
        PlayableCommandStatus SendAiMove(int id,NavPoint goal)
        {long action=(long)Call("AllocateAiSequence");return (PlayableCommandStatus)Call("Apply",new PlayableCommand(1,action,"player-1",PlayableCommandKind.Move,new[]{id},goal,origin:PlayableOrderOrigin.Ai,source:PlayableAiOpeningComposition.SourceIdentity,jobId:action,actionId:action),null);}
        PlayableUnitQueueSnapshot Queue(int id,PlayableOwner owner=PlayableOwner.Player)=>(PlayableUnitQueueSnapshot)Call("TacticalQueueProjection",id,owner);
        void Deliver()
        {
            if(!actualRoutes){while(nav.Requests.TryDequeue(out var r))nav.Answers.TryEnqueue(new NavigationAnswer(r,new SharedFlowRouter(r.Geometry,r.Profile).FindPath(r.Start,r.Goal)));nav.ApplyResults();return;}
            // Match the old helper's synchronous drain without advancing the
            // world. Arrival-owned groups have no ordinary mailbox request yet.
            if(nav.AllocationCount>0)TestContext.WriteLine("A6 owned arrival allocation="+nav.AllocationCount+" mailbox="+nav.Requests.Count);
            for(int pump=0;pump<20000&&nav.PendingCount>0;pump++){
                actualHost.Service(new PlayableRouteBinding((NavGeometry)D.GetProperty("Geometry",F).GetValue(domain),(PlayableProfile)D.GetField("profile",F).GetValue(domain),nav.Admission,nav.RoutePort),4);
                nav.ApplyResults();
            }
            Assert.Zero(nav.PendingCount,"actual host must drain current work without a world step: "+actualHost.LastProjectionFailure);
            nav.ApplyResults();
        }
        void Step(int n=1){for(int k=0;k<n;k++)Call("Step",1d/30);}
        void Advance(int n=200){for(int k=0;k<n;k++){Deliver();Step();}}
        void Restore(){RememberFailureWorld();var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");if(actualRoutes)ReleaseActualWorld();domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"queue-test"});nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);if(actualRoutes){actualHost=new UnityHostRouteService();actualRestores++;}}
        [Test]public void DeferredTailDoesNotRouteAndLimitIsPerMember()
        {
            int a=Spawn(-14,-14),b=Spawn(-18,-14);Send(new[]{a,b},new NavPoint(-12,-14));Deliver();
            var count=nav.CaptureState().RequestSequence;
            for(int k=0;k<64;k++)Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{a},new NavPoint(-10,-14),append:true));
            Assert.AreEqual(64,Queue(a).Deferred.Count);Assert.AreEqual(count,nav.CaptureState().RequestSequence);
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{a,b},new NavPoint(-10,-12),append:true));Assert.AreEqual(64,Queue(a).Deferred.Count);Assert.AreEqual(1,Queue(b).Deferred.Count);Assert.Null(Queue(a,PlayableOwner.Enemy));
            Restore();Assert.AreEqual(64,Queue(a).Deferred.Count);Assert.AreEqual(1,Queue(b).Deferred.Count);
        }
        [Test]public void PendingAiRouteProjectionRequiresExactLiveBindingAndSurvivesColdRestore()
        {
            int id=Spawn(-14,-14);var target=new NavPoint(-12,-14);Assert.AreEqual(PlayableCommandStatus.Applied,SendAiAttackMove(id,target));
            var before=Queue(id);Assert.NotNull(before.Pending);Assert.True(before.PendingAiRouteViable);Assert.Greater(before.Pending.AiActionId,0);Assert.AreEqual(77,before.Pending.PublicObjectiveId);Assert.Null(Queue(id,PlayableOwner.Enemy));
            var request=nav.PendingRequestFor(id);Assert.NotNull(request);var requestId=request.Request;var order=request.Order;var group=before.Pending.GroupId;
            var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");Restore();
            var after=Queue(id);var restored=nav.PendingRequestFor(id);Assert.True(after.PendingAiRouteViable);Assert.AreEqual(group,after.Pending.GroupId);Assert.AreEqual(requestId,restored.Request);Assert.AreEqual(order,restored.Order);
            CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"queue-test"));
        }
        [TestCase("failed")][TestCase("interrupted")]public void FailedOrInterruptedPendingAiRouteProjectionAllowsRetry(string mode)
        {
            int id=Spawn(-14,-14);Assert.AreEqual(PlayableCommandStatus.Applied,SendAiAttackMove(id,new NavPoint(-12,-14)));
            if(mode=="failed")
            {
                // The owned arrival allocator may retain the exact request before
                // publishing it to a route mailbox. Exercise the same domain
                // failure callback used by NavigationSession with that live binding.
                var request=nav.PendingRequestFor(id);Assert.NotNull(request);
                D.GetMethod("RecordTacticalRouteFailure",F).Invoke(domain,new object[]{request,NavigationOutcome.Unreachable});
            }
            else Assert.True(nav.Move(id,new NavPoint(-10,-10)));
            Assert.NotNull(Queue(id).Pending,mode+" route keeps the original issuance for controlled retry");
            Assert.False(Queue(id).PendingAiRouteViable,mode+" route must not suppress a new owner proposal");
            if(mode=="interrupted"){Restore();Assert.False(Queue(id).PendingAiRouteViable,"interrupted binding remains non-viable after restore");}
        }
        [Test]public void PendingAiMoveDeduplicatesOnlyExactLiveMissionRouteAndReleasesOnTerminalOrHumanChange()
        {
            int id=Spawn(-14,-14,PlayableOwner.Player,PlayableEntityKind.Shkval);var goal=new NavPoint(-10,-14);
            Assert.AreEqual(PlayableCommandStatus.Applied,SendAiMove(id,goal));var queue=Queue(id);Assert.NotNull(queue.Pending);Assert.True(queue.PendingAiRouteViable);
            var request=nav.PendingRequestFor(id);Assert.NotNull(request);
            bool SameOrder(PlayableAiAction action,PlayableUnitQueueSnapshot q,string policy=AiArmyPlanner.Policy)
            {
                var actor=new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Shkval,new NavPoint(-14,-14),100,false,0,0,0,queue:q);
                var method=typeof(PlayableAiOwnerLoop).GetMethod("SameOrder",BindingFlags.Static|BindingFlags.NonPublic);Assert.NotNull(method,"SameOrder helper");
                return (bool)method.Invoke(null,new object[]{action,actor,policy,true,"player-1",1L});
            }
            var profile=PlayableProfile.Default;
            PlayableAiAction Action(NavPoint target,PlayableCommandKind kind=PlayableCommandKind.Move)=>new PlayableAiAction(900,"player-1",profile.ProfileId,profile.Revision,1,1,kind,new[]{id},target,seed:19092026,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
            var hasLiveMove=typeof(AiTacticalExecutor).GetMethod("HasLivePendingMove",BindingFlags.Static|BindingFlags.NonPublic);Assert.NotNull(hasLiveMove);
            var actor=new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Shkval,new NavPoint(-14,-14),100,false,0,0,0,queue:queue);
            Assert.True((bool)hasLiveMove.Invoke(null,new object[]{actor,goal}),"The tactical helper must skip its exact viable owner-bound pending Move.");
            Assert.False((bool)hasLiveMove.Invoke(null,new object[]{actor,new NavPoint(goal.X,goal.Z+1)}),"A changed artillery position remains eligible.");
            Assert.True(SameOrder(Action(goal),queue),"An exact owner-bound live Move route is already the selected mission order.");
            Assert.False(SameOrder(Action(new NavPoint(goal.X,goal.Z+1)),queue),"A changed target must remain eligible for a new command.");
            Assert.False(SameOrder(Action(goal),queue,"scout"),"The mission deduplication must not consume another policy's command.");
            Assert.False(SameOrder(Action(goal,PlayableCommandKind.AttackMove),queue),"A changed command kind must not be hidden by the pending Move.");
            Call("RecordTacticalRouteFailure",request,NavigationOutcome.Unreachable);queue=Queue(id);
            actor=new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Shkval,new NavPoint(-14,-14),100,false,0,0,0,queue:queue);
            Assert.False(queue.PendingAiRouteViable);Assert.False((bool)hasLiveMove.Invoke(null,new object[]{actor,goal}),"A terminal route remains retryable.");Assert.False(SameOrder(Action(goal),queue),"A terminal route must leave the normal retry path available.");
            Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{id},goal),"A human replacement still preempts the AI route.");queue=Queue(id);
            actor=new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Shkval,new NavPoint(-14,-14),100,false,0,0,0,queue:queue);
            Assert.False((bool)hasLiveMove.Invoke(null,new object[]{actor,goal}),"A human-owned route is never treated as the native AI Move.");
            Assert.False(queue.PendingAiRouteViable);Assert.False(SameOrder(Action(goal),queue),"A human-owned pending order is never mistaken for the AI mission route.");
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
                20+4+state.Groups.Sum(g=>37+12*g.Members.Length)+QueueLegacyLayout.InstalledLayoutTailBytes(state); // v6 corridor, v7 march, v8 allocation layout
            world.Navigation=world.Navigation.Take(world.Navigation.Length-tail).ToArray();domain=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{world.Encode(),PlayableProfile.Default,7,"queue-test"});nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);Assert.AreEqual(request.Request,nav.Crowd.Units.Single(u=>u.Id==id).FailedRequest);Assert.NotNull(Queue(id).Pending);Restore();Assert.NotNull(Queue(id).Pending);
        }
        [Test]public void SpatialAndQueueSlotReuseSurvivesNonSortedAndEmptyMapRestore()
        {
            Call("BindMatchSeed",7);int a=Spawn(-14,-14),b=Spawn(-18,-14),c=Spawn(-22,-14);
            Send(new[]{b},new NavPoint(-18,-14));Deliver();Step();Send(new[]{a},new NavPoint(-14,-14));Deliver();Step();Send(new[]{c},new NavPoint(-20,-14));Deliver();Step();
            Send(new[]{b},default,PlayableCommandKind.Stop);Send(new[]{a},default,PlayableCommandKind.Stop);
            void CompareAndReuse(int id){var original=domain;Restore();var expected=PlayableWorldRestoreTests.Facts(original,true);var actual=PlayableWorldRestoreTests.Facts(domain,true);string diagnostic="slot restore";if(expected!=actual)diagnostic+=" "+typeof(OfflineParticipantRestoreTests).GetMethod("AuthorityDiff",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{original,domain,"domain",0});Assert.AreEqual(expected,actual,diagnostic);CollectionAssert.AreEqual(PlayableWorldRestoreTests.FactBytes(original,true,false),PlayableWorldRestoreTests.FactBytes(original,true,true),"uncached original authority");CollectionAssert.AreEqual(PlayableWorldRestoreTests.FactBytes(domain,true,false),PlayableWorldRestoreTests.FactBytes(domain,true,true),"uncached restored authority");var command=new PlayableCommand(1,++sequence,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-12,-14),origin:PlayableOrderOrigin.Human);Assert.AreEqual(Call("Apply",command,null),D.GetMethod("Apply",F).Invoke(original,new object[]{command,null}));Assert.AreEqual(PlayableWorldRestoreTests.Facts(original,true),PlayableWorldRestoreTests.Facts(domain,true),"identical live order and LIFO reuse after restore");}
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
            UseActualRoutes();
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-12,-14));Deliver();Step(2);var old=Queue(id).Active;
            Send(new[]{id},new NavPoint(-14,-10));Assert.AreEqual(old.CommandSequence,Queue(id).Active.CommandSequence);Assert.NotNull(Queue(id).Pending);Restore();Assert.AreEqual(old.CommandSequence,Queue(id).Active.CommandSequence);
            Step(100);Assert.True(nav.IsPending(id));Assert.Null(Queue(id).Active);Assert.Null(Queue(id).Completion);Restore();Assert.NotNull(Queue(id).Pending);
            Advance();Assert.AreEqual(2,Queue(id).Completion.Order.Sequence);Assert.Null(Queue(id).Pending);
        }
        [Test]public void IndependentRecipientsShareIssuanceAndRootWithDifferentActivationRevision()
        {
            UseActualRoutes();
            int a=Spawn(-14,-14),b=Spawn(-22,-14);Send(new[]{a},new NavPoint(-12,-14));Send(new[]{b},new NavPoint(-18,-14));Deliver();
            Send(new[]{a,b},new NavPoint(-10,-12),append:true);long issuance=Queue(a).Deferred[0].IssuanceId;Assert.AreEqual(issuance,Queue(b).Deferred[0].IssuanceId);
            long ga=0,gb=0,ra=0,rb=0;
            for(int k=0;k<350;k++){Deliver();Step();var qa=Queue(a);var qb=Queue(b);if(qa.Active?.IssuanceId==issuance){ga=qa.Active.GroupId;ra=qa.Active.ActivationRevision;}if(qb.Active?.IssuanceId==issuance){gb=qb.Active.GroupId;rb=qb.Active.ActivationRevision;}}
            Assert.Greater(ga,0);Assert.AreEqual(ga,gb);Assert.AreNotEqual(ra,rb);
        }
        [Test]public void MixedChainAndDeadTargetSkipActivateFollowingMoveInSameTick()
        {
            UseActualRoutes();
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
        [TestCase("sparse")][TestCase("reordered")][TestCase("empty")]public void SparseArrivalLedgerKeepsLayoutThroughColdRestoreAndNextCommandCopy(string shape)
        {
            UseActualRoutes();int actors=shape=="empty"?1:7;var ids=Enumerable.Range(0,actors).Select(i=>Spawn(-18+i*2,-26)).ToArray();
            foreach(int id in ids){var actor=nav.Crowd.Units.Single(u=>u.Id==id);Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{id},new NavPoint(actor.Position.X,-23)));}Deliver();Assert.AreEqual(actors,nav.ArrivalReservations.Count);
            if(shape=="reordered"){Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{ids[0]},default,PlayableCommandKind.Stop));Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{ids[0]},new NavPoint(-18,-23)));Deliver();Assert.AreEqual(7,nav.ArrivalReservations.Count);}
            else if(shape=="empty"){Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{ids[0]},default,PlayableCommandKind.Stop));Assert.Zero(nav.ArrivalReservations.Count);}
            else{foreach(int id in ids.Take(6))Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{id},default,PlayableCommandKind.Stop));Assert.AreEqual(1,nav.ArrivalReservations.Count);}
            string CopyInfo()
            {
                var map=(System.Collections.Generic.IDictionary<long,NavigationArrivalReservation>)typeof(NavigationSession).GetField("arrivalLedger",F).GetValue(nav);
                var budget=nav.PhysicalHostBudget;long operation=(long)typeof(NavigationSession).GetField("authorityOperationIdentity",F).GetValue(nav);
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,nav.Generation,nav.Admission.PublicationIdentity,out var scope));
                using(scope){
                    var method=typeof(NavigationSession).GetMethod("CopyCommandMap",F).MakeGenericMethod(typeof(long),typeof(NavigationArrivalReservation));var copy=(NavigationCommandSlots<long,NavigationArrivalReservation>)method.Invoke(nav,new object[]{map,0,scope,operation,null,-1});
                    try{
                        string Slots()=>string.Join(",",Enumerable.Range(0,copy.Capacity).Select(i=>copy.TryReadSlot(i,out var key,out _)?i+":"+key:i+":hole"));
                        var prior=copy.Count+"/"+copy.Capacity+" layout="+string.Join(",",copy.CaptureLayout())+" slots="+Slots();if(copy.Count==copy.Capacity)return prior+" full";if(copy.Count==0)return prior+" empty";var row=map.First();copy.AddPreparing(long.MaxValue,row.Value,(map as NavigationCommandSlots<long,NavigationArrivalReservation>)?.OwnerFor(row.Key));return prior+" inserted="+Slots();
                    }finally{copy.ReleaseStorage();}
                }
            }
            var before=CopyInfo();var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");Restore();CollectionAssert.AreEqual(bytes,(byte[])Call("CaptureWorldBytes",7,"queue-test"));var after=CopyInfo();TestContext.Out.WriteLine("hot "+before+" cold "+after);Assert.AreEqual(before,after,"same CopyCommandMap added0 and one free-slot insertion");
            var prior=(byte[])Call("CaptureWorldBytes",7,"queue-test");var budget=new NavigationRetentionBudget();NavigationHostRoot host=null;NavigationAuthorityOwners authority=null;
            try{
                NavigationSession.DecodeDetachedHostAuthority(nav.CaptureState().HostAuthority,budget,out host,out authority);Assert.True(authority.HasLedgerLayout);var order=authority.LedgerRowOrder.ReadOnlyBacking;var mutation=order.Length==0?authority.LedgerLayout.ReadOnlyBacking:order;int mutationIndex=order.Length==0?1:0,original=mutation[mutationIndex],invalid=order.Length==0?authority.LedgerLayout[0]:order.Length;
                byte[] malformed;
                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Capture,nav.Generation,1,out var capture));
                using(capture){
                    NavigationHostEnvelope envelope=null;NavigationCaptureBytes output=null;
                    try{mutation[mutationIndex]=invalid;envelope=NavigationHostEnvelope.CreateOwned(host,authority,capture.OwnedReservation(),nav.Admission.PublicationIdentity);output=NavigationCaptureBytes.Write(capture.OwnedReservation(),writer=>NavPhysicalBodyWire.WriteTo(writer,envelope,capture.OwnedReservation()));malformed=(byte[])output.Bytes.Clone();}
                    finally{mutation[mutationIndex]=original;output?.Dispose();envelope?.ReleaseStorage();}
                }
                var rejectedBudget=new NavigationRetentionBudget();Assert.Throws<ArgumentException>(()=>NavigationSession.DecodeDetachedHostAuthority(malformed,rejectedBudget,out _,out _));Assert.Zero(rejectedBudget.PhysicalBytes);Assert.Zero(rejectedBudget.HeldBytes);Assert.Zero(rejectedBudget.ActiveScopes);
                var stagingBudget=new NavigationRetentionBudget();int stagingDenied=0;long stagingRequest=64L+2048L+128L*(authority.LedgerRowOrder.Count+(long)authority.LedgerLayout[0]);
                stagingBudget.AllocationAdmission=amount=>{if(amount==stagingRequest&&stagingBudget.ActiveScopes==4){stagingDenied++;return false;}return true;};
                object unexpected=null;
                try{var error=Assert.Throws<TargetInvocationException>(()=>unexpected=D.GetMethod("RestoreWorldBytesAdmitted",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{prior,PlayableProfile.Default,7,"queue-test",stagingBudget}));Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);Assert.AreEqual(1,stagingDenied);Assert.Zero(stagingBudget.PhysicalBytes);Assert.Zero(stagingBudget.HeldBytes);Assert.Zero(stagingBudget.ActiveScopes);}
                finally{stagingBudget.AllocationAdmission=null;if(unexpected!=null)D.GetMethod("DestroyOwnedNavigation",F).Invoke(unexpected,null);}

                Assert.True(budget.TryOpenScope(NavigationPreparationKind.Reconstruction,nav.Generation,1,out var scope));
                using(scope){
                    long physical=budget.PhysicalBytes,held=budget.HeldBytes;
                    try{mutation[mutationIndex]=invalid;Assert.Throws<ArgumentException>(()=>authority.ValidateLedgerLayout(scope.OwnedReservation()));}
                    finally{mutation[mutationIndex]=original;}
                    Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,budget.HeldBytes);int denied=0;long request=128L+authority.LedgerLayout[0]+order.Length;
                    budget.AllocationAdmission=bytes=>{if(bytes==request){denied++;return false;}return true;};
                    try{Assert.Throws<InvalidOperationException>(()=>authority.ValidateLedgerLayout(scope.OwnedReservation()));Assert.AreEqual(1,denied);Assert.AreEqual(physical,budget.PhysicalBytes);Assert.AreEqual(held,budget.HeldBytes);}
                    finally{budget.AllocationAdmission=null;}
                }
            }finally{host?.Release();authority?.Release();}Assert.Zero(budget.PhysicalBytes);Assert.Zero(budget.HeldBytes);Assert.Zero(budget.ActiveScopes);CollectionAssert.AreEqual(prior,(byte[])Call("CaptureWorldBytes",7,"queue-test"),"malformed/refused detached metadata leaves live world unchanged");
        }
        [TestCase(false)][TestCase(true)]public void CombatStopRetiresCancelledChaseAttemptAndRestoresExactly(bool installed)
        {
            UseActualRoutes();int id=Spawn(-14,-26),enemy=Spawn(-2,-26,PlayableOwner.Enemy);
            Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Step();Assert.True(nav.IsPending(id));
            if(installed){Deliver();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.True((bool)Call("CombatMove",id,new NavPoint(-14,-24)));Assert.True(nav.IsPending(id));}
            var active=InternalExecution(id,"Active");var type=active.GetType();long cancelled=(long)type.GetField("AttemptRequest",F).GetValue(active),route=(long)type.GetField("RouteRequest",F).GetValue(active);
            Assert.Greater(cancelled,route);Call("CombatStop",id);Assert.False(nav.IsPending(id));Assert.False(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.AreEqual(route,type.GetField("AttemptRequest",F).GetValue(active));Assert.Null(type.GetField("TerminalFailure",F).GetValue(active));
            var before=(byte[])Call("CaptureWorldBytes",7,"queue-test");Restore();CollectionAssert.AreEqual(before,(byte[])Call("CaptureWorldBytes",7,"queue-test"));Assert.AreEqual(PlayableCommandKind.Attack,Queue(id).Active.Kind);
            Assert.True((bool)Call("CombatMove",id,new NavPoint(-14,-24)));Deliver();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Restore();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);
        }
        [Test]public void ActualAttackMetadataFrontierInterleaveCommitsOrderAndQueueExactlyOnce()
        {
            int id=Spawn(-14,-26),enemy=Spawn(-8,-26,PlayableOwner.Enemy),neighbor=Spawn(-18,-26);var kernel=new CertifiedRouteKernel();NavigationHostRoot newest=null;int rendezvous=0,boundaries=0;
            var geometry=(NavGeometry)D.GetProperty("Geometry",F).GetValue(domain);PlayableRouteBinding Binding()=>new PlayableRouteBinding(geometry,PlayableProfile.Default,nav.Admission,nav.RoutePort);
            var units=(System.Collections.IDictionary)D.GetField("units",F).GetValue(domain);var unit=units[id];object Field(string name)=>unit.GetType().GetField(name).GetValue(unit);
            try{
                Assert.True(nav.Move(neighbor,new NavPoint(-16,-26)));nav.SetFormationContact(neighbor,false,1);
                kernel.PauseAtPhysicalBoundary=()=>true;kernel.Pump(Binding(),4);long revision=(long)D.GetField("nextOrderRevision",F).GetValue(domain);
                nav.AfterMetadataObservationForQa=()=>{
                    rendezvous++;Assert.AreEqual(enemy,(int)Field("Target"));Assert.Null(Field("LastOrder"),"ApplyCore already applied, order stamping must still be pending");
                    kernel.PauseAtPhysicalBoundary=()=>++boundaries>1;kernel.Pump(Binding(),4);newest=nav.PinCommittedHostRoot();Assert.AreEqual(CertifiedRouteKernel.PumpStage.ArrivalAdvance,newest.Frontier.Stage);
                };
                Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy));Assert.AreEqual(1,rendezvous);Assert.AreEqual(2,boundaries);
                var stamp=(PlayableOrderStamp)Field("LastOrder");Assert.AreEqual(revision,stamp.Revision);Assert.AreEqual(1,stamp.Sequence);Assert.AreEqual(PlayableCommandKind.Attack,stamp.Kind);Assert.AreEqual(revision+1,(long)D.GetField("nextOrderRevision",F).GetValue(domain));
                var queue=Queue(id);Assert.NotNull(queue.Active);Assert.Null(queue.Pending);Assert.Zero(queue.Deferred.Count);Assert.AreEqual(1,queue.Active.CommandSequence);Assert.AreEqual(revision,queue.Active.ActivationRevision);Assert.AreEqual(enemy,queue.Active.VisibleTargetId);Assert.AreEqual(nav.GroupFor(id).GroupId,queue.Active.GroupId);Assert.AreEqual(2,nav.GroupOrders.Count);
                var current=nav.PinCommittedHostRoot();try{Assert.AreSame(newest,current);}finally{current.Release();}
                Assert.AreEqual(1,nav.GroupFor(neighbor).Members.Single().ContactTick);nav.AfterMetadataObservationForQa=null;kernel.PauseAtPhysicalBoundary=null;kernel.Pump(Binding(),4);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);
            }finally{newest?.Release();nav.AfterMetadataObservationForQa=null;kernel.PauseAtPhysicalBoundary=null;kernel.Clear();nav.ReleaseCommittedHostRoot();Call("DestroyOwnedNavigation");Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);}
        }
        [Test]public void VisibleOutOfRangeAttackChasesAndTargetDeathActivatesTailAfterRestore()
        {
            int id=Spawn(-14,-26),enemy=Spawn(-2,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Send(new[]{id},new NavPoint(-12,-26),append:true);Step();Assert.True(nav.IsPending(id));Restore();Deliver();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Call("Damage",enemy,10000);Step();Assert.AreEqual(2,Queue(id).Pending.CommandSequence);
        }
        [Test]public void FailedReplacementKeepsAttackChaseOnOriginalIdentityAcrossRestore()
        {
            UseArrivalFailureFixture();
            int id=Spawn(-14,-26),enemy=Spawn(-6,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Send(new[]{id},new NavPoint(-14,-10));MaterializeReplacementRequest(id);
            while(nav.Requests.TryDequeue(out var rejected))nav.Answers.TryEnqueue(new NavigationAnswer(rejected,Array.Empty<NavPoint>()));nav.ApplyResults();
            Assert.True((bool)D.GetMethod("TerminalProofMatches",F).Invoke(domain,new object[]{InternalExecution(id,"Pending"),nav.Crowd.Units.Single(u=>u.Id==id)}));var failed=Queue(id);Assert.NotNull(failed.Pending);Assert.AreEqual(2,failed.Pending.CommandSequence);Assert.AreEqual(1,failed.Active.CommandSequence);Assert.Null(failed.Completion);Restore();failed=Queue(id);Assert.AreEqual(2,failed.Pending.CommandSequence);Assert.AreEqual(1,failed.Active.CommandSequence);Send(new[]{id},new NavPoint(-12,-10),append:true);
            Relocate(enemy,new NavPoint(-3,-26));Step();Assert.True(nav.Requests.TryDequeue(out var chase));Assert.AreEqual(failed.Active.GroupId,chase.MemberIdentity.GroupId);Assert.AreEqual(failed.Active.ActivationRevision,chase.MemberIdentity.OrderRevision);Assert.False((bool)InternalExecution(id,"Active").GetType().GetField("Interrupted",F).GetValue(InternalExecution(id,"Active")));Assert.True(nav.Requests.TryEnqueue(chase));
            var qs=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);var pendingExecution=qs[id].GetType().GetField("Pending",F).GetValue(qs[id]);Assert.True((bool)D.GetMethod("TerminalProofMatches",F).Invoke(domain,new object[]{pendingExecution,nav.Crowd.Units.Single(u=>u.Id==id)}));
            Restore();Assert.AreEqual(2,Queue(id).Pending.CommandSequence);Assert.AreEqual(1,Queue(id).Active.CommandSequence);Assert.AreEqual(1,Queue(id).Deferred.Count);Assert.Null(Queue(id).Completion);Assert.True(nav.IsPending(id));Assert.True(nav.Requests.TryDequeue(out var restoredChase));Assert.AreEqual(chase.Request,restoredChase.Request);
            nav.Answers.TryEnqueue(new NavigationAnswer(restoredChase,new SharedFlowRouter(restoredChase.Geometry,restoredChase.Profile).FindPath(restoredChase.Start,restoredChase.Goal)));nav.ApplyResults();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Restore();Assert.True(nav.Crowd.Units.Single(u=>u.Id==id).Moving);Assert.True((bool)Call("TargetAlive",enemy));
            Call("Damage",enemy,10000);Step();Assert.Null(Queue(id).Active);Assert.AreEqual(2,Queue(id).Pending.CommandSequence);Assert.AreEqual(1,Queue(id).Deferred.Count);Assert.Null(Queue(id).Completion);Restore();Assert.Null(Queue(id).Active);Assert.AreEqual(2,Queue(id).Pending.CommandSequence);Assert.AreEqual(1,Queue(id).Deferred.Count);Assert.Null(Queue(id).Completion);Send(new[]{id},default,PlayableCommandKind.Stop);Assert.Null(Queue(id));
        }
        [TestCase(false)][TestCase(true)]public void FailedReplacementRejectsForgedTerminalOrChaseRequest(bool corruptChase)
        {
            UseArrivalFailureFixture();
            int id=Spawn(-14,-26),enemy=Spawn(-6,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Send(new[]{id},new NavPoint(-14,-10));MaterializeReplacementRequest(id);while(nav.Requests.TryDequeue(out var rejected))nav.Answers.TryEnqueue(new NavigationAnswer(rejected,Array.Empty<NavPoint>()));nav.ApplyResults();
            Relocate(enemy,new NavPoint(-3,-26));Step();Assert.True(nav.Requests.TryDequeue(out var chase));Assert.True(nav.Requests.TryEnqueue(chase));
            var queues=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);var q=queues[id];var execution=q.GetType().GetField(corruptChase?"Active":"Pending",F).GetValue(q);
            if(corruptChase)execution.GetType().GetField("AttemptRequest",F).SetValue(execution,chase.Request+1);else{var proof=execution.GetType().GetField("TerminalFailure",F).GetValue(execution);proof.GetType().GetField("Request",F).SetValue(proof,chase.Request+1);}
            var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");Assert.Throws<TargetInvocationException>(()=>D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,PlayableProfile.Default,7,"queue-test"}));
        }
        [Test,Timeout(60000)]public void FailedAttackChaseCooldownSurvivesRestoreAndRetriesWithoutTimerOverride()
        {
            // Synthetic component fault injection, not an ordinary threaded route-failure witness.
            const double dt=1d/30;const int maximumSteps=128;
            var profile=PlayableProfile.Default;var origin=new NavPoint(-14,-26);var target=new NavPoint(-3,-26);
            string output=System.IO.Path.GetFullPath(Environment.GetEnvironmentVariable("SPACEWARS_C23_COOLDOWN_OUTPUT")??
                System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory,".local/qa/c23-failed-chase-cooldown"));
            if(System.IO.Directory.Exists(output)&&System.IO.Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("Cooldown evidence output must be empty.");
            System.IO.Directory.CreateDirectory(output);
            void Save(string name,object value)=>System.IO.File.WriteAllText(System.IO.Path.Combine(output,name),Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));
            object Read(object value,string field){Assert.NotNull(value);var info=value.GetType().GetField(field,F|BindingFlags.Public);Assert.NotNull(info,value.GetType().Name+"."+field);return info.GetValue(value);}
            object Point(NavPoint p)=>new {p.X,p.Z};
            object[] Facts(object[] values)=>values.Select(v=>v is NavLocation location?(object)new {position=Point(location.Position),surface=location.SurfaceId}:v).ToArray();
            long Bits(double value)=>BitConverter.DoubleToInt64Bits(value);
            int id=-1,enemy=-1,steps=0;string phase="preflight";Exception primary=null;var observations=new List<object>();
            double Timer(){var units=(System.Collections.IDictionary)D.GetField("units",F).GetValue(domain);return (double)Read(units[id],"Repath");}
            long Requests()=>nav.CaptureState().RequestSequence;
            object[] RootIdentity()
            {
                var queue=Queue(id);Assert.NotNull(queue.Active);Assert.Null(queue.Pending);Assert.Null(queue.Completion);Assert.Zero(queue.Deferred.Count);
                Assert.AreEqual(PlayableCommandKind.Attack,queue.Active.Kind);Assert.AreEqual(1,queue.Active.CommandSequence);
                var active=InternalExecution(id,"Active");var issuance=Read(active,"Issuance");var command=(PlayableCommand)Read(issuance,"Command");
                Assert.AreEqual(enemy,command.TargetId);Assert.False((bool)Read(active,"Interrupted"));
                var group=nav.GroupFor(id);var member=group.Members.Single(m=>m.Entity==id);var body=nav.Crowd.Units.Single(u=>u.Id==id);
                Assert.AreEqual(queue.Active.GroupId,group.GroupId);Assert.AreEqual(queue.Active.ActivationRevision,member.OrderRevision);Assert.AreEqual(body.Incarnation,member.Incarnation);
                return new object[]{queue.Active.IssuanceId,queue.Active.GroupId,queue.Active.ActivationRevision,Read(issuance,"Revision"),Read(issuance,"Tick"),
                    Read(active,"Incarnation"),Read(active,"ActivationTick"),Read(active,"Assigned"),group.WorldGeneration,group.CommandSequence,group.IssuedTick,
                    group.OriginalLocation,group.MembershipRevision,member.Entity,member.Incarnation,member.MobilityRevision,member.OrderRevision,body.Incarnation,body.MobilityRevision};
            }
            object[] FailureProof()
            {
                var proof=Read(InternalExecution(id,"Active"),"TerminalFailure");Assert.NotNull(proof);
                return new[]{Read(proof,"Request"),Read(proof,"Order"),Read(proof,"GroupId"),Read(proof,"Revision"),Read(proof,"Incarnation"),Read(proof,"Tick"),Read(proof,"Location"),Read(proof,"Outcome")};
            }
            object[] AttemptProof(){var active=InternalExecution(id,"Active");return new[]{Read(active,"AttemptRequest"),Read(active,"AttemptOrder"),Read(active,"AttemptLocation"),Read(active,"RouteRequest"),Read(active,"RouteOrder")};}
            void NormalStep()
            {
                Assert.Less(steps,maximumSteps,"Frozen logical-step horizon.");Step();steps++;
                observations.Add(new {steps,timer=Timer(),timerBits=Bits(Timer()),requests=Requests(),nav.PendingCount,mailbox=nav.Requests.Count});
            }
            byte[] Checkpoint(string name)
            {
                var bytes=(byte[])Call("CaptureWorldBytes",7,"queue-test");System.IO.File.WriteAllBytes(System.IO.Path.Combine(output,name+"-current-v10.bin"),bytes);
                using(var state=PlayableWorldState.Decode(bytes)){
                    Assert.AreEqual(PlayableWorldState.CurrentVersion,state.Version);Assert.AreEqual(11,state.Version);Assert.AreEqual(7,state.Seed);Assert.AreEqual(1,state.Generation);Assert.AreEqual("queue-test",state.SourceIdentity);
                    CollectionAssert.AreEqual(WorldWire.Binding(profile),state.Binding);Assert.Zero(state.Participants.Length,"This is the established standalone component map, without an offline roster.");
                }
                return bytes;
            }
            void ColdRestore(string name)
            {
                var bytes=Checkpoint(name);long timerBits=Bits(Timer()),requests=Requests();var root=RootIdentity();var attempt=AttemptProof();
                var terminal=Read(InternalExecution(id,"Active"),"TerminalFailure");var failure=terminal==null?null:FailureProof();
                var restored=D.GetMethod("RestoreWorldBytes",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{bytes,profile,7,"queue-test"});
                Call("DestroyOwnedNavigation");Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);
                domain=restored;nav=(NavigationSession)D.GetProperty("Navigation",F).GetValue(domain);
                Assert.AreEqual(timerBits,Bits(Timer()),"Literal serialized floating cooldown must survive cold restore.");Assert.AreEqual(requests,Requests());
                CollectionAssert.AreEqual(root,RootIdentity());CollectionAssert.AreEqual(attempt,AttemptProof());
                if(failure==null)Assert.Null(Read(InternalExecution(id,"Active"),"TerminalFailure"));else CollectionAssert.AreEqual(failure,FailureProof());
                var roundTrip=Checkpoint(name+"-restored");CollectionAssert.AreEqual(bytes,roundTrip,"Complete current component envelope must round trip without exclusions.");
            }
            try
            {
                Assert.AreEqual(.5d,profile.AttackRepathSeconds);Assert.Greater(11d,profile.TankRange);Assert.LessOrEqual(11d,profile.TankVisionRange);
                Assert.True(((NavGeometry)D.GetProperty("Geometry",F).GetValue(domain)).IsFree(origin,PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank)));
                Assert.True(((NavGeometry)D.GetProperty("Geometry",F).GetValue(domain)).IsFree(target,PlayableUnitRules.Radius(profile,PlayableEntityKind.Tank)));
                Save("preflight.json",new {componentOnly=true,syntheticFault="typed NavigationAnswer(empty route)",dt,maximumSteps,profile.AttackRepathSeconds,profile.ProfileId,profile.Revision,profile.SourceCommit,profile.SourceManifestSha256,
                    seed=7,generation=1,source="queue-test",map="standalone default profile geometry; no offline configuration",origin=Point(origin),target=Point(target)});
                id=Spawn(origin.X,origin.Z);enemy=Spawn(target.X,target.Z,PlayableOwner.Enemy);
                Assert.AreEqual(PlayableCommandStatus.Applied,Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy));
                phase="initial-natural-chase-request";Save("target-branch-entered.json",new {id,enemy});NormalStep();
                long failedSequence=Requests();
                Assert.True(nav.Requests.TryDequeue(out var failed));Assert.AreEqual(id,failed.Entity);Assert.AreEqual(profile.AttackRepathSeconds,Timer());
                var root=RootIdentity();Assert.AreEqual(Queue(id).Active.GroupId,failed.MemberIdentity.GroupId);Assert.AreEqual(Queue(id).Active.ActivationRevision,failed.MemberIdentity.OrderRevision);
                Assert.True(nav.Answers.TryEnqueue(new NavigationAnswer(failed,Array.Empty<NavPoint>())));nav.ApplyResults();
                Assert.False(nav.IsPending(id));Assert.Zero(nav.PendingCount);Assert.Zero(nav.Requests.Count);
                var terminal=FailureProof();Assert.AreEqual(failed.Request,terminal[0]);Assert.AreEqual(failed.Order,terminal[1]);Assert.AreEqual(NavigationOutcome.Unreachable,terminal[7]);
                CollectionAssert.AreEqual(root,RootIdentity());Assert.AreEqual(profile.AttackRepathSeconds,Timer());
                phase="positive-cooldown-and-restore";double expected=profile.AttackRepathSeconds;
                for(int k=0;k<3;k++){
                    expected=Math.Max(0,expected-dt);NormalStep();Assert.AreEqual(Bits(expected),Bits(Timer()));Assert.Greater(Timer(),0);
                    Assert.AreEqual(failedSequence,Requests());Assert.Zero(nav.PendingCount);Assert.Zero(nav.Requests.Count);CollectionAssert.AreEqual(terminal,FailureProof());
                }
                double capturedTimer=Timer();Save("positive-failure-facts.json",new {steps,timer=capturedTimer,timerBits=Bits(capturedTimer),requests=Requests(),root=Facts(root),failure=Facts(terminal),attempt=Facts(AttemptProof())});
                ColdRestore("positive-failure");CollectionAssert.AreEqual(root,RootIdentity());CollectionAssert.AreEqual(terminal,FailureProof());
                phase="first-naturally-eligible-retry";int firstEligibleStep=-1;
                while(expected>0){
                    expected=Math.Max(0,expected-dt);NormalStep();CollectionAssert.AreEqual(root,RootIdentity());
                    if(expected>0){Assert.AreEqual(Bits(expected),Bits(Timer()));Assert.AreEqual(failedSequence,Requests());Assert.Zero(nav.PendingCount);Assert.Zero(nav.Requests.Count);CollectionAssert.AreEqual(terminal,FailureProof());}
                    else{
                        firstEligibleStep=steps;Assert.AreEqual(Bits(profile.AttackRepathSeconds),Bits(Timer()),"The first eligible normal Step emits the retry and starts only the authored cooldown.");
                        Assert.AreEqual(failedSequence+1,Requests());Assert.AreEqual(1,nav.PendingCount);Assert.AreEqual(1,nav.Requests.Count);Assert.Null(Read(InternalExecution(id,"Active"),"TerminalFailure"));
                    }
                }
                Assert.Greater(firstEligibleStep,0);var retry=nav.PendingRequestFor(id);Assert.NotNull(retry);Assert.Greater(retry.Request,failed.Request);
                Assert.AreEqual(failedSequence+1,retry.Request);Assert.AreNotEqual(failed.Order,retry.Order);
                Assert.AreEqual(failed.MemberIdentity.GroupId,retry.MemberIdentity.GroupId);Assert.AreEqual(failed.MemberIdentity.OrderRevision,retry.MemberIdentity.OrderRevision);
                Assert.AreEqual(failed.MemberIdentity.Incarnation,retry.MemberIdentity.Incarnation);Assert.AreEqual(failed.MemberIdentity.MobilityRevision,retry.MemberIdentity.MobilityRevision);Assert.AreEqual(failed.MemberIdentity.MembershipRevision,retry.MemberIdentity.MembershipRevision);
                var retryAttempt=AttemptProof();Assert.AreEqual(retry.Request,retryAttempt[0]);Assert.AreEqual(retry.Order,retryAttempt[1]);Assert.AreEqual(retry.GoalLocation,retryAttempt[2]);
                phase="pending-duplicate-suppression";expected=profile.AttackRepathSeconds;int pendingSteps=0;
                do{
                    expected=Math.Max(0,expected-dt);NormalStep();pendingSteps++;Assert.AreEqual(Bits(expected),Bits(Timer()));
                    Assert.AreSame(retry,nav.PendingRequestFor(id));Assert.AreEqual(failedSequence+1,Requests());Assert.AreEqual(1,nav.PendingCount);Assert.AreEqual(1,nav.Requests.Count);
                    CollectionAssert.AreEqual(root,RootIdentity());CollectionAssert.AreEqual(retryAttempt,AttemptProof());Assert.Null(Read(InternalExecution(id,"Active"),"TerminalFailure"));
                }while(expected>0);
                NormalStep();pendingSteps++;Assert.Zero(Timer());Assert.AreSame(retry,nav.PendingRequestFor(id));Assert.AreEqual(failedSequence+1,Requests());Assert.AreEqual(1,nav.PendingCount);Assert.AreEqual(1,nav.Requests.Count);
                Save("pending-retry-facts.json",new {steps,firstEligibleStep,pendingSteps,timer=Timer(),requests=Requests(),root=Facts(root),attempt=Facts(retryAttempt),retry.Request,retry.Order});
                ColdRestore("pending-retry");CollectionAssert.AreEqual(root,RootIdentity());CollectionAssert.AreEqual(retryAttempt,AttemptProof());Assert.Null(Read(InternalExecution(id,"Active"),"TerminalFailure"));
                var restoredRetry=nav.PendingRequestFor(id);Assert.NotNull(restoredRetry);Assert.AreEqual(retry.Request,restoredRetry.Request);Assert.AreEqual(retry.Order,restoredRetry.Order);Assert.AreEqual(retry.GoalLocation,restoredRetry.GoalLocation);
                Assert.AreEqual(1,nav.PendingCount);Assert.AreEqual(1,nav.Requests.Count);
                Save("result.json",new {result="PASS",componentOnly=true,ordinaryQualification=false,steps,capturedTimer,firstEligibleStep,pendingSteps,failedRequest=failed.Request,retryRequest=retry.Request,root=Facts(root),attempt=Facts(retryAttempt),observations});
                TestContext.WriteLine($"C2.3 synthetic component cooldown: positive capture={capturedTimer:R}; first eligible step={firstEligibleStep}; pending steps={pendingSteps}; total={steps}; failed={failed.Request}; retry={retry.Request}; no timer override.");
            }
            catch(Exception error){primary=error;try{Save("failure.json",new {phase,steps,id,enemy,error=error.ToString(),observations});}catch(Exception preservation){TestContext.WriteLine(preservation);}throw;}
            finally{
                try{Call("DestroyOwnedNavigation");Assert.Zero(nav.PhysicalHostBudget.PhysicalBytes);Assert.Zero(nav.PhysicalHostBudget.HeldBytes);Assert.Zero(nav.PhysicalHostBudget.ActiveScopes);Save("cleanup.json",new {released=true,physical=nav.PhysicalHostBudget.PhysicalBytes,held=nav.PhysicalHostBudget.HeldBytes,scopes=nav.PhysicalHostBudget.ActiveScopes});}
                catch(Exception cleanup){if(primary==null)throw;TestContext.WriteLine("Cleanup failure after primary: "+cleanup);}
            }
        }

        [Test]public void FailedAttackChaseRestoresThenRetriesSuccessfullyWithoutStaleProof()
        {
            UseArrivalFailureFixture();
            int id=Spawn(-14,-26),enemy=Spawn(-6,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Send(new[]{id},new NavPoint(-14,-10));MaterializeReplacementRequest(id);while(nav.Requests.TryDequeue(out var rejected))nav.Answers.TryEnqueue(new NavigationAnswer(rejected,Array.Empty<NavPoint>()));nav.ApplyResults();
            int observedRequests=0;nav.RouteRequested+=request=>{if(request.Entity!=id)return;observedRequests++;Assert.AreEqual(Queue(id).Active.GroupId,request.MemberIdentity.GroupId);Assert.AreEqual(Queue(id).Active.ActivationRevision,request.MemberIdentity.OrderRevision);Assert.False((bool)InternalExecution(id,"Active").GetType().GetField("Interrupted",F).GetValue(InternalExecution(id,"Active")));Assert.True((bool)D.GetMethod("TerminalProofMatches",F).Invoke(domain,new object[]{InternalExecution(id,"Pending"),nav.Crowd.Units.Single(u=>u.Id==id)}));};
            Assert.True((bool)D.GetMethod("TerminalProofMatches",F).Invoke(domain,new object[]{InternalExecution(id,"Pending"),nav.Crowd.Units.Single(u=>u.Id==id)}));Relocate(enemy,new NavPoint(-3,-26));Step();Assert.True(nav.Requests.TryDequeue(out var failedChase));Assert.AreEqual(1,observedRequests,"one external notification after required Combat acceptance");Assert.AreEqual(Queue(id).Active.GroupId,failedChase.MemberIdentity.GroupId);Assert.AreEqual(Queue(id).Active.ActivationRevision,failedChase.MemberIdentity.OrderRevision);Assert.False((bool)InternalExecution(id,"Active").GetType().GetField("Interrupted",F).GetValue(InternalExecution(id,"Active")));nav.Answers.TryEnqueue(new NavigationAnswer(failedChase,Array.Empty<NavPoint>()));nav.ApplyResults();
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
            UseArrivalFailureFixture();
            int id=Spawn(-14,-26),enemy=Spawn(-3,-26,PlayableOwner.Enemy);Send(new[]{id},default,PlayableCommandKind.Attack,target:enemy);Step();Assert.True(nav.Requests.TryDequeue(out var originalChase));nav.Answers.TryEnqueue(new NavigationAnswer(originalChase,new SharedFlowRouter(originalChase.Geometry,originalChase.Profile).FindPath(originalChase.Start,originalChase.Goal)));nav.ApplyResults();
            var actor=nav.Crowd.Units.Single(u=>u.Id==id);Assert.True(actor.Moving);long installed=actor.InstalledRequest;Send(new[]{id},new NavPoint(-14,-10));MaterializeReplacementRequest(id);Assert.True(nav.Requests.TryDequeue(out var replacement));nav.Answers.TryEnqueue(new NavigationAnswer(replacement,Array.Empty<NavPoint>()));nav.ApplyResults();Assert.True(actor.Moving);Assert.AreEqual(installed,actor.InstalledRequest);Assert.AreEqual(PlayableCommandKind.Attack,Queue(id).Active.Kind);Assert.NotNull(Queue(id).Pending);
            Assert.True((bool)D.GetMethod("TerminalProofMatches",F).Invoke(domain,new object[]{InternalExecution(id,"Pending"),actor}));Assert.True((bool)Call("CombatMove",id,new NavPoint(-14,-24)));Assert.True(nav.Requests.TryDequeue(out var retry));Assert.AreEqual(Queue(id).Active.GroupId,retry.MemberIdentity.GroupId);Assert.AreEqual(Queue(id).Active.ActivationRevision,retry.MemberIdentity.OrderRevision);Assert.False((bool)InternalExecution(id,"Active").GetType().GetField("Interrupted",F).GetValue(InternalExecution(id,"Active")));nav.Answers.TryEnqueue(new NavigationAnswer(retry,Array.Empty<NavPoint>()));nav.ApplyResults();Assert.True(actor.Moving);Assert.AreEqual(installed,actor.InstalledRequest);Assert.AreEqual(PlayableCommandKind.Attack,Queue(id).Active.Kind);Assert.NotNull(InternalExecution(id,"Active").GetType().GetField("TerminalFailure",F).GetValue(InternalExecution(id,"Active")));
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
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)]public void PendingAttemptAndAssignedLocationCorruptionRejects(int mode)
        {
            int id=Spawn(-14,-14);Send(new[]{id},new NavPoint(-12,-14));Deliver();Send(new[]{id},new NavPoint(-14,-10));
            var queues=(System.Collections.IDictionary)D.GetField("tacticalQueues",F).GetValue(domain);var q=queues[id];var e=q.GetType().GetField("Pending",F).GetValue(q);
            if(mode==0)e.GetType().GetField("AttemptRequest",F).SetValue(e,0L);if(mode==1)e.GetType().GetField("Assigned",F).SetValue(e,(NavLocation?)new NavLocation(new NavPoint(-10,-10),NavLocation.FlatSurface));if(mode==2)e.GetType().GetField("Failed",F).SetValue(e,true);if(mode==3)e.GetType().GetField("Assigned",F).SetValue(e,null);if(mode==4){var original=(NavLocation?)e.GetType().GetField("Assigned",F).GetValue(e);e.GetType().GetField("Assigned",F).SetValue(e,(NavLocation?)new NavLocation(original.Value.Position,"wrong-layer"));}
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

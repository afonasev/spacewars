using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiInitiativeTests
    {
        [TestCase(AiDifficulty.Recruit)][TestCase(AiDifficulty.Fighter)][TestCase(AiDifficulty.Veteran)]
        public void Attention_ActualNewVisibleThreatRespectsLatencyPauseBudgetAndReplay(AiDifficulty difficulty)
        {
            var basis=Config(6);var scenario=Enumerable.Range(0,6).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-20+(i%3)*2,-8-(i/3)*3)))
                .Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(0,0),Math.PI)}).ToArray();
            var c=new OfflineMatchConfiguration(P,"a6-attention","a6-new-visible-threat","UnityHostRouteService",basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario,scenarioBuildings:basis.ScenarioBuildings.ToArray());
            var ai=AiProfile.Initial;var a=new PlayableAuthorityTick(c,71,ai,difficulty);var owner=Owner(a);
            var metric=new HeadlessEconomyMetrics(a,ai);long firstSeen=-1,firstApplied=-1;bool paused=false;
            int limit=(int)ai.DifficultyValue(difficulty,"actionsPerDecision");long reaction=AiProfile.SecondsToTicks(ai.DifficultyValue(difficulty,"reactionSeconds"),30);
            var seen=new System.Collections.Generic.Dictionary<long,PlayableAiTraceRecord>();
            Assert.IsEmpty(AiDefensePlanner.Threats(PlayableAiObservation.From(a.ParticipantView("west-owner")),P));
            var enemy=a.ParticipantView("east-owner").Entities.Single(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank);
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,500,"east-owner",PlayableCommandKind.Move,new[]{enemy.Id},new NavPoint(-6,0))).Status);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<360;i++)
                {
                    Step(a,host);metric.Observe(a);var checkpoint=a.CaptureDiagnosticCheckpoints().Single();
                    foreach(var record in checkpoint.Records)if(record.Status==PlayableAiDeliveryStatus.Scheduled)seen[record.ActionId]=record;
                    var threats=AiDefensePlanner.Threats(PlayableAiObservation.From(a.ParticipantView("west-owner")),P);
                    if(firstSeen<0&&threats.Length>0)firstSeen=a.Tick;
                    var pending=((IEnumerable)Get(owner,"pendingItems")).Cast<object>().ToArray();Assert.LessOrEqual(pending.Length,limit);
                    foreach(var group in checkpoint.Records.Where(r=>r.Status==PlayableAiDeliveryStatus.Scheduled).GroupBy(r=>r.ReceiptIdentity.DecisionOrdinal))Assert.LessOrEqual(group.Count(),limit);
                    foreach(var record in checkpoint.Records.Where(r=>r.Status==PlayableAiDeliveryStatus.Applied&&r.Policy==AiDefensePlanner.Policy))
                    {Assert.GreaterOrEqual(record.ApplicationTick,firstSeen+reaction);Assert.GreaterOrEqual(record.ApplicationTick,record.DueTick);if(firstApplied<0)firstApplied=record.ApplicationTick;}
                    if(firstSeen>=0&&!paused)
                    {
                        long tick=a.Tick;var sampled=metric.Reports.Single().SampledTicks;
                        for(int wall=0;wall<100;wall++){Assert.True(a.TryAdvance(paused:true));metric.Observe(a);Assert.AreEqual(tick,a.Tick);}
                        Assert.AreEqual(sampled,metric.Reports.Single().SampledTicks);paused=true;
                        var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c,ai);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());restored.Stop();
                    }
                }
                var report=metric.Reports.Single();Assert.Greater(firstSeen,0,"Threat must enter center range through ordinary native movement");Assert.GreaterOrEqual(firstApplied,firstSeen+reaction);Assert.True(paused);
                Assert.Greater(report.AppliedCommands,0);Assert.Greater(report.AppliedTacticalCommands,0);Assert.Greater(report.PeakMajorArmies,0);Assert.LessOrEqual(report.PeakMajorArmies,ai.DifficultyValue(difficulty,"majorArmies"));
                TestContext.WriteLine(JsonConvert.SerializeObject(new{Scenario="A3-10/A3-11/A3-12",Difficulty=difficulty.ToString(),RequestedReactionSeconds=ai.DifficultyValue(difficulty,"reactionSeconds"),EffectiveReactionTicks=reaction,firstSeen,firstApplied,CommandBudget=limit,Metrics=report,Scheduled=seen.Values.OrderBy(r=>r.ActionId)}));a.Stop();
            }
        }
        [Test] public void Attention_WholeArmyCooldownCannotBeBypassedByAnotherMember()
        {
            var type=typeof(AiArmyPlanner).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");
            var loop=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{P,Opening(),71L,null},null);
            var registry=(AiArmyRegistry)Get(loop,"armies");var original=Observation(60,count:3,target:0);Assert.True(registry.TryCreate(original,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out _));
            PlayableAiObservation At(long tick,bool same=false)
            {
                var units=original.Entities.Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,false,0,0,0,u.Id==1||same?new PlayableTacticalOrderSnapshot(u.Id,u.Owner,71,1,60,PlayableTacticalOrderKind.Move,new NavPoint(4,0),0):null)).ToArray();
                return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,original.Seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,original.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
            }
            var action=new PlayableAiAction(1,"player-1",P.ProfileId,P.Revision,71,61,PlayableCommandKind.Move,new[]{2},new NavPoint(8,0),seed:original.Seed);
            var proposal=type.GetMethod("Proposal",BindingFlags.Instance|BindingFlags.NonPublic);
            object Propose(long tick,int priority=0)=>proposal.Invoke(loop,new object[]{AiArmyPlanner.Policy,action,At(tick),priority});
            long cooldown=AiProfile.SecondsToTicks(AiProfile.Initial.Value("decision.repeatOrderSeconds"),30);
            Assert.IsNull(Propose(60+cooldown-1),"Unordered member cannot bypass another member's effective group order");Assert.NotNull(Propose(60+cooldown));Assert.NotNull(Propose(61,2),"Survival preemption retains C3 priority");
            var identical=new PlayableAiAction(1,"player-1",P.ProfileId,P.Revision,71,500,PlayableCommandKind.Move,new[]{2},new NavPoint(4,0),seed:original.Seed);
            Assert.IsNull(proposal.Invoke(loop,new object[]{AiArmyPlanner.Policy,identical,At(500,true),0}),"Unchanged order remains a no-op after cooldown");
        }
        [Test] public void Attention_DelayedIdenticalEffectiveOrderDoesNotAllocateAnotherSequence()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<45;i++)Step(a,host);
                var item=((IEnumerable)Get(Owner(a),"pendingItems")).Cast<object>().Single(p=>(string)Get(p,"PolicyName")==AiArmyPlanner.Policy);var action=(PlayableAiAction)Get(item,"Action");
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,500,"west-owner",action.Kind,action.CopyEntityIds(),action.Target,targetId:action.TargetId,origin:PlayableOrderOrigin.Ai,source:action.SourceIdentity,jobId:action.ActionId,actionId:action.ActionId)).Status);
                while(a.Tick<((long)Get(item,"DueTick"))+2)Step(a,host);
                var terminal=a.CaptureDiagnosticCheckpoints().Single().Records.Single(r=>r.ActionId==action.ActionId&&r.Status!=PlayableAiDeliveryStatus.Scheduled);
                Assert.AreEqual(PlayableAiDeliveryStatus.Cancelled,terminal.Status);Assert.Zero(terminal.CommandSequence);StringAssert.Contains("already matches",terminal.Message);a.Stop();
            }
        }
        [TestCase(false)][TestCase(true)]
        public void Attention_ActualClosedOrderRetainsWholeArmyCooldown(bool arrival)
        {
            var basis=Config(3,absent:true);var roster=basis.Roster.Select(p=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,OfflineControl.Human)).ToArray();
            var c=new OfflineMatchConfiguration(P,"a6-closed-order","a6-native-closed-order",basis.RouteProvenance,basis.Seed,roster,basis.Starts.ToArray(),basis.Sites.ToArray(),Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:basis.ScenarioUnits.ToArray(),scenarioBuildings:basis.ScenarioBuildings.ToArray());
            var a=new PlayableAuthorityTick(c,71);var type=typeof(AiArmyPlanner).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");
            var loop=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{P,PlayableAiOpeningComposition.Initialize(c.Seed,"west-owner"),71L,null},null);
            var registry=(AiArmyRegistry)Get(loop,"armies");var initial=PlayableAiObservation.From(a.ParticipantView("west-owner"));var ids=initial.Entities.Where(e=>e.Owner==initial.Owner&&e.Kind==PlayableEntityKind.Tank).Select(e=>e.Id).ToArray();Assert.True(registry.TryCreate(initial,AiArmyRole.Attack,AiArmyPlanner.Policy,ids,out _));
            var first=initial.Entities.Single(e=>e.Id==ids[0]);var kind=arrival?PlayableCommandKind.Move:PlayableCommandKind.Stop;
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,500,"west-owner",kind,new[]{first.Id},new NavPoint(first.Position.X+.5,first.Position.Z),origin:PlayableOrderOrigin.Ai,source:PlayableAiOpeningComposition.SourceIdentity,jobId:500,actionId:500)).Status);
            long cooldown=AiProfile.SecondsToTicks(AiProfile.Initial.Value("decision.repeatOrderSeconds"),30);
            using(var host=new UnityHostRouteService())
            {
                if(arrival)while(a.ParticipantView("west-owner").Entities.Single(e=>e.Id==first.Id).NavigationOutcome!=NavigationOutcome.Arrived&&a.Tick<cooldown-1)Step(a,host);
                var closed=a.ParticipantView("west-owner").Entities.Single(e=>e.Id==first.Id);Assert.IsNull(closed.CurrentOrder);Assert.NotNull(closed.OrderStamp);Assert.AreEqual(kind,closed.OrderStamp.Kind);Assert.AreEqual(0,closed.OrderStamp.Tick);
                if(arrival)Assert.AreEqual(NavigationOutcome.Arrived,closed.NavigationOutcome);
                var proposal=type.GetMethod("Proposal",BindingFlags.Instance|BindingFlags.NonPublic);
                object Propose(int priority)
                {
                    var o=PlayableAiObservation.From(a.ParticipantView("west-owner"));var sibling=o.Entities.Single(e=>e.Id==ids[1]);
                    var action=new PlayableAiAction(1,"west-owner",P.ProfileId,P.Revision,71,o.SnapshotSequence,PlayableCommandKind.Move,new[]{sibling.Id},new NavPoint(sibling.Position.X+4,sibling.Position.Z),seed:c.Seed);
                    return proposal.Invoke(loop,new object[]{AiArmyPlanner.Policy,action,o,priority});
                }
                Assert.Less(a.Tick,cooldown);Assert.IsNull(Propose(0),"Closed actual order still consumes this army's attention window");Assert.NotNull(Propose(2),"Survival may preempt the cooldown");
                while(a.Tick<cooldown-1)Step(a,host);Assert.IsNull(Propose(0));Step(a,host);Assert.NotNull(Propose(0),"Exact profile boundary admits changed useful order");
                var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());Assert.AreEqual(kind,restored.ParticipantView("west-owner").Entities.Single(e=>e.Id==first.Id).OrderStamp.Kind);restored.Stop();a.Stop();
                TestContext.WriteLine(JsonConvert.SerializeObject(new{Scenario="A3-11 closed native order",arrival,ClosedTick=closed.OrderStamp.Tick,CooldownTicks=cooldown}));
            }
        }
        [TestCase(false)][TestCase(true)]
        public void Attention_DeliveryNeverCallsMissingOrForeignRecipientsAnEffectiveNoOp(bool foreign)
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<45;i++)Step(a,host);var owner=Owner(a);
                var item=((IEnumerable)Get(owner,"pendingItems")).Cast<object>().Single(p=>(string)Get(p,"PolicyName")==AiArmyPlanner.Policy);var original=(PlayableAiAction)Get(item,"Action");
                int id=foreign?a.ParticipantView("east-owner").Entities.Single(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank).Id:int.MaxValue;
                // Deliberately corrupt only the pending recipient boundary. The real authority
                // must reject it, not mislabel absence/foreign Stop as an effective owner order.
                var corrupt=new PlayableAiAction(original.ActionId,original.PlayerId,original.ProfileId,original.ProfileRevision,original.Generation,original.SnapshotSequence,foreign?PlayableCommandKind.Stop:original.Kind,new[]{id},original.Target,targetId:original.TargetId,seed:original.Seed,sourceIdentity:original.SourceIdentity);
                item.GetType().GetField("Action",BindingFlags.Instance|BindingFlags.Public).SetValue(item,corrupt);
                while(a.Tick<((long)Get(item,"DueTick"))+2)Step(a,host);
                var terminal=a.CaptureDiagnosticCheckpoints().Single().Records.Single(r=>r.ActionId==original.ActionId&&r.Status!=PlayableAiDeliveryStatus.Scheduled);
                Assert.AreEqual(PlayableAiDeliveryStatus.Rejected,terminal.Status);Assert.AreEqual(PlayableCommandStatus.InvalidEntity,terminal.RuntimeStatus);Assert.Greater(terminal.CommandSequence,0);StringAssert.DoesNotContain("already matches",terminal.Message);a.Stop();
                TestContext.WriteLine(JsonConvert.SerializeObject(new{Scenario="A3-11 invalid recipient boundary",foreign,terminal}));
            }
        }
        [TestCase("normal")][TestCase("not-arrived")][TestCase("unsafe")][TestCase("foreign")][TestCase("held")][TestCase("human")][TestCase("already-following")][TestCase("missing-proof")]
        public void Attention_CohortFollowOnlyCompletesTriggeredSafeOwnedAssembly(string change)
        {
            long tick=80;var goal=new NavPoint(-P.FollowDistance,0);var source=PlayableAiOpeningComposition.SourceIdentity;
            PlayableOrderStamp Stamp(int id,long sequence,PlayableOrderOrigin origin=PlayableOrderOrigin.Ai,PlayableCommandKind kind=PlayableCommandKind.Move)=>new PlayableOrderStamp(id,PlayableOwner.Player,71,sequence,sequence,sequence,origin,kind,origin==PlayableOrderOrigin.Ai?source:null,origin==PlayableOrderOrigin.Ai?sequence:0,origin==PlayableOrderOrigin.Ai?sequence:0);
            var trigger=change=="not-arrived"?NavigationOutcome.Moving:NavigationOutcome.Arrived;
            var actors=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(0,0),100,false,0,0,0),
                new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-3.6,0),100,change=="not-arrived",0,0,0,change=="not-arrived"?new PlayableTacticalOrderSnapshot(2,PlayableOwner.Player,71,10,10,PlayableTacticalOrderKind.Move,goal,0):null,navigationOutcome:trigger,orderStamp:Stamp(2,10)),
                new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-8,0),100,true,0,0,0,
                    new PlayableTacticalOrderSnapshot(3,PlayableOwner.Player,71,change=="human"?30:20,change=="human"?30:20,change=="already-following"?PlayableTacticalOrderKind.Follow:PlayableTacticalOrderKind.Move,new NavPoint(-5,0),change=="already-following"?1:0),orderStamp:Stamp(3,change=="human"?30:20,change=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,change=="already-following"?PlayableCommandKind.Follow:PlayableCommandKind.Move)),
                new PlayableEntitySnapshot(4,change=="foreign"?PlayableOwner.Enemy:PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(change=="foreign"?-40:-12,0),100,false,0,0,0,held:change=="held")};
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),50)},Array.Empty<KnownBuilding>());
            var routes=actors.Where(u=>u.Id>1&&u.Owner==PlayableOwner.Player&&!(change=="missing-proof"&&u.Id==4)).Select(u=>new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),PlayableRouteTargetKind.FriendlyAnchor,1,goal,goal,new[]{goal})).ToArray();
            var entities=actors.Concat(change=="unsafe"?new[]{new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-22,0),100,false,0,0,0)}:Array.Empty<PlayableEntitySnapshot>()).ToArray();
            var o=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,1,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities,Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:routes,activeProfile:P));
            var army=new AiArmyState{Id=1,OwnerId="player-1",TacticalOwner=AiArmyPlanner.Policy,Phase=AiArmyPhase.Advancing,Members=new[]{1,2,3,4},Reinforcements=new[]{2,3,4},LeaderId=1};
            var records=new[]{10L,20L}.Select(id=>new PlayableAiTraceRecord("fixture",id,id,id,id,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"ordinary gather",ownerId:"player-1",sourceIdentity:source,receiptIdentity:new AiReceiptIdentity(71,"player-1",1,(int)id),policy:AiArmyPlanner.Policy,kind:id==20&&change=="already-following"?PlayableCommandKind.Follow:PlayableCommandKind.Move)).ToArray();
            var before=JsonConvert.SerializeObject(new{o,army,records});
            var action=(PlayableAiAction)typeof(AiTacticalExecutor).GetMethod("ArrivalAssembly",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{o,army,P,1L,records});
            Assert.AreEqual(before,JsonConvert.SerializeObject(new{o,army,records}),"Cohort proposal must be read-only");
            if(change=="not-arrived"){Assert.IsNull(action,"A valid in-progress Move must not be restarted without a genuine arrived correction");return;}
            Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Follow,action.Kind);Assert.AreEqual(1,action.TargetId);
            var expected=change=="human"||change=="already-following"?new[]{2,4}:new[]{"unsafe","foreign","held","missing-proof"}.Contains(change)?new[]{2,3}:new[]{2,3,4};
            CollectionAssert.AreEqual(expected,action.EntityIds);
        }
        [TestCase(false)][TestCase(true)]
        public void Attention_PendingNativeQueueStopRemainsUsefulAtProposalAndDelivery(bool deferred)
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<45;i++)Step(a,host);var owner=Owner(a);
                var item=((IEnumerable)Get(owner,"pendingItems")).Cast<object>().Single(p=>(string)Get(p,"PolicyName")==AiArmyPlanner.Policy);
                var original=(PlayableAiAction)Get(item,"Action");long due=(long)Get(item,"DueTick");
                while(a.Tick<due)Step(a,host);
                var view=a.ParticipantView("west-owner");var actor=view.Entities.First(u=>original.EntityIds.Contains(u.Id));
                var destination=new NavPoint(actor.Position.X+2,actor.Position.Z);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,500,"west-owner",PlayableCommandKind.Move,original.CopyEntityIds(),destination,origin:PlayableOrderOrigin.Ai,source:original.SourceIdentity,jobId:900,actionId:900)).Status);
                if(deferred)Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,501,"west-owner",PlayableCommandKind.Move,original.CopyEntityIds(),new NavPoint(destination.X+2,destination.Z),origin:PlayableOrderOrigin.Ai,source:original.SourceIdentity,jobId:901,actionId:901,mode:PlayableOrderMode.Append)).Status);
                view=a.ParticipantView("west-owner");actor=view.Entities.Single(u=>u.Id==actor.Id);
                Assert.Null(actor.CurrentOrder);Assert.NotNull(actor.Queue.Pending);Assert.AreEqual(deferred?1:0,actor.Queue.Deferred.Count);
                var observation=PlayableAiObservation.From(view);var projected=observation.Entities.Single(u=>u.Id==actor.Id);
                var stop=new PlayableAiAction(original.ActionId,original.PlayerId,original.ProfileId,original.ProfileRevision,original.Generation,original.SnapshotSequence,PlayableCommandKind.Stop,original.CopyEntityIds(),default,seed:original.Seed,sourceIdentity:original.SourceIdentity);
                var proposal=owner.GetType().GetMethod("Proposal",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,new object[]{AiArmyPlanner.Policy,stop,observation,2});
                var saved=a.CaptureBytes();var control=PlayableAuthorityTick.RestoreBytes(saved,c);
                Assert.AreEqual(PlayableCommandStatus.Applied,control.Apply(new PlayableCommand(71,502,"west-owner",PlayableCommandKind.Stop,original.CopyEntityIds(),origin:PlayableOrderOrigin.Ai,source:original.SourceIdentity,jobId:902,actionId:902)).Status);
                item.GetType().GetField("Action",BindingFlags.Instance|BindingFlags.Public).SetValue(item,stop);
                a.TryAdvance();
                var terminal=a.CaptureDiagnosticCheckpoints().Single().Records.Single(r=>r.ActionId==original.ActionId&&r.Status!=PlayableAiDeliveryStatus.Scheduled);
                TestContext.WriteLine(JsonConvert.SerializeObject(new{Scenario="S3 pending queue useful Stop",deferred,OwnQueue=actor.Queue,ProjectedQueue=projected.Queue,ProposalAdmitted=proposal!=null,terminal}));
                {
                    Assert.AreSame(actor.Queue,projected.Queue,"Existing immutable owner-safe queue projection must survive AI conversion");
                    Assert.NotNull(proposal,"Pending/deferred movement makes Stop useful");
                    Assert.AreEqual(PlayableAiDeliveryStatus.Applied,terminal.Status,"Due Stop must cancel actual pending native orders");
                    Assert.Greater(terminal.CommandSequence,0);
                    foreach(var id in original.EntityIds){Assert.Null(a.ParticipantView("west-owner").Entities.Single(u=>u.Id==id).Queue);Assert.Null(control.ParticipantView("west-owner").Entities.Single(u=>u.Id==id).Queue);}
                }
                host.Service(a,64);Step(a,host);var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
                for(int i=0;i<5;i++){Step(a,host);Step(restored,host);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes(),"Stop queue cancellation survives restored suffix");}
                a.Stop();control.Stop();restored.Stop();
            }
        }
        [Test] public void Attention_ContradictoryDeferredTailMakesIdenticalActiveReplaceUseful()
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);var owner=Owner(a);
            using(var host=new UnityHostRouteService())
            {
                var actor=a.ParticipantView("west-owner").Entities.First(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank);
                var goal=new NavPoint(actor.Position.X+4,actor.Position.Z);var source=PlayableAiOpeningComposition.SourceIdentity;
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,500,"west-owner",PlayableCommandKind.Move,new[]{actor.Id},goal,origin:PlayableOrderOrigin.Ai,source:source,jobId:900,actionId:900)).Status);
                host.Service(a,64);Step(a,host);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,501,"west-owner",PlayableCommandKind.Move,new[]{actor.Id},new NavPoint(goal.X,goal.Z+4),origin:PlayableOrderOrigin.Ai,source:source,jobId:901,actionId:901,mode:PlayableOrderMode.Append)).Status);
                var observation=PlayableAiObservation.From(a.ParticipantView("west-owner"));var projected=observation.Entities.Single(u=>u.Id==actor.Id);
                Assert.NotNull(projected.CurrentOrder);Assert.True(projected.CurrentOrder.Destination.Equals(goal));
                var replace=new PlayableAiAction(950,"west-owner",P.ProfileId,P.Revision,71,observation.SnapshotSequence,PlayableCommandKind.Move,new[]{actor.Id},goal,seed:c.Seed,sourceIdentity:source);
                var proposal=owner.GetType().GetMethod("Proposal",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,new object[]{AiArmyPlanner.Policy,replace,observation,2});
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,502,"west-owner",PlayableCommandKind.Move,new[]{actor.Id},goal,origin:PlayableOrderOrigin.Ai,source:source,jobId:950,actionId:950)).Status);
                Assert.Zero(a.ParticipantView("west-owner").Entities.Single(u=>u.Id==actor.Id).Queue.Deferred.Count,"Ordinary Replace clears the contradictory tail even when the active goal matches");
                Assert.NotNull(proposal,"The effective order includes the contradictory deferred tail");a.Stop();
            }
        }
        [Test] public void Attention_ImmutableQueueConversionRetainsOwnAndHidesForeign()
        {
            var o=Observation();var queue=new PlayableUnitQueueSnapshot(null,new PlayableQueuedOrderSnapshot(1,1,1,1,PlayableCommandKind.Move,null,0),Array.Empty<PlayableQueuedOrderSnapshot>(),null);
            var entities=o.Entities.Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,u.Moving,u.TargetId,u.HullHeading,u.TurretHeading,queue:queue)).ToArray();
            var converted=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,o.Seed,1,1,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities,o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null));
            Assert.AreSame(queue,converted.Entities.First(u=>u.Owner==PlayableOwner.Player).Queue);
            Assert.True(converted.Entities.Where(u=>u.Owner!=PlayableOwner.Player).All(u=>u.Queue==null));
        }
    }
}

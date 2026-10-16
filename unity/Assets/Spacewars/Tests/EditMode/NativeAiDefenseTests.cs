using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiInitiativeTests
    {
        private static PlayableSnapshot DefenseView(long tick,bool threat,int count=6,bool proof=true)
        {
            var o=Observation(tick,count:count,target:threat?101:0,proof:proof);
            var center=new PlayableBuildingSnapshot(60,PlayableOwner.Player,PlayableBuildingKind.Headquarters,new NavPoint(0,0),P.HeadquartersHealth,1,0,0,default);
            return new PlayableSnapshot(P.ProfileId,P.Revision,71,o.Seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,o.Entities.ToArray(),o.Buildings.Concat(new[]{center}).ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:o.RouteProofs.ToArray());
        }
        [Test] public void DefenseRecovery_PreemptsAcceptedOffenseBeforeItsDueTick()
        {
            var type=typeof(AiArmyPlanner).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");
            var loop=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{P,Opening(),71L,null},null);
            void Review(PlayableSnapshot s)=>type.GetMethod("Review",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(loop,new object[]{s,0L});
            var o=Observation(60,count:6);var noCenter=new PlayableSnapshot(P.ProfileId,P.Revision,71,o.Seed,60,60,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,o.Entities.ToArray(),o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:o.RouteProofs.ToArray());
            Review(noCenter);
            var checkpoint=(PlayableAiOwnerCheckpoint)type.GetProperty("Checkpoint",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(loop);
            Assert.True(checkpoint.Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Scheduled));
            Review(DefenseView(61,true));
            checkpoint=(PlayableAiOwnerCheckpoint)type.GetProperty("Checkpoint",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(loop);
            Assert.True(checkpoint.Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Cancelled),"A3: a new observed center threat must invalidate accepted offensive delivery even while its reaction delay is pending");
            Assert.True(checkpoint.Armies.Armies.Any(a=>a.Role==AiArmyRole.MobileDefense&&a.Phase!=AiArmyPhase.Disbanded),"A3 mobile defense must have a major army slot");
        }
        [Test] public void DefenseRecovery_ReservesNearestSufficientMajorArmyWithoutScoutBypass()
        {
            var view=DefenseView(60,true,9);var o=PlayableAiObservation.From(view);
            var foes=Enumerable.Range(102,3).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(8,(id-101)*1.5),100,false,0,0,0)).ToArray();
            var own=o.Entities.Where(u=>u.Owner==o.Owner).Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,new NavPoint(u.Id<=3?0:-8,(u.Id-1)*1.5),100,false,0,0,0)).ToArray();
            var es=own.Concat(o.Entities.Where(u=>u.Owner!=o.Owner)).Concat(foes).ToArray();
            var proofs=own.SelectMany(u=>es.Where(e=>e.Owner!=o.Owner).Select(e=>new PlayableRouteProof(u.Id,u.Owner,71,60,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.VisibleEnemy,e.Id,e.Position,e.Position,new[]{e.Position}))).ToArray();
            o=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,o.Seed,60,60,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,es,o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:proofs));
            var r=Registry();Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var small));Assert.True(r.TryCreate(o,AiArmyRole.Escort,"expansion",new[]{4,5,6,7,8,9},out var enough));
            var d=new AiDefensePlanner("player-1",71);var action=d.Propose(o,P,AiProfile.Initial,r);Assert.NotNull(action);CollectionAssert.AreEquivalent(new[]{4,5,6,7,8,9},action.EntityIds,"The nearer insufficient army must not replace the sufficient army");
            Assert.AreEqual("expansion",r.Capture().Armies.Single(a=>a.Id==enough).TacticalOwner,"Proposal is pure");
            var defense=new AiIntent("defend",AiDefensePlanner.Policy,action,120,0,0,action.EntityIds.Select(id=>"recipient:"+id),2,1,"threat");
            var attack=new AiIntent("attack",AiArmyPlanner.Policy,action,120,0,0,action.EntityIds.Select(id=>"recipient:"+id),0,1,"offense");
            var arbiter=new AiDecisionArbiter(AiProfile.Initial);Assert.AreEqual(defense,arbiter.Select(new[]{attack,defense},60,1000,0,3,armies:r).Single());
            d.Commit(o,action,r);Assert.AreEqual(enough,d.ArmyId);Assert.AreEqual(small,r.ArmyFor(1));Assert.AreEqual(AiArmyRole.MobileDefense,r.Capture().Armies.Single(a=>a.Id==enough).Role);Assert.AreEqual("unit tactical owner conflict",r.Reject(attack));r.AssertInvariants();
            d.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Applied,90));
            d.Observe(PlayableAiObservation.From(DefenseView(91,false,9)),r,P);Assert.False(d.Active);Assert.True(action.EntityIds.All(id=>r.ArmyFor(id)==0));Assert.AreEqual(small,r.ArmyFor(1));
        }
        [Test] public void DefenseRecovery_FreeForceUsesOnlyNeededMembersAndRejectsMissingRoutes()
        {
            var o=PlayableAiObservation.From(DefenseView(60,true));var r=Registry();var d=new AiDefensePlanner("player-1",71);
            var a=d.Propose(o,P,AiProfile.Initial,r);Assert.NotNull(a);Assert.AreEqual(AiProfile.Initial.Value("armies.minimumUnits"),a.EntityIds.Count);Assert.IsEmpty(r.Capture().Armies);
            d.Commit(o,a,r);Assert.AreEqual(1,r.Capture().Armies.Count(g=>g.Major&&g.Phase!=AiArmyPhase.Disbanded));Assert.True(a.EntityIds.All(id=>r.ArmyFor(id)==d.ArmyId));
            var noRoute=PlayableAiObservation.From(DefenseView(61,true,proof:false));Assert.IsNull(new AiDefensePlanner("player-1",71).Propose(noRoute,P,AiProfile.Initial,Registry()));
            Assert.Throws<ArgumentException>(()=>d.Propose(Observation(62),P,AiProfile.Initial,new AiArmyRegistry("other",71,AiProfile.Initial,AiDifficulty.Fighter)),"Foreign registry cannot be used for defense");
        }
        [TestCase("ai-move-unsafe")][TestCase("ai-follow-unsafe")][TestCase("human-move")][TestCase("human-follow")]
        [TestCase("other-policy-move")][TestCase("unapplied-move")][TestCase("safe-current-move")]
        [TestCase("safe-current-follow")][TestCase("safe-core-fallback")]
        public void DefenseAssembly_UnsafeAppliedDefenseOrderStopsOnlyWhenNoSafeContinuation(string scenario)
        {
            var original=PlayableAiObservation.From(DefenseView(60,true));var registry=Registry();var defense=new AiDefensePlanner(original.OwnerId,original.Generation);
            var attack=defense.Propose(original,P,AiProfile.Initial,registry);Assert.NotNull(attack);defense.Commit(original,attack,registry);
            defense.ObserveReceipt(new PlayableAiTraceRecord(original.Identity,attack.ActionId,61,1,61,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,
                "initial Defense attack",ownerId:original.OwnerId,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(original.Generation,original.OwnerId,1,1)));
            var candidate=original.Entities.First(u=>u.Owner==original.Owner&&u.Kind==PlayableEntityKind.Tank&&!attack.EntityIds.Contains(u.Id));
            Assert.True(registry.TryReinforce(original,defense.ArmyId,AiDefensePlanner.Policy,new[]{candidate.Id}));
            var coreId=attack.EntityIds[0];var orderKind=scenario.Contains("follow")?PlayableTacticalOrderKind.Follow:PlayableTacticalOrderKind.Move;
            long sequence=77,tick=59,oldAction=99;var destination=scenario=="safe-current-move"?candidate.Position:default(NavPoint);
            int orderTarget=orderKind==PlayableTacticalOrderKind.Follow?coreId:0;
            var order=new PlayableTacticalOrderSnapshot(candidate.Id,candidate.Owner,original.Generation,sequence,tick,orderKind,destination,orderTarget);
            bool human=scenario.StartsWith("human",StringComparison.Ordinal);
            var stamp=new PlayableOrderStamp(candidate.Id,candidate.Owner,original.Generation,3,sequence,tick,human?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,
                orderKind==PlayableTacticalOrderKind.Follow?PlayableCommandKind.Follow:PlayableCommandKind.Move,
                human?null:PlayableAiOpeningComposition.SourceIdentity,human?0:oldAction,human?0:oldAction);
            var entities=original.Entities.Select(u=>u.Id==candidate.Id?new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,false,0,u.HullHeading,u.TurretHeading,
                order,held:false,navigationOutcome:NavigationOutcome.Moving,orderStamp:stamp):u).ToArray();
            var routes=original.RouteProofs.ToList();
            if(scenario=="safe-current-move")
                routes.Add(new PlayableRouteProof(candidate.Id,candidate.Owner,original.Generation,original.Tick,1,candidate.Position,PlayableUnitRules.Radius(P,candidate.Kind),
                    PlayableRouteTargetKind.FriendlyAnchor,candidate.Id,candidate.Position,candidate.Position,new[]{candidate.Position}));
            if(scenario=="safe-current-follow")
            {
                var leader=original.Entities.Single(u=>u.Id==coreId);var goal=PlayableUnitRules.FollowGoal(P,leader.Position,leader.HullHeading);
                routes.Add(new PlayableRouteProof(candidate.Id,candidate.Owner,original.Generation,original.Tick,1,candidate.Position,PlayableUnitRules.Radius(P,candidate.Kind),
                    PlayableRouteTargetKind.FriendlyAnchor,coreId,goal,goal,new[]{goal}));
            }
            if(scenario=="safe-core-fallback")
            {
                var leader=original.Entities.Single(u=>u.Id==coreId);
                routes.Add(new PlayableRouteProof(candidate.Id,candidate.Owner,original.Generation,original.Tick,1,candidate.Position,PlayableUnitRules.Radius(P,candidate.Kind),
                    PlayableRouteTargetKind.FriendlyAnchor,coreId,leader.Position,leader.Position,new[]{leader.Position}));
            }
            var observationEntities=(scenario.StartsWith("safe-",StringComparison.Ordinal)?entities.Where(u=>u.Owner==original.Owner):entities).ToArray();
            var o=PlayableAiObservation.From(new PlayableSnapshot(original.ProfileId,original.ProfileRevision,original.Generation,original.Seed,original.SnapshotSequence,original.Tick,
                RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,observationEntities,original.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),
                new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:original.Vision,routeProofs:routes.ToArray(),activeProfile:P));
            if(scenario=="safe-current-follow")Assert.True(AiTacticalExecutor.SafeRoute(o,routes.Last(),P),"The safe Follow guard requires a currently valid native-style route proof");
            var recordedPolicy=scenario=="other-policy-move"?AiArmyPlanner.Policy:AiDefensePlanner.Policy;
            var recordedStatus=scenario=="unapplied-move"?PlayableAiDeliveryStatus.Scheduled:PlayableAiDeliveryStatus.Applied;
            var currentRecord=new PlayableAiTraceRecord(o.Identity,oldAction,tick,sequence,tick,recordedStatus,
                recordedStatus==PlayableAiDeliveryStatus.Applied?PlayableCommandStatus.Applied:(PlayableCommandStatus?)null,"prior assembly order",ownerId:o.OwnerId,
                sourceIdentity:stamp.Source,receiptIdentity:new AiReceiptIdentity(o.Generation,o.OwnerId,2,1),policy:recordedPolicy,
                kind:orderKind==PlayableTacticalOrderKind.Follow?PlayableCommandKind.Follow:PlayableCommandKind.Move);
            var records=new[]{currentRecord};
            if(scenario=="safe-current-follow")
            {
                var army=registry.Capture().Armies.Single(a=>a.Id==defense.ArmyId);var leader=o.Entities.Single(u=>u.Id==coreId);
                Assert.Contains(coreId,army.Members);Assert.IsFalse(army.Reinforcements.Contains(coreId));
                Assert.AreEqual(PlayableUnitRules.FollowGoal(P,leader.Position,leader.HullHeading),routes.Last().Target);
                Assert.AreEqual(coreId,routes.Last().TargetId);
                Assert.True(o.RouteProofs.Any(r=>r.UnitId==candidate.Id&&r.TargetId==coreId&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&
                    r.Target.Equals(PlayableUnitRules.FollowGoal(P,leader.Position,leader.HullHeading))&&r.Goal.Equals(r.Target)&&AiTacticalExecutor.SafeRoute(o,r,P)),"Observation must retain the exact safe FollowGoal proof");
                TestContext.WriteLine("SAFE_CURRENT_FOLLOW_FIXTURE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Candidate=candidate.Id,CoreId=coreId,Core=army.Members.Except(army.Reinforcements).ToArray(),CurrentOrder=order,Goal=routes.Last().Target,Safe=AiTacticalExecutor.SafeRoute(o,routes.Last(),P)}));
            }
            var action=defense.ProposeAssembly(o,P,registry,records);
            if(scenario=="ai-move-unsafe"||scenario=="ai-follow-unsafe")
            {
                Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Stop,action.Kind);CollectionAssert.AreEqual(new[]{candidate.Id},action.EntityIds);
                Assert.DoesNotThrow(()=>defense.CommitAssembly(o,action,registry,P,records));Assert.AreEqual(action.ActionId,defense.PendingId);
                Assert.AreEqual(registry.ArmyFor(candidate.Id),defense.ArmyId,"Stop must retain Defense reinforcement ownership");
                var scheduled=new PlayableAiTraceRecord(o.Identity,action.ActionId,70,0,0,PlayableAiDeliveryStatus.Scheduled,null,"stop scheduled",o.OwnerId,
                    PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(o.Generation,o.OwnerId,3,1),AiDefensePlanner.Policy,PlayableCommandKind.Stop);
                defense.ObserveReceipt(scheduled);Assert.AreEqual(action.ActionId,defense.PendingId,"Scheduled feedback must retain the pending Stop");
                var terminal=new PlayableAiTraceRecord(o.Identity,action.ActionId,70,78,70,PlayableAiDeliveryStatus.Cancelled,PlayableCommandStatus.Cancelled,"stop cancelled",o.OwnerId,
                    PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(o.Generation,o.OwnerId,3,1),AiDefensePlanner.Policy,PlayableCommandKind.Stop);
                Assert.DoesNotThrow(()=>defense.ObserveReceipt(terminal));Assert.Zero(defense.PendingId,"Ordinary terminal feedback releases pending state");
                Assert.AreEqual(defense.ArmyId,registry.ArmyFor(candidate.Id),"A terminal Stop receipt must leave reinforcement membership intact");
                return;
            }
            if(scenario=="safe-core-fallback")Assert.AreEqual(PlayableCommandKind.Move,action?.Kind,"A safe ordinary assembly fallback must remain available");
            else if(scenario=="safe-current-follow")Assert.AreNotEqual(PlayableCommandKind.Stop,action?.Kind,"A proven safe current Follow must never be replaced by Stop; action="+action?.Kind+" entities="+string.Join(",",action?.EntityIds??Array.Empty<int>())+" reinforcements="+string.Join(",",registry.Capture().Armies.Single(a=>a.Id==defense.ArmyId).Reinforcements));
            else Assert.IsNull(action,"Human/foreign/unapplied orders and safe current legs must not be replaced by Stop");
        }
        private static OfflineMatchConfiguration DefenseConfig()
        {
            var basis=Config(6);var scenario=Enumerable.Range(0,6).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-20+(i%3)*2,-8-(i/3)*3)))
                .Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-10,0))}).ToArray();
            return new OfflineMatchConfiguration(P,"a3-defense","a3-03-a3-05-unassisted-native","UnityHostRouteService",basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario,scenarioBuildings:basis.ScenarioBuildings.ToArray());
        }
        private static OfflineMatchConfiguration DefenseDetourConfig()
        {
            var basis=Config(7);
            var tankRadius=PlayableUnitRules.Radius(P,PlayableEntityKind.Tank);
            var leftX=-P.ArenaHalfExtent+tankRadius+.25;
            var scenario=Enumerable.Range(0,7).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,
                    new NavPoint(leftX+(i%3)*2,-20-(i/3)*3)))
                .Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-10,0))}).ToArray();
            return new OfflineMatchConfiguration(P,"a3-defense-detour","a3-03-a3-05-unassisted-native-detour","UnityHostRouteService",basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),
                new[]{new NavObstacle(-18.5,-31,-17.5,10)},new double[,]{{0,100},{100,0}},scenario:scenario,scenarioBuildings:basis.ScenarioBuildings.ToArray());
        }
        [Test] public void DefenseRecovery_ActualDetourReinforcementUsesAppliedAssemblyAndJoinsOriginalArmy()
        {
            var c=DefenseDetourConfig();Assert.AreEqual(7,c.ScenarioUnits.Count(u=>u.LogicalPlayer==1));Assert.AreEqual(1,c.ScenarioUnits.Count(u=>u.LogicalPlayer==2));
            Assert.AreEqual(1,c.Obstacles.Count);Assert.AreEqual(-18.5,c.Obstacles[0].MinX);Assert.AreEqual(-17.5,c.Obstacles[0].MaxX);
            Assert.AreEqual(-31,c.Obstacles[0].MinZ);Assert.AreEqual(10,c.Obstacles[0].MaxZ);
            var tankRadius=PlayableUnitRules.Radius(P,PlayableEntityKind.Tank);
            Assert.Less(c.Obstacles[0].MinZ+P.ArenaHalfExtent,2*tankRadius,"The lower obstacle-to-arena gap must be too narrow for a Tank");
            var authoredGeometry=new NavGeometry(P.ArenaHalfExtent,c.Obstacles.ToArray(),1);
            foreach(var unit in c.ScenarioUnits)
                Assert.True(authoredGeometry.IsFree(unit.Position,PlayableUnitRules.Radius(P,unit.Kind)),"Scenario actor must fit the arena and obstacle geometry: "+unit.Position);
            var a=new PlayableAuthorityTick(c,71);var candidates=Array.Empty<int>();var candidate=0;long armyId=0;long appliedTick=-1;long joinedTick=-1;long safeReadbackTick=-1;bool lifecycleVerified=false;
            PlayableAiTraceRecord applied=null;
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<120&&armyId==0;i++)
                {
                    Step(a,host);var owner=Owner(a);var defense=(AiDefensePlanner)Get(owner,"defense");var registry=(AiArmyRegistry)Get(owner,"armies");
                    var view=a.ParticipantView("west-owner");
                    Assert.AreEqual(7,view.Entities.Count(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank&&u.Health>0));
                    Assert.AreEqual(1,view.Entities.Count(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank&&u.Health>0),"threat remains live and visible in HQ vision");
                    if(!defense.Active)continue;
                    armyId=defense.ArmyId;var army=registry.Capture().Armies.Single(x=>x.Id==armyId&&x.TacticalOwner==AiDefensePlanner.Policy);
                    candidates=view.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank&&u.Health>0&&!u.Held&&registry.ArmyFor(u.Id)==0&&
                            u.CurrentOrder==null&&u.NavigationOutcome==NavigationOutcome.Idle&&u.Queue==null)
                        .Select(u=>u.Id).ToArray();
                    Assert.Greater(candidates.Length,0,"actual Defense activation must leave at least one live idle unclaimed Tank before owner observation updates reinforcement claims");
                }
                Assert.Greater(armyId,0,"ordinary actual threat observation must activate a Defense army");
                Assert.Greater(candidates.Length,0,"capture candidate IDs from the actual registry at Defense activation");
                PlayableAiObservation decisionObservation=null;AiArmyRegistryState decisionArmies=null;PlayableAiAction[] decisionPending=null;long decisionTick=-1;bool ownerGeometryVerified=false;
                var assemblyCausalSignatures=new HashSet<string>(StringComparer.Ordinal);
                PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests="west-owner";
                PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=(ownerId,tick,observation,armies,pending)=>
                {
                    if(ownerId!="west-owner")return;
                    decisionTick=tick;decisionObservation=observation;decisionArmies=armies;decisionPending=pending;
                    if(candidate>0)
                    {
                        var unit=observation.Entities.SingleOrDefault(u=>u.Id==candidate&&u.Owner==observation.Owner);
                        var army=armies.Armies.SingleOrDefault(x=>x.Id==armyId&&x.TacticalOwner==AiDefensePlanner.Policy);
                        var owner=Owner(a);var defense=(AiDefensePlanner)Get(owner,"defense");var registry=(AiArmyRegistry)Get(owner,"armies");
                        var records=a.CaptureDiagnosticCheckpoints().Single().Records;
                        var causalCore=army==null?Array.Empty<PlayableEntitySnapshot>():army.Members.Where(id=>!army.Reinforcements.Contains(id))
                            .Select(id=>observation.Entities.SingleOrDefault(u=>u.Id==id&&u.Health>0)).Where(u=>u!=null).ToArray();
                        var threats=AiDefensePlanner.Threats(observation,P);
                        var routes=unit==null?Array.Empty<PlayableRouteProof>():observation.RouteProofs.Where(r=>r.UnitId==candidate&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&
                            r.Owner==observation.Owner&&r.Generation==observation.Generation&&r.Tick==tick).ToArray();
                        bool IsFollowGoal(PlayableRouteProof r)=>causalCore.Any(c=>c.Id==r.TargetId&&r.Target.Equals(PlayableUnitRules.FollowGoal(P,c.Position,c.HullHeading))&&r.Goal.Equals(r.Target));
                        bool IsCorePosition(PlayableRouteProof r)=>causalCore.Any(c=>c.Id==r.TargetId&&c.Position.Equals(r.Target));
                        var safeFollow=routes.Where(r=>IsFollowGoal(r)&&AiTacticalExecutor.SafeRoute(observation,r,P)).ToArray();
                        var safeCore=routes.Where(r=>IsCorePosition(r)&&AiTacticalExecutor.SafeRoute(observation,r,P)).ToArray();
                        var safeHome=routes.Where(r=>observation.Buildings.Any(b=>b.Owner==observation.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&r.TargetId==b.Id&&r.Target.Equals(b.Position))&&AiTacticalExecutor.SafeRoute(observation,r,P)).ToArray();
                        var retreatHome=routes.Where(r=>observation.Buildings.Any(b=>b.Owner==observation.Owner&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&r.TargetId==b.Id&&r.Target.Equals(b.Position))&&AiTacticalExecutor.WithdrawalRoute(observation,r,P)).ToArray();
                        var stamp=unit?.OrderStamp;
                        var currentReceipt=stamp==null?null:records.FirstOrDefault(r=>r.Policy==AiDefensePlanner.Policy&&r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&
                            r.ApplicationTick==stamp.Tick&&r.Kind==stamp.Kind&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ReceiptIdentity?.OwnerId==observation.OwnerId);
                        var proposed=defense.ProposeAssembly(observation,P,registry,records);
                        var signature=string.Join("|",new[]{unit?.CurrentOrder?.Kind.ToString()??"none",unit?.CurrentOrder?.TargetId.ToString()??"0",stamp?.ActionId.ToString()??"0",
                            safeFollow.Length.ToString(),safeCore.Length.ToString(),safeHome.Length.ToString(),retreatHome.Length.ToString(),proposed?.Kind.ToString()??"none",
                            proposed==null?"":string.Join(",",proposed.EntityIds),army?.Phase.ToString()??"missing",registry.ArmyFor(candidate).ToString(),string.Join(",",threats.Select(t=>t.Id))});
                        if(assemblyCausalSignatures.Add(signature))
                        {
                            var routeReadback=routes.Select(r=>new{r.TargetId,r.Target,r.Goal,Path=r.Path.ToArray(),Safe=AiTacticalExecutor.SafeRoute(observation,r,P),Withdrawal=AiTacticalExecutor.WithdrawalRoute(observation,r,P)}).ToArray();
                            var distances=unit==null?Array.Empty<object>():threats.Select(t=>(object)new{t.Id,t.Position,Distance=Math.Sqrt(Math.Pow(unit.Position.X-t.Position.X,2)+Math.Pow(unit.Position.Z-t.Position.Z,2)),
                                StopEnvelope=PlayableUnitRules.Range(P,t.Kind,t.Upgraded)+PlayableUnitRules.Radius(P,unit.Kind)+PlayableUnitRules.Radius(P,t.Kind)}).ToArray();
                            TestContext.WriteLine("DEFENSE_ASSEMBLY_CAUSAL "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Tick=tick,Candidate=candidate,Unit=unit==null?null:new{unit.Position,Order=unit.CurrentOrder,Stamp=unit.OrderStamp,unit.NavigationOutcome,unit.Queue},
                                Army=army,ArmyFor=registry.ArmyFor(candidate),PendingId=defense.PendingId,CurrentReceipt=currentReceipt,Threats=distances,Core=causalCore.Select(u=>new{u.Id,u.Position,u.HullHeading}).ToArray(),
                                SafeFollowCount=safeFollow.Length,SafeCoreCount=safeCore.Length,SafeHomeCount=safeHome.Length,RetreatHomeCount=retreatHome.Length,Routes=routeReadback,
                                Proposal=proposed==null?null:new{proposed.Kind,Actors=proposed.CopyEntityIds(),proposed.TargetId,proposed.Target}},Newtonsoft.Json.Formatting.Indented));
                        }
                    }
                    if(ownerGeometryVerified||!armies.Armies.Any(x=>x.Id==armyId&&x.TacticalOwner==AiDefensePlanner.Policy&&x.Phase!=AiArmyPhase.Disbanded)||AiDefensePlanner.Threats(observation,P).Length==0)return;
                    var obstacle=c.Obstacles[0];Assert.NotNull(observation.Vision,"Actual owner projection must include its current vision snapshot");
                    var corners=new[]{new NavPoint(obstacle.MinX,obstacle.MinZ),new NavPoint(obstacle.MinX,obstacle.MaxZ),new NavPoint(obstacle.MaxX,obstacle.MinZ),new NavPoint(obstacle.MaxX,obstacle.MaxZ)};
                    var visibleCorners=corners.Select(observation.Vision.IsVisible).ToArray();
                    var defenseArmy=armies.Armies.Single(x=>x.Id==armyId);var core=defenseArmy.Members.Where(id=>!defenseArmy.Reinforcements.Contains(id)).ToHashSet();
                    var threat=AiDefensePlanner.Threats(observation,P).OrderBy(x=>x.Id).First();
                    var threatRoutes=observation.RouteProofs.Where(r=>r.Kind==PlayableRouteTargetKind.VisibleEnemy&&r.TargetId==threat.Id&&core.Contains(r.UnitId)&&r.Tick==observation.Tick&&r.Generation==observation.Generation).ToArray();
                    var detour=threatRoutes.FirstOrDefault(r=>r.Path.Any(point=>point.Z>obstacle.MaxZ));
                    var geometryReadback=new{Tick=tick,Threat=threat.Id,Obstacle=new{obstacle.MinX,obstacle.MinZ,obstacle.MaxX,obstacle.MaxZ},Corners=corners.Select((point,i)=>new{point,Visible=visibleCorners[i]}).ToArray(),Core=core.OrderBy(id=>id).ToArray(),ThreatRoutes=threatRoutes.Select(r=>new{r.UnitId,r.TargetId,r.Goal,Path=r.Path.ToArray()}).ToArray(),DetourFound=detour!=null};
                    TestContext.WriteLine("DEFENSE_DETOUR_OWNER_GEOMETRY "+Newtonsoft.Json.JsonConvert.SerializeObject(geometryReadback,Newtonsoft.Json.Formatting.Indented));
                    Assert.True(visibleCorners.All(x=>x),"Actual owner vision must expose all four authored obstacle corners before the detour assertion");
                    Assert.NotNull(detour,"Actual owner route projection must prove a threat route around the upper obstacle boundary before the join horizon");
                    ownerGeometryVerified=true;
                };
                try
                {
                    for(int i=0;i<690&&joinedTick<0;i++)
                    {
                        Step(a,host);var cp=a.CaptureDiagnosticCheckpoints().Single();var owner=Owner(a);var registry=(AiArmyRegistry)Get(owner,"armies");
                        if(candidate==0&&decisionTick>=0&&decisionObservation!=null&&decisionArmies!=null&&decisionPending!=null&&safeReadbackTick!=decisionTick)
                        {
                            var observation=decisionObservation;var armyState=decisionArmies;
                            var army=armyState.Armies.SingleOrDefault(x=>x.Id==armyId&&x.TacticalOwner==AiDefensePlanner.Policy&&x.Phase!=AiArmyPhase.Disbanded);
                            if(army!=null&&AiDefensePlanner.Threats(observation,P).Length>0)
                            foreach(var id in candidates)
                            {
                                var unit=observation.Entities.SingleOrDefault(u=>u.Id==id&&u.Owner==observation.Owner&&u.Health>0);
                                var assigned=armyState.Armies.SingleOrDefault(x=>x.Members.Contains(id)||x.Reinforcements.Contains(id));
                                bool ownershipOk=assigned==null||assigned.Id==armyId&&assigned.Reinforcements.Contains(id);
                                bool pendingFree=decisionPending.All(x=>!x.EntityIds.Contains(id));
                                var core=army.Members.Where(x=>!army.Reinforcements.Contains(x)).ToHashSet();
                                var safe=unit==null?Array.Empty<PlayableRouteProof>():observation.RouteProofs.Where(r=>r.UnitId==id&&r.Owner==observation.Owner&&
                                        r.Generation==observation.Generation&&r.Tick==observation.Tick&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&core.Contains(r.TargetId))
                                    .Where(r=>AiTacticalExecutor.SafeRoute(observation,r,P)).ToArray();
                                if(unit!=null&&!unit.Held&&unit.CurrentOrder==null&&unit.NavigationOutcome==NavigationOutcome.Idle&&unit.Queue==null&&
                                    AiRosterCatalog.Initial.For(unit.Kind).lineWeight>0&&ownershipOk&&pendingFree&&safe.Length>0)
                                {candidate=id;safeReadbackTick=decisionTick;TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Stage="actual owner pre-proposal safe-anchor proof",Owner=observation.OwnerId,Tick=decisionTick,
                                    Threats=AiDefensePlanner.Threats(observation,P).Select(t=>t.Id).ToArray(),ArmyId=armyId,Candidate=id,AssignedArmy=assigned?.Id,
                                    State=unit.CurrentOrder,Navigation=unit.NavigationOutcome,PendingFree=pendingFree,SafeAnchors=safe},Newtonsoft.Json.Formatting.Indented));break;}
                            }
                        }
                        if(candidate>0)
                        {
                            var queued=((System.Collections.IEnumerable)Get(owner,"pendingItems")).Cast<object>().FirstOrDefault(x=>(string)Get(x,"PolicyName")==AiDefensePlanner.Policy&&
                                ((PlayableAiAction)Get(x,"Action")).EntityIds.Contains(candidate)&&(((PlayableAiAction)Get(x,"Action")).Kind==PlayableCommandKind.Move||((PlayableAiAction)Get(x,"Action")).Kind==PlayableCommandKind.Follow));
                            if(!lifecycleVerified&&queued!=null)
                            {
                                var pendingAction=(PlayableAiAction)Get(queued,"Action");var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);
                                CollectionAssert.AreEqual(bytes,restored.CaptureBytes(),"scheduled Defense assembly and callback survive exact authority restore");
                                Assert.AreEqual(pendingAction.ActionId,restored.CaptureDiagnosticCheckpoints().Single().PendingActionId);
                                var paused=PlayableAuthorityTick.RestoreBytes(bytes,c);Assert.True(paused.TryAdvance(paused:true));
                                Assert.True(paused.CaptureDiagnosticCheckpoints().Single().Records.Any(r=>r.ActionId==pendingAction.ActionId&&r.Status==PlayableAiDeliveryStatus.Cancelled));paused.Stop();
                                var stopped=PlayableAuthorityTick.RestoreBytes(bytes,c);stopped.Stop();Assert.AreEqual(0,((AiDefensePlanner)Get(Owner(stopped),"defense")).PendingId);
                                restored.Stop();stopped.Stop();lifecycleVerified=true;
                            }
                            var unit=a.ParticipantView("west-owner").Entities.Single(u=>u.Id==candidate);var stamp=unit.OrderStamp;
                            if(stamp!=null&&(stamp.Kind==PlayableCommandKind.Move||stamp.Kind==PlayableCommandKind.Follow))
                                applied=cp.Records.FirstOrDefault(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&
                                    r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.Kind==stamp.Kind&&r.ReceiptIdentity?.OwnerId=="west-owner");
                            if(applied!=null&&appliedTick<0)appliedTick=applied.ApplicationTick;
                            var army=registry.Capture().Armies.SingleOrDefault(x=>x.Id==armyId&&x.TacticalOwner==AiDefensePlanner.Policy);
                            if(army!=null&&army.Members.Contains(candidate)&&!army.Reinforcements.Contains(candidate))joinedTick=a.Tick;
                        }
                    }
                }
                finally
                {
                    PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=null;
                }
                Assert.Greater(candidate,0,"actual owner projection must expose a free-at-activation idle candidate with a current safe FriendlyAnchor proof to the same Defense core");
                var finalOwner=Owner(a);var finalRegistry=(AiArmyRegistry)Get(finalOwner,"armies");var finalArmy=finalRegistry.Capture().Armies.SingleOrDefault(x=>x.Id==armyId);
                var finalUnit=a.ParticipantView("west-owner").Entities.Single(u=>u.Id==candidate);
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Scenario="actual Defense detour terminal state",candidate,armyId,safeReadbackTick,appliedTick,joinedTick,
                    Applied=applied,FinalArmy=finalArmy,ArmyFor=finalRegistry.ArmyFor(candidate),Unit=new{finalUnit.Position,finalUnit.Health,finalUnit.Held,finalUnit.NavigationOutcome,
                        Order=finalUnit.CurrentOrder==null?null:new{finalUnit.CurrentOrder.Kind,finalUnit.CurrentOrder.Destination,finalUnit.CurrentOrder.TargetId,finalUnit.CurrentOrder.IssuedTick},
                        Stamp=finalUnit.OrderStamp==null?null:new{finalUnit.OrderStamp.Kind,finalUnit.OrderStamp.ActionId,finalUnit.OrderStamp.Sequence,finalUnit.OrderStamp.Tick},Queue=finalUnit.Queue},
                    DefenseRecords=a.CaptureDiagnosticCheckpoints().Single().Records.Where(r=>r.Policy==AiDefensePlanner.Policy).Select(r=>new{r.ActionId,r.Kind,r.Status,r.RuntimeStatus,r.DueTick,r.ApplicationTick,r.Message}).ToArray()}));
                Assert.NotNull(applied,"Defense assembly requires an exact Applied Move/Follow receipt; candidateArmy="+finalRegistry.ArmyFor(candidate));
                Assert.AreEqual(PlayableCommandKind.Follow,applied.Kind,"The proven moving-core join must finish through an applied ordinary Follow receipt");
                Assert.GreaterOrEqual(safeReadbackTick,0);Assert.GreaterOrEqual(appliedTick,0);Assert.True(lifecycleVerified,"scheduled Defense assembly must cross restore, pause, and Stop lifecycle");
                Assert.GreaterOrEqual(joinedTick,0,"candidate must physically join the original Defense army");
                Assert.Contains(candidate,finalArmy.Members);Assert.IsFalse(finalArmy.Reinforcements.Contains(candidate));
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Scenario="actual Defense obstacle detour",candidate,armyId,appliedTick,joinedTick,Applied=applied,Army=finalArmy}));
            }
            a.Stop();
        }
        [Test] public void DefenseRecovery_ActualDetourUnsafeFollowStopsAndRetainsReinforcement()
        {
            var c=DefenseDetourConfig();Assert.AreEqual(7,c.ScenarioUnits.Count(u=>u.LogicalPlayer==1));Assert.AreEqual(1,c.ScenarioUnits.Count(u=>u.LogicalPlayer==2));
            Assert.AreEqual(1,c.Obstacles.Count);Assert.AreEqual(-18.5,c.Obstacles[0].MinX);Assert.AreEqual(-17.5,c.Obstacles[0].MaxX);
            Assert.AreEqual(-31,c.Obstacles[0].MinZ);Assert.AreEqual(10,c.Obstacles[0].MaxZ);
            var tankRadius=PlayableUnitRules.Radius(P,PlayableEntityKind.Tank);
            Assert.Less(c.Obstacles[0].MinZ+P.ArenaHalfExtent,2*tankRadius,"The lower obstacle-to-arena gap must be too narrow for a Tank");
            var authoredGeometry=new NavGeometry(P.ArenaHalfExtent,c.Obstacles.ToArray(),1);
            foreach(var unit in c.ScenarioUnits)
                Assert.True(authoredGeometry.IsFree(unit.Position,PlayableUnitRules.Radius(P,unit.Kind)),"Scenario actor must fit the arena and obstacle geometry: "+unit.Position);
            var a=new PlayableAuthorityTick(c,71);var candidates=Array.Empty<int>();var candidate=0;long armyId=0;long appliedTick=-1;long joinedTick=-1;long stopAppliedTick=-1;long safeReadbackTick=-1;bool lifecycleVerified=false;
            PlayableAiTraceRecord applied=null,stopApplied=null;
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<120&&armyId==0;i++)
                {
                    Step(a,host);var owner=Owner(a);var defense=(AiDefensePlanner)Get(owner,"defense");var registry=(AiArmyRegistry)Get(owner,"armies");
                    var view=a.ParticipantView("west-owner");
                    Assert.AreEqual(7,view.Entities.Count(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank&&u.Health>0));
                    Assert.AreEqual(1,view.Entities.Count(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank&&u.Health>0),"threat remains live and visible in HQ vision");
                    if(!defense.Active)continue;
                    armyId=defense.ArmyId;var army=registry.Capture().Armies.Single(x=>x.Id==armyId&&x.TacticalOwner==AiDefensePlanner.Policy);
                    candidates=view.Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank&&u.Health>0&&!u.Held&&registry.ArmyFor(u.Id)==0&&
                            u.CurrentOrder==null&&u.NavigationOutcome==NavigationOutcome.Idle&&u.Queue==null)
                        .Select(u=>u.Id).ToArray();
                    Assert.Greater(candidates.Length,0,"actual Defense activation must leave at least one live idle unclaimed Tank before owner observation updates reinforcement claims");
                }
                Assert.Greater(armyId,0,"ordinary actual threat observation must activate a Defense army");
                Assert.Greater(candidates.Length,0,"capture candidate IDs from the actual registry at Defense activation");
                PlayableAiObservation decisionObservation=null;AiArmyRegistryState decisionArmies=null;PlayableAiAction[] decisionPending=null;long decisionTick=-1;bool ownerGeometryVerified=false;
                PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests="west-owner";
                var safetyTraceCaptured=false;
                PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=(ownerId,tick,observation,armies,pending)=>
                {
                    if(ownerId!="west-owner")return;
                    decisionTick=tick;decisionObservation=observation;decisionArmies=armies;decisionPending=pending;
                    if(!safetyTraceCaptured&&candidate>0)
                    {
                        var observed=observation.Entities.SingleOrDefault(u=>u.Id==candidate&&u.Owner==observation.Owner);
                        var assigned=armies.Armies.SingleOrDefault(x=>x.Members.Contains(candidate)||x.Reinforcements.Contains(candidate));
                        if(observed?.CurrentOrder!=null&&assigned?.Id==armyId&&assigned.Reinforcements.Contains(candidate))
                        {
                            var traceCore=assigned.Members.Where(id=>!assigned.Reinforcements.Contains(id)).Select(id=>observation.Entities.SingleOrDefault(u=>u.Id==id&&u.Health>0)).Where(u=>u!=null).ToArray();
                            var proofs=observation.RouteProofs.Where(r=>r.UnitId==candidate&&r.Owner==observation.Owner&&r.Generation==observation.Generation&&r.Tick==tick&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor).ToArray();
                            var safeFollow=proofs.Where(r=>traceCore.Any(c=>c.Id==r.TargetId&&r.Target.Equals(PlayableUnitRules.FollowGoal(P,c.Position,c.HullHeading))&&r.Goal.Equals(r.Target))&&AiTacticalExecutor.SafeRoute(observation,r,P)).ToArray();
                            var safeCoreMove=proofs.Where(r=>traceCore.Any(c=>c.Id==r.TargetId&&c.Position.Equals(r.Target))&&AiTacticalExecutor.SafeRoute(observation,r,P)).ToArray();
                            var safeHome=proofs.Where(r=>observation.Buildings.Any(b=>b.Owner==observation.Owner&&b.Kind==PlayableBuildingKind.Headquarters&&r.Target.Equals(b.Position))&&AiTacticalExecutor.SafeRoute(observation,r,P)).ToArray();
                            if(safeFollow.Length==0&&safeCoreMove.Length==0&&safeHome.Length==0)
                            {
                                TestContext.WriteLine("DEFENSE_UNSAFE_ASSEMBLY_TRACE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Tick=tick,Candidate=candidate,ArmyId=armyId,Order=observed.CurrentOrder,Stamp=observed.OrderStamp,Core=traceCore.Select(u=>new{u.Id,u.Position,u.HullHeading}),Threats=AiDefensePlanner.Threats(observation,P).Select(t=>new{t.Id,t.Position,t.Kind}).ToArray(),Routes=proofs.Select(r=>new{r.TargetId,r.Target,r.Goal,Path=r.Path.ToArray(),r.GeometryRevision}),SafeFollow=safeFollow.Length,SafeCoreMove=safeCoreMove.Length},Newtonsoft.Json.Formatting.Indented));
                                safetyTraceCaptured=true;
                            }
                        }
                    }
                    if(ownerGeometryVerified||!armies.Armies.Any(x=>x.Id==armyId&&x.TacticalOwner==AiDefensePlanner.Policy&&x.Phase!=AiArmyPhase.Disbanded)||AiDefensePlanner.Threats(observation,P).Length==0)return;
                    var obstacle=c.Obstacles[0];Assert.NotNull(observation.Vision,"Actual owner projection must include its current vision snapshot");
                    var corners=new[]{new NavPoint(obstacle.MinX,obstacle.MinZ),new NavPoint(obstacle.MinX,obstacle.MaxZ),new NavPoint(obstacle.MaxX,obstacle.MinZ),new NavPoint(obstacle.MaxX,obstacle.MaxZ)};
                    var visibleCorners=corners.Select(observation.Vision.IsVisible).ToArray();
                    var defenseArmy=armies.Armies.Single(x=>x.Id==armyId);var core=defenseArmy.Members.Where(id=>!defenseArmy.Reinforcements.Contains(id)).ToHashSet();
                    var threat=AiDefensePlanner.Threats(observation,P).OrderBy(x=>x.Id).First();
                    var threatRoutes=observation.RouteProofs.Where(r=>r.Kind==PlayableRouteTargetKind.VisibleEnemy&&r.TargetId==threat.Id&&core.Contains(r.UnitId)&&r.Tick==observation.Tick&&r.Generation==observation.Generation).ToArray();
                    var detour=threatRoutes.FirstOrDefault(r=>r.Path.Any(point=>point.Z>obstacle.MaxZ));
                    var geometryReadback=new{Tick=tick,Threat=threat.Id,Obstacle=new{obstacle.MinX,obstacle.MinZ,obstacle.MaxX,obstacle.MaxZ},Corners=corners.Select((point,i)=>new{point,Visible=visibleCorners[i]}).ToArray(),Core=core.OrderBy(id=>id).ToArray(),ThreatRoutes=threatRoutes.Select(r=>new{r.UnitId,r.TargetId,r.Goal,Path=r.Path.ToArray()}).ToArray(),DetourFound=detour!=null};
                    TestContext.WriteLine("DEFENSE_DETOUR_OWNER_GEOMETRY "+Newtonsoft.Json.JsonConvert.SerializeObject(geometryReadback,Newtonsoft.Json.Formatting.Indented));
                    Assert.True(visibleCorners.All(x=>x),"Actual owner vision must expose all four authored obstacle corners before the detour assertion");
                    Assert.NotNull(detour,"Actual owner route projection must prove a threat route around the upper obstacle boundary before the join horizon");
                    ownerGeometryVerified=true;
                };
                try
                {
                    for(int i=0;i<690&&joinedTick<0;i++)
                    {
                        Step(a,host);var cp=a.CaptureDiagnosticCheckpoints().Single();var owner=Owner(a);var registry=(AiArmyRegistry)Get(owner,"armies");
                        if(candidate==0&&decisionTick>=0&&decisionObservation!=null&&decisionArmies!=null&&decisionPending!=null&&safeReadbackTick!=decisionTick)
                        {
                            var observation=decisionObservation;var armyState=decisionArmies;
                            var army=armyState.Armies.SingleOrDefault(x=>x.Id==armyId&&x.TacticalOwner==AiDefensePlanner.Policy&&x.Phase!=AiArmyPhase.Disbanded);
                            if(army!=null&&AiDefensePlanner.Threats(observation,P).Length>0)
                            foreach(var id in candidates)
                            {
                                var unit=observation.Entities.SingleOrDefault(u=>u.Id==id&&u.Owner==observation.Owner&&u.Health>0);
                                var assigned=armyState.Armies.SingleOrDefault(x=>x.Members.Contains(id)||x.Reinforcements.Contains(id));
                                bool ownershipOk=assigned==null||assigned.Id==armyId&&assigned.Reinforcements.Contains(id);
                                bool pendingFree=decisionPending.All(x=>!x.EntityIds.Contains(id));
                                var core=army.Members.Where(x=>!army.Reinforcements.Contains(x)).ToHashSet();
                                var safe=unit==null?Array.Empty<PlayableRouteProof>():observation.RouteProofs.Where(r=>r.UnitId==id&&r.Owner==observation.Owner&&
                                        r.Generation==observation.Generation&&r.Tick==observation.Tick&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&core.Contains(r.TargetId))
                                    .Where(r=>AiTacticalExecutor.SafeRoute(observation,r,P)).ToArray();
                                if(unit!=null&&!unit.Held&&unit.CurrentOrder==null&&unit.NavigationOutcome==NavigationOutcome.Idle&&unit.Queue==null&&
                                    AiRosterCatalog.Initial.For(unit.Kind).lineWeight>0&&ownershipOk&&pendingFree&&safe.Length>0)
                                {candidate=id;safeReadbackTick=decisionTick;TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Stage="actual owner pre-proposal safe-anchor proof",Owner=observation.OwnerId,Tick=decisionTick,
                                    Threats=AiDefensePlanner.Threats(observation,P).Select(t=>t.Id).ToArray(),ArmyId=armyId,Candidate=id,AssignedArmy=assigned?.Id,
                                    State=unit.CurrentOrder,Navigation=unit.NavigationOutcome,PendingFree=pendingFree,SafeAnchors=safe},Newtonsoft.Json.Formatting.Indented));break;}
                            }
                        }
                        if(candidate>0)
                        {
                            var queued=((System.Collections.IEnumerable)Get(owner,"pendingItems")).Cast<object>().FirstOrDefault(x=>(string)Get(x,"PolicyName")==AiDefensePlanner.Policy&&
                                ((PlayableAiAction)Get(x,"Action")).EntityIds.Contains(candidate)&&(((PlayableAiAction)Get(x,"Action")).Kind==PlayableCommandKind.Move||((PlayableAiAction)Get(x,"Action")).Kind==PlayableCommandKind.Follow));
                            if(!lifecycleVerified&&queued!=null)
                            {
                                var pendingAction=(PlayableAiAction)Get(queued,"Action");var bytes=a.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(bytes,c);
                                CollectionAssert.AreEqual(bytes,restored.CaptureBytes(),"scheduled Defense assembly and callback survive exact authority restore");
                                Assert.AreEqual(pendingAction.ActionId,restored.CaptureDiagnosticCheckpoints().Single().PendingActionId);
                                var paused=PlayableAuthorityTick.RestoreBytes(bytes,c);Assert.True(paused.TryAdvance(paused:true));
                                Assert.True(paused.CaptureDiagnosticCheckpoints().Single().Records.Any(r=>r.ActionId==pendingAction.ActionId&&r.Status==PlayableAiDeliveryStatus.Cancelled));paused.Stop();
                                var stopped=PlayableAuthorityTick.RestoreBytes(bytes,c);stopped.Stop();Assert.AreEqual(0,((AiDefensePlanner)Get(Owner(stopped),"defense")).PendingId);
                                restored.Stop();stopped.Stop();lifecycleVerified=true;
                            }
                            var unit=a.ParticipantView("west-owner").Entities.Single(u=>u.Id==candidate);var stamp=unit.OrderStamp;
                            if(stamp!=null&&(stamp.Kind==PlayableCommandKind.Move||stamp.Kind==PlayableCommandKind.Follow))
                                applied=cp.Records.FirstOrDefault(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&
                                    r.ActionId==stamp.ActionId&&r.CommandSequence==stamp.Sequence&&r.ApplicationTick==stamp.Tick&&r.Kind==stamp.Kind&&r.ReceiptIdentity?.OwnerId=="west-owner");
                            if(applied!=null&&appliedTick<0)appliedTick=applied.ApplicationTick;
                            var army=registry.Capture().Armies.SingleOrDefault(x=>x.Id==armyId&&x.TacticalOwner==AiDefensePlanner.Policy);
                            if(army!=null&&army.Members.Contains(candidate)&&!army.Reinforcements.Contains(candidate))joinedTick=a.Tick;
                            var stopStamp=unit.OrderStamp;
                            if(stopStamp?.Kind==PlayableCommandKind.Stop)
                                stopApplied=cp.Records.FirstOrDefault(r=>r.Policy==AiDefensePlanner.Policy&&r.Kind==PlayableCommandKind.Stop&&r.Status==PlayableAiDeliveryStatus.Applied&&
                                    r.RuntimeStatus==PlayableCommandStatus.Applied&&r.ActionId==stopStamp.ActionId&&r.CommandSequence==stopStamp.Sequence&&r.ApplicationTick==stopStamp.Tick&&
                                    r.ReceiptIdentity?.OwnerId=="west-owner");
                            if(stopApplied!=null&&stopAppliedTick<0&&army!=null&&army.Reinforcements.Contains(candidate))
                            {stopAppliedTick=stopApplied.ApplicationTick;break;}
                        }
                    }
                }
                finally
                {
                    PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=null;
                }
                Assert.Greater(candidate,0,"actual owner projection must expose a free-at-activation idle candidate with a current safe FriendlyAnchor proof to the same Defense core");
                var finalOwner=Owner(a);var finalRegistry=(AiArmyRegistry)Get(finalOwner,"armies");var finalArmy=finalRegistry.Capture().Armies.SingleOrDefault(x=>x.Id==armyId);
                var finalUnit=a.ParticipantView("west-owner").Entities.Single(u=>u.Id==candidate);
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Scenario="actual Defense detour safety-stop state",candidate,armyId,safeReadbackTick,appliedTick,joinedTick,stopAppliedTick,
                    Applied=applied,FinalArmy=finalArmy,ArmyFor=finalRegistry.ArmyFor(candidate),Unit=new{finalUnit.Position,finalUnit.Health,finalUnit.Held,finalUnit.NavigationOutcome,
                        Order=finalUnit.CurrentOrder==null?null:new{finalUnit.CurrentOrder.Kind,finalUnit.CurrentOrder.Destination,finalUnit.CurrentOrder.TargetId,finalUnit.CurrentOrder.IssuedTick},
                        Stamp=finalUnit.OrderStamp==null?null:new{finalUnit.OrderStamp.Kind,finalUnit.OrderStamp.ActionId,finalUnit.OrderStamp.Sequence,finalUnit.OrderStamp.Tick},Queue=finalUnit.Queue},
                    DefenseRecords=a.CaptureDiagnosticCheckpoints().Single().Records.Where(r=>r.Policy==AiDefensePlanner.Policy).Select(r=>new{r.ActionId,r.Kind,r.Status,r.RuntimeStatus,r.DueTick,r.ApplicationTick,r.Message}).ToArray()}));
                Assert.NotNull(applied,"The actual scenario must first apply an ordinary Defense Follow");
                Assert.AreEqual(PlayableCommandKind.Follow,applied.Kind);
                Assert.NotNull(stopApplied,"When the visible-threat geometry removes every safe continuation, Defense must apply an ordinary Stop");
                Assert.GreaterOrEqual(safeReadbackTick,0);Assert.GreaterOrEqual(appliedTick,0);Assert.True(lifecycleVerified,"scheduled Defense assembly must cross restore, pause, and Stop lifecycle");
                Assert.GreaterOrEqual(stopAppliedTick,0,"Stop must apply while the actor still belongs to the original Defense reinforcement wave");
                Assert.AreEqual(armyId,finalRegistry.ArmyFor(candidate));Assert.Contains(candidate,finalArmy.Reinforcements);
                Assert.AreEqual(0,((AiDefensePlanner)Get(finalOwner,"defense")).PendingId,"Applied safety Stop must release Defense pending state");
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Scenario="actual Defense obstacle-detour safety-stop",candidate,armyId,appliedTick,stopAppliedTick,Applied=applied,Stopped=stopApplied,Army=finalArmy}));
            }
            a.Stop();
        }
        [Test] public void DefenseRecovery_ActualCombatClearsThreatResumesPressureAndKeepsMacroLive()
        {
            var c=DefenseConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owner(a);var d=(AiDefensePlanner)Get(owner,"defense");
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<120&&!d.Active;i++)Step(a,host);Assert.True(d.Active,"Native routes and ordinary arbitration must reserve mobile defense");
                var initial=a.CaptureDiagnosticCheckpoints().Single();Assert.True(initial.Armies.Armies.Any(g=>g.Id==d.ArmyId&&g.Major&&g.Role==AiArmyRole.MobileDefense));
                var scheduledDefense=initial.Records.FirstOrDefault(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Scheduled);
                Assert.NotNull(scheduledDefense,"Initial Defense attack must be scheduled before the pause");
                var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
                var tick=a.Tick;for(int i=0;i<15;i++){Assert.True(a.TryAdvance(paused:true));Assert.True(b.TryAdvance(paused:true));}Assert.AreEqual(tick,a.Tick);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                bool defended=false,macro=false,recovered=false;long clear=-1;long postResumeDecisionTick=-1;
                PlayableAiObservation postResumeObservation=null;AiArmyRegistryState postResumeArmies=null;PlayableAiAction[] postResumePending=null;
                long pendingIdAtFirstPostResumeDecision=-1,pendingIdAtFirstArbitration=-1,firstArbitrationTick=-1;
                string[] firstArbitrationOrder=null;KeyValuePair<string,int>[] firstArbitrationBands=null;KeyValuePair<string,string>[] firstArbitrationRejections=null;
                object firstOwnerDefensePlannerReadback=null;
                PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests="west-owner";
                PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=(ownerId,decisionTick,observation,armies,pending)=>
                {
                    if(ownerId!="west-owner"||postResumeDecisionTick>=0)return;
                    postResumeDecisionTick=decisionTick;postResumeObservation=observation;postResumeArmies=armies;postResumePending=pending;pendingIdAtFirstPostResumeDecision=d.PendingId;
                    var liveArmies=(AiArmyRegistry)Get(Owner(a),"armies");var proposal=d.Propose(observation,P,AiProfile.Initial,liveArmies);
                    firstOwnerDefensePlannerReadback=new{d.Active,d.ArmyId,d.PendingId,TargetId=Get(d,"targetId"),Proposal=proposal==null?null:new{proposal.ActionId,proposal.Kind,Actors=proposal.CopyEntityIds(),proposal.TargetId},
                        Threats=AiDefensePlanner.Threats(observation,P).Select(x=>x.Id).ToArray(),Registry=armies.Armies.SingleOrDefault(x=>x.Id==d.ArmyId)};
                };
                AiDecisionArbiter.SelectionReadbackOwnerFilterForTests="west-owner";
                AiDecisionArbiter.SelectionReadbackForTests=selection=>
                {
                    if(selection.OwnerId!="west-owner"||firstArbitrationOrder!=null)return;
                    firstArbitrationTick=a.Tick;firstArbitrationOrder=selection.Ordered.ToArray();firstArbitrationBands=selection.Bands.OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray();
                    pendingIdAtFirstArbitration=d.PendingId;
                    var arbiter=(AiDecisionArbiter)Get(Owner(a),"arbiter");firstArbitrationRejections=arbiter.Rejections.OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray();
                };
                try
                {
                    for(int i=0;i<1500;i++)
                    {
                        Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                        var checkpoint=a.CaptureDiagnosticCheckpoints().Single();var observation=PlayableAiObservation.From(a.ParticipantView("west-owner"));
                        defended|=checkpoint.Records.Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied);
                        macro|=checkpoint.Records.Any(r=>(r.Policy=="production"||r.Policy=="economy"||r.Policy=="research")&&r.Status==PlayableAiDeliveryStatus.Applied);
                        if(defended&&AiDefensePlanner.Threats(observation,P).Length==0&&clear<0)clear=a.Tick;
                        if(clear>=0&&checkpoint.Records.Any(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied&&r.ApplicationTick>clear)){recovered=true;break;}
                    }
                }
                finally
                {
                    PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=null;
                    AiDecisionArbiter.SelectionReadbackForTests=null;AiDecisionArbiter.SelectionReadbackOwnerFilterForTests=null;
                }
                var postResumeThreats=postResumeObservation==null?Array.Empty<int>():AiDefensePlanner.Threats(postResumeObservation,P).Select(x=>x.Id).ToArray();
                var postResumeDefense=postResumeArmies?.Armies.SingleOrDefault(x=>x.TacticalOwner==AiDefensePlanner.Policy&&x.Phase!=AiArmyPhase.Disbanded);
                var postResumeIdle=postResumeObservation?.Entities.Where(u=>u.Owner==postResumeObservation.Owner&&u.Health>0&&!u.Held&&u.CurrentOrder==null&&u.NavigationOutcome==NavigationOutcome.Idle&&u.Queue==null)
                    .Select(u=>new{u.Id,u.Kind,Army=postResumeArmies.Armies.SingleOrDefault(g=>g.Members.Contains(u.Id)||g.Reinforcements.Contains(u.Id))?.Id,
                        RouteProofs=postResumeObservation.RouteProofs.Count(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.VisibleEnemy)}).ToArray();
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Scenario="A3-03/A3-05 post-resume owner decision",Profile=P.ProfileId,Ai=AiProfile.Initial.Hash,a.Tick,clear,defended,macro,recovered,
                    InitialDefenseScheduled=new{scheduledDefense.ActionId,scheduledDefense.Kind,scheduledDefense.DueTick,scheduledDefense.Status},
                    PostResumeDecisionTick=postResumeDecisionTick,PostResumeThreats=postResumeThreats,PostResumeDefense=postResumeDefense,
                    PostResumePendingIdBeforeProposal=pendingIdAtFirstPostResumeDecision,FirstArbitrationTick=firstArbitrationTick,PendingIdAtArbitration=pendingIdAtFirstArbitration,
                    OwnerDefensePlanner=firstOwnerDefensePlannerReadback,FirstArbitrationOrder=firstArbitrationOrder,FirstArbitrationBands=firstArbitrationBands,FirstArbitrationRejections=firstArbitrationRejections,
                    PostResumeArmyRoutes=postResumeObservation?.RouteProofs.Count(r=>postResumeDefense!=null&&postResumeDefense.Members.Contains(r.UnitId)),
                    PostResumeIdle=postResumeIdle,PostResumePending=postResumePending,Checkpoint=a.CaptureDiagnosticCheckpoints().Single()}));
                Assert.True(defended);Assert.True(macro,"Defense must not stop affordable macro decisions");Assert.GreaterOrEqual(clear,0,"Combat must actually clear the owner-visible threat, with no injected money/health or enemy retreat");Assert.True(recovered,"Pressure must resume after observed threat clearance");
                var data=AiProfile.Initial.CopyData();data.revision++;var ai=new AiProfile(data);Assert.IsNull(a.ApplyProfile(c.Profile,ai));var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());
                a.Stop();b.Stop();restored.Stop();Assert.False(d.Active);var fresh=new PlayableAuthorityTick(c,72);Assert.False(((AiDefensePlanner)Get(Owner(fresh),"defense")).Active);fresh.Stop();
            }
        }
        [Test] public void DefenseRecovery_StrictPrivateDefenseBindingAndLateGenerationCallback()
        {
            var c=DefenseConfig();var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                var d=(AiDefensePlanner)Get(Owner(a),"defense");for(int i=0;i<120&&!d.Active;i++)Step(a,host);Assert.True(d.Active);Assert.Greater(d.PendingId,0);
                var bytes=a.CaptureBytes();Assert.DoesNotThrow(()=>PlayableAuthorityTick.RestoreBytes(bytes,c));
                typeof(AiDefensePlanner).GetField("targetId",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(d,999999);
                Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c),"Defense target and army objective must bind strictly");
                var b=PlayableAuthorityTick.RestoreBytes(bytes,c);var original=(AiDefensePlanner)Get(Owner(b),"defense");var pending=original.PendingId;
                original.ObserveReceipt(new PlayableAiTraceRecord("fixture",pending,0,0,b.Tick,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"foreign generation",ownerId:"west-owner",sourceIdentity:PlayableAiOpeningComposition.SourceIdentity,receiptIdentity:new AiReceiptIdentity(72,"west-owner",1,1)));Assert.AreEqual(pending,original.PendingId);
                var data=AiProfile.Initial.CopyData();data.revision++;var next=new AiProfile(data);Assert.IsNull(b.ApplyProfile(c.Profile,next));Assert.Zero(original.PendingId);Assert.True(original.Active);var restored=PlayableAuthorityTick.RestoreBytes(b.CaptureBytes(),c,next);CollectionAssert.AreEqual(b.CaptureBytes(),restored.CaptureBytes());
                a.Stop();b.Stop();restored.Stop();
            }
        }
        [Test] public void DefenseRecovery_NoForceUsesOneEmergencyAdmissionThenOrdinaryCadence()
        {
            var type=typeof(AiArmyPlanner).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");var loop=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{P,Opening(),71L,null},null);
            void Review(long tick)=>type.GetMethod("Review",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(loop,new object[]{DefenseView(tick,true,0),0L});
            Review(60);var first=(long)Get(loop,"lastDecisionTick");Assert.AreEqual(60,first);
            for(long tick=61;tick<75;tick++)Review(tick);
            Assert.AreEqual(first,(long)Get(loop,"lastDecisionTick"),"Persistent threat with no admissible defense must not become a new emergency decision each tick");
            Assert.IsEmpty(((AiArmyRegistry)Get(loop,"armies")).Capture().Armies);
        }
        private static PlayableAiObservation DefenseForces(int ownCount,int hostileCount,bool spread=false)
        {
            var baseView=DefenseView(60,true,ownCount);var o=PlayableAiObservation.From(baseView);
            var own=o.Entities.Where(u=>u.Owner==o.Owner).Select(u=>new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,new NavPoint(spread?(u.Id<=3?-8:u.Id<=6?0:-20):0,(u.Id-1)*1.5),100,false,0,0,0)).ToArray();
            var hostile=Enumerable.Range(101,hostileCount).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(8,(id-101)*1.5),100,false,0,0,0)).ToArray();
            var routes=own.SelectMany(u=>hostile.Select(e=>new PlayableRouteProof(u.Id,u.Owner,71,60,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.VisibleEnemy,e.Id,e.Position,e.Position,new[]{e.Position}))).ToArray();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,o.Seed,60,60,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,own.Concat(hostile).ToArray(),o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:routes));
        }
        [Test] public void DefenseRecovery_InsufficientForceDoesNotCreateAnUnsupportedDefenseMission()
        {
            var o=DefenseForces(3,4);var r=Registry();var d=new AiDefensePlanner("player-1",71);
            Assert.Less(AiDefensePlanner.Force(o.Entities.Where(u=>u.Owner==o.Owner).ToArray(),P,AiProfile.Initial),AiDefensePlanner.Force(o.Entities.Where(u=>u.Owner!=o.Owner).ToArray(),P,AiProfile.Initial));
            Assert.IsNull(d.Propose(o,P,AiProfile.Initial,r),"A3 requires a sufficient army; no new suicidal fallback is approved");Assert.IsEmpty(r.Capture().Armies);
        }
        [Test] public void DefenseRecovery_NearestSufficientFreeSubsetBeatsDistantExistingArmy()
        {
            var o=DefenseForces(9,1,true);var r=Registry();Assert.True(r.TryCreate(o,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3},out var existing));
            var action=new AiDefensePlanner("player-1",71).Propose(o,P,AiProfile.Initial,r);Assert.NotNull(action);
            CollectionAssert.AreEquivalent(new[]{4,5,6},action.EntityIds,"Nearest force is the actual reserved sufficient subset, not the average position of every free tank");Assert.AreEqual(existing,r.ArmyFor(1));
        }
        [Test] public void DefenseRecovery_AggregatePendingCapacityRoundtripsAcrossReactionLongerThanCadence()
        {
            var basis=DefenseConfig();
            // Capacity=1 cannot admit macro work while survival still owns every slot.
            // Keep the 100-tick/reaction/restore contract, but place the observed threat
            // within the existing defenders' weapon range so actual combat clears it.
            var scenario=basis.ScenarioUnits.Select(u=>u.LogicalPlayer==2?new OfflineScenarioUnit(u.LogicalPlayer,u.Kind,new NavPoint(-12,-4),u.Heading,u.Upgraded):u).ToArray();
            Assert.True(scenario.Where(u=>u.LogicalPlayer==1).Any(u=>AiOpponentSelector.Distance(u.Position,scenario.Single(e=>e.LogicalPlayer==2).Position)<=P.TankRange));
            var c=new OfflineMatchConfiguration(P,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},scenario:scenario,scenarioBuildings:basis.ScenarioBuildings.ToArray());
            var data=AiProfile.Initial.CopyData();data.revision++;
            data.fields.Single(f=>f.path=="difficulty.fighter.reactionSeconds").value=2;
            data.fields.Single(f=>f.path=="difficulty.fighter.actionsPerDecision").value=1;
            data.fields.Single(f=>f.path=="difficulty.recruit.actionsPerDecision").value=1;
            var ai=new AiProfile(data);var a=new PlayableAuthorityTick(c,71,ai);
            using(var host=new UnityHostRouteService())
            {
                bool pending=false,macro=false;
                for(int tick=0;tick<100;tick++)
                {
                    Step(a,host);var owner=Owner(a);var items=((System.Collections.IEnumerable)Get(owner,"pendingItems")).Cast<object>().ToArray();
                    pending|=items.Any(item=>(string)Get(item,"PolicyName")==AiDefensePlanner.Policy);
                    macro|=items.Any(item=>new[]{"economy","production","research","scout"}.Contains((string)Get(item,"PolicyName")));
                    Assert.LessOrEqual(items.Length,1,"Aggregate accepted pending actions must obey the existing C9 capacity, including old actions plus newly selected policies");
                    Assert.DoesNotThrow(()=>{var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());b.Stop();});
                }
                TestContext.WriteLine("Aggregate capacity diagnostic: pendingDefense="+pending+" pendingMacro="+macro+" bank="+a.ParticipantView("west-owner").Credits+" records="+string.Join(" | ",((PlayableAiOwnerLoop)Owner(a)).Checkpoint.Records.Select(x=>x.Policy+":"+x.Status+":"+x.DueTick+":"+x.Message)));
                Assert.True(pending,"actual defense was admitted");Assert.True(macro,"actual affordable macro work was admitted alongside the capacity-bound defense lifecycle");a.Stop();
            }
        }
        private static byte[] LoopBytes(object loop)
        {
            using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream))
            {loop.GetType().GetMethod("WriteState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(loop,new object[]{writer});writer.Flush();return stream.ToArray();}
        }
        private static object RestoreLoop(object loop,byte[] bytes,long tick,AiProfile ai=null)
        {
            using(var stream=new System.IO.MemoryStream(bytes))using(var reader=new System.IO.BinaryReader(stream))return loop.GetType().GetMethod("ReadState",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{reader,P,ai??AiProfile.Initial,71L,Opening().MatchSeed,tick});
        }
        [Test] public void DefenseRecovery_LastMemberLossCancelsPendingAndRestoresImmediately()
        {
            var type=typeof(AiArmyPlanner).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");var loop=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{P,Opening(),71L,null},null);
            void Review(long tick,int count)=>type.GetMethod("Review",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(loop,new object[]{DefenseView(tick,true,count),0L});
            Review(60,6);var defense=(AiDefensePlanner)Get(loop,"defense");Assert.True(defense.Active);Assert.Greater(defense.PendingId,0);
            Assert.DoesNotThrow(()=>RestoreLoop(loop,LoopBytes(loop),60));
            Review(61,0);Assert.False(defense.Active,"Defense must reconcile with same-tick registry membership loss");Assert.Zero(defense.PendingId,"Lost defensive army cannot retain accepted delivery/callback");
            Assert.DoesNotThrow(()=>RestoreLoop(loop,LoopBytes(loop),61));
        }
        [Test] public void DefenseRecovery_ProfileCapCancelsPendingAndDropsOrphanDefenseSynchronously()
        {
            var type=typeof(AiArmyPlanner).Assembly.GetType("Spacewars.Runtime.PlayableAiOwnerLoop");var loop=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{P,Opening(),71L,null},null);
            var r=(AiArmyRegistry)Get(loop,"armies");var o=DefenseForces(9,1,true);Assert.True(r.TryCreate(o,AiArmyRole.Escort,"escort",new[]{1,2,3},out var first));Assert.True(r.TryCreate(o,AiArmyRole.AlliedSupport,"allied-support",new[]{7,8,9},out var second));
            var snapshot=new PlayableSnapshot(P.ProfileId,P.Revision,71,o.Seed,60,60,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,o.Entities.ToArray(),o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:o.RouteProofs.ToArray());
            type.GetMethod("Review",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(loop,new object[]{snapshot,0L});var d=(AiDefensePlanner)Get(loop,"defense");Assert.True(d.Active);Assert.Greater(d.ArmyId,second);Assert.Greater(d.PendingId,0);
            var data=AiProfile.Initial.CopyData();data.revision++;data.fields.Single(f=>f.path=="difficulty.fighter.majorArmies").value=2;var next=new AiProfile(data);
            type.GetMethod("Rebind",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(AiProfile)},null).Invoke(loop,new object[]{next});Assert.False(d.Active);Assert.Zero(d.PendingId);Assert.AreEqual(first,r.ArmyFor(1));Assert.AreEqual(second,r.ArmyFor(7));
            Assert.DoesNotThrow(()=>RestoreLoop(loop,LoopBytes(loop),60,next));
        }
        [TestCase(6)][TestCase(7)][TestCase(8)][TestCase(9)][TestCase(10)][TestCase(11)][TestCase(12)] public void DefenseRecovery_PrivateSchemaRejectsHistoricalVersionsWithoutMutation(int schema)
        {
            var c=DefenseConfig();var a=new PlayableAuthorityTick(c,71);var original=a.CaptureBytes();var state=PlayableWorldState.Decode(original);
            Assert.AreEqual(13,BitConverter.ToInt32(state.AiAuthority,4));Buffer.BlockCopy(BitConverter.GetBytes(schema),0,state.AiAuthority,4,4);
            Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(state.Encode(),c));CollectionAssert.AreEqual(original,a.CaptureBytes());a.Stop();
        }
        [Test] public void DefenseRecovery_ProgressClockUsesObservedMovementAndDamageNotReceipts()
        {
            var r=Registry();var d=new AiDefensePlanner("player-1",71);var o=PlayableAiObservation.From(DefenseView(60,true));var action=d.Propose(o,P,AiProfile.Initial,r);d.Commit(o,action,r);
            d.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Applied,61));d.Observe(PlayableAiObservation.From(DefenseView(61,true)),r,P);Assert.AreEqual(60,r.Capture().Armies.Single(a=>a.Id==d.ArmyId).ProgressTick);
            PlayableAiObservation Changed(long tick,bool movement,bool damage)
            {
                var basis=DefenseView(tick,true);var es=basis.Entities.Select(e=>new PlayableEntitySnapshot(e.Id,e.Owner,e.Kind,movement&&e.Owner==PlayableOwner.Player?new NavPoint(e.Position.X+1,e.Position.Z):e.Position,damage&&e.Owner==PlayableOwner.Enemy?e.Health-1:e.Health,false,0,0,0)).ToArray();
                return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,basis.Seed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,es,basis.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:basis.Vision));
            }
            d.Observe(Changed(62,true,false),r,P);Assert.AreEqual(62,r.Capture().Armies.Single(a=>a.Id==d.ArmyId).ProgressTick);
            d.Observe(Changed(63,true,true),r,P);Assert.AreEqual(63,r.Capture().Armies.Single(a=>a.Id==d.ArmyId).ProgressTick);
            d.Observe(Changed(64,true,true),r,P);Assert.AreEqual(63,r.Capture().Armies.Single(a=>a.Id==d.ArmyId).ProgressTick);
        }
        [Test] public void DefenseRecovery_ActualOrdinaryApproachInvalidatesPendingOffenseBeforeDueTick()
        {
            var basis=Config(6);var scenario=Enumerable.Range(0,6).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-12+(i%3)*2,-8-(i/3)*3))).Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(0,0),Math.PI)}).ToArray();
            var c=new OfflineMatchConfiguration(P,"a3-pending-approach","a3-authored-ordinary-approach","UnityHostRouteService",basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario,scenarioBuildings:basis.ScenarioBuildings.ToArray());var a=new PlayableAuthorityTick(c,71);
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<45;i++)Step(a,host);var checkpoint=a.CaptureDiagnosticCheckpoints().Single();var pending=checkpoint.Records.Single(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Scheduled);var firstArmy=checkpoint.ArmyMission.ArmyId;
                var enemyView=a.ParticipantView("east-owner");var enemy=enemyView.Entities.Single(e=>e.Owner==enemyView.Owner&&e.Kind==PlayableEntityKind.Tank);
                Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,500,"east-owner",PlayableCommandKind.Move,new[]{enemy.Id},new NavPoint(-4,0))).Status);
                long cancelled=-1;
                while(a.Tick<100)
                {
                    Step(a,host);checkpoint=a.CaptureDiagnosticCheckpoints().Single();if(cancelled<0&&checkpoint.Records.Any(r=>r.ActionId==pending.ActionId&&r.Status==PlayableAiDeliveryStatus.Cancelled))cancelled=a.Tick;
                    var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());restored.Stop();
                }
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{Scenario="A3 accepted-offense preemption",EnemyMoveTick=45,EnemyMoveTarget=new NavPoint(-4,0),pending.DueTick,cancelled,Checkpoint=checkpoint}));
                Assert.GreaterOrEqual(cancelled,0);Assert.Less(cancelled,pending.DueTick,"Owner-visible ordinary approach must invalidate stale offensive delivery before its old due tick");Assert.False(checkpoint.Records.Any(r=>r.ActionId==pending.ActionId&&r.Status==PlayableAiDeliveryStatus.Applied));
                Assert.True(checkpoint.Armies.Armies.Any(g=>g.Id==firstArmy&&g.Role==AiArmyRole.MobileDefense&&g.TacticalOwner==AiDefensePlanner.Policy));a.Stop();
            }
        }
    }
}

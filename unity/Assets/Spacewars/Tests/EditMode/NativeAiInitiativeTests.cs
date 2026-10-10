using System;
using System.Linq;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiInitiativeTests
    {
        private static readonly PlayableProfile P=PlayableProfile.Default;
        private static PlayableAiOpeningCompositionState Opening(PlayableAiOpening kind=PlayableAiOpening.Safe)=>kind==PlayableAiOpening.DoubleMineExplorerRush?PlayableAiOpeningComposition.Initialize(1,"player-1",forcedOpening:kind):Enumerable.Range(1,10000).Select(i=>PlayableAiOpeningComposition.Initialize(i,"player-1",aiProfile:AiProfile.Initial)).First(o=>o.Opening==kind);
        private static PlayableAiObservation Observation(long tick=10,int target=101,double x=0,int health=100,bool proof=true,bool commitments=true,PlayableTacticalOrderKind? order=null,int count=3,int orderTarget=0,bool visiblePoint=true,PlayableAiOpening openingKind=PlayableAiOpening.Safe,int explorers=0,int mines=0)
        {
            var opening=Opening(openingKind);var point=new NavPoint(8,0);
            var units=Enumerable.Range(1,count).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(x,i*.1),100,false,order.HasValue?target:0,0,0,order.HasValue?new PlayableTacticalOrderSnapshot(i,PlayableOwner.Player,71,1,10,order.Value,point,orderTarget>0?orderTarget:target):null))
                .Concat(Enumerable.Range(201,explorers).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Player,PlayableEntityKind.Explorer,new NavPoint(-2,0),100,false,0,0,0)))
                .Concat(target==0?Array.Empty<PlayableEntitySnapshot>():new[]{new PlayableEntitySnapshot(target,PlayableOwner.Enemy,PlayableEntityKind.Tank,point,health,false,0,0,0)}).ToArray();
            var buildings=commitments?new[]{new PlayableBuildingSnapshot(50,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(-10,0),100,1,0,0,default),new PlayableBuildingSnapshot(51,PlayableOwner.Player,PlayableBuildingKind.Refinery,new NavPoint(-15,0),100,1,0,0,default)}:Array.Empty<PlayableBuildingSnapshot>();
            buildings=buildings.Concat(Enumerable.Range(301,mines).Select(i=>new PlayableBuildingSnapshot(i,PlayableOwner.Player,PlayableBuildingKind.Mine,new NavPoint(-20,i),100,1,0,0,default))).ToArray();
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(new NavPoint(0,0),visiblePoint?50:3)},Array.Empty<KnownBuilding>());
            var routes=proof&&target!=0?units.Where(u=>u.Owner==PlayableOwner.Player).Select(u=>new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,P.TankCollisionRadius,PlayableRouteTargetKind.VisibleEnemy,target,point,point,new[]{point})).ToArray():Array.Empty<PlayableRouteProof>();
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,opening.MatchSeed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:routes));
        }
        private static AiArmyRegistry Registry()=>new AiArmyRegistry("player-1",71,AiProfile.Initial,AiDifficulty.Fighter);
        private static PlayableAiAction Plan(AiArmyPlanner p,AiArmyRegistry r,PlayableAiObservation o)=>p.Propose(o,Opening(),P,AiProfile.Initial,r);
        private static PlayableAiTraceRecord Receipt(PlayableAiAction a,PlayableAiDeliveryStatus status,long tick)=>new PlayableAiTraceRecord("fixture",a.ActionId,tick,1,tick,status,PlayableCommandStatus.Applied,"fixture",ownerId:a.PlayerId,sourceIdentity:a.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,a.PlayerId,1,1));
        [Test] public void SafePressure_RequiresCommitmentsViableForceAndActualPermittedRoutes()
        {
            var p=new AiArmyPlanner("player-1",71);var r=Registry();
            Assert.IsNull(Plan(p,r,Observation(commitments:false)));Assert.IsNull(Plan(p,r,Observation(count:2)));Assert.IsNull(Plan(p,r,Observation(proof:false)));
            var o=Observation();var a=Plan(p,r,o);Assert.NotNull(a);Assert.AreEqual(PlayableCommandKind.Attack,a.Kind);
            Assert.IsEmpty(r.Capture().Armies);Assert.Zero(p.Capture().ArmyId,"Proposals must not claim members or pending state");
            p.Commit(o,a,r);Assert.Greater(p.Capture().ArmyId,0);Assert.AreEqual(1,r.Capture().Armies.Count(x=>x.Major));
        }
        [Test] public void Retask_LostKnownTargetReleasesRecipientsAndSelectsNextUsefulPlan()
        {
            var p=new AiArmyPlanner("player-1",71);var r=Registry();var o=Observation();var a=Plan(p,r,o);p.Commit(o,a,r);p.ObserveReceipt(Receipt(a,PlayableAiDeliveryStatus.Applied,11));var first=p.Capture().ArmyId;
            p.Observe(Observation(12,target:102,order:PlayableTacticalOrderKind.Attack),r,AiProfile.Initial);
            Assert.False(p.Active);Assert.True(new[]{1,2,3}.All(id=>r.ArmyFor(id)==0));StringAssert.Contains("whereabouts unconfirmed",p.Capture().Reason);Assert.False(p.Capture().TargetVisible);
            var next=Observation(13,target:102,order:PlayableTacticalOrderKind.Attack,orderTarget:101);var b=Plan(p,r,next);Assert.NotNull(b);Assert.AreEqual(102,b.TargetId);p.Commit(next,b,r);Assert.Greater(p.Capture().ArmyId,first);
        }
        [Test] public void Retask_ReceiptsAndRepeatedOrdersDoNotResetProgressClock()
        {
            var p=new AiArmyPlanner("player-1",71);var r=Registry();var o=Observation();var a=Plan(p,r,o);p.Commit(o,a,r);
            p.ObserveReceipt(Receipt(a,PlayableAiDeliveryStatus.Accepted,11));p.Observe(Observation(11),r,AiProfile.Initial);Assert.AreEqual(AiArmyPhase.Staging,p.Capture().Phase);Assert.AreEqual(10,p.Capture().ProgressTick);
            p.ObserveReceipt(Receipt(a,PlayableAiDeliveryStatus.Applied,12));p.Observe(Observation(12,order:PlayableTacticalOrderKind.Attack),r,AiProfile.Initial);Assert.AreEqual(AiArmyPhase.Engaging,p.Capture().Phase);Assert.AreEqual(10,p.Capture().ProgressTick);
            Assert.IsNull(Plan(p,r,Observation(13,order:PlayableTacticalOrderKind.Attack)));
            p.Observe(Observation(14,x:1,order:PlayableTacticalOrderKind.Attack),r,AiProfile.Initial);Assert.AreEqual(14,p.Capture().ProgressTick);
            p.Observe(Observation(15,x:1,health:90,order:PlayableTacticalOrderKind.Attack),r,AiProfile.Initial);Assert.AreEqual(15,p.Capture().ProgressTick);
            p.Observe(Observation(916,x:1,health:90,order:PlayableTacticalOrderKind.Attack),r,AiProfile.Initial);Assert.False(p.Active);Assert.Zero(r.ArmyFor(1));
            Assert.IsNull(Plan(p,r,Observation(917)));Assert.NotNull(Plan(p,r,Observation(917,target:102)));
        }
        [Test] public void Retask_PhasesAndClockSupportDoesNotImplyFutureRetreatPolicy()
        {
            var p=new AiArmyPlanner("player-1",71);var r=Registry();var o=Observation();p.Commit(o,Plan(p,r,o),r);
            foreach(var phase in new[]{AiArmyPhase.Staging,AiArmyPhase.Advancing,AiArmyPhase.Engaging,AiArmyPhase.Regrouping,AiArmyPhase.Retreating,AiArmyPhase.Recovering})
            {Assert.True(p.SetPhase(r,phase,11));Assert.AreEqual(phase,r.Capture().Armies.Single().Phase);Assert.AreEqual(10,p.Capture().ProgressTick);}
            Assert.IsNull(Plan(p,r,Observation(12)));Assert.False(p.SetPhase(r,AiArmyPhase.Advancing,9));
        }
        [Test] public void Retask_UnconfirmedContactDeadlineReleasesForAnotherUsefulTarget()
        {
            var p=new AiArmyPlanner("player-1",71);var r=Registry();var first=Observation();var action=Plan(p,r,first);p.Commit(first,action,r);p.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Applied,11));
            p.Observe(Observation(12,target:0,visiblePoint:false),r,AiProfile.Initial);Assert.True(p.Active);Assert.False(p.Capture().TargetVisible);Assert.AreEqual(101,p.Capture().TargetId);Assert.AreEqual(10,p.Capture().LastSeenTick);Assert.AreEqual(10,p.Capture().ProgressTick);
            p.Observe(Observation(910,target:0,visiblePoint:false),r,AiProfile.Initial);Assert.False(p.Active);StringAssert.Contains("unconfirmed contact recheck deadline",p.Capture().Reason);Assert.True(new[]{1,2,3}.All(id=>r.ArmyFor(id)==0));
            var next=Plan(p,r,Observation(911,target:102));Assert.NotNull(next);Assert.AreEqual(102,next.TargetId);
        }
        [TestCase(PlayableAiOpening.BlindRush,0,0)][TestCase(PlayableAiOpening.ExplorerAllIn,4,0)][TestCase(PlayableAiOpening.DoubleMineExplorerRush,5,2)]
        public void DeploymentBridge_RequiresAppliedEffectiveOrderAndUnchangedRushUnitBuildingPredicates(PlayableAiOpening kind,int explorers,int mines)
        {
            var p=new AiArmyPlanner("player-1",71);var r=Registry();var opening=Opening(kind);var o=Observation(openingKind:kind,explorers:explorers,mines:mines);
            var action=p.Propose(o,opening,P,AiProfile.Initial,r);Assert.NotNull(action);p.Commit(o,action,r);
            Assert.IsNull(p.Deployment(o,r),"Selected/pending is not deployment");p.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Accepted,11));Assert.IsNull(p.Deployment(o,r));
            p.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Rejected,11));p.Observe(Observation(11,openingKind:kind,explorers:explorers,mines:mines,order:PlayableTacticalOrderKind.Attack),r,AiProfile.Initial);Assert.IsNull(p.Deployment(o,r),"An effective order without an Applied receipt cannot prove deployment");
            var retry=p.Propose(Observation(12,openingKind:kind,explorers:explorers,mines:mines),opening,P,AiProfile.Initial,r);Assert.NotNull(retry);p.Commit(Observation(12,openingKind:kind,explorers:explorers,mines:mines),retry,r);p.ObserveReceipt(Receipt(retry,PlayableAiDeliveryStatus.Applied,13));
            var noOrder=Observation(13,openingKind:kind,explorers:explorers,mines:mines);p.Observe(noOrder,r,AiProfile.Initial);Assert.IsNull(p.Deployment(noOrder,r),"Applied alone does not prove an effective order");
            var effective=Observation(14,openingKind:kind,explorers:explorers,mines:mines,order:PlayableTacticalOrderKind.Attack);p.Observe(effective,r,AiProfile.Initial);var deployed=p.Deployment(effective,r);Assert.NotNull(deployed);
            Assert.AreEqual(PlayableAiOpeningPhase.Complete,new PlayableAiMidgameStrategyPolicy(P,"player-1").Review(effective,opening,nativeDeployment:deployed).Phase);
            Assert.Throws<ArgumentException>(()=>new PlayableAiMidgameStrategyPolicy(P,"player-1").Review(Observation(15,openingKind:kind),opening,nativeDeployment:deployed),"Stale deployment observation must reject");
            if(explorers>0){var shortForce=Observation(14,openingKind:kind,explorers:explorers-1,mines:mines,order:PlayableTacticalOrderKind.Attack);Assert.AreEqual(PlayableAiOpeningPhase.Active,new PlayableAiMidgameStrategyPolicy(P,"player-1").Review(shortForce,opening,nativeDeployment:p.Deployment(shortForce,r)).Phase);}
            if(mines>0){var shortEconomy=Observation(14,openingKind:kind,explorers:explorers,mines:mines-1,order:PlayableTacticalOrderKind.Attack);Assert.AreEqual(PlayableAiOpeningPhase.Active,new PlayableAiMidgameStrategyPolicy(P,"player-1").Review(shortEconomy,opening,nativeDeployment:p.Deployment(shortEconomy,r)).Phase);}
            var occluded=Observation(15,target:0,visiblePoint:false,openingKind:kind,explorers:explorers,mines:mines);p.Observe(occluded,r,AiProfile.Initial);Assert.True(p.Active);Assert.IsNull(p.Deployment(occluded,r),"Occluded contact cannot supply a live deployment observation");
        }
        private static object Get(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(o);
        private static object Owner(PlayableAuthorityTick a)=>((IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single();
        private static void Step(PlayableAuthorityTick a,UnityHostRouteService host)
        {for(int i=0;!a.TryAdvance();i++){Assert.Less(i,4096);host.Service(a,64);}}
        private static OfflineMatchConfiguration Config(int tanks=4,bool hidden=false,bool absent=false,PlayableAiOpening openingKind=PlayableAiOpening.Safe,bool enemyBuilding=false,bool blocked=false)
        {
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-24,6),pin:1),new OfflineStart("east",2,new NavPoint(24,24),new NavPoint(18,24),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,new[]{new TerritorySlot(1,new NavPoint(-24,-9),0),new TerritorySlot(2,new NavPoint(-14,0),0)}),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>())}.Concat(enemyBuilding?new[]{new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(8,-8),Array.Empty<TerritorySlot>())}:Array.Empty<TerritorySite>()).ToArray();
            var roster=new[]{new OfflineParticipant("west-owner",1,1,OfflineControl.Ai),new OfflineParticipant("east-owner",2,2,OfflineControl.Human)};
            var scenario=Enumerable.Range(0,tanks).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,enemyBuilding?new NavPoint(-7+(i%4)*1.5,-8):hidden||absent?new NavPoint(-20+(i%5)*2,-20-(i/5)*3):new NavPoint(-12+(i%5)*2,-8-(i/5)*3))).Concat(absent||enemyBuilding?Array.Empty<OfflineScenarioUnit>():new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,hidden?new NavPoint(28,-20):new NavPoint(6,-8))}).ToArray();
            return new OfflineMatchConfiguration(P,"a2-initiative","a2-visible-pressure","UnityHostRouteService",Enumerable.Range(1,10000).First(seed=>PlayableAiOpeningComposition.Initialize(seed,"west-owner").Opening==openingKind),roster,starts,sites,blocked?new[]{new NavObstacle(-1,-32,1,32)}:Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:scenario,scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Factory,1,1,new NavPoint(-24,-9)),new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,2,new NavPoint(-14,0))}.Concat(enemyBuilding?new[]{new OfflineScenarioBuilding(2,PlayableBuildingKind.Outpost,3,0,new NavPoint(8,-8))}:Array.Empty<OfflineScenarioBuilding>()).ToArray());
        }
        [Test] public void Retask_ActualHiddenAliveAndAbsentWorldsRetainEquivalentRememberedContact()
        {
            // Separate actual genesis worlds provide a hidden-alive/absent control.
            // Neither truth is fed to planners; both import the same actually seen target.
            var seen=new PlayableAuthorityTick(Config(),71);var alive=new PlayableAuthorityTick(Config(hidden:true),71);var absent=new PlayableAuthorityTick(Config(absent:true),71);
            PlayableSnapshot Project(PlayableAuthorityTick a)=>(PlayableSnapshot)Get(a,"domain").GetType().GetMethod("PlayerSnapshotForAi",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Get(a,"domain"),new object[]{0L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,Config().Seed,PlayableOwner.Player,true});
            var visible=PlayableAiObservation.From(Project(seen));var hidden=PlayableAiObservation.From(Project(alive));var missing=PlayableAiObservation.From(Project(absent));
            Assert.True(alive.ParticipantView("east-owner").Entities.Any(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank));Assert.False(absent.ParticipantView("east-owner").Entities.Any(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank));
            Assert.AreEqual(Newtonsoft.Json.JsonConvert.SerializeObject(hidden),Newtonsoft.Json.JsonConvert.SerializeObject(missing));
            var opening=PlayableAiOpeningComposition.Initialize(Config().Seed,"west-owner");
            var states=new System.Collections.Generic.List<AiArmyMissionState>();
            foreach(var o in new[]{hidden,missing})
            {
                var r=new AiArmyRegistry("west-owner",71,AiProfile.Initial,AiDifficulty.Fighter);var planner=new AiArmyPlanner("west-owner",71);
                var action=planner.Propose(visible,opening,P,AiProfile.Initial,r);Assert.NotNull(action);planner.Commit(visible,action,r);planner.ObserveReceipt(Receipt(action,PlayableAiDeliveryStatus.Applied,0));
                var before=planner.Capture();planner.Observe(o,r,AiProfile.Initial);
                Assert.True(planner.Active);Assert.AreEqual(AiArmyPhase.Regrouping,planner.Capture().Phase);Assert.False(planner.Capture().TargetVisible);Assert.AreEqual(before.Target,planner.Capture().Target);Assert.AreEqual(before.ProgressTick,planner.Capture().ProgressTick);Assert.AreEqual(before.LastSeenTick,planner.Capture().LastSeenTick);
                Assert.IsNull(planner.Propose(o,opening,P,AiProfile.Initial,r),"Occlusion must not manufacture an Attack");states.Add(planner.Capture());
                planner.Observe(visible,r,AiProfile.Initial);Assert.True(planner.Capture().TargetVisible);Assert.True(planner.Active);Assert.AreEqual(before.ArmyId,planner.Capture().ArmyId);
            }
            Assert.AreEqual(Newtonsoft.Json.JsonConvert.SerializeObject(states[0]),Newtonsoft.Json.JsonConvert.SerializeObject(states[1]));seen.Stop();alive.Stop();absent.Stop();
        }
        [Test] public void DeploymentBridge_RejectsSameOwnerRegistryFromAnotherGeneration()
        {
            var planner=new AiArmyPlanner("player-1",71);var r=Registry();var o=Observation();var a=Plan(planner,r,o);planner.Commit(o,a,r);planner.ObserveReceipt(Receipt(a,PlayableAiDeliveryStatus.Applied,11));var effective=Observation(12,order:PlayableTacticalOrderKind.Attack);planner.Observe(effective,r,AiProfile.Initial);Assert.NotNull(planner.Deployment(effective,r));
            var dto=r.Capture();dto.Generation=72;var foreign=AiArmyRegistry.Restore(dto,AiProfile.Initial,AiDifficulty.Fighter,12);Assert.IsNull(planner.Deployment(effective,foreign),"A cloned different generation registry cannot certify current deployment");
        }
        [TestCase(false)][TestCase(true)] public void SafePressure_ActualBuildingOnlyTargetUsesFreeApproachAndOrdinaryNativeCommand(bool blocked)
        {
            var c=Config(enemyBuilding:true,blocked:blocked);var a=new PlayableAuthorityTick(c,71);var domain=Get(a,"domain");var owner=Owner(a);var planner=(AiArmyPlanner)Get(owner,"armyPlanner");var v=a.ParticipantView("west-owner");var target=v.Buildings.Single(b=>b.Owner==PlayableOwner.Enemy);var unit=v.Entities.First(e=>e.Owner==v.Owner&&e.Kind==PlayableEntityKind.Tank);
            Assert.False(a.NavigationGeometry.IsFree(target.Position,P.TankCollisionRadius),"Actual building center must be solid");
            var arguments=new object[]{unit.Id,unit.Position,target.Position,default(NavPoint)};Assert.True((bool)domain.GetType().GetMethod("TryAttackApproach",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,arguments));var approach=(NavPoint)arguments[3];Assert.True(a.NavigationGeometry.IsFree(approach,P.TankCollisionRadius));Assert.False(approach.Equals(target.Position));
            var projection=(PlayableSnapshot)domain.GetType().GetMethod("PlayerSnapshotForAi",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(domain,new object[]{0L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player,true});var proofs=PlayableAiObservation.From(projection).RouteProofs.Where(r=>r.TargetId==target.Id&&r.Kind==PlayableRouteTargetKind.VisibleEnemy).ToArray();
            Assert.AreEqual(blocked?0:4,proofs.Length,"Reachability is certified to a free approach, not occupied center");
            foreach(var proof in proofs){Assert.AreEqual(target.Position,proof.Target);Assert.False(proof.Goal.Equals(target.Position));Assert.True(a.NavigationGeometry.IsFree(proof.Goal,proof.Radius));}
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<180;i++)Step(a,host);
                var check=a.CaptureDiagnosticCheckpoints().Single();
                if(blocked)TestContext.WriteLine("O3_BLOCKED_DIAGNOSTIC "+Newtonsoft.Json.JsonConvert.SerializeObject(new{Tick=a.Tick,Mission=planner.Capture(),AppliedArmy=check.Records.Where(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied).ToArray()}));
                if(blocked){Assert.False(planner.Active);Assert.False(check.Records.Any(x=>x.Policy==AiArmyPlanner.Policy&&x.Status==PlayableAiDeliveryStatus.Applied));}
                else{Assert.True(check.Records.Any(x=>x.Policy==AiArmyPlanner.Policy&&x.Status==PlayableAiDeliveryStatus.Applied));Assert.True(a.ParticipantView("west-owner").Entities.Any(u=>u.Owner==v.Owner&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==target.Id));Assert.Greater(planner.Capture().ProgressTick,planner.Capture().StartedTick);}
                a.Stop();
            }
        }
        [TestCase(false)][TestCase(true)] public void C9_PrivateTargetCorruptionRejectsRegistryMismatch(bool point)
        {
            var c=Config();var a=new PlayableAuthorityTick(c,71);var planner=(AiArmyPlanner)Get(Owner(a),"armyPlanner");
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<240&&!planner.Active;i++)Step(a,host);Assert.True(planner.Active);
                var original=a.CaptureBytes();Assert.DoesNotThrow(()=>PlayableAuthorityTick.RestoreBytes(original,c));
                var state=(AiArmyMissionState)Get(planner,"state");if(point)state.Target=new NavPoint(state.Target.X+1,state.Target.Z);else state.TargetId+=100;
                var corrupt=a.CaptureBytes();Assert.False(original.SequenceEqual(corrupt));
                Assert.Throws<ArgumentException>(()=>PlayableAuthorityTick.RestoreBytes(corrupt,c),"Planner target and registry metadata must bind strictly");a.Stop();
            }
        }
        [Test] public void C9_ProfileCapDisbandImmediatelyRestoresWithoutOrphanMission()
        {
            var c=Config(10);var a=new PlayableAuthorityTick(c,71);var owner=Owner(a);var r=(AiArmyRegistry)Get(owner,"armies");var planner=(AiArmyPlanner)Get(owner,"armyPlanner");
            var observation=PlayableAiObservation.From(a.ParticipantView("west-owner"));var ids=observation.Entities.Where(e=>e.Owner==observation.Owner&&e.Kind==PlayableEntityKind.Tank).OrderBy(e=>e.Id).Select(e=>e.Id).ToArray();
            Assert.True(r.TryCreate(observation,AiArmyRole.Escort,"future-escort",ids.Take(3),out var first));Assert.True(r.TryCreate(observation,AiArmyRole.AlliedSupport,"future-support",ids.Skip(3).Take(3),out var second));
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<240&&!planner.Active;i++)Step(a,host);Assert.True(planner.Active);Assert.Greater(planner.Capture().ArmyId,second);
                var data=AiProfile.Initial.CopyData();data.revision++;data.fields.Single(f=>f.path=="difficulty.fighter.majorArmies").value=2;var next=new AiProfile(data);
                Assert.IsNull(a.ApplyProfile(c.Profile,next));Assert.AreEqual(first,r.ArmyFor(ids[0]));Assert.AreEqual(second,r.ArmyFor(ids[3]));
                var bytes=a.CaptureBytes();Assert.DoesNotThrow(()=>{var restored=PlayableAuthorityTick.RestoreBytes(bytes,c,next);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());restored.Stop();});Assert.False(planner.Active);a.Stop();
            }
        }
        [TestCase(PlayableAiOpening.Safe)][TestCase(PlayableAiOpening.BlindRush)] public void SafePressure_ActualAuthorityPendingRoundtripPauseRestartAndProfileBarrier(PlayableAiOpening kind)
        {
            var c=Config(openingKind:kind);var a=new PlayableAuthorityTick(c,71);var owner=Owner(a);var r=(AiArmyRegistry)Get(owner,"armies");var p=(AiArmyPlanner)Get(owner,"armyPlanner");
            using(var host=new UnityHostRouteService())
            {
                for(int i=0;i<240&&!p.Active;i++)Step(a,host);
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new {Tick=a.Tick,Checkpoints=a.CaptureDiagnosticCheckpoints(),Entities=a.ParticipantView("west-owner").Entities,Proposals=((AiDecisionArbiter)Get(owner,"arbiter")).Rejections}));
                Assert.True(p.Active,"Actual owner loop must select pressure through common arbiter");Assert.Greater((long)Get(p,"pendingId"),0);
                var bytes=a.CaptureBytes();var b=PlayableAuthorityTick.RestoreBytes(bytes,c);CollectionAssert.AreEqual(bytes,b.CaptureBytes());
                var clock=p.Capture().ProgressTick;for(int i=0;i<30;i++){Assert.True(a.TryAdvance(paused:true));Assert.True(b.TryAdvance(paused:true));CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}Assert.AreEqual(clock,p.Capture().ProgressTick);
                for(int i=0;i<100;i++){Step(a,host);Step(b,host);CollectionAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                Assert.True(a.CaptureDiagnosticCheckpoints().Single().Records.Any(x=>x.Policy==AiArmyPlanner.Policy&&x.Status==PlayableAiDeliveryStatus.Applied));
                if(kind==PlayableAiOpening.BlindRush)Assert.AreEqual(PlayableAiOpeningPhase.Complete,a.CaptureDiagnosticCheckpoints().Single().Strategy.Phase,"Live native deployment must supply existing rush milestone after pause/restore");
                var d=AiProfile.Initial.CopyData();d.revision++;var ai=new AiProfile(d);Assert.IsNull(a.ApplyProfile(c.Profile,ai));var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,ai);CollectionAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());
                a.Stop();Assert.False(p.Active);Assert.True(r.Capture().Armies.All(x=>x.Phase==AiArmyPhase.Disbanded));var fresh=new PlayableAuthorityTick(c,72);Assert.False(((AiArmyPlanner)Get(Owner(fresh),"armyPlanner")).Active);fresh.Stop();b.Stop();restored.Stop();
            }
        }
    }
}

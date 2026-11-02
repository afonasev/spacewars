using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Presentation;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed partial class NativeAiArmyTests
    {
        private static PlayableAiOpeningCompositionState A4Opening()=>Enumerable.Range(1,10000).Select(seed=>PlayableAiOpeningComposition.Initialize(seed,"player-1",aiProfile:AiProfile.Initial)).First(o=>o.Opening==PlayableAiOpening.Safe);
        private static PlayableAiObservation A4Observation(long tick=10,bool wave=false,bool danger=false,bool route=true,bool near=false,bool joined=false,bool gun=false,bool effective=true)
        {
            var p=PlayableProfile.Default;var opening=A4Opening();var target=new NavPoint(20,0);
            var entities=Enumerable.Range(1,3).Select(id=>new PlayableEntitySnapshot(id,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(0,(id-1)*2),100,false,0,0,0,
                effective?new PlayableTacticalOrderSnapshot(id,PlayableOwner.Player,71,1,0,PlayableTacticalOrderKind.Attack,default,101):null)).ToList();
            if(wave)entities.Add(new PlayableEntitySnapshot(4,PlayableOwner.Player,gun?PlayableEntityKind.Shkval:PlayableEntityKind.Tank,new NavPoint(near?-2:-16,0),100,false,0,0,0,
                joined?new PlayableTacticalOrderSnapshot(4,PlayableOwner.Player,71,2,0,PlayableTacticalOrderKind.Attack,default,101):null));
            entities.Add(new PlayableEntitySnapshot(101,PlayableOwner.Enemy,PlayableEntityKind.Tank,target,100,false,0,0,0));
            if(danger)entities.Add(new PlayableEntitySnapshot(102,PlayableOwner.Enemy,PlayableEntityKind.Tank,new NavPoint(-8,0),100,false,0,0,0));
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(default,50)},Array.Empty<KnownBuilding>());
            var proofs=entities.Where(u=>u.Owner==PlayableOwner.Player).Select(u=>new PlayableRouteProof(u.Id,u.Owner,71,tick,1,u.Position,PlayableUnitRules.Radius(p,u.Kind),PlayableRouteTargetKind.VisibleEnemy,101,target,target,new[]{target})).ToList();
            if(wave&&route){var u=entities.Single(e=>e.Id==4);proofs.Add(new PlayableRouteProof(4,u.Owner,71,tick,1,u.Position,PlayableUnitRules.Radius(p,u.Kind),PlayableRouteTargetKind.FriendlyAnchor,1,default,default,near?new[]{default(NavPoint)}:new[]{new NavPoint(-5,0),default(NavPoint)}));}
            var buildings=new[]{new PlayableBuildingSnapshot(50,PlayableOwner.Player,PlayableBuildingKind.Factory,new NavPoint(-30,0),100,1,0,0,default),new PlayableBuildingSnapshot(51,PlayableOwner.Player,PlayableBuildingKind.Refinery,new NavPoint(-30,8),100,1,0,0,default)};
            return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,opening.MatchSeed,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities.ToArray(),buildings,Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:proofs.ToArray()));
        }
        private static bool AssemblyMatches(PlayableAiObservation o,AiArmyState army,PlayableEntitySnapshot unit,System.Collections.Generic.IEnumerable<PlayableAiTraceRecord> records)
            =>(bool)typeof(AiArmyPlanner).GetMethod("AssemblyFollow",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{o,army,unit,records});
        private static AiArmyPlanner A4Mission(AiArmyRegistry registry)
        {
            var planner=new AiArmyPlanner("player-1",71);var o=A4Observation(effective:false);
            var action=planner.Propose(o,A4Opening(),PlayableProfile.Default,AiProfile.Initial,registry);Assert.NotNull(action);planner.Commit(o,action,registry);
            planner.ObserveReceipt(new PlayableAiTraceRecord(o.Identity,action.ActionId,10,1,10,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"actual fixture receipt",ownerId:o.OwnerId,sourceIdentity:action.SourceIdentity,receiptIdentity:new AiReceiptIdentity(71,o.OwnerId,1,1)));
            planner.Observe(A4Observation(),registry,AiProfile.Initial);return planner;
        }
        [Test] public void Reinforcement_SafeAnchorRetainsArmyAndRejectsIndependentOwner()
        {
            var r=Registry();var p=A4Mission(r);var id=p.Capture().ArmyId;var o=A4Observation(100,wave:true);
            AiTacticalExecutor.Observe(o,r,id,PlayableProfile.Default);var army=r.Capture().Armies.Single();
            Assert.AreEqual(id,r.ArmyFor(4));CollectionAssert.AreEqual(new[]{4},army.Reinforcements);
            var action=p.Propose(o,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);CollectionAssert.AreEqual(new[]{4},action.EntityIds);Assert.AreEqual(default(NavPoint),action.Target);Assert.Zero(action.TargetId);
            var before=Newtonsoft.Json.JsonConvert.SerializeObject(r.Capture());p.Propose(o,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r);Assert.AreEqual(before,Newtonsoft.Json.JsonConvert.SerializeObject(r.Capture()),"Proposals cannot mutate membership");
            StringAssert.Contains("tactical owner conflict",r.Reject(Intent(o,"artillery",PlayableCommandKind.Move,new[]{4})));
            Assert.Null(r.Reject(Intent(o,AiArmyPlanner.Policy,PlayableCommandKind.Move,new[]{4})));
            p.Commit(o,action,r);Assert.AreEqual(id,p.Capture().ArmyId);Assert.AreEqual(1,r.Capture().Armies.Count(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded));
        }
        [TestCase(true,true)][TestCase(false,false)] public void Reinforcement_DangerOrMissingNativeProofWaitsWithoutSoloAttack(bool danger,bool route)
        {
            var r=Registry();var p=A4Mission(r);var o=A4Observation(100,wave:true,danger:danger,route:route);var id=p.Capture().ArmyId;
            AiTacticalExecutor.Observe(o,r,id,PlayableProfile.Default);Assert.AreEqual(id,r.ArmyFor(4));
            Assert.Null(p.Propose(o,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r));
            Assert.AreEqual(id,p.Capture().ArmyId);CollectionAssert.AreEqual(new[]{4},r.Capture().Armies.Single().Reinforcements);
        }
        [Test] public void Reinforcement_JoinsOnlyAfterEffectiveMissionOrderAndRestoresMembership()
        {
            var r=Registry();var p=A4Mission(r);var id=p.Capture().ArmyId;var near=A4Observation(100,wave:true,near:true);
            AiTacticalExecutor.Observe(near,r,id,PlayableProfile.Default);var join=p.Propose(near,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r);
            Assert.AreEqual(PlayableCommandKind.Attack,join.Kind);Assert.Contains(4,join.EntityIds.ToArray());p.Commit(near,join,r);
            CollectionAssert.AreEqual(new[]{4},r.Capture().Armies.Single().Reinforcements,"Selection alone does not prove joining");
            p.Observe(near,r,AiProfile.Initial);Assert.True(p.Active);
            var copy=AiArmyRegistry.Restore(r.Capture(),AiProfile.Initial,AiDifficulty.Fighter,100);CollectionAssert.AreEqual(new[]{4},copy.Capture().Armies.Single().Reinforcements);
            p.Observe(A4Observation(101,wave:true,near:true,joined:true),r,AiProfile.Initial);Assert.True(p.Active);Assert.AreEqual(id,p.Capture().ArmyId);Assert.IsEmpty(r.Capture().Armies.Single().Reinforcements);
        }
        [TestCase("valid")][TestCase("no-proof")][TestCase("unsafe")][TestCase("dead")][TestCase("transferred")][TestCase("human")][TestCase("foreign")][TestCase("stale")][TestCase("sequence")][TestCase("source")][TestCase("action")][TestCase("unapplied")][TestCase("cancelled")][TestCase("wrong-policy")]
        public void Reinforcement_PhysicalFollowAssemblyRequiresCurrentProofAndExactAppliedAuthority(string changed)
        {
            var r=Registry();var p=A4Mission(r);var id=p.Capture().ArmyId;var old=A4Observation(100,wave:true,near:true,danger:changed=="unsafe");
            AiTacticalExecutor.Observe(old,r,id,PlayableProfile.Default);var original=r.Capture().Armies.Single();
            var order=new PlayableTacticalOrderSnapshot(4,PlayableOwner.Player,71,7,90,PlayableTacticalOrderKind.Follow,default,changed=="dead"?999:1);
            var stamp=new PlayableOrderStamp(4,changed=="foreign"?PlayableOwner.Enemy:PlayableOwner.Player,changed=="stale"?72:71,1,changed=="sequence"?8:7,90,changed=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Follow,changed=="source"?"foreign":PlayableAiOpeningComposition.SourceIdentity,42,changed=="action"?43:42);
            var entities=old.Entities.Select(u=>u.Id==4?new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,false,0,0,0,order,orderStamp:stamp):u).ToArray();
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities,old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:changed=="no-proof"?Array.Empty<PlayableRouteProof>():old.RouteProofs.ToArray()));
            var record=new PlayableAiTraceRecord(o.Identity,42,90,7,90,changed=="unapplied"?PlayableAiDeliveryStatus.Scheduled:changed=="cancelled"?PlayableAiDeliveryStatus.Cancelled:PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"fixture authority evidence",o.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,o.OwnerId,7,1),changed=="wrong-policy"?"artillery":AiArmyPlanner.Policy,PlayableCommandKind.Follow);
            if(changed=="transferred"){var state=r.Capture();state.Armies.Single().Members=state.Armies.Single().Members.Where(x=>x!=1).ToArray();state.Armies.Single().LeaderId=2;r=AiArmyRegistry.Restore(state,AiProfile.Initial,AiDifficulty.Fighter,100);}
            p.Observe(o,r,AiProfile.Initial,new[]{record});Assert.True(p.Active);Assert.AreEqual(id,p.Capture().ArmyId);
            Assert.AreEqual(changed!="valid",r.Capture().Armies.Single().Reinforcements.Contains(4),"A receipt or geometry alone must not promote the wave");
            if(changed=="valid")
            {
                Assert.Null(p.Propose(o,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r),"Assembly cannot bypass stock repeat cooldown");
                var later=PlayableAiObservation.From(new PlayableSnapshot(o.ProfileId,o.ProfileRevision,71,o.Seed,180,180,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities,o.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:o.Vision,routeProofs:o.RouteProofs.Select(x=>new PlayableRouteProof(x.UnitId,x.Owner,x.Generation,180,x.GeometryRevision,x.Origin,x.Radius,x.Kind,x.TargetId,x.Target,x.Goal,x.Path.ToArray())).ToArray()));
                var fresh=new PlayableEntitySnapshot(5,later.Owner,PlayableEntityKind.Tank,new NavPoint(-16,8),100,false,0,0,0);
                var freshGoal=PlayableUnitRules.FollowGoal(PlayableProfile.Default,later.Entities.Single(u=>u.Id==1).Position,0);
                later=PlayableAiObservation.From(new PlayableSnapshot(later.ProfileId,later.ProfileRevision,71,later.Seed,later.Tick,later.SnapshotSequence,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,
                    later.Entities.Concat(new[]{fresh}).ToArray(),later.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:later.Vision,
                    routeProofs:later.RouteProofs.Concat(new[]{new PlayableRouteProof(5,later.Owner,71,later.Tick,1,fresh.Position,PlayableUnitRules.Radius(PlayableProfile.Default,fresh.Kind),PlayableRouteTargetKind.FriendlyAnchor,1,freshGoal,freshGoal,new[]{freshGoal})}).ToArray()));
                Assert.True(r.TryReinforce(later,id,AiArmyPlanner.Policy,new[]{5}),"Continuous output is a real first-leg competitor");
                var attack=p.ProposeWithRecords(later,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r,new[]{record});Assert.NotNull(attack);Assert.AreEqual(PlayableCommandKind.Attack,attack.Kind);CollectionAssert.AreEqual(new[]{4},attack.EntityIds);Assert.AreEqual(101,attack.TargetId,"Remaining-live objective gets ordinary line Attack at stock cadence");
                p.Observe(o,r,AiProfile.Initial,Array.Empty<PlayableAiTraceRecord>());Assert.False(p.Active,"Unapplied/absent provenance never exempts a joined replacement order");
            }
            else if(changed!="no-proof"&&changed!="unsafe")
            {typeof(AiArmyRegistry).GetMethod("JoinReinforcements",F).Invoke(r,new object[]{id,AiArmyPlanner.Policy,new[]{4}});p.Observe(o,r,AiProfile.Initial,new[]{record});Assert.False(p.Active,"Invalid effective assembly leg must retain A3 replacement guard");}
        }
        [TestCase("valid")][TestCase("human")][TestCase("foreign")][TestCase("stale")][TestCase("dead")][TestCase("transferred")][TestCase("unassigned")][TestCase("joined")][TestCase("no-order")]
        public void Reinforcement_BetweenDecisionsRequestsOnlyCurrentOwnAssemblyLeg(string changed)
        {
            var r=Registry();var p=A4Mission(r);var o=A4Observation(100,wave:true,near:true);AiTacticalExecutor.Observe(o,r,p.Capture().ArmyId,PlayableProfile.Default);var state=r.Capture();var army=state.Armies.Single();
            var order=changed=="no-order"?null:new PlayableTacticalOrderSnapshot(4,PlayableOwner.Player,71,7,90,PlayableTacticalOrderKind.Follow,default,changed=="dead"?999:1);
            var stamp=new PlayableOrderStamp(4,changed=="foreign"?PlayableOwner.Enemy:PlayableOwner.Player,changed=="stale"?72:71,1,7,90,changed=="human"?PlayableOrderOrigin.Human:PlayableOrderOrigin.Ai,PlayableCommandKind.Follow,PlayableAiOpeningComposition.SourceIdentity,42,42);
            var entities=o.Entities.Select(u=>u.Id==4?new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,false,0,0,0,order,orderStamp:stamp):u).ToArray();
            if(changed=="transferred"){army.Members=army.Members.Where(x=>x!=1).ToArray();army.LeaderId=2;}
            if(changed=="unassigned"){army.Members=army.Members.Where(x=>x!=4).ToArray();army.Reinforcements=Array.Empty<int>();}
            if(changed=="joined")army.Reinforcements=Array.Empty<int>();
            AiArmyRegistry.Restore(state,AiProfile.Initial,AiDifficulty.Fighter,100).AssertInvariants();
            var requests=(PlayableRouteRequest[])typeof(AiTacticalExecutor).GetMethod("AssemblyRouteRequests",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{entities,state,o.Owner,o.Generation,o.Tick,PlayableProfile.Default});
            Assert.AreEqual(changed=="valid"?1:0,requests.Length);
            if(changed=="valid"){var request=requests.Single();Assert.AreEqual(4,request.UnitId);Assert.AreEqual(1,request.TargetId);Assert.AreEqual(PlayableRouteTargetKind.FriendlyAnchor,request.Kind);Assert.AreEqual(PlayableUnitRules.FollowGoal(PlayableProfile.Default,entities.Single(u=>u.Id==1).Position,0),request.Target);Assert.AreEqual(100,request.Tick);Assert.AreEqual(71,request.Generation);}
        }
        [Test] public void Reinforcement_AdmissionIsAtomicAndDoesNotStealAssignedOrPendingUnits()
        {
            var r=Registry();var p=A4Mission(r);var o=A4Observation(100,wave:true);var id=p.Capture().ArmyId;
            Assert.False(r.TryReinforce(o,id,AiArmyPlanner.Policy,new[]{4,101}));Assert.Zero(r.ArmyFor(4));
            Assert.False(r.TryReinforce(o,id,"artillery",new[]{4}));
            AiTacticalExecutor.Observe(o,r,id,PlayableProfile.Default,new[]{4});Assert.Zero(r.ArmyFor(4));
            Assert.True(r.TryReinforce(o,id,AiArmyPlanner.Policy,new[]{4}));Assert.False(r.TryReinforce(o,id,AiArmyPlanner.Policy,new[]{4}));r.AssertInvariants();
        }
        [TestCase("tick")][TestCase("owner")][TestCase("generation")][TestCase("origin")][TestCase("radius")][TestCase("anchor")][TestCase("goal")]
        public void Reinforcement_RejectsStaleForeignOrMismatchedDetachedRoute(string changed)
        {
            var o=A4Observation(100,wave:true);var r=o.RouteProofs.Single(x=>x.Kind==PlayableRouteTargetKind.FriendlyAnchor);Assert.True(AiTacticalExecutor.SafeRoute(o,r,PlayableProfile.Default));
            var invalid=new PlayableRouteProof(r.UnitId,changed=="owner"?PlayableOwner.Enemy:r.Owner,changed=="generation"?72:r.Generation,changed=="tick"?99:r.Tick,r.GeometryRevision,changed=="origin"?new NavPoint(-15,0):r.Origin,changed=="radius"?r.Radius*2:r.Radius,r.Kind,r.TargetId,changed=="anchor"?new NavPoint(1,0):r.Target,changed=="goal"?new NavPoint(1,0):r.Goal,r.Path.ToArray());
            Assert.False(AiTacticalExecutor.SafeRoute(o,invalid,PlayableProfile.Default));
        }
        [Test] public void Reinforcement_MultipleUnitsShareOneArmyWaveWithoutReassignmentChurn()
        {
            var r=Registry();var p=A4Mission(r);var original=A4Observation(100,wave:true);var unit=original.Entities.Single(u=>u.Id==4);var additions=new[]{5,6}.Select(id=>new PlayableEntitySnapshot(id,unit.Owner,unit.Kind,unit.Position,unit.Health,false,0,0,0)).ToArray();
            var route=original.RouteProofs.Single(x=>x.Kind==PlayableRouteTargetKind.FriendlyAnchor);var extra=additions.Select(u=>new PlayableRouteProof(u.Id,u.Owner,71,100,1,u.Position,route.Radius,route.Kind,route.TargetId,route.Target,route.Goal,route.Path.ToArray()));
            var o=PlayableAiObservation.From(new PlayableSnapshot(original.ProfileId,original.ProfileRevision,71,original.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,original.Entities.Concat(additions).ToArray(),original.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:original.Vision,routeProofs:original.RouteProofs.Concat(extra).ToArray()));
            var id=p.Capture().ArmyId;AiTacticalExecutor.Observe(o,r,id,PlayableProfile.Default);var move=p.Propose(o,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r);
            CollectionAssert.AreEqual(new[]{4,5,6},move.EntityIds);for(int i=0;i<20;i++)AiTacticalExecutor.Observe(o,r,id,PlayableProfile.Default);
            Assert.True(new[]{4,5,6}.All(u=>r.ArmyFor(u)==id));Assert.AreEqual(1,r.Capture().Armies.Count(a=>a.Major&&a.Phase!=AiArmyPhase.Disbanded));
        }
        [TestCase("valid")][TestCase("unsafe")][TestCase("dead")][TestCase("army-changed")]
        public void Reinforcement_RetainsOnlyItsOwnLiveCertifiedFollowLeader(string change)
        {
            var p=PlayableProfile.Default;var entities=new[]{new PlayableEntitySnapshot(1,PlayableOwner.Player,PlayableEntityKind.Tank,default,100,false,0,0,0),new PlayableEntitySnapshot(2,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-.25,1.5),100,false,0,0,0),new PlayableEntitySnapshot(3,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(0,4),100,false,0,0,0),new PlayableEntitySnapshot(4,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-3.7354,0),100,false,0,0,0,new PlayableTacticalOrderSnapshot(4,PlayableOwner.Player,71,1,0,PlayableTacticalOrderKind.Follow,default,1),navigationOutcome:NavigationOutcome.Arrived)}
                .Concat(new[]{new PlayableEntitySnapshot(8,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(-.5,3),100,false,0,0,0)}).Concat(Enumerable.Range(5,3).Select(i=>new PlayableEntitySnapshot(i,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(20,(i-5)*2),100,false,0,0,0))).ToArray();
            var vision=new PlayableVision(0,50,50,1,1);vision.Refresh(new[]{new VisionSource(default,50)},Array.Empty<KnownBuilding>());
            PlayableAiObservation Observe(bool valid,bool lost=false)
            {
                var points=entities.Where(u=>u.Id==2||valid&&u.Id==1).Select(u=>new PlayableRouteProof(4,PlayableOwner.Player,71,100,1,entities.Single(x=>x.Id==4).Position,p.TankCollisionRadius,PlayableRouteTargetKind.FriendlyAnchor,u.Id,PlayableUnitRules.FollowGoal(p,u.Position,u.HullHeading),PlayableUnitRules.FollowGoal(p,u.Position,u.HullHeading),new[]{PlayableUnitRules.FollowGoal(p,u.Position,u.HullHeading)})).ToArray();
                return PlayableAiObservation.From(new PlayableSnapshot(p.ProfileId,p.Revision,71,A4Opening().MatchSeed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,entities.Where(u=>!lost||u.Id!=1).ToArray(),Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),routeProofs:points,activeProfile:p));
            }
            var r=Registry();var original=Observe(true);Assert.True(r.TryCreate(original,AiArmyRole.Attack,AiArmyPlanner.Policy,new[]{1,2,3,8},out var id));Assert.True(r.TryReinforce(original,id,AiArmyPlanner.Policy,new[]{4}));
            if(change=="army-changed"){Assert.True(r.TryCreate(original,AiArmyRole.Escort,"other-owner",new[]{5,6,7},out var other));Assert.True(r.TryTransfer(original,id,other,new[]{1}));}
            var o=Observe(change=="valid",change=="dead");r.Observe(o);var army=r.Capture().Armies.Single(a=>a.Id==id);Assert.IsEmpty(AiTacticalExecutor.Ready(o,army,p),"The tiny correction band must never enlarge strict actual Ready");
            var action=AiTacticalExecutor.Propose(o,army,p,1);
            if(change=="valid")Assert.Null(action,"An unchanged accepted current leader leg must not be reissued");
            else {Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Follow,action.Kind);Assert.AreEqual(2,action.TargetId,"A certificate for another leader cannot retain the invalid current Follow");Assert.AreEqual(id,r.ArmyFor(action.TargetId));}
        }
        [Test] public void Artillery_WaveBelongsToExistingArmyAndCannotBecomeIndependentRaid()
        {
            var r=Registry();var p=A4Mission(r);var o=A4Observation(100,wave:true,gun:true);var id=p.Capture().ArmyId;
            AiTacticalExecutor.Observe(o,r,id,PlayableProfile.Default);Assert.AreEqual(id,r.ArmyFor(4));
            var move=p.Propose(o,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r);Assert.AreEqual(PlayableCommandKind.Move,move.Kind);Assert.Zero(move.TargetId);Assert.AreEqual(default(NavPoint),move.Target);
            var basis=A4Observation(101,wave:true,gun:true,near:true);var profile=PlayableProfile.Default;
            var order=new PlayableTacticalOrderSnapshot(4,basis.Owner,71,7,90,PlayableTacticalOrderKind.Follow,default,1);
            var stamp=new PlayableOrderStamp(4,basis.Owner,71,1,7,90,PlayableOrderOrigin.Ai,PlayableCommandKind.Follow,PlayableAiOpeningComposition.SourceIdentity,42,42);
            var units=basis.Entities.Select(u=>u.Id==4?new PlayableEntitySnapshot(u.Id,u.Owner,u.Kind,u.Position,u.Health,false,0,0,0,order,orderStamp:stamp):u).ToArray();
            var near=PlayableAiObservation.From(new PlayableSnapshot(basis.ProfileId,basis.ProfileRevision,71,basis.Seed,101,101,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units,basis.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:basis.Vision,routeProofs:basis.RouteProofs.ToArray(),activeProfile:profile));
            var applied=new PlayableAiTraceRecord(near.Identity,42,90,7,90,PlayableAiDeliveryStatus.Applied,PlayableCommandStatus.Applied,"actual support joining receipt",near.OwnerId,PlayableAiOpeningComposition.SourceIdentity,new AiReceiptIdentity(71,near.OwnerId,1,1),AiArmyPlanner.Policy,PlayableCommandKind.Follow);
            AiTacticalExecutor.Observe(near,r,id,PlayableProfile.Default,records:new[]{applied});
            Assert.IsEmpty(r.Capture().Armies.Single().Reinforcements);Assert.Null(p.Propose(near,A4Opening(),PlayableProfile.Default,AiProfile.Initial,r),"A gun never receives the line's attack command");
        }
        [TestCase("tick")][TestCase("generation")][TestCase("unit")]
        public void Artillery_RejectsStaleOrForeignSupportFact(string changed)
        {
            var r=Registry();var p=A4Mission(r);var old=A4Observation(100,wave:true,gun:true,near:true);long id=p.Capture().ArmyId;
            AiTacticalExecutor.Observe(old,r,id,PlayableProfile.Default);
            var fact=new PlayableArtillerySupportSnapshot(changed=="unit"?101:4,changed=="generation"?72:71,changed=="tick"?99:100,false,false,false,true,new NavPoint(5,0));
            var o=PlayableAiObservation.From(new PlayableSnapshot(old.ProfileId,old.ProfileRevision,71,old.Seed,100,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,old.Entities.ToArray(),old.Buildings.ToArray(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:old.Vision,routeProofs:old.RouteProofs.ToArray(),artillerySupport:new[]{fact}));
            Assert.IsEmpty(o.ArtillerySupport);Assert.Null(AiTacticalExecutor.Propose(o,r.Capture().Armies.Single(),PlayableProfile.Default,1));Assert.AreEqual(id,r.ArmyFor(4));
        }
        [TestCase(false)][TestCase(true)] public void Reinforcement_ProductionRallyUsesArmyAdmissionOnlyForAiCombat(bool ai)
        {
            var c=OfflineParticipantAuthorityTests.Config(2,false);c=new OfflineMatchConfiguration(c.Profile,c.SourceIdentity,c.MapIdentity,c.RouteProvenance,c.Seed,c.Roster.Select((p,i)=>new OfflineParticipant(p.Id,p.LogicalPlayer,p.Team,i==0&&ai?OfflineControl.Ai:OfflineControl.Human)).ToArray(),c.Starts.ToArray(),c.Sites.ToArray(),c.Obstacles.ToArray(),new double[c.Starts.Count,c.Starts.Count]);
            var a=new PlayableAuthorityTick(c,71);var d=Field(a,"domain");var target=new NavPoint(0,0);
            d.GetType().GetMethod("SpawnProduced",F).Invoke(d,new object[]{new NavPoint(-10,0),(NavPoint?)target,PlayableOwner.Player,PlayableEntityKind.Tank});
            var unit=a.ParticipantView(c.Roster[0].Id).Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank).OrderByDescending(u=>u.Id).First();
            Assert.AreEqual(!ai,unit.CurrentOrder!=null);if(!ai)Assert.AreEqual(target,unit.CurrentOrder.Destination);a.Stop();
        }
        [Test] public void Artillery_ActualProjectionUsesOwnArmyCoverAndOrdinarySupportMove()
        {
            var p=PlayableProfile.Default;var type=typeof(PlayableRuntime).Assembly.GetType("Spacewars.Runtime.PlayableDomain",true);
            var d=Activator.CreateInstance(type,F,null,new object[]{p,71L},null);var units=(System.Collections.IDictionary)Field(d,"units");var navigation=(NavigationSession)type.GetProperty("Navigation",F).GetValue(d);
            foreach(int old in units.Keys.Cast<int>().ToArray()){navigation.Remove(old);units.Remove(old);}
            int Spawn(PlayableEntityKind kind,double x,double z,PlayableOwner owner=PlayableOwner.Player)=>(int)type.GetMethod("SpawnUnit",F).Invoke(d,new object[]{new NavPoint(x,z),owner,kind});
            var core=new[]{Spawn(PlayableEntityKind.Tank,-24,-8),Spawn(PlayableEntityKind.Tank,-24,-6),Spawn(PlayableEntityKind.Tank,-24,-10)};
            int gun=Spawn(PlayableEntityKind.Shkval,-12,0);Spawn(PlayableEntityKind.Tank,-9,0);Spawn(PlayableEntityKind.Tank,-4,0,PlayableOwner.Enemy);
            PlayableSnapshot Project(AiArmyRegistryState state)=>(PlayableSnapshot)type.GetMethod("PlayerSnapshotForArmyAi",F).Invoke(d,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,A4Opening().MatchSeed,PlayableOwner.Player,true,state});
            var legacy=Project(null);Assert.True(legacy.ArtillerySupport.Single(f=>f.UnitId==gun).Supported,"Outside-army cover is the negative control");
            var r=Registry();Assert.True(r.TryCreate(PlayableAiObservation.From(legacy),AiArmyRole.Attack,AiArmyPlanner.Policy,core.Concat(new[]{gun}),out var id));
            var actual=PlayableAiObservation.From(Project(r.Capture()));var fact=actual.ArtillerySupport.Single(f=>f.UnitId==gun);Assert.False(fact.Supported,"Other army/free units cannot lend support membership");
            var action=AiTacticalExecutor.Propose(actual,r.Capture().Armies.Single(),p,1);Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);CollectionAssert.AreEqual(new[]{gun},action.EntityIds);Assert.Zero(action.TargetId);Assert.AreEqual(id,r.ArmyFor(gun));
            var args=new object[]{new PlayableCommand(71,1,"player-1",action.Kind,action.CopyEntityIds(),action.Target),null};Assert.AreEqual(PlayableCommandStatus.Applied,type.GetMethod("Apply",F).Invoke(d,args));
            using(var routes=new Spacewars.Presentation.UnityHostRouteService())routes.Service(navigation.Requests,navigation.Answers,71,(NavGeometry)type.GetProperty("Geometry",F).GetValue(d),p,64);
            navigation.ApplyResults();type.GetMethod("Step",F).Invoke(d,new object[]{1d/30});
            var effective=Project(r.Capture()).Entities.Single(u=>u.Id==gun);Assert.AreEqual(PlayableTacticalOrderKind.Move,effective.CurrentOrder.Kind);Assert.AreEqual(action.Target,effective.CurrentOrder.Destination);
            var doctrine=new PlayableAiArtillerySupportPolicy(p);var production=doctrine.TryPlan(actual,A4Opening(),null,productionOnly:true);Assert.True(production==null||production.Kind==PlayableCommandKind.QueueShkval,"Doctrine cannot start another tactical loop");
        }
        [Test] public void Reinforcement_CurrentUnityOpeningPremise()
        {
            var c=A4NativeConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();
            using(var routes=new UnityHostRouteService())for(int tick=0;tick<60;tick++)NativeStep(a,routes);
            var executor=(AiOpeningExecutor)Field(owner,"openingExecutor");
            int required=(int)typeof(AiOpeningExecutor).GetProperty("RequiredForce",F).GetValue(executor);
            var desired=typeof(AiOpeningExecutor).GetMethod("DesiredForce",F).Invoke(executor,new object[]{AiProfile.Initial,required});
            TestContext.WriteLine("UNITY_OPENING_PREMISE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{c.Seed,AiProfile.Initial.Id,AiProfile.Initial.Hash,Fields=AiProfileMetadata.Fields.Count,Opening=A4Opening(),required,desired,executor=executor.Capture(),Mission=((AiArmyPlanner)Field(owner,"armyPlanner")).Capture()}));
            Assert.AreEqual(PlayableAiOpening.Safe,A4Opening().Opening);Assert.Greater(required,0);a.Stop();
        }
        private static OfflineMatchConfiguration A4NativeConfig(bool intrusion=false,bool artillery=false,bool follow=false,bool durableObjective=false)
        {
            var p=PlayableProfile.Default;var starts=new[]{new OfflineStart("west",1,new NavPoint(-24,0),new NavPoint(-10,-8),pin:1),new OfflineStart("east",2,durableObjective?new NavPoint(8,-8):new NavPoint(24,24),new NavPoint(18,24),pin:2)};
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,new[]{new TerritorySlot(1,new NavPoint(-24,-9),0),new TerritorySlot(2,new NavPoint(-14,0),0),new TerritorySlot(3,new NavPoint(-14,12),0)}),new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>()),new TerritorySite(3,PlayableBuildingKind.Outpost,durableObjective?new NavPoint(24,24):new NavPoint(8,-8),Array.Empty<TerritorySlot>())};
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human)};
            // Keep the initial intruder outside the native weapon and center
            // envelopes. Its ordinary human Move crosses the current certified
            // leg during the unchanged delivery reaction window.
            double followIntruderZ=-19.5;
            return new OfflineMatchConfiguration(p,"a4-army-reinforcement","a4-native-front","UnityHostRouteService",A4Opening().MatchSeed,roster,starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,100},{100,0}},scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-7,-8)),new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-5,-8)),new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-3,-8)),new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-1,-8))}.Concat(follow?new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(2,followIntruderZ))}:intrusion?new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Tank,new NavPoint(-4.5,-20.25))}:Array.Empty<OfflineScenarioUnit>()).Concat(artillery?new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Shkval,new NavPoint(-7,-6))}:Array.Empty<OfflineScenarioUnit>()).ToArray(),scenarioBuildings:new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Factory,1,1,new NavPoint(-24,-9)),new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,2,new NavPoint(-14,0)),new OfflineScenarioBuilding(1,PlayableBuildingKind.Refinery,1,3,new NavPoint(-14,12)),new OfflineScenarioBuilding(2,PlayableBuildingKind.Outpost,3,0,durableObjective?new NavPoint(24,24):new NavPoint(8,-8))});
        }
        [TestCase(1)][TestCase(3)] public void Reinforcement_ActualAuthorityRoutesJoinsAndRestoresPendingWave(int count)
        {
            var c=A4NativeConfig(durableObjective:true);var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();var registry=(AiArmyRegistry)Field(owner,"armies");var planner=(AiArmyPlanner)Field(owner,"armyPlanner");var domain=Field(a,"domain");
            using(var routes=new UnityHostRouteService())
            {
                for(int i=0;i<300&&(!planner.Active||planner.Capture().TargetKind==PlayableRouteTargetKind.FriendlyAnchor||!planner.HasAppliedOrder);i++)NativeStep(a,routes);
                Assert.True(planner.Active);Assert.AreNotEqual(PlayableRouteTargetKind.FriendlyAnchor,planner.Capture().TargetKind,"An actual offensive mission must precede wave admission");
                Assert.True(planner.HasAppliedOrder,"The initial offensive command must actually apply before spawning this wave");
                var id=planner.Capture().ArmyId;var initialArmy=registry.Capture().Armies.Single(g=>g.Id==id);
                var beforeWave=PlayableAiObservation.From(a.ParticipantView("player-1"));
                Assert.GreaterOrEqual(AiReinforcementPolicy.ReceivingCore(beforeWave,initialArmy,c.Profile,AiProfile.Initial).Length,initialArmy.MinimumViableForce);
                TestContext.WriteLine("NATIVE_WAVE_PREMISE "+Newtonsoft.Json.JsonConvert.SerializeObject(new{c.Seed,AiProfile.Initial.Hash,InitialCore=initialArmy.Members.Except(initialArmy.Reinforcements),ExistingReinforcements=initialArmy.Reinforcements,ProducedWaveCount=count,Mission=planner.Capture()}));
                // The native objective must remain alive through assembly needs a normal HQ objective
                // and the stock Attack cooldown. Its native health and all timing contracts are unchanged.
                var cohort=new System.Collections.Generic.List<int>();
                for(int n=0;n<count;n++){domain.GetType().GetMethod("SpawnProduced",F).Invoke(domain,new object[]{new NavPoint(-10,-5+n*2),(NavPoint?)new NavPoint(8,-8),PlayableOwner.Player,PlayableEntityKind.Tank});cohort.Add(a.ParticipantView("player-1").Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank).Max(u=>u.Id));NativeStep(a,routes);}
                int wave=cohort[0];Assert.True(cohort.All(u=>a.ParticipantView("player-1").Entities.Single(x=>x.Id==u).CurrentOrder==null));
                bool assigned=false,move=false,joined=false,roundtrip=false,followRoundtrip=false,attackJoined=false;long began=a.Tick;var effective=new System.Collections.Generic.HashSet<int>();
                var diagnosticTicks=new System.Collections.Generic.HashSet<long>();
                void ObserveJoin()
                {
                    move|=a.ParticipantView("player-1").Entities.Single(u=>u.Id==wave).CurrentOrder?.Kind==PlayableTacticalOrderKind.Move;
                    if(count==3&&new long[]{135,225,315,405,450,495,585}.Contains(a.Tick)&&diagnosticTicks.Add(a.Tick))
                    {
                        byte[] OwnerBytes()
                        {using(var stream=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(stream)){owner.GetType().GetMethod("WriteState",F).Invoke(owner,new object[]{writer});writer.Flush();return stream.ToArray();}}
                        var snapshot=(PlayableSnapshot)domain.GetType().GetMethod("PlayerSnapshotForArmyAi",F).Invoke(domain,new object[]{a.Tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player,true,registry.Capture()});
                        var observation=PlayableAiObservation.From(snapshot);var checkpoint=a.CaptureDiagnosticCheckpoints().Single();var before=OwnerBytes();
                        var proposal=typeof(AiArmyPlanner).GetMethod("ProposeWithRecords",F).Invoke(planner,new object[]{observation,checkpoint.Opening,c.Profile,AiProfile.Initial,registry,checkpoint.Records});
                        CollectionAssert.AreEqual(before,OwnerBytes(),"Diagnostic detached Propose must not mutate live owner/planner/RNG state");
                        var pending=((System.Collections.IEnumerable)Field(owner,"pendingItems")).Cast<object>().Select(x=>new{Action=(PlayableAiAction)Field(x,"Action"),DueTick=(long)Field(x,"DueTick"),Policy=(string)Field(x,"PolicyName")}).ToArray();
                        var navigation=(NavigationSession)domain.GetType().GetProperty("Navigation",F).GetValue(domain);
                        foreach(var client in observation.Entities.Where(u=>cohort.Contains(u.Id)&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow))
                        {
                            var anchor=observation.Entities.Single(u=>u.Id==client.CurrentOrder.TargetId);var goal=PlayableUnitRules.FollowGoal(c.Profile,anchor.Position,anchor.HullHeading);
                            navigation.Crowd.TryGet(client.Id,out var actor);
                            TestContext.WriteLine("FOLLOW_EXECUTION_READBACK "+Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,client.Id,client.Position,client.NavigationOutcome,
                                c.Profile.FollowDistance,c.Profile.FollowArrivalTolerance,Anchor=anchor.Id,anchor.HullHeading,Goal=goal,Pending=navigation.IsPending(client.Id),
                                CanResume=domain.GetType().GetMethod("CanResumeCombat",F).Invoke(domain,new object[]{client.Id}),
                                CampaignAllowed=domain.GetType().GetMethod("CampaignMoveAllowed",F).Invoke(domain,new object[]{client.Id,goal}),
                                Free=((NavGeometry)domain.GetType().GetProperty("Geometry",F).GetValue(domain)).IsFree(goal,PlayableUnitRules.Radius(c.Profile,client.Kind)),
                                Actor=actor}));
                        }
                        TestContext.WriteLine("A6 cohort diagnostic "+Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,Cohort=cohort,OriginalArmy=id,Registry=registry.Capture(),Mission=planner.Capture(),Proposed=proposal,Pending=pending,Entities=observation.Entities.Where(u=>u.Owner==observation.Owner),Routes=observation.RouteProofs.Where(r=>cohort.Contains(r.UnitId)),SafeFollow10=observation.RouteProofs.Where(r=>cohort.Contains(r.UnitId)&&r.TargetId==10&&r.Goal.Equals(r.Target)).Select(r=>new{r.UnitId,r.TargetId,r.Goal,Safe=AiTacticalExecutor.SafeRoute(observation,r,c.Profile)}),Applied=checkpoint.Records.Where(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied)}));
                    }
                    var g=registry.Capture().Armies.FirstOrDefault(x=>x.Id==id);if(g==null||g.Phase==AiArmyPhase.Disbanded)return;
                    if(a.Tick>=435&&a.Tick<=461)
                    {
                        var traceState=registry.Capture();var traceArmy=traceState.Armies.Single(x=>x.Id==id);traceArmy.Reinforcements=traceArmy.Reinforcements.Concat(new[]{wave}).Distinct().OrderBy(x=>x).ToArray();
                        var traceSnapshot=(PlayableSnapshot)domain.GetType().GetMethod("PlayerSnapshotForArmyAi",F).Invoke(domain,new object[]{a.Tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player,true,traceState});var trace=PlayableAiObservation.From(traceSnapshot);var actor=trace.Entities.Single(x=>x.Id==wave);var leader=trace.Entities.FirstOrDefault(x=>x.Id==actor.CurrentOrder?.TargetId);
                        PlayableAiObservation Narrow(bool paused,RuntimeStatus status)=>(PlayableAiObservation)PlayableAiObservation.From((PlayableSnapshot)domain.GetType().GetMethod("PlayerSnapshotForArmyAi",F).Invoke(domain,new object[]{a.Tick,status,paused,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player,false,registry.Capture()}));
                        Assert.IsEmpty(Narrow(true,RuntimeStatus.Paused).RouteProofs.Where(x=>x.Kind==PlayableRouteTargetKind.FriendlyAnchor),"Pause must not solve assembly legs");
                        Assert.IsEmpty(Narrow(false,RuntimeStatus.Stopped).RouteProofs.Where(x=>x.Kind==PlayableRouteTargetKind.FriendlyAnchor),"Stopped snapshots must not solve assembly legs");
                        var narrow=Narrow(false,RuntimeStatus.Running);Assert.IsEmpty(narrow.RouteProofs.Where(x=>x.Kind==PlayableRouteTargetKind.VisibleEnemy),"Nondecision assembly must not solve strategic enemy routes");var subset=narrow.RouteProofs.Where(x=>x.Kind==PlayableRouteTargetKind.FriendlyAnchor).ToArray();
                        // A6 can admit a compatible cohort in one attention command. The
                        // CURRENT registry query must certify each remaining real follower,
                        // while promoted actors contribute no assembly work.
                        var followers=narrow.Entities.Where(u=>u.Owner==narrow.Owner&&u.Health>0&&g.Reinforcements.Contains(u.Id)&&
                            u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow&&u.CurrentOrder.Generation==a.Generation&&
                            u.CurrentOrder.UnitId==u.Id&&u.CurrentOrder.Owner==narrow.Owner&&
                            narrow.Entities.Any(l=>l.Id==u.CurrentOrder.TargetId&&l.Owner==narrow.Owner&&l.Health>0&&g.Members.Contains(l.Id)&&!g.Reinforcements.Contains(l.Id)&&AiRosterCatalog.Initial.For(l.Kind).lineWeight>0)).ToArray();
                        Assert.AreEqual(followers.Length,subset.Length,"Exactly one current assembly leg per actual remaining follower");
                        foreach(var follower in followers)
                        {
                            var currentLeader=narrow.Entities.Single(l=>l.Id==follower.CurrentOrder.TargetId);var leg=subset.Single(r=>r.UnitId==follower.Id);
                            Assert.AreEqual(a.Generation,leg.Generation);Assert.AreEqual(narrow.Owner,leg.Owner);Assert.AreEqual(a.Tick,leg.Tick);Assert.AreEqual(follower.Position,leg.Origin);
                            Assert.AreEqual(currentLeader.Id,leg.TargetId);Assert.AreEqual(PlayableUnitRules.FollowGoal(c.Profile,currentLeader.Position,currentLeader.HullHeading),leg.Target);Assert.AreEqual(leg.Target,leg.Goal);
                            Assert.True(AiTacticalExecutor.SafeRoute(narrow,leg,c.Profile),"Every actual cohort follower retains its own safe certified current leg");
                        }
                        Assert.True(subset.All(r=>g.Reinforcements.Contains(r.UnitId)),"Promoted actors, including the original wave actor, contribute no assembly leg");
                        if(!g.Reinforcements.Contains(wave))Assert.IsEmpty(subset.Where(r=>r.UnitId==wave),"Assembly proof work ends on this actor's promotion");
                        else if(actor.CurrentOrder?.Kind==PlayableTacticalOrderKind.Follow)Assert.AreEqual(1,subset.Count(r=>r.UnitId==wave),"The original actor's actual safe leg remains freshly certified");
                        TestContext.WriteLine("A4 assembly diagnostic "+Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,OriginalArmy=id,ActualArmy=registry.ArmyFor(wave),ActualRegistry=g,Mission=planner.Capture(),Actor=actor,Leader=leader,LeaderArmy=leader==null?0:registry.ArmyFor(leader.Id),Routes=trace.RouteProofs.Where(x=>x.UnitId==wave),StrictReady=AiTacticalExecutor.Ready(trace,traceArmy,c.Profile).Select(x=>x.Id).ToArray(),AssemblyMatches=AssemblyMatches(trace,g,actor,a.CaptureDiagnosticCheckpoints().Single().Records),FollowRecords=a.CaptureDiagnosticCheckpoints().Single().Records.Where(x=>x.Policy==AiArmyPlanner.Policy&&x.Kind==PlayableCommandKind.Follow)}));
                    }
                    foreach(var actual in a.ParticipantView("player-1").Entities.Where(u=>cohort.Contains(u.Id)&&registry.ArmyFor(u.Id)==id&&!g.Reinforcements.Contains(u.Id)&&!effective.Contains(u.Id)))
                    {
                        // Ask for this actor alone; other already assembled wave units
                        // remain real current core and may be its effective Follow leader.
                        // This detached request changes no authority state/body/order.
                        var requestState=registry.Capture();var requestArmy=requestState.Armies.Single(x=>x.Id==id);requestArmy.Reinforcements=requestArmy.Reinforcements.Concat(new[]{actual.Id}).Distinct().OrderBy(x=>x).ToArray();
                        var snapshot=(PlayableSnapshot)domain.GetType().GetMethod("PlayerSnapshotForArmyAi",F).Invoke(domain,new object[]{a.Tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player,true,requestState});var observation=PlayableAiObservation.From(snapshot);var u=observation.Entities.Single(x=>x.Id==actual.Id);
                        var physical=g.Copy();physical.Reinforcements=physical.Reinforcements.Concat(new[]{u.Id}).ToArray();
                        if(!AiTacticalExecutor.Ready(observation,physical,c.Profile).Any(x=>x.Id==u.Id))continue;
                        bool attack=u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==planner.Capture().TargetId;
                        bool follow=AssemblyMatches(observation,g,u,a.CaptureDiagnosticCheckpoints().Single().Records);
                        if(attack||follow){Assert.AreEqual(id,planner.Capture().ArmyId,"Every physical join must occur in the live ORIGINAL mission, even if the complete replay later observes natural retasking");effective.Add(u.Id);attackJoined|=attack;TestContext.WriteLine("Physical original-army join tick="+a.Tick+" unit="+u.Id+" order="+u.CurrentOrder.Kind);}
                    }
                    joined=effective.Count==count;
                }
                for(int i=0;i<600&&!joined&&a.Tick-began<690;i++)
                {
                    NativeStep(a,routes);var view=a.ParticipantView("player-1");var unit=view.Entities.Single(u=>u.Id==wave);
                    if(registry.ArmyFor(wave)==id)assigned=true;
                    if(unit.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move)move=true;
                    var army=registry.Capture().Armies.FirstOrDefault(g=>g.Id==id);
                    ObserveJoin();
                    if(a.Tick%45==0)
                    {
                        var snapshot=(PlayableSnapshot)domain.GetType().GetMethod("PlayerSnapshotForArmyAi",F).Invoke(domain,new object[]{a.Tick,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player,true,registry.Capture()});var observation=PlayableAiObservation.From(snapshot);
                        TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,wave,id,Army=registry.ArmyFor(wave),Unit=unit,Mission=planner.Capture(),ArmyState=army,Ready=army==null?Array.Empty<int>():AiTacticalExecutor.Ready(observation,army,c.Profile).Select(u=>u.Id).ToArray(),Routes=observation.RouteProofs.Where(r=>r.UnitId==wave),Checkpoint=a.CaptureDiagnosticCheckpoints().Single().Records.TakeLast(5)}));
                    }
                    var pending=((System.Collections.IEnumerable)Field(owner,"pendingItems")).Cast<object>().FirstOrDefault(x=>(string)Field(x,"PolicyName")==AiArmyPlanner.Policy);var kind=pending==null?(PlayableCommandKind?)null:((PlayableAiAction)Field(pending,"Action")).Kind;
                    if(assigned&&(!roundtrip&&kind==PlayableCommandKind.Move||!followRoundtrip&&kind==PlayableCommandKind.Follow))
                    {
                        var restored=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);ExactByteAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());
                        Assert.LessOrEqual(a.Tick-began+90,690,"The original scenario horizon must fit each full pending replay window");
                        for(int t=0;t<90;t++){NativeStep(a,routes);NativeStep(restored,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),restored.CaptureBytes());ObserveJoin();}
                        restored.Stop();if(kind==PlayableCommandKind.Move)roundtrip=true;else followRoundtrip=true;
                    }
                }
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{assigned,move,roundtrip,joined,a.Tick,Mission=planner.Capture(),Wave=a.ParticipantView("player-1").Entities.Single(u=>u.Id==wave)}));
                Assert.True(assigned,"Wave must be assigned to the live original ArmyId");Assert.True(move,"Wave must use an ordinary native gather route");Assert.True(roundtrip,"Pending wave Move must survive actual authority restore");Assert.True(followRoundtrip,"Tiny arrival correction and pending Follow callback must survive actual authority restore");Assert.True(joined,"Every wave unit must physically join the original live army with a certified effective own assembly Follow or line order");
                while(a.Tick-began<690&&registry.Capture().Armies.Single(g=>g.Id==id).Phase!=AiArmyPhase.Disbanded)
                {NativeStep(a,routes);if(a.ParticipantView("player-1").Entities.Any(u=>cohort.Contains(u.Id)&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==planner.Capture().TargetId&&registry.ArmyFor(u.Id)==id))attackJoined=true;}
                TestContext.WriteLine("NATIVE_MISSION_FINAL "+Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,attackJoined,Army=registry.Capture().Armies.Single(g=>g.Id==id),Mission=planner.Capture(),View=a.ParticipantView("player-1"),Applied=a.CaptureDiagnosticCheckpoints().Single().Records.Where(r=>r.Policy==AiArmyPlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied)}));
                Assert.True(attackJoined,"The native original-army control must still prove ordinary line Attack, observed separately after physical assembly");
                Assert.AreEqual(AiArmyPhase.Disbanded,registry.Capture().Armies.Single(g=>g.Id==id).Phase,"Actual mission completion must release its full wave in the original bounded scenario window");Assert.True(cohort.All(u=>registry.ArmyFor(u)!=id));
            }
            a.Stop();
        }
        private static OfflineMatchConfiguration DeliverySafetyConfiguration()
        {
            var basis=A4NativeConfig();var p=basis.Profile;
            var enemySite=new TerritorySite(3,PlayableBuildingKind.Outpost,basis.Sites.Single(x=>x.Id==3).Position,p);
            var mineSite=new TerritorySite(4,PlayableBuildingKind.Mine,new NavPoint(0,0),p);
            var sites=new[]{new TerritorySite(1,PlayableBuildingKind.Headquarters,basis.Starts[0].Position,new[]{new TerritorySlot(1,new NavPoint(-24,-9),0),new TerritorySlot(2,new NavPoint(-14,0),0),new TerritorySlot(3,new NavPoint(-14,12),0)})}
                .Concat(basis.Sites.Where(x=>x.Id!=1&&x.Id!=3)).Concat(new[]{enemySite,mineSite}).ToArray();
            // Bind the actual Unity opening: three Explorers, including the starter.
            var units=basis.ScenarioUnits.ToArray();
            // Ready, actually visible native income removes the economic-lead guard
            // premise without overriding personality, RNG or the ordinary strategy.
            var income=enemySite.Slots.Where(slot=>slot.Id==2||slot.Id==3).Select(slot=>new OfflineScenarioBuilding(2,PlayableBuildingKind.Refinery,3,slot.Id,slot.Position,slot.Heading));
            var buildings=basis.ScenarioBuildings
                .Concat(income).Concat(new[]{new OfflineScenarioBuilding(2,PlayableBuildingKind.Mine,4,0,mineSite.Position)}).ToArray();
            return new OfflineMatchConfiguration(p,"delivery-safety-current-opening",basis.MapIdentity,basis.RouteProvenance,basis.Seed,basis.Roster.ToArray(),basis.Starts.ToArray(),sites,basis.Obstacles.ToArray(),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)] public void Reinforcement_DelayedDeliverySafetyKeepsOrdinaryWorldTickingAndPendingAction(int completion)
        {
            var c=DeliverySafetyConfiguration();var a=new PlayableAuthorityTick(c,71,asynchronousRoutes:true);
            var owner=Owners(a).Single();var planner=(AiArmyPlanner)Field(owner,"armyPlanner");var source=(PlayableDomain)Field(a,"domain");
            PlayableRuntime runtime=null,restored=null;PlayableDomain domain=null,restoredDomain=null;CertifiedRouteWorker worker=null;
            using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();int parked=0;
            try{
                using(var routes=new UnityHostRouteService()){
                    void Advance(){routes.Service(a,64);Assert.True(a.TryAdvance());}
                    Assert.AreEqual(1,c.Seed,"Freeze the current actual opening seed; a changed derived seed needs a new premise review");
                    Assert.AreEqual("6170fc2251cee9daaa8cf1f100451b36a3343f7c4349ef766255e1b08a03f543",AiProfile.Initial.Hash,"Bind the actual Unity AI profile before the first Advance");
                    var executor=(AiOpeningExecutor)Field(owner,"openingExecutor");var registry=(AiArmyRegistry)Field(owner,"armies");
                    var initial=a.ParticipantView("player-1");
                    foreach(var unit in initial.Entities.Where(u=>u.Kind!=PlayableEntityKind.Hawk))Assert.True(source.Geometry.IsFree(unit.Position,PlayableUnitRules.Radius(c.Profile,unit.Kind)),"Native initial spawns must be free before first Advance");
                    Assert.AreEqual(2,initial.Entities.Count(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Tank));
                    Assert.AreEqual(3,initial.Entities.Count(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Explorer));
                    PlayableAiTraceRecord offensiveEvent=null;long eventArmy=0;int eventTarget=0;
                    var micro=(AiCombatMicroPolicy)Field(owner,"combatMicro");
                    bool CurrentOffenseReady()
                    {
                        if(!planner.Active||!executor.AllowsPressure||!executor.Capture().InitialForceFulfilled)return false;
                        var mission=planner.Capture();var state=registry.Capture();var army=state.Armies.SingleOrDefault(g=>g.Id==mission.ArmyId);
                        if(state.OwnerId!="player-1"||state.Generation!=71||army==null||army.OwnerId!=state.OwnerId||!army.Major||army.Role!=AiArmyRole.Attack||
                            army.Phase==AiArmyPhase.Disbanded||army.Phase==AiArmyPhase.Retreating||army.Phase==AiArmyPhase.Recovering||army.Phase==AiArmyPhase.Regrouping||mission.TargetKind!=PlayableRouteTargetKind.VisibleEnemy)return false;
                        var o=PlayableAiObservation.From(a.ParticipantView("player-1"));
                        var target=o.Buildings.Where(b=>b.Id==mission.TargetId&&o.IsHostile(b.Owner)&&b.Health>0).Select(b=>b.Position)
                            .Concat(o.Entities.Where(u=>u.Id==mission.TargetId&&o.IsHostile(u.Owner)&&u.Health>0).Select(u=>u.Position)).ToArray();
                        if(target.Length!=1||!mission.TargetVisible||o.Vision?.IsVisible(target[0])!=true)return false;
                        var core=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&army.Members.Contains(u.Id)&&!army.Reinforcements.Contains(u.Id)&&AiRosterCatalog.Initial.LineWeight(u.Kind)>0&&
                            !u.Held&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human).ToArray();
                        if(core.Length<army.MinimumViableForce||!(bool)typeof(AiArmyPlanner).GetMethod("Viable",F).Invoke(planner,new object[]{core,c.Profile,AiProfile.Initial,0}))return false;
                        var records=a.CaptureDiagnosticCheckpoints().Single().Records;
                        bool BoundNativeAttack(PlayableEntitySnapshot u)
                        {
                            var stamp=u.OrderStamp;var order=u.CurrentOrder;var pendingOrder=u.Queue?.Pending;
                            if(stamp==null||stamp.UnitId!=u.Id||stamp.Owner!=o.Owner||stamp.Generation!=o.Generation||stamp.Origin!=PlayableOrderOrigin.Ai||stamp.Kind!=PlayableCommandKind.Attack||
                                stamp.Source!=PlayableAiOpeningComposition.SourceIdentity||stamp.ActionId<=0||stamp.JobId!=stamp.ActionId||stamp.Sequence<=0||stamp.Tick>o.Tick)return false;
                            return order!=null&&order.Kind==PlayableTacticalOrderKind.Attack&&order.TargetId==mission.TargetId&&order.Owner==o.Owner&&order.Generation==o.Generation&&order.UnitId==u.Id&&order.CommandSequence==stamp.Sequence&&order.IssuedTick==stamp.Tick||
                                u.Queue?.PendingAiRouteViable==true&&pendingOrder!=null&&pendingOrder.Kind==PlayableCommandKind.Attack&&pendingOrder.VisibleTargetId==mission.TargetId&&pendingOrder.AiActionId==stamp.ActionId&&pendingOrder.CommandSequence==stamp.Sequence;
                        }
                        if(offensiveEvent==null)
                        {
                            if(core.Any(u=>PlayableUnitRules.HealthRatio(c.Profile,u)<1))return false;
                            offensiveEvent=records.FirstOrDefault(r=>r.Policy==AiArmyPlanner.Policy&&r.Kind==PlayableCommandKind.Attack&&r.Status==PlayableAiDeliveryStatus.Applied&&r.RuntimeStatus==PlayableCommandStatus.Applied&&
                                r.OwnerId==o.OwnerId&&r.SourceIdentity==PlayableAiOpeningComposition.SourceIdentity&&r.ReceiptIdentity?.Generation==o.Generation&&r.ReceiptIdentity.OwnerId==o.OwnerId&&
                                core.Any(u=>BoundNativeAttack(u)&&u.OrderStamp.ActionId==r.ActionId&&u.OrderStamp.Sequence==r.CommandSequence&&u.OrderStamp.Tick==r.ApplicationTick));
                            if(offensiveEvent==null)return false;eventArmy=army.Id;eventTarget=mission.TargetId;
                        }
                        // The exact Applied event is latched once, independent of debug ring
                        // retention. Current ownership/order certificates remain a separate gate.
                        return army.Id==eventArmy&&mission.TargetId==eventTarget&&core.All(u=>BoundNativeAttack(u)||micro.CurrentOverride(o,army,u)||AiArmyPlanner.AssemblyFollow(o,army,u,records));
                    }
                    var initialObservation=PlayableAiObservation.From(initial);
                    double SeenIncome(PlayableOwner side)=>initialObservation.Buildings.Where(b=>b.Owner==side&&b.Health>0&&b.Phase==ConstructionPhase.Ready).Sum(b=>TerritoryRules.Income(c.Profile,b.Kind));
                    Assert.AreEqual(40,SeenIncome(initialObservation.Owner));Assert.AreEqual(40,SeenIncome(PlayableOwner.Enemy));Assert.False(AiOpponentSelector.KnownEconomicLead(initialObservation,c.Profile));
                    for(int i=0;i<300&&!CurrentOffenseReady();i++)Advance();
                    Assert.True(CurrentOffenseReady(),"The real current opening must yield an exact Applied offensive event and a current viable bound core before spawning a reinforcement");
                    Assert.AreEqual(3,executor.RequiredForce);var desired=executor.DesiredForce(AiProfile.Initial,executor.RequiredForce);
                    Assert.AreEqual(3,desired[PlayableEntityKind.Explorer]);Assert.False(desired.ContainsKey(PlayableEntityKind.Tank));
                    TestContext.WriteLine($"DELIVERY_PREMISE seed={c.Seed} generation=71 ai={AiProfile.Initial.Id}@{AiProfile.Initial.Revision} hash={AiProfile.Initial.Hash} required={executor.RequiredForce} explorer={desired[PlayableEntityKind.Explorer]} tank=0 army={planner.Capture().ArmyId} target={planner.Capture().TargetId} phase={planner.Capture().Phase}");
                    long originalArmy=planner.Capture().ArmyId;int originalTarget=planner.Capture().TargetId;
                    source.GetType().GetMethod("SpawnProduced",F).Invoke(source,new object[]{new NavPoint(-18,-8),(NavPoint?)null,PlayableOwner.Player,PlayableEntityKind.Tank});
                    int wave=a.ParticipantView("player-1").Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank).Max(u=>u.Id);object pending=null;
                    for(int i=0;i<150&&pending==null;i++){
                        Advance();var currentArmy=registry.Capture().Armies.SingleOrDefault(g=>g.Id==originalArmy);
                        if(CurrentOffenseReady()&&planner.Capture().ArmyId==originalArmy&&planner.Capture().TargetId==originalTarget&&currentArmy!=null&&currentArmy.Reinforcements.Contains(wave))
                            pending=((System.Collections.IEnumerable)Field(owner,"pendingItems")).Cast<object>().FirstOrDefault(x=>(string)Field(x,"PolicyName")==AiArmyPlanner.Policy&&(((PlayableAiAction)Field(x,"Action")).Kind==PlayableCommandKind.Move||((PlayableAiAction)Field(x,"Action")).Kind==PlayableCommandKind.Follow)&&((PlayableAiAction)Field(x,"Action")).EntityIds.Contains(wave));
                    }
                    TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,Wave=a.ParticipantView("player-1").Entities.Single(u=>u.Id==wave),Mission=planner.Capture(),Registry=((AiArmyRegistry)Field(owner,"armies")).Capture(),Pending=((System.Collections.IEnumerable)Field(owner,"pendingItems")).Cast<object>().Select(x=>new{Action=(PlayableAiAction)Field(x,"Action"),Policy=(string)Field(x,"PolicyName")}),Records=a.CaptureDiagnosticCheckpoints().Single().Records}));
                    Assert.NotNull(pending,"Actual policy must schedule a reinforcement Move.");
                    var action=(PlayableAiAction)Field(pending,"Action");long due=(long)Field(pending,"DueTick");
                    while(a.Tick<due-1)Advance();
                    Assert.True(CurrentOffenseReady(),"Capture must retain the live hostile target, original offensive event and current viable certified core");
                    Assert.AreEqual(originalArmy,planner.Capture().ArmyId);Assert.AreEqual(originalTarget,planner.Capture().TargetId);
                    Assert.AreEqual(originalArmy,registry.ArmyFor(wave));Assert.Contains(wave,registry.Capture().Armies.Single(g=>g.Id==originalArmy).Reinforcements);
                    using(var state=a.CaptureWorldState(a.PublicationSequence,_=>0)){
                        // A current supported transport envelope around this exact live authority.
                        state.SetTransportCapture(w=>{w.Write(3);w.Write(false);w.Write(false);w.Write(0L);w.Write(0);w.Write(0L);w.Write(0);w.Write(0);w.Write(0);w.Write(false);});
                        runtime=PlayableRuntime.RestoreBytesForTests(state.Encode(),c,d=>{d.BeforeArmySafetyPathForTests=(pendingAction,actor)=>{if(pendingAction.ActionId==action.ActionId&&Interlocked.CompareExchange(ref parked,1,0)==0){entered.Set();if(!release.Wait(10000))throw new TimeoutException("Safety path boundary not released.");}};});
                    }
                    domain=(PlayableDomain)Field(runtime,"domain");
                    worker=new CertifiedRouteWorker(runtime.NavigationBinding,4,new CertifiedRouteKernel(),()=>runtime.IsStopRequested,runtime.ReportRouteFailure);
                    runtime.RequestPause(false);bool reached=entered.Wait(15000);
                    if(!reached)TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{SafetyProbeMiss=true,completion,Action=action,Snapshot=runtime.Latest.Tick,Failure=runtime.Latest.Failure,Checkpoint=runtime.AiCheckpoint,Actor=runtime.ParticipantView("player-1").Entities.FirstOrDefault(u=>u.Id==wave)}));
                    Assert.True(reached,"Real due delivery must reach its safety solver.");long tick=runtime.Latest.Tick;
                    Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Tick>=tick+10||runtime.IsStopped,2000),"A delayed delivery safety solve must not stop world time.");
                    Assert.False(runtime.IsStopped,runtime.Latest.Failure);
                    Assert.False(runtime.AiCheckpoint.Records.Any(r=>r.ActionId==action.ActionId&&r.Status!=PlayableAiDeliveryStatus.Scheduled),"Pending is not unsafe or unreachable.");
                    Assert.AreEqual(action.ActionId,runtime.AiCheckpoint.PendingActionId,"Keep the exact original action while its current safety path is pending.");
                    if(completion==2){
                        runtime.RequestStop();Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped,5000),"Stopping the world must not wait for the parked safety solver.");Assert.Zero(runtime.Latest.Metrics.Errors);return;
                    }
                    if(completion==0){
                        var origin=runtime.ParticipantView("player-1").Entities.Single(u=>u.Id==wave).Position;release.Set();
                        Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped||runtime.AiCheckpoint.Records.Any(r=>r.ActionId==action.ActionId&&r.Status==PlayableAiDeliveryStatus.Applied)&&runtime.ParticipantView("player-1").Entities.Any(u=>u.Id==wave&&!u.Position.Equals(origin)&&u.OrderStamp?.Origin==PlayableOrderOrigin.Ai),15000),"The released original action must apply and produce real AI-controlled displacement.");
                        Assert.False(runtime.IsStopped,runtime.Latest.Failure);Assert.Zero(runtime.Latest.Metrics.Errors);return;
                    }
                    runtime.RequestPause(true);Assert.True(SpinWait.SpinUntil(()=>runtime.Latest.Paused||runtime.IsStopped,5000));Assert.False(runtime.IsStopped,runtime.Latest.Failure);
                    var capture=runtime.RequestCaptureBytes();Assert.False(capture.Wait(50),"Explicit save must settle pending safety work.");release.Set();Assert.True(capture.Wait(10000));
                    restored=PlayableRuntime.RestoreBytes(capture.Result,c);restoredDomain=(PlayableDomain)Field(restored,"domain");
                    Assert.True(SpinWait.SpinUntil(()=>restored.Latest.Paused||restored.IsStopped,5000));Assert.False(restored.IsStopped,restored.Latest.Failure);Assert.AreEqual(runtime.Latest.Tick,restored.Latest.Tick);
                    runtime.RequestPause(false);
                    Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped||runtime.AiCheckpoint.Records.Any(r=>r.ActionId==action.ActionId&&r.Status!=PlayableAiDeliveryStatus.Scheduled),15000),"Released action must receive an explicit terminal result.");
                    Assert.False(runtime.IsStopped,runtime.Latest.Failure);Assert.Zero(runtime.Latest.Metrics.Errors);
                }
            }finally{
                release.Set();restored?.RequestStop();runtime?.RequestStop();worker?.Dispose();
                if(restored!=null){Assert.True(SpinWait.SpinUntil(()=>restored.IsStopped&&restoredDomain.ObservedPathWorkerStopped,5000));restored.Dispose();restoredDomain.DestroyOwnedNavigation();}
                if(runtime!=null){Assert.True(SpinWait.SpinUntil(()=>runtime.IsStopped&&(worker==null||worker.IsStopped)&&domain.ObservedPathWorkerStopped,5000));runtime.Dispose();domain.DestroyOwnedNavigation();}
                a.Stop();source.DestroyOwnedNavigation();
            }
        }
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)] public void Reinforcement_ActualDueDeliveryRechecksMovingVisibleThreatOutsideBase(bool intrusion,bool follow)
        {
            var c=A4NativeConfig(intrusion:intrusion,follow:follow);var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();var domain=Field(a,"domain");var registry=(AiArmyRegistry)Field(owner,"armies");var planner=(AiArmyPlanner)Field(owner,"armyPlanner");
            using(var routes=new UnityHostRouteService())
            {
                for(int i=0;i<300&&(!planner.Active||planner.Capture().TargetKind==PlayableRouteTargetKind.FriendlyAnchor||!planner.HasAppliedOrder);i++)NativeStep(a,routes);
                Assert.True(planner.Active);domain.GetType().GetMethod("SpawnProduced",F).Invoke(domain,new object[]{new NavPoint(-12,-8),(NavPoint?)null,PlayableOwner.Player,PlayableEntityKind.Tank});
                int wave=a.ParticipantView("player-1").Entities.Where(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Tank).Max(u=>u.Id);object pending=null;
                int bound=follow?(int)AiProfile.SecondsToTicks(AiProfile.Initial.Value("armies.assemblyDeadlineSeconds"),30):150;
                for(int i=0;i<bound&&pending==null;i++)
                {NativeStep(a,routes);pending=((System.Collections.IEnumerable)Field(owner,"pendingItems")).Cast<object>().FirstOrDefault(x=>(string)Field(x,"PolicyName")==AiArmyPlanner.Policy&&((PlayableAiAction)Field(x,"Action")).Kind==(follow?PlayableCommandKind.Follow:PlayableCommandKind.Move)&&((PlayableAiAction)Field(x,"Action")).EntityIds.Contains(wave));}
                if(pending==null)TestContext.WriteLine("Missing native leg "+Newtonsoft.Json.JsonConvert.SerializeObject(new{a.Tick,Wave=a.ParticipantView("player-1").Entities.Single(u=>u.Id==wave),Mission=planner.Capture(),Registry=registry.Capture(),Records=a.CaptureDiagnosticCheckpoints().Single().Records}));
                Assert.NotNull(pending,"A real safe gather Move must be scheduled before the threat moves");var action=(PlayableAiAction)Field(pending,"Action");long due=(long)Field(pending,"DueTick");
                Assert.True((bool)domain.GetType().GetMethod("ArmyMoveSafe",F).Invoke(domain,new object[]{action}),"Initial accepted path must actually be safe");
                if(intrusion)
                {
                    var enemy=a.ParticipantView("enemy-1").Entities.Single(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank);
                    Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,"enemy-1",PlayableCommandKind.Move,new[]{enemy.Id},follow?new NavPoint(2,-10):new NavPoint(-4.5,-8))).Status);
                }
                var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                while(a.Tick<due){NativeStep(a,routes);NativeStep(b,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                var before=PlayableAiObservation.From(a.ParticipantView("player-1"));bool safe=(bool)domain.GetType().GetMethod("ArmyMoveSafe",F).Invoke(domain,new object[]{action});
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{BeforeDue=a.Tick,Intrusion=intrusion,Safe=safe,FollowFixtureRebound=follow,OriginalFollowIntruderZ=-23.5,AuthoredFollowIntruder=c.ScenarioUnits.Where(u=>u.LogicalPlayer==2&&u.Kind==PlayableEntityKind.Tank),Entities=before.Entities,Action=action}));
                if(intrusion){Assert.True(before.Entities.Any(u=>u.Owner==PlayableOwner.Enemy&&u.Kind==PlayableEntityKind.Tank),"Intruder must actually be owner-visible before delivery");Assert.IsEmpty(AiDefensePlanner.Threats(before,c.Profile),"Intrusion must be outside every center's range before due");Assert.False(safe,"Actual trajectory must enter the path's unsafe range before due");}
                while(a.Tick<=due+1){NativeStep(a,routes);NativeStep(b,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                var terminal=a.CaptureDiagnosticCheckpoints().Single().Records.Single(r=>r.ActionId==action.ActionId&&r.Status!=PlayableAiDeliveryStatus.Scheduled);
                TestContext.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new{intrusion,due,a.Tick,Action=action,Terminal=terminal,View=a.ParticipantView("player-1").Entities}));
                Assert.AreEqual(intrusion?PlayableAiDeliveryStatus.Cancelled:PlayableAiDeliveryStatus.Applied,terminal.Status);
                if(intrusion){StringAssert.Contains("Current observed army path",terminal.Message);Assert.Zero(terminal.CommandSequence);Assert.IsEmpty(AiDefensePlanner.Threats(PlayableAiObservation.From(a.ParticipantView("player-1")),c.Profile),"This is an outside-base path intrusion, not the center-defense guard");}
                Assert.False(a.CaptureDiagnosticBudgets().Single().Unpaid.Any(x=>x.Receipt.Id==terminal.ReceiptIdentity.Id));Assert.AreEqual(planner.Capture().ArmyId,registry.ArmyFor(wave));b.Stop();
            }
            a.Stop();
        }
        [Test] public void Artillery_ActualMixedArmyDefensePreemptsPendingOffenseAndRestores()
        {
            var c=A4NativeConfig(artillery:true);var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();var domain=Field(a,"domain");var registry=(AiArmyRegistry)Field(owner,"armies");var planner=(AiArmyPlanner)Field(owner,"armyPlanner");int gun=a.ParticipantView("player-1").Entities.Single(u=>u.Owner==PlayableOwner.Player&&u.Kind==PlayableEntityKind.Shkval).Id;
            using(var routes=new UnityHostRouteService())
            {
                for(int i=0;i<120&&registry.ArmyFor(gun)==0;i++)NativeStep(a,routes);long id=registry.ArmyFor(gun);Assert.Greater(id,0);Assert.AreEqual(id,planner.Capture().ArmyId);
                var old=((System.Collections.IEnumerable)Field(owner,"pendingItems")).Cast<object>().Single(x=>(string)Field(x,"PolicyName")==AiArmyPlanner.Policy);var oldAction=(PlayableAiAction)Field(old,"Action");
                int enemy=(int)domain.GetType().GetMethod("SpawnUnit",F).Invoke(domain,new object[]{new NavPoint(-9,0),PlayableOwner.Enemy,PlayableEntityKind.Tank});
                NativeStep(a,routes);var checkpoint=a.CaptureDiagnosticCheckpoints().Single();Assert.True(checkpoint.Records.Any(r=>r.ActionId==oldAction.ActionId&&r.Status==PlayableAiDeliveryStatus.Cancelled));
                var defense=(AiDefensePlanner)Field(owner,"defense");Assert.True(defense.Active);Assert.AreEqual(id,defense.ArmyId);Assert.AreEqual(id,registry.ArmyFor(gun));Assert.AreEqual(AiArmyRole.MobileDefense,registry.Capture().Armies.Single(g=>g.Id==id).Role);
                var b=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());long tick=a.Tick;
                for(int i=0;i<20;i++){Assert.True(a.TryAdvance(paused:true));Assert.True(b.TryAdvance(paused:true));}Assert.AreEqual(tick,a.Tick);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());
                for(int i=0;i<100&&!a.CaptureDiagnosticCheckpoints().Single().Records.Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied);i++){NativeStep(a,routes);NativeStep(b,routes);ExactByteAssert.AreEqual(a.CaptureBytes(),b.CaptureBytes());}
                Assert.True(a.CaptureDiagnosticCheckpoints().Single().Records.Any(r=>r.Policy==AiDefensePlanner.Policy&&r.Status==PlayableAiDeliveryStatus.Applied));var support=a.ParticipantView("player-1").Entities.Single(u=>u.Id==gun);Assert.AreEqual(enemy,support.CurrentOrder.TargetId);Assert.AreEqual(id,registry.ArmyFor(gun));
                Assert.False(a.CaptureDiagnosticCheckpoints().Single().Records.Any(r=>r.Policy=="artillery"&&(r.Kind==PlayableCommandKind.Move||r.Kind==PlayableCommandKind.Attack)));
                var ai=AiProfile.Initial.CopyData();ai.revision++;var next=new AiProfile(ai);Assert.IsNull(a.ApplyProfile(c.Profile,next));var rebound=PlayableAuthorityTick.RestoreBytes(a.CaptureBytes(),c,next);ExactByteAssert.AreEqual(a.CaptureBytes(),rebound.CaptureBytes());rebound.Stop();b.Stop();
            }
            a.Stop();Assert.Zero(registry.ArmyFor(gun));
        }
        [Test] public void Reinforcement_ActualDangerousFrontUsesReachableRearGatherAnchor()
        {
            var c=A4NativeConfig();var a=new PlayableAuthorityTick(c,71);var owner=Owners(a).Single();var registry=(AiArmyRegistry)Field(owner,"armies");var domain=Field(a,"domain");var type=domain.GetType();
            int Spawn(PlayableOwner who,NavPoint at,PlayableEntityKind kind)=>(int)type.GetMethod("SpawnUnit",F).Invoke(domain,new object[]{at,who,kind});
            var core=new[]{Spawn(PlayableOwner.Player,new NavPoint(0,-20),PlayableEntityKind.Tank),Spawn(PlayableOwner.Player,new NavPoint(2,-20),PlayableEntityKind.Tank),Spawn(PlayableOwner.Player,new NavPoint(4,-20),PlayableEntityKind.Tank)};
            Spawn(PlayableOwner.Enemy,new NavPoint(6,-20),PlayableEntityKind.Tank);int wave=Spawn(PlayableOwner.Player,new NavPoint(-16,-14),PlayableEntityKind.Tank);
            var first=PlayableAiObservation.From(a.ParticipantView("player-1"));Assert.True(registry.TryCreate(first,AiArmyRole.Attack,AiArmyPlanner.Policy,core,out var id));Assert.True(registry.TryReinforce(first,id,AiArmyPlanner.Policy,new[]{wave}));
            var snapshot=(PlayableSnapshot)type.GetMethod("PlayerSnapshotForArmyAi",F).Invoke(domain,new object[]{1L,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,c.Seed,PlayableOwner.Player,true,registry.Capture()});var o=PlayableAiObservation.From(snapshot);
            Assert.True(o.RouteProofs.Any(r=>r.UnitId==wave&&core.Contains(r.TargetId)),"Actual current native front route exists but is unsafe");
            Assert.False(o.RouteProofs.Where(r=>r.UnitId==wave&&core.Contains(r.TargetId)).Any(r=>AiTacticalExecutor.SafeRoute(o,r,c.Profile)));
            var action=AiTacticalExecutor.Propose(o,registry.Capture().Armies.Single(),c.Profile,1);Assert.NotNull(action);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);Assert.Less(action.Target.X,-18,"Move must gather behind the front at a ready home/production approach");
            var proof=o.RouteProofs.Single(r=>r.UnitId==wave&&r.Goal.Equals(action.Target));Assert.True(o.Buildings.Any(b=>b.Id==proof.TargetId&&b.Owner==o.Owner));Assert.True(AiTacticalExecutor.SafeRoute(o,proof,c.Profile));
            Assert.AreEqual(PlayableCommandStatus.Applied,a.Apply(new PlayableCommand(71,1,"player-1",action.Kind,action.CopyEntityIds(),action.Target)).Status);
            using(var routes=new UnityHostRouteService())for(int i=0;i<240;i++){NativeStep(a,routes);if(a.ParticipantView("player-1").Entities.Single(u=>u.Id==wave).NavigationOutcome==NavigationOutcome.Arrived)break;}
            Assert.AreEqual(NavigationOutcome.Arrived,a.ParticipantView("player-1").Entities.Single(u=>u.Id==wave).NavigationOutcome);Assert.AreEqual(id,registry.ArmyFor(wave));a.Stop();
        }
    }
}

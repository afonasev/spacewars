using System;
using System.Linq;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiRelayObjectiveTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile Ai=AiProfile.Initial;
        static readonly NavPoint Target=new NavPoint(5,0);
        static readonly TerritorySite Site=new TerritorySite(4,PlayableBuildingKind.RelayTower,new NavPoint(0,0),Array.Empty<TerritorySlot>());
        static PlayableEntitySnapshot Unit(int id,PlayableOwner owner,NavPoint position,PlayableEntityKind kind=PlayableEntityKind.Explorer,int hp=100,bool held=false)=>
            new PlayableEntitySnapshot(id,owner,kind,position,hp,false,0,0,0,held:held);
        [Test]
        public void FormationReservesSeparatedRelaySlotsOnTheRequestedSide()
        {
            var center = new NavPoint(40, 0);
            var target = new NavPoint(46.4, 0);
            var direction = new NavPoint(1, 0);
            var units = Enumerable.Range(39, 3).Select(i => new NavFormationLayout.Member(
                i, PlayableEntityKind.Explorer, new NavPoint(30, i - 40), .65)).ToArray();
            bool Clear(int _, NavPoint point) => point.X <= 48 && point.X >= 42;
            bool Separated(int _, NavPoint point, int __, NavPoint previous) =>
                NavFormationLayout.Separated(point, previous, 2);
            var ordinary = NavFormationLayout.Destinations(units, target, direction, 3, 80, Clear, Separated);
            Assert.IsTrue(ordinary.Any(point => AiOpponentSelector.Distance(point, center) > 7));
            var relay = NavFormationLayout.Destinations(units, target, direction, 3, 80, Clear, Separated,
                objective: (center, 7 - P.Navigation.ArrivalTolerance));
            Assert.AreEqual(units.Length, relay.Length);
            Assert.IsTrue(relay.All(point => point.X > center.X && AiOpponentSelector.Distance(point, center) < 7));
            for (int i = 0; i < relay.Length; i++)
                for (int j = 0; j < i; j++)
                    Assert.IsTrue(Separated(i, relay[i], j, relay[j]));
        }

        static PlayableAiObservation View(long tick=30,PlayableOwner? owner=null,PlayableOwner? claimant=null,bool contested=false,
            PlayableEntitySnapshot[] units=null,bool relay=true,bool visible=true,double progress=0,PlayableDominationSnapshot? domination=null)
        {
            units=units??new[]{Unit(13,PlayableOwner.Player,new NavPoint(-10,0))};
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Human),new OfflineParticipant("ally",3,1,OfflineControl.Human)};
            var objective=new PlayablePublicScoutObjective(4,Target,relay?PlayablePublicScoutObjectiveRole.Relay:PlayablePublicScoutObjectiveRole.ExpansionIncome,true);
            var vision=new PlayableVision(0,64,64,1,1);
            vision.Refresh(visible?units.Where(u=>u.Owner==PlayableOwner.Player).Select(u=>new VisionSource(u.Position,P.ExplorerVision)):Array.Empty<VisionSource>(),Array.Empty<KnownBuilding>());
            return PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,7,tick,tick,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,0,null,units,
                Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                sites:visible?new[]{new TerritorySiteSnapshot(Site,claimant,owner,progress,contested,0,owner.HasValue)}:Array.Empty<TerritorySiteSnapshot>(),
                publicScoutObjectives:new[]{objective},routeProofs:units.Where(u=>u.Owner==PlayableOwner.Player).Select(u=>new PlayableRouteProof(u.Id,PlayableOwner.Player,71,tick,1,u.Position,PlayableUnitRules.Radius(P,u.Kind),PlayableRouteTargetKind.PublicObjective,4,Target,Target,new[]{Target})).ToArray(),
                vision:vision.Snapshot(),activeProfile:P,ownerId:"player-1",participants:roster,team:1,
                mapRelayControl:new[]{new KnownRelay(Site.Id,owner)},domination:domination,publicSites:new[]{Site}));
        }
        static AiKnowledgeState Knowledge(PlayableAiObservation o){var k=new AiKnowledgeTracker(o.OwnerId,o.Generation);k.Observe(o,Ai);return k.Capture();}
        static AiArmyRegistry Registry()=>new AiArmyRegistry("player-1",71,Ai,AiDifficulty.Fighter);
        static PlayableAiAction Plan(PlayableAiObservation o,AiArmyRegistry r)=>new AiScoutPlanner().Plan(o,Knowledge(o),r,P,Ai,1);
        [TestCase(1,PlayableOwner.Third)]
        [TestCase(2,PlayableOwner.Enemy)]
        public void ImminentDominationUsesPublicControlAndExcludesHeldUnits(int team,PlayableOwner towerOwner)
        {
            var units=new[]{Unit(13,PlayableOwner.Player,new NavPoint(-10,0),PlayableEntityKind.Tank),
                Unit(14,PlayableOwner.Player,new NavPoint(-12,0),PlayableEntityKind.Tank,held:true)};
            var before=View(owner:towerOwner,visible:false,units:units,domination:new PlayableDominationSnapshot(team,team,25,175));
            var now=View(owner:towerOwner,visible:false,units:units,domination:new PlayableDominationSnapshot(team,team,110,90));
            Assert.IsNull(AiRelayObjectives.PlanDomination(before,P,Registry()));
            Assert.Greater(AiRelayObjectives.DominationPreference(now,P,Site.Id),AiRelayObjectives.DominationPreference(before,P,Site.Id));
            var action=AiRelayObjectives.PlanDomination(now,P,Registry());
            Assert.That(action?.SiteId,Is.EqualTo(Site.Id));
            CollectionAssert.AreEqual(new[]{13},action.EntityIds);
            Assert.AreEqual(PlayableCommandKind.AttackMove,action.Kind);
        }
        [TestCase(true,false)]
        [TestCase(false,true)]
        [TestCase(false,false)]
        public void LiveRelayApproachKeepsItsActorWhenReinforcementBecomesAvailable(bool pending,bool moving)
        {
            var outsideCircle=new NavPoint(P.RelayCaptureRadius+10,0);
            var order=new PlayableQueuedOrderSnapshot(1,1,1,1,PlayableCommandKind.AttackMove,new NavLocation(Target,"ground"),0,Site.Id,17);
            var queue=new PlayableUnitQueueSnapshot(pending?null:order,pending?order:null,Array.Empty<PlayableQueuedOrderSnapshot>(),null,pending);
            var assigned=new PlayableEntitySnapshot(13,PlayableOwner.Player,PlayableEntityKind.Tank,outsideCircle,100,moving,0,0,0,
                currentOrder:pending?null:new PlayableTacticalOrderSnapshot(13,PlayableOwner.Player,71,1,20,
                    PlayableTacticalOrderKind.AttackMove,outsideCircle,0),
                navigationOutcome:pending?NavigationOutcome.Idle:NavigationOutcome.Moving,queue:queue);
            var arrival=Unit(14,PlayableOwner.Player,new NavPoint(-12,0),PlayableEntityKind.Tank);
            var view=View(owner:PlayableOwner.Enemy,progress:1,units:new[]{assigned,arrival},
                domination:new PlayableDominationSnapshot(2,2,110,90));
            var arbiter=new AiDecisionArbiter(Ai);
            var assault=arbiter.RelayAssault(view.Tick);
            assault.Begin(Site.Id,Target,20);
            arbiter.SaveRelayAssault(assault);
            Assert.That(AiRelayObjectives.CommittedRecipients(view,arbiter),Is.EquivalentTo(new[]{13}));
            var action=AiRelayObjectives.PlanDomination(view,P,Registry(),arbiter:arbiter);
            Assert.That(action?.EntityIds,Is.EqualTo(new[]{14}));
        }

        [Test]
        public void ScoutSelectionLeavesAReservedActorAndUsesAFreeExplorer()
        {
            var observation=View(units:new[]{Unit(13,PlayableOwner.Player,new NavPoint(-10,0)),
                Unit(14,PlayableOwner.Player,new NavPoint(-12,0))});
            var plan=new AiScoutPlanner().Plan(observation,Knowledge(observation),Registry(),P,Ai,1,
                reservedActors:new System.Collections.Generic.HashSet<int>{13});
            Assert.That(plan?.EntityIds,Is.EqualTo(new[]{14}));
        }

        [Test]
        public void GroundSupportRecognizesAnOpposingTowerDuringATie()
        {
            var tied=View(owner:PlayableOwner.Enemy,domination:new PlayableDominationSnapshot(0,1,10,190));
            Assert.True(AiRelayObjectives.NeedsGroundSupport(tied,P,Site.Id));
            Assert.False(AiRelayObjectives.NeedsGroundSupport(View(owner:PlayableOwner.Enemy),P,Site.Id));
            Assert.False(AiRelayObjectives.NeedsGroundSupport(View(domination:tied.Domination),P,Site.Id));
        }

        [Test]
        public void AttackerStartsBeforeWarningWhenApproachAndNeutralizationTakeTime()
        {
            var state=new PlayableDominationSnapshot(2,2,104,96);
            var view=View(owner:PlayableOwner.Enemy,progress:1,domination:state);
            Assert.False(AiRelayObjectives.Urgent(view));
            Assert.AreEqual(Site.Id,AiRelayObjectives.PlanDomination(view,P,Registry())?.SiteId);
        }

        [Test]
        public void ArrivedOutsideCircleReceivesAnotherApproachAndStallSurvivesRestore()
        {
            var unit=new PlayableEntitySnapshot(13,PlayableOwner.Player,PlayableEntityKind.Tank,new NavPoint(10,0),100,false,0,0,0,
                currentOrder:new PlayableTacticalOrderSnapshot(13,PlayableOwner.Player,71,10,10,PlayableTacticalOrderKind.AttackMove,Target,0),
                navigationOutcome:NavigationOutcome.Arrived);
            var view=View(owner:PlayableOwner.Enemy,progress:1,units:new[]{unit},domination:new PlayableDominationSnapshot(2,2,110,90));
            Assert.NotNull(AiRelayObjectives.PlanDomination(view,P,Registry()),"Arrival outside the physical circle must not reserve the actor forever.");
            var state=new AiRelayAssault();state.Begin(Site.Id,Target,10);
            var arbiter=new AiDecisionArbiter(Ai);
            Assert.True(state.Observe(view,P,Ai,arbiter));
            arbiter.SaveRelayAssault(state);
            Assert.True(arbiter.RejectedDominationApproaches(view.Tick)(Site.Id,Target));
            var bytes=WorldWire.Pack(arbiter.WriteState);
            var restored=new AiDecisionArbiter(Ai);
            using var reader=new System.IO.BinaryReader(new System.IO.MemoryStream(bytes));
            restored.ReadState(reader,view.Tick,"player-1",71,P,Ai);
            CollectionAssert.AreEqual(bytes,WorldWire.Pack(restored.WriteState));
            Assert.AreEqual(Site.Id,restored.RelayAssault(view.Tick).Site);
        }

        [Test]
        public void ContestedCircleRetriesAfterNoCaptureOrCombatProgress()
        {
            PlayableAiObservation Observe(long tick,double progress)=>View(tick:tick,owner:PlayableOwner.Enemy,
                contested:true,progress:progress,units:new[]{new PlayableEntitySnapshot(13,PlayableOwner.Player,PlayableEntityKind.Tank,
                    new NavPoint(5,0),100,false,0,0,0,currentOrder:new PlayableTacticalOrderSnapshot(13,PlayableOwner.Player,71,10,10,PlayableTacticalOrderKind.AttackMove,Target,0))},
                domination:new PlayableDominationSnapshot(2,2,110,90));
            var state=new AiRelayAssault();state.Begin(Site.Id,Target,10);
            var arbiter=new AiDecisionArbiter(Ai);
            Assert.False(state.Observe(Observe(30,1),P,Ai,arbiter));
            long stall=AiProfile.SecondsToTicks(Ai.Value("armies.stallSeconds"),30);
            Assert.False(state.Observe(Observe(30+stall-1,1),P,Ai,arbiter));
            Assert.True(state.Observe(Observe(30+stall,1),P,Ai,arbiter));
            Assert.False(state.Observe(Observe(31+stall,.9),P,Ai,arbiter),"Actual neutralization refreshes the progress clock.");
        }

        [Test]
        public void AlliedRelayBidsPreferAnOnTimeAssaultAndBoundAssemblyByVictory()
        {
            AiTeamPublication Publish(string owner,double arrival)=>new AiTeamPublication{
                Owner=owner,Team=1,Tick=100,RelaySeconds=12,Mission=new AiArmyMissionState(),
                Targets=Array.Empty<AiOpponentAssessment>(),RequestedSites=Array.Empty<int>(),RelayBids=new[]{
                    new AiRelayBid{Site=4,Tick=100,Started=90,Arrival=arrival,Capture=3,Force=50,Resistance=70,Breaks=true},
                    new AiRelayBid{Site=5,Tick=100,Started=90,Arrival=30,Capture=3,Force=50,Resistance=5,Breaks=true}}};
            var board=AiTeamCoordinator.Resolve(new[]{Publish("player-1",2),Publish("ally",8)},Ai);
            Assert.AreEqual(4,board["player-1"].RelaySite);
            Assert.AreEqual(4,board["ally"].RelaySite);
            Assert.LessOrEqual(board["player-1"].RelayDeparture,100+(12-2-3)*30);
            Assert.LessOrEqual(board["ally"].RelayDeparture,100);
            var endangered=Publish("ally",8);endangered.HomeThreatened=true;
            Assert.Zero(AiTeamCoordinator.Resolve(new[]{Publish("player-1",2),endangered},Ai)["player-1"].RelaySite);
        }

        [TestCase(true,8)]
        [TestCase(false,5)]
        public void FastNeutralCaptureWinsSelectionOnlyIfItBreaksTheLead(bool ourTeamAlreadyHoldsOne,int expected)
        {
            var unit=Unit(13,PlayableOwner.Player,new NavPoint(-10,0));
            var sites=new[]{5,6,7,8}.Select(id=>new TerritorySite(id,PlayableBuildingKind.RelayTower,
                new NavPoint(id==8?-1:id*4,0),Array.Empty<TerritorySlot>())).ToArray();
            var ownership=new PlayableOwner?[]{PlayableOwner.Enemy,PlayableOwner.Enemy,
                ourTeamAlreadyHoldsOne?PlayableOwner.Player:(PlayableOwner?)null,null};
            var roster=new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("enemy-1",2,2,OfflineControl.Ai)};
            var vision=new PlayableVision(0,64,64,1,1);
            vision.Refresh(new[]{new VisionSource(unit.Position,64)},Array.Empty<KnownBuilding>());
            var objectives=sites.Select(site=>new PlayablePublicScoutObjective(site.Id,site.Position,PlayablePublicScoutObjectiveRole.Relay,true)).ToArray();
            var proofs=objectives.Where(site=>site.SiteId==5||site.SiteId==8).Select(site=>new PlayableRouteProof(unit.Id,unit.Owner,71,30,1,
                unit.Position,P.ExplorerCollisionRadius,PlayableRouteTargetKind.PublicObjective,site.SiteId,site.Approach,site.Approach,new[]{site.Approach})).ToArray();
            var view=PlayableAiObservation.From(new PlayableSnapshot(P.ProfileId,P.Revision,71,7,30,30,RuntimeStatus.Running,false,
                PlayableMatchOutcome.Playing,0,null,new[]{unit},Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),
                new PlayableRuntimeMetrics(0,0,0,0,0),null,publicScoutObjectives:objectives,routeProofs:proofs,
                vision:vision.Snapshot(),activeProfile:P,ownerId:"player-1",team:1,participants:roster,publicSites:sites,
                mapRelayControl:sites.Select((site,index)=>new KnownRelay(site.Id,ownership[index])).ToArray(),
                domination:new PlayableDominationSnapshot(2,2,110,90)));
            Assert.AreEqual(expected,AiRelayObjectives.PlanDomination(view,P,Registry())?.SiteId);
        }

        [Test]
        public void RejectedRelayApproachChangesTheCertifiedGoalAndSurvivesRestore()
        {
            var config=RelayTowerContractTests.Configuration(P,ai:true,domination:true);
            var domain=new PlayableDomain(P,71,config);
            var arbiter=new AiDecisionArbiter(Ai);
            try
            {
                PlayableSnapshot Projection(Func<int,NavPoint,bool> rejected=null)=>domain.PlayerSnapshotForAiReview(
                    0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,71,PlayableOwner.Player,
                    true,Registry().Capture(),false,rejectedRelayApproach:rejected);
                var initial=Projection().RouteProofs.First(route=>route.Kind==PlayableRouteTargetKind.PublicObjective&&route.TargetId==Site.Id);
                arbiter.RejectDominationApproach(initial.TargetId,initial.Goal,0,90);
                var bytes=WorldWire.Pack(arbiter.WriteState);
                var restored=new AiDecisionArbiter(Ai);
                using var reader=new System.IO.BinaryReader(new System.IO.MemoryStream(bytes));
                restored.ReadState(reader,0,"player-1",71,P,Ai);
                CollectionAssert.AreEqual(bytes,WorldWire.Pack(restored.WriteState));
                var next=Projection(restored.RejectedDominationApproaches(0)).RouteProofs.Single(route=>
                    route.Kind==PlayableRouteTargetKind.PublicObjective&&route.TargetId==Site.Id&&route.UnitId==initial.UnitId);
                Assert.AreEqual(initial.Target,next.Target);
                Assert.AreNotEqual(initial.Goal,next.Goal);
                Assert.LessOrEqual(AiOpponentSelector.Distance(next.Goal,Site.Position),P.RelayCaptureRadius);
                Assert.IsNull(restored.RejectedDominationApproaches(90));
            }
            finally {domain.Navigation.ReleaseCommittedHostRoot();domain.DestroyOwnedNavigation();}
        }

        [Test] public void NeutralAndEnemyRelaysUseOrdinaryCaptureMission()
        {
            foreach(var owner in new PlayableOwner?[]{null,PlayableOwner.Enemy})
            {var o=View(owner:owner);var r=Registry();var action=Plan(o,r);Assert.NotNull(action);Assert.AreEqual(4,action.SiteId);Assert.AreEqual(PlayableCommandKind.Move,action.Kind);
             new AiScoutPlanner().Commit(o,action,r);Assert.AreEqual("relay:4",r.Capture().Armies.Single().Objective);Assert.AreEqual(0,o.Credits);}
        }
        [Test] public void ExistingRelayMissionKeepsItsActorWhenAFreeExplorerIsCloserAtCapacity()
        {
            var planner=new AiScoutPlanner();var r=Registry();var start=View();planner.Commit(start,Plan(start,r),r);
            // Fill the actual difficulty cap, rather than changing the quota.
            int cap=(int)Ai.DifficultyValue(AiDifficulty.Fighter,"scoutAssignments");
            var free=Unit(99,PlayableOwner.Player,new NavPoint(4,0));
            var others=Enumerable.Range(0,cap-1).Select(i=>Unit(30+i,PlayableOwner.Player,new NavPoint(-20,i))).ToArray();
            var current=View(tick:60,units:new[]{Unit(13,PlayableOwner.Player,new NavPoint(-10,0)),free}.Concat(others).ToArray());
            foreach(var u in others)Assert.True(r.TryCreate(current,AiArmyRole.Scout,"scout",new[]{u.Id},out _));
            Assert.AreEqual(cap,r.Capture().Armies.Count(a=>a.Role==AiArmyRole.Scout&&a.Phase!=AiArmyPhase.Disbanded));
            var action=planner.Plan(current,Knowledge(current),r,P,Ai,cap);
            Assert.NotNull(action);CollectionAssert.AreEqual(new[]{13},action.EntityIds);
            Assert.DoesNotThrow(()=>planner.Commit(current,action,r));Assert.Zero(r.ArmyFor(99));
            var restored=AiArmyRegistry.Restore(r.Capture(),Ai,AiDifficulty.Fighter,60);
            CollectionAssert.AreEqual(action.EntityIds,planner.Plan(current,Knowledge(current),restored,P,Ai,cap).EntityIds);
        }
        [Test] public void ArrivalKeepsClaimUntilCaptureAndCurrentRegistryRestores()
        {
            var r=Registry();var planner=new AiScoutPlanner();var start=View();planner.Commit(start,Plan(start,r),r);
            var during=View(tick:60,claimant:PlayableOwner.Player,progress:.5,units:new[]{Unit(13,PlayableOwner.Player,Target)});
            planner.Observe(during,Knowledge(during),r,Ai,1,gameplay:P);Assert.Greater(r.ArmyFor(13),0);Assert.IsNull(Plan(during,r));
            var restored=AiArmyRegistry.Restore(r.Capture(),Ai,AiDifficulty.Fighter,60);Assert.AreEqual(r.ArmyFor(13),restored.ArmyFor(13));
            var done=View(tick:90,owner:PlayableOwner.Player,claimant:PlayableOwner.Player,progress:1,units:new[]{Unit(13,PlayableOwner.Player,Target)});
            planner.Observe(done,Knowledge(done),r,Ai,1,gameplay:P);Assert.Zero(r.ArmyFor(13));Assert.IsNull(Plan(done,r));
        }
        [Test] public void AlliedSafeControlOrClaimIsNotStolenButThreatGetsHelp()
        {
            Assert.IsNull(Plan(View(owner:PlayableOwner.Third,claimant:PlayableOwner.Third),Registry()));
            Assert.IsNull(Plan(View(claimant:PlayableOwner.Third,progress:.5),Registry()));
            var threatened=View(owner:PlayableOwner.Third,claimant:PlayableOwner.Third,contested:true,units:new[]{Unit(13,PlayableOwner.Player,new NavPoint(-10,0)),Unit(20,PlayableOwner.Enemy,new NavPoint(0,2),hp:1),Unit(21,PlayableOwner.Third,new NavPoint(0,-2))});
            Assert.NotNull(Plan(threatened,Registry()),"Known viable allied defense is useful even though the relay cannot change to our ownership");
        }
        [Test] public void LosingContestIsRejectedAndHumanHoldIsPreserved()
        {
            var enemies=Enumerable.Range(20,8).Select(i=>Unit(i,PlayableOwner.Enemy,new NavPoint(i-20,2),PlayableEntityKind.Tank)).ToArray();
            var losing=View(contested:true,units:new[]{Unit(13,PlayableOwner.Player,new NavPoint(-10,0))}.Concat(enemies).ToArray());
            Assert.IsNull(Plan(losing,Registry()));Assert.IsNull(Plan(View(units:new[]{Unit(13,PlayableOwner.Player,new NavPoint(-10,0),held:true)}),Registry()));
        }
        [Test] public void RelayCombatPermissionCannotEscapeItsExactMission()
        {
            var o=View();var r=Registry();var action=Plan(o,r);new AiScoutPlanner().Commit(o,action,r);
            AiIntent Intent(PlayableAiAction a)=>new AiIntent("test","scout",a,100,0,0,new[]{"recipient:13"});
            Assert.IsNull(r.Reject(Intent(action)));
            var foreignSite=new PlayableAiAction(1,o.OwnerId,P.ProfileId,P.Revision,71,30,PlayableCommandKind.AttackMove,new[]{13},Target,siteId:9);
            Assert.IsNotNull(r.Reject(Intent(foreignSite)));
        }
        [Test] public void NoRelayMapDoesNotEnableRelayPolicyAndUnknownControlIsScouted()
        {
            var ordinary=View(relay:false);Assert.False(AiRelayObjectives.HasRelays(ordinary));
            Assert.IsNull(AiRelayObjectives.PlanScout(ordinary,Knowledge(ordinary),Registry(),P,Ai,1,0));
            var unknown=View(visible:false);Assert.True(AiRelayObjectives.Needed(unknown,P,4));Assert.NotNull(Plan(unknown,Registry()));
        }
        [Test] public void ProfileBarrierPreservesRestoredRelayMission()
        {
            var o=View();var owner=new PlayableAiOwnerLoop(P,PlayableAiOpeningComposition.Initialize(7,o.OwnerId,forcedOpening:PlayableAiOpening.Safe),71);
            var field=typeof(PlayableAiOwnerLoop).GetField("armies",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var r=(AiArmyRegistry)field.GetValue(owner);new AiScoutPlanner().Commit(o,Plan(o,r),r);
            byte[] Save(PlayableAiOwnerLoop loop){using(var stream=new System.IO.MemoryStream()){using(var writer=new System.IO.BinaryWriter(stream,System.Text.Encoding.UTF8,true))loop.WriteState(writer);return stream.ToArray();}}
            PlayableAiOwnerLoop restored;using(var stream=new System.IO.MemoryStream(Save(owner)))using(var reader=new System.IO.BinaryReader(stream))restored=PlayableAiOwnerLoop.ReadState(reader,P,Ai,71,7,30);
            var data=Ai.CopyData();data.revision++;var next=new AiProfile(data);
            owner.Rebind(next);restored.Rebind(next);
            Assert.Greater(((AiArmyRegistry)field.GetValue(restored)).ArmyFor(13),0,"A valid relay assignment survives without a warm public-map cache");
            CollectionAssert.AreEqual(Save(owner),Save(restored));owner.Stop();restored.Stop();
        }
        [Test] public void ArmySelectsContestedRelayAndRetainsMissionAtArrival()
        {
            var tanks=Enumerable.Range(1,3).Select(i=>Unit(i,PlayableOwner.Player,new NavPoint(-12,i*3),PlayableEntityKind.Tank)).ToArray();
            var o=View(owner:PlayableOwner.Enemy,units:tanks);var r=Registry();var planner=new AiArmyPlanner(o.OwnerId,o.Generation);planner.Rebind(P);
            var opening=PlayableAiOpeningComposition.Initialize(7,o.OwnerId).WithPhase(PlayableAiOpeningPhase.Complete);
            var action=planner.ProposeWithRecords(o,opening,P,Ai,r,Array.Empty<PlayableAiTraceRecord>());Assert.NotNull(action);Assert.AreEqual(4,action.SiteId);
            planner.Commit(o,action,r);Assert.AreEqual(PlayableRouteTargetKind.PublicObjective,planner.Capture().TargetKind);
            var arrived=View(tick:60,owner:PlayableOwner.Enemy,progress:.5,units:tanks.Select((u,i)=>Unit(u.Id,u.Owner,new NavPoint(3,i),u.Kind)).ToArray());
            planner.Observe(arrived,r,Ai,Array.Empty<PlayableAiTraceRecord>(),opening,true);Assert.True(planner.Active,"Relay arrival cannot be treated as an empty enemy start");
        }
    }
}

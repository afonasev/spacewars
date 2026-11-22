using System;
using System.Linq;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;

namespace Spacewars.Tests.EditMode
{
    public sealed class AiOrbitalPolicyTests
    {
        static readonly PlayableProfile P=PlayableProfile.Default;
        static readonly AiProfile AI=AiProfile.Initial;
        static readonly NavPoint Target=new NavPoint(10,0);
        static PlayableEntitySnapshot Unit(int id,PlayableOwner owner,NavPoint point,double hp=40,PlayableEntityKind kind=PlayableEntityKind.Tank)=>new PlayableEntitySnapshot(id,owner,kind,point,(int)Math.Ceiling(hp),false,0,0,0,maxHealth:100,exactHealth:hp);
        static OrbitalEscalationSnapshot Orbital(int level,params int[] chosen)=>OrbitalEscalationState.Restore(level,0,0,chosen.Concat(new int[5-chosen.Length]).ToArray(),new double[5],P.CopyData()).Snapshot(P.CopyData());
        static PlayableSnapshot Snapshot(OrbitalEscalationSnapshot orbital,PlayableEntitySnapshot[] units=null,PlayableBuildingSnapshot[] buildings=null,PlayablePublicScoutObjective[] objectives=null,OrbitalMineSnapshot[] mines=null,bool visible=true,VisionSource[] sources=null,int seed=7)
        {
            var vision=new PlayableVision(0,100,100,1,1);vision.Refresh(sources??(visible?new[]{new VisionSource(default,100)}:Array.Empty<VisionSource>()),Array.Empty<KnownBuilding>());
            return new PlayableSnapshot(P.ProfileId,P.Revision,1,seed,1,100,RuntimeStatus.Running,false,PlayableMatchOutcome.Playing,1000,null,units??Array.Empty<PlayableEntitySnapshot>(),buildings??Array.Empty<PlayableBuildingSnapshot>(),Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,vision:vision.Snapshot(),publicScoutObjectives:objectives).WithOrbital(orbital).WithMines(mines??Array.Empty<OrbitalMineSnapshot>(),Array.Empty<OrbitalMineBlastSnapshot>());
        }
        static AiKnowledgeState Knowledge(PlayableAiObservation o,params AiVisitedArea[] areas)=>new AiKnowledgeState(o.OwnerId,o.Generation,o.Tick,Array.Empty<AiKnownContact>(),areas);
        static PlayableAiAction Plan(PlayableSnapshot snapshot,AiKnowledgeState known=null,AiPersonality personality=AiPersonality.Aggressive)
        {var o=PlayableAiObservation.From(snapshot);return AiOrbitalPolicy.Plan(o,P,AI,known??Knowledge(o),personality,Array.Empty<PlayableAiTraceRecord>(),PlayableAiOpeningComposition.SourceIdentity);}
        static PlayablePublicScoutObjective Objective(int id,NavPoint p)=>new PlayablePublicScoutObjective(id,p,PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true);
        [Test] public void UnlockedAbilityIsRequiredAndPlaceholdersAreNeverActivated()
        {
            var targets=new[]{Unit(101,PlayableOwner.Enemy,Target)};
            Assert.IsNull(Plan(Snapshot(Orbital(0),targets)));
            Assert.IsNull(Plan(Snapshot(Orbital(3,1,2,3),new[]{Unit(101,PlayableOwner.Enemy,Target,40,PlayableEntityKind.Hawk)})));
        }
        [Test] public void NuclearStrikeNeedsFiveVisibleTargetsAndRejectsFriendlyConcentrations()
        {
            var orbital=Orbital(5,1,2,3,4,OrbitalAbilities.NuclearStrike);
            var hostiles=Enumerable.Range(0,5).Select(i=>Unit(101+i,PlayableOwner.Enemy,new NavPoint(10,i))).ToArray();
            int Count(PlayableSnapshot view)=>AiOrbitalPolicy.NuclearTargetCount(
                PlayableAiObservation.From(view),P,P.CopyData(),new NavPoint(10,2));
            Assert.AreEqual(4,Count(Snapshot(orbital,hostiles.Take(4).ToArray())));
            Assert.AreEqual(0,Count(Snapshot(orbital,hostiles,visible:false)));
            Assert.AreEqual(0,Count(Snapshot(orbital,hostiles.Append(Unit(7,PlayableOwner.Player,new NavPoint(10,2))).ToArray())));
            var action=Plan(Snapshot(orbital,hostiles));
            Assert.AreEqual(OrbitalAbilities.NuclearStrike,action?.TargetId);

            var buildings=new[]{PlayableBuildingKind.Headquarters,PlayableBuildingKind.Factory,PlayableBuildingKind.RepairTower}
                .Select((kind,i)=>new PlayableBuildingSnapshot(300+i,PlayableOwner.Enemy,kind,new NavPoint(11+i,2),
                    100,1,0,0,default,siteId:3,maxHealth:100)).ToArray();
            Assert.AreEqual(5,Count(Snapshot(orbital,hostiles.Take(2).ToArray(),buildings)));
            Assert.AreEqual(2,Count(Snapshot(orbital,hostiles.Take(2).ToArray(),buildings.Skip(1).ToArray())));
        }
        [Test] public void MissingInformationChoosesScanAndSecondChoiceIsDifferent()
        {
            var first=Plan(Snapshot(Orbital(1),objectives:new[]{Objective(2,Target)},visible:false),personality:AiPersonality.Harassing);
            Assert.AreEqual(PlayableCommandKind.ChooseOrbital,first.Kind);Assert.AreEqual(1,first.TargetId);
            var second=Plan(Snapshot(Orbital(2,1),new[]{Unit(101,PlayableOwner.Enemy,Target)},objectives:new[]{Objective(2,Target)}));
            Assert.AreEqual(PlayableCommandKind.ChooseOrbital,second.Kind);Assert.AreNotEqual(1,second.TargetId);
        }
        [TestCase(49.999,true)][TestCase(50,false)][TestCase(0,false)]
        public void HackUsesStrictExactHealthBoundary(double health,bool valid)
        {
            var o=PlayableAiObservation.From(Snapshot(Orbital(1,8),new[]{Unit(101,PlayableOwner.Enemy,Target,health)}));
            Assert.AreEqual(valid,AiOrbitalPolicy.HackValid(o,P,101));
        }
        [Test] public void HiddenAndOwnedTargetsCannotBeHacked()
        {
            foreach(var s in new[]{Snapshot(Orbital(1,8),new[]{Unit(101,PlayableOwner.Enemy,Target)},visible:false),Snapshot(Orbital(1,8),new[]{Unit(101,PlayableOwner.Player,Target)})})
                Assert.False(AiOrbitalPolicy.HackValid(PlayableAiObservation.From(s),P,101));
        }
        [Test] public void MineBuildingsAlsoRequireVulnerabilityAndVisibility()
        {
            PlayableSnapshot View(double health,bool visible)=>Snapshot(Orbital(1,8),buildings:new[]{new PlayableBuildingSnapshot(501,PlayableOwner.Enemy,PlayableBuildingKind.Mine,Target,(int)Math.Ceiling(health),1,0,0,default,maxHealth:100,exactHealth:health)},visible:visible);
            Assert.True(AiOrbitalPolicy.HackValid(PlayableAiObservation.From(View(40,true)),P,501));
            Assert.False(AiOrbitalPolicy.HackValid(PlayableAiObservation.From(View(50,true)),P,501));
            Assert.False(AiOrbitalPolicy.HackValid(PlayableAiObservation.From(View(40,false)),P,501));
        }
        [Test] public void BoostPolicyCastsOnUsefulOwnQueuesAndNeverOnAnOpponent()
        {
            var orbit = Orbital(3, 1, 2, OrbitalAbilities.AcceleratedProduction);
            var life = new PlayableBuildingLifecycleSnapshot(false, 0, false, false, 0, 0, null, null, 0, 0, false);
            PlayableBuildingSnapshot Factory(PlayableOwner owner, double? work) => new PlayableBuildingSnapshot(
                501, owner, PlayableBuildingKind.Factory, default, 300, 1, 0, 0, default,
                maxHealth: 300, lifecycle: life, boostWorkRemaining: work);
            var idle = Snapshot(orbit, buildings: new[] { Factory(PlayableOwner.Player, null) });
            Assert.IsNull(Plan(idle));
            var active = Snapshot(orbit, buildings: new[] { Factory(PlayableOwner.Player, 15) });
            var cast = Plan(active);
            Assert.AreEqual(OrbitalAbilities.AcceleratedProduction, cast?.TargetId);
            CollectionAssert.AreEqual(new[] { 501 }, cast.EntityIds);
            var foreign = Snapshot(orbit, buildings: new[] { Factory(PlayableOwner.Enemy, 15) });
            Assert.IsNull(Plan(foreign));
            var live = PlayableAiObservation.From(foreign);
            Assert.False(AiOrbitalPolicy.CanDeliver(live, P, AI, Knowledge(live), cast));
        }
        [Test] public void CommonIntervalAndTargetLossPreventDelivery()
        {
            var initial=Snapshot(Orbital(1,8),new[]{Unit(101,PlayableOwner.Enemy,Target)});var action=Plan(initial);Assert.NotNull(action);
            var live=PlayableAiObservation.From(Snapshot(Orbital(1,8),new[]{Unit(101,PlayableOwner.Enemy,Target,50)}));
            Assert.False(AiOrbitalPolicy.CanDeliver(live,P,AI,Knowledge(live),action));
            var cooldown=new OrbitalEscalationSnapshot(1,0,250,0,1,new[]{new OrbitalAbilitySnapshot(8,0,30,1)},Array.Empty<int>());
            Assert.IsNull(Plan(Snapshot(cooldown,new[]{Unit(101,PlayableOwner.Enemy,Target)})));
        }
        [Test] public void ScanHasPublicPurposeAndSkipsVisibleOrRecentlyCoveredArea()
        {
            var hidden=Snapshot(Orbital(1,1),objectives:new[]{Objective(2,Target)},visible:false);var action=Plan(hidden);Assert.AreEqual(1,action?.TargetId);
            var o=PlayableAiObservation.From(hidden);Assert.IsNull(Plan(hidden,Knowledge(o,new AiVisitedArea(2,90,true,false))));
            var live=PlayableAiObservation.From(Snapshot(Orbital(1,1),objectives:new[]{Objective(2,Target)}));
            Assert.False(AiOrbitalPolicy.CanDeliver(live,P,AI,Knowledge(live),action),"allied scan already reveals this point");
            Assert.IsNull(Plan(Snapshot(Orbital(1,1),visible:false)),"no hidden entity lookup without public or remembered target");
        }
        [Test]
        public void ScanCanCenterInVisionAndRevealAnUnseenFlank()
        {
            var objective = Objective(2, Target);
            var snapshot = Snapshot(Orbital(1,1), objectives: new[] { objective },
                sources: new[] { new VisionSource(Target, 2) });
            var observation = PlayableAiObservation.From(snapshot);
            var cast = new PlayableAiAction(1, observation.OwnerId, observation.ProfileId, observation.ProfileRevision,
                observation.Generation, observation.SnapshotSequence, PlayableCommandKind.UseOrbital, null, Target, targetId: 1);
            Assert.True(observation.Vision.IsVisible(Target));
            Assert.True(AiOrbitalPolicy.CanDeliver(observation, P, AI, Knowledge(observation), cast),
                "vision at the center must not hide the information gained across the circle");
            Assert.NotNull(Plan(snapshot));
        }

        [Test] public void MineInterceptsGroundButDoesNotExplodeBesideFriendlies()
        {
            var ground = new PlayableEntitySnapshot(101, PlayableOwner.Enemy, PlayableEntityKind.Tank,
                Target, 100, true, 0, Math.PI, 0);
            var corner = Objective(3, new NavPoint(10, 12));
            var orbital = Orbital(1, 2);
            var action = Plan(Snapshot(orbital, new[] { ground }, objectives: new[] { corner }));
            Assert.AreEqual(2, action?.TargetId);
            Assert.That(action.Target.X, Is.LessThan(Target.X - P.MineEnemyClearanceRadius));
            Assert.That(action.Target.Z, Is.EqualTo(0).Within(1e-6));
            var danger = Snapshot(orbital, new[] { ground, Unit(1, PlayableOwner.Player, action.Target, 100) });
            var live = PlayableAiObservation.From(danger);
            Assert.False(AiOrbitalPolicy.CanDeliver(live, P, AI, Knowledge(live), action));
            var advanced = new PlayableEntitySnapshot(101, PlayableOwner.Enemy, PlayableEntityKind.Tank,
                new NavPoint(action.Target.X + 2, 0), 100, true, 0, Math.PI, 0);
            var moved = PlayableAiObservation.From(Snapshot(orbital, new[] { advanced }));
            Assert.False(AiOrbitalPolicy.CanDeliver(moved, P, AI, Knowledge(moved), action),
                "Enemy moved into the exclusion circle after planning");
            Assert.IsNull(Plan(Snapshot(orbital, new[] { Unit(101, PlayableOwner.Enemy, Target, 100, PlayableEntityKind.Hawk) },
                objectives: new[] { corner })));
            Assert.IsNull(Plan(Snapshot(orbital, new[] { Unit(101, PlayableOwner.Enemy, Target, 100) },
                objectives: new[] { corner })), "a stationary enemy does not make an arbitrary corner a route");
        }

        [Test]
        public void SystemFailureFollowsAttackingArmyAndCancelsWhenItTurnsAway()
        {
            var tower = new PlayableBuildingSnapshot(501, PlayableOwner.Enemy, PlayableBuildingKind.RepairTower, Target,
                100, 1, 0, 0, default);
            var range = TerritoryRules.AiDefenseRange(P, tower.Kind);
            var position = new NavPoint(Target.X + range + 3, Target.Z);
            PlayableEntitySnapshot Tank(NavPoint destination) => new PlayableEntitySnapshot(1, PlayableOwner.Player,
                PlayableEntityKind.Tank, position, 100, true, 0, Math.PI, 0,
                new PlayableTacticalOrderSnapshot(1, PlayableOwner.Player, 1, 1, 90,
                    PlayableTacticalOrderKind.AttackMove, destination, 0));
            var orbital = Orbital(3, 1, 2, OrbitalAbilities.SystemFailure);
            var action = Plan(Snapshot(orbital, new[] { Tank(Target) }, new[] { tower }));
            Assert.AreEqual(OrbitalAbilities.SystemFailure, action?.TargetId);
            Assert.That(AiOpponentSelector.Distance(action.Target, tower.Position), Is.LessThanOrEqualTo(P.CopyData().orbitalSystemFailureRadius));
            var away = new NavPoint(position.X + 3, position.Z);
            var live = PlayableAiObservation.From(Snapshot(orbital, new[] { Tank(away) }, new[] { tower }));
            Assert.False(AiOrbitalPolicy.CanDeliver(live, P, AI, Knowledge(live), action));
        }
        [Test] public void CapacityDoesNotReplaceEquallyUsefulOldestMineOrDuplicateAllyPosition()
        {
            var data = P.CopyData();
            var orbital = Orbital(1, 2);
            var enemy = new PlayableEntitySnapshot(101, PlayableOwner.Enemy, PlayableEntityKind.Tank,
                new NavPoint(18, 0), 100, true, 0, Math.PI, 0);
            var first = Plan(Snapshot(orbital, new[] { enemy }));
            Assert.NotNull(first, "a valid route exists without any prior mine");
            var mines = Enumerable.Range(1, (int)data.orbitalMineCapacity).Select(id => new OrbitalMineSnapshot(id,
                PlayableOwner.Player, id == 1 ? first.Target : new NavPoint(-20, id * 6),
                data.orbitalMineModelRadius, data.orbitalMineModelHeight, data.orbitalMineBlastRadius)).ToArray();
            Assert.IsNull(Plan(Snapshot(orbital, new[] { enemy }, mines: mines)), "keep the valuable oldest mine at capacity");

            var allyMine = new OrbitalMineSnapshot(1, PlayableOwner.Player, first.Target,
                data.orbitalMineModelRadius, data.orbitalMineModelHeight, data.orbitalMineBlastRadius);
            var limitedVision = new[] { new VisionSource(enemy.Position, 2), new VisionSource(first.Target, 2) };
            Assert.NotNull(Plan(Snapshot(orbital, new[] { enemy }, sources: limitedVision)));
            Assert.IsNull(Plan(Snapshot(orbital, new[] { enemy }, mines: new[] { allyMine }, sources: limitedVision)),
                "another mine cannot duplicate coverage in the only visible stretch of the approach");
        }
        [Test] public void SuccessfulScanMemorySuppressesRepeatsAndRestoresExactly()
        {
            var arbiter=new AiDecisionArbiter(AI);arbiter.RememberOrbitalScan(Target,90);
            var o=PlayableAiObservation.From(Snapshot(Orbital(1,1),objectives:new[]{Objective(2,Target)},visible:false));
            Assert.IsNull(AiOrbitalPolicy.Plan(o,P,AI,Knowledge(o),AiPersonality.Harassing,Array.Empty<PlayableAiTraceRecord>(),PlayableAiOpeningComposition.SourceIdentity,point=>arbiter.LastOrbitalScan(point,10)));
            byte[] Write(AiDecisionArbiter a){using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);a.WriteState(writer);return stream.ToArray();}
            var bytes=Write(arbiter);var restored=new AiDecisionArbiter(AI);using(var reader=new BinaryReader(new MemoryStream(bytes)))restored.ReadState(reader,100,o.OwnerId,o.Generation,P,AI);
            CollectionAssert.AreEqual(bytes,Write(restored));Assert.AreEqual(90,restored.LastOrbitalScan(Target,10));
            Assert.NotNull(AiOrbitalPolicy.Plan(o,P,AI,Knowledge(o),AiPersonality.Harassing,Array.Empty<PlayableAiTraceRecord>(),PlayableAiOpeningComposition.SourceIdentity,_=>-1));
        }
        [Test] public void PendingOrbitalChoiceRestoresExactCurrentAuthority()
        {
            var map=new AiTestMap();var p=PlayableProfile.Create(P.CopyData(),map);var config=map.Configuration(p,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var authority=new PlayableAuthorityTick(config,907,difficulty:AiDifficulty.Veteran);PlayableAuthorityTick restored=null;
            try
            {
                var state=(OrbitalEscalationState)authority.Domain.GetType().GetMethod("Orbital",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(authority.Domain,new object[]{PlayableOwner.Player});state.Damage(p.CopyData().orbitalThreshold1,p.CopyData());
                for(int i=0;i<60&&!authority.CaptureDiagnosticCheckpoints().Any(c=>c.Records.Any(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Status==PlayableAiDeliveryStatus.Scheduled));i++)
                {while(authority.Requests.TryDequeue(out var request))Assert.True(authority.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>())));authority.TryAdvance();}
                Assert.True(authority.CaptureDiagnosticCheckpoints().Any(c=>c.Records.Any(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Status==PlayableAiDeliveryStatus.Scheduled)));
                var bytes=authority.CaptureBytes();restored=PlayableAuthorityTick.RestoreBytes(bytes,config,AI);CollectionAssert.AreEqual(bytes,restored.CaptureBytes());
                for(int i=0;i<60;i++){while(restored.Requests.TryDequeue(out var request))Assert.True(restored.Answers.TryEnqueue(new NavigationAnswer(request,Array.Empty<NavPoint>())));restored.TryAdvance();}
                Assert.True(restored.CaptureDiagnosticCheckpoints().Any(c=>c.Records.Any(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Status==PlayableAiDeliveryStatus.Applied)),"restored pending choice executes normally");
            }
            finally{restored?.Stop();authority.Stop();restored?.Domain.DestroyOwnedNavigation();authority.Domain.DestroyOwnedNavigation();}
        }
        [Test]
        public void AdvancedOrbitalChoiceCanSelectEveryRemainingAbility()
        {
            var choices = new[] { OrbitalAbilities.SystemFailure, OrbitalAbilities.Reinforcement, OrbitalAbilities.Stasis, OrbitalAbilities.RepairNanobots, OrbitalAbilities.AcceleratedProduction };
            var seeds = Enumerable.Range(1, 32).ToArray();
            var first = seeds.Select(seed => Plan(Snapshot(Orbital(3, 1, 2), seed: seed)).TargetId).Distinct();
            CollectionAssert.AreEquivalent(choices, first);
            foreach (int chosen in choices)
            {
                var second = seeds.Select(seed => Plan(Snapshot(Orbital(4, 1, 2, chosen), seed: seed)).TargetId).Distinct();
                CollectionAssert.AreEquivalent(choices.Where(id => id != chosen), second);
            }
        }

        [Test]
        public void RepairNanobotsNeedThreeWoundedAndPreferActiveCombat()
        {
            const int repair = OrbitalAbilities.RepairNanobots;
            var state = Orbital(3,1,2,repair);
            var quiet = Enumerable.Range(1,3).Select(id => Unit(100+id,PlayableOwner.Player,new NavPoint(-14,id),20)).ToArray();
            Assert.IsNull(Plan(Snapshot(state,quiet.Take(2).ToArray())));
            var fighters = Enumerable.Range(1,3).Select(id => new PlayableEntitySnapshot(200+id,PlayableOwner.Player,
                PlayableEntityKind.Tank,new NavPoint(12,id),70,false,301,0,0,maxHealth:100)).ToArray();
            var enemy = Unit(301,PlayableOwner.Enemy,new NavPoint(15,3));
            var units = quiet.Concat(fighters).Append(enemy).ToArray();
            var action = Plan(Snapshot(state,units));
            Assert.NotNull(action);
            Assert.AreEqual(repair,action.TargetId);
            Assert.Greater(action.Target.X,0);
            var reduced = PlayableAiObservation.From(Snapshot(state,units.Where(unit => unit.Id != fighters[0].Id).ToArray()));
            Assert.False(AiOrbitalPolicy.CanDeliver(reduced,P,AI,Knowledge(reduced),action));
        }

        [Test]
        public void ScoutCanRequestRaidWaveAndAirThreatCancelsDelivery()
        {
            var advanced = Orbital(3, 1, 2, OrbitalAbilities.Reinforcement);
            var quote = OrbitalReinforcement.Quote(P, 1.2, false, false);
            var state = new OrbitalEscalationSnapshot(advanced.Level, advanced.Progress, advanced.Threshold, 0, 0,
                advanced.Abilities.ToArray(), advanced.Options.ToArray(), quote);
            var scout = Unit(101, PlayableOwner.Player, new NavPoint(1, 0), 100, PlayableEntityKind.Explorer);
            var mine = new PlayableBuildingSnapshot(501, PlayableOwner.Enemy, PlayableBuildingKind.Mine, Target,
                100, 1, 0, 0, default, maxHealth:100);
            var ready = Snapshot(state, new[] { scout }, new[] { mine });
            var order = Plan(ready);
            Assert.NotNull(order);
            Assert.AreEqual(OrbitalAbilities.Reinforcement, order.TargetId);
            Assert.AreEqual(mine.Id, order.ParentId);
            CollectionAssert.AreEqual(new[] { scout.Id }, order.EntityIds);
            var hawk = Unit(202, PlayableOwner.Enemy, order.Target, 100, PlayableEntityKind.Hawk);
            var threatened = PlayableAiObservation.From(Snapshot(state, new[] { scout, hawk }, new[] { mine }));
            Assert.False(AiOrbitalPolicy.CanDeliver(threatened, P, AI, Knowledge(threatened), order));
        }
    }
}

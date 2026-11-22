using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class AiProducerRallyThreadedTests
    {
        [UnityTest, Timeout(150000)]
        public IEnumerator ExpertAssignsProducedExplorersWhileFactoryRallyIsActive() =>
            ReinforcementMatch(useReleaseProfile: false);

        [UnityTest, Timeout(150000)]
        public IEnumerator ExpertFactoryReinforcesWithTheShippedReleaseProfile() =>
            ReinforcementMatch(useReleaseProfile: true);

        [UnityTest, Timeout(150000)]
        public IEnumerator FactoryTanksJoinTheAssaultAndDamageEnemyCenters() =>
            ReinforcementMatch(useReleaseProfile: true, verifyCombat: true);

        [TestCase(false)]
        [TestCase(true)]
        public void IdleFactoryStagesOnTheExpectedApproach(bool forwardFactory)
        {
            var p=PlayableProfile.Default;
            var center=new NavPoint(-20,-15);
            var enemy=new NavPoint(20,-15);
            var front=PlayableUnitRules.AttackApproachCandidates(p,enemy,center,p.TankRange).First();
            var position=forwardFactory?new NavPoint(front.X-FactoryReleaseRules.ExitDistance(p,PlayableEntityKind.Tank),center.Z):
                new NavPoint(center.X-8,center.Z);
            var factory=new PlayableBuildingSnapshot(2,PlayableOwner.Player,PlayableBuildingKind.Factory,position,100,1,0,0,
                default,siteId:1,heading:0);
            var site=new TerritorySite(1,PlayableBuildingKind.Headquarters,center,p);
            var vision=new PlayableVision(0,80,80,1,1);
            vision.Refresh(new[]{new VisionSource(center,20)},Array.Empty<KnownBuilding>());
            var snapshot=new PlayableSnapshot(p.ProfileId,p.Revision,1,7,10,10,RuntimeStatus.Running,false,
                PlayableMatchOutcome.Playing,0,null,Array.Empty<PlayableEntitySnapshot>(),new[]{factory},
                Array.Empty<PlayableProjectileSnapshot>(),new PlayableRuntimeMetrics(0,0,0,0,0),null,
                sites:new[]{new TerritorySiteSnapshot(site,PlayableOwner.Player,PlayableOwner.Player,1,false,1,true)},
                vision:vision.Snapshot(),publicScoutObjectives:new[]{new PlayablePublicScoutObjective(2,enemy,
                    PlayablePublicScoutObjectiveRole.PossibleEnemyStart,true)});
            var observation=PlayableAiObservation.From(snapshot);
            var rally=AiPositionDevelopmentPlanner.DefensiveFactoryRally(observation,p,AiProfile.Initial,null,factory,
                AiTacticalExecutor.ObservedGroundGeometry(observation,p));
            if(forwardFactory)Assert.IsFalse(rally.HasValue,"The exit already occupies the defensive position.");
            else
            {
                Assert.True(rally.HasValue);
                Assert.Greater(rally.Value.X,center.X);
                Assert.Greater(AiOpponentSelector.Distance(position,rally.Value),p.FollowDistance);
            }
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator FirstTankMovesToTheDefensiveRallyBeforeAnArmyForms() =>
            ReinforcementMatch(useReleaseProfile:true,defensiveReserve:true);

        private static IEnumerator ReinforcementMatch(bool useReleaseProfile,bool defensiveReserve=false, bool verifyCombat=false)
        {
            const int seed = 7;
            long generation = useReleaseProfile ? 209 : 208;
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../.local/rally-check/match-" + Guid.NewGuid().ToString("N")));
            var profile = PlayableProfile.Default;
            var ai = NativeBalanceFields.InitialAiOpeningProfile();
            if (useReleaseProfile)
            {
                var store = new NativeBalanceStore(profile, Path.Combine(directory, "catalog.json"),
                    authoring: false, startOnRelease: true);
                Assert.IsTrue(store.HasActive, store.Problem);
                profile = store.Active;
                ai = store.ActiveAiProfile;
            }
            // The diagnostic opening builds Explorers, while the release
            // profile exercises tank reinforcement in this same setup.
            var producedKind = useReleaseProfile ? PlayableEntityKind.Tank : PlayableEntityKind.Explorer;
            var starts = new[]
            {
                new OfflineStart("west", 1, new NavPoint(-20, -15), new NavPoint(-20, -27), pin: 1),
                new OfflineStart("east", 2, new NavPoint(20, 15), new NavPoint(20, 27), pin: 2)
            };
            var home = new TerritorySite(1, PlayableBuildingKind.Headquarters, starts[0].Position, profile);
            var enemy = new TerritorySite(2, PlayableBuildingKind.Headquarters, starts[1].Position, profile);
            var reserve = new TerritorySite(3, PlayableBuildingKind.Outpost, new NavPoint(25, 24),
                Array.Empty<TerritorySlot>());
            var kinds = new[] { PlayableBuildingKind.Factory, PlayableBuildingKind.Refinery,
                PlayableBuildingKind.Refinery, PlayableBuildingKind.Refinery };
            var buildings = kinds.Select((kind, i) => new OfflineScenarioBuilding(1, kind, home.Id,
                    home.Slots[i].Id, home.Slots[i].Position, home.Slots[i].Heading))
                .Append(new OfflineScenarioBuilding(2, PlayableBuildingKind.Outpost, reserve.Id, 0, reserve.Position));
            var units = Enumerable.Range(0, defensiveReserve?0:verifyCombat?4:8).Select(i => new OfflineScenarioUnit(1, PlayableEntityKind.Tank,
                new NavPoint(-14 + i % 4 * 3, -2 + i / 4 * 3)));
            var config = new OfflineMatchConfiguration(profile, "ai-producer-rally-v1", "flat-public-active-v1",
                "UnityHostRouteService", seed,
                new[] { new OfflineParticipant("active-owner", 1, 1, OfflineControl.Ai),
                    new OfflineParticipant("human-owner", 2, 2, OfflineControl.Human) },
                starts, new[] { home, enemy, reserve }, PlayableMap.StaticObstacles(profile),
                new double[,] { { 0, 100 }, { 100, 0 } }, scenario: units.ToArray(),
                scenarioBuildings: buildings.ToArray(), spectators: new[] { PlayableRuntime.LocalSpectatorId });

            TestMatchLogSession.StartDefault(directory);
            using var runtime = new PlayableRuntime(config, generation,
                participantDifficulties: new[] { AiDifficulty.Expert, AiDifficulty.Fighter }, aiProfile: ai);
            using var routes = new UnityHostRouteService();
            var startedAt = new Dictionary<int, NavPoint>();
            var leftProduction = new HashSet<int>();
            var assigned = new HashSet<int>();
            var marchedToRally = new HashSet<int>();
            var defensiveOrders=new Dictionary<int,(long Sequence,NavPoint Destination,NavPoint Origin)>();
            var assault = verifyCombat;
            var waveOrders = new Dictionary<int,long>();
            var arrivals = new Dictionary<int,long>();
            var hits = new Dictionary<int,PlayableCenterDamageSnapshot>();
            var initial = new HashSet<int>();
            var rallyPoints = new HashSet<NavPoint>();
            try
            {
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId, 4);
                var deadline = Time.realtimeSinceStartupAsDouble + 125;
                while (runtime.Latest.Tick < 3900 && !runtime.IsStopped && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    routes.Service(runtime, profile.NavigationRequestsPerFrame);
                    Assert.IsNull(runtime.Latest.Failure);
                    Assert.IsNull(routes.LastProjectionFailure);
                    var frame = runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                    var owner = frame.Players
                        .FirstOrDefault(view => view.Owner == PlayableOwner.Player);
                    if (owner == null) { yield return null; continue; }
                    if (initial.Count == 0)
                        foreach (var entity in owner.Entities.Where(u => u.Owner == owner.Owner)) initial.Add(entity.Id);

                    foreach (var factory in owner.Buildings.Where(b => b.Owner == owner.Owner &&
                        b.Kind == PlayableBuildingKind.Factory && b.PrivateState?.HasRally == true))
                        if(!defensiveReserve||AiOpponentSelector.Distance(factory.Rally,home.Position)<=profile.TankRange&&
                            (factory.Rally.X-home.Position.X)*(enemy.Position.X-home.Position.X)+
                            (factory.Rally.Z-home.Position.Z)*(enemy.Position.Z-home.Position.Z)>0)
                            rallyPoints.Add(factory.Rally);
                    foreach (var unit in owner.Entities.Where(u => u.Owner == owner.Owner && u.Kind == producedKind &&
                        u.Health > 0 && !initial.Contains(u.Id)))
                    {
                        if (!startedAt.TryGetValue(unit.Id, out var start))
                            startedAt.Add(unit.Id, unit.Position);
                        else if (unit.OrderStamp?.Origin == PlayableOrderOrigin.Ai && unit.CurrentOrder != null)
                        {
                            assigned.Add(unit.Id);
                            if (AiOpponentSelector.Distance(start, unit.Position) > PlayableUnitRules.Radius(profile, unit.Kind))
                            {
                                leftProduction.Add(unit.Id);
                                if (unit.CurrentOrder.Kind == PlayableTacticalOrderKind.AttackMove &&
                                    rallyPoints.Any(point => AiOpponentSelector.Distance(point, unit.CurrentOrder.Destination) <= profile.FollowDistance))
                                {
                                    if(!defensiveReserve)
                                    {
                                        marchedToRally.Add(unit.Id);
                                        var goal = unit.CurrentOrder.Destination;
                                        if (assault && AiOpponentSelector.Distance(goal, enemy.Position) <
                                            AiOpponentSelector.Distance(goal, home.Position))
                                            waveOrders.TryAdd(unit.Id, owner.Tick);
                                    }
                                    else if(defensiveOrders.TryGetValue(unit.Id,out var leg)&&leg.Sequence==unit.CurrentOrder.CommandSequence&&
                                        AiOpponentSelector.Distance(unit.Position,leg.Destination)+PlayableUnitRules.Radius(profile,unit.Kind)<
                                        AiOpponentSelector.Distance(leg.Origin,leg.Destination))marchedToRally.Add(unit.Id);
                                    else if(!defensiveOrders.TryGetValue(unit.Id,out leg)||leg.Sequence!=unit.CurrentOrder.CommandSequence)
                                        defensiveOrders[unit.Id]=(unit.CurrentOrder.CommandSequence,unit.CurrentOrder.Destination,unit.Position);
                                }
                            }
                        }
                    }
                    if (assault)
                    {
                        var front = owner.Entities.Where(u => u.Owner == owner.Owner && initial.Contains(u.Id) &&
                            u.Health > 0 && u.Kind == PlayableEntityKind.Tank &&
                            AiOpponentSelector.Distance(u.Position, enemy.Position) < AiOpponentSelector.Distance(u.Position, home.Position)).ToArray();
                        foreach (var unit in owner.Entities.Where(u => waveOrders.ContainsKey(u.Id)))
                            if (front.Any(ally => AiOpponentSelector.Distance(ally.Position, unit.Position) <=
                                profile.FollowDistance + profile.FollowArrivalTolerance + 2 * PlayableUnitRules.Radius(profile, unit.Kind)))
                                arrivals.TryAdd(unit.Id, owner.Tick);
                        foreach (var hit in frame.Players.Where(v => v.Owner != owner.Owner).SelectMany(v => v.OwnCenterDamage))
                            if (hit.Damage > 0 && arrivals.TryGetValue(hit.AttackerId, out var arrival) && hit.Tick >= arrival)
                                hits.TryAdd(hit.AttackerId, hit);
                    }
                    if (rallyPoints.Count > 0 && leftProduction.Count > 0 &&
                        (!useReleaseProfile || marchedToRally.Count > 0) && (!assault || hits.Count >= 2)) break;
                    if (owner.Outcome != PlayableMatchOutcome.Playing) break;
                    yield return null;
                }
                TestContext.WriteLine($"AI_RALLY_MATCH seed={seed} tick={runtime.Latest.Tick} profile={profile.ProfileId}@{profile.Revision} ai={ai.Id}@{ai.Revision} rallies={rallyPoints.Count} kind={producedKind} produced={startedAt.Count} assigned={assigned.Count} moved={leftProduction.Count} direct={marchedToRally.Count} logs={directory}");
                if (assault)
                {
                    foreach (var id in waveOrders.Keys)
                        TestContext.WriteLine($"ASSAULT_WAVE tank={id} march={waveOrders[id]} joined={arrivals.GetValueOrDefault(id)} hit={hits.GetValueOrDefault(id)?.Tick} target={hits.GetValueOrDefault(id)?.CenterId} damage={hits.GetValueOrDefault(id)?.Damage}");
                    Assert.That(hits.Count, Is.GreaterThanOrEqualTo(2), "Two factory tanks must reach the assault group and damage an enemy center.");
                }
                Assert.IsNotEmpty(rallyPoints, "A real AI factory rally must finish route admission.");
                Assert.IsNotEmpty(leftProduction, $"An AI-produced {producedKind} must receive an order and physically leave production.");
                if (useReleaseProfile)
                    Assert.IsNotEmpty(marchedToRally, "An AI-produced tank must physically move under AttackMove to an admitted rally.");
            }
            finally
            {
                runtime.RequestStop();
                routes.Dispose();
                Assert.IsTrue(System.Threading.SpinWait.SpinUntil(() => runtime.IsStopped, 5000));
                TestMatchLogSession.Shutdown();
            }
        }
    }
}

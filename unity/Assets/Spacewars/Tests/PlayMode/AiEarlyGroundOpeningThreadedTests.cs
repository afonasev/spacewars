using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class AiEarlyGroundOpeningThreadedTests
    {
        private const long Generation=128;
        private const string Owner="early-ground-owner";
        private const int EnemyMine=3;

        [UnityTest, Timeout(115000)]
        public IEnumerator AggressiveFactoryForceMovesAndDestroysKnownEnemyIncome()
            =>OpeningPressure(PlayableAiOpening.BlindRush);

        [UnityTest, Timeout(115000)]
        public IEnumerator ExplorerAllInMovesAndEnablesCaptureOfKnownEnemyIncome()
            =>OpeningPressure(PlayableAiOpening.ExplorerAllIn);

        [UnityTest, Timeout(115000)]
        public IEnumerator AggressiveOpeningHitsOccupiedMineWithViableRelay()
            =>OpeningPressure(PlayableAiOpening.BlindRush,withRelay:true);

        private static IEnumerator OpeningPressure(PlayableAiOpening expected,bool withRelay=false)
        {
            var data=PlayableProfile.Default.CopyData();
            data.arenaHalfExtent=64;
            data.groundRouteBackend=1;
            // The distant human HQ keeps the test match alive while its
            // separate mine is attacked. Recon can reach that HQ first.
            data.headquartersHealth=1000;
            var p=PlayableProfile.Create(data);
            var ai=NativeBalanceFields.InitialAiOpeningProfile();
            int seed=Enumerable.Range(0,10000).First(n=>PlayableAiOpeningComposition.Initialize(n,Owner,aiProfile:ai).Opening==expected);
            var starts=new[]{
                new OfflineStart("west",1,new NavPoint(-30,-15),new NavPoint(-30,-24),pin:1),
                new OfflineStart("east",2,new NavPoint(30,20),new NavPoint(30,29),pin:2)
            };
            var slots=new[]{
                new TerritorySlot(1,new NavPoint(-30,-7),0),
                new TerritorySlot(2,new NavPoint(-30,-30),0),
                new TerritorySlot(3,new NavPoint(-42,-7),0)
            };
            var target=new NavPoint(-11,-15);
            var sites=new[]{
                new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,slots),
                new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,Array.Empty<TerritorySlot>()),
                new TerritorySite(EnemyMine,PlayableBuildingKind.Mine,target,Array.Empty<TerritorySlot>())
            };
            const int relaySite=4;
            if(withRelay)sites=sites.Concat(new[]{
                new TerritorySite(relaySite,PlayableBuildingKind.RelayTower,new NavPoint(-6,-3),Array.Empty<TerritorySlot>())
            }).ToArray();
            var competingCohorts=new ConcurrentQueue<int[]>();
            if(withRelay)
            {
                PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=Owner;
                PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=(owner,tick,o,registry,pending)=>
                {
                    var mine=o.Buildings.FirstOrDefault(b=>b.SiteId==EnemyMine&&b.Phase==ConstructionPhase.Ready&&o.IsHostile(b.Owner));
                    var relay=o.PublicScoutObjectives.FirstOrDefault(g=>g.SiteId==relaySite&&g.Reachable);
                    if(mine==null||relay==null||!AiRelayObjectives.Needed(o,p,relaySite))return;
                    var candidates=o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&u.OrderStamp?.Origin!=PlayableOrderOrigin.Human&&
                        o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.VisibleEnemy&&r.TargetId==mine.Id)&&
                        o.RouteProofs.Any(r=>r.UnitId==u.Id&&r.Kind==PlayableRouteTargetKind.PublicObjective&&r.TargetId==relaySite)).ToArray();
                    if(candidates.Length>=2&&candidates.Any(u=>u.Kind==PlayableEntityKind.Tank)&&
                        AiRelayObjectives.Viable(o,p,ai,null,candidates,relaySite,relay.Approach))
                        competingCohorts.Enqueue(candidates.Select(u=>u.Id).ToArray());
                };
            }
            var config=new OfflineMatchConfiguration(p,"early-ground-pressure-v1","flat-known-mine-v1","UnityHostRouteService",seed,
                new[]{new OfflineParticipant(Owner,1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},
                starts,sites,Array.Empty<NavObstacle>(),new double[,]{{0,75},{75,0}},spectators:new[]{PlayableRuntime.LocalSpectatorId},
                scenarioBuildings:new[]{new OfflineScenarioBuilding(2,PlayableBuildingKind.Mine,EnemyMine,0,target)});

            var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.local/early-ground-pressure/witness-"+expected+"-"+Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(output);
            using var logs=new TestMatchLogSession(output,new TestMatchLoggingProfile(),new{Fixture=TestContext.CurrentContext.Test.Name,Seed=seed,AiHash=ai.Hash});
            PlayableRuntime.JournalFactory=c=>logs.CreateMatch(c);
            PlayableRuntime runtime=null;
            UnityHostRouteService routes=null;
            try
            {
                double deadline=Time.realtimeSinceStartupAsDouble+95;
                runtime=new PlayableRuntime(config,Generation,startPaused:true,aiDifficulty:AiDifficulty.Veteran,aiProfile:ai);
                routes=new UnityHostRouteService();
                var initial=runtime.ParticipantView(Owner);
                int scout=initial.Entities.Single(u=>u.Owner==initial.Owner&&u.Kind==PlayableEntityKind.Explorer).Id;
                int targetId=initial.Buildings.Single(b=>b.SiteId==EnemyMine&&b.Kind==PlayableBuildingKind.Mine).Id;
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                runtime.RequestPause(false);

                bool captureMine=expected==PlayableAiOpening.ExplorerAllIn;
                long factory=-1,command=-1,movement=-1,firstDamage=-1,damaged=-1,destroyed=-1,captured=-1,income=-1;
                double previousHealth=p.MineHealth;
                long sequence=0;
                var members=new Dictionary<int,(PlayableEntityKind Kind,NavPoint Origin)>();
                while(runtime.Latest.Tick<3600&&runtime.Latest.Outcome==PlayableMatchOutcome.Playing&&
                    !runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)
                {
                    routes.Service(runtime,p.NavigationRequestsPerFrame);
                    var view=runtime.ParticipantView(Owner);
                    Assert.IsNull(view.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    if(factory<0&&view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready))
                        factory=view.Tick;
                    foreach(var unit in view.Entities.Where(u=>u.Owner==view.Owner&&u.Id!=scout&&
                        u.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&u.CurrentOrder?.Kind==PlayableTacticalOrderKind.Attack&&u.CurrentOrder.TargetId==targetId))
                    {
                        if(command<0){command=unit.OrderStamp.Tick;sequence=unit.OrderStamp.Sequence;}
                        if(unit.OrderStamp.Sequence!=sequence)continue;
                        if(!members.TryGetValue(unit.Id,out var start))members.Add(unit.Id,(unit.Kind,unit.Position));
                        else if(movement<0&&AiOpponentSelector.Distance(start.Origin,unit.Position)>PlayableUnitRules.Radius(p,unit.Kind))
                            movement=view.Tick;
                    }
                    var mine=view.Buildings.FirstOrDefault(b=>b.Id==targetId);
                    if(mine!=null)
                    {
                        double health=mine.ExactHealth??mine.Health;
                        if(health<previousHealth)
                        {
                            if(firstDamage<0)firstDamage=view.Tick;
                            if(damaged<0&&movement>=0&&view.Tick>movement)damaged=view.Tick;
                        }
                        previousHealth=health;
                    }
                    if(damaged>=0&&view.Vision.IsVisible(target)&&mine==null){destroyed=view.Tick;break;}
                    if(captureMine&&damaged>=0&&mine?.Owner==view.Owner&&mine.Phase==ConstructionPhase.Ready)
                    {
                        if(captured<0)captured=view.Tick;
                        var settlement=view.IncomeEvents.FirstOrDefault(e=>e.Tick>=captured&&e.BuildingId==targetId&&e.Owner==view.Owner&&e.Amount>0);
                        if(settlement!=null){income=settlement.Tick;break;}
                    }
                    yield return null;
                }
                bool sawCompetingRoutes=members.Count>0&&competingCohorts.Any(cohort=>members.Keys.All(cohort.Contains));
                File.WriteAllText(Path.Combine(output,"readback.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{
                    opening=expected.ToString(),seed,ai.Hash,withRelay,sawCompetingRoutes,scout,targetId,factory,command,movement,firstDamage,damaged,destroyed,captured,income,sequence,
                    members=members.Select(m=>new{id=m.Key,kind=m.Value.Kind.ToString()}),last=runtime.Latest.Tick,outcome=runtime.Latest.Outcome.ToString()
                },Newtonsoft.Json.Formatting.Indented));
                TestContext.WriteLine("EARLY_GROUND_WITNESS "+output);
                if(withRelay)Assert.True(sawCompetingRoutes,"Both the occupied visible mine and the relay offered typed routes to the first ground group.");
                Assert.GreaterOrEqual(factory,0);
                Assert.Greater(command,factory,"Produced combat members receive one ordinary attack order.");
                Assert.That(members.Count,Is.InRange(expected==PlayableAiOpening.BlindRush?2:3,4));
                if(expected==PlayableAiOpening.BlindRush)Assert.True(members.Values.Any(m=>m.Kind==PlayableEntityKind.Tank));
                else Assert.True(members.Values.All(m=>m.Kind==PlayableEntityKind.Explorer));
                Assert.Greater(movement,command,"The ordered opening force moves in the threaded host.");
                Assert.Greater(damaged,movement,"The opening force reaches firing range and damages the observed economy.");
                if(captureMine)
                {
                    Assert.Greater(captured,damaged,"The attack weakens the mine and ordinary control hack transfers it.");
                    Assert.GreaterOrEqual(income,captured,"The very same mine now pays its new owner.");
                }
                else Assert.Greater(destroyed,damaged,"The visible enemy mine is destroyed.");
                Assert.Less(captureMine?income:destroyed,AiProfile.SecondsToTicks(ai.Value("opening.deadlineSeconds"),30));
                Assert.True(runtime.ParticipantView(Owner).Entities.Any(u=>u.Id==scout&&u.Health>0),"The separate starter remains available to scout or capture.");

                var capture=runtime.RequestCaptureBytes();
                while(!capture.IsCompleted&&Time.realtimeSinceStartupAsDouble<deadline)
                {routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.True(capture.IsCompletedSuccessfully,capture.Exception?.ToString());
                long capturedTick;
                using(var envelope=PlayableWorldState.Decode(capture.Result))
                {
                    Assert.AreEqual(seed,envelope.Seed);
                    Assert.Greater(envelope.RuntimeTransport.Length,0);
                    capturedTick=envelope.Tick;
                }
                Stop(ref runtime,ref routes);
                byte[] roundtrip=null;
                runtime=PlayableRuntime.RestoreBytesForTests(capture.Result,config,ai,r=>roundtrip=r.CaptureBeforeStartForTests());
                routes=new UnityHostRouteService();
                CollectionAssert.AreEqual(capture.Result,roundtrip);
                while(runtime.Latest.Tick<capturedTick+30&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline)
                {routes.Service(runtime,p.NavigationRequestsPerFrame);yield return null;}
                Assert.IsNull(runtime.Latest.Failure);
                Assert.GreaterOrEqual(runtime.Latest.Tick,capturedTick+30);
                TestContext.WriteLine($"Exact save tick {capturedTick}, continued to {runtime.Latest.Tick}.");
            }
            finally
            {
                PlayableRuntime.JournalFactory=null;
                if(withRelay)
                {
                    PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=null;
                    PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=null;
                }
                Stop(ref runtime,ref routes);
            }
        }

        private static void Stop(ref PlayableRuntime runtime,ref UnityHostRouteService routes)
        {
            if(runtime==null)return;
            var stopped=runtime;
            runtime=null;
            stopped.RequestStop();
            routes?.Dispose();
            routes=null;
            stopped.Dispose();
            Assert.True(System.Threading.SpinWait.SpinUntil(()=>stopped.IsStopped,5000));
            var authority=(PlayableAuthorityTick)typeof(PlayableRuntime)
                .GetField("authorityTick",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(stopped);
            authority.Domain.Navigation.ReleaseCommittedHostRoot();
            authority.Domain.DestroyOwnedNavigation();
        }
    }
}

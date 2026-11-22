using System;
using System.Collections;
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
    public sealed class AiOrbitalThreadedTests
    {
        [UnityTest,Timeout(90000)] public IEnumerator OrdinaryBotEarnsChoosesHacksAndCommandsCapturedUnit()
        {
            var data=PlayableProfile.Default.CopyData();data.orbitalThreshold1=1;data.tankWeaponDamage=60;data.tankWeaponReloadMilliseconds=10000;data.explorerHealth=100;
            var map=new AiTestMap();var profile=PlayableProfile.Create(data,map);
            var basis=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            var costs=new double[basis.Starts.Count,basis.Starts.Count];for(int a=0;a<basis.Starts.Count;a++)for(int b=0;b<basis.Starts.Count;b++)costs[a,b]=basis.RouteCost(a,b);
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,
                scenario:new[]{new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-10,0)),new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(-3,0))},terrain:map.Id);
            var ad=AiProfile.Initial.CopyData();
            foreach(var f in ad.fields){if(f.path=="orbital.hackValueScale")f.value=5;if(f.path=="orbital.scanUtility"||f.path=="orbital.mineUtility")f.value=1;if(f.path=="orbital.mineContactBonus"||f.path=="orbital.personalityBonus"||f.path=="orbital.choiceVariation")f.value=0;}
            var ai=new AiProfile(ad);var runtime=new PlayableRuntime(config,906,participantDifficulties:new[]{AiDifficulty.Veteran,AiDifficulty.Fighter},aiProfile:ai,startPaused:true);var routes=new UnityHostRouteService();
            try
            {
                float deadline=Time.realtimeSinceStartup+60;
                while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);yield return null;}
                Assert.True(runtime.IsPrepared);int target=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Enemy&&e.Kind==PlayableEntityKind.Explorer&&e.Position.X<0).Id;
                runtime.RequestPause(false);bool captured=false,moved=false;NavPoint capturePoint=default(NavPoint);
                while(Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    var unit=runtime.Latest.Entities.FirstOrDefault(e=>e.Id==target&&e.Owner==PlayableOwner.Player);
                    if(unit!=null){if(!captured){capturePoint=unit.Position;captured=true;}if(unit.CurrentOrder!=null&&unit.OrderStamp?.Origin==PlayableOrderOrigin.Ai&&AiOpponentSelector.Distance(capturePoint,unit.Position)>.1){moved=true;break;}}
                    yield return null;
                }
                TestContext.WriteLine("ORBITAL_RESULT tick="+runtime.Latest.Tick+" captured="+captured+" moved="+moved+" abilities="+string.Join(",",runtime.Latest.Orbital.Abilities.Select(a=>a.Id)));
                Assert.True(runtime.Latest.Orbital.Abilities.Any(a=>a.Id==OrbitalAbilities.ControlHack),"ordinary bot chooses hack");Assert.True(captured,"ordinary AI hack transfers ownership");Assert.True(moved,"captured actor receives and physically executes an AI order");
                var capture=runtime.RequestCaptureBytes();while(!capture.IsCompleted&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                Assert.True(capture.IsCompleted);Assert.False(capture.IsFaulted,capture.Exception?.ToString());
                using(var state=PlayableWorldState.Decode(capture.Result))Assert.Greater(state.AiAuthority.Length,0,"current authority persists orbital decisions");
            }
            finally{runtime.RequestStop();routes.Dispose();runtime.Dispose();}
        }
        [UnityTest,Timeout(90000)] public IEnumerator OrdinaryBotScansAndLearnsPublicEnemyArea()=>Support(1);
        [UnityTest,Timeout(90000)] public IEnumerator OrdinaryBotPlacesMineAndDamagesGroundEnemy()=>Support(2);
        private IEnumerator Support(int ability)
        {
            var data=PlayableProfile.Default.CopyData();data.orbitalThreshold1=1;data.tankWeaponDamage=60;data.tankWeaponReloadMilliseconds=10000;data.explorerHealth=100;
            if (ability == 2)
            {
                data.explorerSpeed = 3;
                data.explorerHealth = 300;
                data.tankWeaponDamage = 40;
            }
            var map=new AiTestMap();var profile=PlayableProfile.Create(data,map);
            var basis=map.Configuration(profile,7,new[]{new OfflineParticipant("player-1",1,1,OfflineControl.Ai),new OfflineParticipant("player-2",2,2,OfflineControl.Human)});
            // A bounded causal scan witness has two PUBLIC possible starts. On the full
            // eight-start map finding a particular enemy in one scan cannot be required fairly.
            var starts=ability==1?basis.Starts.Take(2).ToArray():basis.Starts.ToArray();
            var costs=new double[starts.Length,starts.Length];for(int a=0;a<starts.Length;a++)for(int b=0;b<starts.Length;b++)costs[a,b]=basis.RouteCost(a,b);
            var crossingStart = new NavPoint(0, -17);
            var units = ability == 1 ? new[] {
                new OfflineScenarioUnit(1, PlayableEntityKind.Tank, new NavPoint(-10, 0)),
                new OfflineScenarioUnit(2, PlayableEntityKind.Explorer, new NavPoint(-3, 0))
            } : new[] {
                new OfflineScenarioUnit(1, PlayableEntityKind.Tank, new NavPoint(-7, -17)),
                new OfflineScenarioUnit(1, PlayableEntityKind.Tank, new NavPoint(-8, -29)),
                new OfflineScenarioUnit(2, PlayableEntityKind.Explorer, crossingStart, -Math.PI / 2)
            };
            var outpost = basis.Sites.Single(site => site.Id == 17);
            var buildings = ability == 2 ? new[] { new OfflineScenarioBuilding(1, outpost.Kind, outpost.Id, 0, outpost.Position) } : null;
            var config=new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,basis.Roster.ToArray(),starts,basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,
                scenario:units,terrain:map.Id,scenarioBuildings:buildings);
            var ad=AiProfile.Initial.CopyData();foreach(var f in ad.fields){if(f.path=="orbital.hackValueScale")f.value=.01;if(f.path=="orbital.scanUtility")f.value=ability==1?1000:1;if(f.path=="orbital.mineUtility")f.value=ability==2?1000:1;if(f.path=="orbital.mineContactBonus")f.value=ability==2?1000:0;if(f.path=="orbital.personalityBonus"||f.path=="orbital.choiceVariation")f.value=0;}
            var runtime=new PlayableRuntime(config,908+ability,participantDifficulties:new[]{AiDifficulty.Veteran,AiDifficulty.Fighter},aiProfile:new AiProfile(ad),startPaused:true);var routes=new UnityHostRouteService();
            try
            {
                float deadline=Time.realtimeSinceStartup+60;
                while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline){routes.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);yield return null;}
                Assert.True(runtime.IsPrepared);var initial=PlayableAiObservation.From(runtime.Latest);var hidden=initial.PublicScoutObjectives.Where(t=>initial.Vision?.IsVisible(t.Approach)!=true).ToArray();
                // Roster runtime owns participant loops; legacy AiCheckpoint is intentionally null.
                var authority=(PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
                PlayableAiOwnerCheckpoint Checkpoint()=>authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="player-1");
                runtime.RequestPause(false);bool effect=false,learned=false;
                bool held = false, releaseSent = false, crossingSent = false, crossedMine = false;
                var intruder = ability == 2 ? runtime.ParticipantView("player-2").Entities.Single(u => u.Owner == PlayableOwner.Enemy &&
                    u.Position.Equals(crossingStart)) : null;
                NavPoint? placed = null;
                double closest = double.PositiveInfinity;
                PlayableEntitySnapshot crossed = intruder;
                PlayableCommandReceipt crossingReceipt = null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,profile.NavigationRequestsPerFrame);Assert.IsNull(runtime.Latest.Failure);Assert.IsNull(routes.LastProjectionFailure);
                    if(ability==1)
                    {
                        var view=runtime.Latest;bool scanning=view.ScanLines.Count>0;effect|=scanning;
                        var checkpoint=Checkpoint();long applied=checkpoint.Records.Where(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Kind==PlayableCommandKind.UseOrbital&&r.Status==PlayableAiDeliveryStatus.Applied).Select(r=>r.ApplicationTick).DefaultIfEmpty(-1).Max();
                        // Knowledge must arrive during the scan, before a regular scout can substitute.
                        learned|=scanning&&applied>=0&&checkpoint.Knowledge.Contacts.Any(c=>c.Visible&&c.LastSeenTick>=applied&&initial.Vision?.IsVisible(c.Position)!=true&&
                            !view.Entities.Any(u=>u.Owner==view.Owner&&AiOpponentSelector.Distance(u.Position,c.Position)<=PlayableUnitRules.Vision(profile,u.Kind)));
                    }
                    else
                    {
                        var view = runtime.Latest;
                        if (!held && !view.Paused)
                        {
                            Assert.True(runtime.TrySubmit(new PlayableCommand(910, 1, "player-2", PlayableCommandKind.Hold,
                                new[] { intruder.Id })).Accepted);
                            held = true;
                        }
                        var mine = view.Mines.FirstOrDefault(m => m.Owner == view.Owner);
                        if (mine != null && !releaseSent)
                        {
                            placed = mine.Point;
                            // Release the setup Hold before asking the ordinary host
                            // to route the hostile unit across the approach.
                            Assert.True(runtime.TrySubmit(new PlayableCommand(910, 2, "player-2", PlayableCommandKind.Stop,
                                new[] { intruder.Id })).Accepted);
                            releaseSent = true;
                        }
                        if (releaseSent && !crossingSent && runtime.ReceiptsAfter(1).Any(r =>
                            r.OwnerId == "player-2" && r.Sequence == 2 && r.Status == PlayableCommandStatus.Applied))
                        {
                            Assert.True(runtime.TrySubmit(new PlayableCommand(910, 3, "player-2", PlayableCommandKind.Move,
                                new[] { intruder.Id }, new NavPoint(0, placed.Value.Z - 2 * profile.ExplorerCollisionRadius))).Accepted);
                            crossingSent = true;
                        }
                        if (crossingSent)
                        {
                            crossingReceipt ??= runtime.ReceiptsAfter(2).FirstOrDefault(r => r.OwnerId == "player-2" && r.Sequence == 3 &&
                                r.Status != PlayableCommandStatus.Accepted);
                            var hostileView = runtime.ParticipantView("player-2");
                            var moving = hostileView.Entities.FirstOrDefault(u => u.Id == intruder.Id);
                            if (moving != null)
                            {
                                crossed = moving;
                                closest = Math.Min(closest, AiOpponentSelector.Distance(moving.Position, placed.Value));
                            }
                            crossedMine |= view.MatchEvents.Any(e => e.Kind == MatchEventKind.MineTriggered && e.Point.Equals(placed.Value)) &&
                                hostileView.MatchEvents.Any(e => e.Kind == MatchEventKind.MineHit);
                        }
                        effect |= crossedMine;
                        learned = effect && crossingReceipt?.Status == PlayableCommandStatus.Applied && placed.HasValue &&
                            AiOpponentSelector.Distance(crossingStart, crossed.Position) > profile.MineEnemyClearanceRadius &&
                            Math.Abs(placed.Value.X) < profile.ExplorerCollisionRadius && placed.Value.Z < crossingStart.Z - profile.MineEnemyClearanceRadius;
                    }
                    if(effect&&learned)break;yield return null;
                }
                TestContext.WriteLine("ORBITAL_SUPPORT ability="+ability+" tick="+runtime.Latest.Tick+" effect="+effect+" learned="+learned+" mine="+placed?.X+","+placed?.Z+" receipt="+crossingReceipt?.Status+":"+crossingReceipt?.Message+
                    " closest="+closest+" intruder="+crossed?.Position.X+","+crossed?.Position.Z+" hp="+crossed?.Health+
                    " nav="+crossed?.NavigationOutcome+" choices="+string.Join(",", runtime.Latest.Orbital.Abilities.Select(a=>a.Id))+" casts="+string.Join(",",Checkpoint().Records.Where(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Kind==PlayableCommandKind.UseOrbital).Select(r=>r.Status+"@"+r.ApplicationTick)));
                Assert.True(runtime.Latest.Orbital.Abilities.Any(a=>a.Id==ability),"ordinary bot chose requested opportunity class");Assert.True(effect,"autonomous ability has world effect");Assert.True(learned,ability==1?"scan updates real owner knowledge":"mine contacts ground enemy");
                Assert.True(Checkpoint().Records.Any(r=>r.Policy==AiOrbitalPolicy.Policy&&r.Kind==PlayableCommandKind.UseOrbital&&r.Status==PlayableAiDeliveryStatus.Applied),"ordinary authority applied the ability");
            }
            finally{runtime.RequestStop();routes.Dispose();runtime.Dispose();}
        }
        [UnityTest, Timeout(90000)]
        public IEnumerator OrdinaryBotDisablesATowerAheadOfItsAttack()
        {
            const long generation = 917;
            const int seed = 7;
            var data = PlayableProfile.Default.CopyData();
            data.orbitalThreshold1 = data.orbitalThreshold2 = data.orbitalThreshold3 = data.orbitalThreshold4 = 1;
            data.tankVisionRange = 26;
            data.startingCredits = 3000;
            data.arenaHalfExtent = 50;
            data.centralObstacleHalfDepth = 2;
            data.passageHalfWidth = 1;
            var profile = PlayableProfile.Create(data);
            var starts = new[] {
                new OfflineStart("west", 1, new NavPoint(-25, 12), new NavPoint(-25, 0), pin: 1),
                new OfflineStart("east", 2, new NavPoint(24, 12), new NavPoint(24, 0), pin: 2)
            };
            var home = new TerritorySite(1, PlayableBuildingKind.Headquarters, starts[0].Position, profile);
            var enemy = new TerritorySite(2, PlayableBuildingKind.Headquarters, starts[1].Position, profile, Math.PI);
            var tower = enemy.DefenseSlots(profile).OrderBy(slot => AiOpponentSelector.Distance(slot.Position, new NavPoint(-7, 12))).First();
            var buildings = new[] {
                new OfflineScenarioBuilding(1, PlayableBuildingKind.Factory, home.Id, home.Slots[0].Id, home.Slots[0].Position, home.Slots[0].Heading),
                new OfflineScenarioBuilding(2, PlayableBuildingKind.SentryTurret, enemy.Id, tower.Id, tower.Position, tower.Heading)
            };
            var army = Enumerable.Range(0, 8).Select(i => new OfflineScenarioUnit(1, PlayableEntityKind.Tank,
                new NavPoint(-7 - (i % 2) * 3, 10 + (i / 2) * 3))).ToArray();
            var units = army.Append(new OfflineScenarioUnit(2, PlayableEntityKind.Tank, new NavPoint(-3, 11))).ToArray();
            var config = new OfflineMatchConfiguration(profile, "orbital-tower-attack-v1", "flat-public-v1", "UnityHostRouteService", seed,
                new[] { new OfflineParticipant("player-1", 1, 1, OfflineControl.Ai), new OfflineParticipant("player-2", 2, 2, OfflineControl.Human) },
                starts, new[] { home, enemy }, PlayableMap.StaticObstacles(profile), new double[,] { { 0, 49 }, { 49, 0 } },
                scenario: units, scenarioBuildings: buildings);
            using var runtime = new PlayableRuntime(config, generation, aiDifficulty: AiDifficulty.Veteran, startPaused: true);
            using var routes = new UnityHostRouteService();
            var authority = (PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runtime);
            float deadline = Time.realtimeSinceStartup + 65;
            while (!runtime.IsPrepared && Time.realtimeSinceStartup < deadline)
            {
                routes.Service(runtime, profile.NavigationRequestsPerFrame);
                Assert.IsNull(runtime.Latest.Failure);
                yield return null;
            }
            Assert.True(runtime.IsPrepared);
            runtime.RequestPause(false);
            long disabledTick = -1;
            double range = TerritoryRules.AiDefenseRange(profile, PlayableBuildingKind.SentryTurret);
            int approaching = 0;
            int entered = 0;
            var outsideAtCast = new System.Collections.Generic.HashSet<int>();
            while (Time.realtimeSinceStartup < deadline && !runtime.IsStopped && entered == 0)
            {
                routes.Service(runtime, profile.NavigationRequestsPerFrame);
                var view = runtime.Latest;
                Assert.IsNull(view.Failure);
                Assert.IsNull(routes.LastProjectionFailure);
                var defense = view.Buildings.FirstOrDefault(b => b.Kind == PlayableBuildingKind.SentryTurret && b.Owner != view.Owner);
                var advancing = view.Entities.Where(u => u.Owner == view.Owner && u.Kind == PlayableEntityKind.Tank &&
                    (u.CurrentOrder?.Kind == PlayableTacticalOrderKind.Attack || u.CurrentOrder?.Kind == PlayableTacticalOrderKind.AttackMove)).ToArray();
                approaching = Math.Max(approaching, advancing.Length);
                if (defense?.SystemsDisabled == true)
                {
                    if (disabledTick < 0)
                    {
                        disabledTick = view.Tick;
                        foreach (var tank in advancing.Where(u => AiOpponentSelector.Distance(u.Position, defense.Position) > range))
                            outsideAtCast.Add(tank.Id);
                    }
                    else entered = advancing.Count(u => outsideAtCast.Contains(u.Id) && AiOpponentSelector.Distance(u.Position, defense.Position) <= range);
                }
                yield return null;
            }
            var result = runtime.Latest;
            var records = authority.CaptureDiagnosticCheckpoints().Single(c => c.OwnerId == "player-1").Records;
            TestContext.WriteLine($"SYSTEM_FAILURE tick={result.Tick} level={result.Orbital.Level} abilities={string.Join(",", result.Orbital.Abilities.Select(a => a.Id))} " +
                $"approaching={approaching} disabledTick={disabledTick} outsideAtCast={outsideAtCast.Count} entered={entered}");
            Assert.That(result.Orbital.Abilities.Select(a => a.Id), Does.Contain(OrbitalAbilities.SystemFailure));
            Assert.Greater(disabledTick, 0, "the bot disabled the enemy tower in the match");
            Assert.Greater(outsideAtCast.Count, 0, "an attacking tank was outside tower range at application");
            Assert.Greater(entered, 0, "that tank entered the tower range while the shutdown remained active");
            Assert.True(records.Any(r => r.Policy == AiOrbitalPolicy.Policy && r.Kind == PlayableCommandKind.UseOrbital &&
                r.Status == PlayableAiDeliveryStatus.Applied && r.ApplicationTick <= disabledTick));
        }

        [UnityTest,Timeout(90000)]
        public IEnumerator OrdinaryBotBuysReinforcementsWhenItsArmyCanUseThem()
        {
            var data = PlayableProfile.Default.CopyData();
            data.orbitalThreshold1 = data.orbitalThreshold2 = data.orbitalThreshold3 = data.orbitalThreshold4 = 1;
            data.tankWeaponDamage = 60;
            data.tankWeaponReloadMilliseconds = 10000;
            data.explorerHealth = 100;
            data.startingCredits = 3000;
            var map = new AiTestMap();
            var profile = PlayableProfile.Create(data, map);
            var basis = map.Configuration(profile,7,new[] {
                new OfflineParticipant("player-1",1,1,OfflineControl.Ai),
                new OfflineParticipant("player-2",2,2,OfflineControl.Human)
            });
            var costs = new double[basis.Starts.Count,basis.Starts.Count];
            for (int a=0;a<basis.Starts.Count;a++)
                for (int b=0;b<basis.Starts.Count;b++) costs[a,b]=basis.RouteCost(a,b);
            var config = new OfflineMatchConfiguration(profile,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,7,
                basis.Roster.ToArray(),basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),costs,
                scenario:new[] {
                    new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-10,0)),
                    new OfflineScenarioUnit(2,PlayableEntityKind.Explorer,new NavPoint(-3,0))
                },terrain:map.Id);
            var runtime = new PlayableRuntime(config,916,participantDifficulties:new[] { AiDifficulty.Veteran,AiDifficulty.Fighter },startPaused:true);
            var routes = new UnityHostRouteService();
            try
            {
                float deadline = Time.realtimeSinceStartup+60;
                while (!runtime.IsPrepared && Time.realtimeSinceStartup<deadline)
                {
                    routes.Service(runtime,profile.NavigationRequestsPerFrame);
                    Assert.IsNull(runtime.Latest.Failure);
                    yield return null;
                }
                Assert.True(runtime.IsPrepared);
                runtime.RequestPause(false);
                bool FirstWavePaid() => runtime.Latest.Orbital.ReinforcementQuote.Count==2 &&
                    runtime.Latest.Orbital.ReinforcementQuote.Kind==PlayableEntityKind.Tank;
                while (Time.realtimeSinceStartup<deadline && !FirstWavePaid())
                {
                    routes.Service(runtime,profile.NavigationRequestsPerFrame);
                    Assert.IsNull(runtime.Latest.Failure);
                    yield return null;
                }
                var result = runtime.Latest;
                TestContext.WriteLine($"REINFORCEMENT_BOT tick={result.Tick} level={result.Orbital.Level} credits={result.Credits} reserved={result.Population?.Reserved}");
                Assert.That(result.Orbital.Abilities.Select(a=>a.Id),Does.Contain(OrbitalAbilities.Reinforcement));
                Assert.AreEqual(PlayableEntityKind.Tank,result.Orbital.ReinforcementQuote.Kind,"ordinary bot successfully paid for first wave");
                Assert.AreEqual(3,result.Entities.Count(unit=>unit.Owner==result.Owner&&unit.Kind==PlayableEntityKind.Explorer&&unit.MaterializationProgress<1));
                Assert.Greater(result.Orbital.Abilities.Single(a=>a.Id==OrbitalAbilities.Reinforcement).Remaining,0);
                var authority = (PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
                Assert.True(authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="player-1").Records.Any(record=>
                    record.Policy==AiOrbitalPolicy.Policy&&record.Kind==PlayableCommandKind.UseOrbital&&record.Status==PlayableAiDeliveryStatus.Applied));
            }
            finally
            {
                runtime.RequestStop();
                routes.Dispose();
                runtime.Dispose();
            }
        }
    }
}

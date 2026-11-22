using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

namespace Spacewars.Tests.PlayMode
{
    public sealed class MultimapTpsSoakTests
    {
        private const int Seed = 7;
        private const int HorizonTicks = 18000;
        private const double WallLimit = 600;

        [UnityTest, Explicit, Timeout(630000)]
        public IEnumerator MossHighlandFourBots() => Run(
            new NativeLobbyConfiguration { MossHighland = true },
            new MossHighlandMap(ReadResource<MossHighlandProfileData>("MossHighlandProfile")), 4);

        [UnityTest, Explicit, Timeout(630000)]
        public IEnumerator AntennaCitySixBots() => Run(
            new NativeLobbyConfiguration { AntennaCity = true },
            new AntennaCityMap(ReadResource<AntennaCityProfileData>("AntennaCityProfile")), 6);

        [UnityTest, Explicit, Timeout(630000)]
        public IEnumerator CrimsonGroveEightBots() => Run(
            new NativeLobbyConfiguration { CrimsonGrove = true },
            new CrimsonGroveMap(ReadResource<CrimsonGroveProfileData>("CrimsonGroveProfile")), 8);

        [UnityTest, Explicit, Timeout(90000)]
        public IEnumerator CrimsonTerracePocketFromJournal()
        {
            const int horizon = 1200;
            var root = Environment.GetEnvironmentVariable("SPACEWARS_TPS_SOAK_OUTPUT");
            Assert.True(Path.IsPathRooted(root ?? ""));
            Assert.True(NativeSilentStartup.IsSilent);
            var map = new CrimsonGroveMap(ReadResource<CrimsonGroveProfileData>("CrimsonGroveProfile"));
            var basis = PlayableProfile.Create(ReadResource<PlayableProfileData>("PlayableProfile"), map);
            var store = NativeBalanceStore.ForApplication(basis, Path.Combine(root, "balance.json"));
            store.Select(NativeBalanceStore.DefaultId,12);
            Assert.AreEqual(12, store.SelectedVersion);
            var profile = PlayableProfile.Create(store.Active.CopyData(), map, store.Active.DisplayName);
            const int logicalOwner = 5;
            const string ownerId = "journal-owner";
            var roster = Enumerable.Range(1, 8).Select(i=>new OfflineParticipant(
                i==logicalOwner ? ownerId : "journal-idle-"+i, i, i,
                i==logicalOwner ? OfflineControl.Human : OfflineControl.Scenario)).ToArray();
            var authored = map.Configuration(profile, Seed, roster);
            var starts = authored.Starts.Select(s=>s.SiteId!=logicalOwner ? s :
                new OfflineStart(s.Id,s.SiteId,s.Position,new NavPoint(-37.48723459992169,-43.93231011283889),.9424941073958358,s.PinnedLogicalPlayer)).ToArray();
            var home = authored.Sites.Single(s=>s.Id==logicalOwner);
            var buildingKinds = new[] { PlayableBuildingKind.Refinery, PlayableBuildingKind.Refinery, PlayableBuildingKind.Factory };
            var buildings = home.Slots.Take(buildingKinds.Length).Select((slot,i)=>new OfflineScenarioBuilding(
                logicalOwner,buildingKinds[i],home.Id,slot.Id,slot.Position,slot.Heading)).ToArray();
            var tank = new NavPoint(-52.525640411033024,-124.6528208320984);
            var explorer = new NavPoint(-54.52564041103302,-124.6528208320984);
            var obstacle = new NavPoint(-52.10961704844559,-121.91511886843226);
            var goal = new NavPoint(-37.420620433565944,-43.84062043356596);
            var units = new[] {
                new OfflineScenarioUnit(logicalOwner,PlayableEntityKind.Tank,tank),
                new OfflineScenarioUnit(logicalOwner,PlayableEntityKind.Explorer,explorer,-Math.PI/2),
                new OfflineScenarioUnit(logicalOwner,PlayableEntityKind.Tank,obstacle,1.523764746584608)
            };
            var configuration = new OfflineMatchConfiguration(profile,"crimson-terrace4-group80-journal-fixture",
                authored.MapIdentity,authored.RouteProvenance,Seed,roster,starts,authored.Sites.ToArray(),
                authored.Obstacles.ToArray(),new double[8,8],new[] { PlayableRuntime.LocalSpectatorId },units,map.Id,buildings);
            PlayableRuntime runtime = null;
            var routes = new UnityHostRouteService();
            var output = new List<object>(horizon/15+4);
            TestMatchLogSession.StartDefault(Path.Combine(root,"TestLogs"));
            try
            {
                runtime = new PlayableRuntime(configuration,1,startPaused:true,aiProfile:store.ActiveAiProfile);
                var startup=Stopwatch.StartNew();
                while ((!runtime.IsPrepared || !runtime.Latest.Paused) && !runtime.IsStopped && startup.Elapsed.TotalSeconds<20)
                {routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                Assert.True(runtime.IsPrepared && runtime.Latest.Paused,runtime.Latest.Failure);
                var initialView = runtime.ParticipantView(ownerId);
                var initial = initialView.Entities.Where(e=>e.Owner==initialView.Owner).ToArray();
                int[] ids = {initial.Single(e=>e.Position.Equals(tank)).Id,initial.Single(e=>e.Position.Equals(explorer)).Id};
                int peerId = initial.Single(e=>e.Position.Equals(obstacle)).Id;
                Assert.True(ids.All(id=>initial.Single(e=>e.Id==id).Location.Value.SurfaceId=="grove/terrace/4"));
                Assert.True(map.TryLocate(goal,profile.TankCollisionRadius,out var target));
                Assert.AreEqual("grove/basin",target.SurfaceId);
                TestContext.WriteLine($"POCKET setup: movers={string.Join(",",ids)} peer={peerId} goal={target} profile={store.ActiveIdentity}");
                var domain=GetDomain(runtime);
                const System.Reflection.BindingFlags privateField = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var fields = new[] { "noProgressTicks", "blockedSeconds", "routeIndex", "localIndex", "localRoute", "route" }
                    .ToDictionary(name=>name,name=>typeof(NavUnit).GetField(name,privateField));
                Assert.True(fields.Values.All(field=>field!=null));
                domain.CombatStepForQa=()=>{
                    if(domain.Tick%15!=0||domain.Tick>horizon)return;
                    var nav=domain.Navigation;
                    var rows=ids.Append(peerId).Select(id=>{
                        if(!nav.Crowd.TryGet(id,out var actor))return null;
                        var group=nav.GroupFor(id);
                        var member=group?.Members.SingleOrDefault(m=>m.Entity==id);
                        return (object)new { id, actor.Location, actor.Position, actor.GoalLocation,
                            actor.Moving, actor.Outcome, actor.Heading,
                            NoProgress=fields["noProgressTicks"].GetValue(actor), BlockedSeconds=fields["blockedSeconds"].GetValue(actor),
                            actor.InstalledRequest, actor.InstalledGroupId,
                            RouteIndex=fields["routeIndex"].GetValue(actor), LocalIndex=fields["localIndex"].GetValue(actor),
                            Local=((NavPoint[])fields["localRoute"].GetValue(actor)).Length,
                            Global=((NavPoint[])fields["route"].GetValue(actor)).Length, Pending=nav.IsPending(id),
                            Phase=member?.Phase, Recast=member?.Recast, Plan=group?.FormationPlan?.Phase };
                    }).ToArray();
                    output.Add(new { domain.Tick, Rows=rows });
                };
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                runtime.RequestPause(false);
                var submission = runtime.TrySubmit(new PlayableCommand(1,1,ownerId,PlayableCommandKind.AttackMove,ids,goal));
                Assert.True(submission.Accepted,submission.Status.ToString());
                var timer=Stopwatch.StartNew();
                while (runtime.Latest.Tick<horizon&&!runtime.IsStopped&&timer.Elapsed.TotalSeconds<45)
                {routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                Assert.IsNull(runtime.Latest.Failure);
                Assert.GreaterOrEqual(runtime.Latest.Tick,horizon);
                var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted);
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                var end=runtime.ParticipantView(ownerId);
                foreach (int id in ids)
                {
                    var unit=end.Entities.Single(e=>e.Id==id);
                    Assert.AreEqual("grove/basin",unit.Location.Value.SurfaceId,"The original AttackMove must physically leave the terrace.");
                    Assert.AreEqual(NavigationOutcome.Arrived,unit.NavigationOutcome);
                    Assert.AreEqual(1,unit.Completion?.Order.Sequence,"Arrival must complete the original AttackMove.");
                    Assert.AreEqual("grove/basin",unit.Completion?.AssignedLocation.SurfaceId);
                }
            }
            finally
            {
                runtime?.RequestStop();
                bool stopped=runtime==null||SpinWait.SpinUntil(()=>runtime.IsStopped,5000);
                routes.Dispose();
                runtime?.Dispose();
                TestMatchLogSession.Shutdown();
                if(runtime!=null){
                    var d=GetDomain(runtime);
                    d.CombatStepForQa=null;
                    Assert.True(SpinWait.SpinUntil(()=>!d.Navigation.HasCertifiedHost,5000));
                    d.Navigation.ReleaseCommittedHostRoot();
                    d.DestroyOwnedNavigation();
                }
                File.WriteAllText(Path.Combine(root,"pocket-frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(output));
                Assert.True(stopped);
            }
        }

        [UnityTest, Explicit, Timeout(90000)]
        public IEnumerator AntennaFactoryCohortFromJournal()
        {
            const int horizon = 1200;
            const string ownerId = "journal-owner";
            var root = Environment.GetEnvironmentVariable("SPACEWARS_TPS_SOAK_OUTPUT");
            Assert.True(Path.IsPathRooted(root ?? ""));
            Assert.True(NativeSilentStartup.IsSilent);
            var map = new AntennaCityMap(ReadResource<AntennaCityProfileData>("AntennaCityProfile"));
            var basis = PlayableProfile.Create(ReadResource<PlayableProfileData>("PlayableProfile"), map);
            var store = NativeBalanceStore.ForApplication(basis, Path.Combine(root, "balance.json"));
            Assert.AreEqual(13, store.SelectedVersion);
            var profile = PlayableProfile.Create(store.Active.CopyData(), map, store.Active.DisplayName);
            var roster = Enumerable.Range(1,6).Select(i=>new OfflineParticipant(
                i==1?ownerId:"journal-idle-"+i,i,i,i==1?OfflineControl.Human:OfflineControl.Scenario)).ToArray();
            var authored = map.Configuration(profile, Seed, roster);
            var starts = authored.Starts.Select(s=>s.SiteId!=6?s:
                new OfflineStart(s.Id,s.SiteId,s.Position,s.ExplorerAnchor,s.Heading,1)).ToArray();
            var home = authored.Sites.Single(s=>s.Id==6);
            var kinds = new[] {PlayableBuildingKind.Refinery,PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,
                PlayableBuildingKind.Refinery};
            var buildings = home.Slots.Take(kinds.Length).Select((slot,i)=>new OfflineScenarioBuilding(
                1,kinds[i],home.Id,slot.Id,slot.Position,slot.Heading)).ToArray();
            var units = new[] {
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-119.32436195183753,2.8555987342477005),-2.1928069461169604),
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-121.25305589082963,4.725747891466798),1.5545476862322856),
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-119.98921342813492,4.335326992456558),-0.8179360983159505),
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-118.87544263801543,3.9667357155930656),-2.182880414571546),
                new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-115.90623438849646,-3.226035970923078),-0.21708599978701984),
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-115.89138953656283,3.1818673559184827),0.0),
                new OfflineScenarioUnit(1,PlayableEntityKind.Explorer,new NavPoint(-117.99138953656283,3.1818673559184827),-1.5707963267948966),
            };
            var goal = new NavPoint(-38.61332099679081,-25.999999999999996);
            var configuration = new OfflineMatchConfiguration(profile,"antenna-west-factory-cohort-journal-fixture",
                authored.MapIdentity,authored.RouteProvenance,Seed,roster,starts,authored.Sites.ToArray(),
                authored.Obstacles.ToArray(),new double[6,6],new[]{PlayableRuntime.LocalSpectatorId},units,map.Id,buildings);

            PlayableRuntime runtime = null;
            var routes = new UnityHostRouteService();
            TestMatchLogSession.StartDefault(Path.Combine(root,"TestLogs"));
            try
            {
                runtime = new PlayableRuntime(configuration,1,startPaused:true,aiProfile:store.ActiveAiProfile);
                var startup = Stopwatch.StartNew();
                while ((!runtime.IsPrepared || !runtime.Latest.Paused) && !runtime.IsStopped && startup.Elapsed.TotalSeconds<20)
                {routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                Assert.True(runtime.IsPrepared && runtime.Latest.Paused,runtime.Latest.Failure);
                var initial = runtime.ParticipantView(ownerId).Entities.Where(e=>e.Owner==PlayableOwner.Player).ToArray();
                var ids = units.Select(u=>initial.Single(e=>e.Position.Equals(u.Position)).Id).ToArray();
                int[] movers = ids.Take(3).ToArray();
                Assert.True(map.TryLocate(goal,profile.ExplorerCollisionRadius,out var target));
                Assert.AreEqual("city/plaza/2",target.SurfaceId);
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                runtime.RequestPause(false);
                var submission=runtime.TrySubmit(new PlayableCommand(1,1,ownerId,PlayableCommandKind.AttackMove,movers,goal));
                Assert.True(submission.Accepted,submission.Status.ToString());
                var timer=Stopwatch.StartNew();
                while(runtime.Latest.Tick<horizon&&!runtime.IsStopped&&timer.Elapsed.TotalSeconds<45)
                {routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                Assert.IsNull(runtime.Latest.Failure);
                Assert.GreaterOrEqual(runtime.Latest.Tick,horizon);
                var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted);
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                var entities=runtime.ParticipantView(ownerId).Entities;
                foreach(int id in movers)
                {
                    var unit=entities.Single(e=>e.Id==id);
                    Assert.AreEqual(NavigationOutcome.Arrived,unit.NavigationOutcome,$"Scout {id} must leave the factory pocket.");
                    Assert.AreEqual(1,unit.Completion?.Order.Sequence);
                    Assert.AreEqual("city/plaza/2",unit.Completion?.AssignedLocation.SurfaceId);
                }
            }
            finally
            {
                runtime?.RequestStop();
                bool stopped=runtime==null||SpinWait.SpinUntil(()=>runtime.IsStopped,5000);
                routes.Dispose();
                runtime?.Dispose();
                TestMatchLogSession.Shutdown();
                if(runtime!=null)
                {
                    var d=GetDomain(runtime);
                    Assert.True(SpinWait.SpinUntil(()=>!d.Navigation.HasCertifiedHost,5000));
                    d.Navigation.ReleaseCommittedHostRoot();
                    d.DestroyOwnedNavigation();
                }
                Assert.True(stopped);
            }
        }

        [UnityTest, Explicit, Timeout(90000)]
        public IEnumerator AntennaPlazaTanksFromJournal()
        {
            const int horizon = 2400;
            const string ownerId = "journal-owner";
            var root = Environment.GetEnvironmentVariable("SPACEWARS_TPS_SOAK_OUTPUT");
            Assert.True(Path.IsPathRooted(root ?? ""));
            Assert.True(NativeSilentStartup.IsSilent);
            var map = new AntennaCityMap(ReadResource<AntennaCityProfileData>("AntennaCityProfile"));
            var basis = PlayableProfile.Create(ReadResource<PlayableProfileData>("PlayableProfile"), map);
            var store = NativeBalanceStore.ForApplication(basis, Path.Combine(root, "balance.json"));
            Assert.AreEqual(13, store.SelectedVersion);
            var profile = PlayableProfile.Create(store.Active.CopyData(), map, store.Active.DisplayName);
            var roster = Enumerable.Range(1,6).Select(i=>new OfflineParticipant(
                i==1?ownerId:"journal-idle-"+i,i,i,i==1?OfflineControl.Human:OfflineControl.Scenario)).ToArray();
            var authored = map.Configuration(profile, Seed, roster);
            var starts = authored.Starts.Select(s=>s.SiteId!=4?s:
                new OfflineStart(s.Id,s.SiteId,s.Position,s.ExplorerAnchor,s.Heading,1)).ToArray();
            var home = authored.Sites.Single(s=>s.Id==4);
            var kinds = new[] {PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Factory};
            var buildings = home.Slots.Take(kinds.Length).Select((slot,i)=>new OfflineScenarioBuilding(
                1,kinds[i],home.Id,slot.Id,slot.Position,slot.Heading)).ToArray();
            // Starting poses and the destination come from the six-player Antenna journal.
            var units = new[] {
                new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(52.667559126821835,-103.11161982766487),-3.279380608287738),
                new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(54.741389536562856,-103.10244431423155),-1.5707963267948966),
                new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(56.86294826002474,-103.0976342953675),-0.25514436634820237),
            };
            var goal = new NavPoint(51.453320996790815,-25.999999999999996);
            var configuration = new OfflineMatchConfiguration(profile,"antenna-plaza-tank-cohort-journal-fixture",
                authored.MapIdentity,authored.RouteProvenance,Seed,roster,starts,authored.Sites.ToArray(),
                authored.Obstacles.ToArray(),new double[6,6],new[]{PlayableRuntime.LocalSpectatorId},units,map.Id,buildings);

            PlayableRuntime runtime = null;
            var routes = new UnityHostRouteService();
            TestMatchLogSession.StartDefault(Path.Combine(root,"TestLogs"));
            try
            {
                runtime = new PlayableRuntime(configuration,1,startPaused:true,aiProfile:store.ActiveAiProfile);
                var startup = Stopwatch.StartNew();
                while ((!runtime.IsPrepared || !runtime.Latest.Paused) && !runtime.IsStopped && startup.Elapsed.TotalSeconds<20)
                {routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                Assert.True(runtime.IsPrepared && runtime.Latest.Paused,runtime.Latest.Failure);
                var initial = runtime.ParticipantView(ownerId).Entities.Where(e=>e.Owner==PlayableOwner.Player).ToArray();
                var movers = units.Select(unit => initial.Single(entity => entity.Position.Equals(unit.Position)).Id).ToArray();
                Assert.True(map.TryLocate(goal, profile.TankCollisionRadius, out var target));
                Assert.AreEqual("city/plaza/1", target.SurfaceId);
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId,4);
                runtime.RequestPause(false);
                var submission=runtime.TrySubmit(new PlayableCommand(1,1,ownerId,PlayableCommandKind.AttackMove,movers,goal).AsHuman());
                Assert.True(submission.Accepted,submission.Status.ToString());
                var timer=Stopwatch.StartNew();
                while(runtime.Latest.Tick<horizon&&!runtime.IsStopped&&timer.Elapsed.TotalSeconds<45)
                {routes.Service(runtime,profile.NavigationRequestsPerFrame);yield return null;}
                Assert.IsNull(runtime.Latest.Failure);
                Assert.GreaterOrEqual(runtime.Latest.Tick,horizon);
                var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1&&r.Status!=PlayableCommandStatus.Accepted);
                Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                var entities=runtime.ParticipantView(ownerId).Entities;
                var assigned = new HashSet<NavPoint>();
                foreach (int id in movers)
                {
                    var unit=entities.Single(e=>e.Id==id);
                    Assert.AreEqual(NavigationOutcome.Arrived, unit.NavigationOutcome, $"Tank {id} must reach its assigned plaza slot.");
                    var completion = unit.Completion;
                    Assert.NotNull(completion);
                    Assert.AreEqual(1, completion.Order.Sequence);
                    Assert.AreEqual("city/plaza/1", completion.AssignedLocation.SurfaceId);
                    Assert.True(assigned.Add(completion.AssignedLocation.Position));
                }
            }
            finally
            {
                runtime?.RequestStop();
                bool stopped=runtime==null||SpinWait.SpinUntil(()=>runtime.IsStopped,5000);
                routes.Dispose();
                runtime?.Dispose();
                TestMatchLogSession.Shutdown();
                if(runtime!=null)
                {
                    var d=GetDomain(runtime);
                    Assert.True(SpinWait.SpinUntil(()=>!d.Navigation.HasCertifiedHost,5000));
                    d.Navigation.ReleaseCommittedHostRoot();
                    d.DestroyOwnedNavigation();
                }
                Assert.True(stopped);
            }
        }

        private static T ReadResource<T>(string name) => JsonUtility.FromJson<T>(Resources.Load<TextAsset>(name).text);

        private static IEnumerator Run(NativeLobbyConfiguration setup, IPlayableTerrain map, int playerCount)
        {
            var root = Environment.GetEnvironmentVariable("SPACEWARS_TPS_SOAK_OUTPUT");
            Assert.IsTrue(Path.IsPathRooted(root ?? ""), "Set SPACEWARS_TPS_SOAK_OUTPUT to a new absolute output directory.");
            string output = Path.Combine(root, map.Id);
            Assert.IsFalse(Directory.Exists(output), "Use a fresh directory for each measurement.");
            Directory.CreateDirectory(output);
            Assert.IsTrue(NativeSilentStartup.IsSilent);
            Assert.IsNull(TestMatchLogSession.CurrentDirectory);

            setup.HumanPresent = false;
            setup.HasExplicitSeed = true;
            setup.ExplicitSeed = Seed;
            setup.InitializeParticipants();
            Assert.AreEqual(playerCount, setup.Participants.Count);
            var basis = PlayableProfile.Create(ReadResource<PlayableProfileData>("PlayableProfile"), map);
            var store = NativeBalanceStore.ForApplication(basis, Path.Combine(output, "balance.json"));
            Assert.AreEqual(NativeBalanceStore.DefaultId, store.SelectedId);
            store.Select(NativeBalanceStore.DefaultId,12);
            Assert.AreEqual(12, store.SelectedVersion);
            var profile = PlayableProfile.Create(store.Active.CopyData(), map, store.Active.DisplayName);
            Assert.IsNull(setup.Validate(profile, true, true));
            File.WriteAllText(Path.Combine(output, "profile.json"), JsonUtility.ToJson(profile.CopyData(), true));
            File.WriteAllText(Path.Combine(output, "ai-profile.json"), JsonUtility.ToJson(store.ActiveAiProfile.CopyData(), true));
            TestMatchLogSession.StartDefault(Path.Combine(output, "TestLogs"));
            PlayableRuntime runtime = null;
            var routes = new UnityHostRouteService();
            var follow = Environment.GetEnvironmentVariable("SPACEWARS_TPS_TRACE_FOLLOW") == "1"
                ? new FollowTrace() : null;
            try
            {
                Assert.IsNotNull(TestMatchLogSession.CurrentDirectory);
                runtime = PlayableRuntime.CreateLobbyMatch(profile, 1, setup, startPaused: true, aiProfile: store.ActiveAiProfile);
                var preparation = Stopwatch.StartNew();
                while ((!runtime.IsPrepared || !runtime.Latest.Paused) && !runtime.IsStopped && preparation.Elapsed.TotalSeconds < 20)
                {
                    routes.Service(runtime, profile.NavigationRequestsPerFrame);
                    yield return null;
                }
                Assert.IsTrue(runtime.IsPrepared && runtime.Latest.Paused, runtime.Latest.Failure);
                preparation.Stop();
                routes.Service(runtime, profile.NavigationRequestsPerFrame);
                var start = runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Overview;
                Assert.AreEqual(playerCount, start.Participants.Count);
                Assert.AreEqual(0, start.Tick);
                Assert.AreEqual(12, start.ProfileRevision);
                Assert.AreEqual(Seed, start.Seed);
                var origins = start.Entities.ToDictionary(e => e.Id, e => e.Position);
                var movingOwners = new HashSet<PlayableOwner>();
                runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId, 4);
                Assert.AreEqual(120, runtime.TargetTicksPerSecond, 1e-9);

                using var samples = new StreamWriter(Path.Combine(output, "ticks.csv")) { AutoFlush = true };
                using var units = new StreamWriter(Path.Combine(output, "units.csv"));
                samples.WriteLine("wall,tick,actors,buildings,tick_ms,max_tick_ms,nav_pending,barrier_polls,solver_epoch,pumps,gc0,gc1,max_gap,authority_geometry,wrecks,outcome");
                units.WriteLine("tick,id,owner,kind,x,z,outcome,moving,held,departing,stasis,weapon,target,seq,stamp_source,order,goal_x,goal_z,altitude,active_seq,pending_seq,waiting_viable,order_target");
                var clock = Stopwatch.StartNew();
                var domain = GetDomain(runtime);
                if (follow != null) domain.FollowReadbackForTests = follow.Record;
                runtime.RequestPause(false);
                double nextSample = 0, lastChange = 0, maxGap = 0;
                long nextUnits = 0, lastTick = 0;
                bool navReadoutEnabled = false;
                long navReadoutStart = map is AntennaCityMap ? 8500 : HorizonTicks - QaPerformanceNavigationSteps.Capacity;
                PlayableSnapshot view;
                while (true)
                {
                    routes.Service(runtime, profile.NavigationRequestsPerFrame);
                    var frame = runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId);
                    view = frame.Overview;
                    var publication = routes.CertifiedPublication;
                    Assert.IsNull(view.Failure);
                    Assert.Zero(view.Metrics.Errors);
                    Assert.IsNull(publication?.Failure);
                    double elapsed = clock.Elapsed.TotalSeconds;
                    if (view.Tick != lastTick)
                    {
                        maxGap = Math.Max(maxGap, elapsed - lastChange);
                        lastTick = view.Tick;
                        lastChange = elapsed;
                    }
                    if (elapsed >= nextSample)
                    {
                        samples.WriteLine(FormattableString.Invariant(
                            $"{elapsed:R},{view.Tick},{view.Entities.Count},{view.Buildings.Count},{view.Metrics.TickCpuMilliseconds:R},{view.Metrics.MaximumTickCpu:R},{view.Metrics.NavigationPending},{runtime.BarrierPolls},{publication?.Epoch},{publication?.Pumps},{GC.CollectionCount(0)},{GC.CollectionCount(1)},{Math.Max(maxGap, elapsed - lastChange):R},{runtime.NavigationGeometry.Revision},{view.Wrecks.Count},{view.Outcome}"));
                        nextSample = elapsed + .25;
                        maxGap = 0;
                    }
                    if (view.Tick >= nextUnits)
                    {
                        foreach (var perspective in frame.Players)
                        foreach (var entity in perspective.Entities)
                        {
                            if (entity.Owner != perspective.Owner) continue;
                            if (entity.OrderStamp?.Origin == PlayableOrderOrigin.Ai &&
                                origins.TryGetValue(entity.Id, out var origin) && !origin.Equals(entity.Position))
                                movingOwners.Add(entity.Owner);
                            var order = entity.CurrentOrder;
                            var queue = entity.Queue;
                            var source = entity.OrderStamp?.Source?.Replace(",", "_").Replace("\n", "_");
                            units.WriteLine(FormattableString.Invariant(
                                $"{view.Tick},{entity.Id},{entity.Owner},{entity.Kind},{entity.Position.X:R},{entity.Position.Z:R},{entity.NavigationOutcome},{entity.Moving},{entity.Held},{entity.Departing},{entity.StasisRemaining:R},{entity.WeaponSequence},{entity.TargetId},{entity.OrderStamp?.Sequence},{source},{order?.Kind},{order?.Destination.X:R},{order?.Destination.Z:R},{entity.Altitude:R},{queue?.Active?.CommandSequence},{queue?.Pending?.CommandSequence},{queue?.PendingAiRouteViable},{order?.TargetId}"));
                        }
                        units.Flush();
                        nextUnits = view.Tick + 30;
                    }
                    if (!navReadoutEnabled && view.Tick >= navReadoutStart)
                    {
                        domain.EnableNavigationStepsForQa();
                        navReadoutEnabled = true;
                    }
                    if (view.Tick >= HorizonTicks || view.Outcome != PlayableMatchOutcome.Playing ||
                        elapsed >= WallLimit || runtime.IsStopped)
                        break;
                    yield return null;
                }

                string summary = FormattableString.Invariant(
                    $"TPS_SOAK map={map.Id} profile={store.ActiveIdentity} bots={playerCount} seed={Seed} rate=4 prepared={preparation.Elapsed.TotalSeconds:F3} tick={view.Tick} wall={clock.Elapsed.TotalSeconds:F3} outcome={view.Outcome} movedOwners={movingOwners.Count} log={TestMatchLogSession.CurrentDirectory}");
                File.WriteAllText(Path.Combine(output, "result.txt"), summary + "\n");
                TestContext.WriteLine(summary);
                Assert.IsNull(routes.LastProjectionFailure);
                Assert.AreEqual(playerCount, movingOwners.Count, "Every starting scout must move under an AI command.");
                Assert.GreaterOrEqual(view.Tick, HorizonTicks, "The complete load horizon must be measured.");
            }
            finally
            {
                runtime?.RequestStop();
                bool stopped = runtime == null || SpinWait.SpinUntil(() => runtime.IsStopped, 5000);
                routes.Dispose();
                runtime?.Dispose();
                TestMatchLogSession.Shutdown();
                Assert.IsTrue(stopped, "The authority must release the test match.");
                if (runtime != null)
                {
                    var domain = GetDomain(runtime);
                    follow?.Write(Path.Combine(output, "follow-admission.csv"));
                    runtime.WritePerformanceNavigationSteps(Path.Combine(output, "navigation-steps.csv"));
                    var navigation = domain.Navigation;
                    Assert.IsTrue(SpinWait.SpinUntil(() => !navigation.HasCertifiedHost, 5000), "The route worker must detach.");
                    navigation.ReleaseCommittedHostRoot();
                    domain.DestroyOwnedNavigation();
                    Assert.Zero(navigation.PhysicalHostBudget.PhysicalBytes);
                }
            }
            Assert.IsNull(runtime.Latest.Failure);
            Assert.IsNull(runtime.JournalFailure);
            var journal = Directory.GetFiles(output, "summary.json", SearchOption.AllDirectories).Single();
            var result = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(journal));
            Assert.AreEqual("complete", (string)result["Status"]);
            Assert.AreEqual(0, (int)result["Dropped"]);
        }

        private sealed class FollowTrace
        {
            private const int Capacity = 32768;
            private readonly (long Tick, int Actor, int Leader, NavPoint Position, NavPoint Anchor,
                NavPoint Goal, long Pending, string Result)[] rows =
                new (long, int, int, NavPoint, NavPoint, NavPoint, long, string)[Capacity];
            private int count;
            private int dropped;

            internal void Record(long tick, int actor, int leader, NavPoint position,
                NavPoint anchor, NavPoint goal, long pending, string result)
            {
                // Successful requests and deliberate cooldowns do not need per-tick I/O.
                // Keep each new refusal and a periodic sample while a request waits.
                if ((result == "cooldown" || result == "pending" || result == "settled") && tick % 30 != 0)
                    return;
                if (count == Capacity) { dropped++; return; }
                rows[count++] = (tick, actor, leader, position, anchor, goal, pending, result);
            }

            internal void Write(string path)
            {
                using var output = new StreamWriter(path);
                output.WriteLine("tick,actor,leader,x,z,leader_x,leader_z,goal_x,goal_z,pending,result");
                for (int i = 0; i < count; i++)
                {
                    var row = rows[i];
                    output.WriteLine(FormattableString.Invariant(
                        $"{row.Tick},{row.Actor},{row.Leader},{row.Position.X:R},{row.Position.Z:R},{row.Anchor.X:R},{row.Anchor.Z:R},{row.Goal.X:R},{row.Goal.Z:R},{row.Pending},{row.Result}"));
                }
                output.WriteLine($"# rows={count},dropped={dropped}");
            }
        }

        private static PlayableDomain GetDomain(PlayableRuntime runtime)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var authority = (PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick", flags).GetValue(runtime);
            return authority.Domain;
        }
    }
}

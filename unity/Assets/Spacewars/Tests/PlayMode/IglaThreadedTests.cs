using System;
using System.Collections;
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

public sealed class IglaThreadedTests
{
    [Test]
    public void ShotUsesOneFullLengthPulseAndHeavyCoilResource()
    {
        var parent = new GameObject("Igla presentation test");
        try
        {
            var rig = IglaPresentation.Create(parent.transform).GetComponent<IglaPresentation>();
            var beam = rig.GetComponentInChildren<LineRenderer>();
            var target = new BallisticPoint(4, 1, 7);
            PlayableEntitySnapshot Unit(BallisticPoint? end) =>
                new PlayableEntitySnapshot(1, PlayableOwner.Player, PlayableEntityKind.Igla,
                    default, 90, false, 2, 0, 0, iglaBeam: end);
            var shot = Unit(target);
            rig.Display(shot, 1);
            Assert.True(beam.enabled);
            float peak = beam.widthMultiplier;
            Assert.AreEqual(new Vector3(4, 1, 7), beam.GetPosition(1));
            Assert.AreEqual(Color.white, beam.startColor);
            var muzzle = beam.GetPosition(0);
            Assert.GreaterOrEqual(peak * beam.widthCurve.Evaluate(1f), .12f);
            rig.Display(shot, 1.05);
            Assert.AreEqual(peak, beam.widthMultiplier);
            Assert.AreEqual(Color.white, beam.endColor);
            rig.Display(shot, 1.15);
            Assert.Less(beam.widthMultiplier, peak);
            Assert.That(beam.startColor.a, Is.InRange(.45f, .55f));
            Assert.AreEqual(beam.startColor, beam.endColor);
            Assert.AreEqual(muzzle, beam.GetPosition(0));
            Assert.AreEqual(new Vector3(4, 1, 7), beam.GetPosition(1));
            rig.Display(shot, 1.25);
            Assert.Zero(beam.endColor.a);
            rig.Display(Unit(null), 1.26);
            Assert.False(beam.enabled);
            rig.Display(shot, 2);
            Assert.AreEqual(peak, beam.widthMultiplier);
            Assert.AreEqual(Color.white, beam.endColor);
            var clip = Resources.Load<AudioClip>("Sfx/Hybrid/igla-heavy-coil-1");
            Assert.NotNull(clip);
            Assert.AreEqual(48000, clip.frequency);
            Assert.AreEqual(1, clip.channels);
        }
        finally { UnityEngine.Object.DestroyImmediate(parent); }
    }

    [UnityTest, Timeout(130000)]
    public IEnumerator BotPaysForIglaAndMovesItWithGroundCover()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        const string owner = "igla-owner";
        const int seed = 41;
        const long generation = 132;
        var normal = (PlayableProfile)typeof(PlayableBootstrap).GetMethod("LoadNormalProfile",
            BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        var store = NativeBalanceStore.ForApplication(normal, Path.GetFullPath("../.local/ai-igla-support/lab.json"));
        store.ResetRelease();
        var terms = store.Active.CopyData();
        terms.startingCredits = 1000; // The authored state starts after the opening with cash on hand.
        var profile = PlayableProfile.Create(terms);
        var starts = new[]
        {
            new OfflineStart("west", 1, new NavPoint(-20,-15), new NavPoint(-20,-27), pin:1),
            new OfflineStart("east", 2, new NavPoint(20,15), new NavPoint(20,27), pin:2)
        };
        var home = new TerritorySite(1, PlayableBuildingKind.Headquarters, starts[0].Position, profile);
        var away = new TerritorySite(2, PlayableBuildingKind.Headquarters, starts[1].Position, profile);
        var reserve = new TerritorySite(3, PlayableBuildingKind.Outpost, new NavPoint(24,-23), Array.Empty<TerritorySlot>());
        // A completed midgame economy can afford support before the visible
        // tank fight is settled. The release credit rate and prices still apply.
        var infrastructure = new[] { PlayableBuildingKind.Factory, PlayableBuildingKind.ScientificCenter }
            .Concat(Enumerable.Repeat(PlayableBuildingKind.Refinery, home.Slots.Count - 2));
        var buildings = infrastructure.Select((kind, i) => new OfflineScenarioBuilding(1, kind, 1,
            home.Slots[i].Id, home.Slots[i].Position, home.Slots[i].Heading))
            .Append(new OfflineScenarioBuilding(2, PlayableBuildingKind.Outpost, 3, 0, reserve.Position)).ToArray();
        var units = Enumerable.Range(0, 8).Select(i => new OfflineScenarioUnit(1, PlayableEntityKind.Tank,
            new NavPoint(-23 + i % 4 * 3, -2 + i / 4 * 4)))
            .Concat(Enumerable.Range(0, 6).Select(i => new OfflineScenarioUnit(2, PlayableEntityKind.Tank,
                new NavPoint(-22 + i % 3 * 4, 13 + i / 3 * 4), heading:-Math.PI / 2))).ToArray();
        var config = new OfflineMatchConfiguration(profile, "ai-igla-support-ordinary-v1", "flat-igla-support-v1",
            "UnityHostRouteService", seed, new[]
            {
                new OfflineParticipant(owner, 1, 1, OfflineControl.Ai),
                new OfflineParticipant("human-owner", 2, 2, OfflineControl.Human)
            }, starts, new[] { home, away, reserve }, Array.Empty<NavObstacle>(), new double[,] { { 0, 100 }, { 100, 0 } },
            spectators:new[] { PlayableRuntime.LocalSpectatorId }, scenario:units, scenarioBuildings:buildings);
        using var runtime = new PlayableRuntime(config, generation, startPaused:true, aiProfile:store.ActiveAiProfile);
        using var routes = new UnityHostRouteService();
        try
        {
            // Start from a completed ground opening. Production, joining orders
            // and all movement still run through the normal bot and route host.
            var scheduler = typeof(PlayableRuntime).GetField("aiScheduler", flags).GetValue(runtime);
            var loop = ((IEnumerable)scheduler.GetType().GetField("owners", flags).GetValue(scheduler))
                .Cast<PlayableAiOwnerLoop>().Single();
            var executor = (AiOpeningExecutor)typeof(PlayableAiOwnerLoop).GetField("openingExecutor", flags).GetValue(loop);
            void Opening(string field, object value) => typeof(AiOpeningExecutor).GetField(field, flags).SetValue(executor, value);
            Opening("producerChosen", true);
            Opening("initialForceFulfilled", true);
            Opening("reinforceChosen", true);
            Opening("reinforcementFulfilled", true);
            Opening("phase", PlayableAiOpeningPhase.Complete);
            var completed = (HashSet<string>)typeof(AiOpeningExecutor).GetField("completed", flags).GetValue(executor);
            completed.UnionWith(new[] { "income", "factory", "force", "pressure" });
            var doctrine = (AiDoctrinePolicy)typeof(PlayableAiOwnerLoop).GetField("doctrine", flags).GetValue(loop);
            typeof(AiDoctrinePolicy).GetField("chosen", flags).SetValue(doctrine, true);
            typeof(AiDoctrinePolicy).GetField("doctrine", flags).SetValue(doctrine, AiDoctrine.Heavy);
            runtime.SetSpectatorSpeed(PlayableRuntime.LocalSpectatorId, 4);
            runtime.RequestPause(false);

            bool paid = false, produced = false;
            long nextSample = 1;
            var followOrigins = new Dictionary<int, NavPoint>();
            float deadline = Time.realtimeSinceStartup + 115;
            while (runtime.Latest.Tick < 3600 && runtime.Latest.Outcome == PlayableMatchOutcome.Playing &&
                !runtime.IsStopped && Time.realtimeSinceStartup < deadline)
            {
                routes.Service(runtime, profile.NavigationRequestsPerFrame);
                Assert.IsNull(runtime.Latest.Failure);
                Assert.IsNull(routes.LastProjectionFailure);
                var own = runtime.SpectatorFrame(PlayableRuntime.LocalSpectatorId).Players.Single(view => view.Owner == PlayableOwner.Player);
                if (own.Tick >= nextSample)
                {
                    nextSample += 300;
                    var observed = PlayableAiObservation.From(own);
                    string ownedUnits = string.Join(",", own.Entities.Where(unit => unit.Owner == own.Owner).GroupBy(unit => unit.Kind)
                        .Select(group => $"{group.Key}={group.Count()}"));
                    string visibleUnits = string.Join(",", own.Entities.Where(unit => observed.IsHostile(unit.Owner) &&
                        observed.Vision?.IsVisible(unit.Position) == true).GroupBy(unit => unit.Kind)
                        .Select(group => $"{group.Key}={group.Count()}"));
                    TestContext.WriteLine($"Igla sample {own.Tick}: bank={own.Credits} own={ownedUnits} visible={visibleUnits}; {AiEconomyAdmission.IglaOrderRejection(observed)}");
                }
                paid |= own.Buildings.Where(b => b.Owner == own.Owner && b.PrivateState != null)
                    .SelectMany(b => b.PrivateState.Orders).Any(order => order.Kind == PlayableEntityKind.Igla &&
                        order.PaidCost == profile.IglaCreditCost);
                produced |= own.Entities.Any(unit => unit.Owner == own.Owner && unit.Kind == PlayableEntityKind.Igla);
                foreach (var unit in own.Entities.Where(unit => unit.Owner == own.Owner && unit.Kind == PlayableEntityKind.Igla &&
                    unit.OrderStamp?.Origin == PlayableOrderOrigin.Ai && unit.CurrentOrder?.Kind == PlayableTacticalOrderKind.Follow))
                {
                    if (!own.Entities.Any(front => front.Id == unit.CurrentOrder.TargetId &&
                        front.Owner == own.Owner && front.Health > 0 && AiEconomyAdmission.IglaCover(front.Kind) > 0)) continue;
                    if (!followOrigins.TryGetValue(unit.Id, out var first)) followOrigins.Add(unit.Id, unit.Position);
                    else if (paid && AiOpponentSelector.Distance(first, unit.Position) > PlayableUnitRules.Radius(profile, unit.Kind))
                    {
                        TestContext.WriteLine($"Igla ordered and following ground cover at tick {own.Tick}, {profile.ProfileId} v{profile.Revision}, seed {seed}");
                        yield break;
                    }
                }
                yield return null;
            }
            TestContext.WriteLine($"Igla witness paid={paid}, produced={produced}, followers={followOrigins.Count}, tick={runtime.Latest.Tick}, {profile.ProfileId} v{profile.Revision}, seed={seed}");
            Assert.True(paid, $"Bot never paid for an Igla by tick {runtime.Latest.Tick}, {profile.ProfileId} v{profile.Revision}.");
            Assert.Fail($"No physical Igla movement on an admitted Follow behind living ground cover by tick {runtime.Latest.Tick}. Followers seen: {followOrigins.Count}.");
        }
        finally
        {
            var worker = (CertifiedRouteWorker)typeof(UnityHostRouteService).GetField("worker", flags).GetValue(routes);
            runtime.RequestStop();
            routes.Dispose();
            runtime.Dispose();
            Assert.True(System.Threading.SpinWait.SpinUntil(() => runtime.IsStopped && (worker == null || worker.IsStopped), 5000));
            var authority = (PlayableAuthorityTick)typeof(PlayableRuntime).GetField("authorityTick", flags).GetValue(runtime);
            var checkpoint = authority.CaptureDiagnosticCheckpoints().Single();
            foreach (var record in checkpoint.Records.Where(record => record.Policy == "production").TakeLast(24))
                TestContext.WriteLine($"{record.ApplicationTick}: {record.Kind} {record.Status} {record.Message}");
            authority.Domain.Navigation.ReleaseCommittedHostRoot();
            authority.Domain.DestroyOwnedNavigation();
        }
    }

    [UnityTest]
    public IEnumerator DriveTurnAndFire()
    {
        var normal=(PlayableProfile)typeof(PlayableBootstrap).GetMethod("LoadNormalProfile",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        var store=NativeBalanceStore.ForApplication(normal,Path.GetFullPath("../.local/igla/lab.json"));
        store.ResetRelease();
        var setup=new ProvingGroundSetup(store.Active);
        var start=new NavPoint(-10,-6);
        var goal=new NavPoint(-4,-10);
        var enemyPoint=new NavPoint(3,-4);
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Igla,start));
        Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Explorer,enemyPoint));
        var stage=new GameObject("Igla proving ground");
        using var world=new PlayableWorld(stage.transform,setup.Profile,staged:true);
        yield return world.Preparation;
        var renderer=new GameObject("World view").AddComponent<PlayableBootstrap>();
        renderer.ConfigureShkvalEvidence(world,setup.Profile);
        renderer.transform.SetParent(stage.transform);
        var light=new GameObject("Sun").AddComponent<Light>();
        light.transform.SetParent(stage.transform);
        IndustrialLightingProfile.Load(null).ApplyScene(light);
        var camera=new GameObject("Camera").AddComponent<Camera>();
        camera.transform.SetParent(stage.transform);
        camera.orthographic=true;
        camera.nearClipPlane=.05f;
        camera.farClipPlane=200;
        camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        var output=Environment.GetEnvironmentVariable("SPACEWARS_IGLA_FRAMES");
        var rt=new RenderTexture(960,540,24);
        camera.targetTexture=rt;
        var pixels=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        using var routes=new UnityHostRouteService();
        using var runtime=new PlayableRuntime(setup.Configuration(),92,startPaused:false);
        try
        {
            float deadline=Time.realtimeSinceStartup+75;
            while(!runtime.IsPrepared && Time.realtimeSinceStartup<deadline){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);yield return null;}
            Assert.True(runtime.IsPrepared,runtime.Latest.Failure);
            var id=runtime.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Igla).Id;
            var enemy=runtime.Latest.Entities.Single(e=>e.Kind==PlayableEntityKind.Explorer);
            Assert.True(runtime.TrySubmit(new PlayableCommand(92,1,"player-1",PlayableCommandKind.Move,new[]{id},goal).AsHuman()).Accepted);
            long previous=-1;
            int frames=0,shots=0;
            long firstTick=-1;
            bool attack=false,moved=false,turned=false,beam=false;
            double attackHeading=0;
            var lastHealth=enemy.Health;
            if(output!=null)Directory.CreateDirectory(output);
            while(Time.realtimeSinceStartup<deadline && shots<3)
            {
                routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);
                yield return null;
                var v=runtime.Latest;
                Assert.False(runtime.IsStopped,v.Failure);
                if(v.Tick==previous)continue;
                previous=v.Tick;
                if(firstTick<0)firstTick=v.Tick;
                var u=v.Entities.Single(e=>e.Id==id);
                var target=v.Entities.Single(e=>e.Id==enemy.Id);
                if(u.Moving)moved=true;
                if(!attack && moved && !u.Moving)
                {
                    attackHeading=u.HullHeading;
                    Assert.True(runtime.TrySubmit(new PlayableCommand(92,2,"player-1",PlayableCommandKind.Attack,new[]{id},targetId:enemy.Id).AsHuman()).Accepted);
                    attack=true;
                }
                float traverse=Mathf.Abs(Mathf.DeltaAngle((float)(u.HullHeading*Mathf.Rad2Deg),(float)(u.TurretHeading*Mathf.Rad2Deg)));
                Assert.LessOrEqual(traverse,15.01f);
                if(attack && Math.Abs(u.HullHeading-attackHeading)>.15)turned=true;
                if(target.Health<lastHealth){Assert.AreEqual(16,lastHealth-target.Health);shots++;lastHealth=target.Health;}
                if(u.IglaBeam.HasValue){Assert.False(u.Moving);beam=true;}
                if(output==null)continue;
                renderer.RenderShkvalEvidence(v,camera);
                Vector3 focus=world.Point(u.Position)+Vector3.up*.26f;
                float widen=Mathf.SmoothStep(0,1,Mathf.Clamp01((v.Tick-firstTick-20)/36f));
                focus=Vector3.Lerp(focus,(focus+world.Point(enemyPoint))*.5f,widen);
                camera.orthographicSize=Mathf.Lerp(2.4f,5.1f,widen);
                camera.transform.position=focus+new Vector3(3,9,-12);
                camera.transform.LookAt(focus);
                camera.Render();
                var old=RenderTexture.active;
                RenderTexture.active=rt;
                pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);
                pixels.Apply();
                RenderTexture.active=old;
                if (u.IglaBeam.HasValue)
                    File.AppendAllText(Path.Combine(output, "beam-frames.txt"), $"{frames:D5} {v.Tick}\n");
                File.AppendAllText(Path.Combine(output,"ticks.txt"),v.Tick+Environment.NewLine);
                File.WriteAllBytes(Path.Combine(output,$"{frames++:D5}.jpg"),pixels.EncodeToJPG(92));
            }
            Assert.True(moved && turned && beam,$"move={moved} turn={turned} beam={beam} shots={shots}");
            Assert.AreEqual(3,shots);
            Debug.Log($"Igla footage {store.SelectedId} v{store.SelectedVersion}, {frames} frames");
        }
        finally
        {
            renderer.ConfigureShkvalEvidence(null,setup.Profile);
            camera.targetTexture=null;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(pixels);
            UnityEngine.Object.Destroy(stage);
        }
    }
}

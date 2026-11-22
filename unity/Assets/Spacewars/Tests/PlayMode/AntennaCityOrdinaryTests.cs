using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
public sealed class AntennaCityOrdinaryTests
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    GameObject host;PlayableBootstrap app;PlayableRuntime runtime;StreamWriter log;string output;long sequence;
    T Get<T>(string name)=>(T)typeof(PlayableBootstrap).GetField(name,Flags).GetValue(app);
    void Call(string name)=>typeof(PlayableBootstrap).GetMethod(name,Flags).Invoke(app,null);
    PlayableSnapshot View=>runtime.ParticipantView("player-1");
    static double Distance(NavPoint a,NavPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2));
    void Send(PlayableCommandKind kind,int[] ids,NavPoint target=default,int site=0,int slot=0,PlayableBuildingKind building=PlayableBuildingKind.Headquarters,int parent=0){Assert.True(runtime.TrySubmit(new PlayableCommand(runtime.Generation,++sequence,"player-1",kind,ids,target,siteId:site,slotId:slot,buildingKind:building,parentId:parent).AsHuman()).Accepted);log.WriteLine("COMMAND "+sequence+" "+kind+" "+target.X+","+target.Z);}
    IEnumerator Wait(Func<bool> ready,double seconds,string stage,int groundUnit=0){double start=Time.realtimeSinceStartupAsDouble;while(!ready()&&Time.realtimeSinceStartupAsDouble-start<seconds){Assert.False(runtime.IsStopped,runtime.Latest?.Failure);if(groundUnit!=0){var point=View.Entities.Single(e=>e.Id==groundUnit).Position;Assert.AreEqual("city/street",((AntennaCityMap)Get<PlayableProfile>("profile").AuthoredMap).SupportAt(point)?.Id,stage+" used an elevated shortcut");}yield return null;}Assert.True(ready(),stage+" timeout; tick="+runtime.Latest?.Tick);log.WriteLine("PASS "+stage+" seconds="+(Time.realtimeSinceStartupAsDouble-start));log.Flush();}
    IEnumerator Move(int unit,NavPoint target,string stage){var before=View.Entities.Single(e=>e.Id==unit).Position;Send(PlayableCommandKind.Move,new[]{unit},target);yield return Wait(()=>View.Entities.Any(e=>e.Id==unit&&Distance(e.Position,before)>.25),45,stage+" first motion");yield return Wait(()=>View.Entities.Any(e=>e.Id==unit&&Distance(e.Position,target)<1.4),120,stage+" arrival");}
    IEnumerator MoveOnGround(int unit,NavPoint target,string stage){
        var before=View.Entities.Single(e=>e.Id==unit).Position;Send(PlayableCommandKind.Move,new[]{unit},target);
        yield return Wait(()=>View.Entities.Any(e=>e.Id==unit&&Distance(e.Position,before)>.25),45,stage+" first motion",unit);
        yield return Wait(()=>View.Entities.Any(e=>e.Id==unit&&Distance(e.Position,target)<1.4),120,stage+" arrival",unit);
        Assert.Zero(((AntennaCityMap)Get<PlayableProfile>("profile").AuthoredMap).SurfaceHeight(View.Entities.Single(e=>e.Id==unit).Position));
    }
    void Shot(string name,Vector3 target,float size,Vector3 offset){
        var camera=Get<Camera>("cameraView");var position=camera.transform.position;var rotation=camera.transform.rotation;float zoom=camera.orthographicSize,far=camera.farClipPlane;var previous=camera.targetTexture;var active=RenderTexture.active;
        var materials=host.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Where(m=>m&&m.HasProperty("_FogMask")).Distinct().ToArray();var masks=materials.Select(m=>m.GetTexture("_FogMask")).ToArray();Get<PlayableWorld>("world").InspectionReveal();
        var rt=new RenderTexture(1600,1200,24);rt.Create();var png=new Texture2D(1600,1200,TextureFormat.RGB24,false);
        try{camera.farClipPlane=1000;camera.transform.position=target+offset;camera.transform.LookAt(target);camera.orthographicSize=size;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;png.ReadPixels(new Rect(0,0,1600,1200),0,0);png.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),png.EncodeToPNG());}
        finally{for(int i=0;i<materials.Length;i++)materials[i].SetTexture("_FogMask",masks[i]);camera.targetTexture=previous;RenderTexture.active=active;camera.transform.SetPositionAndRotation(position,rotation);camera.orthographicSize=zoom;camera.farClipPlane=far;UnityEngine.Object.Destroy(png);UnityEngine.Object.Destroy(rt);}
    }
    [UnityTest,Timeout(135000)]
    public IEnumerator RecordedAttackMoveStartsAndArrivesForDispersedTanks()
    {
        const int seed=958389524;
        const long generation=29, order=1;
        const double wallSeconds=115;
        const long horizon=2400;
        var release=new NativeBalanceStore(PlayableProfile.Default,authoring:false,startOnRelease:true);
        Assert.True(release.HasActive,release.Problem);
        release.Select(NativeBalanceStore.DefaultId,11);
        Assert.AreEqual(11,release.SelectedVersion);
        var compose=typeof(PlayableBootstrap).GetMethod("ComposeLobbyProfile",BindingFlags.Static|BindingFlags.NonPublic);
        var p=(PlayableProfile)compose.Invoke(null,new object[]{release.Active,new NativeLobbyConfiguration{AntennaCity=true}});
        var city=(AntennaCityMap)p.AuthoredMap;
        var roster=new[]{new OfflineParticipant("foundry-6",6,1,OfflineControl.Human),new OfflineParticipant("player-1",1,2,OfflineControl.Human)};
        var basis=city.Configuration(p,seed,roster);
        var initial=new[]{
            new OfflineScenarioUnit(6,PlayableEntityKind.Tank,new NavPoint(-94.92199531212768,1.9227882652196189),0.23766557454228307),
            new OfflineScenarioUnit(6,PlayableEntityKind.Tank,new NavPoint(-66.11856455947867,29.41516012572774),-2.236427924612655),
            new OfflineScenarioUnit(6,PlayableEntityKind.Tank,new NavPoint(-68.4842554240252,28.55803252651069),-2.340795031727931),
            new OfflineScenarioUnit(6,PlayableEntityKind.Tank,new NavPoint(-60.57316949652322,34.37678115250146),-2.3931026138384683),
            new OfflineScenarioUnit(6,PlayableEntityKind.Tank,new NavPoint(-32.675724897824246,60.15869884287457),-2.5332441421244285),
            new OfflineScenarioUnit(6,PlayableEntityKind.Tank,new NavPoint(33.5050092200612,80.41748009347307),-2.7116071027454463)
        };
        var config=new OfflineMatchConfiguration(p,basis.SourceIdentity,basis.MapIdentity,basis.RouteProvenance,seed,
            roster,basis.Starts.ToArray(),basis.Sites.ToArray(),basis.Obstacles.ToArray(),new double[6,6],scenario:initial,terrain:city.Id);
        var goal=new NavPoint(-90.99727630615234,1.182120442390442);
        using var match=new PlayableRuntime(config,generation,startPaused:true);
        using var routes=new UnityHostRouteService();
        var clock=System.Diagnostics.Stopwatch.StartNew();
        var domain=(PlayableDomain)typeof(PlayableRuntime).GetField("domain",Flags).GetValue(match);
        try{
            while(!match.IsPrepared||match.Latest?.Paused!=true){
                routes.Service(match,p.NavigationRequestsPerFrame);Assert.IsNull(match.Latest?.Failure);
                Assert.Less(clock.Elapsed.TotalSeconds,wallSeconds,"runtime startup");
                yield return null;
            }
            Assert.True(p.UsesRecastGroundRoutes);
            var starts=match.ParticipantView("foundry-6").Entities.Where(e=>e.Kind==PlayableEntityKind.Tank).ToDictionary(e=>e.Id,e=>e.Position);
            var originalIds=new[]{48,64,70,76,84,94};
            Assert.AreEqual(6,starts.Count);
            var activated=new System.Collections.Generic.Dictionary<int,long>();
            var firstMove=new System.Collections.Generic.Dictionary<int,long>();
            var arrived=new System.Collections.Generic.Dictionary<int,long>();
            match.RequestPause(false);
            while(match.Latest.Paused){routes.Service(match,p.NavigationRequestsPerFrame);Assert.Less(clock.Elapsed.TotalSeconds,wallSeconds);yield return null;}
            var command=new PlayableCommand(generation,order,"foundry-6",PlayableCommandKind.AttackMove,starts.Keys.OrderBy(id=>id).ToArray(),goal).AsHuman();
            Assert.True(match.TrySubmit(command).Accepted);
            PlayableCommandReceipt receipt=null;
            long lastObserved=-1;
            while(clock.Elapsed.TotalSeconds<wallSeconds&&arrived.Count<starts.Count){
                routes.Service(match,p.NavigationRequestsPerFrame);
                Assert.IsNull(match.Latest.Failure);Assert.IsNull(routes.CertifiedPublication?.Failure);
                receipt??=match.ReceiptsAfter(0).FirstOrDefault(r=>r.Sequence==order);
                if(receipt!=null)Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                var view=match.ParticipantView("foundry-6");
                if(receipt!=null&&view.Tick-receipt.AppliedTick>horizon)break;
                if(view.Tick!=lastObserved){
                    lastObserved=view.Tick;
                    foreach(var actor in view.Entities.Where(e=>starts.ContainsKey(e.Id))){
                        if(actor.Queue?.Active?.CommandSequence==order&&!activated.ContainsKey(actor.Id))
                            activated.Add(actor.Id,view.Tick);
                        if(Distance(actor.Position,starts[actor.Id])>.05&&!firstMove.ContainsKey(actor.Id))
                            firstMove.Add(actor.Id,view.Tick);
                        if(actor.OrderStamp?.Sequence==order&&actor.NavigationOutcome==NavigationOutcome.Arrived&&!arrived.ContainsKey(actor.Id))
                            arrived.Add(actor.Id,view.Tick);
                    }
                }
                yield return null;
            }
            Assert.NotNull(receipt);
            TestContext.WriteLine($"CITY_ORDER seed={seed} profile={release.ActiveIdentity} runtime={p.ProfileId}@{p.Revision} order={order} applied={receipt.AppliedTick} pumps={routes.CertifiedPublication?.Pumps}");
            foreach(var actor in match.ParticipantView("foundry-6").Entities.Where(e=>starts.ContainsKey(e.Id))){
                string Tick(System.Collections.Generic.Dictionary<int,long> ticks)=>ticks.TryGetValue(actor.Id,out long tick)?tick.ToString():"missing";
                var origin=starts[actor.Id];
                var recordedId=originalIds[Array.FindIndex(initial,u=>u.Position.Equals(origin))];
                TestContext.WriteLine($"CITY_MEMBER recorded={recordedId} id={actor.Id} origin=({origin.X:R},{origin.Z:R}) active={Tick(activated)} first={Tick(firstMove)} arrived={Tick(arrived)} position=({actor.Position.X:R},{actor.Position.Z:R}) outcome={actor.NavigationOutcome} target={actor.TargetId}");
            }
            Assert.Greater(routes.CertifiedPublication.Pumps,0);
            Assert.AreEqual(starts.Count,activated.Count,"every recipient must activate the original order");
            Assert.AreEqual(starts.Count,firstMove.Count,"every installed recipient must move");
            Assert.AreEqual(starts.Count,arrived.Count,"every recipient must finish the accepted attack-move");
        }finally{
            match.RequestStop();
            Assert.True(System.Threading.SpinWait.SpinUntil(()=>match.IsStopped,10000));
            var stopWorker=typeof(UnityHostRouteService).GetMethod("StopDiagnosticWorker",Flags);
            Assert.True(System.Threading.SpinWait.SpinUntil(()=>(bool)stopWorker.Invoke(routes,null),10000));
            domain.DestroyOwnedNavigation();
        }
    }

    [Test]
    public void RecordedOrder21MovesAfterItsOwnStreetRouteActivates()
    {
        const int seed = 958389524;
        const long generation = 21, order = 21;
        var release = new NativeBalanceStore(PlayableProfile.Default, authoring: false, startOnRelease: true);
        Assert.True(release.HasActive, release.Problem);
        release.Select(NativeBalanceStore.DefaultId, 11);
        Assert.AreEqual(11, release.SelectedVersion);
        var compose = typeof(PlayableBootstrap).GetMethod("ComposeLobbyProfile", BindingFlags.Static | BindingFlags.NonPublic);
        var p = (PlayableProfile)compose.Invoke(null, new object[] { release.Active, new NativeLobbyConfiguration { AntennaCity = true } });
        var city = (AntennaCityMap)p.AuthoredMap;
        var roster = new[] { new OfflineParticipant("foundry-6", 6, 1, OfflineControl.Human), new OfflineParticipant("player-1", 1, 2, OfflineControl.Human) };
        var basis = city.Configuration(p, seed, roster);
        var originalIds = new[] { 35, 44, 48, 55, 64 };
        var initial = new[] {
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(-84.0330144448177, 9.874140681718277), -2.238484536913999),
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(-87.21054625759362, 7.3110868799098645), 1.0601936575123005),
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(-45.79830665195336, 33.96043271907659), -1.884621318234375),
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(-52.368638018851904, -8.13526036090358), -1.2045807704150255),
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(-28.48086878511769, 62.998712593496606), -2.571861208438023)
        };
        var config = new OfflineMatchConfiguration(p, basis.SourceIdentity, basis.MapIdentity, basis.RouteProvenance, seed,
            roster, basis.Starts.ToArray(), basis.Sites.ToArray(), basis.Obstacles.ToArray(), new double[6, 6], scenario: initial, terrain: city.Id);
        var goal = new NavPoint(-78.38528442382812, 14.623401641845703);
        var authority = new PlayableAuthorityTick(config, generation, asynchronousRoutes: true);
        authority.EnableAccruedCredits();
        var domain = (PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain", Flags).GetValue(authority);
        var nav = domain.Navigation;
        var kernel = new CertifiedRouteKernel();
        try
        {
            Assert.True(p.UsesRecastGroundRoutes);
            var tanks = authority.ParticipantView("foundry-6").Entities.Where(e => e.Kind == PlayableEntityKind.Tank).ToArray();
            Assert.AreEqual(initial.Length, tanks.Length);
            var actors = initial.Select(unit => tanks.Single(tank => tank.Position.Equals(unit.Position)).Id).ToArray();
            nav.MovementDelayActor = actors[0];
            var command = new PlayableCommand(generation, order, "foundry-6", PlayableCommandKind.Move, actors, goal).AsHuman();
            var receipt = authority.Apply(command);
            Assert.AreEqual(PlayableCommandStatus.Applied, receipt.Status, receipt.Message);

            // Let the real solver publish one answer before allowing the other
            // results through. Authority can advance while those results wait.
            kernel.PauseAtPhysicalBoundary = () => nav.Answers.Count > 0;
            bool observed = false;
            for (int tick = 0; tick < 1800; tick++)
            {
                kernel.Pump(authority.NavigationBinding, p.NavigationRequestsPerFrame);
                nav.MovementDelayComputation = default;
                Assert.True(authority.TryAdvance());
                var group = nav.GroupFor(actors[0]);
                Assert.NotNull(group);

                if (!observed && nav.Crowd.TryGet(actors[0], out var near) && near.InstalledGroupId == group.GroupId)
                {
                    var executions = (NavigationInstalledExecutions)typeof(NavigationSession).GetField("installedExecutions", Flags).GetValue(nav);
                    var route = executions.OwnerFor(near.Id).Corridor.TypedRoute;
                    var pending = group.Members.Count(m => m.Phase == GroupMemberPhase.PendingRoute || m.Phase == GroupMemberPhase.Allocating);
                    var computation = nav.MovementDelayComputation;
                    int portals = route.Legs.Count(leg => leg.Kind == NavTypedLegKind.Portal);
                    TestContext.WriteLine($"ORDER21 recorded={originalIds[0]} tick={authority.Tick} surface={near.Location.SurfaceId} portals={portals} pending={pending} formation={group.FormationPlan?.Phase} cap={computation.Cap:R}");
                    Assert.AreEqual("city/street", near.Location.SurfaceId);
                    Assert.Zero(portals);
                    Assert.Greater(pending, 0, "the street route must activate while other routes are pending");
                    Assert.True(computation.Computed);
                    Assert.Greater(computation.Cap, 0, "the street route must be allowed to move while peers wait");
                    Assert.IsNull(group.FormationPlan);
                    observed = true;
                    kernel.PauseAtPhysicalBoundary = null;
                }

                if (!observed) continue;
                var arrived = authority.ParticipantView("foundry-6").Entities.Count(e =>
                    actors.Contains(e.Id) && e.OrderStamp?.Sequence == order && e.NavigationOutcome == NavigationOutcome.Arrived);
                if (arrived == actors.Length)
                {
                    TestContext.WriteLine($"ORDER21 seed={seed} profile={release.ActiveIdentity} arrived={arrived} tick={authority.Tick}");
                    return;
                }
            }

            Assert.True(observed, "near street route did not activate");
            Assert.Fail("all five tanks must arrive at their assigned endpoints");
        }
        finally
        {
            kernel.PauseAtPhysicalBoundary = null;
            kernel.Clear();
            authority.Stop();
            domain.DestroyOwnedNavigation();
        }
    }

    [Test]
    public void RecordedOrder23DoesNotWaitForAConnectorOutsideTheSharedSpine()
    {
        const int seed = 958389524;
        const long generation = 23;
        var release = new NativeBalanceStore(PlayableProfile.Default, authoring: false, startOnRelease: true);
        Assert.True(release.HasActive, release.Problem);
        release.Select(NativeBalanceStore.DefaultId, 11);
        Assert.AreEqual(11, release.SelectedVersion);
        var compose = typeof(PlayableBootstrap).GetMethod("ComposeLobbyProfile", BindingFlags.Static | BindingFlags.NonPublic);
        var profile = (PlayableProfile)compose.Invoke(null, new object[] { release.Active, new NativeLobbyConfiguration { AntennaCity = true } });
        var city = (AntennaCityMap)profile.AuthoredMap;
        var roster = new[] { new OfflineParticipant("foundry-6", 6, 1, OfflineControl.Human), new OfflineParticipant("opponent", 1, 2, OfflineControl.Human) };
        var basis = city.Configuration(profile, seed, roster);
        var initial = new[] {
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(-78.48721732794237, 14.527722772295123), -2.437275072252419),
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(-87.07573493833917, 3.654659112997173), -2.112286922992897),
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(-72.74050446412284, 13.708216948498377), -2.844890028335484),
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(-89.31772981150583, 7.855054711978424), -2.703307963705484),
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(-56.125612555446, 39.226554094398345), -2.5460469599527786),
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(-24.01702036631304, 65.03508412903261), -2.891645217629708),
            new OfflineScenarioUnit(6, PlayableEntityKind.Tank, new NavPoint(8.668063967779291, 73.37912630600313), -2.891645217629708)
        };
        var config = new OfflineMatchConfiguration(profile, basis.SourceIdentity, basis.MapIdentity, basis.RouteProvenance, seed,
            roster, basis.Starts.ToArray(), basis.Sites.ToArray(), basis.Obstacles.ToArray(), new double[6, 6], scenario: initial, terrain: city.Id);
        var authority = new PlayableAuthorityTick(config, generation, asynchronousRoutes: true);
        authority.EnableAccruedCredits();
        var domain = (PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain", Flags).GetValue(authority);
        var nav = domain.Navigation;
        var kernel = new CertifiedRouteKernel();
        try
        {
            var tanks = authority.ParticipantView("foundry-6").Entities.Where(e => e.Kind == PlayableEntityKind.Tank).ToArray();
            var actors = initial.Select(unit => tanks.Single(tank => tank.Position.Equals(unit.Position)).Id).ToArray();
            int front = actors[1], onConnector = actors[4];
            nav.MovementDelayActor = front;
            var receipt = authority.Apply(new PlayableCommand(generation, 23, "foundry-6", PlayableCommandKind.AttackMove,
                actors, new NavPoint(-120.14389038085938, -3.084852695465088)).AsHuman());
            Assert.AreEqual(PlayableCommandStatus.Applied, receipt.Status, receipt.Message);
            bool movedWhilePeerOnConnector = false;
            for (int tick = 0; tick < 1500; tick++)
            {
                kernel.Pump(authority.NavigationBinding, profile.NavigationRequestsPerFrame);
                nav.MovementDelayComputation = default;
                Assert.True(authority.TryAdvance());
                var group = nav.GroupFor(front);
                var leader = group?.Members.Single(m => m.Entity == front);
                var peer = group?.Members.Single(m => m.Entity == onConnector);
                if (!movedWhilePeerOnConnector && group?.FormationPlan == null && leader?.Recast.Phase == RecastMovementPhase.Shared &&
                    peer?.Phase == GroupMemberPhase.March && peer.Recast.Phase == RecastMovementPhase.Origin &&
                    nav.Crowd.TryGet(front, out var actor) && Distance(actor.Position, initial[1].Position) > .05)
                {
                    movedWhilePeerOnConnector = true;
                    TestContext.WriteLine($"ORDER23 front=44 tick={authority.Tick} peer=64 phase={peer.Recast.Phase} cap={nav.MovementDelayComputation.Cap:R}");
                }
                if (authority.ParticipantView("foundry-6").Entities.Count(e => actors.Contains(e.Id) &&
                    e.OrderStamp?.Sequence == 23 && e.NavigationOutcome == NavigationOutcome.Arrived) == actors.Length)
                {
                    Assert.True(movedWhilePeerOnConnector, "tank 44 must move while tank 64 is still on its connector");
                    TestContext.WriteLine($"ORDER23 arrived={actors.Length} tick={authority.Tick}");
                    return;
                }
            }
            Assert.Fail("all seven tanks must arrive at their assigned endpoints");
        }
        finally
        {
            kernel.Clear();
            authority.Stop();
            domain.DestroyOwnedNavigation();
        }
    }

    [Test]
    public void RecordedExplorer82RejoinsAheadAfterBypassingItsAlly()
    {
        const int seed = 958389524, homeSite = 4;
        const long generation = 82;
        var release = new NativeBalanceStore(PlayableProfile.Default, authoring: false, startOnRelease: true);
        Assert.True(release.HasActive, release.Problem);
        release.Select(NativeBalanceStore.DefaultId, 11);
        Assert.AreEqual(11, release.SelectedVersion);
        var compose = typeof(PlayableBootstrap).GetMethod("ComposeLobbyProfile", BindingFlags.Static | BindingFlags.NonPublic);
        var profile = (PlayableProfile)compose.Invoke(null, new object[] { release.Active, new NativeLobbyConfiguration { AntennaCity = true } });
        var city = (AntennaCityMap)profile.AuthoredMap;
        var roster = new[] { new OfflineParticipant("player-1", 1, 1, OfflineControl.Human), new OfflineParticipant("opponent", 6, 2, OfflineControl.Human) };
        var basis = city.Configuration(profile, seed, roster);
        var origin = new NavPoint(62.74650355012732, -106.86955573281183);
        var allyOrigin = new NavPoint(64.14869089587414, -104.98606615889673);
        var target = new NavPoint(64.87871445940145, -94.9121524570813);
        var units = new[] {
            new OfflineScenarioUnit(1, PlayableEntityKind.Explorer, origin, 0.38130927538343284),
            new OfflineScenarioUnit(1, PlayableEntityKind.Explorer, allyOrigin, -1.573086870303681)
        };
        var starts = basis.Starts.Select(s => s.SiteId == homeSite ?
            new OfflineStart(s.Id, s.SiteId, s.Position, s.ExplorerAnchor, s.Heading, 1) : s).ToArray();
        var site = basis.Sites.Single(s => s.Id == homeSite);
        var buildings = new[] { PlayableBuildingKind.Refinery, PlayableBuildingKind.Factory, PlayableBuildingKind.Refinery, PlayableBuildingKind.Refinery }
            .Select((kind, i) => new OfflineScenarioBuilding(1, kind, homeSite, i + 1, site.Slots[i].Position, site.Slots[i].Heading)).ToArray();
        var config = new OfflineMatchConfiguration(profile, basis.SourceIdentity, basis.MapIdentity, basis.RouteProvenance, seed,
            roster, starts, basis.Sites.ToArray(), basis.Obstacles.ToArray(), new double[6, 6], scenario: units, terrain: city.Id, scenarioBuildings: buildings);
        var authority = new PlayableAuthorityTick(config, generation, asynchronousRoutes: true);
        authority.EnableAccruedCredits();
        var domain = (PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain", Flags).GetValue(authority);
        var nav = domain.Navigation;
        var kernel = new CertifiedRouteKernel();
        try
        {
            int explorer = authority.ParticipantView("player-1").Entities.Single(e => e.Position.Equals(origin)).Id;
            var command = new PlayableCommand(generation, 1, "player-1", PlayableCommandKind.Move, new[] { explorer }, target).AsHuman();
            var receipt = authority.Apply(command);
            Assert.AreEqual(PlayableCommandStatus.Applied, receipt.Status, receipt.Message);
            for (int tick = 0; tick < 450; tick++)
            {
                kernel.Pump(authority.NavigationBinding, profile.NavigationRequestsPerFrame);
                Assert.True(authority.TryAdvance());
                var actor = authority.ParticipantView("player-1").Entities.Single(e => e.Id == explorer);
                if (actor.NavigationOutcome != NavigationOutcome.Arrived) continue;
                Assert.AreEqual(1, actor.OrderStamp.Sequence);
                Assert.Less(Distance(actor.Position, target), profile.Navigation.ArrivalTolerance);
                Assert.Greater(nav.Crowd.RepairCount, 0, "the recorded approach should exercise the bounded repair");
                TestContext.WriteLine($"EXPLORER82 arrived={authority.Tick} repairs={nav.Crowd.RepairCount}");
                return;
            }
            TestContext.WriteLine($"EXPLORER82 timeout={authority.Tick} repairs={nav.Crowd.RepairCount}");
            Assert.Fail("the explorer must pass the ally and arrive under the original command");
        }
        finally
        {
            kernel.Clear();
            authority.Stop();
            domain.DestroyOwnedNavigation();
        }
    }

    [Test]
    public void RecordedExplorer97KeepsItsApproachToTheGroupPassage()
    {
        const int seed = 958389524, homeSite = 6;
        const long generation = 97;
        var release = new NativeBalanceStore(PlayableProfile.Default, authoring: false, startOnRelease: true);
        Assert.True(release.HasActive, release.Problem);
        release.Select(NativeBalanceStore.DefaultId, 11);
        Assert.AreEqual(11, release.SelectedVersion);
        var compose = typeof(PlayableBootstrap).GetMethod("ComposeLobbyProfile", BindingFlags.Static | BindingFlags.NonPublic);
        var profile = (PlayableProfile)compose.Invoke(null, new object[] { release.Active, new NativeLobbyConfiguration { AntennaCity = true } });
        var city = (AntennaCityMap)profile.AuthoredMap;
        const string owner = "foundry-3";
        var roster = new[] { new OfflineParticipant(owner, 3, 1, OfflineControl.Human), new OfflineParticipant("opponent", 1, 2, OfflineControl.Human) };
        var basis = city.Configuration(profile, seed, roster);
        var units = new[] {
            new OfflineScenarioUnit(3, PlayableEntityKind.Explorer, new NavPoint(-117.99138953656283, 3.1818673559184827), -Math.PI / 2),
            new OfflineScenarioUnit(3, PlayableEntityKind.Explorer, new NavPoint(-120.09138953656284, 3.1818673559184827), -Math.PI),
            new OfflineScenarioUnit(3, PlayableEntityKind.Explorer, new NavPoint(-138.43448986157256, 0.02092049773951929), -2.9987977123474456)
        };
        var starts = basis.Starts.Select(s => s.SiteId == homeSite ?
            new OfflineStart(s.Id, s.SiteId, s.Position, s.ExplorerAnchor, s.Heading, 3) : s).ToArray();
        var config = new OfflineMatchConfiguration(profile, basis.SourceIdentity, basis.MapIdentity, basis.RouteProvenance, seed,
            roster, starts, basis.Sites.ToArray(), basis.Obstacles.ToArray(), new double[6, 6], scenario: units, terrain: city.Id);
        var authority = new PlayableAuthorityTick(config, generation, asynchronousRoutes: true);
        authority.EnableAccruedCredits();
        var domain = (PlayableDomain)typeof(PlayableAuthorityTick).GetField("domain", Flags).GetValue(authority);
        var nav = domain.Navigation;
        var kernel = new CertifiedRouteKernel();
        try
        {
            var explorers = authority.ParticipantView(owner).Entities;
            var actors = units.Select(unit => explorers.Single(e => e.Position.Equals(unit.Position)).Id).ToArray();
            var receipt = authority.Apply(new PlayableCommand(generation, 1, owner, PlayableCommandKind.AttackMove,
                actors, new NavPoint(6.4200000000000035, 52)).AsHuman());
            Assert.AreEqual(PlayableCommandStatus.Applied, receipt.Status, receipt.Message);
            bool formed = false;
            for (int tick = 0; tick < 1700; tick++)
            {
                kernel.Pump(authority.NavigationBinding, profile.NavigationRequestsPerFrame);
                Assert.True(authority.TryAdvance());
                formed |= nav.GroupFor(actors[2])?.FormationPlan?.Phase == RecastFormationPhase.Feeding;
                if (authority.ParticipantView(owner).Entities.Count(e => actors.Contains(e.Id) &&
                    e.OrderStamp?.Sequence == 1 && e.NavigationOutcome == NavigationOutcome.Arrived) != actors.Length) continue;
                Assert.True(formed, "the recorded approach should exercise group passage guidance");
                TestContext.WriteLine($"EXPLORER97 arrived={actors.Length} tick={authority.Tick}");
                return;
            }
            var remaining = authority.ParticipantView(owner).Entities.Where(e => actors.Contains(e.Id))
                .Select(e => $"{e.Id}:{e.NavigationOutcome}@{e.Position.X:R},{e.Position.Z:R}");
            Assert.Fail("the entire group must arrive under the original order: " + string.Join("; ", remaining));
        }
        finally
        {
            kernel.Clear();
            authority.Stop();
            domain.DestroyOwnedNavigation();
        }
    }

    [UnityTest,Timeout(600000)] public IEnumerator LobbySixPlayersRampProductionAiAndCachedReload()
    {
        output=Environment.GetEnvironmentVariable("ANTENNA_CITY_EVIDENCE");Assert.IsNotEmpty(output);Directory.CreateDirectory(output);log=new StreamWriter(Path.Combine(output,"ordinary.txt")){AutoFlush=true};
        bool kbAdded=Keyboard.current==null,mouseAdded=Mouse.current==null;var keyboard=kbAdded?InputSystem.AddDevice<Keyboard>():Keyboard.current;var mouse=mouseAdded?InputSystem.AddDevice<Mouse>():Mouse.current;
        bool ownLogs=TestMatchLogSession.CurrentDirectory==null;if(ownLogs)TestMatchLogSession.StartDefault(Path.Combine(output,"TestLogs"));
        var oldCache=PreparedMapCache.DirectoryOverride;PreparedMapCache.DirectoryOverride=Path.Combine(output,"cache");
        try{
            Assert.Contains("-noaudio",Environment.GetCommandLineArgs());Assert.Contains("-muted",Environment.GetCommandLineArgs());
            host=new GameObject("Antenna City ordinary witness");app=host.AddComponent<PlayableBootstrap>();double deadline=Time.realtimeSinceStartupAsDouble+180;
            while(Get<VisualElement>("lobbyScreen")==null&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.NotNull(Get<VisualElement>("lobbyScreen"));Call("ShowLobby");Get<VisualElement>("lobbyScreen").Q<DropdownField>("lobby-map-choice").value=AntennaCityMap.DisplayName;
            var setup=Get<NativeLobbyConfiguration>("lobbySetup");setup.HasExplicitSeed=true;setup.ExplicitSeed=1112026;Assert.AreEqual(6,setup.Participants.Count);Assert.True(setup.AntennaCity);
            double began=Time.realtimeSinceStartupAsDouble;Call("LaunchLobbyMatch");deadline=began+180;
            while((Get<bool>("inLobby")||Get<bool>("preparing"))&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.False(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));runtime=Get<PlayableRuntime>("runtime");Assert.NotNull(runtime);Assert.AreEqual(6,View.Participants.Count);
            double first=Time.realtimeSinceStartupAsDouble-began;log.WriteLine("READY_FIRST_SECONDS "+first+" worldCache="+Get<PlayableWorld>("world").PreparationCacheHit+" minimapCache="+Get<PlayableMapTerrain>("mapTerrain").PreparationCacheHit);log.WriteLine("TEST_LOG_DIRECTORY "+TestMatchLogSession.CurrentDirectory);
            var p=Get<PlayableProfile>("profile");var map=(AntennaCityMap)p.AuthoredMap;sequence=runtime.HumanSequenceWatermark;
            var hq=View.Buildings.Single(b=>b.Owner==View.Owner&&b.Kind==PlayableBuildingKind.Headquarters);int scout=View.Entities.Single(e=>e.Owner==View.Owner).Id,index=hq.SiteId-1,plaza=index/2,side=index%2==0?-1:1;
            Assert.Zero(map.SurfaceHeight(hq.Position));Shot("overview",Vector3.zero,180,new Vector3(0,300,-240));Shot("topology",Vector3.zero,160,new Vector3(0,360,0));Shot("street-base",new Vector3((float)hq.Position.X,0,(float)hq.Position.Z),31,new Vector3(0,50,-42));
            log.WriteLine("GEOMETRY meshes="+Get<PlayableWorld>("world").GraphicsRoot.GetComponentsInChildren<MeshFilter>().Length+" triangles="+Get<PlayableWorld>("world").GraphicsRoot.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3));
            Send(PlayableCommandKind.BuildAt,Array.Empty<int>(),site:hq.SiteId,slot:1,building:PlayableBuildingKind.Factory,parent:hq.Id);
            // Actual app Update services the threaded host; no sync Pump, accelerated time or entity injection.
            var approach=map.CenterApproach(index);
            // This ordinary threaded command chain must never need a ramp or elevated plaza.
            foreach(var target in approach){yield return MoveOnGround(scout,target,"ground center approach");}
            Shot("ground-center",Vector3.zero,66,new Vector3(0,110,-75));
            foreach(var target in approach.Reverse().Skip(1)){yield return MoveOnGround(scout,target,"ground return");}
            yield return Move(scout,map.RampFoot(plaza,side,3),"street to ramp");yield return Move(scout,map.RampTop(plaza,side,3),"plaza climb");
            Assert.AreEqual(map.PlazaHeight,map.SurfaceHeight(View.Entities.Single(e=>e.Id==scout).Position),.01);
            var at=map.PointAt(-90+120*plaza,52);Shot("plaza-ramps",new Vector3((float)at.X,2,(float)at.Z),38,new Vector3(0,65,-45));
            yield return Move(scout,map.RampFoot(plaza,-side,3),"opposite ramp descent");Assert.Zero(map.SurfaceHeight(View.Entities.Single(e=>e.Id==scout).Position));
            yield return Wait(()=>View.Buildings.Any(b=>b.Owner==View.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready),90,"factory ready");
            var factory=View.Buildings.Single(b=>b.Owner==View.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready);Send(PlayableCommandKind.QueueTank,new[]{factory.Id});
            yield return Wait(()=>View.Entities.Any(e=>e.Owner==View.Owner&&e.Kind==PlayableEntityKind.Tank),90,"ordinary tank production");
            yield return Wait(()=>View.Participants.Where(o=>o.Id!="player-1").All(o=>{var v=runtime.ParticipantView(o.Id);return v.Buildings.Count(b=>b.Owner==v.Owner)>1;}),120,"all five AI economies progress");
            var capture=runtime.RequestCaptureBytes();yield return Wait(()=>capture.IsCompleted,60,"current capture");Assert.AreEqual(System.Threading.Tasks.TaskStatus.RanToCompletion,capture.Status);File.WriteAllBytes(Path.Combine(output,"ordinary.world"),capture.Result);
            Call("ReturnToLobby");deadline=Time.realtimeSinceStartupAsDouble+60;while(!Get<bool>("inLobby")&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;Assert.True(Get<bool>("inLobby"));
            began=Time.realtimeSinceStartupAsDouble;Call("LaunchLobbyMatch");deadline=began+180;while((Get<bool>("inLobby")||Get<bool>("preparing"))&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            Assert.False(Get<bool>("inLobby"));Assert.False(Get<bool>("preparing"));runtime=Get<PlayableRuntime>("runtime");Assert.True(Get<PlayableWorld>("world").PreparationCacheHit);Assert.True(Get<PlayableMapTerrain>("mapTerrain").PreparationCacheHit);
            log.WriteLine("READY_REPEAT_SECONDS "+(Time.realtimeSinceStartupAsDouble-began));log.WriteLine("PASS ordinary six-player city / flat bases / both ramps / production / AI / capture / cache reload");
        }finally{runtime?.RequestStop();log?.Dispose();if(host)UnityEngine.Object.Destroy(host);PreparedMapCache.DirectoryOverride=oldCache;if(kbAdded)InputSystem.RemoveDevice(keyboard);if(mouseAdded)InputSystem.RemoveDevice(mouse);if(ownLogs)TestMatchLogSession.Shutdown();}
    }
}

using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class CerberusThreadedTests
{
    [UnityTest] public IEnumerator SingleCerberusLeadsMixedGroundGroup() => MarchToPoint(1);
    [UnityTest] public IEnumerator TwoCerberusShareFrontRank() => MarchToPoint(2);

    [UnityTest]
    public IEnumerator TankDrivesAroundHeldCerberus()
    {
        var setup = new ProvingGroundSetup(ReleaseProfile());
        var obstacle = new NavPoint(-7, -9);
        var goal = new NavPoint(-3, -9);
        Assert.IsNull(setup.PlaceUnit(0, PlayableEntityKind.Cerberus, obstacle));
        Assert.IsNull(setup.PlaceUnit(0, PlayableEntityKind.Tank, new NavPoint(-12, -9)));
        using var routes = new UnityHostRouteService();
        using var runtime = new PlayableRuntime(setup.Configuration(), 92, startPaused: false);
        float deadline = Time.realtimeSinceStartup + 45;
        while (!runtime.IsPrepared && Time.realtimeSinceStartup < deadline)
        {
            routes.Service(runtime, setup.Profile.NavigationRequestsPerFrame);
            yield return null;
        }
        Assert.True(runtime.IsPrepared, runtime.Latest.Failure);

        int cerberusId = runtime.Latest.Entities.Single(e => e.Kind == PlayableEntityKind.Cerberus).Id;
        int tankId = runtime.Latest.Entities.Single(e => e.Kind == PlayableEntityKind.Tank).Id;
        double clearance = PlayableUnitRules.Radius(setup.Profile, PlayableEntityKind.Cerberus)
            + PlayableUnitRules.Radius(setup.Profile, PlayableEntityKind.Tank);
        Assert.True(runtime.TrySubmit(new PlayableCommand(92, 1, "player-1", PlayableCommandKind.Hold,
            new[] { cerberusId }).AsHuman()).Accepted);
        Assert.True(runtime.TrySubmit(new PlayableCommand(92, 2, "player-1", PlayableCommandKind.Move,
            new[] { tankId }, goal).AsHuman()).Accepted);

        var lastPosition = default(NavPoint);
        var lastOutcome = NavigationOutcome.Idle;
        while (Time.realtimeSinceStartup < deadline)
        {
            routes.Service(runtime, setup.Profile.NavigationRequestsPerFrame);
            var view = runtime.Latest;
            Assert.False(runtime.IsStopped, view.Failure);
            Assert.AreEqual(obstacle, view.Entities.Single(e => e.Id == cerberusId).Position);
            var tank = view.Entities.Single(e => e.Id == tankId);
            lastPosition = tank.Position;
            lastOutcome = tank.NavigationOutcome;
            double x = tank.Position.X - obstacle.X;
            double z = tank.Position.Z - obstacle.Z;
            Assert.GreaterOrEqual(System.Math.Sqrt(x * x + z * z), clearance - .001, $"Tick {view.Tick}");
            if (tank.NavigationOutcome == NavigationOutcome.Arrived)
            {
                Assert.AreEqual(goal.X, tank.Position.X, setup.Profile.Navigation.ArrivalTolerance);
                Assert.AreEqual(goal.Z, tank.Position.Z, setup.Profile.Navigation.ArrivalTolerance);
                yield break;
            }
            if (view.Tick > 360) break;
            yield return null;
        }
        Assert.Fail($"The tank did not pass the held Cerberus. {lastOutcome} at {lastPosition.X}, {lastPosition.Z}");
    }

    private static IEnumerator MarchToPoint(int count)
    {
        var setup = new ProvingGroundSetup(ReleaseProfile());
        void Place(PlayableEntityKind kind, int x, int z) =>
            Assert.IsNull(setup.PlaceUnit(0, kind, new NavPoint(x, z)));
        Place(PlayableEntityKind.Cerberus, -8, count == 1 ? -9 : -12);
        if (count == 2) Place(PlayableEntityKind.Cerberus, -8, -6);
        Place(PlayableEntityKind.Explorer, -12, -12);
        Place(PlayableEntityKind.Explorer, -12, -6);
        Place(PlayableEntityKind.Tank, -16, -9);
        using var routes = new UnityHostRouteService();
        using var runtime = new PlayableRuntime(setup.Configuration(), 90, startPaused: false);
        float deadline = Time.realtimeSinceStartup + 60;
        while (!runtime.IsPrepared && Time.realtimeSinceStartup < deadline)
        {
            routes.Service(runtime, setup.Profile.NavigationRequestsPerFrame);
            yield return null;
        }
        Assert.True(runtime.IsPrepared, runtime.Latest.Failure);
        var ids = runtime.Latest.Entities.Select(e => e.Id).ToArray();
        var order = new PlayableCommand(90, 1, "player-1", PlayableCommandKind.Move, ids, new NavPoint(9, -9));
        Assert.True(runtime.TrySubmit(order.AsHuman()).Accepted);
        while (Time.realtimeSinceStartup < deadline)
        {
            routes.Service(runtime, setup.Profile.NavigationRequestsPerFrame);
            var view = runtime.Latest;
            Assert.False(runtime.IsStopped, view.Failure);
            var front = view.Entities.Where(e => e.Kind == PlayableEntityKind.Cerberus).ToArray();
            double following = view.Entities.Where(e => e.Kind != PlayableEntityKind.Cerberus).Max(e => e.Position.X);
            Assert.Greater(front.Min(e => e.Position.X), following, $"March tick {view.Tick}");
            if (view.Entities.All(e => e.NavigationOutcome == NavigationOutcome.Arrived))
            {
                Assert.Greater(following, 0, "The group crossed the center of the proving ground.");
                Assert.AreEqual(-9, front.Average(e => e.Position.Z), setup.Profile.Navigation.ArrivalTolerance);
                if (count == 2)
                {
                    Assert.AreEqual(front[0].Position.X, front[1].Position.X, setup.Profile.Navigation.ArrivalTolerance);
                    double pitch = 2 * PlayableUnitRules.Radius(setup.Profile, PlayableEntityKind.Cerberus)
                        + 2 * setup.Profile.Navigation.ArrivalTolerance;
                    Assert.AreEqual(2 * pitch, System.Math.Abs(front[0].Position.Z - front[1].Position.Z),
                        2 * setup.Profile.Navigation.ArrivalTolerance);
                }
                yield break;
            }
            yield return null;
        }
        Assert.Fail("The group did not arrive before the deadline.");
    }

    [UnityTest] public IEnumerator HumanAttackMovesIntoDualStreamRange()
    {
        return RunOrder(PlayableCommandKind.Attack);
    }

    [UnityTest] public IEnumerator HumanAttackMoveEngagesWithBothMounts()
    {
        return RunOrder(PlayableCommandKind.AttackMove);
    }

    [UnityTest] public IEnumerator RangedArtilleryAndAircraftCanHitAHeldCerberus()
    {
        var setup=new ProvingGroundSetup(ReleaseProfile());
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Cerberus,new NavPoint(-3,-9)));
        Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Shkval,new NavPoint(7,-9)));
        Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Hawk,new NavPoint(3,-7)));
        using var routes=new UnityHostRouteService();
        using var runtime=new PlayableRuntime(setup.Configuration(),91,startPaused:false);
        float end=Time.realtimeSinceStartup+45;
        while(!runtime.IsPrepared && Time.realtimeSinceStartup<end)
        {
            routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);
            yield return null;
        }
        Assert.True(runtime.IsPrepared,runtime.Latest.Failure);
        var defender=runtime.Latest.Entities.Single(u=>u.Kind==PlayableEntityKind.Cerberus);
        var gun=runtime.Latest.Entities.Single(u=>u.Kind==PlayableEntityKind.Shkval);
        var aircraft=runtime.Latest.Entities.Single(u=>u.Kind==PlayableEntityKind.Hawk);
        Assert.True(runtime.TrySubmit(new PlayableCommand(91,1,"player-1",PlayableCommandKind.Hold,new[]{defender.Id})).Accepted);
        bool artilleryShot=false,airShot=false,hit=false;
        while(Time.realtimeSinceStartup<end && !(artilleryShot && airShot && hit))
        {
            routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);
            var view=runtime.Latest;
            Assert.False(runtime.IsStopped,view.Failure);
            artilleryShot|=view.Projectiles.Any(p=>p.OwnerId==gun.Id);
            airShot|=view.Projectiles.Any(p=>p.OwnerId==aircraft.Id);
            hit|=view.Entities.Single(u=>u.Id==defender.Id).Health<defender.Health;
            Assert.AreEqual(aircraft.Health,view.Entities.Single(u=>u.Id==aircraft.Id).Health);
            Assert.AreEqual(gun.Health,view.Entities.Single(u=>u.Id==gun.Id).Health);
            if(view.Tick>=450)break;
            yield return null;
        }
        Assert.True(artilleryShot && airShot && hit,$"Artillery {artilleryShot}, aircraft {airShot}, damage {hit}");
    }

    private static IEnumerator RunOrder(PlayableCommandKind order)
    {
        var setup=new ProvingGroundSetup(ReleaseProfile());
        return RunOrder(setup,order);
    }

    private static PlayableProfile ReleaseProfile()
    {
        var normal=(PlayableProfile)typeof(PlayableBootstrap).GetMethod("LoadNormalProfile",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        var store=NativeBalanceStore.ForApplication(normal,Path.GetFullPath("../.local/cerberus/lab.json"));
        store.ResetRelease();
        Debug.Log($"Cerberus threaded profile {store.SelectedId} v{store.SelectedVersion}");
        return store.Active;
    }

    private static IEnumerator RunOrder(ProvingGroundSetup setup,PlayableCommandKind order)
    {
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Cerberus,new NavPoint(-8,-9)));
        Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Cerberus,new NavPoint(2,-9)));
        using var routes=new UnityHostRouteService();
        var runtime=new PlayableRuntime(setup.Configuration(),90,startPaused:false);
        float end=Time.realtimeSinceStartup+50;
        try
        {
            while(!runtime.IsPrepared&&Time.realtimeSinceStartup<end){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);yield return null;}
            Assert.True(runtime.IsPrepared,runtime.Latest.Failure);
            var actor=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player);
            var victim=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Enemy);
            Assert.True(runtime.TrySubmit(new PlayableCommand(90,1,"player-1",order,new[]{actor.Id},new NavPoint(7,-9),targetId:order==PlayableCommandKind.Attack?victim.Id:0)).Accepted);
            bool moved=false,dual=false;
            while(Time.realtimeSinceStartup<end&&!dual)
            {
                routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);
                Assert.False(runtime.IsStopped,runtime.Latest.Failure);
                var u=runtime.Latest.Entities.FirstOrDefault(e=>e.Id==actor.Id);
                if(u!=null){moved|=u.Position.X>actor.Position.X+.2;dual=u.Flames?.LeftLength>0&&u.Flames?.RightLength>0;}
                yield return null;
            }
            Assert.True(moved,"The human attack order must move through the threaded navigation host.");
            var terminal=runtime.Latest.Entities.FirstOrDefault(e=>e.Id==actor.Id);
            Assert.True(dual,$"Both plasma streams must acquire the target. Tick {runtime.Latest.Tick}, "+
                $"position ({terminal?.Position.X}, {terminal?.Position.Z}), heading {terminal?.HullHeading}, "+
                $"target {terminal?.TargetId}, movement {terminal?.NavigationOutcome}, "+
                $"streams {terminal?.Flames?.LeftLength}/{terminal?.Flames?.RightLength}.");
            runtime.RequestPause(true);
            while(!runtime.Latest.Paused && Time.realtimeSinceStartup<end)yield return null;
            Assert.True(runtime.Latest.Paused);
            var paused=runtime.Latest;
            yield return null;
            yield return null;
            Assert.AreEqual(paused.Tick,runtime.Latest.Tick);
            var pausedMounts=paused.Entities.Single(e=>e.Id==actor.Id).Flames.Value;
            Assert.AreEqual(pausedMounts,runtime.Latest.Entities.Single(e=>e.Id==actor.Id).Flames.Value);
        }
        finally{runtime.RequestStop();}
    }
}

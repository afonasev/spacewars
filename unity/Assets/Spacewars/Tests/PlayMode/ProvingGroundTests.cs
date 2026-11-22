using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;
public sealed class ProvingGroundTests
{
    [TestCase("parked unit")]
    [TestCase("parked unit in the way")]
    [TestCase("building edge")]
    [TestCase("building in the way")]
    [TestCase("occupied destination")]
    [TestCase("destination inside a building")]
    public void SingleMoveKeepsOrApproachesTheClickedPoint(string scenario)
    {
        var defaults=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(
            Resources.Load<TextAsset>("PlayableProfile").text));
        var store=new NativeBalanceStore(defaults,authoring:false,startOnRelease:true);
        var setup=new ProvingGroundSetup(store.Active);
        var profile=setup.Profile;
        Assert.True(profile.UsesRecastGroundRoutes);
        double hqClearance=profile.HeadquartersFootprintRadius+profile.TankCollisionRadius+.15;
        var origin=new NavPoint(-12,10);
        var target=new NavPoint(-5,10);
        if(scenario=="building edge")
        {
            origin=new NavPoint(-56,-hqClearance);
            target=new NavPoint(-40,-hqClearance);
        }
        if(scenario=="building in the way"||scenario=="destination inside a building")
        {
            origin=new NavPoint(-56,0);
            target=scenario=="building in the way"?new NavPoint(-24,0):new NavPoint(-40,0);
        }
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,origin));
        double peerClearance=2*profile.TankCollisionRadius+.25;
        NavPoint? parked=scenario switch
        {
            "parked unit"=>new NavPoint(target.X,target.Z+peerClearance),
            "parked unit in the way"=>new NavPoint(target.X-peerClearance,target.Z),
            "occupied destination"=>target,
            _=>null
        };
        if(parked.HasValue)Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,parked.Value));

        const int generation=197;
        using var runtime=new PlayableRuntime(setup.Configuration(),generation);
        using var routes=new UnityHostRouteService();
        var clock=System.Diagnostics.Stopwatch.StartNew();
        void Service()
        {
            routes.Service(runtime,profile.NavigationRequestsPerFrame);
            Assert.IsNull(runtime.Latest.Failure);
            Assert.IsNull(routes.CertifiedPublication?.Failure);
            System.Threading.Thread.Sleep(1);
        }
        try
        {
            while((!runtime.IsPrepared||runtime.Latest.Tick==0)&&clock.Elapsed.TotalSeconds<20)Service();
            Assert.True(runtime.IsPrepared&&runtime.Latest.Tick>0);
            var initial=runtime.ParticipantView("player-1");
            int actor=initial.Entities.Single(unit=>unit.Position.Equals(origin)).Id;
            var orders=new HumanMoveOrderQueue();
            Assert.True(orders.Enqueue(profile,initial,new PlayableCommand(generation,1,"player-1",PlayableCommandKind.Move,
                new[]{actor},target)));
            PlayableCommand intent=null;
            HumanMoveDestination.Group[] destinations=null;
            Assert.True(System.Threading.SpinWait.SpinUntil(()=>orders.TryTake(out intent,out destinations),10000));
            if(scenario!="destination inside a building")Assert.AreEqual(target,destinations.Single().Target);
            Assert.True(runtime.TrySubmit(new PlayableCommand(generation,intent.Sequence,intent.PlayerId,intent.Kind,
                destinations.Single().Entities,destinations.Single().Target)).Accepted);
            PlayableCommandReceipt receipt=null;
            while(receipt==null&&clock.Elapsed.TotalSeconds<26)
            {
                Service();
                receipt=runtime.DrainReceipts().SingleOrDefault(r=>r.Sequence==1);
            }
            Assert.NotNull(receipt);
            Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
            var previous=origin;
            double travel=0;
            bool moved=false;
            PlayableEntitySnapshot scout=null;
            long lastTick=-1;
            while(clock.Elapsed.TotalSeconds<40)
            {
                Service();
                var view=runtime.ParticipantView("player-1");
                if(view.Tick==lastTick)continue;
                lastTick=view.Tick;
                scout=view.Entities.Single(unit=>unit.Id==actor);
                travel+=Distance(previous,scout.Position);
                previous=scout.Position;
                moved|=scout.Moving&&!scout.Position.Equals(origin);
                if(scout.Completion?.Order.Sequence==1||lastTick>=receipt.AppliedTick+330)break;
            }
            TestContext.WriteLine($"profile={store.ActiveIdentity} scenario={scenario} applied={receipt.AppliedTick} tick={lastTick} travel={travel:0.00} final={scout?.Position.X:0.00},{scout?.Position.Z:0.00} outcome={scout?.NavigationOutcome}");
            Assert.True(moved);
            Assert.NotNull(scout?.Completion,"The move must reach its assigned point before the deadline.");
            var assigned=scout.Completion.AssignedLocation.Position;
            Assert.AreEqual(assigned,scout.Completion.OriginalLocation.Position);
            Assert.LessOrEqual(Distance(assigned,scout.Position),profile.Navigation.ArrivalTolerance+1e-7);
            if(scenario=="occupied destination")
            {
                Assert.That(Distance(assigned,target),Is.InRange(2*profile.TankCollisionRadius,
                    2*profile.TankCollisionRadius+profile.Navigation.ArrivalTolerance+1e-7));
                Assert.Less(assigned.X,target.X);
            }
            else if(scenario=="destination inside a building")
            {
                Assert.That(Distance(assigned,target),Is.InRange(hqClearance-.15,
                    hqClearance-.15+profile.Navigation.ArrivalTolerance+1e-7));
                Assert.Less(assigned.X,target.X);
            }
            else Assert.AreEqual(target,assigned);
            double direct=Distance(origin,assigned);
            double extra=scenario switch
            {
                "building in the way"=>4*hqClearance,
                "parked unit in the way"=>2*peerClearance,
                _=>profile.Navigation.ArrivalTolerance
            };
            Assert.LessOrEqual(travel,direct+extra,"The route must stay close to a direct approach or short detour.");
        }
        finally
        {
            runtime.RequestStop();
            Assert.True(System.Threading.SpinWait.SpinUntil(()=>runtime.IsStopped,5000));
        }
    }

    [UnityTest] public IEnumerator ExplorerTurningMoveUnderTankFire()
    {
        var load=typeof(PlayableBootstrap).GetMethod("LoadNormalProfile",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        var balancePath=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/order-response/balance.json"));
        var balance=NativeBalanceStore.ForApplication((PlayableProfile)load.Invoke(null,null),balancePath);
        var setup=new ProvingGroundSetup(balance.Active);
        Assert.AreEqual("spacewars-default",balance.SelectedId);
        Assert.AreEqual(13,setup.Profile.Revision);
        var origin=new NavPoint(-3,0);
        var goal=new NavPoint(1,10);
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Explorer,origin));
        Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Tank,new NavPoint(3,0)));
        using var runtime=new PlayableRuntime(setup.Configuration(),97,startPaused:true);
        using var routes=new UnityHostRouteService();
        double deadline=Time.realtimeSinceStartupAsDouble+45;
        while(!runtime.IsPrepared&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);yield return null;}
        Assert.True(runtime.IsPrepared,runtime.Latest?.Failure);
        int id=runtime.Latest.Entities.Single(u=>u.Owner==PlayableOwner.Player).Id;
        var field=typeof(PlayableRuntime).GetField("authorityTick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        var domain=((PlayableAuthorityTick)field.GetValue(runtime)).Domain;
        var trace=new System.Collections.Concurrent.ConcurrentQueue<PlayableSnapshot>();
        int submitted=0;
        PlayableSpatialCompletion arrival=null;
        domain.TacticalCompletionForQa=(unit,completion)=>{
            if(unit==id&&completion.SpatialReceipt?.Order.Sequence==1)
                System.Threading.Volatile.Write(ref arrival,completion.SpatialReceipt);
        };
        const long commandTick=30;
        domain.CombatStepForQa=()=>{
            if(domain.Tick==commandTick){
                var result=runtime.TrySubmit(new PlayableCommand(97,1,"player-1",PlayableCommandKind.Move,new[]{id},goal));
                System.Threading.Volatile.Write(ref submitted,result.Accepted?1:-1);
            }
            if(domain.Tick<=180)trace.Enqueue(domain.Snapshot(0,RuntimeStatus.Running,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,7));
        };
        try{
            runtime.RequestPause(false);
            while(runtime.Latest.Tick<180&&!runtime.IsStopped&&Time.realtimeSinceStartupAsDouble<deadline){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);yield return null;}
            Assert.False(runtime.IsStopped,runtime.Latest.Failure);
            Assert.IsNull(routes.LastProjectionFailure);
            Assert.GreaterOrEqual(runtime.Latest.Tick,180);
            Assert.AreEqual(1,submitted);
            var receipt=runtime.ReceiptsAfter(0).Single(r=>r.Sequence==1);
            Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
            var frames=trace.Where(frame=>frame.Tick<=receipt.AppliedTick+90).ToArray();
            Assert.That(frames.Last().Tick,Is.EqualTo(receipt.AppliedTick+90));
            PlayableEntitySnapshot Scout(PlayableSnapshot frame)=>frame.Entities.Single(u=>u.Id==id);
            PlayableEntitySnapshot Tank(PlayableSnapshot frame)=>frame.Entities.Single(u=>u.Kind==PlayableEntityKind.Tank);
            var motion=frames.First(f=>!Scout(f).Position.Equals(origin));
            var shots=frames.SelectMany(f=>f.Projectiles).Where(p=>p.OwnerId==id&&p.DirectLaunchTick.HasValue).Select(p=>p.DirectLaunchTick.Value).Distinct().OrderBy(t=>t).ToArray();
            long shot=shots.Where(t=>t>=receipt.AppliedTick).DefaultIfEmpty(-1).First();
            var exit=frames.First(f=>Distance(Scout(f).Position,Tank(f).Position)>setup.Profile.TankRange);
            var end=frames.Last();
            var scout=Scout(end);var tank=Tank(end);
            bool turningInMotion=Math.Abs(Math.Atan2(goal.Z-origin.Z,goal.X-origin.X)-Scout(motion).HullHeading)>setup.Profile.Navigation.ForwardAlignmentAngle+1e-8;
            var completion=System.Threading.Volatile.Read(ref arrival);
            bool arrived=completion!=null&&completion.OriginalLocation.Position.Equals(goal)&&completion.CompletionTick<=end.Tick;
            double scoutHp=scout.ExactHealth??scout.Health, tankHp=tank.ExactHealth??tank.Health;
            TestContext.WriteLine($"EXPLORER_CONTROL profile={balance.SelectedId}@{setup.Profile.Revision} map={setup.Profile.AuthoredMap.Id} seed=7 inputTick={commandTick} applied={receipt.AppliedTick} motion={motion.Tick} shot={shot} exit={exit.Tick} last={end.Tick} HP={scoutHp}/{tankHp} movingTurn={turningInMotion} arrived={arrived} arrivalTick={completion?.CompletionTick} shots={string.Join(",",shots)} heading={Scout(motion).HullHeading:R}");
            Assert.That(motion.Tick,Is.GreaterThanOrEqualTo(receipt.AppliedTick));
            Assert.That(exit.Tick,Is.GreaterThanOrEqualTo(motion.Tick));
            Assert.That(shot,Is.GreaterThanOrEqualTo(receipt.AppliedTick),"The Explorer must fire after the movement command.");
            Assert.Less(scoutHp,setup.Profile.ExplorerHealth,"The tank must hit the Explorer.");
            Assert.Less(tankHp,setup.Profile.TankHealth,"The Explorer must hit the tank.");
            Assert.True(arrived);
        }finally{runtime.RequestStop();}
    }

    [Test,Timeout(90000)] public void ExplorerCombatTurnAtFixedRouteBoundary()
    {
        var load=typeof(PlayableBootstrap).GetMethod("LoadNormalProfile",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        var balancePath=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../.local/order-response/balance.json"));
        var balance=NativeBalanceStore.ForApplication((PlayableProfile)load.Invoke(null,null),balancePath);
        Assert.AreEqual("spacewars-default",balance.SelectedId);
        Assert.AreEqual(14,balance.SelectedVersion);
        var setup=new ProvingGroundSetup(balance.Active);
        var origin=new NavPoint(-3,0);var goal=new NavPoint(1,10);
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Explorer,origin));
        Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Tank,new NavPoint(3,0)));
        var authority=new PlayableAuthorityTick(setup.Configuration(),98);
        var domain=authority.Domain;
        var metrics=new PlayableRuntimeMetrics(0,0,0,0,0);
        PlayableSnapshot View()=>domain.Snapshot(0,RuntimeStatus.Running,false,metrics,null,7);
        int id=View().Entities.Single(u=>u.Owner==PlayableOwner.Player).Id;
        CertifiedRouteWorker worker=null;
        try{
            worker=new CertifiedRouteWorker(authority.NavigationBinding,setup.Profile.NavigationRequestsPerFrame);
            var clock=System.Diagnostics.Stopwatch.StartNew();
            const long orderTick=30, horizon=120;
            long motion=-1,exit=-1,shot=-1,ready=-1;
            bool issued=false,movingTurn=false;
            PlayableSpatialCompletion arrival=null;
            domain.TacticalCompletionForQa=(unit,completion)=>{
                if(unit==id&&completion.SpatialReceipt?.Order.Sequence==1)arrival=completion.SpatialReceipt;
            };
            domain.CombatStepForQa=()=>{
                if(domain.Tick<=orderTick||shot>=0)return;
                if(View().Projectiles.Any(p=>p.OwnerId==id&&p.DirectLaunchTick==domain.Tick))shot=domain.Tick;
            };
            while(authority.Tick<horizon&&clock.Elapsed.TotalSeconds<60){
                if(!issued&&authority.Tick==orderTick){
                    var receipt=authority.Apply(new PlayableCommand(98,1,"player-1",PlayableCommandKind.Move,new[]{id},goal));
                    Assert.AreEqual(PlayableCommandStatus.Applied,receipt.Status,receipt.Message);
                    issued=true;
                }
                if(!authority.TryAdvance()){System.Threading.Thread.Sleep(1);continue;}
                Assert.IsNull(worker.Publication.Failure);
                var frame=View();var scout=frame.Entities.Single(u=>u.Id==id);var tank=frame.Entities.Single(u=>u.Kind==PlayableEntityKind.Tank);
                if(issued&&ready<0&&scout.Moving)ready=authority.Tick;
                if(motion<0&&!scout.Position.Equals(origin)){
                    motion=authority.Tick;
                    movingTurn=Math.Abs(Math.Atan2(goal.Z-origin.Z,goal.X-origin.X)-scout.HullHeading)>setup.Profile.Navigation.ForwardAlignmentAngle+1e-8;
                }
                if(exit<0&&Distance(scout.Position,tank.Position)>setup.Profile.TankRange)exit=authority.Tick;
            }
            var end=View();var finalScout=end.Entities.Single(u=>u.Id==id);var finalTank=end.Entities.Single(u=>u.Kind==PlayableEntityKind.Tank);
            double scoutHp=finalScout.ExactHealth??finalScout.Health,tankHp=finalTank.ExactHealth??finalTank.Health;
            TestContext.WriteLine($"EXPLORER_CONTROL_FIXED profile={balance.SelectedId}@{setup.Profile.Revision} map={setup.Profile.AuthoredMap.Id} seed=7 inputTick={orderTick} ready={ready} motion={motion} shot={shot} exit={exit} last={authority.Tick} HP={scoutHp}/{tankHp} movingTurn={movingTurn} end={finalScout.Position.X:R},{finalScout.Position.Z:R} arrival={arrival?.CompletionTick} assigned={arrival?.AssignedLocation.Position.X:R},{arrival?.AssignedLocation.Position.Z:R} outcome={finalScout.NavigationOutcome}");
            Assert.AreEqual(horizon,authority.Tick);
            Assert.Greater(worker.Publication.Pumps,0);
            Assert.GreaterOrEqual(ready,orderTick);
            Assert.GreaterOrEqual(motion,ready);
            Assert.GreaterOrEqual(shot,orderTick);
            Assert.GreaterOrEqual(exit,motion);
            Assert.Less(scoutHp,setup.Profile.ExplorerHealth);
            Assert.Less(tankHp,setup.Profile.TankHealth);
            Assert.That(arrival,Is.Not.Null,"The original Move must publish its authoritative arrival even if subsequent combat moves the Explorer.");
            Assert.AreEqual(goal,arrival.OriginalLocation.Position);
            Assert.That(arrival.CompletionTick,Is.InRange(motion,horizon));
        }finally{
            worker?.Dispose();
            if(worker!=null)Assert.True(System.Threading.SpinWait.SpinUntil(()=>worker.IsStopped,5000));
            domain.Navigation.ReleaseCommittedHostRoot();
            domain.DestroyOwnedNavigation();
        }
    }

    private static double Distance(NavPoint a,NavPoint b)
    {
        double x=a.X-b.X,z=a.Z-b.Z;
        return Math.Sqrt(x*x+z*z);
    }

    [Test] public void CaptureDuringOpeningRetainsTheSharedRouteBarrier()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(-10,0)));
        Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Tank,new NavPoint(10,0)));
        var configuration=setup.Configuration();
        var authority=new PlayableAuthorityTick(configuration,92);
        PlayableAuthorityTick restored=null;
        try
        {
            authority.Domain.StartProvingGroundBattle();
            Assert.True(authority.Domain.ProvingOpeningPending);
            restored=PlayableAuthorityTick.RestoreBytes(authority.CaptureBytes(),configuration);
            Assert.True(restored.Domain.ProvingOpeningPending);
            Assert.AreEqual(authority.Tick,restored.Tick);
        }
        finally
        {
            if(restored!=null)restored.Domain.DestroyOwnedNavigation();
            authority.Domain.DestroyOwnedNavigation();
        }
    }
    [UnityTest] public IEnumerator LiveSandboxAcceptsCommandsBeforeOpeningAttackWithoutResettingHost()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(-12,-9)));
        Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Tank,new NavPoint(12,-9)));
        var runtime=new PlayableRuntime(setup.Configuration(),90,startPaused:false);var routes=new UnityHostRouteService();
        float end=Time.realtimeSinceStartup+45;
        try{
            while((!runtime.IsPrepared||runtime.Latest.Tick<2)&&Time.realtimeSinceStartup<end){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);yield return null;}
            Assert.True(runtime.IsPrepared);Assert.Greater(runtime.Latest.Tick,0);Assert.False(runtime.Latest.Paused);
            int id=runtime.Latest.Entities.Single(e=>e.Owner==PlayableOwner.Player).Id;
            Assert.True(runtime.TrySubmit(new PlayableCommand(90,1,"player-1",PlayableCommandKind.Move,new[]{id},new NavPoint(-8,-9))).Accepted);
            while(Time.realtimeSinceStartup<end&&!runtime.Latest.Entities.Any(e=>e.Id==id&&e.Position.X>-11.9)){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);Assert.False(runtime.IsStopped,runtime.Latest.Failure);yield return null;}
            Assert.Greater(runtime.Latest.Entities.Single(e=>e.Id==id).Position.X,-11.9,"Ordinary manual movement before Start");
            long before=runtime.Latest.Tick;Assert.True(runtime.PlaceProvingGroundUnit(1,PlayableEntityKind.Explorer,new NavPoint(20,20)));
            while(Time.realtimeSinceStartup<end&&runtime.Latest.Entities.Count<3){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);yield return null;}
            Assert.AreEqual(3,runtime.Latest.Entities.Count);Assert.GreaterOrEqual(runtime.Latest.Tick,before);Assert.AreEqual(90,runtime.Generation);
            Assert.True(runtime.StartProvingGroundBattle());Assert.False(runtime.StartProvingGroundBattle());
            while(Time.realtimeSinceStartup<end&&runtime.Latest.Tick<=before+3){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);Assert.False(runtime.IsStopped,runtime.Latest.Failure);yield return null;}
            Assert.False(runtime.IsStopped,runtime.Latest.Failure);
            Assert.True(runtime.TrySubmit(new PlayableCommand(90,2,"player-1",PlayableCommandKind.Hold,new[]{id})).Accepted,"Opening does not consume human sequence");
            while(Time.realtimeSinceStartup<end&&!runtime.ReceiptsAfter(1).Any(r=>r.Sequence==2)){routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);yield return null;}
            Assert.AreEqual(PlayableCommandStatus.Applied,runtime.ReceiptsAfter(1).Single(r=>r.Sequence==2).Status);
            Assert.Greater(runtime.ProvingGroundCosts(1).Army,setup.Profile.TankCreditCost);
        }finally{runtime.RequestStop();routes.Dispose();}
    }
    [UnityTest] public IEnumerator MixedArmiesStartEveryUnitThroughOrdinaryThreadedHost()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        var kinds=new[]{PlayableEntityKind.Tank,PlayableEntityKind.Explorer,PlayableEntityKind.Shkval,PlayableEntityKind.Hawk};
        for(int side=0;side<2;side++)for(int i=0;i<kinds.Length;i++)
            Assert.IsNull(setup.PlaceUnit(side,kinds[i],new NavPoint(side==0?-12:12,-9+6*i)));
        var runtime=new PlayableRuntime(setup.Configuration(),86,startPaused:true);var routes=new UnityHostRouteService();
        float end=Time.realtimeSinceStartup+45;var moved=new System.Collections.Generic.HashSet<int>();
        var observations=new System.Collections.Generic.List<string>();long nextObservation=0;
        var arrivalDiagnostic=typeof(NavigationRoutePort).GetProperty("ArrivalAuthoritySnapshot",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        try{
            while(!runtime.IsPrepared&&Time.realtimeSinceStartup<end)yield return null;
            Assert.True(runtime.IsPrepared);Assert.True(runtime.StartProvingGroundBattle());
            while(Time.realtimeSinceStartup<end&&moved.Count<8){
                routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);Assert.False(runtime.IsStopped,runtime.Latest?.Failure);
                if(runtime.Latest.Tick>=nextObservation){nextObservation=runtime.Latest.Tick+300;observations.Add("tick="+runtime.Latest.Tick+" "+arrivalDiagnostic.GetValue(runtime.NavigationBinding.RoutePort)+" worker="+Newtonsoft.Json.JsonConvert.SerializeObject(routes.CertifiedPublication));}
                foreach(var u in runtime.Latest.Entities)if(u.Owner==PlayableOwner.Player?u.Position.X>-11.9:u.Position.X<11.9)moved.Add(u.Id);
                yield return null;
            }
            foreach(var observation in observations)TestContext.WriteLine(observation);
            TestContext.WriteLine("MIXED_FIRST_MOTION tick="+runtime.Latest.Tick+" moved="+string.Join(",",moved)+" snapshot="+Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Latest.Entities));
            Assert.AreEqual(8,moved.Count,"Every ground and air unit from both armies must advance");
        }finally{runtime.RequestStop();routes.Dispose();}
    }
    [UnityTest] public IEnumerator OrdinaryThreadedBattleMovesBothSidesAndDealsDamage()
    {
        var setup=new ProvingGroundSetup(PlayableProfile.Default);
        Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,new NavPoint(-10,0)));
        Assert.IsNull(setup.PlaceUnit(1,PlayableEntityKind.Tank,new NavPoint(10,0)));
        var runtime=new PlayableRuntime(setup.Configuration(),81,startPaused:true);var routes=new UnityHostRouteService();
        var trace=new System.Text.StringBuilder();long lastLogged=-1;
        bool leftMoved=false,rightMoved=false,damage=false;float deadline=Time.realtimeSinceStartup+45;
        try{
            while(!runtime.IsPrepared&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.True(runtime.IsPrepared,runtime.Latest?.Failure);Assert.AreEqual(0,runtime.Latest.Tick);Assert.AreEqual(0,runtime.Latest.Credits);
            Assert.AreEqual(2,runtime.Latest.Entities.Count);Assert.AreEqual(6,runtime.Latest.Buildings.Count);Assert.True(runtime.StartProvingGroundBattle());
            while(Time.realtimeSinceStartup<deadline){
                routes.Service(runtime,setup.Profile.NavigationRequestsPerFrame);Assert.False(runtime.IsStopped,runtime.Latest?.Failure);
                var snap=runtime.Latest;
                if(snap.Tick!=lastLogged&&(lastLogged<0||snap.Tick-lastLogged>=30)){lastLogged=snap.Tick;trace.AppendLine("tick="+snap.Tick+" paused="+snap.Paused+" units="+Newtonsoft.Json.JsonConvert.SerializeObject(snap.Entities)+" worker="+Newtonsoft.Json.JsonConvert.SerializeObject(routes.CertifiedPublication));}
                var units=runtime.Latest.Entities;leftMoved|=units.Any(u=>u.Owner==PlayableOwner.Player&&u.Position.X>-9.9);rightMoved|=units.Any(u=>u.Owner==PlayableOwner.Enemy&&u.Position.X<9.9);
                damage|=units.Count<2||units.Any(u=>u.Health<setup.Profile.TankHealth);
                if(leftMoved&&rightMoved&&damage)break;yield return null;
            }
            TestContext.WriteLine(trace.ToString());TestContext.WriteLine("FINAL "+Newtonsoft.Json.JsonConvert.SerializeObject(new{runtime.Latest.Tick,runtime.Latest.Paused,runtime.Latest.Metrics,Worker=routes.CertifiedPublication,Projection=routes.LastProjectionFailure}));
            Assert.True(leftMoved,"Left first motion");Assert.True(rightMoved,"Right first motion");Assert.True(damage,"Ordinary combat damage");
            Assert.AreEqual(6,runtime.Latest.Buildings.Count,"Scenario opponent must not build");Assert.AreEqual(PlayableMatchOutcome.Playing,runtime.Latest.Outcome);
            TestContext.WriteLine("PROVING_GROUND_HOST_PASS tick="+runtime.Latest.Tick+" left="+leftMoved+" right="+rightMoved+" damage="+damage);
        }finally{runtime.RequestStop();routes.Dispose();}
    }
}

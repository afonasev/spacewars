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

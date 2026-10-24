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
            Assert.True(runtime.IsPrepared,runtime.Latest?.Failure);Assert.AreEqual(0,runtime.Latest.Tick);Assert.AreEqual(10000,runtime.Latest.Credits);
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

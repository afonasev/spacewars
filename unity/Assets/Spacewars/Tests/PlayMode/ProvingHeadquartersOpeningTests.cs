using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class ProvingHeadquartersOpeningTests
{
    [UnityTest] public IEnumerator ThreadedOpeningKeepsHeadquartersIntentAndAttacksIt()
    {
        string directory=Path.Combine(Path.GetTempPath(),"proving-hq-witness-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var defaults=PlayableProfile.Create(JsonUtility.FromJson<PlayableProfileData>(Resources.Load<TextAsset>("PlayableProfile").text));
        var store=new NativeBalanceStore(defaults,Path.Combine(directory,"profiles.json"),authoring:false,startOnRelease:true);
        var setup=new ProvingGroundSetup(store.Active);var profile=setup.Profile;
        Assert.True(profile.UsesRecastGroundRoutes);
        var origin=new NavPoint(-12,0);Assert.IsNull(setup.PlaceUnit(0,PlayableEntityKind.Tank,origin));
        var runtime=new PlayableRuntime(setup.Configuration(),109);var routes=new UnityHostRouteService();
        var samples=new JArray();var evidence=new JObject{["profile"]=store.ActiveIdentity,["revision"]=store.Active.Revision,["seed"]=7,["map"]=profile.AuthoredMap.Id,["samples"]=samples};
        var clock=System.Diagnostics.Stopwatch.StartNew();
        void Service()=>routes.Service(runtime,profile.NavigationRequestsPerFrame);
        try
        {
            while((!runtime.IsPrepared||runtime.Latest==null)&&clock.Elapsed.TotalSeconds<15){Service();yield return null;}
            Assert.True(runtime.IsPrepared);Assert.NotNull(runtime.Latest);Assert.IsNull(runtime.Latest.Failure);
            int id=runtime.Latest.Entities.Single().Id;
            var headquarters=runtime.Latest.Buildings.Single(b=>b.Owner==PlayableOwner.Enemy&&b.Kind==PlayableBuildingKind.Headquarters);
            double initialHealth=headquarters.ExactHealth??headquarters.Health;
            var goal=headquarters.Position;evidence["goal"]=new JArray(goal.X,goal.Z);
            Assert.True(runtime.StartProvingGroundBattle());clock.Restart();
            bool moved=false,damaged=false,sawOrder=false;long sampledTick=-10;
            while(clock.Elapsed.TotalSeconds<45&&!damaged)
            {
                Service();var view=runtime.Latest;Assert.IsNull(view.Failure);
                var unit=view.Entities.SingleOrDefault(e=>e.Id==id);Assert.NotNull(unit);
                if(unit.CurrentOrder!=null)
                {
                    Assert.AreEqual(PlayableTacticalOrderKind.AttackMove,unit.CurrentOrder.Kind);
                    Assert.AreEqual(goal,unit.CurrentOrder.Destination,"The logical goal is the HQ, never an approach slot");sawOrder=true;
                }
                if(!moved&&Math.Abs(unit.Position.X-origin.X)+Math.Abs(unit.Position.Z-origin.Z)>.01)
                {moved=true;evidence["first_motion_seconds"]=clock.Elapsed.TotalSeconds;Assert.Less(clock.Elapsed.TotalSeconds,10);}
                if(view.Tick>=sampledTick+10){sampledTick=view.Tick;samples.Add(new JObject{["tick"]=view.Tick,["x"]=unit.Position.X,["z"]=unit.Position.Z,["moving"]=unit.Moving});}
                var hq=view.Buildings.Single(b=>b.Id==headquarters.Id);damaged=(hq.ExactHealth??hq.Health)<initialHealth;
                if(damaged){evidence["hq_damage_seconds"]=clock.Elapsed.TotalSeconds;evidence["hq_health_before"]=initialHealth;evidence["hq_health_after"]=hq.ExactHealth??hq.Health;}
                yield return null;
            }
            Assert.True(sawOrder);Assert.True(moved,"Ordinary threaded Start must move the army");Assert.True(damaged,"The army must attack the opposing HQ within 45 seconds");
            evidence["result"]="PASS";TestContext.WriteLine(evidence.ToString(Newtonsoft.Json.Formatting.None));
        }
        finally
        {
            runtime.RequestStop();routes.Dispose();Directory.Delete(directory,true);
            string output=Environment.GetEnvironmentVariable("SPACEWARS_PROVING_HQ_EVIDENCE");
            if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"threaded-witness.json"),evidence.ToString());}
        }
    }
}

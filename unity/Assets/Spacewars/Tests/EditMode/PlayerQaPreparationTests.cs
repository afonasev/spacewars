using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    public sealed class PlayerQaPreparationTests
    {
        private static PlayableProfile Profile()
        {
            var root=Path.Combine(Directory.GetCurrentDirectory(),"Assets/Spacewars/Content/Resources");
            return PlayableProfile.Create(JsonConvert.DeserializeObject<PlayableProfileData>(File.ReadAllText(Path.Combine(root,"PlayableProfile.json"))),
                new ThreeCrossingsMap(JsonConvert.DeserializeObject<ThreeCrossingsProfileData>(File.ReadAllText(Path.Combine(root,"ThreeCrossingsProfile.json")))));
        }
        [Test]
        public void ActualSmallFactoryCreatesTwoByFiveLivingUnitsUnderUnchangedValidators()
        {
            var profile=Profile();string before=JsonConvert.SerializeObject(profile.CopyData());
            var setup=new NativeLobbyConfiguration{HasExplicitSeed=true,ExplicitSeed=7};setup.InitializeParticipants();
            using(var runtime=PlayableRuntime.CreateSmallFivePerformanceMatch(profile,1,setup,startPaused:true)){
                var config=(OfflineMatchConfiguration)typeof(PlayableRuntime).GetField("offlineConfiguration",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(runtime);
                Assert.AreEqual(7,config.Seed);Assert.AreEqual(2,config.Roster.Count);Assert.AreEqual(8,config.ScenarioUnits.Count);
                Assert.AreEqual(10,runtime.DiagnosticGlobalUnits);
                foreach(var p in config.Roster){
                    var view=runtime.ParticipantView(p.Id);var own=view.Entities.Where(e=>e.Owner==view.Owner).ToArray();
                    Assert.AreEqual(0,view.Tick);Assert.AreEqual(5,own.Length);Assert.True(own.All(e=>e.Health>0));
                    Assert.AreEqual(4,own.Count(e=>e.Kind==PlayableEntityKind.Tank));Assert.AreEqual(1,own.Count(e=>e.Kind==PlayableEntityKind.Explorer));
                    foreach(var e in own)Assert.True(profile.AuthoredMap.SupportsFootprint(e.Position,PlayableUnitRules.Radius(profile,e.Kind)));
                }
                // Reproduce the rejected prior offsets against this exact same profile,
                // binding and unchanged constructor; no validator bypass or cap adjustment.
                var old=config.Roster.SelectMany((p,seat)=>Enumerable.Range(0,4).Select(i=>{
                    var h=config.Starts[config.Assignments[seat]].Position;
                    return new OfflineScenarioUnit(p.LogicalPlayer,PlayableEntityKind.Tank,new NavPoint(h.X+(i%2==0?-3:3),h.Z+(i/2==0?-5:5)),0);
                })).ToArray();
                var costs=new double[config.Starts.Count,config.Starts.Count];
                for(int a=0;a<config.Starts.Count;a++)for(int b=0;b<config.Starts.Count;b++)costs[a,b]=config.RouteCost(a,b);
                Assert.Throws<ArgumentException>(()=>new OfflineMatchConfiguration(profile,config.SourceIdentity,config.MapIdentity,config.RouteProvenance,config.Seed,
                    config.Roster.ToArray(),config.Starts.ToArray(),config.Sites.ToArray(),config.Obstacles.ToArray(),costs,config.Spectators.ToArray(),old,profile.AuthoredMap.Id));
                string output=Environment.GetEnvironmentVariable("SPACEWARS_QA_REPAIR_OUTPUT");
                if(output!=null){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"small-factory.json"),JsonConvert.SerializeObject(new{config.Seed,config.Assignments,config.Starts,config.ScenarioUnits,profile=profile.CopyData(),globalUnits=runtime.DiagnosticGlobalUnits},Formatting.Indented));}
            }
            Assert.AreEqual(before,JsonConvert.SerializeObject(profile.CopyData()),"QA placement may not mutate profile/quotas/physics");
        }
        [Test]
        public void AdmissionDeadlineIncludesStartupAndNeverRestartsAtMatchEntry()
        {
            var deadline=PlayerQaDeadline.Parse("1110000\n1120000\n"); // admission1000000, grace110s, hard120s
            Assert.False(deadline.ShouldFinish(1000000));Assert.False(deadline.ShouldFinish(1109999));
            Assert.True(deadline.ShouldFinish(1110000));Assert.True(deadline.ShouldFinish(1120000));
            Assert.True(deadline.ShouldFinish(1115000),"A late initialization is already expired; it gets no new horizon");
            Assert.False(deadline.ShouldCapture(1107999));Assert.True(deadline.ShouldCapture(1108000));
            Assert.AreEqual(10000,deadline.HardUtcMilliseconds-deadline.FinishUtcMilliseconds);
            Assert.Throws<ArgumentException>(()=>new PlayerQaDeadline(120,120));Assert.Throws<ArgumentException>(()=>PlayerQaDeadline.Parse("1\n2\n3"));
        }
        [Test]
        public void CompletionWaitsForRealFrameConsumerAndFinalFooterIncludingNullRuntimeEntry()
        {
            var dir=Path.Combine(Environment.GetEnvironmentVariable("SPACEWARS_QA_REPAIR_OUTPUT")??Path.GetTempPath(),"drain-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            var path=Path.Combine(dir,"frames.csv");var gate=new ManualResetEventSlim(false);var sink=new NativeRouteFrameTelemetry(path,8,()=>gate.Wait());
            try{
                Assert.True(sink.TryWrite(new NativeRouteFrameSample{Tick=1}));Assert.True(sink.TryWrite(new NativeRouteFrameSample{Tick=2}));
                sink.Dispose();Assert.False(sink.TryWrite(new NativeRouteFrameSample{Tick=3}));
                Assert.False(PlayerQaDeadline.ReadyToQuit(true,true,sink),"Null runtime is stopped, but queued evidence must drain first");
                gate.Set();Assert.True(SpinWait.SpinUntil(()=>sink.IsStopped,5000),"Actual IO sink must stop");
                Assert.IsNull(sink.Failure);Assert.Zero(sink.Dropped);Assert.True(PlayerQaDeadline.ReadyToQuit(true,true,sink));
                Assert.False(PlayerQaDeadline.ReadyToQuit(false,true,sink));Assert.False(PlayerQaDeadline.ReadyToQuit(true,false,sink));
                var lines=File.ReadAllLines(path);Assert.AreEqual(4,lines.Length);Assert.AreEqual("# samples=2,dropped=0",lines.Last());
                Assert.True(PlayerQaDeadline.ReadyToQuit(true,true,null),"Early null-runtime/null-sink entry has nothing to wait for");
            }finally{gate.Set();sink.Dispose();Assert.True(SpinWait.SpinUntil(()=>sink.IsStopped,5000));gate.Dispose();}
        }
    }
}

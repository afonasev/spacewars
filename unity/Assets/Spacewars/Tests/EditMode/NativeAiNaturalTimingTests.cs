using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Tests.EditMode
{
    // Opt-in bounded profiling of archived actual worlds. No commands or world injection.
    public sealed class NativeAiNaturalTimingTests
    {
        [TestCase(7108,6600)]
        [TestCase(7109,17100)]
        [Explicit("Extract Q8 checkpoints into .local/o6-timing; run exclusively for timing.")]
        public void ArchivedWindow_DiagnosticsPreserveExactWorldAndSurvivalEvidence(int seed,int tick)
        {
            string folder=Path.Combine(NativeAiNaturalCycleTests.Root(),".local/o6-timing");
            var start=File.ReadAllBytes(Path.Combine(folder,$"{seed}-checkpoint-{tick}.world"));
            var expected=File.ReadAllBytes(Path.Combine(folder,$"{seed}-checkpoint-{tick+300}.world"));
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,seed,roster);
            var metrics=new List<object>();string[] baselineEvidence=null;
            // Reverse the order for the second world to expose first-leg warm-up bias.
            foreach(string mode in seed==7108?new[]{"all","survival","off"}:new[]{"off","survival","all"})
            {
                var setup=Stopwatch.StartNew();var authority=PlayableAuthorityTick.RestoreBytes(start,config);
                var safe=authority.CaptureDiagnosticCheckpoints().Where(x=>x.Opening.Opening.ToString()=="Safe").Select(x=>x.OwnerId).ToArray();
                double setupSeconds=setup.Elapsed.TotalSeconds,callbackSeconds=0,advanceSeconds=0,hostSeconds=0;
                long rows=0,chars=0;var evidence=new NativeAiNaturalCycleTests.SurvivalEvidence();
                using(var sink=new StreamWriter(Path.Combine(folder,$"{seed}-{mode}-guards.jsonl")))
                using(var host=new UnityHostRouteService())
                try
                {
                    ExactByteAssert.AreEqual(start,authority.CaptureBytes(),"restore roundtrip");
                    Action<string> callback=line=>{var timer=Stopwatch.StartNew();evidence.Observe(line);sink.WriteLine(line);rows++;chars+=line.Length+1;callbackSeconds+=timer.Elapsed.TotalSeconds;};
                    PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;
                    PlayableAiOwnerLoop.CurrentSafeFundingStageFilterForTests=null;
                    if(mode=="survival")NativeAiNaturalCycleTests.CaptureSurvivalOnly(safe,callback);
                    else PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=mode=="all"?callback:null;
                    var body=Stopwatch.StartNew();
                    while(authority.Tick<tick+300)
                    {
                        bool advanced=false;
                        for(int attempt=0;attempt<512;attempt++)
                        {
                            var t=Stopwatch.StartNew();bool success=authority.TryAdvance();advanceSeconds+=t.Elapsed.TotalSeconds;
                            if(success){advanced=true;break;}
                            t.Restart();host.Service(authority,64);hostSeconds+=t.Elapsed.TotalSeconds;
                        }
                        Assert.True(advanced,"ordinary native route barrier at "+authority.Tick);Assert.IsNull(authority.Latest.Failure);
                    }
                    sink.Flush();double bodySeconds=body.Elapsed.TotalSeconds;
                    var capture=Stopwatch.StartNew();var end=authority.CaptureBytes();double captureSeconds=capture.Elapsed.TotalSeconds;
                    ExactByteAssert.AreEqual(expected,end,"same all-owner end world as immutable Natural evidence");
                    var actual=evidence.DeferredOwners.Where(safe.Contains).OrderBy(x=>x).ToArray();
                    if(mode!="off")
                    {
                        if(baselineEvidence==null)baselineEvidence=actual;else CollectionAssert.AreEqual(baselineEvidence,actual,"same actual Safe deferral");
                        if(seed==7109)CollectionAssert.AreEqual(new[]{"ai-0"},actual);
                    }
                    metrics.Add(new{mode,seed,fromTick=tick,toTick=authority.Tick,setupSeconds,bodySeconds,advanceSeconds,hostSeconds,callbackSeconds,captureSeconds,rows,chars,worldHash=Wire.Hash(end),deferred=actual});
                }
                finally
                {
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=null;
                    PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;
                    PlayableAiOwnerLoop.CurrentSafeFundingStageFilterForTests=null;authority.Stop();
                }
            }
            File.WriteAllText(Path.Combine(folder,$"{seed}-timing.json"),JsonConvert.SerializeObject(metrics,Formatting.Indented));
            TestContext.WriteLine(JsonConvert.SerializeObject(metrics));
        }
    }
}

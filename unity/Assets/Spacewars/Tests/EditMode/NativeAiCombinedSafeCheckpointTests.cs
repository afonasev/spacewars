using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    // Pure readback of the current natural failure. No advance, injection or guard override.
    public sealed class NativeAiCombinedSafeCheckpointTests
    {
        [Test]
        public void CurrentSafeLastFundingWindowReplaysExactlyWithActualGuards()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());
            while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;
            Assert.NotNull(root);
            string folder=Path.Combine(root.FullName,".local/o6-natural");
            var start=File.ReadAllBytes(Path.Combine(folder,"7109-checkpoint-17100.world"));
            var end=File.ReadAllBytes(Path.Combine(folder,"7109-checkpoint-17400.world"));
            Assert.AreEqual("999912977d5ca9155ad068edc34244133a6180dcfe42b4dcb9b8f0dc60d514c3",Wire.Hash(start));
            Assert.AreEqual("5a12c7f2bf9d2d15a1ff04d50818988e23dacecd6c08698122305757a0a66cea",Wire.Hash(end));
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,7109,roster);
            var authority=PlayableAuthorityTick.RestoreBytes(start,config);
            using(var sink=new StreamWriter(Path.Combine(folder,"combined-safe-last-funding-window.jsonl")))
            using(var host=new UnityHostRouteService())
            try
            {
                CollectionAssert.AreEqual(start,authority.CaptureBytes());
                PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests="ai-0";
                var survival=new NativeAiNaturalCycleTests.SurvivalEvidence();
                PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=line=>{sink.WriteLine(line);survival.Observe(line);};
                while(authority.Tick<17400)
                {
                    bool advanced=false;
                    for(int i=0;i<512;i++){if(authority.TryAdvance()){advanced=true;break;}host.Service(authority,64);}
                    Assert.True(advanced,"ordinary native route barrier at "+authority.Tick);
                    Assert.IsNull(authority.Latest.Failure);
                }
                sink.Flush();
                Assert.Contains("ai-0",survival.DeferredOwners.ToArray(),"Q8 requires an actual same-tick uncovered threat and denied technology funding");
                CollectionAssert.AreEqual(end,authority.CaptureBytes(),"actual guarded all-owner replay must match natural checkpoint");
            }
            finally
            {
                PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=null;
                PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;
                authority.Stop();
            }
        }
        [TestCase(4200,"dd897420f4a75d97879162cb3ccdd075f02e1fafb8ac54fc9d6bb541689249a3")]
        [TestCase(4800,"26b642337d601d3b9c9ac241f2f4ea6be0e39c02e238790df7ce4004c3d0a6f7")]
        [TestCase(17100,"999912977d5ca9155ad068edc34244133a6180dcfe42b4dcb9b8f0dc60d514c3")]
        [TestCase(17400,"5a12c7f2bf9d2d15a1ff04d50818988e23dacecd6c08698122305757a0a66cea")]
        public void CurrentSafeTechnologyReadbackIsPure(int tick,string hash)
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());
            while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;
            Assert.NotNull(root);
            string folder=Path.Combine(root.FullName,".local/o6-natural");
            var bytes=File.ReadAllBytes(Path.Combine(folder,$"7109-checkpoint-{tick}.world"));
            Assert.AreEqual(hash,Wire.Hash(bytes));
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,7109,roster);
            var authority=PlayableAuthorityTick.RestoreBytes(bytes,config);
            try
            {
                CollectionAssert.AreEqual(bytes,authority.CaptureBytes());
                Assert.AreEqual(tick,authority.Tick);
                var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId=="ai-0");
                Assert.AreEqual("Safe",checkpoint.Opening.Opening.ToString());
                var budgets=authority.CaptureDiagnosticBudgets();
                var budget=budgets.Single(x=>x.OwnerId=="ai-0");
                var view=authority.ParticipantView("ai-0");
                var observation=PlayableAiObservation.From(view);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var scheduler=typeof(PlayableAuthorityTick).GetField("scheduler",flags).GetValue(authority);
                var owner=((IEnumerable)scheduler.GetType().GetField("owners",flags).GetValue(scheduler)).Cast<object>().Single(x=>(string)x.GetType().GetProperty("OwnerId",flags).GetValue(x)=="ai-0");
                bool emergency=(bool)owner.GetType().GetMethod("TechnologyEmergency",flags).Invoke(owner,new object[]{observation});
                var plan=new AiTechnologyPlanner().Plan(observation,profile,AiProfile.Initial,budget,emergency:emergency);
                File.WriteAllText(Path.Combine(folder,$"combined-safe-{tick}-readback.json"),JsonConvert.SerializeObject(new {
                    Source="a6dc68dddd64a8c335bb4110c5712ef5df9e3760",CheckpointHash=hash,Tick=tick,ExactRestore=true,Emergency=emergency,
                    view.Credits,view.OwnerEliminated,view.OwnerResearch,plan.Reason,plan.Infrastructure,plan.Action,plan.FundingGoal,
                    Branches=plan.Branches.Select(b=>new{Kind=b.Descriptor.Kind.ToString(),b.Utility,b.AffectedValue,b.MissingCost}),
                    Budget=budget,AllOwnerBudgets=budgets,Records=checkpoint.Records,
                    OwnBuildings=view.Buildings.Where(b=>b.Owner==view.Owner),Army=checkpoint.Armies
                },Formatting.Indented));
                CollectionAssert.AreEqual(bytes,authority.CaptureBytes(),"pure query changed authority");
            }
            finally {authority.Stop();}
        }
    }
}

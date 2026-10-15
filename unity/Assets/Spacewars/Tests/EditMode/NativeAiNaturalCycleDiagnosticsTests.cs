using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Headless;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiNaturalCycleDiagnosticsTests
    {
        [Test] public void NaturalCheckpoints_RestoreExactlyAndExplainTechnologyAffordability()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;Assert.NotNull(root);
            string folder=Path.Combine(root.FullName,".local/o6-natural");int checkedStates=0;
            using(var sink=new StreamWriter(Path.Combine(folder,"technology-diagnosis.jsonl")))
            foreach(int seed in new[]{7108,7109})
            {
                var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
                var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
                var config=((AiTestMap)p.AuthoredMap).Configuration(p,seed,roster);
                var paths=Directory.GetFiles(folder,seed+"-checkpoint-*.world");Assert.IsNotEmpty(paths);
                foreach(string path in paths.OrderBy(x=>long.Parse(Path.GetFileNameWithoutExtension(x).Split('-').Last())))
                {
                    var bytes=File.ReadAllBytes(path);var a=PlayableAuthorityTick.RestoreBytes(bytes,config);
                    CollectionAssert.AreEqual(bytes,a.CaptureBytes(),"exact C9 restore of "+path);checkedStates++;
                    foreach(var budget in a.CaptureDiagnosticBudgets())
                    {
                        var view=a.ParticipantView(budget.OwnerId);var observation=PlayableAiObservation.From(view);
                        if(seed==7108&&a.Tick==1800&&budget.OwnerId=="ai-2")Assert.True(NativeAiNaturalCycleTests.HasReadyExpansion(view),"original Mine9 checkpoint is a real nonhome expansion");
                        // No advance, command, forced opening or artificial threat override in the match.
                        // This counterfactual removes emergency deferral ONLY for a pure read-only
                        // proposal, to isolate affordability from tactical suppression.
                        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                        var scheduler=typeof(PlayableAuthorityTick).GetField("scheduler",flags).GetValue(a);
                        var owner=((IEnumerable)scheduler.GetType().GetField("owners",flags).GetValue(scheduler)).Cast<object>().Single(x=>(string)x.GetType().GetProperty("OwnerId",flags).GetValue(x)==budget.OwnerId);
                        bool emergency=(bool)owner.GetType().GetMethod("TechnologyEmergency",flags).Invoke(owner,new object[]{observation});
                        var actual=new AiTechnologyPlanner().Plan(observation,p,AiProfile.Initial,budget,emergency:emergency);
                        var plan=new AiTechnologyPlanner().Plan(observation,p,AiProfile.Initial,budget,emergency:false);
                        sink.WriteLine(JsonConvert.SerializeObject(new{seed,Tick=a.Tick,budget.OwnerId,Checkpoint=Path.GetFileName(path),CheckpointHash=Wire.Hash(bytes),ExactRestore=true,CorrectedReadyExpansion=NativeAiNaturalCycleTests.HasReadyExpansion(view),EmergencyDisabledForPureQuery=true,ObservedEmergency=emergency,ActualReason=actual.Reason,ActualInfrastructure=actual.Infrastructure,ActualAction=actual.Action,ActualBranches=actual.Branches.Select(b=>new{Kind=b.Descriptor.Kind.ToString(),b.Utility,b.AffectedValue}),observation.Credits,view.HomeSiteId,view.IncomePerSecond,view.SettledIncome,ReadyExpansionSites=view.Buildings.Where(b=>b.Owner==view.Owner&&b.Phase==ConstructionPhase.Ready&&b.SiteId!=view.HomeSiteId&&(b.Kind==PlayableBuildingKind.Mine||b.Kind==PlayableBuildingKind.Outpost||b.Kind==PlayableBuildingKind.Headquarters)).Select(b=>new{b.Id,b.SiteId,Kind=b.Kind.ToString()}),budget.PaidTotal,PaidTechnology=budget.Paid.Where(e=>e.Action.Kind==PlayableCommandKind.QueueResearch||e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.ScientificCenter),AcceptedUnpaidTechnology=budget.Unpaid.Where(e=>e.Action.Kind==PlayableCommandKind.QueueResearch||e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.ScientificCenter),budget.SafetyReserve,Unpaid=budget.Unpaid.Sum(x=>(long)x.Amount),Reserved=budget.Reserved.Sum(x=>(long)x.Amount),plan.Reason,Infrastructure=plan.Infrastructure,Action=plan.Action,Branches=plan.Branches.Select(b=>new{Kind=b.Descriptor.Kind.ToString(),b.Progress,b.Utility,b.AffectedValue,b.MissingCost,ResearchCost=b.Descriptor.Cost(p),ProductiveReady=AiTechnologyPlanner.ProductivePrerequisiteReady(observation,b.Descriptor)}),Research=observation.OwnerResearch}));
                    }
                    CollectionAssert.AreEqual(bytes,a.CaptureBytes(),"pure diagnostics changed checkpoint "+path);
                    a.Stop();
                }
            }
            Assert.Greater(checkedStates,0);TestContext.WriteLine("O6 exact restored natural checkpoints="+checkedStates);
        }
    }
}

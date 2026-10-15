using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Collections;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Headless;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiTechnologyPlacementDiagnosticTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static object Get(object v,string key)=>v.GetType().GetField(key,F).GetValue(v);
        [Test] public void TwoActualStates_ExplainFundingAndPlacementWithoutWorldMutation()
        {
            var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(root!=null&&!File.Exists(Path.Combine(root.FullName,"workflow/project.json")))root=root.Parent;Assert.NotNull(root);
            var p=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)p.AuthoredMap).Configuration(p,7109,roster);
            var folder=Path.Combine(root.FullName,".local/o6-placement");Directory.CreateDirectory(folder);
            using(var output=new StreamWriter(Path.Combine(folder,"facts.jsonl")))
            foreach(int tick in new[]{3900,4050})
            {
                byte[] bytes;
                if(tick==3900)bytes=File.ReadAllBytes(Path.Combine(root.FullName,"unity/Tests/Fixtures/o6-tech-funding/checkpoint-3900.world"));
                else using(var zip=ZipFile.OpenRead(Path.Combine(root.FullName,"docs/native-ai/o6/third-funding-interrupted-final-raw.zip")))using(var input=zip.GetEntry(".local/o6-funding-mechanism/checkpoint-4050.world").Open())using(var stream=new MemoryStream()){input.CopyTo(stream);bytes=stream.ToArray();}
                var a=PlayableAuthorityTick.RestoreBytes(bytes,config);Assert.AreEqual(tick,a.Tick);CollectionAssert.AreEqual(bytes,a.CaptureBytes(),"original/current typed memo exact input");
                var owner=((IEnumerable)Get(Get(a,"scheduler"),"owners")).Cast<object>().Single(x=>(string)x.GetType().GetProperty("OwnerId",F).GetValue(x)=="ai-4");
                var view=a.ParticipantView("ai-4");var o=PlayableAiObservation.From(view);var budget=((AiBudgetLedger)Get(owner,"budget")).Capture();
                bool emergency=(bool)owner.GetType().GetMethod("TechnologyEmergency",F).Invoke(owner,new object[]{o});
                var funding=(AiTechnologyFunding)Get(owner,"technologyFunding");var arbiter=(AiDecisionArbiter)Get(owner,"arbiter");
                var plan=new AiTechnologyPlanner().Plan(o,p,AiProfile.Initial,budget,emergency:emergency,preferredFunding:funding.Preferred(arbiter));
                var placement=new AiEconomyPlanner().InfrastructureRequests(o,p,plan.FundingGoal);
                var expansion=(AiExpansionPlanner)Get(owner,"expansion");var fork=(AiExpansionPlanner)expansion.GetType().GetMethod("Fork",F).Invoke(expansion,null);var proposal=fork.TryPlan(o,p,AiProfile.Initial);
                output.WriteLine(JsonConvert.SerializeObject(new{tick,Source=tick==3900?"original-natural":"interrupted6f",CheckpointHash=Wire.Hash(bytes),ExactRestore=true,Projection="existing participant view; no new route requests, route pumping, advance or emergency override",o.Credits,o.SettledIncome,view.IncomePerSecond,
                    Force=AiDefensePlanner.Force(o.Entities.Where(u=>u.Owner==o.Owner&&u.Health>0&&AiRosterCatalog.Initial.For(u.Kind).lineWeight>0&&string.IsNullOrWhiteSpace(AiRosterCatalog.Initial.For(u.Kind).exclusionReason)).ToArray(),p,AiProfile.Initial),ForceReference=p.TankWeaponDamage/p.TankWeaponReloadSeconds*AiProfile.Initial.Value("utility.forceHorizonSeconds")*AiProfile.Initial.Value("armies.minimumUnits"),MinimumUnits=AiProfile.Initial.Value("armies.minimumUnits"),OwnUnits=o.Entities.Where(u=>u.Owner==o.Owner).Select(u=>new{u.Id,u.Kind,u.Health}),MissingEssentialEconomy=((AiOpeningExecutor)Get(owner,"openingExecutor")).MissingEssentialEconomy(o,AiProfile.Initial),ViableCover=((AiPhaseCompositionPlanner)Get(owner,"composition")).ViableCover(o,p,AiProfile.Initial),TechnologyEmergency=emergency,Opening=((AiOpeningExecutor)Get(owner,"openingExecutor")).Capture(),plan.FundingGoal,plan.Infrastructure,plan.Reason,plan.Action,
                    Branches=plan.Branches.Select(b=>new{b.Descriptor.Kind,b.Utility,b.AffectedValue,ProductiveReady=AiTechnologyPlanner.ProductivePrerequisiteReady(o,b.Descriptor)}),Placement=placement.Select(c=>new{c.Legal,c.Reason,c.Action.SiteId,c.Action.SlotId,c.Action.ParentId,c.Action.BuildingKind}),
                    KnownOwnedSites=o.Sites.Where(s=>s.Owner==o.Owner).Select(s=>new{s.Site.Id,s.Site.Kind,s.Ready,s.Contested,s.CenterId,Slots=s.Site.Slots.Select(slot=>new{slot.Id,Occupants=o.Buildings.Where(b=>b.SiteId==s.Site.Id&&b.SlotId==slot.Id).Select(b=>new{b.Id,b.Kind,b.Phase,b.Health,Selling=b.PrivateState?.Lifecycle?.Selling})})}),
                    ExistingExpansion=expansion.Capture(),ForkExpansionAfterPureQuery=fork.Capture(),ExpansionProposal=proposal,PublicOptions=o.PublicScoutObjectives,Explorers=o.Entities.Where(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0),VisibleHostile=o.Entities.Where(u=>o.IsHostile(u.Owner)&&u.Health>0),budget.Reserved,budget.Unpaid}));output.Flush();
                CollectionAssert.AreEqual(bytes,a.CaptureBytes(),"pure guards/planners/fork/view changed world/AI");a.Stop();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiCurrentSafeFundingDiagnosticTests
    {
        private const string Archive="docs/native-ai/o6/current-natural-20261012-source-compiled-raw.zip";
        private const string ArchiveHash="8e4f56446e3eab63dbc935b15493fa6b20c160e9c50ed026dcaa4fe8c3dac081";
        private const string Entry="natural/7109-checkpoint-4800.world";
        private const string InputHash="5b415a09014b59db4fcd0eb137370fb9bcefa990bed20e7b0e6ee738ae9e2e10";
        private const int StartTick=4800,TickCount=45;
        private static string Root(){var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static void Advance(PlayableAuthorityTick a,UnityHostRouteService host){for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("ordinary typed-route barrier timed out at "+a.Tick);}
        private static void AssertBytesEqual(byte[] expected,byte[] actual,string message)
        {if(expected.Length!=actual.Length)Assert.Fail(message+" length "+expected.Length+" != "+actual.Length);for(int i=0;i<expected.Length;i++)if(expected[i]!=actual[i])Assert.Fail(message+" first differing byte "+i+" ("+expected[i]+" != "+actual[i]+")");}
        private static byte[] ReadEntry(string path,string entry){using(var z=ZipFile.OpenRead(path))using(var s=z.GetEntry(entry).Open())using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}
        private sealed class Run{internal byte[][] World;internal string[] Decisions,Views,Diagnostics;internal int RealDecisionCount;}

        [Test]
        public void CurrentSafeFunding_45OrdinaryTicks_HooksAreByteAndDecisionNeutral()
        {
            var root=Root();var archive=Path.Combine(root,Archive);Assert.AreEqual(ArchiveHash,Wire.Hash(File.ReadAllBytes(archive)),"immutable current natural source packet");
            var input=ReadEntry(archive,Entry);Assert.AreEqual(InputHash,Wire.Hash(input),"exact immutable checkpoint member");
            const int seed=7109;const string owner="ai-0";
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,seed,participants);
            Assert.AreEqual(500,profile.StartingCredits);Assert.AreEqual(8,config.Roster.Count);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            Run Execute(bool enabled,Run expected=null)
            {
                var authority=PlayableAuthorityTick.RestoreBytes(input,config);var worlds=new List<byte[]>();var decisions=new List<string>();var views=new List<string>();var diagnostics=new List<string>();
                try
                {
                    Assert.AreEqual(StartTick,authority.Tick);CollectionAssert.AreEqual(input,authority.CaptureBytes(),"exact original bytes immediately after restore");
                    var initial=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==owner);
                    Assert.AreEqual(seed,initial.Seed);Assert.AreEqual(71,initial.Generation);Assert.AreEqual("Safe",initial.Opening.Opening.ToString());
                    var initialBudget=authority.CaptureDiagnosticBudgets().Single(x=>x.OwnerId==owner);Assert.AreEqual(96,initialBudget.Liquid);Assert.AreEqual(0,initialBudget.SafetyReserve);Assert.IsEmpty(initialBudget.Unpaid);
                    var initialHold=initialBudget.Reserved.Single(AiTechnologyFunding.IsFunding);Assert.AreEqual(92,initialHold.Amount);Assert.AreEqual(4793,initialHold.CreatedTick);Assert.AreEqual(6143,initialHold.ExpiresTick);
                    Assert.AreEqual(PlayableResearchKind.TankChassis,initialHold.Action.ResearchKind);Assert.AreEqual(1,initialHold.Action.SiteId);Assert.AreEqual(3,initialHold.Action.SlotId);Assert.AreEqual(1,initialHold.Action.ParentId);
                    CollectionAssert.AreEqual(input,authority.CaptureBytes(),"checkpoint reads preserve all original bytes");
                    void CaptureDiagnostic(string row)
                    {
                        diagnostics.Add(row);
                    }
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=enabled?new Action<string>(CaptureDiagnostic):null;
                    PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=enabled?owner:null;
                    AiTechnologyFunding.CurrentSafeFundingObserveForTests=enabled?(Action<string,long,string,string,string>)((who,tick,before,after,detail)=>CaptureDiagnostic(JsonConvert.SerializeObject(new{Owner=who,Tick=tick,Stage="TechnologyFunding.Observe",Before=before,After=after,Detail=detail}))):null;
                    AiTechnologyFunding.CurrentSafeFundingObserveOwnerForTests=enabled?owner:null;
                    AiTechnologyFunding.CurrentSafeFundingMutationReadbackForTests=enabled?(Action<string,long,string,string,string>)((who,tick,before,after,detail)=>CaptureDiagnostic(JsonConvert.SerializeObject(new{Owner=who,Tick=tick,Stage="TechnologyFunding."+detail.Split(';')[0],Before=before,After=after,Detail=detail}))):null;
                    AiTechnologyFunding.CurrentSafeFundingMutationOwnerForTests=enabled?owner:null;
                    AiBudgetLedger.CurrentSafeFundingExpiryReadbackForTests=enabled?(Action<string,long,string,string,string>)((who,tick,id,before,after)=>CaptureDiagnostic(JsonConvert.SerializeObject(new{Owner=who,Tick=tick,Stage="TechnologyExpireRemoval",Entry=id,Before=before,After=after}))):null;
                    AiBudgetLedger.CurrentSafeFundingExpiryOwnerForTests=enabled?owner:null;
                    PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=enabled?new Action<PlayableAiProposalAdmissionReadback>(r=>CaptureDiagnostic(JsonConvert.SerializeObject(new{r.OwnerId,r.Tick,r.Policy,r.Stage,r.Reason,Action=r.Action==null?null:new{r.Action.Kind,r.Action.SiteId,r.Action.ResearchKind}}))):null;
                    PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests=enabled?owner:null;
                    AiDecisionArbiter.SelectionReadbackOwnerFilterForTests=enabled?owner:null;
                    AiDecisionArbiter.SelectionReadbackForTests=enabled?(Action<AiArbiterSelectionReadback>)(r=>CaptureDiagnostic(JsonConvert.SerializeObject(new{r.OwnerId,r.Ordered,Bands=r.Bands.OrderBy(x=>x.Key).ToArray()}))):null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=enabled?owner:null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=enabled?new Action<CurrentExpansionDecisionReadback>(c=>CaptureDiagnostic(JsonConvert.SerializeObject(new{Tick=c.Observation.Tick,Owner=c.Observation.OwnerId,Ledger=c.Ledger.Reserved.Where(e=>AiTechnologyFunding.IsFunding(e)).Select(e=>new{e.Id,e.Amount,e.CreatedTick,e.ExpiresTick}).ToArray(),c.ProtectedFunds,c.Threatened,c.UrgentRepair,c.MissingScout,c.EssentialScale,c.ScalingReason,c.ScalingStartup,Proposals=c.Proposals.Select(p=>new{p.Policy,p.Credits,p.StartupFund,p.ReservationId,Kind=p.Action.Kind,Research=p.Action.ResearchKind,Site=p.Action.SiteId}).ToArray(),c.Selected,c.Rejections}))):null;
                    using(var host=new UnityHostRouteService())for(int i=0;i<TickCount;i++)
                    {
                        Advance(authority,host);Assert.AreEqual(StartTick+i+1,authority.Tick);
                        var world=authority.CaptureBytes();var cp=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==owner);
                        var decision=JsonConvert.SerializeObject(new{authority.Tick,cp.DecisionTick,cp.LastPolicy,cp.LastActionKind,cp.PendingActionId,Records=cp.Records.ToArray(),Economy=cp.EconomyDecision==null?null:new{cp.EconomyDecision.Tick,cp.EconomyDecision.Reason,cp.EconomyDecision.Blockers}});
                        var view=JsonConvert.SerializeObject(Enumerable.Range(0,8).Select(n=>authority.ParticipantView("ai-"+n)));
                        var afterReadbacks=authority.CaptureBytes();AssertBytesEqual(world,afterReadbacks,"observer reads changed bytes at tick "+authority.Tick);
                        worlds.Add(world);decisions.Add(decision);views.Add(view);
                        if(expected!=null){AssertBytesEqual(expected.World[i],world,"complete world and AI bytes at tick "+authority.Tick);Assert.AreEqual(expected.Decisions[i],decision,"ordinary decisions and receipts at tick "+authority.Tick);Assert.AreEqual(expected.Views[i],view,"all eight participant views at tick "+authority.Tick);}
                    }
                    Assert.AreEqual(StartTick+TickCount,authority.Tick);
                    return new Run{World=worlds.ToArray(),Decisions=decisions.ToArray(),Views=views.ToArray(),Diagnostics=diagnostics.ToArray(),RealDecisionCount=decisions.Select(JsonConvert.DeserializeObject<JObject>).Select(x=>(long)x["DecisionTick"]).Where(t=>t>=StartTick+1).Distinct().Count()};
                }
                finally
                {
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=null;PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;
                    AiTechnologyFunding.CurrentSafeFundingObserveForTests=null;AiTechnologyFunding.CurrentSafeFundingObserveOwnerForTests=null;
                    AiTechnologyFunding.CurrentSafeFundingMutationReadbackForTests=null;AiTechnologyFunding.CurrentSafeFundingMutationOwnerForTests=null;
                    AiBudgetLedger.CurrentSafeFundingExpiryReadbackForTests=null;AiBudgetLedger.CurrentSafeFundingExpiryOwnerForTests=null;
                    PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=null;PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests=null;
                    AiDecisionArbiter.SelectionReadbackForTests=null;AiDecisionArbiter.SelectionReadbackOwnerFilterForTests=null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;
                    authority.Stop();
                }
            }
            var off=Execute(false);Assert.AreEqual(TickCount,off.World.Length);var on=Execute(true,off);
            Assert.IsEmpty(off.Diagnostics);Assert.IsNotEmpty(on.Diagnostics,"owner-filtered actual diagnostics observed");
            Assert.Greater(on.RealDecisionCount,0,"at least one actual ordinary owner decision in the 45-tick window");
            var diagnosticObjects=on.Diagnostics.Select(JsonConvert.DeserializeObject<JObject>).ToArray();
            Assert.IsTrue(diagnosticObjects.Any(x=>(string)x["Stage"]=="ActualCandidatesBeforeSelection"),"actual ordinary proposal candidates reached arbitration");
            var admissions=diagnosticObjects.Where(x=>x["Policy"]!=null&&x["Reason"]!=null&&x["OwnerId"]!=null).ToArray();
            Assert.IsNotEmpty(admissions,"at least one in-place proposal admission outcome was observed");
            TestContext.WriteLine("CURRENT_SAFE_FUNDING "+JsonConvert.SerializeObject(new{Seed=seed,Owner=owner,StartTick,EndTick=StartTick+TickCount,Entry,InputHash=Wire.Hash(input),HookOffOn=true,FullWorldBytesEqual=true,AllEightParticipantViewsEqual=true,DecisionsAndReceiptsEqual=true,FirstObservedProposalAdmission=admissions[0],Diagnostics=on.Diagnostics}));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    public sealed class NativeAiCurrentSafe7109ScoutOriginDiagnosticTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const string Archive="docs/native-ai/q4/safe-financial-retarget-20261013/attempts/root-preservation/owned-raw-natural-focused.tar.zst";
        private const string ArchiveHash="84dd7ec4ec2bd73953f3d9fafbc822510b60ad8c448c150d4dbc97d1fb2e3385";
        private const string Input=".local/qa/q4-safe7109-scout-origin-20261013/input/7109-checkpoint-5400.world";
        private const string InputHash="08bba8ccef7c73dd0b94b7e8e84742e7ce78d79111caff07acd2918928150c74";
        private const string Control=".local/qa/q4-safe7109-scout-origin-20261013/input/7109-checkpoint-5700.world";
        private const string ControlHash="8cf1b42c66159bbf497a3d2c3f9e7a3d3adfa896b828f1995071a23bdd156b1c";
        private const string Owner="ai-0";
        private static string Root(){var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static object Get(object x,string name){var f=x.GetType().GetField(name,F);Assert.NotNull(f,name);return f.GetValue(x);}
        private static void Advance(PlayableAuthorityTick a,UnityHostRouteService host){for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("typed-route barrier timed out at "+a.Tick);}
        private static void EqualBytes(byte[] a,byte[] b,string message){if(a.Length!=b.Length)Assert.Fail(message+" length differs");for(int i=0;i<a.Length;i++)if(a[i]!=b[i])Assert.Fail(message+" first difference "+i);}
        private static double Milliseconds(long ticks)=>ticks*1000.0/Stopwatch.Frequency;
        private sealed class CoverageConverter:JsonConverter
        {
            private static readonly ReadOnlyCollection<string> Tokens=Array.AsReadOnly(Enumerable.Range(0,256).Select(x=>x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
            public override bool CanConvert(Type t)=>t==typeof(ReadOnlyCollection<byte>);public override bool CanRead=>false;
            public override void WriteJson(JsonWriter w,object value,JsonSerializer s){if(value==null){w.WriteNull();return;}var b=(ReadOnlyCollection<byte>)value;w.WriteStartArray();for(int i=0;i<b.Count;i++)w.WriteRawValue(Tokens[b[i]]);w.WriteEndArray();}
            public override object ReadJson(JsonReader r,Type t,object e,JsonSerializer s)=>throw new NotSupportedException();
        }
        private sealed class CoverageResolver:DefaultContractResolver
        {
            private readonly JsonConverter converter;internal CoverageResolver(JsonConverter c){converter=c;}
            protected override JsonProperty CreateProperty(MemberInfo m,MemberSerialization ms){var p=base.CreateProperty(m,ms);if(m.DeclaringType==typeof(TeamVisionSnapshot)&&m.Name=="Coverage")p.Converter=converter;return p;}
        }
        private static JsonSerializer CreateViewSerializer()
        {
            var c=new CoverageConverter();var s=JsonSerializer.Create(new JsonSerializerSettings{ContractResolver=new CoverageResolver(c)});s.Converters.Add(c);return s;
        }
        private static string ViewsJson(JsonSerializer s,object views)
        {
            var b=new StringBuilder();using(var sw=new StringWriter(b,System.Globalization.CultureInfo.InvariantCulture))using(var w=new JsonTextWriter(sw){Formatting=Formatting.None}){s.Serialize(w,views);w.Flush();}return b.ToString();
        }
        private sealed class Tick { internal byte[] World; internal string Facts,Checkpoints,Ledgers,Receipts,Views,Origin,RequiredReceipts; }
        private sealed class Run { internal Tick[] Ticks; internal byte[] FinalWorld; internal bool Parity=true; internal readonly List<string> Plan=new List<string>(),Events=new List<string>(); internal long CaptureMs,ReadMs,SerializeMs,WriteMs; }

        [Test]
        [Timeout(180000)]
        public void CurrentSafe7109_5400To5700_ScoutOriginDiagnosticIsByteAndFactNeutral()
        {
            var root=Root();var archive=File.ReadAllBytes(Path.Combine(root,Archive));Assert.AreEqual(ArchiveHash,Wire.Hash(archive),"immutable source archive");
            var input=File.ReadAllBytes(Path.Combine(root,Input));var control=File.ReadAllBytes(Path.Combine(root,Control));
            Assert.AreEqual(InputHash,Wire.Hash(input),"exact 5400 checkpoint");Assert.AreEqual(ControlHash,Wire.Hash(control),"exact 5700 control");
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,7109,participants);Assert.AreEqual(8,config.Roster.Count);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            var evidence=Path.Combine(root,".local/qa/q4-safe7109-scout-origin-20261013/attempt-01");Directory.CreateDirectory(evidence);
            Run Execute(bool enabled,Run baseline=null,StreamWriter timeline=null)
            {
                var authority=PlayableAuthorityTick.RestoreBytes(input,config);var run=new Run{Ticks=new Tick[300]};var domain=Get(authority,"domain");
                var viewSerializer=CreateViewSerializer();
                try
                {
                    Assert.AreEqual(5400,authority.Tick);EqualBytes(input,authority.CaptureBytes(),"restore bytes");
                    var cp=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==Owner);Assert.AreEqual(profile.ProfileId,cp.ProfileId);Assert.AreEqual(7109,cp.Seed);Assert.AreEqual(71,cp.Generation);Assert.AreEqual(AiDifficulty.Fighter,cp.Difficulty);Assert.AreEqual("Safe",cp.Opening.Opening.ToString());
                    AiScoutPlanner.PlanReadbackOwnerFilterForTests=enabled?Owner:null;
                    AiScoutPlanner.PlanReadbackForTests=enabled?(Action<AiScoutPlanReadbackForTests>)(r=>run.Plan.Add(JsonConvert.SerializeObject(new{r.OwnerId,r.Generation,r.Tick,r.Stage,r.UnitId,r.SiteId,r.Value}))):null;
                    PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=enabled?Owner:null;
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=enabled?(Action<string>)(s=>run.Events.Add(s)):null;
                    PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests=enabled?Owner:null;
                    PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=enabled?(Action<PlayableAiProposalAdmissionReadback>)(r=>run.Events.Add(JsonConvert.SerializeObject(new{r.OwnerId,r.Tick,r.Policy,r.Stage,r.Reason,Action=r.Action==null?null:new{r.Action.Kind,r.Action.SiteId,Actors=r.Action.CopyEntityIds()}}))):null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=enabled?Owner:null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=enabled?(Action<CurrentExpansionDecisionReadback>)(r=>run.Events.Add(JsonConvert.SerializeObject(new{Tick=r.Observation.Tick,Owner=r.Observation.OwnerId,r.Ledger,r.ProtectedFunds,r.FinanceSite,r.FinanceReady,r.AllowsExpansion,r.ExpansionAction,r.ExpansionIntent,r.Proposals,r.Selected,r.Rejections,r.BeforeFinanceDetach,r.AfterTechnologyAllocation,r.AfterFinancialAllocation,r.ArmyActors,r.PendingActors}))):null;
                    using(var host=new UnityHostRouteService())for(int i=0;i<300;i++)
                    {
                        var planOffset=run.Plan.Count;var eventOffset=run.Events.Count;long timer=Stopwatch.GetTimestamp();
                        Advance(authority,host);var tick=(int)authority.Tick;var world=authority.CaptureBytes();run.CaptureMs+=Stopwatch.GetTimestamp()-timer;
                        timer=Stopwatch.GetTimestamp();var facts=PlayableWorldRestoreTests.Facts(domain,true);run.ReadMs+=Stopwatch.GetTimestamp()-timer;
                        timer=Stopwatch.GetTimestamp();var checkpointsObject=authority.CaptureDiagnosticCheckpoints();var ledgersObject=authority.CaptureDiagnosticBudgets();
                        var viewsObject=Enumerable.Range(0,8).Select(n=>authority.ParticipantView("ai-"+n)).ToArray();
                        var checkpoints=JsonConvert.SerializeObject(checkpointsObject);var ledgers=JsonConvert.SerializeObject(ledgersObject);var views=ViewsJson(viewSerializer,viewsObject);
                        var receiptRows=checkpointsObject.Select(c=>new{c.OwnerId,Receipts=c.Records.ToArray()}).ToArray();
                        var receipts=JsonConvert.SerializeObject(receiptRows);run.SerializeMs+=Stopwatch.GetTimestamp()-timer;
                        Assert.AreEqual(8,checkpointsObject.Length,"all owner checkpoints at "+tick);Assert.AreEqual(8,ledgersObject.Length,"all owner ledgers at "+tick);Assert.AreEqual(8,viewsObject.Length,"all participant views at "+tick);
                        var ownerCheckpoint=checkpointsObject.Single(c=>c.OwnerId==Owner);var ownerView=viewsObject.Single(v=>v.OwnerId==Owner);
                        var knowledge=ownerCheckpoint.Knowledge;var scoutArmy=ownerCheckpoint.Armies.Armies.Where(a=>a.Role==AiArmyRole.Scout&&a.Members.Contains(76)).Select(a=>new{a.Id,a.OwnerId,Role=a.Role.ToString(),Phase=a.Phase.ToString(),a.Objective,a.Members,a.LeaderId,a.CreatedTick,a.ProgressTick}).ToArray();
                        var physical76=ownerView.Entities.FirstOrDefault(x=>x.Id==76);
                        var site2=ownerView.Sites.FirstOrDefault(x=>x.Site.Id==2);var site9=ownerView.Sites.FirstOrDefault(x=>x.Site.Id==9);
                        var discovery=new{Site2Discovered=ownerView.DiscoveredSites.Any(s=>s.Id==2),Site9Discovered=ownerView.DiscoveredSites.Any(s=>s.Id==9),
                            Site2Visible=site2!=null&&ownerView.Vision!=null&&ownerView.Vision.IsVisible(site2.Site.Position),Site9Visible=site9!=null&&ownerView.Vision!=null&&ownerView.Vision.IsVisible(site9.Site.Position),
                            Site2Coverage=knowledge.Areas.FirstOrDefault(a=>a.AreaId==2),Site9Coverage=knowledge.Areas.FirstOrDefault(a=>a.AreaId==9),
                            Site2View=site2,Site9View=site9,Site9Funding=ledgersObject.Single(x=>x.OwnerId==Owner).Reserved.Concat(ledgersObject.Single(x=>x.OwnerId==Owner).Unpaid).Where(e=>e.Purpose==AiExpansionFinancial.Purpose&&e.Action.SiteId==9).Select(e=>new{e.Id,e.Amount,e.CreatedTick,e.ExpiresTick,e.Purpose,Action=e.Action.Kind}).ToArray()};
                        var requiredReceipts=JsonConvert.SerializeObject(new{WorldCaptureTick=tick,Records=ownerCheckpoint.Records.Where(r=>r.ActionId==203).Select(r=>new{r.ActionId,r.Policy,Kind=r.Kind?.ToString(),r.DueTick,r.ApplicationTick,Status=r.Status.ToString(),RuntimeStatus=r.RuntimeStatus?.ToString(),ReceiptIdentity=r.ReceiptIdentity==null?null:new{r.ReceiptIdentity.Id,r.ReceiptIdentity.Generation,r.ReceiptIdentity.OwnerId,r.ReceiptIdentity.DecisionOrdinal,r.ReceiptIdentity.ActionOrdinal}}).ToArray()});
                        var origin=JsonConvert.SerializeObject(new{Site2=discovery.Site2View,discovery.Site2Discovered,discovery.Site2Visible,Site2Coverage=discovery.Site2Coverage,Site9=discovery.Site9View,discovery.Site9Discovered,discovery.Site9Visible,Site9Coverage=discovery.Site9Coverage,discovery.Site9Funding,Explorer76=physical76,ScoutArmy76=scoutArmy,ScoutKnowledge=new{knowledge.OwnerId,knowledge.Generation,knowledge.ObservationTick,knowledge.Areas,knowledge.Contacts},Pending=new{ownerCheckpoint.PendingActionId,ownerCheckpoint.PendingDueTick}});
                        Assert.AreEqual(8,receiptRows.Length,"all-owner receipt sets");Assert.IsTrue(receiptRows.All(x=>x.Receipts!=null),"all owner receipt arrays are present");
                        var current=new Tick{World=world,Facts=facts,Checkpoints=checkpoints,Ledgers=ledgers,Receipts=receipts,Views=views,Origin=origin,RequiredReceipts=requiredReceipts};
                        if(baseline!=null){var prior=baseline.Ticks[i];EqualBytes(prior.World,world,"hook parity "+tick);Assert.AreEqual(prior.Facts,facts,"authority fact parity "+tick);Assert.AreEqual(prior.Checkpoints,checkpoints,"all-owner checkpoint parity "+tick);Assert.AreEqual(prior.Ledgers,ledgers,"all-owner ledger parity "+tick);Assert.AreEqual(prior.Receipts,receipts,"all-owner receipt parity "+tick);Assert.AreEqual(prior.Views,views,"all-view parity "+tick);}
                        timer=Stopwatch.GetTimestamp();var afterWorld=authority.CaptureBytes();EqualBytes(world,afterWorld,"diagnostic reads preserve world at "+tick);
                        Assert.AreEqual(facts,PlayableWorldRestoreTests.Facts(domain,true),"authority facts read-neutral at "+tick);
                        var afterCheckpoints=authority.CaptureDiagnosticCheckpoints();var afterLedgers=authority.CaptureDiagnosticBudgets();
                        Assert.AreEqual(checkpoints,JsonConvert.SerializeObject(afterCheckpoints),"checkpoints read-neutral at "+tick);
                        Assert.AreEqual(ledgers,JsonConvert.SerializeObject(afterLedgers),"ledgers read-neutral at "+tick);
                        Assert.AreEqual(receipts,JsonConvert.SerializeObject(afterCheckpoints.Select(c=>new{c.OwnerId,Receipts=c.Records.ToArray()}).ToArray()),"all-owner receipts read-neutral at "+tick);
                        Assert.AreEqual(views,ViewsJson(viewSerializer,Enumerable.Range(0,8).Select(n=>authority.ParticipantView("ai-"+n)).ToArray()),"views read-neutral at "+tick);run.ReadMs+=Stopwatch.GetTimestamp()-timer;
                        run.Ticks[i]=current;if(tick==5700)run.FinalWorld=world;
                        if(timeline!=null){timer=Stopwatch.GetTimestamp();timeline.WriteLine(JsonConvert.SerializeObject(new{Tick=tick,WorldHash=Wire.Hash(world),AuthorityFacts=facts,AllOwnerCheckpoints=checkpoints,AllOwnerLedgers=ledgers,AllOwnerReceipts=receipts,AllViews=views,
                            Origin=origin,RequiredActualReceipt203=requiredReceipts,ActualPlanReadbacks=enabled?run.Plan.Skip(planOffset).ToArray():Array.Empty<string>(),ActualOwnerEvents=enabled?run.Events.Skip(eventOffset).ToArray():Array.Empty<string>()}));timeline.Flush();run.WriteMs+=Stopwatch.GetTimestamp()-timer;}
                        if(baseline!=null)
                        {
                            baseline.Ticks[i]=null;
                            if(tick!=5423&&tick!=5454&&tick!=5468&&tick!=5700)run.Ticks[i]=null;
                        }
                    }
                    return run;
                }
                finally
                {
                    AiScoutPlanner.PlanReadbackForTests=null;AiScoutPlanner.PlanReadbackOwnerFilterForTests=null;
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=null;PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;
                    PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=null;PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests=null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;authority.Stop();
                }
            }
            Run off,on;
            using(var writer=new StreamWriter(Path.Combine(evidence,"timeline-off.jsonl"),false,new UTF8Encoding(false)))off=Execute(false,null,writer);
            using(var writer=new StreamWriter(Path.Combine(evidence,"timeline-on.jsonl"),false,new UTF8Encoding(false)))on=Execute(true,off,writer);
            var offFinal=off.FinalWorld;var onFinal=on.FinalWorld;
            File.WriteAllBytes(Path.Combine(evidence,"final-5700-hook-off.world"),offFinal);File.WriteAllBytes(Path.Combine(evidence,"final-5700-hook-on.world"),onFinal);
            File.WriteAllLines(Path.Combine(evidence,"plan-readbacks.jsonl"),on.Plan);File.WriteAllLines(Path.Combine(evidence,"owner-events.jsonl"),on.Events);
            bool ChoiceRow(JObject j)=>j!=null&&(int?)j["UnitId"]==76&&(int?)j["SiteId"]==2&&j["Value"]!=null&&(int?)j["Value"]["UnitId"]==76&&(int?)j["Value"]["SiteId"]==2;
            var choices=on.Plan.Select(JObject.Parse).Where(j=>(long?)j["Tick"]==5423&&(string)j["Stage"]=="final.choice").ToArray();
            var choice=choices.FirstOrDefault(ChoiceRow);bool selectedExplorer76Site2=choice!=null;
            JObject ReceiptCapture(long tick)=>JObject.Parse(on.Ticks[(int)tick-5401].RequiredReceipts);
            JArray ReceiptRows(long tick)=> (JArray)ReceiptCapture(tick)["Records"];
            bool Receipt(long captureTick,string status,long app)=>ReceiptCapture(captureTick)["WorldCaptureTick"].Value<long>()==captureTick&&ReceiptRows(captureTick).Any(r=>(long?)r["ActionId"]==203&&(string)r["Policy"]=="scout"&&(string)r["Status"]==status&&(long?)r["ApplicationTick"]==app&&(long?)r["DueTick"]==5453&&
                (string)r["ReceiptIdentity"]?["Id"]=="71:4:ai-0:121:1"&&(long?)r["ReceiptIdentity"]?["Generation"]==71&&(string)r["ReceiptIdentity"]?["OwnerId"]==Owner&&(long?)r["ReceiptIdentity"]?["DecisionOrdinal"]==121&&(int?)r["ReceiptIdentity"]?["ActionOrdinal"]==1);
            bool receiptScheduled=Receipt(5423,"Scheduled",0);bool receiptApplied=Receipt(5454,"Applied",5453);
            var coverage5468=JObject.Parse(on.Ticks[5468-5401].Origin);var site9Coverage=coverage5468["Site9Coverage"];
            bool site9Covered5468=(long?)site9Coverage?["ActuallyCoveredTick"]==5468;
            bool site9VisibilityDistinguished=coverage5468["Site9Discovered"]!=null&&coverage5468["Site9Visible"]!=null&&site9Coverage!=null;
            var scoutOrigin5423=JObject.Parse(on.Ticks[5423-5401].Origin);
            bool explorer76StateCaptured=scoutOrigin5423["Explorer76"]!=null&&scoutOrigin5423["ScoutKnowledge"]!=null&&scoutOrigin5423["Pending"]!=null&&
                ((JArray)scoutOrigin5423["ScoutArmy76"]).Any(a=>(long?)a["Id"]==22&&(string)a["Role"]=="Scout"&&(string)a["Objective"]=="survey:2"&&(long?)a["CreatedTick"]==5423&&((JArray)a["Members"]).Any(m=>(int?)m==76));
            bool endpointOff=Wire.Hash(offFinal)==ControlHash;bool endpointOn=Wire.Hash(onFinal)==ControlHash;
            bool parity=on.Parity;
            var stages=new[]{
                new{Stage="ScoutPlan.5423.Unit76Site2",Status=selectedExplorer76Site2?"Reached":choices.Length==0?"NotReached":"NotQualified",Reason=selectedExplorer76Site2?"Actual ordinary final choice selected explorer 76 for site 2":choices.Length==0?"No actual final-choice callback; no choice was synthesized.":"Actual final-choice callbacks did not select explorer 76 for site 2."},
                new{Stage="Receipt203.Scheduled.5423",Status=receiptScheduled?"Reached":"NotReached",Reason=receiptScheduled?"Actual receipt 203 with required identity and due tick":"No matching scheduled receipt readback."},
                new{Stage="Receipt203.Applied.5453",Status=receiptApplied?"Reached":"NotReached",Reason=receiptApplied?"Same actual receipt identity has ApplicationTick 5453 in the first post-step world snapshot at tick 5454":"No matching applied receipt readback in the tick-5454 snapshot."},
                new{Stage="Site9.Coverage.5468",Status=site9Covered5468?"Reached":"NotReached",Reason=site9Covered5468?"Knowledge reports actual Site 9 coverage at tick 5468":"No actual Site 9 coverage record at 5468."},
                new{Stage="Site9.DiscoveredVsCurrentVisibility.5468",Status=site9VisibilityDistinguished?"Reached":"NotReached",Reason=site9VisibilityDistinguished?"Snapshot discovery and current visibility recorded separately":"One or both actual visibility facts are unavailable."},
                new{Stage="Explorer76.RegistryAndPhysicalState.5423",Status=explorer76StateCaptured?"Reached":"NotReached",Reason=explorer76StateCaptured?"Physical unit, registry membership/clocks, knowledge and pending state captured":"Required state absent from actual snapshot."},
                new{Stage="All300TickHookParity",Status=parity?"Reached":"NotQualified",Reason=parity?"World bytes were compared in-run; facts/checkpoints/receipts/ledgers/views match":"One or more tick comparisons failed."},
                new{Stage="Final5700Control.HookOff",Status=endpointOff?"Reached":"NotQualified",Reason=endpointOff?"Exact final world matches archived control":"Final hook-off hash differs."},
                new{Stage="Final5700Control.HookOn",Status=endpointOn?"Reached":"NotQualified",Reason=endpointOn?"Exact final world matches archived control":"Final hook-on hash differs."}};
            bool qualified=stages.All(s=>s.Status=="Reached");
            var qualification=new{Result=qualified?"QualifiedEpisode":"NotQualified",Seed=7109,Generation=71,Difficulty=AiDifficulty.Fighter,Owners=8,StartTick=5400,EndTick=5700,
                Stages=stages,ReceiptIdentity="71:4:ai-0:121:1",ActionId=203,SelectedExplorer=selectedExplorer76Site2?76:(int?)null,SelectedSite=selectedExplorer76Site2?2:(int?)null,
                HookOffFinalHash=Wire.Hash(offFinal),HookOnFinalHash=Wire.Hash(onFinal),ControlHash=ControlHash,HookOffOnAll300TickParity=parity,
                HookOffTimings=new{CaptureMilliseconds=Milliseconds(off.CaptureMs),ReadsMilliseconds=Milliseconds(off.ReadMs),SerializationMilliseconds=Milliseconds(off.SerializeMs),EvidenceWritingMilliseconds=Milliseconds(off.WriteMs)},
                HookOnTimings=new{CaptureMilliseconds=Milliseconds(on.CaptureMs),ReadsMilliseconds=Milliseconds(on.ReadMs),SerializationMilliseconds=Milliseconds(on.SerializeMs),EvidenceWritingMilliseconds=Milliseconds(on.WriteMs)}};
            File.WriteAllText(Path.Combine(evidence,"episode-qualification.json"),JsonConvert.SerializeObject(qualification,Formatting.Indented));
            var report=new{Result=qualification.Result,DecisionTick=5423,DecisionCallbacks=choices.Select(x=>x.ToString(Formatting.None)).ToArray(),SelectedChoice=choice?.ToString(Formatting.None),
                Receipt203Scheduled=new{WorldCaptureTick=5423,DueTick=5453,ApplicationTick=0,Identity="71:4:ai-0:121:1",Matched=receiptScheduled},
                Receipt203Applied=new{WorldCaptureTick=5454,ApplicationTick=5453,Identity="71:4:ai-0:121:1",Matched=receiptApplied},
                Site9Coverage5468=site9Coverage,Site9DiscoveryAndCurrentVisibility=site9VisibilityDistinguished?new{Discovered=coverage5468["Site9Discovered"],CurrentlyVisible=coverage5468["Site9Visible"]}:null,
                Explorer76OriginState=scoutOrigin5423,HookOffOnWorldFactsReceiptsLedgersViewsEqual=parity,DiagnosticReadsNeutralEveryTick=true,Stages=stages,
                HookOffFinalHash=Wire.Hash(offFinal),HookOnFinalHash=Wire.Hash(onFinal),ControlHash=ControlHash,Timings=new{HookOff=qualification.HookOffTimings,HookOn=qualification.HookOnTimings},
                TimelineOff=Path.Combine(evidence,"timeline-off.jsonl"),TimelineOn=Path.Combine(evidence,"timeline-on.jsonl"),PlanReadbacks=Path.Combine(evidence,"plan-readbacks.jsonl"),OwnerEvents=Path.Combine(evidence,"owner-events.jsonl")};
            File.WriteAllText(Path.Combine(evidence,"summary.json"),JsonConvert.SerializeObject(report,Formatting.Indented));
            TestContext.WriteLine("Q4_SAFE7109_SCOUT_ORIGIN "+JsonConvert.SerializeObject(report));
            Assert.AreEqual(300,off.Ticks.Length);Assert.AreEqual(300,on.Ticks.Length);Assert.IsNotEmpty(on.Plan,"actual ordinary planner readbacks");
            Assert.IsTrue(selectedExplorer76Site2,"tick 5423 ordinary Plan must identify the actual unit 76/site 2 choice");
            Assert.IsTrue(receiptScheduled,"actual action 203 scheduled receipt identity");Assert.IsTrue(receiptApplied,"same receipt applied at tick 5453");
            Assert.IsTrue(site9Covered5468,"actual Site 9 coverage tick");Assert.IsTrue(site9VisibilityDistinguished,"Site 9 discovery and current visibility must be reported separately");
            Assert.IsTrue(explorer76StateCaptured,"Explorer 76 physical/registry/knowledge/pending state");Assert.IsTrue(parity,"all 300 world/fact/receipt/ledger/view snapshots match");
            EqualBytes(control,offFinal,"hook-off final 5700 vs archived control");EqualBytes(control,onFinal,"hook-on final 5700 vs archived control");
        }
    }
}

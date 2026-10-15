using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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
    public sealed class NativeAiCurrentExpansionDecisionDiagnosticTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const string Archive="docs/native-ai/o6/current-natural-20261012-source-compiled-raw.zip";
        private const string ArchiveHash="8e4f56446e3eab63dbc935b15493fa6b20c160e9c50ed026dcaa4fe8c3dac081";
        private static string Root(){var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static object Get(object x,string name){var f=x.GetType().GetField(name,F);if(f!=null)return f.GetValue(x);var p=x.GetType().GetProperty(name,F);Assert.NotNull(p,name);return p.GetValue(x);}
        private static void Advance(PlayableAuthorityTick a,UnityHostRouteService host){for(int i=0;i<512;i++){if(a.TryAdvance())return;host.Service(a,64);}Assert.Fail("ordinary typed-route barrier timed out at "+a.Tick);}
        private static void AssertBytesEqual(byte[] expected,byte[] actual,string message)
        {if(expected.Length!=actual.Length)Assert.Fail(message+" length "+expected.Length+" != "+actual.Length);for(int i=0;i<expected.Length;i++)if(expected[i]!=actual[i])Assert.Fail(message+" first differing byte "+i+" ("+expected[i]+" != "+actual[i]+")");}
        private static byte[] ReadEntry(string path,string entry){using(var z=ZipFile.OpenRead(path))using(var s=z.GetEntry(entry).Open())using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}}
        private sealed class Run { internal byte[][] World;internal string[] Decisions;internal string[] Views;internal string[] Readbacks;internal string[] RouteRows;internal string[] RequestRows;internal string[] GuardRows;internal string[] SafetyRows;internal string[] AdmissionRows;internal string[] ArbiterRows;internal int HookRows;internal string[] FinanceRows;internal string[] FundingRows; }

        [TestCase("GreedyMine",1,9000,"natural/7109-checkpoint-9000.world","9deb7309b01581805902c485e0c74cd5eed0531a8de5045bfbc2ab2e3100214d")]
        [TestCase("GreedySafe",4,600,"natural/7109-checkpoint-600.world","0718986f34eb23ae750516e53560066a51b54f2a2a1cd1e661a0f3fc3779cd3d")]
        public void CurrentNaturalCheckpoint_300OrdinaryTicks_HookIsByteAndDecisionNeutral(string opening,int ownerIndex,int startTick,string entry,string inputHash)
        {
            var root=Root();var archive=Path.Combine(root,Archive);Assert.AreEqual(ArchiveHash,Wire.Hash(File.ReadAllBytes(archive)),"immutable current natural source packet");
            var input=ReadEntry(archive,entry);Assert.AreEqual(inputHash,Wire.Hash(input),"original checkpoint bytes");var seed=7109;var owner="ai-"+ownerIndex;
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,seed,participants);
            Assert.AreEqual(500,profile.StartingCredits);Assert.AreEqual(8,config.Roster.Count);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            Run Execute(bool enabled,Run expected=null)
            {
                var authority=PlayableAuthorityTick.RestoreBytes(input,config);var worlds=new List<byte[]>();var decisions=new List<string>();var observedDecisions=new List<string>();var views=new List<string>();var rows=new List<string>();var routeRows=new List<string>();var requestRows=new List<string>();var guardRows=new List<string>();var safetyRows=new List<string>();var admissionRows=new List<string>();var arbiterRows=new List<string>();
                try
                {
                    Assert.AreEqual(startTick,authority.Tick);CollectionAssert.AreEqual(input,authority.CaptureBytes(),"exact restore before advancing");
                    var initial=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==owner);Assert.AreEqual(seed,initial.Seed);Assert.AreEqual(71,initial.Generation);
                    Assert.AreEqual(opening,initial.Opening.Opening.ToString(),"actual restored opening binding");
                    PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=enabled?owner:null;
                    PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests=enabled?owner:null;
                    PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=enabled?new Action<PlayableAiProposalAdmissionReadback>(r=>admissionRows.Add(JsonConvert.SerializeObject(new{r.OwnerId,r.Tick,r.Policy,
                        Action=r.Action==null?null:new{r.Action.Kind,r.Action.SiteId,Actors=r.Action.CopyEntityIds(),r.Action.Target},r.Stage,r.Reason}))):null;
                    AiDecisionArbiter.SelectionReadbackOwnerFilterForTests=enabled?owner:null;
                    AiDecisionArbiter.SelectionReadbackForTests=enabled?(Action<AiArbiterSelectionReadback>)(r=>arbiterRows.Add(JsonConvert.SerializeObject(new{r.OwnerId,r.Ordered,Bands=r.Bands.OrderBy(x=>x.Key).ToArray()}))):null;
                    AiExpansionPlannerGuardObserverForTests.OwnerFilter=enabled?owner:null;
                    AiExpansionPlannerGuardObserverForTests.Observer=enabled?(Action<string,long,int,int,string,bool>)((who,tick,site,actor,stage,passed)=>guardRows.Add(JsonConvert.SerializeObject(new{Owner=who,Tick=tick,Site=site,Actor=actor,Stage=stage,Result=passed?"EvaluatedPass":"EvaluatedBlock"}))):null;
                    AiExpansionPlannerGuardObserverForTests.SafetyObserver=enabled?(Action<string,long,int,int,int,NavPoint,double,double,string>)((who,tick,site,actor,threat,point,distance,range,kind)=>safetyRows.Add(JsonConvert.SerializeObject(new{Owner=who,Tick=tick,Site=site,Actor=actor,ThreatId=threat,Kind=kind,Point=point,Distance=distance,Range=range}))):null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=enabled?new Action<CurrentExpansionDecisionReadback>(c=>
                    {
                        var o=c.Observation;var intent=c.ExpansionIntent;
                        rows.Add(JsonConvert.SerializeObject(new{Tick=o.Tick,Owner=o.OwnerId,Generation=o.Generation,Sequence=o.SnapshotSequence,Seed=o.Seed,
                            Profile=o.ProfileId,ProfileRevision=o.ProfileRevision,Credits=o.Credits,Population=o.Population,
                            Ledger=new{c.Ledger.Liquid,c.Ledger.SafetyReserve,c.Ledger.PaidTotal,c.Ledger.RepairPaidTotal,
                                Reserved=c.Ledger.Reserved.Select(e=>new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Action=e.Action.Kind,Site=e.Action.SiteId,Actors=e.Action.EntityIds.ToArray()}).ToArray(),
                                Unpaid=c.Ledger.Unpaid.Select(e=>new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Action=e.Action.Kind,Site=e.Action.SiteId,Actors=e.Action.EntityIds.ToArray()}).ToArray()},
                            LedgerStages=new{BeforeFinancialDetach=new{c.BeforeFinanceDetach.Liquid,c.BeforeFinanceDetach.SafetyReserve,c.BeforeFinanceDetach.Reserved,c.BeforeFinanceDetach.Unpaid},
                                AfterTechnologyAllocation=new{c.AfterTechnologyAllocation.Liquid,c.AfterTechnologyAllocation.SafetyReserve,c.AfterTechnologyAllocation.Reserved,c.AfterTechnologyAllocation.Unpaid},
                                AfterFinancialAllocation=new{c.AfterFinancialAllocation.Liquid,c.AfterFinancialAllocation.SafetyReserve,c.AfterFinancialAllocation.Reserved,c.AfterFinancialAllocation.Unpaid}},
                            ProtectedFunds=c.ProtectedFunds,c.FinanceSite,c.FinanceReady,c.Threatened,c.UrgentRepair,c.AllowsExpansion,
                            ThreatIds=c.ThreatIds,c.MissingScout,c.EssentialScale,c.ScalingReason,c.ScalingStartup,c.ArmyActors,c.PendingActors,c.ExpansionState,
                            Explorers=o.Entities.Where(e=>e.Owner==o.Owner&&e.Kind==PlayableEntityKind.Explorer).Select(e=>new{e.Id,e.Health,e.Moving,e.Position,
                                CurrentOrder=e.CurrentOrder==null?null:new{Kind=e.CurrentOrder.Kind,e.CurrentOrder.TargetId,e.CurrentOrder.IssuedTick},
                                OrderStamp=e.OrderStamp==null?null:new{e.OrderStamp.ActionId,e.OrderStamp.Generation,e.OrderStamp.Sequence,e.OrderStamp.Tick,e.OrderStamp.Source},e.NavigationOutcome}).ToArray(),
                            ExpansionAction=c.ExpansionAction==null?null:new{c.ExpansionAction.Kind,c.ExpansionAction.SiteId,Actors=c.ExpansionAction.CopyEntityIds(),c.ExpansionAction.Target},
                            ExpansionIntent=intent==null?null:new{intent.Id,intent.Policy,intent.Credits,intent.StartupFund,intent.Priority,intent.ReservationId,intent.ArmyRejection,
                                Action=intent.Action.Kind,Site=intent.Action.SiteId,Actors=intent.Action.CopyEntityIds()},
                            ActualProposals=c.Proposals.Select(p=>new{p.Id,p.Policy,p.Credits,p.StartupFund,p.Priority,p.ReservationId,p.ArmyRejection,Action=p.Action.Kind,Site=p.Action.SiteId,Actors=p.Action.CopyEntityIds()}).ToArray(),
                            Selected=c.Selected,ArbiterRejections=c.Rejections,RouteProofs=o.RouteProofs.Where(r=>r.Kind==PlayableRouteTargetKind.PublicObjective).Select(r=>new{r.UnitId,r.TargetId,r.Generation,r.Tick,r.GeometryRevision,r.Origin,r.Target,r.Goal,Path=r.Path.ToArray()}).ToArray(),
                            PublicObjectives=o.PublicScoutObjectives.Select(g=>new{g.SiteId,g.Role,g.Reachable,g.Approach}).ToArray(),
                            VisibleHostiles=o.Entities.Where(e=>o.IsHostile(e.Owner)).Select(e=>new{e.Id,Owner=e.Owner,Kind=e.Kind,Position=e.Position,Health=e.Health}).ToArray(),
                            ObserverMode="Actual sequential guards, safety branch, proposal admission, route request/projection, ledger stages, and arbiter ordering are captured in-place; no detached planner/solver/guard re-evaluation."}));
                    }):null;
                    var domain=Get(authority,"domain");var domainType=domain.GetType();var routeObserverField=domainType.GetField("RouteProjectionReadbackForTests",F);
                    var routeOwnerField=domainType.GetField("RouteProjectionOwnerFilterForTests",F);var routeEnabledField=domainType.GetField("CaptureCurrentExpansionRoutesForTests",F);
                    var routeRequestField=domainType.GetField("RouteRequestsReadbackForTests",F);
                    Assert.NotNull(routeObserverField,"test-only actual route-projection observer exists");Assert.NotNull(routeRequestField,"test-only actual route-request observer exists");
                    routeOwnerField.SetValue(domain,enabled?owner:null);routeEnabledField.SetValue(domain,enabled);
                    routeObserverField.SetValue(domain,enabled?(object)new Action<PlayableRouteProjectionReadback>(r=>
                    {
                        var candidates=r.Candidates.Select(c=>new{c.Index,c.Candidate,c.Visible,c.Outcome,c.Path,c.FailedSegmentIndex,c.FailedFrom,c.FailedTo,c.AuthoritativeSegmentFree,c.VisibleSegment}).ToArray();
                        routeRows.Add(JsonConvert.SerializeObject(new { OwnerId=r.OwnerId, Owner=r.Owner, UnitId=r.UnitId, Kind=r.Kind, TargetId=r.TargetId, Generation=r.Generation, Tick=r.Tick, Origin=r.Origin, Radius=r.Radius, RequestedTarget=r.RequestedTarget, Outcome=r.Outcome, BindingRejection=r.BindingRejection, ResolvedTarget=r.ResolvedTarget, OriginVisible=r.OriginVisible, TargetVisible=r.TargetVisible, ObservedGeometryRevision=r.ObservedGeometryRevision, ObservedObstacleCount=r.ObservedObstacleCount, CandidateCount=r.CandidateCount, Candidates=candidates }));
                    }):null);
                    routeRequestField.SetValue(domain,enabled?(object)new Action<string,long,PlayableRouteRequest[]>((who,tick,requests)=>requestRows.Add(JsonConvert.SerializeObject(new{Owner=who,Tick=tick,Requests=requests.Select(q=>new{q.UnitId,q.Kind,q.TargetId,q.Generation,q.Tick,q.Origin,q.Radius,q.Target}).ToArray()}))):null);
                    using(var host=new UnityHostRouteService())for(int i=0;i<300;i++)
                    {
                        Advance(authority,host);Assert.AreEqual(startTick+i+1,authority.Tick);var world=authority.CaptureBytes();
                        var cp=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==owner);var e=cp.EconomyDecision;
                        var bank=authority.CaptureDiagnosticBudgets().Single(x=>x.OwnerId==owner);var ownerView=authority.ParticipantView(owner);
                        var decision=JsonConvert.SerializeObject(new{authority.Tick,cp.DecisionTick,cp.LastPolicy,cp.LastActionKind,cp.PendingActionId,
                            Economy=e==null?null:new{e.Tick,e.Ordinal,e.Liquid,e.Available,e.Reserved,e.Unpaid,e.SafetyReserve,e.Reason,e.Blockers,
                                Candidates=e.Candidates.Select(x=>new{x.Policy,x.Kind,x.EntityIds,x.SiteId,x.SlotId,x.BuildingKind,x.UnitKind,x.Credits,x.Selected,x.Reason}).ToArray()},
                            ExpansionReceipts=cp.Records.Where(r=>r.Policy=="expansion").Select(r=>new{r.ActionId,r.Kind,r.ReceiptIdentity,r.DueTick,r.ApplicationTick,Status=r.Status.ToString(),RuntimeStatus=r.RuntimeStatus?.ToString(),r.Message}).ToArray(),
                            PaidBuildAt=bank.Paid.Where(p=>p.Action.Kind==PlayableCommandKind.BuildAt).Select(p=>new{p.Id,p.Amount,Site=p.Action.SiteId,Building=p.Action.BuildingKind,Actors=p.Action.CopyEntityIds(),p.Receipt}).ToArray(),
                            Site22=ownerView.Buildings.Where(b=>b.SiteId==22).Select(b=>new{b.Id,b.SiteId,b.Kind,b.Phase,b.Health,IsOwn=b.Owner==ownerView.Owner}).ToArray()});
                        observedDecisions.Add(decision);
                        var view=JsonConvert.SerializeObject(Enumerable.Range(0,8).Select(i=>authority.ParticipantView("ai-"+i)));
                        if(expected==null){worlds.Add(world);decisions.Add(decision);views.Add(view);}
                        else{AssertBytesEqual(expected.World[i],world,"complete world and AI bytes at tick "+(startTick+i+1));Assert.AreEqual(expected.Decisions[i],decision,"ordinary decisions/receipts at tick "+(startTick+i+1));Assert.AreEqual(expected.Views[i],view,"all participant views at tick "+(startTick+i+1));}
                    }
                    Assert.AreEqual(startTick+300,authority.Tick);return new Run{World=worlds.ToArray(),Decisions=observedDecisions.ToArray(),Views=views.ToArray(),Readbacks=rows.ToArray(),RouteRows=routeRows.ToArray(),RequestRows=requestRows.ToArray(),GuardRows=guardRows.ToArray(),SafetyRows=safetyRows.ToArray(),AdmissionRows=admissionRows.ToArray(),ArbiterRows=arbiterRows.ToArray(),HookRows=rows.Count};
                }
                finally{PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=null;PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests=null;AiDecisionArbiter.SelectionReadbackForTests=null;AiDecisionArbiter.SelectionReadbackOwnerFilterForTests=null;AiExpansionPlannerGuardObserverForTests.Observer=null;AiExpansionPlannerGuardObserverForTests.SafetyObserver=null;AiExpansionPlannerGuardObserverForTests.OwnerFilter=null;var domain=Get(authority,"domain");var dt=domain.GetType();dt.GetField("RouteProjectionReadbackForTests",F)?.SetValue(domain,null);dt.GetField("RouteRequestsReadbackForTests",F)?.SetValue(domain,null);dt.GetField("RouteProjectionOwnerFilterForTests",F)?.SetValue(domain,null);dt.GetField("CaptureCurrentExpansionRoutesForTests",F)?.SetValue(domain,false);authority.Stop();}
            }
            var off=Execute(false);Assert.AreEqual(300,off.World.Length);var on=Execute(true,off);Assert.Greater(on.HookRows,0,"actual ordinary decisions were observed");
            if(opening=="GreedySafe")
            {
                var rows=on.Decisions.Select(JsonConvert.DeserializeObject<JObject>).ToArray();
                var applied=rows.SelectMany(r=>(JArray)r["ExpansionReceipts"]).Where(r=>(int?)r["Kind"]==(int)PlayableCommandKind.BuildAt&&
                    (string)r["Status"]==PlayableAiDeliveryStatus.Applied.ToString()&&(string)r["RuntimeStatus"]==PlayableCommandStatus.Applied.ToString()&&
                    (long?)r["ApplicationTick"]>startTick&&(long?)r["ApplicationTick"]<=startTick+300).ToArray();
                Assert.IsNotEmpty(applied,"positive control must observe a new ordinary BuildAt with actual Applied runtime receipt");
                int mineCost=TerritoryRules.Cost(profile,PlayableBuildingKind.Mine);
                Assert.AreEqual(200,mineCost,"immutable source profile Mine cost; tick 630 is the Move decision, not a price");
                Assert.IsTrue(rows.SelectMany(r=>(JArray)r["PaidBuildAt"]).Any(p=>(int?)p["Site"]==22&&(int?)p["Building"]==(int)PlayableBuildingKind.Mine&&
                    (int?)p["Amount"]==mineCost&&p["Receipt"]!=null&&p["Receipt"].Type!=JTokenType.Null&&
                    (string)p["Receipt"]["OwnerId"]==owner&&(long?)p["Receipt"]["Generation"]==71&&
                    applied.Any(a=>JToken.DeepEquals(a["ReceiptIdentity"],p["Receipt"]))),
                    "positive control must bind site 22's full actual Mine payment to the same new Applied receipt");
                Assert.IsTrue(rows.SelectMany(r=>(JArray)r["Site22"]).Any(b=>(int?)b["SiteId"]==22&&(int?)b["Kind"]==(int)PlayableBuildingKind.Mine&&
                    (bool?)b["IsOwn"]==true&&(double?)b["Health"]>0),"positive control must observe its own living Mine on site 22");
            }
            Assert.IsEmpty(off.Readbacks);Assert.Greater(on.HookRows,0,"ordinary target-owner decisions were observed");
            Assert.IsTrue(on.Readbacks.All(s=>JsonConvert.DeserializeObject<dynamic>(s).Generation==71));
            var actualGuardEvents=on.GuardRows.Select(JsonConvert.DeserializeObject<JObject>).ToArray();
            var candidateStages=new[]{"actor-owner","actor-kind","actor-alive","actor-idle-moving","actor-current-order","actor-tactical-owner","actor-pending-action","public-objective","typed-route-proof","visible-threat-safety","failed-site-suppression"};
            var guardNotEvaluatedMatrix=actualGuardEvents.Where(e=>(int)e["Actor"]>0).GroupBy(e=>new{Tick=(long)e["Tick"],Site=(int)e["Site"],Actor=(int)e["Actor"]}).Select(g=>
            {
                var evaluated=g.GroupBy(e=>(string)e["Stage"]).ToDictionary(k=>k.Key,k=>(string)k.Last()["Result"]);
                return new{g.Key.Tick,g.Key.Site,g.Key.Actor,Stages=candidateStages.Select(stage=>new{Stage=stage,Result=evaluated.TryGetValue(stage,out var result)?result:"NotEvaluated"}).ToArray()};
            }).ToArray();
            Assert.Greater(on.RequestRows.Length,0,"ordinary route-request stage was observed");
            TestContext.WriteLine("CURRENT_EXPANSION_DECISION "+JsonConvert.SerializeObject(new{Opening=opening,Seed=seed,Owner=owner,StartTick=startTick,EndTick=startTick+300,Input=entry,InputHash=inputHash,
                HookRows=on.HookRows,FullWorldBytesEqual=true,AllParticipantViewsEqual=true,OrdinaryDecisionsAndReceiptsEqual=true,Readbacks=on.Readbacks,
                ActualRouteRequests=on.RequestRows,ActualRouteProjectionResults=on.RouteRows,ActualExpansionGuardBranches=on.GuardRows,GuardOutcomesWithNotEvaluated=guardNotEvaluatedMatrix,
                ActualProposalAdmissionBranches=on.AdmissionRows,ActualSafetyBlockers=on.SafetyRows,ActualArbiterOrderAndBands=on.ArbiterRows}));
        }

        [Test]
        public void Actual7108GreedyMine_4200To4500_ExactDecisionCausalReplay()
        {
            const string opening="GreedyMine",entry="natural/7108-checkpoint-4200.world",inputHash="d0baf66d895627646c1c390aa7a5aa7ed753fe500b2138c560f11bd0e79ecae3";
            const int ownerIndex=4,startTick=4200;
            var root=Root();var archive=Path.Combine(root,Archive);Assert.AreEqual(ArchiveHash,Wire.Hash(File.ReadAllBytes(archive)),"immutable current natural source packet");
            var input=ReadEntry(archive,entry);Assert.AreEqual(inputHash,Wire.Hash(input),"original checkpoint bytes");var seed=7108;var owner="ai-"+ownerIndex;
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,seed,participants);
            Assert.AreEqual(500,profile.StartingCredits);Assert.AreEqual(8,config.Roster.Count);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            Run Execute(bool enabled,Run expected=null)
            {
                var authority=PlayableAuthorityTick.RestoreBytes(input,config);var worlds=new List<byte[]>();var decisions=new List<string>();var observedDecisions=new List<string>();var views=new List<string>();var rows=new List<string>();var routeRows=new List<string>();var requestRows=new List<string>();var guardRows=new List<string>();var safetyRows=new List<string>();var admissionRows=new List<string>();var arbiterRows=new List<string>();var financeRows=new List<string>();var fundingRows=new List<string>();
                try
                {
                    Assert.AreEqual(startTick,authority.Tick);CollectionAssert.AreEqual(input,authority.CaptureBytes(),"exact restore before advancing");
                    var initial=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==owner);Assert.AreEqual(seed,initial.Seed);Assert.AreEqual(71,initial.Generation);
                    Assert.AreEqual(opening,initial.Opening.Opening.ToString(),"actual restored opening binding");
                    PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=enabled?owner:null;
                    PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=enabled?owner:null;
                    PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=enabled?new Action<string>(r=>fundingRows.Add(r)):null;
                    PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests=enabled?owner:null;
                    PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=enabled?new Action<PlayableAiProposalAdmissionReadback>(r=>admissionRows.Add(JsonConvert.SerializeObject(new{r.OwnerId,r.Tick,r.Policy,
                        Action=r.Action==null?null:new{r.Action.Kind,r.Action.SiteId,Actors=r.Action.CopyEntityIds(),r.Action.Target},r.Stage,r.Reason}))):null;
                    AiDecisionArbiter.SelectionReadbackOwnerFilterForTests=enabled?owner:null;
                    AiDecisionArbiter.SelectionReadbackForTests=enabled?(Action<AiArbiterSelectionReadback>)(r=>arbiterRows.Add(JsonConvert.SerializeObject(new{r.OwnerId,r.Ordered,Bands=r.Bands.OrderBy(x=>x.Key).ToArray()}))):null;
                    AiExpansionFinancial.ActualLifecycleReadbackForTests=enabled?(Action<string,long,string,int,AiBudgetEntry,AiBudgetEntry,AiBudgetState>)((who,tick,stage,site,previous,current,state)=>{if(who==owner)financeRows.Add(JsonConvert.SerializeObject(new{Owner=who,Tick=tick,Stage=stage,Site=site,Previous=previous,Current=current,State=state}));}):null;
                    AiExpansionPlannerGuardObserverForTests.OwnerFilter=enabled?owner:null;
                    AiExpansionPlannerGuardObserverForTests.Observer=enabled?(Action<string,long,int,int,string,bool>)((who,tick,site,actor,stage,passed)=>guardRows.Add(JsonConvert.SerializeObject(new{Owner=who,Tick=tick,Site=site,Actor=actor,Stage=stage,Result=passed?"EvaluatedPass":"EvaluatedBlock"}))):null;
                    AiExpansionPlannerGuardObserverForTests.SafetyObserver=enabled?(Action<string,long,int,int,int,NavPoint,double,double,string>)((who,tick,site,actor,threat,point,distance,range,kind)=>safetyRows.Add(JsonConvert.SerializeObject(new{Owner=who,Tick=tick,Site=site,Actor=actor,ThreatId=threat,Kind=kind,Point=point,Distance=distance,Range=range}))):null;
                    PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=enabled?new Action<CurrentExpansionDecisionReadback>(c=>
                    {
                        var o=c.Observation;var intent=c.ExpansionIntent;
                        rows.Add(JsonConvert.SerializeObject(new{Tick=o.Tick,Owner=o.OwnerId,Generation=o.Generation,Sequence=o.SnapshotSequence,Seed=o.Seed,
                            Profile=o.ProfileId,ProfileRevision=o.ProfileRevision,Credits=o.Credits,Population=o.Population,
                            Ledger=new{c.Ledger.Liquid,c.Ledger.SafetyReserve,c.Ledger.PaidTotal,c.Ledger.RepairPaidTotal,
                                Reserved=c.Ledger.Reserved.Select(e=>new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Action=e.Action.Kind,Site=e.Action.SiteId,Actors=e.Action.EntityIds.ToArray()}).ToArray(),
                                Unpaid=c.Ledger.Unpaid.Select(e=>new{e.Id,e.Purpose,e.Amount,e.CreatedTick,e.ExpiresTick,Action=e.Action.Kind,Site=e.Action.SiteId,Actors=e.Action.EntityIds.ToArray()}).ToArray()},
                            LedgerStages=new{BeforeFinancialDetach=new{c.BeforeFinanceDetach.Liquid,c.BeforeFinanceDetach.SafetyReserve,c.BeforeFinanceDetach.Reserved,c.BeforeFinanceDetach.Unpaid},
                                AfterTechnologyAllocation=new{c.AfterTechnologyAllocation.Liquid,c.AfterTechnologyAllocation.SafetyReserve,c.AfterTechnologyAllocation.Reserved,c.AfterTechnologyAllocation.Unpaid},
                                AfterFinancialAllocation=new{c.AfterFinancialAllocation.Liquid,c.AfterFinancialAllocation.SafetyReserve,c.AfterFinancialAllocation.Reserved,c.AfterFinancialAllocation.Unpaid}},
                            ProtectedFunds=c.ProtectedFunds,c.FinanceSite,c.FinanceReady,c.Threatened,c.UrgentRepair,c.AllowsExpansion,
                            ThreatIds=c.ThreatIds,c.MissingScout,c.EssentialScale,c.ScalingReason,c.ScalingStartup,c.ArmyActors,c.PendingActors,c.ExpansionState,
                            Explorers=o.Entities.Where(e=>e.Owner==o.Owner&&e.Kind==PlayableEntityKind.Explorer).Select(e=>new{e.Id,e.Health,e.Moving,e.Position,
                                CurrentOrder=e.CurrentOrder==null?null:new{Kind=e.CurrentOrder.Kind,e.CurrentOrder.TargetId,e.CurrentOrder.IssuedTick},
                                OrderStamp=e.OrderStamp==null?null:new{e.OrderStamp.ActionId,e.OrderStamp.Generation,e.OrderStamp.Sequence,e.OrderStamp.Tick,e.OrderStamp.Source},e.NavigationOutcome}).ToArray(),
                            ExpansionAction=c.ExpansionAction==null?null:new{c.ExpansionAction.Kind,c.ExpansionAction.SiteId,Actors=c.ExpansionAction.CopyEntityIds(),c.ExpansionAction.Target},
                            ExpansionIntent=intent==null?null:new{intent.Id,intent.Policy,intent.Credits,intent.StartupFund,intent.Priority,intent.ReservationId,intent.ArmyRejection,
                                Action=intent.Action.Kind,Site=intent.Action.SiteId,Actors=intent.Action.CopyEntityIds()},
                            ActualProposals=c.Proposals.Select(p=>new{p.Id,p.Policy,p.Credits,p.StartupFund,p.Priority,p.ReservationId,p.ArmyRejection,Action=p.Action.Kind,Site=p.Action.SiteId,Actors=p.Action.CopyEntityIds()}).ToArray(),
                            Selected=c.Selected,ArbiterRejections=c.Rejections,RouteProofs=o.RouteProofs.Where(r=>r.Kind==PlayableRouteTargetKind.PublicObjective).Select(r=>new{r.UnitId,r.TargetId,r.Generation,r.Tick,r.GeometryRevision,r.Origin,r.Target,r.Goal,Path=r.Path.ToArray()}).ToArray(),
                            PublicObjectives=o.PublicScoutObjectives.Select(g=>new{g.SiteId,g.Role,g.Reachable,g.Approach}).ToArray(),
                            VisibleHostiles=o.Entities.Where(e=>o.IsHostile(e.Owner)).Select(e=>new{e.Id,Owner=e.Owner,Kind=e.Kind,Position=e.Position,Health=e.Health}).ToArray(),
                            ObserverMode="Actual sequential guards, safety branch, proposal admission, route request/projection, ledger stages, and arbiter ordering are captured in-place; no detached planner/solver/guard re-evaluation."}));
                    }):null;
                    var domain=Get(authority,"domain");var domainType=domain.GetType();var routeObserverField=domainType.GetField("RouteProjectionReadbackForTests",F);
                    var routeOwnerField=domainType.GetField("RouteProjectionOwnerFilterForTests",F);var routeEnabledField=domainType.GetField("CaptureCurrentExpansionRoutesForTests",F);
                    var routeRequestField=domainType.GetField("RouteRequestsReadbackForTests",F);
                    Assert.NotNull(routeObserverField,"test-only actual route-projection observer exists");Assert.NotNull(routeRequestField,"test-only actual route-request observer exists");
                    routeOwnerField.SetValue(domain,enabled?owner:null);routeEnabledField.SetValue(domain,enabled);
                    routeObserverField.SetValue(domain,enabled?(object)new Action<PlayableRouteProjectionReadback>(r=>
                    {
                        var candidates=r.Candidates.Select(c=>new{c.Index,c.Candidate,c.Visible,c.Outcome,c.Path,c.FailedSegmentIndex,c.FailedFrom,c.FailedTo,c.AuthoritativeSegmentFree,c.VisibleSegment}).ToArray();
                        routeRows.Add(JsonConvert.SerializeObject(new { OwnerId=r.OwnerId, Owner=r.Owner, UnitId=r.UnitId, Kind=r.Kind, TargetId=r.TargetId, Generation=r.Generation, Tick=r.Tick, Origin=r.Origin, Radius=r.Radius, RequestedTarget=r.RequestedTarget, Outcome=r.Outcome, BindingRejection=r.BindingRejection, ResolvedTarget=r.ResolvedTarget, OriginVisible=r.OriginVisible, TargetVisible=r.TargetVisible, ObservedGeometryRevision=r.ObservedGeometryRevision, ObservedObstacleCount=r.ObservedObstacleCount, CandidateCount=r.CandidateCount, Candidates=candidates }));
                    }):null);
                    routeRequestField.SetValue(domain,enabled?(object)new Action<string,long,PlayableRouteRequest[]>((who,tick,requests)=>requestRows.Add(JsonConvert.SerializeObject(new{Owner=who,Tick=tick,Requests=requests.Select(q=>new{q.UnitId,q.Kind,q.TargetId,q.Generation,q.Tick,q.Origin,q.Radius,q.Target}).ToArray()}))):null);
                    using(var host=new UnityHostRouteService())for(int i=0;i<300;i++)
                    {
                        Advance(authority,host);Assert.AreEqual(startTick+i+1,authority.Tick);var world=authority.CaptureBytes();var allViewsBefore=JsonConvert.SerializeObject(Enumerable.Range(0,8).Select(j=>authority.ParticipantView("ai-"+j)));var allOwnerReceiptsBefore=JsonConvert.SerializeObject(authority.CaptureDiagnosticCheckpoints().Select(c=>new{c.OwnerId,Records=c.Records.ToArray()}).ToArray());
                        var cp=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==owner);var e=cp.EconomyDecision;
                        var bank=authority.CaptureDiagnosticBudgets().Single(x=>x.OwnerId==owner);var ownerView=authority.ParticipantView(owner);
                        var decision=JsonConvert.SerializeObject(new{authority.Tick,cp.DecisionTick,cp.LastPolicy,cp.LastActionKind,cp.PendingActionId,
                            Economy=e==null?null:new{e.Tick,e.Ordinal,e.Liquid,e.Available,e.Reserved,e.Unpaid,e.SafetyReserve,e.Reason,e.Blockers,
                                Candidates=e.Candidates.Select(x=>new{x.Policy,x.Kind,x.EntityIds,x.SiteId,x.SlotId,x.BuildingKind,x.UnitKind,x.Credits,x.Selected,x.Reason}).ToArray()},
                            FullOwnerReceipts=cp.Records.ToArray(),AllOwnerReceipts=authority.CaptureDiagnosticCheckpoints().Select(c=>new{c.OwnerId,Records=c.Records.ToArray()}).ToArray(),
                            EveryTickBudget=new{bank.Liquid,bank.SafetyReserve,bank.PaidTotal,bank.RepairPaidTotal,Reserved=bank.Reserved.ToArray(),Unpaid=bank.Unpaid.ToArray(),ScoutPaidClaims=bank.Paid.Where(p=>p.Purpose=="scout").ToArray()},
                            PaidBuildAt=bank.Paid.Where(p=>p.Action.Kind==PlayableCommandKind.BuildAt).Select(p=>new{p.Id,p.Amount,Site=p.Action.SiteId,Building=p.Action.BuildingKind,Actors=p.Action.CopyEntityIds(),p.Receipt}).ToArray(),
                            Site22=ownerView.Buildings.Where(b=>b.SiteId==22).Select(b=>new{b.Id,b.SiteId,b.Kind,b.Phase,b.Health,IsOwn=b.Owner==ownerView.Owner}).ToArray()});
                        observedDecisions.Add(decision);
                        var view=JsonConvert.SerializeObject(Enumerable.Range(0,8).Select(j=>authority.ParticipantView("ai-"+j)));
                        AssertBytesEqual(world,authority.CaptureBytes(),"diagnostic reads preserve complete world and AI bytes at tick "+authority.Tick);Assert.AreEqual(allViewsBefore,view,"all eight complete participant views survive diagnostic reads at tick "+authority.Tick);Assert.AreEqual(allOwnerReceiptsBefore,JsonConvert.SerializeObject(authority.CaptureDiagnosticCheckpoints().Select(c=>new{c.OwnerId,Records=c.Records.ToArray()}).ToArray()),"all owners complete receipt records survive diagnostic reads at tick "+authority.Tick);
                        if(expected==null){worlds.Add(world);decisions.Add(decision);views.Add(view);}
                        else{AssertBytesEqual(expected.World[i],world,"complete world and AI bytes at tick "+(startTick+i+1));Assert.AreEqual(expected.Decisions[i],decision,"ordinary decisions/receipts at tick "+(startTick+i+1));Assert.AreEqual(expected.Views[i],view,"all participant views at tick "+(startTick+i+1));}
                    }
                    Assert.AreEqual(startTick+300,authority.Tick);return new Run{World=worlds.ToArray(),Decisions=observedDecisions.ToArray(),Views=views.ToArray(),Readbacks=rows.ToArray(),RouteRows=routeRows.ToArray(),RequestRows=requestRows.ToArray(),GuardRows=guardRows.ToArray(),SafetyRows=safetyRows.ToArray(),AdmissionRows=admissionRows.ToArray(),ArbiterRows=arbiterRows.ToArray(),HookRows=rows.Count,FinanceRows=financeRows.ToArray(),FundingRows=fundingRows.ToArray()};
                }
                finally{AiExpansionFinancial.ActualLifecycleReadbackForTests=null;PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=null;PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=null;PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests=null;AiDecisionArbiter.SelectionReadbackForTests=null;AiDecisionArbiter.SelectionReadbackOwnerFilterForTests=null;AiExpansionPlannerGuardObserverForTests.Observer=null;AiExpansionPlannerGuardObserverForTests.SafetyObserver=null;AiExpansionPlannerGuardObserverForTests.OwnerFilter=null;var domain=Get(authority,"domain");var dt=domain.GetType();dt.GetField("RouteProjectionReadbackForTests",F)?.SetValue(domain,null);dt.GetField("RouteRequestsReadbackForTests",F)?.SetValue(domain,null);dt.GetField("RouteProjectionOwnerFilterForTests",F)?.SetValue(domain,null);dt.GetField("CaptureCurrentExpansionRoutesForTests",F)?.SetValue(domain,false);authority.Stop();}
            }
            var off=Execute(false);Assert.AreEqual(300,off.World.Length);var on=Execute(true,off);Assert.Greater(on.HookRows,0,"actual ordinary decisions were observed");
            Assert.IsEmpty(off.Readbacks);Assert.Greater(on.HookRows,0,"ordinary target-owner decisions were observed");
            Assert.IsTrue(on.Readbacks.All(s=>JsonConvert.DeserializeObject<dynamic>(s).Generation==71));
            var actualGuardEvents=on.GuardRows.Select(JsonConvert.DeserializeObject<JObject>).ToArray();
            var candidateStages=new[]{"actor-owner","actor-kind","actor-alive","actor-idle-moving","actor-current-order","actor-tactical-owner","actor-pending-action","public-objective","typed-route-proof","visible-threat-safety","failed-site-suppression"};
            var guardNotEvaluatedMatrix=actualGuardEvents.Where(e=>(int)e["Actor"]>0).GroupBy(e=>new{Tick=(long)e["Tick"],Site=(int)e["Site"],Actor=(int)e["Actor"]}).Select(g=>
            {
                var evaluated=g.GroupBy(e=>(string)e["Stage"]).ToDictionary(k=>k.Key,k=>(string)k.Last()["Result"]);
                return new{g.Key.Tick,g.Key.Site,g.Key.Actor,Stages=candidateStages.Select(stage=>new{Stage=stage,Result=evaluated.TryGetValue(stage,out var result)?result:"NotEvaluated"}).ToArray()};
            }).ToArray();
            Assert.Greater(on.RequestRows.Length,0,"ordinary route-request stage was observed");
            var ownerReceiptRows=on.Decisions.Select(JsonConvert.DeserializeObject<JObject>).SelectMany(x=>(JArray)x["FullOwnerReceipts"]).ToArray();
            var scoutDue4220=ownerReceiptRows.Where(x=>(string)x["Policy"]=="scout"&&(long?)x["DueTick"]==4220).ToArray();
            Assert.IsNotEmpty(scoutDue4220,"actual scout terminal receipt for due tick 4220 is retained");
            Assert.IsTrue(scoutDue4220.Any(x=>(int?)x["Status"]==(int)PlayableAiDeliveryStatus.Applied&&(int?)x["RuntimeStatus"]==(int)PlayableCommandStatus.Applied),"scout due tick 4220 has an actual applied terminal receipt");
            var scoutClaimSnapshots=on.Decisions.Select(JsonConvert.DeserializeObject<JObject>).SelectMany(x=>(JArray)x["EveryTickBudget"]["ScoutPaidClaims"]).ToArray();
            Assert.IsTrue(scoutClaimSnapshots.Any(x=>(string)x["Receipt"]["Id"]=="71:4:ai-4:94:1"&&((JArray)x["Claims"]).Any(c=>(string)c=="recipient:51")),"actual scout receipt retains the explorer 51 ownership claim");
            var decision4460=on.Readbacks.Select(JsonConvert.DeserializeObject<JObject>).Single(x=>(long)x["Tick"]==4460);
            Assert.AreEqual("ai-4",(string)decision4460["Owner"]);Assert.AreEqual(15,(int)decision4460["FinanceSite"]);Assert.IsFalse((bool)decision4460["FinanceReady"]);
            var financeAt4460=(JObject)decision4460["Ledger"]["Reserved"].Single(x=>(string)x["Purpose"]==AiExpansionFinancial.Purpose);
            Assert.AreEqual(0,(int)financeAt4460["Amount"]);Assert.AreEqual(4460,(long)financeAt4460["CreatedTick"]);Assert.AreEqual(5810,(long)financeAt4460["ExpiresTick"]);
            TestContext.WriteLine("ACTUAL_7108_GREEDYMINE_4460 "+JsonConvert.SerializeObject(new{Opening=opening,Seed=seed,Owner=owner,StartTick=startTick,EndTick=startTick+300,Input=entry,InputHash=inputHash,
                HookRows=on.HookRows,FullWorldBytesEqual=true,AllParticipantViewsEqual=true,AllOwnerReceiptRecordsEqual=true,OrdinaryDecisionsAndReceiptsEqual=true,Readbacks=on.Readbacks,FullCausalTimeline=on.Decisions,ActualFinancialLifecycle=on.FinanceRows,TechnologyFundingAndArbitration=on.FundingRows,
                ActualRouteRequests=on.RequestRows,ActualRouteProjectionResults=on.RouteRows,ActualExpansionGuardBranches=on.GuardRows,GuardOutcomesWithNotEvaluated=guardNotEvaluatedMatrix,
                ActualProposalAdmissionBranches=on.AdmissionRows,ActualSafetyBlockers=on.SafetyRows,ActualArbiterOrderAndBands=on.ArbiterRows}));
        }
    }
}

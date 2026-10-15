using System;
using System.Collections;
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
    public sealed class NativeAiFinancialAllOpeningsReadbackTests
    {
        private readonly System.Collections.Generic.List<object> rows=new System.Collections.Generic.List<object>();
        private readonly System.Collections.Generic.List<string> eligible=new System.Collections.Generic.List<string>();
        private readonly System.Collections.Generic.List<PlayableAiPreselectionReadback> contexts=new System.Collections.Generic.List<PlayableAiPreselectionReadback>();
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static object Get(object value,string name)=>value.GetType().GetField(name,F).GetValue(value);
        private static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,F).Invoke(value,args);
        private static string Root()
        {var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}

        [OneTimeSetUp] public void CaptureNaturalFinancialCheckpoints()
        {
            var root=Root();var archive=Path.Combine(root,"docs/native-ai/o6/financial-all-openings-source-compiled-raw.zip");
            var output=Path.Combine(root,".local/o6-natural/financial-all-openings-readback.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output,string.Empty);
            var cases=new[]{new{Seed=7109,Tick=600,Advance=30,Owner="ai-4",Entry=".local/o6-natural/7109-checkpoint-600.world",Hash="0718986f34eb23ae750516e53560066a51b54f2a2a1cd1e661a0f3fc3779cd3d"},
                new{Seed=7108,Tick=900,Advance=45,Owner="ai-2",Entry=".local/o6-natural/7108-checkpoint-900.world",Hash="64d03d029c82c0c06ac198044db52334a49163a8816b52da499daaa80933cdb7"}};
            rows.Clear();eligible.Clear();contexts.Clear();
            using(var zip=ZipFile.OpenRead(archive))
            foreach(var c in cases)
            {
                var entry=zip.GetEntry(c.Entry);Assert.NotNull(entry,"immutable checkpoint entry "+c.Entry);byte[] bytes;
                using(var stream=entry.Open())using(var copy=new MemoryStream()){stream.CopyTo(copy);bytes=copy.ToArray();}
                Assert.AreEqual(c.Hash,Wire.Hash(bytes),"immutable archived checkpoint hash");
                var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
                var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
                var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,c.Seed,roster);
                Assert.AreEqual(500,profile.StartingCredits);Assert.AreEqual(c.Seed,config.Seed);Assert.AreEqual(8,config.Roster.Count);
                Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
                var authority=PlayableAuthorityTick.RestoreBytes(bytes,config);var exact=authority.CaptureBytes();
                PlayableAiOwnerCheckpoint checkpoint;PlayableAiPreselectionReadback preselection=null;int hostServiceCalls=0;
                try
                {
                    Assert.AreEqual(c.Tick,authority.Tick);CollectionAssert.AreEqual(bytes,exact,"exact C9 restore before ordinary continuation");
                    PlayableAiOwnerLoop.PreselectionReadbackForTests=x=>{if(x.Observation.OwnerId==c.Owner&&x.Observation.Tick==c.Tick+c.Advance){preselection=x;contexts.Add(x);}};
                    using(var host=new UnityHostRouteService())
                    for(int i=0;i<c.Advance;i++)
                    {
                        bool advanced=false;for(int attempt=0;attempt<512;attempt++){if(authority.TryAdvance()){advanced=true;break;}host.Service(authority,64);hostServiceCalls++;}
                        Assert.True(advanced,"ordinary route barrier at tick "+authority.Tick);Assert.IsNull(authority.Latest.Failure);
                        if(authority.Tick==c.Tick+c.Advance)
                        {
                        }
                    }
                    PlayableAiOwnerLoop.PreselectionReadbackForTests=null;
                    Assert.AreEqual(c.Tick+c.Advance,authority.Tick);checkpoint=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==c.Owner);
                    var scheduler=Get(authority,"scheduler");var owner=((IEnumerable)Get(scheduler,"owners")).Cast<object>().Single(x=>(string)x.GetType().GetProperty("OwnerId",F).GetValue(x)==c.Owner);
                    var pending=((IEnumerable)Get(owner,"pendingItems")).Cast<object>().ToArray();
                    var scheduled=pending.OrderByDescending(x=>(long)Get(x,"ObservationTick")).FirstOrDefault();
                    var scheduledObservation=preselection?.Observation;
                    long decisionTick=preselection?.LastDecisionTick??-1;
                    long decisionInterval=preselection?.DecisionIntervalTicks??-1;
                    long observationDelay=preselection?.ObservationDelayTicks??-1;
                    var trace=((IEnumerable)Get(owner,"records")).Cast<object>().ToArray();
                    var o=scheduledObservation;
                    if(o==null)o=PlayableAiObservation.From(authority.ParticipantView(c.Owner));
                    var budget=(AiBudgetLedger)Get(owner,"budget");var ledger=preselection?.Ledger??budget.Capture();var armies=(AiArmyRegistry)Get(owner,"armies");
                    var expansion=(AiExpansionPlanner)Get(owner,"expansion");var expansionState=expansion.Capture();
                    var finance=Get(owner,"expansionFinancial");var arbiter=(AiDecisionArbiter)Get(owner,"arbiter");
                    int financialSite=preselection?.FinanceSite??(int)Call(finance,"ActiveSite",arbiter);
                    bool financialReady=preselection?.FinanceReady??(bool)Call(finance,"Ready",profile,budget,arbiter,o.Tick);
                    bool AdmissibleNewSite(int id)
                    {var site=scheduledObservation?.Sites.FirstOrDefault(s=>s.Site.Id==id);return site!=null&&!(bool)Call(finance,"IsExpiredFingerprint",site,arbiter);}
                    bool AdmissibleFallbackSite(int id)=>id!=financialSite&&AdmissibleNewSite(id);
                    string capturedIdentity=preselection?.Observation.Identity;int capturedRouteProofs=preselection?.Observation.RouteProofs.Count??0;
                    var worldBeforeDetachedProbe=authority.CaptureBytes();
                    var detachedProbe=preselection==null?null:preselection.ExpansionProbe.Fork().TryPlan(preselection.Observation,profile,AiProfile.Initial,0,preselection.FreshActor,AdmissibleFallbackSite);
                    var worldAfterDetachedProbe=authority.CaptureBytes();
                    CollectionAssert.AreEqual(worldBeforeDetachedProbe,worldAfterDetachedProbe,"offline probe on the detached preselection fork leaves authority state unchanged");
                    Assert.AreEqual(capturedIdentity,preselection?.Observation.Identity,"CaptureBytes boundary does not replace the captured scheduled observation");
                    Assert.AreEqual(capturedRouteProofs,preselection?.Observation.RouteProofs.Count,"CaptureBytes boundary does not invalidate detached typed route proofs");
                    long preselectionAvailable=ledger.Liquid-ledger.SafetyReserve-ledger.Reserved.Sum(e=>(long)e.Amount)-ledger.Unpaid.Sum(e=>(long)e.Amount);
                    var alternative=detachedProbe;
                    if(alternative!=null&&preselection?.CounterfactualAlternative!=null)
                    {Assert.AreEqual(preselection.CounterfactualAlternative.SiteId,alternative.SiteId,"detached replay selects the same alternative site");CollectionAssert.AreEqual(preselection.CounterfactualAlternative.EntityIds,alternative.EntityIds,"detached replay selects the same fresh actor");}
                    var currentGate=preselection==null?null:financialSite==0?preselection.ExpansionProbe.Fork().TryPlan(preselection.Observation,profile,AiProfile.Initial,0,preselection.FreshActor,AdmissibleNewSite):financialReady?preselection.ExpansionProbe.Fork().TryPlan(preselection.Observation,profile,AiProfile.Initial,financialSite,preselection.FreshActor,AdmissibleNewSite):null;
                    var proposal=preselection?.CounterfactualProposal;var armyRejection=proposal?.ArmyRejection;
                    var financeMemo=arbiter.GetType().GetMethod("SiteFundingMemory",F).Invoke(arbiter,null) as string;
                    var threats=AiDefensePlanner.Threats(o,profile);
                    var composition=(AiPhaseCompositionPlanner)Get(owner,"composition");var demand=(AiProductionDemand)Get(owner,"demand");
                    var launches=Get(owner,"launches");var infrastructure=(AiInfrastructurePlanner)Get(owner,"infrastructure");
                    int scoutBudget=(int)owner.GetType().GetProperty("ScoutBudget",F).GetValue(owner);
                    var scoutPlanner=Get(owner,"scoutPlanner");bool usefulScout=(bool)Call(scoutPlanner,"UsefulProductionDemand",o,preselection?.Knowledge??checkpoint.Knowledge,AiProfile.Initial,scoutBudget);
                    var compositionAssessment=composition.Assess(o,profile,AiProfile.Initial,ledger,scoutBudget,
                        o.Buildings.Where(b=>(bool)Call(launches,"ContainsProducer",b.Id)).Select(b=>b.Id),usefulScoutDemand:usefulScout);
                    var productionKind=compositionAssessment.Candidates.LastOrDefault()?.Action.UnitKind??PlayableEntityKind.Tank;
                    var normalEconomy=new AiEconomyPlanner().Plan(o,profile,demand,AiProfile.Initial,budget.Available,productionKind:productionKind);
                    bool missingScout=usefulScout&&!o.Entities.Any(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0)&&
                        !o.Buildings.Where(b=>b.Owner==o.Owner&&b.PrivateState!=null).SelectMany(b=>b.PrivateState.Orders).Any(q=>q.Kind==PlayableEntityKind.Explorer)&&
                        !ledger.Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer);
                    bool urgentRepair=infrastructure.Plan(o,profile,AiProfile.Initial,normalEconomy.Capacity).Any(x=>x.Legal&&x.Priority>0&&x.Action.Kind==PlayableCommandKind.StartBuildingRepair);
                    var scaling=demand.Assess(o,profile,AiProfile.Initial,budget.Available+ledger.Reserved.Where(AiTechnologyFunding.IsFunding).Sum(e=>(long)e.Amount),AiRosterCatalog.Initial,productionKind);
                    bool essentialScale=scaling.CanScale&&normalEconomy.Candidates.Any(x=>x.Policy=="economy"&&x.Action.Kind==PlayableCommandKind.BuildAt&&x.Action.BuildingKind==PlayableBuildingKind.Factory&&
                        AiEconomyAdmission.Reject(o,profile,x.Action)==null&&!expansion.ClaimsSite(x.Action.SiteId)&&!infrastructure.ClaimsSlot(x.Action.SiteId,x.Action.SlotId));
                    int protectedFunds=preselection?.ProtectedFunds??0;
                    if(preselection==null&&ledger.Reserved.Any(e=>(bool)Call(launches,"IsStartup",e)))
                    {
                        var noOwnSavings=new AiBudgetState(ledger.OwnerId,ledger.Generation,ledger.Liquid,ledger.SafetyReserve,ledger.PaidTotal,
                            ledger.Reserved.Where(e=>!AiTechnologyFunding.IsFunding(e)),ledger.Unpaid,ledger.Paid,ledger.ReservationLifetimeTicks,ledger.AdmittedDecision,ledger.AdmittedAction,ledger.RepairPaidTotal);
                        var usefulCycle=composition.Assess(o,profile,AiProfile.Initial,noOwnSavings,scoutBudget,
                            o.Buildings.Where(b=>(bool)Call(launches,"ContainsProducer",b.Id)).Select(b=>b.Id),usefulScoutDemand:usefulScout);
                        int productiveCycle=usefulCycle.Candidates.Where(x=>x.Legal&&x.Policy=="production"&&PlayableUnitRules.ProductionKind(x.Action.Kind).HasValue)
                            .Select(x=>PlayableUnitRules.Cost(profile,x.Action.UnitKind)).DefaultIfEmpty(0).Min();protectedFunds=Math.Max(protectedFunds,productiveCycle);
                    }
                    if(preselection==null&&(scaling.CanScale||scaling.ScalingReason=="factory startup fund unavailable"))protectedFunds=checked(protectedFunds+(int)scaling.StartupFund);
                    long freeAfterProtection=preselectionAvailable-protectedFunds;
                    bool noPendingExpansion=!pending.Any(x=>(string)Get(x,"PolicyName")=="expansion");
                    bool openingAllows=preselection?.AllowsExpansion??(bool)owner.GetType().GetField("openingExecutor",F).GetValue(owner).GetType().GetProperty("AllowsExpansion",F).GetValue(Get(owner,"openingExecutor"));
                    var ownerExplorers=o.Entities.Where(e=>e.Owner==o.Owner&&e.Kind==PlayableEntityKind.Explorer).Select(e=>new{e.Id,e.Health,e.Moving,e.CurrentOrder,ArmyId=armies.ArmyFor(e.Id),
                        PendingPolicies=pending.Where(item=>((PlayableAiAction)Get(item,"Action")).EntityIds.Contains(e.Id)).Select(item=>Get(item,"PolicyName"))}).ToArray();
                    bool preselectionFreshActor=alternative!=null&&preselection.FreshActor(alternative.EntityIds.Single());
                    var financeHold=preselection?.Ledger.Reserved.SingleOrDefault(e=>e.Purpose=="expansion-financial");
                    bool financeHoldExpiryValid=financeHold==null||financeHold.ExpiresTick>o.Tick;
                    bool safeAlternative=alternative!=null&&o.RouteProofs.Any(r=>r.UnitId==alternative.EntityIds.Single()&&r.TargetId==alternative.SiteId&&r.Kind==PlayableRouteTargetKind.PublicObjective&&
                        AiExpansionPlanner.Safe(o,profile,AiProfile.Initial,o.Sites.Single(s=>s.Site.Id==alternative.SiteId),o.Entities.Single(u=>u.Id==alternative.EntityIds.Single()),r));
                    bool fullyPayable=proposal!=null&&freeAfterProtection>=proposal.Credits+proposal.StartupFund;
                    bool proposalExpiryValid=proposal!=null&&proposal.ExpiresTick>o.Tick+observationDelay;
                    bool blockedByCurrentFinance=preselection!=null&&financialSite!=0&&financialSite!=alternative?.SiteId&&!financialReady&&currentGate==null;
                    bool isEligible=preselection!=null&&alternative!=null&&proposal!=null&&proposal.ArmyRejection==null&&preselectionFreshActor&&safeAlternative&&
                        blockedByCurrentFinance&&preselection.ExpansionState.SiteId==0&&!preselection.PendingExpansion&&preselection.AvailableActions>0&&
                        threats.Length==0&&!preselection.UrgentRepair&&preselection.AllowsExpansion&&ledger.Liquid==o.Credits&&fullyPayable&&proposalExpiryValid&&financeHoldExpiryValid;
                    string eligibility=preselection==null?"preselection-observation-unavailable":alternative==null?"no-preselection-fresh-safe-reachable-alternative":proposal==null?"ordinary-proposal-rejected":proposal.ArmyRejection!=null?"army-ownership:"+proposal.ArmyRejection:
                        !safeAlternative?"candidate-route-not-safe":!blockedByCurrentFinance?"current-finance-gate-not-suppressing-alternative":preselection.ExpansionState.SiteId!=0?"expansion-already-claimed":preselection.PendingExpansion?"expansion-pending":preselection.AvailableActions<=0?"no-decision-capacity":
                        threats.Length>0?"visible-threat":preselection.UrgentRepair?"urgent-repair":!preselection.AllowsExpansion?"opening-blocks-expansion":ledger.Liquid!=o.Credits?"ledger-bank-not-current":!fullyPayable?"startup-not-payable-after-protection":!proposalExpiryValid?"proposal-expiry-contract":!financeHoldExpiryValid?"finance-hold-expired":"qualified-preselection-finance-gate";
                    bool actualExpansionPresent=preselection?.ActualExpansionAction!=null&&preselection.ActualExpansionProposal!=null;
                    if(isEligible)eligible.Add(c.Owner+"@"+o.Tick);
                    var explorerClaims=checkpoint.Armies?.Armies.SelectMany(a=>a.Members.Select(id=>new{Army=a.Id,Role=a.Role.ToString(),a.TacticalOwner,Actor=id})).ToArray();
                    var freshView=authority.ParticipantView(c.Owner);var freshObservation=PlayableAiObservation.From(freshView);
                    long decisionAuthorityTick=authority.Tick;
                    var selectedExpansion=pending.SingleOrDefault(x=>(string)Get(x,"PolicyName")=="expansion");
                    var selectedExpansionAction=selectedExpansion==null?null:(PlayableAiAction)Get(selectedExpansion,"Action");
                    long? selectedDue=selectedExpansion==null?(long?)null:(long)Get(selectedExpansion,"DueTick");
                    var deliveryTrace=trace.Cast<PlayableAiTraceRecord>().ToArray();
                    if(selectedDue.HasValue)
                    {
                        using(var deliveryHost=new UnityHostRouteService())
                        while(authority.Tick<=selectedDue.Value)
                        {
                            bool advanced=false;
                            for(int attempt=0;attempt<512;attempt++){if(authority.TryAdvance()){advanced=true;break;}deliveryHost.Service(authority,64);}
                            Assert.True(advanced,"ordinary route barrier before selected expansion delivery at tick "+authority.Tick);
                            Assert.IsNull(authority.Latest.Failure);
                        }
                        Assert.AreEqual(selectedDue.Value+1,authority.Tick,"selected expansion receipt is observed on the first ordinary authority tick after its due tick");
                        deliveryTrace=((IEnumerable)Get(owner,"records")).Cast<PlayableAiTraceRecord>().ToArray();
                    }
                    var deliveredExpansion=selectedExpansionAction==null?null:deliveryTrace.LastOrDefault(r=>r.Policy=="expansion"&&r.ActionId==selectedExpansionAction.ActionId);
                    var financialAfter=budget.Capture().Reserved.SingleOrDefault(e=>e.Purpose=="expansion-financial");
                    int activeExpansionAfter=expansion.Capture().SiteId;
                    bool oldFinancialClockPreserved=financeHold!=null&&financialAfter!=null&&financialAfter.Amount==0&&financialAfter.Action.SiteId==financeHold.Action.SiteId&&
                        financialAfter.CreatedTick==financeHold.CreatedTick&&financialAfter.ExpiresTick==financeHold.ExpiresTick&&
                        (int)Call(finance,"ActiveSite",arbiter)==financeHold.Action.SiteId;
                    bool postDeliveryExactRestore=false;
                    if(deliveredExpansion!=null)
                    {
                        var postDeliveryBytes=authority.CaptureBytes();var restored=PlayableAuthorityTick.RestoreBytes(postDeliveryBytes,config);
                        try{CollectionAssert.AreEqual(postDeliveryBytes,restored.CaptureBytes(),"exact C9 restore with selected expansion receipt and old zero-clock memo");postDeliveryExactRestore=true;}
                        finally{restored.Stop();}
                    }
                    var row=new {c.Seed,CheckpointTick=c.Tick,TargetTick=decisionAuthorityTick,DeliveryTick=selectedDue,c.Owner,CheckpointHash=Wire.Hash(bytes),ExactRestore=true,
                        ObservationSource=scheduledObservation==null?"unavailable-no-preselection-observation":"preselection-before-current-decision-commit",
                        ObservationCapturedBeforeHostDispose=scheduledObservation!=null,HostServiceCallsBeforeDecision=hostServiceCalls,
                        CaptureBytesAfterRestoreBeforeContinuation=true,CaptureBytesInsidePreselectionCallback=false,DetachedProbeCaptureComparedWorld=true,
                        DetachedProbeWorldStable=Wire.Hash(worldBeforeDetachedProbe)==Wire.Hash(worldAfterDetachedProbe),
                        DetachedProbeWorldBeforeHash=Wire.Hash(worldBeforeDetachedProbe),DetachedProbeWorldAfterHash=Wire.Hash(worldAfterDetachedProbe),
                        ActualObservationTick=scheduledObservation?.Tick,ActualObservationSequence=scheduledObservation?.SnapshotSequence,
                        ActualObservationIdentity=scheduledObservation?.Identity,AuthorityTick=decisionAuthorityTick,PostDeliveryAuthorityTick=authority.Tick,LastDecisionTick=decisionTick,
                        DecisionIntervalTicks=decisionInterval,ObservationDelayTicks=observationDelay,
                        ScheduledDueTick=scheduled==null?(long?)null:(long)Get(scheduled,"DueTick"),
                        ScheduledPolicy=scheduled==null?null:Get(scheduled,"PolicyName"),ScheduledAction=scheduled==null?null:Get(scheduled,"Action"),
                        TraceBeforeSelection=preselection?.Trace,TraceAfterDecision=trace,FreshParticipantObservationTick=freshObservation.Tick,FreshParticipantObservationSequence=freshObservation.SnapshotSequence,
                        TypedKnowledgeObservationTick=preselection?.Knowledge.ObservationTick,
                        ScheduledObservationRouteProofCount=scheduledObservation?.RouteProofs.Count,
                        ObservationKnowledgeOrder="preselection owner observation and typed knowledge captured after actual expansion Propose construction and before arbiter selection while route host is active; detached fork probed after callback; fresh participant view sampled last",
                        FreshParticipantRouteProofCount=freshObservation.RouteProofs.Count,FreshParticipantIdentity=freshObservation.Identity,
                        DiagnosticStatus=preselection==null?"UNAVAILABLE":"DIAGNOSTIC_PASS",
                        BehaviorStatus=preselection==null?"UNAVAILABLE":!isEligible?"INELIGIBLE":actualExpansionPresent?"BEHAVIOR_GREEN":"BEHAVIOR_RED",
                        Status=preselection==null?"UNAVAILABLE":!isEligible?"INELIGIBLE":actualExpansionPresent?"BEHAVIOR_GREEN":"BEHAVIOR_RED",Eligibility=eligibility,
                        ActualExpansionAction=preselection?.ActualExpansionAction,ActualExpansionProposal=preselection?.ActualExpansionProposal,
                        ActualExpansionWasProposed=preselection?.ActualExpansionProposal!=null,
                        SelectedExpansionAction=selectedExpansionAction,SelectedExpansionDueTick=selectedDue,ExpansionSelected=selectedExpansion!=null,
                        ExpansionDeliveryStatus=deliveredExpansion?.Status.ToString(),ExpansionDeliveryApplied=deliveredExpansion?.Status==PlayableAiDeliveryStatus.Applied,
                        ActiveExpansionSiteAfterDelivery=activeExpansionAfter,OldFinancialClockPreserved=oldFinancialClockPreserved,PostDeliveryExactRestore=postDeliveryExactRestore,
                        FinancialAfterDelivery=financialAfter,TraceAfterDelivery=deliveryTrace,
                        FullParticipantView=o,TypedKnowledge=preselection?.Knowledge,TypedRouteProofs=o.RouteProofs,Armies=checkpoint.Armies,ActorArmyMembership=explorerClaims,
                        OwnerExplorers=ownerExplorers,IdleUnclaimedExplorerCount=ownerExplorers.Count(e=>e.Health>0&&!e.Moving&&e.CurrentOrder==null&&e.ArmyId==0&&!e.PendingPolicies.Any()),
                        PreselectionArmyActors=preselection?.ArmyActors,PreselectionPendingActors=preselection?.PendingActors,
                        PreselectionFreshActor=preselectionFreshActor,PreselectionBudget=preselection?.Ledger,PreselectionProtectedFunds=preselection?.ProtectedFunds,
                        PreselectionAvailableAfterProtection=freeAfterProtection,PreselectionPendingExpansion=preselection?.PendingExpansion,
                        PreselectionAvailableActions=preselection?.AvailableActions,CounterfactualProposal=proposal,
                        FinanceHold=financeHold,FinanceHoldExpiryValid=financeHoldExpiryValid,ProposalExpiryValid=proposalExpiryValid,
                        FullyPayableAfterProtection=fullyPayable,SafeAlternative=safeAlternative,BlockedByCurrentFinance=blockedByCurrentFinance,
                        Expansion=expansionState,PreselectionExpansion=preselection?.ExpansionState,FinanceSite=financialSite,FinanceReady=financialReady,FinanceMemo=financeMemo,
                        Alternative=alternative,AlternativeOrdinaryIntent=proposal,AlternativeArmyRejection=armyRejection,CurrentGateProposal=currentGate,
                        Threats=threats,UrgentRepair=urgentRepair,ProtectedFunds=protectedFunds,Scaling=scaling,
                        Ledger=ledger,AvailableAfterProtection=freeAfterProtection,Pending=pending.Select(x=>new{Policy=Get(x,"PolicyName"),Action=Get(x,"Action"),Intent=Get(x,"Intent")}),
                        Opening=checkpoint.OpeningExecution,OrdinaryEconomy=normalEconomy.Candidates.Select(x=>new{x.Policy,x.Action,x.Reason,x.Priority}),
                        CurrentDecision=Get(owner,"economyDecision"),PublicObjectives=o.PublicScoutObjectives,RouteProofs=o.RouteProofs,
                        PostDecisionSafeAlternative=alternative!=null&&o.RouteProofs.Any(r=>r.UnitId==alternative.EntityIds.Single()&&r.TargetId==alternative.SiteId&&r.Kind==PlayableRouteTargetKind.PublicObjective&&
                            AiExpansionPlanner.Safe(o,profile,AiProfile.Initial,o.Sites.Single(s=>s.Site.Id==alternative.SiteId),o.Entities.Single(u=>u.Id==alternative.EntityIds.Single()),r))};
                    rows.Add(row);File.AppendAllText(output,JsonConvert.SerializeObject(row,Formatting.None)+Environment.NewLine);
                }
                finally{PlayableAiOwnerLoop.PreselectionReadbackForTests=null;authority.Stop();}
            }
            TestContext.WriteLine("financial readback: "+JsonConvert.SerializeObject(rows,Formatting.None));
        }

        [Test] public void DiagnosticReadback_PreservesExactPreselectionEvidence()
        {
            Assert.AreEqual(2,contexts.Count,"both scheduled owner decisions were observed before the current decision committed");
            CollectionAssert.AreEquivalent(new long[]{630,945},contexts.Select(x=>x.Observation.Tick));
            Assert.True(contexts.All(x=>x.Observation.SnapshotSequence==x.Observation.Tick&&x.Observation.RouteProofs.Count>0),"captured decision observations retain tick, sequence and typed routes");
            TestContext.WriteLine("DIAGNOSTIC_PASS: two exact preselection contexts captured with typed route proofs.");
        }

        [Test] public void ActualExpansionProposal_IsPresentForQualifiedPreselectionCandidate()
        {
            Assert.AreEqual(2,eligible.Count,"both original natural C9 contexts must remain genuinely qualified; otherwise GREEN would be vacuous");
            Assert.AreEqual(2,rows.Count);
            foreach(var source in rows)
            {
                var row=JObject.FromObject(source);int seed=(int)row["Seed"];
                int expectedSite=seed==7109?22:9,expectedActor=seed==7109?13:11;
                Assert.AreEqual("qualified-preselection-finance-gate",(string)row["Eligibility"],"original finance-gate qualification retained for seed "+seed);
                Assert.AreEqual("BEHAVIOR_GREEN",(string)row["BehaviorStatus"],"proposal is GREEN only after qualification and actual proposal");
                Assert.True((bool)row["ActualExpansionWasProposed"],"actual Proposal path receives fallback for seed "+seed);
                Assert.AreEqual(expectedSite,(int)row["ActualExpansionAction"]["SiteId"]);
                CollectionAssert.AreEqual(new[]{expectedActor},row["ActualExpansionAction"]["EntityIds"].ToObject<int[]>());
                Assert.AreEqual(200,(int)row["ActualExpansionProposal"]["StartupFund"]);
                Assert.IsNull((string)row["ActualExpansionProposal"]["ReservationId"],"alternative uses ordinary funding, never the old finance hold");
                Assert.True((bool)row["ExpansionSelected"],"arbiter selected the sole ordinary expansion intent for seed "+seed);
                Assert.AreEqual(expectedSite,(int)row["SelectedExpansionAction"]["SiteId"]);
                Assert.AreEqual("Applied",(string)row["ExpansionDeliveryStatus"],"normal authority delivery applies selected expansion");
                Assert.AreEqual(expectedSite,(int)row["ActiveExpansionSiteAfterDelivery"]);
                Assert.True((bool)row["OldFinancialClockPreserved"],"different selected site keeps the old zero-amount finance clock");
                Assert.True((bool)row["PostDeliveryExactRestore"],"selected expansion plus preserved financial memo survive exact C9 restore");
                var before=row["FinanceHold"];var after=row["FinancialAfterDelivery"];
                Assert.AreEqual(before["Action"]["SiteId"],after["Action"]["SiteId"]);
                Assert.AreEqual(before["CreatedTick"],after["CreatedTick"]);Assert.AreEqual(before["ExpiresTick"],after["ExpiresTick"]);
                Assert.AreEqual(0,(int)after["Amount"]);
            }
            TestContext.WriteLine("BEHAVIOR_GREEN: both qualified original natural C9 decisions proposed, selected and ordinarily delivered the fully payable alternative while preserving the old site clock.");
        }
    }
}

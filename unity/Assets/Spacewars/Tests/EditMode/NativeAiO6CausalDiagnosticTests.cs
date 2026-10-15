using System;
using System.Collections;
using System.Diagnostics;
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
    public sealed class NativeAiO6CausalDiagnosticTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private sealed class Case
        {
            public int Seed,Tick; public string Owner,Entry,Hash;
        }
        private sealed class Capture
        {
            public PlayableAiPreselectionReadback Context; public object OwnerLoop; public AiArmyRegistryState Armies;
            public int[] PendingActors; public object ScoutClaims,PreselectionPending,Counterfactuals,FinanceMemo,EconomyPlan,Composition,Production,TechnologyPlan;
            public object Row;
        }
        private sealed class HookProfileRun
        {
            public byte[][] TickBytes;public string[] Owners;public string[] TargetDecisions;public int OwnerCalls;public long ElapsedStopwatchTicks;
        }
        private static object Get(object value,string name)
        {var field=value.GetType().GetField(name,F);if(field!=null)return field.GetValue(value);return value.GetType().GetProperty(name,F).GetValue(value);}
        private static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,F).Invoke(value,args);
        private static string Root()
        {var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static PlayableAuthorityTick Advance(PlayableAuthorityTick authority,UnityHostRouteService host)
        {
            for(int attempt=0;attempt<512;attempt++)
            {
                if(authority.TryAdvance())return authority;
                host.Service(authority,64);
            }
            Assert.Fail("ordinary typed-route barrier timed out at tick "+authority.Tick);
            return authority;
        }
        private static object Action(PlayableAiAction a)=>a==null?null:new{a.ActionId,a.Kind,a.SiteId,a.BuildingKind,Actors=a.CopyEntityIds(),a.Target};
        private static object Intent(PlayableAiIntentReadback i)=>i==null?null:new{ i.Policy,Action=Action(i.Action),i.Credits,i.StartupFund,i.ExpiresTick,i.ReservationId,i.Priority,i.ArmyRejection,i.Conditions };
        private static object Budget(AiBudgetState b)=>new{b.OwnerId,b.Generation,b.Liquid,b.SafetyReserve,b.PaidTotal,b.RepairPaidTotal,
            Reserved=b.Reserved.Select(e=>new{e.Id,e.Purpose,Amount=e.Amount,e.CreatedTick,e.ExpiresTick,Action=Action(e.Action),e.Terms}),
            Unpaid=b.Unpaid.Select(e=>new{e.Id,e.Purpose,Amount=e.Amount,e.CreatedTick,e.ExpiresTick,Action=Action(e.Action),e.Terms}),
            Paid=b.Paid.Select(e=>new{e.Id,e.Purpose,Amount=e.Amount,e.CreatedTick,e.ExpiresTick,Action=Action(e.Action),e.Terms})};
        private static bool ArmyClaimed(AiArmyRegistryState s,int id)=>s.Armies.Any(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.Members.Contains(id)||a.Reinforcements.Contains(id)));
        private static object[] Counterfactuals(object owner,PlayableAiPreselectionReadback context,PlayableProfile profile,AiArmyRegistry registry,
            AiArmyRegistryState armies,object finance,AiDecisionArbiter arbiter,object pendingItems)
        {
            var o=context.Observation;int financeSite=context.FinanceSite;
            var budget=(AiBudgetLedger)Get(owner,"budget");
            var pendingActors=((IEnumerable)pendingItems).Cast<object>().SelectMany(p=>((PlayableAiAction)Get(p,"Action")).CopyEntityIds()).ToArray();
            bool Fresh(int id)=>registry.ArmyFor(id)==0&&!pendingActors.Contains(id);
            bool Expired(TerritorySiteSnapshot s)=>s!=null&&(bool)Call(finance,"IsExpiredFingerprint",s,arbiter);
            object Probe(string guards,bool ignoreFinanceSite,bool ignoreTombstones,bool ignoreActorClaims)
            {
                bool AllowedSite(int id){var s=o.Sites.FirstOrDefault(x=>x.Site.Id==id);return (ignoreFinanceSite||id!=financeSite)&&(ignoreTombstones||!Expired(s));}
                Func<int,bool> actor=ignoreActorClaims?(_=>true):Fresh;
                var action=context.ExpansionProbe.Fork().TryPlan(o,profile,AiProfile.Initial,0,actor,AllowedSite);
                if(action==null)return new{Guards=guards,Action=(object)null,Intent=(object)null,ArmyRejection=(string)null,FullyPayable=false,ActorFresh=false,ActorIdle=false,ScoutOrArmyClaim=false,SafeTypedRoute=false,ExpiredFingerprint=false,LegalOrdinaryCandidate=false};
                var intent=(AiIntent)Call(owner,"Proposal","expansion",action,o,1);
                string armyRejection=intent==null?null:registry.Reject(intent);
                var actorSnapshot=o.Entities.FirstOrDefault(x=>x.Id==action.CopyEntityIds().Single());var site=o.Sites.FirstOrDefault(x=>x.Site.Id==action.SiteId);
                var route=o.RouteProofs.FirstOrDefault(x=>x.UnitId==action.CopyEntityIds().Single()&&x.TargetId==action.SiteId&&x.Kind==PlayableRouteTargetKind.PublicObjective);
                bool fresh=actorSnapshot!=null&&Fresh(actorSnapshot.Id);
                bool claimed=actorSnapshot!=null&&ArmyClaimed(armies,actorSnapshot.Id);
                bool idle=actorSnapshot!=null&&actorSnapshot.Health>0&&!actorSnapshot.Moving&&actorSnapshot.CurrentOrder==null&&!claimed&&!pendingActors.Contains(actorSnapshot.Id);
                bool safe=actorSnapshot!=null&&site!=null&&route!=null&&AiExpansionPlanner.Safe(o,profile,AiProfile.Initial,site,actorSnapshot,route);
                bool expired=Expired(site);bool payable=intent!=null&&AiExpansionFinancial.FullyFundable(budget,context.ProtectedFunds,intent);
                bool legal=intent!=null&&armyRejection==null&&payable&&fresh&&idle&&safe&&!expired&&action.SiteId!=financeSite&&
                    !context.Threatened&&!context.UrgentRepair&&context.AllowsExpansion&&!context.PendingExpansion&&context.AvailableActions>0&&
                    intent.ExpiresTick>o.Tick+context.ObservationDelayTicks;
                return new{Guards=guards,Action=Action(action),Intent=intent==null?null:new{intent.Id,intent.Policy,intent.Credits,intent.StartupFund,intent.ExpiresTick,intent.ReservationId,intent.Priority,intent.Conditions},
                    ArmyRejection=armyRejection,FullyPayable=payable,ActorFresh=fresh,ActorIdle=idle,ScoutOrArmyClaim=claimed,SafeTypedRoute=safe,ExpiredFingerprint=expired,
                    LegalOrdinaryCandidate=legal,Route=route==null?null:new{route.UnitId,route.TargetId,route.Kind,route.Origin,route.Goal,route.Radius,route.Generation,route.Tick,route.Path}};
            }
            return new[]{Probe("current financial site and tombstones",false,false,false),Probe("ignore active financial site only",true,false,false),
                Probe("ignore expiry tombstones only",false,true,false),Probe("ignore active financial site and tombstones",true,true,false),
                Probe("ignore army/pending actor predicate",false,false,true)};
        }

        [Test]
        public void C9_ActualNaturalPreselectionExplainsExpansionFallbackGuards()
        {
            var root=Root();var project=JObject.Parse(File.ReadAllText(Path.Combine(root,"workflow/project.json")));
            var planningRoot=(string)project["planning_root"];
            var inputDir=Path.Combine(planningRoot,"evidence/native-strategic-ai/o6-next-inputs-20261012");
            var manifest=JObject.Parse(File.ReadAllText(Path.Combine(inputDir,"inputs.json")));
            Assert.AreEqual("5c0b2b25383325b89f800f220941da4fcb7ec1df",(string)manifest["source"]);
            Assert.AreEqual("30e6ab7de534856dd3c425523426476489eb7fe6",(string)manifest["proofCommit"]);
            var cases=new[]{
                new Case{Seed=7108,Tick=900,Owner="ai-4",Entry="7108-checkpoint-900.world",Hash="64d03d029c82c0c06ac198044db52334a49163a8816b52da499daaa80933cdb7"},
                new Case{Seed=7108,Tick=4500,Owner="ai-4",Entry="7108-checkpoint-4500.world",Hash="605a74303bf8e9b847989e414c5c488593dfc9da85f149a5777bb2675fbaf5e3"},
                new Case{Seed=7108,Tick=12000,Owner="ai-4",Entry="7108-checkpoint-12000.world",Hash="d7bb1afa0a16a5f31efdf0e10e5b4136118eecd206f53b36751e4191c76ebee9"},
                new Case{Seed=7109,Tick=900,Owner="ai-1",Entry="7109-checkpoint-900.world",Hash="521f60dca8e6d17226c0fdc7ca245858bd675818744b962efc7d5ade6d4323b8"},
                new Case{Seed=7109,Tick=4500,Owner="ai-1",Entry="7109-checkpoint-4500.world",Hash="dc61b052880d068223efb00e03f7a2cb3520d058d6e7452856799fda29568242"},
                new Case{Seed=7109,Tick=12000,Owner="ai-1",Entry="7109-checkpoint-12000.world",Hash="5297257abdd722bd7acfeba829f6fcaa8c546a3201e9fd8d38b5dc150f9740de"}
            };
            var output=Path.Combine(root,".local/o6-causal-diagnostic/readback.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,string.Empty);
            var rows=new System.Collections.Generic.List<object>();
            var defaultHookOwners=new System.Collections.Generic.HashSet<string>();
            using(var zip=ZipFile.OpenRead(Path.Combine(inputDir,"inputs.zip")))
            foreach(var c in cases)
            {
                var entry=zip.GetEntry(c.Entry);Assert.NotNull(entry,"actual natural C9 input "+c.Entry);
                byte[] bytes;using(var stream=entry.Open())using(var copy=new MemoryStream()){stream.CopyTo(copy);bytes=copy.ToArray();}
                Assert.AreEqual(c.Hash,Wire.Hash(bytes),"input checkpoint SHA-256");
                var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
                var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
                var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,c.Seed,participants);
                Assert.AreEqual(500,profile.StartingCredits);Assert.AreEqual(c.Seed,config.Seed);
                Assert.AreEqual(8,config.Roster.Count);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
                var authority=PlayableAuthorityTick.RestoreBytes(bytes,config);
                try
                {
                    Assert.AreEqual(c.Tick,authority.Tick);CollectionAssert.AreEqual(bytes,authority.CaptureBytes(),"exact restore before ordinary continuation");
                    var initial=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==c.Owner);
                    Assert.AreEqual(PlayableAiOpening.GreedyMine,initial.Opening.Opening,"primary owner really has the GreedyMine opening");
                    Assert.AreEqual(c.Seed,initial.Seed);Assert.AreEqual(c.Owner,initial.OwnerId);
                    var captures=new System.Collections.Generic.List<Capture>();
                    PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=null;
                    PlayableAiOwnerLoop.PreselectionReadbackForTests=x=>
                    {
                        defaultHookOwners.Add(x.Observation.OwnerId);
                        if(x.Observation.OwnerId==c.Owner&&x.Observation.Tick>c.Tick&&x.Observation.Tick<=c.Tick+45)
                        {
                            var scheduler=Get(authority,"scheduler");var owner=((IEnumerable)Get(scheduler,"owners")).Cast<object>().Single(y=>(string)y.GetType().GetProperty("OwnerId",F).GetValue(y)==c.Owner);
                            var registry=(AiArmyRegistry)Get(owner,"armies");var armyState=registry.Capture();var pending=(IEnumerable)Get(owner,"pendingItems");
                            var prePending=pending.Cast<object>().Select(p=>new{Policy=Get(p,"PolicyName"),ObservationTick=Get(p,"ObservationTick"),Action=Action((PlayableAiAction)Get(p,"Action")),DueTick=Get(p,"DueTick")}).ToArray();
                            var prePendingActors=pending.Cast<object>().SelectMany(p=>((PlayableAiAction)Get(p,"Action")).CopyEntityIds()).ToArray();
                            var arbiter=(AiDecisionArbiter)Get(owner,"arbiter");var finance=Get(owner,"expansionFinancial");
                            bool expired(TerritorySiteSnapshot s)=>s!=null&&(bool)Call(finance,"IsExpiredFingerprint",s,arbiter);
                            var preScouts=armyState.Armies.Where(a=>a.Role==AiArmyRole.Scout&&a.TacticalOwner==AiScoutPlanner.Policy&&a.Phase!=AiArmyPhase.Disbanded).Select(a=>new{a.Id,a.TacticalOwner,a.Members,a.Reinforcements,a.Objective,a.Phase}).ToArray();
                            var preExplorers=x.Observation.Entities.Where(u=>u.Owner==x.Observation.Owner&&u.Kind==PlayableEntityKind.Explorer).OrderBy(u=>u.Id).Select(u=>new
                                {u.Id,u.Kind,u.Health,u.Moving,u.Position,CurrentOrder=u.CurrentOrder==null?null:new{u.CurrentOrder.Kind,u.CurrentOrder.TargetId,u.CurrentOrder.Destination,u.CurrentOrder.IssuedTick},
                                 ArmyId=armyState.Armies.Where(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.Members.Contains(u.Id)||a.Reinforcements.Contains(u.Id))).Select(a=>(long?)a.Id).FirstOrDefault(),
                                 ArmyRole=armyState.Armies.FirstOrDefault(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.Members.Contains(u.Id)||a.Reinforcements.Contains(u.Id)))?.Role,
                                 TacticalOwner=armyState.Armies.FirstOrDefault(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.Members.Contains(u.Id)||a.Reinforcements.Contains(u.Id)))?.TacticalOwner,
                                 Pending=prePendingActors.Contains(u.Id),ScoutClaim=preScouts.Any(a=>a.Members.Contains(u.Id)||a.Reinforcements.Contains(u.Id))}).ToArray();
                            var publicSites=x.Observation.Sites.Select(s=>new{Site=s.Site.Id,Kind=s.Site.Kind,Owner=s.Owner,Claimant=s.Claimant,s.CenterId,s.Contested,s.Progress,ExpiredFingerprint=expired(s)}).ToArray();
                            captures.Add(new Capture{Context=x,OwnerLoop=owner,Armies=armyState,PendingActors=prePendingActors,ScoutClaims=preScouts,PreselectionPending=prePending,
                                Counterfactuals=Counterfactuals(owner,x,profile,registry,armyState,finance,arbiter,Get(owner,"pendingItems")),
                                FinanceMemo=Call(arbiter,"SiteFundingMemory"),EconomyPlan=Get(owner,"EconomyPlan"),Composition=Get(owner,"compositionAssessment"),
                                Production=Get(owner,"production"),TechnologyPlan=Get(owner,"TechnologyPlan"),
                                Row=new JObject{["Owners"]=JToken.FromObject(preExplorers),["PublicSites"]=JToken.FromObject(publicSites)}});
                        }
                    };
                    int hostServiceCalls=0;
                    try
                    {
                        using(var host=new UnityHostRouteService())
                        for(int i=0;i<45;i++)
                        {
                            int before=captures.Count;
                            bool advanced=false;
                            for(int attempt=0;attempt<512;attempt++)
                            {
                                if(authority.TryAdvance()){advanced=true;break;}
                                host.Service(authority,64);hostServiceCalls++;
                            }
                            Assert.True(advanced,"ordinary route barrier at tick "+authority.Tick);Assert.IsNull(authority.Latest.Failure);
                            for(int index=before;index<captures.Count;index++)
                            {
                                var capture=captures[index];var context=capture.Context;var observation=context.Observation;
                                var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==c.Owner);
                                var owner=capture.OwnerLoop;var postRegistry=(AiArmyRegistry)Get(owner,"armies");var postPendingItems=(IEnumerable)Get(owner,"pendingItems");
                                int financeSite=context.FinanceSite;var financial=Get(owner,"expansionFinancial");var arbiter=(AiDecisionArbiter)Get(owner,"arbiter");
                                bool Expired(TerritorySiteSnapshot s)=>(bool)Call(financial,"IsExpiredFingerprint",s,arbiter);
                                bool Fresh(int id)=>!ArmyClaimed(capture.Armies,id)&&!capture.PendingActors.Contains(id);
                                PlayableAiAction ReplayProbe(bool ignoreFinancialSite,bool ignoreTombstones,bool ignoreActorClaims)
                                {
                                    bool SiteAllowed(int id){var s=observation.Sites.FirstOrDefault(x=>x.Site.Id==id);return (ignoreFinancialSite||id!=financeSite)&&(ignoreTombstones||!Expired(s));}
                                    return context.ExpansionProbe.Fork().TryPlan(observation,profile,AiProfile.Initial,0,ignoreActorClaims?(_=>true):Fresh,SiteAllowed);
                                }
                                var pending=postPendingItems.Cast<object>().Select(p=>new{Policy=Get(p,"PolicyName"),ObservationTick=Get(p,"ObservationTick"),Action=Action((PlayableAiAction)Get(p,"Action")),DueTick=Get(p,"DueTick")}).ToArray();
                                var selectedPending=pending.Where(p=>(string)p.Policy=="expansion"&&(long)p.ObservationTick==observation.Tick).ToArray();
                                var decision=checkpoint.EconomyDecision;
                                var economyDecision=decision==null?null:new{decision.Tick,decision.Ordinal,decision.Liquid,decision.Available,decision.SafetyReserve,decision.Reserved,decision.Unpaid,decision.Reason,decision.Blockers};
                                var beforeProbe=authority.CaptureBytes();
                                var replayed=new[]{Action(ReplayProbe(false,false,false)),Action(ReplayProbe(true,false,false)),Action(ReplayProbe(false,true,false)),
                                    Action(ReplayProbe(true,true,false)),Action(ReplayProbe(false,false,true))};
                                var afterProbe=authority.CaptureBytes();CollectionAssert.AreEqual(beforeProbe,afterProbe,"detached counterfactual probes never mutate authority C9");
                                bool hasLegalCounterfactual=((IEnumerable)capture.Counterfactuals).Cast<object>().Any(x=>(bool)x.GetType().GetProperty("LegalOrdinaryCandidate").GetValue(x));
                                string behavior=hasLegalCounterfactual?(selectedPending.Length==0?"BEHAVIOR_RED":"BEHAVIOR_GREEN"):"DIAGNOSTIC_ONLY_NO_LEGAL_COUNTERFACTUAL";
                                capture.Row=new
                                {
                                    Source=(string)manifest["source"],ProofCommit=(string)manifest["proofCommit"],Input=c.Entry,InputSHA256=c.Hash,Seed=c.Seed,CheckpointTick=c.Tick,Owner=c.Owner,
                                    Generation=observation.Generation,Opening=initial.Opening.Opening.ToString(),OpeningPhase=initial.Opening.Phase.ToString(),DecisionTick=observation.Tick,ObservationSequence=observation.SnapshotSequence,
                                    ObservationIdentity=observation.Identity,ExactRestoreBeforeContinuation=true,ActualC9Uninjected=true,AuthorityBytesEqualAroundDetachedProbes=Wire.Hash(beforeProbe)==Wire.Hash(afterProbe),
                                    PreselectionBeforeArbiter=true,RouteProofs=observation.RouteProofs.Where(r=>r.Kind==PlayableRouteTargetKind.PublicObjective).Select(r=>new{r.UnitId,r.TargetId,r.Kind,r.Origin,r.Goal,r.Radius,r.Generation,r.Tick,r.Path}).ToArray(),
                                    Owners=((JObject)capture.Row)["Owners"],Armies=capture.Armies.Armies.Select(a=>new{a.Id,a.Role,a.Phase,a.TacticalOwner,a.Members,a.Reinforcements,a.LeaderId,a.Objective,a.ProgressTick}).ToArray(),ScoutClaims=capture.ScoutClaims,
                                    PublicSites=((JObject)capture.Row)["PublicSites"],FinanceSite=financeSite,context.FinanceReady,FinanceMemo=capture.FinanceMemo,Budget=Budget(context.Ledger),context.ProtectedFunds,
                                    FreeBeforeProtection=Math.Max(0,(long)context.Ledger.Liquid-context.Ledger.SafetyReserve-context.Ledger.Reserved.Sum(e=>(long)e.Amount)-context.Ledger.Unpaid.Sum(e=>(long)e.Amount)),
                                    AvailableActions=context.AvailableActions,context.PendingExpansion,context.AllowsExpansion,context.Threatened,context.UrgentRepair,
                                    CurrentExpansionProposal=Intent(context.ActualExpansionProposal),CurrentExpansionAction=Action(context.ActualExpansionAction),LegalCurrentCounterfactual=Intent(context.CounterfactualProposal),
                                    Counterfactuals=capture.Counterfactuals,DetachedProbeReplay=replayed,SelectedExpansionForDecision=selectedPending,PendingBeforeSelection=capture.PreselectionPending,PendingAfterSelection=pending,EconomyDecision=economyDecision,
                                    CurrentEconomyPlan=capture.EconomyPlan,CompositionAssessment=capture.Composition,ProductionState=capture.Production,TechnologyPlan=capture.TechnologyPlan,
                                    TechnologyActionAvailable=Math.Max(0,(long)context.Ledger.Liquid-context.Ledger.SafetyReserve-context.Ledger.Reserved.Sum(e=>(long)e.Amount)-context.Ledger.Unpaid.Sum(e=>(long)e.Amount)),
                                    TechnologyActionSelected=pending.Any(p=>(string)p.Policy=="research"&&(long)p.ObservationTick==observation.Tick),
                                    ArbiterRejections=arbiter.Rejections,
                                    Knowledge=context.Knowledge,BehaviorStatus=behavior,RootCause=hasLegalCounterfactual?"legal counterfactual exists; inspect actual proposal/selection":"diagnostic only: current predicate matrix yields no fully payable legal expansion"
                                };
                                rows.Add(capture.Row);File.AppendAllText(output,JsonConvert.SerializeObject(capture.Row,Formatting.None)+Environment.NewLine);
                            }
                        }
                    }
                    finally{PlayableAiOwnerLoop.PreselectionReadbackForTests=null;PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=null;}
                    Assert.AreEqual(c.Tick+45,authority.Tick,"ordinary natural continuation is bounded to 45 ticks");
                    Assert.Greater(captures.Count,0,"actual target-owner preselection decision occurred within the bounded window");
                    Assert.True(captures.All(x=>x.Row!=null),"every captured target-owner decision was read back after arbitration");
                    TestContext.WriteLine("O6_C9_CAUSAL "+JsonConvert.SerializeObject(new{c.Seed,c.Tick,c.Owner,Decisions=captures.Count,Rows=captures.Select(x=>x.Row)}));
                }
                finally{PlayableAiOwnerLoop.PreselectionReadbackForTests=null;PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=null;authority.Stop();}
            }
            TestContext.WriteLine("O6_C9_CAUSAL_RESULT "+output+" rows="+rows.Count);
            Assert.AreEqual(cases.Length,rows.Select(x=>(string)x.GetType().GetProperty("Input").GetValue(x)).Distinct().Count(),"each of the six original checkpoints contributed an actual decision row");
            var fundingMismatch=rows.Select(JObject.FromObject).SingleOrDefault(r=>(int)r["Seed"]==7108&&(int)r["CheckpointTick"]==12000&&(int)r["DecisionTick"]==12021);
            Assert.NotNull(fundingMismatch,"natural 7108/ai-4 decision at tick 12021 is present");
            var technology=fundingMismatch["TechnologyPlan"];
            Assert.AreEqual((int)PlayableResearchKind.ShkvalGuidance,(int)technology["FundingGoal"]["Research"]);
            Assert.Zero((int)fundingMismatch["TechnologyActionAvailable"]);
            Assert.AreEqual(424,(int)fundingMismatch["Budget"]["Reserved"].Single(x=>(string)x["Purpose"]=="technology-funding")["Amount"]);
            Assert.True(technology["Action"]==null||technology["Action"].Type==JTokenType.Null,"zero unreserved cash and a partial hold for the preferred branch must not advertise any unpayable ready-center research action");
            Assert.True(defaultHookOwners.Any(x=>x!="ai-4"),"a null owner filter preserves the legacy all-owner readback hook in the bounded fixture");
        }

        [Test]
        public void C9_TestHookOwnerFilter_PreservesAuthorityAndDecisionsWhileReducingProbeWork()
        {
            var root=Root();var project=JObject.Parse(File.ReadAllText(Path.Combine(root,"workflow/project.json")));var planningRoot=(string)project["planning_root"];
            var inputDir=Path.Combine(planningRoot,"evidence/native-strategic-ai/o6-next-inputs-20261012");byte[] bytes;
            using(var zip=ZipFile.OpenRead(Path.Combine(inputDir,"inputs.zip")))using(var stream=zip.GetEntry("7108-checkpoint-12000.world").Open())using(var copy=new MemoryStream()){stream.CopyTo(copy);bytes=copy.ToArray();}
            Assert.AreEqual("d7bb1afa0a16a5f31efdf0e10e5b4136118eecd206f53b36751e4191c76ebee9",Wire.Hash(bytes));
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,7108,participants);
            HookProfileRun Run(string ownerFilter)
            {
                var authority=PlayableAuthorityTick.RestoreBytes(bytes,config);var owners=new System.Collections.Generic.List<string>();var targetDecisions=new System.Collections.Generic.List<string>();var tickBytes=new System.Collections.Generic.List<byte[]>();
                try
                {
                    CollectionAssert.AreEqual(bytes,authority.CaptureBytes(),"exact restore before hook profile");
                    PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=ownerFilter;PlayableAiOwnerLoop.PreselectionReadbackElapsedStopwatchTicksForTests=0;PlayableAiOwnerLoop.PreselectionReadbackOwnerCallsForTests=0;
                    PlayableAiOwnerLoop.PreselectionReadbackForTests=x=>
                    {
                        owners.Add(x.Observation.OwnerId);
                        if(x.Observation.OwnerId=="ai-4")targetDecisions.Add(JsonConvert.SerializeObject(new{Tick=x.Observation.Tick,Action=Action(x.ActualExpansionAction),Proposal=Intent(x.ActualExpansionProposal)}));
                    };
                    using(var host=new UnityHostRouteService())
                    for(int i=0;i<45;i++){Advance(authority,host);tickBytes.Add(authority.CaptureBytes());}
                    return new HookProfileRun{TickBytes=tickBytes.ToArray(),Owners=owners.ToArray(),TargetDecisions=targetDecisions.ToArray(),OwnerCalls=PlayableAiOwnerLoop.PreselectionReadbackOwnerCallsForTests,ElapsedStopwatchTicks=PlayableAiOwnerLoop.PreselectionReadbackElapsedStopwatchTicksForTests};
                }
                finally{PlayableAiOwnerLoop.PreselectionReadbackForTests=null;PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=null;authority.Stop();}
            }
            HookProfileRun allOwners=Run(null),targetOnly=Run("ai-4");
            Assert.Greater(allOwners.OwnerCalls,targetOnly.OwnerCalls,"owner filtering skips expensive non-target counterfactual probes");
            Assert.True(allOwners.Owners.Any(x=>x!="ai-4"),"default null filter invokes the hook for non-target owners");
            Assert.True(targetOnly.Owners.All(x=>x=="ai-4"),"filtered hook invokes only the requested owner");
            CollectionAssert.AreEqual(allOwners.TargetDecisions,targetOnly.TargetDecisions,"ordinary ai-4 decisions are unchanged by the readback filter");
            Assert.AreEqual(allOwners.TickBytes.Length,targetOnly.TickBytes.Length);
            for(int i=0;i<allOwners.TickBytes.Length;i++)CollectionAssert.AreEqual(allOwners.TickBytes[i],targetOnly.TickBytes[i],"authority C9 bytes after ordinary tick "+(12001+i));
            double allMs=1000.0*allOwners.ElapsedStopwatchTicks/Stopwatch.Frequency,filteredMs=1000.0*targetOnly.ElapsedStopwatchTicks/Stopwatch.Frequency;
            TestContext.WriteLine("O6_HOOK_PROFILE "+JsonConvert.SerializeObject(new{Checkpoint="7108/ai-4/tick12000",Ticks=45,Owners=8,AllOwnerCalls=allOwners.OwnerCalls,TargetOnlyCalls=targetOnly.OwnerCalls,AllOwnerHookMs=allMs,TargetOnlyHookMs=filteredMs,StopwatchFrequency=Stopwatch.Frequency,AuthorityBytesEqual=true,TargetDecisionsEqual=true}));
        }

        [Test]
        public void C9_NaturalCheckpoint12000_AccumulatesAndPaysHeldTechnologyBranch()
        {
            const string targetOwner="ai-4";const int horizon=18000;
            long Available(AiBudgetState state)=>Math.Max(0,(long)state.Liquid-state.SafetyReserve-state.Reserved.Sum(e=>(long)e.Amount)-state.Unpaid.Sum(e=>(long)e.Amount));
            var root=Root();var project=JObject.Parse(File.ReadAllText(Path.Combine(root,"workflow/project.json")));var planningRoot=(string)project["planning_root"];
            var inputDir=Path.Combine(planningRoot,"evidence/native-strategic-ai/o6-next-inputs-20261012");var manifest=JObject.Parse(File.ReadAllText(Path.Combine(inputDir,"inputs.json")));
            Assert.AreEqual("5c0b2b25383325b89f800f220941da4fcb7ec1df",(string)manifest["source"]);Assert.AreEqual("30e6ab7de534856dd3c425523426476489eb7fe6",(string)manifest["proofCommit"]);
            byte[] bytes;using(var zip=ZipFile.OpenRead(Path.Combine(inputDir,"inputs.zip")))using(var stream=zip.GetEntry("7108-checkpoint-12000.world").Open())using(var copy=new MemoryStream()){stream.CopyTo(copy);bytes=copy.ToArray();}
            Assert.AreEqual("d7bb1afa0a16a5f31efdf0e10e5b4136118eecd206f53b36751e4191c76ebee9",Wire.Hash(bytes));
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,7108,participants);var authority=PlayableAuthorityTick.RestoreBytes(bytes,config);
            AiTechnologyPlan firstPlan=null;AiBudgetState firstBudget=null;long firstDecision=0;bool partialHoldSeen=false,fullHoldSeen=false;AiBudgetState finalBudget=null;PlayableAiObservation finalObservation=null;
            long lastLogTick=authority.Tick;
            string output=Path.Combine(root,".local/qa/o6-funding-action-mismatch/natural-payment.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,string.Empty);
            try
            {
                Assert.AreEqual(12000,authority.Tick);CollectionAssert.AreEqual(bytes,authority.CaptureBytes(),"exact C9 restore, no injections");
                PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=targetOwner;PlayableAiOwnerLoop.PreselectionReadbackForTests=x=>
                {
                    if(x.Observation.OwnerId!=targetOwner)return;
                    var scheduler=Get(authority,"scheduler");var owner=((IEnumerable)Get(scheduler,"owners")).Cast<object>().Single(y=>(string)y.GetType().GetProperty("OwnerId",F).GetValue(y)==targetOwner);
                    var plan=(AiTechnologyPlan)Get(owner,"TechnologyPlan");var ledger=((AiBudgetLedger)Get(owner,"budget")).Capture();
                    var held=ledger.Reserved.Where(AiTechnologyFunding.IsFunding).ToArray();partialHoldSeen|=held.Any(e=>e.Amount>0&&e.Amount<(int)profile.ShkvalGuidanceCost);
                    fullHoldSeen|=held.Any(e=>e.Id.Contains(PlayableResearchKind.ShkvalGuidance.ToString())&&e.Amount>=(int)profile.ShkvalGuidanceCost);
                    if(firstPlan==null){firstPlan=plan;firstBudget=x.Ledger;firstDecision=x.Observation.Tick;}
                };
                using(var host=new UnityHostRouteService())
                for(int i=0;i<horizon-12000&&!authority.WinnerTeam.HasValue;i++)
                {
                    bool advanced=false;for(int attempt=0;attempt<512;attempt++){if(authority.TryAdvance()){advanced=true;break;}host.Service(authority,256);}Assert.True(advanced,"ordinary route barrier at tick "+authority.Tick);
                    if(authority.Tick%45!=21&&i<horizon-12001)continue;
                    finalObservation=PlayableAiObservation.From(authority.ParticipantView(targetOwner));finalBudget=authority.CaptureDiagnosticBudget(targetOwner);
                    var holds=finalBudget.Reserved.Where(AiTechnologyFunding.IsFunding).ToArray();partialHoldSeen|=holds.Any(e=>e.Amount>0&&e.Amount<(int)profile.ShkvalGuidanceCost);
                    fullHoldSeen|=holds.Any(e=>e.Id.Contains(PlayableResearchKind.ShkvalGuidance.ToString())&&e.Amount>=(int)profile.ShkvalGuidanceCost);
                    bool paid=finalBudget.Paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueResearch&&e.Action.ResearchKind==PlayableResearchKind.ShkvalGuidance);
                    if(authority.Tick-lastLogTick>=150||paid)
                    {
                        File.AppendAllText(output,JsonConvert.SerializeObject(new{authority.Tick,finalObservation.Credits,Available=Available(finalBudget),Reserved=holds.Select(e=>new{e.Id,e.Amount}),PaidResearch=finalBudget.Paid.Where(e=>e.Action.Kind==PlayableCommandKind.QueueResearch).Select(e=>new{e.Action.ResearchKind,e.Amount,e.CreatedTick})})+Environment.NewLine);
                        lastLogTick=authority.Tick;
                    }
                    if(paid)break;
                }
                Assert.NotNull(firstPlan,"natural owner decision after restoring tick 12000");Assert.AreEqual(12021,firstDecision);
                Assert.AreEqual(424,firstBudget.Liquid);Assert.Zero(Available(firstBudget));
                Assert.AreEqual(424,firstBudget.Reserved.Single(AiTechnologyFunding.IsFunding).Amount);
                Assert.AreEqual(PlayableResearchKind.ShkvalGuidance,firstPlan.FundingGoal.Research);Assert.IsNull(firstPlan.Action);
                Assert.True(partialHoldSeen,"natural continuation observed an actual partial hold");Assert.True(fullHoldSeen,"natural income filled the held Shkval research amount by original horizon tick 18000");
                Assert.True(finalBudget.Paid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueResearch&&e.Action.ResearchKind==PlayableResearchKind.ShkvalGuidance),"natural held Shkval research was actually paid by original horizon tick 18000");
                TestContext.WriteLine("O6_NATURAL_TECHNOLOGY_PAYMENT "+JsonConvert.SerializeObject(new{firstDecision,authority.Tick,partialHoldSeen,fullHoldSeen,Final=finalObservation,LastBudget=finalBudget,Output=output}));
            }
            finally{PlayableAiOwnerLoop.PreselectionReadbackForTests=null;PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=null;authority.Stop();}
        }
    }
}

namespace Spacewars.Tests.EditMode
{
    // Separate fixture names keep this bounded causal readback selectable without
    // rerunning the older six-input and long natural-payment diagnostics above.
    public sealed class NativeAiO6CurrentExpansionMatrixTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly System.Collections.Generic.HashSet<string> Openings=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        private static string OutputPath;private static int MatrixRows,PayableFreshRows,LegalCurrentRows,LegalIgnoringTombstoneRows,StrictLegalPayableFreshRows;
        private sealed class NaturalCase
        {
            public int Seed,Tick;public string Owner,Entry,Hash;
        }
        private static object Get(object value,string name)
        {var field=value.GetType().GetField(name,F);return field!=null?field.GetValue(value):value.GetType().GetProperty(name,F).GetValue(value);}
        private static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,F).Invoke(value,args);
        private static string Root()
        {var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static string Sha(byte[] bytes)=>Wire.Hash(bytes);
        private static PlayableAuthorityTick Advance(PlayableAuthorityTick authority,UnityHostRouteService host)
        {
            for(int attempt=0;attempt<512;attempt++)
            {if(authority.TryAdvance())return authority;host.Service(authority,64);}
            Assert.Fail("ordinary typed-route barrier timed out at tick "+authority.Tick);return authority;
        }
        private static object Action(PlayableAiAction a)=>a==null?null:new{a.ActionId,a.Kind,a.SiteId,a.BuildingKind,Actors=a.CopyEntityIds(),a.Target};
        private static object Budget(AiBudgetState b)=>new{b.OwnerId,b.Generation,b.Liquid,b.SafetyReserve,b.PaidTotal,b.RepairPaidTotal,
            Reserved=b.Reserved.Select(e=>new{e.Id,e.Purpose,Amount=e.Amount,e.CreatedTick,e.ExpiresTick,Action=Action(e.Action),e.Terms}),
            Unpaid=b.Unpaid.Select(e=>new{e.Id,e.Purpose,Amount=e.Amount,e.CreatedTick,e.ExpiresTick,Action=Action(e.Action),e.Terms}),
            Paid=b.Paid.Select(e=>new{e.Id,e.Purpose,Amount=e.Amount,e.CreatedTick,e.ExpiresTick,Action=Action(e.Action),e.Terms})};
        private static bool ArmyClaimed(AiArmyRegistryState s,int id)=>s.Armies.Any(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.Members.Contains(id)||a.Reinforcements.Contains(id)));
        internal static JObject BuildMatrix(PlayableAiPreselectionReadback c,object owner,PlayableProfile profile,AiArmyRegistryState armies)
        {
            var o=c.Observation;var registry=(AiArmyRegistry)Get(owner,"armies");var finance=Get(owner,"expansionFinancial");var arbiter=(AiDecisionArbiter)Get(owner,"arbiter");
            var budget=(AiBudgetLedger)Get(owner,"budget");var pending=((IEnumerable)Get(owner,"pendingItems")).Cast<object>().ToArray();
            var pendingRows=pending.Select(p=>new{Policy=Get(p,"PolicyName"),ObservationTick=Get(p,"ObservationTick"),DueTick=Get(p,"DueTick"),Action=Action((PlayableAiAction)Get(p,"Action"))}).ToArray();
            var pendingActorIds=pending.SelectMany(p=>((PlayableAiAction)Get(p,"Action")).CopyEntityIds()).ToArray();
            bool ActualFreshActor(int id)=>registry.ArmyFor(id)==0&&!pendingActorIds.Contains(id);
            bool Expired(TerritorySiteSnapshot s)=>s!=null&&(bool)Call(finance,"IsExpiredFingerprint",s,arbiter);
            var scoutClaims=armies.Armies.Where(a=>a.Role==AiArmyRole.Scout&&a.TacticalOwner==AiScoutPlanner.Policy&&a.Phase!=AiArmyPhase.Disbanded)
                .Select(a=>new{a.Id,a.Role,a.Phase,a.TacticalOwner,a.Members,a.Reinforcements,a.Objective}).ToArray();
            var explorers=o.Entities.Where(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0).OrderBy(u=>u.Id)
                .Select(u=>new{u.Id,u.Kind,u.Health,u.Moving,u.Position,CurrentOrder=u.CurrentOrder==null?null:new{u.CurrentOrder.Kind,u.CurrentOrder.TargetId,u.CurrentOrder.Destination,u.CurrentOrder.IssuedTick},
                    Army=armies.Armies.FirstOrDefault(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.Members.Contains(u.Id)||a.Reinforcements.Contains(u.Id)))==null?null:
                        (object)new{Army=armies.Armies.First(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.Members.Contains(u.Id)||a.Reinforcements.Contains(u.Id))).Id,
                            Role=armies.Armies.First(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.Members.Contains(u.Id)||a.Reinforcements.Contains(u.Id))).Role,
                            TacticalOwner=armies.Armies.First(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.Members.Contains(u.Id)||a.Reinforcements.Contains(u.Id))).TacticalOwner},
                    Pending=c.PendingActors.Contains(u.Id),ScoutClaim=scoutClaims.Any(a=>a.Members.Contains(u.Id)||a.Reinforcements.Contains(u.Id))}).ToArray();
            var publicSites=o.Sites.OrderBy(s=>s.Site.Id).Select(s=>new{Site=s.Site.Id,Kind=s.Site.Kind,Owner=s.Owner,Claimant=s.Claimant,s.CenterId,s.Contested,s.Progress,ExpiredFingerprint=Expired(s),
                PublicObjectives=o.PublicScoutObjectives.Where(g=>g.SiteId==s.Site.Id).Select(g=>new{g.Role,g.Reachable,g.Approach}).ToArray()}).ToArray();
            var pairs=new System.Collections.Generic.List<object>();bool anyFullyPayableFreshCandidate=false,anyLegalCurrentCandidate=false,anyLegalIgnoringTombstone=false,anyStrictLegalPayableFreshIgnoringTombstone=false;
            foreach(var actor in o.Entities.Where(u=>u.Owner==o.Owner&&u.Kind==PlayableEntityKind.Explorer&&u.Health>0).OrderBy(u=>u.Id))
            foreach(var site in o.Sites.OrderBy(s=>s.Site.Id))
            {
                var objective=o.PublicScoutObjectives.FirstOrDefault(g=>g.SiteId==site.Site.Id&&g.Reachable&&g.Role!=PlayablePublicScoutObjectiveRole.PossibleEnemyStart);
                var route=o.RouteProofs.FirstOrDefault(r=>r.UnitId==actor.Id&&r.TargetId==site.Site.Id&&r.Kind==PlayableRouteTargetKind.PublicObjective);
                var army=armies.Armies.FirstOrDefault(a=>a.Phase!=AiArmyPhase.Disbanded&&(a.Members.Contains(actor.Id)||a.Reinforcements.Contains(actor.Id)));
                bool alive=actor.Health>0;bool idle=alive&&!actor.Moving&&actor.CurrentOrder==null;bool fresh=ActualFreshActor(actor.Id);bool claimed=ArmyClaimed(armies,actor.Id);
                bool targetKind=(site.Site.Kind==PlayableBuildingKind.Mine||site.Site.Kind==PlayableBuildingKind.Outpost)&&TerritoryRules.Income(profile,site.Site.Kind)>0;
                int buildCost=targetKind?TerritoryRules.Cost(profile,site.Site.Kind):0;bool noCenter=site.CenterId==0;bool uncontested=!site.Contested;
                bool ownerAllowed=!site.Owner.HasValue||site.Owner==o.Owner;bool claimantAllowed=!site.Claimant.HasValue||site.Claimant==o.Owner;
                bool creditsCover=o.Credits>=buildCost;bool objectiveValid=objective!=null;bool routeValid=route!=null;bool routeCurrent=route!=null&&route.Generation==o.Generation&&route.Tick==o.Tick;
                bool safe=routeValid&&targetKind&&AiExpansionPlanner.Safe(o,profile,AiProfile.Initial,site,actor,route);
                bool tombstone=Expired(site);bool isFinanceSite=c.FinanceSite==site.Site.Id;
                bool continuing=c.ExpansionState.SiteId==site.Site.Id&&c.ExpansionState.ActorId==actor.Id;
                bool actualActionMatch=c.ActualExpansionAction!=null&&c.ActualExpansionAction.SiteId==site.Site.Id&&c.ActualExpansionAction.CopyEntityIds().SequenceEqual(new[]{actor.Id});
                bool actualProposalMatch=actualActionMatch&&c.ActualExpansionProposal!=null&&c.ActualExpansionProposal.ArmyRejection==null;
                var suppressionFork=c.ExpansionProbe.Fork();bool suppressed=suppressionFork.Suppressed(site,actor,o,profile,AiProfile.Initial);
                JObject Probe(string remove)
                {
                    var fork=c.ExpansionProbe.Fork();
                    if(remove=="failed-site suppression")((System.Collections.IDictionary)Get(fork,"failed")).Clear();
                    Func<int,bool> actorAllowed=id=>id==actor.Id&&(remove=="army/pending freshness"||ActualFreshActor(id));
                    Func<int,bool> siteAllowed=id=>id==site.Site.Id&&
                        (continuing||remove=="active financial site"||!isFinanceSite||c.FinanceReady)&&
                        (continuing||remove=="expiry tombstone"||!Expired(site));
                    int financialFilter=isFinanceSite&&c.FinanceReady&&remove!="active financial site"?site.Site.Id:0;
                    var action=fork.TryPlan(o,profile,AiProfile.Initial,financialFilter,actorAllowed,siteAllowed);
                    bool exact=action!=null&&action.SiteId==site.Site.Id&&action.CopyEntityIds().SequenceEqual(new[]{actor.Id});
                    bool financePass=continuing||remove=="active financial site"||!isFinanceSite||c.FinanceReady;
                    bool tombstonePass=continuing||remove=="expiry tombstone"||!tombstone;
                    bool suppressionPass=continuing||remove=="failed-site suppression"||!suppressed;
                    bool actorPass=continuing||remove=="army/pending freshness"||fresh;
                    if(!exact)return JObject.FromObject(new{Action=(object)null,FullyPayable=false,FreshActor=fresh,FullyPayableFresh=false,ArmyRejection=(string)null,
                        IntentExpiryContext=(long?)null,FinancePass=financePass,TombstonePass=tombstonePass,SuppressionPass=suppressionPass,LegalCandidate=false,ActualProposalMatch=actualProposalMatch});
                    var intent=(AiIntent)Call(owner,"Proposal","expansion",action,o,1);
                    bool fullyPayable=intent!=null&&AiExpansionFinancial.FullyFundable(budget,c.ProtectedFunds,intent);
                    string armyRejection=intent==null?null:registry.Reject(intent);
                    long? expiresTick=intent==null?(long?)null:intent.ExpiresTick;
                    bool fullFundingRequired=!continuing&&!(isFinanceSite&&c.FinanceReady);
                    bool legal=intent!=null&&armyRejection==null&&(!fullFundingRequired||fullyPayable)&&actorPass&&idle&&safe&&routeValid&&financePass&&tombstonePass&&suppressionPass&&
                        !c.Threatened&&c.AllowsExpansion&&!c.PendingExpansion;
                    return JObject.FromObject(new{Action=Action(action),FullyPayable=fullyPayable,FreshActor=fresh,FullyPayableFresh=fullyPayable&&fresh,ArmyRejection=armyRejection,
                        IntentExpiryContext=expiresTick,FullFundingRequired=fullFundingRequired,FinancePass=financePass,TombstonePass=tombstonePass,SuppressionPass=suppressionPass,LegalCandidate=legal,ActualProposalMatch=actualProposalMatch,
                        Intent=intent==null?null:new{intent.Credits,intent.StartupFund,intent.ExpiresTick,intent.ReservationId,intent.Priority,intent.Conditions}});
                }
                JObject actual=Probe(null);JObject withoutFreshness=Probe("army/pending freshness");JObject withoutFinance=Probe("active financial site");
                JObject withoutTombstone=Probe("expiry tombstone");JObject withoutSuppression=Probe("failed-site suppression");
                anyFullyPayableFreshCandidate|=(bool)withoutTombstone["FullyPayableFresh"];
                anyLegalCurrentCandidate|=(bool)actual["LegalCandidate"];
                anyLegalIgnoringTombstone|=(bool)withoutTombstone["LegalCandidate"];
                anyStrictLegalPayableFreshIgnoringTombstone|=(bool)withoutTombstone["LegalCandidate"]&&(bool)withoutTombstone["FullyPayableFresh"]&&!continuing;
                var failures=new System.Collections.Generic.List<string>();
                if(!targetKind)failures.Add("site kind/income");if(!noCenter)failures.Add("center already present");if(!uncontested)failures.Add("site contested");
                if(!ownerAllowed)failures.Add("site owner not public-claimable");if(!claimantAllowed)failures.Add("claimant not owner");if(!creditsCover)failures.Add("raw build cost exceeds credits");
                if(!idle)failures.Add("Explorer is not alive and idle");if(!fresh)failures.Add("Explorer belongs to army membership or pending action");
                if(!objectiveValid)failures.Add("no reachable public non-enemy-start objective");if(!routeValid)failures.Add("no typed route proof for this actor/site");
                if(routeValid&&!safe)failures.Add("unsafe route/site");if(routeValid&&!routeCurrent)failures.Add("typed route proof tick/generation is not current (context only; TryPlan requires matching route)");if(isFinanceSite&&!c.FinanceReady&&!continuing)failures.Add("active financial site excluded from ordinary fallback");
                if(tombstone&&!continuing)failures.Add("unchanged expiry tombstone");if(suppressed&&!continuing)failures.Add("failed-site condition suppression");
                if(c.Threatened)failures.Add("home threat blocks expansion policy");
                if(!c.AllowsExpansion)failures.Add("opening phase does not allow expansion");if(c.PendingExpansion)failures.Add("expansion action already pending");
                if(c.ExpansionState.SiteId!=0&&!continuing)failures.Add("different existing expansion commitment");
                pairs.Add(new{Actor=actor.Id,Site=site.Site.Id,SiteKind=site.Site.Kind,BuildCost=buildCost,Income=targetKind?TerritoryRules.Income(profile,site.Site.Kind):0,
                    Predicates=new{CurrentVisibleSite=true,targetKind,noCenter,uncontested,ownerAllowed,claimantAllowed,creditsCover,alive,idle,fresh,ReadbackFreshActorApproximation=c.FreshActor(actor.Id),armyClaim=claimed,ArmyId=army?.Id,ArmyRole=army?.Role,ArmyTacticalOwner=army?.TacticalOwner,
                        ObjectivePresent=objectiveValid,ObjectiveRole=objective?.Role,ObjectiveReachable=objective?.Reachable,RoutePresent=routeValid,RouteCurrent=routeCurrent,Safe=safe,
                        FinanceSite=isFinanceSite,FinanceReady=c.FinanceReady,ExpiredTombstone=tombstone,FailedConditionSuppressed=suppressed,Threatened=c.Threatened,UrgentRepair=c.UrgentRepair,OpeningAllowsExpansion=c.AllowsExpansion,
                        PendingExpansion=c.PendingExpansion,AvailableActions=c.AvailableActions,ExistingCommitment=c.ExpansionState.SiteId!=0},
                    ActualFork=actual,SingleGuardRemoval=new{ArmyPendingFreshness=withoutFreshness,ActiveFinancialSite=withoutFinance,ExpiryTombstone=withoutTombstone,FailedSiteSuppression=withoutSuppression},
                    FullyPayableFreshIgnoringTombstone=withoutTombstone["FullyPayableFresh"],LegalCurrentCandidate=actual["LegalCandidate"],
                    LegalIgnoringTombstone=withoutTombstone["LegalCandidate"],ActualProposalMatch=actualProposalMatch,Continuation=continuing,
                    UrgentRepairContextOnly=c.UrgentRepair,RouteCurrentContextOnly=routeCurrent,AvailableActionsSelectionStageContext=c.AvailableActions,FailureReasons=failures});
            }
            var financeMemo=Call(arbiter,"SiteFundingMemory");
            var production=(PlayableAiProductionLivenessPolicy)Get(owner,"production");var productionFork=production.Fork();
            var legalExplorerRequest=productionFork.TryPlan(o,PlayableEntityKind.Explorer);
            var economyPlan=(AiEconomyPlan)Get(owner,"EconomyPlan");
            var actualProductionCandidates=economyPlan.Candidates.Where(x=>x.Policy=="production").Select(x=>new{ x.Policy,x.Legal,x.Priority,x.Reason,Action=Action(x.Action),Admission=AiEconomyAdmission.Reject(o,profile,x.Action,true)}).ToArray();
            return JObject.FromObject(new{Owner=o.OwnerId,Seed=o.Seed,Tick=o.Tick,Generation=o.Generation,
                Ledger=Budget(c.Ledger),c.ProtectedFunds,AvailableBeforeProtection=Math.Max(0,(long)c.Ledger.Liquid-c.Ledger.SafetyReserve-c.Ledger.Reserved.Sum(e=>(long)e.Amount)-c.Ledger.Unpaid.Sum(e=>(long)e.Amount)),
                FinanceSite=c.FinanceSite,c.FinanceReady,FinanceMemo=financeMemo,c.Threatened,c.UrgentRepair,c.AllowsExpansion,c.PendingExpansion,c.AvailableActions,
                c.LastDecisionTick,c.DecisionIntervalTicks,c.ObservationDelayTicks,
                ExpansionState=c.ExpansionState,ActualExpansionAction=Action(c.ActualExpansionAction),ActualExpansionProposal=c.ActualExpansionProposal,
                CounterfactualAlternative=Action(c.CounterfactualAlternative),CounterfactualProposal=c.CounterfactualProposal,Explorers=explorers,ScoutClaims=scoutClaims,Armies=armies,
                PublicSites=publicSites,PublicObjectives=o.PublicScoutObjectives,TypedRoutes=o.RouteProofs.Where(r=>r.Kind==PlayableRouteTargetKind.PublicObjective),Knowledge=c.Knowledge,
                PendingBeforeSelection=pendingRows,OwnerBuildings=o.Buildings.Where(b=>b.Owner==o.Owner).Select(b=>new{b.Id,b.Kind,b.Phase,b.Health,b.SiteId,b.SlotId,PrivateState=b.PrivateState}),
                EconomyPlan=economyPlan,CompositionAssessment=Get(owner,"compositionAssessment"),ProductionState=Get(owner,"production"),
                ExplorerProductionRequest=Action(legalExplorerRequest),ExplorerProductionAdmission=legalExplorerRequest==null?null:AiEconomyAdmission.Reject(o,profile,legalExplorerRequest,true),ActualLegalProductionCandidates=actualProductionCandidates,
                TechnologyPlan=Get(owner,"TechnologyPlan"),
                EveryLivingExplorerSitePair=pairs,AnyFullyPayableFreshCandidate=anyFullyPayableFreshCandidate,
                AnyLegalCurrentCandidate=anyLegalCurrentCandidate,AnyLegalIgnoringTombstone=anyLegalIgnoringTombstone,
                    AnyStrictLegalPayableFreshIgnoringTombstone=anyStrictLegalPayableFreshIgnoringTombstone,
                    TombstoneConclusionPermitted=anyStrictLegalPayableFreshIgnoringTombstone});
        }

        internal static JObject BuildMatrixForNoninterference(PlayableAiPreselectionReadback context,object owner,PlayableProfile profile)
        {return BuildMatrix(context,owner,profile,((AiArmyRegistry)Get(owner,"armies")).Capture());}

        [OneTimeSetUp]
        public void PrepareOutput()
        {
            var root=Root();var archivePath=Path.Combine(root,"docs/native-ai/o6/o6-natural-topic-merge-source-compiled-raw.zip");
            Assert.AreEqual("5b7de68422093403ad1215bbd382c81ba5f771a3e643a2b0abd31159a29057d1",Wire.Hash(File.ReadAllBytes(archivePath)),"source archive exact commit 9e44aaa4");
            OutputPath=Path.Combine(root,".local/o6-current-expansion-matrix/matrix.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));File.WriteAllText(OutputPath,string.Empty);
            Openings.Clear();MatrixRows=PayableFreshRows=LegalCurrentRows=LegalIgnoringTombstoneRows=StrictLegalPayableFreshRows=0;
        }
        [OneTimeTearDown]
        public void RequireInputOpeningCoverage()
        {
            CollectionAssert.IsSubsetOf(new[]{"GreedyMine","Safe","ExplorerAllIn","BlindRush"},Openings,"selected natural inputs must cover the four requested opening families");
            TestContext.WriteLine("O6_CURRENT_EXPANSION_MATRIX_RESULT "+JsonConvert.SerializeObject(new{Output=OutputPath,Inputs=10,Rows=MatrixRows,Openings,
                AnyFullyPayableFreshCandidate=PayableFreshRows>0,AnyLegalCurrentCandidate=LegalCurrentRows>0,AnyLegalIgnoringTombstone=LegalIgnoringTombstoneRows>0,
                AnyStrictLegalPayableFreshIgnoringTombstone=StrictLegalPayableFreshRows>0,
                TombstoneConclusionPermitted=StrictLegalPayableFreshRows>0,NoTombstoneConclusion=StrictLegalPayableFreshRows==0}));
        }

        [TestCase(7108,"ai-0",900,"7108-checkpoint-900.world","64d03d029c82c0c06ac198044db52334a49163a8816b52da499daaa80933cdb7")]
        [TestCase(7108,"ai-0",8400,"7108-checkpoint-8400.world","807aea40a50e6a95076c3a91b527acc4b74a6dd12ddf33c5cbd311fa265a124e")]
        [TestCase(7108,"ai-4",6300,"7108-checkpoint-6300.world","72024f9d7194942bd98429ac2aadd5ea4b5842413f7f70c3059c14266d0eac80")]
        [TestCase(7109,"ai-1",9300,"7109-checkpoint-9300.world","6c4d68fb7dd6468a672a9a3baf241b79084125ed5844d88b9957d5db639b79f3")]
        [TestCase(7109,"ai-0",900,"7109-checkpoint-900.world","521f60dca8e6d17226c0fdc7ca245858bd675818744b962efc7d5ade6d4323b8")]
        [TestCase(7109,"ai-0",10200,"7109-checkpoint-10200.world","243272f474f358dfb76524d31f208fcba1dd574b2716b356b5002aff7a020a88")]
        [TestCase(7108,"ai-6",9900,"7108-checkpoint-9900.world","ea2c520677d8ea2ab85d74e4af5c49c26fefcf1898f78c361ace583e11110025")]
        [TestCase(7109,"ai-7",5700,"7109-checkpoint-5700.world","2b03664b51402904b4f222f11f190fb00174d035c263d6f2d1b70bcb8dc86a3c")]
        [TestCase(7109,"ai-2",11400,"7109-checkpoint-11400.world","ed1ea7a96d5c18edcfd337df68b21eb6d0dc1da4302ab65e8c4810179e07b98c")]
        [TestCase(7109,"ai-6",6600,"7109-checkpoint-6600.world","09f45112255df63dfff1059efff8160ff40fc4db431117a63efef8686575022d")]
        public void NaturalCheckpoint_EnumeratesEveryPreselectionExplorerSitePredicate(int seed,string ownerId,int checkpointTick,string entryName,string inputHash)
        {
            var item=new NaturalCase{Seed=seed,Owner=ownerId,Tick=checkpointTick,Entry=entryName,Hash=inputHash};
            var root=Root();var archivePath=Path.Combine(root,"docs/native-ai/o6/o6-natural-topic-merge-source-compiled-raw.zip");
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            using(var zip=ZipFile.OpenRead(archivePath))
            {
                var member=".local/o6-natural/"+item.Entry;var entry=zip.GetEntry(member);Assert.NotNull(entry,"durable natural input "+member);byte[] bytes;
                using(var stream=entry.Open())using(var copy=new MemoryStream()){stream.CopyTo(copy);bytes=copy.ToArray();}
                Assert.AreEqual(item.Hash,Wire.Hash(bytes),"exact world SHA-256 "+item.Entry);
                var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,item.Seed,participants);Assert.AreEqual(500,profile.StartingCredits);Assert.AreEqual(8,config.Roster.Count);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
                var authority=PlayableAuthorityTick.RestoreBytes(bytes,config);var captured=new System.Collections.Generic.List<JObject>();
                try
                {
                    Assert.AreEqual(item.Tick,authority.Tick);CollectionAssert.AreEqual(bytes,authority.CaptureBytes(),"exact restore before natural continuation");
                    var initial=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==item.Owner);Assert.AreEqual(item.Seed,initial.Seed);Openings.Add(initial.Opening.Opening.ToString());
                    PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=item.Owner;
                    PlayableAiOwnerLoop.PreselectionReadbackForTests=c=>
                    {
                        if(c.Observation.OwnerId!=item.Owner||c.Observation.Tick<=item.Tick||c.Observation.Tick>item.Tick+45)return;
                        var before=authority.CaptureBytes();var scheduler=Get(authority,"scheduler");var owner=((IEnumerable)Get(scheduler,"owners")).Cast<object>().Single(x=>(string)Get(x,"OwnerId")==item.Owner);
                        var registry=(AiArmyRegistry)Get(owner,"armies");var row=BuildMatrix(c,owner,profile,registry.Capture());
                        CollectionAssert.AreEqual(before,authority.CaptureBytes(),"all candidate probes operate on detached planner forks");
                        row["DecisionTick"]=c.Observation.Tick;row["InputEntry"]=item.Entry;row["ArchiveMember"]=member;row["InputSHA256"]=item.Hash;row["CheckpointTick"]=item.Tick;row["Seed"]=item.Seed;row["Opening"]=initial.Opening.Opening.ToString();row["PreselectionBeforeArbiter"]=true;row["ProbeAuthorityBytesEqual"]=true;
                        captured.Add(row);
                    };
                    using(var host=new UnityHostRouteService())
                    for(int tick=0;tick<45;tick++)
                    {
                        int before=captured.Count;Advance(authority,host);
                        var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==item.Owner);
                        for(int i=before;i<captured.Count;i++)
                        {
                            var row=captured[i];row["PostselectionDecision"]=checkpoint.EconomyDecision==null?JValue.CreateNull():JToken.FromObject(new{checkpoint.EconomyDecision.Tick,checkpoint.EconomyDecision.Ordinal,
                                Selected=checkpoint.EconomyDecision.Candidates.Where(x=>x.Selected).Select(x=>new{x.Policy,x.Kind,x.EntityIds,x.SiteId,x.BuildingKind,x.UnitKind,x.Credits,x.Reason}).ToArray(),checkpoint.EconomyDecision.Blockers});
                            row["ActualContinuation"]="unmodified C9 natural authority";File.AppendAllText(OutputPath,row.ToString(Formatting.None)+Environment.NewLine);MatrixRows++;
                            if((bool)row["AnyFullyPayableFreshCandidate"])PayableFreshRows++;
                            if((bool)row["AnyLegalCurrentCandidate"])LegalCurrentRows++;
                            if((bool)row["AnyLegalIgnoringTombstone"])LegalIgnoringTombstoneRows++;
                            if((bool)row["AnyStrictLegalPayableFreshIgnoringTombstone"])StrictLegalPayableFreshRows++;
                        }
                    }
                    Assert.AreEqual(item.Tick+45,authority.Tick,"ordinary 45-tick natural continuation");Assert.Greater(captured.Count,0,"actual preselection readback within bounded horizon for "+item.Owner);
                    Assert.IsTrue(new[]{"GreedyMine","Safe","ExplorerAllIn","BlindRush"}.Contains(initial.Opening.Opening.ToString()),"selected owner must be one of the four requested opening families");
                    Assert.AreEqual(item.Entry,File.ReadAllLines(OutputPath).Select(x=>JObject.Parse(x)["InputEntry"].Value<string>()).LastOrDefault(x=>x==item.Entry),"preselection matrix row saved for input");
                    TestContext.WriteLine("O6_CURRENT_EXPANSION_MATRIX "+JsonConvert.SerializeObject(new{item.Seed,item.Owner,item.Tick,Opening=initial.Opening.Opening.ToString(),Decisions=captured.Count,EndTick=authority.Tick}));
                }
                finally{PlayableAiOwnerLoop.PreselectionReadbackForTests=null;PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=null;authority.Stop();}
            }
        }

    }

    public sealed class NativeAiO6ExpansionProbeNoninterferenceTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static object Get(object value,string name)
        {var field=value.GetType().GetField(name,F);return field!=null?field.GetValue(value):value.GetType().GetProperty(name,F).GetValue(value);}
        private static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,F).Invoke(value,args);
        private static PlayableAuthorityTick Advance(PlayableAuthorityTick authority,UnityHostRouteService host)
        {for(int attempt=0;attempt<512;attempt++){if(authority.TryAdvance())return authority;host.Service(authority,64);}Assert.Fail("ordinary typed-route barrier at "+authority.Tick);return authority;}
        private static object Action(PlayableAiAction a)=>a==null?null:new{a.ActionId,a.Kind,a.SiteId,a.BuildingKind,Actors=a.CopyEntityIds(),a.Target};
        private static string Root()
        {var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        [TestCase(7108,"ai-0",900,"7108-checkpoint-900.world","64d03d029c82c0c06ac198044db52334a49163a8816b52da499daaa80933cdb7")]
        [TestCase(7108,"ai-0",8400,"7108-checkpoint-8400.world","807aea40a50e6a95076c3a91b527acc4b74a6dd12ddf33c5cbd311fa265a124e")]
        [TestCase(7108,"ai-4",6300,"7108-checkpoint-6300.world","72024f9d7194942bd98429ac2aadd5ea4b5842413f7f70c3059c14266d0eac80")]
        [TestCase(7109,"ai-1",9300,"7109-checkpoint-9300.world","6c4d68fb7dd6468a672a9a3baf241b79084125ed5844d88b9957d5db639b79f3")]
        [TestCase(7109,"ai-0",900,"7109-checkpoint-900.world","521f60dca8e6d17226c0fdc7ca245858bd675818744b962efc7d5ade6d4323b8")]
        [TestCase(7109,"ai-0",10200,"7109-checkpoint-10200.world","243272f474f358dfb76524d31f208fcba1dd574b2716b356b5002aff7a020a88")]
        [TestCase(7108,"ai-6",9900,"7108-checkpoint-9900.world","ea2c520677d8ea2ab85d74e4af5c49c26fefcf1898f78c361ace583e11110025")]
        [TestCase(7109,"ai-7",5700,"7109-checkpoint-5700.world","2b03664b51402904b4f222f11f190fb00174d035c263d6f2d1b70bcb8dc86a3c")]
        [TestCase(7109,"ai-2",11400,"7109-checkpoint-11400.world","ed1ea7a96d5c18edcfd337df68b21eb6d0dc1da4302ab65e8c4810179e07b98c")]
        [TestCase(7109,"ai-6",6600,"7109-checkpoint-6600.world","09f45112255df63dfff1059efff8160ff40fc4db431117a63efef8686575022d")]
        public void Natural45Ticks_HookOffAndFullForkMatrixHookOnKeepBytesAndDecisionsEqual(int seed,string ownerId,int checkpointTick,string entryName,string inputHash)
        {
            var root=Root();var path=Path.Combine(root,"docs/native-ai/o6/o6-natural-topic-merge-source-compiled-raw.zip");
            Assert.AreEqual("5b7de68422093403ad1215bbd382c81ba5f771a3e643a2b0abd31159a29057d1",Wire.Hash(File.ReadAllBytes(path)),"source archive exact commit 9e44aaa4");byte[] bytes;
            using(var zip=ZipFile.OpenRead(path))using(var stream=zip.GetEntry(".local/o6-natural/"+entryName).Open())using(var copy=new MemoryStream()){stream.CopyTo(copy);bytes=copy.ToArray();}
            Assert.AreEqual(inputHash,Wire.Hash(bytes),"exact natural input bytes");var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,seed,participants);
            Assert.AreEqual(500,profile.StartingCredits);Assert.AreEqual(8,config.Roster.Count);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            object[] Run(bool hook)
            {
                var authority=PlayableAuthorityTick.RestoreBytes(bytes,config);var world=new System.Collections.Generic.List<byte[]>();var decisions=new System.Collections.Generic.List<string>();int probes=0;string opening;
                try
                {
                    Assert.AreEqual(checkpointTick,authority.Tick);CollectionAssert.AreEqual(bytes,authority.CaptureBytes(),"exact C9 restore");
                    var initial=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==ownerId);Assert.AreEqual(seed,initial.Seed);opening=initial.Opening.Opening.ToString();
                    PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=hook?ownerId:null;
                    PlayableAiOwnerLoop.PreselectionReadbackForTests=hook?new Action<PlayableAiPreselectionReadback>(c=>
                    {
                        if(c.Observation.OwnerId!=ownerId)return;probes++;var before=authority.CaptureBytes();
                        var scheduler=Get(authority,"scheduler");var owner=((IEnumerable)Get(scheduler,"owners")).Cast<object>().Single(x=>(string)Get(x,"OwnerId")==ownerId);
                        NativeAiO6CurrentExpansionMatrixTests.BuildMatrixForNoninterference(c,owner,profile);
                        CollectionAssert.AreEqual(before,authority.CaptureBytes(),"fork matrix does not mutate C9 before arbitration");
                    }):null;
                    using(var host=new UnityHostRouteService())for(int tick=0;tick<45;tick++)
                    {
                        Advance(authority,host);world.Add(authority.CaptureBytes());
                        var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==ownerId);var e=checkpoint.EconomyDecision;
                        decisions.Add(JsonConvert.SerializeObject(new{checkpoint.DecisionTick,checkpoint.LastPolicy,checkpoint.LastActionKind,checkpoint.PendingActionId,
                            Economy=e==null?null:new{e.Tick,e.Ordinal,e.Liquid,e.Available,e.Reserved,e.Unpaid,e.SafetyReserve,e.Reason,e.Blockers,
                                Candidates=e.Candidates.Select(x=>new{x.Policy,x.Kind,x.EntityIds,x.SiteId,x.SlotId,x.BuildingKind,x.UnitKind,x.Credits,x.Selected,x.Reason}).ToArray()}}));
                    }
                    Assert.AreEqual(checkpointTick+45,authority.Tick,"ordinary host-routed continuation stays exactly 45 ticks");
                    if(hook)Assert.Greater(probes,0,"hook-enabled run had actual target-owner preselection");else Assert.Zero(probes,"hook-off baseline did not invoke the readback");
                    return new object[]{world.ToArray(),decisions.ToArray(),probes,opening};
                }
                finally{PlayableAiOwnerLoop.PreselectionReadbackForTests=null;PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=null;authority.Stop();}
            }
            var off=Run(false);var on=Run(true);var offWorld=(byte[][])off[0];var onWorld=(byte[][])on[0];var offDecisions=(string[])off[1];var onDecisions=(string[])on[1];
            Assert.AreEqual(45,offWorld.Length);Assert.AreEqual(45,onWorld.Length);Assert.AreEqual(45,offDecisions.Length);Assert.AreEqual(45,onDecisions.Length);Assert.AreEqual(off[3],on[3],"same checkpoint opening with hook on/off");
            for(int i=0;i<45;i++)
            {CollectionAssert.AreEqual(offWorld[i],onWorld[i],"exact complete world C9 bytes at tick "+(checkpointTick+i+1));Assert.AreEqual(offDecisions[i],onDecisions[i],"ordinary selected decisions at tick "+(checkpointTick+i+1));}
            TestContext.WriteLine("O6_EXPANSION_HOOK_NONINTERFERENCE "+JsonConvert.SerializeObject(new{seed,ownerId,checkpointTick,entryName,Opening=off[3],Ticks=45,HookOnProbes=on[2],ExactWorldBytesEqual=true,OrdinaryDecisionRowsEqual=true}));
        }

    }
}

namespace Spacewars.Tests.EditMode
{
    // New genuine-rich inputs stay isolated from the original twenty-case proof.
    public sealed class NativeAiO6CurrentExpansionRichFundingProbeTests
    {
        private const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static string OutputPath;
        private sealed class NaturalCase
        {public int Seed,Tick;public string Owner,Opening,Entry,Hash;}
        private sealed class RunResult
        {public byte[][] World;public string[] Decisions;public JArray Rows;public string Opening;public int Probes;}

        private static object Get(object value,string name)
        {var field=value.GetType().GetField(name,F);return field!=null?field.GetValue(value):value.GetType().GetProperty(name,F).GetValue(value);}
        private static string Root()
        {var d=new DirectoryInfo(Directory.GetCurrentDirectory());while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;Assert.NotNull(d);return d.FullName;}
        private static PlayableAuthorityTick Advance(PlayableAuthorityTick authority,UnityHostRouteService host)
        {
            for(int attempt=0;attempt<512;attempt++)
            {if(authority.TryAdvance())return authority;host.Service(authority,64);}
            Assert.Fail("ordinary typed-route barrier timed out at tick "+authority.Tick);return authority;
        }

        [OneTimeSetUp]
        public void PrepareOutput()
        {var root=Root();OutputPath=Path.Combine(root,".local/o6-current-expansion-rich-input/matrix.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));File.WriteAllText(OutputPath,string.Empty);}

        private static RunResult Run(bool hook,NaturalCase c,byte[] bytes,PlayableProfile profile,OfflineMatchConfiguration config)
        {
            var authority=PlayableAuthorityTick.RestoreBytes(bytes,config);var world=new System.Collections.Generic.List<byte[]>();var decisions=new System.Collections.Generic.List<string>();var rows=new JArray();int probes=0;
            try
            {
                Assert.AreEqual(c.Tick,authority.Tick,"exact checkpoint tick");CollectionAssert.AreEqual(bytes,authority.CaptureBytes(),"exact C9 restore before continuation");
                var initial=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==c.Owner);Assert.AreEqual(c.Seed,initial.Seed);var opening=initial.Opening.Opening.ToString();Assert.AreEqual(c.Opening,opening,"selected owner checkpoint opening");
                PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=hook?c.Owner:null;
                PlayableAiOwnerLoop.PreselectionReadbackForTests=hook?new Action<PlayableAiPreselectionReadback>(context=>
                {
                    if(context.Observation.OwnerId!=c.Owner||context.Observation.Tick<=c.Tick||context.Observation.Tick>c.Tick+45)return;
                    var before=authority.CaptureBytes();var scheduler=Get(authority,"scheduler");var owner=((IEnumerable)Get(scheduler,"owners")).Cast<object>().Single(x=>(string)Get(x,"OwnerId")==c.Owner);
                    var row=NativeAiO6CurrentExpansionMatrixTests.BuildMatrixForNoninterference(context,owner,profile);
                    CollectionAssert.AreEqual(before,authority.CaptureBytes(),"actual-state bytes unchanged by all detached fork probes");
                    row["InputEntry"]=c.Entry;row["ArchiveMember"]=".local/o6-natural/"+c.Entry;row["InputSHA256"]=c.Hash;row["CheckpointTick"]=c.Tick;row["Opening"]=opening;
                    row["PreselectionBeforeArbiter"]=true;row["ProbeAuthorityBytesEqual"]=true;rows.Add(row);probes++;
                }):null;
                using(var host=new UnityHostRouteService())
                for(int tick=0;tick<45;tick++)
                {
                    int beforeRows=rows.Count;Advance(authority,host);world.Add(authority.CaptureBytes());
                    var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(x=>x.OwnerId==c.Owner);var decision=checkpoint.EconomyDecision;
                    var postselection=decision==null?JValue.CreateNull():JToken.FromObject(new{decision.Tick,decision.Ordinal,
                        Selected=decision.Candidates.Where(x=>x.Selected).Select(x=>new{x.Policy,x.Kind,x.EntityIds,x.SiteId,x.BuildingKind,x.UnitKind,x.Credits,x.Reason}).ToArray(),decision.Blockers});
                    for(int i=beforeRows;i<rows.Count;i++)
                    {rows[i]["PostselectionDecision"]=postselection.DeepClone();rows[i]["ActualContinuation"]="unmodified natural C9 authority";rows[i]["RelativeTick"]=authority.Tick-c.Tick;}
                    decisions.Add(JsonConvert.SerializeObject(new{checkpoint.DecisionTick,checkpoint.LastPolicy,checkpoint.LastActionKind,checkpoint.PendingActionId,
                        Economy=decision==null?null:new{decision.Tick,decision.Ordinal,decision.Liquid,decision.Available,decision.Reserved,decision.Unpaid,decision.SafetyReserve,decision.Reason,decision.Blockers,
                            Candidates=decision.Candidates.Select(x=>new{x.Policy,x.Kind,x.EntityIds,x.SiteId,x.SlotId,x.BuildingKind,x.UnitKind,x.Credits,x.Selected,x.Reason}).ToArray()}}));
                }
                Assert.AreEqual(c.Tick+45,authority.Tick,"exactly 45 ordinary host-routed ticks");if(hook)Assert.Greater(probes,0,"actual target-owner preselection captured");else Assert.Zero(probes,"hook-off run did not invoke readback");
                return new RunResult{World=world.ToArray(),Decisions=decisions.ToArray(),Rows=rows,Opening=opening,Probes=probes};
            }
            finally{PlayableAiOwnerLoop.PreselectionReadbackForTests=null;PlayableAiOwnerLoop.PreselectionReadbackOwnerFilterForTests=null;authority.Stop();}
        }

        [TestCase(7108,"ai-4",13200,"GreedyMine","7108-checkpoint-13200.world","8bcd607b9f62d3fdbcb71dfec96914c05551f2c3970aa4e29f8751f0f8777d4d")]
        [TestCase(7108,"ai-5",8400,"ExplorerAllIn","7108-checkpoint-8400.world","807aea40a50e6a95076c3a91b527acc4b74a6dd12ddf33c5cbd311fa265a124e")]
        [TestCase(7109,"ai-0",0,"Safe","7109-checkpoint-0.world","b82897f07e22c9e8cc115f1db460e69355e3056f26af5f069ca0b082fb13346b")]
        [TestCase(7109,"ai-2",0,"BlindRush","7109-checkpoint-0.world","b82897f07e22c9e8cc115f1db460e69355e3056f26af5f069ca0b082fb13346b")]
        public void RichNaturalCheckpoint_ActualFundingCandidatesAndHookNoninterference(int seed,string owner,int checkpointTick,string checkpointOpening,string entryName,string inputHash)
        {
            var c=new NaturalCase{Seed=seed,Owner=owner,Opening=checkpointOpening,Tick=checkpointTick,Entry=entryName,Hash=inputHash};var root=Root();var archive=Path.Combine(root,"docs/native-ai/o6/o6-natural-topic-merge-source-compiled-raw.zip");
            Assert.AreEqual("5b7de68422093403ad1215bbd382c81ba5f771a3e643a2b0abd31159a29057d1",Wire.Hash(File.ReadAllBytes(archive)),"immutable natural archive");byte[] bytes;
            using(var zip=ZipFile.OpenRead(archive))using(var stream=zip.GetEntry(".local/o6-natural/"+entryName).Open())using(var copy=new MemoryStream()){stream.CopyTo(copy);bytes=copy.ToArray();}
            Assert.AreEqual(inputHash,Wire.Hash(bytes),"exact original checkpoint bytes");var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var participants=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,seed,participants);
            Assert.AreEqual(500,profile.StartingCredits);Assert.AreEqual(seed,config.Seed);Assert.AreEqual(8,config.Roster.Count);Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            var off=Run(false,c,bytes,profile,config);var on=Run(true,c,bytes,profile,config);
            Assert.AreEqual(45,off.World.Length);Assert.AreEqual(45,on.World.Length);Assert.AreEqual(45,off.Decisions.Length);Assert.AreEqual(45,on.Decisions.Length);Assert.AreEqual(off.Opening,on.Opening);
            for(int i=0;i<45;i++)
            {CollectionAssert.AreEqual(off.World[i],on.World[i],"complete C9 bytes with hook off/on at tick "+(c.Tick+i+1));Assert.AreEqual(off.Decisions[i],on.Decisions[i],"ordinary decision row with hook off/on at tick "+(c.Tick+i+1));}
            Assert.Greater(on.Rows.Count,0,"actual funding/action matrix captured after natural continuation");
            lock(typeof(NativeAiO6CurrentExpansionRichFundingProbeTests))
                foreach(var row in on.Rows)File.AppendAllText(OutputPath,row.ToString(Formatting.None)+Environment.NewLine);
            TestContext.WriteLine("O6_RICH_FUNDING_INPUT_RESULT "+JsonConvert.SerializeObject(new{seed,owner,checkpointTick,entryName,checkpointOpening,Ticks=45,MatrixRows=on.Rows.Count,HookOnProbes=on.Probes,ExactWorldBytesEqual=true,OrdinaryDecisionRowsEqual=true}));
        }
    }
}

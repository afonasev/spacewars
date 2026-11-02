using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
namespace Spacewars.Tests.EditMode
{
    public sealed class AiPositionCausalCaptureDiagnosticTests
    {
        [Test,Timeout(180000)] public void Existing4b9SnapshotPositionCausalReadback()
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            object Field(object obj,string name)=>obj.GetType().GetField(name,flags).GetValue(obj);
            string input=Environment.GetEnvironmentVariable("AI_POSITION_CAPTURE");Assert.IsNotEmpty(input);
            string source=Environment.GetEnvironmentVariable("AI_POSITION_SOURCE_SHA");Assert.IsNotEmpty(source);
            string output=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(input))),"causal-4b9a-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
            var clock=Stopwatch.StartNew();
            void Mark(string phase,object details=null)=>File.AppendAllText(Path.Combine(output,"phases.jsonl"),Newtonsoft.Json.JsonConvert.SerializeObject(new{phase,elapsedSeconds=clock.Elapsed.TotalSeconds,details})+"\n");
            var bytes=File.ReadAllBytes(input);Assert.AreEqual(4620541,bytes.Length);using(var sha=SHA256.Create())Assert.AreEqual("a07e7e94710a3dcb95e5c6495230bfce33d157e4c1268b3f03c0de7ac02c6706",BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant());
            long capturedTick;int transportLength;
            using(var state=PlayableWorldState.Decode(bytes))
            {Assert.AreEqual(PlayableWorldState.CurrentVersion,state.Version);Assert.AreEqual(7,state.Seed);Assert.AreEqual(107,state.Generation);Assert.AreEqual("ai-position-development-first-witness-v1",state.SourceIdentity);Assert.Greater(state.RuntimeTransport.Length,0);capturedTick=state.Tick;transportLength=state.RuntimeTransport.Length;Mark("input-envelope",new{state.Version,state.Seed,state.Generation,state.Tick,state.SourceIdentity,transportLength});}
            const int seed=7;const long generation=107;
            var p=PlayableProfile.Default;var ai=NativeBalanceFields.InitialAiOpeningProfile();
            var starts=new[]{new OfflineStart("west",1,new NavPoint(-20,-15),new NavPoint(-20,-27),pin:1),new OfflineStart("east",2,new NavPoint(20,15),new NavPoint(20,27),pin:2)};
            var home=new TerritorySite(1,PlayableBuildingKind.Headquarters,starts[0].Position,p);
            var enemy=new TerritorySite(2,PlayableBuildingKind.Headquarters,starts[1].Position,p);
            var front=new TerritorySite(3,PlayableBuildingKind.Outpost,new NavPoint(-20,15),p);
            var kinds=new[]{PlayableBuildingKind.Factory,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.Refinery,PlayableBuildingKind.ScientificCenter};
            Assert.GreaterOrEqual(home.Slots.Count,kinds.Length);
            var buildings=kinds.Select((kind,i)=>new OfflineScenarioBuilding(1,kind,1,home.Slots[i].Id,home.Slots[i].Position,home.Slots[i].Heading)).Concat(new[]{new OfflineScenarioBuilding(1,PlayableBuildingKind.Outpost,3,0,front.Position)}).ToArray();
            var units=Enumerable.Range(0,5).Select(i=>new OfflineScenarioUnit(1,PlayableEntityKind.Tank,new NavPoint(-25+i*3,-1))).Concat(new[]{new OfflineScenarioUnit(2,PlayableEntityKind.Hawk,new NavPoint(20,-24))}).ToArray();
            var config=new OfflineMatchConfiguration(p,"ai-position-development-first-witness-v1","flat-public-position-v1","UnityHostRouteService",seed,new[]{new OfflineParticipant("position-owner",1,1,OfflineControl.Ai),new OfflineParticipant("human-owner",2,2,OfflineControl.Human)},starts,new[]{home,enemy,front},PlayableMap.StaticObstacles(p),new double[,]{{0,100},{100,0}},scenario:units,scenarioBuildings:buildings,spectators:new[]{PlayableRuntime.LocalSpectatorId});

            Assert.AreEqual("8d2ab24c1989b3fa52b21f175a4fbfe9b29e423eae5328ae55e606b9566c64ff",ai.Hash);
            Assert.AreEqual("unity-owner-research-queue-u6-v1",p.ProfileId);Assert.AreEqual(1,p.Revision);
            PlayableRuntime clone=null;PlayableAuthorityTick authority=null;byte[] roundtrip=null;Exception failure=null;
            try
            {
                Mark("restore-before");clone=PlayableRuntime.RestoreBytesForTests(bytes,config,ai,r=>
                {
                    try
                    {
                        Mark("prestart-enter");authority=(PlayableAuthorityTick)Field(r,"authorityTick");var checkpoint=authority.CaptureDiagnosticCheckpoints().Single(c=>c.OwnerId=="position-owner");
                        var scheduler=Field(authority,"scheduler");var owner=((IEnumerable)Field(scheduler,"owners")).Cast<object>().Single();
                        var ledger=(AiBudgetLedger)Field(owner,"budget");var economy=(PlayableAiEconomicLivenessPolicy)Field(owner,"economy");var opening=(AiOpeningExecutor)Field(owner,"openingExecutor");
                        var view=authority.Latest;var observation=PlayableAiObservation.From(view);Assert.AreEqual(generation,observation.Generation);Assert.AreEqual(seed,observation.Seed);Assert.AreEqual(capturedTick,observation.Tick);
                        var traces=new List<object>();AiPositionDevelopmentPlanner.DiagnosticForTests=(stage,details)=>traces.Add(new{stage,details});
                        var choices=AiPositionDevelopmentPlanner.Plan(observation,p,ai,checkpoint.Opening.WithPhase(checkpoint.OpeningExecution.Phase),checkpoint.Knowledge,true,opening.Air);
                        AiPositionDevelopmentPlanner.DiagnosticForTests=null;
                        var pending=((IEnumerable)Field(owner,"pendingItems")).Cast<object>().Select(v=>new{Policy=Field(v,"PolicyName"),Action=Field(v,"Action"),Intent=Field(v,"Intent"),Identity=Field(v,"Identity"),DueTick=Field(v,"DueTick")}).ToArray();
                        var proposal=owner.GetType().GetMethod("Proposal",flags);
                        var admitted=choices.Select(choice=>
                        {
                            var action=economy.HasPendingObligation?null:economy.Fork().Admit(observation,choice.Action);
                            var quote=action==null?null:(AiIntent)proposal.Invoke(owner,new object[]{"economy",action,observation,action.Kind==PlayableCommandKind.BuildAt?1:0});
                            return new{choice,policyHasPendingObligation=economy.HasPendingObligation,action,quote,
                                admission=AiEconomyAdmission.Reject(observation,p,choice.Action),
                                fundableWithoutAnyExtraProtection=AiExpansionFinancial.FullyFundable(ledger,0,quote),
                                maximumProtectionThatFits=quote==null?(long?)null:ledger.Available-quote.Credits-quote.StartupFund};
                        }).ToArray();
                        var arbiter=(AiDecisionArbiter)Field(owner,"arbiter");var funding=(AiTechnologyFunding)Field(owner,"technologyFunding");
                        var history=(IDictionary)Field(arbiter,"history");var persistedHistory=new List<object>();
                        foreach(DictionaryEntry row in history)persistedHistory.Add(new{key=row.Key,created=Field(row.Value,"FirstTick"),nextRetryOrExpiry=Field(row.Value,"NextRetry"),failures=Field(row.Value,"Failures"),conditions=Field(row.Value,"Conditions")});
                        object memo=history.Contains(AiDecisionArbiter.PositionGoalKey)?history[AiDecisionArbiter.PositionGoalKey]:null;
                        object positionMemo=memo==null?null:(object)new{created=(long)Field(memo,"FirstTick"),expiry=(long)Field(memo,"NextRetry"),tombstone=observation.Tick>(long)Field(memo,"NextRetry"),target=Field(memo,"Conditions"),failures=Field(memo,"Failures")};
                        var held=ledger.Capture();var facts=new{source,producer="4b9ab9e15dae939beea6d3330d767d1dcc9c3af2",observation.Identity,view.Tick,view.Credits,view.Outcome,
                            checkpoint.OpeningExecution,checkpoint.ArmyMission,armyPhase=checkpoint.ArmyMission.Phase.ToString(),checkpoint.EconomyDecision,
                            ownedSites=observation.Sites.Where(s=>s.Owner==observation.Owner).Select(s=>new{s.Site.Id,s.Site.Kind,s.Site.Position,s.Owner,s.Ready,s.Contested,s.CenterId,defensePads=s.Site.DefenseSlots(p).Select(pad=>new{pad.Id,pad.Position,occupied=observation.Buildings.Any(b=>b.SiteId==s.Site.Id&&b.SlotId==pad.Id)})}),
                            buildings=observation.Buildings,units=observation.Entities,objectives=observation.PublicScoutObjectives,traces,choices,admitted,pending,
                            positionMemo,persistedHistory,acceptedScience=funding.HasAcceptedScience(ledger,arbiter),
                            actualPaidSentry=held.Paid.Where(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.SentryTurret&&e.Action.SiteId==3),
                            quoteReadbackEvidence="Structural Plan is pure. Proposal is read-only; Admit operates on economy.Fork only. No Quote or target clock update called on the restored arbiter. Current components are not historical macro operands.",
                            missingEssentialEconomy=opening.MissingEssentialEconomy(observation,ai),
                            ledger=held,bank=new{ledger.Liquid,ledger.Available,ledger.Reserved,ledger.Unpaid,ledger.SafetyReserve},
                            openingProtection=new{initial=opening.InitialForceFundingProtection(observation,p,ai,held),home=opening.HomeEconomyFundingProtection(observation,p,ai,held)},
                            protectedFundsEvidence="Historical macro local is not serialized; no macro tick executed/reconstructed. Current pure protection components and zero-extra-protection bound only.",
                            records=checkpoint.Records};
                        File.WriteAllText(Path.Combine(output,"checkpoint.json"),Newtonsoft.Json.JsonConvert.SerializeObject(facts,Newtonsoft.Json.Formatting.Indented));Mark("checkpoint-written");
                        roundtrip=r.CaptureBeforeStartForTests();Mark("capture-after",new{bytes=roundtrip.Length});
                    }
                    catch(Exception error){failure=error;Mark("failure",error.ToString());}
                    finally{AiPositionDevelopmentPlanner.DiagnosticForTests=null;r.RequestStop();}
                });
                Assert.True(SpinWait.SpinUntil(()=>clone.IsStopped,5000));if(failure!=null)Assert.Fail(failure.ToString());
                using(var after=PlayableWorldState.Decode(roundtrip)){Assert.AreEqual(capturedTick,after.Tick);Assert.AreEqual(transportLength,after.RuntimeTransport.Length);}
                ExactByteAssert.AreEqual(bytes,roundtrip,"diagnostic reads must preserve full Runtime bytes including transport");Mark("exact-after");
            }
            finally{if(clone!=null){clone.RequestStop();Assert.True(SpinWait.SpinUntil(()=>clone.IsStopped,5000));clone.Dispose();}if(authority!=null){authority.Domain.Navigation.ReleaseCommittedHostRoot();authority.Domain.DestroyOwnedNavigation();}Mark("cleanup-after");}
        }
    }
}

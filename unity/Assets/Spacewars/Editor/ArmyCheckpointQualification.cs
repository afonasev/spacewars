using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Spacewars.Headless;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using Spacewars.Presentation;
using UnityEngine;
namespace Spacewars.Editor
{
    // Explicit diagnostic input regeneration through the current Editor authority and native routes.
    public static class ArmyCheckpointQualification
    {
        public static void Generate()
        {
            string destination=Environment.GetEnvironmentVariable("SPACEWARS_A1_CHECKPOINT_OUTPUT"),revision=Environment.GetEnvironmentVariable("SPACEWARS_A1_CODE_REVISION");
            if(string.IsNullOrWhiteSpace(destination)||string.IsNullOrWhiteSpace(revision))throw new InvalidOperationException("A1 checkpoint output and exact code revision required");
            foreach(var fixture in string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SPACEWARS_A1_CHECKPOINT_FIXTURE"))?new[]{"e6-lost-hq","e6-lost-hq-recent-order"}:new[]{Environment.GetEnvironmentVariable("SPACEWARS_A1_CHECKPOINT_FIXTURE")})
            {
                if(fixture!="e6-lost-hq"&&fixture!="e6-lost-hq-recent-order")throw new ArgumentException("Unsupported A1 checkpoint fixture");
                int stop=fixture=="e6-lost-hq"?1200:2400;
                string original=Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures",fixture),input=File.ReadAllText(Path.Combine(original,"manifest.json"));
                var manifest=JsonConvert.DeserializeObject<MatchManifest>(input);var config=HeadlessFixtures.Create(manifest);
                var authority=new PlayableAuthorityTick(config,manifest.Generation);int commandIndex=0;long sequence=0;
                byte[] selected=null;long selectedTick=0,selectedScoutOrderTick=0,selectedScoutCoverageTick=0;int selectedCredits=0;object selectedFacts=null;
                using(var routes=new UnityHostRouteService())while(authority.Tick<=stop+300&&selected==null)
                {
                    while(commandIndex<manifest.Commands.Length&&manifest.Commands[commandIndex].Tick==authority.Tick)
                    {
                        var receipt=authority.Apply(HeadlessFixtures.Command(authority,manifest.Commands[commandIndex++],manifest.Generation,++sequence));
                        if(receipt.Status!=PlayableCommandStatus.Applied&&receipt.Status!=PlayableCommandStatus.Accepted)throw new InvalidOperationException("Checkpoint diagnostic input rejected: "+receipt.Message);
                    }
                    for(int attempts=0;!authority.TryAdvance();attempts++){if(attempts>=4096)throw new InvalidOperationException("Native navigation barrier stalled");routes.Service(authority,manifest.RouteBudget);}
                    var candidate=authority.ParticipantView("west-owner");var memory=authority.CaptureDiagnosticCheckpoints().Single();
                    var budget=authority.CaptureDiagnosticBudgets().Single();int priorPaidScouts=budget.Paid.Count(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer);
                    long available=Math.Max(0,(long)budget.Liquid-budget.SafetyReserve-budget.Unpaid.Sum(e=>(long)e.Amount)-budget.Reserved.Sum(e=>(long)e.Amount));
                    bool pendingScout=budget.Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer);
                    bool queuedScout=candidate.Buildings.Where(b=>b.Owner==candidate.Owner&&b.PrivateState!=null).Any(b=>b.PrivateState.Orders.Any(order=>order.Kind==PlayableEntityKind.Explorer));
                    bool lost=candidate.Tick>=stop-600&&!candidate.Buildings.Any(b=>b.Owner==candidate.Owner&&b.Kind==PlayableBuildingKind.Headquarters)&&!candidate.Entities.Any(e=>e.Owner==candidate.Owner&&e.Kind==PlayableEntityKind.Explorer)&&candidate.Buildings.Any(b=>b.Owner==candidate.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.SiteId==3);
                    bool readyLine=candidate.Buildings.Any(b=>b.Owner==candidate.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.SiteId==3&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState!=null&&b.PrivateState.QueueCount==0);
                    var observation=PlayableAiObservation.From(candidate);
                    bool recoveryLegal=new AiEconomyPlanner().Plan(observation,config.Profile,available:(int)Math.Min(int.MaxValue,available),recoveryExplorerDemand:true).Candidates.Any(x=>x.Legal&&x.Action.Kind==PlayableCommandKind.QueueExplorer);
                    bool opportunity=readyLine&&!pendingScout&&!queuedScout&&available>=config.Profile.ExplorerCreditCost&&available<config.Profile.TankCreditCost&&candidate.Credits<config.Profile.TankCreditCost&&recoveryLegal;
                    bool eligible=stop==1200?priorPaidScouts==0&&candidate.Credits>=config.Profile.ExplorerCreditCost&&candidate.Credits<config.Profile.TankCreditCost:readyLine&&priorPaidScouts==1&&memory.LastScoutOrderTick>0&&candidate.Tick-memory.LastScoutOrderTick<35*30;
                    if(lost&&eligible&&opportunity){selected=authority.CaptureBytes();selectedTick=authority.Tick;selectedCredits=candidate.Credits;selectedScoutOrderTick=memory.LastScoutOrderTick;selectedScoutCoverageTick=memory.LastScoutTick;selectedFacts=new {budget.Liquid,budget.SafetyReserve,NetAvailable=available,Unpaid=budget.Unpaid.Select(e=>new {e.Id,e.Purpose,e.Amount,Kind=e.Action.Kind.ToString()}),Reserved=budget.Reserved.Select(e=>new {e.Id,e.Purpose,e.Amount,Kind=e.Action.Kind.ToString()}),PendingScout=pendingScout,QueuedScout=queuedScout,PriorPaidScouts=priorPaidScouts,RecoveryLegal=recoveryLegal,Factories=candidate.Buildings.Where(b=>b.Owner==candidate.Owner&&b.Kind==PlayableBuildingKind.Factory).Select(b=>new {b.Id,b.SiteId,b.Health,Phase=b.Phase.ToString(),QueueCount=b.PrivateState?.QueueCount}),PublicGoals=observation.PublicScoutObjectives.Select(g=>new {g.SiteId,g.Reachable,Role=g.Role.ToString()}),VisibleHostiles=observation.Entities.Where(e=>observation.IsHostile(e.Owner)).Select(e=>new {e.Id,Kind=e.Kind.ToString(),e.Health,e.Position})};}
                }
                if(selected==null)throw new InvalidOperationException("No current-native checkpoint satisfying unchanged lostHQ/recovery predicates in bounded window around "+stop);
                var bytes=selected;var restored=PlayableAuthorityTick.RestoreBytes(bytes,config);
                if(!bytes.SequenceEqual(restored.CaptureBytes()))throw new InvalidOperationException("Checkpoint roundtrip changed authority bytes");
                string output=Path.Combine(destination,fixture);Directory.CreateDirectory(output);string file="checkpoint-"+stop+".world";File.WriteAllBytes(Path.Combine(output,file),bytes);
                File.WriteAllText(Path.Combine(output,"identity.json"),JsonConvert.SerializeObject(new {Schema="native-army-checkpoint-input-v1",CodeRevision=revision,AiStateSchema=BitConverter.ToInt32(PlayableWorldState.Decode(bytes).AiAuthority,4),Checkpoint=file,CheckpointHash=Wire.Hash(bytes),OriginalManifestHash=Wire.Hash(System.Text.Encoding.UTF8.GetBytes(input)),OriginalCheckpointHash=Wire.Hash(File.ReadAllBytes(Path.Combine(original,file))),Tick=selectedTick,RequestedHistoricalTick=stop,manifest.Generation,manifest.Seed,config.MapIdentity,Profile=WorldWireBinding(config.Profile),AiHash=AiProfile.Initial.Hash,RouteDeliveryPolicy=PlayableAuthorityTick.RouteDeliveryPolicy,UnityVersion=Application.unityVersion,Host="Editor UnityHostRouteService",Observed=new {Credits=selectedCredits,LastScoutTick=selectedScoutCoverageTick,LastScoutOrderTick=selectedScoutOrderTick},Opportunity=selectedFacts,Selection="first actual state with unchanged role/affordability/history predicates in [requested-600,requested+300]"},Formatting.Indented));
                restored.Stop();authority.Stop();Debug.Log("NATIVE_ARMY_CHECKPOINT_PASS "+fixture+" tick="+selectedTick+" hash="+Wire.Hash(bytes));
            }
        }
        public static void DiagnoseRecent()
        {
            string destination=Environment.GetEnvironmentVariable("SPACEWARS_A1_DIAGNOSTIC_OUTPUT");if(string.IsNullOrWhiteSpace(destination))throw new ArgumentException("Diagnostic output required");
            var original=Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/e6-lost-hq-recent-order");var manifest=JsonConvert.DeserializeObject<MatchManifest>(File.ReadAllText(Path.Combine(original,"manifest.json")));var config=HeadlessFixtures.Create(manifest);
            var input=Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures/a1-current-e6/e6-lost-hq-recent-order");var identity=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(input,"identity.json")));var authority=PlayableAuthorityTick.RestoreBytes(File.ReadAllBytes(Path.Combine(input,(string)identity["Checkpoint"])),config);var initial=authority.CaptureDiagnosticCheckpoints().Single();long checkpointTick=authority.Tick,paidTick=0;bool pending=false,born=false,moved=false;var timeline=new System.Collections.Generic.List<object>();var previous=new System.Collections.Generic.Dictionary<int,string>();
            long observationEnd=Math.Max(checkpointTick+700,Math.Max(initial.LastScoutOrderTick+35*30,checkpointTick+AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.idleLineDeadlineSeconds"),30)+AiProfile.SecondsToTicks(PlayableUnitRules.Duration(config.Profile,PlayableEntityKind.Explorer),30))+AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(initial.Difficulty,"decisionSeconds"),30)+AiProfile.SecondsToTicks(AiProfile.Initial.DifficultyValue(initial.Difficulty,"reactionSeconds"),30)+1);
            using(var routes=new UnityHostRouteService())for(int i=0;authority.Tick<observationEnd;i++)
            {
                for(int attempts=0;!authority.TryAdvance();attempts++){if(attempts>=4096)throw new InvalidOperationException("Probe route barrier stalled");routes.Service(authority,64);}
                var budget=authority.CaptureDiagnosticBudgets().Single();pending|=budget.Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer);
                if(paidTick==0&&budget.Paid.Count(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer)>1)paidTick=authority.Tick;
                var view=authority.ParticipantView("west-owner");
                var explorers=view.Entities.Where(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Explorer).ToArray();
                foreach(var e in explorers){string state=e.Health+"/"+e.Moving+"/"+e.CurrentOrder?.CommandSequence;if(!previous.TryGetValue(e.Id,out var old)||old!=state){timeline.Add(new {Tick=authority.Tick,Event="birth/health/order",Entity=e,Armies=authority.CaptureDiagnosticCheckpoints().Single().Armies});previous[e.Id]=state;}}
                foreach(var id in previous.Keys.Where(id=>!explorers.Any(e=>e.Id==id)).ToArray()){timeline.Add(new {Tick=authority.Tick,Event="death",Id=id});previous.Remove(id);}
                born|=view.Entities.Any(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Explorer);moved|=born&&authority.CaptureDiagnosticCheckpoints().Single().LastScoutOrderTick>initial.LastScoutOrderTick;if(moved)break;
            }
            var final=authority.CaptureDiagnosticCheckpoints().Single();var finalView=authority.ParticipantView("west-owner");var bank=authority.CaptureDiagnosticBudgets().Single();
            File.WriteAllText(destination,JsonConvert.SerializeObject(new {checkpointTick,initial.LastScoutTick,initial.LastScoutOrderTick,InitialExplorerDemand=initial.Opening.Intent.Explorer,pending,paidTick,born,moved,FinalTick=authority.Tick,FinalLastScoutTick=final.LastScoutTick,FinalLastScoutOrderTick=final.LastScoutOrderTick,FinalExplorerDemand=final.Opening.Intent.Explorer,IdleDeadline=AiProfile.SecondsToTicks(AiProfile.Initial.Value("economy.idleLineDeadlineSeconds"),30),RescoutDueTick=initial.LastScoutOrderTick+35*30,Unpaid=bank.Unpaid,PaidScoutCount=bank.Paid.Count(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer),Records=final.Records,Timeline=timeline,FinalOwnExplorers=finalView.Entities.Where(e=>e.Owner==finalView.Owner&&e.Kind==PlayableEntityKind.Explorer),final.Mission,final.EconomyDecision,final.Opening},Formatting.Indented));authority.Stop();Debug.Log("A1_RECENT_DIAGNOSIS_WRITTEN "+destination);
        }
        public static void DiagnoseCurrent()
        {
            string destination=Environment.GetEnvironmentVariable("SPACEWARS_A1_CHECKPOINT_OUTPUT"),revision=Environment.GetEnvironmentVariable("SPACEWARS_A1_CODE_REVISION");if(string.IsNullOrWhiteSpace(destination))throw new ArgumentException("Diagnostic output required");Directory.CreateDirectory(destination);
            foreach(var fixture in new[]{"e6-lost-hq","e6-lost-hq-recent-order"})
            {
                int stop=fixture=="e6-lost-hq"?1200:2400;var original=Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures",fixture);var input=File.ReadAllText(Path.Combine(original,"manifest.json"));var manifest=JsonConvert.DeserializeObject<MatchManifest>(input);var config=HeadlessFixtures.Create(manifest);var authority=new PlayableAuthorityTick(config,manifest.Generation);int commandIndex=0;long sequence=0;
                var rows=new System.Collections.Generic.List<object>();string previous=null;long firstLost=-1,firstNoScout=-1,firstReady=-1,firstAffordable=-1,firstEligible=-1;
                using(var routes=new UnityHostRouteService())for(int tick=0;tick<stop+300;tick++)
                {
                    while(commandIndex<manifest.Commands.Length&&manifest.Commands[commandIndex].Tick==authority.Tick)
                    {var receipt=authority.Apply(HeadlessFixtures.Command(authority,manifest.Commands[commandIndex++],manifest.Generation,++sequence));if(receipt.Status!=PlayableCommandStatus.Applied&&receipt.Status!=PlayableCommandStatus.Accepted)throw new InvalidOperationException(receipt.Message);}
                    long before=authority.Tick;for(int attempt=0;!authority.TryAdvance();attempt++){if(attempt>=4096)throw new InvalidOperationException("Diagnostic native route barrier stalled");routes.Service(authority,manifest.RouteBudget);}
                    var view=authority.ParticipantView("west-owner");var memory=authority.CaptureDiagnosticCheckpoints().Single();var budget=authority.CaptureDiagnosticBudgets().Single();long available=Math.Max(0,(long)budget.Liquid-budget.SafetyReserve-budget.Unpaid.Sum(e=>(long)e.Amount)-budget.Reserved.Sum(e=>(long)e.Amount));
                    bool hq=view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters),scout=view.Entities.Any(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Explorer);var line=view.Buildings.FirstOrDefault(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.SiteId==3&&b.Health>0);bool ready=line!=null&&line.Phase==ConstructionPhase.Ready&&line.PrivateState!=null&&line.PrivateState.QueueCount==0;
                    bool pending=budget.Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer),queued=view.Buildings.Where(b=>b.Owner==view.Owner&&b.PrivateState!=null).Any(b=>b.PrivateState.Orders.Any(order=>order.Kind==PlayableEntityKind.Explorer));int paid=budget.Paid.Count(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer);
                    bool legal=new AiEconomyPlanner().Plan(PlayableAiObservation.From(view),config.Profile,available:(int)Math.Min(int.MaxValue,available),recoveryExplorerDemand:true).Candidates.Any(x=>x.Legal&&x.Action.Kind==PlayableCommandKind.QueueExplorer);
                    bool recent=fixture=="e6-lost-hq"?paid==0:paid==1&&memory.LastScoutOrderTick>=0&&view.Tick-memory.LastScoutOrderTick<35*30;
                    bool window=view.Tick>=stop-600;bool lost=window&&!hq&&!scout&&line!=null;bool affordable=available>=config.Profile.ExplorerCreditCost&&available<config.Profile.TankCreditCost&&view.Credits<config.Profile.TankCreditCost;
                    bool eligible=lost&&ready&&affordable&&!pending&&!queued&&memory.Opening.Intent.Explorer>0&&recent&&legal;
                    if(!hq&&firstLost<0)firstLost=view.Tick;if(!scout&&firstNoScout<0)firstNoScout=view.Tick;if(ready&&firstReady<0)firstReady=view.Tick;if(affordable&&firstAffordable<0)firstAffordable=view.Tick;if(eligible&&firstEligible<0)firstEligible=view.Tick;
                    string flags=hq+"/"+scout+"/"+ready+"/"+affordable+"/"+pending+"/"+queued+"/"+recent+"/"+legal+"/"+eligible;
                    if(flags!=previous||view.Tick%30==0)rows.Add(new{view.Tick,view.Outcome,Hq=hq,Scout=scout,FactoryExists=line!=null,FactoryHealth=line?.Health,Queue=line?.PrivateState?.QueueCount,view.Credits,Available=available,PaidScouts=paid,memory.LastScoutTick,memory.LastScoutOrderTick,ExplorerDemand=memory.Opening.Intent.Explorer,Lost=lost,Ready=ready,Affordable=affordable,Pending=pending,Queued=queued,Recent=recent,Legal=legal,Eligible=eligible,Armies=memory.Armies,Records=memory.Records.Where(r=>r.Status==PlayableAiDeliveryStatus.Applied).Reverse().Take(4).Reverse().ToArray()});previous=flags;
                    if(authority.Tick==before)break;
                }
                File.WriteAllText(Path.Combine(destination,fixture+"-diagnostic.json"),JsonConvert.SerializeObject(new{revision,fixture,Requested=stop,End=authority.Tick,OriginalManifestHash=Wire.Hash(System.Text.Encoding.UTF8.GetBytes(input)),OriginalCheckpointHash=Wire.Hash(File.ReadAllBytes(Path.Combine(original,"checkpoint-"+stop+".world"))),AiHash=AiProfile.Initial.Hash,firstLost,firstNoScout,firstReady,firstAffordable,firstEligible,Rows=rows},Formatting.Indented));authority.Stop();Debug.Log("E6_CURRENT_DIAGNOSIS_WRITTEN "+fixture);
            }
        }
        public static void GenerateDerived()
        {
            string destination=Environment.GetEnvironmentVariable("SPACEWARS_A1_CHECKPOINT_OUTPUT"),revision=Environment.GetEnvironmentVariable("SPACEWARS_A1_CODE_REVISION");if(string.IsNullOrWhiteSpace(destination)||string.IsNullOrWhiteSpace(revision))throw new ArgumentException("Derived output/source revision required");Directory.CreateDirectory(destination);
            foreach(var fixture in new[]{"e6-lost-hq","e6-lost-hq-recent-order"})
            {
                int stop=fixture=="e6-lost-hq"?1200:2400;string original=Path.Combine(Directory.GetCurrentDirectory(),"Tests/Fixtures",fixture),text=File.ReadAllText(Path.Combine(original,"manifest.json"));var baseline=JsonConvert.DeserializeObject<MatchManifest>(text);
                var input=new ArmyRecoveryManifest{OriginalFixture=fixture,OriginalManifest=baseline,OriginalManifestHash=Wire.Hash(System.Text.Encoding.UTF8.GetBytes(text)),MapIdentity="a3-derived-"+fixture+"-hq-guard-v1"};var config=ArmyRecoveryFixture.Create(input);var authority=new PlayableAuthorityTick(config,baseline.Generation);ArmyRecoveryFixture.BindCommands(input,authority);
                var output=Path.Combine(destination,fixture);Directory.CreateDirectory(output);string manifestText=JsonConvert.SerializeObject(input,Formatting.Indented);File.WriteAllText(Path.Combine(output,"manifest.json"),manifestText);
                File.WriteAllText(Path.Combine(output,"genesis.json"),JsonConvert.SerializeObject(new{config.SourceIdentity,config.MapIdentity,config.Seed,config.Roster,config.Starts,config.Sites,config.Obstacles,config.ScenarioUnits,config.ScenarioBuildings,Profile=WorldWireBinding(config.Profile),GameplayHash=AiMatchIdentity.GameplayDigest(config.Profile),AiHash=AiProfile.Initial.Hash,MapHash=HeadlessFixtures.MapHash(config),GeometryHash=HeadlessFixtures.GeometryHash(config)},Formatting.Indented));
                byte[] selected=null;long selectedTick=0;object selectedFacts=null;long firstDeath=-1;bool priorScout=true;var rows=new System.Collections.Generic.List<object>();var receipts=new System.Collections.Generic.List<object>();int commandIndex=0;
                using(var routes=new UnityHostRouteService())while(authority.Tick<stop+300&&selected==null)
                {
                    while(commandIndex<input.Commands.Length&&input.Commands[commandIndex].Tick==authority.Tick)
                    {var cmd=input.Commands[commandIndex++];var receipt=authority.Apply(cmd.Create(baseline.Generation));if(receipt.Status!=PlayableCommandStatus.Applied&&receipt.Status!=PlayableCommandStatus.Accepted)throw new InvalidOperationException(receipt.Message);receipts.Add(new{cmd.Purpose,cmd.EntityIds,cmd.Kind,cmd.Sequence,receipt.Status,receipt.AppliedTick});}
                    for(int attempts=0;!authority.TryAdvance();attempts++){if(attempts>=4096)throw new InvalidOperationException("Derived native route barrier stalled");routes.Service(authority,baseline.RouteBudget);}if(authority.Tick==1)ArmyRecoveryFixture.AssertOrders(input,authority);
                    var view=authority.ParticipantView("west-owner");var memory=authority.CaptureDiagnosticCheckpoints().Single();var budget=authority.CaptureDiagnosticBudgets().Single();long available=Math.Max(0,(long)budget.Liquid-budget.SafetyReserve-budget.Unpaid.Sum(e=>(long)e.Amount)-budget.Reserved.Sum(e=>(long)e.Amount));
                    bool hq=view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Headquarters),scout=view.Entities.Any(e=>e.Owner==view.Owner&&e.Kind==PlayableEntityKind.Explorer);if(priorScout&&!scout&&firstDeath<0)firstDeath=view.Tick;priorScout=scout;
                    bool ready=view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.SiteId==3&&b.Health>0&&b.Phase==ConstructionPhase.Ready&&b.PrivateState!=null&&b.PrivateState.QueueCount==0);
                    bool pending=budget.Unpaid.Any(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer),queued=view.Buildings.Where(b=>b.Owner==view.Owner&&b.PrivateState!=null).Any(b=>b.PrivateState.Orders.Any(o=>o.Kind==PlayableEntityKind.Explorer));int paid=budget.Paid.Count(e=>e.Action.Kind==PlayableCommandKind.QueueExplorer);
                    bool legal=new AiEconomyPlanner().Plan(PlayableAiObservation.From(view),config.Profile,available:(int)Math.Min(int.MaxValue,available),recoveryExplorerDemand:true).Candidates.Any(c=>c.Legal&&c.Action.Kind==PlayableCommandKind.QueueExplorer);
                    bool history=stop==1200?paid==0:paid==1&&memory.LastScoutOrderTick>0&&view.Tick-memory.LastScoutOrderTick<35*30;
                    bool lost=view.Tick>=stop-600&&!hq&&!scout&&view.Buildings.Any(b=>b.Owner==view.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.SiteId==3);
                    bool opportunity=ready&&!pending&&!queued&&available>=config.Profile.ExplorerCreditCost&&available<config.Profile.TankCreditCost&&view.Credits<config.Profile.TankCreditCost&&legal;
                    if(view.Tick%30==0)rows.Add(new{view.Tick,Hq=hq,Scout=scout,Ready=ready,view.Credits,Available=available,PaidScouts=paid,memory.LastScoutTick,memory.LastScoutOrderTick,History=history,Opportunity=opportunity,Lost=lost,Legal=legal});
                    if(lost&&history&&opportunity){selected=authority.CaptureBytes();selectedTick=authority.Tick;selectedFacts=new{view.Credits,Available=available,budget.Liquid,budget.SafetyReserve,PaidScouts=paid,memory.LastScoutTick,memory.LastScoutOrderTick,ExplorerDemand=memory.Opening.Intent.Explorer,Ready=ready,Pending=pending,Queued=queued,Legal=legal,FirstNaturalScoutDeath=firstDeath};}
                }
                File.WriteAllText(Path.Combine(output,"replay.json"),JsonConvert.SerializeObject(new{revision,fixture,InputMode="new A3-derived genesis, not same original manifest",input.OriginalRaidIds,input.GuardId,Commands=input.Commands,Receipts=receipts,FirstNaturalScoutDeath=firstDeath,End=authority.Tick,Rows=rows,SelectedTick=selectedTick,SelectedFacts=selectedFacts},Formatting.Indented));
                if(selected==null){authority.Stop();throw new InvalidOperationException("Derived E6 no exact opportunity around "+stop+"; replay retained");}
                var restored=PlayableAuthorityTick.RestoreBytes(selected,config);if(!selected.SequenceEqual(restored.CaptureBytes()))throw new InvalidOperationException("Derived input restore mismatch");string checkpoint="checkpoint-"+stop+".world";File.WriteAllBytes(Path.Combine(output,checkpoint),selected);
                File.WriteAllText(Path.Combine(output,"identity.json"),JsonConvert.SerializeObject(new{Schema="native-army-checkpoint-input-v1",InputMode="native-a3-e6-derived-v1",CodeRevision=revision,AiStateSchema=BitConverter.ToInt32(PlayableWorldState.Decode(selected).AiAuthority,4),Checkpoint=checkpoint,CheckpointHash=Wire.Hash(selected),DerivedManifest="manifest.json",DerivedManifestHash=Wire.Hash(System.Text.Encoding.UTF8.GetBytes(manifestText)),Genesis="genesis.json",GenesisHash=Wire.Hash(File.ReadAllBytes(Path.Combine(output,"genesis.json"))),OriginalManifestHash=input.OriginalManifestHash,OriginalCheckpointHash=Wire.Hash(File.ReadAllBytes(Path.Combine(original,checkpoint))),Tick=selectedTick,RequestedHistoricalTick=stop,baseline.Generation,baseline.Seed,config.SourceIdentity,config.MapIdentity,MapHash=HeadlessFixtures.MapHash(config),GeometryHash=HeadlessFixtures.GeometryHash(config),Profile=WorldWireBinding(config.Profile),GameplayHash=AiMatchIdentity.GameplayDigest(config.Profile),AiHash=AiProfile.Initial.Hash,RouteDeliveryPolicy=PlayableAuthorityTick.RouteDeliveryPolicy,Opportunity=selectedFacts,UnityVersion=Application.unityVersion,Host="Editor UnityHostRouteService",Selection="same exact affordability/history predicates and [requested-600,requested+300] window; new declared stock guard genesis; no state injection"},Formatting.Indented));
                restored.Stop();authority.Stop();Debug.Log("A3_DERIVED_CHECKPOINT_WRITTEN "+fixture+" tick="+selectedTick);
            }
        }
        private static string WorldWireBinding(PlayableProfile profile)=>profile.ProfileId+"@"+profile.Revision;
    }
}

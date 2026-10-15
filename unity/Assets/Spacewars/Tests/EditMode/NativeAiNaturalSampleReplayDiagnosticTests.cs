using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Presentation;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    // Bounded replay of the original NaturalCycle sample side effects only.
    // No Natural horizon retry, action injection, profile change, or runtime edits.
    public sealed class NativeAiNaturalSampleReplayDiagnosticTests
    {
        private const BindingFlags InstanceFlags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const BindingFlags StaticFlags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private const string OwnerId="ai-0";
        private const int Seed=7109,StartTick=4200,FirstSampleTick=4500,EndTick=4800;
        private const string Archive="docs/native-ai/o6/q7-home-economy-20261014/attempt-18/generated-per-seed-facts.tar.gz";
        private const string ArchiveHash="ceede59ad6c185220f689913fd4d2b24c5bfc3c4fe0f40a2cbbeabc9e17a67ce";
        private static readonly int[] Ticks={StartTick,FirstSampleTick,EndTick};
        private static readonly string[] Hashes={
            "1fb12a52bd869da0917f8f5ac16dc591ed506c4dd1d21cb52782a7ab9223406a",
            "0fa60b1054f8d19d06b7a83c365cf7fadcceefbd88b926b702d9f99857b0bb18",
            "f71bbf62a0274d21f08065b966cd59c720aeca91e47eb065c949350ef8a73a48"
        };

        private sealed class Boundary
        { public int Tick;public bool MatchesArchive;public string ExpectedSha256,ActualSha256;public int ExpectedBytes,ActualBytes; }
        private sealed class Replay
        {
            public bool Hooked,ReachedEnd;public int LastTick;public string StopReason;
            public readonly List<string> OwnerStates=new List<string>();
            public readonly List<object> OwnerSnapshots=new List<object>();
            public readonly List<object> ReceiptJournal=new List<object>();
            public readonly List<object> Events=new List<object>();
            public readonly List<object> CashTransitions=new List<object>();
            public readonly List<Boundary> Boundaries=new List<Boundary>();
        }

        private static DirectoryInfo Root()
        {
            var d=new DirectoryInfo(Directory.GetCurrentDirectory());
            while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;
            Assert.NotNull(d,"workspace root");return d;
        }
        private static string Sha256(byte[] bytes)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        private static object Field(object instance,string name)
        {
            var f=instance.GetType().GetField(name,InstanceFlags);Assert.NotNull(f,instance.GetType().FullName+"."+name);return f.GetValue(instance);
        }
        private static object Property(object instance,string name)
        {
            var p=instance.GetType().GetProperty(name,InstanceFlags);Assert.NotNull(p,instance.GetType().FullName+"."+name);return p.GetValue(instance,null);
        }
        private static object Describe(object value,int depth=0)
        {
            if(value==null)return null;
            var type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||value is string||value is decimal)return value;
            if(depth>=6)return "<depth-limit:"+type.Name+">";
            if(value is IDictionary dictionary)
            {
                var entries=new SortedDictionary<string,object>(StringComparer.Ordinal);
                foreach(DictionaryEntry e in dictionary)entries[Convert.ToString(e.Key)]=Describe(e.Value,depth+1);
                return entries;
            }
            if(value is IEnumerable enumerable)return enumerable.Cast<object>().Select(x=>Describe(x,depth+1)).ToArray();
            var fields=type.GetFields(InstanceFlags|BindingFlags.DeclaredOnly).Where(f=>!f.IsStatic).OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray();
            if(fields.Length>0)return fields.ToDictionary(f=>f.Name,f=>Describe(f.GetValue(value),depth+1),StringComparer.Ordinal);
            return type.GetProperties(InstanceFlags).Where(p=>p.CanRead&&p.GetIndexParameters().Length==0).OrderBy(p=>p.Name,StringComparer.Ordinal)
                .ToDictionary(p=>p.Name,p=>Describe(p.GetValue(value,null),depth+1),StringComparer.Ordinal);
        }
        private static object FundingMemo(PlayableAiOwnerLoop owner)
        {
            var arbiter=Field(owner,"arbiter");
            var funding=typeof(AiTechnologyFunding);
            var load=funding.GetMethod("Load",StaticFlags);Assert.NotNull(load,"AiTechnologyFunding.Load");
            return Describe(load.Invoke(null,new[]{arbiter}));
        }
        private static object WorldSnapshot(PlayableAuthorityTick authority)
        {
            var snapshot=authority.Domain.Snapshot(authority.PublicationSequence,RuntimeStatus.Running,false,
                new PlayableRuntimeMetrics(0,0,0,0,0),null,Seed);
            return new{
                snapshot.Tick,snapshot.Outcome,
                Buildings=snapshot.Buildings.Select(b=>new{b.Id,b.Owner,b.Kind,b.SiteId,b.SlotId,b.ParentId,b.Phase,b.Health,
                    b.BlockedReason,Selling=b.PrivateState?.Lifecycle?.Selling,Orders=b.PrivateState?.Orders,Research=b.PrivateState?.Research}).ToArray(),
                Units=snapshot.Entities.Select(e=>new{e.Id,e.Owner,e.Kind,e.Health,e.Position,e.TargetId}).ToArray(),
                Sites=snapshot.Sites.Select(s=>new{Site=s.Site.Id,Kind=s.Site.Kind,Owner=s.Owner,Claimant=s.Claimant,s.CenterId,s.Contested,s.Progress,
                    Slots=s.Site.Slots.Select(slot=>new{slot.Id,slot.Position}).ToArray()}).ToArray(),
                IncomeEvents=snapshot.IncomeEvents.Select(e=>new{e.Tick,e.BuildingId,e.Owner,e.Kind,e.Amount}).ToArray()
            };
        }
        private static object ArmyRegistrySnapshot(PlayableAiOwnerLoop owner)
        {
            var state=owner.ArmyState;
            return new{state.OwnerId,state.Generation,state.NextId,Armies=state.Armies.OrderBy(a=>a.Id).Select(a=>new{
                a.Id,a.OwnerId,a.TacticalOwner,a.Role,a.Phase,Members=a.Members.ToArray(),Reinforcements=a.Reinforcements.ToArray(),
                a.LeaderId,a.MinimumViableForce,a.CreatedTick,a.ProgressTick,a.Objective,a.WithdrawalCondition,a.Anchor,a.Rally
            }).ToArray()};
        }
        private static object UnitTacticalState(PlayableAuthorityTick authority,int id)
        {
            var units=(IDictionary)Field(authority.Domain,"units");
            Assert.True(units.Contains(id),"persisted unit record "+id);
            var unit=units[id];var owner=Field(unit,"Owner");var health=Convert.ToInt32(Field(unit,"Health"));
            Assert.AreEqual(authority.Domain.OwnerFor(OwnerId),owner,"persisted unit owner binding");
            Assert.Greater(health,0,"persisted unit is alive");
            var navigation=Field(authority.Domain,"navigation");
            var queues=(IDictionary)Field(authority.Domain,"tacticalQueues");
            var isPending=navigation.GetType().GetMethod("IsPending",InstanceFlags);Assert.NotNull(isPending,"navigation IsPending getter");
            return new{Id=id,Owner=Describe(owner),Health=health,CurrentOrder=Describe(Field(unit,"CurrentOrder")),LastOrder=Describe(Field(unit,"LastOrder")),
                NavigationPending=(bool)isPending.Invoke(navigation,new object[]{id}),Queue=queues.Contains(id)?Describe(queues[id]):null};
        }
        private static object RegistryOwnership(PlayableAuthorityTick authority,int unitId)
        {
            return authority.Scheduler.Owners.OrderBy(o=>o.OwnerId,StringComparer.Ordinal).Select(owner=>{
                var registry=Field(owner,"armies");var armyFor=registry.GetType().GetMethod("ArmyFor",InstanceFlags);
                Assert.NotNull(armyFor,"AiArmyRegistry.ArmyFor getter");
                return new{Owner=owner.OwnerId,Generation=owner.Generation,ArmyFor=(long)armyFor.Invoke(registry,new object[]{unitId}),
                    Registry=ArmyRegistrySnapshot(owner),PendingAiItems=Describe(Field(owner,"pendingItems"))};
            }).ToArray();
        }
        private static object Accounts(PlayableAuthorityTick authority,OfflineMatchConfiguration config)
        {
            var credits=(IDictionary)Field(authority.Domain,"ownerCredits");
            var income=(IDictionary)Field(authority.Domain,"settledIncome");
            return config.Roster.OrderBy(x=>x.Id,StringComparer.Ordinal).Select(p=>{
                var owner=authority.Domain.OwnerFor(p.Id);
                return new{Owner=p.Id,Cash=Convert.ToDouble(credits[owner]),SettledIncome=Convert.ToDouble(income[owner])};
            }).ToArray();
        }
        private static object[] MatchFacts(PlayableDomain domain)
        {
            var facts=(IEnumerable)Field(domain,"matchFacts");
            return facts.Cast<object>().Select(f=>new{
                Tick=Property(f,"Tick"),Sequence=Property(f,"Sequence"),Seconds=Property(f,"Seconds"),Owner=Property(f,"Owner"),
                Kind=Property(f,"Kind"),Value=Property(f,"Value"),EntityKind=Property(f,"EntityKind"),Name=Property(f,"Name")
            }).ToArray();
        }
        private static string OwnerState(PlayableAuthorityTick authority,OfflineMatchConfiguration config)
        {
            var checkpoints=authority.CaptureDiagnosticCheckpoints().ToDictionary(x=>x.OwnerId,StringComparer.Ordinal);
            var budgets=authority.CaptureDiagnosticBudgets().OrderBy(x=>x.OwnerId,StringComparer.Ordinal).Select(b=>new{
                b.OwnerId,b.Generation,b.Liquid,b.SafetyReserve,b.PaidTotal,b.RepairPaidTotal,b.ReservationLifetimeTicks,
                Reserved=b.Reserved.OrderBy(x=>x.Id,StringComparer.Ordinal).ToArray(),
                Unpaid=b.Unpaid.OrderBy(x=>x.Id,StringComparer.Ordinal).ToArray(),
                Paid=b.Paid.OrderBy(x=>x.Id,StringComparer.Ordinal).ToArray()
            }).ToArray();
            var owners=authority.Scheduler.Owners.OrderBy(o=>o.OwnerId,StringComparer.Ordinal)
                .Select(o=>{
                    var cp=checkpoints[o.OwnerId];
                    return new{Owner=o.OwnerId,Checkpoint=new{cp.OwnerId,cp.Generation,cp.DecisionTick,cp.LastPolicy,cp.LastActionKind,
                        cp.PendingActionId,cp.PendingDueTick,cp.LastCommandSequence,Records=cp.Records.ToArray()},TechnologyMemo=FundingMemo(o)};
                }).ToArray();
            return JsonConvert.SerializeObject(new{Tick=authority.Tick,owners,budgets,Accounts=Accounts(authority,config)},Formatting.None);
        }
        private static void RecordOwnerState(Replay run,PlayableAuthorityTick authority,OfflineMatchConfiguration config,string label,bool retain)
        {
            var state=OwnerState(authority,config);
            run.OwnerStates.Add(Sha256(Encoding.UTF8.GetBytes(state)));
            if(retain)run.OwnerSnapshots.Add(new{Tick=authority.Tick,Label=label,State=state});
        }
        private static string ReceiptIdentity(PlayableAiTraceRecord record)
        {
            var receipt=record.ReceiptIdentity;
            return receipt!=null?receipt.Id:string.Join("|",record.OwnerId??"",record.ObservationIdentity??"",record.ActionId,record.DueTick,record.Policy??"",record.Kind?.ToString()??"");
        }
        private static object ReceiptDto(PlayableAiTraceRecord record)=>new{
            record.ObservationIdentity,record.OwnerId,record.SourceIdentity,record.ActionId,record.DueTick,
            record.CommandSequence,record.ApplicationTick,record.Status,record.RuntimeStatus,record.Message,
            ReceiptIdentity=record.ReceiptIdentity==null?null:new{record.ReceiptIdentity.Generation,record.ReceiptIdentity.OwnerId,
                record.ReceiptIdentity.DecisionOrdinal,record.ReceiptIdentity.ActionOrdinal,record.ReceiptIdentity.Id},record.Policy,record.Kind
        };
        private static void RecordReceiptChanges(Replay run,PlayableAuthorityTick authority,string label,HashSet<string> seenStates,HashSet<string> knownIdentities)
        {
            foreach(var checkpoint in authority.CaptureDiagnosticCheckpoints().OrderBy(x=>x.OwnerId,StringComparer.Ordinal))
            foreach(var record in checkpoint.Records)
            {
                var identity=checkpoint.OwnerId+"|"+ReceiptIdentity(record);
                var dto=ReceiptDto(record);var state=JsonConvert.SerializeObject(dto,Formatting.None);
                var stateIdentity=identity+"\0"+state;
                if(seenStates.Add(stateIdentity))
                {
                    var change=label=="initial"?"initial":knownIdentities.Contains(identity)?"changed":"new";
                    run.ReceiptJournal.Add(new{ObservedTick=authority.Tick,Owner=checkpoint.OwnerId,Label=label,Change=change,Identity=identity,Record=dto});
                }
                knownIdentities.Add(identity);
            }
        }
        // This mirrors the original NaturalCycleTests.sample reads and call order.
        // Its final CaptureBytes is intentionally retained: it reconciles every owner.
        private static byte[] OriginalSample(PlayableAuthorityTick authority)
        {
            var budgets=authority.CaptureDiagnosticBudgets().ToDictionary(x=>x.OwnerId);
            foreach(var checkpoint in authority.CaptureDiagnosticCheckpoints())
            {
                var view=authority.ParticipantView(checkpoint.OwnerId);var budget=budgets[view.OwnerId];
                var own=view.Buildings.Where(x=>x.Owner==view.Owner).ToArray();var units=view.Entities.Where(x=>x.Owner==view.Owner).ToArray();
                var queues=own.Where(x=>x.PrivateState!=null).SelectMany(x=>x.PrivateState.Orders).ToArray();
                _=new{view.Tick,view.OwnerId,view.Credits,budget.PaidTotal,budget.RepairPaidTotal,view.IncomePerSecond,view.SettledIncome,
                    view.OwnerEliminated,Living=units.Select(x=>new{x.Id,x.Kind,x.Health,x.Position}),
                    OwnedBuildings=own.Select(x=>new{x.Id,x.Kind,x.SiteId,x.Phase,x.Health,Queues=x.PrivateState?.Orders,Research=x.PrivateState?.Research}),
                    Research=view.OwnerResearch,Opening=checkpoint.OpeningExecution,Phase=checkpoint.GamePhase,Knowledge=checkpoint.Knowledge,Expansion=checkpoint.Expansion,
                    PaidQueues=queues.Count(x=>x.PaidCost>0)};
            }
            return authority.CaptureBytes();
        }
        private static void Advance(PlayableAuthorityTick authority,UnityHostRouteService host)
        {
            for(int attempt=0;attempt<512;attempt++)
            {
                if(authority.TryAdvance())
                {Assert.IsNull(authority.Latest.Failure,"ordinary NaturalCycle failure at "+authority.Tick);return;}
                host.Service(authority,64);
            }
            Assert.Fail("ordinary typed-route barrier did not advance at "+authority.Tick);
        }
        private static void ClearHooks()
        {
            PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=null;PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=null;
            PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=null;PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests=null;
            PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=null;
            PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=null;PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=null;
            AiTechnologyFunding.CurrentSafeFundingObserveForTests=null;AiTechnologyFunding.CurrentSafeFundingObserveOwnerForTests=null;
            AiTechnologyFunding.CurrentSafeFundingMutationReadbackForTests=null;AiTechnologyFunding.CurrentSafeFundingMutationOwnerForTests=null;
            AiBudgetLedger.CurrentSafeFundingExpiryReadbackForTests=null;AiBudgetLedger.CurrentSafeFundingExpiryOwnerForTests=null;
            AiDecisionArbiter.SelectionReadbackForTests=null;AiDecisionArbiter.SelectionReadbackOwnerFilterForTests=null;
            AiDefensePlanner.TechnologyCoverageReadbackForTests=null;AiDefensePlanner.TechnologyCoverageOwnerFilterForTests=null;
        }
        private static void EnableHooks(Replay run)
        {
            Action<string,object> add=(stage,value)=>run.Events.Add(new{Owner=OwnerId,Stage=stage,Payload=value});
            PlayableAiOwnerLoop.CurrentSafeFundingOwnerFilterForTests=OwnerId;
            PlayableAiOwnerLoop.CurrentSafeFundingReadbackForTests=row=>run.Events.Add(new{Owner=OwnerId,Stage="OwnerLoop",Payload=JsonConvert.DeserializeObject(row)});
            PlayableAiOwnerLoop.ProposalAdmissionOwnerFilterForTests=OwnerId;
            PlayableAiOwnerLoop.ProposalAdmissionReadbackForTests=r=>add("ProposalAdmission",r);
            PlayableAiOwnerLoop.CurrentExpansionDecisionOwnerFilterForTests=OwnerId;
            PlayableAiOwnerLoop.CurrentExpansionDecisionReadbackForTests=r=>add("ExpansionDecision",r);
            AiTechnologyFunding.CurrentSafeFundingObserveOwnerForTests=OwnerId;
            AiTechnologyFunding.CurrentSafeFundingObserveForTests=(owner,tick,before,after,detail)=>run.Events.Add(new{Owner=owner,Tick=tick,Stage="FundingObserve",Before=before,After=after,Detail=detail});
            AiTechnologyFunding.CurrentSafeFundingMutationOwnerForTests=OwnerId;
            AiTechnologyFunding.CurrentSafeFundingMutationReadbackForTests=(owner,tick,before,after,detail)=>run.Events.Add(new{Owner=owner,Tick=tick,Stage="FundingMutation",Before=before,After=after,Detail=detail});
            AiBudgetLedger.CurrentSafeFundingExpiryOwnerForTests=OwnerId;
            AiBudgetLedger.CurrentSafeFundingExpiryReadbackForTests=(owner,tick,id,before,after)=>run.Events.Add(new{Owner=owner,Tick=tick,Stage="BudgetExpiry",Entry=id,Before=before,After=after});
            AiDecisionArbiter.SelectionReadbackOwnerFilterForTests=OwnerId;
            AiDecisionArbiter.SelectionReadbackForTests=r=>add("ArbiterSelection",r);
            // This existing hook accepts a single owner filter, not null-as-all.
            AiDefensePlanner.TechnologyCoverageOwnerFilterForTests=OwnerId;
            AiDefensePlanner.TechnologyCoverageReadbackForTests=row=>run.Events.Add(new{Owner=OwnerId,Stage="TechnologyCoverage",Payload=JsonConvert.DeserializeObject(row)});
        }
        private static Replay Execute(byte[] input,byte[][] expected,bool hooked,OfflineMatchConfiguration config)
        {
            ClearHooks();
            var result=new Replay{Hooked=hooked,LastTick=StartTick};
            var receiptStates=new HashSet<string>(StringComparer.Ordinal);
            var receiptIdentities=new HashSet<string>(StringComparer.Ordinal);
            var authority=PlayableAuthorityTick.RestoreBytes(input,config,AiProfile.Initial);
            Assert.AreEqual(StartTick,authority.Tick);Assert.AreEqual(Seed,config.Seed);Assert.AreEqual(8,config.Roster.Count);
            Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            Assert.IsTrue(authority.CaptureDiagnosticCheckpoints().All(x=>x.Seed==Seed&&x.Generation==71),"saved eight-owner generation/seed binding");
            if(hooked)EnableHooks(result);
            try
            {
                RecordOwnerState(result,authority,config,"restored-4200",true);
                RecordReceiptChanges(result,authority,"initial",receiptStates,receiptIdentities);
                using(var host=new UnityHostRouteService())
                {
                    while(authority.Tick<EndTick)
                    {
                        var accountsBefore=Accounts(authority,config);
                        int factsBefore=hooked?((ICollection)Field(authority.Domain,"matchFacts")).Count:0;
                        var worldBefore=hooked?JsonConvert.SerializeObject(WorldSnapshot(authority),Formatting.None):null;
                        Advance(authority,host);result.LastTick=(int)authority.Tick;
                        bool sampleBoundary=authority.Tick==FirstSampleTick||authority.Tick==EndTick;
                        RecordReceiptChanges(result,authority,sampleBoundary?"before-original-sample":"tick",receiptStates,receiptIdentities);
                        var accountsAfter=Accounts(authority,config);
                        if(hooked&&!JsonConvert.SerializeObject(accountsBefore,Formatting.None).Equals(JsonConvert.SerializeObject(accountsAfter,Formatting.None),StringComparison.Ordinal))
                            result.CashTransitions.Add(new{Tick=authority.Tick,AccountsBefore=accountsBefore,AccountsAfter=accountsAfter,
                                MatchFacts=MatchFacts(authority.Domain).Skip(factsBefore).ToArray(),
                                NormalWorldBefore=worldBefore,NormalWorldAfter=JsonConvert.SerializeObject(WorldSnapshot(authority),Formatting.None)});
                        RecordOwnerState(result,authority,config,sampleBoundary?"before-original-sample":"tick",sampleBoundary);
                        if(authority.Tick==FirstSampleTick||authority.Tick==EndTick)
                        {
                            var bytes=OriginalSample(authority);int index=Array.IndexOf(Ticks,(int)authority.Tick);
                            RecordReceiptChanges(result,authority,"after-original-sample",receiptStates,receiptIdentities);
                            var boundary=new Boundary{Tick=(int)authority.Tick,MatchesArchive=bytes.AsSpan().SequenceEqual(expected[index]),
                                ExpectedSha256=Hashes[index],ActualSha256=Sha256(bytes),ExpectedBytes=expected[index].Length,ActualBytes=bytes.Length};
                            result.Boundaries.Add(boundary);
                            RecordOwnerState(result,authority,config,"after-original-sample",true);
                            if(!boundary.MatchesArchive)
                            {result.StopReason="NotEquivalent: original NaturalCycle sample bytes differ at tick "+authority.Tick;return result;}
                        }
                    }
                }
                result.ReachedEnd=true;return result;
            }
            finally{ClearHooks();}
        }
        private static void WriteReport(string path,byte[][] inputs,Replay off,Replay on,string equivalence)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonConvert.SerializeObject(new{
                Schema="q7-safe7109-natural-sample-replay-v1",Source="038e288c9c5e2601c5f36cf09aa08c2b2f88b7c3",
                ArchiveSha256=ArchiveHash,InputTicks=Ticks.Select((tick,i)=>new{Tick=tick,Sha256=Hashes[i],Bytes=inputs[i].Length}).ToArray(),
                Equivalence=equivalence,HooksOff=off,HooksOn=on,
                Interpretation="Only equivalent after both Natural sample boundaries match archived full C9 bytes and all per-step all-owner checkpoint/receipt/budget/memo state is equal between hooks off/on. Failure is NotEquivalent; infer no policy cause."
            },Formatting.Indented));
        }

        [Test]
        public void Safe7109_Replay4200To4800_PreservesOriginalNaturalSamplesAndCapturesFundingWriters()
        {
            var root=Root();var archivePath=Path.Combine(root.FullName,Archive);var archive=File.ReadAllBytes(archivePath);
            Assert.AreEqual(ArchiveHash,Sha256(archive),"immutable attempt-18 natural source archive");
            var inputRoot=Path.Combine(root.FullName,".local/o6-causal-query-7109-replay");
            var inputs=Ticks.Select((tick,i)=>{
                var path=Path.Combine(inputRoot,"7109-checkpoint-"+tick+".world");Assert.True(File.Exists(path),"extract exact archive member first: "+path);
                var bytes=File.ReadAllBytes(path);Assert.AreEqual(Hashes[i],Sha256(bytes),"archive checkpoint hash at tick "+tick);return bytes;
            }).ToArray();
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,Seed,roster);
            Assert.AreEqual(PlayableProfile.Default.StartingCredits,profile.StartingCredits);
            Assert.IsEmpty(config.ScenarioUnits);Assert.IsEmpty(config.ScenarioBuildings);
            var reportPath=Path.Combine(inputRoot,"report","natural-sample-replay.json");
            Replay off=null,on=null;
            try
            {
                // First establish faithful original sample replay with all hooks disabled.
                off=Execute(inputs[0],inputs,false,config);
                if(!off.ReachedEnd){WriteReport(reportPath,inputs,off,null,off.StopReason);Assert.Fail(off.StopReason);}
                on=Execute(inputs[0],inputs,true,config);
                if(!on.ReachedEnd){WriteReport(reportPath,inputs,off,on,on.StopReason);Assert.Fail(on.StopReason);}
                bool same=off.OwnerStates.SequenceEqual(on.OwnerStates);
                WriteReport(reportPath,inputs,off,on,same?"Equivalent at archived natural sample boundaries; hooks-off/on all-owner state matched":"NotEquivalent: hook-off/on owner, receipt, budget or memo state differed");
                Assert.True(same,"diagnostic callbacks changed an all-owner checkpoint/receipt/budget/memo state");
                Assert.IsTrue(off.Boundaries.All(x=>x.MatchesArchive)&&on.Boundaries.All(x=>x.MatchesArchive),"both replays must equal original Natural samples at ticks 4500 and 4800");
                Assert.AreEqual(EndTick,on.LastTick);
                Assert.IsTrue(on.Events.Any(e=>JsonConvert.SerializeObject(e).Contains("GuardDefense")),"target-owner GuardDefense critical-stage callback was observed");
                Assert.IsNotEmpty(on.CashTransitions,"actual source cash transitions were projected");
            }
            finally{ClearHooks();}
        }

        [Test]
        public void Safe7109_ReadsSaved4800ArmyAndUnit67WithoutScopedMutation()
        {
            const int tick=4800,unitId=67;
            var root=Root();var archive=File.ReadAllBytes(Path.Combine(root.FullName,Archive));
            Assert.AreEqual(ArchiveHash,Sha256(archive),"pinned source archive");
            var inputPath=Path.Combine(root.FullName,".local/o6-causal-query-7109-replay/7109-checkpoint-4800.world");
            Assert.True(File.Exists(inputPath),"extract exact archive member first: "+inputPath);
            var input=File.ReadAllBytes(inputPath);Assert.AreEqual(Hashes[2],Sha256(input),"pinned tick-4800 checkpoint");
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,Seed,roster);
            var authority=PlayableAuthorityTick.RestoreBytes(input,config,AiProfile.Initial);
            Assert.AreEqual(tick,authority.Tick,"restored checkpoint tick");
            Assert.IsTrue(authority.CaptureDiagnosticCheckpoints().All(x=>x.Seed==Seed&&x.Generation==71),"saved generation/seed binding");

            var ownersBefore=OwnerState(authority,config);
            var registryBefore=JsonConvert.SerializeObject(RegistryOwnership(authority,unitId),Formatting.None);
            var unitBefore=JsonConvert.SerializeObject(UnitTacticalState(authority,unitId),Formatting.None);

            var report=new{Tick=tick,CheckpointSha256=Hashes[2],Owner=OwnerId,UnitId=unitId,
                RegistryOwnership=JsonConvert.DeserializeObject(registryBefore),UnitTacticalState=JsonConvert.DeserializeObject(unitBefore)};
            TestContext.WriteLine(JsonConvert.SerializeObject(report,Formatting.Indented));

            Assert.AreEqual(ownersBefore,OwnerState(authority,config),"read projection preserves owner state");
            Assert.AreEqual(registryBefore,JsonConvert.SerializeObject(RegistryOwnership(authority,unitId),Formatting.None),"read projection preserves registry state");
            Assert.AreEqual(unitBefore,JsonConvert.SerializeObject(UnitTacticalState(authority,unitId),Formatting.None),"read projection preserves unit order and queue state");
        }

        [Test]
        public void Safe7109_ActiveDefenseAdoptsOnlyAnActuallySafeFreeReinforcement()
        {
            const int tick=4800,nextTick=4801,unitId=67;const long armyId=10;
            var root=Root();var archive=File.ReadAllBytes(Path.Combine(root.FullName,Archive));
            Assert.AreEqual(ArchiveHash,Sha256(archive),"pinned Natural source archive");
            var inputPath=Path.Combine(root.FullName,".local/o6-causal-query-7109-replay/7109-checkpoint-4800.world");
            Assert.True(File.Exists(inputPath),"extract exact archive member first: "+inputPath);
            var input=File.ReadAllBytes(inputPath);Assert.AreEqual(Hashes[2],Sha256(input),"pinned tick-4800 checkpoint");
            var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,Seed,roster);
            var authority=PlayableAuthorityTick.RestoreBytes(input,config,AiProfile.Initial);
            Assert.AreEqual(tick,authority.Tick,"restore exact saved owner-decision source");
            Assert.IsTrue(authority.CaptureDiagnosticCheckpoints().All(x=>x.Seed==Seed&&x.Generation==71),"saved seed/generation binding");
            var owner=authority.Scheduler.Owners.Single(x=>x.OwnerId==OwnerId);
            var initialArmy=owner.ArmyState.Armies.Single(x=>x.Id==armyId);
            Assert.AreEqual(AiDefensePlanner.Policy,initialArmy.TacticalOwner);Assert.AreEqual(AiArmyRole.MobileDefense,initialArmy.Role);
            CollectionAssert.AreEqual(new[]{54,60},initialArmy.Members);Assert.IsEmpty(initialArmy.Reinforcements);Assert.AreEqual(3,initialArmy.MinimumViableForce);
            var initialOwnership=RegistryOwnership(authority,unitId);
            Assert.IsTrue(((IEnumerable)initialOwnership).Cast<object>().All(x=>Convert.ToInt64(Property(x,"ArmyFor"))==0),"unit 67 starts unclaimed by every owner");
            var initialUnit=UnitTacticalState(authority,unitId);
            Assert.IsNull(Property(initialUnit,"CurrentOrder"));Assert.IsNull(Property(initialUnit,"LastOrder"));

            // The normal cadence is evaluated for the first owner decision after the saved tick.
            bool cadenceRequestsArmyRoutes=owner.NeedsArmyRoutes(nextTick);
            if(!cadenceRequestsArmyRoutes)Assert.Inconclusive("Inconclusive: restored owner cadence does not request the next normal army-route decision.");
            PlayableAiObservation decisionObservation=null;AiArmyRegistryState decisionArmyState=null;PlayableAiAction[] decisionPending=null;
            ClearHooks();PlayableAiOwnerLoop.CurrentOwnerDecisionOwnerFilterForTests=OwnerId;
            PlayableAiOwnerLoop.CurrentOwnerDecisionReadbackForTests=(ownerId,observedTick,observation,armies,pending)=>
            {if(ownerId==OwnerId&&observedTick==nextTick){decisionObservation=observation;decisionArmyState=armies;decisionPending=pending;}};
            try{using(var host=new UnityHostRouteService())while(authority.Tick<nextTick)Advance(authority,host);}
            finally{ClearHooks();}
            Assert.AreEqual(nextTick,authority.Tick,"one ordinary authority tick, no injected action or world state");
            owner=authority.Scheduler.Owners.Single(x=>x.OwnerId==OwnerId);
            if(owner.Checkpoint.DecisionTick!=nextTick)Assert.Inconclusive("Inconclusive: the restored owner did not reach its first normal decision at the expected cadence tick.");
            if(authority.Latest.Outcome!=PlayableMatchOutcome.Playing)Assert.Inconclusive("Inconclusive: the actual match ended before the active-defense decision could be evaluated.");
            if(decisionObservation==null||decisionArmyState==null||decisionPending==null)
                Assert.Inconclusive("Inconclusive: the exact pre-proposal owner observation was not captured at the normal decision.");
            var observation=decisionObservation;
            var threats=AiDefensePlanner.Threats(observation,profile);
            var army=decisionArmyState.Armies.SingleOrDefault(x=>x.Id==armyId&&x.TacticalOwner==AiDefensePlanner.Policy&&x.Phase!=AiArmyPhase.Disbanded);
            if(threats.Length==0)Assert.Inconclusive("Inconclusive: the saved threat did not persist to the actual next decision.");
            if(army==null)Assert.Inconclusive("Inconclusive: Army10 no longer owns an active Defense mission at the next decision.");

            var candidate=observation.Entities.SingleOrDefault(x=>x.Id==unitId&&x.Owner==observation.Owner&&x.Health>0);
            bool registryBoundToDefense=decisionArmyState.Armies.Any(a=>a.Id==armyId&&a.TacticalOwner==AiDefensePlanner.Policy&&a.Members.Contains(unitId)&&a.Reinforcements.Contains(unitId))&&
                authority.Scheduler.Owners.Where(x=>x.OwnerId!=OwnerId).All(x=>x.ArmyState.Armies.All(a=>!a.Members.Contains(unitId)&&!a.Reinforcements.Contains(unitId)));
            bool pendingFree=decisionPending.All(a=>!a.EntityIds.Contains(unitId))&&authority.Scheduler.Owners.Where(x=>x.OwnerId!=OwnerId).All(x=>
                !((IEnumerable)Field(x,"pendingItems")).Cast<object>().Any(p=>((PlayableAiAction)Field(p,"Action")).EntityIds.Contains(unitId)));
            if(candidate==null||!pendingFree||candidate.CurrentOrder!=null||candidate.NavigationOutcome!=NavigationOutcome.Idle||candidate.Queue!=null||AiRosterCatalog.Initial.For(candidate.Kind).lineWeight<=0)
                Assert.Inconclusive("Inconclusive: unit 67 is not a live, idle, unclaimed line-unit candidate at the next decision.");

            var core=army.Members.Where(id=>!army.Reinforcements.Contains(id)).ToHashSet();
            var safeAnchors=observation.RouteProofs.Where(r=>r.UnitId==unitId&&r.Owner==observation.Owner&&r.Generation==observation.Generation&&
                    r.Tick==observation.Tick&&r.Kind==PlayableRouteTargetKind.FriendlyAnchor&&core.Contains(r.TargetId))
                .Where(r=>AiTacticalExecutor.SafeRoute(observation,r,profile)).ToArray();
            TestContext.WriteLine(JsonConvert.SerializeObject(new{RestoredTick=tick,DecisionTick=authority.Tick,DecisionRecord=owner.Checkpoint.DecisionTick,
                Threats=threats.Select(x=>x.Id).ToArray(),Army=army==null?null:new{army.Id,army.TacticalOwner,army.Role,Members=army.Members,Reinforcements=army.Reinforcements,army.MinimumViableForce},
                Candidate=candidate==null?null:new{candidate.Id,candidate.Owner,candidate.Kind,candidate.Health,candidate.CurrentOrder,candidate.NavigationOutcome,Queue=candidate.Queue},
                RegistryBoundToDefense=registryBoundToDefense,PendingFree=pendingFree,SafeAnchors=safeAnchors},Formatting.Indented));
            if(safeAnchors.Length==0)
                Assert.Inconclusive("Inconclusive: no current safe FriendlyAnchor proof for unit 67 to Army10's core; proofs="+observation.RouteProofs.Count+
                    ", unit67="+string.Join(";",observation.RouteProofs.Where(r=>r.UnitId==unitId).Select(r=>r.Kind+":"+r.TargetId+":"+r.Target+"/"+r.Goal))+ ", army="+armyId+
                    ", defenseRoutes="+AiTacticalExecutor.RouteRequests(observation.Entities,observation.Buildings,decisionArmyState,observation.Owner,observation.Generation,observation.Tick,profile).Count(r=>r.UnitId==unitId)+".");
            Assert.IsTrue(registryBoundToDefense,"unit 67 with a current safe route must be registered as a Defense reinforcement before proposals");

            // Once all live preconditions exist, the authoritative decision must attach the unit to
            // this same Defense army. An independent army/front is never a valid substitute.
            owner=authority.Scheduler.Owners.Single(x=>x.OwnerId==OwnerId);
            var assignment=owner.ArmyState.Armies.SingleOrDefault(a=>a.Members.Contains(unitId)||a.Reinforcements.Contains(unitId));
            Assert.NotNull(assignment,"unit 67 must be assigned to the existing Defense army");Assert.AreEqual(armyId,assignment.Id);
            var adopted=owner.ArmyState.Armies.Single(a=>a.Id==armyId);
            CollectionAssert.Contains(adopted.Members,unitId);CollectionAssert.Contains(adopted.Reinforcements,unitId);
            Assert.AreEqual(armyId,authority.Scheduler.Owners.Single(x=>x.OwnerId==OwnerId).ArmyState.Armies.Single(a=>a.Members.Contains(unitId)).Id);
            Assert.AreEqual(initialArmy.MinimumViableForce,adopted.MinimumViableForce,"preserve the saved C5 minimum");
            var ownerPending=((IEnumerable)Field(owner,"pendingItems")).Cast<object>().Where(p=>
                ((string)Field(p,"PolicyName"))=="defense-assembly"&&((PlayableAiAction)Field(p,"Action")).EntityIds.Contains(unitId)).ToArray();
            Assert.AreEqual(1,ownerPending.Length,"one current Defense-owned assembly action");
            var action=(PlayableAiAction)Field(ownerPending[0],"Action");
            Assert.IsTrue(action.Kind==PlayableCommandKind.Move||action.Kind==PlayableCommandKind.Follow,"assembly stays on the safe anchor; it cannot launch an independent attack");
            if(action.Kind==PlayableCommandKind.Follow)CollectionAssert.Contains(core,action.TargetId);
            if(action.Kind==PlayableCommandKind.Move)Assert.IsTrue(safeAnchors.Any(r=>r.Goal.Equals(action.Target)),"move uses one current native safe-anchor goal");
        }
    }
}

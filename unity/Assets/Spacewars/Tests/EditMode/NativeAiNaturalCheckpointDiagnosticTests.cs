using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using NUnit.Framework;
using Spacewars.Headless;
using Spacewars.Runtime;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Tests.EditMode
{
    // Reads the saved Safe/7109 authority states through the production C# wire only.
    // No tick, host, policy Observe/Review, reconciliation, or world-byte capture is permitted.
    public sealed class NativeAiNaturalCheckpointDiagnosticTests
    {
        private const BindingFlags InstanceFlags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const BindingFlags StaticFlags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private const string OwnerId="ai-0";
        private static readonly int[] Ticks={4800,5100,8400,9000,9300};
        private static readonly string[] Hashes={
            "f71bbf62a0274d21f08065b966cd59c720aeca91e47eb065c949350ef8a73a48",
            "4c3a222dee28f65d19bba50016b7924563818d06d53bebd67c41010f52818a8a",
            "f20732cd7f6359a256923f78ce3614f98a35b7a99372a1b1f6d1ce49e4cd1ca6",
            "e4b63add33ce82fdbf4ff8ef99bdad3864ec96af4ae272071b4a15e885377a0a",
            "450c5d726cc9e04ae58bd77a5cc5770b51f49dd7946e5826f944ec0ea295f8d7"
        };

        private static DirectoryInfo Root()
        {
            var d=new DirectoryInfo(Directory.GetCurrentDirectory());
            while(d!=null&&!File.Exists(Path.Combine(d.FullName,"workflow/project.json")))d=d.Parent;
            Assert.NotNull(d,"workspace root");return d;
        }
        private static object Field(object instance,string name)
        {
            var f=instance.GetType().GetField(name,InstanceFlags);
            Assert.NotNull(f,instance.GetType().FullName+"."+name);return f.GetValue(instance);
        }
        private static object Property(object instance,string name)
        {
            var p=instance.GetType().GetProperty(name,InstanceFlags);
            Assert.NotNull(p,instance.GetType().FullName+"."+name);return p.GetValue(instance,null);
        }
        private static object Invoke(object instance,string name,params object[] args)
        {
            var m=instance.GetType().GetMethod(name,InstanceFlags);
            Assert.NotNull(m,instance.GetType().FullName+"."+name);return m.Invoke(instance,args);
        }
        private static string Sha256(byte[] bytes)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
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
            if(value is IEnumerable enumerable)
                return enumerable.Cast<object>().Select(x=>Describe(x,depth+1)).ToArray();
            var fields=type.GetFields(InstanceFlags|BindingFlags.DeclaredOnly).Where(f=>!f.IsStatic).OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray();
            if(fields.Length>0)return fields.ToDictionary(f=>f.Name,f=>Describe(f.GetValue(value),depth+1),StringComparer.Ordinal);
            var properties=type.GetProperties(InstanceFlags).Where(p=>p.CanRead&&p.GetIndexParameters().Length==0).OrderBy(p=>p.Name,StringComparer.Ordinal);
            return properties.ToDictionary(p=>p.Name,p=>Describe(p.GetValue(value,null),depth+1),StringComparer.Ordinal);
        }
        private static object ReadMemo(Type type,object arbiter)
        {
            var load=type.GetMethod("Load",StaticFlags);
            Assert.NotNull(load,type.FullName+".Load");
            return Describe(load.Invoke(null,new[]{arbiter}));
        }
        private static object PendingProjection(object owner)
        {
            var pending=(IEnumerable)Field(owner,"pendingItems");
            return pending.Cast<object>().Select(p=>{
                var intent=Field(p,"Intent");
                return new{
                    ObservationIdentity=Field(p,"ObservationIdentity"),ObservationTick=Field(p,"ObservationTick"),
                    SnapshotSequence=Field(p,"SnapshotSequence"),Action=Field(p,"Action"),DueTick=Field(p,"DueTick"),
                    HumanSequence=Field(p,"HumanSequence"),Ordinal=Field(p,"Ordinal"),LocalActionId=Field(p,"LocalActionId"),
                    PolicyName=Field(p,"PolicyName"),Identity=Field(p,"Identity"),Scout=Field(p,"Scout"),
                    Intent=new{Id=Property(intent,"Id"),Policy=Property(intent,"Policy"),ExpiresTick=Property(intent,"ExpiresTick"),
                        Credits=Property(intent,"Credits"),Population=Property(intent,"Population"),Priority=Property(intent,"Priority"),
                        ReservationId=Property(intent,"ReservationId"),Conditions=Property(intent,"Conditions"),Reason=Property(intent,"Reason")}
                };
            }).ToArray();
        }
        private static PlayableSnapshot WorldSnapshot(object domain,int seed)
        {
            var metrics=new PlayableRuntimeMetrics(0,0,0,0,0);
            return (PlayableSnapshot)Invoke(domain,"Snapshot",0L,RuntimeStatus.Running,false,metrics,null,seed);
        }
        private static bool Eliminated(object domain,PlayableOwner owner)
        {
            var set=(IEnumerable)Field(domain,"eliminated");
            return set.Cast<object>().Any(x=>Equals(x,owner));
        }
        private static int[] EliminatedOwners(object domain)=>Enum.GetValues(typeof(PlayableOwner)).Cast<PlayableOwner>()
            .Where(owner=>Eliminated(domain,owner)).Select(owner=>(int)owner).ToArray();
        private static object WorldProjection(PlayableSnapshot world,int[] eliminatedOwners)
        {
            return new{
                world.Tick,world.Outcome,world.Credits,world.ExactCredits,EliminatedOwners=eliminatedOwners,
                Buildings=world.Buildings.Select(b=>new{b.Id,b.Owner,b.Kind,b.SiteId,b.SlotId,b.ParentId,b.Phase,b.Health,b.BlockedReason,
                    Selling=b.PrivateState?.Lifecycle?.Selling,Orders=b.PrivateState?.Orders,Research=b.PrivateState?.Research}).ToArray(),
                Units=world.Entities.Select(e=>new{e.Id,e.Owner,e.Kind,e.Health,e.Position,e.TargetId}).ToArray(),
                Sites=world.Sites.Select(s=>new{Site=s.Site.Id,Kind=s.Site.Kind,Owner=s.Owner,Claimant=s.Claimant,s.CenterId,s.Contested,s.Progress,
                    Slots=s.Site.Slots.Select(slot=>new{slot.Id,slot.Position}).ToArray()}).ToArray(),
                Research=world.OwnerResearch.Select(r=>new{r.Kind,r.Complete,r.Progress}).ToArray(),
                Impacts=world.Impacts.Select(i=>new{i.Id,i.Owner,i.Tick,i.Radius}).ToArray(),
                IncomeEvents=world.IncomeEvents.Select(e=>new{e.Tick,e.BuildingId,e.Owner,e.Kind,e.Amount}).ToArray()
            };
        }
        private static object BudgetProjection(AiBudgetState b)=>new{
            b.OwnerId,b.Generation,b.Liquid,b.SafetyReserve,b.PaidTotal,b.RepairPaidTotal,b.ReservationLifetimeTicks,
            Reserved=b.Reserved.ToArray(),Unpaid=b.Unpaid.ToArray(),Paid=b.Paid.ToArray()
        };
        private static object OwnerPrivateProjection(object owner)
        {
            var arbiter=Field(owner,"arbiter");
            var runtimeAssembly=typeof(PlayableAuthorityTick).Assembly;
            var techType=runtimeAssembly.GetType("Spacewars.Runtime.AiTechnologyFunding",true);
            var financeType=runtimeAssembly.GetType("Spacewars.Runtime.AiExpansionFinancial",true);
            var rejections=(IEnumerable)Property(arbiter,"Rejections");
            return new{
                OwnerId=Property(owner,"OwnerId"),TechnologyMemo=ReadMemo(techType,arbiter),FinanceMemo=ReadMemo(financeType,arbiter),
                Rejections=rejections.Cast<object>().Select(x=>new{Key=Property(x,"Key"),Value=Property(x,"Value")}).ToArray(),
                Pending=PendingProjection(owner)
            };
        }
        private static object ReadProjection(PlayableAuthorityTick authority,PlayableProfile profile,OfflineMatchConfiguration config)
        {
            var domain=Field(authority,"domain");var scheduler=Field(authority,"scheduler");
            var owners=((IEnumerable)Property(scheduler,"Owners")).Cast<object>().ToArray();
            var budgets=authority.CaptureDiagnosticBudgets().Select(BudgetProjection).ToArray();
            var checkpoints=authority.CaptureDiagnosticCheckpoints();
            var world=WorldSnapshot(domain,config.Seed);
            var tactical=(PlayableTacticalObservationState)Invoke(domain,"CaptureTacticalObservationState");
            return new{
                World=WorldProjection(world,EliminatedOwners(domain)),
                MatchFacts=MatchFactsProjection(domain),
                Damage=tactical.Damage.Select(d=>new{d.CenterId,d.Owner,d.AttackerId,d.Generation,d.Tick,d.Damage}).ToArray(),
                TacticalOrders=tactical.Orders.Select(o=>new{o.UnitId,o.Owner,o.Generation,o.CommandSequence,o.IssuedTick,o.Kind,o.TargetId,o.Destination}).ToArray(),
                Budgets=budgets,
                OwnerHistory=checkpoints.Select(c=>new{c.OwnerId,c.DecisionTick,c.LastPolicy,c.LastActionKind,
                    Records=c.Records.Select(r=>new{r.Policy,r.Kind,r.ReceiptIdentity,r.ActionId,r.DueTick,r.CommandSequence,r.ApplicationTick,r.Status,r.RuntimeStatus,r.Message}).ToArray()}).ToArray(),
                Private=owners.Select(OwnerPrivateProjection).ToArray(),
                Binding=new{Source=PlayableAiOpeningComposition.SourceIdentity,Profile=profile.ProfileId,Revision=profile.Revision,Seed=config.Seed,Tick=world.Tick}
            };
        }
        private static object MatchFactsProjection(object domain)
        {
            var facts=(IEnumerable)Field(domain,"matchFacts");
            return facts.Cast<object>().Select(f=>new{
                Tick=Property(f,"Tick"),Sequence=Property(f,"Sequence"),Seconds=Property(f,"Seconds"),
                Owner=Property(f,"Owner"),Kind=Property(f,"Kind"),Value=Property(f,"Value"),
                EntityKind=Property(f,"EntityKind"),Name=Property(f,"Name")
            }).ToArray();
        }
        private static object CurrentScienceTargetFacts(PlayableAuthorityTick authority,PlayableProfile profile,OfflineMatchConfiguration config)
        {
            var domain=Field(authority,"domain");var scheduler=Field(authority,"scheduler");
            var owners=((IEnumerable)Property(scheduler,"Owners")).Cast<object>().ToArray();
            var ai=owners.Single(x=>Equals(Property(x,"OwnerId"),OwnerId));
            var arbiter=Field(ai,"arbiter");
            var runtimeAssembly=typeof(PlayableAuthorityTick).Assembly;
            var techType=runtimeAssembly.GetType("Spacewars.Runtime.AiTechnologyFunding",true);
            var load=techType.GetMethod("Load",StaticFlags);Assert.NotNull(load);
            var memo=load.Invoke(null,new[]{arbiter});
            if(memo==null)return new{Status="no persisted technology memo; no target/action synthesized"};
            var phase=Field(memo,"Phase").ToString();int site=(int)Field(memo,"Site"),slot=(int)Field(memo,"Slot"),parent=(int)Field(memo,"Parent");
            var world=WorldSnapshot(domain,config.Seed);
            var siteView=world.Sites.FirstOrDefault(s=>s.Site.Id==site);
            var budget=authority.CaptureDiagnosticBudget(OwnerId);
            var matchingEntries=budget.Reserved.Concat(budget.Unpaid).Concat(budget.Paid)
                .Where(e=>e.Action.Kind==PlayableCommandKind.BuildAt&&e.Action.BuildingKind==PlayableBuildingKind.ScientificCenter&&
                    e.Action.SiteId==site&&e.Action.SlotId==slot&&e.Action.ParentId==parent)
                .Select(e=>new{Category=budget.Reserved.Contains(e)?"reserved":budget.Unpaid.Contains(e)?"unpaid":"paid",Entry=e}).ToArray();
            var occupants=world.Buildings.Where(b=>b.SiteId==site&&b.SlotId==slot).ToArray();
            var parentBuilding=world.Buildings.FirstOrDefault(b=>b.Id==parent);
            int scienceCost=TerritoryRules.Cost(profile,PlayableBuildingKind.ScientificCenter);
            return new{
                Tick=world.Tick,Phase=phase,Site=site,Slot=slot,MemoParentId=parent,PersistedMemo=Describe(memo),
                ExactLedgerActions=matchingEntries,
                PhysicalSite=siteView==null?null:new{Id=siteView.Site.Id,Kind=siteView.Site.Kind,Owner=siteView.Owner,Claimant=siteView.Claimant,
                    siteView.CenterId,siteView.Contested,siteView.Progress,siteView.Ready,SlotExists=siteView.Site.Slots.Any(s=>s.Id==slot)},
                SlotOccupants=occupants.Select(b=>new{b.Id,b.Owner,b.Kind,b.Phase,b.Health,b.ParentId,b.BlockedReason,
                    Selling=b.PrivateState?.Lifecycle?.Selling}).ToArray(),
                ParentWorldFact=parentBuilding==null?null:(object)new{parentBuilding.Id,parentBuilding.Owner,parentBuilding.Kind,parentBuilding.Health,parentBuilding.Phase,
                    Selling=parentBuilding.PrivateState?.Lifecycle?.Selling},
                Bank=world.Credits,ScienceCenterCost=scienceCost,CreditsAtLeastCost=world.Credits>=scienceCost,
                Label="persisted target and current omniscient detached world facts only; not an actual participant-policy admission, historical candidate, or cause"
            };
        }

        [Test]
        public void ReadSavedSafe7109Checkpoints()
        {
            var root=Root();var profile=PlayableProfile.Create(PlayableProfile.Default.CopyData(),new AiTestMap());
            var roster=Enumerable.Range(0,8).Select(i=>new OfflineParticipant("ai-"+i,i+1,i+1,OfflineControl.Ai)).ToArray();
            var config=((AiTestMap)profile.AuthoredMap).Configuration(profile,7109,roster);
            var inputRoot=Path.Combine(root.FullName,".local/o6-causal-query-7109");
            var outputRoot=Path.Combine(inputRoot,"report");Directory.CreateDirectory(outputRoot);
            var output=new List<object>();
            for(int i=0;i<Ticks.Length;i++)
            {
                string path=Path.Combine(inputRoot,"7109-checkpoint-"+Ticks[i]+".world");
                Assert.True(File.Exists(path),"missing extracted checkpoint "+path);
                var bytes=File.ReadAllBytes(path);Assert.AreEqual(Hashes[i],Sha256(bytes),"checkpoint hash at tick "+Ticks[i]);
                var authority=PlayableAuthorityTick.RestoreBytes(bytes,config,AiProfile.Initial);
                Assert.AreEqual(Ticks[i],authority.Tick,"restored checkpoint tick");
                string before=JsonConvert.SerializeObject(ReadProjection(authority,profile,config),Formatting.None);
                var scienceFacts=CurrentScienceTargetFacts(authority,profile,config);
                string after=JsonConvert.SerializeObject(ReadProjection(authority,profile,config),Formatting.None);
                Assert.AreEqual(before,after,"scoped immutable world/budget/checkpoint/memo/pending/receipt projections at tick "+Ticks[i]);
                output.Add(new{Schema="q7-safe7109-checkpoint-query-v1",Source="038e288c9c5e2601c5f36cf09aa08c2b2f88b7c3",
                    Tick=Ticks[i],CheckpointSha256=Hashes[i],Scope="selected read projections only; not whole-authority byte equality",
                    MatchFactSemantics=new{
                        Income="Owner is the income recipient",
                        BuildingCompleted="Owner is the building owner; EntityKind identifies the built kind",
                        BuildingDestroyed="Owner is the credited hostile attacker; EntityKind identifies victim kind; victim owner and entity id are not stored",
                        UnitDestroyed="Owner is the credited hostile attacker; EntityKind identifies victim kind; victim owner and entity id are not stored",
                        Limitation="A generic destruction fact cannot identify which saved Mine, Refinery, HQ, or unit was destroyed without separate matching evidence"
                    },World=JsonConvert.DeserializeObject(before),ScienceTargetFacts=scienceFacts});
            }
            File.WriteAllText(Path.Combine(outputRoot,"checkpoint-query.json"),JsonConvert.SerializeObject(output,Formatting.Indented));
        }
    }
}
